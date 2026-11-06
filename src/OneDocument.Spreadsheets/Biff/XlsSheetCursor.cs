using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Spreadsheets.Streaming;

/// <summary>
/// A forward-only cursor over the rows and cells of one worksheet of a legacy binary workbook (BIFF, [MS-XLS] §2.4). Call
/// <see cref="ReadRow"/> to move to the next row that has cells and <see cref="ReadCell"/> to move through its cells; the properties
/// describe the current row or cell and stay valid until the cursor moves. It has the members of <see cref="SheetCursor"/> that a binary
/// workbook can answer; there is no formula text (the tokens are not decoded), only <see cref="HasFormula"/> and <see cref="FormulaKind"/>.
/// </summary>
/// <remarks>
/// <para>
/// Memory is bounded by the largest record of the sheet (a record is at most 8,224 bytes, 65,535 in a damaged file) and reading allocates
/// nothing per cell: numbers, booleans, errors, styles and shared-string indexes are plain values, and text is available as a span or a
/// copy into the caller's buffer; only <see cref="GetText"/> builds a string (once per shared string, within a budget).
/// </para>
/// <para>
/// Rows are the rows that have cell records, in the order the file stores them; the cells of a row come in column order. A sheet that
/// stores them out of order is reported (<c>xls.stream-row-order</c>, <c>xls.stream-cell-order</c>) and delivered as stored. Cells that
/// hold only a format (a blank with a style) are delivered with <see cref="CellValueKind.Empty"/>. The row properties come from the
/// <c>ROW</c> record of the row, which the file stores before its cells; a row without one has the defaults.
/// </para>
/// <para>Failures raise <see cref="OfficeFormatException"/> or <see cref="OfficeLimitException"/> located by stream and record offset; repairs go to
/// <see cref="OpenContext.Defects"/>.</para>
/// </remarks>
public sealed class XlsSheetCursor : ICellCursor
{
    private readonly XlsStreamReader _owner;
    private readonly Stream? _data;
    private readonly BiffRecordStream? _rs;
    private readonly CancellationToken _cancellation;
    private readonly OpenContext _context;
    private readonly int _version;
    private readonly long _recordLimit;
    private readonly Dictionary<int, RowInfo> _rows = [];
    private long _records;
    private int _depth = 1;
    private bool _replay;
    private bool _ended;
    private bool _inRow;
    private bool _held;
    private int _row;
    private int _lastRow = -1;
    private int _lastCol = -1;
    private bool _cellOrderReported;
    private RowInfo _rowInfo = RowInfo.None;

    // The cell record that was decoded last.
    private int _cellRow;
    private int _col;
    private int _style;
    private CellValueKind _kind;
    private double _number;
    private CellError _error;
    private int _sharedIndex = -1;
    private bool _hasFormula;
    private CellFormulaKind _formulaKind;
    private char[] _chars = new char[256];
    private int _charsLength;
    private byte[] _utf8 = new byte[256];

    // A MULRK or MULBLANK record holds several cells; the record's bytes stay in the reader until they are all delivered.
    private int _mulType;
    private int _mulRow;
    private int _mulCol;
    private int _mulLeft;
    private int _mulAt;

    private readonly struct RowInfo(double height, int flags, int outline, int style)
    {
        public static readonly RowInfo None = new(-1, 0, 0, 0);
        public double Height { get; } = height;
        public int Flags { get; } = flags;
        public int Outline { get; } = outline;
        public int Style { get; } = style;
    }

    internal XlsSheetCursor(XlsStreamReader owner, string sheetName, long offset, CancellationToken cancellation)
    {
        _owner = owner;
        _context = owner.Context;
        _version = owner.Version;
        _cancellation = cancellation;
        _recordLimit = _context.Limits.MaxRecordCount;
        SheetName = sheetName;
        _data = owner.OpenData();
        _rs = new BiffRecordStream(_data);
        _rs.Seek(offset);
        if (!_rs.Read() || _rs.Type is not (0x0809 or 0x0409 or 0x0209 or 0x0009))
        {
            _context.Defects.Report(new Defect(DefectKind.Record, "biff.sheet-missing", DefectSeverity.Dropped,
                "The sheet '" + sheetName + "' has no substream at offset " + offset.ToString(CultureInfo.InvariantCulture) + "; it reads as empty.", DefectLocation.InRecord(owner.StreamPath, offset)));
            _ended = true;
            return;
        }

        ReadPrologue();
    }

