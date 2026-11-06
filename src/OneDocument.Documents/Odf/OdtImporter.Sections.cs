using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Page layout: a master page and its page layout become one w:sectPr (page size, margins, columns) with header and footer parts.
internal sealed partial class OdtImporter
{
    private sealed class PageInfo
    {
        public string SectPrXml = string.Empty;
        public int ContentWidth = 9638;
    }

    private readonly Dictionary<string, PageInfo> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _headerFooterByContent = new(StringComparer.Ordinal);
    private int _headerParts;
    private int _footerParts;

    private string SectPr(string? master, bool continuous, bool final = false)
    {
        _ = continuous;
        _ = final;
        return GetPage(master).SectPrXml;
    }

    private int PageContentWidth(string? master) => GetPage(master).ContentWidth;

    private PageInfo GetPage(string? master)
    {
        string key = master ?? string.Empty;
        if (_pages.TryGetValue(key, out PageInfo? cached))
        {
            return cached;
        }

        var info = new PageInfo();
        _pages[key] = info;
        OdfStyle? mp = master is null ? null : _styles.Find("master-page", master);
        OdfStyle? layout = mp is null ? null : _styles.Find("page-layout", mp.Element.GetAttribute(OdfNamespaces.Style, "page-layout-name") ?? string.Empty);
        MarkupNode lp = layout is null ? default : layout.Properties("page-layout-properties");

        // A4 and no margins when the file names no layout (Part 1: the page layout properties have no defaults of their own).
        int pw = OdfUnits.TryTwips(OdfMarkup.Attribute(lp, OdfNamespaces.Fo, "page-width"), out int w) ? w : 11906;
        int ph = OdfUnits.TryTwips(OdfMarkup.Attribute(lp, OdfNamespaces.Fo, "page-height"), out int h) ? h : 16838;
        int mt = Margin(lp, "top"), mb = Margin(lp, "bottom"), ml = Margin(lp, "left"), mr = Margin(lp, "right");
        if (layout is null)
        {
            mt = mb = ml = mr = 1440;
        }

        var sb = new StringBuilder("<w:sectPr>");
        if (mp is not null)
        {
            (string? hd, string? hf, string? he, int headerHeight) = HeaderFooterRefs(mp, layout, header: true);
            (string? fd, string? ff, string? fe, int footerHeight) = HeaderFooterRefs(mp, layout, header: false);
            foreach ((string type, string? rid) in new[] { ("default", hd), ("first", hf), ("even", he) })
            {
                if (rid is not null)
                {
                    sb.Append("<w:headerReference w:type=\"").Append(type).Append("\" r:id=\"").Append(rid).Append("\"/>");
                }
            }

            foreach ((string type, string? rid) in new[] { ("default", fd), ("first", ff), ("even", fe) })
            {
                if (rid is not null)
                {
                    sb.Append("<w:footerReference w:type=\"").Append(type).Append("\" r:id=\"").Append(rid).Append("\"/>");
                }
            }

            int headerDistance = mt, footerDistance = mb;
            if (hd is not null || hf is not null || he is not null)
            {
                mt += headerHeight;
            }

            if (fd is not null || ff is not null || fe is not null)
            {
                mb += footerHeight;
            }

            bool landscape = OdfMarkup.Attribute(lp, OdfNamespaces.Style, "print-orientation") == "landscape";
            AppendPage(sb, pw, ph, landscape, mt, mb, ml, mr, headerDistance, footerDistance);
            if (hf is not null || ff is not null)
            {
                _titlePg.Add(key);
            }
        }
        else
        {
            AppendPage(sb, pw, ph, false, mt, mb, ml, mr, 720, 720);
        }

        string? numFmt = OdfMarkup.Attribute(lp, OdfNamespaces.Style, "num-format");
        if (numFmt is not null && NumFormat(numFmt) is var nf && nf != "decimal" && nf != "none")
        {
            sb.Append("<w:pgNumType w:fmt=\"").Append(nf).Append("\"/>");
        }

        MarkupNode cols = OdfMarkup.Child(lp, OdfNamespaces.Style, "columns");
        if (!cols.IsNull && ParseInt(A(cols, OdfNamespaces.Fo, "column-count"), 1) > 1)
        {
            int n = Math.Min(ParseInt(A(cols, OdfNamespaces.Fo, "column-count"), 1), 45);
            int gap = OdfUnits.Twips(A(cols, OdfNamespaces.Fo, "column-gap"), 720);
            bool sep = !OdfMarkup.Child(cols, OdfNamespaces.Style, "column-sep").IsNull;
            sb.Append("<w:cols w:num=\"").Append(OdfUnits.I(n)).Append("\" w:space=\"").Append(OdfUnits.I(gap)).Append('"');
            if (sep)
            {
                sb.Append(" w:sep=\"1\"");
            }

            sb.Append("/>");
        }

        if (_titlePg.Contains(key))
        {
            sb.Append("<w:titlePg/>");
        }

        if (!OdfMarkup.Child(lp, OdfNamespaces.Style, "footnote-sep").IsNull || OdfMarkup.Attribute(lp, OdfNamespaces.Fo, "background-color") is { } bg && bg != "transparent")
        {
            if (OdfMarkup.Attribute(lp, OdfNamespaces.Fo, "background-color") is { } b2 && b2 != "transparent")
            {
                Lost("loss.page-background", "page background colour", "The page background colour was dropped.");
            }
        }

        sb.Append("</w:sectPr>");
        info.SectPrXml = sb.ToString();
        info.ContentWidth = Math.Max(720, pw - ml - mr);
        return info;
    }

