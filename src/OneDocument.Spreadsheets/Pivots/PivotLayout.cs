using System.Linq;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Pivots;

/// <summary>A node of an axis tree: one item of one field under its parent.</summary>
internal sealed class AxisNode
{
    public int Id;
    public int Level = -1;
    public int Field = -1;
    public int Pos;
    public int AxisIdx = -1;
    public AxisNode? Parent;
    public readonly List<AxisNode> Kids = [];
    public Dictionary<int, AxisNode>? Lookup;
}

internal enum LineKind : byte
{
    Item,
    Subtotal,
    Grand,
}

internal readonly record struct PathEntry(bool IsData, int V);

/// <summary>One row or column of the rendered table (a row or column of the row-items or column-items list).</summary>
internal sealed class Line
{
    public AxisNode Node = null!;
    public int D = -1;
    public LineKind Kind;
    public List<PathEntry> Path = [];

    /// <summary>True when the line carries values: the data field is decided and the line is not a bare group heading.</summary>
    public bool Valued;

    /// <summary>The axis level (index into the axis's level list) of the item the line stands for; 0 for a grand total.</summary>
    public int Level;

    public string? Caption;

    /// <summary>The levels whose labels this line displays.</summary>
    public List<int> Shown = [];
}

internal sealed class AxisLayout
{
    public int[] Levels = [];
    public int DataLevel = -1;
    public AxisNode Root = null!;
    public int PureCount;
    public readonly List<Line> Lines = [];
}

internal struct Agg
{
    public long Count;
    public long Nums;
    public double Sum;
    public double Min;
    public double Max;
    public double Prod;
    public double Mean;
    public double M2;

    public void Add(PivotValue v)
    {
        if (v.Kind == PivotKind.Blank)
        {
            return;
        }

        Count++;
        if (!v.IsNumeric)
        {
            return;
        }

        double x = v.Number;
        Nums++;
        Sum += x;
        if (Nums == 1)
        {
            Min = Max = Prod = x;
        }
        else
        {
            Min = Math.Min(Min, x);
            Max = Math.Max(Max, x);
            Prod *= x;
        }

        double delta = x - Mean;
        Mean += delta / Nums;
        M2 += delta * (x - Mean);
    }

    /// <summary>The summary a data field shows. ECMA-376 Part 1 §18.18.43 (ST_DataConsolidateFunction); empty groups of average and variance give #DIV/0!, as Excel does.</summary>
    public readonly CellValue Result(PivotFunction f)
    {
        switch (f)
        {
            case PivotFunction.Sum:
                return CellValue.FromNumber(Sum);
            case PivotFunction.Count:
                return CellValue.FromNumber(Count);
            case PivotFunction.CountNums:
                return CellValue.FromNumber(Nums);
            case PivotFunction.Average:
                return Nums > 0 ? CellValue.FromNumber(Sum / Nums) : CellValue.FromError(CellError.DivideByZero);
            case PivotFunction.Max:
                return CellValue.FromNumber(Nums > 0 ? Max : 0);
            case PivotFunction.Min:
                return CellValue.FromNumber(Nums > 0 ? Min : 0);
            case PivotFunction.Product:
                return CellValue.FromNumber(Nums > 0 ? Prod : 0);
            case PivotFunction.Var:
                return Nums > 1 ? CellValue.FromNumber(M2 / (Nums - 1)) : CellValue.FromError(CellError.DivideByZero);
            case PivotFunction.VarP:
                return Nums > 0 ? CellValue.FromNumber(M2 / Nums) : CellValue.FromError(CellError.DivideByZero);
            case PivotFunction.StdDev:
                return Nums > 1 ? CellValue.FromNumber(Math.Sqrt(M2 / (Nums - 1))) : CellValue.FromError(CellError.DivideByZero);
            default:
                return Nums > 0 ? CellValue.FromNumber(Math.Sqrt(M2 / Nums)) : CellValue.FromError(CellError.DivideByZero);
        }
    }
}

internal sealed class Acc
{
    public Agg[] A = [];
    public double[] Refs = [];
}

/// <summary>A pivot calculated field: its formula over the sums of the other fields (ECMA-376 Part 1 §18.10.1.12, <c>cacheField formula</c>).</summary>
internal sealed class CalcField
{
    private readonly FormulaNode _node;
    private readonly Evaluator _evaluator;
    private readonly SlotResolver _resolver;

