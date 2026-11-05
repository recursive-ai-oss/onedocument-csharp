using System.Numerics;
using OneFont;
using OneRender.Painting;

namespace RecursiveAI.OneDocument.Charts;

/// <summary>Supplies faces for chart text. The host adapts its font provider to this.</summary>
public interface IChartFonts
{
    /// <summary>Finds a face.</summary>
    /// <param name="family">The family asked for (may be a theme font token such as <c>+mn-lt</c>, which the painter resolves first).</param>
    /// <param name="bold">Bold wanted.</param>
    /// <param name="italic">Italic wanted.</param>
    /// <returns>The face, or null when none is available.</returns>
    FontFace? Resolve(string family, bool bold, bool italic);
}

/// <summary>Formats numbers with an Excel format code. The host adapts the number-format service to this.</summary>
public interface IChartNumberFormatter
{
    /// <summary>Formats a value.</summary>
    /// <param name="value">The number.</param>
    /// <param name="formatCode">The code; null or empty means General.</param>
    /// <returns>The text.</returns>
    string Format(double value, string? formatCode);
}

internal enum HAlign { Left, Center, Right }

internal enum VAlign { Top, Middle, Baseline, Bottom }

// Drawing primitives over the display list in CSS pixels. Strokes are drawn as filled outlines because the list only fills.
internal sealed class ChartCanvas(DisplayList list, IChartFonts? fonts, ChartTheme theme, List<ChartNote> notes)
{
    private readonly ShapedRun _run = new();
    private readonly HashSet<string> _missing = [];
    private uint _id;

    public DisplayList List { get; } = list;

    public ChartTheme Theme { get; } = theme;

    private ItemId Id() => ItemId.Of(0xC0000000u + (++_id), 0, 0);

    public static PaintColor Pc(ChartRgb c, double alpha = 1.0) => new(c.R / 255f, c.G / 255f, c.B / 255f, (float)(c.A * alpha));

    public void BeginFigure(string altText)
    {
        List.BeginTag(Id(), TagRole.Figure, altText);
    }

    public void EndTag() => List.EndTag();

    public void Rect(double x, double y, double w, double h, ChartRgb color)
    {
        if (w <= 0 || h <= 0 || color.A <= 0)
        {
            return;
        }

        List.FillRect(Id(), new RectF((float)x, (float)y, (float)w, (float)h), Pc(color));
    }

    public void Fill(PathBuffer path, ChartRgb color)
    {
        if (path.VerbCount == 0 || color.A <= 0)
        {
            return;
        }

        List.FillPath(Id(), path, Paint.FromColor(Pc(color)));
    }

    public void FillPolygon(IReadOnlyList<(double X, double Y)> pts, ChartRgb color)
    {
        if (pts.Count < 3)
        {
            return;
        }

        var p = new PathBuffer();
        p.MoveTo((float)pts[0].X, (float)pts[0].Y);
        for (int i = 1; i < pts.Count; i++)
        {
            p.LineTo((float)pts[i].X, (float)pts[i].Y);
        }

        p.Close();
        Fill(p, color);
    }

    public void FillGradientRect(double x, double y, double w, double h, IReadOnlyList<ChartGradientStop> stops, double angleDegrees)
    {
        if (w <= 0 || h <= 0 || stops.Count < 2)
        {
            return;
        }

        var gs = new GradientStop[stops.Count];
        for (int i = 0; i < gs.Length; i++)
        {
            gs[i] = new GradientStop((float)stops[i].Position, Pc(Theme.Resolve(stops[i].Color)));
        }

        double a = angleDegrees * Math.PI / 180;
        double cx = x + (w / 2);
        double cy = y + (h / 2);
        double half = (Math.Abs(Math.Cos(a)) * w / 2) + (Math.Abs(Math.Sin(a)) * h / 2);
        Paint paint = List.LinearGradient((float)(cx - (Math.Cos(a) * half)), (float)(cy - (Math.Sin(a) * half)), (float)(cx + (Math.Cos(a) * half)), (float)(cy + (Math.Sin(a) * half)), gs);
        List.FillRect(Id(), new RectF((float)x, (float)y, (float)w, (float)h), paint);
    }

    public void StrokeRect(double x, double y, double w, double h, double width, ChartRgb color)
    {
        var pts = new List<(double, double)> { (x, y), (x + w, y), (x + w, y + h), (x, y + h), (x, y) };
        Polyline(pts, width, color, null, false);
    }

    public void Line(double x1, double y1, double x2, double y2, double width, ChartRgb color, string? dash = null) =>
        Polyline([(x1, y1), (x2, y2)], width, color, dash, false);

