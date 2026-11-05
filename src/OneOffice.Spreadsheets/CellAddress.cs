using System.Globalization;
using System.Text;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>
/// The position of one cell on a sheet: a 1-based row and a 1-based column, within Excel's limits
/// (1,048,576 rows by 16,384 columns, the limits of the format; ECMA-376 Part 1 ST_CellRef).
/// </summary>
/// <remarks>The default value is not a valid address (row 0, column 0); <see cref="IsValid"/> says so.</remarks>
public readonly struct CellAddress : IEquatable<CellAddress>, IComparable<CellAddress>
{
    /// <summary>The number of rows on a sheet: 1,048,576.</summary>
    public const int MaxRows = 1_048_576;

    /// <summary>The number of columns on a sheet: 16,384 (column XFD).</summary>
    public const int MaxColumns = 16_384;

    /// <summary>Creates an address.</summary>
    /// <param name="row">The 1-based row, 1 to <see cref="MaxRows"/>.</param>
    /// <param name="column">The 1-based column, 1 to <see cref="MaxColumns"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The row or column is outside the sheet.</exception>
    public CellAddress(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(row, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row, MaxRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, MaxColumns);
        Row = row;
        Column = column;
    }

    private CellAddress(int row, int column, bool _)
    {
        Row = row;
        Column = column;
    }

    /// <summary>Gets the 1-based row.</summary>
    public int Row { get; }

    /// <summary>Gets the 1-based column (A is 1).</summary>
    public int Column { get; }

    /// <summary>Gets a value indicating whether the row and column are inside the sheet.</summary>
    public bool IsValid => Row is >= 1 and <= MaxRows && Column is >= 1 and <= MaxColumns;

    /// <summary>Parses an A1 reference such as <c>B2</c> or <c>XFD1048576</c>; <c>$</c> signs are accepted and ignored.</summary>
    /// <param name="text">The reference.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is not a cell reference inside the sheet.</exception>
    public static CellAddress Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out CellAddress a) ? a : throw new FormatException("'" + text + "' is not a cell reference inside the sheet.");
    }

    /// <summary>Parses an A1 reference; <c>$</c> signs are accepted and ignored.</summary>
    /// <param name="text">The reference.</param>
    /// <param name="address">The address when the result is true.</param>
    /// <returns>True when the text is a cell reference inside the sheet.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out CellAddress address)
    {
        address = default;
        int i = 0;
        if (i < text.Length && text[i] == '$')
        {
            i++;
        }

        int col = 0;
        int letters = 0;
        while (i < text.Length && (uint)((text[i] | 0x20) - 'a') <= 'z' - 'a')
        {
            col = (col * 26) + ((text[i] | 0x20) - 'a' + 1);
            i++;
            if (++letters > 3)
            {
                return false;
            }
        }

        if (letters == 0)
        {
            return false;
        }

        if (i < text.Length && text[i] == '$')
        {
            i++;
        }

        int row = 0;
        int digits = 0;
        while (i < text.Length && (uint)(text[i] - '0') <= 9)
        {
            row = (row * 10) + (text[i] - '0');
            i++;
            if (++digits > 7)
            {
                return false;
            }
        }

        if (digits == 0 || i != text.Length || row is < 1 or > MaxRows || col is < 1 or > MaxColumns)
        {
            return false;
        }

        address = new CellAddress(row, col, false);
        return true;
    }

    /// <summary>Parses an A1 reference held as UTF-8 bytes, as it is read from a part, without allocating.</summary>
    /// <param name="text">The reference.</param>
    /// <param name="address">The address when the result is true.</param>
    /// <returns>True when the text is a cell reference inside the sheet.</returns>
    public static bool TryParse(ReadOnlySpan<byte> text, out CellAddress address)
    {
        address = default;
        int i = 0;
        int col = 0;
        int letters = 0;
        while (i < text.Length && (uint)((text[i] | 0x20) - 'a') <= 'z' - 'a')
        {
            col = (col * 26) + ((text[i] | 0x20) - 'a' + 1);
            i++;
            if (++letters > 3)
            {
                return false;
            }
        }

        if (letters == 0)
        {
            return false;
        }

        int row = 0;
        int digits = 0;
        while (i < text.Length && (uint)(text[i] - '0') <= 9)
        {
            row = (row * 10) + (text[i] - '0');
            i++;
            if (++digits > 7)
            {
                return false;
            }
        }

        if (digits == 0 || i != text.Length || row is < 1 or > MaxRows || col is < 1 or > MaxColumns)
        {
            return false;
        }

        address = new CellAddress(row, col, false);
        return true;
    }

    /// <summary>Gets the letters of a column: 1 is <c>A</c>, 27 is <c>AA</c>, 16384 is <c>XFD</c>.</summary>
    /// <param name="column">The 1-based column.</param>
    /// <returns>The column name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The column is outside 1 to <see cref="MaxColumns"/>.</exception>
    public static string ColumnName(int column)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, MaxColumns);
        Span<char> buffer = stackalloc char[3]; // at most XFD
        int n = WriteColumn(column, buffer);
        return new string(buffer[..n]);
    }

    /// <summary>Parses the letters of a column name (<c>A</c> to <c>XFD</c>, any case).</summary>
    /// <param name="name">The column name.</param>
    /// <param name="column">The 1-based column when the result is true.</param>
    /// <returns>True when the name is a column inside the sheet.</returns>
    public static bool TryParseColumn(ReadOnlySpan<char> name, out int column)
    {
        column = 0;
        if (name.Length is 0 or > 3)
        {
            return false;
        }

        foreach (char c in name)
        {
            if ((uint)((c | 0x20) - 'a') > 'z' - 'a')
            {
                return false;
            }

            column = (column * 26) + ((c | 0x20) - 'a' + 1);
        }

        return column <= MaxColumns;
    }

    /// <summary>Writes the A1 form into a UTF-8 buffer.</summary>
    /// <param name="destination">At least 10 bytes (three letters and seven digits).</param>
    /// <returns>The number of bytes written.</returns>
    public int WriteUtf8(Span<byte> destination)
    {
        Span<char> letters = stackalloc char[3]; // at most XFD
        int n = WriteColumn(Column, letters);
        for (int i = 0; i < n; i++)
        {
            destination[i] = (byte)letters[i];
        }

        int row = Row;
        int digits = 1;
        for (int t = row; t >= 10; t /= 10)
        {
            digits++;
        }

        for (int i = digits - 1; i >= 0; i--)
        {
            destination[n + i] = (byte)('0' + (row % 10));
            row /= 10;
        }

        return n + digits;
    }

    private static int WriteColumn(int column, Span<char> buffer)
    {
        int n = column > 702 ? 3 : column > 26 ? 2 : 1;
        for (int i = n - 1; i >= 0; i--)
        {
            column--;
            buffer[i] = (char)('A' + (column % 26));
            column /= 26;
        }

        return n;
    }

    /// <summary>Gets the A1 form, for example <c>B2</c>.</summary>
    /// <returns>The reference; <c>?</c> for an address that is not valid.</returns>
    public override string ToString()
    {
        if (!IsValid)
        {
            return "?";
        }

        Span<byte> b = stackalloc byte[10]; // at most XFD1048576
        int n = WriteUtf8(b);
        return Encoding.ASCII.GetString(b[..n]);
    }

    /// <inheritdoc />
    public bool Equals(CellAddress other) => Row == other.Row && Column == other.Column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellAddress a && Equals(a);

    /// <inheritdoc />
    public override int GetHashCode() => (Row * 16_411) ^ Column;

    /// <summary>Orders by row, then by column: the order of cells in a sheet part.</summary>
    /// <param name="other">The other address.</param>
    /// <returns>Negative, zero or positive.</returns>
    public int CompareTo(CellAddress other) => Row != other.Row ? Row.CompareTo(other.Row) : Column.CompareTo(other.Column);

    /// <summary>Compares two addresses.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they are the same cell.</returns>
    public static bool operator ==(CellAddress left, CellAddress right) => left.Equals(right);

    /// <summary>Compares two addresses.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they differ.</returns>
    public static bool operator !=(CellAddress left, CellAddress right) => !left.Equals(right);

    /// <summary>Tests whether an address comes before another in sheet order (row, then column).</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when <paramref name="left"/> is earlier.</returns>
    public static bool operator <(CellAddress left, CellAddress right) => left.CompareTo(right) < 0;

    /// <summary>Tests whether an address comes after another in sheet order.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when <paramref name="left"/> is later.</returns>
    public static bool operator >(CellAddress left, CellAddress right) => left.CompareTo(right) > 0;

    /// <summary>Tests whether an address comes before or is another.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when not later.</returns>
    public static bool operator <=(CellAddress left, CellAddress right) => left.CompareTo(right) <= 0;

    /// <summary>Tests whether an address comes after or is another.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when not earlier.</returns>
    public static bool operator >=(CellAddress left, CellAddress right) => left.CompareTo(right) >= 0;

    internal string ToStringInvariant() => string.Create(CultureInfo.InvariantCulture, $"{ColumnName(Column)}{Row}");
}

