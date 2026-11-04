using System.Globalization;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>
/// A handle to one cell position on a sheet. The cell need not exist in the store: reading an absent cell gives an empty value, and
/// writing creates it. The handle is a small object that holds no data of its own (so that <c>sheet["B2"].Value = 1</c> compiles);
/// bulk work goes through <see cref="CellStore"/>, which creates no objects per cell.
/// </summary>
public sealed class Cell : IEquatable<Cell>
{
    private readonly Worksheet _sheet;
    private readonly int _row;
    private readonly int _column;

    internal Cell(Worksheet sheet, int row, int column)
    {
        _sheet = sheet;
        _row = row;
        _column = column;
    }

    /// <summary>Gets the sheet.</summary>
    public Worksheet Sheet => _sheet;

    /// <summary>Gets the address of the cell.</summary>
    public CellAddress Address => new(_row, _column);

    /// <summary>Gets the 1-based row.</summary>
    public int Row => _row;

    /// <summary>Gets the 1-based column.</summary>
    public int Column => _column;

    /// <summary>Gets a value indicating whether the cell exists in the sheet (a cell with only a style exists).</summary>
    public bool Exists => _sheet.Store.TryGetCell(_row, _column, out _, out _);

    /// <summary>
    /// Gets or sets the value. For a formula cell it is the cached result. Setting a value keeps the cell's style and formula; set
    /// <see cref="Formula"/> to null first to drop the formula. Setting a string value needs <see cref="SetString"/>, because a string
    /// is a handle into the workbook's string table.
    /// </summary>
    /// <exception cref="ArgumentException">A string value that does not belong to this sheet's workbook was given.</exception>
    public CellValue Value
    {
        get => _sheet.Store.TryGetCell(_row, _column, out CellValue v, out _) ? v : default;
        set
        {
            if (value.Kind == CellValueKind.Text && !_sheet.OwnsStringHandle(value))
            {
                throw new ArgumentException("A string value must come from this workbook; use SetString.", nameof(value));
            }

            _sheet.Store.SetValue(_row, _column, value);
        }
    }

    /// <summary>Gets the text of the value: the string for a string cell, the error text for an error, the invariant text of a number or boolean, the empty string for an empty cell.</summary>
    public string Text => _sheet.GetText(Value);

    /// <summary>Gets the number when the value is a number (or 1/0 for a boolean); otherwise 0.</summary>
    public double Number => Value.Number;

    /// <summary>Sets a number.</summary>
    /// <param name="value">The number.</param>
    public void SetNumber(double value) => _sheet.Store.SetValue(_row, _column, CellValue.FromNumber(value));

    /// <summary>Sets a boolean.</summary>
    /// <param name="value">TRUE or FALSE.</param>
    public void SetBoolean(bool value) => _sheet.Store.SetValue(_row, _column, CellValue.FromBoolean(value));

    /// <summary>Sets an error.</summary>
    /// <param name="error">The error.</param>
    public void SetError(CellError error) => _sheet.Store.SetValue(_row, _column, CellValue.FromError(error));

