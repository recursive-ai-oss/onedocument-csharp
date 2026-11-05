using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>Dynamic-array functions, written from Microsoft's function reference pages. Behaviour the pages leave open is marked <c>// open:</c>.</summary>
internal static class ArrayFunctions
{
    private static readonly ArgKind[] A = [ArgKind.Array];

    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("FILTER", 2, 3, [ArgKind.Array, ArgKind.Array, ArgKind.Any], Filter));
        r.Add(FunctionDefinition.Create("SORT", 1, 4, [ArgKind.Array, ArgKind.Array, ArgKind.Array, ArgKind.Value], Sort));
        r.Add(FunctionDefinition.Create("SORTBY", 2, int.MaxValue, A, SortBy, repeatLast: true));
        r.Add(FunctionDefinition.Create("UNIQUE", 1, 3, [ArgKind.Array, ArgKind.Value, ArgKind.Value], Unique));
        r.Add(FunctionDefinition.Create("TAKE", 2, 3, [ArgKind.Array, ArgKind.Value, ArgKind.Value], (c, a) => TakeDrop(c, a, true)));
        r.Add(FunctionDefinition.Create("DROP", 2, 3, [ArgKind.Array, ArgKind.Value, ArgKind.Value], (c, a) => TakeDrop(c, a, false)));
        r.Add(FunctionDefinition.Create("EXPAND", 2, 4, [ArgKind.Array, ArgKind.Value, ArgKind.Value, ArgKind.Any], Expand));
        r.Add(FunctionDefinition.Create("HSTACK", 1, int.MaxValue, A, (c, a) => Stack(c, a, true), repeatLast: true));
        r.Add(FunctionDefinition.Create("VSTACK", 1, int.MaxValue, A, (c, a) => Stack(c, a, false), repeatLast: true));
        r.Add(FunctionDefinition.Create("TOCOL", 1, 3, [ArgKind.Array, ArgKind.Value, ArgKind.Value], (c, a) => ToVector(c, a, true)));
        r.Add(FunctionDefinition.Create("TOROW", 1, 3, [ArgKind.Array, ArgKind.Value, ArgKind.Value], (c, a) => ToVector(c, a, false)));
        r.Add(FunctionDefinition.Create("WRAPROWS", 2, 3, [ArgKind.Array, ArgKind.Value, ArgKind.Any], (c, a) => Wrap(c, a, true)));
        r.Add(FunctionDefinition.Create("WRAPCOLS", 2, 3, [ArgKind.Array, ArgKind.Value, ArgKind.Any], (c, a) => Wrap(c, a, false)));
        r.Add(FunctionDefinition.Create("RANDARRAY", 0, 5, [ArgKind.Value], RandArray, FunctionTraits.Volatile));
    }

    private static CalcValue Err(CalcError e) => CalcValue.FromError(e);

    private static CalcValue Make(Evaluator ev, int rows, int cols, Func<int, int, CalcValue> cell)
    {
        ev.ChargeSteps((long)rows * cols);
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

    private static bool Flag(ReadOnlySpan<CalcValue> args, int i, bool dflt, out CalcValue error)
    {
        error = default;
        if (args.Length <= i || args[i].IsMissing)
        {
            return dflt;
        }

        CalcError e = CalcConvert.ToBoolean(args[i], out bool b);
        if (e != CalcError.None)
        {
            error = Err(e);
        }

        return b;
    }

    // ---- FILTER -----------------------------------------------------------------------------------------------------------

    // Microsoft's FILTER page: include is a column (or row) of the array's height (width); non-zero numbers and TRUE keep the row.
    // Nothing kept gives if_empty, or #CALC!.
    private static CalcValue Filter(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error) || !LookupSupport.TryArray(ev, args[1], out CalcArray inc, out error))
        {
            return error;
        }

        bool byRow;
        if (inc.Columns == 1 && inc.Rows == a.Rows)
        {
            byRow = true;
        }
        else if (inc.Rows == 1 && inc.Columns == a.Columns)
        {
            byRow = false;
        }
        else
        {
            return Err(CalcError.Value);
        }

        var keep = new List<int>();
        int n = byRow ? inc.Rows : inc.Columns;
        for (int i = 0; i < n; i++)
        {
            CalcValue x = byRow ? inc[i, 0] : inc[0, i];
            if (x.IsError)
            {
                return x;
            }

            if (x.Kind == ValueKind.Text)
            {
                return Err(CalcError.Value);
            }

            if (x.Kind is ValueKind.Number or ValueKind.Boolean && x.Number != 0)
            {
                keep.Add(i);
            }
        }

        if (keep.Count == 0)
        {
            return args.Length > 2 && !args[2].IsMissing ? args[2] : Err(CalcError.Calc);
        }

        return byRow
            ? Make(ev, keep.Count, a.Columns, (r, c) => a[keep[r], c])
            : Make(ev, a.Rows, keep.Count, (r, c) => a[r, keep[c]]);
    }

    // ---- SORT, SORTBY ------------------------------------------------------------------------------------------------------

    // Microsoft's sort-order notes: numbers, then text, then FALSE before TRUE, then errors; descending reverses that, and empty
    // cells come last either way.
    internal static int CompareForSort(in CalcValue x, in CalcValue y, bool descending)
    {
        int cx = LookupSupport.Class(x);
        int cy = LookupSupport.Class(y);
        if (cx == 4 || cy == 4)
        {
            return cx == cy ? 0 : cx == 4 ? 1 : -1;
        }

        int c = cx != cy ? cx.CompareTo(cy) : LookupSupport.CompareSameClass(x, y);
        return descending ? -c : c;
    }

    private static CalcValue Sort(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        bool byCol = Flag(args, 3, false, out error);
        if (error.IsError)
        {
            return error;
        }

        var keys = new List<int>();
        var orders = new List<bool>();
        if (args.Length > 1 && !args[1].IsMissing)
        {
            if (!LookupSupport.TryArray(ev, args[1], out CalcArray ki, out error))
            {
                return error;
            }

            for (int r = 0; r < ki.Rows; r++)
            {
                for (int c = 0; c < ki.Columns; c++)
                {
                    if (ki[r, c].IsError)
                    {
                        return ki[r, c];
                    }

                    if (!LookupSupport.TryInt(ki[r, c], out int k, out error))
                    {
                        return error;
                    }

                    keys.Add(k);
                }
            }
        }
        else
        {
            keys.Add(1);
        }

        var ord = new List<int>();
        if (args.Length > 2 && !args[2].IsMissing)
        {
            if (!LookupSupport.TryArray(ev, args[2], out CalcArray oi, out error))
            {
                return error;
            }

            for (int r = 0; r < oi.Rows; r++)
            {
                for (int c = 0; c < oi.Columns; c++)
                {
                    if (oi[r, c].IsError)
                    {
                        return oi[r, c];
                    }

                    if (!LookupSupport.TryInt(oi[r, c], out int o, out error))
                    {
                        return error;
                    }

                    ord.Add(o);
                }
            }
        }

        if (ord.Count == 0)
        {
            ord.Add(1);
        }

        // open: when the orders are fewer than the keys, the last order is reused.
        int extent = byCol ? a.Columns : a.Rows;
        int across = byCol ? a.Rows : a.Columns;
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] < 1 || keys[i] > across)
            {
                return Err(CalcError.Value);
            }

            int o = ord[Math.Min(i, ord.Count - 1)];
            if (o is not (1 or -1))
            {
                return Err(CalcError.Value);
            }

            orders.Add(o == -1);
        }

        int[] idx = new int[extent];
        for (int i = 0; i < extent; i++)
        {
            idx[i] = i;
        }

        Array.Sort(idx, (p, q) =>
        {
            for (int k = 0; k < keys.Count; k++)
            {
                int col = keys[k] - 1;
                int c = byCol ? CompareForSort(a[col, p], a[col, q], orders[k]) : CompareForSort(a[p, col], a[q, col], orders[k]);
                if (c != 0)
                {
                    return c;
                }
            }

            return p.CompareTo(q); // equal keys keep their order: the sort is stable.
        });
        return byCol ? Make(ev, a.Rows, a.Columns, (r, c) => a[r, idx[c]]) : Make(ev, a.Rows, a.Columns, (r, c) => a[idx[r], c]);
    }

    private static CalcValue SortBy(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        var by = new List<CalcArray>();
        var desc = new List<bool>();
        bool? byRow = null;
        int i = 1;
        while (i < args.Length)
        {
            if (!LookupSupport.TryArray(ev, args[i], out CalcArray k, out error))
            {
                return error;
            }

            // Arguments alternate: by_array, sort_order, by_array, sort_order, ...
            bool order = false;
            if (i + 1 < args.Length && !args[i + 1].IsMissing)
            {
                CalcValue o = ev.ToScalar(args[i + 1]);
                if (o.IsError)
                {
                    return o;
                }

                if (!LookupSupport.TryInt(o, out int ov, out error))
                {
                    return error;
                }

                if (ov is not (1 or -1))
                {
                    return Err(CalcError.Value);
                }

                order = ov == -1;
            }

            i += 2;

            bool isCol = k.Columns == 1 && k.Rows == a.Rows;
            bool isRow = k.Rows == 1 && k.Columns == a.Columns;
            if (!isCol && !isRow)
            {
                return Err(CalcError.Value);
            }

            bool thisByRow = isCol;
            if (byRow is { } b && b != thisByRow && !(k.Rows == 1 && k.Columns == 1))
            {
                return Err(CalcError.Value);
            }

            byRow ??= thisByRow;
            by.Add(k);
            desc.Add(order);
        }

        bool rows = byRow ?? true;
        int extent = rows ? a.Rows : a.Columns;
        int[] idx = new int[extent];
        for (int p = 0; p < extent; p++)
        {
            idx[p] = p;
        }

        Array.Sort(idx, (p, q) =>
        {
            for (int k = 0; k < by.Count; k++)
            {
                CalcArray ka = by[k];
                int c = rows ? CompareForSort(ka[p, 0], ka[q, 0], desc[k]) : CompareForSort(ka[0, p], ka[0, q], desc[k]);
                if (c != 0)
                {
                    return c;
                }
            }

            return p.CompareTo(q);
        });
        return rows ? Make(ev, a.Rows, a.Columns, (r, c) => a[idx[r], c]) : Make(ev, a.Rows, a.Columns, (r, c) => a[r, idx[c]]);
    }

    // ---- UNIQUE -----------------------------------------------------------------------------------------------------------

    private sealed class LineComparer(CalcArray a, bool byCol) : IEqualityComparer<int>
    {
        public bool Equals(int x, int y)
        {
            int n = byCol ? a.Rows : a.Columns;
            for (int k = 0; k < n; k++)
            {
                CalcValue p = byCol ? a[k, x] : a[x, k];
                CalcValue q = byCol ? a[k, y] : a[y, k];
                if (!LookupSupport.IsMatch(p, q, false) && !(p.IsBlank && q.IsBlank))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(int x)
        {
            var h = new HashCode();
            int n = byCol ? a.Rows : a.Columns;
            for (int k = 0; k < n; k++)
            {
                CalcValue p = byCol ? a[k, x] : a[x, k];
                h.Add(LookupSupport.Class(p));
                switch (p.Kind)
                {
                    case ValueKind.Number:
                        h.Add((float)p.Number);
                        break;
                    case ValueKind.Text:
                        h.Add(p.Text.GetHashCode(StringComparison.OrdinalIgnoreCase));
                        break;
                    case ValueKind.Boolean:
                        h.Add(p.Boolean);
                        break;
                    case ValueKind.Error:
                        h.Add((int)p.Error);
                        break;
                    default:
                        break;
                }
            }

            return h.ToHashCode();
        }
    }

    // Microsoft's UNIQUE page: distinct rows (columns with by_col) in order of first appearance, or with exactly_once only those
    // that occur once. Text compares ignoring case.
    private static CalcValue Unique(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        bool byCol = Flag(args, 1, false, out error);
        if (error.IsError)
        {
            return error;
        }

        bool once = Flag(args, 2, false, out error);
        if (error.IsError)
        {
            return error;
        }

        int n = byCol ? a.Columns : a.Rows;
        var counts = new Dictionary<int, int>(new LineComparer(a, byCol));
        var order = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (counts.TryGetValue(i, out int c))
            {
                counts[i] = c + 1;
            }
            else
            {
                counts[i] = 1;
                order.Add(i);
            }
        }

        if (once)
        {
            order.RemoveAll(i => counts[i] != 1);
        }

        if (order.Count == 0)
        {
            return Err(CalcError.Calc);
        }

        return byCol ? Make(ev, a.Rows, order.Count, (r, c) => a[r, order[c]]) : Make(ev, order.Count, a.Columns, (r, c) => a[order[r], c]);
    }

    // ---- TAKE, DROP, EXPAND -----------------------------------------------------------------------------------------------

    private static bool Count(ReadOnlySpan<CalcValue> args, int i, out bool given, out int n, out CalcValue error)
    {
        given = args.Length > i && !args[i].IsMissing;
        n = 0;
        error = default;
        return !given || LookupSupport.TryInt(args[i], out n, out error);
    }

    // Microsoft's TAKE and DROP pages: a positive count counts from the start, a negative from the end; omitting one dimension keeps
    // it whole. TAKE of more than exists returns all; nothing left is #CALC!.
    private static CalcValue TakeDrop(Evaluator ev, ReadOnlySpan<CalcValue> args, bool take)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        if (!Count(args, 1, out bool rowsGiven, out int rows, out error) || !Count(args, 2, out bool colsGiven, out int cols, out error))
        {
            return error;
        }

        if (!rowsGiven && !colsGiven)
        {
            return Err(CalcError.Value); // open: neither dimension given.
        }

        (int r0, int r1) = Span(a.Rows, rowsGiven, rows, take);
        (int c0, int c1) = Span(a.Columns, colsGiven, cols, take);
        if (r1 <= r0 || c1 <= c0)
        {
            return Err(CalcError.Calc);
        }

        return Make(ev, r1 - r0, c1 - c0, (r, c) => a[r0 + r, c0 + c]);

        static (int Start, int End) Span(int size, bool given, int n, bool take)
        {
            if (!given)
            {
                return (0, size);
            }

            int m = Math.Min(Math.Abs(n), size);
            if (take)
            {
                return n >= 0 ? (0, m) : (size - m, size);
            }

            return n >= 0 ? (m, size) : (0, size - m);
        }
    }

    private static CalcValue Expand(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        if (!Count(args, 1, out bool rg, out int rows, out error) || !Count(args, 2, out bool cg, out int cols, out error))
        {
            return error;
        }

        if (!rg)
        {
            rows = a.Rows;
        }

        if (!cg)
        {
            cols = a.Columns;
        }

        if (rows < a.Rows || cols < a.Columns)
        {
            return Err(CalcError.Value);
        }

        CalcValue pad = args.Length > 3 && !args[3].IsMissing ? ev.ToScalar(args[3]) : Err(CalcError.NA);
        return Make(ev, rows, cols, (r, c) => r < a.Rows && c < a.Columns ? a[r, c] : pad);
    }

    // ---- HSTACK, VSTACK ----------------------------------------------------------------------------------------------------

    private static CalcValue Stack(Evaluator ev, ReadOnlySpan<CalcValue> args, bool horizontal)
    {
        var parts = new CalcArray[args.Length];
        int rows = 0;
        int cols = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (!LookupSupport.TryArray(ev, args[i], out parts[i], out CalcValue error))
            {
                return error;
            }

            if (horizontal)
            {
                rows = Math.Max(rows, parts[i].Rows);
                cols += parts[i].Columns;
            }
            else
            {
                cols = Math.Max(cols, parts[i].Columns);
                rows += parts[i].Rows;
            }
        }

        CalcValue na = Err(CalcError.NA);
        CalcValue[] cells = ev.NewCells(rows, cols);
        ev.ChargeSteps(cells.Length);
        int offset = 0;
        foreach (CalcArray p in parts)
        {
            for (int r = 0; r < p.Rows; r++)
            {
                for (int c = 0; c < p.Columns; c++)
                {
                    int rr = horizontal ? r : offset + r;
                    int cc = horizontal ? offset + c : c;
                    cells[(rr * cols) + cc] = p[r, c];
                }
            }

            offset += horizontal ? p.Columns : p.Rows;
        }

        // Cells no part reaches take #N/A.
        offset = 0;
        foreach (CalcArray p in parts)
        {
            if (horizontal && p.Rows < rows)
            {
                for (int r = p.Rows; r < rows; r++)
                {
                    for (int c = 0; c < p.Columns; c++)
                    {
                        cells[(r * cols) + offset + c] = na;
                    }
                }
            }
            else if (!horizontal && p.Columns < cols)
            {
                for (int r = 0; r < p.Rows; r++)
                {
                    for (int c = p.Columns; c < cols; c++)
                    {
                        cells[((offset + r) * cols) + c] = na;
                    }
                }
            }

            offset += horizontal ? p.Columns : p.Rows;
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    // ---- TOCOL, TOROW, WRAPROWS, WRAPCOLS ------------------------------------------------------------------------------------

    private static CalcValue ToVector(Evaluator ev, ReadOnlySpan<CalcValue> args, bool column)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        int ignore = 0;
        if (args.Length > 1 && !args[1].IsMissing && !LookupSupport.TryInt(args[1], out ignore, out error))
        {
            return error;
        }

        if (ignore is < 0 or > 3)
        {
            return Err(CalcError.Value);
        }

        bool byCol = Flag(args, 2, false, out error);
        if (error.IsError)
        {
            return error;
        }

        var list = new List<CalcValue>();
        void Add(CalcValue v)
        {
            if (((ignore & 1) != 0 && v.IsBlank) || ((ignore & 2) != 0 && v.IsError))
            {
                return;
            }

            list.Add(v);
        }

        if (byCol)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                for (int r = 0; r < a.Rows; r++)
                {
                    Add(a[r, c]);
                }
            }
        }
        else
        {
            for (int r = 0; r < a.Rows; r++)
            {
                for (int c = 0; c < a.Columns; c++)
                {
                    Add(a[r, c]);
                }
            }
        }

        if (list.Count == 0)
        {
            return Err(CalcError.Calc);
        }

        CalcValue[] cells = ev.NewCells(column ? list.Count : 1, column ? 1 : list.Count);
        ev.ChargeSteps(cells.Length);
        list.CopyTo(cells);
        return CalcValue.FromArray(new CalcArray(column ? list.Count : 1, column ? 1 : list.Count, cells));
    }

    private static CalcValue Wrap(Evaluator ev, ReadOnlySpan<CalcValue> args, bool rows)
    {
        if (!LookupSupport.TryArray(ev, args[0], out CalcArray a, out CalcValue error))
        {
            return error;
        }

        if (!LookupSupport.TryInt(args[1], out int wrap, out error))
        {
            return error;
        }

        if (!Vec.TryOf(a, out Vec v))
        {
            return Err(CalcError.Value);
        }

        if (wrap < 1)
        {
            return Err(CalcError.Num);
        }

        CalcValue pad = args.Length > 2 && !args[2].IsMissing ? ev.ToScalar(args[2]) : Err(CalcError.NA);
        int n = v.Length;
        int lines = (n + wrap - 1) / wrap;
        int outRows = rows ? lines : wrap;
        int outCols = rows ? wrap : lines;
        return Make(ev, outRows, outCols, (r, c) =>
        {
            int k = rows ? (r * wrap) + c : (c * wrap) + r;
            return k < n ? v[k] : pad;
        });
    }

    // ---- RANDARRAY ---------------------------------------------------------------------------------------------------------

    // Microsoft's RANDARRAY page: rows, columns, minimum (0), maximum (1), whole numbers or not (FALSE).
    private static CalcValue RandArray(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        double[] p = [1, 1, 0, 1];
        for (int i = 0; i < 4 && i < args.Length; i++)
        {
            if (args[i].IsMissing)
            {
                continue;
            }

            CalcError e = CalcConvert.ToNumber(args[i], out p[i]);
            if (e != CalcError.None)
            {
                return Err(e);
            }
        }

        bool whole = Flag(args, 4, false, out CalcValue error);
        if (error.IsError)
        {
            return error;
        }

        double rowsD = Math.Truncate(p[0]);
        double colsD = Math.Truncate(p[1]);
        if (rowsD < 1 || colsD < 1 || p[2] > p[3])
        {
            return Err(CalcError.Value);
        }

        if (rowsD * colsD > CalcArray.MaxCells)
        {
            return Err(CalcError.Num);
        }

        if (whole && (p[2] != Math.Floor(p[2]) || p[3] != Math.Floor(p[3])))
        {
            return Err(CalcError.Value); // open: non-integer bounds with integer = TRUE.
        }

        double min = p[2];
        double max = p[3];
        return Make(ev, (int)rowsD, (int)colsD, (_, _) =>
        {
            double u = ev.NextRandom();
            return CalcValue.FromNumber(whole ? min + Math.Floor(u * (max - min + 1)) : min + (u * (max - min)));
        });
    }
}
