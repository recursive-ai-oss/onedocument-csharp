using System.Buffers.Binary;
using System.Globalization;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Cell address text for the translators.</summary>
internal static class XlsbAddress
{
    public static string Column(int zeroBasedColumn)
    {
        Span<char> buf = stackalloc char[4];
        int i = buf.Length;
        int n = zeroBasedColumn + 1;
        while (n > 0)
        {
            n--;
            buf[--i] = (char)('A' + (n % 26));
            n /= 26;
        }

        return new string(buf[i..]);
    }

    public static string Cell(int row, int column) => Column(column) + (row + 1).ToString(CultureInfo.InvariantCulture);

    public static string Range(int rowFirst, int rowLast, int colFirst, int colLast) =>
        rowFirst == rowLast && colFirst == colLast ? Cell(rowFirst, colFirst) : Cell(rowFirst, colFirst) + ":" + Cell(rowLast, colLast);

    /// <summary>Parses <c>A1</c> or <c>A1:B2</c> (absolute markers allowed); false when the text is not a reference.</summary>
    public static bool TryParseRange(string text, out int rowFirst, out int rowLast, out int colFirst, out int colLast)
    {
        rowFirst = rowLast = colFirst = colLast = 0;
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (!TryCell(colon < 0 ? text : text[..colon], out rowFirst, out colFirst))
        {
            return false;
        }

        if (colon < 0)
        {
            rowLast = rowFirst;
            colLast = colFirst;
            return true;
        }

        return TryCell(text[(colon + 1)..], out rowLast, out colLast);
    }

    public static bool TryCell(string text, out int row, out int column)
    {
        row = column = 0;
        int i = 0;
        if (i < text.Length && text[i] == '$')
        {
            i++;
        }

        int c = 0;
        int letters = 0;
        while (i < text.Length && char.IsAsciiLetter(text[i]))
        {
            c = (c * 26) + (char.ToUpperInvariant(text[i]) - 'A' + 1);
            i++;
            letters++;
        }

        if (letters is < 1 or > 3)
        {
            return false;
        }

        if (i < text.Length && text[i] == '$')
        {
            i++;
        }

        int r = 0;
        int digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i]) && digits < 8)
        {
            r = (r * 10) + (text[i] - '0');
            i++;
            digits++;
        }

        if (digits == 0 || i != text.Length || r < 1 || c > 16384 || r > 1048576)
        {
            return false;
        }

        row = r - 1;
        column = c - 1;
        return true;
    }
}

/// <summary>Translates a worksheet part between <c>sheetN.bin</c> and <c>sheetN.xml</c> ([MS-XLSB] §2.1.7.62).</summary>
internal static class XlsbSheetPart
{
    private static readonly string[] SelPane = ["bottomRight", "topRight", "bottomLeft", "topLeft"];

    public static byte[] ToXml(ReadOnlyMemory<byte> bin, OpenContext context, string partName, XlsbFormulaContext formulas, XlsbFontTable fonts)
    {
        var s = new SheetReader(bin, context, partName, formulas, fonts);
        return s.Run();
    }

    private sealed class SheetReader(ReadOnlyMemory<byte> bin, OpenContext context, string partName, XlsbFormulaContext formulas, XlsbFontTable fonts)
    {
        private readonly XlsbRecordReader _r = new(bin, context, partName);
        private readonly XmlSink _x = new(1 << 16);
        private readonly XmlSink _head = new();
        private readonly Dictionary<(int Row, int Column), int> _sharedIndex = [];
        private readonly Dictionary<(int Row, int Column), bool> _arrayMasters = [];
        private bool _inRow;
        private bool _inSheetData;
        private bool _inViews;
        private bool _inView;
        private bool _inCols;
        private bool _inMerge;
        private bool _inHlinks;
        private int _nextShared;
        private int _rowCount;

        // A cell whose formula is a PtgExp waits here for the shared-formula or array-formula record that follows it.
        private PendingCell? _pending;

        private sealed class PendingCell
        {
            public int Row;
            public int Column;
            public int Style;
            public string CellType = string.Empty;
            public string Value = string.Empty;
            public bool AlwaysCalc;
        }

