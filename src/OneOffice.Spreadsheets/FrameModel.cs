using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>
/// What a worksheet part holds around its cells, as a small markup tree built from the bytes before and after <c>sheetData</c>
/// (with a marker where <c>sheetData</c> was). The tree keeps every element in order with its unknown content; the model reads
/// columns, merges, the dimension and frozen panes from it and edits go back into it.
/// </summary>
internal sealed class FrameModel
{
    // ECMA-376 Part 1 §18.3.1.99: the child elements of worksheet, in schema order.
    private static readonly string[] Order =
    [
        "sheetPr", "dimension", "sheetViews", "sheetFormatPr", "cols", "sheetData", "sheetCalcPr", "sheetProtection", "protectedRanges",
        "scenarios", "autoFilter", "sortState", "dataConsolidate", "customSheetViews", "mergeCells", "phoneticPr", "conditionalFormatting",
        "dataValidations", "hyperlinks", "printOptions", "pageMargins", "pageSetup", "headerFooter", "rowBreaks", "colBreaks",
        "customProperties", "cellWatches", "ignoredErrors", "smartTags", "drawing", "legacyDrawing", "legacyDrawingHF", "drawingHF",
        "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst",
    ];

    private const string SplitAttribute = " x-split=\"1\"/>";

    private readonly SheetFrame _frame;
    private readonly string _prefixColon;
    private readonly MarkupDocument _doc;

    private FrameModel(SheetFrame frame, MarkupDocument doc)
    {
        _frame = frame;
        _doc = doc;
        _prefixColon = frame.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(frame.Prefix) + ":";
    }

    public CellRange Dimension { get; private set; }

    public List<ColumnProperties> Columns { get; set; } = [];

    public List<CellRange> Merges { get; private set; } = [];

    public int FrozenRows { get; private set; }

    public int FrozenColumns { get; private set; }

    public bool Changed { get; set; }

    public bool ColumnsChanged { get; set; }

    public bool MergesChanged { get; set; }

    public static FrameModel Read(SheetFrame frame, OpenContext context, string partName)
    {
        string pc = frame.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(frame.Prefix) + ":";
        byte[] bytes;
        if (frame.HadSheetData)
        {
            string marker = "<" + pc + "sheetData" + SplitAttribute;
            bytes = [.. frame.Head, .. Encoding.UTF8.GetBytes(marker), .. frame.Tail];
        }
        else
        {
            bytes = frame.Head;
        }

        MarkupDocument doc = MarkupDocument.Parse(bytes, context, partName);
        var model = new FrameModel(frame, doc);
        if (!frame.HadSheetData)
        {
            // A worksheet must have sheetData; add the marker where it belongs so the write path can place the rows.
            model.InsertInOrder("sheetData", "<" + (frame.Prefix.Length == 0 && doc.Root.Prefix.Length > 0 ? Encoding.UTF8.GetString(doc.Root.Prefix) + ":" : model._prefixColon) + "sheetData" + SplitAttribute);
        }

        model.ReadElements(context, partName);
        return model;
    }

