using System;
using System.Globalization;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>The engineering functions: base conversion, bit operations, <c>DELTA</c>/<c>GESTEP</c>, the error function, Bessel functions, <c>CONVERT</c> and complex numbers.</summary>
internal static partial class EngineeringFunctions
{
    private static CalcValue Num(double d) => Fx.Num(d);
    private static CalcValue NumError => Fx.Err(CalcError.Num);

    public static void Register(FunctionRegistry r)
    {
        // Base conversions: X2Y with an optional `places` for the textual results.
        foreach ((string src, int radix) in new[] { ("BIN", 2), ("OCT", 8), ("HEX", 16), ("DEC", 10) })
        {
            foreach ((string dst, int toRadix) in new[] { ("BIN", 2), ("OCT", 8), ("HEX", 16), ("DEC", 10) })
            {
                if (src == dst)
                {
                    continue;
                }

                int fromRadix = radix, toRadix2 = toRadix;
                r.Add(FunctionDefinition.Create($"{src}2{dst}", 1, toRadix == 10 ? 1 : 2, [], (_, a) => ConvertBase(a, fromRadix, toRadix2)));
            }
        }

        r.Add(FunctionDefinition.Create("BITAND", 2, 2, [], (_, a) => Bit(a, 0)));
        r.Add(FunctionDefinition.Create("BITOR", 2, 2, [], (_, a) => Bit(a, 1)));
        r.Add(FunctionDefinition.Create("BITXOR", 2, 2, [], (_, a) => Bit(a, 2)));
        r.Add(FunctionDefinition.Create("BITLSHIFT", 2, 2, [], (_, a) => Shift(a, false)));
        r.Add(FunctionDefinition.Create("BITRSHIFT", 2, 2, [], (_, a) => Shift(a, true)));
        r.Add(FunctionDefinition.Create("DELTA", 1, 2, [], Delta));
        r.Add(FunctionDefinition.Create("GESTEP", 1, 2, [], GeStep));
        r.Add(FunctionDefinition.Create("ERF", 1, 2, [], ErfFunction));
        r.Add(FunctionDefinition.Create("ERF.PRECISE", 1, 1, [], ErfFunction));
        r.Add(FunctionDefinition.Create("ERFC", 1, 1, [], ErfcFunction));
        r.Add(FunctionDefinition.Create("ERFC.PRECISE", 1, 1, [], ErfcFunction));
        RegisterBessel(r);
        RegisterConvert(r);
        RegisterComplex(r);
    }

    // [MS-XLSX]/Excel: 10 digits at most; a 10-digit number whose top bit is set is negative (two's complement of 10, 30 or 40 bits).
    private static int BitsOf(int radix) => radix switch { 2 => 10, 8 => 30, _ => 40 };

    private static bool ParseText(in CalcValue v, int radix, out long value, out CalcValue error)
    {
        value = 0;
        error = default;
        string s;
        switch (v.Kind)
        {
            case ValueKind.Boolean:
                error = Fx.Err(CalcError.Value);
                return false;
            case ValueKind.Blank:
                s = "0";
                break;
            case ValueKind.Number:
                s = CalcConvert.ToText(v);
                break;
            default:
                s = v.Text;
                break;
        }

        if (s.Length is 0 or > 10)
        {
            error = NumError;
            return false;
        }

        long acc = 0;
        foreach (char c in s)
        {
            int d = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => 99,
            };
            if (d >= radix)
            {
                error = NumError;
                return false;
            }

            acc = (acc * radix) + d;
        }

        int bits = BitsOf(radix);
        if (s.Length == 10 && acc >= 1L << (bits - 1))
        {
            acc -= 1L << bits;
        }

