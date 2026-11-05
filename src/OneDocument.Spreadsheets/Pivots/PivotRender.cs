using System.Linq;
using RecursiveAI.OneDocument.NumberFormats;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Spreadsheets.Pivots;

/// <summary>One cell to write.</summary>
internal sealed class CellOut
{
    public int Row;
    public int Column;
    public CellValue Value;
    public string? Text;
    public string? NumberFormat;
    public int Indent;
    public bool IsLabel;
}

/// <summary>The rendered table: its cells and the numbers the definition's <c>location</c> carries.</summary>
internal sealed class RenderedTable
{
    public int Top;
    public int Left;
    public int HeaderRows;
    public int LabelCols;
    public int FirstHeaderRow;
    public int FirstDataRow;
    public int FirstDataCol;
    public int Bottom;
    public int Right;
    public readonly List<CellOut> Cells = [];
    public CellRange Range => new(new CellAddress(Top, Left), new CellAddress(Bottom, Right));
    public int PageTop;
}

/// <summary>Turns a computed layout into cells (ECMA-376 Part 1 §18.10.1.49 <c>location</c> describes the geometry; the label conventions are Excel's).</summary>
internal static class PivotRenderer
{
    public static RenderedTable Render(TableModel m, CacheData data, PivotLayout lay, Workbook wb, int top, int left)
    {
        DateSystem ds = wb.DateSystem;
        int nd = m.Data.Count;
        AxisLayout rows = lay.Rows;
        AxisLayout cols = lay.Cols;
        var t = new RenderedTable();
        int np = m.Pages.Count;
        if (np > 0 && top < np + 2)
        {
            top = np + 2;
        }

        t.Top = top;
        t.Left = left;

        // Row label columns: consecutive compact levels share one column.
        int levelCount = rows.Levels.Length;
        var colOf = new int[Math.Max(levelCount, 1)];
        var indent = new int[Math.Max(levelCount, 1)];
        int nextCol = 0;
        for (int l = 0; l < levelCount; l++)
        {
            FieldForm f = lay.FormOf(rows, l, true);
            if (f == FieldForm.Compact && l > 0 && lay.FormOf(rows, l - 1, true) == FieldForm.Compact)
            {
                colOf[l] = colOf[l - 1];
                indent[l] = indent[l - 1] + 1;
            }
            else
            {
                colOf[l] = nextCol++;
            }
        }

        t.LabelCols = Math.Max(nextCol, 1);
        bool hasRealCol = cols.PureCount > 0;
        t.HeaderRows = hasRealCol ? 1 + cols.Levels.Length : 1;
        t.FirstDataRow = t.HeaderRows;
        t.FirstDataCol = t.LabelCols;
        t.FirstHeaderRow = hasRealCol ? 1 : nd > 1 ? 0 : 1;
        t.Bottom = top + t.HeaderRows + rows.Lines.Count - 1;
        t.Right = left + t.LabelCols + cols.Lines.Count - 1;
        t.PageTop = top - np - 1;

        void Put(int r, int c, CellValue v, string? text, string? fmt = null, int ind = 0, bool label = false) =>
            t.Cells.Add(new CellOut { Row = r, Column = c, Value = v, Text = text, NumberFormat = fmt, Indent = ind, IsLabel = label });

        void PutLabel(int r, int c, PivotValue v, int ind)
        {
            switch (v.Kind)
            {
                case PivotKind.Number:
                    Put(r, c, CellValue.FromNumber(v.Number), null, null, ind, true);
                    break;
                case PivotKind.Date:
                    Put(r, c, CellValue.FromNumber(v.Number), null, "m/d/yyyy", ind, true);
                    break;
                default:
                    Put(r, c, default, v.Label(ds), null, ind, true);
                    break;
            }
        }

        // Page fields sit above the table, one row each, with a blank row between them and the table.
        for (int k = 0; k < np; k++)
        {
            PageModel pm = m.Pages[k];
            FieldPlan fp = m.Fields[pm.Field];
            FieldData fd = data.Fields[pm.Field];
            int r = t.PageTop + k;
            Put(r, left, default, pm.Name ?? fp.Caption ?? fd.Name, null, 0, true);
            string shown = "(All)";
            if (pm.Selected >= 0 && pm.Selected < fp.Items.Count && fp.Items[pm.Selected].Axis >= 0)
            {
                shown = fd.Axis[fp.Items[pm.Selected].Axis].Label(ds);
            }
            else if (fp.MultipleSelection && fp.Items.Exists(i => i.Hidden))
            {
                shown = "(Multiple Items)";
            }

            Put(r, left + 1, default, shown, null, 0, true);
        }

        // Header: captions.
        int lastHeader = t.HeaderRows - 1;
        if (levelCount == 0)
        {
            // no row fields: no caption
        }
        else
        {
            for (int l = 0; l < levelCount; l++)
            {
                bool first = l == 0 || colOf[l] != colOf[l - 1];
                if (!first)
                {
                    continue;
                }

                string cap;
                if (lay.FormOf(rows, l, true) == FieldForm.Compact)
                {
                    cap = m.RowHeaderCaption ?? "Row Labels";
                }
                else
                {
                    cap = rows.Levels[l] == -2 ? m.DataCaption : m.Fields[rows.Levels[l]].Caption ?? data.Fields[rows.Levels[l]].Name;
                }

                Put(top + lastHeader, left + colOf[l], default, cap, null, 0, true);
            }
        }

        if (hasRealCol)
        {
            if (nd == 1)
            {
                Put(top, left, default, m.Data[0].Name, null, 0, true);
            }

            int firstCol = cols.Levels[0] >= 0 ? cols.Levels[0] : -1;
            string colCap = m.ColHeaderCaption ?? (firstCol >= 0 && !m.Fields[firstCol].Compact ? m.Fields[firstCol].Caption ?? data.Fields[firstCol].Name : "Column Labels");
            Put(top, left + t.LabelCols, default, colCap, null, 0, true);
        }

        // Column labels.
        for (int j = 0; j < cols.Lines.Count; j++)
        {
            Line cl = cols.Lines[j];
            int c = left + t.LabelCols + j;
            if (!hasRealCol)
            {
                if (cols.DataLevel >= 0 && cl.Path.Count > 0)
                {
                    Put(top, c, default, m.Data[cl.D].Name, null, 0, true);
                }
                else if (nd == 1 && j == 0)
                {
                    Put(top, c, default, m.Data[0].Name, null, 0, true);
                }

                continue;
            }

            LabelCells(cl, cols, m, data, lay, ds, (lv, v, text) =>
            {
                int r = top + 1 + lv;
                if (v is { } pv)
                {
                    PutLabel(r, c, pv, 0);
                }
                else
                {
                    Put(r, c, default, text, null, 0, true);
                }
            });
        }

        // Row labels.
        for (int i = 0; i < rows.Lines.Count; i++)
        {
            Line rl = rows.Lines[i];
            int r = top + t.HeaderRows + i;
            LabelCells(rl, rows, m, data, lay, ds, (lv, v, text) =>
            {
                int c = left + colOf[Math.Min(lv, colOf.Length - 1)];
                int ind = lay.FormOf(rows, Math.Min(lv, Math.Max(levelCount - 1, 0)), true) == FieldForm.Compact ? indent[Math.Min(lv, indent.Length - 1)] * m.Indent : 0;
                if (v is { } pv)
                {
                    PutLabel(r, c, pv, ind);
                }
                else
                {
                    Put(r, c, default, text, null, ind, true);
                }
            });
        }

        // Values.
        for (int i = 0; i < rows.Lines.Count; i++)
        {
            Line rl = rows.Lines[i];
            for (int j = 0; j < cols.Lines.Count; j++)
            {
                Line cl = cols.Lines[j];
                CellValue v = lay.Cell(rl, cl);
                int d = lay.DataIndex(rl, cl);
                int r = top + t.HeaderRows + i;
                int c = left + t.LabelCols + j;
                if (v.IsEmpty)
                {
                    if (rl.Valued && cl.Valued && d >= 0 && m.ShowMissing && !string.IsNullOrEmpty(m.MissingCaption))
                    {
                        Put(r, c, default, m.MissingCaption, null, 0, false);
                    }

                    continue;
                }

                if (v.Kind == CellValueKind.Error && m.ShowError && m.ErrorCaption is { } ec)
                {
                    Put(r, c, default, ec, null, 0, false);
                    continue;
                }

                Put(r, c, v, null, d >= 0 ? NumberFormatOf(m.Data[d], wb) : null, 0, false);
            }
        }

        return t;
    }

