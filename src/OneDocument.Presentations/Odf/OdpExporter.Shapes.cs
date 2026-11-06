using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using System.Linq;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// Shapes: p:sp, p:pic, p:grpSp, p:cxnSp and p:graphicFrame become draw:frame, draw:rect, draw:ellipse, draw:custom-shape, draw:path, draw:line,
// draw:connector and draw:g with a graphic style each.
internal sealed partial class OdpExporter
{
    // Maps a point of a group's child space to the page: Ox + (x - ChOx) * Sx.
    private readonly record struct Transform(double Ox, double Oy, double Sx, double Sy, double ChOx, double ChOy)
    {
        public (long X, long Y) Point(long x, long y) => ((long)Math.Round(Ox + (x - ChOx) * Sx), (long)Math.Round(Oy + (y - ChOy) * Sy));
    }

    private readonly record struct Box(long X, long Y, long W, long H, long Rot, bool FlipH, bool FlipV);

    private static MarkupRef? XfrmOf(Shape shape) => shape.Kind switch
    {
        ShapeKind.GraphicFrame => Pml.P(shape.Markup, "xfrm"),
        ShapeKind.Group => Pml.P(shape.Markup, "grpSpPr") is { } g ? Pml.A(g, "xfrm") : null,
        ShapeKind.Shape or ShapeKind.Picture or ShapeKind.Connector => Pml.P(shape.Markup, "spPr") is { } sp ? Pml.A(sp, "xfrm") : null,
        _ => null,
    };

    private static Box? BoxOf(Shape shape, Transform t, bool inherit)
    {
        ShapeBounds? b = inherit ? shape.ResolveBounds() : shape.Bounds;
        if (b is null)
        {
            return null;
        }

        (long x, long y) = t.Point(b.Value.X, b.Value.Y);
        long w = (long)Math.Round(b.Value.Width * t.Sx), h = (long)Math.Round(b.Value.Height * t.Sy);
        MarkupRef? xfrm = XfrmOf(shape);
        bool flipH = xfrm is { } xf && Pml.AttrBool(xf, "flipH") == true, flipV = xfrm is { } xg && Pml.AttrBool(xg, "flipV") == true;
        return new Box(x, y, Math.Max(0, w), Math.Max(0, h), shape.Rotation, flipH, flipV);
    }

    // The position attributes of a shape. A rotated shape is written with draw:transform, the way OpenDocument rotates about the top-left corner.
    private static string Position(Box b, bool allowRotate = true)
    {
        string size = " svg:width=\"" + OdpUnits.Cm(b.W) + "\" svg:height=\"" + OdpUnits.Cm(b.H) + "\"";
        if (b.Rot == 0 || !allowRotate)
        {
            return size + " svg:x=\"" + OdpUnits.Cm(b.X) + "\" svg:y=\"" + OdpUnits.Cm(b.Y) + "\"";
        }

        // ECMA-376 Part 1 §20.1.7.6 (a:xfrm@rot) turns clockwise about the centre; draw:transform's angle is counter-clockwise about the origin.
        double angle = -b.Rot / 60000.0 * Math.PI / 180;
        double cx = b.X + b.W / 2.0, cy = b.Y + b.H / 2.0;
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double tx = cx - ((b.W / 2.0) * cos + (b.H / 2.0) * sin);
        double ty = cy - (-(b.W / 2.0) * sin + (b.H / 2.0) * cos);
        return size + " draw:transform=\"rotate (" + angle.ToString("0.############", CultureInfo.InvariantCulture) + ") translate (" + OdpUnits.Cm((long)Math.Round(tx)) + " " + OdpUnits.Cm((long)Math.Round(ty)) + ")\"";
    }

