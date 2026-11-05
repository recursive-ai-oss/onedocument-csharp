using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

/// <summary>A line-spacing or paragraph-spacing value: a percentage of the single line, or an exact size in points.</summary>
internal readonly record struct SpacingValue(bool IsPercent, double Value);

/// <summary>A bullet or number as the paragraph's properties describe it. ECMA-376 Part 1 §21.1.2.4.</summary>
internal enum BulletKind
{
    Unspecified,
    None,
    Character,
    AutoNumber,
    Picture,
}

/// <summary>The paragraph properties after inheritance, in points.</summary>
internal sealed class SlideParagraphStyle
{
    public int Level;
    public string Align = "l";
    public double MarginLeft;
    public double MarginRight;
    public double Indent;
    public bool Rtl;
    public SpacingValue LineSpacing = new(true, 1.0);
    public SpacingValue SpaceBefore = new(false, 0);
    public SpacingValue SpaceAfter = new(false, 0);
    public BulletKind Bullet = BulletKind.Unspecified;
    public string? BulletChar;
    public string? BulletFont;
    public string? BulletType;
    public int BulletStart = 1;
    public double? BulletSizePercent;
    public double? BulletSizePoints;
    public DrawingColor? BulletColor;
    public double DefaultTab = 72;
    public List<(double Position, string Align)> Tabs = [];
    public bool EastAsianLineBreak = true;
}

/// <summary>The character properties after inheritance, in points.</summary>
internal sealed class SlideRunStyle
{
    public double Size = 18;
    public bool Bold;
    public bool Italic;
    public string? Underline;
    public string? Strike;
    public string Cap = "none";
    public double Spacing;
    public double Baseline;
    public DrawingColor Color = new(0, 0, 0);
    public DrawingColor? Highlight;
    public string Latin = "Calibri";
    public string EastAsian = string.Empty;
    public string ComplexScript = string.Empty;
    public string? Language;
    public double KerningFrom;
    public string? LinkRelationship;
    public bool LinkIsInternal;

    /// <summary>True when the run's own properties set the colour (a table style does not override it).</summary>
    public bool ColorFromRun;

    /// <summary>True when the run's own properties set bold.</summary>
    public bool BoldFromRun;
}

/// <summary>
/// Reads text properties through the inheritance chain of ECMA-376 Part 1 §21.1.2.3 (character) and §21.1.2.2 (paragraph): the
/// paragraph's or run's own properties first, then the list styles (<c>a:lstStyle</c>) of the shape, its layout and master placeholders,
/// the master's text style and the presentation's default text style, each by paragraph level and then its default.
/// </summary>
internal sealed class StyleReader(IReadOnlyList<MarkupNode> containers, ColorResolver colors, DrawingTheme theme, string textColorSlot, double fontScale = 1.0)
{
    private const double EmuPerPoint = 12700.0;

    /// <summary>The list styles that apply, highest priority first.</summary>
    public IReadOnlyList<MarkupNode> Containers { get; } = containers;

    public ColorResolver Colors { get; } = colors;

    public DrawingTheme Theme { get; } = theme;

    /// <summary>The paragraph property elements for a level, highest priority first: the paragraph's own, each container's level entry, then each container's default.</summary>
    public List<MarkupNode> ParagraphChain(MarkupNode pPr, int level)
    {
        var chain = new List<MarkupNode>(Containers.Count * 2 + 1);
        if (!pPr.IsNull)
        {
            chain.Add(pPr);
        }

        string levelName = "lvl" + (level + 1).ToString(CultureInfo.InvariantCulture) + "pPr";
        foreach (MarkupNode c in Containers)
        {
            MarkupNode lvl = DrawingTheme.Child(c, levelName);
            if (!lvl.IsNull)
            {
                chain.Add(lvl);
            }
        }

        foreach (MarkupNode c in Containers)
        {
            MarkupNode def = DrawingTheme.Child(c, "defPPr");
            if (!def.IsNull)
            {
                chain.Add(def);
            }
        }

        return chain;
    }

