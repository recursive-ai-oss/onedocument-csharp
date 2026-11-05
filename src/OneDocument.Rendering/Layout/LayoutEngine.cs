using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Numbering;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>
/// Lays a document out into pages: sections with their page sizes, margins and columns, headers and footers with page numbers, page
/// borders, watermarks and line numbers, and the paragraphs with widow and orphan control, keep-with-next, keep-lines-together and
/// page-break-before. Tables (see docs/layout-tables.md); floating objects, notes and fields other than page numbers are later stages and are reported in
/// <see cref="LayoutDocument.Notes"/>.
/// </summary>
public static partial class LayoutEngine
{
    /// <summary>Lays out a document.</summary>
    /// <param name="document">The document. It is read, not changed.</param>
    /// <param name="options">Fonts and limits; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the layout between paragraphs.</param>
    /// <returns>The pages with their paragraphs, lines and runs, the font substitutions and what was skipped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static LayoutDocument Layout(Document document, LayoutOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        LayoutOptions opt = options ?? new LayoutOptions();
        IFontProvider fonts = opt.Fonts ?? CreateFonts(document, opt);
        CompatSettings compat = CompatSettings.Read(document);
        IReadOnlyDictionary<string, DocumentFontInfo> fontTable = DocumentFonts.ReadFontTable(document);

