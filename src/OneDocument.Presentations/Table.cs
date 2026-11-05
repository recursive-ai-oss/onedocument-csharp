using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// A table in a graphic frame (<c>a:tbl</c>) as a logical grid: rows of cells with their merges resolved. A merged region is one cell
/// that spans several grid positions; the other positions are continuation cells (<see cref="TableCell.IsMergedContinuation"/>) that
/// point at it (<see cref="TableCell.Origin"/>).
/// </summary>
public sealed class Table
{
    internal Table(Shape frame, MarkupRef element)
    {
        Frame = frame;
        Markup = element;
    }

    /// <summary>Gets the graphic frame the table is in.</summary>
    public Shape Frame { get; }

    /// <summary>Gets the <c>a:tbl</c> element.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the column widths in EMU (<c>a:gridCol/@w</c>), left to right.</summary>
    public IReadOnlyList<long> ColumnWidths
    {
        get
        {
            var widths = new List<long>();
            if (Pml.A(Markup, "tblGrid") is { } grid)
            {
                foreach (MarkupRef col in Pml.Elements(grid, SchemaNamespace.Drawing, "gridCol"))
                {
                    widths.Add(Pml.AttrLong(col, "w") ?? 0);
                }
            }

            return widths;
        }
    }

    /// <summary>Gets the rows, top to bottom.</summary>
    public IReadOnlyList<TableRow> Rows
    {
        get
        {
            var rows = new List<TableRow>();
            int i = 0;
            foreach (MarkupRef tr in Pml.Elements(Markup, SchemaNamespace.Drawing, "tr"))
            {
                rows.Add(new TableRow(this, i++, tr));
            }

            return rows;
        }
    }

    /// <summary>Gets the number of rows.</summary>
    public int RowCount => Pml.Elements(Markup, SchemaNamespace.Drawing, "tr").Count;

    /// <summary>Gets the number of grid columns: the grid's columns, or the widest row when the grid is shorter.</summary>
    public int ColumnCount
    {
        get
        {
            int n = ColumnWidths.Count;
            foreach (TableRow row in Rows)
            {
                n = Math.Max(n, row.Cells.Count);
            }

            return n;
        }
    }

    /// <summary>Gets the cell at a grid position.</summary>
    /// <param name="row">The zero-based row.</param>
    /// <param name="column">The zero-based grid column.</param>
    /// <returns>The cell: the origin of a merged region sits at its top-left position and the other positions hold continuation cells.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the table.</exception>
    public TableCell this[int row, int column]
    {
        get
        {
            IReadOnlyList<TableRow> rows = Rows;
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, rows.Count);
            IReadOnlyList<TableCell> cells = rows[row].Cells;
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, cells.Count);
            return cells[column];
        }
    }

    /// <summary>Gets the table style's id (<c>a:tableStyleId</c>), or null.</summary>
    public string? StyleId => Pml.A(Markup, "tblPr") is { } pr && Pml.A(pr, "tableStyleId") is { } id ? id.Node.InnerText : null;

    /// <summary>Gets whether the first row is a header row (<c>tblPr/@firstRow</c>).</summary>
    public bool HasHeaderRow => Pml.A(Markup, "tblPr") is { } pr && Pml.AttrBool(pr, "firstRow") == true;
}

/// <summary>A row of a table (<c>a:tr</c>).</summary>
public sealed class TableRow
{
    internal TableRow(Table table, int index, MarkupRef element)
    {
        Table = table;
        Index = index;
        Markup = element;
    }

    /// <summary>Gets the table the row is in.</summary>
    public Table Table { get; }

    /// <summary>Gets the zero-based row number.</summary>
    public int Index { get; }

    /// <summary>Gets the row element.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the row height in EMU (<c>h</c>), 0 when absent.</summary>
    public long Height => Pml.AttrLong(Markup, "h") ?? 0;

