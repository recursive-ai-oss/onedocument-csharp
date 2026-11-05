using System.Globalization;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>
/// Writes the rows of one sheet of an <see cref="XlsxStreamWriter"/>. Rows go in ascending order and the cells of a row in ascending
/// columns; the write methods take the next column unless <see cref="MoveToColumn"/> or <see cref="SkipColumns"/> moves it. A row
/// starts with <see cref="BeginRow"/> (or implicitly with the first cell after the previous row's end) and ends when the next row
/// begins or the sheet is completed. Writing a cell allocates nothing.
/// </summary>
/// <remarks>
/// A style index is what <see cref="XlsxStreamWriter.AddFormat"/> returned (0 is the default format). Text that XML cannot carry is
/// written with Excel's <c>_xHHHH_</c> escapes (ECMA-376 Part 1 ST_Xstring). A number that is NaN or infinite, which a cell cannot hold,
/// is written as the error <c>#NUM!</c>.
/// </remarks>
public sealed class SheetStreamWriter
{
    private readonly XlsxStreamWriter _owner;
    private readonly Stream _entry;
    private readonly Utf8Buffer _w;
    private readonly List<CellRange> _merges = [];
    private bool _rowOpen;
    private bool _completed;
    private int _row;
    private int _nextColumn = 1;
    private byte[] _rowDigits = new byte[8];
    private int _rowDigitCount;

    internal SheetStreamWriter(XlsxStreamWriter owner, Stream entry, SheetWriteOptions options)
    {
        _owner = owner;
        _entry = entry;
        _w = new Utf8Buffer(entry);
        WriteHead(options);
    }

    /// <summary>Gets the number of the current row (the last one begun), 0 before the first.</summary>
    public int RowNumber => _row;

    /// <summary>Gets the 1-based column the next cell goes to.</summary>
    public int NextColumn => _nextColumn;

    // ECMA-376 Part 1 CT_Worksheet child order: sheetViews, sheetFormatPr, cols, sheetData, mergeCells, pageMargins.
    private void WriteHead(SheetWriteOptions o)
    {
        _w.Ascii("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"" + XlsxStreamWriter.MainNamespace + "\"><sheetViews><sheetView workbookViewId=\"0\"");
        if (!o.ShowGridLines)
        {
            _w.Ascii(" showGridLines=\"0\"");
        }

        if (_owner.SheetCountIsOne)
        {
            _w.Ascii(" tabSelected=\"1\"");
        }

        int fr = Math.Clamp(o.FreezeRows, 0, CellAddress.MaxRows - 1);
        int fc = Math.Clamp(o.FreezeColumns, 0, CellAddress.MaxColumns - 1);
        if (fr > 0 || fc > 0)
        {
            string top = new CellAddress(fr + 1, fc + 1).ToString();
            string active = fr > 0 && fc > 0 ? "bottomRight" : fr > 0 ? "bottomLeft" : "topRight";
            _w.Ascii(">");
            _w.Ascii("<pane" + (fc > 0 ? " xSplit=\"" + fc.ToString(CultureInfo.InvariantCulture) + "\"" : string.Empty) + (fr > 0 ? " ySplit=\"" + fr.ToString(CultureInfo.InvariantCulture) + "\"" : string.Empty)
                + " topLeftCell=\"" + top + "\" activePane=\"" + active + "\" state=\"frozen\"/><selection pane=\"" + active + "\"/></sheetView></sheetViews>");
        }
        else
        {
            _w.Ascii("/></sheetViews>");
        }

        _w.Ascii("<sheetFormatPr defaultRowHeight=\"15\"/>");
        if (o.Columns is { Count: > 0 } cols)
        {
            _w.Ascii("<cols>");
            foreach (ColumnProperties c in cols)
            {
                if (c.Min < 1 || c.Max < c.Min || c.Max > CellAddress.MaxColumns)
                {
                    throw new ArgumentException("A column range is outside the sheet.", nameof(o));
                }

                _w.Ascii("<col min=\"" + c.Min.ToString(CultureInfo.InvariantCulture) + "\" max=\"" + c.Max.ToString(CultureInfo.InvariantCulture) + "\" width=\""
                    + (c.Width >= 0 ? c.Width : 9.140625).ToString("R", CultureInfo.InvariantCulture) + "\"");
                if (c.Style != 0)
                {
                    _w.Ascii(" style=\"" + c.Style.ToString(CultureInfo.InvariantCulture) + "\"");
                }

                if ((c.Flags & ColumnOptions.Hidden) != 0)
                {
                    _w.Ascii(" hidden=\"1\"");
                }

                if (c.Width >= 0)
                {
                    _w.Ascii(" customWidth=\"1\"");
                }

                if (c.OutlineLevel > 0)
                {
                    _w.Ascii(" outlineLevel=\"" + c.OutlineLevel.ToString(CultureInfo.InvariantCulture) + "\"");
                }

                _w.Ascii("/>");
            }

            _w.Ascii("</cols>");
        }

        _w.Ascii("<sheetData>");
    }

