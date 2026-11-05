using System;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>The probability distributions and their inverses, with the legacy compatibility names (Microsoft's statistical function pages).</summary>
internal static class DistributionFunctions
{
    // Beyond these sizes a count is not exactly representable or a sum would not finish; the result is #NUM!.
    private const double MaxTrials = 1e15;
    private const double MaxTerms = 5e6;

    private static CalcValue Bad => Fx.Err(CalcError.Num);

    public static void Register(FunctionRegistry r)
    {
        // normal
        Def(r, "NORM.DIST", 4, 4, a => NormDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "NORMDIST", 4, 4, a => NormDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "NORM.S.DIST", 2, 2, a => NormDist(a[0], 0, 1, a[1] != 0));
        Def(r, "NORMSDIST", 1, 1, a => NormDist(a[0], 0, 1, true));
        Def(r, "NORM.INV", 3, 3, a => NormInv(a[0], a[1], a[2]));
        Def(r, "NORMINV", 3, 3, a => NormInv(a[0], a[1], a[2]));
        Def(r, "NORM.S.INV", 1, 1, a => NormInv(a[0], 0, 1));
        Def(r, "NORMSINV", 1, 1, a => NormInv(a[0], 0, 1));
        Def(r, "PHI", 1, 1, a => Fx.Num(SpecialMath.NormalPdf(a[0])));
        Def(r, "GAUSS", 1, 1, a => Fx.Num(SpecialMath.NormalCdf(a[0]) - 0.5));
        Def(r, "FISHER", 1, 1, a => a[0] <= -1 || a[0] >= 1 ? Bad : Fx.Num(0.5 * SpecialMath.Ln((1 + a[0]) / (1 - a[0]))));
        Def(r, "FISHERINV", 1, 1, a => Fx.Num(RecursiveAI.OneMath.Math64.Tanh(a[0])));
        Def(r, "LOGNORM.DIST", 4, 4, a => LogNormDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "LOGNORMDIST", 3, 3, a => LogNormDist(a[0], a[1], a[2], true));
        Def(r, "LOGNORM.INV", 3, 3, a => LogNormInv(a[0], a[1], a[2]));
        Def(r, "LOGINV", 3, 3, a => LogNormInv(a[0], a[1], a[2]));

        // gamma family
        Def(r, "GAMMALN", 1, 1, a => a[0] <= 0 ? Bad : Fx.Num(SpecialMath.LogGamma(a[0])));
        Def(r, "GAMMALN.PRECISE", 1, 1, a => a[0] <= 0 ? Bad : Fx.Num(SpecialMath.LogGamma(a[0])));
        Def(r, "GAMMA", 1, 1, a => GammaFn(a[0]));
        Def(r, "GAMMA.DIST", 4, 4, a => GammaDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "GAMMADIST", 4, 4, a => GammaDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "GAMMA.INV", 3, 3, a => GammaInv(a[0], a[1], a[2]));
        Def(r, "GAMMAINV", 3, 3, a => GammaInv(a[0], a[1], a[2]));
        Def(r, "EXPON.DIST", 3, 3, a => ExponDist(a[0], a[1], a[2] != 0));
        Def(r, "EXPONDIST", 3, 3, a => ExponDist(a[0], a[1], a[2] != 0));
        Def(r, "WEIBULL.DIST", 4, 4, a => WeibullDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "WEIBULL", 4, 4, a => WeibullDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "CHISQ.DIST", 3, 3, a => ChiDist(a[0], a[1], a[2] != 0, false));
        Def(r, "CHISQ.DIST.RT", 2, 2, a => ChiDist(a[0], a[1], true, true));
        Def(r, "CHIDIST", 2, 2, a => ChiDist(a[0], a[1], true, true));
        Def(r, "CHISQ.INV", 2, 2, a => ChiInv(a[0], a[1], false));
        Def(r, "CHISQ.INV.RT", 2, 2, a => ChiInv(a[0], a[1], true));
        Def(r, "CHIINV", 2, 2, a => ChiInv(a[0], a[1], true));

        // t, F, beta
        Def(r, "T.DIST", 3, 3, a => TDist(a[0], a[1], a[2] != 0));
        Def(r, "T.DIST.RT", 2, 2, a => TTail(a[0], a[1], 1));
        Def(r, "T.DIST.2T", 2, 2, a => a[0] < 0 ? Bad : TTail(a[0], a[1], 2));
        Def(r, "TDIST", 3, 3, a => a[0] < 0 || (Math.Truncate(a[2]) != 1 && Math.Truncate(a[2]) != 2) ? Bad : TTail(a[0], a[1], (int)Math.Truncate(a[2])));
        Def(r, "T.INV", 2, 2, a => TInv(a[0], a[1], false));
        Def(r, "T.INV.2T", 2, 2, a => TInv(a[0], a[1], true));
        Def(r, "TINV", 2, 2, a => TInv(a[0], a[1], true));
        Def(r, "F.DIST", 4, 4, a => FDistFn(a[0], a[1], a[2], a[3] != 0, false));
        Def(r, "F.DIST.RT", 3, 3, a => FDistFn(a[0], a[1], a[2], true, true));
        Def(r, "FDIST", 3, 3, a => FDistFn(a[0], a[1], a[2], true, true));
        Def(r, "F.INV", 3, 3, a => FInv(a[0], a[1], a[2], false));
        Def(r, "F.INV.RT", 3, 3, a => FInv(a[0], a[1], a[2], true));
        Def(r, "FINV", 3, 3, a => FInv(a[0], a[1], a[2], true));
        Def(r, "BETA.DIST", 4, 6, a => BetaDist(a[0], a[1], a[2], a[3] != 0, Opt(a, 4, 0), Opt(a, 5, 1)));
        Def(r, "BETADIST", 3, 5, a => BetaDist(a[0], a[1], a[2], true, Opt(a, 3, 0), Opt(a, 4, 1)));
        Def(r, "BETA.INV", 3, 5, a => BetaInv(a[0], a[1], a[2], Opt(a, 3, 0), Opt(a, 4, 1)));
        Def(r, "BETAINV", 3, 5, a => BetaInv(a[0], a[1], a[2], Opt(a, 3, 0), Opt(a, 4, 1)));

        // discrete
        Def(r, "BINOM.DIST", 4, 4, a => BinomDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "BINOMDIST", 4, 4, a => BinomDist(a[0], a[1], a[2], a[3] != 0));
        Def(r, "BINOM.DIST.RANGE", 3, 4, a => BinomRange(a[0], a[1], a[2], Opt(a, 3, double.NaN)));
        Def(r, "BINOM.INV", 3, 3, a => BinomInv(a[0], a[1], a[2]));
        Def(r, "CRITBINOM", 3, 3, a => BinomInv(a[0], a[1], a[2]));
        Def(r, "POISSON.DIST", 3, 3, a => Poisson(a[0], a[1], a[2] != 0));
        Def(r, "POISSON", 3, 3, a => Poisson(a[0], a[1], a[2] != 0));
        Def(r, "NEGBINOM.DIST", 4, 4, a => NegBinom(a[0], a[1], a[2], a[3] != 0));
        Def(r, "NEGBINOMDIST", 3, 3, a => NegBinom(a[0], a[1], a[2], false));
        Def(r, "HYPGEOM.DIST", 5, 5, a => HyperGeom(a[0], a[1], a[2], a[3], a[4] != 0));
        Def(r, "HYPGEOMDIST", 4, 4, a => HyperGeom(a[0], a[1], a[2], a[3], false));

        // confidence intervals
        Def(r, "CONFIDENCE", 3, 3, a => ConfidenceNorm(a[0], a[1], a[2]));
        Def(r, "CONFIDENCE.NORM", 3, 3, a => ConfidenceNorm(a[0], a[1], a[2]));
        Def(r, "CONFIDENCE.T", 3, 3, a => ConfidenceT(a[0], a[1], a[2]));
    }

    // Registers a function whose arguments are all numbers (booleans and numeric text are converted; an omitted optional argument is NaN).
    private static void Def(FunctionRegistry r, string name, int min, int max, Func<double[], CalcValue> f)
    {
        r.Add(FunctionDefinition.Create(name, min, max, [], (_, a) =>
        {
            var x = new double[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].IsMissing && i >= min)
                {
                    x[i] = double.NaN;
                    continue;
                }

                if (!Fx.TryNum(a[i], out x[i], out CalcValue e))
                {
                    return e;
                }
            }

            return f(x);
        }));
    }

    private static double Opt(double[] a, int i, double dflt) => i < a.Length && !double.IsNaN(a[i]) ? a[i] : dflt;

    // ---- normal ------------------------------------------------------------------------------------------------------------------

    private static CalcValue NormDist(double x, double mean, double sd, bool cumulative)
    {
        if (sd <= 0)
        {
            return Bad;
        }

        double z = (x - mean) / sd;
        return Fx.Num(cumulative ? SpecialMath.NormalCdf(z) : SpecialMath.NormalPdf(z) / sd);
    }

    private static CalcValue NormInv(double p, double mean, double sd)
    {
        if (p <= 0 || p >= 1 || sd <= 0)
        {
            return Bad;
        }

        return Fx.Num(mean + (sd * SpecialMath.NormalInverse(p)));
    }

    private static CalcValue LogNormDist(double x, double mean, double sd, bool cumulative)
    {
        if (x <= 0 || sd <= 0)
        {
            return Bad;
        }

        double z = (SpecialMath.Ln(x) - mean) / sd;
        return Fx.Num(cumulative ? SpecialMath.NormalCdf(z) : SpecialMath.NormalPdf(z) / (x * sd));
    }

    private static CalcValue LogNormInv(double p, double mean, double sd)
    {
        if (p <= 0 || p >= 1 || sd <= 0)
        {
            return Bad;
        }

        return Fx.Num(SpecialMath.Exp(mean + (sd * SpecialMath.NormalInverse(p))));
    }

    // ---- gamma family ------------------------------------------------------------------------------------------------------------

    private static CalcValue GammaFn(double x)
    {
        if (x == 0 || (x < 0 && x == Math.Floor(x)) || x > 171.6)
        {
            return Bad;
        }

        return Fx.Num(SpecialMath.Gamma(x));
    }

    private static CalcValue GammaDist(double x, double alpha, double beta, bool cumulative)
    {
        if (x < 0 || alpha <= 0 || beta <= 0)
        {
            return Bad;
        }

        if (cumulative)
        {
            SpecialMath.IncompleteGamma(alpha, x / beta, out double p, out _);
            return Fx.Num(p);
        }

        if (x == 0)
        {
            return alpha < 1 ? Bad : Fx.Num(alpha == 1 ? 1 / beta : 0);
        }

        return Fx.Num(SpecialMath.Exp(((alpha - 1) * SpecialMath.Ln(x)) - (x / beta) - SpecialMath.LogGamma(alpha) - (alpha * SpecialMath.Ln(beta))));
    }

    private static CalcValue GammaInv(double p, double alpha, double beta)
    {
        if (p < 0 || p > 1 || alpha <= 0 || beta <= 0)
        {
            return Bad;
        }

        if (p == 0)
        {
            return Fx.Num(0);
        }

        if (p == 1)
        {
            return Bad;
        }

        double x = SpecialMath.Invert(
            v => { SpecialMath.IncompleteGamma(alpha, v, out double lo, out _); return lo; },
            v => { SpecialMath.IncompleteGamma(alpha, v, out _, out double up); return up; },
            p, 0, Math.Max(alpha, 1), true);
        return double.IsNaN(x) ? Bad : Fx.Num(x * beta);
    }

    private static CalcValue ExponDist(double x, double lambda, bool cumulative)
    {
        if (x < 0 || lambda <= 0)
        {
            return Bad;
        }

        return Fx.Num(cumulative ? -RecursiveAI.OneMath.Math64.Expm1(-lambda * x) : lambda * SpecialMath.Exp(-lambda * x));
    }

    private static CalcValue WeibullDist(double x, double alpha, double beta, bool cumulative)
    {
        if (x < 0 || alpha <= 0 || beta <= 0)
        {
            return Bad;
        }

        double q = SpecialMath.Pow(x / beta, alpha);
        if (cumulative)
        {
            return Fx.Num(-RecursiveAI.OneMath.Math64.Expm1(-q));
        }

        if (x == 0)
        {
            return alpha < 1 ? Bad : Fx.Num(alpha == 1 ? 1 / beta : 0);
        }

        return Fx.Num(alpha / beta * SpecialMath.Pow(x / beta, alpha - 1) * SpecialMath.Exp(-q));
    }

    private static bool BadDegrees(double df) => df < 1 || df >= 1e10;

    private static CalcValue ChiDist(double x, double df, bool cumulative, bool rightTail)
    {
        df = Math.Truncate(df);
        if (x < 0 || BadDegrees(df))
        {
            return Bad;
        }

        if (!cumulative)
        {
            if (x == 0)
            {
                return df > 2 ? Fx.Num(0) : df == 2 ? Fx.Num(0.5) : Bad;
            }

            double h = df / 2;
            return Fx.Num(SpecialMath.Exp(((h - 1) * SpecialMath.Ln(x)) - (x / 2) - (h * SpecialMath.Ln(2)) - SpecialMath.LogGamma(h)));
        }

        SpecialMath.ChiSquare(x, df, out double lower, out double upper);
        return Fx.Num(rightTail ? upper : lower);
    }

    private static CalcValue ChiInv(double p, double df, bool rightTail)
    {
        df = Math.Truncate(df);
        if (p < 0 || p > 1 || BadDegrees(df))
        {
            return Bad;
        }

        if (rightTail ? p == 1 : p == 0)
        {
            return Fx.Num(0);
        }

        if (rightTail ? p == 0 : p == 1)
        {
            return Bad;
        }

        double x = rightTail
            ? SpecialMath.Solve(v => { SpecialMath.ChiSquare(v, df, out _, out double up); return up; }, false, p, 0, Math.Max(df, 1), true)
            : SpecialMath.Invert(
                v => { SpecialMath.ChiSquare(v, df, out double lo, out _); return lo; },
                v => { SpecialMath.ChiSquare(v, df, out _, out double up); return up; },
                p, 0, Math.Max(df, 1), true);
        return double.IsNaN(x) ? Bad : Fx.Num(x);
    }

    // ---- t, F, beta --------------------------------------------------------------------------------------------------------------

    private static CalcValue TDist(double x, double df, bool cumulative)
    {
        df = Math.Truncate(df);
        if (BadDegrees(df))
        {
            return Bad;
        }

        if (!cumulative)
        {
            return Fx.Num(SpecialMath.StudentPdf(x, df));
        }

        SpecialMath.StudentTail(x, df, out double lower, out _);
        return Fx.Num(lower);
    }

    private static CalcValue TTail(double x, double df, int tails)
    {
        df = Math.Truncate(df);
        if (BadDegrees(df))
        {
            return Bad;
        }

        SpecialMath.StudentTail(Math.Abs(x), df, out _, out double upper);
        if (x < 0 && tails == 1)
        {
            // Right tail of a negative t is the lower part of the distribution.
            return Fx.Num(1 - upper);
        }

        return Fx.Num(tails == 2 ? 2 * upper : upper);
    }

    private static CalcValue TInv(double p, double df, bool twoTailed)
    {
        df = Math.Truncate(df);
        if (p <= 0 || p > 1 || BadDegrees(df) || (!twoTailed && p == 1))
        {
            return Bad;
        }

        if (twoTailed)
        {
            if (p == 1)
            {
                return Fx.Num(0);
            }

            return Fx.Num(SpecialMath.Solve(v => { SpecialMath.StudentTail(v, df, out _, out double up); return up; }, false, p / 2, 0, 1, true));
        }

        if (p == 0.5)
        {
            return Fx.Num(0);
        }

        double q = p < 0.5 ? p : 1 - p;
        double t = SpecialMath.Solve(v => { SpecialMath.StudentTail(v, df, out _, out double up); return up; }, false, q, 0, 1, true);
        return Fx.Num(p < 0.5 ? -t : t);
    }

    private static bool BadF(double d1, double d2) => BadDegrees(d1) || BadDegrees(d2);

    private static CalcValue FDistFn(double x, double d1, double d2, bool cumulative, bool rightTail)
    {
        d1 = Math.Truncate(d1);
        d2 = Math.Truncate(d2);
        if (x < 0 || BadF(d1, d2))
        {
            return Bad;
        }

        if (!cumulative)
        {
            if (x == 0)
            {
                return d1 > 2 ? Fx.Num(0) : d1 == 2 ? Fx.Num(1) : Bad;
            }

            double lnb = SpecialMath.LogGamma(d1 / 2) + SpecialMath.LogGamma(d2 / 2) - SpecialMath.LogGamma((d1 + d2) / 2);
            double ln = (0.5 * ((d1 * SpecialMath.Ln(d1 * x)) + (d2 * SpecialMath.Ln(d2)) - ((d1 + d2) * SpecialMath.Ln((d1 * x) + d2)))) - SpecialMath.Ln(x) - lnb;
            return Fx.Num(SpecialMath.Exp(ln));
        }

        SpecialMath.FDist(x, d1, d2, out double lower, out double upper);
        return Fx.Num(rightTail ? upper : lower);
    }

    private static CalcValue FInv(double p, double d1, double d2, bool rightTail)
    {
        d1 = Math.Truncate(d1);
        d2 = Math.Truncate(d2);
        if (p < 0 || p > 1 || BadF(d1, d2))
        {
            return Bad;
        }

        if (rightTail ? p == 1 : p == 0)
        {
            return Fx.Num(0);
        }

        if (rightTail ? p == 0 : p == 1)
        {
            return Bad;
        }

        double x = rightTail
            ? SpecialMath.Solve(v => { SpecialMath.FDist(v, d1, d2, out _, out double up); return up; }, false, p, 0, 1, true)
            : SpecialMath.Invert(
                v => { SpecialMath.FDist(v, d1, d2, out double lo, out _); return lo; },
                v => { SpecialMath.FDist(v, d1, d2, out _, out double up); return up; },
                p, 0, 1, true);
        return double.IsNaN(x) ? Bad : Fx.Num(x);
    }

    private static CalcValue BetaDist(double x, double a, double b, bool cumulative, double lo, double hi)
    {
        if (a <= 0 || b <= 0 || x < lo || x > hi || lo == hi)
        {
            return Bad;
        }

        double z = (x - lo) / (hi - lo);
        if (cumulative)
        {
            SpecialMath.IncompleteBeta(z, a, b, out double l, out _);
            return Fx.Num(l);
        }

        if (z == 0 || z == 1)
        {
            // The density at an end point is finite only for a shape of at least 1.
            double shape = z == 0 ? a : b;
            if (shape < 1)
            {
                return Bad;
            }

            if (shape > 1)
            {
                return Fx.Num(0);
            }

            return Fx.Num(SpecialMath.Exp(SpecialMath.LogGamma(a + b) - SpecialMath.LogGamma(a) - SpecialMath.LogGamma(b)) / (hi - lo));
        }

        double ln = SpecialMath.LogGamma(a + b) - SpecialMath.LogGamma(a) - SpecialMath.LogGamma(b) + ((a - 1) * SpecialMath.Ln(z)) + ((b - 1) * SpecialMath.Ln1p(-z));
        return Fx.Num(SpecialMath.Exp(ln) / (hi - lo));
    }

    private static CalcValue BetaInv(double p, double a, double b, double lo, double hi)
    {
        if (p <= 0 || p > 1 || a <= 0 || b <= 0 || lo >= hi)
        {
            return Bad;
        }

        if (p == 1)
        {
            return Fx.Num(hi);
        }

        double z = SpecialMath.Invert(
            v => { SpecialMath.IncompleteBeta(v, a, b, out double l, out _); return l; },
            v => { SpecialMath.IncompleteBeta(v, a, b, out _, out double u); return u; },
            p, 0, 1, false);
        return Fx.Num(lo + (z * (hi - lo)));
    }

    // ---- discrete distributions --------------------------------------------------------------------------------------------------

    private static double BinomPmf(double k, double n, double p)
    {
        if (p == 0)
        {
            return k == 0 ? 1 : 0;
        }

        if (p == 1)
        {
            return k == n ? 1 : 0;
        }

        if (n <= 60)
        {
            return SpecialMath.Choose(n, k) * SpecialMath.Pow(p, k) * SpecialMath.Pow(1 - p, n - k);
        }

        return SpecialMath.Exp(SpecialMath.LogChoose(n, k) + (k * SpecialMath.Ln(p)) + ((n - k) * SpecialMath.Ln1p(-p)));
    }

    // P(X <= k) for X ~ Binomial(n, p).
    private static double BinomCdf(double k, double n, double p)
    {
        if (k >= n)
        {
            return 1;
        }

        if (k < 0)
        {
            return 0;
        }

        if (n <= 60)
        {
            double s = 0;
            for (int i = 0; i <= (int)k; i++)
            {
                s += BinomPmf(i, n, p);
            }

            return Math.Min(1, s);
        }

        if (p == 0)
        {
            return 1;
        }

        if (p == 1)
        {
            return 0;
        }

        SpecialMath.IncompleteBeta(1 - p, n - k, k + 1, out double lower, out _);
        return lower;
    }

    private static CalcValue BinomDist(double k, double n, double p, bool cumulative)
    {
        k = Math.Truncate(k);
        n = Math.Truncate(n);
        if (n < 0 || k < 0 || k > n || p < 0 || p > 1)
        {
            return Bad;
        }

        return Fx.Num(cumulative ? BinomCdf(k, n, p) : BinomPmf(k, n, p));
    }

    private static CalcValue BinomRange(double n, double p, double s, double s2)
    {
        n = Math.Truncate(n);
        s = Math.Truncate(s);
        if (n < 0 || p < 0 || p > 1 || s < 0 || s > n)
        {
            return Bad;
        }

        if (double.IsNaN(s2))
        {
            return Fx.Num(BinomPmf(s, n, p));
        }

        s2 = Math.Truncate(s2);
        if (s2 < s || s2 > n)
        {
            return Bad;
        }

        double sum = 0;
        if (n <= 5000 && s2 - s <= 5000)
        {
            for (double k = s; k <= s2; k++)
            {
                sum += BinomPmf(k, n, p);
            }

            return Fx.Num(Math.Min(1, sum));
        }

        return Fx.Num(Math.Max(0, BinomCdf(s2, n, p) - (s > 0 ? BinomCdf(s - 1, n, p) : 0)));
    }

    private static CalcValue BinomInv(double n, double p, double alpha)
    {
        n = Math.Truncate(n);
        if (n < 0 || n > MaxTrials || p < 0 || p > 1 || alpha < 0 || alpha > 1)
        {
            return Bad;
        }

        double lo = 0;
        double hi = n;
        while (lo < hi)
        {
            double mid = Math.Floor((lo + hi) / 2);
            if (BinomCdf(mid, n, p) >= alpha)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return Fx.Num(lo);
    }

    private static CalcValue Poisson(double k, double mean, bool cumulative)
    {
        k = Math.Truncate(k);
        if (k < 0 || mean < 0)
        {
            return Bad;
        }

        if (mean == 0)
        {
            return Fx.Num(cumulative ? 1 : k == 0 ? 1 : 0);
        }

        if (!cumulative)
        {
            return Fx.Num(PoissonPmf(k, mean));
        }

        if (mean <= 700 && k <= 2000)
        {
            double term = SpecialMath.Exp(-mean);
            double sum = term;
            for (int i = 1; i <= (int)k; i++)
            {
                term *= mean / i;
                sum += term;
            }

            return Fx.Num(Math.Min(1, sum));
        }

        SpecialMath.IncompleteGamma(k + 1, mean, out _, out double q);
        return Fx.Num(q);
    }

    private static double PoissonPmf(double k, double mean) =>
        SpecialMath.Exp(-mean + (k * SpecialMath.Ln(mean)) - SpecialMath.LogGamma(k + 1));

    private static CalcValue NegBinom(double f, double s, double p, bool cumulative)
    {
        f = Math.Truncate(f);
        s = Math.Truncate(s);
        if (f < 0 || s < 1 || p < 0 || p > 1)
        {
            return Bad;
        }

        if (p == 0)
        {
            return Fx.Num(0);
        }

        if (cumulative)
        {
            SpecialMath.IncompleteBeta(p, s, f + 1, out double lower, out _);
            return Fx.Num(lower);
        }

        if (p == 1)
        {
            return Fx.Num(f == 0 ? 1 : 0);
        }

        if (f + s - 1 <= 60)
        {
            return Fx.Num(SpecialMath.Choose(f + s - 1, f) * SpecialMath.Pow(p, s) * SpecialMath.Pow(1 - p, f));
        }

        return Fx.Num(SpecialMath.Exp(SpecialMath.LogChoose(f + s - 1, f) + (s * SpecialMath.Ln(p)) + (f * SpecialMath.Ln1p(-p))));
    }

    private static double HyperPmf(double k, double n, double kPop, double nPop)
    {
        if (nPop <= 1000)
        {
            return SpecialMath.Choose(kPop, k) * SpecialMath.Choose(nPop - kPop, n - k) / SpecialMath.Choose(nPop, n);
        }

        return SpecialMath.Exp(SpecialMath.LogChoose(kPop, k) + SpecialMath.LogChoose(nPop - kPop, n - k) - SpecialMath.LogChoose(nPop, n));
    }

    private static CalcValue HyperGeom(double s, double n, double kPop, double nPop, bool cumulative)
    {
        s = Math.Truncate(s);
        n = Math.Truncate(n);
        kPop = Math.Truncate(kPop);
        nPop = Math.Truncate(nPop);
        if (s < 0 || s > Math.Min(n, kPop) || s < Math.Max(0, n - nPop + kPop) || n <= 0 || n > nPop || kPop <= 0 || kPop > nPop || nPop <= 0)
        {
            return Bad;
        }

        // The cumulative sum adds one term per possible outcome; refuse populations whose sum would not finish.
        if (cumulative && s - Math.Max(0, n - nPop + kPop) > MaxTerms)
        {
            return Bad;
        }

        if (!cumulative)
        {
            return Fx.Num(HyperPmf(s, n, kPop, nPop));
        }

        double sum = 0;
        for (double i = Math.Max(0, n - nPop + kPop); i <= s; i++)
        {
            sum += HyperPmf(i, n, kPop, nPop);
        }

        return Fx.Num(Math.Min(1, sum));
    }

    // ---- confidence intervals ----------------------------------------------------------------------------------------------------

    private static CalcValue ConfidenceNorm(double alpha, double sd, double size)
    {
        size = Math.Truncate(size);
        if (alpha <= 0 || alpha >= 1 || sd <= 0 || size < 1)
        {
            return Bad;
        }

        return Fx.Num(-SpecialMath.NormalInverse(alpha / 2) * sd / Math.Sqrt(size));
    }

    private static CalcValue ConfidenceT(double alpha, double sd, double size)
    {
        size = Math.Truncate(size);
        if (alpha <= 0 || alpha >= 1 || sd <= 0 || size < 1)
        {
            return Bad;
        }

        if (size == 1)
        {
            return Fx.Err(CalcError.Div0);
        }

        CalcValue t = TInv(alpha, size - 1, true);
        return t.IsError ? t : Fx.Num(t.Number * sd / Math.Sqrt(size));
    }
}
