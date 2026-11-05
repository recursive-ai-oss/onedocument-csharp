using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

/// <summary>
/// Everything one part of a presentation (a slide, layout, master or notes page) needs to be painted: its theme and colour map, its
/// pictures, and the hooks that make the shape painter handle placeholders, text, tables and charts.
/// </summary>
internal sealed class PartContext
{
    private readonly RenderSession _session;
    private readonly Dictionary<MarkupNode, Shape> _shapes = [];
    private readonly Dictionary<MarkupNode, ShapeInheritance?> _inheritance = [];
    private readonly Dictionary<string, byte[]?> _images = new(StringComparer.Ordinal);
    private bool _indexed;

    public PartContext(RenderSession session, PresentationPart part)
    {
        _session = session;
        Part = part;
        PresentationPart themeOwner = part switch
        {
            Slide s => s.Master ?? part,
            SlideLayout l => l.Master ?? part,
            NotesSlide n => n.NotesMaster ?? part,
            _ => part,
        };
        Theme = session.ThemeOf(themeOwner);
        Map = RenderSession.ColorMapOf(part);
        Colors = new ColorResolver(Theme, Map);
        Drawing = new DrawingContext(Colors, session.Images, ImageBytes, part.PartName) { Tagged = session.Tagged, Fonts = session.Fonts };
        Drawing.TextPainter = PaintShapeText;
        Drawing.ShapeResolver = ResolveShape;
        Drawing.ShapeTagger = TagShape;
        Drawing.FramePainter = PaintFrame;
        Drawing.ChartPainter = (rel, list, x, y, w, h) =>
        {
            if (Charts.ChartHost.Load(part.Owner.Package, part.PartName, rel) is not { } chart)
            {
                return false;
            }

            Charts.ChartHost.Paint(list, chart.Chart, x, y, w, h, chart.Theme, session.Fonts);
            return true;
        };
    }

    public PresentationPart Part { get; }

    public DrawingTheme Theme { get; }

    public Drawing.ColorMap Map { get; }

    public ColorResolver Colors { get; }

    public DrawingContext Drawing { get; }

    /// <summary>The scheme colour text takes when nothing names one: the map's text colour (<c>tx1</c>).</summary>
    public string TextColorSlot => Map.Slot("tx1");

    private byte[]? ImageBytes(string relationshipId)
    {
        if (_images.TryGetValue(relationshipId, out byte[]? cached))
        {
            return cached;
        }

        byte[]? bytes = null;
        foreach (Relationship r in Part.Relationships)
        {
            if (r.Id == relationshipId && r.TargetMode == RelationshipTargetMode.Internal && OpcPackage.TryResolveTarget(Part.PartName, r, out PartUri target)
                && Part.Owner.Package.FindPart(target.Value) is { } part)
            {
                bytes = part.GetBytes().ToArray();
                break;
            }
        }

        _images[relationshipId] = bytes;
        return bytes;
    }

    /// <summary>The target of an external hyperlink relationship, or null.</summary>
    public string? LinkTarget(string relationshipId)
    {
        foreach (Relationship r in Part.Relationships)
        {
            if (r.Id == relationshipId && r.TargetMode == RelationshipTargetMode.External)
            {
                return r.Target;
            }
        }

        return null;
    }

    private void Index()
    {
        if (_indexed)
        {
            return;
        }

        _indexed = true;
        foreach (Shape s in Part.AllShapes())
        {
            _shapes[s.Markup.Node] = s;
        }
    }

    public Shape? ShapeFor(MarkupNode node)
    {
        Index();
        return _shapes.TryGetValue(node, out Shape? s) ? s : null;
    }

    // ---------------------------------------------------------------- inheritance

