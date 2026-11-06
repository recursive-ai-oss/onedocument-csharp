using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// Builds the result of table-of-contents, index and table-of-authorities fields in a private copy of the document, so layout paints them
// and the caller's document is never touched. The entries are real paragraphs (TOC 1 to 9, Index 1 and 2, Table of Authorities) holding
// PAGEREF fields (and ODXEPAGES fields for indexes), so the page numbers follow the pages exactly like a PAGEREF anywhere else and the
// normal passes settle them, including the shift a longer table of contents causes in the pages after it.
// ECMA-376 Part 1 §17.16.5.68 (TOC), §17.16.5.29 (INDEX), §17.16.5.67 (TOA), §17.16.5.72 (XE), §17.16.5.63 (TC), §17.16.5.62 (TA).
internal sealed class FieldUpdater
{
    private readonly Document _doc;
    private readonly FieldModel _model;
    private readonly LayoutOptions _options;
    private readonly List<LayoutNote> _notes;
    private readonly Dictionary<Paragraph, ListLabel> _labels = [];
    private long _nextBookmarkId;
    private int _nextTocName;
    private int _nextXeName;
    private readonly Dictionary<int, string> _headingBookmark = [];
    private readonly Dictionary<int, string> _existingToc = [];

    private FieldUpdater(Document doc, FieldModel model, LayoutOptions options, List<LayoutNote> notes)
    {
        _doc = doc;
        _model = model;
        _options = options;
        _notes = notes;
        foreach (ListLabel l in doc.Numbering.LabelBody())
        {
            _labels[l.Paragraph] = l;
        }

        if (model.Paragraphs.Count > 0 && model.Paragraphs[0].Markup.Node.Prefix.Length > 0)
        {
            W = Encoding.UTF8.GetString(model.Paragraphs[0].Markup.Node.Prefix) + ":";
        }

        foreach (BookmarkInfo b in model.Bookmarks.Values)
        {
            if (b.Closed && b.Name.StartsWith("_Toc", StringComparison.OrdinalIgnoreCase))
            {
                _existingToc.TryAdd(b.StartParagraph, b.Name);
            }

            if (long.TryParse(b.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            {
                _nextBookmarkId = Math.Max(_nextBookmarkId, id + 1);
            }
        }
    }

    // True when some table-of-contents, index or authorities field has to be built for this layout.
    public static bool NeedsBuild(FieldModel model, LayoutOptions options)
    {
        if (options.SkipFieldBuild)
        {
            return false;
        }

        foreach (FieldInfo fi in model.Fields)
        {
            if (IsTarget(fi) && WantsBuild(fi, options))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTarget(FieldInfo fi) =>
        fi.Parsed.Type is "TOC" or "INDEX" or "TOA" && fi.Field is { Parent: null, Kind: FieldKind.Complex, IsComplete: true } && fi.ParagraphIndex >= 0;

    // A field with no stored result is built; one with a result is rebuilt only when the caller asked for every field to be updated.
    private static bool WantsBuild(FieldInfo fi, LayoutOptions options) => !fi.Field.IsLocked && (options.UpdateFields || fi.Field.ResultText.Trim().Length == 0);

    // Returns the copy with the fields built, and the model of that copy.
    public static (Document Doc, FieldModel Model) Apply(Document original, FieldModel originalModel, LayoutOptions options, List<LayoutNote> notes)
    {
        var copy = new MemoryStream();
        original.SaveAs(copy, original.Format);
        copy.Position = 0;
        Document work = Document.Open(copy, new OpenContext(original.Context.Limits), leaveOpen: false);
        FieldModel model = FieldModel.Build(work);
        var updater = new FieldUpdater(work, model, options, notes);
        updater.Run();
        return (work, FieldModel.Build(work));
    }

    // Builds the entries in the document itself (a persisted update) and returns how many fields were built.
    public static int ApplyInPlace(Document doc, FieldModel model, LayoutOptions options, List<LayoutNote> notes)
    {
        int built = model.Fields.Count(f => IsTarget(f) && WantsBuild(f, options));
        new FieldUpdater(doc, model, options, notes).Run();
        return built;
    }

    private void Run()
    {
        // Paragraphs inside a field's result are not candidates for another field's entries.
        var inside = new HashSet<int>();
        foreach (FieldInfo fi in _model.Fields.Where(IsTarget))
        {
            int end = EndParagraph(fi);
            for (int i = fi.ParagraphIndex; i <= end; i++)
            {
                inside.Add(i);
            }
        }

        var targets = _model.Fields.Where(f => IsTarget(f) && WantsBuild(f, _options)).ToList();
        // Prepare every target's entries from the unedited copy, then edit.
        var plans = new List<(FieldInfo Field, List<EntryXml> Entries)>();
        foreach (FieldInfo fi in targets)
        {
            List<EntryXml>? entries = fi.Parsed.Type switch
            {
                "TOC" => BuildToc(fi, inside),
                "INDEX" => IndexBuilder.BuildIndex(this, fi, inside),
                _ => IndexBuilder.BuildAuthorities(this, fi, inside),
            };
            if (entries is not null)
            {
                plans.Add((fi, entries));
            }
        }

        foreach ((FieldInfo fi, List<EntryXml> entries) in plans)
        {
            Replace(fi, entries);
        }
    }

    internal Document Doc => _doc;

    internal FieldModel Model => _model;

    internal LayoutOptions Options => _options;

    internal void Note(string code, string message, FieldInfo at) => _notes.Add(new LayoutNote(code, message, _model.Paragraphs[at.ParagraphIndex]));

    private int EndParagraph(FieldInfo fi)
    {
        if (fi.Field.EndRun is not { } end)
        {
            return fi.ParagraphIndex;
        }

        for (MarkupNode n = end.Node; !n.IsNull; n = n.Parent)
        {
            int index = _model.IndexOfNode(n);
            if (index >= 0)
            {
                return index;
            }
        }

        return fi.ParagraphIndex;
    }

    // One built paragraph: the style it uses, its direct properties and its content. The first one carries the field's begin, the last its end.
    internal sealed class EntryXml
    {
        public string? StyleId;
        public string DirectProperties = string.Empty; // children of w:pPr after pStyle
        public string Content = string.Empty;
    }

    // --- XML pieces --------------------------------------------------------------------------------------------------------------

    internal string W = "w:";

    internal static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c >= ' ' || c is '\t' or '\n' or '\r')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    internal string TextRun(string text, string rPr = "")
    {
        var sb = new StringBuilder();
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] is not ('\t' or '\n'))
            {
                continue;
            }

            if (i > start)
            {
                sb.Append('<').Append(W).Append("r>").Append(rPr.Length == 0 ? string.Empty : $"<{W}rPr>{rPr}</{W}rPr>").Append('<').Append(W).Append("t xml:space=\"preserve\">").Append(Esc(text[start..i])).Append("</").Append(W).Append("t></").Append(W).Append("r>");
            }

            if (i < text.Length)
            {
                string element = text[i] == '\t' ? "tab" : "br";
                sb.Append('<').Append(W).Append("r>").Append(rPr.Length == 0 ? string.Empty : $"<{W}rPr>{rPr}</{W}rPr>").Append('<').Append(W).Append(element).Append("/></").Append(W).Append("r>");
            }

            start = i + 1;
        }

        return sb.ToString();
    }