    /// <summary>Gets the sheet's name.</summary>
    public string SheetName { get; }

    /// <summary>Gets the dimension the sheet declares (<c>DIMENSIONS</c>), or null when it declares none or an unreadable one. It is a hint, not checked against the cells.</summary>
    public CellRange? DeclaredDimension { get; private set; }

    /// <summary>Gets the column properties declared before the cells (<c>COLINFO</c>), in stored order.</summary>
    public IReadOnlyList<ColumnProperties> Columns { get; private set; } = [];

    private static bool IsCell(int t) =>
        t is 0x0203 or 0x0003 or 0x0002 or 0x027E or 0x00BD or 0x0201 or 0x0001 or 0x00BE or 0x00FD or 0x0204 or 0x0004 or 0x00D6 or 0x0205 or 0x0005 or 0x0006 or 0x0206 or 0x0406;

    // The column and dimension records come before the first row of the sheet ([MS-XLS] §2.1.7.20.5 Worksheet substream).
    private void ReadPrologue()
    {
        var cols = new List<ColumnProperties>();
        BiffRecordStream rs = _rs!;
        while (true)
        {
            if (!rs.Read())
            {
                FinishStream();
                break;
            }

            _records++;
            int t = rs.Type;
            if (t is 0x0208 or 0x0008 or 0x00D7 or 0x000A or 0x0809 or 0x0409 or 0x0209 or 0x0009 || IsCell(t))
            {
                _replay = true;
                break;
            }

            ReadOnlySpan<byte> d = rs.Data;
            if (t == 0x007D)
            {
                int first = U16(d, 0);
                int last = U16(d, 2);
                int f = U16(d, 8);
                if (last < first || first > 255)
                {
                    _context.Defects.Report(new Defect(DefectKind.Record, "biff.colinfo-range", DefectSeverity.Dropped,
                        "A column range runs from " + first.ToString(CultureInfo.InvariantCulture) + " to " + last.ToString(CultureInfo.InvariantCulture) + ".", DefectLocation.InRecord(_owner.StreamPath, rs.Offset)));
                    continue;
                }

                ColumnOptions o = ((f & 1) != 0 ? ColumnOptions.Hidden : 0) | ((f & 2) != 0 ? ColumnOptions.CustomWidth : 0) | ((f & 4) != 0 ? ColumnOptions.BestFit : 0)
                    | ((f & 8) != 0 ? ColumnOptions.Phonetic : 0) | ((f & 0x1000) != 0 ? ColumnOptions.Collapsed : 0);
                cols.Add(new ColumnProperties(first + 1, Math.Min(last, 255) + 1, U16(d, 4) / 256.0, U16(d, 6), o, (f >> 8) & 7));
            }
            else if (t == 0x0024)
            {
                // BIFF2 COLWIDTH: first and last column as bytes.
                cols.Add(new ColumnProperties(U8(d, 0) + 1, U8(d, 1) + 1, U16(d, 2) / 256.0, 0, ColumnOptions.CustomWidth));
            }
            else if (t == 0x0200 || (t == 0x0000 && _version == 2))
            {
                // [MS-XLS] §2.4.90 Dimensions: first row, last row plus one, first column, last column plus one (16-bit rows before BIFF8).
                int r1;
                int r2;
                int c1;
                int c2;
                if (_version >= 8)
                {
                    r1 = (int)Math.Min(U32(d, 0), int.MaxValue);
                    r2 = (int)Math.Min(U32(d, 4), int.MaxValue) - 1;
                    c1 = U16(d, 8);
                    c2 = U16(d, 10) - 1;
                }
                else
                {
                    r1 = U16(d, 0);
                    r2 = U16(d, 2) - 1;
                    c1 = U16(d, 4);
                    c2 = U16(d, 6) - 1;
                }

                if (r2 >= r1 && c2 >= c1 && r2 < CellAddress.MaxRows && c2 < CellAddress.MaxColumns)
                {
                    DeclaredDimension = new CellRange(new CellAddress(r1 + 1, c1 + 1), new CellAddress(r2 + 1, c2 + 1));
                }
            }
        }

        Columns = cols;
    }

    // ---------------------------------------------------------------- the current row

    /// <summary>Gets the 1-based number of the current row. Meaningful after <see cref="ReadRow"/> returned true.</summary>
    public int RowNumber => _row + 1;