    private void ReadElements(OpenContext context, string partName)
    {
        foreach (MarkupNode child in _doc.Root.ChildElements)
        {
            switch (child.LocalNameString)
            {
                case "dimension":
                    if (child.GetAttribute("ref") is { } r && CellRange.TryParse(r, out CellRange dim))
                    {
                        Dimension = dim;
                    }

                    break;
                case "cols":
                    foreach (MarkupNode col in child.ChildElements)
                    {
                        if (col.LocalNameString == "col" && ReadColumn(col) is { } cp)
                        {
                            Columns.Add(cp);
                        }
                        else if (col.LocalNameString == "col")
                        {
                            context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.column-range-invalid", DefectSeverity.Dropped,
                                "A col element has no valid min/max; it was dropped.", DefectLocation.InPart(partName, col.SourceStart)));
                        }
                    }

                    break;
                case "mergeCells":
                    foreach (MarkupNode m in child.ChildElements)
                    {
                        if (m.LocalNameString == "mergeCell")
                        {
                            if (m.GetAttribute("ref") is { } mr && CellRange.TryParse(mr, out CellRange range))
                            {
                                Merges.Add(range);
                            }
                            else
                            {
                                context.Defects.Report(new Defect(DefectKind.Reference, "xlsx.merge-range-invalid", DefectSeverity.Dropped,
                                    "A mergeCell has an invalid ref; it was dropped.", DefectLocation.InPart(partName, m.SourceStart)));
                            }
                        }
                    }

                    break;
                case "sheetViews":
                    ReadPane(child);
                    break;
            }
        }
    }

    private void ReadPane(MarkupNode sheetViews)
    {
        foreach (MarkupNode view in sheetViews.ChildElements)
        {
            foreach (MarkupNode pane in view.ChildElements)
            {
                if (pane.LocalNameString == "pane" && pane.GetAttribute("state") is "frozen" or "frozenSplit")
                {
                    FrozenColumns = ParseSplit(pane.GetAttribute("xSplit"));
                    FrozenRows = ParseSplit(pane.GetAttribute("ySplit"));
                    return;
                }
            }

            return; // only the first view is read
        }
    }

    private static int ParseSplit(string? s) =>
        s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? (int)Math.Min(v, CellAddress.MaxRows) : 0;

    private static ColumnProperties? ReadColumn(MarkupNode col)
    {
        int min = 0;
        int max = 0;
        double width = -1;
        int style = 0;
        int outline = 0;
        var flags = ColumnOptions.None;
        StringBuilder? raw = null;
        foreach (MarkupAttributeRef a in col.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                continue;
            }

            string v = a.Value;
            if (a.Prefix.Length == 0)
            {
                switch (Encoding.UTF8.GetString(a.RawName))
                {
                    case "min":
                        _ = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out min);
                        continue;
                    case "max":
                        _ = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out max);
                        continue;
                    case "width":
                        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out width))
                        {
                            width = -1;
                        }

                        continue;
                    case "style":
                        _ = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out style);
                        continue;
                    case "outlineLevel":
                        _ = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out outline);
                        continue;
                    case "hidden":
                        flags |= v is "1" or "true" ? ColumnOptions.Hidden : 0;
                        continue;
                    case "customWidth":
                        flags |= v is "1" or "true" ? ColumnOptions.CustomWidth : 0;
                        continue;
                    case "bestFit":
                        flags |= v is "1" or "true" ? ColumnOptions.BestFit : 0;
                        continue;
                    case "collapsed":
                        flags |= v is "1" or "true" ? ColumnOptions.Collapsed : 0;
                        continue;
                    case "phonetic":
                        flags |= v is "1" or "true" ? ColumnOptions.Phonetic : 0;
                        continue;
                }
            }

            raw ??= new StringBuilder();
            if (raw.Length > 0)
            {
                raw.Append(' ');
            }

            char q = (char)a.Quote;
            raw.Append(Encoding.UTF8.GetString(a.RawName)).Append('=').Append(q).Append(Encoding.UTF8.GetString(a.RawValue)).Append(q);
        }

        if (min < 1 || max < min || max > CellAddress.MaxColumns)
        {
            return null;
        }

        return new ColumnProperties(min, max, width, style, flags, Math.Clamp(outline, 0, 7), raw?.ToString());
    }

    private void InsertInOrder(string local, string xml)
    {
        int idx = Array.IndexOf(Order, local);
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        foreach (MarkupNode child in _doc.Root.ChildElements)
        {
            int ci = Array.IndexOf(Order, child.LocalNameString);
            if (ci > idx && ci >= 0 && !(idx < 0))
            {
                child.InsertXmlBefore(bytes);
                return;
            }
        }

        _doc.Root.AppendXml(bytes);
    }

    private MarkupNode? Find(string local)
    {
        foreach (MarkupNode child in _doc.Root.ChildElements)
        {
            if (child.LocalNameString == local)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Applies edits to the tree and returns the bytes before and after the sheetData marker.</summary>
    public void Apply(CellStore store, bool cellsEdited, out byte[] head, out byte[] tail)
    {
        if (cellsEdited && Find("dimension") is { } dim)
        {
            CellRange used = store.UsedRange();
            string wanted = used.IsValid ? used.ToString() : "A1";
            if (dim.GetAttribute("ref") != wanted)
            {
                dim.SetAttribute("ref", wanted);
            }
        }

        string pc = _prefixColon.Length > 0 || _doc.Root.Prefix.Length == 0 ? _prefixColon : Encoding.UTF8.GetString(_doc.Root.Prefix) + ":";
        if (ColumnsChanged)
        {
            Find("cols")?.Remove();
            if (Columns.Count > 0)
            {
                var sb = new StringBuilder("<").Append(pc).Append("cols>");
                foreach (ColumnProperties c in Columns)
                {
                    sb.Append('<').Append(pc).Append("col min=\"").Append(c.Min.ToString(CultureInfo.InvariantCulture)).Append("\" max=\"").Append(c.Max.ToString(CultureInfo.InvariantCulture)).Append('"');
                    if (c.Width >= 0)
                    {
                        sb.Append(" width=\"").Append(c.Width.ToString("R", CultureInfo.InvariantCulture)).Append('"');
                    }

                    if (c.Style != 0)
                    {
                        sb.Append(" style=\"").Append(c.Style.ToString(CultureInfo.InvariantCulture)).Append('"');
                    }

                    AppendFlag(sb, c.Flags, ColumnOptions.Hidden, "hidden");
                    AppendFlag(sb, c.Flags, ColumnOptions.BestFit, "bestFit");
                    AppendFlag(sb, c.Flags, ColumnOptions.CustomWidth, "customWidth");
                    AppendFlag(sb, c.Flags, ColumnOptions.Phonetic, "phonetic");
                    if (c.OutlineLevel != 0)
                    {
                        sb.Append(" outlineLevel=\"").Append(c.OutlineLevel.ToString(CultureInfo.InvariantCulture)).Append('"');
                    }

                    AppendFlag(sb, c.Flags, ColumnOptions.Collapsed, "collapsed");
                    if (c.RawAttributes is not null)
                    {
                        sb.Append(' ').Append(c.RawAttributes);
                    }

                    sb.Append("/>");
                }

                sb.Append("</").Append(pc).Append("cols>");
                InsertInOrder("cols", sb.ToString());
            }

            ColumnsChanged = false;
        }

        if (MergesChanged)
        {
            Find("mergeCells")?.Remove();
            if (Merges.Count > 0)
            {
                var sb = new StringBuilder("<").Append(pc).Append("mergeCells count=\"").Append(Merges.Count.ToString(CultureInfo.InvariantCulture)).Append("\">");
                foreach (CellRange r in Merges)
                {
                    sb.Append('<').Append(pc).Append("mergeCell ref=\"").Append(r.ToString()).Append("\"/>");
                }

                sb.Append("</").Append(pc).Append("mergeCells>");
                InsertInOrder("mergeCells", sb.ToString());
            }

            MergesChanged = false;
        }

        if (!_doc.IsModified)
        {
            head = _frame.Head;
            tail = _frame.Tail;
            return;
        }

        byte[] all = _doc.ToArray();
        byte[] marker = Encoding.UTF8.GetBytes("<" + (_doc.Root.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(_doc.Root.Prefix) + ":") + "sheetData" + SplitAttribute);
        int at = all.AsSpan().IndexOf(marker);
        if (at < 0)
        {
            // The marker is in the tree and was never edited; losing it would mean the tree was rewritten oddly.
            throw new OfficeFormatException(DefectKind.Structure, "xlsx.frame-marker-lost", "The sheet could not be written because its sheetData position was lost.");
        }

        head = all[..at];
        tail = all[(at + marker.Length)..];
        if (!_frame.HadSheetData)
        {
            _frame.Prefix = _doc.Root.Prefix.ToArray();
        }

        _frame.Head = head;
        _frame.Tail = tail;
        _frame.HadSheetData = true;
    }

    private static void AppendFlag(StringBuilder sb, ColumnOptions flags, ColumnOptions flag, string name)
    {
        if ((flags & flag) != 0)
        {
            sb.Append(' ').Append(name).Append("=\"1\"");
        }
    }
}
