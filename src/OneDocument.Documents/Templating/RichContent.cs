using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Templating;

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
/// Converts the value of an <c>html</c> or <c>markdown</c> tag into paragraphs. Only a small, safe vocabulary survives: paragraphs, headings, bold, italic,
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

        var list = piece.Kind == TemplatePieceKind.Html ? Html.HtmlRich.Convert(text, new OpenContext(ctx.Options.Limits ?? OfficeLimits.Default), dropped) : new MarkdownConverter(dropped).Convert(text);
        if (dropped.Count > 0)
        {
            ctx.Warnings.Add(new Defect(DefectKind.Unsupported, "template.markup-dropped", DefectSeverity.Dropped,
                "The " + (piece.Kind == TemplatePieceKind.Html ? "html" : "markdown") + " tag contained things that were left out: " + string.Join(", ", dropped) + ".",
                DefectLocation.InPart(ctx.CurrentPart)));
        }

        return list;
    }

    // ---- Markdown --------------------------------------------------------------------------------------------------------------

    private sealed class MarkdownConverter(SortedSet<string> dropped)
    {
        private readonly List<RichBlock> _blocks = [];
        private int _listCounter;

        public List<RichBlock> Convert(string markdown)
        {
            string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var para = new List<string>();
            int i = 0;
            // The list the current items belong to: indentation of each open level, and its id.
            var listIndents = new List<int>();
            int listId = -1;
            bool listOrdered = false;

            void FlushParagraph(string? style = null)
            {
                if (para.Count == 0)
                {
                    return;
                }

                _blocks.Add(new RichBlock(style, -1, false, -1, Inline(string.Join("\n", para))));
                para.Clear();
            }

            while (i < lines.Length)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                int indent = line.Length - trimmed.Length;

                if (trimmed.Length == 0)
                {
                    FlushParagraph();
                    listIndents.Clear();
                    i++;
                    continue;
                }

                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    string fence = trimmed[..3];
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal))
                    {
                        _blocks.Add(new RichBlock("code", -1, false, -1, [new RichRun(lines[i], Code: true)]));
                        i++;
                    }

                    i++;
                    continue;
                }

                int hashes = 0;
                while (hashes < trimmed.Length && trimmed[hashes] == '#')
                {
                    hashes++;
                }

                if (indent < 4 && hashes is >= 1 and <= 6 && (hashes == trimmed.Length || trimmed[hashes] == ' '))
                {
                    FlushParagraph();
                    string content = trimmed[hashes..].Trim().TrimEnd('#').TrimEnd();
                    _blocks.Add(new RichBlock("h" + hashes, -1, false, -1, Inline(content)));
                    i++;
                    continue;
                }

                if (indent < 4 && IsThematicBreak(trimmed))
                {
                    FlushParagraph();
                    dropped.Add("thematic break");
                    i++;
                    continue;
                }

                if (trimmed.StartsWith('>'))
                {
                    FlushParagraph();
                    var quote = new List<string>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                    {
                        string q = lines[i].TrimStart()[1..];
                        quote.Add(q.StartsWith(' ') ? q[1..] : q);
                        i++;
                    }

                    _blocks.Add(new RichBlock("quote", -1, false, -1, Inline(string.Join("\n", quote))));
                    continue;
                }

                (bool isItem, bool ordered, int markerWidth) = ListMarker(trimmed);
                if (isItem)
                {
                    FlushParagraph();
                    int level = 0;
                    while (level < listIndents.Count && indent >= listIndents[level] + 2)
                    {
                        level++;
                    }

                    if (listIndents.Count == 0 || level == 0 && indent < listIndents[0] + 2 && ordered != listOrdered)
                    {
                        listId = _listCounter++;
                        listOrdered = ordered;
                        listIndents.Clear();
                        level = 0;
                    }

                    if (level >= listIndents.Count)
                    {
                        listIndents.Add(indent);
                        level = listIndents.Count - 1;
                    }
                    else
                    {
                        listIndents.RemoveRange(level + 1, listIndents.Count - level - 1);
                    }

                    _blocks.Add(new RichBlock(null, Math.Min(level, 8), ordered, listId, Inline(trimmed[markerWidth..].Trim())));
                    i++;
                    continue;
                }

                para.Add(indent >= 4 && para.Count == 0 ? trimmed : trimmed);
                // A line ending in two spaces or a backslash is a hard break; handled by Inline on the joined text.
                i++;
            }

            FlushParagraph();
            return _blocks;
        }

        private static bool IsThematicBreak(string t)
        {
            string s = t.Replace(" ", string.Empty, StringComparison.Ordinal);
            return s.Length >= 3 && (s.All(static c => c == '-') || s.All(static c => c == '*') || s.All(static c => c == '_'));
        }

        private static (bool, bool, int) ListMarker(string t)
        {
            if (t.Length >= 2 && (t[0] is '-' or '*' or '+') && t[1] == ' ')
            {
                return (true, false, 2);
            }

            int d = 0;
            while (d < t.Length && d < 9 && char.IsAsciiDigit(t[d]))
            {
                d++;
            }

            if (d > 0 && d + 1 < t.Length && t[d] is '.' or ')' && t[d + 1] == ' ')
            {
                return (true, true, d + 2);
            }

            return (false, false, 0);
        }

        // Inline spans: code, emphasis, strong, strike-through, links, autolinks, backslash escapes, hard breaks.
        private List<RichRun> Inline(string text)
        {
            var runs = new List<RichRun>();
            ParseInline(text, new Style(), runs);
            return runs;
        }

        private readonly record struct Style(bool Bold, bool Italic, bool Strike, string? Link);

        private void ParseInline(string s, Style st, List<RichRun> runs)
        {
            var buf = new StringBuilder();

            void Emit()
            {
                if (buf.Length > 0)
                {
                    runs.Add(new RichRun(buf.ToString(), st.Bold, st.Italic, false, st.Strike, false, st.Link));
                    buf.Clear();
                }
            }

            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    if (s[i + 1] == '\n')
                    {
                        Emit();
                        runs.Add(new RichRun(string.Empty, Break: true));
                        i += 2;
                        continue;
                    }

                    if (char.IsAsciiLetterOrDigit(s[i + 1]) || char.IsWhiteSpace(s[i + 1]))
                    {
                        buf.Append(c);
                        i++;
                        continue;
                    }

                    buf.Append(s[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '\n')
                {
                    bool hard = buf.Length >= 2 && buf[^1] == ' ' && buf[^2] == ' ';
                    while (buf.Length > 0 && buf[^1] == ' ')
                    {
                        buf.Length--;
                    }

                    if (hard)
                    {
                        Emit();
                        runs.Add(new RichRun(string.Empty, Break: true));
                    }
                    else
                    {
                        buf.Append(' ');
                    }

                    i++;
                    continue;
                }

                if (c == '`')
                {
                    int ticks = 1;
                    while (i + ticks < s.Length && s[i + ticks] == '`')
                    {
                        ticks++;
                    }

                    int close = s.IndexOf(new string('`', ticks), i + ticks, StringComparison.Ordinal);
                    if (close > 0)
                    {
                        Emit();
                        runs.Add(new RichRun(s[(i + ticks)..close].Trim(), st.Bold, st.Italic, false, st.Strike, true, st.Link));
                        i = close + ticks;
                        continue;
                    }
                }

                if ((c == '*' || c == '_') && TryEmphasis(s, i, c, st, runs, Emit, out int next))
                {
                    i = next;
                    continue;
                }

                if (c == '~' && i + 1 < s.Length && s[i + 1] == '~')
                {
                    int close = s.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (close > 0)
                    {
                        Emit();
                        ParseInline(s[(i + 2)..close], st with { Strike = true }, runs);
                        i = close + 2;
                        continue;
                    }
                }

                if (c == '!' && i + 1 < s.Length && s[i + 1] == '[')
                {
                    int rb = s.IndexOf("](", i, StringComparison.Ordinal);
                    int rp = rb < 0 ? -1 : s.IndexOf(')', rb);
                    if (rp > 0)
                    {
                        dropped.Add("image");
                        i = rp + 1;
                        continue;
                    }
                }

                if (c == '[')
                {
                    int rb = FindClosingBracket(s, i);
                    if (rb > 0 && rb + 1 < s.Length && s[rb + 1] == '(')
                    {
                        int rp = s.IndexOf(')', rb + 2);
                        if (rp > 0)
                        {
                            string target = s[(rb + 2)..rp].Trim();
                            int space = target.IndexOf(' ', StringComparison.Ordinal);
                            if (space > 0)
                            {
                                target = target[..space];
                            }

                            target = target.Trim('<', '>');
                            Emit();
                            if (LinkTokens.IsAllowedTarget(target))
                            {
                                ParseInline(s[(i + 1)..rb], st with { Link = target }, runs);
                            }
                            else
                            {
                                dropped.Add("link target not http, https, mailto, tel or #anchor");
                                ParseInline(s[(i + 1)..rb], st, runs);
                            }

                            i = rp + 1;
                            continue;
                        }
                    }
                }

                if (c == '<')
                {
                    int gt = s.IndexOf('>', i);
                    if (gt > 0)
                    {
                        string inner = s[(i + 1)..gt];
                        if (inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || inner.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                        {
                            Emit();
                            runs.Add(new RichRun(inner, st.Bold, st.Italic, false, st.Strike, false, inner));
                            i = gt + 1;
                            continue;
                        }

                        if (inner.Length > 0 && (char.IsAsciiLetter(inner[0]) || inner[0] == '/') && !inner.Contains(' ', StringComparison.Ordinal) || inner.Contains('=', StringComparison.Ordinal))
                        {
                            dropped.Add("inline HTML");
                            i = gt + 1;
                            continue;
                        }
                    }
                }

                buf.Append(c);
                i++;
            }

            Emit();
        }

        private static int FindClosingBracket(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '\\')
                {
                    i++;
                }
                else if (s[i] == '[')
                {
                    depth++;
                }
                else if (s[i] == ']' && --depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryEmphasis(string s, int i, char marker, Style st, List<RichRun> runs, Action emit, out int next)
        {
            next = i;
            int count = 1;
            while (i + count < s.Length && s[i + count] == marker && count < 3)
            {
                count++;
            }

            // Left-flanking: the delimiter run must be followed by non-white space (CommonMark 0.31 §6.2); an underscore inside a word is literal.
            if (i + count >= s.Length || char.IsWhiteSpace(s[i + count]) || (marker == '_' && i > 0 && char.IsLetterOrDigit(s[i - 1])))
            {
                return false;
            }

            string delimiter = new(marker, count);
            int close = i + count;
            while (true)
            {
                close = s.IndexOf(delimiter, close, StringComparison.Ordinal);
                if (close < 0)
                {
                    return false;
                }

                if (close > i + count && !char.IsWhiteSpace(s[close - 1]) && !(marker == '_' && close + count < s.Length && char.IsLetterOrDigit(s[close + count])))
                {
                    break;
                }

                close += count;
            }

            emit();
            Style inner = count switch
            {
                1 => st with { Italic = true },
                2 => st with { Bold = true },
                _ => st with { Bold = true, Italic = true },
            };
            ParseInline(s[(i + count)..close], inner, runs);
            next = close + count;
            return true;
        }
    }
}
