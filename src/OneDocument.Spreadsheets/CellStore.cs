using System.Runtime.InteropServices;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>The kind of a cell formula as the part stores it (ECMA-376 Part 1 CT_CellFormula, attribute <c>t</c>).</summary>
public enum CellFormulaKind : byte
{
    /// <summary>An ordinary formula held in this cell alone.</summary>
    Normal = 0,

    /// <summary>A shared formula (CT_CellFormula): the anchor holds the text and the range, the others only the shared index.</summary>
    Shared = 1,

    /// <summary>An array formula over a range (a legacy CSE array or a spilling-array anchor).</summary>
    Array = 2,

    /// <summary>A data table (what-if) formula.</summary>
    DataTable = 3,
}

/// <summary>
/// The formula of a cell, kept as the text the part holds. The formula is not parsed here: the formula syntax tree belongs to the
/// calculation layer. Immutable, and equal by content, so the store interns identical records.
/// </summary>
/// <param name="Text">The formula text without the leading <c>=</c>, as stored. Empty for the cells of a shared formula that are not its anchor.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Reference">For a shared or array formula, the range it covers (<c>A1:A10</c>); otherwise null.</param>
/// <param name="SharedIndex">For a shared formula, its <c>si</c> index; otherwise -1.</param>
/// <param name="RawAttributes">The other attributes of the <c>f</c> element exactly as written (for example <c>ca="1"</c>, data-table inputs), or null. Written back unchanged.</param>
public sealed record CellFormula(string Text, CellFormulaKind Kind = CellFormulaKind.Normal, string? Reference = null, int SharedIndex = -1, string? RawAttributes = null)
{
    /// <summary>Makes an ordinary formula.</summary>
    /// <param name="text">The formula text; a leading <c>=</c> is removed.</param>
    /// <returns>The formula.</returns>
    public static CellFormula FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new CellFormula(text.StartsWith('=') ? text[1..] : text);
    }
}

/// <summary>Attributes and children of a cell that the store does not model, kept in order for the write path.</summary>
internal sealed class CellExtras
{
    /// <summary>Other attributes of <c>c</c> exactly as written, each with a leading space; null for none.</summary>
    public string? Attributes;

    /// <summary>Raw XML of unknown children of <c>c</c> (for example <c>extLst</c>), written after <c>f</c>, <c>v</c> and <c>is</c>; null for none.</summary>
    public byte[]? Children;
}

/// <summary>A string held by the sheet itself: an inline string, a formula's text result, or an unknown error text.</summary>
internal readonly record struct PoolString(string Text, byte[]? RawInline);

/// <summary>Flags in a row's <c>Flags</c> byte.</summary>
[Flags]
internal enum RowFlags : byte
{
    None = 0,
    Hidden = 1,
    CustomHeight = 2,
    CustomFormat = 4,
    Collapsed = 8,
    ThickTop = 16,
    ThickBottom = 32,
}

/// <summary>
/// One populated row: the cells as parallel arrays sorted by column (struct of arrays, so a scan over values, styles or columns touches
/// one dense array), plus the row's own properties. The arrays are exactly as long as needed after a load, with slack only after edits.
/// </summary>
internal sealed class RowData
{
    // Kind byte: bits 0-2 are the CellValueKind, bit 3 says a formula is attached, bit 4 says a string is an ISO 8601 date (t="d").
    public const byte KindMask = 7;
    public const byte HasFormulaBit = 8;
    public const byte IsoDateBit = 16;

    public readonly int Row;
    public ushort[] Cols = [];
    public byte[] Kinds = [];
    public long[] Data = [];
    public int[] Styles = [];
    public int[]? Formulas;
    public int Count;

    public double Height = -1;
    public RowFlags Flags;
    public byte OutlineLevel;
    public int Style;
    public string? ExtraAttributes;
    public byte[]? ExtraChildren;

    public RowData(int row) => Row = row;

    /// <summary>True when the row carries properties that must be written even without cells.</summary>
    public bool HasProperties => Height >= 0 || Flags != RowFlags.None || OutlineLevel != 0 || Style != 0 || ExtraAttributes is not null || ExtraChildren is not null;

