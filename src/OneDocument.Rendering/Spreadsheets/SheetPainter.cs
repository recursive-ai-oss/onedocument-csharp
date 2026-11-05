using BorderSide = RecursiveAI.OneDocument.Spreadsheets.BorderSide;
using System.Globalization;
using System.Numerics;
using OneFont;
using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>
/// Paints one printed page into a display list. All coordinates are points: the list is in CSS pixels, so the first thing pushed is a
/// transform from points to pixels, and the cells sit under a second transform that applies the page scale.
/// </summary>
internal sealed class SheetPainter
{
    private const double Px = 0.75;        // one pixel at 96 dpi, in points
    private const double Pad = 1.5;        // horizontal text padding inside a cell: 2 px
    private const double VPad = 0.75;      // vertical padding: 1 px
    private const double K = 96.0 / 72.0;

    private readonly RenderContext _ctx;
    private readonly SheetPage _page;
    private readonly SheetPlan _plan;
    private readonly SheetGeometry _geo;
    private readonly SheetPrintSettings _s;
    private readonly DisplayList _list = new();
    private readonly CellStore _store;
    private readonly List<int> _rows = [];
    private readonly List<int> _cols = [];
    private readonly bool _bw;
    private uint _owner;
    private double _titleW;
    private double _titleH;
    private double _bodyLeft;
    private double _bodyTop;
    private ConditionalResults _cf = null!;

    private SheetPainter(SheetLayoutDocument doc, SheetPage page)
    {
        _ctx = doc.Context;
        _page = page;
        _plan = page.Plan;
        _geo = _plan.Geo;
        _s = _plan.Settings;
        _store = _plan.Sheet.Store;
        _bw = _s.BlackAndWhite;
        // The PDF writer treats an element with the same id as the one still open from the previous page as the same element (a table that
        // continues), so every page numbers its items from a base of its own.
        _owner = (uint)(page.Index % 4000) << 20;
    }

    public static DisplayList Paint(SheetLayoutDocument doc, SheetPage page)
    {
        var painter = new SheetPainter(doc, page);
        painter.Run();
        return painter._list;
    }

    private ItemId Id(ushort part = 0, ushort sub = 0) => ItemId.Of(++_owner, part, sub);

    private static RectF R(double x, double y, double w, double h) => new((float)x, (float)y, (float)w, (float)h);

    private PaintColor C(uint rgb) => StyleCache.Paint(_bw ? StyleCache.Gray(rgb) : rgb);

    private bool Tagged => _ctx.Options.Tagged;

    private void Run()
    {
        _plan.Conditional ??= ConditionalResults.Compute(_ctx, _plan);
        _cf = _plan.Conditional;
        SheetPage p = _page;
        (int tr1, int tr2) = p.TitleRows;
        (int tc1, int tc2) = p.TitleColumns;
        CellRange b = p.Body;
        for (int r = tr1; tr1 > 0 && r <= tr2; r++)
        {
            if (_geo.RowHeight(r) > 0)
            {
                _rows.Add(r);
            }
        }

        _titleH = tr1 > 0 ? _geo.RowsHeight(tr1, tr2) : 0;
        for (int r = b.TopLeft.Row; r <= b.BottomRight.Row; r++)
        {
            if (_geo.RowHeight(r) > 0)
            {
                _rows.Add(r);
            }
        }

        for (int c = tc1; tc1 > 0 && c <= tc2; c++)
        {
            if (_geo.ColWidth(c) > 0)
            {
                _cols.Add(c);
            }
        }

        _titleW = tc1 > 0 ? _geo.ColsWidth(tc1, tc2) : 0;
        for (int c = b.TopLeft.Column; c <= b.BottomRight.Column; c++)
        {
            if (_geo.ColWidth(c) > 0)
            {
                _cols.Add(c);
            }
        }

        _bodyLeft = _geo.ColLeft(b.TopLeft.Column);
        _bodyTop = _geo.RowTop(b.TopLeft.Row);

        _list.PushTransform(Id(), Matrix3x2.CreateScale((float)K));
        _list.FillRect(Id(), R(0, 0, p.Width, p.Height), PaintColor.White);

        double gridW = (_titleW + _geo.ColsWidth(b.TopLeft.Column, b.BottomRight.Column)) * p.Scale;
        double gridH = (_titleH + _geo.RowsHeight(b.TopLeft.Row, b.BottomRight.Row)) * p.Scale;
        double hw = _plan.HeadingWidth * p.Scale;
        double hh = _plan.HeadingHeight * p.Scale;
        _list.PushClipRect(Id(), R(p.ContentX - hw, p.ContentY - hh, gridW + hw, gridH + hh));
        _list.PushTransform(Id(), Matrix3x2.CreateScale((float)p.Scale) * Matrix3x2.CreateTranslation((float)p.ContentX, (float)p.ContentY));

        if (_s.PrintGridlines)
        {
            Gridlines();
        }

        Backgrounds();
        Borders();
        if (_s.PrintHeadings)
        {
            Headings();
        }

        Texts();
        _list.PopTransform();
        _list.PopClip();
        // Drawing painters work in pixels, so the points-to-pixels transform is off while they run.
        _list.PopTransform();
        PaintObjects();
        _list.PushTransform(Id(), Matrix3x2.CreateScale((float)K));
        HeaderAndFooter();
        _list.PopTransform();
        _list.Validate();
    }

    // ---- coordinates ----------------------------------------------------------------------------------------------------------

    private bool InTitleRows(int r) => _page.TitleRows.First > 0 && r >= _page.TitleRows.First && r <= _page.TitleRows.Last;

    private bool InTitleCols(int c) => _page.TitleColumns.First > 0 && c >= _page.TitleColumns.First && c <= _page.TitleColumns.Last;

    private double X(int c) => InTitleCols(c) ? _geo.ColLeft(c) - _geo.ColLeft(_page.TitleColumns.First) : _titleW + _geo.ColLeft(c) - _bodyLeft;

    private double Y(int r) => InTitleRows(r) ? _geo.RowTop(r) - _geo.RowTop(_page.TitleRows.First) : _titleH + _geo.RowTop(r) - _bodyTop;

    private (double X, double Y, double W, double H) Box(int r1, int c1, int r2, int c2)
    {
        double x1 = X(c1);
        double y1 = Y(r1);
        double x2 = X(c2) + _geo.ColWidth(c2);
        double y2 = Y(r2) + _geo.RowHeight(r2);
        return (x1, y1, x2 - x1, y2 - y1);
    }

