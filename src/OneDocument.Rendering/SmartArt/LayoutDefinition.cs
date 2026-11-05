using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

// The layout definition (dgm:layoutDef, ECMA-376 Part 1 §21.4.2): a tree of layout nodes with algorithms, constraints and rules, driven by
// forEach / choose over the data model. This file only reads the definition; DiagramInstance applies it to a data model.

/// <summary>The iterator attributes shared by <c>forEach</c>, <c>if</c> and <c>presOf</c> (§21.4.2.1, AG_IteratorAttributes). Each is a list.</summary>
internal sealed class IteratorSpec
{
    public string[] Axis { get; init; } = [];

    public string[] PointType { get; init; } = [];

    public string[] HideLastTransition { get; init; } = [];

    public string[] Start { get; init; } = [];

    public string[] Count { get; init; } = [];

    public string[] Step { get; init; } = [];

    public static IteratorSpec Read(MarkupNode e) => new()
    {
        Axis = Dx.List(e.GetAttribute("axis")),
        PointType = Dx.List(e.GetAttribute("ptType")),
        HideLastTransition = Dx.List(e.GetAttribute("hideLastTrans")),
        Start = Dx.List(e.GetAttribute("st")),
        Count = Dx.List(e.GetAttribute("cnt")),
        Step = Dx.List(e.GetAttribute("step")),
    };
}

internal sealed class AlgorithmDef
{
    public required string Type { get; init; }

    public Dictionary<string, string> Parameters { get; } = new(StringComparer.Ordinal);

    public string Param(string name, string fallback) => Parameters.TryGetValue(name, out string? v) ? v : fallback;
}

internal sealed class ShapeDef
{
    public string Type { get; init; } = "none";

    public double Rotation { get; init; }

    public bool HideGeometry { get; init; }

    public int ZOrderOffset { get; init; }

    public List<(int Index, double Value)> Adjust { get; } = [];
}

internal sealed class ConstraintDef
{
    public required string Type { get; init; }

    public string For { get; init; } = "self";

    public string ForName { get; init; } = string.Empty;

    public string PointType { get; init; } = "all";

    public string RefType { get; init; } = "none";

    public string RefFor { get; init; } = "self";

    public string RefForName { get; init; } = string.Empty;

    public string RefPointType { get; init; } = "all";

    public string Op { get; init; } = "none";

    public double Value { get; init; }

    public double Fact { get; init; } = 1;
}

internal sealed class RuleDef
{
    public required string Type { get; init; }

    public string For { get; init; } = "self";

    public string ForName { get; init; } = string.Empty;

    public string PointType { get; init; } = "all";

    public double Value { get; init; } = double.NaN;

    public double Fact { get; init; } = double.NaN;

    public double Max { get; init; } = double.NaN;
}

/// <summary>What a layout node, <c>forEach</c>, <c>if</c> or <c>else</c> contains (they share one content model).</summary>
internal sealed class LayoutBody
{
    public AlgorithmDef? Algorithm { get; set; }

    public ShapeDef? Shape { get; set; }

    public IteratorSpec? PresentationOf { get; set; }

    public List<ConstraintDef> Constraints { get; } = [];

    public List<RuleDef> Rules { get; } = [];

    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>Layout nodes, <c>forEach</c> and <c>choose</c> elements, in order.</summary>
    public List<object> Items { get; } = [];
}

internal sealed class LayoutNodeDef
{
    public string Name { get; init; } = string.Empty;

    public string StyleLabel { get; init; } = string.Empty;

    public required LayoutBody Body { get; init; }
}

internal sealed class ForEachDef
{
    public string Name { get; init; } = string.Empty;

    public string Ref { get; init; } = string.Empty;

    public required IteratorSpec Iterator { get; init; }

    public required LayoutBody Body { get; init; }
}

internal sealed class IfDef
{
    public required IteratorSpec Iterator { get; init; }

    public required string Function { get; init; }

    public string Argument { get; init; } = "none";

    public required string Operator { get; init; }

    public required string Value { get; init; }

    public required LayoutBody Body { get; init; }
}

internal sealed class ChooseDef
{
    public List<IfDef> Ifs { get; } = [];

    public LayoutBody? Else { get; set; }
}

/// <summary>A layout definition read from a <c>dgm:layoutDef</c> part.</summary>
internal sealed class LayoutDefinition
{
    private const int MaxNodes = 8_000;
    private const int MaxDepth = 64;
    private int _count;

    private LayoutDefinition()
    {
    }

    public LayoutNodeDef Root { get; private set; } = null!;

    public string UniqueId { get; private set; } = string.Empty;

    public string DefaultStyle { get; private set; } = string.Empty;

    /// <summary>Named <c>forEach</c> elements, for <c>forEach/@ref</c>.</summary>
    public Dictionary<string, ForEachDef> ForEachByName { get; } = new(StringComparer.Ordinal);

