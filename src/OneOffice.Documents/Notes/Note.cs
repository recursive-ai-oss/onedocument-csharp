using System.Linq;
using System.Collections;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents.Notes;

/// <summary>Footnote or endnote.</summary>
public enum NoteKind
{
    /// <summary>A footnote (<c>w:footnote</c>, part <c>/word/footnotes.xml</c>).</summary>
    Footnote = 0,

    /// <summary>An endnote (<c>w:endnote</c>, part <c>/word/endnotes.xml</c>).</summary>
    Endnote = 1,
}

/// <summary>The role of a note (<c>w:type</c>).</summary>
public enum NoteType
{
    /// <summary>A note with text that a reference in the body points at.</summary>
    Normal = 0,

    /// <summary>The line drawn between the body and the notes on a page.</summary>
    Separator = 1,

    /// <summary>The longer line drawn above notes that continue from the previous page.</summary>
    ContinuationSeparator = 2,

    /// <summary>The text shown when a note continues on the next page.</summary>
    ContinuationNotice = 3,
}

/// <summary>
/// A footnote or an endnote: a container of blocks in the notes part. Separator and continuation notes are notes too (their
/// <see cref="Type"/> says so) and are kept and listed, never hidden.
/// </summary>
/// <remarks>ECMA-376 Part 1 §17.11 (footnotes and endnotes): the note has an id that references in the body use; its type is normal unless <c>w:type</c> says otherwise.</remarks>
public sealed class Note : BlockContainer
{
    internal Note(Document owner, NoteKind kind, MarkupRef element)
        : base(owner, element)
    {
        Kind = kind;
    }

    /// <summary>Gets whether this is a footnote or an endnote.</summary>
    public NoteKind Kind { get; }

    /// <summary>Gets the note id (<c>w:id</c>) that references use; 0 when the attribute is missing or not a number.</summary>
    public long Id => Wml.Long(Wml.Attr(Container, "id")) ?? 0;

    /// <summary>Gets the note's role; an unknown or missing <c>w:type</c> reads as <see cref="NoteType.Normal"/>.</summary>
    public NoteType Type => Wml.Attr(Container, "type") switch
    {
        "separator" => NoteType.Separator,
        "continuationSeparator" => NoteType.ContinuationSeparator,
        "continuationNotice" => NoteType.ContinuationNotice,
        _ => NoteType.Normal,
    };

    /// <summary>Gets the text of the note: its paragraphs' text joined by line feeds. The automatic number mark contributes nothing.</summary>
    public string Text => string.Join('\n', Paragraphs().Select(p => p.Text));

    /// <summary>Lists the references to this note in the body, in document order (usually one).</summary>
    /// <returns>The references.</returns>
    public IReadOnlyList<NoteReference> References() => Owner.NoteReferences(Kind).Where(r => r.Id == Id).ToList();

