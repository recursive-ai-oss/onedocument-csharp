using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets.Binding;
using RecursiveAI.OneDocument.Spreadsheets.Biff;
using RecursiveAI.OneDocument.Spreadsheets.Streaming;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

[SpreadsheetRow(Sheet = "Cursor")]
public partial record CursorRow
{
    public string Name { get; set; } = string.Empty;

    public double Score { get; set; }

    public bool Flag { get; set; }

    public DateTime When { get; set; }
}

/// <summary>The forward cursors over legacy binary workbooks (<c>.xls</c>) and binary workbooks (<c>.xlsb</c>) against the workbook model.</summary>
public class BinaryCursorTests
{
    static BinaryCursorTests() => SpreadsheetFormats.Register(Formats.FormatRegistry.Default);

    private static string TestData
    {
        get
        {
            string d = AppContext.BaseDirectory;
            while (d.Length > 0 && !Directory.Exists(Path.Combine(d, "testdata")))
            {
                d = Path.GetDirectoryName(d) ?? string.Empty;
            }

            return d.Length > 0 ? Path.Combine(d, "testdata") : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "../../testdata"));
        }
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    public static IEnumerable<object[]> BinaryFiles()
    {
        string root = TestData;
        foreach (string f in Directory.GetFiles(Path.Combine(root, "xls"), "*.xls", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            yield return [Path.GetRelativePath(root, f)];
        }

        foreach (string f in Directory.GetFiles(Path.Combine(root, "xlsb"), "*.xlsb").Order(StringComparer.Ordinal))
        {
            yield return [Path.GetRelativePath(root, f)];
        }
    }

    private readonly record struct Seen(CellValueKind Kind, double Number, string Text, int Style, bool IsDate);

    private static Dictionary<(int Row, int Col), Seen> FromModel(Worksheet ws)
    {
        var map = new Dictionary<(int, int), Seen>();
        foreach (CellRowView r in ws.Store.EnumerateRows())
        {
            for (int i = 0; i < r.Count; i++)
            {
                CellValue v = r.ValueAt(i);
                int style = r.StyleAt(i);
                if (v.Kind == CellValueKind.Empty && style == 0)
                {
                    continue;
                }

                int col = r.ColumnAt(i);
                string text = v.Kind == CellValueKind.Text ? ws.GetText(v) : v.Kind == CellValueKind.Error ? CellErrors.ToText(v.Error) : string.Empty;
                map[(r.Row, col)] = new Seen(v.Kind, v.Kind is CellValueKind.Number or CellValueKind.Boolean ? v.Number : 0, text, style, ws[r.Row, col].IsDate);
            }
        }

        return map;
    }

    private static Dictionary<(int Row, int Col), Seen> FromCursor(ICellCursorView c, bool styles)
    {
        var map = new Dictionary<(int, int), Seen>();
        while (c.ReadRow())
        {
            while (c.ReadCell())
            {
                if (c.Kind == CellValueKind.Empty && (!styles || c.Style == 0))
                {
                    continue;
                }

                string text = c.Kind is CellValueKind.Text or CellValueKind.Error ? c.GetText() : string.Empty;
                map[(c.RowNumber, c.Column)] = new Seen(c.Kind, c.Kind is CellValueKind.Number or CellValueKind.Boolean ? c.Number : 0, text, styles ? c.Style : 0, c.IsDate);
            }
        }

        return map;
    }

    // The three cursors have the same members; this adapter lets one loop read any of them.
    private interface ICellCursorView : IDisposable
    {
        bool ReadRow();

        bool ReadCell();

        int RowNumber { get; }

        int Column { get; }

        int Style { get; }

        CellValueKind Kind { get; }

        double Number { get; }

        bool IsDate { get; }

        string GetText();
    }

    private sealed class XlsView(XlsSheetCursor c) : ICellCursorView
    {
        public bool ReadRow() => c.ReadRow();

        public bool ReadCell() => c.ReadCell();

        public int RowNumber => c.RowNumber;

        public int Column => c.Column;

        public int Style => c.Style;

        public CellValueKind Kind => c.Kind;

        public double Number => c.Number;

        public bool IsDate => c.IsDate;

        public string GetText() => c.GetText();

        public void Dispose() => c.Dispose();
    }

    private sealed class XlsbView(XlsbSheetCursor c) : ICellCursorView
    {
        public bool ReadRow() => c.ReadRow();

        public bool ReadCell() => c.ReadCell();

        public int RowNumber => c.RowNumber;

        public int Column => c.Column;

        public int Style => c.Style;

        public CellValueKind Kind => c.Kind;

        public double Number => c.Number;

        public bool IsDate => c.IsDate;

        public string GetText() => c.Kind == CellValueKind.Error ? CellErrors.ToText(c.Error) : c.GetText();

        public void Dispose() => c.Dispose();
    }

    [Theory]
    [MemberData(nameof(BinaryFiles))]
    public void TheCursorGivesTheCellsOfTheModel(string relative)
    {
        string path = Path.Combine(TestData, relative);
        var context = new OpenContext(password: relative.Contains("secret", StringComparison.Ordinal) ? "password" : null);
        using Workbook wb = Workbook.Open(path, context);
        bool xlsb = path.EndsWith(".xlsb", StringComparison.Ordinal);
        int compared = 0;
        if (xlsb)
        {
            using XlsbStreamReader r = XlsbStreamReader.Open(path, new OpenContext());
            compared = Compare(wb, r.Sheets, i => new XlsbView(r.OpenSheet(i)), styles: false);
        }
        else
        {
            using XlsStreamReader r = XlsStreamReader.Open(path, context);
            compared = Compare(wb, r.Sheets, i => new XlsView(r.OpenSheet(i)), styles: true);
        }

        Assert.True(compared > 0 || wb.Sheets.All(s => s.Store.CellCount == 0), relative + " had cells but none were compared");
    }

    private static int Compare(Workbook wb, IReadOnlyList<StreamSheetInfo> sheets, Func<int, ICellCursorView> open, bool styles)
    {
        var worksheets = wb.Sheets.Where(s => s.Kind == SheetKind.Worksheet).ToList();
        var listed = sheets.Where(s => s.Kind == SheetKind.Worksheet).ToList();
        Assert.Equal(worksheets.Select(s => s.Name), listed.Select(s => s.Name));
        int total = 0;
        for (int k = 0; k < worksheets.Count; k++)
        {
            Dictionary<(int Row, int Col), Seen> expected = FromModel(worksheets[k]);
            Dictionary<(int Row, int Col), Seen> actual;
            using (ICellCursorView c = open(listed[k].Index))
            {
                actual = FromCursor(c, styles);
            }

            foreach (((int Row, int Col) at, Seen e) in expected)
            {
                if (!styles && e.Kind == CellValueKind.Empty)
                {
                    continue;
                }

                string where = worksheets[k].Name + "!" + new CellAddress(at.Row, at.Col);
                Assert.True(actual.TryGetValue(at, out Seen a), where + " is missing from the cursor; the model has " + e + "; the cursor has in that row: " + string.Join("; ", actual.Where(x => x.Key.Row == at.Row).Select(x => x.Key.Col + "=" + x.Value)));
                Assert.Equal(e.Kind, a.Kind);
                Assert.Equal(e.Number, a.Number);
                Assert.Equal(e.Text, a.Text);
                Assert.Equal(e.IsDate, a.IsDate);
                if (styles)
                {
                    Assert.Equal(e.Style, a.Style);
                }
            }

            foreach (((int Row, int Col) at, Seen a) in actual)
            {
                Assert.True(expected.ContainsKey(at) || a.Kind == CellValueKind.Empty, worksheets[k].Name + "!" + new CellAddress(at.Row, at.Col) + " is only in the cursor");
            }

            total += expected.Count;
        }

        return total;
    }

    [Fact]
    public void FormulaCellsReportTheirCachedResultAndKind()
    {
        string path = Path.Combine(TestData, "xls", "formulas.xls");
        using XlsStreamReader r = XlsStreamReader.Open(path);
        int formulas = 0;
        for (int i = 0; i < r.Sheets.Count; i++)
        {
            if (r.Sheets[i].Kind != SheetKind.Worksheet)
            {
                continue;
            }

            using XlsSheetCursor c = r.OpenSheet(i);
            while (c.ReadRow())
            {
                while (c.ReadCell())
                {
                    formulas += c.HasFormula ? 1 : 0;
                }
            }
        }

        Assert.True(formulas > 10);
    }

    // The Workbook stream of a compound file as a bare BIFF stream, which the reader accepts as well.
    private static byte[] BareStream(string path)
    {
        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(path);
        return (cfb.FindPart("/Workbook") ?? cfb.FindPart("/Book"))!.GetBytes().ToArray();
    }

    [Fact]
    public void ABareBiffStreamReadsLikeTheCompoundFile()
    {
        byte[] bare = BareStream(Path.Combine(TestData, "xls", "basic.xls"));
        using XlsStreamReader a = XlsStreamReader.Open(Path.Combine(TestData, "xls", "basic.xls"));
        using XlsStreamReader b = XlsStreamReader.Open(new MemoryStream(bare));
        Assert.Equal(a.Sheets.Select(s => s.Name), b.Sheets.Select(s => s.Name));
        using XlsSheetCursor ca = a.OpenSheet(0);
        using XlsSheetCursor cb = b.OpenSheet(0);
        Assert.Equal(FromCursor(new XlsView(ca), true), FromCursor(new XlsView(cb), true));
    }

    [Fact]
    public void ATruncatedStreamEndsWithALocatedDefect()
    {
        byte[] bare = BareStream(Path.Combine(TestData, "xls", "basic.xls"));
        int cut = bare.Length - 40;
        var context = new OpenContext();
        using XlsStreamReader r = XlsStreamReader.Open(new MemoryStream(bare, 0, cut), context);
        using XlsSheetCursor c = r.OpenSheet(0);
        while (c.ReadRow())
        {
            while (c.ReadCell())
            {
            }
        }

        // Cutting through the last records may land on a record boundary; either way nothing throws and nothing is read past the cut.
        var defects = (DefectCollection)context.Defects;
        Assert.All(defects.Where(d => d.Code == "biff.truncated-record"), d => Assert.True(d.Location.Offset is > 0 and <= 1 << 30));
    }

    [Fact]
    public void ARecordCutShortIsReportedWithItsOffset()
    {
        // BOF, then a NUMBER record that claims 14 bytes but has 3.
        byte[] stream = [0x09, 0x08, 0x08, 0x00, 0x00, 0x06, 0x05, 0x00, 0xC1, 0x0D, 0xCC, 0x07, 0x03, 0x02, 0x0E, 0x00, 0x00, 0x00, 0x00];
        var context = new OpenContext();
        using XlsStreamReader r = XlsStreamReader.Open(new MemoryStream(stream), context);
        var defects = (DefectCollection)context.Defects;
        Defect d = Assert.Single(defects, x => x.Code == "biff.truncated-record");
        Assert.Equal(12, d.Location.Offset);
    }

    [Fact]
    public void ADataStreamThatIsNotBiffIsATypedError()
    {
        OfficeFormatException e = Assert.Throws<OfficeFormatException>(() => XlsStreamReader.Open(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])));
        Assert.Equal("biff.no-bof", e.Defect.Code);
    }

    [Fact]
    public void TheRecordLimitApplies()
    {
        // The same file under a tight limit fails with the limit exception.
        var tight = new OpenContext(new OfficeLimits { MaxRecordCount = 30 });
        OfficeLimitException e = Assert.Throws<OfficeLimitException>(() =>
        {
            using XlsStreamReader t = XlsStreamReader.Open(Path.Combine(TestData, "xls", "formulas.xls"), tight);
            using XlsSheetCursor tc = t.OpenSheet(0);
            while (tc.ReadRow())
            {
                while (tc.ReadCell())
                {
                }
            }
        });
        Assert.Equal(nameof(OfficeLimits.MaxRecordCount), e.LimitName);
    }

    private static byte[] Generate(int rows, OfficeFormat format)
    {
        using Workbook wb = Workbook.Create();
        Worksheet ws = wb.Sheets[0];
        for (int i = 1; i <= rows; i++)
        {
            ws[i, 1].SetNumber(i * 1.25);
            ws[i, 2].SetString(i % 7 == 0 ? "seven" : "x" + (i % 50));
            ws[i, 3].SetBoolean(i % 2 == 0);
            ws[i, 4].SetNumber(i);
            ws[i, 5].SetError(CellError.NotAvailable);
        }

        using var ms = new MemoryStream();
        if (format == OfficeFormat.Xls)
        {
            wb.SaveAs(ms, OfficeFormat.Xls);
        }
        else
        {
            wb.SaveAs(ms, format, new LossReport());
        }

        return ms.ToArray();
    }

    [Fact]
    public void TheXlsCursorAllocatesNothingPerCell()
    {
        byte[] file = Generate(4000, OfficeFormat.Xls);
        using XlsStreamReader r = XlsStreamReader.Open(new MemoryStream(file));
        _ = r.SharedStringCount;
        _ = r.IsDateStyle(0);
        long full = AllocationProbe.Min(() =>
        {
            long t0 = GC.GetAllocatedBytesForCurrentThread();
            _ = Walk(r, 4000);
            return GC.GetAllocatedBytesForCurrentThread() - t0;
        }, 8);
        long small = AllocationProbe.Min(() =>
        {
            long t0 = GC.GetAllocatedBytesForCurrentThread();
            _ = Walk(r, 1000);
            return GC.GetAllocatedBytesForCurrentThread() - t0;
        }, 8);
        Assert.True(full - small < 2_000, "allocated " + full + " for 4000 rows, " + small + " for 1000");
    }

    private static long Walk(XlsStreamReader r, int maxRows)
    {
        long sum = 0;
        Span<char> buf = stackalloc char[64];
        using XlsSheetCursor c = r.OpenSheet(0);
        int rows = 0;
        while (rows++ < maxRows && c.ReadRow())
        {
            while (c.ReadCell())
            {
                sum += c.Column + c.Style + (long)c.Kind + (long)c.Number;
                if (c.Kind == CellValueKind.Text && c.TryCopyText(buf, out int n))
                {
                    sum += n + c.TextUtf8.Length;
                }
            }
        }

        return sum;
    }

    [Fact]
    public void ExtractedTextIsTheTextOfTheModel()
    {
        foreach (string file in new[] { "xls/basic.xls", "xls/features.xls", "xlsb/values.xlsb", "xlsb/sheets.xlsb" })
        {
            string path = Path.Combine(TestData, file);
            string viaCursor = Office.ExtractText(path);
            using Workbook wb = Workbook.Open(path);
            var model = new List<string>();
            foreach (Worksheet ws in wb.Sheets.Where(s => s.Kind == SheetKind.Worksheet))
            {
                foreach (CellRowView row in ws.Store.EnumerateRows())
                {
                    for (int i = 0; i < row.Count; i++)
                    {
                        if (row.ValueAt(i).Kind == CellValueKind.Text)
                        {
                            model.Add(ws.GetText(row.ValueAt(i)));
                        }
                    }
                }
            }

            Assert.Equal(string.Concat(model.Select(t => t + Environment.NewLine)), viaCursor);
        }
    }

    public sealed record Line(int N, string Name);

    [Fact]
    public void ReadRowsReachesBinaryWorkbooks()
    {
        foreach (OfficeFormat format in new[] { OfficeFormat.Xls, OfficeFormat.Xlsb })
        {
            byte[] file = Generate(30, format);
            string path = Path.Combine(Path.GetTempPath(), "cursor-" + Guid.NewGuid().ToString("N") + (format == OfficeFormat.Xls ? ".xls" : ".xlsb"));
            File.WriteAllBytes(path, file);
            try
            {
                object?[][] rows = Spreadsheet.ReadRows(path).ToArray();
                Assert.Equal(30, rows.Length);
                Assert.Equal(1.25, rows[0][0]);
                Assert.Equal("x1", rows[0][1]);
                Assert.Equal(false, rows[0][2]);
                Assert.Equal(CellError.NotAvailable, rows[0][4]);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void EncryptedWorkbooksAreDecryptedForTheCursorToo()
    {
        using Workbook wb = Workbook.Create();
        wb.Sheets[0]["A1"].SetString("hidden");
        wb.Sheets[0]["B1"].SetNumber(42);
        string path = Path.Combine(Path.GetTempPath(), "cursor-" + Guid.NewGuid().ToString("N") + ".xls");
        try
        {
            wb.SaveAs(path, OfficeFormat.Xls, "pw");
            Assert.ThrowsAny<OfficeEncryptedException>(() => XlsStreamReader.Open(path));
            using XlsStreamReader r = XlsStreamReader.Open(path, new OpenContext(password: "pw"));
            using XlsSheetCursor c = r.OpenSheet(0);
            Assert.True(c.ReadRow());
            Assert.True(c.ReadCell());
            Assert.Equal("hidden", c.GetText());
            Assert.True(c.ReadCell());
            Assert.Equal(42, c.Number);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OutOfOrderRowsAndCellsAreDeliveredAsStoredAndReported()
    {
        var b = new List<byte>();
        void Rec(int type, params byte[] data)
        {
            b.AddRange([(byte)type, (byte)(type >> 8), (byte)data.Length, (byte)(data.Length >> 8)]);
            b.AddRange(data);
        }

        void Integer(int row, int col, int value) => Rec(0x0002, (byte)row, 0, (byte)col, 0, 0, 0, 0, (byte)value, 0);
        Rec(0x0009, 0, 0, 0x10, 0);
        Integer(3, 0, 40);
        Integer(0, 2, 12);
        Integer(0, 0, 10);
        Rec(0x000A);
        var context = new OpenContext();
        using XlsStreamReader r = XlsStreamReader.Open(new MemoryStream([.. b]), context);
        using XlsSheetCursor c = r.OpenSheet(0);
        var seen = new List<string>();
        while (c.ReadRow())
        {
            while (c.ReadCell())
            {
                seen.Add(c.RowNumber + ":" + c.Column + "=" + c.Number);
            }
        }

        Assert.Equal(["4:1=40", "1:3=12", "1:1=10"], seen);
        var defects = (DefectCollection)context.Defects;
        Assert.Contains(defects, d => d.Code == "xls.stream-row-order");
        Defect cell = Assert.Single(defects, d => d.Code == "xls.stream-cell-order");
        Assert.Equal("A1", cell.Location.Address);
    }

    [Fact]
    public void TypedRowsReadFromBinaryWorkbooks()
    {
        CursorRow[] rows = Enumerable.Range(1, 40).Select(i => new CursorRow { Name = "n" + i, Score = i * 1.5, Flag = i % 3 == 0, When = new DateTime(2021, 3, 1).AddDays(i) }).ToArray();
        using var xlsx = new MemoryStream();
        Spreadsheet.Write(xlsx, rows);
        xlsx.Position = 0;
        using Workbook wb = Workbook.Open(xlsx);
        foreach (OfficeFormat format in new[] { OfficeFormat.Xls, OfficeFormat.Xlsb })
        {
            using var bin = new MemoryStream();
            if (format == OfficeFormat.Xls)
            {
                wb.SaveAs(bin, OfficeFormat.Xls);
            }
            else
            {
                wb.SaveAs(bin, format, new LossReport());
            }

            bin.Position = 0;
            CursorRow[] back = Spreadsheet.Read<CursorRow>(bin).ToArray();
            Assert.Equal(rows, back);
        }
    }

    [Fact]
    public void ABiff2StreamReadsThroughTheCursor()
    {
        // BIFF2: BOF, an INTEGER, a NUMBER and a LABEL, each with the three attribute bytes, then EOF.
        var b = new List<byte>();
        void Rec(int type, params byte[] data)
        {
            b.Add((byte)type);
            b.Add((byte)(type >> 8));
            b.Add((byte)data.Length);
            b.Add((byte)(data.Length >> 8));
            b.AddRange(data);
        }

        Rec(0x0009, 0, 0, 0x10, 0);
        Rec(0x0002, 0, 0, 0, 0, 0, 0, 0, 7, 0);
        Rec(0x0003, [1, 0, 0, 0, 0, 0, 0, .. BitConverter.GetBytes(2.5)]);
        Rec(0x0004, [1, 0, 1, 0, 0, 0, 0, 2, (byte)'h', (byte)'i']);
        Rec(0x000A);
        byte[] bytes = [.. b];
        using Workbook wb = RecursiveAI.OneDocument.Spreadsheets.Biff.XlsConverter.ToWorkbook(ByteSource.FromMemory(bytes));
        using XlsStreamReader r = XlsStreamReader.Open(new MemoryStream(bytes));
        Assert.Single(r.Sheets);
        Compare(wb, r.Sheets, i => new XlsView(r.OpenSheet(i)), styles: true);
        using XlsSheetCursor c = r.OpenSheet(0);
        Dictionary<(int Row, int Col), Seen> seen = FromCursor(new XlsView(c), true);
        Assert.Equal(7, seen[(1, 1)].Number);
        Assert.Equal(2.5, seen[(2, 1)].Number);
        Assert.Equal("hi", seen[(2, 2)].Text);
    }
}
