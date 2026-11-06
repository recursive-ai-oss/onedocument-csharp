using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests.Biff;

/// <summary>One crafted file per defect code of the legacy workbook reader.</summary>
public class XlsDefectTests
{
    private static Defect[] Read(byte[] file, OfficeLimits? limits = null)
    {
        var ctx = new OpenContext(limits);
        using Workbook wb = XlsConverter.ToWorkbook(XlsBuilder.Source(file), ctx);
        return ((DefectCollection)ctx.Defects).ToArray();
    }

    private static void Expect(string code, byte[] file, OfficeLimits? limits = null, DefectKind? kind = null)
    {
        Defect[] defects = Read(file, limits);
        Defect d = Assert.Single(defects, x => x.Code == code);
        if (kind is { } k)
        {
            Assert.Equal(k, d.Kind);
        }
        else
        {
            Assert.Equal(DefectKind.Record, d.Kind is DefectKind.Formula or DefectKind.Reference ? DefectKind.Record : d.Kind);
        }
    }

    private static byte[] Sheet(params byte[][] records) => XlsBuilder.Workbook([("S", records)]);

    private static byte[] Tok(params int[] bytes) => bytes.Select(b => (byte)b).ToArray();

    private static byte[] FormulaCell(byte[] rgce, int declared = -1) =>
        XlsBuilder.Rec(0x0006, new Bytes().U16(0).U16(0).U16(15).Raw(XlsBuilder.NumValue(1)).U16(0).U32(0).U16(declared < 0 ? rgce.Length : declared).Raw(rgce));

    [Fact]
    public void TruncatedRecordAndTrailingBytes()
    {
        byte[] good = Sheet(XlsBuilder.Number(0, 0, 15, 1));
        Expect("biff.truncated-record", [.. good, 0x00, 0x02, 0xFF, 0xFF, 1, 2]);
        Expect("biff.trailing-bytes", [.. good, 1, 2]);
    }

    [Fact]
    public void NoBofNoWorkbookStreamNoSheet()
    {
        var ex = Assert.Throws<OfficeFormatException>(() => XlsConverter.ToWorkbook(XlsBuilder.Source(XlsBuilder.Rec(0x0042, Tok(0xB0, 0x04)))));
        Assert.Equal("biff.no-bof", ex.Defect.Code);
        var ex2 = Assert.Throws<OfficeFormatException>(() => XlsConverter.ToWorkbook(XlsBuilder.Source(XlsBuilder.InCompoundFile(Tok(1, 2, 3, 4), "Other"))));
        Assert.Equal("biff.no-workbook-stream", ex2.Defect.Code);
        byte[] onlyGlobals = [.. XlsBuilder.Rec(0x0409, new Bytes().U16(0).U16(0x0100).U16(0).U16(0)), .. XlsBuilder.Eof()];
        var ex3 = Assert.Throws<OfficeFormatException>(() => XlsConverter.ToWorkbook(XlsBuilder.Source(onlyGlobals)));
        Assert.Equal("biff.no-sheet", ex3.Defect.Code);
    }

    [Fact]
    public void MissingEof()
    {
        byte[] good = Sheet(XlsBuilder.Number(0, 0, 15, 1));
        Expect("biff.missing-eof", good[..^4]);
    }

    [Fact]
    public void SheetProblems()
    {
        byte[] two = XlsBuilder.Workbook([("A", [XlsBuilder.Number(0, 0, 15, 1)]), ("B", [XlsBuilder.Number(0, 0, 15, 2)])]);
        var rs = BiffRecordSet.Parse(two, new OpenContext());
        int secondBof = rs.Records.Where(r => r.Type == 0x0809).Skip(2).First().Offset;
        Expect("biff.sheet-missing", two[..secondBof]);

        byte[] badName = XlsBuilder.Workbook([("a:b", [])]);
        Expect("biff.sheet-name", badName);

        byte[] hidden = XlsBuilder.Workbook([("A", [])]);
        var rs2 = BiffRecordSet.Parse(hidden, new OpenContext());
        BiffRecord bound = rs2.Records.First(r => r.Type == 0x0085);
        hidden[bound.DataOffset + 4] = 1;
        Expect("biff.no-visible-sheet", hidden);
    }

    [Fact]
    public void CellProblems()
    {
        Expect("biff.cell-out-of-range", Sheet(XlsBuilder.Number(0, 20000, 15, 1)), kind: DefectKind.Reference);
        Expect("biff.string-orphan", Sheet(XlsBuilder.StringRec("x")));
        Expect("biff.colinfo-range", Sheet(XlsBuilder.ColInfo(5, 2, 256)));
        Expect("biff.merge-range", Sheet(XlsBuilder.Merge((5, 2, 0, 0))));
        byte[] file = XlsBuilder.Workbook([("S", [XlsBuilder.LabelSst(0, 0, 15, 5)])], sst: ["a"]);
        Expect("biff.sst-index", file, kind: DefectKind.Reference);
    }

