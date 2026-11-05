using System.Globalization;
using OneRender.Painting;

namespace RecursiveAI.OneDocument.Charts;

// The layout and painting of one chart. Sizes in CSS pixels; Office sizes in points convert with PixelsPerPoint.
internal sealed partial class ChartRenderer
{
    private const double PtPx = ChartPainter.PixelsPerPoint;
    private static readonly ChartRgb DefaultText = new(0x59, 0x59, 0x59);
    private static readonly ChartRgb GridColor = new(0xD9, 0xD9, 0xD9);
    private static readonly ChartRgb White = new(255, 255, 255);

    private readonly ChartSpace _chart;
    private readonly ChartCanvas _c;
    private readonly ChartPaintOptions _opt;
    private readonly ChartTheme _theme;
    private readonly double _x0;
    private readonly double _y0;
    private readonly double _w;
    private readonly double _h;
    private readonly List<ChartNote> _notes;
    private readonly Dictionary<long, AxisScale> _scales = [];
    private readonly double _basePt;

    public ChartRenderer(ChartSpace chart, ChartCanvas canvas, ChartPaintOptions opt, double x, double y, double w, double h, List<ChartNote> notes)
    {
        _chart = chart;
        _c = canvas;
        _opt = opt;
        _theme = canvas.Theme;
        _x0 = x;
        _y0 = y;
        _w = w;
        _h = h;
        _notes = notes;
        _basePt = chart.TextFormat?.SizePoints ?? 10;
    }

    private struct TextStyle
    {
        public double Size;
        public ChartRgb Color;
        public bool Bold;
        public bool Italic;
        public string Family;
        public double Rotation;
    }

    private TextStyle Style(ChartTextFormat? specific, double defaultPt, bool defaultBold = false)
    {
        ChartTextFormat? g = _chart.TextFormat;
        string family = specific?.Font ?? g?.Font ?? "+mn-lt";
        family = family switch { "+mn-lt" or "+mn-ea" or "+mn-cs" => _theme.MinorFont, "+mj-lt" or "+mj-ea" or "+mj-cs" => _theme.MajorFont, _ => family };
        ChartColor? col = specific?.Color ?? g?.Color;
        return new TextStyle
        {
            Size = (specific?.SizePoints ?? defaultPt) * PtPx,
            Color = col is null ? DefaultText : _theme.Resolve(col),
            Bold = specific?.Bold ?? defaultBold,
            Italic = specific?.Italic ?? false,
            Family = family,
            Rotation = specific?.RotationDegrees ?? 0,
        };
    }

    private double TextWidth(string s, in TextStyle t) => _c.Measure(s, t.Size, t.Family, t.Bold, t.Italic).Width;

    private double LineHeight(in TextStyle t)
    {
        (_, double a, double d) = _c.Measure("Ag", t.Size, t.Family, t.Bold, t.Italic);
        return a + d;
    }

    private void DrawText(string s, double x, double y, in TextStyle t, HAlign h, VAlign v, double rotation = 0) =>
        _c.Text(s, x, y, t.Size, t.Color, t.Family, t.Bold, t.Italic, h, v, rotation);

    private string Fmt(double v, string? code)
    {
        if (_opt.Numbers is { } n)
        {
            return n.Format(v, code);
        }

        return v.ToString("G10", CultureInfo.InvariantCulture);
    }

    // ---- colours and fills ----

    private ChartRgb Resolve(ChartColor c) => _theme.Resolve(c);

    private ChartRgb SeriesColor(ChartSeries s) => FillColor(s.Format, _theme.AutomaticSeriesColor(s.Index, 6)) ?? _theme.AutomaticSeriesColor(s.Index, 6);

    private ChartRgb PointColor(ChartGroup g, ChartSeries s, int pointIndex, int seriesColorIndexFallback)
    {
        ChartDataPoint? dp = s.Points.FirstOrDefault(p => p.Index == pointIndex && p.Format?.Fill is not null);
        if (dp?.Format?.Fill is { } f)
        {
            if (f.None)
            {
                return new ChartRgb(0, 0, 0, 0);
            }

            if (f.Solid is { } c)
            {
                return Resolve(c);
            }

            if (f.Gradient.Count > 0)
            {
                return Resolve(f.Gradient[0].Color);
            }
        }

        if (g.VaryColors)
        {
            return _theme.AutomaticSeriesColor(pointIndex, 6);
        }

        _ = seriesColorIndexFallback;
        return SeriesColor(s);
    }

    private ChartRgb? FillColor(ChartShapeFormat? f, ChartRgb auto)
    {
        if (f?.Fill is not { } fill)
        {
            return auto;
        }

        if (fill.None)
        {
            return null;
        }

        if (fill.Solid is { } c)
        {
            return Resolve(c);
        }

        if (fill.Gradient.Count > 0)
        {
            return Resolve(fill.Gradient[0].Color);
        }

        return auto;
    }

