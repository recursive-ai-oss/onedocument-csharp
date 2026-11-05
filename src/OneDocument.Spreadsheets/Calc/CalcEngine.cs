using System.Collections.Concurrent;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

/// <summary>
/// Calculates a workbook. It keeps a dependency graph between calls, watches edits made through the cell API, and on request calculates
/// the formulas those edits made dirty, level by level: formulas on one level do not read each other, so a level runs on all the cores
/// and the answers do not depend on how many there are.
/// </summary>
internal sealed partial class CalcEngine
{
    private const byte Clean = 0;
    private const byte Dirty = 1;
    private const byte Computed = 2;
    private const byte Fired = 3;
    private const byte Running = 4;
    private const int MaxInlineDepth = 200;
    private const int MaxPasses = 8;

    private static readonly Lazy<FunctionRegistry> DefaultRegistry = new(FunctionRegistry.CreateDefault);

    // Evaluate never returns this (it turns every blank into 0), so it can stand for "leave the cell as it is".
    private static readonly CalcValue NoChange = CalcValue.Missing;

    private readonly Workbook _wb;
    private readonly object _logGate = new();
    private readonly List<(int Sheet, int Row, int Column)> _log = [];
    private readonly ConcurrentBag<CalcWorker> _workers = [];
    private readonly Dictionary<int, CellArea> _spill = [];
    private readonly HashSet<int> _blocked = [];
    private bool _structural;
    private bool _forceFull;
    private bool _suppress;
    private string _signature = string.Empty;
    private FunctionRegistry _registry = DefaultRegistry.Value;
    private CalcGraph? _graph;
    private WorkbookResolver? _resolver;
    private byte[] _state = [];
    private int[] _pending = [];
    private int[] _lastRow = [];
    private int[] _lastColumn = [];
    private RecalcResult _result = new();
    private List<(int, int, int)> _spillChanged = [];
    private int _evaluated;
    private IExternalWorkbookResolver? _externalResolver;
    private IWebServiceResolver? _webResolver;
    private ExternalBooks _books = null!;

    public CalcEngine(Workbook workbook) => _wb = workbook;

    public Workbook Workbook => _wb;

    public CalcBook Book { get; private set; } = null!;

    public Evaluator NewEvaluator(bool legacy) =>
        new(_resolver!, _registry, _wb.Limits, new EvaluatorOptions { LegacyImplicitIntersection = legacy, DateSystem = _wb.DateSystem });

    public CalcWorker TemporaryWorker() => new(this);

    private CalcWorker? _adHoc;

    // Evaluates one formula on the cells as they are, without a graph: the resolver reads stored values, and a formula cell that is not
    // being calculated is never calculated here (InlineAllowed stays false on the worker).
    public CalcValue EvaluateAdHoc(string formula, string sheetName, int row, int column, int originRow, int originColumn)
    {
        if (_resolver is null)
        {
            Book = new CalcBook(_wb);
            _resolver = new WorkbookResolver(this);
            _books ??= new ExternalBooks(_wb, _externalResolver);
            _adHoc = null;
        }

        _adHoc ??= TemporaryWorker();
        if (!FormulaParser.TryParse(formula, out FormulaNode? tree, out _, new FormulaParserOptions { Limits = _wb.Limits }) || tree is null)
        {
            return CalcValue.FromError(CalcError.Name);
        }

        if (row != originRow || column != originColumn)
        {
            tree = FormulaTranslator.Translate(tree, row - originRow, column - originColumn);
        }

        return _adHoc.Dynamic.Evaluate(tree, new CalcCell(sheetName, row, column));
    }

    // ---- value conversion ---------------------------------------------------------------------------------------------------

    public static CalcValue ToCalc(Worksheet ws, in CellValue v) => v.Kind switch
    {
        CellValueKind.Number => CalcValue.FromNumber(v.Number),
        CellValueKind.Boolean => CalcValue.FromBoolean(v.Boolean),
        CellValueKind.Text => CalcValue.FromText(ws.GetText(v)),
        CellValueKind.Error => CalcValue.FromError(ToCalcError(v.Error)),
        _ => CalcValue.Blank,
    };

    private static CalcError ToCalcError(CellError e) => e switch
    {
        CellError.Null => CalcError.Null,
        CellError.DivideByZero => CalcError.Div0,
        CellError.Value => CalcError.Value,
        CellError.Reference => CalcError.Ref,
        CellError.Name => CalcError.Name,
        CellError.Number => CalcError.Num,
        CellError.NotAvailable => CalcError.NA,
        CellError.GettingData => CalcError.GettingData,
        CellError.Spill => CalcError.Spill,
        CellError.Calc => CalcError.Calc,
        CellError.Field => CalcError.Field,
        CellError.Connect => CalcError.Connect,
        CellError.Blocked => CalcError.Blocked,
        CellError.Busy => CalcError.Busy,
        _ => CalcError.Unknown,
    };

