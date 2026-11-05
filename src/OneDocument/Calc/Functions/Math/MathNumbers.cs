using System;
using System.Numerics;
using System.Text;

namespace RecursiveAI.OneDocument.Calc;

internal static partial class MathFunctions
{
    private static void RegisterTrig(FunctionRegistry r)
    {
        Unary(r, "SIN", x => Fx.Num(Math.Sin(x)));
        Unary(r, "COS", x => Fx.Num(Math.Cos(x)));
        Unary(r, "TAN", x => Fx.Num(Math.Tan(x)));
        Unary(r, "SINH", x => Fx.Num(Math.Sinh(x)));
        Unary(r, "COSH", x => Fx.Num(Math.Cosh(x)));
        Unary(r, "TANH", x => Fx.Num(Math.Tanh(x)));
        Unary(r, "ASIN", x => x is < -1 or > 1 ? Fx.Err(CalcError.Num) : Fx.Num(Math.Asin(x)));
        Unary(r, "ACOS", x => x is < -1 or > 1 ? Fx.Err(CalcError.Num) : Fx.Num(Math.Acos(x)));
        Unary(r, "ATAN", x => Fx.Num(Math.Atan(x)));
        Unary(r, "ASINH", x => Fx.Num(Math.Asinh(x)));
        Unary(r, "ACOSH", x => x < 1 ? Fx.Err(CalcError.Num) : Fx.Num(Math.Acosh(x)));
        Unary(r, "ATANH", x => x is <= -1 or >= 1 ? Fx.Err(CalcError.Num) : Fx.Num(Math.Atanh(x)));
        Unary(r, "COT", x => x == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(1 / Math.Tan(x)));
        Unary(r, "COTH", x => x == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(1 / Math.Tanh(x)));
        Unary(r, "CSC", x => Math.Sin(x) == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(1 / Math.Sin(x)));
        Unary(r, "CSCH", x => x == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(1 / Math.Sinh(x)));
        Unary(r, "SEC", x => Fx.Num(1 / Math.Cos(x)));
        Unary(r, "SECH", x => Fx.Num(1 / Math.Cosh(x)));
        // ACOT is the arccotangent in (0, pi), so ACOT(0) is pi/2 and ACOT of a negative number is above pi/2.
        Unary(r, "ACOT", x => Fx.Num((Math.PI / 2) - Math.Atan(x)));
        Unary(r, "ACOTH", x => Math.Abs(x) <= 1 ? Fx.Err(CalcError.Num) : Fx.Num(0.5 * Math.Log((x + 1) / (x - 1))));
        Binary(r, "ATAN2", (x, y) => x == 0 && y == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(Math.Atan2(y, x)));
    }

    private static readonly double[] Factorials = BuildFactorials();

    private static double[] BuildFactorials()
    {
        var t = new double[171];
        BigInteger f = BigInteger.One;
        t[0] = 1;
        for (int i = 1; i <= 170; i++)
        {
            f *= i;
            t[i] = (double)f;
        }

        return t;
    }

    private static void RegisterNumberTheory(FunctionRegistry r)
    {
        Unary(r, "FACT", x =>
        {
            double n = Math.Truncate(x);
            return n < 0 || n > 170 ? Fx.Err(CalcError.Num) : CalcValue.FromNumber(Factorials[(int)n]);
        });
        Unary(r, "FACTDOUBLE", x =>
        {
            double n = Math.Truncate(x);
            if (n < -1)
            {
                return Fx.Err(CalcError.Num);
            }

            double p = 1;
            for (double i = n; i > 1; i -= 2)
            {
                p *= i;
                if (double.IsInfinity(p))
                {
                    return Fx.Err(CalcError.Num);
                }
            }

            return CalcValue.FromNumber(p);
        });
        Binary(r, "COMBIN", (n, k) => Combin(Math.Truncate(n), Math.Truncate(k), false));
        Binary(r, "COMBINA", (n, k) => Combin(Math.Truncate(n), Math.Truncate(k), true));
        Binary(r, "PERMUT", (n, k) =>
        {
            n = Math.Truncate(n);
            k = Math.Truncate(k);
            if (n <= 0 && k != 0 || k < 0 || k > n)
            {
                return Fx.Err(CalcError.Num);
            }

            double p = 1;
            for (double i = 0; i < k; i++)
            {
                p *= n - i;
                if (double.IsInfinity(p))
                {
                    return Fx.Err(CalcError.Num);
                }
            }

            return CalcValue.FromNumber(p);
        });
        Binary(r, "PERMUTATIONA", (n, k) =>
        {
            n = Math.Truncate(n);
            k = Math.Truncate(k);
            return n < 0 || k < 0 ? Fx.Err(CalcError.Num) : Evaluator.Apply(BinaryOperator.Power, CalcValue.FromNumber(n), CalcValue.FromNumber(k));
        });
        r.Add(FunctionDefinition.Create("MULTINOMIAL", 1, int.MaxValue, Any, Multinomial, repeatLast: true));
        r.Add(FunctionDefinition.Create("GCD", 1, int.MaxValue, Any, (e, a) => GcdLcm(e, a, false), repeatLast: true));
        r.Add(FunctionDefinition.Create("LCM", 1, int.MaxValue, Any, (e, a) => GcdLcm(e, a, true), repeatLast: true));
    }

