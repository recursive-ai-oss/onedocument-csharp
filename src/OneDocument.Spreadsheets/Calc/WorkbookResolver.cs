using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

/// <summary>The state one thread needs to evaluate formulas: its own evaluators (an evaluator is not reentrant) and, for the sequential phase, the formulas it is in the middle of.</summary>
internal sealed class CalcWorker
{
    [ThreadStatic]
    private static CalcWorker? t_current;

    private readonly CalcEngine _engine;
    private readonly List<Evaluator> _nameEvaluators = [];

    public CalcWorker(CalcEngine engine)
    {
        _engine = engine;
        Legacy = engine.NewEvaluator(legacy: true);
        Dynamic = engine.NewEvaluator(legacy: false);
    }

    /// <summary>The worker running on this thread, if any.</summary>
    public static CalcWorker? Current
    {
        get => t_current;
        set => t_current = value;
    }

    /// <summary>Evaluates formulas with implicit intersection (a plain formula cell).</summary>
    public Evaluator Legacy { get; }

    /// <summary>Evaluates array formulas (dynamic arrays and legacy array formulas).</summary>
    public Evaluator Dynamic { get; }

    /// <summary>True in the sequential phase, where reading a formula that is not yet calculated calculates it first.</summary>
    public bool InlineAllowed { get; set; }

    /// <summary>Set when the formula being evaluated called a web function that was denied.</summary>
    public bool ServiceDenied { get; set; }

    /// <summary>The formulas being evaluated inline, outermost first.</summary>
    public List<int> Stack { get; } = [];

    /// <summary>Formulas found to be part of a circular reference while inline.</summary>
    public HashSet<int> Circular { get; } = [];

    /// <summary>How deeply names are being evaluated inside each other.</summary>
    public int NameDepth { get; set; }

    public Evaluator NameEvaluator(int depth)
    {
        while (_nameEvaluators.Count <= depth)
        {
            _nameEvaluators.Add(_engine.NewEvaluator(legacy: false));
        }

        return _nameEvaluators[depth];
    }
}

/// <summary>The <see cref="ICalcResolver"/> over a workbook's cell store, with names, tables, 3-D references and spill ranges.</summary>
internal sealed class WorkbookResolver(CalcEngine engine) : ICalcResolver
{
    private const int MaxNameDepth = 16;

    private CalcBook Book => engine.Book;

    public CalcValue GetPivotData(string dataField, string? sheet, int row, int column, IReadOnlyList<KeyValuePair<string, CalcValue>> items) =>
        Pivots.PivotLookup.Get(engine.Workbook, sheet, row, column, dataField, items);

    public CalcValue GetCell(string? sheet, int row, int column)
    {
        int si = Book.FindSheet(sheet, 0);
        if (si < 0 || row < 1 || row > CellArea.MaxRow || column < 1 || column > CellArea.MaxColumn)
        {
            return CalcValue.FromError(CalcError.Ref);
        }

        if (engine.TryGetOverlay(si, row, column, out CalcValue overlaid))
        {
            return overlaid;
        }

        Worksheet ws = Book.Sheets[si];
        if (ws.Kind != SheetKind.Worksheet)
        {
            return CalcValue.Blank;
        }

        CellStore store = ws.Store;
        if (!store.TryGetCellWithFormula(row, column, out CellValue v, out bool hasFormula))
        {
            return CalcValue.Blank;
        }

        if (hasFormula && CalcWorker.Current is { InlineAllowed: true } w && engine.ReadInline(w, si, row, column))
        {
            store.TryGetCellWithFormula(row, column, out v, out _);
        }

        return CalcEngine.ToCalc(ws, v);
    }

