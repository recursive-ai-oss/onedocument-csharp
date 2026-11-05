using System.Linq;
using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Pivots;

/// <summary>Where a pivot cache takes its data from (ECMA-376 Part 1 §18.10.1.95, <c>cacheSource type</c>).</summary>
public enum PivotSourceKind
{
    /// <summary>A worksheet range, a table or a defined name: the only source this library refreshes.</summary>
    Worksheet,

    /// <summary>An external connection (database, OLAP cube, the Data Model). Kept as stored; refresh reports "not supported".</summary>
    External,

    /// <summary>Multiple consolidation ranges. Kept as stored; refresh reports "not supported".</summary>
    Consolidation,

    /// <summary>The scenario manager. Kept as stored; refresh reports "not supported".</summary>
    Scenario,

    /// <summary>A source type this library does not know.</summary>
    Unknown,
}

/// <summary>The summary function of a pivot data field (ECMA-376 Part 1 §18.18.43, ST_DataConsolidateFunction).</summary>
public enum PivotFunction
{
    /// <summary>Sum, the default.</summary>
    Sum,

    /// <summary>Count of non-empty values.</summary>
    Count,

    /// <summary>Average.</summary>
    Average,

    /// <summary>Largest number.</summary>
    Max,

    /// <summary>Smallest number.</summary>
    Min,

    /// <summary>Product.</summary>
    Product,

    /// <summary>Count of numbers.</summary>
    CountNums,

    /// <summary>Sample standard deviation.</summary>
    StdDev,

    /// <summary>Population standard deviation.</summary>
    StdDevP,

    /// <summary>Sample variance.</summary>
    Var,

    /// <summary>Population variance.</summary>
    VarP,
}

/// <summary>How a pivot data field shows its values (ECMA-376 Part 1 §18.18.70, ST_ShowDataAs).</summary>
public enum PivotShowDataAs
{
    /// <summary>The summarised value as it is.</summary>
    Normal,

    /// <summary>Difference from the base item of the base field.</summary>
    Difference,

    /// <summary>Percent of the base item.</summary>
    Percent,

    /// <summary>Percent difference from the base item.</summary>
    PercentDiff,

    /// <summary>Running total in the base field.</summary>
    RunTotal,

    /// <summary>Percent of the row's total.</summary>
    PercentOfRow,

    /// <summary>Percent of the column's total.</summary>
    PercentOfCol,

    /// <summary>Percent of the grand total.</summary>
    PercentOfTotal,

    /// <summary>The Index form: (value × grand total) / (row total × column total).</summary>
    Index,

    /// <summary>Percent of the parent row total.</summary>
    PercentOfParentRow,

    /// <summary>Percent of the parent column total.</summary>
    PercentOfParentCol,

    /// <summary>Percent of the parent item of the base field. Kept as stored; refresh shows the normal value and reports <c>pivot.show-data-as-unsupported</c>.</summary>
    PercentOfParent,

    /// <summary>Percent running total. Kept as stored; refresh shows the normal value and reports <c>pivot.show-data-as-unsupported</c>.</summary>
    PercentRunTotal,

    /// <summary>Rank, smallest first. Kept as stored; refresh shows the normal value and reports <c>pivot.show-data-as-unsupported</c>.</summary>
    RankAscending,

    /// <summary>Rank, largest first. Kept as stored; refresh shows the normal value and reports <c>pivot.show-data-as-unsupported</c>.</summary>
    RankDescending,
}

/// <summary>A data field of a pivot table (ECMA-376 Part 1 §18.10.1.43, <c>dataField</c>).</summary>
/// <param name="Name">The caption, such as <c>Sum of Sales</c>.</param>
/// <param name="FieldIndex">The index of the cache field it summarises.</param>
/// <param name="Function">The summary function.</param>
/// <param name="ShowDataAs">How the values are shown.</param>
/// <param name="BaseField">The base field of the difference and running-total forms.</param>
/// <param name="BaseItem">The base item: an index into the base field's items, 1048828 for the previous item, 1048829 for the next.</param>
/// <param name="NumberFormatId">The number format id, 0 for none given.</param>
public sealed record PivotDataField(string Name, int FieldIndex, PivotFunction Function, PivotShowDataAs ShowDataAs, int BaseField, int BaseItem, int NumberFormatId);

