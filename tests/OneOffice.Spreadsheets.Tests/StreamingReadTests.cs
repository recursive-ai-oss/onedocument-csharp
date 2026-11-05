using System.Text;
using RecursiveAI.OneOffice.Spreadsheets.Streaming;

namespace RecursiveAI.OneOffice.Spreadsheets.Tests;

public class StreamingReadTests
{
    private static XlsxStreamReader Open(byte[]? bytes = null, OpenContext? ctx = null) => XlsxStreamReader.Open(new MemoryStream(bytes ?? TestBook.Bytes()), ctx);

    [Fact]
    public void ListsSheetsAndStreamsRowsAndCells()
    {
        using XlsxStreamReader r = Open();
        Assert.Equal(["Data", "Other"], r.Sheets.Select(s => s.Name));
        Assert.Equal(SheetState.Hidden, r.Sheets[1].State);
        using SheetCursor c = r.OpenSheet("Data");
        Assert.Equal("A1:D4", c.DeclaredDimension?.ToString());
        Assert.Equal(2, c.Columns.Count);
        Assert.True(c.Columns[1].Flags.HasFlag(ColumnOptions.Hidden));

        Assert.True(c.ReadRow());
        Assert.Equal(1, c.RowNumber);
        Assert.Equal(20, c.RowHeight);
        Assert.True(c.HasCustomRowHeight);
        Assert.True(c.ReadCell());
        Assert.Equal("A1", c.Address.ToString());
        Assert.Equal(CellValueKind.Text, c.Kind);
        Assert.Equal(0, c.SharedStringIndex);
        Assert.Equal("First", c.GetText());
        Assert.True(c.ReadCell());
        Assert.Equal(1.5, c.Number);
        Assert.Equal(1, c.Style);
        Assert.True(c.ReadCell());
        Assert.True(c.Boolean);
        Assert.True(c.ReadCell());
        Assert.Equal(CellError.DivideByZero, c.Error);
        Assert.Equal("#DIV/0!", c.GetText());
        Assert.False(c.ReadCell());

        Assert.True(c.ReadRow());
        Assert.Equal(2, c.RowNumber);
        Assert.True(c.IsRowHidden);
        Assert.Equal(1, c.RowOutlineLevel);
        Assert.True(c.ReadCell());
        Assert.True(c.HasFormula);
        Assert.Equal("B1*2", c.GetFormula());
        Assert.Equal(3, c.Number);
        Assert.True(c.ReadCell());
        Assert.Equal("helloxx & more", c.GetText());
        Assert.Equal("A1&\"x\"", c.GetFormula());
        Assert.True(c.ReadCell());
        Assert.Equal(" inline ", c.GetText());
        Assert.False(c.HasFormula);
        Assert.True(c.ReadCell());
        Assert.Equal(CellFormulaKind.Array, c.FormulaKind);
        Assert.Equal("D2:D3", Encoding.UTF8.GetString(c.FormulaRangeUtf8));

        // Skipping the rest of a row, then a row whose number skips ahead.
        Assert.True(c.ReadRow());
        Assert.Equal(4, c.RowNumber);
        Assert.True(c.ReadCell());
        Assert.Equal(CellFormulaKind.Shared, c.FormulaKind);
        Assert.Equal(0, c.SharedFormulaIndex);
        Assert.True(c.ReadCell());
        Assert.Equal(string.Empty, c.GetFormula());
        Assert.Equal(5, c.Number);
        Assert.False(c.ReadCell());
        Assert.False(c.ReadRow());
    }

    [Fact]
    public void RichSharedStringsAreFlattenedWithoutPhonetics()
    {
        using XlsxStreamReader r = Open();
        Assert.Equal(2, r.SharedStringCount);
        Assert.Equal("Rich text", r.GetSharedString(1));
    }

