using OneFont;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

// The layout pass of the diagram engine (ECMA-376 Part 1 §21.4): constraints set the sizes and spacing nodes would like, algorithms
// place the children inside the node's frame (shrinking everything uniformly when the preferred sizes do not fit), then text is fitted
// to the shapes. All lengths here are points, measured from the diagram frame's top-left corner.
//
// What the standard leaves open and this engine decides (to be checked against Office's own output at the differential):
//  - how snake picks its row length (largest scale; ties go to more columns);
//  - how far a cycle's nodes may grow before their arrows no longer fit between them;
//  - how a hierarchy spaces its levels when the layout gives no `sp` constraint;
//  - the colour blend of a `span` colour list (DiagramColors.Pick).
internal sealed partial class DiagramLayoutEngine
{
    private const double PointsPerMm = 72.0 / 25.4;
    private const int MaxDepth = 64;

    private readonly DiagramInstantiator _instantiator;
    private readonly DiagramNavigator _nav;
    private readonly DiagramData _data;
    private readonly IFontProvider _fonts;
    private readonly string _family;
    private readonly ColorResolver _colors;
    private readonly List<string> _notes = [];
    private int _placed;

    public DiagramLayoutEngine(DiagramData data, LayoutDefinition definition, IFontProvider fonts, ColorResolver colors)
    {
        _data = data;
        _instantiator = new DiagramInstantiator(data, definition);
        _nav = _instantiator.Navigator;
        _fonts = fonts;
        _colors = colors;
        _family = colors.Theme.MinorFont;
    }

    public IReadOnlyList<string> Notes => _notes;

    public LayoutInstance? Root { get; private set; }

    public DiagramNavigator Navigator => _nav;

    public DiagramData Data => _data;

    public DiagramInstantiator Instantiator => _instantiator;

    public IFontProvider Fonts => _fonts;

    public string Family => _family;

    public ColorResolver Colors => _colors;

    /// <summary>Instantiates the layout for the data model and places every node inside a frame of the given size (points).</summary>
    public LayoutInstance Run(double width, double height)
    {
        LayoutInstance root = _instantiator.Build();
        if (_instantiator.Truncated)
        {
            _notes.Add("smartart.layout-truncated");
        }

        root.X = 0;
        root.Y = 0;
        root.W = width;
        root.H = height;
        root.Placed = true;
        Root = root;
        PlaceNode(root, 0);
        ConnectAll();
        FitText(root);
        return root;
    }

    // ---------------------------------------------------------------- constraints

    private static bool IsLength(string type) => type is "w" or "h" or "l" or "t" or "r" or "b" or "ctrX" or "ctrY" or "wOff" or "hOff" or "lOff" or "rOff" or "tOff" or "bOff"
        or "ctrXOff" or "ctrYOff" or "sp" or "sibSp" or "secSibSp" or "begPad" or "endPad" or "bendDist" or "stemThick" or "connDist" or "diam" or "alignOff";

    private static bool IsMargin(string type) => type is "lMarg" or "rMarg" or "tMarg" or "bMarg" or "begMarg" or "endMarg";

    private static List<LayoutInstance> Select(LayoutInstance owner, string relation, string name, string pointType)
    {
        IEnumerable<LayoutInstance> pool = relation switch
        {
            "ch" => owner.Children,
            "des" => owner.Descendants(),
            _ => [owner],
        };
        var list = new List<LayoutInstance>();
        foreach (LayoutInstance i in pool)
        {
            if ((name.Length == 0 || i.Name == name) && (pointType == "all" || DiagramNavigator.Matches(i.Context, pointType)))
            {
                list.Add(i);
            }
        }

        return list;
    }

    private static double PropOf(LayoutInstance i, string type)
    {
        if (i.Props.TryGetValue(type, out double v))
        {
            return v;
        }

        return type switch
        {
            "w" => i.Placed ? i.W : 0,
            "h" => i.Placed ? i.H : 0,
            "primFontSz" => i.FontSize,
            _ => 0,
        };
    }

