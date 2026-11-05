using System.Globalization;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.Drawing;

/// <summary>A colour in the sRGB space with an alpha channel, each component from 0 to 1.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="A">Opacity, 1 for opaque.</param>
public readonly record struct DrawingColor(double R, double G, double B, double A = 1.0)
{
    /// <summary>Gets the colour as 0xRRGGBB, ignoring alpha.</summary>
    public uint Rgb => ((uint)Byte(R) << 16) | ((uint)Byte(G) << 8) | Byte(B);

    /// <summary>Makes an opaque colour from 0xRRGGBB.</summary>
    /// <param name="rgb">Red in bits 16 to 23, green 8 to 15, blue 0 to 7.</param>
    /// <returns>The colour.</returns>
    public static DrawingColor FromRgb(uint rgb) => new(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);

    private static byte Byte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);
}

/// <summary>
/// Maps the scheme colour names a document uses (<c>bg1</c>, <c>tx1</c>, <c>bg2</c>, <c>tx2</c>, <c>accent1</c> …) to theme colour slots
/// (<c>lt1</c>, <c>dk1</c>, <c>lt2</c>, <c>dk2</c>, <c>accent1</c> …). ECMA-376 Part 1 §20.1.6.1 (clrMap) and §17.15.1.20 (clrSchemeMapping).
/// </summary>
public sealed class ColorMap
{
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal)
    {
        ["bg1"] = "lt1",
        ["tx1"] = "dk1",
        ["bg2"] = "lt2",
        ["tx2"] = "dk2",
    };

    /// <summary>Gets the standard mapping: <c>bg1</c> is light 1, <c>tx1</c> dark 1, <c>bg2</c> light 2, <c>tx2</c> dark 2.</summary>
    public static ColorMap Default { get; } = new();

    /// <summary>Reads a <c>a:clrMap</c> or <c>w:clrSchemeMapping</c> element (the attributes <c>bg1</c>, <c>tx1</c>, <c>bg2</c>, <c>tx2</c>, <c>accent1</c> …).</summary>
    /// <param name="element">The mapping element.</param>
    /// <returns>The map; attributes that are absent keep the standard mapping.</returns>
    public static ColorMap Read(MarkupNode element)
    {
        var map = new ColorMap();
        foreach (MarkupAttributeRef a in element.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                continue;
            }

            string name = System.Text.Encoding.UTF8.GetString(a.LocalName);
            string value = a.Value;
            // Word spells the slots with the same names as DrawingML, except light1/dark1 for lt1/dk1 in some writers.
            map._map[name] = value switch { "light1" => "lt1", "dark1" => "dk1", "light2" => "lt2", "dark2" => "dk2", _ => value };
        }

        return map;
    }

    /// <summary>Finds the theme slot for a scheme colour name.</summary>
    /// <param name="schemeName">A value of <c>a:schemeClr</c>.</param>
    /// <returns>The slot name, which is the name itself when the map does not rename it.</returns>
    public string Slot(string schemeName) => _map.TryGetValue(schemeName, out string? s) ? s : schemeName;
}

/// <summary>
/// A theme (<c>a:theme</c>): its twelve colours, major and minor fonts and the format scheme's fill, line and effect style lists.
/// ECMA-376 Part 1 §20.1.6.9 (theme) and §20.1.4.1 (themeElements).
/// </summary>
public sealed class DrawingTheme
{
    private readonly Dictionary<string, DrawingColor> _colors = new(StringComparer.Ordinal);
    private readonly List<MarkupNode> _fills = [];
    private readonly List<MarkupNode> _lines = [];
    private readonly List<MarkupNode> _effects = [];
    private readonly List<MarkupNode> _backgrounds = [];

    private DrawingTheme()
    {
    }

    /// <summary>Gets the major (heading) Latin font.</summary>
    public string MajorFont { get; private set; } = "Calibri Light";

