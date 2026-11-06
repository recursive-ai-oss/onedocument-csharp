using System.Runtime.CompilerServices;
using System.Text;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>Worksheet features that cross between SpreadsheetML and the binary form: written to records, read back, and written as markup again.</summary>
public class XlsbFeatureTests
{
    private const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"" + TestBook.Main + "\" xmlns:r=\"" + TestBook.R + "\">";

    /// <summary>The markup of sheet 1 after SpreadsheetML → binary → SpreadsheetML, and the markup the binary workbook offers to the hub.</summary>
    private static (string Hub, string Back, LossReport Loss) RoundTrip(string sheetBody)
    {
        string xml = Head + "<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData>" + sheetBody + "</worksheet>";
        var loss = new LossReport();
        byte[] bin;
        using (Workbook source = TestBook.Open(TestBook.Bytes(xml)))
        {
            using var ms = new MemoryStream();
            source.SaveAs(ms, OfficeFormat.Xlsb, loss);
            bin = ms.ToArray();
        }

        using Workbook wb = Workbook.Open(new MemoryStream(bin));
        string hub = Encoding.UTF8.GetString(wb.Package.FindPart("/xl/worksheets/sheet1.xml")!.GetBytes().Span);
        using var xlsx = new MemoryStream();
        wb.SaveAs(xlsx, OfficeFormat.Xlsx, new LossReport());
        using Workbook back = Workbook.Open(new MemoryStream(xlsx.ToArray()));
        return (hub, Encoding.UTF8.GetString(back.Package.FindPart("/xl/worksheets/sheet1.xml")!.GetBytes().Span), loss);
    }

    [Fact]
    public void DataValidationsSurviveTheBinaryForm()
    {
        const string dv = "<dataValidations count=\"3\" disablePrompts=\"1\">"
            + "<dataValidation type=\"list\" allowBlank=\"1\" showErrorMessage=\"1\" errorTitle=\"Bad\" error=\"Pick one\" sqref=\"A1:A5 C1\"><formula1>\"a,b,c\"</formula1></dataValidation>"
            + "<dataValidation type=\"whole\" operator=\"between\" errorStyle=\"warning\" showInputMessage=\"1\" promptTitle=\"P\" prompt=\"1 to 9\" sqref=\"B2:B9\"><formula1>1</formula1><formula2>9</formula2></dataValidation>"
            + "<dataValidation type=\"custom\" sqref=\"D1\"><formula1>AND(D1&gt;0,ISNUMBER(D1))</formula1></dataValidation>"
            + "</dataValidations>";
        (string hub, string back, LossReport loss) = RoundTrip(dv);
        Assert.DoesNotContain("xb:r", hub);
        Assert.DoesNotContain(loss.Items, i => i.Code == "loss.xlsb-sheet-element");
        foreach (string xml in new[] { hub, back })
        {
            Assert.Contains("<dataValidations count=\"3\" disablePrompts=\"1\">", xml);
            Assert.Contains("type=\"list\"", xml);
            Assert.Contains("<formula1>\"a,b,c\"</formula1>", xml);
            Assert.Contains("sqref=\"A1:A5 C1\"", xml);
            Assert.Contains("errorStyle=\"warning\"", xml);
            Assert.Contains("<formula2>9</formula2>", xml);
            Assert.Contains("<formula1>AND(D1&gt;0,ISNUMBER(D1))</formula1>", xml);
            Assert.Contains("errorTitle=\"Bad\"", xml);
            Assert.Contains("prompt=\"1 to 9\"", xml);
        }
    }

