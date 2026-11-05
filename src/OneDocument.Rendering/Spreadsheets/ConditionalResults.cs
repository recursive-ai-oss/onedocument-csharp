using System.Globalization;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>A data bar as it is drawn inside a cell: fractions of the cell's inner width.</summary>
internal sealed class DataBarVisual
{
    public double From;       // left end, 0..1
    public double To;         // right end, 0..1
    public uint Fill;
    public bool Gradient = true;
    public uint? Border;
    public double Axis = -1;  // x of the axis as a fraction, or -1 for none
    public uint AxisColor;
}

/// <summary>An icon of an icon set: which set and which level.</summary>
internal sealed class IconVisual
{
    public string Set = "3TrafficLights1";
    public int Level;
    public int Levels = 3;
}

/// <summary>What conditional formats change about one cell.</summary>
internal sealed class CfCell
{
    public bool? Bold;
    public bool? Italic;
    public bool? Strike;
    public FontUnderline? Underline;
    public uint? FontColor;
    public uint? Fill;
    public CellBorder? Border;
    public uint[]? BorderColors;
    public string? NumberFormat;
    public uint? ScaleColor;
    public DataBarVisual? Bar;
    public IconVisual? Icon;
    public bool HideValue;
    public bool Stopped;
}

/// <summary>The conditional-format results for the cells a sheet prints, computed from the cells' stored values; nothing is recalculated.</summary>
internal sealed class ConditionalResults
{
    private const long MaxEvaluations = 3_000_000;

    private readonly Dictionary<long, CfCell> _cells = [];

    public CfCell? Get(int row, int col) => _cells.GetValueOrDefault(SheetPlan.Key(row, col));

    private CfCell At(int row, int col)
    {
        long k = SheetPlan.Key(row, col);
        if (!_cells.TryGetValue(k, out CfCell? c))
        {
            _cells[k] = c = new CfCell();
        }

        return c;
    }

    private sealed class Entry(ConditionalFormat group, ConditionalFormatRule rule)
    {
        public ConditionalFormat Group = group;
        public ConditionalFormatRule Rule = rule;
        public List<CellRange> Ranges = [];
    }

    public static ConditionalResults Compute(RenderContext ctx, SheetPlan plan)
    {
        var result = new ConditionalResults();
        Worksheet ws = plan.Sheet;
        SheetGeometry geo = plan.Geo;
        var entries = new List<Entry>();
        var extendedBars = new Dictionary<string, ConditionalFormatRule>(StringComparer.OrdinalIgnoreCase);
        foreach (ConditionalFormat group in ws.ConditionalFormats)
        {
            foreach (ConditionalFormatRule rule in group.Rules)
            {
                if (group.IsExtended && rule.Markup.GetAttribute("type") == "dataBar")
                {
                    if (rule.Markup.GetAttribute("id") is { } id)
                    {
                        extendedBars[id] = rule;
                    }

                    continue;
                }

                if (rule.Type == ConditionalRuleType.Unknown)
                {
                    continue;
                }

                entries.Add(new Entry(group, rule) { Ranges = [.. group.Ranges] });
            }
        }

        entries.Sort((a, b) => a.Rule.Priority.CompareTo(b.Rule.Priority));
        long evaluations = 0;
        foreach (Entry e in entries)
        {
            ConditionalFormatRule rule = e.Rule;
            if (e.Ranges.Count == 0)
            {
                continue;
            }

            CellAddress origin = e.Ranges[0].TopLeft;
            Stats? stats = NeedsStats(rule.Type) ? Stats.Collect(ws, e.Ranges) : null;
            foreach (CellRange range in e.Ranges)
            {
                int r1 = Math.Max(range.TopLeft.Row, geo.Row0);
                int r2 = Math.Min(range.BottomRight.Row, geo.Row1);
                int c1 = Math.Max(range.TopLeft.Column, geo.Col0);
                int c2 = Math.Min(range.BottomRight.Column, geo.Col1);
                for (int r = r1; r <= r2; r++)
                {
                    for (int c = c1; c <= c2; c++)
                    {
                        if (++evaluations > MaxEvaluations)
                        {
                            ctx.Note("sheet-layout.conditional-limit", "Conditional formats past " + MaxEvaluations.ToString(CultureInfo.InvariantCulture) + " cells are not evaluated.", ws.Name);
                            return result;
                        }

                        CfCell? existing = result.Get(r, c);
                        if (existing is { Stopped: true })
                        {
                            continue;
                        }

                        ws.Store.TryGetCell(r, c, out CellValue value, out _);
                        result.Apply(ctx, plan, e, stats, extendedBars, origin, r, c, value, existing);
                    }
                }
            }
        }

        return result;
    }

