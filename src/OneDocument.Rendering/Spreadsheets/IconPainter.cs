using OneRender.Painting;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>
/// Draws the icons of conditional-format icon sets as filled paths in a unit square scaled to the icon size, so they stay vector in PDF.
/// The shapes follow the icon sets' published appearance (arrows, traffic lights, flags, signs, symbols, stars, ratings, quarters, boxes);
/// the exact colours are approximations.
/// </summary>
internal static class IconPainter
{
    private const uint Red = 0xE0443E;
    private const uint Yellow = 0xF5B623;
    private const uint Green = 0x57A84A;
    private const uint Gray = 0x8C8C8C;
    private const uint DarkGray = 0x4D4D4D;
    private const uint Pink = 0xF2A29B;
    private const uint Black = 0x000000;
    private const uint White = 0xFFFFFF;
    private const double Kappa = 0.5522847498;

    private enum Shape { Circle, TrafficLight, ArrowUp, ArrowDown, ArrowRight, ArrowUpRight, ArrowDownRight, TriangleUp, TriangleDown, Dash, Diamond, Warning, Flag, SymbolCheck, SymbolExclaim, SymbolCross, Star, Bars, Quarters, Box }

    private readonly record struct Icon(Shape Shape, uint Color, int Fill = 0, int Of = 0);

    // ECMA-376 Part 1 §18.18.42 (ST_IconSetType): the set names. Each lists its icons from the lowest level to the highest.
    private static Icon[]? SetIcons(string name)
    {
        switch (name)
        {
            case "3Arrows": return [new(Shape.ArrowDown, Red), new(Shape.ArrowRight, Yellow), new(Shape.ArrowUp, Green)];
            case "3ArrowsGray": return [new(Shape.ArrowDown, Gray), new(Shape.ArrowRight, Gray), new(Shape.ArrowUp, Gray)];
            case "3Flags": return [new(Shape.Flag, Red), new(Shape.Flag, Yellow), new(Shape.Flag, Green)];
            case "3TrafficLights1": return [new(Shape.Circle, Red), new(Shape.Circle, Yellow), new(Shape.Circle, Green)];
            case "3TrafficLights2": return [new(Shape.TrafficLight, Red), new(Shape.TrafficLight, Yellow), new(Shape.TrafficLight, Green)];
            case "3Signs": return [new(Shape.Diamond, Red), new(Shape.Warning, Yellow), new(Shape.Circle, Green)];
            case "3Symbols": return [new(Shape.SymbolCross, Red), new(Shape.SymbolExclaim, Yellow), new(Shape.SymbolCheck, Green)];
            case "3Symbols2": return [new(Shape.SymbolCross, Red, 1), new(Shape.SymbolExclaim, Yellow, 1), new(Shape.SymbolCheck, Green, 1)];
            case "3Stars": return [new(Shape.Star, Yellow, 0, 2), new(Shape.Star, Yellow, 1, 2), new(Shape.Star, Yellow, 2, 2)];
            case "3Triangles": return [new(Shape.TriangleDown, Red), new(Shape.Dash, Yellow), new(Shape.TriangleUp, Green)];
            case "4Arrows": return [new(Shape.ArrowDown, Red), new(Shape.ArrowDownRight, Yellow), new(Shape.ArrowUpRight, Yellow), new(Shape.ArrowUp, Green)];
            case "4ArrowsGray": return [new(Shape.ArrowDown, Gray), new(Shape.ArrowDownRight, Gray), new(Shape.ArrowUpRight, Gray), new(Shape.ArrowUp, Gray)];
            case "4RedToBlack": return [new(Shape.Circle, Black), new(Shape.Circle, Gray), new(Shape.Circle, Pink), new(Shape.Circle, Red)];
            case "4Rating": return [new(Shape.Bars, Gray, 1, 4), new(Shape.Bars, Gray, 2, 4), new(Shape.Bars, Gray, 3, 4), new(Shape.Bars, Gray, 4, 4)];
            case "4TrafficLights": return [new(Shape.Circle, Black), new(Shape.Circle, Red), new(Shape.Circle, Yellow), new(Shape.Circle, Green)];
            case "5Arrows": return [new(Shape.ArrowDown, Red), new(Shape.ArrowDownRight, Yellow), new(Shape.ArrowRight, Yellow), new(Shape.ArrowUpRight, Yellow), new(Shape.ArrowUp, Green)];
            case "5ArrowsGray": return [new(Shape.ArrowDown, Gray), new(Shape.ArrowDownRight, Gray), new(Shape.ArrowRight, Gray), new(Shape.ArrowUpRight, Gray), new(Shape.ArrowUp, Gray)];
            case "5Rating": return [new(Shape.Bars, Gray, 1, 5), new(Shape.Bars, Gray, 2, 5), new(Shape.Bars, Gray, 3, 5), new(Shape.Bars, Gray, 4, 5), new(Shape.Bars, Gray, 5, 5)];
            case "5Quarters": return [new(Shape.Quarters, DarkGray, 0), new(Shape.Quarters, DarkGray, 1), new(Shape.Quarters, DarkGray, 2), new(Shape.Quarters, DarkGray, 3), new(Shape.Quarters, DarkGray, 4)];
            case "5Boxes": return [new(Shape.Box, DarkGray, 0, 4), new(Shape.Box, DarkGray, 1, 4), new(Shape.Box, DarkGray, 2, 4), new(Shape.Box, DarkGray, 3, 4), new(Shape.Box, DarkGray, 4, 4)];
            default: return null;
        }
    }

