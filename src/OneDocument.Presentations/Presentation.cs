using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;
using P = RecursiveAI.OneDocument.Schema.Presentation;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// A presentation: the semantic hub over a PresentationML package. For OOXML files the hub is a set of views over the markup tree of
/// each part, not a copy: reading goes through <see cref="Markup"/> and the generated views, and an edit changes the tree and nothing
/// else. A presentation opened and saved without edits writes every part byte-identical, and transitions, animations, extension lists
/// and <c>mc:AlternateContent</c> are never touched by the hub.
/// </summary>
/// <remarks>
/// <para>Parts are parsed on first use and only the ones touched are read. <see cref="CommitChanges"/> writes the parts whose markup
/// was edited back into the package; the other parts are never touched, so the package copies them raw.</para>
/// <para>A presentation is not safe for concurrent mutation. After it is loaded, read-only access from several threads is safe as long
/// as nobody edits it.</para>
/// </remarks>
public sealed partial class Presentation : OfficeFile
{
    internal const string OfficeDocumentRelationship = "officeDocument";

    private readonly bool _ownsPackage;
    private readonly Dictionary<string, PresentationPart> _partObjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedSlides = new(StringComparer.Ordinal);
    private MarkupDocument? _main;

    private Presentation(IOpcPackage package, OpenContext context, string mainPartName, bool ownsPackage, OfficeFormat format, string? filePath)
        : base(format, context, filePath)
    {
        Package = package;
        MainPartName = mainPartName;
        _ownsPackage = ownsPackage;
        Parts = new PartStore(package, context);
    }

    /// <summary>Gets the package the presentation sits on.</summary>
    public IOpcPackage Package { get; }

    /// <summary>Gets the limits and defect sink the presentation was opened with. Repairs found while reading any part are reported to <see cref="OpenContext.Defects"/>.</summary>
    public new OpenContext Context => base.Context;

    /// <summary>Gets the part name of the presentation part, for example <c>/ppt/presentation.xml</c>.</summary>
    public string MainPartName { get; }

    internal PartStore Parts { get; }

    /// <summary>
    /// Opens the hub over a package that has a presentation part.
    /// </summary>
    /// <param name="package">An OPC package holding a presentation.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="ownsPackage">True to dispose the package when the presentation is disposed.</param>
    /// <returns>The presentation. Nothing but the package relationships is read yet; the presentation part is parsed on first use.</returns>
    /// <exception cref="OfficeFormatException">The package has no presentation part (<c>pptx.no-main-part</c>). A presentation part that is not well-formed XML raises at its first use instead (see <see cref="MainMarkup"/>).</exception>
    public static Presentation FromPackage(IOpcPackage package, OpenContext? context = null, bool ownsPackage = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        context ??= new OpenContext(package.Limits);
        string? main = FindMainPart(package);
        if (main is null)
        {
            throw new OfficeFormatException(
                DefectKind.Structure,
                "pptx.no-main-part",
                "The package has no presentation part: no officeDocument relationship points at a part that exists.",
                DefectLocation.InPart("/_rels/.rels"));
        }

        return new Presentation(package, context, main, ownsPackage, FormatOf(package, main), package.FilePath);
    }

