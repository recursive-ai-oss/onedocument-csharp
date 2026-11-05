using System.Globalization;

namespace RecursiveAI.OneDocument.Rendering.Drawing;

/// <summary>How a geometry path is filled (ECMA-376 Part 1 §20.1.9.15, ST_PathFillMode).</summary>
public enum PathFillMode
{
    /// <summary>No fill.</summary>
    None = 0,

    /// <summary>The shape's fill, unchanged.</summary>
    Norm = 1,

    /// <summary>The fill, lightened.</summary>
    Lighten = 2,

    /// <summary>The fill, lightened a little.</summary>
    LightenLess = 3,

    /// <summary>The fill, darkened.</summary>
    Darken = 4,

    /// <summary>The fill, darkened a little.</summary>
    DarkenLess = 5,
}

/// <summary>One path of an evaluated geometry, in points relative to the top-left corner of the shape's box.</summary>
public sealed class GeometryPath
{
    internal GeometryPath(OneRender.Painting.PathBuffer path, PathFillMode fill, bool stroke)
    {
        Path = path;
        Fill = fill;
        Stroke = stroke;
    }

    /// <summary>Gets the outline: move, line, quadratic and cubic segments (arcs are already cubic curves).</summary>
    public OneRender.Painting.PathBuffer Path { get; }

    /// <summary>Gets how the path is filled.</summary>
    public PathFillMode Fill { get; }

    /// <summary>Gets whether the path is stroked with the shape's line.</summary>
    public bool Stroke { get; }
}

/// <summary>A shape's geometry evaluated for one size: its paths and the rectangle text is laid out in.</summary>
public sealed class EvaluatedGeometry
{
    internal EvaluatedGeometry(List<GeometryPath> paths, double textLeft, double textTop, double textRight, double textBottom)
    {
        Paths = paths;
        TextLeft = textLeft;
        TextTop = textTop;
        TextRight = textRight;
        TextBottom = textBottom;
    }

    /// <summary>Gets the paths in drawing order.</summary>
    public IReadOnlyList<GeometryPath> Paths { get; }

    /// <summary>Gets the left edge of the text rectangle, in points from the shape's left.</summary>
    public double TextLeft { get; }

    /// <summary>Gets the top edge of the text rectangle, in points from the shape's top.</summary>
    public double TextTop { get; }

    /// <summary>Gets the right edge of the text rectangle, in points from the shape's left.</summary>
    public double TextRight { get; }

    /// <summary>Gets the bottom edge of the text rectangle, in points from the shape's top.</summary>
    public double TextBottom { get; }
}

/// <summary>
/// A shape geometry definition: adjust values, guide formulas, a text rectangle and paths, either one of the ECMA-376 presets
/// (<see cref="Preset"/>) or a custom geometry read from <c>a:custGeom</c> (<see cref="FromCustomGeometry"/>). Evaluating it for a
/// size gives paths in points.
/// </summary>
public sealed class GeometryDefinition
{
    private const double EmuPerPoint = 12700.0;

    private readonly List<(string Name, double Value)> _adjusts = [];
    private readonly List<Guide> _guides = [];
    private readonly List<PathSpec> _paths = [];
    private string[] _rect = ["l", "t", "r", "b"];

    internal GeometryDefinition()
    {
    }

    /// <summary>Gets the names of the 186 preset shapes the standard defines, sorted.</summary>
    public static IReadOnlyList<string> PresetNames => PresetShapeData.Names;

    /// <summary>Gets the names of the adjust values this geometry has, in order.</summary>
    public IEnumerable<string> AdjustNames => _adjusts.Select(a => a.Name);

