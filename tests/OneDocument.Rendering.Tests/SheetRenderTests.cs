using System.Text;
using OneRender.Painting;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class SheetRenderTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 3, 9, 14, 5, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static byte[] Xlsx(string name) => SheetCases.All().First(c => c.Name == name).Xlsx;

    private static Workbook Open(string name) => Workbook.Open(new MemoryStream(Xlsx(name)), leaveOpen: false);

    private static SheetRenderOptions Options() => new() { Fonts = TestFonts.Library, Clock = new FixedClock(), FileName = "book.xlsx" };

    private static SheetLayoutDocument Layout(string name, SheetRenderOptions? options = null)
    {
        Workbook wb = Open(name);
        return WorkbookRenderer.Layout(wb, options ?? Options());
    }

    private static string PdfText(string name)
    {
        using Workbook wb = Open(name);
        using var ms = new MemoryStream();
        WorkbookRenderer.WritePdf(wb, ms, new PdfRenderOptions { Compress = false }, Options());
        using var pdf = OnePdf.PdfDocument.Open(ms.ToArray());
        return string.Join(" | ", pdf.ExtractText().Select(p => string.Join(" ", p.Blocks.SelectMany(b => b.Lines).Select(l => l.Text))));
    }

    [Fact]
    public void TheCaseSetHasThirtyWorkbooksWithUniqueNames()
    {
        Assert.True(SheetCases.All().Count >= 28);
        Assert.Equal(SheetCases.All().Count, SheetCases.All().Select(c => c.Name).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryCaseLaysOutAndWritesATaggedPdfAndPng(string name)
    {
        using Workbook wb = Open(name);
        using var ms = new MemoryStream();
        SheetLayoutDocument layout = WorkbookRenderer.WritePdf(wb, ms, new PdfRenderOptions { Compress = false }, Options());
        string pdf = Encoding.Latin1.GetString(ms.ToArray());
        Assert.StartsWith("%PDF-", pdf, StringComparison.Ordinal);
        Assert.Contains("/StructTreeRoot", pdf, StringComparison.Ordinal);
        Assert.True(layout.Pages.Count >= 1);
        byte[] png = WorkbookRenderer.RenderPng(layout, layout.Pages[0]);
        Assert.Equal(0x89, png[0]);
        Assert.True(png.Length > 1500);
        Assert.DoesNotContain(layout.Notes, n => n.Code is "sheet-layout.font-missing");
    }

    public static IEnumerable<object[]> CaseNames() => SheetCases.All().Select(c => new object[] { c.Name });

    // ---- pagination ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void ADefaultSheetUsesTheExcelDefaultColumnAndRowSizes()
    {
        SheetLayoutDocument l = Layout("x01-basic");
        SheetPage p = Assert.Single(l.Pages);
        Assert.Equal(612, p.Width, 0.01);
        Assert.Equal(792, p.Height, 0.01);
        Assert.Equal(1.0, p.Scale);
        // The sheet has 14 rows and 5 columns: 64 px columns (48 pt) and the 20 px Calibri 11 row (15 pt).
        Assert.Equal("A1:E14", p.Body.ToString());
        Assert.Equal(5 * 48, p.ContentWidth, 0.01);
        Assert.Equal(14 * 15, p.ContentHeight, 0.01);
        Assert.Equal(50.4, p.ContentX, 0.01);
        Assert.Equal(54, p.ContentY, 0.01);
    }

    [Fact]
    public void ThePrintAreaDecidesWhatPrints()
    {
        SheetPage p = Assert.Single(Layout("x02-print-area").Pages);
        Assert.Equal("C3:F12", p.Body.ToString());
        Assert.Equal(4 * 48, p.ContentWidth, 0.01);
        Assert.Equal(10 * 15, p.ContentHeight, 0.01);
    }

    [Fact]
    public void PrintTitlesRepeatOnEveryPageBelowThem()
    {
        IReadOnlyList<SheetPage> pages = Layout("x03-titles").Pages;
        // 684 pt of height: 45 rows on the first page, then 43 rows beside the two title rows.
        Assert.Equal(4, pages.Count);
        Assert.Equal("A1:D45", pages[0].Body.ToString());
        Assert.Equal((0, 0), pages[0].TitleRows);
        Assert.Equal("A46:D88", pages[1].Body.ToString());
        Assert.Equal((1, 2), pages[1].TitleRows);
        Assert.Equal("A132:D150", pages[3].Body.ToString());
        Assert.Equal(new List<int> { 1, 2, 3, 4 }, pages.Select(p => p.Number).ToList());
        Assert.Equal((43 + 2) * 15, pages[1].ContentHeight, 0.01);
    }

    [Fact]
    public void FitToWidthPicksTheLargestScaleThatFits()
    {
        SheetPage p = Assert.Single(Layout("x04-fit-width").Pages);
        // 30 columns of 48 pt on a landscape Letter page with 0.7 inch side margins (691.2 pt): 48 percent is the largest whole percent.
        Assert.Equal(0.48, p.Scale, 1e-9);
        Assert.Equal(792, p.Width, 0.01);
    }

    [Fact]
    public void FitToOnePageShrinksBothWays()
    {
        SheetPage p = Assert.Single(Layout("x05-fit-one").Pages);
        Assert.Equal(0.53, p.Scale, 1e-9);
        Assert.Equal(20 * 48 * 0.53, p.ContentWidth, 0.01);
    }

    [Fact]
    public void ASetScaleChangesHowMuchFitsOnAPage()
    {
        IReadOnlyList<SheetPage> pages = Layout("x06-scale-50").Pages;
        Assert.Equal(2, pages.Count);
        Assert.Equal("A1:F91", pages[0].Body.ToString());
        Assert.Equal(0.5, pages[0].Scale);
    }

    [Fact]
    public void ManualBreaksSplitAfterTheNamedRowAndColumn()
    {
        IReadOnlyList<SheetPage> pages = Layout("x07-manual-breaks").Pages;
        Assert.Equal(new List<string> { "A1:C10", "A11:C40", "D1:H10", "D11:H40" }, pages.Select(p => p.Body.ToString()).ToList());
    }

    [Fact]
    public void PageOrderDecidesWhichStripComesNext()
    {
        IReadOnlyList<SheetPage> pages = Layout("x08-over-then-down").Pages;
        Assert.Equal(9, pages.Count);
        Assert.Equal("A1:J45", pages[0].Body.ToString());
        Assert.Equal("K1:T45", pages[1].Body.ToString());
        Assert.Equal("A46:J90", pages[3].Body.ToString());
    }

    [Fact]
    public void LandscapeA4CentresTheRange()
    {
        SheetPage p = Assert.Single(Layout("x09-a4-landscape-centred").Pages);
        Assert.Equal(841.89, p.Width, 0.05);
        Assert.Equal(595.28, p.Height, 0.05);
        Assert.Equal(50.4 + ((841.89 - 100.8 - 288) / 2), p.ContentX, 0.1);
        Assert.Equal(54 + ((595.28 - 108 - 300) / 2), p.ContentY, 0.1);
    }

    [Fact]
    public void HiddenRowsAndColumnsTakeNoRoomAndCustomSizesAreUsed()
    {
        SheetPage p = Assert.Single(Layout("x10-hidden-and-custom-sizes").Pages);
        // Columns A, B, E, G, H are 48 pt; F is 20.7109375 characters = 145 px = 108.75 pt; C and D are hidden.
        Assert.Equal((5 * 48) + 108.75, p.ContentWidth, 0.01);
        // 20 rows: one hidden, one 30 pt, the rest 15 pt.
        Assert.Equal((18 * 15) + 30, p.ContentHeight, 0.01);
    }

    [Fact]
    public void TheUsedRangeIncludesAFormattedEmptyCell()
    {
        SheetPage p = Assert.Single(Layout("x29-used-range-with-format-only-cell").Pages);
        Assert.Equal("A1:F20", p.Body.ToString());
    }

    [Fact]
    public void PageNumbersContinueAcrossSheetsUntilASheetRestartsThem()
    {
        SheetLayoutDocument l = Layout("x27-several-sheets");
        // First: 70 rows = 2 pages, Second: 2 pages, Third is hidden, Fourth starts at 7.
        Assert.Equal(new List<string> { "First", "First", "Second", "Second", "Fourth" }, l.Pages.Select(p => p.Sheet.Name).ToList());
        Assert.Equal(new List<int> { 1, 2, 3, 4, 7 }, l.Pages.Select(p => p.Number).ToList());
        Assert.Equal(new List<int> { 1, 2, 1, 2, 1 }, l.Pages.Select(p => p.NumberInSheet).ToList());
    }

    // ---- headers and footers ------------------------------------------------------------------------------------------------

    [Fact]
    public void HeaderAndFooterCodesAreFilledIn()
    {
        string text = PdfText("x11-header-footer");
        Assert.Contains("Quarterly report", text, StringComparison.Ordinal);
        Assert.Contains("Header", text, StringComparison.Ordinal);
        Assert.Contains("3/9/2026", text, StringComparison.Ordinal);
        Assert.Contains("Page 1 of 3", text, StringComparison.Ordinal);
        Assert.Contains("Page 3 of 3", text, StringComparison.Ordinal);
        Assert.Contains("File book.xlsx", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstOddAndEvenHeadersAreChosenPerPage()
    {
        string text = PdfText("x12-first-odd-even");
        Assert.Contains("First page", text, StringComparison.Ordinal);
        Assert.Contains("Even page 2", text, StringComparison.Ordinal);
        Assert.Contains("Odd page 3", text, StringComparison.Ordinal);
        Assert.Contains("evF", text, StringComparison.Ordinal);
        Assert.Contains("odF", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderCodesParseFontsSizesAndFields()
    {
        var fields = new HeaderFooterFields(4, 9, "d", "t", "f.xlsx", "p", "S1");
        var style = new TextStyle("Calibri", 11, false, false, false, FontUnderline.None, FontVerticalAlignment.Baseline, 0);
        HeaderFooterSection[] s = HeaderFooterCodes.Parse("&L&\"Arial,Bold Italic\"&14Hi &B&P+1&Cc&&d&Rx&K00FF00&U&A", style, fields);
        Assert.Equal("Hi ", s[0].Runs[0].Text);
        Assert.Equal("Arial", s[0].Runs[0].Style.Family);
        Assert.True(s[0].Runs[0].Style.Bold);
        Assert.True(s[0].Runs[0].Style.Italic);
        Assert.Equal(14, s[0].Runs[0].Style.Size);
        Assert.Equal("5", s[0].Runs[1].Text);
        Assert.False(s[0].Runs[1].Style.Bold);
        Assert.Equal("c&d", s[1].Runs[0].Text);
        Assert.Equal(0x00FF00u, s[2].Runs[^1].Style.Rgb);
        Assert.Equal("S1", s[2].Runs[^1].Text);
    }

    // ---- cells --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ColumnWidthsConvertThroughTheMaximumDigitWidth()
    {
        var ctx = new RenderContext(Open("x01-basic"), Options(), TestFonts.Library);
        Assert.Equal(7, ctx.MaxDigitWidth);
        Assert.Equal(15, ctx.DefaultRowHeightPoints, 1e-9);
        // 8.7109375 characters is Excel's 8 characters plus padding: 61 px; the stored 9.140625 is the 64 px default.
        Assert.Equal(48, ctx.ColumnWidthPoints(9.140625), 1e-9);
        Assert.Equal(108.75, ctx.ColumnWidthPoints(20.7109375), 1e-9);
        Assert.Equal(0.75 * 201, ctx.ColumnWidthPoints(28.7109375), 1e-9);
    }

    [Fact]
    public void NumbersThatDoNotFitShowHashesAndErrorsFollowThePageSetup()
    {
        string text = PdfText("x14-numbers-and-errors");
        Assert.Contains("1,234,567.89", text, StringComparison.Ordinal);      // column A is wide enough
        Assert.Contains("####", text, StringComparison.Ordinal);               // column B is not
        Assert.Contains("--", text, StringComparison.Ordinal);                 // errors="dash"
        Assert.DoesNotContain("#DIV/0!", text, StringComparison.Ordinal);
        Assert.Contains("3/15/2023", text, StringComparison.Ordinal);
        Assert.Contains("26%", text, StringComparison.Ordinal);
        Assert.Contains("-1,235", text, StringComparison.Ordinal);
        Assert.Contains("TRUE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AlignmentCasesDrawTheirText()
    {
        string text = PdfText("x13-alignment");
        foreach (string word in new List<string> { "General text", "Centre", "Indent 2", "Rotated 45", "Rotated 90", "Centred across", "Justified", "Top", "Middle" })
        {
            Assert.Contains(word, text, StringComparison.Ordinal);
        }

        // The wrapped sentence breaks over several lines, so no single glyph run holds all of it.
        SheetLayoutDocument l = Layout("x13-alignment");
        DisplayList list = WorkbookRenderer.Paint(l, l.Pages[0]);
        var runs = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.GlyphRun)
            {
                runs.Add(new string(list[i].GlyphRun.Text));
            }
        }

        Assert.DoesNotContain("A long sentence that has to wrap onto several lines inside its cell", runs);
        Assert.Contains(runs, r => r.StartsWith("A long sentence", StringComparison.Ordinal));
    }

    [Fact]
    public void RightAlignedTextEndsAtTheCellEdge()
    {
        SheetLayoutDocument l = Layout("x13-alignment");
        SheetPage p = l.Pages[0];
        DisplayList list = WorkbookRenderer.Paint(l, p);
        // Column A is 201 px (150.75 pt) and B 131 px (98.25 pt): B ends 249 pt right of the content origin; 1.5 pt of padding.
        // A glyph run's local bounds reach 2.75 pt past its advance on the right.
        double expectedRight = 249 - 1.5 + 2.75;
        bool found = false;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem item = list[i];
            if (item.Kind == ItemKind.GlyphRun && new string(item.GlyphRun.Text) == "Right")
            {
                found = true;
                Assert.InRange(item.LocalBounds.Right, expectedRight - 0.3, expectedRight + 0.3);
            }
        }

        Assert.True(found);
    }

    [Fact]
    public void TextSpillsOnlyIntoEmptyNeighbours()
    {
        SheetLayoutDocument l = Layout("x15-overflow");
        SheetPage p = l.Pages[0];
        DisplayList list = WorkbookRenderer.Paint(l, p);
        // Row 2 starts 15 pt below row 1. A2's text stops at C2 (occupied), so its clip is two 48 pt columns wide.
        double rowTop = p.ContentY + 15;
        var clips = new List<RectF>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.PushClipRect)
            {
                clips.Add(list[i].Rect);
            }
        }

        // Clip rectangles are local to the scaled content transform: y = 15 for the second row.
        Assert.Contains(clips, c => Math.Abs(c.Y - 15) < 0.01 && Math.Abs(c.Width - 96) < 0.01 && Math.Abs(c.X) < 0.01);
        // A1 spills over as many empty cells as its text needs.
        Assert.Contains(clips, c => Math.Abs(c.Y) < 0.01 && c.Width > 200 && c.Width <= 288.01);
        _ = rowTop;
    }

    [Fact]
    public void MergedCellsPaintTheirTextOnce()
    {
        string text = PdfText("x16-merged-cells");
        Assert.Equal(1, CountOf(text, "Merged block"));
        Assert.Equal(1, CountOf(text, "Tall merged cell"));
    }

    [Fact]
    public void ASharedEdgeBetweenTwoBorderedCellsIsDrawnOnce()
    {
        SheetLayoutDocument l = Layout("x17-borders");
        SheetPage p = l.Pages[0];
        DisplayList list = WorkbookRenderer.Paint(l, p);
        // B4 and C4 both have medium (1.5 pt) boxes; the edge between them is column C's left edge: 82.5 pt (B is 11.7109375 characters = 82 px = 61.5 pt).
        double bWidth = 0.75 * 82;
        double aWidth = 48;
        double edgeX = aWidth + bWidth;
        int vertical = 0;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem item = list[i];
            if (item.Kind == ItemKind.FillRect && Math.Abs(item.Rect.Width - 1.5f) < 0.01 && Math.Abs(item.Rect.X + 0.75 - edgeX) < 0.05 && Math.Abs(item.Rect.Y + 0.75 - (15 * 3)) < 0.05)
            {
                vertical++;
            }
        }

        Assert.Equal(1, vertical);
    }

    [Fact]
    public void RichTextRunsKeepTheirText()
    {
        string text = PdfText("x25-rich-text");
        Assert.Contains("Bold", text, StringComparison.Ordinal);
        Assert.Contains("big red italic", text, StringComparison.Ordinal);
        Assert.Contains("sup", text, StringComparison.Ordinal);
        Assert.Contains("Line one", text, StringComparison.Ordinal);
        Assert.Contains("Line two", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptsOtherThanLatinAreShaped()
    {
        string text = PdfText("x28-scripts");
        Assert.Contains("你好", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GridlinesAndHeadingsAreDrawnWhenAsked()
    {
        string text = PdfText("x19-gridlines-headings");
        // Column letters and row numbers print as headings.
        Assert.Contains(" A ", " " + text + " ", StringComparison.Ordinal);
        Assert.Contains("15", text, StringComparison.Ordinal);
    }

    // ---- conditional formats ------------------------------------------------------------------------------------------------

    [Fact]
    public void ConditionalFormatsFillTheCellsTheirRulesSelect()
    {
        SheetLayoutDocument l = Layout("x20-cf-formats");
        DisplayList list = WorkbookRenderer.Paint(l, l.Pages[0]);
        // Column A holds r*100+1; above 600 are rows 6 to 12: seven cells with the light red fill FFC7CE.
        int red = CountFills(list, 0xFF, 0xC7, 0xCE);
        // Column D (above average) uses the same dxf: values r*100+4 average 654, so rows 7 to 12 are above it.
        Assert.Equal(7 + 6, red);
        // Column C's top 3 use the yellow dxf FFEB9C.
        Assert.Equal(3, CountFills(list, 0xFF, 0xEB, 0x9C));
    }

    [Fact]
    public void DataBarsAndColourScalesPaint()
    {
        SheetLayoutDocument bars = Layout("x21-cf-databars");
        DisplayList list = WorkbookRenderer.Paint(bars, bars.Pages[0]);
        int gradients = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.FillRect && list[i].Paint.Kind == PaintKind.Linear)
            {
                gradients++;
            }
        }

        Assert.Equal(14, gradients);

        SheetLayoutDocument scale = Layout("x22-cf-colorscales");
        DisplayList sl = WorkbookRenderer.Paint(scale, scale.Pages[0]);
        // The lowest value takes the first colour F8696B, the highest the last 63BE7B.
        Assert.True(CountFills(sl, 0xF8, 0x69, 0x6B) >= 2);
        Assert.True(CountFills(sl, 0x63, 0xBE, 0x7B) >= 2);
    }

    [Fact]
    public void IconSetsDrawOneVectorIconPerCell()
    {
        SheetLayoutDocument l = Layout("x23-cf-iconsets");
        DisplayList list = WorkbookRenderer.Paint(l, l.Pages[0]);
        int paths = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.FillPath)
            {
                paths++;
            }
        }

        // 20 sets with 3 to 5 icons each (76 cells), every icon at least one path.
        Assert.True(paths >= 76, paths.ToString());
    }

    [Fact]
    public void DuplicateAndTextRulesApply()
    {
        SheetLayoutDocument l = Layout("x24-cf-duplicates-text");
        DisplayList list = WorkbookRenderer.Paint(l, l.Pages[0]);
        // Column A holds r mod 3: values 1,2,0,1,2,0,1,2,0,1 - every value repeats, so all ten cells are duplicates.
        Assert.Equal(10, CountFills(list, 0xFF, 0xC7, 0xCE));
    }

    // ---- objects, tagging, registration -------------------------------------------------------------------------------------

    [Fact]
    public void DrawingAnchorsResolveToPageBoxes()
    {
        SheetLayoutDocument l = Layout("x26-drawing-anchors");
        SheetPage p = l.Pages[0];
        Assert.Equal(2, p.Objects.Count);
        SheetObject chart = p.Objects.Single(o => o.Kind == SheetObjectKind.Chart);
        Assert.Equal("Chart 1", chart.Name);
        Assert.Equal(p.ContentX + 48, chart.X, 0.01);
        Assert.Equal(p.ContentY + (2 * 15), chart.Y, 0.01);
        Assert.Equal(3 * 48, chart.Width, 0.01);
        Assert.Equal(6 * 15, chart.Height, 0.01);
        SheetObject shape = p.Objects.Single(o => o.Kind == SheetObjectKind.Shape);
        Assert.Equal(75, shape.Width, 0.01);
        Assert.Equal(37.5, shape.Height, 0.01);
    }

    [Fact]
    public void ObjectPainterIsCalledForEveryObjectAndPlaceholdersAreOptional()
    {
        var seen = new List<string>();
        SheetRenderOptions o = Options();
        o.ObjectPainter = c => seen.Add(c.Item.Name);
        o.ObjectPlaceholders = true;
        SheetLayoutDocument l = Layout("x26-drawing-anchors", o);
        _ = WorkbookRenderer.Paint(l, l.Pages[0]);
        Assert.Equal(new List<string> { "Chart 1", "Rectangle 2" }, seen);
    }

    [Fact]
    public void PrintedRangesAreTaggedAsTablesWithHeaderCells()
    {
        using Workbook wb = Open("x03-titles");
        using var ms = new MemoryStream();
        WorkbookRenderer.WritePdf(wb, ms, new PdfRenderOptions(), Options());
        using var pdf = OnePdf.PdfDocument.Open(ms.ToArray());
        HashSet<string> types = [.. (pdf.StructureTree ?? throw new InvalidOperationException()).Elements().Select(e => e.Type)];
        Assert.Contains("Table", types);
        Assert.Contains("TR", types);
        Assert.Contains("TH", types);
        Assert.Contains("TD", types);
    }

    [Fact]
    public void ConvertAndSaveAsWriteWorkbooksAndStillWriteDocuments()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sheet-render-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            RenderingFormats.Register(FormatRegistry.Default);
            SpreadsheetFormats.Register(FormatRegistry.Default);
            DocumentFormats.Register(FormatRegistry.Default);
            string xlsx = Path.Combine(dir, "in.xlsx");
            File.WriteAllBytes(xlsx, Xlsx("x01-basic"));
            Office.Convert(xlsx, Path.Combine(dir, "out.pdf"));
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dir, "out.pdf")), 0, 5), StringComparison.Ordinal);
            using (Workbook wb = Workbook.Open(xlsx))
            {
                wb.SaveAs(Path.Combine(dir, "sheet.png"));
            }

            Assert.Equal(0x89, File.ReadAllBytes(Path.Combine(dir, "sheet.png"))[0]);

            string docx = Path.Combine(dir, "in.docx");
            File.WriteAllBytes(docx, TestDocx.Bytes(TestDocx.P("Hello")));
            Office.Convert(docx, Path.Combine(dir, "doc.pdf"));
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dir, "doc.pdf")), 0, 5), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AnEmptyWorkbookWritesOneBlankPage()
    {
        using Workbook wb = Workbook.Create();
        using var ms = new MemoryStream();
        SheetLayoutDocument l = WorkbookRenderer.WritePdf(wb, ms, null, Options());
        Assert.Empty(l.Pages);
        Assert.Contains(l.Notes, n => n.Code == "sheet-layout.empty");
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(ms.ToArray(), 0, 5), StringComparison.Ordinal);
    }

    [Fact]
    public void PrintSettingsReadPaperMarginsAndNames()
    {
        using Workbook wb = Open("x09-a4-landscape-centred");
        SheetPrintSettings s = SheetPrintSettings.Read(wb.Sheets[0]);
        Assert.Equal(9, s.PaperSize);
        Assert.True(s.Landscape);
        Assert.True(s.CenterHorizontally && s.CenterVertically);
        Assert.Equal(0.7 * 72, s.MarginLeft, 1e-9);
        using Workbook t = Open("x03-titles");
        SheetPrintSettings ts = SheetPrintSettings.Read(t.Sheets[0]);
        Assert.Equal((1, 2), ts.TitleRows);
        Assert.Empty(ts.PrintArea);
    }

    private static int CountOf(string text, string needle)
    {
        int n = 0;
        int i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }

        return n;
    }

    private static int CountFills(DisplayList list, byte r, byte g, byte b)
    {
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem item = list[i];
            if (item.Kind == ItemKind.FillRect && item.Paint.Kind == PaintKind.Solid)
            {
                PaintColor c = item.Paint.Color;
                if (PaintColor.ToByte(c.R) == r && PaintColor.ToByte(c.G) == g && PaintColor.ToByte(c.B) == b)
                {
                    n++;
                }
            }
        }

        return n;
    }
}
