using RecursiveAI.OneDocument.Spreadsheets.Pivots;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    private PivotCacheCollection? _pivotCaches;

    /// <summary>
    /// Gets the pivot caches (ECMA-376 Part 1 §18.10.1.64): the data behind the workbook's pivot tables. They are read when first used. Untouched cache and
    /// table parts are written back byte-identical.
    /// </summary>
    public PivotCacheCollection PivotCaches => _pivotCaches ??= new PivotCacheCollection(this);

    /// <summary>
    /// Refreshes every pivot table in the workbook: each cache is rebuilt once from its worksheet or table source, then the cells and the definition of
    /// every table that uses it. Caches with other sources (external, OLAP, the Data Model, consolidation) are kept as stored and reported as
    /// <see cref="PivotRefreshStatus.NotSupported"/>. Nothing else recalculates.
    /// </summary>
    /// <param name="options">Options; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels before anything is written to a cache; a cancelled refresh leaves that cache and its tables as they were.</param>
    /// <returns>One outcome per pivot table.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public PivotRefreshResult RefreshPivots(PivotRefreshOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new PivotRefreshOptions();
        var outcomes = new List<PivotRefreshOutcome>();
        var done = new HashSet<int>();
        foreach (Worksheet s in Sheets)
        {
            if (s.Kind != SheetKind.Worksheet)
            {
                continue;
            }

            foreach (PivotTable t in s.PivotTables)
            {
                PivotCache? c = t.Cache;
                if (c is not null && !done.Add(c.CacheId))
                {
                    continue;
                }

                outcomes.AddRange(PivotRefresher.RefreshCache(this, c, [t], options, cancellationToken).Outcomes);
            }
        }

        return new PivotRefreshResult(outcomes);
    }
}

public sealed partial class Worksheet
{
    private PivotTableCollection? _pivotTables;

    /// <summary>Gets the pivot tables on the sheet (ECMA-376 Part 1 §18.10.1.73). The definition parts are read when the collection is first enumerated.</summary>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    public PivotTableCollection PivotTables
    {
        get
        {
            if (Kind != SheetKind.Worksheet)
            {
                throw new NotSupportedException("Only a worksheet has pivot tables.");
            }

            return _pivotTables ??= new PivotTableCollection(this);
        }
    }

    internal bool PivotTablesModified => _pivotTables?.IsModified ?? false;

    internal void CommitPivotTables() => _pivotTables?.Commit();
}
