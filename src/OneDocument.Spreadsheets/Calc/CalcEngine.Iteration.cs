using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

// Iterative calculation: circular chains are calculated repeatedly until they settle (ECMA-376 Part 1 §18.2.2, calcPr iterate,
// iterateCount, iterateDelta). Without it the engine cuts a cycle to 0 (see BreakCycles).
internal sealed partial class CalcEngine
{
    private bool _iterate;
    private int _iterationLimit = 100;
    private double _iterationDelta = 0.001;
    private CancellationToken _cancel;

    private void ReadIterationSettings()
    {
        _iterate = _wb.IsIterativeCalculation;
        _iterationLimit = _wb.IterationCount;
        _iterationDelta = _wb.IterationDelta;
    }

    /// <summary>
    /// Calculates the formulas of one cycle round after round, in cell order, each round reading what the previous formulas of the same round
    /// just wrote, starting from the values the cells already hold. Stops when a round moves no number by more than the maximum change, or
    /// after the maximum number of rounds.
    /// </summary>
    private void IterateCycle(List<int> members, CalcWorker worker)
    {
        // Reads of cycle members must see the stored values, so the members are not "waiting" any more.
        foreach (int n in members)
        {
            _state[n] = Computed;
        }

        int rounds = 0;
        bool settled = false;
        while (rounds < _iterationLimit && !settled)
        {
            _cancel.ThrowIfCancellationRequested();
            rounds++;
            double largest = 0;
            bool moved = false;
            foreach (int n in members)
            {
                CalcValue before = StoredValue(n);
                CalcValue v = EvalNode(worker, n);
                _evaluated++;
                if (v.IsMissing)
                {
                    continue;
                }

                Commit(n, v);
                CalcValue after = StoredValue(n);
                if (IsNumber(before, out double a) && IsNumber(after, out double b))
                {
                    largest = Math.Max(largest, Math.Abs(b - a));
                }
                else if (!SameValue(before, after))
                {
                    moved = true;
                }
            }

            // Microsoft: iteration stops when values "change by less than" the maximum change; with a maximum change of 0 only an exact repeat stops it.
            settled = !moved && (largest < _iterationDelta || largest == 0);
        }

        _result.IterationRounds += rounds;
        _result.IteratedCells += members.Count;
        if (!settled)
        {
            _result.UnsettledCycles++;
            CalcGraph.Decode(_graph!.Keys[members[0]], out int s, out int r, out int c);
            _wb.OpenContext.Defects.Report(new Defect(DefectKind.Formula, "calc.iteration-limit", DefectSeverity.Repaired,
                "A circular reference was still changing after the maximum number of iterations; the cells keep their last values.",
                DefectLocation.InCell(Book.Sheets[s].Name, new CellAddress(r, c).ToString())));
        }
    }

    private CalcValue StoredValue(int n)
    {
        CalcGraph.Decode(_graph!.Keys[n], out int s, out int r, out int c);
        Worksheet ws = Book.Sheets[s];
        return ws.Store.TryGetCell(r, c, out CellValue v, out _) ? ToCalc(ws, v) : CalcValue.Blank;
    }

    private static bool IsNumber(in CalcValue v, out double d)
    {
        if (v.Kind == ValueKind.Number)
        {
            d = v.Number;
            return true;
        }

        d = 0;
        return v.Kind == ValueKind.Blank;
    }

    private static bool SameValue(in CalcValue a, in CalcValue b) => a.Kind == b.Kind && a.Kind switch
    {
        ValueKind.Boolean => a.Boolean == b.Boolean,
        ValueKind.Text => string.Equals(a.Text, b.Text, StringComparison.Ordinal),
        ValueKind.Error => a.Error == b.Error,
        _ => true,
    };
}