/// <summary>What a refresh did to one pivot table.</summary>
public enum PivotRefreshStatus
{
    /// <summary>The cache and the table were rebuilt.</summary>
    Refreshed,

    /// <summary>The cache is not a worksheet or table source (external, OLAP, Data Model, consolidation), or the table uses a feature refresh cannot reproduce; nothing was changed.</summary>
    NotSupported,

    /// <summary>The source range or table cannot be found or has no usable header row; nothing was changed.</summary>
    SourceInvalid,
}

/// <summary>The result of refreshing one pivot table.</summary>
/// <param name="Table">The table.</param>
/// <param name="Status">What happened.</param>
/// <param name="Message">A short explanation when the table was not refreshed; empty otherwise.</param>
/// <param name="Range">The cells the table now occupies (page fields excluded); invalid when it was not refreshed.</param>
/// <param name="RecordCount">The number of source records read.</param>
public sealed record PivotRefreshOutcome(PivotTable Table, PivotRefreshStatus Status, string Message, CellRange Range, int RecordCount);

/// <summary>The result of a refresh of several pivot tables.</summary>
public sealed class PivotRefreshResult
{
    internal PivotRefreshResult(IReadOnlyList<PivotRefreshOutcome> outcomes) => Outcomes = outcomes;

    /// <summary>Gets one outcome per pivot table, in workbook order.</summary>
    public IReadOnlyList<PivotRefreshOutcome> Outcomes { get; }

    /// <summary>Gets a value indicating whether every table was refreshed.</summary>
    public bool AllRefreshed => Outcomes.All(o => o.Status == PivotRefreshStatus.Refreshed);
}

/// <summary>Options for refreshing pivot tables.</summary>
public sealed class PivotRefreshOptions
{
    /// <summary>
    /// Gets or sets the date stored as the cache's <c>refreshedDate</c>. Null (the default) leaves the stored value alone, so the output does not depend on the clock.
    /// </summary>
    public DateTime? RefreshedDate { get; init; }

    /// <summary>Gets or sets the name stored as the cache's <c>refreshedBy</c>; null leaves it alone.</summary>
    public string? RefreshedBy { get; init; }
}

/// <summary>
/// A pivot cache (ECMA-376 Part 1 §18.10.1.64, <c>pivotCacheDefinition</c>, and its records part): the source data of one or more pivot tables, held as
/// shared item lists and records. A view over the parts; refreshing rebuilds both from the source range and keeps unknown markup in place.
/// </summary>
public sealed class PivotCache
{
    private readonly Workbook _workbook;
    private readonly MarkupDocument _definition;

    internal PivotCache(Workbook workbook, int cacheId, string partName, string relationshipId, MarkupDocument definition)
    {
        _workbook = workbook;
        CacheId = cacheId;
        PartName = partName;
        RelationshipId = relationshipId;
        _definition = definition;
    }

    /// <summary>Gets the id the workbook and the pivot tables use to refer to the cache.</summary>
    public int CacheId { get; }

    /// <summary>Gets the part name of the definition, such as <c>/xl/pivotCache/pivotCacheDefinition1.xml</c>.</summary>
    public string PartName { get; }

    internal string RelationshipId { get; }

    /// <summary>Gets the markup tree of the definition part.</summary>
    public MarkupDocument Markup => _definition;

    internal Workbook Workbook => _workbook;

    internal MarkupNode Root => _definition.Root;

    internal byte[]? NewRecords { get; set; }

    internal bool IsModified => _definition.IsModified || NewRecords is not null;

    /// <summary>Gets the part name of the records part, or null when the cache stores no records.</summary>
    public string? RecordsPartName
    {
        get
        {
            string? rid = Root.GetAttribute(_workbook.RelationshipNamespace, "id");
            if (rid is null)
            {
                return null;
            }

            foreach (Relationship r in _workbook.Package.GetRelationships(PartName))
            {
                if (r.Id == rid && r.TargetMode == RelationshipTargetMode.Internal && PartUri.TryResolve(new PartUri(PartName), r.Target, out PartUri uri))
                {
                    return _workbook.Package.FindPart(uri.Value)?.Name;
                }
            }

            return null;
        }
    }