    private static string? NumberFormatOf(DataModel dm, Workbook wb)
    {
        if (dm.NumFmtId != 0)
        {
            return wb.Styles.GetNumberFormatCode(dm.NumFmtId);
        }

        return dm.ShowAs is PivotShowDataAs.Percent or PivotShowDataAs.PercentDiff or PivotShowDataAs.PercentOfRow or PivotShowDataAs.PercentOfCol
            or PivotShowDataAs.PercentOfTotal or PivotShowDataAs.PercentOfParentRow or PivotShowDataAs.PercentOfParentCol
            ? "0.00%"
            : null;
    }

    private static void LabelCells(Line line, AxisLayout axis, TableModel m, CacheData data, PivotLayout lay, DateSystem ds, Action<int, PivotValue?, string?> emit)
    {
        if (line.Kind != LineKind.Item)
        {
            string text = line.Caption ?? string.Empty;
            emit(line.Shown.Count > 0 ? line.Shown[0] : 0, null, text);
            return;
        }

        // The node of each pure entry of the path, from the root down.
        var chain = new List<AxisNode>();
        for (AxisNode? n = line.Node; n is { Parent: not null }; n = n.Parent)
        {
            chain.Insert(0, n);
        }

        int pure = 0;
        var nodeAt = new AxisNode?[line.Path.Count];
        for (int k = 0; k < line.Path.Count; k++)
        {
            if (!line.Path[k].IsData && pure < chain.Count)
            {
                nodeAt[k] = chain[pure++];
            }
        }

        foreach (int lv in line.Shown)
        {
            if (lv >= line.Path.Count)
            {
                continue;
            }

            if (line.Path[lv].IsData)
            {
                emit(lv, null, m.Data[line.Path[lv].V].Name);
            }
            else if (nodeAt[lv] is { } node)
            {
                emit(lv, lay.LabelOf(node), null);
            }
        }

        _ = axis;
        _ = data;
        _ = ds;
    }
}

