using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// Page counts measured by one layout pass, for the NUMPAGES and SECTIONPAGES fields of the next.
internal sealed class PageTotals : IEquatable<PageTotals>
{
    public int NumPages;
    public int[] SectionPages = [];

    public static PageTotals Of(IReadOnlyList<LayoutPage> pages, int sectionCount)
    {
        int size = sectionCount;
        foreach (LayoutPage p in pages)
        {
            size = Math.Max(size, p.SectionIndex + 1);
        }

        var t = new PageTotals { NumPages = pages.Count, SectionPages = new int[size] };
        foreach (LayoutPage p in pages)
        {
            t.SectionPages[p.SectionIndex]++;
        }

        return t;
    }

    public bool Equals(PageTotals? other) => other is not null && NumPages == other.NumPages && SectionPages.AsSpan().SequenceEqual(other.SectionPages);

    public override bool Equals(object? obj) => Equals(obj as PageTotals);

    public override int GetHashCode() => NumPages;
}

public static partial class LayoutEngine
{
    // The page set-up half of the flow: section geometry and breaks, columns, headers and footers, page numbers, borders, watermarks,
    // line numbers and the page grid. Paragraph placement stays in LayoutEngine.cs and reads the fields below.
    private sealed partial class Flow
    {
        private sealed class RegionState(double top, SectionGeometry geo, double areaX, int sectionIndex)
        {
            public double Top = top;
            public double MaxY = top;
            public readonly SectionGeometry Geo = geo;
            public readonly double AreaX = areaX;
            public readonly int SectionIndex = sectionIndex;
        }

        private SectionGeometry? _geo; // the section being laid out
        private SectionGeometry? _pageGeo; // the section the open page started in
        private ColumnSlot[] _cols = [new ColumnSlot(0, 468)];
        private int _colIndex;
        private double _areaX = 72;
        private double _colX = 72;
        private double _regionTop;
        private double _regionMaxY;
        private double _bodyTop;
        private double _bodyBottom = 720;
        private double _limitBottom = 720;
        private LayoutStory _story;
        private long _printedLast;
        private bool _sectionStartPending;
        private bool _needPage;
        private int _sectionOrdinal;
        private long _lineCounter;
        private bool _lineNumbersStarted;
        private bool _balanceOverflow;
        private RegionState? _region;
        private List<RegionState> _regions = [];
        private FontMatch? _numberFont;
        private double _numberSize = 10;

        private bool NeedsPage => _page is null || _needPage;

        public bool Overflowed => _balanceOverflow;

