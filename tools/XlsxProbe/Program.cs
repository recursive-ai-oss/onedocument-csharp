using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.Packaging;
using RecursiveAI.OneOffice.Spreadsheets;

namespace RecursiveAI.OneOffice.Tools.XlsxProbe;

/// <summary>
/// Checks the workbook layer over real files and prints JSON lines.
/// <c>XlsxProbe sweep DIR OUT [--max N] [--shard I/N]</c>: for each file, opens it and saves it untouched (per-part comparison), forces every
/// worksheet through the write path and compares the model read back, makes one single-cell edit and lists the parts that changed.
/// <c>XlsxProbe lazy FILE</c>: bytes read from the file by opening it, touching one sheet and reading one string.
/// <c>XlsxProbe load FILE</c>: loads every sheet, prints cells, managed bytes per cell and timings (indicative).
/// <c>XlsxProbe model FILE</c>: prints a canonical dump of every cell for comparison with another reader.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: XlsxProbe sweep|lazy|load|model ARGS");
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "sweep" => Sweep(args[1..]),
                "lazy" => Lazy(args[1]),
                "load" => Load(args[1]),
                "model" => Model(args[1]),
                "features" => FeatureSweep.Run(args[1..]),
                "fmtgrid" => FormatGrid.Run(args[1..]),
                "fmtgrid-compare" => FormatGrid.Compare(args[1]),
                _ => 2,
            };
        }
        catch (OfficeException ex)
        {
            Console.Error.WriteLine(ex.Defect.Code + ": " + ex.Message);
            return 1;
        }
    }

    private static string Sha(ReadOnlySpan<byte> b) => Convert.ToHexString(SHA256.HashData(b));

    private sealed class CountingSource(IByteSource inner) : IByteSource
    {
        public long BytesRead;
        public long Length => inner.Length;
        public bool CanReadSync => inner.CanReadSync;

        public int Read(long offset, Span<byte> destination)
        {
            int n = inner.Read(offset, destination);
            BytesRead += n;
            return n;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default) => inner.ReadAsync(offset, destination, cancellationToken);

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
        {
            memory = default;
            return false;
        }

        public void Dispose() => inner.Dispose();
    }

    private static int Lazy(string path)
    {
        using var src = new CountingSource(ByteSource.FromFile(path));
        using Workbook wb = Workbook.Open(src);
        long afterOpen = src.BytesRead;
        Worksheet first = wb.Sheets[0];
        _ = first.Store.CellCount;
        long afterSheet = src.BytesRead;
        long afterSst = afterSheet;
        foreach (CellRowView row in first.Store.EnumerateRows())
        {
            bool found = false;
            for (int i = 0; i < row.Count && !found; i++)
            {
                if (row.ValueAt(i).Kind == CellValueKind.Text)
                {
                    _ = first.GetText(row.ValueAt(i));
                    found = true;
                }
            }

            if (found)
            {
                break;
            }
        }

        afterSst = src.BytesRead;
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(path), ["fileBytes"] = src.Length, ["afterOpen"] = afterOpen, ["afterFirstSheet"] = afterSheet, ["afterFirstString"] = afterSst,
            ["sheets"] = wb.Sheets.Count,
        }));
        return 0;
    }

    private static int Load(string path)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetTotalMemory(true);
        long allocBefore = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();
        using Workbook wb = Workbook.Open(path);
        long cells = 0;
        foreach (Worksheet s in wb.Sheets)
        {
            if (s.Kind == SheetKind.Worksheet)
            {
                cells += s.Store.CellCount;
            }
        }

        double loadMs = sw.Elapsed.TotalMilliseconds;
        long allocLoad = GC.GetTotalAllocatedBytes(true) - allocBefore;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long after = GC.GetTotalMemory(true);
        long modelBytes = 0;
        foreach (Worksheet s in wb.Sheets)
        {
            if (s.Kind == SheetKind.Worksheet)
            {
                modelBytes += s.Store.ApproximateBytes();
            }
        }

        sw.Restart();
        wb.Sheets[0].Rewrite();
        string tmp = Path.Combine(Path.GetTempPath(), "xlsxprobe-" + Environment.ProcessId + ".xlsx");
        wb.SaveAs(tmp, OfficeFormat.Xlsx);
        double saveMs = sw.Elapsed.TotalMilliseconds;
        long outBytes = new FileInfo(tmp).Length;
        File.Delete(tmp);
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(path), ["cells"] = cells, ["managedBytes"] = after - before, ["bytesPerCell"] = cells == 0 ? 0 : Math.Round((double)(after - before) / cells, 2),
            ["storeBytesEstimate"] = modelBytes, ["loadMs"] = Math.Round(loadMs), ["allocatedDuringLoad"] = allocLoad, ["saveMs"] = Math.Round(saveMs), ["fileBytes"] = new FileInfo(path).Length, ["outBytes"] = outBytes,
            ["peakWorkingSetMB"] = Process.GetCurrentProcess().PeakWorkingSet64 >> 20,
        }));
        GC.KeepAlive(wb);
        return 0;
    }

    private static int Model(string path)
    {
        using Workbook wb = Workbook.Open(path);
        var o = Console.Out;
        foreach (Worksheet s in wb.Sheets)
        {
            if (s.Kind != SheetKind.Worksheet)
            {
                o.WriteLine("#" + s.Name + " (not a worksheet)");
                continue;
            }

            o.WriteLine("#" + s.Name);
            foreach (CellRowView row in s.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    CellValue v = row.ValueAt(i);
                    o.WriteLine(new CellAddress(row.Row, row.ColumnAt(i)) + "\t" + v.Kind + "\t" + (v.Kind == CellValueKind.Text ? s.GetText(v) : v.Kind == CellValueKind.Number ? v.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : v.ToString()) + "\t" + row.StyleAt(i) + "\t" + row.FormulaAt(i)?.Text);
                }
            }
        }

        return 0;
    }

    private static int Sweep(string[] args)
    {
        string dir = args[0];
        string outDir = args[1];
        int max = int.MaxValue;
        int shard = 0, shards = 1;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--max")
            {
                max = int.Parse(args[++i]);
            }
            else if (args[i] == "--shard")
            {
                string[] p = args[++i].Split('/');
                shard = int.Parse(p[0]);
                shards = int.Parse(p[1]);
            }
        }

        Directory.CreateDirectory(outDir);
        string[] files = [.. Directory.EnumerateFiles(dir, "*.xlsx", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        int n = 0;
        var summary = new Dictionary<string, long>();
        var lines = new List<string>();
        for (int fi = shard; fi < files.Length && n < max; fi += shards)
        {
            n++;
            Dictionary<string, object?> r = CheckFile(files[fi], dir, outDir);
            lines.Add(JsonSerializer.Serialize(r));
            foreach (string k in new[] { "status" })
            {
                string key = k + ":" + r[k];
                summary[key] = summary.GetValueOrDefault(key) + 1;
            }
        }

        File.WriteAllLines(Path.Combine(outDir, $"sweep-{shard}.jsonl"), lines);
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["kind"] = "summary", ["shard"] = shard, ["files"] = n, ["statuses"] = summary }));
        return 0;
    }

    private static Dictionary<string, object?> CheckFile(string path, string root, string outDir)
    {
        string rel = Path.GetRelativePath(root, path);
        string id = Sha(Encoding.UTF8.GetBytes(rel))[..16];
        var r = new Dictionary<string, object?> { ["kind"] = "file", ["file"] = rel, ["id"] = id, ["size"] = new FileInfo(path).Length };
        var ctx = new OpenContext();
        try
        {
            using Workbook wb = Workbook.Open(path, ctx);
            r["sheets"] = wb.Sheets.Count;
            r["format"] = wb.Format.ToString();

            // 1. Untouched save: every part byte-identical.
            string untouched = Path.Combine(outDir, "untouched", id + ".xlsx");
            Directory.CreateDirectory(Path.GetDirectoryName(untouched)!);
            using (var fs = File.Create(untouched))
            {
                wb.SaveAs(fs, wb.Format);
            }

            (int same, int diff, string? firstDiff) = ComparePackages(path, untouched);
            r["untouchedParts"] = same + diff;
            r["untouchedDiff"] = diff;
            if (firstDiff is not null)
            {
                r["untouchedFirstDiff"] = firstDiff;
            }

            // 2. Force every worksheet through the write path; the model read back must equal the model read.
            long cells = 0;
            foreach (Worksheet s in wb.Sheets)
            {
                if (s.Kind == SheetKind.Worksheet)
                {
                    s.Rewrite();
                    cells += s.Store.CellCount;
                }
            }

            r["cells"] = cells;
            string edited = Path.Combine(outDir, "edited", id + ".xlsx");
            Directory.CreateDirectory(Path.GetDirectoryName(edited)!);
            using (var fs = File.Create(edited))
            {
                wb.SaveAs(fs, wb.Format);
            }

            using Workbook back = Workbook.Open(edited, new OpenContext());
            var mism = new List<string>();
            if (back.Sheets.Count != wb.Sheets.Count)
            {
                mism.Add("sheet count");
            }
            else
            {
                for (int i = 0; i < wb.Sheets.Count; i++)
                {
                    if (wb.Sheets[i].Kind == SheetKind.Worksheet)
                    {
                        CompareSheets(wb.Sheets[i], back.Sheets[i], mism);
                    }
                }
            }

            r["modelMismatches"] = mism.Count;
            if (mism.Count > 0)
            {
                r["firstMismatch"] = mism[0];
            }

            // 3. A single-cell edit changes only that sheet's part (and what must follow).
            using Workbook one = Workbook.Open(path, new OpenContext());
            Worksheet? target = null;
            foreach (Worksheet s in one.Sheets)
            {
                if (s.Kind == SheetKind.Worksheet && s.PartName is not null)
                {
                    target = s;
                    break;
                }
            }

            if (target is not null)
            {
                target["XFD1048576"].SetNumber(1);
                string single = Path.Combine(outDir, "single", id + ".xlsx");
                Directory.CreateDirectory(Path.GetDirectoryName(single)!);
                using (var fs = File.Create(single))
                {
                    one.SaveAs(fs, one.Format);
                }

                r["singleEditChanged"] = ChangedParts(path, single);
                File.Delete(single);
            }

            r["defects"] = ((DefectCollection)ctx.Defects).Count;
            var codes = new Dictionary<string, int>();
            foreach (Defect d in (DefectCollection)ctx.Defects)
            {
                codes[d.Code] = codes.GetValueOrDefault(d.Code) + 1;
            }

            r["defectCodes"] = codes;
            r["status"] = diff == 0 && mism.Count == 0 ? "ok" : diff != 0 ? "untouched-diff" : "model-mismatch";
        }
        catch (OfficeException ex)
        {
            r["status"] = "typed-error";
            r["errorClass"] = ex.GetType().Name;
            r["errorCode"] = ex.Defect.Code;
            r["errorLocation"] = ex.Location.ToString();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            r["status"] = "UNTYPED-EXCEPTION";
            r["errorClass"] = ex.GetType().Name;
            r["error"] = ex.Message;
            r["stack"] = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
        }

        return r;
    }

    internal static (int Same, int Diff, string? First) ComparePackages(string a, string b)
    {
        using OpcPackage pa = OpcPackage.Open(a);
        using OpcPackage pb = OpcPackage.Open(b);
        int same = 0, diff = 0;
        string? first = null;
        foreach (IPackagePart part in pa.Parts)
        {
            IPackagePart? other = pb.FindPart(part.Name);
            if (other is null || !part.GetBytes().Span.SequenceEqual(other.GetBytes().Span) || part.ContentType != other.ContentType)
            {
                diff++;
                first ??= part.Name;
            }
            else
            {
                same++;
            }
        }

        if (pa.Parts.Count != pb.Parts.Count)
        {
            diff++;
            first ??= "part count";
        }

        return (same, diff, first);
    }

    internal static string[] ChangedParts(string a, string b)
    {
        using OpcPackage pa = OpcPackage.Open(a);
        using OpcPackage pb = OpcPackage.Open(b);
        var changed = new List<string>();
        foreach (IPackagePart part in pa.Parts)
        {
            IPackagePart? other = pb.FindPart(part.Name);
            if (other is null)
            {
                changed.Add("-" + part.Name);
            }
            else if (!part.GetBytes().Span.SequenceEqual(other.GetBytes().Span))
            {
                changed.Add(part.Name);
            }
        }

        foreach (IPackagePart part in pb.Parts)
        {
            if (pa.FindPart(part.Name) is null)
            {
                changed.Add("+" + part.Name);
            }
        }

        return [.. changed];
    }

    private static void CompareSheets(Worksheet a, Worksheet b, List<string> mism)
    {
        if (a.Name != b.Name)
        {
            mism.Add("sheet name");
        }

        CellStore sa = a.Store;
        CellStore sb = b.Store;
        if (sa.CellCount != sb.CellCount)
        {
            mism.Add(a.Name + ": cell count " + sa.CellCount + " vs " + sb.CellCount);
        }

        foreach (CellRowView row in sa.EnumerateRows())
        {
            RowProperties pa = a.GetRow(row.Row);
            RowProperties pb = b.GetRow(row.Row);
            if (pa.Height != pb.Height || pa.Hidden != pb.Hidden || pa.OutlineLevel != pb.OutlineLevel || pa.StyleIndex != pb.StyleIndex || pa.Collapsed != pb.Collapsed)
            {
                mism.Add(a.Name + ": row " + row.Row + " properties");
            }

            for (int i = 0; i < row.Count; i++)
            {
                int col = row.ColumnAt(i);
                CellValue va = row.ValueAt(i);
                if (!sb.TryGetCell(row.Row, col, out CellValue vb, out int styleB))
                {
                    mism.Add(a.Name + "!" + new CellAddress(row.Row, col) + " missing");
                    continue;
                }

                bool same = va.Kind == vb.Kind && va.Kind switch
                {
                    CellValueKind.Text => a.GetText(va) == b.GetText(vb),
                    CellValueKind.Number => va.Number.Equals(vb.Number),
                    CellValueKind.Boolean => va.Boolean == vb.Boolean,
                    CellValueKind.Error => va.Error == vb.Error && a.GetText(va) == b.GetText(vb),
                    _ => true,
                };
                if (!same)
                {
                    mism.Add(a.Name + "!" + new CellAddress(row.Row, col) + " value " + va.Kind + "/" + vb.Kind);
                }

                if (row.StyleAt(i) != styleB)
                {
                    mism.Add(a.Name + "!" + new CellAddress(row.Row, col) + " style");
                }

                if (!Equals(row.FormulaAt(i), sb.GetFormula(row.Row, col)))
                {
                    mism.Add(a.Name + "!" + new CellAddress(row.Row, col) + " formula");
                }
            }
        }

        if (!a.MergedCells.SequenceEqual(b.MergedCells))
        {
            mism.Add(a.Name + ": merged cells");
        }

        if (!a.Columns.SequenceEqual(b.Columns))
        {
            mism.Add(a.Name + ": columns");
        }
    }
}
