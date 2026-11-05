using System;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>A calendar date broken out of a serial.</summary>
internal readonly record struct Ymd(int Year, int Month, int Day);

/// <summary>
/// The day-count bases Excel's financial functions share (<c>YEARFRAC</c>, <c>COUP*</c>, <c>PRICE</c>, …): 0 US (NASD) 30/360, 1 actual/actual,
/// 2 actual/360, 3 actual/365, 4 European 30/360; and the coupon-date arithmetic that keeps month-end maturities on month ends.
/// </summary>
internal static class DayCount
{
    public static bool IsLeap(int y) => (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;

    public static int DaysInMonth(int y, int m) => m switch
    {
        2 => IsLeap(y) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    public static bool TryYmd(int serial, DateSystem ds, out Ymd result)
    {
        if (!ExcelDate.TryFromSerial(serial, ds, out ExcelDateTime t))
        {
            result = default;
            return false;
        }

        result = new Ymd(t.Year, t.Month, t.Day);
        return true;
    }

    private static bool IsLastOfFeb(Ymd d) => d.Month == 2 && d.Day == DaysInMonth(d.Year, 2);

    /// <summary>Days between two dates on a 30/360 basis: US (NASD, with the February month-end rules) or European.</summary>
    public static int Days360(Ymd a, Ymd b, bool european)
    {
        int d1 = a.Day, d2 = b.Day;
        if (european)
        {
            if (d1 == 31)
            {
                d1 = 30;
            }

            if (d2 == 31)
            {
                d2 = 30;
            }
        }
        else
        {
            // [MS-OI29500]-style US rule as Excel's YEARFRAC applies it: last day of February counts as the 30th.
            if (IsLastOfFeb(a) && IsLastOfFeb(b))
            {
                d2 = 30;
            }

            if (IsLastOfFeb(a))
            {
                d1 = 30;
            }

            if (d2 == 31 && d1 >= 30)
            {
                d2 = 30;
            }

            if (d1 == 31)
            {
                d1 = 30;
            }
        }

        return ((b.Year - a.Year) * 360) + ((b.Month - a.Month) * 30) + (d2 - d1);
    }

    /// <summary>Days from <paramref name="s1"/> to <paramref name="s2"/> as the basis counts them: 30/360 for bases 0 and 4, actual otherwise.</summary>
    public static bool TryDays(int s1, int s2, int basis, DateSystem ds, out double days)
    {
        days = 0;
        if (basis is 0 or 4)
        {
            if (!TryYmd(s1, ds, out Ymd a) || !TryYmd(s2, ds, out Ymd b))
            {
                return false;
            }

            days = Days360(a, b, basis == 4);
            return true;
        }

        days = s2 - s1;
        return true;
    }

    /// <summary>The length of a year in days for the year fraction on basis 1 (actual/actual), following Excel's <c>YEARFRAC</c>.</summary>
    private static double Basis1Year(Ymd a, Ymd b, int s1, int s2, DateSystem ds)
    {
        if (a.Year == b.Year)
        {
            return IsLeap(a.Year) ? 366 : 365;
        }

        bool withinYear = b.Year == a.Year + 1 && (a.Month > b.Month || (a.Month == b.Month && a.Day >= b.Day));
        if (withinYear)
        {
            // Feb 29 inside the span makes it a 366-day year.
            if (IsLeap(a.Year) && (a.Month < 2 || (a.Month == 2 && a.Day <= 29)) && TryFeb29(a.Year, ds, out int f1) && f1 >= s1 && f1 <= s2)
            {
                return 366;
            }

            if (IsLeap(b.Year) && TryFeb29(b.Year, ds, out int f2) && f2 >= s1 && f2 <= s2)
            {
                return 366;
            }

            return 365;
        }

        double total = 0;
        for (int y = a.Year; y <= b.Year; y++)
        {
            total += IsLeap(y) ? 366 : 365;
        }

        return total / (b.Year - a.Year + 1);
    }

    private static bool TryFeb29(int year, DateSystem ds, out int serial) => ExcelDate.TryToSerial(year, 2, 29, ds, out serial);

    /// <summary>
    /// The fraction of a year between two dates on a basis (<c>YEARFRAC</c>). The dates may come in either order. Returns NaN when a date is
    /// outside the calendar.
    /// </summary>
    public static double YearFrac(int s1, int s2, int basis, DateSystem ds)
    {
        if (s1 > s2)
        {
            (s1, s2) = (s2, s1);
        }

        if (!TryYmd(s1, ds, out Ymd a) || !TryYmd(s2, ds, out Ymd b))
        {
            return double.NaN;
        }

        return basis switch
        {
            0 => Days360(a, b, false) / 360.0,
            4 => Days360(a, b, true) / 360.0,
            1 => s1 == s2 ? 0 : (s2 - s1) / Basis1Year(a, b, s1, s2, ds),
            2 => (s2 - s1) / 360.0,
            _ => (s2 - s1) / 365.0,
        };
    }

    /// <summary>
    /// The date <paramref name="monthsBack"/> months before (negative: after) <paramref name="anchor"/>, keeping the anchor's day of month,
    /// clamped to the month's length; an anchor on a month's last day gives month ends.
    /// </summary>
    public static bool TryShiftMonths(int anchor, long monthsBack, DateSystem ds, out int serial)
    {
        serial = 0;
        if (!TryYmd(anchor, ds, out Ymd a))
        {
            return false;
        }

        bool eom = a.Day == DaysInMonth(a.Year, a.Month);
        long total = ((long)a.Year * 12) + (a.Month - 1) - monthsBack;
        if (total < 1900L * 12 || total > (9999L * 12) + 11)
        {
            return false;
        }

        int y = (int)(total / 12);
        int m = (int)(total % 12) + 1;
        int dim = DaysInMonth(y, m);
        int d = eom ? dim : Math.Min(a.Day, dim);
        return ExcelDate.TryToSerial(y, m, d, ds, out serial);
    }

    /// <summary>The whole months from <paramref name="from"/> to <paramref name="to"/> by calendar month number (day ignored).</summary>
    public static bool TryMonthsBetween(int from, int to, DateSystem ds, out long months)
    {
        months = 0;
        if (!TryYmd(from, ds, out Ymd a) || !TryYmd(to, ds, out Ymd b))
        {
            return false;
        }

        months = (((long)b.Year * 12) + b.Month) - (((long)a.Year * 12) + a.Month);
        return true;
    }
}

/// <summary>Settlement, previous and next coupon dates and the day counts the bond formulas use.</summary>
internal readonly struct CouponInfo
{
    public int Previous { get; init; }

    public int Next { get; init; }

    /// <summary>Coupons payable between settlement and maturity, counting the one at maturity.</summary>
    public int Count { get; init; }

    /// <summary>Days from the previous coupon to settlement.</summary>
    public double A { get; init; }

    /// <summary>Days in the coupon period.</summary>
    public double E { get; init; }

    /// <summary>Days from settlement to the next coupon.</summary>
    public double Dsc { get; init; }

    /// <summary>
    /// Works out the coupon schedule counted back from maturity by 12/frequency months.
    /// </summary>
    public static bool TryCreate(int settlement, int maturity, int frequency, int basis, DateSystem ds, out CouponInfo info)
    {
        info = default;
        int step = 12 / frequency;
        if (!DayCount.TryMonthsBetween(settlement, maturity, ds, out long md))
        {
            return false;
        }

        long j = (md / step) + 1;
        int Coupon(long k) => DayCount.TryShiftMonths(maturity, k * step, ds, out int v) ? v : -1;

        // c(j) decreases as j grows (c(0) is maturity); find the largest j whose coupon still falls after settlement.
        while (j > 0 && !(Coupon(j) > settlement))
        {
            j--;
        }

        while (Coupon(j + 1) > settlement)
        {
            j++;
        }

        int ncd = Coupon(j);
        int pcd = Coupon(j + 1);
        if (ncd <= settlement || pcd < 0 || pcd > settlement)
        {
            return false;
        }

        double a, e, dsc;
        switch (basis)
        {
            case 0:
            case 4:
                if (!DayCount.TryDays(pcd, settlement, basis, ds, out a))
                {
                    return false;
                }

                e = 360.0 / frequency;
                dsc = e - a;
                break;
            case 1:
                a = settlement - pcd;
                e = ncd - pcd;
                dsc = ncd - settlement;
                break;
            case 2:
                a = settlement - pcd;
                e = 360.0 / frequency;
                dsc = ncd - settlement;
                break;
            default:
                a = settlement - pcd;
                e = 365.0 / frequency;
                dsc = ncd - settlement;
                break;
        }

        info = new CouponInfo { Previous = pcd, Next = ncd, Count = (int)(j + 1), A = a, E = e, Dsc = dsc };
        return true;
    }
}