    /// <summary>Gets the preset geometry with the given name (ECMA-376 Part 1 §20.1.9.18, ST_ShapeType).</summary>
    /// <param name="name">The preset name such as <c>rect</c> or <c>wedgeRoundRectCallout</c>.</param>
    /// <returns>The definition, or null when the standard defines no such preset.</returns>
    public static GeometryDefinition? Preset(string name)
    {
        if (name is null || PresetShapeData.Get(name) is not { } text)
        {
            return null;
        }

        var def = new GeometryDefinition();
        PathSpec? current = null;
        foreach (string line in text.Split('\n'))
        {
            string[] t = line.Split(' ');
            switch (t[0])
            {
                case "A" when t.Length == 3 && current is null:
                    def._adjusts.Add((t[1], Num(t[2])));
                    break;
                case "G":
                    def._guides.Add(new Guide(t[1], t[2], t.Length > 3 ? t[3] : "0", t.Length > 4 ? t[4] : "0", t.Length > 5 ? t[5] : "0"));
                    break;
                case "R":
                    def._rect = [t[1], t[2], t[3], t[4]];
                    break;
                case "P":
                    current = new PathSpec(Num(t[1]), Num(t[2]), t[3] switch
                    {
                        "n" => PathFillMode.None,
                        "l" => PathFillMode.Lighten,
                        "L" => PathFillMode.LightenLess,
                        "d" => PathFillMode.Darken,
                        "D" => PathFillMode.DarkenLess,
                        _ => PathFillMode.Norm,
                    }, t[4] == "1");
                    def._paths.Add(current);
                    break;
                case "M" or "L" or "A" or "Q" or "C" or "Z" when current is not null:
                    current.Commands.Add(new PathCommand(t[0][0], t[1..]));
                    break;
            }
        }

        return def;
    }

    /// <summary>
    /// Reads a custom geometry (<c>a:custGeom</c>, ECMA-376 Part 1 §20.1.9.8): its <c>avLst</c>, <c>gdLst</c>, <c>rect</c> and
    /// <c>pathLst</c>. Connection sites and adjust handles are not used for drawing.
    /// </summary>
    /// <param name="custGeom">The <c>a:custGeom</c> element.</param>
    /// <returns>The definition; a geometry with no paths draws nothing.</returns>
    public static GeometryDefinition FromCustomGeometry(OneDocument.Markup.MarkupNode custGeom)
    {
        var def = new GeometryDefinition();
        foreach (OneDocument.Markup.MarkupNode part in custGeom.ChildElements)
        {
            switch (part.LocalNameString)
            {
                case "avLst":
                    foreach (OneDocument.Markup.MarkupNode gd in part.ChildElements)
                    {
                        string[] f = (gd.GetAttribute("fmla") ?? "val 0").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        def._adjusts.Add((gd.GetAttribute("name") ?? "adj", f.Length > 1 ? Num(f[1]) : 0));
                    }

                    break;
                case "gdLst":
                    foreach (OneDocument.Markup.MarkupNode gd in part.ChildElements)
                    {
                        string[] f = (gd.GetAttribute("fmla") ?? "val 0").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (f.Length > 0)
                        {
                            def._guides.Add(new Guide(gd.GetAttribute("name") ?? "g", f[0], f.Length > 1 ? f[1] : "0", f.Length > 2 ? f[2] : "0", f.Length > 3 ? f[3] : "0"));
                        }
                    }

                    break;
                case "rect":
                    def._rect = [part.GetAttribute("l") ?? "l", part.GetAttribute("t") ?? "t", part.GetAttribute("r") ?? "r", part.GetAttribute("b") ?? "b"];
                    break;
                case "pathLst":
                    foreach (OneDocument.Markup.MarkupNode p in part.ChildElements)
                    {
                        var spec = new PathSpec(
                            Num(p.GetAttribute("w") ?? "0"),
                            Num(p.GetAttribute("h") ?? "0"),
                            (p.GetAttribute("fill") ?? "norm") switch
                            {
                                "none" => PathFillMode.None,
                                "lighten" => PathFillMode.Lighten,
                                "lightenLess" => PathFillMode.LightenLess,
                                "darken" => PathFillMode.Darken,
                                "darkenLess" => PathFillMode.DarkenLess,
                                _ => PathFillMode.Norm,
                            },
                            p.GetAttribute("stroke") is not ("0" or "false"));
                        foreach (OneDocument.Markup.MarkupNode c in p.ChildElements)
                        {
                            string n = c.LocalNameString;
                            var pts = c.ChildElements.Select(pt => new[] { pt.GetAttribute("x") ?? "0", pt.GetAttribute("y") ?? "0" }).SelectMany(a => a).ToArray();
                            switch (n)
                            {
                                case "moveTo": spec.Commands.Add(new PathCommand('M', pts)); break;
                                case "lnTo": spec.Commands.Add(new PathCommand('L', pts)); break;
                                case "quadBezTo": spec.Commands.Add(new PathCommand('Q', pts)); break;
                                case "cubicBezTo": spec.Commands.Add(new PathCommand('C', pts)); break;
                                case "close": spec.Commands.Add(new PathCommand('Z', [])); break;
                                case "arcTo": spec.Commands.Add(new PathCommand('A', [c.GetAttribute("wR") ?? "0", c.GetAttribute("hR") ?? "0", c.GetAttribute("stAng") ?? "0", c.GetAttribute("swAng") ?? "0"])); break;
                            }
                        }

                        def._paths.Add(spec);
                    }

                    break;
            }
        }

        return def;
    }

