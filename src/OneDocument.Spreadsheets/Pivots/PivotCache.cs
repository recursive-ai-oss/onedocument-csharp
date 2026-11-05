using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Spreadsheets.Pivots;

/// <summary>A pivot cache field in memory: the source column (or a calculated or group field), its shared items and the per-record item indexes.</summary>
internal sealed class FieldData
{
    public string Name = string.Empty;
    public int Index;
    public bool IsDatabase = true;
    public string? Formula;
    public int NumFmtId;

    /// <summary>The source value of each record (database fields only).</summary>
    public PivotValue[]? Values;

    /// <summary>The shared items in order of first appearance; empty when the field stores its values inline.</summary>
    public readonly List<PivotValue> Shared = [];

    /// <summary>The shared item index of each record, or null when the field stores its values inline.</summary>
    public int[]? Item;

    public bool IsShared => Item is not null;

    /// <summary>The group definition, or null for a plain field.</summary>
    public GroupData? Group;

    /// <summary>The items a pivot table lists for this field: the shared items, or the group items for a grouped field.</summary>
    public List<PivotValue> Axis = [];

    /// <summary>The axis item index of each record, -1 when the record has no item (blank source value of a grouped field).</summary>
    public int[]? AxisItem;
}

/// <summary>The result of grouping a field (ECMA-376 Part 1 §18.10.1.43, <c>fieldGroup</c>).</summary>
internal sealed class GroupData
{
    public int Base;
    public string Kind = "range";
    public MarkupNode Node;
    public List<string> Labels = [];

    /// <summary>The group item of each shared item of the base field.</summary>
    public int[] Map = [];

    public string? StartAttr;
    public string? EndAttr;
    public string? StartAttrName;
    public string? EndAttrName;
    public bool Discrete;
}

/// <summary>The cache as read from the stored definition, before a refresh replaces it.</summary>
internal sealed class OldCache
{
    public readonly List<OldField> Fields = [];

    public static OldCache Read(PivotCache cache)
    {
        var o = new OldCache();
        DateSystem ds = cache.Workbook.DateSystem;
        foreach (MarkupNode f in PivotCache.Child(cache.Root, "cacheFields").ChildElements)
        {
            if (!f.LocalName.SequenceEqual("cacheField"u8))
            {
                continue;
            }

            var of = new OldField
            {
                Node = f,
                Name = f.GetAttribute("name") ?? string.Empty,
                IsDatabase = PivotXml.Bool(f.GetAttribute("databaseField"), true),
                Formula = f.GetAttribute("formula"),
                NumFmtId = PivotXml.IntAttr(f.GetAttribute("numFmtId"), 0),
            };
            MarkupNode shared = PivotCache.Child(f, "sharedItems");
            foreach (MarkupNode it in shared.ChildElements)
            {
                of.Shared.Add(ReadItem(it, ds));
            }

            MarkupNode fg = PivotCache.Child(f, "fieldGroup");
            if (!fg.IsNull)
            {
                of.GroupNode = fg;
                of.GroupBase = PivotXml.IntAttr(fg.GetAttribute("base"), -1);
                foreach (MarkupNode gi in PivotCache.Child(fg, "groupItems").ChildElements)
                {
                    of.GroupLabels.Add(gi.GetAttribute("v") ?? string.Empty);
                }

                foreach (MarkupNode x in PivotCache.Child(fg, "discretePr").ChildElements)
                {
                    of.DiscreteMap.Add(PivotXml.IntAttr(x.GetAttribute("v"), 0));
                }
            }

            o.Fields.Add(of);
        }

        return o;
    }

    public static PivotValue ReadItem(MarkupNode it, DateSystem ds)
    {
        string? v = it.GetAttribute("v");
        if (it.LocalName.SequenceEqual("n"u8))
        {
            return PivotValue.FromNumber(PivotXml.DoubleAttr(v, 0));
        }

        if (it.LocalName.SequenceEqual("s"u8))
        {
            return PivotValue.FromText(v ?? string.Empty);
        }

        if (it.LocalName.SequenceEqual("b"u8))
        {
            return PivotValue.FromBoolean(PivotXml.Bool(v, false));
        }

        if (it.LocalName.SequenceEqual("e"u8))
        {
            return PivotValue.FromError(v ?? "#N/A");
        }

        if (it.LocalName.SequenceEqual("d"u8))
        {
            return PivotXml.TryParseDate(v, ds, out double serial) ? PivotValue.FromDate(serial) : PivotValue.FromText(v ?? string.Empty);
        }

        return PivotValue.Blank;
    }

