using System.Text;
using RecursiveAI.OneOffice.Formats;
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Binding;

namespace RecursiveAI.OneOffice.Smoke;

internal enum SmokeKind
{
    First,
    Second = 5,
}

/// <summary>A row type bound by the source generator: no reflection is involved in reading or writing it.</summary>
[SpreadsheetRow]
internal sealed partial record SmokeRow(int Id, string Name, DateTime When, decimal? Amount, SmokeKind Kind);

/// <summary>Writes typed rows to XLSX and CSV and reads them back, under NativeAOT and WASM.</summary>
internal static class PocoSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        SmokeRow[] rows =
        [
            new(1, "a, b", new DateTime(2024, 5, 6, 7, 8, 9), 12.5m, SmokeKind.Second),
            new(2, "c", new DateTime(1999, 12, 31), null, SmokeKind.First),
        ];
        foreach (OfficeFormat format in new[] { OfficeFormat.Xlsx, OfficeFormat.Csv })
        {
            using var stream = new MemoryStream();
            Spreadsheet.Write(stream, rows, format);
            stream.Position = 0;
            SmokeRow[] back = [.. Spreadsheet.Read<SmokeRow>(stream)];
            if (back.Length != 2 || back[0] != rows[0] || back[1] != rows[1])
            {
                failures.Add("poco " + format);
            }
        }

        using var text = new MemoryStream(Encoding.UTF8.GetBytes("id,name,when,amount,kind\n3,x,2020-01-02,,second\n"));
        SmokeRow[] read = [.. Spreadsheet.Read<SmokeRow>(text)];
        if (read.Length != 1 || read[0].Kind != SmokeKind.Second || read[0].When != new DateTime(2020, 1, 2))
        {
            failures.Add("poco csv names");
        }

        return failures;
    }
}
