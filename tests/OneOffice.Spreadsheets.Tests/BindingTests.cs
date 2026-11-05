using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Formats;
using RecursiveAI.OneOffice.Spreadsheets.Binding;

namespace RecursiveAI.OneOffice.Spreadsheets.Tests;

public enum Status
{
    Draft,
    Active,
    Closed = 10,
}

[SpreadsheetRow(Sheet = "People")]
public partial record Person
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double? Score { get; set; }

    public DateTime Born { get; set; }

    public DateOnly Day { get; set; }

    public TimeOnly At { get; set; }

    public bool Active { get; set; }

    public decimal Balance { get; set; }

    public Guid Key { get; set; }

    public Status State { get; set; }

    public Status? Other { get; set; }

    public long Big { get; set; }

    public ulong UBig { get; set; }

    public float Ratio { get; set; }

    public byte Small { get; set; }

    public string? Note { get; set; }

    [SpreadsheetColumn(Ignore = true)]
    public int Skipped { get; set; }

    public string Computed => Name + Id;
}

[SpreadsheetRow(Strict = true)]
public partial record Item(string Sku, int Qty, decimal? Price);

[SpreadsheetRow]
public partial struct Point
{
    public double X { get; set; }

    public double Y { get; set; }
}

[SpreadsheetRow]
public partial class Renamed
{
    [SpreadsheetColumn("Full name", Order = 2)]
    public string Name { get; set; } = string.Empty;

    [SpreadsheetColumn(Order = 1, Format = "#,##0.00")]
    public decimal Amount { get; set; }

    [SpreadsheetColumn(EnumAsNumber = true, Order = 3)]
    public Status State { get; set; }
}

[SpreadsheetRow(HasHeader = false)]
public partial class Plain
{
    public string A { get; set; } = string.Empty;

    [SpreadsheetColumn(Index = 4)]
    public int D { get; set; }
}

public class BindingTests
{
    private static List<Person> People(int n)
    {
        var list = new List<Person>();
        for (int i = 0; i < n; i++)
        {
            list.Add(new Person
            {
                Id = i,
                Name = "Name " + i + (i % 7 == 0 ? ", with comma and \"quotes\"" : string.Empty),
                Score = i % 3 == 0 ? null : i * 1.25,
                Born = new DateTime(1980, 1, 1).AddDays(i * 37).AddSeconds(i * 61),
                Day = new DateOnly(2000, 1, 1).AddDays(i),
                At = new TimeOnly(i % 24, i % 60, i % 60),
                Active = i % 2 == 0,
                Balance = i * 1234.56m,
                Key = new Guid(i, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11),
                State = (Status)(i % 3 == 2 ? 10 : i % 3),
                Other = i % 4 == 0 ? null : Status.Active,
                Big = i % 5 == 0 ? long.MaxValue - i : -i,
                UBig = i % 5 == 0 ? ulong.MaxValue - (ulong)i : (ulong)i,
                Ratio = i * 0.1f,
                Small = (byte)(i % 256),
                Note = i % 11 == 0 ? null : "n" + i,
            });
        }

        return list;
    }

    private static MemoryStream WriteTo<T>(IEnumerable<T> rows, OfficeFormat format)
        where T : ISpreadsheetRow<T>
    {
        var ms = new MemoryStream();
        Spreadsheet.Write(ms, rows, format);
        ms.Position = 0;
        return ms;
    }

    [Theory]
    [InlineData(OfficeFormat.Xlsx)]
    [InlineData(OfficeFormat.Csv)]
    [InlineData(OfficeFormat.Tsv)]
    public void WriteThenReadGivesEqualRows(OfficeFormat format)
    {
        List<Person> people = People(500);
        using MemoryStream ms = WriteTo(people, format);
        var defects = new DefectCollection(100);
        List<Person> back = [.. Spreadsheet.Read<Person>(ms, new OpenContext(defects: defects))];
        Assert.Empty(defects);
        Assert.Equal(people.Count, back.Count);
        for (int i = 0; i < people.Count; i++)
        {
            Assert.Equal(people[i], back[i]);
        }
    }