    private static CellError ToCellError(CalcError e) => e switch
    {
        CalcError.Null => CellError.Null,
        CalcError.Div0 => CellError.DivideByZero,
        CalcError.Value => CellError.Value,
        CalcError.Ref => CellError.Reference,
        CalcError.Name => CellError.Name,
        CalcError.Num => CellError.Number,
        CalcError.NA => CellError.NotAvailable,
        CalcError.GettingData => CellError.GettingData,
        CalcError.Spill => CellError.Spill,
        CalcError.Calc => CellError.Calc,
        CalcError.Field => CellError.Field,
        CalcError.Connect => CellError.Connect,
        CalcError.Blocked => CellError.Blocked,
        CalcError.Busy => CellError.Busy,
        _ => CellError.Unknown,
    };

    private static CellValue ToCell(CellStore store, in CalcValue v) => v.Kind switch
    {
        ValueKind.Number => CellValue.FromNumber(v.Number),
        ValueKind.Boolean => CellValue.FromBoolean(v.Boolean),
        ValueKind.Text => new CellValue(CellValueKind.Text, 0, store.AddPoolString(v.Text)),
        ValueKind.Error => CellValue.FromError(ToCellError(v.Error)),
        ValueKind.Lambda => CellValue.FromError(CellError.Calc),
        _ => CellValue.FromNumber(0),
    };

    // ---- edit tracking ------------------------------------------------------------------------------------------------------

    private void Hook()
    {
        for (int i = 0; i < Book.Sheets.Length; i++)
        {
            if (Book.Sheets[i].Kind != SheetKind.Worksheet)
            {
                continue;
            }

            int sheet = i;
            Book.Sheets[i].Store.Changed = (row, column, formula) => OnChanged(sheet, row, column, formula);
        }
    }

    private void OnChanged(int sheet, int row, int column, bool formula)
    {
        if (_suppress)
        {
            return;
        }

        lock (_logGate)
        {
            if (column == 0)
            {
                _forceFull = true;
                _structural = true;
                return;
            }

            _log.Add((sheet, row, column));
            if (formula)
            {
                _structural = true;
            }
        }
    }

    // ---- extents and spills -----------------------------------------------------------------------------------------------

    public void UsedExtent(int sheet, out int lastRow, out int lastColumn)
    {
        if (_lastRow[sheet] < 0)
        {
            int lr = 0;
            int lc = 0;
            List<RowData> rows = Book.Sheets[sheet].Store.Rows;
            if (rows.Count > 0)
            {
                lr = rows[^1].Row;
                foreach (RowData r in rows)
                {
                    if (r.Count > 0)
                    {
                        lc = Math.Max(lc, r.Cols[r.Count - 1] + 1);
                    }
                }
            }

            _lastColumn[sheet] = Math.Max(lc, 1);
            _lastRow[sheet] = Math.Max(lr, 1);
        }

        lastRow = _lastRow[sheet];
        lastColumn = _lastColumn[sheet];
    }

    private void InvalidateExtents() => Array.Fill(_lastRow, -1);

    public bool TryGetSpill(int sheet, int row, int column, out CellArea area)
    {
        area = default;
        if (_graph is null || sheet < 0)
        {
            return false;
        }

        int n = _graph.Find(sheet, row, column);
        if (n < 0 || (_graph.Flags[n] & CalcGraph.FlagSpilling) == 0 || !_spill.TryGetValue(n, out area))
        {
            return false;
        }

        area = area with { Sheet = Book.Sheets[sheet].Name };
        return true;
    }

    private void InitSpills()
    {
        _spill.Clear();
        _blocked.Clear();
        CalcGraph g = _graph!;
        for (int n = 0; n < g.Count; n++)
        {
            if ((g.Flags[n] & CalcGraph.FlagSpilling) == 0)
            {
                continue;
            }

            CalcGraph.Decode(g.Keys[n], out int s, out int r, out int c);
            CellFormula? f = Book.Sheets[s].Store.GetFormula(r, c);
            CellArea area = new(null, null, r, c, r, c);
            if (f?.Reference is { } rf && CellRange.TryParse(rf, out CellRange cr) && cr.Contains(new CellAddress(r, c)))
            {
                area = new CellArea(null, null, cr.TopLeft.Row, cr.TopLeft.Column, cr.BottomRight.Row, cr.BottomRight.Column);
            }

            _spill[n] = area;
            if (Book.Sheets[s].Store.TryGetCell(r, c, out CellValue cached, out _) && cached.Kind == CellValueKind.Error && cached.Error == CellError.Spill)
            {
                _blocked.Add(n);
            }
        }
    }

