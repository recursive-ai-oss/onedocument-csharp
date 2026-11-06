using System.Globalization;
using System.Linq;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Writes a worksheet part of SpreadsheetML markup as the records of a binary worksheet ([MS-XLSB] §2.1.7.62).</summary>
internal static class XlsbSheetWriter
{
    private static readonly string[] Panes = ["bottomRight", "topRight", "bottomLeft", "topLeft"];

    public static byte[] FromXml(ReadOnlyMemory<byte> xml, OpenContext context, string partName, XlsbFormulaContext cx, XlsbFontTable fonts, ILossSink loss)
    {
        MarkupDocument doc = MarkupDocument.Parse(xml, context, partName);
        using var ms = new MemoryStream();
        var w = new SheetWriter(ms, context, partName, cx, fonts, loss, doc.Root);
        w.Run();
        return ms.ToArray();
    }

    private sealed class SheetWriter(Stream sink, OpenContext context, string partName, XlsbFormulaContext cx, XlsbFontTable fonts, ILossSink loss, MarkupNode root)
    {
        private readonly XlsbRecordWriter _w = new(sink);
        private readonly XlsbRecordData _d = new();
        private readonly List<(int R1, int R2, int C1, int C2)> _arrays = [];
        private readonly Dictionary<int, (FormulaNode Node, int Row, int Column)> _shared = [];
        private double _defaultRowHeight = 15;

        public void Run()
        {
            _w.Write(XlsbRecordType.BeginSheet);
            MarkupNode sheetPr = root.Element("sheetPr");
            WriteSheetPr(sheetPr);
            foreach (MarkupNode f in root.Element("sheetData").Elements("row").SelectMany(r => r.Elements("c")))
            {
                MarkupNode fn = f.Element("f");
                if (!fn.IsNull && fn.Str("t") == "array" && XlsbAddress.TryParseRange(fn.Str("ref"), out int r1, out int r2, out int c1, out int c2))
                {
                    _arrays.Add((r1, r2, c1, c2));
                }
            }

            _defaultRowHeight = root.Element("sheetFormatPr").Dbl("defaultRowHeight", 15);
            foreach (MarkupNode c in root.ChildElements)
            {
                if (c.TryRaw(out int t, out byte[] data))
                {
                    _w.Write(t, data);
                    continue;
                }

                if (c.NamespaceUri != XlsbMarkup.MainNamespace)
                {
                    continue;
                }

                switch (c.LocalNameString)
                {
                    case "sheetPr":
                        break;
                    case "dimension":
                        if (XlsbAddress.TryParseRange(c.Str("ref"), out int a, out int b, out int cc1, out int cc2))
                        {
                            _d.I32(a);
                            _d.I32(b);
                            _d.I32(cc1);
                            _d.I32(cc2);
                            _w.Write(XlsbRecordType.WsDim, _d);
                        }

                        break;
                    case "sheetViews":
                        WriteViews(c);
                        break;
                    case "sheetFormatPr":
                        WriteFormatPr(c);
                        break;
                    case "cols":
                        WriteCols(c);
                        break;
                    case "sheetData":
                        WriteSheetData(c);
                        break;
                    case "mergeCells":
                    {
                        var list = c.Elements("mergeCell").ToList();
                        if (list.Count > 0)
                        {
                            _d.U32((uint)list.Count);
                            _w.Write(XlsbRecordType.BeginMergeCells, _d);
                            foreach (MarkupNode m in list)
                            {
                                if (XlsbAddress.TryParseRange(m.Str("ref"), out int r1, out int r2, out int k1, out int k2))
                                {
                                    _d.I32(r1);
                                    _d.I32(r2);
                                    _d.I32(k1);
                                    _d.I32(k2);
                                    _w.Write(XlsbRecordType.MergeCell, _d);
                                }
                            }

                            _w.Write(XlsbRecordType.EndMergeCells);
                        }

                        break;
                    }

                    case "hyperlinks":
                        foreach (MarkupNode h in c.Elements("hyperlink"))
                        {
                            if (XlsbAddress.TryParseRange(h.Str("ref"), out int r1, out int r2, out int k1, out int k2))
                            {
                                _d.I32(r1);
                                _d.I32(r2);
                                _d.I32(k1);
                                _d.I32(k2);
                                string? rid = h.GetAttribute(XlsbMarkup.RelationshipNamespace, "id");
                                _d.NullableWideString(string.IsNullOrEmpty(rid) ? null : rid);
                                _d.WideString(h.Str("location"));
                                _d.WideString(h.Str("tooltip"));
                                _d.WideString(h.Str("display"));
                                _w.Write(XlsbRecordType.HLink, _d);
                            }
                        }

                        break;
                    case "printOptions":
                        _d.U16((c.Bool("horizontalCentered") ? 1 : 0) | (c.Bool("verticalCentered") ? 2 : 0) | (c.Bool("headings") ? 4 : 0) | (c.Bool("gridLines") ? 8 : 0) | 0x10);
                        _w.Write(XlsbRecordType.PrintOptions, _d);
                        break;
                    case "pageMargins":
                        _d.Number(c.Dbl("left", 0.7));
                        _d.Number(c.Dbl("right", 0.7));
                        _d.Number(c.Dbl("top", 0.75));
                        _d.Number(c.Dbl("bottom", 0.75));
                        _d.Number(c.Dbl("header", 0.3));
                        _d.Number(c.Dbl("footer", 0.3));
                        _w.Write(XlsbRecordType.Margins, _d);
                        break;
                    case "pageSetup":
                        WritePageSetup(c);
                        break;
                    case "headerFooter":
                        WriteHeaderFooter(c);
                        break;
                    case "drawing":
                        WriteRel(XlsbRecordType.Drawing, c);
                        break;
                    case "legacyDrawing":
                        WriteRel(XlsbRecordType.LegacyDrawing, c);
                        break;
                    default:
                        loss.Report("xlsb.sheet-element", "worksheet element " + c.LocalNameString, 1,
                            "The worksheet element '" + c.LocalNameString + "' has no binary form written here and is left out.");
                        break;
                }
            }

            _w.Write(XlsbRecordType.EndSheet);
        }

