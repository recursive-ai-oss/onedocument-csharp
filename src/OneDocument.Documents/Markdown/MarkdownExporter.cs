using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Notes;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Properties;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.TextExtraction;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>
/// Writes a word-processing document as GitHub Flavored Markdown. The body is read through the hub (paragraphs with resolved styles, numbering labels,
/// tables, notes); headings, lists, quotes, code, tables, links, pictures and footnotes become their Markdown forms and everything else goes to the loss report.
/// </summary>
internal sealed partial class MarkdownExporter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    private readonly Document _doc;
    private readonly MarkdownExportOptions _options;
    private readonly LossReport _loss;
    private readonly CancellationToken _ct;
    private readonly Dictionary<BlockElement, ListLabel> _labels = [];
    private readonly Dictionary<string, string> _bookmarkSlugs = new(StringComparer.Ordinal);
    private readonly List<(string Label, Note Note)> _notes = [];
    private readonly Dictionary<string, string> _noteLabels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private string _part;
    private int _imageCount;
    private int _fenceCounter;
    private bool _headerRow;

    private MarkdownExporter(Document doc, MarkdownExportOptions options, LossReport loss, CancellationToken ct)
    {
        _doc = doc;
        _options = options;
        _loss = loss;
        _ct = ct;
        _part = doc.MainPartName;
    }

    /// <summary>Writes the document as Markdown text.</summary>
    public static string Export(Document doc, MarkdownExportOptions options, LossReport loss, CancellationToken cancellationToken)
    {
        var e = new MarkdownExporter(doc, options, loss, cancellationToken);
        try
        {
            return e.Write();
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException))
        {
            throw new OfficeFormatException(DefectKind.Structure, "markdown.export-failed", "The document could not be written as Markdown: " + ex.GetType().Name + ".", DefectLocation.InPart(doc.MainPartName), ex);
        }
    }

    private void Once(string code, string feature, string message)
    {
        _loss.Report(code, feature, 1, message);
    }

    // ---- intermediate form ------------------------------------------------------------------------------------------------------

    private abstract record Out;

    private sealed record ParaOut(string Text) : Out;

    private sealed record HeadingOut(int Level, string Text) : Out;

    private sealed record CodeOut(List<string> Lines) : Out;

    private sealed record QuoteOut(int Depth, string Text) : Out;

    private sealed record ItemOut(int Level, bool Ordered, long Value, long NumId, string Text, int Task) : Out;

    private sealed record TableOut(List<List<string>> Rows, List<int> Align) : Out;

    private sealed record RuleOut : Out;

    // A paragraph or code block that belongs to the list item before it (Word has the indent, not the nesting).
    private sealed record ContOut(Out Inner) : Out;

    private string Write()
    {
        FindHeadingAnchors();
        foreach (ListLabel label in _doc.Numbering.LabelBody())
        {
            _labels[label.Paragraph] = label;
        }

        var items = new List<Out>();
        Collect(_doc.Body.Blocks(), items, 0);
        var sb = new StringBuilder();
        FrontMatter(sb);
        Assemble(items, sb);

        // Notes are written after the body, in the order they were first referenced; a note that mentions another one adds to the list as it goes.
        if (_notes.Count > 0)
        {
            var text = new StringBuilder();
            for (int i = 0; i < _notes.Count; i++)
            {
                (string label, Note note) = _notes[i];
                _part = note.Kind == NoteKind.Footnote ? FootnotesPart() : EndnotesPart();
                var inner = new List<Out>();
                Collect(note.Blocks(), inner, 0);
                var noteText = new StringBuilder();
                Assemble(inner, noteText);
                string[] lines = noteText.ToString().TrimEnd('\n').Split('\n');
                if (lines.Length == 0 || (lines.Length == 1 && lines[0].Length == 0))
                {
                    continue;
                }

                text.Append("[^").Append(label).Append("]: ").Append(lines[0]).Append('\n');
                for (int k = 1; k < lines.Length; k++)
                {
                    text.Append(lines[k].Length == 0 ? string.Empty : "    " + lines[k]).Append('\n');
                }

                text.Append('\n');
            }

            if (text.Length > 0)
            {
                Separate(sb);
                sb.Append(text.ToString().TrimEnd('\n')).Append('\n');
            }
        }

        return sb.ToString();
    }

    private string FootnotesPart() => _doc.LoadRelatedByType(_doc.OfficeRelationship("footnotes"), out string? name) is null ? _doc.MainPartName : name ?? _doc.MainPartName;

    private string EndnotesPart() => _doc.LoadRelatedByType(_doc.OfficeRelationship("endnotes"), out string? name) is null ? _doc.MainPartName : name ?? _doc.MainPartName;

    private void FrontMatter(StringBuilder sb)
    {
        CoreProperties core = _doc.Properties.Core;
        var lines = new List<string>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add(key + ": " + YamlValue(value.Trim()));
            }
        }

        Add("title", core.Title);
        Add("author", core.Creator);
        Add("subject", core.Subject);
        Add("description", core.Description);
        Add("keywords", core.Keywords);
        if (lines.Count == 0)
        {
            return;
        }

        sb.Append("---\n");
        foreach (string l in lines)
        {
            sb.Append(l).Append('\n');
        }

        sb.Append("---\n\n");
    }

    private static string YamlValue(string v)
    {
        v = v.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        bool plain = v.Length > 0 && char.IsLetterOrDigit(v[0]) && !v.Contains(": ", StringComparison.Ordinal) && !v.Contains(" #", StringComparison.Ordinal) && !v.EndsWith(':')
            && !v.StartsWith("---", StringComparison.Ordinal) && v.IndexOfAny(['"', '\'', '[', ']', '{', '}']) < 0;
        return plain ? v : "\"" + v.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    // ---- headings anchors -------------------------------------------------------------------------------------------------------

    // GitHub gives a heading the anchor of its text in lower case with spaces as hyphens, and numbers repeated ones. A link to a Word bookmark inside a heading
    // is written as a link to that anchor.
    private void FindHeadingAnchors()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Paragraph p in _doc.Body.Paragraphs())
        {
            if (HeadingLevel(p) == 0)
            {
                continue;
            }

            string slug = MarkdownImporter.Slug(p.Text);
            if (counts.TryGetValue(slug, out int n))
            {
                counts[slug] = n + 1;
                slug += "-" + n.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                counts[slug] = 1;
            }

            foreach (MarkupNode d in Descendants(p.Markup.Node))
            {
                if (d.LocalNameString == "bookmarkStart" && A(d, "name") is { } name)
                {
                    _bookmarkSlugs.TryAdd(name, slug);
                }
            }
        }
    }

    // ---- classifying paragraphs -------------------------------------------------------------------------------------------------

    private static bool IsW(MarkupNode n, string local) => n.Kind == MarkupNodeKind.Element && n.LocalNameString == local && n.NamespaceUri is W or WStrict;

    private static string? A(MarkupNode n, string local) => n.GetAttribute(W, local) ?? n.GetAttribute(WStrict, local) ?? n.GetAttribute(local);

    private static string? RA(MarkupNode n, string local) => n.GetAttribute(R, local) ?? n.GetAttribute(RStrict, local);

    private static IEnumerable<MarkupNode> Descendants(MarkupNode root)
    {
        var stack = new Stack<MarkupNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            MarkupNode n = stack.Pop();
            if (n.Kind == MarkupNodeKind.Element)
            {
                yield return n;
                var kids = new List<MarkupNode>(n.ChildElements);
                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    stack.Push(kids[i]);
                }
            }
        }
    }

    private string StyleName(string? styleId) => (_doc.Styles.Find(styleId)?.Name ?? styleId ?? string.Empty).ToLowerInvariant();

    private int HeadingLevel(Paragraph p)
    {
        string name = StyleName(p.StyleId);
        if (name.StartsWith("heading ", StringComparison.Ordinal) && int.TryParse(name.AsSpan(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1)
        {
            return Math.Min(n, 6);
        }

        if (name == "title")
        {
            return 1;
        }

        if (name == "subtitle")
        {
            return 2;
        }

        if (name.StartsWith("toc", StringComparison.Ordinal))
        {
            return 0;
        }

        long? outline = _doc.Styles.ResolveParagraph(p).OutlineLevel;
        return outline is >= 0 and <= 5 ? (int)outline.Value + 1 : 0;
    }

    private bool IsCodeStyle(Paragraph p)
    {
        string name = StyleName(p.StyleId);
        return name.Contains("source code", StringComparison.Ordinal) || name.Contains("preformatted", StringComparison.Ordinal) || name.Contains("verbatim", StringComparison.Ordinal)
            || name.Contains("code", StringComparison.Ordinal) && !name.Contains("char", StringComparison.Ordinal) || name.Contains("macro", StringComparison.Ordinal);
    }

    private bool IsQuoteStyle(Paragraph p)
    {
        string name = StyleName(p.StyleId);
        return name is "quote" or "intense quote" or "block text" or "blockquote" or "block quote";
    }

    private static bool HasBottomBorderOnly(Paragraph p)
    {
        MarkupNode ppr = FirstChild(p.Markup.Node, "pPr");
        if (ppr.IsNull)
        {
            return false;
        }

        MarkupNode bdr = FirstChild(ppr, "pBdr");
        return !bdr.IsNull && !FirstChild(bdr, "bottom").IsNull && FirstChild(bdr, "top").IsNull;
    }

    private static MarkupNode FirstChild(MarkupNode n, string local)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            if (IsW(c, local))
            {
                return c;
            }
        }

        return default;
    }

    // ---- collecting blocks --------------------------------------------------------------------------------------------------------

    private void Collect(IEnumerable<BlockElement> blocks, List<Out> output, int depth)
    {
        foreach (BlockElement block in blocks)
        {
            _ct.ThrowIfCancellationRequested();
            switch (block)
            {
                case Paragraph p:
                    CollectParagraph(p, output);
                    break;
                case Table t:
                    CollectTable(t, output, depth);
                    break;
                case BlockContentControl sdt:
                    Collect(sdt.Content.Blocks(), output, depth + 1);
                    break;
            }
        }
    }

    private void CollectParagraph(Paragraph p, List<Out> output)
    {
        bool afterItem = output.Count > 0 && output[^1] is ItemOut or ContOut;
        bool hasLabel = _labels.TryGetValue(p, out ListLabel? label);
        bool indented = !hasLabel && afterItem && (p.StyleId == "ListParagraph" || (_doc.Styles.ResolveParagraph(p).LeftIndent ?? 0) >= 720);

        if (IsCodeStyle(p))
        {
            var code = new CodeOut([.. RawText(p).Split('\n')]);
            output.Add(indented ? new ContOut(code) : code);
            return;
        }

        int heading = HeadingLevel(p);
        string text = RenderParagraph(p, inTable: false, heading: heading > 0);
        if (text.Trim().Length == 0)
        {
            if (hasLabel && heading == 0)
            {
                // An empty item still has its marker.
                output.Add(new ItemOut(label!.Level, !label.Definition.IsBullet, label.Value, label.NumberingId, string.Empty, 0));
            }
            else if (HasBottomBorderOnly(p))
            {
                output.Add(new RuleOut());
            }

            return;
        }

        if (heading > 0)
        {
            // A heading is one line; breaks inside it become spaces.
            string oneLine = text.Replace("\\\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Trim();
            if (oneLine.EndsWith('#'))
            {
                oneLine = oneLine[..^1] + "\\#";
            }

            output.Add(new HeadingOut(heading, oneLine));
            return;
        }

        if (hasLabel)
        {
            int task = 0;
            if (text.StartsWith('\u2610'))
            {
                task = 1;
            }
            else if (text.StartsWith('\u2612') || text.StartsWith('\u2611'))
            {
                task = 2;
            }

            if (task != 0)
            {
                text = text[1..].TrimStart();
            }

            output.Add(new ItemOut(label!.Level, !label.Definition.IsBullet, label.Value, label.NumberingId, text, task));
            return;
        }

        if (IsQuoteStyle(p))
        {
            long left = _doc.Styles.ResolveParagraph(p).LeftIndent ?? 0;
            output.Add(new QuoteOut(Math.Max(1, (int)Math.Round(left / 720.0)), text));
            return;
        }

        output.Add(indented ? new ContOut(new ParaOut(text)) : new ParaOut(text));
    }

    private void CollectTable(Table t, List<Out> output, int depth)
    {
        var rows = new List<List<string>>();
        int columns = 0;
        foreach (TableRow row in t.Rows())
        {
            var cells = new List<string>();
            // The first row is the header; its bold is how the table shows that, not emphasis in the text.
            _headerRow = rows.Count == 0;
            foreach (TableCell cell in row.Cells())
            {
                var parts = new List<string>();
                foreach (BlockElement b in cell.Blocks())
                {
                    if (b is Paragraph p)
                    {
                        string s = RenderParagraph(p, inTable: true, heading: false).Trim();
                        if (s.Length > 0)
                        {
                            parts.Add(s);
                        }
                    }
                    else if (b is Table)
                    {
                        Once("loss.markdown-nested-table", "nested tables", "A table inside a table cell was flattened to its text.");
                        parts.AddRange(Descendants(b.Markup.Node).Where(d => IsW(d, "p")).Select(d => MarkdownWriting.EscapeText(new Paragraph(_doc, new MarkupRef(d)).Text.Trim(), true, false)).Where(s => s.Length > 0));
                    }
                }

                cells.Add(string.Join("<br>", parts));
                if (cell.GridSpan > 1)
                {
                    Once("loss.markdown-merged-cells", "merged cells", "Merged table cells were written as one cell followed by empty cells.");
                    for (int k = 1; k < cell.GridSpan && k < 64; k++)
                    {
                        cells.Add(string.Empty);
                    }
                }
                else if (cell.VerticalMerge != CellMerge.None)
                {
                    Once("loss.markdown-merged-cells", "merged cells", "Merged table cells were written as one cell followed by empty cells.");
                }
            }

            columns = Math.Max(columns, cells.Count);
            rows.Add(cells);
        }

        _headerRow = false;

        if (rows.Count == 0 || columns == 0)
        {
            return;
        }

        // Alignment comes from the first body row (the header row of a Word table is usually centred or bold by style, not by intent).
        var align = new List<int>();
        for (int i = 0; i < columns; i++)
        {
            align.Add(0);
        }

        if (rows.Count > 1)
        {
            AlignFromRow(t, align);
        }

        foreach (List<string> r in rows)
        {
            while (r.Count < columns)
            {
                r.Add(string.Empty);
            }
        }

        output.Add(new TableOut(rows, align));
        _ = depth;
    }

    private void AlignFromRow(Table t, List<int> align)
    {
        bool first = true;
        foreach (TableRow row in t.Rows())
        {
            if (first)
            {
                first = false;
                continue;
            }

            int col = 0;
            foreach (TableCell cell in row.Cells())
            {
                Paragraph? p = cell.Paragraphs().FirstOrDefault();
                if (col < align.Count && p is not null)
                {
                    align[col] = AlignOf(p);
                }

                col += Math.Max(1, cell.GridSpan);
            }

            return;
        }
    }

    private int AlignOf(Paragraph p) => _doc.Styles.ResolveParagraph(p).Justification switch { "center" => 2, "right" or "end" => 3, _ => 0 };

    // ---- assembling -------------------------------------------------------------------------------------------------------------

    private static void Separate(StringBuilder sb)
    {
        if (sb.Length == 0)
        {
            return;
        }

        int newlines = 0;
        for (int i = sb.Length - 1; i >= 0 && sb[i] == '\n'; i--)
        {
            newlines++;
        }

        for (; newlines < 2; newlines++)
        {
            sb.Append('\n');
        }
    }

    private void Assemble(List<Out> items, StringBuilder sb)
    {
        int i = 0;
        bool? previousOrdered = null;
        int previousAlt = 0;
        while (i < items.Count)
        {
            Out item = items[i];
            switch (item)
            {
                case ParaOut p:
                    previousOrdered = null;
                    Separate(sb);
                    sb.Append(p.Text).Append('\n');
                    i++;
                    break;
                case HeadingOut h:
                    previousOrdered = null;
                    Separate(sb);
                    sb.Append('#', h.Level).Append(' ').Append(h.Text).Append('\n');
                    i++;
                    break;
                case RuleOut:
                    previousOrdered = null;
                    // Three hyphens at the very start would read as the start of front matter.
                    Separate(sb);
                    sb.Append(sb.Length == 0 ? "***\n" : "---\n");
                    i++;
                    break;
                case CodeOut c:
                    {
                        previousOrdered = null;
                        var lines = new List<string>(c.Lines);
                        i++;
                        while (i < items.Count && items[i] is CodeOut more)
                        {
                            lines.AddRange(more.Lines);
                            i++;
                        }

                        Separate(sb);
                        string fence = Fence(lines);
                        sb.Append(fence).Append('\n');
                        foreach (string l in lines)
                        {
                            sb.Append(l).Append('\n');
                        }

                        sb.Append(fence).Append('\n');
                        break;
                    }

                case QuoteOut:
                    {
                        previousOrdered = null;
                        Separate(sb);
                        bool firstQuote = true;
                        while (i < items.Count && items[i] is QuoteOut q)
                        {
                            if (!firstQuote)
                            {
                                sb.Append(string.Concat(Enumerable.Repeat(">", q.Depth)).Replace(">", "> ", StringComparison.Ordinal).TrimEnd()).Append('\n');
                            }

                            firstQuote = false;
                            string prefix = string.Concat(Enumerable.Repeat("> ", q.Depth));
                            foreach (string line in q.Text.Split('\n'))
                            {
                                sb.Append(prefix).Append(line).Append('\n');
                            }

                            i++;
                        }

                        break;
                    }

                case ItemOut:
                    {
                        Separate(sb);
                        var stack = new List<(int Level, int Width)>();
                        long topNum = long.MinValue;
                        bool firstItem = true;
                        int alt = 0;
                        while (i < items.Count && items[i] is ItemOut or ContOut)
                        {
                            if (items[i] is ContOut cont)
                            {
                                // The paragraph or code block of the item before, indented under its text.
                                var lines = new List<string>();
                                var inner = new StringBuilder();
                                var group = new List<Out>();
                                while (i < items.Count && items[i] is ContOut more && (group.Count == 0 || (more.Inner is CodeOut && group[^1] is CodeOut)))
                                {
                                    group.Add(more.Inner);
                                    i++;
                                    if (more.Inner is not CodeOut)
                                    {
                                        break;
                                    }
                                }

                                Assemble(group, inner);
                                string pad2 = new(' ', stack.Sum(static s => s.Width));
                                if (sb.Length > 0 && !(sb.Length >= 2 && sb[^1] == '\n' && sb[^2] == '\n'))
                                {
                                    sb.Append('\n');
                                }

                                foreach (string line in inner.ToString().TrimEnd('\n').Split('\n'))
                                {
                                    sb.Append(line.Length == 0 ? string.Empty : pad2 + line).Append('\n');
                                }

                                _ = lines;
                                _ = cont;
                                continue;
                            }

                            var it = (ItemOut)items[i];
                            while (stack.Count > 0 && stack[^1].Level >= it.Level)
                            {
                                stack.RemoveAt(stack.Count - 1);
                            }

                            if (stack.Count == 0 && (firstItem || it.NumId != topNum))
                            {
                                // A new list. Right after a list of the same kind it takes the other marker (and a blank line), or the two would read as one.
                                alt = previousOrdered == it.Ordered ? 1 - previousAlt : 0;
                                if (!firstItem)
                                {
                                    sb.Append('\n');
                                }

                                topNum = it.NumId;
                                previousOrdered = it.Ordered;
                                previousAlt = alt;
                            }

                            firstItem = false;
                            int indent = stack.Sum(static s => s.Width);
                            string marker = it.Ordered
                                ? it.Value.ToString(CultureInfo.InvariantCulture) + (alt == 0 ? "." : ")") + " "
                                : (alt == 0 ? "-" : "*") + " ";
                            stack.Add((it.Level, marker.Length));
                            string pad = new(' ', indent);
                            string contPad = new(' ', indent + marker.Length);
                            string[] itemLines = it.Text.Split('\n');
                            string head = pad + marker + it.Task switch { 1 => "[ ] ", 2 => "[x] ", _ => string.Empty } + itemLines[0];
                            sb.Append(it.Text.Length == 0 && it.Task == 0 ? head.TrimEnd() : head).Append('\n');
                            for (int k = 1; k < itemLines.Length; k++)
                            {
                                sb.Append(contPad).Append(itemLines[k]).Append('\n');
                            }

                            i++;
                        }

                        break;
                    }

                case TableOut t:
                    previousOrdered = null;
                    Separate(sb);
                    WriteTable(t, sb);
                    i++;
                    break;
                default:
                    i++;
                    break;
            }
        }
    }

    private string Fence(List<string> lines)
    {
        int longest = 0;
        foreach (string l in lines)
        {
            int run = 0;
            foreach (char ch in l)
            {
                run = ch == '`' ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }
        }

        _fenceCounter++;
        return new string('`', Math.Max(3, longest + 1));
    }

    private static void WriteTable(TableOut t, StringBuilder sb) => MarkdownWriting.WriteTable(sb, t.Rows, t.Align);
}
