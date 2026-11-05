using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Spreadsheets.Pivots;

internal enum FieldAxis : byte
{
    None = 0,
    Row = 1,
    Col = 2,
    Page = 3,
    Values = 4,
}

internal enum FieldForm : byte
{
    Compact = 0,
    Outline = 1,
    Tabular = 2,
}

/// <summary>One entry of a pivot field's item list (ECMA-376 Part 1 §18.10.1.45, <c>item</c>).</summary>
internal sealed class ItemPlan
{
    /// <summary>Index into the field's axis items, or -1 for an entry that stands for a subtotal (<c>t</c> set).</summary>
    public int Axis = -1;

    /// <summary>The attributes of the stored element other than <c>x</c>, as XML text, kept so hidden flags, captions and the like survive.</summary>
    public string Attrs = string.Empty;

    public bool Hidden;
}

/// <summary>What a table does with one cache field.</summary>
internal sealed class FieldPlan
{
    public int Index;
    public FieldAxis Axis;
    public string SortType = "manual";
    public bool DefaultSubtotal = true;
    public bool SubtotalTop = true;
    public bool Compact = true;
    public bool Outline = true;
    public bool MultipleSelection;
    public string? Caption;
    public readonly List<ItemPlan> Items = [];

    /// <summary>For each axis item, its position in <see cref="Items"/>, or -1 when it is not listed.</summary>
    public int[] PosOfAxis = [];

    public bool HasItems;
    public MarkupNode Node;

    public FieldForm Form => Compact ? FieldForm.Compact : Outline ? FieldForm.Outline : FieldForm.Tabular;
}

internal sealed class PageModel
{
    public int Field;

    /// <summary>The position of the selected item in the field's item list, or -1 for all items.</summary>
    public int Selected = -1;

    public string? Name;
}

internal sealed class DataModel
{
    public string Name = string.Empty;
    public int Field;
    public PivotFunction Function;
    public PivotShowDataAs ShowAs;
    public int BaseField;
    public int BaseItem;
    public int NumFmtId;
}

/// <summary>Reads the pivot table definition into the shape the layout uses, with every field index mapped to the refreshed cache.</summary>
internal sealed class TableModel
{
    public PivotTable Table = null!;
    public readonly List<int> RowFields = [];
    public readonly List<int> ColFields = [];
    public readonly List<PageModel> Pages = [];
    public readonly List<DataModel> Data = [];
    public FieldPlan[] Fields = [];
    public bool RowGrandTotals = true;
    public bool ColGrandTotals = true;
    public bool Compact = true;
    public bool Outline = true;
    public int Indent = 1;
    public string DataCaption = "Values";
    public string GrandCaption = "Grand Total";
    public string? RowHeaderCaption;
    public string? ColHeaderCaption;
    public string? ErrorCaption;
    public string? MissingCaption;
    public bool ShowError;
    public bool ShowMissing = true;
    public bool AddedDataField;
    public bool PageOverThenDown;
    public readonly List<Defect> Defects = [];