    [Fact]
    public void XlsxDatesAreDatesAndHeaderIsFirstRow()
    {
        using MemoryStream ms = WriteTo(People(3), OfficeFormat.Xlsx);
        string path = Path.Combine(Path.GetTempPath(), "u24-rows-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            File.WriteAllBytes(path, ms.ToArray());
            List<object?[]> rows = [.. Spreadsheet.ReadRows(path, "People")];
            Assert.Equal(4, rows.Count);
            Assert.Equal("Id", rows[0][0]);
            Assert.Equal("Born", rows[0][3]);
            Assert.IsType<DateTime>(rows[1][3]);
            Assert.IsType<DateTime>(rows[1][4]);
            Assert.Equal(new DateTime(1980, 1, 1), rows[1][3]);
            Assert.IsType<double>(rows[1][0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecordWithPrimaryConstructorRoundTrips()
    {
        Item[] items = [new("A-1", 3, 9.99m), new("B-2", 0, null)];
        using MemoryStream ms = WriteTo(items, OfficeFormat.Csv);
        Assert.Equal(items, Spreadsheet.Read<Item>(ms).ToArray());
    }

    [Fact]
    public void StructRoundTrips()
    {
        Point[] points = [new() { X = 1.5, Y = -2 }, new() { X = 1e300, Y = 0.1 }];
        using MemoryStream ms = WriteTo(points, OfficeFormat.Xlsx);
        Point[] back = [.. Spreadsheet.Read<Point>(ms)];
        Assert.Equal(2, back.Length);
        Assert.Equal(1e300, back[1].X);
        Assert.Equal(0.1, back[1].Y);
    }

    [Fact]
    public void RenamedOrderedColumnsAndEnumNumbers()
    {
        Renamed[] rows = [new() { Name = "x", Amount = 1234.5m, State = Status.Closed }];
        using MemoryStream ms = WriteTo(rows, OfficeFormat.Csv);
        string text = new StreamReader(ms).ReadToEnd().TrimStart('\uFEFF');
        Assert.Equal("Amount,Full name,State\r\n1234.5,x,10\r\n", text);
        ms.Position = 0;
        Renamed back = Assert.Single(Spreadsheet.Read<Renamed>(ms));
        Assert.Equal(Status.Closed, back.State);
        Assert.Equal("x", back.Name);
        Assert.Equal(1234.5m, back.Amount);
    }

    [Fact]
    public void HeaderlessUsesPositionAndIndex()
    {
        using var ms = new MemoryStream("a,b,c,4\nz,,,9\n"u8.ToArray());
        Plain[] rows = [.. Spreadsheet.Read<Plain>(ms)];
        Assert.Equal(2, rows.Length);
        Assert.Equal("a", rows[0].A);
        Assert.Equal(4, rows[0].D);
        Assert.Equal(9, rows[1].D);
        using MemoryStream written = WriteTo(rows, OfficeFormat.Csv);
        Assert.Equal("a,,,4\r\nz,,,9\r\n", new StreamReader(written).ReadToEnd().TrimStart('\uFEFF'));
    }

    [Fact]
    public void EnumsReadFromNamesNumbersAndAnyCase()
    {
        using var ms = new MemoryStream("Id,Name,State,Other\n1,a,closed,10\n2,b,1,Draft\n3,c,nope,\n"u8.ToArray());
        var defects = new DefectCollection(200);
        Person[] rows = [.. Spreadsheet.Read<Person>(ms, new OpenContext(defects: defects))];
        Assert.Equal(Status.Closed, rows[0].State);
        Assert.Equal(Status.Closed, rows[0].Other);
        Assert.Equal(Status.Active, rows[1].State);
        Assert.Equal(Status.Draft, rows[1].Other);
        Assert.Equal(Status.Draft, rows[2].State);
        Assert.Null(rows[2].Other);
        Defect bad = Assert.Single(defects, d => d.Code == "binding.cell-unconvertible");
        Assert.Equal("C4", bad.Location.Address);
    }

    [Fact]
    public void LenientReportsLocatedDefectsAndKeepsDefaults()
    {
        using var ms = new MemoryStream("Id,Name,Born\n1,a,not a date\nx,b,2024-02-03\n"u8.ToArray());
        var defects = new DefectCollection(200);
        Person[] rows = [.. Spreadsheet.Read<Person>(ms, new OpenContext(defects: defects))];
        Assert.Equal(2, rows.Length);
        Assert.Equal(default, rows[0].Born);
        Assert.Equal(0, rows[1].Id);
        Assert.Equal(new DateTime(2024, 2, 3), rows[1].Born);
        Assert.Equal(["C2", "A3"], defects.Where(d => d.Code == "binding.cell-unconvertible").Select(d => d.Location.Address).ToArray());
        Assert.Contains(defects, d => d.Code == "binding.column-missing");
        Assert.All(defects, d => Assert.Equal(DefectSeverity.Repaired, d.Severity));
    }

    [Fact]
    public void ARepeatedHeaderUsesTheFirstColumnAndSaysSo()
    {
        using var ms = new MemoryStream("Sku,Qty,sku\nA,1,Z\n"u8.ToArray());
        var defects = new DefectCollection(20);
        Item row = Assert.Single(Spreadsheet.Read<Item>(ms, new OpenContext(defects: defects)));
        Assert.Equal("A", row.Sku);
        Defect d = Assert.Single(defects);
        Assert.Equal("binding.header-duplicate", d.Code);
        Assert.Equal("C1", d.Location.Address);
    }

    [Fact]
    public void StrictThrowsAtTheFirstBadCell()
    {
        using var ms = new MemoryStream("Sku,Qty,Price\nA,1,2\nB,two,3\n"u8.ToArray());
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Spreadsheet.Read<Item>(ms).ToList());
        Assert.Equal("binding.cell-unconvertible", ex.Defect.Code);
        Assert.Equal("B3", ex.Location.Address);
    }

    [Fact]
    public void StrictRejectsAMissingColumnButNotAMissingNullableOne()
    {
        using var missing = new MemoryStream("Sku,Price\nA,1\n"u8.ToArray());
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Spreadsheet.Read<Item>(missing).ToList());
        Assert.Equal("binding.column-missing", ex.Defect.Code);
        Assert.Contains("Qty", ex.Message);
        using var noPrice = new MemoryStream("sku;qty\nA;1\n"u8.ToArray());
        Assert.Equal(new Item("A", 1, null), Assert.Single(Spreadsheet.Read<Item>(noPrice)));
    }

    [Fact]
    public void StrictRejectsAnEmptyCellForANonNullableMember()
    {
        using var ms = new MemoryStream("Sku,Qty,Price\nA,,2\n"u8.ToArray());
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Spreadsheet.Read<Item>(ms).ToList());
        Assert.Equal("binding.cell-empty", ex.Defect.Code);
        Assert.Equal("B2", ex.Location.Address);
    }

    [Fact]
    public void BlankRowsAreSkippedAndHeadersMatchIgnoringCaseAndSpace()
    {
        using var ms = new MemoryStream("\n  QTY  , sku\n\n1,A\n\n2,B\n"u8.ToArray());
        Item[] rows = [.. Spreadsheet.Read<Item>(ms)];
        Assert.Equal([new Item("A", 1, null), new Item("B", 2, null)], rows);
    }

    [Fact]
    public void NumbersAndTextCoerceTheWayASpreadsheetDoes()
    {
        using var ms = new MemoryStream("Id,Active,Balance,Score\n\" 12 \",TRUE,\"1,234.50\",1e2\n3.0,0,7,-1.5\n"u8.ToArray());
        Person[] rows = [.. Spreadsheet.Read<Person>(ms)];
        Assert.Equal(12, rows[0].Id);
        Assert.True(rows[0].Active);
        Assert.Equal(1234.5m, rows[0].Balance);
        Assert.Equal(100.0, rows[0].Score);
        Assert.Equal(3, rows[1].Id);
        Assert.False(rows[1].Active);
    }

    [Fact]
    public void WideIntegersSurviveAsText()
    {
        Person p = new() { Big = long.MaxValue, UBig = ulong.MaxValue };
        using MemoryStream ms = WriteTo([p], OfficeFormat.Xlsx);
        Person back = Assert.Single(Spreadsheet.Read<Person>(ms));
        Assert.Equal(long.MaxValue, back.Big);
        Assert.Equal(ulong.MaxValue, back.UBig);
    }

    [Fact]
    public void ReadsTheSheetTheTypeNamesAndFailsClearlyWhenMissing()
    {
        // A workbook with the data on a sheet called Data: the type asks for People.
        byte[] book = TestBook.Bytes();
        using var ms = new MemoryStream(book);
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Spreadsheet.Read<Person>(ms).ToList());
        Assert.Equal("binding.sheet-not-found", ex.Defect.Code);
    }

