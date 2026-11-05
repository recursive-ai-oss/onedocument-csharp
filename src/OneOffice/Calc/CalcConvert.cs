using System;
using System.Globalization;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>Excel's conversions between the scalar kinds: what <c>"3"+1</c>, <c>TRUE+1</c> and <c>1&amp;2</c> do. Shared by the operators and by function families.</summary>
public static class CalcConvert
{
    /// <summary>Reads a scalar as a number the way an arithmetic operator does.</summary>
    /// <param name="value">A scalar. Blank is 0, TRUE is 1, text is read as a number, percentage, currency amount, date or time (<see cref="TextToNumber"/>).</param>
    /// <param name="number">The number.</param>
    /// <returns><see cref="CalcError.None"/>, the value's own error, or <see cref="CalcError.Value"/> when text is not a number or the value is not a scalar.</returns>
    public static CalcError ToNumber(in CalcValue value, out double number)
    {
        number = 0;
        switch (value.Kind)
        {
            case ValueKind.Number:
            case ValueKind.Boolean:
                number = value.Number;
                return CalcError.None;
            case ValueKind.Blank:
                return CalcError.None;
            case ValueKind.Text:
                return TextToNumber(value.Text, out number) ? CalcError.None : CalcError.Value;
            case ValueKind.Error:
                return value.Error;
            default:
                return CalcError.Value;
        }
    }

    /// <summary>Reads a scalar as a boolean the way <c>NOT</c> and <c>IF</c> do.</summary>
    /// <param name="value">A scalar. Numbers are true when not zero; the text <c>TRUE</c> and <c>FALSE</c> (any case) are accepted; blank is false.</param>
    /// <param name="result">The boolean.</param>
    /// <returns><see cref="CalcError.None"/>, the value's own error, or <see cref="CalcError.Value"/> for other text.</returns>
    public static CalcError ToBoolean(in CalcValue value, out bool result)
    {
        result = false;
        switch (value.Kind)
        {
            case ValueKind.Boolean:
                result = value.Boolean;
                return CalcError.None;
            case ValueKind.Number:
                result = value.Number != 0;
                return CalcError.None;
            case ValueKind.Blank:
                return CalcError.None;
            case ValueKind.Text:
                if (value.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                    return CalcError.None;
                }

                return value.Text.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ? CalcError.None : CalcError.Value;
            case ValueKind.Error:
                return value.Error;
            default:
                return CalcError.Value;
        }
    }

    /// <summary>Reads a scalar as text the way <c>&amp;</c> does.</summary>
    /// <param name="value">A scalar. Numbers use up to 15 significant digits; booleans are TRUE and FALSE; blank is empty; an error is its literal.</param>
    /// <returns>The text.</returns>
    public static string ToText(in CalcValue value) => value.Kind switch
    {
        ValueKind.Text => value.Text,
        ValueKind.Number => NumberToText(value.Number),
        ValueKind.Boolean => value.Boolean ? "TRUE" : "FALSE",
        ValueKind.Error => CalcErrors.ToText(value.Error),
        _ => string.Empty,
    };

