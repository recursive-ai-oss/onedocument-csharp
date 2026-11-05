using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>What a shape is, by the element that holds it.</summary>
public enum ShapeKind
{
    /// <summary>Something the model does not interpret (<c>p:contentPart</c>, <c>p:control</c>, an unknown element). It is kept as it is.</summary>
    Other = 0,

    /// <summary>A shape or text box (<c>p:sp</c>).</summary>
    Shape = 1,

    /// <summary>A picture (<c>p:pic</c>).</summary>
    Picture = 2,

    /// <summary>A group of shapes (<c>p:grpSp</c>).</summary>
    Group = 3,

    /// <summary>A graphic frame (<c>p:graphicFrame</c>): a table, chart, diagram, embedded object or other graphic.</summary>
    GraphicFrame = 4,

    /// <summary>A connector (<c>p:cxnSp</c>).</summary>
    Connector = 5,

    /// <summary>An <c>mc:AlternateContent</c> wrapper; <see cref="Shape.Children"/> are the shapes of the branch a reader that understands it takes (the first <c>mc:Choice</c>, else the fallback).</summary>
    AlternateContent = 6,
}

/// <summary>What a graphic frame holds.</summary>
public enum GraphicFrameKind
{
    /// <summary>Not a graphic frame, or something the model does not recognise.</summary>
    Other = 0,

    /// <summary>A table (<c>a:tbl</c>); see <see cref="Shape.Table"/>.</summary>
    Table = 1,

    /// <summary>A chart (<c>c:chart</c>); the chart part is reachable through <see cref="Shape.References"/>.</summary>
    Chart = 2,

    /// <summary>A diagram (SmartArt); its parts are reachable through <see cref="Shape.References"/>.</summary>
    Diagram = 3,

    /// <summary>An embedded or linked object (<c>p:oleObj</c>).</summary>
    OleObject = 4,
}

/// <summary>The placeholder a shape stands for (<c>p:ph</c>).</summary>
/// <param name="Type">The type (<c>title</c>, <c>ctrTitle</c>, <c>subTitle</c>, <c>body</c>, <c>dt</c>, <c>ftr</c>, <c>sldNum</c>, <c>pic</c>…); <c>obj</c> when the attribute is absent.</param>
/// <param name="Index">The index (<c>idx</c>), 0 when absent. It ties a slide's placeholder to its layout's.</param>
/// <param name="Size">The size hint (<c>sz</c>: <c>full</c>, <c>half</c>, <c>quarter</c>), or null.</param>
/// <param name="Orientation">The orientation (<c>orient</c>: <c>horz</c>, <c>vert</c>), or null.</param>
/// <param name="HasType">True when the <c>type</c> attribute was written.</param>
public readonly record struct PlaceholderInfo(string Type, int Index, string? Size, string? Orientation, bool HasType);

/// <summary>A position and size in EMU (914,400 per inch).</summary>
/// <param name="X">The left edge (<c>off/@x</c>).</param>
/// <param name="Y">The top edge (<c>off/@y</c>).</param>
/// <param name="Width"><c>ext/@cx</c>.</param>
/// <param name="Height"><c>ext/@cy</c>.</param>
public readonly record struct ShapeBounds(long X, long Y, long Width, long Height);

/// <summary>A relationship a shape's markup names (<c>r:embed</c>, <c>r:id</c>, <c>r:link</c>, <c>r:dm</c>…).</summary>
/// <param name="RelationshipId">The id, as written.</param>
/// <param name="Type">The relationship type, or null when the part has no such relationship (the shape is left as it is).</param>
/// <param name="PartName">The part the relationship targets, or null for an external, dangling or missing target.</param>
/// <param name="External">True when the relationship is external (a hyperlink, a linked picture); nothing is fetched.</param>
public readonly record struct ShapeReference(string RelationshipId, string? Type, string? PartName, bool External);

/// <summary>
/// One shape of a slide, layout, master or notes page: a view over its element, created when asked for and holding nothing but the
/// reference. Edits go to the markup tree; the shape is never a copy.
/// </summary>
public sealed partial class Shape
{
    private Shape(PresentationPart part, MarkupRef element, Shape? group, ShapeKind kind)
    {
        Part = part;
        Markup = element;
        Group = group;
        Kind = kind;
    }

