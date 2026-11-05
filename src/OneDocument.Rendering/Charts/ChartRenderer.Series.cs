using System.Globalization;

namespace RecursiveAI.OneDocument.Charts;

// Series painters for the axis-based chart types.
internal sealed partial class ChartRenderer
{
    private static readonly string[] AutoMarkers = ["diamond", "square", "triangle", "x", "star", "circle", "plus"];

    private static double BaseValue(Pair p) => p.Cat.CrossesAt ?? p.Cat.Crosses switch
    {
        "min" => p.Scale.Min,
        "max" => p.Scale.Max,
        _ => Math.Clamp(p.Scale.IsLog ? p.Scale.Min : 0, p.Scale.Min, p.Scale.Max),
    };

    private void PaintGroup(ChartGroup g, Pair p, Geo geo, Box plot)
    {
        switch (g.Type)
        {
            case ChartType.Column:
            case ChartType.Bar:
                PaintBars(g, p, geo);
                break;
            case ChartType.Line:
                PaintLines(g, p, geo);
                break;
            case ChartType.Area:
                PaintAreas(g, p, geo);
                break;
            case ChartType.Scatter:
                PaintScatter(g, p, plot, false);
                break;
            case ChartType.Bubble:
                PaintScatter(g, p, plot, true);
                break;
            case ChartType.Stock:
                PaintStock(g, p, geo);
                break;
            case ChartType.Surface:
                PaintSurface(g, p, geo, plot);
                break;
            case ChartType.Waterfall:
                PaintWaterfall(g, p, geo);
                break;
            case ChartType.Histogram:
            case ChartType.Pareto:
                PaintHistogram(g, p, geo);
                break;
            case ChartType.BoxWhisker:
                PaintBoxes(g, p, geo);
                break;
        }

        foreach (ChartSeries s in g.Series)
        {
            if (s.Hidden || s.IsParetoLine)
            {
                continue;
            }

            foreach (ChartTrendline t in s.Trendlines)
            {
                PaintTrendline(g, p, geo, plot, s, t);
            }

            foreach (ChartErrorBars e in s.ErrorBars)
            {
                PaintErrorBars(g, p, geo, plot, s, e);
            }
        }
    }

    private static IEnumerable<ChartSeries> Ordered(ChartGroup g) => g.Series.Where(s => !s.Hidden).OrderBy(s => s.Order);

    // ---- bars ----

    private void PaintBars(ChartGroup g, Pair p, Geo geo)
    {
        List<ChartSeries> series = [.. Ordered(g)];
        if (series.Count == 0)
        {
            return;
        }

        bool clustered = g.Grouping == ChartGrouping.Standard;
        int m = clustered ? series.Count : 1;
        double gap = Math.Clamp(g.GapWidth, 0, 500) / 100.0;
        double overlap = clustered ? Math.Clamp(g.Overlap, -100, 100) / 100.0 : 1.0;
        double barSlots = 1.0 / (m - ((m - 1) * overlap) + gap);
        double clusterSlots = barSlots * (m - ((m - 1) * overlap));
        double baseV = BaseValue(p);
        for (int i = 0; i < p.N; i++)
        {
            double posAcc = 0, negAcc = 0;
            double total = 0;
            if (g.Grouping == ChartGrouping.PercentStacked)
            {
                total = series.Sum(s => Math.Abs(V(s.Values, i) ?? 0));
            }

            for (int k = 0; k < series.Count; k++)
            {
                ChartSeries s = series[k];
                if (V(s.Values, i) is not { } v)
                {
                    continue;
                }

                double v0;
                double v1;
                if (clustered)
                {
                    v0 = baseV;
                    v1 = v;
                }
                else
                {
                    double vv = g.Grouping == ChartGrouping.PercentStacked ? (total == 0 ? 0 : v / total) : v;
                    if (vv >= 0)
                    {
                        v0 = posAcc;
                        posAcc += vv;
                        v1 = posAcc;
                    }
                    else
                    {
                        v0 = negAcc;
                        negAcc += vv;
                        v1 = negAcc;
                    }
                }

                double cs = i + 0.5 - (clusterSlots / 2) + (clustered ? k * barSlots * (1 - overlap) : 0);
                double c0 = geo.Cat(cs);
                double c1 = geo.Cat(cs + barSlots);
                double a0 = geo.Val(v0);
                double a1 = geo.Val(v1);
                ChartRgb color = PointColor(g, s, i, k);
                ChartShapeFormat? fmt = s.Points.FirstOrDefault(d => d.Index == i)?.Format ?? s.Format;
                double x = geo.Horizontal ? Math.Min(a0, a1) : Math.Min(c0, c1);
                double y = geo.Horizontal ? Math.Min(c0, c1) : Math.Min(a0, a1);
                double w = geo.Horizontal ? Math.Abs(a1 - a0) : Math.Abs(c1 - c0);
                double h = geo.Horizontal ? Math.Abs(c1 - c0) : Math.Abs(a1 - a0);
                if (fmt?.Fill is null && s.Points.FirstOrDefault(d => d.Index == i)?.Format is null)
                {
                    DrawBox(x, y, w, h, s.Format is { Line: not null } sf ? new ChartShapeFormat { Line = sf.Line } : null, color);
                }
                else
                {
                    DrawBox(x, y, w, h, fmt, color);
                }

                PaintBarLabel(g, s, i, v, x, y, w, h, v >= 0, clustered, geo.Horizontal, p);
            }
        }
    }