    /// <summary>Draws one icon. Coordinates are in the current coordinate space of the list (points under the page's scale transform).</summary>
    public static bool Draw(DisplayList list, Func<ItemId> id, string set, int level, double x, double y, double size)
    {
        Icon[]? icons = SetIcons(set);
        if (icons is null)
        {
            return false;
        }

        Icon icon = icons[Math.Clamp(level, 0, icons.Length - 1)];
        var p = new Painter(list, id, x, y, size);
        switch (icon.Shape)
        {
            case Shape.Circle:
                p.Circle(.5, .5, .46, icon.Color);
                if (set.Contains("Traffic", StringComparison.Ordinal) || set == "4RedToBlack")
                {
                    p.Ring(.5, .5, .46, .38, icon.Color == Black ? DarkGray : DarkenOf(icon.Color));
                }

                break;
            case Shape.TrafficLight:
                p.RoundedBox(.1, .02, .8, .96, 0.18, DarkGray);
                p.Circle(.5, .5, .3, icon.Color);
                break;
            case Shape.ArrowUp: p.Poly(Arrow(0), icon.Color); break;
            case Shape.ArrowDown: p.Poly(Arrow(180), icon.Color); break;
            case Shape.ArrowRight: p.Poly(Arrow(90), icon.Color); break;
            case Shape.ArrowUpRight: p.Poly(Arrow(45), icon.Color); break;
            case Shape.ArrowDownRight: p.Poly(Arrow(135), icon.Color); break;
            case Shape.TriangleUp: p.Poly([(.5, .1), (.95, .9), (.05, .9)], icon.Color); break;
            case Shape.TriangleDown: p.Poly([(.05, .1), (.95, .1), (.5, .9)], icon.Color); break;
            case Shape.Dash: p.Poly([(.05, .38), (.95, .38), (.95, .62), (.05, .62)], icon.Color); break;
            case Shape.Diamond: p.Poly([(.5, .03), (.97, .5), (.5, .97), (.03, .5)], icon.Color); break;
            case Shape.Warning:
                p.Poly([(.5, .05), (.97, .92), (.03, .92)], icon.Color);
                p.Poly([(.46, .35), (.54, .35), (.53, .65), (.47, .65)], DarkGray);
                p.Circle(.5, .78, .05, DarkGray);
                break;
            case Shape.Flag:
                p.Poly([(.12, .05), (.24, .05), (.24, .95), (.12, .95)], DarkGray);
                p.Poly([(.24, .1), (.92, .28), (.24, .56)], icon.Color);
                break;
            case Shape.SymbolCheck:
                if (icon.Fill == 0)
                {
                    p.Circle(.5, .5, .46, icon.Color);
                }

                p.Line(.26, .52, .44, .7, .14, icon.Fill == 0 ? White : icon.Color);
                p.Line(.44, .7, .76, .3, .14, icon.Fill == 0 ? White : icon.Color);
                break;
            case Shape.SymbolExclaim:
                if (icon.Fill == 0)
                {
                    p.Circle(.5, .5, .46, icon.Color);
                }

                p.Poly([(.43, .2), (.57, .2), (.55, .58), (.45, .58)], icon.Fill == 0 ? White : icon.Color);
                p.Circle(.5, .74, .07, icon.Fill == 0 ? White : icon.Color);
                break;
            case Shape.SymbolCross:
                if (icon.Fill == 0)
                {
                    p.Circle(.5, .5, .46, icon.Color);
                }

                p.Line(.3, .3, .7, .7, .14, icon.Fill == 0 ? White : icon.Color);
                p.Line(.7, .3, .3, .7, .14, icon.Fill == 0 ? White : icon.Color);
                break;
            case Shape.Star:
                p.Poly(Star(.5, .53, .47, 1), icon.Fill == 0 ? Gray : icon.Color);
                if (icon.Fill == 0)
                {
                    p.Poly(Star(.5, .53, .30, 1), White);
                }
                else if (icon.Fill == 1)
                {
                    p.Poly(Star(.5, .53, .30, 1), White);
                    p.HalfStar(icon.Color);
                }

                break;
            case Shape.Bars:
                for (int i = 0; i < icon.Of; i++)
                {
                    double bw = 0.8 / icon.Of;
                    double h = 0.25 + (0.7 * (i + 1) / icon.Of);
                    p.Poly([(.1 + (i * bw), .95 - h), (.1 + (i * bw) + bw * 0.7, .95 - h), (.1 + (i * bw) + bw * 0.7, .95), (.1 + (i * bw), .95)], i < icon.Fill ? DarkGray : 0xBFBFBF);
                }

                break;
            case Shape.Quarters:
                p.Circle(.5, .5, .46, DarkGray);
                p.Circle(.5, .5, .38, White);
                for (int q = 0; q < icon.Fill; q++)
                {
                    p.Quarter(q, DarkGray);
                }

                break;
            case Shape.Box:
                p.Poly([(.08, .08), (.92, .08), (.92, .92), (.08, .92)], DarkGray);
                p.Poly([(.18, .18), (.82, .18), (.82, .82), (.18, .82)], White);
                if (icon.Fill > 0)
                {
                    double top = .82 - (.64 * icon.Fill / icon.Of);
                    p.Poly([(.18, top), (.82, top), (.82, .82), (.18, .82)], DarkGray);
                }

                break;
        }

        return true;
    }

