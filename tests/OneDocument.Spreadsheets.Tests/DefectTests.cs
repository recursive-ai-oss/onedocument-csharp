using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class DefectTests
{
    private const string Decl = "<?xml version=\"1.0\"?>";

    private static string Sheet(string inner) => Decl + "<worksheet xmlns=\"" + TestBook.Main + "\">" + inner + "</worksheet>";

    private static string[] Codes(string sheetXml, Action<Workbook>? use = null)
    {
        var ctx = new OpenContext();
        using Workbook wb = TestBook.Open(TestBook.Bytes(sheetXml), ctx);
        if (use is null)
        {
            _ = wb.Sheets[0].Store.CellCount;
        }
        else
        {
            use(wb);
        }

        return [.. TestBook.Defects(ctx).Select(d => d.Code)];
    }

    [Fact]
    public void WrongRootInASheetIsFatalWithLocation()
    {
        using Workbook wb = TestBook.Open(TestBook.Bytes(Decl + "<notasheet xmlns=\"" + TestBook.Main + "\"/>"));
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => wb.Sheets[0].Load());
        Assert.Equal("xlsx.wrong-root", ex.Defect.Code);
        Assert.Equal("/xl/worksheets/sheet1.xml", ex.Location.Container);
    }

    [Fact]
    public void WrongRootInTheWorkbookPartIsFatal()
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes("<other/>"));
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Workbook.FromPackage(p));
        Assert.Equal("xlsx.wrong-root", ex.Defect.Code);
        Assert.Equal("/xl/workbook.xml", ex.Location.Container);
    }

    [Fact]
    public void NoMainPartIsFatal()
    {
        OpcPackage p = OpcPackage.Create();
        p.AddPart("/x.xml", "application/xml", Encoding.UTF8.GetBytes("<a/>"));
        Assert.Equal("xlsx.no-main-part", Assert.Throws<OfficeFormatException>(() => Workbook.FromPackage(p)).Defect.Code);
    }

    [Fact]
    public void WorkbookWithoutSheetsReadsAsEmptyWithADefect()
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes("<workbook xmlns=\"" + TestBook.Main + "\"/>"));
        var ctx = new OpenContext();
        using Workbook wb = Workbook.FromPackage(p, ctx);
        Assert.Empty(wb.Sheets);
        Assert.Contains("xlsx.no-sheets", TestBook.Defects(ctx).Select(d => d.Code));
        Worksheet added = wb.Sheets.Add("New");
        added["A1"].SetNumber(1);
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        ms.Position = 0;
        Assert.Equal(1, Workbook.Open(ms).Sheets["New"]["A1"].Number);
    }

    [Fact]
    public void SheetWithoutSheetDataIsRepairedOnWrite()
    {
        using Workbook wb = TestBook.Open(TestBook.Bytes(Sheet("<pageMargins left=\"1\" right=\"1\" top=\"1\" bottom=\"1\" header=\"0\" footer=\"0\"/>")));
        wb.Sheets[0]["B2"].SetNumber(5);
        byte[] saved = TestBook.Save(wb);
        string xml = Encoding.UTF8.GetString(WorkbookTests.PartBytes(saved, "/xl/worksheets/sheet1.xml"));
        Assert.True(xml.IndexOf("<sheetData>", StringComparison.Ordinal) < xml.IndexOf("<pageMargins", StringComparison.Ordinal), xml);
        using Workbook again = TestBook.Open(saved);
        Assert.Equal(5, again.Sheets[0]["B2"].Number);
    }

    [Fact]
    public void SelfClosedSheetDataGetsRows()
    {
        using Workbook wb = TestBook.Open(TestBook.Bytes(Sheet("<sheetData/>")));
        wb.Sheets[0]["A1"].SetString("x");
        byte[] saved = TestBook.Save(wb);
        Assert.Equal("x", TestBook.Open(saved).Sheets[0]["A1"].Text);
    }

    [Fact]
    public void PrefixedNamespaceSheetsReadAndWrite()
    {
        string xml = Decl + "<x:worksheet xmlns:x=\"" + TestBook.Main + "\"><x:dimension ref=\"A1\"/><x:sheetData><x:row r=\"1\"><x:c r=\"A1\"><x:v>3</x:v></x:c></x:row></x:sheetData></x:worksheet>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(xml));
        Assert.Equal(3, wb.Sheets[0]["A1"].Number);
        wb.Sheets[0]["B1"].SetString("s & t");
        wb.Sheets[0]["C1"].Formula = "A1+1";
        byte[] saved = TestBook.Save(wb);
        string text = Encoding.UTF8.GetString(WorkbookTests.PartBytes(saved, "/xl/worksheets/sheet1.xml"));
        Assert.Contains("<x:c r=\"B1\"", text, StringComparison.Ordinal);
        Assert.Contains("<x:dimension ref=\"A1:C1\"/>", text, StringComparison.Ordinal);
        using Workbook again = TestBook.Open(saved);
        Assert.Equal("s & t", again.Sheets[0]["B1"].Text);
        Assert.Equal("A1+1", again.Sheets[0]["C1"].Formula);
    }

    [Fact]
    public void ACommentThatQuotesTheInternalMarkerDoesNotConfuseTheWritePath()
    {
        string xml = Decl + "<worksheet xmlns=\"" + TestBook.Main + "\"><!-- <sheetData x-split=\"1\"/> --><dimension ref=\"A1\"/><sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData></worksheet>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(xml));
        wb.Sheets[0]["C3"].SetNumber(2);
        byte[] saved = TestBook.Save(wb);
        string text = Encoding.UTF8.GetString(WorkbookTests.PartBytes(saved, "/xl/worksheets/sheet1.xml"));
        Assert.Contains("<dimension ref=\"A1:C3\"/>", text, StringComparison.Ordinal);
        Assert.Contains("<!-- <sheetData x-split=\"1\"/> -->", text, StringComparison.Ordinal);
        Assert.Equal(2, TestBook.Open(saved).Sheets[0]["C3"].Number);
    }

    [Fact]
    public void StrictNamespacesReadThroughTheSameModel()
    {
        const string strict = "http://purl.oclc.org/ooxml/spreadsheetml/main";
        string xml = Decl + "<worksheet xmlns=\"" + strict + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"b\"><v>1</v></c></row></sheetData></worksheet>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(xml));
        Assert.True(wb.Sheets[0]["A1"].Value.Boolean);
    }

    [Fact]
    public void RowAndCellRepairsCarryCodes()
    {
        Assert.Contains("xlsx.row-index-invalid", Codes(Sheet("<sheetData><row r=\"0\"><c r=\"A1\"/></row></sheetData>")));
        Assert.Contains("xlsx.cell-row-mismatch", Codes(Sheet("<sheetData><row r=\"2\"><c r=\"A1\"><v>1</v></c></row></sheetData>")));
        Assert.Contains("xlsx.cell-type-unknown", Codes(Sheet("<sheetData><row r=\"1\"><c r=\"A1\" t=\"zz\"><v>1</v></c></row></sheetData>")));
        Assert.Contains("xlsx.formula-type-unknown", Codes(Sheet("<sheetData><row r=\"1\"><c r=\"A1\"><f t=\"weird\">1</f></c></row></sheetData>")));
        Assert.Contains("xlsx.sheetdata-unexpected-element", Codes(Sheet("<sheetData><oops/></sheetData>")));
    }

    [Fact]
    public void ColumnAndMergeRepairsCarryCodes()
    {
        string[] codes = Codes(Sheet("<cols><col min=\"5\" max=\"2\"/></cols><sheetData/><mergeCells><mergeCell ref=\"garbage\"/></mergeCells>"), wb =>
        {
            Assert.Empty(wb.Sheets[0].Columns);
            Assert.Empty(wb.Sheets[0].MergedCells);
        });
        Assert.Contains("xlsx.column-range-invalid", codes);
        Assert.Contains("xlsx.merge-range-invalid", codes);
    }

    [Fact]
    public void MissingSheetPartAndSharedStringAreReportedNotFatal()
    {
        OpcPackage p = OpcPackage.Open(new MemoryStream(TestBook.Bytes()));
        p.RemovePart("/xl/worksheets/sheet2.xml");
        var ctx = new OpenContext();
        using Workbook wb = Workbook.FromPackage(p, ctx);
        Assert.Equal(0, wb.Sheets["Other"].Store.RowCount);
        Assert.Contains("xlsx.sheet-part-missing", TestBook.Defects(ctx).Select(d => d.Code));

        var ctx2 = new OpenContext();
        string bad = Sheet("<sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>99</v></c></row></sheetData>");
        using Workbook wb2 = TestBook.Open(TestBook.Bytes(bad), ctx2);
        Assert.Equal(string.Empty, wb2.Sheets[0]["A1"].Text);
        Assert.Contains("xlsx.shared-string-missing", TestBook.Defects(ctx2).Select(d => d.Code));
    }

    [Fact]
    public void DuplicateSheetNamesAreReported()
    {
        OpcPackage p = OpcPackage.Open(new MemoryStream(TestBook.Bytes()));
        var ctx = new OpenContext();
        byte[] wbXml = p.FindPart("/xl/workbook.xml")!.GetBytes().ToArray();
        p.FindPart("/xl/workbook.xml")!.SetBytes(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(wbXml).Replace("name=\"Other\"", "name=\"data\"", StringComparison.Ordinal)));
        using Workbook wb = Workbook.FromPackage(p, ctx);
        Assert.Contains("xlsx.sheet-name-duplicate", TestBook.Defects(ctx).Select(d => d.Code));
        Assert.Equal(0, wb.Sheets["DATA"].Index);
    }

    [Fact]
    public void LimitsApplyToCellsAndFormulas()
    {
        var tight = new OfficeLimits { MaxRecordCount = 3 };
        using Workbook wb = TestBook.Open(TestBook.Bytes(), new OpenContext(tight));
        OfficeLimitException ex = Assert.Throws<OfficeLimitException>(() => wb.Sheets[0].Load());
        Assert.Equal(nameof(OfficeLimits.MaxRecordCount), ex.LimitName);

        using Workbook wb2 = TestBook.Open(TestBook.Bytes(), new OpenContext(new OfficeLimits { MaxFormulaLength = 4 }));
        Assert.Equal(nameof(OfficeLimits.MaxFormulaLength), Assert.Throws<OfficeLimitException>(() => wb2.Sheets[0].Load()).LimitName);

        using Workbook wb3 = TestBook.Open();
        Assert.Throws<OfficeLimitException>(() => wb3.Sheets[1]["A1"].Formula = new string('1', 9000));
    }

    [Fact]
    public void ABinaryWorkbookIsOpenedAsABinaryWorkbook()
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.bin")]);
        p.AddPart("/xl/workbook.bin", "application/vnd.ms-excel.sheet.binary.macroEnabled.main", new byte[] { 0x83, 0x01, 0x00, 0x84, 0x01, 0x00 });
        using Workbook wb = Workbook.FromPackage(p);
        Assert.True(wb.IsBinary);
        Assert.Equal(OfficeFormat.Xlsb, wb.Format);
    }

    [Fact]
    public void WritingMacroFreeFormatWithAMacroProjectIsRefused()
    {
        OpcPackage p = OpcPackage.Open(new MemoryStream(TestBook.Bytes()));
        p.AddPart("/xl/vbaProject.bin", "application/vnd.ms-office.vbaProject", new byte[] { 1 });
        using Workbook wb = Workbook.FromPackage(p);
        using var ms = new MemoryStream();
        Assert.Throws<OfficeUnsupportedException>(() => wb.SaveAs(ms, OfficeFormat.Xlsx.Equals(wb.Format) ? OfficeFormat.Xltx : OfficeFormat.Xlsx));
        wb.SaveAs(ms, OfficeFormat.Xlsm);
        Assert.True(ms.Length > 0);
    }
}