    private static string? Attr(List<MarkupNode> chain, string name)
    {
        foreach (MarkupNode n in chain)
        {
            if (n.GetAttribute(name) is { } v)
            {
                return v;
            }
        }

        return null;
    }

    private static double? Number(string? s) =>
        s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;

    private static bool? Flag(string? s) => s switch { "1" or "true" => true, "0" or "false" => false, _ => null };

    private static MarkupNode FirstChild(List<MarkupNode> chain, string name)
    {
        foreach (MarkupNode n in chain)
        {
            MarkupNode c = DrawingTheme.Child(n, name);
            if (!c.IsNull)
            {
                return c;
            }
        }

        return default;
    }

    private static SpacingValue? Spacing(List<MarkupNode> chain, string name)
    {
        MarkupNode e = FirstChild(chain, name);
        if (e.IsNull)
        {
            return null;
        }

        MarkupNode pct = DrawingTheme.Child(e, "spcPct");
        if (!pct.IsNull && Number(pct.GetAttribute("val")) is { } p)
        {
            // ECMA-376 Part 1 §21.1.2.2.5 (lnSpc), §20.1.10.46 (ST_TextSpacingPercentOrPercentString): thousandths of a percent.
            return new SpacingValue(true, Math.Clamp(p / 100000.0, 0, 100));
        }

        MarkupNode pts = DrawingTheme.Child(e, "spcPts");
        if (!pts.IsNull && Number(pts.GetAttribute("val")) is { } v)
        {
            // §20.1.10.47 (ST_TextSpacingPoint): hundredths of a point.
            return new SpacingValue(false, Math.Clamp(v / 100.0, 0, 5000));
        }

        return null;
    }

    /// <summary>Reads the paragraph properties of a paragraph at a level.</summary>
    public SlideParagraphStyle Paragraph(MarkupNode pPr, int level)
    {
        List<MarkupNode> chain = ParagraphChain(pPr, level);
        var s = new SlideParagraphStyle { Level = level };
        s.Align = Attr(chain, "algn") ?? "l";
        // Lengths in a file can be anything; keep them to what a slide can show so the layout's arithmetic stays finite.
        s.MarginLeft = Math.Clamp((Number(Attr(chain, "marL")) ?? 0) / EmuPerPoint, 0, 5000);
        s.MarginRight = Math.Clamp((Number(Attr(chain, "marR")) ?? 0) / EmuPerPoint, 0, 5000);
        s.Indent = Math.Clamp((Number(Attr(chain, "indent")) ?? 0) / EmuPerPoint, -5000, 5000);
        s.Rtl = Flag(Attr(chain, "rtl")) ?? false;
        s.DefaultTab = Math.Clamp((Number(Attr(chain, "defTabSz")) ?? 914400) / EmuPerPoint, 1, 5000);
        s.EastAsianLineBreak = Flag(Attr(chain, "eaLnBrk")) ?? true;
        s.LineSpacing = Spacing(chain, "lnSpc") ?? new SpacingValue(true, 1.0);
        s.SpaceBefore = Spacing(chain, "spcBef") ?? new SpacingValue(false, 0);
        s.SpaceAfter = Spacing(chain, "spcAft") ?? new SpacingValue(false, 0);

        // The bullet comes as a unit from the first source that names one (§21.1.2.4.1 and its siblings).
        foreach (MarkupNode n in chain)
        {
            bool found = false;
            foreach (MarkupNode c in n.ChildElements)
            {
                switch (c.LocalNameString)
                {
                    case "buNone":
                        s.Bullet = BulletKind.None;
                        found = true;
                        break;
                    case "buChar":
                        s.Bullet = BulletKind.Character;
                        s.BulletChar = c.GetAttribute("char");
                        found = true;
                        break;
                    case "buAutoNum":
                        s.Bullet = BulletKind.AutoNumber;
                        s.BulletType = c.GetAttribute("type");
                        s.BulletStart = (int)(Number(c.GetAttribute("startAt")) ?? 1);
                        found = true;
                        break;
                    case "buBlip":
                        s.Bullet = BulletKind.Picture;
                        found = true;
                        break;
                }

                if (found)
                {
                    break;
                }
            }

            if (found)
            {
                break;
            }
        }

        MarkupNode buFont = FirstChild(chain, "buFont");
        if (!buFont.IsNull && buFont.GetAttribute("typeface") is { Length: > 0 } bf)
        {
            // A theme reference (+mj-lt) names the theme font; East Asian and complex-script references name none here.
            s.BulletFont = bf is "+mj-ea" or "+mn-ea" or "+mj-cs" or "+mn-cs" ? null : Theme.ResolveFont(bf);
        }

        MarkupNode buSzPct = FirstChild(chain, "buSzPct");
        if (!buSzPct.IsNull && Number(buSzPct.GetAttribute("val")) is { } pct)
        {
            s.BulletSizePercent = pct / 100000.0;
        }

        MarkupNode buSzPts = FirstChild(chain, "buSzPts");
        if (!buSzPts.IsNull && Number(buSzPts.GetAttribute("val")) is { } pts)
        {
            s.BulletSizePoints = pts / 100.0;
        }

        MarkupNode buClr = FirstChild(chain, "buClr");
        if (!buClr.IsNull && Colors.ResolveChild(buClr) is { } bc)
        {
            s.BulletColor = bc;
        }

        MarkupNode tabs = FirstChild(chain, "tabLst");
        foreach (MarkupNode tab in tabs.ChildElements)
        {
            if (tab.LocalNameString == "tab" && Number(tab.GetAttribute("pos")) is { } pos)
            {
                s.Tabs.Add((Math.Clamp(pos / EmuPerPoint, 0, 20000), tab.GetAttribute("algn") ?? "l"));
            }
        }

        s.Tabs.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        return s;
    }

