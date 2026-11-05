using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Binding;

namespace RecursiveAI.OneOffice.PocoSample;

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
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: PocoSample DIR");
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
}