/// <summary>Writes a rendered table into the sheet and its definition.</summary>
internal static class PivotWriter
{
    public static void WriteCells(PivotTable table, RenderedTable t, CellRange oldRange, int oldPageCount)
    {
        Worksheet ws = table.Sheet;
        if (oldRange.IsValid)
        {
            int top = Math.Max(1, oldRange.TopLeft.Row - (oldPageCount > 0 ? oldPageCount + 1 : 0));
            for (int r = top; r <= oldRange.BottomRight.Row; r++)
            {
                for (int c = oldRange.TopLeft.Column; c <= oldRange.BottomRight.Column; c++)
                {
                    if (r < oldRange.TopLeft.Row && c > oldRange.TopLeft.Column + 1)
                    {
                        break;
                    }

                    ws[r, c].Clear();
                }
            }
        }

        foreach (CellOut o in t.Cells)
        {
            if (o.Row < 1 || o.Column < 1 || o.Row > CellAddress.MaxRows || o.Column > CellAddress.MaxColumns)
            {
                continue;
            }

            Cell cell = ws[o.Row, o.Column];
            cell.Clear();
            if (o.Text is not null)
            {
                cell.SetString(o.Text);
            }
            else
            {
                switch (o.Value.Kind)
                {
                    case CellValueKind.Number:
                        cell.SetNumber(o.Value.Number);
                        break;
                    case CellValueKind.Error:
                        cell.SetError(o.Value.Error);
                        break;
                    case CellValueKind.Boolean:
                        cell.SetBoolean(o.Value.Boolean);
                        break;
                    default:
                        continue;
                }
            }

            if (o.NumberFormat is not null || o.Indent > 0)
            {
                cell.SetStyle(new CellStyleSpec
                {
                    NumberFormatCode = o.NumberFormat,
                    Alignment = o.Indent > 0 ? new CellAlignment(CellHorizontalAlignment.Left, Indent: o.Indent) : null,
                });
            }
        }
    }

