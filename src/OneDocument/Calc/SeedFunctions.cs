using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>The functions that prove the registry contract: logic, text, lookup, array construction and the lambda helpers. Function families add their own registration methods beside it.</summary>
internal static class SeedFunctions
{
    private static readonly ArgKind[] V = [ArgKind.Value];
    private static readonly ArgKind[] A = [ArgKind.Array];
    private static readonly ArgKind[] Any = [ArgKind.Any];

    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("SUM", 1, int.MaxValue, Any, Sum, repeatLast: true));
        r.Add(FunctionDefinition.Create("AND", 1, int.MaxValue, Any, (c, a) => Logical(c, a, true), repeatLast: true));
        r.Add(FunctionDefinition.Create("OR", 1, int.MaxValue, Any, (c, a) => Logical(c, a, false), repeatLast: true));
        r.Add(FunctionDefinition.Create("NOT", 1, 1, V, Not));
        r.Add(FunctionDefinition.Create("CONCAT", 1, int.MaxValue, A, Concat, repeatLast: true));
        r.Add(FunctionDefinition.Create("ISERROR", 1, 1, V, (_, a) => CalcValue.FromBoolean(a[0].IsError), FunctionTraits.HandlesErrors));
        r.Add(FunctionDefinition.Create("ROWS", 1, 1, Any, (c, a) => Extent(c, a[0], true)));
        r.Add(FunctionDefinition.Create("COLUMNS", 1, 1, Any, (c, a) => Extent(c, a[0], false)));
        r.Add(FunctionDefinition.Create("INDEX", 2, 4, [ArgKind.Any, ArgKind.Value, ArgKind.Value, ArgKind.Value], Index));
        r.Add(FunctionDefinition.Create("SEQUENCE", 1, 4, V, Sequence, repeatLast: true));
        r.Add(FunctionDefinition.Create("TEXT", 2, 2, V, Text));
        r.Add(FunctionDefinition.CreateLazy("IF", 2, 3, If));
        r.Add(FunctionDefinition.CreateLazy("IFS", 2, int.MaxValue, Ifs));
        r.Add(FunctionDefinition.CreateLazy("CHOOSE", 2, int.MaxValue, Choose));
        r.Add(FunctionDefinition.CreateLazy("IFERROR", 2, 2, (c, a) => IfError(c, a, false)));
        r.Add(FunctionDefinition.CreateLazy("IFNA", 2, 2, (c, a) => IfError(c, a, true)));
        r.Add(FunctionDefinition.CreateLazy("LET", 3, int.MaxValue, Let));
        r.Add(FunctionDefinition.CreateLazy("LAMBDA", 1, int.MaxValue, Lambda));
        r.Add(FunctionDefinition.CreateLazy("SINGLE", 1, 1, (c, a) => c.ImplicitIntersect(c.Evaluate(a[0]))));
        r.Add(FunctionDefinition.CreateLazy("ANCHORARRAY", 1, 1, (c, a) => c.Spill(c.Evaluate(a[0]))));
    }

    private static CalcValue Err(CalcError e) => CalcValue.FromError(e);

    // SUM adds numbers found in references and arrays, ignoring text and booleans there, but coerces its direct arguments:
    // SUM("3", TRUE) is 4 and SUM("x") is #VALUE! (Microsoft's SUM function reference).
    private static CalcValue Sum(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        double sum = 0;
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
                        if (AddCells(b, ref sum) is { } e1)
                        {
                            return e1;
                        }
                    }

                    break;
                case ValueKind.Array:
                    if (AddCells(v.Array!, ref sum) is { } e2)
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

                    sum = Evaluator.AddSnapped(sum, d);
                    break;
            }
        }

        return double.IsFinite(sum) ? CalcValue.FromNumber(sum) : Err(CalcError.Num);
    }

    private static CalcValue? AddCells(CalcArray a, ref double sum)
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
                    sum = Evaluator.AddSnapped(sum, x.Number);
                }
            }
        }

        return null;
    }

    private static CalcValue Logical(Evaluator ev, ReadOnlySpan<CalcValue> args, bool and)
    {
        bool result = and;
        bool any = false;
        for (int i = 0; i < args.Length; i++)
        {
            CalcValue v = args[i];
            if (v.Kind == ValueKind.Reference)
            {
                if (!ev.TryReadAreas(v.Reference!, out CalcArray[] blocks, out CalcError re))
                {
                    return Err(re);
                }

                foreach (CalcArray b in blocks)
                {
                    if (Fold(b, and, ref result, ref any) is { } e)
                    {
                        return e;
                    }
                }
            }
            else if (v.Kind == ValueKind.Array)
            {
                if (Fold(v.Array!, and, ref result, ref any) is { } e)
                {
                    return e;
                }
            }
            else
            {
                if (v.IsError)
                {
                    return v;
                }

                if (v.IsBlank && !v.IsMissing)
                {
                    continue;
                }

                // Text other than "TRUE" and "FALSE" is skipped, as it is inside references: OR("x",TRUE) is TRUE.
                CalcError ce = CalcConvert.ToBoolean(v, out bool b);
                if (ce == CalcError.Value && v.Kind == ValueKind.Text)
                {
                    continue;
                }

                if (ce != CalcError.None)
                {
                    return Err(ce);
                }

                any = true;
                result = and ? result && b : result || b;
            }
        }

        return any ? CalcValue.FromBoolean(result) : Err(CalcError.Value);
    }

    private static CalcValue? Fold(CalcArray a, bool and, ref bool result, ref bool any)
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

                if (x.Kind is ValueKind.Boolean or ValueKind.Number)
                {
                    bool b = x.Number != 0;
                    any = true;
                    result = and ? result && b : result || b;
                }
            }
        }

        return null;
    }

    private static CalcValue Not(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcError e = CalcConvert.ToBoolean(a[0], out bool b);
        return e != CalcError.None ? Err(e) : CalcValue.FromBoolean(!b);
    }

    private static CalcValue Concat(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            CalcValue v = args[i];
            if (v.Kind == ValueKind.Array)
            {
                CalcArray a = v.Array!;
                for (int r = 0; r < a.Rows; r++)
                {
                    for (int c = 0; c < a.Columns; c++)
                    {
                        CalcValue x = a[r, c];
                        if (x.IsError)
                        {
                            return x;
                        }

                        sb.Append(CalcConvert.ToText(x));
                    }
                }
            }
            else if (v.IsError)
            {
                return v;
            }
            else if (v.Kind == ValueKind.Lambda)
            {
                return Err(CalcError.Value);
            }
            else
            {
                sb.Append(CalcConvert.ToText(v));
            }
        }

        return sb.Length > 32767 ? Err(CalcError.Value) : CalcValue.FromText(sb.ToString());
    }

    private static CalcValue Extent(Evaluator ev, CalcValue v, bool rows)
    {
        switch (v.Kind)
        {
            case ValueKind.Reference:
                CellArea a = v.Reference!.First;
                if (a.LastSheet is not null && v.Reference.AreaCount == 1 && false)
                {
                    return Err(CalcError.Ref);
                }

                return CalcValue.FromNumber(rows ? a.Rows : a.Columns);
            case ValueKind.Array:
                return CalcValue.FromNumber(rows ? v.Array!.Rows : v.Array!.Columns);
            case ValueKind.Error:
                return v;
            case ValueKind.Lambda:
                return Err(CalcError.Value);
            default:
                return CalcValue.FromNumber(1);
        }
    }

    private static CalcValue Index(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        CalcValue src = args[0];
        if (src.IsError)
        {
            return src;
        }

        CalcError e = CalcConvert.ToNumber(args[1], out double rowD);
        if (e != CalcError.None)
        {
            return Err(e);
        }

        double colD = 0;
        bool colGiven = args.Length > 2 && !args[2].IsMissing;
        if (colGiven)
        {
            e = CalcConvert.ToNumber(args[2], out colD);
            if (e != CalcError.None)
            {
                return Err(e);
            }
        }

        int areaNum = 1;
        if (args.Length > 3 && !args[3].IsMissing)
        {
            e = CalcConvert.ToNumber(args[3], out double an);
            if (e != CalcError.None)
            {
                return Err(e);
            }

            areaNum = (int)Math.Truncate(an);
        }

        int row = (int)Math.Truncate(rowD);
        int col = (int)Math.Truncate(colD);
        CellArea area;
        CalcArray? arr = null;
        CalcReference? reference = src.Reference;
        if (reference is not null)
        {
            if (areaNum < 1 || areaNum > reference.AreaCount)
            {
                return Err(CalcError.Ref);
            }

            area = reference.Areas[areaNum - 1];
        }
        else
        {
            arr = src.Kind == ValueKind.Array ? src.Array : CalcArray.OfValue(src);
            area = new CellArea(null, null, 1, 1, arr!.Rows, arr.Columns);
        }

        int rows = area.Rows;
        int cols = area.Columns;
        if (!colGiven && args.Length <= 2 || (!colGiven && args.Length > 2))
        {
            // A single row or column takes its position from the one index given (Microsoft's INDEX reference).
            if (rows == 1)
            {
                col = row;
                row = 1;
                if (col == 0 && rowD == 0)
                {
                    row = 0;
                }
            }
        }

        if (row < 0 || col < 0)
        {
            return Err(CalcError.Value);
        }

        if (row > rows || col > cols)
        {
            return Err(CalcError.Ref);
        }

        int r1 = row == 0 ? 1 : row;
        int r2 = row == 0 ? rows : row;
        int c1 = col == 0 ? 1 : col;
        int c2 = col == 0 ? cols : col;
        if (reference is not null)
        {
            return CalcValue.FromReference(new CalcReference([new CellArea(area.Sheet, area.LastSheet, area.Row1 + r1 - 1, area.Column1 + c1 - 1, area.Row1 + r2 - 1, area.Column1 + c2 - 1)]));
        }

        if (r1 == r2 && c1 == c2)
        {
            return arr![r1 - 1, c1 - 1];
        }

        CalcValue[] cells = ev.NewCells(r2 - r1 + 1, c2 - c1 + 1);
        int k = 0;
        for (int r = r1; r <= r2; r++)
        {
            for (int c = c1; c <= c2; c++)
            {
                cells[k++] = arr![r - 1, c - 1];
            }
        }

        return CalcValue.FromArray(new CalcArray(r2 - r1 + 1, c2 - c1 + 1, cells));
    }

    private static CalcValue Sequence(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        double[] p = [0, 1, 1, 1];
        for (int i = 0; i < args.Length; i++)
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

        double rowsD = Math.Truncate(p[0]);
        double colsD = Math.Truncate(p[1]);
        if (rowsD < 0 || colsD < 0)
        {
            return Err(CalcError.Value);
        }

        if (rowsD < 1 || colsD < 1)
        {
            return Err(CalcError.Calc);
        }

        if (rowsD * colsD > CalcArray.MaxCells)
        {
            return Err(CalcError.Num);
        }

        CalcValue[] cells = ev.NewCells((long)rowsD, (long)colsD);
        double v = p[2];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = CalcValue.FromNumber(p[2] + (p[3] * i));
        }

        _ = v;
        return CalcValue.FromArray(new CalcArray((int)rowsD, (int)colsD, cells));
    }

    private static CalcValue Text(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        CalcValue v = args[0];
        string code = CalcConvert.ToText(args[1]);
        if (code.Length == 0)
        {
            return CalcValue.EmptyText;
        }

        NumberFormats.NumberFormat? f = ev.GetFormat(code);
        if (f is null)
        {
            return Err(CalcError.Value);
        }

        var opts = new NumberFormats.FormatOptions(ev.Options.DateSystem, ev.Options.Culture);
        switch (v.Kind)
        {
            case ValueKind.Number:
                return CalcValue.FromText(f.Format(v.Number, opts));
            case ValueKind.Blank:
                return CalcValue.FromText(f.Format(0, opts));
            case ValueKind.Boolean:
                return CalcValue.FromText(v.Boolean ? "TRUE" : "FALSE");
            default:
                if (CalcConvert.TextToNumber(v.Text, out double d))
                {
                    return CalcValue.FromText(f.Format(d, opts));
                }

                return CalcValue.FromText(f.FormatText(v.Text));
        }
    }

    // ---- lazy ---------------------------------------------------------------------------------------------------------------

    private static CalcValue Cond(Evaluator ev, FormulaNode n)
    {
        CalcValue v = ev.Evaluate(n);
        if (v.Kind == ValueKind.Reference && ev.Options.LegacyImplicitIntersection)
        {
            return ev.ImplicitIntersect(v);
        }

        return ev.Dereference(v);
    }

    private static CalcValue If(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        CalcValue cond = Cond(ev, args[0]);
        if (cond.IsError)
        {
            return cond;
        }

        if (cond.Kind == ValueKind.Array)
        {
            CalcArray ca = cond.Array!;
            CalcValue t = args[1] is MissingNode ? CalcValue.FromNumber(0) : ev.Dereference(ev.Evaluate(args[1]));
            CalcValue f = args.Length < 3 ? CalcValue.False : args[2] is MissingNode ? CalcValue.FromNumber(0) : ev.Dereference(ev.Evaluate(args[2]));
            CalcArray ta = t.Array ?? CalcArray.OfValue(t);
            CalcArray fa = f.Array ?? CalcArray.OfValue(f);
            int rows = Math.Max(ca.Rows, Math.Max(ta.Rows, fa.Rows));
            int cols = Math.Max(ca.Columns, Math.Max(ta.Columns, fa.Columns));
            CalcValue[] cells = ev.NewCells(rows, cols);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    CalcValue cv = ca.Broadcasting(r, c);
                    if (cv.IsError)
                    {
                        cells[(r * cols) + c] = cv;
                        continue;
                    }

                    CalcError e = CalcConvert.ToBoolean(cv, out bool b);
                    cells[(r * cols) + c] = e != CalcError.None ? Err(e) : b ? ta.Broadcasting(r, c) : fa.Broadcasting(r, c);
                }
            }

            return CalcValue.FromArray(new CalcArray(rows, cols, cells));
        }

        if (cond.Kind == ValueKind.Lambda)
        {
            return Err(CalcError.Value);
        }

        CalcError ce = CalcConvert.ToBoolean(cond, out bool pick);
        if (ce != CalcError.None)
        {
            return Err(ce);
        }

        if (pick)
        {
            return args[1] is MissingNode ? CalcValue.FromNumber(0) : ev.Evaluate(args[1]);
        }

        if (args.Length < 3)
        {
            return CalcValue.False;
        }

        return args[2] is MissingNode ? CalcValue.FromNumber(0) : ev.Evaluate(args[2]);
    }

    private static CalcValue Ifs(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        if (args.Length % 2 != 0)
        {
            return Err(CalcError.Value);
        }

        for (int i = 0; i < args.Length; i += 2)
        {
            CalcValue c = Cond(ev, args[i]);
            if (c.IsError)
            {
                return c;
            }

            if (c.Kind == ValueKind.Array)
            {
                return IfsArray(ev, args, i, c.Array!);
            }

            CalcError e = CalcConvert.ToBoolean(c, out bool b);
            if (e != CalcError.None)
            {
                return Err(e);
            }

            if (b)
            {
                return ev.Evaluate(args[i + 1]);
            }
        }

        return Err(CalcError.NA);
    }

    // An array condition makes IFS work element by element over the remaining pairs.
    private static CalcValue IfsArray(Evaluator ev, ReadOnlySpan<FormulaNode> args, int first, CalcArray firstCond)
    {
        int pairs = (args.Length - first) / 2;
        var conds = new CalcArray[pairs];
        var vals = new CalcArray[pairs];
        int rows = 1;
        int cols = 1;
        for (int k = 0; k < pairs; k++)
        {
            CalcValue c = k == 0 ? CalcValue.FromArray(firstCond) : Cond(ev, args[first + (2 * k)]);
            CalcValue v = ev.Dereference(ev.Evaluate(args[first + (2 * k) + 1]));
            conds[k] = c.Array ?? CalcArray.OfValue(c);
            vals[k] = v.Array ?? CalcArray.OfValue(v);
            rows = Math.Max(rows, Math.Max(conds[k].Rows, vals[k].Rows));
            cols = Math.Max(cols, Math.Max(conds[k].Columns, vals[k].Columns));
        }

        CalcValue[] cells = ev.NewCells(rows, cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                CalcValue pick = Err(CalcError.NA);
                for (int k = 0; k < pairs; k++)
                {
                    CalcValue cv = conds[k].Broadcasting(r, c);
                    if (cv.IsError)
                    {
                        pick = cv;
                        break;
                    }

                    CalcError e = CalcConvert.ToBoolean(cv, out bool b);
                    if (e != CalcError.None)
                    {
                        pick = Err(e);
                        break;
                    }

                    if (b)
                    {
                        pick = vals[k].Broadcasting(r, c);
                        break;
                    }
                }

                cells[(r * cols) + c] = pick;
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    private static CalcValue Choose(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        CalcValue idx = Cond(ev, args[0]);
        if (idx.IsError)
        {
            return idx;
        }

        if (idx.Kind == ValueKind.Array)
        {
            CalcArray ia = idx.Array!;
            var choices = new CalcArray[args.Length - 1];
            int rows = ia.Rows;
            int cols = ia.Columns;
            for (int k = 1; k < args.Length; k++)
            {
                CalcValue v = ev.Dereference(ev.Evaluate(args[k]));
                choices[k - 1] = v.Array ?? CalcArray.OfValue(v);
                rows = Math.Max(rows, choices[k - 1].Rows);
                cols = Math.Max(cols, choices[k - 1].Columns);
            }

            CalcValue[] cells = ev.NewCells(rows, cols);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    CalcValue iv = ia.Broadcasting(r, c);
                    CalcError ie = iv.IsError ? iv.Error : CalcConvert.ToNumber(iv, out double dd);
                    int n = ie == CalcError.None ? (int)Math.Floor(CalcConvert.ToNumber(iv, out double d2) == CalcError.None ? d2 : 0) : 0;
                    cells[(r * cols) + c] = ie != CalcError.None ? Err(ie) : n < 1 || n > choices.Length ? Err(CalcError.Value) : choices[n - 1].Broadcasting(r, c);
                }
            }

            return CalcValue.FromArray(new CalcArray(rows, cols, cells));
        }

        CalcError e = CalcConvert.ToNumber(idx, out double d);
        if (e != CalcError.None)
        {
            return Err(e);
        }

        int i = (int)Math.Floor(d);
        return i < 1 || i >= args.Length ? Err(CalcError.Value) : ev.Evaluate(args[i]);
    }

    private static CalcValue IfError(Evaluator ev, ReadOnlySpan<FormulaNode> args, bool naOnly)
    {
        CalcValue v = ev.Dereference(ev.Evaluate(args[0]));
        if (v.Kind == ValueKind.Array)
        {
            CalcArray a = v.Array!;
            bool any = false;
            for (int r = 0; r < a.Rows && !any; r++)
            {
                for (int c = 0; c < a.Columns; c++)
                {
                    any |= Hit(a[r, c], naOnly);
                }
            }

            if (!any)
            {
                return v;
            }

            CalcValue alt = ev.Dereference(ev.Evaluate(args[1]));
            CalcArray aa = alt.Array ?? CalcArray.OfValue(alt);
            CalcValue[] cells = ev.NewCells(a.Rows, a.Columns);
            for (int r = 0; r < a.Rows; r++)
            {
                for (int c = 0; c < a.Columns; c++)
                {
                    CalcValue x = a[r, c];
                    cells[(r * a.Columns) + c] = Hit(x, naOnly) ? aa.Broadcasting(r, c) : x;
                }
            }

            return CalcValue.FromArray(new CalcArray(a.Rows, a.Columns, cells));
        }

        return Hit(v, naOnly) ? ev.Evaluate(args[1]) : v;

        static bool Hit(CalcValue x, bool na) => x.IsError && (!na || x.Error == CalcError.NA);
    }

    private static string? NameOf(FormulaNode n) => n is NameNode { Prefix: null } nn ? nn.BareName : null;

    private static CalcValue Let(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        if (args.Length % 2 == 0)
        {
            return Err(CalcError.Value);
        }

        Scope? saved = ev.CurrentScope;
        try
        {
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                string? name = NameOf(args[i]);
                if (name is null)
                {
                    return Err(CalcError.Name);
                }

                CalcValue v = ev.Evaluate(args[i + 1]);
                ev.CurrentScope = Evaluator.Bind(name, v, ev.CurrentScope);
            }

            return ev.Evaluate(args[^1]);
        }
        finally
        {
            ev.CurrentScope = saved;
        }
    }

    private static CalcValue Lambda(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        var names = new List<string>(args.Length - 1);
        for (int i = 0; i < args.Length - 1; i++)
        {
            string? name = NameOf(args[i]);
            if (name is null)
            {
                return Err(CalcError.Value);
            }

            names.Add(name);
        }

        return ev.MakeLambda([.. names], args[^1]);
    }
}
