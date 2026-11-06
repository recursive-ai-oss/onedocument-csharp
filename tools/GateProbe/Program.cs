using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Tools.GateProbe;

/// <summary>
/// Bulk drivers for the corpus gates that have no probe of their own. One JSON line per file on stdout; the last line is a summary.
///   GateProbe convert IN OUT EXT [--ext a,b] [--shard i/n] [--limit N]   opens every file by content and saves it as OUT/&lt;name&gt;.EXT; reports defects and the loss codes of both directions
///   GateProbe odf-rt IN [--shard i/n]                                    opens every OpenDocument file, writes it back untouched and compares entry by entry; then replaces one entry and checks the rest stayed
/// Exit 0 always unless usage is wrong (2): findings are in the lines.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Project references do not bring the packages' build files, so the kinds are registered by hand.
        RecursiveAI.OneDocument.Formats.FormatRegistry registry = RecursiveAI.OneDocument.Formats.FormatRegistry.Default;
        DocumentFormats.Register(registry);
        SpreadsheetFormats.Register(registry);
        PresentationFormats.Register(registry);
        if (args.Length >= 4 && args[0] == "convert")
        {
            return Convert(args);
        }

        if (args.Length >= 4 && args[0] == "trace")
        {
            // GateProbe trace FILE SHEET ADDRESS: follows the first same-sheet precedent that is an error after recalculation, to find where an error starts (debugging aid).
            using Workbook wb = Workbook.Open(args[1]);
            wb.RecalculateAll();
            Worksheet ws = wb.Sheets[args[2]];
            string addr = args[3];
            for (int depth = 0; depth < 12; depth++)
            {
                Cell cell = ws[addr];
                string f = cell.Formula ?? string.Empty;
                Console.WriteLine($"{ws.Name}!{addr}: ={f} -> {cell.Value.Kind}:{ws.GetText(cell.Value)}");
                string? next = null;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(f, @"(?<![A-Za-z0-9_!'])\$?([A-Z]{1,3})\$?([0-9]+)(?![A-Za-z0-9_(])"))
                {
                    string a = m.Groups[1].Value + m.Groups[2].Value;
                    if (ws[a].Value.Kind == CellValueKind.Error)
                    {
                        next = a;
                        break;
                    }
                }

                if (next is null)
                {
                    break;
                }

                addr = next;
            }

            return 0;
        }

        if (args.Length >= 4 && args[0] == "cell")
        {
            // GateProbe cell FILE SHEET ADDRESS...: the sheet names, and for each cell its formula, cached value and recalculated value (debugging aid).
            using Workbook wb = Workbook.Open(args[1]);
            Console.WriteLine("sheets: " + string.Join(" | ", wb.Sheets.Select(s => s.Name + (s.Kind == SheetKind.Worksheet ? "" : "(" + s.Kind + ")"))));
            Worksheet ws = wb.Sheets[args[2]];
            var before = args[3..].Select(a => (a, ws[a].Formula, ws[a].Value)).ToList();
            wb.RecalculateAll();
            foreach ((string a, string? f, CellValue v) in before)
            {
                Console.WriteLine($"{a}: ={f} cached {v.Kind}:{ws.GetText(v)} recalculated {ws[a].Value.Kind}:{ws.GetText(ws[a].Value)}");
            }

            return 0;
        }

        if (args.Length >= 4 && args[0] == "text")
        {
            return TextOf(args[1], args[2], args[3]);
        }

        if (args.Length >= 2 && args[0] == "odf-rt")
        {
            return OdfRoundTrip(args);
        }

        Console.Error.WriteLine("usage: GateProbe convert IN OUT EXT [--ext a,b] [--shard i/n] [--limit N] | odf-rt IN [--shard i/n]");
        return 2;
    }

    private static (int Shard, int Shards, int Limit, string[] Ext) Options(string[] args)
    {
        int shard = 0, shards = 1, limit = int.MaxValue;
        string[] ext = [];
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--shard":
                    string[] p = args[i + 1].Split('/');
                    shard = int.Parse(p[0], CultureInfo.InvariantCulture);
                    shards = int.Parse(p[1], CultureInfo.InvariantCulture);
                    break;
                case "--limit":
                    limit = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    break;
                case "--ext":
                    ext = args[i + 1].Split(',');
                    break;
            }
        }

        return (shard, shards, limit, ext);
    }

    private static List<string> Files(string dir, int shard, int shards, int limit, string[] ext) =>
        [.. Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => ext.Length == 0 || ext.Contains(Path.GetExtension(f).TrimStart('.'), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Where((_, i) => i % shards == shard)
            .Take(limit)];

    private static string[] Codes(LossReport? r) => r is null ? [] : [.. r.Items.Select(i => i.Code + ":" + i.Count)];

    private static int Convert(string[] args)
    {
        string input = args[1], output = args[2], target = args[3].TrimStart('.');
        (int shard, int shards, int limit, string[] ext) = Options(args);
        Directory.CreateDirectory(output);
        int opened = 0, saved = 0, typed = 0, untyped = 0, lossless = 0, lossy = 0;
        var loss = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var lossFiles = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in Files(input, shard, shards, limit, ext))
        {
            string name = Path.GetFileName(file);
            string outPath = Path.Combine(output, Path.GetFileNameWithoutExtension(file) + "-" + Path.GetExtension(file).TrimStart('.') + "." + target);
            string status = "ok", kind = "", format = "", error = "", code = "";
            string[] imp = [], exp = [], conv = [];
            int defects = 0;
            try
            {
                using OfficeFile f = Office.Open(file);
                opened++;
                kind = f.Kind.ToString();
                format = f.Format.ToString();
                defects = f.Defects.Count;
                f.SaveAs(outPath);
                saved++;
                switch (f)
                {
                    case Document d:
                        imp = Codes(d.ImportLoss);
                        exp = Codes(d.ExportLoss);
                        break;
                    case Workbook w:
                        imp = Codes(w.ImportLoss);
                        exp = Codes(w.ExportLoss);
                        break;
                    case Presentation p:
                        imp = Codes(p.ImportLoss);
                        exp = Codes(p.ExportLoss);
                        conv = Codes(p.ConversionLoss);
                        break;
                }
            }
            catch (OfficeException ex)
            {
                status = "typed-error";
                code = ex.Defect.Code;
                typed++;
            }
#pragma warning disable CA1031 // The point of the sweep is to find what is not typed.
            catch (Exception ex)
            {
                status = "UNTYPED-EXCEPTION";
                error = ex.GetType().Name + ": " + ex.Message + " @ " + string.Join(" <- ", (ex.StackTrace ?? string.Empty).Split('\n').Take(4).Select(static l => l.Trim()));
                untyped++;
            }
#pragma warning restore CA1031

            string[] all = [.. imp, .. exp, .. conv];
            if (status == "ok")
            {
                if (all.Length == 0)
                {
                    lossless++;
                }
                else
                {
                    lossy++;
                }
            }

            foreach (string item in all)
            {
                int c = item.LastIndexOf(':');
                string k = item[..c];
                loss[k] = loss.GetValueOrDefault(k) + long.Parse(item[(c + 1)..], CultureInfo.InvariantCulture);
                lossFiles[k] = lossFiles.GetValueOrDefault(k) + 1;
            }

            Console.WriteLine(JsonSerializer.Serialize(new { file = name, status, kind, format, code, error, defects, importLoss = imp, exportLoss = exp, conversionLoss = conv }));
        }

        Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", opened, saved, typed, untyped, lossless, lossy, lossItems = loss, lossFiles }));
        return 0;
    }

    // Office.ExtractText over every file of one extension in a directory, NAME.txt each; typed failures are printed as JSON lines.
    private static int TextOf(string dir, string ext, string outDir)
    {
        Directory.CreateDirectory(outDir);
        int ok = 0, failed = 0, untyped = 0;
        foreach (string path in Directory.EnumerateFiles(dir, "*." + ext, SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            try
            {
                File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".txt"), Office.ExtractText(path), new UTF8Encoding(false));
                ok++;
            }
            catch (OfficeException ex)
            {
                failed++;
                Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), code = ex.Defect.Code }));
            }