        public byte[] Run()
        {
            while (_r.Read())
            {
                if (_pending is not null && _r.Type != XlsbRecordType.ShrFmla && _r.Type != XlsbRecordType.ArrFmla)
                {
                    FlushPending(null, null, null, null);
                }

                Handle();
            }

            CloseAll();
            var outp = new XmlSink(_x.Length + 512);
            outp.Raw("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"").Raw(XlsbMarkup.MainNamespace).Raw("\" xmlns:r=\"")
                .Raw(XlsbMarkup.RelationshipNamespace).Raw("\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:xb=\"").Raw(XlsbMarkup.Namespace).Raw("\" mc:Ignorable=\"xb\">");
            outp.Raw(_head.Memory.Span).Raw(_x.Memory.Span).Raw("</worksheet>");
            return outp.ToArray();
        }

        private void Short(string what)
        {
            context.Defects.Report(new Defect(DefectKind.Record, "xlsb.record-short", DefectSeverity.Repaired,
                "A " + what + " record is shorter than its fields claim; what was read is used.", DefectLocation.InRecord(partName, _r.Offset)));
        }

        private void Raw() => XlsbMarkup.Raw(_x, _r.Type, _r.Data);

        private void CloseRow()
        {
            if (_inRow)
            {
                _x.Raw("</row>");
                _inRow = false;
            }
        }

        private void CloseAll()
        {
            if (_pending is not null)
            {
                FlushPending(null, null, null, null);
            }

            CloseRow();
            if (_inSheetData)
            {
                _x.Raw("</sheetData>");
                _inSheetData = false;
            }

            CloseCols();
            CloseGroup(ref _inMerge, "</mergeCells>");
            CloseGroup(ref _inHlinks, "</hyperlinks>");
        }

        private void CloseGroup(ref bool open, string tag)
        {
            if (open)
            {
                _x.Raw(tag);
                open = false;
            }
        }

        private void CloseCols()
        {
            if (_inCols)
            {
                _head.Raw("</cols>");
                _inCols = false;
            }
        }

