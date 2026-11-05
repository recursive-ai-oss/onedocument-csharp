using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>
/// A forward-only cursor over the rows and cells of one worksheet part (ECMA-376 Part 1 CT_Worksheet, <c>sheetData</c>). Call
/// <see cref="ReadRow"/> to move to the next row and <see cref="ReadCell"/> to move through its cells; the properties describe the
/// current row or cell and stay valid until the cursor moves.
/// </summary>
/// <remarks>
/// <para>
/// Memory is bounded by the largest single token of the sheet part, not by the part, and reading allocates nothing per cell: numbers,
/// booleans, errors, styles and shared-string indexes are plain values, and text is available as a span (<see cref="TextUtf8"/>,
/// <see cref="TryCopyText"/>) or, when the caller asks, as a string (<see cref="GetText"/>). The shared string table is read once, on the first
/// text request, and held as plain text (see <see cref="XlsxStreamReader"/>).
/// </para>
/// <para>
/// Values use the workbook model's <see cref="CellValue"/>. The text of a <see cref="CellValueKind.Text"/> value is not in the handle:
/// read it from the cursor. Rows and cells are delivered in the order the part stores them. A part that stores them out of order is
/// reported (<c>xlsx.stream-row-order</c>, <c>xlsx.stream-cell-order</c>) and delivered as stored; the workbook model sorts and merges
/// them instead. A cursor is not safe for concurrent use.
/// </para>
/// <para>Failures raise <see cref="OfficeFormatException"/> or <see cref="OfficeLimitException"/> located by part and offset; repairs are reported to
/// <see cref="OpenContext.Defects"/> with the cell address.</para>
/// </remarks>
public sealed class SheetCursor : IDisposable
{
    private enum State
    {
        BeforeRows,
        InSheetData,
        InRow,
        Done,
    }

    private readonly XlsxStreamReader _owner;
    private readonly MarkupReader _reader;
    private readonly OpenContext _context;
    private readonly string _partName;
    private readonly byte[] _rootPrefix;
    private readonly CancellationToken _cancellation;
    private State _state;
    private bool _rowHasBody;
    private int _lastRow;
    private int _lastCol;
    private bool _cellOrderReported;

    // The current row.
    private double _rowHeight = -1;
    private int _rowStyle;
    private int _rowOutline;
    private RowBits _rowBits;

    // The current cell.
    private int _col;
    private int _style;
    private CellValueKind _kind;
    private double _number;
    private int _sharedIndex = -1;
    private bool _isoDate;
    private CellError _error;
    private byte[] _value = new byte[256];
    private int _valueLength;
    private byte[] _formula = new byte[256];
    private int _formulaLength;
    private byte[] _formulaRef = new byte[32];
    private int _formulaRefLength;
    private CellFormulaKind _formulaKind;
    private bool _hasFormula;
    private int _sharedFormulaIndex = -1;
    private bool _haveV;
    private int _typeCode;

    [Flags]
    private enum RowBits : byte
    {
        None = 0,
        Hidden = 1,
        CustomHeight = 2,
        CustomFormat = 4,
        Collapsed = 8,
        ThickTop = 16,
        ThickBottom = 32,
    }

    internal SheetCursor(XlsxStreamReader owner, Stream part, OpenContext context, string partName, string sheetName, CancellationToken cancellation)
    {
        _owner = owner;
        _context = context;
        _partName = partName;
        _cancellation = cancellation;
        SheetName = sheetName;
        _reader = new MarkupReader(part, context, partName, leaveOpen: false);
        _rootPrefix = [];
        _rootPrefix = ReadPrologue();
    }

    internal SheetCursor(XlsxStreamReader owner, OpenContext context, string sheetName)
    {
        // A sheet whose part is missing reads as empty.
        _owner = owner;
        _context = context;
        _partName = string.Empty;
        _reader = null!;
        _rootPrefix = [];
        SheetName = sheetName;
        _state = State.Done;
    }

