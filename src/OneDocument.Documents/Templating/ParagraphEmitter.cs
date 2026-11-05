using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>
/// Rebuilds one paragraph from what the engine returned for it. Each text piece becomes a run carrying the formatting of the run its segment came from,
/// the wrappers (hyperlink, tracked change, content control) the original run sat in are reopened around it, and every item the engine never saw is put back
/// once, in its original place.
/// </summary>
internal sealed class ParagraphEmitter(RenderContext context, StoryModel story)
{
    private readonly RenderContext _ctx = context;
    private readonly StoryModel _story = story;

    /// <summary>Emits the paragraph for a rendered unit.</summary>
    /// <param name="source">The source paragraph.</param>
    /// <param name="unit">What the engine produced for it.</param>
    /// <param name="sectionBreak">Section properties to add to the paragraph's properties (the last paragraph of a repeated final section), or empty.</param>
    public string Emit(StoryNode source, TemplateRenderedUnit unit, string sectionBreak)
    {
        ParagraphModel p = source.Paragraph!;
        if (IsUnchanged(p, unit))
        {
            return sectionBreak.Length == 0 ? p.Raw : WithSection(p, p.ParagraphProperties, BodyOf(p), sectionBreak);
        }

        if (TryBlockOnly(p, unit, out string blocks))
        {
            return blocks;
        }

        var sb = new StringBuilder();
        var chain = new List<RunWrapper>();
        int next = 0;
        string prefix = p.Prefix;

        void Switch(RunWrapper[] target)
        {
            int common = 0;
            while (common < chain.Count && common < target.Length && ReferenceEquals(chain[common], target[common]))
            {
                common++;
            }

            for (int i = chain.Count - 1; i >= common; i--)
            {
                sb.Append(chain[i].Close);
            }

            chain.RemoveRange(common, chain.Count - common);
            for (int i = common; i < target.Length; i++)
            {
                sb.Append(target[i].Open);
                chain.Add(target[i]);
            }
        }

        void FlushOpaque(int upTo)
        {
            while (next < upTo)
            {
                ParagraphItem item = p.Items[next++];
                if (item.Xml is not null)
                {
                    Switch(item.Chain);
                    sb.Append(item.Xml);
                }
            }
        }

        bool blockPieces = false;
        foreach (TemplatePiece piece in unit.Pieces)
        {
            int seg = Math.Clamp(piece.Segment, 0, Math.Max(0, p.Segments.Count - 1));
            ParagraphItem? segItem = p.Segments.Count > 0 ? p.Segments[seg] : null;
            FlushOpaque(segItem is null ? 0 : p.SegmentItem[seg]);
            switch (piece.Kind)
            {
                case TemplatePieceKind.Text:
                    if (piece.Text.Length > 0)
                    {
                        EmitText(sb, Switch, segItem, prefix, piece.Text);
                    }

                    break;
                case TemplatePieceKind.Image:
                    {
                        Switch(segItem?.Chain ?? []);
                        sb.Append(Insertions.Image(_ctx, p, segItem, piece.Insertion!));
                        break;
                    }

                default:
                    {
                        // HTML and Markdown inside a paragraph that has other text: the converted content goes inline, paragraphs joined by line breaks.
                        Switch(segItem?.Chain ?? []);
                        sb.Append(Insertions.Inline(_ctx, p, segItem, piece));
                        blockPieces = true;
                        break;
                    }
            }
        }

        FlushOpaque(p.Items.Count);
        Switch([]);
        _ = blockPieces;
        string content = sb.ToString();
        return WithSection(p, p.ParagraphProperties, content, sectionBreak);
    }

    private static string BodyOf(ParagraphModel p)
    {
        if (p.IsEmpty)
        {
            return string.Empty;
        }

        string raw = p.Raw;
        int start = p.StartTag.Length;
        int end = raw.Length - p.EndTag.Length;
        string inner = raw[start..end];
        return p.ParagraphProperties.Length > 0 && inner.StartsWith(p.ParagraphProperties, StringComparison.Ordinal) ? inner[p.ParagraphProperties.Length..] : inner;
    }

    private static string WithSection(ParagraphModel p, string pPr, string content, string sectionBreak)
    {
        if (sectionBreak.Length > 0)
        {
            pPr = AddSection(pPr, p.Prefix, sectionBreak);
        }

        string start = p.IsEmpty ? p.StartTag[..^2] + ">" : p.StartTag;
        string end = "</" + (p.Prefix.Length == 0 ? string.Empty : p.Prefix + ":") + "p>";
        if (p.IsEmpty && pPr.Length == 0 && content.Length == 0)
        {
            return p.StartTag;
        }

        return start + pPr + content + end;
    }