        private void Handle()
        {
            ReadOnlySpan<byte> d = _r.Data;
            switch (_r.Type)
            {
                case XlsbRecordType.BeginSheet:
                case XlsbRecordType.EndSheet:
                    return;
                case XlsbRecordType.WsProp:
                    WsProp(d);
                    return;
                case XlsbRecordType.WsDim:
                {
                    var f = new XlsbRecordFields(d);
                    int r1 = f.I32();
                    int r2 = f.I32();
                    int c1 = f.I32();
                    int c2 = f.I32();
                    _head.Raw("<dimension").Attr("ref", XlsbAddress.Range(r1, r2, c1, c2)).Raw("/>");
                    return;
                }

                case XlsbRecordType.BeginWsViews:
                    _head.Raw("<sheetViews>");
                    _inViews = true;
                    return;
                case XlsbRecordType.EndWsViews:
                    if (_inView)
                    {
                        _head.Raw("</sheetView>");
                        _inView = false;
                    }

                    _head.Raw("</sheetViews>");
                    _inViews = false;
                    return;
                case XlsbRecordType.BeginWsView:
                    WsView(d);
                    return;
                case XlsbRecordType.EndWsView:
                    if (_inView)
                    {
                        _head.Raw("</sheetView>");
                        _inView = false;
                    }

                    return;
                case XlsbRecordType.Pane:
                    Pane(d);
                    return;
                case XlsbRecordType.Sel:
                    Selection(d);
                    return;
                case XlsbRecordType.WsFmtInfo:
                    FormatInfo(d);
                    return;
                case XlsbRecordType.BeginColInfos:
                    _head.Raw("<cols>");
                    _inCols = true;
                    return;
                case XlsbRecordType.EndColInfos:
                    CloseCols();
                    return;
                case XlsbRecordType.ColInfo:
                    ColInfo(d);
                    return;
                case XlsbRecordType.BeginSheetData:
                    CloseCols();
                    _x.Raw("<sheetData>");
                    _inSheetData = true;
                    return;
                case XlsbRecordType.EndSheetData:
                    CloseRow();
                    _x.Raw("</sheetData>");
                    _inSheetData = false;
                    return;
                case XlsbRecordType.RowHdr:
                    RowHeader(d);
                    return;
                case XlsbRecordType.CellBlank:
                case XlsbRecordType.CellRk:
                case XlsbRecordType.CellError:
                case XlsbRecordType.CellBool:
                case XlsbRecordType.CellReal:
                case XlsbRecordType.CellSt:
                case XlsbRecordType.CellIsst:
                case XlsbRecordType.CellRString:
                    ValueCell(d);
                    return;
                case XlsbRecordType.FmlaString:
                case XlsbRecordType.FmlaNum:
                case XlsbRecordType.FmlaBool:
                case XlsbRecordType.FmlaError:
                    FormulaCell(d);
                    return;
                case XlsbRecordType.ShrFmla:
                case XlsbRecordType.ArrFmla:
                    SharedOrArray(d);
                    return;
                case XlsbRecordType.BeginMergeCells:
                    CloseRow();
                    _x.Raw("<mergeCells>");
                    _inMerge = true;
                    return;
                case XlsbRecordType.EndMergeCells:
                    CloseGroup(ref _inMerge, "</mergeCells>");
                    return;
                case XlsbRecordType.MergeCell:
                {
                    var f = new XlsbRecordFields(d);
                    int r1 = f.I32();
                    int r2 = f.I32();
                    int c1 = f.I32();
                    int c2 = f.I32();
                    _x.Raw("<mergeCell").Attr("ref", XlsbAddress.Range(r1, r2, c1, c2)).Raw("/>");
                    return;
                }

                case XlsbRecordType.HLink:
                    Hyperlink(d);
                    return;
                case XlsbRecordType.PrintOptions:
                {
                    var f = new XlsbRecordFields(d);
                    int bits = f.U16();
                    _x.Raw("<printOptions");
                    if ((bits & 1) != 0)
                    {
                        _x.Attr("horizontalCentered", 1);
                    }

                    if ((bits & 2) != 0)
                    {
                        _x.Attr("verticalCentered", 1);
                    }

                    if ((bits & 4) != 0)
                    {
                        _x.Attr("headings", 1);
                    }

                    if ((bits & 8) != 0)
                    {
                        _x.Attr("gridLines", 1);
                    }

                    _x.Raw("/>");
                    return;
                }

                case XlsbRecordType.Margins:
                {
                    var f = new XlsbRecordFields(d);
                    _x.Raw("<pageMargins").Attr("left", f.Number()).Attr("right", f.Number()).Attr("top", f.Number()).Attr("bottom", f.Number())
                        .Attr("header", f.Number()).Attr("footer", f.Number()).Raw("/>");
                    return;
                }

                case XlsbRecordType.PageSetup:
                    PageSetup(d);
                    return;
                case XlsbRecordType.Drawing:
                    RelElement("drawing", d);
                    return;
                case XlsbRecordType.LegacyDrawing:
                    RelElement("legacyDrawing", d);
                    return;
                case XlsbRecordType.BeginHeaderFooter:
                    HeaderFooter(d);
                    return;
                case XlsbRecordType.EndHeaderFooter:
                    return;
                default:
                    if (_inSheetData && !_inRow)
                    {
                        context.Defects.Report(new Defect(DefectKind.Record, "xlsb.record-dropped", DefectSeverity.Dropped,
                            "A record of type " + _r.Type.ToString(CultureInfo.InvariantCulture) + " between rows has no place in the markup; it is dropped.", DefectLocation.InRecord(partName, _r.Offset)));
                        return;
                    }

                    if (_inCols || _inViews)
                    {
                        XlsbMarkup.Raw(_head, _r.Type, d);
                        return;
                    }

                    Raw();
                    return;
            }
        }

        private void RelElement(string name, ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            _x.Raw("<").Raw(name).Attr("r:id", f.NullableWideString() ?? string.Empty).Raw("/>");
        }

        private void WsProp(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int flags = f.U8() | (f.U8() << 8) | (f.U8() << 16);
            XlsbColor tab = XlsbColor.Read(ref f);
            f.I32();
            f.I32();
            string code = f.WideString();
            if (f.Overrun)
            {
                Short("sheet properties");
            }

            _head.Raw("<sheetPr");
            if (code.Length > 0)
            {
                _head.Attr("codeName", code);
            }

            if ((flags & (1 << 17)) == 0)
            {
                _head.Attr("enableFormatConditionsCalculation", 0);
            }

            if ((flags & (1 << 16)) != 0)
            {
                _head.Attr("filterMode", 1);
            }

            if ((flags & (1 << 3)) == 0)
            {
                _head.Attr("published", 0);
            }

            _head.Raw(">");
            if (tab.Type != 0 || tab.Tint != 0)
            {
                _head.Raw("<tabColor");
                tab.Attributes(_head);
                _head.Raw("/>");
            }

            bool below = (flags & (1 << 6)) != 0;
            bool right = (flags & (1 << 7)) != 0;
            bool styles = (flags & (1 << 5)) != 0;
            bool symbols = (flags & (1 << 10)) != 0;
            if (!below || !right || styles || !symbols)
            {
                _head.Raw("<outlinePr");
                if (styles)
                {
                    _head.Attr("applyStyles", 1);
                }

                if (!below)
                {
                    _head.Attr("summaryBelow", 0);
                }

                if (!right)
                {
                    _head.Attr("summaryRight", 0);
                }

                if (!symbols)
                {
                    _head.Attr("showOutlineSymbols", 0);
                }

                _head.Raw("/>");
            }

            bool fit = (flags & (1 << 8)) != 0;
            bool autoBreaks = (flags & 1) != 0;
            if (fit || !autoBreaks)
            {
                _head.Raw("<pageSetUpPr");
                if (!autoBreaks)
                {
                    _head.Attr("autoPageBreaks", 0);
                }

                if (fit)
                {
                    _head.Attr("fitToPage", 1);
                }

                _head.Raw("/>");
            }

            _head.Raw("</sheetPr>");
        }

