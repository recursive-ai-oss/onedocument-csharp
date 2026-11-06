using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Ppt.Binary;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// Shapes: the drawing elements of a page, a master page or a group become p:sp, p:pic, p:cxnSp, p:grpSp and p:graphicFrame.
internal sealed partial class OdpImporter
{
    private readonly record struct Geom(long X, long Y, long W, long H, long Rot, bool FlipH, bool FlipV, bool HasSize);

    private static string ImageRel(Part part, string mediaPart) => part.AddRel(RelBase + "image", "../media/" + mediaPart[(mediaPart.LastIndexOf('/') + 1)..]);

    // ---- geometry -------------------------------------------------------------------------------------------------------------

    private static Geom ReadGeom(MarkupNode n)
    {
        long x = OdpUnits.Emu(A(n, OdfNamespaces.Svg, "x")), y = OdpUnits.Emu(A(n, OdfNamespaces.Svg, "y"));
        bool hasW = OdpUnits.TryEmu(A(n, OdfNamespaces.Svg, "width"), out long w);
        bool hasH = OdpUnits.TryEmu(A(n, OdfNamespaces.Svg, "height"), out long h);
        w = Math.Max(0, w);
        h = Math.Max(0, h);
        long rot = 0;
        if (A(n, OdfNamespaces.Draw, "transform") is { } transform)
        {
            ParseTransform(transform, out double angle, out double tx, out double ty, out bool hasTranslate);
            if (angle != 0)
            {
                double deg = -angle * 180 / Math.PI;
                rot = (long)(((deg % 360) + 360) % 360 * 60000);
            }

            if (hasTranslate)
            {
                // OpenDocument rotates the shape about its top-left corner and then translates it; PresentationML rotates about the centre.
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                double cx = tx + (w / 2.0) * cos + (h / 2.0) * sin;
                double cy = ty - (w / 2.0) * sin + (h / 2.0) * cos;
                x = (long)Math.Round(cx - w / 2.0);
                y = (long)Math.Round(cy - h / 2.0);
            }
        }

        MarkupNode eg = Child(n, OdfNamespaces.Draw, "enhanced-geometry");
        bool flipH = A(eg, OdfNamespaces.Draw, "mirror-horizontal") == "true";
        bool flipV = A(eg, OdfNamespaces.Draw, "mirror-vertical") == "true";
        return new Geom(x, y, w, h, rot, flipH, flipV, hasW && hasH);
    }

