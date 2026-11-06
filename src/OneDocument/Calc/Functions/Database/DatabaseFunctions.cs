using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>
/// The database functions (<c>DSUM</c>, <c>DCOUNT</c>, …): a list with a header row, a field, and a criteria block whose rows are alternatives
/// (OR) and whose cells within a row all have to match (AND).
/// </summary>
internal static class DatabaseFunctions
{
    private enum Kind
    {
        Sum,
        Average,
        Count,
        CountA,
        Get,
        Max,
        Min,
        Product,
        StdevSample,
        StdevPopulation,
        VarSample,
        VarPopulation,
    }

    public static void Register(FunctionRegistry r)
    {
        ArgKind[] k = [ArgKind.Any, ArgKind.Any, ArgKind.Any];
        Add(r, "DSUM", Kind.Sum, k);
        Add(r, "DAVERAGE", Kind.Average, k);
        Add(r, "DCOUNT", Kind.Count, k);
        Add(r, "DCOUNTA", Kind.CountA, k);
        Add(r, "DGET", Kind.Get, k);
        Add(r, "DMAX", Kind.Max, k);
        Add(r, "DMIN", Kind.Min, k);
        Add(r, "DPRODUCT", Kind.Product, k);
        Add(r, "DSTDEV", Kind.StdevSample, k);
        Add(r, "DSTDEVP", Kind.StdevPopulation, k);
        Add(r, "DVAR", Kind.VarSample, k);
        Add(r, "DVARP", Kind.VarPopulation, k);
    }

    private static void Add(FunctionRegistry r, string name, Kind kind, ArgKind[] kinds) =>
        r.Add(FunctionDefinition.Create(name, 3, 3, kinds, (ev, a) => Run(ev, a, kind)));

    // One condition of a criteria row: the database column it tests (or -1 for a computed criterion) and how to test it.
    private readonly record struct Condition(int Column, CalcCriteria? Matcher, bool Computed, CalcValue Cell);

