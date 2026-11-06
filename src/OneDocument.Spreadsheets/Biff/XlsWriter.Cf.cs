using System.Linq;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsWriter
{
    private static readonly string[] CfOperators = ["", "between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"];

    // [MS-XLS] §2.5.36 CFMultistate: the icon set numbers.
    internal static readonly string[] IconSetNames =
    [
        "3Arrows", "3ArrowsGray", "3Flags", "3TrafficLights1", "3TrafficLights2", "3Signs", "3Symbols", "3Symbols2",
        "4Arrows", "4ArrowsGray", "4RedToBlack", "4Rating", "4TrafficLights", "5Arrows", "5ArrowsGray", "5Rating", "5Quarters",
    ];

    // [MS-XLS] §2.4.43 CF12 icfTemplate, for the date rules (and the dateOp their parameters carry, §2.5.24).
    internal static readonly (string Period, int Template, int DateOp)[] DatePeriods =
    [
        ("today", 0x0F, 0), ("tomorrow", 0x10, 6), ("yesterday", 0x11, 1), ("last7Days", 0x12, 2), ("lastMonth", 0x13, 5),
        ("nextMonth", 0x14, 8), ("thisWeek", 0x15, 3), ("nextWeek", 0x16, 7), ("lastWeek", 0x17, 4), ("thisMonth", 0x18, 9),
    ];

    private int _cfId;

    private void WriteConditionalFormats(Worksheet ws)
    {
        _cfId = 0;
        foreach (ConditionalFormat cf in ws.ConditionalFormats)
        {
            if (cf.IsExtended)
            {
                _loss.Report("loss.conditional-formats-extended", "conditional formats", cf.Rules.Count, "Rules of the Excel 2010 extension (cross-sheet or data bar extras) are not written.");
                continue;
            }

            var ranges = cf.Ranges.Where(r => r.BottomRight.Row <= MaxRows && r.BottomRight.Column <= MaxColumns).ToList();
            IReadOnlyList<ConditionalFormatRule> rules = cf.Rules;
            if (ranges.Count == 0 || rules.Count == 0)
            {
                _loss.Report("loss.conditional-formats", "conditional formats", Math.Max(rules.Count, 1), "Rules outside the BIFF8 grid were not written.");
                continue;
            }

            int first = ranges.Min(r => r.TopLeft.Row) - 1;
            int firstCol = ranges.Min(r => r.TopLeft.Column) - 1;
            bool basic = rules.Count <= 3 && rules.All(r => r.Type is ConditionalRuleType.Expression or ConditionalRuleType.CellIs && !r.StopIfTrue);
            var records = new List<byte[]>();
            foreach (ConditionalFormatRule r in rules)
            {
                byte[]? rec = basic ? PlainCf(r, first, firstCol) : Cf12(r, first, firstCol);
                if (rec is not null)
                {
                    records.Add(rec);
                }
            }

            if (records.Count == 0)
            {
                continue;
            }

            _cfId++;
            var h = new BiffBuffer();
            if (!basic)
            {
                h.U16(0x0879);
                h.U16(1);
                h.U16(ranges.Min(r => r.TopLeft.Row) - 1);
                h.U16(ranges.Max(r => r.BottomRight.Row) - 1);
                h.U16(ranges.Min(r => r.TopLeft.Column) - 1);
                h.U16(ranges.Max(r => r.BottomRight.Column) - 1);
            }

            h.U16(records.Count);
            h.U16(_cfId << 1);
            h.U16(ranges.Min(r => r.TopLeft.Row) - 1);
            h.U16(ranges.Max(r => r.BottomRight.Row) - 1);
            h.U16(ranges.Min(r => r.TopLeft.Column) - 1);
            h.U16(ranges.Max(r => r.BottomRight.Column) - 1);
            h.U16(ranges.Count);
            foreach (CellRange r in ranges)
            {
                h.U16(r.TopLeft.Row - 1);
                h.U16(r.BottomRight.Row - 1);
                h.U16(r.TopLeft.Column - 1);
                h.U16(r.BottomRight.Column - 1);
            }

            _s.Record(basic ? 0x01B0 : 0x0879, h);
            foreach (byte[] rec in records)
            {
                _s.Record(basic ? 0x01B1 : 0x087A, rec);
            }
        }
    }

    private byte[]? Dxf(ConditionalFormatRule r)
    {
        if (r.DxfId is { } d && d >= 0 && d < _wb.Styles.DifferentialFormatCount)
        {
            return _styles.EncodeDxf(_wb.Styles.GetDifferentialFormat(d));
        }

        return null;
    }

    private bool Encode(string text, int row, int col, out byte[] rgce)
    {
        if (TryEncodeText(text, row, col, out Encoded? e, out _) && e!.Rgcb.Length == 0)
        {
            rgce = e.Rgce;
            return true;
        }

        rgce = [];
        return false;
    }

    // [MS-XLS] §2.4.42 CF: the type, the comparison, the two formula sizes, the DXFN, the formulas.
    private byte[]? PlainCf(ConditionalFormatRule r, int row, int col)
    {
        byte[] g1 = [];
        byte[] g2 = [];
        int ct;
        int cp = 0;
        IReadOnlyList<string> f = r.Formulas;
        if (r.Type == ConditionalRuleType.CellIs)
        {
            ct = 1;
            cp = Array.IndexOf(CfOperators, r.Operator ?? string.Empty);
            if (cp <= 0 || f.Count == 0 || !Encode(f[0], row, col, out g1) || (f.Count > 1 && cp <= 2 && !Encode(f[1], row, col, out g2)))
            {
                _loss.Report("loss.conditional-formats", "conditional formats", 1, "A rule whose comparison or formula BIFF8 cannot express was not written.");
                return null;
            }
        }
        else
        {
            ct = 2;
            if (f.Count == 0 || !Encode(f[0], row, col, out g1))
            {
                _loss.Report("loss.conditional-formats", "conditional formats", 1, "A rule whose formula BIFF8 cannot express was not written.");
                return null;
            }
        }

        var b = new BiffBuffer();
        b.U8(ct);
        b.U8(cp);
        b.U16(g1.Length);
        b.U16(g2.Length);
        b.Bytes(Dxf(r) ?? _styles.EncodeDxf(new DifferentialFormat()));
        b.Bytes(g1);
        b.Bytes(g2);
        return b.Span.ToArray();
    }

    private static void CfColor(BiffBuffer b, int argb)
    {
        b.U32(2); // XCLRRGB
        b.U8((argb >> 16) & 0xFF);
        b.U8((argb >> 8) & 0xFF);
        b.U8(argb & 0xFF);
        b.U8(0);
        b.F64(0);
    }

    // [MS-XLS] §2.5.39 CFVO.
    private bool Cfvo(BiffBuffer b, ConditionalValuePoint p, int row, int col)
    {
        int type = p.Type switch { "num" => 1, "min" or "autoMin" => 2, "max" or "autoMax" => 3, "percent" => 4, "percentile" => 5, "formula" => 7, _ => 1 };
        b.U8(type);
        if (type == 7)
        {
            if (p.Value is null || !Encode(p.Value, row, col, out byte[] g))
            {
                return false;
            }

            b.U16(g.Length);
            b.Bytes(g);
            return true;
        }

        b.U16(0);
        if (type is 2 or 3)
        {
            return true;
        }

        double v = double.TryParse(p.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;
        b.F64(v);
        return true;
    }

    private byte[]? Cf12(ConditionalFormatRule r, int row, int col)
    {
        int ct;
        int cp = 0;
        int template;
        byte[] g1 = [];
        byte[] g2 = [];
        var parms = new byte[16];
        var content = new BiffBuffer();
        IReadOnlyList<string> f = r.Formulas;
        switch (r.Type)
        {
            case ConditionalRuleType.Expression:
                ct = 2;
                template = 1;
                if (f.Count == 0 || !Encode(f[0], row, col, out g1))
                {
                    return Reject();
                }

                break;
            case ConditionalRuleType.CellIs:
                ct = 1;
                template = 0;
                cp = Array.IndexOf(CfOperators, r.Operator ?? string.Empty);
                if (cp <= 0 || f.Count == 0 || !Encode(f[0], row, col, out g1) || (f.Count > 1 && cp <= 2 && !Encode(f[1], row, col, out g2)))
                {
                    return Reject();
                }

                break;
            case ConditionalRuleType.ColorScale:
                {
                    ColorScaleInfo cs = r.ColorScale!;
                    ct = 3;
                    template = 2;
                    int n = cs.Points.Count;
                    content.U16(0);
                    content.U8(0);
                    content.U8(n);
                    content.U8(n);
                    content.U8(3);
                    for (int i = 0; i < n; i++)
                    {
                        if (!Cfvo(content, cs.Points[i], row, col))
                        {
                            return Reject();
                        }

                        content.F64(n == 3 ? i * 0.5 : i);
                    }

                    for (int i = 0; i < n; i++)
                    {
                        content.F64(n == 3 ? i * 0.5 : i);
                        CfColor(content, _wb.Styles.ResolveColor(cs.Colors[i]));
                    }

                    break;
                }

            case ConditionalRuleType.DataBar:
                {
                    DataBarInfo db = r.DataBar!;
                    ct = 4;
                    template = 3;
                    content.U16(0);
                    content.U8(0);
                    content.U8(db.ShowValue ? 2 : 0);
                    content.U8(db.MinLength);
                    content.U8(db.MaxLength);
                    CfColor(content, _wb.Styles.ResolveColor(db.Color));
                    if (db.Points.Count != 2 || !Cfvo(content, db.Points[0], row, col) || !Cfvo(content, db.Points[1], row, col))
                    {
                        return Reject();
                    }

                    break;
                }

            case ConditionalRuleType.IconSet:
                {
                    IconSetInfo ic = r.IconSet!;
                    int idx = Array.IndexOf(IconSetNames, ic.Name);
                    if (idx < 0)
                    {
                        return Reject();
                    }

                    ct = 6;
                    template = 4;
                    content.U16(0);
                    content.U8(0);
                    content.U8(ic.Points.Count);
                    content.U8(idx);
                    content.U8((ic.ShowValue ? 0 : 1) | (ic.Reverse ? 4 : 0));
                    foreach (ConditionalValuePoint p in ic.Points)
                    {
                        if (!Cfvo(content, p, row, col))
                        {
                            return Reject();
                        }

                        content.U8(p.GreaterOrEqual ? 1 : 0);
                        content.U32(0);
                    }

                    break;
                }

            case ConditionalRuleType.Top10:
                ct = 5;
                template = 5;
                content.U16(4);
                content.U8(0);
                content.U8((r.Bottom ? 0 : 1) | (r.Percent ? 2 : 0));
                content.U16(r.Rank);
                parms[0] = (byte)((r.Bottom ? 0 : 1) | (r.Percent ? 2 : 0));
                parms[1] = (byte)r.Rank;
                parms[2] = (byte)(r.Rank >> 8);
                break;
            case ConditionalRuleType.ContainsText or ConditionalRuleType.NotContainsText or ConditionalRuleType.BeginsWith or ConditionalRuleType.EndsWith:
                ct = 2;
                template = 8;
                parms[0] = (byte)(r.Type switch { ConditionalRuleType.ContainsText => 0, ConditionalRuleType.NotContainsText => 1, ConditionalRuleType.BeginsWith => 2, _ => 3 });
                if (f.Count == 0 || !Encode(f[0], row, col, out g1))
                {
                    return Reject();
                }

                break;
            case ConditionalRuleType.ContainsBlanks or ConditionalRuleType.NotContainsBlanks or ConditionalRuleType.ContainsErrors or ConditionalRuleType.NotContainsErrors:
                ct = 2;
                template = r.Type switch { ConditionalRuleType.ContainsBlanks => 9, ConditionalRuleType.NotContainsBlanks => 10, ConditionalRuleType.ContainsErrors => 11, _ => 12 };
                if (f.Count == 0 || !Encode(f[0], row, col, out g1))
                {
                    return Reject();
                }

                break;
            case ConditionalRuleType.TimePeriod:
                {
                    int at = Array.FindIndex(DatePeriods, d => d.Period == r.TimePeriod);
                    if (at < 0 || f.Count == 0 || !Encode(f[0], row, col, out g1))
                    {
                        return Reject();
                    }

                    ct = 2;
                    template = DatePeriods[at].Template;
                    parms[0] = (byte)DatePeriods[at].DateOp;
                    break;
                }

            default:
                return Reject();
        }

        byte[]? dxf = ct is 3 or 4 or 6 ? null : Dxf(r) ?? _styles.EncodeDxf(new DifferentialFormat());
        var b = new BiffBuffer();
        b.U16(0x087A);
        b.U16(0);
        b.Zeros(8);
        b.U8(ct);
        b.U8(cp);
        b.U16(g1.Length);
        b.U16(g2.Length);
        if (dxf is null)
        {
            b.U32(0);
            b.U16(0);
        }
        else
        {
            b.U32((uint)dxf.Length);
            b.Bytes(dxf);
        }

        b.Bytes(g1);
        b.Bytes(g2);
        b.U16(0); // fmlaActive: no activity condition
        b.U8(r.StopIfTrue && ct is 1 or 2 or 5 ? 2 : 0);
        b.U16(r.Priority);
        b.U16(template);
        b.U8(16);
        b.Bytes(parms);
        b.Bytes(content.Span);
        return b.Span.ToArray();

        byte[]? Reject()
        {
            _loss.Report("loss.conditional-formats-extended", "conditional formats", 1, "A rule type or formula BIFF8 cannot express (unique and duplicate values, above and below average, some formulas) was not written.");
            return null;
        }
    }
}
