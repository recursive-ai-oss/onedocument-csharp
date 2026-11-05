using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Documents.Notes;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// A bookmark of the body: where it starts, the text it encloses and the note reference inside it (for NOTEREF).
// ECMA-376 Part 1 §17.13.6 (bookmarkStart, bookmarkEnd): a range from the start marker to the end marker with the same id.
internal sealed class BookmarkInfo
{
    public required string Name;
    public required string Id;
    public int StartParagraph;
    public int StartOffset; // characters of the paragraph's text before the marker
    public int EndParagraph = -1;
    public bool Closed;
    public NoteKind? NoteKind;
    public long NoteId;
    public readonly StringBuilder Text = new();
}

// One field of the body with the place it sits at.
internal sealed class FieldInfo
{
    public required Field Field;
    public required FieldInstruction Parsed;
    public int ParagraphIndex;
    public int Offset; // characters of the paragraph's text before the field
    public int Order;
    public bool SeqValid;
    public long SeqValue;
    public bool SeqHidden;
}

// What layout needs to know about the document before any page exists: the paragraphs in order, the bookmarks, the fields and the
// counters that do not depend on pages (SEQ numbers, note numbers). Built once per layout from the document that is laid out.
internal sealed class FieldModel
{
    private readonly Dictionary<MarkupNode, int> _paragraphIndex = [];
    private readonly Dictionary<string, BookmarkInfo> _bookmarks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MarkupNode, FieldInfo> _fieldsByBegin = [];
    private readonly HashSet<int> _paragraphsWithFields = [];
    private int[]? _outline;
    private int[] _sectionOf = [];
    private readonly List<(NoteKind Kind, long Id, int Paragraph)> _noteReferences = [];

    private FieldModel(Document doc)
    {
        Doc = doc;
    }

    public Document Doc { get; }

    public List<Paragraph> Paragraphs { get; } = [];

    public List<FieldInfo> Fields { get; } = [];

    public IReadOnlyDictionary<string, BookmarkInfo> Bookmarks => _bookmarks;

    /// <summary>The number of sections of the body.</summary>
    public int SectionCount { get; private set; } = 1;

    public bool HasAnyField => Fields.Count > 0;

    public int IndexOf(Paragraph p) => _paragraphIndex.TryGetValue(p.Markup.Node, out int i) ? i : -1;

    public int IndexOfNode(MarkupNode paragraph) => _paragraphIndex.TryGetValue(paragraph, out int i) ? i : -1;

    public bool ParagraphHasField(int index) => _paragraphsWithFields.Contains(index);

    /// <summary>The outline level of a paragraph, 1 to 9, or 0 for body text (ECMA-376 Part 1 §17.3.1.20, outlineLvl; heading styles).</summary>
    public int OutlineLevel(int paragraph)
    {
        if (_outline is null)
        {
            ReadOutline();
        }

        return paragraph >= 0 && paragraph < _outline!.Length ? _outline[paragraph] : 0;
    }

    public int SectionOf(int paragraph) => paragraph >= 0 && paragraph < _sectionOf.Length ? _sectionOf[paragraph] : 0;

    public FieldInfo? FieldAt(MarkupNode begin) => _fieldsByBegin.GetValueOrDefault(begin);

    public BookmarkInfo? Bookmark(string name) => _bookmarks.GetValueOrDefault(name);

    public static FieldModel Build(Document doc)
    {
        var model = new FieldModel(doc);
        foreach (Paragraph p in doc.Body.Paragraphs())
        {
            model._paragraphIndex[p.Markup.Node] = model.Paragraphs.Count;
            model.Paragraphs.Add(p);
        }

        model.ReadFields();
        model.ReadSections();
        model.NumberSequences();
        model.WalkBody();
        return model;
    }

    private void ReadOutline()
    {
        _outline = new int[Paragraphs.Count];
        for (int i = 0; i < Paragraphs.Count; i++)
        {
            _outline[i] = OutlineLevelOf(Doc, Paragraphs[i]);
        }
    }

    // The level a paragraph has in the outline: its outline level (direct or from the style), else the digit of a "heading N" style name.
    internal static int OutlineLevelOf(Document doc, Paragraph p)
    {
        var pp = p.ResolvedProperties;
        if (pp.OutlineLevel is { } lvl and >= 0 and <= 8)
        {
            return (int)lvl + 1;
        }

        string? id = pp.StyleId;
        if (id is null)
        {
            return 0;
        }

        string name = (doc.Styles.Find(id)?.Name ?? id).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (name.StartsWith("heading", StringComparison.OrdinalIgnoreCase) && name.Length == 8 && name[7] is >= '1' and <= '9')
        {
            return name[7] - '0';
        }

        return 0;
    }

