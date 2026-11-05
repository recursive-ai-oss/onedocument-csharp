using System.Linq;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Pivots;

/// <summary>
/// Answers <c>GETPIVOTDATA</c> from what a pivot table shows: its stored row and column items and the cells they point at. Like Excel, it reads the
/// table as last refreshed; edits to the source change the answer only after a refresh.
/// </summary>
internal static class PivotLookup
{
    private sealed class Entry
    {
        public string? Type;
        public int DataIndex;
        public readonly List<int> Xs = [];
    }

    public static CalcValue Get(Workbook wb, string? sheetName, int row, int column, string dataField, IReadOnlyList<KeyValuePair<string, CalcValue>> pairs)
    {
        CalcValue Ref() => CalcValue.FromError(CalcError.Ref);
        if (sheetName is null || !wb.Sheets.TryGet(sheetName, out Worksheet? ws) || ws.Kind != SheetKind.Worksheet)
        {
            return Ref();
        }

        PivotTable? table = ws.PivotTables.Find(new CellAddress(row, column));
        PivotCache? cache = table?.Cache;
        if (table is null || cache is null)
        {
            return Ref();
        }

        CellRange loc = table.Location;
        MarkupNode root = table.Root;
        MarkupNode locNode = PivotCache.Child(root, "location");
        int firstDataRow = PivotXml.IntAttr(locNode.GetAttribute("firstDataRow"), 1);
        int firstDataCol = PivotXml.IntAttr(locNode.GetAttribute("firstDataCol"), 1);
        OldCache oc = OldCache.Read(cache);
        IReadOnlyList<PivotDataField> datas = table.DataFields;

        int d = -1;
        for (int i = 0; i < datas.Count && d < 0; i++)
        {
            if (string.Equals(datas[i].Name, dataField, StringComparison.OrdinalIgnoreCase))
            {
                d = i;
            }
        }

        for (int i = 0; i < datas.Count && d < 0; i++)
        {
            if (datas[i].FieldIndex >= 0 && datas[i].FieldIndex < oc.Fields.Count && string.Equals(oc.Fields[datas[i].FieldIndex].Name, dataField, StringComparison.OrdinalIgnoreCase))
            {
                d = i;
            }
        }

        if (d < 0)
        {
            return Ref();
        }

        IReadOnlyList<int> rowFields = table.RowFields;
        IReadOnlyList<int> colFields = table.ColumnFields;
        if (datas.Count > 1 && !rowFields.Contains(-2) && !colFields.Contains(-2))
        {
            colFields = [.. colFields, -2];
        }

        var rowSpec = new List<(int Field, int Pos)>();
        var colSpec = new List<(int Field, int Pos)>();
        DateSystem ds = wb.DateSystem;
        foreach (KeyValuePair<string, CalcValue> p in pairs)
        {
            int f = oc.Fields.FindIndex(x => x.IsDatabase && string.Equals(x.Name, p.Key, StringComparison.OrdinalIgnoreCase));
            if (f < 0)
            {
                f = oc.Fields.FindIndex(x => string.Equals(x.Name, p.Key, StringComparison.OrdinalIgnoreCase));
            }

            if (f < 0)
            {
                return Ref();
            }

            int pos = PositionOf(table, oc, f, p.Value, ds, out int selectedPos);
            if (rowFields.Contains(f) || colFields.Contains(f))
            {
                if (pos < 0)
                {
                    return Ref();
                }

                (rowFields.Contains(f) ? rowSpec : colSpec).Add((f, pos));
            }
            else if (table.PageFields.Contains(f))
            {
                // A report filter answers only for the item it currently shows.
                if (pos < 0 || selectedPos != pos)
                {
                    return Ref();
                }
            }
            else
            {
                return Ref();
            }
        }

        int rowIdx = FindEntry(Entries(root, "rowItems"), rowFields, rowSpec, d);
        int colIdx = FindEntry(Entries(root, "colItems"), colFields, colSpec, d);
        if (rowIdx < 0 || colIdx < 0)
        {
            return Ref();
        }

        CellValue v = ws[loc.TopLeft.Row + firstDataRow + rowIdx, loc.TopLeft.Column + firstDataCol + colIdx].Value;
        return v.IsEmpty ? Ref() : CalcEngine.ToCalc(ws, v);
    }

