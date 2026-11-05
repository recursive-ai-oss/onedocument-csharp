namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>The kind of value a cell holds. A formula cell has the kind of its cached result.</summary>
public enum CellValueKind : byte
{
    /// <summary>No value (a cell that only carries a style, or a formula whose result was not stored).</summary>
    Empty = 0,

    /// <summary>A number, including dates and times, which are numbers until the date rules apply (ECMA-376 Part 1, SpreadsheetML dates).</summary>
    Number = 1,

    /// <summary>Text.</summary>
    Text = 2,

    /// <summary>TRUE or FALSE.</summary>
    Boolean = 3,

    /// <summary>An error value such as <c>#DIV/0!</c>.</summary>
    Error = 4,
}

/// <summary>The error values a cell can hold (ECMA-376 Part 1 ST_CellType <c>e</c>, and the values current Excel writes).</summary>
public enum CellError
{
    /// <summary><c>#NULL!</c></summary>
    Null = 0,

    /// <summary><c>#DIV/0!</c></summary>
    DivideByZero = 1,

    /// <summary><c>#VALUE!</c></summary>
    Value = 2,

    /// <summary><c>#REF!</c></summary>
    Reference = 3,

    /// <summary><c>#NAME?</c></summary>
    Name = 4,

    /// <summary><c>#NUM!</c></summary>
    Number = 5,

    /// <summary><c>#N/A</c></summary>
    NotAvailable = 6,

    /// <summary><c>#GETTING_DATA</c></summary>
    GettingData = 7,

    /// <summary><c>#SPILL!</c></summary>
    Spill = 8,

    /// <summary><c>#CALC!</c></summary>
    Calc = 9,

    /// <summary><c>#FIELD!</c></summary>
    Field = 10,

    /// <summary><c>#CONNECT!</c></summary>
    Connect = 11,

    /// <summary><c>#BLOCKED!</c></summary>
    Blocked = 12,

    /// <summary><c>#UNKNOWN!</c></summary>
    Unknown = 13,

    /// <summary><c>#EXTERNAL!</c></summary>
    External = 14,

    /// <summary><c>#PYTHON!</c></summary>
    Python = 15,

    /// <summary><c>#BUSY!</c></summary>
    Busy = 16,
}

/// <summary>Text of the error values, as they are written in a part.</summary>
public static class CellErrors
{
    private static readonly string[] Names =
    [
        "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A", "#GETTING_DATA", "#SPILL!", "#CALC!", "#FIELD!",
        "#CONNECT!", "#BLOCKED!", "#UNKNOWN!", "#EXTERNAL!", "#PYTHON!", "#BUSY!",
    ];

    /// <summary>Gets the text of an error, for example <c>#N/A</c>.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of <see cref="CellError"/>.</exception>
    public static string ToText(CellError error) =>
        (uint)error < (uint)Names.Length ? Names[(int)error] : throw new ArgumentOutOfRangeException(nameof(error));

    /// <summary>Reads the text of an error.</summary>
    /// <param name="text">The text, such as <c>#REF!</c>; compared ignoring case.</param>
    /// <param name="error">The error when the result is true.</param>
    /// <returns>True when the text names a known error.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out CellError error)
    {
        for (int i = 0; i < Names.Length; i++)
        {
            if (text.Equals(Names[i], StringComparison.OrdinalIgnoreCase))
            {
                error = (CellError)i;
                return true;
            }
        }

        error = default;
        return false;
    }

    /// <summary>Reads the text of an error held as ASCII bytes.</summary>
    /// <param name="text">The text.</param>
    /// <param name="error">The error when the result is true.</param>
    /// <returns>True when the text names a known error.</returns>
    internal static bool TryParse(ReadOnlySpan<byte> text, out CellError error)
    {
        error = default;
        if (text.Length is < 4 or > 13 || text[0] != (byte)'#')
        {
            return false;
        }

        Span<char> chars = stackalloc char[13]; // longest name is #GETTING_DATA
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] > 0x7F)
            {
                return false;
            }

            chars[i] = (char)text[i];
        }

        return TryParse(chars[..text.Length], out error);
    }
}

