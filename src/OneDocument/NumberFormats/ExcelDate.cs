using System;

namespace RecursiveAI.OneDocument.NumberFormats;

/// <summary>The two date systems a workbook can use (ECMA-376 Part 1 §18.2.28, <c>date1904</c>).</summary>
public enum DateSystem
{
    /// <summary>Serial 1 is 1900-01-01 and the fictitious 1900-02-29 is serial 60. The default.</summary>
    Date1900 = 0,

    /// <summary>Serial 0 is 1904-01-01; there is no leap-year quirk.</summary>
    Date1904 = 1,
}

/// <summary>
/// A date and time of day broken out of a serial number. In the 1900 system, serial 0 gives year 1900, month 1, day 0 and serial 60
/// gives 1900-02-29; both are dates Excel shows although they do not exist on the calendar.
/// </summary>
/// <param name="Year">1900 to 9999.</param>
/// <param name="Month">1 to 12.</param>
/// <param name="Day">1 to 31, or 0 for serial 0 of the 1900 system.</param>
/// <param name="Hour">0 to 23.</param>
/// <param name="Minute">0 to 59.</param>
/// <param name="Second">0 to 59.</param>
/// <param name="Millisecond">0 to 999.</param>
/// <param name="DayOfWeek">0 for Sunday to 6 for Saturday, as Excel counts it (the 1900 system calls serial 1 a Sunday).</param>
/// <param name="DayNumber">The whole-day part of the serial.</param>
public readonly record struct ExcelDateTime(
    int Year, int Month, int Day, int Hour, int Minute, int Second, int Millisecond, int DayOfWeek, int DayNumber);

/// <summary>
/// Excel serial-date arithmetic. All methods are allocation-free and independent of the host culture.
/// </summary>
/// <remarks>
/// The 1900 system treats 1900 as a leap year (serial 60 is 1900-02-29) and counts serial 0 as "1900-01-00"; serials from 61 on are
/// real calendar days (1899-12-30 plus the serial). Times are fractions of a day and are rounded to the nearest millisecond, which
/// carries into the next day when a fraction rounds up to a whole day. The largest serial is 2958465 (9999-12-31) in the 1900
/// system and 2957003 in the 1904 system; negative serials have no date.
/// </remarks>
public static class ExcelDate
{
    /// <summary>The serial of 9999-12-31 in the 1900 system.</summary>
    public const int MaxSerial1900 = 2958465;

    /// <summary>The serial of 9999-12-31 in the 1904 system.</summary>
    public const int MaxSerial1904 = 2957003;

    internal const long MillisecondsPerDay = 86_400_000;

    private static readonly int DaysAt1900Jan1 = DaysFromCivil(1900, 1, 1);
    private static readonly int DaysAt1900Mar1 = DaysFromCivil(1900, 3, 1);
    private static readonly int DaysAt1904Jan1 = DaysFromCivil(1904, 1, 1);

    /// <summary>Returns the largest whole-day serial of a system.</summary>
    /// <param name="system">The date system.</param>
    /// <returns>2958465 or 2957003.</returns>
    public static int MaxSerial(DateSystem system) => system == DateSystem.Date1904 ? MaxSerial1904 : MaxSerial1900;

    /// <summary>Breaks a serial into date and time, rounding the time to the nearest millisecond.</summary>
    /// <param name="serial">The serial number; fractions are the time of day.</param>
    /// <param name="system">The date system.</param>
    /// <param name="result">The broken-out value when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> for NaN, infinity, negative serials (after rounding) and serials past 9999-12-31.</returns>
    public static bool TryFromSerial(double serial, DateSystem system, out ExcelDateTime result)
    {
        if (!TryRoundToMilliseconds(serial, 1, system, out long ms))
        {
            result = default;
            return false;
        }

        return TryFromMilliseconds(ms, system, out result);
    }

    /// <summary>Converts a calendar date to a whole-day serial.</summary>
    /// <param name="year">1900 to 9999.</param>
    /// <param name="month">1 to 12.</param>
    /// <param name="day">The day of the month. 1900-01-00 (serial 0) and 1900-02-29 (serial 60) are accepted in the 1900 system.</param>
    /// <param name="system">The date system.</param>
    /// <param name="serial">The serial when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the date does not exist or is outside the system's range.</returns>
    public static bool TryToSerial(int year, int month, int day, DateSystem system, out int serial)
    {
        serial = 0;
        if (year is < 1900 or > 9999 || month is < 1 or > 12)
        {
            return false;
        }

        if (system == DateSystem.Date1900)
        {
            if (year == 1900 && month == 1 && day == 0)
            {
                return true;
            }

            if (year == 1900 && month == 2 && day == 29)
            {
                serial = 60;
                return true;
            }
        }

        if (day < 1 || day > DaysInMonth(year, month))
        {
            return false;
        }

        int days = DaysFromCivil(year, month, day);
        if (system == DateSystem.Date1904)
        {
            serial = days - DaysAt1904Jan1;
            return serial is >= 0 and <= MaxSerial1904;
        }

        serial = days < DaysAt1900Mar1 ? days - DaysAt1900Jan1 + 1 : days - DaysAt1900Mar1 + 61;
        return serial is >= 1 and <= MaxSerial1900;
    }

    /// <summary>Converts a time of day to the fraction of a day.</summary>
    /// <param name="hour">0 or more; values past 23 give a fraction of a day above 1 (a duration).</param>
    /// <param name="minute">Minutes.</param>
    /// <param name="second">Seconds.</param>
    /// <param name="millisecond">Milliseconds.</param>
    /// <returns>The fraction of a day.</returns>
    public static double TimeToFraction(int hour, int minute, int second, int millisecond = 0) =>
        ((((((hour * 60L) + minute) * 60) + second) * 1000L) + millisecond) / (double)MillisecondsPerDay;

