using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;
using W = RecursiveAI.OneDocument.Schema.Wordprocessing;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>
/// A word-processing document: the semantic hub over a WordprocessingML package. For OOXML files the hub is a set of typed views
/// over the markup tree of each part, not a copy: reading goes through <see cref="Markup"/> and the generated views, and an edit
/// changes the tree and nothing else. A document opened and saved without edits writes every part byte-identical.
/// </summary>
/// <remarks>
/// <para>Parts are parsed on first use and only the ones touched are read. <see cref="CommitChanges"/> writes the parts whose
/// markup was edited back into the package; the other parts are never touched, so the package copies them raw.</para>
/// <para>A document is not safe for concurrent mutation. After it is loaded, read-only access from several threads is safe as
/// long as nobody edits it.</para>
/// </remarks>
public sealed partial class Document : OfficeFile
{
    internal const string OfficeDocumentRelationship = "officeDocument";

    private readonly bool _ownsPackage;
    private Body? _body;
    private MarkupDocument? _main;

    private Document(IOpcPackage package, OpenContext context, string mainPartName, bool ownsPackage, OfficeFormat format, string? filePath)
        : base(format, context, filePath)
    {
        Package = package;
        MainPartName = mainPartName;
        _ownsPackage = ownsPackage;
        Parts = new PartStore(package, context);
    }

    /// <summary>Gets the package the document sits on.</summary>
    public IOpcPackage Package { get; }

    /// <summary>Gets the limits and defect sink the document was opened with. Repairs found while reading any part are reported to <see cref="OpenContext.Defects"/>.</summary>
    public new OpenContext Context => base.Context;

    /// <summary>Gets the part name of the main document part, for example <c>/word/document.xml</c>.</summary>
    public string MainPartName { get; }

    internal PartStore Parts { get; }

    // First-use initialisation shared by concurrent readers: one instance, built under the document's gate.
    private T Init<T>(ref T? field, Func<T> create) where T : class
    {
        if (Volatile.Read(ref field) is { } existing)
        {
            return existing;
        }

        lock (Parts.Gate)
        {
            return field ??= create();
        }
    }

    /// <summary>
    /// Opens the hub over a package that has a main document part.
    /// </summary>
    /// <param name="package">An OPC package holding a word-processing document.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="ownsPackage">True to dispose the package when the document is disposed.</param>
    /// <returns>The document. Nothing but the package relationships is read yet; the main part is parsed on first use.</returns>
    /// <exception cref="OfficeFormatException">The package has no main document part (<c>docx.no-main-part</c>). A main part that is not well-formed XML raises at its first use instead (see <see cref="MainMarkup"/>).</exception>
    public static Document FromPackage(IOpcPackage package, OpenContext? context = null, bool ownsPackage = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        context ??= new OpenContext(package.Limits);
        string? main = FindMainPart(package);
        if (main is null)
        {
            throw new OfficeFormatException(
                DefectKind.Structure,
                "docx.no-main-part",
                "The package has no main document part: no officeDocument relationship points at a part that exists.",
                DefectLocation.InPart("/_rels/.rels"));
        }

        // The main part is parsed on first use (MainMarkup), so opening and saving an untouched document never reads it.
        return new Document(package, context, main, ownsPackage, FormatOf(package, main), package.FilePath);
    }

    private static string? FindMainPart(IOpcPackage package)
    {
        foreach (Relationship r in package.GetRelationships())
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && RelationshipTypes.Is(r.Type, OfficeDocumentRelationship)
                && PartUri.TryResolve(null, r.Target, out PartUri uri) && package.FindPart(uri.Value) is { } part)
            {
                return part.Name;
            }
        }

        return null;
    }

    private MarkupDocument LoadMain()
    {
        // The main part must parse: without it there is no document. Other parts degrade to defects.
        MarkupDocument main = Parts.Load(MainPartName, required: true)!;
        MarkupNode root = main.Root;
        if (root.IsNull || !new MarkupRef(root).As<W.Document>().HasValue)
        {
            throw new OfficeFormatException(
                DefectKind.Structure,
                "docx.wrong-root",
                "The main document part does not start with a w:document element.",
                DefectLocation.InPart(MainPartName, root.IsNull ? -1 : root.SourceStart));
        }

        return main;
    }

    /// <summary>
    /// Gets the markup tree of the main part, parsed on first use and shared afterwards. Safe to call from several threads at once.
    /// </summary>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML or does not start with <c>w:document</c> (<c>docx.wrong-root</c>); raised at every access until the part is fixed, with the part name and offset.</exception>
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

    /// <summary>Gets the <c>w:document</c> element: the hop from the hub to the markup layer.</summary>
    public MarkupRef Markup => new(MainMarkup.Root);

    /// <summary>Gets the typed view of the <c>w:document</c> element.</summary>
    public W.Document View => W.Document.Create(Markup);

    /// <summary>Gets a value indicating whether the document uses the Strict namespaces.</summary>
    public bool IsStrict => Markup.NamespaceUriText.Contains("purl.oclc.org", StringComparison.Ordinal);

    /// <summary>
    /// Gets the body. A document without a <c>w:body</c> reads as an empty body (and reports <c>docx.no-body</c> once); the tree
    /// is not changed until something is added.
    /// </summary>
    public Body Body
    {
        get
        {
            Body? existing = Volatile.Read(ref _body);
            if (existing is not null)
            {
                return existing;
            }

            lock (Parts.Gate)
            {
                return _body ??= CreateBody();
            }
        }
    }

    private Body CreateBody()
    {
        MarkupRef? element = Markup.FindChild(SchemaNamespace.Wordprocessing, "body"u8);
        if (element is null)
        {
            Context.Defects.Report(new Defect(
                DefectKind.Structure,
                "docx.no-body",
                DefectSeverity.Repaired,
                "The document has no w:body element; it reads as empty.",
                DefectLocation.InPart(MainPartName, Markup.Node.SourceStart)));
        }

        return new Body(this, element ?? default);
    }

    /// <summary>
    /// Writes every part whose markup was edited back into the package. Parts nobody edited are not touched, so the package
    /// copies them as stored. Saving to a file or stream is the package's (<see cref="IPackage.Save"/>).
    /// </summary>
    public void CommitChanges()
    {
        SyncTextBoxAlternates();
        NoteEditsForRtf();
        NoteEditsForOdf();
        NoteEditsForMarkdown();
        Parts.Flush();
        Volatile.Read(ref _properties)?.Commit();
    }

    /// <summary>Gets a value indicating whether any parsed part has been edited since it was read.</summary>
    public bool HasUncommittedChanges => Parts.HasEdits || (Volatile.Read(ref _properties)?.HasUncommittedChanges ?? false);

    /// <summary>Releases the package when the document owns it.</summary>
    /// <param name="disposing">True when called from <see cref="OfficeFile.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsPackage)
        {
            Package.Dispose();
        }

        base.Dispose(disposing);
    }
}