    private static bool NeedsStats(ConditionalRuleType t) => t is ConditionalRuleType.ColorScale or ConditionalRuleType.DataBar or ConditionalRuleType.IconSet
        or ConditionalRuleType.Top10 or ConditionalRuleType.AboveAverage or ConditionalRuleType.UniqueValues or ConditionalRuleType.DuplicateValues;

    private sealed class Stats
    {
        public List<double> Numbers = [];
        public double[] Sorted = [];
        public double Min;
        public double Max;
        public double Average;
        public double StdDev;
        public Dictionary<string, int> Counts = new(StringComparer.OrdinalIgnoreCase);

        public static Stats Collect(Worksheet ws, List<CellRange> ranges)
        {
            var s = new Stats();
            CellStore store = ws.Store;
            foreach (CellRange range in ranges)
            {
                // Only rows that hold cells are visited, so a range of a million empty rows costs nothing.
                foreach (CellRowView row in store.EnumerateRows())
                {
                    if (row.Row < range.TopLeft.Row || row.Row > range.BottomRight.Row)
                    {
                        continue;
                    }

                    for (int i = 0; i < row.Count; i++)
                    {
                        int col = row.ColumnAt(i);
                        if (col < range.TopLeft.Column || col > range.BottomRight.Column)
                        {
                            continue;
                        }

                        CellValue v = row.ValueAt(i);
                        if (v.Kind == CellValueKind.Number)
                        {
                            s.Numbers.Add(v.Number);
                        }

                        if (v.Kind != CellValueKind.Empty)
                        {
                            string key = v.Kind + ":" + ws.GetText(v);
                            s.Counts[key] = s.Counts.GetValueOrDefault(key) + 1;
                        }
                    }
                }
            }

            s.Sorted = [.. s.Numbers.Order()];
            if (s.Sorted.Length > 0)
            {
                s.Min = s.Sorted[0];
                s.Max = s.Sorted[^1];
                s.Average = s.Numbers.Average();
                if (s.Sorted.Length > 1)
                {
                    double ss = s.Numbers.Sum(x => (x - s.Average) * (x - s.Average));
                    s.StdDev = Math.Sqrt(ss / (s.Sorted.Length - 1));
                }
            }

            return s;
        }

        // PERCENTILE.INC.
        public double Percentile(double p)
        {
            if (Sorted.Length == 0)
            {
                return 0;
            }

            double rank = Math.Clamp(p, 0, 1) * (Sorted.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = Math.Min(lo + 1, Sorted.Length - 1);
            return Sorted[lo] + ((rank - lo) * (Sorted[hi] - Sorted[lo]));
        }
    }

