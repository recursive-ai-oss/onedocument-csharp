using RecursiveAI.OneOffice.Packaging;
using RecursiveAI.OneOffice.Spreadsheets.Calc;

namespace RecursiveAI.OneOffice.Spreadsheets;

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
        }, cancellationToken);

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
