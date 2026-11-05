using System.Globalization;

namespace RecursiveAI.OneDocument.Charts;

// Charts on a pair of axes: column, bar, line, area, scatter, bubble, stock, surface, and the cartesian chartex layouts.
internal sealed partial class ChartRenderer
{
    private sealed class Pair
    {
        public ChartAxis Cat = new();
        public ChartAxis Val = new();
        public List<ChartGroup> Groups = [];
        public AxisScale Scale;
        public AxisScale XScale;
        public bool IsXY;
        public bool Secondary;
        public bool Percent;
        public int N;
        public List<string> Labels = [];
        public double[]? DatePos;
        public double DateMin;
        public string? ValueFormatCode;
    }

    private sealed class Geo(Box plot, bool horizontal, int n, bool catReverse, bool valReverse, AxisScale scale)
    {
        public Box Plot { get; } = plot;

        public bool Horizontal { get; } = horizontal;

        public int N { get; } = n;

        public AxisScale Scale { get; set; } = scale;

        public double Slot => (Horizontal ? Plot.H : Plot.W) / Math.Max(1, N);

        // A position along the category axis in slot units (0..N).
        public double Cat(double pos)
        {
            double f = catReverse ? N - pos : pos;
            return Horizontal ? Plot.Bottom - (f * Slot) : Plot.X + (f * Slot);
        }

        public double Val(double v)
        {
            double f = Scale.Fraction(v);
            if (valReverse)
            {
                f = 1 - f;
            }

            return Horizontal ? Plot.X + (f * Plot.W) : Plot.Bottom - (f * Plot.H);
        }

        public (double X, double Y) Pt(double catPos, double v) => Horizontal ? (Val(v), Cat(catPos)) : (Cat(catPos), Val(v));
    }

    private static bool IsXYType(ChartType t) => t is ChartType.Scatter or ChartType.Bubble;

    private static double? V(ChartData? d, int i) => d is { IsNumeric: true } && i >= 0 && i < d.Numbers.Count ? d.Numbers[i] : null;

    private ChartAxis FindAxis(long id, ChartAxisKind kind, ChartAxisPosition pos)
    {
        ChartAxis? a = _chart.Axes.FirstOrDefault(x => x.Id == id);
        return a ?? new ChartAxis { Id = id, Kind = kind, Position = pos, Deleted = false, MajorGridlines = false };
    }

    private List<Pair> BuildPairs()
    {
        var pairs = new List<Pair>();
        foreach (ChartGroup g in _chart.Groups)
        {
            if (g.Type is ChartType.Pie or ChartType.Doughnut or ChartType.Radar or ChartType.Sunburst or ChartType.Treemap or ChartType.Funnel or ChartType.RegionMap or ChartType.Unknown)
            {
                continue;
            }

            bool xy = IsXYType(g.Type);
            ChartAxis cat;
            ChartAxis val;
            if (g.AxisIds.Count >= 2)
            {
                ChartAxis a0 = FindAxis(g.AxisIds[0], xy ? ChartAxisKind.Value : ChartAxisKind.Category, ChartAxisPosition.Bottom);
                ChartAxis a1 = FindAxis(g.AxisIds[1], ChartAxisKind.Value, ChartAxisPosition.Left);
                // The pair may be listed value-first; the value axis is the one that is a value axis (both are, for XY).
                if (!xy && a0.Kind == ChartAxisKind.Value && a1.Kind != ChartAxisKind.Value)
                {
                    (a0, a1) = (a1, a0);
                }

                cat = a0;
                val = a1;
            }
            else
            {
                ChartAxis? c0 = _chart.Axes.FirstOrDefault(a => a.Kind != ChartAxisKind.Value);
                ChartAxis? v0 = _chart.Axes.FirstOrDefault(a => a.Kind == ChartAxisKind.Value);
                cat = c0 ?? new ChartAxis { Id = -1, Kind = ChartAxisKind.Category, Position = ChartAxisPosition.Bottom };
                val = v0 ?? new ChartAxis { Id = -2, Kind = ChartAxisKind.Value, Position = ChartAxisPosition.Left, MajorGridlines = true };
                if (c0 is null && v0 is null)
                {
                    val.MajorGridlines = true;
                }
            }

            Pair? p = pairs.FirstOrDefault(q => q.Cat.Id == cat.Id && q.Val.Id == val.Id);
            if (p is null)
            {
                p = new Pair { Cat = cat, Val = val, IsXY = xy, Secondary = pairs.Count > 0 };
                pairs.Add(p);
            }

            p.Groups.Add(g);
        }

        return pairs;
    }

    // ---- derived data for the chartex layouts that live on axes ----

