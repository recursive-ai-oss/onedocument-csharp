using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

// Small readers over DiagramML and DrawingML markup. Names are compared without regard to prefix, as the rest of the renderer does.
internal static class Dx
{
    public const string Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    public const string Dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";
    public const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static MarkupNode Child(MarkupNode parent, string local)
    {
        if (parent.IsNull)
        {
            return default;
        }

        for (MarkupNode c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element && c.LocalName.SequenceEqual(Encoding.UTF8.GetBytes(local)))
            {
                return c;
            }
        }

        return default;
    }

    public static IEnumerable<MarkupNode> Children(MarkupNode parent, string local)
    {
        if (parent.IsNull)
        {
            yield break;
        }

        for (MarkupNode c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element && c.LocalNameString == local)
            {
                yield return c;
            }
        }
    }

    // The value of an attribute by local name, namespace and prefix aside (r:dm, r:id).
    public static string? AnyAttr(MarkupNode e, string local)
    {
        byte[] name = Encoding.UTF8.GetBytes(local);
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual(name))
            {
                return a.Value;
            }
        }

        return null;
    }

    public static double Num(string? s, double fallback) =>
        s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : fallback;

    public static int Int(string? s, int fallback) =>
        s is not null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    public static bool Bool(string? s, bool fallback) => s switch
    {
        "1" or "true" or "on" => true,
        "0" or "false" or "off" => false,
        _ => fallback,
    };

    // ECMA-376 Part 1 §21.4.2: iterator attributes are xsd lists (axis="ch desOrSelf"); an empty or missing value is an empty list.
    public static string[] List(string? s) => string.IsNullOrWhiteSpace(s) ? [] : s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case < ' ' when c is not ('\t' or '\n' or '\r'): break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    public static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    public static long Emu(double points) => (long)Math.Round(points * 12700.0);
}

/// <summary>The kind of a point in a diagram's data model (ECMA-376 Part 1 §21.4.7.55, ST_PtType).</summary>
internal enum DiagramPointType
{
    /// <summary>An ordinary node.</summary>
    Node,

    /// <summary>An assistant node.</summary>
    Assistant,

    /// <summary>The document point at the root of the tree.</summary>
    Document,

    /// <summary>A presentation point (what the layout produced).</summary>
    Presentation,

    /// <summary>The transition between a parent and a child.</summary>
    ParentTransition,

    /// <summary>The transition between a node and its following sibling.</summary>
    SiblingTransition,
}

/// <summary>A point of the data model: a node with text, a transition, or a presentation point.</summary>
internal sealed class DiagramPoint
{
    public required string Id { get; init; }

    public DiagramPointType Type { get; init; }

    /// <summary>The <c>dgm:t</c> text body, or a null node.</summary>
    public MarkupNode Text { get; init; }

    /// <summary>The point's <c>dgm:spPr</c> (user formatting), or a null node.</summary>
    public MarkupNode ShapeProperties { get; init; }

    public string? PresAssocId { get; init; }

    public string? PresName { get; init; }

    public string? PresStyleLabel { get; init; }

    public int? PresStyleIndex { get; init; }

    public int? PresStyleCount { get; init; }

    /// <summary>Layout variables stored with the point (<c>dgm:presLayoutVars</c>), by element name.</summary>
    public IReadOnlyDictionary<string, string> LayoutVariables { get; init; } = new Dictionary<string, string>();

    /// <summary>The plain text of the point: paragraphs joined by a line feed.</summary>
    public string PlainText
    {
        get
        {
            if (Text.IsNull)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            bool first = true;
            foreach (MarkupNode p in Dx.Children(Text, "p"))
            {
                if (!first)
                {
                    sb.Append('\n');
                }

                first = false;
                AppendParagraph(sb, p);
            }

            return sb.ToString();
        }
    }

    internal static void AppendParagraph(StringBuilder sb, MarkupNode p)
    {
        foreach (MarkupNode r in p.ChildElements)
        {
            switch (r.LocalNameString)
            {
                case "r" or "fld":
                    sb.Append(Dx.Child(r, "t").InnerText);
                    break;
                case "br":
                    sb.Append('\v');
                    break;
            }
        }
    }
}

