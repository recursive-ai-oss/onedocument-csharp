using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Spreadsheets.Binding;

/// <summary>
/// The cells of the current row, by schema column, as generated <see cref="ISpreadsheetRow{TSelf}"/> code reads them. One instance
/// serves a whole read: its buffers are reused from row to row, so reading allocates only what the row's values need (strings).
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Read</c> overload returns true when the cell held a value that converted, and false when the cell is empty or could not be
/// converted, with the member left at its default. A cell that cannot be converted is reported once: lenient types get the defect
/// <c>binding.cell-unconvertible</c> in the <see cref="OpenContext"/>, strict types an <see cref="OfficeFormatException"/>. An empty cell
/// for a member that cannot be null is silent when lenient and <c>binding.cell-empty</c> when strict.
/// </para>
/// <para>
/// Conversions follow Excel's coercions: text that looks like a number reads as a number, a number reads as TRUE unless it is 0,
/// and TRUE reads as 1. An integer column takes a number only when it is whole and in range. Dates come from a number (a serial of the
/// workbook's date system) or from ISO 8601 text.
/// </para>
/// </remarks>
public sealed class RowReader
{
    private readonly RowSchema _schema;
    private readonly OpenContext _context;
    private readonly string _sheetName;
    private readonly DateSystem _dateSystem;
    private readonly Slot[] _slots;

    internal RowReader(RowSchema schema, OpenContext context, string sheetName, DateSystem dateSystem)
    {
        _schema = schema;
        _context = context;
        _sheetName = sheetName;
        _dateSystem = dateSystem;
        _slots = new Slot[schema.Count];
    }

    private struct Slot
    {
        public CellValueKind Kind;
        public double Number;
        public string? Text;
        public char[]? Chars;
        public int CharLength;
        public bool IsDate;
        public CellError Error;
        public int SourceColumn;
    }

    /// <summary>Gets the 1-based number of the sheet row being read (the record number for delimited text).</summary>
    public int RowNumber { get; internal set; }

    // ------------------------------------------------------------------ filled by the sources

