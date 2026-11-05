using System;
using RecursiveAI.OneMath;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>
/// The special functions and distribution kernels behind the statistical family: log-gamma, the error function, the regularized incomplete gamma and
/// beta functions, the normal quantile and a monotone-inverse solver. Exponentials and logarithms go through OneMath so results are the same on every platform.
/// </summary>
internal static class SpecialMath
{
    public const double Sqrt2 = 1.4142135623730951;
    public const double SqrtPi = 1.7724538509055159;
    public const double SqrtTwoPi = 2.5066282746310002;
    public const double LnSqrtTwoPi = 0.91893853320467274;

    private const double Eps = 1e-16;
    private const int MaxIterations = 20000;

    public static double Ln(double x) => Math64.Log(x);

    public static double Exp(double x) => Math64.Exp(x);

    public static double Pow(double x, double y) => Math64.Pow(x, y);

    public static double Ln1p(double x) => Math64.Log1p(x);

    // ---- gamma -------------------------------------------------------------------------------------------------------------------

    // Lanczos approximation with g = 7 and nine coefficients; good to about 1e-15 for x >= 0.5.
    private static readonly double[] Lanczos =
    [
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
    ];

    /// <summary>The natural log of the gamma function for x &gt; 0.</summary>
    public static double LogGamma(double x)
    {
        if (x == 1 || x == 2)
        {
            return 0;
        }

        if (x >= 15)
        {
            // Stirling series: ln G(x) = (x - 1/2) ln x - x + ln sqrt(2 pi) + 1/(12x) - 1/(360x^3) + 1/(1260x^5) - 1/(1680x^7)
            double x2 = x * x;
            double series = (1.0 / (12 * x)) - (1.0 / (360 * x * x2)) + (1.0 / (1260 * x * x2 * x2)) - (1.0 / (1680 * x * x2 * x2 * x2));
            return ((x - 0.5) * Ln(x)) - x + LnSqrtTwoPi + series;
        }

        if (x < 0.5)
        {
            return LogGamma(x + 1) - Ln(x);
        }

        double z = x - 1;
        double a = Lanczos[0];
        double t = z + 7.5;
        for (int i = 1; i < Lanczos.Length; i++)
        {
            a += Lanczos[i] / (z + i);
        }

        return LnSqrtTwoPi + ((z + 0.5) * Ln(t)) - t + Ln(a);
    }

    /// <summary>The gamma function for x &gt; 0 (exact for whole numbers up to 171).</summary>
    public static double Gamma(double x)
    {
        if (x == Math.Floor(x) && x <= 171)
        {
            double f = 1;
            for (int i = 2; i < (int)x; i++)
            {
                f *= i;
            }

            return f;
        }

        if (x > 171.7)
        {
            return double.PositiveInfinity;
        }

        if (x < 0)
        {
            // Reflection: G(x) = pi / (sin(pi x) G(1 - x))
            return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
        }

        return Exp(LogGamma(x));
    }

