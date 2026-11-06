using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsImporter
{
    // [MS-XLS] §2.5.36 CFMultistate: the icon set numbers.
    private static readonly string[] IconSets =
    [
        "3Arrows", "3ArrowsGray", "3Flags", "3TrafficLights1", "3TrafficLights2", "3Signs", "3Symbols", "3Symbols2",
        "4Arrows", "4ArrowsGray", "4RedToBlack", "4Rating", "4TrafficLights", "5Arrows", "5ArrowsGray", "5Rating", "5Quarters",
    ];

    // [MS-XLS] §2.5.24 CFExDateTemplateParams: the template of a date rule and the period it names.
    private static readonly string?[] DatePeriodsByTemplate =
        ["today", "tomorrow", "yesterday", "last7Days", "lastMonth", "nextMonth", "thisWeek", "nextWeek", "lastWeek", "thisMonth"];

    // [MS-XLS] §2.4.57 CondFmt12: a 12-byte future-record header, then the same structure as CondFmt. Its rules are CF12 records.
    private void CondFmt12Record(BiffRecord r)
    {
        FlushCf();
        int cref = U16(r, 12 + 12);
        var refs = new List<string>();
        for (int k = 0; k < cref && 12 + 14 + k * 8 + 8 <= r.Length; k++)
        {
            int at = 12 + 14 + k * 8;
            refs.Add(RangeText(U16(r, at), U16(r, at + 4), U16(r, at + 2), U16(r, at + 6)));
        }

        if (refs.Count == 0)
        {
            return;
        }

        _sheetFeatures.CurrentCf = new CondFormat { SqRef = string.Join(' ', refs), Row = U16(r, 12 + 4), Col = U16(r, 12 + 8), Expected = U16(r, 12) };
    }

    private static string CfvoXml(ReadOnlySpan<byte> d, ref int p, Func<byte[], string?> formula, bool icon = false, bool equal = true)
    {
        int type = p < d.Length ? d[p++] : 1;
        int cce = p + 2 <= d.Length ? d[p] | (d[p + 1] << 8) : 0;
        p += 2;
        string? f = null;
        if (cce > 0 && p + cce <= d.Length)
        {
            f = formula(d.Slice(p, cce).ToArray());
            p += cce;
        }

        double num = 0;
        bool hasNum = cce == 0 && type is not (2 or 3) && p + 8 <= d.Length;
        if (hasNum)
        {
            num = BitConverter.ToDouble(d.Slice(p, 8));
            p += 8;
        }

        string tn = type switch { 1 => "num", 2 => "min", 3 => "max", 4 => "percent", 5 => "percentile", 7 => "formula", _ => "num" };
        var sb = new StringBuilder("<cfvo type=\"").Append(tn).Append('"');
        if (f is not null)
        {
            sb.Append(" val=\"").Append(Esc(f)).Append('"');
        }
        else if (hasNum)
        {
            sb.Append(" val=\"").Append(num.ToString("R", CultureInfo.InvariantCulture)).Append('"');
        }

        if (icon && !equal)
        {
            sb.Append(" gte=\"0\"");
        }

        return sb.Append("/>").ToString();
    }

    // [MS-XLS] §2.5.21 CFColor.
    private static string CfColorXml(string tag, ReadOnlySpan<byte> d, ref int p)
    {
        if (p + 16 > d.Length)
        {
            p = d.Length;
            return "<" + tag + " auto=\"1\"/>";
        }

        uint type = BitConverter.ToUInt32(d.Slice(p, 4));
        uint value = BitConverter.ToUInt32(d.Slice(p + 4, 4));
        double tint = BitConverter.ToDouble(d.Slice(p + 8, 8));
        byte r = d[p + 4];
        byte g = d[p + 5];
        byte b = d[p + 6];
        p += 16;
        string attrs = type switch
        {
            2 => "rgb=\"FF" + r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture) + "\"",
            1 => "indexed=\"" + I((int)value) + "\"",
            3 => "theme=\"" + I((int)value) + "\"",
            _ => "auto=\"1\"",
        };
        if (tint != 0 && tint is >= -1 and <= 1)
        {
            attrs += " tint=\"" + tint.ToString("R", CultureInfo.InvariantCulture) + "\"";
        }

        return "<" + tag + " " + attrs + "/>";
    }

    // [MS-XLS] §2.4.43 CF12: a rule of the Excel 2007 kinds. A rule that carries a formula keeps it; colour scales, data bars, icon sets,
    // top-N, text, date, blank, error, average and duplicate rules become the matching SpreadsheetML rule types.
    private void Cf12Record(BiffRecord r)
    {
        SheetFeatureData f = _sheetFeatures;
        if (f.CurrentCf is not { } cf)
        {
            return;
        }

        ReadOnlySpan<byte> d = _rs.Data(r).Span;
        const int h = 12;
        if (d.Length < h + 6 + 4)
        {
            Defect("biff.cf-length", DefectSeverity.Dropped, "A conditional-format rule is shorter than its parts.", r.Offset);
            return;
        }

        int ct = d[h];
        int cp = d[h + 1];
        int cce1 = d[h + 2] | (d[h + 3] << 8);
        int cce2 = d[h + 4] | (d[h + 5] << 8);
        int p = h + 6;
        uint cbDxf = BitConverter.ToUInt32(d.Slice(p, 4));
        p += 4;
        string? dxf = null;
        if (cbDxf == 0)
        {
            p += 2;
        }
        else if (p + cbDxf <= d.Length)
        {
            dxf = DxfXml(d.Slice(p, (int)cbDxf));
            p += (int)cbDxf;
        }
        else
        {
            Defect("biff.cf-length", DefectSeverity.Dropped, "A conditional-format rule's format is longer than the record.", r.Offset);
            return;
        }

        if (p + cce1 + cce2 + 2 > d.Length)
        {
            Defect("biff.cf-length", DefectSeverity.Dropped, "A conditional-format rule is shorter than its parts.", r.Offset);
            return;
        }

        ReadOnlyMemory<byte> data = _rs.Data(r);
        string? t1 = cce1 > 0 ? DecodeToText(data.Slice(p, cce1), ReadOnlyMemory<byte>.Empty, cf.Row, cf.Col, r.Offset) : null;
        p += cce1;
        string? t2 = cce2 > 0 ? DecodeToText(data.Slice(p, cce2), ReadOnlyMemory<byte>.Empty, cf.Row, cf.Col, r.Offset) : null;
        p += cce2;
        int cceActive = d[p] | (d[p + 1] << 8);
        p += 2 + cceActive;
        if (p + 1 + 2 + 2 + 1 + 16 > d.Length)
        {
            Defect("biff.cf-length", DefectSeverity.Dropped, "A conditional-format rule is shorter than its parts.", r.Offset);
            return;
        }

        int flags = d[p];
        int priority = d[p + 1] | (d[p + 2] << 8);
        int template = d[p + 3] | (d[p + 4] << 8);
        ReadOnlySpan<byte> parms = d.Slice(p + 6, 16);
        p += 6 + 16;
        bool stop = (flags & 2) != 0;

        string? Formula(byte[] rgce) => DecodeToText(rgce, ReadOnlyMemory<byte>.Empty, cf.Row, cf.Col, r.Offset);
        string dxfAttr = string.Empty;
        if (dxf is not null && ct is not (3 or 4 or 6))
        {
            _dxfs.Add(dxf);
            dxfAttr = " dxfId=\"" + I(_dxfs.Count - 1) + "\"";
        }

        string head = "<cfRule type=\"{0}\"" + dxfAttr + " priority=\"" + I(priority == 0 ? f.CfPriority++ : priority) + "\"" + (stop ? " stopIfTrue=\"1\"" : string.Empty);
        var sb = new StringBuilder();
        string? bad = null;
        switch (ct)
        {
            case 1 when template == 0:
                {
                    string[] ops = ["", "between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"];
                    if (t1 is null || cp is < 1 or > 8)
                    {
                        bad = "formula";
                        break;
                    }

                    sb.Append(string.Format(CultureInfo.InvariantCulture, head, "cellIs")).Append(" operator=\"").Append(ops[cp]).Append("\"><formula>").Append(Esc(t1)).Append("</formula>");
                    if (t2 is not null && cp <= 2)
                    {
                        sb.Append("<formula>").Append(Esc(t2)).Append("</formula>");
                    }

                    sb.Append("</cfRule>");
                    break;
                }

            case 2:
                {
                    if (t1 is null)
                    {
                        bad = "formula";
                        break;
                    }

                    string formula = "<formula>" + Esc(t1) + "</formula>";
                    string close = formula + "</cfRule>";
                    switch (template)
                    {
                        case 1:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "expression")).Append('>').Append(close);
                            break;
                        case 8:
                            {
                                int ctp = parms[0] | (parms[1] << 8);
                                string[] ops = ["containsText", "notContains", "beginsWith", "endsWith"];
                                string[] types = ["containsText", "notContainsText", "beginsWith", "endsWith"];
                                string? text = FirstTextLiteral(t1);
                                if (ctp > 3 || text is null)
                                {
                                    bad = "text";
                                    break;
                                }

                                sb.Append(string.Format(CultureInfo.InvariantCulture, head, types[ctp])).Append(" operator=\"").Append(ops[ctp]).Append("\" text=\"").Append(Esc(text)).Append("\">").Append(close);
                                break;
                            }

                        case 9:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "containsBlanks")).Append('>').Append(close);
                            break;
                        case 10:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "notContainsBlanks")).Append('>').Append(close);
                            break;
                        case 11:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "containsErrors")).Append('>').Append(close);
                            break;
                        case 12:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "notContainsErrors")).Append('>').Append(close);
                            break;
                        case >= 0x0F and <= 0x18:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "timePeriod")).Append(" timePeriod=\"").Append(DatePeriodsByTemplate[template - 0x0F]).Append("\">").Append(close);
                            break;
                        case 0x19 or 0x1A or 0x1D or 0x1E:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "aboveAverage"));
                            if (template is 0x1A or 0x1E)
                            {
                                sb.Append(" aboveAverage=\"0\"");
                            }

                            if (template is 0x1D or 0x1E)
                            {
                                sb.Append(" equalAverage=\"1\"");
                            }

                            if ((parms[0] | (parms[1] << 8)) is > 0 and var dev)
                            {
                                sb.Append(" stdDev=\"").Append(I(dev)).Append('"');
                            }

                            sb.Append("/>");
                            break;
                        case 7:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "uniqueValues")).Append("/>");
                            break;
                        case 0x1B:
                            sb.Append(string.Format(CultureInfo.InvariantCulture, head, "duplicateValues")).Append("/>");
                            break;
                        default:
                            bad = "template";
                            break;
                    }

                    break;
                }

            case 3:
                {
                    // CFGradient: 2 unused, reserved, cInterpCurve, cGradientCurve, flags; then interpolation points, then the colour curve.
                    if (p + 6 > d.Length)
                    {
                        bad = "short";
                        break;
                    }

                    int n = d[p + 3];
                    p += 6;
                    if (n is not (2 or 3))
                    {
                        bad = "points";
                        break;
                    }

                    var points = new List<string>();
                    for (int k = 0; k < n; k++)
                    {
                        points.Add(CfvoXml(d, ref p, Formula));
                        p += 8;
                    }

                    var colors = new List<string>();
                    for (int k = 0; k < n; k++)
                    {
                        p += 8;
                        colors.Add(CfColorXml("color", d, ref p));
                    }

                    sb.Append(string.Format(CultureInfo.InvariantCulture, head, "colorScale")).Append("><colorScale>").AppendJoin(string.Empty, points).AppendJoin(string.Empty, colors).Append("</colorScale></cfRule>");
                    break;
                }

            case 4:
                {
                    // CFDatabar: 2 unused, reserved, flags, min and max length, colour, two value objects.
                    if (p + 6 + 16 > d.Length)
                    {
                        bad = "short";
                        break;
                    }

                    bool showValue = (d[p + 3] & 2) != 0;
                    int min = d[p + 4];
                    int max = d[p + 5];
                    p += 6;
                    string color = CfColorXml("color", d, ref p);
                    string v1 = CfvoXml(d, ref p, Formula);
                    string v2 = CfvoXml(d, ref p, Formula);
                    sb.Append(string.Format(CultureInfo.InvariantCulture, head, "dataBar")).Append("><dataBar");
                    if (min != 10)
                    {
                        sb.Append(" minLength=\"").Append(I(min)).Append('"');
                    }

                    if (max != 90)
                    {
                        sb.Append(" maxLength=\"").Append(I(max)).Append('"');
                    }

                    if (!showValue)
                    {
                        sb.Append(" showValue=\"0\"");
                    }

                    sb.Append('>').Append(v1).Append(v2).Append(color).Append("</dataBar></cfRule>");
                    break;
                }

            case 5:
                {
                    // CFFilter: size, reserved, flags, parameter.
                    if (p + 6 > d.Length)
                    {
                        bad = "short";
                        break;
                    }

                    int fl = d[p + 3];
                    int n = d[p + 4] | (d[p + 5] << 8);
                    sb.Append(string.Format(CultureInfo.InvariantCulture, head, "top10")).Append(" rank=\"").Append(I(n)).Append('"');
                    if ((fl & 2) != 0)
                    {
                        sb.Append(" percent=\"1\"");
                    }

                    if ((fl & 1) == 0)
                    {
                        sb.Append(" bottom=\"1\"");
                    }

                    sb.Append("/>");
                    break;
                }

            case 6:
                {
                    // CFMultistate: 2 unused, reserved, state count, icon set, flags; then the thresholds.
                    if (p + 6 > d.Length)
                    {
                        bad = "short";
                        break;
                    }

                    int states = d[p + 3];
                    int set = d[p + 4];
                    int fl = d[p + 5];
                    p += 6;
                    if (set >= IconSets.Length || states < 2 || states > 5)
                    {
                        bad = "icon set";
                        break;
                    }

                    var points = new List<string>();
                    for (int k = 0; k < states; k++)
                    {
                        string pointXml = CfvoXml(d, ref p, Formula);
                        bool equal = p < d.Length && d[p] != 0;
                        p += 5;
                        points.Add(equal ? pointXml : pointXml.Replace("/>", " gte=\"0\"/>", StringComparison.Ordinal));
                    }

                    sb.Append(string.Format(CultureInfo.InvariantCulture, head, "iconSet")).Append("><iconSet iconSet=\"").Append(IconSets[set]).Append('"');
                    if ((fl & 1) != 0)
                    {
                        sb.Append(" showValue=\"0\"");
                    }

                    if ((fl & 4) != 0)
                    {
                        sb.Append(" reverse=\"1\"");
                    }

                    sb.Append('>').AppendJoin(string.Empty, points).Append("</iconSet></cfRule>");
                    break;
                }

            default:
                bad = "type";
                break;
        }

        if (bad is not null)
        {
            _lossCounts["loss.conditional-formats-extended"] = _lossCounts.GetValueOrDefault("loss.conditional-formats-extended") + 1;
            return;
        }

        cf.Rules.Add(sb.ToString());
    }

    // The text a text rule searches for: the first string constant in its formula (SEARCH("x",A1), LEFT(A1,LEN("x"))="x", and so on).
    private static string? FirstTextLiteral(string formula)
    {
        int i = formula.IndexOf('"', StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        for (int k = i + 1; k < formula.Length; k++)
        {
            if (formula[k] == '"')
            {
                if (k + 1 < formula.Length && formula[k + 1] == '"')
                {
                    sb.Append('"');
                    k++;
                    continue;
                }

                return sb.ToString();
            }

            sb.Append(formula[k]);
        }

        return null;
    }
}