    /// <summary>Sets a string. The text goes into the workbook's shared string table.</summary>
    /// <param name="text">The text; control characters XML cannot carry are stored as <c>_xHHHH_</c> escapes.</param>
    public void SetString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _sheet.Store.SetValue(_row, _column, _sheet.Workbook.MakeStringValue(text));
    }

    /// <summary>Empties the value, keeping the style and formula.</summary>
    public void ClearValue() => _sheet.Store.SetValue(_row, _column, default);

    /// <summary>Removes the cell completely: value, formula, style and any markup kept for it.</summary>
    /// <returns>True when the cell existed.</returns>
    public bool Clear() => _sheet.Store.RemoveCell(_row, _column);

    /// <summary>Gets or sets the style index (the position in the workbook's cell formats; 0 is the default). The styles themselves are not interpreted here.</summary>
    public int StyleIndex
    {
        get => _sheet.Store.TryGetCell(_row, _column, out _, out int s) ? s : 0;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _sheet.Store.SetStyle(_row, _column, value);
        }
    }

    /// <summary>
    /// Gets or sets the formula as text without the leading <c>=</c>; null when the cell has none. For the cells of a shared formula
    /// that are not its anchor the text is empty: see <see cref="FormulaDetail"/>. Setting a formula discards the cached value and
    /// asks the workbook to recalculate when it is next opened in a calculating application (the file's <c>calcPr</c> gets
    /// <c>fullCalcOnLoad</c> unless calculation is manual); nothing is calculated here.
    /// </summary>
    /// <exception cref="OfficeLimitException">The formula is longer than <see cref="OfficeLimits.MaxFormulaLength"/>.</exception>
    public string? Formula
    {
        get => _sheet.Store.GetFormula(_row, _column)?.Text;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                FormulaDetail = null;
                return;
            }

            FormulaDetail = CellFormula.FromText(value);
        }
    }

    /// <summary>Gets or sets the formula with its kind, range and shared index (see <see cref="CellFormula"/>). Setting a non-null formula discards the cached value and marks the workbook for recalculation as <see cref="Formula"/> does.</summary>
    /// <exception cref="OfficeLimitException">The formula is longer than <see cref="OfficeLimits.MaxFormulaLength"/>.</exception>
    public CellFormula? FormulaDetail
    {
        get => _sheet.Store.GetFormula(_row, _column);
        set
        {
            if (value is null)
            {
                _sheet.Store.SetFormula(_row, _column, null);
                return;
            }

            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxFormulaLength), _sheet.Workbook.Limits.MaxFormulaLength, value.Text.Length,
                Diagnostics.DefectLocation.InCell(_sheet.Name, Address.ToString()));
            _sheet.Store.SetFormula(_row, _column, value);
            _sheet.Store.SetValue(_row, _column, default);
            _sheet.Workbook.MarkNeedsRecalculation();
        }
    }

    /// <inheritdoc />
    public bool Equals(Cell? other) => other is not null && ReferenceEquals(_sheet, other._sheet) && _row == other._row && _column == other._column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Cell c && Equals(c);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_sheet, _row, _column);

    /// <inheritdoc />
    public override string ToString() => _sheet.Name + "!" + Address.ToString();

    /// <summary>Compares two handles.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they name the same cell.</returns>
    public static bool operator ==(Cell? left, Cell? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Compares two handles.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they name different cells.</returns>
    public static bool operator !=(Cell? left, Cell? right) => !(left == right);
}

/// <summary>A handle to the properties of one row (CT_Row): height, visibility, outline level and style.</summary>
public sealed class RowProperties
{
    private readonly Worksheet _sheet;
    private readonly int _row;

    internal RowProperties(Worksheet sheet, int row)
    {
        _sheet = sheet;
        _row = row;
    }

    /// <summary>Gets the 1-based row.</summary>
    public int Row => _row;

    private RowData? Data => _sheet.Store.FindRow(_row);

    /// <summary>Gets or sets the height in points, or a negative number when the row has the sheet's default height. Setting a height marks it custom.</summary>
    public double Height
    {
        get => Data?.Height ?? -1;
        set
        {
            RowData r = _sheet.Store.EditRow(_row);
            r.Height = value;
            r.Flags = value >= 0 ? r.Flags | RowFlags.CustomHeight : r.Flags & ~RowFlags.CustomHeight;
        }
    }

    /// <summary>Gets or sets whether the row is hidden.</summary>
    public bool Hidden
    {
        get => ((Data?.Flags ?? 0) & RowFlags.Hidden) != 0;
        set
        {
            RowData r = _sheet.Store.EditRow(_row);
            r.Flags = value ? r.Flags | RowFlags.Hidden : r.Flags & ~RowFlags.Hidden;
        }
    }

    /// <summary>Gets or sets the outline level, 0 to 7.</summary>
    public int OutlineLevel
    {
        get => Data?.OutlineLevel ?? 0;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 7);
            _sheet.Store.EditRow(_row).OutlineLevel = (byte)value;
        }
    }

    /// <summary>Gets or sets whether the outline group the row heads is collapsed.</summary>
    public bool Collapsed
    {
        get => ((Data?.Flags ?? 0) & RowFlags.Collapsed) != 0;
        set
        {
            RowData r = _sheet.Store.EditRow(_row);
            r.Flags = value ? r.Flags | RowFlags.Collapsed : r.Flags & ~RowFlags.Collapsed;
        }
    }

    /// <summary>Gets or sets the style index of the row's empty cells. Setting it marks the row as formatted.</summary>
    public int StyleIndex
    {
        get => Data?.Style ?? 0;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            RowData r = _sheet.Store.EditRow(_row);
            r.Style = value;
            r.Flags = value != 0 ? r.Flags | RowFlags.CustomFormat : r.Flags & ~RowFlags.CustomFormat;
        }
    }

    /// <inheritdoc />
    public override string ToString() => "row " + _row.ToString(CultureInfo.InvariantCulture);
}
