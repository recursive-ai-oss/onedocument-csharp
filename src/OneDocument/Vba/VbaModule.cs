using System.Text;

namespace RecursiveAI.OneDocument.Vba;

/// <summary>One module of a VBA project: its source text and the facts the <c>dir</c> stream records about it. Nothing here runs the code.</summary>
public sealed class VbaModule
{
    private readonly Encoding _encoding;
    private string? _source;

    internal VbaModule(string name, string streamName, VbaModuleKind kind, Encoding encoding)
    {
        Name = name;
        StreamName = streamName;
        Kind = kind;
        _encoding = encoding;
    }

    /// <summary>Gets the module's name (MODULENAMEUNICODE when present, else MODULENAME in the project code page).</summary>
    public string Name { get; }

    /// <summary>Gets the name of the module's stream in the <c>VBA</c> storage (MODULESTREAMNAME), which may differ from <see cref="Name"/>.</summary>
    public string StreamName { get; }

    /// <summary>Gets what kind of module it is. A module the <c>PROJECT</c> stream does not list is <see cref="VbaModuleKind.Class"/> when its type record says 0x0022 and <see cref="VbaModuleKind.Standard"/> otherwise.</summary>
    public VbaModuleKind Kind { get; internal set; }

    /// <summary>Gets a value indicating whether the module is marked read-only (MODULEREADONLY).</summary>
    public bool IsReadOnly { get; internal set; }

    /// <summary>Gets a value indicating whether the module is marked private (MODULEPRIVATE).</summary>
    public bool IsPrivate { get; internal set; }

    /// <summary>Gets the module's description (MODULEDOCSTRING), or null.</summary>
    public string? DocString { get; internal set; }

    /// <summary>Gets the offset in the module stream where the compressed source starts (MODULEOFFSET); the bytes before it are the p-code performance cache.</summary>
    public long TextOffset { get; internal set; }

    /// <summary>Gets the length in bytes of the module stream, or -1 when the stream is missing.</summary>
    public long StreamLength { get; internal set; } = -1;

    /// <summary>Gets a value indicating whether the module stream carries a performance cache (compiled p-code) before the source.</summary>
    public bool HasPerformanceCache => TextOffset > 0 && StreamLength > 0;

    /// <summary>Gets a value indicating whether source text was read: the stream exists, the offset is inside it and the source decompressed without damage.</summary>
    public bool SourceAvailable { get; internal set; }

    /// <summary>Gets a value indicating whether the source container was damaged; <see cref="Source"/> then holds what decoded before the damage.</summary>
    public bool SourceDamaged { get; internal set; }

    /// <summary>Gets the module's source bytes as stored (the project code page), after decompression. Empty when <see cref="SourceAvailable"/> is false and nothing decoded.</summary>
    public ReadOnlyMemory<byte> SourceBytes { get; internal set; }

    /// <summary>Gets the name of the designer storage under the project root (user forms), or null.</summary>
    public string? DesignerStorage { get; internal set; }

    /// <summary>Gets the module's source text, decoded with the project's code page, line endings as stored (CR LF). Empty when no source was read.</summary>
    public string Source => _source ??= _encoding.GetString(SourceBytes.Span);

    /// <summary>Gets the file extension Office gives an exported module: <c>.bas</c> for procedural modules, <c>.frm</c> for user forms, <c>.cls</c> for classes and document modules.</summary>
    public string FileExtension => Kind switch
    {
        VbaModuleKind.Standard => ".bas",
        VbaModuleKind.Designer => ".frm",
        _ => ".cls",
    };

    /// <summary>Gets <see cref="Name"/> with <see cref="FileExtension"/>, with characters a file name cannot hold replaced by underscores.</summary>
    public string FileName
    {
        get
        {
            var sb = new StringBuilder(Name.Length + 4);
            foreach (char c in Name)
            {
                sb.Append(char.IsControl(c) || "\\/:*?\"<>|".Contains(c, StringComparison.Ordinal) ? '_' : c);
            }

            return sb.Append(FileExtension).ToString();
        }
    }

    /// <summary>
    /// Builds the text of the file Office writes when a module is exported. The stored source already carries the <c>Attribute</c> lines
    /// (<c>VB_Name</c>, <c>VB_Base</c>, <c>VB_Exposed</c> and so on); the header Office puts before them is added here.
    /// </summary>
    /// <returns>
    /// A <c>.bas</c> file is the source. A class or document module is preceded by <c>VERSION 1.0 CLASS</c> with its <c>BEGIN … END</c> block.
    /// A user form is preceded by <c>VERSION 5.00</c> and an empty form block: the form's controls and layout live in the designer storage,
    /// which is kept opaque and not exported. Line endings are CR LF throughout.
    /// </returns>
    public string ExportText()
    {
        string body = Source;
        if (body.Length > 0 && !body.Contains("Attribute VB_Name", StringComparison.OrdinalIgnoreCase))
        {
            body = "Attribute VB_Name = \"" + Name + "\"\r\n" + body;
        }
        else if (body.Length == 0)
        {
            body = "Attribute VB_Name = \"" + Name + "\"\r\n";
        }

        return Kind switch
        {
            VbaModuleKind.Standard => body,
            VbaModuleKind.Designer => "VERSION 5.00\r\nBegin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} " + Name + "\r\nEnd\r\n" + body,
            _ => "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n" + body,
        };
    }

    /// <inheritdoc />
    public override string ToString() => Kind + " " + Name;
}
