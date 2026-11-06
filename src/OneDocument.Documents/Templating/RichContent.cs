using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Markdown;
using RecursiveAI.OneDocument.Templating;
using OneHtmlDocument = RecursiveAI.OneHtml.Document;
using OneHtmlNode = RecursiveAI.OneHtml.Node;
using OneHtmlNodeKind = RecursiveAI.OneHtml.NodeKind;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>One stretch of formatted text in converted HTML or Markdown.</summary>
internal sealed record RichRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false, bool Code = false, string? Link = null, bool Break = false);

/// <summary>A converted paragraph: its style, its list membership and its runs.</summary>
/// <param name="Style">A heading level 1 to 6 as "h1".."h6", "quote", "code", or null for a body paragraph.</param>
/// <param name="ListLevel">The nesting level of a list item (from 0), or -1 when the paragraph is not in a list.</param>
/// <param name="Ordered">Whether the list is numbered.</param>
/// <param name="ListId">Identifies the list, so each numbered list starts again at 1.</param>
/// <param name="Runs">The text.</param>
internal sealed record RichBlock(string? Style, int ListLevel, bool Ordered, int ListId, List<RichRun> Runs);

/// <summary>
/// Converts the value of an <c>html</c> or <c>markdown</c> tag into paragraphs (Markdown through the parser in <c>Markdown/</c>). Only a small, safe vocabulary survives: paragraphs, headings, bold, italic,
/// underline, strike-through, code, links, line breaks, quotes and nested lists. Everything else (scripts, styles, forms, images, tables, event attributes)
/// is dropped, and the drop is reported, so the HTML never injects markup of its own into the document.
/// </summary>
internal static class RichContent
{
    public static IReadOnlyList<RichBlock> Convert(RenderContext ctx, TemplatePiece piece)
    {
        string text = piece.Insertion?.Value is { Kind: TemplateDataKind.Scalar } v ? System.Convert.ToString(v.Raw, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
        var dropped = new SortedSet<string>(StringComparer.Ordinal);
        int max = (int)Math.Min(int.MaxValue, (ctx.Options.Limits ?? OfficeLimits.Default).MaxPartSize);
        if (text.Length > max)
        {
            dropped.Add("content over the part size limit");
            text = text[..max];
        }

        var list = piece.Kind == TemplatePieceKind.Html ? new HtmlConverter(dropped).Convert(text) : MarkdownRich.Convert(text, dropped, (ctx.Options.Limits ?? OfficeLimits.Default).MaxRecursionDepth);
        if (dropped.Count > 0)
        {
            ctx.Warnings.Add(new Defect(DefectKind.Unsupported, "template.markup-dropped", DefectSeverity.Dropped,
                "The " + (piece.Kind == TemplatePieceKind.Html ? "html" : "markdown") + " tag contained things that were left out: " + string.Join(", ", dropped) + ".",
                DefectLocation.InPart(ctx.CurrentPart)));
        }

        return list;
    }

    // ---- HTML ------------------------------------------------------------------------------------------------------------------

    private sealed class HtmlConverter(SortedSet<string> dropped)
    {
        private readonly List<RichBlock> _blocks = [];
        private List<RichRun>? _current;
        private string? _currentStyle;
        private int _currentList = -1;
        private bool _currentOrdered;
        private int _currentListId;
        private int _nextListId;
        private readonly Stack<(bool Ordered, int Id)> _lists = new();
        private int _depth;

        public List<RichBlock> Convert(string html)
        {
            OneHtmlDocument doc = OneHtmlDocument.Parse("<!DOCTYPE html><body>" + html);
            OneHtmlNode body = doc.Root.QuerySelector("body");
            if (!body.IsNull)
            {
                Walk(body, new Format());
            }

            Flush();
            return _blocks;
        }

        private readonly record struct Format(bool Bold, bool Italic, bool Underline, bool Strike, bool Code, string? Link);

        private void Walk(OneHtmlNode parent, Format f)
        {
            if (++_depth > 200)
            {
                dropped.Add("content nested deeper than 200 levels");
                _depth--;
                return;
            }

            for (OneHtmlNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind == OneHtmlNodeKind.Text)
                {
                    AddText(n.Data.ToString(), f, preserve: false);
                }
                else if (n.IsElement)
                {
                    Element(n, f);
                }
            }

            _depth--;
        }

        private void Element(OneHtmlNode e, Format f)
        {
            string name = e.LocalName;
            switch (name)
            {
                case "script" or "style" or "template" or "iframe" or "object" or "embed" or "noscript" or "head" or "title" or "meta" or "link" or "svg" or "math" or "form" or "input" or "button" or "select" or "textarea":
                    dropped.Add("<" + name + ">");
                    return;
                case "img":
                    dropped.Add("<img>");
                    return;
                case "table" or "thead" or "tbody" or "tfoot" or "tr" or "td" or "th":
                    // Tables are the importer's job; their text is kept, one paragraph per cell.
                    dropped.Add("table structure");
                    Flush();
                    Walk(e, f);
                    Flush();
                    return;
                case "p" or "div" or "section" or "article" or "header" or "footer" or "main" or "nav" or "aside" or "figure" or "figcaption" or "address" or "details" or "summary" or "dl" or "dt" or "dd":
                    Flush();
                    Walk(e, f);
                    Flush();
                    return;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Flush();
                    _currentStyle = name;
                    Walk(e, f);
                    Flush();
                    return;
                case "blockquote":
                    Flush();
                    _currentStyle = "quote";
                    Walk(e, f);
                    Flush();
                    return;
                case "pre":
                    Flush();
                    _currentStyle = "code";
                    AddText(e.TextContent ?? string.Empty, f with { Code = true }, preserve: true);
                    Flush();
                    return;
                case "ul" or "ol":
                    Flush();
                    _lists.Push((name == "ol", _nextListId++));
                    Walk(e, f);
                    _lists.Pop();
                    Flush();
                    return;
                case "li":
                    Flush();
                    Item(e, f);
                    return;
                case "br":
                    EnsureBlock();
                    _current!.Add(new RichRun(string.Empty, Break: true));
                    return;
                case "hr":
                    Flush();
                    return;
                case "b" or "strong":
                    Walk(e, f with { Bold = true });
                    return;
                case "i" or "em" or "cite" or "dfn" or "var":
                    Walk(e, f with { Italic = true });
                    return;
                case "u" or "ins":
                    Walk(e, f with { Underline = true });
                    return;
                case "s" or "strike" or "del":
                    Walk(e, f with { Strike = true });
                    return;
                case "code" or "kbd" or "samp" or "tt":
                    Walk(e, f with { Code = true });
                    return;
                case "a":
                    {
                        string? href = e.GetAttribute("href")?.Trim();
                        if (href is not null && LinkTokens.IsAllowedTarget(href))
                        {
                            Walk(e, f with { Link = href });
                        }
                        else
                        {
                            if (href is not null)
                            {
                                dropped.Add("link target not http, https, mailto, tel or #anchor");
                            }

                            Walk(e, f);
                        }

                        return;
                    }

                default:
                    // Unknown or purely presentational inline elements (span, font, small, sup...) keep their text.
                    Walk(e, f);
                    return;
            }
        }

        private void Item(OneHtmlNode li, Format f)
        {
            (bool ordered, int id) = _lists.Count > 0 ? _lists.Peek() : (false, _nextListId++);
            _currentList = Math.Max(0, _lists.Count - 1);
            _currentOrdered = ordered;
            _currentListId = id;
            // Text before a nested list is the item's own paragraph; nested lists start their own.
            for (OneHtmlNode n = li.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.IsElement && n.LocalName is "ul" or "ol")
                {
                    Flush();
                    Element(n, f);
                    _currentList = Math.Max(0, _lists.Count - 1);
                    _currentOrdered = ordered;
                    _currentListId = id;
                }
                else if (n.Kind == OneHtmlNodeKind.Text)
                {
                    AddText(n.Data.ToString(), f, preserve: false);
                }
                else if (n.IsElement)
                {
                    Element(n, f);
                }
            }

            Flush();
            _currentList = -1;
        }

