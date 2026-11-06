using System.Linq;
using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Conditional formatting: BrtBeginConditionalFormatting … BrtEndConditionalFormatting ([MS-XLSB] §2.4.34, §2.4.23, §2.5.15–§2.5.19) and the <c>conditionalFormatting</c> element.</summary>
internal static class XlsbConditionalFormatting
{
    private const int CellIs = 1, ExprIs = 2, Gradient = 3, DataBar = 4, Filter = 5, MultiState = 6;
    private const int TplExpr = 0, TplFormula = 1, TplGradient = 2, TplDataBar = 3, TplMultiState = 4, TplFilter = 5, TplUnique = 7, TplContainsText = 8, TplBlanks = 9, TplNoBlanks = 10,
        TplErrors = 11, TplNoErrors = 12, TplToday = 0xF, TplThisMonth = 0x18, TplAbove = 0x19, TplBelow = 0x1A, TplDuplicate = 0x1B, TplEqualAbove = 0x1D, TplEqualBelow = 0x1E;

    private static readonly string[] CellOperators = ["", "between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"];
    private static readonly string[] TextOperators = ["containsText", "notContains", "beginsWith", "endsWith"];
    private static readonly string[] TimePeriods = ["today", "tomorrow", "yesterday", "last7Days", "lastMonth", "nextMonth", "thisWeek", "nextWeek", "lastWeek", "thisMonth"];
    private static readonly string[] ValueTypes = ["", "num", "min", "max", "percent", "percentile", "", "formula"];
    private static readonly string[] IconSets =
    [
        "3Arrows", "3ArrowsGray", "3Flags", "3TrafficLights1", "3TrafficLights2", "3Signs", "3Symbols", "3Symbols2", "4Arrows", "4ArrowsGray", "4RedToBlack", "4Rating",
        "4TrafficLights", "5Arrows", "5ArrowsGray", "5Rating", "5Quarters",
    ];

    // ------------------------------------------------------------------ binary → markup

    public static bool ToXml(List<XlsbGroupRecord> g, XlsbFeatureReader reader, XmlSink x)
    {
        var f = new XlsbRecordFields(g[0].Data);
        int ccf = f.I32();
        int pivot = f.I32();
        string? sqref = XlsbFeatureReader.ReadSqRef(ref f, out int row, out int col);
        if (sqref is null || f.Overrun || ccf < 0)
        {
            return false;
        }

        var body = new XmlSink();
        int i = 1;
        int rules = 0;
        while (i < g.Count && g[i].Type != XlsbRecordType.EndConditionalFormatting)
        {
            if (g[i].Type != XlsbRecordType.BeginCFRule)
            {
                return false;
            }

            int end = i;
            while (end < g.Count && g[end].Type != XlsbRecordType.EndCFRule)
            {
                end++;
            }

            if (end >= g.Count || !Rule(g, i, end, reader, row, col, body))
            {
                return false;
            }

            rules++;
            i = end + 1;
        }

        if (rules != ccf)
        {
            return false;
        }

        x.Raw("<conditionalFormatting");
        if (pivot != 0)
        {
            x.Attr("pivot", 1);
        }

        x.Attr("sqref", sqref).Raw(">").Raw(body.Memory.Span).Raw("</conditionalFormatting>");
        return true;
    }

