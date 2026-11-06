using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Vba;

/// <summary>
/// A VBA project read as data: its modules with their source, references, project information, protection and signature streams
/// ([MS-OVBA]). Reading decompresses source text and nothing else; no code is compiled, interpreted or run, and the p-code performance
/// cache is never decoded.
/// </summary>
/// <remarks>
/// The project lives in <c>vbaProject.bin</c> of an OOXML package, in the <c>Macros</c> storage of a Word 97–2003 file, in the
/// <c>_VBA_PROJECT_CUR</c> storage of an Excel 97–2003 file, and in a compressed storage in the <c>PowerPoint Document</c> stream of a
/// PowerPoint 97–2003 file. <see cref="Open(string, OpenContext, CancellationToken)"/> finds it in any of them. Damage is reported as <c>vba.*</c> defects
/// to the context's sink and reading continues with what is intact.
/// </remarks>
public sealed partial class VbaProject
{
    private readonly List<VbaModule> _modules = [];
    private readonly List<VbaReference> _references = [];
    private readonly List<string> _designerStorages = [];
    private readonly List<string> _unlisted = [];

    internal VbaProject(string container)
    {
        Container = container;
    }

    /// <summary>Gets where the project was found: the part name of <c>vbaProject.bin</c> (<c>/word/vbaProject.bin</c>), or the storage (<c>/Macros</c>, <c>/_VBA_PROJECT_CUR</c>, <c>/PowerPoint Document (VbaProjectStg)</c>).</summary>
    public string Container { get; }

    /// <summary>Gets the project's name (PROJECTNAME), or null when the <c>dir</c> stream could not be read that far.</summary>
    public string? Name { get; internal set; }

    /// <summary>Gets the Windows code page of the project's byte strings and source text (PROJECTCODEPAGE); 1252 when the record is missing.</summary>
    public int CodePage { get; internal set; } = 1252;

    /// <summary>Gets the platform the project was saved on (PROJECTSYSKIND).</summary>
    public VbaSysKind SysKind { get; internal set; } = VbaSysKind.Unknown;

    /// <summary>Gets the project's LCID (PROJECTLCID), or 0.</summary>
    public int Lcid { get; internal set; }

    /// <summary>Gets the compat version (PROJECTCOMPATVERSION), or null when the optional record is absent.</summary>
    public uint? CompatVersion { get; internal set; }

    /// <summary>Gets the project's description (PROJECTDOCSTRING), or null.</summary>
    public string? Description { get; internal set; }

    /// <summary>Gets the help file path (PROJECTHELPFILEPATH), or null.</summary>
    public string? HelpFile { get; internal set; }

    /// <summary>Gets the compilation constants (PROJECTCONSTANTS), for example <c>Win64 = -1 : VBA7 = -1</c>, or null.</summary>
    public string? Constants { get; internal set; }

    /// <summary>Gets the project's major version (PROJECTVERSION).</summary>
    public uint VersionMajor { get; internal set; }

    /// <summary>Gets the project's minor version (PROJECTVERSION).</summary>
    public ushort VersionMinor { get; internal set; }

    /// <summary>Gets the modules in the order of the <c>dir</c> stream.</summary>
    public IReadOnlyList<VbaModule> Modules => _modules;

    /// <summary>Gets the references to type libraries, controls and other projects, in order.</summary>
    public IReadOnlyList<VbaReference> References => _references;

    /// <summary>Gets the names of the designer storages (user forms) under the project root. They are kept opaque: the controls and layout are not decoded.</summary>
    public IReadOnlyList<string> DesignerStorages => _designerStorages;

    /// <summary>Gets the streams of the <c>VBA</c> storage that the <c>dir</c> stream does not list (other than <c>dir</c>, <c>_VBA_PROJECT</c> and the <c>__SRP_</c> caches): a module the editor would not show.</summary>
    public IReadOnlyList<string> UnlistedStreams => _unlisted;

    /// <summary>Gets the text of the <c>PROJECT</c> stream (project items, protection values, host extenders, window states) in the project code page, or null when the stream is missing.</summary>
    public string? ProjectText { get; internal set; }

