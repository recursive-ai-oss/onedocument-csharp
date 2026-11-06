using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Documents.Templating;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Markdown;

internal sealed partial class MarkdownImporter
{
    private readonly record struct Fmt(bool Bold, bool Italic, bool Strike, string? RunStyle);

    // ---- inlines ------------------------------------------------------------------------------------------------------------------

    private static string Run(string text, bool bold, bool italic, bool strike, string? runStyle, bool underline = false)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var rpr = new StringBuilder();
        if (runStyle is not null)
        {
            rpr.Append("<w:rStyle w:val=\"").Append(runStyle).Append("\"/>");
        }

        if (bold)
        {
            rpr.Append("<w:b/>");
        }

        if (italic)
        {
            rpr.Append("<w:i/>");
        }

        if (strike)
        {
            rpr.Append("<w:strike/>");
        }

        if (underline)
        {
            rpr.Append("<w:u w:val=\"single\"/>");
        }

        return "<w:r>" + (rpr.Length == 0 ? string.Empty : "<w:rPr>" + rpr + "</w:rPr>") + XmlSnip.RunContent("w", text) + "</w:r>";
    }

    private void Inlines(MdNode parent, Fmt f, StringBuilder sb, Story story, int depth)
    {
        if (depth > _maxDepth)
        {
            _loss.Report("loss.markdown-nesting", "deep nesting", 1, "Content nested deeper than the recursion limit was flattened to its text.");
            sb.Append(Run(PlainText(parent), f.Bold, f.Italic, f.Strike, f.RunStyle));
            return;
        }

        for (MdNode? n = parent.FirstChild; n is not null; n = n.Next)
        {
            switch (n.Kind)
            {
                case MdKind.Text:
                    sb.Append(Run(n.Literal, f.Bold, f.Italic, f.Strike, f.RunStyle));
                    break;
                case MdKind.SoftBreak:
                    sb.Append(Run(" ", f.Bold, f.Italic, f.Strike, f.RunStyle));
                    break;
                case MdKind.LineBreak:
                    sb.Append("<w:r><w:br/></w:r>");
                    break;
                case MdKind.Code:
                    sb.Append(Run(n.Literal, f.Bold, f.Italic, f.Strike, f.RunStyle ?? "InlineCode"));
                    break;
                case MdKind.HtmlInline:
                    {
                        MdNode? used = RawHtmlInline(n, f, sb, story);
                        if (used is not null)
                        {
                            n = used;
                        }

                        break;
                    }
                case MdKind.Emph:
                    Inlines(n, f with { Italic = true }, sb, story, depth + 1);
                    break;
                case MdKind.Strong:
                    Inlines(n, f with { Bold = true }, sb, story, depth + 1);
                    break;
                case MdKind.Strike:
                    Inlines(n, f with { Strike = true }, sb, story, depth + 1);
                    break;
                case MdKind.Link:
                    LinkInline(n, f, sb, story, depth);
                    break;
                case MdKind.Image:
                    ImageInline(n, f, sb, story);
                    break;
                case MdKind.FootnoteReference:
                    FootnoteReference(n, sb);
                    break;
            }
        }
    }

    // Web, mail and in-document targets, and relative paths, become links; any other scheme (script, file, custom) is printed as text.
    internal static bool IsLinkable(string url)
    {
        if (LinkTokens.IsAllowedTarget(url))
        {
            return true;
        }

        if (url.Length == 0 || url.Length > 2083)
        {
            return false;
        }

        foreach (char ch in url)
        {
            if (ch < 0x20 || ch == '\u007F')
            {
                return false;
            }
        }

        int colon = url.IndexOf(':', StringComparison.Ordinal);
        int slash = url.IndexOfAny(['/', '?', '#']);
        return colon < 0 || (slash >= 0 && slash < colon);
    }

    private void LinkInline(MdNode n, Fmt f, StringBuilder sb, Story story, int depth)
    {
        string url = n.Destination;
        if (!IsLinkable(url))
        {
            _loss.Report("loss.markdown-link-target", "link targets", 1, "A link whose target is not a web, mail or relative address was kept as its text only.");
            Inlines(n, f, sb, story, depth + 1);
            return;
        }

        string tooltip = n.Title.Length > 0 ? " w:tooltip=\"" + XmlSnip.Escape(n.Title) + "\"" : string.Empty;
        string open;
        if (url.StartsWith('#'))
        {
            open = "<w:hyperlink w:anchor=\"" + XmlSnip.Escape(BookmarkName(Slug(url[1..]))) + "\"" + tooltip + " w:history=\"1\">";
        }
        else
        {
            string id = story.Add(RelType + "hyperlink", url, RelationshipTargetMode.External);
            open = "<w:hyperlink r:id=\"" + id + "\"" + tooltip + " w:history=\"1\">";
        }

        sb.Append(open);
        Inlines(n, f with { RunStyle = "Hyperlink" }, sb, story, depth + 1);
        sb.Append("</w:hyperlink>");
    }

    private void FootnoteReference(MdNode n, StringBuilder sb)
    {
        string key = MdText.NormalizeLabel(n.Label);
        if (!_footnoteDefinitions.TryGetValue(key, out MdNode? def))
        {
            sb.Append(Run("[^" + n.Label + "]", false, false, false, null));
            return;
        }

        int id = 0;
        foreach ((int existing, MdNode d) in _footnoteOrder)
        {
            if (d == def)
            {
                id = existing;
                break;
            }
        }

        if (id == 0)
        {
            id = _footnoteOrder.Count + 1;
            _footnoteOrder.Add((id, def));
        }

        sb.Append("<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteReference w:id=\"").Append(id.ToString(CultureInfo.InvariantCulture)).Append("\"/></w:r>");
    }

    // ---- pictures -----------------------------------------------------------------------------------------------------------------

    private void ImageInline(MdNode n, Fmt f, StringBuilder sb, Story story)
    {
        string alt = PlainText(n);
        byte[]? data = ImageBytes(n.Destination);
        if (data is null || !ImageInfo.TryRead(data, out ImageInfo info))
        {
            if (data is not null)
            {
                _loss.Report("loss.markdown-image-format", "image formats", 1, "A picture that is not PNG, JPEG, GIF or BMP was not embedded; its description was kept as text.");
            }
            else
            {
                _loss.Report("loss.markdown-image", "images", 1, "A picture that is not given as a data: address and has no image resolver was not embedded; its description was kept as text.");
            }

            sb.Append(Run(alt, f.Bold, true, f.Strike, f.RunStyle));
            return;
        }

        string name = "/word/media/image" + (_media.Count + 1).ToString(CultureInfo.InvariantCulture) + "." + info.Extension;
        _media.Add((name, info.ContentType, data));
        string rid = story.Add(RelType + "image", name[6..], RelationshipTargetMode.Internal);
        // Pixels at 96 per inch, at most six inches wide.
        const long emuPerPixel = 9525;
        const long maxWidth = 5_486_400;
        long cx = info.Width * emuPerPixel;
        long cy = info.Height * emuPerPixel;
        if (cx > maxWidth)
        {
            cy = cy * maxWidth / cx;
            cx = maxWidth;
        }

        string id = (++_drawingId).ToString(CultureInfo.InvariantCulture);
        sb.Append("<w:r>").Append(Insertions.Drawing(false, "w:", rid, id, "Picture " + id, XmlSnip.Escape(alt), Math.Max(1, cx), Math.Max(1, cy))).Append("</w:r>");
    }

    private byte[]? ImageBytes(string destination)
    {
        byte[]? bytes = null;
        if (destination.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = destination.IndexOf(',', StringComparison.Ordinal);
            if (comma > 0 && destination.AsSpan(0, comma).Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    bytes = Convert.FromBase64String(destination[(comma + 1)..].Trim());
                }
                catch (FormatException)
                {
                    return null;
                }
            }
        }
        else if (_options.ImageResolver is { } resolve)
        {
            // Opt-in: the caller decides what an address means (a folder, a web client); nothing is fetched otherwise.
            bytes = resolve(destination);
        }

        if (bytes is null)
        {
            return null;
        }

        _imageBytes += bytes.Length;
        if (bytes.Length > _ctx.Limits.MaxPartSize || _imageBytes > _ctx.Limits.MaxAllocationBytes)
        {
            _loss.Report("loss.markdown-image-size", "image sizes", 1, "A picture over the size limit was not embedded.");
            return null;
        }

        return bytes;
    }

    // ---- fixed parts --------------------------------------------------------------------------------------------------------------

    private static string SettingsXml(bool footnotes) =>
        Decl + "<w:settings xmlns:w=\"" + WNs + "\">" +
        (footnotes ? "<w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"0\"/></w:footnotePr>" : string.Empty) +
        "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>";

    private static string HeadingStyle(int level)
    {
        // Sizes in half-points follow the usual outline of heading sizes; the level goes in w:outlineLvl (ECMA-376 Part 1 §17.3.1.20).
        int[] sizes = [32, 26, 24, 22, 22, 22];
        string extra = level switch { 4 => "<w:i/>", 6 => "<w:i/>", _ => string.Empty };
        string l = level.ToString(CultureInfo.InvariantCulture);
        return "<w:style w:type=\"paragraph\" w:styleId=\"Heading" + l + "\"><w:name w:val=\"heading " + l + "\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:uiPriority w:val=\"9\"/><w:qFormat/>" +
            "<w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"" + (level == 1 ? 240 : 200) + "\" w:after=\"80\"/><w:outlineLvl w:val=\"" + (level - 1).ToString(CultureInfo.InvariantCulture) + "\"/></w:pPr>" +
            "<w:rPr><w:b/>" + extra + "<w:sz w:val=\"" + sizes[level - 1].ToString(CultureInfo.InvariantCulture) + "\"/><w:szCs w:val=\"" + sizes[level - 1].ToString(CultureInfo.InvariantCulture) + "\"/></w:rPr></w:style>";
    }

    private static string StylesXml
    {
        get
        {
            var sb = new StringBuilder(Decl).Append("<w:styles xmlns:w=\"").Append(WNs).Append("\">");
            sb.Append("<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>");
            sb.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
            sb.Append("<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/><w:uiPriority w:val=\"1\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");
            sb.Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>");
            for (int level = 1; level <= 6; level++)
            {
                sb.Append(HeadingStyle(level));
            }

            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Quote\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:uiPriority w:val=\"29\"/><w:qFormat/><w:pPr><w:pBdr><w:left w:val=\"single\" w:sz=\"18\" w:space=\"8\" w:color=\"BFBFBF\"/></w:pBdr><w:ind w:left=\"720\"/></w:pPr><w:rPr><w:i/><w:iCs/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"SourceCode\"><w:name w:val=\"Source Code\"/><w:basedOn w:val=\"Normal\"/><w:uiPriority w:val=\"99\"/><w:qFormat/><w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F2F2F2\"/><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"character\" w:styleId=\"InlineCode\"><w:name w:val=\"Inline Code\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:uiPriority w:val=\"99\"/><w:qFormat/><w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:uiPriority w:val=\"34\"/><w:qFormat/><w:pPr><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr></w:style>");
            sb.Append("<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:rPr><w:color w:val=\"0563C1\" w:themeColor=\"hyperlink\"/><w:u w:val=\"single\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"FootnoteText\"><w:name w:val=\"footnote text\"/><w:basedOn w:val=\"Normal\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"character\" w:styleId=\"FootnoteReference\"><w:name w:val=\"footnote reference\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>");
            sb.Append("<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:basedOn w:val=\"TableNormal\"/><w:uiPriority w:val=\"39\"/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:tblPr><w:tblBorders>");
            foreach (string side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
            {
                sb.Append("<w:").Append(side).Append(" w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/>");
            }

            sb.Append("</w:tblBorders></w:tblPr></w:style>");
            return sb.Append("</w:styles>").ToString();
        }
    }

    // Two abstract definitions (bullets, numbers) of nine levels each; every list is an instance of one, and a numbered list that starts
    // above 1 overrides the start of its own level (ECMA-376 Part 1 §17.9.27 lvlOverride).
    private string NumberingXml()
    {
        var sb = new StringBuilder(Decl).Append("<w:numbering xmlns:w=\"").Append(WNs).Append("\">");
        string[] bullets = ["\u2022", "\u25E6", "\u25AA"];
        string[] formats = ["decimal", "lowerLetter", "lowerRoman"];
        for (int abs = 0; abs < 2; abs++)
        {
            sb.Append("<w:abstractNum w:abstractNumId=\"").Append(abs).Append("\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
            for (int lvl = 0; lvl < MaxLevels; lvl++)
            {
                string left = (720 * (lvl + 1)).ToString(CultureInfo.InvariantCulture);
                sb.Append("<w:lvl w:ilvl=\"").Append(lvl).Append("\"><w:start w:val=\"1\"/>");
                if (abs == 0)
                {
                    sb.Append("<w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"").Append(bullets[lvl % 3]).Append("\"/>");
                }
                else
                {
                    sb.Append("<w:numFmt w:val=\"").Append(formats[lvl % 3]).Append("\"/><w:lvlText w:val=\"%").Append(lvl + 1).Append(".\"/>");
                }

                sb.Append("<w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"").Append(left).Append("\" w:hanging=\"360\"/></w:pPr></w:lvl>");
            }

            sb.Append("</w:abstractNum>");
        }

        foreach ((int numId, bool ordered, int start, int level) in _nums)
        {
            sb.Append("<w:num w:numId=\"").Append(numId).Append("\"><w:abstractNumId w:val=\"").Append(ordered ? 1 : 0).Append("\"/>");
            if (ordered)
            {
                // Every ordered list restarts at its own number, whatever list came before it on the same level.
                sb.Append("<w:lvlOverride w:ilvl=\"").Append(level).Append("\"><w:startOverride w:val=\"").Append(start.ToString(CultureInfo.InvariantCulture)).Append("\"/></w:lvlOverride>");
            }

            sb.Append("</w:num>");
        }

        return sb.Append("</w:numbering>").ToString();
    }
}
