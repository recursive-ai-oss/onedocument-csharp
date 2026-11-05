using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Csv;

// CsvProbe: drives the CSV layer for the checks in tools/csv-grid and the corpus sweep.
//
//   CsvProbe cells [--excel] FILE...      one NAME.ours.tsv per file beside OUTDIR (-o DIR), same layout as the oracle's cells mode
//   CsvProbe sweep DIR                    reads every *.csv, rewrites it, reads it again; prints counts
//   CsvProbe roundtrip FILE.xlsx OUT.csv  writes the first sheet of a workbook as CSV (formatted), prints the loss report
return Probe.Run(args);

internal static class Probe
{
    private static readonly string[] ErrorNames = ["#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A"];
    private static readonly int[] ErrorCodes = [2000, 2007, 2015, 2023, 2029, 2036, 2042];

    internal static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: CsvProbe cells|sweep|roundtrip ...");
            return 2;
        }

        switch (args[0])
        {
            case "cells":
                return Cells(args);
            case "sweep":
                return Sweep(args[1]);
            case "roundtrip":
                return WorkbookToCsv(args[1], args[2]);
            default:
                Console.Error.WriteLine("unknown command " + args[0]);
                return 2;
        }
    }

    private static int Cells(string[] args)
    {
        bool excel = args.Contains("--excel");
        string outDir = ".";
        var files = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-o")
            {
                outDir = args[++i];
            }
            else if (args[i] != "--excel")
            {
                files.Add(args[i]);
            }
        }

        Directory.CreateDirectory(outDir);
        var parser = new CsvValueParser(new CsvValueOptions { PreserveLeadingZeros = !excel, PreserveLongNumbers = !excel });
        foreach (string file in files)
        {
            var ctx = new OpenContext();
            using FileStream fs = File.OpenRead(file);
            using var reader = new CsvReader(fs, new CsvReadOptions { Delimiter = ',' }, ctx);
            var lines = new List<string>();
            int row = 0;
            while (reader.ReadRecord())
            {
                row++;
                for (int c = 0; c < reader.FieldCount; c++)
                {
                    ReadOnlySpan<char> f = reader.GetField(c);
                    if (f.Length == 0)
                    {
                        continue;
                    }

                    CsvValue v = parser.Parse(f);
                    (string type, string value) = v.Kind switch
                    {
                        CsvValueKind.Text => ("string", f.ToString()),
                        CsvValueKind.Boolean => ("boolean", v.Boolean ? "True" : "False"),
                        CsvValueKind.Error => ("error", ErrorCodes[Math.Min((int)v.Error, 6)].ToString(CultureInfo.InvariantCulture)),
                        _ => ("number", v.Number.ToString("R", CultureInfo.InvariantCulture)),
                    };
                    lines.Add(row + "\t" + (c + 1) + "\t" + type + "\t" + Esc(value) + "\t" + Esc(v.FormatCode ?? "General"));
                }
            }

            File.WriteAllLines(Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + ".ours.tsv"), lines, new UTF8Encoding(false));
        }

        return 0;
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    // Reads each file, writes its records back with minimal quoting and the original line ending, reads that again, and compares.
    private static int Sweep(string dir)
    {
        int files = 0, clean = 0, defective = 0, fieldEqual = 0, byteEqual = 0, failed = 0;
        var codes = new SortedDictionary<string, int>();
        long records = 0, fields = 0;
        foreach (string file in Directory.EnumerateFiles(dir, "*.csv").Order(StringComparer.Ordinal))
        {
            files++;
            try
            {
                byte[] original = File.ReadAllBytes(file);
                var ctx = new OpenContext();
                List<string[]> first = ReadAll(original, ctx);
                var defects = ctx.Defects as DefectCollection;
                if (defects is { Count: > 0 })
                {
                    defective++;
                    foreach (Defect d in defects)
                    {
                        codes[d.Code] = codes.GetValueOrDefault(d.Code) + 1;
                    }
                }
                else
                {
                    clean++;
                }

                records += first.Count;
                foreach (string[] r in first)
                {
                    fields += r.Length;
                }

                string eol = original.AsSpan().IndexOf((byte)'\n') >= 0 && original.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";
                var ms = new MemoryStream();
                using (var w = new CsvWriter(ms, new CsvWriteOptions { Delimiter = DelimiterOf(original), LineEnding = eol, ByteOrderMark = original.Length > 2 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF, FormulaInjection = CsvFormulaInjection.None }))
                {
                    foreach (string[] r in first)
                    {
                        foreach (string f in r)
                        {
                            w.WriteField(f, isText: false);
                        }

                        w.EndRecord();
                    }
                }

                List<string[]> second = ReadAll(ms.ToArray(), new OpenContext());
                bool same = first.Count == second.Count && first.Zip(second).All(p => p.First.AsSpan().SequenceEqual(p.Second));
                if (same)
                {
                    fieldEqual++;
                }

                if (ms.ToArray().AsSpan().SequenceEqual(original))
                {
                    byteEqual++;
                }
            }
            catch (OfficeException e)
            {
                failed++;
                Console.WriteLine("typed failure " + Path.GetFileName(file) + ": " + e.Defect.Code);
            }
        }

        Console.WriteLine($"files={files} clean={clean} withDefects={defective} typedFailures={failed} records={records} fields={fields}");
        Console.WriteLine($"rewrite-then-read field-equal={fieldEqual}/{files - failed} byte-equal-to-original={byteEqual}/{files - failed}");
        foreach ((string c, int n) in codes)
        {
            Console.WriteLine($"  defect {c}: {n} files");
        }

        return 0;
    }

    private static char DelimiterOf(byte[] data)
    {
        using var r = new CsvReader(new MemoryStream(data));
        r.ReadRecord();
        return r.Delimiter;
    }

    private static List<string[]> ReadAll(byte[] data, OpenContext ctx)
    {
        using var r = new CsvReader(new MemoryStream(data), context: ctx);
        var rows = new List<string[]>();
        while (r.ReadRecord())
        {
            var f = new string[r.FieldCount];
            for (int i = 0; i < f.Length; i++)
            {
                f[i] = r.GetField(i).ToString();
            }

            rows.Add(f);
        }

        return rows;
    }

    private static int WorkbookToCsv(string input, string output)
    {
        using Workbook wb = Workbook.Open(input);
        var loss = new LossReport();
        using FileStream fs = File.Create(output);
        CsvDocument.Write(wb, fs, new CsvWriteOptions(), loss);
        Console.WriteLine(loss.ToString());
        foreach (LossItem i in loss.Items)
        {
            Console.WriteLine($"  {i.Code}: {i.Count}");
        }

        return 0;
    }
}
