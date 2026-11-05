using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

internal static partial class TextFunctions
{
    private static void RegisterSplit(FunctionRegistry r)
    {
        ArgKind[] kinds = [ArgKind.Value, ArgKind.Array, ArgKind.Value, ArgKind.Value, ArgKind.Value, ArgKind.Array];
        r.Add(FunctionDefinition.Create("TEXTBEFORE", 2, 6, kinds, (e, a) => BeforeAfter(e, a, true)));
        r.Add(FunctionDefinition.Create("TEXTAFTER", 2, 6, kinds, (e, a) => BeforeAfter(e, a, false)));
        r.Add(FunctionDefinition.Create("TEXTSPLIT", 2, 6, [ArgKind.Array, ArgKind.Array, ArgKind.Array, ArgKind.Value, ArgKind.Value, ArgKind.Array], Split));
    }

    // Reads delimiters from a value or array; the error to return comes back when one of them is an error.
    private static List<string>? Delimiters(Evaluator ev, in CalcValue arg, out CalcValue error)
    {
        error = default;
        var list = new List<string>();
        var items = new List<CalcValue>();
        CalcError fe = Fx.Flatten(ev, arg, items);
        if (fe != CalcError.None)
        {
            error = Fx.Err(fe);
            return null;
        }

        foreach (CalcValue v in items)
        {
            if (v.IsError)
            {
                error = v;
                return null;
            }

            list.Add(Str(v));
        }

        return list;
    }

    // Every delimiter occurrence from the left, not overlapping; where several delimiters match at one place the longest wins.
    private static List<(int Pos, int Len)> Occurrences(string text, List<string> delims, StringComparison cmp)
    {
        var found = new List<(int, int)>();
        int i = 0;
        while (i < text.Length)
        {
            int best = 0;
            foreach (string d in delims)
            {
                if (d.Length > best && i + d.Length <= text.Length && string.Compare(text, i, d, 0, d.Length, cmp) == 0)
                {
                    best = d.Length;
                }
            }

            if (best > 0)
            {
                found.Add((i, best));
                i += best;
            }
            else
            {
                i++;
            }
        }

        return found;
    }

    // TEXTBEFORE / TEXTAFTER(text, delimiter, instance = 1, match_mode = 0, match_end = 0, if_not_found = #N/A): a negative instance counts from the end;
    // match_end treats the end (or, counting backwards, the start) of the text as one more delimiter.
    private static CalcValue BeforeAfter(Evaluator ev, ReadOnlySpan<CalcValue> a, bool before)
    {
        string text = Str(a[0]);
        List<string>? delims = Delimiters(ev, a[1], out CalcValue e);
        if (delims is null)
        {
            return e;
        }

        double inst = 1;
        double mode = 0;
        double matchEnd = 0;
        if ((!Fx.Omitted(a, 2) && !Fx.TryInt(a[2], out inst, out e)) || (!Fx.Omitted(a, 3) && !Fx.TryInt(a[3], out mode, out e)) || (!Fx.Omitted(a, 4) && !Fx.TryInt(a[4], out matchEnd, out e)))
        {
            return e;
        }

        if (inst == 0 || Math.Abs(inst) > text.Length || mode is not (0 or 1) || matchEnd is not (0 or 1))
        {
            return Fx.Err(CalcError.Value);
        }

        List<(int Pos, int Len)> occ = Occurrences(text, delims.FindAll(d => d.Length > 0), mode == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        bool emptyDelim = delims.Exists(d => d.Length == 0);
        if (emptyDelim)
        {
            occ.Add(inst > 0 ? (0, 0) : (text.Length, 0));
            occ.Sort((x, y) => x.Pos.CompareTo(y.Pos));
        }

        int k = (int)Math.Abs(inst);
        int n = occ.Count;
        (int Pos, int Len) target;
        if (k <= n)
        {
            target = inst > 0 ? occ[k - 1] : occ[n - k];
        }
        else if (matchEnd == 1 && k == n + 1)
        {
            target = inst > 0 ? (text.Length, 0) : (0, 0);
        }
        else
        {
            return Fx.Omitted(a, 5) ? Fx.Err(CalcError.NA) : ev.ToScalar(a[5]);
        }

        return CalcValue.FromText(before ? text[..target.Pos] : text[(target.Pos + target.Len)..]);
    }

    private static List<string> SplitBy(string s, List<string> delims, StringComparison cmp, bool ignoreEmpty)
    {
        var parts = new List<string>();
        int start = 0;
        foreach ((int pos, int len) in Occurrences(s, delims, cmp))
        {
            parts.Add(s[start..pos]);
            start = pos + len;
        }

        parts.Add(s[start..]);
        if (ignoreEmpty)
        {
            parts.RemoveAll(p => p.Length == 0);
        }

        return parts;
    }

    // TEXTSPLIT(text, col_delimiter, row_delimiter, ignore_empty = FALSE, match_mode = 0, pad_with = #N/A): rows first, then columns inside each row;
    // short rows are padded.
    private static CalcValue Split(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcArray? ta = Fx.AsArray(ev, a[0], out CalcValue e);
        if (ta is null)
        {
            return e;
        }

        CalcValue tv = ta[0, 0];
        if (tv.IsError)
        {
            return tv;
        }

        string text = Str(tv);
        List<string>? cols = Delimiters(ev, a[1], out e);
        if (cols is null)
        {
            return e;
        }

        List<string>? rows = Fx.Omitted(a, 2) ? [] : Delimiters(ev, a[2], out e);
        if (rows is null)
        {
            return e;
        }

        cols.RemoveAll(d => d.Length == 0);
        rows.RemoveAll(d => d.Length == 0);
        bool ignoreEmpty = false;
        double mode = 0;
        if ((!Fx.Omitted(a, 3) && !Fx.TryBool(a[3], out ignoreEmpty, out e)) || (!Fx.Omitted(a, 4) && !Fx.TryInt(a[4], out mode, out e)))
        {
            return e;
        }

        if (mode is not (0 or 1) || (cols.Count == 0 && rows.Count == 0))
        {
            return Fx.Err(CalcError.Value);
        }

        StringComparison cmp = mode == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        CalcValue pad = Fx.Omitted(a, 5) ? Fx.Err(CalcError.NA) : ev.ToScalar(a[5]);
        List<string> rowParts = rows.Count == 0 ? [text] : SplitBy(text, rows, cmp, ignoreEmpty);
        var table = new List<List<string>>();
        int width = 0;
        foreach (string rp in rowParts)
        {
            List<string> cp = cols.Count == 0 ? [rp] : SplitBy(rp, cols, cmp, ignoreEmpty);
            table.Add(cp);
            width = Math.Max(width, cp.Count);
        }

        if (table.Count == 0 || width == 0)
        {
            return Fx.Err(CalcError.Calc);
        }

        CalcValue[] cells = ev.NewCells(table.Count, width);
        for (int r = 0; r < table.Count; r++)
        {
            for (int c = 0; c < width; c++)
            {
                cells[(r * width) + c] = c < table[r].Count ? CalcValue.FromText(table[r][c]) : pad;
            }
        }

        return CalcValue.FromArray(new CalcArray(table.Count, width, cells));
    }
}
