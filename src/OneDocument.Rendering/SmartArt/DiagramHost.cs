using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

/// <summary>When a diagram's layout is run instead of painting the drawing Office stored with it.</summary>
public enum DiagramRegeneration
{
    /// <summary>Paint the stored drawing; run the layout when there is none, or when the data model's text is not in it (the diagram was edited without being refreshed). The default.</summary>
    WhenMissingOrStale = 0,

    /// <summary>Always run the layout, as after an edit. The stored drawing is ignored.</summary>
    Always = 1,

    /// <summary>Never run the layout; a diagram without a usable stored drawing is not painted.</summary>
    Never = 2,
}

/// <summary>Where the shapes painted for a diagram came from.</summary>
public enum DiagramSource
{
    /// <summary>Nothing was painted.</summary>
    None = 0,

    /// <summary>The drawing stored with the diagram (<c>dsp:drawing</c>).</summary>
    Cached = 1,

    /// <summary>Shapes made by running the layout definition.</summary>
    Regenerated = 2,
}

/// <summary>What happened to one diagram frame.</summary>
/// <param name="HostPart">The part holding the frame (a slide, a document part, a worksheet drawing).</param>
/// <param name="DataPart">The diagram's data part, or null when it could not be found.</param>
/// <param name="Source">Where the painted shapes came from.</param>
/// <param name="ShapeCount">How many shapes were painted.</param>
/// <param name="Reason">Why the layout was run (<c>missing</c>, <c>stale</c>, <c>forced</c>), or why nothing was painted (<c>data-unreadable</c>, <c>layout-unreadable</c>, <c>regeneration-off</c>); null for a stored drawing.</param>
public sealed record DiagramReport(string HostPart, string? DataPart, DiagramSource Source, int ShapeCount, string? Reason);

/// <summary>Options for painting diagrams.</summary>
public sealed class DiagramOptions
{
    /// <summary>Gets or sets when the layout is run. Default <see cref="DiagramRegeneration.WhenMissingOrStale"/>.</summary>
    public DiagramRegeneration Regeneration { get; set; }

    /// <summary>Gets or sets a callback that receives one report per diagram painted. Default null.</summary>
    public Action<DiagramReport>? Report { get; set; }
}

/// <summary>The relationship ids a diagram frame (<c>dgm:relIds</c>) carries, relative to the part that holds the frame.</summary>
/// <param name="Data">The data model part (<c>r:dm</c>).</param>
/// <param name="Layout">The layout definition part (<c>r:lo</c>), or null.</param>
/// <param name="QuickStyle">The quick-style part (<c>r:qs</c>), or null.</param>
/// <param name="Colors">The colour-transform part (<c>r:cs</c>), or null.</param>
public sealed record DiagramReferences(string Data, string? Layout, string? QuickStyle, string? Colors)
{
    internal static DiagramReferences? Read(MarkupNode relIds) =>
        Dx.AnyAttr(relIds, "dm") is { Length: > 0 } dm ? new DiagramReferences(dm, Dx.AnyAttr(relIds, "lo"), Dx.AnyAttr(relIds, "qs"), Dx.AnyAttr(relIds, "cs")) : null;
}

/// <summary>A diagram frame found in a package.</summary>
/// <param name="HostPart">The part that holds the frame.</param>
/// <param name="References">The frame's relationship ids.</param>
/// <param name="WidthPoints">The frame's width in points, or null when the frame states none.</param>
/// <param name="HeightPoints">The frame's height in points, or null.</param>
public sealed record DiagramLocation(string HostPart, DiagramReferences References, double? WidthPoints = null, double? HeightPoints = null);

