using System;

namespace RecursiveAI.OneDocument.Calc;

internal static partial class MathFunctions
{
    private static readonly decimal[] Pow10 = BuildPow10();

    private static decimal[] BuildPow10()
    {
        var t = new decimal[29];
        t[0] = 1;
        for (int i = 1; i < t.Length; i++)
        {
            t[i] = t[i - 1] * 10;
        }

        return t;
    }

    private static void RegisterRounding(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("ROUND", 2, 2, V, (_, a) => Digits(a, 0)));
        r.Add(FunctionDefinition.Create("ROUNDDOWN", 2, 2, V, (_, a) => Digits(a, 1)));
        r.Add(FunctionDefinition.Create("ROUNDUP", 2, 2, V, (_, a) => Digits(a, 2)));
        r.Add(FunctionDefinition.Create("TRUNC", 1, 2, V, (_, a) => Digits(a, 1)));
        Unary(r, "INT", x => Fx.Num(Math.Floor(x)));
        Unary(r, "EVEN", x => Fx.Num(x < 0 ? -EvenUp(-x) : EvenUp(x)));
        Unary(r, "ODD", x => Fx.Num(x < 0 ? -OddUp(-x) : OddUp(x)));
        Binary(r, "MROUND", MRound);
        Binary(r, "CEILING", (n, s) => n > 0 && s < 0 ? Fx.Err(CalcError.Num) : s == 0 ? CalcValue.FromNumber(0) : Fx.Num(Multiple(n, s, true)));
        Binary(r, "FLOOR", (n, s) => s == 0 ? (n == 0 ? CalcValue.FromNumber(0) : Fx.Err(CalcError.Div0)) : n > 0 && s < 0 ? Fx.Err(CalcError.Num) : Fx.Num(Multiple(n, s, false)));
        r.Add(FunctionDefinition.Create("CEILING.MATH", 1, 3, V, (_, a) => MathMultiple(a, true)));
        r.Add(FunctionDefinition.Create("FLOOR.MATH", 1, 3, V, (_, a) => MathMultiple(a, false)));
        r.Add(FunctionDefinition.Create("CEILING.PRECISE", 1, 2, V, (_, a) => Precise(a, true)));
        r.Add(FunctionDefinition.Create("ISO.CEILING", 1, 2, V, (_, a) => Precise(a, true)));
        r.Add(FunctionDefinition.Create("FLOOR.PRECISE", 1, 2, V, (_, a) => Precise(a, false)));
    }

    // ROUND, ROUNDDOWN, ROUNDUP and TRUNC work on the 15-digit decimal the cell shows, so ROUND(2.675, 2) is 2.68 as in Excel:
    // converting a double to decimal keeps 15 significant digits.
    private static CalcValue Digits(ReadOnlySpan<CalcValue> a, int mode)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e))
        {
            return e;
        }

        double d = 0;
        if (!Fx.Omitted(a, 1) && !Fx.TryNum(a[1], out d, out e))
        {
            return e;
        }

        return Fx.Num(RoundTo(x, d, mode));
    }

    /// <summary>Rounds <paramref name="x"/> to <paramref name="digitsValue"/> decimal places (negative: left of the point). Mode 0 is half away from zero, 1 toward zero, 2 away from zero.</summary>
    internal static double RoundTo(double x, double digitsValue, int mode)
    {
        if (x == 0 || !double.IsFinite(x))
        {
            return x;
        }

        int digits = (int)Math.Clamp(Math.Truncate(digitsValue), -400, 400);
        if (digits >= 0)
        {
            if (digits > 28 || Math.Abs(x) >= 7.9e27)
            {
                return x;
            }

            decimal m = (decimal)x;
            decimal p = Pow10[digits];
            if (Math.Abs(m) > decimal.MaxValue / p)
            {
                return x;
            }

            return (double)(Snap(m * p, mode) / p);
        }

        int k = -digits;
        if (k <= 28 && Math.Abs(x) < 7.9e27)
        {
            decimal m = (decimal)x;
            decimal p = Pow10[k];
            decimal res = Snap(m / p, mode);
            return Math.Abs(res) > decimal.MaxValue / p ? (double)res * (double)p : (double)(res * p);
        }

        if (k > 308)
        {
            return mode == 2 ? double.PositiveInfinity : 0;
        }

        double pw = Math.Pow(10, k);
        double s = x / pw;
        double rs = mode switch
        {
            0 => Math.Round(s, MidpointRounding.AwayFromZero),
            1 => Math.Truncate(s),
            _ => s > 0 ? Math.Ceiling(s) : Math.Floor(s),
        };
        return rs * pw;
    }

    private static decimal Snap(decimal s, int mode) => mode switch
    {
        0 => Math.Round(s, 0, MidpointRounding.AwayFromZero),
        1 => Math.Truncate(s),
        _ => s > 0 ? Math.Ceiling(s) : Math.Floor(s),
    };

    private static double EvenUp(double x)
    {
        double c = Math.Ceiling(Fx.Round15(x));
        return c % 2 == 0 ? c : c + 1;
    }

    private static double OddUp(double x)
    {
        double c = Math.Ceiling(Fx.Round15(x));
        return c % 2 != 0 ? c : c + 1;
    }

    private static CalcValue MRound(double n, double m)
    {
        if (m == 0 || n == 0)
        {
            return CalcValue.FromNumber(0);
        }

        if ((n < 0) != (m < 0))
        {
            return Fx.Err(CalcError.Num);
        }

        double q = Fx.Round15(n / m);
        return Fx.Num(Fx.Round15(Math.Round(q, MidpointRounding.AwayFromZero) * m));
    }

    // The next multiple of the significance above (up) or below the number. The quotient is rounded to 15 digits first so that
    // CEILING(0.3, 0.1) is 0.3 and not the next step because 0.3/0.1 is 2.9999999999999996 in binary.
    private static double Multiple(double n, double s, bool up)
    {
        double q = Fx.Round15(n / s);
        double k = up ? Math.Ceiling(q) : Math.Floor(q);
        return Fx.Round15(k * s);
    }

    private static CalcValue MathMultiple(ReadOnlySpan<CalcValue> a, bool ceiling)
    {
        if (!Fx.TryNum(a[0], out double n, out CalcValue e))
        {
            return e;
        }

        double s = 1;
        if (!Fx.Omitted(a, 1) && !Fx.TryNum(a[1], out s, out e))
        {
            return e;
        }

        double mode = 0;
        if (!Fx.Omitted(a, 2) && !Fx.TryNum(a[2], out mode, out e))
        {
            return e;
        }

        if (s == 0 || n == 0)
        {
            return CalcValue.FromNumber(0);
        }

        s = Math.Abs(s);
        // ECMA-376 style definition of the Excel 2013 functions: for a negative number a non-zero mode rounds away from zero.
        bool up = ceiling;
        if (n < 0 && mode != 0)
        {
            up = !up;
        }

        return Fx.Num(Multiple(n, s, up));
    }

    private static CalcValue Precise(ReadOnlySpan<CalcValue> a, bool ceiling)
    {
        if (!Fx.TryNum(a[0], out double n, out CalcValue e))
        {
            return e;
        }

        double s = 1;
        if (!Fx.Omitted(a, 1) && !Fx.TryNum(a[1], out s, out e))
        {
            return e;
        }

        if (s == 0 || n == 0)
        {
            return CalcValue.FromNumber(0);
        }

        return Fx.Num(Multiple(n, Math.Abs(s), ceiling));
    }
}