    /// <summary>Gets the sheet's name.</summary>
    public string SheetName { get; }

    /// <summary>Gets the dimension the part declares (<c>dimension</c>), or null when it declares none or an unreadable one. It is a hint, not checked against the cells.</summary>
    public CellRange? DeclaredDimension { get; private set; }

    /// <summary>Gets the column properties declared before the cells (<c>cols</c>), in stored order.</summary>
    public IReadOnlyList<ColumnProperties> Columns { get; private set; } = [];

    // The head of a worksheet comes first in the part; the cursor reads up to the start of sheetData and keeps the two hints it needs.
    private byte[] ReadPrologue()
    {
        byte[] root = [];
        var cols = new List<ColumnProperties>();
        while (_reader.Read())
        {
            if (_reader.Depth == 0 && _reader.Kind is XmlTokenKind.StartElement or XmlTokenKind.EmptyElement)
            {
                if (!_reader.LocalName.SequenceEqual("worksheet"u8))
                {
                    throw new OfficeFormatException(DefectKind.Structure, "xlsx.wrong-root", "The sheet part does not start with a worksheet element.", DefectLocation.InPart(_partName, _reader.TokenOffset));
                }

                root = _reader.Prefix.ToArray();
                if (_reader.Kind == XmlTokenKind.EmptyElement)
                {
                    break;
                }

                continue;
            }

            if (_reader.Depth != 1 || _reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement) || !_reader.Prefix.SequenceEqual(root))
            {
                continue;
            }

            ReadOnlySpan<byte> n = _reader.LocalName;
            if (n.SequenceEqual("sheetData"u8))
            {
                _state = _reader.Kind == XmlTokenKind.EmptyElement ? State.Done : State.InSheetData;
                Columns = cols;
                return root;
            }

            if (n.SequenceEqual("dimension"u8))
            {
                foreach (XmlAttributeToken a in _reader.Attributes)
                {
                    if (a.Prefix.Length == 0 && a.LocalName.SequenceEqual("ref"u8))
                    {
                        string text = XmlText.GetString(a.RawValue, true);
                        if (CellRange.TryParse(text, out CellRange r))
                        {
                            DeclaredDimension = r;
                        }
                    }
                }
            }
            else if (n.SequenceEqual("cols"u8) && _reader.Kind == XmlTokenKind.StartElement)
            {
                ReadCols(cols);
                continue;
            }

            if (_reader.Kind == XmlTokenKind.StartElement)
            {
                _reader.Skip();
            }
        }

