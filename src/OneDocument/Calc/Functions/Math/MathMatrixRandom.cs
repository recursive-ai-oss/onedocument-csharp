using System;

namespace RecursiveAI.OneDocument.Calc;

internal static partial class MathFunctions
{
    private static void RegisterMatrix(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("MMULT", 2, 2, AA, MMult));
        r.Add(FunctionDefinition.Create("MDETERM", 1, 1, A, MDeterm));
        r.Add(FunctionDefinition.Create("MINVERSE", 1, 1, A, MInverse));
        r.Add(FunctionDefinition.Create("MUNIT", 1, 1, V, MUnit));
    }

    // Reads a numeric matrix; a blank, text or boolean entry makes the whole call #VALUE!, an error entry propagates.
    private static double[,]? ReadMatrix(Evaluator ev, in CalcValue arg, bool square, out CalcValue error)
    {
        CalcArray? a = Fx.AsArray(ev, arg, out error);
        if (a is null)
        {
            return null;
        }

        if (square && a.Rows != a.Columns)
        {
            error = Fx.Err(CalcError.Value);
            return null;
        }

        var m = new double[a.Rows, a.Columns];
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                CalcValue v = a[r, c];
                if (v.IsError)
                {
                    error = v;
                    return null;
                }

                if (v.Kind != ValueKind.Number)
                {
                    error = Fx.Err(CalcError.Value);
                    return null;
                }

                m[r, c] = v.Number;
            }
        }

        return m;
    }

    private static CalcValue FromMatrix(Evaluator ev, double[,] m)
    {
        int rows = m.GetLength(0);
        int cols = m.GetLength(1);
        CalcValue[] cells = ev.NewCells(rows, cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double v = m[r, c];
                if (!double.IsFinite(v))
                {
                    return Fx.Err(CalcError.Num);
                }

                cells[(r * cols) + c] = CalcValue.FromNumber(v == 0 ? 0 : v);
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    private static CalcValue MMult(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        double[,]? x = ReadMatrix(ev, args[0], false, out CalcValue e);
        if (x is null)
        {
            return e;
        }

        double[,]? y = ReadMatrix(ev, args[1], false, out e);
        if (y is null)
        {
            return e;
        }

        if (x.GetLength(1) != y.GetLength(0))
        {
            return Fx.Err(CalcError.Value);
        }

        int n = x.GetLength(0);
        int m = y.GetLength(1);
        int k = x.GetLength(1);
        var p = new double[n, m];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int t = 0; t < k; t++)
                {
                    s += x[i, t] * y[t, j];
                }

                p[i, j] = s;
            }
        }

        return FromMatrix(ev, p);
    }

    // LU decomposition with partial pivoting; returns the determinant and, when asked, the inverse. A pivot that is zero relative to the matrix scale means singular.
    private static bool Invert(double[,] src, out double det, out double[,] inv)
    {
        int n = src.GetLength(0);
        var a = (double[,])src.Clone();
        inv = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            inv[i, i] = 1;
        }

        double scale = 0;
        foreach (double v in a)
        {
            scale = Math.Max(scale, Math.Abs(v));
        }

        det = 1;
        bool singular = false;
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++)
            {
                if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c]))
                {
                    piv = r;
                }
            }

            if (Math.Abs(a[piv, c]) <= scale * 1e-15 * n || a[piv, c] == 0)
            {
                det = 0;
                singular = true;
                break;
            }

            if (piv != c)
            {
                det = -det;
                for (int k = 0; k < n; k++)
                {
                    (a[c, k], a[piv, k]) = (a[piv, k], a[c, k]);
                    (inv[c, k], inv[piv, k]) = (inv[piv, k], inv[c, k]);
                }
            }

            double d = a[c, c];
            det *= d;
            for (int k = 0; k < n; k++)
            {
                a[c, k] /= d;
                inv[c, k] /= d;
            }

            for (int r = 0; r < n; r++)
            {
                if (r == c)
                {
                    continue;
                }

                double f = a[r, c];
                if (f == 0)
                {
                    continue;
                }

                for (int k = 0; k < n; k++)
                {
                    a[r, k] -= f * a[c, k];
                    inv[r, k] -= f * inv[c, k];
                }
            }
        }

        return !singular;
    }

    private static CalcValue MDeterm(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        double[,]? m = ReadMatrix(ev, args[0], true, out CalcValue e);
        if (m is null)
        {
            return e;
        }

        Invert(m, out double det, out _);
        return Fx.Num(det);
    }

    private static CalcValue MInverse(Evaluator ev, ReadOnlySpan<CalcValue> args)
    {
        double[,]? m = ReadMatrix(ev, args[0], true, out CalcValue e);
        if (m is null)
        {
            return e;
        }

        return Invert(m, out _, out double[,] inv) ? FromMatrix(ev, inv) : Fx.Err(CalcError.Num);
    }

    private static CalcValue MUnit(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryInt(a[0], out double n, out CalcValue e))
        {
            return e;
        }

        if (n < 1)
        {
            return Fx.Err(CalcError.Value);
        }

        if (n > 5000)
        {
            return Fx.Err(CalcError.Num);
        }

        int k = (int)n;
        CalcValue[] cells = ev.NewCells(k, k);
        for (int i = 0; i < k; i++)
        {
            for (int j = 0; j < k; j++)
            {
                cells[(i * k) + j] = CalcValue.FromNumber(i == j ? 1 : 0);
            }
        }

        return CalcValue.FromArray(new CalcArray(k, k, cells));
    }

    // ---- random numbers -------------------------------------------------------------------------------------------------------

    private static void RegisterRandom(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("RAND", 0, 0, V, (ev, _) => CalcValue.FromNumber(ev.NextRandom()), FunctionTraits.Volatile));
        r.Add(FunctionDefinition.Create("RANDBETWEEN", 2, 2, V, RandBetween, FunctionTraits.Volatile));
    }

    private static CalcValue RandBetween(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double lo, out CalcValue e) || !Fx.TryNum(a[1], out double hi, out e))
        {
            return e;
        }

        lo = Math.Ceiling(lo);
        hi = Math.Floor(hi);
        if (lo > hi)
        {
            return Fx.Err(CalcError.Num);
        }

        return CalcValue.FromNumber(lo + Math.Floor(ev.NextRandom() * (hi - lo + 1)));
    }
}