    /// <summary>
    /// Reads the table. <paramref name="oldToNew"/> maps stored cache-field indexes to the refreshed cache's; <paramref name="old"/> gives the stored
    /// items' values so hidden flags and order survive a change of the shared items.
    /// </summary>
    public static TableModel Read(PivotTable table, CacheData data, OldCache old, Func<int, int> oldToNew)
    {
        MarkupNode root = table.Root;
        var m = new TableModel
        {
            Table = table,
            RowGrandTotals = PivotXml.Bool(root.GetAttribute("rowGrandTotals"), true),
            ColGrandTotals = PivotXml.Bool(root.GetAttribute("colGrandTotals"), true),
            Compact = PivotXml.Bool(root.GetAttribute("compact"), true),
            Outline = PivotXml.Bool(root.GetAttribute("outline"), true),
            Indent = PivotXml.IntAttr(root.GetAttribute("indent"), 1),
            DataCaption = root.GetAttribute("dataCaption") ?? "Values",
            GrandCaption = root.GetAttribute("grandTotalCaption") ?? "Grand Total",
            RowHeaderCaption = root.GetAttribute("rowHeaderCaption"),
            ColHeaderCaption = root.GetAttribute("colHeaderCaption"),
            ErrorCaption = root.GetAttribute("errorCaption"),
            MissingCaption = root.GetAttribute("missingCaption"),
            ShowError = PivotXml.Bool(root.GetAttribute("showError"), false),
            ShowMissing = PivotXml.Bool(root.GetAttribute("showMissing"), true),
            PageOverThenDown = PivotXml.Bool(root.GetAttribute("pageOverThenDown"), false),
        };

        int n = data.Fields.Count;
        m.Fields = new FieldPlan[n];
        for (int i = 0; i < n; i++)
        {
            m.Fields[i] = new FieldPlan { Index = i };
        }

        foreach (MarkupNode f in PivotCache.Child(root, "rowFields").ChildElements)
        {
            int x = PivotXml.IntAttr(f.GetAttribute("x"), -1);
            m.AddAxisField(m.RowFields, x, oldToNew, n);
        }

        foreach (MarkupNode f in PivotCache.Child(root, "colFields").ChildElements)
        {
            int x = PivotXml.IntAttr(f.GetAttribute("x"), -1);
            m.AddAxisField(m.ColFields, x, oldToNew, n);
        }

        // Stored pivot fields, in stored index order.
        var stored = new List<MarkupNode>();
        foreach (MarkupNode f in PivotCache.Child(root, "pivotFields").ChildElements)
        {
            if (f.LocalName.SequenceEqual("pivotField"u8))
            {
                stored.Add(f);
            }
        }

        var pageNodes = new List<(int Field, MarkupNode Node)>();
        foreach (MarkupNode p in PivotCache.Child(root, "pageFields").ChildElements)
        {
            int f = oldToNew(PivotXml.IntAttr(p.GetAttribute("fld"), -1));
            if (f >= 0 && f < n)
            {
                pageNodes.Add((f, p));
            }
        }

        foreach (int f in m.RowFields)
        {
            if (f >= 0)
            {
                m.Fields[f].Axis = FieldAxis.Row;
            }
        }

        foreach (int f in m.ColFields)
        {
            if (f >= 0)
            {
                m.Fields[f].Axis = FieldAxis.Col;
            }
        }

        foreach ((int f, _) in pageNodes)
        {
            m.Fields[f].Axis = FieldAxis.Page;
        }

        for (int oi = 0; oi < stored.Count; oi++)
        {
            int ni = oldToNew(oi);
            if (ni < 0 || ni >= n)
            {
                continue;
            }

            MarkupNode pf = stored[oi];
            FieldPlan plan = m.Fields[ni];
            plan.Node = pf;
            plan.SortType = pf.GetAttribute("sortType") ?? "manual";
            plan.DefaultSubtotal = PivotXml.Bool(pf.GetAttribute("defaultSubtotal"), true);
            plan.SubtotalTop = PivotXml.Bool(pf.GetAttribute("subtotalTop"), true);
            plan.Compact = PivotXml.Bool(pf.GetAttribute("compact"), true);
            plan.Outline = PivotXml.Bool(pf.GetAttribute("outline"), true);
            plan.MultipleSelection = PivotXml.Bool(pf.GetAttribute("multipleItemSelectionAllowed"), false);
            plan.Caption = pf.GetAttribute("name");
            if (pf.GetAttribute("dataField") is "1" or "true" && plan.Axis == FieldAxis.None)
            {
                plan.Axis = FieldAxis.Values;
            }

            if (plan.Axis is FieldAxis.Row or FieldAxis.Col or FieldAxis.Page)
            {
                BuildItems(plan, pf, data.Fields[ni], old.AxisValues(oi));
            }
            else if (PivotCache.Child(pf, "items") is { IsNull: false })
            {
                BuildItems(plan, pf, data.Fields[ni], old.AxisValues(oi));
            }
            else
            {
                plan.PosOfAxis = [];
            }

            if (pf.GetAttribute("customSubtotals") is not null || pf.GetAttribute("sumSubtotal") is "1" or "true" || pf.GetAttribute("countASubtotal") is "1" or "true")
            {
                m.Defects.Add(new Defect(DefectKind.Unsupported, "pivot.custom-subtotals-unsupported", DefectSeverity.Repaired,
                    "The field '" + data.Fields[ni].Name + "' asks for subtotal functions other than automatic; the automatic subtotal is shown.", DefectLocation.InPart(table.PartName, pf.SourceStart)));
            }
        }

        foreach ((int f, MarkupNode p) in pageNodes)
        {
            var pm = new PageModel { Field = f, Name = p.GetAttribute("name") };
            int item = PivotXml.IntAttr(p.GetAttribute("item"), -1);
            if (item >= 0 && item != 32767)
            {
                // The stored position points into the stored item list; follow the item through the refresh by value.
                pm.Selected = -2;
                int oi = OldIndexOf(oldToNew, f, stored.Count);
                if (oi >= 0 && oi < stored.Count)
                {
                    int ordinal = 0;
                    foreach (MarkupNode it in PivotCache.Child(stored[oi], "items").ChildElements)
                    {
                        if (ordinal++ == item)
                        {
                            int x = PivotXml.IntAttr(it.GetAttribute("x"), -1);
                            List<PivotValue> ov = old.AxisValues(oi);
                            if (x >= 0 && x < ov.Count)
                            {
                                pm.Selected = FindPosByValue(m.Fields[f], data.Fields[f], ov[x]);
                            }

                            break;
                        }
                    }
                }
            }

            m.Pages.Add(pm);
        }

        foreach (MarkupNode d in PivotCache.Child(root, "dataFields").ChildElements)
        {
            int fld = oldToNew(PivotXml.IntAttr(d.GetAttribute("fld"), -1));
            if (fld < 0 || fld >= n)
            {
                m.Defects.Add(new Defect(DefectKind.Reference, "pivot.data-field-dropped", DefectSeverity.Dropped,
                    "A data field refers to a source column that is no longer there; it is dropped.", DefectLocation.InPart(table.PartName, d.SourceStart)));
                continue;
            }

            var dm = new DataModel
            {
                Field = fld,
                Name = d.GetAttribute("name") ?? string.Empty,
                Function = ParseFunction(d.GetAttribute("subtotal")),
                ShowAs = ParseShowAs(d.GetAttribute("showDataAs")),
                BaseField = PivotXml.IntAttr(d.GetAttribute("baseField"), 0) is var bf && bf >= 0 ? oldToNew(bf) : bf,
                BaseItem = PivotXml.IntAttr(d.GetAttribute("baseItem"), 1048832),
                NumFmtId = PivotXml.IntAttr(d.GetAttribute("numFmtId"), 0),
            };
            if (dm.Name.Length == 0)
            {
                dm.Name = FunctionCaption(dm.Function) + data.Fields[fld].Name;
            }

            if (dm.ShowAs is PivotShowDataAs.PercentOfParent or PivotShowDataAs.PercentRunTotal or PivotShowDataAs.RankAscending or PivotShowDataAs.RankDescending)
            {
                m.Defects.Add(new Defect(DefectKind.Unsupported, "pivot.show-data-as-unsupported", DefectSeverity.Repaired,
                    "The data field '" + dm.Name + "' uses a show-values-as form that refresh does not compute; the normal value is shown.", DefectLocation.InPart(table.PartName, d.SourceStart)));
                dm.ShowAs = PivotShowDataAs.Normal;
            }

            m.Data.Add(dm);
        }

        if (PivotCache.Child(root, "filters") is { IsNull: false } filters)
        {
            m.Defects.Add(new Defect(DefectKind.Unsupported, "pivot.filters-not-applied", DefectSeverity.Repaired,
                "The table has label, value or date filters; refresh does not apply them (hidden items and report filters are applied).", DefectLocation.InPart(table.PartName, filters.SourceStart)));
        }

        // A table with several data fields lists them as a field -2 on one axis. Add it on the column axis when the stored definition lacks it.
        if (m.Data.Count > 1 && !m.RowFields.Contains(-2) && !m.ColFields.Contains(-2))
        {
            m.ColFields.Add(-2);
            m.AddedDataField = true;
        }

        return m;
    }

