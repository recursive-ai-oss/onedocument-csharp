using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Templating;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>
/// Builds a WordprocessingML package from a parsed Markdown tree: headings in the built-in heading styles, paragraphs, emphasis as run
/// properties, lists as numbering definitions, quotes, code in a code style, tables, hyperlinks, pictures and footnotes. What WordprocessingML
/// has no counterpart for is reported to the loss report.
/// </summary>
internal sealed partial class MarkdownImporter
{
    private const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelType = RelNs + "/";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    private const string CtPrefix = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
    private const int MaxLevels = 9;

    private readonly MdNode _root;
    private readonly MarkdownImportOptions _options;
    private readonly OpenContext _ctx;
    private readonly LossReport _loss;
    private readonly Dictionary<string, string> _properties;
    private readonly Dictionary<string, MdNode> _footnoteDefinitions = new(StringComparer.Ordinal);
    private readonly List<(int Id, MdNode Definition)> _footnoteOrder = [];
    private readonly List<(string Name, string ContentType, byte[] Data)> _media = [];
    private readonly List<(int NumId, bool Ordered, int Start, int Level)> _nums = [];
    private readonly Dictionary<string, int> _anchorCounts = new(StringComparer.Ordinal);
    private readonly int _maxDepth;
    private int _drawingId;
    private int _bookmarkId;
        private long _imageBytes;

    /// <summary>A part's relationships: hyperlinks and pictures get ids from rId10 up, so the fixed ones keep their ids whatever the content.</summary>
    private sealed class Story
    {
        public List<Relationship> Items { get; } = [];

        private int _next = 10;

        public string Add(string type, string target, RelationshipTargetMode mode)
        {
            foreach (Relationship r in Items)
            {
                if (r.Type == type && r.Target == target && r.TargetMode == mode)
                {
                    return r.Id;
                }
            }

            string id = "rId" + _next++.ToString(CultureInfo.InvariantCulture);
            Items.Add(new Relationship(id, type, target, mode));
            return id;
        }
    }

    private readonly record struct Ctx(int Quote, int ListLevel, bool Continuation, bool Tight, bool Bold);

    public MarkdownImporter(MdNode root, MarkdownImportOptions options, OpenContext context, LossReport loss, Dictionary<string, string> properties)
    {
        _root = root;
        _options = options;
        _ctx = context;
        _loss = loss;
        _properties = properties;
        _maxDepth = Math.Clamp(context.Limits.MaxRecursionDepth, 8, 1000);
    }