        private void WriteRel(int type, MarkupNode c)
        {
            string? rid = c.GetAttribute(XlsbMarkup.RelationshipNamespace, "id");
            _d.NullableWideString(rid);
            _w.Write(type, _d);
        }

        private void WriteSheetPr(MarkupNode pr)
        {
            MarkupNode outline = pr.Element("outlinePr");
            MarkupNode page = pr.Element("pageSetUpPr");
            MarkupNode tab = pr.Element("tabColor");
            int flags = (page.Bool("autoPageBreaks", true) ? 1 : 0) | (pr.Bool("published", true) ? 8 : 0) | (outline.Bool("applyStyles") ? 32 : 0)
                | (outline.Bool("summaryBelow", true) ? 64 : 0) | (outline.Bool("summaryRight", true) ? 128 : 0) | (page.Bool("fitToPage") ? 256 : 0)
                | (outline.Bool("showOutlineSymbols", true) ? 1024 : 0) | (pr.Bool("filterMode") ? 1 << 16 : 0) | (pr.Bool("enableFormatConditionsCalculation", true) ? 1 << 17 : 0);
            _d.U8(flags & 0xFF);
            _d.U8((flags >> 8) & 0xFF);
            _d.U8((flags >> 16) & 0xFF);
            (tab.IsNull ? new XlsbColor(0, 64, 0, 0) : XlsbColor.FromXml(tab)).Write(_d);
            _d.I32(-1);
            _d.I32(-1);
            _d.WideString(pr.Str("codeName"));
            _w.Write(XlsbRecordType.WsProp, _d);
        }

