using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>
/// <c>GROUPBY</c>, <c>PIVOTBY</c> and <c>PERCENTOF</c>, written from Microsoft's function reference pages. The aggregation is one function
/// (a name such as <c>SUM</c> or a LAMBDA); a vector of functions (<c>HSTACK(SUM, COUNT)</c>) is not supported yet. Behaviour the pages
/// leave open is marked <c>// open:</c>.
/// </summary>
internal static class GroupFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("PERCENTOF", 2, 2, [ArgKind.Array, ArgKind.Array], PercentOf));
        r.Add(FunctionDefinition.CreateLazy("GROUPBY", 3, 8, GroupBy));
        r.Add(FunctionDefinition.CreateLazy("PIVOTBY", 4, 11, PivotBy));
    }

    private static CalcValue Opt(Evaluator ev, ReadOnlySpan<FormulaNode> args, int i) => args.Length > i ? ev.Dereference(ev.Evaluate(args[i])) : CalcValue.Missing;

    private static CalcValue Err(CalcError e) => CalcValue.FromError(e);

    private static CalcValue PercentOf(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        double part = 0;
        double whole = 0;
        for (int k = 0; k < 2; k++)
        {
            if (!LookupSupport.TryArray(ev, args[k], out CalcArray a, out CalcValue error))
            {
                return error;
            }

            double sum = 0;
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
                        sum += x.Number;
                    }
                }
            }

            if (k == 0)
            {
                part = sum;
            }
            else
            {
                whole = sum;
            }
        }

        return whole == 0 ? Err(CalcError.Div0) : CalcValue.FromNumber(part / whole);
    }

    // ---- shared pieces ------------------------------------------------------------------------------------------------------

    private readonly record struct Options(bool HasHeaders, bool ShowHeaders);

    private static Options ReadHeaders(CalcValue given, CalcArray values)
    {
        if (!given.IsMissing && given.Kind == ValueKind.Number)
        {
            int h = (int)given.Number;
            return new Options(h is 1 or 3, h is 2 or 3);
        }

        // open: "automatic" — taken as headers present and shown when every cell of the first row of values is text.
        bool text = true;
        for (int c = 0; c < values.Columns; c++)
        {
            text &= values[0, c].Kind == ValueKind.Text;
        }

        return new Options(text && values.Rows > 1, text && values.Rows > 1);
    }

    private static CalcArray Rows(Evaluator ev, CalcArray a, int from, List<int> picked)
    {
        CalcValue[] cells = ev.NewCells(picked.Count, a.Columns);
        ev.ChargeSteps(cells.Length);
        for (int r = 0; r < picked.Count; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                cells[(r * a.Columns) + c] = a[picked[r] + from, c];
            }
        }

        return new CalcArray(Math.Max(1, picked.Count), a.Columns, picked.Count == 0 ? new CalcValue[a.Columns] : cells);
    }

    // The function gets the group's values as a column; a function that takes two arguments (PERCENTOF) also gets every value.
    private static bool Aggregate(Evaluator ev, CalcValue fn, CalcArray subset, CalcArray all, out CalcValue result)
    {
        bool two = fn.Lambda is { } l && (l.Function is { MinArgs: >= 2 } || (l.Function is null && l.Parameters.Length == 2));
        CalcValue[] call = two ? [CalcValue.FromArray(subset), CalcValue.FromArray(all)] : [CalcValue.FromArray(subset)];
        return LambdaHelpers.TryScalar(ev, ev.Invoke(fn, call), out result);
    }

    private static CalcArray Column(Evaluator ev, CalcArray v, int col, List<int> picked)
    {
        CalcValue[] cells = ev.NewCells(Math.Max(1, picked.Count), 1);
        ev.ChargeSteps(cells.Length);
        for (int r = 0; r < picked.Count; r++)
        {
            cells[r] = v[picked[r], col];
        }

        return new CalcArray(cells.Length, 1, cells);
    }

    private static bool KeyEquals(CalcArray keys, int a, int b)
    {
        for (int c = 0; c < keys.Columns; c++)
        {
            CalcValue p = keys[a, c];
            CalcValue q = keys[b, c];
            if (!LookupSupport.IsMatch(p, q, false) && !(p.IsBlank && q.IsBlank))
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareKeys(CalcArray keys, int a, int b, int col, bool desc) => ArrayFunctions.CompareForSort(keys[a, col], keys[b, col], desc);

    private static List<int> Spec(Evaluator ev, CalcValue v)
    {
        var spec = new List<int>();
        if (v.IsMissing)
        {
            return spec;
        }

        CalcArray? a = ev.ToArray(v);
        if (a is null)
        {
            return spec;
        }

        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                if (a[r, c].Kind == ValueKind.Number)
                {
                    spec.Add((int)a[r, c].Number);
                }
            }
        }

        return spec;
    }

    // ---- GROUPBY -------------------------------------------------------------------------------------------------------------

    private static CalcValue GroupBy(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        CalcValue rfv = ev.Evaluate(args[0]);
        CalcValue vv = ev.Evaluate(args[1]);
        if (!LookupSupport.TryArray(ev, rfv, out CalcArray rf, out CalcValue error) || !LookupSupport.TryArray(ev, vv, out CalcArray values, out error))
        {
            return error;
        }

        CalcValue fn = LambdaHelpers.FunctionArg(ev, args[2]);
        if (fn.IsError)
        {
            return fn;
        }

        if (fn.Kind != ValueKind.Lambda)
        {
            return Err(CalcError.Value);
        }

        CalcValue headerArg = Opt(ev, args, 3);
        CalcValue depthArg = Opt(ev, args, 4);
        CalcValue sortArg = Opt(ev, args, 5);
        CalcValue filterArg = Opt(ev, args, 6);
        CalcValue relArg = Opt(ev, args, 7);
        foreach (CalcValue o in new[] { headerArg, depthArg, sortArg, filterArg, relArg })
        {
            if (o.IsError)
            {
                return o;
            }
        }

        Options h = ReadHeaders(headerArg, values);
        int depth = depthArg.IsMissing ? 1 : (int)Math.Truncate(ev.ToScalar(depthArg).Number);
        int skip = h.HasHeaders ? 1 : 0;
        if (rf.Rows != values.Rows)
        {
            return Err(CalcError.Value);
        }

        int n = rf.Rows - skip;
        if (n < 1)
        {
            return Err(CalcError.Calc);
        }

        var include = new List<int>();
        CalcArray? filter = filterArg.IsMissing ? null : ev.ToArray(filterArg);
        for (int i = 0; i < n; i++)
        {
            bool keep = true;
            if (filter is not null)
            {
                int fr = filter.Rows == n ? i : i + skip;
                if (fr >= filter.Rows)
                {
                    return Err(CalcError.Value);
                }

                CalcValue f = filter[fr, 0];
                keep = f.Kind is ValueKind.Boolean or ValueKind.Number && f.Number != 0;
            }

            if (keep)
            {
                include.Add(i);
            }
        }

        if (include.Count == 0)
        {
            return Err(CalcError.Calc);
        }

        int k = rf.Columns;
        int m = values.Columns;
        CalcArray keys = Rows(ev, rf, skip, include);
        var groups = new List<List<int>>(); // positions into `include`
        var firstOf = new List<int>();
        for (int i = 0; i < include.Count; i++)
        {
            int g = firstOf.FindIndex(f => KeyEquals(keys, f, i));
            if (g < 0)
            {
                firstOf.Add(i);
                groups.Add([i]);
            }
            else
            {
                groups[g].Add(i);
            }
        }

        var dataRows = new List<int>();
        foreach (int i in include)
        {
            dataRows.Add(i + skip);
        }

        CalcValue[] agg = new CalcValue[groups.Count * m];
        var allCols = new CalcArray[m];
        for (int j = 0; j < m; j++)
        {
            allCols[j] = Column(ev, values, j, dataRows);
        }

        for (int g = 0; g < groups.Count; g++)
        {
            var rowsOfGroup = new List<int>();
            foreach (int p in groups[g])
            {
                rowsOfGroup.Add(dataRows[p]);
            }

            for (int j = 0; j < m; j++)
            {
                if (!Aggregate(ev, fn, Column(ev, values, j, rowsOfGroup), allCols[j], out agg[(g * m) + j]))
                {
                    return Err(CalcError.Calc);
                }
            }
        }

        // Sort the groups by the requested output columns (negative: descending), then by the keys, then by first appearance.
        List<int> spec = Spec(ev, sortArg);
        if (spec.Count == 0 && sortArg.IsMissing)
        {
            spec.Add(1);
        }

        int[] order = new int[groups.Count];
        for (int g = 0; g < order.Length; g++)
        {
            order[g] = g;
        }

        Array.Sort(order, (a, b) =>
        {
            foreach (int s in spec)
            {
                if (s == 0)
                {
                    continue;
                }

                int col = Math.Abs(s) - 1;
                bool desc = s < 0;
                int c = col < k
                    ? CompareKeys(keys, firstOf[a], firstOf[b], col, desc)
                    : col - k < m ? ArrayFunctions.CompareForSort(agg[(a * m) + col - k], agg[(b * m) + col - k], desc) : 0;
                if (c != 0)
                {
                    return c;
                }
            }

            for (int col = 0; col < k; col++)
            {
                int c = CompareKeys(keys, firstOf[a], firstOf[b], col, false);
                if (c != 0)
                {
                    return c;
                }
            }

            return a.CompareTo(b);
        });

        // Lay out: [header] [grand total on top] groups (with outer-level subtotals) [grand total at the bottom].
        var lines = new List<CalcValue[]>();
        CalcValue empty = CalcValue.EmptyText;
        if (h.ShowHeaders)
        {
            var head = new CalcValue[k + m];
            for (int c = 0; c < k; c++)
            {
                head[c] = h.HasHeaders ? rf[0, c] : empty;
            }

            for (int j = 0; j < m; j++)
            {
                head[k + j] = h.HasHeaders ? values[0, j] : empty;
            }

            lines.Add(head);
        }

        CalcValue[]? grand = null;
        if (depth != 0)
        {
            grand = new CalcValue[k + m];
            grand[0] = CalcValue.FromText("Total");
            for (int c = 1; c < k; c++)
            {
                grand[c] = empty;
            }

            for (int j = 0; j < m; j++)
            {
                if (!Aggregate(ev, fn, allCols[j], allCols[j], out grand[k + j]))
                {
                    return Err(CalcError.Calc);
                }
            }
        }

        if (grand is not null && depth < 0)
        {
            lines.Add(grand);
        }

        bool subtotals = Math.Abs(depth) >= 2 && k > 1;
        for (int oi = 0; oi < order.Length; oi++)
        {
            int g = order[oi];
            var line = new CalcValue[k + m];
            for (int c = 0; c < k; c++)
            {
                line[c] = keys[firstOf[g], c];
            }

            for (int j = 0; j < m; j++)
            {
                line[k + j] = agg[(g * m) + j];
            }

            lines.Add(line);
            bool lastOfOuter = oi == order.Length - 1 || !LookupSupport.IsMatch(keys[firstOf[g], 0], keys[firstOf[order[oi + 1]], 0], false);
            if (subtotals && lastOfOuter)
            {
                var rowsOfOuter = new List<int>();
                for (int p = 0; p < include.Count; p++)
                {
                    if (LookupSupport.IsMatch(keys[p, 0], keys[firstOf[g], 0], false))
                    {
                        rowsOfOuter.Add(dataRows[p]);
                    }
                }

                var sub = new CalcValue[k + m];
                sub[0] = CalcValue.FromText(CalcConvert.ToText(keys[firstOf[g], 0]) + " Total");
                for (int c = 1; c < k; c++)
                {
                    sub[c] = empty;
                }

                for (int j = 0; j < m; j++)
                {
                    if (!Aggregate(ev, fn, Column(ev, values, j, rowsOfOuter), allCols[j], out sub[k + j]))
                    {
                        return Err(CalcError.Calc);
                    }
                }

                lines.Add(sub);
            }
        }

        if (grand is not null && depth > 0)
        {
            lines.Add(grand);
        }

        _ = relArg; // open: field_relationship (hierarchy or table) does not change the layout here.
        return ToArray(ev, lines, k + m);
    }

    private static CalcValue ToArray(Evaluator ev, List<CalcValue[]> lines, int width)
    {
        CalcValue[] cells = ev.NewCells(lines.Count, width);
        ev.ChargeSteps(cells.Length);
        for (int r = 0; r < lines.Count; r++)
        {
            Array.Copy(lines[r], 0, cells, r * width, width);
        }

        return CalcValue.FromArray(new CalcArray(lines.Count, width, cells));
    }

    // ---- PIVOTBY -------------------------------------------------------------------------------------------------------------

    // open: only one column of values is supported, and totals are the grand totals (depth 1); a deeper total depth, relative_to and
    // several value columns are not laid out yet.
    private static CalcValue PivotBy(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        CalcValue rfv = ev.Evaluate(args[0]);
        CalcValue cfv = ev.Evaluate(args[1]);
        CalcValue vv = ev.Evaluate(args[2]);
        if (!LookupSupport.TryArray(ev, rfv, out CalcArray rf, out CalcValue error) || !LookupSupport.TryArray(ev, cfv, out CalcArray cf, out error) ||
            !LookupSupport.TryArray(ev, vv, out CalcArray values, out error))
        {
            return error;
        }

        CalcValue fn = LambdaHelpers.FunctionArg(ev, args[3]);
        if (fn.IsError)
        {
            return fn;
        }

        if (fn.Kind != ValueKind.Lambda || values.Columns != 1 || rf.Rows != values.Rows || cf.Rows != values.Rows)
        {
            return Err(CalcError.Value);
        }

        CalcValue headerArg = Opt(ev, args, 4);
        int rowDepth = Opt(ev, args, 5).IsMissing ? 1 : (int)Math.Truncate(ev.ToScalar(Opt(ev, args, 5)).Number);
        List<int> rowSpec = Spec(ev, Opt(ev, args, 6));
        int colDepth = Opt(ev, args, 7).IsMissing ? 1 : (int)Math.Truncate(ev.ToScalar(Opt(ev, args, 7)).Number);
        List<int> colSpec = Spec(ev, Opt(ev, args, 8));
        CalcValue filterArg = Opt(ev, args, 9);
        Options h = ReadHeaders(headerArg, values);
        int skip = h.HasHeaders ? 1 : 0;
        int n = values.Rows - skip;
        if (n < 1)
        {
            return Err(CalcError.Calc);
        }

        var include = new List<int>();
        CalcArray? filter = filterArg.IsMissing ? null : ev.ToArray(filterArg);
        for (int i = 0; i < n; i++)
        {
            bool keep = true;
            if (filter is not null)
            {
                int fr = filter.Rows == n ? i : i + skip;
                CalcValue f = fr < filter.Rows ? filter[fr, 0] : CalcValue.False;
                keep = f.Kind is ValueKind.Boolean or ValueKind.Number && f.Number != 0;
            }

            if (keep)
            {
                include.Add(i);
            }
        }

        if (include.Count == 0)
        {
            return Err(CalcError.Calc);
        }

        CalcArray rkeys = Rows(ev, rf, skip, include);
        CalcArray ckeys = Rows(ev, cf, skip, include);
        var rowFirst = new List<int>();
        var colFirst = new List<int>();
        var rowOf = new int[include.Count];
        var colOf = new int[include.Count];
        for (int i = 0; i < include.Count; i++)
        {
            int rg = rowFirst.FindIndex(f => KeyEquals(rkeys, f, i));
            if (rg < 0)
            {
                rowFirst.Add(i);
                rg = rowFirst.Count - 1;
            }

            int cg = colFirst.FindIndex(f => KeyEquals(ckeys, f, i));
            if (cg < 0)
            {
                colFirst.Add(i);
                cg = colFirst.Count - 1;
            }

            rowOf[i] = rg;
            colOf[i] = cg;
        }

        int[] ro = SortedKeys(rkeys, rowFirst, rowSpec);
        int[] co = SortedKeys(ckeys, colFirst, colSpec);
        var dataRows = new List<int>();
        foreach (int i in include)
        {
            dataRows.Add(i + skip);
        }

        CalcArray all = Column(ev, values, 0, dataRows);
        int kr = rf.Columns;
        int kc = cf.Columns;
        bool rowTotal = rowDepth != 0;
        bool colTotal = colDepth != 0;
        int width = kr + co.Length + (colTotal ? 1 : 0);
        int height = kc + ro.Length + (rowTotal ? 1 : 0);
        var lines = new List<CalcValue[]>();
        CalcValue empty = CalcValue.EmptyText;
        for (int hr = 0; hr < kc; hr++)
        {
            var head = new CalcValue[width];
            Array.Fill(head, empty);
            for (int c = 0; c < co.Length; c++)
            {
                head[kr + c] = ckeys[colFirst[co[c]], hr];
            }

            if (colTotal && hr == 0)
            {
                head[width - 1] = CalcValue.FromText("Total");
            }

            lines.Add(head);
        }

        CalcValue Cell(Func<int, bool> pick)
        {
            var rows = new List<int>();
            for (int i = 0; i < include.Count; i++)
            {
                if (pick(i))
                {
                    rows.Add(dataRows[i]);
                }
            }

            if (rows.Count == 0)
            {
                return empty;
            }

            return Aggregate(ev, fn, Column(ev, values, 0, rows), all, out CalcValue res) ? res : Err(CalcError.Calc);
        }

        for (int ri = 0; ri < ro.Length; ri++)
        {
            var line = new CalcValue[width];
            for (int c = 0; c < kr; c++)
            {
                line[c] = rkeys[rowFirst[ro[ri]], c];
            }

            for (int ci = 0; ci < co.Length; ci++)
            {
                line[kr + ci] = Cell(i => rowOf[i] == ro[ri] && colOf[i] == co[ci]);
            }

            if (colTotal)
            {
                line[width - 1] = Cell(i => rowOf[i] == ro[ri]);
            }

            lines.Add(line);
        }

        if (rowTotal)
        {
            var line = new CalcValue[width];
            Array.Fill(line, empty);
            line[0] = CalcValue.FromText("Total");
            for (int ci = 0; ci < co.Length; ci++)
            {
                line[kr + ci] = Cell(i => colOf[i] == co[ci]);
            }

            if (colTotal)
            {
                line[width - 1] = Cell(_ => true);
            }

            lines.Add(line);
        }

        _ = height;
        return ToArray(ev, lines, width);
    }

    private static int[] SortedKeys(CalcArray keys, List<int> first, List<int> spec)
    {
        int[] order = new int[first.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        if (spec.Count == 0)
        {
            spec = [1];
        }

        Array.Sort(order, (a, b) =>
        {
            foreach (int s in spec)
            {
                int col = Math.Abs(s) - 1;
                if (s != 0 && col < keys.Columns)
                {
                    int c = CompareKeys(keys, first[a], first[b], col, s < 0);
                    if (c != 0)
                    {
                        return c;
                    }
                }
            }

            return a.CompareTo(b);
        });
        return order;
    }
}