/// <summary>A connection of the data model.</summary>
internal sealed class DiagramConnection
{
    public required string Id { get; init; }

    /// <summary><c>parOf</c> (default), <c>presOf</c>, <c>presParOf</c> or <c>unknownRelationship</c>.</summary>
    public string Type { get; init; } = "parOf";

    public required string SourceId { get; init; }

    public required string DestinationId { get; init; }

    public int SourceOrder { get; init; }

    public string? ParentTransitionId { get; init; }

    public string? SiblingTransitionId { get; init; }
}

/// <summary>
/// A diagram's data model (<c>dgm:dataModel</c>, ECMA-376 Part 1 §21.4.3.1): points and the parent-of connections that make the tree,
/// with the transitions that sit between a node and its children and between siblings.
/// </summary>
internal sealed class DiagramData
{
    private const int MaxPoints = 20_000;
    private readonly Dictionary<string, DiagramPoint> _points = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DiagramConnection>> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DiagramConnection> _parentOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DiagramConnection> _byTransition = new(StringComparer.Ordinal);

    private DiagramData(MarkupDocument source)
    {
        Source = source;
    }

    public MarkupDocument Source { get; }

    public IReadOnlyList<DiagramPoint> Points { get; private set; } = [];

    public IReadOnlyList<DiagramConnection> Connections { get; private set; } = [];

    /// <summary>The point of type <c>doc</c>.</summary>
    public DiagramPoint Document { get; private set; } = null!;

    /// <summary>The relationship id of the cached drawing (<c>dsp:dataModelExt/@relId</c>), or null.</summary>
    public string? DrawingRelationshipId { get; private set; }

    public DiagramPoint? Find(string id) => _points.TryGetValue(id, out DiagramPoint? p) ? p : null;

    /// <summary>The parent-of connections whose source is the given point, in order.</summary>
    public IReadOnlyList<DiagramConnection> ChildConnections(string id) => _children.TryGetValue(id, out List<DiagramConnection>? l) ? l : [];

    /// <summary>The connection that makes a point a child, or null for the document point.</summary>
    public DiagramConnection? ParentConnection(string childId) => _parentOf.TryGetValue(childId, out DiagramConnection? c) ? c : null;

    /// <summary>The connection a transition point belongs to.</summary>
    public DiagramConnection? ConnectionOfTransition(string transitionId) => _byTransition.TryGetValue(transitionId, out DiagramConnection? c) ? c : null;

    /// <summary>The ids of the layout-affecting variables of the document point.</summary>
    public IReadOnlyDictionary<string, string> RootVariables => Document.LayoutVariables;