    /// <summary>Gets the row height in points when the row's height was set by hand, otherwise -1.</summary>
    public double RowHeight => _rowInfo.Height;

    /// <summary>Gets a value indicating whether the row is hidden.</summary>
    public bool IsRowHidden => (_rowInfo.Flags & 1) != 0;

    /// <summary>Gets a value indicating whether the row's height was set by hand.</summary>
    public bool HasCustomRowHeight => _rowInfo.Height >= 0;

    /// <summary>Gets a value indicating whether the row has a format of its own.</summary>
    public bool HasCustomRowFormat => (_rowInfo.Flags & 4) != 0;

    /// <summary>Gets a value indicating whether the row's outline group is collapsed.</summary>
    public bool IsRowCollapsed => (_rowInfo.Flags & 8) != 0;

    /// <summary>Gets the row's outline level, 0 to 7.</summary>
    public int RowOutlineLevel => _rowInfo.Outline;

    /// <summary>Gets the XF index of the row's own format, 0 when it has none.</summary>
    public int RowStyle => _rowInfo.Style;

    /// <summary>Moves to the next row that has cells, skipping the cells of the current row that were not read.</summary>
    /// <returns>False after the last row.</returns>
    /// <exception cref="OfficeFormatException">The sheet is damaged beyond repair.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">The cancellation token given to the reader was cancelled.</exception>
    public bool ReadRow()
    {
        try
        {
            return ReadRowCore();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or IOException))
        {
            throw XlsStreamReader.Guard(ex, _owner.StreamPath);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool ReadRowCore()
    {
        _cancellation.ThrowIfCancellationRequested();
        if (_inRow)
        {
            if (_held && _cellRow == _row)
            {
                _held = false;
            }

            while (!_held && Advance())
            {
                if (_cellRow != _row)
                {
                    _held = true;
                }
            }
        }

        if (!_held)
        {
            if (!Advance())
            {
                _inRow = false;
                return false;
            }

            _held = true;
        }

        _row = _cellRow;
        _inRow = true;
        _lastCol = -1;
        _cellOrderReported = false;
        _rowInfo = _rows.TryGetValue(_row, out RowInfo info) ? info : RowInfo.None;
        if (_row <= _lastRow)
        {
            _context.Defects.Report(new Defect(DefectKind.Structure, "xls.stream-row-order", DefectSeverity.Repaired,
                "Rows are not in ascending order; the streaming reader delivers them as stored.", DefectLocation.InRecord(_owner.StreamPath, _rs!.Offset)));
        }

        _lastRow = _row;
        return true;
    }

    // ---------------------------------------------------------------- the current cell

    /// <summary>Gets the 1-based column of the current cell.</summary>
    public int Column => _col + 1;

    /// <summary>Gets the address of the current cell.</summary>
    public CellAddress Address => new(_row + 1, _col + 1);

    /// <summary>Gets the XF index of the current cell, 0 when it has the default format.</summary>
    public int Style => _style;

    /// <summary>Gets the kind of the current cell's value; for a formula cell, the kind of its cached result.</summary>
    public CellValueKind Kind => _kind;

    /// <summary>
    /// Gets the value as the workbook model holds it. For text the handle is the shared string index when the cell uses the shared string
    /// table, and -1 otherwise (the text is the cell's own); read text through the cursor.
    /// </summary>
    public CellValue Value => _kind switch
    {
        CellValueKind.Number => CellValue.FromNumber(_number),
        CellValueKind.Boolean => CellValue.FromBoolean(_number != 0),
        CellValueKind.Error => CellValue.FromError(_error),
        CellValueKind.Text => new CellValue(CellValueKind.Text, 0, _sharedIndex),
        _ => default,
    };

    /// <summary>Gets the number of a <see cref="CellValueKind.Number"/> cell, 1 or 0 for a boolean, 0 otherwise.</summary>
    public double Number => _kind is CellValueKind.Number or CellValueKind.Boolean ? _number : 0;

    /// <summary>Gets a value indicating whether the cell holds TRUE.</summary>
    public bool Boolean => _kind == CellValueKind.Boolean && _number != 0;

    /// <summary>Gets the error of an error cell.</summary>
    public CellError Error => _kind == CellValueKind.Error ? _error : default;

    /// <summary>Gets the index into the shared string table when the cell's text is a shared string, otherwise -1.</summary>
    public int SharedStringIndex => _kind == CellValueKind.Text ? _sharedIndex : -1;

    /// <summary>Gets the text of a text cell as UTF-8 (the error text, such as <c>#N/A</c>, for an error cell), valid until the cursor moves. The characters are encoded into a reused buffer; nothing is allocated per cell.</summary>
    public ReadOnlySpan<byte> TextUtf8
    {
        get
        {
            if (_kind == CellValueKind.Text)
            {
                return Encode(_sharedIndex >= 0 ? _owner.Strings.Get(_sharedIndex) : _chars.AsSpan(0, _charsLength));
            }

            return _kind == CellValueKind.Error && (uint)_error <= (uint)CellError.Busy ? Encode(CellErrors.ToText(_error)) : default;
        }
    }

    private ReadOnlySpan<byte> Encode(ReadOnlySpan<char> text)
    {
        int max = Encoding.UTF8.GetMaxByteCount(text.Length);
        if (_utf8.Length < max)
        {
            _utf8 = new byte[Math.Max(max, _utf8.Length * 2)];
        }

        return _utf8.AsSpan(0, Encoding.UTF8.GetBytes(text, _utf8));
    }

    /// <summary>Copies the text of a text or error cell into a buffer without allocating.</summary>
    /// <param name="destination">The buffer.</param>
    /// <param name="written">The number of chars written, or the number the buffer would need when it is too small.</param>
    /// <returns>True when the text fits.</returns>
    public bool TryCopyText(Span<char> destination, out int written)
    {
        ReadOnlySpan<char> text;
        if (_kind == CellValueKind.Text)
        {
            text = _sharedIndex >= 0 ? _owner.Strings.Get(_sharedIndex) : _chars.AsSpan(0, _charsLength);
        }
        else if (_kind == CellValueKind.Error && (uint)_error <= (uint)CellError.Busy)
        {
            text = CellErrors.ToText(_error);
        }
        else
        {
            written = 0;
            return true;
        }

        written = text.Length;
        if (text.Length > destination.Length)
        {
            return false;
        }

        text.CopyTo(destination);
        return true;
    }

    /// <summary>Gets the text of a text or error cell as a string. A shared string is built once and reused (within a memory budget); other text allocates.</summary>
    /// <returns>The text; for an error cell, the error as written; empty for other kinds.</returns>
    public string GetText() => _kind switch
    {
        CellValueKind.Text => _sharedIndex >= 0 ? _owner.Strings.GetString(_sharedIndex) : _charsLength == 0 ? string.Empty : new string(_chars, 0, _charsLength),
        CellValueKind.Error => (uint)_error <= (uint)CellError.Busy ? CellErrors.ToText(_error) : string.Empty,
        _ => string.Empty,
    };

    /// <summary>Gets a value indicating whether the cell has a formula. The cursor reports the cached result Excel stored; the tokens are not decoded.</summary>
    public bool HasFormula => _hasFormula;

    /// <summary>Gets the kind of the cell's formula: ordinary, shared (the cell uses another cell's formula), array or data table.</summary>
    public CellFormulaKind FormulaKind => _formulaKind;

    /// <summary>Gets a value indicating whether the cell is a number whose format shows a date or time (see <see cref="XlsStreamReader.IsDateStyle"/>).</summary>
    public bool IsDate => _kind == CellValueKind.Number && _owner.IsDateStyle(_style);

    /// <summary>Reads the cell as a date and time, by the workbook's date system (the 1900 system's leap-year quirk included).</summary>
    /// <param name="value">The date and time when the result is true.</param>
    /// <returns>True when the cell is a date-formatted number inside the range of dates.</returns>
    public bool TryGetDateTime(out DateTime value)
    {
        if (IsDate)
        {
            return ExcelDate.TryToDateTime(_number, _owner.DateSystem, out value);
        }

        value = default;
        return false;
    }

    /// <summary>Moves to the next cell of the current row.</summary>
    /// <returns>False after the last cell of the row.</returns>
    /// <exception cref="OfficeFormatException">The sheet is damaged beyond repair.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public bool ReadCell()
    {
        try
        {
            return ReadCellCore();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or IOException))
        {
            throw XlsStreamReader.Guard(ex, _owner.StreamPath);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool ReadCellCore()
    {
        if (!_inRow)
        {
            return false;
        }

        if (_held)
        {
            _held = false;
        }
        else
        {
            if (!Advance())
            {
                return false;
            }

            if (_cellRow != _row)
            {
                _held = true;
                return false;
            }
        }

        if (_col <= _lastCol && !_cellOrderReported)
        {
            _cellOrderReported = true;
            _context.Defects.Report(new Defect(DefectKind.Structure, "xls.stream-cell-order", DefectSeverity.Repaired,
                "Cells of a row are not in ascending column order; the streaming reader delivers them as stored.", DefectLocation.InCell(SheetName, Address.ToString())));
        }

        _lastCol = _col;
        return true;
    }

    // ---------------------------------------------------------------- records

    private static int U8(ReadOnlySpan<byte> d, int at) => (uint)at < (uint)d.Length ? d[at] : 0;

    private static int U16(ReadOnlySpan<byte> d, int at) => (uint)(at + 1) < (uint)d.Length ? d[at] | (d[at + 1] << 8) : 0;

    private static uint U32(ReadOnlySpan<byte> d, int at) => at >= 0 && at + 4 <= d.Length ? BinaryPrimitives.ReadUInt32LittleEndian(d[at..]) : 0;

    private static double F64(ReadOnlySpan<byte> d, int at) => at >= 0 && at + 8 <= d.Length ? BinaryPrimitives.ReadDoubleLittleEndian(d[at..]) : 0;

    private void FinishStream()
    {
        if (_rs!.Truncated)
        {
            _context.Defects.Report(new Defect(DefectKind.Record, "biff.truncated-record", DefectSeverity.Dropped,
                "The stream ends inside a record; the record and anything after it are ignored.", DefectLocation.InRecord(_owner.StreamPath, _rs.Position)));
        }

        _ended = true;
    }

    // Loads the next cell into the current-cell fields. Row and column records between cells are read on the way.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool Advance()
    {
        while (true)
        {
            if (_mulLeft > 0)
            {
                if (NextMul())
                {
                    return true;
                }

                continue;
            }

            if (_ended)
            {
                return false;
            }

            BiffRecordStream rs = _rs!;
            if (_replay)
            {
                _replay = false;
            }
            else
            {
                if (!rs.Read())
                {
                    FinishStream();
                    return false;
                }

                if (++_records > _recordLimit)
                {
                    OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), _recordLimit, _records, DefectLocation.InRecord(_owner.StreamPath, rs.Offset));
                }
            }

            int type = rs.Type;
            if (type is 0x0809 or 0x0409 or 0x0209 or 0x0009)
            {
                _depth++;
                continue;
            }

            if (type == 0x000A)
            {
                if (--_depth <= 0)
                {
                    _ended = true;
                    return false;
                }

                continue;
            }

            if (_depth > 1)
            {
                // Inside an embedded chart substream.
                continue;
            }

            ReadOnlySpan<byte> d = rs.Data;
            switch (type)
            {
                case 0x0203:
                case 0x0003:
                {
                    int xf = CellXf(d, out int hdr);
                    if (Place(U16(d, 0), U16(d, 2), xf))
                    {
                        _kind = CellValueKind.Number;
                        _number = F64(d, hdr);
                        return true;
                    }

                    break;
                }

                case 0x0002:
                {
                    int xf = CellXf(d, out int hdr);
                    if (Place(U16(d, 0), U16(d, 2), xf))
                    {
                        _kind = CellValueKind.Number;
                        _number = U16(d, hdr);
                        return true;
                    }

                    break;
                }

                case 0x027E:
                    if (Place(U16(d, 0), U16(d, 2), U16(d, 4)))
                    {
                        _kind = CellValueKind.Number;
                        _number = XlsImporter.DecodeRk(U32(d, 6));
                        return true;
                    }

                    break;
                case 0x00BD:
                case 0x00BE:
                {
                    int first = U16(d, 2);
                    int last = U16(d, d.Length - 2);
                    int size = type == 0x00BD ? 6 : 2;
                    int n = Math.Min((d.Length - 6) / size, last - first + 1);
                    if (n > 0)
                    {
                        _mulType = type;
                        _mulRow = U16(d, 0);
                        _mulCol = first;
                        _mulLeft = n;
                        _mulAt = 4;
                    }

                    break;
                }

                case 0x0201:
                case 0x0001:
                {
                    int xf = CellXf(d, out _);
                    if (Place(U16(d, 0), U16(d, 2), xf))
                    {
                        return true;
                    }

                    break;
                }

                case 0x00FD:
                    if (LabelSst(d))
                    {
                        return true;
                    }

                    break;
                case 0x0204:
                case 0x0004:
                case 0x00D6:
                    if (Label(type))
                    {
                        return true;
                    }

                    break;
                case 0x0205:
                case 0x0005:
                {
                    int xf = CellXf(d, out int hdr);
                    int value = U8(d, hdr);
                    bool isError = U8(d, hdr + 1) != 0;
                    if (Place(U16(d, 0), U16(d, 2), xf))
                    {
                        if (isError)
                        {
                            _kind = CellValueKind.Error;
                            _error = XlsImporter.ErrorOf(value);
                        }
                        else
                        {
                            _kind = CellValueKind.Boolean;
                            _number = value != 0 ? 1 : 0;
                        }

                        return true;
                    }

                    break;
                }

                case 0x0006:
                case 0x0206:
                case 0x0406:
                    if (Formula(d))
                    {
                        return true;
                    }

                    break;
                case 0x0207:
                case 0x0007:
                    _context.Defects.Report(new Defect(DefectKind.Record, "biff.string-orphan", DefectSeverity.Dropped,
                        "A STRING record does not follow a formula that needs one.", DefectLocation.InRecord(_owner.StreamPath, rs.Offset)));
                    break;
                case 0x0208:
                case 0x0008:
                    RowRecord(d);
                    break;
                case 0x00D7:
                    _rows.Clear();
                    break;
                default:
                    break;
            }
        }
    }