    /// <summary>Starts a row, ending the previous one.</summary>
    /// <param name="rowNumber">The 1-based row number; 0 (the default) takes the one after the last row. Must be above the last row's number.</param>
    /// <param name="height">The row height in points; negative (the default) leaves it automatic.</param>
    /// <param name="hidden">True to hide the row.</param>
    /// <param name="outlineLevel">The outline level, 0 to 7.</param>
    /// <exception cref="ArgumentOutOfRangeException">The number is not above the last row's, or is past the last row of a sheet.</exception>
    /// <exception cref="InvalidOperationException">The sheet was completed.</exception>
    /// <exception cref="OperationCanceledException">The cancellation token given to the writer was cancelled.</exception>
    public void BeginRow(int rowNumber = 0, double height = -1, bool hidden = false, int outlineLevel = 0)
    {
        ThrowIfCompleted();
        _owner.Cancellation.ThrowIfCancellationRequested();
        EndRow();
        int row = rowNumber == 0 ? _row + 1 : rowNumber;
        if (row <= _row || row > CellAddress.MaxRows)
        {
            throw new ArgumentOutOfRangeException(nameof(rowNumber), "Rows are written in ascending order, up to " + CellAddress.MaxRows + ".");
        }

        _row = row;
        _nextColumn = 1;
        _rowOpen = true;
        Span<byte> d = _w.Reserve(64);
        "<row r=\""u8.CopyTo(d);
        int n = 8;
        row.TryFormat(d[n..], out int digits, default, CultureInfo.InvariantCulture);
        _rowDigitCount = digits;
        d.Slice(n, digits).CopyTo(_rowDigits);
        n += digits;
        d[n++] = (byte)'"';
        _w.Advance(n);
        if (height >= 0)
        {
            _w.Ascii(" ht=\"" + height.ToString("R", CultureInfo.InvariantCulture) + "\" customHeight=\"1\"");
        }

        if (hidden)
        {
            _w.Ascii(" hidden=\"1\"");
        }

        if (outlineLevel > 0)
        {
            _w.Ascii(" outlineLevel=\"" + Math.Min(outlineLevel, 7).ToString(CultureInfo.InvariantCulture) + "\"");
        }

        _w.Ascii(">");
    }

    private void EndRow()
    {
        if (_rowOpen)
        {
            _w.Raw("</row>"u8);
            _rowOpen = false;
        }
    }

    /// <summary>Moves the next cell to a column.</summary>
    /// <param name="column">The 1-based column; at least the next one.</param>
    /// <exception cref="ArgumentOutOfRangeException">The column is before the next one or past the last column.</exception>
    public void MoveToColumn(int column)
    {
        if (column < _nextColumn || column > CellAddress.MaxColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Cells are written in ascending columns, up to " + CellAddress.MaxColumns + ".");
        }

        _nextColumn = column;
    }

    /// <summary>Leaves columns empty.</summary>
    /// <param name="count">How many columns to pass over.</param>
    public void SkipColumns(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        MoveToColumn(_nextColumn + count);
    }

    // Starts a cell: <c r="A1" s="n"; the caller adds the type attribute, then ">" or "/>".
    private void OpenCell(int style)
    {
        if (!_rowOpen)
        {
            BeginRow();
        }

        if (_nextColumn > CellAddress.MaxColumns)
        {
            throw new InvalidOperationException("The row has no column left.");
        }

        Span<byte> d = _w.Reserve(48);
        "<c r=\""u8.CopyTo(d);
        int n = 6;
        int col = _nextColumn++;
        Span<byte> letters = stackalloc byte[3]; // at most XFD
        int k = col > 702 ? 3 : col > 26 ? 2 : 1;
        int c = col;
        for (int i = k - 1; i >= 0; i--)
        {
            c--;
            letters[i] = (byte)('A' + (c % 26));
            c /= 26;
        }

        letters[..k].CopyTo(d[n..]);
        n += k;
        _rowDigits.AsSpan(0, _rowDigitCount).CopyTo(d[n..]);
        n += _rowDigitCount;
        d[n++] = (byte)'"';
        if (style > 0)
        {
            " s=\""u8.CopyTo(d[n..]);
            n += 4;
            style.TryFormat(d[n..], out int sd, default, CultureInfo.InvariantCulture);
            n += sd;
            d[n++] = (byte)'"';
        }

        _w.Advance(n);
    }

