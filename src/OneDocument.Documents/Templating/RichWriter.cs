using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Documents.Styles;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>Writes converted HTML and Markdown as WordprocessingML paragraphs and runs, adding the styles and list definitions they use when the document lacks them.</summary>
internal static class RichWriter
{
    private static readonly string[] OrderedFormats = ["decimal", "lowerLetter", "lowerRoman"];

    public static string Paragraphs(RenderContext ctx, ParagraphModel host, IReadOnlyList<RichBlock> blocks)
    {
        string p = host.Prefix.Length == 0 ? string.Empty : host.Prefix + ":";
        var sb = new StringBuilder();
        Dictionary<int, long> numIds = [];
        foreach (RichBlock block in blocks)
        {
            var pPr = new StringBuilder();
            string? styleId = block.ListLevel >= 0 ? EnsureStyle(ctx, "ListParagraph") : block.Style is null ? null : EnsureStyle(ctx, block.Style);
            if (styleId is not null)
            {
                pPr.Append('<').Append(p).Append("pStyle ").Append(p).Append("val=\"").Append(styleId).Append("\"/>");
            }

            if (block.ListLevel >= 0)
            {
                if (!numIds.TryGetValue(block.ListId, out long numId))
                {
                    numId = EnsureList(ctx, block.Ordered, restart: true, block.ListId);
                    numIds[block.ListId] = numId;
                }

                pPr.Append('<').Append(p).Append("numPr><").Append(p).Append("ilvl ").Append(p).Append("val=\"").Append(block.ListLevel.ToString(CultureInfo.InvariantCulture))
                    .Append("\"/><").Append(p).Append("numId ").Append(p).Append("val=\"").Append(numId.ToString(CultureInfo.InvariantCulture)).Append("\"/></").Append(p).Append("numPr>");
            }

            sb.Append('<').Append(p).Append('p').Append('>');
            if (pPr.Length > 0)
            {
                sb.Append('<').Append(p).Append("pPr>").Append(pPr).Append("</").Append(p).Append("pPr>");
            }

            foreach (RichRun run in block.Runs)
            {
                sb.Append(Run(ctx, host.Prefix, run, null));
            }

            sb.Append("</").Append(p).Append("p>");
        }

        return sb.ToString();
    }

    public static string Runs(RenderContext ctx, ParagraphModel host, ParagraphItem? segment, IReadOnlyList<RichBlock> blocks)
    {
        string p = host.Prefix.Length == 0 ? string.Empty : host.Prefix + ":";
        var sb = new StringBuilder();
        bool first = true;
        foreach (RichBlock block in blocks)
        {
            if (!first)
            {
                sb.Append('<').Append(p).Append("r><").Append(p).Append("br/></").Append(p).Append("r>");
            }

            first = false;
            foreach (RichRun run in block.Runs)
            {
                sb.Append(Run(ctx, host.Prefix, run, segment));
            }
        }

        return sb.ToString();
    }

