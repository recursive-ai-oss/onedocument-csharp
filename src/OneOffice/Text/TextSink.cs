namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>Where a paragraph sits when it is handed to a sink.</summary>
/// <param name="PartOffset">Byte offset of the element in its part.</param>
/// <param name="TableDepth">Tables the paragraph is inside (0 outside tables; a worksheet cell is 1).</param>
/// <param name="InTextBox">True for a paragraph of a text box.</param>
internal readonly record struct ParagraphInfo(long PartOffset, int TableDepth, bool InTextBox);

/// <summary>Receives what an extractor finds, in reading order. One sink per extraction; the extractors never see how it is used.</summary>
internal abstract class TextSink
{
    /// <summary>Starts a part (a story, slide or sheet).</summary>
    public abstract void BeginPart(string partName, TextStory story);

    /// <summary>A paragraph's text; valid only during the call.</summary>
    public abstract void Paragraph(ReadOnlySpan<char> text, in ParagraphInfo info);

    /// <summary>A table cell starts (outermost tables and worksheet cells only matter to the plain-text layout).</summary>
    public abstract void CellStart(int tableDepth);

    /// <summary>A table row ends.</summary>
    public abstract void RowEnd(int tableDepth);

    /// <summary>Ends a part.</summary>
    public abstract void EndPart();
}

/// <summary>Lays the text out as plain text: a line per paragraph, cells of a row separated by a tab, a row on one line.</summary>
internal sealed class PlainTextSink(TextWriter output) : TextSink
{
    private bool _wrotePart;
    private bool _partHasText;
    private int _cellsInRow;
    private bool _cellHasText;

    public override void BeginPart(string partName, TextStory story)
    {
        // Parts are told apart by a blank line, as long as the one before wrote something.
        if (_wrotePart)
        {
            output.Write('\n');
            _wrotePart = false;
        }

        _partHasText = false;
        _cellsInRow = 0;
        _cellHasText = false;
    }

    public override void Paragraph(ReadOnlySpan<char> text, in ParagraphInfo info)
    {
        if (info.TableDepth == 0)
        {
            output.Write(text);
            output.Write('\n');
            _partHasText = true;
            return;
        }

        // Inside a cell the paragraphs (and line breaks) run together with spaces, so a row stays on one line.
        if (text.IsEmpty)
        {
            return;
        }

        if (_cellHasText)
        {
            output.Write(' ');
        }

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                output.Write(text[start..i]);
                output.Write(' ');
                start = i + 1;
            }
        }

        output.Write(text[start..]);
        _cellHasText = true;
        _partHasText = true;
    }

    public override void CellStart(int tableDepth)
    {
        if (tableDepth != 1)
        {
            return;
        }

        if (_cellsInRow > 0)
        {
            output.Write('\t');
        }

        _cellsInRow++;
        _cellHasText = false;
    }

    public override void RowEnd(int tableDepth)
    {
        if (tableDepth != 1)
        {
            return;
        }

        if (_cellsInRow > 0)
        {
            output.Write('\n');
        }

        _cellsInRow = 0;
        _cellHasText = false;
    }

    public override void EndPart() => _wrotePart = _partHasText;
}

/// <summary>Collects paragraphs for <see cref="NormalizedText"/>.</summary>
internal sealed class NormalizedSink : TextSink
{
    private readonly List<NormalizedParagraph> _paragraphs = [];
    private string _part = string.Empty;
    private TextStory _story;
    private int _textOffset;

    public override void BeginPart(string partName, TextStory story)
    {
        _part = partName;
        _story = story;
    }

    public override void Paragraph(ReadOnlySpan<char> text, in ParagraphInfo info)
    {
        var p = new NormalizedParagraph(_paragraphs.Count, _story, _part, info.PartOffset, text.ToString(), info.TableDepth > 0, info.InTextBox, _textOffset);
        _paragraphs.Add(p);
        _textOffset += text.Length + 1;
    }

    public override void CellStart(int tableDepth)
    {
    }

    public override void RowEnd(int tableDepth)
    {
    }

    public override void EndPart()
    {
    }

    public NormalizedText ToResult() => new(_paragraphs);
}
