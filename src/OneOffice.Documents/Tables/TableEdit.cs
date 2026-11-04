using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;
using W = RecursiveAI.OneOffice.Schema.Wordprocessing;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>Table edits. Each changes the table's own markup only, and keeps properties, unknown markup and the other rows exactly as they were.</summary>
public sealed partial class Table
{
    /// <summary>Gets the physical cell that holds the content at a grid position: the top-left cell of a merged region.</summary>
    /// <param name="row">The zero-based row.</param>
    /// <param name="column">The zero-based grid column.</param>
    /// <returns>The cell, or null when the position is outside the table or empty.</returns>
    public TableCell? GetCell(int row, int column) => LogicalTable.Build(this, report: false)[row, column]?.Cell;

    /// <summary>
    /// Replaces the content of the cell at a grid position with one paragraph holding the text. The first paragraph's properties and its
    /// first run's formatting are kept; every other block of the cell (further paragraphs, nested tables) is removed. A line feed becomes a
    /// line break and a tab a tab character.
    /// </summary>
    /// <param name="row">The zero-based row.</param>
    /// <param name="column">The zero-based grid column; a merged region is addressed by any of its positions.</param>
    /// <param name="text">The new text.</param>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the table or empty.</exception>
    public void SetCellText(int row, int column, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TableCell cell = GetCell(row, column) ?? throw new ArgumentOutOfRangeException(nameof(column), "There is no cell at this grid position.");
        MarkupRef tc = cell.Container;
        MarkupRef? keep = null;
        foreach (MarkupRef child in WmlEdit.ElementList(tc))
        {
            if (keep is null && Wml.Is(child, "p"u8))
            {
                keep = child;
            }
            else if (!Wml.Is(child, "tcPr"u8))
            {
                child.Node.Remove();
            }
        }

        string p = WmlEdit.Qualified(tc, "p");
        if (keep is null)
        {
            tc.Node.AppendXml(WmlEdit.Utf8("<" + p + "/>"));
            keep = Wml.Elements(tc).Last();
        }

        MarkupRef? rPr = null;
        foreach (MarkupRef run in Wml.Elements(keep.Value))
        {
            if (Wml.Is(run, "r"u8) && Wml.Child(run, "rPr"u8) is { } rp)
            {
                rPr = rp;
                break;
            }
        }

        string rPrXml = rPr?.Node.OuterXml ?? string.Empty;
        foreach (MarkupRef child in WmlEdit.ElementList(keep.Value))
        {
            if (!Wml.Is(child, "pPr"u8))
            {
                child.Node.Remove();
            }
        }

        if (text.Length > 0)
        {
            keep.Value.Node.AppendXml(WmlEdit.Utf8(RunXml(keep.Value, rPrXml, text)));
        }
    }

    private static string RunXml(MarkupRef near, string rPrXml, string text)
    {
        string r = WmlEdit.Qualified(near, "r"), t = WmlEdit.Qualified(near, "t"), br = WmlEdit.Qualified(near, "br"), tab = WmlEdit.Qualified(near, "tab");
        var sb = new StringBuilder();
        sb.Append('<').Append(r).Append('>').Append(rPrXml);
        var seg = new StringBuilder();
        void Flush()
        {
            if (seg.Length > 0)
            {
                sb.Append('<').Append(t).Append(" xml:space=\"preserve\">").Append(WmlEdit.Escape(seg.ToString())).Append("</").Append(t).Append('>');
                seg.Clear();
            }
        }

        foreach (char c in text)
        {
            switch (c)
            {
                case '\n':
                    Flush();
                    sb.Append('<').Append(br).Append("/>");
                    break;
                case '\t':
                    Flush();
                    sb.Append('<').Append(tab).Append("/>");
                    break;
                case '\r':
                    break;
                default:
                    seg.Append(c);
                    break;
            }
        }

        Flush();
        sb.Append("</").Append(r).Append('>');
        return sb.ToString();
    }

