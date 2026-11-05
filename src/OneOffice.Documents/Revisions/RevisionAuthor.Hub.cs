using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents.Revisions;

// The same operations on the document model's own types, so a caller that works with Paragraph, Run, Table, TableRow and TableCell
// does not drop to markup nodes. They are thin: each hands the element to the markup-level operation of the same name.
public sealed partial class RevisionAuthor
{
    private static readonly string[] s_paragraphExcluded = ["rPr", "sectPr", "pPrChange"];
    private static readonly string[] s_runExcluded = ["ins", "del", "moveFrom", "moveTo", "rPrChange"];
    private static readonly string[] s_rowExcluded = ["ins", "del", "trPrChange"];
    private static readonly string[] s_cellExcluded = ["cellIns", "cellDel", "cellMerge", "tcPrChange"];

    // ---------------------------------------------------------------- text

    /// <summary>Inserts text as a tracked insertion at a position in a paragraph. See <see cref="InsertText(MarkupNode, int, string)"/>.</summary>
    /// <param name="paragraph">The paragraph, from the document this author's set belongs to.</param>
    /// <param name="offset">The position in the paragraph's current text.</param>
    /// <param name="text">The text.</param>
    public void InsertText(Paragraph paragraph, int offset, string text) => InsertText(NodeOf(paragraph), offset, text);

    /// <summary>Marks a stretch of a paragraph's current text as deleted. See <see cref="DeleteText(MarkupNode, int, int)"/>.</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="offset">The first character to delete.</param>
    /// <param name="length">How many characters.</param>
    public void DeleteText(Paragraph paragraph, int offset, int length) => DeleteText(NodeOf(paragraph), offset, length);

    /// <summary>Replaces a stretch of text: a tracked deletion followed by a tracked insertion. See <see cref="ReplaceText(MarkupNode, int, int, string)"/>.</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="offset">The first character to replace.</param>
    /// <param name="length">How many characters.</param>
    /// <param name="text">The new text; it takes the formatting of the text it replaces.</param>
    public void ReplaceText(Paragraph paragraph, int offset, int length, string text) => ReplaceText(NodeOf(paragraph), offset, length, text);

    /// <summary>Moves a stretch of text to another place, as <c>w:moveFrom</c> and <c>w:moveTo</c> with their range markers. See <see cref="MoveText(MarkupNode, int, int, MarkupNode, int, string?)"/>.</summary>
    /// <param name="paragraph">The paragraph the text is in.</param>
    /// <param name="offset">The first character to move.</param>
    /// <param name="length">How many characters.</param>
    /// <param name="destinationParagraph">The paragraph it goes into (may be the same).</param>
    /// <param name="destinationOffset">The position in the destination's current text, measured before the move.</param>
    /// <param name="moveName">The name pairing the two ends; null makes one from the id.</param>
    public void MoveText(Paragraph paragraph, int offset, int length, Paragraph destinationParagraph, int destinationOffset, string? moveName = null) =>
        MoveText(NodeOf(paragraph), offset, length, NodeOf(destinationParagraph), destinationOffset, moveName);

    // ----------------------------------------------------------- paragraphs

    /// <summary>Inserts a new paragraph after <paramref name="paragraph"/> as a tracked change. See <see cref="InsertParagraph(MarkupNode, string)"/>.</summary>
    /// <param name="paragraph">The paragraph to insert after.</param>
    /// <param name="text">The new paragraph's text (may be empty).</param>
    /// <returns>The new paragraph. It holds the properties the old one had; the old paragraph's mark is the inserted one.</returns>
    public Paragraph InsertParagraph(Paragraph paragraph, string text)
    {
        MarkupNode node = NodeOf(paragraph);
        InsertParagraph(node, text);
        return new Paragraph(paragraph.Owner, new MarkupRef(WordXml.NextElement(node)));
    }

    /// <summary>Marks a whole paragraph, its text and its mark, as deleted.</summary>
    /// <param name="paragraph">The paragraph.</param>
    public void DeleteParagraph(Paragraph paragraph) => DeleteParagraph(NodeOf(paragraph));

