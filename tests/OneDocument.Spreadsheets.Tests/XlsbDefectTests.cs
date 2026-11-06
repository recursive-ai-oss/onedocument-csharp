using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Streaming;
using RecursiveAI.OneDocument.Xlsb;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>Damaged binary workbooks: each repair is a defect with a location, and nothing but a typed exception escapes.</summary>
public class XlsbDefectTests
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private static byte[] Record(int type, byte[] data)
    {
        using var ms = new MemoryStream();
        new XlsbRecordWriter(ms).Write(type, data);
        return ms.ToArray();
    }

    private static byte[] Wide(string s) => [.. BitConverter.GetBytes(s.Length), .. System.Text.Encoding.Unicode.GetBytes(s)];

    private static byte[] Cell(int type, int col, byte[] value) => Record(type, [.. BitConverter.GetBytes(col), .. BitConverter.GetBytes(0), .. value]);

    private static byte[] RowHeader(int row) => Record(XlsbRecordType.RowHdr, [.. BitConverter.GetBytes(row), .. BitConverter.GetBytes(0), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    /// <summary>A binary workbook with one sheet made of the given records, optionally with extra workbook records.</summary>
    private static byte[] Book(byte[] sheetRecords, byte[]? workbookExtra = null)
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "xl/workbook.bin")]);
        using var wb = new MemoryStream();
        wb.Write(Record(XlsbRecordType.BeginBook, []));
        if (workbookExtra is not null)
        {
            wb.Write(workbookExtra);
        }

        wb.Write(Record(XlsbRecordType.BeginBundleShs, []));
        wb.Write(Record(XlsbRecordType.BundleSh, [.. BitConverter.GetBytes(0), .. BitConverter.GetBytes(1), .. Wide("rId1"), .. Wide("S")]));
        wb.Write(Record(XlsbRecordType.EndBundleShs, []));
        wb.Write(Record(XlsbRecordType.EndBook, []));
        p.AddPart("/xl/workbook.bin", XlsbBook.WorkbookBinType, wb.ToArray());
        p.SetRelationships("/xl/workbook.bin", [new Relationship("rId1", Rel + "worksheet", "worksheets/sheet1.bin")]);
        using var sh = new MemoryStream();
        sh.Write(Record(XlsbRecordType.BeginSheet, []));
        sh.Write(Record(XlsbRecordType.BeginSheetData, []));
        sh.Write(sheetRecords);
        sh.Write(Record(XlsbRecordType.EndSheetData, []));
        sh.Write(Record(XlsbRecordType.EndSheet, []));
        p.AddPart("/xl/worksheets/sheet1.bin", XlsbBook.SheetBinType, sh.ToArray());
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();
    }

    private static List<Defect> ReadAll(byte[] file)
    {
        var ctx = new OpenContext();
        using XlsbStreamReader r = XlsbStreamReader.Open(new MemoryStream(file), ctx);
        using XlsbSheetCursor c = r.OpenSheet(0);
        while (c.ReadRow())
        {
            while (c.ReadCell())
            {
            }
        }

        return [.. TestBook.Defects(ctx)];
    }

    [Fact]
    public void ARowIndexOutsideTheSheetIsRepaired()
    {
        List<Defect> defects = ReadAll(Book([.. RowHeader(0x7FFFFFFF), .. Cell(XlsbRecordType.CellReal, 0, BitConverter.GetBytes(1.5))]));
        Defect d = Assert.Single(defects, x => x.Code == "xlsb.row-invalid");
        Assert.Equal(DefectSeverity.Repaired, d.Severity);
        Assert.Equal("/xl/worksheets/sheet1.bin", d.Location.Container);
    }

    [Fact]
    public void ACellRecordShorterThanItsHeaderIsDropped()
    {
        List<Defect> defects = ReadAll(Book([.. RowHeader(0), .. Record(XlsbRecordType.CellReal, [1, 2, 3])]));
        Defect d = Assert.Single(defects, x => x.Code == "xlsb.record-short");
        Assert.Equal(DefectSeverity.Dropped, d.Severity);
    }

    [Fact]
    public void ACellInAColumnOutsideTheSheetIsDropped()
    {
        List<Defect> defects = ReadAll(Book([.. RowHeader(0), .. Cell(XlsbRecordType.CellReal, 20000, BitConverter.GetBytes(1.5))]));
        Assert.Single(defects, x => x.Code == "xlsb.cell-column-invalid");
    }

    [Fact]
    public void AFormulaThatCannotBeReadKeepsItsCachedValue()
    {
        // FmlaNum: cached number, flags, cce, then a token string that stops in the middle of a token (PtgRef needs 6 bytes).
        byte[] value = [.. BitConverter.GetBytes(7.0), 0, 0, .. BitConverter.GetBytes(3), 0x24, 0, 0, .. BitConverter.GetBytes(0)];
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(new MemoryStream(Book([.. RowHeader(0), .. Cell(XlsbRecordType.FmlaNum, 0, value)])), ctx);
        Assert.Equal(7, wb.Sheets[0]["A1"].Number);
        Assert.Contains(TestBook.Defects(ctx), x => x.Code == "xlsb.formula-unreadable");
    }

    [Fact]
    public void AWorkbookPartThatIsTooShortIsATypedFailure()
    {
        // A BrtWbProp holding two bytes where four are needed.
        byte[] file = Book([], Record(XlsbRecordType.WbProp, [1, 0]));
        OfficeFormatException e = Assert.Throws<OfficeFormatException>(() => XlsbStreamReader.Open(new MemoryStream(file)));
        Assert.Equal("xlsb.part-unreadable", e.Defect.Code);
        Assert.Equal("/xl/workbook.bin", e.Location.Container);
    }

    [Fact]
    public void APackageWithoutAWorkbookPartIsATypedFailure()
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, []);
        p.AddPart("/xl/other.bin", XlsbBook.SheetBinType, new byte[] { 1 });
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        OfficeFormatException e = Assert.Throws<OfficeFormatException>(() => XlsbStreamReader.Open(new MemoryStream(ms.ToArray())));
        Assert.Equal("xlsb.no-main-part", e.Defect.Code);
    }
}