        private void WriteViews(MarkupNode views)
        {
            var list = views.Elements("sheetView").ToList();
            if (list.Count == 0)
            {
                return;
            }

            _w.Write(XlsbRecordType.BeginWsViews);
            foreach (MarkupNode v in list)
            {
                int flags = (v.Bool("windowProtection") ? 1 : 0) | (v.Bool("showFormulas") ? 2 : 0) | (v.Bool("showGridLines", true) ? 4 : 0)
                    | (v.Bool("showRowColHeaders", true) ? 8 : 0) | (v.Bool("showZeros", true) ? 16 : 0) | (v.Bool("rightToLeft") ? 32 : 0) | (v.Bool("tabSelected") ? 64 : 0)
                    | (v.Bool("showRuler", true) ? 128 : 0) | (v.Bool("showOutlineSymbols", true) ? 256 : 0) | (v.Bool("defaultGridColor", true) ? 512 : 0);
                string view = v.Str("view", "normal");
                int top = 0;
                int left = 0;
                if (v.GetAttribute("topLeftCell") is { } tl)
                {
                    XlsbAddress.TryCell(tl, out top, out left);
                }

                _d.U16(flags);
                _d.I32(view == "pageBreakPreview" ? 1 : view == "pageLayout" ? 2 : 0);
                _d.I32(top);
                _d.I32(left);
                _d.U8(v.Int("colorId", 64));
                _d.U8(0);
                _d.U16(0);
                _d.U16(v.Int("zoomScale", 100));
                _d.U16(v.Int("zoomScaleNormal", 100));
                _d.U16(v.Int("zoomScaleSheetLayoutView"));
                _d.U16(v.Int("zoomScalePageLayoutView"));
                _d.I32(v.Int("workbookViewId"));
                _w.Write(XlsbRecordType.BeginWsView, _d);
                foreach (MarkupNode c in v.ChildElements)
                {
                    if (c.TryRaw(out int t, out byte[] data))
                    {
                        _w.Write(t, data);
                    }
                    else if (c.Is("pane"))
                    {
                        int pt = 0;
                        int pl = 0;
                        if (c.GetAttribute("topLeftCell") is { } ptl)
                        {
                            XlsbAddress.TryCell(ptl, out pt, out pl);
                        }

                        string state = c.Str("state", "split");
                        _d.Number(c.Dbl("xSplit"));
                        _d.Number(c.Dbl("ySplit"));
                        _d.I32(pt);
                        _d.I32(pl);
                        _d.I32(Math.Max(0, Array.IndexOf(Panes, c.Str("activePane", "topLeft"))));
                        _d.U8((state is "frozen" or "frozenSplit" ? 1 : 0) | (state == "frozenSplit" ? 2 : 0));
                        _w.Write(XlsbRecordType.Pane, _d);
                    }
                    else if (c.Is("selection"))
                    {
                        int row = 0;
                        int col = 0;
                        if (c.GetAttribute("activeCell") is { } ac)
                        {
                            XlsbAddress.TryCell(ac, out row, out col);
                        }

                        string[] refs = c.Str("sqref", XlsbAddress.Cell(row, col)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        _d.I32(Math.Max(0, Array.IndexOf(Panes, c.Str("pane", "topLeft"))));
                        _d.I32(row);
                        _d.I32(col);
                        _d.I32(0);
                        _d.I32(refs.Length);
                        foreach (string rf in refs)
                        {
                            if (!XlsbAddress.TryParseRange(rf, out int r1, out int r2, out int c1, out int c2))
                            {
                                continue;
                            }

                            _d.I32(r1);
                            _d.I32(r2);
                            _d.I32(c1);
                            _d.I32(c2);
                        }

                        _w.Write(XlsbRecordType.Sel, _d);
                    }
                }

                _w.Write(XlsbRecordType.EndWsView);
            }

            _w.Write(XlsbRecordType.EndWsViews);
        }

        private void WriteFormatPr(MarkupNode c)
        {
            _d.U32(c.GetAttribute("defaultColWidth") is not null ? (uint)Math.Round(c.Dbl("defaultColWidth") * 256) : 0xFFFFFFFF);
            _d.U16(c.Int("baseColWidth", 8));
            _d.U16((int)Math.Round(c.Dbl("defaultRowHeight", 15) * 20));
            _d.U16((c.Bool("customHeight") ? 1 : 0) | (c.Bool("zeroHeight") ? 2 : 0) | (c.Bool("thickTop") ? 4 : 0) | (c.Bool("thickBottom") ? 8 : 0));
            _d.U8(c.Int("outlineLevelRow"));
            _d.U8(c.Int("outlineLevelCol"));
            _w.Write(XlsbRecordType.WsFmtInfo, _d);
        }

        private void WriteCols(MarkupNode cols)
        {
            var list = cols.Elements("col").ToList();
            if (list.Count == 0)
            {
                return;
            }

            _w.Write(XlsbRecordType.BeginColInfos);
            foreach (MarkupNode c in list)
            {
                _d.I32(c.Int("min", 1) - 1);
                _d.I32(c.Int("max", c.Int("min", 1)) - 1);
                _d.U32((uint)Math.Round(c.Dbl("width", 8.43) * 256));
                _d.I32(c.Int("style"));
                int flags = (c.Bool("hidden") ? 1 : 0) | (c.Bool("customWidth") ? 2 : 0) | (c.Bool("bestFit") ? 4 : 0) | (c.Bool("phonetic") ? 8 : 0)
                    | ((c.Int("outlineLevel") & 7) << 8) | (c.Bool("collapsed") ? 0x1000 : 0);
                _d.U16(flags);
                _w.Write(XlsbRecordType.ColInfo, _d);
            }

            _w.Write(XlsbRecordType.EndColInfos);
        }

        private void WritePageSetup(MarkupNode c)
        {
            _d.I32(c.Int("paperSize", 1));
            _d.I32(c.Int("scale", 100));
            _d.I32(c.Int("horizontalDpi", 600));
            _d.I32(c.Int("verticalDpi", 600));
            _d.I32(c.Int("copies", 1));
            _d.I32(c.Int("firstPageNumber", 1));
            _d.I32(c.Int("fitToWidth", 1));
            _d.I32(c.Int("fitToHeight", 1));
            int flags = (c.Str("pageOrder", "downThenOver") == "overThenDown" ? 1 : 0) | (c.Str("orientation") == "landscape" ? 2 : 0) | (c.Str("orientation") == "default" ? 0 : 0)
                | (c.Bool("blackAndWhite") ? 8 : 0) | (c.Bool("draft") ? 16 : 0) | (c.Bool("useFirstPageNumber") ? 0x80 : 0);
            _d.U16(flags);
            _d.NullableWideString(c.GetAttribute(XlsbMarkup.RelationshipNamespace, "id"));
            _w.Write(XlsbRecordType.PageSetup, _d);
        }

        private void WriteHeaderFooter(MarkupNode c)
        {
            _d.U16((c.Bool("differentOddEven") ? 1 : 0) | (c.Bool("differentFirst") ? 2 : 0) | (c.Bool("scaleWithDoc", true) ? 4 : 0) | (c.Bool("alignWithMargins", true) ? 8 : 0));
            foreach (string name in new[] { "oddHeader", "oddFooter", "evenHeader", "evenFooter", "firstHeader", "firstFooter" })
            {
                MarkupNode n = c.Element(name);
                _d.NullableWideString(n.IsNull ? null : n.InnerText);
            }

            _w.Write(XlsbRecordType.BeginHeaderFooter, _d);
            _w.Write(XlsbRecordType.EndHeaderFooter);
        }

        // ------------------------------------------------------------ cells

        private void WriteSheetData(MarkupNode sheetData)
        {
            _w.Write(XlsbRecordType.BeginSheetData);
            int nextRow = 0;
            foreach (MarkupNode row in sheetData.Elements("row"))
            {
                int r = row.GetAttribute("r") is not null ? row.Int("r") - 1 : nextRow;
                nextRow = r + 1;
                var cells = new List<(int Col, MarkupNode Node)>();
                int nextCol = 0;
                foreach (MarkupNode c in row.Elements("c"))
                {
                    int col = nextCol;
                    if (c.GetAttribute("r") is { } addr && XlsbAddress.TryCell(addr, out _, out int ac))
                    {
                        col = ac;
                    }

                    cells.Add((col, c));
                    nextCol = col + 1;
                }

                WriteRowHeader(row, r, cells);
                foreach ((int col, MarkupNode node) in cells)
                {
                    WriteCell(r, col, node);
                }

                foreach (MarkupNode extra in row.ChildElements)
                {
                    if (extra.TryRaw(out int t, out byte[] data))
                    {
                        _w.Write(t, data);
                    }
                }
            }

            _w.Write(XlsbRecordType.EndSheetData);
        }

        private void WriteRowHeader(MarkupNode row, int r, List<(int Col, MarkupNode Node)> cells)
        {
            _d.I32(r);
            _d.I32(row.Int("s"));
            double ht = row.GetAttribute("ht") is not null ? row.Dbl("ht") : _defaultRowHeight;
            _d.U16((int)Math.Round(ht * 20));
            int b1 = (row.Bool("thickTop") ? 1 : 0) | (row.Bool("thickBot") ? 2 : 0);
            int b2 = (row.Int("outlineLevel") & 7) | (row.Bool("collapsed") ? 8 : 0) | (row.Bool("hidden") ? 16 : 0) | (row.Bool("customHeight") ? 32 : 0) | (row.Bool("customFormat") ? 64 : 0);
            _d.U8(b1);
            _d.U8(b2);
            _d.U8(row.Bool("ph") ? 1 : 0);
            // One span per block of 1,024 columns that holds a cell ([MS-XLSB] §2.5.9).
            var spans = new SortedDictionary<int, (int Min, int Max)>();
            foreach ((int col, _) in cells)
            {
                int seg = col / 1024;
                spans[seg] = spans.TryGetValue(seg, out (int Min, int Max) s) ? (Math.Min(s.Min, col), Math.Max(s.Max, col)) : (col, col);
            }

            _d.U32((uint)spans.Count);
            foreach ((int min, int max) in spans.Values)
            {
                _d.I32(min);
                _d.I32(max);
            }

            _w.Write(XlsbRecordType.RowHdr, _d);
        }

        private void CellHead(int col, MarkupNode c)
        {
            _d.I32(col);
            _d.I32(c.Int("s") & 0xFFFFFF);
        }

        private void WriteCell(int row, int col, MarkupNode c)
        {
            MarkupNode f = c.Element("f");
            MarkupNode v = c.Element("v");
            string type = c.Str("t", "n");
            string text = v.IsNull ? string.Empty : v.InnerText;
            bool inArrayFollower = false;
            (int Row, int Col) arrayMaster = default;
            if (f.IsNull)
            {
                foreach ((int r1, int r2, int c1, int c2) in _arrays)
                {
                    if (row >= r1 && row <= r2 && col >= c1 && col <= c2)
                    {
                        inArrayFollower = true;
                        arrayMaster = (r1, c1);
                        break;
                    }
                }
            }

            if (!f.IsNull || inArrayFollower)
            {
                WriteFormulaCell(row, col, c, f, type, text, !v.IsNull, inArrayFollower, arrayMaster);
                return;
            }

            switch (type)
            {
                case "s":
                    CellHead(col, c);
                    _d.U32(uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint idx) ? idx : 0);
                    _w.Write(XlsbRecordType.CellIsst, _d);
                    return;
                case "b":
                    CellHead(col, c);
                    _d.U8(text is "1" or "true" ? 1 : 0);
                    _w.Write(XlsbRecordType.CellBool, _d);
                    return;
                case "e":
                    CellHead(col, c);
                    _d.U8(CalcErrors.TryParse(text, out CalcError e) ? XlsbFormula.ErrorToCode(e, out _) : 0x0F);
                    _w.Write(XlsbRecordType.CellError, _d);
                    return;
                case "str":
                    CellHead(col, c);
                    _d.WideString(text);
                    _w.Write(XlsbRecordType.CellSt, _d);
                    return;
                case "inlineStr":
                    InlineString(col, c);
                    return;
                case "d":
                    loss.Report("xlsb.date-text", "ISO 8601 date cells", 1, "A cell holding an ISO 8601 date as text (t=\"d\") is written as text.");
                    CellHead(col, c);
                    _d.WideString(text);
                    _w.Write(XlsbRecordType.CellSt, _d);
                    return;
            }

            if (v.IsNull)
            {
                CellHead(col, c);
                _w.Write(XlsbRecordType.CellBlank, _d);
                return;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                CellHead(col, c);
                _d.WideString(text);
                _w.Write(XlsbRecordType.CellSt, _d);
                return;
            }

            CellHead(col, c);
            if (number == Math.Floor(number) && number >= -(1 << 29) && number < (1 << 29) && !(number == 0 && double.IsNegative(number)))
            {
                // RkNumber ([MS-XLSB] §2.5.123): a signed integer in 30 bits.
                _d.U32(((uint)(int)number << 2) | 2);
                _w.Write(XlsbRecordType.CellRk, _d);
            }
            else
            {
                _d.Number(number);
                _w.Write(XlsbRecordType.CellReal, _d);
            }
        }