    internal string FieldRuns(string instruction, string cached, string rPr = "")
    {
        string pr = rPr.Length == 0 ? string.Empty : $"<{W}rPr>{rPr}</{W}rPr>";
        return $"<{W}r>{pr}<{W}fldChar {W}fldCharType=\"begin\"/></{W}r><{W}r>{pr}<{W}instrText xml:space=\"preserve\"> {Esc(instruction)} </{W}instrText></{W}r>"
            + $"<{W}r>{pr}<{W}fldChar {W}fldCharType=\"separate\"/></{W}r><{W}r>{pr}<{W}t xml:space=\"preserve\">{Esc(cached)}</{W}t></{W}r><{W}r>{pr}<{W}fldChar {W}fldCharType=\"end\"/></{W}r>";
    }

    // The id of the style that stands for a built-in name such as "toc 1", or null when the document does not define one.
    internal string? StyleFor(string name, string id)
    {
        StyleDefinition? s = _doc.Styles.FindByName(name) ?? _doc.Styles.Find(id);
        return s?.Id;
    }

    internal long TextWidthTwips(int paragraph)
    {
        IReadOnlyList<Section> sections = _doc.Sections;
        Section s = sections[Math.Min(_model.SectionOf(paragraph), sections.Count - 1)];
        long width = s.PageSize.Width - s.Margins.Left - s.Margins.Right - s.Margins.Gutter;
        SectionColumns cols = s.Columns;
        if (cols.Count > 1 && cols.EqualWidth)
        {
            width = (width - (cols.Space * (cols.Count - 1))) / cols.Count;
        }

        return Math.Max(720, width);
    }

