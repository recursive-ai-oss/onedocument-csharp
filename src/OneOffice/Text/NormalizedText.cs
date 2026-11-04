namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>Which story of a document a paragraph belongs to.</summary>
public enum TextStory
{
    /// <summary>The main text of a word-processing document.</summary>
    Body = 0,

    /// <summary>A header.</summary>
    Header = 1,

    /// <summary>A footer.</summary>
    Footer = 2,

    /// <summary>The footnotes.</summary>
    Footnotes = 3,

    /// <summary>The endnotes.</summary>
    Endnotes = 4,

    /// <summary>The comments.</summary>
    Comments = 5,

    /// <summary>A slide.</summary>
    Slide = 6,

    /// <summary>A slide's speaker notes.</summary>
    SlideNotes = 7,

    /// <summary>A worksheet; each string cell is one paragraph.</summary>
    Sheet = 8,

    /// <summary>The text of a diagram (SmartArt) that a document or slide holds, read from its data part.</summary>
    Diagram = 9,
}

/// <summary>One paragraph of the normalized text, with where it came from.</summary>
public sealed class NormalizedParagraph
{
    internal NormalizedParagraph(int index, TextStory story, string partName, long partOffset, string text, bool inTable, bool inTextBox, int textOffset)
    {
        Index = index;
        Story = story;
        PartName = partName;
        PartOffset = partOffset;
        Text = text;
        InTable = inTable;
        InTextBox = inTextBox;
        TextOffset = textOffset;
    }

    /// <summary>Gets the position of the paragraph in <see cref="NormalizedText.Paragraphs"/>, from zero.</summary>
    public int Index { get; }

    /// <summary>Gets the story the paragraph is in.</summary>
    public TextStory Story { get; }

    /// <summary>Gets the part name, for example <c>/word/document.xml</c>.</summary>
    public string PartName { get; }

    /// <summary>
    /// Gets the byte offset in the uncompressed part of the paragraph's start tag (a word-processing or drawing paragraph) or of the cell's
    /// start tag (a worksheet). It is the same offset the markup tree reports for that element.
    /// </summary>
    public long PartOffset { get; }

    /// <summary>
    /// Gets the paragraph's text: run boundaries and field codes removed, deleted text left out unless asked for, a tab as <c>\t</c> and a
    /// line break as <c>\n</c>. Empty for an empty paragraph.
    /// </summary>
    public string Text { get; }

    /// <summary>Gets whether the paragraph is in a table cell (or is a worksheet cell).</summary>
    public bool InTable { get; }

    /// <summary>Gets whether the paragraph is in a text box; it follows the paragraph that holds the text box.</summary>
    public bool InTextBox { get; }

    /// <summary>Gets the position of the paragraph's first character in <see cref="NormalizedText.Text"/>.</summary>
    public int TextOffset { get; }
}

/// <summary>
/// A document's text as a list of paragraphs, for comparing documents: the text is stable (it does not depend on how runs were split or
/// on field codes), and every paragraph says where in its part it came from.
/// </summary>
/// <remarks>
/// The joined <see cref="Text"/> puts a line feed after each paragraph but the last, so a character offset in it maps back to a paragraph
/// (<see cref="TryLocate"/>) and from there to a part and byte offset. Paragraph order is the order of the plain-text extraction: the main
/// story, then headers and footers, footnotes, endnotes and comments, or slides in presentation order, or sheets in workbook order.
/// </remarks>
public sealed class NormalizedText
{
    private string? _text;

    internal NormalizedText(List<NormalizedParagraph> paragraphs) => Paragraphs = paragraphs;

    /// <summary>Gets the paragraphs, in order.</summary>
    public IReadOnlyList<NormalizedParagraph> Paragraphs { get; }

    /// <summary>Gets the paragraphs joined by line feeds.</summary>
    public string Text => _text ??= Join();

    private string Join()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Paragraphs.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            sb.Append(Paragraphs[i].Text);
        }

        return sb.ToString();
    }

    /// <summary>Finds the paragraph that holds a character of <see cref="Text"/>.</summary>
    /// <param name="offset">A character offset in <see cref="Text"/>.</param>
    /// <param name="paragraph">The paragraph, when found.</param>
    /// <param name="offsetInParagraph">The offset inside <see cref="NormalizedParagraph.Text"/>; the length of the text for the line feed that ends the paragraph.</param>
    /// <returns>False when the offset is outside the text.</returns>
    public bool TryLocate(int offset, out NormalizedParagraph paragraph, out int offsetInParagraph)
    {
        paragraph = null!;
        offsetInParagraph = 0;
        if (offset < 0 || Paragraphs.Count == 0)
        {
            return false;
        }

        int lo = 0;
        int hi = Paragraphs.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >>> 1;
            if (Paragraphs[mid].TextOffset <= offset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        NormalizedParagraph p = Paragraphs[lo];
        if (offset > p.TextOffset + p.Text.Length)
        {
            return false;
        }

        paragraph = p;
        offsetInParagraph = offset - p.TextOffset;
        return true;
    }
}
