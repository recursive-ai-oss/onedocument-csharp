using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Calc;

namespace RecursiveAI.OneOffice.Tools.CalcSweep;

/// <summary>Writes the Excel test workbooks and compares OneOffice's results with the cells Excel reported for them.</summary>
internal static class OracleCases
{
    public static int Gen(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach ((string name, Action<Workbook> build) in Cases.All)
        {
            using Workbook wb = Workbook.Create();
            build(wb);
            wb.SaveAs(Path.Combine(dir, name + ".xlsx"));
            Console.WriteLine(name);
        }

        return 0;
    }

    public static int Compare(string answers)
    {
        int cells = 0, agree = 0, missing = 0;
        foreach ((string name, Action<Workbook> build) in Cases.All)
        {
            string tsv = Path.Combine(answers, name + ".cells.tsv");
            if (!File.Exists(tsv))
            {
                Console.WriteLine($"{name}: no answers file");
                missing++;
                continue;
            }

            using Workbook wb = Workbook.Create();
            build(wb);
            wb.Recalculate();
            Worksheet s = wb.Sheets[0];
            foreach (string line in File.ReadLines(tsv))
            {
                string[] f = line.Split('\t');
                int row = int.Parse(f[0], CultureInfo.InvariantCulture);
                int col = int.Parse(f[1], CultureInfo.InvariantCulture);
                string expected = f[2] == "number" ? "number:" + double.Parse(f[3], CultureInfo.InvariantCulture).ToString("G15", CultureInfo.InvariantCulture) : f[2] + ":" + f[3];
                string ours = Describe(s, s[row, col].Value);
                cells++;
                if (ours == expected || (f[2] == "empty" && ours == "empty:"))
                {
                    agree++;
                }
                else
                {
                    Console.WriteLine($"{name} {new CellAddress(row, col)}: Excel {expected}, ours {ours}");
                }
            }
        }

        Console.WriteLine($"{agree} of {cells} cells agree ({missing} cases without answers)");
        return agree == cells && missing == 0 ? 0 : 1;
    }

    // The oracle reports errors as Excel's numeric codes and numbers as round-trip text; compare numbers at 15 digits.
    private static string Describe(Worksheet ws, CellValue v)
    {
        switch (v.Kind)
        {
            case CellValueKind.Number:
                return "number:" + v.Number.ToString("G15", CultureInfo.InvariantCulture);
            case CellValueKind.Boolean:
                return "boolean:" + (v.Boolean ? "True" : "False");
            case CellValueKind.Text:
                return "string:" + ws.GetText(v);
            case CellValueKind.Error:
                return "error:" + ErrorCode(v.Error).ToString(CultureInfo.InvariantCulture);
            default:
                return "empty:";
        }
    }

    private static int ErrorCode(CellError e) => e switch
    {
        CellError.Null => 2000,
        CellError.DivideByZero => 2007,
        CellError.Value => 2015,
        CellError.Reference => 2023,
        CellError.Name => 2029,
        CellError.Number => 2036,
        CellError.NotAvailable => 2042,
        CellError.Spill => 2045,
        CellError.Calc => 2046,
        _ => 2015,
    };
}