    private static int OldIndexOf(Func<int, int> oldToNew, int newIdx, int storedCount)
    {
        for (int i = 0; i < storedCount; i++)
        {
            if (oldToNew(i) == newIdx)
            {
                return i;
            }
        }

        return -1;
    }

    private void AddAxisField(List<int> list, int x, Func<int, int> oldToNew, int n)
    {
        if (x == -2)
        {
            if (!list.Contains(-2))
            {
                list.Add(-2);
            }

            return;
        }

        int nx = oldToNew(x);
        if (nx < 0 || nx >= n)
        {
            Defects.Add(new Defect(DefectKind.Reference, "pivot.axis-field-dropped", DefectSeverity.Dropped,
                "An axis field refers to a source column that is no longer there; it is dropped.", DefectLocation.InPart(Table.PartName)));
            return;
        }

        list.Add(nx);
    }

    private static int FindPosByValue(FieldPlan plan, FieldData fd, PivotValue v)
    {
        for (int ai = 0; ai < fd.Axis.Count; ai++)
        {
            if (fd.Axis[ai].Equals(v))
            {
                return ai < plan.PosOfAxis.Length ? plan.PosOfAxis[ai] : -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Builds a field's item list: the stored items that still exist (by value, in stored order, with their flags), then the new ones. A field sorted
    /// ascending or descending is ordered afresh. ECMA-376 Part 1 §18.10.1.45 (<c>item</c>), §18.10.1.69 (<c>pivotField sortType</c>).
    /// </summary>
    private static void BuildItems(FieldPlan plan, MarkupNode pf, FieldData fd, List<PivotValue> oldAxis)
    {
        plan.HasItems = true;
        var axisIndex = new Dictionary<PivotValue, int>();
        for (int i = 0; i < fd.Axis.Count; i++)
        {
            axisIndex.TryAdd(fd.Axis[i], i);
        }

        var placed = new bool[fd.Axis.Count];
        var carried = new List<ItemPlan>();
        var trailing = new List<ItemPlan>();
        foreach (MarkupNode it in PivotCache.Child(pf, "items").ChildElements)
        {
            string attrs = AttrText(it, skipX: true);
            string? t = it.GetAttribute("t");
            int x = PivotXml.IntAttr(it.GetAttribute("x"), -1);
            if (t is not null || x < 0)
            {
                trailing.Add(new ItemPlan { Attrs = attrs });
                continue;
            }

            if (x >= oldAxis.Count || !axisIndex.TryGetValue(oldAxis[x], out int ai) || placed[ai])
            {
                continue;
            }

            placed[ai] = true;
            carried.Add(new ItemPlan { Axis = ai, Attrs = attrs, Hidden = PivotXml.Bool(it.GetAttribute("h"), false) });
        }

        var fresh = new List<ItemPlan>();
        for (int i = 0; i < placed.Length; i++)
        {
            if (!placed[i])
            {
                fresh.Add(new ItemPlan { Axis = i });
            }
        }

        bool grouped = fd.Group is not null;
        Comparison<ItemPlan> byValue = (a, b) => grouped ? a.Axis.CompareTo(b.Axis) : PivotValue.Compare(fd.Axis[a.Axis], fd.Axis[b.Axis]);
        switch (plan.SortType)
        {
            case "ascending":
                carried.AddRange(fresh);
                carried.Sort(byValue);
                break;
            case "descending":
                carried.AddRange(fresh);
                carried.Sort((a, b) => byValue(b, a));
                break;
            default:
                fresh.Sort(byValue);
                carried.AddRange(fresh);
                break;
        }

        plan.Items.AddRange(carried);
        if (trailing.Count == 0 && plan.DefaultSubtotal && plan.Axis is FieldAxis.Row or FieldAxis.Col or FieldAxis.Page)
        {
            trailing.Add(new ItemPlan { Attrs = " t=\"default\"" });
        }

        plan.Items.AddRange(trailing);
        plan.PosOfAxis = new int[fd.Axis.Count];
        Array.Fill(plan.PosOfAxis, -1);
        for (int p = 0; p < plan.Items.Count; p++)
        {
            if (plan.Items[p].Axis >= 0)
            {
                plan.PosOfAxis[plan.Items[p].Axis] = p;
            }
        }
    }

    internal static string AttrText(MarkupNode node, bool skipX)
    {
        var sb = new StringBuilder();
        foreach (MarkupAttributeRef a in node.Attributes)
        {
            string raw = Encoding.UTF8.GetString(a.RawName);
            if (skipX && raw == "x")
            {
                continue;
            }

            sb.Append(' ').Append(raw).Append("=\"").Append(Encoding.UTF8.GetString(a.RawValue)).Append('"');
        }

        return sb.ToString();
    }

    public static PivotFunction ParseFunction(string? s) => s switch
    {
        "count" => PivotFunction.Count,
        "average" => PivotFunction.Average,
        "max" => PivotFunction.Max,
        "min" => PivotFunction.Min,
        "product" => PivotFunction.Product,
        "countNums" => PivotFunction.CountNums,
        "stdDev" => PivotFunction.StdDev,
        "stdDevp" => PivotFunction.StdDevP,
        "var" => PivotFunction.Var,
        "varp" => PivotFunction.VarP,
        _ => PivotFunction.Sum,
    };

    public static PivotShowDataAs ParseShowAs(string? s) => s switch
    {
        "difference" => PivotShowDataAs.Difference,
        "percent" => PivotShowDataAs.Percent,
        "percentDiff" => PivotShowDataAs.PercentDiff,
        "runTotal" => PivotShowDataAs.RunTotal,
        "percentOfRow" => PivotShowDataAs.PercentOfRow,
        "percentOfCol" => PivotShowDataAs.PercentOfCol,
        "percentOfTotal" => PivotShowDataAs.PercentOfTotal,
        "index" => PivotShowDataAs.Index,
        "percentOfParentRow" => PivotShowDataAs.PercentOfParentRow,
        "percentOfParentCol" => PivotShowDataAs.PercentOfParentCol,
        "percentOfParent" => PivotShowDataAs.PercentOfParent,
        "percentRunTotal" => PivotShowDataAs.PercentRunTotal,
        "rankAscending" => PivotShowDataAs.RankAscending,
        "rankDescending" => PivotShowDataAs.RankDescending,
        _ => PivotShowDataAs.Normal,
    };

    public static string FunctionCaption(PivotFunction f) => f switch
    {
        PivotFunction.Count => "Count of ",
        PivotFunction.Average => "Average of ",
        PivotFunction.Max => "Max of ",
        PivotFunction.Min => "Min of ",
        PivotFunction.Product => "Product of ",
        PivotFunction.CountNums => "Count of ",
        PivotFunction.StdDev => "StdDev of ",
        PivotFunction.StdDevP => "StdDevp of ",
        PivotFunction.Var => "Var of ",
        PivotFunction.VarP => "Varp of ",
        _ => "Sum of ",
    };
}

/// <summary>Reads parts of a pivot table definition that other code needs.</summary>
internal static class PivotDefinition
{
    public static IReadOnlyList<PivotDataField> ReadDataFields(MarkupNode root)
    {
        var list = new List<PivotDataField>();
        foreach (MarkupNode d in PivotCache.Child(root, "dataFields").ChildElements)
        {
            PivotFunction fn = TableModel.ParseFunction(d.GetAttribute("subtotal"));
            int fld = PivotXml.IntAttr(d.GetAttribute("fld"), -1);
            string name = d.GetAttribute("name") ?? string.Empty;
            list.Add(new PivotDataField(name, fld, fn, TableModel.ParseShowAs(d.GetAttribute("showDataAs")), PivotXml.IntAttr(d.GetAttribute("baseField"), 0),
                PivotXml.IntAttr(d.GetAttribute("baseItem"), 1048832), PivotXml.IntAttr(d.GetAttribute("numFmtId"), 0)));
        }

        return list;
    }
}
