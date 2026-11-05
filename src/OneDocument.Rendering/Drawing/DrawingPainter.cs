using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.Drawing;

/// <summary>
/// Paints DrawingML (shapes, groups, pictures, connectors) into a display list. The same code serves Word drawings, slide shapes and
/// worksheet drawings: it reads the shape elements (<c>wps:wsp</c>, <c>p:sp</c>, <c>pic:pic</c>, <c>wpg:wgp</c>, <c>p:grpSp</c>,
/// <c>p:cxnSp</c>) and never asks which application they came from.
/// </summary>
/// <remarks>
/// Geometry comes from <see cref="GeometryDefinition"/>. Fills are solid, gradient (linear and path), pattern (drawn as an eight by eight
/// tile) and picture (stretched with crop, or tiled). Lines have widths, dashes, caps, joins, compound styles and arrowheads; they are
/// turned into filled outlines because the display list has no stroke item. Outer shadows and glows are drop-shadow filters; soft edges,
/// reflections, inner shadows and 3D are not drawn and are reported as notes on the <see cref="DrawingContext"/>.
/// </remarks>
public static class DrawingPainter
{
    private const double PointsToPixels = 96.0 / 72.0;
    private const double EmuPerPoint = 12700.0;
    private const int MaxDepth = 24;
    private const int MaxShapes = 20_000;

    /// <summary>Paints a drawing.</summary>
    /// <param name="list">The display list; coordinates are CSS pixels on the page.</param>
    /// <param name="nextId">Allocates item ids.</param>
    /// <param name="drawing">A <c>w:drawing</c>, <c>a:graphic</c>, or a shape element itself.</param>
    /// <param name="box">Where the object sits on the page, in points; its size replaces the extents in the shape's own transform.</param>
    /// <param name="context">Colours, pictures and notes.</param>
    /// <param name="altText">Alternative text for the figure tag; null for none.</param>
    /// <param name="decorative">True to mark the object as an artifact instead of a figure.</param>
    /// <returns>True when something was painted or a placeholder drawn; false when the element holds no drawable shape.</returns>
    public static bool Draw(DisplayList list, Func<ItemId> nextId, MarkupNode drawing, DrawingBox box, DrawingContext context, string? altText = null, bool decorative = false)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(nextId);
        ArgumentNullException.ThrowIfNull(context);
        MarkupNode shape = FindShape(drawing);
        if (shape.IsNull || !double.IsFinite(box.Width) || !double.IsFinite(box.Height) || box.Width < 0 || box.Height < 0 || (box.Width == 0 && box.Height == 0))
        {
            return false;
        }

        var scene = new Scene(list, nextId, context);
        bool tagged = context.Tagged;
        if (tagged)
        {
            list.BeginTag(nextId(), decorative ? TagRole.Artifact : TagRole.Figure, decorative ? null : altText);
        }

        Matrix3x2 root = Matrix3x2.CreateScale((float)(PointsToPixels / EmuPerPoint)) * Matrix3x2.CreateTranslation((float)(box.X * PointsToPixels), (float)(box.Y * PointsToPixels));
        scene.PaintNode(shape, root, box, 0);
        if (tagged)
        {
            list.EndTag();
        }

