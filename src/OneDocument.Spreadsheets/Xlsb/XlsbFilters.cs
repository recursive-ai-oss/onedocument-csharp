using System.Globalization;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>AutoFilter records ([MS-XLSB] §2.4.8 and the filter records that follow it) and the <c>autoFilter</c> element. Columns that use a filter kind this layer does not model stay as raw records in place.</summary>
internal static class XlsbFilters
{
    private static readonly string[] DynamicTypes =
    [
        "null", "aboveAverage", "belowAverage", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "tomorrow", "today", "yesterday", "nextWeek", "thisWeek",
        "lastWeek", "nextMonth", "thisMonth", "lastMonth", "nextQuarter", "thisQuarter", "lastQuarter", "nextYear", "thisYear", "lastYear", "yearToDate", "Q1", "Q2", "Q3", "Q4",
        "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8", "M9", "M10", "M11", "M12",
    ];

    private static readonly string[] CustomOperators = ["", "lessThan", "equal", "lessThanOrEqual", "greaterThan", "notEqual", "greaterThanOrEqual"];
    private static readonly string[] Groupings = ["year", "month", "day", "hour", "minute", "second"];

    /// <summary>BrtBeginAFilter … BrtEndAFilter as an <c>autoFilter</c> element; false when the opening record is damaged.</summary>
    public static bool ToXml(List<XlsbGroupRecord> group, XmlSink x)
    {
        var f = new XlsbRecordFields(group[0].Data);
        int r1 = f.I32();
        int r2 = f.I32();
        int c1 = f.I32();
        int c2 = f.I32();
        if (f.Overrun || r1 < 0 || r2 < r1 || c1 < 0 || c2 < c1 || r2 > 1048575 || c2 > 16383)
        {
            return false;
        }

        x.Raw("<autoFilter").Attr("ref", XlsbAddress.Range(r1, r2, c1, c2)).Raw(">");
        int i = 1;
        while (i < group.Count)
        {
            XlsbGroupRecord r = group[i];
            if (r.Type == XlsbRecordType.BeginFilterColumn)
            {
                int end = i;
                while (end < group.Count && group[end].Type != XlsbRecordType.EndFilterColumn)
                {
                    end++;
                }

                if (end >= group.Count)
                {
                    end = group.Count - 1;
                }

                var column = new XmlSink();
                if (FilterColumn(group, i, end, column))
                {
                    x.Raw(column.Memory.Span);
                }
                else
                {
                    for (int k = i; k <= end; k++)
                    {
                        XlsbMarkup.Raw(x, group[k].Type, group[k].Data);
                    }
                }

                i = end + 1;
                continue;
            }

            if (r.Type != XlsbRecordType.EndAFilter)
            {
                XlsbMarkup.Raw(x, r.Type, r.Data);
            }

            i++;
        }

        x.Raw("</autoFilter>");
        return true;
    }