    /// <summary>Gets the cells left to right, one per grid column the row writes (continuation cells of merged regions included).</summary>
    public IReadOnlyList<TableCell> Cells
    {
        get
        {
            var cells = new List<TableCell>();
            int i = 0;
            foreach (MarkupRef tc in Pml.Elements(Markup, SchemaNamespace.Drawing, "tc"))
            {
                cells.Add(new TableCell(this, i++, tc));
            }

            return cells;
        }
    }
}

/// <summary>A cell of a table (<c>a:tc</c>).</summary>
public sealed class TableCell
{
    internal TableCell(TableRow row, int column, MarkupRef element)
    {
        Row = row;
        Column = column;
        Markup = element;
    }

    /// <summary>Gets the row the cell is in.</summary>
    public TableRow Row { get; }

    /// <summary>Gets the zero-based grid column.</summary>
    public int Column { get; }

    /// <summary>Gets the cell element.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the number of grid columns the cell spans (<c>gridSpan</c>, 1 when absent).</summary>
    public int ColumnSpan => Math.Max(1, Pml.AttrInt(Markup, "gridSpan") ?? 1);

    /// <summary>Gets the number of rows the cell spans (<c>rowSpan</c>, 1 when absent).</summary>
    public int RowSpan => Math.Max(1, Pml.AttrInt(Markup, "rowSpan") ?? 1);

    /// <summary>Gets whether the cell is covered by a merged region that starts elsewhere (<c>hMerge</c> or <c>vMerge</c>).</summary>
    public bool IsMergedContinuation => Pml.AttrBool(Markup, "hMerge") == true || Pml.AttrBool(Markup, "vMerge") == true;

    /// <summary>
    /// Gets the grid position of the cell that owns this one: itself for an ordinary cell or the origin of a merged region, otherwise the
    /// cell that starts the region (found by walking left over <c>hMerge</c> cells, then up over <c>vMerge</c> cells). A continuation whose
    /// origin is missing reads as the position of the first cell the walk reaches.
    /// </summary>
    public (int Row, int Column) Origin
    {
        get
        {
            IReadOnlyList<TableRow> rows = Row.Table.Rows;
            int r = Row.Index;
            int c = Column;
            int guard = rows.Count + Row.Cells.Count + 2;
            while (guard-- > 0)
            {
                IReadOnlyList<TableCell> cells = rows[r].Cells;
                if (c < 0 || c >= cells.Count)
                {
                    break;
                }

                TableCell cell = cells[c];
                if (Pml.AttrBool(cell.Markup, "hMerge") == true && c > 0)
                {
                    c--;
                }
                else if (Pml.AttrBool(cell.Markup, "vMerge") == true && r > 0)
                {
                    r--;
                }
                else
                {
                    break;
                }
            }

            return (r, c);
        }
    }

    /// <summary>Gets the cell's text body (<c>a:txBody</c>), or null when the cell has none.</summary>
    public TextBody? TextBody =>
        Pml.A(Markup, "txBody") is { } tx ? new TextBody(Row.Table.Frame.Part, null, tx) : null;

    /// <summary>Gets the cell's text; empty when it has no text body. See <see cref="TextBody.Text"/>.</summary>
    public string Text => TextBody?.Text ?? string.Empty;

    /// <summary>Gets <c>a:tcPr</c>, the cell's own properties (borders, fill, margins), or an invalid reference.</summary>
    public MarkupRef Properties => Pml.A(Markup, "tcPr") ?? default;

    /// <summary>Replaces the cell's text, creating the text body when the cell has none. See <see cref="TextBody.SetText"/>.</summary>
    /// <param name="text">The new text.</param>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (TextBody is null)
        {
            var prefixes = new Prefixes(Markup.Node);
            Markup.Node.PrependXml(Pml.Utf8(FillTemplate("<{a}:txBody><{a}:bodyPr/><{a}:lstStyle/><{a}:p/></{a}:txBody>", prefixes)));
        }

        TextBody!.SetText(text);
    }

    private static string FillTemplate(string fragment, Prefixes prefixes) => Presentations.TextBody.Fill(fragment, prefixes);
}
