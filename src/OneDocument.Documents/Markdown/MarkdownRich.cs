using RecursiveAI.OneDocument.Documents.Templating;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>
/// Turns Markdown into the small paragraph-and-run vocabulary the template engine inserts (headings, quotes, code lines, list items, formatted runs). It reads the text
/// with the same parser as the Markdown importer, so a Markdown value in a template and a Markdown file mean the same thing; what the vocabulary cannot hold (tables,
/// pictures, raw HTML, rules) is left out and named in <c>dropped</c>.
/// </summary>
internal sealed class MarkdownRich
{
    private readonly List<RichBlock> _output = [];
    private readonly SortedSet<string> _dropped;
    private readonly int _maxDepth;
    private int _listIds;

    private MarkdownRich(SortedSet<string> dropped, int maxDepth)
    {
        _dropped = dropped;
        _maxDepth = maxDepth;
    }

    // Where a block sits: inside a quote, and in which list item (level from 0, or -1 outside lists).
    private readonly record struct Place(bool Quote, int ListLevel, bool Ordered, int ListId);

    public static List<RichBlock> Convert(string markdown, SortedSet<string> dropped, int maxDepth)
    {
        var options = new MarkdownParserOptions { Footnotes = false, MaxDepth = Math.Max(8, maxDepth) };
        MarkdownParseResult parsed = MarkdownParser.Parse(markdown, options);
        if (parsed.Issues.Count > 0)
        {
            dropped.Add("content nested deeper than " + options.MaxDepth + " levels");
        }

        var rich = new MarkdownRich(dropped, options.MaxDepth);
        rich.Blocks(parsed.Root, new Place(false, -1, false, -1), 0);
        return rich._output;
    }

    private void Blocks(MdNode parent, Place place, int depth)
    {
        for (MdNode? n = parent.FirstChild; n is not null; n = n.Next)
        {
            Block(n, place, depth);
        }
    }

    private void Block(MdNode n, Place place, int depth)
    {
        if (depth > _maxDepth)
        {
            _dropped.Add("content nested deeper than " + _maxDepth + " levels");
            return;
        }

        switch (n.Kind)
        {
            case MdKind.Paragraph:
                _output.Add(new RichBlock(place.Quote ? "quote" : null, place.ListLevel, place.Ordered, place.ListId, Runs(n)));
                break;
            case MdKind.Heading:
                _output.Add(new RichBlock("h" + n.Level, place.ListLevel, place.Ordered, place.ListId, Runs(n)));
                break;
            case MdKind.BlockQuote:
                Blocks(n, place with { Quote = true }, depth + 1);
                break;
            case MdKind.CodeBlock:
                {
                    string literal = n.Literal.EndsWith('\n') ? n.Literal[..^1] : n.Literal;
                    foreach (string line in literal.Split('\n'))
                    {
                        _output.Add(new RichBlock("code", -1, false, -1, [new RichRun(line, Code: true)]));
                    }

                    break;
                }

            case MdKind.List:
                List(n, place, depth);
                break;
            case MdKind.Table:
                _dropped.Add("table structure");
                foreach (MdNode row in n.Children())
                {
                    foreach (MdNode cell in row.Children())
                    {
                        List<RichRun> runs = Runs(cell);
                        if (runs.Count > 0)
                        {
                            _output.Add(new RichBlock(null, -1, false, -1, runs));
                        }
                    }
                }

                break;
            case MdKind.ThematicBreak:
                _dropped.Add("thematic break");
                break;
            case MdKind.HtmlBlock:
                _dropped.Add("inline HTML");
                break;
        }
    }

    private void List(MdNode list, Place place, int depth)
    {
        int id = _listIds++;
        int level = Math.Min(place.ListLevel + 1, 8);
        var inItem = new Place(place.Quote, level, list.Ordered, id);
        for (MdNode? item = list.FirstChild; item is not null; item = item.Next)
        {
            bool first = true;
            for (MdNode? child = item.FirstChild; child is not null; child = child.Next)
            {
                if (first && child.Kind is MdKind.Paragraph or MdKind.Heading)
                {
                    // The first paragraph of an item carries its marker.
                    first = false;
                    Block(child, inItem, depth + 1);
                    continue;
                }

                first = false;
                // Nested lists are one level deeper; any other further block of the item is an ordinary paragraph or block under it.
                Block(child, child.Kind == MdKind.List ? inItem : place with { ListLevel = -1 }, depth + 1);
            }
        }
    }

    private List<RichRun> Runs(MdNode block)
    {
        var runs = new List<RichRun>();
        Inlines(block, new Format(false, false, false, null), runs, 0);
        return runs;
    }

    private readonly record struct Format(bool Bold, bool Italic, bool Strike, string? Link);

    private void Inlines(MdNode parent, Format f, List<RichRun> runs, int depth)
    {
        if (depth > 64)
        {
            runs.Add(new RichRun(MarkdownImporter.PlainText(parent), f.Bold, f.Italic, false, f.Strike, false, f.Link));
            return;
        }

        for (MdNode? n = parent.FirstChild; n is not null; n = n.Next)
        {
            switch (n.Kind)
            {
                case MdKind.Text:
                    runs.Add(new RichRun(n.Literal, f.Bold, f.Italic, false, f.Strike, false, f.Link));
                    break;
                case MdKind.SoftBreak:
                    runs.Add(new RichRun(" ", f.Bold, f.Italic, false, f.Strike, false, f.Link));
                    break;
                case MdKind.LineBreak:
                    runs.Add(new RichRun(string.Empty, Break: true));
                    break;
                case MdKind.Code:
                    runs.Add(new RichRun(n.Literal, f.Bold, f.Italic, false, f.Strike, true, f.Link));
                    break;
                case MdKind.Emph:
                    Inlines(n, f with { Italic = true }, runs, depth + 1);
                    break;
                case MdKind.Strong:
                    Inlines(n, f with { Bold = true }, runs, depth + 1);
                    break;
                case MdKind.Strike:
                    Inlines(n, f with { Strike = true }, runs, depth + 1);
                    break;
                case MdKind.Link:
                    if (LinkTokens.IsAllowedTarget(n.Destination))
                    {
                        Inlines(n, f with { Link = n.Destination }, runs, depth + 1);
                    }
                    else
                    {
                        _dropped.Add("link target not http, https, mailto, tel or #anchor");
                        Inlines(n, f, runs, depth + 1);
                    }

                    break;
                case MdKind.Image:
                    _dropped.Add("image");
                    break;
                case MdKind.HtmlInline:
                    _dropped.Add("inline HTML");
                    break;
            }
        }
    }
}