    private bool Place(int row, int col, int xf)
    {
        if (col >= CellAddress.MaxColumns)
        {
            _context.Defects.Report(new Defect(DefectKind.Reference, "biff.cell-out-of-range", DefectSeverity.Dropped,
                "A cell at row " + (row + 1).ToString(CultureInfo.InvariantCulture) + ", column " + (col + 1).ToString(CultureInfo.InvariantCulture) + " is outside the sheet.", DefectLocation.InRecord(_owner.StreamPath, _rs!.Offset)));
            return false;
        }

        _cellRow = row;
        _col = col;
        _style = xf;
        _kind = CellValueKind.Empty;
        _number = 0;
        _error = default;
        _sharedIndex = -1;
        _hasFormula = false;
        _formulaKind = CellFormulaKind.Normal;
        _charsLength = 0;
        return true;
    }

    // BIFF2 cells carry three attribute bytes where later versions carry a 16-bit XF index ([MS-XLS] has no BIFF2 layout; observed files).
    private int CellXf(ReadOnlySpan<byte> d, out int headerLength)
    {
        if (_version >= 3)
        {
            headerLength = 6;
            return U16(d, 4);
        }

        headerLength = 7;
        return _owner.SyntheticXf(U8(d, 4), U8(d, 5), U8(d, 6));
    }

