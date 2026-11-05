using System.Globalization;
using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

internal sealed partial class RenderSession
{
    private Dictionary<string, MarkupNode>? _tableStyles;

    // ECMA-376 Part 1 §20.1.4.2.27 (graphicFrame content): a frame holds a table, chart, diagram or object by the uri of its graphicData.
    public bool PaintFrame(PartContext pc, MarkupNode frame, DisplayList list, double x, double y, double w, double h)
    {
        MarkupNode data = DrawingTheme.Child(DrawingTheme.Child(frame, "graphic"), "graphicData");
        if (data.IsNull)
        {
            return false;
        }

        MarkupNode tbl = DrawingTheme.Child(data, "tbl");
        if (!tbl.IsNull)
        {
            PaintTable(pc, tbl, list, x, y);
            return true;
        }

        return PaintDiagram(pc, frame, data, list, x, y, w, h);
    }

    // ---------------------------------------------------------------- table styles

    private MarkupNode TableStyle(PartContext pc, string? id)
    {
        if (id is null)
        {
            return default;
        }

        if (_tableStyles is null)
        {
            _tableStyles = new Dictionary<string, MarkupNode>(StringComparer.OrdinalIgnoreCase);
            PresentationPart main = pc.Part is Slide s ? s.Master ?? pc.Part : pc.Part;
            _ = main;
            foreach (Relationship r in Presentation.Package.GetRelationships(Presentation.MainPartName))
            {
                if (r.Type.EndsWith("/tableStyles", StringComparison.Ordinal) && OpcPackage.TryResolveTarget(Presentation.MainPartName, r, out PartUri t)
                    && Presentation.Package.FindPart(t.Value) is { } part
                    && MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? doc, Presentation.Context, t.Value) && doc is not null)
                {
                    _tableDocuments.Add(doc);
                    foreach (MarkupNode st in doc.Root.ChildElements)
                    {
                        if (st.LocalNameString == "tblStyle" && st.GetAttribute("styleId") is { } sid)
                        {
                            _tableStyles[sid] = st;
                        }
                    }
                }
            }
        }

        if (!_tableStyles.ContainsKey(id) && string.Equals(id, BuiltInStyleId, StringComparison.OrdinalIgnoreCase)
            && MarkupDocument.TryParse(System.Text.Encoding.UTF8.GetBytes(BuiltInMedium2Accent1), out MarkupDocument? builtIn, Presentation.Context, "builtin-table-style.xml") && builtIn is not null)
        {
            // A deck made by a generator often names the default table style without defining it; PowerPoint draws its built-in look.
            _tableDocuments.Add(builtIn);
            _tableStyles[id] = builtIn.Root;
        }

