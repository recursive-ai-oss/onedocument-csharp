using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Layout;

// What one pass of layout tells the next about the pages: which page each paragraph starts on, what each page prints as its number,
// and how many pages the document and its sections have. Fields that depend on pages read these; layout runs again until a pass
// produces the facts it was given.
internal sealed class PageFact
{
    public long Printed;
    public string? Format; // w:pgNumType w:fmt of the page's section
    public int Section;
}

internal sealed class FieldFacts : IEquatable<FieldFacts>
{
    private ulong _hash;

    public required PageTotals Totals;
    public PageFact[] Pages = [];

    /// <summary>The 0-based page each body paragraph starts on, by the paragraph's index in the <see cref="FieldModel"/>; -1 when it was not placed.</summary>
    public int[] FirstPage = [];

    /// <summary>For a paragraph that is split across pages: each piece's page and the layout text offset of its first line.</summary>
    public Dictionary<int, List<(int Page, int TextStart)>> Pieces = [];

    /// <summary>The length of the layout text of each split paragraph (the text the piece offsets index).</summary>
    public Dictionary<int, int> LayoutTextLength = [];

    /// <summary>The paragraphs that start on each page, in page order.</summary>
    public List<int>[] ByPage = [];

    public static FieldFacts Build(IReadOnlyList<LayoutPage> pages, FieldModel model, PageTotals totals, Document doc)
    {
        var facts = new FieldFacts { Totals = totals };
        IReadOnlyList<Section> sections = doc.Sections;
        facts.Pages = new PageFact[pages.Count];
        facts.ByPage = new List<int>[pages.Count];
        facts.FirstPage = new int[model.Paragraphs.Count];
        Array.Fill(facts.FirstPage, -1);
        for (int i = 0; i < pages.Count; i++)
        {
            LayoutPage page = pages[i];
            string? format = page.SectionIndex < sections.Count ? sections[page.SectionIndex].PageNumbering.Format : null;
            facts.Pages[i] = new PageFact { Printed = page.PrintedNumber, Format = format, Section = page.SectionIndex };
            facts.ByPage[i] = [];
            Visit(facts, model, page, i);
        }

        // Only paragraphs that really split keep their pieces.
        foreach (int key in facts.Pieces.Keys.ToArray())
        {
            if (facts.Pieces[key].Count < 2)
            {
                facts.Pieces.Remove(key);
                facts.LayoutTextLength.Remove(key);
            }
        }

        facts.Seal();
        return facts;
    }

    private static void Visit(FieldFacts facts, FieldModel model, LayoutNode node, int pageIndex)
    {
        foreach (LayoutNode child in node.Children)
        {
            if (child is LayoutParagraph lp)
            {
                if (lp.Story != LayoutStory.Body)
                {
                    continue;
                }

                int index = model.IndexOf(lp.Source);
                if (index < 0)
                {
                    continue;
                }

                int first = facts.FirstPage[index];
                if (first >= 0 && first < pageIndex && !lp.ContinuesFromPreviousPage)
                {
                    continue; // a repeated table header row shows the paragraph again
                }

                if (first < 0)
                {
                    facts.FirstPage[index] = pageIndex;
                    facts.ByPage[pageIndex].Add(index);
                }

                if (!facts.Pieces.TryGetValue(index, out List<(int, int)>? list))
                {
                    list = [];
                    facts.Pieces[index] = list;
                    facts.LayoutTextLength[index] = lp.Text.Length;
                }

                int start = 0;
                foreach (LayoutLine line in lp.Lines)
                {
                    start = line.TextStart;
                    break;
                }

                if (list.Count == 0 || list[^1].Item1 != pageIndex)
                {
                    list.Add((pageIndex, start));
                }
            }
            else
            {
                Visit(facts, model, child, pageIndex);
            }
        }
    }

    /// <summary>The page (0-based) the character at <paramref name="offset"/> of a body paragraph's text is on; -1 when the paragraph was not placed.</summary>
    public int PageOf(FieldModel model, int paragraph, int offset)
    {
        if (paragraph < 0 || paragraph >= FirstPage.Length)
        {
            return -1;
        }

        if (!Pieces.TryGetValue(paragraph, out List<(int Page, int TextStart)>? pieces))
        {
            return FirstPage[paragraph];
        }

        // The marker sits at a fraction of the paragraph's text; the piece whose first line starts at or before that fraction holds it.
        int hubLength = Math.Max(1, model.Paragraphs[paragraph].Text.Length);
        double at = (double)Math.Clamp(offset, 0, hubLength) / hubLength * LayoutTextLength[paragraph];
        int page = pieces[0].Page;
        foreach ((int p, int textStart) in pieces)
        {
            if (textStart <= at)
            {
                page = p;
            }
        }

        return page;
    }

    private void Seal()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v)
        {
            unchecked
            {
                h = (h ^ (ulong)v) * 1099511628211UL;
            }
        }

        Mix(Totals.NumPages);
        foreach (int s in Totals.SectionPages)
        {
            Mix(s);
        }

        foreach (PageFact p in Pages)
        {
            Mix(p.Printed);
            Mix(p.Section);
            Mix(p.Format is null ? -1 : p.Format.Length);
        }

        foreach (int f in FirstPage)
        {
            Mix(f);
        }

        foreach (KeyValuePair<int, List<(int Page, int TextStart)>> kv in Pieces.OrderBy(static k => k.Key))
        {
            Mix(kv.Key);
            foreach ((int page, int textStart) in kv.Value)
            {
                Mix(page);
                Mix(textStart);
            }
        }

        _hash = h;
    }

    public bool Equals(FieldFacts? other) =>
        other is not null && _hash == other._hash && Pages.Length == other.Pages.Length && FirstPage.Length == other.FirstPage.Length && Totals.Equals(other.Totals);

    public override bool Equals(object? obj) => Equals(obj as FieldFacts);

    public override int GetHashCode() => (int)_hash;
}