    private void Apply(RenderContext ctx, SheetPlan plan, Entry e, Stats? stats, Dictionary<string, ConditionalFormatRule> bars, CellAddress origin, int r, int c, CellValue value, CfCell? existing)
    {
        ConditionalFormatRule rule = e.Rule;
        Worksheet ws = plan.Sheet;
        bool hit;
        switch (rule.Type)
        {
            case ConditionalRuleType.Expression:
                hit = rule.Formulas.Count > 0 && Truthy(Eval(ws, rule.Formulas[0], r, c, origin));
                break;
            case ConditionalRuleType.CellIs:
                hit = CellIs(ws, rule, r, c, origin, value);
                break;
            case ConditionalRuleType.ContainsText:
                hit = rule.Text is { Length: > 0 } t1 && ws.GetText(value).Contains(t1, StringComparison.OrdinalIgnoreCase);
                break;
            case ConditionalRuleType.NotContainsText:
                hit = rule.Text is { Length: > 0 } t2 && !ws.GetText(value).Contains(t2, StringComparison.OrdinalIgnoreCase);
                break;
            case ConditionalRuleType.BeginsWith:
                hit = rule.Text is { Length: > 0 } t3 && ws.GetText(value).StartsWith(t3, StringComparison.OrdinalIgnoreCase);
                break;
            case ConditionalRuleType.EndsWith:
                hit = rule.Text is { Length: > 0 } t4 && ws.GetText(value).EndsWith(t4, StringComparison.OrdinalIgnoreCase);
                break;
            case ConditionalRuleType.ContainsBlanks:
                hit = value.Kind == CellValueKind.Empty || (value.Kind == CellValueKind.Text && ws.GetText(value).Trim().Length == 0);
                break;
            case ConditionalRuleType.NotContainsBlanks:
                hit = !(value.Kind == CellValueKind.Empty || (value.Kind == CellValueKind.Text && ws.GetText(value).Trim().Length == 0));
                break;
            case ConditionalRuleType.ContainsErrors:
                hit = value.Kind == CellValueKind.Error;
                break;
            case ConditionalRuleType.NotContainsErrors:
                hit = value.Kind != CellValueKind.Error;
                break;
            case ConditionalRuleType.DuplicateValues:
                hit = value.Kind != CellValueKind.Empty && stats!.Counts.GetValueOrDefault(value.Kind + ":" + ws.GetText(value)) > 1;
                break;
            case ConditionalRuleType.UniqueValues:
                hit = value.Kind != CellValueKind.Empty && stats!.Counts.GetValueOrDefault(value.Kind + ":" + ws.GetText(value)) == 1;
                break;
            case ConditionalRuleType.Top10:
                hit = value.Kind == CellValueKind.Number && Top10(rule, stats!, value.Number);
                break;
            case ConditionalRuleType.AboveAverage:
                hit = value.Kind == CellValueKind.Number && Above(rule, stats!, value.Number);
                break;
            case ConditionalRuleType.TimePeriod:
                hit = value.Kind == CellValueKind.Number && TimePeriod(ctx, ws, rule.TimePeriod, value.Number);
                break;
            case ConditionalRuleType.ColorScale:
                if (value.Kind == CellValueKind.Number && rule.ColorScale is { } cs)
                {
                    At(r, c).ScaleColor ??= ScaleColor(ctx, ws, cs, stats!, origin, value.Number);
                }

                return;
            case ConditionalRuleType.DataBar:
                if (value.Kind == CellValueKind.Number && rule.DataBar is { } db)
                {
                    CfCell cell = At(r, c);
                    cell.Bar ??= Bar(ctx, ws, rule, db, bars, stats!, origin, value.Number);
                }

                return;
            case ConditionalRuleType.IconSet:
                if (value.Kind == CellValueKind.Number && rule.IconSet is { } ic)
                {
                    CfCell cell = At(r, c);
                    if (cell.Icon is null)
                    {
                        cell.Icon = Icon(ws, ic, stats!, origin, value.Number);
                        cell.HideValue |= !ic.ShowValue;
                    }
                }

                return;
            default:
                return;
        }

        if (!hit)
        {
            return;
        }

        CfCell target = existing ?? At(r, c);
        if (rule.DxfId is { } dxfId && dxfId >= 0 && dxfId < ws.Workbook.Styles.DifferentialFormatCount)
        {
            ApplyDxf(ctx, ws.Workbook.Styles.GetDifferentialFormat(dxfId), target);
        }

        if (rule.StopIfTrue)
        {
            target.Stopped = true;
        }
    }

    private static void ApplyDxf(RenderContext ctx, DifferentialFormat dxf, CfCell t)
    {
        if (dxf.Font is { } f)
        {
            if (f.Bold)
            {
                t.Bold ??= true;
            }

            if (f.Italic)
            {
                t.Italic ??= true;
            }

            if (f.Strike)
            {
                t.Strike ??= true;
            }

            if (f.Underline != FontUnderline.None)
            {
                t.Underline ??= f.Underline;
            }

            if (f.Color.Kind is not (ColorKind.None or ColorKind.Auto))
            {
                t.FontColor ??= ctx.Styles.Rgb(f.Color, 0);
            }
        }

        if (dxf.Fill is { } fill && t.Fill is null)
        {
            // A differential fill keeps the colour that shows in bgColor when it is solid (the pattern's background), which is how Excel writes it.
            StyleColor col = fill.Pattern is FillPattern.None or FillPattern.Solid && fill.Background.Kind is not (ColorKind.None or ColorKind.Auto) ? fill.Background : fill.Foreground;
            if (fill.Pattern != FillPattern.None || col.Kind is not (ColorKind.None or ColorKind.Auto))
            {
                t.Fill = ctx.Styles.Rgb(col, 0xFFFFFF);
            }
        }

        if (dxf.Border is { } b && t.Border is null)
        {
            t.Border = b;
            t.BorderColors = [ctx.Styles.Rgb(b.Left.Color, 0), ctx.Styles.Rgb(b.Right.Color, 0), ctx.Styles.Rgb(b.Top.Color, 0), ctx.Styles.Rgb(b.Bottom.Color, 0), ctx.Styles.Rgb(b.Diagonal.Color, 0)];
        }

        if (dxf.NumberFormatCode is { Length: > 0 } nf)
        {
            t.NumberFormat ??= nf;
        }
    }