        public void RunSection(Section section, bool balance, CancellationToken ct)
        {
            SectionGeometry geo = SectionGeometry.Read(section, ctx.Compat);
            SectionBreakType type = section.BreakType;
            bool sameSize = _geo is not null && _page is not null && !_needPage && Math.Abs(geo.Width - _geo.Width) < 0.5 && Math.Abs(geo.Height - _geo.Height) < 0.5;
            bool continuous = type is SectionBreakType.Continuous or SectionBreakType.NextColumn && sameSize;
            _sectionIndex = section.Index;
            _sectionOrdinal++;
            _geo = geo;
            ResetLineNumbers(geo, firstOfDocument: _pages.Count == 0 && !_lineNumbersStarted);

            if (!continuous)
            {
                _sectionStartPending = true;
                if (_pages.Count > 0)
                {
                    // ECMA-376 Part 1 §17.6.22 (type): oddPage and evenPage sections start on a page with that printed number, so a blank
                    // page is inserted when the next one has the wrong parity. Word also keeps physical and printed parity together when
                    // even and odd headers differ (observed in Word 365; see Section.RequiresBlankPageBefore).
                    long next = section.FirstPrintedPageNumber(_printedLast);
                    bool blank = (type == SectionBreakType.OddPage && (next & 1) == 0) || (type == SectionBreakType.EvenPage && (next & 1) == 1);
                    if (!blank && type is SectionBreakType.NextPage or SectionBreakType.NextColumn && ctx.Compat.EvenAndOddHeaders)
                    {
                        blank = section.RequiresBlankPageBefore(_pages.Count + 1, next);
                    }

                    if (blank)
                    {
                        StartPage(blank: true);
                        ctx.NoteOnce("layout.blank-page", "layout.blank-page", "A blank page was inserted to start a section on an odd or even page.", null);
                    }
                }

                _needPage = true;
                _previous = null;
                _pendingAfter = 0;
                if (!Stopped)
                {
                    StartPage();
                    // Observed in Word 16.0.20527: the first paragraph of a section that starts a page keeps its space before, as after a
                    // hard page break (a natural break drops it).
                    _honourSpaceBefore = true;
                }
            }
            else
            {
                // A continuous section continues on the same page below the text above it, with its own columns (and margins).
                if (_cols.Length > 1)
                {
                    _pendingAfter = 0;
                }

                double top = Math.Max(_regionMaxY, _y);
                LayoutRect area = geo.TextArea(_page!.PrintedNumber);
                _areaX = area.X;
                OpenRegion(top, geo, area.X);
                EnterColumn();
                if (type == SectionBreakType.NextColumn)
                {
                    _forceBreakBeforeNext = true;
                    _forceIsColumn = true;
                }
            }

            if (Stopped)
            {
                return;
            }

            var blocks = new List<FormattedParagraph>();
            Collect(section.Blocks(), blocks, ct);
            AppendEndnotes(section, blocks);
            if (balance && !probe && _cols.Length > 1 && blocks.Count > 0)
            {
                // ECMA-376 Part 1 §17.6.22: a continuous break after a multi-column section balances that section's columns, so the
                // text is spread as evenly as the lines allow.
                double h = Balance(blocks);
                if (h > 0)
                {
                    SetBase(_regionTop + h);
                }
            }

            for (int i = 0; i < blocks.Count && !Stopped; i++)
            {
                ct.ThrowIfCancellationRequested();
                Place(blocks, i);
            }

            SetBase(_bodyBottom);
        }

        // ---- pages ----

        private void StartPage(bool blank = false)
        {
            if (probe)
            {
                _balanceOverflow = true;
                Stopped = true;
                return;
            }

            if (_pages.Count >= options.MaxPages)
            {
                Stopped = true;
                ctx.NoteOnce("layout.page-limit", "layout.page-limit", "Layout stopped at the page limit.", null);
                return;
            }

            FinalizePage();
            SectionGeometry geo = _geo ?? SectionGeometry.Default();
            bool first = _sectionStartPending && !blank;
            long printed = _printedLast + 1;
            if (first && geo.Source?.PageNumbering.Start is { } start)
            {
                // ECMA-376 Part 1 §17.6.12 (pgNumType start): the section's first page prints this number.
                printed = start;
            }

            _printedLast = printed;
            if (first)
            {
                _sectionStartPending = false;
            }

            LayoutRect area = geo.TextArea(printed);
            var page = new LayoutPage(_pages.Count + 1, _sectionIndex, geo.Width, geo.Height, area)
            {
                PrintedNumber = printed,
                PrintedNumberText = NumberFormatter.Format(geo.Source?.PageNumbering.Format, printed).Text,
                IsBlank = blank,
                IsFirstPageOfSection = first,
            };
            _pages.Add(page);
            _page = page;
            _needPage = false;
            _pageGeo = geo;
            _areaX = area.X;
            _regions = [];
            _region = null;
            _colNotes = null;

            (double top, double bottom) = LayStories(page, geo, area, printed, first);
            bottom = Math.Max(bottom, top + 12);
            page.ContentArea = new LayoutRect(area.X, top, area.Width, bottom - top);
            _bodyTop = top;
            _bodyBottom = bottom;
            _limitBottom = bottom;
            _baseBottom = bottom;
            _cols = geo.Columns;
            _colIndex = 0;
            OpenRegion(top, geo, area.X);
            EnterColumn();
            _honourSpaceBefore = _pages.Count == 1;
            AddBorders(page, geo, area, first);
            if (geo.Source?.LineNumbering is { Restart: "newPage" } ln)
            {
                _lineCounter = ln.Start - 1;
            }
        }

