using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Styles and property mapping: w:style -> style:style, w:rPr -> style:text-properties, w:pPr -> style:paragraph-properties.
internal sealed partial class OdtExporter
{
    private sealed class StyleDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Type = "paragraph";
        public string? BasedOn;
        public string? Next;
        public bool IsDefault;
        public MarkupNode Node;
        public string OdfName = string.Empty;
        public string DisplayName = string.Empty;
    }

    private readonly Dictionary<string, StyleDef> _styleDefs = new(StringComparer.Ordinal);
    private readonly List<StyleDef> _styleList = [];
    private StyleDef? _defaultParagraph;

    private static string OdfDisplayName(string wordName, string type)
    {
        string lower = wordName.ToLowerInvariant();
        if (lower.StartsWith("heading ", StringComparison.Ordinal) && lower.Length == 9 && char.IsAsciiDigit(lower[8]))
        {
            return "Heading " + lower[8];
        }

        if (lower.StartsWith("toc ", StringComparison.Ordinal) && lower.Length == 5 && char.IsAsciiDigit(lower[4]))
        {
            return "Contents " + lower[4];
        }

        return lower switch
        {
            "normal" => "Standard",
            "title" => "Title",
            "subtitle" => "Subtitle",
            "header" => "Header",
            "footer" => "Footer",
            "footnote text" => "Footnote",
            "endnote text" => "Endnote",
            "body text" => "Text body",
            "caption" => "Caption",
            "toc heading" => "Contents Heading",
            "quote" => "Quotations",
            "hyperlink" when type == "character" => "Internet link",
            "footnote reference" => "Footnote Characters",
            "endnote reference" => "Endnote Characters",
            "strong" => "Strong Emphasis",
            "page number" => "Page Number",
            _ => wordName,
        };
    }

    private void ReadStyles()
    {
        if (_stylesRoot.IsNull)
        {
            return;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkupNode st in _stylesRoot.ChildElements)
        {
            if (!IsW(st, "style") || A(st, "styleId") is not { } id)
            {
                continue;
            }

            var def = new StyleDef
            {
                Id = id,
                Type = A(st, "type") ?? "paragraph",
                Node = st,
                IsDefault = A(st, "default") is "1" or "true" or "on",
                BasedOn = A(Child(st, "basedOn"), "val"),
                Next = A(Child(st, "next"), "val"),
            };
            def.Name = A(Child(st, "name"), "val") ?? id;
            def.DisplayName = OdfDisplayName(def.Name, def.Type);
            def.OdfName = def.Type == "paragraph" && def.IsDefault ? "Standard" : OdfUnits.EncodeStyleName(def.DisplayName);
            if (def.Type == "paragraph" && def.IsDefault)
            {
                def.DisplayName = "Standard";
            }

            string unique = def.OdfName;
            for (int n = 2; !used.Add(def.Type + "|" + unique); n++)
            {
                unique = def.OdfName + OdfUnits.I(n);
            }

            def.OdfName = unique;
            _styleDefs[id] = def;
            _styleList.Add(def);
            if (def.Type == "paragraph" && def.IsDefault)
            {
                _defaultParagraph = def;
            }
        }
    }

    private string? OdfStyleName(string? id, string type) =>
        id is not null && _styleDefs.TryGetValue(id, out StyleDef? d) && d.Type == type ? d.OdfName : null;

    // Looks a property up along w:basedOn: the first style in the chain with the element wins.
    private MarkupNode StyleProp(string? styleId, string group, string element)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (styleId is not null && seen.Add(styleId) && _styleDefs.TryGetValue(styleId, out StyleDef? d))
        {
            MarkupNode g = Child(d.Node, group);
            MarkupNode e = Child(g, element);
            if (!e.IsNull)
            {
                return e;
            }

            styleId = d.BasedOn;
        }

        if (_stylesRoot.IsNull)
        {
            return default;
        }

        MarkupNode defaults = Child(_stylesRoot, "docDefaults");
        MarkupNode dg = Child(Child(defaults, group == "pPr" ? "pPrDefault" : "rPrDefault"), group);
        return Child(dg, element);
    }

    private int EffectiveOutline(string? styleId, MarkupNode directPPr)
    {
        MarkupNode o = Child(directPPr, "outlineLvl");
        if (o.IsNull)
        {
            o = StyleProp(styleId, "pPr", "outlineLvl");
        }

        int v = o.IsNull ? -1 : Int(A(o, "val"), -1);
        return v is >= 0 and <= 8 ? v + 1 : 0;
    }

    // ---- character properties -----------------------------------------------------------------------------------------------------

    private static string? HighlightColour(string name) => name switch
    {
        "yellow" => "#ffff00",
        "green" => "#00ff00",
        "cyan" => "#00ffff",
        "magenta" => "#ff00ff",
        "blue" => "#0000ff",
        "red" => "#ff0000",
        "darkBlue" => "#000080",
        "darkCyan" => "#008080",
        "darkGreen" => "#008000",
        "darkMagenta" => "#800080",
        "darkRed" => "#800000",
        "darkYellow" => "#808000",
        "darkGray" => "#808080",
        "lightGray" => "#c0c0c0",
        "black" => "#000000",
        "white" => "#ffffff",
        _ => null,
    };

    private static string Lower(string? hex) => hex is null ? string.Empty : "#" + hex.ToLowerInvariant();

    private static bool IsHexColor(string? v) => v is { Length: 6 } && System.Linq.Enumerable.All(v, char.IsAsciiHexDigit);

    private string TextAttrs(MarkupNode rPr)
    {
        if (rPr.IsNull)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        void Attr(string name, string value) => sb.Append(' ').Append(name).Append("=\"").Append(Esc(value)).Append('"');

        foreach (MarkupNode e in rPr.ChildElements)
        {
            if (e.NamespaceUri is not (W or WStrict))
            {
                continue;
            }

            string? val = A(e, "val");
            switch (e.LocalNameString)
            {
                case "b": Attr("fo:font-weight", On(e) ? "bold" : "normal"); Attr("style:font-weight-asian", On(e) ? "bold" : "normal"); break;
                case "bCs": Attr("style:font-weight-complex", On(e) ? "bold" : "normal"); break;
                case "i": Attr("fo:font-style", On(e) ? "italic" : "normal"); Attr("style:font-style-asian", On(e) ? "italic" : "normal"); break;
                case "iCs": Attr("style:font-style-complex", On(e) ? "italic" : "normal"); break;
                case "caps": Attr("fo:text-transform", On(e) ? "uppercase" : "none"); break;
                case "smallCaps": Attr("fo:font-variant", On(e) ? "small-caps" : "normal"); break;
                case "strike": if (On(e)) { Attr("style:text-line-through-style", "solid"); Attr("style:text-line-through-type", "single"); } else { Attr("style:text-line-through-style", "none"); } break;
                case "dstrike": if (On(e)) { Attr("style:text-line-through-style", "solid"); Attr("style:text-line-through-type", "double"); } break;
                case "vanish": if (On(e)) { Attr("text:display", "none"); } break;
                case "outline": if (On(e)) { Attr("style:text-outline", "true"); } break;
                case "shadow": if (On(e)) { Attr("fo:text-shadow", "1pt 1pt"); } break;
                case "emboss": if (On(e)) { Attr("style:font-relief", "embossed"); } break;
                case "imprint": if (On(e)) { Attr("style:font-relief", "engraved"); } break;
                case "color":
                    if (IsHexColor(val))
                    {
                        Attr("fo:color", Lower(val));
                    }
                    else if (val == "auto")
                    {
                        Attr("style:use-window-font-color", "true");
                    }

                    break;
                case "sz":
                    if (Int(val, -1) > 0)
                    {
                        Attr("fo:font-size", OdfUnits.Pt(Int(val) / 2.0));
                        Attr("style:font-size-asian", OdfUnits.Pt(Int(val) / 2.0));
                    }

                    break;
                case "szCs":
                    if (Int(val, -1) > 0)
                    {
                        Attr("style:font-size-complex", OdfUnits.Pt(Int(val) / 2.0));
                    }

                    break;
                case "rFonts":
                {
                    string? latin = A(e, "ascii") ?? A(e, "hAnsi");
                    if (latin is { Length: > 0 })
                    {
                        Attr("style:font-name", UseFont(latin));
                    }

                    if (A(e, "eastAsia") is { Length: > 0 } ea)
                    {
                        Attr("style:font-name-asian", UseFont(ea));
                    }

                    if (A(e, "cs") is { Length: > 0 } cs)
                    {
                        Attr("style:font-name-complex", UseFont(cs));
                    }

                    break;
                }

                case "vertAlign":
                    Attr("style:text-position", val switch { "superscript" => "super 58%", "subscript" => "sub 58%", _ => "0% 100%" });
                    break;
                case "spacing":
                    if (val is not null && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sp))
                    {
                        Attr("fo:letter-spacing", OdfUnits.Pt(sp / 20.0));
                    }

                    break;
                case "w":
                    if (Int(val, 0) > 0)
                    {
                        Attr("style:text-scale", Int(val) + "%");
                    }

                    break;
                case "u":
                    if (val is null or "none")
                    {
                        Attr("style:text-underline-style", "none");
                        break;
                    }

                    (string style, string type, bool bold) = val switch
                    {
                        "double" => ("solid", "double", false),
                        "thick" => ("solid", "single", true),
                        "dotted" => ("dotted", "single", false),
                        "dottedHeavy" => ("dotted", "single", true),
                        "dash" or "dashedHeavy" => ("dash", "single", val == "dashedHeavy"),
                        "dashLong" or "dashLongHeavy" => ("long-dash", "single", val == "dashLongHeavy"),
                        "dotDash" or "dashDotHeavy" => ("dot-dash", "single", val == "dashDotHeavy"),
                        "dotDotDash" or "dashDotDotHeavy" => ("dot-dot-dash", "single", val == "dashDotDotHeavy"),
                        "wave" or "wavyHeavy" => ("wave", "single", val == "wavyHeavy"),
                        "wavyDouble" => ("wave", "double", false),
                        _ => ("solid", "single", false),
                    };
                    Attr("style:text-underline-style", style);
                    Attr("style:text-underline-type", type);
                    if (bold)
                    {
                        Attr("style:text-underline-width", "bold");
                    }

                    if (val == "words")
                    {
                        Attr("style:text-underline-mode", "skip-white-space");
                    }

                    Attr("style:text-underline-color", IsHexColor(A(e, "color")) ? Lower(A(e, "color")) : "font-color");
                    break;
                case "highlight":
                    if (val is not null && HighlightColour(val) is { } hc)
                    {
                        Attr("fo:background-color", hc);
                    }

                    break;
                case "shd":
                    if (IsHexColor(A(e, "fill")) && Child(rPr, "highlight").IsNull)
                    {
                        Attr("fo:background-color", Lower(A(e, "fill")));
                    }

                    break;
                case "lang":
                {
                    SplitLang(A(e, "val"), out string? l, out string? c);
                    if (l is not null)
                    {
                        Attr("fo:language", l);
                        if (c is not null)
                        {
                            Attr("fo:country", c);
                        }
                    }

                    SplitLang(A(e, "eastAsia"), out string? la, out string? ca);
                    if (la is not null)
                    {
                        Attr("style:language-asian", la);
                        if (ca is not null)
                        {
                            Attr("style:country-asian", ca);
                        }
                    }

                    SplitLang(A(e, "bidi"), out string? lc, out string? cc);
                    if (lc is not null)
                    {
                        Attr("style:language-complex", lc);
                        if (cc is not null)
                        {
                            Attr("style:country-complex", cc);
                        }
                    }

                    break;
                }
            }
        }

        return sb.ToString();
    }

    private static void SplitLang(string? tag, out string? language, out string? country)
    {
        language = country = null;
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        int dash = tag.IndexOf('-', StringComparison.Ordinal);
        language = dash < 0 ? tag : tag[..dash];
        if (dash >= 0)
        {
            string rest = tag[(dash + 1)..];
            int d2 = rest.IndexOf('-', StringComparison.Ordinal);
            string c = d2 < 0 ? rest : rest[..d2];
            if (c.Length == 2)
            {
                country = c;
            }
        }
    }

    // ---- paragraph properties --------------------------------------------------------------------------------------------------

    private static string BorderValue(MarkupNode b)
    {
        string? val = A(b, "val");
        if (val is null or "nil" or "none")
        {
            return "none";
        }

        double pt = Math.Clamp(Int(A(b, "sz"), 4) / 8.0, 0.25, 12);
        string style = val switch { "dotted" => "dotted", "dashed" or "dashSmallGap" => "dashed", "double" => "double", "threeDEngrave" => "groove", "threeDEmboss" => "ridge", "inset" => "inset", "outset" => "outset", _ => "solid" };
        string color = IsHexColor(A(b, "color")) ? Lower(A(b, "color")) : "#000000";
        return OdfUnits.Pt(pt) + " " + style + " " + color;
    }

    private static string ParaAttrs(MarkupNode pPr, out string children)
    {
        children = string.Empty;
        if (pPr.IsNull)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        void Attr(string name, string value) => sb.Append(' ').Append(name).Append("=\"").Append(Esc(value)).Append('"');
        bool rtl = OnChild(pPr, "bidi");
        foreach (MarkupNode e in pPr.ChildElements)
        {
            if (e.NamespaceUri is not (W or WStrict))
            {
                continue;
            }

            string? val = A(e, "val");
            switch (e.LocalNameString)
            {
                case "jc":
                    Attr("fo:text-align", val switch
                    {
                        "center" => "center",
                        "both" or "distribute" or "thaiDistribute" or "lowKashida" or "mediumKashida" or "highKashida" => "justify",
                        "right" => rtl ? "start" : "end",
                        "end" => "end",
                        "left" => rtl ? "end" : "start",
                        _ => "start",
                    });
                    break;
                case "ind":
                {
                    int left = Int(A(e, "left") ?? A(e, "start"));
                    int right = Int(A(e, "right") ?? A(e, "end"));
                    Attr("fo:margin-left", Cm(left));
                    Attr("fo:margin-right", Cm(right));
                    int hanging = Int(A(e, "hanging"));
                    int first = Int(A(e, "firstLine"));
                    Attr("fo:text-indent", Cm(hanging > 0 ? -hanging : first));
                    break;
                }

                case "spacing":
                {
                    if (A(e, "before") is { } b)
                    {
                        Attr("fo:margin-top", Cm(Int(b)));
                    }

                    if (A(e, "after") is { } a)
                    {
                        Attr("fo:margin-bottom", Cm(Int(a)));
                    }

                    if (A(e, "line") is { } l && Int(l, 0) > 0)
                    {
                        string rule = A(e, "lineRule") ?? "auto";
                        if (rule == "auto")
                        {
                            Attr("fo:line-height", (Int(l) * 100 / 240).ToString(CultureInfo.InvariantCulture) + "%");
                        }
                        else if (rule == "atLeast")
                        {
                            Attr("style:line-height-at-least", Cm(Int(l)));
                        }
                        else
                        {
                            Attr("fo:line-height", Cm(Int(l)));
                        }
                    }

                    break;
                }

                case "keepNext": Attr("fo:keep-with-next", On(e) ? "always" : "auto"); break;
                case "keepLines": Attr("fo:keep-together", On(e) ? "always" : "auto"); break;
                case "pageBreakBefore": if (On(e)) { Attr("fo:break-before", "page"); } break;
                case "widowControl": Attr("fo:widows", On(e) ? "2" : "0"); Attr("fo:orphans", On(e) ? "2" : "0"); break;
                case "contextualSpacing": if (On(e)) { Attr("style:contextual-spacing", "true"); } break;
                case "bidi": if (On(e)) { Attr("style:writing-mode", "rl-tb"); } break;
                case "shd":
                    if (IsHexColor(A(e, "fill")))
                    {
                        Attr("fo:background-color", Lower(A(e, "fill")));
                    }

                    break;
                case "pBdr":
                    foreach (MarkupNode side in e.ChildElements)
                    {
                        string? name = side.LocalNameString switch { "top" => "top", "bottom" => "bottom", "left" or "start" => "left", "right" or "end" => "right", _ => null };
                        if (name is null)
                        {
                            continue;
                        }

                        Attr("fo:border-" + name, BorderValue(side));
                        if (A(side, "space") is { } space && Int(space, 0) > 0)
                        {
                            Attr("fo:padding-" + name, OdfUnits.Pt(Int(space)));
                        }
                    }

                    break;
                case "tabs":
                {
                    var tabs = new StringBuilder("<style:tab-stops>");
                    foreach (MarkupNode t in e.ChildElements)
                    {
                        if (!IsW(t, "tab") || A(t, "val") is "clear")
                        {
                            continue;
                        }

                        string type = A(t, "val") switch { "center" => "center", "right" => "right", "decimal" => "char", _ => "left" };
                        string leader = A(t, "leader") switch { "dot" or "middleDot" => ".", "hyphen" => "-", "underscore" => "_", _ => string.Empty };
                        tabs.Append("<style:tab-stop style:position=\"").Append(Cm(Int(A(t, "pos")))).Append("\" style:type=\"").Append(type).Append('"');
                        if (type == "char")
                        {
                            tabs.Append(" style:char=\".\"");
                        }

                        if (leader.Length > 0)
                        {
                            tabs.Append(" style:leader-style=\"solid\" style:leader-text=\"").Append(leader).Append('"');
                        }

                        tabs.Append("/>");
                    }

                    tabs.Append("</style:tab-stops>");
                    if (tabs.Length > "<style:tab-stops></style:tab-stops>".Length)
                    {
                        children = tabs.ToString();
                    }

                    break;
                }
            }
        }

        return sb.ToString();
    }

    private static string TextPropsXml(string attrs) => attrs.Length == 0 ? string.Empty : "<style:text-properties" + attrs + "/>";

    private static string ParaPropsXml(string attrs, string children) =>
        attrs.Length == 0 && children.Length == 0 ? string.Empty : "<style:paragraph-properties" + attrs + (children.Length == 0 ? "/>" : ">" + children + "</style:paragraph-properties>");

    // ---- styles.xml ---------------------------------------------------------------------------------------------------------------

    private string BuildStyles()
    {
        _auto = _stylesAuto;
        var inner = new StringBuilder();

        // docDefaults -> the default styles of the paragraph and character families.
        MarkupNode defaults = _stylesRoot.IsNull ? default : Child(_stylesRoot, "docDefaults");
        string dText = TextAttrs(Child(Child(defaults, "rPrDefault"), "rPr"));
        string dParaAttrs = ParaAttrs(Child(Child(defaults, "pPrDefault"), "pPr"), out string dParaChildren);
        if (dText.Length > 0 || dParaAttrs.Length > 0 || dParaChildren.Length > 0)
        {
            inner.Append("<style:default-style style:family=\"paragraph\">").Append(ParaPropsXml(dParaAttrs, dParaChildren)).Append(TextPropsXml(dText)).Append("</style:default-style>");
        }

        bool haveStandard = _defaultParagraph is not null;
        foreach (StyleDef d in _styleList)
        {
            if (d.Type is not ("paragraph" or "character"))
            {
                continue;
            }

            EmitCommonStyle(inner, d);
        }

        inner.Append("<style:style style:name=\"Graphics\" style:family=\"graphic\"><style:graphic-properties text:anchor-type=\"paragraph\" svg:x=\"0cm\" svg:y=\"0cm\" style:wrap=\"dynamic\" style:number-wrapped-paragraphs=\"no-limit\" style:wrap-contour=\"false\" style:vertical-pos=\"top\" style:vertical-rel=\"paragraph\" style:horizontal-pos=\"center\" style:horizontal-rel=\"paragraph\"/></style:style>");
        if (!haveStandard)
        {
            inner.Append("<style:style style:name=\"Standard\" style:family=\"paragraph\" style:class=\"text\"/>");
        }

        string listStyles = BuildListStyles();
        inner.Append(listStyles);

        string masters = BuildMasters(out string layouts);
        var sb = new StringBuilder(XmlDecl);
        sb.Append("<office:document-styles").Append(Namespaces).Append(" office:version=\"1.3\">");
        sb.Append("<office:font-face-decls>").Append(FontDecls()).Append("</office:font-face-decls>");
        sb.Append("<office:styles>").Append(inner).Append("</office:styles>");
        sb.Append("<office:automatic-styles>").Append(layouts).Append(_stylesAuto.Xml).Append("</office:automatic-styles>");
        sb.Append("<office:master-styles>").Append(masters).Append("</office:master-styles>");
        sb.Append("</office:document-styles>");
        _auto = _contentAuto;
        return sb.ToString();
    }

    private void EmitCommonStyle(StringBuilder sb, StyleDef d)
    {
        bool para = d.Type == "paragraph";
        sb.Append("<style:style style:name=\"").Append(Esc(d.OdfName)).Append("\" style:family=\"").Append(para ? "paragraph" : "text").Append('"');
        if (d.DisplayName != d.OdfName && d.DisplayName != "Standard")
        {
            sb.Append(" style:display-name=\"").Append(Esc(d.DisplayName)).Append('"');
        }

        if (d.BasedOn is not null && OdfStyleName(d.BasedOn, d.Type) is { } parent)
        {
            sb.Append(" style:parent-style-name=\"").Append(Esc(parent)).Append('"');
        }

        if (para && d.Next is not null && OdfStyleName(d.Next, "paragraph") is { } next && next != d.OdfName)
        {
            sb.Append(" style:next-style-name=\"").Append(Esc(next)).Append('"');
        }

        MarkupNode pPr = Child(d.Node, "pPr");
        if (para)
        {
            int outline = EffectiveOutline(null, pPr);
            if (outline > 0)
            {
                sb.Append(" style:default-outline-level=\"").Append(OdfUnits.I(outline)).Append('"');
            }

            // A style's numbering becomes the style's list style.
            MarkupNode numPr = Child(pPr, "numPr");
            if (!numPr.IsNull && Int(A(Child(numPr, "numId"), "val")) is int numId and > 0)
            {
                sb.Append(" style:list-style-name=\"").Append(ListStyleName(numId)).Append('"');
            }
        }

        sb.Append(para ? " style:class=\"text\">" : ">");
        if (para)
        {
            string attrs = ParaAttrs(pPr, out string children);
            sb.Append(ParaPropsXml(attrs, children));
        }

        sb.Append(TextPropsXml(TextAttrs(Child(d.Node, "rPr"))));
        sb.Append("</style:style>");
    }
}
