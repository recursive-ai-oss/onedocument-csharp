using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

/// <summary>How a Goal Seek ended.</summary>
public enum GoalSeekStatus
{
    /// <summary>The formula cell came within the maximum change of the target value.</summary>
    Found = 0,

    /// <summary>The iterations ran out, or the formula stopped being a number on the way, without reaching the target.</summary>
    NotFound = 1,

    /// <summary>The cell to set holds no formula, so changing another cell cannot move it.</summary>
    SetCellNotFormula = 2,

    /// <summary>The cell to change holds a formula; it must hold a constant.</summary>
    ChangingCellIsFormula = 3,

    /// <summary>The cell to set does not show a number for the value the changing cell has now.</summary>
    SetCellNotNumeric = 4,
}

/// <summary>Settings for <see cref="Workbook.GoalSeek"/>. Every property has a default.</summary>
public sealed class GoalSeekOptions
{
    /// <summary>Gets the most formula evaluations the search makes. Zero or less (the default) takes the workbook's iteration count (<see cref="Workbook.IterationCount"/>, 100 unless the file says otherwise).</summary>
    public int MaxIterations { get; init; }

    /// <summary>Gets how close the formula cell must come to the target: the search stops when the difference is no more than this. Negative or NaN (the default) takes the workbook's maximum change (<see cref="Workbook.IterationDelta"/>, 0.001 unless the file says otherwise).</summary>
    public double MaxChange { get; init; } = double.NaN;

    /// <summary>Gets a value indicating whether a found value is written into the changing cell (and the workbook recalculated), as Excel does when the user accepts the result. Default true. When false, or when the value is not found, the workbook is left as it was.</summary>
    public bool Apply { get; init; } = true;
}

/// <summary>The outcome of <see cref="Workbook.GoalSeek"/>.</summary>
public sealed class GoalSeekResult
{
    internal GoalSeekResult(GoalSeekStatus status, int iterations, double changingValue, double setValue, double target)
    {
        Status = status;
        Iterations = iterations;
        ChangingValue = changingValue;
        SetValue = setValue;
        Target = target;
    }

    /// <summary>Gets how the search ended.</summary>
    public GoalSeekStatus Status { get; }

    /// <summary>Gets a value indicating whether the target was reached (<see cref="Status"/> is <see cref="GoalSeekStatus.Found"/>).</summary>
    public bool Found => Status == GoalSeekStatus.Found;

    /// <summary>Gets the number of times the formula was evaluated with a new value in the changing cell, not counting the one at the starting value.</summary>
    public int Iterations { get; }

    /// <summary>Gets the changing cell's value at the end: the solution when found, otherwise the closest value tried.</summary>
    public double ChangingValue { get; }

    /// <summary>Gets what the formula cell showed for <see cref="ChangingValue"/>.</summary>
    public double SetValue { get; }

    /// <summary>Gets the value that was asked for.</summary>
    public double Target { get; }
}

internal sealed partial class CalcEngine
{
    // The search is a secant iteration from the cell's present value: probe a little away, then move to where the line through the last two
    // points crosses the target. When two points give the same answer the step grows, alternating sides, until the formula responds.
    // Excel documents the search only through its two limits (maximum iterations and maximum change), which this follows.
    public GoalSeekResult GoalSeek(Cell setCell, double target, Cell changingCell, int maxIterations, double maxChange, bool apply, CancellationToken ct)
    {
        int setSheet = IndexOf(setCell.Sheet);
        int changeSheet = IndexOf(changingCell.Sheet);
        (int, int, int) set = (setSheet, setCell.Row, setCell.Column);
        (int, int, int) change = (changeSheet, changingCell.Row, changingCell.Column);
        CalcGraph g = _graph!;
        double start = changingCell.Value.Kind == CellValueKind.Number ? changingCell.Value.Number : 0;
        if (g.Find(changeSheet, changingCell.Row, changingCell.Column) >= 0)
        {
            return new GoalSeekResult(GoalSeekStatus.ChangingCellIsFormula, 0, start, double.NaN, target);
        }

        if (g.Find(setSheet, setCell.Row, setCell.Column) < 0)
        {
            return new GoalSeekResult(GoalSeekStatus.SetCellNotFormula, 0, start, double.NaN, target);
        }

        Cone cone = BuildCone([change]);
        CalcWorker worker = TemporaryWorker();
        int iterations = 0;
        double bestX = start;
        double bestY = double.NaN;

        // Returns false when the formula does not give a number.
        bool Probe(double x, out double y)
        {
            CalcValue v = EvaluateWith(cone, [(change, CalcValue.FromNumber(x))], set, worker);
            y = v.Kind == ValueKind.Number ? v.Number : v.Kind == ValueKind.Blank ? 0 : double.NaN;
            return !double.IsNaN(y) && double.IsFinite(y);
        }

        GoalSeekResult Finish(GoalSeekStatus status)
        {
            if (status == GoalSeekStatus.Found && apply)
            {
                changingCell.SetNumber(bestX);
            }

            return new GoalSeekResult(status, iterations, bestX, bestY, target);
        }

        if (!Probe(start, out double y0))
        {
            return new GoalSeekResult(GoalSeekStatus.SetCellNotNumeric, 0, start, double.NaN, target);
        }

        bestY = y0;
        if (Math.Abs(y0 - target) <= maxChange)
        {
            return Finish(GoalSeekStatus.Found);
        }

        double x0 = start;
        double f0 = y0 - target;
        double h = Math.Max(Math.Abs(start) * 1e-3, 1e-3);
        double x1 = x0 + h;
        int widen = 0;
        while (iterations < maxIterations)
        {
            ct.ThrowIfCancellationRequested();
            iterations++;
            if (!Probe(x1, out double y1))
            {
                return Finish(GoalSeekStatus.NotFound);
            }

            double f1 = y1 - target;
            if (Math.Abs(f1) < Math.Abs(bestY - target))
            {
                bestX = x1;
                bestY = y1;
            }

            if (Math.Abs(f1) <= maxChange)
            {
                bestX = x1;
                bestY = y1;
                return Finish(GoalSeekStatus.Found);
            }

            double slope = (f1 - f0) / (x1 - x0);
            double x2;
            if (slope == 0 || !double.IsFinite(slope))
            {
                widen++;
                x2 = x1 + (widen % 2 == 1 ? 1 : -1) * h * widen * 2;
            }
            else
            {
                x2 = x1 - (f1 / slope);
            }

            if (!double.IsFinite(x2))
            {
                return Finish(GoalSeekStatus.NotFound);
            }

            x0 = x1;
            f0 = f1;
            x1 = x2;
        }

        return Finish(GoalSeekStatus.NotFound);
    }

    private int IndexOf(Worksheet sheet)
    {
        for (int i = 0; i < Book.Sheets.Length; i++)
        {
            if (ReferenceEquals(Book.Sheets[i], sheet))
            {
                return i;
            }
        }

        throw new ArgumentException("The cell belongs to another workbook.");
    }
}
