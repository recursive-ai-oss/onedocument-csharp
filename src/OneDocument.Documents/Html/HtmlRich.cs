using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Templating;

namespace RecursiveAI.OneDocument.Documents.Html;

/// <summary>
/// The template engine's view of the importer: the same reader and sanitising, flattened to paragraphs, runs and nested lists (no tables, pictures or
/// colours), because a template tag inserts into the middle of a document that already has its own styles.
/// </summary>
internal static class HtmlRich
{
    public static List<RichBlock> Convert(string html, OpenContext context, SortedSet<string> dropped, CancellationToken cancellationToken = default)
    {
        var loss = new LossReport();
        // Fragments are read as a UTF-8 page, so a <meta charset> inside the value cannot change how the text is decoded.
        byte[] bytes = [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE html><body>" + html)];
        HtmlContent content = new HtmlReader(new HtmlImportOptions(), context, loss, cancellationToken).Read(bytes);
        foreach (LossItem item in loss.Items)
        {
            dropped.Add(item.Feature);
        }

        var blocks = new List<RichBlock>();
        Flatten(content, content.Blocks, blocks, dropped);
        return blocks;
    }

    private static void Flatten(HtmlContent content, List<HBlock> source, List<RichBlock> target, SortedSet<string> dropped)
    {
        foreach (HBlock b in source)
        {
            if (b is HTable t)
            {
                // Tables are the document importer's job; here their text is kept, one paragraph per cell block.
                dropped.Add("table structure");
                foreach (HRow row in t.Rows)
                {
                    foreach (HCell cell in row.Cells)
                    {
                        Flatten(content, cell.Blocks, target, dropped);
                    }
                }

                continue;
            }

            if (b is not HPara p || p.Rule)
            {
                continue;
            }

            string? style = TemplateStyle(p.StyleId);
            HStyle? def = content.Styles.TryGetValue(p.StyleId, out HStyle? d) ? d : null;
            var runs = new List<RichRun>();
            foreach (HInline inl in p.Inlines)
            {
                switch (inl)
                {
                    case HText x:
                        {
                            HRunProps r = x.Props;
                            HRunProps baseline = def?.Run ?? content.DefaultRun;
                            bool mono = r.Font is "Courier New" or "Consolas" or "Lucida Console";
                            runs.Add(new RichRun(x.Text, r.Bold && !baseline.Bold, r.Italic && !baseline.Italic, r.Underline && x.Link is null, r.Strike, mono && baseline.Font != r.Font, x.Link));
                            break;
                        }

                    case HBreak:
                        runs.Add(new RichRun(string.Empty, Break: true));
                        break;
                    case HImage:
                        dropped.Add("<img>");
                        break;
                }
            }

            if (runs.Count == 0 || !runs.Exists(static r => r.Break || r.Text.Trim().Length > 0))
            {
                continue;
            }

            bool inList = p.List is not null;
            target.Add(new RichBlock(inList ? null : style, inList ? p.List!.Depth : -1, p.List?.Ordered ?? false, p.List?.Id ?? 0, runs));
        }
    }

    private static string? TemplateStyle(string id)
    {
        int cut = id.LastIndexOf('_');
        string tag = cut >= 0 ? id[(cut + 1)..] : id;
        return tag switch
        {
            "Heading1" => "h1",
            "Heading2" => "h2",
            "Heading3" => "h3",
            "Heading4" => "h4",
            "Heading5" => "h5",
            "Heading6" => "h6",
            "Quote" => "quote",
            "HTMLPreformatted" => "code",
            _ => null,
        };
    }
}