    /// <summary>Gets the minor (body) Latin font.</summary>
    public string MinorFont { get; private set; } = "Calibri";

    /// <summary>Gets the theme Office 2013 and later write by default, used when a document has no theme part.</summary>
    public static DrawingTheme Default { get; } = CreateDefault();

    internal IReadOnlyList<MarkupNode> FillStyles => _fills;

    internal IReadOnlyList<MarkupNode> LineStyles => _lines;

    internal IReadOnlyList<MarkupNode> EffectStyles => _effects;

    internal IReadOnlyList<MarkupNode> BackgroundFillStyles => _backgrounds;

    /// <summary>Reads a theme part.</summary>
    /// <param name="root">The <c>a:theme</c> element.</param>
    /// <returns>The theme. Missing colours fall back to the default theme's.</returns>
    public static DrawingTheme Read(MarkupNode root)
    {
        var theme = new DrawingTheme();
        foreach (KeyValuePair<string, DrawingColor> kv in Default._colors)
        {
            theme._colors[kv.Key] = kv.Value;
        }

        MarkupNode elements = Child(root, "themeElements");
        foreach (MarkupNode part in elements.ChildElements)
        {
            switch (part.LocalNameString)
            {
                case "clrScheme":
                    foreach (MarkupNode slot in part.ChildElements)
                    {
                        foreach (MarkupNode c in slot.ChildElements)
                        {
                            if (ColorResolver.ResolveLiteral(c) is { } color)
                            {
                                theme._colors[slot.LocalNameString] = color;
                            }

                            break;
                        }
                    }

                    break;
                case "fontScheme":
                    if (Child(Child(part, "majorFont"), "latin").GetAttribute("typeface") is { Length: > 0 } maj)
                    {
                        theme.MajorFont = maj;
                    }

                    if (Child(Child(part, "minorFont"), "latin").GetAttribute("typeface") is { Length: > 0 } min)
                    {
                        theme.MinorFont = min;
                    }

                    break;
                case "fmtScheme":
                    foreach (MarkupNode list in part.ChildElements)
                    {
                        List<MarkupNode>? target = list.LocalNameString switch
                        {
                            "fillStyleLst" => theme._fills,
                            "lnStyleLst" => theme._lines,
                            "effectStyleLst" => theme._effects,
                            "bgFillStyleLst" => theme._backgrounds,
                            _ => null,
                        };
                        if (target is not null)
                        {
                            foreach (MarkupNode style in list.ChildElements)
                            {
                                target.Add(style);
                            }
                        }
                    }

                    break;
            }
        }

        return theme;
    }

    /// <summary>Looks a theme colour up by slot name.</summary>
    /// <param name="slot">A slot such as <c>accent1</c>, <c>dk1</c>, <c>lt2</c>, <c>hlink</c>.</param>
    /// <returns>The colour, or null for an unknown slot.</returns>
    public DrawingColor? Color(string slot) => _colors.TryGetValue(slot, out DrawingColor c) ? c : null;

    /// <summary>Resolves a font reference such as <c>+mj-lt</c> or <c>+mn-lt</c> to a family name.</summary>
    /// <param name="typeface">The typeface attribute.</param>
    /// <returns>The family; the input itself when it is not a theme reference.</returns>
    public string ResolveFont(string typeface) => typeface switch
    {
        "+mj-lt" => MajorFont,
        "+mn-lt" => MinorFont,
        _ => typeface,
    };

    internal static MarkupNode Child(MarkupNode parent, string local)
    {
        if (parent.IsNull)
        {
            return default;
        }

        foreach (MarkupNode n in parent.ChildElements)
        {
            if (n.LocalNameString == local)
            {
                return n;
            }
        }

        return default;
    }