        private void WsView(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int flags = f.U16();
            int view = f.I32();
            int top = f.I32();
            int left = f.I32();
            int icv = f.U8();
            f.U8();
            f.U16();
            int scale = f.U16();
            int scaleNormal = f.U16();
            int scaleSlv = f.U16();
            int scalePlv = f.U16();
            int wbView = f.I32();
            if (f.Overrun)
            {
                Short("sheet view");
            }

            if (_inView)
            {
                _head.Raw("</sheetView>");
            }

            _head.Raw("<sheetView");
            if ((flags & 1) != 0)
            {
                _head.Attr("windowProtection", 1);
            }

            if ((flags & 2) != 0)
            {
                _head.Attr("showFormulas", 1);
            }

            if ((flags & 4) == 0)
            {
                _head.Attr("showGridLines", 0);
            }

            if ((flags & 8) == 0)
            {
                _head.Attr("showRowColHeaders", 0);
            }

            if ((flags & 16) == 0)
            {
                _head.Attr("showZeros", 0);
            }

            if ((flags & 32) != 0)
            {
                _head.Attr("rightToLeft", 1);
            }

            if ((flags & 64) != 0)
            {
                _head.Attr("tabSelected", 1);
            }

            if ((flags & 128) == 0)
            {
                _head.Attr("showRuler", 0);
            }

            if ((flags & 256) == 0)
            {
                _head.Attr("showOutlineSymbols", 0);
            }

            if ((flags & 512) == 0)
            {
                _head.Attr("defaultGridColor", 0);
            }

            if (view == 1)
            {
                _head.Attr("view", "pageBreakPreview");
            }
            else if (view == 2)
            {
                _head.Attr("view", "pageLayout");
            }

            if (top != 0 || left != 0)
            {
                _head.Attr("topLeftCell", XlsbAddress.Cell(top, left));
            }

            if ((flags & 512) == 0)
            {
                _head.Attr("colorId", icv);
            }

            if (scale != 100 && scale != 0)
            {
                _head.Attr("zoomScale", scale);
            }

            if (scaleNormal != 100 && scaleNormal != 0)
            {
                _head.Attr("zoomScaleNormal", scaleNormal);
            }

            if (scaleSlv != 0)
            {
                _head.Attr("zoomScaleSheetLayoutView", scaleSlv);
            }

            if (scalePlv != 0)
            {
                _head.Attr("zoomScalePageLayoutView", scalePlv);
            }

            _head.Attr("workbookViewId", wbView).Raw(">");
            _inView = true;
        }

        private void Pane(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            double x = f.Number();
            double y = f.Number();
            int top = f.I32();
            int left = f.I32();
            int active = f.I32();
            int flags = f.U8();
            bool frozen = (flags & 1) != 0;
            bool frozenNoSplit = (flags & 2) != 0;
            _head.Raw("<pane");
            if (x != 0)
            {
                _head.Attr("xSplit", x);
            }

            if (y != 0)
            {
                _head.Attr("ySplit", y);
            }

            _head.Attr("topLeftCell", XlsbAddress.Cell(top, left));
            if (active is >= 0 and <= 3)
            {
                _head.Attr("activePane", SelPane[active]);
            }

            _head.Attr("state", frozen ? (frozenNoSplit ? "frozenSplit" : "frozen") : "split").Raw("/>");
        }