    // ECMA-376 Part 1 §17.3.1.26 (w:pPr): w:sectPr comes after the other paragraph properties and before w:pPrChange.
    private static string AddSection(string pPr, string prefix, string sectPr)
    {
        string p = prefix.Length == 0 ? string.Empty : prefix + ":";
        if (pPr.Length == 0)
        {
            return "<" + p + "pPr>" + sectPr + "</" + p + "pPr>";
        }

        if (pPr.EndsWith("/>", StringComparison.Ordinal))
        {
            return pPr[..^2] + ">" + sectPr + "</" + p + "pPr>";
        }

        int change = pPr.IndexOf("<" + p + "pPrChange", StringComparison.Ordinal);
        int at = change >= 0 ? change : pPr.LastIndexOf("</" + p + "pPr>", StringComparison.Ordinal);
        return at < 0 ? pPr : pPr.Insert(at, sectPr);
    }

    private static bool IsUnchanged(ParagraphModel p, TemplateRenderedUnit unit)
    {
        int count = p.Segments.Count;
        if (count == 0)
        {
            return unit.Pieces.Count == 0;
        }

        var texts = new StringBuilder?[count];
        foreach (TemplatePiece piece in unit.Pieces)
        {
            if (piece.Kind != TemplatePieceKind.Text || piece.Segment < 0 || piece.Segment >= count || piece.Text.Contains(LinkTokens.Open, StringComparison.Ordinal))
            {
                return false;
            }

            (texts[piece.Segment] ??= new StringBuilder()).Append(piece.Text);
        }

        for (int i = 0; i < count; i++)
        {
            string have = texts[i]?.ToString() ?? string.Empty;
            if (!string.Equals(have, p.Segments[i].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // A paragraph that holds only an HTML or Markdown tag is replaced by the converted paragraphs.
    private bool TryBlockOnly(ParagraphModel p, TemplateRenderedUnit unit, out string blocks)
    {
        blocks = string.Empty;
        bool any = false;
        foreach (TemplatePiece piece in unit.Pieces)
        {
            if (piece.Kind is TemplatePieceKind.Html or TemplatePieceKind.Markdown)
            {
                any = true;
            }
            else if (piece.Kind != TemplatePieceKind.Text || !string.IsNullOrWhiteSpace(piece.Text))
            {
                return false;
            }
        }

        if (!any)
        {
            return false;
        }

        var sb = new StringBuilder();
        foreach (TemplatePiece piece in unit.Pieces)
        {
            if (piece.Kind is TemplatePieceKind.Html or TemplatePieceKind.Markdown)
            {
                sb.Append(Insertions.Blocks(_ctx, p, piece));
            }
        }

        blocks = sb.ToString();
        return true;
    }

    private void EmitText(StringBuilder sb, Action<RunWrapper[]> switchChain, ParagraphItem? seg, string prefix, string text)
    {
        RunWrapper[] chain = seg?.Chain ?? [];
        string runStart = seg?.RunStart is { Length: > 0 } s ? s : "<" + (prefix.Length == 0 ? string.Empty : prefix + ":") + "r>";
        string rPr = seg?.RunProperties ?? string.Empty;
        string closeRun = "</" + (prefix.Length == 0 ? string.Empty : prefix + ":") + "r>";

        int pos = 0;
        while (pos < text.Length)
        {
            int open = text.IndexOf(LinkTokens.Open, pos);
            int close = open < 0 ? -1 : text.IndexOf(LinkTokens.Close, open + 1);
            if (open < 0 || close < 0)
            {
                switchChain(chain);
                sb.Append(runStart).Append(rPr).Append(XmlSnip.RunContent(prefix, text[pos..])).Append(closeRun);
                return;
            }

            if (open > pos)
            {
                switchChain(chain);
                sb.Append(runStart).Append(rPr).Append(XmlSnip.RunContent(prefix, text[pos..open])).Append(closeRun);
            }

            if (int.TryParse(text.AsSpan(open + 1, close - open - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index) && _ctx.Link(index) is { } link)
            {
                switchChain(chain);
                sb.Append(Insertions.Hyperlink(_ctx, prefix, runStart, rPr, link.Url, link.Text));
            }

            pos = close + 1;
        }
    }
}