    /// <summary>
    /// Replaces the text of the note. The first paragraph keeps its properties and the automatic number mark; everything else in
    /// the note is removed and the text becomes one run. Tabs and line feeds become <c>w:tab</c> and <c>w:br</c>.
    /// </summary>
    /// <param name="text">The new text.</param>
    /// <exception cref="ArgumentException">The text holds a character XML cannot carry.</exception>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string prefix = HubXml.Prefix(Container.Node, Owner.WordprocessingNamespaceUri, "w", out string decl);
        string runBody = HubXml.RunContent(prefix, text);
        string run = "<" + prefix + ":r>" + runBody + "</" + prefix + ":r>";
        MarkupNode paragraph = default;
        for (MarkupNode n = Container.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "p")
            {
                paragraph = n;
                break;
            }
        }

        if (paragraph.IsNull)
        {
            Container.Node.AppendXml(HubXml.Bytes("<" + prefix + ":p" + decl + ">" + run + "</" + prefix + ":p>"));
            return;
        }

        var doomed = new List<MarkupNode>();
        for (MarkupNode n = Container.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n != paragraph)
            {
                doomed.Add(n);
            }
        }

        for (MarkupNode n = paragraph.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element || n.LocalNameString == "pPr" || IsMarkRun(n))
            {
                continue;
            }

            doomed.Add(n);
        }

        foreach (MarkupNode n in doomed)
        {
            n.Remove();
        }

        paragraph.AppendXml(HubXml.Bytes(run));
    }

    private static bool IsMarkRun(MarkupNode run)
    {
        if (run.LocalNameString != "r")
        {
            return false;
        }

        return !HubXml.FirstChildElement(run, "footnoteRef").IsNull || !HubXml.FirstChildElement(run, "endnoteRef").IsNull;
    }

    /// <summary>
    /// Inserts a reference to this note into the body, in a new run right after <paramref name="after"/>. The run uses the
    /// document's reference character style when it has one, else superscript. Only normal notes can be referenced.
    /// </summary>
    /// <param name="after">A run of the main document part, not inside a field's code.</param>
    /// <returns>The new reference.</returns>
    /// <exception cref="InvalidOperationException">The note is a separator or continuation note.</exception>
    public NoteReference InsertReference(Run after)
    {
        ArgumentNullException.ThrowIfNull(after);
        RequireNormal();
        after.Markup.Node.InsertXmlAfter(HubXml.Bytes(ReferenceRun(after.Markup.Node)));
        return Found(after.Markup.Node.NextSibling);
    }

    /// <summary>Appends a reference to this note at the end of a paragraph of the main document part (see <see cref="InsertReference"/>).</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <returns>The new reference.</returns>
    /// <exception cref="InvalidOperationException">The note is a separator or continuation note.</exception>
    public NoteReference AppendReference(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        RequireNormal();
        paragraph.Markup.Node.AppendXml(HubXml.Bytes(ReferenceRun(paragraph.Markup.Node)));
        return Found(paragraph.Markup.Node.LastChild);
    }

    private void RequireNormal()
    {
        if (Type != NoteType.Normal)
        {
            throw new InvalidOperationException("Only a normal note can be referenced from the body.");
        }
    }

    private NoteReference Found(MarkupNode run)
    {
        while (!run.IsNull && run.Kind != MarkupNodeKind.Element)
        {
            run = run.NextSibling;
        }

        MarkupNode reference = HubXml.FirstChildElement(run, Kind == NoteKind.Footnote ? "footnoteReference" : "endnoteReference");
        return new NoteReference(Owner, Kind, new MarkupRef(reference));
    }

    private string ReferenceRun(MarkupNode context)
    {
        string p = HubXml.Prefix(context, Owner.WordprocessingNamespaceUri, "w", out string decl);
        string styleId = Kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference";
        string rPr = Owner.Styles.Find(styleId) is not null
            ? "<" + p + ":rPr><" + p + ":rStyle " + p + ":val=\"" + styleId + "\"/></" + p + ":rPr>"
            : "<" + p + ":rPr><" + p + ":vertAlign " + p + ":val=\"superscript\"/></" + p + ":rPr>";
        string element = Kind == NoteKind.Footnote ? "footnoteReference" : "endnoteReference";
        return "<" + p + ":r" + decl + ">" + rPr + "<" + p + ":" + element + " " + p + ":id=\"" + Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"/></" + p + ":r>";
    }
}

/// <summary>A reference to a note in the body (<c>w:footnoteReference</c> or <c>w:endnoteReference</c>).</summary>
public sealed class NoteReference
{
    internal NoteReference(Document owner, NoteKind kind, MarkupRef element)
    {
        Owner = owner;
        Kind = kind;
        Markup = element;
    }

    /// <summary>Gets the document the reference is in.</summary>
    public Document Owner { get; }

    /// <summary>Gets whether it refers to a footnote or an endnote.</summary>
    public NoteKind Kind { get; }

    /// <summary>Gets the reference element.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the id of the note referred to.</summary>
    public long Id => Wml.Long(Wml.Attr(Markup, "id")) ?? 0;

    /// <summary>Gets a value indicating whether a custom mark follows the reference instead of the automatic number (<c>w:customMarkFollows</c>).</summary>
    public bool CustomMarkFollows => Wml.OnOff(Wml.Attr(Markup, "customMarkFollows")) ?? false;