    /// <summary>
    /// Inserts an empty row so that it becomes row <paramref name="index"/>. The new row copies the layout and properties of the row above
    /// it (the first row when inserting at the top) with empty cells that keep the first paragraph's properties. Vertical merges stay
    /// consistent: the new row's cell continues a merged region exactly when the cell above and the cell below belong to one.
    /// </summary>
    /// <param name="index">The new row's zero-based position, from 0 to <see cref="Table.RowCount"/>.</param>
    /// <returns>The new row.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside 0..RowCount.</exception>
    /// <exception cref="InvalidOperationException">The table has no row to copy.</exception>
    public TableRow InsertRow(int index)
    {
        LogicalTable lt = LogicalTable.Build(this, report: false);
        List<TableRow> rows = [.. Rows()];
        if ((uint)index > (uint)rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("The table has no row to copy.");
        }

        int templateIndex = index > 0 ? index - 1 : 0;
        TableRow template = rows[templateIndex];
        byte[] xml = WmlEdit.Utf8(template.Markup.Node.OuterXml);
        MarkupNode newNode;
        if (index > 0)
        {
            template.Markup.Node.InsertXmlAfter(xml);
            newNode = template.Markup.Node.NextSibling;
        }
        else
        {
            template.Markup.Node.InsertXmlBefore(xml);
            newNode = template.Markup.Node.PreviousSibling;
        }

        while (!newNode.IsNull && newNode.Kind != MarkupNodeKind.Element)
        {
            newNode = index > 0 ? newNode.NextSibling : newNode.PreviousSibling;
        }

        var row = new MarkupRef(newNode);
        int col = template.GridBefore;
        foreach (MarkupRef tc in WmlEdit.ElementList(row))
        {
            if (!Wml.Is(tc, "tc"u8))
            {
                continue;
            }

            int start = col;
            col += new TableCell(Owner, tc).GridSpan;
            ClearCell(tc);
            bool continues = index > 0 && index < lt.RowCount && lt[index, start] is { } below && below.Row < index;
            SetVerticalMerge(tc, continues ? CellMerge.Continue : CellMerge.None);
            WmlEdit.RemoveAttr(tc, "hMerge");
        }

        // Revision marks on the source row do not belong to the copy. A header flag is kept: the copy extends the header block.
        if (Wml.Child(row, "trPr"u8) is { } trPr)
        {
            foreach (MarkupRef e in WmlEdit.ElementList(trPr))
            {
                if (Wml.Is(e, "ins"u8) || Wml.Is(e, "del"u8) || Wml.Is(e, "trPrChange"u8))
                {
                    e.Node.Remove();
                }
            }
        }

        return new TableRow(Owner, row, this);
    }