    private ShapeInheritance? ResolveShape(MarkupNode node)
    {
        if (_inheritance.TryGetValue(node, out ShapeInheritance? cached))
        {
            return cached;
        }

        ShapeInheritance? result = null;
        if (ShapeFor(node) is { } shape)
        {
            var parents = new List<MarkupNode>();
            foreach (Shape p in shape.InheritanceChain)
            {
                parents.Add(p.Markup.Node);
            }

            long? top = null, height = null;
            if (shape.Kind == ShapeKind.Shape && shape.TextBody is { } body && shape.ResolveBounds() is { } bounds)
            {
                BodyProperties bp = BodyProperties.Read(shape);
                if (bp.Fit == AutofitKind.ResizeShape && _session.MeasureText(this, shape, body, bp, bounds) is { } h)
                {
                    height = (long)Math.Round(h * 12700.0);
                    long delta = bounds.Height - height.Value;
                    top = bp.Anchor switch { "ctr" => bounds.Y + (delta / 2), "b" => bounds.Y + delta, _ => bounds.Y };
                }
            }

            result = new ShapeInheritance { Parents = parents, HeightEmu = height, TopEmu = top };
        }

        _inheritance[node] = result;
        return result;
    }

    private DrawingTag? TagShape(MarkupNode node)
    {
        if (ShapeFor(node) is not { } shape)
        {
            return null;
        }

        string? alt = shape.AltText is { Length: > 0 } a ? a : null;
        bool decorative = IsDecorative(shape);
        switch (shape.Kind)
        {
            case ShapeKind.Picture:
                return decorative ? new DrawingTag(TagRole.Artifact, null) : new DrawingTag(TagRole.Figure, alt);
            case ShapeKind.Connector:
                return new DrawingTag(TagRole.Artifact, null);
            case ShapeKind.GraphicFrame:
                return shape.FrameKind == GraphicFrameKind.Table ? null : decorative ? new DrawingTag(TagRole.Artifact, null) : new DrawingTag(TagRole.Figure, alt);
            default:
                if (decorative)
                {
                    return new DrawingTag(TagRole.Artifact, null);
                }

                return shape.TextBody is { } tb && tb.Text.Length > 0 ? null : alt is not null ? new DrawingTag(TagRole.Figure, alt) : null;
        }
    }

    // The Office 2019 "decorative" mark is an extension element (adec:decorative val="1") under cNvPr.
    private static bool IsDecorative(Shape shape)
    {
        MarkupRef cNvPr = shape.CommonProperties;
        if (!cNvPr.IsValid)
        {
            return false;
        }

        return HasDecorative(cNvPr.Node, 0);
    }

