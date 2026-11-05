using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

// Connectors, text fitting and the conversion of laid-out nodes into shapes.
internal sealed partial class DiagramLayoutEngine
{
    private Dictionary<string, List<LayoutInstance>>? _presenting;

    private IEnumerable<LayoutInstance> All()
    {
        if (Root is null)
        {
            yield break;
        }

        yield return Root;
        foreach (LayoutInstance d in Root.Descendants())
        {
            yield return d;
        }
    }

    // ---------------------------------------------------------------- connectors

    private void ConnectAll()
    {
        _presenting = new Dictionary<string, List<LayoutInstance>>(StringComparer.Ordinal);
        foreach (LayoutInstance i in All())
        {
            foreach (DiagramPoint p in i.PresentationOf)
            {
                if (!_presenting.TryGetValue(p.Id, out List<LayoutInstance>? list))
                {
                    _presenting[p.Id] = list = [];
                }

                list.Add(i);
            }
        }

        foreach (LayoutInstance i in All().ToList())
        {
            if (i.Algorithm.Type == "conn")
            {
                Connect(i);
            }
        }
    }

    private LayoutInstance? FindPresenting(string pointId, string name, LayoutInstance except)
    {
        if (_presenting is null || !_presenting.TryGetValue(pointId, out List<LayoutInstance>? list))
        {
            return null;
        }

        // The node made for the point itself wins over one that merely shows it among its descendants (presOf desOrSelf).
        IEnumerable<LayoutInstance> candidates = list.Where(i => i != except && i.Placed && i.Algorithm.Type != "conn" && (name.Length == 0 || i.Name == name));
        return candidates.FirstOrDefault(i => i.Context.Id == pointId) ?? candidates.FirstOrDefault();
    }

    // ECMA-376 Part 1 §21.4.7 (conn): a connector joins the layout nodes that present the two ends of a transition: for a sibling
    // transition the node it follows and the next node (the first again after the last, which closes a cycle), for a parent
    // transition the parent and the child. `dim` 2D is a block arrow, 1D a line; `connRout` shapes the line.
    private void Connect(LayoutInstance c)
    {
        DiagramPoint transition = c.PresentationOf.FirstOrDefault(p => p.Type is DiagramPointType.SiblingTransition or DiagramPointType.ParentTransition) ?? c.Context;
        if (transition.Type is not (DiagramPointType.SiblingTransition or DiagramPointType.ParentTransition) || _data.ConnectionOfTransition(transition.Id) is not { } cx)
        {
            return;
        }

        string? srcId = cx.DestinationId, dstId = null;
        if (transition.Type == DiagramPointType.SiblingTransition)
        {
            IReadOnlyList<DiagramConnection> sibs = _data.ChildConnections(cx.SourceId);
            int idx = -1;
            for (int i = 0; i < sibs.Count; i++)
            {
                if (sibs[i].Id == cx.Id)
                {
                    idx = i;
                }
            }

            dstId = idx < 0 ? null : idx + 1 < sibs.Count ? sibs[idx + 1].DestinationId : sibs.Count > 1 ? sibs[0].DestinationId : null;
        }
        else
        {
            srcId = cx.SourceId;
            dstId = cx.DestinationId;
        }

        if (srcId is null || dstId is null)
        {
            return;
        }

        string srcName = c.Algorithm.Param("srcNode", string.Empty), dstName = c.Algorithm.Param("dstNode", string.Empty);
        LayoutInstance? a = FindPresenting(srcId, srcName, c) ?? FindPresenting(srcId, string.Empty, c);
        LayoutInstance? b = FindPresenting(dstId, dstName, c) ?? FindPresenting(dstId, string.Empty, c);
        if (a is null || b is null || a == b)
        {
            return;
        }

        double acx = a.X + (a.W / 2), acy = a.Y + (a.H / 2), bcx = b.X + (b.W / 2), bcy = b.Y + (b.H / 2);
        double dx = bcx - acx, dy = bcy - acy;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            return;
        }

        string dim = c.Algorithm.Param("dim", "2D");
        if (dim == "1D")
        {
            Route(c, a, b, dx, dy);
            return;
        }

