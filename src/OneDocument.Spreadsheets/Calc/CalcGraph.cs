using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

/// <summary>
/// The dependency graph of a workbook: one node per formula cell, found by position, with a reverse index from every cell and block a
/// formula reads to the formulas that read it. Edges are never stored one per cell of a block, so <c>SUM(A1:A100000)</c> costs one entry.
/// </summary>
internal sealed class CalcGraph : IPrecedentSink
{
    public const byte FlagDynamic = 1;
    public const byte FlagVolatile = 2;
    public const byte FlagSpilling = 4;
    public const byte FlagArray = 8;
    public const byte FlagUnparsable = 16;
    public const byte FlagExternal = 32;

    // A block of at most this many cells is stored as one edge per cell; larger blocks as one entry per column.
    private const int ExpandLimit = 16;
    private const int WideColumns = 256;

    private readonly FunctionRegistry _registry;
    private readonly Dictionary<(int Sheet, int Index), (FormulaNode Tree, int Row, int Column)> _anchors = [];

    private long[] _edgeKeyBuf = new long[1024];
    private int[] _edgeNodeBuf = new int[1024];
    private int _edgeCount;
    private RectEntry[] _rectBuf = new RectEntry[256];
    private int _rectCount;
    private WideEntry[] _wideBuf = new WideEntry[16];
    private int _wideCount;
    private int _current;

    private long[] _edgeKeys = [];
    private int[] _edgeStart = [];
    private int[] _edgeNodes = [];
    private RectEntry[] _rects = [];
    private int[] _rectPrefixMax = [];
    private readonly Dictionary<long, (int Start, int Length)> _rectSegments = [];
    private WideEntry[] _wide = [];
    private int[] _byColumn = [];
    private readonly Dictionary<long, (int Start, int Length)> _columnSegments = [];

    private struct RectEntry
    {
        public long ColumnKey;
        public int Row1;
        public int Row2;
        public int Node;
    }

    private struct WideEntry
    {
        public int Sheet;
        public int Row1;
        public int Column1;
        public int Row2;
        public int Column2;
        public int Node;
    }

    private CalcGraph(CalcBook book, FunctionRegistry registry)
    {
        Book = book;
        _registry = registry;
    }

    public CalcBook Book { get; }

    public int Count { get; private set; }

    /// <summary>The sorted keys (sheet, row, column) of the formula cells.</summary>
    public long[] Keys { get; private set; } = [];

    public byte[] Flags { get; private set; } = [];

    /// <summary>The data-table blocks (the cell holding the <c>t="dataTable"</c> formula, and that formula). They are not nodes: the engine calculates them after the formulas.</summary>
    public List<(int Sheet, int Row, int Column, CellFormula Formula)> DataTables { get; } = [];

    public static long Key(int sheet, int row, int column) => ((long)sheet << 34) | ((long)(row - 1) << 14) | (uint)(column - 1);

    public static void Decode(long key, out int sheet, out int row, out int column)
    {
        sheet = (int)(key >> 34);
        row = (int)((key >> 14) & 0xFFFFF) + 1;
        column = (int)(key & 0x3FFF) + 1;
    }

    /// <summary>Finds the node at a position.</summary>
    /// <returns>The node number, or -1 when the cell holds no (calculable) formula.</returns>
    public int Find(int sheet, int row, int column)
    {
        int i = Array.BinarySearch(Keys, 0, Count, Key(sheet, row, column));
        return i >= 0 ? i : -1;
    }

    /// <summary>Builds the graph of a workbook.</summary>
    public static CalcGraph Build(CalcBook book, FunctionRegistry registry, CancellationToken cancellationToken)
    {
        var g = new CalcGraph(book, registry);
        g.BuildCore(cancellationToken);
        return g;
    }