    private void PaintBarLabel(ChartGroup g, ChartSeries s, int i, double v, double x, double y, double w, double h, bool positive, bool clustered, bool horizontal, Pair p)
    {
        ChartDataLabels? dl = s.Labels ?? g.Labels;
        if (dl is null || dl.Deleted)
        {
            return;
        }

        string? text = LabelText(dl, s, i, v, p, g);
        if (text is null)
        {
            return;
        }

        TextStyle ts = Style(dl.TextFormat, _basePt * 0.9);
        string pos = dl.Position ?? (clustered ? "outEnd" : "ctr");
        double cx = x + (w / 2);
        double cy = y + (h / 2);
        double gapPx = 3;
        if (horizontal)
        {
            switch (pos)
            {
                case "outEnd":
                    DrawText(text, positive ? x + w + gapPx : x - gapPx, cy, ts, positive ? HAlign.Left : HAlign.Right, VAlign.Middle);
                    break;
                case "inEnd":
                    DrawText(text, positive ? x + w - gapPx : x + gapPx, cy, ts, positive ? HAlign.Right : HAlign.Left, VAlign.Middle);
                    break;
                case "inBase":
                    DrawText(text, positive ? x + gapPx : x + w - gapPx, cy, ts, positive ? HAlign.Left : HAlign.Right, VAlign.Middle);
                    break;
                default:
                    DrawText(text, cx, cy, ts, HAlign.Center, VAlign.Middle);
                    break;
            }
        }
        else
        {
            switch (pos)
            {
                case "outEnd":
                    DrawText(text, cx, positive ? y - gapPx : y + h + gapPx, ts, HAlign.Center, positive ? VAlign.Bottom : VAlign.Top);
                    break;
                case "inEnd":
                    DrawText(text, cx, positive ? y + gapPx : y + h - gapPx, ts, HAlign.Center, positive ? VAlign.Top : VAlign.Bottom);
                    break;
                case "inBase":
                    DrawText(text, cx, positive ? y + h - gapPx : y + gapPx, ts, HAlign.Center, positive ? VAlign.Bottom : VAlign.Top);
                    break;
                default:
                    DrawText(text, cx, cy, ts, HAlign.Center, VAlign.Middle);
                    break;
            }
        }
    }

    private string? LabelText(ChartDataLabels dl, ChartSeries s, int i, double v, Pair? p, ChartGroup g, double? percent = null)
    {
        var parts = new List<string>();
        if (dl.ShowSeriesName && s.Name?.Text is { } sn)
        {
            parts.Add(sn);
        }

        if (dl.ShowCategoryName)
        {
            parts.Add(p is not null && i < p.Labels.Count ? p.Labels[i] : CategoryText(s.Categories, i, g));
        }

        if (dl.ShowValue)
        {
            string? code = dl.FormatCode is { Length: > 0 } && !dl.SourceLinked ? dl.FormatCode : s.Values?.FormatCode ?? dl.FormatCode;
            if (dl.FormatCode is { Length: > 0 } f && dl.SourceLinked is false)
            {
                code = f;
            }

            parts.Add(Fmt(v, code));
        }

        if (dl.ShowPercent && percent is { } pc)
        {
            parts.Add(Fmt(pc, "0%"));
        }

        return parts.Count == 0 ? null : string.Join(dl.Separator ?? ", ", parts);
    }

    // ---- lines and areas ----