        private void OpenRegion(double top, SectionGeometry geo, double areaX)
        {
            if (_region is not null)
            {
                _region.MaxY = Math.Max(_region.MaxY, _regionMaxY);
            }

            _region = new RegionState(top, geo, areaX, _sectionIndex);
            _regions.Add(_region);
            _cols = geo.Columns;
            _colIndex = 0;
            _regionTop = top;
            _regionMaxY = top;
        }

        private void EnterColumn()
        {
            ColumnSlot c = _cols[_colIndex];
            _colX = _areaX + c.X;
            _textWidth = c.Width;
            _y = _regionTop;
            _pageHasContent = false;
            _pendingAfter = 0;
            NotesEnterColumn();
        }

        private void AdvanceColumnOrPage()
        {
            if (_colIndex + 1 < _cols.Length)
            {
                _colIndex++;
                EnterColumn();
            }
            else
            {
                StartPage();
            }
        }

        // Closes the open page: records its column regions, draws the column separators and applies the vertical alignment.
        private void FinalizePage()
        {
            if (_page is not { } page || probe || page.ColumnRegions.Count > 0)
            {
                return;
            }

            if (_region is not null)
            {
                _region.MaxY = Math.Max(_region.MaxY, _regionMaxY);
            }

            // Footnotes take the bottom of the page; the body regions end above them.
            EmitNotes(page);
            var list = new List<LayoutColumnRegion>();
            for (int r = 0; r < _regions.Count; r++)
            {
                RegionState reg = _regions[r];
                double bottom = r == _regions.Count - 1 ? Math.Max(_bodyBottom - page.NotesReservation, reg.MaxY) : reg.MaxY;
                var rects = new List<LayoutRect>(reg.Geo.Columns.Length);
                foreach (ColumnSlot c in reg.Geo.Columns)
                {
                    rects.Add(new LayoutRect(reg.AreaX + c.X, reg.Top, c.Width, Math.Max(0, bottom - reg.Top)));
                }

                list.Add(new LayoutColumnRegion(reg.Top, bottom, rects, reg.SectionIndex));
                if (reg.Geo.Separator && reg.MaxY > reg.Top)
                {
                    // ECMA-376 Part 1 §17.6.4 (cols sep): a line in the gap between neighbouring columns, as tall as the text.
                    var sorted = reg.Geo.Columns.OrderBy(static c => c.X).ToArray();
                    for (int i = 0; i + 1 < sorted.Length; i++)
                    {
                        double x = reg.AreaX + ((sorted[i].X + sorted[i].Width + sorted[i + 1].X) / 2);
                        page.Add(new LayoutDecoration(LayoutDecorationKind.ColumnSeparator) { Bounds = new LayoutRect(x - 0.25, reg.Top, 0.5, reg.MaxY - reg.Top), Color = 0 });
                    }
                }
            }

            page.ColumnRegions = list;
            ApplyVerticalAlignment(page);
        }

        // ECMA-376 Part 1 §17.6.23 (vAlign): centre or bottom-align the body text between the top and bottom margins. `both`
        // (spread the paragraphs) is reported and laid out at the top.
        private void ApplyVerticalAlignment(LayoutPage page)
        {
            string? v = _pageGeo?.VAlign;
            if (v is null or "top")
            {
                return;
            }

            if (v == "both")
            {
                ctx.NoteOnce("layout.valign-both", "layout.valign-both", "Justified vertical alignment is not implemented; the text is placed at the top.", null);
                return;
            }

            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (LayoutParagraph p in page.BodyParagraphs)
            {
                min = Math.Min(min, p.Bounds.Y);
                max = Math.Max(max, p.Bounds.Bottom);
            }

            if (min > max)
            {
                return;
            }

            LayoutRect a = page.ContentArea;
            double dy = v == "bottom" ? a.Bottom - max : ((a.Bottom - max) - (min - a.Y)) / 2;
            if (dy <= 0.01)
            {
                return;
            }

            foreach (LayoutNode n in page.Children)
            {
                if (n is LayoutParagraph { Story: LayoutStory.Body } || n is LayoutDecoration { DecorationKind: LayoutDecorationKind.LineNumber })
                {
                    Shift(n, dy);
                }
            }
        }