    private static string? FindMainPart(IOpcPackage package)
    {
        foreach (Relationship r in package.GetRelationships())
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && Pml.RelIs(r.Type, OfficeDocumentRelationship)
                && PartUri.TryResolve(null, r.Target, out PartUri uri) && package.FindPart(uri.Value) is { } part)
            {
                return part.Name;
            }
        }

        return null;
    }

    private MarkupDocument LoadMain()
    {
        MarkupDocument main = Parts.Load(MainPartName, required: true)!;
        MarkupNode root = main.Root;
        if (root.IsNull || !new MarkupRef(root).As<P.Presentation>().HasValue)
        {
            throw new OfficeFormatException(
                DefectKind.Structure,
                "pptx.wrong-root",
                "The presentation part does not start with a p:presentation element.",
                DefectLocation.InPart(MainPartName, root.IsNull ? -1 : root.SourceStart));
        }

        return main;
    }

    /// <summary>
    /// Gets the markup tree of the presentation part, parsed on first use and shared afterwards. Safe to call from several threads at once.
    /// </summary>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML or does not start with <c>p:presentation</c> (<c>pptx.wrong-root</c>); raised at every access until the part is fixed, with the part name and offset.</exception>
    public MarkupDocument MainMarkup
    {
        get
        {
            MarkupDocument? main = Volatile.Read(ref _main);
            if (main is not null)
            {
                return main;
            }

            lock (Parts.Gate)
            {
                return _main ??= LoadMain();
            }
        }
    }

    /// <summary>Gets the <c>p:presentation</c> element: the hop from the hub to the markup layer.</summary>
    public MarkupRef Markup => new(MainMarkup.Root);

    /// <summary>Gets the typed view of the <c>p:presentation</c> element.</summary>
    public P.Presentation View => P.Presentation.Create(Markup);

    /// <summary>Gets a value indicating whether the presentation uses the Strict namespaces.</summary>
    public bool IsStrict => SchemaNamespaces.IsStrict(Markup.NamespaceUriText);

    /// <summary>Gets a value indicating whether the package carries a VBA project. It is never read or run.</summary>
    public bool HasVbaProject
    {
        get
        {
            foreach (IPackagePart part in Package.Parts)
            {
                if (string.Equals(part.ContentType, "application/vnd.ms-office.vbaProject", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the slide size in EMU (<c>p:sldSz</c>), or null when the presentation part has none.</summary>
    public SlideSize? SlideSize
    {
        get
        {
            MarkupRef? size = Pml.P(Markup, "sldSz");
            if (size is null || Pml.AttrLong(size.Value, "cx") is not { } cx || Pml.AttrLong(size.Value, "cy") is not { } cy)
            {
                return null;
            }

            return new SlideSize(cx, cy, Pml.Attr(size.Value, "type"));
        }
    }

    /// <summary>
    /// Gets the presentation-wide default text style (<c>p:defaultTextStyle</c>), the last link of every text style chain, or an invalid
    /// reference when there is none.
    /// </summary>
    public MarkupRef DefaultTextStyle => Pml.P(Markup, "defaultTextStyle") ?? default;

    /// <summary>
    /// Writes every part whose markup was edited back into the package. Parts nobody edited are not touched, so the package copies
    /// them as stored. Saving to a file or stream is the package's (<see cref="IPackage.Save"/>).
    /// </summary>
    public void CommitChanges() => Parts.Flush();

    /// <summary>Gets a value indicating whether any parsed part has been edited since it was read.</summary>
    public bool HasUncommittedChanges => Parts.HasEdits;

    /// <summary>Releases the package when the presentation owns it.</summary>
    /// <param name="disposing">True when called from <see cref="OfficeFile.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsPackage)
        {
            Package.Dispose();
        }

        base.Dispose(disposing);
    }

    // First-use creation of one hub object per part, shared by concurrent readers.
    internal T GetPart<T>(string partName, Func<Presentation, string, T> create) where T : PresentationPart
    {
        lock (Parts.Gate)
        {
            if (_partObjects.TryGetValue(partName, out PresentationPart? existing) && existing is T typed)
            {
                return typed;
            }

            T made = create(this, partName);
            _partObjects[partName] = made;
            return made;
        }
    }

    internal void ForgetPart(string partName)
    {
        lock (Parts.Gate)
        {
            _partObjects.Remove(partName);
            Parts.Forget(partName);
        }
    }

    // Resolves a relationship of a part to the part it targets, or null for external, dangling or invalid targets.
    internal string? ResolveTarget(string? source, Relationship relationship)
    {
        if (relationship.TargetMode == RelationshipTargetMode.Internal
            && PartUri.TryResolve(source is null ? null : new PartUri(source), relationship.Target, out PartUri uri)
            && Package.FindPart(uri.Value) is { } part)
        {
            return part.Name;
        }

        return null;
    }

    internal Relationship? FindRelationship(string source, string id)
    {
        foreach (Relationship r in Package.GetRelationships(source))
        {
            if (string.Equals(r.Id, id, StringComparison.Ordinal))
            {
                return r;
            }
        }

        return null;
    }

    internal void Report(string code, DefectSeverity severity, string message, string part, int offset = -1) =>
        Context.Defects.Report(new Defect(DefectKind.Structure, code, severity, message, DefectLocation.InPart(part, offset)));
}

/// <summary>The size of a slide in EMU (914,400 per inch).</summary>
/// <param name="Width"><c>cx</c>.</param>
/// <param name="Height"><c>cy</c>.</param>
/// <param name="Type">The <c>type</c> attribute naming a standard size (<c>screen16x9</c>, <c>custom</c>…), or null when absent.</param>
public readonly record struct SlideSize(long Width, long Height, string? Type);
