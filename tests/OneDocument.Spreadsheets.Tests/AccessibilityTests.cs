using System.Text;
using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>One positive and one negative workbook per accessibility rule that applies to workbooks.</summary>
public class AccessibilityTests
{
    private static Workbook Book(string sheetName = "Sales")
    {
        Workbook wb = Workbook.Create();
        wb.Sheets[0].Rename(sheetName);
        wb.Sheets[0]["A1"].SetString("Item");
        wb.SetAccessibilityTitle("Book");
        return wb;
    }

    // With ONEDOCUMENT_A11Y_FIXTURES set, each rule case is also written as a file, for checking against Office's own checker.
    private static void Emit(Workbook wb, string name)
    {
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_A11Y_FIXTURES") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            wb.SaveAs(Path.Combine(dir, name + ".xlsx"));
        }
    }

    private static string[] Ids(AccessibilityReport r) => [.. r.Issues.Select(i => i.RuleId)];

    private static void Fires(string ruleId, Action<Workbook> positive, Action<Workbook> negative, Action<AccessibilityIssue>? located = null)
    {
        using (Workbook wb = Book())
        {
            positive(wb);
            Emit(wb, ruleId + ".positive");
            AccessibilityReport r = wb.CheckAccessibility();
            AccessibilityIssue? hit = r.Issues.FirstOrDefault(i => i.RuleId == ruleId);
            Assert.True(hit is not null, ruleId + " did not fire; got: " + string.Join(", ", Ids(r)));
            located?.Invoke(hit);
        }

        using (Workbook wb = Book())
        {
            negative(wb);
            Emit(wb, ruleId + ".negative");
            Assert.DoesNotContain(ruleId, Ids(wb.CheckAccessibility()));
        }
    }

    private static void AddDrawing(Workbook wb, string anchorXml)
    {
        Worksheet ws = wb.Sheets[0];
        const string xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        wb.Package.AddPart("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml", Encoding.UTF8.GetBytes(
            "<xdr:wsDr xmlns:xdr=\"" + xdr + "\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" + anchorXml + "</xdr:wsDr>"));
        var rels = new List<Relationship>(wb.Package.GetRelationships(ws.PartName)) { new("rId50", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing", "../drawings/drawing1.xml") };
        wb.Package.SetRelationships(ws.PartName, rels);
    }

    private static string Anchor(string shape) =>
        "<xdr:twoCellAnchor><xdr:from><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" + shape + "<xdr:clientData/></xdr:twoCellAnchor>";

    private static string Pic(string attrs) =>
        "<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"2\" name=\"Picture 1\" " + attrs + "/><xdr:cNvPicPr/></xdr:nvPicPr><xdr:blipFill><a:blip r:embed=\"rId9\"/></xdr:blipFill><xdr:spPr/></xdr:pic>";

    [Fact]
    public void AltText_Missing_FileName_AndChart()
    {
        Fires("alt-text.missing", wb => AddDrawing(wb, Anchor(Pic(""))), wb => AddDrawing(wb, Anchor(Pic("descr=\"Sales by region\""))), hit =>
        {
            Assert.Equal(AccessibilityCategory.Error, hit.Category);
            Assert.Equal("Sales", hit.Location.Sheet);
            Assert.Equal("C5", hit.Location.Range);
            Assert.Equal(2, hit.Location.ShapeId);
        });
        Fires("alt-text.file-name", wb => AddDrawing(wb, Anchor(Pic("descr=\"chart.png\""))), wb => AddDrawing(wb, Anchor(Pic("descr=\"A chart\""))));
        using Workbook c = Book();
        AddDrawing(c, Anchor("<xdr:graphicFrame><xdr:nvGraphicFramePr><xdr:cNvPr id=\"3\" name=\"Chart 2\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/></a:graphic></xdr:graphicFrame>"));
        Assert.Contains(c.CheckAccessibility().Issues, i => i.RuleId == "alt-text.missing" && i.Message.Contains("chart", StringComparison.Ordinal));
    }

    [Fact]
    public void SheetDefaultName()
    {
        using Workbook bad = Book("Sheet3");
        Emit(bad, "sheet.default-name.positive");
        Assert.Contains("sheet.default-name", Ids(bad.CheckAccessibility()));
        using Workbook good = Book("October sales totals");
        Emit(good, "sheet.default-name.negative");
        Assert.DoesNotContain("sheet.default-name", Ids(good.CheckAccessibility()));
        Assert.Equal(AccessibilityCategory.Warning, bad.CheckAccessibility().Issues.First(i => i.RuleId == "sheet.default-name").Category);
    }

    [Fact]
    public void SheetBlank()
    {
        using Workbook wb = Book();
        Emit(wb, "sheet.blank.negative");
        wb.Sheets.Add("Empty");
        Emit(wb, "sheet.blank.positive");
        AccessibilityIssue hit = Assert.Single(wb.CheckAccessibility().Issues, i => i.RuleId == "sheet.blank");
        Assert.Equal("Empty", hit.Location.Sheet);
    }

    [Fact]
    public void TableHeaderRow_AndDefaultName()
    {
        Fires("table.header-row",
            wb => { wb.Sheets[0].Tables.Add("Orders", CellRange.Parse("A1:B3"), hasHeaderRow: false); },
            wb => { wb.Sheets[0].Tables.Add("Orders", CellRange.Parse("A1:B3")); },
            hit => Assert.Equal("A1:B3", hit.Location.Range));
        Fires("table.default-name",
            wb => wb.Sheets[0].Tables.Add("Table1", CellRange.Parse("A1:B3")),
            wb => wb.Sheets[0].Tables.Add("Orders", CellRange.Parse("A1:B3")),
            hit => Assert.Equal(AccessibilityCategory.Tip, hit.Category));
    }

    [Fact]
    public void MergedCells() =>
        Fires("table.merged-cells", wb => wb.Sheets[0].Merge(CellRange.Parse("B2:C3")), wb => { }, hit =>
        {
            Assert.Equal("B2:C3", hit.Location.Range);
            Assert.Equal(AccessibilityCategory.Warning, hit.Category);
        });

    [Fact]
    public void HyperlinkText()
    {
        static void Link(Workbook wb, string text)
        {
            Worksheet ws = wb.Sheets[0];
            ws["B2"].SetString(text);
            ws.InsertFrameElement("hyperlinks", "<hyperlinks xmlns=\"" + TestBook.Main + "\"><hyperlink ref=\"B2\" location=\"Sales!A1\"/></hyperlinks>");
        }

        Fires("hyperlink.text", wb => Link(wb, "click here"), wb => Link(wb, "Back to the summary"), hit => Assert.Equal("B2", hit.Location.Range));
    }

    [Fact]
    public void RedOnlyNegativeNumbers()
    {
        static void Num(Workbook wb, string format)
        {
            Cell c = wb.Sheets[0]["B2"];
            c.SetNumber(-5);
            c.SetStyle(new CellStyleSpec { NumberFormatCode = format });
        }

        Fires("number-format.red-only", wb => Num(wb, "0.00;[Red]0.00"), wb => Num(wb, "0.00;[Red]-0.00"), hit =>
        {
            Assert.Equal(AccessibilityCategory.Error, hit.Category);
            Assert.Equal("B2", hit.Location.Range);
        });
        using Workbook paren = Book();
        Num(paren, "0.00;[Red](0.00)");
        Assert.DoesNotContain("number-format.red-only", Ids(paren.CheckAccessibility()));
        using Workbook single = Book();
        Num(single, "[Red]0.00");
        Assert.DoesNotContain("number-format.red-only", Ids(single.CheckAccessibility()));
    }

    [Fact]
    public void Contrast_FontAgainstFill()
    {
        static void Cellf(Workbook wb, int font, int? fill)
        {
            Cell c = wb.Sheets[0]["B2"];
            c.SetString("Text");
            c.SetStyle(new CellStyleSpec { Font = new CellFont(Color: StyleColor.FromRgb(font)), Fill = fill is { } f ? CellFill.Solid(StyleColor.FromRgb(f)) : null });
        }

        Fires("contrast.low", wb => Cellf(wb, 0xD9D9D9, null), wb => Cellf(wb, 0x000000, null), hit =>
        {
            Assert.Equal("B2", hit.Location.Range);
            Assert.Equal(AccessibilityCategory.Warning, hit.Category);
        });
        using Workbook dark = Book();
        Cellf(dark, 0x303030, 0x202020);
        Assert.Contains("contrast.low", Ids(dark.CheckAccessibility()));
        using Workbook light = Book();
        Cellf(light, 0xFFFFFF, 0x1F4E79);
        Assert.DoesNotContain("contrast.low", Ids(light.CheckAccessibility()));
    }

    [Fact]
    public void DocumentTitle()
    {
        using Workbook wb = Workbook.Create();
        wb.Sheets[0].Rename("Sales");
        wb.Sheets[0]["A1"].SetString("x");
        Emit(wb, "document.title.positive");
        Assert.Contains("document.title", Ids(wb.CheckAccessibility()));
        Assert.True(wb.SetAccessibilityTitle("Annual sales").Applied);
        Emit(wb, "document.title.negative");
        Assert.DoesNotContain("document.title", Ids(wb.CheckAccessibility()));
    }

    [Fact]
    public void RedOnlyDetection_ParsesSections()
    {
        Assert.True(WorkbookAccessibilityChecker.IsRedOnlyNegative("#,##0;[Red]#,##0"));
        Assert.True(WorkbookAccessibilityChecker.IsRedOnlyNegative("0.00;[red]0.00;0"));
        Assert.False(WorkbookAccessibilityChecker.IsRedOnlyNegative("#,##0;[Red]-#,##0"));
        Assert.False(WorkbookAccessibilityChecker.IsRedOnlyNegative("#,##0;[Red](#,##0)"));
        Assert.False(WorkbookAccessibilityChecker.IsRedOnlyNegative("0.00"));
        Assert.False(WorkbookAccessibilityChecker.IsRedOnlyNegative("0.00;\"-\"0.00"));
        Assert.True(WorkbookAccessibilityChecker.IsRedOnlyNegative("0.00;[Red]\"neg \"0.00"));
        Assert.True(WorkbookAccessibilityChecker.IsRedOnlyNegative("0.00;[Red]\"neg-\"0.00"));
    }

    [Fact]
    public void Checking_ChangesNothing()
    {
        using Workbook wb = Book();
        AddDrawing(wb, Anchor(Pic("")));
        byte[] before = TestBook.Save(wb);
        _ = wb.CheckAccessibility();
        Assert.Equal(before, TestBook.Save(wb));
    }
}