        double angle = Math.Atan2(dy, dx);
        double length, thickness, mx, my;
        if (c.Placed && c.W > 0 && c.H > 0 && c.Props.ContainsKey("w"))
        {
            length = Math.Max(c.W, c.H);
            thickness = Math.Min(c.W, c.H);
            mx = c.X + (c.W / 2);
            my = c.Y + (c.H / 2);
        }
        else
        {
            (double x1, double y1) = EdgePoint(a, dx, dy);
            (double x2, double y2) = EdgePoint(b, -dx, -dy);
            double gap = Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));
            length = gap * 0.55;
            thickness = Math.Min(length * 1.1, Math.Min(Math.Min(a.W, a.H), Math.Min(b.W, b.H)) * 0.4);
            mx = (x1 + x2) / 2;
            my = (y1 + y2) / 2;
        }

        if (length <= 0.5 || thickness <= 0.5)
        {
            return;
        }

        c.X = mx - (length / 2);
        c.Y = my - (thickness / 2);
        c.W = length;
        c.H = thickness;
        c.Rotation = angle * 180 / Math.PI;
        c.Placed = true;
    }

    // Where the ray from a rectangle's centre in direction (dx, dy) leaves it.
    private static (double X, double Y) EdgePoint(LayoutInstance r, double dx, double dy)
    {
        double cx = r.X + (r.W / 2), cy = r.Y + (r.H / 2);
        double tx = Math.Abs(dx) < 1e-12 ? double.PositiveInfinity : (r.W / 2) / Math.Abs(dx);
        double ty = Math.Abs(dy) < 1e-12 ? double.PositiveInfinity : (r.H / 2) / Math.Abs(dy);
        double t = Math.Min(tx, ty);
        return (cx + (dx * t), cy + (dy * t));
    }

    private static (double X, double Y) Anchor(LayoutInstance r, string point, double fallbackX, double fallbackY) => point switch
    {
        "bCtr" => (r.X + (r.W / 2), r.Y + r.H),
        "tCtr" => (r.X + (r.W / 2), r.Y),
        "midL" => (r.X, r.Y + (r.H / 2)),
        "midR" => (r.X + r.W, r.Y + (r.H / 2)),
        "ctr" => (r.X + (r.W / 2), r.Y + (r.H / 2)),
        "bL" => (r.X, r.Y + r.H),
        "bR" => (r.X + r.W, r.Y + r.H),
        "tL" => (r.X, r.Y),
        "tR" => (r.X + r.W, r.Y),
        _ => (fallbackX, fallbackY),
    };

    private static void Route(LayoutInstance c, LayoutInstance a, LayoutInstance b, double dx, double dy)
    {
        // The route runs along the axis the ends face: stated by begPts/endPts, else vertical when one node is wholly above the other,
        // horizontal when they are wholly side by side, else along the longer centre distance.
        string beg = c.Algorithm.Param("begPts", "auto"), end = c.Algorithm.Param("endPts", "auto");
        bool above = a.Y + a.H <= b.Y + 0.01 || b.Y + b.H <= a.Y + 0.01;
        bool beside = a.X + a.W <= b.X + 0.01 || b.X + b.W <= a.X + 0.01;
        bool vertical = beg is "bCtr" or "tCtr" || end is "bCtr" or "tCtr"
            ? true
            : beg is "midL" or "midR" || end is "midL" or "midR" ? false : above && !beside ? true : beside && !above ? false : Math.Abs(dy) > Math.Abs(dx);
        (double ax, double ay) = vertical
            ? (a.X + (a.W / 2), dy > 0 ? a.Y + a.H : a.Y)
            : (dx > 0 ? a.X + a.W : a.X, a.Y + (a.H / 2));
        (double bx, double by) = vertical
            ? (b.X + (b.W / 2), dy > 0 ? b.Y : b.Y + b.H)
            : (dx > 0 ? b.X : b.X + b.W, b.Y + (b.H / 2));
        (ax, ay) = Anchor(a, c.Algorithm.Param("begPts", "auto"), ax, ay);
        (bx, by) = Anchor(b, c.Algorithm.Param("endPts", "auto"), bx, by);
        string routing = c.Algorithm.Param("connRout", "stra");
        var route = new ConnectorRoute
        {
            EndArrow = c.Algorithm.Param("endSty", "auto") == "arr",
            BeginArrow = c.Algorithm.Param("begSty", "auto") == "arr",
            Curved = routing is "curve" or "longCurve",
        };
        route.Points.Add((ax, ay));
        if (routing is "bend" or "curve" or "longCurve")
        {
            if (vertical)
            {
                double midY = (ay + by) / 2;
                route.Points.Add((ax, midY));
                route.Points.Add((bx, midY));
            }
            else
            {
                double midX = (ax + bx) / 2;
                route.Points.Add((midX, ay));
                route.Points.Add((midX, by));
            }
        }

        route.Points.Add((bx, by));
        c.Route = route;
        c.Placed = true;
    }

    // ---------------------------------------------------------------- text

    private IEnumerable<LayoutInstance> TextNodes() =>
        All().Where(i => i.Placed && i.Shape is { Type: not "none", HideGeometry: false } && i.Algorithm.Type != "conn" && i.PresentationOf.Any(p => p.Type is DiagramPointType.Node or DiagramPointType.Assistant && p.PlainText.Trim().Length > 0));

    private static string GeometryOf(LayoutInstance n) => n.GeometryOverride ?? (n.Shape is { Type: "conn" } ? "rightArrow" : n.Shape?.Type ?? "rect");

    private static (double L, double T, double R, double B) TextRect(LayoutInstance n)
    {
        string geom = GeometryOf(n);
        if (GeometryDefinition.Preset(geom) is { } def)
        {
            EvaluatedGeometry eg = def.Evaluate(n.W, n.H, AdjustMap(n, def));
            return (eg.TextLeft, eg.TextTop, eg.TextRight, eg.TextBottom);
        }

        return (0, 0, n.W, n.H);
    }

    private static Dictionary<string, double> AdjustMap(LayoutInstance n, GeometryDefinition def)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        string[] names = [.. def.AdjustNames];
        if (n.AdjustOverride is { } ov && names.Length > 0)
        {
            map[names[0]] = ov;
        }
        else if (n.Shape is { } sh)
        {
            foreach ((int idx, double val) in sh.Adjust)
            {
                if (idx >= 1 && idx <= names.Length)
                {
                    map[names[idx - 1]] = val;
                }
            }
        }

        return map;
    }

    private static (double L, double T, double R, double B) Margins(LayoutInstance n, double size) => (
        MarginOf(n, "lMarg", size), MarginOf(n, "tMarg", size), MarginOf(n, "rMarg", size), MarginOf(n, "bMarg", size));

    private static double MarginOf(LayoutInstance n, string type, double size) =>
        n.FontMargins.TryGetValue(type, out double f) ? f * size : n.Props.GetValueOrDefault(type, 0);

    private bool Fits(LayoutInstance n, double size)
    {
        (double l, double t, double r, double b) = TextRect(n);
        (double ml, double mt, double mr, double mb) = Margins(n, size);
        double w = r - l - ml - mr, h = b - t - mt - mb;
        if (w <= 0.5 || h <= 0.5)
        {
            return false;
        }

        LaidText laid = DiagramText.Layout(BuildParagraphs(n, size), w, _fonts, _family);
        return laid.Height <= h + 0.5 && !laid.SplitWord;
    }

    private static double MinimumFont(LayoutInstance n)
    {
        for (LayoutInstance? cur = n.Parent; cur is not null; cur = cur.Parent)
        {
            foreach (RuleDef r in cur.Rules)
            {
                if (r.Type == "primFontSz" && !double.IsNaN(r.Value) && r.For != "self" && (r.ForName.Length == 0 || r.ForName == n.Name))
                {
                    return r.Value;
                }
            }
        }

        foreach (RuleDef r in n.Rules)
        {
            if (r.Type == "primFontSz" && !double.IsNaN(r.Value))
            {
                return r.Value;
            }
        }

        return 5;
    }

    // The standard's rules shrink `primFontSz` stepwise until the text fits; the result is the largest whole-point size that fits, never
    // below the rule's floor. Nodes bound by one `op="equ"` constraint take the smallest size any of them needs.
    private void FitText(LayoutInstance root)
    {
        _ = root;
        List<LayoutInstance> nodes = [.. TextNodes()];
        var best = new Dictionary<LayoutInstance, double>();
        foreach (LayoutInstance n in nodes)
        {
            double cap = Math.Floor(n.Props.GetValueOrDefault("primFontSz", 18));
            double floor = Math.Ceiling(MinimumFont(n));
            cap = Math.Max(cap, floor);
            double lo = floor, hi = cap;
            if (!Fits(n, lo))
            {
                best[n] = lo;
                continue;
            }

            while (hi - lo > 0.5)
            {
                double mid = Math.Floor((lo + hi + 1) / 2);
                if (Fits(n, mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }

                if (lo >= hi)
                {
                    break;
                }
            }

            best[n] = Fits(n, hi) ? hi : lo;
        }

        foreach (IGrouping<string?, LayoutInstance> g in nodes.GroupBy(n => n.EqualGroups.GetValueOrDefault("primFontSz")))
        {
            if (g.Key is null)
            {
                foreach (LayoutInstance n in g)
                {
                    n.FontSize = best[n];
                }

                continue;
            }

            double size = g.Min(n => best[n]);
            foreach (LayoutInstance n in g)
            {
                n.FontSize = size;
            }
        }
    }

    // The paragraphs a node shows at a primary font size: the node's own text, then (when the layout asks for bullets) its descendants'
    // text one level in. Smaller levels use 80 % of the primary size.
    private List<ParaSpec> BuildParagraphs(LayoutInstance n, double size)
    {
        string align = n.Algorithm.Param("parTxLTRAlign", "ctr");
        bool bullets = Dx.Bool(_instantiator.Variable(n, "bulEnabled"), false);
        int baseDepth = _nav.Depth(n.Context);
        var paras = new List<ParaSpec>();
        foreach (DiagramPoint pt in n.PresentationOf)
        {
            if (pt.Type is not (DiagramPointType.Node or DiagramPointType.Assistant) || pt.Text.IsNull)
            {
                continue;
            }

            int level = Math.Max(0, _nav.Depth(pt) - baseDepth);
            double lvlSize = level == 0 ? size : Math.Max(1, size * 0.8);
            foreach (MarkupNode p in Dx.Children(pt.Text, "p"))
            {
                bool bulleted = bullets && level > 0;
                var para = new ParaSpec
                {
                    Level = level,
                    Align = bulleted ? "l" : align,
                    MarginLeft = bulleted ? lvlSize * 0.4 * level : 0,
                    Indent = bulleted ? -lvlSize * 0.4 : 0,
                    LineSpacing = 0.9,
                    SpaceBefore = 0,
                    SpaceAfter = 0.35 * 1.2 * lvlSize,
                    Bullet = bulleted ? "\u2022" : null,
                };
                foreach (MarkupNode r in p.ChildElements)
                {
                    switch (r.LocalNameString)
                    {
                        case "r" or "fld":
                            {
                                MarkupNode rPr = Dx.Child(r, "rPr");
                                string text = Dx.Child(r, "t").InnerText;
                                if (text.Length == 0)
                                {
                                    break;
                                }

                                MarkupNode fill = Dx.Child(rPr, "solidFill");
                                para.Runs.Add(new RunSpec
                                {
                                    Text = text,
                                    Size = lvlSize,
                                    Bold = !rPr.IsNull && Dx.Bool(rPr.GetAttribute("b"), false),
                                    Italic = !rPr.IsNull && Dx.Bool(rPr.GetAttribute("i"), false),
                                    Family = Dx.Child(rPr, "latin").GetAttribute("typeface"),
                                    ColorXml = fill.IsNull ? null : DrawingXml.Copy(fill),
                                });
                                break;
                            }

                        case "br":
                            para.Runs.Add(new RunSpec { LineBreak = true, Size = lvlSize });
                            break;
                    }
                }

                if (para.Runs.Count > 0)
                {
                    paras.Add(para);
                }
            }
        }

        return paras;
    }

    // ---------------------------------------------------------------- shapes

    /// <summary>Turns the laid-out tree into shapes in drawing order.</summary>
    public List<OutShape> Shapes(DiagramColors? colors, DiagramQuickStyle? quickStyle)
    {
        var shapes = new List<OutShape>();
        var presData = new Dictionary<(string, string), DiagramPoint>();
        foreach (DiagramPoint p in _data.Points)
        {
            if (p.Type == DiagramPointType.Presentation && p.PresAssocId is { } assoc && p.PresName is { } name)
            {
                presData.TryAdd((assoc, name), p);
            }
        }

        int seq = 0;
        foreach (LayoutInstance n in All())
        {
            if (!n.Placed || n.Shape is not { } sh || sh.Type == "none" || sh.HideGeometry)
            {
                continue;
            }

            bool connector = n.Algorithm.Type == "conn";
            if (connector && n.Route is null && n.W <= 0)
            {
                continue;
            }

            if (!connector && (n.W <= 0 || n.H <= 0))
            {
                continue;
            }

            string label = n.StyleLabel.Length > 0 ? n.StyleLabel : connector ? (n.Route is null ? "sibTrans2D1" : "parChTrans1D1") : "node1";
            presData.TryGetValue((n.Context.Id, n.Name), out DiagramPoint? pres);
            if (pres?.PresStyleLabel is { Length: > 0 } custom)
            {
                label = custom;
            }

            shapes.Add(new OutShape
            {
                Instance = n,
                Sequence = seq++,
                ModelId = "{" + (0xA5800000u + (uint)seq).ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + "-0000-4000-8000-" + n.Order.ToString("X12", System.Globalization.CultureInfo.InvariantCulture) + "}",
                Geometry = GeometryOf(n),
                Label = label,
                IsConnector = connector,
                Presentation = pres,
                ZOrder = sh.ZOrderOffset,
            });
        }

        // Colours: the i-th of n shapes with one style label take the i-th of n colours (the data model's own index wins when stored).
        foreach (IGrouping<string, OutShape> g in shapes.GroupBy(s => s.Label))
        {
            List<OutShape> list = [.. g];
            ColorStyleLabel? cl = colors is not null && colors.Labels.TryGetValue(g.Key, out ColorStyleLabel? c) ? c : null;
            for (int i = 0; i < list.Count; i++)
            {
                OutShape s = list[i];
                int idx = s.Presentation?.PresStyleIndex ?? i;
                int cnt = s.Presentation?.PresStyleCount ?? list.Count;
                s.FillXml = DiagramColors.Pick(cl?.Fill, idx, cnt, _colors) ?? (cl is null ? "<a:schemeClr val=\"accent1\"/>" : null);
                s.LineXml = DiagramColors.Pick(cl?.Line, idx, cnt, _colors) ?? (cl is null ? "<a:schemeClr val=\"lt1\"/>" : null);
                s.TextXml = DiagramColors.Pick(cl?.TextFill, idx, cnt, _colors) ?? (cl is null ? "<a:schemeClr val=\"lt1\"/>" : null);
                s.Style = quickStyle is not null && quickStyle.Labels.TryGetValue(g.Key, out StyleLabelDef? sd) ? sd : new StyleLabelDef();
            }
        }

        shapes = [.. shapes.OrderBy(s => s.ZOrder).ThenBy(s => s.Sequence)];
        foreach (OutShape s in shapes)
        {
            LayoutInstance n = s.Instance;
            if (!s.IsConnector && n.FontSize > 0)
            {
                s.Paragraphs = BuildParagraphs(n, n.FontSize);
                (double l, double t, double r, double b) = TextRect(n);
                s.TextRectangle = (n.X + l, n.Y + t, r - l, b - t);
                s.Margins = Margins(n, n.FontSize);
                s.Anchor = n.Algorithm.Param("txAnchorVert", "mid") switch { "t" => "t", "b" => "b", _ => "ctr" };
            }
        }

        return shapes;
    }
}

/// <summary>A shape the engine produced, before it is written as DrawingML.</summary>
internal sealed class OutShape
{
    public required LayoutInstance Instance { get; init; }

    public int Sequence { get; init; }

    public required string ModelId { get; init; }

    public required string Geometry { get; init; }

    public required string Label { get; init; }

    public bool IsConnector { get; init; }

    public DiagramPoint? Presentation { get; init; }

    public int ZOrder { get; init; }

    public string? FillXml { get; set; }

    public string? LineXml { get; set; }

    public string? TextXml { get; set; }

    public StyleLabelDef Style { get; set; } = new();

    public List<ParaSpec> Paragraphs { get; set; } = [];

    public (double X, double Y, double W, double H) TextRectangle { get; set; }

    public (double L, double T, double R, double B) Margins { get; set; }

    public string Anchor { get; set; } = "ctr";

    public string PlainText => string.Join("\n", Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.LineBreak ? "\n" : r.Text))));
}