    private bool NextMul()
    {
        ReadOnlySpan<byte> d = _rs!.Data;
        int at = _mulAt;
        bool placed = Place(_mulRow, _mulCol, U16(d, at));
        if (_mulType == 0x00BD)
        {
            _kind = CellValueKind.Number;
            _number = XlsImporter.DecodeRk(U32(d, at + 2));
            _mulAt += 6;
        }
        else
        {
            _mulAt += 2;
        }

        _mulCol++;
        _mulLeft--;
        return placed;
    }

    private bool LabelSst(ReadOnlySpan<byte> d)
    {
        long isst = U32(d, 6);
        if (isst >= _owner.Strings.Count)
        {
            _context.Defects.Report(new Defect(DefectKind.Reference, "biff.sst-index", DefectSeverity.Dropped,
                "A text cell refers to shared string " + isst.ToString(CultureInfo.InvariantCulture) + ", which the table does not hold.", DefectLocation.InRecord(_owner.StreamPath, _rs!.Offset)));
            return Place(U16(d, 0), U16(d, 2), U16(d, 4));
        }

        if (!Place(U16(d, 0), U16(d, 2), U16(d, 4)))
        {
            return false;
        }

        _kind = CellValueKind.Text;
        _sharedIndex = (int)isst;
        return true;
    }