    private static DrawingTheme CreateDefault()
    {
        var t = new DrawingTheme();
        void Set(string slot, uint rgb) => t._colors[slot] = DrawingColor.FromRgb(rgb);

        // The Office 2013-2022 colour scheme, used only when a document carries no theme.
        Set("dk1", 0x000000);
        Set("lt1", 0xFFFFFF);
        Set("dk2", 0x44546A);
        Set("lt2", 0xE7E6E6);
        Set("accent1", 0x4472C4);
        Set("accent2", 0xED7D31);
        Set("accent3", 0xA5A5A5);
        Set("accent4", 0xFFC000);
        Set("accent5", 0x5B9BD5);
        Set("accent6", 0x70AD47);
        Set("hlink", 0x0563C1);
        Set("folHlink", 0x954F72);
        return t;
    }
}

/// <summary>
/// Resolves DrawingML colour elements (<c>a:srgbClr</c>, <c>a:schemeClr</c>, <c>a:sysClr</c>, <c>a:hslClr</c>, <c>a:scrgbClr</c>,
/// <c>a:prstClr</c>) with all their transforms to sRGB. ECMA-376 Part 1 §20.1.2.3.
/// </summary>
public sealed class ColorResolver
{
    private readonly DrawingTheme _theme;
    private readonly ColorMap _map;

    /// <summary>Creates a resolver.</summary>
    /// <param name="theme">The theme scheme colours come from; null for <see cref="DrawingTheme.Default"/>.</param>
    /// <param name="map">The colour map; null for the standard one.</param>
    public ColorResolver(DrawingTheme? theme = null, ColorMap? map = null)
    {
        _theme = theme ?? DrawingTheme.Default;
        _map = map ?? ColorMap.Default;
    }

    /// <summary>Gets the theme.</summary>
    public DrawingTheme Theme => _theme;

    /// <summary>Resolves a colour element.</summary>
    /// <param name="element">A colour element such as <c>a:schemeClr</c>, with its transform children.</param>
    /// <param name="placeholder">The colour <c>phClr</c> stands for in a theme style, or null (then black).</param>
    /// <returns>The colour, or null when the element is not a colour or names an unknown colour.</returns>
    public DrawingColor? Resolve(MarkupNode element, DrawingColor? placeholder = null)
    {
        DrawingColor? basis = ResolveBasis(element, placeholder, _theme, _map);
        return basis is { } b ? ApplyTransforms(b, element) : null;
    }