/// <summary>A request from the drawing painter to paint a diagram frame.</summary>
/// <param name="References">The frame's relationship ids.</param>
/// <param name="List">The display list; coordinates are CSS pixels.</param>
/// <param name="NextId">Allocates item ids.</param>
/// <param name="Context">The painter's context: colours, fonts, notes, tagging.</param>
/// <param name="X">Left of the frame in CSS pixels.</param>
/// <param name="Y">Top of the frame in CSS pixels.</param>
/// <param name="Width">Width of the frame in CSS pixels.</param>
/// <param name="Height">Height of the frame in CSS pixels.</param>
/// <param name="AltText">The frame's alternative text, or null.</param>
/// <param name="InFigure">True when the caller already opened a <c>Figure</c> tag for the frame.</param>
public sealed record DiagramPaintRequest(DiagramReferences References, DisplayList List, Func<ItemId> NextId, DrawingContext Context, double X, double Y, double Width, double Height, string? AltText, bool InFigure);

/// <summary>A shape of a diagram, as stored or as the layout produced it.</summary>
/// <param name="Name">The layout node's name (for a regenerated shape).</param>
/// <param name="Geometry">The preset geometry name.</param>
/// <param name="X">Left in points from the frame's left.</param>
/// <param name="Y">Top in points from the frame's top.</param>
/// <param name="Width">Width in points.</param>
/// <param name="Height">Height in points.</param>
/// <param name="Rotation">Rotation in degrees clockwise.</param>
/// <param name="Text">The shape's text, paragraphs joined by a line feed.</param>
/// <param name="FontSize">The primary font size chosen, in points; 0 for a shape without text.</param>
/// <param name="StyleLabel">The style label whose colours the shape takes.</param>
/// <param name="IsConnector">True for a connector or block arrow between two nodes.</param>
public sealed record DiagramShape(string Name, string Geometry, double X, double Y, double Width, double Height, double Rotation, string Text, double FontSize, string StyleLabel, bool IsConnector);

/// <summary>The result of running a diagram's layout.</summary>
/// <param name="Shapes">The shapes in drawing order.</param>
/// <param name="Drawing">The shapes as a <c>dsp:drawing</c> part, the form Office caches.</param>
/// <param name="Notes">Notes about what the engine approximated or could not do.</param>
/// <param name="Outline">The layout nodes the definition produced, one per line, indented by depth: name, algorithm, the point type it was made for, and its rectangle in points. For diagnosing a layout that does not come out as expected.</param>
public sealed record DiagramLayoutResult(IReadOnlyList<DiagramShape> Shapes, byte[] Drawing, IReadOnlyList<string> Notes, string Outline = "");

/// <summary>
/// Paints SmartArt diagrams. The stored drawing (<c>dsp:drawing</c>, [MS-ODRAWXML]) is painted as ordinary shapes; when it is missing or
/// stale the layout definition (ECMA-376 Part 1 §21.4) is run against the data model and the shapes it makes are painted the same way.
/// </summary>
/// <remarks>
/// A host (Word, slide or worksheet painter) installs <see cref="CreatePainter"/> as <see cref="DrawingContext.DiagramPainter"/>; the
/// drawing painter then calls it for every <c>dgm:relIds</c> frame. Layout definitions, colours and styles are read from the parts the
/// file carries; nothing is built in.
/// </remarks>
public static class DiagramHost
{
    private const string DiagramDataType = "/diagramData";
    private const string DiagramDrawingType = "/diagramDrawing";
    private static readonly Lazy<FontLibrary> DefaultFonts = new(FontLibrary.CreateDefault);

    /// <summary>Lists the diagram frames of a package by reading every part that relates to a diagram data part.</summary>
    /// <param name="package">The package.</param>
    /// <returns>The frames in package order.</returns>
    public static IReadOnlyList<DiagramLocation> Find(IOpcPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var found = new List<DiagramLocation>();
        foreach (IPackagePart part in package.Parts)
        {
            if (!package.GetRelationships(part.Name).Any(r => r.Type.EndsWith(DiagramDataType, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? doc, null, part.Name) || doc is null)
            {
                continue;
            }

            Collect(doc.Root, part.Name, found, 0);
        }

        return found;
    }

    private static void Collect(MarkupNode n, string host, List<DiagramLocation> found, int depth)
    {
        if (n.IsNull || depth > 48 || found.Count > 10_000)
        {
            return;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == "relIds" && DiagramReferences.Read(c) is { } refs)
            {
                (double? w, double? h) = FrameSize(c);
                found.Add(new DiagramLocation(host, refs, w, h));
            }
            else if (c.LocalNameString != "Fallback")
            {
                Collect(c, host, found, depth + 1);
            }
        }
    }

