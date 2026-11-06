using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

/// <summary>
/// One layout node of a laid-out diagram: the node a <c>dgm:layoutNode</c> of the definition became for a point of the data model
/// (ECMA-376 Part 1 §21.4.2.18). It is the in-memory equivalent of a presentation point.
/// </summary>
internal sealed class LayoutInstance
{
    public required LayoutNodeDef Definition { get; init; }

    public LayoutInstance? Parent { get; init; }

    /// <summary>The point the node was created for (the context of its <c>forEach</c> iteration).</summary>
    public required DiagramPoint Context { get; init; }

    public string Name => Definition.Name;

    public List<LayoutInstance> Children { get; } = [];

    /// <summary>The points whose text the node shows (<c>presOf</c>).</summary>
    public List<DiagramPoint> PresentationOf { get; } = [];

    public AlgorithmDef Algorithm { get; set; } = new() { Type = "tx" };

    public ShapeDef? Shape { get; set; }

    public IteratorSpec? PresentationSpec { get; set; }

    public List<ConstraintDef> Constraints { get; } = [];

    public List<RuleDef> Rules { get; } = [];

    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    public string StyleLabel { get; set; } = string.Empty;

    // ---- layout state (points, relative to the diagram's frame)

    /// <summary>Numeric properties set by constraints (<c>w</c>, <c>h</c>, <c>l</c>, <c>primFontSz</c>, …).</summary>
    public Dictionary<string, double> Props { get; } = new(StringComparer.Ordinal);

    /// <summary>Margin constraints that follow the font size: type to factor.</summary>
    public Dictionary<string, double> FontMargins { get; } = new(StringComparer.Ordinal);