    /// <summary>Gets the kind of source.</summary>
    public PivotSourceKind SourceKind => SourceNode is { IsNull: false } s
        ? s.GetAttribute("type") switch { "worksheet" => PivotSourceKind.Worksheet, "external" => PivotSourceKind.External, "consolidation" => PivotSourceKind.Consolidation, "scenario" => PivotSourceKind.Scenario, _ => PivotSourceKind.Unknown }
        : PivotSourceKind.Unknown;

    internal MarkupNode SourceNode => Child(Root, "cacheSource");

    internal MarkupNode WorksheetSource => Child(SourceNode, "worksheetSource");

    /// <summary>Gets the source range text (<c>ref</c> of <c>worksheetSource</c>), or null when the source is a named range or table.</summary>
    public string? SourceReference => WorksheetSource is { IsNull: false } w ? w.GetAttribute("ref") : null;

    /// <summary>Gets the source sheet name, or null.</summary>
    public string? SourceSheet => WorksheetSource is { IsNull: false } w ? w.GetAttribute("sheet") : null;

    /// <summary>Gets the source defined name or table name, or null.</summary>
    public string? SourceName => WorksheetSource is { IsNull: false } w ? w.GetAttribute("name") : null;

    /// <summary>Gets a value indicating whether the file asks the application to refresh the cache when it opens (<c>refreshOnLoad</c>).</summary>
    public bool RefreshOnLoad => PivotXml.Bool(Root.GetAttribute("refreshOnLoad"), false);

    /// <summary>Gets a value indicating whether the cache stores its records (<c>saveData</c>, default true).</summary>
    public bool SaveData => PivotXml.Bool(Root.GetAttribute("saveData"), true);

    /// <summary>Gets the record count the definition states.</summary>
    public int RecordCount => PivotXml.IntAttr(Root.GetAttribute("recordCount"), 0);

    /// <summary>Gets the cache field names in order, including calculated and group fields.</summary>
    public IReadOnlyList<string> FieldNames
    {
        get
        {
            var list = new List<string>();
            foreach (MarkupNode f in Child(Root, "cacheFields").ChildElements)
            {
                if (f.LocalName.SequenceEqual("cacheField"u8))
                {
                    list.Add(f.GetAttribute("name") ?? string.Empty);
                }
            }

            return list;
        }
    }

    /// <summary>Gets the pivot tables that use the cache.</summary>
    public IReadOnlyList<PivotTable> Tables
    {
        get
        {
            var list = new List<PivotTable>();
            foreach (Worksheet s in _workbook.Sheets)
            {
                if (s.Kind != SheetKind.Worksheet)
                {
                    continue;
                }

                foreach (PivotTable t in s.PivotTables)
                {
                    if (t.CacheId == CacheId)
                    {
                        list.Add(t);
                    }
                }
            }

            return list;
        }
    }

    internal static MarkupNode Child(MarkupNode parent, string local)
    {
        if (parent.IsNull)
        {
            return default;
        }

        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalName.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(local)))
            {
                return c;
            }
        }

        return default;
    }
}

/// <summary>
/// A pivot table (ECMA-376 Part 1 §18.10.1.73, <c>pivotTableDefinition</c>): a view over the table's definition part. The rendered cells are ordinary
/// cells of the sheet. <see cref="Refresh"/> rebuilds the cache from its source and the cells and the definition's derived parts from the cache.
/// </summary>
public sealed class PivotTable
{
    internal PivotTable(Worksheet sheet, string partName, MarkupDocument definition, string relationshipId)
    {
        Sheet = sheet;
        PartName = partName;
        Markup = definition;
        RelationshipId = relationshipId;
    }

    /// <summary>Gets the worksheet that holds the rendered table.</summary>
    public Worksheet Sheet { get; }

    /// <summary>Gets the part name, such as <c>/xl/pivotTables/pivotTable1.xml</c>.</summary>
    public string PartName { get; }

    internal string RelationshipId { get; }

    /// <summary>Gets the markup tree of the definition part.</summary>
    public MarkupDocument Markup { get; }