    // The size the frame states, from the nearest ancestor that has a wp:extent or an xfrm/ext (EMU to points).
    private static (double? Width, double? Height) FrameSize(MarkupNode relIds)
    {
        int guard = 0;
        for (MarkupNode cur = relIds.Parent; !cur.IsNull && guard++ < 8; cur = cur.Parent)
        {
            foreach (MarkupNode c in cur.ChildElements)
            {
                MarkupNode ext = c.LocalNameString == "extent" ? c : c.LocalNameString == "xfrm" ? Dx.Child(c, "ext") : default;
                if (!ext.IsNull && ext.GetAttribute("cx") is { } cx && ext.GetAttribute("cy") is { } cy)
                {
                    return (Dx.Num(cx, 0) / 12700, Dx.Num(cy, 0) / 12700);
                }
            }
        }

        return (null, null);
    }

    /// <summary>Creates the function a host installs as <see cref="DrawingContext.DiagramPainter"/>.</summary>
    /// <param name="package">The package.</param>
    /// <param name="sourcePartName">The part whose relationships the frames' ids refer to.</param>
    /// <param name="options">Options, or null for the defaults.</param>
    /// <returns>The painter; it returns false when nothing could be painted.</returns>
    public static Func<DiagramPaintRequest, bool> CreatePainter(IOpcPackage package, string sourcePartName, DiagramOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(sourcePartName);
        return request => Paint(package, sourcePartName, request, options);
    }

    /// <summary>Paints one diagram frame.</summary>
    /// <param name="package">The package.</param>
    /// <param name="sourcePartName">The part that holds the frame.</param>
    /// <param name="request">The frame, its box and the list to paint into.</param>
    /// <param name="options">Options, or null for the defaults.</param>
    /// <returns>True when shapes were painted.</returns>
    public static bool Paint(IOpcPackage package, string sourcePartName, DiagramPaintRequest request, DiagramOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(request);
        options ??= new DiagramOptions();
        const double PxToPt = 0.75;
        double wPt = request.Width * PxToPt, hPt = request.Height * PxToPt;
        DrawingContext parent = request.Context;
        string? dataPart = ResolvePart(package, sourcePartName, request.References.Data);
        DiagramData? data = dataPart is null ? null : LoadData(package, dataPart);
        if (data is null)
        {
            parent.Note("smartart.data-unreadable", "A diagram's data part is missing or unreadable; the diagram is not painted.");
            options.Report?.Invoke(new DiagramReport(sourcePartName, dataPart, DiagramSource.None, 0, "data-unreadable"));
            return false;
        }

        string? cachePart = data.DrawingRelationshipId is { } rid ? ResolvePart(package, sourcePartName, rid) : null;
        MarkupDocument? cached = cachePart is null ? null : Load(package, cachePart);
        if (cached is not null && !IsDrawing(cached))
        {
            cached = null;
        }

        string? reason = null;
        if (options.Regeneration == DiagramRegeneration.Always)
        {
            reason = "forced";
        }
        else if (cached is null)
        {
            reason = "missing";
        }
        else if (options.Regeneration == DiagramRegeneration.WhenMissingOrStale && IsStale(data, cached))
        {
            reason = "stale";
        }

        MarkupDocument? drawing = cached;
        string? drawingPart = cachePart;
        DiagramSource source = DiagramSource.Cached;
        if (reason is not null)
        {
            if (options.Regeneration == DiagramRegeneration.Never)
            {
                parent.Note("smartart.not-painted", "A diagram has no usable stored drawing and layout is switched off; it is not painted.");
                options.Report?.Invoke(new DiagramReport(sourcePartName, dataPart, DiagramSource.None, 0, "regeneration-off"));
                return false;
            }

            DiagramLayoutResult? made = Regenerate(package, sourcePartName, request.References, data, wPt, hPt, parent);
            if (made is null)
            {
                // A stale drawing is still better than none when the layout cannot be read.
                if (cached is null || reason == "forced")
                {
                    parent.Note("smartart.layout-unreadable", "A diagram's layout definition is missing or unreadable; the diagram is not painted.");
                    options.Report?.Invoke(new DiagramReport(sourcePartName, dataPart, DiagramSource.None, 0, "layout-unreadable"));
                    return false;
                }

                parent.Note("smartart.layout-unreadable", "A diagram's layout definition is missing or unreadable; its stored drawing is painted although the data changed.");
            }
            else
            {
                drawing = MarkupDocument.Parse(made.Drawing);
                drawingPart = null;
                source = DiagramSource.Regenerated;
                foreach (string note in made.Notes)
                {
                    parent.Note(note, "The diagram engine approximated a feature (" + note + ").");
                }
            }
        }

        if (drawing is null)
        {
            return false;
        }

        MarkupNode tree = Dx.Child(drawing.Root, "spTree");
        var ctx = new DrawingContext(parent.Colors, parent.Images, drawingPart is null ? null : rel => ImageBytes(package, drawingPart, rel), drawingPart ?? sourcePartName + "#diagram")
        {
            Tagged = parent.Tagged,
            Fonts = parent.Fonts,
        };
        IFontProvider fonts = parent.Fonts ?? DefaultFonts.Value;
        ctx.TextPainter = req => PaintText(req, ctx, fonts);
        bool figure = parent.Tagged && !request.InFigure;
        if (figure)
        {
            request.List.BeginTag(request.NextId(), TagRole.Figure, request.AltText);
        }

        int count = DrawingPainter.DrawShapes(request.List, request.NextId, tree, request.X * PxToPt, request.Y * PxToPt, ctx);
        if (figure)
        {
            request.List.EndTag();
        }

        foreach (DrawingNote n in ctx.Notes)
        {
            parent.Note(n.Code, n.Message);
        }

        options.Report?.Invoke(new DiagramReport(sourcePartName, dataPart, source, count, reason));
        return true;
    }

