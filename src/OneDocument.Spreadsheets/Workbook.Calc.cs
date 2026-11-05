using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    private CalcEngine? _calc;

    /// <summary>
    /// Calculates the formulas that edits made out of date, plus the volatile ones, and stores the results as the cells' cached values.
    /// The first call calculates everything. Opening a workbook never calls this; cached values stay as stored until you do.
    /// </summary>
    /// <remarks>
    /// <para>An explicit call calculates whatever the calculation mode says: a workbook set to manual calculation stays manual (the
    /// setting is not changed), and this call is the "calculate now" command.</para>
    /// <para>Edits are noticed when they go through <see cref="Cell"/> (values, formulas, clearing). A formula edit rebuilds the dependency
    /// graph on the next call (a pass over the formulas, no evaluation); a value edit costs only its dependents. A circular reference
    /// reads as 0 and is reported to the open context's defects as <c>calc.circular-reference</c>. A dynamic-array formula that cannot spill
    /// shows <c>#SPILL!</c>. If the call is cancelled, some cells hold new values and some old ones; call it again to finish.</para>
    /// </remarks>
    /// <param name="options">Threads, functions and the full-recalculation switch; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between levels of formulas.</param>
    /// <returns>What was done.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public RecalcResult Recalculate(RecalcOptions? options = null, CancellationToken cancellationToken = default)
    {
        _calc ??= new CalcEngine(this);
        return _calc.Run(options ?? new RecalcOptions(), cancellationToken);
    }

    /// <summary>Calculates every formula, whether or not anything it reads changed. Same as <see cref="Recalculate"/> with <see cref="RecalcOptions.Full"/>.</summary>
    /// <param name="options">Threads and functions; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between levels of formulas.</param>
    /// <returns>What was done.</returns>
    public RecalcResult RecalculateAll(RecalcOptions? options = null, CancellationToken cancellationToken = default) =>
        Recalculate(new RecalcOptions
        {
            Full = true,
            MaxDegreeOfParallelism = options?.MaxDegreeOfParallelism ?? 0,
            Functions = options?.Functions,
            ParallelThreshold = options?.ParallelThreshold ?? 256,
            ExternalWorkbooks = options?.ExternalWorkbooks,
            WebServices = options?.WebServices,
        }, cancellationToken);

    /// <summary>
    /// Finds the value for one cell that makes a formula cell show a target value, as Excel's Goal Seek does. The workbook is calculated first
    /// (so the search starts from consistent values); the search then changes nothing in the grid until it ends.
    /// </summary>
    /// <remarks>
    /// The search stops when the formula cell is within the maximum change of the target (<see cref="IterationDelta"/>, 0.001 by default) or
    /// after the maximum number of evaluations (<see cref="IterationCount"/>, 100 by default); <see cref="GoalSeekOptions"/> overrides both.
    /// A found value is written into the changing cell and the workbook recalculated, unless <see cref="GoalSeekOptions.Apply"/> is false.
    /// Formulas that build references at run time (<c>INDIRECT</c>, <c>OFFSET</c>) are not followed through the changing cell.
    /// </remarks>
    /// <param name="setCell">The cell with the formula whose value is wanted.</param>
    /// <param name="toValue">The value the formula cell should show.</param>
    /// <param name="changingCell">The cell holding a constant that the search may change.</param>
    /// <param name="options">Limits and whether to apply the answer; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between evaluations.</param>
    /// <returns>What the search found; a typed status, never an exception, for a search that fails.</returns>
    /// <exception cref="ArgumentException">A cell belongs to another workbook.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public GoalSeekResult GoalSeek(Cell setCell, double toValue, Cell changingCell, GoalSeekOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setCell);
        ArgumentNullException.ThrowIfNull(changingCell);
        if (!ReferenceEquals(setCell.Sheet.Workbook, this) || !ReferenceEquals(changingCell.Sheet.Workbook, this))
        {
            throw new ArgumentException("The cells must belong to this workbook.");
        }

        Recalculate(null, cancellationToken);
        int max = options is { MaxIterations: > 0 } ? options.MaxIterations : IterationCount;
        double change = options is { MaxChange: >= 0 } ? options.MaxChange : IterationDelta;
        GoalSeekResult result = _calc!.GoalSeek(setCell, toValue, changingCell, max, change, options?.Apply ?? true, cancellationToken);
        if (result.Found && (options?.Apply ?? true))
        {
            Recalculate(null, cancellationToken);
        }

        return result;
    }

    /// <summary>Gets a value indicating whether iterative calculation is on (<c>calcPr iterate</c>, ECMA-376 Part 1 §18.2.2). Off by default.</summary>
    public bool IsIterativeCalculation => CalcPr()?.GetAttribute("iterate") is "1" or "true";

    /// <summary>Gets the most passes iterative calculation (and Goal Seek) makes (<c>calcPr iterateCount</c>). Default 100.</summary>
    public int IterationCount =>
        CalcPr()?.GetAttribute("iterateCount") is { } s && int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 1 ? Math.Min(n, MaxIterationCount) : 100;

    /// <summary>Gets the change under which iterative calculation (and Goal Seek) stops (<c>calcPr iterateDelta</c>). Default 0.001.</summary>
    public double IterationDelta =>
        CalcPr()?.GetAttribute("iterateDelta") is { } s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && d >= 0 && double.IsFinite(d) ? d : 0.001;

    /// <summary>The most iterations Excel's options dialog accepts.</summary>
    internal const int MaxIterationCount = 32767;

    /// <summary>Turns iterative calculation on or off and sets its limits, writing them to the file's <c>calcPr</c>.</summary>
    /// <param name="enabled">True to let circular references iterate.</param>
    /// <param name="maxIterations">The most passes, 1 to 32767 (Excel's range). Default 100.</param>
    /// <param name="maxChange">Iteration stops once no cell on the cycle moves by more than this. Zero or more. Default 0.001.</param>
    /// <exception cref="ArgumentOutOfRangeException">A limit is outside its range.</exception>
    public void SetIterativeCalculation(bool enabled, int maxIterations = 100, double maxChange = 0.001)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIterations, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxIterations, MaxIterationCount);
        if (!(maxChange >= 0) || !double.IsFinite(maxChange))
        {
            throw new ArgumentOutOfRangeException(nameof(maxChange));
        }

        MarkupNode? calc = CalcPr();
        if (calc is null)
        {
            InsertWorkbookChild("calcPr", "<" + Pc() + "calcPr/>");
            calc = CalcPr();
        }

        MarkupNode c = calc!.Value;
        if (enabled)
        {
            c.SetAttribute("iterate", "1");
        }
        else
        {
            c.RemoveAttribute("iterate");
        }

        c.SetAttribute("iterateCount", maxIterations.ToString(System.Globalization.CultureInfo.InvariantCulture));
        c.SetAttribute("iterateDelta", maxChange.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    private const string MetadataContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheetMetadata+xml";

    /// <summary>
    /// Finds the cell-metadata index (<c>cm</c>) that marks a formula as a dynamic array, creating the metadata part when the workbook has
    /// none ([MS-XLSX] CT_DynamicArrayProperties: type <c>XLDAPR</c>, <c>fDynamic</c> true).
    /// </summary>
    internal int EnsureDynamicArrayMetadata()
    {
        foreach (Worksheet ws in Sheets)
        {
            if (ws.IsLoaded && ws.Store.FindDynamicArrayMetadataIndex() is { } existing)
            {
                return existing;
            }
        }

        string baseType = RelationshipBase + "sheetMetadata";
        var rels = new List<Relationship>(Package.GetRelationships(MainPartName));
        if (rels.Exists(r => r.Type == baseType))
        {
            throw new NotSupportedException("The workbook has a metadata part without a dynamic-array entry; adding one is not supported.");
        }

        string ns = MainNamespace;
        const string da = "http://schemas.microsoft.com/office/spreadsheetml/2017/dynamicarray";
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<metadata xmlns=\"" + ns + "\" xmlns:xda=\"" + da + "\">"
            + "<metadataTypes count=\"1\"><metadataType name=\"XLDAPR\" minSupportedVersion=\"120000\" copy=\"1\" pasteAll=\"1\" pasteValues=\"1\" merge=\"1\" splitFirst=\"1\" rowColShift=\"1\" clearFormats=\"1\" clearComments=\"1\" assign=\"1\" coerce=\"1\" cellMeta=\"1\"/></metadataTypes>"
            + "<futureMetadata name=\"XLDAPR\" count=\"1\"><bk><extLst><ext uri=\"{bdbb8cdc-fa1e-496e-a857-3c3f30c029c3}\"><xda:dynamicArrayProperties fDynamic=\"1\" fCollapsed=\"0\"/></ext></extLst></bk></futureMetadata>"
            + "<cellMetadata count=\"1\"><bk><rc t=\"1\" v=\"0\"/></bk></cellMetadata></metadata>";
        string partName = "/xl/metadata.xml";
        for (int n = 2; Package.FindPart(partName) is not null; n++)
        {
            partName = "/xl/metadata" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".xml";
        }

        Package.AddPart(partName, MetadataContentType, System.Text.Encoding.UTF8.GetBytes(xml));
        string rid = "rId1";
        for (int n = 1; rels.Exists(r => r.Id == rid); n++)
        {
            rid = "rId" + (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        rels.Add(new Relationship(rid, baseType, partName[(partName.LastIndexOf('/') + 1)..]));
        Package.SetRelationships(MainPartName, rels);
        return 1;
    }
}
