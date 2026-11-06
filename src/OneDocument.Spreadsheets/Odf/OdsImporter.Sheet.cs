using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

// Worksheets: columns, rows and cells of a table:table (OpenDocument 1.3 Part 1 §9.1), and the parts of a worksheet that sit around the cell data.
internal sealed partial class OdsImporter
{
    private sealed record DefinedNameInfo(string Name, string Formula, int? Sheet, bool Hidden);

    private readonly List<DefinedNameInfo> _names = [];
    private readonly HashSet<string> _nameKeys = new(StringComparer.OrdinalIgnoreCase);
    private long _cells;

    private sealed class SheetState
    {
        public readonly List<string> Merges = [];
        public readonly List<(string Ref, string Target, string? Tip)> Links = [];
        public readonly List<(int Row, int Col, string Author, string Text)> Comments = [];
        public readonly Dictionary<string, CellSet> Validations = new(StringComparer.Ordinal);
        public readonly List<(int Row1, int Col1, int Row2, int Col2)> Arrays = [];
        public readonly List<(int Min, int Max, string Style, double Width, bool Hidden)> Columns = [];
        public int MaxCol;
        public int HeaderRowFirst;
        public int HeaderRowLast;
        public int HeaderColFirst;
        public int HeaderColLast;
        public string SheetName = string.Empty;
        public ArrayBufferWriter<byte>? Data;
    }

    /// <summary>A set of cells that compresses into a list of rectangles, for a validation's or a format's range list.</summary>
    internal sealed class CellSet
    {
        private readonly SortedDictionary<int, SortedSet<int>> _rows = [];

        public void Add(int row, int col, int count)
        {
            if (!_rows.TryGetValue(row, out SortedSet<int>? cols))
            {
                _rows[row] = cols = [];
            }

            for (int i = 0; i < count; i++)
            {
                cols.Add(col + i);
            }
        }

        public bool IsEmpty => _rows.Count == 0;

        public (int Row, int Col) First
        {
            get
            {
                foreach (KeyValuePair<int, SortedSet<int>> kv in _rows)
                {
                    return (kv.Key, kv.Value.Min);
                }

                return (1, 1);
            }
        }

        // Rectangles as an sqref: runs of columns per row, then identical runs on consecutive rows join.
        public string ToSqref()
        {
            var open = new Dictionary<(int C1, int C2), (int R1, int R2)>();
            var done = new List<(int R1, int C1, int R2, int C2)>();
            int previousRow = -2;
            foreach (KeyValuePair<int, SortedSet<int>> kv in _rows)
            {
                var runs = new List<(int C1, int C2)>();
                int start = -1;
                int last = -1;
                foreach (int c in kv.Value)
                {
                    if (start < 0)
                    {
                        start = last = c;
                    }
                    else if (c == last + 1)
                    {
                        last = c;
                    }
                    else
                    {
                        runs.Add((start, last));
                        start = last = c;
                    }
                }

                if (start >= 0)
                {
                    runs.Add((start, last));
                }

                var next = new Dictionary<(int C1, int C2), (int R1, int R2)>();
                foreach ((int c1, int c2) in runs)
                {
                    if (kv.Key == previousRow + 1 && open.TryGetValue((c1, c2), out (int R1, int R2) rect))
                    {
                        next[(c1, c2)] = (rect.R1, kv.Key);
                        open.Remove((c1, c2));
                    }
                    else
                    {
                        next[(c1, c2)] = (kv.Key, kv.Key);
                    }
                }

                foreach (KeyValuePair<(int C1, int C2), (int R1, int R2)> o in open)
                {
                    done.Add((o.Value.R1, o.Key.C1, o.Value.R2, o.Key.C2));
                }

                open = next;
                previousRow = kv.Key;
            }

            foreach (KeyValuePair<(int C1, int C2), (int R1, int R2)> o in open)
            {
                done.Add((o.Value.R1, o.Key.C1, o.Value.R2, o.Key.C2));
            }

            done.Sort();
            var parts = new List<string>();
            foreach ((int r1, int c1, int r2, int c2) in done)
            {
                string a = CellAddress.ColumnName(c1) + I(r1);
                parts.Add(r1 == r2 && c1 == c2 ? a : a + ":" + CellAddress.ColumnName(c2) + I(r2));
            }

            return string.Join(' ', parts);
        }
    }