    private static CalcValue Eval(Worksheet ws, string formula, int r, int c, CellAddress origin)
    {
        try
        {
            CalcValue v = ws.Workbook.EvaluateFormula(formula.TrimStart('='), ws, r, c, origin.Row, origin.Column);
            if (v.Kind == ValueKind.Array && v.Array is { Count: > 0 } arr)
            {
                return arr[0, 0];
            }

            return v;
        }
        catch (OfficeLimitException)
        {
            return CalcValue.FromError(CalcError.Value);
        }
    }

    private static bool Truthy(CalcValue v) => v.Kind switch
    {
        ValueKind.Boolean => v.Boolean,
        ValueKind.Number => v.Number != 0,
        _ => false,
    };

    private static CalcValue ToCalc(Worksheet ws, CellValue v) => v.Kind switch
    {
        CellValueKind.Number => CalcValue.FromNumber(v.Number),
        CellValueKind.Boolean => CalcValue.FromBoolean(v.Boolean),
        CellValueKind.Text => CalcValue.FromText(ws.GetText(v)),
        CellValueKind.Error => CalcValue.FromError(CalcError.Value),
        _ => CalcValue.FromNumber(0),
    };

    // Excel orders numbers before text before booleans when it compares values of different kinds.
    private static int Rank(CalcValue v) => v.Kind switch { ValueKind.Text => 1, ValueKind.Boolean => 2, _ => 0 };

    private static int Compare(CalcValue a, CalcValue b)
    {
        int ra = Rank(a);
        int rb = Rank(b);
        if (ra != rb)
        {
            return ra.CompareTo(rb);
        }

        return ra switch
        {
            1 => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase),
            2 => a.Boolean.CompareTo(b.Boolean),
            _ => a.Number.CompareTo(b.Number),
        };
    }

    // ECMA-376 Part 1 §18.18.15 (ST_ConditionalFormattingOperator).
    private static bool CellIs(Worksheet ws, ConditionalFormatRule rule, int r, int c, CellAddress origin, CellValue value)
    {
        IReadOnlyList<string> f = rule.Formulas;
        if (f.Count == 0 || value.Kind == CellValueKind.Error)
        {
            return false;
        }

        CalcValue cell = ToCalc(ws, value);
        CalcValue a = Eval(ws, f[0], r, c, origin);
        if (a.IsError)
        {
            return false;
        }

        switch (rule.Operator)
        {
            case "between" or "notBetween":
                {
                    if (f.Count < 2)
                    {
                        return false;
                    }

                    CalcValue b = Eval(ws, f[1], r, c, origin);
                    bool inside = Compare(cell, a) >= 0 && Compare(cell, b) <= 0;
                    return rule.Operator == "between" ? inside : !inside;
                }

            case "equal": return Compare(cell, a) == 0;
            case "notEqual": return Compare(cell, a) != 0;
            case "greaterThan": return Compare(cell, a) > 0;
            case "greaterThanOrEqual": return Compare(cell, a) >= 0;
            case "lessThan": return Compare(cell, a) < 0;
            case "lessThanOrEqual": return Compare(cell, a) <= 0;
            case "containsText": return ws.GetText(value).Contains(a.Text, StringComparison.OrdinalIgnoreCase);
            case "notContains": return !ws.GetText(value).Contains(a.Text, StringComparison.OrdinalIgnoreCase);
            case "beginsWith": return ws.GetText(value).StartsWith(a.Text, StringComparison.OrdinalIgnoreCase);
            case "endsWith": return ws.GetText(value).EndsWith(a.Text, StringComparison.OrdinalIgnoreCase);
            default: return false;
        }
    }

