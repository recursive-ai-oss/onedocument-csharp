using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Bench;

/// <summary>
/// The spreadsheet half of a results page. Reads the TSV files the <c>xlsxstream all</c> and <c>bpcall</c> commands write
/// (<c>xlsx-*.tsv</c>, <c>bpc.tsv</c>) and appends the per-size tables, the bar table (OneDocument against the fastest managed
/// comparator of the same operation and size) and the bytes-per-cell table.
/// </summary>
internal static class XlsxReport
{
    private sealed record Line(string Row, string Size, int Reps, double MinMs, double MedianMs, double Alloc, double PeakMb, string Checksum);

    // (title, our gate row, comparator rows, informational rows of ours)
    private static readonly (string Title, string Ours, string[] Others, string[] Info)[] Groups =
    [
        ("Streaming read, typed values (every cell)", "onedocument.read.string", ["sylvan.read", "exceldatareader.read", "miniexcel.read"], ["onedocument.read.span"]),
        ("Streaming write", "onedocument.write.shared", ["miniexcel.write", "openxml.write"], ["onedocument.write.inline"]),
        ("Full model: open and visit every cell", "onedocument.model.load", ["closedxml.model.load", "npoi.model.load", "epplus.model.load"], []),
        ("Full model: open, change one cell, save", "onedocument.model.editsave", ["closedxml.model.editsave", "npoi.model.editsave", "epplus.model.editsave"], ["onedocument.model.loadsave-untouched"]),
    ];

    /// <summary>Appends the spreadsheet tables for every <c>xlsx-*.tsv</c> in <paramref name="dir"/>; returns whether anything was written.</summary>
    public static bool Append(StringBuilder md, string dir)
    {
        var lines = new List<Line>();
        foreach (string file in Directory.EnumerateFiles(dir, "xlsx-*.tsv").Order(StringComparer.Ordinal))
        {
            foreach (string l in File.ReadLines(file).Skip(1))
            {
                string[] f = l.Split('\t');
                if (f.Length >= 8 && int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int reps)
                    && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double min)
                    && double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double med)
                    && double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double alloc)
                    && double.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double peak))
                {
                    lines.Add(new Line(f[0], f[1], reps, min, med, alloc, peak, f[7]));
                }
                else
                {
                    md.Append(CultureInfo.InvariantCulture, $"Failed row: `{l}`\n\n");
                }
            }
        }

        if (lines.Count == 0)
        {
            return false;
        }

        var bar = new List<(string Group, string Size, double Ours, string Best, double BestMedian)>();
        foreach ((string title, string ours, string[] others, string[] info) in Groups)
        {
            var inGroup = lines.Where(l => l.Row == ours || others.Contains(l.Row) || info.Contains(l.Row)).ToList();
            if (inGroup.Count == 0)
            {
                continue;
            }

            md.Append("## ").Append(title).Append("\n\n");
            md.Append("| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |\n|---|---|---:|---:|---:|---:|---:|---:|---|\n");
            foreach (IGrouping<string, Line> g in inGroup.GroupBy(l => l.Size).OrderBy(g => SizeKey(g.Key)))
            {
                Line? us = g.FirstOrDefault(l => l.Row == ours);
                double cells = Cells(g.Key);
                foreach (Line l in g.OrderBy(l => l.Row == ours ? 0 : info.Contains(l.Row) ? 1 : 2).ThenBy(l => l.Row, StringComparer.Ordinal))
                {
                    string ratio = us is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{l.MedianMs / us.MedianMs:F2}x");
                    md.Append(CultureInfo.InvariantCulture, $"| {g.Key} | {l.Row} | {l.Reps} | {Ms(l.MedianMs)} | {Ms(l.MinMs)} | {ratio} | {l.Alloc / cells:F2} B | {l.PeakMb:F0} MB | {l.Checksum} |\n");
                }

                var rivals = g.Where(l => others.Contains(l.Row)).ToList();
                if (us is not null && rivals.Count > 0)
                {
                    Line best = rivals.MinBy(l => l.MedianMs)!;
                    bar.Add((title, g.Key, us.MedianMs, best.Row, best.MedianMs));
                }
            }

            md.Append('\n');
        }

        if (bar.Count > 0)
        {
            md.Append("## Bar: OneDocument against the fastest managed comparator\n\n");
            md.Append("Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).\n\n");
            md.Append("| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |\n|---|---|---:|---|---:|---:|\n");
            foreach ((string group, string size, double o, string best, double bm) in bar)
            {
                md.Append(CultureInfo.InvariantCulture, $"| {group} | {size} | {Ms(o)} | {best} | {Ms(bm)} | {o / bm:F2}x |\n");
            }

            double geomean = Math.Exp(bar.Average(r => Math.Log(r.Ours / r.BestMedian)));
            var worst = bar.MaxBy(r => r.Ours / r.BestMedian);
            md.Append(CultureInfo.InvariantCulture, $"\nGeometric mean of the ratios: {geomean:F3}x over {bar.Count} rows. Worst row: {worst.Group}, {worst.Size} at {worst.Ours / worst.BestMedian:F2}x.\n\n");
        }

        return true;
    }

    /// <summary>Appends the bytes-per-cell table from <c>bpc.tsv</c> when present.</summary>
    public static void AppendFootprint(StringBuilder md, string dir)
    {
        string path = Path.Combine(dir, "bpc.tsv");
        if (!File.Exists(path))
        {
            return;
        }

        md.Append("## Memory per cell, full model (fresh process per library and input)\n\n");
        md.Append("Retained is the managed heap still reachable after the file is loaded, every cell visited and two collections, divided by the cell count; peak is the process high-water mark.\n\n");
        md.Append("| Input | Cells | Library | Retained bytes/cell | Allocated bytes/cell | Load | Peak working set |\n|---|---:|---|---:|---:|---:|---:|\n");
        foreach (string l in File.ReadLines(path).Skip(1))
        {
            string[] f = l.Split('\t');
            if (f.Length >= 8 && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cells) && cells > 0
                && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double retained)
                && double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double alloc))
            {
                md.Append(CultureInfo.InvariantCulture, $"| {f[1]} | {cells:N0} | {f[0]} | {retained / cells:F1} | {alloc / cells:F0} | {Ms(double.Parse(f[6], CultureInfo.InvariantCulture))} | {f[7]} MB |\n");
            }
            else
            {
                md.Append(CultureInfo.InvariantCulture, $"| {f.ElementAtOrDefault(1)} | | {f[0]} | failed ({string.Join(' ', f.Skip(2)).Trim()}) | | | |\n");
            }
        }

        md.Append('\n');
    }

    private static double Cells(string size)
    {
        // "values-10000" and "write-10000" both end in the row count; every row has 10 cells.
        string digits = new(size.Where(char.IsDigit).ToArray());
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double rows) ? rows * 10 : 1;
    }

    private static double SizeKey(string size) => Cells(size);

    private static string Ms(double ms) => ms >= 1000 ? string.Create(CultureInfo.InvariantCulture, $"{ms / 1000:F2} s") : string.Create(CultureInfo.InvariantCulture, $"{ms:F1} ms");
}
