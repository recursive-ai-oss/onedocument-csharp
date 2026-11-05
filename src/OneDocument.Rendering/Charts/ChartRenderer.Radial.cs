using System.Globalization;

namespace RecursiveAI.OneDocument.Charts;

// Charts without a pair of axes: pie, doughnut, radar, sunburst, treemap, funnel.
internal sealed partial class ChartRenderer
{
    private static Box Square(Box avail, double shrink)
    {
        double d = Math.Min(avail.W, avail.H) * shrink;
        return new Box(avail.X + ((avail.W - d) / 2), avail.Y + ((avail.H - d) / 2), d, d);
    }

    private Box PaintPie(Box avail)
    {
        ChartGroup g = _chart.Groups[0];
        List<ChartSeries> series = [.. Ordered(g)];
        if (series.Count == 0)
        {
            return avail;
        }

        bool doughnut = g.Type == ChartType.Doughnut;
        bool outside = series.Any(s => (s.Labels ?? g.Labels) is { Deleted: false } dl && (dl.Position is null or "bestFit" or "outEnd") && (dl.ShowValue || dl.ShowCategoryName || dl.ShowPercent || dl.ShowSeriesName)) && !doughnut;
        int maxExplosion = series.Max(s => Math.Max(s.Explosion, s.Points.Select(pt => pt.Explosion ?? 0).DefaultIfEmpty(0).Max()));
        Box plot = (_chart.PlotAreaLayout is { InnerTarget: true, X: { } lx, Y: { } ly, Width: { } lw, Height: { } lh })
            ? new Box(_x0 + (lx * _w), _y0 + (ly * _h), lw * _w, lh * _h)
            : Square(avail, outside ? 0.72 : 0.92);
        double cx = plot.X + (plot.W / 2);
        double cy = plot.Y + (plot.H / 2);
        double rOuter = Math.Min(plot.W, plot.H) / 2 / (1 + (maxExplosion / 100.0));
        double hole = doughnut ? Math.Clamp(g.HoleSize, 10, 90) / 100.0 : 0;
        int rings = doughnut ? series.Count : 1;
        double ringWidth = doughnut ? rOuter * (1 - hole) / rings : rOuter;
        for (int si = 0; si < (doughnut ? series.Count : 1); si++)
        {
            ChartSeries s = series[si];
            int n = s.Values?.Count ?? 0;
            double total = 0;
            for (int i = 0; i < n; i++)
            {
                total += Math.Abs(V(s.Values, i) ?? 0);
            }

            if (total <= 0)
            {
                continue;
            }

            double r1 = doughnut ? (rOuter * hole) + (ringWidth * (si + 1)) : rOuter;
            double r0 = doughnut ? r1 - ringWidth : 0;
            double angle = g.FirstSliceAngle * Math.PI / 180;
            for (int i = 0; i < n; i++)
            {
                double v = Math.Abs(V(s.Values, i) ?? 0);
                if (v <= 0)
                {
                    continue;
                }

                double sweep = v / total * 2 * Math.PI;
                double mid = angle + (sweep / 2);
                ChartDataPoint? dp = s.Points.FirstOrDefault(d => d.Index == i);
                double expl = (dp?.Explosion ?? s.Explosion) / 100.0 * rOuter;
                double ox = Math.Sin(mid) * expl;
                double oy = -Math.Cos(mid) * expl;
                ChartRgb color = PointColor(g, s, i, si);
                var poly = ChartCanvas.Sector(cx + ox, cy + oy, r0, r1, angle, angle + sweep);
                _c.FillPolygon(poly, color);
                ChartShapeFormat? f = dp?.Format ?? s.Format;
                ChartRgb border = LineColor(f, White) ?? new ChartRgb(0, 0, 0, 0);
                if (border.A > 0 && n > 1)
                {
                    _c.Polyline(poly, LineWidth(f, 1.5 * 0.75), border, null, true);
                }

                ChartDataLabels? dl = s.Labels ?? g.Labels;
                if (dl is { Deleted: false } && LabelText(dl, s, i, V(s.Values, i) ?? 0, null, g, v / total) is { } text)
                {
                    TextStyle ts = Style(dl.TextFormat, _basePt * 0.9);
                    string pos = dl.Position ?? (doughnut ? "ctr" : "bestFit");
                    double rr = pos switch { "ctr" => doughnut ? (r0 + r1) / 2 : r1 * 0.6, "inEnd" => r1 * 0.82, _ => r1 + 12 };
                    if (pos == "bestFit")
                    {
                        // Large slices carry the label inside, small ones outside.
                        rr = sweep > 0.5 ? r1 * 0.65 : r1 + 12;
                    }

                    bool inside = rr < r1;
                    ChartRgb tc = dl.TextFormat?.Color is null && inside ? ContrastText(color) : ts.Color;
                    double lx2 = cx + ox + (Math.Sin(mid) * rr);
                    double ly2 = cy + oy - (Math.Cos(mid) * rr);
                    HAlign ha = inside ? HAlign.Center : Math.Sin(mid) >= 0.1 ? HAlign.Left : Math.Sin(mid) <= -0.1 ? HAlign.Right : HAlign.Center;
                    VAlign va = inside ? VAlign.Middle : Math.Cos(mid) > 0.1 ? VAlign.Bottom : Math.Cos(mid) < -0.1 ? VAlign.Top : VAlign.Middle;
                    _c.Text(text, lx2, ly2, ts.Size, tc, ts.Family, ts.Bold, ts.Italic, ha, va);
                }

                angle += sweep;
            }
        }

        return plot;
    }

