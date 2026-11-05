using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>How <see cref="Stat.Gather"/> treats the values it meets.</summary>
internal enum GatherMode
{
    /// <summary>AVERAGE, VAR, MEDIAN and the rest: cells of references and arrays contribute numbers only; direct arguments are coerced.</summary>
    Numbers,

    /// <summary>The "A" forms (AVERAGEA, MAXA, VARA): in references and arrays text counts as 0, TRUE as 1 and FALSE as 0.</summary>
    WithText,

    /// <summary>COUNT: like <see cref="Numbers"/> but nothing is an error; whatever cannot be a number is skipped.</summary>
    Count,
}

/// <summary>Argument walking and small numeric helpers shared by the statistical functions.</summary>
internal static class Stat
{
    public static CalcValue Err(CalcError e) => CalcValue.FromError(e);

    /// <summary>
    /// Collects the numbers of the arguments. Microsoft's reference for AVERAGE and its relatives: arguments that are text, logical values or empty
    /// cells inside a reference or array are ignored, while a value typed directly into the argument list is converted (text that is not a number
    /// is <c>#VALUE!</c>); an error anywhere is returned.
    /// </summary>
    public static CalcValue? Gather(Evaluator ev, ReadOnlySpan<CalcValue> args, List<double> into, GatherMode mode)
    {
        for (int i = 0; i < args.Length; i++)
        {
            CalcValue v = args[i];
            switch (v.Kind)
            {
                case ValueKind.Reference:
                    if (!ev.TryReadAreas(v.Reference!, out CalcArray[] blocks, out CalcError re))
                    {
                        return mode == GatherMode.Count ? null : Err(re);
                    }

                    foreach (CalcArray b in blocks)
                    {
                        if (GatherCells(b, into, mode) is { } e1)
                        {
                            return e1;
                        }
                    }

                    break;
                case ValueKind.Array:
                    if (GatherCells(v.Array!, into, mode) is { } e2)
                    {
                        return e2;
                    }

                    break;
                case ValueKind.Lambda:
                    return mode == GatherMode.Count ? null : Err(CalcError.Value);
                case ValueKind.Error:
                    if (mode != GatherMode.Count)
                    {
                        return v;
                    }

                    break;
                default:
                    CalcError e = CalcConvert.ToNumber(v, out double d);
                    if (e == CalcError.None)
                    {
                        // A direct text argument that is not a number never reaches here: ToNumber fails for it.
                        into.Add(d);
                    }
                    else if (mode != GatherMode.Count)
                    {
                        return Err(e);
                    }

                    break;
            }
        }

        return null;
    }

    private static CalcValue? GatherCells(CalcArray a, List<double> into, GatherMode mode)
    {
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                CalcValue x = a[r, c];
                switch (x.Kind)
                {
                    case ValueKind.Number:
                        into.Add(x.Number);
                        break;
                    case ValueKind.Error:
                        if (mode != GatherMode.Count)
                        {
                            return x;
                        }

                        break;
                    case ValueKind.Text when mode == GatherMode.WithText:
                        into.Add(0);
                        break;
                    case ValueKind.Boolean when mode == GatherMode.WithText:
                        into.Add(x.Number);
                        break;
                    default:
                        break;
                }
            }
        }

        return null;
    }

    /// <summary>Reads one argument as a numeric sample: its numbers in reading order.</summary>
    public static CalcValue? Sample(Evaluator ev, in CalcValue arg, out List<double> values, GatherMode mode = GatherMode.Numbers)
    {
        values = [];
        return Gather(ev, [arg], values, mode);
    }

    /// <summary>Reads two arguments as paired samples: the pairs where both cells hold numbers. Different sizes are <c>#N/A</c>.</summary>
    public static CalcValue? Pairs(Evaluator ev, in CalcValue a, in CalcValue b, out List<double> xs, out List<double> ys)
    {
        xs = [];
        ys = [];
        CalcArray? aa = Fx.AsArray(ev, a, out CalcValue e);
        if (aa is null)
        {
            return e;
        }

        CalcArray? bb = Fx.AsArray(ev, b, out e);
        if (bb is null)
        {
            return e;
        }

        if (aa.Rows != bb.Rows || aa.Columns != bb.Columns)
        {
            return Err(CalcError.NA);
        }

        for (int r = 0; r < aa.Rows; r++)
        {
            for (int c = 0; c < aa.Columns; c++)
            {
                CalcValue p = aa[r, c];
                CalcValue q = bb[r, c];
                if (p.IsError)
                {
                    return p;
                }

                if (q.IsError)
                {
                    return q;
                }

                if (p.Kind == ValueKind.Number && q.Kind == ValueKind.Number)
                {
                    xs.Add(p.Number);
                    ys.Add(q.Number);
                }
            }
        }

        return null;
    }

    /// <summary>The mean of a sample, with a second pass that removes the rounding error of the running sum.</summary>
    public static double Mean(List<double> v)
    {
        double sum = 0;
        foreach (double x in v)
        {
            sum += x;
        }

        double mean = sum / v.Count;
        double corr = 0;
        foreach (double x in v)
        {
            corr += x - mean;
        }

        return mean + (corr / v.Count);
    }

    /// <summary>The sum of squared deviations from the mean (two-pass).</summary>
    public static double SumSquares(List<double> v, out double mean)
    {
        mean = Mean(v);
        double s = 0;
        foreach (double x in v)
        {
            double d = x - mean;
            s += d * d;
        }

        return s;
    }

    public static double[] Sorted(List<double> v)
    {
        double[] a = [.. v];
        Array.Sort(a);
        return a;
    }

    public static bool TryNum(in CalcValue v, out double d, out CalcValue error) => Fx.TryNum(v, out d, out error);

    /// <summary>Truncates toward zero as the "number is truncated to an integer" rule of the reference pages does.</summary>
    public static double Trunc(double d) => Math.Truncate(d);

    public static CalcValue Number(double d) => Fx.Num(d);
}
