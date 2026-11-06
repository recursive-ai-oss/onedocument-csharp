using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests.Biff;

public class XlsReaderTests
{
    private static Workbook Convert(byte[] bytes, out DefectCollection defects, out LossReport loss)
    {
        var ctx = new OpenContext();
        defects = (DefectCollection)ctx.Defects;
        Workbook wb = XlsConverter.ToWorkbook(XlsBuilder.Source(bytes), ctx);
        loss = wb.ImportLoss!;
        return wb;
    }

    private static byte[] OneSheet(params byte[][] records) => XlsBuilder.Workbook([("Sheet1", records)]);

    private static string Text(Worksheet ws, string a1) => ws[a1].Text;

    [Fact]
    public void RecordSetKeepsUnknownRecordsAndReportsATruncatedOne()
    {
        byte[] stream = [.. XlsBuilder.Rec(0x7777, [1, 2, 3]), .. XlsBuilder.Rec(0x0042, [0xB0, 0x04]), 0x09, 0x08, 0xFF, 0x00, 1];
        var ctx = new OpenContext();
        var rs = BiffRecordSet.Parse(stream, ctx);
        Assert.Equal(2, rs.Records.Count);
        Assert.Equal(0x7777, rs.Records[0].Type);
        Assert.Equal([1, 2, 3], rs.Data(rs.Records[0]).ToArray());
        Defect d = Assert.Single(((DefectCollection)ctx.Defects).ToArray());
        Assert.Equal("biff.truncated-record", d.Code);
        Assert.Equal(DefectKind.Record, d.Kind);
        Assert.Equal(13, d.Location.Offset);
    }