    // Draws a rectangle using the shape's fill (gradients included) and line.
    private void DrawBox(double x, double y, double w, double h, ChartShapeFormat? f, ChartRgb autoFill, ChartRgb? autoLine = null)
    {
        if (w < 0)
        {
            x += w;
            w = -w;
        }

        if (h < 0)
        {
            y += h;
            h = -h;
        }

        if (f?.Fill is { Gradient.Count: >= 2 } g)
        {
            _c.FillGradientRect(x, y, w, h, g.Gradient, g.GradientAngle);
        }
        else if (FillColor(f, autoFill) is { } fc)
        {
            _c.Rect(x, y, w, h, fc);
        }

        ChartRgb? line = LineColor(f, autoLine);
        if (line is { } lc)
        {
            double lw = LineWidth(f, 0.75);
            _c.StrokeRect(x, y, w, h, lw, lc);
        }
    }

    private ChartRgb? LineColor(ChartShapeFormat? f, ChartRgb? auto)
    {
        if (f?.Line is not { } l)
        {
            return auto;
        }

        if (l.None)
        {
            return null;
        }

        return l.Color is { } c ? Resolve(c) : auto;
    }

    private static double LineWidth(ChartShapeFormat? f, double defaultPt) => (f?.Line?.WidthEmu is { } e ? e / 12700.0 : defaultPt) * PtPx;

    // ---- the whole chart ----

    public ChartLayoutInfo Render()
    {
        var area = new RectF((float)_x0, (float)_y0, (float)_w, (float)_h);
        bool tagged = _opt.Tagged;
        if (tagged)
        {
            _c.BeginFigure(_chart.DescribeForAccessibility());
        }

        // Chart area: white with a hairline unless the file says otherwise. Office draws this when c:spPr is absent.
        DrawBox(_x0, _y0, _w, _h, _chart.ChartAreaFormat, White, _chart.ChartAreaFormat is null ? GridColor : null);

        double pad = 7 * PtPx / 1.0 * 0.75;
        var avail = new Box(_x0 + pad, _y0 + pad, _w - (2 * pad), _h - (2 * pad));

        Box titleBox = default;
        string? titleText = TitleText();
        if (titleText is not null && _chart.Title is { } title)
        {
            TextStyle ts = Style(title.Text?.Format ?? title.TextFormat, Math.Round(_basePt * 1.4), false);
            string[] lines = titleText.Split('\n');
            double tw = lines.Max(l => TextWidth(l, ts));
            double lh = LineHeight(ts);
            double th = lh * lines.Length;
            double tx = avail.X + ((avail.W - tw) / 2);
            double ty = avail.Y;
            if (title.Layout is { X: { } lx, Y: { } ly })
            {
                tx = _x0 + (lx * _w);
                ty = _y0 + (ly * _h);
            }
            else if (!title.Overlay)
            {
                avail = new Box(avail.X, avail.Y + th + (pad * 0.6), avail.W, avail.H - th - (pad * 0.6));
            }

            titleBox = new Box(tx, ty, tw, th);
            if (title.Format is { } tf)
            {
                DrawBox(tx - 4, ty - 2, tw + 8, th + 4, tf, new ChartRgb(0, 0, 0, 0), null);
            }

            for (int i = 0; i < lines.Length; i++)
            {
                DrawText(lines[i], tx + (tw / 2), ty + (i * lh), ts, HAlign.Center, VAlign.Top);
            }
        }

        Box legendBox = default;
        List<LegendEntry> entries = _chart.Legend is null ? [] : LegendEntries();
        Legend? legendPlan = null;
        if (_chart.Legend is { } lg && entries.Count > 0)
        {
            legendPlan = PlanLegend(lg, entries, ref avail, pad);
            legendBox = legendPlan.Box;
        }

        Box plot = default;
        ChartGroup? first = _chart.Groups.Count > 0 ? _chart.Groups[0] : null;
        if (first is null)
        {
            DrawPlaceholder(avail, "This chart has no data.");
        }
        else
        {
            switch (first.Type)
            {
                case ChartType.Pie:
                case ChartType.Doughnut:
                    plot = PaintPie(avail);
                    break;
                case ChartType.Radar:
                    plot = PaintRadar(avail);
                    break;
                case ChartType.Sunburst:
                    plot = PaintSunburst(avail);
                    break;
                case ChartType.Treemap:
                    plot = PaintTreemap(avail);
                    break;
                case ChartType.Funnel:
                    plot = PaintFunnel(avail);
                    break;
                case ChartType.RegionMap:
                case ChartType.Unknown:
                    DrawPlaceholder(avail, first.Note ?? "This chart type is not drawn.");
                    plot = avail;
                    break;
                default:
                    plot = PaintCartesian(avail);
                    break;
            }
        }

        if (legendPlan is not null)
        {
            DrawLegend(legendPlan, entries);
        }

        if (tagged)
        {
            _c.EndTag();
        }

        return new ChartLayoutInfo
        {
            ChartArea = area,
            PlotArea = plot.ToRect(),
            Title = titleBox.ToRect(),
            Legend = legendBox.ToRect(),
            Scales = new Dictionary<long, AxisScale>(_scales),
            Notes = [.. _chart.Notes, .. _notes],
        };
    }