    /// <summary>Moves a whole paragraph to after another one. See <see cref="MoveParagraph(MarkupNode, MarkupNode, string?)"/>.</summary>
    /// <param name="paragraph">The paragraph to move.</param>
    /// <param name="after">The paragraph it goes after.</param>
    /// <param name="moveName">The name pairing the two ends; null makes one from the id.</param>
    public void MoveParagraph(Paragraph paragraph, Paragraph after, string? moveName = null) =>
        MoveParagraph(NodeOf(paragraph), NodeOf(after), moveName);

    // ----------------------------------------------------------- formatting

    /// <summary>Changes the formatting of a stretch of text as a tracked change.</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="offset">The first character.</param>
    /// <param name="length">How many characters; zero does nothing.</param>
    /// <param name="change">The properties to set over the existing ones; the others stay. New elements go where the schema's order puts them.</param>
    public void FormatText(Paragraph paragraph, int offset, int length, RunPropertiesDelta change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MarkupNode node = NodeOf(paragraph);
        CheckRange(node, offset, length);
        if (length == 0)
        {
            return;
        }

        WmlWriter w = WmlWriter.For(node);
        _ = change.Merge(default, w, s_runExcluded); // validates before anything changes
        FormatCore(node, offset, length, run => change.Merge(WordXml.Child(run, "rPr"), w, s_runExcluded));
    }

    /// <summary>Changes a run's formatting as a tracked change.</summary>
    /// <param name="run">The run.</param>
    /// <param name="change">The properties to set over the existing ones.</param>
    public void ChangeRunProperties(Run run, RunPropertiesDelta change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MarkupNode node = NodeOf(run.Markup.Node, "r");
        WmlWriter w = WmlWriter.For(node);
        ChangeRunProperties(node, change.Merge(WordXml.Child(node, "rPr"), w, s_runExcluded));
    }

    /// <summary>Changes a paragraph's formatting (alignment, indents, spacing, style, numbering) as a tracked change.</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="change">The properties to set over the existing ones.</param>
    public void ChangeParagraphProperties(Paragraph paragraph, ParagraphPropertiesDelta change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MarkupNode node = NodeOf(paragraph);
        WmlWriter w = WmlWriter.For(node);
        ChangeParagraphProperties(node, change.Merge(WordXml.Child(node, "pPr"), w, s_paragraphExcluded));
    }

    /// <summary>Changes a table's formatting as a tracked change.</summary>
    /// <param name="table">The table.</param>
    /// <param name="change">The properties to set over the existing ones.</param>
    public void ChangeTableProperties(Table table, TablePropertiesDelta change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MarkupNode node = NodeOf(table.Markup.Node, "tbl");
        WmlWriter w = WmlWriter.For(node);
        ChangeTableProperties(node, change.Merge(WordXml.Child(node, "tblPr"), w, ["tblPrChange"]));
    }

    /// <summary>Changes a table row's formatting as a tracked change.</summary>
    /// <param name="row">The row.</param>
    /// <param name="change">The properties to set over the existing ones.</param>
    public void ChangeTableRowProperties(TableRow row, RowPropertiesDelta change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MarkupNode node = NodeOf(row.Markup.Node, "tr");
        WmlWriter w = WmlWriter.For(node);
        ChangeTableRowProperties(node, change.Merge(WordXml.Child(node, "trPr"), w, s_rowExcluded));
    }

    /// <summary>Changes a table cell's formatting as a tracked change.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="change">The properties to set over the existing ones.</param>
    public void ChangeTableCellProperties(TableCell cell, CellPropertiesDelta change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MarkupNode node = NodeOf(cell.Container.Node, "tc");
        WmlWriter w = WmlWriter.For(node);
        ChangeTableCellProperties(node, change.Merge(WordXml.Child(node, "tcPr"), w, s_cellExcluded));
    }

    // ---------------------------------------------------------------- tables

