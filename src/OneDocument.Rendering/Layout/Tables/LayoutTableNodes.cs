using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>
/// A table on a page, or the part of a table that sits on this page. A table that breaks across pages has one
/// <see cref="LayoutTable"/> per page; the header rows repeat at the top of each continuation. A nested table is a child of its cell.
/// </summary>
public sealed class LayoutTable : LayoutNode
{
    internal LayoutTable(Table source)
        : base(LayoutNodeKind.Table)
    {
        Source = source;
    }

    /// <summary>Gets the hub table this block was laid out from.</summary>
    public Table Source { get; }

    /// <summary>Gets the grid column widths in points, left to right in grid order (the same on every page).</summary>
    public IReadOnlyList<double> ColumnWidths { get; internal set; } = [];

    /// <summary>Gets whether the table continues a table that started on an earlier page.</summary>
    public bool ContinuesFromPreviousPage { get; internal set; }

    /// <summary>Gets whether the table continues on the next page.</summary>
    public bool ContinuesOnNextPage { get; internal set; }

    /// <summary>Gets the rows of this part in order, repeated header rows first.</summary>
    public IEnumerable<LayoutTableRow> Rows => Children.OfType<LayoutTableRow>();

    internal TablePaintInfo Paint { get; set; } = new();
}

/// <summary>A row of a table, or the part of a row that sits on one page (a row can split between pages unless it says otherwise).</summary>
public sealed class LayoutTableRow : LayoutNode
{
    internal LayoutTableRow(int index)
        : base(LayoutNodeKind.TableRow)
    {
        Index = index;
    }

    /// <summary>Gets the zero-based row number in the table.</summary>
    public int Index { get; }

    /// <summary>Gets whether the row is marked as a header row (<c>w:tblHeader</c>).</summary>
    public bool IsHeader { get; internal set; }

    /// <summary>Gets whether this is a header row repeated from an earlier page rather than the row's first appearance.</summary>
    public bool IsRepeatedHeader { get; internal set; }

    /// <summary>Gets whether this is the continuation of a row that split at a page break.</summary>
    public bool ContinuesFromPreviousPage { get; internal set; }

    /// <summary>Gets whether the row continues on the next page.</summary>
    public bool ContinuesOnNextPage { get; internal set; }

    /// <summary>Gets the cells that start in this row part, in grid order.</summary>
    public IEnumerable<LayoutTableCell> Cells => Children.OfType<LayoutTableCell>();
}

/// <summary>A cell: its box (which spans every row of a vertical merge that sits on this page) and its blocks.</summary>
public sealed class LayoutTableCell : LayoutNode
{
    internal LayoutTableCell(TableCell source, int row, int column, int columnSpan, int rowSpan)
        : base(LayoutNodeKind.TableCell)
    {
        Source = source;
        Row = row;
        Column = column;
        ColumnSpan = columnSpan;
        RowSpan = rowSpan;
    }

    /// <summary>Gets the hub cell (the top-left cell of a merged region).</summary>
    public TableCell Source { get; }

    /// <summary>Gets the zero-based row of the cell's top edge.</summary>
    public int Row { get; }

    /// <summary>Gets the zero-based grid column of the cell's left edge (reading order, before any right-to-left mirroring).</summary>
    public int Column { get; }

    /// <summary>Gets the number of grid columns the cell covers.</summary>
    public int ColumnSpan { get; }

    /// <summary>Gets the number of rows the cell covers.</summary>
    public int RowSpan { get; }

    /// <summary>Gets whether the cell is in a header row.</summary>
    public bool IsHeader { get; internal set; }

    /// <summary>Gets whether the cell's content continues from the previous page.</summary>
    public bool ContinuesFromPreviousPage { get; internal set; }

    /// <summary>Gets whether the cell's content continues on the next page.</summary>
    public bool ContinuesOnNextPage { get; internal set; }

    /// <summary>Gets the paragraphs directly in the cell (not those of nested tables).</summary>
    public IEnumerable<LayoutParagraph> Paragraphs => Children.OfType<LayoutParagraph>();

    /// <summary>Gets the tables nested directly in the cell.</summary>
    public IEnumerable<LayoutTable> Tables => Children.OfType<LayoutTable>();

    internal CellPaintInfo Paint { get; set; } = new();
}

// What the painter needs about a cell beyond its box.
internal sealed class CellPaintInfo
{
    public uint? Shading;
    public CellEdge? Top;
    public CellEdge? Bottom;
    public CellEdge? Left;
    public CellEdge? Right;
    public bool Separate; // borders sit inside the cell box (cell spacing) instead of straddling its edges
    public bool PaintRight = true;
    public bool PaintBottom = true;
}

internal sealed class TablePaintInfo
{
}

// A resolved border: width in points, colour 0xRRGGBB, the w:val style name.
internal sealed record CellEdge(double Width, uint Color, string Style);
