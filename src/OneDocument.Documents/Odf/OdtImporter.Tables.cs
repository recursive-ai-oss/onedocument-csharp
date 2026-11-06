using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Tables: table:table -> w:tbl with a grid from the column definitions, spans from number-columns/rows-spanned and covered cells,
// header rows and the cell formatting (borders, background, padding, vertical alignment) read from the cell styles.
internal sealed partial class OdtImporter
{
    private const int MaxRepeat = 10_000;

    private sealed class TableColumn
    {
        public int Width;
        public double Relative;
    }

    private void EmitTable(Story s, MarkupNode table, ListCtx? list)
    {
        _depth++;
        try
        {
            CheckDepth(table);
            OdfStyle? style = A(table, OdfNamespaces.Table, "style-name") is { } sn ? _styles.Find("table", sn) : null;
            MarkupNode tp = style is null ? default : style.Properties("table-properties");

            var columns = new List<TableColumn>();
            var rows = new List<(MarkupNode Row, bool Header)>();
            Collect(table, columns, rows, header: false, 0);

            int gridCount = columns.Count;
            if (gridCount == 0)
            {
                foreach ((MarkupNode row, bool _) in rows)
                {
                    int n = 0;
                    foreach (MarkupNode c in row.ChildElements)
                    {
                        n += Math.Max(1, ParseInt(A(c, OdfNamespaces.Table, "number-columns-repeated"), 1) * ParseInt(A(c, OdfNamespaces.Table, "number-columns-spanned"), 1));
                    }

                    gridCount = Math.Max(gridCount, n);
                }

                for (int i = 0; i < gridCount; i++)
                {
                    columns.Add(new TableColumn());
                }
            }

            int available = PageContentWidth(_currentMaster);
            int tableWidth = OdfUnits.TryTwips(OdfMarkup.Attribute(tp, OdfNamespaces.Style, "width"), out int tw) && tw > 0 ? tw : 0;
            double relSum = 0;
            int fixedSum = 0;
            int unspecified = 0;
            foreach (TableColumn c in columns)
            {
                if (c.Width > 0)
                {
                    fixedSum += c.Width;
                }
                else if (c.Relative > 0)
                {
                    relSum += c.Relative;
                }
                else
                {
                    unspecified++;
                }
            }

            int total = tableWidth > 0 ? tableWidth : (relSum > 0 || unspecified > 0 ? available : fixedSum);
            // Columns with a relative width and columns with none share what the fixed ones leave, a column with none counting as one unit.
            double units = relSum + unspecified;
            int remaining = Math.Max(total - fixedSum, columns.Count * 200);
            foreach (TableColumn c in columns)
            {
                if (c.Width > 0)
                {
                    continue;
                }

                double share = c.Relative > 0 ? c.Relative : 1;
                c.Width = Math.Max(200, (int)(remaining * share / Math.Max(1, units)));
            }

            int gridTotal = 0;
            foreach (TableColumn c in columns)
            {
                gridTotal += c.Width;
            }

            var tblPr = new PropBag();
            tblPr.Set("tblW", "<w:tblW w:w=\"" + OdfUnits.I(tableWidth > 0 ? tableWidth : gridTotal) + "\" w:type=\"dxa\"/>");
            string align = OdfMarkup.Attribute(tp, OdfNamespaces.Table, "align") ?? "left";
            if (align is "center" or "right")
            {
                tblPr.Set("jc", X("jc", ("val", align)));
            }

            if (OdfUnits.TryTwips(OdfMarkup.Attribute(tp, OdfNamespaces.Fo, "margin-left"), out int ml) && ml != 0 && align is "left" or "margins")
            {
                tblPr.Set("tblInd", "<w:tblInd w:w=\"" + OdfUnits.I(ml) + "\" w:type=\"dxa\"/>");
            }

            tblPr.Set("tblLayout", "<w:tblLayout w:type=\"fixed\"/>");
            tblPr.Set("tblCellMar", "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar>");
            if (OdfMarkup.Attribute(tp, OdfNamespaces.Fo, "break-before") == "page")
            {
                Lost("loss.table-page-break", "page break before a table", "A page break before a table was dropped.");
            }

            s.Raw("<w:tbl><w:tblPr>" + tblPr.ToXml(WordOrder.TblPr) + "</w:tblPr><w:tblGrid>");
            foreach (TableColumn c in columns)
            {
                s.Raw("<w:gridCol w:w=\"" + OdfUnits.I(Math.Max(1, c.Width)) + "\"/>");
            }

            s.Raw("</w:tblGrid>");

            var rowSpanLeft = new int[gridCount + 1];
            var colSpanAt = new int[gridCount + 1];
            int emitted = 0;
            foreach ((MarkupNode row, bool header) in rows)
            {
                int repeat = Math.Clamp(ParseInt(A(row, OdfNamespaces.Table, "number-rows-repeated"), 1), 1, MaxRepeat);
                if (emitted + repeat > MaxRepeat)
                {
                    Defect(DefectKind.Limit, "odf.repeat-capped", DefectSeverity.Dropped, "A table repeats its rows more often than the reader keeps; the rest were dropped.", row);
                    repeat = Math.Max(0, MaxRepeat - emitted);
                }

                for (int r = 0; r < repeat; r++)
                {
                    EmitRow(s, row, header, columns, gridCount, rowSpanLeft, colSpanAt);
                    emitted++;
                }
            }

            if (rows.Count == 0)
            {
                // A table needs a row.
                s.Raw("<w:tr><w:tc><w:p/></w:tc></w:tr>");
            }

            s.Raw("</w:tbl>");
            s.EndsWithParagraph = false;
            _ = list;
        }
        finally
        {
            _depth--;
        }
    }