    private static uint DarkenOf(uint rgb)
    {
        static uint C(uint v) => (uint)(v * 0.7);
        return (C((rgb >> 16) & 255) << 16) | (C((rgb >> 8) & 255) << 8) | C(rgb & 255);
    }

    private static (double, double)[] Arrow(double degrees)
    {
        (double, double)[] up = [(.5, .04), (.96, .52), (.68, .52), (.68, .96), (.32, .96), (.32, .52), (.04, .52)];
        double a = degrees * Math.PI / 180;
        double cos = Math.Cos(a);
        double sin = Math.Sin(a);
        var r = new (double, double)[up.Length];
        for (int i = 0; i < up.Length; i++)
        {
            double dx = up[i].Item1 - .5;
            double dy = up[i].Item2 - .5;
            r[i] = (.5 + (dx * cos) - (dy * sin), .5 + (dx * sin) + (dy * cos));
        }

        return r;
    }

    private static (double, double)[] Star(double cx, double cy, double radius, double _)
    {
        var pts = new (double, double)[10];
        for (int i = 0; i < 10; i++)
        {
            double r = i % 2 == 0 ? radius : radius * 0.42;
            double a = (-Math.PI / 2) + (i * Math.PI / 5);
            pts[i] = (cx + (r * Math.Cos(a)), cy + (r * Math.Sin(a)));
        }

        return pts;
    }

