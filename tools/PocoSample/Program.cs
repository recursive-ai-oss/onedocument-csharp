using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Binding;

namespace RecursiveAI.OneDocument.PocoSample;

public enum Tier
{
    Basic,
    Plus,
    Pro = 10,
}

[SpreadsheetRow(Sheet = "Orders")]
public sealed partial record Order
{
    public int Id { get; init; }

    public string Customer { get; init; } = string.Empty;

    [SpreadsheetColumn(Format = "#,##0.00")]
    public decimal Total { get; init; }

    public DateTime Placed { get; init; }

    public DateOnly Due { get; init; }

    public TimeOnly Slot { get; init; }

    public bool Paid { get; init; }

    public Tier Tier { get; init; }

    public double? Discount { get; init; }

    public long Big { get; init; }

    public Guid Ref { get; init; }
}

/// <summary>Writes the same typed rows as XLSX, CSV and TSV into a directory, for opening in a spreadsheet application.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "sweep")
        {
            return Sweep(args.Skip(1));
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: PocoSample DIR | sweep DIR...");
            return 2;
        }

        Directory.CreateDirectory(args[0]);
        var rows = new List<Order>();
        for (int i = 0; i < 60; i++)
        {
            rows.Add(new Order
            {
                Id = i + 1,
                Customer = i % 9 == 0 ? "Ann, \"the\" Third" : "Customer " + i,
                Total = i * 123.456m,
                Placed = new DateTime(2024, 1, 1, 8, 30, 0).AddHours(i * 7),
                Due = new DateOnly(2024, 3, 1).AddDays(i),
                Slot = new TimeOnly(i % 24, (i * 7) % 60, 0),
                Paid = i % 2 == 0,
                Tier = (Tier)(i % 3 == 2 ? 10 : i % 3),
                Discount = i % 4 == 0 ? null : i / 100.0,
                Big = i % 10 == 0 ? long.MaxValue - i : i,
                Ref = new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1),
            });
        }

        Spreadsheet.Write(Path.Combine(args[0], "typed.xlsx"), rows);
        Spreadsheet.Write(Path.Combine(args[0], "typed.csv"), rows);
        Spreadsheet.Write(Path.Combine(args[0], "typed.tsv"), rows);

        // Read the XLSX back: a quick self-check before the files go anywhere.
        List<Order> back = [.. Spreadsheet.Read<Order>(Path.Combine(args[0], "typed.xlsx"))];
        bool same = back.Count == rows.Count && back.Zip(rows).All(p => p.First == p.Second);
        Console.WriteLine(same ? "typed.xlsx reads back equal" : "typed.xlsx DIFFERS");
        return same ? 0 : 1;
    }

    /// <summary>
    /// Reads the first sheet of every workbook under the given directories into a row type that does not fit them (columns by position,
    /// mixed types, everything lenient). Only typed errors and defects are acceptable outcomes.
    /// </summary>
    private static int Sweep(IEnumerable<string> dirs)
    {
        int files = 0;
        int ok = 0;
        long rows = 0;
        var typed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var defects = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var other = new List<string>();
        foreach (string file in dirs.SelectMany(d => Directory.EnumerateFiles(d, "*.xlsx", SearchOption.AllDirectories)).Order(StringComparer.Ordinal))
        {
            files++;
            var sink = new DefectCollection(1000);
            try
            {
                foreach (Loose _ in Spreadsheet.Read<Loose>(file, new OpenContext(defects: sink)))
                {
                    rows++;
                }

                ok++;
            }
            catch (OfficeException ex)
            {
                typed[ex.Defect.Code] = typed.GetValueOrDefault(ex.Defect.Code) + 1;
            }
            catch (Exception ex)
            {
                other.Add(Path.GetFileName(file) + ": " + ex.GetType().Name + ": " + ex.Message);
            }

            foreach (Defect d in sink)
            {
                defects[d.Code] = defects.GetValueOrDefault(d.Code) + 1;
            }
        }

        Console.WriteLine($"files {files}, read to the end {ok}, rows {rows}");
        foreach (KeyValuePair<string, int> kv in typed)
        {
            Console.WriteLine($"typed error {kv.Key}: {kv.Value}");
        }

        foreach (KeyValuePair<string, int> kv in defects)
        {
            Console.WriteLine($"defect {kv.Key}: {kv.Value}");
        }

        foreach (string o in other.Take(20))
        {
            Console.WriteLine("UNTYPED " + o);
        }

        Console.WriteLine($"untyped exceptions: {other.Count}");
        return other.Count == 0 ? 0 : 1;
    }
}

/// <summary>Columns by position with mixed types; most workbooks will not fit it.</summary>
[SpreadsheetRow(HasHeader = false)]
public sealed partial class Loose
{
    public string? A { get; set; }

    public double? B { get; set; }

    public int? C { get; set; }

    public DateTime? D { get; set; }

    public bool? E { get; set; }

    public string F { get; set; } = string.Empty;

    public decimal? G { get; set; }

    public Tier? H { get; set; }

    public TimeOnly? I { get; set; }

    public long? J { get; set; }
}
