using System;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>A one-dimensional view of a row or column of a <see cref="CalcArray"/>, for searching.</summary>
internal readonly struct Vec
{
    private readonly CalcArray _array;
    private readonly bool _isRow;
    private readonly int _fixed;

    public Vec(CalcArray array, bool isRow, int index)
    {
        _array = array;
        _isRow = isRow;
        _fixed = index;
    }

    public int Length => _isRow ? _array.Columns : _array.Rows;

    public CalcValue this[int i] => _isRow ? _array[_fixed, i] : _array[i, _fixed];

    /// <summary>The array as a vector: a single row or a single column, or null when it is a block.</summary>
    public static bool TryOf(CalcArray a, out Vec v)
    {
        if (a.Rows == 1)
        {
            v = new Vec(a, true, 0);
            return true;
        }

        if (a.Columns == 1)
        {
            v = new Vec(a, false, 0);
            return true;
        }

        v = default;
        return false;
    }
}

/// <summary>Matching rules shared by the lookup functions: type-strict equality, wildcards and the ordered search.</summary>
internal static class LookupSupport
{
    /// <summary>The class a value sorts in: numbers, text, logical values, errors, then empty cells.</summary>
    public static int Class(in CalcValue v) => v.Kind switch
    {
        ValueKind.Number => 0,
        ValueKind.Text => 1,
        ValueKind.Boolean => 2,
        ValueKind.Error => 3,
        _ => 4,
    };

    /// <summary>Orders two values of the same class (numbers by value to 15 digits, text ignoring case, FALSE before TRUE, errors by code).</summary>
    public static int CompareSameClass(in CalcValue a, in CalcValue b)
    {
        if (a.Kind == ValueKind.Error)
        {
            return ((int)a.Error).CompareTo((int)b.Error);
        }

        if (a.Kind == ValueKind.Blank)
        {
            return 0;
        }

        return Evaluator.Compare(a, b);
    }

    /// <summary>A looked-up empty cell or omitted value searches as zero.</summary>
    public static CalcValue Normalise(in CalcValue v) => v.Kind == ValueKind.Blank ? CalcValue.FromNumber(0) : v;

    /// <summary>True when the text holds a wildcard (<c>*</c>, <c>?</c>) or an escape (<c>~</c>).</summary>
    public static bool HasWildcard(string s) => s.AsSpan().IndexOfAny('*', '?', '~') >= 0;

    /// <summary>Matches text against a pattern in which <c>*</c> is any run, <c>?</c> any one character and <c>~</c> escapes the next character; case-insensitive.</summary>
    public static bool WildcardMatch(string pattern, string text) => new WildcardPattern(pattern).IsMatch(text);

    /// <summary>Exact equality for lookups: the classes must agree (the number 1 never matches the text "1"); text may use wildcards.</summary>
    public static bool IsMatch(in CalcValue target, in CalcValue cell, bool wildcards)
    {
        if (Class(target) != Class(cell))
        {
            return false;
        }

        if (wildcards && target.Kind == ValueKind.Text)
        {
            return WildcardMatch(target.Text, cell.Text);
        }

        return CompareSameClass(target, cell) == 0;
    }

    /// <summary>Position (0-based) of the first exact match in the given direction, or -1.</summary>
    public static int FindExact(in Vec v, in CalcValue target, bool wildcards, bool reverse)
    {
        CalcValue t = Normalise(target);
        bool wild = wildcards && t.Kind == ValueKind.Text && HasWildcard(t.Text);
        int n = v.Length;
        if (!reverse)
        {
            for (int i = 0; i < n; i++)
            {
                if (IsMatch(t, v[i], wild))
                {
                    return i;
                }
            }
        }
        else
        {
            for (int i = n - 1; i >= 0; i--)
            {
                if (IsMatch(t, v[i], wild))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>The order the searches see two values in: numbers, text, logical values, errors, then empty cells; within a class by value.</summary>
    public static int Order(in CalcValue a, in CalcValue b)
    {
        int ca = Class(a);
        int cb = Class(b);
        return ca != cb ? ca.CompareTo(cb) : CompareSameClass(a, b);
    }

    /// <summary>
    /// The legacy approximate search (<c>MATCH</c> types 1 and -1, <c>VLOOKUP</c>, <c>HLOOKUP</c>, <c>LOOKUP</c>) over data assumed sorted.
    /// Observed in Excel 16.0.20527 on unsorted and mixed data: a probe that lands on a cell of another class moves up to the next cell of
    /// the target's class; when a probe is equal the search stops there and, ascending, takes the last of the run of equal cells that
    /// follows (descending it stays on the probe); otherwise it keeps the last probe on the near side. Ascending that is the last element
    /// not greater than the target; descending, the last element not less than it.
    /// </summary>
    /// <param name="v">The data.</param>
    /// <param name="target">The value looked for.</param>
    /// <param name="ascending">The order the data is assumed to be in.</param>
    /// <returns>The 0-based position, or -1.</returns>
    public static int Binary(in Vec v, in CalcValue target, bool ascending)
    {
        CalcValue t = Normalise(target);
        if (t.Kind == ValueKind.Error)
        {
            return -1;
        }

        int cls = Class(t);
        int n = v.Length;
        int lo = 0;
        int hi = n - 1;
        int best = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            int probe = mid;
            while (probe <= hi && Class(v[probe]) != cls)
            {
                probe++;
            }

            if (probe > hi)
            {
                hi = mid - 1;
                continue;
            }

            int c = CompareSameClass(v[probe], t);
            if (!ascending)
            {
                c = -c;
            }

            if (c == 0)
            {
                int last = probe;
                while (ascending && last + 1 < n && Class(v[last + 1]) == cls && CompareSameClass(v[last + 1], t) == 0)
                {
                    last++;
                }

                return last;
            }

            if (c < 0)
            {
                best = probe;
                lo = probe + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best;
    }

    /// <summary>Converts an argument to a truncated integer.</summary>
    public static bool TryInt(in CalcValue v, out int n, out CalcValue error)
    {
        CalcError e = CalcConvert.ToNumber(v, out double d);
        if (e != CalcError.None)
        {
            n = 0;
            error = CalcValue.FromError(e);
            return false;
        }

        d = Math.Truncate(d);
        if (d > int.MaxValue / 2 || d < int.MinValue / 2)
        {
            n = 0;
            error = CalcValue.FromError(CalcError.Value);
            return false;
        }

        n = (int)d;
        error = default;
        return true;
    }

    public static CalcValue Err(CalcError e) => CalcValue.FromError(e);

    /// <summary>Reads an argument as an array; false (with the error to return) for an error or a lambda.</summary>
    public static bool TryArray(Evaluator ev, in CalcValue v, out CalcArray array, out CalcValue error)
    {
        CalcValue d = ev.Dereference(v);
        switch (d.Kind)
        {
            case ValueKind.Array:
                array = d.Array!;
                ev.ChargeSteps(array.Count);
                error = default;
                return true;
            case ValueKind.Error:
                array = null!;
                error = d;
                return false;
            case ValueKind.Lambda:
                array = null!;
                error = Err(CalcError.Value);
                return false;
            default:
                array = CalcArray.OfValue(d);
                error = default;
                return true;
        }
    }
}