        private void Selection(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int pane = f.I32();
            int row = f.I32();
            int col = f.I32();
            int rfxAct = f.I32();
            int count = f.I32();
            var refs = new List<string>();
            for (int i = 0; i < count && !f.Overrun && i < 4096; i++)
            {
                int r1 = f.I32();
                int r2 = f.I32();
                int c1 = f.I32();
                int c2 = f.I32();
                refs.Add(XlsbAddress.Range(r1, r2, c1, c2));
            }

            _ = rfxAct;
            _head.Raw("<selection");
            if (pane != 3 && pane is >= 0 and <= 3)
            {
                _head.Attr("pane", SelPane[pane]);
            }

            _head.Attr("activeCell", XlsbAddress.Cell(row, col));
            if (refs.Count > 0)
            {
                _head.Attr("sqref", string.Join(' ', refs));
            }

            _head.Raw("/>");
        }

        private void FormatInfo(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            uint dxGCol = f.U32();
            int cch = f.U16();
            int miy = f.U16();
            int flags = f.U16();
            int outRow = f.U8();
            int outCol = f.U8();
            _head.Raw("<sheetFormatPr");
            if (cch != 0)
            {
                _head.Attr("baseColWidth", cch);
            }

            if (dxGCol != 0xFFFFFFFF)
            {
                _head.Attr("defaultColWidth", dxGCol / 256.0);
            }

            _head.Attr("defaultRowHeight", miy / 20.0);
            if ((flags & 1) != 0)
            {
                _head.Attr("customHeight", 1);
            }

            if ((flags & 2) != 0)
            {
                _head.Attr("zeroHeight", 1);
            }

            if ((flags & 4) != 0)
            {
                _head.Attr("thickTop", 1);
            }

            if ((flags & 8) != 0)
            {
                _head.Attr("thickBottom", 1);
            }

            if (outRow != 0)
            {
                _head.Attr("outlineLevelRow", outRow);
            }

            if (outCol != 0)
            {
                _head.Attr("outlineLevelCol", outCol);
            }

            _head.Raw("/>");
        }

        private void ColInfo(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int first = f.I32();
            int last = f.I32();
            uint dx = f.U32();
            int style = f.I32();
            int flags = f.U16();
            if (f.Overrun)
            {
                Short("column");
            }

            _head.Raw("<col").Attr("min", first + 1).Attr("max", last + 1).Attr("width", dx / 256.0);
            if (style != 0)
            {
                _head.Attr("style", style);
            }

            if ((flags & 1) != 0)
            {
                _head.Attr("hidden", 1);
            }

            if ((flags & 4) != 0)
            {
                _head.Attr("bestFit", 1);
            }

            if ((flags & 2) != 0)
            {
                _head.Attr("customWidth", 1);
            }

            if ((flags & 8) != 0)
            {
                _head.Attr("phonetic", 1);
            }

            int level = (flags >> 8) & 7;
            if (level != 0)
            {
                _head.Attr("outlineLevel", level);
            }

            if ((flags & 0x1000) != 0)
            {
                _head.Attr("collapsed", 1);
            }

            _head.Raw("/>");
        }

        private void RowHeader(ReadOnlySpan<byte> d)
        {
            CloseRow();
            var f = new XlsbRecordFields(d);
            int row = f.I32();
            int style = f.I32();
            int height = f.U16();
            int b1 = f.U8();
            int b2 = f.U8();
            int b3 = f.U8();
            if (f.Overrun)
            {
                Short("row header");
            }

            _rowIndex = row;
            _x.Raw("<row").Attr("r", row + 1);
            bool ghost = (b2 & 0x40) != 0;
            if (ghost)
            {
                _x.Attr("s", style).Attr("customFormat", 1);
            }

            if ((b2 & 0x20) != 0)
            {
                _x.Attr("ht", height / 20.0).Attr("customHeight", 1);
            }
            else if (height != 0)
            {
                _x.Attr("ht", height / 20.0);
            }

            if ((b2 & 0x10) != 0)
            {
                _x.Attr("hidden", 1);
            }

            int level = b2 & 7;
            if (level != 0)
            {
                _x.Attr("outlineLevel", level);
            }

            if ((b2 & 8) != 0)
            {
                _x.Attr("collapsed", 1);
            }

            if ((b1 & 1) != 0)
            {
                _x.Attr("thickTop", 1);
            }

            if ((b1 & 2) != 0)
            {
                _x.Attr("thickBot", 1);
            }

            if ((b3 & 1) != 0)
            {
                _x.Attr("ph", 1);
            }

            _x.Raw(">");
            _inRow = true;
            _rowCount++;
        }