    private static bool Rule(List<XlsbGroupRecord> g, int first, int last, XlsbFeatureReader reader, int row, int col, XmlSink x)
    {
        XlsbGroupRecord head = g[first];
        var f = new XlsbRecordFields(head.Data);
        int type = f.I32();
        int tpl = f.I32();
        uint dxf = f.U32();
        int pri = f.I32();
        int param = f.I32();
        f.I32();
        f.I32();
        int flags = f.U16();
        uint cb1 = f.U32();
        uint cb2 = f.U32();
        uint cb3 = f.U32();
        string? str = f.NullableWideString();
        if (f.Overrun)
        {
            return false;
        }

        var formulas = new List<string>();
        foreach (uint cb in new[] { cb1, cb2, cb3 })
        {
            if (cb == 0)
            {
                continue;
            }

            string? text = reader.ReadFormula(ref f, row, col, head.Offset);
            if (text is null)
            {
                return false;
            }

            formulas.Add(text);
        }

        string? ruleType;
        var attrs = new XmlSink();
        switch (type, tpl)
        {
            case (CellIs, TplExpr):
                if (param is < 1 or > 8)
                {
                    return false;
                }

                ruleType = "cellIs";
                attrs.Attr("operator", CellOperators[param]);
                break;
            case (ExprIs, TplFormula):
                ruleType = "expression";
                break;
            case (ExprIs, TplUnique):
                ruleType = "uniqueValues";
                break;
            case (ExprIs, TplDuplicate):
                ruleType = "duplicateValues";
                break;
            case (ExprIs, TplContainsText):
                if (param is < 0 or > 3)
                {
                    return false;
                }

                ruleType = param switch { 0 => "containsText", 1 => "notContainsText", 2 => "beginsWith", _ => "endsWith" };
                attrs.Attr("operator", TextOperators[param]).Attr("text", str ?? string.Empty);
                break;
            case (ExprIs, TplBlanks):
                ruleType = "containsBlanks";
                break;
            case (ExprIs, TplNoBlanks):
                ruleType = "notContainsBlanks";
                break;
            case (ExprIs, TplErrors):
                ruleType = "containsErrors";
                break;
            case (ExprIs, TplNoErrors):
                ruleType = "notContainsErrors";
                break;
            case (ExprIs, >= TplToday and <= TplThisMonth):
                ruleType = "timePeriod";
                attrs.Attr("timePeriod", TimePeriods[tpl - TplToday]);
                break;
            case (ExprIs, TplAbove or TplBelow or TplEqualAbove or TplEqualBelow):
                ruleType = "aboveAverage";
                if (tpl is TplBelow or TplEqualBelow)
                {
                    attrs.Attr("aboveAverage", 0);
                }

                if (tpl is TplEqualAbove or TplEqualBelow)
                {
                    attrs.Attr("equalAverage", 1);
                }

                if (param > 0)
                {
                    attrs.Attr("stdDev", param);
                }

                break;
            case (Filter, TplFilter):
                ruleType = "top10";
                attrs.Attr("rank", param);
                if ((flags & 0x10) != 0)
                {
                    attrs.Attr("percent", 1);
                }

                if ((flags & 0x08) != 0)
                {
                    attrs.Attr("bottom", 1);
                }

                break;
            case (Gradient, TplGradient):
                ruleType = "colorScale";
                break;
            case (DataBar, TplDataBar):
                ruleType = "dataBar";
                break;
            case (MultiState, TplMultiState):
                ruleType = "iconSet";
                break;
            default:
                return false;
        }

        x.Raw("<cfRule").Attr("type", ruleType);
        if (dxf != 0xFFFFFFFF && type is CellIs or ExprIs or Filter)
        {
            x.Attr("dxfId", (long)dxf);
        }

        x.Attr("priority", pri);
        if ((flags & 0x02) != 0)
        {
            x.Attr("stopIfTrue", 1);
        }

        x.Raw(attrs.Memory.Span).Raw(">");
        foreach (string text in formulas)
        {
            x.Raw("<formula>").Text(text).Raw("</formula>");
        }

        if (type is Gradient or DataBar or MultiState && !Visual(g, first + 1, last, type, reader, row, col, x))
        {
            return false;
        }

        x.Raw("</cfRule>");
        return true;
    }