    private void WriteWorksheet(
        OpcPackage package,
        MarkupNode table,
        string sheetName,
        int index,
        List<(string Name, string ContentType, ReadOnlyMemory<byte> Content)> parts,
        List<Relationship> workbookRels)
    {
        int number = index + 1;
        string partName = "/xl/worksheets/sheet" + I(number) + ".xml";
        string relId = "rId" + I(number);
        _sheetRelIds.Add(relId);
        workbookRels.Add(new Relationship(relId, RelBase + "worksheet", "worksheets/sheet" + I(number) + ".xml"));

        var state = new SheetState { SheetName = sheetName };
        MarkupNode tableStyleNode = default;
        OdfStyle? tableStyle = A(table, Table, "style-name") is { } tsn ? OdsStyleLookup.Find(_styles, "table", tsn) : null;
        if (tableStyle is not null)
        {
            tableStyleNode = tableStyle.Element;
        }

        _ = tableStyleNode;
        bool visible = Prop(tableStyle, "table-properties", Table, "display") != "false";
        _sheetStates.Add(visible ? string.Empty : "hidden");
        if (_activeTableName is not null && string.Equals(_activeTableName, A(table, Table, "name"), StringComparison.Ordinal))
        {
            _activeSheet = index;
        }

        ReadNames(Child(table, Table, "named-expressions"), index);
        if (A(table, Table, "print-ranges") is { } printRanges)
        {
            var areas = new List<string>();
            foreach (Range r in ReadRanges(printRanges))
            {
                areas.Add(AbsoluteText(r.Sheet is null ? r with { Sheet = A(table, Table, "name") } : r));
            }

            if (areas.Count > 0)
            {
                _printRanges[sheetName] = string.Join(',', areas);
            }
        }

        ReadColumns(table, state, 0);
        double defaultWidth = DefaultColumnWidth(state);

        var data = new ArrayBufferWriter<byte>();
        state.Data = data;
        var dw = new XmlWriter(data);
        dw.WriteStartElement("sheetData");
        int nextRow = 1;
        WriteRows(dw, table, state, ref nextRow, header: false);
        dw.WriteEndElement();
        dw.Complete();

        foreach (MarkupNode c in table.ChildElements)
        {
            if (c.NamespaceUri == Table && c.LocalNameString is "shapes" or "scenario")
            {
                Lost("loss." + c.LocalNameString, "drawings in tables", "Shapes, pictures and charts on a sheet were not carried over.");
            }
            else if (c.NamespaceUri == CalcExt && c.LocalNameString == "sparkline-groups")
            {
                Lost("loss.sparklines", "sparklines", "Sparklines were not carried over.");
            }
            else if (c.NamespaceUri == Table && c.LocalNameString == "tracked-changes")
            {
                Lost("loss.tracked-changes", "tracked changes", "Tracked changes were not carried over.");
            }
        }

        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        w.WriteXmlDeclaration();
        w.WriteStartElement("worksheet");
        w.WriteNamespaceDeclaration(null, MainNs);
        w.WriteNamespaceDeclaration("r", RNs);

        OdfStyle? master = A(table, Table, "style-name") is not null && tableStyle is not null && tableStyle.MasterPageName is { } mp ? OdsStyleLookup.Find(_styles, "master-page", mp) : null;
        PageLayoutInfo? layout = master is null ? null : ReadPageLayout(master);

        string? tabColor = Rgb(Prop(tableStyle, "table-properties", Table, "tab-color") ?? Prop(tableStyle, "table-properties", "http://openoffice.org/2009/table", "tab-color"));
        bool fit = layout?.FitToPage ?? false;
        if (tabColor is not null || fit)
        {
            w.WriteStartElement("sheetPr");
            if (tabColor is not null)
            {
                w.WriteStartElement("tabColor");
                w.WriteAttribute("rgb", tabColor);
                w.WriteEndElement();
            }

            if (fit)
            {
                w.WriteStartElement("pageSetUpPr");
                w.WriteAttribute("fitToPage", "1");
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        _views.TryGetValue(A(table, Table, "name") ?? string.Empty, out SheetView? view);
        bool grid = view?.ShowGrid ?? _showGrid ?? true;
        bool rtl = Prop(tableStyle, "table-properties", Style, "writing-mode") == "rl-tb";
        bool selected = index == _activeSheet;
        w.WriteStartElement("sheetViews");
        w.WriteStartElement("sheetView");
        if (!grid)
        {
            w.WriteAttribute("showGridLines", "0");
        }

        if (rtl)
        {
            w.WriteAttribute("rightToLeft", "1");
        }

        if (view?.Zoom is { } zoom and >= 10 and <= 400 && zoom != 100)
        {
            w.WriteAttribute("zoomScale", I(zoom));
        }

        if (selected)
        {
            w.WriteAttribute("tabSelected", "1");
        }

        w.WriteAttribute("workbookViewId", "0");
        if (view is { } v && (v.FrozenColumns > 0 || v.FrozenRows > 0))
        {
            w.WriteStartElement("pane");
            if (v.FrozenColumns > 0)
            {
                w.WriteAttribute("xSplit", I(v.FrozenColumns));
            }

            if (v.FrozenRows > 0)
            {
                w.WriteAttribute("ySplit", I(v.FrozenRows));
            }

            w.WriteAttribute("topLeftCell", Col(v.FrozenColumns + 1) + I(v.FrozenRows + 1));
            w.WriteAttribute("activePane", v.FrozenColumns > 0 && v.FrozenRows > 0 ? "bottomRight" : v.FrozenRows > 0 ? "bottomLeft" : "topRight");
            w.WriteAttribute("state", "frozen");
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndElement();

        if (defaultWidth > 0)
        {
            w.WriteStartElement("sheetFormatPr");
            w.WriteAttribute("defaultRowHeight", "15");
            w.WriteAttribute("defaultColWidth", Inv(defaultWidth));
            w.WriteEndElement();
        }

        WriteColumns(w, state, defaultWidth);
        w.WriteRaw(data.WrittenSpan);

        if (A(table, Table, "protected") == "true")
        {
            w.WriteStartElement("sheetProtection");
            w.WriteAttribute("sheet", "1");
            MarkupNode extra = default;
            foreach (MarkupNode c in table.ChildElements)
            {
                if (c.LocalNameString == "table-protection" && c.NamespaceUri == OdfNamespaces.LibreOfficeExtension)
                {
                    extra = c;
                }
            }

            if (!extra.IsNull)
            {
                if (A(extra, OdfNamespaces.LibreOfficeExtension, "select-protected-cells") == "false")
                {
                    w.WriteAttribute("selectLockedCells", "1");
                }

                if (A(extra, OdfNamespaces.LibreOfficeExtension, "select-unprotected-cells") == "false")
                {
                    w.WriteAttribute("selectUnlockedCells", "1");
                }
            }

            w.WriteEndElement();
            if (A(table, Table, "protection-key") is not null)
            {
                Lost("loss.protection-password", "protection password", "A protection password is stored as a hash that cannot be carried to the workbook; the sheet is protected without a password.");
            }
        }

        if (_filters.TryGetValue(sheetName, out string? filterRef))
        {
            w.WriteStartElement("autoFilter");
            w.WriteAttribute("ref", filterRef);
            w.WriteEndElement();
        }

        if (state.Merges.Count > 0)
        {
            w.WriteStartElement("mergeCells");
            w.WriteAttribute("count", I(state.Merges.Count));
            foreach (string m in state.Merges)
            {
                w.WriteStartElement("mergeCell");
                w.WriteAttribute("ref", m);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        int priority = 1;
        MarkupNode cfs = Child(table, CalcExt, "conditional-formats");
        if (!cfs.IsNull)
        {
            WriteConditionalFormats(w, cfs, sheetName, ref priority);
        }

        WriteDataValidations(w, state, sheetName);

        var rels = new List<Relationship>();
        var related = new List<(string Name, string ContentType, ReadOnlyMemory<byte> Content)>();
        if (state.Links.Count > 0)
        {
            w.WriteStartElement("hyperlinks");
            foreach ((string reference, string target, string? tip) in state.Links)
            {
                w.WriteStartElement("hyperlink");
                w.WriteAttribute("ref", reference);
                if (target.StartsWith('#'))
                {
                    w.WriteAttribute("location", InternalLocation(target[1..]));
                }
                else
                {
                    // Only a relationship is written: the target is never fetched.
                    string id = "rId" + I(rels.Count + 1);
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

        WritePageSetup(w, layout);

        if (state.Comments.Count > 0)
        {
            string commentsName = "/xl/comments" + I(number) + ".xml";
            string vmlName = "/xl/drawings/vmlDrawing" + I(number) + ".vml";
            related.Add((commentsName, CtBase + "comments+xml", BuildComments(state.Comments)));
            related.Add((vmlName, "application/vnd.openxmlformats-officedocument.vmlDrawing", BuildVml(state.Comments)));
            rels.Add(new Relationship("rId" + I(rels.Count + 1), RelBase + "comments", "../comments" + I(number) + ".xml"));
            string vmlId = "rId" + I(rels.Count + 1);
            rels.Add(new Relationship(vmlId, RelBase + "vmlDrawing", "../drawings/vmlDrawing" + I(number) + ".vml"));
            w.WriteStartElement("legacyDrawing");
            w.WriteAttribute("r:id", vmlId);
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.Complete();

        // Print areas and titles become the built-in names (ECMA-376 Part 1 §18.2.5 definedName).
        if (_printRanges.TryGetValue(sheetName, out string? area) && _nameKeys.Add(I(index) + "!_xlnm.Print_Area"))
        {
            _names.Add(new DefinedNameInfo("_xlnm.Print_Area", area, index, false));
        }

        if (_nameKeys.Add(I(index) + "!_xlnm.Print_Titles"))
        {
            var titles = new List<string>();
            string prefix = new SheetPrefix(sheetName).ToString();
            if (state.HeaderColFirst > 0)
            {
                titles.Add(prefix + "$" + Col(state.HeaderColFirst) + ":$" + Col(state.HeaderColLast));
            }

            if (state.HeaderRowFirst > 0)
            {
                titles.Add(prefix + "$" + I(state.HeaderRowFirst) + ":$" + I(state.HeaderRowLast));
            }

            if (titles.Count > 0)
            {
                _names.Add(new DefinedNameInfo("_xlnm.Print_Titles", string.Join(',', titles), index, false));
            }
        }

        parts.Add((partName, CtBase + "worksheet+xml", buffer.WrittenMemory));
        parts.AddRange(related);
        if (rels.Count > 0)
        {
            package.SetRelationships(partName, rels);
        }
    }

    // A link to a place in this document is written "#Sheet.A1" or "#Sheet.A1:B2" or "#$Sheet.A1".
    private string InternalLocation(string target)
    {
        int p = 0;
        if (TryReadRange(target, ref p, out Range r) && p == target.Length && r.Sheet is not null)
        {
            string text = RangeText(r);
            return new SheetPrefix(SheetOf(r.Sheet)).ToString() + text;
        }

        return target;
    }

    // ---- columns ------------------------------------------------------------------------------------------------------------------------

    private void ReadColumns(MarkupNode container, SheetState state, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        foreach (MarkupNode c in container.ChildElements)
        {
            if (c.NamespaceUri != Table)
            {
                continue;
            }

            switch (c.LocalNameString)
            {
                case "table-column":
                    AddColumn(c, state);
                    break;
                case "table-columns" or "table-column-group":
                    ReadColumns(c, state, depth + 1);
                    break;
                case "table-header-columns":
                    int before = _columnCursor;
                    ReadColumns(c, state, depth + 1);
                    if (_columnCursor > before)
                    {
                        state.HeaderColFirst = before + 1;
                        state.HeaderColLast = _columnCursor;
                    }

                    break;
                default:
                    break;
            }
        }

        if (depth == 0)
        {
            _columnCursor = 0;
        }
    }

    private int _columnCursor;

    private void AddColumn(MarkupNode c, SheetState state)
    {
        int repeat = Repeat(c, "number-columns-repeated");
        int min = _columnCursor + 1;
        int max = Math.Min(CellArea.MaxColumn, _columnCursor + repeat);
        _columnCursor += repeat;
        if (min > CellArea.MaxColumn)
        {
            return;
        }

        string? styleName = A(c, Table, "style-name");
        OdfStyle? s = styleName is null ? null : OdsStyleLookup.Find(_styles, "table-column", styleName);
        double width = OdfLengths.TryPoints(Prop(s, "table-column-properties", Style, "column-width"), out double pts) ? pts : -1;
        bool hidden = A(c, Table, "visibility") is "collapse" or "filter";
        state.Columns.Add((min, max, A(c, Table, "default-cell-style-name") ?? string.Empty, width, hidden));
    }

    // Column widths: points in a document, characters of the default font's digit in a worksheet. At 96 dpi with a 7 pixel digit and 5 pixels of
    // padding (Arial 10), 48 points is the 8.43 characters of a default column.
    private static double WidthChars(double points) => Math.Round(Math.Max(0, ((points * 96 / 72) - 5) / 7), 2);

    // The trailing column group that spans most of the sheet carries the default width.
    private static double DefaultColumnWidth(SheetState state)
    {
        if (state.Columns.Count == 0)
        {
            return 0;
        }

        (int min, int max, string style, double width, bool hidden) = state.Columns[^1];
        return max - min >= 255 && width > 0 && style.Length == 0 && !hidden ? WidthChars(width) : 0;
    }

    private void WriteColumns(XmlWriter w, SheetState state, double defaultWidth)
    {
        bool open = false;
        foreach ((int min, int max, string style, double width, bool hidden) in state.Columns)
        {
            int xf = StyleIndex(style);
            double chars = width > 0 ? WidthChars(width) : 0;
            bool isDefault = !hidden && xf == 0 && (chars == 0 || Math.Abs(chars - 8.43) < 0.1 || (defaultWidth > 0 && Math.Abs(chars - defaultWidth) < 0.005));
            if (isDefault)
            {
                continue;
            }

            if (!open)
            {
                w.WriteStartElement("cols");
                open = true;
            }

            w.WriteStartElement("col");
            w.WriteAttribute("min", I(min));
            w.WriteAttribute("max", I(max));
            w.WriteAttribute("width", Inv(chars > 0 ? chars : (defaultWidth > 0 ? defaultWidth : 8.43)));
            if (xf != 0)
            {
                w.WriteAttribute("style", I(xf));
            }

            if (hidden)
            {
                w.WriteAttribute("hidden", "1");
            }

            if (chars > 0)
            {
                w.WriteAttribute("customWidth", "1");
            }

            w.WriteEndElement();
        }

        if (open)
        {
            w.WriteEndElement();
        }
    }

    // ---- rows ---------------------------------------------------------------------------------------------------------------------------

    private int Repeat(MarkupNode n, string attribute)
    {
        string? v = A(n, Table, attribute);
        if (v is null)
        {
            return 1;
        }

        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int r) || r < 1)
        {
            Defect("ods.bad-repeat", DefectKind.Value, DefectSeverity.Repaired, "'" + v + "' is not a repeat count; 1 was used.", At(n));
            return 1;
        }

        return r;
    }

    private void WriteRows(XmlWriter w, MarkupNode container, SheetState state, ref int row, bool header)
    {
        foreach (MarkupNode c in container.ChildElements)
        {
            if (c.NamespaceUri != Table)
            {
                continue;
            }

            switch (c.LocalNameString)
            {
                case "table-row":
                    WriteRowGroup(w, c, state, ref row);
                    break;
                case "table-rows" or "table-row-group" or "table-header-rows":
                    int first = row;
                    WriteRows(w, c, state, ref row, header || c.LocalNameString == "table-header-rows");
                    if (c.LocalNameString == "table-header-rows" && row > first)
                    {
                        state.HeaderRowFirst = first;
                        state.HeaderRowLast = row - 1;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private void WriteRowGroup(XmlWriter w, MarkupNode rowNode, SheetState state, ref int row)
    {
        _ct.ThrowIfCancellationRequested();
        int repeat = Repeat(rowNode, "number-rows-repeated");
        if (row > CellArea.MaxRow)
        {
            if (RowHasContent(rowNode))
            {
                Defect("ods.row-limit", DefectKind.Limit, DefectSeverity.Dropped, "A row past the last row of a sheet was dropped.", At(rowNode));
            }

            return;
        }

        string? styleName = A(rowNode, Table, "style-name");
        OdfStyle? rs = styleName is null ? null : OdsStyleLookup.Find(_styles, "table-row", styleName);
        double height = OdfLengths.TryPoints(Prop(rs, "table-row-properties", Style, "row-height"), out double pts) ? pts : -1;
        bool optimal = Prop(rs, "table-row-properties", Style, "use-optimal-row-height") == "true";
        bool hidden = A(rowNode, Table, "visibility") is "collapse" or "filter";
        string? rowDefault = A(rowNode, Table, "default-cell-style-name");

        bool hasContent = RowHasContent(rowNode);
        int emit = hasContent ? Math.Min(repeat, CellArea.MaxRow - row + 1) : 0;
        if (!hasContent && (hidden || (height > 0 && !optimal)) && repeat <= 1024)
        {
            emit = Math.Min(repeat, CellArea.MaxRow - row + 1);
        }

        for (int k = 0; k < emit; k++)
        {
            int r = row + k;
            w.WriteStartElement("row");
            w.WriteAttribute("r", I(r));
            int rowXf = StyleIndex(rowDefault);
            if (rowXf != 0 && !hasContent)
            {
                w.WriteAttribute("s", I(rowXf));
                w.WriteAttribute("customFormat", "1");
            }

            if (height > 0)
            {
                w.WriteAttribute("ht", Inv(Math.Round(height, 2)));
                if (!optimal)
                {
                    w.WriteAttribute("customHeight", "1");
                }
            }

            if (hidden)
            {
                w.WriteAttribute("hidden", "1");
            }

            if (hasContent)
            {
                WriteCells(w, rowNode, r, rowDefault, state);
            }

            w.WriteEndElement();
            if (state.Data is { } buffer)
            {
                // The sheet part is built in memory, so its size is bounded like any other part.
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), _ctx.Limits.MaxPartSize, buffer.WrittenCount, At(rowNode));
            }
        }

        if (hasContent && emit < repeat)
        {
            Defect("ods.row-limit", DefectKind.Limit, DefectSeverity.Dropped, "Rows past the last row of a sheet were dropped.", At(rowNode));
        }

        row += repeat;
    }

    // A row has content when a cell carries a value, a formula, a note, a validation or a non-default style that is not just a filler.
    private bool RowHasContent(MarkupNode rowNode)
    {
        foreach (MarkupNode c in rowNode.ChildElements)
        {
            if (c.NamespaceUri != Table)
            {
                continue;
            }

            if (c.LocalNameString is "table-cell" or "covered-table-cell" && (CellHasContent(c) || A(c, Table, "content-validation-name") is not null || (Repeat(c, "number-columns-repeated") < 256 && IsStyled(c))))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsStyled(MarkupNode cell) => A(cell, Table, "style-name") is { } sn && StyleIndex(sn, cell) != 0;

    // A cell that spans more than itself is a merged cell even when it holds nothing.
    private static bool IsSpanned(MarkupNode cell) =>
        (int.TryParse(A(cell, Table, "number-columns-spanned"), NumberStyles.None, CultureInfo.InvariantCulture, out int c) && c > 1)
        || (int.TryParse(A(cell, Table, "number-rows-spanned"), NumberStyles.None, CultureInfo.InvariantCulture, out int r) && r > 1);

    private static bool CellHasContent(MarkupNode cell)
    {
        if (A(cell, Office, "value-type") is not null || A(cell, Table, "formula") is not null || IsSpanned(cell))
        {
            return true;
        }

        foreach (MarkupNode c in cell.ChildElements)
        {
            if (c.IsElement(Text, "p") || c.IsElement(Office, "annotation"))
            {
                return true;
            }
        }

        return false;
    }

    private void WriteCells(XmlWriter w, MarkupNode rowNode, int row, string? rowDefault, SheetState state)
    {
        int col = 1;
        foreach (MarkupNode cell in rowNode.ChildElements)
        {
            if (cell.NamespaceUri != Table || cell.LocalNameString is not ("table-cell" or "covered-table-cell"))
            {
                continue;
            }

            if (col > CellArea.MaxColumn)
            {
                Defect("ods.column-limit", DefectKind.Limit, DefectSeverity.Dropped, "A cell past the last column of a sheet was dropped.", At(cell));
                break;
            }

            int repeat = Repeat(cell, "number-columns-repeated");
            int run = Math.Min(repeat, CellArea.MaxColumn - col + 1);
            bool covered = cell.LocalNameString == "covered-table-cell";
            string? styleName = A(cell, Table, "style-name") ?? ColumnStyleAt(state, col) ?? rowDefault;
            int xf = StyleIndex(styleName, cell);

            if (A(cell, Table, "content-validation-name") is { } validation)
            {
                if (!state.Validations.TryGetValue(validation, out CellSet? set))
                {
                    state.Validations[validation] = set = new CellSet();
                }

                set.Add(row, col, run);
            }

            bool content = CellHasContent(cell);
            bool inArray = covered && InArray(state, row, col);
            if (covered && !inArray)
            {
                col += repeat;
                continue;
            }

            if (!content && !inArray)
            {
                if (xf != 0 && repeat < 256)
                {
                    for (int i = 0; i < run; i++)
                    {
                        CountCell(cell);
                        string a = Col(col + i) + I(row);
                        w.WriteStartElement("c");
                        w.WriteAttribute("r", a);
                        w.WriteAttribute("s", I(xf));
                        w.WriteEndElement();
                        state.MaxCol = Math.Max(state.MaxCol, col + i);
                    }
                }

                col += repeat;
                continue;
            }

            int spanC = 1;
            int spanR = 1;
            if (!covered)
            {
                int.TryParse(A(cell, Table, "number-columns-spanned"), NumberStyles.None, CultureInfo.InvariantCulture, out spanC);
                int.TryParse(A(cell, Table, "number-rows-spanned"), NumberStyles.None, CultureInfo.InvariantCulture, out spanR);
                spanC = Math.Max(1, spanC);
                spanR = Math.Max(1, spanR);
            }

            for (int i = 0; i < run; i++)
            {
                CountCell(cell);
                int c = col + i;
                WriteCell(w, cell, row, c, xf, state, covered);
                if (spanC > 1 || spanR > 1)
                {
                    state.Merges.Add(Col(c) + I(row) + ":" + Col(Math.Min(CellArea.MaxColumn, c + spanC - 1)) + I(Math.Min(CellArea.MaxRow, row + spanR - 1)));
                }
            }

            col += repeat;
        }
    }

    private void CountCell(MarkupNode at)
    {
        _cells++;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), _ctx.Limits.MaxRecordCount, _cells, At(at));
    }

    private static string? ColumnStyleAt(SheetState state, int col)
    {
        foreach ((int min, int max, string style, _, _) in state.Columns)
        {
            if (col >= min && col <= max)
            {
                return style.Length == 0 ? null : style;
            }
        }

        return null;
    }

    private static bool InArray(SheetState state, int row, int col)
    {
        foreach ((int r1, int c1, int r2, int c2) in state.Arrays)
        {
            if (row >= r1 && row <= r2 && col >= c1 && col <= c2)
            {
                return true;
            }
        }

        return false;
    }

    // ---- cells ------------------------------------------------------------------------------------------------------------------------------

    private void WriteCell(XmlWriter w, MarkupNode cell, int row, int col, int xf, SheetState state, bool covered)
    {
        string address = Col(col) + I(row);
        DefectLocation where = DefectLocation.InCell(state.SheetName, address);
        state.MaxCol = Math.Max(state.MaxCol, col);

        string? valueType = A(cell, Office, "value-type");
        string? calcType = A(cell, CalcExt, "value-type");
        ReadCellText(cell, out string text, out string? href, out bool rich, out MarkupNode annotation);
        if (rich)
        {
            Lost("loss.rich-text", "rich text in cells", "Formatting of parts of a cell's text was not carried over.");
        }

        string? formulaAttr = covered ? null : A(cell, Table, "formula");
        string? formula = null;
        if (formulaAttr is not null)
        {
            formula = FormulaText(formulaAttr, cell, state.SheetName, address);
        }

        string? arrayRef = null;
        if (formula is not null && (A(cell, Table, "number-matrix-columns-spanned") is not null || A(cell, Table, "number-matrix-rows-spanned") is not null))
        {
            int.TryParse(A(cell, Table, "number-matrix-columns-spanned"), NumberStyles.None, CultureInfo.InvariantCulture, out int mc);
            int.TryParse(A(cell, Table, "number-matrix-rows-spanned"), NumberStyles.None, CultureInfo.InvariantCulture, out int mr);
            mc = Math.Max(1, mc);
            mr = Math.Max(1, mr);
            int r2 = Math.Min(CellArea.MaxRow, row + mr - 1);
            int c2 = Math.Min(CellArea.MaxColumn, col + mc - 1);
            arrayRef = Col(col) + I(row) + (mc > 1 || mr > 1 ? ":" + Col(c2) + I(r2) : string.Empty);
            state.Arrays.Add((row, col, r2, c2));
        }

        string? cellType = null;
        string? value = null;
        bool inlineText = false;
        switch (calcType == "error" ? "error" : valueType)
        {
            case "error":
                string errText = text.Trim();
                if (!CalcErrors.TryParse(errText, out _))
                {
                    errText = "#VALUE!";
                }

                cellType = "e";
                value = errText;
                break;
            case "float" or "percentage" or "currency":
                string? num = A(cell, Office, "value");
                if (num is not null && double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
                {
                    value = Inv(d);
                }
                else
                {
                    Defect("ods.bad-number", DefectKind.Value, DefectSeverity.Repaired, "'" + num + "' is not a number; the displayed text was kept.", where);
                    value = text;
                    cellType = formula is null ? "inlineStr" : "str";
                    inlineText = formula is null;
                }

                break;
            case "date":
                string? date = A(cell, Office, "date-value");
                if (date is not null && TryDateSerial(date, out double serial))
                {
                    value = Inv(serial);
                }
                else
                {
                    Defect("ods.bad-date", DefectKind.Value, DefectSeverity.Repaired, "'" + date + "' is not a date; the displayed text was kept.", where);
                    value = text;
                    cellType = formula is null ? "inlineStr" : "str";
                    inlineText = formula is null;
                }

                break;
            case "time":
                string? time = A(cell, Office, "time-value");
                if (time is not null && TryDuration(time, out double fraction))
                {
                    value = Inv(fraction);
                }
                else
                {
                    Defect("ods.bad-time", DefectKind.Value, DefectSeverity.Repaired, "'" + time + "' is not a time; the displayed text was kept.", where);
                    value = text;
                    cellType = formula is null ? "inlineStr" : "str";
                    inlineText = formula is null;
                }

                break;
            case "boolean":
                string? flag = A(cell, Office, "boolean-value");
                value = flag is "true" or "1" ? "1" : "0";
                cellType = "b";
                break;
            case "string":
                value = A(cell, Office, "string-value") ?? text;
                cellType = formula is null ? "inlineStr" : "str";
                inlineText = formula is null;
                break;
            default:
                if (valueType is null && formula is null && text.Length > 0)
                {
                    value = text;
                    cellType = "inlineStr";
                    inlineText = true;
                }
                else if (valueType is not null)
                {
                    Defect("ods.bad-value-type", DefectKind.Value, DefectSeverity.Repaired, "The value type '" + valueType + "' is unknown; the displayed text was kept.", where);
                    value = text;
                    cellType = formula is null ? "inlineStr" : "str";
                    inlineText = formula is null;
                }

                break;
        }

        w.WriteStartElement("c");
        w.WriteAttribute("r", address);
        if (xf != 0)
        {
            w.WriteAttribute("s", I(xf));
        }

        if (cellType is not null)
        {
            w.WriteAttribute("t", cellType);
        }

        if (formula is not null)
        {
            w.WriteStartElement("f");
            if (arrayRef is not null)
            {
                w.WriteAttribute("t", "array");
                w.WriteAttribute("ref", arrayRef);
            }

            w.WriteText(formula);
            w.WriteEndElement();
        }

        if (inlineText)
        {
            w.WriteStartElement("is");
            WriteInlineText(w, value ?? string.Empty);
            w.WriteEndElement();
        }
        else if (value is not null)
        {
            w.WriteStartElement("v");
            w.WriteText(value);
            w.WriteEndElement();
        }

        w.WriteEndElement();

        if (href is { Length: > 0 })
        {
            state.Links.Add((address, href, null));
        }

        if (!annotation.IsNull)
        {
            string author = Child(annotation, OdfNamespaces.DublinCore, "creator").InnerText.Trim();
            var sb = new StringBuilder();
            foreach (MarkupNode p in annotation.ChildElements)
            {
                if (p.IsElement(Text, "p"))
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('\n');
                    }

                    AppendParagraph(p, sb, out _, out _);
                }
            }

            state.Comments.Add((row, col, author, sb.ToString()));
        }
    }

    private static void WriteInlineText(XmlWriter w, string text)
    {
        w.WriteStartElement("t");
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\n', StringComparison.Ordinal) || text.Contains("  ", StringComparison.Ordinal)))
        {
            w.WriteAttribute("xml:space", "preserve");
        }

        w.WriteText(text);
        w.WriteEndElement();
    }

    // OpenDocument 1.3 Part 1 §6.1.? text:p: spaces beyond the first are text:s, tabs and line breaks are elements.
    private static void ReadCellText(MarkupNode cell, out string text, out string? href, out bool rich, out MarkupNode annotation)
    {
        var sb = new StringBuilder();
        string? link = null;
        bool hasRich = false;
        annotation = default;
        foreach (MarkupNode c in cell.ChildElements)
        {
            if (c.IsElement(Text, "p"))
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                AppendParagraph(c, sb, out string? l, out bool r);
                link ??= l;
                hasRich |= r;
            }
            else if (c.IsElement(Office, "annotation") && annotation.IsNull)
            {
                annotation = c;
            }
        }

        text = sb.ToString();
        href = link;
        rich = hasRich;
    }

    private static void AppendParagraph(MarkupNode p, StringBuilder sb, out string? href, out bool rich)
    {
        href = null;
        rich = false;
        AppendInline(p, sb, ref href, ref rich, 0);
    }

    private static void AppendInline(MarkupNode parent, StringBuilder sb, ref string? href, ref bool rich, int depth)
    {
        if (depth > 16)
        {
            return;
        }

        foreach (MarkupNode n in parent.Children)
        {
            if (n.Kind == MarkupNodeKind.Text)
            {
                sb.Append(n.InnerText);
            }
            else if (n.Kind == MarkupNodeKind.Element && n.NamespaceUri == Text)
            {
                switch (n.LocalNameString)
                {
                    case "s":
                        int.TryParse(A(n, Text, "c"), NumberStyles.None, CultureInfo.InvariantCulture, out int count);
                        sb.Append(' ', Math.Clamp(count == 0 ? 1 : count, 1, 4096));
                        break;
                    case "tab":
                        sb.Append('\t');
                        break;
                    case "line-break":
                        sb.Append('\n');
                        break;
                    case "a":
                        href ??= A(n, OdfNamespaces.XLink, "href");
                        AppendInline(n, sb, ref href, ref rich, depth + 1);
                        break;
                    case "span":
                        if (A(n, Text, "style-name") is not null)
                        {
                            rich = true;
                        }

                        AppendInline(n, sb, ref href, ref rich, depth + 1);
                        break;
                    case "note" or "note-citation" or "soft-page-break":
                        break;
                    default:
                        AppendInline(n, sb, ref href, ref rich, depth + 1);
                        break;
                }
            }
        }
    }

    // OpenDocument 1.3 Part 1 §? office:date-value: an ISO 8601 date or date-time without a zone.
    private bool TryDateSerial(string iso, out double serial)
    {
        serial = 0;
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value))
        {
            return false;
        }

        return ExcelDate.TryToSerial(value, _dateSystem, out serial);
    }

    // office:time-value is an xsd:duration: PT08H30M00S, possibly past 24 hours or with fractions of a second.
    private static bool TryDuration(string text, out double days)
    {
        days = 0;
        string s = text.Trim();
        bool negative = s.StartsWith('-');
        if (negative)
        {
            s = s[1..];
        }

        if (!s.StartsWith('P'))
        {
            return false;
        }

        double seconds = 0;
        bool time = false;
        int i = 1;
        while (i < s.Length)
        {
            if (s[i] == 'T')
            {
                time = true;
                i++;
                continue;
            }

            int start = i;
            while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
            {
                i++;
            }

            if (i == start || i >= s.Length || !double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            {
                return false;
            }

            char unit = s[i++];
            seconds += unit switch
            {
                'D' => n * 86400,
                'H' when time => n * 3600,
                'M' when time => n * 60,
                'S' when time => n,
                _ => double.NaN,
            };
            if (double.IsNaN(seconds))
            {
                return false;
            }
        }

        days = (negative ? -seconds : seconds) / 86400;
        return true;
    }

    // ---- comments ---------------------------------------------------------------------------------------------------------------------------

    private static ReadOnlyMemory<byte> BuildComments(List<(int Row, int Col, string Author, string Text)> comments)
    {
        var authors = comments.Select(static c => c.Author).Distinct(StringComparer.Ordinal).ToList();
        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        w.WriteXmlDeclaration();
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
            w.WriteAttribute("ref", Col(col) + I(row));
            w.WriteAttribute("authorId", I(authors.IndexOf(author)));
            w.WriteStartElement("text");
            WriteInlineText(w, text);
            w.WriteEndElement();
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndElement();
        w.Complete();
        return buffer.WrittenMemory;
    }

    // The legacy drawing a spreadsheet application needs to show a note: one hidden shape per comment, anchored beside its cell.
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
}