        private int _rowIndex;

        private void ValueCell(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int col = f.I32();
            int word = f.I32();
            int style = word & 0xFFFFFF;
            int row = _rowIndex;
            string address = XlsbAddress.Cell(row, col);
            _x.Raw("<c").Attr("r", address);
            if (style != 0)
            {
                _x.Attr("s", style);
            }

            switch (_r.Type)
            {
                case XlsbRecordType.CellBlank:
                    _x.Raw("/>");
                    return;
                case XlsbRecordType.CellRk:
                    _x.Raw("><v>").Number(Rk(f.U32())).Raw("</v></c>");
                    break;
                case XlsbRecordType.CellReal:
                    _x.Raw("><v>").Number(f.Number()).Raw("</v></c>");
                    break;
                case XlsbRecordType.CellBool:
                    _x.Attr("t", "b").Raw("><v>").Raw(f.U8() != 0 ? "1" : "0").Raw("</v></c>");
                    break;
                case XlsbRecordType.CellError:
                    _x.Attr("t", "e").Raw("><v>").Raw(ErrorText(f.U8())).Raw("</v></c>");
                    break;
                case XlsbRecordType.CellIsst:
                    _x.Attr("t", "s").Raw("><v>").Int(f.U32()).Raw("</v></c>");
                    break;
                case XlsbRecordType.CellSt:
                    _x.Attr("t", "inlineStr").Raw("><is>");
                    {
                        string text = f.WideString();
                        _x.Raw("<t");
                        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\n', StringComparison.Ordinal)))
                        {
                            _x.Raw(" xml:space=\"preserve\"");
                        }

                        _x.Raw(">").Text(text).Raw("</t>");
                    }

                    _x.Raw("</is></c>");
                    break;
                case XlsbRecordType.CellRString:
                {
                    _x.Attr("t", "inlineStr").Raw("><is>");
                    var inner = new XmlSink();
                    XlsbSharedStringsPart.RichInner(inner, d[8..], fonts, context, partName, _r.Offset);
                    _x.Raw(inner.Memory.Span).Raw("</is></c>");
                    break;
                }
            }

            if (f.Overrun)
            {
                Short("cell");
            }
        }

        internal static double Rk(uint v)
        {
            // [MS-XLSB] §2.5.123: 30 bits of number, either a signed integer or the top bits of a double, optionally divided by 100.
            double n = (v & 2) != 0 ? (int)v >> 2 : BitConverter.Int64BitsToDouble((long)(v & 0xFFFFFFFC) << 32);
            return (v & 1) != 0 ? n / 100.0 : n;
        }

        private static string ErrorText(int code) => CalcErrors.ToText(XlsbFormula.ErrorFromStored(code));

        private void FormulaCell(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int col = f.I32();
            int word = f.I32();
            int style = word & 0xFFFFFF;
            int row = _rowIndex;
            string type;
            string value;
            switch (_r.Type)
            {
                case XlsbRecordType.FmlaString:
                    type = "str";
                    value = f.WideString();
                    break;
                case XlsbRecordType.FmlaNum:
                    type = string.Empty;
                    value = f.Number().ToString("R", CultureInfo.InvariantCulture);
                    break;
                case XlsbRecordType.FmlaBool:
                    type = "b";
                    value = f.U8() != 0 ? "1" : "0";
                    break;
                default:
                    type = "e";
                    value = ErrorText(f.U8());
                    break;
            }

            int flags = f.U16();
            int cce = (int)f.U32();
            if (cce < 0 || cce > f.Remaining)
            {
                Short("formula cell");
                WriteCell(row, col, style, type, value, null, false);
                return;
            }

            ReadOnlySpan<byte> rgce = f.Bytes(cce);
            int cb = (int)f.U32();
            ReadOnlySpan<byte> rgcb = cb >= 0 && cb <= f.Remaining ? f.Bytes(cb) : default;
            bool alwaysCalc = (flags & 1) != 0;
            if (rgce.Length >= 5 && rgce[0] == 0x01)
            {
                // PtgExp: the formula lives in a shared or array formula record ([MS-XLSB] §2.5.98.40).
                int mr = BinaryPrimitives.ReadInt32LittleEndian(rgce[1..]);
                int mc = rgcb.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(rgcb) : col;
                if (mr == row && mc == col)
                {
                    _pending = new PendingCell { Row = row, Column = col, Style = style, CellType = type, Value = value, AlwaysCalc = alwaysCalc };
                    return;
                }

                if (_sharedIndex.TryGetValue((mr, mc), out int si))
                {
                    WriteCell(row, col, style, type, value, "<f t=\"shared\" si=\"" + si.ToString(CultureInfo.InvariantCulture) + "\"" + (alwaysCalc ? " ca=\"1\"" : string.Empty) + "/>", false);
                }
                else
                {
                    WriteCell(row, col, style, type, value, null, false);
                }

                return;
            }

            string? text = Decode(rgce, rgcb, XlsbFormulaKind.Cell, row, col, row, col);
            WriteCell(row, col, style, type, value, text is null ? null : FormulaElement(text, alwaysCalc, null, null, -1), false);
        }

