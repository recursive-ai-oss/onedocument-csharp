using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Spreadsheets.Binding;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public enum Level
{
    Low,
    Mid,
    High,
}

/// <summary>Ten typed columns: two strings and eight values.</summary>
[SpreadsheetRow]
public sealed partial class Wide
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public double Amount { get; set; }

    public long Count { get; set; }

    public decimal Price { get; set; }

    public DateTime When { get; set; }

    public bool Flag { get; set; }

    public Level Level { get; set; }

    public short? Small { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class BindingAllocationTests
{
    private static List<Wide> Rows(int n, int distinct = int.MaxValue)
    {
        var list = new List<Wide>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(new Wide
            {
                Id = i,
                Code = "C" + (i % distinct).ToString("D8", System.Globalization.CultureInfo.InvariantCulture),
                Amount = i * 0.37,
                Count = i * 1000L,
                Price = i % 1000 / 100m,
                When = new DateTime(2000, 1, 1).AddMinutes(i),
                Flag = i % 2 == 0,
                Level = (Level)(i % 3),
                Small = i % 5 == 0 ? null : (short)(i % 100),
                Name = "Row number " + (i % distinct).ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        return list;
    }

    // The size of a string object on 64-bit .NET: header + length + chars + terminator, rounded up to 8 bytes.
    private static long StringBytes(string s) => (22 + 2L * s.Length + 7) & ~7L;

    private static long ReadBytes(byte[] file, int rows)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        int n = 0;
        foreach (Wide w in Spreadsheet.Read<Wide>(new MemoryStream(file)))
        {
            if (++n == rows)
            {
                break;
            }
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Theory]
    [InlineData(OfficeFormat.Xlsx)]
    [InlineData(OfficeFormat.Csv)]
    public void ReadingAllocatesOnlyTheObjectsAndTheirStrings(OfficeFormat format)
    {
        const int Small = 10_000;
        const int Large = 60_000;
        List<Wide> data = Rows(Large);
        var ms = new MemoryStream();
        Spreadsheet.Write(ms, data, format);
        byte[] file = ms.ToArray();

        // What the rows themselves cost: the object, plus the two strings of each row.
        long a = GC.GetAllocatedBytesForCurrentThread();
        var probe = new Wide[1000];
        for (int i = 0; i < probe.Length; i++)
        {
            probe[i] = new Wide();
        }

        long measuredObject = (GC.GetAllocatedBytesForCurrentThread() - a - 8 * probe.Length - 24) / probe.Length;
        Assert.InRange(measuredObject, 16, 160);

        ReadBytes(file, 100); // warm up the JIT
        long smallBytes = ReadBytes(file, Small);
        long largeBytes = ReadBytes(file, Large);
        long strings = 0;
        for (int i = Small; i < Large; i++)
        {
            strings += StringBytes(data[i].Code) + StringBytes(data[i].Name);
        }

        double perRow = (largeBytes - smallBytes - strings) / (double)(Large - Small) - measuredObject;
        Assert.True(perRow is > -8 and < 8, $"{format}: {perRow:F2} bytes per row beyond the object ({measuredObject}) and its strings");
    }

    [Theory]
    [InlineData(OfficeFormat.Xlsx)]
    [InlineData(OfficeFormat.Csv)]
    public void WritingAllocatesNothingPerRow(OfficeFormat format)
    {
        List<Wide> data = Rows(40_000, 200); // repeated strings: the shared string table holds each once
        Spreadsheet.Write(Stream.Null, data.Take(100), format); // warm up
        long Measure(int rows)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Spreadsheet.Write(Stream.Null, data.Take(rows), format);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        long small = Measure(5_000);
        long large = Measure(40_000);
        double perRow = (large - small) / 35_000.0;
        Assert.True(perRow < 8, $"{format}: {perRow:F2} bytes per row");
    }
}
