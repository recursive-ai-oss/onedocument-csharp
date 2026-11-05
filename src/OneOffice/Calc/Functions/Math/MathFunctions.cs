using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>Math and trigonometry: arithmetic, rounding, powers and logarithms, trigonometry, number theory, combinatorics, conversions and random numbers.</summary>
internal static partial class MathFunctions
{
    private static readonly ArgKind[] V = [ArgKind.Value];
    private static readonly ArgKind[] A = [ArgKind.Array];
    private static readonly ArgKind[] AA = [ArgKind.Array, ArgKind.Array];
    private static readonly ArgKind[] Any = [ArgKind.Any];

    public static void Register(FunctionRegistry r)
    {
        RegisterBasic(r);
        RegisterRounding(r);
        RegisterTrig(r);
        RegisterNumberTheory(r);
        RegisterMatrix(r);
        RegisterAggregates(r);
        RegisterRandom(r);
    }

    private static void Unary(FunctionRegistry r, string name, Func<double, CalcValue> f) =>
        r.Add(FunctionDefinition.Create(name, 1, 1, V, (_, a) => Fx.TryNum(a[0], out double x, out CalcValue e) ? f(x) : e));

    private static void Binary(FunctionRegistry r, string name, Func<double, double, CalcValue> f) =>
        r.Add(FunctionDefinition.Create(name, 2, 2, V, (_, a) =>
            !Fx.TryNum(a[0], out double x, out CalcValue e1) ? e1 : !Fx.TryNum(a[1], out double y, out CalcValue e2) ? e2 : f(x, y)));

    private static void RegisterBasic(FunctionRegistry r)
    {
        Unary(r, "ABS", x => Fx.Num(Math.Abs(x)));
        Unary(r, "SIGN", x => Fx.Num(Math.Sign(x)));
        Unary(r, "SQRT", x => x < 0 ? Fx.Err(CalcError.Num) : Fx.Num(Math.Sqrt(x)));
        Unary(r, "SQRTPI", x => x < 0 ? Fx.Err(CalcError.Num) : Fx.Num(Math.Sqrt(x * Math.PI)));
        Unary(r, "EXP", x => Fx.Num(Math.Exp(x)));
        Unary(r, "LN", x => x <= 0 ? Fx.Err(CalcError.Num) : Fx.Num(Math.Log(x)));
        Unary(r, "LOG10", x => x <= 0 ? Fx.Err(CalcError.Num) : Fx.Num(Math.Log10(x)));
        r.Add(FunctionDefinition.Create("LOG", 1, 2, V, Log));
        r.Add(FunctionDefinition.Create("PI", 0, 0, V, (_, _) => CalcValue.FromNumber(Math.PI)));
        r.Add(FunctionDefinition.Create("POWER", 2, 2, V, (_, a) => Evaluator.Apply(BinaryOperator.Power, a[0], a[1])));
        Unary(r, "DEGREES", x => Fx.Num(x * 180 / Math.PI));
        Unary(r, "RADIANS", x => Fx.Num(x * Math.PI / 180));
        Binary(r, "QUOTIENT", (n, d) => d == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(Math.Truncate(n / d)));
        Binary(r, "MOD", Mod);
        r.Add(FunctionDefinition.Create("PRODUCT", 1, int.MaxValue, Any, Product, repeatLast: true));
        r.Add(FunctionDefinition.Create("SUMSQ", 1, int.MaxValue, Any, SumSq, repeatLast: true));
        r.Add(FunctionDefinition.Create("SUMPRODUCT", 1, int.MaxValue, A, SumProduct, repeatLast: true));
        r.Add(FunctionDefinition.Create("SUMX2MY2", 2, 2, AA, (e, a) => PairSum(e, a, (x, y) => (x * x) - (y * y))));
        r.Add(FunctionDefinition.Create("SUMX2PY2", 2, 2, AA, (e, a) => PairSum(e, a, (x, y) => (x * x) + (y * y))));
        r.Add(FunctionDefinition.Create("SUMXMY2", 2, 2, AA, (e, a) => PairSum(e, a, (x, y) => (x - y) * (x - y))));
        r.Add(FunctionDefinition.Create("SERIESSUM", 4, 4, [ArgKind.Value, ArgKind.Value, ArgKind.Value, ArgKind.Array], SeriesSum));
        r.Add(FunctionDefinition.Create("ROMAN", 1, 2, V, Roman));
        r.Add(FunctionDefinition.Create("ARABIC", 1, 1, V, Arabic));
        r.Add(FunctionDefinition.Create("BASE", 2, 3, V, Base));
        r.Add(FunctionDefinition.Create("DECIMAL", 2, 2, V, DecimalFn));
    }