    [Fact]
    public void SharedStringTableProblems()
    {
        byte[] sst = [.. new Bytes().U32(3).U32(3).U16(1).U8(0).U8('a').ToArray()];
        byte[] file = XlsBuilder.Workbook([("S", [])], extraGlobals: [XlsBuilder.Rec(0x00FC, sst)]);
        Expect("biff.sst-short", file);
        byte[] cut = [.. new Bytes().U32(1).U32(1).U16(9).U8(0).U8('a').ToArray()];
        Expect("biff.sst-truncated", XlsBuilder.Workbook([("S", [])], extraGlobals: [XlsBuilder.Rec(0x00FC, cut)]));
    }

    [Fact]
    public void FormulaProblems()
    {
        Expect("biff.formula-length", Sheet(FormulaCell(Tok(0x1E, 1, 0), declared: 50)), kind: DefectKind.Record);
        Expect("biff.formula-token", Sheet(FormulaCell(Tok(0x7F))), kind: DefectKind.Formula);
        Expect("biff.formula-stack", Sheet(FormulaCell(Tok(0x1E, 1, 0, 0x1E, 2, 0))), kind: DefectKind.Formula);
        Expect("biff.formula-name", Sheet(FormulaCell(Tok(0x23, 9, 0, 0, 0))), kind: DefectKind.Formula);
        Expect("biff.formula-exp-missing", Sheet(FormulaCell(Tok(0x01, 3, 0, 3, 0))), kind: DefectKind.Formula);
        Expect("biff.formula-exp", Sheet(FormulaCell(Tok(0x01, 0, 0, 0, 0, 0x1E, 1, 0))), kind: DefectKind.Formula);
        Expect("biff.formula-table", Sheet(FormulaCell(Tok(0x02, 0, 0, 0, 0, 0x1E, 1, 0))), kind: DefectKind.Formula);
        Expect("biff.formula-pivot-name", Sheet(FormulaCell(Tok(0x18, 0x1D, 0, 0, 0, 0))), kind: DefectKind.Formula);
        Expect("biff.formula-macro-function", Sheet(FormulaCell(Tok(0x1E, 1, 0, 0x22, 1, 0x01, 0x80))), kind: DefectKind.Formula);
        Expect("biff.formula-text-invalid", Sheet(FormulaCell(Tok(0x17, 1, 0, 0))), kind: DefectKind.Formula);
        Expect("biff.formula-too-long", Sheet(FormulaCell(Tok(0x1E, 1, 0, 0x1E, 2, 0, 0x03, 0x1E, 3, 0, 0x03, 0x1E, 4, 0, 0x03))), new OfficeLimits { MaxFormulaLength = 5 }, DefectKind.Formula);
    }

    [Fact]
    public void ConditionalFormatAndNameProblems()
    {
        byte[] condFmt = XlsBuilder.Rec(0x01B0, new Bytes().U16(1).U16(1).U16(0).U16(3).U16(0).U16(0).U16(1).U16(0).U16(3).U16(0).U16(0));
        byte[] cf = XlsBuilder.Rec(0x01B1, new Bytes().U8(1).U8(5).U16(100).U16(0));
        Expect("biff.cf-length", Sheet(condFmt, cf));

        byte[] name1 = XlsBuilder.Rec(0x0018, new Bytes().U16(0).U8(0).U8(1).U16(3).U16(0).U16(0).U8(0).U8(0).U8(0).U8(0).Chars8("N").Raw(Tok(0x1E, 1, 0)));
        Expect("biff.name-repeated", XlsBuilder.Workbook([("S", [])], extraGlobals: [name1, name1]), kind: DefectKind.Reference);
        byte[] bad = XlsBuilder.Rec(0x0018, new Bytes().U16(0).U8(0).U8(4).U16(3).U16(0).U16(0).U8(0).U8(0).U8(0).U8(0).Chars8("1 2 ").Raw(Tok(0x1E, 1, 0)));
        Expect("biff.name-rejected", XlsBuilder.Workbook([("S", [])], extraGlobals: [bad]), kind: DefectKind.Reference);
    }

    [Fact]
    public void CodePageDefectIsReportedOncePerWorkbookWithKindEncoding()
    {
        byte[] file = XlsBuilder.Workbook([("S", [])], codePage: XlsBuilder.Rec(0x0042, new Bytes().U16(65000 + 1234)));
        Expect("biff.codepage-unavailable", file, kind: DefectKind.Encoding);
    }
}
