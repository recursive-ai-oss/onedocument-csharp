using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Styles: common paragraph and character styles become w:style elements; an automatic style (direct formatting) is resolved onto its
// nearest common ancestor, whose id goes in w:pStyle / w:rStyle, with the automatic properties written as direct formatting.
internal sealed partial class OdtImporter
{
    private readonly Dictionary<(string Family, string Name), (string Id, string Name)> _styleIds = [];
    private readonly HashSet<string> _usedIds = new(StringComparer.OrdinalIgnoreCase);
    private double _baseHalfPoints = 24;

    private sealed class ResolvedParagraph
    {
        public string? StyleId;
        public PropBag PPr = new();
        public PropBag RPr = new();
        public string? DirectListStyle;
        public string? MasterPage;
        public bool BreakBefore;
    }

    private readonly Dictionary<string, ResolvedParagraph> _paragraphCache = new(StringComparer.Ordinal);

    // Word's built-in styles have fixed ids and lower-case names; the OpenDocument style with the matching role takes them so that
    // the hub (and Word) recognise headings, notes and hyperlinks.
    private static (string Id, string Name)? BuiltIn(string family, string display)
    {
        if (family == "paragraph")
        {
            switch (display)
            {
                case "Standard": return ("Normal", "Normal");
                case "Title": return ("Title", "Title");
                case "Subtitle": return ("Subtitle", "Subtitle");
                case "Header": return ("Header", "header");
                case "Footer": return ("Footer", "footer");
                case "Footnote": return ("FootnoteText", "footnote text");
                case "Endnote": return ("EndnoteText", "endnote text");
                case "Text body": case "Body Text": return ("BodyText", "Body Text");
                case "Caption": return ("Caption", "caption");
                case "Contents Heading": return ("TOCHeading", "TOC Heading");
                case "List Paragraph": return ("ListParagraph", "List Paragraph");
                case "Quotations": return ("Quote", "Quote");
            }

            if (display.StartsWith("Heading ", StringComparison.Ordinal) && display.Length == 9 && display[8] is >= '1' and <= '9')
            {
                return ("Heading" + display[8], "heading " + display[8]);
            }

            if (display.StartsWith("Contents ", StringComparison.Ordinal) && display.Length == 10 && display[9] is >= '1' and <= '9')
            {
                return ("TOC" + display[9], "toc " + display[9]);
            }
        }
        else if (family == "text")
        {
            switch (display)
            {
                case "Internet link": return ("Hyperlink", "Hyperlink");
                case "Footnote Characters": case "Footnote anchor": return ("FootnoteReference", "footnote reference");
                case "Endnote Characters": case "Endnote anchor": return ("EndnoteReference", "endnote reference");
                case "Strong Emphasis": return ("Strong", "Strong");
                case "Emphasis": return ("Emphasis", "Emphasis");
                case "Page Number": return ("PageNumber", "page number");
            }
        }

        return null;
    }

