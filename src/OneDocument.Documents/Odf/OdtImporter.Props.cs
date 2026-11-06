using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Property conversion: OpenDocument style properties (fo:, style:, svg: attributes) -> WordprocessingML property elements, kept as ready XML
// in PropBag so that nearer styles override farther ones and the elements come out in schema order.
internal sealed partial class OdtImporter
{
    private readonly Dictionary<string, string> _fontFamilies = new(StringComparer.Ordinal);

    private void PrepareFonts()
    {
        foreach (MarkupNode root in new[] { _pkg.StylesRoot, _pkg.ContentRoot })
        {
            MarkupNode decls = OdfMarkup.Child(root, OdfNamespaces.Office, "font-face-decls");
            if (decls.IsNull)
            {
                continue;
            }

            foreach (MarkupNode f in decls.ChildElements)
            {
                string? name = A(f, OdfNamespaces.Style, "name");
                string? family = A(f, OdfNamespaces.Svg, "font-family");
                if (name is not null && family is not null)
                {
                    _fontFamilies.TryAdd(name, CleanFamily(family));
                }
            }
        }
    }

    // svg:font-family may quote a name that has spaces and may list alternatives; the first name is the font.
    private static string CleanFamily(string family)
    {
        string f = family.Trim();
        int comma = f.IndexOf(',', StringComparison.Ordinal);
        if (comma > 0)
        {
            f = f[..comma].Trim();
        }

        return f.Trim('\'', '"');
    }

    private string FontName(string? styleFontName, string? foFamily)
    {
        if (styleFontName is not null)
        {
            return _fontFamilies.TryGetValue(styleFontName, out string? f) ? f : styleFontName;
        }

        return foFamily is null ? string.Empty : CleanFamily(foFamily);
    }