    /// <summary>Returns the serial of a <see cref="DateTime"/> (the time of day included).</summary>
    /// <param name="value">A date from 1900-01-01 on (1904-01-01 in the 1904 system).</param>
    /// <param name="system">The date system.</param>
    /// <param name="serial">The serial when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the date is outside the system's range.</returns>
    public static bool TryToSerial(DateTime value, DateSystem system, out double serial)
    {
        serial = 0;
        if (!TryToSerial(value.Year, value.Month, value.Day, system, out int day))
        {
            return false;
        }

        serial = day + (value.TimeOfDay.Ticks / (double)TimeSpan.TicksPerDay);
        return true;
    }

    /// <summary>Returns the <see cref="DateTime"/> of a serial.</summary>
    /// <param name="serial">The serial number.</param>
    /// <param name="system">The date system.</param>
    /// <param name="value">The date when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the serial is out of range or falls on a day that does not exist (serial 0 and 60 of the 1900 system).</returns>
    public static bool TryToDateTime(double serial, DateSystem system, out DateTime value)
    {
        value = default;
        if (!TryFromSerial(serial, system, out ExcelDateTime e) || e.Day == 0 || (system == DateSystem.Date1900 && e.DayNumber == 60))
        {
            return false;
        }

        value = new DateTime(e.Year, e.Month, e.Day, e.Hour, e.Minute, e.Second, e.Millisecond, DateTimeKind.Unspecified);
        return true;
    }

    /// <summary>Returns whether a serial is one Excel can show as a date.</summary>
    /// <param name="serial">The serial number.</param>
    /// <param name="system">The date system.</param>
    /// <returns><see langword="true"/> when <see cref="TryFromSerial"/> would succeed.</returns>
    public static bool IsInRange(double serial, DateSystem system) => TryFromSerial(serial, system, out _);

    // Rounds a serial to a whole number of `unitMs`-millisecond steps. Returns false when the result is outside the date range.
    internal static bool TryRoundToMilliseconds(double serial, int unitMs, DateSystem system, out long ms)
    {
        ms = 0;
        if (double.IsNaN(serial) || double.IsInfinity(serial) || serial < 0 || serial >= MaxSerial(system) + 1.0)
        {
            return false;
        }

        double total = Math.Round(serial * MillisecondsPerDay, MidpointRounding.AwayFromZero);
        long t = (long)total;
        if (unitMs > 1)
        {
            t = (t + (unitMs / 2)) / unitMs * unitMs;
        }

        if (t >= (MaxSerial(system) + 1L) * MillisecondsPerDay)
        {
            return false;
        }

        ms = t;
        return true;
    }

    internal static bool TryFromMilliseconds(long totalMs, DateSystem system, out ExcelDateTime result)
    {
        int n = (int)(totalMs / MillisecondsPerDay);
        int rest = (int)(totalMs % MillisecondsPerDay);
        int year, month, day;
        if (system == DateSystem.Date1904)
        {
            CivilFromDays(DaysAt1904Jan1 + n, out year, out month, out day);
        }
        else if (n == 0)
        {
            (year, month, day) = (1900, 1, 0);
        }
        else if (n == 60)
        {
            (year, month, day) = (1900, 2, 29);
        }
        else if (n < 60)
        {
            CivilFromDays(DaysAt1900Jan1 + n - 1, out year, out month, out day);
        }
        else
        {
            CivilFromDays(DaysAt1900Mar1 + n - 61, out year, out month, out day);
        }

        // Excel counts serial 1 of the 1900 system as a Sunday; the 1904 system's serial 0 (1904-01-01) is a Friday.
        int dow = system == DateSystem.Date1904 ? ((n + 5) % 7) : (((n - 1) % 7) + 7) % 7;
        result = new ExcelDateTime(year, month, day, rest / 3_600_000, rest / 60_000 % 60, rest / 1000 % 60, rest % 1000, dow, n);
        return year <= 9999;
    }

    internal static int DaysInMonth(int year, int month) => month switch
    {
        2 => IsLeap(year) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    private static bool IsLeap(int y) => (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;

    // Days since 1970-01-01 of a proleptic Gregorian date (the standard civil-calendar day-count algorithm).
    private static int DaysFromCivil(int y, int m, int d)
    {
        y -= m <= 2 ? 1 : 0;
        int era = (y >= 0 ? y : y - 399) / 400;
        int yoe = y - (era * 400);
        int doy = (((153 * (m + (m > 2 ? -3 : 9))) + 2) / 5) + d - 1;
        int doe = (yoe * 365) + (yoe / 4) - (yoe / 100) + doy;
        return (era * 146097) + doe - 719468;
    }

    private static void CivilFromDays(int z, out int y, out int m, out int d)
    {
        z += 719468;
        int era = (z >= 0 ? z : z - 146096) / 146097;
        int doe = z - (era * 146097);
        int yoe = (doe - (doe / 1460) + (doe / 36524) - (doe / 146096)) / 365;
        y = yoe + (era * 400);
        int doy = doe - ((365 * yoe) + (yoe / 4) - (yoe / 100));
        int mp = ((5 * doy) + 2) / 153;
        d = doy - (((153 * mp) + 2) / 5) + 1;
        m = mp + (mp < 10 ? 3 : -9);
        y += m <= 2 ? 1 : 0;
    }
}