    [Fact]
    public void SheetProtectionSurvivesTheBinaryForm()
    {
        const string prot = "<sheetProtection password=\"CC1A\" sheet=\"1\" objects=\"1\" scenarios=\"0\" formatCells=\"0\" formatColumns=\"1\" formatRows=\"1\" insertColumns=\"1\" insertRows=\"1\" insertHyperlinks=\"1\" deleteColumns=\"1\" deleteRows=\"1\" selectLockedCells=\"0\" sort=\"0\" autoFilter=\"1\" pivotTables=\"1\" selectUnlockedCells=\"1\"/>";
        (string hub, string back, _) = RoundTrip(prot);
        Assert.DoesNotContain("xb:r", hub);
        foreach (string xml in new[] { hub, back })
        {
            Assert.Contains("password=\"CC1A\"", xml);
            Assert.Contains("sheet=\"1\"", xml);
            Assert.Contains("objects=\"1\"", xml);
            Assert.Contains("scenarios=\"0\"", xml);
            Assert.Contains("formatCells=\"0\"", xml);
            Assert.Contains("formatRows=\"1\"", xml);
            Assert.Contains("sort=\"0\"", xml);
            Assert.Contains("selectUnlockedCells=\"1\"", xml);
        }
    }

    [Fact]
    public void AutoFilterSurvivesTheBinaryForm()
    {
        const string af = "<autoFilter ref=\"A1:D20\">"
            + "<filterColumn colId=\"0\"><filters blank=\"1\"><filter val=\"x\"/><filter val=\"y\"/><dateGroupItem year=\"2020\" month=\"3\" dateTimeGrouping=\"month\"/></filters></filterColumn>"
            + "<filterColumn colId=\"1\" hiddenButton=\"1\"><customFilters and=\"1\"><customFilter operator=\"greaterThan\" val=\"5\"/><customFilter operator=\"lessThan\" val=\"9.5\"/></customFilters></filterColumn>"
            + "<filterColumn colId=\"2\"><top10 top=\"0\" percent=\"1\" val=\"10\"/></filterColumn>"
            + "<filterColumn colId=\"3\"><dynamicFilter type=\"thisMonth\"/></filterColumn>"
            + "</autoFilter>";
        (string hub, string back, LossReport loss) = RoundTrip(af);
        Assert.DoesNotContain("xb:r", hub);
        Assert.DoesNotContain(loss.Items, i => i.Code == "loss.xlsb-sheet-element" || i.Code == "loss.xlsb-filter-element");
        foreach (string xml in new[] { hub, back })
        {
            Assert.Contains("<autoFilter ref=\"A1:D20\">", xml);
            Assert.Contains("<filters blank=\"1\">", xml);
            Assert.Contains("<filter val=\"x\"/>", xml);
            Assert.Contains("dateTimeGrouping=\"month\"", xml);
            Assert.Contains("hiddenButton=\"1\"", xml);
            Assert.Contains("<customFilters and=\"1\">", xml);
            Assert.Contains("operator=\"greaterThan\" val=\"5\"", xml);
            Assert.Contains("val=\"9.5\"", xml);
            Assert.Contains("<top10 top=\"0\" percent=\"1\" val=\"10\"/>", xml);
            Assert.Contains("<dynamicFilter type=\"thisMonth\"/>", xml);
        }
    }

    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    /// <summary>A workbook whose first sheet has a table and comments, written as a binary workbook and read back.</summary>
    private static (Workbook Bin, byte[] Bytes) TableAndComments()
    {
        byte[] baseBytes = TestBook.Bytes(Head + "<sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Item</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>Qty</t></is></c><c r=\"C1\" t=\"inlineStr\"><is><t>Price</t></is></c></row>"
            + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>a</t></is></c><c r=\"B2\"><v>2</v></c><c r=\"C2\"><f>Table1[[#This Row],[Qty]]*2</f><v>4</v></c></row></sheetData>"
            + "<tableParts count=\"1\"><tablePart r:id=\"rId7\"/></tableParts></worksheet>");
        using OpcPackage p = OpcPackage.Open(new MemoryStream(baseBytes));
        p.AddPart("/xl/tables/table1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<table xmlns=\"" + TestBook.Main + "\" id=\"1\" name=\"Table1\" displayName=\"Table1\" ref=\"A1:C3\" totalsRowCount=\"1\">"
            + "<autoFilter ref=\"A1:C2\"/><tableColumns count=\"3\"><tableColumn id=\"1\" name=\"Item\" totalsRowLabel=\"Total\"/><tableColumn id=\"2\" name=\"Qty\" totalsRowFunction=\"sum\"/>"
            + "<tableColumn id=\"3\" name=\"Price\"><calculatedColumnFormula>Table1[[#This Row],[Qty]]*2</calculatedColumnFormula></tableColumn></tableColumns>"
            + "<tableStyleInfo name=\"TableStyleMedium9\" showFirstColumn=\"0\" showLastColumn=\"0\" showRowStripes=\"1\" showColumnStripes=\"0\"/></table>"));
        p.AddPart("/xl/comments1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<comments xmlns=\"" + TestBook.Main + "\"><authors><author>Ann</author><author>Bo</author></authors><commentList>"
            + "<comment ref=\"B1\" authorId=\"1\"><text><r><rPr><b/><sz val=\"9\"/><rFont val=\"Tahoma\"/></rPr><t>Bo:</t></r><r><t xml:space=\"preserve\"> hello</t></r></text></comment></commentList></comments>"));
        p.SetRelationships("/xl/worksheets/sheet1.xml",
        [
            new Relationship("rId7", Rel + "table", "../tables/table1.xml"),
            new Relationship("rId8", Rel + "comments", "../comments1.xml"),
        ]);
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        using Workbook source = Workbook.Open(new MemoryStream(ms.ToArray()));
        using var bin = new MemoryStream();
        var loss = new LossReport();
        source.SaveAs(bin, OfficeFormat.Xlsb, loss);
        Assert.DoesNotContain(loss.Items, i => i.Code is "loss.xlsb-formula-unparsed" or "loss.xlsb-formula-approximated");
        return (Workbook.Open(new MemoryStream(bin.ToArray())), bin.ToArray());
    }

    [Fact]
    public void TablesAndCommentsSurviveTheBinaryForm()
    {
        (Workbook wb, byte[] bytes) = TableAndComments();
        using (wb)
        {
            string table = Encoding.UTF8.GetString(wb.Package.FindPart("/xl/tables/table1.xml")!.GetBytes().Span);
            string comments = Encoding.UTF8.GetString(wb.Package.FindPart("/xl/comments1.xml")!.GetBytes().Span);
            Assert.DoesNotContain("xb:r", table);
            Assert.Contains("ref=\"A1:C3\"", table);
            Assert.Contains("totalsRowCount=\"1\"", table);
            Assert.Contains("<autoFilter ref=\"A1:C2\">", table);
            Assert.Contains("totalsRowLabel=\"Total\"", table);
            Assert.Contains("totalsRowFunction=\"sum\"", table);
            Assert.Contains("<calculatedColumnFormula>Table1[[#This Row],[Qty]]*2</calculatedColumnFormula>", table);
            Assert.Contains("name=\"TableStyleMedium9\"", table);
            Assert.Contains("showRowStripes=\"1\"", table);
            Assert.Contains("<author>Bo</author>", comments);
            Assert.Contains("<comment ref=\"B1\" authorId=\"1\">", comments);
            Assert.True(comments.Contains("Tahoma", StringComparison.Ordinal), comments);
            Assert.Matches(@"<b( val=.1.)?/>", comments);
            Assert.Contains("xml:space=\"preserve\"> hello", comments);
            Assert.Contains("<tablePart r:id=\"rId7\"/>", Encoding.UTF8.GetString(wb.Package.FindPart("/xl/worksheets/sheet1.xml")!.GetBytes().Span));
            Assert.Equal("Table1[[#This Row],[Qty]]*2", wb.Sheets[0]["C2"].Formula);
        }

        // Untouched, the binary parts are written back unchanged; converting to SpreadsheetML keeps both features.
        using Workbook again = Workbook.Open(new MemoryStream(bytes));
        using var xlsx = new MemoryStream();
        var loss = new LossReport();
        again.SaveAs(xlsx, OfficeFormat.Xlsx, loss);
        Assert.DoesNotContain(loss.Items, i => i.Code == "loss.xlsb-part-not-converted");
        using Workbook back = Workbook.Open(new MemoryStream(xlsx.ToArray()));
        Assert.NotNull(back.Package.FindPart("/xl/tables/table1.xml"));
        Assert.NotNull(back.Package.FindPart("/xl/comments1.xml"));
    }

    [Fact]
    public void ConditionalFormattingSurvivesTheBinaryForm()
    {
        const string q = "\"";
        string cf = "<conditionalFormatting sqref="+q+"B2:B9 D2"+q+">"
            + "<cfRule type="+q+"cellIs"+q+" dxfId="+q+"0"+q+" priority="+q+"1"+q+" operator="+q+"between"+q+"><formula>1.5</formula><formula>2.5</formula></cfRule>"
            + "<cfRule type="+q+"expression"+q+" dxfId="+q+"1"+q+" priority="+q+"2"+q+" stopIfTrue="+q+"1"+q+"><formula>MOD(ROW(),2)=0</formula></cfRule>"
            + "<cfRule type="+q+"containsText"+q+" dxfId="+q+"2"+q+" priority="+q+"3"+q+" operator="+q+"containsText"+q+" text="+q+"abc"+q+"><formula>NOT(ISERROR(SEARCH("+q+"abc"+q+",B2)))</formula></cfRule>"
            + "<cfRule type="+q+"top10"+q+" dxfId="+q+"0"+q+" priority="+q+"4"+q+" percent="+q+"1"+q+" bottom="+q+"1"+q+" rank="+q+"20"+q+"/>"
            + "<cfRule type="+q+"aboveAverage"+q+" dxfId="+q+"0"+q+" priority="+q+"5"+q+" aboveAverage="+q+"0"+q+" equalAverage="+q+"1"+q+" stdDev="+q+"2"+q+"/>"
            + "<cfRule type="+q+"timePeriod"+q+" dxfId="+q+"0"+q+" priority="+q+"6"+q+" timePeriod="+q+"lastWeek"+q+"><formula>AND(TODAY()-ROUNDDOWN(B2,0)&gt;=(WEEKDAY(TODAY())),TODAY()-ROUNDDOWN(B2,0)&lt;(WEEKDAY(TODAY())+7))</formula></cfRule>"
            + "</conditionalFormatting>"
            + "<conditionalFormatting sqref="+q+"E1:E9"+q+"><cfRule type="+q+"colorScale"+q+" priority="+q+"7"+q+"><colorScale><cfvo type="+q+"min"+q+"/><cfvo type="+q+"percentile"+q+" val="+q+"50"+q+"/><cfvo type="+q+"max"+q+"/><color rgb="+q+"FFF8696B"+q+"/><color rgb="+q+"FFFFEB84"+q+"/><color theme="+q+"4"+q+"/></colorScale></cfRule></conditionalFormatting>"
            + "<conditionalFormatting sqref="+q+"F1:F9"+q+"><cfRule type="+q+"dataBar"+q+" priority="+q+"8"+q+"><dataBar showValue="+q+"0"+q+"><cfvo type="+q+"min"+q+"/><cfvo type="+q+"num"+q+" val="+q+"100"+q+"/><color rgb="+q+"FF638EC6"+q+"/></dataBar></cfRule></conditionalFormatting>"
            + "<conditionalFormatting sqref="+q+"G1:G9"+q+"><cfRule type="+q+"iconSet"+q+" priority="+q+"9"+q+"><iconSet iconSet="+q+"4Rating"+q+" reverse="+q+"1"+q+"><cfvo type="+q+"percent"+q+" val="+q+"0"+q+"/><cfvo type="+q+"percent"+q+" val="+q+"25"+q+"/><cfvo type="+q+"formula"+q+" val="+q+"$H$1"+q+"/><cfvo type="+q+"num"+q+" val="+q+"90"+q+" gte="+q+"0"+q+"/></iconSet></cfRule></conditionalFormatting>";
        (string hub, string back, LossReport loss) = RoundTrip(cf);
        Assert.DoesNotContain("xb:r", hub);
        Assert.DoesNotContain(loss.Items, i => i.Code.StartsWith("loss.xlsb-conditional", StringComparison.Ordinal) || i.Code == "loss.xlsb-sheet-element");
        foreach (string xml in new[] { hub, back })
        {
            Assert.Contains("<conditionalFormatting sqref="+q+"B2:B9 D2"+q+">", xml);
            Assert.Contains("operator="+q+"between"+q+"><formula>1.5</formula><formula>2.5</formula>", xml);
            Assert.Contains("stopIfTrue="+q+"1"+q+"><formula>MOD(ROW(),2)=0</formula>", xml);
            Assert.Contains("text="+q+"abc"+q, xml);
            Assert.Contains("rank="+q+"20"+q, xml);
            Assert.Contains("percent="+q+"1"+q, xml);
            Assert.Contains("bottom="+q+"1"+q, xml);
            Assert.Contains("aboveAverage="+q+"0"+q, xml);
            Assert.Contains("equalAverage="+q+"1"+q, xml);
            Assert.Contains("stdDev="+q+"2"+q, xml);
            Assert.Contains("timePeriod="+q+"lastWeek"+q, xml);
            Assert.Contains("<cfvo type="+q+"percentile"+q+" val="+q+"50"+q+"/>", xml);
            Assert.Contains("<color rgb="+q+"FFF8696B"+q+"/>", xml);
            Assert.Contains("<color theme="+q+"4"+q+"/>", xml);
            Assert.Contains("<dataBar showValue="+q+"0"+q+">", xml);
            Assert.Contains("iconSet="+q+"4Rating"+q+" reverse="+q+"1"+q, xml);
            Assert.Contains("<cfvo type="+q+"formula"+q+" val="+q+"$H$1"+q+"/>", xml);
            Assert.Contains("gte="+q+"0"+q, xml);
        }
    }

    [Fact]
    public void PageBreaksAndIgnoredErrorsSurviveTheBinaryForm()
    {
        const string q = "\"";
        string body = "<rowBreaks count="+q+"2"+q+" manualBreakCount="+q+"2"+q+"><brk id="+q+"10"+q+" max="+q+"16383"+q+" man="+q+"1"+q+"/><brk id="+q+"20"+q+" min="+q+"1"+q+" max="+q+"5"+q+" man="+q+"1"+q+"/></rowBreaks>"
            + "<colBreaks count="+q+"1"+q+" manualBreakCount="+q+"1"+q+"><brk id="+q+"3"+q+" max="+q+"1048575"+q+" man="+q+"1"+q+"/></colBreaks>"
            + "<ignoredErrors><ignoredError sqref="+q+"A1:B2"+q+" numberStoredAsText="+q+"1"+q+" formula="+q+"1"+q+"/></ignoredErrors>";
        (string hub, string back, _) = RoundTrip(body);
        Assert.DoesNotContain("xb:r", hub);
        foreach (string xml in new[] { hub, back })
        {
            Assert.Contains("<rowBreaks count="+q+"2"+q+" manualBreakCount="+q+"2"+q+">", xml);
            Assert.Contains("<brk id="+q+"20"+q+" min="+q+"1"+q+" max="+q+"5"+q+" man="+q+"1"+q+"/>", xml);
            Assert.Contains("<colBreaks count="+q+"1"+q, xml);
            Assert.Contains("<ignoredError sqref="+q+"A1:B2"+q+" numberStoredAsText="+q+"1"+q+" formula="+q+"1"+q+"/>", xml);
        }
    }

    [Fact]
    public void GeneratedWorkbooksOpenAndConvert()
    {
        string dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "testdata", "xlsb"));
        string[] files = Directory.GetFiles(dir, "*.xlsb");
        Assert.True(files.Length >= 5);
        foreach (string file in files)
        {
            using Workbook wb = Workbook.Open(file);
            Assert.True(wb.IsBinary);
            Assert.Empty(wb.Defects);
            using var xlsx = new MemoryStream();
            wb.SaveAs(xlsx, OfficeFormat.Xlsx, new LossReport());
            using Workbook back = Workbook.Open(new MemoryStream(xlsx.ToArray()));
            using var bin = new MemoryStream();
            back.SaveAs(bin, OfficeFormat.Xlsb, new LossReport());
            using Workbook again = Workbook.Open(new MemoryStream(bin.ToArray()));
            Assert.Equal(wb.Sheets.Count, again.Sheets.Count);
        }
    }

    private static string Here([CallerFilePath] string path = "") => path;
}