    private static bool Top10(ConditionalFormatRule rule, Stats s, double v)
    {
        int n = s.Sorted.Length;
        if (n == 0)
        {
            return false;
        }

        int k = rule.Percent ? (int)Math.Floor(n * Math.Clamp(rule.Rank, 0, 100) / 100.0) : rule.Rank;
        k = Math.Clamp(k, 1, n);
        // Ties with the k-th value are included.
        return rule.Bottom ? v <= s.Sorted[k - 1] : v >= s.Sorted[n - k];
    }

    private static bool Above(ConditionalFormatRule rule, Stats s, double v)
    {
        if (s.Sorted.Length == 0)
        {
            return false;
        }

        int sd = X.Int(rule.Markup.GetAttribute("stdDev"), 0);
        bool equal = X.Flag(rule.Markup.GetAttribute("equalAverage"));
        bool above = rule.AboveAverage;
        double bound = s.Average + ((above ? 1 : -1) * sd * s.StdDev);
        return above ? (equal ? v >= bound : v > bound) : (equal ? v <= bound : v < bound);
    }

    private static bool TimePeriod(RenderContext ctx, Worksheet ws, string? period, double serial)
    {
        if (!ExcelDate.TryToDateTime(serial, ws.Workbook.DateSystem, out DateTime d))
        {
            return false;
        }

        DateTime today = ctx.Options.Clock.GetLocalNow().Date;
        DateTime day = d.Date;
        // Weeks run Sunday to Saturday.
        DateTime weekStart = today.AddDays(-(int)today.DayOfWeek);
        switch (period)
        {
            case "today": return day == today;
            case "yesterday": return day == today.AddDays(-1);
            case "tomorrow": return day == today.AddDays(1);
            case "last7Days": return day <= today && day > today.AddDays(-7);
            case "thisWeek": return day >= weekStart && day < weekStart.AddDays(7);
            case "lastWeek": return day >= weekStart.AddDays(-7) && day < weekStart;
            case "nextWeek": return day >= weekStart.AddDays(7) && day < weekStart.AddDays(14);
            case "thisMonth": return day.Year == today.Year && day.Month == today.Month;
            case "lastMonth": { DateTime m = today.AddMonths(-1); return day.Year == m.Year && day.Month == m.Month; }
            case "nextMonth": { DateTime m = today.AddMonths(1); return day.Year == m.Year && day.Month == m.Month; }
            default: return false;
        }
    }

    // The value a colour-scale, data-bar or icon-set point stands for.
    private static double PointValue(Worksheet ws, ConditionalValuePoint p, Stats s, CellAddress origin, double fallback)
    {
        switch (p.Type)
        {
            case "min" or "autoMin": return s.Min;
            case "max" or "autoMax": return s.Max;
            case "num": return X.Double(p.Value, fallback);
            case "percent": return s.Min + (X.Double(p.Value, 0) / 100.0 * (s.Max - s.Min));
            case "percentile": return s.Percentile(X.Double(p.Value, 0) / 100.0);
            case "formula" when p.Value is { } f:
                {
                    CalcValue v = Eval(ws, f, origin.Row, origin.Column, origin);
                    return v.Kind == ValueKind.Number ? v.Number : fallback;
                }

            default: return fallback;
        }
    }

    private static uint? ScaleColor(RenderContext ctx, Worksheet ws, ColorScaleInfo cs, Stats s, CellAddress origin, double v)
    {
        int n = Math.Min(cs.Points.Count, cs.Colors.Count);
        if (n < 2)
        {
            return null;
        }

        var xs = new double[n];
        var cols = new uint[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = PointValue(ws, cs.Points[i], s, origin, i == 0 ? s.Min : s.Max);
            cols[i] = ctx.Styles.Rgb(cs.Colors[i], 0xFFFFFF);
        }

        if (v <= xs[0])
        {
            return cols[0];
        }

        for (int i = 1; i < n; i++)
        {
            if (v <= xs[i])
            {
                double span = xs[i] - xs[i - 1];
                double t = span <= 0 ? 1 : (v - xs[i - 1]) / span;
                return StyleCache.Blend(cols[i - 1], cols[i], t);
            }
        }

        return cols[n - 1];
    }