    /// <summary>Evaluates the geometry for a shape of the given size.</summary>
    /// <param name="widthPoints">The shape's width in points.</param>
    /// <param name="heightPoints">The shape's height in points.</param>
    /// <param name="adjustments">Values from the shape's <c>avLst</c> that replace the defaults, by name (for example <c>adj</c> or <c>adj1</c>); may be null.</param>
    /// <returns>
    /// The paths in points. Every coordinate is finite: a guide that would divide by zero or overflow evaluates to 0, as the standard
    /// leaves such values undefined.
    /// </returns>
    public EvaluatedGeometry Evaluate(double widthPoints, double heightPoints, IReadOnlyDictionary<string, double>? adjustments = null)
    {
        double w = Math.Max(0, widthPoints) * EmuPerPoint;
        double h = Math.Max(0, heightPoints) * EmuPerPoint;
        var env = new Dictionary<string, double>(StringComparer.Ordinal);
        GuideMath.AddBuiltIns(env, w, h);
        foreach ((string name, double value) in _adjusts)
        {
            env[name] = adjustments is not null && adjustments.TryGetValue(name, out double v) ? v : value;
        }

        foreach (Guide g in _guides)
        {
            env[g.Name] = GuideMath.Eval(g, env);
        }

        double Get(string token) => GuideMath.Value(token, env);

        var paths = new List<GeometryPath>(_paths.Count);
        foreach (PathSpec spec in _paths)
        {
            double sx = spec.W > 0 ? w / spec.W : 1.0;
            double sy = spec.H > 0 ? h / spec.H : 1.0;
            var pb = new OneRender.Painting.PathBuffer();
            double cx = 0, cy = 0, startX = 0, startY = 0;
            bool open = false;
            float Px(double v) => (float)(Finite(v) / EmuPerPoint);
            foreach (PathCommand c in spec.Commands)
            {
                string[] a = c.Args;
                switch (c.Op)
                {
                    case 'M':
                        cx = startX = Get(a[0]) * sx;
                        cy = startY = Get(a[1]) * sy;
                        pb.MoveTo(Px(cx), Px(cy));
                        open = true;
                        break;
                    case 'L':
                        EnsureOpen(pb, ref open, cx, cy, Px);
                        cx = Get(a[0]) * sx;
                        cy = Get(a[1]) * sy;
                        pb.LineTo(Px(cx), Px(cy));
                        break;
                    case 'Q':
                        EnsureOpen(pb, ref open, cx, cy, Px);
                        pb.QuadTo(Px(Get(a[0]) * sx), Px(Get(a[1]) * sy), Px(Get(a[2]) * sx), Px(Get(a[3]) * sy));
                        cx = Get(a[2]) * sx;
                        cy = Get(a[3]) * sy;
                        break;
                    case 'C':
                        EnsureOpen(pb, ref open, cx, cy, Px);
                        pb.CubicTo(Px(Get(a[0]) * sx), Px(Get(a[1]) * sy), Px(Get(a[2]) * sx), Px(Get(a[3]) * sy), Px(Get(a[4]) * sx), Px(Get(a[5]) * sy));
                        cx = Get(a[4]) * sx;
                        cy = Get(a[5]) * sy;
                        break;
                    case 'A':
                        EnsureOpen(pb, ref open, cx, cy, Px);
                        ArcTo(pb, ref cx, ref cy, Get(a[0]) * sx, Get(a[1]) * sy, Get(a[2]), Get(a[3]), Px);
                        break;
                    case 'Z':
                        if (open)
                        {
                            pb.Close();
                            cx = startX;
                            cy = startY;
                            open = false;
                        }

                        break;
                }
            }

            paths.Add(new GeometryPath(pb, spec.Fill, spec.Stroke));
        }

        return new EvaluatedGeometry(paths, Get(_rect[0]) / EmuPerPoint, Get(_rect[1]) / EmuPerPoint, Get(_rect[2]) / EmuPerPoint, Get(_rect[3]) / EmuPerPoint);
    }