    private static CalcValue Run(Evaluator ev, ReadOnlySpan<CalcValue> a, Kind kind)
    {
        CalcArray? db = Fx.AsArray(ev, a[0], out CalcValue e0);
        if (db is null)
        {
            return e0;
        }

        CalcArray? crit = Fx.AsArray(ev, a[2], out CalcValue e2);
        if (crit is null)
        {
            return e2;
        }

        CalcValue fieldArg = ev.Dereference(a[1]);
        if (fieldArg.IsError)
        {
            return fieldArg;
        }

        // Observed in Excel 16.0.20527: a criteria block of a single row (a header with nothing under it) or a single value is #VALUE!.
        if (db.Rows < 1 || db.Columns < 1 || crit.Rows < 2)
        {
            return Fx.Err(CalcError.Value);
        }

        // The field is a column number (1-based) or a header text, compared ignoring case.
        int column = -1;
        // Observed: DCOUNT and DCOUNTA may leave the field out, and then count the records that match.
        bool countRecords = a[1].IsMissing && (kind is Kind.Count or Kind.CountA);
        if (fieldArg.Kind == ValueKind.Array)
        {
            return Fx.Err(CalcError.Value);
        }

        if (fieldArg.Kind == ValueKind.Text)
        {
            for (int c = 0; c < db.Columns; c++)
            {
                CalcValue h = db[0, c];
                if (h.Kind == ValueKind.Text && string.Equals(h.Text, fieldArg.Text, StringComparison.OrdinalIgnoreCase))
                {
                    column = c;
                    break;
                }
            }
        }
        else if (fieldArg.Kind is ValueKind.Number or ValueKind.Boolean)
        {
            double n = Math.Truncate(fieldArg.Number);
            // Observed: TRUE names the first column, as the number 1 does.
            if (n >= 1 && n <= db.Columns)
            {
                column = (int)n - 1;
            }
        }

        if (column < 0 && !countRecords)
        {
            return Fx.Err(CalcError.Value);
        }

        // Criteria columns: a header that names a database field tests that field; any other header (or none) makes the cell a computed
        // criterion whose result must be TRUE.
        var columnOf = new int[crit.Columns];
        for (int c = 0; c < crit.Columns; c++)
        {
            columnOf[c] = -1;
            CalcValue h = crit[0, c];
            if (h.Kind == ValueKind.Text && h.Text.Length > 0)
            {
                for (int d = 0; d < db.Columns; d++)
                {
                    CalcValue dh = db[0, d];
                    if (dh.Kind == ValueKind.Text && string.Equals(dh.Text, h.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        columnOf[c] = d;
                        break;
                    }
                }
            }
        }

        var rowsOfConditions = new List<List<Condition>>();
        for (int r = 1; r < crit.Rows; r++)
        {
            var row = new List<Condition>();
            for (int c = 0; c < crit.Columns; c++)
            {
                CalcValue cell = crit[r, c];
                if (cell.IsBlank || (cell.Kind == ValueKind.Text && cell.Text.Length == 0))
                {
                    continue;
                }

                if (columnOf[c] < 0)
                {
                    row.Add(new Condition(-1, null, true, cell));
                }
                else
                {
                    row.Add(new Condition(columnOf[c], CalcCriteria.Parse(PrefixMatch(cell)), false, cell));
                }
            }

            rowsOfConditions.Add(row);
        }

        // With no criteria rows every record matches.
        bool matchAll = rowsOfConditions.Count == 0;
        var picked = new List<CalcValue>();
        for (int r = 1; r < db.Rows; r++)
        {
            bool match = matchAll;
            foreach (List<Condition> row in rowsOfConditions)
            {
                bool ok = true;
                foreach (Condition cond in row)
                {
                    if (cond.Computed)
                    {
                        if (!(cond.Cell.Kind == ValueKind.Boolean && cond.Cell.Boolean))
                        {
                            ok = false;
                            break;
                        }
                    }
                    else if (!cond.Matcher!.Matches(db[r, cond.Column]))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    match = true;
                    break;
                }
            }

            if (match)
            {
                // A record counted without a field stands for a non-empty value.
                picked.Add(column < 0 ? CalcValue.FromNumber(1) : db[r, column]);
            }
        }

        return Aggregate(kind, picked);
    }

    // Text without an operator matches the cells that begin with it ("Dav" finds "Davolio"); "=Dav" asks for an exact match.
    private static CalcValue PrefixMatch(in CalcValue criterion)
    {
        if (criterion.Kind != ValueKind.Text)
        {
            return criterion;
        }

        string t = criterion.Text;
        if (t.Length == 0 || t[0] is '<' or '>' or '=' || CalcConvert.TextToNumber(t, out _) || CalcErrors.TryParse(t, out _)
            || t.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || t.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return criterion;
        }

        return CalcValue.FromText(t + "*");
    }

    private static CalcValue Aggregate(Kind kind, List<CalcValue> values)
    {
        if (kind == Kind.Get)
        {
            return values.Count switch
            {
                0 => Fx.Err(CalcError.Value),
                1 => values[0].IsBlank ? CalcValue.FromNumber(0) : values[0],
                _ => Fx.Err(CalcError.Num),
            };
        }

        if (kind == Kind.CountA)
        {
            int count = 0;
            foreach (CalcValue v in values)
            {
                if (!v.IsBlank)
                {
                    count++;
                }
            }

            return CalcValue.FromNumber(count);
        }

        var nums = new List<double>();
        foreach (CalcValue v in values)
        {
            if (v.IsError && kind != Kind.Count)
            {
                return v;
            }

            if (v.Kind == ValueKind.Number)
            {
                nums.Add(v.Number);
            }
        }

        int n = nums.Count;
        switch (kind)
        {
            case Kind.Count:
                return CalcValue.FromNumber(n);
            case Kind.Sum:
                {
                    double s = 0;
                    foreach (double d in nums)
                    {
                        s = Evaluator.AddSnapped(s, d);
                    }

                    return Fx.Num(s);
                }

            case Kind.Average:
                {
                    if (n == 0)
                    {
                        return Fx.Err(CalcError.Div0);
                    }

                    double s = 0;
                    foreach (double d in nums)
                    {
                        s = Evaluator.AddSnapped(s, d);
                    }

                    return Fx.Num(s / n);
                }

            case Kind.Max:
                return CalcValue.FromNumber(n == 0 ? 0 : Max(nums));
            case Kind.Min:
                return CalcValue.FromNumber(n == 0 ? 0 : Min(nums));
            case Kind.Product:
                {
                    if (n == 0)
                    {
                        return CalcValue.FromNumber(0);
                    }

                    double p = 1;
                    foreach (double d in nums)
                    {
                        p *= d;
                    }

                    return Fx.Num(p);
                }

            default:
                {
                    bool sample = kind is Kind.StdevSample or Kind.VarSample;
                    if (n == 0 || (sample && n < 2))
                    {
                        return Fx.Err(CalcError.Div0);
                    }

                    double mean = 0;
                    foreach (double d in nums)
                    {
                        mean += d;
                    }

                    mean /= n;
                    double ss = 0;
                    foreach (double d in nums)
                    {
                        ss += (d - mean) * (d - mean);
                    }

                    double variance = ss / (sample ? n - 1 : n);
                    return Fx.Num(kind is Kind.StdevSample or Kind.StdevPopulation ? Math.Sqrt(variance) : variance);
                }
        }
    }

    private static double Max(List<double> v)
    {
        double m = v[0];
        foreach (double d in v)
        {
            m = Math.Max(m, d);
        }

        return m;
    }

    private static double Min(List<double> v)
    {
        double m = v[0];
        foreach (double d in v)
        {
            m = Math.Min(m, d);
        }

        return m;
    }
}
