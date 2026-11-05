using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

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

    /// <summary>Gets the supplier of live workbooks for references to other workbooks. Null (the default) denies all access: such references read the values cached in the file, and <c>#REF!</c> where the cache has none.</summary>
    public IExternalWorkbookResolver? ExternalWorkbooks { get; init; }

    /// <summary>Gets the supplier of <c>WEBSERVICE</c> and <c>FILTERXML</c> answers. Null (the default) denies the call: the function gives Excel's offline result <c>#VALUE!</c>, and a cell that has a cached value keeps it.</summary>
    public IWebServiceResolver? WebServices { get; init; }
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

    /// <summary>Gets the number of rounds iterative calculation made, summed over the cycles (0 when iteration is off or nothing was circular).</summary>
    public int IterationRounds { get; internal set; }

    /// <summary>Gets the number of formula cells that were calculated iteratively.</summary>
    public int IteratedCells { get; internal set; }

    /// <summary>Gets the number of cycles that were still changing by more than the maximum change when the maximum number of iterations ran out. They keep their last values.</summary>
    public int UnsettledCycles { get; internal set; }

    /// <summary>Gets the number of data-table cells that were calculated.</summary>
    public int DataTableCells { get; internal set; }

    /// <summary>Gets the number of references to other workbooks that were read from a live workbook the resolver supplied.</summary>
    public int ExternalResolved { get; internal set; }

    /// <summary>Gets the number of references to other workbooks that had to be answered with <c>#REF!</c> because neither the cached values nor the resolver had the cell.</summary>
    public int ExternalMissing { get; internal set; }

    /// <summary>Gets a value indicating whether the dependency graph was (re)built by this call.</summary>
    public bool GraphBuilt { get; internal set; }

    /// <summary>Gets a value indicating whether every formula was evaluated.</summary>
    public bool WasFull { get; internal set; }
}
