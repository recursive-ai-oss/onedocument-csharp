using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Tables and drawings.
internal sealed partial class OdtExporter
{
    private sealed class CellInfo
    {
        public MarkupNode Node;
        public int Start;
        public int Span = 1;
        public string? VMerge;
        public int RowSpan = 1;
    }

    private void Table(StringBuilder sb, MarkupNode tbl)
    {
        _depth++;
        try
        {
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, _depth, DefectLocation.InPart(_part, Math.Max(0, tbl.SourceStart)));
            MarkupNode tblPr = Child(tbl, "tblPr");
            string? styleId = A(Child(tblPr, "tblStyle"), "val");
            var grid = new List<int>();
            foreach (MarkupNode g in Child(tbl, "tblGrid").ChildElements)
            {
                if (IsW(g, "gridCol"))
                {
                    grid.Add(Math.Max(1, Int(A(g, "w"), 1)));
                }
            }

            var rows = new List<MarkupNode>();
            foreach (MarkupNode r in tbl.ChildElements)
            {
                if (IsW(r, "tr"))
                {
                    rows.Add(r);
                }
            }

            // Grid positions of the cells (ECMA-376 Part 1 §17.4.17 gridSpan, §17.4.84 vMerge).
            var infos = new List<List<CellInfo>>();
            int maxCols = grid.Count;
            foreach (MarkupNode r in rows)
            {
                var list = new List<CellInfo>();
                int col = Int(A(Child(Child(r, "trPr"), "gridBefore"), "val"));
                foreach (MarkupNode tc in r.ChildElements)
                {
                    if (!IsW(tc, "tc"))
                    {
                        continue;
                    }

                    MarkupNode tcPr = Child(tc, "tcPr");
                    var ci = new CellInfo
                    {
                        Node = tc,
                        Start = col,
                        Span = Math.Max(1, Int(A(Child(tcPr, "gridSpan"), "val"), 1)),
                        VMerge = Child(tcPr, "vMerge") is { IsNull: false } vm ? (A(vm, "val") == "restart" ? "restart" : "continue") : null,
                    };
                    col += ci.Span;
                    list.Add(ci);
                }

                maxCols = Math.Max(maxCols, col);
                infos.Add(list);
            }

            while (grid.Count < maxCols)
            {
                grid.Add(grid.Count > 0 ? grid[^1] : 1000);
            }

            for (int r = 0; r < infos.Count; r++)
            {
                foreach (CellInfo ci in infos[r])
                {
                    if (ci.VMerge != "restart")
                    {
                        continue;
                    }

                    int span = 1;
                    for (int k = r + 1; k < infos.Count; k++)
                    {
                        CellInfo? below = infos[k].FirstOrDefault(x => x.Start == ci.Start && x.VMerge == "continue");
                        if (below is null)
                        {
                            break;
                        }

                        span++;
                    }

                    ci.RowSpan = span;
                }
            }

            int total = System.Linq.Enumerable.Sum(grid);
            string name = "Table" + OdfUnits.I(++_tableCount);
            MarkupNode tblW = Child(tblPr, "tblW");
            string wType = A(tblW, "type") ?? "auto";
            int wVal = Int(A(tblW, "w"));
            string tProps = " style:width=\"" + Cm(wType == "dxa" && wVal > 0 ? wVal : total) + "\"";
            string jc = A(Child(tblPr, "jc"), "val") ?? "left";
            tProps += " table:align=\"" + (jc is "center" ? "center" : jc is "right" or "end" ? "right" : "left") + "\"";
            int tblInd = Int(A(Child(tblPr, "tblInd"), "w"));
            if (tblInd != 0 && jc is not ("center" or "right" or "end"))
            {
                tProps = tProps.Replace("table:align=\"left\"", "table:align=\"margins\"", StringComparison.Ordinal) + " fo:margin-left=\"" + Cm(tblInd) + "\"";
            }

            string? master = TakeMaster();
            string tStyle = AddAuto("table", "Tbl", null, master is null ? string.Empty : " style:master-page-name=\"" + master + "\"", "<style:table-properties" + tProps + "/>");
            sb.Append("<table:table table:name=\"").Append(name).Append("\" table:style-name=\"").Append(tStyle).Append("\">");
            foreach (int gw in grid)
            {
                string cs = AddAuto("table-column", "Col", null, string.Empty, "<style:table-column-properties style:column-width=\"" + Cm(gw) + "\"/>");
                sb.Append("<table:table-column table:style-name=\"").Append(cs).Append("\"/>");
            }

            MarkupNode borders = Child(tblPr, "tblBorders");
            if (borders.IsNull)
            {
                borders = StyleTableBorders(styleId);
            }

            MarkupNode cellMar = Child(tblPr, "tblCellMar");
            if (cellMar.IsNull)
            {
                cellMar = StyleTableCellMar(styleId);
            }

            bool inHeader = false;
            for (int r = 0; r < rows.Count; r++)
            {
                MarkupNode trPr = Child(rows[r], "trPr");
                bool header = !Child(trPr, "tblHeader").IsNull && OnChild(trPr, "tblHeader");
                if (header && !inHeader)
                {
                    sb.Append("<table:table-header-rows>");
                    inHeader = true;
                }
                else if (!header && inHeader)
                {
                    sb.Append("</table:table-header-rows>");
                    inHeader = false;
                }

                string rowAttrs = string.Empty;
                MarkupNode th = Child(trPr, "trHeight");
                if (!th.IsNull && Int(A(th, "val")) > 0)
                {
                    rowAttrs += A(th, "hRule") == "exact" ? " style:row-height=\"" + Cm(Int(A(th, "val"))) + "\"" : " style:min-row-height=\"" + Cm(Int(A(th, "val"))) + "\"";
                }

                if (!Child(trPr, "cantSplit").IsNull && OnChild(trPr, "cantSplit"))
                {
                    rowAttrs += " fo:keep-together=\"always\"";
                }

                sb.Append("<table:table-row");
                if (rowAttrs.Length > 0)
                {
                    sb.Append(" table:style-name=\"").Append(AddAuto("table-row", "Row", null, string.Empty, "<style:table-row-properties" + rowAttrs + "/>")).Append('"');
                }

                sb.Append('>');
                int gridBefore = Int(A(Child(trPr, "gridBefore"), "val"));
                for (int i = 0; i < gridBefore; i++)
                {
                    sb.Append("<table:covered-table-cell/>");
                }

                foreach (CellInfo ci in infos[r])
                {
                    if (ci.VMerge == "continue")
                    {
                        for (int i = 0; i < ci.Span; i++)
                        {
                            sb.Append("<table:covered-table-cell/>");
                        }

                        continue;
                    }

                    string cellStyle = CellStyle(ci, r, infos.Count, borders, cellMar);
                    sb.Append("<table:table-cell table:style-name=\"").Append(cellStyle).Append('"');
                    if (ci.Span > 1)
                    {
                        sb.Append(" table:number-columns-spanned=\"").Append(OdfUnits.I(ci.Span)).Append('"');
                    }

                    if (ci.RowSpan > 1)
                    {
                        sb.Append(" table:number-rows-spanned=\"").Append(OdfUnits.I(ci.RowSpan)).Append('"');
                    }

                    sb.Append(" office:value-type=\"string\">");
                    var content = new StringBuilder();
                    string[] savedPending = [_pendingMaster ?? string.Empty];
                    SubContext(() =>
                    {
                        foreach (MarkupNode k in ci.Node.ChildElements)
                        {
                            Block(content, k);
                        }

                        CloseLists(content);
                    });
                    _ = savedPending;
                    sb.Append(content.Length == 0 ? "<text:p/>" : content.ToString()).Append("</table:table-cell>");
                    for (int i = 1; i < ci.Span; i++)
                    {
                        sb.Append("<table:covered-table-cell/>");
                    }
                }

                sb.Append("</table:table-row>");
            }

            if (inHeader)
            {
                sb.Append("</table:table-header-rows>");
            }

            if (rows.Count == 0)
            {
                sb.Append("<table:table-row><table:table-cell><text:p/></table:table-cell></table:table-row>");
            }

            sb.Append("</table:table>");
        }
        finally
        {
            _depth--;
        }
    }

    private MarkupNode StyleTableBorders(string? styleId) => StyleProp(styleId, "tblPr", "tblBorders");

    private MarkupNode StyleTableCellMar(string? styleId) => StyleProp(styleId, "tblPr", "tblCellMar");

    private string CellStyle(CellInfo ci, int row, int rowCount, MarkupNode tblBorders, MarkupNode tblCellMar)
    {
        MarkupNode tcPr = Child(ci.Node, "tcPr");
        MarkupNode tcBorders = Child(tcPr, "tcBorders");
        var attrs = new StringBuilder();
        void Attr(string n, string v) => attrs.Append(' ').Append(n).Append("=\"").Append(Esc(v)).Append('"');
        foreach (string side in new[] { "top", "left", "bottom", "right" })
        {
            MarkupNode b = Child(tcBorders, side);
            if (b.IsNull && side == "left")
            {
                b = Child(tcBorders, "start");
            }

            if (b.IsNull && side == "right")
            {
                b = Child(tcBorders, "end");
            }

            if (b.IsNull && !tblBorders.IsNull)
            {
                string key = side switch
                {
                    "top" => row == 0 ? "top" : "insideH",
                    "bottom" => row == rowCount - 1 ? "bottom" : "insideH",
                    "left" => ci.Start == 0 ? "left" : "insideV",
                    _ => "right",
                };
                b = Child(tblBorders, key);
                if (b.IsNull && key == "left")
                {
                    b = Child(tblBorders, "start");
                }
            }

            if (!b.IsNull)
            {
                Attr("fo:border-" + side, BorderValue(b));
            }
        }

        MarkupNode shd = Child(tcPr, "shd");
        if (IsHexColor(A(shd, "fill")))
        {
            Attr("fo:background-color", Lower(A(shd, "fill")));
        }

        MarkupNode mar = Child(tcPr, "tcMar");
        if (mar.IsNull)
        {
            mar = tblCellMar;
        }

        int top = 0, bottom = 0, left = 108, right = 108;
        if (!mar.IsNull)
        {
            top = Int(A(Child(mar, "top"), "w"));
            bottom = Int(A(Child(mar, "bottom"), "w"));
            MarkupNode l = Child(mar, "left");
            MarkupNode r = Child(mar, "right");
            left = Int(A(l.IsNull ? Child(mar, "start") : l, "w"));
            right = Int(A(r.IsNull ? Child(mar, "end") : r, "w"));
        }

        Attr("fo:padding-top", Cm(top));
        Attr("fo:padding-bottom", Cm(bottom));
        Attr("fo:padding-left", Cm(left));
        Attr("fo:padding-right", Cm(right));
        string? va = A(Child(tcPr, "vAlign"), "val");
        Attr("style:vertical-align", va switch { "center" => "middle", "bottom" => "bottom", _ => "top" });
        return AddAuto("table-cell", "Cell", null, string.Empty, "<style:table-cell-properties" + attrs + "/>");
    }

    // ---- drawings ------------------------------------------------------------------------------------------------------------------------

    private string Drawing(MarkupNode container)
    {
        string local = container.LocalNameString;
        if (local == "drawing")
        {
            foreach (MarkupNode k in container.ChildElements)
            {
                if (k.NamespaceUri is Wp or WpStrict && k.LocalNameString is "inline" or "anchor")
                {
                    return DrawingObject(k);
                }
            }

            return string.Empty;
        }

        if (local == "pict")
        {
            return VmlPicture(container);
        }

        Lost("loss.ole-objects", "embedded objects", "Embedded objects are written as their preview picture when they have one, and are dropped otherwise.");
        MarkupNode shape = FindDescendant(container, VNs, "shape", 0);
        return shape.IsNull ? string.Empty : VmlPicture(container);
    }

    private static MarkupNode FindDescendant(MarkupNode n, string ns, string local, int depth)
    {
        if (depth > 12)
        {
            return default;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.NamespaceUri == ns && c.LocalNameString == local)
            {
                return c;
            }

            MarkupNode f = FindDescendant(c, ns, local, depth + 1);
            if (!f.IsNull)
            {
                return f;
            }
        }

        return default;
    }

    private static MarkupNode FindW(MarkupNode n, string local, int depth)
    {
        if (depth > 12)
        {
            return default;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (IsW(c, local))
            {
                return c;
            }

            MarkupNode f = FindW(c, local, depth + 1);
            if (!f.IsNull)
            {
                return f;
            }
        }

        return default;
    }

    private static string? WpAttr(MarkupNode n, string local) => n.GetAttribute(local);

    private string DrawingObject(MarkupNode obj)
    {
        bool inline = obj.LocalNameString == "inline";
        long cx = 0, cy = 0;
        string name = "Frame" + OdfUnits.I(_frameCount + 1);
        string? descr = null, title = null;
        foreach (MarkupNode k in obj.ChildElements)
        {
            if (k.NamespaceUri is not (Wp or WpStrict))
            {
                continue;
            }

            switch (k.LocalNameString)
            {
                case "extent":
                    cx = long.TryParse(WpAttr(k, "cx"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long x) ? x : 0;
                    cy = long.TryParse(WpAttr(k, "cy"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long y) ? y : 0;
                    break;
                case "docPr":
                    if (WpAttr(k, "name") is { Length: > 0 } nm)
                    {
                        name = nm;
                    }

                    descr = WpAttr(k, "descr");
                    title = WpAttr(k, "title");
                    break;
            }
        }

        MarkupNode graphic = default;
        foreach (MarkupNode k in obj.ChildElements)
        {
            if (k.NamespaceUri is ANs or ANsStrict && k.LocalNameString == "graphic")
            {
                graphic = k;
            }
        }

        MarkupNode data = default;
        foreach (MarkupNode k in graphic.ChildElements)
        {
            if (k.LocalNameString == "graphicData")
            {
                data = k;
            }
        }

        string content = string.Empty;
        MarkupNode pic = FindDescendant(data, "http://schemas.openxmlformats.org/drawingml/2006/picture", "pic", 0);
        if (!pic.IsNull)
        {
            MarkupNode blip = FindDescendant(pic, ANs, "blip", 0);
            if (blip.IsNull)
            {
                blip = FindDescendant(pic, ANsStrict, "blip", 0);
            }

            string? embed = blip.IsNull ? null : RA(blip, "embed");
            string? href = embed is null ? null : RegisterMedia(embed);
            if (href is null)
            {
                Lost("loss.pictures", "pictures", "Pictures whose data is linked or missing were not written.");
                return string.Empty;
            }

            content = "<draw:image xlink:href=\"" + Esc(href) + "\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>";
        }
        else
        {
            MarkupNode txbx = FindDescendant(data, "http://schemas.microsoft.com/office/word/2010/wordprocessingShape", "txbx", 0);
            MarkupNode box = txbx.IsNull ? default : Child(txbx, "txbxContent");
            if (!box.IsNull)
            {
                content = TextBoxContent(box, cy);
            }
            else
            {
                string uri = data.IsNull ? string.Empty : data.GetAttribute("uri") ?? string.Empty;
                if (uri.Contains("chart", StringComparison.OrdinalIgnoreCase))
                {
                    Lost("loss.charts", "charts", "Charts are not written to OpenDocument.");
                }
                else if (uri.Contains("diagram", StringComparison.OrdinalIgnoreCase))
                {
                    Lost("loss.smartart", "diagrams", "Diagrams are not written to OpenDocument.");
                }
                else
                {
                    Lost("loss.shapes", "drawing shapes", "Drawing shapes are not written to OpenDocument.");
                }

                return string.Empty;
            }
        }

        _frameCount++;
        string anchor = inline ? "as-char" : "paragraph";
        string pos = string.Empty;
        string gprops;
        if (inline)
        {
            gprops = " style:vertical-pos=\"top\" style:vertical-rel=\"baseline\" style:horizontal-pos=\"left\" style:horizontal-rel=\"paragraph\" fo:margin-left=\"0cm\" fo:margin-right=\"0cm\" fo:margin-top=\"0cm\" fo:margin-bottom=\"0cm\" style:wrap=\"none\"";
        }
        else
        {
            MarkupNode ph = default, pv = default;
            foreach (MarkupNode k in obj.ChildElements)
            {
                if (k.NamespaceUri is Wp or WpStrict)
                {
                    if (k.LocalNameString == "positionH")
                    {
                        ph = k;
                    }
                    else if (k.LocalNameString == "positionV")
                    {
                        pv = k;
                    }
                }
            }

            string hFrom = WpAttr(ph, "relativeFrom") ?? "column", vFrom = WpAttr(pv, "relativeFrom") ?? "paragraph";
            if (vFrom == "page" && hFrom == "page")
            {
                anchor = "page";
            }

            string hRel = hFrom switch { "page" => "page", "margin" => "page-content", "character" => "char", "leftMargin" or "insideMargin" => "page-start-margin", "rightMargin" or "outsideMargin" => "page-end-margin", _ => "paragraph" };
            string vRel = vFrom switch { "page" => "page", "margin" => "page-content", "line" => "line", "topMargin" or "bottomMargin" or "insideMargin" or "outsideMargin" => "page", _ => "paragraph" };
            (string hPos, string? xo) = PosOf(ph, "left", "from-left");
            (string vPos, string? yo) = PosOf(pv, "top", "from-top");
            if (xo is not null)
            {
                pos += " svg:x=\"" + xo + "\"";
            }

            if (yo is not null)
            {
                pos += " svg:y=\"" + yo + "\"";
            }

            string wrap = "parallel";
            bool behind = obj.GetAttribute("behindDoc") is "1" or "true";
            foreach (MarkupNode k in obj.ChildElements)
            {
                if (k.NamespaceUri is Wp or WpStrict && k.LocalNameString.StartsWith("wrap", StringComparison.Ordinal))
                {
                    wrap = k.LocalNameString switch { "wrapTopAndBottom" => "none", "wrapNone" => "run-through", _ => "parallel" };
                }
            }

            gprops = " style:wrap=\"" + wrap + "\" style:horizontal-pos=\"" + hPos + "\" style:horizontal-rel=\"" + hRel + "\" style:vertical-pos=\"" + vPos + "\" style:vertical-rel=\"" + vRel
                + "\" fo:margin-left=\"0.32cm\" fo:margin-right=\"0.32cm\" fo:margin-top=\"0cm\" fo:margin-bottom=\"0cm\" style:run-through=\"" + (behind ? "background" : "foreground") + "\"";
        }

        if (content.StartsWith("<draw:image", StringComparison.Ordinal))
        {
            gprops += " fo:border=\"none\"";
        }

        string style = AddAuto("graphic", "fr", "Graphics", string.Empty, "<style:graphic-properties" + gprops + "/>");
        var sb = new StringBuilder("<draw:frame draw:style-name=\"");
        sb.Append(style).Append("\" draw:name=\"").Append(Esc(name)).Append("\" text:anchor-type=\"").Append(anchor).Append('"').Append(pos)
            .Append(" svg:width=\"").Append(OdfUnits.EmuToCm(cx)).Append("\" svg:height=\"").Append(OdfUnits.EmuToCm(cy)).Append('"');
        if (!inline)
        {
            sb.Append(" draw:z-index=\"").Append(OdfUnits.I(_frameCount)).Append('"');
        }

        sb.Append('>').Append(content);
        if (!string.IsNullOrEmpty(title))
        {
            sb.Append("<svg:title>").Append(Esc(title)).Append("</svg:title>");
        }

        if (!string.IsNullOrEmpty(descr))
        {
            sb.Append("<svg:desc>").Append(Esc(descr)).Append("</svg:desc>");
        }

        return sb.Append("</draw:frame>").ToString();
    }

    private static (string Pos, string? Offset) PosOf(MarkupNode p, string alignDefault, string fromName)
    {
        if (p.IsNull)
        {
            return (fromName, "0cm");
        }

        foreach (MarkupNode k in p.ChildElements)
        {
            if (k.LocalNameString == "align")
            {
                return (k.InnerText.Trim() switch { "center" => "center", "right" or "outside" => alignDefault == "left" ? "right" : "bottom", "bottom" => "bottom", "inside" => alignDefault, _ => alignDefault }, null);
            }

            if (k.LocalNameString == "posOffset" && long.TryParse(k.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long emu))
            {
                return (fromName, OdfUnits.EmuToCm(emu));
            }
        }

        return (fromName, "0cm");
    }

    private string TextBoxContent(MarkupNode box, long cyEmu)
    {
        var body = new StringBuilder();
        SubContext(() =>
        {
            foreach (MarkupNode c in box.ChildElements)
            {
                Block(body, c);
            }

            CloseLists(body);
        });
        return "<draw:text-box fo:min-height=\"" + OdfUnits.EmuToCm(cyEmu) + "\">" + (body.Length == 0 ? "<text:p/>" : body.ToString()) + "</draw:text-box>";
    }

    // A legacy picture or text box: v:shape with v:imagedata or v:textbox; the size comes from the style property.
    private string VmlPicture(MarkupNode pict)
    {
        MarkupNode shape = FindDescendant(pict, VNs, "shape", 0);
        if (shape.IsNull)
        {
            Lost("loss.shapes", "drawing shapes", "Drawing shapes are not written to OpenDocument.");
            return string.Empty;
        }

        string style = shape.GetAttribute("style") ?? string.Empty;
        double wPt = VmlLength(style, "width", 72), hPt = VmlLength(style, "height", 72);
        MarkupNode image = FindDescendant(shape, VNs, "imagedata", 0);
        string content;
        if (!image.IsNull)
        {
            string? embed = image.GetAttribute(R, "id") ?? image.GetAttribute(RStrict, "id");
            string? href = embed is null ? null : RegisterMedia(embed);
            if (href is null)
            {
                Lost("loss.pictures", "pictures", "Pictures whose data is linked or missing were not written.");
                return string.Empty;
            }

            content = "<draw:image xlink:href=\"" + Esc(href) + "\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>";
        }
        else
        {
            MarkupNode textbox = FindDescendant(shape, VNs, "textbox", 0);
            MarkupNode box = textbox.IsNull ? default : Child(textbox, "txbxContent");
            if (box.IsNull)
            {
                Lost("loss.shapes", "drawing shapes", "Drawing shapes are not written to OpenDocument.");
                return string.Empty;
            }

            content = TextBoxContent(box, (long)(hPt * 12700));
        }

        _frameCount++;
        bool absolute = style.Contains("position:absolute", StringComparison.Ordinal);
        string gstyle = AddAuto("graphic", "fr", "Graphics", string.Empty,
            "<style:graphic-properties style:wrap=\"" + (absolute ? "parallel" : "none") + "\" style:vertical-pos=\"" + (absolute ? "from-top" : "top") + "\" style:vertical-rel=\"" + (absolute ? "paragraph" : "baseline")
            + "\" style:horizontal-pos=\"" + (absolute ? "from-left" : "left") + "\" style:horizontal-rel=\"paragraph\"/>");
        string pos = absolute ? " svg:x=\"" + OdfUnits.Cm(VmlLength(style, "margin-left", 0)) + "\" svg:y=\"" + OdfUnits.Cm(VmlLength(style, "margin-top", 0)) + "\"" : string.Empty;
        return "<draw:frame draw:style-name=\"" + gstyle + "\" draw:name=\"Frame" + OdfUnits.I(_frameCount) + "\" text:anchor-type=\"" + (absolute ? "paragraph" : "as-char") + "\"" + pos
            + " svg:width=\"" + OdfUnits.Cm(wPt) + "\" svg:height=\"" + OdfUnits.Cm(hPt) + "\">" + content + "</draw:frame>";
    }

    private static double VmlLength(string style, string property, double fallback)
    {
        foreach (string part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && part[..colon].Trim() == property && OdfUnits.TryPoints(part[(colon + 1)..].Trim(), out double pt))
            {
                return pt;
            }
        }

        return fallback;
    }

    private readonly Dictionary<string, string> _mediaByPart = new(StringComparer.Ordinal);

    private string? RegisterMedia(string relId)
    {
        Relationship? rel = RelationshipOf(relId);
        if (rel is null || rel.TargetMode != RelationshipTargetMode.Internal)
        {
            return null;
        }

        IPackagePart? part = Target(relId);
        if (part is null)
        {
            return null;
        }

        if (_mediaByPart.TryGetValue(part.Name, out string? known))
        {
            return known;
        }

        byte[] data = part.GetBytes().ToArray();
        string ext = Path.GetExtension(part.Name).TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0)
        {
            ext = "bin";
        }

        string mediaType = ext switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "tif" or "tiff" => "image/tiff",
            "emf" => "image/x-emf",
            "wmf" => "image/x-wmf",
            "svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
        string name = "Pictures/image" + OdfUnits.I(++_imageCount) + "." + ext;
        _media.Add((name, data, mediaType));
        _mediaByPart[part.Name] = name;
        return name;
    }
}