    [Fact]
    public void ValuesAgreeWithTheWorkbookModel()
    {
        byte[] bytes = TestBook.Bytes();
        using Workbook wb = TestBook.Open(bytes);
        using XlsxStreamReader r = Open(bytes);
        foreach (StreamSheetInfo info in r.Sheets)
        {
            Worksheet ws = wb.Sheets[info.Index];
            using SheetCursor c = r.OpenSheet(info.Index);
            int cells = 0;
            while (c.ReadRow())
            {
                while (c.ReadCell())
                {
                    Cell cell = ws[c.RowNumber, c.Column];
                    Assert.Equal(cell.Value.Kind, c.Kind);
                    Assert.Equal(cell.Value.Number, c.Number);
                    Assert.Equal(cell.StyleIndex, c.Style);
                    Assert.Equal(cell.Formula is not null, c.HasFormula);
                    if (c.Kind == CellValueKind.Text)
                    {
                        Assert.Equal(cell.Text, c.GetText());
                    }

                    cells++;
                }
            }

            Assert.Equal(ws.Store.CellCount, cells);
        }
    }

    [Fact]
    public void TryCopyTextDecodesEscapesWithoutAllocating()
    {
        string sheet = "<?xml version=\"1.0\"?><worksheet xmlns=\"" + TestBook.Main + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>a_x000D_b</t></is></c></row></sheetData></worksheet>";
        using XlsxStreamReader r = Open(TestBook.Bytes(sheet));
        using SheetCursor c = r.OpenSheet(0);
        Assert.True(c.ReadRow());
        Assert.True(c.ReadCell());
        Span<char> buf = stackalloc char[16];
        Assert.True(c.TryCopyText(buf, out int n));
        Assert.Equal("a\rb", buf[..n].ToString());
        Assert.False(c.TryCopyText(buf[..1], out _));
    }

    [Fact]
    public void OutOfOrderRowsAndCellsAreReportedAndDeliveredAsStored()
    {
        string sheet = "<?xml version=\"1.0\"?><worksheet xmlns=\"" + TestBook.Main + "\"><sheetData>"
            + "<row r=\"3\"><c r=\"B3\"><v>1</v></c><c r=\"A3\"><v>2</v></c></row><row r=\"2\"><c r=\"A2\"><v>3</v></c></row></sheetData></worksheet>";
        var ctx = new OpenContext();
        using XlsxStreamReader r = Open(TestBook.Bytes(sheet), ctx);
        using SheetCursor c = r.OpenSheet(0);
        var seen = new List<string>();
        while (c.ReadRow())
        {
            while (c.ReadCell())
            {
                seen.Add(c.Address.ToString());
            }
        }

        Assert.Equal(["B3", "A3", "A2"], seen);
        string[] codes = [.. TestBook.Defects(ctx).Select(d => d.Code)];
        Assert.Contains("xlsx.stream-cell-order", codes);
        Assert.Contains("xlsx.stream-row-order", codes);
    }

    [Fact]
    public void BadCellAddressIsDroppedWithAnInPartLocation()
    {
        string sheet = "<?xml version=\"1.0\"?><worksheet xmlns=\"" + TestBook.Main + "\"><sheetData><row r=\"1\"><c r=\"ZZZZ1\"><v>1</v></c><c r=\"B1\"><v>2</v></c></row></sheetData></worksheet>";
        var ctx = new OpenContext();
        using XlsxStreamReader r = Open(TestBook.Bytes(sheet), ctx);
        using SheetCursor c = r.OpenSheet(0);
        Assert.True(c.ReadRow());
        Assert.True(c.ReadCell());
        Assert.Equal("B1", c.Address.ToString());
        Assert.False(c.ReadCell());
        Assert.Contains(TestBook.Defects(ctx), d => d.Code == "xlsx.cell-address-invalid" && d.Location.Container == "/xl/worksheets/sheet1.xml");
    }