    internal ListLabel? LabelOf(int paragraph) => _labels.GetValueOrDefault(_model.Paragraphs[paragraph]);

    // A bookmark around a paragraph so PAGEREF can name it; an existing _Toc bookmark is reused.
    internal string HeadingBookmark(int paragraph)
    {
        if (_headingBookmark.TryGetValue(paragraph, out string? known))
        {
            return known;
        }

        if (_existingToc.TryGetValue(paragraph, out string? existing))
        {
            _headingBookmark[paragraph] = existing;
            return existing;
        }

        string name = UniqueName("_Toc", 900000000 + _nextTocName++);
        long id = _nextBookmarkId++;
        MarkupNode p = _model.Paragraphs[paragraph].Markup.Node;
        string start = $"<{W}bookmarkStart {W}id=\"{id}\" {W}name=\"{name}\"/>";
        MarkupNode pPr = FirstChild(p, "pPr");
        if (pPr.IsNull)
        {
            p.PrependXml(Encoding.UTF8.GetBytes(start));
        }
        else
        {
            pPr.InsertXmlAfter(Encoding.UTF8.GetBytes(start));
        }

        p.AppendXml(Encoding.UTF8.GetBytes($"<{W}bookmarkEnd {W}id=\"{id}\"/>"));
        _headingBookmark[paragraph] = name;
        return name;
    }

    // A bookmark with no extent at the place of a field (an XE or TA entry), so its page can be asked for.
    internal string LocationBookmark(FieldInfo at)
    {
        string name = UniqueName("_OdXe", _nextXeName++);
        long id = _nextBookmarkId++;
        at.Field.Markup.Node.InsertXmlBefore(Encoding.UTF8.GetBytes($"<{W}bookmarkStart {W}id=\"{id}\" {W}name=\"{name}\"/><{W}bookmarkEnd {W}id=\"{id}\"/>"));
        return name;
    }

    private string UniqueName(string prefix, int number)
    {
        string name;
        do
        {
            name = prefix + number.ToString(prefix == "_Toc" ? "D9" : "D6", CultureInfo.InvariantCulture);
            number++;
        }
        while (_model.Bookmarks.ContainsKey(name));
        return name;
    }