    /// <summary>Resolves the first colour child of an element such as <c>a:solidFill</c> or <c>a:gs</c>.</summary>
    /// <param name="parent">The parent element.</param>
    /// <param name="placeholder">The colour <c>phClr</c> stands for.</param>
    /// <returns>The colour, or null when there is no colour child.</returns>
    public DrawingColor? ResolveChild(MarkupNode parent, DrawingColor? placeholder = null)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (Resolve(c, placeholder) is { } r)
            {
                return r;
            }
        }

        return null;
    }

    // A colour element without scheme lookups: used for the theme's own colour slots.
    internal static DrawingColor? ResolveLiteral(MarkupNode element)
    {
        DrawingColor? basis = ResolveBasis(element, null, null, null);
        return basis is { } b ? ApplyTransforms(b, element) : null;
    }

    private static DrawingColor? ResolveBasis(MarkupNode e, DrawingColor? placeholder, DrawingTheme? theme, ColorMap? map)
    {
        switch (e.LocalNameString)
        {
            case "srgbClr":
                return uint.TryParse(e.GetAttribute("val"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb) ? DrawingColor.FromRgb(rgb) : new DrawingColor(0, 0, 0);
            case "scrgbClr":
                return new DrawingColor(
                    FromLinear(Percent(e.GetAttribute("r"))), FromLinear(Percent(e.GetAttribute("g"))), FromLinear(Percent(e.GetAttribute("b"))));
            case "hslClr":
                {
                    double h = (Number(e.GetAttribute("hue")) / 60000.0) % 360.0;
                    (double r, double g, double b) = HslToRgb(h < 0 ? h + 360 : h, Percent(e.GetAttribute("sat")), Percent(e.GetAttribute("lum")));
                    return new DrawingColor(r, g, b);
                }

            case "sysClr":
                if (uint.TryParse(e.GetAttribute("lastClr"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint last))
                {
                    return DrawingColor.FromRgb(last);
                }

                return DrawingColor.FromRgb(e.GetAttribute("val") switch { "window" or "menu" or "highlightText" => 0xFFFFFFu, "btnFace" or "3dFace" => 0xF0F0F0u, "highlight" => 0x3399FFu, "grayText" => 0x6D6D6Du, _ => 0u });
            case "prstClr":
                return PresetColors.Find(e.GetAttribute("val") ?? string.Empty) is { } p ? DrawingColor.FromRgb(p) : null;
            case "schemeClr":
                {
                    string name = e.GetAttribute("val") ?? "tx1";
                    if (name == "phClr")
                    {
                        return placeholder ?? new DrawingColor(0, 0, 0);
                    }

                    if (theme is null || map is null)
                    {
                        return null;
                    }

                    return theme.Color(map.Slot(name)) ?? theme.Color(name);
                }

            default:
                return null;
        }
    }

    // ECMA-376 Part 1 §20.1.2.3: the transforms apply in document order. Lightness, saturation and hue work in HSL; tint and shade mix
    // with white and black in linear light (the way Office renders them), alpha is a plain multiplier.
    private static DrawingColor ApplyTransforms(DrawingColor c, MarkupNode element)
    {
        foreach (MarkupNode t in element.ChildElements)
        {
            double v = Percent(t.GetAttribute("val"));
            double raw = Number(t.GetAttribute("val"));
            switch (t.LocalNameString)
            {
                case "tint":
                    c = Linear(c, x => (x * v) + (1 - v));
                    break;
                case "shade":
                    c = Linear(c, x => x * v);
                    break;
                case "comp":
                    {
                        (double h, double s, double l) = RgbToHsl(c);
                        (double r, double g, double b) = HslToRgb((h + 180) % 360, s, l);
                        c = c with { R = r, G = g, B = b };
                        break;
                    }

                case "inv":
                    c = c with { R = 1 - c.R, G = 1 - c.G, B = 1 - c.B };
                    break;
                case "gray":
                    {
                        double y = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
                        c = c with { R = y, G = y, B = y };
                        break;
                    }

                case "alpha":
                    c = c with { A = Clamp(v) };
                    break;
                case "alphaOff":
                    c = c with { A = Clamp(c.A + v) };
                    break;
                case "alphaMod":
                    c = c with { A = Clamp(c.A * v) };
                    break;
                case "hue":
                    c = Hsl(c, (h, s, l) => (raw / 60000.0, s, l));
                    break;
                case "hueOff":
                    c = Hsl(c, (h, s, l) => (h + (raw / 60000.0), s, l));
                    break;
                case "hueMod":
                    c = Hsl(c, (h, s, l) => (h * v, s, l));
                    break;
                case "sat":
                    c = Hsl(c, (h, s, l) => (h, v, l));
                    break;
                case "satOff":
                    c = Hsl(c, (h, s, l) => (h, s + v, l));
                    break;
                case "satMod":
                    c = Hsl(c, (h, s, l) => (h, s * v, l));
                    break;
                case "lum":
                    c = Hsl(c, (h, s, l) => (h, s, v));
                    break;
                case "lumOff":
                    c = Hsl(c, (h, s, l) => (h, s, l + v));
                    break;
                case "lumMod":
                    c = Hsl(c, (h, s, l) => (h, s, l * v));
                    break;
                case "red":
                    c = c with { R = Clamp(v) };
                    break;
                case "redOff":
                    c = c with { R = Clamp(c.R + v) };
                    break;
                case "redMod":
                    c = c with { R = Clamp(c.R * v) };
                    break;
                case "green":
                    c = c with { G = Clamp(v) };
                    break;
                case "greenOff":
                    c = c with { G = Clamp(c.G + v) };
                    break;
                case "greenMod":
                    c = c with { G = Clamp(c.G * v) };
                    break;
                case "blue":
                    c = c with { B = Clamp(v) };
                    break;
                case "blueOff":
                    c = c with { B = Clamp(c.B + v) };
                    break;
                case "blueMod":
                    c = c with { B = Clamp(c.B * v) };
                    break;
                case "gamma":
                    c = c with { R = FromLinear(c.R), G = FromLinear(c.G), B = FromLinear(c.B) };
                    break;
                case "invGamma":
                    c = c with { R = ToLinear(c.R), G = ToLinear(c.G), B = ToLinear(c.B) };
                    break;
            }
        }

        return c;
    }

    private static DrawingColor Linear(DrawingColor c, Func<double, double> f) =>
        c with { R = FromLinear(Clamp(f(ToLinear(c.R)))), G = FromLinear(Clamp(f(ToLinear(c.G)))), B = FromLinear(Clamp(f(ToLinear(c.B)))) };

    private static DrawingColor Hsl(DrawingColor c, Func<double, double, double, (double H, double S, double L)> f)
    {
        (double h, double s, double l) = RgbToHsl(c);
        (double h2, double s2, double l2) = f(h, s, l);
        (double r, double g, double b) = HslToRgb(((h2 % 360.0) + 360.0) % 360.0, Clamp(s2), Clamp(l2));
        return c with { R = r, G = g, B = b };
    }

    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static double Number(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    // ECMA-376 Part 1 §20.1.10.40 (ST_Percentage): 1000ths of a percent in Transitional files; Strict files write "50%".
    private static double Percent(string? s)
    {
        if (s is null)
        {
            return 0;
        }

        if (s.EndsWith('%'))
        {
            return Number(s[..^1]) / 100.0;
        }

        return Number(s) / 100000.0;
    }

    // sRGB transfer function (IEC 61966-2-1).
    internal static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    internal static double FromLinear(double c) => c <= 0.0031308 ? c * 12.92 : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055;

    internal static (double H, double S, double L) RgbToHsl(DrawingColor c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B));
        double min = Math.Min(c.R, Math.Min(c.G, c.B));
        double l = (max + min) / 2;
        double d = max - min;
        if (d < 1e-12)
        {
            return (0, 0, l);
        }

        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == c.R ? ((c.G - c.B) / d) + (c.G < c.B ? 6 : 0) : max == c.G ? ((c.B - c.R) / d) + 2 : ((c.R - c.G) / d) + 4;
        return (h * 60, s, l);
    }

    internal static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        if (s <= 0)
        {
            return (l, l, l);
        }

        double q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        double p = (2 * l) - q;
        double hk = h / 360.0;
        return (HueToRgb(p, q, hk + (1.0 / 3)), HueToRgb(p, q, hk), HueToRgb(p, q, hk - (1.0 / 3)));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + ((q - p) * 6 * t);
        }

        if (t < 0.5)
        {
            return q;
        }

        if (t < 2.0 / 3)
        {
            return p + ((q - p) * ((2.0 / 3) - t) * 6);
        }

        return p;
    }
}