    private static bool Visual(List<XlsbGroupRecord> g, int from, int to, int type, XlsbFeatureReader reader, int row, int col, XmlSink x)
    {
        var values = new List<(int Type, double Number, string? Formula, bool Gte)>();
        var colors = new List<XlsbColor>();
        XlsbGroupRecord begin = default;
        for (int i = from; i < to; i++)
        {
            XlsbGroupRecord r = g[i];
            switch (r.Type)
            {
                case XlsbRecordType.BeginColorScale or XlsbRecordType.BeginDatabar or XlsbRecordType.BeginIconSet:
                    begin = r;
                    break;
                case XlsbRecordType.EndColorScale or XlsbRecordType.EndDatabar or XlsbRecordType.EndIconSet:
                    break;
                case XlsbRecordType.CFVO:
                {
                    var f = new XlsbRecordFields(r.Data);
                    int vt = f.I32();
                    double num = f.Number();
                    f.I32();
                    int gte = f.I32();
                    uint cb = f.U32();
                    string? formula = null;
                    if (cb > 0)
                    {
                        formula = reader.ReadFormula(ref f, row, col, r.Offset);
                        if (formula is null)
                        {
                            return false;
                        }
                    }

                    if (f.Overrun || vt is < 1 or > 7 || ValueTypes[vt].Length == 0)
                    {
                        return false;
                    }

                    values.Add((vt, num, formula, gte != 0));
                    break;
                }

                case XlsbRecordType.Color:
                {
                    var f = new XlsbRecordFields(r.Data);
                    colors.Add(XlsbColor.Read(ref f));
                    break;
                }

                default:
                    return false;
            }
        }

        if (begin.Data is null)
        {
            return false;
        }

        void Cfvo((int Type, double Number, string? Formula, bool Gte) v, bool icon)
        {
            x.Raw("<cfvo").Attr("type", ValueTypes[v.Type]);
            if (v.Type == 7 || v.Formula is not null)
            {
                x.Attr("val", v.Formula ?? string.Empty);
            }
            else if (v.Type is 1 or 4 or 5)
            {
                x.Attr("val", v.Number);
            }

            if (icon && !v.Gte)
            {
                x.Attr("gte", 0);
            }

            x.Raw("/>");
        }

        void Color(XlsbColor c)
        {
            x.Raw("<color");
            c.Attributes(x);
            x.Raw("/>");
        }

        switch (type)
        {
            case Gradient:
                if (values.Count != colors.Count || values.Count is < 2 or > 3)
                {
                    return false;
                }

                x.Raw("<colorScale>");
                foreach (var v in values)
                {
                    Cfvo(v, false);
                }

                foreach (XlsbColor c in colors)
                {
                    Color(c);
                }

                x.Raw("</colorScale>");
                return true;
            case DataBar:
            {
                var f = new XlsbRecordFields(begin.Data);
                int min = f.U8();
                int max = f.U8();
                int show = f.U8();
                if (values.Count != 2 || colors.Count != 1 || f.Overrun)
                {
                    return false;
                }

                x.Raw("<dataBar");
                if (min != 10)
                {
                    x.Attr("minLength", min);
                }

                if (max != 90)
                {
                    x.Attr("maxLength", max);
                }

                if (show == 0)
                {
                    x.Attr("showValue", 0);
                }

                x.Raw(">");
                Cfvo(values[0], false);
                Cfvo(values[1], false);
                Color(colors[0]);
                x.Raw("</dataBar>");
                return true;
            }

            default:
            {
                var f = new XlsbRecordFields(begin.Data);
                int set = f.I32();
                int bits = f.U16();
                if (f.Overrun || (uint)set >= IconSets.Length || values.Count == 0)
                {
                    return false;
                }

                x.Raw("<iconSet").Attr("iconSet", IconSets[set]);
                if ((bits & 2) != 0)
                {
                    x.Attr("showValue", 0);
                }

                if ((bits & 4) != 0)
                {
                    x.Attr("reverse", 1);
                }

                x.Raw(">");
                foreach (var v in values)
                {
                    Cfvo(v, true);
                }

                x.Raw("</iconSet>");
                return true;
            }
        }
    }

    // ------------------------------------------------------------------ markup → binary

    private static readonly int[] TimePeriodParams = [0, 6, 1, 2, 5, 8, 3, 7, 4, 9];

    public static void FromXml(MarkupNode cf, XlsbRecordWriter w, XlsbFeatureWriter fw, ILossSink loss)
    {
        var rules = cf.Elements("cfRule").ToList();
        var range = new XlsbRecordData();
        if (rules.Count == 0 || !XlsbFeatureWriter.WriteSqRef(cf.Str("sqref"), range, out int row, out int col))
        {
            loss.Report("loss.xlsb-conditional-format-dropped", "conditional formatting without a rule or a valid range", 1,
                "A conditional formatting block has no rule or no valid range and is not written.");
            return;
        }

        var body = new MemoryStream();
        var bw = new XlsbRecordWriter(body);
        int written = 0;
        foreach (MarkupNode rule in rules)
        {
            if (Rule(rule, bw, fw, row, col, loss))
            {
                written++;
            }
        }

        if (written == 0)
        {
            return;
        }

        var head = new XlsbRecordData();
        head.I32(written);
        head.I32(cf.Bool("pivot") ? 1 : 0);
        head.Bytes(range.Span);
        w.Write(XlsbRecordType.BeginConditionalFormatting, head);
        w.WriteFramed(body.ToArray());
        w.Write(XlsbRecordType.EndConditionalFormatting);
    }

