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
//   CsvProbe cells [--excel] [--year N] FILE...   one NAME.ours.tsv per file beside OUTDIR (-o DIR), same layout as the oracle's cells mode
//   CsvProbe sweep DIR                    reads every *.csv, rewrites it, reads it again; prints counts
//   CsvProbe gen OUTDIR                   workbooks written as CSV (raw and formatted) with the cells Excel should read back (*.ours.tsv)
//   CsvProbe bench FILE ROWS              writes a ROWS x 10 file, then times reading it (fields; fields + values) and writing it back, with allocations
//   CsvProbe roundtrip FILE.xlsx OUT.csv  writes the first sheet of a workbook as CSV (formatted), prints the loss report
return Probe.Run(args);

internal static class Probe
{
    // The error values as Excel reports them through the object model (CVErr codes).
    private static readonly Dictionary<CellError, int> ErrorCodes = new()
    {
        [CellError.Null] = -2146826288, [CellError.DivideByZero] = -2146826281, [CellError.Value] = -2146826273, [CellError.Reference] = -2146826265,
        [CellError.Name] = -2146826259, [CellError.Number] = -2146826252, [CellError.NotAvailable] = -2146826246, [CellError.GettingData] = -2146826245,
        [CellError.Spill] = -2146826243, [CellError.Calc] = -2146826238,
    };

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
            case "bench":
                return Bench(args[1], int.Parse(args[2], CultureInfo.InvariantCulture));
            case "gen":
                return Generate(args[1]);
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
        int year = 0;
        string outDir = ".";
        var files = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-o")
            {
                outDir = args[++i];
            }
            else if (args[i] == "--year")
            {
                year = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] != "--excel")
            {
                files.Add(args[i]);
            }
        }

        Directory.CreateDirectory(outDir);
        var parser = new CsvValueParser(new CsvValueOptions { PreserveLeadingZeros = !excel, PreserveLongNumbers = !excel, DefaultYear = year });
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
                        CsvValueKind.Error => ("error", ErrorCodes.GetValueOrDefault(v.Error, 0).ToString(CultureInfo.InvariantCulture)),
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

    // Builds a workbook through the CSV import (so dates, percentages and currency carry number formats), adds cells through the model,
    // writes it as CSV twice and records what Excel should read back from each file.
    private static int Generate(string outDir)
    {
        Directory.CreateDirectory(outDir);
        const string seed = "label,n\r\ndate,2020-01-15\r\ndatetime,2020-01-15 13:45\r\ntime,13:45:30\r\npercent,12.5%\r\ngrouped,\"1,234,567.5\"\r\ncurrency,$1234.5\r\nexp,1.5E-7\r\nneg,(42)\r\n";
        using Workbook wb = CsvDocument.ToWorkbook(new MemoryStream(Encoding.UTF8.GetBytes(seed)));
        Worksheet s = wb.Sheets[0];
        int r = 10;
        double[] numbers = [0, 1, -1, 0.5, 0.1 + 0.2, 1.0 / 3, 1e300, 1e-300, 123456789012345, 1234567890123456, 4503599627370497, 100, 1e15, 1e21, -1234.5678, 0.000123, 255];
        foreach (double d in numbers)
        {
            s["A" + r].SetString("num");
            s["B" + r].SetNumber(d);
            r++;
        }

        string[] texts = ["hello", "a,b", "say \"hi\"", "two\nlines", "r\r\nn", "\u00e9\u20ac\u65e5\u672c", " padded ", "semi;colon", "tab\there", "pipe|pipe", "'quoted", "=SUM(1,2)", "+49 123", "@x", "-dash", "x\ty", "\"\"", "back\\slash", "emoji \U0001F600", new string('x', 5000)];
        foreach (string t in texts)
        {
            s["A" + r].SetString("text");
            s["B" + r].SetString(t);
            r++;
        }

        s["A" + r].SetString("bool");
        s["B" + r++].SetBoolean(true);
        s["A" + r].SetString("bool");
        s["B" + r++].SetBoolean(false);
        s["A" + r].SetString("err");
        s["B" + r++].SetError(CellError.NotAvailable);
        s["A" + r].SetString("err");
        s["B" + r++].SetError(CellError.DivideByZero);
        s["A" + r].SetString("formula");
        s["B" + r].Formula = "1+1";
        s["B" + r++].SetNumber(2);
        s["A" + r].SetString("gap");
        r += 2; // two empty rows
        s["C" + r].SetString("after gap, column C");

        foreach ((string name, bool formatted) in new[] { ("w-raw", false), ("w-formatted", true) })
        {
            var options = new CsvWriteOptions { Formatted = formatted };
            using (FileStream fs = File.Create(Path.Combine(outDir, name + ".csv")))
            {
                CsvDocument.Write(wb, fs, options);
            }

            // What Excel should read: strings as strings (the injection guard adds a quote), numbers and dates as numbers, no formulas.
            var lines = new List<string>();
            foreach (CellRowView row in s.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    CellValue v = row.ValueAt(i);
                    string type;
                    string value;
                    switch (v.Kind)
                    {
                        case CellValueKind.Text:
                            type = "string";
                            value = s.GetText(v);
                            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
                            {
                                value = "'" + value;
                            }

                            break;
                        case CellValueKind.Boolean:
                            (type, value) = ("boolean", v.Boolean ? "True" : "False");
                            break;
                        case CellValueKind.Error:
                            (type, value) = ("error", ErrorCodes.GetValueOrDefault(v.Error, 0).ToString(CultureInfo.InvariantCulture));
                            break;
                        default:
                            (type, value) = ("number", v.Number.ToString("R", CultureInfo.InvariantCulture));
                            break;
                    }

                    lines.Add(row.Row + "\t" + row.ColumnAt(i) + "\t" + type + "\t" + Esc(value) + "\tGeneral");
                }
            }

            File.WriteAllLines(Path.Combine(outDir, name + ".ours.tsv"), lines, new UTF8Encoding(false));
        }

        return 0;
    }

    private static int Bench(string path, int rows)
    {
        var sw = Stopwatch.StartNew();
        using (var w = new CsvWriter(File.Create(path), new CsvWriteOptions { ByteOrderMark = false }, leaveOpen: false))
        {
            for (int i = 0; i < rows; i++)
            {
                w.WriteField("name" + i, isText: false);
                w.WriteNumber(i * 1.5);
                w.WriteField("quoted, \"text\" " + (i % 97), isText: false);
                w.WriteField("2020-01-" + (1 + i % 28).ToString("00", CultureInfo.InvariantCulture), isText: false);
                w.WriteField((i % 100) + "%", isText: false);
                w.WriteField(i % 2 == 0 ? "TRUE" : "FALSE", isText: false);
                w.WriteField("plain text field number " + i, isText: false);
                w.WriteField("1,234.5", isText: false);
                w.WriteField([], isText: false);
                w.WriteField("x", isText: false);
                w.EndRecord();
            }
        }

        long bytes = new FileInfo(path).Length;
        Console.WriteLine($"file {bytes / 1e6:F1} MB, {rows} rows, generated in {sw.Elapsed.TotalSeconds:F2}s");
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (bool values in new[] { false, true })
            {
                var parser = new CsvValueParser();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                long fields = 0, sum = 0;
                using var fs = File.OpenRead(path);
                using var r = new CsvReader(fs);
                while (r.ReadRecord())
                {
                    for (int c = 0; c < r.FieldCount; c++)
                    {
                        fields++;
                        ReadOnlySpan<char> f = r.GetField(c);
                        sum += values ? (long)parser.Parse(f).Kind : f.Length;
                    }
                }

                double secs = sw.Elapsed.TotalSeconds;
                long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                Console.WriteLine($"read {(values ? "fields+values" : "fields       ")}: {secs:F3}s  {bytes / 1e6 / secs:F0} MB/s  {fields / secs / 1e6:F1} Mfields/s  allocated {alloc / 1024.0:F0} KiB total ({alloc / (double)fields:F4} B/field)  [{sum}]");
            }

            long a1 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            using (var r = new CsvReader(File.OpenRead(path), leaveOpen: false))
            using (var w = new CsvWriter(Stream.Null, new CsvWriteOptions { ByteOrderMark = false }))
            {
                while (r.ReadRecord())
                {
                    for (int c = 0; c < r.FieldCount; c++)
                    {
                        w.WriteField(r.GetField(c), isText: false);
                    }

                    w.EndRecord();
                }
            }

            double wsecs = sw.Elapsed.TotalSeconds;
            long walloc = GC.GetAllocatedBytesForCurrentThread() - a1;
            Console.WriteLine($"read+write copy       : {wsecs:F3}s  {bytes / 1e6 / wsecs:F0} MB/s  allocated {walloc / 1024.0:F0} KiB total");
        }

        File.Delete(path);
        return 0;
    }
}