    private static double PosOf(Pair p, int i)
    {
        if (p.DatePos is { } dp)
        {
            return i < dp.Length ? dp[i] + 0.5 : i + 0.5;
        }

        if (p.Val.CrossBetween || p.N <= 1)
        {
            return i + 0.5;
        }

        return i * p.N / (double)(p.N - 1);
    }

    private static double?[] Cumulative(ChartGroup g, List<ChartSeries> series, int idx, int n, bool percent)
    {
        var result = new double?[n];
        for (int i = 0; i < n; i++)
        {
            if (V(series[idx].Values, i) is null)
            {
                result[i] = null;
                continue;
            }

            double sum = 0;
            for (int k = 0; k <= idx; k++)
            {
                sum += V(series[k].Values, i) ?? 0;
            }

            if (percent)
            {
                double total = series.Sum(s => Math.Abs(V(s.Values, i) ?? 0));
                sum = total == 0 ? 0 : sum / total;
            }

            result[i] = sum;
        }

        _ = g;
        return result;
    }

    private static double?[] SeriesValues(ChartGroup g, List<ChartSeries> series, int idx, Pair p)
    {
        if (g.Grouping != ChartGrouping.Standard)
        {
            return Cumulative(g, series, idx, p.N, g.Grouping == ChartGrouping.PercentStacked);
        }

        var r = new double?[p.N];
        for (int i = 0; i < p.N; i++)
        {
            r[i] = V(series[idx].Values, i);
        }

        return r;
    }

    private void PaintLines(ChartGroup g, Pair p, Geo geo)
    {
        List<ChartSeries> series = [.. Ordered(g)];
        for (int si = 0; si < series.Count; si++)
        {
            ChartSeries s = series[si];
            double?[] vals = SeriesValues(g, series, si, p);
            ChartRgb color = LineColor(s.Format, SeriesColor(s)) ?? new ChartRgb(0, 0, 0, 0);
            double w = LineWidth(s.Format, 2.25);
            var run = new List<(double X, double Y)>();
            void Flush()
            {
                if (run.Count >= 2)
                {
                    _c.Polyline(s.Smooth ? Smooth(run) : run, w, color, s.Format?.Line?.Dash, false);
                }

                run.Clear();
            }

            for (int i = 0; i < p.N; i++)
            {
                double? v = vals[i];
                if (v is null && _chart.BlanksAs == ChartBlanksAs.Zero)
                {
                    v = 0;
                }

                if (v is null)
                {
                    if (_chart.BlanksAs != ChartBlanksAs.Span)
                    {
                        Flush();
                    }

                    continue;
                }

                run.Add(geo.Pt(PosOf(p, i), v.Value));
            }

            Flush();
            // Markers and labels.
            for (int i = 0; i < p.N; i++)
            {
                if (vals[i] is not { } v)
                {
                    continue;
                }

                (double x, double y) = geo.Pt(PosOf(p, i), v);
                bool markers = g.ShowMarkers && s.Marker?.Symbol != "none";
                if (markers && (s.Marker is not null || g.ShowMarkers))
                {
                    ChartMarker? mk = s.Points.FirstOrDefault(d => d.Index == i)?.Marker ?? s.Marker;
                    DrawMarker(mk, s, x, y, si);
                }

                ChartDataLabels? dl = s.Labels ?? g.Labels;
                if (dl is { Deleted: false } && LabelText(dl, s, i, V(s.Values, i) ?? v, p, g) is { } text)
                {
                    PlaceLabel(text, dl, x, y, "r", 4 + (s.Marker?.Size ?? 5), Style(dl.TextFormat, _basePt * 0.9));
                }
            }
        }
    }

    private void PlaceLabel(string text, ChartDataLabels dl, double x, double y, string defaultPos, double gap, TextStyle ts)
    {
        string pos = dl.Position ?? defaultPos;
        switch (pos)
        {
            case "t":
                DrawText(text, x, y - gap, ts, HAlign.Center, VAlign.Bottom);
                break;
            case "b":
                DrawText(text, x, y + gap, ts, HAlign.Center, VAlign.Top);
                break;
            case "l":
                DrawText(text, x - gap, y, ts, HAlign.Right, VAlign.Middle);
                break;
            case "ctr":
                DrawText(text, x, y, ts, HAlign.Center, VAlign.Middle);
                break;
            default:
                DrawText(text, x + gap, y, ts, HAlign.Left, VAlign.Middle);
                break;
        }
    }

