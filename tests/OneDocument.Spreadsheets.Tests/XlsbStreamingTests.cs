using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets.Streaming;
using RecursiveAI.OneDocument.Xlsb;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class XlsbStreamingTests
{
    private const int Rows = 4000;

    /// <summary>Writes a streamed sheet in the XLSX form, converts it through the hub into a binary workbook and returns both files.</summary>
    private static (byte[] Xlsx, byte[] Xlsb) Generate(int rows = Rows)
    {
        var ms = new MemoryStream();
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(ms))
        {
            SheetStreamWriter s = w.AddSheet("S");
            for (int i = 0; i < rows; i++)
            {
                s.BeginRow();
                s.WriteNumber(i * 1.25);
                s.WriteText(i % 7 == 0 ? "seven" : "x" + (i % 50), 0);
                s.WriteBoolean(i % 2 == 0);
                s.WriteFormulaNumber("A1+1", i);
                s.WriteDate(new DateTime(2020, 1, 1).AddDays(i), 0);
            }
        }

        byte[] xlsx = ms.ToArray();
        using Workbook wb = Workbook.Open(new MemoryStream(xlsx));
        using var bin = new MemoryStream();
        wb.SaveAs(bin, OfficeFormat.Xlsb, new LossReport());
        return (xlsx, bin.ToArray());
    }

    [Fact]
    public void BinaryCursorDeliversTheSameCellsAsTheXmlCursor()
    {
        (byte[] xlsx, byte[] xlsb) = Generate(300);
        using XlsxStreamReader x = XlsxStreamReader.Open(new MemoryStream(xlsx));
        using XlsbStreamReader b = XlsbStreamReader.Open(new MemoryStream(xlsb));
        Assert.Equal("S", b.Sheets[0].Name);
        using SheetCursor xc = x.OpenSheet(0);
        using XlsbSheetCursor bc = b.OpenSheet(0);
        int cells = 0;
        while (xc.ReadRow())
        {
            Assert.True(bc.ReadRow());
            Assert.Equal(xc.RowNumber, bc.RowNumber);
            while (xc.ReadCell())
            {
                Assert.True(bc.ReadCell());
                Assert.Equal(xc.Address, bc.Address);
                Assert.Equal(xc.Kind, bc.Kind);
                Assert.Equal(xc.HasFormula, bc.HasFormula);
                Assert.Equal(xc.Number, bc.Number);
                if (xc.Kind == CellValueKind.Text)
                {
                    Assert.Equal(xc.GetText(), bc.GetText());
                }

                Assert.Equal(xc.IsDate, bc.IsDate);
                cells++;
            }

            Assert.False(bc.ReadCell());
        }

        Assert.False(bc.ReadRow());
        Assert.Equal(300 * 5, cells);
    }

    [Fact]
    public void BinaryReadingAllocatesNothingPerCell()
    {
        (_, byte[] xlsb) = Generate();
        using XlsbStreamReader r = XlsbStreamReader.Open(new MemoryStream(xlsb));
        _ = r.SharedStringCount;
        _ = r.IsDateStyle(0);
        _ = Walk(r, Rows);
        long last = -1;
        int steady = 0;
        for (int k = 0; k < 30 && steady < 3; k++)
        {
            long t0 = GC.GetAllocatedBytesForCurrentThread();
            _ = Walk(r, Rows);
            long used = GC.GetAllocatedBytesForCurrentThread() - t0;
            steady = used == last ? steady + 1 : 0;
            last = used;
        }

        long full = AllocationProbe.Min(() =>
        {
            long t0 = GC.GetAllocatedBytesForCurrentThread();
            _ = Walk(r, Rows);
            return GC.GetAllocatedBytesForCurrentThread() - t0;
        }, 8);
        long small = AllocationProbe.Min(() =>
        {
            long t0 = GC.GetAllocatedBytesForCurrentThread();
            _ = Walk(r, 1000);
            return GC.GetAllocatedBytesForCurrentThread() - t0;
        }, 8);
        // Opening the part costs the same for any size; 3,000 more rows (15,000 cells) must cost nothing.
        Assert.True(full - small < 2_000, "allocated " + full + " for " + Rows + " rows, " + small + " for 1000");
    }

    private static long Walk(XlsbStreamReader r, int maxRows)
    {
        long sum = 0;
        Span<char> buf = stackalloc char[64];
        using XlsbSheetCursor c = r.OpenSheet(0);
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
                    sum += (long)c.FormulaKind;
                }
            }
        }

        return sum;
    }
}