    // ---- the run --------------------------------------------------------------------------------------------------------------

    public RecalcResult Run(RecalcOptions options, CancellationToken ct)
    {
        _result = new RecalcResult();
        _evaluated = 0;
        _cancel = ct;
        ReadIterationSettings();
        _externalResolver = options.ExternalWorkbooks;
        _webResolver = options.WebServices;
        _books = new ExternalBooks(_wb, _externalResolver);
        FunctionRegistry registry = options.Functions ?? DefaultRegistry.Value;
        string signature = CalcBook.Signature(_wb);
        bool full = options.Full;
        bool rebuild = _graph is null || signature != _signature || !ReferenceEquals(registry, _registry) || _structural;
        if (rebuild)
        {
            if (_graph is null || signature != _signature || !ReferenceEquals(registry, _registry))
            {
                full = true;
            }

            _registry = registry;
            _signature = signature;
            while (_workers.TryTake(out _))
            {
            }

            Book = new CalcBook(_wb);
            _resolver = new WorkbookResolver(this);
            _graph = CalcGraph.Build(Book, registry, ct);
            _state = new byte[_graph.Count];
            _pending = new int[_graph.Count];
            _lastRow = new int[Book.Sheets.Length];
            _lastColumn = new int[Book.Sheets.Length];
            Hook();
            InitSpills();
            _result.GraphBuilt = true;
        }

        List<(int, int, int)> seeds;
        lock (_logGate)
        {
            seeds = [.. _log];
            _log.Clear();
            _structural = false;
            if (_forceFull)
            {
                full = true;
                _forceFull = false;
            }
        }

        InvalidateExtents();
        _result.Formulas = _graph!.Count;
        _result.WasFull = full;
        int dop = options.MaxDegreeOfParallelism <= 0 ? Environment.ProcessorCount : options.MaxDegreeOfParallelism;
        _suppress = true;
        try
        {
            bool all = full;
            CalcWorker? tableWorker = null;
            List<(int, int, int)> valueSeeds = seeds;
            for (int pass = 1; pass <= MaxPasses; pass++)
            {
                _result.Passes = pass;
                _spillChanged = [];
                int evaluatedBefore = _evaluated;
                RunPass(all, valueSeeds, pass == 1, dop, options.ParallelThreshold, ct);
                // Tables are calculated after the formulas they read. A later pass re-runs them only if it calculated something.
                if (_graph!.DataTables.Count > 0 && (pass == 1 ? all || valueSeeds.Count > 0 || _evaluated > 0 : _evaluated > evaluatedBefore))
                {
                    RunDataTables(tableWorker ??= new CalcWorker(this));
                }

                if (_spillChanged.Count == 0)
                {
                    break;
                }

                valueSeeds = _spillChanged;
                all = false;
            }
        }
        finally
        {
            _suppress = false;
        }

        _result.Evaluated = _evaluated;
        _result.ExternalResolved = _books.Resolved;
        _result.ExternalMissing = _books.Missing;
        if (_externalResolver is not null)
        {
            _books.Flush();
        }

        return _result;
    }

