using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Documents.Fields;

namespace RecursiveAI.OneDocument.Layout;

// Index and table-of-authorities entries. ECMA-376 Part 1 §17.16.5.29 (INDEX), §17.16.5.72 (XE), §17.16.5.67 (TOA), §17.16.5.62 (TA).
// Each XE or TA field gets a bookmark with no extent at its place; the entry's page list is an ODXEPAGES field naming those bookmarks (a
// name that starts with ~ stands for the whole range of a bookmark the entry was marked with: XE \r, TA \r).
internal static class IndexBuilder
{
    private sealed class Node
    {
        public readonly List<string> Marks = [];
        public readonly SortedDictionary<string, Node> Subs = new(Comparer);
        public string? CrossReference;
        public bool Bold;
        public bool Italic;
    }

    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);

    // The INDEX field's separators: entry to first page (\e), between pages (\l), between a start and end page (\g), before a cross reference (\k).
    private sealed record Separators(string Entry, string Pages, string Range, string CrossReference, bool Tab);

    public static List<FieldUpdater.EntryXml>? BuildIndex(FieldUpdater u, FieldInfo index, HashSet<int> inside)
    {
        FieldInstruction f = index.Parsed;
        string? type = f.Switch("f")?.Value;
        (int first, int last) range = (0, u.Model.Paragraphs.Count - 1);
        if (f.Switch("b")?.Value is { } name && u.Model.Bookmark(name) is { } bm)
        {
            range = (bm.StartParagraph, bm.EndParagraph < 0 ? u.Model.Paragraphs.Count - 1 : bm.EndParagraph);
        }

        var top = new SortedDictionary<string, Node>(Comparer);
        foreach (FieldInfo xe in u.Model.Fields)
        {
            if (xe.Parsed.Type != "XE" || xe.ParagraphIndex < range.first || xe.ParagraphIndex > range.last || inside.Contains(xe.ParagraphIndex))
            {
                continue;
            }

            if ((xe.Parsed.Switch("f")?.Value) != type)
            {
                continue;
            }

            string text = xe.Parsed.FirstArgument ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            // A colon starts a subentry; subentries nest as deep as the text goes (§17.16.5.72).
            string[] levels = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (levels.Length == 0)
            {
                continue;
            }

            if (!top.TryGetValue(levels[0], out Node? target))
            {
                top[levels[0]] = target = new Node();
            }

            for (int i = 1; i < levels.Length; i++)
            {
                if (!target.Subs.TryGetValue(levels[i], out Node? sub))
                {
                    target.Subs[levels[i]] = sub = new Node();
                }

                target = sub;
            }

            if (xe.Parsed.Switch("t")?.Value is { } cross)
            {
                target.CrossReference = cross;
            }
            else
            {
                target.Marks.Add(RangeToken(u, xe) ?? u.LocationBookmark(xe));
                target.Bold |= xe.Parsed.HasSwitch("b");
                target.Italic |= xe.Parsed.HasSwitch("i");
            }
        }

        string entrySeparator = f.Switch("e") is { } e ? e.Value ?? f.Arguments.ElementAtOrDefault(0) ?? ", " : ", ";
        var sep = new Separators(
            entrySeparator,
            f.Switch("l")?.Value ?? ", ",
            f.Switch("g")?.Value ?? "\u2013",
            f.Switch("k")?.Value ?? ". ",
            entrySeparator is "^t" or "\t" or "tab" || entrySeparator.Contains('\t', StringComparison.Ordinal));
        bool runIn = f.HasSwitch("r");
        string? headingTemplate = f.HasSwitch("h") ? (f.Switch("h")?.Value ?? f.Arguments.ElementAtOrDefault(0) ?? "A") : null;
        long width = u.TextWidthTwips(index.ParagraphIndex);
        string? headingStyle = u.StyleFor("index heading", "IndexHeading");
        string? current = null;
        var result = new List<FieldUpdater.EntryXml>();
        foreach ((string key, Node node) in top)
        {
            if (headingTemplate is not null)
            {
                string letter = key.Length > 0 && char.IsLetter(key[0]) ? char.ToUpperInvariant(key[0]).ToString() : "Symbols";
                if (letter != current)
                {
                    current = letter;
                    result.Add(Heading(u, headingStyle, HeadingText(headingTemplate, letter)));
                }
            }

            if (runIn)
            {
                result.Add(RunIn(u, key, node, sep, width));
                continue;
            }

            Emit(u, result, key, node, 1, sep, width);
        }

        if (result.Count == 0)
        {
            result.Add(new FieldUpdater.EntryXml { Content = u.TextRun("No index entries found.") });
        }

        return result;
    }

    // §17.16.5.29 \h: the text is the heading of each letter group; a leading A (or a) is replaced by the letter, and a space gives a blank line.
    private static string HeadingText(string template, string letter)
    {
        if (template.Length > 0 && template[0] is 'A' or 'a' && letter.Length == 1)
        {
            return (template[0] == 'a' ? char.ToLowerInvariant(letter[0]) : letter[0]) + template[1..];
        }

        return template.Trim().Length == 0 ? string.Empty : template;
    }

    private static FieldUpdater.EntryXml Heading(FieldUpdater u, string? style, string text) =>
        new() { StyleId = style, DirectProperties = style is null ? $"<{u.W}keepNext/>" : string.Empty, Content = text.Length == 0 ? string.Empty : u.TextRun(text, style is null ? $"<{u.W}b/>" : string.Empty) };

    private static string? RangeToken(FieldUpdater u, FieldInfo xe)
    {
        string? bookmark = xe.Parsed.Switch("r")?.Value ?? (xe.Parsed.HasSwitch("r") ? xe.Parsed.Arguments.ElementAtOrDefault(1) : null);
        return bookmark is not null && u.Model.Bookmark(bookmark) is not null ? "~" + bookmark : null;
    }

    private static void Emit(FieldUpdater u, List<FieldUpdater.EntryXml> into, string text, Node node, int level, Separators sep, long width)
    {
        string style = u.StyleFor("index " + Math.Min(level, 9), "Index" + Math.Min(level, 9)) ?? string.Empty;
        var content = new StringBuilder(u.TextRun(text));
        AppendPages(u, content, node, sep);
        string props = node.Marks.Count > 0 && sep.Tab ? $"<{u.W}tabs><{u.W}tab {u.W}val=\"right\" {u.W}leader=\"dot\" {u.W}pos=\"{width}\"/></{u.W}tabs>" : string.Empty;
        if (style.Length == 0)
        {
            props += $"<{u.W}ind {u.W}left=\"{220 * (level - 1)}\"/>";
        }

        into.Add(new FieldUpdater.EntryXml { StyleId = style.Length == 0 ? null : style, DirectProperties = props, Content = content.ToString() });
        foreach ((string subKey, Node sub) in node.Subs)
        {
            Emit(u, into, subKey, sub, level + 1, sep, width);
        }
    }

    // \r: an entry and its subentries on one line; a colon follows the main entry and semicolons separate the subentries.
    private static FieldUpdater.EntryXml RunIn(FieldUpdater u, string text, Node node, Separators sep, long width)
    {
        string style = u.StyleFor("index 1", "Index1") ?? string.Empty;
        var content = new StringBuilder(u.TextRun(text));
        AppendPages(u, content, node, sep);
        bool firstSub = true;
        void Subs(Node n)
        {
            foreach ((string key, Node sub) in n.Subs)
            {
                content.Append(u.TextRun(firstSub ? ": " : "; "));
                firstSub = false;
                content.Append(u.TextRun(key));
                AppendPages(u, content, sub, sep);
                Subs(sub);
            }
        }

        Subs(node);
        return new FieldUpdater.EntryXml { StyleId = style.Length == 0 ? null : style, DirectProperties = style.Length == 0 ? $"<{u.W}ind {u.W}left=\"0\"/>" : string.Empty, Content = content.ToString() };
    }

    private static void AppendPages(FieldUpdater u, StringBuilder content, Node node, Separators sep)
    {
        if (node.CrossReference is { } cross)
        {
            content.Append(u.TextRun(sep.CrossReference + cross));
        }
        else if (node.Marks.Count > 0)
        {
            content.Append(sep.Tab ? u.TextRun("\t") : u.TextRun(sep.Entry));
            string rPr = (node.Bold ? $"<{u.W}b/>" : string.Empty) + (node.Italic ? $"<{u.W}i/>" : string.Empty);
            content.Append(u.FieldRuns(PageList(node.Marks, sep.Pages, sep.Range, passim: false), "1", rPr));
        }
    }

    private static string PageList(List<string> marks, string pageSeparator, string rangeSeparator, bool passim) =>
        IndexPages.FieldName + " " + string.Join(' ', marks) + " \\l \"" + pageSeparator.Replace("\"", "\\\"", StringComparison.Ordinal) + "\" \\g \"" + rangeSeparator.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" + (passim ? " \\p 5" : string.Empty);

    private static readonly string[] CategoryNames =
    [
        string.Empty, "Cases", "Statutes", "Other Authorities", "Rules", "Treatises", "Regulations", "Constitutional Provisions",
    ];

    public static List<FieldUpdater.EntryXml>? BuildAuthorities(FieldUpdater u, FieldInfo toa, HashSet<int> inside)
    {
        FieldInstruction f = toa.Parsed;
        int? category = f.Switch("c")?.Value is { } c && int.TryParse(c, out int cv) ? cv : null;
        bool headings = f.HasSwitch("h");
        bool passim = f.HasSwitch("p");
        string separator = f.Switch("e") is { } e ? e.Value ?? f.Arguments.ElementAtOrDefault(0) ?? "^t" : "^t";
        string pageSeparator = f.Switch("l")?.Value ?? ", ";
        string rangeSeparator = f.Switch("g")?.Value ?? "\u2013";
        bool tab = separator is "^t" or "\t" or "tab" || separator.Contains('\t', StringComparison.Ordinal);
        var categories = new SortedDictionary<int, SortedDictionary<string, Node>>();
        foreach (FieldInfo ta in u.Model.Fields)
        {
            if (ta.Parsed.Type != "TA" || ta.ParagraphIndex < 0 || inside.Contains(ta.ParagraphIndex))
            {
                continue;
            }

            // §17.16.5.62: a TA field without \c is in category 1; a TOA with \c 0 (Word's "all") lists every category.
            int cat = ta.Parsed.Switch("c")?.Value is { } tc && int.TryParse(tc, out int tcv) ? tcv : 1;
            if (category is { } wanted && wanted != 0 && wanted != cat)
            {
                continue;
            }

            string? longCitation = ta.Parsed.Switch("l")?.Value;
            if (string.IsNullOrEmpty(longCitation))
            {
                continue;
            }

            if (!categories.TryGetValue(cat, out SortedDictionary<string, Node>? list))
            {
                categories[cat] = list = new SortedDictionary<string, Node>(Comparer);
            }

            if (!list.TryGetValue(longCitation, out Node? node))
            {
                list[longCitation] = node = new Node();
            }

            node.Marks.Add(RangeToken(u, ta) ?? u.LocationBookmark(ta));
            node.Bold |= ta.Parsed.HasSwitch("b");
            node.Italic |= ta.Parsed.HasSwitch("i");
        }

        string w = u.W;
        string entryStyle = u.StyleFor("table of authorities", "TableofAuthorities") ?? string.Empty;
        string? headingStyle = u.StyleFor("toa heading", "TOAHeading");
        long width = u.TextWidthTwips(toa.ParagraphIndex);
        var result = new List<FieldUpdater.EntryXml>();
        foreach ((int cat, SortedDictionary<string, Node> list) in categories)
        {
            if (headings)
            {
                string title = cat >= 1 && cat < CategoryNames.Length ? CategoryNames[cat] : "Category " + cat.ToString(CultureInfo.InvariantCulture);
                result.Add(Heading(u, headingStyle, title));
            }

            foreach ((string citation, Node node) in list)
            {
                string rPr = (node.Bold ? $"<{w}b/>" : string.Empty) + (node.Italic ? $"<{w}i/>" : string.Empty);
                string props = tab ? $"<{w}tabs><{w}tab {w}val=\"right\" {w}leader=\"dot\" {w}pos=\"{width}\"/></{w}tabs>" : string.Empty;
                if (entryStyle.Length == 0)
                {
                    props += $"<{w}ind {w}left=\"220\" {w}hanging=\"220\"/>";
                }

                result.Add(new FieldUpdater.EntryXml
                {
                    StyleId = entryStyle.Length == 0 ? null : entryStyle,
                    DirectProperties = props,
                    Content = u.TextRun(citation) + (tab ? u.TextRun("\t") : u.TextRun(separator)) + u.FieldRuns(PageList(node.Marks, pageSeparator, rangeSeparator, passim), "1", rPr),
                });
            }
        }

        if (result.Count == 0)
        {
            result.Add(new FieldUpdater.EntryXml { Content = u.TextRun("No table of authorities entries found.") });
        }

        return result;
    }
}