/// <summary>
/// The value of a cell, as the store holds it and as the calculation stage will read it. It is a 16-byte value type: reading a cell
/// allocates nothing. A string is a handle, not text: ask the sheet (<see cref="Worksheet.GetText(CellValue)"/>) or use
/// <see cref="Cell.Text"/>. Handles are valid for the workbook they came from.
/// </summary>
/// <remarks>
/// A number is a double; a boolean is 0 or 1 in the same slot; an error is its <see cref="CellError"/> number. The string handle is a
/// non-negative index into the shared string table, or the bitwise complement of an index into the sheet's own string pool (inline
/// strings, formula results, error texts Excel does not know).
/// </remarks>
public readonly struct CellValue : IEquatable<CellValue>
{
    private readonly double _number;
    private readonly int _ref;
    private readonly CellValueKind _kind;

    internal CellValue(CellValueKind kind, double number, int reference)
    {
        _kind = kind;
        _number = number;
        _ref = reference;
    }

    /// <summary>Gets the empty value.</summary>
    public static CellValue Empty => default;

    /// <summary>Gets the kind.</summary>
    public CellValueKind Kind => _kind;

    /// <summary>Gets a value indicating whether the value is empty.</summary>
    public bool IsEmpty => _kind == CellValueKind.Empty;

    /// <summary>Gets the number. Meaningful for <see cref="CellValueKind.Number"/>; 0 or 1 for a boolean; 0 for other kinds.</summary>
    public double Number => _kind is CellValueKind.Number or CellValueKind.Boolean ? _number : 0;

    /// <summary>Gets the boolean. True when the kind is <see cref="CellValueKind.Boolean"/> and the value is TRUE.</summary>
    public bool Boolean => _kind == CellValueKind.Boolean && _number != 0;

    /// <summary>Gets the error. Meaningful for <see cref="CellValueKind.Error"/>; <see cref="CellError.Unknown"/> for an error text the table does not know.</summary>
    public CellError Error => _kind == CellValueKind.Error ? (_ref >= 0 ? (CellError)_ref : CellError.Unknown) : default;

    /// <summary>Gets the string handle (see the type remarks). Meaningful for <see cref="CellValueKind.Text"/>.</summary>
    internal int StringRef => _ref;

    internal int ErrorRef => _ref;

    /// <summary>Makes a number value.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static CellValue FromNumber(double value) => new(CellValueKind.Number, value, 0);

    /// <summary>Makes a boolean value.</summary>
    /// <param name="value">TRUE or FALSE.</param>
    /// <returns>The value.</returns>
    public static CellValue FromBoolean(bool value) => new(CellValueKind.Boolean, value ? 1 : 0, 0);

    /// <summary>Makes an error value.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The value.</returns>
    public static CellValue FromError(CellError error) => new(CellValueKind.Error, 0, (int)error);

    /// <summary>Converts a number to a value.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator CellValue(double value) => FromNumber(value);

    /// <summary>Converts a boolean to a value.</summary>
    /// <param name="value">The boolean.</param>
    public static implicit operator CellValue(bool value) => FromBoolean(value);

    /// <summary>Compares kind and payload; two string handles are equal only when they are the same handle (compare text for content).</summary>
    /// <param name="other">The other value.</param>
    /// <returns>True when equal.</returns>
    public bool Equals(CellValue other) =>
        _kind == other._kind && _ref == other._ref && (_number == other._number || (double.IsNaN(_number) && double.IsNaN(other._number)));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellValue v && Equals(v);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_kind, _number, _ref);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(CellValue left, CellValue right) => left.Equals(right);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(CellValue left, CellValue right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => _kind switch
    {
        CellValueKind.Empty => string.Empty,
        CellValueKind.Number => _number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        CellValueKind.Boolean => _number != 0 ? "TRUE" : "FALSE",
        CellValueKind.Error => _ref >= 0 ? CellErrors.ToText((CellError)_ref) : "#UNKNOWN!",
        _ => "string#" + _ref.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
