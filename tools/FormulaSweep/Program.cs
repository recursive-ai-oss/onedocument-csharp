using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Calc;
using RecursiveAI.OneOffice.Spreadsheets;

namespace RecursiveAI.OneOffice.Tools.FormulaSweep;

/// <summary>
/// Parses every formula of every workbook under the given directories and prints them back in file form.
/// <c>FormulaSweep DIR... [--shard I/N] [--max N] [--show N]</c>. Reports files, formulas, parse failures by code (with samples), print mismatches
/// (the printed text differs from the stored text) and, for R1C1 and shared-formula translation, a second round trip that must also be stable.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var dirs = new List<string>();
        int shard = 0;
        int shards = 1;
        int max = int.MaxValue;
        int show = 20;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shard":
                    string[] p = args[++i].Split('/');
                    shard = int.Parse(p[0]);
                    shards = int.Parse(p[1]);
                    break;
                case "--max":
                    max = int.Parse(args[++i]);
                    break;
                case "--show":
                    show = int.Parse(args[++i]);
                    break;
                default:
                    dirs.Add(args[i]);
                    break;
            }
        }

        string[] files = dirs.SelectMany(d => Directory.EnumerateFiles(d, "*.xl*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).Where((_, i) => i % shards == shard).Take(max).ToArray();
        long formulas = 0, parsed = 0, mismatches = 0, r1c1Bad = 0, shared = 0, unreadable = 0, withFormulas = 0;
        var failures = new Dictionary<string, int>();
        var samples = new List<string>();
        var mismatchSamples = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var options = new FormulaParserOptions();
        foreach (string f in files)
        {
            long before = formulas;
            try
            {
                using Workbook wb = Workbook.Open(f);
                foreach (Worksheet s in wb.Sheets)
                {
                    if (s.Kind != SheetKind.Worksheet)
                    {
                        continue;
                    }

                    foreach (CellRowView row in s.Store.EnumerateRows())
                    {
                        for (int i = 0; i < row.Count; i++)
                        {
                            CellFormula? cf = row.FormulaAt(i);
                            if (cf is null || string.IsNullOrEmpty(cf.Text) || cf.Kind == CellFormulaKind.DataTable)
                            {
                                continue;
                            }

                            formulas++;
                            if (cf.Kind == CellFormulaKind.Shared)
                            {
                                shared++;
                            }

                            string text = cf.Text;
                            if (!FormulaParser.TryParse(text, out FormulaNode? node, out FormulaSyntaxError err, options) || node is null)
                            {
                                failures[err.Code] = failures.GetValueOrDefault(err.Code) + 1;
                                if (samples.Count < show)
                                {
                                    samples.Add($"{Path.GetFileName(f)} {s.Name}!{new CellAddress(row.Row, row.ColumnAt(i))} {err} :: {Trim(text)}");
                                }

                                continue;
                            }

                            parsed++;
                            string printed = FormulaPrinter.Print(node);
                            if (!string.Equals(printed, text, StringComparison.Ordinal))
                            {
                                mismatches++;
                                if (mismatchSamples.Count < show)
                                {
                                    mismatchSamples.Add($"{Path.GetFileName(f)} {s.Name}!{new CellAddress(row.Row, row.ColumnAt(i))} :: {Trim(text)} => {Trim(printed)}");
                                }
                            }

                            // R1C1 must be a faithful second spelling: print as R1C1 and parse it back to the same A1 text.
                            if (seen.Add(text))
                            {
                                int r = row.Row;
                                int c = row.ColumnAt(i);
                                string r1 = FormulaPrinter.Print(node, new FormulaPrintOptions { Syntax = FormulaSyntax.R1C1, OriginRow = r, OriginColumn = c });
                                bool ok = FormulaParser.TryParse(r1, out FormulaNode? back, out _, new FormulaParserOptions { Syntax = FormulaSyntax.R1C1, OriginRow = r, OriginColumn = c })
                                    && back is not null && FormulaPrinter.Print(back) == FormulaPrinter.Print(FormulaParser.Parse(printed));
                                if (!ok)
                                {
                                    r1c1Bad++;
                                    if (mismatchSamples.Count < show)
                                    {
                                        mismatchSamples.Add($"R1C1 {Path.GetFileName(f)} {new CellAddress(r, c)} :: {Trim(text)} => {Trim(r1)}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OfficeException)
            {
                unreadable++;
            }

            if (formulas > before)
            {
                withFormulas++;
            }
        }

        Console.WriteLine($"files={files.Length} unreadable={unreadable} filesWithFormulas={withFormulas} formulas={formulas} shared={shared} parsed={parsed} failed={formulas - parsed} printMismatch={mismatches} r1c1Mismatch={r1c1Bad}");
        foreach (KeyValuePair<string, int> kv in failures.OrderByDescending(k => k.Value))
        {
            Console.WriteLine($"failure {kv.Key} {kv.Value}");
        }

        foreach (string s in samples)
        {
            Console.WriteLine("sample " + s);
        }

        foreach (string s in mismatchSamples)
        {
            Console.WriteLine("mismatch " + s);
        }

        return 0;
    }

    private static string Trim(string s) => s.Length > 160 ? s[..160] + "..." : s;
}