    private bool Label(int type)
    {
        BiffRecordStream rs = _rs!;
        ReadOnlySpan<byte> d = rs.Data;
        if (type == 0x00D6 || _version >= 8)
        {
            // A BIFF8 label may carry rich-text runs and may be split by CONTINUE: the general reader handles it; the common
            // plain case below has no continuation and no runs.
            if (type != 0x00D6 && U16(d, 6) is int cch && (U8(d, 8) & ~1) == 0 && !rs.NextIsContinue())
            {
                bool wide = (d[8] & 1) != 0;
                int bytes = wide ? cch * 2 : cch;
                if (9 + bytes <= d.Length)
                {
                    if (!Place(U16(d, 0), U16(d, 2), U16(d, 4)))
                    {
                        return false;
                    }

                    _kind = CellValueKind.Text;
                    SetChars(d.Slice(9, bytes), wide, Encoding.Latin1);
                    return true;
                }
            }

            RecordReader rd = ReadJoined();
            int row = rd.U16();
            int col = rd.U16();
            int xf = rd.U16();
            if (!Place(row, col, xf))
            {
                return false;
            }

            _kind = CellValueKind.Text;
            SetString(rd.UnicodeString(2, true, _owner.Enc8));
            return true;
        }

        if (_version >= 3)
        {
            int cch = U16(d, 6);
            if (!Place(U16(d, 0), U16(d, 2), U16(d, 4)))
            {
                return false;
            }

            _kind = CellValueKind.Text;
            SetChars(d.Slice(Math.Min(8, d.Length), Math.Min(cch, Math.Max(0, d.Length - 8))), false, _owner.Enc8);
            return true;
        }

        {
            int xf = CellXf(d, out int hdr);
            int len = U8(d, hdr);
            if (!Place(U16(d, 0), U16(d, 2), xf))
            {
                return false;
            }

            _kind = CellValueKind.Text;
            SetChars(d.Slice(Math.Min(hdr + 1, d.Length), Math.Min(len, Math.Max(0, d.Length - hdr - 1))), false, _owner.Enc8);
            return true;
        }
    }

