using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>The financial functions: time value of money, cash-flow returns, depreciation, securities and bonds.</summary>
internal static partial class FinancialFunctions
{
    private static CalcValue Num(double d) => Fx.Num(d);
    private static CalcValue NumError => Fx.Err(CalcError.Num);
    private static CalcValue DivError => Fx.Err(CalcError.Div0);

    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("FV", 3, 5, [], FvFunction));
        r.Add(FunctionDefinition.Create("PV", 3, 5, [], PvFunction));
        r.Add(FunctionDefinition.Create("PMT", 3, 5, [], PmtFunction));
        r.Add(FunctionDefinition.Create("NPER", 3, 5, [], NperFunction));
        r.Add(FunctionDefinition.Create("RATE", 3, 6, [], RateFunction));
        r.Add(FunctionDefinition.Create("IPMT", 4, 6, [], (e, a) => InterestOrPrincipal(e, a, true)));
        r.Add(FunctionDefinition.Create("PPMT", 4, 6, [], (e, a) => InterestOrPrincipal(e, a, false)));
        r.Add(FunctionDefinition.Create("ISPMT", 4, 4, [], IspmtFunction));
        r.Add(FunctionDefinition.Create("CUMIPMT", 6, 6, [], (e, a) => Cumulative(e, a, true)));
        r.Add(FunctionDefinition.Create("CUMPRINC", 6, 6, [], (e, a) => Cumulative(e, a, false)));
        r.Add(FunctionDefinition.Create("EFFECT", 2, 2, [], EffectFunction));
        r.Add(FunctionDefinition.Create("NOMINAL", 2, 2, [], NominalFunction));
        r.Add(FunctionDefinition.Create("RRI", 3, 3, [], RriFunction));
        r.Add(FunctionDefinition.Create("PDURATION", 3, 3, [], PdurationFunction));
        r.Add(FunctionDefinition.Create("FVSCHEDULE", 2, 2, [ArgKind.Value, ArgKind.Array], FvScheduleFunction));
        r.Add(FunctionDefinition.Create("NPV", 2, int.MaxValue, [ArgKind.Value, ArgKind.Any], NpvFunction, repeatLast: true));
        r.Add(FunctionDefinition.Create("XNPV", 3, 3, [ArgKind.Value, ArgKind.Array, ArgKind.Array], XnpvFunction));
        r.Add(FunctionDefinition.Create("IRR", 1, 2, [ArgKind.Array, ArgKind.Value], IrrFunction));
        r.Add(FunctionDefinition.Create("XIRR", 2, 3, [ArgKind.Array, ArgKind.Array, ArgKind.Value], XirrFunction));
        r.Add(FunctionDefinition.Create("MIRR", 3, 3, [ArgKind.Array, ArgKind.Value, ArgKind.Value], MirrFunction));
        r.Add(FunctionDefinition.Create("DOLLARDE", 2, 2, [], (e, a) => Dollar(e, a, true)));
        r.Add(FunctionDefinition.Create("DOLLARFR", 2, 2, [], (e, a) => Dollar(e, a, false)));
        RegisterDepreciation(r);
        RegisterSecurities(r);
    }

    // ECMA-376 Part 1 §18.17.7 (FV, PV, PMT, NPER): the annuity equation  pv(1+r)^n + pmt(1+r*type)((1+r)^n-1)/r + fv = 0.
    private static double Growth(double r, double n) => Math.Exp(n * Math.Log(1 + r));

    private static double Fv(double r, double n, double pmt, double pv, double type)
    {
        if (r == 0)
        {
            return -(pv + (pmt * n));
        }

        double g = Growth(r, n);
        return -((pv * g) + (pmt * (1 + (r * type)) * (g - 1) / r));
    }

    private static double Pv(double r, double n, double pmt, double fv, double type)
    {
        if (r == 0)
        {
            return -(fv + (pmt * n));
        }

        double g = Growth(r, n);
        return -(fv + (pmt * (1 + (r * type)) * (g - 1) / r)) / g;
    }

    private static double Pmt(double r, double n, double pv, double fv, double type)
    {
        if (r == 0)
        {
            return -(pv + fv) / n;
        }

        double g = Growth(r, n);
        return -(fv + (pv * g)) * r / ((1 + (r * type)) * (g - 1));
    }

    private static CalcValue FvFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0), n = x.Num(1), pmt = x.Num(2), pv = x.Num(3), t = x.Num(4);
        return x.Failed ? x.Error : Num(Fv(r, n, pmt, pv, t != 0 ? 1 : 0));
    }

    private static CalcValue PvFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0), n = x.Num(1), pmt = x.Num(2), fv = x.Num(3), t = x.Num(4);
        return x.Failed ? x.Error : Num(Pv(r, n, pmt, fv, t != 0 ? 1 : 0));
    }

    private static CalcValue PmtFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0), n = x.Num(1), pv = x.Num(2), fv = x.Num(3), t = x.Num(4);
        if (x.Failed)
        {
            return x.Error;
        }

        return n == 0 ? NumError : Num(Pmt(r, n, pv, fv, t != 0 ? 1 : 0));
    }

    private static CalcValue NperFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0), pmt = x.Num(1), pv = x.Num(2), fv = x.Num(3), t = x.Num(4);
        if (x.Failed)
        {
            return x.Error;
        }

        double type = t != 0 ? 1 : 0;
        if (r == 0)
        {
            return pmt == 0 ? NumError : Num(-(pv + fv) / pmt);
        }

        double p = pmt * (1 + (r * type));
        double top = p - (fv * r), bottom = p + (pv * r);
        if (bottom == 0 || top / bottom <= 0 || 1 + r <= 0)
        {
            return NumError;
        }

        return Num(Math.Log(top / bottom) / Math.Log(1 + r));
    }

    // RATE: Excel documents Newton iteration from the guess (default 0.1), 20 steps, to within 0.0000001, otherwise #NUM!. Plain Newton from
    // 0.1 does not converge for long annuities that Excel solves, so the iteration is damped and runs up to 100 steps.
    private static CalcValue RateFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double n = x.Num(0), pmt = x.Num(1), pv = x.Num(2), fv = x.Num(3), t = x.Num(4), guess = x.Num(5, 0.1);
        if (x.Failed)
        {
            return x.Error;
        }

        double type = t != 0 ? 1 : 0;
        if (n <= 0 || guess <= -1)
        {
            return NumError;
        }

        double F(double r)
        {
            if (r == 0)
            {
                return pv + (pmt * n) + fv;
            }

            double gm1 = Math.Exp(n * Math.Log(1 + r)) - 1;
            return (pv * (gm1 + 1)) + (pmt * (1 + (r * type)) * gm1 / r) + fv;
        }

        double Df(double r)
        {
            double h = 1e-7 * Math.Max(1, Math.Abs(r));
            return (F(r + h) - F(r - h)) / (2 * h);
        }

        return FinSupport.Newton(F, Df, guess, 100, out double rate) ? Num(rate) : NumError;
    }

    // Interest in period `per` is the rate times the balance carried into it; for payments at the start of the period the first period
    // carries no interest and each later one is the end-of-period figure one period earlier, discounted by one period.
    private static double Ipmt(double r, double per, double n, double pv, double fv, double type)
    {
        double pmt = Pmt(r, n, pv, fv, type);
        if (type != 0)
        {
            if (per <= 1)
            {
                return 0;
            }

            return Fv(r, per - 2, pmt, pv, 0) * r / (1 + r);
        }

        return Fv(r, per - 1, pmt, pv, 0) * r;
    }

    private static CalcValue InterestOrPrincipal(Evaluator ev, ReadOnlySpan<CalcValue> a, bool interest)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0), per = x.Num(1), n = x.Num(2), pv = x.Num(3), fv = x.Num(4), t = x.Num(5);
        if (x.Failed)
        {
            return x.Error;
        }

        double type = t != 0 ? 1 : 0;
        if (per < 1 || per > n || n <= 0)
        {
            return NumError;
        }

        double ip = Ipmt(r, per, n, pv, fv, type);
        return Num(interest ? ip : Pmt(r, n, pv, fv, type) - ip);
    }

    private static CalcValue IspmtFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0), per = x.Num(1), n = x.Num(2), pv = x.Num(3);
        if (x.Failed)
        {
            return x.Error;
        }

        return n == 0 ? DivError : Num(pv * r * ((per / n) - 1));
    }

    private static CalcValue Cumulative(Evaluator ev, ReadOnlySpan<CalcValue> a, bool interest)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0), n = x.Int(1), pv = x.Num(2), start = x.Int(3), end = x.Int(4), type = x.Num(5);
        if (x.Failed)
        {
            return x.Error;
        }

        if (r <= 0 || n <= 0 || pv <= 0 || start < 1 || end < start || end > n || type is not (0 or 1))
        {
            return NumError;
        }

        double pmt = Pmt(r, n, pv, 0, type);
        // Beyond 2^53 a double no longer advances by one, so only spans that count exactly are walked period by period.
        if (end - start < 100_000 && end < 9_007_199_254_740_992d)
        {
            double sum = 0;
            for (double p = start; p <= end; p++)
            {
                double ip = Ipmt(r, p, n, pv, 0, type);
                sum += interest ? ip : pmt - ip;
            }

            return Num(sum);
        }

        // A very long span is summed in closed form: the interest of periods a..b paid at the end is the payments less the balance reduction.
        double pmt0 = Pmt(r, n, pv, 0, 0);
        double CumulativeInterest0(double a, double b) => (pmt0 * (b - a + 1)) - (Fv(r, a - 1, pmt0, pv, 0) - Fv(r, b, pmt0, pv, 0));
        double total = type == 0
            ? CumulativeInterest0(start, end)
            : end == 1 ? 0 : CumulativeInterest0(Math.Max(start - 1, 1), end - 1) / (1 + r);
        return Num(interest ? total : (pmt * (end - start + 1)) - total);
    }

    private static CalcValue EffectFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double nominal = x.Num(0), k = x.Int(1);
        if (x.Failed)
        {
            return x.Error;
        }

        if (nominal <= 0 || k < 1)
        {
            return NumError;
        }

        return Num(Math.Exp(k * Math.Log(1 + (nominal / k))) - 1);
    }

    private static CalcValue NominalFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double eff = x.Num(0), k = x.Int(1);
        if (x.Failed)
        {
            return x.Error;
        }

        if (eff <= 0 || k < 1)
        {
            return NumError;
        }

        return Num(k * (Math.Exp(Math.Log(1 + eff) / k) - 1));
    }

    private static CalcValue RriFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double n = x.Num(0), pv = x.Num(1), fv = x.Num(2);
        if (x.Failed)
        {
            return x.Error;
        }

        if (n <= 0 || pv == 0)
        {
            return NumError;
        }

        double ratio = fv / pv;
        return ratio < 0 ? NumError : Num(Math.Pow(ratio, 1 / n) - 1);
    }

    private static CalcValue PdurationFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0), pv = x.Num(1), fv = x.Num(2);
        if (x.Failed)
        {
            return x.Error;
        }

        if (r <= 0 || pv <= 0 || fv <= 0)
        {
            return NumError;
        }

        return Num((Math.Log(fv) - Math.Log(pv)) / Math.Log(1 + r));
    }

    private static CalcValue FvScheduleFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double principal = x.Num(0);
        if (x.Failed)
        {
            return x.Error;
        }

        var rates = new List<double>();
        if (!FinSupport.TryNumbers(ev, a[1], true, rates, out CalcValue err))
        {
            return err;
        }

        foreach (double r in rates)
        {
            principal *= 1 + r;
        }

        return Num(principal);
    }

    private static CalcValue NpvFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0);
        if (x.Failed)
        {
            return x.Error;
        }

        var sink = new ListSink();
        if (Fx.Collect(ev, a[1..], ref sink) is { } e)
        {
            return e;
        }

        if (r == -1)
        {
            return DivError;
        }

        double sum = 0;
        double factor = 1;
        if (sink.Values is not null)
        {
            foreach (double v in sink.Values)
            {
                factor *= 1 + r;
                sum += v / factor;
            }
        }

        return Num(sum);
    }

    private static CalcValue XnpvFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double r = x.Num(0);
        if (x.Failed)
        {
            return x.Error;
        }

        var values = new List<double>();
        var dates = new List<double>();
        if (!FinSupport.TryNumbers(ev, a[1], true, values, out CalcValue e1))
        {
            return e1;
        }

        if (!FinSupport.TryNumbers(ev, a[2], true, dates, out CalcValue e2))
        {
            return e2;
        }

        if (values.Count != dates.Count || values.Count == 0)
        {
            return NumError;
        }

        if (r <= -1)
        {
            return NumError;
        }

        double d0 = Math.Floor(dates[0]);
        double sum = 0;
        for (int i = 0; i < values.Count; i++)
        {
            double d = Math.Floor(dates[i]);
            if (d < d0 || d < 0)
            {
                return NumError;
            }

            sum += values[i] / Math.Pow(1 + r, (d - d0) / 365);
        }

        return Num(sum);
    }

    private static bool HasBothSigns(List<double> v)
    {
        bool pos = false, neg = false;
        foreach (double d in v)
        {
            pos |= d > 0;
            neg |= d < 0;
        }

        return pos && neg;
    }

    // IRR: as RATE, a damped Newton iteration from the guess (default 0.1); #NUM! when it does not converge.
    private static CalcValue IrrFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double guess = x.Num(1, 0.1);
        if (x.Failed)
        {
            return x.Error;
        }

        var v = new List<double>();
        if (!FinSupport.TryNumbers(ev, a[0], false, v, out CalcValue err))
        {
            return err;
        }

        if (!HasBothSigns(v) || guess <= -1)
        {
            return NumError;
        }

        double F(double r)
        {
            double f = 0, w = 1, q = 1 / (1 + r);
            for (int k = 0; k < v.Count; k++)
            {
                f += v[k] * w;
                w *= q;
            }

            return f;
        }

        double Df(double r)
        {
            double df = 0, w = 1, q = 1 / (1 + r);
            for (int k = 0; k < v.Count; k++)
            {
                df -= k * v[k] * w * q;
                w *= q;
            }

            return df;
        }

        return FinSupport.Newton(F, Df, guess, 100, out double irr) ? Num(irr) : NumError;
    }

    // XIRR: damped Newton iteration on the 365-day-year equation (Excel documents 100 tries to 0.000001 percent).
    private static CalcValue XirrFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double guess = x.Num(2, 0.1);
        if (x.Failed)
        {
            return x.Error;
        }

        var v = new List<double>();
        var d = new List<double>();
        if (!FinSupport.TryNumbers(ev, a[0], true, v, out CalcValue e1))
        {
            return e1;
        }

        if (!FinSupport.TryNumbers(ev, a[1], true, d, out CalcValue e2))
        {
            return e2;
        }

        if (v.Count != d.Count || v.Count < 2 || !HasBothSigns(v) || guess <= -1)
        {
            return NumError;
        }

        double d0 = Math.Floor(d[0]);
        var t = new double[d.Count];
        for (int i = 0; i < t.Length; i++)
        {
            double di = Math.Floor(d[i]);
            if (di < d0 || di < 0)
            {
                return NumError;
            }

            t[i] = (di - d0) / 365;
        }

        double F(double r)
        {
            double f = 0;
            for (int k = 0; k < v.Count; k++)
            {
                f += v[k] * Math.Pow(1 + r, -t[k]);
            }

            return f;
        }

        double Df(double r)
        {
            double df = 0;
            for (int k = 0; k < v.Count; k++)
            {
                df -= t[k] * v[k] * Math.Pow(1 + r, -t[k] - 1);
            }

            return df;
        }

        return FinSupport.Newton(F, Df, guess, 100, out double xirr) ? Num(xirr) : NumError;
    }

    private static CalcValue MirrFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double fin = x.Num(1), reinv = x.Num(2);
        if (x.Failed)
        {
            return x.Error;
        }

        var v = new List<double>();
        if (!FinSupport.TryNumbers(ev, a[0], false, v, out CalcValue err))
        {
            return err;
        }

        int n = v.Count;
        if (!HasBothSigns(v) || n < 2)
        {
            return DivError;
        }

        double pos = 0, neg = 0;
        for (int k = 0; k < n; k++)
        {
            if (v[k] > 0)
            {
                pos += v[k] * Math.Pow(1 + reinv, n - 1 - k);
            }
            else if (v[k] < 0)
            {
                neg += v[k] / Math.Pow(1 + fin, k);
            }
        }

        return Num(Math.Pow(pos / -neg, 1.0 / (n - 1)) - 1);
    }

    // DOLLARDE / DOLLARFR: the digits after the point are a count of `fraction`ths; their width is the number of digits of `fraction`.
    private static CalcValue Dollar(Evaluator ev, ReadOnlySpan<CalcValue> a, bool toDecimal)
    {
        var x = new FinArgs(ev, a);
        double d = x.Num(0), fraction = x.Int(1);
        if (x.Failed)
        {
            return x.Error;
        }

        if (fraction < 0)
        {
            return NumError;
        }

        if (fraction == 0)
        {
            return DivError;
        }

        double scale = Math.Pow(10, Math.Ceiling(Math.Log10(fraction)));
        double whole = Math.Truncate(d);
        double frac = d - whole;
        double result = toDecimal ? whole + (Fx.Round15(frac * scale) / fraction) : whole + (Fx.Round15(frac * fraction) / scale);
        return Num(Fx.Round15(result));
    }
}