    internal MarkupNode Root => Markup.Root;

    internal bool IsModified => Markup.IsModified;

    /// <summary>Gets the table's name.</summary>
    public string Name => Root.GetAttribute("name") ?? string.Empty;

    /// <summary>Gets the id of the cache the table uses.</summary>
    public int CacheId => PivotXml.IntAttr(Root.GetAttribute("cacheId"), -1);

    /// <summary>Gets the cache, or null when the workbook has none with the table's id.</summary>
    public PivotCache? Cache => Sheet.Workbook.PivotCaches.FindById(CacheId);

    /// <summary>Gets the cells the table occupies as stored: <c>location ref</c>, page fields excluded. Invalid when the definition has none.</summary>
    public CellRange Location => CellRange.TryParse(PivotCache.Child(Root, "location") is { IsNull: false } l ? l.GetAttribute("ref") : null, out CellRange r) ? r : default;

    /// <summary>Gets the cache field indexes on the row axis in order; -2 stands for the data fields.</summary>
    public IReadOnlyList<int> RowFields => AxisFields("rowFields");

    /// <summary>Gets the cache field indexes on the column axis in order; -2 stands for the data fields.</summary>
    public IReadOnlyList<int> ColumnFields => AxisFields("colFields");

    /// <summary>Gets the cache field indexes of the page (report filter) fields in order.</summary>
    public IReadOnlyList<int> PageFields
    {
        get
        {
            var list = new List<int>();
            foreach (MarkupNode f in PivotCache.Child(Root, "pageFields").ChildElements)
            {
                list.Add(PivotXml.IntAttr(f.GetAttribute("fld"), -1));
            }

            return list;
        }
    }

    /// <summary>Gets the data fields in order.</summary>
    public IReadOnlyList<PivotDataField> DataFields => PivotDefinition.ReadDataFields(Root);

    /// <summary>
    /// Rebuilds the pivot cache from its worksheet or table source, then every table that uses the cache (this one included, because the
    /// cache's shared items are shared): the cache definition and records, the fields' item lists, the row and column items, the location and
    /// the rendered cells. Other cache sources give <see cref="PivotRefreshStatus.NotSupported"/> and change nothing.
    /// </summary>
    /// <remarks>Nothing recalculates: formulas that read the pivot's cells (<c>GETPIVOTDATA</c>) see the new cells at the next recalculation.</remarks>
    /// <param name="options">Options; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels before the cells are written; a cancelled refresh changes nothing.</param>
    /// <returns>The outcome for this table.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public PivotRefreshOutcome Refresh(PivotRefreshOptions? options = null, CancellationToken cancellationToken = default)
    {
        PivotRefreshResult result = PivotRefresher.RefreshCache(Sheet.Workbook, Cache, [this], options ?? new PivotRefreshOptions(), cancellationToken);
        foreach (PivotRefreshOutcome o in result.Outcomes)
        {
            if (ReferenceEquals(o.Table, this))
            {
                return o;
            }
        }

        return new PivotRefreshOutcome(this, PivotRefreshStatus.NotSupported, "The table has no cache.", default, 0);
    }

    private List<int> AxisFields(string local)
    {
        var list = new List<int>();
        foreach (MarkupNode f in PivotCache.Child(Root, local).ChildElements)
        {
            list.Add(PivotXml.IntAttr(f.GetAttribute("x"), -1));
        }

        return list;
    }

    /// <inheritdoc />
    public override string ToString() => Sheet.Name + "!" + Name;
}

/// <summary>The pivot tables of one worksheet (the sheet's <c>pivotTable</c> relationships). Read when first enumerated.</summary>
public sealed class PivotTableCollection : IEnumerable<PivotTable>
{
    private readonly Worksheet _sheet;
    private readonly object _gate = new();
    private List<PivotTable>? _tables;

    internal PivotTableCollection(Worksheet sheet) => _sheet = sheet;