    private static string Hex(string? color)
    {
        if (color is null)
        {
            return string.Empty;
        }

        string c = color.Trim();
        if (c.Length == 7 && c[0] == '#' && IsHex(c.AsSpan(1)))
        {
            return c[1..].ToUpperInvariant();
        }

        return string.Empty;
    }

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        foreach (char ch in s)
        {
            if (!char.IsAsciiHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string X(string name, params (string Attr, string? Value)[] attrs)
    {
        var sb = new StringBuilder("<w:").Append(name);
        foreach ((string attr, string? value) in attrs)
        {
            if (value is not null)
            {
                sb.Append(" w:").Append(attr).Append("=\"").Append(OdfUnits.Esc(value)).Append('"');
            }
        }

        return sb.Append("/>").ToString();
    }

    // ---- text properties ---------------------------------------------------------------------------------------------------------

    private PropBag RunProps(MarkupNode tp, double baseHalfPoints = 24)
    {
        var bag = new PropBag();
        if (tp.IsNull)
        {
            return bag;
        }

        string? weight = A(tp, OdfNamespaces.Fo, "font-weight");
        if (weight is not null)
        {
            bool bold = weight is "bold" || (int.TryParse(weight, NumberStyles.None, CultureInfo.InvariantCulture, out int wv) && wv >= 600);
            bag.Set("b", bold ? "<w:b/>" : "<w:b w:val=\"0\"/>");
        }

        string? cw = A(tp, OdfNamespaces.Style, "font-weight-complex");
        if (cw is not null)
        {
            bool bold = cw is "bold" || (int.TryParse(cw, NumberStyles.None, CultureInfo.InvariantCulture, out int wv) && wv >= 600);
            bag.Set("bCs", bold ? "<w:bCs/>" : "<w:bCs w:val=\"0\"/>");
        }

        string? style = A(tp, OdfNamespaces.Fo, "font-style");
        if (style is not null)
        {
            bag.Set("i", style is "italic" or "oblique" ? "<w:i/>" : "<w:i w:val=\"0\"/>");
        }

        string? cstyle = A(tp, OdfNamespaces.Style, "font-style-complex");
        if (cstyle is not null)
        {
            bag.Set("iCs", cstyle is "italic" or "oblique" ? "<w:iCs/>" : "<w:iCs w:val=\"0\"/>");
        }

        string? transform = A(tp, OdfNamespaces.Fo, "text-transform");
        if (transform == "uppercase")
        {
            bag.Set("caps", "<w:caps/>");
        }
        else if (transform is "none" or "lowercase")
        {
            bag.Set("caps", "<w:caps w:val=\"0\"/>");
        }

        string? variant = A(tp, OdfNamespaces.Fo, "font-variant");
        if (variant == "small-caps")
        {
            bag.Set("smallCaps", "<w:smallCaps/>");
        }
        else if (variant == "normal")
        {
            bag.Set("smallCaps", "<w:smallCaps w:val=\"0\"/>");
        }

        string? line = A(tp, OdfNamespaces.Style, "text-line-through-style");
        if (line is not null)
        {
            if (line == "none")
            {
                bag.Set("strike", "<w:strike w:val=\"0\"/>");
            }
            else if (A(tp, OdfNamespaces.Style, "text-line-through-type") == "double")
            {
                bag.Set("dstrike", "<w:dstrike/>");
            }
            else
            {
                bag.Set("strike", "<w:strike/>");
            }
        }

        if (IsOutline(A(tp, OdfNamespaces.Style, "text-outline")))
        {
            bag.Set("outline", "<w:outline/>");
        }

        string? shadow = A(tp, OdfNamespaces.Fo, "text-shadow");
        if (shadow is not null && shadow != "none")
        {
            bag.Set("shadow", "<w:shadow/>");
        }

        string? relief = A(tp, OdfNamespaces.Style, "font-relief");
        if (relief == "embossed")
        {
            bag.Set("emboss", "<w:emboss/>");
        }
        else if (relief == "engraved")
        {
            bag.Set("imprint", "<w:imprint/>");
        }

        if (A(tp, OdfNamespaces.Text, "display") == "none")
        {
            bag.Set("vanish", "<w:vanish/>");
        }

        string? color = Hex(A(tp, OdfNamespaces.Fo, "color"));
        if (color.Length > 0)
        {
            bag.Set("color", X("color", ("val", color)));
        }
        else if (IsTrue(A(tp, OdfNamespaces.Style, "use-window-font-color")))
        {
            bag.Set("color", X("color", ("val", "auto")));
        }

        string? spacing = A(tp, OdfNamespaces.Fo, "letter-spacing");
        if (spacing is not null && spacing != "normal" && OdfUnits.TryTwips(spacing, out int sp))
        {
            bag.Set("spacing", X("spacing", ("val", OdfUnits.I(sp))));
        }

        string? scale = A(tp, OdfNamespaces.Style, "text-scale");
        if (scale is not null && scale.EndsWith('%') && int.TryParse(scale.AsSpan(0, scale.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int pct) && pct is >= 1 and <= 600)
        {
            bag.Set("w", X("w", ("val", OdfUnits.I(pct))));
        }

        double size = baseHalfPoints;
        string? fs = A(tp, OdfNamespaces.Fo, "font-size");
        string? position = A(tp, OdfNamespaces.Style, "text-position");
        double relSize = 100;
        if (position is not null)
        {
            string[] tokens = position.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0)
            {
                string first = tokens[0];
                if (first == "super" || (first.EndsWith('%') && double.TryParse(first.AsSpan(0, first.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double up) && up > 0))
                {
                    bag.Set("vertAlign", X("vertAlign", ("val", "superscript")));
                }
                else if (first == "sub" || (first.EndsWith('%') && double.TryParse(first.AsSpan(0, first.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double down) && down < 0))
                {
                    bag.Set("vertAlign", X("vertAlign", ("val", "subscript")));
                }
                else
                {
                    bag.Set("vertAlign", X("vertAlign", ("val", "baseline")));
                }

                if (tokens.Length > 1 && tokens[1].EndsWith('%') && double.TryParse(tokens[1].AsSpan(0, tokens[1].Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double rel))
                {
                    relSize = rel;
                }
            }
        }

        if (fs is not null)
        {
            if (fs.EndsWith('%') && double.TryParse(fs.AsSpan(0, fs.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double fp))
            {
                size = Math.Round(baseHalfPoints * fp / 100);
                bag.Set("sz", X("sz", ("val", OdfUnits.I((long)Math.Max(2, size)))));
            }
            else if (OdfUnits.TryPoints(fs, out double pts) && pts > 0)
            {
                size = Math.Round(pts * 2);
                bag.Set("sz", X("sz", ("val", OdfUnits.I((long)Math.Clamp(size, 2, 3276)))));
            }
        }

        _ = relSize;
        string? fsc = A(tp, OdfNamespaces.Style, "font-size-complex");
        if (fsc is not null && OdfUnits.TryPoints(fsc, out double cpts) && cpts > 0)
        {
            bag.Set("szCs", X("szCs", ("val", OdfUnits.I((long)Math.Clamp(Math.Round(cpts * 2), 2, 3276)))));
        }

        // Fonts: the latin font names ascii and hAnsi, the Asian one eastAsia, the complex one cs.
        string latin = FontName(A(tp, OdfNamespaces.Style, "font-name"), A(tp, OdfNamespaces.Fo, "font-family"));
        string asian = FontName(A(tp, OdfNamespaces.Style, "font-name-asian"), A(tp, OdfNamespaces.Style, "font-family-asian"));
        string complex = FontName(A(tp, OdfNamespaces.Style, "font-name-complex"), A(tp, OdfNamespaces.Style, "font-family-complex"));
        if (latin.Length + asian.Length + complex.Length > 0)
        {
            bag.Set("rFonts", X("rFonts",
                ("ascii", latin.Length > 0 ? latin : null),
                ("hAnsi", latin.Length > 0 ? latin : null),
                ("eastAsia", asian.Length > 0 ? asian : null),
                ("cs", complex.Length > 0 ? complex : null)));
        }

        string? u = A(tp, OdfNamespaces.Style, "text-underline-style");
        if (u is not null)
        {
            if (u == "none")
            {
                bag.Set("u", X("u", ("val", "none")));
            }
            else
            {
                string type = A(tp, OdfNamespaces.Style, "text-underline-type") ?? "single";
                string bold = A(tp, OdfNamespaces.Style, "text-underline-width") is "bold" or "thick" ? "Heavy" : string.Empty;
                string val = u switch
                {
                    "dotted" => "dotted" + (bold.Length > 0 ? "Heavy" : string.Empty),
                    "dash" => "dash" + bold,
                    "long-dash" => "dashLong" + bold,
                    "dot-dash" => "dotDash" + bold,
                    "dot-dot-dash" => "dotDotDash" + bold,
                    "wave" => "wave" + (bold.Length > 0 ? "Heavy" : string.Empty),
                    _ => type == "double" ? "double" : bold.Length > 0 ? "thick" : "single",
                };
                string ucolor = Hex(A(tp, OdfNamespaces.Style, "text-underline-color"));
                bag.Set("u", X("u", ("val", val), ("color", ucolor.Length > 0 ? ucolor : null)));
            }
        }

        string? bg = Hex(A(tp, OdfNamespaces.Fo, "background-color"));
        if (bg.Length > 0)
        {
            bag.Set("shd", X("shd", ("val", "clear"), ("color", "auto"), ("fill", bg)));
        }

        string lang = A(tp, OdfNamespaces.Fo, "language") is { } l && l != "zxx" ? l + (A(tp, OdfNamespaces.Fo, "country") is { } c && c != "none" ? "-" + c : string.Empty) : string.Empty;
        string langAsia = A(tp, OdfNamespaces.Style, "language-asian") is { } la && la != "zxx" ? la + (A(tp, OdfNamespaces.Style, "country-asian") is { } ca && ca != "none" ? "-" + ca : string.Empty) : string.Empty;
        string langCs = A(tp, OdfNamespaces.Style, "language-complex") is { } lc && lc != "zxx" ? lc + (A(tp, OdfNamespaces.Style, "country-complex") is { } cc && cc != "none" ? "-" + cc : string.Empty) : string.Empty;
        if (lang.Length + langAsia.Length + langCs.Length > 0)
        {
            bag.Set("lang", X("lang", ("val", lang.Length > 0 ? lang : null), ("eastAsia", langAsia.Length > 0 ? langAsia : null), ("bidi", langCs.Length > 0 ? langCs : null)));
        }

        return bag;
    }

    private static bool IsOutline(string? v) => v is "true" or "1";

    // ---- paragraph properties ----------------------------------------------------------------------------------------------------

    private PropBag ParaProps(MarkupNode pp, bool inStyle)
    {
        var bag = new PropBag();
        if (pp.IsNull)
        {
            return bag;
        }

        string? align = A(pp, OdfNamespaces.Fo, "text-align");
        bool rtl = A(pp, OdfNamespaces.Style, "writing-mode") is "rl-tb" or "rl";
        if (align is not null)
        {
            // Part 1: start/end follow the writing direction; Word's left/right are physical, and with w:bidi they flip back, so the
            // logical values are written as left/right in a left-to-right paragraph and swapped in a right-to-left one.
            string jc = align switch
            {
                "start" => rtl ? "right" : "left",
                "end" => rtl ? "left" : "right",
                "left" => "left",
                "right" => "right",
                "center" => "center",
                "justify" => "both",
                _ => string.Empty,
            };
            if (jc.Length > 0)
            {
                bag.Set("jc", X("jc", ("val", jc)));
            }
        }

        if (rtl)
        {
            bag.Set("bidi", "<w:bidi/>");
        }

        int left = 0, right = 0;
        bool hasLeft = false, hasRight = false;
        string? margin = A(pp, OdfNamespaces.Fo, "margin");
        if (margin is not null && OdfUnits.TryTwips(margin, out int m))
        {
            left = right = m;
            hasLeft = hasRight = true;
        }

        if (OdfUnits.TryTwips(A(pp, OdfNamespaces.Fo, "margin-left"), out int ml))
        {
            left = ml;
            hasLeft = true;
        }

        if (OdfUnits.TryTwips(A(pp, OdfNamespaces.Fo, "margin-right"), out int mr))
        {
            right = mr;
            hasRight = true;
        }

        string? indent = A(pp, OdfNamespaces.Fo, "text-indent");
        bool hasIndent = indent is not null && OdfUnits.TryTwips(indent, out _);
        int ti = hasIndent ? OdfUnits.Twips(indent) : 0;
        if (hasLeft || hasRight || hasIndent)
        {
            bag.Set("ind", X("ind",
                ("left", hasLeft ? OdfUnits.I(left) : null),
                ("right", hasRight ? OdfUnits.I(right) : null),
                ("firstLine", hasIndent && ti > 0 ? OdfUnits.I(ti) : null),
                ("hanging", hasIndent && ti < 0 ? OdfUnits.I(-ti) : null)));
        }

        int before = 0, after = 0;
        bool hasBefore = false, hasAfter = false;
        if (margin is not null && OdfUnits.TryTwips(margin, out int mm))
        {
            before = after = mm;
            hasBefore = hasAfter = true;
        }

        if (OdfUnits.TryTwips(A(pp, OdfNamespaces.Fo, "margin-top"), out int mt))
        {
            before = mt;
            hasBefore = true;
        }

        if (OdfUnits.TryTwips(A(pp, OdfNamespaces.Fo, "margin-bottom"), out int mb))
        {
            after = mb;
            hasAfter = true;
        }

        string? lh = A(pp, OdfNamespaces.Fo, "line-height");
        string? lhAtLeast = A(pp, OdfNamespaces.Style, "line-height-at-least");
        string? lineVal = null, lineRule = null;
        if (lh is not null && lh.EndsWith('%') && double.TryParse(lh.AsSpan(0, lh.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double lp) && lp > 0)
        {
            lineVal = OdfUnits.I((long)Math.Round(240 * lp / 100));
            lineRule = "auto";
        }
        else if (lh is not null && lh != "normal" && OdfUnits.TryTwips(lh, out int lt) && lt > 0)
        {
            lineVal = OdfUnits.I(lt);
            lineRule = "exact";
        }
        else if (lhAtLeast is not null && OdfUnits.TryTwips(lhAtLeast, out int la) && la > 0)
        {
            lineVal = OdfUnits.I(la);
            lineRule = "atLeast";
        }

        if (hasBefore || hasAfter || lineVal is not null)
        {
            bag.Set("spacing", X("spacing",
                ("before", hasBefore ? OdfUnits.I(before) : null),
                ("after", hasAfter ? OdfUnits.I(after) : null),
                ("line", lineVal),
                ("lineRule", lineRule)));
        }

        if (A(pp, OdfNamespaces.Fo, "keep-with-next") is { } kwn)
        {
            bag.Set("keepNext", kwn == "always" ? "<w:keepNext/>" : "<w:keepNext w:val=\"0\"/>");
        }

        if (A(pp, OdfNamespaces.Fo, "keep-together") is { } kt)
        {
            bag.Set("keepLines", kt == "always" ? "<w:keepLines/>" : "<w:keepLines w:val=\"0\"/>");
        }

        string? bb = A(pp, OdfNamespaces.Fo, "break-before");
        if (bb == "page")
        {
            bag.Set("pageBreakBefore", "<w:pageBreakBefore/>");
        }
        else if (bb == "auto")
        {
            bag.Set("pageBreakBefore", "<w:pageBreakBefore w:val=\"0\"/>");
        }
        else if (bb == "column")
        {
            Lost("loss.column-breaks", "column breaks", "A column break before a paragraph was dropped.");
        }

        if (A(pp, OdfNamespaces.Fo, "break-after") == "page")
        {
            Lost("loss.page-break-after", "page breaks after a paragraph", "A page break after a paragraph was dropped (the hub has only a break before).");
        }

        string? widows = A(pp, OdfNamespaces.Fo, "widows");
        string? orphans = A(pp, OdfNamespaces.Fo, "orphans");
        if (widows is not null || orphans is not null)
        {
            bool off = (widows is "0" or "1") && (orphans is null or "0" or "1");
            bag.Set("widowControl", off ? "<w:widowControl w:val=\"0\"/>" : "<w:widowControl/>");
        }

        string? bgc = Hex(A(pp, OdfNamespaces.Fo, "background-color"));
        if (bgc.Length > 0)
        {
            bag.Set("shd", X("shd", ("val", "clear"), ("color", "auto"), ("fill", bgc)));
        }

        string borders = Borders(pp, "w:pBdr");
        if (borders.Length > 0)
        {
            bag.Set("pBdr", borders);
        }

        MarkupNode tabs = OdfMarkup.Child(pp, OdfNamespaces.Style, "tab-stops");
        if (!tabs.IsNull)
        {
            var sb = new StringBuilder("<w:tabs>");
            bool any = false;
            foreach (MarkupNode t in tabs.ChildElements)
            {
                if (!Is(t, OdfNamespaces.Style, "tab-stop") || !OdfUnits.TryTwips(A(t, OdfNamespaces.Style, "position"), out int pos))
                {
                    continue;
                }

                string type = A(t, OdfNamespaces.Style, "type") switch { "center" => "center", "right" => "right", "char" => "decimal", _ => "left" };
                string leader = A(t, OdfNamespaces.Style, "leader-text") is { Length: > 0 } lt2 ? lt2 switch { "." => "dot", "-" => "hyphen", "_" => "underscore", _ => "dot" }
                    : A(t, OdfNamespaces.Style, "leader-style") is { } ls and not "none" ? "dot" : "none";
                sb.Append("<w:tab w:val=\"").Append(type).Append("\" w:leader=\"").Append(leader).Append("\" w:pos=\"").Append(OdfUnits.I(pos)).Append("\"/>");
                any = true;
            }

            if (any)
            {
                bag.Set("tabs", sb.Append("</w:tabs>").ToString());
            }
        }

        if (IsTrue(A(pp, OdfNamespaces.Style, "contextual-spacing")))
        {
            bag.Set("contextualSpacing", "<w:contextualSpacing/>");
        }

        _ = inStyle;
        return bag;
    }

    // ---- borders ---------------------------------------------------------------------------------------------------------------

    // Part 1, Common style properties: fo:border is "width style color" (or none); the per-side attributes override it, and
    // style:border-line-width gives the three widths of a double line. Word writes the size in eighths of a point.
    private static string Borders(MarkupNode props, string container)
    {
        string[] sides = ["top", "left", "bottom", "right"];
        string? all = A(props, OdfNamespaces.Fo, "border");
        string? allWidth = A(props, OdfNamespaces.Style, "border-line-width");
        string? padAll = A(props, OdfNamespaces.Fo, "padding");
        var sb = new StringBuilder();
        foreach (string side in sides)
        {
            string? b = A(props, OdfNamespaces.Fo, "border-" + side) ?? all;
            if (b is null || b == "none")
            {
                continue;
            }

            string? lw = A(props, OdfNamespaces.Style, "border-line-width-" + side) ?? allWidth;
            string? pad = A(props, OdfNamespaces.Fo, "padding-" + side) ?? padAll;
            sb.Append(Border(side, b, lw, pad, container));
        }

        if (sb.Length == 0)
        {
            return string.Empty;
        }

        return "<" + container + ">" + sb + "</" + container + ">";
    }

    private static string Border(string side, string spec, string? lineWidth, string? padding, string container)
    {
        string[] parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double widthPt = 0.5;
        string style = "solid";
        string color = "auto";
        foreach (string p in parts)
        {
            if (p.StartsWith('#'))
            {
                string h = Hex(p);
                if (h.Length > 0)
                {
                    color = h;
                }
            }
            else if (OdfUnits.TryPoints(p, out double pt) && char.IsAsciiDigit(p[0]))
            {
                widthPt = pt;
            }
            else
            {
                style = p;
            }
        }

        string val = style switch
        {
            "solid" => "single",
            "dotted" => "dotted",
            "dashed" => "dashed",
            "double" => "double",
            "groove" => "threeDEngrave",
            "ridge" => "threeDEmboss",
            "inset" => "inset",
            "outset" => "outset",
            "hidden" or "none" => "nil",
            _ => "single",
        };
        if (val == "single" && lineWidth is not null && lineWidth.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 3)
        {
            val = "double";
        }

        int size = (int)Math.Clamp(Math.Round(widthPt * 8), 2, 96);
        int space = 0;
        if (padding is not null && OdfUnits.TryPoints(padding, out double pp2))
        {
            space = (int)Math.Clamp(Math.Round(pp2), 0, 31);
        }

        // Table cell borders have no w:space; a paragraph border does.
        bool para = container == "w:pBdr";
        string element = !para && side is "left" or "right" ? side == "left" ? "start" : "end" : side;
        _ = element;
        return "<w:" + side + " w:val=\"" + val + "\" w:sz=\"" + OdfUnits.I(size) + "\"" + (para ? " w:space=\"" + OdfUnits.I(space) + "\"" : " w:space=\"0\"") + " w:color=\"" + color + "\"/>";
    }
}