    private readonly Dictionary<ChartGroup, Derived> _derived = [];

    private sealed class Derived
    {
        public List<string> Labels = [];
        public List<double> Counts = [];
        public List<double> Cumulative = [];
        public List<double> BinEdges = [];
        public List<int> Kinds = [];
        public double Min;
        public double Max;
        public List<BoxStats> Boxes = [];
    }

    internal sealed class BoxStats
    {
        public string Label = string.Empty;
        public int CatIndex;
        public int SeriesIndex;
        public double Min, Q1, Median, Q3, Max, Mean;
        public double LowWhisker, HighWhisker;
        public List<double> Outliers = [];
    }

    private void Prepare(ChartGroup g)
    {
        switch (g.Type)
        {
            case ChartType.Histogram:
            case ChartType.Pareto:
                _derived[g] = BuildHistogram(g);
                break;
            case ChartType.BoxWhisker:
                _derived[g] = BuildBoxes(g);
                break;
            case ChartType.Waterfall:
                _derived[g] = BuildWaterfall(g);
                break;
        }
    }

    private void Range(Pair p, out double min, out double max)
    {
        double lo = double.PositiveInfinity;
        double hi = double.NegativeInfinity;
        void Add(double v)
        {
            if (double.IsFinite(v))
            {
                lo = Math.Min(lo, v);
                hi = Math.Max(hi, v);
            }
        }

        foreach (ChartGroup g in p.Groups)
        {
            switch (g.Type)
            {
                case ChartType.Column:
                case ChartType.Bar:
                case ChartType.Line:
                case ChartType.Area:
                    if (g.Grouping == ChartGrouping.Standard)
                    {
                        foreach (ChartSeries s in g.Series)
                        {
                            for (int i = 0; s.Values is not null && i < s.Values.Count; i++)
                            {
                                if (V(s.Values, i) is { } v)
                                {
                                    Add(v);
                                }
                            }
                        }
                    }
                    else
                    {
                        int n = g.Series.Max(s => s.Values?.Count ?? 0);
                        for (int i = 0; i < n; i++)
                        {
                            double pos = 0, neg = 0;
                            foreach (ChartSeries s in g.Series)
                            {
                                double v = V(s.Values, i) ?? 0;
                                if (v >= 0)
                                {
                                    pos += v;
                                }
                                else
                                {
                                    neg += v;
                                }
                            }

                            Add(pos);
                            Add(neg);
                        }

                        Add(0);
                        if (g.Grouping == ChartGrouping.PercentStacked)
                        {
                            p.Percent = true;
                        }
                    }

                    break;
                case ChartType.Scatter:
                case ChartType.Bubble:
                case ChartType.Stock:
                case ChartType.Surface:
                    foreach (ChartSeries s in g.Series)
                    {
                        for (int i = 0; s.Values is not null && i < s.Values.Count; i++)
                        {
                            if (V(s.Values, i) is { } v)
                            {
                                Add(v);
                            }
                        }
                    }

                    break;
                case ChartType.Waterfall:
                case ChartType.Histogram:
                case ChartType.Pareto:
                case ChartType.BoxWhisker:
                    if (_derived.TryGetValue(g, out Derived? d))
                    {
                        Add(d.Min);
                        Add(d.Max);
                    }

                    break;
            }
        }

        min = double.IsFinite(lo) ? lo : 0;
        max = double.IsFinite(lo) ? hi : 1;
    }

    private static void XRange(Pair p, out double min, out double max)
    {
        min = double.PositiveInfinity;
        max = double.NegativeInfinity;
        foreach (ChartGroup g in p.Groups)
        {
            foreach (ChartSeries s in g.Series)
            {
                ChartData? xs = s.XValues ?? s.Categories;
                int n = s.Values?.Count ?? 0;
                for (int i = 0; i < n; i++)
                {
                    double x = V(xs, i) ?? (i + 1);
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                }
            }
        }

        if (!double.IsFinite(min))
        {
            min = 0;
            max = 1;
        }
    }

