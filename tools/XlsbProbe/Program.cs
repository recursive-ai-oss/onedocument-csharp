using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Csv;

// Reads binary workbooks and reports what the reader makes of them.
//   XlsbProbe sweep <dir>            open, read every cell, save untouched, convert to SpreadsheetML; one line per file and a summary
//   XlsbProbe xml <file> [part]      print a part as the workbook hub sees it (default: the workbook part)
//   XlsbProbe csv <file> <out-dir>   write every sheet as CSV (cell values only) for comparison with another converter
return args.Length == 0 ? Usage() : args[0] switch
{
    "sweep" => Sweep(args[1]),
    "xml" => Xml(args[1], args.Length > 2 ? args[2] : null),
    "csv" => WriteCsv(args[1], args[2]),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: XlsbProbe sweep <dir> | xml <file> [part] | csv <file> <out-dir>");
    return 2;
}

static Dictionary<string, string> Hashes(Stream s)
{
    s.Position = 0;
    using OpcPackage p = OpcPackage.Open(s);
    var map = new Dictionary<string, string>();
    foreach (IPackagePart part in p.Parts)
    {
        map[part.Name] = Convert.ToHexString(SHA256.HashData(part.GetBytes().Span));
    }

    return map;
}

static int Sweep(string dir)
{
    bool verbose = Environment.GetEnvironmentVariable("XLSB_VERBOSE") == "1";
    int files = 0, opened = 0, identical = 0, converted = 0, typedFailures = 0, untyped = 0;
    long cells = 0, formulas = 0, parts = 0, partsSame = 0;
    var codes = new SortedDictionary<string, int>();
    var lossCodes = new SortedDictionary<string, long>();
    int again = 0, againMismatch = 0;
    var rawTypes = new SortedDictionary<int, int>();
    foreach (string file in Directory.EnumerateFiles(dir, "*.xlsb", SearchOption.AllDirectories).Order())
    {
        files++;
        string name = Path.GetFileName(file)[..8];
        try
        {
            var ctx = new OpenContext();
            using Workbook wb = Workbook.Open(file, ctx);
            opened++;
            int sheetCells = 0, sheetFormulas = 0;
            foreach (Worksheet s in wb.Sheets)
            {
                if (s.Kind != SheetKind.Worksheet)
                {
                    continue;
                }

                foreach (CellRowView row in s.Store.EnumerateRows())
                {
                    sheetCells += row.Count;
                }

                sheetFormulas += CountFormulas(s);
            }

            cells += sheetCells;
            formulas += sheetFormulas;
            foreach (IPackagePart hp in wb.Package.Parts.Where(q => q.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                string xml = Encoding.UTF8.GetString(hp.GetBytes().Span);
                int at = 0;
                while ((at = xml.IndexOf("<xb:r", at, StringComparison.Ordinal)) >= 0)
                {
                    int t0 = xml.IndexOf(" t=\"", at, StringComparison.Ordinal) + 4;
                    int t1 = xml.IndexOf('"', t0);
                    if (int.TryParse(xml.AsSpan(t0, t1 - t0), out int rt))
                    {
                        rawTypes[rt] = rawTypes.GetValueOrDefault(rt) + 1;
                    }

                    at = t1;
                }
            }

            using var save = new MemoryStream();
            wb.SaveAs(save, OfficeFormat.Xlsb);
            Dictionary<string, string> before, after;
            using (var fs = File.OpenRead(file))
            {
                before = Hashes(fs);
            }

            after = Hashes(save);
            int same = before.Count(kv => after.TryGetValue(kv.Key, out string? h) && h == kv.Value);
            parts += before.Count;
            partsSame += same;
            bool ident = same == before.Count && after.Count == before.Count;
            if (ident)
            {
                identical++;
            }

            string conv = "ok";
            try
            {
                using var x = new MemoryStream();
                var loss = new LossReport();
                try
                {
                    wb.SaveAs(x, OfficeFormat.Xlsx, loss);
                }
                catch (OfficeUnsupportedException)
                {
                    x.SetLength(0);
                    wb.SaveAs(x, OfficeFormat.Xlsm, loss);
                }
                foreach (LossItem li in loss.Items)
                {
                    lossCodes[li.Code] = lossCodes.GetValueOrDefault(li.Code) + li.Count;
                }

                x.Position = 0;
                using Workbook back = Workbook.Open(x);
                converted++;
                conv = "ok(" + loss.Items.Count + " loss)";

                // Back to a binary workbook through every writer, then compare what the model holds.
                using var y = new MemoryStream();
                var loss2 = new LossReport();
                back.SaveAs(y, OfficeFormat.Xlsb, loss2);
                foreach (LossItem li in loss2.Items)
                {
                    lossCodes["to-xlsb:" + li.Code] = lossCodes.GetValueOrDefault("to-xlsb:" + li.Code) + li.Count;
                }

                y.Position = 0;
                using Workbook re = Workbook.Open(y);
                again++;
                int c2 = 0, f2 = 0;
                foreach (Worksheet s in re.Sheets)
                {
                    if (s.Kind != SheetKind.Worksheet)
                    {
                        continue;
                    }

                    foreach (CellRowView row in s.Store.EnumerateRows())
                    {
                        c2 += row.Count;
                    }

                    f2 += CountFormulas(s);
                }

                if (c2 != sheetCells || f2 != sheetFormulas)
                {
                    againMismatch++;
                    conv += " ROUNDTRIP cells " + sheetCells + "->" + c2 + " formulas " + sheetFormulas + "->" + f2;
                }
            }
            catch (OfficeException ex)
            {
                conv = "typed:" + ex.Defect.Code;
            }

            foreach (Defect d in wb.Defects)
            {
                codes[d.Code] = codes.GetValueOrDefault(d.Code) + 1;
                if (verbose)
                {
                    Console.WriteLine("    " + d);
                }
            }

            Console.WriteLine($"{name} sheets={wb.Sheets.Count} cells={sheetCells} formulas={sheetFormulas} defects={wb.Defects.Count} parts={same}/{before.Count} xlsx={conv}");
        }
        catch (OfficeException ex)
        {
            typedFailures++;
            codes[ex.Defect.Code] = codes.GetValueOrDefault(ex.Defect.Code) + 1;
            Console.WriteLine($"{name} TYPED {ex.Defect.Code}: {ex.Message}");
        }
        catch (Exception ex)
        {
            untyped++;
            Console.WriteLine($"{name} UNTYPED {ex.GetType().Name}: {ex.Message}");
        }
    }

    Console.WriteLine($"files={files} opened={opened} typed-failures={typedFailures} untyped={untyped} cells={cells} formulas={formulas} untouched-save-identical-files={identical} parts-identical={partsSame}/{parts} xlsx-converted={converted}");
    Console.WriteLine($"binary -> SpreadsheetML -> binary: {again} files, {againMismatch} with different cell or formula counts");
    foreach ((int t, int n) in rawTypes)
    {
        Console.WriteLine($"  raw record type {t}: {n}");
    }

    foreach ((string code, long n) in lossCodes)
    {
        Console.WriteLine($"  loss {code}: {n}");
    }

    foreach ((string code, int n) in codes)
    {
        Console.WriteLine($"  {code}: {n}");
    }

    return untyped == 0 ? 0 : 1;
}

static int Xml(string file, string? part)
{
    using Workbook wb = Workbook.Open(file);
    IPackagePart? p = wb.Package.FindPart(part ?? wb.MainPartName);
    if (p is null)
    {
        Console.Error.WriteLine("no such part; parts: " + string.Join(", ", wb.Package.Parts.Select(x => x.Name)));
        return 1;
    }

    Console.WriteLine(Encoding.UTF8.GetString(p.GetBytes().Span));
    return 0;
}

static int WriteCsv(string file, string outDir)
{
    Directory.CreateDirectory(outDir);
    using Workbook wb = Workbook.Open(file);
    int i = 0;
    foreach (Worksheet s in wb.Sheets)
    {
        if (s.Kind != SheetKind.Worksheet)
        {
            continue;
        }

        // Raw cell values, one line per row with data; no number formats applied.
        using var w = new StreamWriter(Path.Combine(outDir, (++i).ToString() + ".csv"), false, new UTF8Encoding(false));
        int last = 0;
        foreach (CellRowView row in s.Store.EnumerateRows())
        {
            for (; last < row.Row - 1; last++)
            {
                w.Write('\n');
            }

            var cols = new SortedDictionary<int, string>();
            for (int c = 0; c < row.Count; c++)
            {
                CellValue v = row.ValueAt(c);
                string t = v.Kind switch
                {
                    CellValueKind.Number => v.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    CellValueKind.Text => s.GetText(v),
                    CellValueKind.Boolean => v.Boolean ? "TRUE" : "FALSE",
                    CellValueKind.Error => CellErrors.ToText(v.Error),
                    _ => string.Empty,
                };
                cols[row.ColumnAt(c)] = t;
            }

            int width = cols.Count == 0 ? 0 : cols.Keys.Max();
            string[] line = new string[width];
            Array.Fill(line, string.Empty);
            foreach ((int k, string t) in cols)
            {
                line[k - 1] = t.Length == 0 ? string.Empty : '"' + t.Replace("\"", "\"\"") + '"';
            }

            w.Write(string.Join(',', line));
            w.Write('\n');
            last = row.Row;
        }
    }

    return 0;
}

static int CountFormulas(Worksheet s)
{
    int n = 0;
    foreach (CellRowView row in s.Store.EnumerateRows())
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (s.Store.GetFormula(row.Row, row.ColumnAt(i)) is not null)
            {
                n++;
            }
        }
    }

    return n;
}
