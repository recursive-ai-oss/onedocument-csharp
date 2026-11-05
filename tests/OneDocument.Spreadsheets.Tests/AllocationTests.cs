namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class AllocationTests
{
    [Fact]
    public void IteratingUsedCellsAllocatesNothingPerCell()
    {
        using Workbook wb = TestBook.Open();
        CellStore store = wb.Sheets["Data"].Store;
        long sum = Walk(store); // warm up the JIT
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            sum += Walk(store);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sum != 0);
        Assert.Equal(0, allocated);
    }

    private static long Walk(CellStore store)
    {
        long sum = 0;
        Span<CellValue> values = stackalloc CellValue[16];
        foreach (CellRowView row in store.EnumerateRows())
        {
            int n = row.CopyValues(values);
            for (int i = 0; i < row.Count; i++)
            {
                sum += row.ColumnAt(i) + row.StyleAt(i) + (long)row.ValueAt(i).Kind + (row.HasFormulaAt(i) ? 1 : 0);
            }

            sum += n;
        }

        foreach ((int r, CellValue v, int s) in store.EnumerateColumn(2))
        {
            sum += r + s + (long)v.Kind;
        }

        return sum;
    }
}