    private static CalcValue Log(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e))
        {
            return e;
        }

        double b = 10;
        if (!Fx.Omitted(a, 1) && !Fx.TryNum(a[1], out b, out e))
        {
            return e;
        }

        if (x <= 0 || b <= 0)
        {
            return Fx.Err(CalcError.Num);
        }

        if (b == 1)
        {
            return Fx.Err(CalcError.Div0);
        }

        return Fx.Num(b == 10 ? Math.Log10(x) : Math.Log(x) / Math.Log(b));
    }

    // MOD(n, d) has the sign of the divisor: n - d * INT(n / d), computed from the exact remainder so large quotients do not lose digits.
    private static CalcValue Mod(double n, double d)
    {
        if (d == 0)
        {
            return Fx.Err(CalcError.Div0);
        }

        double m = n % d;
        if (m != 0 && (m < 0) != (d < 0))
        {
            m += d;
        }

        return Fx.Num(m);
    }

    private static CalcValue Product(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        var s = new ProductSink();
        if (Fx.Collect(ev, args, ref s) is { } e)
        {
            return e;
        }

        return s.Count == 0 ? CalcValue.FromNumber(0) : Fx.Num(s.Total);
    }

    private static CalcValue SumSq(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        var s = new SumSquaresSink();
        if (Fx.Collect(ev, args, ref s) is { } e)
        {
            return e;
        }

        return Fx.Num(s.Total);
    }

    // SUMPRODUCT treats every entry that is not a number as zero; the arrays must have the same dimensions.
    private static CalcValue SumProduct(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        var arrays = new CalcArray[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            CalcArray? a = Fx.AsArray(ev, args[i], out CalcValue err);
            if (a is null)
            {
                return err;
            }

            if (i > 0 && (a.Rows != arrays[0].Rows || a.Columns != arrays[0].Columns))
            {
                return Fx.Err(CalcError.Value);
            }

            arrays[i] = a;
        }

        double total = 0;
        for (int r = 0; r < arrays[0].Rows; r++)
        {
            for (int c = 0; c < arrays[0].Columns; c++)
            {
                double p = 1;
                for (int i = 0; i < arrays.Length; i++)
                {
                    CalcValue v = arrays[i][r, c];
                    if (v.IsError)
                    {
                        return v;
                    }

                    p *= v.Kind == ValueKind.Number ? v.Number : 0;
                }

                total = Evaluator.AddSnapped(total, p);
            }
        }

        return Fx.Num(total);
    }

    // SUMX2MY2 and its siblings skip a pair when either entry is not a number; arrays of different size are #N/A.
    private static CalcValue PairSum(Evaluator ev, ReadOnlySpan<CalcValue> args, Func<double, double, double> f)
    {
        CalcArray? x = Fx.AsArray(ev, args[0], out CalcValue e1);
        if (x is null)
        {
            return e1;
        }

        CalcArray? y = Fx.AsArray(ev, args[1], out CalcValue e2);
        if (y is null)
        {
            return e2;
        }

        if (x.Rows != y.Rows || x.Columns != y.Columns)
        {
            return Fx.Err(CalcError.NA);
        }

        double total = 0;
        for (int r = 0; r < x.Rows; r++)
        {
            for (int c = 0; c < x.Columns; c++)
            {
                CalcValue a = x[r, c];
                CalcValue b = y[r, c];
                if (a.IsError)
                {
                    return a;
                }

                if (b.IsError)
                {
                    return b;
                }

                if (a.Kind == ValueKind.Number && b.Kind == ValueKind.Number)
                {
                    total += f(a.Number, b.Number);
                }
            }
        }

        return Fx.Num(total);
    }

    private static CalcValue SeriesSum(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e) || !Fx.TryNum(a[1], out double n, out e) || !Fx.TryNum(a[2], out double m, out e))
        {
            return e;
        }

        CalcArray? co = Fx.AsArray(ev, a[3], out e);
        if (co is null)
        {
            return e;
        }

        double sum = 0;
        int i = 0;
        for (int r = 0; r < co.Rows; r++)
        {
            for (int c = 0; c < co.Columns; c++)
            {
                CalcValue v = co[r, c];
                if (v.IsError)
                {
                    return v;
                }

                if (v.Kind != ValueKind.Number)
                {
                    return Fx.Err(CalcError.Value);
                }

                sum += v.Number * Math.Pow(x, n + (i * m));
                i++;
            }
        }

        return Fx.Num(sum);
    }
}
