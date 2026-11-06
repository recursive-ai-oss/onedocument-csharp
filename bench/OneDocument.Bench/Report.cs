using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RecursiveAI.OneDocument.Bench;

/// <summary>
/// Turns a results directory into one Markdown page: the machine and load records, the versions, a timing table per benchmark
/// class (median, ratio to the class baseline, allocations) and the peak-memory table.
/// </summary>
internal static class Report
{
    /// <summary>report &lt;resultsDir&gt;: reads bdn/**/*-report-full.json, peak.tsv, env.txt, load.log and versions.txt; writes results.md.</summary>
    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: report <resultsDir>");
            return 2;
        }

        string dir = args[0];
        var md = new StringBuilder();
        md.Append("# Benchmark results\n\n");
        AppendFile(md, "Machine and load", Path.Combine(dir, "env.txt"));
        AppendFile(md, "Load while running (sampled)", Path.Combine(dir, "load.log"));
        AppendFile(md, "Versions", Path.Combine(dir, "versions.txt"));

        var bar = new List<(string Class, string Input, double Ours, string Fastest, double FastestMedian)>();
        foreach (string file in Directory.EnumerateFiles(dir, "*-report-full.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            string title = doc.RootElement.GetProperty("Title").GetString() ?? file;
            md.Append("## ").Append(title.Split('-')[0].Split('.').Last()).Append("\n\n");
            md.Append("| Input | Method | Median | Ratio | Allocated/op |\n|---|---|---:|---:|---:|\n");
            var rows = new List<(string Input, string Method, double Median, double Alloc)>();
            foreach (JsonElement b in doc.RootElement.GetProperty("Benchmarks").EnumerateArray())
            {
                string input = (b.GetProperty("Parameters").GetString() ?? string.Empty).Replace("Input=", string.Empty, StringComparison.Ordinal);
                if (b.GetProperty("Statistics").ValueKind != JsonValueKind.Object)
                {
                    md.Append(CultureInfo.InvariantCulture, $"| {input} | {b.GetProperty("Method").GetString()} | failed | | |\n");
                    continue;
                }

                double median = b.GetProperty("Statistics").GetProperty("Median").GetDouble();
                double alloc = b.GetProperty("Memory").GetProperty("BytesAllocatedPerOperation").GetDouble();
                rows.Add((input, b.GetProperty("Method").GetString() ?? "?", median, alloc));
            }

            // The first row of each input in declaration order is the class baseline.
            foreach (IGrouping<string, (string Input, string Method, double Median, double Alloc)> g in rows.GroupBy(r => r.Input))
            {
                double baseline = g.First().Median;
                foreach ((string input, string method, double median, double alloc) in g)
                {
                    md.Append(CultureInfo.InvariantCulture, $"| {input} | {method} | {Time(median)} | {median / baseline:F2}x | {Bytes(alloc)} |\n");
                }
            }

            md.Append('\n');

            // The bar compares OneDocument with the fastest comparator of the class (the raw zip row is a floor, not a comparator).
            string className = title.Split('-')[0].Split('.').Last();
            foreach (IGrouping<string, (string Input, string Method, double Median, double Alloc)> g in rows.GroupBy(r => r.Input))
            {
                var ours = g.Where(r => r.Method == "OneDocument").ToList();
                var others = g.Where(r => r.Method is not ("OneDocument" or "RawZip")).ToList();
                if (ours.Count == 1 && others.Count > 0)
                {
                    var best = others.MinBy(r => r.Median);
                    bar.Add((className, g.Key, ours[0].Median, best.Method, best.Median));
                }
            }
        }

        if (bar.Count > 0)
        {
            md.Append("## Bar: OneDocument against the fastest comparator\n\n");
            md.Append("Ratio is OneDocument median / the fastest comparator's median in the same class and input; at or below 1.00 is faster. The raw zip row is a floor and is not a comparator.\n\n");
            md.Append("| Class | Input | OneDocument | Fastest comparator | Comparator median | Ratio |\n|---|---|---:|---|---:|---:|\n");
            foreach ((string c, string input, double o, string f, double fm) in bar)
            {
                md.Append(CultureInfo.InvariantCulture, $"| {c} | {input} | {Time(o)} | {f} | {Time(fm)} | {o / fm:F2}x |\n");
            }

            double geomean = Math.Exp(bar.Average(r => Math.Log(r.Ours / r.FastestMedian)));
            var worst = bar.MaxBy(r => r.Ours / r.FastestMedian);
            md.Append(CultureInfo.InvariantCulture, $"\nGeometric mean of the ratios: {geomean:F3}x over {bar.Count} rows. Worst row: {worst.Class} {worst.Input} at {worst.Ours / worst.FastestMedian:F2}x.\n\n");
        }

        XlsxReport.Append(md, dir);
        FmtReport.Append(md, dir);
        XlsxReport.AppendFootprint(md, dir);

        string peak = Path.Combine(dir, "peak.tsv");
        if (File.Exists(peak))
        {
            md.Append("## Peak memory (one run in a fresh process)\n\n");
            md.Append("Baseline is the working set after the input bytes are loaded; peak is the process high-water mark; allocated is managed bytes allocated by the row.\n\n");
            md.Append("| Row | Input | Baseline | Peak | Allocated |\n|---|---|---:|---:|---:|\n");
            foreach (string line in File.ReadLines(peak).Skip(1))
            {
                string[] f = line.Split('\t');
                if (f.Length >= 5 && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double bs)
                    && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double pk)
                    && double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double al))
                {
                    md.Append(CultureInfo.InvariantCulture, $"| {f[0]} | {f[1]} | {Bytes(bs)} | {Bytes(pk)} | {Bytes(al)} |\n");
                }
                else
                {
                    md.Append(CultureInfo.InvariantCulture, $"| {f[0]} | {f.ElementAtOrDefault(1)} | failed | failed | failed |\n");
                }
            }
        }

        File.WriteAllText(Path.Combine(dir, "results.md"), md.ToString());
        Console.WriteLine(Path.Combine(dir, "results.md"));
        return 0;
    }

    private static void AppendFile(StringBuilder md, string heading, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        md.Append("## ").Append(heading).Append("\n\n```text\n").Append(File.ReadAllText(path).TrimEnd()).Append("\n```\n\n");
    }

    private static string Time(double ns) => ns switch
    {
        >= 1e9 => string.Create(CultureInfo.InvariantCulture, $"{ns / 1e9:F2} s"),
        >= 1e6 => string.Create(CultureInfo.InvariantCulture, $"{ns / 1e6:F2} ms"),
        >= 1e3 => string.Create(CultureInfo.InvariantCulture, $"{ns / 1e3:F2} us"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{ns:F0} ns"),
    };

    private static string Bytes(double b) => b switch
    {
        >= 1 << 30 => string.Create(CultureInfo.InvariantCulture, $"{b / (1 << 30):F2} GB"),
        >= 1 << 20 => string.Create(CultureInfo.InvariantCulture, $"{b / (1 << 20):F1} MB"),
        >= 1 << 10 => string.Create(CultureInfo.InvariantCulture, $"{b / (1 << 10):F1} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{b:F0} B"),
    };
}