    private void WriteShape(StringBuilder sb, Shape shape, Transform t, Slide? slide = null, int index = 0)
    {
        _depth++;
        try
        {
            if (_depth > 24)
            {
                Lost("loss.odp-export-deep-groups", "deeply nested groups", "Groups nested deeper than 24 levels were dropped.");
                return;
            }

            if (shape.IsHidden)
            {
                Lost("loss.odp-export-hidden-shapes", "hidden shapes", "Shapes hidden on the slide are not written.");
                return;
            }

            switch (shape.Kind)
            {
                case ShapeKind.Shape:
                    WriteSp(sb, shape, t, slide);
                    break;
                case ShapeKind.Picture:
                    WritePicture(sb, shape, t);
                    break;
                case ShapeKind.Group:
                    WriteGroup(sb, shape, t, slide);
                    break;
                case ShapeKind.Connector:
                    WriteConnector(sb, shape, t);
                    break;
                case ShapeKind.GraphicFrame:
                    WriteGraphicFrame(sb, shape, t);
                    break;
                case ShapeKind.AlternateContent:
                    foreach (Shape child in shape.Children)
                    {
                        WriteShape(sb, child, t, slide, index);
                    }

                    break;
                default:
                    Lost("loss.odp-export-unknown-shapes", "shapes of an unknown kind", "Shapes the model does not interpret were not written.");
                    break;
            }
        }
        finally
        {
            _depth--;
        }
    }

    private static string NameAttr(Shape shape) => shape.Name.Length > 0 ? " draw:name=\"" + OdpUnits.Esc(shape.Name) + "\"" : "";

    private static string AltChildren(Shape shape) => shape.AltText is { Length: > 0 } alt ? "<svg:desc>" + OdpUnits.Esc(alt) + "</svg:desc>" : "";

    // ---- p:sp -----------------------------------------------------------------------------------------------------------------

    private void WriteSp(StringBuilder sb, Shape shape, Transform t, Slide? slide)
    {
        PlaceholderInfo? ph = shape.Placeholder;
        Box? box = BoxOf(shape, t, inherit: ph is not null);
        if (box is not { } b)
        {
            // Without a transform and without a placeholder to take one from the shape has no place on the page.
            Lost("loss.odp-export-unpositioned", "shapes with no position", "Shapes that have no position and no placeholder to take it from were not written.");
            return;
        }

        MarkupRef? spPr = Pml.P(shape.Markup, "spPr");
        string? cls = ph is { } p0 ? ClassOf(p0) : null;
        string graphic = GraphicStyle(shape, spPr, shape.Part, family: cls is null ? "graphic" : "presentation");
        string? preset = spPr is { } sp && Pml.A(sp, "prstGeom") is { } pg ? Pml.Attr(pg, "prst") : null;
        bool custom = spPr is { } sp2 && Pml.A(sp2, "custGeom") is not null;
        bool isTextBox = shape.Markup.Node.GetAttribute("txBox") is not null || Pml.P(shape.Markup, "nvSpPr") is { } nv && Pml.P(nv, "cNvSpPr") is { } cnv && Pml.AttrBool(cnv, "txBox") == true;
        bool frame = cls is "title" or "subtitle" or "outline" || isTextBox && !custom && (preset is null or "rect") || ph is not null;
        string textStyle = "";
        if (frame)
        {
            sb.Append("<draw:frame").Append(NameAttr(shape)).Append(cls is null ? " draw:style-name=\"" : " presentation:style-name=\"").Append(graphic).Append("\" draw:layer=\"layout\"").Append(Position(b));
            if (cls is not null)
            {
                sb.Append(" presentation:class=\"").Append(cls).Append('"');
            }

            sb.Append("><draw:text-box>");
            if (shape.TextBody is { } body)
            {
                WriteTextParagraphs(sb, body, shape);
            }

            sb.Append("</draw:text-box>").Append(AltChildren(shape)).Append("</draw:frame>");
            return;
        }

        _ = textStyle;
        string name = NameAttr(shape);
        string common = name + " draw:style-name=\"" + graphic + "\" draw:layer=\"layout\"";
        var text = new StringBuilder();
        if (shape.TextBody is { } tb)
        {
            WriteTextParagraphs(text, tb, shape);
        }

        if (custom)
        {
            WriteCustomPath(sb, shape, spPr!.Value, b, common, text.ToString());
            return;
        }

        switch (preset)
        {
            case "rect" or null:
                sb.Append("<draw:rect").Append(common).Append(Position(b)).Append('>').Append(AltChildren(shape)).Append(text).Append("</draw:rect>");
                if (b.FlipH || b.FlipV)
                {
                    Lost("loss.odp-export-flips", "flipped shapes", "Flipped rectangles and ellipses are written unflipped.");
                }

                break;
            case "roundRect":
                {
                    long adj = AdjustValue(spPr, "adj1", 16667);
                    long radius = (long)Math.Round(Math.Min(b.W, b.H) * adj / 100000.0);
                    sb.Append("<draw:rect").Append(common).Append(" svg:rx=\"").Append(OdpUnits.Cm(radius)).Append("\" svg:ry=\"").Append(OdpUnits.Cm(radius)).Append('"').Append(Position(b)).Append('>')
                        .Append(AltChildren(shape)).Append(text).Append("</draw:rect>");
                    break;
                }

            case "ellipse":
                sb.Append("<draw:ellipse").Append(common).Append(Position(b)).Append('>').Append(AltChildren(shape)).Append(text).Append("</draw:ellipse>");
                break;
            default:
                WritePresetShape(sb, shape, spPr, preset, b, common, text.ToString());
                break;
        }
    }

