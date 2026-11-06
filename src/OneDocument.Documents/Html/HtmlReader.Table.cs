using System.Linq;
using System.Globalization;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using OneHtmlNode = RecursiveAI.OneHtml.Node;

namespace RecursiveAI.OneDocument.Documents.Html;

internal sealed partial class HtmlReader
{
    private const int MaxSpan = 1000;
    private const int MaxTableCells = 200_000;

    private readonly record struct Extent(double Value, bool Percent);

    private sealed class PendingCell(HCell cell, OneHtmlNode node, int column)
    {
        public HCell Cell { get; } = cell;

        public OneHtmlNode Node { get; } = node;

        public int Column { get; } = column;
    }

    private int _tableCells;

    private static int ParseSpan(string? v)
    {
        return int.TryParse(v?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Clamp(n, 1, MaxSpan) : 1;
    }

    // An element's width or height as a twip count or a percentage, from the attribute and then the style.
    private static Extent? Length(OneHtmlNode e, ComputedStyle s, string name)
    {
        string? a = Attr(e, name)?.Trim();
        double em = HtmlCss.FontPoints(s);
        if (!string.IsNullOrEmpty(a))
        {
            if (a.EndsWith('%'))
            {
                if (double.TryParse(a[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double p) && p > 0 && p <= 1000)
                {
                    return new Extent(p, true);
                }
            }
            else if (double.TryParse(a.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out double px) && px > 0 && px < 100000)
            {
                return new Extent(px * 15, false);
            }
        }

        if (s.IsDeclared(name))
        {
            string v = Prop(s, name);
            if (v.EndsWith('%'))
            {
                if (double.TryParse(v[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double p) && p > 0 && p <= 1000)
                {
                    return new Extent(p, true);
                }
            }
            else if (HtmlCss.Twips(v, em) is int tw && tw > 0)
            {
                return new Extent(tw, false);
            }
        }

        return null;
    }

    private void Table(OneHtmlNode t, ComputedStyle ts, Format f)
    {
        _ct.ThrowIfCancellationRequested();
        FlushPara();
        var table = new HTable();
        var rows = new List<(OneHtmlNode Row, bool Head, bool Foot)>();
        var cols = new List<Extent?>();
        OneHtmlNode caption = default;
        foreach (OneHtmlNode c in t.Children)
        {
            switch (c.LocalName)
            {
                case "caption":
                    if (caption.IsNull)
                    {
                        caption = c;
                    }

                    break;
                case "thead" or "tbody" or "tfoot":
                    foreach (OneHtmlNode r in c.Children)
                    {
                        if (r.LocalName == "tr")
                        {
                            rows.Add((r, c.LocalName == "thead", c.LocalName == "tfoot"));
                        }
                    }

                    break;
                case "tr":
                    rows.Add((c, false, false));
                    break;
                case "colgroup":
                    {
                        bool any = false;
                        foreach (OneHtmlNode col in c.Children)
                        {
                            if (col.LocalName == "col")
                            {
                                any = true;
                                AddCol(cols, col);
                            }
                        }

                        if (!any)
                        {
                            AddCol(cols, c);
                        }

                        break;
                    }

                case "col":
                    AddCol(cols, c);
                    break;
            }
        }

        // The foot is drawn last whatever its place in the source.
        rows = [.. rows.Where(static r => !r.Foot), .. rows.Where(static r => r.Foot)];

        int borderAttr = int.TryParse(Attr(t, "border"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) ? Math.Clamp(b, 0, 20) : 0;
        string? tableBg = OwnBackground(ts) ?? HtmlCss.Color(Attr(t, "bgcolor"));
        table.Shade = tableBg;
        table.Rtl = ts.IsRightToLeft;
        table.Border = borderAttr > 0 ? new HBorder("single", Math.Max(2, borderAttr * 6), null) : BorderOf(ts, "top");
        string? al = Attr(t, "align")?.Trim().ToLowerInvariant();
        string ml = Prop(ts, "margin-left");
        string mr = Prop(ts, "margin-right");
        table.Align = al is "center" or "right" ? al : ml == "auto" && mr == "auto" ? "center" : ml == "auto" ? "right" : null;
        if (table.Align is null && ml != "auto")
        {
            table.IndentTwips = _indentLeft + Margin(ts, "margin-left", HtmlCss.FontPoints(ts));
        }

        // ---- place the cells on a grid (ECMA-376 Part 1 §17.4: vMerge continues a cell down; gridSpan widens it)
        var pending = new List<PendingCell>();
        var active = new SortedDictionary<int, (int Remaining, int Span)>();
        int maxCols = 0;
        foreach ((OneHtmlNode rowNode, bool head, _) in rows)
        {
            ComputedStyle rs = _styles.GetStyle(rowNode);
            var row = new HRow();
            string? rowBg = OwnBackground(rs) ?? HtmlCss.Color(Attr(rowNode, "bgcolor"));
            if (Length(rowNode, rs, "height") is { Percent: false } rh)
            {
                row.HeightTwips = (int)Math.Min(rh.Value, 31680);
            }

            var cellNodes = rowNode.Children.Where(static c => c.LocalName is "td" or "th").ToList();
            int col = 0;
            int ci = 0;
            while (true)
            {
                if (active.TryGetValue(col, out var a) && a.Remaining > 0)
                {
                    row.Cells.Add(new HCell { Continuation = true, ColSpan = a.Span });
                    if (a.Remaining == 1)
                    {
                        active.Remove(col);
                    }
                    else
                    {
                        active[col] = (a.Remaining - 1, a.Span);
                    }

                    col += a.Span;
                    continue;
                }

                if (ci >= cellNodes.Count)
                {
                    // Cells that a row above spans still continue in this row even after the last cell of its own.
                    int next = active.Keys.Where(k => k >= col).DefaultIfEmpty(-1).Min();
                    if (next >= 0)
                    {
                        col = next;
                        continue;
                    }

                    break;
                }

                if (++_tableCells > MaxTableCells)
                {
                    Lose("loss.html-table-size", "very large tables", "Cells past the first " + MaxTableCells.ToString(CultureInfo.InvariantCulture) + " were left out.", t);
                    cellNodes.Clear();
                    ci = 0;
                    continue;
                }

                OneHtmlNode cn = cellNodes[ci++];
                ComputedStyle cs = _styles.GetStyle(cn);
                var cell = new HCell
                {
                    ColSpan = ParseSpan(Attr(cn, "colspan")),
                    RowSpan = ParseSpan(Attr(cn, "rowspan")),
                    Header = cn.LocalName == "th",
                    Shade = OwnBackground(cs) ?? HtmlCss.Color(Attr(cn, "bgcolor")) ?? rowBg,
                };
                double em = HtmlCss.FontPoints(cs);
                cell.BorderTop = BorderOf(cs, "top") ?? DefaultCellBorder(borderAttr);
                cell.BorderRight = BorderOf(cs, "right") ?? DefaultCellBorder(borderAttr);
                cell.BorderBottom = BorderOf(cs, "bottom") ?? DefaultCellBorder(borderAttr);
                cell.BorderLeft = BorderOf(cs, "left") ?? DefaultCellBorder(borderAttr);
                string va = (Attr(cn, "valign") ?? Prop(cs, "vertical-align")).ToLowerInvariant();
                cell.VAlign = va switch { "top" => "top", "bottom" => "bottom", _ => null };
                cell.PadTop = Math.Max(0, HtmlCss.Twips(Prop(cs, "padding-top"), em) ?? 0);
                cell.PadRight = Math.Max(0, HtmlCss.Twips(Prop(cs, "padding-right"), em) ?? 0);
                cell.PadBottom = Math.Max(0, HtmlCss.Twips(Prop(cs, "padding-bottom"), em) ?? 0);
                cell.PadLeft = Math.Max(0, HtmlCss.Twips(Prop(cs, "padding-left"), em) ?? 0);
                if (cell.RowSpan > 1)
                {
                    active[col] = (cell.RowSpan - 1, cell.ColSpan);
                }

                pending.Add(new PendingCell(cell, cn, col));
                if (Length(cn, cs, "width") is { } w && cell.ColSpan == 1)
                {
                    while (cols.Count <= col)
                    {
                        cols.Add(null);
                    }

                    cols[col] ??= w;
                }

                row.Cells.Add(cell);
                col += cell.ColSpan;
            }

            row.Header = head;
            maxCols = Math.Max(maxCols, col);
            table.Rows.Add(row);
        }

        // Header rows: the thead rows, or leading rows made only of th cells.
        for (int i = 0; i < table.Rows.Count; i++)
        {
            HRow r = table.Rows[i];
            if (r.Header)
            {
                continue;
            }

            if (i == table.Rows.Count - 1 && table.Rows.Count > 1 || r.Cells.Count == 0 || !r.Cells.All(static c => c.Header || c.Continuation))
            {
                break;
            }

            r.Header = table.Rows.Take(i).All(static x => x.Header);
            if (!r.Header)
            {
                break;
            }
        }

        if (maxCols == 0)
        {
            return;
        }

        // ---- column widths
        int avail = Math.Max(720, _avail - _indentLeft - _indentRight);
        Extent? tw = Length(t, ts, "width");
        int specified = tw is null ? 0 : tw.Value.Percent ? (int)Math.Min(avail * tw.Value.Value / 100, 31680) : (int)Math.Min(tw.Value.Value, 31680);
        int[] grid = new int[maxCols];
        int known = 0;
        int unknown = 0;
        int totalHint = specified > 0 ? specified : avail;
        for (int c = 0; c < maxCols; c++)
        {
            Extent? e = c < cols.Count ? cols[c] : null;
            if (e is { } ex)
            {
                grid[c] = Math.Max(240, ex.Percent ? (int)(totalHint * ex.Value / 100) : (int)ex.Value);
                known += grid[c];
            }
            else
            {
                unknown++;
            }
        }

        int total = specified > 0 ? specified : unknown == 0 ? known : avail;
        if (unknown > 0)
        {
            int each = Math.Max(240, (total - known) / unknown);
            for (int c = 0; c < maxCols; c++)
            {
                if (grid[c] == 0)
                {
                    grid[c] = each;
                }
            }
        }

        int sum = grid.Sum();
        if ((specified > 0 || unknown > 0) && sum != total && sum > 0 && total > 0)
        {
            // Scale to the table width so the grid and the width agree (ECMA-376 Part 1 §17.4.64 tblGrid, §17.4.63 tblW).
            double k = (double)total / sum;
            for (int c = 0; c < maxCols; c++)
            {
                grid[c] = Math.Max(120, (int)Math.Round(grid[c] * k));
            }
        }

        table.Grid.AddRange(grid);
        if (tw is not null)
        {
            if (tw.Value.Percent)
            {
                table.WidthPercent50 = (int)Math.Min(tw.Value.Value * 50, 5000);
            }
            else
            {
                table.WidthTwips = grid.Sum();
            }
        }

        // ---- cell contents
        OneHtmlNode savedSource = _source;
        List<HBlock> savedTarget = _target;
        int savedL = _indentLeft;
        int savedR = _indentRight;
        int savedGap = _gap;
        int savedAvail = _avail;
        HList? savedList = _liList;
        bool savedUsed = _liUsed;
        bool savedBreak = _pageBreakNext;
        _liList = null;
        _pageBreakNext = false;
        foreach (PendingCell pc in pending)
        {
            int cw = 0;
            for (int c = pc.Column; c < pc.Column + pc.Cell.ColSpan && c < maxCols; c++)
            {
                cw += grid[c];
            }

            pc.Cell.WidthTwips = cw;
            _target = pc.Cell.Blocks;
            _indentLeft = 0;
            _indentRight = 0;
            _gap = 0;
            _avail = Math.Max(240, cw - Math.Max(0, pc.Cell.PadLeft) - Math.Max(0, pc.Cell.PadRight));
            _source = pc.Node;
            PlaceAnchor(pc.Node);
            ComputedStyle cs = _styles.GetStyle(pc.Node);
            (bool u, bool st) = Decoration(cs);
            Walk(pc.Node, f with { Underline = f.Underline || u, Strike = f.Strike || st, Lang = NormalLang(pc.Node, f.Lang) });
            FlushPara();
            FlushPendingBookmarks();
            if (pc.Cell.Blocks.Count == 0 || pc.Cell.Blocks[^1] is HTable)
            {
                pc.Cell.Blocks.Add(new HPara());
            }
        }

        _target = savedTarget;
        _source = savedSource;
        _indentLeft = savedL;
        _indentRight = savedR;
        _avail = savedAvail;
        _gap = savedGap;
        _liList = savedList;
        _liUsed = savedUsed;
        _pageBreakNext = savedBreak;

        if (!caption.IsNull)
        {
            BlockElement(caption, _styles.GetStyle(caption), f, "table-caption");
            FlushPara();
        }

        PlaceAnchor(t);
        FlushPendingBookmarksBeforeTable();
        _target.Add(table);
        _gap = 0;
    }

    private void FlushPendingBookmarksBeforeTable() => FlushPendingBookmarks();

    private static HBorder? DefaultCellBorder(int borderAttr) => borderAttr > 0 ? new HBorder("single", 6, null) : null;

    private static void AddCol(List<Extent?> cols, OneHtmlNode col)
    {
        int span = ParseSpan(Attr(col, "span"));
        Extent? w = null;
        string? a = Attr(col, "width")?.Trim();
        if (!string.IsNullOrEmpty(a))
        {
            if (a.EndsWith('%'))
            {
                if (double.TryParse(a[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double p) && p > 0 && p <= 1000)
                {
                    w = new Extent(p, true);
                }
            }
            else if (double.TryParse(a.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out double px) && px > 0 && px < 100000)
            {
                w = new Extent(px * 15, false);
            }
        }

        for (int i = 0; i < span && cols.Count < 4096; i++)
        {
            cols.Add(w);
        }
    }
}
