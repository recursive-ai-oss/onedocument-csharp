using System.Linq;
using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Xml2003;

internal sealed partial class SpreadsheetMl2003Converter
{
    private sealed class SheetState
    {
        public List<string> Merges { get; } = [];

        public List<(string Ref, string Target, string? Tip)> Links { get; } = [];

        public List<(int Row, int Col, string Author, string Text)> Comments { get; } = [];

        public int MaxRow { get; set; }

        public int MaxCol { get; set; }
    }

    private void WriteWorksheet(
        OpcPackage package,
        MarkupNode ws,
        string sheetName,
        int number,
        List<(string Name, string ContentType, ReadOnlyMemory<byte> Content)> parts,
        List<Relationship> workbookRels)
    {
        int sheetIndex = number - 1;
        string partName = "/xl/worksheets/sheet" + number.ToString(CultureInfo.InvariantCulture) + ".xml";
        string relId = "rId" + number.ToString(CultureInfo.InvariantCulture);
        _sheetRelIds.Add(relId);
        workbookRels.Add(new Relationship(relId, RelBase + "worksheet", "worksheets/sheet" + number.ToString(CultureInfo.InvariantCulture) + ".xml"));

        MarkupNode table = Child(ws, "Table");
        MarkupNode options = Child(ws, "WorksheetOptions");
        MarkupNode names = Child(ws, "Names");
        if (!names.IsNull)
        {
            ReadNames(names, sheetIndex);
        }

        // Column properties first: a cell without a style takes its column's.
        var columns = new List<(int Min, int Max, MarkupNode Node)>();
        var columnStyles = new Dictionary<int, string>();
        int nextColumn = 0;
        if (!table.IsNull)
        {
            foreach (MarkupNode c in table.ChildElements)
            {
                if (Name(c) != "Column")
                {
                    continue;
                }

                int index = IntAttr(c, "Index");
                int min = index > 0 && index > nextColumn ? index : nextColumn + 1;
                if (index > 0 && index <= nextColumn)
                {
                    Defect("sml2003.bad-index", DefectSeverity.Repaired, "A column index goes backwards; the column was placed after the previous one.", c);
                }

                int max = min + Math.Max(0, IntAttr(c, "Span"));
                if (max > CellAddress.MaxColumns)
                {
                    Defect("sml2003.bad-index", DefectSeverity.Dropped, "A column is beyond the last column of a sheet.", c);
                    break;
                }

                nextColumn = max;
                columns.Add((min, max, c));
                if (Attr(c, "StyleID") is { } sid)
                {
                    for (int k = min; k <= max; k++)
                    {
                        columnStyles[k] = sid;
                    }
                }
            }
        }

        var state = new SheetState();
        var dataBuffer = new ArrayBufferWriter<byte>();
        var data = new XmlWriter(dataBuffer);
        data.WriteStartElement("sheetData");
        if (!table.IsNull)
        {
            WriteRows(data, table, sheetName, columnStyles, state);
        }

        data.WriteEndElement();
        data.Complete();

        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        w.WriteXmlDeclaration();
        w.WriteStartElement("worksheet");
        w.WriteNamespaceDeclaration(null, MainNs);
        w.WriteNamespaceDeclaration("r", RNs);

        var rels = new List<Relationship>();
        var sheetRelParts = new List<(string Name, string ContentType, ReadOnlyMemory<byte> Content)>();

        bool fitToPage = !options.IsNull && !Child(options, "FitToPage").IsNull;
        if (fitToPage)
        {
            w.WriteStartElement("sheetPr");
            w.WriteStartElement("pageSetUpPr");
            w.WriteAttribute("fitToPage", "1");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        WriteSheetViews(w, options);
        if (!table.IsNull && (Attr(table, "DefaultRowHeight") is not null || Attr(table, "DefaultColumnWidth") is not null))
        {
            w.WriteStartElement("sheetFormatPr");
            double rowHeight = double.TryParse(Attr(table, "DefaultRowHeight"), NumberStyles.Float, CultureInfo.InvariantCulture, out double rh) && rh > 0 ? rh : 12.75;
            w.WriteAttribute("defaultRowHeight", Inv(rowHeight));
            if (double.TryParse(Attr(table, "DefaultColumnWidth"), NumberStyles.Float, CultureInfo.InvariantCulture, out double cw) && cw > 0)
            {
                w.WriteAttribute("defaultColWidth", Inv(WidthChars(cw)));
            }

            w.WriteEndElement();
        }

        WriteColumns(w, columns);
        w.WriteRaw(dataBuffer.WrittenSpan);

        if (!options.IsNull && IsTrue(Child(options, "ProtectContents").InnerText.Trim()))
        {
            w.WriteStartElement("sheetProtection");
            w.WriteAttribute("sheet", "1");
            if (IsTrue(Child(options, "ProtectObjects").InnerText.Trim()))
            {
                w.WriteAttribute("objects", "1");
            }

            if (IsTrue(Child(options, "ProtectScenarios").InnerText.Trim()))
            {
                w.WriteAttribute("scenarios", "1");
            }

            w.WriteEndElement();
        }

        MarkupNode filter = Child(ws, "AutoFilter");
        if (!filter.IsNull && Attr(filter, "Range") is { } filterRange && RangeToA1(filterRange) is { } filterRef)
        {
            w.WriteStartElement("autoFilter");
            w.WriteAttribute("ref", filterRef);
            w.WriteEndElement();
        }

        if (state.Merges.Count > 0)
        {
            w.WriteStartElement("mergeCells");
            w.WriteAttribute("count", state.Merges.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string m in state.Merges)
            {
                w.WriteStartElement("mergeCell");
                w.WriteAttribute("ref", m);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        int priority = 1;
        foreach (MarkupNode cf in ws.ChildElements)
        {
            if (Name(cf) == "ConditionalFormatting")
            {
                WriteConditionalFormatting(w, cf, ref priority);
            }
        }

        WriteDataValidations(w, ws);

        if (state.Links.Count > 0)
        {
            w.WriteStartElement("hyperlinks");
            foreach ((string reference, string target, string? tip) in state.Links)
            {
                w.WriteStartElement("hyperlink");
                w.WriteAttribute("ref", reference);
                if (target.StartsWith('#'))
                {
                    w.WriteAttribute("location", target[1..]);
                }
                else
                {
                    // Only a relationship is written: the target is never fetched.
                    string id = "rId" + (rels.Count + 1).ToString(CultureInfo.InvariantCulture);
                    rels.Add(new Relationship(id, RelBase + "hyperlink", target, RelationshipTargetMode.External));
                    w.WriteAttribute("r:id", id);
                }

                if (tip is { Length: > 0 })
                {
                    w.WriteAttribute("tooltip", tip);
                }

                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        WritePageSetup(w, options);

        if (state.Comments.Count > 0)
        {
            string commentsName = "/xl/comments" + number.ToString(CultureInfo.InvariantCulture) + ".xml";
            string vmlName = "/xl/drawings/vmlDrawing" + number.ToString(CultureInfo.InvariantCulture) + ".vml";
            sheetRelParts.Add((commentsName, CtBase + "comments+xml", BuildComments(state.Comments)));
            sheetRelParts.Add((vmlName, "application/vnd.openxmlformats-officedocument.vmlDrawing", BuildVml(state.Comments)));
            rels.Add(new Relationship("rId" + (rels.Count + 1).ToString(CultureInfo.InvariantCulture), RelBase + "comments", "../comments" + number.ToString(CultureInfo.InvariantCulture) + ".xml"));
            string vmlId = "rId" + (rels.Count + 1).ToString(CultureInfo.InvariantCulture);
            rels.Add(new Relationship(vmlId, RelBase + "vmlDrawing", "../drawings/vmlDrawing" + number.ToString(CultureInfo.InvariantCulture) + ".vml"));
            w.WriteStartElement("legacyDrawing");
            w.WriteAttribute("r:id", vmlId);
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.Complete();

        parts.Add((partName, CtBase + "worksheet+xml", buffer.WrittenMemory));
        parts.AddRange(sheetRelParts);
        if (rels.Count > 0)
        {
            package.SetRelationships(partName, rels);
        }

        if (!options.IsNull)
        {
            foreach (MarkupNode o in options.ChildElements)
            {
                if (Name(o) is "PageBreaks" or "Panes")
                {
                    Lost("loss.page-breaks", "manual page breaks", "Manual page breaks were not carried over.");
                }
            }
        }
    }

    // Column widths: points in 2003, characters of the default font's digit in XLSX. At 96 dpi with a 7 pixel digit and 5 pixels of
    // padding (Arial 10), 48 points is the 8.43 characters of a default column.
    private static double WidthChars(double points) => Math.Round(Math.Max(0, ((points * 96 / 72) - 5) / 7), 2);

    private static void WriteColumns(XmlWriter w, List<(int Min, int Max, MarkupNode Node)> columns)
    {
        if (columns.Count == 0)
        {
            return;
        }

        bool any = false;
        foreach ((int min, int max, MarkupNode node) in columns)
        {
            bool hasWidth = double.TryParse(Attr(node, "Width"), NumberStyles.Float, CultureInfo.InvariantCulture, out double width) && width >= 0;
            bool hidden = IsTrue(Attr(node, "Hidden"));
            if (!hasWidth && !hidden)
            {
                continue;
            }

            if (!any)
            {
                w.WriteStartElement("cols");
                any = true;
            }

            w.WriteStartElement("col");
            w.WriteAttribute("min", min.ToString(CultureInfo.InvariantCulture));
            w.WriteAttribute("max", max.ToString(CultureInfo.InvariantCulture));
            w.WriteAttribute("width", Inv(hasWidth ? WidthChars(width) : 8.43));
            if (hasWidth)
            {
                w.WriteAttribute("customWidth", "1");
            }

            if (hidden)
            {
                w.WriteAttribute("hidden", "1");
            }

            w.WriteEndElement();
        }

        if (any)
        {
            w.WriteEndElement();
        }
    }

    // ---- rows and cells ----------------------------------------------------------------------------------------------------------------

    private void WriteRows(XmlWriter w, MarkupNode table, string sheet, Dictionary<int, string> columnStyles, SheetState state)
    {
        int last = 0;
        foreach (MarkupNode row in table.ChildElements)
        {
            if (Name(row) != "Row")
            {
                continue;
            }

            _ct.ThrowIfCancellationRequested();
            int index = IntAttr(row, "Index");
            int r = index > 0 && index > last ? index : last + 1;
            if (index > 0 && index <= last)
            {
                Defect("sml2003.bad-index", DefectSeverity.Repaired, "A row index goes backwards; the row was placed after the previous one.", row);
            }

            int span = Math.Max(0, IntAttr(row, "Span"));
            if (r > CellAddress.MaxRows)
            {
                Defect("sml2003.bad-index", DefectSeverity.Dropped, "A row is beyond the last row of a sheet.", row);
                break;
            }

            last = Math.Min(CellAddress.MaxRows, r + span);
            for (int k = 0; k <= span && r + k <= CellAddress.MaxRows; k++)
            {
                WriteRow(w, row, r + k, sheet, columnStyles, state, withCells: k == 0);
            }
        }
    }

    private void WriteRow(XmlWriter w, MarkupNode row, int r, string sheet, Dictionary<int, string> columnStyles, SheetState state, bool withCells)
    {
        string? styleId = Attr(row, "StyleID");
        bool hasHeight = double.TryParse(Attr(row, "Height"), NumberStyles.Float, CultureInfo.InvariantCulture, out double height) && height >= 0;
        bool hidden = IsTrue(Attr(row, "Hidden"));
        var cells = new List<MarkupNode>();
        if (withCells)
        {
            foreach (MarkupNode c in row.ChildElements)
            {
                if (Name(c) == "Cell")
                {
                    cells.Add(c);
                }
            }
        }

        if (cells.Count == 0 && styleId is null && !hasHeight && !hidden)
        {
            return;
        }

        state.MaxRow = Math.Max(state.MaxRow, r);
        w.WriteStartElement("row");
        w.WriteAttribute("r", r.ToString(CultureInfo.InvariantCulture));
        int rowStyle = styleId is null ? 0 : StyleIndex(styleId, row);
        if (rowStyle != 0)
        {
            w.WriteAttribute("s", rowStyle.ToString(CultureInfo.InvariantCulture));
            w.WriteAttribute("customFormat", "1");
        }

        if (hasHeight)
        {
            w.WriteAttribute("ht", Inv(height));
            if (Attr(row, "AutoFitHeight") == "0")
            {
                w.WriteAttribute("customHeight", "1");
            }
        }

        if (hidden)
        {
            w.WriteAttribute("hidden", "1");
        }

        int col = 0;
        foreach (MarkupNode cell in cells)
        {
            int index = IntAttr(cell, "Index");
            int c = index > 0 && index > col ? index : col + 1;
            if (index > 0 && index <= col)
            {
                Defect("sml2003.bad-index", DefectSeverity.Repaired, "A cell index goes backwards; the cell was placed after the previous one.", cell, DefectLocation.InCell(sheet, CellAddress.ColumnName(Math.Min(col + 1, CellAddress.MaxColumns)) + r.ToString(CultureInfo.InvariantCulture)));
            }

            if (c > CellAddress.MaxColumns)
            {
                Defect("sml2003.bad-index", DefectSeverity.Dropped, "A cell is beyond the last column of a sheet.", cell, DefectLocation.InCell(sheet, "XFD" + r.ToString(CultureInfo.InvariantCulture)));
                break;
            }

            int across = Math.Max(0, IntAttr(cell, "MergeAcross"));
            int down = Math.Max(0, IntAttr(cell, "MergeDown"));
            across = Math.Min(across, CellAddress.MaxColumns - c);
            down = Math.Min(down, CellAddress.MaxRows - r);
            col = c + across;
            WriteCell(w, cell, r, c, sheet, styleId ?? (columnStyles.TryGetValue(c, out string? cs) ? cs : null), state);
            if (across > 0 || down > 0)
            {
                state.Merges.Add(CellAddress.ColumnName(c) + r.ToString(CultureInfo.InvariantCulture) + ":" + CellAddress.ColumnName(c + across) + (r + down).ToString(CultureInfo.InvariantCulture));
            }
        }

        w.WriteEndElement();
    }

    private void WriteCell(XmlWriter w, MarkupNode cell, int r, int c, string sheet, string? fallbackStyle, SheetState state)
    {
        string address = CellAddress.ColumnName(c) + r.ToString(CultureInfo.InvariantCulture);
        DefectLocation where = DefectLocation.InCell(sheet, address);
        state.MaxCol = Math.Max(state.MaxCol, c);
        string? styleId = Attr(cell, "StyleID") ?? fallbackStyle;
        int style = styleId is null ? 0 : StyleIndex(styleId, cell);
        MarkupNode data = Child(cell, "Data");
        string type = Attr(data, "Type") ?? "String";
        string? formula = Attr(cell, "Formula");
        string? a1 = null;
        if (!string.IsNullOrEmpty(formula))
        {
            a1 = ToA1(formula, r, c, cell, where);
            if (a1 is not null && a1.StartsWith('='))
            {
                a1 = a1[1..];
            }
        }

        string? arrayRange = Attr(cell, "ArrayRange");
        string? arrayRef = arrayRange is null ? null : RangeToA1(arrayRange);

        w.WriteStartElement("c");
        w.WriteAttribute("r", address);
        if (style != 0)
        {
            w.WriteAttribute("s", style.ToString(CultureInfo.InvariantCulture));
        }

        string? value = null;
        string? cellType = null;
        List<(string Text, Dictionary<string, string> Props)>? rich = null;
        if (!data.IsNull)
        {
            switch (type)
            {
                case "Number":
                    string text = data.InnerText.Trim();
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
                    {
                        value = Inv(number);
                    }
                    else
                    {
                        Defect("sml2003.bad-number", DefectSeverity.Repaired, "'" + text + "' is not a number; it was kept as text.", data, where);
                        value = text;
                        cellType = a1 is null ? "inlineStr" : "str";
                    }

                    break;
                case "DateTime":
                    string iso = data.InnerText.Trim();
                    if (TryDateSerial(iso, out double serial))
                    {
                        value = Inv(serial);
                    }
                    else
                    {
                        Defect("sml2003.bad-date", DefectSeverity.Repaired, "'" + iso + "' is not a date; it was kept as text.", data, where);
                        value = iso;
                        cellType = a1 is null ? "inlineStr" : "str";
                    }

                    break;
                case "Boolean":
                    string flag = data.InnerText.Trim();
                    value = flag is "1" or "true" or "True" or "TRUE" ? "1" : "0";
                    cellType = "b";
                    break;
                case "Error":
                    value = data.InnerText.Trim();
                    cellType = "e";
                    break;
                default:
                    cellType = a1 is null ? "inlineStr" : "str";
                    rich = ReadRichText(data);
                    value = rich is null ? data.InnerText : string.Concat(rich.Select(static p => p.Text));
                    break;
            }
        }

        if (cellType is not null && cellType != "inlineStr")
        {
            w.WriteAttribute("t", cellType);
        }
        else if (cellType == "inlineStr")
        {
            w.WriteAttribute("t", "inlineStr");
        }

        if (a1 is not null)
        {
            w.WriteStartElement("f");
            if (arrayRef is not null)
            {
                w.WriteAttribute("t", "array");
                w.WriteAttribute("ref", arrayRef);
            }

            w.WriteText(a1);
            w.WriteEndElement();
        }

        if (cellType == "inlineStr")
        {
            w.WriteStartElement("is");
            if (rich is null)
            {
                WriteInlineText(w, "t", value ?? string.Empty);
            }
            else
            {
                foreach ((string text, Dictionary<string, string> props) in rich)
                {
                    w.WriteStartElement("r");
                    WriteRunProperties(w, props);
                    WriteInlineText(w, "t", text);
                    w.WriteEndElement();
                }
            }

            w.WriteEndElement();
        }
        else if (value is not null)
        {
            w.WriteStartElement("v");
            w.WriteText(value);
            w.WriteEndElement();
        }

        w.WriteEndElement();

        if (Attr(cell, "HRef") is { Length: > 0 } href)
        {
            state.Links.Add((address, href, Attr(cell, "HRefScreenTip")));
        }

        MarkupNode comment = Child(cell, "Comment");
        if (!comment.IsNull)
        {
            state.Comments.Add((r, c, Attr(comment, "Author") ?? string.Empty, comment.InnerText.Trim()));
        }

        MarkupNode named = Child(cell, "NamedCell");
        if (!named.IsNull && Attr(named, "Name") is { Length: > 0 } cellName)
        {
            string key = "-!" + (BuiltInNames.TryGetValue(cellName, out string? b) ? b : cellName);
            if (_nameKeys.Add(key))
            {
                _names.Add((BuiltInNames.TryGetValue(cellName, out string? bi) ? bi : cellName, "'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'!$" + CellAddress.ColumnName(c) + "$" + r.ToString(CultureInfo.InvariantCulture), null, false));
            }
        }
    }

    private static void WriteInlineText(XmlWriter w, string element, string text)
    {
        w.WriteStartElement(element);
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\n', StringComparison.Ordinal)))
        {
            w.WriteAttribute("xml:space", "preserve");
        }

        w.WriteText(text);
        w.WriteEndElement();
    }

    // Excel 2003 stores a date as ISO 8601 ("2004-05-17T00:00:00.000"). A time alone is written on the day 1899-12-31 (or 1899-12-30),
    // which has no serial of its own: it becomes a fraction of a day.
    private bool TryDateSerial(string iso, out double serial)
    {
        serial = 0;
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value))
        {
            return false;
        }

        if (value.Year < 1900)
        {
            serial = value.TimeOfDay.Ticks / (double)TimeSpan.TicksPerDay;
            return value.Year >= 1899;
        }

        return ExcelDate.TryToSerial(value, _dateSystem, out serial);
    }

    // ---- rich text ------------------------------------------------------------------------------------------------------------------

    // The HTML subset of a String cell's Data: B, I, U, S, Sub, Sup and Font (Face, Size, Color) nest inside it. Returns null when the
    // cell holds plain text.
    private static List<(string Text, Dictionary<string, string> Props)>? ReadRichText(MarkupNode data)
    {
        bool any = false;
        foreach (MarkupNode unused in data.ChildElements)
        {
            _ = unused;
            any = true;
            break;
        }

        if (!any)
        {
            return null;
        }

        var runs = new List<(string, Dictionary<string, string>)>();
        CollectRuns(data, new Dictionary<string, string>(StringComparer.Ordinal), runs, 0);
        return runs.Count == 0 || (runs.Count == 1 && runs[0].Item2.Count == 0) ? null : runs;
    }

    private static void CollectRuns(MarkupNode node, Dictionary<string, string> props, List<(string, Dictionary<string, string>)> runs, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        foreach (MarkupNode child in node.Children)
        {
            if (child.Kind == MarkupNodeKind.Element)
            {
                var next = new Dictionary<string, string>(props, StringComparer.Ordinal);
                switch (Name(child).ToLowerInvariant())
                {
                    case "b":
                        next["b"] = "1";
                        break;
                    case "i":
                        next["i"] = "1";
                        break;
                    case "u":
                        next["u"] = "1";
                        break;
                    case "s":
                        next["strike"] = "1";
                        break;
                    case "sub":
                        next["vert"] = "subscript";
                        break;
                    case "sup":
                        next["vert"] = "superscript";
                        break;
                    case "font":
                        if (Attr(child, "Face") is { } face)
                        {
                            next["face"] = face;
                        }

                        if (Attr(child, "Size") is { } size)
                        {
                            next["size"] = size;
                        }

                        if (Rgb(Attr(child, "Color")) is { } color)
                        {
                            next["color"] = color;
                        }

                        break;
                }

                CollectRuns(child, next, runs, depth + 1);
            }
            else if (child.Kind is MarkupNodeKind.Text or MarkupNodeKind.CData or MarkupNodeKind.Whitespace)
            {
                string text = child.InnerText;
                if (text.Length > 0)
                {
                    runs.Add((text, props));
                }
            }
        }
    }

    // CT_RPrElt (ECMA-376 Part 1 §18.4.7) is a choice, so the order of the properties is free.
    private static void WriteRunProperties(XmlWriter w, Dictionary<string, string> props)
    {
        if (props.Count == 0)
        {
            return;
        }

        w.WriteStartElement("rPr");
        if (props.TryGetValue("face", out string? face))
        {
            w.WriteStartElement("rFont");
            w.WriteAttribute("val", face);
            w.WriteEndElement();
        }

        foreach ((string key, string element) in new[] { ("b", "b"), ("i", "i"), ("strike", "strike"), ("u", "u") })
        {
            if (props.ContainsKey(key))
            {
                w.WriteStartElement(element);
                w.WriteEndElement();
            }
        }

        if (props.TryGetValue("vert", out string? vert))
        {
            w.WriteStartElement("vertAlign");
            w.WriteAttribute("val", vert);
            w.WriteEndElement();
        }

        if (props.TryGetValue("size", out string? size) && double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out double sz) && sz is > 0 and <= 409)
        {
            w.WriteStartElement("sz");
            w.WriteAttribute("val", Inv(sz));
            w.WriteEndElement();
        }

        if (props.TryGetValue("color", out string? color))
        {
            w.WriteStartElement("color");
            w.WriteAttribute("rgb", color);
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    // ---- sheet options ----------------------------------------------------------------------------------------------------------------

    private static void WriteSheetViews(XmlWriter w, MarkupNode options)
    {
        if (options.IsNull)
        {
            return;
        }

        bool selected = !Child(options, "Selected").IsNull;
        bool noGrid = !Child(options, "DoNotDisplayGridlines").IsNull;
        bool noHeadings = !Child(options, "DoNotDisplayHeadings").IsNull;
        bool rtl = !Child(options, "DisplayRightToLeft").IsNull;
        bool freeze = !Child(options, "FreezePanes").IsNull;
        _ = int.TryParse(Child(options, "SplitHorizontal").InnerText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int ySplit);
        _ = int.TryParse(Child(options, "SplitVertical").InnerText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int xSplit);
        _ = int.TryParse(Child(options, "TopRowBottomPane").InnerText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int topRow);
        _ = int.TryParse(Child(options, "LeftColumnRightPane").InnerText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int leftColumn);
        bool zoomed = int.TryParse(Child(options, "Zoom").InnerText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int zoom) && zoom is >= 10 and <= 400 && zoom != 100;
        bool pane = freeze && (xSplit > 0 || ySplit > 0) && xSplit < CellAddress.MaxColumns && ySplit < CellAddress.MaxRows;
        if (!selected && !noGrid && !noHeadings && !rtl && !pane && !zoomed)
        {
            return;
        }

        w.WriteStartElement("sheetViews");
        w.WriteStartElement("sheetView");
        if (noGrid)
        {
            w.WriteAttribute("showGridLines", "0");
        }

        if (noHeadings)
        {
            w.WriteAttribute("showRowColHeaders", "0");
        }

        if (rtl)
        {
            w.WriteAttribute("rightToLeft", "1");
        }

        if (selected)
        {
            w.WriteAttribute("tabSelected", "1");
        }

        if (zoomed)
        {
            w.WriteAttribute("zoomScale", zoom.ToString(CultureInfo.InvariantCulture));
        }

        w.WriteAttribute("workbookViewId", "0");
        if (pane)
        {
            w.WriteStartElement("pane");
            if (xSplit > 0)
            {
                w.WriteAttribute("xSplit", xSplit.ToString(CultureInfo.InvariantCulture));
            }

            if (ySplit > 0)
            {
                w.WriteAttribute("ySplit", ySplit.ToString(CultureInfo.InvariantCulture));
            }

            w.WriteAttribute("topLeftCell", CellAddress.ColumnName(Math.Min(CellAddress.MaxColumns, Math.Max(xSplit, leftColumn) + 1)) + (Math.Min(CellAddress.MaxRows - 1, Math.Max(ySplit, topRow)) + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttribute("activePane", xSplit > 0 && ySplit > 0 ? "bottomRight" : ySplit > 0 ? "bottomLeft" : "topRight");
            w.WriteAttribute("state", "frozen");
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WritePageSetup(XmlWriter w, MarkupNode options)
    {
        if (options.IsNull)
        {
            return;
        }

        MarkupNode setup = Child(options, "PageSetup");
        MarkupNode print = Child(options, "Print");
        if (!setup.IsNull)
        {
            MarkupNode layout = Child(setup, "Layout");
            if (!layout.IsNull && (IsTrue(Attr(layout, "CenterHorizontal")) || IsTrue(Attr(layout, "CenterVertical"))))
            {
                w.WriteStartElement("printOptions");
                if (IsTrue(Attr(layout, "CenterHorizontal")))
                {
                    w.WriteAttribute("horizontalCentered", "1");
                }

                if (IsTrue(Attr(layout, "CenterVertical")))
                {
                    w.WriteAttribute("verticalCentered", "1");
                }

                w.WriteEndElement();
            }

            MarkupNode margins = Child(setup, "PageMargins");
            MarkupNode header = Child(setup, "Header");
            MarkupNode footer = Child(setup, "Footer");
            if (!margins.IsNull || !header.IsNull || !footer.IsNull)
            {
                w.WriteStartElement("pageMargins");
                w.WriteAttribute("left", Margin(margins, "Left", 0.75));
                w.WriteAttribute("right", Margin(margins, "Right", 0.75));
                w.WriteAttribute("top", Margin(margins, "Top", 1));
                w.WriteAttribute("bottom", Margin(margins, "Bottom", 1));
                w.WriteAttribute("header", Margin(header, "Margin", 0.5));
                w.WriteAttribute("footer", Margin(footer, "Margin", 0.5));
                w.WriteEndElement();
            }

            WritePageSetupElement(w, layout, print);
            string? oddHeader = Attr(header, "Data");
            string? oddFooter = Attr(footer, "Data");
            if (!string.IsNullOrEmpty(oddHeader) || !string.IsNullOrEmpty(oddFooter))
            {
                w.WriteStartElement("headerFooter");
                if (!string.IsNullOrEmpty(oddHeader))
                {
                    w.WriteStartElement("oddHeader");
                    w.WriteText(oddHeader);
                    w.WriteEndElement();
                }

                if (!string.IsNullOrEmpty(oddFooter))
                {
                    w.WriteStartElement("oddFooter");
                    w.WriteText(oddFooter);
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }
        }
        else if (!print.IsNull)
        {
            WritePageSetupElement(w, default, print);
        }
    }

    private static string Margin(MarkupNode n, string attribute, double fallback) =>
        double.TryParse(Attr(n, attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0 && v < 50 ? Inv(v) : Inv(fallback);

    private static void WritePageSetupElement(XmlWriter w, MarkupNode layout, MarkupNode print)
    {
        bool landscape = Attr(layout, "Orientation") == "Landscape";
        string? scale = print.IsNull ? null : Child(print, "Scale").InnerText.Trim();
        string? paper = print.IsNull ? null : Child(print, "PaperSizeIndex").InnerText.Trim();
        string? fitWidth = print.IsNull ? null : Child(print, "FitWidth").InnerText.Trim();
        string? fitHeight = print.IsNull ? null : Child(print, "FitHeight").InnerText.Trim();
        bool hasScale = int.TryParse(scale, NumberStyles.None, CultureInfo.InvariantCulture, out int s) && s is >= 10 and <= 400;
        bool hasPaper = int.TryParse(paper, NumberStyles.None, CultureInfo.InvariantCulture, out int p) && p > 0;
        bool hasW = int.TryParse(fitWidth, NumberStyles.None, CultureInfo.InvariantCulture, out int fw);
        bool hasH = int.TryParse(fitHeight, NumberStyles.None, CultureInfo.InvariantCulture, out int fh);
        if (!landscape && !hasScale && !hasPaper && !hasW && !hasH)
        {
            return;
        }

        w.WriteStartElement("pageSetup");
        if (hasPaper)
        {
            w.WriteAttribute("paperSize", p.ToString(CultureInfo.InvariantCulture));
        }

        if (hasScale)
        {
            w.WriteAttribute("scale", s.ToString(CultureInfo.InvariantCulture));
        }

        if (hasW)
        {
            w.WriteAttribute("fitToWidth", fw.ToString(CultureInfo.InvariantCulture));
        }

        if (hasH)
        {
            w.WriteAttribute("fitToHeight", fh.ToString(CultureInfo.InvariantCulture));
        }

        if (landscape)
        {
            w.WriteAttribute("orientation", "landscape");
        }

        w.WriteEndElement();
    }

    // ---- comments ---------------------------------------------------------------------------------------------------------------------

    private static ReadOnlyMemory<byte> BuildComments(List<(int Row, int Col, string Author, string Text)> comments)
    {
        var authors = new List<string>();
        foreach ((_, _, string author, _) in comments)
        {
            if (!authors.Contains(author))
            {
                authors.Add(author);
            }
        }

        return Plain(w =>
        {
            w.WriteStartElement("comments");
            w.WriteNamespaceDeclaration(null, MainNs);
            w.WriteStartElement("authors");
            foreach (string a in authors)
            {
                w.WriteStartElement("author");
                w.WriteText(a);
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteStartElement("commentList");
            foreach ((int row, int col, string author, string text) in comments)
            {
                w.WriteStartElement("comment");
                w.WriteAttribute("ref", CellAddress.ColumnName(col) + row.ToString(CultureInfo.InvariantCulture));
                w.WriteAttribute("authorId", authors.IndexOf(author).ToString(CultureInfo.InvariantCulture));
                w.WriteStartElement("text");
                WriteInlineText(w, "t", text);
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
        });
    }

    // The legacy drawing Excel needs to show a note: one hidden shape per comment, anchored beside its cell.
    private static ReadOnlyMemory<byte> BuildVml(List<(int Row, int Col, string Author, string Text)> comments)
    {
        var sb = new StringBuilder();
        sb.Append("<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">")
            .Append("<o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"1\"/></o:shapelayout>")
            .Append("<v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\" path=\"m,l,21600r21600,l21600,xe\"><v:stroke joinstyle=\"miter\"/><v:path gradientshapeok=\"t\" o:connecttype=\"rect\"/></v:shapetype>");
        int id = 1025;
        foreach ((int row, int col, _, _) in comments)
        {
            sb.Append("<v:shape id=\"_x0000_s").Append(id++).Append("\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:59.25pt;margin-top:1.5pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden\" fillcolor=\"#ffffe1\" o:insetmode=\"auto\">")
                .Append("<v:fill color2=\"#ffffe1\"/><v:shadow on=\"t\" color=\"black\" obscured=\"t\"/><v:path o:connecttype=\"none\"/>")
                .Append("<v:textbox style=\"mso-direction-alt:auto\"><div style=\"text-align:left\"></div></v:textbox>")
                .Append("<x:ClientData ObjectType=\"Note\"><x:MoveWithCells/><x:SizeWithCells/><x:Anchor>")
                .Append(col).Append(", 15, ").Append(Math.Max(0, row - 2)).Append(", 2, ").Append(col + 2).Append(", 15, ").Append(row + 2).Append(", 16</x:Anchor>")
                .Append("<x:AutoFill>False</x:AutoFill><x:Row>").Append(row - 1).Append("</x:Row><x:Column>").Append(col - 1).Append("</x:Column></x:ClientData></v:shape>");
        }

        sb.Append("</xml>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ---- conditional formatting and data validation -------------------------------------------------------------------------------------

    private static readonly Dictionary<string, string> Operators = new(StringComparer.Ordinal)
    {
        ["Greater"] = "greaterThan",
        ["Less"] = "lessThan",
        ["Equal"] = "equal",
        ["NotEqual"] = "notEqual",
        ["Between"] = "between",
        ["NotBetween"] = "notBetween",
        ["GreaterOrEqual"] = "greaterThanOrEqual",
        ["LessOrEqual"] = "lessThanOrEqual",
    };

    private void WriteConditionalFormatting(XmlWriter w, MarkupNode cf, ref int priority)
    {
        string range = Child(cf, "Range").InnerText.Trim();
        string? sqref = string.IsNullOrEmpty(range) ? null : RangeListToA1(range);
        if (sqref is null)
        {
            Lost("loss.conditional-formats", "conditional formats", "Conditional formats whose range could not be read were dropped.");
            return;
        }

        (int originRow, int originCol) = FirstCell(sqref);
        var rules = new List<MarkupNode>();
        foreach (MarkupNode c in cf.ChildElements)
        {
            if (Name(c) == "Condition")
            {
                rules.Add(c);
            }
        }

        if (rules.Count == 0)
        {
            return;
        }

        w.WriteStartElement("conditionalFormatting");
        w.WriteAttribute("sqref", sqref);
        foreach (MarkupNode condition in rules)
        {
            string qualifier = Child(condition, "Qualifier").InnerText.Trim();
            string? v1 = NullIfEmpty(Child(condition, "Value1").InnerText);
            string? v2 = NullIfEmpty(Child(condition, "Value2").InnerText);
            string? f1 = v1 is null ? null : ToA1(v1, originRow, originCol, condition);
            string? f2 = v2 is null ? null : ToA1(v2, originRow, originCol, condition);
            w.WriteStartElement("cfRule");
            bool isCellIs = Operators.TryGetValue(qualifier, out string? op);
            w.WriteAttribute("type", isCellIs ? "cellIs" : "expression");
            MarkupNode format = Child(condition, "Format");
            if (!format.IsNull)
            {
                w.WriteAttribute("dxfId", DxfIndex(Attr(format, "Style")).ToString(CultureInfo.InvariantCulture));
            }

            w.WriteAttribute("priority", (priority++).ToString(CultureInfo.InvariantCulture));
            if (isCellIs)
            {
                w.WriteAttribute("operator", op!);
            }

            if (f1 is not null)
            {
                w.WriteStartElement("formula");
                w.WriteText(f1.TrimStart('='));
                w.WriteEndElement();
            }

            if (f2 is not null && isCellIs)
            {
                w.WriteStartElement("formula");
                w.WriteText(f2.TrimStart('='));
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static string? NullIfEmpty(string s)
    {
        string t = s.Trim();
        return t.Length == 0 ? null : t;
    }

    private static (int Row, int Col) FirstCell(string sqref)
    {
        string first = sqref.Split(' ')[0].Split(':')[0];
        int i = 0;
        while (i < first.Length && char.IsAsciiLetter(first[i]))
        {
            i++;
        }

        _ = CellAddress.TryParseColumn(first.AsSpan(0, i), out int col);
        _ = int.TryParse(first.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out int row);
        return (Math.Max(1, row), Math.Max(1, col));
    }

    private static readonly Dictionary<string, string> ValidationTypes = new(StringComparer.Ordinal)
    {
        ["Whole"] = "whole",
        ["Decimal"] = "decimal",
        ["List"] = "list",
        ["Date"] = "date",
        ["Time"] = "time",
        ["TextLength"] = "textLength",
        ["Custom"] = "custom",
    };

    private void WriteDataValidations(XmlWriter w, MarkupNode ws)
    {
        var items = new List<MarkupNode>();
        foreach (MarkupNode c in ws.ChildElements)
        {
            if (Name(c) == "DataValidation")
            {
                items.Add(c);
            }
        }

        if (items.Count == 0)
        {
            return;
        }

        var kept = new List<(MarkupNode Node, string SqRef, string Type)>();
        foreach (MarkupNode dv in items)
        {
            string type = Child(dv, "Type").InnerText.Trim();
            string? sqref = RangeListToA1(Child(dv, "Range").InnerText.Trim());
            if (sqref is null || !ValidationTypes.TryGetValue(type, out string? mapped))
            {
                if (type != "None")
                {
                    Lost("loss.data-validation", "data validation", "Validation rules that could not be read were dropped.");
                }

                continue;
            }

            kept.Add((dv, sqref, mapped));
        }

        if (kept.Count == 0)
        {
            return;
        }

        w.WriteStartElement("dataValidations");
        w.WriteAttribute("count", kept.Count.ToString(CultureInfo.InvariantCulture));
        foreach ((MarkupNode dv, string sqref, string type) in kept)
        {
            (int row, int col) = FirstCell(sqref);
            w.WriteStartElement("dataValidation");
            w.WriteAttribute("type", type);
            string qualifier = Child(dv, "Qualifier").InnerText.Trim();
            if (type != "list" && type != "custom" && Operators.TryGetValue(qualifier, out string? op) && op != "equal")
            {
                w.WriteAttribute("operator", op);
            }
            else if (type != "list" && type != "custom" && qualifier == "Equal")
            {
                w.WriteAttribute("operator", "equal");
            }

            if (!IsTrue(Child(dv, "IgnoreBlank").InnerText.Trim()) && !Child(dv, "IgnoreBlank").IsNull)
            {
                w.WriteAttribute("allowBlank", "0");
            }
            else
            {
                w.WriteAttribute("allowBlank", "1");
            }

            if (Child(dv, "InputHide").IsNull && NullIfEmpty(Child(dv, "InputMessage").InnerText) is not null)
            {
                w.WriteAttribute("showInputMessage", "1");
            }

            if (Child(dv, "ErrorHide").IsNull)
            {
                w.WriteAttribute("showErrorMessage", "1");
            }

            if (NullIfEmpty(Child(dv, "ErrorTitle").InnerText) is { } et)
            {
                w.WriteAttribute("errorTitle", et);
            }

            if (NullIfEmpty(Child(dv, "ErrorMessage").InnerText) is { } em)
            {
                w.WriteAttribute("error", em);
            }

            if (NullIfEmpty(Child(dv, "InputTitle").InnerText) is { } it)
            {
                w.WriteAttribute("promptTitle", it);
            }

            if (NullIfEmpty(Child(dv, "InputMessage").InnerText) is { } im)
            {
                w.WriteAttribute("prompt", im);
            }

            w.WriteAttribute("sqref", sqref);
            string? value = NullIfEmpty(Child(dv, "Value").InnerText);
            string? min = NullIfEmpty(Child(dv, "Min").InnerText);
            string? max = NullIfEmpty(Child(dv, "Max").InnerText);
            string? first = type == "list" || type == "custom" ? value : (min ?? value);
            string? second = max;
            if (first is not null)
            {
                string? f = first.StartsWith('"') ? first : ToA1(first, row, col, dv);
                if (f is not null)
                {
                    w.WriteStartElement("formula1");
                    w.WriteText(f.TrimStart('='));
                    w.WriteEndElement();
                }
            }

            if (second is not null && type != "list" && type != "custom")
            {
                string? f = ToA1(second, row, col, dv);
                if (f is not null)
                {
                    w.WriteStartElement("formula2");
                    w.WriteText(f.TrimStart('='));
                    w.WriteEndElement();
                }
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
    }
}