    private static CalcValue Combin(double n, double k, bool repetition)
    {
        if (n < 0 || k < 0 || (!repetition && k > n))
        {
            return Fx.Err(CalcError.Num);
        }

        if (repetition)
        {
            if (n == 0)
            {
                return k == 0 ? CalcValue.FromNumber(1) : Fx.Err(CalcError.Num);
            }

            n = n + k - 1;
        }

        double m = Math.Min(k, n - k);
        if (m == 0)
        {
            return CalcValue.FromNumber(1);
        }

        // C(n, k) is past 1.8e308 as soon as the smaller side reaches about 1030, whatever n is.
        if (m > 1030)
        {
            return Fx.Err(CalcError.Num);
        }

        if (n < 9e15)
        {
            BigInteger c = BigInteger.One;
            long nn = (long)n;
            for (long i = 1; i <= (long)m; i++)
            {
                c = c * (nn - (long)m + i) / i;
            }

            double d = (double)c;
            return double.IsInfinity(d) ? Fx.Err(CalcError.Num) : CalcValue.FromNumber(d);
        }

        double acc = 1;
        for (double i = 1; i <= m; i++)
        {
            acc = acc * (n - m + i) / i;
        }

        return Fx.Num(acc);
    }

    private static CalcValue Multinomial(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        var s = new ListSink();
        if (Fx.Collect(ev, args, ref s) is { } e)
        {
            return e;
        }

        BigInteger sum = 0;
        BigInteger den = 1;
        foreach (double v in s.Values ?? [])
        {
            double t = Math.Truncate(v);
            if (t < 0 || t > 170)
            {
                return Fx.Err(CalcError.Num);
            }

            sum += (long)t;
            den *= new BigInteger(Factorials[(int)t]);
        }

        if (sum > 170)
        {
            return Fx.Err(CalcError.Num);
        }

        double result = Factorials[(int)sum] / (double)den;
        return Fx.Num(Math.Round(result, MidpointRounding.AwayFromZero) == result ? result : Fx.Round15(result));
    }

    private static CalcValue GcdLcm(Evaluator ev, ReadOnlySpan<CalcValue> args, bool lcm)
    {
        var s = new ListSink();
        if (Fx.Collect(ev, args, ref s) is { } e)
        {
            return e;
        }

        double result = lcm ? 1 : 0;
        foreach (double v in s.Values ?? [])
        {
            double t = Math.Truncate(v);
            if (t < 0 || t >= 9007199254740992.0)
            {
                return Fx.Err(CalcError.Num);
            }

            if (lcm)
            {
                if (t == 0)
                {
                    return CalcValue.FromNumber(0);
                }

                double g = Gcd(result, t);
                result = result / g * t;
                if (result >= 9007199254740992.0)
                {
                    return Fx.Err(CalcError.Num);
                }
            }
            else
            {
                result = Gcd(result, t);
            }
        }

        return CalcValue.FromNumber(result);
    }

    private static double Gcd(double a, double b)
    {
        while (b != 0)
        {
            double t = a % b;
            a = b;
            b = t;
        }

        return a;
    }

    // ---- conversions ---------------------------------------------------------------------------------------------------------