    internal readonly record struct Box(double X, double Y, double W, double H)
    {
        public double Right => X + W;

        public double Bottom => Y + H;

        public RectF ToRect() => new((float)X, (float)Y, (float)Math.Max(0, W), (float)Math.Max(0, H));
    }

    private string? TitleText()
    {
        if (_chart.Title is not { } t)
        {
            return null;
        }

        if (t.Text?.Text is { Length: > 0 } s)
        {
            return s;
        }

        // No text: Office shows the series name for a single-series chart, otherwise "Chart Title".
        if (_chart.AutoTitleDeleted)
        {
            return null;
        }

        int count = _chart.Groups.Sum(g => g.Series.Count);
        return count == 1 && _chart.Groups[0].Series[0].Name?.Text is { Length: > 0 } n ? n : "Chart Title";
    }

    private void DrawPlaceholder(Box b, string text)
    {
        _c.Rect(b.X, b.Y, b.W, b.H, new ChartRgb(0xF2, 0xF2, 0xF2));
        _c.StrokeRect(b.X, b.Y, b.W, b.H, 0.75 * PtPx, GridColor);
        TextStyle ts = Style(null, 11);
        DrawText(text, b.X + (b.W / 2), b.Y + (b.H / 2), ts, HAlign.Center, VAlign.Middle);
        _notes.Add(new ChartNote("chart.placeholder", text));
    }

    // ---- legend ----

    private readonly record struct LegendEntry(string Text, ChartRgb Color, bool IsLine, bool HasMarker);

    private sealed class Legend(Box box, int columns, double rowHeight, double colWidth)
    {
        public Box Box { get; } = box;

        public int Columns { get; } = columns;

        public double RowHeight { get; } = rowHeight;

        public double ColWidth { get; } = colWidth;
    }

    private List<LegendEntry> LegendEntries()
    {
        var list = new List<LegendEntry>();
        int idx = 0;
        ChartLegend legend = _chart.Legend!;
        foreach (ChartGroup g in _chart.Groups)
        {
            bool perPoint = g.Type is ChartType.Pie or ChartType.Doughnut || (g.VaryColors && g.Series.Count == 1 && g.Type is not (ChartType.Line or ChartType.Scatter or ChartType.Radar));
            if (g.Type is ChartType.Sunburst or ChartType.Treemap or ChartType.Histogram or ChartType.BoxWhisker or ChartType.Funnel or ChartType.RegionMap)
            {
                continue;
            }

            if (g.Type is ChartType.Waterfall)
            {
                foreach ((string name, int i) in new[] { ("Increase", 0), ("Decrease", 1), ("Total", 2) })
                {
                    list.Add(new LegendEntry(name, _theme.AutomaticSeriesColor(i, 3), false, false));
                }

                continue;
            }

            if (perPoint && g.Series.Count > 0)
            {
                ChartSeries s = g.Series[0];
                ChartData? cats = s.Categories;
                int n = Math.Max(cats?.Count ?? 0, s.Values?.Count ?? 0);
                for (int i = 0; i < n && i < 500; i++)
                {
                    if (!legend.DeletedEntries.Contains(idx))
                    {
                        list.Add(new LegendEntry(CategoryText(cats, i, g), PointColor(g, s, i, 0), false, false));
                    }

                    idx++;
                }

                continue;
            }

            foreach (ChartSeries s in g.Series.OrderBy(x => x.Order))
            {
                if (!legend.DeletedEntries.Contains(idx) && !s.Hidden)
                {
                    bool line = g.Type is ChartType.Line or ChartType.Scatter or ChartType.Radar or ChartType.Stock;
                    ChartRgb col = line ? (LineColor(s.Format, SeriesColor(s)) ?? SeriesColor(s)) : SeriesColor(s);
                    list.Add(new LegendEntry(s.Name?.Text ?? "Series" + (s.Index + 1).ToString(CultureInfo.InvariantCulture), col, line, line && g.ShowMarkers && s.Marker?.Symbol != "none"));
                }

                idx++;
            }
        }

        return list;
    }