    private void PrepareStyleIds()
    {
        OdfStyle? def = _styles.GetDefault("paragraph");
        if (def is not null && OdfUnits.TryPoints(OdfMarkup.Attribute(def.Properties("text-properties"), OdfNamespaces.Fo, "font-size"), out double pts) && pts > 0)
        {
            _baseHalfPoints = Math.Round(pts * 2);
        }

        // Built-in ids are reserved first so that an unrelated style cannot take one.
        foreach (OdfStyle s in _styles.All)
        {
            if (s.IsAutomatic || s.IsDefault || s.Family is not ("paragraph" or "text"))
            {
                continue;
            }

            if (BuiltIn(s.Family, s.DisplayName) is { } b && _usedIds.Add(b.Id))
            {
                _styleIds[(s.Family, s.Name)] = b;
            }
        }

        foreach (OdfStyle s in _styles.All)
        {
            if (s.IsAutomatic || s.IsDefault || s.Family is not ("paragraph" or "text") || _styleIds.ContainsKey((s.Family, s.Name)))
            {
                continue;
            }

            string display = s.DisplayName;
            var sb = new StringBuilder();
            foreach (char c in display)
            {
                if (char.IsAsciiLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }

            if (sb.Length == 0 || char.IsAsciiDigit(sb[0]))
            {
                sb.Insert(0, s.Family == "text" ? "Char" : "Style");
            }

            string id = sb.ToString();
            string unique = id;
            for (int n = 2; !_usedIds.Add(unique); n++)
            {
                unique = id + n.ToString(CultureInfo.InvariantCulture);
            }

            _styleIds[(s.Family, s.Name)] = (unique, display);
        }
    }

    private string? StyleIdOf(string family, string? name) =>
        name is not null && _styleIds.TryGetValue((family, name), out (string Id, string Name) v) ? v.Id : null;

    private ResolvedParagraph ResolveParagraph(string? styleName)
    {
        string key = styleName ?? string.Empty;
        if (_paragraphCache.TryGetValue(key, out ResolvedParagraph? cached))
        {
            return cached;
        }

        var r = new ResolvedParagraph();
        OdfStyle? style = styleName is null ? null : _styles.Find("paragraph", styleName);
        if (style is not null)
        {
            var autos = new List<OdfStyle>();
            foreach (OdfStyle s in _styles.Chain(style))
            {
                if (s.IsDefault)
                {
                    break;
                }

                if (!s.IsAutomatic)
                {
                    r.StyleId = StyleIdOf("paragraph", s.Name);
                    if (r.MasterPage is null)
                    {
                        r.MasterPage = s.MasterPageName;
                    }

                    break;
                }

                autos.Add(s);
                r.MasterPage ??= s.MasterPageName;
            }

            // A master page on an inherited style also starts a new page.
            if (r.MasterPage is null)
            {
                foreach (OdfStyle s in _styles.Chain(style))
                {
                    if (s.MasterPageName is { Length: > 0 } mp)
                    {
                        r.MasterPage = mp;
                        break;
                    }
                }
            }

            for (int i = autos.Count - 1; i >= 0; i--)
            {
                OdfStyle a = autos[i];
                r.PPr.Merge(ParaProps(a.Properties("paragraph-properties"), inStyle: false));
                r.RPr.Merge(RunProps(a.Properties("text-properties"), _baseHalfPoints));
                if (a.ListStyleName is { Length: > 0 } ls)
                {
                    r.DirectListStyle = ls;
                }
            }

            if (r.PPr.Get("pageBreakBefore") is { } pb && !pb.Contains("w:val=\"0\"", StringComparison.Ordinal))
            {
                r.BreakBefore = true;
            }
        }

        _paragraphCache[key] = r;
        return r;
    }

    // A text style used by a span: the common character style it descends from and the direct formatting of the automatic ones.
    private (string? StyleId, PropBag Direct) ResolveSpan(string? styleName)
    {
        var direct = new PropBag();
        if (styleName is null)
        {
            return (null, direct);
        }

        OdfStyle? style = _styles.Find("text", styleName);
        if (style is null)
        {
            return (null, direct);
        }

        string? id = null;
        var autos = new List<OdfStyle>();
        foreach (OdfStyle s in _styles.Chain(style))
        {
            if (s.IsDefault)
            {
                break;
            }

            if (!s.IsAutomatic)
            {
                id = StyleIdOf("text", s.Name);
                break;
            }

            autos.Add(s);
        }

        for (int i = autos.Count - 1; i >= 0; i--)
        {
            direct.Merge(RunProps(autos[i].Properties("text-properties"), _baseHalfPoints));
        }

        return (id, direct);
    }

    // ---- styles.xml -----------------------------------------------------------------------------------------------------------

    private Story BuildStyles()
    {
        var story = new Story("/word/styles.xml");
        story.Begin("w:styles");
        XmlWriter w = story.W;

        OdfStyle? defPara = _styles.GetDefault("paragraph");
        PropBag defR = defPara is null ? new PropBag() : RunProps(defPara.Properties("text-properties"), _baseHalfPoints);
        PropBag defP = defPara is null ? new PropBag() : ParaProps(defPara.Properties("paragraph-properties"), inStyle: true);
        if (!defR.IsEmpty || !defP.IsEmpty)
        {
            story.Raw("<w:docDefaults>");
            if (!defR.IsEmpty)
            {
                story.Raw("<w:rPrDefault><w:rPr>" + defR.ToXml(WordOrder.RPr) + "</w:rPr></w:rPrDefault>");
            }

            if (!defP.IsEmpty)
            {
                story.Raw("<w:pPrDefault><w:pPr>" + defP.ToXml(WordOrder.PPr) + "</w:pPr></w:pPrDefault>");
            }

            story.Raw("</w:docDefaults>");
        }

        bool hasNormal = false;
        foreach (OdfStyle s in _styles.All)
        {
            if (s.IsAutomatic || s.IsDefault || s.Family is not ("paragraph" or "text") || !_styleIds.TryGetValue((s.Family, s.Name), out (string Id, string Name) ids))
            {
                continue;
            }

            hasNormal |= ids.Id == "Normal";
            EmitStyle(story, s, ids);
        }

        if (!hasNormal)
        {
            story.Raw("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
        }

        // The note and hyperlink styles the converted markup refers to, when the file defines no style for the role.
        if (!_usedIds.Contains("FootnoteReference"))
        {
            story.Raw("<w:style w:type=\"character\" w:styleId=\"FootnoteReference\"><w:name w:val=\"footnote reference\"/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>");
        }

        if (!_usedIds.Contains("EndnoteReference"))
        {
            story.Raw("<w:style w:type=\"character\" w:styleId=\"EndnoteReference\"><w:name w:val=\"endnote reference\"/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>");
        }

        if (!_usedIds.Contains("Hyperlink"))
        {
            story.Raw("<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:rPr><w:color w:val=\"0563C1\"/><w:u w:val=\"single\"/></w:rPr></w:style>");
        }

        _ = w;
        story.End();
        return story;
    }

    private void EmitStyle(Story story, OdfStyle s, (string Id, string Name) ids)
    {
        bool para = s.Family == "paragraph";
        var sb = new StringBuilder();
        sb.Append("<w:style w:type=\"").Append(para ? "paragraph" : "character").Append('"');
        if (ids.Id == "Normal")
        {
            sb.Append(" w:default=\"1\"");
        }

        sb.Append(" w:styleId=\"").Append(OdfUnits.Esc(ids.Id)).Append("\"><w:name w:val=\"").Append(OdfUnits.Esc(ids.Name)).Append("\"/>");
        string? parent = s.ParentName;
        if (parent is not null && StyleIdOf(s.Family, parent) is { } pid && pid != ids.Id)
        {
            sb.Append("<w:basedOn w:val=\"").Append(OdfUnits.Esc(pid)).Append("\"/>");
        }

        if (para && s.NextName is { } next && StyleIdOf("paragraph", next) is { } nid)
        {
            sb.Append("<w:next w:val=\"").Append(OdfUnits.Esc(nid)).Append("\"/>");
        }

        sb.Append("<w:qFormat/>");
        if (para)
        {
            PropBag ppr = ParaProps(s.Properties("paragraph-properties"), inStyle: true);
            int outline = s.DefaultOutlineLevel;
            string? ls = s.ListStyleName;
            if (ls is { Length: > 0 } && NumIdForStyle(ls) is { } numId)
            {
                int ilvl = outline > 0 ? Math.Min(outline - 1, 8) : 0;
                ppr.Set("numPr", "<w:numPr><w:ilvl w:val=\"" + OdfUnits.I(ilvl) + "\"/><w:numId w:val=\"" + OdfUnits.I(numId) + "\"/></w:numPr>");
            }

            if (outline is >= 1 and <= 10)
            {
                ppr.Set("outlineLvl", "<w:outlineLvl w:val=\"" + OdfUnits.I(outline - 1) + "\"/>");
            }

            if (!ppr.IsEmpty)
            {
                sb.Append("<w:pPr>").Append(ppr.ToXml(WordOrder.PPr)).Append("</w:pPr>");
            }
        }

        PropBag rpr = RunProps(s.Properties("text-properties"), _baseHalfPoints);
        if (!rpr.IsEmpty)
        {
            sb.Append("<w:rPr>").Append(rpr.ToXml(WordOrder.RPr)).Append("</w:rPr>");
        }

        sb.Append("</w:style>");
        story.Raw(sb.ToString());
    }
}