        private static void Shift(LayoutNode n, double dy)
        {
            n.Bounds = n.Bounds with { Y = n.Bounds.Y + dy };
            if (n is LayoutLine l)
            {
                l.Baseline += dy;
            }
            else if (n is LayoutDecoration d)
            {
                d.Baseline += dy;
            }

            foreach (LayoutNode c in n.Children)
            {
                Shift(c, dy);
            }
        }

        // ---- headers and footers ----

        // ECMA-376 Part 1 §17.10 (headers and footers), §17.6.11 (pgMar header, footer): the header's text starts `header` points from
        // the page top and the footer's ends `footer` points from the bottom. A taller story pushes the body edge in. Returns the body
        // top and bottom.
        private (double Top, double Bottom) LayStories(LayoutPage page, SectionGeometry geo, LayoutRect area, long printed, bool first)
        {
            double top = area.Y;
            double bottom = area.Bottom;
            Section? sec = geo.Source;
            if (sec is null)
            {
                return (top, bottom);
            }

            var state = new PageFieldState
            {
                Printed = printed,
                NumberFormat = sec.PageNumbering.Format,
                NumPages = totals?.NumPages ?? 0,
                SectionPages = totals is not null && sec.Index < totals.SectionPages.Length ? totals.SectionPages[sec.Index] : 0,
                SectionNumber = sec.Index + 1,
                Physical = page.Number,
                Totals = totals,
            };

            HeaderFooter? header = sec.ForPage(HeaderFooterKind.Header, first, printed).Story;
            if (header is not null)
            {
                List<FormattedParagraph> fps = FormatStory(header, state, area.Width);
                double y = EmitStory(fps, LayoutStory.Header, geo.Header, area.X);
                page.HeaderArea = new LayoutRect(area.X, geo.Header, area.Width, Math.Max(0, y - geo.Header));
                top = Math.Max(top, y);
                AddWatermark(page, header, geo, area);
            }

            HeaderFooter? footer = sec.ForPage(HeaderFooterKind.Footer, first, printed).Story;
            if (footer is not null)
            {
                List<FormattedParagraph> fps = FormatStory(footer, state, area.Width);
                double height = StoryHeight(fps);
                double footerTop = geo.Height - geo.Footer - height;
                EmitStory(fps, LayoutStory.Footer, footerTop, area.X);
                page.FooterArea = new LayoutRect(area.X, footerTop, area.Width, height);
                bottom = Math.Min(bottom, footerTop);
            }

            return (top, bottom);
        }

        private List<FormattedParagraph> FormatStory(HeaderFooter story, PageFieldState state, double width)
        {
            var list = new List<FormattedParagraph>();
            ctx.PageFields = state;
            try
            {
                CollectStory(story.Blocks(), list, width, 0);
            }
            finally
            {
                ctx.PageFields = null;
                ctx.RunText = null;
            }

            return list;
        }

        private void CollectStory(IEnumerable<BlockElement> elements, List<FormattedParagraph> into, double width, int depth)
        {
            foreach (BlockElement b in elements)
            {
                switch (b)
                {
                    case Paragraph p:
                        into.Add(ParagraphFormatter.Format(p, ctx, width));
                        break;
                    case BlockContentControl cc when depth < 16:
                        CollectStory(cc.Content.Blocks(), into, width, depth + 1);
                        break;
                    case Table:
                        ctx.NoteOnce("layout.table-skipped", "layout.table-skipped", "Tables are not laid out yet; their content is left out of the pages.", b);
                        break;
                }
            }
        }