    /// <summary>Gets the run that holds the reference, or null when the element is not in a run.</summary>
    public Run? Run
    {
        get
        {
            MarkupNode parent = Markup.Node.Parent;
            return !parent.IsNull && parent.Kind == MarkupNodeKind.Element && parent.LocalNameString == "r" ? new Run(Owner, new MarkupRef(parent)) : null;
        }
    }

    /// <summary>Gets the note referred to, or null when the notes part has no such note (reported as <c>docx.note-missing</c> when the references were listed).</summary>
    public Note? Note => (Kind == NoteKind.Footnote ? Owner.Footnotes : Owner.Endnotes).Find(Id);
}

/// <summary>
/// The footnotes or the endnotes of a document. A document without a notes part has an empty collection; adding the first note
/// creates the part with its separator notes. Only the notes part, the relationship, the settings and the body change.
/// </summary>
public sealed class NoteCollection : IReadOnlyList<Note>
{
    private const string FootnotesContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml";
    private const string EndnotesContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml";

    private readonly Document _owner;
    private List<Note>? _items;
    private MarkupRef _root;

    internal NoteCollection(Document owner, NoteKind kind)
    {
        _owner = owner;
        Kind = kind;
    }

    /// <summary>Gets whether this collection holds footnotes or endnotes.</summary>
    public NoteKind Kind { get; }

    private string ElementName => Kind == NoteKind.Footnote ? "footnote" : "endnote";

    private string PartLocalName => Kind == NoteKind.Footnote ? "footnotes" : "endnotes";

    /// <summary>Gets the number of notes, separators included.</summary>
    public int Count => Items.Count;

    /// <summary>Gets a note by position in the part.</summary>
    /// <param name="index">Zero-based position.</param>
    public Note this[int index] => Items[index];

    /// <summary>Finds a note by id. With duplicate ids (<c>docx.note-duplicate-id</c>) the first one wins.</summary>
    /// <param name="id">The note id.</param>
    /// <returns>The note, or null.</returns>
    public Note? Find(long id) => Items.FirstOrDefault(n => n.Id == id);

    /// <summary>Lists the notes that carry text: separator and continuation notes left out.</summary>
    /// <returns>The normal notes in part order.</returns>
    public IReadOnlyList<Note> Normal() => Items.Where(n => n.Type == NoteType.Normal).ToList();

    /// <inheritdoc/>
    public IEnumerator<Note> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private List<Note> Items
    {
        get
        {
            if (_items is not null)
            {
                return _items;
            }

            _items = [];
            MarkupDocument? doc = _owner.LoadRelatedByType(_owner.OfficeRelationship(PartLocalName), out string? name);
            if (doc is null)
            {
                return _items;
            }

            _root = new MarkupRef(doc.Root);
            var seen = new HashSet<long>();
            foreach (MarkupRef e in Wml.Elements(_root))
            {
                if (!Wml.Is(e, System.Text.Encoding.ASCII.GetBytes(ElementName)))
                {
                    continue;
                }

                var note = new Note(_owner, Kind, e);
                if (!seen.Add(note.Id))
                {
                    _owner.Context.Defects.Report(new Defect(DefectKind.Reference, "docx.note-duplicate-id", DefectSeverity.Repaired,
                        "Two " + ElementName + "s have the id " + note.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; references use the first.",
                        DefectLocation.InPart(name!, e.Node.SourceStart)));
                }

                _items.Add(note);
            }

            return _items;
        }
    }