    /// <summary>
    /// Runs a diagram's layout without painting. Useful to inspect what the engine makes and to write a drawing part.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="sourcePartName">The part that holds the frame.</param>
    /// <param name="references">The frame's relationship ids.</param>
    /// <param name="widthPoints">The frame's width in points.</param>
    /// <param name="heightPoints">The frame's height in points.</param>
    /// <param name="colors">The theme colours, or null for the default theme.</param>
    /// <param name="fonts">The font provider, or null for the default library.</param>
    /// <returns>The shapes and the drawing part, or null when the data or the layout definition cannot be read.</returns>
    public static DiagramLayoutResult? Generate(IOpcPackage package, string sourcePartName, DiagramReferences references, double widthPoints, double heightPoints, ColorResolver? colors = null, IFontProvider? fonts = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(references);
        string? dataPart = ResolvePart(package, sourcePartName, references.Data);
        DiagramData? data = dataPart is null ? null : LoadData(package, dataPart);
        if (data is null)
        {
            return null;
        }

        var ctx = new DrawingContext(colors) { Fonts = fonts };
        return Regenerate(package, sourcePartName, references, data, widthPoints, heightPoints, ctx);
    }

    /// <summary>Reads the shapes of a diagram's stored drawing without painting it.</summary>
    /// <param name="package">The package.</param>
    /// <param name="sourcePartName">The part that holds the frame.</param>
    /// <param name="references">The frame's relationship ids.</param>
    /// <returns>The stored shapes in drawing order, or null when the diagram has no readable stored drawing.</returns>
    public static IReadOnlyList<DiagramShape>? ReadStored(IOpcPackage package, string sourcePartName, DiagramReferences references)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(references);
        string? dataPart = ResolvePart(package, sourcePartName, references.Data);
        DiagramData? data = dataPart is null ? null : LoadData(package, dataPart);
        string? cachePart = data?.DrawingRelationshipId is { } rid ? ResolvePart(package, sourcePartName, rid) : null;
        if (cachePart is null || Load(package, cachePart) is not { } drawing || !IsDrawing(drawing))
        {
            return null;
        }

