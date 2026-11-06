using System.Text;
using RecursiveAI.OneDocument.Documents.Markdown;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// Renders the parser's tree as HTML in the shape the CommonMark and GFM specifications print their examples in, so that the published
/// examples can be compared with what the parser produced. Test code only.
/// </summary>
internal static class MdHtml
{
    public static string Render(MdNode root, bool gfm)
    {
        var sb = new StringBuilder();
        Block(sb, root, gfm, tight: false);
        return sb.ToString();
    }

    private static void Cr(StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.Append('\n');
        }
    }

    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => c.ToString() });
        }

        return sb.ToString();
    }

    private static string Filter(string html, bool gfm)
    {
        if (!gfm)
        {
            return html;
        }

        // GFM "disallowed raw HTML": these tags have their '<' replaced.
        var sb = new StringBuilder();
        for (int i = 0; i < html.Length; i++)
        {
            if (html[i] == '<')
            {
                int j = i + 1;
                if (j < html.Length && html[j] == '/')
                {
                    j++;
                }

                foreach (string tag in new[] { "title", "textarea", "style", "xmp", "iframe", "noembed", "noframes", "script", "plaintext" })
                {
                    if (string.Compare(html, j, tag, 0, tag.Length, StringComparison.OrdinalIgnoreCase) == 0
                        && (j + tag.Length >= html.Length || html[j + tag.Length] is ' ' or '\t' or '\n' or '/' or '>'))
                    {
                        sb.Append("&lt;");
                        goto next;
                    }
                }
            }

            sb.Append(html[i]);
            next:;
        }

        return sb.ToString();
    }

    private static void Block(StringBuilder sb, MdNode n, bool gfm, bool tight)
    {
        switch (n.Kind)
        {
            case MdKind.Document:
                foreach (MdNode c in n.Children())
                {
                    Block(sb, c, gfm, false);
                }

                break;
            case MdKind.BlockQuote:
                Cr(sb);
                sb.Append("<blockquote>\n");
                foreach (MdNode c in n.Children())
                {
                    Block(sb, c, gfm, false);
                }

                Cr(sb);
                sb.Append("</blockquote>\n");
                break;
            case MdKind.List:
                Cr(sb);
                if (n.Ordered)
                {
                    sb.Append(n.Start != 1 ? "<ol start=\"" + n.Start + "\">\n" : "<ol>\n");
                }
                else
                {
                    sb.Append("<ul>\n");
                }

                foreach (MdNode c in n.Children())
                {
                    Block(sb, c, gfm, n.Tight);
                }

                Cr(sb);
                sb.Append(n.Ordered ? "</ol>\n" : "</ul>\n");
                break;
            case MdKind.Item:
                Cr(sb);
                sb.Append("<li>");
                bool firstBlock = true;
                foreach (MdNode c in n.Children())
                {
                    if (n.Task != 0 && firstBlock && c.Kind == MdKind.Paragraph)
                    {
                        string box = n.Task == 2 ? "<input checked=\"\" disabled=\"\" type=\"checkbox\"> " : "<input disabled=\"\" type=\"checkbox\"> ";
                        if (tight)
                        {
                            sb.Append(box);
                            Inlines(sb, c, gfm);
                        }
                        else
                        {
                            Cr(sb);
                            sb.Append("<p>").Append(box);
                            Inlines(sb, c, gfm);
                            sb.Append("</p>\n");
                        }
                    }
                    else
                    {
                        Block(sb, c, gfm, tight);
                    }

                    firstBlock = false;
                }

                sb.Append("</li>\n");
                break;
            case MdKind.Paragraph:
                if (tight)
                {
                    Inlines(sb, n, gfm);
                }
                else
                {
                    Cr(sb);
                    sb.Append("<p>");
                    Inlines(sb, n, gfm);
                    sb.Append("</p>\n");
                }

                break;
            case MdKind.Heading:
                Cr(sb);
                sb.Append("<h").Append(n.Level).Append('>');
                Inlines(sb, n, gfm);
                sb.Append("</h").Append(n.Level).Append(">\n");
                break;
            case MdKind.ThematicBreak:
                Cr(sb);
                sb.Append("<hr />\n");
                break;
            case MdKind.CodeBlock:
                Cr(sb);
                string lang = n.Info.Split(' ', '\t')[0];
                sb.Append("<pre><code").Append(lang.Length > 0 ? " class=\"language-" + Escape(lang) + "\"" : string.Empty).Append('>').Append(Escape(n.Literal)).Append("</code></pre>\n");
                break;
            case MdKind.HtmlBlock:
                Cr(sb);
                sb.Append(Filter(n.Literal, gfm)).Append('\n');
                break;
            case MdKind.Table:
                Cr(sb);
                sb.Append("<table>\n<thead>\n");
                bool body = false;
                foreach (MdNode row in n.Children())
                {
                    if (!row.IsHeader && !body)
                    {
                        sb.Append("</thead>\n<tbody>\n");
                        body = true;
                    }

                    sb.Append("<tr>\n");
                    foreach (MdNode cell in row.Children())
                    {
                        string tag = row.IsHeader ? "th" : "td";
                        string align = cell.Align switch { 1 => " align=\"left\"", 2 => " align=\"center\"", 3 => " align=\"right\"", _ => string.Empty };
                        sb.Append('<').Append(tag).Append(align).Append('>');
                        Inlines(sb, cell, gfm);
                        sb.Append("</").Append(tag).Append(">\n");
                    }

                    sb.Append("</tr>\n");
                }

                sb.Append(body ? "</tbody>\n" : "</thead>\n").Append("</table>\n");
                break;
            case MdKind.FootnoteDefinition:
                break;
        }
    }

    private static void Inlines(StringBuilder sb, MdNode parent, bool gfm)
    {
        foreach (MdNode n in parent.Children())
        {
            Inline(sb, n, gfm);
        }
    }

    private static void Inline(StringBuilder sb, MdNode n, bool gfm)
    {
        switch (n.Kind)
        {
            case MdKind.Text:
                sb.Append(Escape(n.Literal));
                break;
            case MdKind.SoftBreak:
                sb.Append('\n');
                break;
            case MdKind.LineBreak:
                sb.Append("<br />\n");
                break;
            case MdKind.Code:
                sb.Append("<code>").Append(Escape(n.Literal)).Append("</code>");
                break;
            case MdKind.HtmlInline:
                sb.Append(Filter(n.Literal, gfm));
                break;
            case MdKind.Emph:
                sb.Append("<em>");
                Inlines(sb, n, gfm);
                sb.Append("</em>");
                break;
            case MdKind.Strong:
                sb.Append("<strong>");
                Inlines(sb, n, gfm);
                sb.Append("</strong>");
                break;
            case MdKind.Strike:
                sb.Append("<del>");
                Inlines(sb, n, gfm);
                sb.Append("</del>");
                break;
            case MdKind.Link:
                sb.Append("<a href=\"").Append(Escape(MdText.EncodeUri(n.Destination))).Append('"');
                if (n.Title.Length > 0)
                {
                    sb.Append(" title=\"").Append(Escape(n.Title)).Append('"');
                }

                sb.Append('>');
                Inlines(sb, n, gfm);
                sb.Append("</a>");
                break;
            case MdKind.Image:
                var alt = new StringBuilder();
                PlainText(alt, n);
                sb.Append("<img src=\"").Append(Escape(MdText.EncodeUri(n.Destination))).Append("\" alt=\"").Append(Escape(alt.ToString())).Append('"');
                if (n.Title.Length > 0)
                {
                    sb.Append(" title=\"").Append(Escape(n.Title)).Append('"');
                }

                sb.Append(" />");
                break;
        }
    }

    private static void PlainText(StringBuilder sb, MdNode n)
    {
        foreach (MdNode c in n.Children())
        {
            switch (c.Kind)
            {
                case MdKind.Text or MdKind.Code:
                    sb.Append(c.Literal);
                    break;
                case MdKind.SoftBreak or MdKind.LineBreak:
                    sb.Append(' ');
                    break;
                default:
                    PlainText(sb, c);
                    break;
            }
        }
    }

    /// <summary>The comparison the specifications' own test runners make: white space between tags does not count.</summary>
    public static string Normalize(string html)
    {
        var sb = new StringBuilder(html.Length);
        int i = 0;
        while (i < html.Length)
        {
            if (html[i] == '>')
            {
                sb.Append('>');
                int j = i + 1;
                while (j < html.Length && char.IsWhiteSpace(html[j]))
                {
                    j++;
                }

                if (j < html.Length && html[j] == '<')
                {
                    i = j;
                    continue;
                }
            }
            else
            {
                sb.Append(html[i]);
            }

            i++;
        }

        return sb.ToString().Trim();
    }
}
