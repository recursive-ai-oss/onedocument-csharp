using System.Globalization;
using System.Linq;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Translates a table part between <c>tableN.bin</c> and <c>tableN.xml</c> ([MS-XLSB] §2.1.7.51, §2.4.100, §2.4.101, §2.4.706, §2.4.708, §2.4.847).</summary>
internal static class XlsbTablePart
{
    private static readonly string[] TotalFunctions = ["none", "average", "count", "countNums", "max", "min", "sum", "stdDev", "var", "custom"];

    /// <summary>The identifier, name and column names of a binary table part; null when the part has no table record.</summary>
    public static (int Id, XlsbTableInfo Info)? ReadInfo(ReadOnlyMemory<byte> bin, OpenContext context, string partName)
    {
        var r = new XlsbRecordReader(bin, context, partName);
        int id = 0;
        string? name = null;
        var columns = new List<string>();
        bool seen = false;
        while (r.Read())
        {
            if (r.Type == XlsbRecordType.BeginList)
            {
                var f = new XlsbRecordFields(r.Data);
                f.Skip(16);
                f.I32();
                id = (int)f.U32();
                f.Skip(4 * 10);
                string? stName = f.NullableWideString();
                string? display = f.NullableWideString();
                name = display ?? stName;
                seen = true;
            }
            else if (r.Type == XlsbRecordType.BeginListCol && seen)
            {
                var f = new XlsbRecordFields(r.Data);
                f.Skip(24);
                string? stName = f.NullableWideString();
                string? caption = f.NullableWideString();
                columns.Add(caption ?? stName ?? string.Empty);
            }
        }

        return seen && name is not null ? (id, new XlsbTableInfo(name, columns)) : null;
    }

    /// <summary>The identifier, name and column names of a SpreadsheetML table part.</summary>
    public static (int Id, XlsbTableInfo Info)? InfoFromXml(MarkupNode table)
    {
        string? name = table.GetAttribute("displayName") ?? table.GetAttribute("name");
        if (name is null)
        {
            return null;
        }

        return (table.Int("id"), new XlsbTableInfo(name, table.Element("tableColumns").Elements("tableColumn").Select(c => c.Str("name")).ToList()));
    }

    private static void Dxf(XmlSink x, string name, uint id)
    {
        if (id != 0xFFFFFFFF)
        {
            x.Attr(name, (long)id);
        }
    }

