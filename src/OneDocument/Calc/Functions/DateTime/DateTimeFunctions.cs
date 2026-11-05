using System;
using System.Collections.Generic;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>The date and time functions (Microsoft's "Date and time functions" reference pages), counting serials in the evaluator's date system.</summary>
internal static class DateTimeFunctions
{
    private static readonly ArgKind[] V = [];

    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("TODAY", 0, 0, V, (c, _) => Fx.Num(Math.Floor(Now(c))), FunctionTraits.Volatile));
        r.Add(FunctionDefinition.Create("NOW", 0, 0, V, (c, _) => Fx.Num(Now(c)), FunctionTraits.Volatile));
        r.Add(FunctionDefinition.Create("DATE", 3, 3, V, DateFn));
        r.Add(FunctionDefinition.Create("TIME", 3, 3, V, TimeFn));
        r.Add(FunctionDefinition.Create("DATEVALUE", 1, 1, V, DateValue));
        r.Add(FunctionDefinition.Create("TIMEVALUE", 1, 1, V, TimeValue));
        r.Add(FunctionDefinition.Create("YEAR", 1, 1, V, (c, a) => Part(c, a[0], p => p.Year)));
        r.Add(FunctionDefinition.Create("MONTH", 1, 1, V, (c, a) => Part(c, a[0], p => p.Month)));
        r.Add(FunctionDefinition.Create("DAY", 1, 1, V, (c, a) => Part(c, a[0], p => p.Day)));
        r.Add(FunctionDefinition.Create("HOUR", 1, 1, V, (c, a) => Part(c, a[0], p => p.Hour)));
        r.Add(FunctionDefinition.Create("MINUTE", 1, 1, V, (c, a) => Part(c, a[0], p => p.Minute)));
        r.Add(FunctionDefinition.Create("SECOND", 1, 1, V, (c, a) => Part(c, a[0], p => p.Second)));
        r.Add(FunctionDefinition.Create("WEEKDAY", 1, 2, V, WeekdayFn));
        r.Add(FunctionDefinition.Create("WEEKNUM", 1, 2, V, WeekNumFn));
        r.Add(FunctionDefinition.Create("ISOWEEKNUM", 1, 1, V, IsoWeekNumFn));
        r.Add(FunctionDefinition.Create("EDATE", 2, 2, V, (c, a) => Shift(c, a, false)));
        r.Add(FunctionDefinition.Create("EOMONTH", 2, 2, V, (c, a) => Shift(c, a, true)));
        r.Add(FunctionDefinition.Create("DAYS", 2, 2, V, DaysFn));
        r.Add(FunctionDefinition.Create("DAYS360", 2, 3, V, Days360Fn));
        r.Add(FunctionDefinition.Create("DATEDIF", 3, 3, V, DateDif));
        r.Add(FunctionDefinition.Create("YEARFRAC", 2, 3, V, YearFracFn));
        r.Add(FunctionDefinition.Create("NETWORKDAYS", 2, 3, [ArgKind.Value, ArgKind.Value, ArgKind.Any], (c, a) => NetworkDays(c, a[0], a[1], CalcValue.Missing, a.Length > 2 ? a[2] : CalcValue.Missing)));
        r.Add(FunctionDefinition.Create("NETWORKDAYS.INTL", 2, 4, [ArgKind.Value, ArgKind.Value, ArgKind.Value, ArgKind.Any], (c, a) => NetworkDays(c, a[0], a[1], a.Length > 2 ? a[2] : CalcValue.Missing, a.Length > 3 ? a[3] : CalcValue.Missing)));
        r.Add(FunctionDefinition.Create("WORKDAY", 2, 3, [ArgKind.Value, ArgKind.Value, ArgKind.Any], (c, a) => WorkDay(c, a[0], a[1], CalcValue.Missing, a.Length > 2 ? a[2] : CalcValue.Missing)));
        r.Add(FunctionDefinition.Create("WORKDAY.INTL", 2, 4, [ArgKind.Value, ArgKind.Value, ArgKind.Value, ArgKind.Any], (c, a) => WorkDay(c, a[0], a[1], a.Length > 2 ? a[2] : CalcValue.Missing, a.Length > 3 ? a[3] : CalcValue.Missing)));
    }

    private static CalcValue Bad => Fx.Err(CalcError.Num);

    // ---- clock and serials -------------------------------------------------------------------------------------------------------

    private static DateTime LocalNow(Evaluator ev)
    {
        TimeProvider clock = ev.Resolver.Clock ?? ev.Options.Clock ?? TimeProvider.System;
        return clock.GetLocalNow().DateTime;
    }

    private static double Now(Evaluator ev)
    {
        // The 1900 and 1904 systems both count from their own epoch; the time of day is a fraction of a day.
        return ExcelDate.TryToSerial(LocalNow(ev), ev.Options.DateSystem, out double s) ? s : 0;
    }

    // A date argument as a serial: numbers and booleans as they are, text as date text (the evaluator's date system) or else as a number.
    private static bool Serial(Evaluator ev, in CalcValue v, out double serial, out CalcValue error)
    {
        error = default;
        serial = 0;
        if (v.Kind == ValueKind.Text)
        {
            if (DateText.TryParse(v.Text.Trim(), ev.Options.DateSystem, LocalNow(ev).Year, out double d, out double t, out _, out _))
            {
                serial = d + t;
                return true;
            }
        }

        return Fx.TryNum(v, out serial, out error);
    }

    // A date argument as a whole-day serial that is inside the date system's range.
    private static bool Day(Evaluator ev, in CalcValue v, out int day, out CalcValue error)
    {
        day = 0;
        if (!Serial(ev, v, out double s, out error))
        {
            return false;
        }

        s = Math.Floor(s);
        if (s < 0 || s > ExcelDate.MaxSerial(ev.Options.DateSystem))
        {
            error = Bad;
            return false;
        }

        day = (int)s;
        return true;
    }

    private static bool Parts(Evaluator ev, in CalcValue v, out ExcelDateTime parts, out CalcValue error)
    {
        parts = default;
        if (!Serial(ev, v, out double s, out error))
        {
            return false;
        }

        if (s < 0 || !ExcelDate.TryFromSerial(s, ev.Options.DateSystem, out parts))
        {
            error = Bad;
            return false;
        }

        return true;
    }

    private static CalcValue Part(Evaluator ev, in CalcValue v, Func<ExcelDateTime, int> pick) =>
        Parts(ev, v, out ExcelDateTime p, out CalcValue e) ? Fx.Num(pick(p)) : e;

    private static int DayOfWeek(int serial, DateSystem system) => (serial + (system == DateSystem.Date1904 ? 5 : 6)) % 7; // 0 = Sunday

    private static long FloorDiv(long a, long b) => (a / b) - ((a % b != 0 && (a < 0) != (b < 0)) ? 1 : 0);

    private static bool FirstOfMonth(int year, int month, DateSystem system, out int serial) => ExcelDate.TryToSerial(year, month, 1, system, out serial);

    // ---- DATE, TIME, DATEVALUE, TIMEVALUE ----------------------------------------------------------------------------------------

    private static CalcValue DateFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double y, out CalcValue e) || !Fx.TryNum(a[1], out double m, out e) || !Fx.TryNum(a[2], out double d, out e))
        {
            return e;
        }

        y = Math.Truncate(y);
        m = Math.Truncate(m);
        d = Math.Truncate(d);
        if (y < 0 || y >= 10000 || Math.Abs(m) > 1e8 || Math.Abs(d) > 1e8)
        {
            return Bad;
        }

        // ECMA-376 does not describe DATE; Microsoft's page: a year from 0 to 1899 has 1900 added.
        if (y < 1900)
        {
            y += 1900;
        }

        long total = ((long)y * 12) + (long)m - 1;
        long ny = FloorDiv(total, 12);
        int nm = (int)(total - (ny * 12)) + 1;
        DateSystem sys = ev.Options.DateSystem;
        if (ny < 1900 || ny > 9999 || !FirstOfMonth((int)ny, nm, sys, out int first))
        {
            return Bad;
        }

        double serial = first + d - 1;
        return serial < 0 || serial > ExcelDate.MaxSerial(sys) ? Bad : Fx.Num(serial);
    }

    private static CalcValue TimeFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double h, out CalcValue e) || !Fx.TryNum(a[1], out double m, out e) || !Fx.TryNum(a[2], out double s, out e))
        {
            return e;
        }

        h = Math.Truncate(h);
        m = Math.Truncate(m);
        s = Math.Truncate(s);
        if (h < 0 || m < 0 || s < 0 || h > 32767 || m > 32767 || s > 32767)
        {
            return Bad;
        }

        double seconds = (h * 3600) + (m * 60) + s;
        return Fx.Num((seconds % 86400) / 86400);
    }

    private static CalcValue DateValue(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (a[0].Kind != ValueKind.Text)
        {
            return Fx.Err(CalcError.Value);
        }

        return DateText.TryParse(a[0].Text.Trim(), ev.Options.DateSystem, LocalNow(ev).Year, out double d, out _, out bool hasDate, out _) && hasDate
            ? Fx.Num(d)
            : Fx.Err(CalcError.Value);
    }

    private static CalcValue TimeValue(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (a[0].Kind != ValueKind.Text)
        {
            return Fx.Err(CalcError.Value);
        }

        return DateText.TryParse(a[0].Text.Trim(), ev.Options.DateSystem, LocalNow(ev).Year, out _, out double t, out _, out _)
            ? Fx.Num(t)
            : Fx.Err(CalcError.Value);
    }

    // ---- weekdays and week numbers -----------------------------------------------------------------------------------------------

    private static bool OptionalType(in ReadOnlySpan<CalcValue> a, int index, int dflt, out int type, out CalcValue error)
    {
        type = dflt;
        error = default;
        if (a.Length > index && !a[index].IsMissing)
        {
            if (!Fx.TryNum(a[index], out double t, out error))
            {
                return false;
            }

            type = (int)Math.Truncate(Math.Max(-1e6, Math.Min(1e6, t)));
        }

        return true;
    }

    private static CalcValue WeekdayFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Day(ev, a[0], out int day, out CalcValue e) || !OptionalType(a, 1, 1, out int type, out e))
        {
            return e;
        }

        int dow = DayOfWeek(day, ev.Options.DateSystem);
        int mondayBased = (dow + 6) % 7;
        if (type == 1)
        {
            return Fx.Num(dow + 1);
        }

        if (type == 2)
        {
            return Fx.Num(mondayBased + 1);
        }

        if (type == 3)
        {
            return Fx.Num(mondayBased);
        }

        if (type is >= 11 and <= 17)
        {
            return Fx.Num((((mondayBased - (type - 11)) + 7) % 7) + 1);
        }

        return Bad;
    }

    private static CalcValue WeekNumFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Parts(ev, a[0], out ExcelDateTime p, out CalcValue e) || !OptionalType(a, 1, 1, out int type, out e))
        {
            return e;
        }

        if (type == 21)
        {
            return IsoWeek(ev, p);
        }

        // The day the week starts on, 0 = Sunday.
        int start = type switch
        {
            1 => 0,
            2 => 1,
            >= 11 and <= 16 => type - 10,
            17 => 0,
            _ => -1,
        };
        if (start < 0)
        {
            return Bad;
        }

        DateSystem sys = ev.Options.DateSystem;
        if (!FirstOfMonth(p.Year, 1, sys, out int jan1))
        {
            // Only 1904-system dates in 1900-1903 and serial 0 of the 1900 system reach here.
            return Fx.Num(0);
        }

        int offset = (DayOfWeek(jan1, sys) - start + 7) % 7;
        long week = FloorDiv(p.DayNumber - jan1 + offset, 7) + 1;
        return Fx.Num(week);
    }

    private static CalcValue IsoWeekNumFn(Evaluator ev, ReadOnlySpan<CalcValue> a) =>
        Parts(ev, a[0], out ExcelDateTime p, out CalcValue e) ? IsoWeek(ev, p) : e;

    // ISO 8601: weeks start on Monday and week 1 holds the first Thursday of the year.
    private static CalcValue IsoWeek(Evaluator ev, ExcelDateTime p)
    {
        DateSystem sys = ev.Options.DateSystem;
        if (!FirstOfMonth(p.Year, 1, sys, out int jan1))
        {
            return Fx.Num(0);
        }

        int dow = DayOfWeek(p.DayNumber, sys);
        int isoDow = dow == 0 ? 7 : dow;
        int doy = p.DayNumber - jan1 + 1;
        int week = ((doy - isoDow + 10) / 7);
        if (week < 1)
        {
            return Fx.Num(WeeksInYear(p.Year - 1, sys));
        }

        if (week > WeeksInYear(p.Year, sys))
        {
            return Fx.Num(1);
        }

        return Fx.Num(week);
    }

    private static int WeeksInYear(int year, DateSystem sys)
    {
        if (!FirstOfMonth(year, 1, sys, out int jan1))
        {
            return 52;
        }

        int days = year >= 9999 ? 365 : FirstOfMonth(year + 1, 1, sys, out int next) ? next - jan1 : 365;
        int dow = DayOfWeek(jan1, sys);
        return dow == 4 || (days == 366 && dow == 3) ? 53 : 52;
    }

    // ---- EDATE, EOMONTH ----------------------------------------------------------------------------------------------------------

    private static CalcValue Shift(Evaluator ev, ReadOnlySpan<CalcValue> a, bool endOfMonth)
    {
        if (!Day(ev, a[0], out int day, out CalcValue e) || !Fx.TryNum(a[1], out double months, out e))
        {
            return e;
        }

        months = Math.Truncate(months);
        DateSystem sys = ev.Options.DateSystem;
        if (!ExcelDate.TryFromSerial(day, sys, out ExcelDateTime p) || Math.Abs(months) > 1e6)
        {
            return Bad;
        }

        if (months == 0 && !endOfMonth)
        {
            return Fx.Num(day);
        }

        long total = ((long)p.Year * 12) + (p.Month - 1) + (long)months;
        long ny = FloorDiv(total, 12);
        int nm = (int)(total - (ny * 12)) + 1;
        if (ny < 1900 || ny > 9999 || !FirstOfMonth((int)ny, nm, sys, out int first))
        {
            return Bad;
        }

        int dim = ny == 1900 && nm == 2 ? 29 : ExcelDate.DaysInMonth((int)ny, nm);
        double serial = endOfMonth ? first + dim - 1 : first + Math.Min(p.Day, dim) - 1;
        return serial < 0 || serial > ExcelDate.MaxSerial(sys) ? Bad : Fx.Num(serial);
    }

    // ---- day counts --------------------------------------------------------------------------------------------------------------

    private static CalcValue DaysFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Day(ev, a[0], out int end, out CalcValue e) || !Day(ev, a[1], out int start, out e))
        {
            return e;
        }

        return Fx.Num(end - start);
    }

    private static CalcValue Days360Fn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Day(ev, a[0], out int start, out CalcValue e) || !Day(ev, a[1], out int end, out e))
        {
            return e;
        }

        bool european = false;
        if (a.Length == 3 && !a[2].IsMissing)
        {
            if (CalcConvert.ToBoolean(a[2], out european) is var be && be != CalcError.None)
            {
                return Fx.Err(be);
            }
        }

        DateSystem sys = ev.Options.DateSystem;
        return DayCount.TryYmd(start, sys, out Ymd s) && DayCount.TryYmd(end, sys, out Ymd t)
            ? Fx.Num(DayCount.Days360(s, t, european))
            : Bad;
    }

    private static CalcValue YearFracFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Day(ev, a[0], out int d1, out CalcValue e) || !Day(ev, a[1], out int d2, out e))
        {
            return e;
        }

        int basis = 0;
        if (a.Length == 3 && !a[2].IsMissing)
        {
            if (!Fx.TryNum(a[2], out double b, out e))
            {
                return e;
            }

            basis = (int)Math.Truncate(Math.Max(-10, Math.Min(10, b)));
        }

        if (basis is < 0 or > 4)
        {
            return Bad;
        }

        double frac = DayCount.YearFrac(d1, d2, basis, ev.Options.DateSystem);
        return double.IsNaN(frac) ? Bad : Fx.Num(frac);
    }

    // DATEDIF (undocumented by Microsoft beyond a compatibility note): units Y, M, D, MD, YM, YD.
    private static CalcValue DateDif(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Day(ev, a[0], out int d1, out CalcValue e) || !Day(ev, a[1], out int d2, out e))
        {
            return e;
        }

        if (a[2].Kind != ValueKind.Text)
        {
            return Fx.Err(CalcError.Value);
        }

        if (d1 > d2)
        {
            return Bad;
        }

        DateSystem sys = ev.Options.DateSystem;
        ExcelDate.TryFromSerial(d1, sys, out ExcelDateTime s);
        ExcelDate.TryFromSerial(d2, sys, out ExcelDateTime t);
        switch (a[2].Text.ToUpperInvariant())
        {
            case "Y":
                return Fx.Num(t.Year - s.Year - (BeforeAnniversary(s, t) ? 1 : 0));
            case "M":
                return Fx.Num(((t.Year - s.Year) * 12) + (t.Month - s.Month) - (t.Day < s.Day ? 1 : 0));
            case "D":
                return Fx.Num(d2 - d1);
            case "MD":
                if (t.Day >= s.Day)
                {
                    return Fx.Num(t.Day - s.Day);
                }

                int pm = t.Month == 1 ? 12 : t.Month - 1;
                int py = t.Month == 1 ? t.Year - 1 : t.Year;
                return Fx.Num(t.Day + ExcelDate.DaysInMonth(Math.Max(py, 1900), pm) - s.Day);
            case "YM":
                int m = t.Month - s.Month - (t.Day < s.Day ? 1 : 0);
                return Fx.Num(m < 0 ? m + 12 : m);
            case "YD":
                int year = BeforeAnniversary(s, t) ? t.Year - 1 : t.Year;
                int day = Math.Min(s.Day, ExcelDate.DaysInMonth(year, s.Month));
                if (!ExcelDate.TryToSerial(year, s.Month, day, sys, out int anchor))
                {
                    return Bad;
                }

                return Fx.Num(d2 - anchor);
            default:
                return Bad;
        }
    }

    private static bool BeforeAnniversary(ExcelDateTime s, ExcelDateTime t) => t.Month < s.Month || (t.Month == s.Month && t.Day < s.Day);

    // ---- working days ------------------------------------------------------------------------------------------------------------

    // The weekend as seven flags indexed by weekday (0 = Sunday), from a weekend number or a seven-character Monday-first string.
    private static bool Weekend(in CalcValue spec, bool[] flags, out CalcValue error)
    {
        error = default;
        if (spec.IsMissing)
        {
            flags[0] = true;
            flags[6] = true;
            return true;
        }

        if (spec.Kind == ValueKind.Text)
        {
            string s = spec.Text;
            if (s.Length != 7)
            {
                error = Fx.Err(CalcError.Value);
                return false;
            }

            int weekendDays = 0;
            for (int i = 0; i < 7; i++)
            {
                if (s[i] is not ('0' or '1'))
                {
                    error = Fx.Err(CalcError.Value);
                    return false;
                }

                if (s[i] == '1')
                {
                    flags[(i + 1) % 7] = true;
                    weekendDays++;
                }
            }

            if (weekendDays == 7)
            {
                error = Fx.Err(CalcError.Value);
                return false;
            }

            return true;
        }

        if (!Fx.TryNum(spec, out double n, out error))
        {
            return false;
        }

        int code = (int)Math.Truncate(n);
        if (code is >= 1 and <= 7)
        {
            // 1 = Saturday+Sunday, 2 = Sunday+Monday, ... 7 = Friday+Saturday.
            int first = code == 1 ? 6 : code - 2;
            flags[first] = true;
            flags[(first + 1) % 7] = true;
            return true;
        }

        if (code is >= 11 and <= 17)
        {
            flags[code - 11] = true;
            return true;
        }

        error = Bad;
        return false;
    }

    private static bool Holidays(Evaluator ev, in CalcValue arg, HashSet<int> into, out CalcValue error)
    {
        error = default;
        if (arg.IsMissing)
        {
            return true;
        }

        var cells = new List<CalcValue>();
        CalcError ce = Fx.Flatten(ev, arg, cells);
        if (ce != CalcError.None)
        {
            error = Fx.Err(ce);
            return false;
        }

        foreach (CalcValue c in cells)
        {
            if (c.IsError)
            {
                error = c;
                return false;
            }

            if (c.IsBlank)
            {
                continue;
            }

            if (c.Kind == ValueKind.Boolean)
            {
                error = Fx.Err(CalcError.Value);
                return false;
            }

            if (!Serial(ev, c, out double s, out error))
            {
                return false;
            }

            into.Add((int)Math.Floor(s));
        }

        return true;
    }

    private static int WorkDaysPerWeek(bool[] weekend)
    {
        int n = 0;
        foreach (bool w in weekend)
        {
            if (!w)
            {
                n++;
            }
        }

        return n;
    }

    private static CalcValue NetworkDays(Evaluator ev, in CalcValue startArg, in CalcValue endArg, in CalcValue weekendArg, in CalcValue holidayArg)
    {
        if (!Day(ev, startArg, out int start, out CalcValue e) || !Day(ev, endArg, out int end, out e))
        {
            return e;
        }

        var weekend = new bool[7];
        if (!Weekend(weekendArg, weekend, out e))
        {
            return e;
        }

        var holidays = new HashSet<int>();
        if (!Holidays(ev, holidayArg, holidays, out e))
        {
            return e;
        }

        DateSystem sys = ev.Options.DateSystem;
        bool reversed = start > end;
        int lo = reversed ? end : start;
        int hi = reversed ? start : end;
        int total = hi - lo + 1;
        int perWeek = WorkDaysPerWeek(weekend);
        long count = (long)(total / 7) * perWeek;
        for (int d = lo + ((total / 7) * 7); d <= hi; d++)
        {
            if (!weekend[DayOfWeek(d, sys)])
            {
                count++;
            }
        }

        foreach (int h in holidays)
        {
            if (h >= lo && h <= hi && !weekend[DayOfWeek(h, sys)])
            {
                count--;
            }
        }

        return Fx.Num(reversed ? -count : count);
    }

    private static CalcValue WorkDay(Evaluator ev, in CalcValue startArg, in CalcValue daysArg, in CalcValue weekendArg, in CalcValue holidayArg)
    {
        if (!Day(ev, startArg, out int start, out CalcValue e) || !Fx.TryNum(daysArg, out double days, out e))
        {
            return e;
        }

        var weekend = new bool[7];
        if (!Weekend(weekendArg, weekend, out e))
        {
            return e;
        }

        var holidays = new HashSet<int>();
        if (!Holidays(ev, holidayArg, holidays, out e))
        {
            return e;
        }

        days = Math.Truncate(days);
        DateSystem sys = ev.Options.DateSystem;
        int max = ExcelDate.MaxSerial(sys);
        if (Math.Abs(days) > 4_000_000)
        {
            return Bad;
        }

        int step = days < 0 ? -1 : 1;
        long remaining = (long)Math.Abs(days);
        long cur = start;
        while (remaining > 0)
        {
            cur += step;
            if (cur < 0 || cur > max)
            {
                return Bad;
            }

            if (!weekend[DayOfWeek((int)cur, sys)] && !holidays.Contains((int)cur))
            {
                remaining--;
            }
        }

        return Fx.Num(cur);
    }
}