    private static List<Entry> Entries(MarkupNode root, string local)
    {
        var list = new List<Entry>();
        Entry? prev = null;
        foreach (MarkupNode i in PivotCache.Child(root, local).ChildElements)
        {
            var e = new Entry { Type = i.GetAttribute("t"), DataIndex = PivotXml.IntAttr(i.GetAttribute("i"), 0) };
            int r = PivotXml.IntAttr(i.GetAttribute("r"), 0);
            if (prev is not null)
            {
                for (int k = 0; k < r && k < prev.Xs.Count; k++)
                {
                    e.Xs.Add(prev.Xs[k]);
                }
            }

            foreach (MarkupNode x in i.ChildElements)
            {
                e.Xs.Add(PivotXml.IntAttr(x.GetAttribute("v"), 0));
            }

            list.Add(e);
            prev = e;
        }

        return list;
    }

    // The entry whose path is the given fields' items (any order among the fields written), or the grand total when none are given.
    private static int FindEntry(List<Entry> entries, IReadOnlyList<int> levels, List<(int Field, int Pos)> spec, int d)
    {
        int dl = levels.ToList().IndexOf(-2);
        int pureCount = levels.Count(l => l >= 0);
        if (entries.Count == 0)
        {
            return pureCount == 0 && spec.Count == 0 ? 0 : -1;
        }

        if (spec.Count == 0)
        {
            if (pureCount == 0)
            {
                return dl >= 0 ? entries.FindIndex(e => e.Xs.Count > 0 && e.Xs[^1] == d) : 0;
            }

            return entries.FindIndex(e => e.Type == "grand" && (dl < 0 || e.DataIndex == d));
        }

        // The specified fields must be the first k pure levels.
        int k = spec.Count;
        var target = new List<int>();
        int seen = 0;
        int end = 0;
        for (int l = 0; l < levels.Count && seen < k; l++)
        {
            if (levels[l] == -2)
            {
                target.Add(d);
                end = l + 1;
                continue;
            }

            int idx = spec.FindIndex(s => s.Field == levels[l]);
            if (idx < 0)
            {
                return -1;
            }

            target.Add(spec[idx].Pos);
            seen++;
            end = l + 1;
        }

        if (seen < k)
        {
            return -1;
        }

        if (dl == end)
        {
            target.Add(d);
        }
        else if (dl > end)
        {
            return -1;
        }

        return entries.FindIndex(e => e.Type != "grand" && e.Xs.Count == target.Count && e.Xs.SequenceEqual(target));
    }

    // The position in the pivot field's item list of the item a value names, and the position the page field currently shows.
    private static int PositionOf(PivotTable table, OldCache oc, int field, CalcValue item, DateSystem ds, out int selected)
    {
        selected = -1;
        MarkupNode pf = default;
        int n = 0;
        foreach (MarkupNode f in PivotCache.Child(table.Root, "pivotFields").ChildElements)
        {
            if (n++ == field)
            {
                pf = f;
                break;
            }
        }

        if (pf.IsNull)
        {
            return -1;
        }

        List<PivotValue> axis = oc.AxisValues(field);
        int pos = -1;
        int ordinal = 0;
        foreach (MarkupNode it in PivotCache.Child(pf, "items").ChildElements)
        {
            int x = PivotXml.IntAttr(it.GetAttribute("x"), -1);
            if (it.GetAttribute("t") is null && x >= 0 && x < axis.Count && pos < 0 && Matches(axis[x], item, ds))
            {
                pos = ordinal;
            }

            ordinal++;
        }

        foreach (MarkupNode p in PivotCache.Child(table.Root, "pageFields").ChildElements)
        {
            if (PivotXml.IntAttr(p.GetAttribute("fld"), -1) == field)
            {
                selected = PivotXml.IntAttr(p.GetAttribute("item"), -1);
            }
        }

        return pos;
    }

    private static bool Matches(PivotValue v, CalcValue item, DateSystem ds)
    {
        switch (item.Kind)
        {
            case ValueKind.Number:
                return v.IsNumeric ? v.Number == item.Number : string.Equals(v.Label(ds), CalcConvert.ToText(item), StringComparison.OrdinalIgnoreCase);
            case ValueKind.Boolean:
                return v.Kind == PivotKind.Boolean && (v.Number != 0) == (item.Number != 0);
            case ValueKind.Text:
                return v.Kind switch
                {
                    PivotKind.Text or PivotKind.Error => string.Equals(v.Text, item.Text, StringComparison.OrdinalIgnoreCase),
                    PivotKind.Blank => item.Text.Length == 0 || string.Equals(item.Text, "(blank)", StringComparison.OrdinalIgnoreCase),
                    _ => string.Equals(v.Label(ds), item.Text, StringComparison.OrdinalIgnoreCase),
                };
            default:
                return false;
        }
    }
}