    private static int ParseInt(string? v, int fallback) =>
        int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : fallback;

    private void Collect(MarkupNode parent, List<TableColumn> columns, List<(MarkupNode, bool)> rows, bool header, int depth)
    {
        OfficeLimitExceeded(depth, parent);
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.NamespaceUri != OdfNamespaces.Table)
            {
                continue;
            }

            switch (c.LocalNameString)
            {
                case "table-column":
                {
                    int repeat = Math.Clamp(ParseInt(A(c, OdfNamespaces.Table, "number-columns-repeated"), 1), 1, 1024);
                    OdfStyle? cs = A(c, OdfNamespaces.Table, "style-name") is { } n ? _styles.Find("table-column", n) : null;
                    MarkupNode cp = cs is null ? default : cs.Properties("table-column-properties");
                    int width = OdfUnits.TryTwips(OdfMarkup.Attribute(cp, OdfNamespaces.Style, "column-width"), out int w) ? w : 0;
                    double rel = 0;
                    if (OdfMarkup.Attribute(cp, OdfNamespaces.Style, "rel-column-width") is { } rc && rc.EndsWith('*')
                        && double.TryParse(rc.AsSpan(0, rc.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double rv))
                    {
                        rel = rv;
                    }

                    for (int i = 0; i < repeat; i++)
                    {
                        columns.Add(new TableColumn { Width = width, Relative = rel });
                    }

                    break;
                }

                case "table-columns":
                case "table-header-columns":
                case "table-column-group":
                    Collect(c, columns, rows, header, depth + 1);
                    break;
                case "table-row":
                    rows.Add((c, header));
                    break;
                case "table-header-rows":
                    Collect(c, columns, rows, header: true, depth + 1);
                    break;
                case "table-rows":
                case "table-row-group":
                    Collect(c, columns, rows, header, depth + 1);
                    break;
            }
        }
    }

    private void EmitRow(Story s, MarkupNode row, bool header, List<TableColumn> columns, int gridCount, int[] rowSpanLeft, int[] colSpanAt)
    {
        var trPr = new StringBuilder();
        OdfStyle? rs = A(row, OdfNamespaces.Table, "style-name") is { } n ? _styles.Find("table-row", n) : null;
        MarkupNode rp = rs is null ? default : rs.Properties("table-row-properties");
        if (OdfMarkup.Attribute(rp, OdfNamespaces.Fo, "keep-together") == "always")
        {
            trPr.Append("<w:cantSplit/>");
        }

        if (OdfUnits.TryTwips(OdfMarkup.Attribute(rp, OdfNamespaces.Style, "row-height"), out int h) && h > 0)
        {
            trPr.Append("<w:trHeight w:val=\"").Append(OdfUnits.I(h)).Append("\" w:hRule=\"exact\"/>");
        }
        else if (OdfUnits.TryTwips(OdfMarkup.Attribute(rp, OdfNamespaces.Style, "min-row-height"), out int mh) && mh > 0)
        {
            trPr.Append("<w:trHeight w:val=\"").Append(OdfUnits.I(mh)).Append("\" w:hRule=\"atLeast\"/>");
        }

        if (header)
        {
            trPr.Append("<w:tblHeader/>");
        }

        s.Raw("<w:tr>");
        if (trPr.Length > 0)
        {
            s.Raw("<w:trPr>" + trPr + "</w:trPr>");
        }

        int col = 0;
        int skip = 0;
        int cells = 0;
        foreach (MarkupNode c in row.ChildElements)
        {
            bool covered = Is(c, OdfNamespaces.Table, "covered-table-cell");
            if (!covered && !Is(c, OdfNamespaces.Table, "table-cell"))
            {
                continue;
            }

            int repeat = Math.Clamp(ParseInt(A(c, OdfNamespaces.Table, "number-columns-repeated"), 1), 1, MaxRepeat);
            int colSpan = Math.Max(1, ParseInt(A(c, OdfNamespaces.Table, "number-columns-spanned"), 1));
            int rowSpan = Math.Max(1, ParseInt(A(c, OdfNamespaces.Table, "number-rows-spanned"), 1));
            for (int r = 0; r < repeat && col <= gridCount + 8 && cells < MaxRepeat; r++)
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                if (covered)
                {
                    if (col < rowSpanLeft.Length && rowSpanLeft[col] > 0)
                    {
                        int width = Math.Max(1, colSpanAt[col]);
                        rowSpanLeft[col]--;
                        EmitCell(s, c, columns, col, width, vMerge: "continue", rowSpan: 1);
                        col += width;
                        skip = width - 1;
                        cells++;
                        continue;
                    }

                    // A covered cell nothing spans over: an empty cell keeps the grid whole.
                    EmitCell(s, default, columns, col, 1, vMerge: null, rowSpan: 1);
                    col++;
                    cells++;
                    continue;
                }

                int span = Math.Min(colSpan, Math.Max(1, gridCount - col));
                EmitCell(s, c, columns, col, span, vMerge: rowSpan > 1 ? "restart" : null, rowSpan);
                if (rowSpan > 1 && col < rowSpanLeft.Length)
                {
                    rowSpanLeft[col] = rowSpan - 1;
                    colSpanAt[col] = span;
                }

                col += span;
                skip = colSpan - 1;
                cells++;
            }
        }

        if (cells == 0)
        {
            s.Raw("<w:tc><w:p/></w:tc>");
        }

        s.Raw("</w:tr>");
    }

    private void EmitCell(Story s, MarkupNode cell, List<TableColumn> columns, int col, int span, string? vMerge, int rowSpan)
    {
        _ = rowSpan;
        int width = 0;
        for (int i = col; i < col + span && i < columns.Count; i++)
        {
            width += columns[i].Width;
        }

        var tcPr = new PropBag();
        tcPr.Set("tcW", "<w:tcW w:w=\"" + OdfUnits.I(Math.Max(width, 1)) + "\" w:type=\"dxa\"/>");
        if (span > 1)
        {
            tcPr.Set("gridSpan", "<w:gridSpan w:val=\"" + OdfUnits.I(span) + "\"/>");
        }

        if (vMerge == "restart")
        {
            tcPr.Set("vMerge", "<w:vMerge w:val=\"restart\"/>");
        }
        else if (vMerge == "continue")
        {
            tcPr.Set("vMerge", "<w:vMerge/>");
        }

        if (!cell.IsNull && A(cell, OdfNamespaces.Table, "style-name") is { } sn && _styles.Find("table-cell", sn) is { } style)
        {
            ApplyCellStyle(tcPr, style);
        }

        s.Raw("<w:tc><w:tcPr>" + tcPr.ToXml(WordOrder.TcPr) + "</w:tcPr>");
        s.EndsWithParagraph = false;
        if (!cell.IsNull && vMerge != "continue")
        {
            foreach (MarkupNode c in cell.ChildElements)
            {
                EmitBlock(s, c, null, null);
            }
        }

        if (!s.EndsWithParagraph)
        {
            s.Raw("<w:p/>");
        }

        s.Raw("</w:tc>");
        s.EndsWithParagraph = false;
    }

    private void ApplyCellStyle(PropBag tcPr, OdfStyle style)
    {
        // Properties inherit along the style chain (an automatic cell style names a common one as its parent).
        MarkupNode merged = default;
        foreach (OdfStyle st in _styles.Chain(style))
        {
            MarkupNode cp = st.Properties("table-cell-properties");
            if (cp.IsNull)
            {
                continue;
            }

            merged = cp;
            break;
        }

        if (merged.IsNull)
        {
            return;
        }

        string borders = Borders(merged, "w:tcBorders");
        if (borders.Length > 0)
        {
            tcPr.Set("tcBorders", borders);
        }

        string bg = Hex(OdfMarkup.Attribute(merged, OdfNamespaces.Fo, "background-color"));
        if (bg.Length > 0)
        {
            tcPr.Set("shd", X("shd", ("val", "clear"), ("color", "auto"), ("fill", bg)));
        }

        string? pad = OdfMarkup.Attribute(merged, OdfNamespaces.Fo, "padding");
        var mar = new StringBuilder();
        foreach (string side in new[] { "top", "left", "bottom", "right" })
        {
            string? v = OdfMarkup.Attribute(merged, OdfNamespaces.Fo, "padding-" + side) ?? pad;
            if (v is not null && OdfUnits.TryTwips(v, out int t))
            {
                mar.Append("<w:").Append(side).Append(" w:w=\"").Append(OdfUnits.I(t)).Append("\" w:type=\"dxa\"/>");
            }
        }

        if (mar.Length > 0)
        {
            tcPr.Set("tcMar", "<w:tcMar>" + mar + "</w:tcMar>");
        }

        string? va = OdfMarkup.Attribute(merged, OdfNamespaces.Style, "vertical-align");
        if (va is "middle" or "bottom")
        {
            tcPr.Set("vAlign", X("vAlign", ("val", va == "middle" ? "center" : "bottom")));
        }
    }
}