    internal bool IsModified
    {
        get
        {
            if (_tables is null)
            {
                return false;
            }

            foreach (PivotTable t in _tables)
            {
                if (t.IsModified)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal void Commit()
    {
        if (_tables is null)
        {
            return;
        }

        foreach (PivotTable t in _tables)
        {
            if (t.IsModified && _sheet.Workbook.Package.FindPart(t.PartName) is { } part)
            {
                part.SetBytes(t.Markup.ToArray());
            }
        }
    }

    private List<PivotTable> Load()
    {
        lock (_gate)
        {
            if (_tables is not null)
            {
                return _tables;
            }

            var list = new List<PivotTable>();
            Workbook wb = _sheet.Workbook;
            if (_sheet.PartName is { } sheetPart && _sheet.Kind == SheetKind.Worksheet)
            {
                foreach (Relationship r in wb.Package.GetRelationships(sheetPart))
                {
                    if (!Workbook.IsRelationship(r.Type, "pivotTable"))
                    {
                        continue;
                    }

                    if (r.TargetMode != RelationshipTargetMode.Internal || !PartUri.TryResolve(new PartUri(sheetPart), r.Target, out PartUri uri) || wb.Package.FindPart(uri.Value) is not { } part)
                    {
                        wb.OpenContext.Defects.Report(new Defect(DefectKind.Packaging, "xlsx.pivot-table-part-missing", DefectSeverity.Dropped,
                            "A pivotTable relationship does not lead to a part; the pivot table is ignored.", DefectLocation.InPart(sheetPart)));
                        continue;
                    }

                    try
                    {
                        MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, part.Name);
                        if (!doc.Root.LocalName.SequenceEqual("pivotTableDefinition"u8))
                        {
                            throw new OfficeFormatException(DefectKind.Structure, "xlsx.pivot-table-wrong-root", "A pivot table part does not start with pivotTableDefinition.", DefectLocation.InPart(part.Name));
                        }

                        list.Add(new PivotTable(_sheet, part.Name, doc, r.Id));
                    }
                    catch (OfficeFormatException ex)
                    {
                        wb.OpenContext.Defects.Report(new Defect(DefectKind.Structure, "xlsx.pivot-table-unreadable", DefectSeverity.Dropped,
                            "A pivot table part cannot be read (" + ex.Defect.Code + "); the pivot table is ignored and kept as stored.", DefectLocation.InPart(part.Name)));
                    }
                }
            }

            _tables = list;
            return list;
        }
    }

    /// <inheritdoc />
    public IEnumerator<PivotTable> GetEnumerator() => Load().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Gets the number of pivot tables.</summary>
    public int Count => Load().Count;

    /// <summary>Gets a pivot table by name (case-insensitive), or null.</summary>
    /// <param name="name">The table's name.</param>
    /// <returns>The table, or null.</returns>
    public PivotTable? this[string name]
    {
        get
        {
            foreach (PivotTable t in Load())
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            return null;
        }
    }

    /// <summary>Finds the table whose location holds a cell.</summary>
    /// <param name="address">The cell.</param>
    /// <returns>The table, or null.</returns>
    public PivotTable? Find(CellAddress address)
    {
        foreach (PivotTable t in Load())
        {
            if (t.Location is { IsValid: true } r && r.Contains(address))
            {
                return t;
            }
        }

        return null;
    }
}

/// <summary>The pivot caches of a workbook, read from its <c>pivotCaches</c> element on first use.</summary>
public sealed class PivotCacheCollection : IEnumerable<PivotCache>
{
    private readonly Workbook _workbook;
    private readonly object _gate = new();
    private List<PivotCache>? _caches;

    internal PivotCacheCollection(Workbook workbook) => _workbook = workbook;

    internal bool IsModified
    {
        get
        {
            if (_caches is null)
            {
                return false;
            }

            foreach (PivotCache c in _caches)
            {
                if (c.IsModified)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal void Commit()
    {
        if (_caches is null)
        {
            return;
        }

        Workbook wb = _workbook;
        foreach (PivotCache c in _caches)
        {
            if (!c.IsModified)
            {
                continue;
            }

            if (c.NewRecords is { } bytes)
            {
                string? name = c.RecordsPartName;
                if (name is not null && wb.Package.FindPart(name) is { } existing)
                {
                    existing.SetBytes(bytes);
                }
                else
                {
                    string part = "/xl/pivotCache/pivotCacheRecords1.xml";
                    for (int n = 2; wb.Package.FindPart(part) is not null; n++)
                    {
                        part = "/xl/pivotCache/pivotCacheRecords" + n.ToString(CultureInfo.InvariantCulture) + ".xml";
                    }

                    wb.Package.AddPart(part, "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml", bytes);
                    var rels = new List<Relationship>(wb.Package.GetRelationships(c.PartName));
                    string rid = "rId1";
                    for (int n = 2; rels.Exists(r => r.Id == rid); n++)
                    {
                        rid = "rId" + n.ToString(CultureInfo.InvariantCulture);
                    }

                    string defDir = c.PartName[..(c.PartName.LastIndexOf('/') + 1)];
                    rels.Add(new Relationship(rid, wb.RelationshipBase + "pivotCacheRecords", part.StartsWith(defDir, StringComparison.Ordinal) ? part[defDir.Length..] : part));
                    wb.Package.SetRelationships(c.PartName, rels);
                    c.Root.SetAttribute(wb.RelationshipNamespace, "id", rid, "r");
                }

                c.NewRecords = null;
            }

            if (wb.Package.FindPart(c.PartName) is { } def)
            {
                def.SetBytes(c.Markup.ToArray());
            }
        }
    }

    private List<PivotCache> Load()
    {
        lock (_gate)
        {
            if (_caches is not null)
            {
                return _caches;
            }

            var list = new List<PivotCache>();
            Workbook wb = _workbook;
            MarkupNode caches = PivotCache.Child(wb.Markup.Root, "pivotCaches");
            foreach (MarkupNode pc in caches.ChildElements)
            {
                if (!pc.LocalName.SequenceEqual("pivotCache"u8))
                {
                    continue;
                }

                int id = PivotXml.IntAttr(pc.GetAttribute("cacheId"), -1);
                string? rid = pc.GetAttribute(wb.RelationshipNamespace, "id");
                Relationship? rel = null;
                foreach (Relationship r in wb.Package.GetRelationships(wb.MainPartName))
                {
                    if (r.Id == rid)
                    {
                        rel = r;
                        break;
                    }
                }

                if (rel is null || rel.TargetMode != RelationshipTargetMode.Internal || !PartUri.TryResolve(new PartUri(wb.MainPartName), rel.Target, out PartUri uri) || wb.Package.FindPart(uri.Value) is not { } part)
                {
                    wb.OpenContext.Defects.Report(new Defect(DefectKind.Packaging, "xlsx.pivot-cache-part-missing", DefectSeverity.Dropped,
                        "A pivotCache element does not lead to a cache definition part; the cache is ignored.", DefectLocation.InPart(wb.MainPartName, pc.SourceStart)));
                    continue;
                }

                try
                {
                    MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, part.Name);
                    if (!doc.Root.LocalName.SequenceEqual("pivotCacheDefinition"u8))
                    {
                        throw new OfficeFormatException(DefectKind.Structure, "xlsx.pivot-cache-wrong-root", "A pivot cache part does not start with pivotCacheDefinition.", DefectLocation.InPart(part.Name));
                    }

                    list.Add(new PivotCache(wb, id, part.Name, rid!, doc));
                }
                catch (OfficeFormatException ex)
                {
                    wb.OpenContext.Defects.Report(new Defect(DefectKind.Structure, "xlsx.pivot-cache-unreadable", DefectSeverity.Dropped,
                        "A pivot cache definition cannot be read (" + ex.Defect.Code + "); the cache is ignored and kept as stored.", DefectLocation.InPart(part.Name)));
                }
            }

            _caches = list;
            return list;
        }
    }

    /// <inheritdoc />
    public IEnumerator<PivotCache> GetEnumerator() => Load().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Gets the number of caches.</summary>
    public int Count => Load().Count;

    /// <summary>Finds a cache by its id.</summary>
    /// <param name="cacheId">The id used by <c>pivotCache cacheId</c> and the pivot tables.</param>
    /// <returns>The cache, or null.</returns>
    public PivotCache? FindById(int cacheId)
    {
        foreach (PivotCache c in Load())
        {
            if (c.CacheId == cacheId)
            {
                return c;
            }
        }

        return null;
    }
}
