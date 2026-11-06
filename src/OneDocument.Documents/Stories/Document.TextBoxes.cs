using System.Linq;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    private readonly Dictionary<MarkupRef, TextBox> _textBoxes = [];

    /// <summary>
    /// Finds the text boxes of every story: the body, the headers and footers the sections reference, the footnotes, the endnotes and
    /// the comments, in that order and in document order within each. A text box inside another text box is reached through the outer
    /// box's <see cref="BlockContainer.TextBoxes"/>, not listed here. Boxes in <c>mc:Fallback</c> copies are not listed; they are the
    /// <see cref="TextBox.Alternate"/> of the box they repeat.
    /// </summary>
    /// <returns>The text boxes; empty when the document has none.</returns>
    public IReadOnlyList<TextBox> TextBoxes()
    {
        var all = new List<TextBox>();
        all.AddRange(Body.TextBoxes());
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Section section in Sections)
        {
            foreach (HeaderFooterReferenceInfo r in section.HeaderFooterReferences())
            {
                if (section.GetOwn(r.Kind, r.Type) is { } story && seen.Add(story.PartName))
                {
                    all.AddRange(story.TextBoxes());
                }
            }
        }

        foreach (Notes.Note note in Footnotes)
        {
            all.AddRange(note.TextBoxes());
        }

        foreach (Notes.Note note in Endnotes)
        {
            all.AddRange(note.TextBoxes());
        }

        foreach (Comments.Comment comment in Comments)
        {
            all.AddRange(comment.TextBoxes());
        }

        return all;
    }

    /// <summary>
    /// Wraps any <c>w:p</c> element of this document's parts as a paragraph, wherever it sits: in a text box, a shape group, a
    /// content control inside inline content, a field result or markup the hub does not model. Edits and tracked changes work on it
    /// like on any paragraph.
    /// </summary>
    /// <param name="element">An element from one of this document's trees.</param>
    /// <returns>The paragraph.</returns>
    /// <exception cref="ArgumentException">The element is not a WordprocessingML <c>w:p</c>, or belongs to another document.</exception>
    public Paragraph WrapParagraph(MarkupRef element)
    {
        if (!element.IsValid || element.Namespace != SchemaNamespace.Wordprocessing || !element.LocalName.SequenceEqual("p"u8))
        {
            throw new ArgumentException("The element is not a w:p element.", nameof(element));
        }

        if (Parts.PartNameOf(element.Node) is null)
        {
            throw new ArgumentException("The element does not belong to this document.", nameof(element));
        }

        return new Paragraph(this, element);
    }

    /// <summary>Wraps any <c>w:tbl</c> element of this document's parts as a table, wherever it sits (for example in a text box).</summary>
    /// <param name="element">An element from one of this document's trees.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentException">The element is not a WordprocessingML <c>w:tbl</c>, or belongs to another document.</exception>
    public Table WrapTable(MarkupRef element)
    {
        if (!element.IsValid || element.Namespace != SchemaNamespace.Wordprocessing || !element.LocalName.SequenceEqual("tbl"u8))
        {
            throw new ArgumentException("The element is not a w:tbl element.", nameof(element));
        }

        if (Parts.PartNameOf(element.Node) is null)
        {
            throw new ArgumentException("The element does not belong to this document.", nameof(element));
        }

        return new Table(this, element);
    }

    internal TextBox TextBoxFor(MarkupRef content, MarkupRef alternate)
    {
        lock (Parts.Gate)
        {
            // A cached box is only good while its content element is still in its tree; an edit that replaced the element (accepting a
            // change can) leaves a stale entry whose node index may since have been given to something else.
            if (!_textBoxes.TryGetValue(content, out TextBox? box) || !TextBoxScanner.IsAttached(box.Container.Node) || !TextBoxScanner.IsContent(box.Container.Node))
            {
                box = new TextBox(this, content, alternate);
                _textBoxes[content] = box;
            }

            return box;
        }
    }

    private void SyncTextBoxAlternates()
    {
        List<TextBox> boxes;
        lock (Parts.Gate)
        {
            // Boxes whose elements an edit removed (accepting a change can replace a whole paragraph) are forgotten.
            foreach (MarkupRef gone in _textBoxes.Keys.Where(k => !TextBoxScanner.IsAttached(k.Node)).ToList())
            {
                _textBoxes.Remove(gone);
            }

            boxes = [.. _textBoxes.Values];
        }

        foreach (TextBox box in boxes)
        {
            box.SyncAlternate();
        }
    }
}

public abstract partial class BlockContainer
{
    /// <summary>
    /// Finds the text boxes anchored in the paragraphs of this container (tables and content controls included), in document order.
    /// <c>mc:AlternateContent</c> copies count once (see <see cref="TextBox"/>).
    /// </summary>
    /// <returns>The text boxes; empty when there are none.</returns>
    public IReadOnlyList<TextBox> TextBoxes()
    {
        var result = new List<TextBox>();
        foreach (Paragraph p in Paragraphs())
        {
            result.AddRange(p.TextBoxes());
        }

        return result;
    }
}

public sealed partial class Paragraph
{
    /// <summary>Finds the text boxes anchored in this paragraph, in document order (see <see cref="BlockContainer.TextBoxes"/>).</summary>
    /// <returns>The text boxes.</returns>
    public IReadOnlyList<TextBox> TextBoxes() => TextBoxScanner.In(Owner, Markup.Node);
}
