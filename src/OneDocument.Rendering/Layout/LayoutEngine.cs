using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>
/// Lays a document out into pages. This stage flows paragraphs down single-column pages of each section's size and margins, with
/// widow and orphan control, keep-with-next, keep-lines-together and page-break-before. Tables, headers and footers, columns, floating
/// objects, notes and fields are later stages and are reported in <see cref="LayoutDocument.Notes"/>.
/// </summary>
public static class LayoutEngine
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
        var ctx = new LayoutContext(document, fonts, CompatSettings.Read(document), DocumentFonts.ReadFontTable(document));
        var flow = new Flow(ctx, opt);

        IReadOnlyList<Section> sections = document.Sections;
        for (int s = 0; s < sections.Count; s++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            flow.RunSection(sections[s], cancellationToken);
            if (flow.Stopped)
            {
                break;
            }
        }

        flow.Finish();
        return new LayoutDocument(flow.Pages, ctx.FontNotes, ctx.Notes, ctx.Compat.Mode);
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

    // The page-by-page placement state.
    private sealed class Flow(LayoutContext ctx, LayoutOptions options)
    {
        private readonly List<LayoutPage> _pages = [];
        private LayoutPage? _page;
        private double _y;
        private bool _pageHasContent;
        private bool _honourSpaceBefore = true;
        private double _pendingAfter;
        private FormattedParagraph? _previous;
        private int _sectionIndex;
        private LayoutRect _content;
        private double _pageWidth;
        private double _pageHeight;
        private double _textWidth;
        private bool _forceBreakBeforeNext;

        public bool Stopped { get; private set; }

        public IReadOnlyList<LayoutPage> Pages => _pages;

        public void Finish()
        {
            if (_pages.Count == 0)
            {
                // An empty document still has one page.
                StartPage();
            }
        }

        public void RunSection(Section section, CancellationToken ct)
        {
            _sectionIndex = section.Index;
            PageSize size = section.PageSize;
            PageMargins m = section.Margins;
            double width = size.Width / 20.0;
            double height = size.Height / 20.0;
            double left = (m.Left + m.Gutter) / 20.0;
            double right = m.Right / 20.0;
            double top = m.Top / 20.0;
            double bottom = m.Bottom / 20.0;
            var content = new LayoutRect(left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom));

            bool continuous = section.BreakType == SectionBreakType.Continuous && _page is not null && Math.Abs(width - _pageWidth) < 0.5 && Math.Abs(height - _pageHeight) < 0.5;
            if (!continuous)
            {
                if (_page is not null)
                {
                    _page = null;
                }

                _pageWidth = width;
                _pageHeight = height;
                _content = content;
                _textWidth = content.Width;
                bool odd = section.BreakType == SectionBreakType.OddPage;
                bool even = section.BreakType == SectionBreakType.EvenPage;
                if ((odd && _pages.Count % 2 == 1) || (even && _pages.Count % 2 == 0))
                {
                    // A blank page keeps the section on an odd or even page.
                    StartPage();
                    _page = null;
                    ctx.NoteOnce("layout.blank-page", "layout.blank-page", "A blank page was inserted to start a section on an odd or even page.", null);
                }

                _previous = null;
                _pendingAfter = 0;
            }
            else
            {
                _textWidth = _content.Width;
            }

            var blocks = new List<FormattedParagraph>();
            Collect(section.Blocks(), blocks, ct);
            for (int i = 0; i < blocks.Count && !Stopped; i++)
            {
                ct.ThrowIfCancellationRequested();
                Place(blocks, i);
            }
        }

        private void Collect(IEnumerable<BlockElement> elements, List<FormattedParagraph> into, CancellationToken ct)
        {
            foreach (BlockElement b in elements)
            {
                ct.ThrowIfCancellationRequested();
                switch (b)
                {
                    case Paragraph p:
                        into.Add(ParagraphFormatter.Format(p, ctx, _textWidth));
                        break;
                    case BlockContentControl cc:
                        Collect(cc.Content.Blocks(), into, ct);
                        break;
                    case Table:
                        ctx.NoteOnce("layout.table-skipped", "layout.table-skipped", "Tables are not laid out yet; their content is left out of the pages.", b);
                        break;
                    default:
                        ctx.NoteOnce("layout.block-skipped", "layout.block-skipped", "A block that is not a paragraph or a table is left out of the pages.", b);
                        break;
                }
            }
        }

        private void StartPage()
        {
            if (_pages.Count >= options.MaxPages)
            {
                Stopped = true;
                ctx.NoteOnce("layout.page-limit", "layout.page-limit", "Layout stopped at the page limit.", null);
                return;
            }

            _page = new LayoutPage(_pages.Count + 1, _sectionIndex, _pageWidth == 0 ? 612 : _pageWidth, _pageHeight == 0 ? 792 : _pageHeight, _content.Width == 0 ? new LayoutRect(72, 72, 468, 648) : _content);
            _pages.Add(_page);
            _y = _page.ContentArea.Y;
            _pageHasContent = false;
            _honourSpaceBefore = _pages.Count == 1;
            _pendingAfter = 0;
        }

        private void NewPage(bool honourSpace)
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
            if (_page is null)
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
                if (_pageHasContent)
                {
                    NewPage(honourSpace: true);
                }
            }

            if (fp.PageBreakBefore && _pageHasContent)
            {
                NewPage(honourSpace: true);
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

                if (_y + need > _page!.ContentArea.Bottom + 0.01 && need <= _page.ContentArea.Height)
                {
                    NewPage(honourSpace: false);
                }
            }

            int placed = 0;
            while (placed < fp.Lines.Count && !Stopped)
            {
                double gap = Gap(fp);
                double avail = _page!.ContentArea.Bottom - (_y + (placed == 0 ? gap : 0));
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

                Emit(fp, placed, count, gap, placed == 0, placed + count >= fp.Lines.Count);
                placed += count;
                if (placed < fp.Lines.Count)
                {
                    NewPage(honourSpace: false);
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
                h += fp.Lines[i].Height;
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
                h += fp.Lines[i].Height;
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

            double y = top + (first ? fp.BorderTopHeight : 0);
            var lp = new LayoutParagraph(fp.Source, fp.Text)
            {
                Label = first ? fp.Label : null,
                HeadingLevel = fp.Heading,
                IsRightToLeft = fp.Rtl,
                ContinuesFromPreviousPage = !first,
                ContinuesOnNextPage = !last,
                Paint = fp.Paint,
            };

            double left = page.ContentArea.X;
            bool breakAfter = false;
            for (int i = from; i < from + count; i++)
            {
                FormattedLine fl = fp.Lines[i];
                var line = new LayoutLine
                {
                    Ascent = fl.Ascent,
                    Descent = fl.Descent,
                    Baseline = y + fl.Ascent,
                    TextStart = fl.TextStart,
                    TextLength = fl.TextLength,
                    Text = fl.Text,
                    IsLastLine = fl.IsLast,
                };
                double minX = double.MaxValue;
                double maxX = double.MinValue;
                foreach (LineItem item in fl.Items)
                {
                    var run = new LayoutRun(item.Kind)
                    {
                        Text = item.Text,
                        TextStart = item.TextStart,
                        Source = item.Source,
                        Font = item.Font,
                        FontSize = item.Size,
                        BidiLevel = item.Level,
                        Bounds = new LayoutRect(left + item.X, y, item.Width, fl.Height),
                        ClusterStarts = item.ClusterStarts,
                        ClusterX = [.. item.ClusterX.Select(v => v + left)],
                        Paint = item.Paint,
                        Glyphs = item.Glyphs,
                    };
                    line.Add(run);
                    minX = Math.Min(minX, left + item.X);
                    maxX = Math.Max(maxX, left + item.X + item.Width);
                }

                if (minX > maxX)
                {
                    minX = left + fp.Paint.LeftEdge;
                    maxX = minX;
                }

                line.Bounds = new LayoutRect(minX, y, maxX - minX, fl.Height);
                lp.Add(line);
                y += fl.Height;
                if (fl.EndsWithPageBreak)
                {
                    breakAfter = true;
                }
            }

            if (last)
            {
                y += fp.BorderBottomHeight;
            }

            lp.Bounds = new LayoutRect(left + fp.Paint.LeftEdge, top, Math.Max(0, fp.Paint.RightEdge - fp.Paint.LeftEdge), y - top);
            page.Add(lp);
            _y = y;
            _pageHasContent = true;
            _honourSpaceBefore = true;
            if (breakAfter)
            {
                _forceBreakBeforeNext = true;
            }
        }
    }
}