    // Applies the node's constraint list in order. A constraint names its targets (self, children or descendants, by layout-node name and
    // point type) and a value: a literal, or a property of a referenced node times a factor.
    private static void ApplyConstraints(LayoutInstance owner)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (ConstraintDef c in owner.Constraints)
            {
                // A bare constraint (no value, no reference) only names a property; rules grow it, so it states no size.
                if (c.Type == "none" || (c.RefType == "none" && !c.HasValue))
                {
                    continue;
                }

                List<LayoutInstance> targets = Select(owner, c.For, c.ForName, c.PointType);
                if (targets.Count == 0)
                {
                    continue;
                }

                if (IsMargin(c.Type) && c.RefType == "primFontSz")
                {
                    foreach (LayoutInstance t in targets)
                    {
                        t.FontMargins[c.Type] = c.Fact;
                    }

                    continue;
                }

                double value;
                if (c.RefType != "none")
                {
                    List<LayoutInstance> refs = Select(owner, c.RefFor, c.RefForName, c.RefPointType);
                    if (refs.Count == 0)
                    {
                        continue;
                    }

                    value = PropOf(refs[0], c.RefType) * c.Fact;
                }
                else
                {
                    value = IsLength(c.Type) ? c.Value * PointsPerMm : c.Value;
                }

                string group = owner.Order + ":" + c.Type + ":" + c.ForName;
                foreach (LayoutInstance t in targets)
                {
                    switch (c.Op)
                    {
                        case "gte":
                            t.MinBounds[c.Type] = Math.Max(t.MinBounds.GetValueOrDefault(c.Type), value);
                            t.Props[c.Type] = t.Props.TryGetValue(c.Type, out double lo) ? Math.Max(lo, value) : value;
                            break;
                        case "lte":
                            t.MaxBounds[c.Type] = t.MaxBounds.TryGetValue(c.Type, out double hi) ? Math.Min(hi, value) : value;
                            t.Props[c.Type] = t.Props.TryGetValue(c.Type, out double cur) ? Math.Min(cur, value) : value;
                            break;
                        case "equ":
                            t.Props[c.Type] = value;
                            t.EqualGroups[c.Type] = group;
                            break;
                        default:
                            t.Props[c.Type] = value;
                            break;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- placement

    private void PlaceNode(LayoutInstance n, int depth)
    {
        if (depth > MaxDepth || ++_placed > 20_000)
        {
            return;
        }

        n.Placed = true;
        n.NeedsPlacement = false;
        n.Props["w"] = n.W;
        n.Props["h"] = n.H;
        ApplyConstraints(n);
        switch (n.Algorithm.Type)
        {
            case "lin":
                Linear(n);
                break;
            case "snake":
                Snake(n);
                break;
            case "cycle":
                Cycle(n);
                break;
            case "pyra":
                Pyramid(n);
                break;
            case "hierRoot":
                ApplyConstraintsBelow(n, 0);
                HierarchyRoot(n, n.X, n.Y, n.W, n.H, null);
                break;
            case "hierChild":
                ApplyConstraintsBelow(n, 0);
                HierarchyChildren(n, n.X, n.Y, n.W, n.H, null);
                break;
            default:
                Composite(n);
                break;
        }

        PlaceChildren(n, depth);
    }

    // Children the algorithm gave a rectangle are placed in turn; sub-trees of a hierarchy were placed together with their root and only
    // their leaves are left.
    private void PlaceChildren(LayoutInstance n, int depth)
    {
        foreach (LayoutInstance c in n.Children)
        {
            if (c.Placed && c.NeedsPlacement)
            {
                PlaceNode(c, depth + 1);
            }
            else if (c.Placed && c.Algorithm.Type is "hierRoot" or "hierChild")
            {
                PlaceChildren(c, depth + 1);
            }
        }
    }

    // A hierarchy needs its descendants' preferred sizes before it can place anything.
    private static void ApplyConstraintsBelow(LayoutInstance n, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        foreach (LayoutInstance c in n.Children)
        {
            ApplyConstraints(c);
            ApplyConstraintsBelow(c, depth + 1);
        }
    }

    private static bool IsFloating(LayoutInstance c) => c.Algorithm.Type == "conn";

    private static bool IsSpacer(LayoutInstance c) => c.Algorithm.Type == "sp";

    // The size a parent's constraints gave a child; a child with none fills its parent.
    private static (double W, double H) Preferred(LayoutInstance c, LayoutInstance parent) =>
        (Math.Max(0, c.Props.TryGetValue("w", out double w) ? w : parent.W), Math.Max(0, c.Props.TryGetValue("h", out double h) ? h : parent.H));

    private static void Assign(LayoutInstance c, double x, double y, double w, double h)
    {
        c.X = x;
        c.Y = y;
        c.W = Math.Max(0, w);
        c.H = Math.Max(0, h);
        c.Placed = true;
        c.NeedsPlacement = true;
    }

    private static double Scale(double needed, double available)
    {
        if (needed <= 1e-9)
        {
            return 1;
        }

        return Math.Clamp(available / needed, 0, 1);
    }

    private static double Gap(LayoutInstance n)
    {
        // Spacing comes from spacer or connector children when the layout has them, else from the node's own `sp` property.
        bool hasSpacers = n.Children.Any(c => IsSpacer(c) || IsFloating(c));
        return hasSpacers ? 0 : n.Props.GetValueOrDefault("sp", n.Props.GetValueOrDefault("sibSp", 0));
    }

    // ECMA-376 Part 1 §21.4.7 (lin): children in a row or column, in order or reversed; the group is centred unless told otherwise.
    private static void Linear(LayoutInstance n)
    {
        string dir = n.Algorithm.Param("linDir", "fromL");
        bool horizontal = dir is "fromL" or "fromR";
        bool reverse = dir is "fromR" or "fromB";
        List<LayoutInstance> items = [.. n.Children];
        if (reverse)
        {
            items.Reverse();
        }

        if (items.Count == 0)
        {
            return;
        }

        double gap = Gap(n);
        var sizes = items.Select(c => Preferred(c, n)).ToList();
        double total = 0, cross = 0;
        foreach ((double w, double h) in sizes)
        {
            total += horizontal ? w : h;
            cross = Math.Max(cross, horizontal ? h : w);
        }

        total += gap * (items.Count - 1);
        double axisAvail = horizontal ? n.W : n.H;
        double crossAvail = horizontal ? n.H : n.W;
        double s = Math.Min(Scale(total, axisAvail), Scale(cross, crossAvail));
        string alongAlign = horizontal ? n.Algorithm.Param("horzAlign", "ctr") : n.Algorithm.Param("vertAlign", "mid");
        string acrossAlign = horizontal ? n.Algorithm.Param("vertAlign", "mid") : n.Algorithm.Param("horzAlign", "ctr");
        string nodeAcross = horizontal ? n.Algorithm.Param("nodeVertAlign", "mid") : n.Algorithm.Param("nodeHorzAlign", "ctr");
        double usedAxis = total * s;
        double startAxis = alongAlign switch { "l" or "t" => 0, "r" or "b" => axisAvail - usedAxis, _ => (axisAvail - usedAxis) / 2 };
        double bandCross = cross * s;
        double bandStart = acrossAlign switch { "l" or "t" => 0, "r" or "b" => crossAvail - bandCross, _ => (crossAvail - bandCross) / 2 };
        double pos = startAxis;
        for (int i = 0; i < items.Count; i++)
        {
            (double w, double h) = sizes[i];
            double iw = w * s, ih = h * s;
            double crossSize = horizontal ? ih : iw;
            double off = nodeAcross switch { "l" or "t" => 0, "r" or "b" => bandCross - crossSize, _ => (bandCross - crossSize) / 2 };
            if (horizontal)
            {
                Assign(items[i], n.X + pos, n.Y + bandStart + off, iw, ih);
            }
            else
            {
                Assign(items[i], n.X + bandStart + off, n.Y + pos, iw, ih);
            }

            pos += (horizontal ? iw : ih) + (gap * s);
        }
    }

    // ECMA-376 Part 1 §21.4.7 (snake): children fill rows (or columns) that wrap, the way text does. The row length is the one that
    // lets the children stay largest.
    private static void Snake(LayoutInstance n)
    {
        List<LayoutInstance> nodes = [.. n.Children.Where(c => !IsFloating(c) && !IsSpacer(c))];
        if (nodes.Count == 0)
        {
            return;
        }

        string grow = n.Algorithm.Param("grDir", "tL");
        bool byColumn = n.Algorithm.Param("flowDir", "row") == "col";
        bool snakeBack = n.Algorithm.Param("contDir", "sameDir") == "revDir";
        double gap = n.Props.GetValueOrDefault("sp", n.Props.GetValueOrDefault("sibSp", n.Children.Where(IsSpacer).Select(c => Preferred(c, n).W).FirstOrDefault()));
        var sizes = nodes.Select(c => Preferred(c, n)).ToList();
        int count = nodes.Count;
        double bestScale = -1;
        int bestLines = 1, bestPer = count;
        for (int per = 1; per <= count; per++)
        {
            int lines = (count + per - 1) / per;
            (double naturalW, double naturalH) = GridSize(sizes, per, lines, gap, byColumn);
            double s = Math.Min(Scale(naturalW, n.W), Scale(naturalH, n.H));
            if (s > bestScale + 1e-9 || (Math.Abs(s - bestScale) <= 1e-9 && !byColumn && per > bestPer))
            {
                bestScale = s;
                bestLines = lines;
                bestPer = per;
            }
        }

        double scale = Math.Max(bestScale, 0);
        // Column widths and row heights of the grid, then each line is centred (off="ctr").
        int cols = byColumn ? bestLines : bestPer;
        int rows = byColumn ? bestPer : bestLines;
        var colW = new double[cols];
        var rowH = new double[rows];
        for (int i = 0; i < count; i++)
        {
            (int r, int c) = Cell(i, bestPer, byColumn);
            colW[c] = Math.Max(colW[c], sizes[i].W * scale);
            rowH[r] = Math.Max(rowH[r], sizes[i].H * scale);
        }

        double g = gap * scale;
        double gridW = colW.Sum() + (g * (cols - 1)), gridH = rowH.Sum() + (g * (rows - 1));
        double ox = n.X + ((n.W - gridW) / 2), oy = n.Y + ((n.H - gridH) / 2);
        for (int i = 0; i < count; i++)
        {
            (int r, int c) = Cell(i, bestPer, byColumn);
            int inLine = byColumn ? Math.Min(bestPer, count - (c * bestPer)) : Math.Min(bestPer, count - (r * bestPer));
            int posInLine = byColumn ? r : c;
            int line = byColumn ? c : r;
            if (snakeBack && line % 2 == 1)
            {
                posInLine = inLine - 1 - posInLine;
            }

            // Lines that are not full are centred across the grid.
            double x, y;
            if (byColumn)
            {
                double used = Enumerable.Range(0, inLine).Sum(k => rowH[k]) + (g * (inLine - 1));
                y = oy + ((gridH - used) / 2) + Enumerable.Range(0, posInLine).Sum(k => rowH[k] + g);
                x = ox + Enumerable.Range(0, line).Sum(k => colW[k] + g);
            }
            else
            {
                double used = Enumerable.Range(0, inLine).Sum(k => colW[k]) + (g * (inLine - 1));
                x = ox + ((gridW - used) / 2) + Enumerable.Range(0, posInLine).Sum(k => colW[k] + g);
                y = oy + Enumerable.Range(0, line).Sum(k => rowH[k] + g);
            }

            double w = sizes[i].W * scale, h = sizes[i].H * scale;
            double cellW = byColumn ? colW[line] : colW[posInLine];
            double cellH = byColumn ? rowH[posInLine] : rowH[line];
            x += (cellW - w) / 2;
            y += (cellH - h) / 2;
            if (grow is "tR" or "bR")
            {
                x = n.X + n.W - (x - n.X) - w;
            }

            if (grow is "bL" or "bR")
            {
                y = n.Y + n.H - (y - n.Y) - h;
            }

            Assign(nodes[i], x, y, w, h);
        }

        // Connector and spacer children between neighbours in one line sit in the gap; they are placed by the connector pass.
        PlaceBetweenInSnake(n, nodes);
    }

    private static (int Row, int Col) Cell(int i, int perLine, bool byColumn) => byColumn ? (i % perLine, i / perLine) : (i / perLine, i % perLine);

    private static (double W, double H) GridSize(List<(double W, double H)> sizes, int per, int lines, double gap, bool byColumn)
    {
        int cols = byColumn ? lines : per;
        int rows = byColumn ? per : lines;
        var colW = new double[cols];
        var rowH = new double[rows];
        for (int i = 0; i < sizes.Count; i++)
        {
            (int r, int c) = Cell(i, per, byColumn);
            colW[c] = Math.Max(colW[c], sizes[i].W);
            rowH[r] = Math.Max(rowH[r], sizes[i].H);
        }

        return (colW.Sum() + (gap * (cols - 1)), rowH.Sum() + (gap * (rows - 1)));
    }

    private static void PlaceBetweenInSnake(LayoutInstance n, List<LayoutInstance> nodes)
    {
        // A transition node follows the node it belongs to. When the next node is on the same line it takes the gap between them.
        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            LayoutInstance a = nodes[i], b = nodes[i + 1];
            int ia = n.Children.IndexOf(a), ib = n.Children.IndexOf(b);
            for (int k = ia + 1; k < ib; k++)
            {
                LayoutInstance t = n.Children[k];
                if (!IsFloating(t) && !IsSpacer(t))
                {
                    continue;
                }

                bool sameRow = Math.Abs((a.Y + (a.H / 2)) - (b.Y + (b.H / 2))) < 1e-6;
                bool sameCol = Math.Abs((a.X + (a.W / 2)) - (b.X + (b.W / 2))) < 1e-6;
                if (!sameRow && !sameCol)
                {
                    continue;
                }

                if (sameRow)
                {
                    double left = Math.Min(a.X + a.W, b.X + b.W), right = Math.Max(a.X, b.X);
                    double len = Math.Max(0, right - left);
                    double th = Math.Min(a.H, b.H) * 0.5;
                    Assign(t, left, a.Y + (a.H / 2) - (th / 2), len, th);
                }
                else
                {
                    double top = Math.Min(a.Y + a.H, b.Y + b.H), bottom = Math.Max(a.Y, b.Y);
                    double len = Math.Max(0, bottom - top);
                    double tw = Math.Min(a.W, b.W) * 0.5;
                    Assign(t, a.X + (a.W / 2) - (tw / 2), top, tw, len);
                }

                t.NeedsPlacement = false;
            }
        }

    }

    // ECMA-376 Part 1 §21.4.7 (cycle): children on a circle (or an arc), the first at the top; stAng and spanAng are degrees clockwise.
    // The nodes are scaled down until the gap between neighbours can hold an arrow (about 45 % of the smaller node side).
    private static void Cycle(LayoutInstance n)
    {
        List<LayoutInstance> nodes = [.. n.Children.Where(c => !IsFloating(c) && !IsSpacer(c))];
        if (nodes.Count == 0)
        {
            return;
        }

        double start = Dx.Num(n.Algorithm.Param("stAng", "0"), 0);
        double span = Dx.Num(n.Algorithm.Param("spanAng", "360"), 360);
        bool alongPath = n.Algorithm.Param("rotPath", "none") == "alongPath";
        var sizes = nodes.Select(c => Preferred(c, n)).ToList();
        int count = nodes.Count;
        double cx = n.X + (n.W / 2), cy = n.Y + (n.H / 2);
        double maxW = Math.Max(1, sizes.Max(z => z.W)), maxH = Math.Max(1, sizes.Max(z => z.H));
        double minSide = Math.Max(1, sizes.Min(z => Math.Min(z.W, z.H)));
        double s = count == 1 ? Math.Min(1, Math.Min(n.W / maxW, n.H / maxH)) : Math.Min(1, Math.Min(n.W / maxW, n.H / maxH));

        (double X, double Y)[] Positions(double scale)
        {
            double radius = Math.Max(0, Math.Min((n.W / 2) - (maxW * scale / 2), (n.H / 2) - (maxH * scale / 2)));
            var pts = new (double X, double Y)[count];
            for (int i = 0; i < count; i++)
            {
                double frac = span >= 360 ? (double)i / count : count > 1 ? (double)i / (count - 1) : 0;
                double ang = (start + (span * frac)) * Math.PI / 180;
                pts[i] = count == 1 ? (cx, cy) : (cx + (radius * Math.Sin(ang)), cy - (radius * Math.Cos(ang)));
            }

            return pts;
        }

        if (count > 1)
        {
            for (int step = 0; step < 160 && s > 0.02; step++)
            {
                (double X, double Y)[] pts = Positions(s);
                double need = 0.45 * minSide * s;
                double worst = double.MaxValue;
                int pairs = span >= 360 ? count : count - 1;
                for (int i = 0; i < pairs; i++)
                {
                    int j = (i + 1) % count;
                    worst = Math.Min(worst, EdgeGap(pts[i], sizes[i], pts[j], sizes[j], s));
                }

                if (worst >= need)
                {
                    break;
                }

                s *= 0.97;
            }
        }

        (double X, double Y)[] final = Positions(s);
        for (int i = 0; i < count; i++)
        {
            double w = sizes[i].W * s, h = sizes[i].H * s;
            Assign(nodes[i], final[i].X - (w / 2), final[i].Y - (h / 2), w, h);
            if (alongPath)
            {
                double frac = span >= 360 ? (double)i / count : count > 1 ? (double)i / (count - 1) : 0;
                nodes[i].Rotation = (start + (span * frac)) % 360;
            }
        }
    }

    // The distance between two scaled rectangles measured along the line through their centres.
    private static double EdgeGap((double X, double Y) a, (double W, double H) sa, (double X, double Y) b, (double W, double H) sb, double scale)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double dist = Math.Sqrt((dx * dx) + (dy * dy));
        if (dist < 1e-9)
        {
            return 0;
        }

        double ux = Math.Abs(dx / dist), uy = Math.Abs(dy / dist);
        double Reach((double W, double H) sz)
        {
            double tx = ux < 1e-12 ? double.PositiveInfinity : sz.W * scale / 2 / ux;
            double ty = uy < 1e-12 ? double.PositiveInfinity : sz.H * scale / 2 / uy;
            return Math.Min(tx, ty);
        }

        return dist - Reach(sa) - Reach(sb);
    }

    // ECMA-376 Part 1 §21.4.7 (pyra): levels stacked from the apex; the first is a triangle, the rest trapezoids that continue its sides.
    private static void Pyramid(LayoutInstance n)
    {
        List<LayoutInstance> levels = [.. n.Children.Where(c => !IsFloating(c) && !IsSpacer(c) && c.Shape is { Type: not "none" })];
        if (levels.Count == 0)
        {
            levels = [.. n.Children.Where(c => !IsFloating(c) && !IsSpacer(c))];
        }

        int count = levels.Count;
        if (count == 0)
        {
            return;
        }

        bool fromBottom = n.Algorithm.Param("linDir", "fromT") == "fromB";
        double levelH = n.H / count;
        for (int i = 0; i < count; i++)
        {
            int rank = fromBottom ? count - 1 - i : i; // 0 is the apex level
            double bottomW = n.W * (rank + 1) / count;
            double topW = n.W * rank / count;
            LayoutInstance level = levels[i];
            Assign(level, n.X + ((n.W - bottomW) / 2), n.Y + (rank * levelH), bottomW, levelH);
            if (rank == 0)
            {
                level.GeometryOverride = "triangle";
                level.AdjustOverride = 50000;
            }
            else
            {
                level.GeometryOverride = "trapezoid";
                double inset = (bottomW - topW) / 2;
                level.AdjustOverride = Math.Min(50000, inset / Math.Max(1e-9, Math.Min(bottomW, levelH)) * 100000);
            }
        }
    }

    // ECMA-376 Part 1 §21.4.7 (composite): every child is placed by its own constraints (l, t, w, h, r, b, ctrX, ctrY, diam).
    private static void Composite(LayoutInstance n)
    {
        foreach (LayoutInstance c in n.Children)
        {
            if (IsFloating(c) && !c.Props.ContainsKey("w"))
            {
                continue;
            }

            double w = c.Props.TryGetValue("diam", out double dm) ? dm : c.Props.TryGetValue("w", out double pw) ? pw : n.W;
            double h = c.Props.TryGetValue("diam", out double dm2) ? dm2 : c.Props.TryGetValue("h", out double ph) ? ph : n.H;
            double x = c.Props.TryGetValue("l", out double l) ? l
                : c.Props.TryGetValue("r", out double r) ? r - w
                : c.Props.TryGetValue("ctrX", out double cxv) ? cxv - (w / 2) : 0;
            double y = c.Props.TryGetValue("t", out double t) ? t
                : c.Props.TryGetValue("b", out double b) ? b - h
                : c.Props.TryGetValue("ctrY", out double cyv) ? cyv - (h / 2) : 0;
            x += c.Props.GetValueOrDefault("lOff");
            y += c.Props.GetValueOrDefault("tOff");
            Assign(c, n.X + x, n.Y + y, w, h);
        }
    }

    // ---------------------------------------------------------------- hierarchies

    private static (double W, double H) Natural(LayoutInstance n, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            return Preferred(n, n);
        }

        switch (n.Algorithm.Type)
        {
            case "hierRoot":
                {
                    (LayoutInstance? root, LayoutInstance? kids) = HierParts(n);
                    (double rw, double rh) = root is null ? (0, 0) : Preferred(root, n);
                    (double cw, double ch) = kids is null ? (0, 0) : Natural(kids, depth + 1);
                    double vgap = LevelGap(n, rh);
                    return (Math.Max(rw, cw), rh + (cw > 0 ? vgap + ch : 0));
                }

            case "hierChild":
                {
                    List<LayoutInstance> subs = [.. n.Children.Where(c => !IsFloating(c))];
                    double w = 0, h = 0;
                    foreach (LayoutInstance c in subs)
                    {
                        (double cw, double ch) = Natural(c, depth + 1);
                        w += cw;
                        h = Math.Max(h, ch);
                    }

                    return (w + (SiblingGap(n, subs) * Math.Max(0, subs.Count - 1)), h);
                }

            default:
                return Preferred(n, n.Parent ?? n);
        }
    }

