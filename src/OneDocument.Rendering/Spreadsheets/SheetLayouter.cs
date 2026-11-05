using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>Splits the print areas of sheets into pages: scale, automatic and manual breaks, repeated titles, page order, centring.</summary>
internal static class SheetLayouter
{
    private const double Epsilon = 1e-6;

    public static SheetLayoutDocument Layout(Workbook workbook, SheetRenderOptions options, CancellationToken cancellationToken)
    {
        IFontProvider fonts = options.Fonts ?? FontLibrary.CreateDefault();
        var ctx = new RenderContext(workbook, options, fonts);
        var pages = new List<SheetPage>();
        int running = 0;
        for (int i = 0; i < workbook.Sheets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Worksheet sheet = workbook.Sheets[i];
            if (sheet.State != SheetState.Visible && !options.IncludeHiddenSheets)
            {
                continue;
            }

            if (sheet.Kind != SheetKind.Worksheet)
            {
                ctx.Note("sheet-layout.sheet-kind-skipped", "A " + sheet.Kind + " sheet is not printed.", sheet.Name);
                continue;
            }

            SheetPlan? plan = SheetPlan.Create(ctx, sheet, i);
            if (plan is null)
            {
                continue;
            }

            SheetPrintSettings s = plan.Settings;
            int first = s.UseFirstPageNumber ? s.FirstPageNumber : running + 1;
            int inSheet = 0;
            double scale = ChooseScale(plan);
            for (int a = 0; a < plan.Areas.Count; a++)
            {
                foreach (SheetPage page in PagesOfArea(plan, a, scale, i))
                {
                    if (pages.Count >= options.MaxPages)
                    {
                        ctx.Note("sheet-layout.page-limit", "Layout stopped at the page limit.", sheet.Name);
                        goto done;
                    }

                    page.Index = pages.Count;
                    page.NumberInSheet = ++inSheet;
                    page.Number = first + inSheet - 1;
                    pages.Add(page);
                }
            }

            running = pages.Count == 0 ? running : Math.Max(running, first + inSheet - 1);
        }

    done:
        ctx.TotalPages = pages.Count == 0 ? 0 : Math.Max(pages.Count, pages.Max(p => p.Number));
        foreach (string missing in ctx.Text.Missing.Distinct())
        {
            ctx.Note("sheet-layout.font-missing", "No font is available for \"" + missing + "\"; its text is not drawn.");
        }

        return new SheetLayoutDocument(workbook, ctx, pages);
    }

    // The scale of the sheet: the page setup's, or for fit-to-page the largest whole percent (at most 100) at which every area fits.
    private static double ChooseScale(SheetPlan plan)
    {
        SheetPrintSettings s = plan.Settings;
        if (!s.FitToPage)
        {
            return s.Scale / 100.0;
        }

        int best = 100;
        for (int a = 0; a < plan.Areas.Count; a++)
        {
            int lo = 10;
            int hi = 100;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Fits(plan, a, mid / 100.0))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            best = Math.Min(best, lo);
        }