    private Box PaintCartesian(Box avail)
    {
        foreach (ChartGroup g in _chart.Groups)
        {
            Prepare(g);
        }

        List<Pair> pairs = BuildPairs();
        if (pairs.Count == 0)
        {
            DrawPlaceholder(avail, "This chart has no plottable groups.");
            return avail;
        }

        Pair main = pairs[0];
        bool horizontal = main.Groups[0].Type == ChartType.Bar;
        foreach (Pair p in pairs)
        {
            p.Percent = false;
            Range(p, out double lo, out double hi);
            p.Scale = AxisScaler.Compute(p.Val, lo, hi, p.Groups.All(g => g.Grouping == ChartGrouping.PercentStacked && g.Type is ChartType.Column or ChartType.Bar or ChartType.Line or ChartType.Area));
            _scales[p.Val.Id] = p.Scale;
            if (p.IsXY)
            {
                XRange(p, out double xlo, out double xhi);
                p.XScale = AxisScaler.Compute(p.Cat, xlo, xhi);
                _scales[p.Cat.Id] = p.XScale;
            }
            else
            {
                FillCategories(p);
            }

            p.ValueFormatCode = p.Val.FormatCode is { Length: > 0 } fc && !p.Val.SourceLinked ? fc
                : p.Groups.SelectMany(g => g.Series).Select(s => s.Values?.FormatCode).FirstOrDefault(f => f is { Length: > 0 });
            if (p.Val.FormatCode is { Length: > 0 } fc2 && (p.Val.SourceLinked is false || p.ValueFormatCode is null))
            {
                p.ValueFormatCode = fc2;
            }

            if (p.Groups.All(g => g.Grouping == ChartGrouping.PercentStacked) && p.Val.FormatCode is null)
            {
                p.ValueFormatCode = "0%";
            }
        }

        // The Pareto cumulative line has its own 0..100 % axis on the right.
        bool pareto = main.Groups.Any(g => g.Type == ChartType.Pareto);

        // ---- room for axis labels and titles ----
        double left = 0, right = 0, top = 0, bottom = 0;
        TextStyle valueStyle = Style(main.Val.TextFormat, _basePt * 0.9);
        TextStyle catStyle = Style(main.Cat.TextFormat, _basePt * 0.9);
        double lh = LineHeight(valueStyle);

        double TitleSpace(ChartAxis a) => a.Title is null ? 0 : LineHeight(Style(a.Title.Text?.Format ?? a.Title.TextFormat, _basePt, true)) + 6;

        // Value axes.
        foreach (Pair p in pairs)
        {
            if (p.Val.Deleted || p.Val.TickLabelPosition == "none")
            {
                continue;
            }

            string[] labels = [.. p.Scale.MajorTicks().Select(t => Fmt(t, p.ValueFormatCode))];
            double wmax = labels.Length == 0 ? 0 : labels.Max(l => TextWidth(l, valueStyle));
            double tick = p.Val.MajorTickMark is "out" or "cross" ? 4 : 0;
            double ext = (horizontal ? lh : wmax) + 5 + tick;
            bool far = p.Val.Crosses == "max" || p.Secondary || (main.Cat.Reverse && !horizontal && p == main);
            if (main.Cat.Reverse && p.Val.Crosses == "max")
            {
                far = false;
            }

            if (horizontal)
            {
                if (far ^ p.Val.Reverse)
                {
                    top += ext;
                }
                else
                {
                    bottom += ext;
                }
            }
            else if (far)
            {
                right += ext;
            }
            else
            {
                left += ext;
            }
        }

        foreach (Pair p in pairs)
        {
            if (!p.Val.Deleted && p.Val.Title is not null)
            {
                bool far = p.Val.Crosses == "max" || p.Secondary;
                double ts = TitleSpace(p.Val);
                if (horizontal)
                {
                    if (far)
                    {
                        top += ts;
                    }
                    else
                    {
                        bottom += ts;
                    }
                }
                else if (far)
                {
                    right += ts;
                }
                else
                {
                    left += ts;
                }
            }
        }

        if (pareto)
        {
            right += 40;
        }

        if (main.IsXY)
        {
            // X value axis labels sit below.
            if (!main.Cat.Deleted && main.Cat.TickLabelPosition != "none")
            {
                bottom += lh + 5 + 4;
            }

            bottom += TitleSpace(main.Cat);
        }

        Box plotOuter = avail;
        ChartManualLayout? ml = _chart.PlotAreaLayout;
        bool manualInner = false;
        if (ml is { X: { } mx, Y: { } my, Width: { } mw, Height: { } mh })
        {
            var box = new Box(_x0 + (mx * _w), _y0 + (my * _h), mw * _w, mh * _h);
            if (ml.InnerTarget)
            {
                manualInner = true;
                plotOuter = box;
            }
            else
            {
                plotOuter = box;
            }
        }

        double innerW = plotOuter.W - left - right;
        Box tentative = manualInner ? plotOuter : new Box(plotOuter.X + left, plotOuter.Y + top, Math.Max(10, innerW), Math.Max(10, plotOuter.H - top - bottom));

        // Category axis: choose how labels are laid out for the slot size, then reserve its height (or width).
        CategoryLabelPlan? plan = null;
        if (!main.IsXY && !main.Cat.Deleted && main.Cat.TickLabelPosition != "none")
        {
            double slot = (horizontal ? tentative.H : tentative.W) / Math.Max(1, main.N);
            plan = PlanCategoryLabels(main, catStyle, slot, horizontal);
            double ext = plan.Extent + 5 + (main.Cat.MajorTickMark is "out" or "cross" ? 4 : 0);
            bool farCat = main.Cat.Crosses == "max" || (main.Val.Reverse && !horizontal);
            if (horizontal)
            {
                left += ext;
            }
            else if (farCat)
            {
                top += ext;
            }
            else
            {
                bottom += ext;
            }
        }

        if (!main.Cat.Deleted && main.Cat.Title is not null && !main.IsXY)
        {
            double ts = TitleSpace(main.Cat);
            if (horizontal)
            {
                left += ts;
            }
            else
            {
                bottom += ts;
            }
        }

        Box plot = manualInner ? plotOuter : new Box(plotOuter.X + left, plotOuter.Y + top, Math.Max(10, plotOuter.W - left - right), Math.Max(10, plotOuter.H - top - bottom));
        // Keep the first and last label from running over the edge of the chart.
        if (!manualInner && !horizontal && plan is not null && plan.Rotation == 0 && left < 20 && main.N > 0)
        {
            double halfLabel = Math.Min(plan.MaxLabelWidth / 2, 40) - (plot.W / main.N / 2);
            if (halfLabel > 0)
            {
                double shift = Math.Min(halfLabel, 20);
                plot = new Box(plot.X + Math.Max(0, shift - left), plot.Y, plot.W - Math.Max(0, shift - left) - Math.Max(0, shift - right), plot.H);
            }
        }

        // Plot area background.
        if (_chart.PlotAreaFormat is { } pf)
        {
            DrawBox(plot.X, plot.Y, plot.W, plot.H, pf, new ChartRgb(0, 0, 0, 0), null);
        }

        // Gridlines first, under the series.
        foreach (Pair p in pairs)
        {
            if (!p.Secondary || p.Val.MajorGridlines)
            {
                DrawGrid(p, plot, horizontal, p == main);
            }
        }

        var order = _chart.Groups.OrderBy(g => g.Type switch { ChartType.Area => 0, ChartType.Column or ChartType.Bar or ChartType.Histogram or ChartType.Waterfall or ChartType.Pareto or ChartType.BoxWhisker => 1, ChartType.Line or ChartType.Stock => 2, _ => 3 }).ToList();
        foreach (ChartGroup g in order)
        {
            Pair? p = pairs.FirstOrDefault(q => q.Groups.Contains(g));
            if (p is null)
            {
                continue;
            }

            var geo = new Geo(plot, horizontal, Math.Max(1, p.N), main.Cat.Reverse, p.Val.Reverse, p.Scale);
            PaintGroup(g, p, geo, plot);
        }

        foreach (Pair p in pairs)
        {
            DrawAxes(p, plot, horizontal, p == main, plan, valueStyle, catStyle);
        }

        if (pareto)
        {
            DrawParetoAxis(plot, valueStyle);
        }

        return plot;
    }