    public static byte[] ToXml(ReadOnlyMemory<byte> bin, OpenContext context, string partName, XlsbFormulaContext formulas)
    {
        var r = new XlsbRecordReader(bin, context, partName);
        var x = new XmlSink(1 << 12);
        var children = new XmlSink(1 << 12);
        var columns = new XmlSink(1 << 12);
        var group = new List<XlsbGroupRecord>();
        int groupEnd = 0;
        var features = new XlsbFeatureReader(context, partName, formulas);
        int columnCount = 0;
        bool inColumn = false;
        int firstDataRow = 0;
        int firstColumn = 0;
        string styleName = string.Empty;
        int styleFlags = 0;
        bool haveStyle = false;
        var columnBody = new XmlSink();
        string columnHead = string.Empty;
        while (r.Read())
        {
            if (groupEnd != 0)
            {
                group.Add(new XlsbGroupRecord(r.Type, r.Data.ToArray(), r.Offset));
                if (r.Type == groupEnd)
                {
                    var tmp = new XmlSink();
                    if (XlsbFilters.ToXml(group, tmp))
                    {
                        children.Raw(tmp.Memory.Span);
                    }
                    else
                    {
                        foreach (XlsbGroupRecord g in group)
                        {
                            XlsbMarkup.Raw(children, g.Type, g.Data);
                        }
                    }

                    groupEnd = 0;
                    group.Clear();
                }

                continue;
            }

            switch (r.Type)
            {
                case XlsbRecordType.BeginList:
                {
                    var f = new XlsbRecordFields(r.Data);
                    int r1 = f.I32();
                    int r2 = f.I32();
                    int c1 = f.I32();
                    int c2 = f.I32();
                    int lt = f.I32();
                    uint id = f.U32();
                    int header = f.I32();
                    int totals = f.I32();
                    uint bits = f.U32();
                    uint dxfHeader = f.U32();
                    uint dxfData = f.U32();
                    uint dxfAgg = f.U32();
                    uint dxfBorder = f.U32();
                    uint dxfHeaderBorder = f.U32();
                    uint dxfAggBorder = f.U32();
                    uint conn = f.U32();
                    string? stName = f.NullableWideString();
                    string? display = f.NullableWideString();
                    string? comment = f.NullableWideString();
                    string? styleHeader = f.NullableWideString();
                    string? styleData = f.NullableWideString();
                    string? styleAgg = f.NullableWideString();
                    if (f.Overrun)
                    {
                        context.Defects.Report(new Diagnostics.Defect(Diagnostics.DefectKind.Record, "xlsb.record-short", Diagnostics.DefectSeverity.Repaired,
                            "A table record is shorter than its fields claim; what was read is used.", Diagnostics.DefectLocation.InRecord(partName, r.Offset)));
                    }

                    firstDataRow = r1 + (header != 0 ? 1 : 0);
                    firstColumn = c1;
                    string name = stName ?? display ?? string.Empty;
                    x.Raw("<table").Raw(" xmlns=\"").Raw(XlsbMarkup.MainNamespace).Raw("\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:xb=\"")
                        .Raw(XlsbMarkup.Namespace).Raw("\" mc:Ignorable=\"xb\"").Attr("id", (long)id).Attr("name", name).Attr("displayName", display ?? name);
                    if (!string.IsNullOrEmpty(comment))
                    {
                        x.Attr("comment", comment);
                    }

                    if (lt == 2)
                    {
                        x.Attr("tableType", "xml");
                    }
                    else if (lt == 3)
                    {
                        x.Attr("tableType", "queryTable");
                    }

                    if (conn != 0)
                    {
                        x.Attr("connectionId", (long)conn);
                    }

                    x.Attr("ref", XlsbAddress.Range(r1, r2, c1, c2));
                    if (header == 0)
                    {
                        x.Attr("headerRowCount", 0);
                    }

                    if (totals != 0)
                    {
                        x.Attr("totalsRowCount", totals);
                    }
                    else if ((bits & 1) != 0)
                    {
                        x.Attr("totalsRowShown", 1);
                    }

                    if ((bits & 2) != 0)
                    {
                        x.Attr("insertRow", 0);
                    }

                    if ((bits & 4) != 0)
                    {
                        x.Attr("insertRowShift", 1);
                    }

                    if ((bits & 16) != 0)
                    {
                        x.Attr("published", 1);
                    }

                    Dxf(x, "headerRowDxfId", dxfHeader);
                    Dxf(x, "dataDxfId", dxfData);
                    Dxf(x, "totalsRowDxfId", dxfAgg);
                    Dxf(x, "headerRowBorderDxfId", dxfHeaderBorder);
                    Dxf(x, "tableBorderDxfId", dxfBorder);
                    Dxf(x, "totalsRowBorderDxfId", dxfAggBorder);
                    if (!string.IsNullOrEmpty(styleHeader))
                    {
                        x.Attr("headerRowCellStyle", styleHeader);
                    }

                    if (!string.IsNullOrEmpty(styleData))
                    {
                        x.Attr("dataCellStyle", styleData);
                    }

                    if (!string.IsNullOrEmpty(styleAgg))
                    {
                        x.Attr("totalsRowCellStyle", styleAgg);
                    }

                    x.Raw(">");
                    break;
                }

                case XlsbRecordType.BeginAFilter:
                    group.Add(new XlsbGroupRecord(r.Type, r.Data.ToArray(), r.Offset));
                    groupEnd = XlsbRecordType.EndAFilter;
                    break;
                case XlsbRecordType.BeginListCols:
                case XlsbRecordType.EndListCols:
                    break;
                case XlsbRecordType.BeginListCol:
                {
                    var f = new XlsbRecordFields(r.Data);
                    uint id = f.U32();
                    uint ilta = f.U32();
                    uint dxfHdr = f.U32();
                    f.U32();
                    uint dxfAgg = f.U32();
                    uint qsi = f.U32();
                    string? stName = f.NullableWideString();
                    string? caption = f.NullableWideString();
                    string? total = f.NullableWideString();
                    string? styleHeader = f.NullableWideString();
                    f.NullableWideString();
                    string? styleAgg = f.NullableWideString();
                    columnCount++;
                    var h = new XmlSink();
                    h.Raw("<tableColumn").Attr("id", (long)id);
                    if (stName is { Length: > 0 } && stName != (caption ?? stName))
                    {
                        h.Attr("uniqueName", stName);
                    }

                    h.Attr("name", caption ?? stName ?? string.Empty);
                    if (ilta != 0 && ilta < TotalFunctions.Length)
                    {
                        h.Attr("totalsRowFunction", TotalFunctions[ilta]);
                    }

                    if (!string.IsNullOrEmpty(total))
                    {
                        h.Attr("totalsRowLabel", total);
                    }

                    if (qsi != 0)
                    {
                        h.Attr("queryTableFieldId", (long)qsi);
                    }

                    Dxf(h, "headerRowDxfId", dxfHdr);
                    Dxf(h, "totalsRowDxfId", dxfAgg);
                    if (!string.IsNullOrEmpty(styleHeader))
                    {
                        h.Attr("headerRowCellStyle", styleHeader);
                    }

                    if (!string.IsNullOrEmpty(styleAgg))
                    {
                        h.Attr("totalsRowCellStyle", styleAgg);
                    }

                    columnHead = h.ToString();
                    columnBody.Clear();
                    inColumn = true;
                    break;
                }

                case XlsbRecordType.ListCCFmla when inColumn:
                case XlsbRecordType.ListTrFmla when inColumn:
                {
                    var f = new XlsbRecordFields(r.Data);
                    int flags = f.U8();
                    int row = firstDataRow;
                    string? text = features.ReadFormula(ref f, row, firstColumn + columnCount - 1, r.Offset);
                    if (text is null)
                    {
                        XlsbMarkup.Raw(columnBody, r.Type, r.Data);
                        break;
                    }

                    string tag = r.Type == XlsbRecordType.ListCCFmla ? "calculatedColumnFormula" : "totalsRowFormula";
                    columnBody.Raw("<").Raw(tag);
                    if ((flags & 2) != 0)
                    {
                        columnBody.Attr("array", 1);
                    }

                    columnBody.Raw(">").Text(text).Raw("</").Raw(tag).Raw(">");
                    break;
                }

                case XlsbRecordType.EndListCol when inColumn:
                    columns.Raw(columnHead);
                    if (columnBody.Length == 0)
                    {
                        columns.Raw("/>");
                    }
                    else
                    {
                        columns.Raw(">").Raw(columnBody.Memory.Span).Raw("</tableColumn>");
                    }

                    inColumn = false;
                    break;
                case XlsbRecordType.TableStyleClient:
                {
                    var f = new XlsbRecordFields(r.Data);
                    styleFlags = f.U16();
                    styleName = f.NullableWideString() ?? string.Empty;
                    haveStyle = true;
                    break;
                }

                case XlsbRecordType.EndList:
                    break;
                default:
                    if (inColumn)
                    {
                        XlsbMarkup.Raw(columnBody, r.Type, r.Data);
                    }
                    else
                    {
                        XlsbMarkup.Raw(children, r.Type, r.Data);
                    }

                    break;
            }
        }

        x.Raw(children.Memory.Span);
        x.Raw("<tableColumns").Attr("count", columnCount).Raw(">").Raw(columns.Memory.Span).Raw("</tableColumns>");
        if (haveStyle)
        {
            x.Raw("<tableStyleInfo");
            if (styleName.Length > 0)
            {
                x.Attr("name", styleName);
            }

            x.Attr("showFirstColumn", (styleFlags & 1) != 0 ? 1 : 0).Attr("showLastColumn", (styleFlags & 2) != 0 ? 1 : 0)
                .Attr("showRowStripes", (styleFlags & 4) != 0 ? 1 : 0).Attr("showColumnStripes", (styleFlags & 8) != 0 ? 1 : 0).Raw("/>");
        }

        x.Raw("</table>");
        return Prefix(x);
    }