        private void InlineString(int col, MarkupNode c)
        {
            MarkupNode isNode = c.Element("is");
            bool rich = isNode.Elements("r").Any();
            CellHead(col, c);
            if (!rich)
            {
                var sb = new System.Text.StringBuilder();
                foreach (MarkupNode t in isNode.Elements("t"))
                {
                    sb.Append(XlsbSharedStringsPart.Decode(t.InnerText));
                }

                _d.WideString(sb.ToString());
                _w.Write(XlsbRecordType.CellSt, _d);
                return;
            }

            var text = new System.Text.StringBuilder();
            var runs = new List<(int Start, int Font)>();
            foreach (MarkupNode r in isNode.ChildElements)
            {
                if (r.Is("t"))
                {
                    text.Append(XlsbSharedStringsPart.Decode(r.InnerText));
                }
                else if (r.Is("r"))
                {
                    MarkupNode rpr = r.Element("rPr");
                    int font = 0;
                    if (!rpr.IsNull)
                    {
                        XlsbFont fnt = XlsbFont.FromXml(rpr);
                        font = fonts.Find(fnt);
                        if (font < 0)
                        {
                            fonts.Fonts.Add(fnt);
                            fonts.Grew = true;
                            font = fonts.Fonts.Count - 1;
                        }
                    }

                    runs.Add((text.Length, font));
                    text.Append(XlsbSharedStringsPart.Decode(r.Element("t").InnerText));
                }
            }

            _d.U8(1);
            _d.WideString(text.ToString());
            _d.U32((uint)runs.Count);
            foreach ((int s, int fnt) in runs)
            {
                _d.U16(s);
                _d.U16(fnt);
            }

            _w.Write(XlsbRecordType.CellRString, _d);
        }

