using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// A list of styles from the strongest to the weakest. A property is the first one found along each style's own parent chain; the
// family defaults come last, so that a default never beats a style that sets the property explicitly.
internal sealed class StyleCascade
{
    private readonly OdfStyleSet _set;
    private readonly OdfStyle?[] _styles;

    public StyleCascade(OdfStyleSet set, params OdfStyle?[] strongestFirst)
    {
        _set = set;
        _styles = strongestFirst;
    }

    // The colour that "automatic" text takes (black or white, whichever contrasts with the shape's fill), or null for the theme's text colour.
    public string? AutoColor { get; set; }

    public StyleCascade With(OdfStyle? stronger)
    {
        var all = new OdfStyle?[_styles.Length + 1];
        all[0] = stronger;
        Array.Copy(_styles, 0, all, 1, _styles.Length);
        return new StyleCascade(_set, all) { AutoColor = AutoColor };
    }

    public string? Get(string properties, string ns, string attribute)
    {
        foreach (OdfStyle? s in _styles)
        {
            if (s is null)
            {
                continue;
            }

            foreach (OdfStyle link in _set.Chain(s))
            {
                if (link.IsDefault)
                {
                    continue;
                }

                if (OdfMarkup.Attribute(link.Properties(properties), ns, attribute) is { } v)
                {
                    return v;
                }
            }
        }

        foreach (OdfStyle? s in _styles)
        {
            if (s is not null && _set.GetDefault(s.Family) is { } def && OdfMarkup.Attribute(def.Properties(properties), ns, attribute) is { } v)
            {
                return v;
            }
        }

        return null;
    }

    public string? Text(string ns, string attribute) => Get("text-properties", ns, attribute);

    public string? Para(string ns, string attribute) => Get("paragraph-properties", ns, attribute);

    public string? Graphic(string ns, string attribute) => Get("graphic-properties", ns, attribute);

    // A font size in points: "18pt" or a length; percentages are not sizes here.
    public double? TextPoints(string attribute) =>
        OdpUnits.TryPoints(Text(OdfNamespaces.Fo, attribute), out double pt) ? pt : null;

    // The first style in the chain that carries a child element (a text:list-style inside a presentation style, for one).
    public MarkupNode FindChild(string properties, string ns, string local)
    {
        foreach (OdfStyle? s in _styles)
        {
            if (s is null)
            {
                continue;
            }

            foreach (OdfStyle link in _set.Chain(s))
            {
                MarkupNode c = OdfMarkup.Child(link.Properties(properties), ns, local);
                if (!c.IsNull)
                {
                    return c;
                }
            }
        }

        return default;
    }
}

// The styles a shape names: draw:style-name (graphic family), presentation:style-name (presentation family) and draw:text-style-name
// (a paragraph style giving the frame's text its defaults).
internal sealed class ShapeStyle
{
    public ShapeStyle(OdfStyleSet set, string? presentationStyle, string? graphicStyle, string? textStyle, OdfStyle? extraText = null)
    {
        Presentation = presentationStyle is null ? null : set.Find("presentation", presentationStyle);
        Graphic = graphicStyle is null ? null : set.Find("graphic", graphicStyle);
        TextDefaults = textStyle is null ? null : set.Find("paragraph", textStyle);
        Shape = new StyleCascade(set, Presentation, Graphic);
        if (Graphic is null && Presentation is null)
        {
            Shape = new StyleCascade(set, set.GetDefault("graphic"));
        }

        Text = Shape.With(TextDefaults).With(extraText);
        string? fillColor = Shape.Graphic(OdfNamespaces.Draw, "fill") == "solid" ? OdpUnits.Color(Shape.Graphic(OdfNamespaces.Draw, "fill-color")) : null;
        if (fillColor is not null)
        {
            // Relative luminance, rounded: dark fills get white text.
            int r = System.Convert.ToInt32(fillColor[..2], 16), g = System.Convert.ToInt32(fillColor.Substring(2, 2), 16), b = System.Convert.ToInt32(fillColor.Substring(4, 2), 16);
            Text.AutoColor = 0.2126 * r + 0.7152 * g + 0.0722 * b < 128 ? "FFFFFF" : "000000";
        }
    }

    public OdfStyle? Presentation { get; }

    public OdfStyle? Graphic { get; }

    public OdfStyle? TextDefaults { get; }

    public StyleCascade Shape { get; }

    public StyleCascade Text { get; }
}

internal sealed partial class OdpImporter
{
    private enum SceneKind
    {
        Slide,
        Master,
        Notes,
    }

    private sealed class Counters
    {
        public int Body;
    }

    private sealed record Scope(Part Part, MasterInfo Master, SceneKind Kind, MarkupNode Page)
    {
        public bool FromMaster { get; init; }

        public Counters Counters { get; } = new();
    }

    // The union of the boxes of the shapes of a group.
    private sealed class Extent
    {
        public bool Any;
        public long MinX, MinY, MaxX, MaxY;

