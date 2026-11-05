using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Layout;

// Turns a hub table into a formatted table: resolved properties, borders, column widths, cell content at its final width and row
// heights. Placement on pages is TablePlacement's job. Rules are cited where they come from; the choices the specification leaves open
// are listed in docs/layout-tables.md.
internal static class TableFormatter
{
    private const int MaxDepth = 6;
    private const double Unbounded = 30000; // wide enough that nothing wraps, used to measure content

    public static FormattedTableBlock FormatBlock(Table table, LayoutContext ctx, double available)
    {
        // The flow's block type needs a paragraph; the table item never reads it.
        return new FormattedTableBlock { Source = null!, Table = Format(table, ctx, available, 0) };
    }

    private sealed class Prepared
    {
        public required Table Table;
        public required LogicalTable Model;
        public required Dictionary<LogicalCell, ResolvedProperties> Props;
        public required ResolvedProperties TableProps;
        public required List<FormattedCell> Cells;
        public required List<CellWidthSpec> Specs;
        public required double[] Grid;
        public bool FixedLayout;
        public double Spacing;
        public bool Rtl;
        public PreferredWidth TableWidth { get; set; }
    }

    // ECMA-376 Part 1 §17.4.64 (tblW), §17.4.42 (tcW): dxa in twentieths of a point, pct in fiftieths of a percent (or a "50%" string),
    // auto and nil leave the width to the algorithm. A dxa width of 0 reads as auto.
    internal static PreferredWidth ReadWidth(ResolvedProperty? p)
    {
        if (p is null)
        {
            return PreferredWidth.Auto;
        }

        string type = p.Attribute("type") ?? "dxa";
        string? w = p.Attribute("w");
        if (w is null || type is "auto" or "nil")
        {
            return PreferredWidth.Auto;
        }

        if (w.EndsWith('%'))
        {
            return double.TryParse(w.AsSpan(0, w.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pc)
                ? new PreferredWidth(WidthKind.Percent, pc)
                : PreferredWidth.Auto;
        }

        if (!long.TryParse(w, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long v))
        {
            return PreferredWidth.Auto;
        }

        if (type == "pct")
        {
            return v <= 0 ? PreferredWidth.Auto : new PreferredWidth(WidthKind.Percent, v / 50.0);
        }

        return v <= 0 ? PreferredWidth.Auto : new PreferredWidth(WidthKind.Points, v / 20.0);
    }

    // ECMA-376 Part 1 §17.4.43 (tcMar) and §17.4.42 (tblCellMar): the cell's own margin beats the table's; defaults 108 twips left and
    // right, 0 top and bottom.
    private static double Margin(ResolvedProperties bag, string side)
    {
        string[] names = side switch
        {
            "left" => ["left", "start"],
            "right" => ["right", "end"],
            _ => [side],
        };
        foreach (string container in new[] { "tcMar", "tblCellMar" })
        {
            foreach (string name in names)
            {
                if (bag.Get(container + "/" + name) is { } p && p.Number("w") is { } v)
                {
                    return v / 20.0;
                }
            }
        }

        return side is "left" or "right" ? 5.4 : 0;
    }

    private static Prepared Prepare(Table table, LayoutContext ctx, int depth)
    {
        LogicalTable model = table.Logical();
        var props = new Dictionary<LogicalCell, ResolvedProperties>();
        foreach (LogicalCell c in model.Cells)
        {
            props[c] = ctx.Doc.Styles.ResolveTableCell(table, model, c);
        }

        ResolvedProperties tb = props[model.Cells[0]];
        bool rtl = table.IsRightToLeft;
        // ECMA-376 Part 1 §17.4.53 (tblLayout), §17.4.44 (tblCellSpacing, dxa).
        bool fixedLayout = tb.Get("tblLayout")?.Attribute("type") == "fixed" || table.IsFixedLayout;
        double spacing = Math.Max(0, (tb.Get("tblCellSpacing")?.Number("w") ?? 0) / 20.0);

        var cells = new List<FormattedCell>(model.Cells.Count);
        foreach (LogicalCell c in model.Cells)
        {
            cells.Add(new FormattedCell { Logical = c, Row = c.Row, Column = c.Column, ColumnSpan = c.ColumnSpan, RowSpan = c.RowSpan });
        }

        TableBorders.Resolve(model, props, cells, rtl, spacing > 0);

        var specs = new List<CellWidthSpec>(cells.Count);
        double halfOrFull = spacing > 0 ? 1.0 : 0.5;
        foreach (FormattedCell fc in cells)
        {
            ResolvedProperties bag = props[fc.Logical];
            fc.PadLeft = Margin(bag, "left") + ((fc.Left?.Width ?? 0) * halfOrFull);
            fc.PadRight = Margin(bag, "right") + ((fc.Right?.Width ?? 0) * halfOrFull);
            fc.PadTop = Margin(bag, "top") + ((fc.Top?.Width ?? 0) * halfOrFull);
            fc.PadBottom = Margin(bag, "bottom") + ((fc.Bottom?.Width ?? 0) * halfOrFull);
            fc.VAlign = bag.Get("vAlign")?.Value ?? "top";
            fc.Shading = Shading(bag.Get("shd"));
            if (bag.Get("textDirection")?.Value is { } td && td is not ("lrTb" or "lrTbV"))
            {
                ctx.NoteOnce("layout.table-text-direction", "layout.table-text-direction", "Text direction in table cells is not laid out; the text is set horizontally.", table);
            }

            PreferredWidth pref = ReadWidth(bag.Get("tcW"));
            double min = 0;
            double max = 0;
            if (!fixedLayout)
            {
                (min, max) = MeasureBlocks(fc.Logical.Cell.Blocks(), ctx, depth);
                if (bag.Get("noWrap") is { IsOn: true })
                {
                    min = max;
                }
            }

            double pad = fc.PadLeft + fc.PadRight;
            specs.Add(new CellWidthSpec(fc.Column, fc.ColumnSpan, min + pad, max + pad, pref));
        }

        int n = model.ColumnCount;
        var grid = new double[n];
        IReadOnlyList<long> g = table.GridColumns;
        for (int i = 0; i < n && i < g.Count; i++)
        {
            grid[i] = g[i] / 20.0;
        }

        return new Prepared
        {
            Table = table,
            Model = model,
            Props = props,
            TableProps = tb,
            Cells = cells,
            Specs = specs,
            Grid = grid,
            FixedLayout = fixedLayout,
            Spacing = spacing,
            Rtl = rtl,
            TableWidth = ReadWidth(tb.Get("tblW")),
        };
    }

    private static double[] SolveWidths(Prepared p, double available)
    {
        int n = p.Grid.Length;
        double gaps = p.Spacing * (n + 1);
        PreferredWidth tw = p.TableWidth.Kind == WidthKind.Points ? p.TableWidth with { Value = Math.Max(0, p.TableWidth.Value - gaps) } : p.TableWidth;
        return TableWidths.Solve(p.FixedLayout, Math.Max(0, available - gaps), tw, p.Grid, p.Specs);
    }

    // The narrowest and widest a nested table can be, for the cell that holds it.
    private static (double Min, double Max) Intrinsic(Table table, LayoutContext ctx, int depth)
    {
        Prepared p = Prepare(table, ctx, depth + 1);
        if (p.Model.RowCount == 0)
        {
            return (0, 0);
        }

        // A percentage width says nothing about the content, so it is left out when measuring.
        if (p.TableWidth.Kind == WidthKind.Percent)
        {
            p.TableWidth = PreferredWidth.Auto;
        }

        double gaps = p.Spacing * (p.Grid.Length + 1);
        double min = SolveWidths(p, 0).Sum() + gaps;
        double max = SolveWidths(p, Unbounded).Sum() + gaps;
        if (p.TableWidth.Kind == WidthKind.Points)
        {
            max = Math.Max(max, p.TableWidth.Value);
        }

        return (min, Math.Max(min, max));
    }

    public static FormattedTable Format(Table table, LayoutContext ctx, double available, int depth)
    {
        LogicalTable model = table.Logical();
        if (model.RowCount == 0 || model.ColumnCount == 0 || model.Cells.Count == 0)
        {
            return new FormattedTable { Source = table, Model = model };
        }

        Prepared p = Prepare(table, ctx, depth);
        double[] widths = SolveWidths(p, available);
        int cols = widths.Length;
        var ft = new FormattedTable { Source = table, Model = model, ColumnWidths = widths, Spacing = p.Spacing, Rtl = p.Rtl, Cells = p.Cells };
        ft.ColumnLeft = new double[cols];
        double x = p.Spacing;
        for (int i = 0; i < cols; i++)
        {
            ft.ColumnLeft[i] = x;
            x += widths[i] + p.Spacing;
        }

        ft.Width = x;
        foreach (FormattedCell fc in p.Cells)
        {
            fc.X = ft.ColumnLeft[fc.Column];
            int last = Math.Min(cols - 1, fc.Column + fc.ColumnSpan - 1);
            fc.Width = ft.ColumnLeft[last] + widths[last] - fc.X;
        }

        // Position: ECMA-376 Part 1 §17.4.28 (jc) and §17.4.51 (tblInd). Up to compatibility mode 14 the table is placed so that the
        // text of its first column lines up with the margin, which pulls the edge out by the left cell margin; mode 15 puts the edge
        // at the indent.
        ResolvedProperties tb = p.TableProps;
        double ind = (tb.Get("tblInd")?.Number("w") ?? 0) / 20.0;
        double outdent = ctx.Compat.Mode < 15 ? Margin(tb, "left") : 0;
        string jc = tb.Value("jc") ?? "left";
        ft.Left = jc switch
        {
            "center" => (available - ft.Width) / 2.0,
            "right" or "end" => available - ft.Width,
            _ => ind - outdent,
        };

        ReadFloating(table, ft);

        // Rows.
        List<TableRow> rows = [.. table.Rows()];
        for (int r = 0; r < model.RowCount; r++)
        {
            TableRow tr = r < rows.Count ? rows[r] : rows[^1];
            var row = new FormattedRow { Index = r, IsHeader = tr.IsHeader, CantSplit = tr.CannotSplit };
            if (tr.Height is { } h)
            {
                // ECMA-376 Part 1 §17.4.81 (trHeight): atLeast is the default rule; auto ignores the value.
                row.Rule = tr.HeightRule is "exact" or "auto" ? tr.HeightRule : "atLeast";
                row.Height = h / 20.0;
            }

            ft.Rows.Add(row);
        }

        foreach (FormattedCell fc in p.Cells)
        {
            ft.Rows[fc.Row].Starting.Add(fc);
            fc.IsHeader = ft.Rows[fc.Row].IsHeader;
            BuildContent(fc, ctx, depth);
        }

        for (int r = 0; r < ft.Rows.Count && ft.Rows[r].IsHeader; r++)
        {
            ft.HeaderCount++;
        }

        ComputeRowHeights(ft);
        ft.GroupEnd = ComputeGroups(ft);
        return ft;
    }

    // Heights: a single-row cell sets its row's minimum; a vertically merged cell then adds what its rows still lack to the last row of
    // its region. trHeight applies per row first (exact fixes the height, atLeast is a floor).
    private static void ComputeRowHeights(FormattedTable ft)
    {
        int rows = ft.Rows.Count;
        var h = new double[rows];
        foreach (FormattedCell fc in ft.Cells.Where(static c => c.RowSpan == 1))
        {
            h[fc.Row] = Math.Max(h[fc.Row], fc.Need);
        }

        for (int r = 0; r < rows; r++)
        {
            FormattedRow row = ft.Rows[r];
            double spec = row.Height;
            h[r] = row.Rule switch
            {
                "exact" => spec,
                "atLeast" => Math.Max(h[r], spec),
                _ => h[r],
            };
        }

        foreach (FormattedCell fc in ft.Cells.Where(static c => c.RowSpan > 1).OrderBy(static c => c.Row + c.RowSpan))
        {
            int end = Math.Min(rows - 1, fc.Row + fc.RowSpan - 1);
            double have = 0;
            for (int r = fc.Row; r <= end; r++)
            {
                have += h[r];
            }

            have += ft.Spacing * (end - fc.Row);
            if (have < fc.Need && ft.Rows[end].Rule != "exact")
            {
                h[end] += fc.Need - have;
            }
        }

        for (int r = 0; r < rows; r++)
        {
            ft.Rows[r].Height = h[r];
        }
    }

    private static int[] ComputeGroups(FormattedTable ft)
    {
        int rows = ft.Rows.Count;
        var reach = new int[rows];
        for (int r = 0; r < rows; r++)
        {
            reach[r] = r;
        }

        foreach (FormattedCell fc in ft.Cells)
        {
            reach[fc.Row] = Math.Max(reach[fc.Row], Math.Min(rows - 1, fc.Row + fc.RowSpan - 1));
        }

        var ends = new int[rows];
        for (int r = 0; r < rows; r++)
        {
            int e = reach[r];
            for (int i = r; i <= e; i++)
            {
                e = Math.Max(e, reach[i]);
            }

            ends[r] = e;
        }

        return ends;
    }

    // ECMA-376 Part 1 §17.4.57 (tblpPr). Position attributes are in twentieths of a point.
    private static void ReadFloating(Table table, FormattedTable ft)
    {
        if (table.PropertiesElement is not { } tblPr || Xml.Child(tblPr, "tblpPr") is not { } fp)
        {
            return;
        }

        ft.Floating = true;
        ft.HorizontalAnchor = Xml.Attr(fp, "horzAnchor") ?? "text";
        ft.VerticalAnchor = Xml.Attr(fp, "vertAnchor") ?? "margin";
        ft.FloatX = (Xml.Long(Xml.Attr(fp, "tblpX")) ?? 0) / 20.0;
        ft.FloatY = (Xml.Long(Xml.Attr(fp, "tblpY")) ?? 0) / 20.0;
        ft.HorizontalSpec = Xml.Attr(fp, "tblpXSpec");
        ft.VerticalSpec = Xml.Attr(fp, "tblpYSpec");
    }

    // ECMA-376 Part 1 §17.3.4 (shd) and §17.18.78 (ST_Shd): clear shows the fill, solid the colour, pctN mixes N percent of the colour
    // into the fill; the stripe and trellis patterns are approximated as pct50.
    internal static uint? Shading(ResolvedProperty? shd)
    {
        if (shd is null || shd.Attribute("val") is null or "nil")
        {
            return null;
        }

        uint? fill = Hex(shd.Attribute("fill"));
        uint? color = Hex(shd.Attribute("color"));
        string val = shd.Attribute("val")!;
        if (val == "clear")
        {
            return fill;
        }

        uint fg = color ?? 0;
        uint bg = fill ?? 0xFFFFFF;
        if (val == "solid")
        {
            return fg;
        }

        double pct = val.StartsWith("pct", StringComparison.Ordinal) && int.TryParse(val.AsSpan(3), out int pv) ? pv / 100.0 : 0.5;
        uint Mix(int shift) => (uint)Math.Round((((fg >> shift) & 0xFF) * pct) + (((bg >> shift) & 0xFF) * (1 - pct)));
        return (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
    }

    private static uint? Hex(string? s) =>
        s is { Length: 6 } && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint v) ? v : null;

    // ---- cell content ----

    private static void Collect(IEnumerable<BlockElement> blocks, List<CellItem> into, LayoutContext ctx, double width, int depth)
    {
        foreach (BlockElement b in blocks)
        {
            switch (b)
            {
                case Paragraph p:
                    into.Add(new CellItem { Paragraph = ParagraphFormatter.Format(p, ctx, width) });
                    break;
                case BlockContentControl cc:
                    Collect(cc.Content.Blocks(), into, ctx, width, depth);
                    break;
                case Table t:
                    if (depth + 1 > MaxDepth)
                    {
                        ctx.NoteOnce("layout.table-depth", "layout.table-depth", "Tables nested deeper than " + MaxDepth + " levels are left out of the pages.", t);
                        break;
                    }

                    FormattedTable nested = Format(t, ctx, width, depth + 1);
                    if (nested.Rows.Count > 0)
                    {
                        into.Add(new CellItem { Table = nested });
                    }

                    break;
                default:
                    ctx.NoteOnce("layout.block-skipped", "layout.block-skipped", "A block that is not a paragraph or a table is left out of the pages.", b);
                    break;
            }
        }
    }

    private static void BuildContent(FormattedCell fc, LayoutContext ctx, int depth)
    {
        Collect(fc.Logical.Cell.Blocks(), fc.Items, ctx, fc.InnerWidth, depth);
        double y = 0;
        double pendingAfter = 0;
        FormattedParagraph? prev = null;
        for (int i = 0; i < fc.Items.Count; i++)
        {
            CellItem item = fc.Items[i];
            if (item.Paragraph is { } fp)
            {
                double before = fp.SpaceBefore;
                double after = pendingAfter;
                if (prev is not null && fp.StyleId == prev.StyleId)
                {
                    if (fp.ContextualSpacing)
                    {
                        before = 0;
                    }

                    if (prev.ContextualSpacing)
                    {
                        after = 0;
                    }
                }

                double ext = y;
                y += i == 0 ? before : after + before;
                for (int k = 0; k < fp.Lines.Count; k++)
                {
                    double top = y;
                    y += (k == 0 ? fp.BorderTopHeight : 0) + fp.Lines[k].Height;
                    if (k == fp.Lines.Count - 1)
                    {
                        y += fp.BorderBottomHeight;
                    }

                    fc.Units.Add(new CellUnit(i, k, k == 0 ? ext : top, top, y));
                }

                pendingAfter = fp.SpaceAfter;
                prev = fp;
            }
            else if (item.Table is { } nested)
            {
                double ext = y;
                y += pendingAfter;
                for (int k = 0; k < nested.Rows.Count; k++)
                {
                    double top = y;
                    y += nested.Rows[k].Height + (k == 0 ? nested.Spacing : 0) + nested.Spacing;
                    fc.Units.Add(new CellUnit(i, k, k == 0 ? ext : top, top, y));
                }

                pendingAfter = 0;
                prev = null;
            }
        }

        fc.ContentHeight = y + pendingAfter;
    }

    // ---- measuring ----

    private static (double Min, double Max) MeasureBlocks(IEnumerable<BlockElement> blocks, LayoutContext ctx, int depth)
    {
        double min = 0;
        double max = 0;
        foreach (BlockElement b in blocks)
        {
            switch (b)
            {
                case Paragraph p:
                    (double pmin, double pmax) = MeasureParagraph(p, ctx);
                    min = Math.Max(min, pmin);
                    max = Math.Max(max, pmax);
                    break;
                case BlockContentControl cc:
                    (double cmin, double cmax) = MeasureBlocks(cc.Content.Blocks(), ctx, depth);
                    min = Math.Max(min, cmin);
                    max = Math.Max(max, cmax);
                    break;
                case Table t when depth + 1 <= MaxDepth:
                    (double tmin, double tmax) = Intrinsic(t, ctx, depth);
                    min = Math.Max(min, tmin);
                    max = Math.Max(max, tmax);
                    break;
            }
        }

        return (min, max);
    }

    // The narrowest width is the longest stretch with no break opportunity (spaces, hyphens and East Asian characters break); the
    // widest is the longest line when nothing wraps. Both include the paragraph's indents.
    private static (double Min, double Max) MeasureParagraph(Paragraph p, LayoutContext ctx)
    {
        FormattedParagraph fp = ParagraphFormatter.Format(p, ctx, Unbounded);
        string text = fp.Text;
        var cw = new double[text.Length];
        double max = 0;
        foreach (FormattedLine line in fp.Lines)
        {
            double l = double.MaxValue;
            double r = double.MinValue;
            foreach (LineItem item in line.Items)
            {
                l = Math.Min(l, item.X);
                r = Math.Max(r, item.X + item.Width);
                FillCharWidths(item, cw);
            }

            if (l <= r)
            {
                // Centred or right-aligned text sits far from the left at this width; measure it by its own extent plus the indent.
                max = Math.Max(max, l > Unbounded / 4 ? r - l + fp.Paint.LeftEdge : r);
            }
        }

        double indents = fp.Paint.LeftEdge + (Unbounded - fp.Paint.RightEdge);
        max += Unbounded - fp.Paint.RightEdge;

        double best = 0;
        double cur = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch is ' ' or '\t' or '\n' or '\f')
            {
                best = Math.Max(best, cur);
                cur = 0;
                continue;
            }

            cur += cw[i];
            if (ch is '-' or '\u2010' or '\u2013' or '\u2014' || IsIdeograph(ch))
            {
                best = Math.Max(best, cur);
                cur = 0;
            }
        }

