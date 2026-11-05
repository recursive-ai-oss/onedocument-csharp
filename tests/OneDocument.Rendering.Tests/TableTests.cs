using System.Text;
using OneFont;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class TableTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    internal static LayoutDocument Lay(string body, string? sectPr = null, string? settings = null)
    {
        using Document d = TestDocx.Open(body, sectPr: sectPr, settings: settings);
        return LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts });
    }

    internal static string Tc(string content, string? tcPr = null) => TableCases.Tc(content, tcPr);

    internal static string Tr(string cells, string? trPr = null) => TableCases.Tr(cells, trPr);

    internal static string Tbl(string rows, string? tblPr = null, params int[] grid) => TableCases.Tbl(rows, tblPr, grid);

    private static double TextWidth(string text, string? rPr = null)
    {
        LayoutDocument l = Lay(TestDocx.P(text, rPr: rPr));
        return l.Pages[0].Paragraphs.First().Lines.First().Runs.Sum(r => r.Width);
    }

    private static double LineHeight
    {
        get
        {
            FontMetrics m = Fonts.Resolve(new FontRequest("Liberation Sans"))!.Face.Metrics;
            return (m.Ascender + m.LineGap - m.Descender) * 12.0 / m.UnitsPerEm;
        }
    }

    private static IReadOnlyList<LayoutTableCell> Cells(LayoutTable t) => [.. t.Rows.SelectMany(r => r.Cells)];

    // ---- widths ----

    [Fact]
    public void FixedLayoutTakesCellWidths()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("a"), "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>") + Tc(TestDocx.P("b"), "<w:tcW w:w=\"3000\" w:type=\"dxa\"/>") + Tc(TestDocx.P("c"), "<w:tcW w:w=\"4000\" w:type=\"dxa\"/>")),
            "<w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLayout w:type=\"fixed\"/>", 3000, 3000, 3000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal([100.0, 150.0, 200.0], t.ColumnWidths);
        Assert.Equal(450, t.Bounds.Width, 3);
        // Compatibility mode 12 pulls the edge out by the left cell margin so the text lines up with the margin.
        Assert.Equal(72 - 5.4, t.Bounds.X, 3);
        Assert.Equal([100.0, 150.0, 200.0], Cells(t).Select(c => c.Bounds.Width).ToArray());
    }

    [Fact]
    public void FixedLayoutWithoutCellWidthsUsesGrid()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("a")) + Tc(TestDocx.P("b"))), "<w:tblLayout w:type=\"fixed\"/>", 2000, 6000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal([100.0, 300.0], t.ColumnWidths);
    }

    [Fact]
    public void FixedLayoutScalesGridToStatedTableWidth()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("a")) + Tc(TestDocx.P("b"))), "<w:tblW w:w=\"4000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>", 1000, 3000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal(50.0, t.ColumnWidths[0], 3);
        Assert.Equal(150.0, t.ColumnWidths[1], 3);
    }

    [Fact]
    public void FixedLayoutPercentOfTableWidth()
    {
        // tcW in pct (fiftieths of a percent) is a share of the stated table width.
        string body = Tbl(Tr(Tc(TestDocx.P("a"), "<w:tcW w:w=\"1250\" w:type=\"pct\"/>") + Tc(TestDocx.P("b"), "<w:tcW w:w=\"3750\" w:type=\"pct\"/>")),
            "<w:tblW w:w=\"8000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>", 4000, 4000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal(100.0, t.ColumnWidths[0], 3);
        Assert.Equal(300.0, t.ColumnWidths[1], 3);
    }

    [Fact]
    public void AutofitFitsContent()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("Hello")) + Tc(TestDocx.P("A little longer text"))), "<w:tblW w:w=\"0\" w:type=\"auto\"/>", 2000, 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal(TextWidth("Hello") + 10.8, t.ColumnWidths[0], 2);
        Assert.Equal(TextWidth("A little longer text") + 10.8, t.ColumnWidths[1], 2);
    }

    [Fact]
    public void AutofitWrapsWhenContentIsWiderThanThePage()
    {
        string longText = string.Join(' ', Enumerable.Repeat("wordy", 120));
        string body = Tbl(Tr(Tc(TestDocx.P("short")) + Tc(TestDocx.P(longText))), "<w:tblW w:w=\"0\" w:type=\"auto\"/>", 2000, 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal(468, t.ColumnWidths.Sum(), 2);
        Assert.True(t.ColumnWidths[0] >= TextWidth("short") + 10.8 - 0.01);
        Assert.True(Cells(t)[1].Paragraphs.Single().Lines.Count() > 1);
    }

    [Fact]
    public void AutofitHonoursPreferredCellWidthAndShrinksTheOthersFirst()
    {
        string longText = string.Join(' ', Enumerable.Repeat("wordy", 120));
        string body = Tbl(Tr(Tc(TestDocx.P("fixed"), "<w:tcW w:w=\"2880\" w:type=\"dxa\"/>") + Tc(TestDocx.P(longText))), "<w:tblW w:w=\"0\" w:type=\"auto\"/>", 2880, 6000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal(144.0, t.ColumnWidths[0], 3);
        Assert.Equal(324.0, t.ColumnWidths[1], 3);
    }

    [Fact]
    public void AutofitToStatedPercentWidth()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("a")) + Tc(TestDocx.P("b"))), "<w:tblW w:w=\"2500\" w:type=\"pct\"/>", 2000, 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal(234, t.ColumnWidths.Sum(), 3);
        Assert.Equal(t.ColumnWidths[0], t.ColumnWidths[1], 3);
    }

    [Fact]
    public void AutofitToStatedDxaWidthGrowsAutoColumnsOnly()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("x"), "<w:tcW w:w=\"1440\" w:type=\"dxa\"/>") + Tc(TestDocx.P("y"))), "<w:tblW w:w=\"8640\" w:type=\"dxa\"/>", 1440, 1440);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal(432, t.ColumnWidths.Sum(), 3);
        Assert.Equal(72.0, t.ColumnWidths[0], 3);
    }

    [Fact]
    public void SpanningCellRaisesItsColumns()
    {
        string wide = "A spanning cell with a good deal of text in it";
        string body = Tbl(
            Tr(Tc(TestDocx.P("a")) + Tc(TestDocx.P("b"))) +
            Tr(Tc(TestDocx.P(wide), "<w:gridSpan w:val=\"2\"/>")),
            "<w:tblW w:w=\"0\" w:type=\"auto\"/>", 2000, 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Equal(TextWidth(wide) + 10.8, t.ColumnWidths.Sum(), 2);
        LayoutTableCell spanning = t.Rows.Last().Cells.Single();
        Assert.Equal(2, spanning.ColumnSpan);
        Assert.Equal(t.ColumnWidths.Sum(), spanning.Bounds.Width, 3);
    }

    [Fact]
    public void NestedTableFitsItsCell()
    {
        string inner = Tbl(Tr(Tc(TestDocx.P("n1")) + Tc(TestDocx.P("n2"))), "<w:tblW w:w=\"5000\" w:type=\"pct\"/>", 1000, 1000) + TestDocx.P("after");
        string body = Tbl(Tr(Tc(inner, "<w:tcW w:w=\"4000\" w:type=\"dxa\"/>") + Tc(TestDocx.P("o"))), "<w:tblW w:w=\"0\" w:type=\"auto\"/>", 4000, 1000);
        LayoutTable outer = Lay(body).Pages[0].Tables.Single();
        LayoutTableCell cell = Cells(outer)[0];
        LayoutTable nested = cell.Tables.Single();
        Assert.Equal(200.0, outer.ColumnWidths[0], 3);
        Assert.Equal(cell.Bounds.Width - 10.8, nested.Bounds.Width, 3);
        Assert.Equal(cell.Bounds.X + 5.4 - 5.4, nested.Bounds.X, 3); // mode 12: the nested table's edge is pulled out by its cell margin
        Assert.Equal("after", cell.Paragraphs.Single().Text);
        Assert.True(cell.Paragraphs.Single().Bounds.Y >= nested.Bounds.Bottom - 0.01);
    }

    [Fact]
    public void NestedPercentWidthDoesNotInflateItsColumn()
    {
        string inner = Tbl(Tr(Tc(TestDocx.P("n1")) + Tc(TestDocx.P("n2"))), "<w:tblW w:w=\"5000\" w:type=\"pct\"/>", 1000, 1000);
        string body = Tbl(Tr(Tc(inner) + Tc(TestDocx.P("The quick brown fox jumps"))), "<w:tblW w:w=\"0\" w:type=\"auto\"/>", 4000, 3000);
        LayoutTable outer = Lay(body).Pages[0].Tables.Single();
        Assert.True(outer.ColumnWidths[0] < 150);
        Assert.Equal(TextWidth("The quick brown fox jumps") + 10.8, outer.ColumnWidths[1], 2);
    }

    [Fact]
    public void CenteredAndIndentedTables()
    {
        string cellsXml = Tr(Tc(TestDocx.P("a"), "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>"));
        LayoutTable centered = Lay(Tbl(cellsXml, "<w:jc w:val=\"center\"/><w:tblLayout w:type=\"fixed\"/>", 2000)).Pages[0].Tables.Single();
        Assert.Equal(72 + ((468 - 100) / 2.0), centered.Bounds.X, 3);
        LayoutTable right = Lay(Tbl(cellsXml, "<w:jc w:val=\"right\"/><w:tblLayout w:type=\"fixed\"/>", 2000)).Pages[0].Tables.Single();
        Assert.Equal(72 + 468 - 100, right.Bounds.X, 3);
        LayoutTable indented = Lay(Tbl(cellsXml, "<w:tblInd w:w=\"720\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>", 2000)).Pages[0].Tables.Single();
        Assert.Equal(72 + 36 - 5.4, indented.Bounds.X, 3);
    }

    [Fact]
    public void ModeFifteenPlacesTheEdgeAtTheIndent()
    {
        const string settings = "<w:settings xmlns:w=\"" + TestDocx.W + "\"><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>";
        string body = Tbl(Tr(Tc(TestDocx.P("a"), "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>")), "<w:tblLayout w:type=\"fixed\"/>", 2000);
        LayoutTable t = Lay(body, settings: settings).Pages[0].Tables.Single();
        Assert.Equal(72, t.Bounds.X, 3);
    }

    // ---- cell geometry ----

    [Fact]
    public void CellMarginsAndRowHeight()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("a"), "<w:tcW w:w=\"2000\" w:type=\"dxa\"/><w:tcMar><w:top w:w=\"100\" w:type=\"dxa\"/><w:left w:w=\"200\" w:type=\"dxa\"/><w:bottom w:w=\"60\" w:type=\"dxa\"/></w:tcMar>")),
            "<w:tblLayout w:type=\"fixed\"/>", 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        LayoutTableCell c = Cells(t).Single();
        LayoutLine line = c.Paragraphs.Single().Lines.Single();
        Assert.Equal(c.Bounds.Y + 5, c.Paragraphs.Single().Bounds.Y, 3);
        Assert.Equal(c.Bounds.X + 10, line.Runs.First().Bounds.X, 3);
        Assert.Equal(5 + LineHeight + 3, t.Bounds.Height, 2);
    }

    [Fact]
    public void RowHeightRules()
    {
        string atLeast = Tbl(Tr(Tc(TestDocx.P("a")), "<w:trHeight w:val=\"1000\" w:hRule=\"atLeast\"/>") + Tr(Tc(TestDocx.P("b")), "<w:trHeight w:val=\"100\" w:hRule=\"atLeast\"/>"), "<w:tblLayout w:type=\"fixed\"/>", 2000);
        LayoutTable t = Lay(atLeast).Pages[0].Tables.Single();
        Assert.Equal(50, t.Rows.First().Bounds.Height, 3);
        Assert.Equal(LineHeight, t.Rows.Last().Bounds.Height, 2);

        string exact = Tbl(Tr(Tc(TestDocx.P("a") + TestDocx.P("b") + TestDocx.P("c") + TestDocx.P("d")), "<w:trHeight w:val=\"400\" w:hRule=\"exact\"/>"), "<w:tblLayout w:type=\"fixed\"/>", 2000);
        LayoutTable te = Lay(exact).Pages[0].Tables.Single();
        Assert.Equal(20, te.Rows.Single().Bounds.Height, 3);
        Assert.Single(Cells(te).Single().Paragraphs);
    }

    [Fact]
    public void VerticalAlignment()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("tall") + TestDocx.P("tall") + TestDocx.P("tall") + TestDocx.P("tall")) + Tc(TestDocx.P("mid"), "<w:vAlign w:val=\"center\"/>") + Tc(TestDocx.P("end"), "<w:vAlign w:val=\"bottom\"/>")),
            "<w:tblLayout w:type=\"fixed\"/>", 2000, 2000, 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        IReadOnlyList<LayoutTableCell> cells = Cells(t);
        double rowTop = t.Rows.Single().Bounds.Y;
        double rowHeight = t.Rows.Single().Bounds.Height;
        Assert.Equal(4 * LineHeight, rowHeight, 2);
        Assert.Equal(rowTop + ((rowHeight - LineHeight) / 2), cells[1].Paragraphs.Single().Bounds.Y, 2);
        Assert.Equal(rowTop + rowHeight - LineHeight, cells[2].Paragraphs.Single().Bounds.Y, 2);
    }

    [Fact]
    public void VerticalMergeSpansRows()
    {
        string body = Tbl(
            Tr(Tc(TestDocx.P("m") + TestDocx.P("m") + TestDocx.P("m"), "<w:vMerge w:val=\"restart\"/>") + Tc(TestDocx.P("r1"))) +
            Tr(Tc(TestDocx.P(string.Empty), "<w:vMerge/>") + Tc(TestDocx.P("r2"))),
            "<w:tblLayout w:type=\"fixed\"/>", 2000, 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        LayoutTableCell merged = Cells(t).Single(c => c.RowSpan == 2);
        Assert.Equal(t.Bounds.Height, merged.Bounds.Height, 3);
        // The merged cell needs three lines; the rows share the deficit by giving it to the last row.
        Assert.Equal(3 * LineHeight, merged.Bounds.Height, 2);
        Assert.Equal(LineHeight, t.Rows.First().Bounds.Height, 2);
        Assert.Equal(2 * LineHeight, t.Rows.Last().Bounds.Height, 2);
    }

    [Fact]
    public void CellSpacingSeparatesCells()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("a"), "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>") + Tc(TestDocx.P("b"), "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>")),
            "<w:tblCellSpacing w:w=\"40\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>", 2000, 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        IReadOnlyList<LayoutTableCell> cells = Cells(t);
        Assert.Equal(200 + (3 * 2), t.Bounds.Width, 3);
        Assert.Equal(t.Bounds.X + 2, cells[0].Bounds.X, 3);
        Assert.Equal(cells[0].Bounds.Right + 2, cells[1].Bounds.X, 3);
        Assert.Equal(LineHeight + 4, t.Bounds.Height, 2);
    }

    // ---- borders ----

    private static CellEdge Edge(double width, uint color, string style = "single") => new(width, color, style);

    [Fact]
    public void BorderConflictHeavierWins()
    {
        Assert.Equal(3, TableBorders.Conflict(Edge(1, 0), Edge(3, 0))!.Width);
        Assert.Equal(3, TableBorders.Conflict(Edge(3, 0), Edge(1, 0))!.Width);
        Assert.Equal("double", TableBorders.Conflict(Edge(1, 0, "double"), Edge(1.5, 0))!.Style); // 2 lines of 1 pt outweigh 1.5 pt
        Assert.NotNull(TableBorders.Conflict(null, Edge(1, 0)));
    }

    [Fact]
    public void BorderConflictTiesGoToStyleThenDarkerColour()
    {
        Assert.Equal("dashed", TableBorders.Conflict(Edge(1, 0, "dotted"), Edge(1, 0, "dashed"))!.Style);
        Assert.Equal(0x000000u, TableBorders.Conflict(Edge(1, 0xFF0000), Edge(1, 0x000000))!.Color);
        Assert.Equal(0x00FF00u, TableBorders.Conflict(Edge(1, 0x00FF00), Edge(1, 0xFFFF00))!.Color);
    }

    [Fact]
    public void SharedEdgeTakesTheHeavierBorderAndCellBordersBeatTableBorders()
    {
        string body = Tbl(
            Tr(Tc(TestDocx.P("a"), "<w:tcBorders><w:right w:val=\"single\" w:sz=\"4\" w:color=\"FF0000\"/></w:tcBorders>") +
               Tc(TestDocx.P("b"), "<w:tcBorders><w:left w:val=\"single\" w:sz=\"24\" w:color=\"0000FF\"/></w:tcBorders>") +
               Tc(TestDocx.P("c"))),
            "<w:tblBorders><w:insideV w:val=\"single\" w:sz=\"48\" w:color=\"00FF00\"/><w:top w:val=\"single\" w:sz=\"8\"/></w:tblBorders>", 2000, 2000, 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        IReadOnlyList<LayoutTableCell> c = Cells(t);
        Assert.Equal(3.0, c[0].Paint.Right!.Width);
        Assert.Equal(0x0000FFu, c[0].Paint.Right!.Color);
        Assert.Equal(3.0, c[1].Paint.Left!.Width);
        // Between b and c nothing is set on the cells, so the table's inside border applies.
        Assert.Equal(6.0, c[1].Paint.Right!.Width);
        Assert.Equal(0x00FF00u, c[2].Paint.Left!.Color);
        Assert.Equal(1.0, c[0].Paint.Top!.Width);
        Assert.Null(c[0].Paint.Bottom);
        Assert.False(c[0].Paint.PaintRight);
        Assert.True(c[2].PaintRightForTest());
    }

    [Fact]
    public void ExplicitNoneOnACellRemovesTheTableBorder()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("a"), "<w:tcBorders><w:bottom w:val=\"nil\"/></w:tcBorders>")),
            "<w:tblBorders><w:bottom w:val=\"single\" w:sz=\"8\"/></w:tblBorders>", 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        Assert.Null(Cells(t).Single().Paint.Bottom);
    }

    [Fact]
    public void BordersAddToCellPadding()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("a"), "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>")),
            "<w:tblBorders><w:top w:val=\"single\" w:sz=\"16\"/><w:bottom w:val=\"single\" w:sz=\"16\"/><w:left w:val=\"single\" w:sz=\"16\"/><w:right w:val=\"single\" w:sz=\"16\"/></w:tblBorders><w:tblLayout w:type=\"fixed\"/>", 2000);
        LayoutTable t = Lay(body).Pages[0].Tables.Single();
        LayoutTableCell c = Cells(t).Single();
        Assert.Equal(c.Bounds.X + 5.4 + 1, c.Paragraphs.Single().Lines.Single().Runs.First().Bounds.X, 3);
        Assert.Equal(LineHeight + 2, t.Bounds.Height, 2);
    }

    // ---- styles ----

    [Fact]
    public void TableStyleShadingAndBordersApplyByRegion()
    {
        string styles = TestDocx.DefaultStyles.Replace("</w:styles>",
            "<w:style w:type=\"table\" w:styleId=\"Grid\"><w:name w:val=\"Grid\"/><w:tblPr><w:tblBorders><w:insideH w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr>" +
            "<w:tblStylePr w:type=\"firstRow\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"DDDDDD\"/></w:tcPr></w:tblStylePr></w:style></w:styles>", StringComparison.Ordinal);
        string body = Tbl(Tr(Tc(TestDocx.P("h"))) + Tr(Tc(TestDocx.P("b"))), "<w:tblStyle w:val=\"Grid\"/><w:tblLook w:val=\"0420\" w:firstRow=\"1\"/><w:tblLayout w:type=\"fixed\"/>", 2000);
        using Document d = Document.Open(new MemoryStream(TestDocx.Bytes(body, styles)), leaveOpen: false);
        LayoutTable t = LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts }).Pages[0].Tables.Single();
        IReadOnlyList<LayoutTableCell> c = Cells(t);
        Assert.Equal(0xDDDDDDu, c[0].Paint.Shading);
        Assert.Null(c[1].Paint.Shading);
        Assert.Equal(0.5, c[0].Paint.Bottom!.Width);
    }

    // ---- pagination ----

    private static string Rows(int count, string? trPr = null, string? text = null) =>
        string.Concat(Enumerable.Range(0, count).Select(i => Tr(Tc(TestDocx.P(text ?? "row " + i)), trPr)));

    [Fact]
    public void HeaderRowsRepeatOnEachPage()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("Header")), "<w:tblHeader/>") + Rows(100), "<w:tblLayout w:type=\"fixed\"/>", 4000);
        LayoutDocument l = Lay(body);
        Assert.True(l.Pages.Count >= 3);
        int bodyRows = 0;
        for (int p = 0; p < l.Pages.Count; p++)
        {
            LayoutTable t = l.Pages[p].Tables.Single();
            List<LayoutTableRow> rows = [.. t.Rows];
            Assert.Equal(0, rows[0].Index);
            Assert.True(rows[0].IsHeader);
            Assert.Equal(p > 0, rows[0].IsRepeatedHeader);
            Assert.Equal(p > 0, t.ContinuesFromPreviousPage);
            Assert.Equal(p < l.Pages.Count - 1, t.ContinuesOnNextPage);
            bodyRows += rows.Count(r => !r.IsRepeatedHeader && r.Index > 0);
            Assert.True(t.Bounds.Bottom <= l.Pages[p].ContentArea.Bottom + 0.01);
        }

        Assert.Equal(100, bodyRows);
    }

    [Fact]
    public void RowsSplitAcrossPagesLineByLine()
    {
        string tall = string.Concat(Enumerable.Range(0, 100).Select(i => TestDocx.P("line " + i)));
        string body = Tbl(Tr(Tc(tall)), "<w:tblLayout w:type=\"fixed\"/>", 4000);
        LayoutDocument l = Lay(body);
        Assert.True(l.Pages.Count >= 2);
        var lines = new List<string>();
        for (int p = 0; p < l.Pages.Count; p++)
        {
            LayoutTableRow row = l.Pages[p].Tables.Single().Rows.Single();
            Assert.Equal(p > 0, row.ContinuesFromPreviousPage);
            Assert.Equal(p < l.Pages.Count - 1, row.ContinuesOnNextPage);
            lines.AddRange(row.Cells.Single().Paragraphs.Select(x => x.Text));
        }

        Assert.Equal(Enumerable.Range(0, 100).Select(i => "line " + i), lines);
    }

    [Fact]
    public void CantSplitMovesTheWholeRowToTheNextPage()
    {
        string filler = string.Concat(Enumerable.Range(0, 40).Select(i => TestDocx.P("filler " + i)));
        string tall = string.Concat(Enumerable.Range(0, 10).Select(i => TestDocx.P("cell " + i)));
        string Table(string? trPr) => Tbl(Tr(Tc(tall), trPr), "<w:tblLayout w:type=\"fixed\"/>", 4000);

        LayoutDocument split = Lay(filler + Table(null));
        Assert.Equal(2, split.Pages.Count);
        Assert.True(split.Pages[0].Tables.Single().Rows.Single().ContinuesOnNextPage);

        LayoutDocument kept = Lay(filler + Table("<w:cantSplit/>"));
        Assert.Equal(2, kept.Pages.Count);
        Assert.Empty(kept.Pages[0].Tables);
        LayoutTableRow row = kept.Pages[1].Tables.Single().Rows.Single();
        Assert.False(row.ContinuesFromPreviousPage);
        Assert.Equal(10, row.Cells.Single().Paragraphs.Count());
    }

    [Fact]
    public void CantSplitRowTallerThanAPageStillSplits()
    {
        string tall = string.Concat(Enumerable.Range(0, 100).Select(i => TestDocx.P("cell " + i)));
        LayoutDocument l = Lay(Tbl(Tr(Tc(tall), "<w:cantSplit/>"), "<w:tblLayout w:type=\"fixed\"/>", 4000));
        Assert.True(l.Pages.Count >= 2);
        Assert.Equal(100, l.Pages.Sum(p => p.Tables.Sum(t => t.Rows.Sum(r => r.Cells.Sum(c => c.Paragraphs.Count())))));
    }

    [Fact]
    public void BrokenVerticalMergeKeepsAllOfItsContent()
    {
        string merged = string.Concat(Enumerable.Range(0, 120).Select(i => TestDocx.P("m" + i)));
        string body = Tbl(
            Tr(Tc(merged, "<w:vMerge w:val=\"restart\"/>") + Tc(TestDocx.P("r1"))) +
            Tr(Tc(TestDocx.P(string.Empty), "<w:vMerge/>") + Tc(TestDocx.P("r2"))),
            "<w:tblLayout w:type=\"fixed\"/>", 2000, 2000);
        LayoutDocument l = Lay(body);
        var texts = l.Pages.SelectMany(p => p.Tables).SelectMany(t => t.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Paragraphs).Select(p => p.Text).Where(x => x.StartsWith('m')).ToList();
        Assert.Equal(Enumerable.Range(0, 120).Select(i => "m" + i), texts);
    }

    [Fact]
    public void ParagraphsFollowTheTable()
    {
        string body = TestDocx.P("before") + Tbl(Tr(Tc(TestDocx.P("a"))), "<w:tblLayout w:type=\"fixed\"/>", 2000) + TestDocx.P("after");
        LayoutPage page = Lay(body).Pages[0];
        LayoutTable t = page.Tables.Single();
        List<LayoutParagraph> paras = [.. page.Paragraphs];
        Assert.Equal(["before", "after"], paras.Select(p => p.Text));
        Assert.Equal(paras[0].Bounds.Bottom, t.Bounds.Y, 2);
        Assert.Equal(t.Bounds.Bottom, paras[1].Bounds.Y, 2);
    }

    [Fact]
    public void FloatingTableIsPaintedAtItsPositionWithoutTakingRoom()
    {
        string body = TestDocx.P("text") + Tbl(Tr(Tc(TestDocx.P("f"), "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>")),
            "<w:tblpPr w:horzAnchor=\"page\" w:vertAnchor=\"page\" w:tblpX=\"2000\" w:tblpY=\"3000\"/><w:tblLayout w:type=\"fixed\"/>", 2000) + TestDocx.P("more");
        LayoutDocument l = Lay(body);
        LayoutTable t = l.Pages[0].Tables.Single();
        Assert.Equal(100, t.Bounds.X, 3);
        Assert.Equal(150, t.Bounds.Y, 3);
        List<LayoutParagraph> paras = [.. l.Pages[0].Paragraphs];
        Assert.Equal(paras[0].Bounds.Bottom, paras[1].Bounds.Y, 2);
    }

    // ---- painting ----

    [Fact]
    public void TaggedTablePaintsAndWritesPdf()
    {
        string body = Tbl(Tr(Tc(TestDocx.P("Name")) + Tc(TestDocx.P("Value")), "<w:tblHeader/>") + Rows(80),
            "<w:tblBorders><w:top w:val=\"single\" w:sz=\"8\"/><w:insideH w:val=\"double\" w:sz=\"8\"/><w:insideV w:val=\"dashed\" w:sz=\"8\"/></w:tblBorders><w:tblLayout w:type=\"fixed\"/>", 2000, 2000);
        using Document d = TestDocx.Open(body);
        LayoutDocument l = LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts });
        Assert.True(l.Pages.Count >= 2);
        foreach (LayoutPage p in l.Pages)
        {
            Assert.NotNull(LayoutPainter.Paint(p));
        }

        using var ms = new MemoryStream();
        DocumentRenderer.WritePdf(l, d, ms);
        Assert.True(ms.Length > 1000);
    }

    [Fact]
    public void ManyNestedLevelsAreBounded()
    {
        string inner = TestDocx.P("deep");
        for (int i = 0; i < 9; i++)
        {
            inner = Tbl(Tr(Tc(inner)), "<w:tblLayout w:type=\"fixed\"/>", 4000);
        }

        LayoutDocument l = Lay(inner + TestDocx.P("end"));
        Assert.Contains(l.Notes, n => n.Code == "layout.table-depth");
    }
}

internal static class TableTestExtensions
{
    public static bool PaintRightForTest(this LayoutTableCell c) => c.Paint.PaintRight;
}