    /// <summary>The package, ready for <c>Document.FromPackage</c>.</summary>
    public OpcPackage BuildPackage(CancellationToken cancellationToken)
    {
        CollectFootnotes(_root);
        var main = new Story();
        var body = new StringBuilder();
        EmitChildren(_root, new Ctx(0, -1, false, true, false), body, main, 0, cancellationToken);

        // Footnotes are written after the body so that their numbers follow the order of the references.
        var footStory = new Story();
        string footnotesXml = FootnotesXml(footStory, cancellationToken);

        OpcPackage package = OpcPackage.Create(_ctx.Limits);
        try
        {
            package.SetRelationships(null,
            [
                new Relationship("rId1", RelType + "officeDocument", "word/document.xml"),
                new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
            ]);

            var fixedRels = new List<Relationship> { new("rId1", RelType + "styles", "styles.xml"), new("rId3", RelType + "settings", "settings.xml") };
            if (_nums.Count > 0)
            {
                fixedRels.Add(new Relationship("rId2", RelType + "numbering", "numbering.xml"));
            }

            if (_footnoteOrder.Count > 0)
            {
                fixedRels.Add(new Relationship("rId4", RelType + "footnotes", "footnotes.xml"));
            }

            fixedRels.AddRange(main.Items);

            package.AddPart("/word/document.xml", CtPrefix + "document.main+xml", Encoding.UTF8.GetBytes(
                Decl + "<w:document xmlns:w=\"" + WNs + "\" xmlns:r=\"" + RelNs + "\"><w:body>" + body +
                "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>"));
            package.AddPart("/word/styles.xml", CtPrefix + "styles+xml", Encoding.UTF8.GetBytes(StylesXml));
            package.AddPart("/word/settings.xml", CtPrefix + "settings+xml", Encoding.UTF8.GetBytes(SettingsXml(_footnoteOrder.Count > 0)));
            if (_nums.Count > 0)
            {
                package.AddPart("/word/numbering.xml", CtPrefix + "numbering+xml", Encoding.UTF8.GetBytes(NumberingXml()));
            }

            if (_footnoteOrder.Count > 0)
            {
                package.AddPart("/word/footnotes.xml", CtPrefix + "footnotes+xml", Encoding.UTF8.GetBytes(footnotesXml));
                if (footStory.Items.Count > 0)
                {
                    package.SetRelationships("/word/footnotes.xml", footStory.Items);
                }
            }

            foreach ((string name, string contentType, byte[] data) in _media)
            {
                package.AddPart(name, contentType, data);
            }

            package.SetRelationships("/word/document.xml", fixedRels);
            package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(CoreXml()));
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private string CoreXml()
    {
        var core = new StringBuilder(Decl).Append("<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
        void Elem(string tag, string key)
        {
            if (_properties.TryGetValue(key, out string? v) && v.Length > 0)
            {
                core.Append('<').Append(tag).Append('>').Append(XmlSnip.Escape(v)).Append("</").Append(tag).Append('>');
            }
        }

        Elem("dc:creator", "author");
        Elem("dc:description", "description");
        Elem("cp:keywords", "keywords");
        Elem("dc:subject", "subject");
        Elem("dc:title", "title");
        return core.Append("</cp:coreProperties>").ToString();
    }

    // ---- footnotes ---------------------------------------------------------------------------------------------------------------

    private void CollectFootnotes(MdNode node)
    {
        var stack = new Stack<MdNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            MdNode n = stack.Pop();
            if (n.Kind == MdKind.FootnoteDefinition)
            {
                _footnoteDefinitions.TryAdd(MdText.NormalizeLabel(n.Label), n);
                continue;
            }

            for (MdNode? c = n.FirstChild; c is not null; c = c.Next)
            {
                if (c.IsContainer)
                {
                    stack.Push(c);
                }
            }
        }
    }

    private string FootnotesXml(Story story, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(Decl).Append("<w:footnotes xmlns:w=\"").Append(WNs).Append("\" xmlns:r=\"").Append(RelNs).Append("\">");
        sb.Append("<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:separator/></w:r></w:p></w:footnote>");
        sb.Append("<w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>");
        // A footnote that mentions another footnote adds it to the list while this loop runs; the loop reads the list as it grows.
        for (int i = 0; i < _footnoteOrder.Count; i++)
        {
            (int id, MdNode def) = _footnoteOrder[i];
            sb.Append("<w:footnote w:id=\"").Append(id.ToString(CultureInfo.InvariantCulture)).Append("\">");
            var inner = new StringBuilder();
            EmitChildren(def, new Ctx(0, -1, false, true, false), inner, story, 1, cancellationToken);
            string xml = inner.ToString();
            const string mark = "<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> </w:t></w:r>";
            int firstP = xml.IndexOf("<w:p>", StringComparison.Ordinal);
            int firstP2 = xml.IndexOf("<w:p ", StringComparison.Ordinal);
            if (firstP < 0 && firstP2 < 0)
            {
                xml = "<w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr>" + mark + "</w:p>" + xml;
            }
            else
            {
                // The reference mark goes at the start of the first paragraph, after its properties.
                int at = firstP >= 0 ? firstP : firstP2;
                int gt = xml.IndexOf('>', at) + 1;
                if (xml.AsSpan(gt).StartsWith("<w:pPr>", StringComparison.Ordinal))
                {
                    gt = xml.IndexOf("</w:pPr>", gt, StringComparison.Ordinal) + "</w:pPr>".Length;
                }
                else
                {
                    xml = xml.Insert(gt, "<w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr>");
                    gt += "<w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr>".Length;
                }

                xml = xml.Insert(gt, mark);
            }

            sb.Append(xml).Append("</w:footnote>");
        }

        return sb.Append("</w:footnotes>").ToString();
    }

    // ---- blocks -------------------------------------------------------------------------------------------------------------------

    private void EmitChildren(MdNode parent, Ctx c, StringBuilder sb, Story story, int depth, CancellationToken ct)
    {
        for (MdNode? n = parent.FirstChild; n is not null; n = n.Next)
        {
            ct.ThrowIfCancellationRequested();
            EmitBlock(n, c, sb, story, depth, ct);
        }
    }

    private void EmitBlock(MdNode n, Ctx c, StringBuilder sb, Story story, int depth, CancellationToken ct)
    {
        if (depth > _maxDepth)
        {
            _loss.Report("loss.markdown-nesting", "deep nesting", 1, "Content nested deeper than the recursion limit was flattened to its text.");
            AppendFlatText(n, sb);
            return;
        }

        switch (n.Kind)
        {
            case MdKind.Paragraph:
                EmitParagraph(n, c, sb, story, styleId: null, numbering: null, taskPrefix: null);
                break;
            case MdKind.Heading:
                EmitHeading(n, c, sb, story, numbering: null, taskPrefix: null);
                break;
            case MdKind.BlockQuote:
                EmitChildren(n, c with { Quote = c.Quote + 1 }, sb, story, depth + 1, ct);
                break;
            case MdKind.List:
                EmitList(n, c, sb, story, depth, ct);
                break;
            case MdKind.CodeBlock:
                EmitCode(n, c, sb);
                break;
            case MdKind.HtmlBlock:
                RawHtmlBlock(n, c, sb, story);
                break;
            case MdKind.ThematicBreak:
                sb.Append("<w:p><w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"auto\"/></w:pBdr></w:pPr></w:p>");
                break;
            case MdKind.Table:
                EmitTable(n, sb, story, depth);
                break;
            case MdKind.FootnoteDefinition:
                break;
            case MdKind.Item:
                EmitChildren(n, c, sb, story, depth + 1, ct);
                break;
        }
    }

    private static void AppendFlatText(MdNode n, StringBuilder sb)
    {
        var text = new StringBuilder();
        var stack = new Stack<MdNode>();
        stack.Push(n);
        int guard = 0;
        while (stack.Count > 0 && guard++ < 100_000)
        {
            MdNode x = stack.Pop();
            if (x.Kind is MdKind.Text or MdKind.Code or MdKind.CodeBlock)
            {
                text.Append(x.Literal).Append(' ');
            }
            else if (x.Text is { } t)
            {
                text.Append(t).Append(' ');
            }

            for (MdNode? c = x.LastChild; c is not null; c = c.Prev)
            {
                stack.Push(c);
            }
        }

        sb.Append("<w:p>").Append(Run(text.ToString().Trim(), false, false, false, null)).Append("</w:p>");
    }

    private static string PPr(string? styleId, (int Level, int NumId)? numbering, int indentTwips, bool noContextual, string? align, bool keepNext = false)
    {
        var sb = new StringBuilder();
        if (styleId is not null)
        {
            sb.Append("<w:pStyle w:val=\"").Append(styleId).Append("\"/>");
        }

        if (keepNext)
        {
            sb.Append("<w:keepNext/>");
        }

        if (numbering is { } nm)
        {
            sb.Append("<w:numPr><w:ilvl w:val=\"").Append(nm.Level).Append("\"/><w:numId w:val=\"").Append(nm.NumId).Append("\"/></w:numPr>");
        }

        if (indentTwips > 0)
        {
            sb.Append("<w:ind w:left=\"").Append(indentTwips.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
        }

        if (noContextual)
        {
            sb.Append("<w:contextualSpacing w:val=\"0\"/>");
        }

        if (align is not null)
        {
            sb.Append("<w:jc w:val=\"").Append(align).Append("\"/>");
        }

        return sb.Length == 0 ? string.Empty : "<w:pPr>" + sb + "</w:pPr>";
    }

    private void EmitParagraph(MdNode n, Ctx c, StringBuilder sb, Story story, string? styleId, (int Level, int NumId)? numbering, string? taskPrefix)
    {
        bool inQuote = c.Quote > 0;
        string? style = styleId ?? (numbering is not null || c.ListLevel >= 0 ? "ListParagraph" : inQuote ? "Quote" : null);
        int indent = numbering is null && c.ListLevel >= 0 ? 720 * (c.ListLevel + 1) : 0;
        if (inQuote && style == "Quote")
        {
            indent = 720 * c.Quote;
        }

        sb.Append("<w:p>").Append(PPr(style, numbering, indent, !c.Tight && (numbering is not null || c.ListLevel >= 0), null));
        if (taskPrefix is not null)
        {
            sb.Append(Run(taskPrefix, false, false, false, null));
        }

        Inlines(n, new Fmt(c.Bold, false, false, null), sb, story, 0);
        sb.Append("</w:p>");
    }

    private void EmitHeading(MdNode n, Ctx c, StringBuilder sb, Story story, (int Level, int NumId)? numbering, string? taskPrefix)
    {
        string style = "Heading" + Math.Clamp(n.Level, 1, 6).ToString(CultureInfo.InvariantCulture);
        string anchor = AnchorFor(n);
        int id = ++_bookmarkId;
        sb.Append("<w:p>").Append(PPr(style, numbering, numbering is null && c.ListLevel >= 0 ? 720 * (c.ListLevel + 1) : 0, false, null));
        sb.Append("<w:bookmarkStart w:id=\"").Append(id).Append("\" w:name=\"").Append(XmlSnip.Escape(anchor)).Append("\"/>");
        if (taskPrefix is not null)
        {
            sb.Append(Run(taskPrefix, false, false, false, null));
        }

        Inlines(n, new Fmt(false, false, false, null), sb, story, 0);
        sb.Append("<w:bookmarkEnd w:id=\"").Append(id).Append("\"/></w:p>");
    }

    // Headings carry a bookmark named after their GitHub-style slug, so a link such as [x](#my-heading) lands on them.
    private string AnchorFor(MdNode heading)
    {
        string slug = Slug(PlainText(heading));
        if (_anchorCounts.TryGetValue(slug, out int seen))
        {
            _anchorCounts[slug] = seen + 1;
            slug += "-" + seen.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            _anchorCounts[slug] = 1;
        }

        return BookmarkName(slug);
    }

    internal static string BookmarkName(string slug)
    {
        string name = "md_" + slug.Replace('-', '_');
        return name.Length > 40 ? name[..40] : name;
    }

    internal static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                sb.Append(ch);
            }
            else if (ch is ' ' or '-')
            {
                sb.Append('-');
            }
        }