    // ECMA-376 Part 1 §20.1.10.48 (ST_PresetLineDashVal): dash and gap lengths in multiples of the line width.
    private static double[]? DashPattern(string? dash) => dash switch
    {
        null or "solid" => null,
        "dot" or "sysDot" => [1, 1],
        "sysDash" => [3, 1],
        "dash" => [4, 3],
        "lgDash" => [8, 3],
        "dashDot" or "sysDashDot" => [4, 3, 1, 3],
        "lgDashDot" => [8, 3, 1, 3],
        "lgDashDotDot" or "sysDashDotDot" => [8, 3, 1, 3, 1, 3],
        _ => null,
    };

    /// <summary>Strokes a polyline as filled quads with round joins; dashes follow the preset.</summary>
    public void Polyline(IReadOnlyList<(double X, double Y)> pts, double width, ChartRgb color, string? dash, bool closed)
    {
        if (pts.Count < 2 || width <= 0 || color.A <= 0)
        {
            return;
        }

        var path = new PathBuffer();
        double hw = width / 2;
        double[]? pattern = DashPattern(dash);
        var segs = new List<((double X, double Y) A, (double X, double Y) B)>();
        int n = pts.Count;
        int last = closed ? n : n - 1;
        if (pattern is null)
        {
            for (int i = 0; i < last; i++)
            {
                segs.Add((pts[i], pts[(i + 1) % n]));
            }
        }
        else
        {
            int pi = 0;
            double remain = pattern[0] * width;
            bool on = true;
            for (int i = 0; i < last && segs.Count < 20000; i++)
            {
                (double X, double Y) a = pts[i];
                (double X, double Y) b = pts[(i + 1) % n];
                double len = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
                double pos = 0;
                while (pos < len && segs.Count < 20000)
                {
                    double take = Math.Min(remain, len - pos);
                    if (on && take > 0)
                    {
                        segs.Add(((a.X + ((b.X - a.X) * pos / len), a.Y + ((b.Y - a.Y) * pos / len)), (a.X + ((b.X - a.X) * (pos + take) / len), a.Y + ((b.Y - a.Y) * (pos + take) / len))));
                    }

                    pos += take;
                    remain -= take;
                    if (remain <= 1e-9)
                    {
                        pi = (pi + 1) % pattern.Length;
                        on = pi % 2 == 0;
                        remain = Math.Max(pattern[pi] * width, 0.01);
                    }
                }
            }
        }

        foreach (((double X, double Y) a, (double X, double Y) b) in segs)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len < 1e-6)
            {
                continue;
            }