    // The current record and the CONTINUE records after it, kept apart because a string split by a CONTINUE restarts its flag byte.
    private RecordReader ReadJoined()
    {
        BiffRecordStream rs = _rs!;
        var parts = new List<ReadOnlyMemory<byte>> { rs.Data.ToArray() };
        long total = rs.Length;
        while (rs.NextIsContinue())
        {
            rs.Read();
            total += rs.Length;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _context.Limits.MaxAllocationBytes, total, DefectLocation.InRecord(_owner.StreamPath, rs.Offset));
            parts.Add(rs.Data.ToArray());
        }

        return RecordReader.FromParts([.. parts]);
    }

    private void SetChars(ReadOnlySpan<byte> bytes, bool wide, Encoding enc)
    {
        int max = wide ? bytes.Length / 2 : bytes.Length;
        if (_chars.Length < max)
        {
            _chars = new char[Math.Max(max, _chars.Length * 2)];
        }

        _charsLength = wide ? Encoding.Unicode.GetChars(bytes[..(max * 2)], _chars) : enc.GetChars(bytes, _chars);
    }

    private void SetString(string s)
    {
        if (_chars.Length < s.Length)
        {
            _chars = new char[Math.Max(s.Length, _chars.Length * 2)];
        }

        s.CopyTo(_chars);
        _charsLength = s.Length;
    }

    // [MS-XLS] §2.4.127 Formula: cell, 8-byte cached value, flags, 4 unused bytes (BIFF5 and later), the token length and the tokens.
    // A value whose last two bytes are 0xFFFF is not a number: 0 string (in the STRING record that follows), 1 boolean, 2 error, 3 empty text.
    private bool Formula(ReadOnlySpan<byte> d)
    {
        int xf;
        int vAt;
        int cceAt;
        if (_version >= 3)
        {
            xf = U16(d, 4);
            vAt = 6;
            cceAt = _version >= 5 ? 20 : 16;
        }
        else
        {
            xf = CellXf(d, out _);
            vAt = 7;
            cceAt = 16;
        }

        if (!Place(U16(d, 0), U16(d, 2), xf))
        {
            return false;
        }

        _hasFormula = true;
        bool wantsString = false;
        if (U16(d, vAt + 6) == 0xFFFF)
        {
            switch (U8(d, vAt))
            {
                case 0:
                    wantsString = true;
                    _kind = CellValueKind.Text;
                    break;
                case 1:
                    _kind = CellValueKind.Boolean;
                    _number = U8(d, vAt + 2) != 0 ? 1 : 0;
                    break;
                case 2:
                    _kind = CellValueKind.Error;
                    _error = XlsImporter.ErrorOf(U8(d, vAt + 2));
                    break;
                default:
                    _kind = CellValueKind.Text;
                    break;
            }
        }
        else
        {
            _kind = CellValueKind.Number;
            _number = F64(d, vAt);
        }

        // PtgExp (0x01) and PtgTbl (0x02) alone mean the formula lives in a SHRFMLA, ARRAY or TABLE record ([MS-XLS] §2.5.198.58, §2.5.198.100).
        int cce = _version >= 3 ? U16(d, cceAt) : U8(d, cceAt);
        int rgceAt = _version >= 3 ? cceAt + 2 : 17;
        if (cce == 5 && rgceAt < d.Length)
        {
            bool shared = _version >= 3 ? (U16(d, vAt + 8) & 8) != 0 : (U8(d, 15) & 8) != 0;
            _formulaKind = d[rgceAt] == 0x01 ? (shared ? CellFormulaKind.Shared : CellFormulaKind.Array) : d[rgceAt] == 0x02 ? CellFormulaKind.DataTable : CellFormulaKind.Normal;
        }

        if (wantsString)
        {
            ReadFormulaString();
        }

        return true;
    }

    // The text result of a formula is in the STRING record after it, which may follow a SHRFMLA, ARRAY or TABLE record.
    private void ReadFormulaString()
    {
        BiffRecordStream rs = _rs!;
        while (rs.Read())
        {
            _records++;
            int t = rs.Type;
            if (t is 0x04BC or 0x0021 or 0x0221 or 0x0236 or 0x0036)
            {
                continue;
            }

            if (t is 0x0207 or 0x0007)
            {
                StringRecord();
            }
            else
            {
                _replay = true;
            }

            return;
        }

        FinishStream();
    }

    private void StringRecord()
    {
        BiffRecordStream rs = _rs!;
        ReadOnlySpan<byte> d = rs.Data;
        if (_version >= 8)
        {
            int cch = U16(d, 0);
            if ((U8(d, 2) & ~1) == 0 && d.Length >= 3 && !rs.NextIsContinue())
            {
                bool wide = (d[2] & 1) != 0;
                int bytes = wide ? cch * 2 : cch;
                if (3 + bytes <= d.Length)
                {
                    SetChars(d.Slice(3, bytes), wide, Encoding.Latin1);
                    return;
                }
            }

            SetString(ReadJoined().UnicodeString(2, true, _owner.Enc8));
        }
        else if (_version >= 3)
        {
            SetChars(d.Slice(Math.Min(2, d.Length), Math.Min(U16(d, 0), Math.Max(0, d.Length - 2))), false, _owner.Enc8);
        }
        else
        {
            SetChars(d.Slice(Math.Min(1, d.Length), Math.Min(U8(d, 0), Math.Max(0, d.Length - 1))), false, _owner.Enc8);
        }
    }

    // [MS-XLS] §2.4.184 Row: the height, the outline level and the flags of one row; they come before the row's cells.
    private void RowRecord(ReadOnlySpan<byte> d)
    {
        int row = U16(d, 0);
        if (_rows.Count >= 512)
        {
            _rows.Clear();
        }

        if (_version < 3)
        {
            int h = U16(d, 6);
            _rows[row] = new RowInfo((h & 0x8000) == 0 && h > 0 ? h / 20.0 : -1, 0, 0, 0);
            return;
        }

        int height = U16(d, 6) & 0x7FFF;
        int flags = U8(d, 12);
        bool custom = (flags & 0x40) != 0;
        int style = (flags & 0x80) != 0 ? U16(d, 14) & 0xFFF : 0;
        _rows[row] = new RowInfo(custom && height > 0 ? height / 20.0 : -1, ((flags & 0x20) != 0 ? 1 : 0) | ((flags & 0x80) != 0 && style != 0 ? 4 : 0) | ((flags & 0x10) != 0 ? 8 : 0), flags & 7, style);
    }

    /// <summary>Releases the stream.</summary>
    public void Dispose()
    {
        _data?.Dispose();
        _ended = true;
        _inRow = false;
    }
}