        private string? Decode(ReadOnlySpan<byte> rgce, ReadOnlySpan<byte> rgcb, XlsbFormulaKind kind, int originRow, int originCol, int row, int col)
        {
            if (XlsbFormula.TryDecode(rgce, rgcb, formulas, kind, originRow, originCol, out FormulaNode node, out string? problem))
            {
                return FormulaPrinter.Print(node, new FormulaPrintOptions { Form = FormulaForm.File });
            }

            context.Defects.Report(new Defect(DefectKind.Formula, "xlsb.formula-unreadable", DefectSeverity.Dropped,
                "The formula of " + XlsbAddress.Cell(row, col) + " could not be read (" + problem + "); the cell keeps its cached value.",
                DefectLocation.InCell(partName, XlsbAddress.Cell(row, col))));
            return null;
        }

        private static string FormulaElement(string text, bool alwaysCalc, string? type, string? reference, int shared)
        {
            var x = new XmlSink(text.Length + 48);
            x.Raw("<f");
            if (type is not null)
            {
                x.Attr("t", type);
            }

            if (reference is not null)
            {
                x.Attr("ref", reference);
            }

            if (shared >= 0)
            {
                x.Attr("si", shared);
            }

            if (alwaysCalc)
            {
                x.Attr("ca", 1);
            }

            x.Raw(">").Text(text).Raw("</f>");
            return x.ToString();
        }

        private void WriteCell(int row, int col, int style, string type, string value, string? formulaXml, bool isString)
        {
            _ = isString;
            _x.Raw("<c").Attr("r", XlsbAddress.Cell(row, col));
            if (style != 0)
            {
                _x.Attr("s", style);
            }

            if (type.Length > 0)
            {
                _x.Attr("t", type);
            }

            _x.Raw(">");
            if (formulaXml is not null)
            {
                _x.Raw(formulaXml);
            }

            _x.Raw("<v>").Text(value).Raw("</v></c>");
        }

        private void SharedOrArray(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int r1 = f.I32();
            int r2 = f.I32();
            int c1 = f.I32();
            int c2 = f.I32();
            bool isArray = _r.Type == XlsbRecordType.ArrFmla;
            int flags = 0;
            if (isArray)
            {
                flags = f.U8();
            }

            int cce = (int)f.U32();
            if (cce < 0 || cce > f.Remaining)
            {
                Short("shared or array formula");
                FlushPending(null, null, null, null);
                return;
            }

            ReadOnlySpan<byte> rgce = f.Bytes(cce);
            int cb = (int)f.U32();
            ReadOnlySpan<byte> rgcb = cb >= 0 && cb <= f.Remaining ? f.Bytes(cb) : default;
            if (_pending is null)
            {
                return;
            }

            PendingCell p = _pending;
            string? text = Decode(rgce, rgcb, isArray ? XlsbFormulaKind.Array : XlsbFormulaKind.Shared, p.Row, p.Column, p.Row, p.Column);
            string range = XlsbAddress.Range(r1, r2, c1, c2);
            if (isArray)
            {
                _arrayMasters[(p.Row, p.Column)] = true;
                FlushPending(text, "array", range, null, (flags & 1) != 0);
            }
            else
            {
                int si = _nextShared++;
                _sharedIndex[(p.Row, p.Column)] = si;
                FlushPending(text, "shared", range, si);
            }
        }