    private void FillCategories(Pair p)
    {
        int n = 0;
        ChartData? cats = null;
        foreach (ChartGroup g in p.Groups)
        {
            if (_derived.TryGetValue(g, out Derived? d))
            {
                n = Math.Max(n, d.Labels.Count);
                if (p.Labels.Count < d.Labels.Count)
                {
                    p.Labels = d.Labels;
                }

                continue;
            }

            foreach (ChartSeries s in g.Series)
            {
                n = Math.Max(n, Math.Max(s.Values?.Count ?? 0, s.Categories?.Count ?? 0));
                cats ??= s.Categories is { Count: > 0 } ? s.Categories : null;
            }

            if (g.Type == ChartType.Surface)
            {
                n = Math.Max(n, g.Series.Max(s => s.Values?.Count ?? 0));
            }
        }

        p.N = Math.Max(1, n);
        if (p.Labels.Count == 0)
        {
            string? code = p.Cat.FormatCode is { Length: > 0 } fc && !p.Cat.SourceLinked ? fc : cats?.FormatCode ?? p.Cat.FormatCode;
            bool isDate = p.Cat.Kind == ChartAxisKind.Date && cats is { IsNumeric: true };
            for (int i = 0; i < p.N; i++)
            {
                if (cats is { IsNumeric: true } && i < cats.Numbers.Count && cats.Numbers[i] is { } d)
                {
                    p.Labels.Add(Fmt(d, code ?? (isDate ? "m/d/yyyy" : null)));
                }
                else
                {
                    p.Labels.Add(CategoryText(cats, i));
                }
            }
        }

        // Date axes place points by their date: ECMA-376 Part 1 §21.2.2.38 (dateAx).
        if (p.Cat.Kind == ChartAxisKind.Date && cats is { IsNumeric: true } && cats.Numbers.Count == p.N && cats.Numbers.All(x => x.HasValue))
        {
            double min = cats.Numbers.Min()!.Value;
            double max = cats.Numbers.Max()!.Value;
            double span = max - min;
            if (span > 0 && span < 100000 && span + 1 > p.N)
            {
                p.DatePos = [.. cats.Numbers.Select(x => x!.Value - min)];
                p.DateMin = min;
                p.N = (int)Math.Round(span) + 1;
            }
        }
    }