    /// <summary>Constraints with <c>op="equ"</c> make their targets share one value; the key names the group.</summary>
    public Dictionary<string, string> EqualGroups { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, double> MinBounds { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, double> MaxBounds { get; } = new(StringComparer.Ordinal);

    public bool Placed { get; set; }

    /// <summary>True once a parent's algorithm gave the node a rectangle that its own algorithm has not yet used.</summary>
    public bool NeedsPlacement { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double W { get; set; }

    public double H { get; set; }

    public double Rotation { get; set; }

    /// <summary>Overrides of the shape's geometry set by the parent's algorithm (the pyramid's trapezoids).</summary>
    public string? GeometryOverride { get; set; }

    public double? AdjustOverride { get; set; }

    /// <summary>A connector's route, set by the connector algorithm.</summary>
    public ConnectorRoute? Route { get; set; }

    /// <summary>The font size chosen for the node's text, in points.</summary>
    public double FontSize { get; set; }

    public int Order { get; set; }

    public bool Has(string prop) => Props.ContainsKey(prop);

    public double Get(string prop, double fallback) => Props.TryGetValue(prop, out double v) ? v : fallback;

    public IEnumerable<LayoutInstance> Descendants()
    {
        foreach (LayoutInstance c in Children)
        {
            yield return c;
            foreach (LayoutInstance d in c.Descendants())
            {
                yield return d;
            }
        }
    }
}

/// <summary>A connector between two layout nodes, in diagram points.</summary>
internal sealed class ConnectorRoute
{
    public List<(double X, double Y)> Points { get; } = [];

    public bool EndArrow { get; init; }

    public bool BeginArrow { get; init; }

    public bool Curved { get; init; }
}

/// <summary>Walks the axes of §21.4.2 (self, ch, des, followSib, …) over a data model.</summary>
internal sealed class DiagramNavigator(DiagramData data)
{
    private const int MaxResults = 20_000;

    public DiagramData Data { get; } = data;

    /// <summary>Applies an iterator's axes in sequence, each to the points the previous one produced.</summary>
    public List<DiagramPoint> Select(DiagramPoint context, IteratorSpec it)
    {
        if (it.Axis.Length == 0)
        {
            return [];
        }

        List<DiagramPoint> current = [context];
        for (int i = 0; i < it.Axis.Length; i++)
        {
            string axis = it.Axis[i];
            string pt = Pick(it.PointType, i, "all");
            int st = Dx.Int(Pick(it.Start, i, "1"), 1);
            int cnt = Dx.Int(Pick(it.Count, i, "0"), 0);
            int step = Dx.Int(Pick(it.Step, i, "1"), 1);
            bool hide = Dx.Bool(Pick(it.HideLastTransition, i, "true"), true);
            var next = new List<DiagramPoint>();
            foreach (DiagramPoint p in current)
            {
                next.AddRange(OnAxis(p, axis, pt, st, cnt, step, hide));
                if (next.Count > MaxResults)
                {
                    return next;
                }
            }

            current = next;
        }

        return current;
    }

    private static string Pick(string[] list, int i, string fallback) => list.Length == 0 ? fallback : list[Math.Min(i, list.Length - 1)];

    public List<DiagramPoint> OnAxis(DiagramPoint p, string axis, string pointType, int start, int count, int step, bool hideLastTransition)
    {
        List<DiagramPoint> raw = axis switch
        {
            "self" => [p],
            "ch" => ChildSequence(p.Id),
            "des" => Descendants(p.Id, 0),
            "desOrSelf" => [p, .. Descendants(p.Id, 0)],
            "par" => Parent(p) is { } par ? [par] : [],
            "ancst" => Ancestors(p),
            "ancstOrSelf" => [p, .. Ancestors(p)],
            "followSib" => Siblings(p, after: true),
            "precedSib" => Siblings(p, after: false),
            "follow" => Document(p, after: true),
            "preced" => Document(p, after: false),
            "root" => [Data.Document],
            _ => [],
        };

        var list = new List<DiagramPoint>(raw.Count);
        foreach (DiagramPoint q in raw)
        {
            if (Matches(q, pointType) && !(hideLastTransition && IsLastSiblingTransition(q) && pointType is "sibTrans" or "all"))
            {
                list.Add(q);
            }
        }

        // ECMA-376 Part 1 §21.4.2.1: st is the 1-based first point, step skips, cnt (0 = all) limits the count.
        var result = new List<DiagramPoint>();
        for (int i = Math.Max(start, 1) - 1; i < list.Count; i += Math.Max(step, 1))
        {
            result.Add(list[i]);
            if (count > 0 && result.Count >= count)
            {
                break;
            }
        }

        return result;
    }

    public static bool Matches(DiagramPoint p, string pointType) => pointType switch
    {
        "all" => true,
        "doc" => p.Type == DiagramPointType.Document,
        "node" or "norm" => p.Type == DiagramPointType.Node,
        "asst" or "nonNorm" => p.Type == DiagramPointType.Assistant,
        "nonAsst" => p.Type != DiagramPointType.Assistant,
        "parTrans" => p.Type == DiagramPointType.ParentTransition,
        "sibTrans" => p.Type == DiagramPointType.SiblingTransition,
        "pres" => p.Type == DiagramPointType.Presentation,
        _ => false,
    };

    // The last sibling transition of a parent has no sibling to lead to: layouts hide it unless hideLastTrans="0".
    private bool IsLastSiblingTransition(DiagramPoint p)
    {
        if (p.Type != DiagramPointType.SiblingTransition || Data.ConnectionOfTransition(p.Id) is not { } cx)
        {
            return false;
        }

        IReadOnlyList<DiagramConnection> sibs = Data.ChildConnections(cx.SourceId);
        return sibs.Count > 0 && sibs[^1].Id == cx.Id;
    }

    // The children of a point in the order layouts see them: for each parent-of connection its parent transition, the node and the
    // sibling transition that leads to the next one.
    public List<DiagramPoint> ChildSequence(string parentId)
    {
        var list = new List<DiagramPoint>();
        foreach (DiagramConnection cx in Data.ChildConnections(parentId))
        {
            if (cx.ParentTransitionId is { Length: > 0 } pt && Data.Find(pt) is { } ptp)
            {
                list.Add(ptp);
            }

            if (Data.Find(cx.DestinationId) is { } node)
            {
                list.Add(node);
            }

            if (cx.SiblingTransitionId is { Length: > 0 } st && Data.Find(st) is { } stp)
            {
                list.Add(stp);
            }
        }

        return list;
    }

    private List<DiagramPoint> Descendants(string id, int depth)
    {
        var list = new List<DiagramPoint>();
        if (depth > 64)
        {
            return list;
        }

        foreach (DiagramPoint c in ChildSequence(id))
        {
            list.Add(c);
            if (c.Type is DiagramPointType.Node or DiagramPointType.Assistant && list.Count < MaxResults)
            {
                list.AddRange(Descendants(c.Id, depth + 1));
            }
        }

        return list;
    }

    public DiagramPoint? Parent(DiagramPoint p)
    {
        DiagramConnection? cx = p.Type is DiagramPointType.ParentTransition or DiagramPointType.SiblingTransition ? Data.ConnectionOfTransition(p.Id) : Data.ParentConnection(p.Id);
        return cx is null ? null : Data.Find(cx.SourceId);
    }

    private List<DiagramPoint> Ancestors(DiagramPoint p)
    {
        var list = new List<DiagramPoint>();
        DiagramPoint? cur = Parent(p);
        while (cur is not null && list.Count < 64)
        {
            list.Add(cur);
            cur = Parent(cur);
        }

        return list;
    }

    private List<DiagramPoint> Siblings(DiagramPoint p, bool after)
    {
        if (Parent(p) is not { } parent)
        {
            return [];
        }

        List<DiagramPoint> seq = ChildSequence(parent.Id);
        int idx = seq.FindIndex(q => q.Id == p.Id);
        if (idx < 0)
        {
            return [];
        }

        if (after)
        {
            return seq.GetRange(idx + 1, seq.Count - idx - 1);
        }

        List<DiagramPoint> before = seq.GetRange(0, idx);
        before.Reverse();
        return before;
    }

    private List<DiagramPoint> Document(DiagramPoint p, bool after)
    {
        var all = new List<DiagramPoint> { Data.Document };
        all.AddRange(Descendants(Data.Document.Id, 0));
        int idx = all.FindIndex(q => q.Id == p.Id);
        if (idx < 0)
        {
            return [];
        }

        if (after)
        {
            return all.GetRange(idx + 1, all.Count - idx - 1);
        }

        List<DiagramPoint> before = all.GetRange(0, idx);
        before.Reverse();
        return before;
    }

    /// <summary>The 1-based position of a node among its parent's nodes, and how many there are.</summary>
    public (int Position, int Count) PositionAmongSiblings(DiagramPoint p)
    {
        if (Parent(p) is not { } parent)
        {
            return (1, 1);
        }

        List<DiagramPoint> nodes = ChildSequence(parent.Id).FindAll(q => q.Type == p.Type || (q.Type == DiagramPointType.Node && p.Type == DiagramPointType.Assistant));
        int idx = nodes.FindIndex(q => q.Id == p.Id);
        return (idx + 1, nodes.Count);
    }

    public int Depth(DiagramPoint p)
    {
        int d = 0;
        DiagramPoint? cur = p;
        while (cur is not null && d < 128)
        {
            cur = Parent(cur);
            if (cur is not null)
            {
                d++;
            }
        }

        return d;
    }

    public int MaxDepthBelow(DiagramPoint p, int depth = 0)
    {
        if (depth > 64)
        {
            return 0;
        }

        int best = 0;
        foreach (DiagramConnection cx in Data.ChildConnections(p.Id))
        {
            if (Data.Find(cx.DestinationId) is { } c)
            {
                best = Math.Max(best, 1 + MaxDepthBelow(c, depth + 1));
            }
        }

        return best;
    }
}

/// <summary>Applies a layout definition to a data model: creates the layout nodes, resolving <c>forEach</c> and <c>choose</c>.</summary>
internal sealed class DiagramInstantiator
{
    private const int MaxInstances = 5_000;
    private const int MaxDepth = 64;
    private readonly DiagramData _data;
    private readonly LayoutDefinition _definition;
    private readonly DiagramNavigator _nav;
    private int _count;

    public DiagramInstantiator(DiagramData data, LayoutDefinition definition)
    {
        _data = data;
        _definition = definition;
        _nav = new DiagramNavigator(data);
    }

    public DiagramNavigator Navigator => _nav;

    public bool Truncated { get; private set; }

    /// <summary>Builds the tree of layout nodes for the document point.</summary>
    public LayoutInstance Build() => Create(_definition.Root, _data.Document, null, 0);

    private LayoutInstance Create(LayoutNodeDef def, DiagramPoint ctx, LayoutInstance? parent, int depth)
    {
        _count++;
        var inst = new LayoutInstance { Definition = def, Context = ctx, Parent = parent, Order = _count, StyleLabel = def.StyleLabel };
        Apply(def.Body, inst, ctx, depth);
        if (inst.PresentationSpec is { } spec)
        {
            inst.PresentationOf.AddRange(_nav.Select(ctx, spec));
        }

        return inst;
    }

    private void Apply(LayoutBody body, LayoutInstance inst, DiagramPoint ctx, int depth)
    {
        if (body.Algorithm is not null)
        {
            inst.Algorithm = body.Algorithm;
        }

        if (body.Shape is not null)
        {
            inst.Shape = body.Shape;
        }

        if (body.PresentationOf is not null)
        {
            inst.PresentationSpec = body.PresentationOf;
        }

        inst.Constraints.AddRange(body.Constraints);
        inst.Rules.AddRange(body.Rules);
        foreach (KeyValuePair<string, string> kv in body.Variables)
        {
            inst.Variables[kv.Key] = kv.Value;
        }

        foreach (object item in body.Items)
        {
            if (_count >= MaxInstances || depth > MaxDepth)
            {
                Truncated = true;
                return;
            }

            switch (item)
            {
                case LayoutNodeDef child:
                    inst.Children.Add(Create(child, ctx, inst, depth + 1));
                    break;
                case ForEachDef fe:
                    ApplyForEach(fe, inst, ctx, depth + 1, 0);
                    break;
                case ChooseDef ch:
                    {
                        LayoutBody? chosen = null;
                        foreach (IfDef f in ch.Ifs)
                        {
                            if (Evaluate(f, ctx, inst))
                            {
                                chosen = f.Body;
                                break;
                            }
                        }

                        chosen ??= ch.Else;
                        if (chosen is not null)
                        {
                            Apply(chosen, inst, ctx, depth + 1);
                        }

                        break;
                    }
            }
        }
    }

    private void ApplyForEach(ForEachDef fe, LayoutInstance inst, DiagramPoint ctx, int depth, int refDepth)
    {
        // ECMA-376 Part 1 §21.4.2.14 (forEach/@ref): a forEach may stand for another by name, which is how a layout recurses.
        if (fe.Ref.Length > 0)
        {
            if (refDepth < MaxDepth && _definition.ForEachByName.TryGetValue(fe.Ref, out ForEachDef? target) && !ReferenceEquals(target, fe))
            {
                ApplyForEach(target, inst, ctx, depth, refDepth + 1);
            }

            return;
        }

        foreach (DiagramPoint pt in _nav.Select(ctx, fe.Iterator))
        {
            if (_count >= MaxInstances)
            {
                Truncated = true;
                return;
            }

            Apply(fe.Body, inst, pt, depth);
        }
    }

    private bool Evaluate(IfDef f, DiagramPoint ctx, LayoutInstance inst)
    {
        List<DiagramPoint> pts = f.Iterator.Axis.Length == 0 ? [ctx] : _nav.Select(ctx, f.Iterator);
        switch (f.Function)
        {
            case "cnt":
                return Compare(pts.Count, f.Operator, Dx.Num(f.Value, 0));
            case "pos" or "revPos" or "posEven" or "posOdd":
                {
                    if (pts.Count == 0)
                    {
                        return false;
                    }

                    (int pos, int total) = _nav.PositionAmongSiblings(pts[0]);
                    switch (f.Function)
                    {
                        case "pos":
                            return Compare(pos, f.Operator, Dx.Num(f.Value, 0));
                        case "revPos":
                            return Compare(total - pos + 1, f.Operator, Dx.Num(f.Value, 0));
                        default:
                            {
                                bool want = Dx.Bool(f.Value, true);
                                bool even = pos % 2 == 0;
                                bool actual = f.Function == "posEven" ? even : !even;
                                return f.Operator == "neq" ? actual != want : actual == want;
                            }
                    }
                }

            case "depth":
                return pts.Count > 0 && Compare(_nav.Depth(pts[0]), f.Operator, Dx.Num(f.Value, 0));
            case "maxDepth":
                return pts.Count > 0 && Compare(_nav.MaxDepthBelow(pts[0]), f.Operator, Dx.Num(f.Value, 0));
            case "var":
                return CompareText(Variable(inst, f.Argument), f.Operator, f.Value);
            default:
                return false;
        }
    }

    // ECMA-376 Part 1 §21.4.2: a variable takes the value the nearest layout node (itself outward) declares; the data model's root
    // point, which stores the user's choices, wins where it states one.
    public string Variable(LayoutInstance? inst, string arg)
    {
        string key = arg == "bulEnabled" ? "bulletEnabled" : arg;
        if (_data.RootVariables.TryGetValue(key, out string? user) && user.Length > 0)
        {
            return user;
        }

        for (LayoutInstance? cur = inst; cur is not null; cur = cur.Parent)
        {
            if (cur.Variables.TryGetValue(key, out string? v) && v.Length > 0)
            {
                return v;
            }
        }

        return key switch
        {
            "orgChart" or "bulletEnabled" => "false",
            "chMax" or "chPref" => "-1",
            "dir" => "norm",
            "hierBranch" => "std",
            "animOne" => "one",
            "animLvl" => "none",
            "resizeHandles" => "rel",
            _ => string.Empty,
        };
    }

    private static bool Compare(double a, string op, double b) => op switch
    {
        "equ" => Math.Abs(a - b) < 1e-9,
        "neq" => Math.Abs(a - b) >= 1e-9,
        "gt" => a > b,
        "lt" => a < b,
        "gte" => a >= b - 1e-9,
        "lte" => a <= b + 1e-9,
        _ => false,
    };

    private static bool CompareText(string actual, string op, string expected)
    {
        if (bool.TryParse(actual, out bool ba) && (bool.TryParse(expected, out bool bb) || expected is "0" or "1"))
        {
            bool eb = expected is "1" || (bool.TryParse(expected, out bool bx) && bx);
            return op == "neq" ? ba != eb : ba == eb;
        }

        if (double.TryParse(actual, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double na) &&
            double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double nb))
        {
            return Compare(na, op, nb);
        }

        bool eq = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        return op == "neq" ? !eq : eq;
    }
}
