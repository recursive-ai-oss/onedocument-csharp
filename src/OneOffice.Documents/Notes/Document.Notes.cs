using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Notes;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents;

public sealed partial class Document
{
    private NoteCollection? _footnotes;
    private NoteCollection? _endnotes;

    /// <summary>Gets the footnotes (<c>/word/footnotes.xml</c>); empty when the document has none.</summary>
    public NoteCollection Footnotes => _footnotes ??= new NoteCollection(this, NoteKind.Footnote);

    /// <summary>Gets the endnotes (<c>/word/endnotes.xml</c>); empty when the document has none.</summary>
    public NoteCollection Endnotes => _endnotes ??= new NoteCollection(this, NoteKind.Endnote);

    /// <summary>
    /// Lists the note references in the body, in document order. A reference whose note does not exist is reported once per
    /// call as <c>docx.note-missing</c>.
    /// </summary>
    /// <param name="kind">Footnote or endnote references.</param>
    /// <returns>The references.</returns>
    public IReadOnlyList<NoteReference> NoteReferences(NoteKind kind)
    {
        string name = kind == NoteKind.Footnote ? "footnoteReference" : "endnoteReference";
        NoteCollection notes = kind == NoteKind.Footnote ? Footnotes : Endnotes;
        var result = new List<NoteReference>();
        foreach (MarkupNode n in HubXml.FindWordElements(Body.Container.IsValid ? Body.Container.Node : default, name))
        {
            var reference = new NoteReference(this, kind, new MarkupRef(n));
            if (notes.Find(reference.Id) is null)
            {
                Context.Defects.Report(new Defect(DefectKind.Reference, "docx.note-missing", DefectSeverity.Repaired,
                    "A " + (kind == NoteKind.Footnote ? "footnote" : "endnote") + " reference points at id " + reference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", which the notes part does not have.",
                    DefectLocation.InPart(MainPartName, n.SourceStart)));
            }

            result.Add(reference);
        }

        return result;
    }

    /// <summary>
    /// Gets the document-wide note numbering properties (<c>w:footnotePr</c> or <c>w:endnotePr</c> in the settings part). Reading
    /// works without a settings part (nothing is set); setting a property needs one.
    /// </summary>
    /// <param name="kind">Footnote or endnote properties.</param>
    /// <returns>The properties holder.</returns>
    public NoteNumbering GetNoteNumbering(NoteKind kind)
    {
        MarkupDocument? settings = Parts.LoadRelated(MainPartName, "settings", out _);
        return new NoteNumbering(this, kind, settings is null ? default : new MarkupRef(settings.Root), isSettings: true);
    }

    /// <summary>Gets the note numbering properties of a section (its <c>w:sectPr</c>). Setting a property creates the element in schema order.</summary>
    /// <param name="sectionProperties">A <c>w:sectPr</c> element.</param>
    /// <param name="kind">Footnote or endnote properties.</param>
    /// <returns>The properties holder.</returns>
    public NoteNumbering GetNoteNumbering(MarkupRef sectionProperties, NoteKind kind) =>
        new(this, kind, sectionProperties, isSettings: false);
}