        Columns = cols;
        _state = State.Done;
        return root;
    }

    private void ReadCols(List<ColumnProperties> cols)
    {
        while (_reader.Read())
        {
            if (_reader.Kind == XmlTokenKind.EndElement && _reader.Depth == 1)
            {
                return;
            }

            if (_reader.Depth != 2 || _reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement) || !_reader.LocalName.SequenceEqual("col"u8))
            {
                continue;
            }

            int min = 0, max = 0, style = 0, outline = 0;
            double width = -1;
            var flags = ColumnOptions.None;
            foreach (XmlAttributeToken a in _reader.Attributes)
            {
                if (a.Prefix.Length != 0)
                {
                    continue;
                }

                ReadOnlySpan<byte> an = a.LocalName;
                ReadOnlySpan<byte> v = a.RawValue;
                if (an.SequenceEqual("min"u8))
                {
                    _ = StreamXml.TryInt(v, out min);
                }
                else if (an.SequenceEqual("max"u8))
                {
                    _ = StreamXml.TryInt(v, out max);
                }
                else if (an.SequenceEqual("width"u8))
                {
                    if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out width))
                    {
                        width = -1;
                    }
                }
                else if (an.SequenceEqual("style"u8))
                {
                    _ = StreamXml.TryInt(v, out style);
                }
                else if (an.SequenceEqual("outlineLevel"u8))
                {
                    _ = StreamXml.TryInt(v, out outline);
                }
                else if (StreamXml.ParseBool(v))
                {
                    flags |= an.SequenceEqual("hidden"u8) ? ColumnOptions.Hidden
                        : an.SequenceEqual("customWidth"u8) ? ColumnOptions.CustomWidth
                        : an.SequenceEqual("bestFit"u8) ? ColumnOptions.BestFit
                        : an.SequenceEqual("collapsed"u8) ? ColumnOptions.Collapsed
                        : an.SequenceEqual("phonetic"u8) ? ColumnOptions.Phonetic
                        : 0;
                }
            }

            if (min >= 1 && max >= min && max <= CellAddress.MaxColumns && cols.Count < 16384)
            {
                cols.Add(new ColumnProperties(min, max, width, style, flags, Math.Clamp(outline, 0, 7)));
            }
        }
    }

    // ---------------------------------------------------------------- the current row

    /// <summary>Gets the 1-based number of the current row. Meaningful after <see cref="ReadRow"/> returned true.</summary>
    public int RowNumber { get; private set; }

    /// <summary>Gets the row height in points as stored, or -1 when the row has none.</summary>
    public double RowHeight => _rowHeight;

    /// <summary>Gets a value indicating whether the row is hidden.</summary>
    public bool IsRowHidden => (_rowBits & RowBits.Hidden) != 0;

    /// <summary>Gets a value indicating whether the row's height was set by hand (<c>customHeight</c>).</summary>
    public bool HasCustomRowHeight => (_rowBits & RowBits.CustomHeight) != 0;

    /// <summary>Gets a value indicating whether the row's own format applies to its empty cells (<c>customFormat</c>).</summary>
    public bool HasCustomRowFormat => (_rowBits & RowBits.CustomFormat) != 0;

    /// <summary>Gets a value indicating whether the row's outline group is collapsed.</summary>
    public bool IsRowCollapsed => (_rowBits & RowBits.Collapsed) != 0;

    /// <summary>Gets the row's outline level, 0 to 7.</summary>
    public int RowOutlineLevel => _rowOutline;

    /// <summary>Gets the style index of the row (<c>s</c>), 0 when it has none.</summary>
    public int RowStyle => _rowStyle;

    /// <summary>Moves to the next row, skipping the cells of the current row that were not read.</summary>
    /// <returns>False after the last row.</returns>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">The cancellation token given to the reader was cancelled.</exception>
    public bool ReadRow()
    {
        _cancellation.ThrowIfCancellationRequested();
        if (_state == State.InRow)
        {
            if (_rowHasBody)
            {
                SkipRowBody();
            }

            _state = State.InSheetData;
        }

        if (_state != State.InSheetData)
        {
            return false;
        }

        while (_reader.Read())
        {
            if (_reader.Kind == XmlTokenKind.EndElement && _reader.Depth == 1)
            {
                _state = State.Done;
                return false;
            }

            if (_reader.Depth != 2 || _reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement))
            {
                continue;
            }

            if (!_reader.LocalName.SequenceEqual("row"u8) || !_reader.Prefix.SequenceEqual(_rootPrefix))
            {
                _context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.sheetdata-unexpected-element", DefectSeverity.Dropped,
                    "An element other than a row inside sheetData was dropped.", DefectLocation.InPart(_partName, _reader.TokenOffset)));
                _reader.Skip();
                continue;
            }

            StartRow();
            return true;
        }

        _state = State.Done;
        return false;
    }

    private void SkipRowBody()
    {
        while (_reader.Read())
        {
            if (_reader.Kind == XmlTokenKind.EndElement && _reader.Depth == 2)
            {
                return;
            }
        }
    }

    private void StartRow()
    {
        _rowHasBody = _reader.Kind == XmlTokenKind.StartElement;
        long offset = _reader.TokenOffset;
        int row = 0;
        _rowHeight = -1;
        _rowStyle = 0;
        _rowOutline = 0;
        _rowBits = RowBits.None;
        foreach (XmlAttributeToken a in _reader.Attributes)
        {
            if (a.Prefix.Length != 0)
            {
                continue;
            }

            ReadOnlySpan<byte> n = a.LocalName;
            ReadOnlySpan<byte> v = a.RawValue;
            if (n.Length == 1 && n[0] == (byte)'r')
            {
                if (!StreamXml.TryInt(v, out row) || row is < 1 or > CellAddress.MaxRows)
                {
                    _context.Defects.Report(new Defect(DefectKind.Reference, "xlsx.row-index-invalid", DefectSeverity.Repaired,
                        "A row has an invalid r attribute; it takes the number after the previous row.", DefectLocation.InPart(_partName, offset)));
                    row = 0;
                }
            }
            else if (n.Length == 1 && n[0] == (byte)'s')
            {
                _ = StreamXml.TryInt(v, out _rowStyle);
            }
            else if (n.SequenceEqual("ht"u8))
            {
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && h >= 0)
                {
                    _rowHeight = h;
                }
            }
            else if (n.SequenceEqual("outlineLevel"u8))
            {
                _ = StreamXml.TryInt(v, out _rowOutline);
                _rowOutline = Math.Clamp(_rowOutline, 0, 7);
            }
            else if (StreamXml.ParseBool(v))
            {
                _rowBits |= n.SequenceEqual("hidden"u8) ? RowBits.Hidden
                    : n.SequenceEqual("customHeight"u8) ? RowBits.CustomHeight
                    : n.SequenceEqual("customFormat"u8) ? RowBits.CustomFormat
                    : n.SequenceEqual("collapsed"u8) ? RowBits.Collapsed
                    : n.SequenceEqual("thickTop"u8) ? RowBits.ThickTop
                    : n.SequenceEqual("thickBot"u8) ? RowBits.ThickBottom
                    : 0;
            }
        }

        if (row == 0)
        {
            row = _lastRow + 1;
        }

        if (row > CellAddress.MaxRows)
        {
            throw new OfficeFormatException(DefectKind.Reference, "xlsx.row-index-invalid", "A row lies beyond the last row of a sheet.", DefectLocation.InPart(_partName, offset));
        }

        if (row <= _lastRow)
        {
            _context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.stream-row-order", DefectSeverity.Repaired,
                "Rows are not in ascending order; the streaming reader delivers them as stored.", DefectLocation.InPart(_partName, offset)));
        }

        RowNumber = row;
        _lastRow = row;
        _lastCol = 0;
        _cellOrderReported = false;
        _state = State.InRow;
        ClearCell();
    }

    // ---------------------------------------------------------------- the current cell

    /// <summary>Gets the 1-based column of the current cell.</summary>
    public int Column => _col;

    /// <summary>Gets the address of the current cell.</summary>
    public CellAddress Address => new(RowNumber, _col);

    /// <summary>Gets the style index of the current cell (<c>s</c>), 0 when it has none.</summary>
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
        CellValueKind.Error => new CellValue(CellValueKind.Error, 0, _error == CellError.Unknown ? -1 : (int)_error),
        CellValueKind.Text => new CellValue(CellValueKind.Text, 0, _sharedIndex),
        _ => default,
    };

    /// <summary>Gets the number of a <see cref="CellValueKind.Number"/> cell, 1 or 0 for a boolean, 0 otherwise.</summary>
    public double Number => _kind is CellValueKind.Number or CellValueKind.Boolean ? _number : 0;

    /// <summary>Gets a value indicating whether the cell holds TRUE.</summary>
    public bool Boolean => _kind == CellValueKind.Boolean && _number != 0;

    /// <summary>Gets the error of an error cell; <see cref="CellError.Unknown"/> for an error text the table does not know (read it with <see cref="GetText"/>).</summary>
    public CellError Error => _kind == CellValueKind.Error ? _error : default;

    /// <summary>Gets the index into the shared string table when the cell's text is a shared string, otherwise -1.</summary>
    public int SharedStringIndex => _kind == CellValueKind.Text ? _sharedIndex : -1;

    /// <summary>Gets a value indicating whether the text is an ISO 8601 date the part stores as text (<c>t="d"</c>).</summary>
    public bool IsIsoDateText => _isoDate;

    /// <summary>
    /// Gets the text of a text or error cell as UTF-8, without allocating: XML references are decoded, runs are joined and phonetic runs left
    /// out, and <c>_xHHHH_</c> escapes are still in place (use <see cref="TryCopyText"/> or <see cref="GetText"/> to resolve them). Empty for other kinds.
    /// Valid until the cursor moves.
    /// </summary>
    public ReadOnlySpan<byte> TextUtf8
    {
        get
        {
            if (_kind == CellValueKind.Text && _sharedIndex >= 0)
            {
                return _owner.GetSharedStrings().GetUtf8(_sharedIndex);
            }

            return _kind is CellValueKind.Text or CellValueKind.Error ? _value.AsSpan(0, _valueLength) : default;
        }
    }

    /// <summary>Copies the text of a text or error cell into a buffer, escapes resolved, without allocating.</summary>
    /// <param name="destination">The buffer.</param>
    /// <param name="written">The number of chars written, or the number the buffer would need when it is too small (an upper bound for text with escapes).</param>
    /// <returns>True when the text fits.</returns>
    public bool TryCopyText(Span<char> destination, out int written)
    {
        ReadOnlySpan<byte> utf8 = TextUtf8;
        int needed = Encoding.UTF8.GetCharCount(utf8);
        if (needed > destination.Length)
        {
            written = needed;
            return false;
        }

        int n = Encoding.UTF8.GetChars(utf8, destination);
        written = StreamXml.DecodeEscapes(destination[..n]);
        return true;
    }

    /// <summary>Gets the text of a text or error cell as a string (allocates).</summary>
    /// <returns>The text with escapes resolved; empty for other kinds. For an error cell, the error as written.</returns>
    public string GetText() => StreamXml.ToString(TextUtf8);

    /// <summary>Gets a value indicating whether the cell has a formula.</summary>
    public bool HasFormula => _hasFormula;

    /// <summary>Gets the kind of the cell's formula (ordinary, shared, array, data table).</summary>
    public CellFormulaKind FormulaKind => _formulaKind;

    /// <summary>Gets the formula text as UTF-8, XML references decoded, without the leading <c>=</c>. Empty for a shared formula that only refers to its anchor. Valid until the cursor moves.</summary>
    public ReadOnlySpan<byte> FormulaUtf8 => _hasFormula ? _formula.AsSpan(0, _formulaLength) : default;

    /// <summary>Gets the formula text as a string (allocates).</summary>
    /// <returns>The formula, or an empty string.</returns>
    public string GetFormula() => _hasFormula && _formulaLength > 0 ? Encoding.UTF8.GetString(_formula, 0, _formulaLength) : string.Empty;

    /// <summary>Gets the range a shared or array formula covers (<c>ref</c>) as UTF-8, empty when the formula has none.</summary>
    public ReadOnlySpan<byte> FormulaRangeUtf8 => _hasFormula ? _formulaRef.AsSpan(0, _formulaRefLength) : default;

    /// <summary>Gets the shared formula group index (<c>si</c>), or -1 when the formula is not shared.</summary>
    public int SharedFormulaIndex => _hasFormula && _formulaKind == CellFormulaKind.Shared ? _sharedFormulaIndex : -1;

    /// <summary>
    /// Gets a value indicating whether the cell is a number whose format shows a date or time, found through the number format of its
    /// style (<see cref="XlsxStreamReader.IsDateStyle"/>). The style sheet is read once, on the first use.
    /// </summary>
    public bool IsDate => _kind == CellValueKind.Number && _owner.IsDateStyle(_style);

    /// <summary>Reads the cell as a date and time, by the workbook's date system (the 1900 system's leap-year quirk included).</summary>
    /// <param name="value">The date and time when the result is true.</param>
    /// <returns>True when the cell is a date-formatted number inside the range of dates (or an ISO 8601 text date that parses).</returns>
    public bool TryGetDateTime(out DateTime value)
    {
        if (_kind == CellValueKind.Number && _owner.IsDateStyle(_style))
        {
            return ExcelDate.TryToDateTime(_number, _owner.DateSystem, out value);
        }

        if (_isoDate)
        {
            return DateTime.TryParse(Encoding.UTF8.GetString(_value, 0, _valueLength), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
        }

        value = default;
        return false;
    }

    private void ClearCell()
    {
        _col = 0;
        _style = 0;
        _kind = CellValueKind.Empty;
        _number = 0;
        _sharedIndex = -1;
        _isoDate = false;
        _error = default;
        _valueLength = 0;
        _formulaLength = 0;
        _formulaRefLength = 0;
        _hasFormula = false;
        _formulaKind = CellFormulaKind.Normal;
        _sharedFormulaIndex = -1;
    }

    /// <summary>Moves to the next cell of the current row.</summary>
    /// <returns>False after the last cell of the row.</returns>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public bool ReadCell()
    {
        if (_state != State.InRow || !_rowHasBody)
        {
            return false;
        }

        while (_reader.Read())
        {
            if (_reader.Kind == XmlTokenKind.EndElement && _reader.Depth == 2)
            {
                _rowHasBody = false;
                return false;
            }

            if (_reader.Depth != 3 || _reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement))
            {
                continue;
            }

            if (_reader.LocalName.SequenceEqual("c"u8) && _reader.Prefix.SequenceEqual(_rootPrefix))
            {
                if (ParseCell())
                {
                    return true;
                }

                continue;
            }

            _reader.Skip();
        }

        _rowHasBody = false;
        return false;
    }

    private bool ParseCell()
    {
        bool empty = _reader.Kind == XmlTokenKind.EmptyElement;
        long offset = _reader.TokenOffset;
        ClearCell();
        int col = 0;
        _typeCode = 0;
        bool badAddress = false;
        foreach (XmlAttributeToken a in _reader.Attributes)
        {
            if (a.Prefix.Length != 0 || a.LocalName.Length != 1)
            {
                continue;
            }

            byte n = a.LocalName[0];
            if (n == (byte)'r')
            {
                if (CellAddress.TryParse(a.RawValue, out CellAddress addr))
                {
                    col = addr.Column;
                    if (addr.Row != RowNumber)
                    {
                        _context.Defects.Report(new Defect(DefectKind.Reference, "xlsx.cell-row-mismatch", DefectSeverity.Repaired,
                            "A cell's address names a different row than its row element; it stays in the row element's row.", DefectLocation.InCell(SheetName, addr.ToString())));
                    }
                }
                else
                {
                    badAddress = true;
                }
            }
            else if (n == (byte)'s')
            {
                _ = StreamXml.TryInt(a.RawValue, out _style);
            }
            else if (n == (byte)'t')
            {
                _typeCode = TypeCode(a.RawValue);
            }
        }

        if (badAddress || (col == 0 && _lastCol + 1 > CellAddress.MaxColumns) || col > CellAddress.MaxColumns)
        {
            _context.Defects.Report(new Defect(DefectKind.Reference, "xlsx.cell-address-invalid", DefectSeverity.Dropped,
                "A cell has an address outside the sheet or not in A1 form; the cell was dropped.", DefectLocation.InPart(_partName, offset)));
            if (!empty)
            {
                _reader.Skip();
            }

            ClearCell();
            return false;
        }

        if (col == 0)
        {
            col = _lastCol + 1;
        }

        if (col <= _lastCol && !_cellOrderReported)
        {
            _cellOrderReported = true;
            _context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.stream-cell-order", DefectSeverity.Repaired,
                "Cells of a row are not in ascending column order; the streaming reader delivers them as stored.", DefectLocation.InCell(SheetName, new CellAddress(RowNumber, col).ToString())));
        }

        _lastCol = col;
        _col = col;
        _haveV = false;
        bool haveInline = false;
        if (!empty)
        {
            while (_reader.Read())
            {
                if (_reader.Kind == XmlTokenKind.EndElement && _reader.Depth == 3)
                {
                    break;
                }

                if (_reader.Depth != 4 || _reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement))
                {
                    continue;
                }

                bool main = _reader.Prefix.SequenceEqual(_rootPrefix);
                ReadOnlySpan<byte> ln = _reader.LocalName;
                if (main && ln.SequenceEqual("v"u8))
                {
                    _haveV = true;
                    if (_reader.Kind == XmlTokenKind.StartElement)
                    {
                        _valueLength = 0;
                        StreamXml.AppendText(_reader, ref _value, ref _valueLength);
                    }

                    continue;
                }

                if (main && ln.SequenceEqual("f"u8))
                {
                    ParseFormula();
                    continue;
                }

                if (main && ln.SequenceEqual("is"u8))
                {
                    haveInline = true;
                    if (_reader.Kind == XmlTokenKind.StartElement)
                    {
                        ParseInline();
                    }

                    continue;
                }

                if (_reader.Kind == XmlTokenKind.StartElement)
                {
                    _reader.Skip();
                }
            }
        }

        DecodeValue(haveInline);
        return true;
    }

    // ECMA-376 Part 1 CT_Rst: the text of an inline string is every t inside is or inside its runs; rPh is phonetic data.
    private void ParseInline()
    {
        _valueLength = 0;
        while (_reader.Read())
        {
            if (_reader.Kind == XmlTokenKind.EndElement && _reader.Depth == 4)
            {
                return;
            }

            if (_reader.Kind != XmlTokenKind.StartElement)
            {
                continue;
            }

            ReadOnlySpan<byte> n = _reader.LocalName;
            if (n.SequenceEqual("rPh"u8))
            {
                _reader.Skip();
            }
            else if (n.SequenceEqual("t"u8) && _reader.Depth is 5 or 6)
            {
                StreamXml.AppendText(_reader, ref _value, ref _valueLength);
            }
        }
    }

    private void ParseFormula()
    {
        _hasFormula = true;
        _formulaKind = CellFormulaKind.Normal;
        _formulaRefLength = 0;
        _sharedFormulaIndex = -1;
        _formulaLength = 0;
        foreach (XmlAttributeToken a in _reader.Attributes)
        {
            if (a.Prefix.Length != 0)
            {
                continue;
            }

            ReadOnlySpan<byte> n = a.LocalName;
            ReadOnlySpan<byte> v = a.RawValue;
            if (n.SequenceEqual("t"u8))
            {
                if (v.SequenceEqual("shared"u8))
                {
                    _formulaKind = CellFormulaKind.Shared;
                }
                else if (v.SequenceEqual("array"u8))
                {
                    _formulaKind = CellFormulaKind.Array;
                }
                else if (v.SequenceEqual("dataTable"u8))
                {
                    _formulaKind = CellFormulaKind.DataTable;
                }
                else if (!v.SequenceEqual("normal"u8))
                {
                    _context.Defects.Report(new Defect(DefectKind.Formula, "xlsx.formula-type-unknown", DefectSeverity.Repaired,
                        "A formula has an unknown t attribute; it is read as an ordinary formula.", DefectLocation.InCell(SheetName, new CellAddress(RowNumber, _col).ToString())));
                }
            }
            else if (n.SequenceEqual("ref"u8))
            {
                if (v.Length > _formulaRef.Length)
                {
                    _formulaRef = new byte[v.Length * 2];
                }

                _formulaRefLength = XmlText.NeedsDecoding(v, true) ? XmlText.Decode(v, _formulaRef, true) : CopyTo(v, _formulaRef);
            }
            else if (n.SequenceEqual("si"u8))
            {
                _ = StreamXml.TryInt(v, out _sharedFormulaIndex);
            }
        }

        if (_reader.Kind == XmlTokenKind.StartElement)
        {
            StreamXml.AppendText(_reader, ref _formula, ref _formulaLength);
            if (_formulaLength > _context.Limits.MaxFormulaLength)
            {
                // The location is built only on this path: building it per cell would allocate.
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxFormulaLength), _context.Limits.MaxFormulaLength, _formulaLength,
                    DefectLocation.InCell(SheetName, new CellAddress(RowNumber, _col).ToString()));
            }
        }
    }

    private static int CopyTo(ReadOnlySpan<byte> v, byte[] dest)
    {
        v.CopyTo(dest);
        return v.Length;
    }

    // ECMA-376 Part 1 ST_CellType: how the v text is read.
    private static int TypeCode(ReadOnlySpan<byte> t) =>
        t.SequenceEqual("n"u8) ? 0 : t.SequenceEqual("s"u8) ? 1 : t.SequenceEqual("b"u8) ? 2 : t.SequenceEqual("e"u8) ? 3
        : t.SequenceEqual("str"u8) ? 4 : t.SequenceEqual("inlineStr"u8) ? 5 : t.SequenceEqual("d"u8) ? 6 : 7;

    private void DecodeValue(bool haveInline)
    {
        ReadOnlySpan<byte> v = _value.AsSpan(0, _valueLength);
        bool valid = true;
        switch (_typeCode)
        {
            case 0:
                if (_haveV && !v.IsEmpty)
                {
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    {
                        _kind = CellValueKind.Number;
                        _number = d;
                    }
                    else
                    {
                        valid = false;
                    }
                }

                break;
            case 1:
                if (_haveV)
                {
                    if (StreamXml.TryInt(v, out int idx) && idx >= 0)
                    {
                        _kind = CellValueKind.Text;
                        _sharedIndex = idx;
                    }
                    else
                    {
                        valid = false;
                    }
                }

                break;
            case 2:
                if (_haveV)
                {
                    _kind = CellValueKind.Boolean;
                    _number = StreamXml.ParseBool(v) ? 1 : 0;
                }

                break;
            case 3:
                if (_haveV)
                {
                    _kind = CellValueKind.Error;
                    _error = CellErrors.TryParse(v, out CellError err) ? err : CellError.Unknown;
                }

                break;
            case 4:
                if (_haveV)
                {
                    _kind = CellValueKind.Text;
                }

                break;
            case 5:
                if (haveInline || _haveV)
                {
                    _kind = CellValueKind.Text;
                }

                break;
            case 6:
                if (_haveV)
                {
                    _kind = CellValueKind.Text;
                    _isoDate = true;
                    int start = 0;
                    int end = _valueLength;
                    while (start < end && _value[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                    {
                        start++;
                    }

                    while (end > start && _value[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                    {
                        end--;
                    }

                    if (start > 0)
                    {
                        Buffer.BlockCopy(_value, start, _value, 0, end - start);
                    }

                    _valueLength = end - start;
                }

                break;
            default:
                _context.Defects.Report(new Defect(DefectKind.Value, "xlsx.cell-type-unknown", DefectSeverity.Repaired,
                    "A cell has an unknown t attribute; its value is read as a number if it parses, otherwise text.", DefectLocation.InCell(SheetName, new CellAddress(RowNumber, _col).ToString())));
                if (_haveV && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n2))
                {
                    _kind = CellValueKind.Number;
                    _number = n2;
                }
                else if (_haveV)
                {
                    _kind = CellValueKind.Text;
                }

                break;
        }

        if (!valid)
        {
            _context.Defects.Report(new Defect(DefectKind.Value, "xlsx.cell-value-invalid", DefectSeverity.Repaired,
                "A cell's value does not match its type; the cell is read as empty.", DefectLocation.InCell(SheetName, new CellAddress(RowNumber, _col).ToString())));
        }
    }

    /// <summary>Releases the part stream.</summary>
    public void Dispose()
    {
        _reader?.Dispose();
        _state = State.Done;
    }
}