    private static ChartRgb ContrastText(ChartRgb bg)
    {
        double lum = ((0.299 * bg.R) + (0.587 * bg.G) + (0.114 * bg.B)) / 255;
        return lum > 0.6 ? new ChartRgb(0x40, 0x40, 0x40) : new ChartRgb(255, 255, 255);
    }

    private Box PaintRadar(Box avail)
    {
        ChartGroup g = _chart.Groups[0];
        List<ChartSeries> series = [.. Ordered(g)];
        int n = series.Count == 0 ? 0 : series.Max(s => Math.Max(s.Values?.Count ?? 0, s.Categories?.Count ?? 0));
        if (n < 3)
        {
            DrawPlaceholder(avail, "A radar chart needs at least three categories.");
            return avail;
        }

        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (ChartSeries s in series)
        {
            for (int i = 0; i < n; i++)
            {
                if (V(s.Values, i) is { } v)
                {
                    lo = Math.Min(lo, v);
                    hi = Math.Max(hi, v);
                }
            }
        }

        if (!double.IsFinite(lo))
        {
            lo = 0;
            hi = 1;
        }

        ChartAxis valAx = _chart.Axes.FirstOrDefault(a => a.Kind == ChartAxisKind.Value) ?? new ChartAxis { Kind = ChartAxisKind.Value, MajorGridlines = true };
        AxisScale scale = AxisScaler.Compute(valAx, Math.Min(lo, 0), hi);
        _scales[valAx.Id] = scale;
        TextStyle ts = Style(_chart.Axes.FirstOrDefault(a => a.Kind == ChartAxisKind.Category)?.TextFormat, _basePt * 0.9);
        string[] cats = [.. Enumerable.Range(0, n).Select(i => CategoryText(series[0].Categories, i))];
        double labelW = cats.Max(c => TextWidth(c, ts));
        Box plot = Square(avail, 1);
        double r = (Math.Min(plot.W, plot.H) / 2) - Math.Max(labelW, LineHeight(ts)) - 8;
        r = Math.Max(10, Math.Min(r, Math.Min(plot.W, plot.H) / 2 - LineHeight(ts) - 6));
        double cx = plot.X + (plot.W / 2);
        double cy = plot.Y + (plot.H / 2);
        (double X, double Y) At(int i, double frac) => (cx + (Math.Sin(2 * Math.PI * i / n) * r * frac), cy - (Math.Cos(2 * Math.PI * i / n) * r * frac));
        // Rings at every major tick, spokes to every category.
        foreach (double t in scale.MajorTicks())
        {
            var ring = new List<(double X, double Y)>();
            double f = scale.Fraction(t);
            for (int i = 0; i < n; i++)
            {
                ring.Add(At(i, f));
            }

            _c.Polyline(ring, 0.75 * PtPx, GridColor, null, true);
        }

        for (int i = 0; i < n; i++)
        {
            (double x, double y) = At(i, 1);
            _c.Line(cx, cy, x, y, 0.75 * PtPx, GridColor);
            (double lx, double ly) = At(i, 1.0);
            double s = Math.Sin(2 * Math.PI * i / n);
            double c = Math.Cos(2 * Math.PI * i / n);
            DrawText(cats[i], lx + (s * 6), ly - (c * 6), ts, s > 0.2 ? HAlign.Left : s < -0.2 ? HAlign.Right : HAlign.Center, c > 0.2 ? VAlign.Bottom : c < -0.2 ? VAlign.Top : VAlign.Middle);
        }

        TextStyle vs = Style(valAx.TextFormat, _basePt * 0.9);
        foreach (double t in scale.MajorTicks())
        {
            (double x, double y) = At(0, scale.Fraction(t));
            DrawText(Fmt(t, valAx.FormatCode ?? series[0].Values?.FormatCode), x - 4, y, vs, HAlign.Right, VAlign.Middle);
        }

        string style = g.Style ?? "standard";
        for (int si = 0; si < series.Count; si++)
        {
            ChartSeries s = series[si];
            var pts = new List<(double X, double Y)>();
            for (int i = 0; i < n; i++)
            {
                pts.Add(At(i, scale.Fraction(V(s.Values, i) ?? scale.Min)));
            }

            ChartRgb color = SeriesColor(s);
            if (style == "filled")
            {
                _c.FillPolygon(pts, new ChartRgb(color.R, color.G, color.B, 0.5));
            }

            _c.Polyline(pts, LineWidth(s.Format, 2.25), LineColor(s.Format, color) ?? color, s.Format?.Line?.Dash, true);
            if (style == "marker" || s.Marker is { Symbol: not "none" })
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    DrawMarker(s.Marker, s, pts[i].X, pts[i].Y, si);
                }
            }
        }

        return plot;
    }

    // ---- hierarchies for sunburst and treemap ----

    private sealed class Node
    {
        public string Name = string.Empty;
        public double Value;
        public List<Node> Children = [];
        public Dictionary<string, Node> Index = [];
        public int Depth;
        public int Top;
    }

    private static Node BuildTree(ChartSeries s)
    {
        var root = new Node { Depth = -1 };
        ChartData? cats = s.Categories;
        int n = Math.Max(cats?.Count ?? 0, s.Values?.Count ?? 0);
        int levels = 1 + (cats?.OuterLevels.Count ?? 0);
        var carry = new string?[levels];
        for (int i = 0; i < n; i++)
        {
            Node cur = root;
            for (int k = 0; k < levels; k++)
            {
                // Outermost level first: OuterLevels holds the levels from the one next to the leaf outwards.
                List<string?>? list = k == levels - 1 ? cats?.Strings : cats?.OuterLevels[levels - 2 - k];
                string? name = list is not null && i < list.Count ? list[i] : null;
                if (k < levels - 1)
                {
                    if (name is not null)
                    {
                        carry[k] = name;
                        for (int j = k + 1; j < levels - 1; j++)
                        {
                            carry[j] = null;
                        }
                    }

                    name = carry[k];
                }

                name ??= string.Empty;
                Node? child;
                if (k < levels - 1 && cur.Index.TryGetValue(name, out child))
                {
                    cur = child;
                    continue;
                }

                child = new Node { Name = name, Depth = k, Top = k == 0 ? cur.Children.Count : cur.Top };
                if (k < levels - 1)
                {
                    cur.Index[name] = child;
                }

                cur.Children.Add(child);
                cur = child;
            }

            double v = V(s.Values, i) ?? (s.Values is null ? 1 : 0);
            cur.Value += Math.Max(0, v);
        }

        Sum(root);
        return root;

        static double Sum(Node nd)
        {
            if (nd.Children.Count > 0)
            {
                nd.Value = nd.Children.Sum(Sum);
            }

            return nd.Value;
        }
    }

    private Box PaintSunburst(Box avail)
    {
        ChartGroup g = _chart.Groups[0];
        ChartSeries? s = Ordered(g).FirstOrDefault();
        if (s is null)
        {
            return avail;
        }

        Node root = BuildTree(s);
        int depth = MaxDepth(root);
        if (root.Value <= 0 || depth == 0)
        {
            DrawPlaceholder(avail, "This sunburst chart has no data.");
            return avail;
        }

        Box plot = Square(avail, 0.95);
        double cx = plot.X + (plot.W / 2);
        double cy = plot.Y + (plot.H / 2);
        double r = Math.Min(plot.W, plot.H) / 2;
        double ring = r / depth;
        TextStyle ts = Style(null, _basePt * 0.9);
        void Draw(Node nd, double a0, double a1)
        {
            if (nd.Depth >= 0)
            {
                ChartRgb baseColor = _theme.AutomaticSeriesColor(nd.Top, 6);
                ChartRgb color = nd.Depth == 0 ? baseColor : Lighten(baseColor, nd.Depth * 0.18);
                var poly = ChartCanvas.Sector(cx, cy, nd.Depth * ring, (nd.Depth + 1) * ring, a0, a1);
                _c.FillPolygon(poly, color);
                _c.Polyline(poly, 0.75 * PtPx, White, null, true);
                double mid = (a0 + a1) / 2;
                double arc = (a1 - a0) * (nd.Depth + 0.5) * ring;
                if (nd.Name.Length > 0 && arc > TextWidth(nd.Name, ts) * 0.9 || (a1 - a0) > 0.5)
                {
                    double rr = (nd.Depth + 0.5) * ring;
                    _c.Text(nd.Name, cx + (Math.Sin(mid) * rr), cy - (Math.Cos(mid) * rr), ts.Size, ContrastText(color), ts.Family, false, false, HAlign.Center, VAlign.Middle);
                }
            }

            double start = a0;
            foreach (Node child in nd.Children)
            {
                double sweep = nd.Value <= 0 ? 0 : (a1 - a0) * child.Value / nd.Value;
                if (sweep > 0)
                {
                    Draw(child, start, start + sweep);
                }

                start += sweep;
            }
        }

        Draw(root, 0, 2 * Math.PI);
        return plot;
    }

    private static int MaxDepth(Node n) => n.Children.Count == 0 ? n.Depth + 1 : n.Children.Max(MaxDepth);

    private static ChartRgb Lighten(ChartRgb c, double amount) =>
        new((byte)(c.R + ((255 - c.R) * amount)), (byte)(c.G + ((255 - c.G) * amount)), (byte)(c.B + ((255 - c.B) * amount)), c.A);

    private Box PaintTreemap(Box avail)
    {
        ChartGroup g = _chart.Groups[0];
        ChartSeries? s = Ordered(g).FirstOrDefault();
        if (s is null)
        {
            return avail;
        }

        Node root = BuildTree(s);
        if (root.Value <= 0)
        {
            DrawPlaceholder(avail, "This treemap has no data.");
            return avail;
        }

        TextStyle ts = Style(null, _basePt * 0.9);
        double header = LineHeight(ts) + 3;
        void Layout(Node nd, Box r)
        {
            if (nd.Depth >= 0 && nd.Children.Count == 0)
            {
                ChartRgb color = Lighten(_theme.AutomaticSeriesColor(nd.Top, 6), nd.Depth > 0 ? 0.0 : 0.0);
                _c.Rect(r.X, r.Y, r.W, r.H, color);
                _c.StrokeRect(r.X, r.Y, r.W, r.H, 1.0 * PtPx, White);
                if (r.W > TextWidth(nd.Name, ts) + 6 && r.H > LineHeight(ts) + 2)
                {
                    _c.Text(nd.Name, r.X + 4, r.Y + 3, ts.Size, ContrastText(color), ts.Family, false, false, HAlign.Left, VAlign.Top);
                }

                return;
            }

            Box inner = r;
            if (nd.Depth >= 0)
            {
                ChartRgb color = Lighten(_theme.AutomaticSeriesColor(nd.Top, 6), 0.55);
                _c.Rect(r.X, r.Y, r.W, r.H, color);
                _c.StrokeRect(r.X, r.Y, r.W, r.H, 1.0 * PtPx, White);
                _c.Text(nd.Name, r.X + 4, r.Y + 2, ts.Size, new ChartRgb(0x26, 0x26, 0x26), ts.Family, true, false, HAlign.Left, VAlign.Top);
                inner = new Box(r.X + 2, r.Y + header, Math.Max(0, r.W - 4), Math.Max(0, r.H - header - 2));
            }

            foreach ((Node child, Box cr) in Squarify([.. nd.Children.Where(c => c.Value > 0).OrderByDescending(c => c.Value)], inner))
            {
                Layout(child, cr);
            }
        }

        Layout(root, avail);
        return avail;
    }

    // Squarified treemap layout (Bruls, Huizing, van Wijk 2000).
    private static List<(Node, Box)> Squarify(List<Node> items, Box rect)
    {
        var result = new List<(Node, Box)>();
        double total = items.Sum(i => i.Value);
        if (total <= 0 || rect.W <= 0 || rect.H <= 0)
        {
            return result;
        }

        double scale = rect.W * rect.H / total;
        Box r = rect;
        int idx = 0;
        while (idx < items.Count)
        {
            bool wide = r.W >= r.H;
            double side = wide ? r.H : r.W;
            var row = new List<Node>();
            double rowArea = 0;
            double best = double.PositiveInfinity;
            int j = idx;
            while (j < items.Count)
            {
                double a = items[j].Value * scale;
                double newArea = rowArea + a;
                double minA = Math.Min(row.Select(n => n.Value * scale).DefaultIfEmpty(a).Min(), a);
                double maxA = Math.Max(row.Select(n => n.Value * scale).DefaultIfEmpty(a).Max(), a);
                double worst = Math.Max(side * side * maxA / (newArea * newArea), newArea * newArea / (side * side * minA));
                if (row.Count > 0 && worst > best)
                {
                    break;
                }

                best = worst;
                row.Add(items[j]);
                rowArea = newArea;
                j++;
            }

            double thickness = side <= 0 ? 0 : rowArea / side;
            double pos = 0;
            foreach (Node n in row)
            {
                double len = thickness <= 0 ? 0 : n.Value * scale / thickness;
                result.Add((n, wide ? new Box(r.X, r.Y + pos, thickness, len) : new Box(r.X + pos, r.Y, len, thickness)));
                pos += len;
            }

            r = wide ? new Box(r.X + thickness, r.Y, r.W - thickness, r.H) : new Box(r.X, r.Y + thickness, r.W, r.H - thickness);
            idx = j;
        }

        return result;
    }

    private Box PaintFunnel(Box avail)
    {
        ChartGroup g = _chart.Groups[0];
        ChartSeries? s = Ordered(g).FirstOrDefault();
        int n = s?.Values?.Count ?? 0;
        if (s is null || n == 0)
        {
            DrawPlaceholder(avail, "This funnel chart has no data.");
            return avail;
        }

        TextStyle ts = Style(_chart.Axes.FirstOrDefault()?.TextFormat, _basePt * 0.9);
        double labelW = Enumerable.Range(0, n).Max(i => TextWidth(CategoryText(s.Categories, i), ts)) + 10;
        var plot = new Box(avail.X + labelW, avail.Y, Math.Max(10, avail.W - labelW), avail.H);
        double max = Enumerable.Range(0, n).Max(i => Math.Abs(V(s.Values, i) ?? 0));
        if (max <= 0)
        {
            max = 1;
        }

        double slot = plot.H / n;
        double barH = slot / (1 + 0.06);
        for (int i = 0; i < n; i++)
        {
            double v = Math.Abs(V(s.Values, i) ?? 0);
            double w = plot.W * v / max;
            double y = plot.Y + (i * slot) + ((slot - barH) / 2);
            ChartRgb color = PointColor(g, s, i, 0);
            _c.Rect(plot.X + ((plot.W - w) / 2), y, w, barH, color);
            DrawText(CategoryText(s.Categories, i), plot.X - 6, y + (barH / 2), ts, HAlign.Right, VAlign.Middle);
            ChartDataLabels? dl = s.Labels ?? new ChartDataLabels { ShowValue = true };
            if (!dl.Deleted && LabelText(dl, s, i, V(s.Values, i) ?? 0, null, g) is { } text)
            {
                _c.Text(text, plot.X + (plot.W / 2), y + (barH / 2), ts.Size, ContrastText(color), ts.Family, false, false, HAlign.Center, VAlign.Middle);
            }
        }

        return plot;
    }
}