    private sealed class CategoryLabelPlan
    {
        public double Rotation;
        public int Skip = 1;
        public double Extent;
        public double MaxLabelWidth;
        public string[][] Lines = [];
    }

    private CategoryLabelPlan PlanCategoryLabels(Pair p, TextStyle ts, double slot, bool verticalAxis)
    {
        double lh = LineHeight(ts);
        var plan = new CategoryLabelPlan();
        int n = Math.Min(p.Labels.Count, p.N);
        double[] widths = [.. p.Labels.Take(n).Select(l => TextWidth(l, ts))];
        double maxW = widths.Length == 0 ? 0 : widths.Max();
        plan.MaxLabelWidth = maxW;
        plan.Lines = [.. p.Labels.Take(n).Select(l => new[] { l })];
        if (verticalAxis)
        {
            plan.Extent = maxW;
            plan.Skip = p.Cat.TickLabelSkip ?? Math.Max(1, (int)Math.Ceiling((lh * 1.1) / Math.Max(1, slot)));
            return plan;
        }

        bool explicitRot = Math.Abs(ts.Rotation) > 0.01 && Math.Abs(ts.Rotation) <= 360;
        if (explicitRot)
        {
            plan.Rotation = ts.Rotation;
        }
        else if (maxW + 6 <= slot)
        {
            plan.Rotation = 0;
        }
        else
        {
            string[][]? wrapped = WrapLabels(p.Labels.Take(n).ToArray(), ts, slot - 6);
            if (wrapped is not null && p.Cat.TickLabelSkip is null)
            {
                plan.Lines = wrapped;
                plan.Rotation = 0;
            }
            else if (lh * 1.3 <= slot * 0.7071)
            {
                plan.Rotation = -45;
            }
            else if (lh * 1.3 <= slot)
            {
                plan.Rotation = -90;
            }
            else
            {
                plan.Rotation = 0;
            }
        }

        double rad = Math.Abs(plan.Rotation) * Math.PI / 180;
        int maxLines = plan.Lines.Length == 0 ? 1 : plan.Lines.Max(l => l.Length);
        plan.Extent = (maxW * Math.Sin(rad)) + (lh * maxLines * Math.Cos(rad)) + (rad > 0 ? lh * 0.3 * Math.Sin(rad) : 0);
        if (plan.Rotation == 0 && plan.Lines.Length > 0 && plan.Lines.Max(l => l.Length) == 1)
        {
            plan.Extent = lh;
        }

        double effectiveSlot = rad == 0 ? slot : Math.Max(1, slot / Math.Max(0.2, Math.Sin(rad)));
        plan.Skip = p.Cat.TickLabelSkip ?? (plan.Rotation == 0 && plan.Lines.Max(l => l.Length) == 1 ? AxisScaler.LabelSkip(n, slot, maxW + 6) : rad > 0 && rad < Math.PI / 2 - 0.01 ? Math.Max(1, (int)Math.Ceiling(lh * 1.2 / (slot * 0.7071))) : 1);
        _ = effectiveSlot;
        return plan;
    }

    // Wraps each label at spaces into at most three lines of at most maxWidth; null when a single word is wider.
    private string[][]? WrapLabels(string[] labels, TextStyle ts, double maxWidth)
    {
        var result = new string[labels.Length][];
        for (int i = 0; i < labels.Length; i++)
        {
            string[] words = labels[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                result[i] = [string.Empty];
                continue;
            }

            var lines = new List<string>();
            string cur = words[0];
            if (TextWidth(cur, ts) > maxWidth)
            {
                return null;
            }

            for (int k = 1; k < words.Length; k++)
            {
                if (TextWidth(words[k], ts) > maxWidth)
                {
                    return null;
                }

                string trial = cur + " " + words[k];
                if (TextWidth(trial, ts) <= maxWidth)
                {
                    cur = trial;
                }
                else
                {
                    lines.Add(cur);
                    cur = words[k];
                }
            }

            lines.Add(cur);
            if (lines.Count > 3)
            {
                return null;
            }

            result[i] = [.. lines];
        }

        return result;
    }