    public CalcArray GetRange(in CellArea area)
    {
        int si = Book.FindSheet(area.Sheet, 0);
        if (si < 0)
        {
            return CalcArray.Broadcast(area.Rows, area.Columns, CalcValue.FromError(CalcError.Ref));
        }

        Worksheet ws = Book.Sheets[si];
        if (ws.Kind != SheetKind.Worksheet)
        {
            return CalcArray.Broadcast(area.Rows, area.Columns, CalcValue.Blank);
        }

        long count = (long)area.Rows * area.Columns;
        if (count > int.MaxValue / 2)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.MaxAllocationBytes), int.MaxValue, count);
        }

        CellStore store = ws.Store;
        var cells = new CalcValue[(int)count];
        List<RowData> rows = store.Rows;
        int idx = store.FindRowIndex(area.Row1);
        if (idx < 0)
        {
            idx = ~idx;
        }

        CalcWorker? w = CalcWorker.Current is { InlineAllowed: true } cw ? cw : null;
        for (; idx < rows.Count && rows[idx].Row <= area.Row2; idx++)
        {
            RowData r = rows[idx];
            int start = r.Find(area.Column1 - 1);
            if (start < 0)
            {
                start = ~start;
            }

            for (int i = start; i < r.Count && r.Cols[i] + 1 <= area.Column2; i++)
            {
                int col = r.Cols[i] + 1;
                if (w is not null && (r.Kinds[i] & RowData.HasFormulaBit) != 0)
                {
                    engine.ReadInline(w, si, r.Row, col);
                }

                CellValue v = RowData.Decode(r.Kinds[i], r.Data[i]);
                cells[((r.Row - area.Row1) * area.Columns) + (col - area.Column1)] = CalcEngine.ToCalc(ws, v);
            }
        }

        engine.ApplyOverlay(si, area, cells);
        return new CalcArray(area.Rows, area.Columns, cells);
    }

    public CellArea ClipArea(in CellArea area)
    {
        int si = Book.FindSheet(area.Sheet, -1);
        if (si < 0 || Book.Sheets[si].Kind != SheetKind.Worksheet)
        {
            return area;
        }

        engine.UsedExtent(si, out int lastRow, out int lastColumn);
        int r2 = area.Rows >= CellArea.MaxRow ? Math.Max(area.Row1, Math.Min(area.Row2, lastRow)) : area.Row2;
        int c2 = area.Columns >= CellArea.MaxColumn ? Math.Max(area.Column1, Math.Min(area.Column2, lastColumn)) : area.Column2;
        return area with { Row2 = r2, Column2 = c2 };
    }

    public bool TryExpandSheets(string first, string last, out IReadOnlyList<string> sheets)
    {
        int a = Book.FindSheet(first, -1);
        int b = Book.FindSheet(last, -1);
        if (a < 0 || b < 0)
        {
            sheets = [];
            return false;
        }

        var list = new List<string>();
        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
        {
            list.Add(Book.Sheets[i].Name);
        }

        sheets = list;
        return true;
    }

    public CalcValue ResolveName(string? scopeSheet, string name, in CalcCell origin) => ResolveNameCore(scopeSheet, name, origin);

    private CalcValue ResolveNameCore(string? scopeSheet, string name, CalcCell origin)
    {
        int osi = Book.FindSheet(origin.Sheet, 0);
        int scope = scopeSheet is null ? osi : Book.FindSheet(scopeSheet, -1);
        NameInfo? info = Book.FindName(name, scope);
        if (info?.GetTree(Book) is not { } tree)
        {
            return CalcValue.FromError(CalcError.Name);
        }

        if (TryAsReference(tree, osi, out CalcReference? reference))
        {
            return CalcValue.FromReference(reference!);
        }

        CalcWorker w = CalcWorker.Current ?? engine.TemporaryWorker();
        if (w.NameDepth >= MaxNameDepth)
        {
            return CalcValue.FromError(CalcError.Ref);
        }

        w.NameDepth++;
        try
        {
            return w.NameEvaluator(w.NameDepth).Evaluate(tree, new CalcCell(origin.Sheet, origin.Row, origin.Column));
        }
        finally
        {
            w.NameDepth--;
        }
    }

    private bool TryAsReference(FormulaNode tree, int sheet, out CalcReference? reference)
    {
        reference = null;
        while (tree is ParenNode p)
        {
            tree = p.Inner;
        }

        string? Sheet(SheetPrefix? prefix) => prefix?.Sheet ?? Book.Sheets[Math.Max(sheet, 0)].Name;
        if (tree is CellReferenceNode c && c.Prefix?.Workbook is null)
        {
            reference = new CalcReference([new CellArea(Sheet(c.Prefix), c.Prefix?.LastSheet, c.Row.Index, c.Column.Index, c.Row.Index, c.Column.Index)]);
            return true;
        }

        if (tree is AreaReferenceNode a && a.Prefix?.Workbook is null)
        {
            int r1 = a.AreaKind == AreaKind.Columns ? 1 : a.Row1.Index;
            int r2 = a.AreaKind == AreaKind.Columns ? CellArea.MaxRow : a.Row2.Index;
            int c1 = a.AreaKind == AreaKind.Rows ? 1 : a.Column1.Index;
            int c2 = a.AreaKind == AreaKind.Rows ? CellArea.MaxColumn : a.Column2.Index;
            reference = new CalcReference([new CellArea(Sheet(a.Prefix), a.Prefix?.LastSheet, Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2))]);
            return true;
        }

        return false;
    }

    public CalcValue ResolveStructured(StructuredReferenceNode node, in CalcCell origin)
    {
        int osi = Book.FindSheet(origin.Sheet, 0);
        CalcError e = StructuredRefs.Resolve(Book, node, osi, origin.Row, origin.Column, out CellArea[] areas);
        return e != CalcError.None ? CalcValue.FromError(e) : CalcValue.FromReference(new CalcReference(areas));
    }

    public CalcValue ResolveExternal(SheetPrefix prefix, FormulaNode node) => engine.ReadExternal(prefix, node);

    public CalcValue ResolveWebService(string url) => engine.WebService(url);

    public CalcValue ResolveFilterXml(string xml, string xpath) => engine.FilterXml(xml, xpath);

    public bool TryGetSpill(string? sheet, int row, int column, out CellArea area) => engine.TryGetSpill(Book.FindSheet(sheet, 0), row, column, out area);
}
