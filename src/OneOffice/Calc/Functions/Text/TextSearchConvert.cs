using System;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneOffice.Calc;

internal static partial class TextFunctions
{
    private static void RegisterSearch(FunctionRegistry r)
    {
        foreach (string suffix in new[] { string.Empty, "B" })
        {
            r.Add(FunctionDefinition.Create("FIND" + suffix, 2, 3, V, (_, a) => Find(a, false)));
            r.Add(FunctionDefinition.Create("SEARCH" + suffix, 2, 3, V, (_, a) => Find(a, true)));
        }

        r.Add(FunctionDefinition.Create("SUBSTITUTE", 3, 4, V, Substitute));
    }

    // FIND matches case and takes the text literally; SEARCH ignores case and understands ? * ~. Both give the 1-based position, #VALUE! when
    // there is none or the start is outside the text.
    private static CalcValue Find(ReadOnlySpan<CalcValue> a, bool search)
    {
        string needle = Str(a[0]);
        string hay = Str(a[1]);
        double start = 1;
        if (!Fx.Omitted(a, 2) && !Fx.TryInt(a[2], out start, out CalcValue e))
        {
            return e;
        }

        if (start < 1 || start > hay.Length + 1)
        {
            return Fx.Err(CalcError.Value);
        }

        int from = (int)start - 1;
        int pos;
        if (search)
        {
            pos = new WildcardPattern(needle).Find(hay, from);
        }
        else
        {
            pos = hay.IndexOf(needle, from, StringComparison.Ordinal);
        }

        return pos < 0 ? Fx.Err(CalcError.Value) : CalcValue.FromNumber(pos + 1);
    }

    // SUBSTITUTE replaces every case-sensitive occurrence, or only the nth when an instance number is given.
    private static CalcValue Substitute(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = Str(a[0]);
        string oldT = Str(a[1]);
        string newT = Str(a[2]);
        double inst = 0;
        if (!Fx.Omitted(a, 3))
        {
            if (!Fx.TryInt(a[3], out inst, out CalcValue e))
            {
                return e;
            }

            if (inst < 1)
            {
                return Fx.Err(CalcError.Value);
            }
        }

        if (oldT.Length == 0)
        {
            return CalcValue.FromText(t);
        }

        var sb = new StringBuilder();
        int pos = 0;
        int count = 0;
        while (true)
        {
            int i = t.IndexOf(oldT, pos, StringComparison.Ordinal);
            if (i < 0)
            {
                break;
            }

            count++;
            sb.Append(t, pos, i - pos);
            if (inst == 0 || count == inst)
            {
                sb.Append(newT);
            }
            else
            {
                sb.Append(oldT);
            }

            pos = i + oldT.Length;
            if (inst != 0 && count == inst)
            {
                break;
            }
        }

        sb.Append(t, pos, t.Length - pos);
        return sb.Length > MaxLength ? Fx.Err(CalcError.Value) : CalcValue.FromText(sb.ToString());
    }

    // ---- text <-> number --------------------------------------------------------------------------------------------------------

