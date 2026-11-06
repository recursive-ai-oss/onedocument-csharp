using System;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>Receives the numbers an aggregate function finds in its arguments.</summary>
internal interface INumberSink
{
    /// <summary>Takes one number.</summary>
    /// <param name="value">The number.</param>
    void Add(double value);
}

/// <summary>Coercion and argument-walking helpers shared by the function families.</summary>
internal static class Fx
{
    public static CalcValue Err(CalcError e) => CalcValue.FromError(e);

    /// <summary>A numeric result: not finite is <c>#NUM!</c>, negative zero is zero.</summary>
    public static CalcValue Num(double d) => double.IsFinite(d) ? CalcValue.FromNumber(d == 0 ? 0 : d) : Err(CalcError.Num);

    /// <summary>Coerces a scalar argument to a number; the error to return comes back in <paramref name="error"/>.</summary>
    public static bool TryNum(in CalcValue v, out double d, out CalcValue error)
    {
        CalcError e = CalcConvert.ToNumber(v, out d);
        error = e == CalcError.None ? default : Err(e);
        return e == CalcError.None;
    }

    /// <summary>Coerces to an integer by truncation toward zero, as Excel does for count-like arguments.</summary>
    public static bool TryInt(in CalcValue v, out double d, out CalcValue error)
    {
        if (!TryNum(v, out d, out error))
        {
            return false;
        }

        d = Math.Truncate(d);
        return true;
    }

    public static bool TryBool(in CalcValue v, out bool b, out CalcValue error)
    {
        CalcError e = CalcConvert.ToBoolean(v, out b);
        error = e == CalcError.None ? default : Err(e);
        return e == CalcError.None;
    }

    /// <summary>True for an omitted argument (<c>F(1,,2)</c>) or one left off the end.</summary>
    public static bool Omitted(ReadOnlySpan<CalcValue> args, int i) => i >= args.Length || args[i].IsMissing;

    /// <summary>Rounds to 15 significant digits, the precision Excel keeps when it compares and displays.</summary>
    public static double Round15(double v)
    {
        if (v == 0 || !double.IsFinite(v))
        {
            return v;
        }

        int e = (int)Math.Floor(Math.Log10(Math.Abs(v)));
        int shift = 14 - e;
        if (shift > 300 || shift < -300)
        {
            return v;
        }

        double scale = Math.Pow(10, shift);
        double r = Math.Round(v * scale, MidpointRounding.AwayFromZero) / scale;
        return double.IsFinite(r) ? r : v;
    }

    /// <summary>
    /// Walks the numbers in <paramref name="args"/> the way <c>SUM</c> does: a direct scalar is coerced (text must be a number, a boolean counts, an
    /// omitted argument is 0), while the cells of a reference or array contribute only their numbers; the first error found is returned.
    /// </summary>
    public static CalcValue? Collect<T>(Evaluator ev, ReadOnlySpan<CalcValue> args, ref T sink)
        where T : struct, INumberSink
    {
        for (int i = 0; i < args.Length; i++)
        {
            CalcValue v = args[i];
            switch (v.Kind)
            {
                case ValueKind.Reference:
                    if (!ev.TryReadAreas(v.Reference!, out CalcArray[] blocks, out CalcError re))
                    {
                        return Err(re);
                    }

                    foreach (CalcArray b in blocks)
                    {
                        if (CollectArray(b, ref sink) is { } e1)
                        {
                            return e1;
                        }
                    }

                    break;
                case ValueKind.Array:
                    if (CollectArray(v.Array!, ref sink) is { } e2)
                    {
                        return e2;
                    }

                    break;
                case ValueKind.Lambda:
                    return Err(CalcError.Value);
                default:
                    CalcError e = CalcConvert.ToNumber(v, out double d);
                    if (e != CalcError.None)
                    {
                        return Err(e);
                    }

                    sink.Add(d);
                    break;
            }
        }

        return null;
    }