    /// <summary>Formats a number as the General format does without a width limit: up to 15 significant digits, no trailing zeros, scientific notation from 1E+20 and below 1E-10 (measured against Excel for 1E+19, 1E+20 and 1E-10; the small side is still being probed).</summary>
    /// <param name="d">A finite number.</param>
    /// <returns>The text.</returns>
    public static string NumberToText(double d)
    {
        if (d == 0)
        {
            return "0";
        }

        // 15 significant digits: the shortest of E14 form.
        string e = Math.Abs(d).ToString("E14", CultureInfo.InvariantCulture); // d.dddddddddddddddE+xxx
        int ePos = e.IndexOf('E', StringComparison.Ordinal);
        int exp = int.Parse(e.AsSpan(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string digits = (e[0] + e.Substring(2, ePos - 2)).TrimEnd('0');
        if (digits.Length == 0)
        {
            digits = "0";
        }

        string sign = d < 0 ? "-" : string.Empty;
        if (exp >= 20 || exp < -10)
        {
            string mant = digits.Length > 1 ? digits[0] + "." + digits[1..] : digits;
            int a = Math.Abs(exp);
            return sign + mant + "E" + (exp < 0 ? "-" : "+") + (a < 10 ? "0" : string.Empty) + a.ToString(CultureInfo.InvariantCulture);
        }

        if (exp >= 0)
        {
            return digits.Length <= exp + 1
                ? sign + digits + new string('0', exp + 1 - digits.Length)
                : sign + digits[..(exp + 1)] + "." + digits[(exp + 1)..];
        }

        return sign + "0." + new string('0', -exp - 1) + digits;
    }

    /// <summary>Reads text as Excel does when text meets arithmetic: an optional sign, digits with group separators, a decimal point, an exponent, a percent sign or a currency sign; also dates (<c>2024-03-01</c>, <c>3/1/2024</c>) and times (<c>13:45</c>, <c>1:45 PM</c>) as serial numbers.</summary>
    /// <param name="text">The text; spaces at either end are ignored.</param>
    /// <param name="number">The number.</param>
    /// <returns>True when the whole text is a number.</returns>
    public static bool TextToNumber(string text, out double number)
    {
        number = 0;
        ReadOnlySpan<char> s = text.AsSpan().Trim();
        if (s.IsEmpty)
        {
            return false;
        }

        bool percent = false;
        if (s[^1] == '%')
        {
            percent = true;
            s = s[..^1].TrimEnd();
        }

        bool neg = false;
        if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
        {
            neg = s[0] == '-';
            s = s[1..].TrimStart();
        }

        if (s.Length > 0 && s[0] == '$')
        {
            s = s[1..].TrimStart();
            if (s.Length > 0 && (s[0] == '-' || s[0] == '+') && !neg)
            {
                neg = s[0] == '-';
                s = s[1..];
            }
        }

        if (s.IsEmpty)
        {
            return false;
        }

        if (TryPlainNumber(s, out double v))
        {
            number = (neg ? -v : v) / (percent ? 100 : 1);
            return true;
        }

        if (!percent && TryDateTime(s, out v))
        {
            number = neg ? -v : v;
            return true;
        }

        return false;
    }

    private static bool TryPlainNumber(ReadOnlySpan<char> s, out double v)
    {
        v = 0;
        // Group separators must split the integer part into 1–3 digits then groups of exactly three.
        int i = 0;
        int firstGroup = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            i++;
            firstGroup++;
        }

        int intEnd = i;
        if (i < s.Length && s[i] == ',')
        {
            if (firstGroup is < 1 or > 3)
            {
                return false;
            }

            while (i < s.Length && s[i] == ',')
            {
                if (i + 3 >= s.Length || !char.IsAsciiDigit(s[i + 1]) || !char.IsAsciiDigit(s[i + 2]) || !char.IsAsciiDigit(s[i + 3]))
                {
                    return false;
                }

                i += 4;
                intEnd = i;
            }
        }

        char[] clean = new char[s.Length];
        int n = 0;
        for (int k = 0; k < s.Length; k++)
        {
            if (k < intEnd && s[k] == ',')
            {
                continue;
            }

            clean[n++] = s[k];
        }

        ReadOnlySpan<char> t = clean.AsSpan(0, n);
        if (t.IsEmpty)
        {
            return false;
        }

        // Only digits, one point and an exponent are numbers; "Infinity", "NaN", hex and spaces are not.
        bool digit = false;
        bool point = false;
        bool exp = false;
        for (int k = 0; k < t.Length; k++)
        {
            char c = t[k];
            if (char.IsAsciiDigit(c))
            {
                digit = true;
            }
            else if (c == '.' && !point && !exp)
            {
                point = true;
            }
            else if ((c == 'E' || c == 'e') && digit && !exp && k + 1 < t.Length)
            {
                exp = true;
                if (t[k + 1] == '+' || t[k + 1] == '-')
                {
                    k++;
                    if (k + 1 >= t.Length)
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
        }

        if (!digit || !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsInfinity(v))
        {
            return false;
        }

        return true;
    }

    private static bool TryDateTime(ReadOnlySpan<char> s, out double serial)
    {
        serial = 0;
        string t = s.ToString();
        double datePart = 0;
        bool haveDate = false;
        int sp = t.IndexOf(' ', StringComparison.Ordinal);
        string dateText = sp > 0 ? t[..sp] : t;
        string? rest = sp > 0 ? t[(sp + 1)..].Trim() : null;
        if (dateText.Contains('-', StringComparison.Ordinal) || dateText.Contains('/', StringComparison.Ordinal))
        {
            char sep = dateText.Contains('-', StringComparison.Ordinal) ? '-' : '/';
            string[] p = dateText.Split(sep);
            if (p.Length != 3 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int a) ||
                !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int b) ||
                !int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out int c))
            {
                return false;
            }

            int y;
            int m;
            int d;
            if (p[0].Length == 4)
            {
                y = a;
                m = b;
                d = c;
            }
            else
            {
                m = a;
                d = b;
                y = c;
                if (p[2].Length <= 2)
                {
                    y += y < 30 ? 2000 : 1900;
                }
            }

            if (!ExcelDate.TryToSerial(y, m, d, DateSystem.Date1900, out int ser))
            {
                return false;
            }

            datePart = ser;
            haveDate = true;
        }
        else
        {
            rest = t;
        }

        double timePart = 0;
        if (rest is not null)
        {
            if (!TryTime(rest, out timePart))
            {
                return false;
            }
        }
        else if (!haveDate)
        {
            return false;
        }

        serial = datePart + timePart;
        return true;
    }

    private static bool TryTime(string t, out double fraction)
    {
        fraction = 0;
        bool pm = false;
        bool am = false;
        string u = t.Trim();
        if (u.EndsWith("PM", StringComparison.OrdinalIgnoreCase))
        {
            pm = true;
            u = u[..^2].TrimEnd();
        }
        else if (u.EndsWith("AM", StringComparison.OrdinalIgnoreCase))
        {
            am = true;
            u = u[..^2].TrimEnd();
        }

        string[] p = u.Split(':');
        if (p.Length is < 2 or > 3 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h) ||
            !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int mi))
        {
            return false;
        }

        double sec = 0;
        if (p.Length == 3 && !double.TryParse(p[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out sec))
        {
            return false;
        }

        if ((pm || am) && (h < 1 || h > 12))
        {
            return false;
        }

        if (pm && h < 12)
        {
            h += 12;
        }

        if (am && h == 12)
        {
            h = 0;
        }

        if (mi > 59 || sec >= 60 || h > 9999)
        {
            return false;
        }

        fraction = ((h * 3600.0) + (mi * 60.0) + sec) / 86400.0;
        return true;
    }
}