    private static byte[] Prefix(XmlSink x) => System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n").Concat(x.ToArray()).ToArray();

    private static uint DxfOf(MarkupNode n, string a) => n.GetAttribute(a) is { } v && uint.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint r) ? r : 0xFFFFFFFF;

    public static byte[] FromXml(ReadOnlyMemory<byte> xml, OpenContext context, string partName, XlsbFormulaContext cx, Diagnostics.ILossSink loss)
    {
        MarkupDocument doc = MarkupDocument.Parse(xml, context, partName);
        MarkupNode t = doc.Root;
        using var ms = new MemoryStream();
        var w = new XlsbRecordWriter(ms);
        var d = new XlsbRecordData();
        var features = new XlsbFeatureWriter(w, d, context, partName, cx, loss);
        if (!XlsbAddress.TryParseRange(t.Str("ref"), out int r1, out int r2, out int c1, out int c2))
        {
            loss.Report("xlsb.table-dropped", "table without a valid range", 1, "A table has no valid range and is written empty.");
            r1 = r2 = c1 = c2 = 0;
        }

        int header = t.Int("headerRowCount", 1);
        int totals = t.Int("totalsRowCount");
        string display = t.Str("displayName", t.Str("name"));
        string name = t.Str("name", display);
        d.I32(r1);
        d.I32(r2);
        d.I32(c1);
        d.I32(c2);
        d.I32(t.Str("tableType") == "xml" ? 2 : t.Str("tableType") == "queryTable" ? 3 : 0);
        d.U32((uint)t.Int("id", 1));
        d.I32(header != 0 ? 1 : 0);
        d.I32(totals != 0 ? 1 : 0);
        d.U32((t.Bool("totalsRowShown") ? 1u : 0) | (t.GetAttribute("insertRow") is "0" or "false" ? 2u : 0) | (t.Bool("insertRowShift") ? 4u : 0) | (t.Bool("published") ? 16u : 0));
        d.U32(DxfOf(t, "headerRowDxfId"));
        d.U32(DxfOf(t, "dataDxfId"));
        d.U32(DxfOf(t, "totalsRowDxfId"));
        d.U32(DxfOf(t, "tableBorderDxfId"));
        d.U32(DxfOf(t, "headerRowBorderDxfId"));
        d.U32(DxfOf(t, "totalsRowBorderDxfId"));
        d.U32((uint)t.Int("connectionId"));
        d.NullableWideString(name == display ? null : name);
        d.NullableWideString(display);
        d.NullableWideString(t.GetAttribute("comment") ?? string.Empty);
        d.NullableWideString(t.GetAttribute("headerRowCellStyle"));
        d.NullableWideString(t.GetAttribute("dataCellStyle"));
        d.NullableWideString(t.GetAttribute("totalsRowCellStyle"));
        w.Write(XlsbRecordType.BeginList, d);
        foreach (MarkupNode c in t.ChildElements)
        {
            if (c.TryRaw(out int rt, out byte[] rd))
            {
                w.Write(rt, rd);
            }
            else if (c.Is("autoFilter"))
            {
                XlsbFilters.FromXml(c, w, d, loss);
            }
        }

        var cols = t.Element("tableColumns").Elements("tableColumn").ToList();
        d.U32((uint)cols.Count);
        w.Write(XlsbRecordType.BeginListCols, d);
        int index = 0;
        foreach (MarkupNode c in cols)
        {
            string fn = c.Str("totalsRowFunction", "none");
            int ilta = Math.Max(0, Array.IndexOf(TotalFunctions, fn));
            d.U32((uint)c.Int("id", index + 1));
            d.U32((uint)ilta);
            d.U32(DxfOf(c, "headerRowDxfId"));
            d.U32(0xFFFFFFFF);
            d.U32(DxfOf(c, "totalsRowDxfId"));
            d.U32((uint)c.Int("queryTableFieldId"));
            d.NullableWideString(c.GetAttribute("uniqueName"));
            d.NullableWideString(c.Str("name"));
            d.NullableWideString(c.GetAttribute("totalsRowLabel"));
            d.NullableWideString(c.GetAttribute("headerRowCellStyle"));
            d.NullableWideString(null);
            d.NullableWideString(c.GetAttribute("totalsRowCellStyle"));
            w.Write(XlsbRecordType.BeginListCol, d);
            int row = r1 + (header != 0 ? 1 : 0);
            foreach (MarkupNode f in c.ChildElements)
            {
                if (f.TryRaw(out int rt, out byte[] rd))
                {
                    w.Write(rt, rd);
                }
                else if (f.Is("calculatedColumnFormula") || f.Is("totalsRowFormula"))
                {
                    var rec = new XlsbRecordData();
                    rec.U8(f.Bool("array") ? 2 : 0);
                    if (features.WriteFormula(f.InnerText, row, c1 + index, "a table column in " + partName, rec))
                    {
                        w.Write(f.Is("calculatedColumnFormula") ? XlsbRecordType.ListCCFmla : XlsbRecordType.ListTrFmla, rec.Span);
                    }
                }
            }

            w.Write(XlsbRecordType.EndListCol);
            index++;
        }

        w.Write(XlsbRecordType.EndListCols);
        MarkupNode style = t.Element("tableStyleInfo");
        d.U16((style.Bool("showFirstColumn") ? 1 : 0) | (style.Bool("showLastColumn") ? 2 : 0) | (style.Bool("showRowStripes") ? 4 : 0) | (style.Bool("showColumnStripes") ? 8 : 0));
        d.NullableWideString(style.GetAttribute("name"));
        w.Write(XlsbRecordType.TableStyleClient, d);
        w.Write(XlsbRecordType.EndList);
        return ms.ToArray();
    }
}