    private static bool Rule(MarkupNode rule, XlsbRecordWriter bw, XlsbFeatureWriter fw, int row, int col, ILossSink loss)
    {
        string kind = rule.Str("type");
        int type;
        int tpl;
        int param = 0;
        int flags = 0;
        string text = string.Empty;
        switch (kind)
        {
            case "cellIs":
                type = CellIs;
                tpl = TplExpr;
                param = Math.Max(1, Array.IndexOf(CellOperators, rule.Str("operator", "equal")));
                break;
            case "expression":
                (type, tpl) = (ExprIs, TplFormula);
                break;
            case "uniqueValues":
                (type, tpl) = (ExprIs, TplUnique);
                break;
            case "duplicateValues":
                (type, tpl) = (ExprIs, TplDuplicate);
                break;
            case "containsText" or "notContainsText" or "beginsWith" or "endsWith":
                (type, tpl) = (ExprIs, TplContainsText);
                param = kind switch { "containsText" => 0, "notContainsText" => 1, "beginsWith" => 2, _ => 3 };
                text = rule.Str("text");
                break;
            case "containsBlanks":
                (type, tpl) = (ExprIs, TplBlanks);
                break;
            case "notContainsBlanks":
                (type, tpl) = (ExprIs, TplNoBlanks);
                break;
            case "containsErrors":
                (type, tpl) = (ExprIs, TplErrors);
                break;
            case "notContainsErrors":
                (type, tpl) = (ExprIs, TplNoErrors);
                break;
            case "timePeriod":
            {
                int k = Array.IndexOf(TimePeriods, rule.Str("timePeriod"));
                if (k < 0)
                {
                    goto default;
                }

                (type, tpl, param) = (ExprIs, TplToday + k, TimePeriodParams[k]);
                break;
            }

            case "aboveAverage":
            {
                bool above = rule.Bool("aboveAverage", true);
                bool equal = rule.Bool("equalAverage");
                (type, tpl) = (ExprIs, above ? (equal ? TplEqualAbove : TplAbove) : (equal ? TplEqualBelow : TplBelow));
                param = rule.Int("stdDev");
                flags |= above ? 0x04 : 0;
                break;
            }

            case "top10":
                (type, tpl) = (Filter, TplFilter);
                param = rule.Int("rank", 10);
                flags |= (rule.Bool("percent") ? 0x10 : 0) | (rule.Bool("bottom") ? 0x08 : 0);
                break;
            case "colorScale":
                (type, tpl) = (Gradient, TplGradient);
                break;
            case "dataBar":
                (type, tpl) = (DataBar, TplDataBar);
                break;
            case "iconSet":
                (type, tpl) = (MultiState, TplMultiState);
                break;
            default:
                loss.Report("loss.xlsb-conditional-format-rule", "conditional formatting rule " + kind, 1,
                    "A conditional formatting rule of type '" + kind + "' has no binary form written yet and is left out.");
                return false;
        }

        if (rule.Bool("stopIfTrue"))
        {
            flags |= 0x02;
        }

        var rgces = new List<(byte[] Rgce, byte[] Rgcb)>();
        foreach (MarkupNode f in rule.Elements("formula").Take(3))
        {
            if (!fw.TryEncode(f.InnerText, row, col, "a conditional formatting rule", out byte[] rgce, out byte[] rgcb))
            {
                return false;
            }

            rgces.Add((rgce, rgcb));
        }

        var d = new XlsbRecordData();
        d.I32(type);
        d.I32(tpl);
        d.U32(type is CellIs or ExprIs or Filter ? (rule.GetAttribute("dxfId") is { } dx && uint.TryParse(dx, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint dxf) ? dxf : 0xFFFFFFFF) : 0xFFFFFFFF);
        d.I32(rule.Int("priority", 1));
        d.I32(param);
        d.I32(0);
        d.I32(0);
        d.U16(flags);
        for (int i = 0; i < 3; i++)
        {
            d.U32(i < rgces.Count ? (uint)rgces[i].Rgce.Length : 0);
        }

        d.NullableWideString(text.Length > 0 ? text : null);
        foreach ((byte[] rgce, byte[] rgcb) in rgces)
        {
            d.U32((uint)rgce.Length);
            d.Bytes(rgce);
            d.U32((uint)rgcb.Length);
            d.Bytes(rgcb);
        }

        bw.Write(XlsbRecordType.BeginCFRule, d);
        switch (type)
        {
            case Gradient:
            {
                MarkupNode cs = rule.Element("colorScale");
                bw.Write(XlsbRecordType.BeginColorScale);
                foreach (MarkupNode v in cs.Elements("cfvo"))
                {
                    Cfvo(v, bw, fw, row, col, false);
                }

                foreach (MarkupNode c in cs.Elements("color"))
                {
                    ColorRecord(c, bw);
                }

                bw.Write(XlsbRecordType.EndColorScale);
                break;
            }

            case DataBar:
            {
                MarkupNode db = rule.Element("dataBar");
                var h = new XlsbRecordData();
                h.U8(db.Int("minLength", 10));
                h.U8(db.Int("maxLength", 90));
                h.U8(db.Bool("showValue", true) ? 1 : 0);
                bw.Write(XlsbRecordType.BeginDatabar, h);
                foreach (MarkupNode v in db.Elements("cfvo"))
                {
                    Cfvo(v, bw, fw, row, col, false);
                }

                foreach (MarkupNode c in db.Elements("color"))
                {
                    ColorRecord(c, bw);
                }

                bw.Write(XlsbRecordType.EndDatabar);
                break;
            }

            case MultiState:
            {
                MarkupNode ic = rule.Element("iconSet");
                var h = new XlsbRecordData();
                h.I32(Math.Max(0, Array.IndexOf(IconSets, ic.Str("iconSet", "3TrafficLights1"))));
                h.U16((ic.Bool("showValue", true) ? 0 : 2) | (ic.Bool("reverse") ? 4 : 0));
                bw.Write(XlsbRecordType.BeginIconSet, h);
                foreach (MarkupNode v in ic.Elements("cfvo"))
                {
                    Cfvo(v, bw, fw, row, col, true);
                }

                bw.Write(XlsbRecordType.EndIconSet);
                break;
            }
        }

        bw.Write(XlsbRecordType.EndCFRule);
        return true;
    }

