using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Calc;

// Reads every legacy workbook under a directory: does it open, which typed defects and losses come up, and (information only)
// how the cached formula results compare with a recalculation of the converted workbook.
//
//   XlsProbe <directory> [--max N] [--list]
//
// Nothing is written; file names are printed only for failures so the output carries no corpus content.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: XlsProbe <directory> [--max N] [--list]");
            return 2;
        }

        if (args.Length > 1 && args[0] == "--why")
        {
            // Why does the container reader refuse a file the sniffer calls unknown?
            byte[] raw = File.ReadAllBytes(args[1]);
            try
            {
                using var strict = new RecursiveAI.OneArchive.Cfb.CompoundFileReader(raw, new RecursiveAI.OneArchive.Cfb.CompoundFileReaderOptions { Limits = OfficeLimits.Default.ToArchiveLimits() });
                Console.WriteLine("strict reader opens it; Find(Workbook)=" + (strict.Find("Workbook")?.ObjectType.ToString() ?? "null") + " Find(Book)=" + (strict.Find("Book")?.ObjectType.ToString() ?? "null"));
                Console.WriteLine("sniffer says " + RecursiveAI.OneDocument.Formats.FormatSniffer.Detect(raw));
            }
            catch (Exception ex)
            {
                Console.WriteLine("strict reader: " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                using var pkg = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(raw));
                Console.WriteLine("package reader opens it; streams: " + string.Join(", ", pkg.Parts.Select(p => p.Name)));
                foreach (Defect d in ((DefectCollection)pkg.Defects).ToArray())
                {
                    Console.WriteLine("  " + d.Code + " " + d.Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("package reader: " + ex.GetType().Name + ": " + ex.Message);
            }

            return 0;
        }

        if (args.Length > 2 && args[0] == "--sst")
        {
            // Lays out the shared string table records of one file: lengths of SST and the CONTINUE records after it.
            var c0 = new OpenContext(new OfficeLimits { MaxPartSize = 256 << 20 });
            using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(args[1], c0);
            var part = cfb.FindPart("/Workbook") ?? cfb.FindPart("/Book");
            var rs = RecursiveAI.OneDocument.Spreadsheets.Biff.BiffRecordSet.Parse(part!.GetBytes(), c0);
            for (int i = 0; i < rs.Records.Count; i++)
            {
                if (rs.Records[i].Type != 0x00FC)
                {
                    continue;
                }

                var d = rs.Data(rs.Records[i]).Span;
                Console.WriteLine($"SST at {rs.Records[i].Offset} len={rs.Records[i].Length} total={BitConverter.ToUInt32(d)} unique={BitConverter.ToUInt32(d[4..])}");
                for (int k = i + 1; k < rs.Records.Count && rs.Records[k].Type == 0x003C; k++)
                {
                    var cd = rs.Data(rs.Records[k]).Span;
                    Console.WriteLine($"  CONTINUE len={rs.Records[k].Length} first bytes {Convert.ToHexString(cd[..Math.Min(8, cd.Length)])}");
                }
            }

            return 0;
        }

        int max = int.MaxValue;
        bool list = args.Contains("--list");
        for (int i = 1; i + 1 < args.Length; i++)
        {
            if (args[i] == "--max")
            {
                max = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
            }
        }

        var files = Directory.EnumerateFiles(args[0], "*.xls", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Take(max).ToList();
        var defects = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var losses = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var firstFile = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var formats = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int opened = 0;
        long formulas = 0;
        long compared = 0;
        long matched = 0;
        long cells = 0;
        var clock = Stopwatch.StartNew();
        foreach (string file in files)
        {
            var ctx = new OpenContext(new OfficeLimits { MaxPartSize = 256 << 20 });
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                OfficeFormat fmt = RecursiveAI.OneDocument.Formats.FormatSniffer.Detect(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(bytes), ctx.Limits);
                formats[fmt.ToString()] = formats.GetValueOrDefault(fmt.ToString()) + 1;
                using Workbook wb = Workbook.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(bytes), ctx);
                opened++;
                foreach (Defect d in ((DefectCollection)ctx.Defects).ToArray())
                {
                    defects[d.Code] = defects.GetValueOrDefault(d.Code) + 1;
                    firstFile.TryAdd(d.Code, Path.GetFileName(file));
                }

                foreach (LossItem l in wb.ImportLoss!.Items)
                {
                    losses[l.Code] = losses.GetValueOrDefault(l.Code) + l.Count;
                }

                // Cached results before and after a recalculation (information only: the function library, not the reader, decides the difference).
                var cached = new List<(Worksheet S, int R, int C, CellValue V)>();
                foreach (Worksheet ws in wb.Sheets)
                {
                    foreach (CellRowView row in ws.Store.EnumerateRows())
                    {
                        cells += row.Count;
                        for (int i = 0; i < row.Count; i++)
                        {
                            if (row.HasFormulaAt(i))
                            {
                                formulas++;
                                cached.Add((ws, row.Row, row.ColumnAt(i), row.ValueAt(i)));
                            }
                        }
                    }
                }

                if (cached.Count > 0 && cached.Count < 50000)
                {
                    wb.Recalculate(new RecalcOptions { Full = true });
                    foreach ((Worksheet s, int r, int c, CellValue v) in cached)
                    {
                        CellValue now = s[r, c].Value;
                        compared++;
                        if (Same(v, now, s.GetText(v), s.GetText(now)))
                        {
                            matched++;
                        }
                    }
                }
            }
            catch (OfficeException ex)
            {
                string key = ex.Defect.Code;
                if (!failures.TryGetValue(key, out List<string>? l))
                {
                    failures[key] = l = [];
                }

                l.Add(Path.GetFileName(file));
            }
            catch (Exception ex)
            {
                string key = "UNTYPED " + ex.GetType().Name + " " + ex.Message.Split('\n')[0];
                if (!failures.TryGetValue(key, out List<string>? l))
                {
                    failures[key] = l = [];
                }

                l.Add(Path.GetFileName(file));
            }
        }

        Console.WriteLine($"files={files.Count} opened={opened} failed={files.Count - opened} seconds={clock.Elapsed.TotalSeconds:F1}");
        Console.WriteLine("sniffed: " + string.Join(", ", formats.Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine($"cells={cells} formulas={formulas} recalculated-and-compared={compared} equal={matched}");
        foreach (KeyValuePair<string, List<string>> f in failures)
        {
            Console.WriteLine($"FAIL {f.Key}: {f.Value.Count} file(s){(list ? ": " + string.Join(", ", f.Value.Take(20)) : string.Empty)}");
        }

        Console.WriteLine("defects (files-with-code counts are per defect): " + string.Join(", ", defects.Select(kv => kv.Key + "=" + kv.Value)));
        if (list)
        {
            Console.WriteLine("first file per defect: " + string.Join(", ", firstFile.Select(kv => kv.Key + "=" + kv.Value)));
        }

        Console.WriteLine("losses (items): " + string.Join(", ", losses.Select(kv => kv.Key + "=" + kv.Value)));
        return failures.Keys.Any(k => k.StartsWith("UNTYPED", StringComparison.Ordinal)) ? 1 : 0;
    }

    private static bool Same(CellValue a, CellValue b, string at, string bt)
    {
        if (a.Kind == CellValueKind.Number && b.Kind == CellValueKind.Number)
        {
            return Math.Abs(a.Number - b.Number) <= 1e-9 * Math.Max(1, Math.Abs(a.Number));
        }

        return a.Kind == b.Kind && at == bt;
    }
}
