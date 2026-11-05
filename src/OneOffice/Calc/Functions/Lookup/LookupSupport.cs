using System;

namespace RecursiveAI.OneOffice.Calc;

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
    public static bool WildcardMatch(string pattern, string text)
    {
        int p = 0;
        int t = 0;
        int starP = -1;
        int starT = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length)
            {
                char pc = pattern[p];
                if (pc == '*')
                {
                    starP = ++p;
                    starT = t;
                    continue;
                }

                bool escaped = false;
                if (pc == '~' && p + 1 < pattern.Length)
                {
                    pc = pattern[p + 1];
                    escaped = true;
                }

                if ((!escaped && pc == '?') || char.ToUpperInvariant(pc) == char.ToUpperInvariant(text[t]))
                {
                    p += escaped ? 2 : 1;
                    t++;
                    continue;
                }
            }

            if (starP < 0)
            {
                return false;
            }

            p = starP;
            t = ++starT;
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

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

    /// <summary>
    /// Binary search over data assumed sorted. Elements of another class than the target are skipped by stepping toward the low end
    /// (a block of other-class cells never matches). Ascending: the last element not greater than the target; descending: the last
    /// element not less than it. On unsorted data the answer is whatever the probing finds, as in Excel.
    /// </summary>
    /// <param name="v">The data.</param>
    /// <param name="target">The value looked for.</param>
    /// <param name="ascending">The order the data is assumed to be in.</param>
    /// <param name="exactOnly">True to return only an equal element (the first of a run of equal ones).</param>
    /// <returns>The 0-based position, or -1.</returns>
    public static int Binary(in Vec v, in CalcValue target, bool ascending, bool exactOnly)
    {
        CalcValue t = Normalise(target);
        int cls = Class(t);
        int lo = 0;
        int hi = v.Length - 1;
        int best = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            int probe = mid;
            while (probe >= lo && Class(v[probe]) != cls)
            {
                probe--;
            }

            if (probe < lo)
            {
                lo = mid + 1;
                continue;
            }

            int c = CompareSameClass(v[probe], t);
            if (!ascending)
            {
                c = -c;
            }

            if (c <= 0)
            {
                best = probe;
                lo = mid + 1;
                if (c == 0 && exactOnly)
                {
                    // Keep going right: the last equal element wins in approximate mode; callers wanting the first use FindFirstEqualBinary.
                }
            }
            else
            {
                hi = probe - 1;
            }
        }

        if (best >= 0 && exactOnly && CompareSameClass(v[best], t) != 0)
        {
            return -1;
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