        private void EnsureBlock() => _current ??= [];

        private void AddText(string text, Format f, bool preserve)
        {
            if (!preserve)
            {
                // CSS white-space collapsing, simplified: runs of white space become one space; leading space of a block is dropped.
                var sb = new StringBuilder(text.Length);
                bool space = false;
                foreach (char c in text)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        space = true;
                        continue;
                    }

                    if (space && (sb.Length > 0 || (_current is { Count: > 0 } cur && !EndsWithBreak(cur) && !EndsWithSpace(cur))))
                    {
                        sb.Append(' ');
                    }

                    space = false;
                    sb.Append(c);
                }

                if (space && sb.Length > 0 || (space && _current is { Count: > 0 } c2 && !EndsWithBreak(c2) && !EndsWithSpace(c2)))
                {
                    sb.Append(' ');
                }

                text = sb.ToString();
                if (text.Length == 0)
                {
                    return;
                }
            }

            EnsureBlock();
            _current!.Add(new RichRun(text, f.Bold, f.Italic, f.Underline, f.Strike, f.Code, f.Link));
        }

        private static bool EndsWithBreak(List<RichRun> runs) => runs[^1].Break;

        private static bool EndsWithSpace(List<RichRun> runs) => runs[^1].Text.EndsWith(' ');

        private void Flush()
        {
            if (_current is { Count: > 0 } runs && runs.Exists(static r => r.Break || r.Text.Trim().Length > 0))
            {
                // Trim trailing breaks and spaces.
                while (runs.Count > 0 && (runs[^1].Break || runs[^1].Text.Trim().Length == 0))
                {
                    runs.RemoveAt(runs.Count - 1);
                }

                if (runs.Count > 0 && runs[^1].Text.EndsWith(' '))
                {
                    runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd() };
                }

                _blocks.Add(new RichBlock(_currentStyle, _currentList, _currentOrdered, _currentListId, runs));
            }

            _current = null;
            if (_currentList < 0)
            {
                _currentStyle = null;
            }
            else
            {
                _currentStyle = null;
            }
        }
    }
}
