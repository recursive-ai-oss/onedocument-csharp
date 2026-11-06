using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Bench;

/// <summary>
/// The format-rows half of a results page. Reads the <c>fmt-*.tsv</c> files that <c>fmtbench group</c> writes and appends one table per
/// operation, the bar table (OneDocument against the fastest managed comparator of the same operation and input) and the strict table,
/// which also counts comparators that do a lighter job than ours (forward value readers against our full model).
/// </summary>
internal static class FmtReport
{
    private sealed record Line(string Row, string Size, int Reps, double MinMs, double MedianMs, double Alloc, double PeakMb, string Checksum);

    // (title, our row, like-for-like comparators, extra comparators counted only in the strict table)
    private static readonly (string Title, string Ours, string[] Others, string[] Strict)[] Groups =
    [
        ("XLS read, value streaming", "onedocument.xls.read", ["sylvan.xls.read", "exceldatareader.xls.read"], []),
        ("XLSB read, value streaming", "onedocument.xlsb.read", ["sylvan.xlsb.read", "exceldatareader.xlsb.read"], []),
        ("XLS read, full model", "onedocument.xls.model", ["npoi.xls.model"], []),
        ("XLS write (build 10 columns per row, save)", "onedocument.xls.write", ["npoi.xls.write"], []),
        ("Recalculation, cold (every formula)", "onedocument.calc.cold", ["closedxml.calc.cold", "npoi.calc.cold", "epplus.calc.cold"], []),
        ("Recalculation, incremental (one input cell changed)", "onedocument.calc.incr", ["closedxml.calc.incr", "npoi.calc.incr", "epplus.calc.incr"], []),
        ("PPTX open, read the first slide", "onedocument.pptx.load", ["openxml.pptx.load"], []),
        ("PPTX open and save untouched", "onedocument.pptx.loadsave", ["openxml.pptx.loadsave"], []),
        ("PPTX edit one slide's text, save", "onedocument.pptx.edit", ["openxml.pptx.edit"], []),
        ("PPTX open and save untouched, to a stream", "onedocument.pptx.loadsave.mem", ["openxml.pptx.loadsave.mem"], []),
        ("PPTX edit one slide's text, save to a stream", "onedocument.pptx.edit.mem", ["openxml.pptx.edit.mem"], []),
        ("Text extraction, PPTX", "onedocument.pptx.extract", ["openxml.pptx.extract"], []),
        ("Text extraction, XLSX", "onedocument.xlsx.extract", ["openxml.xlsx.extract", "npoi.xlsx.extract"], []),
        ("Template fill, XLSX (loop over N items)", "onedocument.tpl.xlsx", ["closedxmlreport.tpl.xlsx"], []),
    ];

    // Rows with no managed comparator: published as information.
    private static readonly string[] InfoRows =
        ["onedocument.xlsb.model", "onedocument.tpl.docx", "onedocument.render.docx", "onedocument.doc.extract", "onedocument.rtf.extract", "onedocument.odt.extract", "onedocument.ods.model"];

    /// <summary>Appends the tables for every <c>fmt-*.tsv</c> in <paramref name="dir"/>; returns whether anything was written.</summary>
    public static bool Append(StringBuilder md, string dir)
    {
        var lines = new List<Line>();
        foreach (string file in Directory.EnumerateFiles(dir, "fmt-*.tsv").Order(StringComparer.Ordinal))
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
        var strict = new List<(string Group, string Size, double Ours, string Best, double BestMedian)>();
        foreach ((string title, string ours, string[] others, string[] extra) in Groups)
        {
            var inGroup = lines.Where(l => l.Row == ours || others.Contains(l.Row) || extra.Contains(l.Row)).ToList();
            if (inGroup.Count == 0)
            {
                continue;
            }

            md.Append("## ").Append(title).Append("\n\n");
            md.Append("| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |\n|---|---|---:|---:|---:|---:|---:|---:|---|\n");
            foreach (IGrouping<string, Line> g in inGroup.GroupBy(l => l.Size).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Line? us = g.FirstOrDefault(l => l.Row == ours);
                foreach (Line l in g.OrderBy(l => l.Row == ours ? 0 : 1).ThenBy(l => l.Row, StringComparer.Ordinal))
                {
                    string ratio = us is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{l.MedianMs / us.MedianMs:F2}x");
                    md.Append(CultureInfo.InvariantCulture, $"| {g.Key} | {l.Row}{(extra.Contains(l.Row) ? " (strict only)" : string.Empty)} | {l.Reps} | {Ms(l.MedianMs)} | {Ms(l.MinMs)} | {ratio} | {Mb(l.Alloc)} | {l.PeakMb:F0} MB | {l.Checksum} |\n");
                }

                if (us is null)
                {
                    continue;
                }

                var rivals = g.Where(l => others.Contains(l.Row)).ToList();
                if (rivals.Count > 0)
                {
                    Line best = rivals.MinBy(l => l.MedianMs)!;
                    bar.Add((title, g.Key, us.MedianMs, best.Row, best.MedianMs));
                }

                var all = g.Where(l => others.Contains(l.Row) || extra.Contains(l.Row)).ToList();
                if (extra.Length > 0 && all.Count > 0)
                {
                    Line best = all.MinBy(l => l.MedianMs)!;
                    strict.Add((title, g.Key, us.MedianMs, best.Row, best.MedianMs));
                }
            }

            md.Append('\n');
        }

        var info = lines.Where(l => InfoRows.Contains(l.Row)).ToList();
        if (info.Count > 0)
        {
            md.Append("## Rows with no managed comparator (information)\n\n| Input | Row | Reps | Median | Min | Allocated | Peak working set |\n|---|---|---:|---:|---:|---:|---:|\n");
            foreach (Line l in info.OrderBy(l => l.Row, StringComparer.Ordinal).ThenBy(l => l.Size, StringComparer.Ordinal))
            {
                md.Append(CultureInfo.InvariantCulture, $"| {l.Size} | {l.Row} | {l.Reps} | {Ms(l.MedianMs)} | {Ms(l.MinMs)} | {Mb(l.Alloc)} | {l.PeakMb:F0} MB |\n");
            }

            md.Append('\n');
        }

        Table(md, "Bar: OneDocument against the fastest like-for-like managed comparator", bar);
        Table(md, "Strict: also against the lighter-job comparators (forward value readers against our full model)", strict);
        return true;
    }

    private static void Table(StringBuilder md, string title, List<(string Group, string Size, double Ours, string Best, double BestMedian)> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        md.Append("## ").Append(title).Append("\n\n");
        md.Append("Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.\n\n");
        md.Append("| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |\n|---|---|---:|---|---:|---:|\n");
        foreach ((string group, string size, double o, string best, double bm) in rows)
        {
            md.Append(CultureInfo.InvariantCulture, $"| {group} | {size} | {Ms(o)} | {best} | {Ms(bm)} | {o / bm:F2}x |\n");
        }

        double geomean = Math.Exp(rows.Average(r => Math.Log(r.Ours / r.BestMedian)));
        var worst = rows.MaxBy(r => r.Ours / r.BestMedian);
        md.Append(CultureInfo.InvariantCulture, $"\nGeometric mean of the ratios: {geomean:F3}x over {rows.Count} rows. Worst row: {worst.Group}, {worst.Size} at {worst.Ours / worst.BestMedian:F2}x.\n\n");
    }

    private static string Mb(double bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576:F1} MB");

    private static string Ms(double ms) => ms >= 1000 ? string.Create(CultureInfo.InvariantCulture, $"{ms / 1000:F2} s") : string.Create(CultureInfo.InvariantCulture, $"{ms:F1} ms");
}