    /// <summary>Gets the part that holds the shape.</summary>
    public PresentationPart Part { get; }

    /// <summary>Gets the shape's element: the hop from the hub to the markup layer.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the group the shape is in, or null at the top of the shape tree.</summary>
    public Shape? Group { get; }

    /// <summary>Gets what the shape is.</summary>
    public ShapeKind Kind { get; }

    internal static List<Shape> ReadChildren(PresentationPart part, MarkupRef container, Shape? group, int depth)
    {
        var list = new List<Shape>();
        if (!container.IsValid)
        {
            return list;
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), part.Owner.Limits.MaxRecursionDepth, depth, DefectLocation.InPart(part.PartName, container.Node.SourceStart));
        foreach (MarkupNode child in container.Node.ChildElements)
        {
            var r = new MarkupRef(child);
            if (MarkupCompatibility.IsAlternateContent(child))
            {
                list.Add(new Shape(part, r, group, ShapeKind.AlternateContent) { _depth = depth + 1 });
                continue;
            }

            if (r.Namespace != SchemaNamespace.Presentation)
            {
                continue;
            }

            ShapeKind? kind = child.LocalNameString switch
            {
                "sp" => ShapeKind.Shape,
                "pic" => ShapeKind.Picture,
                "grpSp" => ShapeKind.Group,
                "graphicFrame" => ShapeKind.GraphicFrame,
                "cxnSp" => ShapeKind.Connector,
                "nvGrpSpPr" or "grpSpPr" or "extLst" => null,
                _ => ShapeKind.Other,
            };
            if (kind is { } k)
            {
                list.Add(new Shape(part, r, group, k) { _depth = depth + 1 });
            }
        }