    /// <summary>Reads the character properties of a run; <paramref name="rPr"/> may be a null node.</summary>
    public SlideRunStyle Run(MarkupNode rPr, List<MarkupNode> paragraphChain)
    {
        var chain = new List<MarkupNode>(paragraphChain.Count + 1);
        if (!rPr.IsNull)
        {
            chain.Add(rPr);
        }

        foreach (MarkupNode p in paragraphChain)
        {
            MarkupNode d = DrawingTheme.Child(p, "defRPr");
            if (!d.IsNull)
            {
                chain.Add(d);
            }
        }

        var s = new SlideRunStyle();
        s.Size = Math.Clamp((Number(Attr(chain, "sz")) ?? 1800) / 100.0 * fontScale, 1, 4000);
        s.Bold = Flag(Attr(chain, "b")) ?? false;
        s.Italic = Flag(Attr(chain, "i")) ?? false;
        string? u = Attr(chain, "u");
        s.Underline = u is null or "none" ? null : u;
        string? strike = Attr(chain, "strike");
        s.Strike = strike is null or "noStrike" ? null : strike;
        s.Cap = Attr(chain, "cap") ?? "none";
        s.Spacing = Math.Clamp((Number(Attr(chain, "spc")) ?? 0) / 100.0, -1000, 1000);
        // §21.1.2.3.9 (rPr baseline): thousandths of a percent of the font size, positive raises.
        s.Baseline = Math.Clamp((Number(Attr(chain, "baseline")) ?? 0) / 100000.0, -5, 5);
        s.Language = Attr(chain, "lang");
        s.KerningFrom = (Number(Attr(chain, "kern")) ?? 0) / 100.0;

        MarkupNode fill = FirstChild(chain, "solidFill");
        DrawingColor? color = fill.IsNull ? null : Colors.ResolveChild(fill);
        if (color is null)
        {
            MarkupNode grad = FirstChild(chain, "gradFill");
            if (!grad.IsNull)
            {
                foreach (MarkupNode gs in DrawingTheme.Child(grad, "gsLst").ChildElements)
                {
                    color = Colors.ResolveChild(gs);
                    break;
                }
            }
        }

        s.Color = color ?? Theme.Color(textColorSlot) ?? new DrawingColor(0, 0, 0);
        MarkupNode hl = FirstChild(chain, "highlight");
        if (!hl.IsNull)
        {
            s.Highlight = Colors.ResolveChild(hl);
        }

        s.ColorFromRun = !rPr.IsNull && (!DrawingTheme.Child(rPr, "solidFill").IsNull || !DrawingTheme.Child(rPr, "gradFill").IsNull);
        s.BoldFromRun = !rPr.IsNull && rPr.GetAttribute("b") is not null;
        s.Latin = Typeface(FirstChild(chain, "latin"), "Calibri");
        s.EastAsian = Typeface(FirstChild(chain, "ea"), string.Empty);
        s.ComplexScript = Typeface(FirstChild(chain, "cs"), string.Empty);
        if (!rPr.IsNull)
        {
            MarkupNode link = DrawingTheme.Child(rPr, "hlinkClick");
            if (!link.IsNull)
            {
                string? id = RelationshipOf(link);
                string? action = link.GetAttribute("action");
                if (id is { Length: > 0 } && string.IsNullOrEmpty(action))
                {
                    s.LinkRelationship = id;
                }
                else
                {
                    s.LinkIsInternal = true;
                }

                // A hyperlink takes the theme's link colour and an underline unless the run says otherwise (an hlinkClr extension is not read).
                if (Theme.Color("hlink") is { } hc)
                {
                    s.Color = hc;
                }

                s.Underline ??= "sng";
            }
        }

        return s;
    }