        value = acc;
        return true;
    }

    private static CalcValue ConvertBase(ReadOnlySpan<CalcValue> a, int from, int to)
    {
        long value;
        if (from == 10)
        {
            CalcError e = CalcConvert.ToNumber(a[0], out double d);
            if (e != CalcError.None)
            {
                return Fx.Err(e);
            }

            d = Math.Truncate(d);
            if (Math.Abs(d) > 1e15)
            {
                return NumError;
            }

            value = (long)d;
        }
        else if (!ParseText(a[0], from, out value, out CalcValue err))
        {
            return err;
        }

        if (to == 10)
        {
            return Num(value);
        }

        int bits = BitsOf(to);
        long limit = 1L << (bits - 1);
        if (value < -limit || value >= limit)
        {
            return NumError;
        }

        int places = 0;
        if (!Fx.Omitted(a, 1))
        {
            CalcError pe = CalcConvert.ToNumber(a[1], out double p);
            if (pe != CalcError.None)
            {
                return Fx.Err(pe);
            }

            p = Math.Truncate(p);
            if (p < 1 || p > 10)
            {
                return NumError;
            }

            places = (int)p;
        }

        bool negative = value < 0;
        long magnitude = negative ? value + (1L << bits) : value;
        string text = ToRadix(magnitude, to);
        if (!negative)
        {
            if (text.Length > places && places != 0)
            {
                return NumError;
            }

            if (places > text.Length)
            {
                text = new string('0', places - text.Length) + text;
            }
        }

        return CalcValue.FromText(text);
    }

    private static string ToRadix(long v, int radix)
    {
        if (v == 0)
        {
            return "0";
        }

        var buf = new char[40]; // at most 40 binary digits
        int i = buf.Length;
        while (v > 0)
        {
            int d = (int)(v % radix);
            buf[--i] = (char)(d < 10 ? '0' + d : 'A' + d - 10);
            v /= radix;
        }

        return new string(buf, i, buf.Length - i);
    }

    private const double Max48 = 281474976710656.0; // 2^48

    private static bool BitArg(in CalcValue v, out long n, out CalcValue err)
    {
        err = default;
        n = 0;
        CalcError e = CalcConvert.ToNumber(v, out double d);
        if (e != CalcError.None)
        {
            err = Fx.Err(e);
            return false;
        }

        if (d < 0 || d != Math.Floor(d) || d >= Max48)
        {
            err = NumError;
            return false;
        }

        n = (long)d;
        return true;
    }

    private static CalcValue Bit(ReadOnlySpan<CalcValue> a, int op)
    {
        if (!BitArg(a[0], out long x, out CalcValue e1))
        {
            return e1;
        }

        if (!BitArg(a[1], out long y, out CalcValue e2))
        {
            return e2;
        }

        return Num(op switch { 0 => x & y, 1 => x | y, _ => x ^ y });
    }

    private static CalcValue Shift(ReadOnlySpan<CalcValue> a, bool right)
    {
        if (!BitArg(a[0], out long x, out CalcValue e1))
        {
            return e1;
        }

        CalcError e = CalcConvert.ToNumber(a[1], out double s);
        if (e != CalcError.None)
        {
            return Fx.Err(e);
        }

        s = Math.Truncate(s);
        if (Math.Abs(s) > 53)
        {
            return NumError;
        }

        int amount = (int)s;
        if (right)
        {
            amount = -amount;
        }

        // A left shift (positive amount) that leaves 48 bits is #NUM!; a right shift drops bits.
        if (amount >= 0)
        {
            double r = x * Math.Pow(2, amount);
            return r >= Max48 ? NumError : Num(r);
        }

        return Num(-amount >= 63 ? 0 : x >> -amount);
    }

    private static CalcValue Delta(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e1))
        {
            return e1;
        }

        double y = 0;
        if (!Fx.Omitted(a, 1) && !Fx.TryNum(a[1], out y, out CalcValue e2))
        {
            return e2;
        }

        return Num(x == y ? 1 : 0);
    }

    private static CalcValue GeStep(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e1))
        {
            return e1;
        }

        double step = 0;
        if (!Fx.Omitted(a, 1) && !Fx.TryNum(a[1], out step, out CalcValue e2))
        {
            return e2;
        }

        return Num(x >= step ? 1 : 0);
    }

    private static CalcValue ErfFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double lo, out CalcValue e1))
        {
            return e1;
        }

        if (a.Length < 2 || a[1].IsMissing)
        {
            return Num(Erf(lo));
        }

        if (!Fx.TryNum(a[1], out double hi, out CalcValue e2))
        {
            return e2;
        }

        return Num(Erf(hi) - Erf(lo));
    }

    private static CalcValue ErfcFunction(Evaluator ev, ReadOnlySpan<CalcValue> a) =>
        Fx.TryNum(a[0], out double x, out CalcValue e) ? Num(Erfc(x)) : e;

    /// <summary>The error function. The series 2/√π·e^(−x²)·Σ 2ⁿx^(2n+1)/(2n+1)!! has only positive terms, so it is stable; large arguments use the complement.</summary>
    internal static double Erf(double x)
    {
        double ax = Math.Abs(x);
        double r;
        if (ax < 2.5)
        {
            double term = ax, sum = ax, x2 = 2 * ax * ax;
            for (int n = 0; n < 400; n++)
            {
                term *= x2 / ((2 * n) + 3);
                sum += term;
                if (term < sum * 1e-17)
                {
                    break;
                }
            }

            r = 2 / Math.Sqrt(Math.PI) * Math.Exp(-ax * ax) * sum;
        }
        else
        {
            r = ax > 6 ? 1 : 1 - ErfcPositive(ax);
        }

        return x < 0 ? -r : r;
    }

    /// <summary>The complementary error function; for x ≥ 1 a continued fraction keeps the relative accuracy of tiny results.</summary>
    internal static double Erfc(double x)
    {
        if (x < 0)
        {
            return 2 - Erfc(-x);
        }

        return x < 1 ? 1 - Erf(x) : ErfcPositive(x);
    }

    private static double ErfcPositive(double x)
    {
        if (x > 27)
        {
            return 0;
        }

        // erfc(x) = e^(−x²)/√π · 1/(x + (1/2)/(x + 1/(x + (3/2)/(x + …)))), evaluated from the tail.
        double t = x;
        for (int k = 400; k >= 1; k--)
        {
            t = x + (k / 2.0 / t);
        }

        return Math.Exp(-x * x) / Math.Sqrt(Math.PI) / t;
    }

    internal static string Inv(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}
