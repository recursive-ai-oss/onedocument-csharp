using RecursiveAI.OneOffice.Calc;

namespace RecursiveAI.OneOffice.Spreadsheets.Calc;

/// <summary>Settings for one <see cref="Workbook.Recalculate"/> call. Every property has a default, so <c>null</c> options mean "calculate with all the cores and the shipped functions".</summary>
public sealed class RecalcOptions
{
    /// <summary>Gets the most threads used for independent formulas. 0 or less (the default) means one per processor; 1 calculates on the calling thread only. The results do not depend on this number.</summary>
    public int MaxDegreeOfParallelism { get; init; }

    /// <summary>Gets the functions the formulas may call. Null (the default) means the shipped set. Add custom functions to a registry created with <see cref="FunctionRegistry.CreateDefault"/>.</summary>
    public FunctionRegistry? Functions { get; init; }

    /// <summary>Gets a value indicating whether every formula is calculated even when nothing it reads has changed. False (the default) calculates what an edit made dirty, plus the volatile formulas.</summary>
    public bool Full { get; init; }

    /// <summary>Gets the smallest number of ready formulas worth handing to several threads at once. Below it a level is calculated on the calling thread. Default 256.</summary>
    public int ParallelThreshold { get; init; } = 256;
}

/// <summary>What one recalculation did.</summary>
public sealed class RecalcResult
{
    internal RecalcResult()
    {
    }

    /// <summary>Gets the number of formula cells in the workbook.</summary>
    public int Formulas { get; internal set; }

    /// <summary>Gets the number of formulas that were evaluated (a formula evaluated twice because a spill changed what it reads counts twice).</summary>
    public int Evaluated { get; internal set; }

    /// <summary>Gets the number of dependency levels processed.</summary>
    public int Levels { get; internal set; }

    /// <summary>Gets the number of passes: 1 normally, more when a spill changed cells that other formulas read.</summary>
    public int Passes { get; internal set; }

    /// <summary>Gets the number of formula cells that are part of a circular reference. They read as 0, as in Excel without iterative calculation.</summary>
    public int CircularCells { get; internal set; }

    /// <summary>Gets the number of dynamic-array formulas that ended in <c>#SPILL!</c>.</summary>
    public int SpillErrors { get; internal set; }

    /// <summary>Gets a value indicating whether the dependency graph was (re)built by this call.</summary>
    public bool GraphBuilt { get; internal set; }

    /// <summary>Gets a value indicating whether every formula was evaluated.</summary>
    public bool WasFull { get; internal set; }
}
