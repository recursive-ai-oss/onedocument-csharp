using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

// What-if evaluation: answer "what would this cell show if that cell held x?" without touching the grid. Data tables and Goal Seek both
// use it. The substituted values and every formula that depends on them live in an overlay the resolver consults first, so nothing is
// written to the workbook and nothing leaks from one question to the next.
internal sealed partial class CalcEngine
{
    private Dictionary<(int Sheet, int Row, int Column), CalcValue>? _overlay;

    /// <summary>The formulas that depend, directly or through other formulas, on some input cells, in an order where each follows what it reads.</summary>
    public sealed class Cone
    {
        internal Cone(int[] order) => Order = order;

        internal int[] Order { get; }
    }

    public bool TryGetOverlay(int sheet, int row, int column, out CalcValue value)
    {
        if (_overlay is { } o && o.TryGetValue((sheet, row, column), out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Replaces the values in a block just read from the grid with the overlay's.</summary>
    public void ApplyOverlay(int sheet, in CellArea area, CalcValue[] cells)
    {
        if (_overlay is not { Count: > 0 } o)
        {
            return;
        }

        foreach (KeyValuePair<(int Sheet, int Row, int Column), CalcValue> e in o)
        {
            (int s, int r, int c) = e.Key;
            if (s == sheet && r >= area.Row1 && r <= area.Row2 && c >= area.Column1 && c <= area.Column2)
            {
                cells[((r - area.Row1) * area.Columns) + (c - area.Column1)] = e.Value;
            }
        }
    }

    /// <summary>Finds the formulas that depend on the input cells, ordered so that each comes after the ones it reads.</summary>
    public Cone BuildCone(IReadOnlyList<(int Sheet, int Row, int Column)> inputs)
    {
        CalcGraph g = _graph!;
        var members = new HashSet<int>();
        var queue = new List<int>();
        var tmp = new List<int>();
        foreach ((int s, int r, int c) in inputs)
        {
            tmp.Clear();
            g.Dependents(s, r, c, tmp);
            foreach (int d in tmp)
            {
                if (members.Add(d))
                {
                    queue.Add(d);
                }
            }
        }

        for (int head = 0; head < queue.Count; head++)
        {
            CalcGraph.Decode(g.Keys[queue[head]], out int s, out int r, out int c);
            tmp.Clear();
            g.Dependents(s, r, c, tmp);
            foreach (int d in tmp)
            {
                if (members.Add(d))
                {
                    queue.Add(d);
                }
            }
        }

        // Kahn's algorithm inside the set. A formula caught in a cycle is left out: it keeps its stored value.
        var waiting = new Dictionary<int, int>();
        foreach (int n in queue)
        {
            waiting[n] = 0;
        }

        foreach (int n in queue)
        {
            CalcGraph.Decode(g.Keys[n], out int s, out int r, out int c);
            tmp.Clear();
            g.Dependents(s, r, c, tmp);
            foreach (int d in new HashSet<int>(tmp))
            {
                if (d != n)
                {
                    waiting[d]++;
                }
            }
        }

        var ready = new List<int>();
        foreach (int n in queue)
        {
            if (waiting[n] == 0)
            {
                ready.Add(n);
            }
        }

        ready.Sort();
        for (int head = 0; head < ready.Count; head++)
        {
            int n = ready[head];
            CalcGraph.Decode(g.Keys[n], out int s, out int r, out int c);
            tmp.Clear();
            g.Dependents(s, r, c, tmp);
            foreach (int d in new HashSet<int>(tmp))
            {
                if (d != n && --waiting[d] == 0)
                {
                    ready.Add(d);
                }
            }
        }

        return new Cone([.. ready]);
    }

    /// <summary>
    /// Calculates the cone with the input cells holding the given values and reads one cell: what the formulas would show, with the grid
    /// left exactly as it was. Must be called between calculations, on one thread.
    /// </summary>
    public CalcValue EvaluateWith(Cone cone, IReadOnlyList<((int Sheet, int Row, int Column) Cell, CalcValue Value)> inputs, (int Sheet, int Row, int Column) target, CalcWorker worker)
    {
        _overlay = [];
        CalcWorker? previous = CalcWorker.Current;
        CalcWorker.Current = worker;
        try
        {
            foreach (((int s, int r, int c), CalcValue v) in inputs)
            {
                _overlay[(s, r, c)] = v;
            }

            CalcGraph g = _graph!;
            foreach (int n in cone.Order)
            {
                CalcValue v = EvalNode(worker, n);
                if (v.IsMissing)
                {
                    continue;
                }

                CalcGraph.Decode(g.Keys[n], out int s, out int r, out int c);
                _overlay[(s, r, c)] = Scalar(v);
            }

            if (_overlay.TryGetValue(target, out CalcValue answer))
            {
                return answer;
            }

            Worksheet ws = Book.Sheets[target.Sheet];
            return ws.Store.TryGetCell(target.Row, target.Column, out CellValue stored, out _) ? ToCalc(ws, stored) : CalcValue.Blank;
        }
        finally
        {
            CalcWorker.Current = previous;
            _overlay = null;
        }
    }

    private static CalcValue Scalar(in CalcValue v) => v.Kind switch
    {
        ValueKind.Array => v.Array![0, 0],
        ValueKind.Reference or ValueKind.Lambda => CalcValue.FromError(CalcError.Value),
        _ => v,
    };

    // ---- data tables ----------------------------------------------------------------------------------------------------------

    private sealed record DataTableSpec(int Sheet, CellRange Range, bool TwoD, bool Row, CellAddress? Input1, CellAddress? Input2, bool Deleted1, bool Deleted2);

    /// <summary>Calculates every data-table block and writes the results into its cells.</summary>
    private void RunDataTables(CalcWorker worker)
    {
        CalcGraph g = _graph!;
        foreach ((int sheet, int _, int _, CellFormula f) in g.DataTables)
        {
            _cancel.ThrowIfCancellationRequested();
            DataTableSpec? spec = ReadDataTable(sheet, f);
            if (spec is not null)
            {
                CalculateDataTable(spec, worker);
            }
        }
    }

    // ECMA-376 Part 1 §18.3.1.40 (f): ref is the block of results; dt2D marks a two-variable table; dtr tells whether a one-variable table
    // has its input values in a row (true) or a column; r1 is the row input cell (the first input), r2 the column input cell of a
    // two-variable table; del1/del2 mean the input cell was deleted.
    private static DataTableSpec? ReadDataTable(int sheet, CellFormula f)
    {
        if (f.Reference is null || !CellRange.TryParse(f.Reference, out CellRange range) || !range.IsValid)
        {
            return null;
        }

        bool Flag(string name) => RawAttribute(f.RawAttributes, name) is "1" or "true";
        CellAddress? Input(string name) => RawAttribute(f.RawAttributes, name) is { } text && CellAddress.TryParse(text, out CellAddress a) ? a : null;
        return new DataTableSpec(sheet, range, Flag("dt2D"), Flag("dtr"), Input("r1"), Input("r2"), Flag("del1"), Flag("del2"));
    }

    internal static string? RawAttribute(string? raw, string name)
    {
        if (raw is null)
        {
            return null;
        }

        int i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && char.IsWhiteSpace(raw[i]))
            {
                i++;
            }

            int eq = raw.IndexOf('=', i);
            if (eq < 0 || eq + 2 > raw.Length)
            {
                return null;
            }

            string key = raw[i..eq].Trim();
            char quote = raw[eq + 1];
            if (quote is not ('"' or '\''))
            {
                return null;
            }

            int end = raw.IndexOf(quote, eq + 2);
            if (end < 0)
            {
                return null;
            }

            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                return raw[(eq + 2)..end];
            }

            i = end + 1;
        }

        return null;
    }