    /// <summary>
    /// Inserts a row into a table as a tracked change: the row, its cells' text and its marks are recorded as inserted.
    /// The row copies the structure of a neighbouring row (see <see cref="Table.InsertRow(int)"/>).
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="index">The new row's position, 0 to the row count.</param>
    /// <param name="cellTexts">The text of each cell in order (missing cells stay empty; extra texts are refused).</param>
    /// <returns>The new row.</returns>
    /// <exception cref="ArgumentException">There are more texts than cells.</exception>
    public TableRow InsertRow(Table table, int index, IReadOnlyList<string>? cellTexts = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        NodeOf(table.Markup.Node, "tbl");
        if (cellTexts is not null)
        {
            foreach (string t in cellTexts)
            {
                ArgumentNullException.ThrowIfNull(t);
                WordXml.RequireXmlText(t, nameof(cellTexts));
            }
        }

        TableRow row = table.InsertRow(index);
        if (cellTexts is not null)
        {
            List<TableCell> cells = [.. row.Cells()];
            if (cellTexts.Count > cells.Count)
            {
                row.Markup.Node.Remove();
                throw new ArgumentException("The row has fewer cells than texts.", nameof(cellTexts));
            }

            for (int i = 0; i < cellTexts.Count; i++)
            {
                Paragraph? first = cells[i].Paragraphs().FirstOrDefault();
                first?.SetText(cellTexts[i]);
            }
        }

        MarkRowInserted(row);
        return row;
    }

    /// <summary>Marks a table row, which the caller has just added, as an inserted row.</summary>
    /// <param name="row">The row.</param>
    public void MarkRowInserted(TableRow row) => MarkRowInserted(NodeOf(row.Markup.Node, "tr"));

    /// <summary>Marks a table row as deleted, with its text.</summary>
    /// <param name="row">The row.</param>
    public void MarkRowDeleted(TableRow row) => MarkRowDeleted(NodeOf(row.Markup.Node, "tr"));

    /// <summary>Marks a table cell, which the caller has just added, as an inserted cell.</summary>
    /// <param name="cell">The cell.</param>
    public void MarkCellInserted(TableCell cell) => MarkCellInserted(NodeOf(cell.Container.Node, "tc"));

    /// <summary>Marks a table cell as deleted, with its text.</summary>
    /// <param name="cell">The cell.</param>
    public void MarkCellDeleted(TableCell cell) => MarkCellDeleted(NodeOf(cell.Container.Node, "tc"));

    /// <summary>Records a change in how a cell is merged vertically. See <see cref="MergeCells(MarkupNode, string?, string?)"/>.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="mergeNow">The new state: <c>restart</c>, <c>continue</c> or null.</param>
    /// <param name="mergeBefore">The state before.</param>
    public void MergeCells(TableCell cell, string? mergeNow, string? mergeBefore) =>
        MergeCells(NodeOf(cell.Container.Node, "tc"), mergeNow, mergeBefore);

    /// <summary>
    /// Inserts a whole table after a block as a tracked change: every row is an inserted row with inserted text, so rejecting removes
    /// the table. The table has one grid column per column of the widest row, equal widths across the page's text width given, single borders
    /// and no style. When the table would end a table cell, an inserted empty paragraph follows it (a cell has to end with a paragraph);
    /// rejecting then leaves that one empty paragraph behind, as an inserted final paragraph mark cannot be removed.
    /// </summary>
    /// <param name="after">The block it goes after (a paragraph or a table).</param>
    /// <param name="cells">The rows, each a list of cell texts; at least one row, and every row at least one cell.</param>
    /// <param name="widthTwips">The table's width in twips (default 9360: six and a half inches).</param>
    /// <returns>The new table.</returns>
    public Table InsertTable(BlockElement after, IReadOnlyList<IReadOnlyList<string>> cells, int widthTwips = 9360)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(cells);
        MarkupNode anchor = after.Markup.Node;
        if (!WordXml.IsAny(anchor, "p", "tbl") || !_set.Parts.Any(p => ReferenceEquals(p.Markup, anchor.Document)))
        {
            throw new ArgumentException("The block is not a paragraph or table in one of the revision set's parts.", nameof(after));
        }

        if (cells.Count == 0 || widthTwips <= 0)
        {
            throw new ArgumentException("A table needs at least one row and a positive width.");
        }