        return true;
    }

    /// <summary>
    /// Paints the shapes of a shape tree (<c>p:spTree</c>, a group, or any element whose children are shapes) each at the position its own
    /// transform gives, as a slide or a worksheet drawing layer places them.
    /// </summary>
    /// <param name="list">The display list; coordinates are CSS pixels.</param>
    /// <param name="nextId">Allocates item ids.</param>
    /// <param name="tree">The element holding the shapes.</param>
    /// <param name="originX">Where the tree's origin falls on the page, in points.</param>
    /// <param name="originY">As <paramref name="originX"/>, vertically.</param>
    /// <param name="context">Colours, pictures and notes.</param>
    /// <returns>The number of shapes and groups painted at the top level.</returns>
    public static int DrawShapes(DisplayList list, Func<ItemId> nextId, MarkupNode tree, double originX, double originY, DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(nextId);
        ArgumentNullException.ThrowIfNull(context);
        var scene = new Scene(list, nextId, context);
        Matrix3x2 root = Matrix3x2.CreateScale((float)(PointsToPixels / EmuPerPoint)) * Matrix3x2.CreateTranslation((float)(originX * PointsToPixels), (float)(originY * PointsToPixels));
        int count = 0;
        foreach (MarkupNode child in tree.ChildElements)
        {
            if (child.LocalNameString is "sp" or "pic" or "cxnSp" or "grpSp" or "graphicFrame" or "AlternateContent" or "wsp" or "wgp")
            {
                scene.PaintNode(child, root, null, 0);
                count++;
            }
        }

        return count;
    }

    internal static MarkupNode FindShape(MarkupNode n)
    {
        for (int depth = 0; depth < 8 && !n.IsNull; depth++)
        {
            switch (n.LocalNameString)
            {
                case "drawing" or "inline" or "anchor" or "pict":
                    {
                        MarkupNode next = default;
                        foreach (MarkupNode c in n.ChildElements)
                        {
                            if (c.LocalNameString is "graphic" or "inline" or "anchor" or "group" or "shape")
                            {
                                next = c;
                                break;
                            }
                        }

                        n = next;
                        continue;
                    }

                case "graphic":
                    n = DrawingTheme.Child(n, "graphicData");
                    continue;
                case "graphicData":
                    n = FirstElement(n);
                    continue;
                case "AlternateContent":
                    n = ChooseAlternative(n);
                    continue;
                default:
                    return n;
            }
        }

        return default;
    }

    private static MarkupNode FirstElement(MarkupNode n)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            return c;
        }

        return default;
    }

    // ECMA-376 Part 3 §10.2: a Choice is used when its Requires namespaces are understood; the drawing namespaces named here are.
    private static MarkupNode ChooseAlternative(MarkupNode alt)
    {
        foreach (MarkupNode c in alt.ChildElements)
        {
            if (c.LocalNameString == "Choice" && FirstElement(c) is { IsNull: false } inner && IsShape(inner))
            {
                return inner;
            }
        }

        foreach (MarkupNode c in alt.ChildElements)
        {
            if (c.LocalNameString == "Fallback" && FirstElement(c) is { IsNull: false } inner)
            {
                return inner;
            }
        }

        return default;
    }

    private static bool IsShape(MarkupNode n) => n.LocalNameString is "sp" or "wsp" or "pic" or "cxnSp" or "grpSp" or "wgp" or "graphicFrame" or "graphic" or "drawing";

    private sealed class Scene(DisplayList list, Func<ItemId> nextId, DrawingContext ctx)
    {
        private int _shapes;

        public void PaintNode(MarkupNode node, Matrix3x2 parent, DrawingBox? top, int depth)
        {
            if (depth > MaxDepth || ++_shapes > MaxShapes)
            {
                ctx.Note("drawing.limit", "A drawing is nested too deeply or has too many shapes; the rest was left out.");
                return;
            }

            switch (node.LocalNameString)
            {
                case "sp" or "wsp" or "cxnSp" or "pic":
                    PaintShape(node, parent, top);
                    break;
                case "grpSp" or "wgp":
                    PaintGroup(node, parent, top, depth);
                    break;
                case "AlternateContent":
                    {
                        MarkupNode chosen = ChooseAlternative(node);
                        if (!chosen.IsNull)
                        {
                            PaintNode(chosen, parent, top, depth + 1);
                        }

                        break;
                    }

                case "graphicFrame" or "chart":
                    PaintChartFrame(node, parent, top);
                    break;
                case "graphic" or "drawing":
                    {
                        MarkupNode inner = FindShape(node);
                        if (!inner.IsNull && inner != node)
                        {
                            PaintNode(inner, parent, top, depth + 1);
                        }

                        break;
                    }
            }
        }

        // A graphic frame that shows a chart (c:chart or cx:chart with an r:id): the chart is painted at the frame's box by the chart
        // painter the host installed. Anything else in a frame (tables, diagrams, OLE) is not painted here.
        private void PaintChartFrame(MarkupNode node, Matrix3x2 parent, DrawingBox? top)
        {
            string? rel = node.LocalNameString == "chart" ? RelationshipId(node) : ChartReference(node, 0);
            if (rel is null || ctx.ChartPainter is not { } painter)
            {
                ctx.Note("drawing.graphic-frame", rel is null
                    ? "A table, diagram or other graphic frame inside a drawing is not painted by the shape painter."
                    : "A chart inside a drawing is not painted because no chart painter was installed.");
                return;
            }

            // The frame's own transform is p:xfrm (a plain a:xfrm-like element with off and ext) or, in a Word drawing, the box given.
            MarkupNode xf = default;
            foreach (MarkupNode c in node.ChildElements)
            {
                if (c.LocalNameString == "xfrm")
                {
                    xf = c;
                }
            }

            Xfrm f = ReadXfrm(xf, top);
            Vector2 a = Vector2.Transform(new Vector2((float)f.X, (float)f.Y), parent);
            Vector2 b = Vector2.Transform(new Vector2((float)(f.X + f.W), (float)(f.Y + f.H)), parent);
            double w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
            if (w < 1 || h < 1 || !painter(rel, list, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), w, h))
            {
                ctx.Note("drawing.chart-not-painted", "A chart could not be read or painted; nothing is drawn for it.");
            }
        }

        private static string? RelationshipId(MarkupNode e)
        {
            foreach (MarkupAttributeRef a in e.Attributes)
            {
                if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual("id"u8))
                {
                    return a.Value;
                }
            }

            return null;
        }

        private static string? ChartReference(MarkupNode n, int depth)
        {
            if (depth > 6)
            {
                return null;
            }

            foreach (MarkupNode c in n.ChildElements)
            {
                if (c.LocalNameString == "chart" && RelationshipId(c) is { } id)
                {
                    return id;
                }

                if (c.LocalNameString is "Fallback")
                {
                    continue;
                }

                if (ChartReference(c, depth + 1) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        private readonly record struct Xfrm(double X, double Y, double W, double H, double Rot, bool FlipH, bool FlipV, double ChX, double ChY, double ChW, double ChH);

        // ECMA-376 Part 1 §20.1.7.6 (xfrm): off and ext in EMU; rot in 60000ths of a degree, clockwise; flips apply before rotation.
        private static Xfrm ReadXfrm(MarkupNode xfrm, DrawingBox? top)
        {
            double x = 0, y = 0, w = 0, h = 0, rot = 0;
            bool fh = false, fv = false;
            double cx = 0, cy = 0, cw = 0, ch = 0;
            bool hasExt = false, hasChExt = false;
            if (!xfrm.IsNull)
            {
                rot = DrawingInfo.Emu(xfrm.GetAttribute("rot"));
                fh = xfrm.GetAttribute("flipH") is "1" or "true";
                fv = xfrm.GetAttribute("flipV") is "1" or "true";
                foreach (MarkupNode c in xfrm.ChildElements)
                {
                    switch (c.LocalNameString)
                    {
                        case "off":
                            x = DrawingInfo.Emu(c.GetAttribute("x"));
                            y = DrawingInfo.Emu(c.GetAttribute("y"));
                            break;
                        case "ext":
                            w = DrawingInfo.Emu(c.GetAttribute("cx"));
                            h = DrawingInfo.Emu(c.GetAttribute("cy"));
                            hasExt = true;
                            break;
                        case "chOff":
                            cx = DrawingInfo.Emu(c.GetAttribute("x"));
                            cy = DrawingInfo.Emu(c.GetAttribute("y"));
                            break;
                        case "chExt":
                            cw = DrawingInfo.Emu(c.GetAttribute("cx"));
                            ch = DrawingInfo.Emu(c.GetAttribute("cy"));
                            hasChExt = true;
                            break;
                    }
                }
            }

            if (top is { } t)
            {
                // The object's place on the page is the layout's; the shape keeps its own rotation and flips.
                x = 0;
                y = 0;
                w = t.Width * EmuPerPoint;
                h = t.Height * EmuPerPoint;
                hasExt = true;
            }

            if (!hasChExt || cw <= 0 || ch <= 0)
            {
                cx = x;
                cy = y;
                cw = w;
                ch = h;
            }

            _ = hasExt;
            return new Xfrm(x, y, Math.Clamp(w, 0, 2.54e11), Math.Clamp(h, 0, 2.54e11), rot / 60000.0, fh, fv, cx, cy, cw, ch);
        }

        private static Matrix3x2 Placement(Xfrm f, double scaleToPoints)
        {
            // Local units are points for a shape (scaleToPoints = 12700) and EMU for a group's children (scale 1).
            double cx = f.W / 2, cy = f.H / 2;
            Matrix3x2 m = Matrix3x2.CreateScale((float)scaleToPoints);
            m *= Matrix3x2.CreateTranslation((float)-cx, (float)-cy);
            m *= Matrix3x2.CreateScale(f.FlipH ? -1f : 1f, f.FlipV ? -1f : 1f);
            if (f.Rot != 0)
            {
                m *= Matrix3x2.CreateRotation((float)(f.Rot * Math.PI / 180.0));
            }

            m *= Matrix3x2.CreateTranslation((float)(cx + f.X), (float)(cy + f.Y));
            return m;
        }

        private void PaintGroup(MarkupNode group, Matrix3x2 parent, DrawingBox? top, int depth)
        {
            MarkupNode grpPr = DrawingTheme.Child(group, "grpSpPr");
            Xfrm f = ReadXfrm(DrawingTheme.Child(grpPr, "xfrm"), top);
            // Children live in a space of their own (chOff, chExt) that is stretched onto the group's box (off, ext).
            double sx = f.ChW > 0 ? f.W / f.ChW : 1, sy = f.ChH > 0 ? f.H / f.ChH : 1;
            Matrix3x2 m = Matrix3x2.CreateTranslation((float)-f.ChX, (float)-f.ChY) * Matrix3x2.CreateScale((float)sx, (float)sy) * Matrix3x2.CreateTranslation((float)f.X, (float)f.Y);
            double cx = f.X + (f.W / 2), cy = f.Y + (f.H / 2);
            m *= Matrix3x2.CreateTranslation((float)-cx, (float)-cy);
            m *= Matrix3x2.CreateScale(f.FlipH ? -1f : 1f, f.FlipV ? -1f : 1f);
            if (f.Rot != 0)
            {
                m *= Matrix3x2.CreateRotation((float)(f.Rot * Math.PI / 180.0));
            }

            m *= Matrix3x2.CreateTranslation((float)cx, (float)cy);
            m *= parent;
            foreach (MarkupNode child in group.ChildElements)
            {
                PaintNode(child, m, null, depth + 1);
            }
        }

        private void PaintShape(MarkupNode shape, Matrix3x2 parent, DrawingBox? top)
        {
            string kind = shape.LocalNameString;
            MarkupNode spPr = DrawingTheme.Child(shape, "spPr");
            MarkupNode style = DrawingTheme.Child(shape, "style");
            Xfrm f = ReadXfrm(DrawingTheme.Child(spPr, "xfrm"), top);
            double wPt = f.W / EmuPerPoint, hPt = f.H / EmuPerPoint;
            Matrix3x2 m = Placement(f, EmuPerPoint) * parent;

            GeometryDefinition? geometry = null;
            var adjust = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (MarkupNode g in spPr.ChildElements)
            {
                if (g.LocalNameString == "prstGeom")
                {
                    geometry = GeometryDefinition.Preset(g.GetAttribute("prst") ?? "rect");
                    if (geometry is null)
                    {
                        ctx.Note("drawing.unknown-preset", "A shape names a preset geometry the standard does not define; a rectangle is drawn.");
                        geometry = GeometryDefinition.Preset("rect");
                    }

                    foreach (MarkupNode av in DrawingTheme.Child(g, "avLst").ChildElements)
                    {
                        string[] parts = (av.GetAttribute("fmla") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && parts[0] == "val" && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                        {
                            adjust[av.GetAttribute("name") ?? string.Empty] = v;
                        }
                    }
                }
                else if (g.LocalNameString == "custGeom")
                {
                    geometry = GeometryDefinition.FromCustomGeometry(g);
                }
            }

            if (geometry is null && kind == "pic")
            {
                geometry = GeometryDefinition.Preset("rect");
            }

            EvaluatedGeometry? eg = geometry?.Evaluate(wPt, hPt, adjust);
            FillSpec? fill = kind == "pic" ? PictureFill(shape, spPr) : ResolveFill(spPr, style);
            LineSpec line = ResolveLine(spPr, style);
            // Office keeps a line's width when a group is stretched, so the stretch the group put into this shape's matrix is divided out.
            double det = Math.Abs((double)m.M11 * m.M22 - ((double)m.M12 * m.M21));
            double widthFix = det > 1e-18 ? PointsToPixels / Math.Sqrt(det) : 1;
            if (!double.IsFinite(widthFix) || widthFix <= 0)
            {
                widthFix = 1;
            }

            line.Width *= widthFix;
            line.ArrowMin = 1.5 * widthFix;
            var effects = ResolveEffects(spPr, style);

            bool filter = effects.Shadow is not null || effects.Glow is not null;
            if (filter)
            {
                var ops = new FilterOp[1];
                // Effects are specified in points; the filter works in page pixels outside the shape's transform.
                if (effects.Shadow is { } s)
                {
                    ops[0] = new FilterOp(FilterOpKind.DropShadow, (float)(s.Dx * PointsToPixels), (float)(s.Dy * PointsToPixels), (float)(s.Sigma * PointsToPixels), s.Color);
                }
                else
                {
                    GlowSpec g = effects.Glow!.Value;
                    ops[0] = new FilterOp(FilterOpKind.DropShadow, 0f, 0f, (float)(g.Sigma * PointsToPixels), g.Color);
                }

                list.PushFilter(nextId(), ops);
            }

            list.PushTransform(nextId(), m);
            if (eg is not null)
            {
                foreach (GeometryPath gp in eg.Paths)
                {
                    if (fill is not null && gp.Fill != PathFillMode.None)
                    {
                        PaintFill(gp.Path, fill, gp.Fill, wPt, hPt);
                    }
                }

                if (line.Visible)
                {
                    foreach (GeometryPath gp in eg.Paths)
                    {
                        if (gp.Stroke)
                        {
                            PaintLine(gp.Path, line);
                        }
                    }
                }
            }

            list.PopTransform();
            if (filter)
            {
                list.PopFilter();
            }

            if (effects.Unsupported is { } note)
            {
                ctx.Note("drawing.effect-skipped", note);
            }

            if (DrawingTheme.Child(spPr, "scene3d").IsNull is false || DrawingTheme.Child(spPr, "sp3d").IsNull is false)
            {
                ctx.Note("drawing.3d-flattened", "3-D formatting is drawn as its flat two-dimensional shape.");
            }

            PaintShapeText(shape, eg, m);
        }

        private void PaintShapeText(MarkupNode shape, EvaluatedGeometry? eg, Matrix3x2 m)
        {
            MarkupNode body = DrawingTheme.Child(shape, "bodyPr");
            MarkupNode txBody = DrawingTheme.Child(shape, "txBody");
            MarkupNode txbx = DrawingTheme.Child(shape, "txbx");
            if (body.IsNull && !txBody.IsNull)
            {
                body = DrawingTheme.Child(txBody, "bodyPr");
            }

            if (txBody.IsNull && txbx.IsNull)
            {
                return;
            }

            if (ctx.TextPainter is not { } painter || eg is null)
            {
                ctx.Note("drawing.text-not-painted", "The text inside a shape is not painted here.");
                return;
            }

            // ECMA-376 Part 1 §21.1.2.1.1 (bodyPr): insets default to 0.1 inch left and right and 0.05 inch top and bottom.
            double l = Inset(body, "lIns", 91440), t = Inset(body, "tIns", 45720), r = Inset(body, "rIns", 91440), b = Inset(body, "bIns", 45720);
            double x0 = eg.TextLeft + l, y0 = eg.TextTop + t;
            double w = eg.TextRight - eg.TextLeft - l - r, h = eg.TextBottom - eg.TextTop - t - b;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            painter(new ShapeTextRequest(list, shape, Matrix3x2.CreateTranslation((float)x0, (float)y0) * m, w, h, nextId, body));
        }

        private static double Inset(MarkupNode body, string name, double fallback) =>
            body.IsNull || body.GetAttribute(name) is not { } s ? fallback / EmuPerPoint : DrawingInfo.Emu(s) / EmuPerPoint;

        // ---------------------------------------------------------------- fills

        private sealed record FillSpec(MarkupNode Element, DrawingColor? Placeholder);

        private FillSpec? ResolveFill(MarkupNode spPr, MarkupNode style)
        {
            foreach (MarkupNode c in spPr.ChildElements)
            {
                switch (c.LocalNameString)
                {
                    case "noFill":
                        return null;
                    case "solidFill" or "gradFill" or "blipFill" or "pattFill":
                        return new FillSpec(c, null);
                    case "grpFill":
                        ctx.Note("drawing.group-fill", "A shape that takes its group's fill is painted without a fill.");
                        return null;
                }
            }

            // ECMA-376 Part 1 §20.1.4.2.10 (fillRef): idx 1 to 999 pick the theme's fill styles, 1001 and up its background fills.
            MarkupNode fillRef = DrawingTheme.Child(style, "fillRef");
            if (fillRef.IsNull)
            {
                return null;
            }

            int idx = (int)DrawingInfo.Emu(fillRef.GetAttribute("idx"));
            DrawingColor? ph = ctx.Colors.ResolveChild(fillRef);
            IReadOnlyList<MarkupNode> list2 = idx >= 1001 ? ctx.Colors.Theme.BackgroundFillStyles : ctx.Colors.Theme.FillStyles;
            int pos = idx >= 1001 ? idx - 1001 : idx - 1;
            if (idx == 0 || pos < 0 || pos >= list2.Count)
            {
                return ctx.Colors.Theme.FillStyles.Count == 0 && idx > 0 && ph is not null ? new FillSpec(default, ph) : null;
            }

            return new FillSpec(list2[pos], ph);
        }

        private FillSpec? PictureFill(MarkupNode pic, MarkupNode spPr)
        {
            MarkupNode blip = DrawingTheme.Child(pic, "blipFill");
            FillSpec? own = ResolveFill(spPr, default);
            if (own is not null && DrawingTheme.Child(spPr, "blipFill").IsNull is false)
            {
                return own;
            }

            return blip.IsNull ? null : new FillSpec(blip, null);
        }

        private static PaintColor ToPaint(DrawingColor c) => new((float)c.R, (float)c.G, (float)c.B, (float)c.A);

        private static DrawingColor Modify(DrawingColor c, PathFillMode mode) => mode switch
        {
            // ECMA-376 leaves the amounts open; these match the usual look of the 3-D presets' lit and shaded faces.
            PathFillMode.Lighten => new DrawingColor(c.R + ((1 - c.R) * 0.4), c.G + ((1 - c.G) * 0.4), c.B + ((1 - c.B) * 0.4), c.A),
            PathFillMode.LightenLess => new DrawingColor(c.R + ((1 - c.R) * 0.2), c.G + ((1 - c.G) * 0.2), c.B + ((1 - c.B) * 0.2), c.A),
            PathFillMode.Darken => new DrawingColor(c.R * 0.6, c.G * 0.6, c.B * 0.6, c.A),
            PathFillMode.DarkenLess => new DrawingColor(c.R * 0.8, c.G * 0.8, c.B * 0.8, c.A),
            _ => c,
        };

        private void PaintFill(PathBuffer path, FillSpec fill, PathFillMode mode, double w, double h)
        {
            if (fill.Element.IsNull)
            {
                if (fill.Placeholder is { } solid)
                {
                    list.FillPath(nextId(), path, Paint.FromColor(ToPaint(Modify(solid, mode))));
                }

                return;
            }

            switch (fill.Element.LocalNameString)
            {
                case "solidFill":
                    if (ctx.Colors.ResolveChild(fill.Element, fill.Placeholder) is { } c)
                    {
                        list.FillPath(nextId(), path, Paint.FromColor(ToPaint(Modify(c, mode))));
                    }

                    break;
                case "gradFill":
                    list.FillPath(nextId(), path, Gradient(fill.Element, fill.Placeholder, w, h, mode));
                    break;
                case "pattFill":
                    PaintPattern(path, fill, mode, w, h);
                    break;
                case "blipFill":
                    PaintBlip(path, fill.Element, w, h);
                    break;
            }
        }

        // ECMA-376 Part 1 §20.1.8.33 (gradFill): stops at gs/@pos (thousandths of a percent), a linear direction (lin/@ang, clockwise from
        // the x axis) or a path gradient that grows from fillToRect. Colours blend in sRGB.
        private Paint Gradient(MarkupNode grad, DrawingColor? ph, double w, double h, PathFillMode mode)
        {
            var stops = new List<GradientStop>();
            foreach (MarkupNode gs in DrawingTheme.Child(grad, "gsLst").ChildElements)
            {
                if (ctx.Colors.ResolveChild(gs, ph) is { } col)
                {
                    double pos = Math.Clamp(DrawingInfo.Emu(gs.GetAttribute("pos")) / 100000.0, 0, 1);
                    stops.Add(new GradientStop((float)pos, ToPaint(Modify(col, mode))));
                }
            }

            if (stops.Count == 0)
            {
                return Paint.FromColor(PaintColor.Transparent);
            }

            stops = [.. stops.OrderBy(s => s.Offset)];
            if (stops.Count == 1)
            {
                return Paint.FromColor(stops[0].Color);
            }

            MarkupNode pathGrad = DrawingTheme.Child(grad, "path");
            if (!pathGrad.IsNull)
            {
                MarkupNode rect = DrawingTheme.Child(pathGrad, "fillToRect");
                double l = Pct(rect, "l"), t = Pct(rect, "t"), r = Pct(rect, "r"), b = Pct(rect, "b");
                double cx = w * (l + (1 - r)) / 2, cy = h * (t + (1 - b)) / 2;
                double dx = Math.Max(cx, w - cx), dy = Math.Max(cy, h - cy);
                double rx, ry;
                if (pathGrad.GetAttribute("path") == "circle")
                {
                    rx = ry = Math.Sqrt((dx * dx) + (dy * dy));
                }
                else
                {
                    rx = dx * Math.Sqrt(2);
                    ry = dy * Math.Sqrt(2);
                }

                return list.RadialGradient((float)cx, (float)cy, (float)Math.Max(rx, 0.01), (float)Math.Max(ry, 0.01), stops.ToArray());
            }

            MarkupNode lin = DrawingTheme.Child(grad, "lin");
            double ang = lin.IsNull ? 0 : DrawingInfo.Emu(lin.GetAttribute("ang")) / 60000.0 * Math.PI / 180.0;
            if (!lin.IsNull && lin.GetAttribute("scaled") is "1" or "true")
            {
                ang = Math.Atan2(h * Math.Sin(ang), w * Math.Cos(ang));
            }

            double ux = Math.Cos(ang), uy = Math.Sin(ang);
            double half = ((Math.Abs(w * ux) + Math.Abs(h * uy)) / 2);
            double mx = w / 2, my = h / 2;
            return list.LinearGradient((float)(mx - (ux * half)), (float)(my - (uy * half)), (float)(mx + (ux * half)), (float)(my + (uy * half)), stops.ToArray());
        }

        private static double Pct(MarkupNode e, string attr) => e.IsNull ? 0 : DrawingInfo.Emu(e.GetAttribute(attr)) / 100000.0;

        private void PaintPattern(PathBuffer path, FillSpec fill, PathFillMode mode, double w, double h)
        {
            string prst = fill.Element.GetAttribute("prst") ?? "pct50";
            DrawingColor fg = ctx.Colors.ResolveChild(DrawingTheme.Child(fill.Element, "fgClr"), fill.Placeholder) ?? new DrawingColor(0, 0, 0);
            DrawingColor bg = ctx.Colors.ResolveChild(DrawingTheme.Child(fill.Element, "bgClr"), fill.Placeholder) ?? new DrawingColor(1, 1, 1);
            fg = Modify(fg, mode);
            bg = Modify(bg, mode);
            string key = "pattern:" + prst + ":" + fg.Rgb.ToString("X6") + ":" + bg.Rgb.ToString("X6");
            byte[] png = PatternTiles.Png(prst, fg, bg, out bool approximated);
            if (approximated)
            {
                ctx.Note("drawing.pattern-approximated", "Some pattern fills are drawn with a similar pattern.");
            }

            if (!ctx.Images.TryGet(key, png, out ImageInfo info))
            {
                list.FillPath(nextId(), path, Paint.FromColor(ToPaint(bg)));
                return;
            }

            // One pattern cell is one pixel at 96 dpi, which is three quarters of a point.
            const float tile = 8 * 0.75f;
            list.PushClipPath(nextId(), path);
            list.DrawImageTiled(nextId(), info.Handle, RectF.Empty, new RectF(0, 0, tile, tile), new RectF(-1, -1, (float)w + 2, (float)h + 2), 0, 0, ImageSampling.Pixelated);
            list.PopClip();
        }

        // ECMA-376 Part 1 §20.1.8.14 (blipFill): srcRect crops the picture; stretch/fillRect fits what is left to the shape (or inset
        // rectangle); tile repeats it at its natural size scaled by sx and sy.
        private void PaintBlip(PathBuffer path, MarkupNode blipFill, double w, double h)
        {
            MarkupNode blip = DrawingTheme.Child(blipFill, "blip");
            string? embed = null;
            if (!blip.IsNull)
            {
                foreach (MarkupAttributeRef a in blip.Attributes)
                {
                    if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual("embed"u8))
                    {
                        embed = a.Value;
                    }
                }
            }

            RectF bounds = new(0, 0, (float)w, (float)h);
            ImageInfo info = default;
            bool ok = false;
            if (embed is not null && ctx.ImageBytes(embed) is { } bytes)
            {
                // EMF and WMF are drawn as vector content by the metafile painter, clipped to the shape's outline; when it cannot draw
                // the picture the bitmap path below runs, and ends in the placeholder.
                if (RecursiveAI.OneDocument.Rendering.Metafiles.PictureHook.IsVectorPicture(bytes))
                {
                    MarkupNode crop = DrawingTheme.Child(blipFill, "srcRect");
                    var mctx = new RecursiveAI.OneDocument.Rendering.Metafiles.MetafilePaintContext(list, ctx.Images.Pictures, nextId)
                    {
                        Fonts = ctx.Fonts,
                        Report = d => ctx.Note("drawing.metafile-" + d.Code, d.Message),
                    };
                    list.PushClipPath(nextId(), path);
                    bool drawn = RecursiveAI.OneDocument.Rendering.Metafiles.PictureHook.TryPaint(bytes, null, mctx, in bounds,
                        new RecursiveAI.OneDocument.Rendering.Metafiles.PictureCrop(Pct(crop, "l"), Pct(crop, "t"), Pct(crop, "r"), Pct(crop, "b")));
                    list.PopClip();
                    if (drawn)
                    {
                        return;
                    }
                }

                string key = ctx.Scope + "|" + embed;
                ok = ctx.Images.TryGet(key, bytes, out info);
                if (ok)
                {
                    ok = ApplyBlipEffects(blip, key, ref info);
                }
                else
                {
                    ctx.Note("drawing.picture-undecodable", "A picture could not be decoded (for example an EMF or WMF metafile); a placeholder is drawn.");
                }
            }
            else if (embed is null && !blip.IsNull)
            {
                ctx.Note("drawing.picture-linked", "A picture is linked, not embedded; linked pictures are never fetched, so a placeholder is drawn.");
            }
            else
            {
                ctx.Note("drawing.picture-missing", "A picture has no image part; a placeholder is drawn.");
            }

            if (!ok)
            {
                Placeholder(path, bounds);
                return;
            }

            MarkupNode src = DrawingTheme.Child(blipFill, "srcRect");
            double cl = Pct(src, "l"), ct = Pct(src, "t"), cr = Pct(src, "r"), cb = Pct(src, "b");
            double fw = Math.Max(1e-4, 1 - cl - cr), fh = Math.Max(1e-4, 1 - ct - cb);
            MarkupNode tile = DrawingTheme.Child(blipFill, "tile");
            list.PushClipPath(nextId(), path);
            if (!tile.IsNull)
            {
                double sx = Scale(tile, "sx"), sy = Scale(tile, "sy");
                double tw = info.Width * 0.75 * sx * (1 / fw), th = info.Height * 0.75 * sy * (1 / fh);
                double tx = DrawingInfo.Emu(tile.GetAttribute("tx")) / EmuPerPoint, ty = DrawingInfo.Emu(tile.GetAttribute("ty")) / EmuPerPoint;
                if (tw > 0.01 && th > 0.01)
                {
                    RectF srcPx = new((float)(cl * info.Width), (float)(ct * info.Height), (float)(fw * info.Width), (float)(fh * info.Height));
                    list.DrawImageTiled(nextId(), info.Handle, srcPx, new RectF((float)tx, (float)ty, (float)(tw * fw), (float)(th * fh)), bounds, 0, 0);
                }
            }
            else
            {
                MarkupNode fr = DrawingTheme.Child(DrawingTheme.Child(blipFill, "stretch"), "fillRect");
                double dl = Pct(fr, "l") * w, dt = Pct(fr, "t") * h, dr = Pct(fr, "r") * w, db = Pct(fr, "b") * h;
                double bx = dl, by = dt, bw = w - dl - dr, bh = h - dt - db;
                // The cropped part fills (bx, by, bw, bh); the whole picture is therefore larger or smaller than that by 1 / fw, 1 / fh.
                if (cl >= 0 && ct >= 0 && cr >= 0 && cb >= 0)
                {
                    var srcPx = new RectF((float)(cl * info.Width), (float)(ct * info.Height), (float)(fw * info.Width), (float)(fh * info.Height));
                    list.DrawImage(nextId(), info.Handle, srcPx, new RectF((float)bx, (float)by, (float)bw, (float)bh));
                }
                else
                {
                    // A negative crop pads the picture; draw it whole, larger or smaller, and let the clip trim it.
                    double ww = bw / fw, hh = bh / fh;
                    list.DrawImage(nextId(), info.Handle, RectF.Empty, new RectF((float)(bx - (cl * ww)), (float)(by - (ct * hh)), (float)ww, (float)hh));
                }
            }

            list.PopClip();
        }

        private static double Scale(MarkupNode e, string attr)
        {
            double v = DrawingInfo.Emu(e.GetAttribute(attr));
            return v <= 0 ? 1.0 : v / 100000.0;
        }

        private bool ApplyBlipEffects(MarkupNode blip, string key, ref ImageInfo info)
        {
            var parts = new List<MarkupNode>();
            foreach (MarkupNode e in blip.ChildElements)
            {
                if (ImageEffects.Handles(e.LocalNameString))
                {
                    parts.Add(e);
                }
                else if (e.LocalNameString != "extLst")
                {
                    ctx.Note("drawing.picture-effect", "A picture effect that is not supported (" + e.LocalNameString + ") was left out; the picture is drawn as stored.");
                }
            }

            if (parts.Count == 0)
            {
                return true;
            }

            string signature = string.Concat(parts.Select(p => p.OuterXml));
            DrawingColor? bgPlaceholder = null;
            _ = bgPlaceholder;
            if (!ctx.Images.TryGetVariant(key, signature, img => ImageEffects.Apply(img, parts, ctx.Colors), out ImageInfo variant))
            {
                return false;
            }

            info = variant;
            return true;
        }

        private void Placeholder(PathBuffer path, RectF bounds)
        {
            list.FillPath(nextId(), path, Paint.FromColor(new PaintColor(0.85f, 0.85f, 0.85f, 1f)));
            var cross = new PathBuffer();
            cross.MoveTo(bounds.X, bounds.Y);
            cross.LineTo(bounds.Right, bounds.Bottom);
            cross.MoveTo(bounds.Right, bounds.Y);
            cross.LineTo(bounds.X, bounds.Bottom);
            List<Polyline> lines = PathGeometry.Flatten(cross, Matrix3x2.Identity);
            list.FillPath(nextId(), PathGeometry.Stroke(lines, 0.75, LineCap.Flat, LineJoin.Miter, 4), Paint.FromColor(new PaintColor(0.55f, 0.55f, 0.55f, 1f)));
        }

        // ---------------------------------------------------------------- lines

        private sealed class LineSpec
        {
            public bool Visible;
            public double Width = 0.75;
            public double ArrowMin = 1.5;
            public string Cap = "flat";
            public string Compound = "sng";
            public string Join = "round";
            public double MiterLimit = 8;
            public string? Dash;
            public List<(double On, double Off)>? CustomDash;
            public DrawingColor Color = new(0, 0, 0);
            public (string Type, string W, string Len)? Head;
            public (string Type, string W, string Len)? Tail;
        }

        // ECMA-376 Part 1 §20.1.2.2.24 (ln): an explicit ln overrides the style's lnRef item by item.
        private LineSpec ResolveLine(MarkupNode spPr, MarkupNode style)
        {
            var spec = new LineSpec();
            MarkupNode lnRef = DrawingTheme.Child(style, "lnRef");
            DrawingColor? ph = null;
            if (!lnRef.IsNull)
            {
                int idx = (int)DrawingInfo.Emu(lnRef.GetAttribute("idx"));
                ph = ctx.Colors.ResolveChild(lnRef);
                if (idx >= 1 && idx <= ctx.Colors.Theme.LineStyles.Count)
                {
                    ApplyLine(spec, ctx.Colors.Theme.LineStyles[idx - 1], ph);
                }
                else if (idx >= 1 && ph is { } fallback)
                {
                    spec.Visible = true;
                    spec.Width = 0.75;
                    spec.Color = fallback;
                }
            }

            ApplyLine(spec, DrawingTheme.Child(spPr, "ln"), ph);
            return spec;
        }

        private void ApplyLine(LineSpec spec, MarkupNode ln, DrawingColor? ph)
        {
            if (ln.IsNull)
            {
                return;
            }

            if (ln.GetAttribute("w") is { } w)
            {
                spec.Width = DrawingInfo.Emu(w) / EmuPerPoint;
            }

            spec.Cap = ln.GetAttribute("cap") ?? spec.Cap;
            spec.Compound = ln.GetAttribute("cmpd") ?? spec.Compound;
            foreach (MarkupNode c in ln.ChildElements)
            {
                switch (c.LocalNameString)
                {
                    case "noFill":
                        spec.Visible = false;
                        break;
                    case "solidFill":
                        if (ctx.Colors.ResolveChild(c, ph) is { } col)
                        {
                            spec.Visible = true;
                            spec.Color = col;
                        }

                        break;
                    case "gradFill":
                        // A gradient line is drawn in the colour of its first stop.
                        MarkupNode first = DrawingTheme.Child(c, "gsLst").ChildElements.FirstOrDefault();
                        if (!first.IsNull && ctx.Colors.ResolveChild(first, ph) is { } gcol)
                        {
                            spec.Visible = true;
                            spec.Color = gcol;
                            ctx.Note("drawing.line-gradient", "A gradient line is drawn in its first colour.");
                        }

                        break;
                    case "pattFill":
                        if (ctx.Colors.ResolveChild(DrawingTheme.Child(c, "fgClr"), ph) is { } pcol)
                        {
                            spec.Visible = true;
                            spec.Color = pcol;
                        }

                        break;
                    case "prstDash":
                        spec.Dash = c.GetAttribute("val");
                        spec.CustomDash = null;
                        break;
                    case "custDash":
                        spec.CustomDash = [];
                        foreach (MarkupNode ds in c.ChildElements)
                        {
                            spec.CustomDash.Add((DrawingInfo.Emu(ds.GetAttribute("d")) / 100000.0, DrawingInfo.Emu(ds.GetAttribute("sp")) / 100000.0));
                        }

                        break;
                    case "round":
                        spec.Join = "round";
                        break;
                    case "bevel":
                        spec.Join = "bevel";
                        break;
                    case "miter":
                        spec.Join = "miter";
                        spec.MiterLimit = c.GetAttribute("lim") is { } lim ? Math.Max(1, DrawingInfo.Emu(lim) / 100000.0) : 8;
                        break;
                    case "headEnd":
                        spec.Head = End(c);
                        break;
                    case "tailEnd":
                        spec.Tail = End(c);
                        break;
                }
            }
        }

        private static (string, string, string)? End(MarkupNode e) =>
            e.GetAttribute("type") is { } t and not "none" ? (t, e.GetAttribute("w") ?? "med", e.GetAttribute("len") ?? "med") : null;

        // Dash presets are lengths of dash and gap in multiples of the line width. ECMA-376 Part 1 §20.1.10.48 (ST_PresetLineDashVal).
        private static double[]? DashPattern(string? preset) => preset switch
        {
            "dot" => [1, 3],
            "dash" => [4, 3],
            "lgDash" => [8, 3],
            "dashDot" => [4, 3, 1, 3],
            "lgDashDot" => [8, 3, 1, 3],
            "lgDashDotDot" => [8, 3, 1, 3, 1, 3],
            "sysDash" => [3, 1],
            "sysDot" => [1, 1],
            "sysDashDot" => [3, 1, 1, 1],
            "sysDashDotDot" => [3, 1, 1, 1, 1, 1],
            _ => null,
        };

        private void PaintLine(PathBuffer path, LineSpec line)
        {
            double width = Math.Max(line.Width, 0.25 * line.ArrowMin / 1.5);
            List<Polyline> lines = PathGeometry.Flatten(path, Matrix3x2.Identity);
            if (lines.Count == 0)
            {
                return;
            }

            LineCap cap = line.Cap switch { "rnd" => LineCap.Round, "sq" => LineCap.Square, _ => LineCap.Flat };
            LineJoin join = line.Join switch { "bevel" => LineJoin.Bevel, "miter" => LineJoin.Miter, _ => LineJoin.Round };
            double[]? dash = null;
            if (line.CustomDash is { Count: > 0 } custom)
            {
                dash = [.. custom.SelectMany(d => new[] { Math.Max(d.On, 0.01) * width, Math.Max(d.Off, 0.01) * width })];
            }
            else if (DashPattern(line.Dash) is { } preset)
            {
                dash = [.. preset.Select(v => v * width)];
            }

            (double Offset, double Fraction)[] parts = line.Compound switch
            {
                "dbl" => [(-1.0 / 3, 1.0 / 3), (1.0 / 3, 1.0 / 3)],
                "thickThin" => [(-0.25, 0.5), (0.35, 0.3)],
                "thinThick" => [(-0.35, 0.3), (0.25, 0.5)],
                "tri" => [(-0.4, 0.2), (0, 0.3), (0.4, 0.2)],
                _ => [(0, 1.0)],
            };

            PaintColor color = ToPaint(line.Color);
            foreach ((double offset, double fraction) in parts)
            {
                List<Polyline> src = offset == 0 ? lines : PathGeometry.Offset(lines, offset * width);
                List<Polyline> dashed = dash is null ? src : PathGeometry.Dash(src, dash);
                PathBuffer outline = PathGeometry.Stroke(dashed, width * fraction, cap, join, line.MiterLimit);
                if (outline.VerbCount > 0)
                {
                    list.FillPath(nextId(), outline, Paint.FromColor(color));
                }
            }

            // Arrowheads belong to open figures only.
            Polyline first = lines[0], last = lines[^1];
            if (line.Head is { } head && !first.Closed && first.Points.Count >= 2)
            {
                Arrow(first.Points[0], first.Points[1], head, width, color, line.ArrowMin);
            }

            if (line.Tail is { } tail && !last.Closed && last.Points.Count >= 2)
            {
                Arrow(last.Points[^1], last.Points[^2], tail, width, color, line.ArrowMin);
            }
        }

        // ECMA-376 Part 1 §20.1.8.38 (tailEnd): the head is drawn at the end of the line, pointing away from it; sm, med and lg are 2, 3 and
        // 5 times the line width (never less than about two pixels' worth, so thin lines still get a visible head).
        private void Arrow(Vector2 tip, Vector2 from, (string Type, string W, string Len) end, double width, PaintColor color, double minUnit)
        {
            Vector2 dir = tip - from;
            if (dir.LengthSquared() < 1e-12f)
            {
                return;
            }

            dir = Vector2.Normalize(dir);
            Vector2 nrm = new(-dir.Y, dir.X);
            double unit = Math.Max(width, minUnit);
            float aw = (float)(unit * Size(end.W)), al = (float)(unit * Size(end.Len));
            var pb = new PathBuffer();
            switch (end.Type)
            {
                case "triangle":
                    pb.MoveTo(tip.X, tip.Y);
                    Vector2 b1 = tip - (dir * al) + (nrm * aw / 2), b2 = tip - (dir * al) - (nrm * aw / 2);
                    pb.LineTo(b1.X, b1.Y);
                    pb.LineTo(b2.X, b2.Y);
                    pb.Close();
                    break;
                case "stealth":
                    Vector2 s1 = tip - (dir * al) + (nrm * aw / 2), s2 = tip - (dir * al) - (nrm * aw / 2), notch = tip - (dir * al * 0.65f);
                    pb.MoveTo(tip.X, tip.Y);
                    pb.LineTo(s1.X, s1.Y);
                    pb.LineTo(notch.X, notch.Y);
                    pb.LineTo(s2.X, s2.Y);
                    pb.Close();
                    break;
                case "diamond":
                    Vector2 c = tip;
                    pb.MoveTo(c.X + (dir.X * al / 2), c.Y + (dir.Y * al / 2));
                    pb.LineTo(c.X + (nrm.X * aw / 2), c.Y + (nrm.Y * aw / 2));
                    pb.LineTo(c.X - (dir.X * al / 2), c.Y - (dir.Y * al / 2));
                    pb.LineTo(c.X - (nrm.X * aw / 2), c.Y - (nrm.Y * aw / 2));
                    pb.Close();
                    break;
                case "oval":
                    const int n = 20;
                    for (int i = 0; i < n; i++)
                    {
                        double a = 2 * Math.PI * i / n;
                        float px = tip.X + (float)(Math.Cos(a) * al / 2 * dir.X) + (float)(Math.Sin(a) * aw / 2 * nrm.X);
                        float py = tip.Y + (float)(Math.Cos(a) * al / 2 * dir.Y) + (float)(Math.Sin(a) * aw / 2 * nrm.Y);
                        if (i == 0)
                        {
                            pb.MoveTo(px, py);
                        }
                        else
                        {
                            pb.LineTo(px, py);
                        }
                    }

                    pb.Close();
                    break;
                case "arrow":
                    {
                        Vector2 a1 = tip - (dir * al) + (nrm * aw / 2), a2 = tip - (dir * al) - (nrm * aw / 2);
                        var open = new List<Polyline> { new([a1, tip, a2], false) };
                        pb = PathGeometry.Stroke(open, Math.Max(width, 0.75), LineCap.Flat, LineJoin.Miter, 8);
                        break;
                    }

                default:
                    return;
            }

            list.FillPath(nextId(), pb, Paint.FromColor(color));
        }

        private static double Size(string s) => s switch { "sm" => 2, "lg" => 5, _ => 3 };

        // ---------------------------------------------------------------- effects

        private readonly record struct ShadowSpec(double Dx, double Dy, double Sigma, PaintColor Color);

        private readonly record struct GlowSpec(double Sigma, PaintColor Color);

        private readonly record struct EffectSet(ShadowSpec? Shadow, GlowSpec? Glow, string? Unsupported);

        // ECMA-376 Part 1 §20.1.8.45 (outerShdw): blurRad and dist in EMU, dir in 60000ths of a degree clockwise from the x axis. The
        // blur radius is about twice the Gaussian's standard deviation.
        private EffectSet ResolveEffects(MarkupNode spPr, MarkupNode style)
        {
            MarkupNode lst = DrawingTheme.Child(spPr, "effectLst");
            DrawingColor? ph = null;
            if (lst.IsNull)
            {
                MarkupNode effectRef = DrawingTheme.Child(style, "effectRef");
                int idx = effectRef.IsNull ? 0 : (int)DrawingInfo.Emu(effectRef.GetAttribute("idx"));
                if (idx >= 1 && idx <= ctx.Colors.Theme.EffectStyles.Count)
                {
                    ph = ctx.Colors.ResolveChild(effectRef);
                    lst = DrawingTheme.Child(ctx.Colors.Theme.EffectStyles[idx - 1], "effectLst");
                }
            }

            ShadowSpec? shadow = null;
            GlowSpec? glow = null;
            string? unsupported = null;
            foreach (MarkupNode e in lst.ChildElements)
            {
                switch (e.LocalNameString)
                {
                    case "outerShdw":
                        {
                            double blur = DrawingInfo.Emu(e.GetAttribute("blurRad")) / EmuPerPoint, dist = DrawingInfo.Emu(e.GetAttribute("dist")) / EmuPerPoint;
                            double dir = DrawingInfo.Emu(e.GetAttribute("dir")) / 60000.0 * Math.PI / 180.0;
                            DrawingColor c = ctx.Colors.ResolveChild(e, ph) ?? new DrawingColor(0, 0, 0, 0.4);
                            shadow = new ShadowSpec(dist * Math.Cos(dir), dist * Math.Sin(dir), blur / 2, ToPaint(c));
                            break;
                        }

                    case "glow":
                        {
                            double rad = DrawingInfo.Emu(e.GetAttribute("rad")) / EmuPerPoint;
                            DrawingColor c = ctx.Colors.ResolveChild(e, ph) ?? new DrawingColor(1, 1, 0, 0.5);
                            glow = new GlowSpec(Math.Max(rad / 2, 0.5), ToPaint(c));
                            break;
                        }

                    default:
                        unsupported = "The effect " + e.LocalNameString + " is not drawn (only outer shadow and glow are).";
                        break;
                }
            }

            return new EffectSet(shadow, shadow is null ? glow : null, unsupported);
        }
    }
}
