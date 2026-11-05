using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>Logical functions beyond the seed set: <c>XOR</c>, <c>SWITCH</c>, <c>TRUE</c>, <c>FALSE</c>.</summary>
internal static class LogicalFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("TRUE", 0, 0, [], (_, _) => CalcValue.True));
        r.Add(FunctionDefinition.Create("FALSE", 0, 0, [], (_, _) => CalcValue.False));
        r.Add(FunctionDefinition.Create("XOR", 1, int.MaxValue, [ArgKind.Any], Xor, repeatLast: true));
        r.Add(FunctionDefinition.CreateLazy("SWITCH", 3, 254, Switch));
    }

    // XOR is TRUE when an odd number of its logical values are TRUE. Text and blanks inside references and arrays are skipped;
    // with no logical value at all the result is #VALUE!, as for AND and OR.
    private static CalcValue Xor(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        var values = new List<CalcValue>();
        bool any = false;
        int trues = 0;
        for (int i = 0; i < args.Length; i++)
        {
            values.Clear();
            CalcError fe = Fx.Flatten(ev, args[i], values);
            if (fe != CalcError.None)
            {
                return Fx.Err(fe);
            }

            bool direct = args[i].Kind is not (ValueKind.Reference or ValueKind.Array);
            foreach (CalcValue v in values)
            {
                if (v.IsError)
                {
                    return v;
                }

                if (v.IsBlank && (!direct || !v.IsMissing))
                {
                    continue;
                }

                CalcError ce = CalcConvert.ToBoolean(v, out bool b);
                if (ce == CalcError.Value && v.Kind == ValueKind.Text)
                {
                    continue;
                }

                if (ce != CalcError.None)
                {
                    return Fx.Err(ce);
                }

                any = true;
                if (b)
                {
                    trues++;
                }
            }
        }

        return any ? CalcValue.FromBoolean(trues % 2 == 1) : Fx.Err(CalcError.Value);
    }

    // SWITCH(expression, value1, result1, [value2, result2, ...], [default]): the first value equal to the expression picks its result;
    // an array expression is switched element by element; no match and no default is #N/A.
    private static CalcValue Switch(Evaluator ev, ReadOnlySpan<FormulaNode> args)
    {
        CalcValue ex = ev.Dereference(ev.Evaluate(args[0]));
        if (ex.Kind != ValueKind.Array)
        {
            return Pick(ev, args, ex);
        }

        CalcArray a = ex.Array!;
        CalcValue[] cells = ev.NewCells(a.Rows, a.Columns);
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                cells[(r * a.Columns) + c] = ev.ToScalar(Pick(ev, args, a[r, c]));
            }
        }

        return CalcValue.FromArray(new CalcArray(a.Rows, a.Columns, cells));
    }

    private static CalcValue Pick(Evaluator ev, ReadOnlySpan<FormulaNode> args, CalcValue ex)
    {
        if (ex.IsError)
        {
            return ex;
        }

        int pairs = (args.Length - 1) / 2;
        for (int i = 0; i < pairs; i++)
        {
            CalcValue v = ev.ToScalar(ev.Evaluate(args[1 + (2 * i)]));
            if (v.IsError)
            {
                return v;
            }

            if (Evaluator.Compare(ex, v) == 0)
            {
                return ev.Evaluate(args[2 + (2 * i)]);
            }
        }

        return args.Length % 2 == 0 ? ev.Evaluate(args[^1]) : Fx.Err(CalcError.NA);
    }
}
