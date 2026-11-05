using System;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Calc;

internal static partial class FinancialFunctions
{
    private static void RegisterSecurities(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("COUPDAYBS", 3, 4, [], (e, a) => Coupon(e, a, 0)));
        r.Add(FunctionDefinition.Create("COUPDAYS", 3, 4, [], (e, a) => Coupon(e, a, 1)));
        r.Add(FunctionDefinition.Create("COUPDAYSNC", 3, 4, [], (e, a) => Coupon(e, a, 2)));
        r.Add(FunctionDefinition.Create("COUPNCD", 3, 4, [], (e, a) => Coupon(e, a, 3)));
        r.Add(FunctionDefinition.Create("COUPNUM", 3, 4, [], (e, a) => Coupon(e, a, 4)));
        r.Add(FunctionDefinition.Create("COUPPCD", 3, 4, [], (e, a) => Coupon(e, a, 5)));
        r.Add(FunctionDefinition.Create("PRICE", 6, 7, [], PriceFunction));
        r.Add(FunctionDefinition.Create("YIELD", 6, 7, [], YieldFunction));
        r.Add(FunctionDefinition.Create("DURATION", 5, 6, [], (e, a) => DurationFunction(e, a, false)));
        r.Add(FunctionDefinition.Create("MDURATION", 5, 6, [], (e, a) => DurationFunction(e, a, true)));
        r.Add(FunctionDefinition.Create("ACCRINT", 6, 8, [], AccrintFunction));
        r.Add(FunctionDefinition.Create("ACCRINTM", 3, 5, [], AccrintmFunction));
        r.Add(FunctionDefinition.Create("DISC", 4, 5, [], DiscFunction));
        r.Add(FunctionDefinition.Create("INTRATE", 4, 5, [], IntrateFunction));
        r.Add(FunctionDefinition.Create("RECEIVED", 4, 5, [], ReceivedFunction));
        r.Add(FunctionDefinition.Create("PRICEDISC", 4, 5, [], PriceDiscFunction));
        r.Add(FunctionDefinition.Create("YIELDDISC", 4, 5, [], YieldDiscFunction));
        r.Add(FunctionDefinition.Create("PRICEMAT", 5, 6, [], PriceMatFunction));
        r.Add(FunctionDefinition.Create("YIELDMAT", 5, 6, [], YieldMatFunction));
        r.Add(FunctionDefinition.Create("TBILLPRICE", 3, 3, [], (e, a) => TBill(e, a, 0)));
        r.Add(FunctionDefinition.Create("TBILLYIELD", 3, 3, [], (e, a) => TBill(e, a, 1)));
        r.Add(FunctionDefinition.Create("TBILLEQ", 3, 3, [], (e, a) => TBill(e, a, 2)));
        r.Add(FunctionDefinition.Create("ODDFPRICE", 8, 9, [], (e, a) => OddFirst(e, a, false)));
        r.Add(FunctionDefinition.Create("ODDFYIELD", 8, 9, [], (e, a) => OddFirst(e, a, true)));
        r.Add(FunctionDefinition.Create("ODDLPRICE", 7, 8, [], (e, a) => OddLast(e, a, false)));
        r.Add(FunctionDefinition.Create("ODDLYIELD", 7, 8, [], (e, a) => OddLast(e, a, true)));
    }

    // [MS-OI29500]/Excel: coupon dates are counted back from maturity by 12/frequency months; a maturity on a month's last day gives month ends.
    private static CalcValue Coupon(Evaluator ev, ReadOnlySpan<CalcValue> a, int which)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1), f = x.Frequency(2), b = x.Basis(3);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || !CouponInfo.TryCreate(s, m, f, b, x.DateSystem, out CouponInfo c))
        {
            return NumError;
        }

        return which switch
        {
            0 => Num(c.A),
            1 => Num(CouponDays(c, f, b)),
            2 => Num(c.Dsc),
            3 => Num(c.Next),
            4 => Num(c.Count),
            _ => Num(c.Previous),
        };
    }

    // COUPDAYS: the actual length of the period on basis 1, otherwise the basis's notional year divided by the frequency.
    private static double CouponDays(in CouponInfo c, int f, int basis) => basis == 1 ? c.Next - c.Previous : basis == 3 ? 365.0 / f : 360.0 / f;

    private static double BondPrice(in CouponInfo c, double rate, double yld, double redemption, int f)
    {
        double cpn = 100 * rate / f;
        double y = yld / f;
        double t = c.Dsc / c.E;
        double accrued = cpn * c.A / c.E;
        if (c.Count == 1)
        {
            return ((redemption + cpn) / (1 + (t * y))) - accrued;
        }

        double p = redemption / Math.Pow(1 + y, c.Count - 1 + t);
        for (int k = 1; k <= c.Count; k++)
        {
            p += cpn / Math.Pow(1 + y, k - 1 + t);
        }

        return p - accrued;
    }

    private static CalcValue PriceFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double rate = x.Num(2), yld = x.Num(3), red = x.Num(4);
        int f = x.Frequency(5), b = x.Basis(6);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || rate < 0 || yld < 0 || red <= 0 || !CouponInfo.TryCreate(s, m, f, b, x.DateSystem, out CouponInfo c))
        {
            return NumError;
        }

        return Num(BondPrice(c, rate, yld, red, f));
    }

    private static CalcValue YieldFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double rate = x.Num(2), pr = x.Num(3), red = x.Num(4);
        int f = x.Frequency(5), b = x.Basis(6);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || rate < 0 || pr <= 0 || red <= 0 || !CouponInfo.TryCreate(s, m, f, b, x.DateSystem, out CouponInfo c))
        {
            return NumError;
        }

        if (c.Count == 1)
        {
            double cpn = rate / f;
            double basis = (pr / 100) + (c.A / c.E * cpn);
            return Num(((red / 100) + cpn - basis) / basis * (f * c.E / c.Dsc));
        }

        // The price falls as the yield rises, so the yield that gives the quoted price is unique.
        return FinSupport.SolveDecreasing(y => BondPrice(c, rate, y, red, f), pr, -f + 1e-9, out double root) ? Num(root) : NumError;
    }

    private static CalcValue DurationFunction(Evaluator ev, ReadOnlySpan<CalcValue> a, bool modified)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double coupon = x.Num(2), yld = x.Num(3);
        int f = x.Frequency(4), b = x.Basis(5);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || coupon < 0 || yld < 0 || !CouponInfo.TryCreate(s, m, f, b, x.DateSystem, out CouponInfo c))
        {
            return NumError;
        }

        double cpn = 100 * coupon / f, y = yld / f, t0 = c.Dsc / c.E;
        double sumPv = 0, sumT = 0;
        for (int k = 1; k <= c.Count; k++)
        {
            double cf = cpn + (k == c.Count ? 100 : 0);
            double pv = cf / Math.Pow(1 + y, k - 1 + t0);
            sumPv += pv;
            sumT += (k - 1 + t0) / f * pv;
        }

        double d = sumT / sumPv;
        return Num(modified ? d / (1 + y) : d);
    }

    // Days of [lo, hi] on the basis divided by the notional length of the coupon period [qa, qb] it lies in.
    private static bool PeriodFraction(int lo, int hi, int qa, int qb, int f, int basis, DateSystem ds, out double fraction)
    {
        fraction = 0;
        if (!DayCount.TryDays(lo, hi, basis, ds, out double days))
        {
            return false;
        }

        double length = basis == 1 ? qb - qa : basis == 3 ? 365.0 / f : 360.0 / f;
        fraction = days / length;
        return true;
    }

    /// <summary>
    /// Adds up, over the quasi-coupon periods (counted from <paramref name="anchor"/> in both directions by 12/frequency months) that overlap
    /// [<paramref name="from"/>, <paramref name="to"/>], the fraction of each period the span covers.
    /// </summary>
    private static bool SumFractions(int anchor, int f, int basis, DateSystem ds, int from, int to, out double sum)
    {
        sum = 0;
        if (to <= from)
        {
            return true;
        }

        int step = 12 / f;
        bool Q(long j, out int date) => DayCount.TryShiftMonths(anchor, j * step, ds, out date);
        if (!DayCount.TryMonthsBetween(from, anchor, ds, out long months))
        {
            return false;
        }

        long j = (long)Math.Floor((double)months / step);
        int guard = 0;
        while (!Q(j, out int qj) || qj > from)
        {
            j++;
            if (++guard > 200_000)
            {
                return false;
            }
        }

        while (Q(j - 1, out int qn) && qn <= from)
        {
            j--;
            if (++guard > 400_000)
            {
                return false;
            }
        }

        while (true)
        {
            if (!Q(j, out int qa) || !Q(j - 1, out int qb))
            {
                return false;
            }

            if (qa >= to)
            {
                return true;
            }

            int lo = Math.Max(from, qa), hi = Math.Min(to, qb);
            if (hi > lo)
            {
                if (!PeriodFraction(lo, hi, qa, qb, f, basis, ds, out double fr))
                {
                    return false;
                }

                sum += fr;
            }

            j--;
            if (++guard > 600_000)
            {
                return false;
            }
        }
    }

    private static CalcValue AccrintFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int issue = x.Date(0), first = x.Date(1), settle = x.Date(2);
        double rate = x.Num(3), par = x.Num(4, 1000);
        int f = x.Frequency(5), b = x.Basis(6);
        bool fromIssue = x.Bool(7, true);
        if (x.Failed)
        {
            return x.Error;
        }

        if (rate <= 0 || par <= 0 || issue >= settle)
        {
            return NumError;
        }

        int start = fromIssue ? issue : first;
        if (!SumFractions(first, f, b, x.DateSystem, start, settle, out double sum))
        {
            return NumError;
        }

        return Num(par * rate / f * sum);
    }

    private static CalcValue AccrintmFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int issue = x.Date(0), settle = x.Date(1);
        double rate = x.Num(2), par = x.Num(3, 1000);
        int b = x.Basis(4);
        if (x.Failed)
        {
            return x.Error;
        }

        if (rate <= 0 || par <= 0 || issue >= settle)
        {
            return NumError;
        }

        double yf = DayCount.YearFrac(issue, settle, b, x.DateSystem);
        return double.IsNaN(yf) ? NumError : Num(par * rate * yf);
    }

    private static bool YearSpan(ref FinArgs x, int s, int m, int b, out double yf)
    {
        yf = DayCount.YearFrac(s, m, b, x.DateSystem);
        return !double.IsNaN(yf) && yf > 0;
    }

    private static CalcValue DiscFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double pr = x.Num(2), red = x.Num(3);
        int b = x.Basis(4);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || pr <= 0 || red <= 0 || !YearSpan(ref x, s, m, b, out double yf))
        {
            return NumError;
        }

        return Num((red - pr) / red / yf);
    }

    private static CalcValue IntrateFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double inv = x.Num(2), red = x.Num(3);
        int b = x.Basis(4);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || inv <= 0 || red <= 0 || !YearSpan(ref x, s, m, b, out double yf))
        {
            return NumError;
        }

        return Num((red - inv) / inv / yf);
    }

    private static CalcValue ReceivedFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double inv = x.Num(2), disc = x.Num(3);
        int b = x.Basis(4);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || inv <= 0 || disc <= 0 || !YearSpan(ref x, s, m, b, out double yf))
        {
            return NumError;
        }

        double denominator = 1 - (disc * yf);
        return denominator <= 0 ? NumError : Num(inv / denominator);
    }

    private static CalcValue PriceDiscFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double disc = x.Num(2), red = x.Num(3);
        int b = x.Basis(4);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || disc <= 0 || red <= 0 || !YearSpan(ref x, s, m, b, out double yf))
        {
            return NumError;
        }

        return Num(red - (disc * red * yf));
    }

    private static CalcValue YieldDiscFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double pr = x.Num(2), red = x.Num(3);
        int b = x.Basis(4);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || pr <= 0 || red <= 0 || !YearSpan(ref x, s, m, b, out double yf))
        {
            return NumError;
        }

        return Num((red - pr) / pr / yf);
    }

    private static CalcValue PriceMatFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1), issue = x.Date(2);
        double rate = x.Num(3), yld = x.Num(4);
        int b = x.Basis(5);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || rate < 0 || yld < 0 || !YearSpan(ref x, s, m, b, out double dsm))
        {
            return NumError;
        }

        double dim = DayCount.YearFrac(issue, m, b, x.DateSystem), ais = DayCount.YearFrac(issue, s, b, x.DateSystem);
        if (double.IsNaN(dim) || double.IsNaN(ais))
        {
            return NumError;
        }

        return Num(((100 + (dim * rate * 100)) / (1 + (dsm * yld))) - (ais * rate * 100));
    }

    private static CalcValue YieldMatFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1), issue = x.Date(2);
        double rate = x.Num(3), pr = x.Num(4);
        int b = x.Basis(5);
        if (x.Failed)
        {
            return x.Error;
        }

        if (s >= m || rate < 0 || pr <= 0 || !YearSpan(ref x, s, m, b, out double dsm))
        {
            return NumError;
        }

        double dim = DayCount.YearFrac(issue, m, b, x.DateSystem), ais = DayCount.YearFrac(issue, s, b, x.DateSystem);
        if (double.IsNaN(dim) || double.IsNaN(ais))
        {
            return NumError;
        }

        double current = (pr / 100) + (ais * rate);
        return Num(((1 + (dim * rate)) - current) / current / dsm);
    }

    // Treasury bills: discount on a 360-day year; the bond-equivalent yield uses 365 days, and for more than half a year a quadratic.
    private static CalcValue TBill(Evaluator ev, ReadOnlySpan<CalcValue> a, int which)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1);
        double v = x.Num(2);
        if (x.Failed)
        {
            return x.Error;
        }

        double dsm = m - s;
        if (s >= m || dsm > 365 || v <= 0)
        {
            return NumError;
        }

        switch (which)
        {
            case 0:
                return Num(100 * (1 - (v * dsm / 360)));
            case 1:
                return Num((100 - v) / v * (360 / dsm));
            default:
                if (dsm <= 182)
                {
                    double den = 360 - (v * dsm);
                    return den <= 0 ? NumError : Num(365 * v / den);
                }

                double price = 100 * (1 - (v * dsm / 360)) / 100;
                if (price <= 0)
                {
                    return NumError;
                }

                double year = TbillYearLength(s, m, x.DateSystem);
                double qa = (dsm / (2 * year)) - 0.25, qb = dsm / year, qc = (price - 1) / price;
                double disc = (qb * qb) - (4 * qa * qc);
                return disc < 0 ? NumError : Num((-qb + Math.Sqrt(disc)) / (2 * qa));
        }
    }

    // The bond-equivalent yield of a bill over more than half a year counts a leap day when the bill spans one.
    private static double TbillYearLength(int s, int m, DateSystem ds)
    {
        if (DayCount.TryYmd(s, ds, out Ymd a) && DayCount.TryYmd(m, ds, out Ymd b))
        {
            for (int y = a.Year; y <= b.Year; y++)
            {
                if (DayCount.IsLeap(y) && ExcelDate.TryToSerial(y, 2, 29, ds, out int f) && f > s && f <= m)
                {
                    return 366;
                }
            }
        }

        return 365;
    }

    // Odd first period: the first coupon covers the quasi-coupon periods between issue and the first coupon date.
    private static double OddFirstPrice(double cpn, double y, int count, double accrued, double toFirst, double sinceIssueToFirst, double red)
    {
        double p = red / Math.Pow(1 + y, count - 1 + toFirst);
        p += cpn * sinceIssueToFirst / Math.Pow(1 + y, toFirst);
        for (int k = 2; k <= count; k++)
        {
            p += cpn / Math.Pow(1 + y, k - 1 + toFirst);
        }

        return p - (cpn * accrued);
    }

    private static CalcValue OddFirst(Evaluator ev, ReadOnlySpan<CalcValue> a, bool wantYield)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1), issue = x.Date(2), fc = x.Date(3);
        double rate = x.Num(4), v = x.Num(5), red = x.Num(6);
        int f = x.Frequency(7), b = x.Basis(8);
        if (x.Failed)
        {
            return x.Error;
        }

        if (!(m > fc && fc > s && s > issue) || rate < 0 || v < 0 || red <= 0 || (wantYield && v <= 0))
        {
            return NumError;
        }

        DateSystem ds = x.DateSystem;
        int step = 12 / f;
        if (!DayCount.TryMonthsBetween(fc, m, ds, out long months)
            || !SumFractions(fc, f, b, ds, issue, s, out double accrued)
            || !SumFractions(fc, f, b, ds, issue, fc, out double dfc)
            || !SumFractions(fc, f, b, ds, s, fc, out double toFirst))
        {
            return NumError;
        }

        int count = (int)Math.Min(1_000_000, ((months + step - 1) / step) + 1);
        double cpn = 100 * rate / f;
        double Price(double yld) => OddFirstPrice(cpn, yld / f, count, accrued, toFirst, dfc, red);
        if (!wantYield)
        {
            return Num(Price(v));
        }

        return FinSupport.SolveDecreasing(Price, v, -f + 1e-9, out double root) ? Num(root) : NumError;
    }

    private static CalcValue OddLast(Evaluator ev, ReadOnlySpan<CalcValue> a, bool wantYield)
    {
        var x = new FinArgs(ev, a);
        int s = x.Date(0), m = x.Date(1), last = x.Date(2);
        double rate = x.Num(3), v = x.Num(4), red = x.Num(5);
        int f = x.Frequency(6), b = x.Basis(7);
        if (x.Failed)
        {
            return x.Error;
        }

        if (!(m > s && s > last) || rate < 0 || v < 0 || red <= 0 || (wantYield && v <= 0))
        {
            return NumError;
        }

        DateSystem ds = x.DateSystem;
        if (!SumFractions(last, f, b, ds, last, m, out double dc)
            || !SumFractions(last, f, b, ds, last, s, out double accrued)
            || !SumFractions(last, f, b, ds, s, m, out double toMaturity))
        {
            return NumError;
        }

        double cpn = 100 * rate / f;
        if (!wantYield)
        {
            return Num(((red + (cpn * dc)) / (1 + (v / f * toMaturity))) - (cpn * accrued));
        }

        double denominator = v + (cpn * accrued);
        return denominator <= 0 || toMaturity <= 0 ? NumError : Num((((red + (cpn * dc)) / denominator) - 1) * f / toMaturity);
    }
}
