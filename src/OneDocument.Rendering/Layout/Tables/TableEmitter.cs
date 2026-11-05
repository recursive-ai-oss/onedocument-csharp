using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Layout;

// A row (or part of a row) of a table part and the cells drawn in it.
internal sealed class SliceRow
{
    public int Row;
    public double Height;
    public bool Repeated;
    public bool ContinuesFrom;
    public bool ContinuesOn;
}

// A cell's share of a table part: units U0 to U1 of its content, drawn in part rows FirstRow to LastRow. U1 of -1 means "decide when
// the part is closed" (a vertically merged cell whose rows are still being added).
internal sealed class SliceCell
{
    public required FormattedCell Cell;
    public int U0;
    public int U1;
    public int FirstRow;
    public int LastRow;
}

// Builds the layout nodes of one table part.
internal static class TableEmitter
{
    public static LayoutTable Emit(FormattedTable ft, IReadOnlyList<SliceRow> rows, IReadOnlyList<SliceCell> cells, double left, double top)
    {
        var table = new LayoutTable(ft.Source)
        {
            ColumnWidths = ft.ColumnWidths,
        };

        double s = ft.Spacing;
        var rowTop = new double[rows.Count];
        double y = top + s;
        for (int i = 0; i < rows.Count; i++)
        {
            rowTop[i] = y;
            y += rows[i].Height + s;
        }

        table.Bounds = new LayoutRect(left, top, ft.Width, y - top);
        var rowNodes = new LayoutTableRow[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var rn = new LayoutTableRow(rows[i].Row)
            {
                IsHeader = ft.Rows[rows[i].Row].IsHeader,
                IsRepeatedHeader = rows[i].Repeated,
                ContinuesFromPreviousPage = rows[i].ContinuesFrom,
                ContinuesOnNextPage = rows[i].ContinuesOn,
                Bounds = new LayoutRect(left, rowTop[i], ft.Width, rows[i].Height),
            };
            rowNodes[i] = rn;
            table.Add(rn);
        }

        foreach (SliceCell sc in cells)
        {
            FormattedCell fc = sc.Cell;
            double cx = left + (ft.Rtl ? ft.Width - (fc.X + fc.Width) : fc.X);
            double cy = rowTop[sc.FirstRow];
            double ch = rowTop[sc.LastRow] + rows[sc.LastRow].Height - cy;
            var node = new LayoutTableCell(fc.Logical.Cell, fc.Row, fc.Column, fc.ColumnSpan, fc.RowSpan)
            {
                IsHeader = fc.IsHeader,
                ContinuesFromPreviousPage = sc.U0 > 0,
                ContinuesOnNextPage = sc.U1 < fc.Units.Count,
                Bounds = new LayoutRect(cx, cy, fc.Width, ch),
                Paint = new CellPaintInfo
                {
                    Shading = fc.Shading,
                    Top = fc.Top,
                    Bottom = fc.Bottom,
                    Left = ft.Rtl ? fc.Right : fc.Left,
                    Right = ft.Rtl ? fc.Left : fc.Right,
                    Separate = ft.Spacing > 0,
                    PaintRight = fc.PaintRight,
                    PaintBottom = fc.PaintBottom,
                },
            };
            FillCell(node, fc, sc, cx + fc.PadLeft, cy + fc.PadTop, ch);
            rowNodes[sc.FirstRow].Add(node);
        }

        return table;
    }

    private static void FillCell(LayoutTableCell node, FormattedCell fc, SliceCell sc, double ox, double oy, double boxHeight)
    {
        if (fc.Units.Count == 0)
        {
            return;
        }

        double offset = 0;
        if (sc.U0 == 0 && sc.U1 >= fc.Units.Count && fc.VAlign is "center" or "bottom")
        {
            // ECMA-376 Part 1 §17.4.84 (vAlign): the content sits at the top, centre or bottom of the cell.
            double spare = boxHeight - fc.PadTop - fc.PadBottom - fc.Extent(0, fc.Units.Count);
            offset = Math.Max(0, fc.VAlign == "center" ? spare / 2 : spare);
        }

        double baseOffset = fc.Base(sc.U0);
        int u = sc.U0;
        while (u < sc.U1)
        {
            int item = fc.Units[u].Item;
            int v = u;
            while (v < sc.U1 && fc.Units[v].Item == item)
            {
                v++;
            }

            double top = oy + offset + (fc.Units[u].Top - baseOffset);
            CellItem ci = fc.Items[item];
            if (ci.Paragraph is { } fp)
            {
                int from = fc.Units[u].Index;
                int count = v - u;
                node.Add(ParagraphEmitter.Build(fp, from, count, ox, top, from == 0, from + count >= fp.Lines.Count, out _, out _));
            }
            else if (ci.Table is { } nested)
            {
                int r0 = fc.Units[u].Index;
                int r1 = fc.Units[v - 1].Index;
                node.Add(EmitFull(nested, r0, r1, ox + nested.Left, top));
            }

            u = v;
        }
    }

    // A nested table (or any run of whole rows): every cell that starts in the rows, drawn complete.
    public static LayoutTable EmitFull(FormattedTable ft, int r0, int r1, double left, double top)
    {
        var rows = new List<SliceRow>();
        for (int r = r0; r <= r1; r++)
        {
            rows.Add(new SliceRow { Row = r, Height = ft.Rows[r].Height });
        }

        var cells = new List<SliceCell>();
        foreach (FormattedCell fc in ft.Cells)
        {
            if (fc.Row >= r0 && fc.Row <= r1)
            {
                cells.Add(new SliceCell { Cell = fc, U0 = 0, U1 = fc.Units.Count, FirstRow = fc.Row - r0, LastRow = Math.Min(r1, fc.Row + fc.RowSpan - 1) - r0 });
            }
        }

        return Emit(ft, rows, cells, left, top);
    }
}
