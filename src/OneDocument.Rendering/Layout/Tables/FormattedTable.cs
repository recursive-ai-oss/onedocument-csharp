using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Styles;

namespace RecursiveAI.OneDocument.Layout;

// A table block in the page flow. FormattedParagraph is the flow's block type; the table item carries the first paragraph of the table
// as a stand-in source and is told apart by its type.
internal sealed class FormattedTableBlock : FormattedParagraph
{
    public required FormattedTable Table;
}

internal sealed class CellItem
{
    public FormattedParagraph? Paragraph;
    public FormattedTable? Table;
}

// The smallest piece of cell content that can end up on one page: one line of a paragraph or one row of a nested table.
// ExtTop is where its extent starts (including the space before a block), Top where it is drawn, Bottom its lower edge.
internal readonly record struct CellUnit(int Item, int Index, double ExtTop, double Top, double Bottom);

internal sealed class FormattedCell
{
    public required LogicalCell Logical;
    public int Row;
    public int Column;
    public int ColumnSpan;
    public int RowSpan;
    public double X; // left edge relative to the table's left edge, reading order
    public double Width;
    public double PadLeft;
    public double PadRight;
    public double PadTop;
    public double PadBottom;
    public List<CellItem> Items = [];
    public List<CellUnit> Units = [];
    public double ContentHeight; // all content including the space after the last paragraph
    public string VAlign = "top";
    public uint? Shading;
    public bool IsHeader;
    public CellEdge? Top;
    public CellEdge? Bottom;
    public CellEdge? Left;
    public CellEdge? Right;
    public bool PaintRight = true;
    public bool PaintBottom = true;
    public int Cursor; // pagination state: the next unit to place

    public double InnerWidth => Math.Max(1, Width - PadLeft - PadRight);

    // Where content from unit u0 starts, relative to the content origin: the first unit keeps the space before it, later ones start at 0.
    public double Base(int u0) => u0 <= 0 || u0 >= Units.Count ? 0 : Units[u0].Top;

    // The height of units u0 (inclusive) to u1 (exclusive).
    public double Extent(int u0, int u1)
    {
        if (u1 <= u0 || Units.Count == 0)
        {
            return u0 == 0 && Units.Count == 0 ? ContentHeight : 0;
        }

        double end = u1 >= Units.Count ? ContentHeight : Units[u1 - 1].Bottom;
        return end - Base(u0);
    }

    public double Need => PadTop + Extent(0, Units.Count) + PadBottom;
}

internal sealed class FormattedRow
{
    public int Index;
    public double Height;
    public bool IsHeader;
    public bool CantSplit;
    public string Rule = "auto"; // auto, atLeast or exact
    public List<FormattedCell> Starting = [];
}

internal sealed class FormattedTable
{
    public required Table Source;
    public required LogicalTable Model;
    public double[] ColumnWidths = [];
    public double[] ColumnLeft = []; // left edge of each column relative to the table's left edge
    public double Spacing;
    public double Width;
    public double Left; // the table's left edge relative to the container's left edge (indent, alignment, outdent)
    public bool Rtl;
    public List<FormattedRow> Rows = [];
    public List<FormattedCell> Cells = [];
    public int[] GroupEnd = [];
    public int HeaderCount; // header rows at the top that repeat
    public bool Floating;
    public double FloatX;
    public double FloatY;
    public string HorizontalAnchor = "text";
    public string VerticalAnchor = "margin";
    public string? HorizontalSpec;
    public string? VerticalSpec;
}