// ECMA-376 Part 1 §20.1.10.47 (ST_PresetColorVal): the named colours, which are the CSS colour keywords under Office's spellings.
internal static class PresetColors
{
    private static readonly Dictionary<string, uint> Table = Build();

    public static uint? Find(string name) => Table.TryGetValue(name, out uint v) ? v : null;

    private static Dictionary<string, uint> Build()
    {
        const string data =
            "aliceBlue F0F8FF,antiqueWhite FAEBD7,aqua 00FFFF,aquamarine 7FFFD4,azure F0FFFF,beige F5F5DC,bisque FFE4C4,black 000000,blanchedAlmond FFEBCD,blue 0000FF," +
            "blueViolet 8A2BE2,brown A52A2A,burlyWood DEB887,cadetBlue 5F9EA0,chartreuse 7FFF00,chocolate D2691E,coral FF7F50,cornflowerBlue 6495ED,cornsilk FFF8DC,crimson DC143C," +
            "cyan 00FFFF,deepPink FF1493,deepSkyBlue 00BFFF,dimGray 696969,dkBlue 00008B,dkCyan 008B8B,dkGoldenrod B8860B,dkGray A9A9A9,dkGreen 006400,dkKhaki BDB76B," +
            "dkMagenta 8B008B,dkOliveGreen 556B2F,dkOrange FF8C00,dkOrchid 9932CC,dkRed 8B0000,dkSalmon E9967A,dkSeaGreen 8FBC8B,dkSlateBlue 483D8B,dkSlateGray 2F4F4F,dkTurquoise 00CED1," +
            "dkViolet 9400D3,dodgerBlue 1E90FF,firebrick B22222,floralWhite FFFAF0,forestGreen 228B22,fuchsia FF00FF,gainsboro DCDCDC,ghostWhite F8F8FF,gold FFD700,goldenrod DAA520," +
            "gray 808080,green 008000,greenYellow ADFF2F,honeydew F0FFF0,hotPink FF69B4,indianRed CD5C5C,indigo 4B0082,ivory FFFFF0,khaki F0E68C,lavender E6E6FA," +
            "lavenderBlush FFF0F5,lawnGreen 7CFC00,lemonChiffon FFFACD,lime 00FF00,limeGreen 32CD32,linen FAF0E6,ltBlue ADD8E6,ltCoral F08080,ltCyan E0FFFF,ltGoldenrodYellow FAFAD2," +
            "ltGray D3D3D3,ltGreen 90EE90,ltPink FFB6C1,ltSalmon FFA07A,ltSeaGreen 20B2AA,ltSkyBlue 87CEFA,ltSlateGray 778899,ltSteelBlue B0C4DE,ltYellow FFFFE0,magenta FF00FF," +
            "maroon 800000,medAquamarine 66CDAA,medBlue 0000CD,medOrchid BA55D3,medPurple 9370DB,medSeaGreen 3CB371,medSlateBlue 7B68EE,medSpringGreen 00FA9A,medTurquoise 48D1CC,medVioletRed C71585," +
            "midnightBlue 191970,mintCream F5FFFA,mistyRose FFE4E1,moccasin FFE4B5,navajoWhite FFDEAD,navy 000080,oldLace FDF5E6,olive 808000,oliveDrab 6B8E23,orange FFA500," +
            "orangeRed FF4500,orchid DA70D6,paleGoldenrod EEE8AA,paleGreen 98FB98,paleTurquoise AFEEEE,paleVioletRed DB7093,papayaWhip FFEFD5,peachPuff FFDAB9,peru CD853F,pink FFC0CB," +
            "plum DDA0DD,powderBlue B0E0E6,purple 800080,red FF0000,rosyBrown BC8F8F,royalBlue 4169E1,saddleBrown 8B4513,salmon FA8072,sandyBrown F4A460,seaGreen 2E8B57," +
            "seaShell FFF5EE,sienna A0522D,silver C0C0C0,skyBlue 87CEEB,slateBlue 6A5ACD,slateGray 708090,snow FFFAFA,springGreen 00FF7F,steelBlue 4682B4,tan D2B48C," +
            "teal 008080,thistle D8BFD8,tomato FF6347,turquoise 40E0D0,violet EE82EE,wheat F5DEB3,white FFFFFF,whiteSmoke F5F5F5,yellow FFFF00,yellowGreen 9ACD32";
        var d = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (string item in data.Split(','))
        {
            int sp = item.IndexOf(' ');
            d[item[..sp]] = uint.Parse(item[(sp + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return d;
    }
}