    /// <summary>
    /// Rewrites the derived parts of the definition: the field items, row and column items, location and the field lists the refresh changed.
    /// Everything else, unknown elements and attributes included, stays where it was.
    /// </summary>
    public static void WriteDefinition(TableModel m, CacheData data, PivotLayout lay, RenderedTable t, Func<int, int> oldToNew, bool remapped)
    {
        PivotTable table = m.Table;
        MarkupNode root = table.Root;
        string pc = PivotCacheWriter.PrefixOf(root);
        DateSystem ds = table.Sheet.Workbook.DateSystem;
        _ = ds;

        // pivotFields: one per cache field, in cache order.
        var stored = new List<MarkupNode>();
        foreach (MarkupNode f in PivotCache.Child(root, "pivotFields").ChildElements)
        {
            if (f.LocalName.SequenceEqual("pivotField"u8))
            {
                stored.Add(f);
            }
        }

        var xml = new StringBuilder();
        xml.Append('<').Append(pc).Append("pivotFields count=\"").Append(PivotXml.Int(data.Fields.Count)).Append("\">");
        for (int ni = 0; ni < data.Fields.Count; ni++)
        {
            FieldPlan fp = m.Fields[ni];
            int oi = -1;
            for (int i = 0; i < stored.Count; i++)
            {
                if (oldToNew(i) == ni)
                {
                    oi = i;
                    break;
                }
            }

            MarkupNode node = oi >= 0 ? stored[oi] : default;
            xml.Append('<').Append(pc).Append("pivotField");
            bool hasAxis = false;
            bool hasDataField = false;
            if (!node.IsNull)
            {
                foreach (MarkupAttributeRef a in node.Attributes)
                {
                    string raw = Encoding.UTF8.GetString(a.RawName);
                    if (raw == "axis")
                    {
                        hasAxis = true;
                        continue;
                    }

                    if (raw == "dataField")
                    {
                        hasDataField = true;
                        continue;
                    }

                    xml.Append(' ').Append(raw).Append("=\"").Append(Encoding.UTF8.GetString(a.RawValue)).Append('"');
                }
            }
            else
            {
                xml.Append(" showAll=\"0\"");
            }

            _ = hasAxis;
            _ = hasDataField;
            string? axis = fp.Axis switch { FieldAxis.Row => "axisRow", FieldAxis.Col => "axisCol", FieldAxis.Page => "axisPage", _ => null };
            if (axis is not null)
            {
                xml.Append(" axis=\"").Append(axis).Append('"');
            }

            if (m.Data.Exists(d => d.Field == ni))
            {
                xml.Append(" dataField=\"1\"");
            }

            bool hasChildren = fp.HasItems || (!node.IsNull && node.ChildElements.GetEnumerator().MoveNext());
            if (!hasChildren)
            {
                xml.Append("/>");
                continue;
            }

            xml.Append('>');
            if (fp.HasItems)
            {
                xml.Append('<').Append(pc).Append("items count=\"").Append(PivotXml.Int(fp.Items.Count)).Append("\">");
                foreach (ItemPlan ip in fp.Items)
                {
                    xml.Append('<').Append(pc).Append("item");
                    if (ip.Axis >= 0)
                    {
                        xml.Append(" x=\"").Append(PivotXml.Int(ip.Axis)).Append('"');
                    }

                    xml.Append(ip.Attrs).Append("/>");
                }

                xml.Append("</").Append(pc).Append("items>");
            }

            if (!node.IsNull)
            {
                foreach (MarkupNode c in node.ChildElements)
                {
                    if (!c.LocalName.SequenceEqual("items"u8))
                    {
                        xml.Append(c.OuterXml);
                    }
                }
            }

            xml.Append("</").Append(pc).Append("pivotField>");
        }

        xml.Append("</").Append(pc).Append("pivotFields>");
        SetChild(root, "pivotFields", xml.ToString());

        // rowFields / colFields: regenerate only when the field list changed.
        if (remapped || m.AddedDataField)
        {
            SetAxisFields(root, "rowFields", m.RowFields, pc);
            SetAxisFields(root, "colFields", m.ColFields, pc);
        }

        // pageFields: carry the selected item through the refresh.
        MarkupNode pages = PivotCache.Child(root, "pageFields");
        if (!pages.IsNull)
        {
            var sb = new StringBuilder();
            sb.Append('<').Append(pc).Append("pageFields count=\"").Append(PivotXml.Int(m.Pages.Count)).Append("\">");
            int k = 0;
            foreach (MarkupNode p in pages.ChildElements)
            {
                int f = oldToNew(PivotXml.IntAttr(p.GetAttribute("fld"), -1));
                if (f < 0 || f >= data.Fields.Count)
                {
                    continue;
                }

                PageModel pm = m.Pages[k++];
                sb.Append('<').Append(pc).Append("pageField");
                foreach (MarkupAttributeRef a in p.Attributes)
                {
                    string raw = Encoding.UTF8.GetString(a.RawName);
                    if (raw is "fld" or "item")
                    {
                        continue;
                    }

                    sb.Append(' ').Append(raw).Append("=\"").Append(Encoding.UTF8.GetString(a.RawValue)).Append('"');
                }

                sb.Append(" fld=\"").Append(PivotXml.Int(f)).Append('"');
                if (pm.Selected >= 0)
                {
                    sb.Append(" item=\"").Append(PivotXml.Int(pm.Selected)).Append('"');
                }

                sb.Append("/>");
            }

            sb.Append("</").Append(pc).Append("pageFields>");
            pages.ReplaceWithXml(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        if (remapped)
        {
            RemapDataFields(root, oldToNew, data.Fields.Count, pc);
        }

        SetChild(root, "rowItems", ItemsXml(pc, "rowItems", lay.Rows));
        SetChild(root, "colItems", ItemsXml(pc, "colItems", lay.Cols));

        MarkupNode loc = PivotCache.Child(root, "location");
        string locXml = "<" + pc + "location ref=\"" + t.Range + "\" firstHeaderRow=\"" + PivotXml.Int(t.FirstHeaderRow) + "\" firstDataRow=\"" + PivotXml.Int(t.FirstDataRow)
            + "\" firstDataCol=\"" + PivotXml.Int(t.FirstDataCol) + "\"" + (m.Pages.Count > 0 ? " rowPageCount=\"" + PivotXml.Int(m.Pages.Count) + "\" colPageCount=\"1\"" : string.Empty) + "/>";
        if (loc.IsNull)
        {
            SetChild(root, "location", locXml);
        }
        else
        {
            loc.SetAttribute("ref", t.Range.ToString());
            loc.SetAttribute("firstHeaderRow", PivotXml.Int(t.FirstHeaderRow));
            loc.SetAttribute("firstDataRow", PivotXml.Int(t.FirstDataRow));
            loc.SetAttribute("firstDataCol", PivotXml.Int(t.FirstDataCol));
            if (m.Pages.Count > 0)
            {
                loc.SetAttribute("rowPageCount", PivotXml.Int(m.Pages.Count));
                loc.SetAttribute("colPageCount", "1");
            }
            else
            {
                loc.RemoveAttribute("rowPageCount");
                loc.RemoveAttribute("colPageCount");
            }
        }
    }

    private static void SetAxisFields(MarkupNode root, string local, List<int> fields, string pc)
    {
        if (fields.Count == 0)
        {
            MarkupNode existing = PivotCache.Child(root, local);
            if (!existing.IsNull)
            {
                existing.Remove();
            }

            return;
        }

        var sb = new StringBuilder();
        sb.Append('<').Append(pc).Append(local).Append(" count=\"").Append(PivotXml.Int(fields.Count)).Append("\">");
        foreach (int f in fields)
        {
            sb.Append('<').Append(pc).Append("field x=\"").Append(PivotXml.Int(f)).Append("\"/>");
        }

        sb.Append("</").Append(pc).Append(local).Append('>');
        SetChild(root, local, sb.ToString());
    }

    private static void RemapDataFields(MarkupNode root, Func<int, int> oldToNew, int n, string pc)
    {
        MarkupNode df = PivotCache.Child(root, "dataFields");
        if (df.IsNull)
        {
            return;
        }

        var sb = new StringBuilder();
        int count = 0;
        var body = new StringBuilder();
        foreach (MarkupNode d in df.ChildElements)
        {
            int f = oldToNew(PivotXml.IntAttr(d.GetAttribute("fld"), -1));
            if (f < 0 || f >= n)
            {
                continue;
            }

            count++;
            body.Append('<').Append(pc).Append("dataField");
            foreach (MarkupAttributeRef a in d.Attributes)
            {
                string raw = Encoding.UTF8.GetString(a.RawName);
                if (raw is "fld" or "baseField")
                {
                    continue;
                }

                body.Append(' ').Append(raw).Append("=\"").Append(Encoding.UTF8.GetString(a.RawValue)).Append('"');
            }

            body.Append(" fld=\"").Append(PivotXml.Int(f)).Append('"');
            if (d.GetAttribute("baseField") is { } bf)
            {
                int nb = oldToNew(PivotXml.IntAttr(bf, 0));
                body.Append(" baseField=\"").Append(PivotXml.Int(nb < 0 ? 0 : nb)).Append('"');
            }

            body.Append("/>");
        }

        sb.Append('<').Append(pc).Append("dataFields count=\"").Append(PivotXml.Int(count)).Append("\">").Append(body).Append("</").Append(pc).Append("dataFields>");
        df.ReplaceWithXml(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    // ECMA-376 Part 1 §18.10.1.73 (CT_pivotTableDefinition): the order of the child elements.
    private static readonly string[] Order =
    [
        "location", "pivotFields", "rowFields", "rowItems", "colFields", "colItems", "pageFields", "dataFields", "formats", "conditionalFormats",
        "chartFormats", "pivotHierarchies", "pivotTableStyleInfo", "filters", "rowHierarchiesUsage", "colHierarchiesUsage", "extLst",
    ];

    private static void SetChild(MarkupNode root, string local, string xml)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        MarkupNode existing = PivotCache.Child(root, local);
        if (!existing.IsNull)
        {
            existing.ReplaceWithXml(bytes);
            return;
        }

        int idx = Array.IndexOf(Order, local);
        foreach (MarkupNode c in root.ChildElements)
        {
            if (Array.IndexOf(Order, c.LocalNameString) > idx)
            {
                c.InsertXmlBefore(bytes);
                return;
            }
        }

        root.AppendXml(bytes);
    }

    /// <summary>ECMA-376 Part 1 §18.10.1.80 (<c>rowItems</c>), §18.10.1.43 (<c>i</c>): <c>r</c> repeats the leading items of the previous entry, <c>t</c> marks subtotals and grand totals, <c>i</c> picks the data field.</summary>
    private static string ItemsXml(string pc, string local, AxisLayout axis)
    {
        var sb = new StringBuilder();
        sb.Append('<').Append(pc).Append(local).Append(" count=\"").Append(PivotXml.Int(axis.Lines.Count)).Append("\">");
        List<PathEntry>? prev = null;
        foreach (Line l in axis.Lines)
        {
            int repeat = 0;
            if (l.Kind != LineKind.Grand && prev is not null)
            {
                while (repeat < prev.Count && repeat < l.Path.Count - 1 && prev[repeat] == l.Path[repeat])
                {
                    repeat++;
                }
            }

            sb.Append('<').Append(pc).Append('i');
            if (repeat > 0)
            {
                sb.Append(" r=\"").Append(PivotXml.Int(repeat)).Append('"');
            }

            if (l.Kind == LineKind.Subtotal)
            {
                sb.Append(" t=\"default\"");
            }
            else if (l.Kind == LineKind.Grand)
            {
                sb.Append(" t=\"grand\"");
            }

            if (l.D > 0)
            {
                sb.Append(" i=\"").Append(PivotXml.Int(l.D)).Append('"');
            }

            if (l.Path.Count == 0)
            {
                sb.Append("/>");
            }
            else
            {
                sb.Append('>');
                for (int k = repeat; k < l.Path.Count; k++)
                {
                    int v = l.Path[k].V;
                    sb.Append('<').Append(pc).Append('x');
                    if (v != 0)
                    {
                        sb.Append(" v=\"").Append(PivotXml.Int(v)).Append('"');
                    }

                    sb.Append("/>");
                }

                sb.Append("</").Append(pc).Append("i>");
            }

            prev = l.Kind == LineKind.Grand ? null : l.Path;
        }

        sb.Append("</").Append(pc).Append(local).Append('>');
        return sb.ToString();
    }
}
