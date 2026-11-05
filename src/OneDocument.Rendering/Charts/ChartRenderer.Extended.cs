using System.Globalization;

namespace RecursiveAI.OneDocument.Charts;

// Chartex layouts that sit on axes: waterfall, histogram, Pareto, box and whisker.
// [MS-ODRAWXML] §2.24 (cx:series layoutId) names the layouts; the statistics follow Excel's documented definitions.
internal sealed partial class ChartRenderer
{
    private static Derived BuildWaterfall(ChartGroup g)
    {
        var d = new Derived();
        ChartSeries? s = Ordered(g).FirstOrDefault();
        int n = s?.Values?.Count ?? 0;
        double running = 0;
        d.Min = 0;
        d.Max = 0;
        for (int i = 0; i < n; i++)
        {
            double v = V(s!.Values, i) ?? 0;
            bool total = s.Points.Any(pt => pt.Index == i && pt.IsSubtotal);
            double start = total ? 0 : running;
            double end = total ? v : running + v;
            running = end;
            d.Cumulative.Add(start);
            d.Counts.Add(end);
            d.Kinds.Add(total ? 2 : v >= 0 ? 0 : 1);
            d.Labels.Add(CategoryText(s.Categories, i));
            d.Min = Math.Min(d.Min, Math.Min(start, end));
            d.Max = Math.Max(d.Max, Math.Max(start, end));
        }

        return d;
    }

    private void PaintWaterfall(ChartGroup g, Pair p, Geo geo)
    {
        if (!_derived.TryGetValue(g, out Derived? d) || d.Counts.Count == 0)
        {
            return;
        }

        ChartSeries s = Ordered(g).First();
        double barSlots = 1.0 / (1 + (Math.Clamp(g.GapWidth == 150 ? 50 : g.GapWidth, 0, 500) / 100.0));
        ChartRgb conn = new(0x7F, 0x7F, 0x7F);
        for (int i = 0; i < d.Counts.Count; i++)
        {
            ChartRgb color = s.Points.FirstOrDefault(pt => pt.Index == i && pt.Format?.Fill?.Solid is not null) is { } dp
                ? Resolve(dp.Format!.Fill!.Solid!)
                : _theme.AutomaticSeriesColor(d.Kinds[i], 3);
            double c0 = geo.Cat(i + 0.5 - (barSlots / 2));
            double c1 = geo.Cat(i + 0.5 + (barSlots / 2));
            double a0 = geo.Val(d.Cumulative[i]);
            double a1 = geo.Val(d.Counts[i]);
            _c.Rect(Math.Min(c0, c1), Math.Min(a0, a1), Math.Abs(c1 - c0), Math.Abs(a1 - a0), color);
            if (g.ShowConnectorLines && i + 1 < d.Counts.Count)
            {
                double nextC = geo.Cat(i + 1.5 - (barSlots / 2));
                _c.Line(Math.Max(c0, c1), a1, Math.Min(nextC, geo.Cat(i + 1.5 + (barSlots / 2))), a1, 0.75 * PtPx, conn);
            }

            ChartDataLabels? dl = s.Labels;
            if (dl is { Deleted: false })
            {
                double delta = d.Counts[i] - d.Cumulative[i];
                double shown = d.Kinds[i] == 2 ? d.Counts[i] : delta;
                string text = Fmt(shown, dl.FormatCode ?? s.Values?.FormatCode);
                TextStyle ts = Style(dl.TextFormat, _basePt * 0.9);
                bool up = d.Counts[i] >= d.Cumulative[i];
                DrawText(text, (c0 + c1) / 2, up ? Math.Min(a0, a1) - 3 : Math.Max(a0, a1) + 3, ts, HAlign.Center, up ? VAlign.Bottom : VAlign.Top);
            }
        }

        _ = p;
    }