    // --- bookmarks, note references and the text between markers -----------------------------------------------------------------

    private readonly List<BookmarkInfo> _open = [];
    private readonly List<(FieldInfo? Info, bool Replaced)> _fieldStack = [];
    private int _suppress; // inside the result of a SEQ field whose computed number stands in for it
    private int _lastParagraph = -1;

    private void WalkBody()
    {
        if (Doc.Body.Container.IsValid)
        {
            WalkBlocks(Doc.Body.Container.Node);
        }

        // Ranges that never closed end with the document.
        foreach (BookmarkInfo b in _open)
        {
            b.EndParagraph = Math.Max(0, Paragraphs.Count - 1);
        }
    }

    private static bool IsWord(MarkupNode n) => new MarkupRef(n).Namespace == SchemaNamespace.Wordprocessing;

    private void WalkBlocks(MarkupNode parent)
    {
        for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (!IsWord(n))
            {
                // mc:AlternateContent and other wrappers: the Choice branch is the one Office reads.
                if (n.LocalNameString is "AlternateContent" or "Choice")
                {
                    WalkBlocks(n);
                }

                continue;
            }

            ReadOnlySpan<byte> local = n.LocalName;
            if (local.SequenceEqual("p"u8))
            {
                if (_paragraphIndex.TryGetValue(n, out int index))
                {
                    WalkParagraph(n, index);
                }
            }
            else if (local.SequenceEqual("bookmarkStart"u8))
            {
                // A marker between paragraphs starts at the beginning of the next one.
                StartBookmark(n, _lastParagraph + 1, 0);
            }
            else if (local.SequenceEqual("bookmarkEnd"u8))
            {
                EndBookmark(n.GetAttribute(n.NamespaceUri, "id"), Math.Max(0, _lastParagraph));
            }
            else if (local.SequenceEqual("tcPr"u8) || local.SequenceEqual("trPr"u8) || local.SequenceEqual("tblPr"u8) || local.SequenceEqual("sdtPr"u8)
                || local.SequenceEqual("sdtEndPr"u8) || local.SequenceEqual("sectPr"u8) || local.SequenceEqual("tblGrid"u8) || local.SequenceEqual("txbxContent"u8))
            {
                continue;
            }
            else
            {
                WalkBlocks(n);
            }
        }
    }

    private void WalkParagraph(MarkupNode p, int index)
    {
        _lastParagraph = index;
        int offset = 0;
        WalkInline(p, index, ref offset);
        // A range that goes on past the paragraph carries the paragraph mark in its text.
        foreach (BookmarkInfo b in _open)
        {
            b.Text.Append('\n');
        }
    }

    private void WalkInline(MarkupNode parent, int paragraph, ref int offset)
    {
        for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (!IsWord(n))
            {
                if (n.LocalNameString is "AlternateContent" or "Choice")
                {
                    WalkInline(n, paragraph, ref offset);
                }

                continue;
            }

            ReadOnlySpan<byte> local = n.LocalName;
            if (local.SequenceEqual("r"u8))
            {
                ReadRun(n, paragraph, ref offset);
            }
            else if (local.SequenceEqual("fldSimple"u8))
            {
                FieldInfo? fi = FieldAt(n);
                if (fi is not null)
                {
                    fi.Offset = offset;
                }

                bool replaced = fi is { SeqValid: true };
                if (replaced)
                {
                    AppendToBookmarks(FieldEvaluator.SeqText(fi!));
                    _suppress++;
                }

                WalkInline(n, paragraph, ref offset);
                if (replaced)
                {
                    _suppress--;
                }
            }
            else if (local.SequenceEqual("bookmarkStart"u8))
            {
                StartBookmark(n, paragraph, offset);
            }
            else if (local.SequenceEqual("bookmarkEnd"u8))
            {
                EndBookmark(n.GetAttribute(n.NamespaceUri, "id"), paragraph);
            }
            else if (local.SequenceEqual("pPr"u8) || local.SequenceEqual("rPr"u8) || local.SequenceEqual("sdtPr"u8) || local.SequenceEqual("sdtEndPr"u8)
                || local.SequenceEqual("customXmlPr"u8) || local.SequenceEqual("smartTagPr"u8) || local.SequenceEqual("proofErr"u8))
            {
                continue;
            }
            else
            {
                WalkInline(n, paragraph, ref offset);
            }
        }
    }

    private void ReadRun(MarkupNode run, int paragraph, ref int offset)
    {
        var sb = new StringBuilder();
        for (MarkupNode c = run.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element || !IsWord(c))
            {
                continue;
            }

            ReadOnlySpan<byte> local = c.LocalName;
            if (local.SequenceEqual("fldChar"u8))
            {
                FieldChar(run, c, offset);
            }
            else if (local.SequenceEqual("t"u8))
            {
                sb.Append(c.InnerText);
            }
            else if (local.SequenceEqual("tab"u8))
            {
                sb.Append('\t');
            }
            else if (local.SequenceEqual("br"u8) || local.SequenceEqual("cr"u8))
            {
                sb.Append('\n');
            }
            else if (local.SequenceEqual("noBreakHyphen"u8))
            {
                sb.Append('\u2011');
            }
            else if (local.SequenceEqual("softHyphen"u8))
            {
                sb.Append('\u00AD');
            }
            else if (local.SequenceEqual("footnoteReference"u8) || local.SequenceEqual("endnoteReference"u8))
            {
                NoteKind kind = local.SequenceEqual("footnoteReference"u8) ? NoteKind.Footnote : NoteKind.Endnote;
                long id = long.TryParse(c.GetAttribute(c.NamespaceUri, "id"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : 0;
                bool custom = c.GetAttribute(c.NamespaceUri, "customMarkFollows") is "1" or "true" or "on";
                if (!custom)
                {
                    _noteReferences.Add((kind, id, paragraph));
                }

                foreach (BookmarkInfo b in _open)
                {
                    if (b.NoteKind is null)
                    {
                        b.NoteKind = kind;
                        b.NoteId = id;
                    }
                }
            }
        }

        if (sb.Length == 0)
        {
            return;
        }

        offset += sb.Length;
        if (_suppress == 0)
        {
            AppendToBookmarks(sb.ToString());
        }
    }

    private void AppendToBookmarks(string text)
    {
        foreach (BookmarkInfo b in _open)
        {
            b.Text.Append(text);
        }
    }

    // A SEQ field's result inside a bookmark is the number layout computes, so a REF to a caption shows the number it will print.
    private void FieldChar(MarkupNode run, MarkupNode fldChar, int offset)
    {
        switch (fldChar.GetAttribute(fldChar.NamespaceUri, "fldCharType"))
        {
            case "begin":
                FieldInfo? fi = FieldAt(run);
                if (fi is not null)
                {
                    fi.Offset = offset;
                }

                _fieldStack.Add((fi, false));
                break;
            case "separate" when _fieldStack.Count > 0:
                (FieldInfo? info, _) = _fieldStack[^1];
                if (info is { SeqValid: true })
                {
                    AppendToBookmarks(FieldEvaluator.SeqText(info));
                    _suppress++;
                    _fieldStack[^1] = (info, true);
                }

                break;
            case "end" when _fieldStack.Count > 0:
                if (_fieldStack[^1].Replaced)
                {
                    _suppress--;
                }

                _fieldStack.RemoveAt(_fieldStack.Count - 1);
                break;
        }
    }

    private void StartBookmark(MarkupNode n, int paragraph, int offset)
    {
        string? name = n.GetAttribute(n.NamespaceUri, "name");
        string? id = n.GetAttribute(n.NamespaceUri, "id");
        if (string.IsNullOrEmpty(name) || id is null)
        {
            return;
        }

        var info = new BookmarkInfo { Name = name, Id = id, StartParagraph = Math.Clamp(paragraph, 0, Math.Max(0, Paragraphs.Count - 1)), StartOffset = offset };
        // The first definition of a name wins, as in Word (a name cannot be defined twice).
        _bookmarks.TryAdd(name, info);
        _open.Add(info);
    }

    private void EndBookmark(string? id, int paragraph)
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            if (_open[i].Id == id)
            {
                _open[i].EndParagraph = paragraph;
                _open[i].Closed = true;
                _open.RemoveAt(i);
                return;
            }
        }
    }

    // --- fields ----------------------------------------------------------------------------------------------------------------

    private void ReadFields()
    {
        int order = 0;
        foreach (Field f in Doc.Fields())
        {
            int paragraph = -1;
            for (MarkupNode n = f.Markup.Node; !n.IsNull; n = n.Parent)
            {
                if (_paragraphIndex.TryGetValue(n, out paragraph))
                {
                    break;
                }

                paragraph = -1;
            }

            var info = new FieldInfo { Field = f, Parsed = f.Parsed, ParagraphIndex = paragraph, Order = order++ };
            Fields.Add(info);
            _fieldsByBegin.TryAdd(f.Markup.Node, info);
            if (paragraph >= 0)
            {
                _paragraphsWithFields.Add(paragraph);
            }
        }
    }

    private void ReadSections()
    {
        _sectionOf = new int[Paragraphs.Count];
        int section = 0;
        for (int i = 0; i < Paragraphs.Count; i++)
        {
            _sectionOf[i] = section;
            if (Xml.Child(new MarkupRef(Paragraphs[i].Markup.Node), "pPr") is { } ppr && Xml.Child(ppr, "sectPr") is not null)
            {
                section++;
            }
        }

        SectionCount = section + 1;
    }

    // ECMA-376 Part 1 §17.16.5.56 (SEQ): a counter per identifier. \c repeats the last value, \r n sets it, \h hides the result, \s n restarts
    // the count at every heading of level n. A SEQ inside the cached result of another field (a table of contents copy, a REF) is a copy
    // and does not count.
    private void NumberSequences()
    {
        var counters = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var seenEpoch = new Dictionary<(string, int), int>();
        var epoch = new int[10];
        int nextParagraph = 0;
        foreach (FieldInfo fi in Fields)
        {
            if (fi.Parsed.Type != "SEQ" || fi.ParagraphIndex < 0 || IsCopy(fi.Field))
            {
                continue;
            }

            while (nextParagraph <= fi.ParagraphIndex)
            {
                int level = OutlineLevel(nextParagraph);
                if (level > 0)
                {
                    epoch[level]++;
                }

                nextParagraph++;
            }

            string id = fi.Parsed.FirstArgument ?? string.Empty;
            counters.TryGetValue(id, out long value);
            if (fi.Parsed.Switch("s")?.Value is { } s && int.TryParse(s, out int sl) && sl is >= 1 and <= 9)
            {
                seenEpoch.TryGetValue((id, sl), out int last);
                if (last != epoch[sl])
                {
                    value = 0;
                    seenEpoch[(id, sl)] = epoch[sl];
                }
            }

            if (fi.Parsed.HasSwitch("c"))
            {
                // keep the value
            }
            else if (fi.Parsed.Switch("r")?.Value is { } r && long.TryParse(r, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long reset))
            {
                value = reset;
            }
            else
            {
                value++;
            }

            counters[id] = value;
            fi.SeqValid = true;
            fi.SeqValue = value;
            // \h hides the result unless a general-formatting switch is also present (§17.16.5.56).
            fi.SeqHidden = fi.Parsed.HasSwitch("h") && !fi.Parsed.Switches.Any(static s => s.Name == "*");
        }
    }

    private static bool IsCopy(Field f)
    {
        for (Field? p = f.Parent; p is not null; p = p.Parent)
        {
            if (p.Type is "TOC" or "INDEX" or "TOA" or "REF" or "STYLEREF" or "PAGEREF" or "NOTEREF")
            {
                return true;
            }
        }

        return false;
    }

    // --- notes -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The number a note reference prints (ECMA-376 Part 1 §17.11): references count in document order from the start value, restarting at each
    /// section when the numbering says so. Per-page restarts need the pages and are not applied (the count continues).
    /// </summary>
    public bool TryNoteNumber(NoteKind kind, long id, out string text)
    {
        text = string.Empty;
        EffectiveNoteNumbering eff = NoteNumbering.Combine(null, Doc.GetNoteNumbering(kind));
        long n = eff.Start - 1;
        int section = -1;
        foreach ((NoteKind k, long refId, int paragraph) in _noteReferences)
        {
            if (k != kind)
            {
                continue;
            }

            int s = SectionOf(paragraph);
            if (eff.Restart == NoteRestart.EachSection && s != section)
            {
                n = eff.Start - 1;
            }

            section = s;
            n++;
            if (refId == id)
            {
                text = NumberFormatter.Format(eff.Format, n).Text;
                return true;
            }
        }

        return false;
    }

    public bool NotesRestartEachPage(NoteKind kind) => NoteNumbering.Combine(null, Doc.GetNoteNumbering(kind)).Restart == NoteRestart.EachPage;
}