    /// <summary>Reads a data model.</summary>
    /// <param name="doc">The parsed <c>dgm:dataModel</c> part.</param>
    /// <returns>The model, or null when it has no document point or is too large.</returns>
    public static DiagramData? TryRead(MarkupDocument doc)
    {
        MarkupNode root = doc.Root;
        if (root.IsNull || root.LocalNameString != "dataModel")
        {
            return null;
        }

        var data = new DiagramData(doc);
        var points = new List<DiagramPoint>();
        foreach (MarkupNode pt in Dx.Children(Dx.Child(root, "ptLst"), "pt"))
        {
            if (points.Count >= MaxPoints)
            {
                return null;
            }

            if (pt.GetAttribute("modelId") is not { } id)
            {
                continue;
            }

            MarkupNode prSet = Dx.Child(pt, "prSet");
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (MarkupNode v in Dx.Child(prSet, "presLayoutVars").ChildElements)
            {
                vars[v.LocalNameString] = v.GetAttribute("val") ?? string.Empty;
            }

            var point = new DiagramPoint
            {
                Id = id,
                Type = pt.GetAttribute("type") switch
                {
                    "asst" => DiagramPointType.Assistant,
                    "doc" => DiagramPointType.Document,
                    "pres" => DiagramPointType.Presentation,
                    "parTrans" => DiagramPointType.ParentTransition,
                    "sibTrans" => DiagramPointType.SiblingTransition,
                    _ => DiagramPointType.Node,
                },
                Text = Dx.Child(pt, "t"),
                ShapeProperties = Dx.Child(pt, "spPr"),
                PresAssocId = prSet.IsNull ? null : prSet.GetAttribute("presAssocID"),
                PresName = prSet.IsNull ? null : prSet.GetAttribute("presName"),
                PresStyleLabel = prSet.IsNull ? null : prSet.GetAttribute("presStyleLbl"),
                PresStyleIndex = prSet.IsNull || prSet.GetAttribute("presStyleIdx") is not { } si ? null : Dx.Int(si, 0),
                PresStyleCount = prSet.IsNull || prSet.GetAttribute("presStyleCnt") is not { } sc ? null : Dx.Int(sc, 0),
                LayoutVariables = vars,
            };
            points.Add(point);
            data._points[id] = point;
            if (point.Type == DiagramPointType.Document)
            {
                data.Document ??= point;
            }
        }

        if (data.Document is null)
        {
            // A data model without its document point is damaged but still names the stored drawing; read it as an empty tree.
            var synthetic = new DiagramPoint { Id = string.Empty, Type = DiagramPointType.Document };
            points.Add(synthetic);
            data._points[string.Empty] = synthetic;
            data.Document = synthetic;
        }

        var cxns = new List<DiagramConnection>();
        foreach (MarkupNode c in Dx.Children(Dx.Child(root, "cxnLst"), "cxn"))
        {
            if (c.GetAttribute("modelId") is not { } id || c.GetAttribute("srcId") is not { } src || c.GetAttribute("destId") is not { } dst)
            {
                continue;
            }

            var cx = new DiagramConnection
            {
                Id = id,
                Type = c.GetAttribute("type") ?? "parOf",
                SourceId = src,
                DestinationId = dst,
                SourceOrder = Dx.Int(c.GetAttribute("srcOrd"), 0),
                ParentTransitionId = c.GetAttribute("parTransId"),
                SiblingTransitionId = c.GetAttribute("sibTransId"),
            };
            cxns.Add(cx);
            if (cx.Type != "parOf")
            {
                continue;
            }

            if (!data._children.TryGetValue(src, out List<DiagramConnection>? list))
            {
                data._children[src] = list = [];
            }

            list.Add(cx);
            data._parentOf.TryAdd(dst, cx);
            if (cx.ParentTransitionId is { Length: > 0 } pt0)
            {
                data._byTransition[pt0] = cx;
            }

            if (cx.SiblingTransitionId is { Length: > 0 } st0)
            {
                data._byTransition[st0] = cx;
            }
        }

        foreach (List<DiagramConnection> l in data._children.Values)
        {
            // Stable by source order so equal orders keep file order.
            List<DiagramConnection> sorted = [.. l.Select((c, i) => (c, i)).OrderBy(t => t.c.SourceOrder).ThenBy(t => t.i).Select(t => t.c)];
            l.Clear();
            l.AddRange(sorted);
        }

        data.Points = points;
        data.Connections = cxns;

        // [MS-ODRAWXML] §2.3.1 (dataModelExt): the cached drawing's relationship id is stored in the data model's extension list.
        foreach (MarkupNode ext in Dx.Children(Dx.Child(root, "extLst"), "ext"))
        {
            foreach (MarkupNode e in ext.ChildElements)
            {
                if (e.LocalNameString == "dataModelExt" && e.GetAttribute("relId") is { } rid)
                {
                    data.DrawingRelationshipId = rid;
                }
            }
        }

        return data;
    }

    /// <summary>The text of every node and assistant point that has any, in tree order.</summary>
    public IEnumerable<string> NodeTexts()
    {
        var stack = new Stack<(string Id, int Depth)>();
        stack.Push((Document.Id, 0));
        int guard = 0;
        while (stack.Count > 0 && guard++ < MaxPoints * 2)
        {
            (string id, int depth) = stack.Pop();
            if (Find(id) is { } p && p.Type is DiagramPointType.Node or DiagramPointType.Assistant && p.PlainText.Trim() is { Length: > 0 } t)
            {
                yield return t;
            }

            if (depth > 64)
            {
                continue;
            }

            IReadOnlyList<DiagramConnection> kids = ChildConnections(id);
            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push((kids[i].DestinationId, depth + 1));
            }
        }
    }
}
