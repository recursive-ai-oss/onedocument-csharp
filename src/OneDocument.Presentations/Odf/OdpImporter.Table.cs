using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// Tables: a table:table inside a frame becomes a graphic frame holding a:tbl.
internal sealed partial class OdpImporter
{
    private const int MaxTableColumns = 16384;
    private const int MaxTableRows = 1_048_576;

    // Repeat counts are attacker-controlled; a table is read up to this many cells (a table on a slide is read in a slide part, so the budget is smaller than for text tables).
    private const int MaxTableCells = 50_000;

    private static IEnumerable<MarkupNode> TableElements(MarkupNode table, string local, string group1, string group2)
    {
        foreach (MarkupNode c in table.ChildElements)
        {
            if (Is(c, OdfNamespaces.Table, local))
            {
                yield return c;
            }
            else if (Is(c, OdfNamespaces.Table, group1) || Is(c, OdfNamespaces.Table, group2))
            {
                foreach (MarkupNode d in c.ChildElements)
                {
                    if (Is(d, OdfNamespaces.Table, local))
                    {
                        yield return d;
                    }
                }
            }
        }
    }

    private static int Repeat(MarkupNode n, string attribute, int limit)
    {
        return int.TryParse(A(n, OdfNamespaces.Table, attribute), NumberStyles.None, CultureInfo.InvariantCulture, out int r) ? Math.Clamp(r, 1, limit) : 1;
    }