        int columns = 0;
        foreach (IReadOnlyList<string> r in cells)
        {
            ArgumentNullException.ThrowIfNull(r);
            if (r.Count == 0)
            {
                throw new ArgumentException("A row needs at least one cell.", nameof(cells));
            }

            foreach (string t in r)
            {
                ArgumentNullException.ThrowIfNull(t);
                WordXml.RequireXmlText(t, nameof(cells));
            }

            columns = Math.Max(columns, r.Count);
        }

        WmlWriter w = WmlWriter.For(anchor);
        string inv(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int colWidth = widthTwips / columns;
        var xml = new StringBuilder();
        string border(string n) => w.Empty(n, w.Attr("val", "single") + w.Attr("sz", "4") + w.Attr("space", "0") + w.Attr("color", "auto"));
        xml.Append(w.Wrap("tblPr", string.Empty,
            w.Empty("tblW", w.Attr("w", inv(colWidth * columns)) + w.Attr("type", "dxa"))
            + w.Wrap("tblBorders", string.Empty, border("top") + border("left") + border("bottom") + border("right") + border("insideH") + border("insideV"))
            + w.Empty("tblLayout", w.Attr("type", "fixed"))));
        var grid = new StringBuilder();
        for (int c = 0; c < columns; c++)
        {
            grid.Append(w.Empty("gridCol", w.Attr("w", inv(colWidth))));
        }

        xml.Append(w.Wrap("tblGrid", string.Empty, grid.ToString()));
        foreach (IReadOnlyList<string> r in cells)
        {
            var row = new StringBuilder();
            for (int c = 0; c < columns; c++)
            {
                string text = c < r.Count ? r[c] : string.Empty;
                string content = text.Length == 0 ? string.Empty : w.Wrap("r", string.Empty, RunContent(w, text));
                row.Append(w.Wrap("tc", string.Empty,
                    w.Wrap("tcPr", string.Empty, w.Empty("tcW", w.Attr("w", inv(colWidth)) + w.Attr("type", "dxa")))
                    + w.Wrap("p", string.Empty, content)));
            }

            xml.Append(w.Wrap("tr", string.Empty, row.ToString()));
        }

        string table = w.Wrap("tbl", string.Empty, xml.ToString());
        anchor.InsertXmlAfter(WordXml.Bytes(table));
        MarkupNode created = WordXml.NextElement(anchor);
        foreach (MarkupNode row in created.ChildElements.Where(c => WordXml.Is(c, "tr")).ToList())
        {
            MarkRowInserted(row);
        }

        MarkupNode following = WordXml.NextElement(created);
        if (following.IsNull && WordXml.Is(anchor.Parent, "tc"))
        {
            // A cell has to end with a paragraph, so one follows the table; its mark is recorded as inserted. A body that ends with a
            // table needs nothing: Word supplies the final paragraph itself.
            created.InsertXmlAfter(WordXml.Bytes(w.Wrap("p", string.Empty, string.Empty)));
            MarkParagraphMark(WordXml.NextElement(created), "ins");
        }

        _set.Invalidate();
        return new Table(after.Owner, new MarkupRef(created));
    }

    /// <summary>Marks every row of a table as deleted, with its text, so accepting removes the table.</summary>
    /// <param name="table">The table.</param>
    public void DeleteTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        MarkupNode node = NodeOf(table.Markup.Node, "tbl");
        foreach (MarkupNode row in node.ChildElements.Where(c => WordXml.Is(c, "tr")).ToList())
        {
            MarkRowDeleted(row);
        }

        _set.Invalidate();
    }

    // ------------------------------------------------------------ internals

    private static MarkupNode NodeOf(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return paragraph.Markup.Node;
    }

    private MarkupNode NodeOf(MarkupNode node, string local)
    {
        if (!WordXml.Is(node, local))
        {
            throw new ArgumentException("The element is not a w:" + local + " element.");
        }

        if (!_set.Parts.Any(p => ReferenceEquals(p.Markup, node.Document)))
        {
            throw new ArgumentException("The element is not in one of the revision set's parts.");
        }

        return node;
    }
}