    private void CalculateDataTable(DataTableSpec t, CalcWorker worker)
    {
        Worksheet ws = Book.Sheets[t.Sheet];
        int top = t.Range.TopLeft.Row, left = t.Range.TopLeft.Column;
        int bottom = t.Range.BottomRight.Row, right = t.Range.BottomRight.Column;
        if (top < 2 || left < 2)
        {
            return; // no room for the row and column of input values
        }

        bool broken = t.Input1 is null || t.Deleted1 || (t.TwoD && (t.Input2 is null || t.Deleted2));
        Cone? cone = null;
        if (!broken)
        {
            var inputs = new List<(int Sheet, int Row, int Column)> { (t.Sheet, t.Input1!.Value.Row, t.Input1.Value.Column) };
            if (t.TwoD)
            {
                inputs.Add((t.Sheet, t.Input2!.Value.Row, t.Input2.Value.Column));
            }

            cone = BuildCone(inputs);
        }

        CalcValue Read(int r, int c) => ws.Store.TryGetCell(r, c, out CellValue v, out _) ? ToCalc(ws, v) : CalcValue.Blank;
        for (int r = top; r <= bottom; r++)
        {
            for (int c = left; c <= right; c++)
            {
                CalcValue result;
                if (broken)
                {
                    result = CalcValue.FromError(CalcError.Ref);
                }
                else
                {
                    var values = new List<((int Sheet, int Row, int Column) Cell, CalcValue Value)>(2);
                    (int, int, int) target;
                    if (t.TwoD)
                    {
                        // The corner holds the formula; the top row feeds the row input cell, the left column the column input cell.
                        values.Add(((t.Sheet, t.Input1!.Value.Row, t.Input1.Value.Column), Read(top - 1, c)));
                        values.Add(((t.Sheet, t.Input2!.Value.Row, t.Input2.Value.Column), Read(r, left - 1)));
                        target = (t.Sheet, top - 1, left - 1);
                    }
                    else if (t.Row)
                    {
                        values.Add(((t.Sheet, t.Input1!.Value.Row, t.Input1.Value.Column), Read(top - 1, c)));
                        target = (t.Sheet, r, left - 1);
                    }
                    else
                    {
                        values.Add(((t.Sheet, t.Input1!.Value.Row, t.Input1.Value.Column), Read(r, left - 1)));
                        target = (t.Sheet, top - 1, c);
                    }

                    result = EvaluateWith(cone!, values, target, worker);
                }

                if (WriteScalar(ws, r, c, result))
                {
                    _spillChanged.Add((t.Sheet, r, c));
                }

                _result.DataTableCells++;
            }
        }
    }
}