    /// <summary>
    /// Removes a row. When the row holds the first cell of a vertical merge that continues below, the cell beneath becomes the start of
    /// the region, so the rest of the region survives (its content stays in the removed cell and goes with it).
    /// </summary>
    /// <param name="index">The zero-based row.</param>
    /// <exception cref="ArgumentOutOfRangeException">The row does not exist.</exception>
    /// <exception cref="InvalidOperationException">It is the only row; a table needs one.</exception>
    public void DeleteRow(int index)
    {
        LogicalTable lt = LogicalTable.Build(this, report: false);
        List<TableRow> rows = [.. Rows()];
        if ((uint)index >= (uint)rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (rows.Count == 1)
        {
            throw new InvalidOperationException("The only row of a table cannot be deleted.");
        }

        foreach (LogicalCell cell in lt.Cells)
        {
            if (cell.Row == index && cell.RowSpan > 1)
            {
                // The next row's covered cell takes over as the region's start.
                TableCell next = cell.Covered[0];
                SetVerticalMerge(next.Container, CellMerge.Restart);
            }
        }

        rows[index].Markup.Node.Remove();
    }

    /// <summary>
    /// Inserts an empty grid column so that it becomes column <paramref name="column"/>. A cell that spans across the insertion point gets
    /// wider; every other row gets a new empty cell that copies the properties of its neighbour (without merges). A grid column is added
    /// to <c>w:tblGrid</c> with the width of the column to its left (right at the start).
    /// </summary>
    /// <param name="column">The new column's zero-based position, from 0 to <see cref="LogicalTable.ColumnCount"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="column"/> is outside 0..ColumnCount.</exception>
    public void InsertColumn(int column)
    {
        LogicalTable lt = LogicalTable.Build(this, report: false);
        if ((uint)column > (uint)lt.ColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        IReadOnlyList<long> widths = GridColumns;
        long width = widths.Count == 0 ? 0 : widths[Math.Clamp(column > 0 ? column - 1 : 0, 0, widths.Count - 1)];
        List<TableRow> rows = [.. Rows()];
        for (int r = 0; r < rows.Count; r++)
        {
            InsertColumnInRow(rows[r], column, width);
        }

        // Grid: pad a grid that is shorter than the rows first so that the index lines up.
        MarkupRef? grid = Wml.Child(Markup, "tblGrid"u8);
        if (grid is { } g)
        {
            string gc = WmlEdit.Qualified(g, "gridCol");
            while (Wml.Elements(g).Count(e => Wml.Is(e, "gridCol"u8)) < lt.ColumnCount)
            {
                g.Node.AppendXml(WmlEdit.Utf8("<" + gc + " " + WmlEdit.Qualified(g, "w") + "=\"" + width + "\"/>"));
            }

            List<MarkupRef> cols = [.. Wml.Elements(g).Where(e => Wml.Is(e, "gridCol"u8))];
            string xml = "<" + gc + " " + WmlEdit.Qualified(g, "w") + "=\"" + width + "\"/>";
            if (column < cols.Count)
            {
                cols[column].Node.InsertXmlBefore(WmlEdit.Utf8(xml));
            }
            else if (cols.Count > 0)
            {
                cols[^1].Node.InsertXmlAfter(WmlEdit.Utf8(xml));
            }
            else
            {
                g.Node.AppendXml(WmlEdit.Utf8(xml));
            }
        }
    }

    private static void InsertColumnInRow(TableRow row, int column, long width)
    {
        int col = row.GridBefore;
        if (column < col)
        {
            SetRowCount(row, "gridBefore", row.GridBefore + 1);
            return;
        }

        MarkupRef? left = null;
        MarkupRef? firstRight = null;
        foreach (TableCell cell in row.Cells())
        {
            int span = cell.GridSpan;
            if (column > col && column < col + span)
            {
                SetGridSpan(cell.Container, span + 1);
                return;
            }

            if (col >= column)
            {
                firstRight = cell.Container;
                break;
            }

            left = cell.Container;
            col += span;
        }

        if (firstRight is null && col < column)
        {
            // The position lies in the empty grid columns after the last cell.
            SetRowCount(row, "gridAfter", row.GridAfter + 1);
            return;
        }

        MarkupRef neighbour = firstRight ?? left ?? default;
        if (!neighbour.IsValid)
        {
            return;
        }

        string tc = WmlEdit.Qualified(neighbour, "tc"), tcPr = WmlEdit.Qualified(neighbour, "tcPr"), tcW = WmlEdit.Qualified(neighbour, "tcW"),
            p = WmlEdit.Qualified(neighbour, "p"), w = WmlEdit.Qualified(neighbour, "w"), type = WmlEdit.Qualified(neighbour, "type");
        string xml = "<" + tc + "><" + tcPr + "><" + tcW + " " + w + "=\"" + width + "\" " + type + "=\"dxa\"/></" + tcPr + "><" + p + "/></" + tc + ">";
        if (firstRight is { } fr)
        {
            fr.Node.InsertXmlBefore(WmlEdit.Utf8(xml));
        }
        else
        {
            left!.Value.Node.InsertXmlAfter(WmlEdit.Utf8(xml));
        }
    }

    /// <summary>
    /// Removes a grid column. A cell that spans it gets narrower; a cell that covers only that column is removed. The column leaves
    /// <c>w:tblGrid</c>. The last remaining column cannot be removed.
    /// </summary>
    /// <param name="column">The zero-based grid column.</param>
    /// <exception cref="ArgumentOutOfRangeException">The column does not exist.</exception>
    /// <exception cref="InvalidOperationException">It is the only column.</exception>
    public void DeleteColumn(int column)
    {
        LogicalTable lt = LogicalTable.Build(this, report: false);
        if ((uint)column >= (uint)lt.ColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        if (lt.ColumnCount == 1)
        {
            throw new InvalidOperationException("The only column of a table cannot be deleted.");
        }

        foreach (TableRow row in Rows())
        {
            int col = row.GridBefore;
            if (column < col)
            {
                SetRowCount(row, "gridBefore", col - 1);
                continue;
            }

            bool done = false;
            foreach (TableCell cell in row.Cells())
            {
                int span = cell.GridSpan;
                if (column >= col && column < col + span)
                {
                    if (span > 1)
                    {
                        SetGridSpan(cell.Container, span - 1);
                    }
                    else
                    {
                        cell.Container.Node.Remove();
                    }

                    done = true;
                    break;
                }

                col += span;
            }

            if (!done && row.GridAfter > 0)
            {
                SetRowCount(row, "gridAfter", row.GridAfter - 1);
            }
        }

        if (Wml.Child(Markup, "tblGrid"u8) is { } g)
        {
            List<MarkupRef> cols = [.. Wml.Elements(g).Where(e => Wml.Is(e, "gridCol"u8))];
            if (column < cols.Count)
            {
                cols[column].Node.Remove();
            }
        }
    }

    // --- helpers ---

    private static void ClearCell(MarkupRef tc)
    {
        MarkupRef? firstParagraph = null;
        foreach (MarkupRef child in WmlEdit.ElementList(tc))
        {
            if (firstParagraph is null && Wml.Is(child, "p"u8))
            {
                firstParagraph = child;
            }
            else if (!Wml.Is(child, "tcPr"u8))
            {
                child.Node.Remove();
            }
        }

        if (firstParagraph is not { } p)
        {
            tc.Node.AppendXml(WmlEdit.Utf8("<" + WmlEdit.Qualified(tc, "p") + "/>"));
            return;
        }

        WmlEdit.StripParagraphIds(p);
        foreach (MarkupRef child in WmlEdit.ElementList(p))
        {
            if (!Wml.Is(child, "pPr"u8))
            {
                child.Node.Remove();
            }
            else if (Wml.Child(child, "sectPr"u8) is { } sect)
            {
                sect.Node.Remove();
            }
        }
    }

    private static void SetVerticalMerge(MarkupRef tc, CellMerge merge)
    {
        W.TableCell view = W.TableCell.Create(tc);
        if (merge == CellMerge.None)
        {
            view.TableCellProperties?.RemoveVerticalMerge();
            return;
        }

        MarkupRef v = view.GetOrAddTableCellProperties().GetOrAddVerticalMerge().Markup;
        if (merge == CellMerge.Restart)
        {
            WmlEdit.SetAttr(v, "val", "restart");
        }
        else
        {
            WmlEdit.RemoveAttr(v, "val");
        }
    }

    private static void SetGridSpan(MarkupRef tc, int span)
    {
        W.TableCell view = W.TableCell.Create(tc);
        if (span <= 1)
        {
            view.TableCellProperties?.RemoveGridSpan();
            return;
        }

        WmlEdit.SetAttr(view.GetOrAddTableCellProperties().GetOrAddGridSpan().Markup, "val", span.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void SetRowCount(TableRow row, string name, int count)
    {
        W.TableRowProperties props = row.View.GetOrAddTableRowProperties();
        MarkupRef e = name == "gridBefore" ? props.GetOrAddGridBefore().Markup : props.GetOrAddGridAfter().Markup;
        if (count <= 0)
        {
            if (name == "gridBefore")
            {
                props.RemoveGridBefore();
            }
            else
            {
                props.RemoveGridAfter();
            }

            return;
        }

        WmlEdit.SetAttr(e, "val", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
