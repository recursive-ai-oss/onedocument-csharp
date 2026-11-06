using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// Text bodies: paragraphs, lists, runs, fields and links.
internal sealed partial class OdpImporter
{
    private const string FieldGuid = "{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}";

    private enum SegmentKind
    {
        Text,
        Break,
        SlideNumber,
        Date,
    }

    private sealed class Segment
    {
        public SegmentKind Kind;
        public string Text = "";
        public StyleCascade Cascade = null!;
        public string? Link;
    }

    private sealed class ParagraphState
    {
        public readonly List<Segment> Segments = [];
        public bool AtStart = true;
        public bool PendingSpace;
        public string? FooterText;
    }

    private readonly record struct BulletInfo(string Xml, long MarL, long Indent, bool Geometry);

    // ---- text bodies ----------------------------------------------------------------------------------------------------------

    // ECMA-376 Part 1 §21.1.2.1.1 (a:bodyPr): insets in EMU, the anchor, wrapping and the autofit mode.
    private void WriteTextBody(Scope scope, StringBuilder sb, MarkupNode container, ShapeStyle style, string tag, bool placeholder, string? footerText = null)
    {
        StyleCascade g = style.Shape;
        sb.Append('<').Append(tag).Append("><a:bodyPr");
        if (g.Graphic(OdfNamespaces.Fo, "wrap-option") == "no-wrap")
        {
            sb.Append(" wrap=\"none\"");
        }

        long Pad(string side) => OdpUnits.Emu(g.Graphic(OdfNamespaces.Fo, "padding-" + side) ?? g.Graphic(OdfNamespaces.Fo, "padding"), 0);
        sb.Append(" lIns=\"").Append(Pad("left")).Append("\" tIns=\"").Append(Pad("top")).Append("\" rIns=\"").Append(Pad("right")).Append("\" bIns=\"").Append(Pad("bottom")).Append('"');
        sb.Append(g.Graphic(OdfNamespaces.Draw, "textarea-vertical-align") switch { "middle" => " anchor=\"ctr\"", "bottom" => " anchor=\"b\"", "justify" => " anchor=\"just\"", _ => " anchor=\"t\"" });
        if (g.Graphic(OdfNamespaces.Draw, "textarea-horizontal-align") == "center")
        {
            sb.Append(" anchorCtr=\"1\"");
        }

        string? writing = style.Text.Para(OdfNamespaces.Style, "writing-mode");
        if (writing is "tb-rl" or "tb-lr")
        {
            sb.Append(" vert=\"eaVert\"");
        }

        sb.Append(" rtlCol=\"0\">");
        if (g.Graphic(OdfNamespaces.Style, "shrink-to-fit") == "true")
        {
            sb.Append("<a:normAutofit/>");
        }
        else if (g.Graphic(OdfNamespaces.Draw, "auto-grow-height") == "true")
        {
            sb.Append("<a:spAutoFit/>");
        }
        else
        {
            sb.Append("<a:noAutofit/>");
        }

        sb.Append("</a:bodyPr><a:lstStyle/>");
        MarkupNode shapeList = style.Presentation is null ? default : OdfMarkup.Child(style.Presentation.Properties("graphic-properties"), OdfNamespaces.Text, "list-style");
        if (shapeList.IsNull)
        {
            shapeList = g.FindChild("graphic-properties", OdfNamespaces.Text, "list-style");
        }

        bool wrote = false;
        WriteParagraphs(scope, sb, container, style, shapeList, 0, ref wrote, footerText);
        if (!wrote && (placeholder || true))
        {
            // DrawingML wants at least one paragraph in a text body.
            sb.Append("<a:p>");
            if (footerText is { Length: > 0 })
            {
                sb.Append("<a:r><a:rPr lang=\"en-US\"/><a:t>").Append(OdpUnits.Esc(footerText)).Append("</a:t></a:r>");
            }

            sb.Append("<a:endParaRPr lang=\"en-US\"/></a:p>");
        }

        sb.Append("</").Append(tag).Append('>');
    }