    private void DrawGrid(Pair p, Box plot, bool horizontal, bool isMain)
    {
        _ = isMain;
        var geo = new Geo(plot, horizontal, Math.Max(1, p.N), false, p.Val.Reverse, p.Scale);
        if (p.IsXY)
        {
            if (p.Cat.MajorGridlines)
            {
                var xs = new Geo(plot, false, 1, false, false, p.XScale);
                foreach (double t in p.XScale.MajorTicks())
                {
                    double x = plot.X + (p.XScale.Fraction(t) * plot.W);
                    GridLine(x, plot.Y, x, plot.Bottom, p.Cat.MajorGridlineFormat);
                }

                _ = xs;
            }
        }

        if (p.Val.MinorGridlines)
        {
            foreach (double t in p.Scale.MinorTicks())
            {
                double c = geo.Val(t);
                if (horizontal)
                {
                    GridLine(c, plot.Y, c, plot.Bottom, p.Val.MinorGridlineFormat, 0.5);
                }
                else
                {
                    GridLine(plot.X, c, plot.Right, c, p.Val.MinorGridlineFormat, 0.5);
                }
            }
        }

        if (p.Val.MajorGridlines)
        {
            foreach (double t in p.Scale.MajorTicks())
            {
                double c = geo.Val(t);
                if (horizontal)
                {
                    GridLine(c, plot.Y, c, plot.Bottom, p.Val.MajorGridlineFormat);
                }
                else
                {
                    GridLine(plot.X, c, plot.Right, c, p.Val.MajorGridlineFormat);
                }
            }
        }

        if (p.Cat.MajorGridlines && !p.IsXY)
        {
            for (int i = 0; i <= p.N; i++)
            {
                double c = new Geo(plot, horizontal, p.N, false, false, p.Scale).Cat(i);
                if (horizontal)
                {
                    GridLine(plot.X, c, plot.Right, c, p.Cat.MajorGridlineFormat);
                }
                else
                {
                    GridLine(c, plot.Y, c, plot.Bottom, p.Cat.MajorGridlineFormat);
                }
            }
        }
    }

    private void GridLine(double x1, double y1, double x2, double y2, ChartShapeFormat? f, double defaultPt = 0.75)
    {
        ChartRgb? col = LineColor(f, GridColor);
        if (col is { } c)
        {
            _c.Line(x1, y1, x2, y2, LineWidth(f, defaultPt), c, f?.Line?.Dash);
        }
    }