        best = Math.Max(best, cur);
        return (best + indents, Math.Max(max, best + indents));
    }

    private static bool IsIdeograph(char ch) => ch is >= '\u2E80' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF' or >= '\uF900' and <= '\uFAFF' or >= '\uFF00' and <= '\uFFEF';

    private static void FillCharWidths(LineItem item, double[] cw)
    {
        if (item.TextStart < 0 || item.TextStart >= cw.Length)
        {
            return;
        }

        if (item.Kind == LayoutRunKind.Tab)
        {
            cw[item.TextStart] = item.Width;
            return;
        }

        int len = Math.Min(item.Text.Length, cw.Length - item.TextStart);
        if (len <= 0)
        {
            return;
        }

        if (item.ClusterStarts.Length >= 2 && item.ClusterX.Length == item.ClusterStarts.Length)
        {
            for (int k = 0; k + 1 < item.ClusterStarts.Length; k++)
            {
                int s = item.ClusterStarts[k];
                int e = Math.Min(item.ClusterStarts[k + 1], len);
                if (e <= s)
                {
                    continue;
                }

                double w = Math.Abs(item.ClusterX[k + 1] - item.ClusterX[k]) / (e - s);
                for (int c = s; c < e; c++)
                {
                    cw[item.TextStart + c] = w;
                }
            }

            return;
        }

        for (int c = 0; c < len; c++)
        {
            cw[item.TextStart + c] = item.Width / len;
        }
    }
}