    private static Derived BuildHistogram(ChartGroup g)
    {
        var d = new Derived();
        ChartSeries? s = Ordered(g).FirstOrDefault(x => !x.IsParetoLine);
        var vals = new List<double>();
        for (int i = 0; i < (s?.Values?.Count ?? 0); i++)
        {
            if (V(s!.Values, i) is { } v)
            {
                vals.Add(v);
            }
        }

        bool binned = g.BinCount is not null || g.BinSize is not null;
        bool categorical = !binned && s?.Categories is { IsNumeric: false, Count: > 0 };
        if (categorical)
        {
            // Text categories: the bars count the rows of each category.
            var order = new List<string>();
            var counts = new Dictionary<string, double>();
            for (int i = 0; i < s!.Categories!.Count; i++)
            {
                string key = s.Categories.Strings[i] ?? string.Empty;
                counts[key] = counts.GetValueOrDefault(key) + 1;
                if (!order.Contains(key))
                {
                    order.Add(key);
                }
            }

            foreach (string k in order)
            {
                d.Labels.Add(k);
                d.Counts.Add(counts[k]);
            }
        }
        else if (vals.Count > 0)
        {
            double min = vals.Min();
            double max = vals.Max();
            double width;
            int bins;
            if (g.BinSize is { } bs && bs > 0)
            {
                width = bs;
                bins = Math.Max(1, (int)Math.Ceiling(((max - min) / width) - 1e-9));
            }
            else if (g.BinCount is { } bc && bc > 0)
            {
                bins = bc;
                width = (max - min) / bins;
            }
            else
            {
                // Scott's normal reference rule, which Excel uses for automatic bins: width = 3.5 s / n^(1/3).
                double mean = vals.Average();
                double sd = vals.Count > 1 ? Math.Sqrt(vals.Sum(v => (v - mean) * (v - mean)) / (vals.Count - 1)) : 0;
                width = sd > 0 ? 3.5 * sd / Math.Pow(vals.Count, 1.0 / 3) : 1;
                bins = Math.Max(1, (int)Math.Ceiling((max - min) / width));
            }

            if (width <= 0)
            {
                width = 1;
                bins = 1;
            }

            bins = Math.Min(bins, 1000);
            var counts = new double[bins];
            foreach (double v in vals)
            {
                // Intervals are closed on the right, with the first one closed on both sides.
                int idx = (int)Math.Ceiling(((v - min) / width) - 1e-9) - 1;
                counts[Math.Clamp(idx, 0, bins - 1)]++;
            }

            for (int i = 0; i < bins; i++)
            {
                double a = min + (i * width);
                double b = a + width;
                d.Labels.Add((i == 0 ? "[" : "(") + a.ToString("G6", CultureInfo.InvariantCulture) + ", " + b.ToString("G6", CultureInfo.InvariantCulture) + "]");
                d.Counts.Add(counts[i]);
                d.BinEdges.Add(a);
            }
        }

        if (g.Type == ChartType.Pareto && d.Counts.Count > 0)
        {
            var idx = Enumerable.Range(0, d.Counts.Count).OrderByDescending(i => d.Counts[i]).ToList();
            d.Labels = [.. idx.Select(i => d.Labels[i])];
            d.Counts = [.. idx.Select(i => d.Counts[i])];
            double total = d.Counts.Sum();
            double acc = 0;
            foreach (double c in d.Counts)
            {
                acc += c;
                d.Cumulative.Add(total <= 0 ? 0 : acc / total);
            }
        }

        d.Min = 0;
        d.Max = d.Counts.Count == 0 ? 1 : d.Counts.Max();
        return d;
    }