    private CalcField(FormulaNode node, int[] slots, Workbook wb)
    {
        _node = node;
        Slots = slots;
        _resolver = new SlotResolver(slots.Length);
        _evaluator = new Evaluator(_resolver, null, wb.Limits, new EvaluatorOptions { DateSystem = wb.DateSystem });
    }

    /// <summary>The cache field index each name slot stands for.</summary>
    public int[] Slots { get; }

    /// <summary>Reads the formula. Field names (also in single quotes) become name slots; the evaluator then resolves them to the group's sums.</summary>
    public static CalcField? Parse(string formula, CacheData data, Workbook wb)
    {
        string f = formula.Trim().TrimStart('=');
        var slots = new List<int>();
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < f.Length)
        {
            char c = f[i];
            if (c == '"')
            {
                int j = i + 1;
                while (j < f.Length && (f[j] != '"' || (j + 1 < f.Length && f[j + 1] == '"' && ++j > 0)))
                {
                    j++;
                }

                j = Math.Min(j + 1, f.Length);
                sb.Append(f, i, j - i);
                i = j;
            }
            else if (c == '\'')
            {
                int j = f.IndexOf('\'', i + 1);
                if (j < 0)
                {
                    return null;
                }

                int fi = FieldIndex(data, f[(i + 1)..j]);
                if (fi < 0)
                {
                    return null;
                }

                sb.Append("_pv").Append(Slot(slots, fi));
                i = j + 1;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < f.Length && (char.IsLetterOrDigit(f[j]) || f[j] == '_' || f[j] == '.'))
                {
                    j++;
                }

                string word = f[i..j];
                int fi = j < f.Length && f[j] == '(' ? -1 : FieldIndex(data, word);
                if (fi >= 0)
                {
                    sb.Append("_pv").Append(Slot(slots, fi));
                }
                else
                {
                    sb.Append(word);
                }

                i = j;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        if (!FormulaParser.TryParse(sb.ToString(), out FormulaNode? node, out _, new FormulaParserOptions { Limits = wb.Limits }) || node is null)
        {
            return null;
        }

        return new CalcField(node, [.. slots], wb);
    }

    private static int Slot(List<int> slots, int field)
    {
        int s = slots.IndexOf(field);
        if (s < 0)
        {
            slots.Add(field);
            s = slots.Count - 1;
        }

        return s;
    }

    private static int FieldIndex(CacheData data, string name)
    {
        foreach (FieldData fd in data.Fields)
        {
            if (fd.IsDatabase && string.Equals(fd.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return fd.Index;
            }
        }

        return -1;
    }

    public CellValue Evaluate(ReadOnlySpan<double> sums)
    {
        _resolver.Values = sums.ToArray();
        CalcValue v;
        try
        {
            v = _evaluator.ToScalar(_evaluator.Evaluate(_node, new CalcCell(null, 1, 1)));
        }
        catch (OfficeException)
        {
            return CellValue.FromError(CellError.Value);
        }

        if (v.IsError)
        {
            return CellValue.FromError(v.Error switch
            {
                CalcError.Div0 => CellError.DivideByZero,
                CalcError.Ref => CellError.Reference,
                CalcError.Name => CellError.Name,
                CalcError.Num => CellError.Number,
                CalcError.NA => CellError.NotAvailable,
                CalcError.Null => CellError.Null,
                _ => CellError.Value,
            });
        }

        return v.Kind == ValueKind.Number ? CellValue.FromNumber(v.Number) : v.Kind == ValueKind.Boolean ? CellValue.FromNumber(v.Number) : CellValue.FromError(CellError.Value);
    }

    private sealed class SlotResolver(int count) : ICalcResolver
    {
        public double[] Values = new double[count];

        public CalcValue GetCell(string? sheet, int row, int column) => CalcValue.Blank;

        public CalcValue ResolveName(string? scopeSheet, string name, in CalcCell origin)
        {
            if (name.StartsWith("_pv", StringComparison.Ordinal) && int.TryParse(name.AsSpan(3), out int slot) && slot < Values.Length)
            {
                return CalcValue.FromNumber(Values[slot]);
            }

            return CalcValue.FromError(CalcError.Name);
        }
    }
}

/// <summary>The computed table: the two axes, the accumulated groups and the way a cell's value is derived.</summary>
internal sealed class PivotLayout
{
    private readonly TableModel _model;
    private readonly CacheData _data;
    private readonly Dictionary<long, Acc> _accs = [];
    private readonly CalcField?[] _calc;
    private readonly int[] _refFields;
    private int _colNodeCount;
    private readonly DateSystem _dateSystem;