    private static void EnsureOpen(OneRender.Painting.PathBuffer pb, ref bool open, double cx, double cy, Func<double, float> px)
    {
        // A drawing command after close or at the start continues from the current point.
        if (!open)
        {
            pb.MoveTo(px(cx), px(cy));
            open = true;
        }
    }

    internal static double Finite(double v) => double.IsFinite(v) ? Math.Clamp(v, -1e12, 1e12) : 0;

    private static double Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    // ECMA-376 Part 1 §20.1.9.3 (arcTo): the arc starts at the current point, which lies at angle stAng on an ellipse of radii wR and hR;
    // it sweeps swAng (angles in 60000ths of a degree, clockwise positive because y grows downward). The angle is the direction of the
    // line from the centre to the point, so it is converted to the ellipse's parameter before the arc is cut into cubic curves.
    private static void ArcTo(OneRender.Painting.PathBuffer pb, ref double cx, ref double cy, double wR, double hR, double stAng, double swAng, Func<double, float> px)
    {
        wR = Math.Abs(wR);
        hR = Math.Abs(hR);
        if (wR < 1e-9 || hR < 1e-9 || swAng == 0 || !double.IsFinite(swAng) || !double.IsFinite(stAng))
        {
            return;
        }

        double st = stAng / 60000.0 * Math.PI / 180.0;
        double sw = Math.Clamp(swAng / 60000.0, -360.0, 360.0) * Math.PI / 180.0;
        double t0 = Parametric(st, wR, hR);
        double t1 = Parametric(st + sw, wR, hR);
        // Keep the parameter sweep in the direction (and roughly the length) of the angle sweep.
        double dt = t1 - t0;
        while (sw > 0 && dt < 0)
        {
            dt += 2 * Math.PI;
        }

        while (sw < 0 && dt > 0)
        {
            dt -= 2 * Math.PI;
        }

        if (Math.Abs(sw) >= 2 * Math.PI - 1e-9)
        {
            dt = Math.Sign(sw) * 2 * Math.PI;
        }

        double centreX = cx - (wR * Math.Cos(t0));
        double centreY = cy - (hR * Math.Sin(t0));
        int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(dt) / (Math.PI / 2) - 1e-9));
        double step = dt / n;
        double k = 4.0 / 3.0 * Math.Tan(step / 4);
        double a = t0;
        for (int i = 0; i < n; i++)
        {
            double b = a + step;
            double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b);
            pb.CubicTo(
                px(centreX + (wR * (ca - (k * sa)))), px(centreY + (hR * (sa + (k * ca)))),
                px(centreX + (wR * (cb + (k * sb)))), px(centreY + (hR * (sb - (k * cb)))),
                px(centreX + (wR * cb)), px(centreY + (hR * sb)));
            a = b;
        }

        cx = centreX + (wR * Math.Cos(t0 + dt));
        cy = centreY + (hR * Math.Sin(t0 + dt));
    }

    // The ellipse parameter t of the point whose centre-to-point direction is theta: tan(theta) = (hR sin t) / (wR cos t).
    private static double Parametric(double theta, double wR, double hR)
    {
        double t = Math.Atan2(wR * Math.Sin(theta), hR * Math.Cos(theta));
        double d = t - theta;
        d -= 2 * Math.PI * Math.Round(d / (2 * Math.PI));
        return theta + d;
    }

    private sealed record Guide(string Name, string Op, string A, string B, string C);

    private sealed class PathSpec(double w, double h, PathFillMode fill, bool stroke)
    {
        public double W { get; } = w;

        public double H { get; } = h;

        public PathFillMode Fill { get; } = fill;

        public bool Stroke { get; } = stroke;

        public List<PathCommand> Commands { get; } = [];
    }

    private sealed record PathCommand(char Op, string[] Args);

    // ECMA-376 Part 1 §20.1.9.11 (gd) and §20.1.1.2 (the formula language of ST_GeomGuideFormula).
    private static class GuideMath
    {
        private const double Deg = 60000.0;

        public static void AddBuiltIns(Dictionary<string, double> env, double w, double h)
        {
            double ss = Math.Min(w, h);
            double ls = Math.Max(w, h);
            env["l"] = 0;
            env["t"] = 0;
            env["r"] = w;
            env["b"] = h;
            env["w"] = w;
            env["h"] = h;
            env["hc"] = w / 2;
            env["vc"] = h / 2;
            env["ss"] = ss;
            env["ls"] = ls;
            foreach (int n in new[] { 2, 3, 4, 5, 6, 8, 10, 12, 32 })
            {
                env["wd" + n.ToString(CultureInfo.InvariantCulture)] = w / n;
                env["hd" + n.ToString(CultureInfo.InvariantCulture)] = h / n;
            }

            foreach (int n in new[] { 2, 4, 6, 8, 16, 32 })
            {
                env["ssd" + n.ToString(CultureInfo.InvariantCulture)] = ss / n;
            }

            env["cd2"] = 180 * Deg;
            env["cd4"] = 90 * Deg;
            env["cd8"] = 45 * Deg;
            env["3cd4"] = 270 * Deg;
            env["3cd8"] = 135 * Deg;
            env["5cd8"] = 225 * Deg;
            env["7cd8"] = 315 * Deg;
        }

        public static double Value(string token, Dictionary<string, double> env)
        {
            if (env.TryGetValue(token, out double v))
            {
                return v;
            }

            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public static double Eval(Guide g, Dictionary<string, double> env)
        {
            double x = Value(g.A, env), y = Value(g.B, env), z = Value(g.C, env);
            double r = g.Op switch
            {
                "*/" => z == 0 ? 0 : x * y / z,
                "+-" => x + y - z,
                "+/" => z == 0 ? 0 : (x + y) / z,
                "?:" => x > 0 ? y : z,
                "abs" => Math.Abs(x),
                "at2" => Math.Atan2(y, x) * 180 / Math.PI * Deg,
                "cat2" => x * Math.Cos(Math.Atan2(z, y)),
                "sat2" => x * Math.Sin(Math.Atan2(z, y)),
                "cos" => x * Math.Cos(y / Deg * Math.PI / 180),
                "sin" => x * Math.Sin(y / Deg * Math.PI / 180),
                "tan" => x * Math.Tan(y / Deg * Math.PI / 180),
                "max" => Math.Max(x, y),
                "min" => Math.Min(x, y),
                "mod" => Math.Sqrt((x * x) + (y * y) + (z * z)),
                "pin" => y < x ? x : y > z ? z : y,
                "sqrt" => x < 0 ? 0 : Math.Sqrt(x),
                "val" => x,
                _ => 0,
            };
            return double.IsFinite(r) ? Math.Clamp(r, -1e15, 1e15) : 0;
        }
    }
}