    /// <summary>The items a pivot field's <c>x</c> indexes point into: group labels for a grouped field, else the shared items.</summary>
    public List<PivotValue> AxisValues(int field)
    {
        if (field < 0 || field >= Fields.Count)
        {
            return [];
        }

        OldField f = Fields[field];
        if (f.GroupNode is { IsNull: false })
        {
            return f.GroupLabels.ConvertAll(PivotValue.FromText);
        }

        return f.Shared;
    }
}

internal sealed class OldField
{
    public MarkupNode Node;
    public string Name = string.Empty;
    public bool IsDatabase = true;
    public string? Formula;
    public int NumFmtId;
    public readonly List<PivotValue> Shared = [];
    public MarkupNode GroupNode;
    public int GroupBase = -1;
    public readonly List<string> GroupLabels = [];
    public readonly List<int> DiscreteMap = [];
}

/// <summary>The source range of a cache and its header and data.</summary>
internal sealed class SourceData
{
    public string[] Headers = [];
    public PivotValue[][] Columns = [];
    public int[] NumFmt = [];
    public int Records;
}

/// <summary>Reads a cache's worksheet or table source.</summary>
internal static class PivotSource
{
    /// <summary>Resolves the source to a sheet and range; null with a reason when it cannot be found.</summary>
    public static (Worksheet Sheet, CellRange Range)? Resolve(PivotCache cache, out string reason)
    {
        Workbook wb = cache.Workbook;
        reason = string.Empty;
        string? name = cache.SourceName;
        string? sheetName = cache.SourceSheet;
        string? reference = cache.SourceReference;
        if (!string.IsNullOrEmpty(name))
        {
            foreach (Worksheet s in wb.Sheets)
            {
                if (s.Kind != SheetKind.Worksheet)
                {
                    continue;
                }

                foreach (Table t in s.Tables)
                {
                    if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(t.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        CellRange r = t.Range;
                        if (t.TotalsRowCount > 0 && r.IsValid)
                        {
                            r = new CellRange(r.TopLeft, new CellAddress(r.BottomRight.Row - t.TotalsRowCount, r.BottomRight.Column));
                        }

                        return r.IsValid ? (s, r) : null;
                    }
                }
            }

            DefinedName? dn = wb.Names.Find(name);
            if (dn is not null && TryParseReference(wb, dn.Formula, out Worksheet? ws, out CellRange range))
            {
                return (ws!, range);
            }

            reason = "The source name '" + name + "' is not a table or a defined name that refers to a range.";
            return null;
        }

        if (sheetName is null || reference is null)
        {
            reason = "The cache source has no sheet and range.";
            return null;
        }

        if (!wb.Sheets.TryGet(sheetName, out Worksheet? sheet) || sheet.Kind != SheetKind.Worksheet)
        {
            reason = "The source sheet '" + sheetName + "' does not exist.";
            return null;
        }

        if (!CellRange.TryParse(reference.Replace("$", string.Empty, StringComparison.Ordinal), out CellRange rr))
        {
            reason = "The source range '" + reference + "' is not a range.";
            return null;
        }

        return (sheet, rr);
    }

