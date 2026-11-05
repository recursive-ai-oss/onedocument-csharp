using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Notes;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

internal readonly record struct NoteKey(NoteKind Kind, long Id);

// What a reference mark shows. A custom mark (w:customMarkFollows) is the text that follows the reference in its run; it takes no number.
internal sealed record NoteMark(string Text, bool Custom, int Section);

// The numbering of every note reference in the body, decided before any paragraph is formatted.
// ECMA-376 Part 1 §17.11 (footnotePr), §17.11 (endnotePr), §17.11 (numRestart), §17.11 (numStart), §17.11 (numFmt):
// the marks run in document order; a restart starts again at numStart in each section or on each page; section properties win over the
// document's. Numbering that depends on the page (restart each page) uses the pages of the previous layout pass.
internal sealed class NotePlan
{
    public static NotePlan Empty { get; } = new();

    private readonly Dictionary<NoteKey, NoteMark> _marks = [];

    public bool HasFootnotes { get; private set; }

    public bool HasEndnotes { get; private set; }

    /// <summary>Gets whether some section numbers footnotes anew on each page, so the numbers depend on the pagination.</summary>
    public bool UsesPageRestart { get; private set; }

    public EffectiveNoteNumbering[] Footnote { get; private set; } = [];

    public EffectiveNoteNumbering[] Endnote { get; private set; } = [];

    /// <summary>Gets, for each section, the endnote ids its body refers to in order.</summary>
    public List<long>[] EndnotesBySection { get; private set; } = [];

    public bool TryGet(NoteKind kind, long id, out NoteMark mark) => _marks.TryGetValue(new NoteKey(kind, id), out mark!);

    public static NotePlan Build(Document doc, IReadOnlyList<Section> sections, IReadOnlyDictionary<NoteKey, int>? pages)
    {
        bool footnotes = doc.Footnotes.Normal().Count > 0;
        bool endnotes = doc.Endnotes.Normal().Count > 0;
        if (!footnotes && !endnotes)
        {
            return Empty;
        }

        var plan = new NotePlan { HasFootnotes = footnotes, HasEndnotes = endnotes };
        NoteNumbering docFootnote = doc.GetNoteNumbering(NoteKind.Footnote);
        NoteNumbering docEndnote = doc.GetNoteNumbering(NoteKind.Endnote);
        plan.Footnote = new EffectiveNoteNumbering[sections.Count];
        plan.Endnote = new EffectiveNoteNumbering[sections.Count];
        plan.EndnotesBySection = new List<long>[sections.Count];
        var refs = new List<(NoteKey Key, bool Custom)>[sections.Count];
        for (int s = 0; s < sections.Count; s++)
        {
            Section section = sections[s];
            plan.Footnote[s] = NoteNumbering.Combine(doc.GetNoteNumbering(section.PropertiesElement, NoteKind.Footnote), docFootnote);
            plan.Endnote[s] = NoteNumbering.Combine(doc.GetNoteNumbering(section.PropertiesElement, NoteKind.Endnote), docEndnote);
            plan.UsesPageRestart |= plan.Footnote[s].Restart == NoteRestart.EachPage;
            refs[s] = [];
            plan.EndnotesBySection[s] = [];
            foreach (BlockElement b in section.Blocks())
            {
                Scan(b.Markup.Node, refs[s], 0);
            }
        }

        plan.Number(doc, refs, NoteKind.Footnote, plan.Footnote, pages);
        plan.Number(doc, refs, NoteKind.Endnote, plan.Endnote, pages);
        for (int s = 0; s < sections.Count; s++)
        {
            foreach ((NoteKey key, _) in refs[s])
            {
                if (key.Kind == NoteKind.Endnote && plan._marks.ContainsKey(key) && !plan.EndnotesBySection[s].Contains(key.Id))
                {
                    plan.EndnotesBySection[s].Add(key.Id);
                }
            }
        }

        return plan;
    }

    private void Number(Document doc, List<(NoteKey Key, bool Custom)>[] refs, NoteKind kind, EffectiveNoteNumbering[] eff, IReadOnlyDictionary<NoteKey, int>? pages)
    {
        var byId = new Dictionary<long, Note>();
        foreach (Note n in kind == NoteKind.Footnote ? doc.Footnotes : doc.Endnotes)
        {
            byId.TryAdd(n.Id, n);
        }

        long counter = 0;
        int lastPage = -1;
        for (int s = 0; s < refs.Length; s++)
        {
            EffectiveNoteNumbering e = eff[s];
            if (s == 0 || e.Restart != NoteRestart.Continuous)
            {
                // A restart each section (and the first section of the document) starts at numStart.
                counter = e.Start - 1;
                lastPage = -1;
            }

            foreach ((NoteKey key, bool custom) in refs[s])
            {
                if (_marks.ContainsKey(key) || !byId.TryGetValue(key.Id, out Note? note) || note.Type != NoteType.Normal)
                {
                    continue;
                }

                if (custom)
                {
                    _marks[key] = new NoteMark(string.Empty, true, s);
                    continue;
                }

                // Only footnotes can restart on each page; Word does not offer it for endnotes.
                if (kind == NoteKind.Footnote && e.Restart == NoteRestart.EachPage && pages is not null && pages.TryGetValue(key, out int page) && page != lastPage)
                {
                    counter = e.Start - 1;
                    lastPage = page;
                }

                counter++;
                _marks[key] = new NoteMark(NumberFormatter.Format(e.Format, counter).Text, false, s);
            }
        }
    }

    // Finds the references below a block in document order. Text boxes cannot hold notes in Word, and the fallback branch of
    // alternate content repeats the chosen branch, so neither is entered.
    private static void Scan(MarkupNode node, List<(NoteKey, bool)> into, int depth)
    {
        if (depth > 256)
        {
            return;
        }

        for (MarkupNode c = node.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (Xml.Eq(c.LocalName, "Fallback") || Xml.Eq(c.LocalName, "txbxContent"))
            {
                continue;
            }

            var r = new MarkupRef(c);
            if (r.Namespace == SchemaNamespace.Wordprocessing)
            {
                NoteKind? kind = Xml.Eq(c.LocalName, "footnoteReference") ? NoteKind.Footnote : Xml.Eq(c.LocalName, "endnoteReference") ? NoteKind.Endnote : null;
                if (kind is { } k && Xml.Long(Xml.Attr(r, "id")) is { } id)
                {
                    string? custom = Xml.Attr(r, "customMarkFollows");
                    into.Add((new NoteKey(k, id), custom is "1" or "true" or "on"));
                    continue;
                }
            }

            Scan(c, into, depth + 1);
        }
    }
}
