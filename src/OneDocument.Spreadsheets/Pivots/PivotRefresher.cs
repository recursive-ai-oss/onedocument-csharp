using RecursiveAI.OneDocument.NumberFormats;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Spreadsheets.Pivots;

/// <summary>Refreshes a pivot cache and the tables that use it.</summary>
internal static class PivotRefresher
{
    public static PivotRefreshResult RefreshCache(Workbook wb, PivotCache? cache, IReadOnlyList<PivotTable> requested, PivotRefreshOptions options, CancellationToken ct)
    {
        var outcomes = new List<PivotRefreshOutcome>();
        if (cache is null)
        {
            foreach (PivotTable t in requested)
            {
                outcomes.Add(new PivotRefreshOutcome(t, PivotRefreshStatus.NotSupported, "The workbook has no pivot cache with id " + t.CacheId + ".", default, 0));
            }

            return new PivotRefreshResult(outcomes);
        }

        IReadOnlyList<PivotTable> tables = cache.Tables;
        if (cache.SourceKind != PivotSourceKind.Worksheet)
        {
            foreach (PivotTable t in tables)
            {
                outcomes.Add(new PivotRefreshOutcome(t, PivotRefreshStatus.NotSupported, "The cache source is " + cache.SourceKind + "; only worksheet and table sources can be refreshed.", default, 0));
            }

            return new PivotRefreshResult(outcomes);
        }

        (Worksheet Sheet, CellRange Range)? source = PivotSource.Resolve(cache, out string reason);
        SourceData? src = source is { } s ? PivotSource.Read(s.Sheet, s.Range, ct, out reason) : null;
        if (src is null)
        {
            foreach (PivotTable t in tables)
            {
                outcomes.Add(new PivotRefreshOutcome(t, PivotRefreshStatus.SourceInvalid, reason, default, 0));
            }

            return new PivotRefreshResult(outcomes);
        }

        DateSystem ds = wb.DateSystem;
        OldCache old = OldCache.Read(cache);
        int n = src.Headers.Length;

        // Stored cache field index -> refreshed index. Source columns match by name; calculated and group fields follow the columns in stored order.
        var map = new Dictionary<int, int>();
        var extras = new List<OldField>();
        if (old.Fields.Count == 0)
        {
            for (int i = 0; i < n; i++)
            {
                map[i] = i;
            }
        }
        else
        {
            for (int i = 0; i < old.Fields.Count; i++)
            {
                OldField of = old.Fields[i];
                if (!of.IsDatabase)
                {
                    map[i] = n + extras.Count;
                    extras.Add(of);
                    continue;
                }

                int j = Array.FindIndex(src.Headers, h => string.Equals(h, of.Name, StringComparison.OrdinalIgnoreCase));
                if (j >= 0)
                {
                    map[i] = j;
                }
                else
                {
                    wb.OpenContext.Defects.Report(new Defect(DefectKind.Reference, "pivot.source-field-missing", DefectSeverity.Dropped,
                        "The source no longer has a column '" + of.Name + "'; the field is dropped from the cache and the tables.", DefectLocation.InPart(cache.PartName)));
                }
            }
        }

        bool remapped = old.Fields.Count != 0 && (old.Fields.Count != n + extras.Count || map.Any(kv => kv.Key != kv.Value) || map.Count != old.Fields.Count);
        int MapFn(int i) => map.TryGetValue(i, out int v) ? v : -1;

        // A field must list its items when any table puts it on an axis.
        var onAxis = new bool[n];
        foreach (PivotTable t in tables)
        {
            int oi = 0;
            foreach (MarkupNode pf in PivotCache.Child(t.Root, "pivotFields").ChildElements)
            {
                if (pf.GetAttribute("axis") is "axisRow" or "axisCol" or "axisPage" && MapFn(oi) is >= 0 and var ni && ni < n)
                {
                    onAxis[ni] = true;
                }

                oi++;
            }

            foreach (string list in new[] { "rowFields", "colFields" })
            {
                foreach (MarkupNode f in PivotCache.Child(t.Root, list).ChildElements)
                {
                    if (MapFn(PivotXml.IntAttr(f.GetAttribute("x"), -1)) is >= 0 and var ni && ni < n)
                    {
                        onAxis[ni] = true;
                    }
                }
            }

            foreach (MarkupNode f in PivotCache.Child(t.Root, "pageFields").ChildElements)
            {
                if (MapFn(PivotXml.IntAttr(f.GetAttribute("fld"), -1)) is >= 0 and var ni && ni < n)
                {
                    onAxis[ni] = true;
                }
            }
        }

        var defects = new List<Defect>();
        CacheData data = PivotCacheBuilder.Build(cache, src, old, onAxis, map, extras, ds, defects.Add);

        var models = new List<(PivotTable Table, TableModel Model, PivotLayout Layout, RenderedTable Rendered, CellRange OldRange, int OldPages)>();
        foreach (PivotTable t in tables)
        {
            TableModel m = TableModel.Read(t, data, old, MapFn);
            PivotLayout lay = PivotLayout.Build(m, data, wb, ct);
            CellRange loc = t.Location;
            int top = loc.IsValid ? loc.TopLeft.Row : 3;
            int left = loc.IsValid ? loc.TopLeft.Column : 1;
            RenderedTable rt = PivotRenderer.Render(m, data, lay, wb, top, left);
            int oldPages = PivotXml.IntAttr(PivotCache.Child(t.Root, "location") is { IsNull: false } l ? l.GetAttribute("rowPageCount") : null, 0);
            models.Add((t, m, lay, rt, loc, oldPages));
        }

        ct.ThrowIfCancellationRequested();

        PivotCacheWriter.Write(cache, data, old, map, n, options);
        foreach ((PivotTable t, TableModel m, PivotLayout lay, RenderedTable rt, CellRange oldRange, int oldPages) in models)
        {
            PivotWriter.WriteCells(t, rt, oldRange, oldPages);
            PivotWriter.WriteDefinition(m, data, lay, rt, MapFn, remapped);
            defects.AddRange(m.Defects);
            outcomes.Add(new PivotRefreshOutcome(t, PivotRefreshStatus.Refreshed, string.Empty, rt.Range, data.Records));
        }

        foreach (Defect d in defects)
        {
            wb.OpenContext.Defects.Report(d);
        }

        return new PivotRefreshResult(outcomes);
    }
}