    private (CellValue Value, int Style, bool Exists) Fetch(int r, int c)
    {
        if (_store.TryGetCell(r, c, out CellValue v, out int style))
        {
            return (v, style, true);
        }

        int rs = _geo.Covers(r, c) ? _plan.RowStyles[r - _geo.Row0] : 0;
        int cs = c >= _plan.ColumnStylesFirst && c - _plan.ColumnStylesFirst < _plan.ColumnStyles.Length ? _plan.ColumnStyles[c - _plan.ColumnStylesFirst] : 0;
        return (default, rs != 0 ? rs : cs, false);
    }

    // The distinct cells and merged ranges to paint: a merged range once, from its first cell on this page.
    private IEnumerable<(int Row, int Col, CellRange? Merge, int AnchorRow, int AnchorCol)> Items()
    {
        var done = new HashSet<long>();
        foreach (int r in _rows)
        {
            foreach (int c in _cols)
            {
                if (_plan.MergeCover.TryGetValue(SheetPlan.Key(r, c), out CellRange m))
                {
                    long mk = SheetPlan.Key(m.TopLeft.Row, m.TopLeft.Column);
                    if (done.Add(mk))
                    {
                        yield return (m.TopLeft.Row, m.TopLeft.Column, m, r, c);
                    }

                    continue;
                }

                yield return (r, c, null, r, c);
            }
        }
    }

    private (double X, double Y, double W, double H) ItemBox(int r, int c, CellRange? m) =>
        m is { } mr ? Box(mr.TopLeft.Row, mr.TopLeft.Column, mr.BottomRight.Row, mr.BottomRight.Column) : Box(r, c, r, c);

    // ---- gridlines, fills -----------------------------------------------------------------------------------------------------

    private void Gridlines()
    {
        PaintColor color = C(0x808080);
        double w = Px * 0.67;
        double totalW = _titleW + _geo.ColsWidth(_page.Body.TopLeft.Column, _page.Body.BottomRight.Column);
        double totalH = _titleH + _geo.RowsHeight(_page.Body.TopLeft.Row, _page.Body.BottomRight.Row);
        foreach (int r in _rows)
        {
            double y = Y(r) + _geo.RowHeight(r);
            _list.FillRect(Id(30), R(0, y - (w / 2), totalW, w), color);
        }

        foreach (int c in _cols)
        {
            double x = X(c) + _geo.ColWidth(c);
            _list.FillRect(Id(31), R(x - (w / 2), 0, w, totalH), color);
        }

        _list.FillRect(Id(30), R(0, -(w / 2), totalW, w), color);
        _list.FillRect(Id(31), R(-(w / 2), 0, w, totalH), color);
    }

    private void Backgrounds()
    {
        foreach ((int r, int c, CellRange? m, _, _) in Items())
        {
            (CellValue value, int styleIndex, bool exists) = Fetch(r, c);
            StyleInfo st = _ctx.Styles.Get(styleIndex);
            CfCell? cf = _cf.Get(r, c);
            (double x, double y, double w, double h) = ItemBox(r, c, m);
            uint? fill = cf?.ScaleColor ?? cf?.Fill ?? st.Fill;
            if (fill is { } f)
            {
                _list.FillRect(Id(20), R(x, y, w, h), C(f));
            }
            else if (st.Gradient is { } g && st.GradientColors.Length > 0)
            {
                GradientFill(x, y, w, h, g, st);
            }

            if (cf?.Bar is { } bar)
            {
                DataBar(x, y, w, h, bar);
            }

            if (cf?.Icon is { } icon)
            {
                double size = Math.Min(h - (2 * VPad), st.Text.Size * 1.0);
                IconPainter.Draw(_list, () => Id(22), icon.Set, icon.Level, x + Pad, y + ((h - size) / 2), size);
            }

            _ = value;
            _ = exists;
        }
    }

    private void GradientFill(double x, double y, double w, double h, GradientFill g, StyleInfo st)
    {
        var stops = new OneRender.Painting.GradientStop[g.Stops.Count];
        for (int i = 0; i < stops.Length; i++)
        {
            stops[i] = new OneRender.Painting.GradientStop((float)Math.Clamp(g.Stops[i].Position, 0, 1), C(st.GradientColors[i]));
        }

        if (g.IsPath)
        {
            _list.FillRect(Id(20), R(x, y, w, h), C(st.GradientColors[^1]));
            return;
        }

        // ECMA-376 Part 1 §18.8.24 (gradientFill): degree is the angle of the gradient line, 0 running left to right, increasing clockwise.
        double a = g.Degree * Math.PI / 180;
        double dx = Math.Cos(a);
        double dy = Math.Sin(a);
        double half = ((Math.Abs(dx) * w) + (Math.Abs(dy) * h)) / 2;
        double cx = x + (w / 2);
        double cy = y + (h / 2);
        Paint paint = _list.LinearGradient((float)(cx - (dx * half)), (float)(cy - (dy * half)), (float)(cx + (dx * half)), (float)(cy + (dy * half)), stops);
        _list.FillRect(Id(20), R(x, y, w, h), paint);
    }

    private void DataBar(double x, double y, double w, double h, DataBarVisual bar)
    {
        double inner = w - (2 * Pad);
        double bx = x + Pad + (bar.From * inner);
        double bw = (bar.To - bar.From) * inner;
        double by = y + 2 * Px;
        double bh = Math.Max(0, h - (4 * Px));
        if (bw > 0 && bh > 0)
        {
            if (bar.Gradient)
            {
                uint light = StyleCache.Blend(bar.Fill, 0xFFFFFF, 0.78);
                // The bar fades from its colour at the base to a light tint at the tip; a negative bar fades the other way.
                bool leftward = bar.Axis >= 0 && bar.To <= bar.Axis + 1e-9 && bar.From < bar.Axis;
                Paint paint = _list.LinearGradient((float)(leftward ? bx + bw : bx), 0, (float)(leftward ? bx : bx + bw), 0,
                    [new OneRender.Painting.GradientStop(0, C(bar.Fill)), new OneRender.Painting.GradientStop(1, C(light))]);
                _list.FillRect(Id(21), R(bx, by, bw, bh), paint);
            }
            else
            {
                _list.FillRect(Id(21), R(bx, by, bw, bh), C(bar.Fill));
            }

            if (bar.Border is { } border)
            {
                PaintColor bc = C(border);
                double t = Px;
                _list.FillRect(Id(21, 1), R(bx, by, bw, t), bc);
                _list.FillRect(Id(21, 2), R(bx, by + bh - t, bw, t), bc);
                _list.FillRect(Id(21, 3), R(bx, by, t, bh), bc);
                _list.FillRect(Id(21, 4), R(bx + bw - t, by, t, bh), bc);
            }
        }

        if (bar.Axis >= 0)
        {
            double ax = x + Pad + (bar.Axis * inner);
            _list.FillRect(Id(21, 5), R(ax - (Px / 2), y, Px, h), C(bar.AxisColor));
        }
    }