/// <summary>A rectangle of cells, both corners included (<c>A1:C3</c>); a single cell has both corners equal.</summary>
public readonly struct CellRange : IEquatable<CellRange>
{
    /// <summary>Creates a range; the corners are put in order.</summary>
    /// <param name="first">One corner.</param>
    /// <param name="last">The opposite corner.</param>
    /// <exception cref="ArgumentException">A corner is not a valid address.</exception>
    public CellRange(CellAddress first, CellAddress last)
    {
        if (!first.IsValid || !last.IsValid)
        {
            throw new ArgumentException("A range needs two valid cell addresses.");
        }

        TopLeft = new CellAddress(Math.Min(first.Row, last.Row), Math.Min(first.Column, last.Column));
        BottomRight = new CellAddress(Math.Max(first.Row, last.Row), Math.Max(first.Column, last.Column));
    }

    /// <summary>Gets the upper-left cell.</summary>
    public CellAddress TopLeft { get; }

    /// <summary>Gets the lower-right cell.</summary>
    public CellAddress BottomRight { get; }

    /// <summary>Gets a value indicating whether the range has valid corners (the default value has not).</summary>
    public bool IsValid => TopLeft.IsValid;

    /// <summary>Gets the number of rows.</summary>
    public int RowCount => BottomRight.Row - TopLeft.Row + 1;

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount => BottomRight.Column - TopLeft.Column + 1;

    /// <summary>Parses <c>A1:C3</c> or a single cell <c>B2</c>.</summary>
    /// <param name="text">The reference.</param>
    /// <param name="range">The range when the result is true.</param>
    /// <returns>True when the text is a range inside the sheet.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out CellRange range)
    {
        range = default;
        int colon = text.IndexOf(':');
        if (colon < 0)
        {
            if (!CellAddress.TryParse(text, out CellAddress one))
            {
                return false;
            }

            range = new CellRange(one, one);
            return true;
        }

        if (!CellAddress.TryParse(text[..colon], out CellAddress a) || !CellAddress.TryParse(text[(colon + 1)..], out CellAddress b))
        {
            return false;
        }

        range = new CellRange(a, b);
        return true;
    }

    /// <summary>Parses <c>A1:C3</c> or a single cell.</summary>
    /// <param name="text">The reference.</param>
    /// <returns>The range.</returns>
    /// <exception cref="FormatException">The text is not a range inside the sheet.</exception>
    public static CellRange Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out CellRange r) ? r : throw new FormatException("'" + text + "' is not a cell range inside the sheet.");
    }

    /// <summary>Tests whether a cell lies inside the range.</summary>
    /// <param name="address">The cell.</param>
    /// <returns>True when inside.</returns>
    public bool Contains(CellAddress address) =>
        address.Row >= TopLeft.Row && address.Row <= BottomRight.Row && address.Column >= TopLeft.Column && address.Column <= BottomRight.Column;

    /// <summary>Tests whether two ranges share a cell.</summary>
    /// <param name="other">The other range.</param>
    /// <returns>True when they overlap.</returns>
    public bool Intersects(CellRange other) =>
        TopLeft.Row <= other.BottomRight.Row && other.TopLeft.Row <= BottomRight.Row
        && TopLeft.Column <= other.BottomRight.Column && other.TopLeft.Column <= BottomRight.Column;

    /// <summary>Gets the A1 form: <c>B2</c> for one cell, <c>A1:C3</c> otherwise.</summary>
    /// <returns>The reference.</returns>
    public override string ToString() => TopLeft == BottomRight ? TopLeft.ToString() : TopLeft + ":" + BottomRight;

    /// <inheritdoc />
    public bool Equals(CellRange other) => TopLeft == other.TopLeft && BottomRight == other.BottomRight;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellRange r && Equals(r);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TopLeft, BottomRight);

    /// <summary>Compares two ranges.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when the same.</returns>
    public static bool operator ==(CellRange left, CellRange right) => left.Equals(right);

    /// <summary>Compares two ranges.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(CellRange left, CellRange right) => !left.Equals(right);
}