        return list;
    }

    private int _depth;

    /// <summary>Gets the shapes inside a group, or the shapes of the branch an <c>mc:AlternateContent</c> wrapper selects; empty for other shapes.</summary>
    public IReadOnlyList<Shape> Children
    {
        get
        {
            switch (Kind)
            {
                case ShapeKind.Group:
                    return ReadChildren(Part, Markup, this, _depth);
                case ShapeKind.AlternateContent:
                    MarkupRef? branch = null;
                    foreach (MarkupNode b in Markup.Node.ChildElements)
                    {
                        if (MarkupCompatibility.IsChoice(b) && branch is null)
                        {
                            branch = new MarkupRef(b);
                        }
                        else if (MarkupCompatibility.IsFallback(b) && branch is null)
                        {
                            branch = new MarkupRef(b);
                        }
                    }

                    return branch is null ? [] : ReadChildren(Part, branch.Value, Group, _depth);
                default:
                    return [];
            }
        }
    }

    // The non-visual properties element: nvSpPr, nvPicPr, nvGrpSpPr, nvGraphicFramePr or nvCxnSpPr.
    private MarkupRef NonVisual
    {
        get
        {
            foreach (MarkupNode c in Markup.Node.ChildElements)
            {
                string n = c.LocalNameString;
                if (n.StartsWith("nv", StringComparison.Ordinal) && n.EndsWith("Pr", StringComparison.Ordinal))
                {
                    return new MarkupRef(c);
                }
            }

            return default;
        }
    }

    /// <summary>Gets the <c>p:cNvPr</c> element (id, name, description), or an invalid reference.</summary>
    public MarkupRef CommonProperties => Pml.P(NonVisual, "cNvPr") ?? default;

    /// <summary>Gets the shape id (<c>cNvPr/@id</c>), unique within its part; null when absent.</summary>
    public long? Id => Pml.AttrLong(CommonProperties, "id");

    /// <summary>Gets or sets the shape's name (<c>cNvPr/@name</c>); empty when absent. Setting it on a shape without <c>cNvPr</c> does nothing.</summary>
    public string Name
    {
        get => Pml.Attr(CommonProperties, "name") ?? string.Empty;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (CommonProperties is { IsValid: true } c)
            {
                c.Node.SetAttribute("name", value);
            }
        }
    }

    /// <summary>Gets or sets the alternative text (<c>cNvPr/@descr</c>), or null. Setting null removes it.</summary>
    public string? AltText
    {
        get => Pml.Attr(CommonProperties, "descr");
        set
        {
            if (CommonProperties is not { IsValid: true } c)
            {
                return;
            }

            if (value is null)
            {
                c.Node.RemoveAttribute("descr");
            }
            else
            {
                c.Node.SetAttribute("descr", value);
            }
        }
    }

    /// <summary>Gets whether the shape is hidden in the selection pane (<c>cNvPr/@hidden</c>).</summary>
    public bool IsHidden => Pml.AttrBool(CommonProperties, "hidden") ?? false;

    /// <summary>Gets the placeholder the shape stands for, or null when it is not a placeholder.</summary>
    public PlaceholderInfo? Placeholder
    {
        get
        {
            if (Kind is ShapeKind.AlternateContent or ShapeKind.Other || Pml.P(NonVisual, "nvPr") is not { } nvPr || Pml.P(nvPr, "ph") is not { } ph)
            {
                return null;
            }

            // ECMA-376 Part 1 §19.3.1.36 (ph): type defaults to obj and idx to 0.
            string? type = Pml.Attr(ph, "type");
            return new PlaceholderInfo(type ?? "obj", Pml.AttrInt(ph, "idx") ?? 0, Pml.Attr(ph, "sz"), Pml.Attr(ph, "orient"), type is not null);
        }
    }

    // The element that holds the transform: a:xfrm in spPr / grpSpPr, or p:xfrm in a graphic frame.
    private MarkupRef? TransformElement
    {
        get
        {
            switch (Kind)
            {
                case ShapeKind.GraphicFrame:
                    return Pml.P(Markup, "xfrm");
                case ShapeKind.Group:
                    return Pml.P(Markup, "grpSpPr") is { } g ? Pml.A(g, "xfrm") : null;
                case ShapeKind.Shape or ShapeKind.Picture or ShapeKind.Connector:
                    return Pml.P(Markup, "spPr") is { } sp ? Pml.A(sp, "xfrm") : null;
                default:
                    return null;
            }
        }
    }

    /// <summary>Gets the shape's own position and size, or null when it has no transform (a placeholder then takes them from its layout).</summary>
    public ShapeBounds? Bounds
    {
        get
        {
            if (TransformElement is not { } xfrm)
            {
                return null;
            }

            MarkupRef? off = Pml.A(xfrm, "off");
            MarkupRef? ext = Pml.A(xfrm, "ext");
            if (off is null || ext is null)
            {
                return null;
            }

            return new ShapeBounds(Pml.AttrLong(off.Value, "x") ?? 0, Pml.AttrLong(off.Value, "y") ?? 0, Pml.AttrLong(ext.Value, "cx") ?? 0, Pml.AttrLong(ext.Value, "cy") ?? 0);
        }
    }

    /// <summary>Gets the shape's rotation in 60,000ths of a degree (<c>xfrm/@rot</c>), or 0.</summary>
    public long Rotation => TransformElement is { } x ? Pml.AttrLong(x, "rot") ?? 0 : 0;

    /// <summary>Gets the shape's text body (<c>p:txBody</c>), or null when it has none (a picture, a table frame, a placeholder the slide has not filled).</summary>
    public TextBody? TextBody
    {
        get
        {
            if (Kind != ShapeKind.Shape || Pml.P(Markup, "txBody") is not { } tx)
            {
                return null;
            }

            return new TextBody(this, tx);
        }
    }

    /// <summary>Gets what a graphic frame holds; <see cref="GraphicFrameKind.Other"/> for other shapes.</summary>
    public GraphicFrameKind FrameKind
    {
        get
        {
            string? uri = GraphicDataUri;
            if (uri is null)
            {
                return GraphicFrameKind.Other;
            }

            if (GraphicData is { } data && Pml.A(data, "tbl") is not null)
            {
                return GraphicFrameKind.Table;
            }

            if (uri.EndsWith("/chart", StringComparison.Ordinal) || uri.EndsWith("/chartex", StringComparison.Ordinal))
            {
                return GraphicFrameKind.Chart;
            }

            if (uri.EndsWith("/diagram", StringComparison.Ordinal))
            {
                return GraphicFrameKind.Diagram;
            }

            return uri.EndsWith("/ole", StringComparison.Ordinal) ? GraphicFrameKind.OleObject : GraphicFrameKind.Other;
        }
    }

    private MarkupRef? GraphicData => Kind == ShapeKind.GraphicFrame && Pml.A(Markup, "graphic") is { } g ? Pml.A(g, "graphicData") : null;

    /// <summary>Gets the <c>uri</c> of a graphic frame's <c>a:graphicData</c> (it names the kind of graphic), or null.</summary>
    public string? GraphicDataUri => GraphicData is { } d ? Pml.Attr(d, "uri") : null;

    /// <summary>Gets the table of a graphic frame that holds one, or null.</summary>
    public Table? Table => GraphicData is { } d && Pml.A(d, "tbl") is { } tbl ? new Table(this, tbl) : null;

    /// <summary>Gets the relationship id of a picture's image (<c>a:blip/@r:embed</c>), or null.</summary>
    public string? ImageRelationshipId
    {
        get
        {
            if (Kind != ShapeKind.Picture || Pml.P(Markup, "blipFill") is not { } fill || Pml.A(fill, "blip") is not { } blip)
            {
                return null;
            }

            return Pml.RelAttr(blip, "embed");
        }
    }

    /// <summary>Gets the part name of a picture's image, or null when the shape is not a picture, the image is linked, or the part is missing. The image is never decoded.</summary>
    public string? ImagePartName
    {
        get
        {
            string? id = ImageRelationshipId;
            Relationship? rel = id is null ? null : Part.Owner.FindRelationship(Part.PartName, id);
            return rel is null ? null : Part.Owner.ResolveTarget(Part.PartName, rel);
        }
    }

    /// <summary>
    /// Lists every relationship the shape's markup names, in document order: pictures, charts, diagrams, embedded objects, media and
    /// hyperlinks. A shape that is duplicated or deleted takes these parts along (see <see cref="Presentation.DuplicateSlide"/>).
    /// </summary>
    /// <returns>The references; empty when the shape names none.</returns>
    public IReadOnlyList<ShapeReference> References
    {
        get
        {
            var list = new List<ShapeReference>();
            var stack = new Stack<MarkupNode>();
            stack.Push(Markup.Node);
            // Document order: an explicit stack visits children right to left, so push them reversed.
            while (stack.Count > 0)
            {
                MarkupNode n = stack.Pop();
                foreach (MarkupAttributeRef a in n.Attributes)
                {
                    if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && SchemaNamespaces.Resolve(a.NamespaceUriBytes) == SchemaNamespace.Relationships)
                    {
                        string id = a.Value;
                        Relationship? rel = Part.Owner.FindRelationship(Part.PartName, id);
                        list.Add(new ShapeReference(
                            id,
                            rel?.Type,
                            rel is null ? null : Part.Owner.ResolveTarget(Part.PartName, rel),
                            rel?.TargetMode == RelationshipTargetMode.External));
                    }
                }

                var kids = new List<MarkupNode>();
                foreach (MarkupNode c in n.ChildElements)
                {
                    kids.Add(c);
                }

                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    stack.Push(kids[i]);
                }
            }

            return list;
        }
    }

    /// <summary>Gets the text of the shape (a text body's paragraphs, or a table's cells), or an empty string.</summary>
    public string Text
    {
        get
        {
            if (TextBody is { } body)
            {
                return body.Text;
            }

            if (Table is { } table)
            {
                var cells = new List<string>();
                foreach (TableRow row in table.Rows)
                {
                    foreach (TableCell cell in row.Cells)
                    {
                        if (!cell.IsMergedContinuation)
                        {
                            cells.Add(cell.Text);
                        }
                    }
                }

                return string.Join('\n', cells);
            }

            return string.Empty;
        }
    }
}