    // ---- borders --------------------------------------------------------------------------------------------------------------

    // Heavier lines win where two cells share an edge; at equal weight the cell to the right or below wins.
    private static int Weight(BorderLineStyle s) => s switch
    {
        BorderLineStyle.None => 0,
        BorderLineStyle.Hair => 1,
        BorderLineStyle.Dotted => 2,
        BorderLineStyle.DashDotDot => 3,
        BorderLineStyle.DashDot => 4,
        BorderLineStyle.Dashed => 5,
        BorderLineStyle.Thin => 6,
        BorderLineStyle.MediumDashDotDot => 7,
        BorderLineStyle.SlantDashDot => 8,
        BorderLineStyle.MediumDashDot => 9,
        BorderLineStyle.MediumDashed => 10,
        BorderLineStyle.Medium => 11,
        BorderLineStyle.Doubled => 12,
        BorderLineStyle.Thick => 13,
        _ => 0,
    };

    private (BorderSide Side, uint Color) SideOf(int r, int c, int which)
    {
        // which: 0 left, 1 right, 2 top, 3 bottom, 4 diagonal.
        (CellValue _, int styleIndex, bool _) = Fetch(r, c);
        StyleInfo st = _ctx.Styles.Get(styleIndex);
        CfCell? cf = _cf.Get(r, c);
        CellBorder border = st.Style.Border;
        uint[] colors = st.BorderColors;
        if (cf?.Border is { } cb && cf.BorderColors is { } cc)
        {
            BorderSide over = which switch { 0 => cb.Left, 1 => cb.Right, 2 => cb.Top, 3 => cb.Bottom, _ => cb.Diagonal };
            if (over.Style != BorderLineStyle.None)
            {
                return (over, cc[which]);
            }
        }

        BorderSide side = which switch { 0 => border.Left, 1 => border.Right, 2 => border.Top, 3 => border.Bottom, _ => border.Diagonal };
        return (side, colors[which]);
    }

    private (BorderSide Side, uint Color) Shared(int r1, int c1, int w1, int r2, int c2, int w2)
    {
        (BorderSide a, uint ac) = SideOf(r1, c1, w1);
        (BorderSide b, uint bc) = SideOf(r2, c2, w2);
        return Weight(a.Style) > Weight(b.Style) ? (a, ac) : (b.Style != BorderLineStyle.None ? (b, bc) : (a, ac));
    }

    private void Borders()
    {
        for (int ri = 0; ri < _rows.Count; ri++)
        {
            int r = _rows[ri];
            bool nextRowAdjacent = ri + 1 < _rows.Count && _rows[ri + 1] == r + 1;
            for (int ci = 0; ci < _cols.Count; ci++)
            {
                int c = _cols[ci];
                bool nextColAdjacent = ci + 1 < _cols.Count && _cols[ci + 1] == c + 1;
                _plan.MergeCover.TryGetValue(SheetPlan.Key(r, c), out CellRange m);
                bool merged = m.IsValid;
                double x = X(c);
                double y = Y(r);
                double w = _geo.ColWidth(c);
                double h = _geo.RowHeight(r);
                // Interior edges of a merged range carry no border.
                bool left = !merged || c == m.TopLeft.Column;
                bool right = !merged || c == m.BottomRight.Column;
                bool top = !merged || r == m.TopLeft.Row;
                bool bottom = !merged || r == m.BottomRight.Row;
                if (left)
                {
                    (BorderSide s, uint col) = c > 1 ? Shared(r, c - 1, 1, r, c, 0) : SideOf(r, c, 0);
                    Edge(x, y, x, y + h, false, s, col);
                }

                if (top)
                {
                    (BorderSide s, uint col) = r > 1 ? Shared(r - 1, c, 3, r, c, 2) : SideOf(r, c, 2);
                    Edge(x, y, x + w, y, true, s, col);
                }

                // The right and bottom edges are the next cell's left and top edges, so only a cell whose neighbour is not drawn next to it draws them.
                if (right && !nextColAdjacent)
                {
                    (BorderSide s, uint col) = Shared(r, c, 1, r, c + 1, 0);
                    Edge(x + w, y, x + w, y + h, false, s, col);
                }

                if (bottom && !nextRowAdjacent)
                {
                    (BorderSide s, uint col) = Shared(r, c, 3, r + 1, c, 2);
                    Edge(x, y + h, x + w, y + h, true, s, col);
                }

                if (!merged || (r == m.TopLeft.Row && c == m.TopLeft.Column))
                {
                    Diagonals(r, c, x, y, merged ? Box(m.TopLeft.Row, m.TopLeft.Column, m.BottomRight.Row, m.BottomRight.Column) : (x, y, w, h));
                }
            }
        }
    }

    private void Diagonals(int r, int c, double x, double y, (double X, double Y, double W, double H) box)
    {
        (CellValue _, int styleIndex, bool _) = Fetch(r, c);
        StyleInfo st = _ctx.Styles.Get(styleIndex);
        CellBorder b = st.Style.Border;
        if (b.Diagonal.Style == BorderLineStyle.None || !(b.DiagonalUp || b.DiagonalDown))
        {
            return;
        }

        double t = Thickness(b.Diagonal.Style);
        PaintColor color = C(st.BorderColors[4]);
        if (b.DiagonalDown)
        {
            Slash(box.X, box.Y, box.X + box.W, box.Y + box.H, t, color);
        }

        if (b.DiagonalUp)
        {
            Slash(box.X, box.Y + box.H, box.X + box.W, box.Y, t, color);
        }
    }

