using System.IO.Compression;
using System.Text;
using RecursiveAI.OneOffice.Spreadsheets.Streaming;

namespace RecursiveAI.OneOffice.Spreadsheets.Tests;

public class StreamingWriteTests
{
    private static byte[] Write(Action<XlsxStreamWriter> body, XlsxWriteOptions? options = null)
    {
        var ms = new MemoryStream();
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(ms, options))
        {
            body(w);
        }

        return ms.ToArray();
    }

    [Fact]
    public void ColumnSkippedBeforeTheFirstCellOfASheetIsKept()
    {
        byte[] file = Write(w =>
        {
            SheetStreamWriter s = w.AddSheet("S");
            s.SkipColumns(3);
            s.WriteNumber(7);
            s.Complete();
        });
        using var r = XlsxStreamReader.Open(new MemoryStream(file));
        using SheetCursor c = r.OpenSheet(0);
        Assert.True(c.ReadRow());
        Assert.True(c.ReadCell());
        Assert.Equal(4, c.Column);
    }

    [Fact]
    public void WrittenWorkbookReadsBackThroughTheModelAndTheCursor()
    {
        byte[] file = Write(w =>
        {
            int bold = w.AddFormat(new StreamCellFormat { Bold = true, FillColor = 0xFFFF00, Border = StreamBorder.Thin });
            int date = w.AddFormat(StreamCellFormat.Date);
            SheetStreamWriter s = w.AddSheet("Data", new SheetWriteOptions { FreezeRows = 1, Columns = [new ColumnProperties(1, 2, 20, 0, ColumnOptions.None)] });
            s.BeginRow();
            s.WriteText("Name", bold);
            s.WriteText("Value", bold);
            s.BeginRow(3, height: 30);
            s.WriteText("a & <b> _x0041_ \u0001 \r\n tail ");
            s.WriteNumber(0.1 + 0.2);
            s.WriteBoolean(true);
            s.WriteError(CellError.NotAvailable);
            s.WriteDate(new DateTime(2024, 2, 29, 12, 0, 0), date);
            s.SkipColumns(1);
            s.WriteFormulaString("A3&\"x\"", "yx");
            s.WriteFormula("SUM(B3:B3)");
            s.WriteText("\uD83D\uDE00 emoji \uD800 lone");
            s.Merge(CellRange.Parse("A5:B6"));
            w.AddSheet("Second").WriteNumber(7);
        });

        using Workbook wb = Workbook.Open(new MemoryStream(file));
        Worksheet ws = wb.Sheets["Data"];
        Assert.Equal("Name", ws["A1"].Text);
        Assert.Equal("a & <b> _x0041_ \u0001 \r\n tail ", ws["A3"].Text);
        Assert.Equal(0.1 + 0.2, ws["B3"].Number);
        Assert.True(ws["C3"].Value.Boolean);
        Assert.Equal(CellError.NotAvailable, ws["D3"].Value.Error);
        Assert.Equal(45351.5, ws["E3"].Number);
        Assert.Equal("A3&\"x\"", ws["G3"].Formula);
        Assert.Equal("yx", ws["G3"].Text);
        Assert.Equal("SUM(B3:B3)", ws["H3"].Formula);
        Assert.Equal("\uD83D\uDE00 emoji \uD800 lone", ws["I3"].Text);
        Assert.True(wb.NeedsRecalculation);
        Assert.Equal("A5:B6", ws.MergedCells.Single().ToString());
        Assert.True(ws.TryGetFreezePane(out int rows, out int cols));
        Assert.Equal((1, 0), (rows, cols));
        Assert.Equal(30, ws.GetRow(3).Height);
        Assert.Equal(7, wb.Sheets["Second"]["A1"].Number);
        Assert.True(wb.Package.FindPart("/xl/styles.xml") is not null);

        using XlsxStreamReader r = XlsxStreamReader.Open(new MemoryStream(file));
        using SheetCursor c = r.OpenSheet("Data");
        Assert.Equal(2, c.Columns.Count + 1);
        int count = 0;
        while (c.ReadRow())
        {
            while (c.ReadCell())
            {
                count++;
                if (c.Address.ToString() == "E3")
                {
                    Assert.True(c.IsDate);
                    Assert.True(c.TryGetDateTime(out DateTime d));
                    Assert.Equal(new DateTime(2024, 2, 29, 12, 0, 0), d);
                }
            }
        }

        Assert.Equal(10, count);
    }

    [Fact]
    public void LoneSurrogateIsEscapedAndSurvives()
    {
        byte[] file = Write(w => w.AddSheet("S", null).WriteText("x\uD800y"), new XlsxWriteOptions { StringStorage = StreamStringStorage.Inline });
        using XlsxStreamReader r = XlsxStreamReader.Open(new MemoryStream(file));
        using SheetCursor c = r.OpenSheet(0);
        Assert.True(c.ReadRow());
        Assert.True(c.ReadCell());
        Assert.Equal("x\uD800y", c.GetText());
    }

    [Fact]
    public void EqualInputGivesEqualBytes()
    {
        static byte[] One() => Write(w =>
        {
            SheetStreamWriter s = w.AddSheet("S");
            for (int i = 0; i < 100; i++)
            {
                s.BeginRow();
                s.WriteNumber(i);
                s.WriteText("t" + (i % 3));
            }
        });
        Assert.Equal(One(), One());
    }

    [Fact]
    public void InlineStorageWritesNoSharedStrings()
    {
        byte[] file = Write(w => w.AddSheet("S").WriteText("hello"), new XlsxWriteOptions { StringStorage = StreamStringStorage.Inline });
        using var zip = new ZipArchive(new MemoryStream(file));
        Assert.Null(zip.GetEntry("xl/sharedStrings.xml"));
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public void SharedTableOverflowFallsBackToInline()
    {
        byte[] file = Write(w =>
        {
            SheetStreamWriter s = w.AddSheet("S");
            for (int i = 0; i < 10; i++)
            {
                s.BeginRow();
                s.WriteText("s" + i);
            }
        }, new XlsxWriteOptions { MaxSharedStrings = 4 });
        using Workbook wb = Workbook.Open(new MemoryStream(file));
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal("s" + i, wb.Sheets[0][i + 1, 1].Text);
        }

        Assert.Equal(4, wb.SharedStrings.Count);
    }

    [Fact]
    public void ZipOutputWorksWithZip64AndWithAnUnseekableStream()
    {
        var sink = new NonSeekable();
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(sink, new XlsxWriteOptions { Zip64 = true }))
        {
            SheetStreamWriter s = w.AddSheet("S");
            s.WriteNumber(1);
        }

        using Workbook wb = Workbook.Open(new MemoryStream(sink.Bytes()));
        Assert.Equal(1, wb.Sheets[0]["A1"].Number);
        using var zip = new ZipArchive(new MemoryStream(sink.Bytes()));
        Assert.Equal(1, zip.GetEntry("xl/worksheets/sheet1.xml") is { } ? 1 : 0);
    }

    [Fact]
    public void MisuseIsRejected()
    {
        using var ms = new MemoryStream();
        using XlsxStreamWriter w = XlsxStreamWriter.Create(ms);
        SheetStreamWriter s = w.AddSheet("A");
        Assert.Throws<ArgumentException>(() => w.AddSheet("a"));
        Assert.Throws<ArgumentException>(() => w.AddSheet("bad:name"));
        Assert.Throws<ArgumentException>(() => w.AddSheet(new string('x', 32)));
        s.BeginRow(5);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.BeginRow(5));
        s.WriteNumber(1);
        s.WriteNumber(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.MoveToColumn(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.WriteNumber(1, 5));
        Assert.Throws<ArgumentException>(() => s.WriteFormula("A1\u0001"));
        Assert.Throws<ArgumentException>(() => s.WriteFormula(new string('1', 9000)));
        s.WriteFormula("=1+1");
    }

    [Fact]
    public void WritingAllocatesNothingPerCell()
    {
        var sink = new CountingSink();
        using XlsxStreamWriter w = XlsxStreamWriter.Create(sink);
        SheetStreamWriter s = w.AddSheet("S");
        int fmt = w.AddFormat(new StreamCellFormat { Bold = true });
        string[] words = ["alpha", "beta", "gamma & delta", "with_x"];
        long Run(int rows)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < rows; i++)
            {
                s.BeginRow();
                s.WriteNumber(i + 0.5, fmt);
                s.WriteText(words[i & 3]);
                s.WriteBoolean(true);
                s.WriteFormulaNumber("A1+1", i);
                s.WriteDate(new DateTime(2020, 1, 1), 0);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        _ = Run(2000); // warm up and let the buffers and deflater settle
        long allocated = Run(20000);
        Assert.True(allocated < 20_000, "allocated " + allocated); // 100,000 cells; the deflater may allocate, the writer does not
    }

    private sealed class CountingSink : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }

    private sealed class NonSeekable : Stream
    {
        private readonly MemoryStream _inner = new();

        public byte[] Bytes() => _inner.ToArray();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }
}