        private void FlushPending(string? text, string? type, string? range, int? shared, bool alwaysCalc = false)
        {
            PendingCell? p = _pending;
            _pending = null;
            if (p is null)
            {
                return;
            }

            string? xml = text is null ? null : FormulaElement(text, p.AlwaysCalc || alwaysCalc, type, range, shared ?? -1);
            WriteCell(p.Row, p.Column, p.Style, p.CellType, p.Value, xml, false);
        }

        private void Hyperlink(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int r1 = f.I32();
            int r2 = f.I32();
            int c1 = f.I32();
            int c2 = f.I32();
            string? rel = f.NullableWideString();
            string location = f.WideString();
            string tooltip = f.WideString();
            string display = f.WideString();
            if (f.Overrun)
            {
                Short("hyperlink");
            }

            if (!_inHlinks)
            {
                CloseRow();
                _x.Raw("<hyperlinks>");
                _inHlinks = true;
            }

            _x.Raw("<hyperlink").Attr("ref", XlsbAddress.Range(r1, r2, c1, c2));
            if (!string.IsNullOrEmpty(rel))
            {
                _x.Attr("r:id", rel);
            }

            if (location.Length > 0)
            {
                _x.Attr("location", location);
            }

            if (tooltip.Length > 0)
            {
                _x.Attr("tooltip", tooltip);
            }

            if (display.Length > 0)
            {
                _x.Attr("display", display);
            }

            _x.Raw("/>");
        }

        private void HeaderFooter(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int flags = f.U16();
            string?[] strings = new string?[6];
            for (int i = 0; i < 6; i++)
            {
                strings[i] = f.NullableWideString();
            }

            _x.Raw("<headerFooter");
            if ((flags & 1) != 0)
            {
                _x.Attr("differentOddEven", 1);
            }

            if ((flags & 2) != 0)
            {
                _x.Attr("differentFirst", 1);
            }

            if ((flags & 4) == 0)
            {
                _x.Attr("scaleWithDoc", 0);
            }

            if ((flags & 8) == 0)
            {
                _x.Attr("alignWithMargins", 0);
            }

            _x.Raw(">");
            string[] names = ["oddHeader", "oddFooter", "evenHeader", "evenFooter", "firstHeader", "firstFooter"];
            for (int i = 0; i < 6; i++)
            {
                if (strings[i] is { Length: > 0 } t)
                {
                    _x.Raw("<").Raw(names[i]).Raw(">").Text(t).Raw("</").Raw(names[i]).Raw(">");
                }
            }

            _x.Raw("</headerFooter>");
        }

        private void PageSetup(ReadOnlySpan<byte> d)
        {
            var f = new XlsbRecordFields(d);
            int paper = f.I32();
            int scale = f.I32();
            int hres = f.I32();
            int vres = f.I32();
            int copies = f.I32();
            int first = f.I32();
            int fitW = f.I32();
            int fitH = f.I32();
            int flags = f.U16();
            string? rel = f.NullableWideString();
            if (f.Overrun)
            {
                Short("page setup");
            }

            _x.Raw("<pageSetup");
            if (paper != 0)
            {
                _x.Attr("paperSize", paper);
            }

            if (scale != 100)
            {
                _x.Attr("scale", scale);
            }

            if ((flags & 0x80) != 0)
            {
                _x.Attr("firstPageNumber", first);
            }

            if (fitW != 1)
            {
                _x.Attr("fitToWidth", fitW);
            }

            if (fitH != 1)
            {
                _x.Attr("fitToHeight", fitH);
            }

            if ((flags & 1) == 0)
            {
                _x.Attr("pageOrder", "downThenOver");
            }

            if ((flags & 4) == 0)
            {
                _x.Attr("orientation", (flags & 2) != 0 ? "landscape" : "portrait");
            }
            else if ((flags & 2) != 0)
            {
                _x.Attr("orientation", "landscape");
            }

            if ((flags & 8) != 0)
            {
                _x.Attr("blackAndWhite", 1);
            }

            if ((flags & 16) != 0)
            {
                _x.Attr("draft", 1);
            }

            if ((flags & 0x80) != 0)
            {
                _x.Attr("useFirstPageNumber", 1);
            }

            if (hres != 600 && hres != 0)
            {
                _x.Attr("horizontalDpi", hres);
            }

            if (vres != 600 && vres != 0)
            {
                _x.Attr("verticalDpi", vres);
            }

            if (copies != 1)
            {
                _x.Attr("copies", copies);
            }

            if (!string.IsNullOrEmpty(rel))
            {
                _x.Attr("r:id", rel);
            }

            _x.Raw("/>");
        }
    }
}