    // Catmull-Rom spline through the points, sampled; Excel's smoothing is a Bezier through the points, which looks alike.
    private static List<(double X, double Y)> Smooth(List<(double X, double Y)> pts)
    {
        var r = new List<(double X, double Y)>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            (double X, double Y) p0 = pts[Math.Max(0, i - 1)];
            (double X, double Y) p1 = pts[i];
            (double X, double Y) p2 = pts[i + 1];
            (double X, double Y) p3 = pts[Math.Min(pts.Count - 1, i + 2)];
            for (int s = 0; s < 12; s++)
            {
                double t = s / 12.0;
                double t2 = t * t;
                double t3 = t2 * t;
                r.Add((
                    0.5 * ((2 * p1.X) + ((-p0.X + p2.X) * t) + (((2 * p0.X) - (5 * p1.X) + (4 * p2.X) - p3.X) * t2) + (((-p0.X) + (3 * p1.X) - (3 * p2.X) + p3.X) * t3)),
                    0.5 * ((2 * p1.Y) + ((-p0.Y + p2.Y) * t) + (((2 * p0.Y) - (5 * p1.Y) + (4 * p2.Y) - p3.Y) * t2) + (((-p0.Y) + (3 * p1.Y) - (3 * p2.Y) + p3.Y) * t3))));
            }
        }