    public AxisLayout Rows = null!;
    public AxisLayout Cols = null!;
    public int Included;

    private PivotLayout(TableModel model, CacheData data, Workbook wb)
    {
        _model = model;
        _data = data;
        _dateSystem = wb.DateSystem;
        _calc = new CalcField?[model.Data.Count];
        var refs = new List<int>();
        for (int k = 0; k < model.Data.Count; k++)
        {
            FieldData fd = data.Fields[model.Data[k].Field];
            if (!fd.IsDatabase && fd.Formula is { } formula)
            {
                CalcField? cf = CalcField.Parse(formula, data, wb);
                _calc[k] = cf;
                if (cf is null)
                {
                    model.Defects.Add(new Defect(DefectKind.Formula, "pivot.calculated-field-unparsable", DefectSeverity.Dropped,
                        "The calculated field '" + fd.Name + "' has a formula that cannot be read; its cells show #VALUE!.", DefectLocation.InPart(model.Table.PartName)));
                }
                else
                {
                    foreach (int s in cf.Slots)
                    {
                        if (!refs.Contains(s))
                        {
                            refs.Add(s);
                        }
                    }
                }
            }
        }

        _refFields = [.. refs];
    }

    public static PivotLayout Build(TableModel model, CacheData data, Workbook wb, CancellationToken ct)
    {
        var layout = new PivotLayout(model, data, wb);
        layout.Run(ct);
        return layout;
    }

    private static int[] Pure(int[] levels) => [.. levels.Where(l => l >= 0)];

    private void Run(CancellationToken ct)
    {
        int[] rowLevels = [.. _model.RowFields];
        int[] colLevels = [.. _model.ColFields];
        int[] rowPure = Pure(rowLevels);
        int[] colPure = Pure(colLevels);
        var included = new bool[_data.Records];
        for (int r = 0; r < included.Length; r++)
        {
            included[r] = Passes(r);
        }

        AxisNode rowRoot = BuildTree(rowPure, included, out AxisNode[] rowLeaf, ct, out int rowCount);
        AxisNode colRoot = BuildTree(colPure, included, out AxisNode[] colLeaf, ct, out int colCount);
        _colNodeCount = colCount;
        Rows = Generate(rowLevels, rowRoot, true);
        Cols = Generate(colLevels, colRoot, false);
        AccumulateAll(included, rowLeaf, colLeaf, ct);
        _ = rowCount;
    }

    private bool Passes(int rec)
    {
        foreach (FieldPlan fp in _model.Fields)
        {
            if (fp.Axis is not (FieldAxis.Row or FieldAxis.Col or FieldAxis.Page) || !fp.HasItems)
            {
                continue;
            }

            FieldData fd = _data.Fields[fp.Index];
            int ai = fd.AxisItem is { } a ? a[rec] : -1;
            if (ai < 0)
            {
                return false;
            }

            int pos = ai < fp.PosOfAxis.Length ? fp.PosOfAxis[ai] : -1;
            if (fp.Axis == FieldAxis.Page)
            {
                PageModel? pm = _model.Pages.Find(p => p.Field == fp.Index);
                if (pm is { Selected: >= 0 } && pos != pm.Selected)
                {
                    return false;
                }

                if (pm is { Selected: < 0 } && fp.MultipleSelection && pos >= 0 && fp.Items[pos].Hidden)
                {
                    return false;
                }

                continue;
            }

            if (pos < 0 || fp.Items[pos].Hidden)
            {
                return false;
            }
        }

        return true;
    }

