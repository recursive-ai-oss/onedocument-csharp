namespace RecursiveAI.OneDocument.Layout;

/// <summary>How a table or cell states its preferred width.</summary>
internal enum WidthKind
{
    Auto,
    Points,
    Percent,
}

internal readonly record struct PreferredWidth(WidthKind Kind, double Value)
{
    public static PreferredWidth Auto => new(WidthKind.Auto, 0);

    // Points for a fixed width, percent (0 to 100) for a relative one.
    public double? Resolve(double reference) => Kind switch
    {
        WidthKind.Points => Value,
        WidthKind.Percent => reference * Value / 100.0,
        _ => null,
    };
}

/// <summary>One logical cell as the width algorithm sees it: where it sits, how it prefers to be sized and what its content needs.</summary>
/// <param name="Column">First grid column.</param>
/// <param name="Span">Number of grid columns, 1 or more.</param>
/// <param name="Min">Narrowest width in points that holds the content (the longest unbreakable stretch plus padding).</param>
/// <param name="Max">Width in points that holds the content on its longest line without wrapping, plus padding.</param>
/// <param name="Preferred">The cell's preferred width.</param>
internal readonly record struct CellWidthSpec(int Column, int Span, double Min, double Max, PreferredWidth Preferred);

// The column width algorithm for fixed and autofit tables. ECMA-376 Part 1 §17.4.53 (tblLayout) says only that a fixed table is sized
// from preferred widths and an autofit one from its contents; the steps below are the usual automatic-table-layout reading of that, in
// the order Word's behaviour suggests, and are listed as open points in docs/layout-tables.md.
internal static class TableWidths
{
    /// <summary>Computes the grid column widths in points.</summary>
    /// <param name="fixedLayout">Whether <c>w:tblLayout</c> is fixed.</param>
    /// <param name="available">The width the table may use: the text width, or the inner width of the cell it sits in.</param>
    /// <param name="tableWidth">The table's preferred width (<c>w:tblW</c>).</param>
    /// <param name="grid">The <c>w:tblGrid</c> widths in points; 0 for a column without one.</param>
    /// <param name="cells">The logical cells.</param>
    public static double[] Solve(bool fixedLayout, double available, PreferredWidth tableWidth, double[] grid, IReadOnlyList<CellWidthSpec> cells)
    {
        int n = grid.Length;
        if (n == 0)
        {
            return [];
        }

        double? specified = tableWidth.Resolve(available);
        return fixedLayout ? Fixed(available, specified, grid, cells) : Autofit(available, specified, grid, cells);
    }