        public void Add(long x, long y, long w, long h)
        {
            if (!Any)
            {
                (MinX, MinY, MaxX, MaxY, Any) = (x, y, x + w, y + h, true);
                return;
            }

            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x + w);
            MaxY = Math.Max(MaxY, y + h);
        }
    }

    // ---- fills, lines and effects ---------------------------------------------------------------------------------------------

    private static string Srgb(string hex, double? alpha = null) =>
        alpha is { } a && a < 1 ? "<a:srgbClr val=\"" + hex + "\"><a:alpha val=\"" + OdpUnits.I((long)Math.Round(Math.Clamp(a, 0, 1) * 100000)) + "\"/></a:srgbClr>" : "<a:srgbClr val=\"" + hex + "\"/>";

    // The fill element for a draw:fill value; draw and fo give the other attributes of the same property group. Empty when nothing maps.
    private string FillXml(string fill, Func<string, string?> draw, Func<string, string?> fo, Part part, MarkupNode at)
    {
        _ = fo;
        double? alpha = draw("opacity") is { } op ? OdpUnits.Percent(op) : null;
        switch (fill)
        {
            case "none":
                return "<a:noFill/>";
            case "solid":
                {
                    string? hex = OdpUnits.Color(draw("fill-color"));
                    return hex is null ? "" : "<a:solidFill>" + Srgb(hex, alpha) + "</a:solidFill>";
                }

            case "gradient":
                return GradientXml(draw("fill-gradient-name"), alpha);
            case "bitmap":
                {
                    string? name = draw("fill-image-name");
                    MarkupNode img = name is null ? default : FindNamed("fill-image", name);
                    if (!img.IsNull && RegisterImage(img) is { } media)
                    {
                        string rid = ImageRel(part, media);
                        return "<a:blipFill><a:blip r:embed=\"" + rid + "\"/><a:stretch><a:fillRect/></a:stretch></a:blipFill>";
                    }

                    Defect(DefectKind.Reference, "odp.missing-fill-image", DefectSeverity.Dropped, "A bitmap fill names an image that is not there.", at);
                    return "";
                }

            default:
                Lost("loss.odp-fill-" + fill, "fills of type " + fill, "Hatch fills have no counterpart in the presentation model; the shape has no fill.");
                return "<a:noFill/>";
        }
    }

    // A named draw: resource (gradient, stroke-dash, fill-image, marker) from the common styles.
    private MarkupNode FindNamed(string local, string name)
    {
        foreach (MarkupNode root in new[] { _pkg.StylesRoot, _pkg.ContentRoot })
        {
            foreach (string container in new[] { "styles", "automatic-styles" })
            {
                MarkupNode c = Child(root, OdfNamespaces.Office, container);
                if (c.IsNull)
                {
                    continue;
                }

                foreach (MarkupNode e in c.ChildElements)
                {
                    if (Is(e, OdfNamespaces.Draw, local) && A(e, OdfNamespaces.Draw, "name") == name)
                    {
                        return e;
                    }
                }
            }
        }

        return default;
    }

    private string GradientXml(string? name, double? alpha)
    {
        MarkupNode g = name is null ? default : FindNamed("gradient", name);
        if (g.IsNull)
        {
            return "";
        }

        string start = OdpUnits.Color(A(g, OdfNamespaces.Draw, "start-color")) ?? "FFFFFF";
        string end = OdpUnits.Color(A(g, OdfNamespaces.Draw, "end-color")) ?? "000000";
        double angle = int.TryParse(A(g, OdfNamespaces.Draw, "angle"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tenths) ? tenths / 10.0 : 0;
        string style = A(g, OdfNamespaces.Draw, "style") ?? "linear";
        // OpenDocument counts the angle counter-clockwise from "start colour at the top"; DrawingML clockwise from the x axis.
        long ang = (long)(((90 - angle) % 360 + 360) % 360 * 60000);
        var sb = new StringBuilder("<a:gradFill rotWithShape=\"1\"><a:gsLst>");
        if (style == "axial")
        {
            sb.Append("<a:gs pos=\"0\">").Append(Srgb(end, alpha)).Append("</a:gs><a:gs pos=\"50000\">").Append(Srgb(start, alpha)).Append("</a:gs><a:gs pos=\"100000\">").Append(Srgb(end, alpha)).Append("</a:gs>");
        }
        else
        {
            sb.Append("<a:gs pos=\"0\">").Append(Srgb(start, alpha)).Append("</a:gs><a:gs pos=\"100000\">").Append(Srgb(end, alpha)).Append("</a:gs>");
        }

        sb.Append("</a:gsLst>");
        if (style is "radial" or "ellipsoid" or "square" or "rectangular")
        {
            sb.Append("<a:path path=\"").Append(style is "radial" or "ellipsoid" ? "circle" : "rect").Append("\"><a:fillToRect l=\"50000\" t=\"50000\" r=\"50000\" b=\"50000\"/></a:path>");
        }
        else
        {
            sb.Append("<a:lin ang=\"").Append(ang).Append("\" scaled=\"0\"/>");
        }

        sb.Append("</a:gradFill>");
        return sb.ToString();
    }

    private string ShapeFill(ShapeStyle style, Part part, MarkupNode at)
    {
        string fill = style.Shape.Graphic(OdfNamespaces.Draw, "fill") ?? "none";
        string xml = FillXml(fill, k => style.Shape.Graphic(OdfNamespaces.Draw, k), k => style.Shape.Graphic(OdfNamespaces.Fo, k), part, at);
        return xml.Length == 0 ? "<a:noFill/>" : xml;
    }

    // ECMA-376 Part 1 §20.1.2.2.24 (a:ln): width in EMU, then fill, dash, join, head and tail ends in that order.
    private string ShapeLine(ShapeStyle style, bool defaultVisible = false)
    {
        StyleCascade g = style.Shape;
        string stroke = g.Graphic(OdfNamespaces.Draw, "stroke") ?? (defaultVisible ? "solid" : "none");
        if (stroke == "none")
        {
            return "<a:ln><a:noFill/></a:ln>";
        }

        long width = OdpUnits.Emu(g.Graphic(OdfNamespaces.Svg, "stroke-width"), 0);
        var sb = new StringBuilder("<a:ln");
        if (width > 0)
        {
            sb.Append(" w=\"").Append(width).Append('"');
        }

        sb.Append(g.Graphic(OdfNamespaces.Svg, "stroke-linecap") switch { "round" => " cap=\"rnd\"", "square" => " cap=\"sq\"", _ => "" }).Append('>');
        string? color = OdpUnits.Color(g.Graphic(OdfNamespaces.Svg, "stroke-color")) ?? "000000";
        double? alpha = OdpUnits.Percent(g.Graphic(OdfNamespaces.Svg, "stroke-opacity"));
        sb.Append("<a:solidFill>").Append(Srgb(color, alpha)).Append("</a:solidFill>");
        if (stroke == "dash")
        {
            sb.Append("<a:prstDash val=\"").Append(DashOf(g.Graphic(OdfNamespaces.Draw, "stroke-dash"))).Append("\"/>");
        }

        sb.Append(g.Graphic(OdfNamespaces.Svg, "stroke-linejoin") switch
        {
            "round" => "<a:round/>",
            "bevel" => "<a:bevel/>",
            "miter" => "<a:miter lim=\"800000\"/>",
            _ => "",
        });
        if (g.Graphic(OdfNamespaces.Draw, "marker-start") is not null)
        {
            sb.Append("<a:headEnd type=\"triangle\"/>");
        }

        if (g.Graphic(OdfNamespaces.Draw, "marker-end") is not null)
        {
            sb.Append("<a:tailEnd type=\"triangle\"/>");
        }

        sb.Append("</a:ln>");
        return sb.ToString();
    }

    // ECMA-376 Part 1 §20.1.10.48 (ST_PresetLineDashVal): the dash resource names a dot and a dash length; the preset nearest to its shape is used.
    private string DashOf(string? name)
    {
        MarkupNode d = name is null ? default : FindNamed("stroke-dash", name);
        if (d.IsNull)
        {
            return "dash";
        }

        bool dot = A(d, OdfNamespaces.Draw, "dots1") is not null && A(d, OdfNamespaces.Draw, "dots2") is null && A(d, OdfNamespaces.Draw, "dots1-length") is null;
        string? len = A(d, OdfNamespaces.Draw, "dots1-length");
        if (A(d, OdfNamespaces.Draw, "dots2") is not null)
        {
            return "dashDot";
        }

        return OdpUnits.Percent(len) is { } pct && pct <= 1.2 || dot ? "sysDot" : "dash";
    }

    private static string ShapeEffects(ShapeStyle style)
    {
        StyleCascade g = style.Shape;
        if (g.Graphic(OdfNamespaces.Draw, "shadow") != "visible")
        {
            return "";
        }

        long dx = OdpUnits.Emu(g.Graphic(OdfNamespaces.Draw, "shadow-offset-x")), dy = OdpUnits.Emu(g.Graphic(OdfNamespaces.Draw, "shadow-offset-y"));
        long dist = (long)Math.Round(Math.Sqrt((double)dx * dx + (double)dy * dy));
        long dir = (long)(((Math.Atan2(dy, dx) * 180 / Math.PI) % 360 + 360) % 360 * 60000);
        long blur = OdpUnits.Emu(g.Graphic(OdfNamespaces.LibreOfficeExtension, "shadow-blur"));
        string color = OdpUnits.Color(g.Graphic(OdfNamespaces.Draw, "shadow-color")) ?? "000000";
        double? opacity = OdpUnits.Percent(g.Graphic(OdfNamespaces.Draw, "shadow-opacity"));
        return "<a:effectLst><a:outerShdw blurRad=\"" + OdpUnits.I(blur) + "\" dist=\"" + OdpUnits.I(dist) + "\" dir=\"" + OdpUnits.I(dir) + "\" algn=\"tl\" rotWithShape=\"0\">" + Srgb(color, opacity ?? 1) + "</a:outerShdw></a:effectLst>";
    }
}