    private static (LayoutInstance? Root, LayoutInstance? Children) HierParts(LayoutInstance n)
    {
        LayoutInstance? root = null, kids = null;
        foreach (LayoutInstance c in n.Children)
        {
            if (IsFloating(c))
            {
                continue;
            }

            if (c.Algorithm.Type == "hierChild")
            {
                kids ??= c;
            }
            else
            {
                root ??= c;
            }
        }

        return (root, kids);
    }

    // Space between a root and its children: the layout's `sp` when it gives one, else half the root's height.
    private static double LevelGap(LayoutInstance n, double rootHeight) => n.Props.GetValueOrDefault("sp", rootHeight * 0.5);

    private static double SiblingGap(LayoutInstance n, List<LayoutInstance> subs)
    {
        double sp = n.Props.GetValueOrDefault("sibSp", n.Props.GetValueOrDefault("sp", -1));
        if (sp >= 0)
        {
            return sp;
        }

        // Without a stated spacing, a quarter of the widest sibling root.
        double widest = subs.Count == 0 ? 0 : subs.Max(c => c.Children.Count > 0 && !IsFloating(c.Children[0]) ? Preferred(c.Children[0], c).W : Preferred(c, n).W);
        return widest * 0.25;
    }

    // ECMA-376 Part 1 §21.4.7 (hierRoot, hierChild): a root node centred above its children, whose own sub-trees sit side by side. A
    // sub-tree is as wide as its widest level; the whole tree is scaled down to the frame when it does not fit.
    private void HierarchyRoot(LayoutInstance n, double x, double y, double w, double h, double? forcedScale)
    {
        (double nw, double nh) = Natural(n);
        double s = forcedScale ?? Math.Min(Scale(nw, w), Scale(nh, h));
        double top = forcedScale is null ? y + ((h - (nh * s)) / 2) : y;
        double left = forcedScale is null ? x + ((w - (nw * s)) / 2) : x;
        double width = nw * s;
        (LayoutInstance? root, LayoutInstance? kids) = HierParts(n);
        double rootBottom = top;
        if (root is not null)
        {
            (double rw, double rh) = Preferred(root, n);
            Assign(root, left + ((width - (rw * s)) / 2), top, rw * s, rh * s);
            rootBottom = top + (rh * s);
            n.X = left;
            n.Y = top;
            n.W = width;
            n.H = nh * s;
            n.Placed = true;
            n.NeedsPlacement = false;
            if (kids is not null)
            {
                (double cw, double ch) = Natural(kids);
                double gap = LevelGap(n, rh) * s;
                HierarchyChildren(kids, left + ((width - (cw * s)) / 2), rootBottom + gap, cw * s, ch * s, s);
            }
        }
    }

    private void HierarchyChildren(LayoutInstance n, double x, double y, double w, double h, double? forcedScale)
    {
        List<LayoutInstance> subs = [.. n.Children.Where(c => !IsFloating(c))];
        (double nw, double nh) = Natural(n);
        double s = forcedScale ?? Math.Min(Scale(nw, w), Scale(nh, h));
        double gap = SiblingGap(n, subs) * s;
        double total = (nw * s);
        double cursor = forcedScale is null ? x + ((w - total) / 2) : x;
        double top = forcedScale is null ? y + ((h - (nh * s)) / 2) : y;
        n.X = cursor;
        n.Y = top;
        n.W = total;
        n.H = nh * s;
        n.Placed = true;
        n.NeedsPlacement = false;
        foreach (LayoutInstance c in subs)
        {
            (double cw, double ch) = Natural(c);
            if (c.Algorithm.Type == "hierRoot")
            {
                HierarchyRoot(c, cursor, top, cw * s, ch * s, s);
            }
            else
            {
                Assign(c, cursor, top, cw * s, ch * s);
            }

            cursor += (cw * s) + gap;
        }
    }
}