#pragma warning disable CA1031 // The point of the sweep is to find what is not typed.
            catch (Exception ex)
            {
                untyped++;
                Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), untyped = ex.GetType().Name + ": " + ex.Message }));
            }
#pragma warning restore CA1031
        }

        Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", ok, typedErrors = failed, untyped }));
        return 0;
    }

    private static string Sha(ReadOnlySpan<byte> b) => System.Convert.ToHexString(SHA256.HashData(b));

    private static int OdfRoundTrip(string[] args)
    {
        (int shard, int shards, int limit, _) = Options(args);
        string[] ext = ["odt", "ods", "odp", "ott", "ots", "otp", "fodt", "fods", "fodp"];
        int files = 0, rejected = 0, untyped = 0, entriesTotal = 0, entriesIdentical = 0, editOk = 0, editBad = 0, fileIdentical = 0;
        foreach (string file in Files(args[1], shard, shards, limit, ext))
        {
            string name = Path.GetFileName(file);
            files++;
            string status = "ok", detail = "";
            int total = 0, same = 0;
            try
            {
                using OdfPackage pkg = OdfPackage.Open(ByteSource.FromFile(file));
                var before = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (OdfEntry e in pkg.Entries)
                {
                    if (!e.IsDirectory)
                    {
                        before[e.Name] = Sha(pkg.ReadEntry(e.Name).Span);
                    }
                }

                using var ms = new MemoryStream();
                if (pkg.IsFlat)
                {
                    pkg.WriteFlatTo(ms);
                }
                else
                {
                    pkg.WriteTo(ms);
                }

                byte[] written = ms.ToArray();
                if (Sha(written) == Sha(File.ReadAllBytes(file)))
                {
                    fileIdentical++;
                }

                if (!pkg.IsFlat)
                {
                    using OdfPackage again = OdfPackage.Open(ByteSource.FromMemory(written));
                    foreach ((string n, string h) in before)
                    {
                        total++;
                        if (again.TryReadEntry(n, out ReadOnlyMemory<byte> b) && Sha(b.Span) == h)
                        {
                            same++;
                        }
                        else
                        {
                            detail += " differs:" + n;
                        }
                    }

                    // An edit to one entry must leave every other entry's bytes alone.
                    using OdfPackage edit = OdfPackage.Open(ByteSource.FromMemory(written));
                    edit.SetEntry("gate-probe.txt", Encoding.UTF8.GetBytes("edited"), "text/plain");
                    using var ms2 = new MemoryStream();
                    edit.WriteTo(ms2);
                    using OdfPackage after = OdfPackage.Open(ByteSource.FromMemory(ms2.ToArray()));
                    bool ok = after.Contains("gate-probe.txt");
                    foreach ((string n, string h) in before)
                    {
                        if (!after.TryReadEntry(n, out ReadOnlyMemory<byte> b) || (Sha(b.Span) != h && n != "META-INF/manifest.xml"))
                        {
                            ok = false;
                            detail += " edit-changed:" + n;
                        }
                    }

                    if (ok)
                    {
                        editOk++;
                    }
                    else
                    {
                        editBad++;
                    }
                }
                else
                {
                    total = 1;
                    same = Sha(written) == Sha(File.ReadAllBytes(file)) ? 1 : 0;
                }
            }
            catch (OfficeException ex)
            {
                status = "typed-error";
                detail = ex.Defect.Code;
                rejected++;
            }
#pragma warning disable CA1031 // The point of the sweep is to find what is not typed.
            catch (Exception ex)
            {
                status = "UNTYPED-EXCEPTION";
                detail = ex.GetType().Name + ": " + ex.Message;
                untyped++;
            }
#pragma warning restore CA1031

            entriesTotal += total;
            entriesIdentical += same;
            Console.WriteLine(JsonSerializer.Serialize(new { file = name, status, entries = total, identical = same, detail }));
        }

        Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", files, rejected, untyped, entries = entriesTotal, entriesIdentical, fileIdentical, editOk, editBad }));
        return 0;
    }
}