        private static double StoryHeight(List<FormattedParagraph> fps)
        {
            double h = 0;
            double after = 0;
            for (int i = 0; i < fps.Count; i++)
            {
                h += (i == 0 ? fps[i].SpaceBefore : Math.Max(after, fps[i].SpaceBefore)) + Total(fps[i], 0, fps[i].Lines.Count);
                after = fps[i].SpaceAfter;
            }

            return h;
        }

        // Places a header or footer's paragraphs from `top` and returns the y below the last line. Reuses Emit, so the story's nodes are
        // built exactly as body paragraphs are; the flow's own state is put back afterwards.
        private double EmitStory(List<FormattedParagraph> fps, LayoutStory story, double top, double left)
        {
            (double y, bool hasContent, bool honour, double colX, LayoutStory st, bool force, int col, double maxY) =
                (_y, _pageHasContent, _honourSpaceBefore, _colX, _story, _forceBreakBeforeNext, _colIndex, _regionMaxY);
            _story = story;
            _colX = left;
            _y = top;
            _colIndex = 0;
            _honourSpaceBefore = true;
            double after = 0;
            for (int i = 0; i < fps.Count; i++)
            {
                FormattedParagraph fp = fps[i];
                double gap = i == 0 ? fp.SpaceBefore : Math.Max(after, fp.SpaceBefore);
                _pageHasContent = false;
                Emit(fp, 0, fp.Lines.Count, gap, true, true);
                after = fp.SpaceAfter;
            }

            double bottom = _y;
            (_y, _pageHasContent, _honourSpaceBefore, _colX, _story, _forceBreakBeforeNext, _colIndex, _regionMaxY) = (y, hasContent, honour, colX, st, force, col, maxY);
            return bottom;
        }

        // ---- decorations ----

        // ECMA-376 Part 1 §17.6.2 (pgBorders): offsetFrom text measures `space` points outward from the text area, offsetFrom page
        // inward from the page edge; display limits the borders to the first page or to the others.
        private void AddBorders(LayoutPage page, SectionGeometry geo, LayoutRect area, bool first)
        {
            PageBorders? b = geo.Borders;
            if (b is null || (b.Display == "firstPage" && !page.IsFirstPageOfSection) || (b.Display == "notFirstPage" && page.IsFirstPageOfSection))
            {
                return;
            }

            double Thickness(PageBorderSide s) => s.Style switch
            {
                "double" => s.Width * 3,
                "triple" or "thinThickSmallGap" or "thickThinSmallGap" => s.Width * 5,
                _ => s.Width,
            };

            double tT = b.Top is { } t0 ? Thickness(t0) : 0;
            double tB = b.Bottom is { } b0 ? Thickness(b0) : 0;
            double tL = b.Left is { } l0 ? Thickness(l0) : 0;
            double tR = b.Right is { } r0 ? Thickness(r0) : 0;
            double left;
            double right;
            double topEdge;
            double bottomEdge;
            if (b.FromPage)
            {
                left = (b.Left?.Space ?? 0);
                right = geo.Width - (b.Right?.Space ?? 0);
                topEdge = b.Top?.Space ?? 0;
                bottomEdge = geo.Height - (b.Bottom?.Space ?? 0);
            }
            else
            {
                left = area.X - (b.Left?.Space ?? 0) - tL;
                right = area.Right + (b.Right?.Space ?? 0) + tR;
                topEdge = area.Y - (b.Top?.Space ?? 0) - tT;
                bottomEdge = area.Bottom + (b.Bottom?.Space ?? 0) + tB;
            }

            left = Math.Max(0, left);
            topEdge = Math.Max(0, topEdge);
            right = Math.Min(geo.Width, right);
            bottomEdge = Math.Min(geo.Height, bottomEdge);
            if (b.Top is { } top)
            {
                AddBorderSide(page, top, new LayoutRect(left, topEdge, right - left, tT));
            }

            if (b.Bottom is { } bottom)
            {
                AddBorderSide(page, bottom, new LayoutRect(left, bottomEdge - tB, right - left, tB));
            }

            if (b.Left is { } l)
            {
                AddBorderSide(page, l, new LayoutRect(left, topEdge, tL, bottomEdge - topEdge));
            }

            if (b.Right is { } r)
            {
                AddBorderSide(page, r, new LayoutRect(right - tR, topEdge, tR, bottomEdge - topEdge));
            }
        }