    private static bool TryParseReference(Workbook wb, string formula, out Worksheet? sheet, out CellRange range)
    {
        sheet = null;
        range = default;
        string f = formula.Trim().TrimStart('=');
        int bang = f.LastIndexOf('!');
        if (bang <= 0)
        {
            return false;
        }

        string sn = f[..bang];
        if (sn.Length >= 2 && sn[0] == '\'' && sn[^1] == '\'')
        {
            sn = sn[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return wb.Sheets.TryGet(sn, out sheet) && sheet.Kind == SheetKind.Worksheet && CellRange.TryParse(f[(bang + 1)..].Replace("$", string.Empty, StringComparison.Ordinal), out range);
    }

    public static SourceData? Read(Worksheet sheet, CellRange range, CancellationToken ct, out string reason)
    {
        reason = string.Empty;
        int cols = range.ColumnCount;
        int rows = range.RowCount - 1;
        if (rows < 0 || cols < 1)
        {
            reason = "The source range has no header row.";
            return null;
        }

        long cells = (long)rows * cols;
        long limit = sheet.Workbook.Limits.MaxAllocationBytes / 64;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), limit, cells, DefectLocation.InPart(sheet.PartName ?? sheet.Name));

        var headers = new string[cols];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int r0 = range.TopLeft.Row;
        int c0 = range.TopLeft.Column;
        for (int c = 0; c < cols; c++)
        {
            Cell cell = sheet[r0, c0 + c];
            CellValue v = cell.Value;
            string h = v.Kind == CellValueKind.Empty ? string.Empty : v.Kind == CellValueKind.Number ? v.Number.ToString("R", CultureInfo.InvariantCulture) : sheet.GetText(v);
            if (h.Length == 0)
            {
                reason = "The header cell " + cell.Address + " is empty: a pivot field needs a name.";
                return null;
            }

            string name = h;
            for (int n = 2; !seen.Add(name); n++)
            {
                name = h + n.ToString(CultureInfo.InvariantCulture);
            }

            headers[c] = name;
        }

        var data = new SourceData { Headers = headers, Records = rows, Columns = new PivotValue[cols][], NumFmt = new int[cols] };
        DateSystem ds = sheet.Workbook.DateSystem;
        for (int c = 0; c < cols; c++)
        {
            var col = new PivotValue[rows];
            for (int r = 0; r < rows; r++)
            {
                if ((r & 1023) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                }

                Cell cell = sheet[r0 + 1 + r, c0 + c];
                CellValue v = cell.Value;
                switch (v.Kind)
                {
                    case CellValueKind.Number:
                        bool date = cell.IsDate;
                        col[r] = date ? PivotValue.FromDate(v.Number) : PivotValue.FromNumber(v.Number);
                        if (data.NumFmt[c] == 0)
                        {
                            data.NumFmt[c] = cell.Style.NumberFormatId;
                        }

                        break;
                    case CellValueKind.Text:
                        col[r] = PivotValue.FromText(sheet.GetText(v));
                        break;
                    case CellValueKind.Boolean:
                        col[r] = PivotValue.FromBoolean(v.Boolean);
                        break;
                    case CellValueKind.Error:
                        col[r] = PivotValue.FromError(sheet.GetText(v));
                        break;
                    default:
                        col[r] = PivotValue.Blank;
                        break;
                }
            }

            data.Columns[c] = col;
        }

        _ = ds;
        return data;
    }
}

/// <summary>Builds the new cache from source data and the stored definition.</summary>
internal static class PivotCacheBuilder
{
    /// <summary>The names that decide whether a field must list its items: any field on an axis of any table, or used by a group.</summary>
    public static CacheData Build(PivotCache cache, SourceData src, OldCache old, bool[] onAxisNewIndex, Dictionary<int, int> oldToNew, List<OldField> extras, DateSystem ds, Action<Defect> report)
    {
        int n = src.Headers.Length;
        var data = new CacheData { Records = src.Records };
        var groupedBase = new HashSet<int>();
        for (int k = 0; k < extras.Count; k++)
        {
            if (extras[k].GroupNode.IsNull is false && oldToNew.TryGetValue(extras[k].GroupBase, out int b))
            {
                groupedBase.Add(b);
            }
        }

        for (int j = 0; j < n; j++)
        {
            OldField? of = FindOld(old, src.Headers[j]);
            if (of is { GroupNode.IsNull: false } && oldToNew.TryGetValue(of.GroupBase, out int bb))
            {
                groupedBase.Add(bb);
            }
        }

        for (int j = 0; j < n; j++)
        {
            var f = new FieldData { Name = src.Headers[j], Index = j, Values = src.Columns[j], NumFmtId = src.NumFmt[j] };
            OldField? of = FindOld(old, f.Name);
            if (of is not null && of.NumFmtId != 0)
            {
                f.NumFmtId = of.NumFmtId;
            }

            bool nonNumeric = false;
            foreach (PivotValue v in f.Values)
            {
                if (v.Kind is PivotKind.Text or PivotKind.Date or PivotKind.Boolean or PivotKind.Error)
                {
                    nonNumeric = true;
                    break;
                }
            }

            if (nonNumeric || onAxisNewIndex[j] || groupedBase.Contains(j))
            {
                BuildShared(f);
            }

            f.Axis = f.Shared;
            f.AxisItem = f.Item;
            data.Fields.Add(f);
        }

        for (int k = 0; k < extras.Count; k++)
        {
            OldField e = extras[k];
            data.Fields.Add(new FieldData { Name = e.Name, Index = n + k, IsDatabase = false, Formula = e.Formula, NumFmtId = e.NumFmtId });
        }

        // Fields with a group of their own definition (the base field itself, or an extra group field).
        for (int j = 0; j < data.Fields.Count; j++)
        {
            OldField? of = j < n ? FindOld(old, data.Fields[j].Name) : extras[j - n];
            if (of is null || of.GroupNode.IsNull)
            {
                continue;
            }

            if (!oldToNew.TryGetValue(of.GroupBase, out int baseNew))
            {
                continue;
            }

            FieldData baseField = data.Fields[baseNew];
            if (!baseField.IsShared)
            {
                BuildShared(baseField);
                baseField.Axis = baseField.Shared;
                baseField.AxisItem = baseField.Item;
            }

            GroupData? g = PivotGrouping.Compute(of, old.Fields.Count > of.GroupBase && of.GroupBase >= 0 ? old.Fields[of.GroupBase] : null, baseField, ds, report, cache.PartName);
            if (g is null)
            {
                continue;
            }

            g.Base = baseNew;
            FieldData f = data.Fields[j];
            f.Group = g;
            f.Axis = g.Labels.ConvertAll(PivotValue.FromText);
            var idx = new int[src.Records];
            int[] baseItem = baseField.Item!;
            for (int r = 0; r < idx.Length; r++)
            {
                int bi = baseItem[r];
                idx[r] = bi >= 0 && bi < g.Map.Length ? g.Map[bi] : -1;
            }

            f.AxisItem = idx;
        }

        return data;
    }