    private AxisNode BuildTree(int[] pure, bool[] included, out AxisNode[] leaf, CancellationToken ct, out int count)
    {
        var root = new AxisNode { Id = 0 };
        int id = 1;
        leaf = new AxisNode[_data.Records];
        for (int r = 0; r < leaf.Length; r++)
        {
            if ((r & 4095) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (!included[r])
            {
                continue;
            }

            AxisNode node = root;
            for (int l = 0; l < pure.Length; l++)
            {
                FieldData fd = _data.Fields[pure[l]];
                FieldPlan fp = _model.Fields[pure[l]];
                int ai = fd.AxisItem![r];
                int pos = fp.PosOfAxis[ai];
                node.Lookup ??= [];
                if (!node.Lookup.TryGetValue(pos, out AxisNode? child))
                {
                    child = new AxisNode { Id = id++, Level = l, Field = pure[l], Pos = pos, AxisIdx = ai, Parent = node };
                    node.Lookup[pos] = child;
                    node.Kids.Add(child);
                }

                node = child;
            }

            leaf[r] = node;
        }

        Sort(root);
        count = id;
        return root;
    }

    private static void Sort(AxisNode n)
    {
        n.Kids.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        foreach (AxisNode k in n.Kids)
        {
            Sort(k);
        }
    }

    private AxisLayout Generate(int[] levels, AxisNode root, bool rowAxis)
    {
        var a = new AxisLayout { Levels = levels, Root = root, DataLevel = Array.IndexOf(levels, -2), PureCount = levels.Count(l => l >= 0) };
        int nd = _model.Data.Count;
        bool grand = rowAxis ? _model.ColGrandTotals : _model.RowGrandTotals;
        if (levels.Length == 0)
        {
            a.Lines.Add(new Line { Node = root, D = -1, Kind = LineKind.Item, Valued = true, Level = 0 });
        }
        else
        {
            Gen(a, root, 0, -1, [], rowAxis);
        }

        if (grand && a.PureCount > 0)
        {
            if (a.DataLevel >= 0 && nd > 0)
            {
                for (int d = 0; d < nd; d++)
                {
                    a.Lines.Add(new Line { Node = root, D = d, Kind = LineKind.Grand, Path = [new PathEntry(true, d)], Valued = true, Level = 0, Caption = nd > 1 ? "Total " + _model.Data[d].Name : _model.GrandCaption });
                }
            }
            else
            {
                a.Lines.Add(new Line { Node = root, D = -1, Kind = LineKind.Grand, Path = [new PathEntry(false, 0)], Valued = true, Level = 0, Caption = _model.GrandCaption });
            }
        }

        MarkShown(a, rowAxis);
        return a;
    }

    internal FieldForm FormOf(AxisLayout a, int li, bool rowAxis)
    {
        if (!rowAxis)
        {
            return FieldForm.Tabular;
        }

        if (a.Levels[li] == -2)
        {
            return _model.Compact ? FieldForm.Compact : _model.Outline ? FieldForm.Outline : FieldForm.Tabular;
        }

        return _model.Fields[a.Levels[li]].Form;
    }

    private static bool HasPure(AxisLayout a, int from)
    {
        for (int i = from; i < a.Levels.Length; i++)
        {
            if (a.Levels[i] >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void Gen(AxisLayout a, AxisNode node, int li, int d, List<PathEntry> path, bool rowAxis)
    {
        if (li >= a.Levels.Length)
        {
            return;
        }

        int nd = _model.Data.Count;
        bool last = li == a.Levels.Length - 1;
        FieldForm form = FormOf(a, li, rowAxis);
        if (a.Levels[li] == -2)
        {
            for (int dd = 0; dd < nd; dd++)
            {
                var p2 = new List<PathEntry>(path) { new(true, dd) };
                bool deeper = HasPure(a, li + 1);
                if (form != FieldForm.Tabular || last)
                {
                    a.Lines.Add(new Line { Node = node, D = dd, Kind = LineKind.Item, Path = p2, Valued = true, Level = li });
                }

                Gen(a, node, li + 1, dd, p2, rowAxis);
                _ = deeper;
            }

            return;
        }

        FieldPlan fp = _model.Fields[a.Levels[li]];
        bool complete = a.DataLevel < 0 || a.DataLevel < li;
        bool deeperPure = HasPure(a, li + 1);
        foreach (AxisNode child in node.Kids)
        {
            var p2 = new List<PathEntry>(path) { new(false, child.Pos) };
            bool hasSub = deeperPure && complete && fp.DefaultSubtotal;
            bool valued = complete && (!deeperPure || (form != FieldForm.Tabular && fp.SubtotalTop && fp.DefaultSubtotal));
            if (form != FieldForm.Tabular || last)
            {
                a.Lines.Add(new Line { Node = child, D = d, Kind = LineKind.Item, Path = p2, Valued = valued, Level = li });
            }

            Gen(a, child, li + 1, d, p2, rowAxis);
            if (hasSub && (form == FieldForm.Tabular || !fp.SubtotalTop))
            {
                a.Lines.Add(new Line { Node = child, D = d, Kind = LineKind.Subtotal, Path = p2, Valued = true, Level = li, Caption = LabelOf(child).Label(_dateSystem) + " Total" });
            }
        }
    }

    public PivotValue LabelOf(AxisNode n) => _data.Fields[n.Field].Axis[n.AxisIdx];

    // A line shows the label of its own item; in a tabular field (and always on the column axis) a parent's label shows on the first line under it.
    private void MarkShown(AxisLayout a, bool rowAxis)
    {
        Line? prev = null;
        foreach (Line l in a.Lines)
        {
            if (l.Kind == LineKind.Grand)
            {
                l.Shown.Add(0);
                prev = null;
                continue;
            }

            if (l.Kind == LineKind.Subtotal)
            {
                l.Shown.Add(l.Level);
                prev = null;
                continue;
            }

            int own = l.Path.Count - 1;
            for (int lv = 0; lv < own; lv++)
            {
                bool repeated = prev is { Kind: LineKind.Item } && prev.Path.Count > lv && SamePrefix(prev.Path, l.Path, lv);
                if (FormOf(a, lv, rowAxis) == FieldForm.Tabular && !repeated)
                {
                    l.Shown.Add(lv);
                }
            }

            if (own >= 0)
            {
                l.Shown.Add(own);
            }

            prev = l;
        }
    }

    private static bool SamePrefix(List<PathEntry> a, List<PathEntry> b, int through)
    {
        for (int i = 0; i <= through; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private void AccumulateAll(bool[] included, AxisNode[] rowLeaf, AxisNode[] colLeaf, CancellationToken ct)
    {
        int nd = _model.Data.Count;
        var rowChain = new List<AxisNode>();
        var colChain = new List<AxisNode>();
        for (int r = 0; r < included.Length; r++)
        {
            if (!included[r])
            {
                continue;
            }

            if ((r & 4095) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            Included++;
            Chain(rowLeaf[r], rowChain);
            Chain(colLeaf[r], colChain);
            foreach (AxisNode rn in rowChain)
            {
                foreach (AxisNode cn in colChain)
                {
                    long key = ((long)rn.Id * (_colNodeCount + 1)) + cn.Id;
                    if (!_accs.TryGetValue(key, out Acc? acc))
                    {
                        acc = new Acc { A = new Agg[nd], Refs = new double[_refFields.Length] };
                        _accs[key] = acc;
                    }

                    for (int k = 0; k < nd; k++)
                    {
                        FieldData fd = _data.Fields[_model.Data[k].Field];
                        if (fd.IsDatabase)
                        {
                            acc.A[k].Add(fd.Values![r]);
                        }
                    }

                    for (int s = 0; s < _refFields.Length; s++)
                    {
                        PivotValue v = _data.Fields[_refFields[s]].Values![r];
                        if (v.IsNumeric)
                        {
                            acc.Refs[s] += v.Number;
                        }
                    }
                }
            }
        }
    }

    private static void Chain(AxisNode leaf, List<AxisNode> chain)
    {
        chain.Clear();
        for (AxisNode? n = leaf; n is not null; n = n.Parent)
        {
            chain.Add(n);
        }
    }

    private Acc? Get(AxisNode r, AxisNode c) => _accs.TryGetValue(((long)r.Id * (_colNodeCount + 1)) + c.Id, out Acc? a) ? a : null;

    /// <summary>The value a cell shows, or empty when the row and column have no records in common or the line carries no values.</summary>
    public CellValue Cell(Line row, Line col)
    {
        if (!row.Valued || !col.Valued)
        {
            return default;
        }

        int d = DataIndex(row, col);
        if (d < 0)
        {
            return default;
        }

        return Shown(row.Node, col.Node, d);
    }

    /// <summary>The data field a cell of this row and column shows: the one the row or column picks, else the only one.</summary>
    public int DataIndex(Line row, Line col) => row.D >= 0 ? row.D : col.D >= 0 ? col.D : _model.Data.Count == 1 ? 0 : -1;

    private CellValue Raw(AxisNode r, AxisNode c, int d)
    {
        Acc? acc = Get(r, c);
        if (acc is null)
        {
            return default;
        }

        if (_calc[d] is { } cf)
        {
            var sums = new double[cf.Slots.Length];
            for (int i = 0; i < sums.Length; i++)
            {
                sums[i] = acc.Refs[Array.IndexOf(_refFields, cf.Slots[i])];
            }

            return cf.Evaluate(sums);
        }

        if (!_data.Fields[_model.Data[d].Field].IsDatabase)
        {
            return CellValue.FromError(CellError.Value);
        }

        return acc.A[d].Result(_model.Data[d].Function);
    }

    private static CellValue Divide(CellValue a, CellValue b)
    {
        if (a.Kind == CellValueKind.Error)
        {
            return a;
        }

        if (b.Kind == CellValueKind.Error)
        {
            return b;
        }

        if (a.Kind != CellValueKind.Number || b.Kind != CellValueKind.Number)
        {
            return default;
        }

        return b.Number == 0 ? CellValue.FromError(CellError.DivideByZero) : CellValue.FromNumber(a.Number / b.Number);
    }

    private CellValue Shown(AxisNode r, AxisNode c, int d)
    {
        DataModel dm = _model.Data[d];
        CellValue raw = Raw(r, c, d);
        if (raw.IsEmpty)
        {
            return raw;
        }

        AxisNode rr = Rows.Root;
        AxisNode cr = Cols.Root;
        switch (dm.ShowAs)
        {
            case PivotShowDataAs.Normal:
                return raw;
            case PivotShowDataAs.PercentOfTotal:
                return Divide(raw, Raw(rr, cr, d));
            case PivotShowDataAs.PercentOfRow:
                return Divide(raw, Raw(r, cr, d));
            case PivotShowDataAs.PercentOfCol:
                return Divide(raw, Raw(rr, c, d));
            case PivotShowDataAs.PercentOfParentRow:
                return Divide(raw, Raw(r.Parent ?? r, c, d));
            case PivotShowDataAs.PercentOfParentCol:
                return Divide(raw, Raw(r, c.Parent ?? c, d));
            case PivotShowDataAs.Index:
                {
                    CellValue grand = Raw(rr, cr, d);
                    CellValue rowTotal = Raw(r, cr, d);
                    CellValue colTotal = Raw(rr, c, d);
                    if (raw.Kind != CellValueKind.Number || grand.Kind != CellValueKind.Number || rowTotal.Kind != CellValueKind.Number || colTotal.Kind != CellValueKind.Number)
                    {
                        return CellValue.FromError(CellError.DivideByZero);
                    }

                    double den = rowTotal.Number * colTotal.Number;
                    return den == 0 ? CellValue.FromError(CellError.DivideByZero) : CellValue.FromNumber(raw.Number * grand.Number / den);
                }

            default:
                return Relative(r, c, d, dm, raw);
        }
    }

    // Difference, percent, percent difference and running total are taken along the base field, on whichever axis holds it, at the base field's level only.
    private CellValue Relative(AxisNode r, AxisNode c, int d, DataModel dm, CellValue raw)
    {
        bool onRows = _model.RowFields.Contains(dm.BaseField);
        bool onCols = !onRows && _model.ColFields.Contains(dm.BaseField);
        if (!onRows && !onCols)
        {
            return default;
        }

        AxisNode self = onRows ? r : c;
        if (self.Field != dm.BaseField)
        {
            return default;
        }

        List<AxisNode> sibs = (self.Parent ?? (onRows ? Rows.Root : Cols.Root)).Kids;
        int at = sibs.IndexOf(self);
        CellValue At(AxisNode s) => onRows ? Raw(s, c, d) : Raw(r, s, d);
        if (dm.ShowAs == PivotShowDataAs.RunTotal)
        {
            double sum = 0;
            for (int i = 0; i <= at; i++)
            {
                CellValue v = At(sibs[i]);
                if (v.Kind == CellValueKind.Error)
                {
                    return v;
                }

                sum += v.Kind == CellValueKind.Number ? v.Number : 0;
            }

            return CellValue.FromNumber(sum);
        }

        AxisNode? baseNode = null;
        if (dm.BaseItem == 1048828)
        {
            baseNode = at > 0 ? sibs[at - 1] : null;
        }
        else if (dm.BaseItem == 1048829)
        {
            baseNode = at + 1 < sibs.Count ? sibs[at + 1] : null;
        }
        else
        {
            foreach (AxisNode s in sibs)
            {
                if (s.Pos == dm.BaseItem)
                {
                    baseNode = s;
                }
            }
        }

        if (baseNode is null || ReferenceEquals(baseNode, self))
        {
            return default;
        }

        CellValue b = At(baseNode);
        if (b.Kind != CellValueKind.Number || raw.Kind != CellValueKind.Number)
        {
            return b.Kind == CellValueKind.Error ? b : default;
        }

        return dm.ShowAs switch
        {
            PivotShowDataAs.Difference => CellValue.FromNumber(raw.Number - b.Number),
            PivotShowDataAs.Percent => Divide(raw, b),
            _ => Divide(CellValue.FromNumber(raw.Number - b.Number), b),
        };
    }
}