        return _tableStyles.TryGetValue(id, out MarkupNode style) ? style : default;
    }

    private const string BuiltInStyleId = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";

    // Medium Style 2 - Accent 1, the table style PowerPoint applies to a new table.
    private const string BuiltInMedium2Accent1 = "<a:tblStyle xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" styleId=\"{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}\" styleName=\"Medium Style 2 - Accent 1\"><a:wholeTbl><a:tcTxStyle><a:fontRef idx=\"minor\"><a:prstClr val=\"black\"/></a:fontRef><a:schemeClr val=\"dk1\"/></a:tcTxStyle><a:tcStyle><a:tcBdr><a:left><a:ln w=\"12700\" cmpd=\"sng\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill></a:ln></a:left><a:right><a:ln w=\"12700\" cmpd=\"sng\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill></a:ln></a:right><a:top><a:ln w=\"12700\" cmpd=\"sng\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill></a:ln></a:top><a:bottom><a:ln w=\"12700\" cmpd=\"sng\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill></a:ln></a:bottom><a:insideH><a:ln w=\"12700\" cmpd=\"sng\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill></a:ln></a:insideH><a:insideV><a:ln w=\"12700\" cmpd=\"sng\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill></a:ln></a:insideV></a:tcBdr><a:fill><a:solidFill><a:schemeClr val=\"accent1\"><a:tint val=\"20000\"/></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:wholeTbl><a:band1H><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:schemeClr val=\"accent1\"><a:tint val=\"40000\"/></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:band1H><a:band2H><a:tcStyle><a:tcBdr/></a:tcStyle></a:band2H><a:band1V><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:schemeClr val=\"accent1\"><a:tint val=\"40000\"/></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:band1V><a:band2V><a:tcStyle><a:tcBdr/></a:tcStyle></a:band2V><a:lastCol><a:tcTxStyle b=\"on\"><a:fontRef idx=\"minor\"><a:prstClr val=\"black\"/></a:fontRef><a:schemeClr val=\"lt1\"/></a:tcTxStyle><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:schemeClr val=\"accent1\"></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:lastCol><a:firstCol><a:tcTxStyle b=\"on\"><a:fontRef idx=\"minor\"><a:prstClr val=\"black\"/></a:fontRef><a:schemeClr val=\"lt1\"/></a:tcTxStyle><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:schemeClr val=\"accent1\"></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:firstCol><a:lastRow><a:tcTxStyle b=\"on\"><a:fontRef idx=\"minor\"><a:prstClr val=\"black\"/></a:fontRef><a:schemeClr val=\"lt1\"/></a:tcTxStyle><a:tcStyle><a:tcBdr><a:top><a:ln w=\"38100\" cmpd=\"sng\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill></a:ln></a:top></a:tcBdr><a:fill><a:solidFill><a:schemeClr val=\"accent1\"></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:lastRow><a:firstRow><a:tcTxStyle b=\"on\"><a:fontRef idx=\"minor\"><a:prstClr val=\"black\"/></a:fontRef><a:schemeClr val=\"lt1\"/></a:tcTxStyle><a:tcStyle><a:tcBdr><a:bottom><a:ln w=\"38100\" cmpd=\"sng\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill></a:ln></a:bottom></a:tcBdr><a:fill><a:solidFill><a:schemeClr val=\"accent1\"></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:firstRow></a:tblStyle>";

    private readonly List<MarkupDocument> _tableDocuments = [];

    private sealed class CellLook
    {
        public bool? Bold;
        public DrawingColor? TextColor;
        public MarkupNode Fill;
        public MarkupNode FillRef;
        public MarkupNode[] Borders = new MarkupNode[4]; // left, right, top, bottom: an a:ln or a:lnRef holder
    }

    private sealed class TableCellInfo
    {
        public MarkupNode Node;
        public int Row, Column, ColSpan = 1, RowSpan = 1;
        public bool Skip;
        public List<SlideTextParagraph> Paragraphs = [];
        public double MarginLeft = 7.2, MarginRight = 7.2, MarginTop = 3.6, MarginBottom = 3.6;
        public string Anchor = "t";
        public CellLook Look = new();
    }

    private void PaintTable(PartContext pc, MarkupNode tbl, DisplayList list, double x, double y)
    {
        // Columns and rows: ECMA-376 Part 1 §21.1.3.14 (tblGrid) and §21.1.3.15 (tr); sizes in EMU.
        var colW = new List<double>();
        foreach (MarkupNode gc in DrawingTheme.Child(tbl, "tblGrid").ChildElements)
        {
            if (gc.LocalNameString == "gridCol")
            {
                colW.Add(Math.Max(0, Number(gc.GetAttribute("w")) / 12700.0));
            }
        }

        var rowsXml = new List<MarkupNode>();
        foreach (MarkupNode tr in tbl.ChildElements)
        {
            if (tr.LocalNameString == "tr")
            {
                rowsXml.Add(tr);
            }
        }

        if (rowsXml.Count == 0 || colW.Count == 0)
        {
            return;
        }

        // A table is laid out cell by cell; a hostile grid of millions of cells is left out.
        if (rowsXml.Count > 2000 || colW.Count > 500 || (long)rowsXml.Count * colW.Count > 20_000)
        {
            AddNote("slide.table-limit", "A table with more than 20,000 cells (or 2,000 rows or 500 columns) is not painted.");
            return;
        }

        MarkupNode tblPr = DrawingTheme.Child(tbl, "tblPr");
        bool firstRow = Flag(tblPr, "firstRow"), lastRow = Flag(tblPr, "lastRow"), firstCol = Flag(tblPr, "firstCol"), lastCol = Flag(tblPr, "lastCol");
        bool bandRow = Flag(tblPr, "bandRow"), bandCol = Flag(tblPr, "bandCol");
        MarkupNode style = TableStyle(pc, DrawingTheme.Child(tblPr, "tableStyleId").InnerText.Trim());
        StyleReader styles = pc.Styles(null);

        var cells = new List<TableCellInfo>();
        int rowCount = rowsXml.Count, colCount = colW.Count;
        for (int r = 0; r < rowCount; r++)
        {
            int c = 0;
            foreach (MarkupNode tc in rowsXml[r].ChildElements)
            {
                if (tc.LocalNameString != "tc")
                {
                    continue;
                }

                if (c >= colCount)
                {
                    break;
                }

                var cell = new TableCellInfo { Node = tc, Row = r, Column = c };
                cell.ColSpan = Math.Clamp((int)Number(tc.GetAttribute("gridSpan"), 1), 1, colCount - c);
                cell.RowSpan = Math.Clamp((int)Number(tc.GetAttribute("rowSpan"), 1), 1, rowCount - r);
                cell.Skip = tc.GetAttribute("hMerge") is "1" or "true" || tc.GetAttribute("vMerge") is "1" or "true";
                MarkupNode tcPr = DrawingTheme.Child(tc, "tcPr");
                // §21.1.3.17 (tcPr): margins default to 0.1 inch left and right, 0.05 inch top and bottom.
                cell.MarginLeft = Number(tcPr.IsNull ? null : tcPr.GetAttribute("marL"), 91440) / 12700.0;
                cell.MarginRight = Number(tcPr.IsNull ? null : tcPr.GetAttribute("marR"), 91440) / 12700.0;
                cell.MarginTop = Number(tcPr.IsNull ? null : tcPr.GetAttribute("marT"), 45720) / 12700.0;
                cell.MarginBottom = Number(tcPr.IsNull ? null : tcPr.GetAttribute("marB"), 45720) / 12700.0;
                cell.Anchor = tcPr.IsNull ? "t" : tcPr.GetAttribute("anchor") ?? "t";
                cell.Look = LookOf(pc, style, tcPr, r, c, rowCount, colCount, firstRow, lastRow, firstCol, lastCol, bandRow, bandCol, cell);
                if (!cell.Skip)
                {
                    MarkupNode txBody = DrawingTheme.Child(tc, "txBody");
                    if (!txBody.IsNull)
                    {
                        cell.Paragraphs = TextBodyReader.Read(txBody, styles, Fields);
                        foreach (SlideTextParagraph p in cell.Paragraphs)
                        {
                            foreach (SlideTextRun run in p.Runs)
                            {
                                Restyle(run.Style, cell.Look);
                            }

                            Restyle(p.EndStyle, cell.Look);
                        }
                    }
                }

                cells.Add(cell);
                c++;
            }
        }

        // Row heights grow to hold the text: a row is at least as tall as its tr/@h and as its tallest single-row cell's text.
        var rowH = new double[rowCount];
        for (int r = 0; r < rowCount; r++)
        {
            rowH[r] = Math.Max(0, Number(rowsXml[r].GetAttribute("h")) / 12700.0);
        }

        var textHeight = new Dictionary<TableCellInfo, double>();
        foreach (TableCellInfo cell in cells)
        {
            if (cell.Skip)
            {
                continue;
            }

            double cw = ColumnsWidth(colW, cell.Column, cell.ColSpan) - cell.MarginLeft - cell.MarginRight;
            double th = 0;
            if (cw > 0 && cell.Paragraphs.Count > 0)
            {
                PlacedText placed = Layout.Layout(cell.Paragraphs, new TextLayoutSettings { Width = cw, Height = double.MaxValue / 4, Wrap = true });
                th = placed.Height;
                textHeight[cell] = th;
            }

            double need = th + cell.MarginTop + cell.MarginBottom;
            if (cell.RowSpan == 1)
            {
                rowH[cell.Row] = Math.Max(rowH[cell.Row], need);
            }
        }

        foreach (TableCellInfo cell in cells)
        {
            if (cell.Skip || cell.RowSpan == 1)
            {
                continue;
            }

            double have = 0;
            for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
            {
                have += rowH[r];
            }

            double need = (textHeight.TryGetValue(cell, out double th) ? th : 0) + cell.MarginTop + cell.MarginBottom;
            if (need > have)
            {
                rowH[cell.Row + cell.RowSpan - 1] += need - have;
            }
        }

        var colX = new double[colCount + 1];
        for (int c = 0; c < colCount; c++)
        {
            colX[c + 1] = colX[c] + colW[c];
        }

        var rowY = new double[rowCount + 1];
        for (int r = 0; r < rowCount; r++)
        {
            rowY[r + 1] = rowY[r] + rowH[r];
        }

        double ox = x / K, oy = y / K; // the frame's corner in points
        Matrix3x2 toPage = Matrix3x2.CreateScale((float)K);
        bool tagged = Tagged;
        if (tagged)
        {
            list.BeginTag(NextId(), TagRole.Table);
        }

        // Fills first, then text, then borders over both.
        foreach (TableCellInfo cell in cells)
        {
            if (cell.Skip)
            {
                continue;
            }

            double cx = ox + colX[cell.Column], cy = oy + rowY[cell.Row];
            double cw = colX[cell.Column + cell.ColSpan] - colX[cell.Column], ch = rowY[cell.Row + cell.RowSpan] - rowY[cell.Row];
            if (FillPaint(pc, cell.Look) is { } fill)
            {
                if (tagged)
                {
                    list.BeginTag(NextId(), TagRole.Artifact);
                }

                list.FillRect(NextId(), new RectF((float)(cx * K), (float)(cy * K), (float)(cw * K), (float)(ch * K)), fill);
                if (tagged)
                {
                    list.EndTag();
                }
            }
        }

        for (int r = 0; r < rowCount; r++)
        {
            if (tagged)
            {
                list.BeginTag(NextId(), TagRole.TableRow);
            }

            foreach (TableCellInfo cell in cells)
            {
                if (cell.Row != r || cell.Skip)
                {
                    continue;
                }

                double cx = ox + colX[cell.Column], cy = oy + rowY[cell.Row];
                double cw = colX[cell.Column + cell.ColSpan] - colX[cell.Column], ch = rowY[cell.Row + cell.RowSpan] - rowY[cell.Row];
                // PDF/UA needs header cells in every table: the first row is the header row whether or not the table marks it (firstRow only styles it).
                bool header = cell.Row == 0;
                if (tagged)
                {
                    list.BeginTag(NextId(), header ? TagRole.TableHeaderCell : TagRole.TableDataCell, columnSpan: cell.ColSpan, rowSpan: cell.RowSpan);
                }

                double tw = cw - cell.MarginLeft - cell.MarginRight;
                if (tw > 0 && cell.Paragraphs.Count > 0)
                {
                    PlacedText placed = Layout.Layout(cell.Paragraphs, new TextLayoutSettings { Width = tw, Height = ch, Wrap = true });
                    double avail = ch - cell.MarginTop - cell.MarginBottom;
                    double shift = cell.Anchor switch { "ctr" => (avail - placed.Height) / 2, "b" => avail - placed.Height, _ => 0 };
                    var options = new TextEmitter.Options { Tagged = tagged, LinkTarget = pc.LinkTarget, NextId = NextId };
                    TextEmitter.Emit(list, placed, Matrix3x2.CreateTranslation((float)(cx + cell.MarginLeft), (float)(cy + cell.MarginTop)) * toPage, 0, shift, options);
                }

                if (tagged)
                {
                    list.EndTag();
                }
            }

            if (tagged)
            {
                list.EndTag();
            }
        }

        if (tagged)
        {
            list.BeginTag(NextId(), TagRole.Artifact);
        }

        foreach (TableCellInfo cell in cells)
        {
            if (cell.Skip)
            {
                continue;
            }

            double cx = ox + colX[cell.Column], cy = oy + rowY[cell.Row];
            double cw = colX[cell.Column + cell.ColSpan] - colX[cell.Column], ch = rowY[cell.Row + cell.RowSpan] - rowY[cell.Row];
            PaintBorder(pc, list, cell.Look.Borders[0], cx, cy, 0, ch, true);
            PaintBorder(pc, list, cell.Look.Borders[1], cx + cw, cy, 0, ch, true);
            PaintBorder(pc, list, cell.Look.Borders[2], cx, cy, cw, 0, false);
            PaintBorder(pc, list, cell.Look.Borders[3], cx, cy + ch, cw, 0, false);
        }

        if (tagged)
        {
            list.EndTag();
            list.EndTag();
        }
    }

    private static double ColumnsWidth(List<double> colW, int start, int span)
    {
        double sum = 0;
        for (int i = start; i < start + span && i < colW.Count; i++)
        {
            sum += colW[i];
        }

        return sum;
    }

    private static double Number(string? s, double fallback = 0) =>
        s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : fallback;

    private static bool Flag(MarkupNode e, string name) => !e.IsNull && e.GetAttribute(name) is "1" or "true";

    // Table-style text properties apply to runs that do not set their own.
    private static void Restyle(SlideRunStyle run, CellLook look)
    {
        if (look.Bold == true && !run.BoldFromRun)
        {
            run.Bold = true;
        }

        if (look.TextColor is { } color && !run.ColorFromRun)
        {
            run.Color = color;
        }
    }

    // ECMA-376 Part 1 §20.1.4.2 (tblStyle) and §21.1.3.13 (tableStyleId): the style parts that apply to a cell, from the whole table up to
    // the corner cells; the later part wins, and the cell's own properties win over all of them.
    private static CellLook LookOf(PartContext pc, MarkupNode style, MarkupNode tcPr, int r, int c, int rows, int cols, bool firstRow, bool lastRow, bool firstCol, bool lastCol, bool bandRow, bool bandCol, TableCellInfo cell)
    {
        var look = new CellLook();
        if (!style.IsNull)
        {
            var parts = new List<string> { "wholeTbl" };
            int dataRow = r - (firstRow ? 1 : 0);
            if (bandRow && !(firstRow && r == 0) && !(lastRow && r == rows - 1))
            {
                parts.Add(dataRow % 2 == 0 ? "band1H" : "band2H");
            }

            int dataCol = c - (firstCol ? 1 : 0);
            if (bandCol && !(firstCol && c == 0) && !(lastCol && c == cols - 1))
            {
                parts.Add(dataCol % 2 == 0 ? "band1V" : "band2V");
            }

            if (lastCol && c == cols - 1)
            {
                parts.Add("lastCol");
            }

            if (firstCol && c == 0)
            {
                parts.Add("firstCol");
            }

            if (lastRow && r == rows - 1)
            {
                parts.Add("lastRow");
            }

            if (firstRow && r == 0)
            {
                parts.Add("firstRow");
            }

            foreach (string part in parts)
            {
                MarkupNode p = DrawingTheme.Child(style, part);
                if (p.IsNull)
                {
                    continue;
                }

                MarkupNode txStyle = DrawingTheme.Child(p, "tcTxStyle");
                if (!txStyle.IsNull)
                {
                    string? b = txStyle.GetAttribute("b");
                    if (b is "on") { look.Bold = true; }
                    else if (b is "off") { look.Bold = false; }
                    foreach (MarkupNode ch in txStyle.ChildElements)
                    {
                        if (ch.LocalNameString is "schemeClr" or "srgbClr" or "sysClr" && pc.Colors.Resolve(ch) is { } tcol)
                        {
                            look.TextColor = tcol;
                        }
                        else if (ch.LocalNameString == "fontRef")
                        {
                            DrawingColor? fc = pc.Colors.ResolveChild(ch);
                            if (fc is not null)
                            {
                                look.TextColor = fc;
                            }
                        }
                    }
                }

                MarkupNode tcStyle = DrawingTheme.Child(p, "tcStyle");
                if (!tcStyle.IsNull)
                {
                    MarkupNode fill = DrawingTheme.Child(tcStyle, "fill");
                    if (!fill.IsNull)
                    {
                        foreach (MarkupNode f in fill.ChildElements)
                        {
                            look.Fill = f;
                            look.FillRef = default;
                        }
                    }

                    MarkupNode fillRef = DrawingTheme.Child(tcStyle, "fillRef");
                    if (!fillRef.IsNull)
                    {
                        look.FillRef = fillRef;
                        look.Fill = default;
                    }

                    MarkupNode bdr = DrawingTheme.Child(tcStyle, "tcBdr");
                    SetBorder(look, 0, bdr, c == 0 ? "left" : "insideV");
                    SetBorder(look, 1, bdr, c + cell.ColSpan >= cols ? "right" : "insideV");
                    SetBorder(look, 2, bdr, r == 0 ? "top" : "insideH");
                    SetBorder(look, 3, bdr, r + cell.RowSpan >= rows ? "bottom" : "insideH");
                }
            }
        }

        if (!tcPr.IsNull)
        {
            foreach (MarkupNode ch in tcPr.ChildElements)
            {
                switch (ch.LocalNameString)
                {
                    case "lnL": look.Borders[0] = ch; break;
                    case "lnR": look.Borders[1] = ch; break;
                    case "lnT": look.Borders[2] = ch; break;
                    case "lnB": look.Borders[3] = ch; break;
                    case "noFill" or "solidFill" or "gradFill" or "pattFill" or "blipFill":
                        look.Fill = ch;
                        look.FillRef = default;
                        break;
                }
            }
        }

        return look;
    }

    private static void SetBorder(CellLook look, int side, MarkupNode bdr, string name)
    {
        MarkupNode e = DrawingTheme.Child(bdr, name);
        if (!e.IsNull)
        {
            // The holder is either a:ln or a:lnRef; both are read by PaintBorder.
            foreach (MarkupNode ch in e.ChildElements)
            {
                look.Borders[side] = ch;
            }
        }
    }

    private Paint? FillPaint(PartContext pc, CellLook look)
    {
        if (!look.Fill.IsNull)
        {
            switch (look.Fill.LocalNameString)
            {
                case "solidFill":
                    return pc.Colors.ResolveChild(look.Fill) is { } c ? Paint.FromColor(Color(c)) : null;
                case "gradFill":
                    foreach (MarkupNode gs in DrawingTheme.Child(look.Fill, "gsLst").ChildElements)
                    {
                        AddNote("slide.table-gradient-approximated", "A table cell with a gradient fill is filled with its first colour.");
                        return pc.Colors.ResolveChild(gs) is { } g ? Paint.FromColor(Color(g)) : null;
                    }

                    return null;
                case "noFill":
                    return null;
                default:
                    AddNote("slide.table-fill-approximated", "A table cell with a picture or pattern fill is left unfilled.");
                    return null;
            }
        }

        if (!look.FillRef.IsNull)
        {
            int idx = (int)Number(look.FillRef.GetAttribute("idx"));
            DrawingColor? ph = pc.Colors.ResolveChild(look.FillRef);
            if (idx == 0)
            {
                return null;
            }

            if (ph is { } phc && idx is >= 1 and <= 999)
            {
                IReadOnlyList<MarkupNode> fills = pc.Theme.FillStyles;
                if (idx - 1 < fills.Count && fills[idx - 1].LocalNameString == "solidFill")
                {
                    DrawingColor? c2 = pc.Colors.ResolveChild(fills[idx - 1], phc);
                    return c2 is { } cc ? Paint.FromColor(Color(cc)) : null;
                }

                return Paint.FromColor(Color(phc));
            }
        }

        return null;
    }

    private static PaintColor Color(DrawingColor c) => new((float)c.R, (float)c.G, (float)c.B, (float)c.A);

    // A border is drawn as a filled bar centred on the cell edge. Dashes are not drawn (the line is solid).
    private void PaintBorder(PartContext pc, DisplayList list, MarkupNode holder, double x, double y, double w, double h, bool vertical)
    {
        if (holder.IsNull)
        {
            return;
        }

        double width = 0.75;
        DrawingColor? color = null;
        MarkupNode ln = holder;
        if (holder.LocalNameString == "lnRef")
        {
            int idx = (int)Number(holder.GetAttribute("idx"));
            DrawingColor? ph = pc.Colors.ResolveChild(holder);
            if (idx < 1 || idx > pc.Theme.LineStyles.Count)
            {
                return;
            }

            ln = pc.Theme.LineStyles[idx - 1];
            color = ph;
            if (ln.GetAttribute("w") is { } lw)
            {
                width = Number(lw) / 12700.0;
            }
        }
        else if (holder.GetAttribute("w") is { } own)
        {
            width = Number(own) / 12700.0;
        }

        foreach (MarkupNode c in ln.ChildElements)
        {
            if (c.LocalNameString == "noFill")
            {
                return;
            }

            if (c.LocalNameString == "solidFill")
            {
                color = pc.Colors.ResolveChild(c, color);
            }
        }

        if (color is not { } col || width <= 0)
        {
            return;
        }

        width = Math.Max(width, 0.25);
        Paint paint = Paint.FromColor(Color(col));
        RectF rect = vertical
            ? new RectF((float)((x - (width / 2)) * K), (float)(y * K), (float)(width * K), (float)(h * K))
            : new RectF((float)(x * K), (float)((y - (width / 2)) * K), (float)(w * K), (float)(width * K));
        list.FillRect(NextId(), rect, paint);
    }
}
