using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>Something the layout skipped or approximated, with where.</summary>
/// <param name="Code">A <c>sheet-layout.*</c> code.</param>
/// <param name="Message">What happened.</param>
/// <param name="Location">The sheet name and address, or null.</param>
public sealed record SheetRenderNote(string Code, string Message, string? Location);

/// <summary>Everything the layout of one workbook shares: fonts, text engine, style cache, defaults of the default font.</summary>
internal sealed class RenderContext
{
    public RenderContext(Workbook workbook, SheetRenderOptions options, IFontProvider fonts)
    {
        Workbook = workbook;
        Options = options;
        Styles = new StyleCache(workbook);
        Text = new TextEngine(fonts);
        TextStyle normal = Styles.Get(0).Text;
        DefaultStyle = normal;
        // A column's width is counted in digits of the default font: Excel measures the widest digit at 96 dpi in whole pixels.
        // ECMA-376 Part 1 §18.3.1.13 (col): width = Truncate([{Number of Characters} * {Maximum Digit Width} + {5 pixel padding}] / {Maximum Digit Width} * 256) / 256.
        double widest = 0;
        for (char d = '0'; d <= '9'; d++)
        {
            widest = Math.Max(widest, Text.Measure(d.ToString(), normal));
        }

        MaxDigitWidth = Math.Max(1, Math.Round(widest * 96 / 72));
        (double asc, double desc, double gap) = Text.LineMetrics(normal);
        // Excel's default row: the font's ascent, descent and line gap each rounded to whole pixels, plus 2 pixels.
        double px = Math.Round(asc * 96 / 72) + Math.Round(desc * 96 / 72) + Math.Round(gap * 96 / 72) + 2;
        DefaultRowHeightPoints = px * 0.75;
    }

    public Workbook Workbook { get; }

    public SheetRenderOptions Options { get; }

    public StyleCache Styles { get; }

    public TextEngine Text { get; }

    public TextStyle DefaultStyle { get; }

    public double MaxDigitWidth { get; }

    public double DefaultRowHeightPoints { get; }

    public List<SheetRenderNote> Notes { get; } = [];

    public char[] Scratch { get; } = new char[1024];

    public int TotalPages { get; set; }

    public void Note(string code, string message, string? location = null)
    {
        if (Notes.Count < 1000)
        {
            Notes.Add(new SheetRenderNote(code, message, location));
        }
    }

    // Pixel width of a stored column width (characters including padding), then points.
    // [MS-OI29500]-style rounding: pixels = Truncate(((256 * width + Truncate(128 / MDW)) / 256) * MDW).
    public double ColumnWidthPoints(double storedWidth)
    {
        double mdw = MaxDigitWidth;
        double px = Math.Truncate((((256 * storedWidth) + Math.Truncate(128 / mdw)) / 256) * mdw);
        return px * 0.75;
    }

    public double DefaultColumnWidthPoints(SheetPrintSettings s)
    {
        if (s.DefaultColumnWidth > 0)
        {
            return ColumnWidthPoints(s.DefaultColumnWidth);
        }

        // ECMA-376 Part 1 §18.3.1.81 (sheetFormatPr): the default width is baseColWidth characters plus the 5 pixels of padding and gridline,
        // which Excel rounds up to a whole number of 8-pixel steps (8 characters of a 7-pixel digit is 61 pixels, drawn as 64).
        double px = Math.Ceiling(((s.BaseColumnWidth * MaxDigitWidth) + 5) / 8) * 8;
        return px * 0.75;
    }
}

/// <summary>The sizes of the rows and columns a sheet prints, in points before scaling, with prefix sums for positions.</summary>
internal sealed class SheetGeometry
{
    private readonly double[] _rowTop;
    private readonly double[] _colLeft;

    public SheetGeometry(int row0, int row1, int col0, int col1, double[] rowHeights, double[] colWidths)
    {
        Row0 = row0;
        Row1 = row1;
        Col0 = col0;
        Col1 = col1;
        _rowTop = new double[rowHeights.Length + 1];
        for (int i = 0; i < rowHeights.Length; i++)
        {
            _rowTop[i + 1] = _rowTop[i] + rowHeights[i];
        }

        _colLeft = new double[colWidths.Length + 1];
        for (int i = 0; i < colWidths.Length; i++)
        {
            _colLeft[i + 1] = _colLeft[i] + colWidths[i];
        }
    }

    public int Row0 { get; }

    public int Row1 { get; }

    public int Col0 { get; }

    public int Col1 { get; }

    public bool Covers(int row, int col) => row >= Row0 && row <= Row1 && col >= Col0 && col <= Col1;

    public double RowTop(int row) => _rowTop[Math.Clamp(row - Row0, 0, _rowTop.Length - 1)];

    public double RowHeight(int row) => row < Row0 || row > Row1 ? 0 : _rowTop[row - Row0 + 1] - _rowTop[row - Row0];

    public double ColLeft(int col) => _colLeft[Math.Clamp(col - Col0, 0, _colLeft.Length - 1)];

    public double ColWidth(int col) => col < Col0 || col > Col1 ? 0 : _colLeft[col - Col0 + 1] - _colLeft[col - Col0];

    // Size of rows [a, b] / columns [a, b].
    public double RowsHeight(int a, int b) => b < a ? 0 : RowTop(b + 1) - RowTop(a);

    public double ColsWidth(int a, int b) => b < a ? 0 : ColLeft(b + 1) - ColLeft(a);
}