    private void WriteTableFrame(Scope scope, MarkupNode frame, MarkupNode table, Geom g, StringBuilder sb)
    {
        Part part = scope.Part;
        var columns = new List<(long Width, OdfStyle? Default)>();
        foreach (MarkupNode col in TableElements(table, "table-column", "table-columns", "table-header-columns"))
        {
            int repeat = Repeat(col, "number-columns-repeated", MaxTableColumns);
            OdfStyle? cs = A(col, OdfNamespaces.Table, "style-name") is { } sn ? _styles.Find("table-column", sn) : null;
            OdfStyle? dflt = A(col, OdfNamespaces.Table, "default-cell-style-name") is { } dn ? _styles.Find("table-cell", dn) : null;
            long width = cs is null ? 0 : OdpUnits.Emu(OdfMarkup.Attribute(cs.Properties("table-column-properties"), OdfNamespaces.Style, "column-width"));
            for (int i = 0; i < repeat && columns.Count < MaxTableColumns; i++)
            {
                columns.Add((width, dflt));
            }
        }

        if (columns.Count == 0)
        {
            Defect(DefectKind.Structure, "odp.empty-table", DefectSeverity.Dropped, "A table has no columns and was dropped.", table);
            return;
        }

        var headerRows = new HashSet<MarkupNode>();
        foreach (MarkupNode header in table.ChildElements)
        {
            if (Is(header, OdfNamespaces.Table, "table-header-rows"))
            {
                foreach (MarkupNode r in header.ChildElements)
                {
                    headerRows.Add(r);
                }
            }
        }

        long known = columns.Where(c => c.Width > 0).Sum(c => c.Width);
        int unknown = columns.Count(c => c.Width <= 0);
        long each = unknown > 0 ? Math.Max(1, (Math.Max(g.W, known) - known) / unknown) : 0;
        long total = 0;
        var widths = new long[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            widths[i] = columns[i].Width > 0 ? columns[i].Width : each;
            total += widths[i];
        }

        int id = part.NextId();
        var rows = new StringBuilder();
        int rowIndex = 0;
        int cellCount = 0;
        bool capped = false;
        var origin = new Dictionary<(int Row, int Col), (int Row, int Col)>();
        long heightSum = 0;
        foreach (MarkupNode row in TableElements(table, "table-row", "table-rows", "table-header-rows"))
        {
            _ct.ThrowIfCancellationRequested();
            int repeatRows = Repeat(row, "number-rows-repeated", 1000);
            for (int rr = 0; rr < repeatRows && !capped; rr++)
            {
                if (rowIndex >= MaxTableRows)
                {
                    break;
                }

                OdfStyle? rs = A(row, OdfNamespaces.Table, "style-name") is { } rn ? _styles.Find("table-row", rn) : null;
                MarkupNode rp = rs?.Properties("table-row-properties") ?? default;
                long height = OdpUnits.Emu(A(rp, OdfNamespaces.Style, "row-height") ?? A(rp, OdfNamespaces.Style, "min-row-height"), 370840);
                heightSum += height;
                OdfStyle? rowDefault = A(row, OdfNamespaces.Table, "default-cell-style-name") is { } rdn ? _styles.Find("table-cell", rdn) : null;
                rows.Append("<a:tr h=\"").Append(height).Append("\">");
                int col = 0;
                foreach (MarkupNode cell in row.ChildElements)
                {
                    bool covered = Is(cell, OdfNamespaces.Table, "covered-table-cell");
                    if (!covered && !Is(cell, OdfNamespaces.Table, "table-cell"))
                    {
                        continue;
                    }

                    int repeat = Repeat(cell, "number-columns-repeated", MaxTableColumns);
                    for (int k = 0; k < repeat && col < columns.Count; k++, col++)
                    {
                        if (++cellCount > MaxTableCells)
                        {
                            if (!capped)
                            {
                                Defect(DefectKind.Limit, "odf.repeat-capped", DefectSeverity.Dropped, "A table repeats rows or cells by counts that exceed the reader's budget (" + OdpUnits.I(MaxTableCells) + " cells); the rest were dropped.", table);
                            }

                            capped = true;
                            break;
                        }

                        int colSpan = covered ? 1 : Repeat(cell, "number-columns-spanned", MaxTableColumns);
                        int rowSpan = covered ? 1 : Repeat(cell, "number-rows-spanned", MaxTableRows);
                        colSpan = Math.Min(colSpan, columns.Count - col);
                        if (!covered && (colSpan > 1 || rowSpan > 1))
                        {
                            for (int dr = 0; dr < rowSpan && dr < 1000; dr++)
                            {
                                for (int dc = 0; dc < colSpan; dc++)
                                {
                                    if (dr != 0 || dc != 0)
                                    {
                                        origin[(rowIndex + dr, col + dc)] = (rowIndex, col);
                                    }
                                }
                            }
                        }

                        WriteTableCell(scope, sb: rows, cell, covered, colSpan, rowSpan, origin.TryGetValue((rowIndex, col), out var o) ? o : null, rowIndex, col, rowDefault ?? columns[col].Default, headerRows.Contains(row));
                    }
                }

                while (col < columns.Count)
                {
                    // A short row is completed with empty cells; PresentationML needs a cell for every grid column.
                    rows.Append("<a:tc><a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></a:txBody><a:tcPr/></a:tc>");
                    col++;
                }

                rows.Append("</a:tr>");
                rowIndex++;
            }
        }

        if (rowIndex == 0)
        {
            Defect(DefectKind.Structure, "odp.empty-table", DefectSeverity.Dropped, "A table has no rows and was dropped.", table);
            return;
        }

        bool firstRow = headerRows.Count > 0;
        long frameW = g.W > 0 ? g.W : total;
        long frameH = g.H > 0 ? g.H : heightSum;
        sb.Append("<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(OdpUnits.Esc(NameOf(frame, "Table", id))).Append('"').Append(AltAttribute(frame))
            .Append("/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"").Append(g.X).Append("\" y=\"").Append(g.Y)
            .Append("\"/><a:ext cx=\"").Append(frameW).Append("\" cy=\"").Append(frameH).Append("\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr")
            .Append(firstRow ? " firstRow=\"1\"" : "").Append("/><a:tblGrid>");
        foreach (long w in widths)
        {
            sb.Append("<a:gridCol w=\"").Append(w).Append("\"/>");
        }

        sb.Append("</a:tblGrid>").Append(rows).Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
    }

    private void WriteTableCell(Scope scope, StringBuilder sb, MarkupNode cell, bool covered, int colSpan, int rowSpan, (int Row, int Col)? origin, int row, int col, OdfStyle? defaultStyle, bool header)
    {
        _ = header;
        OdfStyle? cs = A(cell, OdfNamespaces.Table, "style-name") is { } sn ? _styles.Find("table-cell", sn) : defaultStyle;
        sb.Append("<a:tc");
        if (!covered && colSpan > 1)
        {
            sb.Append(" gridSpan=\"").Append(colSpan).Append('"');
        }

        if (!covered && rowSpan > 1)
        {
            sb.Append(" rowSpan=\"").Append(rowSpan).Append('"');
        }

        if (covered)
        {
            // ECMA-376 Part 1 §21.1.3.16 (a:tc): the cells a span covers are marked hMerge and vMerge.
            bool h = origin is { } o1 ? col > o1.Col : true;
            bool v = origin is { } o2 && row > o2.Row;
            if (h)
            {
                sb.Append(" hMerge=\"1\"");
            }

            if (v)
            {
                sb.Append(" vMerge=\"1\"");
            }
        }

        sb.Append("><a:txBody>");
        var style = new ShapeStyle(_styles, null, null, null, cs);
        var inner = new StringBuilder();
        var holder = new StringBuilder();
        WriteTextBody(scope, holder, cell, style, "p:txBody", placeholder: true);
        // The shared body writer wraps its output in a tag and a bodyPr; a cell wants the paragraphs only.
        string body = holder.ToString();
        int start = body.IndexOf("<a:lstStyle/>", StringComparison.Ordinal) + "<a:lstStyle/>".Length;
        int end = body.LastIndexOf("</p:txBody>", StringComparison.Ordinal);
        inner.Append("<a:bodyPr/><a:lstStyle/>").Append(body, start, end - start);
        sb.Append(inner).Append("</a:txBody>");
        sb.Append(CellProperties(cs, scope.Part, cell)).Append("</a:tc>");
    }

    private string CellProperties(OdfStyle? cs, Part part, MarkupNode at)
    {
        if (cs is null)
        {
            return "<a:tcPr/>";
        }

        MarkupNode props = cs.Properties("table-cell-properties");
        MarkupNode gprops = default;
        foreach (OdfStyle link in _styles.Chain(cs))
        {
            foreach (MarkupNode c in link.Element.ChildElements)
            {
                if (c.LocalNameString == "graphic-properties" && gprops.IsNull)
                {
                    gprops = c;
                }
            }

            if (!gprops.IsNull)
            {
                break;
            }
        }

        string? Prop(string ns, string name) => _styles.Resolve(cs, "table-cell-properties", ns, name);
        var sb = new StringBuilder("<a:tcPr");
        long Pad(string side) => OdpUnits.Emu(Prop(OdfNamespaces.Fo, "padding-" + side) ?? Prop(OdfNamespaces.Fo, "padding") ?? A(gprops, OdfNamespaces.Fo, "padding-" + side), -1);
        foreach ((string side, string attr) in new[] { ("left", "marL"), ("right", "marR"), ("top", "marT"), ("bottom", "marB") })
        {
            long p = Pad(side);
            if (p >= 0)
            {
                sb.Append(' ').Append(attr).Append("=\"").Append(p).Append('"');
            }
        }

        sb.Append(Prop(OdfNamespaces.Style, "vertical-align") switch { "middle" => " anchor=\"ctr\"", "bottom" => " anchor=\"b\"", "top" => " anchor=\"t\"", _ => "" }).Append('>');
        foreach ((string side, string tag) in new[] { ("left", "a:lnL"), ("right", "a:lnR"), ("top", "a:lnT"), ("bottom", "a:lnB") })
        {
            string? border = Prop(OdfNamespaces.Fo, "border-" + side) ?? Prop(OdfNamespaces.Fo, "border");
            if (border is not null)
            {
                sb.Append(BorderXml(tag, border));
            }
        }

        string? fill = A(gprops, OdfNamespaces.Draw, "fill");
        string? bg = Prop(OdfNamespaces.Fo, "background-color");
        if (fill is not null)
        {
            string xml = FillXml(fill, k => A(gprops, OdfNamespaces.Draw, k), k => A(gprops, OdfNamespaces.Fo, k), part, at);
            sb.Append(xml);
        }
        else if (OdpUnits.Color(bg) is { } hex)
        {
            sb.Append("<a:solidFill>").Append(Srgb(hex)).Append("</a:solidFill>");
        }
        else if (bg == "transparent")
        {
            sb.Append("<a:noFill/>");
        }

        _ = props;
        return sb.Append("</a:tcPr>").ToString();
    }

    // fo:border is "width style colour", for example "0.48pt solid #ffffff".
    private static string BorderXml(string tag, string border)
    {
        string[] parts = border.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] == "none" || parts.Contains("none") || parts.Contains("hidden"))
        {
            return "<" + tag + " w=\"0\"><a:noFill/></" + tag + ">";
        }

        long width = 12700;
        string color = "000000";
        string dash = "";
        foreach (string p in parts)
        {
            if (OdpUnits.TryEmu(p, out long w))
            {
                width = Math.Clamp(w, 0, 20116800);
            }
            else if (OdpUnits.Color(p) is { } c)
            {
                color = c;
            }
            else if (p == "dashed")
            {
                dash = "<a:prstDash val=\"dash\"/>";
            }
            else if (p == "dotted")
            {
                dash = "<a:prstDash val=\"sysDot\"/>";
            }
        }

        return "<" + tag + " w=\"" + OdpUnits.I(width) + "\"><a:solidFill>" + Srgb(color) + "</a:solidFill>" + dash + "</" + tag + ">";
    }
}
