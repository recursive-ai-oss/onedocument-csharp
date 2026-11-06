using System.Security.Cryptography;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Xlsb;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class XlsbTests
{
    private static byte[] ToXlsb(Workbook wb, LossReport? loss = null)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsb, loss ?? new LossReport());
        return ms.ToArray();
    }

    private static Workbook Reopen(byte[] bytes) => Workbook.Open(new MemoryStream(bytes));

    private static Dictionary<string, string> PartHashes(byte[] bytes)
    {
        using OpcPackage p = OpcPackage.Open(new MemoryStream(bytes));
        var map = new Dictionary<string, string>();
        foreach (IPackagePart part in p.Parts)
        {
            map[part.Name] = Convert.ToHexString(SHA256.HashData(part.GetBytes().Span));
        }

        return map;
    }

    [Fact]
    public void RecordFramingRoundTripsLongTypesAndSizes()
    {
        using var ms = new MemoryStream();
        var w = new XlsbRecordWriter(ms);
        byte[] big = new byte[300_000];
        new Random(7).NextBytes(big);
        w.Write(0, [1, 2, 3]);
        w.Write(127, []);
        w.Write(128, big.AsSpan(0, 127));
        w.Write(16383, big.AsSpan(0, 16384));
        w.Write(637, big);
        var r = new XlsbRecordReader(ms.ToArray(), new OpenContext(), "/t.bin");
        Assert.True(r.Read());
        Assert.Equal(0, r.Type);
        Assert.Equal(3, r.Data.Length);
        Assert.True(r.Read());
        Assert.Equal(127, r.Type);
        Assert.True(r.Read());
        Assert.Equal(128, r.Type);
        Assert.Equal(127, r.Data.Length);
        Assert.True(r.Read());
        Assert.Equal(16383, r.Type);
        Assert.Equal(16384, r.Data.Length);
        Assert.True(r.Read());
        Assert.Equal(637, r.Type);
        Assert.True(r.Data.SequenceEqual(big));
        Assert.False(r.Read());
        Assert.False(r.WasTruncated);

        // The same bytes through a stream, in small reads.
        var s = new XlsbRecordReader(new MemoryStream(ms.ToArray()), new OpenContext(), "/t.bin");
        int n = 0;
        while (s.Read())
        {
            n++;
        }

        Assert.Equal(5, n);
    }

    [Fact]
    public void TruncatedRecordIsADefectNotAnException()
    {
        using var ms = new MemoryStream();
        var w = new XlsbRecordWriter(ms);
        w.Write(5, new byte[40]);
        w.Write(6, new byte[40]);
        byte[] cut = ms.ToArray()[..^10];
        var ctx = new OpenContext();
        var r = new XlsbRecordReader(cut, ctx, "/t.bin");
        Assert.True(r.Read());
        Assert.False(r.Read());
        Assert.True(r.WasTruncated);
        Assert.Contains(ctx.Defects is DefectCollection d ? d : throw new InvalidOperationException(), x => x.Code == "xlsb.record-truncated");
    }

    [Fact]
    public void RecordLargerThanTheLimitRaisesALimitException()
    {
        using var ms = new MemoryStream();
        new XlsbRecordWriter(ms).Write(5, new byte[5000]);
        var ctx = new OpenContext(new OfficeLimits { MaxRecordSize = 1000 });
        var r = new XlsbRecordReader(ms.ToArray(), ctx, "/t.bin");
        Assert.Throws<OfficeLimitException>(() => r.Read());
    }

    public static IEnumerable<object[]> Formulas() =>
    [
        ["1+2*3"], ["(1+2)*3"], ["-A1"], ["A1%"], ["\"a\"&\"b\""], ["A1:B3"], ["$A$1+B$2+$C3"], ["Sheet1!A1+'My Sheet'!B2"], ["SUM(A1:A10)"],
        ["SUM(1,2,3)"], ["AVERAGE(A1:B2,5)"], ["IF(A1>0,1,2)"], ["IF(A1>0,\"x\")"], ["CHOOSE(2,A1,B1,C1)"], ["IFERROR(A1/B1,0)"], ["ROUND(A1,2)"],
        ["TRUE"], ["#N/A"], ["{1,2;3,4}"], ["{\"a\",TRUE,#DIV/0!}"], ["Total+1"], ["Sheet1!Local*2"], ["SUMPRODUCT(A1:A3*B1:B3)"], ["VLOOKUP(A1,B1:D9,2,FALSE)"],
        ["A:A"], ["1:3"], ["Sheet1!A1:Sheet1!B2"], ["A1:B2 B2:C3"], ["SUM((A1,B2))"], ["NOW()"], ["_xlfn.XLOOKUP(A1,B:B,C:C)"], ["65536"], ["65535"], ["0.5"], ["1E+300"],
        ["IF(A1,IF(B1,1,2),3)"], ["MAX(IF(A1:A3>1,A1:A3))"], ["LEN(\"héllo\")"], ["COUNTIF(A1:A9,\">1\")"], ["Sheet1:Sheet2!A1"], ["INDEX(A1:C3,2,)"],
    ];

    private static XlsbFormulaContext Context()
    {
        var cx = new XlsbFormulaContext();
        cx.SheetNames.AddRange(["Sheet1", "My Sheet", "Sheet2"]);
        cx.SupBooks.Add(new XlsbSupBook(XlsbSupBookKind.Self));
        cx.Names.Add(new XlsbDefinedName { Name = "Total", SheetIndex = -1 });
        cx.Names.Add(new XlsbDefinedName { Name = "Local", SheetIndex = 0 });
        return cx;
    }

    private static string Canon(FormulaNode n) => FormulaPrinter.Print(n, new FormulaPrintOptions { Form = FormulaForm.File });

    [Theory]
    [MemberData(nameof(Formulas))]
    public void FormulaTokensRoundTripThroughTheTree(string text)
    {
        XlsbFormulaContext cx = Context();
        FormulaNode ast = FormulaParser.Parse(text);
        Assert.True(XlsbFormula.TryEncode(ast, cx, XlsbFormulaKind.Cell, 4, 2, out byte[] rgce, out byte[] rgcb, out string? problem), problem);
        Assert.True(XlsbFormula.TryDecode(rgce, rgcb, cx, XlsbFormulaKind.Cell, 4, 2, out FormulaNode back, out string? why), why);
        Assert.Equal(Canon(ast), Canon(back));

        // Tokens -> tree -> tokens is stable: what was decoded encodes to the same bytes.
        Assert.True(XlsbFormula.TryEncode(back, cx, XlsbFormulaKind.Cell, 4, 2, out byte[] rgce2, out byte[] rgcb2, out _));
        Assert.Equal(rgce, rgce2);
        Assert.Equal(rgcb, rgcb2);
    }

    [Fact]
    public void IfTokensCarryTheControlOffsetsTheFormatDefines()
    {
        XlsbFormulaContext cx = Context();
        Assert.True(XlsbFormula.TryEncode(FormulaParser.Parse("IF(A1,1,2)"), cx, XlsbFormulaKind.Cell, 0, 0, out byte[] rgce, out _, out _));
        // A1 (class value), PtgAttrIf, 1, PtgAttrGoto, 2, PtgAttrGoto, PtgFuncVar(IF).
        Assert.Equal(0x44, rgce[0]);
        Assert.Equal([0x19, 0x02], rgce[7..9]);
        int ifOffset = rgce[9] | (rgce[10] << 8);
        Assert.Equal(3 + 4, ifOffset); // PtgInt (3 bytes) + PtgAttrGoto (4 bytes)
        Assert.Equal(0x42, rgce[^4]);
        Assert.Equal(3, rgce[^3]);
        Assert.Equal(1, rgce[^2]);
    }

    [Fact]
    public void WorkbookRoundTripsThroughBinary()
    {
        using Workbook source = TestBook.Open();
        var loss = new LossReport();
        byte[] bin = ToXlsb(source, loss);
        using Workbook wb = Reopen(bin);
        Assert.Equal(OfficeFormat.Xlsb, wb.Format);
        Assert.True(wb.IsBinary);
        Assert.Equal(2, wb.Sheets.Count);
        Assert.Equal("Data", wb.Sheets[0].Name);
        Assert.Equal(SheetState.Hidden, wb.Sheets[1].State);
        Assert.Equal(5, wb.Sheets[1].SheetId);
        Worksheet s = wb.Sheets["Data"];
        Worksheet o = source.Sheets["Data"];
        Assert.Equal("First", s["A1"].Text);
        Assert.Equal(1.5, s["B1"].Number);
        Assert.True(s["C1"].Value.Boolean);
        Assert.Equal(CellError.DivideByZero, s["D1"].Value.Error);
        Assert.Equal("B1*2", s["A2"].Formula);
        Assert.Equal(3, s["A2"].Number);
        Assert.Equal("helloxx & more", s["B2"].Text);
        Assert.Equal(" inline ", s["C2"].Text);
        Assert.Equal(CellFormulaKind.Array, s["D2"].FormulaDetail!.Kind);
        Assert.Equal("D2:D3", s["D2"].FormulaDetail!.Reference);
        Assert.Equal("A2+1", s["A4"].Formula);
        // Shared formulas are written out as one formula per cell.
        Assert.Equal("B2+1", s["B4"].Formula);
        Assert.Equal(o.UsedRange().ToString(), s.UsedRange().ToString());
        Assert.Equal(20, s.GetRow(1).Height);
        Assert.Single(s.MergedCells);
        Assert.Equal("Other!$A$1", wb.Names.First(n => n.Name == "Local").Formula);
    }

    [Fact]
    public void UntouchedBinaryWorkbookSavesEveryPartIdentical()
    {
        byte[] bin;
        using (Workbook source = TestBook.Open())
        {
            bin = ToXlsb(source);
        }

        using var wb = Reopen(bin);
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsb);
        Dictionary<string, string> before = PartHashes(bin);
        Dictionary<string, string> after = PartHashes(ms.ToArray());
        Assert.Equal(before.Count, after.Count);
        foreach ((string name, string hash) in before)
        {
            Assert.True(after.TryGetValue(name, out string? h) && h == hash, name);
        }
    }

    [Fact]
    public void EditingOneSheetRewritesOnlyThatSheet()
    {
        byte[] bin;
        using (Workbook source = TestBook.Open())
        {
            bin = ToXlsb(source);
        }

        using var wb = Reopen(bin);
        wb.Sheets["Other"]["B2"].Value = 42;
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsb);
        Dictionary<string, string> before = PartHashes(bin);
        Dictionary<string, string> after = PartHashes(ms.ToArray());
        var changed = before.Where(kv => !after.TryGetValue(kv.Key, out string? h) || h != kv.Value).Select(kv => kv.Key).Order().ToList();
        Assert.Equal(["/xl/worksheets/sheet2.bin"], changed);
        using Workbook again = Reopen(ms.ToArray());
        Assert.Equal(42, again.Sheets["Other"]["B2"].Number);
        Assert.Equal(7, again.Sheets["Other"]["A1"].Number);
    }

    [Fact]
    public void BinaryWorkbookConvertsToSpreadsheetMl()
    {
        byte[] bin;
        using (Workbook source = TestBook.Open())
        {
            bin = ToXlsb(source);
        }

        using var wb = Reopen(bin);
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx, new LossReport());
        using Workbook x = Reopen(ms.ToArray());
        Assert.Equal(OfficeFormat.Xlsx, x.Format);
        Assert.Equal("First", x.Sheets["Data"]["A1"].Text);
        Assert.Equal("B1*2", x.Sheets["Data"]["A2"].Formula);
        Assert.DoesNotContain("xb:", System.Text.Encoding.UTF8.GetString(x.Package.FindPart(x.MainPartName)!.GetBytes().Span));
    }
}
