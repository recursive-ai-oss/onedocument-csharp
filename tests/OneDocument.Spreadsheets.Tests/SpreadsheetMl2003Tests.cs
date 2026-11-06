using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets.Xml2003;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class SpreadsheetMl2003Tests
{
    static SpreadsheetMl2003Tests() => SpreadsheetFormats.Register(FormatRegistry.Default);

    private const string Open =
        "<?xml version=\"1.0\"?><?mso-application progid=\"Excel.Sheet\"?>" +
        "<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
        "xmlns:x=\"urn:schemas-microsoft-com:office:excel\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:html=\"http://www.w3.org/TR/REC-html40\">";

    private static string Book(string inner) => Open + inner + "</Workbook>";

    private static string Sheet(string table, string extra = "", string name = "Sheet1") =>
        "<Worksheet ss:Name=\"" + name + "\"><Table>" + table + "</Table>" + extra + "</Worksheet>";

    private static Workbook Load(string xml, OpenContext? context = null) =>
        Workbook.Open(new MemoryStream(Encoding.UTF8.GetBytes(xml)), context ?? new OpenContext(), leaveOpen: false);

    private static Dictionary<string, string> Saved(Workbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        using var zip = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read);
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            parts[e.FullName] = reader.ReadToEnd();
        }

        return parts;
    }

    private static string[] Codes(OpenContext ctx) => [.. ((DefectCollection)ctx.Defects).Select(d => d.Code)];

    [Fact]
    public void ValuesOfEveryTypeAndIndexGaps()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Row><Cell><Data ss:Type=\"String\">text</Data></Cell><Cell ss:Index=\"3\"><Data ss:Type=\"Number\">2.5</Data></Cell><Cell><Data ss:Type=\"Boolean\">1</Data></Cell><Cell><Data ss:Type=\"Error\">#N/A</Data></Cell></Row>" +
            "<Row ss:Index=\"4\"><Cell ss:Index=\"2\"><Data ss:Type=\"String\"> padded </Data></Cell></Row>")));
        Worksheet ws = wb.Sheets["Sheet1"];
        Assert.Equal("text", ws["A1"].Text);
        Assert.Equal(CellValueKind.Empty, ws["B1"].Value.Kind);
        Assert.Equal(2.5, ws["C1"].Number);
        Assert.True(ws["D1"].Value.Boolean);
        Assert.Equal(CellError.NotAvailable, ws["E1"].Value.Error);
        Assert.Equal(" padded ", ws["B4"].Text);
        Assert.Equal(CellValueKind.Empty, ws["A2"].Value.Kind);
        Assert.Equal(OfficeFormat.Xlsx, wb.Format);
    }

    [Fact]
    public void DatesTimesAndTheDateSystem()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Row><Cell><Data ss:Type=\"DateTime\">2024-03-15T00:00:00.000</Data></Cell><Cell><Data ss:Type=\"DateTime\">2024-03-15T12:00:00.000</Data></Cell><Cell><Data ss:Type=\"DateTime\">1899-12-31T18:00:00.000</Data></Cell></Row>")));
        Worksheet ws = wb.Sheets[0];
        Assert.Equal(45366, ws["A1"].Number);
        Assert.Equal(45366.5, ws["B1"].Number);
        Assert.Equal(0.75, ws["C1"].Number);

        using Workbook wb1904 = Load(Open + "<ExcelWorkbook xmlns=\"urn:schemas-microsoft-com:office:excel\"><Date1904/></ExcelWorkbook>" +
            Sheet("<Row><Cell><Data ss:Type=\"DateTime\">2024-03-15T00:00:00.000</Data></Cell></Row>") + "</Workbook>");
        Assert.Equal(DateSystem1904(), wb1904.DateSystem);
        Assert.Equal(43904, wb1904.Sheets[0]["A1"].Number);
    }

    private static RecursiveAI.OneDocument.NumberFormats.DateSystem DateSystem1904() => RecursiveAI.OneDocument.NumberFormats.DateSystem.Date1904;

    [Fact]
    public void FormulasInR1C1BecomeA1AndKeepTheirCachedValues()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Row><Cell><Data ss:Type=\"Number\">1</Data></Cell><Cell><Data ss:Type=\"Number\">2</Data></Cell><Cell><Data ss:Type=\"Number\">3</Data></Cell></Row>" +
            "<Row><Cell ss:Formula=\"=R[-1]C+R[-1]C[1]\"><Data ss:Type=\"Number\">3</Data></Cell><Cell ss:Formula=\"=SUM(R1C1:R1C3)\"><Data ss:Type=\"Number\">6</Data></Cell>" +
            "<Cell ss:Formula=\"=IF(RC[-1]&gt;5,&quot;big&quot;,&quot;small&quot;)\"><Data ss:Type=\"String\">big</Data></Cell><Cell ss:Formula=\"=Sheet2!R1C1*2\"><Data ss:Type=\"Number\">8</Data></Cell></Row>") +
            Sheet("<Row><Cell><Data ss:Type=\"Number\">4</Data></Cell></Row>", name: "Sheet2")));
        Worksheet ws = wb.Sheets["Sheet1"];
        Assert.Equal("A1+B1", ws["A2"].Formula);
        Assert.Equal("SUM($A$1:$C$1)", ws["B2"].Formula);
        Assert.Equal("IF(B2>5,\"big\",\"small\")", ws["C2"].Formula);
        Assert.Equal("Sheet2!$A$1*2", ws["D2"].Formula);
        // Opening never recalculates: the cached values are the file's.
        Assert.Equal(3, ws["A2"].Number);
        Assert.Equal("big", ws["C2"].Text);
        // And an explicit recalculation agrees with them.
        wb.Recalculate();
        Assert.Equal(3, ws["A2"].Number);
        Assert.Equal(6, ws["B2"].Number);
        Assert.Equal("big", ws["C2"].Text);
        Assert.Equal(8, ws["D2"].Number);
    }

    [Fact]
    public void ArrayFormulaKeepsItsRange()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Row><Cell ss:ArrayRange=\"R1C1:R2C1\" ss:Formula=\"={1;2}\"><Data ss:Type=\"Number\">1</Data></Cell></Row><Row><Cell><Data ss:Type=\"Number\">2</Data></Cell></Row>")));
        string xml = Saved(wb)["xl/worksheets/sheet1.xml"];
        Assert.Contains("<f t=\"array\" ref=\"A1:A2\">", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void StylesInheritAndDeduplicate()
    {
        using Workbook wb = Load(Book(
            "<Styles><Style ss:ID=\"Default\" ss:Name=\"Normal\"><Font ss:FontName=\"Calibri\" ss:Size=\"11\"/></Style>" +
            "<Style ss:ID=\"b\"><Font ss:Bold=\"1\" ss:Color=\"#FF0000\"/></Style>" +
            "<Style ss:ID=\"bd\" ss:Parent=\"b\"><NumberFormat ss:Format=\"Short Date\"/><Interior ss:Color=\"#FFFF00\" ss:Pattern=\"Solid\"/><Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Top\" ss:WrapText=\"1\" ss:Rotate=\"45\"/>" +
            "<Borders><Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"2\" ss:Color=\"#000000\"/></Borders></Style>" +
            "<Style ss:ID=\"same\"><Font ss:Bold=\"1\" ss:Color=\"#FF0000\"/></Style></Styles>" +
            Sheet("<Row><Cell ss:StyleID=\"b\"><Data ss:Type=\"String\">x</Data></Cell><Cell ss:StyleID=\"bd\"><Data ss:Type=\"Number\">1</Data></Cell><Cell ss:StyleID=\"same\"><Data ss:Type=\"String\">y</Data></Cell></Row>")));
        Dictionary<string, string> parts = Saved(wb);
        string styles = parts["xl/styles.xml"];
        // b and same share one cell format, bd is a second, and the default is the first.
        Assert.Contains("<cellXfs count=\"3\">", styles, StringComparison.Ordinal);
        Assert.Contains("<b/>", styles, StringComparison.Ordinal);
        Assert.Contains("<color rgb=\"FFFF0000\"/>", styles, StringComparison.Ordinal);
        Assert.Contains("<fgColor rgb=\"FFFFFF00\"/>", styles, StringComparison.Ordinal);
        Assert.Contains("<bottom style=\"medium\"><color rgb=\"FF000000\"/></bottom>", styles, StringComparison.Ordinal);
        Assert.Contains("horizontal=\"center\" vertical=\"top\" textRotation=\"45\" wrapText=\"1\"", styles, StringComparison.Ordinal);
        Assert.Contains("formatCode=\"m/d/yyyy\"", styles, StringComparison.Ordinal);
        Worksheet ws = wb.Sheets[0];
        Assert.Equal(ws["A1"].StyleIndex, ws["C1"].StyleIndex);
        Assert.NotEqual(ws["A1"].StyleIndex, ws["B1"].StyleIndex);
    }

    [Fact]
    public void NegativeRotationAndAllPatternsMap()
    {
        using Workbook wb = Load(Book(
            "<Styles><Style ss:ID=\"r\"><Alignment ss:Rotate=\"-30\"/><Interior ss:Color=\"#00FF00\" ss:Pattern=\"Gray25\" ss:PatternColor=\"#0000FF\"/></Style></Styles>" +
            Sheet("<Row><Cell ss:StyleID=\"r\"><Data ss:Type=\"String\">x</Data></Cell></Row>")));
        string styles = Saved(wb)["xl/styles.xml"];
        Assert.Contains("textRotation=\"120\"", styles, StringComparison.Ordinal);
        Assert.Contains("<patternFill patternType=\"lightGray\"><fgColor rgb=\"FF0000FF\"/><bgColor rgb=\"FF00FF00\"/>", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void RichTextKeepsItsRuns()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Row><Cell><ss:Data ss:Type=\"String\" xmlns=\"http://www.w3.org/TR/REC-html40\">plain <B>bold</B> and <I><U>both</U></I><Sup>2</Sup></ss:Data></Cell></Row>")));
        string xml = Saved(wb)["xl/worksheets/sheet1.xml"];
        Assert.Contains("<r><t xml:space=\"preserve\">plain </t></r>", xml, StringComparison.Ordinal);
        Assert.Contains("<r><rPr><b/></rPr><t>bold</t></r>", xml, StringComparison.Ordinal);
        Assert.Contains("<r><rPr><i/><u/></rPr><t>both</t></r>", xml, StringComparison.Ordinal);
        Assert.Contains("<vertAlign val=\"superscript\"/>", xml, StringComparison.Ordinal);
        Assert.Equal("plain bold and both2", wb.Sheets[0]["A1"].Text);
    }

    [Fact]
    public void MergesWidthsHeightsAndHiding()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Column ss:Width=\"48\"/><Column ss:Hidden=\"1\" ss:Span=\"1\" ss:Width=\"96\"/>" +
            "<Row ss:Height=\"30\" ss:AutoFitHeight=\"0\"><Cell ss:MergeAcross=\"2\" ss:MergeDown=\"1\"><Data ss:Type=\"String\">m</Data></Cell><Cell><Data ss:Type=\"String\">after</Data></Cell></Row>" +
            "<Row ss:Hidden=\"1\"><Cell ss:Index=\"4\"><Data ss:Type=\"String\">h</Data></Cell></Row>")));
        string xml = Saved(wb)["xl/worksheets/sheet1.xml"];
        Assert.Contains("<mergeCell ref=\"A1:C2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<c r=\"D1\"", xml, StringComparison.Ordinal);
        Assert.Contains("ht=\"30\" customHeight=\"1\"", xml, StringComparison.Ordinal);
        Assert.Contains("<col min=\"1\" max=\"1\" width=\"8.43\" customWidth=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<col min=\"2\" max=\"3\" width=\"17.57\" customWidth=\"1\" hidden=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<row r=\"2\" hidden=\"1\">", xml.Replace("<row r=\"2\"", "<row r=\"2\"", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Single(wb.Sheets[0].MergedCells);
    }

    [Fact]
    public void RowSpanRepeatsStyledRows()
    {
        using Workbook wb = Load(Book(
            "<Styles><Style ss:ID=\"s1\"><Font ss:Bold=\"1\"/></Style></Styles>" +
            Sheet("<Row ss:Span=\"2\" ss:StyleID=\"s1\" ss:Height=\"20\"/><Row><Cell><Data ss:Type=\"String\">after</Data></Cell></Row>")));
        string xml = Saved(wb)["xl/worksheets/sheet1.xml"];
        Assert.Contains("<row r=\"1\"", xml, StringComparison.Ordinal);
        Assert.Contains("<row r=\"3\"", xml, StringComparison.Ordinal);
        Assert.Contains("<row r=\"4\"", xml, StringComparison.Ordinal);
        Assert.Equal("after", wb.Sheets[0]["A4"].Text);
    }

    [Fact]
    public void NamesAtBothLevels()
    {
        using Workbook wb = Load(Book(
            "<Names><NamedRange ss:Name=\"Total\" ss:RefersTo=\"=Sheet1!R1C1:R3C1\"/><NamedRange ss:Name=\"Rate\" ss:RefersTo=\"=0.07\"/></Names>" +
            "<Worksheet ss:Name=\"Sheet1\"><Names><NamedRange ss:Name=\"Print_Titles\" ss:RefersTo=\"=Sheet1!R1\"/><NamedRange ss:Name=\"Local\" ss:RefersTo=\"=Sheet1!R2C2\"/></Names>" +
            "<Table><Row><Cell><NamedCell ss:Name=\"Corner\"/><Data ss:Type=\"Number\">1</Data></Cell></Row></Table></Worksheet>"));
        Assert.Equal("Sheet1!$A$1:$A$3", wb.Names.Find("Total")!.Formula);
        Assert.Equal("0.07", wb.Names.Find("Rate")!.Formula);
        Assert.Equal("Sheet1!$B$2", wb.Names.Find("Local", wb.Sheets[0])!.Formula);
        Assert.Equal("Sheet1!$1:$1", wb.Names.Find("_xlnm.Print_Titles", wb.Sheets[0])!.Formula);
        Assert.Equal("'Sheet1'!$A$1", wb.Names.Find("Corner")!.Formula);
    }

    [Fact]
    public void CommentsAndHyperlinks()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Row><Cell ss:HRef=\"https://example.com/p\" ss:HRefScreenTip=\"go\"><Data ss:Type=\"String\">site</Data></Cell><Cell ss:HRef=\"#Sheet1!A1\"><Data ss:Type=\"String\">here</Data>" +
            "<Comment ss:Author=\"Ann\"><ss:Data xmlns=\"http://www.w3.org/TR/REC-html40\"><B>Ann:</B><Font html:Color=\"#000000\"> remember</Font></ss:Data></Comment></Cell></Row>")));
        Dictionary<string, string> parts = Saved(wb);
        Assert.Contains("<hyperlink ref=\"A1\"", parts["xl/worksheets/sheet1.xml"], StringComparison.Ordinal);
        Assert.Contains("tooltip=\"go\"", parts["xl/worksheets/sheet1.xml"], StringComparison.Ordinal);
        Assert.Contains("location=\"Sheet1!A1\"", parts["xl/worksheets/sheet1.xml"], StringComparison.Ordinal);
        Assert.Contains("TargetMode=\"External\"", parts["xl/worksheets/_rels/sheet1.xml.rels"], StringComparison.Ordinal);
        Assert.Contains("<comment ref=\"B1\" authorId=\"0\">", parts["xl/comments1.xml"], StringComparison.Ordinal);
        Assert.Contains("Ann: remember", parts["xl/comments1.xml"], StringComparison.Ordinal);
        Assert.Contains("<legacyDrawing", parts["xl/worksheets/sheet1.xml"], StringComparison.Ordinal);
        Assert.Contains("<x:Row>0</x:Row><x:Column>1</x:Column>", parts["xl/drawings/vmlDrawing1.vml"], StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalFormattingAndValidation()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Row><Cell><Data ss:Type=\"Number\">5</Data></Cell></Row>",
            "<ConditionalFormatting xmlns=\"urn:schemas-microsoft-com:office:excel\"><Range>R1C1:R5C1</Range><Condition><Qualifier>Greater</Qualifier><Value1>10</Value1><Format Style='font-weight:700;color:red;color:#FF0000;background:#FFFF00'/></Condition>" +
            "<Condition><Value1>RC=1</Value1><Format Style='text-decoration:underline'/></Condition></ConditionalFormatting>" +
            "<DataValidation xmlns=\"urn:schemas-microsoft-com:office:excel\"><Range>R1C2:R5C2</Range><Type>Whole</Type><Qualifier>Between</Qualifier><Min>1</Min><Max>10</Max><ErrorMessage>1 to 10</ErrorMessage></DataValidation>" +
            "<DataValidation xmlns=\"urn:schemas-microsoft-com:office:excel\"><Range>R1C3</Range><Type>List</Type><Value>\"a,b,c\"</Value></DataValidation>" +
            "<AutoFilter xmlns=\"urn:schemas-microsoft-com:office:excel\" x:Range=\"R1C1:R5C3\"/>")));
        string xml = Saved(wb)["xl/worksheets/sheet1.xml"];
        Assert.Contains("<conditionalFormatting sqref=\"A1:A5\"><cfRule type=\"cellIs\" dxfId=\"0\" priority=\"1\" operator=\"greaterThan\"><formula>10</formula>", xml, StringComparison.Ordinal);
        Assert.Contains("<cfRule type=\"expression\" dxfId=\"1\" priority=\"2\"><formula>A1=1</formula>", xml, StringComparison.Ordinal);
        Assert.Contains("<dataValidation type=\"whole\" operator=\"between\"", xml, StringComparison.Ordinal);
        Assert.Contains("<formula1>1</formula1><formula2>10</formula2>", xml, StringComparison.Ordinal);
        Assert.Contains("<formula1>\"a,b,c\"</formula1>", xml, StringComparison.Ordinal);
        Assert.Contains("<autoFilter ref=\"A1:C5\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<dxfs count=\"2\">", Saved(wb)["xl/styles.xml"], StringComparison.Ordinal);
    }

    [Fact]
    public void WorksheetOptionsBecomeViewsAndPageSetup()
    {
        using Workbook wb = Load(Book(Sheet(
            "<Row><Cell><Data ss:Type=\"Number\">1</Data></Cell></Row>",
            "<WorksheetOptions xmlns=\"urn:schemas-microsoft-com:office:excel\"><PageSetup><Layout x:Orientation=\"Landscape\" x:CenterHorizontal=\"1\"/><Header x:Margin=\"0.3\" x:Data=\"&amp;CTitle\"/><Footer x:Margin=\"0.3\"/>" +
            "<PageMargins x:Bottom=\"0.9\" x:Left=\"0.6\" x:Right=\"0.6\" x:Top=\"0.9\"/></PageSetup><Print><ValidPrinterInfo/><Scale>80</Scale><PaperSizeIndex>9</PaperSizeIndex></Print>" +
            "<Selected/><DoNotDisplayGridlines/><FreezePanes/><FrozenNoSplit/><SplitHorizontal>1</SplitHorizontal><TopRowBottomPane>1</TopRowBottomPane><ActivePane>2</ActivePane></WorksheetOptions>")));
        string xml = Saved(wb)["xl/worksheets/sheet1.xml"];
        Assert.Contains("showGridLines=\"0\" tabSelected=\"1\"", xml, StringComparison.Ordinal);
        Assert.Contains("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<pageMargins left=\"0.6\" right=\"0.6\" top=\"0.9\" bottom=\"0.9\" header=\"0.3\" footer=\"0.3\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<pageSetup paperSize=\"9\" scale=\"80\" orientation=\"landscape\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<oddHeader>&amp;CTitle</oddHeader>", xml, StringComparison.Ordinal);
        Assert.Contains("<printOptions horizontalCentered=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.True(wb.Sheets[0].TryGetFreezePane(out int rows, out int columns));
        Assert.Equal((1, 0), (rows, columns));
    }

    [Fact]
    public void DocumentPropertiesCarryOver()
    {
        using Workbook wb = Load(Book(
            "<DocumentProperties xmlns=\"urn:schemas-microsoft-com:office:office\"><Title>Budget</Title><Author>Bo</Author><Created>2006-05-04T03:02:01Z</Created><Company>Acme</Company></DocumentProperties>" + Sheet(string.Empty)));
        Dictionary<string, string> parts = Saved(wb);
        Assert.Contains("<dc:title>Budget</dc:title>", parts["docProps/core.xml"], StringComparison.Ordinal);
        Assert.Contains("<Company>Acme</Company>", parts["docProps/app.xml"], StringComparison.Ordinal);
    }

    [Fact]
    public void RepairsAreDefectsWithLocations()
    {
        var ctx = new OpenContext();
        using Workbook wb = Load(Book(
            "<Styles><Style ss:ID=\"Default\"/><Style ss:ID=\"Default\"><Font ss:Bold=\"1\"/></Style><Style ss:ID=\"orphan\" ss:Parent=\"nowhere\"/></Styles>" +
            "<Worksheet ss:Name=\"a/b\"><Table><Row><Cell><Data ss:Type=\"Number\">1,5</Data></Cell><Cell><Data ss:Type=\"DateTime\">last week</Data></Cell>" +
            "<Cell ss:Formula=\"=SUM(R[\"><Data ss:Type=\"Number\">9</Data></Cell><Cell ss:StyleID=\"nope\"><Data ss:Type=\"String\">s</Data></Cell><Cell ss:Index=\"2\"><Data ss:Type=\"String\">back</Data></Cell></Row></Table></Worksheet>" +
            "<Worksheet ss:Name=\"a/b\"><Table/></Worksheet>"), ctx);
        string[] codes = Codes(ctx);
        Assert.Contains("sml2003.bad-number", codes);
        Assert.Contains("sml2003.bad-date", codes);
        Assert.Contains("sml2003.bad-formula", codes);
        Assert.Contains("sml2003.bad-style-ref", codes);
        Assert.Contains("sml2003.bad-index", codes);
        Assert.Contains("sml2003.sheet-name", codes);
        Assert.Contains("sml2003.duplicate-style", codes);
        Defect formula = ((DefectCollection)ctx.Defects).First(d => d.Code == "sml2003.bad-formula");
        Assert.Equal(DefectLocationKind.Cell, formula.Location.Kind);
        Assert.Equal("ab", formula.Location.Container);
        Assert.Equal("C1", formula.Location.Address);
        Assert.Equal(2, wb.Sheets.Count);
        Assert.Equal("ab", wb.Sheets[0].Name);
        // The unreadable formula leaves its cached value in place as a constant.
        Assert.Equal(9, wb.Sheets[0]["C1"].Number);
        Assert.Null(wb.Sheets[0]["C1"].Formula);
        Assert.Equal("1,5", wb.Sheets[0]["A1"].Text);
    }

    [Fact]
    public void NotSpreadsheetMlIsATypedError()
    {
        var ex = Assert.Throws<OfficeFormatException>(() => SpreadsheetMl2003.ToPackage(ByteSource.FromMemory(Encoding.UTF8.GetBytes("<Workbook xmlns=\"urn:other\"/>"))));
        Assert.Equal("sml2003.not-spreadsheet", ex.Defect.Code);
        Assert.Throws<OfficeFormatException>(() => SpreadsheetMl2003.ToPackage(ByteSource.FromMemory(Encoding.UTF8.GetBytes(Open + "<Worksheet"))));
        Assert.Throws<OfficeFormatException>(() => SpreadsheetMl2003.ToPackage(ByteSource.FromMemory(Encoding.UTF8.GetBytes("<!DOCTYPE w [<!ENTITY a \"b\">]>" + Book("")))));
    }

    [Fact]
    public void HugeIndexesAreDroppedNotTrusted()
    {
        var ctx = new OpenContext();
        using Workbook wb = Load(Book(Sheet("<Row ss:Index=\"2000000\"><Cell><Data ss:Type=\"String\">x</Data></Cell></Row><Row><Cell ss:Index=\"20000\"><Data ss:Type=\"String\">y</Data></Cell></Row>")), ctx);
        Assert.Contains("sml2003.bad-index", Codes(ctx));
        Assert.Equal(CellValueKind.Empty, wb.Sheets[0]["A1"].Value.Kind);
    }

    [Fact]
    public void SniffedAndOpenedThroughTheRegistry()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Book(Sheet("<Row><Cell><Data ss:Type=\"String\">via registry</Data></Cell></Row>")));
        Assert.Equal(OfficeFormat.Excel2003Xml, FormatSniffer.Detect(ByteSource.FromMemory(bytes), OfficeLimits.Default));
        using OfficeFile file = Office.Open(ByteSource.FromMemory(bytes));
        Assert.IsType<Workbook>(file);
        using var writer = new StringWriter();
        Office.ExtractText(ByteSource.FromMemory(bytes), writer);
        Assert.Contains("via registry", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertedWorkbookSavesAndReopens()
    {
        using Workbook wb = Load(Book(Sheet("<Row><Cell><Data ss:Type=\"Number\">7</Data></Cell></Row>")));
        Assert.NotNull(wb.ImportLoss);
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        ms.Position = 0;
        using Workbook again = Workbook.Open(ms);
        Assert.Equal(7, again.Sheets[0]["A1"].Number);
        Assert.Null(again.ImportLoss);
    }

    [Fact]
    public void NamedStylesAreReportedAsLoss()
    {
        using Workbook wb = Load(Book("<Styles><Style ss:ID=\"Default\" ss:Name=\"Normal\"/><Style ss:ID=\"s1\" ss:Name=\"Title\"><Font ss:Bold=\"1\"/></Style></Styles>" + Sheet(string.Empty)));
        Assert.Equal(1, wb.ImportLoss!.CountOf("loss.cell-style-names"));
    }

    private static string Fixture(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "xml2003", name));

    [Fact]
    public void LibreOfficeExcelFileOpens()
    {
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(Fixture("lo-excel.xml"), ctx);
        Assert.Equal(["Data", "Second"], wb.Sheets.Select(s => s.Name));
        Worksheet data = wb.Sheets["Data"];
        Assert.Equal("Header", data["A1"].Text);
        Assert.Equal(1234.5, data["B2"].Number);
        Assert.Equal(45366, data["C2"].Number);
        Assert.Equal("big", data["D3"].Text);
        Assert.Equal("link", data["B5"].Text);
        Assert.Contains(data.MergedCells, m => m.ToString() == "A4:B5");
        // LibreOffice writes OpenFormula text where Excel writes R1C1: the formulas are reported, the cached values kept.
        Assert.Contains("sml2003.bad-formula", Codes(ctx));
        Assert.Equal(1254.5, data["C3"].Number);
        Assert.DoesNotContain(((DefectCollection)ctx.Defects), d => d.Severity == DefectSeverity.Fatal);
    }

    [Fact]
    public void FeatureWorkbookOpensAndItsCachedValuesSurviveARecalculation()
    {
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(Fixture("excel-features.xml"), ctx);
        Assert.Equal(["Sales", "Totals"], wb.Sheets.Select(s => s.Name));
        Worksheet sales = wb.Sheets["Sales"];
        Assert.Equal("SUM(B2:B5)", sales["B7"].Formula);
        Assert.Equal("IF(B7>1000,\"large\",\"small\")", sales["D7"].Formula);
        Assert.Equal("B7*Rate", sales["E7"].Formula);
        Assert.Equal("Totals!$A$1+1", sales["E8"].Formula);
        Assert.Equal(CellError.DivideByZero, sales["E5"].Value.Error);
        Assert.Equal("Sales!$C$2", wb.Names.Find("Rate")!.Formula);
        Assert.NotNull(wb.Names.Find("_xlnm.Print_Area", sales));
        Assert.Empty(((DefectCollection)ctx.Defects));

        // Information: recalculating reproduces the numbers the file stored.
        var cached = new List<(Worksheet Sheet, string Address, CellValue Value)>();
        foreach (Worksheet sheet in wb.Sheets)
        {
            foreach (string address in new[] { "B7", "C7", "D7", "E7", "E8", "B1", "B2" })
            {
                if (sheet[address].Formula is not null || (sheet.Name == "Totals" && address is "B2"))
                {
                    cached.Add((sheet, address, sheet[address].Value));
                }
            }
        }

        Assert.NotEmpty(cached);
        wb.Recalculate();
        foreach ((Worksheet sheet, string address, CellValue before) in cached)
        {
            CellValue after = sheet[address].Value;
            Assert.Equal(before.Kind, after.Kind);
            if (before.Kind == CellValueKind.Number)
            {
                Assert.True(Math.Abs(before.Number - after.Number) < 1e-9, sheet.Name + "!" + address + ": " + before.Number + " vs " + after.Number);
            }
            else
            {
                Assert.Equal(sheet.GetText(before), sheet.GetText(after));
            }
        }
    }

    [Fact]
    public void ConvertsToXlsxByExtension()
    {
        string dir = Path.Combine(Path.GetTempPath(), "xml2003-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string output = Path.Combine(dir, "out.xlsx");
            Office.Convert(Fixture("excel-features.xml"), output);
            using Workbook again = Workbook.Open(output);
            Assert.Equal(2, again.Sheets.Count);
            Assert.Equal("SUM(B2:B5)", again.Sheets["Sales"]["B7"].Formula);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EmptyWorkbookGetsASheetAndBadPropertyDatesAreDropped()
    {
        var ctx = new OpenContext();
        using Workbook wb = Load(Book("<DocumentProperties xmlns=\"urn:schemas-microsoft-com:office:office\"><Created>whenever</Created></DocumentProperties>"), ctx);
        Assert.Single(wb.Sheets);
        Assert.Contains("sml2003.no-sheets", Codes(ctx));
        Assert.Contains("sml2003.bad-value", Codes(ctx));
    }
}