    private Legend PlanLegend(ChartLegend lg, List<LegendEntry> entries, ref Box avail, double pad)
    {
        TextStyle ts = Style(lg.TextFormat, _basePt * 0.9);
        double rowH = LineHeight(ts) + 2;
        double key = lg.Position is ChartLegendPosition.Top or ChartLegendPosition.Bottom ? 14 : 14;
        double maxText = entries.Max(e => TextWidth(e.Text, ts));
        double colW = key + 5 + maxText + 14;
        Box box;
        int cols = 1;
        switch (lg.Position)
        {
            case ChartLegendPosition.Top:
            case ChartLegendPosition.Bottom:
                {
                    double total = entries.Sum(e => key + 5 + TextWidth(e.Text, ts) + 14);
                    double width = Math.Min(avail.W, total);
                    int rows = (int)Math.Ceiling(total / Math.Max(1, avail.W));
                    cols = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)rows));
                    double h = rows * rowH;
                    double y = lg.Position == ChartLegendPosition.Top ? avail.Y : avail.Bottom - h;
                    box = new Box(avail.X + ((avail.W - width) / 2), y, width, h);
                    if (!lg.Overlay && lg.Layout is null)
                    {
                        avail = lg.Position == ChartLegendPosition.Top
                            ? new Box(avail.X, avail.Y + h + (pad * 0.5), avail.W, avail.H - h - (pad * 0.5))
                            : new Box(avail.X, avail.Y, avail.W, avail.H - h - (pad * 0.5));
                    }

                    colW = rows == 1 ? -1 : colW;
                    break;
                }

            default:
                {
                    double h = Math.Min(avail.H, entries.Count * rowH);
                    double w = colW;
                    double x = lg.Position == ChartLegendPosition.Left ? avail.X : avail.Right - w;
                    double y = lg.Position == ChartLegendPosition.TopRight ? avail.Y : avail.Y + ((avail.H - h) / 2);
                    box = new Box(x, y, w, h);
                    if (!lg.Overlay && lg.Layout is null)
                    {
                        avail = lg.Position == ChartLegendPosition.Left
                            ? new Box(avail.X + w + (pad * 0.5), avail.Y, avail.W - w - (pad * 0.5), avail.H)
                            : new Box(avail.X, avail.Y, avail.W - w - (pad * 0.5), avail.H);
                    }

                    break;
                }
        }

        if (lg.Layout is { X: { } lx, Y: { } ly })
        {
            double w = lg.Layout.Width is { } lw ? lw * _w : box.W;
            double h = lg.Layout.Height is { } lh ? lh * _h : box.H;
            box = new Box(_x0 + (lx * _w), _y0 + (ly * _h), w, h);
        }

        return new Legend(box, cols, rowH, colW);
    }

    private void DrawLegend(Legend plan, List<LegendEntry> entries)
    {
        ChartLegend lg = _chart.Legend!;
        TextStyle ts = Style(lg.TextFormat, _basePt * 0.9);
        Box b = plan.Box;
        if (lg.Format is { } f)
        {
            DrawBox(b.X, b.Y, b.W, b.H, f, new ChartRgb(0, 0, 0, 0), null);
        }

        bool horizontal = lg.Position is ChartLegendPosition.Top or ChartLegendPosition.Bottom;
        double x = b.X;
        double y = b.Y;
        double rowH = plan.RowHeight;
        for (int i = 0; i < entries.Count; i++)
        {
            LegendEntry e = entries[i];
            double ew = 14 + 5 + TextWidth(e.Text, ts) + 14;
            if (horizontal && x + ew > b.Right + 0.5 && x > b.X)
            {
                x = b.X;
                y += rowH;
            }
            else if (!horizontal && y + rowH > b.Bottom + 0.5 && i > 0)
            {
                break;
            }

            double cy = y + (rowH / 2);
            if (e.IsLine)
            {
                _c.Line(x, cy, x + 14, cy, 2 * PtPx, e.Color);
                if (e.HasMarker)
                {
                    _c.Circle(x + 7, cy, 2.5 * PtPx, e.Color);
                }
            }
            else
            {
                _c.Rect(x + 3, cy - 3.5, 8, 7, e.Color);
            }

            DrawText(e.Text, x + 14 + 5, cy, ts, HAlign.Left, VAlign.Middle);
            if (horizontal)
            {
                x += ew;
            }
            else
            {
                y += rowH;
            }
        }
    }

    private static string CategoryText(ChartData? cats, int i, ChartGroup? g = null)
    {
        _ = g;
        if (cats is null)
        {
            return (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        if (!cats.IsNumeric)
        {
            return i < cats.Strings.Count && cats.Strings[i] is { } s ? s : (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        return i < cats.Numbers.Count && cats.Numbers[i] is { } d ? d.ToString("G10", CultureInfo.InvariantCulture) : (i + 1).ToString(CultureInfo.InvariantCulture);
    }
}