    // ROMAN forms 0..4 (Microsoft's ROMAN reference: 499 is CDXCIX, LDVLIV, XDIX, VDIV, ID). Each form lists the values the numeral may use.
    private static readonly (int Value, string Symbol)[][] RomanForms =
    [
        [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")],
        [(1000, "M"), (950, "LM"), (900, "CM"), (500, "D"), (450, "LD"), (400, "CD"), (100, "C"), (95, "VC"), (90, "XC"), (50, "L"), (45, "VL"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")],
        [(1000, "M"), (990, "XM"), (900, "CM"), (500, "D"), (490, "XD"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")],
        [(1000, "M"), (995, "VM"), (990, "XM"), (900, "CM"), (500, "D"), (495, "VD"), (490, "XD"), (400, "CD"), (100, "C"), (95, "VC"), (90, "XC"), (50, "L"), (45, "VL"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")],
        [(1000, "M"), (999, "IM"), (995, "VM"), (990, "XM"), (950, "LM"), (900, "CM"), (500, "D"), (499, "ID"), (495, "VD"), (490, "XD"), (450, "LD"), (400, "CD"), (100, "C"), (99, "IC"), (95, "VC"), (90, "XC"), (50, "L"), (49, "IL"), (45, "VL"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")],
    ];

    private static CalcValue Roman(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryInt(a[0], out double n, out CalcValue e))
        {
            return e;
        }

        double form = 0;
        if (!Fx.Omitted(a, 1))
        {
            // TRUE means classic (0) and FALSE means the most concise (4).
            if (a[1].Kind == ValueKind.Boolean)
            {
                form = a[1].Boolean ? 0 : 4;
            }
            else if (!Fx.TryInt(a[1], out form, out e))
            {
                return e;
            }
        }

        if (n < 0 || n > 3999 || form < 0 || form > 4)
        {
            return Fx.Err(CalcError.Value);
        }

        int v = (int)n;
        var sb = new StringBuilder();
        foreach ((int value, string symbol) in RomanForms[(int)form])
        {
            while (v >= value)
            {
                sb.Append(symbol);
                v -= value;
            }
        }

        return CalcValue.FromText(sb.ToString());
    }

    private static CalcValue Arabic(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = CalcConvert.ToText(a[0]).Trim();
        bool neg = false;
        if (t.StartsWith('-'))
        {
            neg = true;
            t = t[1..];
        }

        double total = 0;
        double prev = 0;
        for (int i = t.Length - 1; i >= 0; i--)
        {
            double v = char.ToUpperInvariant(t[i]) switch
            {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => -1,
            };
            if (v < 0)
            {
                return Fx.Err(CalcError.Value);
            }

            total += v < prev ? -v : v;
            prev = Math.Max(prev, v);
        }

        return CalcValue.FromNumber(neg ? -total : total);
    }

    private static CalcValue Base(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryInt(a[0], out double n, out CalcValue e) || !Fx.TryInt(a[1], out double radix, out e))
        {
            return e;
        }

        double min = 0;
        if (!Fx.Omitted(a, 2) && !Fx.TryInt(a[2], out min, out e))
        {
            return e;
        }

        if (n < 0 || n >= 9007199254740992.0 || radix < 2 || radix > 36 || min < 0 || min > 255)
        {
            return Fx.Err(CalcError.Num);
        }

        ulong v = (ulong)n;
        var digits = new StringBuilder();
        int rx = (int)radix;
        do
        {
            digits.Insert(0, "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[(int)(v % (ulong)rx)]);
            v /= (ulong)rx;
        }
        while (v > 0);

        while (digits.Length < (int)min)
        {
            digits.Insert(0, '0');
        }

        return CalcValue.FromText(digits.ToString());
    }

    private static CalcValue DecimalFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = CalcConvert.ToText(a[0]);
        if (!Fx.TryInt(a[1], out double radix, out CalcValue e))
        {
            return e;
        }

        if (radix < 2 || radix > 36 || t.Length > 255)
        {
            return Fx.Err(CalcError.Num);
        }

        double total = 0;
        foreach (char ch in t)
        {
            int d = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'z' => ch - 'a' + 10,
                >= 'A' and <= 'Z' => ch - 'A' + 10,
                _ => -1,
            };
            if (d < 0 || d >= radix)
            {
                return Fx.Err(CalcError.Num);
            }

            total = (total * radix) + d;
        }

        return Fx.Num(total);
    }
}