        r.Add(pts[^1]);
        return r;
    }

    private void DrawMarker(ChartMarker? mk, ChartSeries s, double x, double y, int seriesOrdinal)
    {
        string sym = mk?.Symbol ?? "auto";
        if (sym == "none")
        {
            return;
        }

        if (sym == "auto")
        {
            sym = AutoMarkers[seriesOrdinal % AutoMarkers.Length];
        }

        double size = (mk?.Size ?? 5) * PtPx;
        ChartRgb auto = SeriesColor(s);
        ChartRgb fill = FillColor(mk?.Format, auto) ?? new ChartRgb(0, 0, 0, 0);
        ChartRgb? line = LineColor(mk?.Format, auto);
        double lw = LineWidth(mk?.Format, 0.75);
        double r = size / 2;
        switch (sym)
        {
            case "square":
                _c.Rect(x - r, y - r, size, size, fill);
                if (line is { } l1)
                {
                    _c.StrokeRect(x - r, y - r, size, size, lw, l1);
                }

                break;
            case "diamond":
                Poly([(x, y - r - 1), (x + r + 1, y), (x, y + r + 1), (x - r - 1, y)], fill, line, lw);
                break;
            case "triangle":
                Poly([(x, y - r - 1), (x + r + 1, y + r), (x - r - 1, y + r)], fill, line, lw);
                break;
            case "x":
                _c.Line(x - r, y - r, x + r, y + r, lw, line ?? fill);
                _c.Line(x - r, y + r, x + r, y - r, lw, line ?? fill);
                break;
            case "plus":
                _c.Line(x - r, y, x + r, y, lw, line ?? fill);
                _c.Line(x, y - r, x, y + r, lw, line ?? fill);
                break;
            case "star":
                _c.Line(x - r, y - r, x + r, y + r, lw, line ?? fill);
                _c.Line(x - r, y + r, x + r, y - r, lw, line ?? fill);
                _c.Line(x, y - r, x, y + r, lw, line ?? fill);
                break;
            case "dash":
                _c.Rect(x - r, y - (r / 3), size, size / 1.5 / 1.5, fill);
                break;
            case "dot":
                _c.Rect(x - (r / 2), y - (r / 2), r, r, fill);
                break;
            default:
                _c.Circle(x, y, r, fill, line, lw);
                break;
        }
    }

    private void Poly(List<(double X, double Y)> pts, ChartRgb fill, ChartRgb? line, double lw)
    {
        _c.FillPolygon(pts, fill);
        if (line is { } l)
        {
            _c.Polyline(pts, lw, l, null, true);
        }
    }

    private void PaintAreas(ChartGroup g, Pair p, Geo geo)
    {
        List<ChartSeries> series = [.. Ordered(g)];
        double baseV = BaseValue(p);
        for (int si = 0; si < series.Count; si++)
        {
            ChartSeries s = series[si];
            double?[] top = SeriesValues(g, series, si, p);
            double?[] bottom = si > 0 && g.Grouping != ChartGrouping.Standard ? SeriesValues(g, series, si - 1, p) : new double?[p.N];
            var upper = new List<(double X, double Y)>();
            var lower = new List<(double X, double Y)>();
            void Flush()
            {
                if (upper.Count >= 2)
                {
                    var poly = new List<(double X, double Y)>(upper);
                    for (int i = lower.Count - 1; i >= 0; i--)
                    {
                        poly.Add(lower[i]);
                    }

                    ChartRgb? fill = FillColor(s.Format, SeriesColor(s));
                    if (s.Format?.Fill is { Gradient.Count: >= 2 })
                    {
                        fill = Resolve(s.Format.Fill.Gradient[0].Color);
                    }

                    if (fill is { } f)
                    {
                        _c.FillPolygon(poly, f);
                    }

                    if (LineColor(s.Format, null) is { } lc)
                    {
                        _c.Polyline(upper, LineWidth(s.Format, 0.75), lc, s.Format?.Line?.Dash, false);
                    }
                }

                upper.Clear();
                lower.Clear();
            }

            for (int i = 0; i < p.N; i++)
            {
                if (top[i] is not { } v)
                {
                    if (_chart.BlanksAs != ChartBlanksAs.Span)
                    {
                        Flush();
                    }

                    continue;
                }

                double pos = PosOf(p, i);
                upper.Add(geo.Pt(pos, v));
                lower.Add(geo.Pt(pos, bottom[i] ?? baseV));
            }

            Flush();
            for (int i = 0; i < p.N; i++)
            {
                ChartDataLabels? dl = s.Labels ?? g.Labels;
                if (top[i] is { } v && dl is { Deleted: false } && LabelText(dl, s, i, V(s.Values, i) ?? v, p, g) is { } text)
                {
                    (double x, double y) = geo.Pt(PosOf(p, i), v);
                    PlaceLabel(text, dl, x, y, "t", 3, Style(dl.TextFormat, _basePt * 0.9));
                }
            }
        }
    }

    // ---- scatter and bubble ----

    private void PaintScatter(ChartGroup g, Pair p, Box plot, bool bubble)
    {
        List<ChartSeries> series = [.. Ordered(g)];
        double maxSize = bubble ? series.Select(s => s.BubbleSizes).Where(d => d is not null).SelectMany(d => d!.Numbers).Where(x => x.HasValue).Select(x => Math.Abs(x!.Value)).DefaultIfEmpty(1).Max() : 1;
        if (maxSize <= 0)
        {
            maxSize = 1;
        }

        double maxDiam = 0.25 * Math.Min(plot.W, plot.H) * g.BubbleScale / 100.0;
        string style = g.Style ?? "lineMarker";
        for (int si = 0; si < series.Count; si++)
        {
            ChartSeries s = series[si];
            ChartData? xs = s.XValues ?? s.Categories;
            int n = s.Values?.Count ?? 0;
            var pts = new List<(double X, double Y, int I)>();
            for (int i = 0; i < n; i++)
            {
                if (V(s.Values, i) is not { } y)
                {
                    continue;
                }

                double x = V(xs, i) ?? (i + 1);
                pts.Add((plot.X + (p.XScale.Fraction(x) * plot.W), plot.Bottom - (p.Scale.Fraction(y) * plot.H), i));
            }

            ChartRgb color = SeriesColor(s);
            if (!bubble)
            {
                bool drawLine = (style.Contains("line", StringComparison.OrdinalIgnoreCase) || style.Contains("smooth", StringComparison.OrdinalIgnoreCase)) && s.Format?.Line?.None != true;
                if (drawLine && pts.Count >= 2)
                {
                    List<(double X, double Y)> line = [.. pts.Select(q => (q.X, q.Y))];
                    _c.Polyline(s.Smooth || style.Contains("smooth", StringComparison.OrdinalIgnoreCase) ? Smooth(line) : line, LineWidth(s.Format, 2.25), LineColor(s.Format, color) ?? color, s.Format?.Line?.Dash, false);
                }

                bool markers = s.Marker?.Symbol != "none" && (s.Marker is not null || style is "lineMarker" or "marker" or "smoothMarker");
                if (markers)
                {
                    foreach ((double x, double y, int i) in pts)
                    {
                        DrawMarker(s.Points.FirstOrDefault(d => d.Index == i)?.Marker ?? s.Marker, s, x, y, si);
                    }
                }
            }
            else
            {
                foreach ((double x, double y, int i) in pts)
                {
                    double size = Math.Abs(V(s.BubbleSizes, i) ?? 0);
                    double d = maxDiam * Math.Sqrt(size / maxSize);
                    ChartRgb c = PointColor(g, s, i, si);
                    _c.Circle(x, y, d / 2, new ChartRgb(c.R, c.G, c.B, 0.75), c, 0.75 * PtPx);
                }
            }

            ChartDataLabels? dl = s.Labels ?? g.Labels;
            if (dl is { Deleted: false })
            {
                foreach ((double x, double y, int i) in pts)
                {
                    double value = V(s.Values, i) ?? 0;
                    string? text = LabelText(dl, s, i, value, p, g);
                    if (dl.ShowCategoryName && s.XValues is { IsNumeric: true })
                    {
                        text = LabelText(new ChartDataLabels { ShowValue = dl.ShowValue, FormatCode = dl.FormatCode, SourceLinked = dl.SourceLinked, ShowSeriesName = dl.ShowSeriesName, Separator = dl.Separator }, s, i, value, p, g);
                        string xtext = Fmt(V(s.XValues, i) ?? i + 1, s.XValues.FormatCode);
                        text = text is null ? xtext : xtext + (dl.Separator ?? ", ") + text;
                    }

                    if (text is not null)
                    {
                        PlaceLabel(text, dl, x, y, "r", 6, Style(dl.TextFormat, _basePt * 0.9));
                    }
                }
            }
        }
    }

    // ---- stock ----

    private void PaintStock(ChartGroup g, Pair p, Geo geo)
    {
        List<ChartSeries> series = [.. Ordered(g)];
        if (series.Count < 3)
        {
            DrawPlaceholder(new Box(geo.Plot.X, geo.Plot.Y, geo.Plot.W, geo.Plot.H), "A stock chart needs high, low and close series.");
            return;
        }

        bool hasOpen = series.Count >= 4;
        ChartSeries? open = hasOpen ? series[0] : null;
        ChartSeries high = series[hasOpen ? 1 : 0];
        ChartSeries low = series[hasOpen ? 2 : 1];
        ChartSeries close = series[hasOpen ? 3 : 2];
        ChartRgb dark = new(0x40, 0x40, 0x40);
        double barSlots = 1.0 / (1 + (Math.Clamp(g.GapWidth, 0, 500) / 100.0));
        for (int i = 0; i < p.N; i++)
        {
            double? h = V(high.Values, i);
            double? l = V(low.Values, i);
            double? c = V(close.Values, i);
            double? o = open is null ? null : V(open.Values, i);
            double pos = PosOf(p, i);
            if (h is { } hv && l is { } lv && (g.HasHighLowLines || LineColor(high.Format, null) is not null))
            {
                (double x1, double y1) = geo.Pt(pos, hv);
                (double x2, double y2) = geo.Pt(pos, lv);
                _c.Line(x1, y1, x2, y2, LineWidth(high.Format, 0.75), LineColor(high.Format, dark) ?? dark);
            }

            double tick = geo.Slot * 0.2;
            if (c is { } cv)
            {
                (double x, double y) = geo.Pt(pos, cv);
                _c.Line(x, y, geo.Horizontal ? x : x + tick, geo.Horizontal ? y + tick : y, 1.2 * PtPx, dark);
            }

            if (o is { } ov && c is { } cv2)
            {
                if (g.HasUpDownBars)
                {
                    (double xa, double ya) = geo.Pt(pos - (barSlots / 2), ov);
                    (double xb, double yb) = geo.Pt(pos + (barSlots / 2), cv2);
                    bool up = cv2 >= ov;
                    DrawBox(Math.Min(xa, xb), Math.Min(ya, yb), Math.Abs(xb - xa), Math.Abs(yb - ya), null, up ? White : new ChartRgb(0, 0, 0), dark);
                }
                else
                {
                    (double x, double y) = geo.Pt(pos, ov);
                    _c.Line(x, y, geo.Horizontal ? x : x - tick, geo.Horizontal ? y - tick : y, 1.2 * PtPx, dark);
                }
            }
        }
    }

    // ---- surface, flattened to a contour grid ----

    private void PaintSurface(ChartGroup g, Pair p, Geo geo, Box plot)
    {
        _notes.Add(new ChartNote("chart.surface-flattened", "A surface chart is drawn as a top view with contour bands."));
        List<ChartSeries> series = [.. Ordered(g)];
        int cols = p.N;
        int rows = series.Count;
        if (cols < 2 || rows < 2)
        {
            return;
        }

        double cw = plot.W / (cols - 1);
        double rh = plot.H / (rows - 1);
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                double sum = 0;
                int cnt = 0;
                foreach ((int rr, int cc) in new[] { (r, c), (r + 1, c), (r, c + 1), (r + 1, c + 1) })
                {
                    if (V(series[rr].Values, cc) is { } v)
                    {
                        sum += v;
                        cnt++;
                    }
                }

                if (cnt == 0)
                {
                    continue;
                }

                int band = (int)Math.Floor(((sum / cnt) - geo.Scale.Min) / geo.Scale.MajorUnit);
                _c.Rect(plot.X + (c * cw), plot.Bottom - ((r + 1) * rh), cw + 0.5, rh + 0.5, _theme.AutomaticSeriesColor(Math.Max(0, band), 6));
            }
        }
    }

    // ---- trendlines and error bars ----

    private void PaintTrendline(ChartGroup g, Pair p, Geo geo, Box plot, ChartSeries s, ChartTrendline t)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        ChartData? xd = s.XValues ?? s.Categories;
        for (int i = 0; i < (s.Values?.Count ?? 0); i++)
        {
            if (V(s.Values, i) is { } y)
            {
                xs.Add(p.IsXY ? V(xd, i) ?? (i + 1) : i + 1);
                ys.Add(y);
            }
        }

        if (xs.Count < 2)
        {
            return;
        }

        Func<double, double>? f = Fit(t, xs, ys);
        if (f is null)
        {
            return;
        }

        double x0 = xs.Min() - t.Backward;
        double x1 = xs.Max() + t.Forward;
        var pts = new List<(double X, double Y)>();
        const int Samples = 80;
        for (int k = 0; k <= Samples; k++)
        {
            double x = x0 + ((x1 - x0) * k / Samples);
            double y = f(x);
            if (!double.IsFinite(y))
            {
                continue;
            }

            if (p.IsXY)
            {
                pts.Add((plot.X + (p.XScale.Fraction(x) * plot.W), plot.Bottom - (p.Scale.Fraction(y) * plot.H)));
            }
            else
            {
                pts.Add(geo.Pt(x - 0.5 + 0.5, y));
            }
        }

        ChartRgb col = LineColor(t.Format, SeriesColor(s)) ?? SeriesColor(s);
        _c.Polyline(pts, LineWidth(t.Format, 2.25) * 0.75, col, t.Format?.Line?.Dash ?? "sysDot", false);
        _ = g;
    }

    private static Func<double, double>? Fit(ChartTrendline t, List<double> xs, List<double> ys)
    {
        int n = xs.Count;
        switch (t.Type)
        {
            case "linear":
                return Poly(xs, ys, 1);
            case "poly":
                return Poly(xs, ys, Math.Clamp(t.Order, 2, 6));
            case "exp":
                {
                    if (ys.Any(y => y <= 0))
                    {
                        return null;
                    }

                    Func<double, double>? lin = Poly(xs, [.. ys.Select(y => Math.Log(y))], 1);
                    return lin is null ? null : x => Math.Exp(lin(x));
                }

            case "log":
                {
                    if (xs.Any(x => x <= 0))
                    {
                        return null;
                    }

                    Func<double, double>? lin = Poly([.. xs.Select(x => Math.Log(x))], ys, 1);
                    return lin is null ? null : x => x > 0 ? lin(Math.Log(x)) : double.NaN;
                }

            case "power":
                {
                    if (xs.Any(x => x <= 0) || ys.Any(y => y <= 0))
                    {
                        return null;
                    }

                    Func<double, double>? lin = Poly([.. xs.Select(x => Math.Log(x))], [.. ys.Select(y => Math.Log(y))], 1);
                    return lin is null ? null : x => x > 0 ? Math.Exp(lin(Math.Log(x))) : double.NaN;
                }

            case "movingAvg":
                {
                    int period = Math.Clamp(t.Order, 2, Math.Max(2, n));
                    return x =>
                    {
                        int idx = xs.FindIndex(v => v >= x - 1e-9);
                        if (idx < period - 1)
                        {
                            return double.NaN;
                        }

                        double sum = 0;
                        for (int k = idx - period + 1; k <= idx; k++)
                        {
                            sum += ys[k];
                        }

                        return sum / period;
                    };
                }
        }

        return null;
    }

    // Least squares polynomial by the normal equations with Gaussian elimination.
    private static Func<double, double>? Poly(List<double> xs, List<double> ys, int degree)
    {
        int m = degree + 1;
        var a = new double[m, m + 1];
        for (int i = 0; i < xs.Count; i++)
        {
            for (int r = 0; r < m; r++)
            {
                for (int c = 0; c < m; c++)
                {
                    a[r, c] += Math.Pow(xs[i], r + c);
                }

                a[r, m] += ys[i] * Math.Pow(xs[i], r);
            }
        }

        for (int col = 0; col < m; col++)
        {
            int piv = col;
            for (int r = col + 1; r < m; r++)
            {
                if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col]))
                {
                    piv = r;
                }
            }

            if (Math.Abs(a[piv, col]) < 1e-12)
            {
                return null;
            }

            if (piv != col)
            {
                for (int c = 0; c <= m; c++)
                {
                    (a[col, c], a[piv, c]) = (a[piv, c], a[col, c]);
                }
            }

            for (int r = 0; r < m; r++)
            {
                if (r == col)
                {
                    continue;
                }

                double f = a[r, col] / a[col, col];
                for (int c = col; c <= m; c++)
                {
                    a[r, c] -= f * a[col, c];
                }
            }
        }

        var coef = new double[m];
        for (int i = 0; i < m; i++)
        {
            coef[i] = a[i, m] / a[i, i];
        }

        return x =>
        {
            double y = 0;
            for (int i = m - 1; i >= 0; i--)
            {
                y = (y * x) + coef[i];
            }

            return y;
        };
    }

    private void PaintErrorBars(ChartGroup g, Pair p, Geo geo, Box plot, ChartSeries s, ChartErrorBars e)
    {
        ChartRgb col = LineColor(e.Format, DefaultText) ?? DefaultText;
        double w = LineWidth(e.Format, 0.75);
        var ys = new List<double>();
        for (int i = 0; i < (s.Values?.Count ?? 0); i++)
        {
            if (V(s.Values, i) is { } y)
            {
                ys.Add(y);
            }
        }

        double mean = ys.Count == 0 ? 0 : ys.Average();
        double sd = ys.Count > 1 ? Math.Sqrt(ys.Sum(y => (y - mean) * (y - mean)) / (ys.Count - 1)) : 0;
        bool isX = e.Direction == "x" && p.IsXY;
        ChartData? xd = s.XValues ?? s.Categories;
        for (int i = 0; i < (s.Values?.Count ?? 0); i++)
        {
            if (V(s.Values, i) is not { } y)
            {
                continue;
            }

            double basis = isX ? (V(xd, i) ?? i + 1) : y;
            double plus;
            double minus;
            switch (e.ValueType)
            {
                case "percentage":
                    plus = minus = Math.Abs(basis) * e.Value / 100.0;
                    break;
                case "stdDev":
                    plus = minus = sd * e.Value;
                    break;
                case "stdErr":
                    plus = minus = ys.Count > 0 ? sd / Math.Sqrt(ys.Count) : 0;
                    break;
                case "cust":
                    plus = i < e.Plus.Count ? e.Plus[i] ?? 0 : 0;
                    minus = i < e.Minus.Count ? e.Minus[i] ?? 0 : 0;
                    break;
                default:
                    plus = minus = e.Value;
                    break;
            }

            if (e.BarType == "plus")
            {
                minus = 0;
            }
            else if (e.BarType == "minus")
            {
                plus = 0;
            }

            (double xa, double ya, double xb, double yb) = isX
                ? (plot.X + (p.XScale.Fraction(basis - minus) * plot.W), plot.Bottom - (p.Scale.Fraction(y) * plot.H), plot.X + (p.XScale.Fraction(basis + plus) * plot.W), plot.Bottom - (p.Scale.Fraction(y) * plot.H))
                : Ends(p, geo, plot, i, y, minus, plus, V(xd, i) ?? (i + 1));
            _c.Line(xa, ya, xb, yb, w, col);
            if (e.EndCap)
            {
                double cap = 3;
                if (isX)
                {
                    _c.Line(xa, ya - cap, xa, ya + cap, w, col);
                    _c.Line(xb, yb - cap, xb, yb + cap, w, col);
                }
                else
                {
                    _c.Line(xa - cap, ya, xa + cap, ya, w, col);
                    _c.Line(xb - cap, yb, xb + cap, yb, w, col);
                }
            }
        }

        _ = g;
    }

    private static (double Xa, double Ya, double Xb, double Yb) Ends(Pair p, Geo geo, Box plot, int i, double y, double minus, double plus, double x)
    {
        if (p.IsXY)
        {
            double px = plot.X + (p.XScale.Fraction(x) * plot.W);
            return (px, plot.Bottom - (p.Scale.Fraction(y - minus) * plot.H), px, plot.Bottom - (p.Scale.Fraction(y + plus) * plot.H));
        }

        (double xa, double ya) = geo.Pt(PosOf(p, i), y - minus);
        (double xb, double yb) = geo.Pt(PosOf(p, i), y + plus);
        return (xa, ya, xb, yb);
    }
}