        return best / 100.0;
    }

    private static bool Fits(SheetPlan plan, int area, double scale)
    {
        SheetPrintSettings s = plan.Settings;
        (List<(int, int)> rows, List<(int, int)> cols) = Split(plan, area, scale, honorBreaks: false);
        return (s.FitToWidth == 0 || cols.Count <= s.FitToWidth) && (s.FitToHeight == 0 || rows.Count <= s.FitToHeight);
    }

    private static (List<(int First, int Last)> Rows, List<(int First, int Last)> Cols) Split(SheetPlan plan, int area, double scale, bool honorBreaks)
    {
        SheetPrintSettings s = plan.Settings;
        CellRange r = plan.Areas[area];
        SheetGeometry g = plan.Geo;
        double availW = ((s.PageWidth - s.MarginLeft - s.MarginRight) / scale) - plan.HeadingWidth;
        double availH = ((s.PageHeight - s.MarginTop - s.MarginBottom) / scale) - plan.HeadingHeight;
        List<(int, int)> rows = SplitAxis(r.TopLeft.Row, r.BottomRight.Row, row => g.RowHeight(row), availH, s.TitleRows, honorBreaks ? s.RowBreaks : []);
        List<(int, int)> cols = SplitAxis(r.TopLeft.Column, r.BottomRight.Column, col => g.ColWidth(col), availW, s.TitleColumns, honorBreaks ? s.ColumnBreaks : []);
        return (rows, cols);
    }

    // Packs rows (or columns) into pages. A page holds at least one line; titles take room on every page that starts below them.
    private static List<(int First, int Last)> SplitAxis(int first, int last, Func<int, double> size, double avail, (int First, int Last) titles, IReadOnlyList<int> breaks)
    {
        var pages = new List<(int, int)>();
        double titleSize = 0;
        if (titles.First > 0)
        {
            for (int i = titles.First; i <= titles.Last; i++)
            {
                titleSize += size(i);
            }
        }

        var breakSet = new HashSet<int>(breaks);
        int start = first;
        while (start <= last)
        {
            double cap = avail - (titles.First > 0 && start > titles.Last ? titleSize : 0);
            double used = 0;
            int end = start;
            while (end <= last)
            {
                double sz = size(end);
                if (end > start && used + sz > cap + Epsilon)
                {
                    break;
                }

                used += sz;
                end++;
                if (breakSet.Contains(end - 1))
                {
                    break;
                }
            }

            double total = 0;
            for (int i = start; i < end; i++)
            {
                total += size(i);
            }

            // A page of only hidden lines prints nothing.
            if (total > 0 || end - start == 0)
            {
                pages.Add((start, end - 1));
            }

            start = end;
        }

        return pages;
    }

    private static IEnumerable<SheetPage> PagesOfArea(SheetPlan plan, int area, double scale, int sheetIndex)
    {
        SheetPrintSettings s = plan.Settings;
        SheetGeometry g = plan.Geo;
        (List<(int First, int Last)> rows, List<(int First, int Last)> cols) = Split(plan, area, scale, honorBreaks: !s.FitToPage);
        var grid = new List<(int RowPage, int ColPage)>();
        if (s.PageOrder == SheetPageOrder.DownThenOver)
        {
            for (int c = 0; c < cols.Count; c++)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    grid.Add((r, c));
                }
            }
        }
        else
        {
            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < cols.Count; c++)
                {
                    grid.Add((r, c));
                }
            }
        }

        foreach ((int rp, int cp) in grid)
        {
            (int r1, int r2) = rows[rp];
            (int c1, int c2) = cols[cp];
            (int First, int Last) tr = s.TitleRows.First > 0 && r1 > s.TitleRows.Last ? s.TitleRows : default;
            (int First, int Last) tc = s.TitleColumns.First > 0 && c1 > s.TitleColumns.Last ? s.TitleColumns : default;
            double w = g.ColsWidth(c1, c2) + (tc.First > 0 ? g.ColsWidth(tc.First, tc.Last) : 0);
            double h = g.RowsHeight(r1, r2) + (tr.First > 0 ? g.RowsHeight(tr.First, tr.Last) : 0);
            double totalW = (w + plan.HeadingWidth) * scale;
            double totalH = (h + plan.HeadingHeight) * scale;
            double areaW = s.PageWidth - s.MarginLeft - s.MarginRight;
            double areaH = s.PageHeight - s.MarginTop - s.MarginBottom;
            double x0 = s.MarginLeft + (s.CenterHorizontally ? Math.Max(0, (areaW - totalW) / 2) : 0);
            double y0 = s.MarginTop + (s.CenterVertically ? Math.Max(0, (areaH - totalH) / 2) : 0);
            var page = new SheetPage(plan, sheetIndex)
            {
                Width = s.PageWidth,
                Height = s.PageHeight,
                Scale = scale,
                Body = new CellRange(new CellAddress(r1, c1), new CellAddress(r2, c2)),
                TitleRows = tr,
                TitleColumns = tc,
                ContentX = x0 + (plan.HeadingWidth * scale),
                ContentY = y0 + (plan.HeadingHeight * scale),
                ContentWidth = w * scale,
                ContentHeight = h * scale,
                AreaIndex = area,
            };
            page.Objects = ObjectsOnPage(plan, page, tr, tc);
            yield return page;
        }
    }

    private static List<SheetObject> ObjectsOnPage(SheetPlan plan, SheetPage page, (int First, int Last) tr, (int First, int Last) tc)
    {
        var list = new List<SheetObject>();
        SheetGeometry g = plan.Geo;
        CellRange b = page.Body;
        double left = g.ColLeft(b.TopLeft.Column);
        double right = g.ColLeft(b.BottomRight.Column + 1);
        double top = g.RowTop(b.TopLeft.Row);
        double bottom = g.RowTop(b.BottomRight.Row + 1);
        double titleW = tc.First > 0 ? g.ColsWidth(tc.First, tc.Last) : 0;
        double titleH = tr.First > 0 ? g.RowsHeight(tr.First, tr.Last) : 0;
        foreach (Anchor a in plan.Anchors)
        {
            if (plan.AnchorBox(a) is not { } box || box.X + box.W <= left || box.X >= right || box.Y + box.H <= top || box.Y >= bottom)
            {
                continue;
            }

            double x = page.ContentX + ((titleW + box.X - left) * page.Scale);
            double y = page.ContentY + ((titleH + box.Y - top) * page.Scale);
            list.Add(new SheetObject(a.Kind, a.Name, x, y, box.W * page.Scale, box.H * page.Scale, a.Node, a.DrawingPart));
        }

        return list;
    }
}