            double nx = -dy / len * hw;
            double ny = dx / len * hw;
            path.MoveTo((float)(a.X + nx), (float)(a.Y + ny));
            path.LineTo((float)(b.X + nx), (float)(b.Y + ny));
            path.LineTo((float)(b.X - nx), (float)(b.Y - ny));
            path.LineTo((float)(a.X - nx), (float)(a.Y - ny));
            path.Close();
        }

        // Round joins for solid thick lines.
        if (pattern is null && width > 1.6 && n <= 4000)
        {
            for (int i = closed ? 0 : 1; i < (closed ? n : n - 1); i++)
            {
                Disc(path, pts[i].X, pts[i].Y, hw);
            }
        }

        Fill(path, color);
    }

    private static void Disc(PathBuffer path, double cx, double cy, double r)
    {
        const int N = 12;
        for (int i = 0; i < N; i++)
        {
            double t = 2 * Math.PI * i / N;
            float px = (float)(cx + (Math.Cos(t) * r));
            float py = (float)(cy + (Math.Sin(t) * r));
            if (i == 0)
            {
                path.MoveTo(px, py);
            }
            else
            {
                path.LineTo(px, py);
            }
        }

        path.Close();
    }

    public void Circle(double cx, double cy, double r, ChartRgb fill, ChartRgb? line = null, double lineWidth = 1)
    {
        var path = new PathBuffer();
        PolygonCircle(path, cx, cy, r, 28);
        Fill(path, fill);
        if (line is { } l)
        {
            var pts = new List<(double, double)>();
            for (int i = 0; i <= 28; i++)
            {
                double t = 2 * Math.PI * i / 28;
                pts.Add((cx + (Math.Cos(t) * r), cy + (Math.Sin(t) * r)));
            }

            Polyline(pts, lineWidth, l, null, false);
        }
    }

    private static void PolygonCircle(PathBuffer path, double cx, double cy, double r, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n;
            float px = (float)(cx + (Math.Cos(t) * r));
            float py = (float)(cy + (Math.Sin(t) * r));
            if (i == 0)
            {
                path.MoveTo(px, py);
            }
            else
            {
                path.LineTo(px, py);
            }
        }

        path.Close();
    }

    /// <summary>A pie slice or ring segment; angles in radians clockwise from 12 o'clock.</summary>
    public static List<(double X, double Y)> Sector(double cx, double cy, double inner, double outer, double a0, double a1)
    {
        var pts = new List<(double X, double Y)>();
        int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(a1 - a0) / (Math.PI / 60)));
        for (int i = 0; i <= steps; i++)
        {
            double a = a0 + ((a1 - a0) * i / steps);
            pts.Add((cx + (Math.Sin(a) * outer), cy - (Math.Cos(a) * outer)));
        }

        if (inner <= 0)
        {
            pts.Add((cx, cy));
        }
        else
        {
            for (int i = steps; i >= 0; i--)
            {
                double a = a0 + ((a1 - a0) * i / steps);
                pts.Add((cx + (Math.Sin(a) * inner), cy - (Math.Cos(a) * inner)));
            }
        }

        return pts;
    }

    // ---- text ----

    private FontFace? Face(string family, bool bold, bool italic)
    {
        FontFace? f = fonts?.Resolve(family, bold, italic);
        if (f is null && _missing.Add(family))
        {
            notes.Add(new ChartNote("chart.font-missing", "No font was found for '" + family + "'; its text is measured by estimate and not drawn."));
        }

        return f;
    }

    public (double Width, double Ascent, double Descent) Measure(string text, double size, string family, bool bold, bool italic)
    {
        FontFace? f = Face(family, bold, italic);
        if (f is null)
        {
            return (text.Length * size * 0.5, size * 0.9, size * 0.25);
        }

        double upm = f.UnitsPerEm;
        FontMetrics m = f.Metrics;
        double asc = (m.Ascender + (m.LineGap / 2.0)) * size / upm;
        double desc = -m.Descender * size / upm;
        if (text.Length == 0)
        {
            return (0, asc, desc);
        }

        TextShaping.ShapeRun(text, f, _run, false, null);
        double adv = 0;
        foreach (ShapedGlyph g in _run.Glyphs)
        {
            adv += g.XAdvance;
        }

        return (adv * size / upm, asc, desc);
    }

    /// <summary>Draws one line of text. (x, y) is the anchor: horizontal by <paramref name="h"/>, vertical by <paramref name="v"/>.</summary>
    public void Text(string text, double x, double y, double size, ChartRgb color, string family, bool bold, bool italic, HAlign h, VAlign v, double rotationDegrees = 0)
    {
        if (text.Length == 0 || color.A <= 0)
        {
            return;
        }

        FontFace? f = Face(family, bold, italic);
        (double w, double asc, double desc) = Measure(text, size, family, bold, italic);
        if (f is null)
        {
            return;
        }

        double ox = h switch { HAlign.Center => -w / 2, HAlign.Right => -w, _ => 0 };
        double oy = v switch { VAlign.Top => asc, VAlign.Middle => (asc - desc) / 2, VAlign.Bottom => -desc, _ => 0 };
        TextShaping.ShapeRun(text, f, _run, false, null);
        int n = _run.GlyphCount;
        var ids = new int[n];
        var xs = new float[n];
        var ys = new float[n];
        var cl = new int[n];
        double upm = f.UnitsPerEm;
        double pen = 0;
        ReadOnlySpan<ShapedGlyph> glyphs = _run.Glyphs;
        ReadOnlySpan<ShapedGlyphOrigin> origins = _run.Origins;
        for (int i = 0; i < n; i++)
        {
            ids[i] = glyphs[i].GlyphId;
            xs[i] = (float)(ox + ((pen + glyphs[i].XOffset) * size / upm));
            ys[i] = (float)(oy - (glyphs[i].YOffset * size / upm));
            cl[i] = Math.Clamp(origins[i].Cluster, 0, Math.Max(0, text.Length - 1));
            pen += glyphs[i].XAdvance;
        }

        bool rotated = Math.Abs(rotationDegrees) > 0.01;
        float px = (float)x;
        float py = (float)y;
        if (rotated)
        {
            List.PushTransform(Id(), Matrix3x2.CreateRotation((float)(rotationDegrees * Math.PI / 180)) * Matrix3x2.CreateTranslation(px, py));
            px = 0;
            py = 0;
        }

        List.DrawGlyphRun(Id(), f, (float)size, px, py, Pc(color), ids, xs, ys, cl, text);
        if (rotated)
        {
            List.PopTransform();
        }
    }
}