    [Fact]
    public void WrongRootIsFatal()
    {
        using XlsxStreamReader r = Open(TestBook.Bytes("<?xml version=\"1.0\"?><nope xmlns=\"" + TestBook.Main + "\"/>"));
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => r.OpenSheet(0));
        Assert.Equal("xlsx.wrong-root", ex.Defect.Code);
    }

    [Fact]
    public void DatesUseTheStyleNumberFormat()
    {
        // style 1 is a date format, style 0 General
        string styles = "<?xml version=\"1.0\"?><styleSheet xmlns=\"" + TestBook.Main + "\"><numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/></numFmts>"
            + "<cellXfs count=\"3\"><xf numFmtId=\"0\"/><xf numFmtId=\"164\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>";
        string sheet = "<?xml version=\"1.0\"?><worksheet xmlns=\"" + TestBook.Main + "\"><sheetData><row r=\"1\"><c r=\"A1\" s=\"1\"><v>45000</v></c><c r=\"B1\"><v>45000</v></c><c r=\"C1\" s=\"2\"><v>1</v></c></row></sheetData></worksheet>";
        byte[] bytes = TestBook.Bytes(sheet);
        using Workbook wb = TestBook.Open(bytes);
        wb.Package.FindPart("/xl/styles.xml")!.SetBytes(Encoding.UTF8.GetBytes(styles));
        using var ms = new MemoryStream();
        wb.SaveAs(ms, wb.Format);
        using XlsxStreamReader r = Open(ms.ToArray());
        using SheetCursor c = r.OpenSheet(0);
        Assert.True(c.ReadRow());
        Assert.True(c.ReadCell());
        Assert.True(c.IsDate);
        Assert.True(c.TryGetDateTime(out DateTime d));
        Assert.Equal(new DateTime(2023, 3, 15), d);
        Assert.True(c.ReadCell());
        Assert.False(c.IsDate);
        Assert.True(c.ReadCell());
        Assert.True(c.IsDate);
    }

    [Fact]
    public void ReadingAllocatesNothingPerCell()
    {
        // Generate a sheet with numbers, shared strings, inline strings and formulas, then walk it twice.
        var ms = new MemoryStream();
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(ms))
        {
            SheetStreamWriter s = w.AddSheet("S");
            for (int i = 0; i < 4000; i++)
            {
                s.BeginRow();
                s.WriteNumber(i * 1.25);
                s.WriteText(i % 7 == 0 ? "seven" : "x" + (i % 50), 0);
                s.WriteBoolean(i % 2 == 0);
                s.WriteFormula("A1+1", i);
                s.WriteDate(new DateTime(2020, 1, 1).AddDays(i), 0);
            }
        }

        ms.Position = 0;
        using XlsxStreamReader r = XlsxStreamReader.Open(ms);
        _ = r.SharedStringCount; // load the table before measuring
        _ = r.IsDateStyle(0);
        long a = Walk(r, 4000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long b = Walk(r, 4000);
        long small = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        _ = Walk(r, 1000);
        long smaller = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(a, b);
        // Opening the part and its buffers costs the same for any size; reading 3,000 more rows (15,000 cells) must cost nothing.
        Assert.True(small - smaller < 2_000, "allocated " + small + " for 4000 rows, " + smaller + " for 1000");
    }

    private static long Walk(XlsxStreamReader r, int maxRows)
    {
        long sum = 0;
        Span<char> buf = stackalloc char[64];
        using SheetCursor c = r.OpenSheet(0);
        int rows = 0;
        while (rows++ < maxRows && c.ReadRow())
        {
            while (c.ReadCell())
            {
                sum += c.Column + c.Style + (long)c.Kind;
                sum += (long)c.Number;
                if (c.Kind == CellValueKind.Text && c.TryCopyText(buf, out int n))
                {
                    sum += n + c.TextUtf8.Length;
                }

                if (c.HasFormula)
                {
                    sum += c.FormulaUtf8.Length;
                }
            }
        }

        return sum;
    }
}