    public int Find(int col0)
    {
        int lo = 0;
        int hi = Count - 1;
        ushort[] cols = Cols;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int c = cols[mid];
            if (c == col0)
            {
                return mid;
            }

            if (c < col0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    /// <summary>Makes room for one cell at <paramref name="at"/>.</summary>
    public void InsertSlot(int at)
    {
        if (Count == Cols.Length)
        {
            int cap = Math.Max(4, Count + (Count >> 1));
            Array.Resize(ref Cols, cap);
            Array.Resize(ref Kinds, cap);
            Array.Resize(ref Data, cap);
            Array.Resize(ref Styles, cap);
            if (Formulas is not null)
            {
                Array.Resize(ref Formulas, cap);
            }
        }

        if (at < Count)
        {
            int n = Count - at;
            Array.Copy(Cols, at, Cols, at + 1, n);
            Array.Copy(Kinds, at, Kinds, at + 1, n);
            Array.Copy(Data, at, Data, at + 1, n);
            Array.Copy(Styles, at, Styles, at + 1, n);
            if (Formulas is not null)
            {
                Array.Copy(Formulas, at, Formulas, at + 1, n);
            }
        }

        Count++;
        if (Formulas is not null)
        {
            Formulas[at] = 0;
        }
    }

    public void RemoveSlot(int at)
    {
        int n = Count - at - 1;
        if (n > 0)
        {
            Array.Copy(Cols, at + 1, Cols, at, n);
            Array.Copy(Kinds, at + 1, Kinds, at, n);
            Array.Copy(Data, at + 1, Data, at, n);
            Array.Copy(Styles, at + 1, Styles, at, n);
            if (Formulas is not null)
            {
                Array.Copy(Formulas, at + 1, Formulas, at, n);
            }
        }

        Count--;
    }

    public void EnsureFormulaArray()
    {
        if (Formulas is null)
        {
            Formulas = new int[Cols.Length];
        }
    }

    public static CellValue Decode(byte kind, long data)
    {
        switch ((CellValueKind)(kind & KindMask))
        {
            case CellValueKind.Number:
                return new CellValue(CellValueKind.Number, BitConverter.Int64BitsToDouble(data), 0);
            case CellValueKind.Boolean:
                return new CellValue(CellValueKind.Boolean, data != 0 ? 1 : 0, 0);
            case CellValueKind.Text:
                return new CellValue(CellValueKind.Text, 0, (int)data);
            case CellValueKind.Error:
                return new CellValue(CellValueKind.Error, 0, (int)data);
            default:
                return default;
        }
    }

    public static void Encode(in CellValue v, out byte kind, out long data)
    {
        kind = (byte)v.Kind;
        switch (v.Kind)
        {
            case CellValueKind.Number:
                data = BitConverter.DoubleToInt64Bits(v.Number);
                break;
            case CellValueKind.Boolean:
                data = v.Boolean ? 1 : 0;
                break;
            case CellValueKind.Text:
                data = v.StringRef;
                break;
            case CellValueKind.Error:
                data = v.ErrorRef;
                break;
            default:
                data = 0;
                break;
        }
    }
}

/// <summary>
/// The cells of one sheet. Rows are sparse (only populated rows exist, sorted by row number) and each row keeps its cells as parallel
/// arrays sorted by column, so iteration by row is a linear scan and a lookup is two binary searches. About 15 bytes per cell plus
/// the row's fixed cost, and 4 more per cell in rows that hold formulas. Formulas are interned records; strings are handles
/// (<see cref="CellValue"/>), so the store holds no per-cell objects.
/// </summary>
/// <remarks>
/// <para>Row and column numbers are 1-based as in <see cref="CellAddress"/>. Reading is safe from several threads when nobody edits;
/// editing is not thread safe.</para>
/// <para>What the store does not model (unknown cell attributes and children, unknown row attributes and children) is kept in side tables and written back unchanged.</para>
/// </remarks>
public sealed class CellStore
{
    private readonly List<RowData> _rows = [];
    private readonly List<CellFormula> _formulas = [];
    private readonly Dictionary<CellFormula, int> _formulaIndex = [];
    private readonly List<PoolString> _pool = [];
    private readonly Dictionary<long, CellExtras> _extras = [];
    private long _cellCount;
    private int _lastRowIndex = -1;

    /// <summary>Gets the number of rows that exist (they hold cells or row properties).</summary>
    public int RowCount => _rows.Count;

    /// <summary>Gets the number of cells that exist (a cell with only a style counts).</summary>
    public long CellCount => _cellCount;

    /// <summary>Gets a value indicating whether anything was changed since the store was loaded or last marked clean.</summary>
    public bool IsModified { get; internal set; }

    /// <summary>Gets a value indicating whether a formula was removed or replaced since the load (the calculation chain of the file is then stale).</summary>
    internal bool FormulaRemoved { get; set; }

    internal List<RowData> Rows => _rows;

    internal IReadOnlyList<PoolString> Pool => _pool;

    internal int FormulaPoolCount => _formulas.Count;

    /// <summary>Estimates the managed memory the store holds, in bytes (arrays, row objects, formula and string pools); a figure for budgeting, not an exact count.</summary>
    /// <returns>The estimate.</returns>
    public long ApproximateBytes()
    {
        long total = 0;
        foreach (RowData r in _rows)
        {
            total += 120 + (4 * 24) + ((long)r.Cols.Length * (2 + 1 + 8 + 4)) + (r.Formulas is null ? 0 : 24 + ((long)r.Formulas.Length * 4));
        }

        return total + (_formulas.Count * 80L) + (_pool.Count * 56L) + (_extras.Count * 100L);
    }

    /// <summary>Gets the smallest range holding every cell, or an invalid range when the sheet has none.</summary>
    /// <returns>The used range.</returns>
    public CellRange UsedRange()
    {
        int minRow = int.MaxValue;
        int maxRow = 0;
        int minCol = int.MaxValue;
        int maxCol = 0;
        foreach (RowData r in _rows)
        {
            if (r.Count == 0)
            {
                continue;
            }

            minRow = Math.Min(minRow, r.Row);
            maxRow = Math.Max(maxRow, r.Row);
            minCol = Math.Min(minCol, r.Cols[0] + 1);
            maxCol = Math.Max(maxCol, r.Cols[r.Count - 1] + 1);
        }

        return maxRow == 0 ? default : new CellRange(new CellAddress(minRow, minCol), new CellAddress(maxRow, maxCol));
    }

    /// <summary>Reads a cell.</summary>
    /// <param name="row">The 1-based row.</param>
    /// <param name="column">The 1-based column.</param>
    /// <param name="value">The value (cached result for a formula cell).</param>
    /// <param name="styleIndex">The style index (0 when none).</param>
    /// <returns>True when the cell exists.</returns>
    public bool TryGetCell(int row, int column, out CellValue value, out int styleIndex)
    {
        RowData? r = FindRow(row);
        if (r is not null)
        {
            int at = r.Find(column - 1);
            if (at >= 0)
            {
                value = RowData.Decode(r.Kinds[at], r.Data[at]);
                styleIndex = r.Styles[at];
                return true;
            }
        }

        value = default;
        styleIndex = 0;
        return false;
    }

    /// <summary>Reads the formula of a cell.</summary>
    /// <param name="row">The 1-based row.</param>
    /// <param name="column">The 1-based column.</param>
    /// <returns>The formula, or null when the cell has none.</returns>
    public CellFormula? GetFormula(int row, int column)
    {
        RowData? r = FindRow(row);
        if (r?.Formulas is { } f)
        {
            int at = r.Find(column - 1);
            if (at >= 0 && f[at] != 0)
            {
                return _formulas[f[at] - 1];
            }
        }

        return null;
    }

    /// <summary>Enumerates the populated rows in order.</summary>
    /// <returns>The rows.</returns>
    public RowEnumerable EnumerateRows() => new(this);

    /// <summary>Finds a populated row.</summary>
    /// <param name="row">The 1-based row.</param>
    /// <param name="view">The row when the result is true.</param>
    /// <returns>True when the row exists.</returns>
    public bool TryGetRow(int row, out CellRowView view)
    {
        RowData? r = FindRow(row);
        view = r is null ? default : new CellRowView(this, r);
        return r is not null;
    }

    /// <summary>Enumerates the cells of one column in row order, without allocating.</summary>
    /// <param name="column">The 1-based column.</param>
    /// <returns>The cells that exist in the column.</returns>
    public ColumnEnumerable EnumerateColumn(int column) => new(this, column);

    internal RowData? FindRow(int row)
    {
        int last = _lastRowIndex;
        if ((uint)last < (uint)_rows.Count && _rows[last].Row == row)
        {
            return _rows[last];
        }

        int idx = FindRowIndex(row);
        if (idx >= 0)
        {
            _lastRowIndex = idx;
            return _rows[idx];
        }

        return null;
    }

    internal int FindRowIndex(int row)
    {
        int lo = 0;
        int hi = _rows.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int r = _rows[mid].Row;
            if (r == row)
            {
                return mid;
            }

            if (r < row)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    internal RowData GetOrAddRow(int row)
    {
        if (_rows.Count == 0 || _rows[^1].Row < row)
        {
            var added = new RowData(row);
            _rows.Add(added);
            _lastRowIndex = _rows.Count - 1;
            return added;
        }

        int idx = FindRowIndex(row);
        if (idx >= 0)
        {
            _lastRowIndex = idx;
            return _rows[idx];
        }

        var created = new RowData(row);
        _rows.Insert(~idx, created);
        _lastRowIndex = ~idx;
        return created;
    }

    /// <summary>Appends a finished row while loading. The row must come after every existing row.</summary>
    internal void AppendRow(RowData row)
    {
        _rows.Add(row);
        _cellCount += row.Count;
    }

    internal void ReplaceRow(RowData existing, RowData replacement)
    {
        int idx = FindRowIndex(existing.Row);
        _cellCount += replacement.Count - existing.Count;
        _rows[idx] = replacement;
    }

    internal void InsertLoadedRow(RowData row)
    {
        int idx = FindRowIndex(row.Row);
        _rows.Insert(~idx, row);
        _cellCount += row.Count;
    }

    internal int InternFormula(CellFormula formula)
    {
        if (_formulaIndex.TryGetValue(formula, out int existing))
        {
            return existing;
        }

        _formulas.Add(formula);
        int id = _formulas.Count; // 1-based; 0 means no formula
        _formulaIndex[formula] = id;
        return id;
    }

    internal CellFormula FormulaAt(int id) => _formulas[id - 1];

    internal int AddPoolString(string text, byte[]? rawInline = null)
    {
        _pool.Add(new PoolString(text, rawInline));
        return ~(_pool.Count - 1);
    }

    internal PoolString PoolAt(int handle) => _pool[~handle];

    internal CellExtras? GetExtras(int row, int column) => _extras.TryGetValue(ExtraKey(row, column), out CellExtras? e) ? e : null;

    internal void SetExtras(int row, int column, CellExtras? extras)
    {
        long key = ExtraKey(row, column);
        if (extras is null)
        {
            _extras.Remove(key);
        }
        else
        {
            _extras[key] = extras;
        }
    }

    internal bool HasExtras => _extras.Count > 0;

    private static long ExtraKey(int row, int column) => ((long)row << 14) | (uint)(column - 1);

    internal int GetOrAddCell(int row, int column, out RowData data)
    {
        data = GetOrAddRow(row);
        int at = data.Find(column - 1);
        if (at < 0)
        {
            at = ~at;
            data.InsertSlot(at);
            data.Cols[at] = (ushort)(column - 1);
            data.Kinds[at] = 0;
            data.Data[at] = 0;
            data.Styles[at] = 0;
            _cellCount++;
        }

        IsModified = true;
        return at;
    }

    /// <summary>Gets a row for editing its properties, creating it if needed.</summary>
    internal RowData EditRow(int row)
    {
        IsModified = true;
        return GetOrAddRow(row);
    }

    /// <summary>Told after a cell's value or formula changes through an edit (column 0 means a whole row); the flag is true when a formula was set, replaced or removed; set by workbook calculation to track edits.</summary>
    internal Action<int, int, bool>? Changed { get; set; }

    /// <summary>Finds the <c>cm</c> value of an array formula cell, which marks a dynamic array; null when no cell has one.</summary>
    internal int? FindDynamicArrayMetadataIndex()
    {
        foreach (CellExtras e in _extras.Values)
        {
            string? a = e.Attributes;
            int at = a is null ? -1 : a.IndexOf(" cm=\"", StringComparison.Ordinal);
            if (at >= 0)
            {
                int start = at + 5;
                int end = a!.IndexOf('"', start);
                if (end > start && int.TryParse(a.AsSpan(start, end - start), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int cm))
                {
                    return cm;
                }
            }
        }

        return null;
    }

    /// <summary>Reads a cell and says whether it holds a formula.</summary>
    internal bool TryGetCellWithFormula(int row, int column, out CellValue value, out bool hasFormula)
    {
        RowData? r = FindRow(row);
        if (r is not null)
        {
            int at = r.Find(column - 1);
            if (at >= 0)
            {
                value = RowData.Decode(r.Kinds[at], r.Data[at]);
                hasFormula = (r.Kinds[at] & RowData.HasFormulaBit) != 0;
                return true;
            }
        }

        value = default;
        hasFormula = false;
        return false;
    }

    internal void SetValue(int row, int column, in CellValue value)
    {
        Changed?.Invoke(row, column, false);
        int at = GetOrAddCell(row, column, out RowData r);
        RowData.Encode(value, out byte kind, out long data);
        r.Kinds[at] = (byte)(kind | (r.Kinds[at] & RowData.HasFormulaBit));
        r.Data[at] = data;
    }

    internal void SetStyle(int row, int column, int style)
    {
        int at = GetOrAddCell(row, column, out RowData r);
        r.Styles[at] = style;
    }

    internal void SetFormula(int row, int column, CellFormula? formula)
    {
        Changed?.Invoke(row, column, true);
        int at = GetOrAddCell(row, column, out RowData r);
        bool had = (r.Kinds[at] & RowData.HasFormulaBit) != 0;
        if (had)
        {
            FormulaRemoved = true;
        }

        if (formula is null)
        {
            r.Kinds[at] = (byte)(r.Kinds[at] & ~RowData.HasFormulaBit);
            if (r.Formulas is not null)
            {
                r.Formulas[at] = 0;
            }

            return;
        }

        r.EnsureFormulaArray();
        r.Formulas![at] = InternFormula(formula);
        r.Kinds[at] = (byte)(r.Kinds[at] | RowData.HasFormulaBit);
    }

    internal bool RemoveCell(int row, int column)
    {
        RowData? r = FindRow(row);
        if (r is null)
        {
            return false;
        }

        int at = r.Find(column - 1);
        if (at < 0)
        {
            return false;
        }

        Changed?.Invoke(row, column, (r.Kinds[at] & RowData.HasFormulaBit) != 0);
        if ((r.Kinds[at] & RowData.HasFormulaBit) != 0)
        {
            FormulaRemoved = true;
        }

        r.RemoveSlot(at);
        _cellCount--;
        _extras.Remove(ExtraKey(row, column));
        IsModified = true;
        return true;
    }

    /// <summary>Removes the row and everything in it.</summary>
    internal void RemoveRow(int row)
    {
        int idx = FindRowIndex(row);
        if (idx < 0)
        {
            return;
        }

        RowData r = _rows[idx];
        Changed?.Invoke(row, 0, true);
        for (int i = 0; i < r.Count; i++)
        {
            if ((r.Kinds[i] & RowData.HasFormulaBit) != 0)
            {
                FormulaRemoved = true;
            }

            _extras.Remove(ExtraKey(row, r.Cols[i] + 1));
        }

        _cellCount -= r.Count;
        _rows.RemoveAt(idx);
        _lastRowIndex = -1;
        IsModified = true;
    }

    /// <summary>Gets a bulk copy of values and styles of one row segment.</summary>
    internal static int CopyRowValues(RowData r, int fromColumn, Span<CellValue> destination)
    {
        int n = 0;
        int start = r.Find(fromColumn - 1);
        if (start < 0)
        {
            start = ~start;
        }

        for (int i = start; i < r.Count && n < destination.Length; i++, n++)
        {
            destination[n] = RowData.Decode(r.Kinds[i], r.Data[i]);
        }

        return n;
    }

    /// <summary>Enumerates populated rows.</summary>
    public readonly struct RowEnumerable
    {
        private readonly CellStore _store;

        internal RowEnumerable(CellStore store) => _store = store;

        /// <summary>Gets the enumerator.</summary>
        /// <returns>The enumerator.</returns>
        public Enumerator GetEnumerator() => new(_store);

        /// <summary>Walks the rows.</summary>
        public struct Enumerator
        {
            private readonly CellStore _store;
            private int _i;

            internal Enumerator(CellStore store)
            {
                _store = store;
                _i = -1;
            }

            /// <summary>Gets the current row.</summary>
            public readonly CellRowView Current => new(_store, _store._rows[_i]);

            /// <summary>Moves to the next row.</summary>
            /// <returns>False at the end.</returns>
            public bool MoveNext() => ++_i < _store._rows.Count;
        }
    }

    /// <summary>Enumerates the cells of one column.</summary>
    public readonly struct ColumnEnumerable
    {
        private readonly CellStore _store;
        private readonly int _column;

        internal ColumnEnumerable(CellStore store, int column)
        {
            _store = store;
            _column = column;
        }

        /// <summary>Gets the enumerator.</summary>
        /// <returns>The enumerator.</returns>
        public Enumerator GetEnumerator() => new(_store, _column);

        /// <summary>Walks the cells.</summary>
        public struct Enumerator
        {
            private readonly CellStore _store;
            private readonly int _col0;
            private int _i;
            private int _at;

            internal Enumerator(CellStore store, int column)
            {
                _store = store;
                _col0 = column - 1;
                _i = -1;
                _at = -1;
            }

            /// <summary>Gets the current cell as (row, value, style).</summary>
            public readonly (int Row, CellValue Value, int Style) Current
            {
                get
                {
                    RowData r = _store._rows[_i];
                    return (r.Row, RowData.Decode(r.Kinds[_at], r.Data[_at]), r.Styles[_at]);
                }
            }

            /// <summary>Moves to the next cell of the column.</summary>
            /// <returns>False at the end.</returns>
            public bool MoveNext()
            {
                while (++_i < _store._rows.Count)
                {
                    RowData r = _store._rows[_i];
                    int at = r.Find(_col0);
                    if (at >= 0)
                    {
                        _at = at;
                        return true;
                    }
                }

                return false;
            }
        }
    }
}

/// <summary>A read-only view of one populated row of a <see cref="CellStore"/>. The cells are indexed 0 to <see cref="Count"/> - 1 in column order.</summary>
public readonly struct CellRowView
{
    private readonly CellStore _store;
    private readonly RowData _row;

    internal CellRowView(CellStore store, RowData row)
    {
        _store = store;
        _row = row;
    }

    /// <summary>Gets the 1-based row number.</summary>
    public int Row => _row.Row;

    /// <summary>Gets the number of cells in the row.</summary>
    public int Count => _row.Count;

    /// <summary>Gets the 1-based column of the i-th cell.</summary>
    /// <param name="i">The index, 0 to <see cref="Count"/> - 1.</param>
    /// <returns>The column.</returns>
    public int ColumnAt(int i) => _row.Cols[CheckIndex(i)] + 1;

    /// <summary>Gets the value of the i-th cell.</summary>
    /// <param name="i">The index.</param>
    /// <returns>The value.</returns>
    public CellValue ValueAt(int i) => RowData.Decode(_row.Kinds[CheckIndex(i)], _row.Data[i]);

    /// <summary>Gets the style index of the i-th cell.</summary>
    /// <param name="i">The index.</param>
    /// <returns>The style index.</returns>
    public int StyleAt(int i) => _row.Styles[CheckIndex(i)];

    /// <summary>Tells whether the i-th cell has a formula.</summary>
    /// <param name="i">The index.</param>
    /// <returns>True when it has.</returns>
    public bool HasFormulaAt(int i) => (_row.Kinds[CheckIndex(i)] & RowData.HasFormulaBit) != 0;

    /// <summary>Gets the formula of the i-th cell.</summary>
    /// <param name="i">The index.</param>
    /// <returns>The formula or null.</returns>
    public CellFormula? FormulaAt(int i) => _row.Formulas is { } f && f[CheckIndex(i)] != 0 ? _store.FormulaAt(f[i]) : null;

    /// <summary>Copies the columns of the row into a span.</summary>
    /// <param name="destination">At least <see cref="Count"/> elements for the whole row; shorter spans receive the first cells.</param>
    /// <returns>The number copied.</returns>
    public int CopyColumns(Span<int> destination)
    {
        int n = Math.Min(destination.Length, _row.Count);
        for (int i = 0; i < n; i++)
        {
            destination[i] = _row.Cols[i] + 1;
        }

        return n;
    }

    /// <summary>Copies the values of the row into a span, in column order.</summary>
    /// <param name="destination">Receives the first cells of the row.</param>
    /// <returns>The number copied.</returns>
    public int CopyValues(Span<CellValue> destination)
    {
        int n = Math.Min(destination.Length, _row.Count);
        for (int i = 0; i < n; i++)
        {
            destination[i] = RowData.Decode(_row.Kinds[i], _row.Data[i]);
        }

        return n;
    }

    private int CheckIndex(int i)
    {
        if ((uint)i >= (uint)_row.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(i));
        }

        return i;
    }
}
