using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>Counting, extremes, averages, ranking and the variance family (Microsoft's statistical function pages).</summary>
internal static class DescriptiveFunctions
{
    private static readonly ArgKind[] Any = [ArgKind.Any];
    private static readonly ArgKind[] AnyValue = [ArgKind.Any, ArgKind.Value];
    private static readonly ArgKind[] AnyValueValue = [ArgKind.Any, ArgKind.Value, ArgKind.Value];
    private static readonly ArgKind[] RankKinds = [ArgKind.Value, ArgKind.Any, ArgKind.Value];

    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("COUNT", 1, 255, Any, Count, repeatLast: true));
        r.Add(FunctionDefinition.Create("COUNTA", 1, 255, Any, CountA, repeatLast: true));
        r.Add(FunctionDefinition.Create("COUNTBLANK", 1, 1, Any, CountBlank));
        r.Add(FunctionDefinition.Create("COUNTIF", 2, 2, Any, (c, a) => RunIfs(c, a, 0, mask => Fx.Num(CountTrue(mask)))));
        r.Add(FunctionDefinition.Create("COUNTIFS", 2, 254, Any, (c, a) => a.Length % 2 != 0 ? Fx.Err(CalcError.Value) : RunIfs(c, a, 0, mask => Fx.Num(CountTrue(mask))), repeatLast: true));
        r.Add(FunctionDefinition.Create("AVERAGEIF", 2, 3, [ArgKind.Any, ArgKind.Value, ArgKind.Any], AverageIf));
        r.Add(FunctionDefinition.Create("AVERAGEIFS", 3, 255, Any, (c, a) => IfsOver(c, a, Aggregate.Average), repeatLast: true));
        r.Add(FunctionDefinition.Create("MAXIFS", 3, 255, Any, (c, a) => IfsOver(c, a, Aggregate.Max), repeatLast: true));
        r.Add(FunctionDefinition.Create("MINIFS", 3, 255, Any, (c, a) => IfsOver(c, a, Aggregate.Min), repeatLast: true));

        r.Add(FunctionDefinition.Create("AVERAGE", 1, 255, Any, (c, a) => Average(c, a, GatherMode.Numbers), repeatLast: true));
        r.Add(FunctionDefinition.Create("AVERAGEA", 1, 255, Any, (c, a) => Average(c, a, GatherMode.WithText), repeatLast: true));
        r.Add(FunctionDefinition.Create("MAX", 1, 255, Any, (c, a) => Extreme(c, a, GatherMode.Numbers, true), repeatLast: true));
        r.Add(FunctionDefinition.Create("MAXA", 1, 255, Any, (c, a) => Extreme(c, a, GatherMode.WithText, true), repeatLast: true));
        r.Add(FunctionDefinition.Create("MIN", 1, 255, Any, (c, a) => Extreme(c, a, GatherMode.Numbers, false), repeatLast: true));
        r.Add(FunctionDefinition.Create("MINA", 1, 255, Any, (c, a) => Extreme(c, a, GatherMode.WithText, false), repeatLast: true));
        r.Add(FunctionDefinition.Create("MEDIAN", 1, 255, Any, Median, repeatLast: true));
        r.Add(FunctionDefinition.Create("MODE", 1, 255, Any, ModeSingle, repeatLast: true));
        r.Add(FunctionDefinition.Create("MODE.SNGL", 1, 255, Any, ModeSingle, repeatLast: true));
        r.Add(FunctionDefinition.Create("MODE.MULT", 1, 255, Any, ModeMultiple, repeatLast: true));
        r.Add(FunctionDefinition.Create("LARGE", 2, 2, AnyValue, (c, a) => Nth(c, a, true)));
        r.Add(FunctionDefinition.Create("SMALL", 2, 2, AnyValue, (c, a) => Nth(c, a, false)));
        r.Add(FunctionDefinition.Create("RANK", 2, 3, RankKinds, (c, a) => Rank(c, a, false)));
        r.Add(FunctionDefinition.Create("RANK.EQ", 2, 3, RankKinds, (c, a) => Rank(c, a, false)));
        r.Add(FunctionDefinition.Create("RANK.AVG", 2, 3, RankKinds, (c, a) => Rank(c, a, true)));
        r.Add(FunctionDefinition.Create("PERCENTILE", 2, 2, AnyValue, (c, a) => Percentile(c, a, false)));
        r.Add(FunctionDefinition.Create("PERCENTILE.INC", 2, 2, AnyValue, (c, a) => Percentile(c, a, false)));
        r.Add(FunctionDefinition.Create("PERCENTILE.EXC", 2, 2, AnyValue, (c, a) => Percentile(c, a, true)));
        r.Add(FunctionDefinition.Create("QUARTILE", 2, 2, AnyValue, (c, a) => Quartile(c, a, false)));
        r.Add(FunctionDefinition.Create("QUARTILE.INC", 2, 2, AnyValue, (c, a) => Quartile(c, a, false)));
        r.Add(FunctionDefinition.Create("QUARTILE.EXC", 2, 2, AnyValue, (c, a) => Quartile(c, a, true)));
        r.Add(FunctionDefinition.Create("PERCENTRANK", 2, 3, AnyValueValue, (c, a) => PercentRank(c, a, false)));
        r.Add(FunctionDefinition.Create("PERCENTRANK.INC", 2, 3, AnyValueValue, (c, a) => PercentRank(c, a, false)));
        r.Add(FunctionDefinition.Create("PERCENTRANK.EXC", 2, 3, AnyValueValue, (c, a) => PercentRank(c, a, true)));

        Variance(r, "VAR", GatherMode.Numbers, true, false);
        Variance(r, "VAR.S", GatherMode.Numbers, true, false);
        Variance(r, "VARA", GatherMode.WithText, true, false);
        Variance(r, "VARP", GatherMode.Numbers, false, false);
        Variance(r, "VAR.P", GatherMode.Numbers, false, false);
        Variance(r, "VARPA", GatherMode.WithText, false, false);
        Variance(r, "STDEV", GatherMode.Numbers, true, true);
        Variance(r, "STDEV.S", GatherMode.Numbers, true, true);
        Variance(r, "STDEVA", GatherMode.WithText, true, true);
        Variance(r, "STDEVP", GatherMode.Numbers, false, true);
        Variance(r, "STDEV.P", GatherMode.Numbers, false, true);
        Variance(r, "STDEVPA", GatherMode.WithText, false, true);

        r.Add(FunctionDefinition.Create("DEVSQ", 1, 255, Any, DevSq, repeatLast: true));
        r.Add(FunctionDefinition.Create("AVEDEV", 1, 255, Any, AveDev, repeatLast: true));
        r.Add(FunctionDefinition.Create("GEOMEAN", 1, 255, Any, GeoMean, repeatLast: true));
        r.Add(FunctionDefinition.Create("HARMEAN", 1, 255, Any, HarMean, repeatLast: true));
        r.Add(FunctionDefinition.Create("TRIMMEAN", 2, 2, AnyValue, TrimMean));
        r.Add(FunctionDefinition.Create("SKEW", 1, 255, Any, (c, a) => Skew(c, a, false), repeatLast: true));
        r.Add(FunctionDefinition.Create("SKEW.P", 1, 255, Any, (c, a) => Skew(c, a, true), repeatLast: true));
        r.Add(FunctionDefinition.Create("KURT", 1, 255, Any, Kurt, repeatLast: true));
        r.Add(FunctionDefinition.Create("STANDARDIZE", 3, 3, [], Standardize));
        r.Add(FunctionDefinition.Create("FREQUENCY", 2, 2, [ArgKind.Any, ArgKind.Any], Frequency));
        r.Add(FunctionDefinition.Create("PROB", 3, 4, [ArgKind.Any, ArgKind.Any, ArgKind.Value, ArgKind.Value], Prob));
    }

    // ---- counting ----------------------------------------------------------------------------------------------------------------

    private static CalcValue Count(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var v = new List<double>();
        Stat.Gather(ev, a, v, GatherMode.Count);
        return Fx.Num(v.Count);
    }

    private static CalcValue CountA(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        long n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            CalcValue v = a[i];
            switch (v.Kind)
            {
                case ValueKind.Reference:
                    if (!ev.TryReadAreas(v.Reference!, out CalcArray[] blocks, out CalcError re))
                    {
                        return Fx.Err(re);
                    }

                    foreach (CalcArray b in blocks)
                    {
                        n += NonBlank(b);
                    }

                    break;
                case ValueKind.Array:
                    n += NonBlank(v.Array!);
                    break;
                default:
                    n++;
                    break;
            }
        }

        return Fx.Num(n);
    }

    private static long NonBlank(CalcArray a)
    {
        long n = 0;
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                if (!a[r, c].IsBlank)
                {
                    n++;
                }
            }
        }

        return n;
    }

    private static CalcValue CountBlank(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcValue v = a[0];
        if (v.Kind is not (ValueKind.Reference or ValueKind.Array))
        {
            return Fx.Err(CalcError.Value);
        }

        CalcArray? arr = Fx.AsArray(ev, v, out CalcValue e);
        if (arr is null)
        {
            return e;
        }

        long n = 0;
        for (int r = 0; r < arr.Rows; r++)
        {
            for (int c = 0; c < arr.Columns; c++)
            {
                CalcValue x = arr[r, c];
                if (x.IsBlank || (x.Kind == ValueKind.Text && x.Text.Length == 0))
                {
                    n++;
                }
            }
        }

        return Fx.Num(n);
    }

    // ---- conditional aggregates --------------------------------------------------------------------------------------------------

    private enum Aggregate
    {
        Average,
        Max,
        Min,
    }

    private static int CountTrue(bool[] mask)
    {
        int n = 0;
        foreach (bool b in mask)
        {
            if (b)
            {
                n++;
            }
        }

        return n;
    }

    // Reads a range argument; returns null with the error to hand back when it is not a range of cells.
    private static CalcArray? ReadRange(Evaluator ev, in CalcValue v, out CellArea area, out CalcValue error)
    {
        area = default;
        error = default;
        CalcValue d = v;
        if (d.Kind == ValueKind.Reference)
        {
            CalcReference rf = d.Reference!;
            if (rf.AreaCount == 1 && rf.First.LastSheet is null)
            {
                area = rf.First;
            }

            if (!ev.TryReadAreas(rf, out CalcArray[] blocks, out CalcError re))
            {
                error = Fx.Err(re);
                return null;
            }

            if (blocks.Length != 1)
            {
                error = Fx.Err(CalcError.Value);
                return null;
            }

            return blocks[0];
        }

        if (d.Kind == ValueKind.Array)
        {
            return d.Array;
        }

        error = d.IsError ? d : Fx.Err(CalcError.Value);
        return null;
    }

    /// <summary>
    /// The shared loop of the *IFS functions. Arguments from <paramref name="first"/> are range/criteria pairs, all ranges the same size
    /// (otherwise <c>#VALUE!</c>); the mask says which cells satisfy every criterion. A criterion given as an array runs the whole
    /// calculation once per element and returns an array.
    /// </summary>
    private static CalcValue RunIfs(Evaluator ev, ReadOnlySpan<CalcValue> a, int first, Func<bool[], CalcValue> reduce, CalcArray? shapeLike = null)
    {
        if (Fx.RangeShapesDiffer(a, first, 2))
        {
            return Fx.Err(CalcError.Value);
        }

        int pairs = (a.Length - first) / 2;
        var ranges = new CalcArray[pairs];
        var crits = new CalcValue[pairs];
        int rows = 1;
        int cols = 1;
        for (int i = 0; i < pairs; i++)
        {
            CalcArray? rg = ReadRange(ev, a[first + (2 * i)], out _, out CalcValue e);
            if (rg is null)
            {
                return e;
            }

            if ((i > 0 && (rg.Rows != ranges[0].Rows || rg.Columns != ranges[0].Columns)) ||
                (shapeLike is not null && (rg.Rows != shapeLike.Rows || rg.Columns != shapeLike.Columns)))
            {
                return Fx.Err(CalcError.Value);
            }

            CalcValue cv = ev.Dereference(a[first + (2 * i) + 1]);
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
            crits[i] = cv;
        }

        bool scalar = rows == 1 && cols == 1 && Array.TrueForAll(crits, v => v.Kind != ValueKind.Array);
        if (scalar)
        {
            return reduce(Mask(ranges, crits, 0, 0));
        }

        CalcValue[] cells = ev.NewCells(rows, cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                cells[(r * cols) + c] = reduce(Mask(ranges, crits, r, c));
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    private static bool[] Mask(CalcArray[] ranges, CalcValue[] crits, int row, int col)
    {
        CalcArray first = ranges[0];
        var mask = new bool[first.Rows * first.Columns];
        Array.Fill(mask, true);
        for (int i = 0; i < ranges.Length; i++)
        {
            CalcValue cv = crits[i].Kind == ValueKind.Array ? crits[i].Array!.Broadcasting(row, col) : crits[i];
            var crit = CalcCriteria.Parse(cv);
            CalcArray rg = ranges[i];
            for (int r = 0; r < rg.Rows; r++)
            {
                for (int c = 0; c < rg.Columns; c++)
                {
                    int k = (r * rg.Columns) + c;
                    if (mask[k] && !crit.Matches(rg[r, c]))
                    {
                        mask[k] = false;
                    }
                }
            }
        }

        return mask;
    }

    private static CalcValue Reduce(CalcArray values, bool[] mask, Aggregate how)
    {
        double sum = 0;
        double best = 0;
        long n = 0;
        for (int r = 0; r < values.Rows; r++)
        {
            for (int c = 0; c < values.Columns; c++)
            {
                if (!mask[(r * values.Columns) + c])
                {
                    continue;
                }

                CalcValue x = values[r, c];
                if (x.IsError)
                {
                    return x;
                }

                if (x.Kind != ValueKind.Number)
                {
                    continue;
                }

                sum = Evaluator.AddSnapped(sum, x.Number);
                best = n == 0 ? x.Number : how == Aggregate.Max ? Math.Max(best, x.Number) : Math.Min(best, x.Number);
                n++;
            }
        }

        return how switch
        {
            Aggregate.Average => n == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(sum / n),
            _ => Fx.Num(n == 0 ? 0 : best),
        };
    }

    private static CalcValue IfsOver(Evaluator ev, ReadOnlySpan<CalcValue> a, Aggregate how)
    {
        if (a.Length % 2 == 0)
        {
            return Fx.Err(CalcError.Value);
        }

        // The values range and every criteria range must have one size: the ranges are a[0], a[1], a[3], ...
        var shapes = new CalcValue[1 + ((a.Length - 1) / 2)];
        shapes[0] = a[0];
        for (int i = 1; i < shapes.Length; i++)
        {
            shapes[i] = a[(2 * i) - 1];
        }

        if (Fx.RangeShapesDiffer(shapes, 0, 1))
        {
            return Fx.Err(CalcError.Value);
        }

        CalcArray? values = ReadRange(ev, a[0], out _, out CalcValue e);
        if (values is null)
        {
            return e;
        }

        return RunIfs(ev, a, 1, mask => Reduce(values, mask, how), values);
    }

    // AVERAGEIF(range, criteria, [average_range]): average_range is resized to the shape of range from its own top-left cell.
    private static CalcValue AverageIf(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcArray? range = ReadRange(ev, a[0], out _, out CalcValue e);
        if (range is null)
        {
            return e;
        }

        CalcArray values = range;
        if (a.Length == 3 && !a[2].IsMissing)
        {
            CalcArray? avg = ReadRange(ev, a[2], out CellArea area, out e);
            if (avg is null)
            {
                return e;
            }

            if (avg.Rows != range.Rows || avg.Columns != range.Columns)
            {
                if (area.Row1 == 0)
                {
                    return Fx.Err(CalcError.Value);
                }

                var resized = new CellArea(area.Sheet, area.LastSheet, area.Row1, area.Column1,
                    Math.Min(CellArea.MaxRow, area.Row1 + range.Rows - 1), Math.Min(CellArea.MaxColumn, area.Column1 + range.Columns - 1));
                if (!ev.TryReadAreas(new CalcReference([resized]), out CalcArray[] blocks, out CalcError re))
                {
                    return Fx.Err(re);
                }

                avg = blocks[0];
                if (avg.Rows != range.Rows || avg.Columns != range.Columns)
                {
                    return Fx.Err(CalcError.Value);
                }
            }

            values = avg;
        }

        return RunIfs(ev, [a[0], a[1]], 0, mask => Reduce(values, mask, Aggregate.Average));
    }

    // ---- averages and extremes ---------------------------------------------------------------------------------------------------

    private static CalcValue Average(Evaluator ev, ReadOnlySpan<CalcValue> a, GatherMode mode)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, mode) is { } err)
        {
            return err;
        }

        if (v.Count == 0)
        {
            return Fx.Err(CalcError.Div0);
        }

        double sum = 0;
        foreach (double x in v)
        {
            sum = Evaluator.AddSnapped(sum, x);
        }

        return Fx.Num(sum / v.Count);
    }

    private static CalcValue Extreme(Evaluator ev, ReadOnlySpan<CalcValue> a, GatherMode mode, bool max)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, mode) is { } err)
        {
            return err;
        }

        if (v.Count == 0)
        {
            return Fx.Num(0);
        }

        double best = v[0];
        foreach (double x in v)
        {
            best = max ? Math.Max(best, x) : Math.Min(best, x);
        }

        return Fx.Num(best);
    }

    private static CalcValue Median(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, GatherMode.Numbers) is { } err)
        {
            return err;
        }

        if (v.Count == 0)
        {
            return Fx.Err(CalcError.Num);
        }

        double[] s = Stat.Sorted(v);
        int n = s.Length;
        return Fx.Num(n % 2 == 1 ? s[n / 2] : (s[(n / 2) - 1] + s[n / 2]) / 2);
    }

    private static List<double>? Modes(Evaluator ev, ReadOnlySpan<CalcValue> a, out CalcValue error)
    {
        error = default;
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, GatherMode.Numbers) is { } err)
        {
            error = err;
            return null;
        }

        var counts = new Dictionary<double, int>();
        var order = new List<double>();
        foreach (double x in v)
        {
            if (counts.TryGetValue(x, out int n))
            {
                counts[x] = n + 1;
            }
            else
            {
                counts[x] = 1;
                order.Add(x);
            }
        }

        int max = 0;
        foreach (int n in counts.Values)
        {
            max = Math.Max(max, n);
        }

        if (max < 2)
        {
            error = Fx.Err(CalcError.NA);
            return null;
        }

        var result = new List<double>();
        foreach (double x in order)
        {
            if (counts[x] == max)
            {
                result.Add(x);
            }
        }

        return result;
    }

    private static CalcValue ModeSingle(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        List<double>? m = Modes(ev, a, out CalcValue e);
        return m is null ? e : Fx.Num(m[0]);
    }

    private static CalcValue ModeMultiple(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        List<double>? m = Modes(ev, a, out CalcValue e);
        if (m is null)
        {
            return e;
        }

        CalcValue[] cells = ev.NewCells(m.Count, 1);
        for (int i = 0; i < m.Count; i++)
        {
            cells[i] = CalcValue.FromNumber(m[i]);
        }

        return CalcValue.FromArray(new CalcArray(m.Count, 1, cells));
    }

    // LARGE and SMALL round a fractional k up (Microsoft's pages do not say; open until the grid answers).
    private static CalcValue Nth(Evaluator ev, ReadOnlySpan<CalcValue> a, bool large)
    {
        if (Stat.Sample(ev, a[0], out List<double> v) is { } err)
        {
            return err;
        }

        if (!Fx.TryNum(a[1], out double k, out CalcValue e))
        {
            return e;
        }

        k = Math.Ceiling(k);
        if (v.Count == 0 || k < 1 || k > v.Count)
        {
            return Fx.Err(CalcError.Num);
        }

        double[] s = Stat.Sorted(v);
        return Fx.Num(large ? s[s.Length - (int)k] : s[(int)k - 1]);
    }

    private static CalcValue Rank(Evaluator ev, ReadOnlySpan<CalcValue> a, bool average)
    {
        // RANK(number, ref, order) and RANK.EQ/AVG(number, ref, order): the number is a value, the reference is walked for numbers.
        if (!Fx.TryNum(a[0], out double x, out CalcValue e))
        {
            return e;
        }

        if (a[1].Kind is not (ValueKind.Reference or ValueKind.Array))
        {
            return Fx.Err(CalcError.Value);
        }

        if (Stat.Sample(ev, a[1], out List<double> v) is { } err)
        {
            return err;
        }

        bool ascending = false;
        if (a.Length == 3 && !a[2].IsMissing)
        {
            if (!Fx.TryNum(a[2], out double o, out e))
            {
                return e;
            }

            ascending = o != 0;
        }

        int better = 0;
        int equal = 0;
        foreach (double y in v)
        {
            if (y == x)
            {
                equal++;
            }
            else if (ascending ? y < x : y > x)
            {
                better++;
            }
        }

        if (equal == 0)
        {
            return Fx.Err(CalcError.NA);
        }

        return Fx.Num(average ? better + 1 + ((equal - 1) / 2.0) : better + 1);
    }

    private static double Interpolate(double[] s, double pos)
    {
        int i = (int)Math.Floor(pos);
        if (i >= s.Length - 1)
        {
            return s[^1];
        }

        return s[i] + ((pos - i) * (s[i + 1] - s[i]));
    }

    private static CalcValue PercentileOf(double[] s, double k, bool exclusive)
    {
        int n = s.Length;
        if (n == 0)
        {
            return Fx.Err(CalcError.Num);
        }

        if (!exclusive)
        {
            return k < 0 || k > 1 ? Fx.Err(CalcError.Num) : Fx.Num(Interpolate(s, k * (n - 1)));
        }

        double pos = k * (n + 1);
        if (pos < 1 || pos > n)
        {
            return Fx.Err(CalcError.Num);
        }

        return Fx.Num(Interpolate(s, pos - 1));
    }

    private static CalcValue Percentile(Evaluator ev, ReadOnlySpan<CalcValue> a, bool exclusive)
    {
        if (Stat.Sample(ev, a[0], out List<double> v) is { } err)
        {
            return err;
        }

        if (!Fx.TryNum(a[1], out double k, out CalcValue e))
        {
            return e;
        }

        return PercentileOf(Stat.Sorted(v), k, exclusive);
    }

    private static CalcValue Quartile(Evaluator ev, ReadOnlySpan<CalcValue> a, bool exclusive)
    {
        if (Stat.Sample(ev, a[0], out List<double> v) is { } err)
        {
            return err;
        }

        if (!Fx.TryNum(a[1], out double q, out CalcValue e))
        {
            return e;
        }

        q = Math.Truncate(q);
        if (exclusive ? q < 1 || q > 3 : q < 0 || q > 4)
        {
            return Fx.Err(CalcError.Num);
        }

        return PercentileOf(Stat.Sorted(v), q / 4, exclusive);
    }

    private static CalcValue PercentRank(Evaluator ev, ReadOnlySpan<CalcValue> a, bool exclusive)
    {
        if (Stat.Sample(ev, a[0], out List<double> v) is { } err)
        {
            return err;
        }

        if (!Fx.TryNum(a[1], out double x, out CalcValue e))
        {
            return e;
        }

        int digits = 3;
        if (a.Length == 3 && !a[2].IsMissing)
        {
            if (!Fx.TryNum(a[2], out double sig, out e))
            {
                return e;
            }

            if (sig < 1)
            {
                return Fx.Err(CalcError.Num);
            }

            digits = (int)Math.Min(sig, 15);
        }

        double[] s = Stat.Sorted(v);
        int n = s.Length;
        if (n == 0 || x < s[0] || x > s[^1])
        {
            return Fx.Err(CalcError.NA);
        }

        int less = 0;
        while (less < n && s[less] < x)
        {
            less++;
        }

        double rank;
        if (s[less] == x)
        {
            rank = less;
        }
        else
        {
            // Between s[less - 1] and s[less].
            double lo = s[less - 1];
            double hi = s[less];
            rank = less - 1 + ((x - lo) / (hi - lo));
        }

        double result;
        if (exclusive)
        {
            result = (rank + 1) / (n + 1);
        }
        else
        {
            if (n == 1)
            {
                return Fx.Err(CalcError.NA);
            }

            result = rank / (n - 1);
        }

        double scale = Math.Pow(10, digits);
        return Fx.Num(Math.Floor(Fx.Round15(result * scale)) / scale);
    }

    // ---- variance and deviation --------------------------------------------------------------------------------------------------

    private static void Variance(FunctionRegistry r, string name, GatherMode mode, bool sample, bool sqrt)
    {
        r.Add(FunctionDefinition.Create(name, 1, 255, Any, (ev, a) =>
        {
            var v = new List<double>();
            if (Stat.Gather(ev, a, v, mode) is { } err)
            {
                return err;
            }

            if (v.Count < (sample ? 2 : 1))
            {
                return Fx.Err(CalcError.Div0);
            }

            double ss = Stat.SumSquares(v, out _);
            double var = ss / (sample ? v.Count - 1 : v.Count);
            return Fx.Num(sqrt ? Math.Sqrt(var) : var);
        }, repeatLast: true));
    }

    private static CalcValue DevSq(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, GatherMode.Numbers) is { } err)
        {
            return err;
        }

        return v.Count == 0 ? Fx.Err(CalcError.Num) : Fx.Num(Stat.SumSquares(v, out _));
    }

    private static CalcValue AveDev(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, GatherMode.Numbers) is { } err)
        {
            return err;
        }

        if (v.Count == 0)
        {
            return Fx.Err(CalcError.Num);
        }

        double mean = Stat.Mean(v);
        double s = 0;
        foreach (double x in v)
        {
            s += Math.Abs(x - mean);
        }

        return Fx.Num(s / v.Count);
    }

    private static CalcValue GeoMean(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, GatherMode.Numbers) is { } err)
        {
            return err;
        }

        if (v.Count == 0)
        {
            return Fx.Err(CalcError.Num);
        }

        double s = 0;
        foreach (double x in v)
        {
            if (x <= 0)
            {
                return Fx.Err(CalcError.Num);
            }

            s += SpecialMath.Ln(x);
        }

        return Fx.Num(SpecialMath.Exp(s / v.Count));
    }

    private static CalcValue HarMean(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, GatherMode.Numbers) is { } err)
        {
            return err;
        }

        if (v.Count == 0)
        {
            return Fx.Err(CalcError.Num);
        }

        double s = 0;
        foreach (double x in v)
        {
            if (x <= 0)
            {
                return Fx.Err(CalcError.Num);
            }

            s += 1 / x;
        }

        return Fx.Num(v.Count / s);
    }

    private static CalcValue TrimMean(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (Stat.Sample(ev, a[0], out List<double> v) is { } err)
        {
            return err;
        }

        if (!Fx.TryNum(a[1], out double p, out CalcValue e))
        {
            return e;
        }

        if (p < 0 || p >= 1 || v.Count == 0)
        {
            return Fx.Err(CalcError.Num);
        }

        double[] s = Stat.Sorted(v);
        int cut = (int)Math.Floor(s.Length * p / 2);
        double sum = 0;
        for (int i = cut; i < s.Length - cut; i++)
        {
            sum += s[i];
        }

        return Fx.Num(sum / (s.Length - (2 * cut)));
    }

    private static CalcValue Skew(Evaluator ev, ReadOnlySpan<CalcValue> a, bool population)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, GatherMode.Numbers) is { } err)
        {
            return err;
        }

        int n = v.Count;
        if (n < (population ? 1 : 3))
        {
            return Fx.Err(CalcError.Div0);
        }

        double ss = Stat.SumSquares(v, out double mean);
        double sd = Math.Sqrt(ss / (population ? n : n - 1));
        if (sd == 0)
        {
            return Fx.Err(CalcError.Div0);
        }

        double s3 = 0;
        foreach (double x in v)
        {
            double z = (x - mean) / sd;
            s3 += z * z * z;
        }

        return Fx.Num(population ? s3 / n : n / ((n - 1.0) * (n - 2.0)) * s3);
    }

    private static CalcValue Kurt(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var v = new List<double>();
        if (Stat.Gather(ev, a, v, GatherMode.Numbers) is { } err)
        {
            return err;
        }

        int n = v.Count;
        if (n < 4)
        {
            return Fx.Err(CalcError.Div0);
        }

        double ss = Stat.SumSquares(v, out double mean);
        double sd = Math.Sqrt(ss / (n - 1));
        if (sd == 0)
        {
            return Fx.Err(CalcError.Div0);
        }

        double s4 = 0;
        foreach (double x in v)
        {
            double z = (x - mean) / sd;
            s4 += z * z * z * z;
        }

        double nn = n;
        return Fx.Num((nn * (nn + 1) / ((nn - 1) * (nn - 2) * (nn - 3)) * s4) - (3 * (nn - 1) * (nn - 1) / ((nn - 2) * (nn - 3))));
    }

    private static CalcValue Standardize(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e) || !Fx.TryNum(a[1], out double m, out e) || !Fx.TryNum(a[2], out double sd, out e))
        {
            return e;
        }

        return sd <= 0 ? Fx.Err(CalcError.Num) : Fx.Num((x - m) / sd);
    }

    // FREQUENCY(data, bins): counts of data in (previous bin, bin], plus the count above the largest bin, as a vertical array.
    // Bins are taken in ascending order of value and the counts are put back in the order the bins were given (open until the grid answers).
    private static CalcValue Frequency(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (Stat.Sample(ev, a[0], out List<double> data) is { } err)
        {
            return err;
        }

        if (Stat.Sample(ev, a[1], out List<double> bins) is { } err2)
        {
            return err2;
        }

        int m = bins.Count;
        int[] order = new int[m];
        for (int i = 0; i < m; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (p, q) => bins[p].CompareTo(bins[q]));
        var counts = new double[m + 1];
        foreach (double x in data)
        {
            int slot = m;
            for (int i = 0; i < m; i++)
            {
                if (x <= bins[order[i]])
                {
                    slot = i;
                    break;
                }
            }

            counts[slot]++;
        }

        CalcValue[] cells = ev.NewCells(m + 1, 1);
        for (int i = 0; i < m; i++)
        {
            cells[order[i]] = CalcValue.FromNumber(counts[i]);
        }

        cells[m] = CalcValue.FromNumber(counts[m]);
        return CalcValue.FromArray(new CalcArray(m + 1, 1, cells));
    }

    private static CalcValue Prob(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcArray? xs = Fx.AsArray(ev, a[0], out CalcValue e);
        if (xs is null)
        {
            return e;
        }

        CalcArray? ps = Fx.AsArray(ev, a[1], out e);
        if (ps is null)
        {
            return e;
        }

        if (xs.Rows != ps.Rows || xs.Columns != ps.Columns)
        {
            return Fx.Err(CalcError.NA);
        }

        if (!Fx.TryNum(a[2], out double lo, out e))
        {
            return e;
        }

        double hi = lo;
        bool range = a.Length == 4 && !a[3].IsMissing;
        if (range && !Fx.TryNum(a[3], out hi, out e))
        {
            return e;
        }

        double total = 0;
        double result = 0;
        for (int r = 0; r < xs.Rows; r++)
        {
            for (int c = 0; c < xs.Columns; c++)
            {
                CalcValue px = ps[r, c];
                CalcValue xv = xs[r, c];
                if (px.IsError)
                {
                    return px;
                }

                if (xv.IsError)
                {
                    return xv;
                }

                if (px.Kind != ValueKind.Number || xv.Kind != ValueKind.Number)
                {
                    return Fx.Err(CalcError.Value);
                }

                double p = px.Number;
                double x = xv.Number;

                if (p <= 0 || p > 1)
                {
                    return Fx.Err(CalcError.Num);
                }

                total += p;
                if (range ? x >= lo && x <= hi : x == lo)
                {
                    result += p;
                }
            }
        }

        // The probabilities must add up to 1 (to within rounding).
        if (Math.Abs(total - 1) > 1e-9)
        {
            return Fx.Err(CalcError.Num);
        }

        return Fx.Num(result);
    }
}
