using System;
using System.Collections.Generic;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>
/// Reads the numeric arguments of a financial function left to right. The first failure (a text that is not a number, an invalid date, a basis
/// outside 0 to 4) is kept and every later read returns a neutral value, so a function reads all its arguments and then checks <see cref="Failed"/> once.
/// </summary>
internal ref struct FinArgs
{
    private readonly ReadOnlySpan<CalcValue> _a;
    private CalcValue _error;
    private bool _failed;

    public FinArgs(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        Ev = ev;
        _a = args;
        _error = default;
        _failed = false;
    }

    public Evaluator Ev { get; }

    public DateSystem DateSystem => Ev.Options.DateSystem;

    public bool Failed => _failed;

    public CalcValue Error => _error;

    public bool Has(int i) => !Fx.Omitted(_a, i);

    private void Fail(CalcError e)
    {
        if (!_failed)
        {
            _failed = true;
            _error = Fx.Err(e);
        }
    }

    public void FailNum() => Fail(CalcError.Num);

    /// <summary>A number; an omitted argument is <paramref name="def"/>.</summary>
    public double Num(int i, double def = 0)
    {
        if (!Has(i))
        {
            return def;
        }

        CalcError e = CalcConvert.ToNumber(_a[i], out double d);
        if (e != CalcError.None)
        {
            Fail(e);
            return 0;
        }

        return d;
    }

    /// <summary>A whole number by truncation toward zero.</summary>
    public double Int(int i, double def = 0) => Math.Truncate(Num(i, def));

    /// <summary>A date serial truncated to a whole day; outside the calendar is <c>#NUM!</c>.</summary>
    public int Date(int i)
    {
        double d = Math.Floor(Num(i));
        if (_failed)
        {
            return 0;
        }

        if (d < 0 || d > ExcelDate.MaxSerial(DateSystem))
        {
            Fail(CalcError.Num);
            return 0;
        }

        return (int)d;
    }

    /// <summary>A day-count basis 0 to 4; omitted is 0.</summary>
    public int Basis(int i)
    {
        double b = Int(i);
        if (b is < 0 or > 4)
        {
            Fail(CalcError.Num);
            return 0;
        }

        return (int)b;
    }

    /// <summary>A coupon frequency: 1, 2 or 4.</summary>
    public int Frequency(int i)
    {
        double f = Int(i);
        if (f is not (1 or 2 or 4))
        {
            Fail(CalcError.Num);
            return 1;
        }

        return (int)f;
    }

    /// <summary>A logical; omitted is <paramref name="def"/>.</summary>
    public bool Bool(int i, bool def)
    {
        if (!Has(i))
        {
            return def;
        }

        CalcError e = CalcConvert.ToBoolean(_a[i], out bool b);
        if (e != CalcError.None)
        {
            Fail(e);
            return def;
        }

        return b;
    }
}

internal static class FinSupport
{
    /// <summary>
    /// Reads the numbers of an argument in reading order. With <paramref name="strict"/> every cell must be a number (a blank is 0; text or a
    /// boolean is <c>#VALUE!</c>); otherwise text, booleans and blanks inside references and arrays are skipped, as <c>IRR</c> and <c>NPV</c> do.
    /// </summary>
    public static bool TryNumbers(Evaluator ev, in CalcValue arg, bool strict, List<double> into, out CalcValue error)
    {
        error = default;
        var cells = new List<CalcValue>();
        CalcError fe = Fx.Flatten(ev, arg, cells);
        if (fe != CalcError.None)
        {
            error = Fx.Err(fe);
            return false;
        }

        foreach (CalcValue c in cells)
        {
            switch (c.Kind)
            {
                case ValueKind.Error:
                    error = c;
                    return false;
                case ValueKind.Number:
                    into.Add(c.Number);
                    break;
                case ValueKind.Blank:
                    if (strict)
                    {
                        into.Add(0);
                    }

                    break;
                default:
                    if (strict)
                    {
                        error = Fx.Err(CalcError.Value);
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Newton's method with step halving: a step is shortened until it keeps the rate above -1 and does not increase |f|, so long annuities
    /// (hundreds of periods) converge from the default guess where plain Newton overshoots. Stops when a step is below 1e-12.
    /// </summary>
    public static bool Newton(Func<double, double> f, Func<double, double> df, double guess, int maxIterations, out double root)
    {
        root = 0;
        double r = guess, fr = f(r);
        for (int it = 0; it < maxIterations; it++)
        {
            double d = df(r);
            if (!double.IsFinite(fr) || !double.IsFinite(d) || d == 0)
            {
                return false;
            }

            if (fr == 0)
            {
                root = r;
                return true;
            }

            double step = fr / d, t = 1, rn = r, frn = fr;
            for (int h = 0; h < 40; h++)
            {
                rn = r - (t * step);
                if (rn > -1)
                {
                    frn = f(rn);
                    if (double.IsFinite(frn) && (Math.Abs(frn) < Math.Abs(fr) || h == 39))
                    {
                        break;
                    }
                }

                t /= 2;
            }

            if (!(rn > -1) || !double.IsFinite(frn))
            {
                return false;
            }

            bool done = Math.Abs(rn - r) < 1e-12 * Math.Max(1, Math.Abs(rn));
            r = rn;
            fr = frn;
            if (done)
            {
                root = r;
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the root of a function that decreases with its argument by bisection on a bracket that grows until it straddles the target.</summary>
    public static bool SolveDecreasing(Func<double, double> f, double target, double lo, out double root)
    {
        root = 0;
        double flo = f(lo);
        if (!double.IsFinite(flo) || flo < target)
        {
            return false;
        }

        double hi = Math.Max(lo + 1, 1);
        int guard = 0;
        while (true)
        {
            double fh = f(hi);
            if (double.IsFinite(fh) && fh <= target)
            {
                break;
            }

            hi = (hi * 2) + 1;
            if (++guard > 60 || hi > 1e12)
            {
                return false;
            }
        }

        for (int i = 0; i < 200; i++)
        {
            double mid = (lo + hi) / 2;
            double fm = f(mid);
            if (!double.IsFinite(fm))
            {
                return false;
            }

            if (fm > target)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }

            if (hi - lo <= 1e-15 * Math.Max(1, Math.Abs(hi)))
            {
                break;
            }
        }

        root = (lo + hi) / 2;
        return true;
    }
}