    /// <summary>Reads a layout definition.</summary>
    /// <param name="doc">The parsed part.</param>
    /// <returns>The definition, or null when the part is not a layout definition or is too large.</returns>
    public static LayoutDefinition? TryRead(MarkupDocument doc)
    {
        MarkupNode root = doc.Root;
        if (root.IsNull || root.LocalNameString != "layoutDef")
        {
            return null;
        }

        MarkupNode rootNode = Dx.Child(root, "layoutNode");
        if (rootNode.IsNull)
        {
            return null;
        }

        var def = new LayoutDefinition
        {
            UniqueId = root.GetAttribute("uniqueId") ?? string.Empty,
            DefaultStyle = root.GetAttribute("defStyle") ?? string.Empty,
        };
        try
        {
            def.Root = def.ReadNode(rootNode, 0);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return def;
    }

    private LayoutNodeDef ReadNode(MarkupNode e, int depth)
    {
        Guard(depth);
        var body = new LayoutBody();
        ReadBody(e, body, depth);
        return new LayoutNodeDef { Name = e.GetAttribute("name") ?? string.Empty, StyleLabel = e.GetAttribute("styleLbl") ?? string.Empty, Body = body };
    }

    private void Guard(int depth)
    {
        if (depth > MaxDepth || ++_count > MaxNodes)
        {
            throw new InvalidOperationException("layout definition too large");
        }
    }

    private void ReadBody(MarkupNode e, LayoutBody body, int depth)
    {
        foreach (MarkupNode c in e.ChildElements)
        {
            switch (c.LocalNameString)
            {
                case "alg":
                    {
                        var alg = new AlgorithmDef { Type = c.GetAttribute("type") ?? "tx" };
                        foreach (MarkupNode p in Dx.Children(c, "param"))
                        {
                            if (p.GetAttribute("type") is { } t && p.GetAttribute("val") is { } v)
                            {
                                alg.Parameters[t] = v;
                            }
                        }

                        body.Algorithm = alg;
                        break;
                    }

                case "shape":
                    {
                        var shape = new ShapeDef
                        {
                            Type = c.GetAttribute("type") ?? "none",
                            Rotation = Dx.Num(c.GetAttribute("rot"), 0),
                            HideGeometry = Dx.Bool(c.GetAttribute("hideGeom"), false),
                            ZOrderOffset = Dx.Int(c.GetAttribute("zOrderOff"), 0),
                        };
                        foreach (MarkupNode a in Dx.Children(Dx.Child(c, "adjLst"), "adj"))
                        {
                            shape.Adjust.Add((Dx.Int(a.GetAttribute("idx"), 1), Dx.Num(a.GetAttribute("val"), 0)));
                        }

                        body.Shape = shape;
                        break;
                    }

                case "presOf":
                    body.PresentationOf = IteratorSpec.Read(c);
                    break;
                case "constrLst":
                    foreach (MarkupNode k in Dx.Children(c, "constr"))
                    {
                        body.Constraints.Add(new ConstraintDef
                        {
                            Type = k.GetAttribute("type") ?? "none",
                            For = k.GetAttribute("for") ?? "self",
                            ForName = k.GetAttribute("forName") ?? string.Empty,
                            PointType = k.GetAttribute("ptType") ?? "all",
                            RefType = k.GetAttribute("refType") ?? "none",
                            RefFor = k.GetAttribute("refFor") ?? "self",
                            RefForName = k.GetAttribute("refForName") ?? string.Empty,
                            RefPointType = k.GetAttribute("refPtType") ?? "all",
                            Op = k.GetAttribute("op") ?? "none",
                            Value = Dx.Num(k.GetAttribute("val"), 0),
                            Fact = Dx.Num(k.GetAttribute("fact"), 1),
                        });
                    }

                    break;
                case "ruleLst":
                    foreach (MarkupNode k in Dx.Children(c, "rule"))
                    {
                        body.Rules.Add(new RuleDef
                        {
                            Type = k.GetAttribute("type") ?? "none",
                            For = k.GetAttribute("for") ?? "self",
                            ForName = k.GetAttribute("forName") ?? string.Empty,
                            PointType = k.GetAttribute("ptType") ?? "all",
                            Value = Dx.Num(k.GetAttribute("val"), double.NaN),
                            Fact = Dx.Num(k.GetAttribute("fact"), double.NaN),
                            Max = Dx.Num(k.GetAttribute("max"), double.NaN),
                        });
                    }

                    break;
                case "varLst":
                    foreach (MarkupNode v in c.ChildElements)
                    {
                        body.Variables[v.LocalNameString] = v.GetAttribute("val") ?? string.Empty;
                    }

                    break;
                case "layoutNode":
                    body.Items.Add(ReadNode(c, depth + 1));
                    break;
                case "forEach":
                    {
                        Guard(depth + 1);
                        var inner = new LayoutBody();
                        ReadBody(c, inner, depth + 1);
                        var fe = new ForEachDef { Name = c.GetAttribute("name") ?? string.Empty, Ref = c.GetAttribute("ref") ?? string.Empty, Iterator = IteratorSpec.Read(c), Body = inner };
                        if (fe.Name.Length > 0)
                        {
                            ForEachByName.TryAdd(fe.Name, fe);
                        }

                        body.Items.Add(fe);
                        break;
                    }

                case "choose":
                    {
                        Guard(depth + 1);
                        var ch = new ChooseDef();
                        foreach (MarkupNode b in c.ChildElements)
                        {
                            var inner = new LayoutBody();
                            ReadBody(b, inner, depth + 1);
                            if (b.LocalNameString == "if")
                            {
                                ch.Ifs.Add(new IfDef
                                {
                                    Iterator = IteratorSpec.Read(b),
                                    Function = b.GetAttribute("func") ?? "cnt",
                                    Argument = b.GetAttribute("arg") ?? "none",
                                    Operator = b.GetAttribute("op") ?? "equ",
                                    Value = b.GetAttribute("val") ?? "0",
                                    Body = inner,
                                });
                            }
                            else if (b.LocalNameString == "else")
                            {
                                ch.Else = inner;
                            }
                        }

                        body.Items.Add(ch);
                        break;
                    }
            }
        }
    }
}