        var shapes = new List<DiagramShape>();
        foreach (MarkupNode sp in Dx.Child(drawing.Root, "spTree").ChildElements)
        {
            if (sp.LocalNameString != "sp")
            {
                continue;
            }

            MarkupNode spPr = Dx.Child(sp, "spPr");
            MarkupNode xfrm = Dx.Child(spPr, "xfrm");
            MarkupNode off = Dx.Child(xfrm, "off"), ext = Dx.Child(xfrm, "ext");
            var sb = new System.Text.StringBuilder();
            double size = 0;
            bool first = true;
            foreach (MarkupNode p in Dx.Children(Dx.Child(sp, "txBody"), "p"))
            {
                if (!first)
                {
                    sb.Append('\n');
                }

                first = false;
                DiagramPoint.AppendParagraph(sb, p);
                if (size == 0)
                {
                    foreach (MarkupNode r in Dx.Children(p, "r"))
                    {
                        size = Dx.Num(Dx.Child(r, "rPr").GetAttribute("sz"), 0) / 100;
                        break;
                    }
                }
            }

            shapes.Add(new DiagramShape(
                string.Empty,
                Dx.Child(spPr, "prstGeom").GetAttribute("prst") ?? (Dx.Child(spPr, "custGeom").IsNull ? string.Empty : "custGeom"),
                Dx.Num(off.IsNull ? null : off.GetAttribute("x"), 0) / 12700,
                Dx.Num(off.IsNull ? null : off.GetAttribute("y"), 0) / 12700,
                Dx.Num(ext.IsNull ? null : ext.GetAttribute("cx"), 0) / 12700,
                Dx.Num(ext.IsNull ? null : ext.GetAttribute("cy"), 0) / 12700,
                Dx.Num(xfrm.IsNull ? null : xfrm.GetAttribute("rot"), 0) / 60000,
                sb.ToString(),
                size,
                string.Empty,
                false));
        }