        return sb.ToString();
    }

    internal static string PlainText(MdNode n)
    {
        var sb = new StringBuilder();
        var stack = new Stack<MdNode>();
        for (MdNode? c = n.LastChild; c is not null; c = c.Prev)
        {
            stack.Push(c);
        }

        int guard = 0;
        while (stack.Count > 0 && guard++ < 1_000_000)
        {
            MdNode x = stack.Pop();
            switch (x.Kind)
            {
                case MdKind.Text or MdKind.Code:
                    sb.Append(x.Literal);
                    break;
                case MdKind.SoftBreak or MdKind.LineBreak:
                    sb.Append(' ');
                    break;
                default:
                    for (MdNode? c = x.LastChild; c is not null; c = c.Prev)
                    {
                        stack.Push(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    private void EmitCode(MdNode n, Ctx c, StringBuilder sb)
    {
        string literal = n.Literal;
        if (literal.EndsWith('\n'))
        {
            literal = literal[..^1];
        }

        if (n.Info.Length > 0)
        {
            _loss.Report("loss.markdown-code-language", "code block languages", 1, "The language of a fenced code block has no place in the document and was dropped.");
        }

        int indent = c.ListLevel >= 0 ? 720 * (c.ListLevel + 1) : 0;
        foreach (string line in literal.Split('\n'))
        {
            sb.Append("<w:p>").Append(PPr("SourceCode", null, indent, false, null));
            if (line.Length > 0)
            {
                sb.Append("<w:r>").Append(XmlSnip.RunContent("w", line)).Append("</w:r>");
            }

            sb.Append("</w:p>");
        }

        if (literal.Length == 0)
        {
            sb.Append("<w:p>").Append(PPr("SourceCode", null, indent, false, null)).Append("</w:p>");
        }
    }

    // The seam for raw HTML: when the HTML importer is available a block is handed to it; until then its text is kept and the markup is reported.
    private void RawHtmlBlock(MdNode n, Ctx c, StringBuilder sb, Story story)
    {
        string text = HtmlText(n.Literal);
        _loss.Report("loss.markdown-raw-html", "raw HTML", 1, "Raw HTML was not interpreted; its tags were dropped and its text kept.");
        if (text.Length == 0)
        {
            return;
        }

        foreach (string para in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append("<w:p>").Append(PPr(c.Quote > 0 ? "Quote" : null, null, c.Quote > 0 ? 720 * c.Quote : c.ListLevel >= 0 ? 720 * (c.ListLevel + 1) : 0, false, null))
                .Append(Run(para.Trim(), false, false, false, null)).Append("</w:p>");
        }

        _ = story;
    }

    // The text between the tags, with the tags, comments and declarations removed; one scan.
    internal static string HtmlText(string html)
    {
        var sb = new StringBuilder(html.Length);
        var memo = new MdScan.HtmlMemo();
        int i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<' && MdScan.TryScanHtmlTag(html, i, html.Length, memo, out int end))
            {
                string tag = html.Substring(i, Math.Min(end - i, 12));
                if (tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("</p", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("<p", StringComparison.OrdinalIgnoreCase)
                    || tag.StartsWith("</div", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("\n\n");
                }

                i = end;
            }
            else if (html[i] == '&' && MdText.TryEntity(html, i, out string v, out int e2))
            {
                sb.Append(v);
                i = e2;
            }
            else
            {
                sb.Append(html[i]);
                i++;
            }
        }

        // Single line ends inside a paragraph are spaces.
        string[] paras = sb.ToString().Split("\n\n");
        return string.Join("\n\n", paras.Select(static p => string.Join(' ', p.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .Where(static p => p.Length > 0));
    }

    // ---- lists --------------------------------------------------------------------------------------------------------------------

    private void EmitList(MdNode list, Ctx c, StringBuilder sb, Story story, int depth, CancellationToken ct)
    {
        int level = Math.Min(c.ListLevel + 1, MaxLevels - 1);
        if (c.ListLevel + 1 >= MaxLevels)
        {
            _loss.Report("loss.markdown-list-depth", "list nesting", 1, "Lists nested deeper than nine levels share the ninth level.");
        }

        int numId = _nums.Count + 1;
        _nums.Add((numId, list.Ordered, list.Start, level));
        Ctx inner = c with { ListLevel = level, Tight = list.Tight, Continuation = false };
        for (MdNode? item = list.FirstChild; item is not null; item = item.Next)
        {
            ct.ThrowIfCancellationRequested();
            string? task = item.Task switch { 1 => "\u2610 ", 2 => "\u2612 ", _ => null };
            bool first = true;
            for (MdNode? child = item.FirstChild; child is not null; child = child.Next)
            {
                if (first)
                {
                    first = false;
                    if (child.Kind == MdKind.Paragraph)
                    {
                        EmitParagraph(child, inner, sb, story, null, (level, numId), task);
                        continue;
                    }

                    if (child.Kind == MdKind.Heading)
                    {
                        EmitHeading(child, inner, sb, story, (level, numId), task);
                        continue;
                    }

                    // The item starts with something that is not text (a code block, a nested list): the marker sits on an empty paragraph.
                    sb.Append("<w:p>").Append(PPr("ListParagraph", (level, numId), 0, false, null));
                    if (task is not null)
                    {
                        sb.Append(Run(task, false, false, false, null));
                    }

                    sb.Append("</w:p>");
                }

                EmitBlock(child, inner with { Continuation = true }, sb, story, depth + 1, ct);
            }

            if (first)
            {
                // An empty item still shows its marker.
                sb.Append("<w:p>").Append(PPr("ListParagraph", (level, numId), 0, false, null));
                if (task is not null)
                {
                    sb.Append(Run(task, false, false, false, null));
                }

                sb.Append("</w:p>");
            }
        }
    }

    // ---- tables -------------------------------------------------------------------------------------------------------------------

    private void EmitTable(MdNode table, StringBuilder sb, Story story, int depth)
    {
        int cols = table.FirstChild?.ChildCount() ?? 1;
        int width = 9360 / Math.Max(1, cols);
        sb.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"0\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr><w:tblGrid>");
        for (int i = 0; i < cols; i++)
        {
            sb.Append("<w:gridCol w:w=\"").Append(width).Append("\"/>");
        }

        sb.Append("</w:tblGrid>");
        for (MdNode? row = table.FirstChild; row is not null; row = row.Next)
        {
            sb.Append("<w:tr>");
            if (row.IsHeader)
            {
                sb.Append("<w:trPr><w:tblHeader/></w:trPr>");
            }

            for (MdNode? cell = row.FirstChild; cell is not null; cell = cell.Next)
            {
                sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(width).Append("\" w:type=\"dxa\"/></w:tcPr><w:p>");
                string? align = cell.Align switch { 2 => "center", 3 => "right", _ => null };
                string ppr = PPr(null, null, 0, false, align);
                sb.Append(ppr);
                Inlines(cell, new Fmt(row.IsHeader, false, false, null), sb, story, 0);
                sb.Append("</w:p></w:tc>");
            }

            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl>");
        // A table cannot be the last thing in the body, and two tables in a row would merge; an empty paragraph keeps them apart.
        sb.Append("<w:p/>");
        _ = depth;
    }
}

internal static class MdNodeExtensions
{
    public static int ChildCount(this MdNode n)
    {
        int count = 0;
        for (MdNode? c = n.FirstChild; c is not null; c = c.Next)
        {
            count++;
        }

        return count;
    }
}