    [Fact]
    public async Task WriteByExtensionAndAsyncReadBack()
    {
        string dir = Path.Combine(Path.GetTempPath(), "u24-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            List<Person> people = People(2500);
            foreach (string ext in new[] { ".xlsx", ".csv", ".tsv" })
            {
                string path = Path.Combine(dir, "p" + ext);
                Spreadsheet.Write(path, people);
                Assert.Equal(people, Spreadsheet.Read<Person>(path).ToList());
                int n = 0;
                await foreach (Person _ in Spreadsheet.ReadAsync<Person>(path))
                {
                    n++;
                }

                Assert.Equal(people.Count, n);
            }

            Assert.Throws<OfficeUnsupportedException>(() => Spreadsheet.Write(Path.Combine(dir, "p.docx"), people));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void OutOfRangeDateOnWriteIsLocated()
    {
        Person p = new() { Born = new DateTime(1800, 1, 1) };
        var ms = new MemoryStream();
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Spreadsheet.Write(ms, [p], OfficeFormat.Xlsx));
        Assert.Equal("binding.value-out-of-range", ex.Defect.Code);
        Assert.Equal("D2", ex.Location.Address);
    }

    [Fact]
    public void CancellationStopsTheEnumeration()
    {
        using MemoryStream ms = WriteTo(People(50), OfficeFormat.Csv);
        using var cts = new CancellationTokenSource();
        int n = 0;
        Assert.Throws<OperationCanceledException>(() =>
        {
            foreach (Person _ in Spreadsheet.Read<Person>(ms, null, cts.Token))
            {
                if (++n == 5)
                {
                    cts.Cancel();
                }
            }
        });
    }

    [Fact]
    public void GarbageIsATypedFormatError()
    {
        using var ms = new MemoryStream([1, 2, 3, 0, 255, 7]);
        Assert.Throws<OfficeFormatException>(() => Spreadsheet.Read<Person>(ms).ToList());
    }
}