    internal void Clear()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            ref Slot s = ref _slots[i];
            s.Kind = CellValueKind.Empty;
            s.Text = null;
            s.CharLength = 0;
        }
    }

    internal void SetNumber(int slot, double number, bool isDate, int sourceColumn)
    {
        ref Slot s = ref _slots[slot];
        s.Kind = CellValueKind.Number;
        s.Number = number;
        s.IsDate = isDate;
        s.SourceColumn = sourceColumn;
    }

    internal void SetBoolean(int slot, bool value, int sourceColumn)
    {
        ref Slot s = ref _slots[slot];
        s.Kind = CellValueKind.Boolean;
        s.Number = value ? 1 : 0;
        s.SourceColumn = sourceColumn;
    }

    internal void SetError(int slot, CellError error, int sourceColumn)
    {
        ref Slot s = ref _slots[slot];
        s.Kind = CellValueKind.Error;
        s.Error = error;
        s.SourceColumn = sourceColumn;
    }

    internal void SetText(int slot, string text, int sourceColumn)
    {
        ref Slot s = ref _slots[slot];
        s.Kind = CellValueKind.Text;
        s.Text = text;
        s.SourceColumn = sourceColumn;
    }

    /// <summary>Returns the slot's character buffer, at least <paramref name="length"/> long, for the source to fill before <see cref="CommitText"/>.</summary>
    internal char[] TextBuffer(int slot, int length)
    {
        ref Slot s = ref _slots[slot];
        if (s.Chars is null || s.Chars.Length < length)
        {
            s.Chars = new char[Math.Max(length, 32)];
        }

        return s.Chars;
    }

    internal void CommitText(int slot, int length, int sourceColumn)
    {
        ref Slot s = ref _slots[slot];
        s.Kind = CellValueKind.Text;
        s.Text = null;
        s.CharLength = length;
        s.SourceColumn = sourceColumn;
    }

    internal void SetSourceColumn(int slot, int sourceColumn) => _slots[slot].SourceColumn = sourceColumn;

    internal ColumnType TypeOf(int slot) => _schema.Columns[slot].Type;

    // ------------------------------------------------------------------ problems

    private bool Empty(int column)
    {
        ColumnSpec spec = _schema.Columns[column];
        if (_schema.Strict && !spec.Nullable && spec.CanRead)
        {
            Fail(column, "binding.cell-empty", "The cell is empty but '" + spec.Name + "' cannot be empty.");
        }

        return false;
    }

    /// <summary>Reports that the cell of a column could not be converted to the member's type, and tells the caller to use the default.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="expected">The name of the type wanted, for the message.</param>
    /// <returns>Always false, so a generated reader can <c>return r.Invalid(...)</c>.</returns>
    /// <exception cref="OfficeFormatException">The type is strict.</exception>
    public bool Invalid(int column, string expected)
    {
        Fail(column, "binding.cell-unconvertible", "The cell of '" + _schema.Columns[column].Name + "' cannot be read as " + expected + "; the member keeps its default.");
        return false;
    }

    private void Fail(int column, string code, string message)
    {
        int sourceColumn = _slots[column].SourceColumn;
        string address = sourceColumn >= 1 && RowNumber >= 1 && RowNumber <= CellAddress.MaxRows && sourceColumn <= CellAddress.MaxColumns
            ? new CellAddress(RowNumber, sourceColumn).ToString()
            : "A" + RowNumber.ToString(CultureInfo.InvariantCulture);
        DefectLocation where = DefectLocation.InCell(_sheetName, address);
        if (_schema.Strict)
        {
            throw new OfficeFormatException(DefectKind.Value, code, message, where);
        }

        _context.Defects.Report(new Defect(DefectKind.Value, code, DefectSeverity.Repaired, message, where));
    }

    // ------------------------------------------------------------------ text

    private static ReadOnlySpan<char> Chars(ref Slot s) => s.Text is not null ? s.Text.AsSpan() : s.Chars.AsSpan(0, s.CharLength);

    /// <summary>Reads a cell as text. A number reads as Excel shows it unformatted (a date as ISO 8601), a boolean as TRUE or FALSE, an error as its text.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The text, or null when the cell is empty.</param>
    /// <returns>True when the cell has a value.</returns>
    public bool Read(int column, out string? value)
    {
        ref Slot s = ref _slots[column];
        switch (s.Kind)
        {
            case CellValueKind.Text:
                value = s.Text ?? new string(s.Chars!, 0, s.CharLength);
                return true;
            case CellValueKind.Number:
                value = NumberText(ref s);
                return true;
            case CellValueKind.Boolean:
                value = s.Number != 0 ? "TRUE" : "FALSE";
                return true;
            case CellValueKind.Error:
                value = CellErrors.ToText(s.Error);
                return true;
            default:
                value = null;
                return Empty(column);
        }
    }

    private string NumberText(ref Slot s)
    {
        if (s.IsDate && ExcelDate.TryToDateTime(s.Number, _dateSystem, out DateTime d))
        {
            return d.TimeOfDay == TimeSpan.Zero
                ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return s.Number.ToString(CultureInfo.InvariantCulture);
    }

    // A blank text cell is an empty cell for every type but text.
    private static bool IsBlank(ref Slot s)
    {
        if (s.Kind == CellValueKind.Empty)
        {
            return true;
        }

        return s.Kind == CellValueKind.Text && Chars(ref s).IsWhiteSpace();
    }

    // ------------------------------------------------------------------ booleans and numbers

    /// <summary>Reads a cell as TRUE or FALSE (a number is TRUE unless it is 0; text is TRUE, FALSE, 1 or 0 in any case).</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out bool value)
    {
        value = false;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        switch (s.Kind)
        {
            case CellValueKind.Boolean:
            case CellValueKind.Number:
                value = s.Number != 0;
                return true;
            case CellValueKind.Text:
                ReadOnlySpan<char> t = Chars(ref s).Trim();
                if (t.Equals("true", StringComparison.OrdinalIgnoreCase) || t.SequenceEqual("1"))
                {
                    value = true;
                    return true;
                }

                if (t.Equals("false", StringComparison.OrdinalIgnoreCase) || t.SequenceEqual("0"))
                {
                    return true;
                }

                break;
        }

        return Invalid(column, "a boolean");
    }

    private static bool TryDouble(ref Slot s, out double value)
    {
        switch (s.Kind)
        {
            case CellValueKind.Number:
            case CellValueKind.Boolean:
                value = s.Number;
                return true;
            case CellValueKind.Text:
                if (double.TryParse(Chars(ref s), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
                {
                    return true;
                }

                break;
        }

        value = 0;
        return false;
    }

    private bool ReadInteger(int column, long min, long max, string name, out long value)
    {
        value = 0;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        if (s.Kind == CellValueKind.Text
            && long.TryParse(Chars(ref s), NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out long parsed))
        {
            if (parsed >= min && parsed <= max)
            {
                value = parsed;
                return true;
            }

            return Invalid(column, name);
        }

        // 9.2233720368547758E18 is 2^63, one past long.MaxValue; compare below it so the cast cannot overflow.
        if (TryDouble(ref s, out double d) && d == Math.Floor(d) && d >= -9.2233720368547758E18 && d < 9.2233720368547758E18)
        {
            long n = (long)d;
            if (n >= min && n <= max)
            {
                value = n;
                return true;
            }
        }

        return Invalid(column, name);
    }

    /// <summary>Reads a cell as a byte (whole numbers 0 to 255).</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out byte value)
    {
        bool ok = ReadInteger(column, byte.MinValue, byte.MaxValue, "a byte", out long v);
        value = (byte)v;
        return ok;
    }

    /// <summary>Reads a cell as a signed byte.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out sbyte value)
    {
        bool ok = ReadInteger(column, sbyte.MinValue, sbyte.MaxValue, "an sbyte", out long v);
        value = (sbyte)v;
        return ok;
    }

    /// <summary>Reads a cell as a 16-bit integer.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out short value)
    {
        bool ok = ReadInteger(column, short.MinValue, short.MaxValue, "a 16-bit integer", out long v);
        value = (short)v;
        return ok;
    }

    /// <summary>Reads a cell as an unsigned 16-bit integer.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out ushort value)
    {
        bool ok = ReadInteger(column, ushort.MinValue, ushort.MaxValue, "an unsigned 16-bit integer", out long v);
        value = (ushort)v;
        return ok;
    }

    /// <summary>Reads a cell as a 32-bit integer.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out int value)
    {
        bool ok = ReadInteger(column, int.MinValue, int.MaxValue, "a 32-bit integer", out long v);
        value = (int)v;
        return ok;
    }

    /// <summary>Reads a cell as an unsigned 32-bit integer.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out uint value)
    {
        bool ok = ReadInteger(column, uint.MinValue, uint.MaxValue, "an unsigned 32-bit integer", out long v);
        value = (uint)v;
        return ok;
    }

    /// <summary>Reads a cell as a 64-bit integer. A number cell is exact up to 2^53; larger values need to be text.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out long value) => ReadInteger(column, long.MinValue, long.MaxValue, "a 64-bit integer", out value);

    /// <summary>Reads a cell as an unsigned 64-bit integer.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out ulong value)
    {
        value = 0;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        if (s.Kind == CellValueKind.Text && ulong.TryParse(Chars(ref s), NumberStyles.AllowThousands | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out ulong parsed))
        {
            value = parsed;
            return true;
        }

        // 1.8446744073709552E19 is 2^64, one past ulong.MaxValue.
        if (TryDouble(ref s, out double d) && d == Math.Floor(d) && d >= 0 && d < 1.8446744073709552E19)
        {
            value = (ulong)d;
            return true;
        }

        return Invalid(column, "an unsigned 64-bit integer");
    }

    /// <summary>Reads a cell as a double. Text that does not parse, and NaN and infinity, are not numbers.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out double value)
    {
        value = 0;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        return TryDouble(ref s, out value) || Invalid(column, "a number");
    }

    /// <summary>Reads a cell as a single-precision number.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out float value)
    {
        bool ok = Read(column, out double d);
        value = (float)d;
        return ok;
    }

    /// <summary>
    /// Reads a cell as a decimal. A number cell is a double, which Excel keeps to 15 significant digits, so it converts to the
    /// decimal of those digits; text keeps every digit it has.
    /// </summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out decimal value)
    {
        value = 0;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        if (s.Kind == CellValueKind.Text)
        {
            return decimal.TryParse(Chars(ref s), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value) || Invalid(column, "a decimal");
        }

        // The decimal range is about 7.9E28; the cast throws beyond it.
        if (TryDouble(ref s, out double d) && Math.Abs(d) < 7.9E28)
        {
            value = (decimal)d;
            return true;
        }

        return Invalid(column, "a decimal");
    }

    // ------------------------------------------------------------------ dates, times, guids

    private bool TryDateTime(ref Slot s, out DateTime value)
    {
        if (s.Kind == CellValueKind.Number)
        {
            return ExcelDate.TryToDateTime(s.Number, _dateSystem, out value);
        }

        if (s.Kind == CellValueKind.Text)
        {
            return DateTime.TryParse(Chars(ref s), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out value);
        }

        value = default;
        return false;
    }

    /// <summary>Reads a cell as a date and time: a serial of the workbook's date system, or ISO 8601 text.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value, with kind Unspecified for a serial.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out DateTime value)
    {
        value = default;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        return TryDateTime(ref s, out value) || Invalid(column, "a date");
    }

    /// <summary>Reads a cell as a date (the time of day is dropped).</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out DateOnly value)
    {
        bool ok = Read(column, out DateTime d);
        value = DateOnly.FromDateTime(d);
        return ok;
    }

    /// <summary>Reads a cell as a date and time with a zero offset; a spreadsheet keeps no time zone. Text may carry an offset, which is kept.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out DateTimeOffset value)
    {
        value = default;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        if (s.Kind == CellValueKind.Text
            && DateTimeOffset.TryParse(Chars(ref s), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out value))
        {
            return true;
        }

        if (s.Kind == CellValueKind.Number && ExcelDate.TryToDateTime(s.Number, _dateSystem, out DateTime d))
        {
            value = new DateTimeOffset(d.Ticks, TimeSpan.Zero);
            return true;
        }

        return Invalid(column, "a date");
    }

    /// <summary>Reads a cell as a time of day: the fraction of a day of a number (rounded to the millisecond), or time text.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out TimeOnly value)
    {
        value = default;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        if (s.Kind == CellValueKind.Number && double.IsFinite(s.Number) && Math.Abs(s.Number) < 1E9)
        {
            double fraction = s.Number - Math.Floor(s.Number);
            long ms = (long)Math.Round(fraction * 86_400_000d);
            value = new TimeOnly((ms >= 86_400_000 ? 0 : ms) * TimeSpan.TicksPerMillisecond);
            return true;
        }

        if (s.Kind == CellValueKind.Text)
        {
            ReadOnlySpan<char> t = Chars(ref s);
            if (TimeOnly.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value))
            {
                return true;
            }

            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime d))
            {
                value = TimeOnly.FromDateTime(d);
                return true;
            }
        }

        return Invalid(column, "a time");
    }

    /// <summary>Reads a cell as a GUID from its text.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when the cell held a value that converted.</returns>
    public bool Read(int column, out Guid value)
    {
        value = default;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        return (s.Kind == CellValueKind.Text && Guid.TryParse(Chars(ref s).Trim(), out value)) || Invalid(column, "a GUID");
    }

    /// <summary>
    /// Reads the token an enum member is made from: a name (the trimmed text) or a whole number. The generated code matches it against
    /// the enum's members, ignoring case, and calls <see cref="Invalid"/> when none fits.
    /// </summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="name">The text of the cell, trimmed; empty for a number cell.</param>
    /// <param name="number">The number when <paramref name="isNumber"/> is true.</param>
    /// <param name="isNumber">True when the cell is a whole number (or text of one).</param>
    /// <returns>True when the cell has a token; false when it is empty or not usable (already reported).</returns>
    public bool ReadEnumToken(int column, out ReadOnlySpan<char> name, out long number, out bool isNumber)
    {
        name = default;
        number = 0;
        isNumber = false;
        ref Slot s = ref _slots[column];
        if (IsBlank(ref s))
        {
            return Empty(column);
        }

        if (s.Kind == CellValueKind.Text)
        {
            name = Chars(ref s).Trim();
            if (long.TryParse(name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
            {
                isNumber = true;
            }

            return true;
        }

        if (s.Kind == CellValueKind.Number && s.Number == Math.Floor(s.Number) && Math.Abs(s.Number) < 9.0E18)
        {
            number = (long)s.Number;
            isNumber = true;
            return true;
        }

        return Invalid(column, "an enum value");
    }
}