    private void BuildCore(CancellationToken ct)
    {
        Worksheet[] sheets = Book.Sheets;
        var keys = new List<long>();
        var flags = new List<byte>();

        // Shared formulas first: the anchor holds the text, the others only its index (ECMA-376 Part 1 §18.3.1.40).
        for (int s = 0; s < sheets.Length; s++)
        {
            if (sheets[s].Kind != SheetKind.Worksheet)
            {
                continue;
            }

            CellStore store = sheets[s].Store;
            foreach (RowData r in store.Rows)
            {
                if (r.Formulas is null)
                {
                    continue;
                }

                for (int i = 0; i < r.Count; i++)
                {
                    if (r.Formulas[i] == 0)
                    {
                        continue;
                    }

                    CellFormula f = store.FormulaAt(r.Formulas[i]);
                    if (f.Kind == CellFormulaKind.Shared && f.Text.Length > 0 && f.SharedIndex >= 0 && TryParse(f.Text, sheets[s], r.Row, r.Cols[i] + 1, out FormulaNode? t))
                    {
                        _anchors[(s, f.SharedIndex)] = (t!, r.Row, r.Cols[i] + 1);
                    }
                }
            }
        }

        var walker = new DependencyWalker(Book, _registry);
        for (int s = 0; s < sheets.Length; s++)
        {
            if (sheets[s].Kind != SheetKind.Worksheet)
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            CellStore store = sheets[s].Store;
            foreach (RowData r in store.Rows)
            {
                if (r.Formulas is null)
                {
                    continue;
                }

                for (int i = 0; i < r.Count; i++)
                {
                    if (r.Formulas[i] == 0)
                    {
                        continue;
                    }

                    CellFormula f = store.FormulaAt(r.Formulas[i]);
                    if (f.Kind == CellFormulaKind.DataTable)
                    {
                        DataTables.Add((s, r.Row, r.Cols[i] + 1, f));
                        continue;
                    }

                    int col = r.Cols[i] + 1;
                    byte flag = 0;
                    if (f.Kind == CellFormulaKind.Array)
                    {
                        flag |= IsDynamicArray(store, r.Row, col) ? FlagSpilling : FlagArray;
                    }

                    FormulaNode? tree = GetTree(s, r.Row, col, f);
                    int id = keys.Count;
                    keys.Add(Key(s, r.Row, col));
                    if (tree is null)
                    {
                        flag |= FlagUnparsable;
                        flags.Add(flag);
                        continue;
                    }

                    _current = id;
                    walker.Walk(tree, s, r.Row, col, this);
                    if (walker.Dynamic)
                    {
                        flag |= FlagDynamic;
                    }

                    if (walker.Volatile)
                    {
                        flag |= FlagVolatile;
                    }

                    if (walker.External)
                    {
                        flag |= FlagExternal;
                    }

                    flags.Add(flag);
                }
            }
        }

        Keys = [.. keys];
        Flags = [.. flags];
        Count = Keys.Length;
        IndexEdges();
        IndexRects();
        IndexColumns();
        _edgeKeyBuf = [];
        _edgeNodeBuf = [];
        _rectBuf = [];
        _wideBuf = [];
    }

    /// <summary>True when the cell carries dynamic-array metadata (<c>cm</c>) rather than being a legacy array formula (ECMA-376 Part 1 §18.3.1.4; [MS-XLSX] CT_DynamicArrayProperties).</summary>
    public static bool IsDynamicArray(CellStore store, int row, int column)
    {
        string? attrs = store.GetExtras(row, column)?.Attributes;
        return attrs is not null && attrs.Contains(" cm=\"", StringComparison.Ordinal);
    }

    private bool TryParse(string text, Worksheet sheet, int row, int column, out FormulaNode? tree)
    {
        var options = new FormulaParserOptions { Limits = Book.Workbook.Limits };
        try
        {
            if (FormulaParser.TryParse(text, out tree, out FormulaSyntaxError error, options))
            {
                return true;
            }

            Book.Workbook.OpenContext.Defects.Report(new Defect(DefectKind.Formula, "calc.formula-unparsable", DefectSeverity.Dropped,
                "The formula cannot be read (" + error.Code + "); the cell keeps its stored value.", DefectLocation.InCell(sheet.Name, new CellAddress(row, column).ToString())));
        }
        catch (OfficeLimitException)
        {
            Book.Workbook.OpenContext.Defects.Report(new Defect(DefectKind.Formula, "calc.formula-unparsable", DefectSeverity.Dropped,
                "The formula exceeds a limit; the cell keeps its stored value.", DefectLocation.InCell(sheet.Name, new CellAddress(row, column).ToString())));
        }

        tree = null;
        return false;
    }

