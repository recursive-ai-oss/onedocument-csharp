using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.NumberFormats;
using RecursiveAI.OneOffice.Spreadsheets;

namespace RecursiveAI.OneOffice.Tools.XlsxProbe;

/// <summary>
/// <c>XlsxProbe fmtgrid DIR... --out OUT [--files N]</c>: picks workbooks from the corpus directories and writes, for each, a small generated
/// workbook that carries its number format codes and numeric values (never the file itself) as <c>OUT/fmt-NN.xlsx</c>, with our displayed text for
/// each cell in <c>OUT/fmt-NN.ours.tsv</c>. The generated workbooks go to Excel in grid mode, whose <c>.answers.tsv</c> is compared with
/// <c>fmtgrid-compare</c>.
/// <c>XlsxProbe fmtgrid-compare OUT</c>: lists the cells where Excel's text and ours differ.
/// </summary>
internal static class FormatGrid
{
    private const int PerFile = 120;
    private const int PerCode = 8;

    public static int Run(string[] args)
    {
        var dirs = new List<string>();
        string? outDir = null;
        int files = 50;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out") outDir = args[++i];
            else if (args[i] == "--files") files = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else dirs.Add(args[i]);
        }

        if (outDir is null || dirs.Count == 0)
        {
            Console.Error.WriteLine("usage: XlsxProbe fmtgrid DIR... --out OUT [--files N]");
            return 2;
        }

        Directory.CreateDirectory(outDir);
        var candidates = new List<string>();
        foreach (string d in dirs)
        {
            candidates.AddRange(Directory.EnumerateFiles(d, "*.xlsx", SearchOption.AllDirectories).Where(f => new FileInfo(f).Length < 3 << 20));
        }

        candidates.Sort(StringComparer.Ordinal);
        int written = 0, rows = 0;
        int step = Math.Max(1, candidates.Count / (files * 8));
        for (int ci = 0; ci < candidates.Count && written < files; ci += step)
        {
            List<(string Code, double Value)>? pairs;
            DateSystem system;
            try
            {
                pairs = Collect(candidates[ci], out system);
            }
            catch (OfficeException)
            {
                continue;
            }

            if (pairs is null || pairs.Select(p => p.Code).Distinct(StringComparer.Ordinal).Count() < 2)
            {
                continue;
            }

            rows += WriteGrid(pairs, system, Path.Combine(outDir, $"fmt-{written:00}"), written);
            written++;
        }

        Console.WriteLine($"{{\"kind\":\"summary\",\"files\":{written},\"cells\":{rows}}}");
        return 0;
    }

    private static List<(string, double)>? Collect(string path, out DateSystem system)
    {
        using Workbook wb = Workbook.Open(path, new OpenContext());
        system = wb.DateSystem;
        var seen = new HashSet<(string, double)>();
        var perCode = new Dictionary<string, int>(StringComparer.Ordinal);
        var list = new List<(string, double)>();
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
                    CellValue v = row.ValueAt(i);
                    if (v.Kind != CellValueKind.Number || !double.IsFinite(v.Number))
                    {
                        continue;
                    }

                    string code = wb.Styles.Resolve(row.StyleAt(i)).NumberFormatCode;
                    if (perCode.GetValueOrDefault(code) >= PerCode || !seen.Add((code, v.Number)))
                    {
                        continue;
                    }

                    perCode[code] = perCode.GetValueOrDefault(code) + 1;
                    list.Add((code, v.Number));
                    if (list.Count >= PerFile)
                    {
                        return list;
                    }
                }
            }
        }

        return list;
    }

    private static int WriteGrid(List<(string Code, double Value)> pairs, DateSystem system, string stem, int index)
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        s.Rename("cells");
        if (system == DateSystem.Date1904)
        {
            MarkupNode first = wb.Markup.Root.ChildElements.First();
            first.InsertXmlBefore("<workbookPr date1904=\"1\"/>"u8);
        }

        var ours = new StringBuilder();
        s["A1"].SetString("id");
        s["B1"].SetString("value");
        int r = 2;
        foreach ((string code, double value) in pairs)
        {
            string id = $"{index:00}-{r - 1:000}";
            s[r, 1].SetString(id);
            Cell c = s[r, 2];
            c.SetNumber(value);
            c.SetStyle(new CellStyleSpec { NumberFormatCode = code });
            // The oracle reads the text in a column 100 characters wide, so General has room for all 15 digits.
            string text = c.GetFormattedText(99);
            ours.Append(id).Append('\t').Append(Escape(text)).Append('\t').Append(Escape(code)).Append('\t').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            r++;
        }

        wb.SaveAs(stem + ".xlsx");
        File.WriteAllText(stem + ".ours.tsv", ours.ToString(), new UTF8Encoding(false));
        return pairs.Count;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    public static int Compare(string dir)
    {
        int total = 0, same = 0, missing = 0;
        var diffs = new List<string>();
        foreach (string ours in Directory.EnumerateFiles(dir, "*.ours.tsv").Order(StringComparer.Ordinal))
        {
            string answers = ours[..^".ours.tsv".Length] + ".answers.tsv";
            if (!File.Exists(answers))
            {
                missing++;
                continue;
            }

            var excel = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(answers, Encoding.UTF8))
            {
                int t = line.IndexOf('\t', StringComparison.Ordinal);
                if (t > 0) excel[line[..t]] = line[(t + 1)..];
            }

            foreach (string line in File.ReadAllLines(ours, Encoding.UTF8))
            {
                string[] f = line.Split('\t');
                if (f.Length < 4) continue;
                total++;
                if (excel.TryGetValue(f[0], out string? e) && e == f[1])
                {
                    same++;
                }
                else
                {
                    diffs.Add($"{f[0]}\tcode={f[2]}\tvalue={f[3]}\tours={f[1]}\texcel={(e ?? "(none)")}");
                }
            }
        }

        File.WriteAllLines(Path.Combine(dir, "fmtgrid-diffs.tsv"), diffs, new UTF8Encoding(false));
        Console.WriteLine($"{{\"kind\":\"fmtgrid\",\"cells\":{total},\"same\":{same},\"different\":{diffs.Count},\"filesWithoutAnswers\":{missing}}}");
        foreach (string d in diffs.Take(15)) Console.WriteLine(d);
        return diffs.Count == 0 ? 0 : 1;
    }
}