    private static DataBarVisual? Bar(RenderContext ctx, Worksheet ws, ConditionalFormatRule rule, DataBarInfo db, Dictionary<string, ConditionalFormatRule> ext, Stats s, CellAddress origin, double v)
    {
        double lo = db.Points.Count > 0 ? PointValue(ws, db.Points[0], s, origin, s.Min) : s.Min;
        double hi = db.Points.Count > 1 ? PointValue(ws, db.Points[1], s, origin, s.Max) : s.Max;
        uint fill = ctx.Styles.Rgb(db.Color, 0x638EC6);

        // Excel 2010 and later write the extra settings (negative bars, axis, solid fill) in an x14 extension linked by id.
        MarkupNode x14 = default;
        foreach (MarkupNode ex in X.Descendants(rule.Markup))
        {
            if (ex.LocalNameString == "id" && ex.InnerText is { Length: > 0 } id && ext.TryGetValue(id.Trim(), out ConditionalFormatRule? er))
            {
                x14 = X.Child(er.Markup, "dataBar");
            }
        }

        bool isExt = !x14.IsNull;
        double minLen = (isExt ? X.Double(x14.GetAttribute("minLength"), 0) : db.MinLength) / 100.0;
        double maxLen = (isExt ? X.Double(x14.GetAttribute("maxLength"), 100) : db.MaxLength) / 100.0;
        var bar = new DataBarVisual { Fill = fill, Gradient = !isExt || x14.GetAttribute("gradient") is not ("0" or "false") };
        string axisPosition = isExt ? x14.GetAttribute("axisPosition") ?? "automatic" : "automatic";
        bool negatives = isExt && lo < 0 && axisPosition != "none";
        if (isExt && X.Flag(x14.GetAttribute("border")) && X.Child(x14, "borderColor") is { IsNull: false } bc)
        {
            bar.Border = ctx.Styles.Rgb(CellContents.ReadColor(bc), fill);
        }

        if (hi <= lo)
        {
            bar.From = 0;
            bar.To = minLen;
            return bar;
        }

        if (!negatives)
        {
            double t = Math.Clamp((v - lo) / (hi - lo), 0, 1);
            bar.From = 0;
            bar.To = minLen + (t * (maxLen - minLen));
            return bar;
        }

        // The axis sits where zero falls between the lowest and highest value (or mid-cell when asked); positive bars grow right, negative left.
        double axis = axisPosition == "middle" ? 0.5 : Math.Clamp(-lo / (hi - lo), 0, 1);
        if (hi <= 0)
        {
            axis = 1;
        }

        bar.Axis = axis;
        bar.AxisColor = X.Child(x14, "axisColor") is { IsNull: false } ac ? ctx.Styles.Rgb(CellContents.ReadColor(ac), 0) : 0;
        if (v >= 0)
        {
            bar.From = axis;
            bar.To = axis + (hi > 0 ? Math.Clamp(v / hi, 0, 1) * (1 - axis) * maxLen : 0);
        }
        else
        {
            bool same = X.Flag(x14.GetAttribute("negativeBarColorSameAsPositive"));
            bar.Fill = same ? fill : X.Child(x14, "negativeFillColor") is { IsNull: false } nf ? ctx.Styles.Rgb(CellContents.ReadColor(nf), 0xFF0000) : 0xFF0000;
            bar.To = axis;
            bar.From = axis - (lo < 0 ? Math.Clamp(v / lo, 0, 1) * axis * maxLen : 0);
            if (bar.Border is not null)
            {
                bar.Border = X.Child(x14, "negativeBorderColor") is { IsNull: false } nb ? ctx.Styles.Rgb(CellContents.ReadColor(nb), 0xFF0000) : bar.Fill;
            }
        }

        return bar;
    }

    // ECMA-376 Part 1 §18.3.1.49 (iconSet): the first cfvo is the lowest level's start; the value gets the highest level whose threshold it reaches.
    private static IconVisual Icon(Worksheet ws, IconSetInfo set, Stats s, CellAddress origin, double v)
    {
        int n = Math.Max(2, set.Points.Count);
        int level = 0;
        for (int i = 1; i < set.Points.Count; i++)
        {
            ConditionalValuePoint p = set.Points[i];
            double threshold = PointValue(ws, p, s, origin, s.Max);
            if (v > threshold || (p.GreaterOrEqual && v >= threshold))
            {
                level = i;
            }
        }

        if (set.Reverse)
        {
            level = n - 1 - level;
        }

        return new IconVisual { Set = set.Name, Level = level, Levels = n };
    }
}