    // OpenDocument 1.3 Part 1 §20.? (draw:transform): "rotate (radians) translate (x y)", counter-clockwise angles, lengths in EMU after parsing.
    private static void ParseTransform(string s, out double angle, out double tx, out double ty, out bool hasTranslate)
    {
        angle = 0;
        tx = ty = 0;
        hasTranslate = false;
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ','))
            {
                i++;
            }

            int nameStart = i;
            while (i < s.Length && char.IsLetter(s[i]))
            {
                i++;
            }

            string name = s[nameStart..i];
            while (i < s.Length && s[i] != '(')
            {
                i++;
            }

            int open = i;
            while (i < s.Length && s[i] != ')')
            {
                i++;
            }

            if (open >= s.Length || name.Length == 0)
            {
                break;
            }

            string[] args = s[(open + 1)..i].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            i++;
            if (name == "rotate" && args.Length >= 1 && double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) && double.IsFinite(a))
            {
                angle += a;
            }
            else if (name == "translate" && args.Length >= 1)
            {
                hasTranslate = true;
                tx += OdpUnits.Emu(args[0]);
                ty += args.Length >= 2 ? OdpUnits.Emu(args[1]) : 0;
            }
        }
    }

    private static string Xfrm(Geom g, string tag = "a:xfrm")
    {
        var sb = new StringBuilder("<").Append(tag);
        if (g.Rot != 0)
        {
            sb.Append(" rot=\"").Append(g.Rot).Append('"');
        }

        if (g.FlipH)
        {
            sb.Append(" flipH=\"1\"");
        }

        if (g.FlipV)
        {
            sb.Append(" flipV=\"1\"");
        }

        return sb.Append("><a:off x=\"").Append(g.X).Append("\" y=\"").Append(g.Y).Append("\"/><a:ext cx=\"").Append(g.W).Append("\" cy=\"").Append(g.H).Append("\"/></").Append(tag).Append('>').ToString();
    }

    private static void Grow(Extent? ext, Geom g) => ext?.Add(g.X, g.Y, g.W, g.H);

    // ---- dispatch -------------------------------------------------------------------------------------------------------------

    private void WriteShapes(Scope scope, MarkupNode container, StringBuilder sb, Extent? ext)
    {
        _depth++;
        try
        {
            CheckDepth(container);
            if (_depth > MaxGroupDepth)
            {
                Defect(DefectKind.Structure, "odp.group-too-deep", DefectSeverity.Dropped, "Groups are nested more deeply than the reader follows; the inner shapes were dropped.", container);
                return;
            }

            foreach (MarkupNode c in container.ChildElements)
            {
                _ct.ThrowIfCancellationRequested();
                if (c.NamespaceUri != OdfNamespaces.Draw)
                {
                    continue;
                }

                switch (c.LocalNameString)
                {
                    case "frame":
                        WriteFrame(scope, c, sb, ext);
                        break;
                    case "custom-shape" or "rect" or "ellipse" or "circle" or "polygon" or "polyline" or "path" or "regular-polygon" or "caption":
                        WriteGeometryShape(scope, c, sb, ext);
                        break;
                    case "line" or "connector":
                        WriteLine(scope, c, sb, ext);
                        break;
                    case "g":
                        WriteGroup(scope, c, sb, ext);
                        break;
                    case "a":
                        WriteShapes(scope, c, sb, ext);
                        break;
                    case "page-thumbnail" or "glue-point" or "layer" or "layer-set" or "gluepoint":
                        break;
                    case "measure" or "control" or "plugin" or "floating-frame" or "applet" or "object" or "object-ole":
                        Lost("loss.odp-" + c.LocalNameString, "drawing objects (" + c.LocalNameString + ")", "This kind of drawing object has no counterpart in the presentation model and was dropped.");
                        break;
                    default:
                        Lost("loss.unmapped-markup." + c.LocalNameString, "unmapped markup (" + c.LocalNameString + ")", "Elements with no counterpart in the presentation model were skipped.");
                        break;
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    private static string NameOf(MarkupNode n, string fallback, int id)
    {
        string? name = A(n, OdfNamespaces.Draw, "name");
        return string.IsNullOrEmpty(name) ? fallback + " " + OdpUnits.I(id) : name;
    }

    // svg:title and svg:desc children are a shape's alternative text; the description wins.
    private static string AltAttribute(MarkupNode n)
    {
        string? desc = null, title = null;
        foreach (MarkupNode c in n.ChildElements)
        {
            if (Is(c, OdfNamespaces.Svg, "desc"))
            {
                desc = c.InnerText;
            }
            else if (Is(c, OdfNamespaces.Svg, "title"))
            {
                title = c.InnerText;
            }
        }

        string? alt = string.IsNullOrEmpty(desc) ? title : desc;
        return string.IsNullOrEmpty(alt) ? "" : " descr=\"" + OdpUnits.Esc(alt) + "\"";
    }

    private ShapeStyle StyleOf(MarkupNode n) =>
        new(_styles, A(n, OdfNamespaces.Presentation, "style-name"), A(n, OdfNamespaces.Draw, "style-name"), A(n, OdfNamespaces.Draw, "text-style-name"));

    // ---- geometric shapes -----------------------------------------------------------------------------------------------------

    private void WriteGeometryShape(Scope scope, MarkupNode n, StringBuilder sb, Extent? ext)
    {
        Geom g = ReadGeom(n);
        string kind = n.LocalNameString;
        string? custom = null;
        if (kind is "polygon" or "polyline" or "path" or "regular-polygon")
        {
            // The shape's box comes from its viewBox when it has no size of its own.
            custom = CustomGeometryFrom(n, kind, ref g);
        }
        else if (kind == "custom-shape")
        {
            custom = null;
        }

        Part part = scope.Part;
        ShapeStyle style = StyleOf(n);
        int id = part.NextId();
        Grow(ext, g);
        bool hasText = HasText(n);
        sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(OdpUnits.Esc(NameOf(n, "Shape", id))).Append('"').Append(AltAttribute(n)).Append("/><p:cNvSpPr")
            .Append(kind == "custom-shape" || !hasText ? "" : "").Append("/><p:nvPr/></p:nvSpPr><p:spPr>").Append(Xfrm(g));
        sb.Append(custom ?? PresetGeometry(n, kind, style));
        sb.Append(ShapeFill(style, part, n)).Append(ShapeLine(style)).Append(ShapeEffects(style)).Append("</p:spPr>");
        if (hasText)
        {
            WriteTextBody(scope, sb, n, style, "p:txBody", placeholder: false);
        }

        sb.Append("</p:sp>");
    }

    private static bool HasText(MarkupNode n)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.NamespaceUri == OdfNamespaces.Text && c.LocalNameString is "p" or "h" or "list")
            {
                return true;
            }
        }

        return false;
    }

    private string PresetGeometry(MarkupNode n, string kind, ShapeStyle style)
    {
        _ = style;
        string preset = "rect";
        string avList = "";
        switch (kind)
        {
            case "ellipse" or "circle":
                preset = "ellipse";
                if (A(n, OdfNamespaces.Draw, "kind") is { } k && k != "full")
                {
                    Lost("loss.odp-ellipse-" + k, "ellipse " + k + "s", "Partial ellipses (arcs, sections and cuts) are drawn as whole ellipses.");
                }

                break;
            case "rect" or "caption":
                if (OdpUnits.Emu(A(n, OdfNamespaces.Svg, "rx")) > 0 || OdpUnits.Emu(A(n, OdfNamespaces.Draw, "corner-radius")) > 0)
                {
                    preset = "roundRect";
                }

                if (kind == "caption")
                {
                    Lost("loss.odp-caption", "caption shapes", "The callout line of a caption shape is not carried; the shape is a rectangle.");
                }

                break;
            case "custom-shape":
                {
                    MarkupNode eg = Child(n, OdfNamespaces.Draw, "enhanced-geometry");
                    string? type = A(eg, OdfNamespaces.Draw, "type");
                    string? mapped = type is null ? null : PresetOfType(type);
                    if (mapped is null)
                    {
                        if (type is not null || !eg.IsNull)
                        {
                            Lost("loss.odp-custom-geometry", "custom shape geometries", "Shapes whose geometry is a formula-driven path are drawn as rectangles.");
                        }

                        mapped = "rect";
                    }
                    else if (type!.StartsWith("ooxml-", StringComparison.Ordinal) && A(eg, OdfNamespaces.Draw, "modifiers") is { } mods)
                    {
                        // The modifiers of an ooxml-* shape are the preset's adjust values in the order the preset declares them. PowerPoint
                        // refuses a file whose avLst names a guide the preset does not have (a roundRect has "adj", not "adj1"), so the names
                        // come from the preset's own definition and values beyond them are dropped.
                        string[]? names = PresetAdjustNames.Of(mapped);
                        var av = new StringBuilder();
                        int i = 0;
                        foreach (string token in mods.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (names is null || i >= names.Length)
                            {
                                break;
                            }

                            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
                            {
                                av.Append("<a:gd name=\"").Append(names[i]).Append("\" fmla=\"val ").Append(OdpUnits.I((long)Math.Round(v))).Append("\"/>");
                            }

                            i++;
                        }

                        avList = av.ToString();
                    }

                    preset = mapped;
                    break;
                }
        }

        return "<a:prstGeom prst=\"" + preset + "\"><a:avLst>" + avList + "</a:avLst></a:prstGeom>";
    }

    // draw:type of an enhanced geometry to a DrawingML preset; null when there is no preset for it.
    private static string? PresetOfType(string type)
    {
        if (type.StartsWith("ooxml-", StringComparison.Ordinal))
        {
            string name = type[6..];
            return name.Length is > 0 and <= 40 && name.All(char.IsAsciiLetterOrDigit) ? name : null;
        }

        if (type.StartsWith("mso-spt", StringComparison.Ordinal) && int.TryParse(type.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
        {
            return PptShapeTypes.Preset(n);
        }

        if (type.StartsWith("flowchart-", StringComparison.Ordinal))
        {
            string[] parts = type[10..].Split('-');
            return "flowChart" + string.Concat(parts.Select(p => p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p[1..]));
        }

        return type switch
        {
            "rectangle" or "quadrat" => "rect",
            "round-rectangle" or "round-quadrat" => "roundRect",
            "ellipse" => "ellipse",
            "diamond" => "diamond",
            "isosceles-triangle" => "triangle",
            "right-triangle" => "rtTriangle",
            "parallelogram" => "parallelogram",
            "trapezoid" => "trapezoid",
            "hexagon" => "hexagon",
            "octagon" => "octagon",
            "cross" => "plus",
            "star4" or "star5" or "star6" or "star8" or "star12" or "star24" => type,
            "right-arrow" => "rightArrow",
            "left-arrow" => "leftArrow",
            "up-arrow" => "upArrow",
            "down-arrow" => "downArrow",
            "left-right-arrow" => "leftRightArrow",
            "up-down-arrow" => "upDownArrow",
            "can" => "can",
            "cube" => "cube",
            "pentagon-right" => "homePlate",
            "heart" => "heart",
            "smiley" => "smileyFace",
            "sun" => "sun",
            "moon" => "moon",
            "lightning" => "lightningBolt",
            "cloud" => "cloud",
            "ring" => "donut",
            "frame" => "frame",
            "forbidden" => "noSmoking",
            "block-arc" => "blockArc",
            "paper" => "foldedCorner",
            _ => null,
        };
    }

    // ---- custom geometry from points and paths --------------------------------------------------------------------------------

    private string? CustomGeometryFrom(MarkupNode n, string kind, ref Geom g)
    {
        string[] vb = (A(n, OdfNamespaces.Svg, "viewBox") ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (vb.Length != 4 || !double.TryParse(vb[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double vx) || !double.TryParse(vb[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double vy)
            || !double.TryParse(vb[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double vw) || !double.TryParse(vb[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double vh)
            || vw <= 0 || vh <= 0 || !double.IsFinite(vx + vy + vw + vh))
        {
            Defect(DefectKind.Value, "odp.bad-viewbox", DefectSeverity.Dropped, "A polygon or path has no usable svg:viewBox; it was drawn as a rectangle.", n);
            return null;
        }

        var cmds = new StringBuilder();
        bool closed = false;
        if (kind == "path")
        {
            closed = PathData(A(n, OdfNamespaces.Svg, "d") ?? "", vx, vy, cmds, n);
        }
        else if (kind is "polygon" or "polyline")
        {
            string[] pts = (A(n, OdfNamespaces.Draw, "points") ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                if (!double.TryParse(pts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double px) || !double.TryParse(pts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double py))
                {
                    break;
                }

                cmds.Append(i == 0 ? "<a:moveTo>" : "<a:lnTo>").Append(Pt(px - vx, py - vy)).Append(i == 0 ? "</a:moveTo>" : "</a:lnTo>");
            }

            if (kind == "polygon")
            {
                cmds.Append("<a:close/>");
                closed = true;
            }
        }
        else
        {
            Lost("loss.odp-regular-polygon", "regular polygons", "Regular polygons are drawn as rectangles.");
            return null;
        }

        if (cmds.Length == 0)
        {
            return null;
        }

        if (!g.HasSize)
        {
            g = g with { W = OdpUnits.Emu(A(n, OdfNamespaces.Svg, "width")), H = OdpUnits.Emu(A(n, OdfNamespaces.Svg, "height")) };
        }

        return "<a:custGeom><a:avLst/><a:gdLst/><a:ahLst/><a:cxnLst/><a:rect l=\"0\" t=\"0\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"" + OdpUnits.I((long)Math.Round(vw)) + "\" h=\"" + OdpUnits.I((long)Math.Round(vh))
            + (closed ? "\"" : "\" fill=\"none\"") + ">" + cmds + "</a:path></a:pathLst></a:custGeom>";
    }

    private static string Pt(double x, double y) => "<a:pt x=\"" + OdpUnits.I((long)Math.Round(x)) + "\" y=\"" + OdpUnits.I((long)Math.Round(y)) + "\"/>";

    // SVG 1.1 path data (M L H V C S Q T Z, absolute and relative; arcs become straight lines to their end point).
    private bool PathData(string d, double vx, double vy, StringBuilder cmds, MarkupNode at)
    {
        int i = 0;
        char cmd = 'M';
        double cx = 0, cy = 0, sx = 0, sy = 0, lastCx = 0, lastCy = 0;
        bool closed = false, arcSeen = false, any = false;
        char prev = ' ';
        double Next()
        {
            while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ','))
            {
                i++;
            }

            int start = i;
            if (i < d.Length && d[i] is '-' or '+')
            {
                i++;
            }

            bool dot = false;
            while (i < d.Length && (char.IsAsciiDigit(d[i]) || (d[i] == '.' && !dot)))
            {
                dot |= d[i] == '.';
                i++;
            }

            if (i < d.Length && d[i] is 'e' or 'E' && i + 1 < d.Length && (char.IsAsciiDigit(d[i + 1]) || d[i + 1] is '-' or '+'))
            {
                i += 2;
                while (i < d.Length && char.IsAsciiDigit(d[i]))
                {
                    i++;
                }
            }

            return i > start && double.TryParse(d.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : double.NaN;
        }

        bool HasNumber()
        {
            while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ','))
            {
                i++;
            }

            return i < d.Length && (char.IsAsciiDigit(d[i]) || d[i] is '-' or '+' or '.');
        }

        int guard = 0;
        while (i < d.Length && guard++ < 1_000_000)
        {
            while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ','))
            {
                i++;
            }

            if (i >= d.Length)
            {
                break;
            }

            if (char.IsLetter(d[i]))
            {
                cmd = d[i++];
            }
            else if (!HasNumber())
            {
                break;
            }

            bool rel = char.IsLower(cmd);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    {
                        double x = Next(), y = Next();
                        if (double.IsNaN(x + y))
                        {
                            return closed;
                        }

                        if (rel) { x += cx; y += cy; }
                        (cx, cy, sx, sy) = (x, y, x, y);
                        cmds.Append("<a:moveTo>").Append(Pt(x - vx, y - vy)).Append("</a:moveTo>");
                        cmd = rel ? 'l' : 'L';
                        any = true;
                        break;
                    }

                case 'L':
                    {
                        double x = Next(), y = Next();
                        if (double.IsNaN(x + y))
                        {
                            return closed;
                        }

                        if (rel) { x += cx; y += cy; }
                        (cx, cy) = (x, y);
                        cmds.Append("<a:lnTo>").Append(Pt(x - vx, y - vy)).Append("</a:lnTo>");
                        break;
                    }

                case 'H':
                    {
                        double x = Next();
                        if (double.IsNaN(x))
                        {
                            return closed;
                        }

                        cx = rel ? cx + x : x;
                        cmds.Append("<a:lnTo>").Append(Pt(cx - vx, cy - vy)).Append("</a:lnTo>");
                        break;
                    }

                case 'V':
                    {
                        double y = Next();
                        if (double.IsNaN(y))
                        {
                            return closed;
                        }

                        cy = rel ? cy + y : y;
                        cmds.Append("<a:lnTo>").Append(Pt(cx - vx, cy - vy)).Append("</a:lnTo>");
                        break;
                    }

                case 'C' or 'S':
                    {
                        bool smooth = char.ToUpperInvariant(cmd) == 'S';
                        double x1, y1;
                        if (smooth)
                        {
                            bool prevCubic = prev is 'C' or 'S';
                            x1 = prevCubic ? 2 * cx - lastCx : cx;
                            y1 = prevCubic ? 2 * cy - lastCy : cy;
                        }
                        else
                        {
                            x1 = Next();
                            y1 = Next();
                            if (rel) { x1 += cx; y1 += cy; }
                        }

                        double x2 = Next(), y2 = Next(), x = Next(), y = Next();
                        if (double.IsNaN(x1 + y1 + x2 + y2 + x + y))
                        {
                            return closed;
                        }

                        if (rel) { x2 += cx; y2 += cy; x += cx; y += cy; }
                        cmds.Append("<a:cubicBezTo>").Append(Pt(x1 - vx, y1 - vy)).Append(Pt(x2 - vx, y2 - vy)).Append(Pt(x - vx, y - vy)).Append("</a:cubicBezTo>");
                        (lastCx, lastCy, cx, cy) = (x2, y2, x, y);
                        break;
                    }

                case 'Q' or 'T':
                    {
                        bool smooth = char.ToUpperInvariant(cmd) == 'T';
                        double x1, y1;
                        if (smooth)
                        {
                            bool prevQuad = prev is 'Q' or 'T';
                            x1 = prevQuad ? 2 * cx - lastCx : cx;
                            y1 = prevQuad ? 2 * cy - lastCy : cy;
                        }
                        else
                        {
                            x1 = Next();
                            y1 = Next();
                            if (rel) { x1 += cx; y1 += cy; }
                        }

                        double x = Next(), y = Next();
                        if (double.IsNaN(x1 + y1 + x + y))
                        {
                            return closed;
                        }

                        if (rel) { x += cx; y += cy; }
                        cmds.Append("<a:quadBezTo>").Append(Pt(x1 - vx, y1 - vy)).Append(Pt(x - vx, y - vy)).Append("</a:quadBezTo>");
                        (lastCx, lastCy, cx, cy) = (x1, y1, x, y);
                        break;
                    }

                case 'A':
                    {
                        double rx = Next(), ry = Next(), rot = Next(), large = Next(), sweep = Next(), x = Next(), y = Next();
                        if (double.IsNaN(rx + ry + rot + large + sweep + x + y))
                        {
                            return closed;
                        }

                        if (rel) { x += cx; y += cy; }
                        arcSeen = true;
                        (cx, cy) = (x, y);
                        cmds.Append("<a:lnTo>").Append(Pt(x - vx, y - vy)).Append("</a:lnTo>");
                        break;
                    }

                case 'Z':
                    cmds.Append("<a:close/>");
                    closed = true;
                    (cx, cy) = (sx, sy);
                    break;
                default:
                    Defect(DefectKind.Value, "odp.bad-path", DefectSeverity.Dropped, "A path holds the command '" + cmd + "', which is not SVG path data; the rest of the path was dropped.", at);
                    return closed;
            }

            prev = char.ToUpperInvariant(cmd);
        }

        if (arcSeen)
        {
            Lost("loss.odp-path-arcs", "elliptical arcs in paths", "Elliptical arc segments of a path are drawn as straight lines.");
        }

        return closed && any;
    }

    // ---- lines and connectors -------------------------------------------------------------------------------------------------

    private void WriteLine(Scope scope, MarkupNode n, StringBuilder sb, Extent? ext)
    {
        long x1 = OdpUnits.Emu(A(n, OdfNamespaces.Svg, "x1")), y1 = OdpUnits.Emu(A(n, OdfNamespaces.Svg, "y1"));
        long x2 = OdpUnits.Emu(A(n, OdfNamespaces.Svg, "x2")), y2 = OdpUnits.Emu(A(n, OdfNamespaces.Svg, "y2"));
        var g = new Geom(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1), 0, x2 < x1, y2 < y1, true);
        Grow(ext, g);
        ShapeStyle style = StyleOf(n);
        int id = scope.Part.NextId();
        string preset = n.LocalNameString == "line" ? "line" : A(n, OdfNamespaces.Draw, "type") switch
        {
            "curve" => "curvedConnector3",
            "standard" => "bentConnector3",
            "lines" => "bentConnector2",
            _ => "straightConnector1",
        };
        sb.Append("<p:cxnSp><p:nvCxnSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(OdpUnits.Esc(NameOf(n, n.LocalNameString == "line" ? "Line" : "Connector", id))).Append('"').Append(AltAttribute(n))
            .Append("/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr><p:spPr>").Append(Xfrm(g)).Append("<a:prstGeom prst=\"").Append(preset).Append("\"><a:avLst/></a:prstGeom>")
            .Append(ShapeLine(style, defaultVisible: true)).Append(ShapeEffects(style)).Append("</p:spPr></p:cxnSp>");
        if (HasText(n))
        {
            Lost("loss.odp-line-text", "text on lines", "Text attached to a line or connector has no counterpart and was dropped.");
        }
    }

    // ---- groups ---------------------------------------------------------------------------------------------------------------

    private void WriteGroup(Scope scope, MarkupNode n, StringBuilder sb, Extent? ext)
    {
        var inner = new StringBuilder();
        var box = new Extent();
        int id = scope.Part.NextId();
        WriteShapes(scope, n, inner, box);
        if (!box.Any)
        {
            return;
        }

        var g = new Geom(box.MinX, box.MinY, box.MaxX - box.MinX, box.MaxY - box.MinY, 0, false, false, true);
        Grow(ext, g);
        sb.Append("<p:grpSp><p:nvGrpSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(OdpUnits.Esc(NameOf(n, "Group", id))).Append("\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"")
            .Append(g.X).Append("\" y=\"").Append(g.Y).Append("\"/><a:ext cx=\"").Append(g.W).Append("\" cy=\"").Append(g.H).Append("\"/><a:chOff x=\"").Append(g.X).Append("\" y=\"").Append(g.Y)
            .Append("\"/><a:chExt cx=\"").Append(g.W).Append("\" cy=\"").Append(g.H).Append("\"/></a:xfrm></p:grpSpPr>").Append(inner).Append("</p:grpSp>");
    }

    // ---- frames ---------------------------------------------------------------------------------------------------------------

    private static MarkupNode MasterFrameOfClass(MasterInfo master, string cls)
    {
        if (master.Node.IsNull)
        {
            return default;
        }

        foreach (MarkupNode f in master.Node.ChildElements)
        {
            if (Is(f, OdfNamespaces.Draw, "frame") && A(f, OdfNamespaces.Presentation, "class") == cls)
            {
                return f;
            }
        }

        return default;
    }

    private void WriteFrame(Scope scope, MarkupNode frame, StringBuilder sb, Extent? ext)
    {
        string? cls = A(frame, OdfNamespaces.Presentation, "class");
        if (cls is "page" or "handout")
        {
            return;
        }

        Geom g = ReadGeom(frame);
        if (!g.HasSize && cls is not null && !MasterFrameOfClass(scope.Master, cls).IsNull && !scope.FromMaster)
        {
            // A placeholder that gives no box takes the one the master page's frame of the same kind has.
            g = ReadGeom(MasterFrameOfClass(scope.Master, cls));
        }

        ShapeStyle style = StyleOf(frame);
        MarkupNode textBox = Child(frame, OdfNamespaces.Draw, "text-box");
        MarkupNode image = Child(frame, OdfNamespaces.Draw, "image");
        MarkupNode table = Child(frame, OdfNamespaces.Table, "table");
        Grow(ext, g);
        if (!table.IsNull)
        {
            WriteTableFrame(scope, frame, table, g, sb);
            return;
        }

        if (!textBox.IsNull || (image.IsNull && Child(frame, OdfNamespaces.Draw, "object").IsNull && Child(frame, OdfNamespaces.Draw, "object-ole").IsNull))
        {
            WriteTextFrame(scope, frame, textBox, cls, g, style, sb);
            return;
        }

        // A picture, or an object (chart, formula, OLE) with a preview picture.
        bool isObject = !Child(frame, OdfNamespaces.Draw, "object").IsNull || !Child(frame, OdfNamespaces.Draw, "object-ole").IsNull;
        if (isObject)
        {
            Lost("loss.odp-embedded-object", "embedded objects", "Charts, formulas and other embedded objects are not carried; their preview picture is kept when the file has one.");
        }

        if (image.IsNull || RegisterImage(image) is not { } media)
        {
            return;
        }

        if (style.Shape.Graphic(OdfNamespaces.Fo, "clip") is { } clip && clip.Replace(" ", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal) is not ("rect(0cm0cm0cm0cm)" or "rect(0pt0pt0pt0pt)" or "auto"))
        {
            Lost("loss.odp-picture-crop", "cropped pictures", "Cropping needs the picture's size; cropped pictures are shown whole.");
        }

        string rid = ImageRel(scope.Part, media);
        int id = scope.Part.NextId();
        sb.Append("<p:pic><p:nvPicPr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(OdpUnits.Esc(NameOf(frame, "Picture", id))).Append('"').Append(AltAttribute(frame)).Append("/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>")
            .Append("<p:blipFill><a:blip r:embed=\"").Append(rid).Append("\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>").Append(Xfrm(g)).Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>")
            .Append(ShapeLine(style)).Append(ShapeEffects(style)).Append("</p:spPr></p:pic>");
    }

    private void WriteTextFrame(Scope scope, MarkupNode frame, MarkupNode textBox, string? cls, Geom g, ShapeStyle style, StringBuilder sb)
    {
        Part part = scope.Part;
        string? ph = null;
        string name;
        int id = part.NextId();
        bool master = scope.Kind == SceneKind.Master;
        switch (cls)
        {
            case "title":
                ph = "type=\"title\"";
                name = "Title";
                break;
            case "subtitle":
                ph = master ? null : "type=\"subTitle\" idx=\"1\"";
                name = "Subtitle";
                break;
            case "outline" or "text":
                ph = "type=\"body\" idx=\"" + OdpUnits.I(++scope.Counters.Body) + "\"";
                name = "Content";
                break;
            case "date-time":
                ph = "type=\"dt\" sz=\"half\" idx=\"10\"";
                name = "Date";
                break;
            case "footer":
                ph = "type=\"ftr\" sz=\"quarter\" idx=\"11\"";
                name = "Footer";
                break;
            case "page-number":
                ph = "type=\"sldNum\" sz=\"quarter\" idx=\"12\"";
                name = "Slide Number";
                break;
            default:
                name = "TextBox";
                break;
        }

        // Only the slide's own text and the master's displayed frames are placeholders; the master's other text is plain text.
        sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(OdpUnits.Esc(A(frame, OdfNamespaces.Draw, "name") is { Length: > 0 } dn ? dn : name + " " + OdpUnits.I(id))).Append('"').Append(AltAttribute(frame)).Append("/>");
        sb.Append(ph is null ? "<p:cNvSpPr txBox=\"1\"/>" : "<p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr>").Append("<p:nvPr>");
        if (ph is not null)
        {
            sb.Append("<p:ph ").Append(ph).Append("/>");
        }

        sb.Append("</p:nvPr></p:nvSpPr><p:spPr>").Append(Xfrm(g)).Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>");
        sb.Append(ShapeFill(style, part, frame)).Append(ShapeLine(style)).Append(ShapeEffects(style)).Append("</p:spPr>");
        WriteTextBody(scope, sb, textBox.IsNull ? frame : textBox, style, "p:txBody", placeholder: true, footerText: FooterText(scope, cls));
        sb.Append("</p:sp>");
    }

    // The text a footer, header or date frame of a master page shows on a slide comes from the declaration the slide names.
    private string? FooterText(Scope scope, string? cls)
    {
        if (!scope.FromMaster || scope.Page.IsNull)
        {
            return null;
        }

        switch (cls)
        {
            case "footer" when A(scope.Page, OdfNamespaces.Presentation, "use-footer-name") is { } f && _footerDecls.TryGetValue(f, out string? ft):
                return ft;
            case "date-time" when A(scope.Page, OdfNamespaces.Presentation, "use-date-time-name") is { } d && _dateDecls.TryGetValue(d, out string? dt):
                return dt;
            default:
                return null;
        }
    }
}
