using System;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneDocument.Calc;

internal static partial class EngineeringFunctions
{
    // A complex number with the suffix it was written with ('i', 'j', or '\0' for a plain real number).
    private readonly record struct Cx(double Re, double Im, char Suffix);

    private static void RegisterComplex(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("COMPLEX", 2, 3, [], ComplexFunction));
        r.Add(FunctionDefinition.Create("IMREAL", 1, 1, [], (_, a) => Part(a[0], 0)));
        r.Add(FunctionDefinition.Create("IMAGINARY", 1, 1, [], (_, a) => Part(a[0], 1)));
        r.Add(FunctionDefinition.Create("IMABS", 1, 1, [], (_, a) => Part(a[0], 2)));
        r.Add(FunctionDefinition.Create("IMARGUMENT", 1, 1, [], (_, a) => Part(a[0], 3)));
        r.Add(FunctionDefinition.Create("IMCONJUGATE", 1, 1, [], (_, a) => Unary(a[0], z => new Cx(z.Re, -z.Im, z.Suffix))));
        r.Add(FunctionDefinition.Create("IMSIN", 1, 1, [], (_, a) => Unary(a[0], Sin)));
        r.Add(FunctionDefinition.Create("IMCOS", 1, 1, [], (_, a) => Unary(a[0], Cos)));
        r.Add(FunctionDefinition.Create("IMTAN", 1, 1, [], (_, a) => Unary(a[0], z => Divide(Sin(z), Cos(z)))));
        r.Add(FunctionDefinition.Create("IMCOT", 1, 1, [], (_, a) => Unary(a[0], z => Divide(Cos(z), Sin(z)))));
        r.Add(FunctionDefinition.Create("IMSEC", 1, 1, [], (_, a) => Unary(a[0], z => Divide(One(z), Cos(z)))));
        r.Add(FunctionDefinition.Create("IMCSC", 1, 1, [], (_, a) => Unary(a[0], z => Divide(One(z), Sin(z)))));
        r.Add(FunctionDefinition.Create("IMSINH", 1, 1, [], (_, a) => Unary(a[0], Sinh)));
        r.Add(FunctionDefinition.Create("IMCOSH", 1, 1, [], (_, a) => Unary(a[0], Cosh)));
        r.Add(FunctionDefinition.Create("IMSECH", 1, 1, [], (_, a) => Unary(a[0], z => Divide(One(z), Cosh(z)))));
        r.Add(FunctionDefinition.Create("IMCSCH", 1, 1, [], (_, a) => Unary(a[0], z => Divide(One(z), Sinh(z)))));
        r.Add(FunctionDefinition.Create("IMEXP", 1, 1, [], (_, a) => Unary(a[0], z => new Cx(Math.Exp(z.Re) * Math.Cos(z.Im), Math.Exp(z.Re) * Math.Sin(z.Im), z.Suffix))));
        r.Add(FunctionDefinition.Create("IMLN", 1, 1, [], (_, a) => Unary(a[0], Ln)));
        r.Add(FunctionDefinition.Create("IMLOG10", 1, 1, [], (_, a) => Unary(a[0], z => Scale(Ln(z), 1 / Math.Log(10)))));
        r.Add(FunctionDefinition.Create("IMLOG2", 1, 1, [], (_, a) => Unary(a[0], z => Scale(Ln(z), 1 / Math.Log(2)))));
        r.Add(FunctionDefinition.Create("IMSQRT", 1, 1, [], (_, a) => Unary(a[0], Sqrt)));
        r.Add(FunctionDefinition.Create("IMPOWER", 2, 2, [], PowerFunction));
        r.Add(FunctionDefinition.Create("IMDIV", 2, 2, [], (_, a) => Binary(a[0], a[1], Divide)));
        r.Add(FunctionDefinition.Create("IMSUB", 2, 2, [], (_, a) => Binary(a[0], a[1], (x, y) => new Cx(x.Re - y.Re, x.Im - y.Im, Join(x, y)))));
        r.Add(FunctionDefinition.Create("IMSUM", 1, int.MaxValue, [ArgKind.Any], (e, a) => Fold(e, a, false), repeatLast: true));
        r.Add(FunctionDefinition.Create("IMPRODUCT", 1, int.MaxValue, [ArgKind.Any], (e, a) => Fold(e, a, true), repeatLast: true));
    }

    private static char Join(Cx a, Cx b) => a.Suffix != '\0' ? a.Suffix : b.Suffix;

    private static Cx One(Cx z) => new(1, 0, z.Suffix);

    private static Cx Scale(Cx z, double k) => new(z.Re * k, z.Im * k, z.Suffix);

    private static Cx Sin(Cx z) => new(Math.Sin(z.Re) * Math.Cosh(z.Im), Math.Cos(z.Re) * Math.Sinh(z.Im), z.Suffix);

    private static Cx Cos(Cx z) => new(Math.Cos(z.Re) * Math.Cosh(z.Im), -Math.Sin(z.Re) * Math.Sinh(z.Im), z.Suffix);

    private static Cx Sinh(Cx z) => new(Math.Sinh(z.Re) * Math.Cos(z.Im), Math.Cosh(z.Re) * Math.Sin(z.Im), z.Suffix);

    private static Cx Cosh(Cx z) => new(Math.Cosh(z.Re) * Math.Cos(z.Im), Math.Sinh(z.Re) * Math.Sin(z.Im), z.Suffix);

    private static Cx Ln(Cx z) => new(Math.Log(Math.Sqrt((z.Re * z.Re) + (z.Im * z.Im))), Math.Atan2(z.Im, z.Re), z.Suffix);

    private static Cx Sqrt(Cx z)
    {
        double r = Math.Sqrt(Math.Sqrt((z.Re * z.Re) + (z.Im * z.Im)));
        double th = Math.Atan2(z.Im, z.Re) / 2;
        return new Cx(r * Math.Cos(th), r * Math.Sin(th), z.Suffix);
    }

    // A zero divisor gives NaN, which the callers turn into #NUM!.
    private static Cx Divide(Cx x, Cx y)
    {
        double d = (y.Re * y.Re) + (y.Im * y.Im);
        if (d == 0)
        {
            return new Cx(double.NaN, double.NaN, Join(x, y));
        }

        return new Cx(((x.Re * y.Re) + (x.Im * y.Im)) / d, ((x.Im * y.Re) - (x.Re * y.Im)) / d, Join(x, y));
    }

    private static bool ParseReal(ReadOnlySpan<char> s, out double v)
    {
        v = 0;
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!(char.IsAsciiDigit(c) || c is '.' or 'e' or 'E' or '+' or '-'))
            {
                return false;
            }
        }

        return double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out v)
            && double.IsFinite(v);
    }

    // Excel's text form: "a+bi", "a-bj", "bi", "i", "-j", "a". No spaces; the suffix is i or j.
    private static bool TryParseComplex(string s, out Cx z)
    {
        z = default;
        if (s.Length == 0)
        {
            return true;
        }

        char last = s[^1];
        if (last is not ('i' or 'j'))
        {
            if (!ParseReal(s, out double re))
            {
                return false;
            }

            z = new Cx(re, 0, '\0');
            return true;
        }

        string body = s[..^1];
        int split = -1;
        for (int p = body.Length - 1; p > 0; p--)
        {
            if (body[p] is '+' or '-' && body[p - 1] is not ('e' or 'E'))
            {
                split = p;
                break;
            }
        }

        string realText = split > 0 ? body[..split] : string.Empty;
        string imagText = split > 0 ? body[split..] : body;
        double real = 0;
        if (realText.Length > 0 && !ParseReal(realText, out real))
        {
            return false;
        }

        double imag;
        if (imagText.Length == 0 || imagText == "+")
        {
            imag = 1;
        }
        else if (imagText == "-")
        {
            imag = -1;
        }
        else if (!ParseReal(imagText, out imag))
        {
            return false;
        }

        z = new Cx(real, imag, last);
        return true;
    }

    private static bool ReadComplex(in CalcValue v, out Cx z, out CalcValue error)
    {
        z = default;
        error = default;
        switch (v.Kind)
        {
            case ValueKind.Number:
                z = new Cx(v.Number, 0, '\0');
                return true;
            case ValueKind.Blank:
                return true;
            case ValueKind.Boolean:
                error = Fx.Err(CalcError.Value);
                return false;
            default:
                if (!TryParseComplex(v.Text, out z))
                {
                    error = NumError;
                    return false;
                }

                return true;
        }
    }

    private static string Part(double d) => CalcConvert.NumberToText(d == 0 ? 0 : d);

    private static CalcValue Format(Cx z)
    {
        if (!double.IsFinite(z.Re) || !double.IsFinite(z.Im))
        {
            return NumError;
        }

        char suffix = z.Suffix == 'j' ? 'j' : 'i';
        if (z.Im == 0)
        {
            return CalcValue.FromText(Part(z.Re));
        }

        string imag = z.Im == 1 ? string.Empty : z.Im == -1 ? "-" : Part(z.Im);
        string text = imag + suffix;
        if (z.Re == 0)
        {
            return CalcValue.FromText(text);
        }

        return CalcValue.FromText(Part(z.Re) + (text[0] == '-' ? string.Empty : "+") + text);
    }

    private static CalcValue ComplexFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double re, out CalcValue e1))
        {
            return e1;
        }

        if (!Fx.TryNum(a[1], out double im, out CalcValue e2))
        {
            return e2;
        }

        char suffix = 'i';
        if (!Fx.Omitted(a, 2))
        {
            if (a[2].Kind != ValueKind.Text)
            {
                return Fx.Err(CalcError.Value);
            }

            string s = a[2].Text;
            if (s.Length != 0 && s != "i" && s != "j")
            {
                return Fx.Err(CalcError.Value);
            }

            suffix = s.Length == 0 ? 'i' : s[0];
        }

        return Format(new Cx(re, im, suffix));
    }

    private static CalcValue Part(in CalcValue v, int which)
    {
        if (!ReadComplex(v, out Cx z, out CalcValue err))
        {
            return err;
        }

        switch (which)
        {
            case 0:
                return Num(z.Re);
            case 1:
                return Num(z.Im);
            case 2:
                return Num(Math.Sqrt((z.Re * z.Re) + (z.Im * z.Im)));
            default:
                return z.Re == 0 && z.Im == 0 ? Fx.Err(CalcError.Div0) : Num(Math.Atan2(z.Im, z.Re));
        }
    }

    private static CalcValue Unary(in CalcValue v, Func<Cx, Cx> f) =>
        ReadComplex(v, out Cx z, out CalcValue err) ? Format(f(z)) : err;

    private static CalcValue Binary(in CalcValue a, in CalcValue b, Func<Cx, Cx, Cx> f)
    {
        if (!ReadComplex(a, out Cx x, out CalcValue e1))
        {
            return e1;
        }

        if (!ReadComplex(b, out Cx y, out CalcValue e2))
        {
            return e2;
        }

        if (x.Suffix != '\0' && y.Suffix != '\0' && x.Suffix != y.Suffix)
        {
            return Fx.Err(CalcError.Value);
        }

        return Format(f(x, y));
    }

    // z^n by De Moivre's formula; 0^n is 0 for n > 0 and #NUM! otherwise.
    private static CalcValue PowerFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!ReadComplex(a[0], out Cx z, out CalcValue e1))
        {
            return e1;
        }

        if (!Fx.TryNum(a[1], out double n, out CalcValue e2))
        {
            return e2;
        }

        double mod = Math.Sqrt((z.Re * z.Re) + (z.Im * z.Im));
        if (mod == 0)
        {
            return n > 0 ? Format(new Cx(0, 0, z.Suffix)) : NumError;
        }

        double r = Math.Pow(mod, n), th = Math.Atan2(z.Im, z.Re) * n;
        return Format(new Cx(r * Math.Cos(th), r * Math.Sin(th), z.Suffix));
    }

    // IMSUM and IMPRODUCT take any number of arguments and ranges; blank cells inside a range are skipped.
    private static CalcValue Fold(Evaluator ev, ReadOnlySpan<CalcValue> args, bool product)
    {
        var cells = new List<CalcValue>();
        foreach (CalcValue arg in args)
        {
            CalcError fe = Fx.Flatten(ev, arg, cells);
            if (fe != CalcError.None)
            {
                return Fx.Err(fe);
            }
        }

        var acc = new Cx(product ? 1 : 0, 0, '\0');
        foreach (CalcValue c in cells)
        {
            if (c.IsError)
            {
                return c;
            }

            if (c.Kind == ValueKind.Blank && !c.IsMissing)
            {
                continue;
            }

            if (!ReadComplex(c, out Cx z, out CalcValue err))
            {
                return err;
            }

            if (acc.Suffix != '\0' && z.Suffix != '\0' && acc.Suffix != z.Suffix)
            {
                return Fx.Err(CalcError.Value);
            }

            char suffix = Join(acc, z);
            acc = product
                ? new Cx((acc.Re * z.Re) - (acc.Im * z.Im), (acc.Re * z.Im) + (acc.Im * z.Re), suffix)
                : new Cx(acc.Re + z.Re, acc.Im + z.Im, suffix);
        }

        return Format(acc);
    }
}
