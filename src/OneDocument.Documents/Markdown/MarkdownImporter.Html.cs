using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents.Templating;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Markdown;

// Raw HTML in Markdown goes through the HTML importer's fragment reader (sanitised, nothing fetched), and what comes out is written here with the same
// styles as the rest of the document. A block of HTML becomes paragraphs; inline tags with their content, found by matching the open and close tag among
// the siblings, become runs. What the importer drops (scripts, styles, forms, table structure, pictures) is reported as loss.markdown-raw-html.
internal sealed partial class MarkdownImporter
{
    private static readonly HashSet<string> s_voidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "img", "hr", "input", "wbr", "area", "base", "col", "embed", "link", "meta", "param", "source", "track",
    };

    private List<RichBlock> HtmlFragment(string html)
    {
        var dropped = new SortedSet<string>(StringComparer.Ordinal);
        List<RichBlock> blocks = Html.HtmlRich.Convert(html, _ctx, dropped);
        foreach (string feature in dropped)
        {
            _loss.Report("loss.markdown-raw-html", "raw HTML", 1, "Raw HTML went through the HTML importer; what the document model does not carry (" + feature + " and the like) was dropped.");
        }

        return blocks;
    }

    // A block of HTML: paragraphs, headings, lists, quotes and code as the HTML importer reads them.
    private void RawHtmlBlock(MdNode n, Ctx c, StringBuilder sb, Story story)
    {
        EmitRichBlocks(HtmlFragment(n.Literal), c, sb, story);
    }

    private void EmitRichBlocks(IReadOnlyList<RichBlock> blocks, Ctx c, StringBuilder sb, Story story)
    {
        var numbers = new Dictionary<int, int>();
        foreach (RichBlock b in blocks)
        {
            (int Level, int NumId)? numbering = null;
            string? style = b.Style switch
            {
                "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "Heading" + b.Style[1],
                "quote" => "Quote",
                "code" => "SourceCode",
                _ => null,
            };
            if (b.ListLevel >= 0)
            {
                int level = Math.Min(b.ListLevel, MaxLevels - 1);
                if (!numbers.TryGetValue(b.ListId, out int numId))
                {
                    numId = AllocNum(b.Ordered, 1, level);
                    numbers[b.ListId] = numId;
                }

                numbering = (level, numId);
                style ??= "ListParagraph";
            }
            else if (style is null && c.Quote > 0)
            {
                style = "Quote";
            }

            int indent = style == "Quote" ? 720 * Math.Max(1, c.Quote) : 0;
            sb.Append("<w:p>").Append(PPr(style, numbering, numbering is null ? indent : 0, false, null));
            RichRuns(b.Runs, new Fmt(false, false, false, null), sb, story);
            sb.Append("</w:p>");
        }
    }

    private static void RichRuns(List<RichRun> runs, Fmt f, StringBuilder sb, Story story)
    {
        int i = 0;
        while (i < runs.Count)
        {
            string? link = runs[i].Link;
            int j = i;
            var inner = new StringBuilder();
            while (j < runs.Count && runs[j].Link == link)
            {
                RichRun r = runs[j];
                if (r.Break)
                {
                    inner.Append("<w:r><w:br/></w:r>");
                }
                else
                {
                    inner.Append(Run(r.Text, f.Bold || r.Bold, f.Italic || r.Italic, f.Strike || r.Strike, r.Code ? "InlineCode" : link is not null ? "Hyperlink" : f.RunStyle, r.Underline && link is null));
                }

                j++;
            }

            if (link is not null && IsLinkable(link))
            {
                if (link.StartsWith('#'))
                {
                    sb.Append("<w:hyperlink w:anchor=\"").Append(XmlSnip.Escape(BookmarkName(Slug(link[1..])))).Append("\" w:history=\"1\">");
                }
                else
                {
                    sb.Append("<w:hyperlink r:id=\"").Append(story.Add(RelType + "hyperlink", link, RelationshipTargetMode.External)).Append("\" w:history=\"1\">");
                }

                sb.Append(inner).Append("</w:hyperlink>");
            }
            else
            {
                sb.Append(inner);
            }

            i = j;
        }
    }

    // Inline raw HTML at node n. Returns the last sibling it used, so the caller continues after it, or null when only n was used.
    private MdNode? RawHtmlInline(MdNode n, Fmt f, StringBuilder sb, Story story)
    {
        string tag = n.Literal;
        if (tag.StartsWith("<!", StringComparison.Ordinal) || tag.StartsWith("<?", StringComparison.Ordinal))
        {
            // A comment, declaration or processing instruction has no content.
            return null;
        }

        if (tag.StartsWith("</", StringComparison.Ordinal))
        {
            // A closing tag whose opener was not found among the siblings.
            return null;
        }

        string name = TagName(tag);
        bool standalone = tag.EndsWith("/>", StringComparison.Ordinal) || s_voidTags.Contains(name);
        if (standalone)
        {
            if (name.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("<w:r><w:br/></w:r>");
            }
            else
            {
                InlineRich(HtmlFragment(tag), f, sb, story);
            }

            return null;
        }

        // Find the matching closing tag among the later siblings, and take everything between as the element's content.
        int depth = 1;
        MdNode? close = null;
        int scanned = 0;
        for (MdNode? s = n.Next; s is not null && scanned < 10_000; s = s.Next, scanned++)
        {
            if (s.Kind != MdKind.HtmlInline)
            {
                continue;
            }

            if (s.Literal.StartsWith("</", StringComparison.Ordinal) && TagName(s.Literal).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                if (--depth == 0)
                {
                    close = s;
                    break;
                }
            }
            else if (!s.Literal.StartsWith("<!", StringComparison.Ordinal) && !s.Literal.EndsWith("/>", StringComparison.Ordinal) && !s.Literal.StartsWith("</", StringComparison.Ordinal)
                && TagName(s.Literal).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
        }

        if (close is null)
        {
            // An opening tag nothing closes: the tag alone says nothing the document can hold.
            InlineRich(HtmlFragment(tag), f, sb, story);
            return null;
        }

        var html = new StringBuilder(tag);
        for (MdNode? s = n.Next; s is not null && s != close; s = s.Next)
        {
            HtmlOf(s, html, 0);
        }

        html.Append(close.Literal);
        InlineRich(HtmlFragment(html.ToString()), f, sb, story);
        return close;
    }

    // The runs of the blocks the HTML importer returned, one after another with a line break between blocks.
    private static void InlineRich(IReadOnlyList<RichBlock> blocks, Fmt f, StringBuilder sb, Story story)
    {
        bool first = true;
        foreach (RichBlock b in blocks)
        {
            if (!first)
            {
                sb.Append("<w:r><w:br/></w:r>");
            }

            first = false;
            RichRuns(b.Runs, f, sb, story);
        }
    }

    private static string TagName(string tag)
    {
        int i = tag.StartsWith("</", StringComparison.Ordinal) ? 2 : 1;
        int start = i;
        while (i < tag.Length && (char.IsAsciiLetterOrDigit(tag[i]) || tag[i] == '-'))
        {
            i++;
        }

        return tag[start..i];
    }

    // A sibling written back as HTML, so it can sit inside the element the HTML importer is asked to read.
    private static void HtmlOf(MdNode n, StringBuilder sb, int depth)
    {
        if (depth > 64)
        {
            sb.Append(System.Net.WebUtility.HtmlEncode(PlainText(n)));
            return;
        }

        switch (n.Kind)
        {
            case MdKind.Text:
                sb.Append(System.Net.WebUtility.HtmlEncode(n.Literal));
                break;
            case MdKind.SoftBreak:
                sb.Append('\n');
                break;
            case MdKind.LineBreak:
                sb.Append("<br>");
                break;
            case MdKind.Code:
                sb.Append("<code>").Append(System.Net.WebUtility.HtmlEncode(n.Literal)).Append("</code>");
                break;
            case MdKind.HtmlInline:
                sb.Append(n.Literal);
                break;
            case MdKind.Emph or MdKind.Strong or MdKind.Strike:
                {
                    string t = n.Kind == MdKind.Emph ? "em" : n.Kind == MdKind.Strong ? "strong" : "del";
                    sb.Append('<').Append(t).Append('>');
                    for (MdNode? c = n.FirstChild; c is not null; c = c.Next)
                    {
                        HtmlOf(c, sb, depth + 1);
                    }

                    sb.Append("</").Append(t).Append('>');
                    break;
                }

            case MdKind.Link:
                sb.Append("<a href=\"").Append(System.Net.WebUtility.HtmlEncode(n.Destination)).Append("\">");
                for (MdNode? c = n.FirstChild; c is not null; c = c.Next)
                {
                    HtmlOf(c, sb, depth + 1);
                }

                sb.Append("</a>");
                break;
            default:
                sb.Append(System.Net.WebUtility.HtmlEncode(PlainText(n)));
                break;
        }

        _ = CultureInfo.InvariantCulture;
    }
}