    /// <summary>Parses the formula of a cell (translating a shared formula's anchor to the cell). Returns null when the formula cannot be read.</summary>
    public FormulaNode? GetTree(int sheet, int row, int column, CellFormula f)
    {
        if (f.Kind == CellFormulaKind.Shared && f.Text.Length == 0)
        {
            if (!_anchors.TryGetValue((sheet, f.SharedIndex), out var anchor))
            {
                return null;
            }

            return FormulaTranslator.Translate(anchor.Tree, row - anchor.Row, column - anchor.Column);
        }

        if (f.Kind == CellFormulaKind.Shared && _anchors.TryGetValue((sheet, f.SharedIndex), out var own) && own.Row == row && own.Column == column)
        {
            return own.Tree;
        }

        return TryParse(f.Text, Book.Sheets[sheet], row, column, out FormulaNode? t) ? t : null;
    }

    // ---- edge collection ----------------------------------------------------------------------------------------------

    void IPrecedentSink.Area(int sheet, int row1, int column1, int row2, int column2)
    {
        row2 = Math.Min(row2, CellArea.MaxRow);
        column2 = Math.Min(column2, CellArea.MaxColumn);
        long cells = (long)(row2 - row1 + 1) * (column2 - column1 + 1);
        if (cells <= ExpandLimit)
        {
            for (int r = row1; r <= row2; r++)
            {
                for (int c = column1; c <= column2; c++)
                {
                    if (_edgeCount == _edgeKeyBuf.Length)
                    {
                        Array.Resize(ref _edgeKeyBuf, _edgeCount * 2);
                        Array.Resize(ref _edgeNodeBuf, _edgeCount * 2);
                    }

                    _edgeKeyBuf[_edgeCount] = Key(sheet, r, c);
                    _edgeNodeBuf[_edgeCount++] = _current;
                }
            }

            return;
        }

        if (column2 - column1 + 1 > WideColumns)
        {
            if (_wideCount == _wideBuf.Length)
            {
                Array.Resize(ref _wideBuf, _wideCount * 2);
            }

            _wideBuf[_wideCount++] = new WideEntry { Sheet = sheet, Row1 = row1, Column1 = column1, Row2 = row2, Column2 = column2, Node = _current };
            return;
        }

        for (int c = column1; c <= column2; c++)
        {
            if (_rectCount == _rectBuf.Length)
            {
                Array.Resize(ref _rectBuf, _rectCount * 2);
            }

            _rectBuf[_rectCount++] = new RectEntry { ColumnKey = ((long)sheet << 14) | (uint)(c - 1), Row1 = row1, Row2 = row2, Node = _current };
        }
    }

    private void IndexEdges()
    {
        Array.Sort(_edgeKeyBuf, _edgeNodeBuf, 0, _edgeCount);
        var starts = new List<int>();
        var uniq = new List<long>();
        for (int i = 0; i < _edgeCount; i++)
        {
            if (i == 0 || _edgeKeyBuf[i] != _edgeKeyBuf[i - 1])
            {
                uniq.Add(_edgeKeyBuf[i]);
                starts.Add(i);
            }
        }

        starts.Add(_edgeCount);
        _edgeKeys = [.. uniq];
        _edgeStart = [.. starts];
        _edgeNodes = new int[_edgeCount];
        Array.Copy(_edgeNodeBuf, _edgeNodes, _edgeCount);
    }