    private void PaintHistogram(ChartGroup g, Pair p, Geo geo)
    {
        if (!_derived.TryGetValue(g, out Derived? d) || d.Counts.Count == 0)
        {
            return;
        }

        ChartSeries? s = Ordered(g).FirstOrDefault(x => !x.IsParetoLine);
        double gap = g.GapWidth == 150 ? 5 : g.GapWidth;
        double barSlots = 1.0 / (1 + (gap / 100.0));
        ChartRgb color = s is null ? _theme.AutomaticSeriesColor(0, 1) : SeriesColor(s);
        for (int i = 0; i < d.Counts.Count; i++)
        {
            double c0 = geo.Cat(i + 0.5 - (barSlots / 2));
            double c1 = geo.Cat(i + 0.5 + (barSlots / 2));
            double a0 = geo.Val(0);
            double a1 = geo.Val(d.Counts[i]);
            _c.Rect(Math.Min(c0, c1), Math.Min(a0, a1), Math.Abs(c1 - c0), Math.Abs(a1 - a0), color);
            ChartDataLabels? dl = s?.Labels;
            if (dl is { Deleted: false })
            {
                DrawText(Fmt(d.Counts[i], dl.FormatCode), (c0 + c1) / 2, Math.Min(a0, a1) - 3, Style(dl.TextFormat, _basePt * 0.9), HAlign.Center, VAlign.Bottom);
            }
        }

        if (g.Type == ChartType.Pareto && d.Cumulative.Count == d.Counts.Count)
        {
            ChartSeries? line = g.Series.FirstOrDefault(x => x.IsParetoLine);
            ChartRgb lc = line is null ? _theme.AutomaticSeriesColor(1, 2) : LineColor(line.Format, _theme.AutomaticSeriesColor(1, 2)) ?? _theme.AutomaticSeriesColor(1, 2);
            var pts = new List<(double X, double Y)>();
            for (int i = 0; i < d.Cumulative.Count; i++)
            {
                double y = geo.Plot.Bottom - (d.Cumulative[i] * geo.Plot.H);
                pts.Add((geo.Cat(i + 0.5), y));
            }

            _c.Polyline(pts, 2.25 * PtPx, lc, null, false);
            foreach ((double x, double y) in pts)
            {
                _c.Circle(x, y, 2.5 * PtPx, lc);
            }
        }

        _ = p;
    }

    private void DrawParetoAxis(Box plot, TextStyle ts)
    {
        for (int k = 0; k <= 10; k++)
        {
            double y = plot.Bottom - (k / 10.0 * plot.H);
            DrawText((k * 10).ToString(CultureInfo.InvariantCulture) + "%", plot.Right + 8, y, ts, HAlign.Left, VAlign.Middle);
        }

        _c.Line(plot.Right, plot.Y, plot.Right, plot.Bottom, 0.75 * PtPx, GridColor);
    }

    private static Derived BuildBoxes(ChartGroup g)
    {
        var d = new Derived();
        List<ChartSeries> series = [.. Ordered(g)];
        var cats = new List<string>();
        foreach (ChartSeries s in series)
        {
            if (s.Categories is { IsNumeric: false } c)
            {
                foreach (string? x in c.Strings)
                {
                    string key = x ?? string.Empty;
                    if (!cats.Contains(key))
                    {
                        cats.Add(key);
                    }
                }
            }
        }

        if (cats.Count == 0)
        {
            cats.Add(string.Empty);
        }

        d.Labels = cats;
        d.Min = double.PositiveInfinity;
        d.Max = double.NegativeInfinity;
        for (int si = 0; si < series.Count; si++)
        {
            ChartSeries s = series[si];
            for (int ci = 0; ci < cats.Count; ci++)
            {
                var vals = new List<double>();
                for (int i = 0; i < (s.Values?.Count ?? 0); i++)
                {
                    string key = s.Categories is { IsNumeric: false } c && i < c.Strings.Count ? c.Strings[i] ?? string.Empty : string.Empty;
                    if (key == cats[ci] && V(s.Values, i) is { } v)
                    {
                        vals.Add(v);
                    }
                }

                if (vals.Count == 0)
                {
                    continue;
                }

                BoxStats b = Stats(vals, g.ExclusiveQuartiles);
                b.CatIndex = ci;
                b.SeriesIndex = si;
                b.Label = cats[ci];
                d.Boxes.Add(b);
                d.Min = Math.Min(d.Min, Math.Min(b.LowWhisker, b.Outliers.DefaultIfEmpty(b.LowWhisker).Min()));
                d.Max = Math.Max(d.Max, Math.Max(b.HighWhisker, b.Outliers.DefaultIfEmpty(b.HighWhisker).Max()));
            }
        }

        if (!double.IsFinite(d.Min))
        {
            d.Min = 0;
            d.Max = 1;
        }

        return d;
    }

