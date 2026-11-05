using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>How a colour is specified (ECMA-376 Part 1 §18.8.3, CT_Color).</summary>
public enum ColorKind : byte
{
    /// <summary>No colour element.</summary>
    None = 0,

    /// <summary>The automatic colour (<c>auto="1"</c>): the application's default for the property.</summary>
    Auto,

    /// <summary>An explicit ARGB colour.</summary>
    Rgb,

    /// <summary>An index into the indexed palette.</summary>
    Indexed,

    /// <summary>An index into the theme's colour scheme, optionally tinted.</summary>
    Theme,
}

/// <summary>A colour as a style stores it. Resolve it to ARGB with <see cref="StyleSheet.ResolveColor"/>.</summary>
/// <param name="Kind">How the colour is specified.</param>
/// <param name="Value">The ARGB value (<see cref="ColorKind.Rgb"/>), the palette index (<see cref="ColorKind.Indexed"/>) or the theme colour index (<see cref="ColorKind.Theme"/>).</param>
/// <param name="Tint">For a theme or indexed colour, the tint from -1 (black) to 1 (white); 0 leaves the colour as it is.</param>
public readonly record struct StyleColor(ColorKind Kind, int Value = 0, double Tint = 0)
{
    /// <summary>Gets the automatic colour.</summary>
    public static StyleColor Auto => new(ColorKind.Auto);

    /// <summary>Makes an opaque colour from 0xRRGGBB.</summary>
    /// <param name="rgb">The red, green and blue bytes.</param>
    /// <returns>The colour.</returns>
    public static StyleColor FromRgb(int rgb) => new(ColorKind.Rgb, unchecked((int)0xFF000000u) | (rgb & 0xFFFFFF));

    /// <summary>Makes a colour from an ARGB value.</summary>
    /// <param name="argb">The alpha, red, green and blue bytes.</param>
    /// <returns>The colour.</returns>
    public static StyleColor FromArgb(int argb) => new(ColorKind.Rgb, argb);

    /// <summary>Makes a theme colour (0 = light 1, 1 = dark 1, 2 = light 2, 3 = dark 2, 4–9 = accents 1–6, 10 = hyperlink, 11 = followed hyperlink).</summary>
    /// <param name="index">The theme colour index.</param>
    /// <param name="tint">The tint.</param>
    /// <returns>The colour.</returns>
    public static StyleColor FromTheme(int index, double tint = 0) => new(ColorKind.Theme, index, tint);

    /// <summary>Makes a palette colour.</summary>
    /// <param name="index">The index into the indexed palette.</param>
    /// <returns>The colour.</returns>
    public static StyleColor FromIndexed(int index) => new(ColorKind.Indexed, index);

    internal static StyleColor Read(MarkupNode e)
    {
        if (e.IsNull)
        {
            return default;
        }

        double tint = double.TryParse(e.GetAttribute("tint"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0;
        if (e.GetAttribute("rgb") is { } rgb)
        {
            int v = 0;
            string h = rgb.Length == 6 ? "FF" + rgb : rgb;
            if (h.Length == 8 && int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
            {
                return new StyleColor(ColorKind.Rgb, v, tint);
            }
        }

        if (e.GetAttribute("theme") is { } th && int.TryParse(th, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ti))
        {
            return new StyleColor(ColorKind.Theme, ti, tint);
        }

        if (e.GetAttribute("indexed") is { } ix && int.TryParse(ix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ii))
        {
            return new StyleColor(ColorKind.Indexed, ii, tint);
        }

        return e.GetAttribute("auto") is "1" or "true" ? Auto : default;
    }

    internal string ToAttributes()
    {
        string a = Kind switch
        {
            ColorKind.Rgb => "rgb=\"" + ((uint)Value).ToString("X8", CultureInfo.InvariantCulture) + "\"",
            ColorKind.Indexed => "indexed=\"" + Value.ToString(CultureInfo.InvariantCulture) + "\"",
            ColorKind.Theme => "theme=\"" + Value.ToString(CultureInfo.InvariantCulture) + "\"",
            ColorKind.Auto => "auto=\"1\"",
            _ => string.Empty,
        };
        return Tint != 0 && Kind is ColorKind.Rgb or ColorKind.Indexed or ColorKind.Theme ? a + " tint=\"" + Tint.ToString("R", CultureInfo.InvariantCulture) + "\"" : a;
    }
}

/// <summary>The underline of a font (CT_UnderlineProperty, §18.4.13).</summary>
public enum FontUnderline : byte
{
    /// <summary>None.</summary>
    None,

    /// <summary>A single underline.</summary>
    Straight,

    /// <summary>A double underline.</summary>
    Doubled,

    /// <summary>A single accounting underline.</summary>
    SingleAccounting,

    /// <summary>A double accounting underline.</summary>
    DoubleAccounting,
}

/// <summary>Super- and subscript (ST_VerticalAlignRun).</summary>
public enum FontVerticalAlignment : byte
{
    /// <summary>On the baseline.</summary>
    Baseline,

    /// <summary>Raised.</summary>
    Superscript,

    /// <summary>Lowered.</summary>
    Subscript,
}

/// <summary>A font (CT_Font, ECMA-376 Part 1 §18.8.22). Properties the model does not know stay in the part when a font is read and written back unchanged.</summary>
/// <param name="Name">The typeface name; null when the font has none (as in a differential format).</param>
/// <param name="Size">The size in points; 0 when unspecified.</param>
/// <param name="Bold">Bold.</param>
/// <param name="Italic">Italic.</param>
/// <param name="Strike">Strikethrough.</param>
/// <param name="Underline">The underline.</param>
/// <param name="VerticalAlignment">Super- or subscript.</param>
/// <param name="Color">The colour.</param>
/// <param name="Family">The font family (<c>family</c>), 0 when unspecified.</param>
/// <param name="Charset">The character set (<c>charset</c>), -1 when unspecified.</param>
/// <param name="Scheme">The theme font scheme (<c>major</c>, <c>minor</c>), or null.</param>
public sealed record CellFont(
    string? Name = null,
    double Size = 0,
    bool Bold = false,
    bool Italic = false,
    bool Strike = false,
    FontUnderline Underline = FontUnderline.None,
    FontVerticalAlignment VerticalAlignment = FontVerticalAlignment.Baseline,
    StyleColor Color = default,
    int Family = 0,
    int Charset = -1,
    string? Scheme = null)
{
    internal static CellFont Read(MarkupNode font)
    {
        string? name = null;
        double size = 0;
        bool b = false, i = false, strike = false;
        var u = FontUnderline.None;
        var va = FontVerticalAlignment.Baseline;
        StyleColor color = default;
        int family = 0, charset = -1;
        string? scheme = null;
        foreach (MarkupNode c in font.ChildElements)
        {
            string val = c.GetAttribute("val") ?? string.Empty;
            switch (c.LocalNameString)
            {
                case "name": name = val; break;
                case "sz": _ = double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out size); break;
                case "b": b = Xl.Flag(c); break;
                case "i": i = Xl.Flag(c); break;
                case "strike": strike = Xl.Flag(c); break;
                case "u":
                    u = val switch { "double" => FontUnderline.Doubled, "singleAccounting" => FontUnderline.SingleAccounting, "doubleAccounting" => FontUnderline.DoubleAccounting, "none" => FontUnderline.None, _ => FontUnderline.Straight };
                    break;
                case "vertAlign": va = val switch { "superscript" => FontVerticalAlignment.Superscript, "subscript" => FontVerticalAlignment.Subscript, _ => FontVerticalAlignment.Baseline }; break;
                case "color": color = StyleColor.Read(c); break;
                case "family": _ = int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out family); break;
                case "charset": _ = int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out charset); break;
                case "scheme": scheme = val; break;
            }
        }

        return new CellFont(name, size, b, i, strike, u, va, color, family, charset, scheme);
    }

    // ECMA-376 Part 1 §18.8.22: Excel writes b, i, strike, u, vertAlign, sz, color, name, family, charset, scheme in this order.
    internal string ToXml(string pc, string tag = "font")
    {
        var sb = new StringBuilder("<").Append(pc).Append(tag).Append('>');
        if (Bold) sb.Append('<').Append(pc).Append("b/>");
        if (Italic) sb.Append('<').Append(pc).Append("i/>");
        if (Strike) sb.Append('<').Append(pc).Append("strike/>");
        if (Underline != FontUnderline.None)
        {
            sb.Append('<').Append(pc).Append('u');
            if (Underline != FontUnderline.Straight)
            {
                sb.Append(" val=\"").Append(Underline switch { FontUnderline.Doubled => "double", FontUnderline.SingleAccounting => "singleAccounting", _ => "doubleAccounting" }).Append('"');
            }

            sb.Append("/>");
        }

        if (VerticalAlignment != FontVerticalAlignment.Baseline)
        {
            sb.Append('<').Append(pc).Append("vertAlign val=\"").Append(VerticalAlignment == FontVerticalAlignment.Superscript ? "superscript" : "subscript").Append("\"/>");
        }

        if (Size > 0) sb.Append('<').Append(pc).Append("sz val=\"").Append(Size.ToString("R", CultureInfo.InvariantCulture)).Append("\"/>");
        if (Color.Kind != ColorKind.None) sb.Append('<').Append(pc).Append("color ").Append(Color.ToAttributes()).Append("/>");
        if (Name is not null) sb.Append('<').Append(pc).Append("name val=\"").Append(Xl.Esc(Name)).Append("\"/>");
        if (Family != 0) sb.Append('<').Append(pc).Append("family val=\"").Append(Family.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
        if (Charset >= 0) sb.Append('<').Append(pc).Append("charset val=\"").Append(Charset.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
        if (Scheme is not null) sb.Append('<').Append(pc).Append("scheme val=\"").Append(Xl.Esc(Scheme)).Append("\"/>");
        return sb.Append("</").Append(pc).Append(tag).Append('>').ToString();
    }
}

/// <summary>A pattern fill's pattern (ST_PatternType, §18.18.55).</summary>
public enum FillPattern : byte
{
    /// <summary>No fill.</summary>
    None,

    /// <summary>A solid fill in the foreground colour.</summary>
    Solid,

    /// <summary>50% gray.</summary>
    MediumGray,

    /// <summary>75% gray.</summary>
    DarkGray,

    /// <summary>25% gray.</summary>
    LightGray,

    /// <summary>Dark horizontal lines.</summary>
    DarkHorizontal,

    /// <summary>Dark vertical lines.</summary>
    DarkVertical,

    /// <summary>Dark downward diagonal lines.</summary>
    DarkDown,

    /// <summary>Dark upward diagonal lines.</summary>
    DarkUp,

    /// <summary>Dark grid.</summary>
    DarkGrid,

    /// <summary>Dark trellis.</summary>
    DarkTrellis,

    /// <summary>Light horizontal lines.</summary>
    LightHorizontal,

    /// <summary>Light vertical lines.</summary>
    LightVertical,

    /// <summary>Light downward diagonal lines.</summary>
    LightDown,

    /// <summary>Light upward diagonal lines.</summary>
    LightUp,

    /// <summary>Light grid.</summary>
    LightGrid,

    /// <summary>Light trellis.</summary>
    LightTrellis,

    /// <summary>12.5% gray.</summary>
    Gray125,

    /// <summary>6.25% gray.</summary>
    Gray0625,
}

/// <summary>One stop of a gradient fill (CT_GradientStop).</summary>
/// <param name="Position">The position from 0 to 1.</param>
/// <param name="Color">The colour at the stop.</param>
public readonly record struct GradientStop(double Position, StyleColor Color);

/// <summary>A gradient fill (CT_GradientFill, §18.8.24).</summary>
/// <param name="IsPath">True for a path gradient, false for a linear one.</param>
/// <param name="Degree">The angle of a linear gradient in degrees.</param>
/// <param name="Left">For a path gradient, the left edge of the focus rectangle (0–1).</param>
/// <param name="Right">The right edge of the focus rectangle.</param>
/// <param name="Top">The top edge of the focus rectangle.</param>
/// <param name="Bottom">The bottom edge of the focus rectangle.</param>
/// <param name="Stops">The colour stops.</param>
public sealed record GradientFill(bool IsPath, double Degree, double Left, double Right, double Top, double Bottom, IReadOnlyList<GradientStop> Stops);

/// <summary>A fill (CT_Fill, §18.8.20): a pattern with its colours, or a gradient.</summary>
/// <param name="Pattern">The pattern; ignored when <paramref name="Gradient"/> is set.</param>
/// <param name="Foreground">The pattern's foreground colour (the colour of a solid fill).</param>
/// <param name="Background">The pattern's background colour.</param>
/// <param name="Gradient">The gradient, or null for a pattern fill.</param>
public sealed record CellFill(FillPattern Pattern = FillPattern.None, StyleColor Foreground = default, StyleColor Background = default, GradientFill? Gradient = null)
{
    private static readonly string[] PatternNames =
    [
        "none", "solid", "mediumGray", "darkGray", "lightGray", "darkHorizontal", "darkVertical", "darkDown", "darkUp", "darkGrid", "darkTrellis",
        "lightHorizontal", "lightVertical", "lightDown", "lightUp", "lightGrid", "lightTrellis", "gray125", "gray0625",
    ];

    /// <summary>Makes a solid fill.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The fill.</returns>
    public static CellFill Solid(StyleColor color) => new(FillPattern.Solid, color, StyleColor.Auto);

    internal static CellFill Read(MarkupNode fill)
    {
        foreach (MarkupNode c in fill.ChildElements)
        {
            if (c.LocalName.SequenceEqual("patternFill"u8))
            {
                int p = Array.IndexOf(PatternNames, c.GetAttribute("patternType") ?? "none");
                StyleColor fg = default, bg = default;
                foreach (MarkupNode k in c.ChildElements)
                {
                    if (k.LocalName.SequenceEqual("fgColor"u8)) fg = StyleColor.Read(k);
                    else if (k.LocalName.SequenceEqual("bgColor"u8)) bg = StyleColor.Read(k);
                }

                return new CellFill(p < 0 ? FillPattern.None : (FillPattern)p, fg, bg);
            }

            if (c.LocalName.SequenceEqual("gradientFill"u8))
            {
                var stops = new List<GradientStop>();
                foreach (MarkupNode s in c.ChildElements)
                {
                    if (s.LocalName.SequenceEqual("stop"u8))
                    {
                        StyleColor col = default;
                        foreach (MarkupNode k in s.ChildElements)
                        {
                            if (k.LocalName.SequenceEqual("color"u8)) col = StyleColor.Read(k);
                        }

                        stops.Add(new GradientStop(Xl.Double(s.GetAttribute("position")), col));
                    }
                }

                return new CellFill(FillPattern.None, default, default,
                    new GradientFill(c.GetAttribute("type") == "path", Xl.Double(c.GetAttribute("degree")), Xl.Double(c.GetAttribute("left")), Xl.Double(c.GetAttribute("right")),
                        Xl.Double(c.GetAttribute("top")), Xl.Double(c.GetAttribute("bottom")), stops));
            }
        }

        return new CellFill();
    }

    internal string ToXml(string pc)
    {
        var sb = new StringBuilder("<").Append(pc).Append("fill>");
        if (Gradient is { } g)
        {
            sb.Append('<').Append(pc).Append("gradientFill");
            if (g.IsPath) sb.Append(" type=\"path\"");
            if (g.Degree != 0) sb.Append(" degree=\"").Append(Xl.Num(g.Degree)).Append('"');
            if (g.Left != 0) sb.Append(" left=\"").Append(Xl.Num(g.Left)).Append('"');
            if (g.Right != 0) sb.Append(" right=\"").Append(Xl.Num(g.Right)).Append('"');
            if (g.Top != 0) sb.Append(" top=\"").Append(Xl.Num(g.Top)).Append('"');
            if (g.Bottom != 0) sb.Append(" bottom=\"").Append(Xl.Num(g.Bottom)).Append('"');
            sb.Append('>');
            foreach (GradientStop s in g.Stops)
            {
                sb.Append('<').Append(pc).Append("stop position=\"").Append(Xl.Num(s.Position)).Append("\"><").Append(pc).Append("color ").Append(s.Color.ToAttributes()).Append("/></").Append(pc).Append("stop>");
            }

            sb.Append("</").Append(pc).Append("gradientFill>");
        }
        else
        {
            sb.Append('<').Append(pc).Append("patternFill patternType=\"").Append(PatternNames[(int)Pattern]).Append('"');
            if (Foreground.Kind == ColorKind.None && Background.Kind == ColorKind.None)
            {
                sb.Append("/>");
            }
            else
            {
                sb.Append('>');
                if (Foreground.Kind != ColorKind.None) sb.Append('<').Append(pc).Append("fgColor ").Append(Foreground.ToAttributes()).Append("/>");
                if (Background.Kind != ColorKind.None) sb.Append('<').Append(pc).Append("bgColor ").Append(Background.ToAttributes()).Append("/>");
                sb.Append("</").Append(pc).Append("patternFill>");
            }
        }

        return sb.Append("</").Append(pc).Append("fill>").ToString();
    }
}

/// <summary>A border line style (ST_BorderStyle, §18.18.3).</summary>
public enum BorderLineStyle : byte
{
    /// <summary>No line.</summary>
    None,

    /// <summary>Thin.</summary>
    Thin,

    /// <summary>Medium.</summary>
    Medium,

    /// <summary>Dashed.</summary>
    Dashed,

    /// <summary>Dotted.</summary>
    Dotted,

    /// <summary>Thick.</summary>
    Thick,

    /// <summary>Double.</summary>
    Doubled,

    /// <summary>Hairline.</summary>
    Hair,

    /// <summary>Medium dashed.</summary>
    MediumDashed,

    /// <summary>Dash-dot.</summary>
    DashDot,

    /// <summary>Medium dash-dot.</summary>
    MediumDashDot,

    /// <summary>Dash-dot-dot.</summary>
    DashDotDot,

    /// <summary>Medium dash-dot-dot.</summary>
    MediumDashDotDot,

    /// <summary>Slanted dash-dot.</summary>
    SlantDashDot,
}

/// <summary>One side of a border.</summary>
/// <param name="Style">The line style.</param>
/// <param name="Color">The line colour.</param>
public readonly record struct BorderSide(BorderLineStyle Style = BorderLineStyle.None, StyleColor Color = default);

/// <summary>A border (CT_Border, §18.8.4).</summary>
/// <param name="Left">The left (or start) side.</param>
/// <param name="Right">The right (or end) side.</param>
/// <param name="Top">The top side.</param>
/// <param name="Bottom">The bottom side.</param>
/// <param name="Diagonal">The diagonal line.</param>
/// <param name="DiagonalUp">Draw the diagonal from bottom left to top right.</param>
/// <param name="DiagonalDown">Draw the diagonal from top left to bottom right.</param>
public sealed record CellBorder(BorderSide Left = default, BorderSide Right = default, BorderSide Top = default, BorderSide Bottom = default, BorderSide Diagonal = default, bool DiagonalUp = false, bool DiagonalDown = false)
{
    private static readonly string[] StyleNames =
    [
        "none", "thin", "medium", "dashed", "dotted", "thick", "double", "hair", "mediumDashed", "dashDot", "mediumDashDot", "dashDotDot", "mediumDashDotDot", "slantDashDot",
    ];

    /// <summary>Makes a border with the same line on all four sides.</summary>
    /// <param name="style">The line style.</param>
    /// <param name="color">The colour.</param>
    /// <returns>The border.</returns>
    public static CellBorder Box(BorderLineStyle style, StyleColor color = default) { var s = new BorderSide(style, color); return new CellBorder(s, s, s, s); }

    internal static CellBorder Read(MarkupNode border)
    {
        BorderSide l = default, r = default, t = default, b = default, d = default;
        foreach (MarkupNode c in border.ChildElements)
        {
            switch (c.LocalNameString)
            {
                case "left" or "start": l = Side(c); break;
                case "right" or "end": r = Side(c); break;
                case "top": t = Side(c); break;
                case "bottom": b = Side(c); break;
                case "diagonal": d = Side(c); break;
            }
        }

        return new CellBorder(l, r, t, b, d, Xl.FlagAttr(border, "diagonalUp"), Xl.FlagAttr(border, "diagonalDown"));
    }

    private static BorderSide Side(MarkupNode e)
    {
        int s = Array.IndexOf(StyleNames, e.GetAttribute("style") ?? "none");
        StyleColor col = default;
        foreach (MarkupNode k in e.ChildElements)
        {
            if (k.LocalName.SequenceEqual("color"u8)) col = StyleColor.Read(k);
        }

        return new BorderSide(s < 0 ? BorderLineStyle.None : (BorderLineStyle)s, col);
    }

    internal string ToXml(string pc)
    {
        var sb = new StringBuilder("<").Append(pc).Append("border");
        if (DiagonalUp) sb.Append(" diagonalUp=\"1\"");
        if (DiagonalDown) sb.Append(" diagonalDown=\"1\"");
        sb.Append('>');
        Add(sb, pc, "left", Left);
        Add(sb, pc, "right", Right);
        Add(sb, pc, "top", Top);
        Add(sb, pc, "bottom", Bottom);
        Add(sb, pc, "diagonal", Diagonal);
        return sb.Append("</").Append(pc).Append("border>").ToString();
    }

    private static void Add(StringBuilder sb, string pc, string name, BorderSide s)
    {
        if (s.Style == BorderLineStyle.None)
        {
            sb.Append('<').Append(pc).Append(name).Append("/>");
            return;
        }

        sb.Append('<').Append(pc).Append(name).Append(" style=\"").Append(StyleNames[(int)s.Style]).Append("\">");
        sb.Append('<').Append(pc).Append("color ").Append(s.Color.Kind == ColorKind.None ? "auto=\"1\"" : s.Color.ToAttributes()).Append("/></").Append(pc).Append(name).Append('>');
    }
}

/// <summary>Horizontal alignment (ST_HorizontalAlignment).</summary>
public enum CellHorizontalAlignment : byte
{
    /// <summary>By the type of the value.</summary>
    General,

    /// <summary>Left.</summary>
    Left,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Right.</summary>
    Right,

    /// <summary>Repeated to fill the cell.</summary>
    Fill,

    /// <summary>Justified.</summary>
    Justify,

    /// <summary>Centred across the selection.</summary>
    CenterContinuous,

    /// <summary>Distributed.</summary>
    Distributed,
}

/// <summary>Vertical alignment (ST_VerticalAlignment).</summary>
public enum CellVerticalAlignment : byte
{
    /// <summary>Bottom (the default).</summary>
    Bottom,

    /// <summary>Top.</summary>
    Top,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Justified.</summary>
    Justify,

    /// <summary>Distributed.</summary>
    Distributed,
}

/// <summary>Cell alignment (CT_CellAlignment, §18.8.1).</summary>
/// <param name="Horizontal">Horizontal alignment.</param>
/// <param name="Vertical">Vertical alignment.</param>
/// <param name="WrapText">Wrap long text.</param>
/// <param name="Indent">Indent in character widths.</param>
/// <param name="TextRotation">Rotation in degrees (0–180; 255 for vertical text).</param>
/// <param name="ShrinkToFit">Shrink text to fit the cell.</param>
/// <param name="ReadingOrder">0 by context, 1 left to right, 2 right to left.</param>
/// <param name="JustifyLastLine">Justify the last line.</param>
/// <param name="RelativeIndent">The indent relative to the cell style's.</param>
public sealed record CellAlignment(CellHorizontalAlignment Horizontal = CellHorizontalAlignment.General, CellVerticalAlignment Vertical = CellVerticalAlignment.Bottom, bool WrapText = false,
    int Indent = 0, int TextRotation = 0, bool ShrinkToFit = false, int ReadingOrder = 0, bool JustifyLastLine = false, int RelativeIndent = 0)
{
    private static readonly string[] H = ["general", "left", "center", "right", "fill", "justify", "centerContinuous", "distributed"];
    private static readonly string[] V = ["bottom", "top", "center", "justify", "distributed"];

    internal static CellAlignment Read(MarkupNode a)
    {
        int h = Array.IndexOf(H, a.GetAttribute("horizontal") ?? "general");
        int v = Array.IndexOf(V, a.GetAttribute("vertical") ?? "bottom");
        return new CellAlignment(h < 0 ? 0 : (CellHorizontalAlignment)h, v < 0 ? 0 : (CellVerticalAlignment)v, Xl.FlagAttr(a, "wrapText"), Xl.Int(a.GetAttribute("indent")),
            Xl.Int(a.GetAttribute("textRotation")), Xl.FlagAttr(a, "shrinkToFit"), Xl.Int(a.GetAttribute("readingOrder")), Xl.FlagAttr(a, "justifyLastLine"), Xl.Int(a.GetAttribute("relativeIndent")));
    }

    internal string ToXml(string pc)
    {
        var sb = new StringBuilder("<").Append(pc).Append("alignment");
        if (Horizontal != 0) sb.Append(" horizontal=\"").Append(H[(int)Horizontal]).Append('"');
        if (Vertical != 0) sb.Append(" vertical=\"").Append(V[(int)Vertical]).Append('"');
        if (TextRotation != 0) sb.Append(" textRotation=\"").Append(TextRotation.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (WrapText) sb.Append(" wrapText=\"1\"");
        if (Indent != 0) sb.Append(" indent=\"").Append(Indent.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (RelativeIndent != 0) sb.Append(" relativeIndent=\"").Append(RelativeIndent.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (JustifyLastLine) sb.Append(" justifyLastLine=\"1\"");
        if (ShrinkToFit) sb.Append(" shrinkToFit=\"1\"");
        if (ReadingOrder != 0) sb.Append(" readingOrder=\"").Append(ReadingOrder.ToString(CultureInfo.InvariantCulture)).Append('"');
        return sb.Append("/>").ToString();
    }
}

/// <summary>Cell protection (CT_CellProtection, §18.8.7). It takes effect when the sheet is protected.</summary>
/// <param name="Locked">The cell is locked (the default).</param>
/// <param name="Hidden">The formula is hidden.</param>
public sealed record CellProtection(bool Locked = true, bool Hidden = false)
{
    internal static CellProtection Read(MarkupNode p) => new(p.GetAttribute("locked") is not ("0" or "false"), Xl.FlagAttr(p, "hidden"));

    internal string ToXml(string pc) => "<" + pc + "protection" + (Locked ? string.Empty : " locked=\"0\"") + (Hidden ? " hidden=\"1\"" : string.Empty) + "/>";
}

/// <summary>A cell format (<c>xf</c>, CT_Xf §18.8.45) as stored: ids into the style sheet's collections.</summary>
/// <param name="NumberFormatId">The number format id (built-in below 164, else a <c>numFmt</c>).</param>
/// <param name="FontId">The index into fonts.</param>
/// <param name="FillId">The index into fills.</param>
/// <param name="BorderId">The index into borders.</param>
/// <param name="StyleFormatId">For a cell format, the index of the named cell style's format in <c>cellStyleXfs</c>.</param>
/// <param name="ApplyNumberFormat">The stored <c>applyNumberFormat</c>, or null when absent.</param>
/// <param name="ApplyFont">The stored <c>applyFont</c>, or null.</param>
/// <param name="ApplyFill">The stored <c>applyFill</c>, or null.</param>
/// <param name="ApplyBorder">The stored <c>applyBorder</c>, or null.</param>
/// <param name="ApplyAlignment">The stored <c>applyAlignment</c>, or null.</param>
/// <param name="ApplyProtection">The stored <c>applyProtection</c>, or null.</param>
/// <param name="QuotePrefix">The text is shown with a leading apostrophe in the editor.</param>
/// <param name="Alignment">The alignment child, or null.</param>
/// <param name="Protection">The protection child, or null.</param>
public sealed record CellFormat(int NumberFormatId, int FontId, int FillId, int BorderId, int StyleFormatId, bool? ApplyNumberFormat, bool? ApplyFont, bool? ApplyFill, bool? ApplyBorder,
    bool? ApplyAlignment, bool? ApplyProtection, bool QuotePrefix, CellAlignment? Alignment, CellProtection? Protection);

/// <summary>A named cell style (<c>cellStyle</c>, §18.8.8).</summary>
/// <param name="Name">The style's name.</param>
/// <param name="FormatId">The index into <c>cellStyleXfs</c>.</param>
/// <param name="BuiltInId">The built-in style id (<c>builtinId</c>), or -1 for a user style.</param>
/// <param name="Hidden">The style is hidden in the gallery.</param>
public sealed record NamedCellStyle(string Name, int FormatId, int BuiltInId, bool Hidden);

/// <summary>A differential format (<c>dxf</c>, §18.8.14): the properties a conditional format or table style layers over a cell's own.</summary>
/// <param name="Font">The font properties set, or null.</param>
/// <param name="NumberFormatCode">The number format code, or null.</param>
/// <param name="Fill">The fill, or null.</param>
/// <param name="Border">The border, or null.</param>
/// <param name="Alignment">The alignment, or null.</param>
/// <param name="Protection">The protection, or null.</param>
public sealed record DifferentialFormat(CellFont? Font = null, string? NumberFormatCode = null, CellFill? Fill = null, CellBorder? Border = null, CellAlignment? Alignment = null, CellProtection? Protection = null);

/// <summary>What a cell format resolves to once the named cell style, number formats and collections are applied.</summary>
/// <param name="FormatIndex">The index of the cell format (what <see cref="Cell.StyleIndex"/> holds).</param>
/// <param name="StyleName">The named cell style's name, or null.</param>
/// <param name="NumberFormatId">The number format id.</param>
/// <param name="NumberFormatCode">The number format code (<c>General</c> when unknown).</param>
/// <param name="Font">The font.</param>
/// <param name="Fill">The fill.</param>
/// <param name="Border">The border.</param>
/// <param name="Alignment">The alignment (the default when none is stored).</param>
/// <param name="Protection">The protection (locked, visible when none is stored).</param>
/// <param name="QuotePrefix">The quote-prefix flag.</param>
public sealed record ResolvedCellStyle(int FormatIndex, string? StyleName, int NumberFormatId, string NumberFormatCode, CellFont Font, CellFill Fill, CellBorder Border, CellAlignment Alignment, CellProtection Protection, bool QuotePrefix);

/// <summary>A description of a cell format to find or add with <see cref="StyleSheet.GetOrAdd"/>. A null part is the default (General, the workbook's first font, fill and border).</summary>
public sealed record CellStyleSpec
{
    /// <summary>Gets the number format code, or null for General.</summary>
    public string? NumberFormatCode { get; init; }

    /// <summary>Gets the font, or null for the default font.</summary>
    public CellFont? Font { get; init; }

    /// <summary>Gets the fill, or null for none.</summary>
    public CellFill? Fill { get; init; }

    /// <summary>Gets the border, or null for none.</summary>
    public CellBorder? Border { get; init; }

    /// <summary>Gets the alignment, or null for the default.</summary>
    public CellAlignment? Alignment { get; init; }

    /// <summary>Gets the protection, or null for the default (locked).</summary>
    public CellProtection? Protection { get; init; }

    /// <summary>Gets a value indicating whether the quote-prefix flag is set.</summary>
    public bool QuotePrefix { get; init; }

    /// <summary>Gets the name of the cell style the format builds on, or null for Normal.</summary>
    public string? StyleName { get; init; }

    /// <summary>Makes a spec equal to a resolved style, to change a part of it with a <c>with</c> expression.</summary>
    /// <param name="style">The resolved style.</param>
    /// <returns>The spec.</returns>
    public static CellStyleSpec From(ResolvedCellStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new CellStyleSpec { NumberFormatCode = style.NumberFormatCode, Font = style.Font, Fill = style.Fill, Border = style.Border, Alignment = style.Alignment, Protection = style.Protection, QuotePrefix = style.QuotePrefix, StyleName = style.StyleName };
    }
}

/// <summary>Small readers and writers shared by the style, table, validation and name models.</summary>
internal static class Xl
{
    public static bool Flag(MarkupNode e) => e.GetAttribute("val") is not ("0" or "false");

    public static bool FlagAttr(MarkupNode e, string name) => e.GetAttribute(name) is "1" or "true";

    public static bool? OptFlag(MarkupNode e, string name) => e.GetAttribute(name) switch { "1" or "true" => true, "0" or "false" => false, _ => null };

    public static int Int(string? s, int fallback = 0) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    public static double Double(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    public static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    public static string Esc(string s)
    {
        if (s.AsSpan().IndexOfAny("&<>\"'") < 0 && !HasControl(s))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", '\'' => "&apos;", _ => char.IsControl(c) && c is not ('\t' or '\n' or '\r') ? string.Empty : c.ToString() });
        }

        return sb.ToString();
    }

    private static bool HasControl(string s)
    {
        foreach (char c in s)
        {
            if (char.IsControl(c) && c is not ('\t' or '\n' or '\r'))
            {
                return true;
            }
        }

        return false;
    }

    public static MarkupNode Child(MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    public static string Prefix(MarkupNode root) => root.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(root.Prefix) + ":";

    public static MarkupNode LastElement(MarkupNode parent)
    {
        MarkupNode n = parent.LastChild;
        while (!n.IsNull && n.Kind != MarkupNodeKind.Element)
        {
            n = n.PreviousSibling;
        }

        return n;
    }

    /// <summary>Inserts an element before the first existing child that comes later in the schema order, or appends it.</summary>
    public static MarkupNode InsertInOrder(MarkupNode parent, string[] order, string local, string xml)
    {
        int idx = Array.IndexOf(order, local);
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        foreach (MarkupNode child in parent.ChildElements)
        {
            int ci = Array.IndexOf(order, child.LocalNameString);
            if (ci > idx && idx >= 0)
            {
                child.InsertXmlBefore(bytes);
                return child.PreviousSibling.Kind == MarkupNodeKind.Element ? child.PreviousSibling : PrevElement(child);
            }
        }

        parent.AppendXml(bytes);
        return LastElement(parent);
    }

    private static MarkupNode PrevElement(MarkupNode n)
    {
        MarkupNode p = n.PreviousSibling;
        while (!p.IsNull && p.Kind != MarkupNodeKind.Element)
        {
            p = p.PreviousSibling;
        }

        return p;
    }
}
