using System;
using System.Collections.Generic;
using System.Linq;

namespace RecursiveAI.OneOffice.Calc;

internal static partial class MathFunctions
{
    private static void RegisterAggregates(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("SUMIF", 2, 3, [ArgKind.Any, ArgKind.Value, ArgKind.Any], SumIf));
        r.Add(FunctionDefinition.Create("SUMIFS", 3, 255, Any, SumIfs, repeatLast: true));
        r.Add(FunctionDefinition.Create("SUBTOTAL", 2, 255, [ArgKind.Value, ArgKind.Any], Subtotal, repeatLast: true));
        r.Add(FunctionDefinition.Create("AGGREGATE", 3, 255, [ArgKind.Value, ArgKind.Value, ArgKind.Any], Aggregate, repeatLast: true));
    }

    // A criteria range must be one rectangle on one sheet; anything else (an array constant, a union, a 3-D block) is #VALUE!.
    private static CalcArray? ReadRange(Evaluator ev, in CalcValue arg, out CellArea area, out CalcValue error)
    {
        area = default;
        error = Fx.Err(CalcError.Value);
        if (arg.Kind != ValueKind.Reference)
        {
            return arg.IsError ? Fail(arg, out error) : null;
        }

        CalcReference rf = arg.Reference!;
        if (rf.AreaCount != 1 || rf.First.LastSheet is not null || rf.IsSpill)
        {
            return null;
        }

        area = rf.First;
        if (!ev.TryReadAreas(rf, out CalcArray[] blocks, out CalcError re))
        {
            error = Fx.Err(re);
            return null;
        }

        return blocks[0];

        static CalcArray? Fail(CalcValue v, out CalcValue e)
        {
            e = v;
            return null;
        }
    }

    // The cells to add: the sum range is moved so that its top-left cell stays and its size becomes the criteria range's size.
    private static CalcArray? ReadSumRange(Evaluator ev, in CalcValue arg, CellArea criteria, bool exactSize, out CalcValue error)
    {
        CalcArray? block = ReadRange(ev, arg, out CellArea sa, out error);
        if (block is null)
        {
            return null;
        }

        if (block.Rows == criteria.Rows && block.Columns == criteria.Columns)
        {
            return block;
        }

        if (exactSize)
        {
            error = Fx.Err(CalcError.Value);
            return null;
        }

        int r2 = Math.Min(CellArea.MaxRow, sa.Row1 + criteria.Rows - 1);
        int c2 = Math.Min(CellArea.MaxColumn, sa.Column1 + criteria.Columns - 1);
        var moved = new CalcReference([new CellArea(sa.Sheet, null, sa.Row1, sa.Column1, r2, c2)]);
        if (!ev.TryReadAreas(moved, out CalcArray[] blocks, out CalcError re))
        {
            error = Fx.Err(re);
            return null;
        }

        return blocks[0];
    }

    private static CalcValue SumIf(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcArray? range = ReadRange(ev, a[0], out CellArea area, out CalcValue e);
        if (range is null)
        {
            return e;
        }

        if (a[1].IsError)
        {
            return a[1];
        }

        CalcArray sums = range;
        if (!Fx.Omitted(a, 2))
        {
            CalcArray? s = ReadSumRange(ev, a[2], area, false, out e);
            if (s is null)
            {
                return e;
            }

            sums = s;
        }

        var crit = CalcCriteria.Parse(a[1]);
        double total = 0;
        for (int r = 0; r < range.Rows; r++)
        {
            for (int c = 0; c < range.Columns; c++)
            {
                if (!crit.Matches(range[r, c]))
                {
                    continue;
                }

                CalcValue v = r < sums.Rows && c < sums.Columns ? sums[r, c] : CalcValue.Blank;
                if (v.IsError)
                {
                    return v;
                }

                if (v.Kind == ValueKind.Number)
                {
                    total = Evaluator.AddSnapped(total, v.Number);
                }
            }
        }

        return Fx.Num(total);
    }

    private static CalcValue SumIfs(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (a.Length % 2 == 0)
        {
            return Fx.Err(CalcError.Value);
        }

        CalcArray? first = ReadRange(ev, a[1], out CellArea area, out CalcValue e);
        if (first is null)
        {
            return e;
        }

        CalcArray? sums = ReadSumRange(ev, a[0], area, true, out e);
        if (sums is null)
        {
            return e;
        }

        int pairs = (a.Length - 1) / 2;
        var ranges = new CalcArray[pairs];
        var critValues = new CalcValue[pairs];
        int rows = 1;
        int cols = 1;
        for (int i = 0; i < pairs; i++)
        {
            CalcArray? rg = i == 0 ? first : ReadRange(ev, a[1 + (2 * i)], out _, out e);
            if (rg is null)
            {
                return e;
            }

            if (rg.Rows != first.Rows || rg.Columns != first.Columns)
            {
                return Fx.Err(CalcError.Value);
            }

            CalcValue cv = ev.Dereference(a[2 + (2 * i)]);
            if (cv.IsError)
            {
                return cv;
            }

            if (cv.Array is { } ca)
            {
                rows = Math.Max(rows, ca.Rows);
                cols = Math.Max(cols, ca.Columns);
            }

            ranges[i] = rg;
            critValues[i] = cv;
        }

        // A criterion given as an array runs the whole sum once per element and returns the results as an array.
        if (rows == 1 && cols == 1 && critValues.All(v => v.Kind != ValueKind.Array))
        {
            return SumWhere(first, sums, ranges, critValues, 0, 0);
        }

        CalcValue[] cells = ev.NewCells(rows, cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                cells[(r * cols) + c] = SumWhere(first, sums, ranges, critValues, r, c);
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    private static CalcValue SumWhere(CalcArray first, CalcArray sums, CalcArray[] ranges, CalcValue[] critValues, int row, int col)
    {
        var crits = new CalcCriteria[ranges.Length];
        for (int i = 0; i < crits.Length; i++)
        {
            CalcValue cv = critValues[i].Array is { } ca ? ca.Broadcasting(row, col) : critValues[i];
            if (cv.IsError)
            {
                return cv;
            }

            crits[i] = CalcCriteria.Parse(cv);
        }

        double total = 0;
        for (int r = 0; r < first.Rows; r++)
        {
            for (int c = 0; c < first.Columns; c++)
            {
                bool all = true;
                for (int i = 0; i < crits.Length && all; i++)
                {
                    all = crits[i].Matches(ranges[i][r, c]);
                }

                if (!all)
                {
                    continue;
                }

                CalcValue v = sums[r, c];
                if (v.IsError)
                {
                    return v;
                }

                if (v.Kind == ValueKind.Number)
                {
                    total = Evaluator.AddSnapped(total, v.Number);
                }
            }
        }

        return Fx.Num(total);
    }

    // ---- SUBTOTAL and AGGREGATE ------------------------------------------------------------------------------------------------

    private readonly record struct Gathered(List<double> Numbers, int NonBlank, CalcValue? Error);

    private static Gathered Gather(Evaluator ev, ReadOnlySpan<CalcValue> args, bool skipHidden, bool skipFiltered, bool skipNested, bool skipErrors, bool errorsStop)
    {
        var nums = new List<double>();
        int nonBlank = 0;
        CalcValue? firstError = null;
        ICalcResolver res = ev.Resolver;
        for (int i = 0; i < args.Length; i++)
        {
            CalcValue arg = args[i];
            if (arg.Kind == ValueKind.Reference)
            {
                CalcReference rf = arg.Reference!;
                bool plain = rf.IsSpill;
                foreach (CellArea area in rf.Areas.ToArray())
                {
                    plain |= area.LastSheet is not null;
                }

                if (plain)
                {
                    if (!ev.TryReadAreas(rf, out CalcArray[] blocks, out CalcError re))
                    {
                        return new Gathered(nums, nonBlank, Fx.Err(re));
                    }

                    foreach (CalcArray b in blocks)
                    {
                        AddBlock(b, 0, 0, null);
                    }

                    continue;
                }

                foreach (CellArea area in rf.Areas.ToArray())
                {
                    if (!ev.TryReadAreas(new CalcReference([area]), out CalcArray[] blocks, out CalcError re))
                    {
                        return new Gathered(nums, nonBlank, Fx.Err(re));
                    }

                    AddBlock(blocks[0], area.Row1, area.Column1, area.Sheet ?? ev.Origin.Sheet);
                }
            }
            else if (arg.Kind == ValueKind.Array)
            {
                AddBlock(arg.Array!, 0, 0, null);
            }
            else if (arg.IsError)
            {
                if (!skipErrors)
                {
                    firstError ??= arg;
                }
            }
            else if (!arg.IsMissing)
            {
                CalcError ce = CalcConvert.ToNumber(arg, out double d);
                if (ce != CalcError.None)
                {
                    firstError ??= Fx.Err(ce);
                }
                else
                {
                    nums.Add(d);
                    nonBlank++;
                }
            }
        }

        return new Gathered(nums, nonBlank, firstError);

        void AddBlock(CalcArray b, int row0, int col0, string? sheet)
        {
            for (int r = 0; r < b.Rows; r++)
            {
                if (sheet is not null && (skipHidden || skipFiltered))
                {
                    RowVisibility vis = res.GetRowVisibility(sheet, row0 + r);
                    if ((vis == RowVisibility.Filtered && (skipHidden || skipFiltered)) || (vis == RowVisibility.Hidden && skipHidden))
                    {
                        continue;
                    }
                }

                for (int c = 0; c < b.Columns; c++)
                {
                    if (sheet is not null && skipNested && res.IsSubtotalCell(sheet, row0 + r, col0 + c))
                    {
                        continue;
                    }

                    CalcValue v = b[r, c];
                    if (v.IsError)
                    {
                        if (skipErrors)
                        {
                            continue;
                        }

                        nonBlank++;
                        if (errorsStop)
                        {
                            firstError ??= v;
                        }

                        continue;
                    }

                    if (v.Kind == ValueKind.Number)
                    {
                        nums.Add(v.Number);
                    }

                    if (!v.IsBlank)
                    {
                        nonBlank++;
                    }
                }
            }
        }
    }

    private static CalcValue Subtotal(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryInt(a[0], out double f, out CalcValue e))
        {
            return e;
        }

        int fn = (int)f;
        bool hidden = fn >= 101;
        int basic = hidden ? fn - 100 : fn;
        if (basic is < 1 or > 11)
        {
            return Fx.Err(CalcError.Value);
        }

        // Functions 1-11 skip filtered rows but keep rows hidden by hand; 101-111 skip both. Cells that are themselves SUBTOTAL results are always skipped.
        Gathered g = Gather(ev, a[1..], hidden, true, true, false, basic is not (2 or 3));
        if (g.Error is { } err)
        {
            return err;
        }

        return Reduce(basic, g.Numbers, g.NonBlank, 0);
    }

    private static CalcValue Aggregate(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryInt(a[0], out double f, out CalcValue e) || !Fx.TryInt(a[1], out double o, out e))
        {
            return e;
        }

        int fn = (int)f;
        int opt = (int)o;
        if (fn is < 1 or > 19 || opt is < 0 or > 7)
        {
            return Fx.Err(CalcError.Value);
        }

        bool needsK = fn >= 14;
        if (needsK && a.Length != 4)
        {
            return Fx.Err(CalcError.Value);
        }

        if (!needsK && a.Length < 3)
        {
            return Fx.Err(CalcError.Value);
        }

        double k = 0;
        if (needsK)
        {
            CalcValue kv = a[3].Kind == ValueKind.Reference ? ev.Dereference(a[3]) : a[3];
            if (kv.Kind == ValueKind.Array)
            {
                kv = kv.Array![0, 0];
            }

            if (kv.IsError)
            {
                return kv;
            }

            if (!Fx.TryNum(kv, out k, out e))
            {
                return e;
            }
        }

        bool skipNested = opt <= 3;
        bool skipHidden = opt is 1 or 3 or 5 or 7;
        bool skipErrors = opt is 2 or 3 or 6 or 7;
        Gathered g = Gather(ev, needsK ? a.Slice(2, 1) : a[2..], skipHidden, skipHidden, skipNested, skipErrors, fn is not (2 or 3));
        if (g.Error is { } err)
        {
            return err;
        }

        return Reduce(fn, g.Numbers, g.NonBlank, k);
    }

    private static CalcValue Reduce(int fn, List<double> v, int nonBlank, double k)
    {
        int n = v.Count;
        double sum = 0;
        foreach (double d in v)
        {
            sum = Evaluator.AddSnapped(sum, d);
        }

        switch (fn)
        {
            case 1:
                return n == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(sum / n);
            case 2:
                return CalcValue.FromNumber(n);
            case 3:
                return CalcValue.FromNumber(nonBlank);
            case 4:
                return CalcValue.FromNumber(n == 0 ? 0 : Max(v));
            case 5:
                return CalcValue.FromNumber(n == 0 ? 0 : Min(v));
            case 6:
                {
                    if (n == 0)
                    {
                        return CalcValue.FromNumber(0);
                    }

                    double p = 1;
                    foreach (double d in v)
                    {
                        p *= d;
                    }

                    return Fx.Num(p);
                }

            case 7:
            case 10:
                return n < 2 ? Fx.Err(CalcError.Div0) : Variance(v, sum, n - 1, fn == 7);
            case 8:
            case 11:
                return n < 1 ? Fx.Err(CalcError.Div0) : Variance(v, sum, n, fn == 8);
            case 9:
                return Fx.Num(sum);
            case 12:
                {
                    if (n == 0)
                    {
                        return Fx.Err(CalcError.Num);
                    }

                    double[] s = Sorted(v);
                    return Fx.Num(n % 2 == 1 ? s[n / 2] : (s[(n / 2) - 1] + s[n / 2]) / 2);
                }

            case 13:
                return Mode(v);
            case 14:
            case 15:
                {
                    int kk = (int)Math.Ceiling(k);
                    if (kk < 1 || kk > n)
                    {
                        return Fx.Err(CalcError.Num);
                    }

                    double[] s = Sorted(v);
                    return CalcValue.FromNumber(fn == 14 ? s[n - kk] : s[kk - 1]);
                }

            case 16:
            case 18:
                return Percentile(v, k, fn == 18);
            default:
                {
                    // QUARTILE: the quartile number is truncated and stands for the percentile quart / 4.
                    double quart = Math.Truncate(k);
                    bool exc = fn == 19;
                    if (quart < (exc ? 1 : 0) || quart > (exc ? 3 : 4))
                    {
                        return Fx.Err(CalcError.Num);
                    }

                    return Percentile(v, quart / 4, exc);
                }
        }
    }

    private static double Max(List<double> v)
    {
        double m = double.NegativeInfinity;
        foreach (double d in v)
        {
            m = Math.Max(m, d);
        }

        return m;
    }

    private static double Min(List<double> v)
    {
        double m = double.PositiveInfinity;
        foreach (double d in v)
        {
            m = Math.Min(m, d);
        }

        return m;
    }

    private static double[] Sorted(List<double> v)
    {
        double[] s = [.. v];
        Array.Sort(s);
        return s;
    }

    private static CalcValue Variance(List<double> v, double sum, int divisor, bool sqrt)
    {
        double mean = sum / v.Count;
        double ss = 0;
        foreach (double d in v)
        {
            ss += (d - mean) * (d - mean);
        }

        double var = ss / divisor;
        return Fx.Num(sqrt ? Math.Sqrt(var) : var);
    }

    private static CalcValue Mode(List<double> v)
    {
        double best = 0;
        int bestCount = 1;
        for (int i = 0; i < v.Count; i++)
        {
            int c = 0;
            for (int j = 0; j < v.Count; j++)
            {
                if (v[j] == v[i])
                {
                    c++;
                }
            }

            if (c > bestCount)
            {
                bestCount = c;
                best = v[i];
            }
        }

        return bestCount > 1 ? CalcValue.FromNumber(best) : Fx.Err(CalcError.NA);
    }

    // PERCENTILE.INC interpolates at k * (n - 1); PERCENTILE.EXC at k * (n + 1) - 1 and needs 1/(n+1) <= k <= n/(n+1).
    private static CalcValue Percentile(List<double> v, double k, bool exclusive)
    {
        int n = v.Count;
        if (n == 0 || k < 0 || k > 1)
        {
            return Fx.Err(CalcError.Num);
        }

        double[] s = Sorted(v);
        double pos;
        if (exclusive)
        {
            pos = (k * (n + 1)) - 1;
            if (pos < 0 || pos > n - 1)
            {
                return Fx.Err(CalcError.Num);
            }
        }
        else
        {
            pos = k * (n - 1);
        }

        int lo = (int)Math.Floor(pos);
        double frac = pos - lo;
        if (lo + 1 >= n)
        {
            return CalcValue.FromNumber(s[n - 1]);
        }

        return Fx.Num(s[lo] + (frac * (s[lo + 1] - s[lo])));
    }
}