    private void IndexRects()
    {
        _rects = new RectEntry[_rectCount];
        Array.Copy(_rectBuf, _rects, _rectCount);
        Array.Sort(_rects, static (a, b) =>
        {
            int c = a.ColumnKey.CompareTo(b.ColumnKey);
            return c != 0 ? c : a.Row1.CompareTo(b.Row1);
        });
        _rectPrefixMax = new int[_rectCount];
        int segStart = 0;
        for (int i = 0; i < _rectCount; i++)
        {
            bool first = i == 0 || _rects[i].ColumnKey != _rects[i - 1].ColumnKey;
            if (first)
            {
                if (i > 0)
                {
                    _rectSegments[_rects[i - 1].ColumnKey] = (segStart, i - segStart);
                }

                segStart = i;
                _rectPrefixMax[i] = _rects[i].Row2;
            }
            else
            {
                _rectPrefixMax[i] = Math.Max(_rectPrefixMax[i - 1], _rects[i].Row2);
            }
        }

        if (_rectCount > 0)
        {
            _rectSegments[_rects[_rectCount - 1].ColumnKey] = (segStart, _rectCount - segStart);
        }

        _wide = new WideEntry[_wideCount];
        Array.Copy(_wideBuf, _wide, _wideCount);
    }

    private void IndexColumns()
    {
        var composite = new long[Count];
        _byColumn = new int[Count];
        for (int i = 0; i < Count; i++)
        {
            Decode(Keys[i], out int s, out int r, out int c);
            composite[i] = ((long)s << 34) | ((long)(c - 1) << 20) | (uint)(r - 1);
            _byColumn[i] = i;
        }

        Array.Sort(composite, _byColumn);
        int start = 0;
        for (int i = 1; i <= Count; i++)
        {
            if (i == Count || (composite[i] >> 20) != (composite[start] >> 20))
            {
                long colKey = ((composite[start] >> 34) << 14) | ((composite[start] >> 20) & 0x3FFF);
                _columnSegments[colKey] = (start, i - start);
                start = i;
            }
        }
    }

    // ---- queries ------------------------------------------------------------------------------------------------------

    /// <summary>Adds to <paramref name="into"/> every formula that reads the cell (with repeats when a formula reads it twice).</summary>
    public void Dependents(int sheet, int row, int column, List<int> into)
    {
        long key = Key(sheet, row, column);
        int e = Array.BinarySearch(_edgeKeys, key);
        if (e >= 0)
        {
            for (int i = _edgeStart[e]; i < _edgeStart[e + 1]; i++)
            {
                into.Add(_edgeNodes[i]);
            }
        }

        if (_rectCount > 0 && _rectSegments.TryGetValue(((long)sheet << 14) | (uint)(column - 1), out var seg))
        {
            // Entries are sorted by first row; the running maximum of last rows lets the scan stop early.
            int lo = seg.Start;
            int hi = seg.Start + seg.Length - 1;
            int last = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                if (_rects[mid].Row1 <= row)
                {
                    last = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            for (int i = last; i >= seg.Start && _rectPrefixMax[i] >= row; i--)
            {
                if (_rects[i].Row2 >= row)
                {
                    into.Add(_rects[i].Node);
                }
            }
        }

        for (int i = 0; i < _wide.Length; i++)
        {
            ref WideEntry w = ref _wide[i];
            if (w.Sheet == sheet && row >= w.Row1 && row <= w.Row2 && column >= w.Column1 && column <= w.Column2)
            {
                into.Add(w.Node);
            }
        }
    }

    /// <summary>Adds to <paramref name="into"/> the formula cells inside a block.</summary>
    public void NodesInArea(int sheet, int row1, int column1, int row2, int column2, List<int> into)
    {
        for (int c = column1; c <= column2; c++)
        {
            if (!_columnSegments.TryGetValue(((long)sheet << 14) | (uint)(c - 1), out var seg))
            {
                continue;
            }

            // Nodes of one column are in row order.
            int lo = 0;
            int hi = seg.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                Decode(Keys[_byColumn[seg.Start + mid]], out _, out int r, out _);
                if (r < row1)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            for (int i = lo; i < seg.Length; i++)
            {
                int node = _byColumn[seg.Start + i];
                Decode(Keys[node], out _, out int r, out _);
                if (r > row2)
                {
                    break;
                }

                into.Add(node);
            }
        }
    }
}