    /// <summary>ln of the binomial coefficient for real arguments (n &gt;= k &gt;= 0).</summary>
    public static double LogChoose(double n, double k) => LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);

    /// <summary>The binomial coefficient; exact products while they stay below 2^53, logs after.</summary>
    public static double Choose(double n, double k)
    {
        if (k < 0 || k > n)
        {
            return 0;
        }

        k = Math.Min(k, n - k);
        if (k <= 60)
        {
            double r = 1;
            for (int i = 1; i <= (int)k; i++)
            {
                r = r * (n - k + i) / i;
                if (!double.IsFinite(r))
                {
                    break;
                }
            }

            if (double.IsFinite(r))
            {
                return r < 9e15 ? Math.Round(r) : r;
            }
        }

        return Exp(LogChoose(n, k));
    }

    // ---- error function ----------------------------------------------------------------------------------------------------------

    private static double ExpNegSquare(double x)
    {
        // e^(-x^2) with the square split in two exact parts, so large x does not lose digits in the exponent.
        double xh = Math.Round(x * 16) / 16;
        return Exp(-xh * xh) * Exp(-(x - xh) * (x + xh));
    }

    public static double Erf(double x)
    {
        double ax = Math.Abs(x);
        if (ax >= 2.5)
        {
            double e = 1 - Erfc(ax);
            return x < 0 ? -e : e;
        }

        // erf(x) = 2/sqrt(pi) e^(-x^2) sum 2^n x^(2n+1) / (2n+1)!!, all terms positive.
        double term = ax;
        double sum = ax;
        double x2 = 2 * ax * ax;
        for (int n = 0; n < 200; n++)
        {
            term *= x2 / ((2 * n) + 3);
            sum += term;
            if (term < sum * 1e-17)
            {
                break;
            }
        }

        double r = 2 / SqrtPi * ExpNegSquare(ax) * sum;
        return x < 0 ? -r : r;
    }

    public static double Erfc(double x)
    {
        if (x < 0)
        {
            return 2 - Erfc(-x);
        }

        if (x < 2.5)
        {
            return 1 - Erf(x);
        }

        if (x > 27)
        {
            return 0;
        }

        // erfc(x) = e^(-x^2)/sqrt(pi) / (x + (1/2)/(x + 1/(x + (3/2)/(x + ...)))), evaluated from the tail up.
        double f = x;
        for (int k = 150; k >= 1; k--)
        {
            f = x + (k / 2.0 / f);
        }

        return ExpNegSquare(x) / SqrtPi / f;
    }

    // ---- normal ------------------------------------------------------------------------------------------------------------------

    public static double NormalCdf(double z) => 0.5 * Erfc(-z / Sqrt2);

    public static double NormalPdf(double z) => Exp(-0.5 * z * z) / SqrtTwoPi;

    private static readonly double[] AcA = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
    private static readonly double[] AcB = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
    private static readonly double[] AcC = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
    private static readonly double[] AcD = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];

    /// <summary>The standard normal quantile for 0 &lt; p &lt; 1: a rational start refined by Halley steps against <see cref="NormalCdf"/>.</summary>
    public static double NormalInverse(double p)
    {
        if (p > 0.5)
        {
            return -NormalInverse(1 - p);
        }

        double x;
        if (p < 0.02425)
        {
            double q = Math.Sqrt(-2 * Ln(p));
            x = ((((((AcC[0] * q) + AcC[1]) * q + AcC[2]) * q + AcC[3]) * q + AcC[4]) * q + AcC[5]) /
                (((((AcD[0] * q) + AcD[1]) * q + AcD[2]) * q + AcD[3]) * q + 1);
        }
        else
        {
            double q = p - 0.5;
            double r = q * q;
            x = ((((((AcA[0] * r) + AcA[1]) * r + AcA[2]) * r + AcA[3]) * r + AcA[4]) * r + AcA[5]) * q /
                (((((((AcB[0] * r) + AcB[1]) * r + AcB[2]) * r + AcB[3]) * r + AcB[4]) * r) + 1);
        }

        for (int i = 0; i < 3; i++)
        {
            double e = NormalCdf(x) - p;
            double u = e * SqrtTwoPi * Exp(x * x / 2);
            x -= u / (1 + (x * u / 2));
        }

        return x;
    }

    // ---- incomplete gamma --------------------------------------------------------------------------------------------------------

    /// <summary>Lower and upper regularized incomplete gamma P(a, x) and Q(a, x), each computed on the side that does not cancel.</summary>
    public static void IncompleteGamma(double a, double x, out double p, out double q)
    {
        if (x <= 0)
        {
            p = 0;
            q = 1;
            return;
        }

        if (double.IsPositiveInfinity(x))
        {
            p = 1;
            q = 0;
            return;
        }

        double front = Exp((-x) + (a * Ln(x)) - LogGamma(a));
        if (x < a + 1)
        {
            double term = 1 / a;
            double sum = term;
            for (int n = 1; n < MaxIterations; n++)
            {
                term *= x / (a + n);
                sum += term;
                if (Math.Abs(term) < Math.Abs(sum) * Eps)
                {
                    break;
                }
            }

            p = Math.Min(1, sum * front);
            q = 1 - p;
        }
        else
        {
            // Continued fraction (modified Lentz).
            const double tiny = 1e-300;
            double b = x + 1 - a;
            double c = 1 / tiny;
            double d = 1 / b;
            double h = d;
            for (int i = 1; i < MaxIterations; i++)
            {
                double an = -i * (i - a);
                b += 2;
                d = (an * d) + b;
                if (Math.Abs(d) < tiny)
                {
                    d = tiny;
                }

                c = b + (an / c);
                if (Math.Abs(c) < tiny)
                {
                    c = tiny;
                }

                d = 1 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1) < Eps)
                {
                    break;
                }
            }

            q = Math.Min(1, front * h);
            p = 1 - q;
        }
    }

    // ---- incomplete beta ---------------------------------------------------------------------------------------------------------

    private static double BetaFraction(double a, double b, double x)
    {
        const double tiny = 1e-300;
        double qab = a + b;
        double qap = a + 1;
        double qam = a - 1;
        double c = 1;
        double d = 1 - (qab * x / qap);
        if (Math.Abs(d) < tiny)
        {
            d = tiny;
        }

        d = 1 / d;
        double h = d;
        for (int m = 1; m < MaxIterations; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + (aa * d);
            if (Math.Abs(d) < tiny)
            {
                d = tiny;
            }

            c = 1 + (aa / c);
            if (Math.Abs(c) < tiny)
            {
                c = tiny;
            }

            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + (aa * d);
            if (Math.Abs(d) < tiny)
            {
                d = tiny;
            }

            c = 1 + (aa / c);
            if (Math.Abs(c) < tiny)
            {
                c = tiny;
            }

            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < Eps)
            {
                break;
            }
        }

        return h;
    }

    /// <summary>The regularized incomplete beta function I_x(a, b) and its complement, each on the side that does not cancel.</summary>
    public static void IncompleteBeta(double x, double a, double b, out double lower, out double upper)
    {
        if (x <= 0)
        {
            lower = 0;
            upper = 1;
            return;
        }

        if (x >= 1)
        {
            lower = 1;
            upper = 0;
            return;
        }

        double front = Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + (a * Ln(x)) + (b * Ln1p(-x)));
        if (x < (a + 1) / (a + b + 2))
        {
            lower = Math.Min(1, front * BetaFraction(a, b, x) / a);
            upper = 1 - lower;
        }
        else
        {
            upper = Math.Min(1, front * BetaFraction(b, a, 1 - x) / b);
            lower = 1 - upper;
        }
    }

    // ---- inverse -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Solves f(x) = target for a monotone f by bisection to the last bit. With <paramref name="doubling"/> the upper end <paramref name="hi"/> is a first guess
    /// that is doubled until f crosses the target (NaN when it never does).
    /// </summary>
    public static double Solve(Func<double, double> f, bool increasing, double target, double lo, double hi, bool doubling)
    {
        bool Reaches(double x) => increasing ? f(x) >= target : f(x) <= target;
        if (doubling)
        {
            while (!Reaches(hi))
            {
                lo = hi;
                hi *= 2;
                if (hi > 1e300)
                {
                    return double.NaN;
                }
            }
        }

        for (int i = 0; i < 400; i++)
        {
            double mid = lo + ((hi - lo) / 2);
            if (mid <= lo || mid >= hi)
            {
                break;
            }

            if (Reaches(mid))
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return hi;
    }

    /// <summary>
    /// The quantile of an increasing distribution function: <paramref name="lower"/> is P(X &lt;= x), <paramref name="upper"/> is P(X &gt; x). The
    /// side with the smaller tail is solved so quantiles near 0 and 1 keep their digits.
    /// </summary>
    public static double Invert(Func<double, double> lower, Func<double, double> upper, double p, double lo, double hi, bool doubling) =>
        p > 0.5 ? Solve(upper, false, 1 - p, lo, hi, doubling) : Solve(lower, true, p, lo, hi, doubling);

    // ---- distribution cdfs -------------------------------------------------------------------------------------------------------

    public static void StudentTail(double t, double df, out double lower, out double upper)
    {
        // P(T <= t) for df degrees of freedom, through the incomplete beta function of df/(df + t^2).
        IncompleteBeta(df / (df + (t * t)), df / 2, 0.5, out double lo, out _);
        double tail = 0.5 * lo;
        double rest = 1 - tail;
        if (t >= 0)
        {
            lower = rest;
            upper = tail;
        }
        else
        {
            lower = tail;
            upper = rest;
        }

    }

    public static double StudentPdf(double t, double df) =>
        Exp(LogGamma((df + 1) / 2) - LogGamma(df / 2) - (0.5 * Ln(df * Math.PI)) - ((df + 1) / 2 * Ln1p(t * t / df)));

    public static void ChiSquare(double x, double df, out double lower, out double upper) => IncompleteGamma(df / 2, x / 2, out lower, out upper);

    public static void FDist(double x, double d1, double d2, out double lower, out double upper) =>
        IncompleteBeta(d1 * x / ((d1 * x) + d2), d1 / 2, d2 / 2, out lower, out upper);
}