    private void RunPass(bool all, List<(int Sheet, int Row, int Column)> valueSeeds, bool includeAlways, int dop, int threshold, CancellationToken ct)
    {
        CalcGraph g = _graph!;
        int count = g.Count;
        Array.Clear(_state);
        Array.Clear(_pending);
        var queue = new List<int>();
        var tmp = new List<int>();

        void Mark(int i)
        {
            if (_state[i] == Clean)
            {
                _state[i] = Dirty;
                queue.Add(i);
            }
        }

        if (all)
        {
            for (int i = 0; i < count; i++)
            {
                Mark(i);
            }
        }
        else
        {
            foreach ((int s, int r, int c) in valueSeeds)
            {
                int node = s < Book.Sheets.Length ? g.Find(s, r, c) : -1;
                if (node >= 0)
                {
                    Mark(node);
                }
                else
                {
                    // A change inside a spill range may block or unblock the formula that fills it.
                    foreach ((int an, CellArea sa) in includeAlways ? _spill : [])
                    {
                        CalcGraph.Decode(g.Keys[an], out int asheet, out _, out _);
                        if (asheet == s && sa.Contains(r, c))
                        {
                            Mark(an);
                        }
                    }

                    tmp.Clear();
                    g.Dependents(s, r, c, tmp);
                    foreach (int d in tmp)
                    {
                        Mark(d);
                    }
                }
            }

            if (includeAlways)
            {
                foreach (int b in _blocked)
                {
                    Mark(b); // a spill that failed is tried again
                }

                for (int i = 0; i < count; i++)
                {
                    if ((g.Flags[i] & (CalcGraph.FlagDynamic | CalcGraph.FlagVolatile)) != 0
                        || ((g.Flags[i] & CalcGraph.FlagExternal) != 0 && (_externalResolver is not null || _webResolver is not null)))
                    {
                        Mark(i);
                    }
                }
            }
        }

        // Count, for every dirty formula, how many dirty formulas it waits for; everything a dirty formula feeds is dirty too.
        for (int head = 0; head < queue.Count; head++)
        {
            int p = queue[head];
            CalcGraph.Decode(g.Keys[p], out int ps, out int pr, out int pc);
            tmp.Clear();
            g.Dependents(ps, pr, pc, tmp);
            foreach (int d in tmp)
            {
                _pending[d]++;
                Mark(d);
            }

            if ((head & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
        }

        var frontier = new List<int>();
        foreach (int n in queue)
        {
            if (_pending[n] == 0)
            {
                frontier.Add(n);
            }
        }

        frontier.Sort();
        int fired = 0;
        var next = new List<int>();
        CalcWorker main = new(this);
        while (fired < queue.Count)
        {
            if (frontier.Count == 0)
            {
                frontier = BreakCycles(queue, main);
                if (frontier.Count == 0)
                {
                    break;
                }

                // The members of the cycles were given their values (0); they fire like any other finished formula.
                foreach (int m in frontier)
                {
                    _state[m] = Fired;
                }

                foreach (int m in frontier)
                {
                    FireDependents(m, tmp, next);
                    fired++;
                }

                frontier = SortedCopy(next);
                continue;
            }

            ct.ThrowIfCancellationRequested();
            _result.Levels++;
            EvaluateLevel(frontier, main, dop, threshold, ct);
            next.Clear();
            foreach (int n in frontier)
            {
                _state[n] = Fired;
            }

            foreach (int n in frontier)
            {
                FireDependents(n, tmp, next);
            }

            fired += frontier.Count;
            frontier = SortedCopy(next);
        }
    }

    private static List<int> SortedCopy(List<int> source)
    {
        var copy = new List<int>(source);
        copy.Sort();
        source.Clear();
        return copy;
    }

    private void FireDependents(int p, List<int> tmp, List<int> next)
    {
        CalcGraph.Decode(_graph!.Keys[p], out int s, out int r, out int c);
        tmp.Clear();
        _graph.Dependents(s, r, c, tmp);
        foreach (int d in tmp)
        {
            if (_state[d] == Clean)
            {
                continue;
            }

            if (--_pending[d] == 0 && _state[d] != Fired)
            {
                next.Add(d);
            }
        }
    }

    // ---- evaluation of one level -----------------------------------------------------------------------------------------

    private void EvaluateLevel(List<int> frontier, CalcWorker main, int dop, int threshold, CancellationToken ct)
    {
        CalcGraph g = _graph!;
        var plain = new List<int>();
        var dynamic = new List<int>();
        for (int i = 0; i < frontier.Count; i++)
        {
            int n = frontier[i];
            if (_state[n] != Dirty)
            {
                continue; // already calculated while another formula needed it
            }

            ((g.Flags[n] & CalcGraph.FlagDynamic) != 0 ? dynamic : plain).Add(n);
        }

        var results = new CalcValue[plain.Count];
        if (plain.Count >= threshold && dop > 1)
        {
            var po = new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct };
            var ranges = System.Collections.Concurrent.Partitioner.Create(0, plain.Count, Math.Max(16, plain.Count / (dop * 4)));
            int evaluated = 0;
            Parallel.ForEach(ranges, po, () => RentWorker(), (range, _, worker) =>
            {
                CalcWorker.Current = worker;
                try
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        results[i] = EvalNode(worker, plain[i]);
                    }
                }
                finally
                {
                    CalcWorker.Current = null;
                }

                Interlocked.Add(ref evaluated, range.Item2 - range.Item1);
                return worker;
            }, worker => _workers.Add(worker));
            _evaluated += evaluated;
        }
        else
        {
            CalcWorker.Current = main;
            try
            {
                for (int i = 0; i < plain.Count; i++)
                {
                    results[i] = EvalNode(main, plain[i]);
                }
            }
            finally
            {
                CalcWorker.Current = null;
            }

            _evaluated += plain.Count;
        }