    /// <summary>Gets the class identifier written as <c>ID=</c> in the <c>PROJECT</c> stream, or null.</summary>
    public string? ProjectId { get; internal set; }

    /// <summary>Gets the protection the project reports.</summary>
    public VbaProtection Protection { get; internal set; } = VbaProtection.None;

    /// <summary>Gets a value indicating whether the project is locked for viewing (<see cref="VbaProtection.IsLocked"/>). The lock is reported, never opened.</summary>
    public bool IsLocked => Protection.IsLocked;

    /// <summary>Gets the signature streams that accompany the project (found, not verified).</summary>
    public VbaSignatureKinds Signatures { get; internal set; }

    /// <summary>Gets a value indicating whether the project carries a signature of any kind. Verification is not done here.</summary>
    public bool IsSigned => Signatures != VbaSignatureKinds.None;

    /// <summary>Gets the version in the <c>_VBA_PROJECT</c> stream: the VBA version that saved the performance caches. 0xFFFF means the caches are not to be trusted; null when the stream is missing or shorter than seven bytes.</summary>
    public ushort? PerformanceCacheVersion { get; internal set; }

    /// <summary>Gets a value indicating whether the project holds compiled p-code: a module stream with bytes before its source, or a <c>_VBA_PROJECT</c> stream with a performance cache.</summary>
    public bool HasPCode { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the source text and the compiled p-code may disagree (the "VBA stomping" case). The p-code is live
    /// (the cache version is not 0xFFFF) while a module that carries it has no readable source, or the project's <c>dir</c> stream and module
    /// streams do not line up. The p-code itself is never decoded, so a source that was replaced by other readable source is not detectable here.
    /// </summary>
    public bool SourceAndPCodeMayDisagree { get; internal set; }

    /// <summary>Finds a module by name, ignoring case.</summary>
    /// <param name="name">The module name.</param>
    /// <returns>The module, or null.</returns>
    public VbaModule? FindModule(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (VbaModule m in _modules)
        {
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>Returns the source of every module, keyed by module name (in project order; a later module of the same name replaces an earlier one).</summary>
    /// <returns>One string per module.</returns>
    public IReadOnlyDictionary<string, string> ExtractSources()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (VbaModule m in _modules)
        {
            map[m.Name] = m.Source;
        }

        return map;
    }

    /// <summary>Writes every module as a file named by <see cref="VbaModule.FileName"/> (<c>.bas</c>, <c>.cls</c>, <c>.frm</c>) in a directory, in the project code page.</summary>
    /// <param name="directory">The directory; it is created when missing. Existing files of the same names are overwritten.</param>
    /// <returns>The paths written.</returns>
    /// <exception cref="IOException">A file cannot be written.</exception>
    public IReadOnlyList<string> ExportModules(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        Encoding enc = VbaCodePages.Get(CodePage);
        var written = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VbaModule m in _modules)
        {
            string file = m.FileName;
            for (int n = 2; !used.Add(file); n++)
            {
                file = Path.GetFileNameWithoutExtension(m.FileName) + "_" + n.ToString(CultureInfo.InvariantCulture) + m.FileExtension;
            }

            string path = Path.Combine(directory, file);
            File.WriteAllBytes(path, enc.GetBytes(m.ExportText()));
            written.Add(path);
        }

        return written;
    }

    internal void Add(VbaModule m) => _modules.Add(m);

    internal void Add(VbaReference r) => _references.Add(r);

    internal void AddDesigner(string name) => _designerStorages.Add(name);

    internal void AddUnlisted(string name) => _unlisted.Add(name);

    /// <inheritdoc />
    public override string ToString() => (Name ?? "(unnamed)") + " in " + Container + ": " + _modules.Count.ToString(CultureInfo.InvariantCulture) + " modules";
}

/// <summary>Code pages for VBA text, through the BCL.</summary>
internal static class VbaCodePages
{
    internal static Encoding Get(int codePage) => OleCodePages.TryGet(codePage, out Encoding? e) ? e : Encoding.Latin1;

    internal static bool Supported(int codePage) => OleCodePages.TryGet(codePage, out _);
}