        private void AddBorderSide(LayoutPage page, PageBorderSide side, LayoutRect rect)
        {
            if (side.Art)
            {
                ctx.NoteOnce("layout.page-border-art", "layout.page-border-art", "Art page borders are drawn as plain lines.", null);
            }

            page.Add(new LayoutDecoration(LayoutDecorationKind.PageBorder) { Bounds = rect, Style = side.Art ? "single" : side.Style, Color = side.Color });
        }

        // A text watermark in the header (see Watermarks): one diagonal string centred in the text area, behind the text.
        private void AddWatermark(LayoutPage page, HeaderFooter header, SectionGeometry geo, LayoutRect area)
        {
            foreach (BlockElement b in header.Blocks())
            {
                if (b is not Paragraph p)
                {
                    continue;
                }

                if (FindWatermark(p.Markup.Node, 0) is not { } spec)
                {
                    continue;
                }

                FontMatch? font = ctx.ResolveFont(spec.Family, spec.Bold, spec.Italic);
                if (font is null || DecorationText.Shape(spec.Text, font.Face, 100) is not { } probeShape || probeShape.Width <= 0)
                {
                    return;
                }

                // The shape's width is the text's width: the text path is scaled to fill it.
                double size = Math.Clamp(100 * spec.ShapeWidth / probeShape.Width, 6, 600);
                (GlyphData g, double w) = DecorationText.Shape(spec.Text, font.Face, size)!.Value;
                double cx = area.X + (area.Width / 2);
                double cy = area.Y + (area.Height / 2);
                page.Add(new LayoutDecoration(LayoutDecorationKind.Watermark)
                {
                    Bounds = new LayoutRect(cx - (w / 2), cy - (size / 2), w, size),
                    Text = spec.Text,
                    Baseline = cy + (size * 0.35),
                    Rotation = spec.Rotation,
                    FontSize = size,
                    Color = spec.Color,
                    Opacity = spec.Opacity,
                    Face = font.Face,
                    Glyphs = g,
                });
                _ = geo;
                return;
            }
        }

        private static WatermarkSpec? FindWatermark(MarkupNode n, int depth)
        {
            if (depth > 8)
            {
                return null;
            }

            for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind != MarkupNodeKind.Element)
                {
                    continue;
                }