    private void WriteParagraphs(Scope scope, StringBuilder sb, MarkupNode container, ShapeStyle style, MarkupNode listStyle, int level, ref bool wrote, string? footerText)
    {
        _depth++;
        try
        {
            CheckDepth(container);
            foreach (MarkupNode c in container.ChildElements)
            {
                if (c.NamespaceUri != OdfNamespaces.Text)
                {
                    continue;
                }

                switch (c.LocalNameString)
                {
                    case "p" or "h":
                        WriteParagraph(scope, sb, c, style, listStyle, level, footerText);
                        wrote = true;
                        break;
                    case "list":
                        {
                            MarkupNode ls = A(c, OdfNamespaces.Text, "style-name") is { } n && _styles.Find("list", n) is { } found ? found.Element : listStyle;
                            foreach (MarkupNode item in c.ChildElements)
                            {
                                if (Is(item, OdfNamespaces.Text, "list-item") || Is(item, OdfNamespaces.Text, "list-header"))
                                {
                                    WriteParagraphs(scope, sb, item, style, ls, level + 1, ref wrote, footerText);
                                }
                            }

                            break;
                        }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void WriteParagraph(Scope scope, StringBuilder sb, MarkupNode p, ShapeStyle style, MarkupNode listStyle, int level, string? footerText)
    {
        _ = scope;
        OdfStyle? paraStyle = A(p, OdfNamespaces.Text, "style-name") is { } sn ? _styles.Find("paragraph", sn) : null;
        StyleCascade c = style.Text.With(paraStyle);
        bool inList = level > 0;
        var st = new ParagraphState { FooterText = footerText };
        WriteInline(p, c, null, st, scope);

        sb.Append("<a:p><a:pPr");
        long marL = OdpUnits.Emu(c.Para(OdfNamespaces.Fo, "margin-left"));
        long indent = OdpUnits.Emu(c.Para(OdfNamespaces.Fo, "text-indent"));
        BulletInfo bullet = inList ? BulletOf(listStyle, level) : new BulletInfo("<a:buNone/>", 0, 0, false);
        if (inList && bullet.Geometry)
        {
            marL += bullet.MarL;
            indent = bullet.Indent;
        }

        if (marL != 0)
        {
            sb.Append(" marL=\"").Append(Math.Clamp(marL, 0, 51206400)).Append('"');
        }

        if (indent != 0)
        {
            sb.Append(" indent=\"").Append(Math.Clamp(indent, -51206400, 51206400)).Append('"');
        }

        if (inList && level > 1)
        {
            sb.Append(" lvl=\"").Append(Math.Min(level - 1, 8)).Append('"');
        }

        string? align = c.Para(OdfNamespaces.Fo, "text-align");
        string? writing = c.Para(OdfNamespaces.Style, "writing-mode");
        bool rtl = writing is "rl-tb" or "rl";
        sb.Append(align switch
        {
            "center" => " algn=\"ctr\"",
            "end" or "right" => rtl && align == "end" ? " algn=\"l\"" : " algn=\"r\"",
            "justify" => " algn=\"just\"",
            "start" or "left" => rtl && align == "start" ? " algn=\"r\"" : " algn=\"l\"",
            _ => "",
        });
        if (rtl)
        {
            sb.Append(" rtl=\"1\"");
        }

        sb.Append('>');
        string? lh = c.Para(OdfNamespaces.Fo, "line-height");
        if (OdpUnits.Percent(lh) is { } pct && pct > 0)
        {
            sb.Append("<a:lnSpc><a:spcPct val=\"").Append(OdpUnits.I((long)Math.Round(pct * 100000))).Append("\"/></a:lnSpc>");
        }
        else if (OdpUnits.TryPoints(lh, out double fixedPt) && fixedPt > 0)
        {
            sb.Append("<a:lnSpc><a:spcPts val=\"").Append(OdpUnits.I((long)Math.Round(fixedPt * 100))).Append("\"/></a:lnSpc>");
        }

        if (OdpUnits.TryPoints(c.Para(OdfNamespaces.Fo, "margin-top"), out double before) && before >= 0)
        {
            sb.Append("<a:spcBef><a:spcPts val=\"").Append(OdpUnits.I((long)Math.Round(before * 100))).Append("\"/></a:spcBef>");
        }

        if (OdpUnits.TryPoints(c.Para(OdfNamespaces.Fo, "margin-bottom"), out double after) && after >= 0)
        {
            sb.Append("<a:spcAft><a:spcPts val=\"").Append(OdpUnits.I((long)Math.Round(after * 100))).Append("\"/></a:spcAft>");
        }

        sb.Append(bullet.Xml).Append("</a:pPr>");
        foreach (Segment s in st.Segments)
        {
            switch (s.Kind)
            {
                case SegmentKind.Text:
                    sb.Append("<a:r>").Append(RunProps(s.Cascade, "a:rPr", s.Link, scope.Part)).Append("<a:t>").Append(OdpUnits.Esc(s.Text)).Append("</a:t></a:r>");
                    break;
                case SegmentKind.Break:
                    sb.Append("<a:br>").Append(RunProps(s.Cascade, "a:rPr", null, scope.Part)).Append("</a:br>");
                    break;
                case SegmentKind.SlideNumber:
                    sb.Append("<a:fld id=\"").Append(FieldGuid).Append("\" type=\"slidenum\">").Append(RunProps(s.Cascade, "a:rPr", null, scope.Part)).Append("<a:t>").Append(OdpUnits.Esc(s.Text)).Append("</a:t></a:fld>");
                    break;
                case SegmentKind.Date:
                    sb.Append("<a:fld id=\"").Append(FieldGuid).Append("\" type=\"datetime1\">").Append(RunProps(s.Cascade, "a:rPr", null, scope.Part)).Append("<a:t>").Append(OdpUnits.Esc(s.Text)).Append("</a:t></a:fld>");
                    break;
            }
        }

        sb.Append(RunProps(c, "a:endParaRPr", null, scope.Part)).Append("</a:p>");
    }

    // ---- inline content -------------------------------------------------------------------------------------------------------

    private static void AddText(ParagraphState st, string text, StyleCascade c, string? link)
    {
        if (text.Length == 0)
        {
            return;
        }

        FlushSpace(st, c, link);
        st.AtStart = false;
        if (st.Segments.Count > 0 && st.Segments[^1] is { Kind: SegmentKind.Text } last && ReferenceEquals(last.Cascade, c) && last.Link == link)
        {
            last.Text += text;
            return;
        }

        st.Segments.Add(new Segment { Kind = SegmentKind.Text, Text = text, Cascade = c, Link = link });
    }

    private static void FlushSpace(ParagraphState st, StyleCascade c, string? link)
    {
        if (!st.PendingSpace)
        {
            return;
        }

        st.PendingSpace = false;
        if (st.Segments.Count > 0 && st.Segments[^1] is { Kind: SegmentKind.Text } last && ReferenceEquals(last.Cascade, c) && last.Link == link)
        {
            last.Text += " ";
        }
        else
        {
            st.Segments.Add(new Segment { Kind = SegmentKind.Text, Text = " ", Cascade = c, Link = link });
        }
    }

    // OpenDocument 1.3 Part 1 §6.1.2 (white space): runs of white space collapse to one space, a space at the start of a paragraph and
    // after a line break is dropped, and so is one at its end; text:s, text:tab and text:line-break are the explicit forms.
    private static void AddCollapsed(ParagraphState st, string text, StyleCascade c, string? link)
    {
        var sb = new StringBuilder();
        foreach (char ch in text)
        {
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                if (sb.Length > 0)
                {
                    AddText(st, sb.ToString(), c, link);
                    sb.Clear();
                }

                if (!st.AtStart)
                {
                    st.PendingSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
            }
        }

        if (sb.Length > 0)
        {
            AddText(st, sb.ToString(), c, link);
        }
    }

    private void WriteInline(MarkupNode parent, StyleCascade c, string? link, ParagraphState st, Scope scope)
    {
        _depth++;
        try
        {
            CheckDepth(parent);
            foreach (MarkupNode n in parent.Children)
            {
                switch (n.Kind)
                {
                    case MarkupNodeKind.Text or MarkupNodeKind.Whitespace or MarkupNodeKind.CData:
                        AddCollapsed(st, n.InnerText, c, link);
                        break;
                    case MarkupNodeKind.Element:
                        WriteInlineElement(n, c, link, st, scope);
                        break;
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void WriteInlineElement(MarkupNode n, StyleCascade c, string? link, ParagraphState st, Scope scope)
    {
        string ns = n.NamespaceUri;
        string local = n.LocalNameString;
        if (ns == OdfNamespaces.Text)
        {
            switch (local)
            {
                case "span":
                    {
                        OdfStyle? s = A(n, OdfNamespaces.Text, "style-name") is { } sn ? _styles.Find("text", sn) : null;
                        WriteInline(n, s is null ? c : c.With(s), link, st, scope);
                        break;
                    }

                case "a":
                    {
                        string? href = A(n, OdfNamespaces.XLink, "href");
                        string? target = null;
                        if (!string.IsNullOrEmpty(href))
                        {
                            if (href.StartsWith('#'))
                            {
                                Lost("loss.odp-internal-links", "links to slides", "Links to other slides or to named places in the presentation are kept as plain text.");
                            }
                            else
                            {
                                target = href;
                            }
                        }

                        OdfStyle? s = A(n, OdfNamespaces.Text, "style-name") is { } sn ? _styles.Find("text", sn) : null;
                        WriteInline(n, s is null ? c : c.With(s), target ?? link, st, scope);
                        break;
                    }

                case "s":
                    {
                        int count = int.TryParse(A(n, OdfNamespaces.Text, "c"), NumberStyles.None, CultureInfo.InvariantCulture, out int k) ? Math.Clamp(k, 1, 10000) : 1;
                        AddText(st, new string(' ', count), c, link);
                        break;
                    }

                case "tab":
                    AddText(st, "\t", c, link);
                    break;
                case "line-break":
                    st.PendingSpace = false;
                    st.Segments.Add(new Segment { Kind = SegmentKind.Break, Cascade = c });
                    st.AtStart = true;
                    break;
                case "page-number":
                    FlushSpace(st, c, link);
                    st.AtStart = false;
                    st.Segments.Add(new Segment { Kind = SegmentKind.SlideNumber, Text = "\u2039#\u203a", Cascade = c });
                    break;
                case "date" or "time":
                    FlushSpace(st, c, link);
                    st.AtStart = false;
                    st.Segments.Add(new Segment { Kind = SegmentKind.Date, Text = n.InnerText, Cascade = c });
                    break;
                case "soft-page-break" or "bookmark" or "bookmark-start" or "bookmark-end" or "reference-mark" or "reference-mark-start" or "reference-mark-end" or "tracked-changes" or "change" or "change-start" or "change-end":
                    break;
                case "note":
                    Lost("loss.odp-notes", "footnotes in slides", "Footnotes and endnotes have no place on a slide and were dropped.");
                    break;
                case "ruby":
                    {
                        MarkupNode bas = Child(n, OdfNamespaces.Text, "ruby-base");
                        WriteInline(bas.IsNull ? n : bas, c, link, st, scope);
                        Lost("loss.odp-ruby", "ruby text", "Ruby annotations were dropped; the base text is kept.");
                        break;
                    }

                default:
                    AddCollapsed(st, n.InnerText, c, link);
                    break;
            }
        }
        else if (ns == OdfNamespaces.Presentation && local is "footer" or "header" or "date-time")
        {
            AddText(st, st.FooterText ?? "", c, link);
        }
        else if (ns == OdfNamespaces.Office && local == "annotation" || ns == OdfNamespaces.Office && local == "annotation-end")
        {
            Lost("loss.odp-comments", "comments", "Comments (annotations) were dropped.");
        }
        else if (ns == OdfNamespaces.Draw)
        {
            Lost("loss.odp-inline-shapes", "shapes anchored in text", "Shapes anchored inside a paragraph were dropped.");
        }
    }

    // ---- character properties -------------------------------------------------------------------------------------------------

    // ECMA-376 Part 1 §21.1.2.3.9 (a:rPr): attributes lang, sz, b, i, u, strike, cap, spc, baseline; then fill, highlight, fonts, hyperlink.
    private string RunProps(StyleCascade c, string tag, string? link, Part part)
    {
        var sb = new StringBuilder("<").Append(tag);
        string? lang = c.Text(OdfNamespaces.Fo, "language");
        if (lang is { Length: > 0 } && lang is not ("zxx" or "none"))
        {
            string? country = c.Text(OdfNamespaces.Fo, "country");
            sb.Append(" lang=\"").Append(OdpUnits.Esc(country is { Length: > 0 } and not "none" ? lang + "-" + country : lang)).Append('"');
        }

        if (c.TextPoints("font-size") is { } size && size > 0 && size <= 4000)
        {
            sb.Append(" sz=\"").Append(OdpUnits.I((long)Math.Round(size * 100))).Append('"');
        }

        string? weight = c.Text(OdfNamespaces.Fo, "font-weight");
        if (weight is not null)
        {
            bool bold = weight == "bold" || int.TryParse(weight, NumberStyles.None, CultureInfo.InvariantCulture, out int w) && w >= 600;
            sb.Append(" b=\"").Append(bold ? '1' : '0').Append('"');
        }

        string? italic = c.Text(OdfNamespaces.Fo, "font-style");
        if (italic is not null)
        {
            sb.Append(" i=\"").Append(italic is "italic" or "oblique" ? '1' : '0').Append('"');
        }

        string? underline = c.Text(OdfNamespaces.Style, "text-underline-style");
        if (underline is not null)
        {
            sb.Append(" u=\"").Append(underline == "none" ? "none" : c.Text(OdfNamespaces.Style, "text-underline-type") == "double" ? "dbl" : "sng").Append('"');
        }

        string? strike = c.Text(OdfNamespaces.Style, "text-line-through-style");
        if (strike is not null)
        {
            sb.Append(" strike=\"").Append(strike == "none" ? "noStrike" : c.Text(OdfNamespaces.Style, "text-line-through-type") == "double" ? "dblStrike" : "sngStrike").Append('"');
        }

        if (c.Text(OdfNamespaces.Fo, "text-transform") == "uppercase")
        {
            sb.Append(" cap=\"all\"");
        }
        else if (c.Text(OdfNamespaces.Fo, "font-variant") == "small-caps")
        {
            sb.Append(" cap=\"small\"");
        }

        if (OdpUnits.TryPoints(c.Text(OdfNamespaces.Fo, "letter-spacing"), out double spacing) && spacing != 0)
        {
            sb.Append(" spc=\"").Append(OdpUnits.I((long)Math.Round(spacing * 100))).Append('"');
        }

        string? position = c.Text(OdfNamespaces.Style, "text-position");
        if (position is not null)
        {
            string first = position.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            long baseline = first switch
            {
                "super" => 30000,
                "sub" => -25000,
                _ => OdpUnits.Percent(first) is { } pc ? (pc > 0 ? 30000 : pc < 0 ? -25000 : 0) : 0,
            };
            if (baseline != 0)
            {
                sb.Append(" baseline=\"").Append(baseline).Append('"');
            }
        }

        sb.Append(" dirty=\"0\">");
        string? color = OdpUnits.Color(c.Text(OdfNamespaces.Fo, "color"));
        if (color is null && c.Text(OdfNamespaces.Style, "use-window-font-color") == "true")
        {
            color = c.AutoColor;
        }

        if (color is not null)
        {
            sb.Append("<a:solidFill>").Append(Srgb(color, OdpUnits.Percent(c.Text(OdfNamespaces.LibreOfficeExtension, "opacity")))).Append("</a:solidFill>");
        }

        if (OdpUnits.Color(c.Text(OdfNamespaces.Fo, "background-color")) is { } back)
        {
            sb.Append("<a:highlight>").Append(Srgb(back)).Append("</a:highlight>");
        }

        string? latin = FontOf(c, "font-name", "font-family");
        if (latin is not null)
        {
            sb.Append("<a:latin typeface=\"").Append(OdpUnits.Esc(latin)).Append("\"/>");
        }

        if (FontOf(c, "font-name-asian", "font-family-asian") is { } ea)
        {
            sb.Append("<a:ea typeface=\"").Append(OdpUnits.Esc(ea)).Append("\"/>");
        }

        if (FontOf(c, "font-name-complex", "font-family-complex") is { } cs)
        {
            sb.Append("<a:cs typeface=\"").Append(OdpUnits.Esc(cs)).Append("\"/>");
        }

        if (link is not null)
        {
            string rid = part.AddRel(RelBase + "hyperlink", link, external: true);
            sb.Append("<a:hlinkClick r:id=\"").Append(rid).Append("\"/>");
        }

        return sb.Append("</").Append(tag).Append('>').ToString();
    }

    private string? FontOf(StyleCascade c, string faceName, string family)
    {
        string? face = c.Text(OdfNamespaces.Style, faceName);
        if (face is not null && _fonts.TryGetValue(face, out string? mapped))
        {
            return mapped;
        }

        string? fo = c.Text(OdfNamespaces.Fo, family) ?? c.Text(OdfNamespaces.Style, family);
        return fo is null ? face : CleanFamily(fo);
    }

    // ---- bullets and numbers --------------------------------------------------------------------------------------------------

    private static MarkupNode ListStyleOf(OdfStyle? holder) =>
        holder is null ? default : OdfMarkup.Child(holder.Properties("graphic-properties"), OdfNamespaces.Text, "list-style");

    private void AppendBullet(StringBuilder sb, MarkupNode listStyle, int level0) => sb.Append(BulletOf(listStyle, level0 + 1).Xml);

    // OpenDocument 1.3 Part 1 §14.? (list level styles) to ECMA-376 Part 1 §21.1.2.4 (bullets): a character, a number or a picture.
    private BulletInfo BulletOf(MarkupNode listStyle, int level)
    {
        MarkupNode def = default;
        int best = 0;
        if (!listStyle.IsNull)
        {
            foreach (MarkupNode ls in listStyle.ChildElements)
            {
                if (ls.NamespaceUri != OdfNamespaces.Text || !ls.LocalNameString.StartsWith("list-level-style", StringComparison.Ordinal))
                {
                    continue;
                }

                int l = int.TryParse(A(ls, OdfNamespaces.Text, "level"), NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : 1;
                if (l == level)
                {
                    def = ls;
                    break;
                }

                if (l < level && l > best)
                {
                    best = l;
                    def = ls;
                }
            }
        }

        if (def.IsNull)
        {
            return new BulletInfo("<a:buFont typeface=\"Arial\"/><a:buChar char=\"\u2022\"/>", 457200L * level, -342900, true);
        }

        MarkupNode lp = Child(def, OdfNamespaces.Style, "list-level-properties");
        long marL, indent;
        MarkupNode align = Child(lp, OdfNamespaces.Style, "list-level-label-alignment");
        bool geometry = true;
        if (A(lp, OdfNamespaces.Text, "list-level-position-and-space-mode") == "label-alignment" || !align.IsNull)
        {
            marL = OdpUnits.Emu(A(align, OdfNamespaces.Fo, "margin-left"));
            indent = OdpUnits.Emu(A(align, OdfNamespaces.Fo, "text-indent"));
        }
        else
        {
            long before = OdpUnits.Emu(A(lp, OdfNamespaces.Text, "space-before"));
            long label = OdpUnits.Emu(A(lp, OdfNamespaces.Text, "min-label-width"));
            marL = before + label;
            indent = -label;
            geometry = lp.IsNull ? false : true;
        }

        var sb = new StringBuilder();
        MarkupNode tp = Child(def, OdfNamespaces.Style, "text-properties");
        if (OdpUnits.Color(A(tp, OdfNamespaces.Fo, "color")) is { } bc && A(tp, OdfNamespaces.Style, "use-window-font-color") != "true")
        {
            sb.Append("<a:buClr>").Append(Srgb(bc)).Append("</a:buClr>");
        }

        if (OdpUnits.Percent(A(tp, OdfNamespaces.Fo, "font-size")) is { } pct && pct > 0 && Math.Abs(pct - 1) > 0.001)
        {
            sb.Append("<a:buSzPct val=\"").Append(OdpUnits.I((long)Math.Round(Math.Clamp(pct, 0.25, 4) * 100000))).Append("\"/>");
        }

        string kind = def.LocalNameString;
        if (kind == "list-level-style-bullet")
        {
            string bulletChar = A(def, OdfNamespaces.Text, "bullet-char") ?? "\u2022";
            string? family = A(tp, OdfNamespaces.Fo, "font-family") ?? A(tp, OdfNamespaces.Style, "font-name");
            string font = family is null ? "Arial" : CleanFamily(family);
            char first = bulletChar.Length > 0 ? bulletChar[0] : '\u2022';
            if (first is >= '\uE000' and <= '\uF8FF' || font is "OpenSymbol" or "StarSymbol")
            {
                // The producer's own symbol font has no counterpart; its private-use bullets are shown as a round bullet.
                if (first is >= '\uE000' and <= '\uF8FF')
                {
                    first = '\u2022';
                }

                font = "Arial";
            }

            sb.Append("<a:buFont typeface=\"").Append(OdpUnits.Esc(font)).Append("\"/><a:buChar char=\"").Append(OdpUnits.Esc(first.ToString())).Append("\"/>");
        }
        else if (kind == "list-level-style-number")
        {
            string fmt = A(def, OdfNamespaces.Style, "num-format") ?? "";
            string prefix = A(def, OdfNamespaces.Style, "num-prefix") ?? "";
            string suffix = A(def, OdfNamespaces.Style, "num-suffix") ?? "";
            string? type = NumberType(fmt, prefix, suffix);
            if (type is null)
            {
                sb.Append("<a:buNone/>");
            }
            else
            {
                sb.Append("<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"").Append(type).Append('"');
                if (int.TryParse(A(def, OdfNamespaces.Text, "start-value"), NumberStyles.None, CultureInfo.InvariantCulture, out int start) && start > 1 && start < 32767)
                {
                    sb.Append(" startAt=\"").Append(start).Append('"');
                }

                sb.Append("/>");
            }
        }
        else if (kind == "list-level-style-image")
        {
            sb.Append("<a:buFont typeface=\"Arial\"/><a:buChar char=\"\u2022\"/>");
            Lost("loss.odp-picture-bullets", "picture bullets", "Picture bullets are shown as round bullets.");
        }
        else
        {
            sb.Append("<a:buNone/>");
        }

        return new BulletInfo(sb.ToString(), marL, indent, geometry);
    }

    // ECMA-376 Part 1 §20.1.10.61 (ST_TextAutonumberScheme).
    private static string? NumberType(string fmt, string prefix, string suffix)
    {
        string kind = fmt switch
        {
            "1" => "arabic",
            "a" => "alphaLc",
            "A" => "alphaUc",
            "i" => "romanLc",
            "I" => "romanUc",
            _ => "",
        };
        if (kind.Length == 0)
        {
            return null;
        }

        string tail = prefix == "(" && suffix == ")" ? "ParenBoth" : suffix == ")" ? "ParenR" : suffix == "." ? "Period" : suffix.Length == 0 ? "Plain" : "Period";
        return kind + tail;
    }
}