    private static long AdjustValue(MarkupRef? spPr, string name, long fallback)
    {
        if (spPr is { } sp && Pml.A(sp, "prstGeom") is { } pg && Pml.A(pg, "avLst") is { } av)
        {
            foreach (MarkupRef gd in Pml.Elements(av, SchemaNamespace.Drawing, "gd"))
            {
                if (Pml.Attr(gd, "name") == name && Pml.Attr(gd, "fmla") is { } f && f.StartsWith("val ", StringComparison.Ordinal)
                    && long.TryParse(f.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                {
                    return v;
                }
            }
        }

        return fallback;
    }

    // Preset shapes with no OpenDocument counterpart of their own are written as ooxml-* enhanced geometries, with the adjust values as modifiers.
    private static void WritePresetShape(StringBuilder sb, Shape shape, MarkupRef? spPr, string preset, Box b, string common, string text)
    {
        string modifiers = "";
        if (spPr is { } sp && Pml.A(sp, "prstGeom") is { } pg && Pml.A(pg, "avLst") is { } av)
        {
            var values = new List<string>();
            foreach (MarkupRef gd in Pml.Elements(av, SchemaNamespace.Drawing, "gd"))
            {
                if (Pml.Attr(gd, "fmla") is { } f && f.StartsWith("val ", StringComparison.Ordinal))
                {
                    values.Add(f[4..]);
                }
            }

            modifiers = values.Count > 0 ? " draw:modifiers=\"" + OdpUnits.Esc(string.Join(' ', values)) + "\"" : "";
        }

        sb.Append("<draw:custom-shape").Append(common).Append(Position(b)).Append('>').Append(AltChildren(shape)).Append(text)
            .Append("<draw:enhanced-geometry svg:viewBox=\"0 0 21600 21600\" draw:mirror-horizontal=\"").Append(b.FlipH ? "true" : "false").Append("\" draw:mirror-vertical=\"").Append(b.FlipV ? "true" : "false")
            .Append("\" draw:type=\"ooxml-").Append(OdpUnits.Esc(preset)).Append('"').Append(modifiers).Append(" draw:enhanced-path=\"M 0 0 L 21600 0 21600 21600 0 21600 Z N\"/></draw:custom-shape>");
    }

    private void WriteCustomPath(StringBuilder sb, Shape shape, MarkupRef spPr, Box b, string common, string text)
    {
        MarkupRef? cg = Pml.A(spPr, "custGeom");
        MarkupRef? pathLst = cg is { } c ? Pml.A(c, "pathLst") : null;
        var d = new StringBuilder();
        long vw = 0, vh = 0;
        bool arcs = false;
        if (pathLst is { } pl)
        {
            foreach (MarkupRef path in Pml.Elements(pl, SchemaNamespace.Drawing, "path"))
            {
                long w = Pml.AttrLong(path, "w") ?? 0, h = Pml.AttrLong(path, "h") ?? 0;
                if (vw == 0)
                {
                    vw = w > 0 ? w : b.W;
                    vh = h > 0 ? h : b.H;
                }

                double sx = w > 0 ? (double)vw / w : 1, sy = h > 0 ? (double)vh / h : 1;
                foreach (MarkupRef cmd in Pml.Elements(path))
                {
                    string op = cmd.LocalNameText;
                    var pts = Pml.Elements(cmd, SchemaNamespace.Drawing, "pt");
                    string P(MarkupRef pt) => OdpUnits.I((long)Math.Round((Pml.AttrLong(pt, "x") ?? 0) * sx)) + " " + OdpUnits.I((long)Math.Round((Pml.AttrLong(pt, "y") ?? 0) * sy));
                    switch (op)
                    {
                        case "moveTo" when pts.Count == 1:
                            d.Append("M ").Append(P(pts[0])).Append(' ');
                            break;
                        case "lnTo" when pts.Count == 1:
                            d.Append("L ").Append(P(pts[0])).Append(' ');
                            break;
                        case "cubicBezTo" when pts.Count == 3:
                            d.Append("C ").Append(P(pts[0])).Append(' ').Append(P(pts[1])).Append(' ').Append(P(pts[2])).Append(' ');
                            break;
                        case "quadBezTo" when pts.Count == 2:
                            d.Append("Q ").Append(P(pts[0])).Append(' ').Append(P(pts[1])).Append(' ');
                            break;
                        case "close":
                            d.Append("Z ");
                            break;
                        case "arcTo":
                            arcs = true;
                            break;
                    }
                }
            }
        }

        if (d.Length == 0)
        {
            Lost("loss.odp-export-empty-geometry", "custom geometries with no path", "Custom geometries that hold no drawable path were not written.");
            return;
        }

        if (arcs)
        {
            Lost("loss.odp-export-path-arcs", "arcs in custom geometries", "Arc segments of custom geometries were left out of the path.");
        }

        sb.Append("<draw:path").Append(common).Append(Position(b)).Append(" svg:viewBox=\"0 0 ").Append(OdpUnits.I(vw)).Append(' ').Append(OdpUnits.I(vh)).Append("\" svg:d=\"").Append(d.ToString().TrimEnd()).Append("\">")
            .Append(AltChildren(shape)).Append(text).Append("</draw:path>");
    }

    // ---- pictures -------------------------------------------------------------------------------------------------------------

    private string? MediaFor(PresentationPart owner, string? partName)
    {
        _ = owner;
        if (partName is null)
        {
            return null;
        }

        if (_mediaByPart.TryGetValue(partName, out string? known))
        {
            return known;
        }

        IPackagePart? p = _p.Package.FindPart(partName);
        if (p is null)
        {
            return null;
        }

        string ext = Path.GetExtension(partName).TrimStart('.').ToLowerInvariant();
        string name = "Pictures/image" + OdpUnits.I(_media.Count + 1) + (ext.Length > 0 ? "." + ext : "");
        _media.Add((name, p.GetBytes().ToArray(), p.ContentType));
        _mediaByPart[partName] = name;
        return name;
    }

    private void WritePicture(StringBuilder sb, Shape shape, Transform t)
    {
        Box? box = BoxOf(shape, t, inherit: false);
        string? media = MediaFor(shape.Part, shape.ImagePartName);
        if (box is not { } b || media is null)
        {
            Lost("loss.odp-export-pictures", "pictures that could not be written", "Pictures with no position or no image data in the package were not written.");
            return;
        }

        string graphic = GraphicStyle(shape, Pml.P(shape.Markup, "spPr"), shape.Part, picture: true);
        sb.Append("<draw:frame").Append(NameAttr(shape)).Append(" draw:style-name=\"").Append(graphic).Append("\" draw:layer=\"layout\"").Append(Position(b)).Append("><draw:image xlink:href=\"").Append(media)
            .Append("\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>").Append(AltChildren(shape)).Append("</draw:frame>");
        if (Pml.P(shape.Markup, "blipFill") is { } bf && Pml.A(bf, "srcRect") is not null)
        {
            Lost("loss.odp-export-crop", "cropped pictures", "The crop of a picture is not written; the picture is written whole.");
        }
    }

    // ---- groups, connectors, graphic frames -----------------------------------------------------------------------------------

    private void WriteGroup(StringBuilder sb, Shape shape, Transform t, Slide? slide)
    {
        MarkupRef? xfrm = XfrmOf(shape);
        if (xfrm is not { } xf)
        {
            foreach (Shape child in shape.Children)
            {
                WriteShape(sb, child, t, slide);
            }

            return;
        }

        MarkupRef? off = Pml.A(xf, "off"), ext = Pml.A(xf, "ext"), chOff = Pml.A(xf, "chOff"), chExt = Pml.A(xf, "chExt");
        double gx = off is { } o ? Pml.AttrLong(o, "x") ?? 0 : 0, gy = off is { } o2 ? Pml.AttrLong(o2, "y") ?? 0 : 0;
        double gw = ext is { } e ? Pml.AttrLong(e, "cx") ?? 0 : 0, gh = ext is { } e2 ? Pml.AttrLong(e2, "cy") ?? 0 : 0;
        double cx = chOff is { } c ? Pml.AttrLong(c, "x") ?? gx : gx, cy = chOff is { } c2 ? Pml.AttrLong(c2, "y") ?? gy : gy;
        double cw = chExt is { } ce ? Pml.AttrLong(ce, "cx") ?? gw : gw, ch = chExt is { } ce2 ? Pml.AttrLong(ce2, "cy") ?? gh : gh;
        double sx = cw > 0 ? gw / cw : 1, sy = ch > 0 ? gh / ch : 1;
        if (shape.Rotation != 0)
        {
            Lost("loss.odp-export-group-rotation", "rotated groups", "Group rotation is not written; the shapes of a rotated group keep their own rotation.");
        }

        var inner = new Transform(t.Ox + (gx - t.ChOx) * t.Sx, t.Oy + (gy - t.ChOy) * t.Sy, sx * t.Sx, sy * t.Sy, cx, cy);
        sb.Append("<draw:g").Append(NameAttr(shape)).Append('>');
        foreach (Shape s in shape.Children)
        {
            WriteShape(sb, s, inner, slide);
        }

        sb.Append("</draw:g>");
    }

    private void WriteConnector(StringBuilder sb, Shape shape, Transform t)
    {
        Box? box = BoxOf(shape, t, inherit: false);
        if (box is not { } b)
        {
            return;
        }

        string prst = Pml.P(shape.Markup, "spPr") is { } sp && Pml.A(sp, "prstGeom") is { } pg ? Pml.Attr(pg, "prst") ?? "line" : "line";
        long x1 = b.FlipH ? b.X + b.W : b.X, x2 = b.FlipH ? b.X : b.X + b.W;
        long y1 = b.FlipV ? b.Y + b.H : b.Y, y2 = b.FlipV ? b.Y : b.Y + b.H;
        string graphic = GraphicStyle(shape, Pml.P(shape.Markup, "spPr"), shape.Part, line: true);
        string coords = " svg:x1=\"" + OdpUnits.Cm(x1) + "\" svg:y1=\"" + OdpUnits.Cm(y1) + "\" svg:x2=\"" + OdpUnits.Cm(x2) + "\" svg:y2=\"" + OdpUnits.Cm(y2) + "\"";
        string common = NameAttr(shape) + " draw:style-name=\"" + graphic + "\" draw:layer=\"layout\"";
        if (prst is "line" or "straightConnector1")
        {
            sb.Append("<draw:line").Append(common).Append(coords).Append('>').Append(AltChildren(shape)).Append("</draw:line>");
        }
        else
        {
            string type = prst.StartsWith("curved", StringComparison.Ordinal) ? "curve" : "standard";
            sb.Append("<draw:connector").Append(common).Append(" draw:type=\"").Append(type).Append('"').Append(coords).Append('>').Append(AltChildren(shape)).Append("</draw:connector>");
        }
    }

    private void WriteGraphicFrame(StringBuilder sb, Shape shape, Transform t)
    {
        Box? box = BoxOf(shape, t, inherit: false);
        if (box is not { } b)
        {
            return;
        }

        switch (shape.FrameKind)
        {
            case GraphicFrameKind.Table when shape.Table is { } table:
                WriteTable(sb, shape, table, b);
                break;
            case GraphicFrameKind.Chart:
                Lost("loss.odp-export-charts", "charts", "Charts are not written to OpenDocument.");
                break;
            case GraphicFrameKind.Diagram:
                Lost("loss.odp-export-diagrams", "diagrams", "Diagrams (SmartArt) are not written to OpenDocument.");
                break;
            case GraphicFrameKind.OleObject:
                Lost("loss.odp-export-ole", "embedded objects", "Embedded objects are not written to OpenDocument.");
                break;
            default:
                Lost("loss.odp-export-graphic-frames", "graphic frames of an unknown kind", "Graphic frames the model does not interpret were not written.");
                break;
        }
    }

    // ---- graphic styles -------------------------------------------------------------------------------------------------------

    private static string? PresetHex(string name) => name.ToLowerInvariant() switch
    {
        "black" => "000000",
        "white" => "FFFFFF",
        "red" => "FF0000",
        "green" => "008000",
        "blue" => "0000FF",
        "yellow" => "FFFF00",
        "gray" or "grey" => "808080",
        "orange" => "FFA500",
        "purple" => "800080",
        "cyan" or "aqua" => "00FFFF",
        "magenta" or "fuchsia" => "FF00FF",
        "lime" => "00FF00",
        "navy" => "000080",
        "teal" => "008080",
        "maroon" => "800000",
        "olive" => "808000",
        "silver" => "C0C0C0",
        _ => null,
    };

    private static string? HexOf(PresentationPart part, MarkupRef holder, out double? opacity)
    {
        opacity = null;
        ColorValue? c = FormatReader.ReadColor(holder);
        if (c is null)
        {
            return null;
        }

        foreach (MarkupRef e in Pml.Elements(holder))
        {
            if (e.Namespace == SchemaNamespace.Drawing && e.LocalNameText is "srgbClr" or "schemeClr" or "sysClr" or "prstClr")
            {
                if (Pml.A(e, "alpha") is { } a && Pml.AttrInt(a, "val") is { } v && v < 100000)
                {
                    opacity = v / 100000.0;
                }

                break;
            }
        }

        return c.Value.Kind == ColorKind.Preset ? PresetHex(c.Value.Value) : part.ResolveColor(c);
    }

    // The fill attributes of a property holder (spPr or bgPr) as graphic-properties attributes; empty when it says nothing.
    private string FillProperties(MarkupRef holder, PresentationPart part, out string? styleFill)
    {
        styleFill = null;
        foreach (MarkupRef c in Pml.Elements(holder))
        {
            if (c.Namespace != SchemaNamespace.Drawing)
            {
                continue;
            }

            switch (c.LocalNameText)
            {
                case "noFill":
                    return " draw:fill=\"none\"";
                case "solidFill":
                    {
                        string? hex = HexOf(part, c, out double? opacity);
                        if (hex is null)
                        {
                            return "";
                        }

                        return " draw:fill=\"solid\" draw:fill-color=\"#" + hex.ToLowerInvariant() + "\"" + (opacity is { } o ? " draw:opacity=\"" + ((int)Math.Round(o * 100)).ToString(CultureInfo.InvariantCulture) + "%\"" : "");
                    }

                case "gradFill":
                    return GradientProperties(c, part);
                case "blipFill":
                    {
                        MarkupRef? blip = Pml.A(c, "blip");
                        string? rid = blip is { } b ? Pml.RelAttr(b, "embed") : null;
                        string? target = rid is null ? null : part.Owner.ResolveTarget(part.PartName, part.Relationships.FirstOrDefault(r => r.Id == rid)!);
                        string? media = MediaFor(part, target);
                        if (media is null)
                        {
                            return "";
                        }

                        string name = "Image" + OdpUnits.I(++_resourceCount);
                        _resources.Append("<draw:fill-image draw:name=\"").Append(name).Append("\" xlink:href=\"").Append(media).Append("\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>");
                        return " draw:fill=\"bitmap\" draw:fill-image-name=\"" + name + "\"";
                    }

                case "pattFill":
                    {
                        MarkupRef? fg = Pml.A(c, "fgClr");
                        string? hex = fg is { } f ? HexOf(part, f, out _) : null;
                        Lost("loss.odp-export-pattern-fills", "pattern fills", "Pattern fills are written as solid fills in their foreground colour.");
                        return hex is null ? "" : " draw:fill=\"solid\" draw:fill-color=\"#" + hex.ToLowerInvariant() + "\"";
                    }
            }
        }

        return "";
    }

    private string GradientProperties(MarkupRef grad, PresentationPart part)
    {
        MarkupRef? list = Pml.A(grad, "gsLst");
        var stops = list is { } l ? Pml.Elements(l, SchemaNamespace.Drawing, "gs") : [];
        if (stops.Count < 2)
        {
            return "";
        }

        string? start = HexOf(part, stops[0], out _), end = HexOf(part, stops[^1], out _);
        if (start is null || end is null)
        {
            return "";
        }

        if (stops.Count > 2)
        {
            Lost("loss.odp-export-gradient-stops", "gradients with more than two stops", "Gradients are written with their first and last colour.");
        }

        MarkupRef? lin = Pml.A(grad, "lin");
        long ang = lin is { } li ? Pml.AttrLong(li, "ang") ?? 0 : 0;
        // DrawingML angles run clockwise from the x axis in 60,000ths of a degree; OpenDocument's counter-clockwise in tenths, from "top to bottom".
        int odfAngle = (int)(((90 - ang / 60000.0) % 360 + 360) % 360 * 10);
        bool radial = Pml.A(grad, "path") is not null;
        string name = "Gradient" + OdpUnits.I(++_resourceCount);
        _resources.Append("<draw:gradient draw:name=\"").Append(name).Append("\" draw:style=\"").Append(radial ? "radial" : "linear").Append("\" draw:start-color=\"#").Append(start.ToLowerInvariant())
            .Append("\" draw:end-color=\"#").Append(end.ToLowerInvariant()).Append("\" draw:angle=\"").Append(odfAngle / 10).Append("deg\" draw:border=\"0%\"/>");
        return " draw:fill=\"gradient\" draw:fill-gradient-name=\"" + name + "\"";
    }

    // ECMA-376 Part 1 §20.1.2.2.24 (a:ln) to svg:stroke-* and draw:stroke.
    private string LineProperties(MarkupRef? spPr, Shape? shape, PresentationPart part, bool defaultVisible)
    {
        MarkupRef? ln = spPr is { } sp ? Pml.A(sp, "ln") : null;
        long width = ln is { } l ? Pml.AttrLong(l, "w") ?? 0 : 0;
        string? hex = null;
        bool none = false, dashed = false;
        double? opacity = null;
        string? head = null, tail = null;
        if (ln is { } line)
        {
            foreach (MarkupRef c in Pml.Elements(line))
            {
                switch (c.LocalNameText)
                {
                    case "noFill":
                        none = true;
                        break;
                    case "solidFill":
                        hex = HexOf(part, c, out opacity);
                        break;
                    case "prstDash":
                        dashed = Pml.Attr(c, "val") is not (null or "solid");
                        break;
                    case "headEnd":
                        head = Pml.Attr(c, "type");
                        break;
                    case "tailEnd":
                        tail = Pml.Attr(c, "type");
                        break;
                }
            }
        }

        if (none)
        {
            return " draw:stroke=\"none\"";
        }

        if (hex is null)
        {
            // A shape style (p:style/a:lnRef) gives the line when the shape does not.
            if (shape is not null && Pml.P(shape.Markup, "style") is { } style && Pml.A(style, "lnRef") is { } lr && Pml.AttrInt(lr, "idx") is > 0)
            {
                hex = HexOf(part, lr, out _);
                if (width == 0)
                {
                    width = Pml.AttrInt(lr, "idx") switch { 1 => 6350, 2 => 12700, _ => 19050 };
                }
            }
            else if (defaultVisible)
            {
                hex = "000000";
            }
        }

        if (hex is null)
        {
            return " draw:stroke=\"none\"";
        }

        var sb = new StringBuilder(" draw:stroke=\"").Append(dashed ? "dash" : "solid").Append("\" svg:stroke-width=\"").Append(OdpUnits.Cm(width > 0 ? width : 12700)).Append("\" svg:stroke-color=\"#").Append(hex.ToLowerInvariant()).Append('"');
        if (opacity is { } o)
        {
            sb.Append(" svg:stroke-opacity=\"").Append(((int)Math.Round(o * 100)).ToString(CultureInfo.InvariantCulture)).Append("%\"");
        }

        if (dashed)
        {
            sb.Append(" draw:stroke-dash=\"").Append(DashResource()).Append('"');
        }

        if (head is not (null or "none"))
        {
            sb.Append(" draw:marker-start=\"").Append(ArrowResource()).Append('"');
        }

        if (tail is not (null or "none"))
        {
            sb.Append(" draw:marker-end=\"").Append(ArrowResource()).Append('"');
        }

        return sb.ToString();
    }

    private string? _dash, _arrow;

    private string DashResource()
    {
        if (_dash is null)
        {
            _dash = "Dash";
            _resources.Append("<draw:stroke-dash draw:name=\"Dash\" draw:style=\"rect\" draw:dots1=\"1\" draw:dots1-length=\"0.2cm\" draw:distance=\"0.15cm\"/>");
        }

        return _dash;
    }

    private string ArrowResource()
    {
        if (_arrow is null)
        {
            _arrow = "Arrow";
            _resources.Append("<draw:marker draw:name=\"Arrow\" svg:viewBox=\"0 0 20 30\" svg:d=\"M10 0l-10 30h20z\"/>");
        }

        return _arrow;
    }

    private string GraphicStyle(Shape shape, MarkupRef? spPr, PresentationPart part, bool picture = false, bool line = false, string family = "graphic")
    {
        var attrs = new StringBuilder();
        if (!picture)
        {
            string fill = spPr is { } sp ? FillProperties(sp, part, out _) : "";
            if (fill.Length == 0 && !line && Pml.P(shape.Markup, "style") is { } style && Pml.A(style, "fillRef") is { } fr && Pml.AttrInt(fr, "idx") is > 0 && HexOf(part, fr, out _) is { } fh)
            {
                fill = " draw:fill=\"solid\" draw:fill-color=\"#" + fh.ToLowerInvariant() + "\"";
            }

            attrs.Append(fill.Length == 0 ? (line ? "" : " draw:fill=\"none\"") : fill);
        }

        attrs.Append(LineProperties(spPr, shape, part, defaultVisible: line));
        if (spPr is { } sp3 && Pml.A(sp3, "effectLst") is { } eff && Pml.A(eff, "outerShdw") is { } shd)
        {
            long dist = Pml.AttrLong(shd, "dist") ?? 0, dir = Pml.AttrLong(shd, "dir") ?? 0;
            double rad = dir / 60000.0 * Math.PI / 180;
            string? hex = HexOf(part, shd, out double? op);
            attrs.Append(" draw:shadow=\"visible\" draw:shadow-offset-x=\"").Append(OdpUnits.Cm((long)Math.Round(dist * Math.Cos(rad)))).Append("\" draw:shadow-offset-y=\"").Append(OdpUnits.Cm((long)Math.Round(dist * Math.Sin(rad))))
                .Append("\" draw:shadow-color=\"#").Append((hex ?? "000000").ToLowerInvariant()).Append('"');
            if (op is { } o)
            {
                attrs.Append(" draw:shadow-opacity=\"").Append(((int)Math.Round(o * 100)).ToString(CultureInfo.InvariantCulture)).Append("%\"");
            }
        }

        if (shape.TextBody is { } body && !picture)
        {
            BodyFormat bf = shape.ResolveBodyFormat();
            attrs.Append(" fo:padding-left=\"").Append(OdpUnits.Cm(bf.LeftInset)).Append("\" fo:padding-top=\"").Append(OdpUnits.Cm(bf.TopInset)).Append("\" fo:padding-right=\"").Append(OdpUnits.Cm(bf.RightInset))
                .Append("\" fo:padding-bottom=\"").Append(OdpUnits.Cm(bf.BottomInset)).Append('"');
            attrs.Append(bf.Anchor switch { "ctr" => " draw:textarea-vertical-align=\"middle\"", "b" => " draw:textarea-vertical-align=\"bottom\"", "just" => " draw:textarea-vertical-align=\"justify\"", _ => " draw:textarea-vertical-align=\"top\"" });
            attrs.Append(bf.Wrap == "none" ? " fo:wrap-option=\"no-wrap\"" : " fo:wrap-option=\"wrap\"");
            attrs.Append(bf.Autofit switch
            {
                AutofitKind.ShrinkText => " draw:auto-grow-height=\"false\" style:shrink-to-fit=\"true\"",
                AutofitKind.ResizeShape => " draw:auto-grow-height=\"true\"",
                _ => " draw:auto-grow-height=\"false\"",
            });
            _ = body;
        }

        return AddStyle(family, family == "graphic" ? "gr" : "pr", "", "<style:graphic-properties" + attrs + "/>");
    }
}