    private void DrawAxes(Pair p, Box plot, bool horizontal, bool isMain, CategoryLabelPlan? plan, TextStyle valueStyle, TextStyle catStyle)
    {
        var geo = new Geo(plot, horizontal, Math.Max(1, p.N), p.Secondary ? false : p.Cat.Reverse, p.Val.Reverse, p.Scale);
        ChartRgb axisColor = LineColor(p.Cat.Format, GridColor) ?? new ChartRgb(0, 0, 0, 0);
        double axisW = LineWidth(p.Cat.Format, 0.75);
        double crossVal = p.Cat.CrossesAt ?? p.Cat.Crosses switch
        {
            "min" => p.Scale.Min,
            "max" => p.Scale.Max,
            _ => Math.Clamp(p.Scale.IsLog ? p.Scale.Min : 0, p.Scale.Min, p.Scale.Max),
        };
        if (p.IsXY)
        {
            crossVal = p.Cat.CrossesAt ?? Math.Clamp(0, p.Scale.Min, p.Scale.Max);
        }

        // ---- value axis ----
        bool farVal = p.Val.Crosses == "max" || p.Secondary || (p.Cat.Reverse && p.Val.Crosses != "max" && !horizontal && isMain);
        if (p.Cat.Reverse && p.Val.Crosses == "max" && !p.Secondary)
        {
            farVal = false;
        }

        if (!p.Val.Deleted)
        {
            ChartRgb vcol = LineColor(p.Val.Format, null) ?? new ChartRgb(0, 0, 0, 0);
            double vw = LineWidth(p.Val.Format, 0.75);
            bool far = horizontal ? farVal ^ p.Val.Reverse : farVal;
            double axisPos = horizontal ? (far ? plot.Y : plot.Bottom) : (far ? plot.Right : plot.X);
            if (vcol.A > 0)
            {
                if (horizontal)
                {
                    _c.Line(plot.X, axisPos, plot.Right, axisPos, vw, vcol);
                }
                else
                {
                    _c.Line(axisPos, plot.Y, axisPos, plot.Bottom, vw, vcol);
                }
            }

            foreach (double t in p.Scale.MajorTicks())
            {
                double c = geo.Val(t);
                if (p.Val.MajorTickMark is "out" or "cross" && vcol.A > 0)
                {
                    TickAt(horizontal, c, axisPos, far ? 4 : -4 * -1, vcol, vw, far);
                }

                if (p.Val.TickLabelPosition != "none")
                {
                    string label = Fmt(t, p.ValueFormatCode);
                    double off = 4 + (p.Val.MajorTickMark is "out" or "cross" ? 4 : 0);
                    if (horizontal)
                    {
                        DrawText(label, c, far ? axisPos - off : axisPos + off, valueStyle, HAlign.Center, far ? VAlign.Bottom : VAlign.Top);
                    }
                    else
                    {
                        DrawText(label, far ? axisPos + off : axisPos - off, c, valueStyle, far ? HAlign.Left : HAlign.Right, VAlign.Middle);
                    }
                }
            }

            if (p.Val.MinorTickMark is "out" or "cross" && vcol.A > 0)
            {
                foreach (double t in p.Scale.MinorTicks())
                {
                    double c = geo.Val(t);
                    TickAt(horizontal, c, axisPos, far ? 2 : -2 * -1, vcol, vw, far);
                }
            }

            if (p.Val.Title is { } vt)
            {
                TextStyle tst = Style(vt.Text?.Format ?? vt.TextFormat, _basePt, true);
                string text = vt.Text?.Text ?? "Axis Title";
                if (horizontal)
                {
                    DrawText(text, plot.X + (plot.W / 2), far ? plot.Y - (plot.H * 0) - LabelBand(true, valueStyle, far, plot) : plot.Bottom + LabelBand(true, valueStyle, far, plot), tst, HAlign.Center, far ? VAlign.Bottom : VAlign.Top);
                }
                else
                {
                    double tx = far ? plot.Right + LabelBand(false, valueStyle, far, plot) + (LineHeight(tst) * 0.5) : plot.X - LabelBand(false, valueStyle, far, plot) - (LineHeight(tst) * 0.5);
                    DrawText(text, tx, plot.Y + (plot.H / 2), tst, HAlign.Center, VAlign.Middle, far ? 90 : -90);
                }
            }
        }

        if (p.Secondary)
        {
            return;
        }

        // ---- category (or X value) axis ----
        if (p.IsXY)
        {
            DrawXAxis(p, plot, valueStyle, geo);
            return;
        }

        double catPos = geo.Val(crossVal);
        bool low = p.Cat.TickLabelPosition == "low";
        bool high = p.Cat.TickLabelPosition == "high";
        if (!p.Cat.Deleted)
        {
            if (axisColor.A > 0)
            {
                if (horizontal)
                {
                    _c.Line(catPos, plot.Y, catPos, plot.Bottom, axisW, axisColor);
                }
                else
                {
                    _c.Line(plot.X, catPos, plot.Right, catPos, axisW, axisColor);
                }
            }

            bool crossFar = p.Cat.Crosses == "max";
            int n = p.N;
            var g2 = new Geo(plot, horizontal, n, p.Cat.Reverse, p.Val.Reverse, p.Scale);
            int tickSkip = p.Cat.TickMarkSkip ?? 1;
            if (axisColor.A > 0 && p.Cat.MajorTickMark is "out" or "cross" or "in")
            {
                int steps = n + 1;
                for (int i = 0; i < steps; i += Math.Max(1, tickSkip))
                {
                    double c = g2.Cat(i);
                    double dir = p.Cat.MajorTickMark == "in" ? -1 : 1;
                    if (horizontal)
                    {
                        _c.Line(catPos - (4 * dir), c, catPos, c, axisW, axisColor);
                    }
                    else
                    {
                        _c.Line(c, catPos, c, catPos + (4 * dir), axisW, axisColor);
                    }
                }
            }

            if (plan is not null && p.Cat.TickLabelPosition != "none")
            {
                double basePos = low ? (horizontal ? plot.X : plot.Bottom) : high ? (horizontal ? plot.Right : plot.Y) : catPos;
                if (!low && !high && (p.Val.Reverse ^ crossFar) && !horizontal)
                {
                    basePos = catPos;
                }

                double off = 5 + (p.Cat.MajorTickMark is "out" or "cross" ? 4 : 0);
                double lh = LineHeight(catStyle);
                for (int i = 0; i < Math.Min(p.Labels.Count, n); i += Math.Max(1, plan.Skip))
                {
                    double pos = p.DatePos is { } dp && i < dp.Length ? dp[i] + 0.5 : i + 0.5;
                    if (p.DatePos is not null && i >= p.DatePos.Length)
                    {
                        break;
                    }

                    double c = g2.Cat(pos);
                    string[] lines = i < plan.Lines.Length ? plan.Lines[i] : [p.Labels[i]];
                    if (horizontal)
                    {
                        for (int k = 0; k < lines.Length; k++)
                        {
                            DrawText(lines[k], basePos - off, c + ((k - ((lines.Length - 1) / 2.0)) * lh), catStyle, HAlign.Right, VAlign.Middle);
                        }
                    }
                    else if (plan.Rotation == 0)
                    {
                        bool above = high || (!low && (p.Val.Reverse ^ crossFar));
                        for (int k = 0; k < lines.Length; k++)
                        {
                            double ly = above ? basePos - off - ((lines.Length - 1 - k) * lh) : basePos + off + (k * lh);
                            DrawText(lines[k], c, ly, catStyle, HAlign.Center, above ? VAlign.Bottom : VAlign.Top);
                        }
                    }
                    else
                    {
                        // Rotated labels end at the tick, so their right end is anchored there.
                        DrawText(p.Labels[i], c, basePos + off, catStyle, plan.Rotation < 0 ? HAlign.Right : HAlign.Left, VAlign.Middle, plan.Rotation);
                    }
                }
            }

            if (p.Cat.Title is { } ct)
            {
                TextStyle tst = Style(ct.Text?.Format ?? ct.TextFormat, _basePt, true);
                string text = ct.Text?.Text ?? "Axis Title";
                if (horizontal)
                {
                    double ext = (plan?.Extent ?? 0) + 5 + 4;
                    DrawText(text, plot.X - ext - (LineHeight(tst) * 0.5), plot.Y + (plot.H / 2), tst, HAlign.Center, VAlign.Middle, -90);
                }
                else
                {
                    double ext = (plan?.Extent ?? 0) + 5 + 4;
                    DrawText(text, plot.X + (plot.W / 2), plot.Bottom + ext + 3, tst, HAlign.Center, VAlign.Top);
                }
            }
        }
    }

