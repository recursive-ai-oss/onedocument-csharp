using System;

namespace RecursiveAI.OneDocument.Calc;

internal static partial class FinancialFunctions
{
    private static void RegisterDepreciation(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("SLN", 3, 3, [], SlnFunction));
        r.Add(FunctionDefinition.Create("SYD", 4, 4, [], SydFunction));
        r.Add(FunctionDefinition.Create("DB", 4, 5, [], DbFunction));
        r.Add(FunctionDefinition.Create("DDB", 4, 5, [], DdbFunction));
        r.Add(FunctionDefinition.Create("VDB", 5, 7, [], VdbFunction));
        r.Add(FunctionDefinition.Create("AMORLINC", 6, 7, [], AmorLinc));
        r.Add(FunctionDefinition.Create("AMORDEGRC", 6, 7, [], AmorDegrc));
    }

    private static CalcValue SlnFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double cost = x.Num(0), salvage = x.Num(1), life = x.Num(2);
        if (x.Failed)
        {
            return x.Error;
        }

        return life == 0 ? DivError : Num((cost - salvage) / life);
    }

    private static CalcValue SydFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double cost = x.Num(0), salvage = x.Num(1), life = x.Num(2), per = x.Num(3);
        if (x.Failed)
        {
            return x.Error;
        }

        if (life <= 0 || per <= 0 || per > life)
        {
            return NumError;
        }

        return Num((cost - salvage) * (life - per + 1) * 2 / (life * (life + 1)));
    }

    // Fixed-declining balance: the rate is rounded to three decimals; the first and the (life+1)th periods are pro-rated by `month`.
    private static CalcValue DbFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double cost = x.Num(0), salvage = x.Num(1), life = x.Num(2), period = Math.Truncate(x.Num(3)), month = x.Int(4, 12);
        if (x.Failed)
        {
            return x.Error;
        }

        if (cost < 0 || salvage < 0 || life <= 0 || period <= 0 || month < 1 || month > 12)
        {
            return NumError;
        }

        if (period > life + (month < 12 ? 1 : 0))
        {
            return NumError;
        }

        if (cost == 0)
        {
            return Num(0);
        }

        double rate = Math.Round(1 - Math.Pow(salvage / cost, 1 / life), 3);
        double first = cost * rate * month / 12;
        if (period == 1)
        {
            return Num(first);
        }

        // After the first period the book value shrinks by (1 - rate) each full period, so no loop over the periods is needed.
        double book = cost - first;
        double whole = Math.Truncate(life);
        if (period <= whole)
        {
            return Num(book * Math.Pow(1 - rate, period - 2) * rate);
        }

        return Num(book * Math.Pow(1 - rate, whole - 1) * rate * (12 - month) / 12);
    }

    // Total depreciation after k whole periods of declining balance at factor/life, never taking the book value below salvage.
    private static double DdbTotal(double cost, double salvage, double life, double factor, double k)
    {
        if (k <= 0)
        {
            return 0;
        }

        double cap = Math.Max(cost - salvage, 0);
        double q = factor / life;
        if (q >= 1)
        {
            return cap;
        }

        return Math.Min(cost * (1 - Math.Pow(1 - q, k)), cap);
    }

    private static double DdbTotalAt(double cost, double salvage, double life, double factor, double t)
    {
        double lo = Math.Floor(t);
        double a = DdbTotal(cost, salvage, life, factor, lo);
        if (t == lo)
        {
            return a;
        }

        double b = DdbTotal(cost, salvage, life, factor, lo + 1);
        return a + ((t - lo) * (b - a));
    }

    private static CalcValue DdbFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double cost = x.Num(0), salvage = x.Num(1), life = x.Num(2), period = x.Num(3), factor = x.Num(4, 2);
        if (x.Failed)
        {
            return x.Error;
        }

        if (cost < 0 || salvage < 0 || life <= 0 || period <= 0 || period > life || factor <= 0)
        {
            return NumError;
        }

        return Num(DdbTotalAt(cost, salvage, life, factor, period) - DdbTotalAt(cost, salvage, life, factor, period - 1));
    }

    // Variable declining balance: each period takes the larger of the declining-balance charge and the straight-line charge over the
    // remaining life (unless no_switch), never below salvage; partial periods at either end are weighted by their overlap.
    private static CalcValue VdbFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        double cost = x.Num(0), salvage = x.Num(1), life = x.Num(2), start = x.Num(3), end = x.Num(4), factor = x.Num(5, 2);
        bool noSwitch = x.Bool(6, false);
        if (x.Failed)
        {
            return x.Error;
        }

        if (cost < 0 || salvage < 0 || life <= 0 || start < 0 || end < start || end > life || factor < 0)
        {
            return NumError;
        }

        double n = Math.Ceiling(end);
        if (n > 5_000_000)
        {
            return NumError;
        }

        double book = cost, result = 0;
        for (double i = 1; i <= n; i++)
        {
            double ddb = book * factor / life;
            double dep = ddb;
            double remaining = life - (i - 1);
            if (!noSwitch && remaining > 0)
            {
                double sl = (book - salvage) / remaining;
                if (sl > ddb)
                {
                    dep = sl;
                }
            }

            dep = Math.Min(dep, Math.Max(book - salvage, 0));
            double w = Math.Max(0, Math.Min(i, end) - Math.Max(i - 1, start));
            result += dep * w;
            book -= dep;
        }

        return Num(result);
    }

    private static bool AmortArgs(ref FinArgs x, out double cost, out double salvage, out double period, out double rate, out int basis, out double firstFraction)
    {
        cost = x.Num(0);
        int purchased = x.Date(1), first = x.Date(2);
        salvage = x.Num(3);
        period = Math.Truncate(x.Num(4));
        rate = x.Num(5);
        basis = x.Basis(6);
        firstFraction = 0;
        if (x.Failed)
        {
            return false;
        }

        if (basis == 2 || period > 1_000_000 || cost <= 0 || salvage < 0 || salvage > cost || period < 0 || rate <= 0 || purchased > first)
        {
            x.FailNum();
            return false;
        }

        firstFraction = DayCount.YearFrac(purchased, first, basis, x.DateSystem);
        if (double.IsNaN(firstFraction))
        {
            x.FailNum();
            return false;
        }

        return true;
    }

    // Linear depreciation of the French system: a pro-rated first period, then full years until the base is used up, then the remainder.
    private static CalcValue AmorLinc(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        if (!AmortArgs(ref x, out double cost, out double salvage, out double period, out double rate, out _, out double fraction))
        {
            return x.Error;
        }

        double full = cost * rate;
        double first = fraction * full;
        if (period == 0)
        {
            return Num(first);
        }

        double usable = cost - salvage - first;
        if (usable <= 0)
        {
            return Num(0);
        }

        double whole = Math.Floor(usable / full);
        if (period <= whole)
        {
            return Num(full);
        }

        return Num(period == whole + 1 ? usable - (whole * full) : 0);
    }

    // Declining depreciation of the French system: the rate is multiplied by a coefficient that depends on the asset's life, and every
    // charge is rounded to a whole currency unit.
    private static CalcValue AmorDegrc(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var x = new FinArgs(ev, a);
        if (!AmortArgs(ref x, out double cost, out double salvage, out double period, out double rate, out _, out double fraction))
        {
            return x.Error;
        }

        double life = 1 / rate;
        if (life < 1)
        {
            return NumError;
        }

        double coefficient = life < 3 ? 1 : life < 5 ? 1.5 : life <= 6 ? 2 : 2.5;
        rate *= coefficient;
        double first = Math.Round(fraction * rate * cost, MidpointRounding.AwayFromZero);
        if (period == 0)
        {
            return Num(first);
        }

        cost -= first;
        double rest = cost - salvage;
        double charge = 0;
        for (double n = 0; n < period; n++)
        {
            charge = Math.Round(rate * cost, MidpointRounding.AwayFromZero);
            rest -= charge;
            if (rest < 0)
            {
                return Num(period - n <= 1 ? Math.Round(cost * 0.5, MidpointRounding.AwayFromZero) : 0);
            }

            cost -= charge;
        }

        return Num(charge);
    }
}