    private readonly HashSet<string> _titlePg = new(StringComparer.Ordinal);

    private static int Margin(MarkupNode lp, string side)
    {
        string? v = OdfMarkup.Attribute(lp, OdfNamespaces.Fo, "margin-" + side) ?? OdfMarkup.Attribute(lp, OdfNamespaces.Fo, "margin");
        return OdfUnits.Twips(v);
    }

    private static void AppendPage(StringBuilder sb, int pw, int ph, bool landscape, int mt, int mb, int ml, int mr, int header, int footer)
    {
        sb.Append("<w:pgSz w:w=\"").Append(OdfUnits.I(pw)).Append("\" w:h=\"").Append(OdfUnits.I(ph)).Append('"');
        if (landscape)
        {
            sb.Append(" w:orient=\"landscape\"");
        }

        sb.Append("/><w:pgMar w:top=\"").Append(OdfUnits.I(mt)).Append("\" w:right=\"").Append(OdfUnits.I(mr)).Append("\" w:bottom=\"").Append(OdfUnits.I(mb))
            .Append("\" w:left=\"").Append(OdfUnits.I(ml)).Append("\" w:header=\"").Append(OdfUnits.I(header)).Append("\" w:footer=\"").Append(OdfUnits.I(footer)).Append("\" w:gutter=\"0\"/>");
    }

    // Header and footer parts of a master page: style:header (default), style:header-left (even pages) and style:header-first, and the same for footers.
    private (string? Default, string? First, string? Even, int Height) HeaderFooterRefs(OdfStyle master, OdfStyle? layout, bool header)
    {
        string prefix = header ? "header" : "footer";
        MarkupNode def = OdfMarkup.Child(master.Element, OdfNamespaces.Style, prefix);
        MarkupNode left = OdfMarkup.Child(master.Element, OdfNamespaces.Style, prefix + "-left");
        MarkupNode first = OdfMarkup.Child(master.Element, OdfNamespaces.Style, prefix + "-first");
        string? d = MakeHeaderFooter(def, header);
        string? f = MakeHeaderFooter(first, header);
        string? e = MakeHeaderFooter(left, header);
        if (e is not null)
        {
            _usesEvenOdd = true;
            if (d is null)
            {
                // Word needs the odd page's part when the even one exists.
                d = MakeHeaderFooter(default, header, empty: true);
            }
        }

        int height = 0;
        if (d is not null || f is not null || e is not null)
        {
            MarkupNode hs = layout is null ? default : layout.Properties(prefix + "-style");
            MarkupNode props = OdfMarkup.Child(hs, OdfNamespaces.Style, "header-footer-properties");
            height = OdfUnits.Twips(OdfMarkup.Attribute(props, OdfNamespaces.Fo, "min-height")) + OdfUnits.Twips(OdfMarkup.Attribute(props, OdfNamespaces.Fo, header ? "margin-bottom" : "margin-top"));
        }

        return (d, f, e, height);
    }

    private string? MakeHeaderFooter(MarkupNode hf, bool header, bool empty = false)
    {
        if (hf.IsNull && !empty)
        {
            return null;
        }

        if (!hf.IsNull && A(hf, OdfNamespaces.Style, "display") == "false")
        {
            return null;
        }

        int n = header ? ++_headerParts : ++_footerParts;
        string name = (header ? "header" : "footer") + OdfUnits.I(n) + ".xml";
        var story = new Story("/word/" + name);
        story.Begin(header ? "w:hdr" : "w:ftr");
        story.EndsWithParagraph = false;
        if (!hf.IsNull)
        {
            EmitBlocks(story, hf, null);
        }

        if (!story.EndsWithParagraph)
        {
            story.Raw("<w:p/>");
        }

        story.End();

        // Master pages that show the same header or footer share one part.
        string text = Encoding.UTF8.GetString(story.Bytes.Span);
        string kind = header ? "header" : "footer";
        if (_headerFooterByContent.TryGetValue(kind + text, out string? known))
        {
            if (header)
            {
                _headerParts--;
            }
            else
            {
                _footerParts--;
            }

            return known;
        }

        _extraParts.Add((story, CtBase + (header ? "header+xml" : "footer+xml")));
        string rid = _main.AddRel(RelBase + kind, name);
        _headerFooterByContent[kind + text] = rid;
        return rid;
    }
}