    private static string Run(RenderContext ctx, string prefix, RichRun run, ParagraphItem? baseRun)
    {
        string p = prefix.Length == 0 ? string.Empty : prefix + ":";
        if (run.Break)
        {
            return "<" + p + "r><" + p + "br/></" + p + "r>";
        }

        var rPr = new StringBuilder();
        if (run.Link is not null)
        {
            Insertions.EnsureHyperlinkStyle(ctx);
            rPr.Append('<').Append(p).Append("rStyle ").Append(p).Append("val=\"Hyperlink\"/>");
        }

        if (run.Code)
        {
            rPr.Append('<').Append(p).Append("rFonts ").Append(p).Append("ascii=\"Consolas\" ").Append(p).Append("hAnsi=\"Consolas\" ").Append(p).Append("cs=\"Consolas\"/>");
        }

        if (run.Bold)
        {
            rPr.Append('<').Append(p).Append("b/>");
        }

        if (run.Italic)
        {
            rPr.Append('<').Append(p).Append("i/>");
        }

        if (run.Strike)
        {
            rPr.Append('<').Append(p).Append("strike/>");
        }

        if (run.Underline && run.Link is null)
        {
            rPr.Append('<').Append(p).Append("u ").Append(p).Append("val=\"single\"/>");
        }

        string props = rPr.Length == 0 ? string.Empty : "<" + p + "rPr>" + rPr + "</" + p + "rPr>";
        string runXml = "<" + p + "r>" + props + XmlSnip.RunContent(prefix, run.Text) + "</" + p + "r>";
        if (run.Link is null)
        {
            return runXml;
        }

        string target;
        if (run.Link.StartsWith('#'))
        {
            target = " " + p + "anchor=\"" + XmlSnip.Escape(run.Link[1..]) + "\"";
        }
        else
        {
            string id = ctx.AddRelationship(ctx.CurrentPart, ctx.RelationshipType("hyperlink"), run.Link, Packaging.RelationshipTargetMode.External);
            target = " xmlns:r=\"" + (ctx.Strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "\" r:id=\"" + id + "\"";
        }

        _ = baseRun;
        return "<" + p + "hyperlink" + target + " " + p + "history=\"1\">" + runXml + "</" + p + "hyperlink>";
    }

    // ---- styles ----------------------------------------------------------------------------------------------------------------

    // Finds the style by id, then by Word's built-in name, and adds a plain definition when the document has neither.
    internal static string EnsureStyle(RenderContext ctx, string key)
    {
        (string id, string name, string? basedOn, string? ppr, string? rpr) = key switch
        {
            "h1" => ("Heading1", "heading 1", "Normal", Heading(0, 240), "<w:b/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/>"),
            "h2" => ("Heading2", "heading 2", "Normal", Heading(1, 40), "<w:b/><w:sz w:val=\"26\"/><w:szCs w:val=\"26\"/>"),
            "h3" => ("Heading3", "heading 3", "Normal", Heading(2, 40), "<w:b/><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/>"),
            "h4" => ("Heading4", "heading 4", "Normal", Heading(3, 40), "<w:b/><w:i/>"),
            "h5" => ("Heading5", "heading 5", "Normal", Heading(4, 40), "<w:b/>"),
            "h6" => ("Heading6", "heading 6", "Normal", Heading(5, 40), "<w:i/>"),
            "quote" => ("Quote", "Quote", "Normal", "<w:spacing w:before=\"200\" w:after=\"160\"/><w:ind w:left=\"864\" w:right=\"864\"/>", "<w:i/><w:iCs/>"),
            "code" => ("HtmlPreformatted", "HTML Preformatted", "Normal", "<w:spacing w:after=\"0\"/>", "<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/><w:sz w:val=\"20\"/>"),
            _ => ("ListParagraph", "List Paragraph", "Normal", "<w:ind w:left=\"720\"/><w:contextualSpacing/>", null),
        };

        StyleSheet sheet = ctx.Document.Styles;
        if (sheet.Find(id) is { } byId)
        {
            return byId.Id;
        }

        if (sheet.FindByName(name) is { } byName)
        {
            return byName.Id;
        }

        StyleDefinition style = sheet.AddStyle(id, name, StyleType.Paragraph, sheet.Find(basedOn!) is null ? null : basedOn);
        string w = " xmlns:w=\"" + ctx.Ns + "\"";
        MarkupNode node = style.Markup.Node;
        node.AppendXml(Encoding.UTF8.GetBytes("<w:next" + w + " w:val=\"Normal\"/>"));
        node.AppendXml(Encoding.UTF8.GetBytes("<w:qFormat" + w + "/>"));
        if (ppr is not null)
        {
            node.AppendXml(Encoding.UTF8.GetBytes("<w:pPr" + w + ">" + ppr + "</w:pPr>"));
        }

        if (rpr is not null)
        {
            node.AppendXml(Encoding.UTF8.GetBytes("<w:rPr" + w + ">" + rpr + "</w:rPr>"));
        }

        return id;
    }

    private static string Heading(int level, int before) =>
        "<w:keepNext/><w:keepLines/><w:spacing w:before=\"" + before.ToString(CultureInfo.InvariantCulture) + "\" w:after=\"0\"/><w:outlineLvl w:val=\"" + level.ToString(CultureInfo.InvariantCulture) + "\"/>";

    // ---- lists -----------------------------------------------------------------------------------------------------------------

    /// <summary>The numbering instance for a list: all bullet lists share one; each numbered list gets its own, restarting at 1.</summary>
    internal static long EnsureList(RenderContext ctx, bool ordered, bool restart, int listId)
    {
        RichListState st = ctx.Lists;
        MarkupDocument numbering = NumberingPart(ctx);
        MarkupNode root = numbering.Root;
        string w = " xmlns:w=\"" + ctx.Ns + "\"";
        if (ordered)
        {
            if (st.OrderedNums.TryGetValue(listId, out long existing))
            {
                return existing;
            }

            if (st.OrderedAbstract < 0)
            {
                st.OrderedAbstract = AddAbstract(root, w, ordered: true);
            }

            long num = AddNum(root, w, st.OrderedAbstract, restart);
            st.OrderedNums[listId] = num;
            return num;
        }

        if (st.BulletNum < 0)
        {
            st.BulletAbstract = AddAbstract(root, w, ordered: false);
            st.BulletNum = AddNum(root, w, st.BulletAbstract, restart: false);
        }

        return st.BulletNum;
    }

    private static MarkupDocument NumberingPart(RenderContext ctx)
    {
        Document doc = ctx.Document;
        MarkupDocument? existing = doc.LoadRelatedByType(ctx.RelationshipType("numbering"), out _);
        return existing ?? doc.CreatePart(ctx.RelationshipType("numbering"), "/word/numbering.xml",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml", "<w:numbering xmlns:w=\"" + ctx.Ns + "\"/>", out _);
    }

    private static long MaxId(MarkupNode root, string element, string attribute)
    {
        long max = 0;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalNameString == element && long.TryParse(c.GetAttribute(root.NamespaceUri, attribute), NumberStyles.None, CultureInfo.InvariantCulture, out long v))
            {
                max = Math.Max(max, v);
            }
        }