        private FormulaNode? Parse(string text, int row, int col)
        {
            var options = new FormulaParserOptions { Limits = context.Limits };
            if (FormulaParser.TryParse(text, out FormulaNode? node, out FormulaSyntaxError error, options))
            {
                return node;
            }

            loss.Report("xlsb.formula-unparsed", "formula that could not be parsed", 1,
                "The formula of " + XlsbAddress.Cell(row, col) + " in " + partName + " could not be parsed (" + error.Code + "); the cell keeps its value only.");
            return null;
        }

        private void WriteFormulaCell(int row, int col, MarkupNode c, MarkupNode f, string type, string text, bool hasValue, bool arrayFollower, (int Row, int Col) master)
        {
            string ft = f.IsNull ? string.Empty : f.Str("t");
            byte[] rgce = [];
            byte[] rgcb = [];
            bool arrayMaster = ft == "array";
            FormulaNode? node = null;
            if (arrayFollower)
            {
                rgce = new byte[5];
                rgce[0] = 0x01;
                BitConverter.TryWriteBytes(rgce.AsSpan(1), master.Row);
                rgcb = BitConverter.GetBytes(master.Col);
            }
            else if (arrayMaster)
            {
                rgce = new byte[5];
                rgce[0] = 0x01;
                BitConverter.TryWriteBytes(rgce.AsSpan(1), row);
                rgcb = BitConverter.GetBytes(col);
                node = Parse(f.InnerText, row, col);
            }
            else if (ft == "shared")
            {
                int si = f.Int("si");
                string body = f.InnerText;
                if (body.Length > 0)
                {
                    FormulaNode? master0 = Parse(body, row, col);
                    if (master0 is not null)
                    {
                        _shared[si] = (master0, row, col);
                        node = master0;
                    }
                }
                else if (_shared.TryGetValue(si, out (FormulaNode Node, int Row, int Column) m))
                {
                    node = FormulaTranslator.Translate(m.Node, row - m.Row, col - m.Column);
                }
            }
            else if (ft == "dataTable")
            {
                loss.Report("xlsb.data-table", "data table formulas", 1, "A data table is written as its cached values.");
            }
            else
            {
                node = Parse(f.InnerText, row, col);
            }

            if (node is not null && !arrayMaster)
            {
                if (!XlsbFormula.TryEncode(node, cx, XlsbFormulaKind.Cell, row, col, out rgce, out rgcb, out string? problem))
                {
                    loss.Report("xlsb.formula-approximated", "formula written approximately", 1, "A formula could not be encoded exactly: " + problem);
                }
            }

            bool alwaysCalc = f.Bool("ca") || !hasValue;
            if (rgce.Length == 0)
            {
                // No formula could be written: keep the value.
                WriteValueOnly(row, col, c, type, text);
                return;
            }

            int type2 = type switch { "str" => XlsbRecordType.FmlaString, "b" => XlsbRecordType.FmlaBool, "e" => XlsbRecordType.FmlaError, _ => XlsbRecordType.FmlaNum };
            CellHead(col, c);
            switch (type2)
            {
                case XlsbRecordType.FmlaString: _d.WideString(text); break;
                case XlsbRecordType.FmlaBool: _d.U8(text is "1" or "true" ? 1 : 0); break;
                case XlsbRecordType.FmlaError: _d.U8(CalcErrors.TryParse(text, out CalcError e) ? XlsbFormula.ErrorToCode(e, out _) : 0x0F); break;
                default:
                    _d.Number(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : 0);
                    break;
            }

            _d.U16(alwaysCalc ? 1 : 0);
            _d.U32((uint)rgce.Length);
            _d.Bytes(rgce);
            _d.U32((uint)rgcb.Length);
            _d.Bytes(rgcb);
            _w.Write(type2, _d);
            if (arrayMaster && node is not null)
            {
                if (XlsbAddress.TryParseRange(f.Str("ref"), out int r1, out int r2, out int c1, out int c2))
                {
                    if (!XlsbFormula.TryEncode(node, cx, XlsbFormulaKind.Array, row, col, out byte[] arce, out byte[] arcb, out string? problem))
                    {
                        loss.Report("xlsb.formula-approximated", "formula written approximately", 1, "A formula could not be encoded exactly: " + problem);
                    }

                    _d.I32(r1);
                    _d.I32(r2);
                    _d.I32(c1);
                    _d.I32(c2);
                    _d.U8(f.Bool("ca") ? 1 : 0);
                    _d.U32((uint)arce.Length);
                    _d.Bytes(arce);
                    _d.U32((uint)arcb.Length);
                    _d.Bytes(arcb);
                    _w.Write(XlsbRecordType.ArrFmla, _d);
                }
            }
        }

        private void WriteValueOnly(int row, int col, MarkupNode c, string type, string text)
        {
            _ = row;
            CellHead(col, c);
            switch (type)
            {
                case "b":
                    _d.U8(text is "1" or "true" ? 1 : 0);
                    _w.Write(XlsbRecordType.CellBool, _d);
                    break;
                case "e":
                    _d.U8(CalcErrors.TryParse(text, out CalcError e) ? XlsbFormula.ErrorToCode(e, out _) : 0x0F);
                    _w.Write(XlsbRecordType.CellError, _d);
                    break;
                case "str":
                    _d.WideString(text);
                    _w.Write(XlsbRecordType.CellSt, _d);
                    break;
                default:
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    {
                        _d.Number(n);
                        _w.Write(XlsbRecordType.CellReal, _d);
                    }
                    else
                    {
                        _w.Write(XlsbRecordType.CellBlank, _d);
                    }

                    break;
            }
        }
    }
}