    private static bool FilterColumn(List<XlsbGroupRecord> g, int first, int last, XmlSink x)
    {
        var head = new XlsbRecordFields(g[first].Data);
        int col = head.I32();
        int bits = head.U16();
        if (head.Overrun || col < 0)
        {
            return false;
        }

        var body = new XmlSink();
        bool any = false;
        for (int i = first + 1; i <= last; i++)
        {
            XlsbGroupRecord r = g[i];
            switch (r.Type)
            {
                case XlsbRecordType.BeginFilters:
                {
                    var f = new XlsbRecordFields(r.Data);
                    bool blank = f.I32() != 0;
                    body.Raw("<filters");
                    if (blank)
                    {
                        body.Attr("blank", 1);
                    }

                    body.Raw(">");
                    i++;
                    for (; i <= last && g[i].Type != XlsbRecordType.EndFilters; i++)
                    {
                        if (g[i].Type == XlsbRecordType.Filter)
                        {
                            var s = new XlsbRecordFields(g[i].Data);
                            body.Raw("<filter").Attr("val", s.WideString()).Raw("/>");
                        }
                        else if (g[i].Type == XlsbRecordType.AFilterDateGroupItem)
                        {
                            if (!DateGroup(g[i].Data, body))
                            {
                                return false;
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }

                    body.Raw("</filters>");
                    any = true;
                    break;
                }

                case XlsbRecordType.BeginCustomFilters:
                {
                    var f = new XlsbRecordFields(r.Data);
                    bool and = f.I32() == AndValue;
                    body.Raw("<customFilters");
                    if (and)
                    {
                        body.Attr("and", 1);
                    }

                    body.Raw(">");
                    i++;
                    for (; i <= last && g[i].Type != XlsbRecordType.EndCustomFilters; i++)
                    {
                        if (g[i].Type != XlsbRecordType.CustomFilter || !Custom(g[i].Data, body))
                        {
                            return false;
                        }
                    }

                    body.Raw("</customFilters>");
                    any = true;
                    break;
                }

                case XlsbRecordType.Top10Filter:
                {
                    var f = new XlsbRecordFields(r.Data);
                    int flags = f.U8();
                    double val = f.Number();
                    double filterVal = f.Number();
                    if (f.Overrun)
                    {
                        return false;
                    }

                    body.Raw("<top10");
                    if ((flags & 1) == 0)
                    {
                        body.Attr("top", 0);
                    }

                    if ((flags & 2) != 0)
                    {
                        body.Attr("percent", 1);
                    }

                    body.Attr("val", val);
                    if ((flags & 4) != 0)
                    {
                        body.Attr("filterVal", filterVal);
                    }

                    body.Raw("/>");
                    any = true;
                    break;
                }

                case XlsbRecordType.DynamicFilter:
                {
                    var f = new XlsbRecordFields(r.Data);
                    int cft = f.I32();
                    f.I32();
                    double val = f.Number();
                    double max = f.Number();
                    if (f.Overrun || (uint)cft >= DynamicTypes.Length || DynamicTypes[cft].Length == 0)
                    {
                        return false;
                    }

                    body.Raw("<dynamicFilter").Attr("type", DynamicTypes[cft]);
                    if (val != 0)
                    {
                        body.Attr("val", val);
                    }

                    if (max != 0)
                    {
                        body.Attr("maxVal", max);
                    }

                    body.Raw("/>");
                    any = true;
                    break;
                }

                case XlsbRecordType.EndFilterColumn:
                    break;
                default:
                    return false;
            }
        }

        x.Raw("<filterColumn").Attr("colId", col);
        if ((bits & 1) != 0)
        {
            x.Attr("hiddenButton", 1);
        }

        if ((bits & 2) != 0)
        {
            x.Attr("showButton", 0);
        }

        if (!any)
        {
            x.Raw("/>");
        }
        else
        {
            x.Raw(">").Raw(body.Memory.Span).Raw("</filterColumn>");
        }

        return true;
    }

    // The field is named fAnd, but [MS-XLSB] §2.4.40 describes 0 as the AND relationship and 1 as OR; LibreOffice reads our files the same way. Excel settles it at the gate.
    internal const int AndValue = 0;

    private static bool Custom(byte[] data, XmlSink x)
    {
        var f = new XlsbRecordFields(data);
        int vts = f.U8();
        int sgn = f.U8();
        ReadOnlySpan<byte> union = f.Bytes(8);
        if (f.Overrun || sgn is < 1 or > 6)
        {
            return false;
        }

        string val;
        switch (vts)
        {
            case 4:
                val = System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(union).ToString("R", CultureInfo.InvariantCulture);
                break;
            case 6:
                val = f.WideString();
                if (f.Overrun)
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        x.Raw("<customFilter");
        if (sgn != 2)
        {
            x.Attr("operator", CustomOperators[sgn]);
        }

        x.Attr("val", val).Raw("/>");
        return true;
    }

    private static bool DateGroup(byte[] data, XmlSink x)
    {
        var f = new XlsbRecordFields(data);
        int yr = f.U16();
        int mon = f.U16();
        int dom = (int)f.U32();
        int hour = f.U16();
        int min = f.U16();
        int sec = f.U16();
        f.U16();
        f.U32();
        int level = (int)f.U32();
        if (f.Overrun || level is < 0 or > 5)
        {
            return false;
        }

        x.Raw("<dateGroupItem").Attr("year", yr);
        if (level >= 1)
        {
            x.Attr("month", mon);
        }

        if (level >= 2)
        {
            x.Attr("day", dom);
        }

        if (level >= 3)
        {
            x.Attr("hour", hour);
        }

        if (level >= 4)
        {
            x.Attr("minute", min);
        }

        if (level >= 5)
        {
            x.Attr("second", sec);
        }

        x.Attr("dateTimeGrouping", Groupings[level]).Raw("/>");
        return true;
    }

    // ------------------------------------------------------------------ writing

    /// <summary>Writes an <c>autoFilter</c> element as records.</summary>
    public static void FromXml(MarkupNode af, XlsbRecordWriter w, XlsbRecordData d, Diagnostics.ILossSink loss)
    {
        if (!XlsbAddress.TryParseRange(af.Str("ref"), out int r1, out int r2, out int c1, out int c2))
        {
            loss.Report("loss.xlsb-filter-dropped", "AutoFilter without a valid range", 1, "An AutoFilter has no valid range and is not written.");
            return;
        }

        d.I32(r1);
        d.I32(r2);
        d.I32(c1);
        d.I32(c2);
        w.Write(XlsbRecordType.BeginAFilter, d);
        foreach (MarkupNode c in af.ChildElements)
        {
            if (c.TryRaw(out int t, out byte[] data))
            {
                w.Write(t, data);
            }
            else if (c.Is("filterColumn"))
            {
                Column(c, w, d);
            }
            else if (c.NamespaceUri == XlsbMarkup.MainNamespace)
            {
                loss.Report("loss.xlsb-filter-element", "AutoFilter element " + c.LocalNameString, 1, "The AutoFilter element '" + c.LocalNameString + "' has no binary form written yet and is left out.");
            }
        }

        w.Write(XlsbRecordType.EndAFilter);
    }

    private static void Column(MarkupNode c, XlsbRecordWriter w, XlsbRecordData d)
    {
        d.I32(c.Int("colId"));
        d.U16((c.Bool("hiddenButton") ? 1 : 0) | (!c.Bool("showButton", true) ? 2 : 0));
        w.Write(XlsbRecordType.BeginFilterColumn, d);
        foreach (MarkupNode k in c.ChildElements)
        {
            if (k.TryRaw(out int t, out byte[] data))
            {
                w.Write(t, data);
                continue;
            }

            switch (k.LocalNameString)
            {
                case "filters":
                    d.I32(k.Bool("blank") ? 1 : 0);
                    d.I32(0);
                    w.Write(XlsbRecordType.BeginFilters, d);
                    foreach (MarkupNode item in k.ChildElements)
                    {
                        if (item.Is("filter"))
                        {
                            d.WideString(item.Str("val"));
                            w.Write(XlsbRecordType.Filter, d);
                        }
                        else if (item.Is("dateGroupItem"))
                        {
                            int level = Math.Max(0, Array.IndexOf(Groupings, item.Str("dateTimeGrouping", "year")));
                            d.U16(item.Int("year"));
                            d.U16(item.Int("month"));
                            d.U32((uint)item.Int("day"));
                            d.U16(item.Int("hour"));
                            d.U16(item.Int("minute"));
                            d.U16(item.Int("second"));
                            d.U16(0);
                            d.U32(0);
                            d.U32((uint)level);
                            w.Write(XlsbRecordType.AFilterDateGroupItem, d);
                        }
                    }

                    w.Write(XlsbRecordType.EndFilters);
                    break;
                case "customFilters":
                    d.I32(k.Bool("and") ? AndValue : 1 - AndValue);
                    w.Write(XlsbRecordType.BeginCustomFilters, d);
                    foreach (MarkupNode item in k.Elements("customFilter"))
                    {
                        int op = Array.IndexOf(CustomOperators, item.Str("operator", "equal"));
                        if (op < 1)
                        {
                            op = 2;
                        }

                        string val = item.Str("val");
                        bool number = double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double num);
                        d.U8(number ? 4 : 6);
                        d.U8(op);
                        if (number)
                        {
                            d.Number(num);
                        }
                        else
                        {
                            d.Zeros(8);
                            d.WideString(val);
                        }

                        w.Write(XlsbRecordType.CustomFilter, d);
                    }

                    w.Write(XlsbRecordType.EndCustomFilters);
                    break;
                case "top10":
                    d.U8((k.Bool("top", true) ? 1 : 0) | (k.Bool("percent") ? 2 : 0) | (k.GetAttribute("filterVal") is not null ? 4 : 0));
                    d.Number(k.Dbl("val"));
                    d.Number(k.Dbl("filterVal"));
                    w.Write(XlsbRecordType.Top10Filter, d);
                    break;
                case "dynamicFilter":
                    d.I32(Math.Max(0, Array.IndexOf(DynamicTypes, k.Str("type"))));
                    d.I32(0);
                    d.Number(k.Dbl("val"));
                    d.Number(k.Dbl("maxVal"));
                    w.Write(XlsbRecordType.DynamicFilter, d);
                    break;
            }
        }

        w.Write(XlsbRecordType.EndFilterColumn);
    }
}