        return max;
    }

    // ECMA-376 Part 1 §17.9.1 (w:abstractNum): nine levels; the abstract definitions come before the num elements.
    private static long AddAbstract(MarkupNode root, string w, bool ordered)
    {
        long id = MaxId(root, "abstractNum", "abstractNumId") + 1;
        var sb = new StringBuilder();
        sb.Append("<w:abstractNum").Append(w).Append(" w:abstractNumId=\"").Append(id.ToString(CultureInfo.InvariantCulture)).Append("\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
        for (int lvl = 0; lvl < 9; lvl++)
        {
            string format = ordered ? OrderedFormats[lvl % 3] : "bullet";
            string text = ordered ? "%" + (lvl + 1).ToString(CultureInfo.InvariantCulture) + "." : "\u2022";
            int left = 720 * (lvl + 1);
            sb.Append("<w:lvl w:ilvl=\"").Append(lvl.ToString(CultureInfo.InvariantCulture)).Append("\"><w:start w:val=\"1\"/><w:numFmt w:val=\"").Append(format)
                .Append("\"/><w:lvlText w:val=\"").Append(text).Append("\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"")
                .Append(left.ToString(CultureInfo.InvariantCulture)).Append("\" w:hanging=\"360\"/></w:pPr></w:lvl>");
        }

        sb.Append("</w:abstractNum>");
        byte[] xml = Encoding.UTF8.GetBytes(sb.ToString());
        MarkupNode firstNum = default;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalNameString == "num")
            {
                firstNum = c;
                break;
            }
        }

        if (firstNum.IsNull)
        {
            root.AppendXml(xml);
        }
        else
        {
            firstNum.InsertXmlBefore(xml);
        }

        return id;
    }

    private static long AddNum(MarkupNode root, string w, long abstractId, bool restart)
    {
        long id = MaxId(root, "num", "numId") + 1;
        string over = restart ? "<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"1\"/></w:lvlOverride>" : string.Empty;
        string xml = "<w:num" + w + " w:numId=\"" + id.ToString(CultureInfo.InvariantCulture) + "\"><w:abstractNumId w:val=\"" + abstractId.ToString(CultureInfo.InvariantCulture) + "\"/>" + over + "</w:num>";
        MarkupNode cleanup = default;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalNameString == "numIdMacAtCleanup")
            {
                cleanup = c;
            }
        }

        if (cleanup.IsNull)
        {
            root.AppendXml(Encoding.UTF8.GetBytes(xml));
        }
        else
        {
            cleanup.InsertXmlBefore(Encoding.UTF8.GetBytes(xml));
        }

        return id;
    }
}

/// <summary>The list definitions a render has added to the numbering part.</summary>
internal sealed class RichListState
{
    public long BulletNum = -1;
    public long OrderedAbstract = -1;
    public long BulletAbstract = -1;
    public readonly Dictionary<int, long> OrderedNums = [];
}