    /// <summary>
    /// Adds a normal note at the end of the part with the given text and returns it. The note's first paragraph starts with the
    /// automatic number mark (styled when the document has the note styles). Nothing refers to the note until
    /// <see cref="Note.InsertReference"/> is called.
    /// </summary>
    /// <param name="text">The note text.</param>
    /// <returns>The new note. Its id is one more than the highest id in use, and at least 1.</returns>
    /// <exception cref="ArgumentException">The text holds a character XML cannot carry.</exception>
    public Note Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsurePart();
        long id = Math.Max(1, Items.Count == 0 ? 1 : Items.Max(n => n.Id) + 1);
        MarkupNode root = _root.Node;
        string p = HubXml.Prefix(root, _owner.WordprocessingNamespaceUri, "w", out string decl);
        string textStyle = Kind == NoteKind.Footnote ? "FootnoteText" : "EndnoteText";
        string refStyle = Kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference";
        string mark = Kind == NoteKind.Footnote ? "footnoteRef" : "endnoteRef";
        string pPr = _owner.Styles.Find(textStyle) is not null ? "<" + p + ":pPr><" + p + ":pStyle " + p + ":val=\"" + textStyle + "\"/></" + p + ":pPr>" : string.Empty;
        string rPr = _owner.Styles.Find(refStyle) is not null
            ? "<" + p + ":rPr><" + p + ":rStyle " + p + ":val=\"" + refStyle + "\"/></" + p + ":rPr>"
            : "<" + p + ":rPr><" + p + ":vertAlign " + p + ":val=\"superscript\"/></" + p + ":rPr>";
        string xml = "<" + p + ":" + ElementName + decl + " " + p + ":id=\"" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"><" + p + ":p>" + pPr
            + "<" + p + ":r>" + rPr + "<" + p + ":" + mark + "/></" + p + ":r><" + p + ":r>"
            + HubXml.RunContent(p, " " + text) + "</" + p + ":r></" + p + ":p></" + p + ":" + ElementName + ">";
        root.AppendXml(HubXml.Bytes(xml));
        MarkupNode added = root.LastChild;
        while (!added.IsNull && added.Kind != MarkupNodeKind.Element)
        {
            added = added.PreviousSibling;
        }

        var note = new Note(_owner, Kind, new MarkupRef(added));
        Items.Add(note);
        return note;
    }

    /// <summary>
    /// Removes a normal note and every reference to it in the body (the reference run goes when it holds nothing else). The
    /// separator and continuation notes cannot be removed.
    /// </summary>
    /// <param name="note">A note of this collection.</param>
    /// <returns>True when the note was in the collection and is removed.</returns>
    /// <exception cref="InvalidOperationException">The note is a separator or continuation note.</exception>
    public bool Remove(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.Type != NoteType.Normal)
        {
            throw new InvalidOperationException("Separator and continuation notes cannot be removed.");
        }

        if (!Items.Remove(note))
        {
            return false;
        }

        foreach (NoteReference r in _owner.NoteReferences(Kind).Where(r => r.Id == note.Id))
        {
            MarkupNode run = r.Markup.Node.Parent;
            bool onlyReference = run.LocalNameString == "r" && run.ChildElements.All(c => c == r.Markup.Node || c.LocalNameString == "rPr");
            (onlyReference ? run : r.Markup.Node).Remove();
        }

        note.Container.Node.Remove();
        return true;
    }

    private void EnsurePart()
    {
        if (_root.IsValid)
        {
            return;
        }

        _ = Items;
        if (_root.IsValid)
        {
            return;
        }

        string ns = _owner.WordprocessingNamespaceUri;
        string sep = "<w:" + ElementName + " w:type=\"separator\" w:id=\"-1\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:separator/></w:r></w:p></w:" + ElementName + ">"
            + "<w:" + ElementName + " w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:continuationSeparator/></w:r></w:p></w:" + ElementName + ">";
        MarkupDocument doc = _owner.CreatePart(
            _owner.OfficeRelationship(PartLocalName),
            "/word/" + PartLocalName + ".xml",
            Kind == NoteKind.Footnote ? FootnotesContentType : EndnotesContentType,
            "<w:" + PartLocalName + " xmlns:w=\"" + ns + "\">" + sep + "</w:" + PartLocalName + ">",
            out _);
        _root = new MarkupRef(doc.Root);
        _items = [];
        foreach (MarkupRef e in Wml.Elements(_root))
        {
            _items.Add(new Note(_owner, Kind, e));
        }

        _owner.GetNoteNumbering(Kind).AddSeparatorReferences();
    }
}