    // Quartiles as QUARTILE.EXC (rank (n+1)p) or QUARTILE.INC (rank (n-1)p+1); whiskers reach the furthest value within 1.5 IQR.
    internal static BoxStats Stats(List<double> values, bool exclusive)
    {
        double[] x = [.. values.OrderBy(v => v)];
        int n = x.Length;
        double Q(double p)
        {
            double pos = exclusive ? (n + 1) * p : ((n - 1) * p) + 1;
            if (pos <= 1)
            {
                return x[0];
            }

            if (pos >= n)
            {
                return x[n - 1];
            }

            int lo = (int)Math.Floor(pos);
            return x[lo - 1] + ((pos - lo) * (x[lo] - x[lo - 1]));
        }

        var b = new BoxStats { Min = x[0], Max = x[n - 1], Q1 = Q(0.25), Median = Q(0.5), Q3 = Q(0.75), Mean = x.Average() };
        double iqr = b.Q3 - b.Q1;
        double lowFence = b.Q1 - (1.5 * iqr);
        double highFence = b.Q3 + (1.5 * iqr);
        b.LowWhisker = x.Where(v => v >= lowFence).DefaultIfEmpty(b.Q1).Min();
        b.HighWhisker = x.Where(v => v <= highFence).DefaultIfEmpty(b.Q3).Max();
        b.Outliers = [.. x.Where(v => v < lowFence || v > highFence)];
        return b;
    }

    private void PaintBoxes(ChartGroup g, Pair p, Geo geo)
    {
        if (!_derived.TryGetValue(g, out Derived? d))
        {
            return;
        }

        List<ChartSeries> series = [.. Ordered(g)];
        int m = Math.Max(1, series.Count);
        double boxSlots = 0.6 / m;
        foreach (BoxStats b in d.Boxes)
        {
            ChartSeries s = series[b.SeriesIndex];
            ChartRgb color = SeriesColor(s);
            ChartRgb dark = new((byte)(color.R * 0.6), (byte)(color.G * 0.6), (byte)(color.B * 0.6));
            double center = b.CatIndex + 0.5 + ((b.SeriesIndex - ((m - 1) / 2.0)) * (0.8 / m));
            double x0 = geo.Cat(center - (boxSlots / 2));
            double x1 = geo.Cat(center + (boxSlots / 2));
            double xc = geo.Cat(center);
            double yq1 = geo.Val(b.Q1);
            double yq3 = geo.Val(b.Q3);
            double w = 0.75 * PtPx;
            _c.Line(xc, geo.Val(b.HighWhisker), xc, yq3, w, dark);
            _c.Line(xc, yq1, xc, geo.Val(b.LowWhisker), w, dark);
            double cap = Math.Abs(x1 - x0) / 4;
            _c.Line(xc - cap, geo.Val(b.HighWhisker), xc + cap, geo.Val(b.HighWhisker), w, dark);
            _c.Line(xc - cap, geo.Val(b.LowWhisker), xc + cap, geo.Val(b.LowWhisker), w, dark);
            _c.Rect(Math.Min(x0, x1), Math.Min(yq1, yq3), Math.Abs(x1 - x0), Math.Abs(yq1 - yq3), new ChartRgb(color.R, color.G, color.B, 0.85));
            _c.StrokeRect(Math.Min(x0, x1), Math.Min(yq1, yq3), Math.Abs(x1 - x0), Math.Abs(yq1 - yq3), w, dark);
            double ym = geo.Val(b.Median);
            _c.Line(x0, ym, x1, ym, w * 1.5, dark);
            if (g.ShowMeanMarker)
            {
                double r = 3;
                double ymean = geo.Val(b.Mean);
                _c.Line(xc - r, ymean - r, xc + r, ymean + r, w, dark);
                _c.Line(xc - r, ymean + r, xc + r, ymean - r, w, dark);
            }

            if (g.ShowOutliers)
            {
                foreach (double o in b.Outliers)
                {
                    _c.Circle(xc, geo.Val(o), 2.5, new ChartRgb(255, 255, 255, 0), dark, w);
                }
            }
        }

        _ = p;
    }
}