    private static void RegisterConvert(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("VALUE", 1, 1, V, ValueFn));
        r.Add(FunctionDefinition.Create("NUMBERVALUE", 1, 3, V, NumberValue));
        r.Add(FunctionDefinition.Create("FIXED", 1, 3, V, Fixed));
        r.Add(FunctionDefinition.Create("DOLLAR", 1, 2, V, Dollar));
        r.Add(FunctionDefinition.Create("TRANSLATE", 2, 3, V, (_, _) => Fx.Err(CalcError.Connect)));
        r.Add(FunctionDefinition.Create("DETECTLANGUAGE", 1, 1, V, (_, _) => Fx.Err(CalcError.Connect)));
    }

    private static CalcValue ValueFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcValue v = a[0];
        switch (v.Kind)
        {
            case ValueKind.Number:
                return v;
            case ValueKind.Blank:
                return CalcValue.FromNumber(0);
            case ValueKind.Text:
                return CalcConvert.TextToNumber(v.Text, out double d) ? CalcValue.FromNumber(d) : Fx.Err(CalcError.Value);
            default:
                return Fx.Err(CalcError.Value);
        }
    }

    // NUMBERVALUE reads text with the separators the caller names (default "." and ","): spaces are ignored anywhere, group separators before the
    // decimal separator are dropped, and each trailing % divides by 100. Empty text is 0.
    private static CalcValue NumberValue(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = Str(a[0]);
        char dec = '.';
        char grp = ',';
        if (!Fx.Omitted(a, 1) && Str(a[1]) is { Length: > 0 } ds)
        {
            dec = ds[0];
        }

        if (!Fx.Omitted(a, 2) && Str(a[2]) is { Length: > 0 } gs)
        {
            grp = gs[0];
        }

        if (dec == grp)
        {
            return Fx.Err(CalcError.Value);
        }

        var sb = new StringBuilder();
        int percent = 0;
        bool seenDec = false;
        bool seenDigit = false;
        bool exp = false;
        foreach (char c in t)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (percent > 0)
            {
                if (c != '%')
                {
                    return Fx.Err(CalcError.Value);
                }

                percent++;
                continue;
            }

            if (c == '%')
            {
                percent = 1;
            }
            else if (c == dec && !exp)
            {
                if (seenDec)
                {
                    return Fx.Err(CalcError.Value);
                }

                seenDec = true;
                sb.Append('.');
            }
            else if (c == grp && !seenDec && !exp)
            {
                continue;
            }
            else if (char.IsAsciiDigit(c))
            {
                seenDigit = true;
                sb.Append(c);
            }
            else if ((c is '+' or '-') && (sb.Length == 0 || sb[^1] is 'E'))
            {
                sb.Append(c);
            }
            else if (c is 'E' or 'e' && seenDigit && !exp)
            {
                exp = true;
                sb.Append('E');
            }
            else
            {
                return Fx.Err(CalcError.Value);
            }
        }

        if (sb.Length == 0)
        {
            return CalcValue.FromNumber(0);
        }

        if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            return Fx.Err(CalcError.Value);
        }

        for (int i = 0; i < percent; i++)
        {
            d /= 100;
        }

        return Fx.Num(d);
    }

    private static string GroupDigits(string digits)
    {
        if (digits.Length <= 3)
        {
            return digits;
        }

        var sb = new StringBuilder(digits.Length + (digits.Length / 3));
        int first = digits.Length % 3;
        if (first > 0)
        {
            sb.Append(digits, 0, first);
        }

        for (int i = first; i < digits.Length; i += 3)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(digits, i, 3);
        }

        return sb.ToString();
    }

    /// <summary>Formats a number rounded to <paramref name="decimals"/> places as "1,234.50" (the FIXED/DOLLAR body, without sign).</summary>
    private static string FixedBody(double magnitude, int decimals, bool commas)
    {
        string s = magnitude < 7.9e27 ? ((decimal)magnitude).ToString("F" + Math.Max(decimals, 0).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : magnitude.ToString("F" + Math.Max(decimals, 0).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (!commas)
        {
            return s;
        }

        int dot = s.IndexOf('.', StringComparison.Ordinal);
        string intPart = dot < 0 ? s : s[..dot];
        return GroupDigits(intPart) + (dot < 0 ? string.Empty : s[dot..]);
    }

    // FIXED(number, decimals = 2, no_commas = FALSE): rounds like ROUND and returns text; a negative count of decimals rounds left of the point.
    private static CalcValue Fixed(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e))
        {
            return e;
        }

        double dec = 2;
        if (!Fx.Omitted(a, 1) && !Fx.TryInt(a[1], out dec, out e))
        {
            return e;
        }

        bool noCommas = false;
        if (!Fx.Omitted(a, 2) && !Fx.TryBool(a[2], out noCommas, out e))
        {
            return e;
        }

        if (dec > 127)
        {
            return Fx.Err(CalcError.Value);
        }

        double rounded = MathFunctions.RoundTo(x, dec, 0);
        string body = FixedBody(Math.Abs(rounded), (int)dec, !noCommas);
        bool negative = rounded < 0;
        return CalcValue.FromText((negative ? "-" : string.Empty) + body);
    }

    // DOLLAR(number, decimals = 2): currency text with the dollar sign; negative numbers are in parentheses.
    private static CalcValue Dollar(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e))
        {
            return e;
        }

        double dec = 2;
        if (!Fx.Omitted(a, 1) && !Fx.TryInt(a[1], out dec, out e))
        {
            return e;
        }

        if (dec > 127)
        {
            return Fx.Err(CalcError.Value);
        }

        double rounded = MathFunctions.RoundTo(x, dec, 0);
        string body = "$" + FixedBody(Math.Abs(rounded), (int)dec, true);
        return CalcValue.FromText(rounded < 0 ? "(" + body + ")" : body);
    }
}