    private static bool HasDecorative(MarkupNode n, int depth)
    {
        if (depth > 4)
        {
            return false;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == "decorative" && c.GetAttribute("val") is "1" or "true")
            {
                return true;
            }

            if (HasDecorative(c, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- text

    /// <summary>The list styles that apply to a shape's text, highest priority first.</summary>
    public List<MarkupNode> Containers(Shape? shape)
    {
        var list = new List<MarkupNode>();
        if (shape is not null)
        {
            if (shape.TextBody?.ListStyle is { IsValid: true } own)
            {
                list.Add(own.Node);
            }

            foreach (Shape s in shape.InheritanceChain)
            {
                if (s.TextBody?.ListStyle is { IsValid: true } inherited)
                {
                    list.Add(inherited.Node);
                }
            }

            PlaceholderInfo? ph = shape.Placeholder;
            switch (Part)
            {
                case Slide or SlideLayout or SlideMaster:
                    SlideMaster? master = Part switch { Slide sl => sl.Master, SlideLayout la => la.Master, SlideMaster m => m, _ => null };
                    if (master is not null)
                    {
                        MarkupRef style = ph is not { } info
                            ? master.OtherStyle
                            : info.Type switch
                            {
                                "title" or "ctrTitle" => master.TitleStyle,
                                "dt" or "ftr" or "sldNum" or "hdr" => master.OtherStyle,
                                _ => master.BodyStyle,
                            };
                        if (style.IsValid)
                        {
                            list.Add(style.Node);
                        }
                    }

                    break;
                case NotesSlide or NotesMaster:
                    NotesMaster? nm = Part is NotesSlide ns ? ns.NotesMaster : (NotesMaster)Part;
                    if (nm?.NotesStyle is { IsValid: true } notes)
                    {
                        list.Add(notes.Node);
                    }

                    break;
            }
        }

        if (Part.Owner.DefaultTextStyle is { IsValid: true } deflt)
        {
            list.Add(deflt.Node);
        }

        return list;
    }

    public StyleReader Styles(Shape? shape) => new(Containers(shape), Colors, Theme, TextColorSlot);

    private void PaintShapeText(ShapeTextRequest req) => _session.PaintShapeText(this, req);

    private bool PaintFrame(MarkupNode frame, DisplayList list, double x, double y, double w, double h) => _session.PaintFrame(this, frame, list, x, y, w, h);
}

/// <summary>Body properties after inheritance. ECMA-376 Part 1 §21.1.2.1.1 (bodyPr).</summary>
internal sealed class BodyProperties
{
    public double Left = 7.2, Top = 3.6, Right = 7.2, Bottom = 3.6;
    public string Anchor = "t";
    public bool AnchorCenter;
    public bool Wrap = true;
    public string Vertical = "horz";
    public int Columns = 1;
    public double ColumnSpacing;
    public double Rotation;
    public bool Upright;
    public AutofitKind Fit = AutofitKind.None;
    public double? FontScale;
    public double? LineReduction;

    public static BodyProperties Read(Shape shape)
    {
        var sources = new List<MarkupNode>();
        if (shape.TextBody?.BodyProperties is { IsValid: true } own)
        {
            sources.Add(own.Node);
        }

        foreach (Shape s in shape.InheritanceChain)
        {
            if (s.TextBody?.BodyProperties is { IsValid: true } inherited)
            {
                sources.Add(inherited.Node);
            }
        }

        return Read(sources);
    }

    public static BodyProperties Read(IReadOnlyList<MarkupNode> sources)
    {
        var b = new BodyProperties();
        string? Attr(string name)
        {
            foreach (MarkupNode n in sources)
            {
                if (n.GetAttribute(name) is { } v)
                {
                    return v;
                }
            }

            return null;
        }

        double Emu(string name, double fallbackPoints) =>
            Attr(name) is { } v && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double e) && double.IsFinite(e)
                ? e / 12700.0
                : fallbackPoints;

        // ECMA-376 Part 1 §21.1.2.1.1: insets default to 0.1 inch left and right, 0.05 inch top and bottom.
        b.Left = Math.Clamp(Emu("lIns", 7.2), 0, 5000);
        b.Top = Math.Clamp(Emu("tIns", 3.6), 0, 5000);
        b.Right = Math.Clamp(Emu("rIns", 7.2), 0, 5000);
        b.Bottom = Math.Clamp(Emu("bIns", 3.6), 0, 5000);
        b.Anchor = Attr("anchor") ?? "t";
        b.AnchorCenter = Attr("anchorCtr") is "1" or "true";
        b.Wrap = Attr("wrap") != "none";
        b.Vertical = Attr("vert") ?? "horz";
        b.Columns = Attr("numCol") is { } nc && int.TryParse(nc, out int ncv) ? Math.Clamp(ncv, 1, 16) : 1;
        b.ColumnSpacing = Math.Clamp(Emu("spcCol", 0), 0, 5000);
        b.Rotation = Attr("rot") is { } r && double.TryParse(r, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double rv) ? rv / 60000.0 : 0;
        b.Upright = Attr("upright") is "1" or "true";
        foreach (MarkupNode n in sources)
        {
            MarkupNode norm = DrawingTheme.Child(n, "normAutofit");
            if (!norm.IsNull)
            {
                b.Fit = AutofitKind.ShrinkText;
                if (norm.GetAttribute("fontScale") is { } fs && double.TryParse(fs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fsv))
                {
                    b.FontScale = Math.Clamp(fsv / 100000.0, 0.01, 1.0);
                }

                if (norm.GetAttribute("lnSpcReduction") is { } lr && double.TryParse(lr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lrv))
                {
                    b.LineReduction = Math.Clamp(lrv / 100000.0, 0.0, 0.9);
                }

                break;
            }

            if (!DrawingTheme.Child(n, "spAutoFit").IsNull)
            {
                b.Fit = AutofitKind.ResizeShape;
                break;
            }

            if (!DrawingTheme.Child(n, "noAutofit").IsNull)
            {
                break;
            }
        }

        return b;
    }
}