    private double LabelBand(bool horizontalAxis, TextStyle ts, bool far, Box plot)
    {
        _ = far;
        _ = plot;
        // The value labels' extent: line height for a horizontal axis; a typical label width for a vertical one.
        return horizontalAxis ? LineHeight(ts) + 9 : TextWidth("0000", ts) + 9;
    }

    private void TickAt(bool horizontalAxis, double along, double axisPos, double signedLength, ChartRgb color, double width, bool far)
    {
        _ = signedLength;
        double len = 4 * (far ? 1 : -1);
        if (horizontalAxis)
        {
            _c.Line(along, axisPos, along, axisPos - len, width, color);
        }
        else
        {
            _c.Line(axisPos, along, axisPos + len, along, width, color);
        }
    }

    private void DrawXAxis(Pair p, Box plot, TextStyle ts, Geo geo)
    {
        _ = geo;
        if (p.Cat.Deleted)
        {
            return;
        }

        ChartRgb col = LineColor(p.Cat.Format, GridColor) ?? new ChartRgb(0, 0, 0, 0);
        double w = LineWidth(p.Cat.Format, 0.75);
        double yAxis = plot.Bottom - (p.Scale.Fraction(Math.Clamp(p.Cat.CrossesAt ?? 0, p.Scale.Min, p.Scale.Max)) * plot.H);
        if (col.A > 0)
        {
            _c.Line(plot.X, yAxis, plot.Right, yAxis, w, col);
        }

        string? code = p.Cat.FormatCode is { Length: > 0 } fc && !p.Cat.SourceLinked ? fc : p.Groups.SelectMany(g => g.Series).Select(s => s.XValues?.FormatCode).FirstOrDefault(f => f is { Length: > 0 }) ?? p.Cat.FormatCode;
        foreach (double t in p.XScale.MajorTicks())
        {
            double x = plot.X + (p.XScale.Fraction(t) * plot.W);
            if (col.A > 0 && p.Cat.MajorTickMark is "out" or "cross")
            {
                _c.Line(x, yAxis, x, yAxis + 4, w, col);
            }

            if (p.Cat.TickLabelPosition != "none")
            {
                DrawText(Fmt(t, code), x, plot.Bottom + 9, ts, HAlign.Center, VAlign.Top);
            }
        }

        if (p.Cat.Title is { } ct)
        {
            TextStyle tst = Style(ct.Text?.Format ?? ct.TextFormat, _basePt, true);
            DrawText(ct.Text?.Text ?? "Axis Title", plot.X + (plot.W / 2), plot.Bottom + LineHeight(ts) + 14, tst, HAlign.Center, VAlign.Top);
        }
    }
}
