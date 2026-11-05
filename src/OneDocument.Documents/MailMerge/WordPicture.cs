using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Documents.MailMerge;

/// <summary>
/// The pictures and case switches of Word fields: <c>\@</c> date-time pictures, <c>\#</c> numeric pictures and the <c>\*</c>
/// case formats. Output is for the invariant (en-US) culture.
/// </summary>
internal static class WordPicture
{
    private static readonly string[] MonthNames = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
    private static readonly string[] MonthAbbreviations = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;

    /// <summary>
    /// Formats a date-time with a Word date-time picture (ECMA-376 Part 1 §17.16.4, field formatting switches, <c>\@</c>). Word's letters differ from
    /// the spreadsheet format language, so this does not go through the number-format service: <c>M</c> is the month and <c>m</c> the
    /// minute, <c>h</c> counts hours 1–12 and <c>H</c> 0–23.
    /// </summary>
    public static string FormatDate(DateTime value, string picture)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < picture.Length)
        {
            char c = picture[i];
            if (c == '\'')
            {
                // A quoted literal; a quote that never closes runs to the end of the picture.
                int end = picture.IndexOf('\'', i + 1);
                if (end < 0)
                {
                    end = picture.Length;
                }

                sb.Append(picture, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }

            if (c == '\\' && i + 1 < picture.Length)
            {
                sb.Append(picture[i + 1]);
                i += 2;
                continue;
            }

            int run = 1;
            while (i + run < picture.Length && picture[i + run] == c)
            {
                run++;
            }

            switch (c)
            {
                case 'M':
                    sb.Append(run switch
                    {
                        1 => value.Month.ToString(CultureInfo.InvariantCulture),
                        2 => value.Month.ToString("00", CultureInfo.InvariantCulture),
                        3 => MonthAbbreviations[value.Month - 1],
                        _ => MonthNames[value.Month - 1],
                    });
                    break;
                case 'd':
                    sb.Append(run switch
                    {
                        1 => value.Day.ToString(CultureInfo.InvariantCulture),
                        2 => value.Day.ToString("00", CultureInfo.InvariantCulture),
                        3 => value.DayOfWeek.ToString()[..3],
                        _ => value.DayOfWeek.ToString(),
                    });
                    break;
                case 'y':
                    sb.Append(run <= 2
                        ? (value.Year % 100).ToString("00", CultureInfo.InvariantCulture)
                        : value.Year.ToString("0000", CultureInfo.InvariantCulture));
                    break;
                case 'h':
                    sb.Append(Pad(value.Hour % 12 == 0 ? 12 : value.Hour % 12, run));
                    break;
                case 'H':
                    sb.Append(Pad(value.Hour, run));
                    break;
                case 'm':
                    sb.Append(Pad(value.Minute, run));
                    break;
                case 's':
                    sb.Append(Pad(value.Second, run));
                    break;
                case 'A' or 'a':
                    {
                        // AM/PM and am/pm, A/P and a/p (the case of the picture is the case of the output).
                        bool pm = value.Hour >= 12;
                        if (TryMeridiem(picture, i, out int length, out bool upper, out bool shortForm))
                        {
                            string text = shortForm ? (pm ? "P" : "A") : (pm ? "PM" : "AM");
                            sb.Append(upper ? text : text.ToLowerInvariant());
                            i += length;
                            continue;
                        }

                        sb.Append(c, run);
                        break;
                    }

                default:
                    sb.Append(c, run);
                    break;
            }

            i += run;
        }

        return sb.ToString();
    }

    private static string Pad(int number, int width) =>
        number.ToString(width >= 2 ? "00" : "0", CultureInfo.InvariantCulture);

    private static bool TryMeridiem(string picture, int at, out int length, out bool upper, out bool shortForm)
    {
        length = 0;
        upper = false;
        shortForm = false;
        ReadOnlySpan<char> rest = picture.AsSpan(at);
        if (rest.StartsWith("AM/PM", StringComparison.Ordinal) || rest.StartsWith("am/pm", StringComparison.Ordinal))
        {
            length = 5;
            // Word shows AM and PM in capitals whichever case the picture uses.
            upper = true;
            return true;
        }

        if (rest.StartsWith("A/P", StringComparison.Ordinal) || rest.StartsWith("a/p", StringComparison.Ordinal))
        {
            length = 3;
            upper = rest[0] == 'A';
            shortForm = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a number with a Word numeric picture (ECMA-376 Part 1 §17.16.4, field formatting switches, <c>\#</c>). The picture is
    /// translated into a number-format code and formatted by the number-format service, so <c>#,##0.00</c> and
    /// <c>positive;negative;zero</c> sections behave as in a cell. Where Word's items differ they are handled here: <c>%</c> and
    /// <c>$</c> are literal characters (Word does not scale by 100), <c>'text'</c> is a literal, <c>-</c> and <c>+</c> print a sign (or a
    /// space), <c>x</c> drops the digits to its left (or rounds at its place after the decimal point), and <c>#</c> shows a space where
    /// the number has no digit, except in a picture with a digit-grouping comma, where it shows nothing (Word's output there is a
    /// question for the desktop application).
    /// </summary>
    public static string FormatNumber(double value, string picture)
    {
        List<string> sections = SplitSections(picture);
        string section = sections[0];
        double v = Math.Abs(value);
        bool negativeSection = false;
        if (value < 0 && sections.Count >= 2)
        {
            section = sections[1];
            negativeSection = true;
        }
        else if (value == 0 && sections.Count >= 3)
        {
            section = sections[2];
        }

        // x: Word drops the digits left of the placeholder, or rounds at it when it sits right of the decimal point.
        int x = IndexOfUnquoted(section, 'x');
        if (x >= 0)
        {
            int dot = IndexOfUnquoted(section, '.');
            if (dot < 0 || x < dot)
            {
                int places = 0;
                for (int i = x; i < (dot < 0 ? section.Length : dot); i++)
                {
                    if (section[i] is '0' or '#' or 'x')
                    {
                        places++;
                    }
                }

                double modulus = Math.Pow(10, places);
                v = Math.Truncate(v) % modulus + (v - Math.Truncate(v));
            }
            else
            {
                int places = 0;
                for (int i = dot + 1; i <= x; i++)
                {
                    if (section[i] is '0' or '#' or 'x')
                    {
                        places++;
                    }
                }

                v = Math.Round(v, Math.Min(places, 15), MidpointRounding.AwayFromZero);
                int cut = x + 1;
                while (cut < section.Length && section[cut] is '0' or '#')
                {
                    section = section.Remove(cut, 1);
                }
            }

            // The x place itself prints a digit only when the number has one, so a picture such as x## gives "3" for 3.
            section = section.Remove(x, 1).Insert(x, "#");
        }

        bool grouping = x >= 0 || IndexOfUnquoted(section, ',') >= 0;
        var code = new StringBuilder();
        bool anySign = false;
        int i2 = 0;
        while (i2 < section.Length)
        {
            char c = section[i2];
            if (c == '\'')
            {
                int end = section.IndexOf('\'', i2 + 1);
                if (end < 0)
                {
                    end = section.Length;
                }

                code.Append('"').Append(section, i2 + 1, end - i2 - 1).Append('"');
                i2 = end + 1;
                continue;
            }

            switch (c)
            {
                case '0' or '.' or ',' or '$' or '(' or ')' or ' ' or ':' or '/':
                    code.Append(c);
                    break;
                case '#':
                    code.Append(grouping ? '#' : '?');
                    break;
                case '-':
                    anySign = true;
                    code.Append('"').Append(value < 0 ? '-' : ' ').Append('"');
                    break;
                case '+':
                    anySign = true;
                    code.Append('"').Append(value < 0 ? '-' : (value > 0 ? '+' : ' ')).Append('"');
                    break;
                case '\\' when i2 + 1 < section.Length:
                    code.Append('\\').Append(section[++i2]);
                    break;
                default:
                    code.Append('\\').Append(c);
                    break;
            }

            i2++;
        }

        string text = NumberFormat.TryParse(code.ToString(), out NumberFormat? format, out _) && format is not null
            ? format.Format(v)
            : v.ToString("R", CultureInfo.InvariantCulture);
        return value < 0 && !negativeSection && !anySign ? "-" + text : text;
    }

    private static int IndexOfUnquoted(string s, char target)
    {
        bool quoted = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && s[i] == target)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> SplitSections(string picture)
    {
        var list = new List<string>();
        bool quoted = false;
        int start = 0;
        for (int i = 0; i < picture.Length; i++)
        {
            if (picture[i] == '\'')
            {
                quoted = !quoted;
            }
            else if (picture[i] == ';' && !quoted && list.Count < 2)
            {
                list.Add(picture[start..i]);
                start = i + 1;
            }
        }

        list.Add(picture[start..]);
        return list;
    }

    /// <summary>Applies a <c>\*</c> case format: <c>Upper</c>, <c>Lower</c>, <c>FirstCap</c>, <c>Caps</c>; anything else leaves the text alone.</summary>
    public static string ApplyCase(string text, string format)
    {
        switch (format.ToLowerInvariant())
        {
            case "upper":
                return text.ToUpperInvariant();
            case "lower":
                return text.ToLowerInvariant();
            case "firstcap":
                {
                    string lower = text.ToLowerInvariant();
                    for (int i = 0; i < lower.Length; i++)
                    {
                        if (char.IsLetter(lower[i]))
                        {
                            return lower[..i] + char.ToUpperInvariant(lower[i]) + lower[(i + 1)..];
                        }
                    }

                    return lower;
                }

            case "caps":
                {
                    var sb = new StringBuilder(text.Length);
                    bool start = true;
                    foreach (char ch in text)
                    {
                        sb.Append(start && char.IsLetter(ch) ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                        start = !char.IsLetterOrDigit(ch) && ch != '\'';
                    }

                    return sb.ToString();
                }

            default:
                return text;
        }
    }
}