        // Writing the grid is sequential, in node order, so spills claim cells deterministically.
        for (int i = 0; i < plain.Count; i++)
        {
            int n = plain[i];
            if (!results[i].IsMissing)
            {
                Commit(n, results[i]);
            }

            _state[n] = Computed;
        }

        if (dynamic.Count > 0)
        {
            main.InlineAllowed = true;
            CalcWorker.Current = main;
            try
            {
                foreach (int n in dynamic)
                {
                    if (_state[n] == Dirty)
                    {
                        EvalInline(main, n);
                    }
                }
            }
            finally
            {
                main.InlineAllowed = false;
                CalcWorker.Current = null;
            }
        }
    }

    private CalcWorker RentWorker() => _workers.TryTake(out CalcWorker? w) ? w : new CalcWorker(this);

    /// <summary>Called by the resolver when a formula reads a cell that holds a formula in the sequential phase.</summary>
    /// <returns>True when the cell was calculated just now, so its stored value should be read again.</returns>
    public bool ReadInline(CalcWorker w, int sheet, int row, int column)
    {
        int n = _graph!.Find(sheet, row, column);
        if (n < 0)
        {
            return false;
        }

        if (_state[n] == Dirty)
        {
            return EvalInline(w, n);
        }

        if (_state[n] == Running)
        {
            int at = w.Stack.IndexOf(n);
            for (int k = Math.Max(at, 0); k < w.Stack.Count; k++)
            {
                w.Circular.Add(w.Stack[k]);
            }
        }

        return false;
    }

    private bool EvalInline(CalcWorker w, int n)
    {
        if (w.Stack.Count >= MaxInlineDepth)
        {
            return false;
        }

        _state[n] = Running;
        w.Stack.Add(n);
        CalcValue v;
        try
        {
            v = EvalNode(w, n);
        }
        finally
        {
            w.Stack.RemoveAt(w.Stack.Count - 1);
        }

        _evaluated++;
        if (w.Circular.Remove(n))
        {
            v = CalcValue.FromNumber(0);
            ReportCircular(n);
        }

        _state[n] = Computed;
        if (!v.IsMissing)
        {
            Commit(n, v);
        }

        return true;
    }

    private CalcValue EvalNode(CalcWorker w, int n)
    {
        CalcGraph g = _graph!;
        CalcGraph.Decode(g.Keys[n], out int s, out int r, out int c);
        Worksheet ws = Book.Sheets[s];
        CellFormula? f = ws.Store.GetFormula(r, c);
        FormulaNode? tree = f is null ? null : g.GetTree(s, r, c, f);
        if (tree is null)
        {
            return NoChange;
        }

        Evaluator ev = (g.Flags[n] & (CalcGraph.FlagSpilling | CalcGraph.FlagArray)) != 0 ? w.Dynamic : w.Legacy;
        try
        {
            w.ServiceDenied = false;
            CalcValue result = ev.Evaluate(tree, new CalcCell(ws.Name, r, c));
            if (w.ServiceDenied)
            {
                // A web call that was denied: the cell keeps the value the file cached for it, if it has one.
                w.ServiceDenied = false;
                if (ws.Store.TryGetCell(r, c, out CellValue cached, out _) && !cached.IsEmpty)
                {
                    return NoChange;
                }
            }

            return result;
        }
        catch (OfficeLimitException)
        {
            _wb.OpenContext.Defects.Report(new Defect(DefectKind.Limit, "calc.limit-exceeded", DefectSeverity.Repaired,
                "The formula exceeded a calculation limit; the cell shows #VALUE!.", DefectLocation.InCell(ws.Name, new CellAddress(r, c).ToString())));
            return CalcValue.FromError(CalcError.Value);
        }
    }

    // ---- other workbooks and web services -------------------------------------------------------------------------------

    public CalcValue ReadExternal(SheetPrefix prefix, FormulaNode node) => _books.Read(prefix, node);

    public CalcValue WebService(string url)
    {
        if (_webResolver is null)
        {
            Deny();
            return CalcValue.FromError(CalcError.Value);
        }

        return _webResolver.Fetch(url) is { } text ? CalcValue.FromText(text) : CalcValue.FromError(CalcError.Value);
    }

    public CalcValue FilterXml(string xml, string xpath)
    {
        if (_webResolver is null)
        {
            Deny();
            return CalcValue.FromError(CalcError.Value);
        }

        return _webResolver.FilterXml(xml, xpath) ?? CalcValue.FromError(CalcError.Value);
    }

    private static void Deny()
    {
        if (CalcWorker.Current is { } w)
        {
            w.ServiceDenied = true;
        }
    }

    // ---- circular references ----------------------------------------------------------------------------------------------

    private void ReportCircular(int n)
    {
        _result.CircularCells++;
        CalcGraph.Decode(_graph!.Keys[n], out int s, out int r, out int c);
        _wb.OpenContext.Defects.Report(new Defect(DefectKind.Formula, "calc.circular-reference", DefectSeverity.Repaired,
            "The formula is part of a circular reference; without iterative calculation it reads as 0.",
            DefectLocation.InCell(Book.Sheets[s].Name, new CellAddress(r, c).ToString())));
    }

    private sealed class AreaCollector : IPrecedentSink
    {
        public List<(int Sheet, int Row1, int Column1, int Row2, int Column2)> Areas { get; } = [];

        public void Area(int sheet, int row1, int column1, int row2, int column2) => Areas.Add((sheet, row1, column1, row2, column2));
    }

    /// <summary>
    /// Called when no formula is ready but some are still waiting: what is left is cycles and the formulas below them. Finds the formulas that
    /// lie on a cycle (strongly connected sets), gives them the value 0 as Excel does without iteration, and returns them.
    /// </summary>
    private List<int> BreakCycles(List<int> queue, CalcWorker w)
    {
        CalcGraph g = _graph!;
        var left = new List<int>();
        foreach (int n in queue)
        {
            if (_state[n] == Dirty)
            {
                left.Add(n);
            }
        }

        if (left.Count == 0)
        {
            return [];
        }

        var walker = new DependencyWalker(Book, _registry);
        var adjacency = new Dictionary<int, int[]>();
        int[] Adj(int n)
        {
            if (adjacency.TryGetValue(n, out int[]? cached))
            {
                return cached;
            }

            CalcGraph.Decode(g.Keys[n], out int s, out int r, out int c);
            CellFormula? f = Book.Sheets[s].Store.GetFormula(r, c);
            FormulaNode? tree = f is null ? null : g.GetTree(s, r, c, f);
            var found = new List<int>();
            if (tree is not null)
            {
                var sink = new AreaCollector();
                walker.Walk(tree, s, r, c, sink);
                var hits = new List<int>();
                foreach ((int sh, int r1, int c1, int r2, int c2) in sink.Areas)
                {
                    hits.Clear();
                    g.NodesInArea(sh, r1, c1, r2, c2, hits);
                    foreach (int h in hits)
                    {
                        if (_state[h] == Dirty)
                        {
                            found.Add(h);
                        }
                    }
                }
            }

            int[] result = [.. new HashSet<int>(found)];
            adjacency[n] = result;
            return result;
        }

        // Tarjan's strongly connected components, with an explicit stack.
        var index = new Dictionary<int, int>();
        var low = new Dictionary<int, int>();
        var onStack = new HashSet<int>();
        var stack = new Stack<int>();
        var cycle = new List<int>();
        var components = new List<List<int>>();
        int counter = 0;
        foreach (int root in left)
        {
            if (index.TryGetValue(root, out _))
            {
                continue;
            }

            var frames = new Stack<(int Node, int[] Edges, int Pos)>();
            index[root] = low[root] = counter++;
            stack.Push(root);
            onStack.Add(root);
            frames.Push((root, Adj(root), 0));
            while (frames.Count > 0)
            {
                (int v, int[] edges, int pos) = frames.Pop();
                if (pos < edges.Length)
                {
                    frames.Push((v, edges, pos + 1));
                    int u = edges[pos];
                    if (!index.TryGetValue(u, out int uIndex))
                    {
                        index[u] = low[u] = counter++;
                        stack.Push(u);
                        onStack.Add(u);
                        frames.Push((u, Adj(u), 0));
                    }
                    else if (onStack.Contains(u))
                    {
                        low[v] = Math.Min(low[v], uIndex);
                    }

                    continue;
                }

                if (low[v] == index[v])
                {
                    var component = new List<int>();
                    int x;
                    do
                    {
                        x = stack.Pop();
                        onStack.Remove(x);
                        component.Add(x);
                    }
                    while (x != v);

                    if (component.Count > 1 || Array.IndexOf(Adj(v), v) >= 0)
                    {
                        cycle.AddRange(component);
                        components.Add(component);
                    }
                }

                if (frames.Count > 0)
                {
                    int parent = frames.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[v]);
                }
            }
        }

        if (cycle.Count == 0)
        {
            cycle.Add(left[0]); // cannot happen with a consistent graph; keeps the run moving
        }
        else if (_iterate)
        {
            // ECMA-376 Part 1 §18.2.2 (calcPr iterate): circular chains are calculated repeatedly instead of being cut. A cycle waits until
            // everything it reads from outside itself is finished, so cycles that feed each other are iterated in dependency order.
            var ready = new List<int>();
            foreach (List<int> comp in components)
            {
                var members = new HashSet<int>(comp);
                bool closed = true;
                foreach (int m in comp)
                {
                    foreach (int d in Adj(m))
                    {
                        if (!members.Contains(d))
                        {
                            closed = false;
                            break;
                        }
                    }

                    if (!closed)
                    {
                        break;
                    }
                }

                if (closed)
                {
                    comp.Sort();
                    IterateCycle(comp, w);
                    ready.AddRange(comp);
                }
            }

            if (ready.Count > 0)
            {
                ready.Sort();
                return ready;
            }
        }

        cycle.Sort();
        foreach (int n in cycle)
        {
            ReportCircular(n);
            Commit(n, CalcValue.FromNumber(0));
            _state[n] = Computed;
        }

        return cycle;
    }

    // ---- writing results --------------------------------------------------------------------------------------------------

    private static bool WriteScalar(Worksheet ws, int row, int column, in CalcValue v)
    {
        CellStore store = ws.Store;
        bool had = store.TryGetCell(row, column, out CellValue old, out _);
        CalcValue nv = v.Kind is ValueKind.Array or ValueKind.Reference ? CalcValue.FromError(CalcError.Value) : v;
        if (had && Same(ws, old, nv))
        {
            return false;
        }

        store.SetValue(row, column, ToCell(store, nv));
        return true;
    }

    private static bool Same(Worksheet ws, in CellValue old, in CalcValue v)
    {
        switch (v.Kind)
        {
            case ValueKind.Number:
                return old.Kind == CellValueKind.Number && old.Number.Equals(v.Number);
            case ValueKind.Boolean:
                return old.Kind == CellValueKind.Boolean && old.Boolean == v.Boolean;
            case ValueKind.Text:
                return old.Kind == CellValueKind.Text && string.Equals(ws.GetText(old), v.Text, StringComparison.Ordinal);
            case ValueKind.Error:
                return old.Kind == CellValueKind.Error && old.Error == ToCellError(v.Error);
            default:
                return false;
        }
    }

    private void Commit(int n, in CalcValue value)
    {
        CalcGraph g = _graph!;
        CalcGraph.Decode(g.Keys[n], out int s, out int r, out int c);
        Worksheet ws = Book.Sheets[s];
        byte flags = g.Flags[n];
        if ((flags & (CalcGraph.FlagSpilling | CalcGraph.FlagArray)) == 0)
        {
            WriteScalar(ws, r, c, value.Kind == ValueKind.Array ? value.Array![0, 0] : value);
            return;
        }

        CalcArray? arr = value.Kind == ValueKind.Array ? value.Array : CalcArray.OfValue(value.Kind == ValueKind.Lambda ? CalcValue.FromError(CalcError.Calc) : value);
        if ((flags & CalcGraph.FlagArray) != 0)
        {
            CommitLegacyArray(ws, s, r, c, arr!);
        }
        else
        {
            CommitSpill(n, ws, s, r, c, arr!);
        }
    }

    private void CommitLegacyArray(Worksheet ws, int sheet, int row, int column, CalcArray arr)
    {
        // A legacy array formula fills the block its range names; a smaller result pads with #N/A (ECMA-376 Part 1 §18.3.1.2).
        CellFormula? f = ws.Store.GetFormula(row, column);
        int r2 = row;
        int c2 = column;
        if (f?.Reference is { } rf && CellRange.TryParse(rf, out CellRange cr) && cr.Contains(new CellAddress(row, column)))
        {
            r2 = cr.BottomRight.Row;
            c2 = cr.BottomRight.Column;
        }

        for (int i = 0; i <= r2 - row; i++)
        {
            for (int j = 0; j <= c2 - column; j++)
            {
                if (WriteScalar(ws, row + i, column + j, arr.Broadcasting(i, j)) && (i > 0 || j > 0))
                {
                    _spillChanged.Add((sheet, row + i, column + j));
                }
            }
        }
    }

    private void CommitSpill(int node, Worksheet ws, int sheet, int row, int column, CalcArray arr)
    {
        CellStore store = ws.Store;
        _spill.TryGetValue(node, out CellArea own);
        if (own.Row1 == 0)
        {
            own = new CellArea(null, null, row, column, row, column);
        }

        CellArea target = new(null, null, row, column, row, column);
        _blocked.Remove(node);
        CalcValue anchor;
        if (arr.Rows == 1 && arr.Columns == 1)
        {
            anchor = arr[0, 0];
        }
        else
        {
            int tr = row + arr.Rows - 1;
            int tc = column + arr.Columns - 1;
            if (IsBlocked(store, ws, sheet, row, column, tr, tc, own))
            {
                anchor = CalcValue.FromError(CalcError.Spill);
                _result.SpillErrors++;
                _blocked.Add(node);
            }
            else
            {
                anchor = arr[0, 0];
                target = new CellArea(null, null, row, column, tr, tc);
                for (int i = 0; i < arr.Rows; i++)
                {
                    for (int j = 0; j < arr.Columns; j++)
                    {
                        if ((i > 0 || j > 0) && WriteScalar(ws, row + i, column + j, arr[i, j]))
                        {
                            _spillChanged.Add((sheet, row + i, column + j));
                        }
                    }
                }
            }
        }

        WriteScalar(ws, row, column, anchor);

        // Cells the earlier result covered and this one does not become empty.
        for (int r = own.Row1; r <= own.Row2; r++)
        {
            for (int c = own.Column1; c <= own.Column2; c++)
            {
                if ((r == row && c == column) || target.Contains(r, c))
                {
                    continue;
                }

                if (store.TryGetCellWithFormula(r, c, out CellValue v, out bool hasFormula) && !hasFormula)
                {
                    store.TryGetCell(r, c, out _, out int style);
                    if (style == 0)
                    {
                        store.RemoveCell(r, c);
                    }
                    else
                    {
                        store.SetValue(r, c, default);
                    }

                    if (!v.IsEmpty)
                    {
                        _spillChanged.Add((sheet, r, c));
                    }
                }
            }
        }

        _spill[node] = target;
        InvalidateExtents();

        string newRef = target.IsSingleCell ? new CellAddress(row, column).ToString() : new CellRange(new CellAddress(target.Row1, target.Column1), new CellAddress(target.Row2, target.Column2)).ToString();
        CellFormula? f = store.GetFormula(row, column);
        if (f is not null && !string.Equals(f.Reference, newRef, StringComparison.Ordinal))
        {
            store.SetFormula(row, column, f with { Reference = newRef });
        }
    }

    // ECMA-376 Part 1 §18.3.1.2 and [MS-XLSX]: a dynamic array result is blocked by a cell that holds a value or formula, a merged
    // range, a table, or the edge of the sheet.
    private bool IsBlocked(CellStore store, Worksheet ws, int sheet, int row, int column, int r2, int c2, in CellArea own)
    {
        if (r2 > CellArea.MaxRow || c2 > CellArea.MaxColumn)
        {
            return true;
        }

        foreach (CellRange m in ws.MergedCells)
        {
            if (m.TopLeft.Row <= r2 && m.BottomRight.Row >= row && m.TopLeft.Column <= c2 && m.BottomRight.Column >= column)
            {
                return true;
            }
        }

        foreach (TableInfo t in Book.Tables)
        {
            if (t.Sheet == sheet && t.Row1 <= r2 && t.Row2 >= row && t.Column1 <= c2 && t.Column2 >= column)
            {
                return true;
            }
        }

        List<RowData> rows = store.Rows;
        int idx = store.FindRowIndex(row);
        if (idx < 0)
        {
            idx = ~idx;
        }

        for (; idx < rows.Count && rows[idx].Row <= r2; idx++)
        {
            RowData rd = rows[idx];
            int at = rd.Find(column - 1);
            if (at < 0)
            {
                at = ~at;
            }

            for (; at < rd.Count && rd.Cols[at] + 1 <= c2; at++)
            {
                int col = rd.Cols[at] + 1;
                if ((rd.Row == row && col == column) || own.Contains(rd.Row, col))
                {
                    continue;
                }

                if ((rd.Kinds[at] & RowData.HasFormulaBit) != 0 || RowData.Decode(rd.Kinds[at], rd.Data[at]).Kind != CellValueKind.Empty)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