    private readonly struct Painter(DisplayList list, Func<ItemId> id, double x, double y, double size)
    {
        private (float, float) P(double u, double v) => ((float)(x + (u * size)), (float)(y + (v * size)));

        private void Fill(PathBuffer path, uint rgb) => list.FillPath(id(), path, StyleCache.Paint(rgb));

        public void Poly((double, double)[] pts, uint rgb)
        {
            var path = new PathBuffer();
            for (int i = 0; i < pts.Length; i++)
            {
                (float px, float py) = P(pts[i].Item1, pts[i].Item2);
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
            Fill(path, rgb);
        }

        public void Circle(double cx, double cy, double r, uint rgb)
        {
            var path = new PathBuffer();
            Arc(path, cx, cy, r, true);
            Fill(path, rgb);
        }

        public void Ring(double cx, double cy, double rOuter, double rInner, uint rgb)
        {
            var path = new PathBuffer();
            Arc(path, cx, cy, rOuter, true);
            Arc(path, cx, cy, rInner, false);
            list.FillPath(id(), path, StyleCache.Paint(rgb), FillRule.EvenOdd);
        }

        private void Arc(PathBuffer path, double cx, double cy, double r, bool clockwise)
        {
            double k = r * Kappa;
            (float, float)[] c = clockwise
                ? [P(cx + r, cy), P(cx + r, cy + k), P(cx + k, cy + r), P(cx, cy + r), P(cx - k, cy + r), P(cx - r, cy + k), P(cx - r, cy), P(cx - r, cy - k), P(cx - k, cy - r), P(cx, cy - r), P(cx + k, cy - r), P(cx + r, cy - k)]
                : [P(cx + r, cy), P(cx + r, cy - k), P(cx + k, cy - r), P(cx, cy - r), P(cx - k, cy - r), P(cx - r, cy - k), P(cx - r, cy), P(cx - r, cy + k), P(cx - k, cy + r), P(cx, cy + r), P(cx + k, cy + r), P(cx + r, cy + k)];
            path.MoveTo(c[0].Item1, c[0].Item2);
            for (int i = 0; i < 4; i++)
            {
                (float, float) a = c[(i * 3) + 1];
                (float, float) b = c[(i * 3) + 2];
                (float, float) e = c[((i * 3) + 3) % 12];
                path.CubicTo(a.Item1, a.Item2, b.Item1, b.Item2, e.Item1, e.Item2);
            }

            path.Close();
        }

        public void RoundedBox(double bx, double by, double bw, double bh, double radius, uint rgb)
        {
            var path = new PathBuffer();
            double k = radius * (1 - Kappa);
            (float, float) p;
            p = P(bx + radius, by);
            path.MoveTo(p.Item1, p.Item2);
            p = P(bx + bw - radius, by);
            path.LineTo(p.Item1, p.Item2);
            Corner(path, bx + bw - k, by, bx + bw, by + k, bx + bw, by + radius);
            p = P(bx + bw, by + bh - radius);
            path.LineTo(p.Item1, p.Item2);
            Corner(path, bx + bw, by + bh - k, bx + bw - k, by + bh, bx + bw - radius, by + bh);
            p = P(bx + radius, by + bh);
            path.LineTo(p.Item1, p.Item2);
            Corner(path, bx + k, by + bh, bx, by + bh - k, bx, by + bh - radius);
            p = P(bx, by + radius);
            path.LineTo(p.Item1, p.Item2);
            Corner(path, bx, by + k, bx + k, by, bx + radius, by);
            path.Close();
            Fill(path, rgb);
        }

        private void Corner(PathBuffer path, double c1x, double c1y, double c2x, double c2y, double ex, double ey)
        {
            (float, float) a = P(c1x, c1y);
            (float, float) b = P(c2x, c2y);
            (float, float) e = P(ex, ey);
            path.CubicTo(a.Item1, a.Item2, b.Item1, b.Item2, e.Item1, e.Item2);
        }

        public void Line(double x1, double y1, double x2, double y2, double w, uint rgb)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len <= 0)
            {
                return;
            }

            double nx = -dy / len * w / 2;
            double ny = dx / len * w / 2;
            Poly([(x1 + nx, y1 + ny), (x2 + nx, y2 + ny), (x2 - nx, y2 - ny), (x1 - nx, y1 - ny)], rgb);
        }

        public void HalfStar(uint rgb)
        {
            // The left half of the star in colour: the star clipped to x < 0.5.
            (double, double)[] star = Star(.5, .53, .47, 1);
            var half = new List<(double, double)>();
            for (int i = 0; i < star.Length; i++)
            {
                (double ax, double ay) = star[i];
                (double bx, double by) = star[(i + 1) % star.Length];
                if (ax <= .5)
                {
                    half.Add((ax, ay));
                }

                if ((ax < .5 && bx > .5) || (ax > .5 && bx < .5))
                {
                    double t = (.5 - ax) / (bx - ax);
                    half.Add((.5, ay + (t * (by - ay))));
                }
            }

            if (half.Count >= 3)
            {
                Poly([.. half], rgb);
            }
        }

        public void Quarter(int index, uint rgb)
        {
            // Quarter 0 is the top right, then clockwise.
            double a0 = (-Math.PI / 2) + (index * Math.PI / 2);
            double a1 = a0 + (Math.PI / 2);
            double r = .38;
            var path = new PathBuffer();
            (float, float) c = P(.5, .5);
            path.MoveTo(c.Item1, c.Item2);
            (float, float) s = P(.5 + (r * Math.Cos(a0)), .5 + (r * Math.Sin(a0)));
            path.LineTo(s.Item1, s.Item2);
            double k = 4.0 / 3.0 * Math.Tan((a1 - a0) / 4) * r;
            (float, float) c1 = P(.5 + (r * Math.Cos(a0)) - (k * Math.Sin(a0)), .5 + (r * Math.Sin(a0)) + (k * Math.Cos(a0)));
            (float, float) c2 = P(.5 + (r * Math.Cos(a1)) + (k * Math.Sin(a1)), .5 + (r * Math.Sin(a1)) - (k * Math.Cos(a1)));
            (float, float) e = P(.5 + (r * Math.Cos(a1)), .5 + (r * Math.Sin(a1)));
            path.CubicTo(c1.Item1, c1.Item2, c2.Item1, c2.Item2, e.Item1, e.Item2);
            path.Close();
            Fill(path, rgb);
        }
    }
}