    private void Slash(double x1, double y1, double x2, double y2, double t, PaintColor color)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len <= 0)
        {
            return;
        }

        float nx = (float)(-dy / len * t / 2);
        float ny = (float)(dx / len * t / 2);
        var path = new PathBuffer();
        path.MoveTo((float)x1 + nx, (float)y1 + ny);
        path.LineTo((float)x2 + nx, (float)y2 + ny);
        path.LineTo((float)x2 - nx, (float)y2 - ny);
        path.LineTo((float)x1 - nx, (float)y1 - ny);
        path.Close();
        _list.FillPath(Id(23), path, color);
    }

    private static double Thickness(BorderLineStyle s) => s switch
    {
        BorderLineStyle.Hair => Px * 0.5,
        BorderLineStyle.Thin or BorderLineStyle.Dashed or BorderLineStyle.Dotted or BorderLineStyle.DashDot or BorderLineStyle.DashDotDot => Px,
        BorderLineStyle.Medium or BorderLineStyle.MediumDashed or BorderLineStyle.MediumDashDot or BorderLineStyle.MediumDashDotDot or BorderLineStyle.SlantDashDot => 2 * Px,
        BorderLineStyle.Thick => 3 * Px,
        BorderLineStyle.Doubled => 3 * Px,
        _ => Px,
    };

    // Dash patterns in units of the line's thickness, on then off.
    private static double[] Pattern(BorderLineStyle s) => s switch
    {
        BorderLineStyle.Dotted or BorderLineStyle.Hair => [1, 1],
        BorderLineStyle.Dashed or BorderLineStyle.MediumDashed => [3, 1],
        BorderLineStyle.DashDot or BorderLineStyle.MediumDashDot or BorderLineStyle.SlantDashDot => [3, 1, 1, 1],
        BorderLineStyle.DashDotDot or BorderLineStyle.MediumDashDotDot => [3, 1, 1, 1, 1, 1],
        _ => [],
    };

    private void Edge(double x1, double y1, double x2, double y2, bool horizontal, BorderSide side, uint rgb)
    {
        if (side.Style == BorderLineStyle.None)
        {
            return;
        }

        PaintColor color = C(rgb);
        double t = Thickness(side.Style);
        if (side.Style == BorderLineStyle.Doubled)
        {
            // Two 1 px lines with a 1 px gap.
            double line = Px;
            Stroke(x1, y1, x2, y2, horizontal, line, -line, color, []);
            Stroke(x1, y1, x2, y2, horizontal, line, line, color, []);
            return;
        }

        Stroke(x1, y1, x2, y2, horizontal, t, 0, color, Pattern(side.Style));
    }

    private void Stroke(double x1, double y1, double x2, double y2, bool horizontal, double t, double shift, PaintColor color, double[] pattern)
    {
        double len = horizontal ? x2 - x1 : y2 - y1;
        if (len <= 0)
        {
            return;
        }

        if (pattern.Length == 0)
        {
            // Solid lines run past each end by half their width so corners meet.
            if (horizontal)
            {
                _list.FillRect(Id(24), R(x1 - (t / 2), y1 + shift - (t / 2), len + t, t), color);
            }
            else
            {
                _list.FillRect(Id(24), R(x1 + shift - (t / 2), y1 - (t / 2), t, len + t), color);
            }

            return;
        }

        double unit = Math.Max(t, Px);
        double pos = 0;
        int i = 0;
        int n = 0;
        while (pos < len && n++ < 4000)
        {
            double run = pattern[i % pattern.Length] * unit;
            bool on = i % 2 == 0;
            double end = Math.Min(len, pos + run);
            if (on && end > pos)
            {
                if (horizontal)
                {
                    _list.FillRect(Id(25), R(x1 + pos, y1 + shift - (t / 2), end - pos, t), color);
                }
                else
                {
                    _list.FillRect(Id(25), R(x1 + shift - (t / 2), y1 + pos, t, end - pos), color);
                }
            }

            pos = end;
            i++;
        }
    }

    // ---- headings -------------------------------------------------------------------------------------------------------------

    private void Headings()
    {
        TextStyle style = _ctx.DefaultStyle with { Rgb = 0x000000 };
        PaintColor fill = C(0xE6E6E6);
        PaintColor line = C(0x808080);
        double hw = _plan.HeadingWidth;
        double hh = _plan.HeadingHeight;
        if (Tagged)
        {
            _list.BeginTag(Id(), TagRole.Artifact);
        }

        _list.FillRect(Id(26), R(-hw, -hh, hw, hh), fill);
        foreach (int r in _rows)
        {
            double y = Y(r);
            double h = _geo.RowHeight(r);
            _list.FillRect(Id(26), R(-hw, y, hw, h), fill);
            _list.FillRect(Id(27), R(-hw, y + h - (Px / 2), hw, Px * 0.67), line);
            CenterText(r.ToString(CultureInfo.InvariantCulture), style, -hw, y, hw, h);
        }

        foreach (int c in _cols)
        {
            double x = X(c);
            double w = _geo.ColWidth(c);
            _list.FillRect(Id(26), R(x, -hh, w, hh), fill);
            _list.FillRect(Id(27), R(x + w - (Px / 2), -hh, Px * 0.67, hh), line);
            CenterText(CellAddress.ColumnName(c), style, x, -hh, w, hh);
        }

        _list.FillRect(Id(27), R(-hw, -(Px / 2), hw + _titleW + _geo.ColsWidth(_page.Body.TopLeft.Column, _page.Body.BottomRight.Column), Px * 0.67), line);
        _list.FillRect(Id(27), R(-(Px / 2), -hh, Px * 0.67, hh + _titleH + _geo.RowsHeight(_page.Body.TopLeft.Row, _page.Body.BottomRight.Row)), line);
        if (Tagged)
        {
            _list.EndTag();
        }
    }

    private void CenterText(string text, TextStyle style, double x, double y, double w, double h)
    {
        TextBlock block = _ctx.Text.Layout([(text, style)], double.PositiveInfinity, false, null);
        if (block.Lines.Count == 0)
        {
            return;
        }

        TextLine line = block.Lines[0];
        DrawLine(line, x + ((w - line.Width) / 2), y + ((h - line.Height) / 2) + line.Ascent);
    }

    // ---- text -----------------------------------------------------------------------------------------------------------------

    // Text is tagged as a table whose rows and cells cover the same grid: every visible column of every row is accounted for, by a cell with
    // text, a merged cell's spans, or an empty cell that spans the gap (PDF/UA wants rows of one width). Rows and columns repeated from an
    // earlier page are artifacts. The print-title and table-header rows are header cells; a page with none takes its first row with text.
    private void Texts()
    {
        bool tagged = Tagged;
        var items = new Dictionary<long, (CellRange? Merge, CellContent Content, StyleInfo Style, CfCell? Cf, int Row, int Col)>();
        foreach ((int ir, int ic, CellRange? m, int ar, int ac) in Items())
        {
            if (PrepareCell(ir, ic, m) is { } prepared)
            {
                items[SheetPlan.Key(ar, ac)] = (m, prepared.Content, prepared.Style, prepared.Cf, ir, ic);
            }
        }

        if (items.Count == 0)
        {
            return;
        }

        HashSet<int> headerRows = HeaderRows();
        var thRows = new HashSet<int>();
        foreach (int r in _rows)
        {
            if (!InTitleRows(r) && headerRows.Contains(r) && _cols.Any(c => items.ContainsKey(SheetPlan.Key(r, c))))
            {
                thRows.Add(r);
            }
        }

        if (thRows.Count == 0)
        {
            foreach (int r in _rows)
            {
                if (!InTitleRows(r) && _cols.Any(c => items.ContainsKey(SheetPlan.Key(r, c))))
                {
                    thRows.Add(r);
                    break;
                }
            }
        }

        if (tagged)
        {
            _list.BeginTag(Id(), TagRole.Table);
        }

        var occupied = new HashSet<long>();
        for (int ri = 0; ri < _rows.Count; ri++)
        {
            int r = _rows[ri];
            if (InTitleRows(r))
            {
                // A repeated title row is drawn as an artifact, outside the table's rows.
                if (tagged)
                {
                    _list.BeginTag(Id(), TagRole.Artifact);
                }

                foreach (int c in _cols)
                {
                    if (items.TryGetValue(SheetPlan.Key(r, c), out var it))
                    {
                        PaintCellText(it.Row, it.Col, it.Merge, it.Content, it.Style, it.Cf);
                    }
                }

                if (tagged)
                {
                    _list.EndTag();
                }

                continue;
            }

            bool header = thRows.Contains(r);
            TagRole cellRole = header ? TagRole.TableHeaderCell : TagRole.TableDataCell;
            if (tagged)
            {
                _list.BeginTag(Id(), TagRole.TableRow);
            }

            int ci = 0;
            while (ci < _cols.Count)
            {
                if (occupied.Contains(SheetPlan.Key(ri, ci)))
                {
                    ci++;
                    continue;
                }

                int c = _cols[ci];
                if (items.TryGetValue(SheetPlan.Key(r, c), out var it))
                {
                    int colSpan = 1;
                    int rowSpan = 1;
                    if (it.Merge is { } m)
                    {
                        while (ci + colSpan < _cols.Count && _cols[ci + colSpan] <= m.BottomRight.Column)
                        {
                            colSpan++;
                        }

                        while (ri + rowSpan < _rows.Count && _rows[ri + rowSpan] <= m.BottomRight.Row && !InTitleRows(_rows[ri + rowSpan]))
                        {
                            rowSpan++;
                        }

                        for (int dr = 0; dr < rowSpan; dr++)
                        {
                            for (int dc = 0; dc < colSpan; dc++)
                            {
                                if (dr > 0 || dc > 0)
                                {
                                    occupied.Add(SheetPlan.Key(ri + dr, ci + dc));
                                }
                            }
                        }
                    }

                    bool repeatedColumn = InTitleCols(c);
                    if (tagged)
                    {
                        _list.BeginTag(Id(), cellRole, columnSpan: colSpan, rowSpan: rowSpan);
                    }

                    if (repeatedColumn && tagged)
                    {
                        // The cell is still a cell of the row; its repeated text is an artifact inside it.
                        _list.BeginTag(Id(), TagRole.Artifact);
                    }

                    PaintCellText(it.Row, it.Col, it.Merge, it.Content, it.Style, it.Cf);
                    if (repeatedColumn && tagged)
                    {
                        _list.EndTag();
                    }

                    if (tagged)
                    {
                        _list.EndTag();
                    }

                    ci += colSpan;
                    continue;
                }

                int start = ci;
                ci++;
                while (ci < _cols.Count && !occupied.Contains(SheetPlan.Key(ri, ci)) && !items.ContainsKey(SheetPlan.Key(r, _cols[ci])))
                {
                    ci++;
                }

                if (tagged)
                {
                    _list.BeginTag(Id(), cellRole, columnSpan: ci - start);
                    EmptyCellContent(X(_cols[start]), Y(r));
                    _list.EndTag();
                }
            }

            if (tagged)
            {
                _list.EndTag();
            }
        }

        if (tagged)
        {
            _list.EndTag();
        }
    }

    // A cell with nothing in it still needs content to stay in the structure tree, so it holds one no-break space, which draws nothing.
    private void EmptyCellContent(double x, double y)
    {
        TextBlock block = _ctx.Text.Layout([("\u00A0", _ctx.DefaultStyle)], double.PositiveInfinity, false, null);
        if (block.Lines.Count > 0 && block.Lines[0].Chunks.Count > 0)
        {
            DrawLine(block.Lines[0], x, y + block.Lines[0].Ascent);
        }
    }

    // Rows that hold column headings: the sheet's print titles and the header rows of its tables.
    private HashSet<int> HeaderRows()
    {
        var set = new HashSet<int>();
        if (_s.TitleRows.First > 0)
        {
            for (int r = _s.TitleRows.First; r <= _s.TitleRows.Last; r++)
            {
                set.Add(r);
            }
        }

        foreach (Table t in _plan.Sheet.Tables)
        {
            CellRange range = t.Range;
            if (range.IsValid)
            {
                for (int i = 0; i < t.HeaderRowCount; i++)
                {
                    set.Add(range.TopLeft.Row + i);
                }
            }
        }

        return set;
    }

    private (CellContent Content, StyleInfo Style, CfCell? Cf)? PrepareCell(int r, int c, CellRange? merge)
    {
        (CellValue value, int styleIndex, bool exists) = Fetch(r, c);
        if (!exists || value.Kind == CellValueKind.Empty)
        {
            return null;
        }

        StyleInfo st = _ctx.Styles.Get(styleIndex);
        CfCell? cf = _cf.Get(r, c);
        if (cf is { HideValue: true })
        {
            return null;
        }

        if (cf is not null && (cf.Bold is not null || cf.Italic is not null || cf.Strike is not null || cf.Underline is not null || cf.FontColor is not null || cf.NumberFormat is not null))
        {
            TextStyle t = st.Text;
            t = t with
            {
                Bold = cf.Bold ?? t.Bold,
                Italic = cf.Italic ?? t.Italic,
                Strike = cf.Strike ?? t.Strike,
                Underline = cf.Underline ?? t.Underline,
                Rgb = cf.FontColor ?? t.Rgb,
            };
            NumberFormat nf = cf.NumberFormat is { } code && NumberFormat.TryParse(code, out NumberFormat? parsed, out _) ? parsed : st.Format;
            st = new StyleInfo { Style = st.Style, Text = t, Format = nf, Fill = st.Fill, Gradient = st.Gradient, GradientColors = st.GradientColors, Pattern = st.Pattern, HasBorder = st.HasBorder, BorderColors = st.BorderColors, IsGeneral = cf.NumberFormat is null && st.IsGeneral };
        }

        (double _, double _, double w, double _) = ItemBox(r, c, merge);
        double iconInset = cf?.Icon is not null ? Math.Min(st.Text.Size, 20) + 2 : 0;
        double available = Math.Max(1, w - (2 * Pad) - iconInset - (Math.Clamp(st.Style.Alignment.Indent, 0, 250) * _ctx.MaxDigitWidth * 0.75));
        CellContent? content = CellContents.Build(_ctx, _plan.Sheet, _s, value, st, available, null);
        if (content is null || content.IsEmptyText)
        {
            return null;
        }

        return (content, st, cf);
    }

    private void PaintCellText(int r, int c, CellRange? merge, CellContent content, StyleInfo st, CfCell? cf)
    {
        (double x, double y, double w, double h) = ItemBox(r, c, merge);
        CellAlignment al = st.Style.Alignment;
        double iconInset = cf?.Icon is not null ? Math.Min(st.Text.Size, 20) + 2 : 0;
        double indent = Math.Clamp(al.Indent, 0, 250) * _ctx.MaxDigitWidth * 0.75;
        double left = x + Pad + iconInset;
        double inner = Math.Max(1, w - (2 * Pad) - iconInset);
        bool wrap = al.WrapText;
        int rotation = al.TextRotation;
        bool stacked = rotation == 255;
        double angle = rotation is > 0 and <= 90 ? rotation : rotation is > 90 and <= 180 ? -(rotation - 90) : 0;
        bool rotated = angle != 0 && !stacked;
        bool rtlRead = al.ReadingOrder == 2;

        List<(string Text, TextStyle Style)> spans = content.Spans;
        CellHorizontalAlignment ha = al.Horizontal;
        if (ha == CellHorizontalAlignment.Fill)
        {
            string one = content.PlainText;
            double unit = _ctx.Text.Measure(one, spans[0].Style);
            int n = unit > 0 ? Math.Max(1, (int)Math.Floor((inner - indent) / unit)) : 1;
            spans = [(string.Concat(Enumerable.Repeat(one, n)), spans[0].Style)];
            wrap = false;
        }

        if (stacked)
        {
            // Stacked text puts one character under another.
            spans = [(string.Join('\n', content.PlainText.Select(ch => ch.ToString())), spans[0].Style)];
            wrap = false;
        }

        double maxWidth = double.PositiveInfinity;
        if (wrap)
        {
            maxWidth = rotated && Math.Abs(angle) == 90 ? Math.Max(1, h - (2 * VPad)) : Math.Max(1, inner - indent - (ha is CellHorizontalAlignment.Left or CellHorizontalAlignment.Right ? 0 : 0));
        }

        TextBlock block = _ctx.Text.Layout(spans, maxWidth, rtlRead, null);
        if (block.Lines.Count == 0)
        {
            return;
        }

        if (al.ShrinkToFit && !wrap && !rotated && block.Width > inner - indent && block.Width > 0)
        {
            double f = Math.Max(0.1, (inner - indent) / block.Width);
            var shrunk = spans.Select(s => (s.Text, s.Style with { Size = s.Style.Size * f })).ToList();
            block = _ctx.Text.Layout(shrunk, double.PositiveInfinity, rtlRead, null);
        }

        // General alignment: text left (right for right-to-left text), numbers right, booleans and errors centred.
        CellHorizontalAlignment effective = ha switch
        {
            CellHorizontalAlignment.General => content.IsNumber ? CellHorizontalAlignment.Right : content.IsCentered ? CellHorizontalAlignment.Center : block.IsRightToLeft || rtlRead ? CellHorizontalAlignment.Right : CellHorizontalAlignment.Left,
            CellHorizontalAlignment.Fill => CellHorizontalAlignment.Left,
            _ => ha,
        };

        if (stacked)
        {
            effective = CellHorizontalAlignment.Center;
        }

        if (effective == CellHorizontalAlignment.CenterContinuous)
        {
            (x, w) = ContinuousBox(r, c, x, w);
            left = x + Pad;
            inner = Math.Max(1, w - (2 * Pad));
            effective = CellHorizontalAlignment.Center;
        }

        double textW = block.Width;
        double blockH = block.Height;

        // Position of the block's box inside the cell, for the horizontal and vertical alignments.
        double boxW = textW;
        double boxH = blockH;
        Matrix3x2 rotation2D = Matrix3x2.Identity;
        double boxX;
        double boxY;
        if (rotated)
        {
            double rad = -angle * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            (double X, double Y)[] corners = [(0, 0), (textW, 0), (0, blockH), (textW, blockH)];
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach ((double cx, double cy) in corners)
            {
                double rx = (cx * cos) - (cy * sin);
                double ry = (cx * sin) + (cy * cos);
                minX = Math.Min(minX, rx);
                maxX = Math.Max(maxX, rx);
                minY = Math.Min(minY, ry);
                maxY = Math.Max(maxY, ry);
            }

            boxW = maxX - minX;
            boxH = maxY - minY;
            boxX = effective switch { CellHorizontalAlignment.Right => left + inner - indent - boxW, CellHorizontalAlignment.Center => left + ((inner - boxW) / 2), _ => left + indent };
            boxY = VerticalTop(al.Vertical, y, h, boxH);
            rotation2D = Matrix3x2.CreateRotation((float)rad) * Matrix3x2.CreateTranslation((float)(boxX - minX), (float)(boxY - minY));
        }
        else
        {
            boxX = left;
            boxY = VerticalTop(al.Vertical, y, h, boxH);
        }

        // How far the text may spill: into empty neighbours in the direction of its alignment, unless it wraps or is a number.
        double clipL = x;
        double clipR = x + w;
        bool overflowing = !rotated && !wrap && textW > inner - indent + 1e-6;
        if (overflowing && !content.IsNumber)
        {
            (clipL, clipR) = Spill(r, c, merge, effective, x, w, textW + indent + (2 * Pad));
        }

        bool needsClip = overflowing || boxH > h + 1e-6 || rotated || textW > inner + 1e-6;
        if (needsClip)
        {
            _list.PushClipRect(Id(40), R(clipL, y, clipR - clipL, h));
        }

        if (rotated)
        {
            _list.PushTransform(Id(41), rotation2D);
        }

        double top = rotated ? 0 : boxY;
        for (int li = 0; li < block.Lines.Count; li++)
        {
            TextLine line = block.Lines[li];
            double extra = 0;
            double lineX;
            if (rotated)
            {
                lineX = 0;
            }
            else
            {
                double room = inner - indent;
                lineX = effective switch
                {
                    CellHorizontalAlignment.Right => left + inner - indent - line.Width,
                    CellHorizontalAlignment.Center => left + ((inner - line.Width) / 2),
                    _ => left + (rtlRead ? 0 : indent),
                };
                if (wrap && !stacked && (effective == CellHorizontalAlignment.Justify && !line.IsLast || effective == CellHorizontalAlignment.Distributed) && line.Spaces > 0 && room > line.Width)
                {
                    extra = room - line.Width;
                    lineX = left + indent;
                }
                else if (effective is CellHorizontalAlignment.Justify or CellHorizontalAlignment.Distributed)
                {
                    lineX = left + indent;
                }
            }

            if (stacked)
            {
                lineX = left + ((inner - line.Width) / 2);
            }

            if (extra > 0)
            {
                Justify(line, extra);
            }

            DrawLine(line, lineX, top + line.Ascent);
            top += line.Height;
        }

        if (rotated)
        {
            _list.PopTransform();
        }

        if (needsClip)
        {
            _list.PopClip();
        }
    }

    private static double VerticalTop(CellVerticalAlignment v, double y, double h, double boxH) => v switch
    {
        CellVerticalAlignment.Top or CellVerticalAlignment.Justify or CellVerticalAlignment.Distributed => y + VPad,
        CellVerticalAlignment.Center => y + ((h - boxH) / 2),
        _ => y + h - VPad - boxH,
    };

    // Spreads the extra width over the spaces of a line.
    private static void Justify(TextLine line, double extra)
    {
        if (line.Spaces <= 0)
        {
            return;
        }

        double per = extra / line.Spaces;
        double shift = 0;
        foreach (GlyphChunk chunk in line.Chunks)
        {
            double within = 0;
            for (int i = 0; i < chunk.X.Length; i++)
            {
                chunk.X[i] += (float)(shift + within);
                int ci = chunk.Clusters[i];
                bool nextIsSameCluster = i + 1 < chunk.Clusters.Length && chunk.Clusters[i + 1] == ci;
                if (!nextIsSameCluster && ci >= 0 && ci < chunk.Text.Length && chunk.Text[ci] == ' ')
                {
                    // The space glyph itself stays; everything after it moves.
                    within += per;
                }
            }

            shift += within;
            chunk.Start += 0;
            chunk.Width += within;
        }

        line.Width += extra;
    }

    // Centre across selection: the cell and the empty cells to its right that also have that alignment.
    private (double X, double W) ContinuousBox(int r, int c, double x, double w)
    {
        int last = c;
        for (int cc = c + 1; cc <= _page.Body.BottomRight.Column; cc++)
        {
            (CellValue v, int si, bool exists) = Fetch(r, cc);
            if ((exists && v.Kind != CellValueKind.Empty) || _ctx.Styles.Get(si).Style.Alignment.Horizontal != CellHorizontalAlignment.CenterContinuous)
            {
                break;
            }

            last = cc;
        }

        double right = X(last) + _geo.ColWidth(last);
        return (x, right - x);
    }

    // The room text has to spill into: empty neighbours (no value, not merged) towards the side it is aligned to.
    private (double Left, double Right) Spill(int r, int c, CellRange? merge, CellHorizontalAlignment align, double x, double w, double needed)
    {
        double left = x;
        double right = x + w;
        int firstCol = merge is { } m ? m.TopLeft.Column : c;
        int lastCol = merge is { } m2 ? m2.BottomRight.Column : c;
        bool goRight = align is CellHorizontalAlignment.Left or CellHorizontalAlignment.Center or CellHorizontalAlignment.General or CellHorizontalAlignment.Justify or CellHorizontalAlignment.Distributed;
        bool goLeft = align is CellHorizontalAlignment.Right or CellHorizontalAlignment.Center;
        if (goRight)
        {
            for (int cc = lastCol + 1; cc <= _page.Body.BottomRight.Column && right - left < needed + (goLeft ? needed : 0); cc++)
            {
                if (!Empty(r, cc))
                {
                    break;
                }

                right = X(cc) + _geo.ColWidth(cc);
            }
        }

        if (goLeft)
        {
            for (int cc = firstCol - 1; cc >= _page.Body.TopLeft.Column && right - left < needed + (goRight ? needed : 0); cc--)
            {
                if (!Empty(r, cc))
                {
                    break;
                }

                left = X(cc);
            }
        }

        return (left, right);
    }

    private bool Empty(int r, int c)
    {
        if (_plan.MergeCover.ContainsKey(SheetPlan.Key(r, c)))
        {
            return false;
        }

        return !_store.TryGetCell(r, c, out CellValue v, out _) || v.Kind == CellValueKind.Empty;
    }

    // Draws the glyph runs of a line with the left edge at x and the baseline at y, and the underline and strike-through of each run.
    private void DrawLine(TextLine line, double x, double baseline)
    {
        foreach (GlyphChunk chunk in line.Chunks)
        {
            double ox = x + chunk.Start;
            double oy = baseline - chunk.BaselineShift;
            PaintColor color = C(chunk.Style.Rgb);
            _list.DrawGlyphRun(Id(8), chunk.Face, (float)chunk.Size, (float)ox, (float)oy, color, chunk.GlyphIds, chunk.X, chunk.Y, chunk.Clusters, chunk.Text, chunk.RightToLeft);
            Decorations(chunk, ox, oy, color);
        }
    }

    private void Decorations(GlyphChunk chunk, double ox, double oy, PaintColor color)
    {
        TextStyle s = chunk.Style;
        if (s.Underline == FontUnderline.None && !s.Strike)
        {
            return;
        }

        FontMetrics m = chunk.Face.Metrics;
        double unit = chunk.Size / m.UnitsPerEm;
        double thick = Math.Max(0.5, m.UnderlineThickness * unit);
        double width = chunk.Width;
        if (s.Underline != FontUnderline.None)
        {
            double uy = oy + Math.Max(thick, Math.Abs(m.UnderlinePosition) * unit);
            _list.FillRect(Id(9), R(ox, uy - (thick / 2), width, thick), color);
            if (s.Underline is FontUnderline.Doubled or FontUnderline.DoubleAccounting)
            {
                _list.FillRect(Id(9, 1), R(ox, uy + (thick * 1.8) - (thick / 2), width, thick), color);
            }
        }

        if (s.Strike)
        {
            double sy = oy - (chunk.Size * 0.3);
            _list.FillRect(Id(10), R(ox, sy - (thick / 2), width, thick), color);
        }
    }

    // ---- drawing objects ------------------------------------------------------------------------------------------------------

    private readonly Dictionary<string, DrawingContext> _drawingContexts = [];

    private DrawingContext ContextFor(string drawingPart)
    {
        if (!_drawingContexts.TryGetValue(drawingPart, out DrawingContext? c))
        {
            IOpcPackage package = _ctx.Workbook.Package;
            var bytes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            byte[]? ImageBytes(string rid)
            {
                if (bytes.TryGetValue(rid, out byte[]? cached))
                {
                    return cached;
                }

                byte[]? found = null;
                foreach (Relationship r in package.GetRelationships(drawingPart))
                {
                    // Linked (external) pictures are never opened.
                    if (r.Id == rid && OpcPackage.TryResolveTarget(drawingPart, r, out PartUri uri) && package.FindPart(uri.Value) is { } part)
                    {
                        found = part.GetBytes().ToArray();
                    }
                }

                return bytes[rid] = found;
            }

            c = new DrawingContext(new ColorResolver(_ctx.DrawingTheme), _ctx.Images, ImageBytes, drawingPart) { Tagged = Tagged, Fonts = _ctx.Fonts };
            c.ChartPainter = (rel, list, x, y, w, h) =>
            {
                if (RecursiveAI.OneDocument.Charts.ChartHost.Load(package, drawingPart, rel) is not { } chart)
                {
                    return false;
                }

                RecursiveAI.OneDocument.Charts.ChartHost.Paint(list, chart.Chart, x, y, w, h, chart.Theme, _ctx.Fonts);
                return true;
            };
            _drawingContexts[drawingPart] = c;
        }

        return c;
    }

    // The shape inside an anchor: the drawing painter takes the shape element itself.
    private static MarkupNode ShapeOf(MarkupNode anchor)
    {
        foreach (MarkupNode c in anchor.ChildElements)
        {
            if (c.LocalNameString is "sp" or "pic" or "graphicFrame" or "grpSp" or "cxnSp" or "AlternateContent")
            {
                return c;
            }
        }

        return default;
    }

    private void PaintObjects()
    {
        if (_page.Objects.Count == 0)
        {
            return;
        }

        // Objects that spill past the printed cells are cut at the edge of the page's content.
        _list.PushClipRect(Id(51), new RectF((float)(_page.ContentX * K), (float)(_page.ContentY * K), (float)(_page.ContentWidth * K), (float)(_page.ContentHeight * K)));
        foreach (SheetObject o in _page.Objects)
        {
            var bounds = new RectF((float)(o.X * K), (float)(o.Y * K), (float)(o.Width * K), (float)(o.Height * K));
            if (_ctx.Options.PaintDrawings && o.Width > 0 && o.Height > 0)
            {
                DrawingContext dc = ContextFor(o.DrawingPart);
                if (!DrawingPainter.Draw(_list, () => Id(52), ShapeOf(o.Source), new DrawingBox(o.X, o.Y, o.Width, o.Height), dc, o.Name.Length > 0 ? o.Name : null))
                {
                    _ctx.Note("sheet-layout.object-not-painted", "The drawing object \"" + o.Name + "\" holds nothing the drawing painter can draw.", _plan.Sheet.Name);
                }
            }

            if (_ctx.Options.ObjectPlaceholders)
            {
                if (Tagged)
                {
                    _list.BeginTag(Id(), TagRole.Artifact);
                }

                PaintColor edge = C(0x808080);
                const float t = 1f;
                _list.FillRect(Id(50), new RectF(bounds.X, bounds.Y, bounds.Width, t), edge);
                _list.FillRect(Id(50, 1), new RectF(bounds.X, bounds.Y + bounds.Height - t, bounds.Width, t), edge);
                _list.FillRect(Id(50, 2), new RectF(bounds.X, bounds.Y, t, bounds.Height), edge);
                _list.FillRect(Id(50, 3), new RectF(bounds.X + bounds.Width - t, bounds.Y, t, bounds.Height), edge);
                if (Tagged)
                {
                    _list.EndTag();
                }
            }

            _ctx.Options.ObjectPainter?.Invoke(new SheetObjectPaintContext(_page, o, _list, bounds));
        }

        _list.PopClip();
    }

    // ---- headers and footers --------------------------------------------------------------------------------------------------

    private void HeaderAndFooter()
    {
        SheetHeaderFooter hf = _s.HeaderFooter;
        string? header = hf.HeaderFor(_page.NumberInSheet, _page.Number);
        string? footer = hf.FooterFor(_page.NumberInSheet, _page.Number);
        if (string.IsNullOrEmpty(header) && string.IsNullOrEmpty(footer))
        {
            return;
        }

        DateTimeOffset now = _ctx.Options.Clock.GetLocalNow();
        string file = _ctx.Options.FileName ?? (_ctx.Workbook.FilePath is { } fp ? Path.GetFileName(fp) : "Book1.xlsx");
        string dir = _ctx.Workbook.FilePath is { } fp2 ? Path.GetDirectoryName(fp2) ?? string.Empty : string.Empty;
        var fields = new HeaderFooterFields(_page.Number, Math.Max(_ctx.TotalPages, _page.Number),
            now.ToString("M/d/yyyy", CultureInfo.InvariantCulture), now.ToString("h:mm tt", CultureInfo.InvariantCulture), file, dir, _plan.Sheet.Name);
        double scale = hf.ScaleWithDocument ? _page.Scale : 1;
        TextStyle baseStyle = _ctx.DefaultStyle with { Rgb = 0 };
        double left = hf.AlignWithMargins ? _s.MarginLeft : Math.Min(_s.MarginLeft, 36);
        double right = _page.Width - (hf.AlignWithMargins ? _s.MarginRight : Math.Min(_s.MarginRight, 36));
        if (Tagged)
        {
            _list.BeginTag(Id(), TagRole.Artifact);
        }

        PaintHf(HeaderFooterCodes.Parse(header, baseStyle, fields), left, right, _s.MarginHeader, false, scale);
        PaintHf(HeaderFooterCodes.Parse(footer, baseStyle, fields), left, right, _page.Height - _s.MarginFooter, true, scale);
        if (Tagged)
        {
            _list.EndTag();
        }
    }

    private void PaintHf(HeaderFooterSection[] sections, double left, double right, double edge, bool bottomUp, double scale)
    {
        for (int i = 0; i < 3; i++)
        {
            HeaderFooterSection sec = sections[i];
            if (sec.HasPicture)
            {
                _ctx.Note("sheet-layout.header-picture", "A header or footer picture (&G) is not drawn.", _plan.Sheet.Name);
            }

            if (sec.Runs.Count == 0)
            {
                continue;
            }

            for (int k = 0; k < sec.Runs.Count; k++)
            {
                (string t, TextStyle s) = sec.Runs[k];
                sec.Runs[k] = (t, s with { Size = s.Size * scale });
            }

            TextBlock block = _ctx.Text.Layout(sec.Runs, double.PositiveInfinity, false, null);
            double top = bottomUp ? edge - block.Height : edge;
            foreach (TextLine line in block.Lines)
            {
                double x = i switch { 0 => left, 1 => left + ((right - left - line.Width) / 2), _ => right - line.Width };
                DrawLine(line, x, top + line.Ascent);
                top += line.Height;
            }
        }

    }
}
