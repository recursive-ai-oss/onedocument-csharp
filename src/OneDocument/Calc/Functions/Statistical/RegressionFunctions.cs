using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>Correlation, simple and multiple regression, forecasting and the significance tests.</summary>
internal static class RegressionFunctions
{
    private static readonly ArgKind[] Any2 = [ArgKind.Any, ArgKind.Any];
    private static readonly ArgKind[] Any3 = [ArgKind.Any, ArgKind.Any, ArgKind.Any];

    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("CORREL", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.Correl)));
        r.Add(FunctionDefinition.Create("PEARSON", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.Correl)));
        r.Add(FunctionDefinition.Create("COVAR", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.CovP)));
        r.Add(FunctionDefinition.Create("COVARIANCE.P", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.CovP)));
        r.Add(FunctionDefinition.Create("COVARIANCE.S", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.CovS)));
        r.Add(FunctionDefinition.Create("RSQ", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.Rsq)));
        r.Add(FunctionDefinition.Create("SLOPE", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.Slope)));
        r.Add(FunctionDefinition.Create("INTERCEPT", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.Intercept)));
        r.Add(FunctionDefinition.Create("STEYX", 2, 2, Any2, (c, a) => Paired(c, a[0], a[1], P.Steyx)));
        r.Add(FunctionDefinition.Create("FORECAST", 3, 3, [ArgKind.Value, ArgKind.Any, ArgKind.Any], Forecast));
        r.Add(FunctionDefinition.Create("FORECAST.LINEAR", 3, 3, [ArgKind.Value, ArgKind.Any, ArgKind.Any], Forecast));
        r.Add(FunctionDefinition.Create("LINEST", 1, 4, [ArgKind.Any, ArgKind.Any, ArgKind.Value, ArgKind.Value], (c, a) => LinEst(c, a, false)));
        r.Add(FunctionDefinition.Create("LOGEST", 1, 4, [ArgKind.Any, ArgKind.Any, ArgKind.Value, ArgKind.Value], (c, a) => LinEst(c, a, true)));
        r.Add(FunctionDefinition.Create("TREND", 1, 4, [ArgKind.Any, ArgKind.Any, ArgKind.Any, ArgKind.Value], (c, a) => Trend(c, a, false)));
        r.Add(FunctionDefinition.Create("GROWTH", 1, 4, [ArgKind.Any, ArgKind.Any, ArgKind.Any, ArgKind.Value], (c, a) => Trend(c, a, true)));

        // FORECAST.ETS needs the exact "AAA" exponential-smoothing algorithm, which Microsoft's reference names but does not specify, so these
        // return #N/A (the result Excel gives when it cannot forecast) after Excel's own argument rules would have been applied.
        r.Add(FunctionDefinition.Create("FORECAST.ETS", 3, 6, [ArgKind.Any], (_, _) => Fx.Err(CalcError.NA), repeatLast: true));
        r.Add(FunctionDefinition.Create("FORECAST.ETS.CONFINT", 3, 7, [ArgKind.Any], (_, _) => Fx.Err(CalcError.NA), repeatLast: true));
        r.Add(FunctionDefinition.Create("FORECAST.ETS.SEASONALITY", 2, 4, [ArgKind.Any], (_, _) => Fx.Err(CalcError.NA), repeatLast: true));
        r.Add(FunctionDefinition.Create("FORECAST.ETS.STAT", 3, 6, [ArgKind.Any], (_, _) => Fx.Err(CalcError.NA), repeatLast: true));

        r.Add(FunctionDefinition.Create("Z.TEST", 2, 3, [ArgKind.Any, ArgKind.Value, ArgKind.Value], ZTest));
        r.Add(FunctionDefinition.Create("ZTEST", 2, 3, [ArgKind.Any, ArgKind.Value, ArgKind.Value], ZTest));
        r.Add(FunctionDefinition.Create("T.TEST", 4, 4, [ArgKind.Any, ArgKind.Any, ArgKind.Value, ArgKind.Value], TTest));
        r.Add(FunctionDefinition.Create("TTEST", 4, 4, [ArgKind.Any, ArgKind.Any, ArgKind.Value, ArgKind.Value], TTest));
        r.Add(FunctionDefinition.Create("F.TEST", 2, 2, Any2, FTest));
        r.Add(FunctionDefinition.Create("FTEST", 2, 2, Any2, FTest));
        r.Add(FunctionDefinition.Create("CHISQ.TEST", 2, 2, Any2, ChiTest));
        r.Add(FunctionDefinition.Create("CHITEST", 2, 2, Any2, ChiTest));
        _ = Any3;
    }

    private enum P
    {
        Correl,
        CovP,
        CovS,
        Rsq,
        Slope,
        Intercept,
        Steyx,
    }

    // The sums every paired statistic is made of: means and the centred sums of squares and products.
    private static void Moments(List<double> x, List<double> y, out double mx, out double my, out double sxx, out double syy, out double sxy)
    {
        mx = Stat.Mean(x);
        my = Stat.Mean(y);
        sxx = 0;
        syy = 0;
        sxy = 0;
        for (int i = 0; i < x.Count; i++)
        {
            double dx = x[i] - mx;
            double dy = y[i] - my;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }
    }

    private static CalcValue Paired(Evaluator ev, in CalcValue first, in CalcValue second, P what)
    {
        // SLOPE, INTERCEPT, RSQ and STEYX take (known_y's, known_x's); the others are symmetric.
        if (Stat.Pairs(ev, first, second, out List<double> a, out List<double> b) is { } err)
        {
            return err;
        }

        int n = a.Count;
        List<double> y = a;
        List<double> x = b;
        switch (what)
        {
            case P.CovP:
                if (n < 1)
                {
                    return Fx.Err(CalcError.Div0);
                }

                Moments(a, b, out _, out _, out _, out _, out double cp);
                return Fx.Num(cp / n);
            case P.CovS:
                if (n < 2)
                {
                    return Fx.Err(CalcError.Div0);
                }

                Moments(a, b, out _, out _, out _, out _, out double cs);
                return Fx.Num(cs / (n - 1));
            default:
                break;
        }

        if (n < 2)
        {
            return Fx.Err(CalcError.Div0);
        }

        Moments(x, y, out double mx, out double my, out double sxx, out double syy, out double sxy);
        switch (what)
        {
            case P.Correl:
                return sxx == 0 || syy == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(sxy / Math.Sqrt(sxx * syy));
            case P.Rsq:
                return sxx == 0 || syy == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(sxy * sxy / (sxx * syy));
            case P.Slope:
                return sxx == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(sxy / sxx);
            case P.Intercept:
                return sxx == 0 ? Fx.Err(CalcError.Div0) : Fx.Num(my - (sxy / sxx * mx));
            default:
                if (n < 3 || sxx == 0)
                {
                    return Fx.Err(CalcError.Div0);
                }

                return Fx.Num(Math.Sqrt(Math.Max(0, syy - (sxy * sxy / sxx)) / (n - 2)));
        }
    }

    private static CalcValue Forecast(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x0, out CalcValue e))
        {
            return e;
        }

        if (Stat.Pairs(ev, a[1], a[2], out List<double> y, out List<double> x) is { } err)
        {
            return err;
        }

        if (y.Count < 2)
        {
            return Fx.Err(CalcError.Div0);
        }

        Moments(x, y, out double mx, out double my, out double sxx, out _, out double sxy);
        if (sxx == 0)
        {
            return Fx.Err(CalcError.Div0);
        }

        double slope = sxy / sxx;
        return Fx.Num(my + (slope * (x0 - mx)));
    }

    // ---- multiple regression -----------------------------------------------------------------------------------------------------

    private sealed class Fit
    {
        public double[] Coefficients = [];   // one per predictor, then the intercept when fitted (0 for a dropped predictor)
        public double[] StdErrors = [];
        public double R2;
        public double StdErrY;
        public double F;
        public double Df;
        public double SsReg;
        public double SsResid;
        public bool DfZero;
        public int Predictors;
    }

    // Ordinary least squares by Householder QR. A predictor that is (numerically) a combination of the ones before it is dropped: its
    // coefficient and standard error are 0, as LINEST reports for collinear columns.
    private static Fit Regress(double[][] cols, double[] y, bool constant)
    {
        int n = y.Length;
        int k = cols.Length;
        int p = k + (constant ? 1 : 0);
        var a = new double[p][];
        for (int j = 0; j < k; j++)
        {
            a[j] = (double[])cols[j].Clone();
        }

        if (constant)
        {
            a[k] = new double[n];
            Array.Fill(a[k], 1.0);
        }

        double[] qty = (double[])y.Clone();
        var rdiag = new double[p];
        var used = new bool[p];
        var r = new double[p, p];
        int rank = 0;
        var kept = new List<int>();
        var reflectors = new List<double[]>();
        var work = new double[p][];
        for (int j = 0; j < p; j++)
        {
            work[j] = (double[])a[j].Clone();
        }

        double[] scale = new double[p];
        for (int j = 0; j < p; j++)
        {
            double s = 0;
            foreach (double v in a[j])
            {
                s += v * v;
            }

            scale[j] = Math.Sqrt(s);
        }

        for (int j = 0; j < p; j++)
        {
            // Apply earlier reflectors to this column.
            for (int q = 0; q < reflectors.Count; q++)
            {
                Reflect(reflectors[q], q, work[j]);
            }

            double norm = 0;
            for (int i = rank; i < n; i++)
            {
                norm += work[j][i] * work[j][i];
            }

            norm = Math.Sqrt(norm);
            if (rank >= n || norm <= 1e-11 * Math.Max(scale[j], 1e-300))
            {
                continue;
            }

            var v = new double[n];
            double alpha = work[j][rank] > 0 ? -norm : norm;
            for (int i = rank; i < n; i++)
            {
                v[i] = work[j][i];
            }

            v[rank] -= alpha;
            double vn = 0;
            for (int i = rank; i < n; i++)
            {
                vn += v[i] * v[i];
            }

            vn = Math.Sqrt(vn);
            if (vn > 0)
            {
                for (int i = rank; i < n; i++)
                {
                    v[i] /= vn;
                }
            }

            reflectors.Add(v);
            Reflect(v, rank, work[j]);
            Reflect(v, rank, qty);
            kept.Add(j);
            used[j] = true;
            rdiag[j] = work[j][rank];
            rank++;
        }

        // R is the first `rank` rows of the reduced columns; rebuild it.
        for (int c = 0; c < kept.Count; c++)
        {
            for (int rr = 0; rr <= c; rr++)
            {
                r[rr, c] = work[kept[c]][rr];
            }
        }

        // Back substitution R b = Q'y.
        var b = new double[rank];
        for (int i = rank - 1; i >= 0; i--)
        {
            double s = qty[i];
            for (int c = i + 1; c < rank; c++)
            {
                s -= r[i, c] * b[c];
            }

            b[i] = s / r[i, i];
        }

        var fit = new Fit { Predictors = k, Coefficients = new double[p], StdErrors = new double[p] };
        for (int c = 0; c < kept.Count; c++)
        {
            fit.Coefficients[kept[c]] = b[c];
        }

        double ssResid = 0;
        for (int i = rank; i < n; i++)
        {
            ssResid += qty[i] * qty[i];
        }

        // Recompute the residual directly: more faithful when the fit is nearly exact.
        double direct = 0;
        for (int i = 0; i < n; i++)
        {
            double fitted = 0;
            for (int j = 0; j < p; j++)
            {
                fitted += fit.Coefficients[j] * a[j][i];
            }

            double d = y[i] - fitted;
            direct += d * d;
        }

        ssResid = direct;
        double ssTotal = 0;
        if (constant)
        {
            double my = 0;
            foreach (double v in y)
            {
                my += v;
            }

            my /= n;
            foreach (double v in y)
            {
                ssTotal += (v - my) * (v - my);
            }
        }
        else
        {
            foreach (double v in y)
            {
                ssTotal += v * v;
            }
        }

        fit.SsResid = ssResid;
        fit.SsReg = ssTotal - ssResid;
        fit.R2 = ssTotal == 0 ? 1 : fit.SsReg / ssTotal;
        fit.Df = n - rank;
        fit.DfZero = fit.Df <= 0;
        if (!fit.DfZero)
        {
            double s2 = ssResid / fit.Df;
            fit.StdErrY = Math.Sqrt(s2);
            int regressors = rank - (constant && used[k] ? 1 : 0);
            fit.F = regressors == 0 || s2 == 0 ? 0 : fit.SsReg / regressors / s2;

            // Standard errors: sqrt(s2 * diag((R'R)^-1)) = s * norm of the rows of R^-1.
            var rinv = new double[rank, rank];
            for (int c = 0; c < rank; c++)
            {
                rinv[c, c] = 1 / r[c, c];
                for (int i = c - 1; i >= 0; i--)
                {
                    double s = 0;
                    for (int m = i + 1; m <= c; m++)
                    {
                        s += r[i, m] * rinv[m, c];
                    }

                    rinv[i, c] = -s / r[i, i];
                }
            }

            for (int c = 0; c < kept.Count; c++)
            {
                double s = 0;
                for (int m = c; m < rank; m++)
                {
                    s += rinv[c, m] * rinv[c, m];
                }

                fit.StdErrors[kept[c]] = fit.StdErrY * Math.Sqrt(s);
            }
        }

        _ = rdiag;
        return fit;
    }

    private static void Reflect(double[] v, int from, double[] target)
    {
        double dot = 0;
        for (int i = from; i < target.Length; i++)
        {
            dot += v[i] * target[i];
        }

        dot *= 2;
        for (int i = from; i < target.Length; i++)
        {
            target[i] -= dot * v[i];
        }
    }

    // Reads known_y's and known_x's (LINEST's layout rules): y is one row or one column; the predictors are the other dimension of x.
    private static CalcValue? ReadModel(Evaluator ev, ReadOnlySpan<CalcValue> a, out double[] y, out double[][] cols, out bool yIsColumn, out int n)
    {
        y = [];
        cols = [];
        yIsColumn = true;
        n = 0;
        CalcArray? ya = Fx.AsArray(ev, a[0], out CalcValue e);
        if (ya is null)
        {
            return e;
        }

        if (ya.Rows != 1 && ya.Columns != 1)
        {
            return Fx.Err(CalcError.Ref);
        }

        yIsColumn = ya.Columns == 1;
        n = ya.Rows * ya.Columns;
        y = new double[n];
        for (int i = 0; i < n; i++)
        {
            CalcValue v = yIsColumn ? ya[i, 0] : ya[0, i];
            if (v.IsError)
            {
                return v;
            }

            if (v.Kind != ValueKind.Number)
            {
                return Fx.Err(CalcError.Value);
            }

            y[i] = v.Number;
        }

        bool xGiven = a.Length > 1 && !a[1].IsMissing;
        if (!xGiven)
        {
            cols = [Sequence(n)];
            return null;
        }

        CalcArray? xa = Fx.AsArray(ev, a[1], out e);
        if (xa is null)
        {
            return e;
        }

        int k = yIsColumn ? xa.Columns : xa.Rows;
        if ((yIsColumn ? xa.Rows : xa.Columns) != n)
        {
            return Fx.Err(CalcError.Ref);
        }

        cols = new double[k][];
        for (int j = 0; j < k; j++)
        {
            cols[j] = new double[n];
            for (int i = 0; i < n; i++)
            {
                CalcValue v = yIsColumn ? xa[i, j] : xa[j, i];
                if (v.IsError)
                {
                    return v;
                }

                if (v.Kind != ValueKind.Number)
                {
                    return Fx.Err(CalcError.Value);
                }

                cols[j][i] = v.Number;
            }
        }

        return null;
    }

    private static double[] Sequence(int n)
    {
        var s = new double[n];
        for (int i = 0; i < n; i++)
        {
            s[i] = i + 1;
        }

        return s;
    }

    private static CalcValue LinEst(Evaluator ev, ReadOnlySpan<CalcValue> a, bool log)
    {
        if (ReadModel(ev, a, out double[] y, out double[][] cols, out _, out int n) is { } err)
        {
            return err;
        }

        bool constant = true;
        if (a.Length > 2 && !a[2].IsMissing)
        {
            if (CalcConvert.ToBoolean(a[2], out constant) is var ce && ce != CalcError.None)
            {
                return Fx.Err(ce);
            }
        }

        bool stats = false;
        if (a.Length > 3 && !a[3].IsMissing)
        {
            if (CalcConvert.ToBoolean(a[3], out stats) is var se && se != CalcError.None)
            {
                return Fx.Err(se);
            }
        }

        if (log)
        {
            y = (double[])y.Clone();
            for (int i = 0; i < y.Length; i++)
            {
                if (y[i] <= 0)
                {
                    return Fx.Err(CalcError.Num);
                }

                y[i] = SpecialMath.Ln(y[i]);
            }
        }

        if (n == 0)
        {
            return Fx.Err(CalcError.Value);
        }

        Fit fit = Regress(cols, y, constant);
        int k = cols.Length;
        int width = k + 1;
        int rows = stats ? 5 : 1;
        CalcValue[] cells = ev.NewCells(rows, width);
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = CalcValue.FromError(CalcError.NA);
        }

        for (int j = 0; j < k; j++)
        {
            double coef = fit.Coefficients[k - 1 - j];
            cells[j] = Fx.Num(log ? SpecialMath.Exp(coef) : coef);
        }

        double intercept = constant ? fit.Coefficients[k] : 0;
        cells[k] = Fx.Num(log ? SpecialMath.Exp(intercept) : intercept);
        if (stats)
        {
            if (fit.DfZero)
            {
                for (int j = 0; j < width; j++)
                {
                    cells[width + j] = Fx.Err(CalcError.Num);
                }

                cells[2 * width] = Fx.Num(fit.R2);
                cells[(2 * width) + 1] = Fx.Err(CalcError.Num);
                cells[3 * width] = Fx.Err(CalcError.Num);
                cells[(3 * width) + 1] = Fx.Num(0);
                cells[4 * width] = Fx.Num(fit.SsReg);
                cells[(4 * width) + 1] = Fx.Num(fit.SsResid);
            }
            else
            {
                for (int j = 0; j < k; j++)
                {
                    cells[width + j] = Fx.Num(fit.StdErrors[k - 1 - j]);
                }

                cells[width + k] = constant ? Fx.Num(fit.StdErrors[k]) : Fx.Err(CalcError.NA);
                cells[2 * width] = Fx.Num(fit.R2);
                cells[(2 * width) + 1] = Fx.Num(fit.StdErrY);
                cells[3 * width] = Fx.Num(fit.F);
                cells[(3 * width) + 1] = Fx.Num(fit.Df);
                cells[4 * width] = Fx.Num(fit.SsReg);
                cells[(4 * width) + 1] = Fx.Num(fit.SsResid);
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, width, cells));
    }

    private static CalcValue Trend(Evaluator ev, ReadOnlySpan<CalcValue> a, bool log)
    {
        if (ReadModel(ev, a, out double[] y, out double[][] cols, out bool yIsColumn, out int n) is { } err)
        {
            return err;
        }

        bool constant = true;
        if (a.Length > 3 && !a[3].IsMissing)
        {
            if (CalcConvert.ToBoolean(a[3], out constant) is var ce && ce != CalcError.None)
            {
                return Fx.Err(ce);
            }
        }

        if (log)
        {
            y = (double[])y.Clone();
            for (int i = 0; i < y.Length; i++)
            {
                if (y[i] <= 0)
                {
                    return Fx.Err(CalcError.Num);
                }

                y[i] = SpecialMath.Ln(y[i]);
            }
        }

        if (n == 0)
        {
            return Fx.Err(CalcError.Value);
        }

        Fit fit = Regress(cols, y, constant);
        int k = cols.Length;

        // The points to predict: new_x, or the known x's themselves.
        CalcArray? nx = null;
        bool newGiven = a.Length > 2 && !a[2].IsMissing;
        if (newGiven)
        {
            nx = Fx.AsArray(ev, a[2], out CalcValue e);
            if (nx is null)
            {
                return e;
            }
        }

        double Predict(double[] x)
        {
            double v = constant ? fit.Coefficients[k] : 0;
            for (int j = 0; j < k; j++)
            {
                v += fit.Coefficients[j] * x[j];
            }

            return log ? SpecialMath.Exp(v) : v;
        }

        if (!newGiven)
        {
            CalcValue[] same = ev.NewCells(yIsColumn ? n : 1, yIsColumn ? 1 : n);
            var pt = new double[k];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    pt[j] = cols[j][i];
                }

                same[i] = Fx.Num(Predict(pt));
            }

            return CalcValue.FromArray(new CalcArray(yIsColumn ? n : 1, yIsColumn ? 1 : n, same));
        }

        CalcArray nxa = nx!;
        if (k == 1)
        {
            CalcValue[] cells1 = ev.NewCells(nxa.Rows, nxa.Columns);
            var pt = new double[1];
            for (int r = 0; r < nxa.Rows; r++)
            {
                for (int c = 0; c < nxa.Columns; c++)
                {
                    CalcValue v = nxa[r, c];
                    if (v.IsError)
                    {
                        return v;
                    }

                    if (v.Kind != ValueKind.Number)
                    {
                        return Fx.Err(CalcError.Value);
                    }

                    pt[0] = v.Number;
                    cells1[(r * nxa.Columns) + c] = Fx.Num(Predict(pt));
                }
            }

            return CalcValue.FromArray(new CalcArray(nxa.Rows, nxa.Columns, cells1));
        }

        int m = yIsColumn ? nxa.Rows : nxa.Columns;
        if ((yIsColumn ? nxa.Columns : nxa.Rows) != k)
        {
            return Fx.Err(CalcError.Ref);
        }

        CalcValue[] cellsK = ev.NewCells(yIsColumn ? m : 1, yIsColumn ? 1 : m);
        var p = new double[k];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < k; j++)
            {
                CalcValue v = yIsColumn ? nxa[i, j] : nxa[j, i];
                if (v.IsError)
                {
                    return v;
                }

                if (v.Kind != ValueKind.Number)
                {
                    return Fx.Err(CalcError.Value);
                }

                p[j] = v.Number;
            }

            cellsK[i] = Fx.Num(Predict(p));
        }

        return CalcValue.FromArray(new CalcArray(yIsColumn ? m : 1, yIsColumn ? 1 : m, cellsK));
    }

    // ---- significance tests ------------------------------------------------------------------------------------------------------

    private static CalcValue ZTest(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (Stat.Sample(ev, a[0], out List<double> v) is { } err)
        {
            return err;
        }

        if (!Fx.TryNum(a[1], out double x, out CalcValue e))
        {
            return e;
        }

        int n = v.Count;
        if (n == 0)
        {
            return Fx.Err(CalcError.NA);
        }

        double mean = Stat.Mean(v);
        double sigma;
        if (a.Length == 3 && !a[2].IsMissing)
        {
            if (!Fx.TryNum(a[2], out sigma, out e))
            {
                return e;
            }

            if (sigma <= 0)
            {
                return Fx.Err(CalcError.Num);
            }
        }
        else
        {
            if (n < 2)
            {
                return Fx.Err(CalcError.Div0);
            }

            sigma = Math.Sqrt(Stat.SumSquares(v, out _) / (n - 1));
            if (sigma == 0)
            {
                return Fx.Err(CalcError.Div0);
            }
        }

        return Fx.Num(1 - SpecialMath.NormalCdf((mean - x) / (sigma / Math.Sqrt(n))));
    }

    private static CalcValue TTest(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[2], out double tails, out CalcValue e) || !Fx.TryNum(a[3], out double type, out e))
        {
            return e;
        }

        tails = Math.Truncate(tails);
        type = Math.Truncate(type);
        double t;
        double df;
        if (type == 1)
        {
            CalcArray? p = Fx.AsArray(ev, a[0], out e);
            CalcArray? q = Fx.AsArray(ev, a[1], out e);
            if (p is null || q is null)
            {
                return e;
            }

            if (p.Count != q.Count)
            {
                return Fx.Err(CalcError.NA);
            }

            if (Stat.Pairs(ev, a[0], a[1], out List<double> xs, out List<double> ys) is { } perr)
            {
                return perr;
            }

            var d = new List<double>(xs.Count);
            for (int i = 0; i < xs.Count; i++)
            {
                d.Add(xs[i] - ys[i]);
            }

            if (d.Count < 2)
            {
                return Fx.Err(CalcError.Div0);
            }

            double ssd = Stat.SumSquares(d, out double md);
            double sd = Math.Sqrt(ssd / (d.Count - 1));
            if (sd == 0)
            {
                return Fx.Err(CalcError.Div0);
            }

            t = md / (sd / Math.Sqrt(d.Count));
            df = d.Count - 1;
        }
        else
        {
            if (Stat.Sample(ev, a[0], out List<double> u) is { } e1)
            {
                return e1;
            }

            if (Stat.Sample(ev, a[1], out List<double> w) is { } e2)
            {
                return e2;
            }

            if (type is not (2 or 3))
            {
                return Fx.Err(CalcError.Num);
            }

            if (u.Count < 2 || w.Count < 2)
            {
                return Fx.Err(CalcError.Div0);
            }

            double s1 = Stat.SumSquares(u, out double m1);
            double s2 = Stat.SumSquares(w, out double m2);
            double v1 = s1 / (u.Count - 1);
            double v2 = s2 / (w.Count - 1);
            if (type == 2)
            {
                df = u.Count + w.Count - 2;
                double pooled = (s1 + s2) / df;
                if (pooled == 0)
                {
                    return Fx.Err(CalcError.Div0);
                }

                t = (m1 - m2) / Math.Sqrt(pooled * ((1.0 / u.Count) + (1.0 / w.Count)));
            }
            else
            {
                double qa = v1 / u.Count;
                double qb = v2 / w.Count;
                if (qa + qb == 0)
                {
                    return Fx.Err(CalcError.Div0);
                }

                t = (m1 - m2) / Math.Sqrt(qa + qb);
                df = (qa + qb) * (qa + qb) / ((qa * qa / (u.Count - 1)) + (qb * qb / (w.Count - 1)));
            }
        }

        if (tails is not (1 or 2))
        {
            return Fx.Err(CalcError.Num);
        }

        SpecialMath.StudentTail(Math.Abs(t), df, out _, out double upper);
        return Fx.Num(Math.Min(1, tails * upper));
    }

    private static CalcValue FTest(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (Stat.Sample(ev, a[0], out List<double> u) is { } e1)
        {
            return e1;
        }

        if (Stat.Sample(ev, a[1], out List<double> w) is { } e2)
        {
            return e2;
        }

        if (u.Count < 2 || w.Count < 2)
        {
            return Fx.Err(CalcError.Div0);
        }

        double v1 = Stat.SumSquares(u, out _) / (u.Count - 1);
        double v2 = Stat.SumSquares(w, out _) / (w.Count - 1);
        if (v1 == 0 || v2 == 0)
        {
            return Fx.Err(CalcError.Div0);
        }

        SpecialMath.FDist(v1 / v2, u.Count - 1, w.Count - 1, out double lower, out double upper);
        return Fx.Num(Math.Min(1, 2 * Math.Min(lower, upper)));
    }

    private static CalcValue ChiTest(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcArray? act = Fx.AsArray(ev, a[0], out CalcValue e);
        if (act is null)
        {
            return e;
        }

        CalcArray? exp = Fx.AsArray(ev, a[1], out e);
        if (exp is null)
        {
            return e;
        }

        if (act.Rows != exp.Rows || act.Columns != exp.Columns)
        {
            return Fx.Err(CalcError.NA);
        }

        double stat = 0;
        int used = 0;
        for (int r = 0; r < act.Rows; r++)
        {
            for (int c = 0; c < act.Columns; c++)
            {
                CalcValue x = act[r, c];
                CalcValue y = exp[r, c];
                if (x.IsError)
                {
                    return x;
                }

                if (y.IsError)
                {
                    return y;
                }

                if (x.Kind != ValueKind.Number || y.Kind != ValueKind.Number)
                {
                    continue;
                }

                if (y.Number == 0)
                {
                    return Fx.Err(CalcError.Div0);
                }

                double d = x.Number - y.Number;
                stat += d * d / y.Number;
                used++;
            }
        }

        if (used == 0)
        {
            return Fx.Err(CalcError.Div0);
        }

        double df = act.Rows > 1 && act.Columns > 1 ? (act.Rows - 1.0) * (act.Columns - 1.0) : Math.Max(act.Rows, act.Columns) - 1.0;
        if (df < 1)
        {
            return Fx.Err(CalcError.Div0);
        }

        SpecialMath.ChiSquare(stat, df, out _, out double upper);
        return Fx.Num(upper);
    }
}
