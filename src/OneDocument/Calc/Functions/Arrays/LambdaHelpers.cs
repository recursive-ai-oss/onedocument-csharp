using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>
/// The LAMBDA helper functions: <c>MAP</c>, <c>REDUCE</c>, <c>SCAN</c>, <c>BYROW</c>, <c>BYCOL</c>, <c>MAKEARRAY</c>, <c>ISOMITTED</c>.
/// They are lazy so that a function name written bare (<c>MAP(a, SUM)</c>, <c>REDUCE(0, a, SUM)</c>) can be used as the lambda.
/// </summary>
internal static class LambdaHelpers
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.CreateLazy("MAP", 2, int.MaxValue, Map));
        r.Add(FunctionDefinition.CreateLazy("REDUCE", 3, 3, (c, a) => Fold(c, a, false)));
        r.Add(FunctionDefinition.CreateLazy("SCAN", 3, 3, (c, a) => Fold(c, a, true)));
        r.Add(FunctionDefinition.CreateLazy("BYROW", 2, 2, (c, a) => ByLine(c, a, true)));
        r.Add(FunctionDefinition.CreateLazy("BYCOL", 2, 2, (c, a) => ByLine(c, a, false)));
        r.Add(FunctionDefinition.CreateLazy("MAKEARRAY", 3, 3, MakeArray));
        r.Add(FunctionDefinition.CreateLazy("ISOMITTED", 1, 1, IsOmitted));
    }

    private static CalcValue Err(CalcError e) => CalcValue.FromError(e);

    /// <summary>
    /// Evaluates an argument that should be a function. A bare function name that is not a defined name or a LET/LAMBDA name
    /// (<c>SUM</c>, <c>PERCENTOF</c>) becomes a function value ("eta-reduced"), as the function reference describes.
    /// </summary>
    internal static CalcValue FunctionArg(Evaluator ev, FormulaNode node)
    {
        CalcValue v = ev.Evaluate(node);
        if (v is { Kind: ValueKind.Error, Error: CalcError.Name } && node is NameNode { Prefix: null } name &&
            ev.Registry.TryGet(name.BareName, out FunctionDefinition? def) && def is { Eager: not null })
        {
            return CalcValue.FromLambda(new LambdaValue(def));
        }

        return v;
    }

    /// <summary>
    /// A lambda result in a place that holds one value: a 1 x 1 array unwraps. A larger array or a function makes the whole result
    /// <c>#CALC!</c> in Excel, so this returns false and the caller gives up.
    /// </summary>
    internal static bool TryScalar(Evaluator ev, CalcValue result, out CalcValue scalar)
    {
        CalcValue res = ev.Dereference(result);
        if (res.Kind == ValueKind.Array)
        {
            if (res.Array!.Count != 1)
            {
                scalar = default;
                return false;
            }

            scalar = res.Array[0, 0];
            return true;
        }

        scalar = res;
        return res.Kind != ValueKind.Lambda;
    }

    /// <summary>True when the lambda can take this many arguments.</summary>
    internal static bool Accepts(CalcValue fn, int count) =>
        fn.Lambda is { } l && (l.Function is not null || l.Parameters.Length == count);

    private static CalcValue Data(Evaluator ev, FormulaNode node, out CalcArray array)
    {
        CalcValue v = ev.Evaluate(node);
        if (!LookupSupport.TryArray(ev, v, out array, out CalcValue error))
        {
            array = null!;
            return error;
        }

        return default;
    }

    private static CalcValue Map(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        int n = args.Length - 1;
        var arrays = new CalcArray[n];
        int rows = 1;
        int cols = 1;
        for (int i = 0; i < n; i++)
        {
            CalcValue e = Data(ev, args[i], out arrays[i]);
            if (e.IsError)
            {
                return e;
            }

            rows = Math.Max(rows, arrays[i].Rows);
            cols = Math.Max(cols, arrays[i].Columns);
        }

        CalcValue fn = FunctionArg(ev, args[^1]);
        if (fn.IsError)
        {
            return fn;
        }

        if (!Accepts(fn, n))
        {
            return Err(CalcError.Value);
        }

        CalcValue[] cells = ev.NewCells(rows, cols);
        var call = new CalcValue[n];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                for (int i = 0; i < n; i++)
                {
                    // MAP does not stretch a one-row or one-column array: a cell outside a smaller array is #N/A.
                    call[i] = r < arrays[i].Rows && c < arrays[i].Columns ? arrays[i][r, c] : Err(CalcError.NA);
                }

                if (!TryScalar(ev, ev.Invoke(fn, call), out CalcValue one))
                {
                    return Err(CalcError.Calc);
                }

                cells[(r * cols) + c] = one;
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    // Microsoft's REDUCE and SCAN pages: the lambda takes (accumulator, value); with no initial value the first element starts the
    // accumulation. REDUCE gives the final accumulator, SCAN the array of every intermediate one (each a single value).
    private static CalcValue Fold(Evaluator ev, ReadOnlySpan<FormulaNode> args, bool scan)
    {
        CalcValue init = ev.Evaluate(args[0]);
        if (init.IsError)
        {
            return init;
        }

        CalcValue e = Data(ev, args[1], out CalcArray a);
        if (e.IsError)
        {
            return e;
        }

        CalcValue fn = FunctionArg(ev, args[2]);
        if (fn.IsError)
        {
            return fn;
        }

        if (!Accepts(fn, 2))
        {
            return Err(CalcError.Value);
        }

        bool hasInit = !init.IsMissing;
        CalcValue acc = hasInit ? ev.Dereference(init) : a[0, 0];
        int start = hasInit ? 0 : 1;
        CalcValue[] cells = scan ? ev.NewCells(a.Rows, a.Columns) : [];
        var call = new CalcValue[2];
        if (scan && !hasInit)
        {
            cells[0] = acc;
        }

        for (int k = start; k < a.Count; k++)
        {
            call[0] = acc;
            call[1] = a[k / a.Columns, k % a.Columns];
            acc = ev.Dereference(ev.Invoke(fn, call));
            if (scan)
            {
                if (!TryScalar(ev, acc, out acc))
                {
                    return Err(CalcError.Calc);
                }

                cells[k] = acc;
            }
            else if (acc.Kind == ValueKind.Lambda)
            {
                return Err(CalcError.Calc);
            }
        }

        return scan ? CalcValue.FromArray(new CalcArray(a.Rows, a.Columns, cells)) : acc;
    }

    // Microsoft's BYROW and BYCOL pages: the lambda gets each row (column) as an array and must return one value.
    private static CalcValue ByLine(Evaluator ev, ReadOnlySpan<FormulaNode> args, bool rows)
    {
        CalcValue e = Data(ev, args[0], out CalcArray a);
        if (e.IsError)
        {
            return e;
        }

        CalcValue fn = FunctionArg(ev, args[1]);
        if (fn.IsError)
        {
            return fn;
        }

        if (!Accepts(fn, 1))
        {
            return Err(CalcError.Value);
        }

        int n = rows ? a.Rows : a.Columns;
        int len = rows ? a.Columns : a.Rows;
        CalcValue[] cells = ev.NewCells(rows ? n : 1, rows ? 1 : n);
        var call = new CalcValue[1];
        for (int i = 0; i < n; i++)
        {
            CalcValue[] line = ev.NewCells(rows ? 1 : len, rows ? len : 1);
            for (int k = 0; k < len; k++)
            {
                line[k] = rows ? a[i, k] : a[k, i];
            }

            call[0] = CalcValue.FromArray(new CalcArray(rows ? 1 : len, rows ? len : 1, line));
            if (!TryScalar(ev, ev.Invoke(fn, call), out CalcValue one))
            {
                return Err(CalcError.Calc);
            }

            cells[i] = one;
        }

        return CalcValue.FromArray(new CalcArray(rows ? n : 1, rows ? 1 : n, cells));
    }

    private static CalcValue MakeArray(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        CalcValue rv = ev.ToScalar(ev.Evaluate(args[0]));
        CalcValue cv = ev.ToScalar(ev.Evaluate(args[1]));
        if (rv.IsError)
        {
            return rv;
        }

        if (cv.IsError)
        {
            return cv;
        }

        if (!LookupSupport.TryInt(rv, out int rows, out CalcValue error) || !LookupSupport.TryInt(cv, out int cols, out error))
        {
            return error;
        }

        CalcValue fn = FunctionArg(ev, args[2]);
        if (fn.IsError)
        {
            return fn;
        }

        if (rows < 1 || cols < 1 || !Accepts(fn, 2))
        {
            return Err(CalcError.Value);
        }

        CalcValue[] cells = ev.NewCells(rows, cols);
        var pair = new CalcValue[2];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                pair[0] = CalcValue.FromNumber(r + 1);
                pair[1] = CalcValue.FromNumber(c + 1);
                if (!TryScalar(ev, ev.Invoke(fn, pair), out CalcValue one))
                {
                    return Err(CalcError.Calc);
                }

                cells[(r * cols) + c] = one;
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    // Microsoft's ISOMITTED page: TRUE when the argument of the enclosing LAMBDA was not supplied.
    private static CalcValue IsOmitted(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        CalcValue v = ev.Evaluate(args[0]);
        return CalcValue.FromBoolean(v.IsMissing);
    }
}