    private static void ColorRecord(MarkupNode c, XlsbRecordWriter bw)
    {
        var d = new XlsbRecordData();
        XlsbColor.FromXml(c).Write(d);
        bw.Write(XlsbRecordType.Color, d);
    }

    private static void Cfvo(MarkupNode v, XlsbRecordWriter bw, XlsbFeatureWriter fw, int row, int col, bool icon)
    {
        int vt = Math.Max(1, Array.IndexOf(ValueTypes, v.Str("type", "num")));
        string val = v.Str("val");
        var d = new XlsbRecordData();
        d.I32(vt);
        double num = 0;
        byte[] rgce = [];
        byte[] rgcb = [];
        bool hasFormula = false;
        if (vt == 7)
        {
            hasFormula = fw.TryEncode(val, row, col, "a conditional formatting value", out rgce, out rgcb);
        }
        else if (vt is 1 or 4 or 5 && val.Length > 0 && !double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out num))
        {
            // A formula where a number is expected (Excel writes these as formulas with a numeric type).
            hasFormula = fw.TryEncode(val, row, col, "a conditional formatting value", out rgce, out rgcb);
        }

        d.Number(num);
        d.I32(icon ? 1 : 0);
        d.I32(v.Bool("gte", true) ? 1 : 0);
        d.U32(hasFormula ? (uint)rgce.Length : 0);
        if (hasFormula)
        {
            d.U32((uint)rgce.Length);
            d.Bytes(rgce);
            d.U32((uint)rgcb.Length);
            d.Bytes(rgcb);
        }

        bw.Write(XlsbRecordType.CFVO, d);
    }
}
