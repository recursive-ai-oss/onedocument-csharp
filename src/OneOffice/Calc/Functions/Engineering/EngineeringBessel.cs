using System;

namespace RecursiveAI.OneOffice.Calc;

internal static partial class EngineeringFunctions
{
    private const double MaxBesselX = 1e6;
    private const int MaxBesselOrder = 100_000;

    private static void RegisterBessel(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("BESSELI", 2, 2, [], (_, a) => Bessel(a, 0)));
        r.Add(FunctionDefinition.Create("BESSELJ", 2, 2, [], (_, a) => Bessel(a, 1)));
        r.Add(FunctionDefinition.Create("BESSELK", 2, 2, [], (_, a) => Bessel(a, 2)));
        r.Add(FunctionDefinition.Create("BESSELY", 2, 2, [], (_, a) => Bessel(a, 3)));
    }

    private static CalcValue Bessel(ReadOnlySpan<CalcValue> a, int kind)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e1))
        {
            return e1;
        }

        if (!Fx.TryNum(a[1], out double nn, out CalcValue e2))
        {
            return e2;
        }

        nn = Math.Truncate(nn);
        if (nn < 0 || nn > MaxBesselOrder || Math.Abs(x) > MaxBesselX)
        {
            return NumError;
        }

        int n = (int)nn;
        if (kind >= 2 && x <= 0)
        {
            return NumError;
        }

        double v = kind switch
        {
            0 => BesselI(x, n),
            1 => BesselJ(x, n),
            2 => BesselK(x, n),
            _ => BesselY(x, n),
        };
        return Num(v);
    }

    // Leading term (x/2)^n / n!, built factor by factor so that a large order underflows to 0 instead of overflowing.
    private static double LeadingTerm(double h, int n)
    {
        double t = 1;
        for (int i = 1; i <= n; i++)
        {
            t *= h / i;
            if (t == 0)
            {
                break;
            }
        }

        return t;
    }

    // I_n(x) = Σ (x/2)^(2k+n) / (k! (k+n)!)  (all terms positive).
    private static double BesselI(double x, int n)
    {
        double h = Math.Abs(x) / 2;
        double t = LeadingTerm(h, n), sum = t, hh = h * h;
        for (int k = 0; k < 20000 && t > 0; k++)
        {
            t *= hh / ((k + 1.0) * (k + n + 1.0));
            sum += t;
            if (t <= sum * 1e-17)
            {
                break;
            }
        }

        return x < 0 && (n & 1) == 1 ? -sum : sum;
    }

    // J_n(x): the power series where its terms do not cancel; otherwise the trapezoid rule on (1/2π)∫cos(nτ − x sin τ)dτ, which is
    // exact to rounding for a periodic analytic integrand once there are more points than |x| + n.
    private static double BesselJ(double x, int n)
    {
        if (x == 0)
        {
            return n == 0 ? 1 : 0;
        }

        if (Math.Abs(x) <= 60)
        {
            double h = Math.Abs(x) / 2, hh = h * h;
            double t = LeadingTerm(h, n), sum = t, max = Math.Abs(t);
            for (int k = 0; k < 5000; k++)
            {
                t *= -hh / ((k + 1.0) * (k + n + 1.0));
                sum += t;
                max = Math.Max(max, Math.Abs(t));
                if (Math.Abs(t) <= Math.Abs(sum) * 1e-18 || t == 0)
                {
                    break;
                }
            }

            if (sum != 0 && max <= Math.Abs(sum) * 10)
            {
                return x < 0 && (n & 1) == 1 ? -sum : sum;
            }
        }

        int points = (int)(Math.Abs(x) + n) + 80;
        double acc = 0;
        double step = 2 * Math.PI / points;
        for (int j = 0; j < points; j++)
        {
            double tau = step * j;
            acc += Math.Cos((n * tau) - (x * Math.Sin(tau)));
        }

        return acc / points;
    }

    // K_n(x) = ∫₀^∞ e^(−x cosh t) cosh(n t) dt by the trapezoid rule; the integrand is analytic and decays double-exponentially.
    private static double BesselK(double x, int n)
    {
        double h = 0.25 / Math.Sqrt(1 + (x / 4));
        double Term(double t)
        {
            double e = -x * Math.Cosh(t);
            return 0.5 * (Math.Exp(e + (n * t)) + Math.Exp(e - (n * t)));
        }

        double sum = 0.5 * Term(0);
        for (int k = 1; k < 2_000_000; k++)
        {
            double t = k * h;
            double v = Term(t);
            sum += v;
            if (v <= sum * 1e-18 && x * Math.Sinh(t) > n)
            {
                break;
            }

            if (double.IsInfinity(sum))
            {
                break;
            }
        }

        return sum * h;
    }

    // 20-point Gauss–Legendre nodes and weights, found by Newton iteration on the Legendre polynomial.
    private static readonly (double[] Nodes, double[] Weights) GaussLegendre = ComputeGaussLegendre();

    private static (double[], double[]) ComputeGaussLegendre()
    {
        const int m = 20;
        var nodes = new double[m];
        var weights = new double[m];
        for (int i = 0; i < m; i++)
        {
            double z = Math.Cos(Math.PI * (i + 0.75) / (m + 0.5)), pp = 1;
            for (int it = 0; it < 100; it++)
            {
                double p1 = 1, p2 = 0;
                for (int j = 1; j <= m; j++)
                {
                    double p3 = p2;
                    p2 = p1;
                    p1 = (((2.0 * j) - 1) * z * p2 - ((j - 1.0) * p3)) / j;
                }

                pp = m * ((z * p1) - p2) / ((z * z) - 1);
                double z1 = z;
                z = z1 - (p1 / pp);
                if (Math.Abs(z - z1) < 1e-16)
                {
                    break;
                }
            }

            nodes[i] = z;
            weights[i] = 2 / ((1 - (z * z)) * pp * pp);
        }

        return (nodes, weights);
    }

    private static double Integrate(Func<double, double> f, double a, double b, int panels)
    {
        (double[] nodes, double[] weights) = GaussLegendre;
        double w = (b - a) / panels, total = 0;
        for (int p = 0; p < panels; p++)
        {
            double mid = a + ((p + 0.5) * w), half = w / 2, s = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                s += weights[i] * f(mid + (half * nodes[i]));
            }

            total += s * half;
        }

        return total;
    }

    // Y_n(x) = (1/π)∫₀^π sin(x sin θ − nθ)dθ − (1/π)∫₀^∞ (e^(nt) + (−1)^n e^(−nt)) e^(−x sinh t) dt, both by composite Gauss–Legendre.
    private static double BesselY(double x, int n)
    {
        double sign = (n & 1) == 0 ? 1 : -1;
        int panels = (int)((x + n) / 2) + 2;
        double first = Integrate(th => Math.Sin((x * Math.Sin(th)) - (n * th)), 0, Math.PI, panels);

        double upper = 0.25;
        while ((x * Math.Sinh(upper)) - (n * upper) < 50 || x * Math.Cosh(upper) < n)
        {
            upper += 0.25;
            if (upper > 800)
            {
                break;
            }
        }

        double width = Math.Min(0.5, 2.0 / (n + x + 1));
        int tailPanels = (int)Math.Ceiling(upper / width);
        double second = Integrate(t => Math.Exp((n * t) - (x * Math.Sinh(t))) + (sign * Math.Exp((-n * t) - (x * Math.Sinh(t)))), 0, upper, tailPanels);
        return (first - second) / Math.PI;
    }
}