    private static OldField? FindOld(OldCache old, string name)
    {
        foreach (OldField f in old.Fields)
        {
            if (f.IsDatabase && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }
        }

        return null;
    }

    private static void BuildShared(FieldData f)
    {
        f.Shared.Clear();
        var index = new Dictionary<PivotValue, int>();
        var item = new int[f.Values!.Length];
        for (int r = 0; r < item.Length; r++)
        {
            PivotValue v = f.Values[r];
            if (!index.TryGetValue(v, out int i))
            {
                i = f.Shared.Count;
                index[v] = i;
                f.Shared.Add(v);
            }

            item[r] = i;
        }

        f.Item = item;
    }
}

/// <summary>The new cache.</summary>
internal sealed class CacheData
{
    public readonly List<FieldData> Fields = [];
    public int Records;
}

/// <summary>Date and numeric grouping of a cache field (ECMA-376 Part 1 §18.10.1.60 <c>fieldGroup</c>, §18.10.1.80 <c>rangePr</c>).</summary>
internal static class PivotGrouping
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static GroupData? Compute(OldField field, OldField? oldBase, FieldData baseField, DateSystem ds, Action<Defect> report, string partName)
    {
        MarkupNode fg = field.GroupNode;
        MarkupNode range = PivotCache.Child(fg, "rangePr");
        if (range.IsNull)
        {
            return ComputeDiscrete(fg, field, oldBase, baseField);
        }

        string by = range.GetAttribute("groupBy") ?? "range";
        var g = new GroupData { Kind = by, Node = fg };
        bool autoStart = PivotXml.Bool(range.GetAttribute("autoStart"), true);
        bool autoEnd = PivotXml.Bool(range.GetAttribute("autoEnd"), true);
        double min = double.MaxValue;
        double max = double.MinValue;
        bool allInt = true;
        foreach (PivotValue v in baseField.Shared)
        {
            if (!v.IsNumeric)
            {
                continue;
            }

            min = Math.Min(min, v.Number);
            max = Math.Max(max, v.Number);
            allInt &= PivotXml.IsInteger(v.Number);
        }

        if (min > max)
        {
            min = max = 0;
        }

        bool isDate = by != "range";
        double interval = Math.Max(PivotXml.DoubleAttr(range.GetAttribute("groupInterval"), 1), isDate ? 1 : double.Epsilon);
        double start;
        double end;
        if (isDate)
        {
            double storedStart = PivotXml.TryParseDate(range.GetAttribute("startDate"), ds, out double s0) ? s0 : min;
            double storedEnd = PivotXml.TryParseDate(range.GetAttribute("endDate"), ds, out double e0) ? e0 : max;
            start = autoStart ? Math.Floor(min) : storedStart;
            end = autoEnd ? Math.Floor(max) : storedEnd;
            g.StartAttrName = "startDate";
            g.EndAttrName = "endDate";
            g.StartAttr = PivotXml.DateText(start, ds);
            g.EndAttr = PivotXml.DateText(end, ds);
        }
        else
        {
            start = autoStart ? min : PivotXml.DoubleAttr(range.GetAttribute("startNum"), min);
            end = autoEnd ? max : PivotXml.DoubleAttr(range.GetAttribute("endNum"), max);
            g.StartAttrName = "startNum";
            g.EndAttrName = "endNum";
            g.StartAttr = PivotXml.Num(start);
            g.EndAttr = PivotXml.Num(end);
        }

        string lo = by == "range" ? "<" + Number(start) : "<" + DateLabel(start, ds);
        string hi = by == "range" ? ">" + Number(end) : ">" + DateLabel(end, ds);
        var map = new int[baseField.Shared.Count];
        g.Labels.Add(lo);
        switch (by)
        {
            case "range":
                {
                    int buckets = (int)Math.Min(Math.Floor((end - start) / interval) + 1, 100_000);
                    for (int k = 0; k < buckets; k++)
                    {
                        double a = start + (k * interval);
                        double b = allInt && PivotXml.IsInteger(interval) ? a + interval - 1 : a + interval;
                        g.Labels.Add(Number(a) + "-" + Number(b));
                    }

                    for (int i = 0; i < map.Length; i++)
                    {
                        map[i] = Place(baseField.Shared[i], start, end, isDate, v => (int)Math.Min(Math.Floor((v - start) / interval), buckets - 1) + 1, buckets);
                    }

                    break;
                }

            case "years":
                {
                    int y0 = Date(start, ds).Year;
                    int y1 = Math.Max(Date(end, ds).Year, y0);
                    for (int y = y0; y <= y1; y++)
                    {
                        g.Labels.Add(y.ToString(CultureInfo.InvariantCulture));
                    }

                    for (int i = 0; i < map.Length; i++)
                    {
                        map[i] = Place(baseField.Shared[i], start, end, isDate, v => Date(v, ds).Year - y0 + 1, y1 - y0 + 1);
                    }

                    break;
                }

            case "quarters":
                for (int q = 1; q <= 4; q++)
                {
                    g.Labels.Add("Qtr" + q.ToString(CultureInfo.InvariantCulture));
                }

                for (int i = 0; i < map.Length; i++)
                {
                    map[i] = Place(baseField.Shared[i], start, end, isDate, v => ((Date(v, ds).Month - 1) / 3) + 1, 4);
                }

                break;
            case "months":
                g.Labels.AddRange(Months);
                for (int i = 0; i < map.Length; i++)
                {
                    map[i] = Place(baseField.Shared[i], start, end, isDate, v => Date(v, ds).Month, 12);
                }

                break;
            case "days":
                if (interval <= 1)
                {
                    for (int d = 1; d <= 366; d++)
                    {
                        DateTime dt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddDays(d - 1);
                        g.Labels.Add(dt.Day.ToString(CultureInfo.InvariantCulture) + "-" + Months[dt.Month - 1]);
                    }

                    for (int i = 0; i < map.Length; i++)
                    {
                        map[i] = Place(baseField.Shared[i], start, end, isDate, v => new DateTime(2000, Date(v, ds).Month, Date(v, ds).Day).DayOfYear, 366);
                    }
                }
                else
                {
                    int buckets = (int)Math.Min(Math.Floor((end - start) / interval) + 1, 100_000);
                    for (int k = 0; k < buckets; k++)
                    {
                        DateTime a = Date(start + (k * interval), ds);
                        DateTime b = Date(Math.Min(start + ((k + 1) * interval) - 1, end), ds);
                        g.Labels.Add(a.Day.ToString(CultureInfo.InvariantCulture) + "-" + Months[a.Month - 1] + " - " + b.Day.ToString(CultureInfo.InvariantCulture) + "-" + Months[b.Month - 1]);
                    }

                    for (int i = 0; i < map.Length; i++)
                    {
                        map[i] = Place(baseField.Shared[i], start, end, isDate, v => (int)Math.Min(Math.Floor((Math.Floor(v) - start) / interval), buckets - 1) + 1, buckets);
                    }
                }

                break;
            case "hours":
            case "minutes":
            case "seconds":
                {
                    int count = by == "hours" ? 24 : 60;
                    for (int k = 0; k < count; k++)
                    {
                        g.Labels.Add(k.ToString(CultureInfo.InvariantCulture));
                    }

                    for (int i = 0; i < map.Length; i++)
                    {
                        map[i] = Place(baseField.Shared[i], start, end, isDate, v =>
                        {
                            int secs = (int)Math.Round((v - Math.Floor(v)) * 86400);
                            int part = by == "hours" ? secs / 3600 : by == "minutes" ? secs / 60 % 60 : secs % 60;
                            return part + 1;
                        }, count);
                    }

                    break;
                }

            default:
                report(new Defect(DefectKind.Unsupported, "pivot.group-unsupported", DefectSeverity.Dropped,
                    "The field '" + field.Name + "' is grouped by '" + by + "', which refresh does not reproduce; the group is dropped.", DefectLocation.InPart(partName)));
                return null;
        }

        g.Labels.Add(hi);
        for (int i = 0; i < map.Length; i++)
        {
            if (map[i] == int.MaxValue)
            {
                map[i] = g.Labels.Count - 1;
            }
        }

        g.Map = map;
        return g;
    }

    // A value before the start falls in the "<" item (0), one after the end in the ">" item (int.MaxValue, fixed up by the caller); others get the inner item.
    private static int Place(PivotValue v, double start, double end, bool day, Func<double, int> inner, int innerCount)
    {
        if (!v.IsNumeric)
        {
            return -1;
        }

        double x = day ? Math.Floor(v.Number) : v.Number;
        if (x < start)
        {
            return 0;
        }

        if (x > end)
        {
            return int.MaxValue;
        }

        return Math.Clamp(inner(v.Number), 1, Math.Max(innerCount, 1));
    }

    private static GroupData? ComputeDiscrete(MarkupNode fg, OldField field, OldField? oldBase, FieldData baseField)
    {
        var g = new GroupData { Node = fg, Kind = "discrete", Discrete = true };
        g.Labels.AddRange(field.GroupLabels);
        var map = new int[baseField.Shared.Count];
        for (int i = 0; i < map.Length; i++)
        {
            int found = -1;
            if (oldBase is not null)
            {
                for (int k = 0; k < oldBase.Shared.Count && k < field.DiscreteMap.Count; k++)
                {
                    if (oldBase.Shared[k].Equals(baseField.Shared[i]))
                    {
                        found = field.DiscreteMap[k];
                        break;
                    }
                }
            }

            if (found < 0 || found >= g.Labels.Count)
            {
                string label = baseField.Shared[i].Label(DateSystem.Date1900);
                found = g.Labels.IndexOf(label);
                if (found < 0)
                {
                    g.Labels.Add(label);
                    found = g.Labels.Count - 1;
                }
            }

            map[i] = found;
        }

        g.Map = map;
        return g;
    }

    private static DateTime Date(double serial, DateSystem ds) => ExcelDate.TryToDateTime(serial, ds, out DateTime d) ? d : new DateTime(1900, 1, 1);

    private static string DateLabel(double serial, DateSystem ds) => Date(serial, ds).ToString("M/d/yyyy", CultureInfo.InvariantCulture);

    private static string Number(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Writes the cache definition and records parts.</summary>
internal static class PivotCacheWriter
{
    public static void Write(PivotCache cache, CacheData data, OldCache old, Dictionary<int, int> oldToNew, int dbCount, PivotRefreshOptions options)
    {
        Workbook wb = cache.Workbook;
        DateSystem ds = wb.DateSystem;
        string pc = PrefixOf(cache.Root);
        var sb = new StringBuilder();
        sb.Append('<').Append(pc).Append("cacheFields count=\"").Append(PivotXml.Int(data.Fields.Count)).Append("\">");
        foreach (FieldData f in data.Fields)
        {
            OldField? of = FindOldFor(old, f, dbCount);
            sb.Append('<').Append(pc).Append("cacheField");
            bool hasName = false;
            bool hasNumFmt = false;
            if (of is not null)
            {
                foreach (MarkupAttributeRef a in of.Node.Attributes)
                {
                    string raw = Encoding.UTF8.GetString(a.RawName);
                    if (raw == "name")
                    {
                        hasName = true;
                    }

                    if (raw == "numFmtId")
                    {
                        hasNumFmt = true;
                    }

                    sb.Append(' ').Append(raw).Append("=\"").Append(Encoding.UTF8.GetString(a.RawValue)).Append('"');
                }
            }

            if (!hasName)
            {
                sb.Append(" name=\"").Append(PivotXml.Esc(f.Name)).Append('"');
            }

            if (!hasNumFmt)
            {
                sb.Append(" numFmtId=\"").Append(PivotXml.Int(f.NumFmtId)).Append('"');
            }

            if (of is null && !f.IsDatabase)
            {
                sb.Append(" databaseField=\"0\"");
            }

            sb.Append('>');
            if (f.IsDatabase)
            {
                WriteSharedItems(sb, pc, f, ds);
            }
            else if (of is not null && PivotCache.Child(of.Node, "sharedItems") is { IsNull: false } si)
            {
                sb.Append(si.OuterXml);
            }

            if (f.Group is { } g)
            {
                WriteFieldGroup(sb, pc, g, oldToNew, f);
            }

            if (of is not null)
            {
                foreach (MarkupNode c in of.Node.ChildElements)
                {
                    if (!c.LocalName.SequenceEqual("sharedItems"u8) && !c.LocalName.SequenceEqual("fieldGroup"u8))
                    {
                        sb.Append(c.OuterXml);
                    }
                }

                if (f.Group is null && PivotCache.Child(of.Node, "fieldGroup") is { IsNull: false } keep)
                {
                    sb.Append(keep.OuterXml);
                }
            }

            sb.Append("</").Append(pc).Append("cacheField>");
        }

        sb.Append("</").Append(pc).Append("cacheFields>");
        MarkupNode cf = PivotCache.Child(cache.Root, "cacheFields");
        if (cf.IsNull)
        {
            MarkupNode after = PivotCache.Child(cache.Root, "cacheSource");
            if (after.IsNull)
            {
                cache.Root.PrependXml(Encoding.UTF8.GetBytes(sb.ToString()));
            }
            else
            {
                after.InsertXmlAfter(Encoding.UTF8.GetBytes(sb.ToString()));
            }
        }
        else
        {
            cf.ReplaceWithXml(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        cache.Root.SetAttribute("recordCount", PivotXml.Int(data.Records));
        if (options.RefreshedDate is { } when && ExcelDate.TryToSerial(when, ds, out double serial))
        {
            cache.Root.SetAttribute("refreshedDate", PivotXml.Num(serial));
        }

        if (options.RefreshedBy is { } by)
        {
            cache.Root.SetAttribute("refreshedBy", by);
        }

        if (cache.SaveData)
        {
            cache.NewRecords = BuildRecords(wb, data, ds);
        }
    }

    private static OldField? FindOldFor(OldCache old, FieldData f, int dbCount)
    {
        if (f.IsDatabase)
        {
            foreach (OldField o in old.Fields)
            {
                if (o.IsDatabase && string.Equals(o.Name, f.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return o;
                }
            }

            return null;
        }

        int k = f.Index - dbCount;
        int seen = 0;
        foreach (OldField o in old.Fields)
        {
            if (!o.IsDatabase)
            {
                if (seen == k)
                {
                    return o;
                }

                seen++;
            }
        }

        return null;
    }

    internal static string PrefixOf(MarkupNode root) => root.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(root.Prefix) + ":";

    private static void WriteSharedItems(StringBuilder sb, string pc, FieldData f, DateSystem ds)
    {
        bool text = false, num = false, date = false, boolean = false, err = false, blank = false, allInt = true;
        double min = double.MaxValue, max = double.MinValue, dmin = double.MaxValue, dmax = double.MinValue;
        foreach (PivotValue v in f.Values!)
        {
            switch (v.Kind)
            {
                case PivotKind.Text:
                    text = true;
                    break;
                case PivotKind.Number:
                    num = true;
                    allInt &= PivotXml.IsInteger(v.Number);
                    min = Math.Min(min, v.Number);
                    max = Math.Max(max, v.Number);
                    break;
                case PivotKind.Date:
                    date = true;
                    dmin = Math.Min(dmin, v.Number);
                    dmax = Math.Max(dmax, v.Number);
                    break;
                case PivotKind.Boolean:
                    boolean = true;
                    break;
                case PivotKind.Error:
                    err = true;
                    break;
                default:
                    blank = true;
                    break;
            }
        }

        int kinds = (text ? 1 : 0) + (num ? 1 : 0) + (date ? 1 : 0) + (boolean ? 1 : 0) + (err ? 1 : 0);
        sb.Append('<').Append(pc).Append("sharedItems");

        // ECMA-376 Part 1 §18.10.1.90: containsSemiMixedTypes, containsNonDate and containsString default to true; the others to false.
        if (!text && !blank && !err)
        {
            sb.Append(" containsSemiMixedTypes=\"0\"");
        }

        if (date && !text && !num && !boolean && !err)
        {
            sb.Append(" containsNonDate=\"0\"");
        }

        if (date)
        {
            sb.Append(" containsDate=\"1\"");
        }

        if (!text)
        {
            sb.Append(" containsString=\"0\"");
        }

        if (blank)
        {
            sb.Append(" containsBlank=\"1\"");
        }

        if (kinds > 1)
        {
            sb.Append(" containsMixedTypes=\"1\"");
        }

        if (num)
        {
            sb.Append(" containsNumber=\"1\"");
            if (allInt)
            {
                sb.Append(" containsInteger=\"1\"");
            }

            sb.Append(" minValue=\"").Append(PivotXml.Num(min)).Append("\" maxValue=\"").Append(PivotXml.Num(max)).Append('"');
        }

        if (date)
        {
            sb.Append(" minDate=\"").Append(PivotXml.DateText(dmin, ds)).Append("\" maxDate=\"").Append(PivotXml.DateText(dmax, ds)).Append('"');
        }

        if (f.IsShared)
        {
            sb.Append(" count=\"").Append(PivotXml.Int(f.Shared.Count)).Append("\">");
            foreach (PivotValue v in f.Shared)
            {
                sb.Append(PivotXml.Item(pc, v, ds));
            }

            sb.Append("</").Append(pc).Append("sharedItems>");
        }
        else
        {
            sb.Append("/>");
        }
    }

    private static void WriteFieldGroup(StringBuilder sb, string pc, GroupData g, Dictionary<int, int> oldToNew, FieldData f)
    {
        _ = oldToNew;
        sb.Append('<').Append(pc).Append("fieldGroup");
        foreach (MarkupAttributeRef a in g.Node.Attributes)
        {
            string raw = Encoding.UTF8.GetString(a.RawName);
            if (raw == "base")
            {
                continue;
            }

            sb.Append(' ').Append(raw).Append("=\"").Append(Encoding.UTF8.GetString(a.RawValue)).Append('"');
        }

        sb.Append(" base=\"").Append(PivotXml.Int(g.Base)).Append("\">");
        if (g.Discrete)
        {
            sb.Append('<').Append(pc).Append("discretePr count=\"").Append(PivotXml.Int(g.Map.Length)).Append("\">");
            foreach (int m in g.Map)
            {
                sb.Append('<').Append(pc).Append("x v=\"").Append(PivotXml.Int(m)).Append("\"/>");
            }

            sb.Append("</").Append(pc).Append("discretePr>");
        }
        else
        {
            MarkupNode range = PivotCache.Child(g.Node, "rangePr");
            sb.Append('<').Append(pc).Append("rangePr");
            foreach (MarkupAttributeRef a in range.Attributes)
            {
                string raw = Encoding.UTF8.GetString(a.RawName);
                if (raw == g.StartAttrName || raw == g.EndAttrName)
                {
                    continue;
                }

                sb.Append(' ').Append(raw).Append("=\"").Append(Encoding.UTF8.GetString(a.RawValue)).Append('"');
            }

            sb.Append(' ').Append(g.StartAttrName).Append("=\"").Append(g.StartAttr).Append("\" ").Append(g.EndAttrName).Append("=\"").Append(g.EndAttr).Append("\"/>");
        }

        sb.Append('<').Append(pc).Append("groupItems count=\"").Append(PivotXml.Int(g.Labels.Count)).Append("\">");
        foreach (string label in g.Labels)
        {
            sb.Append('<').Append(pc).Append("s v=\"").Append(PivotXml.Esc(label)).Append("\"/>");
        }

        sb.Append("</").Append(pc).Append("groupItems>");
        foreach (MarkupNode c in g.Node.ChildElements)
        {
            if (!c.LocalName.SequenceEqual("rangePr"u8) && !c.LocalName.SequenceEqual("discretePr"u8) && !c.LocalName.SequenceEqual("groupItems"u8))
            {
                sb.Append(c.OuterXml);
            }
        }

        sb.Append("</").Append(pc).Append("fieldGroup>");
        _ = f;
    }

    private static byte[] BuildRecords(Workbook wb, CacheData data, DateSystem ds)
    {
        var sb = new StringBuilder(64 + (data.Records * data.Fields.Count * 16));
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<pivotCacheRecords xmlns=\"").Append(wb.MainNamespace).Append("\" xmlns:r=\"").Append(wb.RelationshipNamespace)
            .Append("\" count=\"").Append(PivotXml.Int(data.Records)).Append("\">");
        for (int r = 0; r < data.Records; r++)
        {
            sb.Append("<r>");
            foreach (FieldData f in data.Fields)
            {
                if (!f.IsDatabase)
                {
                    continue;
                }

                if (f.Item is { } item)
                {
                    sb.Append("<x v=\"").Append(PivotXml.Int(item[r])).Append("\"/>");
                }
                else
                {
                    sb.Append(PivotXml.Item(string.Empty, f.Values![r], ds));
                }
            }

            sb.Append("</r>");
        }

        sb.Append("</pivotCacheRecords>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