                if (Xml.Eq(c.LocalName, "pict"))
                {
                    if (Watermarks.Read(c) is { } spec)
                    {
                        return spec;
                    }
                }
                else if (FindWatermark(c, depth + 1) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        // ---- line numbers and the page grid ----

        private void ResetLineNumbers(SectionGeometry geo, bool firstOfDocument)
        {
            if (geo.Source?.LineNumbering is not { } ln)
            {
                return;
            }

            // ECMA-376 Part 1 §17.6.8 (lnNumType restart): numbering restarts on each page or section, or runs on.
            if (ln.Restart != "continuous" || firstOfDocument || !_lineNumbersStarted)
            {
                _lineCounter = ln.Start - 1;
            }

            _lineNumbersStarted = true;
        }

        private void NumberLine(FormattedParagraph fp, FormattedLine fl, LayoutLine line, double y, double left)
        {
            if (probe || _geo?.Source?.LineNumbering is not { } ln || ln.CountBy <= 0)
            {
                return;
            }

            if (fp.Source.ResolvedProperties.Flag("suppressLineNumbers") == true)
            {
                return;
            }

            _lineCounter++;
            if (_lineCounter % ln.CountBy != 0)
            {
                return;
            }

            foreach (LineItem item in fl.Items)
            {
                if (item.Font is not null && item.Kind == LayoutRunKind.Text)
                {
                    _numberFont = item.Font;
                    _numberSize = item.Size;
                    break;
                }
            }

            _numberFont ??= ctx.ResolveFont("Liberation Sans", false, false);
            if (_numberFont is null || DecorationText.Shape(_lineCounter.ToString(System.Globalization.CultureInfo.InvariantCulture), _numberFont.Face, _numberSize) is not { } shaped)
            {
                return;
            }

            // The number sits `distance` points left of the text; 0 means automatic, which is a quarter inch here.
            double distance = ln.Distance > 0 ? ln.Distance / 20.0 : 18;
            double x = left - distance - shaped.Width;
            _page!.Add(new LayoutDecoration(LayoutDecorationKind.LineNumber)
            {
                Bounds = new LayoutRect(x, y, shaped.Width, fl.Height),
                Text = _lineCounter.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Baseline = y + fl.Ascent,
                FontSize = _numberSize,
                Face = _numberFont.Face,
                Glyphs = shaped.Glyphs,
            });
            _ = line;
        }

        // ECMA-376 Part 1 §17.6.5 (docGrid): with a line grid each line takes a whole number of grid pitches. The extra goes above the
        // text. Paragraphs can opt out with snapToGrid off.
        private void SnapToGrid(FormattedParagraph fp)
        {
            double pitch = _geo?.GridPitch ?? 0;
            if (pitch <= 0 || fp.Source.ResolvedProperties.Flag("snapToGrid") == false)
            {
                return;
            }

            foreach (FormattedLine l in fp.Lines)
            {
                double h = l.Height;
                double snapped = Math.Max(1, Math.Ceiling((h / pitch) - 1e-6)) * pitch;
                l.Ascent += snapped - h;
            }
        }

        // ---- balanced columns ----

        // Finds the shortest column height at which the section's text still fits its columns, by trying the real placement in a scratch
        // flow. Returns -1 when the text does not fit the page remainder anyway.
        private double Balance(List<FormattedParagraph> blocks)
        {
            double avail = _bodyBottom - _regionTop;
            double total = 0;
            foreach (FormattedParagraph fp in blocks)
            {
                total += fp.SpaceBefore + Total(fp, 0, fp.Lines.Count) + fp.SpaceAfter;
            }

            if (avail <= 0 || total <= 0 || !Fits(blocks, avail))
            {
                return -1;
            }

            double lo = total / _cols.Length;
            double hi = avail;
            for (int i = 0; i < 16 && hi - lo > 0.1; i++)
            {
                double mid = (lo + hi) / 2;
                if (Fits(blocks, mid))
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            return hi + 0.02;
        }

        private bool Fits(List<FormattedParagraph> blocks, double height)
        {
            var scratch = new Flow(ctx, options, totals, probe: true);
            scratch.SetUpProbe(_geo!, _cols, height);
            var copy = new List<FormattedParagraph>(blocks);
            for (int i = 0; i < copy.Count && !scratch.Stopped; i++)
            {
                scratch.Place(copy, i);
            }

            return !scratch.Overflowed;
        }

        private void SetUpProbe(SectionGeometry geo, ColumnSlot[] cols, double height)
        {
            _geo = geo;
            _pageGeo = geo;
            _cols = cols;
            _colIndex = 0;
            _areaX = 0;
            _colX = 0;
            _regionTop = 0;
            _y = 0;
            _bodyTop = 0;
            _bodyBottom = height;
            _limitBottom = height;
            _textWidth = cols[0].Width;
            _honourSpaceBefore = true;
            _page = new LayoutPage(1, _sectionIndex, geo.Width, geo.Height, new LayoutRect(0, 0, cols[0].Width, height));
            _pages.Add(_page);
        }
    }
}