        return shapes;
    }

    private static DiagramLayoutResult? Regenerate(IOpcPackage package, string source, DiagramReferences refs, DiagramData data, double wPt, double hPt, DrawingContext ctx)
    {
        if (wPt < 1 || hPt < 1 || refs.Layout is null || ResolvePart(package, source, refs.Layout) is not { } loPart || Load(package, loPart) is not { } loDoc || LayoutDefinition.TryRead(loDoc) is not { } def)
        {
            return null;
        }

        DiagramColors? colors = refs.Colors is null || ResolvePart(package, source, refs.Colors) is not { } csPart || Load(package, csPart) is not { } csDoc ? null : DiagramColors.TryRead(csDoc);
        DiagramQuickStyle? style = refs.QuickStyle is null || ResolvePart(package, source, refs.QuickStyle) is not { } qsPart || Load(package, qsPart) is not { } qsDoc ? null : DiagramQuickStyle.TryRead(qsDoc);
        IFontProvider fonts = ctx.Fonts ?? DefaultFonts.Value;
        var engine = new DiagramLayoutEngine(data, def, fonts, ctx.Colors);
        engine.Run(wPt, hPt);
        List<OutShape> shapes = engine.Shapes(colors, style);
        byte[] drawing = DiagramDrawingWriter.Write(shapes);
        var list = shapes.Select(s => new DiagramShape(
            s.Instance.Name,
            s.Geometry,
            s.Instance.Route is null ? s.Instance.X : s.Instance.Route.Points.Min(p => p.X),
            s.Instance.Route is null ? s.Instance.Y : s.Instance.Route.Points.Min(p => p.Y),
            s.Instance.Route is null ? s.Instance.W : s.Instance.Route.Points.Max(p => p.X) - s.Instance.Route.Points.Min(p => p.X),
            s.Instance.Route is null ? s.Instance.H : s.Instance.Route.Points.Max(p => p.Y) - s.Instance.Route.Points.Min(p => p.Y),
            s.Instance.Rotation,
            s.PlainText,
            s.Instance.FontSize,
            s.Label,
            s.IsConnector)).ToList();
        return new DiagramLayoutResult(list, drawing, engine.Notes, engine.Outline());
    }

    // The text of every node must appear in the stored drawing, or the drawing no longer shows the data.
    private static bool IsStale(DiagramData data, MarkupDocument drawing)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkupNode sp in Dx.Child(drawing.Root, "spTree").ChildElements)
        {
            if (sp.LocalNameString != "sp")
            {
                continue;
            }

            var sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (MarkupNode p in Dx.Children(Dx.Child(sp, "txBody"), "p"))
            {
                if (!first)
                {
                    sb.Append('\n');
                }

                first = false;
                DiagramPoint.AppendParagraph(sb, p);
            }

            shown.Add(Normalize(sb.ToString()));
        }

        foreach (string text in data.NodeTexts())
        {
            if (!shown.Contains(Normalize(text)) && !shown.Any(s => s.Contains(Normalize(text), StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string s) => string.Join(' ', s.Split([' ', '\n', '\r', '\t', '\v'], StringSplitOptions.RemoveEmptyEntries));

    private static bool IsDrawing(MarkupDocument d) => d.Root is { IsNull: false } r && r.LocalNameString == "drawing";

    private static string? ResolvePart(IOpcPackage package, string source, string relationshipId)
    {
        foreach (Relationship r in package.GetRelationships(source))
        {
            if (r.Id == relationshipId && OpcPackage.TryResolveTarget(source, r, out PartUri target))
            {
                return target.Value;
            }
        }

        return null;
    }

    private static MarkupDocument? Load(IOpcPackage package, string partName) =>
        package.FindPart(partName) is { } part && MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? doc, null, partName) ? doc : null;

    private static DiagramData? LoadData(IOpcPackage package, string partName) => Load(package, partName) is { } d ? DiagramData.TryRead(d) : null;

    private static byte[]? ImageBytes(IOpcPackage package, string drawingPart, string relationshipId)
    {
        string? target = ResolvePart(package, drawingPart, relationshipId);
        return target is null ? null : package.FindPart(target)?.GetBytes().ToArray();
    }

    // Text of a diagram shape: DrawingML paragraphs laid out in the text rectangle (insets already removed) and anchored by bodyPr.
    private static void PaintText(ShapeTextRequest req, DrawingContext ctx, IFontProvider fonts)
    {
        MarkupNode tx = Dx.Child(req.Shape, "txBody");
        if (tx.IsNull)
        {
            return;
        }

        MarkupNode fontRef = Dx.Child(Dx.Child(req.Shape, "style"), "fontRef");
        DrawingColor? color = fontRef.IsNull ? null : ctx.Colors.ResolveChild(fontRef);
        string family = fontRef.GetAttribute("idx") == "major" ? ctx.Colors.Theme.MajorFont : ctx.Colors.Theme.MinorFont;
        List<ParaSpec> paras = DiagramText.Parse(tx, ctx.Colors, color ?? new DrawingColor(0, 0, 0), 18);
        if (paras.Count == 0)
        {
            return;
        }

        bool wrap = req.BodyProperties.IsNull || req.BodyProperties.GetAttribute("wrap") != "none";
        LaidText laid = DiagramText.Layout(paras, req.Width, fonts, family, wrap);
        string anchor = req.BodyProperties.IsNull ? "t" : req.BodyProperties.GetAttribute("anchor") ?? "t";
        double dy = anchor switch { "ctr" => (req.Height - laid.Height) / 2, "b" => req.Height - laid.Height, _ => 0 };
        req.List.PushTransform(req.NextId(), req.Transform);
        DiagramText.Draw(req.List, req.NextId, laid, 0, dy, ctx.Tagged);
        req.List.PopTransform();
        _ = Matrix3x2.Identity;
    }
}