    private static CalcValue? CollectArray<T>(CalcArray a, ref T sink)
        where T : struct, INumberSink
    {
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                CalcValue x = a[r, c];
                if (x.IsError)
                {
                    return x;
                }

                if (x.Kind == ValueKind.Number)
                {
                    sink.Add(x.Number);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Flattens every argument into scalar values in reading order (references and arrays row by row). Returns the error when a reference cannot be read.
    /// </summary>
    public static CalcError Flatten(Evaluator ev, in CalcValue arg, List<CalcValue> into)
    {
        switch (arg.Kind)
        {
            case ValueKind.Reference:
                if (!ev.TryReadAreas(arg.Reference!, out CalcArray[] blocks, out CalcError re))
                {
                    return re;
                }

                foreach (CalcArray b in blocks)
                {
                    AddAll(b, into);
                }

                return CalcError.None;
            case ValueKind.Array:
                AddAll(arg.Array!, into);
                return CalcError.None;
            case ValueKind.Lambda:
                return CalcError.Value;
            default:
                into.Add(arg);
                return CalcError.None;
        }
    }

    private static void AddAll(CalcArray a, List<CalcValue> into)
    {
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                into.Add(a[r, c]);
            }
        }
    }

    /// <summary>Reads an argument as an array of scalars (a scalar becomes 1 × 1); null with an error when it is an error or lambda.</summary>
    public static CalcArray? AsArray(Evaluator ev, in CalcValue v, out CalcValue error)
    {
        error = default;
        CalcValue d = ev.Dereference(v);
        switch (d.Kind)
        {
            case ValueKind.Array:
                return d.Array;
            case ValueKind.Error:
                error = d;
                return null;
            case ValueKind.Lambda:
                error = Err(CalcError.Value);
                return null;
            default:
                return CalcArray.OfValue(d);
        }
    }

    public static CalcValue Array(Evaluator ev, int rows, int cols, Func<int, int, CalcValue> cell)
    {
        CalcValue[] cells = ev.NewCells(rows, cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                cells[(r * cols) + c] = cell(r, c);
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    /// <summary>
    /// True when the range arguments at <paramref name="start"/>, <paramref name="start"/> + <paramref name="step"/>, ... are explicit rectangles
    /// of different sizes. The <c>*IFS</c> functions answer <c>#VALUE!</c> for that, and checking the references first avoids reading
    /// blocks that can only be thrown away. A whole-row or whole-column reference is skipped: it is clipped to the data when read.
    /// </summary>
    public static bool RangeShapesDiffer(ReadOnlySpan<CalcValue> args, int start, int step)
    {
        int rows = -1;
        int cols = -1;
        for (int i = start; i < args.Length; i += step)
        {
            if (args[i].Kind != ValueKind.Reference || args[i].Reference is not { AreaCount: 1, IsSpill: false } rf || rf.First.LastSheet is not null)
            {
                continue;
            }

            CellArea a = rf.First;
            if (a.Rows >= CellArea.MaxRow || a.Columns >= CellArea.MaxColumn)
            {
                continue;
            }

            if (rows < 0)
            {
                rows = a.Rows;
                cols = a.Columns;
            }
            else if (a.Rows != rows || a.Columns != cols)
            {
                return true;
            }
        }

        return false;
    }

    public static string Inv(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Adds numbers the way <c>SUM</c> does, snapping a cancelled total to zero.</summary>
internal struct SumSink : INumberSink
{
    public double Total;

    public void Add(double value) => Total = Evaluator.AddSnapped(Total, value);
}

/// <summary>Multiplies numbers and counts them.</summary>
internal struct ProductSink : INumberSink
{
    public double Total;
    public int Count;

    public void Add(double value)
    {
        Total = Count == 0 ? value : Total * value;
        Count++;
    }
}

/// <summary>Adds squares.</summary>
internal struct SumSquaresSink : INumberSink
{
    public double Total;

    public void Add(double value) => Total += value * value;
}

/// <summary>Collects numbers into a list.</summary>
internal struct ListSink : INumberSink
{
    public List<double> Values;

    public void Add(double value) => (Values ??= []).Add(value);
}
