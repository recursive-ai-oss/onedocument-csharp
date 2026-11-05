using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Streaming;

namespace RecursiveAI.OneOffice.Tools.StreamProbe;

/// <summary>
/// Checks the streaming cursors over real files and prints JSON lines.
/// <c>StreamProbe compare DIR OUT [--max N] [--shard I/N]</c>: for every workbook under DIR, reads each worksheet with the cursor and with the workbook model and compares every cell.
/// <c>StreamProbe read FILE [SHEET]</c>: streams one sheet, printing working set at 10k/100k/1M rows, bytes allocated per cell and throughput.
/// <c>StreamProbe write OUT ROWS COLS [--inline] [--seed N]</c>: writes a generated workbook with the streaming writer, printing working set at checkpoints.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: StreamProbe compare|read|write ARGS");
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "compare" => Compare(args[1..]),
                "read" => Read(args[1], args.Length > 2 ? args[2] : null),
                "write" => Write(args[1..]),
                "xcompare" => XCompare(args[1], args[2]),
                _ => 2,
            };
        }
        catch (OfficeException ex)
        {
            Console.Error.WriteLine(ex.Defect.Code + ": " + ex.Message);
            return 1;
        }
    }

    private static void Json(Dictionary<string, object?> o) => Console.WriteLine(JsonSerializer.Serialize(o));

    // ------------------------------------------------------------------ compare

    private static int Compare(string[] a)
    {
        string dir = a[0];
        string outPath = a[1];
        int max = int.MaxValue;
        int shard = 0, shards = 1;
        for (int i = 2; i < a.Length; i++)
        {
            if (a[i] == "--max")
            {
                max = int.Parse(a[++i]);
            }
            else if (a[i] == "--shard")
            {
                string[] p = a[++i].Split('/');
                shard = int.Parse(p[0]);
                shards = int.Parse(p[1]);
            }
        }

        string[] files = [.. Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xltx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)];
        using var output = new StreamWriter(outPath);
        int done = 0, ok = 0, rejected = 0, mismatchFiles = 0, unordered = 0;
        long cells = 0, mismatches = 0;
        for (int i = shard; i < files.Length && done < max; i += shards)
        {
            string f = files[i];
            done++;
            var rec = new Dictionary<string, object?> { ["file"] = Path.GetFileName(f) };
            try
            {
                (long c, long m, bool unord, string? first) = CompareFile(f);
                rec["cells"] = c;
                rec["mismatches"] = m;
                rec["unordered"] = unord;
                if (first is not null)
                {
                    rec["first"] = first;
                }

                cells += c;
                mismatches += m;
                ok++;
                if (m > 0)
                {
                    mismatchFiles++;
                }

                if (unord)
                {
                    unordered++;
                }
            }
            catch (OfficeException ex)
            {
                rec["rejected"] = ex.Defect.Code;
                rejected++;
            }

            output.WriteLine(JsonSerializer.Serialize(rec));
        }

        Json(new Dictionary<string, object?> { ["summary"] = true, ["files"] = done, ["compared"] = ok, ["typedRejections"] = rejected, ["cells"] = cells, ["mismatchedCells"] = mismatches, ["filesWithMismatch"] = mismatchFiles, ["filesWithOrderDefects"] = unordered });
        return mismatchFiles == 0 ? 0 : 1;
    }

    private static (long Cells, long Mismatches, bool Unordered, string? First) CompareFile(string path)
    {
        var sctx = new OpenContext();
        using Workbook wb = Workbook.Open(path);
        using XlsxStreamReader r = XlsxStreamReader.Open(path, sctx);
        long cells = 0, bad = 0;
        string? first = null;
        for (int si = 0; si < r.Sheets.Count; si++)
        {
            if (r.Sheets[si].Kind != SheetKind.Worksheet)
            {
                continue;
            }

            Worksheet ws = wb.Sheets[si];
            using SheetCursor c = r.OpenSheet(si);
            CellStore store = ws.Store;
            var rows = store.EnumerateRows().GetEnumerator();
            while (c.ReadRow())
            {
                if (!rows.MoveNext())
                {
                    bad++;
                    first ??= ws.Name + ": the cursor has more rows than the model at row " + c.RowNumber;
                    break;
                }

                CellRowView hub = rows.Current;
                if (hub.Row != c.RowNumber)
                {
                    // Rows with only properties are in the model too; both lists are sorted, so a row the model lacks means a count difference.
                    bad++;
                    first ??= ws.Name + ": row " + c.RowNumber + " vs model row " + hub.Row;
                    continue;
                }

                int i = 0;
                while (c.ReadCell())
                {
                    cells++;
                    if (i >= hub.Count || !SameCell(ws, hub, i, c, r))
                    {
                        bad++;
                        first ??= ws.Name + "!" + c.Address;
                    }

                    i++;
                }

                if (i != hub.Count)
                {
                    bad++;
                    first ??= ws.Name + ": row " + c.RowNumber + " has " + i + " cells, the model " + hub.Count;
                }
            }

            if (rows.MoveNext())
            {
                bad++;
                first ??= ws.Name + ": the model has more rows than the cursor";
            }
        }

        bool unordered = ((DefectCollection)sctx.Defects).Any(d => d.Code is "xlsx.stream-row-order" or "xlsx.stream-cell-order");
        return (cells, unordered ? 0 : bad, unordered, unordered ? "order defect, not compared" : first);
    }

    private static bool SameCell(Worksheet ws, CellRowView hub, int i, SheetCursor c, XlsxStreamReader r)
    {
        _ = r;
        if (hub.ColumnAt(i) != c.Column || hub.StyleAt(i) != c.Style)
        {
            return false;
        }

        CellValue v = hub.ValueAt(i);
        if (v.Kind != c.Kind)
        {
            return false;
        }

        switch (v.Kind)
        {
            case CellValueKind.Number or CellValueKind.Boolean:
                if (BitConverter.DoubleToInt64Bits(v.Number) != BitConverter.DoubleToInt64Bits(c.Number))
                {
                    return false;
                }

                break;
            case CellValueKind.Error:
                if (v.Error != c.Error)
                {
                    return false;
                }

                break;
            case CellValueKind.Text:
                if (ws.GetText(v) != c.GetText())
                {
                    return false;
                }

                break;
        }

        CellFormula? f = hub.FormulaAt(i);
        if ((f is not null) != c.HasFormula)
        {
            return false;
        }

        return f is null || (f.Text == c.GetFormula() && f.Kind == c.FormulaKind && (f.Kind != CellFormulaKind.Shared || f.SharedIndex == c.SharedFormulaIndex));
    }

    // ------------------------------------------------------------------ read

    private static int Read(string path, string? sheet)
    {
        using XlsxStreamReader r = XlsxStreamReader.Open(path);
        int index = 0;
        if (sheet is not null)
        {
            index = r.Sheets.First(s => s.Name == sheet).Index;
        }

        using SheetCursor c = r.OpenSheet(index);
        _ = r.SharedStringCount;
        Span<char> buf = new char[4096];
        GC.Collect();
        long baseWs = Process.GetCurrentProcess().WorkingSet64;
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        long rows = 0, cells = 0, chars = 0;
        double sum = 0;
        long nextCheck = 10_000;
        var points = new List<Dictionary<string, object?>>();
        while (c.ReadRow())
        {
            rows++;
            while (c.ReadCell())
            {
                cells++;
                switch (c.Kind)
                {
                    case CellValueKind.Number:
                        sum += c.Number;
                        break;
                    case CellValueKind.Text:
                        if (c.TryCopyText(buf, out int n))
                        {
                            chars += n;
                        }

                        break;
                }
            }

            if (rows == nextCheck)
            {
                Process p = Process.GetCurrentProcess();
                points.Add(new Dictionary<string, object?> { ["rows"] = rows, ["workingSetMB"] = p.WorkingSet64 >> 20, ["peakWorkingSetMB"] = p.PeakWorkingSet64 >> 20, ["gcHeapMB"] = GC.GetTotalMemory(false) >> 20 });
                nextCheck *= 10;
            }
        }

        double secs = sw.Elapsed.TotalSeconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - a0;
        Process pe = Process.GetCurrentProcess();
        Json(new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(path), ["fileMB"] = new FileInfo(path).Length >> 20, ["rows"] = rows, ["cells"] = cells, ["seconds"] = Math.Round(secs, 3),
            ["cellsPerSecond"] = (long)(cells / secs), ["allocatedBytes"] = allocated, ["bytesPerCell"] = cells == 0 ? 0 : Math.Round((double)allocated / cells, 4),
            ["baselineWorkingSetMB"] = baseWs >> 20, ["peakWorkingSetMB"] = pe.PeakWorkingSet64 >> 20, ["checkpoints"] = points, ["sum"] = sum, ["chars"] = chars,
        });
        return 0;
    }

    // ------------------------------------------------------------------ xcompare

    // Compares the values of two workbooks sheet by sheet with two cursors (for example a file and what another application made of it).
    private static int XCompare(string pathA, string pathB)
    {
        using XlsxStreamReader ra = XlsxStreamReader.Open(pathA);
        using XlsxStreamReader rb = XlsxStreamReader.Open(pathB);
        long cells = 0, bad = 0, onlyA = 0, onlyB = 0;
        string? first = null;
        int sheets = Math.Min(ra.Sheets.Count, rb.Sheets.Count);
        for (int si = 0; si < sheets; si++)
        {
            using SheetCursor a = ra.OpenSheet(si);
            using SheetCursor b = rb.OpenSheet(si);
            bool haveA = Next(a);
            bool haveB = Next(b);
            while (haveA || haveB)
            {
                int cmp = !haveA ? 1 : !haveB ? -1 : a.RowNumber != b.RowNumber ? a.RowNumber.CompareTo(b.RowNumber) : a.Column.CompareTo(b.Column);
                if (cmp < 0)
                {
                    if (a.Kind != CellValueKind.Empty)
                    {
                        onlyA++;
                        first ??= "only in A: " + a.Address;
                    }

                    haveA = Next(a);
                }
                else if (cmp > 0)
                {
                    if (b.Kind != CellValueKind.Empty)
                    {
                        onlyB++;
                        first ??= "only in B: " + b.Address;
                    }

                    haveB = Next(b);
                }
                else
                {
                    cells++;
                    if (!SameValue(a, b))
                    {
                        bad++;
                        first ??= "sheet " + si + " " + a.Address + ": " + Describe(a) + " vs " + Describe(b);
                    }

                    haveA = Next(a);
                    haveB = Next(b);
                }
            }
        }

        Json(new Dictionary<string, object?> { ["sheets"] = sheets, ["cellsCompared"] = cells, ["different"] = bad, ["onlyInA"] = onlyA, ["onlyInB"] = onlyB, ["first"] = first });
        return bad + onlyA + onlyB == 0 ? 0 : 1;
    }

    private static bool Next(SheetCursor c)
    {
        while (true)
        {
            if (c.ReadCell())
            {
                return true;
            }

            if (!c.ReadRow())
            {
                return false;
            }
        }
    }

    private static bool SameValue(SheetCursor a, SheetCursor b)
    {
        if (a.Kind != b.Kind)
        {
            return false;
        }

        switch (a.Kind)
        {
            case CellValueKind.Number:
                return a.Number == b.Number || Math.Abs(a.Number - b.Number) <= 1e-12 * Math.Max(Math.Abs(a.Number), Math.Abs(b.Number));
            case CellValueKind.Boolean:
                return a.Boolean == b.Boolean;
            case CellValueKind.Error:
                return a.Error == b.Error;
            case CellValueKind.Text:
                return a.TextUtf8.SequenceEqual(b.TextUtf8) || a.GetText() == b.GetText();
            default:
                return true;
        }
    }

    private static string Describe(SheetCursor c) => c.Kind + ":" + (c.Kind == CellValueKind.Text ? c.GetText() : c.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

    // ------------------------------------------------------------------ write

    private static int Write(string[] a)
    {
        string path = a[0];
        int rows = int.Parse(a[1]);
        int cols = int.Parse(a[2]);
        bool inline = a.Contains("--inline");
        int sheets = a.Contains("--sheets") ? int.Parse(a[Array.IndexOf(a, "--sheets") + 1]) : 1;
        var sw = Stopwatch.StartNew();
        var points = new List<Dictionary<string, object?>>();
        long nextCheck = 10_000;
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(path, new XlsxWriteOptions { StringStorage = inline ? StreamStringStorage.Inline : StreamStringStorage.Shared }))
        {
            int bold = w.AddFormat(new StreamCellFormat { Bold = true });
            int money = w.AddFormat(new StreamCellFormat { NumberFormat = "#,##0.00" });
            int date = w.AddFormat(StreamCellFormat.Date);
            Span<char> f = stackalloc char[16];
            for (int sheetNo = 1; sheetNo <= sheets; sheetNo++)
            {
            SheetStreamWriter s = w.AddSheet("Data" + sheetNo, new SheetWriteOptions { FreezeRows = 1, Columns = [new ColumnProperties(1, cols, 14, 0, ColumnOptions.None)] });
            s.BeginRow();
            for (int c = 1; c <= cols; c++)
            {
                s.WriteText("Column " + c, bold);
            }

            string[] words = ["alpha", "beta", "gamma", "delta & co", "epsilon <x>", "zeta", "eta_x0041_", "theta"];
            var d0 = new DateTime(2000, 1, 1);
            for (int r = 2; r <= rows; r++)
            {
                s.BeginRow();
                for (int c = 1; c <= cols; c++)
                {
                    switch (c % 5)
                    {
                        case 0:
                            s.WriteNumber(((r * 31) + c) * 0.37, money);
                            break;
                        case 1:
                            s.WriteNumber(r + c);
                            break;
                        case 2:
                            s.WriteText(words[(r + c) & 7]);
                            break;
                        case 3:
                            s.WriteDate(d0.AddDays(r % 9000), date);
                            break;
                        default:
                            if (r % 5 == 0)
                            {
                                f[0] = 'A';
                                r.TryFormat(f[1..], out int fl);
                                "+1".CopyTo(f[(1 + fl)..]);
                                s.WriteFormulaNumber(f[..(fl + 3)], r + 2);
                            }
                            else
                            {
                                s.WriteBoolean((r & 1) == 0);
                            }

                            break;
                    }
                }

                if (r == nextCheck || (sheetNo > 1 && r == rows))
                {
                    Process p = Process.GetCurrentProcess();
                    points.Add(new Dictionary<string, object?> { ["sheet"] = sheetNo, ["rows"] = r, ["workingSetMB"] = p.WorkingSet64 >> 20, ["peakWorkingSetMB"] = p.PeakWorkingSet64 >> 20 });
                    nextCheck = nextCheck == r ? nextCheck * 10 : nextCheck;
                }
            }
            }
        }

        double secs = sw.Elapsed.TotalSeconds;
        Process pe = Process.GetCurrentProcess();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        long cells = (long)rows * cols * sheets;
        Json(new Dictionary<string, object?>
        {
            ["out"] = Path.GetFileName(path), ["rows"] = rows, ["cols"] = cols, ["cells"] = cells, ["fileMB"] = new FileInfo(path).Length >> 20, ["seconds"] = Math.Round(secs, 3),
            ["cellsPerSecond"] = (long)(cells / secs), ["allocatedBytes"] = alloc, ["bytesPerCell"] = Math.Round((double)alloc / cells, 4), ["peakWorkingSetMB"] = pe.PeakWorkingSet64 >> 20, ["checkpoints"] = points,
        });
        return 0;
    }
}