    /// <summary>Writes a number. NaN and infinities, which a cell cannot hold, are written as <c>#NUM!</c>.</summary>
    /// <param name="value">The number.</param>
    /// <param name="style">The style index.</param>
    public void WriteNumber(double value, int style = 0)
    {
        if (!double.IsFinite(value))
        {
            WriteError(CellError.Number, style);
            return;
        }

        OpenCell(style);
        _w.Raw("><v>"u8);
        _w.Double(value);
        _w.Raw("</v></c>"u8);
    }

    /// <summary>Writes a date and time as the serial number of the workbook's date system, with a style that should carry a date format (<see cref="StreamCellFormat.Date"/>).</summary>
    /// <param name="value">The date and time.</param>
    /// <param name="style">The style index.</param>
    /// <exception cref="ArgumentOutOfRangeException">The date is outside the range spreadsheets hold.</exception>
    public void WriteDate(DateTime value, int style)
    {
        if (!ExcelDate.TryToSerial(value, _owner.DateSystem, out double serial))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The date is outside the range of the workbook's date system.");
        }

        WriteNumber(serial, style);
    }

    /// <summary>Writes text.</summary>
    /// <param name="text">The text; any characters (those XML cannot carry are escaped). Long text is cut at Excel's limit of 32,767 characters.</param>
    /// <param name="style">The style index.</param>
    public void WriteText(ReadOnlySpan<char> text, int style = 0)
    {
        if (text.Length > 32767)
        {
            text = text[..32767];
        }

        int shared = text.Length == 0 ? -1 : _owner.SharedIndex(text);
        OpenCell(style);
        if (shared >= 0)
        {
            _w.Raw(" t=\"s\"><v>"u8);
            _w.Int(shared);
            _w.Raw("</v></c>"u8);
            return;
        }

        _w.Raw(" t=\"inlineStr\"><is><t"u8);
        if (XlsxStreamWriter.NeedsPreserve(text))
        {
            _w.Raw(" xml:space=\"preserve\""u8);
        }

        _w.Raw(">"u8);
        _w.Text(text);
        _w.Raw("</t></is></c>"u8);
    }

    /// <summary>Writes TRUE or FALSE.</summary>
    /// <param name="value">The value.</param>
    /// <param name="style">The style index.</param>
    public void WriteBoolean(bool value, int style = 0)
    {
        OpenCell(style);
        _w.Raw(value ? " t=\"b\"><v>1</v></c>"u8 : " t=\"b\"><v>0</v></c>"u8);
    }

    /// <summary>Writes an error value.</summary>
    /// <param name="error">The error.</param>
    /// <param name="style">The style index.</param>
    public void WriteError(CellError error, int style = 0)
    {
        OpenCell(style);
        _w.Raw(" t=\"e\"><v>"u8);
        _w.Ascii(CellErrors.ToText(error));
        _w.Raw("</v></c>"u8);
    }

    /// <summary>Writes a cell that holds only a style.</summary>
    /// <param name="style">The style index.</param>
    public void WriteBlank(int style)
    {
        OpenCell(style);
        _w.Raw("/>"u8);
    }

    /// <summary>Writes a value of the workbook model (numbers, booleans and errors; a text value cannot be written this way because its handle belongs to a workbook).</summary>
    /// <param name="value">The value; empty writes nothing but moves past the column.</param>
    /// <param name="style">The style index.</param>
    /// <exception cref="ArgumentException">The value is text.</exception>
    public void WriteValue(CellValue value, int style = 0)
    {
        switch (value.Kind)
        {
            case CellValueKind.Number:
                WriteNumber(value.Number, style);
                break;
            case CellValueKind.Boolean:
                WriteBoolean(value.Boolean, style);
                break;
            case CellValueKind.Error:
                WriteError(value.Error, style);
                break;
            case CellValueKind.Text:
                throw new ArgumentException("Write text with WriteText.", nameof(value));
            default:
                if (style > 0)
                {
                    WriteBlank(style);
                }
                else
                {
                    SkipColumns(1);
                }

                break;
        }
    }

    /// <summary>Writes a formula with no cached value. The workbook then asks the application to calculate on opening.</summary>
    /// <param name="formula">The formula text without the leading <c>=</c>, in the file's notation (A1 references, comma separators, English names).</param>
    /// <param name="style">The style index.</param>
    /// <exception cref="ArgumentException">The formula has a character XML cannot carry, or is longer than 8,192 characters.</exception>
    public void WriteFormula(ReadOnlySpan<char> formula, int style = 0)
    {
        _owner.NoteFormulaWithoutValue();
        OpenCell(style);
        _w.Raw("><f>"u8);
        WriteFormulaText(formula);
        _w.Raw("</f></c>"u8);
    }

    /// <summary>Writes a formula with a cached number.</summary>
    /// <param name="formula">The formula text without the leading <c>=</c>.</param>
    /// <param name="cached">The result as calculated.</param>
    /// <param name="style">The style index.</param>
    public void WriteFormula(ReadOnlySpan<char> formula, double cached, int style = 0)
    {
        if (!double.IsFinite(cached))
        {
            WriteFormulaError(formula, CellError.Number, style);
            return;
        }

        OpenCell(style);
        _w.Raw("><f>"u8);
        WriteFormulaText(formula);
        _w.Raw("</f><v>"u8);
        _w.Double(cached);
        _w.Raw("</v></c>"u8);
    }

    /// <summary>Writes a formula with a cached boolean.</summary>
    /// <param name="formula">The formula text without the leading <c>=</c>.</param>
    /// <param name="cached">The result as calculated.</param>
    /// <param name="style">The style index.</param>
    public void WriteFormula(ReadOnlySpan<char> formula, bool cached, int style = 0)
    {
        OpenCell(style);
        _w.Raw(" t=\"b\"><f>"u8);
        WriteFormulaText(formula);
        _w.Raw(cached ? "</f><v>1</v></c>"u8 : "</f><v>0</v></c>"u8);
    }

    /// <summary>Writes a formula with cached text.</summary>
    /// <param name="formula">The formula text without the leading <c>=</c>.</param>
    /// <param name="cachedText">The result as calculated.</param>
    /// <param name="style">The style index.</param>
    public void WriteFormulaText(ReadOnlySpan<char> formula, ReadOnlySpan<char> cachedText, int style = 0)
    {
        OpenCell(style);
        _w.Raw(" t=\"str\"><f>"u8);
        WriteFormulaText(formula);
        _w.Raw("</f><v>"u8);
        _w.Text(cachedText);
        _w.Raw("</v></c>"u8);
    }

    /// <summary>Writes a formula with a cached error.</summary>
    /// <param name="formula">The formula text without the leading <c>=</c>.</param>
    /// <param name="cached">The error as calculated.</param>
    /// <param name="style">The style index.</param>
    public void WriteFormulaError(ReadOnlySpan<char> formula, CellError cached, int style = 0)
    {
        OpenCell(style);
        _w.Raw(" t=\"e\"><f>"u8);
        WriteFormulaText(formula);
        _w.Raw("</f><v>"u8);
        _w.Ascii(CellErrors.ToText(cached));
        _w.Raw("</v></c>"u8);
    }

    private void WriteFormulaText(ReadOnlySpan<char> formula)
    {
        if (formula.Length > 8192)
        {
            throw new ArgumentException("A formula is at most 8,192 characters.", nameof(formula));
        }

        if (formula.Length > 0 && formula[0] == '=')
        {
            formula = formula[1..];
        }

        _w.Plain(formula);
    }

    /// <summary>Merges a range of cells. Ranges are collected and written after the rows, so this can be called at any time before the sheet is completed. Overlaps are not checked.</summary>
    /// <param name="range">The range; more than one cell.</param>
    /// <exception cref="ArgumentException">The range is a single cell.</exception>
    public void Merge(CellRange range)
    {
        if (range.TopLeft == range.BottomRight)
        {
            throw new ArgumentException("A merged range has more than one cell.", nameof(range));
        }

        _merges.Add(range);
    }

    private void ThrowIfCompleted() => ObjectDisposedException.ThrowIf(_completed, this);

    /// <summary>Ends the sheet: closes the rows, writes the merged ranges and closes the part. Adding the next sheet or finishing the workbook does this too.</summary>
    public void Complete()
    {
        if (_completed)
        {
            return;
        }

        EndRow();
        _w.Raw("</sheetData>"u8);
        if (_merges.Count > 0)
        {
            _w.Ascii("<mergeCells count=\"" + _merges.Count.ToString(CultureInfo.InvariantCulture) + "\">");
            foreach (CellRange r in _merges)
            {
                _w.Ascii("<mergeCell ref=\"" + r + "\"/>");
            }

            _w.Raw("</mergeCells>"u8);
        }

        _w.Ascii("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>");
        _w.Flush();
        _completed = true;
        _entry.Dispose();
    }
}