    [Fact]
    public void CellKindsAreRead()
    {
        byte[] file = XlsBuilder.Workbook(
            [("Sheet1",
            [
                XlsBuilder.Number(0, 0, 15, 3.5),
                XlsBuilder.Rk(0, 1, 15, (5u << 2) | 2),
                XlsBuilder.Rk(0, 2, 15, (12345u << 2) | 3),
                XlsBuilder.Rk(0, 3, 15, unchecked((uint)(-7 << 2)) | 2),
                XlsBuilder.Rec(0x00BD, new Bytes().U16(1).U16(0).U16(15).U32((1u << 2) | 2).U16(15).U32((2u << 2) | 2).U16(2)),
                XlsBuilder.LabelSst(2, 0, 15, 1),
                XlsBuilder.LabelSst(2, 1, 0, 0),
                XlsBuilder.Blank(3, 0, 16),
                XlsBuilder.Rec(0x00BE, new Bytes().U16(3).U16(1).U16(16).U16(16).U16(2)),
                XlsBuilder.BoolErr(4, 0, 15, 1, error: false),
                XlsBuilder.BoolErr(4, 1, 15, 0x2A, error: true),
                XlsBuilder.BoolErr(4, 2, 15, 0x07, error: true),
                XlsBuilder.Label(5, 0, 15, "plain label"),
            ])],
            sst: ["first", "Ünï ✓ wide"]);
        using Workbook wb = Convert(file, out DefectCollection defects, out _);
        Worksheet ws = wb.Sheets[0];
        Assert.Equal(3.5, ws["A1"].Number);
        Assert.Equal(5, ws["B1"].Number);
        Assert.Equal(123.45, ws["C1"].Number);
        Assert.Equal(-7, ws["D1"].Number);
        Assert.Equal(1, ws["A2"].Number);
        Assert.Equal(2, ws["B2"].Number);
        Assert.Equal("Ünï ✓ wide", Text(ws, "A3"));
        Assert.Equal("first", Text(ws, "B3"));
        Assert.True(ws["A4"].Exists);
        Assert.Equal(16, ws["A4"].StyleIndex);
        Assert.Equal(16, ws["B4"].StyleIndex);
        Assert.True(ws["A5"].Value.Boolean);
        Assert.Equal(CellError.NotAvailable, ws["B5"].Value.Error);
        Assert.Equal(CellError.DivideByZero, ws["C5"].Value.Error);
        Assert.Equal("plain label", Text(ws, "A6"));
        Assert.Empty(defects.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void SharedStringsSplitByContinueRestartWithAFlagByte(int split)
    {
        string[] strings = ["short", new string('a', 60) + "Ü", "wide ✓ text that goes on and on", "tail"];
        byte[] file = XlsBuilder.Workbook([("S", [XlsBuilder.LabelSst(0, 0, 15, 1), XlsBuilder.LabelSst(1, 0, 15, 2), XlsBuilder.LabelSst(2, 0, 15, 3)])], sst: strings, sstSplit: split);
        using Workbook wb = Convert(file, out DefectCollection defects, out _);
        Assert.Equal(strings[1], Text(wb.Sheets[0], "A1"));
        Assert.Equal(strings[2], Text(wb.Sheets[0], "A2"));
        Assert.Equal(strings[3], Text(wb.Sheets[0], "A3"));
        Assert.Empty(defects.ToArray());
    }

    private static byte[] Tokens(params Action<Bytes>[] parts)
    {
        var b = new Bytes();
        foreach (Action<Bytes> p in parts)
        {
            p(b);
        }

        return b.ToArray();
    }

    private static Action<Bytes> Ref(int row, int col, bool rowRel = true, bool colRel = true) =>
        b => b.U8(0x24).U16(row).U16(col | (colRel ? 0x4000 : 0) | (rowRel ? 0x8000 : 0));

    private static Action<Bytes> Int(int v) => b => b.U8(0x1E).U16(v);

    private static Action<Bytes> Op(int token) => b => b.U8(token);

    [Fact]
    public void FormulaCachedValuesStayAndStringResultsComeFromTheStringRecord()
    {
        byte[] file = OneSheet(
            XlsBuilder.Number(0, 0, 15, 4),
            XlsBuilder.Formula(0, 1, 15, XlsBuilder.NumValue(5), Tokens(Ref(0, 0), Int(1), Op(0x03))),
            XlsBuilder.Formula(0, 2, 15, XlsBuilder.StrValue(), Tokens(b => b.U8(0x17).U8(2).U8(0).U8('h').U8('i'))),
            XlsBuilder.StringRec("hi"),
            XlsBuilder.Formula(0, 3, 15, XlsBuilder.BoolValue(true), Tokens(b => b.U8(0x1D).U8(1))),
            XlsBuilder.Formula(0, 4, 15, XlsBuilder.ErrValue(0x07), Tokens(Int(1), Int(0), Op(0x06))),
            XlsBuilder.Formula(0, 5, 15, XlsBuilder.NumValue(99), Tokens(Ref(0, 0, rowRel: false, colRel: false), Op(0x14))));
        using Workbook wb = Convert(file, out DefectCollection defects, out _);
        Worksheet ws = wb.Sheets[0];
        Assert.Equal("A1+1", ws["B1"].Formula);
        Assert.Equal(5, ws["B1"].Number);
        Assert.Equal("\"hi\"", ws["C1"].Formula);
        Assert.Equal("hi", Text(ws, "C1"));
        Assert.Equal("TRUE", ws["D1"].Formula);
        Assert.True(ws["D1"].Value.Boolean);
        Assert.Equal("1/0", ws["E1"].Formula);
        Assert.Equal(CellError.DivideByZero, ws["E1"].Value.Error);
        Assert.Equal("$A$1%", ws["F1"].Formula);
        Assert.Equal(99, ws["F1"].Number);
        Assert.Empty(defects.ToArray());
    }

    [Fact]
    public void SharedFormulaCellsGetTheirOwnRelativeText()
    {
        // B1 holds the shared formula A1*2; B2 and B3 point at it with PtgExp. PtgRefN offsets are relative to each cell.
        byte[] shrfmla = XlsBuilder.Rec(0x04BC, new Bytes().U16(0).U16(2).U8(1).U8(1).U8(0).U8(3).U16(9)
            .Raw(Tokens(b => b.U8(0x2C).U16(0).U16(0xC000 | 0x3FFF), Int(2), Op(0x05))));
        byte[] exp1 = Tokens(b => b.U8(0x01).U16(0).U16(1));
        byte[] file = OneSheet(
            XlsBuilder.Number(0, 0, 15, 1),
            XlsBuilder.Number(1, 0, 15, 2),
            XlsBuilder.Number(2, 0, 15, 3),
            XlsBuilder.Formula(0, 1, 15, XlsBuilder.NumValue(2), exp1),
            shrfmla,
            XlsBuilder.Formula(1, 1, 15, XlsBuilder.NumValue(4), exp1),
            XlsBuilder.Formula(2, 1, 15, XlsBuilder.NumValue(6), exp1));
        using Workbook wb = Convert(file, out DefectCollection defects, out _);
        Worksheet ws = wb.Sheets[0];
        Assert.Equal("A1*2", ws["B1"].Formula);
        Assert.Equal("A2*2", ws["B2"].Formula);
        Assert.Equal("A3*2", ws["B3"].Formula);
        Assert.Equal(6, ws["B3"].Number);
        Assert.Empty(defects.ToArray());
    }

    [Fact]
    public void ArrayFormulaAnchorCarriesTheRangeAndOtherCellsKeepValues()
    {
        byte[] array = XlsBuilder.Rec(0x0221, new Bytes().U16(0).U16(1).U8(2).U8(2).U16(0).U32(0).U16(9).Raw(Tokens(Ref(0, 0), Int(2), Op(0x05))));
        byte[] exp = Tokens(b => b.U8(0x01).U16(0).U16(2));
        byte[] file = OneSheet(
            XlsBuilder.Formula(0, 2, 15, XlsBuilder.NumValue(10), exp),
            array,
            XlsBuilder.Formula(1, 2, 15, XlsBuilder.NumValue(20), Tokens(b => b.U8(0x01).U16(0).U16(2))));
        using Workbook wb = Convert(file, out _, out _);
        Worksheet ws = wb.Sheets[0];
        CellFormula f = ws["C1"].FormulaDetail!;
        Assert.Equal(CellFormulaKind.Array, f.Kind);
        Assert.Equal("C1:C2", f.Reference);
        Assert.Equal("A1*2", f.Text);
        Assert.Null(ws["C2"].FormulaDetail);
        Assert.Equal(20, ws["C2"].Number);
    }

    [Fact]
    public void ThreeDReferencesNamesAndFunctionsDecode()
    {
        // EXTERNSHEET entry 0 refers to sheet 1 of the internal SUPBOOK.
        byte[] supbook = XlsBuilder.Rec(0x01AE, new Bytes().U16(2).U16(0x0401));
        byte[] externSheet = XlsBuilder.Rec(0x0017, new Bytes().U16(1).U16(0).U16(1).U16(1));
        byte[] name = XlsBuilder.Rec(0x0018, new Bytes().U16(0).U8(0).U8(5).U16(7).U16(0).U16(0).U8(0).U8(0).U8(0).U8(0).Chars8("Total").Raw(Tokens(Int(1), Int(2), Op(0x03))));
        byte[] file = XlsBuilder.Workbook(
            [("First", [XlsBuilder.Formula(0, 0, 15, XlsBuilder.NumValue(0), Tokens(
                b => b.U8(0x3A).U16(0).U16(2).U16(0xC000 | 1),
                b => b.U8(0x23).U16(1).U16(0),
                Op(0x03),
                b => b.U8(0x1E).U16(1),
                b => b.U8(0x1E).U16(2),
                b => b.U8(0x22).U8(2).U16(4),
                Op(0x03)))]),
            ("Second", [])],
            extraGlobals: [supbook, externSheet, name]);
        using Workbook wb = Convert(file, out DefectCollection defects, out _);
        Assert.True(wb.Sheets[0]["A1"].Formula is not null, string.Join("; ", defects.ToArray().Select(d => d.Code + " " + d.Message)));
        Assert.Equal("Second!B3+Total+SUM(1,2)", wb.Sheets[0]["A1"].Formula);
        Assert.Contains(wb.Names, n => n.Name == "Total" && n.Formula == "1+2");
        Assert.Empty(defects.ToArray());
    }

    [Fact]
    public void FilepassRaisesTheEncryptedException()
    {
        byte[] file = XlsBuilder.Workbook([("S", [])], extraGlobals: [XlsBuilder.Rec(0x002F, new Bytes().U16(1).U16(1).U16(1).Raw(new byte[48]))]);
        var ex = Assert.Throws<OfficeEncryptedException>(() => XlsConverter.ToWorkbook(XlsBuilder.Source(file)));
        Assert.Equal("encryption.password-required", ex.Defect.Code);
    }

    [Fact]
    public void DamagedInputRaisesTypedExceptionsOnly()
    {
        Assert.Throws<OfficeFormatException>(() => XlsConverter.ToWorkbook(XlsBuilder.Source([1, 2, 3, 4, 5, 6])));
        byte[] good = OneSheet(XlsBuilder.Number(0, 0, 15, 1));
        for (int cut = 4; cut < good.Length; cut += 7)
        {
            byte[] bad = good[..cut];
            try
            {
                using Workbook wb = XlsConverter.ToWorkbook(XlsBuilder.Source(bad));
            }
            catch (OfficeException)
            {
            }
        }

        // A record that claims more data than the stream has.
        byte[] lying = [.. good, 0x00, 0x02, 0xFF, 0xFF, 1, 2];
        using Workbook ok = Convert(lying, out DefectCollection d, out _);
        Assert.Contains(d.ToArray(), x => x.Code == "biff.truncated-record");
    }

    [Fact]
    public void RowsColumnsMergesAndViewsAreCarried()
    {
        byte[] pane = XlsBuilder.Rec(0x0041, new Bytes().U16(1).U16(2).U16(2).U16(1).U8(0));
        byte[] file = OneSheet(
            XlsBuilder.ColInfo(1, 2, 20 * 256, 15, 0x0003),
            XlsBuilder.ColInfo(4, 4, 8 * 256, 15, 0x0001),
            XlsBuilder.Row(0, 300, 0x0040 | 0x0080 | 0x0001),
            XlsBuilder.Row(3, 200, 0x0020),
            XlsBuilder.Number(0, 0, 15, 1),
            XlsBuilder.Merge((0, 1, 0, 2)),
            pane,
            XlsBuilder.Window2(0x06B6 | 0x08 & ~0x02));
        using Workbook wb = Convert(file, out _, out _);
        Worksheet ws = wb.Sheets[0];
        Assert.Equal("A1:C2", Assert.Single(ws.MergedCells).ToString());
        ColumnProperties c = ws.Columns.First(x => x.Min == 2);
        Assert.Equal(3, c.Max);
        Assert.Equal(20, c.Width, 3);
        Assert.True(c.Flags.HasFlag(ColumnOptions.Hidden));
        Assert.True(ws.GetRow(1).Height > 14.9 && ws.GetRow(1).Height < 15.1);
        Assert.True(ws.GetRow(4).Hidden);
        using var saved = new MemoryStream();
        wb.SaveAs(saved, OfficeFormat.Xlsx);
        Assert.True(saved.Length > 1000);
    }

    [Fact]
    public void HyperlinksNotesValidationsAndConditionalFormatsComeThrough()
    {
        string url = "https://example.com/x?a=1";
        byte[] urlBytes = Encoding.Unicode.GetBytes(url + "\0");
        byte[] hlink = XlsBuilder.Rec(0x01B8, new Bytes().U16(0).U16(0).U16(0).U16(0)
            .Raw(Convert16("79EAC9D0-BAF9-11CE-8C82-00AA004BA90B")).U32(2).U32(0x03)
            .Raw(Convert16("79EAC9E0-BAF9-11CE-8C82-00AA004BA90B")).U32((uint)urlBytes.Length).Raw(urlBytes));
        byte[] obj = XlsBuilder.Rec(0x005D, new Bytes().U16(0x15).U16(18).U16(0x19).U16(1).U16(0).Raw(new byte[12]).U16(0));
        byte[] note = XlsBuilder.Rec(0x001C, new Bytes().U16(1).U16(1).U16(0).U16(1).Long8("Ann"));
        byte[] txo = XlsBuilder.Rec(0x01B6, new Bytes().U16(0x12).U16(0).U16(0).U32(0).U16(5).U16(0).U16(0).U32(0));
        byte[] txoText = XlsBuilder.Rec(0x003C, new Bytes().U8(0).Raw(Encoding.Latin1.GetBytes("hello")));
        byte[] txoRuns = XlsBuilder.Rec(0x003C, new Bytes().U16(0).U16(0).U32(0));
        byte[] dv = XlsBuilder.Rec(0x01BE, new Bytes().U32(0x00040000 | 0x3 | 0x80 | 0x100 | 0x80000)
            .Long8(string.Empty).Long8(string.Empty).Long8("pick").Long8("bad")
            .U16(8).U16(0).Raw(Tokens(b => b.U8(0x17).U8(5).U8(0).U8('a').U8(0).U8('b').U8(0).U8('c')))
            .U16(0).U16(0).U16(1).U16(0).U16(4).U16(2).U16(2));
        byte[] condFmt = XlsBuilder.Rec(0x01B0, new Bytes().U16(1).U16(1).U16(0).U16(3).U16(0).U16(0).U16(1).U16(0).U16(3).U16(0).U16(0));
        var cfData = new Bytes().U8(1).U8(5).U16(3).U16(0).U32(0x20000000).U16(0).U32((1u << 10) | (5u << 23)).Raw(Tokens(Int(10)));
        byte[] cf = XlsBuilder.Rec(0x01B1, cfData);
        byte[] file = OneSheet(XlsBuilder.Number(0, 0, 15, 1), hlink, obj, note, txo, txoText, txoRuns, dv, condFmt, cf);
        using Workbook wb = Convert(file, out DefectCollection defects, out _);
        Worksheet ws = wb.Sheets[0];
        var rels = wb.Package.GetRelationships(ws.PartName!);
        Assert.Contains(rels, r => r.Target == url && r.TargetMode == RecursiveAI.OneDocument.Packaging.RelationshipTargetMode.External);
        Assert.Contains(rels, r => r.Type.EndsWith("/comments", StringComparison.Ordinal));
        string comments = Encoding.UTF8.GetString(wb.Package.FindPart("/xl/comments1.xml")!.GetBytes().ToArray());
        Assert.Contains("hello", comments, StringComparison.Ordinal);
        Assert.Contains("Ann", comments, StringComparison.Ordinal);
        Assert.Contains("B2", comments, StringComparison.Ordinal);
        DataValidation v = Assert.Single(ws.Validations);
        Assert.Equal("\"a,b,c\"", v.Formula1);
        Assert.Single(ws.ConditionalFormats);
        Assert.DoesNotContain(defects.ToArray(), x => x.Severity == DefectSeverity.Fatal);
    }

    private static byte[] Convert16(string guid) => Guid.Parse(guid).ToByteArray();

    [Fact]
    public void CodePageDecidesEightBitText()
    {
        // BIFF5: 8-bit text in the workbook's code page (1251 here). 0xD0 0xEF is "Пр" in Windows-1251.
        byte[] book = Biff5Book(1251, [Biff5Label(0, 0, new byte[] { 0xD0, 0xEF })]);
        using Workbook wb = Convert(XlsBuilder.InCompoundFile(book, "Book"), out _, out _);
        Assert.Equal("Рп", Text(wb.Sheets[0], "A1"));
    }

    [Fact]
    public void UnavailableCodePageIsReportedAndTextStillReads()
    {
        byte[] book = Biff5Book(99999, [Biff5Label(0, 0, "abc"u8.ToArray())]);
        using Workbook wb = Convert(book, out DefectCollection defects, out _);
        Assert.Equal("abc", Text(wb.Sheets[0], "A1"));
        Assert.Contains(defects.ToArray(), d => d.Code == "biff.codepage-unavailable" && d.Kind == DefectKind.Encoding);
    }

    private static byte[] Biff5Label(int row, int col, byte[] text) =>
        XlsBuilder.Rec(0x0204, new Bytes().U16(row).U16(col).U16(15).U16(text.Length).Raw(text));

    private static byte[] Biff5Book(int codePage, byte[][] sheetRecords)
    {
        var pre = new List<byte>();
        pre.AddRange(XlsBuilder.Rec(0x0809, new Bytes().U16(0x0500).U16(0x05).U16(0x0DBB).U16(0x07CC)));
        pre.AddRange(XlsBuilder.Rec(0x0042, new Bytes().U16(codePage)));
        for (int i = 0; i < 5; i++)
        {
            pre.AddRange(XlsBuilder.Rec(0x0031, new Bytes().U16(200).U16(0).U16(0x7FFF).U16(400).U16(0).U8(0).U8(0).U8(0).U8(0).U8(5).Raw("Arial"u8.ToArray())));
        }

        for (int i = 0; i < 16; i++)
        {
            pre.AddRange(XlsBuilder.Rec(0x00E0, new Bytes().U16(0).U16(0).U16(i < 15 ? 0xFFF5 : 1).U8(0x20).U8(0).U32(0x20C0).U32(0)));
        }

        var bound = XlsBuilder.Rec(0x0085, new Bytes().U32(0).U8(0).U8(0).U8(1).Raw("S"u8.ToArray()));
        var eof = XlsBuilder.Eof();
        long pos = pre.Count + bound.Length + eof.Length;
        bound = XlsBuilder.Rec(0x0085, new Bytes().U32((uint)pos).U8(0).U8(0).U8(1).Raw("S"u8.ToArray()));
        var all = new List<byte>(pre);
        all.AddRange(bound);
        all.AddRange(eof);
        all.AddRange(XlsBuilder.Rec(0x0809, new Bytes().U16(0x0500).U16(0x10).U16(0).U16(0)));
        foreach (byte[] r in sheetRecords)
        {
            all.AddRange(r);
        }

        all.AddRange(XlsBuilder.Eof());
        return [.. all];
    }

    [Fact]
    public void Biff5FormulasUseTheNarrowReferenceLayout()
    {
        // BIFF5 PtgRef: a row word whose high bits are the relative flags, then an 8-bit column.
        byte[] rgce = Tokens(b => b.U8(0x24).U16(0xC000 | 2).U8(1), b => b.U8(0x25).U16(0x8000 | 0).U16(0x8000 | 9).U8(0).U8(3), Op(0x03));
        byte[] formula = XlsBuilder.Rec(0x0006, new Bytes().U16(0).U16(0).U16(15).F64(7).U16(0).U32(0).U16(rgce.Length).Raw(rgce));
        using Workbook wb = Convert(Biff5Book(1252, [formula]), out DefectCollection defects, out _);
        Assert.Equal("B3+$A1:$D10", wb.Sheets[0]["A1"].Formula);
        Assert.Equal(7, wb.Sheets[0]["A1"].Number);
        Assert.Empty(defects.ToArray());
    }

    [Fact]
    public void Biff2StreamReadsValuesAndFormats()
    {
        var s = new List<byte>();
        s.AddRange(XlsBuilder.Rec(0x0009, new Bytes().U16(0).U16(0x10)));
        s.AddRange(XlsBuilder.Rec(0x0031, new Bytes().U16(200).U16(1).U8(5).Raw("Arial"u8.ToArray())));
        s.AddRange(XlsBuilder.Rec(0x001E, new Bytes().U8(7).Raw("General"u8.ToArray())));
        s.AddRange(XlsBuilder.Rec(0x001E, new Bytes().U8(4).Raw("0.00"u8.ToArray())));
        s.AddRange(XlsBuilder.Rec(0x0003, new Bytes().U16(0).U16(0).U8(0).U8(0x01).U8(0).F64(2.5)));
        s.AddRange(XlsBuilder.Rec(0x0002, new Bytes().U16(0).U16(1).U8(0).U8(0).U8(0).U16(42)));
        s.AddRange(XlsBuilder.Rec(0x0004, new Bytes().U16(1).U16(0).U8(0).U8(0).U8(0).U8(3).Raw("abc"u8.ToArray())));
        s.AddRange(XlsBuilder.Rec(0x0005, new Bytes().U16(1).U16(1).U8(0).U8(0).U8(0).U8(1).U8(0)));
        s.AddRange(XlsBuilder.Rec(0x0006, new Bytes().U16(2).U16(0).U8(0).U8(0).U8(0).F64(3).U8(0).U8(8).Raw(Tokens(b => b.U8(0x24).U16(0xC000).U8(0), Int(1), Op(0x03)))));
        s.AddRange(XlsBuilder.Eof());
        using Workbook wb = Convert([.. s], out DefectCollection defects, out _);
        Worksheet ws = wb.Sheets[0];
        Assert.Equal(2.5, ws["A1"].Number);
        Assert.Equal(42, ws["B1"].Number);
        Assert.Equal("abc", Text(ws, "A2"));
        Assert.True(ws["B2"].Value.Boolean);
        Assert.Equal(3, ws["A3"].Number);
        Assert.NotNull(ws["A3"].Formula);
        Assert.Empty(defects.ToArray());
    }

    [Fact]
    public void CompoundFileWithAStreamNameLackingItsNullIsStillFoundAndRead()
    {
        // Some writers (Excel on the Mac among them) end a directory name with something other than a null; the strict container reader refuses them.
        byte[] cfb = XlsBuilder.InCompoundFile(OneSheet(XlsBuilder.Number(0, 0, 15, 7)));
        byte[] name = Encoding.Unicode.GetBytes("Workbook");
        int at = cfb.AsSpan().IndexOf(name);
        Assert.True(at >= 0);
        Assert.Equal(18, cfb[at + 64]);
        cfb[at + 16] = 0x20;
        Assert.Equal(OfficeFormat.Xls, RecursiveAI.OneDocument.Formats.FormatSniffer.Detect(cfb));
        using Workbook wb = Convert(cfb, out _, out _);
        Assert.Equal(7, wb.Sheets[0]["A1"].Number);
    }

    [Fact]
    public void OfficeOpensAnXlsAndConvertsItToXlsx()
    {
        SpreadsheetFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        byte[] bytes = XlsBuilder.InCompoundFile(XlsBuilder.Workbook([("Sheet1", [XlsBuilder.Number(0, 0, 15, 12.5), XlsBuilder.LabelSst(1, 0, 15, 0), XlsBuilder.Formula(0, 1, 15, XlsBuilder.NumValue(25), Tokens(Ref(0, 0), Int(2), Op(0x05)))])], sst: ["findme"]));
        string dir = Path.Combine(Path.GetTempPath(), "xlstests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string xls = Path.Combine(dir, "in.xls");
            string xlsx = Path.Combine(dir, "out.xlsx");
            File.WriteAllBytes(xls, bytes);
            Office.Convert(xls, xlsx);
            using Workbook back = Workbook.Open(xlsx);
            Assert.Equal(12.5, back.Sheets[0]["A1"].Number);
            Assert.Equal("A1*2", back.Sheets[0]["B1"].Formula);
            Assert.Equal(25, back.Sheets[0]["B1"].Number);
            Assert.Contains("findme", Office.ExtractText(xls), StringComparison.Ordinal);
            using Workbook direct = Workbook.Open(xls);
            Assert.NotNull(direct.ImportLoss);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