        // NUMPAGES and SECTIONPAGES in a header or footer need the page counts, which exist only after the pages do: lay out again with
        // the previous pass's counts until they stop changing (at most three passes). This is the small loop the general field update
        // replaces.
        PageTotals? totals = null;
        for (int pass = 0; ; pass++)
        {
            var ctx = new LayoutContext(document, fonts, compat, fontTable);
            var flow = new Flow(ctx, opt, totals, probe: false);
            IReadOnlyList<Section> sections = document.Sections;
            for (int s = 0; s < sections.Count; s++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool balance = s + 1 < sections.Count && sections[s + 1].BreakType == SectionBreakType.Continuous;
                flow.RunSection(sections[s], balance, cancellationToken);
                if (flow.Stopped)
                {
                    break;
                }
            }

            flow.Finish();
            PageTotals measured = PageTotals.Of(flow.Pages, sections.Count);
            if (!ctx.UsesPageTotals || pass >= 2 || (totals is not null && totals.Equals(measured)))
            {
                return new LayoutDocument(flow.Pages, ctx.FontNotes, ctx.Notes, ctx.Compat.Mode);
            }

            totals = measured;
        }
    }

    private static FontLibrary CreateFonts(Document document, LayoutOptions opt)
    {
        FontLibrary fonts = FontLibrary.CreateDefault();
        fonts.AddOptionalPackages();
        if (opt.UseEmbeddedFonts)
        {
            DocumentFonts.AddEmbeddedFonts(document, fonts);
        }

        return fonts;
    }

    // The page-by-page placement state. Page set-up (section geometry, headers and footers, columns, decorations) is in Pages/Flow.Pages.cs.
    private sealed partial class Flow(LayoutContext ctx, LayoutOptions options, PageTotals? totals, bool probe)
    {
        private readonly List<LayoutPage> _pages = [];
        private LayoutPage? _page;
        private double _y;
        private bool _pageHasContent;
        private bool _honourSpaceBefore = true;
        private double _pendingAfter;
        private FormattedParagraph? _previous;
        private int _sectionIndex;
        private double _textWidth;
        private bool _forceBreakBeforeNext;
        private bool _forceIsColumn;

        private LayoutContext Ctx => ctx;

        public bool Stopped { get; private set; }

        public IReadOnlyList<LayoutPage> Pages => _pages;

        public void Finish()
        {
            if (_pages.Count == 0)
            {
                // An empty document still has one page.
                StartPage();
            }

            FinalizePage();
        }

        private void Collect(IEnumerable<BlockElement> elements, List<FormattedParagraph> into, CancellationToken ct)
        {
            foreach (BlockElement b in elements)
            {
                ct.ThrowIfCancellationRequested();
                switch (b)
                {
                    case Paragraph p:
                        FormattedParagraph fp = ParagraphFormatter.Format(p, ctx, _textWidth);
                        SnapToGrid(fp);
                        into.Add(fp);
                        break;
                    case BlockContentControl cc:
                        Collect(cc.Content.Blocks(), into, ct);
                        break;
                    case Table t:
                        into.Add(TableFormatter.FormatBlock(t, ctx, _textWidth));
                        break;
                    default:
                        ctx.NoteOnce("layout.block-skipped", "layout.block-skipped", "A block that is not a paragraph or a table is left out of the pages.", b);
                        break;
                }
            }
        }

        // Overflow: the next column of the region if there is one, else a new page.
        private void NewPage(bool honourSpace)
        {
            AdvanceColumnOrPage();
            _honourSpaceBefore = honourSpace || _pages.Count == 1;
        }

        // A hard page break or page-break-before: always a new page, even in a multi-column section.
        private void ForcePage(bool honourSpace)
        {
            StartPage();
            _honourSpaceBefore = honourSpace || _pages.Count == 1;
        }

        private double Gap(FormattedParagraph fp)
        {
            double before = fp.SpaceBefore;
            double after = _pendingAfter;
            if (_previous is not null && fp.StyleId == _previous.StyleId)
            {
                if (fp.ContextualSpacing)
                {
                    before = 0;
                }

                if (_previous.ContextualSpacing)
                {
                    after = 0;
                }
            }

            if (!_pageHasContent)
            {
                // Word drops space before at the top of a page that came from a natural break; the first page and pages that start at a
                // forced break keep it (to be confirmed against Word's own output; see docs/layout.md).
                return _honourSpaceBefore ? before : 0;
            }

            return after + before;
        }

        private void Place(List<FormattedParagraph> blocks, int index)
        {
            FormattedParagraph fp = blocks[index];
            if (fp is FormattedTableBlock tableBlock)
            {
                PlaceTable(tableBlock.Table);
                return;
            }

            if (NeedsPage)
            {
                StartPage();
                if (Stopped)
                {
                    return;
                }
            }

            if (_forceBreakBeforeNext)
            {
                _forceBreakBeforeNext = false;
                if (!_pageHasContent)
                {
                    // Nothing follows a break on an empty page or column: no blank one is made.
                }
                else if (_forceIsColumn)
                {
                    // ECMA-376 Part 1 §17.3.3.1 (br type=column): the text after it starts in the next column; with one column that is the next page.
                    NewPage(honourSpace: true);
                }
                else
                {
                    ForcePage(honourSpace: true);
                }
            }

            if (fp.PageBreakBefore && _pageHasContent)
            {
                ForcePage(honourSpace: true);
            }

            // Keep with next: the paragraph stays on the page with the first lines of the next one.
            if (fp.KeepNext && _pageHasContent && index + 1 < blocks.Count)
            {
                double need = Gap(fp) + Total(fp, 0, fp.Lines.Count);
                int j = index + 1;
                int guard = 0;
                FormattedParagraph cur = fp;
                while (j < blocks.Count && guard++ < 20)
                {
                    FormattedParagraph next = blocks[j];
                    need += cur.SpaceAfter + next.SpaceBefore;
                    if (next.KeepNext)
                    {
                        need += Total(next, 0, next.Lines.Count);
                        cur = next;
                        j++;
                        continue;
                    }

                    need += Total(next, 0, Math.Min(next.Lines.Count, 2));
                    break;
                }

                if (_y + need > _limitBottom + 0.01 && need <= _limitBottom - _regionTop)
                {
                    NewPage(honourSpace: false);
                }
            }

            int placed = 0;
            while (placed < fp.Lines.Count && !Stopped)
            {
                bool wantSpace = NeedsFloatSpace(fp);
                if (Math.Abs(fp.FormattedWidth - _textWidth) > 0.5 || wantSpace || fp.Reflowed)
                {
                    // The column the text moved to has another width, or objects on the page take part of it: cut the lines again, keeping
                    // the ones already placed, and resume where the text had got to. The lines are cut around what is on the page now.
                    double gapNow = Gap(fp);
                    double y0 = _y + (placed == 0 ? gapNow : 0);
                    ILineSpace? space = wantSpace ? BuildSpace(fp, y0, placed == 0 ? fp.BorderTopHeight : 0, includeOwn: placed == 0) : null;
                    fp = ParagraphFormatter.Format(fp.Source, ctx, _textWidth, space, placed > 0 ? fp : null, placed);
                    SnapToGrid(fp);
                    blocks[index] = fp;
                    if (placed >= fp.Lines.Count)
                    {
                        break;
                    }
                }

                double gap = Gap(fp);
                double avail = _limitBottom - (_y + (placed == 0 ? gap : 0));
                double remainingHeight = Total(fp, placed, fp.Lines.Count - placed, includeTop: placed == 0);
                int count;
                if (remainingHeight <= avail + 0.01)
                {
                    count = fp.Lines.Count - placed;
                }
                else
                {
                    count = FitLines(fp, placed, avail);
                    int remainingLines = fp.Lines.Count - placed;
                    bool keepTogether = fp.KeepLines && placed == 0;
                    if (fp.WidowControl && count < remainingLines)
                    {
                        // Orphan control keeps two lines at the bottom, widow control two at the top of the next page.
                        if (placed == 0 && count < 2 && remainingLines >= 2)
                        {
                            count = 0;
                        }
                        else if (remainingLines - count == 1)
                        {
                            count = placed == 0 && count - 1 < 2 ? 0 : count - 1;
                        }
                    }

                    if (keepTogether)
                    {
                        count = 0;
                    }

                    if (count <= 0)
                    {
                        if (_pageHasContent)
                        {
                            NewPage(honourSpace: false);
                            continue;
                        }

                        count = Math.Max(1, FitLines(fp, placed, avail));
                    }
                }

                // A page or column break in the middle of a paragraph ends the lines on this page after the line that holds it.
                for (int k = placed; k < placed + count; k++)
                {
                    if (fp.Lines[k].EndsWithPageBreak)
                    {
                        count = k - placed + 1;
                        break;
                    }
                }

                Emit(fp, placed, count, gap, placed == 0, placed + count >= fp.Lines.Count);
                placed += count;
                if (placed < fp.Lines.Count)
                {
                    if (_forceBreakBeforeNext)
                    {
                        _forceBreakBeforeNext = false;
                        if (_forceIsColumn)
                        {
                            NewPage(honourSpace: true);
                        }
                        else
                        {
                            ForcePage(honourSpace: true);
                        }
                    }
                    else
                    {
                        NewPage(honourSpace: false);
                    }
                }
            }

            _previous = fp;
            _pendingAfter = fp.SpaceAfter + fp.BorderBottomHeight * 0;
        }

        private static double Total(FormattedParagraph fp, int from, int count, bool includeTop = true)
        {
            double h = 0;
            for (int i = from; i < from + count && i < fp.Lines.Count; i++)
            {
                h += fp.Lines[i].Advance;
            }

            if (includeTop && from == 0)
            {
                h += fp.BorderTopHeight;
            }

            if (from + count >= fp.Lines.Count)
            {
                h += fp.BorderBottomHeight;
            }

            return h;
        }

        private static int FitLines(FormattedParagraph fp, int from, double avail)
        {
            double h = from == 0 ? fp.BorderTopHeight : 0;
            int n = 0;
            for (int i = from; i < fp.Lines.Count; i++)
            {
                h += fp.Lines[i].Advance;
                if (h > avail + 0.01)
                {
                    break;
                }

                n++;
            }

            return n;
        }

        private void Emit(FormattedParagraph fp, int from, int count, double gap, bool first, bool last)
        {
            LayoutPage page = _page!;
            double top = _y + (first || !_pageHasContent ? gap : 0);
            if (!first)
            {
                top = _y;
            }

            LayoutParagraph lp = ParagraphEmitter.Build(fp, from, count, _colX, top, first, last, out double y, out bool breakAfter);
            lp.Story = _story;
            lp.Column = _colIndex;
            bool columnBreak = false;
            if (breakAfter)
            {
                // The break that ended the lines is a column break when its character is U+000E.
                for (int i = from + count - 1; i >= from; i--)
                {
                    FormattedLine fl = fp.Lines[i];
                    if (fl.EndsWithPageBreak)
                    {
                        columnBreak = fl.TextLength > 0 && fp.Text[fl.TextStart + fl.TextLength - 1] == '\u000E';
                        break;
                    }
                }
            }

            if (_story == LayoutStory.Body)
            {
                int n = 0;
                foreach (LayoutLine line in lp.Lines)
                {
                    FormattedLine fl = fp.Lines[from + n++];
                    if (!fl.SameRow)
                    {
                        NumberLine(fp, fl, line, line.Baseline - fl.Ascent, _colX);
                    }
                }
            }

            page.Add(lp);
            if (first)
            {
                CommitFloats(fp, lp, top);
            }

            _y = y;
            _pageHasContent = true;
            _honourSpaceBefore = true;
            _regionMaxY = Math.Max(_regionMaxY, y);
            if (breakAfter && _story == LayoutStory.Body)
            {
                _forceBreakBeforeNext = true;
                _forceIsColumn = columnBreak;
            }
        }
    }
}
