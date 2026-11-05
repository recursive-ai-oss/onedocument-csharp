using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Tools.CalcSweep;

/// <summary>
/// The cached-value harness, and with <c>gen</c> / <c>compare</c> the Excel test workbooks. <c>CalcSweep PATH... [--shard I/N] [--max N] [--show N] [--out DIR] [--threads N]</c>: opens each workbook (a
/// file, or every .xlsx/.xlsm under a directory), remembers what every formula cell holds, recalculates everything, and compares. Numbers
/// are compared at 15 significant digits, text, booleans and errors exactly. Each mismatch is classified:
/// <c>unimplemented</c> (the formula calls a function the registry lacks; the name is counted), <c>external</c> (reads another workbook),
/// <c>downstream-or-bug</c> (the workbook has an unimplemented function elsewhere, so the cause is told apart only by looking) or <c>bug</c>. The floor is
/// matches / formulas whose functions are all implemented. Corpus files are read only; nothing is written back to them.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "gen")
        {
            return OracleCases.Gen(args[1]);
        }

        if (args.Length == 2 && args[0] == "compare")
        {
            return OracleCases.Compare(args[1]);
        }

        var paths = new List<string>();
        int shard = 0, shards = 1, max = int.MaxValue, show = 30, threads = 0;
        string? outDir = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shard":
                    string[] p = args[++i].Split('/');
                    shard = int.Parse(p[0], CultureInfo.InvariantCulture);
                    shards = int.Parse(p[1], CultureInfo.InvariantCulture);
                    break;
                case "--max":
                    max = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--show":
                    show = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--threads":
                    threads = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--out":
                    outDir = args[++i];
                    break;
                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count == 0)
        {
            Console.Error.WriteLine("usage: CalcSweep PATH... [--shard I/N] [--max N] [--show N] [--out DIR] [--threads N]");
            return 2;
        }

        var files = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                files.Add(path);
            }
        }

        files.Sort(StringComparer.Ordinal);
        files = [.. files.Where((_, i) => i % shards == shard).Take(max)];
        FunctionRegistry registry = FunctionRegistry.CreateDefault();
        var totals = new Dictionary<string, long>();
        var samples = new Dictionary<string, List<string>>();
        var unimplemented = new Dictionary<string, long>();
        var mismatchLines = new List<string>();
        int opened = 0, failed = 0;
        foreach (string file in files)
        {
            try
            {
                Sweep(file, registry, threads, totals, samples, unimplemented, mismatchLines);
                opened++;
            }
            catch (Exception ex) when (ex is OfficeException or IOException or InvalidDataException)
            {
                failed++;
                Console.WriteLine($"skip {Path.GetFileName(file)}: {ex.GetType().Name}");
            }
        }

        long formulas = totals.GetValueOrDefault("formulas");
        long testable = totals.GetValueOrDefault("testable");
        long matched = totals.GetValueOrDefault("testable-match");
        Console.WriteLine($"files {opened} (unreadable {failed}); formulas {formulas}; of those with every function implemented {testable}, matching {matched} ({(testable == 0 ? 0 : 100.0 * matched / testable):F2}%)");
        foreach ((string k, long v) in totals.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {k}: {v}");
        }

        Console.WriteLine("most common unimplemented functions:");
        foreach ((string k, long v) in unimplemented.OrderByDescending(kv => kv.Value).Take(25))
        {
            Console.WriteLine($"  {k}: {v}");
        }

        foreach ((string cls, List<string> list) in samples)
        {
            Console.WriteLine($"samples {cls}:");
            foreach (string line in list.Take(show))
            {
                Console.WriteLine("  " + line);
            }
        }

        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllLines(Path.Combine(outDir, $"mismatches-{shard}.tsv"), mismatchLines, new UTF8Encoding(false));
        }

        return 0;
    }

    private static void Add(Dictionary<string, long> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;

    private static void Sweep(string file, FunctionRegistry registry, int threads, Dictionary<string, long> totals, Dictionary<string, List<string>> samples,
        Dictionary<string, long> unimplemented, List<string> mismatchLines)
    {
        using Workbook wb = Workbook.Open(file);
        var before = new List<(Worksheet Sheet, int Row, int Column, string Value, string? Missing, bool External, string Formula)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DefinedName n in wb.Names)
        {
            names.Add(n.Name);
        }

        foreach (Worksheet ws in wb.Sheets)
        {
            if (ws.Kind != SheetKind.Worksheet)
            {
                continue;
            }

            foreach (CellRowView row in ws.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    CellFormula? f = row.FormulaAt(i);
                    if (f is null || f.Kind == CellFormulaKind.DataTable)
                    {
                        continue;
                    }

                    string text = f.Text;
                    string? missing = null;
                    bool external = false;
                    if (text.Length > 0 && FormulaParser.TryParse(text, out FormulaNode? tree, out _))
                    {
                        Scan(tree!, registry, names, ref missing, ref external);
                    }

                    before.Add((ws, row.Row, row.ColumnAt(i), Key(ws, row.ValueAt(i)), missing, external, text));
                }
            }
        }

        RecalcResult r = wb.RecalculateAll(new RecalcOptions { MaxDegreeOfParallelism = threads });
        string name = Path.GetFileName(file);
        bool anyBad = before.Exists(b => b.Missing is not null);
        int fileMismatch = 0;
        foreach ((Worksheet ws, int row, int col, string expected, string? missing, bool external, string formula) in before)
        {
            Add(totals, "formulas");
            string actual = Key(ws, ws[row, col].Value);
            bool match = expected == actual;
            if (missing is null && !external)
            {
                Add(totals, "testable");
                if (match)
                {
                    Add(totals, "testable-match");
                }
            }

            if (match)
            {
                continue;
            }

            fileMismatch++;
            string cls = missing is not null ? "unimplemented" : external ? "external" : anyBad ? "downstream-or-bug" : "bug";
            if (missing is not null)
            {
                unimplemented[missing] = unimplemented.GetValueOrDefault(missing) + 1;
            }

            Add(totals, "mismatch-" + cls);
            string addr = $"{name}!{ws.Name}!{new CellAddress(row, col)}";
            string line = $"{cls}\t{addr}\t{formula}\texpected {expected}\tgot {actual}";
            mismatchLines.Add(line);
            if (!samples.TryGetValue(cls, out List<string>? list))
            {
                samples[cls] = list = [];
            }

            if (list.Count < 200)
            {
                list.Add(line);
            }
        }

        Add(totals, "files");
        Console.WriteLine($"{name}: {before.Count} formulas, {r.Evaluated} evaluated, {fileMismatch} differ, {r.CircularCells} circular, {r.SpillErrors} #SPILL!");
    }

    private static void Scan(FormulaNode n, FunctionRegistry registry, HashSet<string> names, ref string? missing, ref bool external)
    {
        switch (n)
        {
            case FunctionCallNode f when !registry.TryGet(f.Name, out _) && !names.Contains(f.Name) && !f.Name.StartsWith("_xlpm.", StringComparison.Ordinal):
                missing ??= f.Name.ToUpperInvariant();
                break;
            case CellReferenceNode { Prefix.Workbook: not null }:
            case AreaReferenceNode { Prefix.Workbook: not null }:
                external = true;
                break;
        }

        foreach (FormulaNode c in n.Children)
        {
            Scan(c, registry, names, ref missing, ref external);
        }
    }

    // Excel stores 15 significant digits, so numbers compare as their 15-digit text.
    private static string Key(Worksheet ws, CellValue v) => v.Kind switch
    {
        CellValueKind.Number => "n:" + v.Number.ToString("G15", CultureInfo.InvariantCulture),
        CellValueKind.Boolean => "b:" + v.Boolean,
        CellValueKind.Text => "s:" + ws.GetText(v),
        CellValueKind.Error => "e:" + ws.GetText(v),
        _ => "z:",
    };
}
