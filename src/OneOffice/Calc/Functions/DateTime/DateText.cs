using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>
/// Reads the date and time spellings <c>DATEVALUE</c> and <c>TIMEVALUE</c> accept: numeric dates with <c>/</c>, <c>-</c> or <c>.</c> (month first, or
/// year first when the first part has four digits), dates with a month name in either order, and times with a 24-hour or AM/PM clock. Month and day
/// names are English; the pivot for two-digit years is 30 (00-29 are 2000-2029, 30-99 are 1930-1999).
/// </summary>
internal static class DateText
{
    private static readonly string[] Months =
    [
        "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december",
    ];

    private static readonly string[] Days = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sun", "mon", "tue", "tues", "wed", "thu", "thur", "thurs", "fri", "sat"];

    /// <summary>Parses date and/or time text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="system">The date system the serial is counted in.</param>
    /// <param name="currentYear">The year used when the date has none (<c>1/5</c>, <c>Jan 5</c>).</param>
    /// <param name="date">The whole-day serial when a date was found.</param>
    /// <param name="time">The fraction of a day when a time was found.</param>
    /// <param name="hasDate">True when the text held a date.</param>
    /// <param name="hasTime">True when the text held a time.</param>
    /// <returns>False when the text is neither.</returns>
    public static bool TryParse(string text, DateSystem system, int currentYear, out double date, out double time, out bool hasDate, out bool hasTime)
    {
        date = 0;
        time = 0;
        hasDate = false;
        hasTime = false;
        string[] tokens = text.Replace(',', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var dateTokens = new List<string>();
        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i];
            if (t.Contains(':', StringComparison.Ordinal) || IsHourWithMeridiem(t))
            {
                string clock = t;
                if (i + 1 < tokens.Length && IsMeridiem(tokens[i + 1]))
                {
                    clock += tokens[++i];
                }

                if (hasTime || !TryTime(clock, out time))
                {
                    return false;
                }

                hasTime = true;
            }
            else if (!IsDayName(t))
            {
                dateTokens.Add(t);
            }
        }

        if (dateTokens.Count > 0)
        {
            if (!TryDate(string.Join(' ', dateTokens), system, currentYear, out int serial))
            {
                return false;
            }

            date = serial;
            hasDate = true;
        }

        return hasDate || hasTime;
    }

    private static bool IsDayName(string t) => Array.Exists(Days, d => string.Equals(d, t.TrimEnd('.'), StringComparison.OrdinalIgnoreCase));

    private static bool IsMeridiem(string t) =>
        t.Equals("AM", StringComparison.OrdinalIgnoreCase) || t.Equals("PM", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("A", StringComparison.OrdinalIgnoreCase) || t.Equals("P", StringComparison.OrdinalIgnoreCase);

    private static bool IsHourWithMeridiem(string t)
    {
        if (t.Length < 3 || !(t.EndsWith("AM", StringComparison.OrdinalIgnoreCase) || t.EndsWith("PM", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return int.TryParse(t.AsSpan(0, t.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static bool TryTime(string t, out double fraction)
    {
        fraction = 0;
        bool pm = false;
        bool meridiem = false;
        string u = t.Trim();
        if (u.EndsWith("AM", StringComparison.OrdinalIgnoreCase) || u.EndsWith("PM", StringComparison.OrdinalIgnoreCase))
        {
            pm = u.EndsWith("PM", StringComparison.OrdinalIgnoreCase);
            meridiem = true;
            u = u[..^2].TrimEnd();
        }
        else if (u.EndsWith('A') || u.EndsWith('P') || u.EndsWith('a') || u.EndsWith('p'))
        {
            pm = char.ToUpperInvariant(u[^1]) == 'P';
            meridiem = true;
            u = u[..^1].TrimEnd();
        }

        string[] p = u.Split(':');
        if (p.Length > 3 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h))
        {
            return false;
        }

        int mi = 0;
        if (p.Length >= 2 && !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out mi))
        {
            return false;
        }

        double sec = 0;
        if (p.Length == 3 && !double.TryParse(p[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out sec))
        {
            return false;
        }

        if (p.Length == 1 && !meridiem)
        {
            return false;
        }

        if (meridiem)
        {
            if (h < 1 || h > 12)
            {
                return false;
            }

            h = (h % 12) + (pm ? 12 : 0);
        }

        if (h > 23 || mi > 59 || sec >= 60 || sec < 0)
        {
            return false;
        }

        fraction = ((h * 3600.0) + (mi * 60.0) + sec) / 86400.0;
        return true;
    }

    private static int MonthOf(string part)
    {
        string p = part.TrimEnd('.').ToLowerInvariant();
        if (p.Length < 3)
        {
            return 0;
        }

        for (int i = 0; i < 12; i++)
        {
            if (Months[i] == p || (p.Length == 3 && Months[i].StartsWith(p, StringComparison.Ordinal)) || (p == "sept" && i == 8))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static int FullYear(string part, int value) => part.Length <= 2 ? (value < 30 ? 2000 + value : 1900 + value) : value;

    private static bool TryDate(string text, DateSystem system, int currentYear, out int serial)
    {
        serial = 0;
        string[] parts = text.Split(['/', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        var nums = new int[parts.Length];
        var months = new int[parts.Length];
        int monthAt = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            months[i] = MonthOf(parts[i]);
            if (months[i] > 0)
            {
                if (monthAt >= 0)
                {
                    return false;
                }

                monthAt = i;
            }
            else if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i]))
            {
                return false;
            }
        }

        int y;
        int m;
        int d;
        if (parts.Length == 3)
        {
            if (monthAt == 0)
            {
                m = months[0];
                d = nums[1];
                y = FullYear(parts[2], nums[2]);
            }
            else if (monthAt == 1)
            {
                m = months[1];
                if (parts[0].Length >= 3)
                {
                    y = nums[0];
                    d = nums[2];
                }
                else
                {
                    d = nums[0];
                    y = FullYear(parts[2], nums[2]);
                }
            }
            else if (monthAt == 2)
            {
                return false;
            }
            else if (parts[0].Length >= 3 || nums[0] > 31)
            {
                y = nums[0];
                m = nums[1];
                d = nums[2];
            }
            else
            {
                m = nums[0];
                d = nums[1];
                y = FullYear(parts[2], nums[2]);
            }
        }
        else if (monthAt >= 0)
        {
            int other = monthAt == 0 ? 1 : 0;
            m = months[monthAt];
            if (parts[other].Length >= 3 || nums[other] > 31)
            {
                y = nums[other];
                d = 1;
            }
            else
            {
                y = currentYear;
                d = nums[other];
            }
        }
        else if (parts[0].Length >= 3 || nums[0] > 31)
        {
            y = nums[0];
            m = nums[1];
            d = 1;
        }
        else
        {
            m = nums[0];
            d = nums[1];
            y = currentYear;
        }

        return y >= 1900 && ExcelDate.TryToSerial(y, m, d, system, out serial);
    }
}