    private string Typeface(MarkupNode e, string fallback)
    {
        if (e.IsNull || e.GetAttribute("typeface") is not { Length: > 0 } tf)
        {
            return fallback;
        }

        return tf switch
        {
            // East Asian and complex-script theme fonts are not read from the theme; the font library's fallback chooses a face for them.
            "+mj-ea" or "+mn-ea" or "+mj-cs" or "+mn-cs" => string.Empty,
            _ => Theme.ResolveFont(tf),
        };
    }

    /// <summary>The characters after (and for ParenBoth, before) the number of an automatic bullet scheme.</summary>
    public static string ResolveListSuffix(string? type) => type switch
    {
        null => ".",
        _ when type.EndsWith("ParenBoth", StringComparison.Ordinal) => ")",
        _ when type.EndsWith("ParenR", StringComparison.Ordinal) => ")",
        _ when type.EndsWith("Period", StringComparison.Ordinal) => ".",
        _ when type.EndsWith("Plain", StringComparison.Ordinal) => string.Empty,
        _ => ".",
    };

    private static string? RelationshipOf(MarkupNode e)
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
}

/// <summary>Numbering schemes for automatic bullets. ECMA-376 Part 1 §21.1.2.4.1 (buAutoNum) and §20.1.10.61 (ST_TextAutonumberScheme).</summary>
internal static class AutoNumber
{
    public static string Format(string? type, int value)
    {
        string core = (type ?? "arabicPeriod") switch
        {
            var t when t.StartsWith("alphaLc", StringComparison.Ordinal) => Alpha(value, false),
            var t when t.StartsWith("alphaUc", StringComparison.Ordinal) => Alpha(value, true),
            var t when t.StartsWith("romanLc", StringComparison.Ordinal) => Roman(value).ToLowerInvariant(),
            var t when t.StartsWith("romanUc", StringComparison.Ordinal) => Roman(value),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
        string suffix = StyleReader.ResolveListSuffix(type);
        return type is not null && type.EndsWith("ParenBoth", StringComparison.Ordinal) ? "(" + core + suffix : core + suffix;
    }

    // a, b, ... z, aa, bb, ... as PowerPoint numbers: the letter repeats rather than carrying (§20.1.10.61 alphaLcPeriod: "a., b., ... aa., bb.").
    private static string Alpha(int value, bool upper)
    {
        int n = Math.Max(1, value) - 1;
        char c = (char)((upper ? 'A' : 'a') + (n % 26));
        return new string(c, (n / 26) + 1);
    }

    private static string Roman(int value)
    {
        if (value is < 1 or > 3999)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        (int, string)[] table = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var sb = new System.Text.StringBuilder();
        foreach ((int v, string s) in table)
        {
            while (value >= v)
            {
                sb.Append(s);
                value -= v;
            }
        }

        return sb.ToString();
    }
}