    internal static MarkupNode FirstChild(MarkupNode parent, string local)
    {
        for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && Xml.Eq(n.LocalName, local))
            {
                return n;
            }
        }

        return default;
    }

    // --- table of contents -------------------------------------------------------------------------------------------------------

    private sealed record TocEntry(int Level, string Text, string? Number, int Paragraph, FieldInfo? Tc, bool NoPage);

    private List<EntryXml>? BuildToc(FieldInfo fi, HashSet<int> inside)
    {
        FieldInstruction f = fi.Parsed;
        (int lo, int hi)? outline = ParseRange(f.Switch("o")?.Value);
        bool useOutline = f.HasSwitch("u");
        List<(string Style, int Level)> custom = ParseStyles(f.Switch("t")?.Value);
        string? seqId = f.Switch("c")?.Value;
        string? tcId = f.Switch("f")?.Value;
        (int lo, int hi)? tcLevels = ParseRange(f.Switch("l")?.Value);
        bool hasTc = f.HasSwitch("f") || f.HasSwitch("l");
        if (outline is null && !useOutline && custom.Count == 0 && seqId is null && !hasTc)
        {
            outline = (1, 9);
        }

        (int Lo, int Hi)? noPage = f.HasSwitch("n") ? ParseRange(f.Switch("n")?.Value) ?? (1, 9) : null;
        (int first, int last) range = (0, _model.Paragraphs.Count - 1);
        if (f.Switch("b")?.Value is { } bmName && _model.Bookmark(bmName) is { } bm)
        {
            range = (bm.StartParagraph, bm.EndParagraph < 0 ? _model.Paragraphs.Count - 1 : bm.EndParagraph);
        }

        var seqParagraphs = new HashSet<int>();
        if (seqId is not null)
        {
            foreach (FieldInfo x in _model.Fields)
            {
                if (x.Parsed.Type == "SEQ" && x.ParagraphIndex >= 0 && string.Equals(x.Parsed.FirstArgument, seqId, StringComparison.OrdinalIgnoreCase))
                {
                    seqParagraphs.Add(x.ParagraphIndex);
                }
            }
        }

        bool keepTabs = f.HasSwitch("w");
        bool keepBreaks = f.HasSwitch("x");
        var entries = new List<TocEntry>();
        var tcByParagraph = new Dictionary<int, List<FieldInfo>>();
        if (hasTc)
        {
            foreach (FieldInfo t in _model.Fields)
            {
                if (t.Parsed.Type == "TC" && t.ParagraphIndex >= 0 && (t.Parsed.Switch("f")?.Value ?? "C") == (tcId ?? "C"))
                {
                    if (!tcByParagraph.TryGetValue(t.ParagraphIndex, out List<FieldInfo>? list))
                    {
                        tcByParagraph[t.ParagraphIndex] = list = [];
                    }

                    list.Add(t);
                }
            }
        }

        for (int i = range.first; i <= range.last && i < _model.Paragraphs.Count; i++)
        {
            if (inside.Contains(i))
            {
                continue;
            }

            Paragraph p = _model.Paragraphs[i];
            int level = 0;
            if (outline is { } o)
            {
                int h = HeadingStyleLevel(p);
                if (h >= o.lo && h <= o.hi)
                {
                    level = h;
                }
            }

            if (level == 0 && useOutline)
            {
                int ol = _model.OutlineLevel(i);
                (int lo, int hi) = outline ?? (1, 9);
                if (ol >= lo && ol <= hi)
                {
                    level = ol;
                }
            }

            if (level == 0 && custom.Count > 0 && p.ResolvedProperties.StyleId is { } sid)
            {
                string? styleName = _doc.Styles.Find(sid)?.Name;
                foreach ((string style, int lvl) in custom)
                {
                    if (string.Equals(style, sid, StringComparison.OrdinalIgnoreCase) || string.Equals(style, styleName, StringComparison.OrdinalIgnoreCase))
                    {
                        level = lvl;
                        break;
                    }
                }
            }

            if (level == 0 && seqParagraphs.Contains(i))
            {
                level = 1;
            }

            if (level > 0)
            {
                string text = EntryText(p, keepTabs, keepBreaks);
                if (text.Length > 0)
                {
                    string? number = _labels.TryGetValue(p, out ListLabel? label) && label.Text.Length > 0 && label.Definition.Format != "bullet" ? label.Text : null;
                    entries.Add(new TocEntry(Math.Min(level, 9), text, number, i, null, noPage is { } np && level >= np.Lo && level <= np.Hi));
                }
            }

            if (tcByParagraph.TryGetValue(i, out List<FieldInfo>? tcs))
            {
                foreach (FieldInfo tc in tcs)
                {
                    int lvl = tc.Parsed.Switch("l")?.Value is { } lv && int.TryParse(lv, out int parsed) ? Math.Clamp(parsed, 1, 9) : 1;
                    if (tcLevels is { } tl && (lvl < tl.lo || lvl > tl.hi))
                    {
                        continue;
                    }

                    string text = tc.Parsed.FirstArgument ?? string.Empty;
                    if (text.Length > 0)
                    {
                        entries.Add(new TocEntry(lvl, text, null, i, tc, tc.Parsed.HasSwitch("n")));
                    }
                }
            }
        }

        string? separator = f.Switch("p")?.Value;
        bool links = f.HasSwitch("h");
        long width = TextWidthTwips(fi.ParagraphIndex);
        var result = new List<EntryXml>();
        foreach (TocEntry e in entries)
        {
            string target = e.Tc is null ? HeadingBookmark(e.Paragraph) : TcBookmark(e.Tc);
            string style = StyleFor("toc " + e.Level, "TOC" + e.Level) ?? string.Empty;
            var props = new StringBuilder();
            bool tabbed = e.Number is not null;
            props.Append('<').Append(W).Append("tabs>");
            if (tabbed)
            {
                props.Append('<').Append(W).Append("tab ").Append(W).Append("val=\"left\" ").Append(W).Append("pos=\"").Append(440 * e.Level).Append("\"/>");
            }

            if (!e.NoPage && separator is null)
            {
                props.Append('<').Append(W).Append("tab ").Append(W).Append("val=\"right\" ").Append(W).Append("leader=\"dot\" ").Append(W).Append("pos=\"").Append(width).Append("\"/>");
            }

            props.Append("</").Append(W).Append("tabs>");
            if (!tabbed && (e.NoPage || separator is not null))
            {
                props.Clear();
            }

            if (style.Length == 0)
            {
                // The document has no TOC style: what Word's built-in one does (indent per level, 5 pt after).
                props.Append('<').Append(W).Append("spacing ").Append(W).Append("after=\"100\"/><").Append(W).Append("ind ").Append(W).Append("left=\"").Append(220 * (e.Level - 1)).Append("\"/>");
            }

            var content = new StringBuilder();
            if (e.Number is not null)
            {
                content.Append(TextRun(e.Number)).Append(TextRun("\t"));
            }

            content.Append(TextRun(e.Text));
            if (!e.NoPage)
            {
                content.Append(separator is null ? TextRun("\t") : TextRun(separator));
                content.Append(FieldRuns("PAGEREF " + target + (links ? " \\h" : string.Empty), "1"));
            }

            string body = content.ToString();
            if (links)
            {
                body = $"<{W}hyperlink {W}anchor=\"{target}\" {W}history=\"1\">{body}</{W}hyperlink>";
            }

            result.Add(new EntryXml { StyleId = style.Length == 0 ? null : style, DirectProperties = props.ToString(), Content = body });
        }

        if (result.Count == 0)
        {
            result.Add(new EntryXml { Content = TextRun("No table of contents entries found.") });
        }

        return result;
    }

    private string TcBookmark(FieldInfo tc) => LocationBookmark(tc);

    private static string EntryText(Paragraph p, bool keepTabs, bool keepBreaks)
    {
        string text = p.Text;
        if (!keepTabs)
        {
            text = text.Replace('\t', ' ');
        }

        text = keepBreaks ? text : text.Replace('\n', ' ');
        return text.Trim();
    }

    // The heading level a "heading N" style gives (ECMA-376 Part 1 §17.16.5.68, \o: built-in heading styles).
    private int HeadingStyleLevel(Paragraph p)
    {
        string? id = p.ResolvedProperties.StyleId;
        if (id is null)
        {
            return 0;
        }

        string name = (_doc.Styles.Find(id)?.Name ?? id).Replace(" ", string.Empty, StringComparison.Ordinal);
        return name.StartsWith("heading", StringComparison.OrdinalIgnoreCase) && name.Length == 8 && name[7] is >= '1' and <= '9' ? name[7] - '0' : 0;
    }

    internal static (int lo, int hi)? ParseRange(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lo))
        {
            return null;
        }

        int hi = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : lo;
        return (Math.Clamp(lo, 1, 9), Math.Clamp(hi, 1, 9));
    }

    // \t "Style 1,1,Style 2,2": style names and levels, separated by the list separator (a comma or a semicolon).
    private static List<(string, int)> ParseStyles(string? text)
    {
        var list = new List<(string, int)>();
        if (string.IsNullOrEmpty(text))
        {
            return list;
        }

        string[] parts = text.Split([',', ';'], StringSplitOptions.TrimEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) && level is >= 1 and <= 9)
            {
                list.Add((parts[i], level));
            }
        }

        return list;
    }

    // --- replacing the result ----------------------------------------------------------------------------------------------------

    private void Replace(FieldInfo fi, List<EntryXml> entries)
    {
        Field field = fi.Field;
        MarkupNode begin = field.Markup.Node;
        if (field.SeparatorRun is not { } sepRef || field.EndRun is not { } endRef)
        {
            Note("layout.field-skipped", "A field without a result separator cannot be built.", fi);
            return;
        }

        MarkupNode separate = sepRef.Node;
        MarkupNode end = endRef.Node;
        MarkupNode pBegin = begin.Parent;
        MarkupNode pEnd = end.Parent;
        string prefix = pBegin.Prefix.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(pBegin.Prefix) + ":";
        if (prefix.Length == 0 || separate.Parent != pBegin || !Xml.Eq(pBegin.LocalName, "p") || !Xml.Eq(pEnd.LocalName, "p") || (pBegin != pEnd && pBegin.Parent != pEnd.Parent))
        {
            Note("layout.field-skipped", "The field's code and result are not laid out in paragraphs of one container, so its result is not built.", fi);
            return;
        }

        // Everything from the begin character to the separator stays (the field code); the old result goes.
        var codeXml = new StringBuilder();
        for (MarkupNode n = begin; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element)
            {
                codeXml.Append(n.OuterXml);
            }

            if (n == separate)
            {
                break;
            }
        }

        string leading = string.Empty; // content before the begin run in its paragraph
        var before = new StringBuilder();
        for (MarkupNode n = pBegin.FirstChild; !n.IsNull && n != begin; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && !Xml.Eq(n.LocalName, "pPr"))
            {
                before.Append(n.OuterXml);
            }
        }

        leading = before.ToString();
        string sectPr = string.Empty;
        MarkupNode oldPPr = FirstChild(pBegin, "pPr");
        if (!oldPPr.IsNull && FirstChild(oldPPr, "sectPr") is { IsNull: false } sp)
        {
            sectPr = sp.OuterXml;
        }

        string endXml = end.OuterXml;
        var tail = new StringBuilder();
        if (pBegin == pEnd)
        {
            for (MarkupNode n = end.NextSibling; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element)
                {
                    tail.Append(n.OuterXml);
                }
            }
        }

        // Collect what to remove before anything moves.
        var remove = new List<MarkupNode>();
        if (pBegin == pEnd)
        {
            for (MarkupNode n = pBegin.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element && !Xml.Eq(n.LocalName, "pPr"))
                {
                    remove.Add(n);
                }
            }
        }
        else
        {
            for (MarkupNode n = pBegin.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element && !Xml.Eq(n.LocalName, "pPr"))
                {
                    remove.Add(n);
                }
            }

            for (MarkupNode n = pBegin.NextSibling; !n.IsNull && n != pEnd; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element)
                {
                    remove.Add(n);
                }
            }

            for (MarkupNode n = pEnd.FirstChild; !n.IsNull && n != end; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element && !Xml.Eq(n.LocalName, "pPr"))
                {
                    remove.Add(n);
                }
            }
        }

        string P(EntryXml e, string prefixContent, string suffixContent, string extraPPr)
        {
            string pPr = (e.StyleId is null ? string.Empty : $"<{W}pStyle {W}val=\"{Esc(e.StyleId)}\"/>") + e.DirectProperties + extraPPr;
            return $"<{W}p>{(pPr.Length == 0 ? string.Empty : $"<{W}pPr>{pPr}</{W}pPr>")}{prefixContent}{e.Content}{suffixContent}</{W}p>";
        }

        // A field whose old result ended in a paragraph of its own (a table of contents built before, or written by Word) would leave that
        // paragraph behind as an empty line on every rebuild: its end character moves into the last entry instead, as for a one-paragraph field.
        bool mergeEnd = false;
        if (pBegin != pEnd && NextElement(end).IsNull && FirstChild(pEnd, "pPr") is var endPPr && (endPPr.IsNull || FirstChild(endPPr, "sectPr").IsNull))
        {
            mergeEnd = true;
            remove.Add(end);
        }

        bool single = pBegin == pEnd || mergeEnd;
        string closing = single ? endXml + tail : string.Empty;

        // The first entry goes into the field's own paragraph; the rest become paragraphs after it.
        EntryXml first = entries[0];
        string firstPPr = (first.StyleId is null ? string.Empty : $"<{W}pStyle {W}val=\"{Esc(first.StyleId)}\"/>") + first.DirectProperties + sectPr;
        string firstContent = leading + codeXml + first.Content + (entries.Count == 1 ? closing : string.Empty);

        var more = new StringBuilder();
        for (int i = 1; i < entries.Count; i++)
        {
            more.Append(P(entries[i], string.Empty, i == entries.Count - 1 ? closing : string.Empty, string.Empty));
        }

        foreach (MarkupNode n in remove)
        {
            n.Remove();
        }

        if (mergeEnd)
        {
            pEnd.Remove();
        }

        if (!oldPPr.IsNull)
        {
            oldPPr.Remove();
        }

        pBegin.PrependXml(Encoding.UTF8.GetBytes(firstPPr.Length == 0 ? string.Empty : $"<{W}pPr>{firstPPr}</{W}pPr>"));
        pBegin.AppendXml(Encoding.UTF8.GetBytes(firstContent));
        if (more.Length > 0)
        {
            pBegin.InsertXmlAfter(Encoding.UTF8.GetBytes(more.ToString()));
        }

        if (fi.Parsed.Type == "INDEX" && fi.Parsed.Switch("c")?.Value is { } cs && int.TryParse(cs, out int columns) && columns > 1)
        {
            MarkupNode last = single ? pBegin : pEnd;
            for (int i = 1; single && i < entries.Count; i++)
            {
                last = NextElement(last);
            }

            InColumns(fi, pBegin, last, Math.Min(columns, 4));
        }
    }

    private static MarkupNode NextElement(MarkupNode n)
    {
        MarkupNode next = n.NextSibling;
        while (!next.IsNull && next.Kind != MarkupNodeKind.Element)
        {
            next = next.NextSibling;
        }

        return next;
    }

    private static MarkupNode PreviousElement(MarkupNode n)
    {
        MarkupNode prev = n.PreviousSibling;
        while (!prev.IsNull && prev.Kind != MarkupNodeKind.Element)
        {
            prev = prev.PreviousSibling;
        }

        return prev;
    }

    // INDEX \c N: the index sits in its own continuous section with N columns (ECMA-376 Part 1 §17.16.5.29). The section break before it
    // closes the part of the section above with the section's own properties; the one after it carries the columns; the section
    // that goes on after the index becomes continuous.
    private void InColumns(FieldInfo fi, MarkupNode first, MarkupNode last, int columns)
    {
        IReadOnlyList<Section> sections = _doc.Sections;
        Section section = sections[Math.Min(_model.SectionOf(fi.ParagraphIndex), sections.Count - 1)];
        if (!section.PropertiesElement.IsValid)
        {
            Note("layout.field-skipped", "The section holding the index has no section properties, so the index columns are not applied.", fi);
            return;
        }

        MarkupNode sect = section.PropertiesElement.Node;
        string clone = sect.OuterXml;
        string wq = System.Text.RegularExpressions.Regex.Escape(W);
        string cols = System.Text.RegularExpressions.Regex.Replace(clone, "<" + wq + "(headerReference|footerReference|pgNumType|titlePg|type)\\b[^>]*/>", string.Empty);
        cols = System.Text.RegularExpressions.Regex.Replace(cols, "<" + wq + "cols\\b[^>]*/>|<" + wq + "cols\\b[^>]*>.*?</" + wq + "cols>", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        int close = cols.LastIndexOf("</" + W + "sectPr>", StringComparison.Ordinal);
        if (close < 0)
        {
            return;
        }

        int open = cols.IndexOf('>', StringComparison.Ordinal) + 1;
        cols = cols[..open] + $"<{W}type {W}val=\"continuous\"/>" + cols[open..close] + $"<{W}cols {W}num=\"{columns}\" {W}space=\"{section.Columns.Space}\"/>" + cols[close..];
        MarkupNode before = PreviousElement(first);
        if (!before.IsNull && Xml.Eq(before.LocalName, "p"))
        {
            AddSectPr(before, clone);
        }

        AddSectPr(last, cols);
        MarkupNode type = FirstChild(sect, "type");
        if (type.IsNull)
        {
            sect.PrependXml(Encoding.UTF8.GetBytes($"<{W}type {W}val=\"continuous\"/>"));
        }
        else
        {
            type.SetAttribute(type.NamespaceUri, "val", "continuous");
        }
    }

    private void AddSectPr(MarkupNode paragraph, string sectPr)
    {
        MarkupNode pPr = FirstChild(paragraph, "pPr");
        if (pPr.IsNull)
        {
            paragraph.PrependXml(Encoding.UTF8.GetBytes($"<{W}pPr>{sectPr}</{W}pPr>"));
        }
        else if (FirstChild(pPr, "sectPr").IsNull)
        {
            pPr.AppendXml(Encoding.UTF8.GetBytes(sectPr));
        }
    }
}