    // Fixed: the preferred widths of the cells (first one found per column) decide; the grid fills the columns no cell states; a table
    // that states only a width and has no cell widths scales its grid to that width. Contents never change the result.
    private static double[] Fixed(double available, double? specified, double[] grid, IReadOnlyList<CellWidthSpec> cells)
    {
        int n = grid.Length;
        double reference = specified ?? available;
        var w = new double[n];
        var set = new bool[n];
        bool anyCellWidth = false;
        foreach (CellWidthSpec c in cells)
        {
            if (c.Span == 1 && c.Preferred.Resolve(reference) is { } p && !set[c.Column])
            {
                w[c.Column] = p;
                set[c.Column] = true;
                anyCellWidth = true;
            }
        }

        // A spanning cell states the width of its whole span: used when none of its columns has its own.
        foreach (CellWidthSpec c in cells)
        {
            if (c.Span > 1 && c.Preferred.Resolve(reference) is { } p && Enumerable.Range(c.Column, c.Span).All(i => !set[i]))
            {
                double gridSum = Enumerable.Range(c.Column, c.Span).Sum(i => grid[i]);
                for (int i = c.Column; i < c.Column + c.Span; i++)
                {
                    w[i] = gridSum > 0 ? p * grid[i] / gridSum : p / c.Span;
                    set[i] = true;
                }

                anyCellWidth = true;
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (!set[i])
            {
                w[i] = grid[i];
            }
        }

        double total = w.Sum();
        if (!anyCellWidth && specified is { } target && total > 0 && Math.Abs(total - target) > 0.01)
        {
            double k = target / total;
            for (int i = 0; i < n; i++)
            {
                w[i] *= k;
            }
        }
        else if (total <= 0)
        {
            double each = (specified ?? available) / n;
            Array.Fill(w, each);
        }

        return w;
    }

    private static double[] Autofit(double available, double? specified, double[] grid, IReadOnlyList<CellWidthSpec> cells)
    {
        int n = grid.Length;
        double reference = specified ?? available;
        var min = new double[n];
        var max = new double[n];
        var pref = new double?[n];

        foreach (CellWidthSpec c in cells.Where(static c => c.Span == 1))
        {
            min[c.Column] = Math.Max(min[c.Column], c.Min);
            max[c.Column] = Math.Max(max[c.Column], c.Max);
            if (c.Preferred.Resolve(reference) is { } p)
            {
                pref[c.Column] = Math.Max(pref[c.Column] ?? 0, p);
            }
        }

        // A spanning cell raises the totals of its columns when they fall short; the extra goes to the columns in proportion to their
        // current maximum (equally when all are zero).
        foreach (CellWidthSpec c in cells.Where(static c => c.Span > 1))
        {
            Spread(min, c.Column, c.Span, c.Min, max);
            Spread(max, c.Column, c.Span, c.Max, max);
            if (c.Preferred.Resolve(reference) is { } p)
            {
                double have = 0;
                for (int i = c.Column; i < c.Column + c.Span; i++)
                {
                    have += pref[i] ?? 0;
                }

                if (have < p)
                {
                    var open = Enumerable.Range(c.Column, c.Span).Where(i => pref[i] is null).ToList();
                    if (open.Count == 0)
                    {
                        open = [.. Enumerable.Range(c.Column, c.Span)];
                    }

                    foreach (int i in open)
                    {
                        pref[i] = (pref[i] ?? 0) + ((p - have) / open.Count);
                    }
                }
            }
        }

        // A column nobody covers keeps its grid width.
        var target = new double[n];
        for (int i = 0; i < n; i++)
        {
            max[i] = Math.Max(max[i], min[i]);
            if (pref[i] is { } p)
            {
                target[i] = Math.Max(p, min[i]);
            }
            else if (max[i] == 0 && min[i] == 0)
            {
                target[i] = grid[i];
            }
            else
            {
                target[i] = max[i];
            }
        }

        double minSum = min.Sum();
        double targetSum = target.Sum();
        var w = new double[n];
        double limit = specified ?? available;
        if (minSum >= limit)
        {
            // Nothing can shrink further; the table is wider than its space.
            Array.Copy(min, w, n);
            return w;
        }

        Array.Copy(target, w, n);
        if (targetSum > limit)
        {
            // Too wide: columns that state no width give up room first (down to their minimum), then the ones that do.
            double need = targetSum - limit;
            need -= Shrink(w, min, pref, need, wantPreferred: false);
            if (need > 1e-9)
            {
                Shrink(w, min, pref, need, wantPreferred: true);
            }
        }
        else if (specified is not null && targetSum < limit)
        {
            // A stated table width larger than the contents need: the extra goes to the columns that state no width, in proportion to
            // their size; when every column states one, to all of them.
            double extra = limit - targetSum;
            bool anyAuto = pref.Any(static p => p is null);
            double basis = 0;
            for (int i = 0; i < n; i++)
            {
                if (!anyAuto || pref[i] is null)
                {
                    basis += target[i];
                }
            }

            int share = anyAuto ? pref.Count(static p => p is null) : n;
            for (int i = 0; i < n; i++)
            {
                if (!anyAuto || pref[i] is null)
                {
                    w[i] += basis > 0 ? extra * target[i] / basis : extra / share;
                }
            }
        }

        return w;
    }

    // Takes up to <paramref name="need"/> points off the chosen group of columns, each down to its minimum, in proportion to how much
    // it has above that minimum. Returns what was taken.
    private static double Shrink(double[] w, double[] min, double?[] pref, double need, bool wantPreferred)
    {
        double capacity = 0;
        for (int i = 0; i < w.Length; i++)
        {
            if ((pref[i] is not null) == wantPreferred)
            {
                capacity += w[i] - min[i];
            }
        }

        if (capacity <= 0)
        {
            return 0;
        }

        double take = Math.Min(need, capacity);
        for (int i = 0; i < w.Length; i++)
        {
            if ((pref[i] is not null) == wantPreferred)
            {
                w[i] -= (w[i] - min[i]) * take / capacity;
            }
        }

        return take;
    }

    private static void Spread(double[] values, int column, int span, double needed, double[] weights)
    {
        double have = 0;
        double weight = 0;
        for (int i = column; i < column + span; i++)
        {
            have += values[i];
            weight += weights[i];
        }

        if (have >= needed)
        {
            return;
        }

        double extra = needed - have;
        for (int i = column; i < column + span; i++)
        {
            values[i] += weight > 0 ? extra * weights[i] / weight : extra / span;
        }
    }
}
