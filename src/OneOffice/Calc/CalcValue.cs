using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>The error values a formula can produce. The numbers are stable and match Excel's error codes where it has one.</summary>
public enum CalcError : byte
{
    /// <summary>Not an error.</summary>
    None = 0,

    /// <summary><c>#NULL!</c>: the intersection of two ranges is empty.</summary>
    Null = 1,

    /// <summary><c>#DIV/0!</c>.</summary>
    Div0 = 2,

    /// <summary><c>#VALUE!</c>: an operand or argument has the wrong type.</summary>
    Value = 3,

    /// <summary><c>#REF!</c>: an invalid reference.</summary>
    Ref = 4,

    /// <summary><c>#NAME?</c>: an unknown name or function.</summary>
    Name = 5,

    /// <summary><c>#NUM!</c>: a number that cannot be used or held.</summary>
    Num = 6,

    /// <summary><c>#N/A</c>.</summary>
    NA = 7,

    /// <summary><c>#GETTING_DATA</c>: a value that is still loading.</summary>
    GettingData = 8,

    /// <summary><c>#SPILL!</c>: a dynamic array result has no room.</summary>
    Spill = 9,

    /// <summary><c>#CALC!</c>: the calculation engine could not finish (an empty array, for example).</summary>
    Calc = 10,

    /// <summary><c>#BLOCKED!</c>.</summary>
    Blocked = 11,

    /// <summary><c>#CONNECT!</c>.</summary>
    Connect = 12,

    /// <summary><c>#BUSY!</c>.</summary>
    Busy = 13,

    /// <summary><c>#FIELD!</c>: a missing field of a data type.</summary>
    Field = 14,

    /// <summary><c>#UNKNOWN!</c>.</summary>
    Unknown = 15,
}

/// <summary>Text and parsing for <see cref="CalcError"/>.</summary>
public static class CalcErrors
{
    /// <summary>The text of an error as it is written in a formula or shown in a cell (<c>#DIV/0!</c>).</summary>
    /// <param name="error">The error.</param>
    /// <returns>The text, or the empty string for <see cref="CalcError.None"/>.</returns>
    public static string ToText(CalcError error) => error switch
    {
        CalcError.Null => "#NULL!",
        CalcError.Div0 => "#DIV/0!",
        CalcError.Value => "#VALUE!",
        CalcError.Ref => "#REF!",
        CalcError.Name => "#NAME?",
        CalcError.Num => "#NUM!",
        CalcError.NA => "#N/A",
        CalcError.GettingData => "#GETTING_DATA",
        CalcError.Spill => "#SPILL!",
        CalcError.Calc => "#CALC!",
        CalcError.Blocked => "#BLOCKED!",
        CalcError.Connect => "#CONNECT!",
        CalcError.Busy => "#BUSY!",
        CalcError.Field => "#FIELD!",
        CalcError.Unknown => "#UNKNOWN!",
        _ => string.Empty,
    };

    /// <summary>Reads an error literal, ignoring case.</summary>
    /// <param name="text">The text, such as <c>#N/A</c>.</param>
    /// <param name="error">The error.</param>
    /// <returns><see langword="true"/> when the text is exactly one of the error literals.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out CalcError error)
    {
        for (int i = 1; i <= 15; i++)
        {
            if (text.Equals(ToText((CalcError)i), StringComparison.OrdinalIgnoreCase))
            {
                error = (CalcError)i;
                return true;
            }
        }

        error = CalcError.None;
        return false;
    }
}

/// <summary>What a <see cref="CalcValue"/> holds.</summary>
public enum ValueKind : byte
{
    /// <summary>An empty cell or a missing argument.</summary>
    Blank = 0,

    /// <summary>A number (also dates and times, which are serial numbers).</summary>
    Number = 1,

    /// <summary>Text.</summary>
    Text = 2,

    /// <summary>TRUE or FALSE.</summary>
    Boolean = 3,

    /// <summary>An error value.</summary>
    Error = 4,

    /// <summary>A rectangular array of values (<see cref="CalcArray"/>).</summary>
    Array = 5,

    /// <summary>A reference to cells (<see cref="CalcReference"/>), not yet read.</summary>
    Reference = 6,

    /// <summary>A function value made by <c>LAMBDA</c> (<see cref="LambdaValue"/>).</summary>
    Lambda = 7,
}

/// <summary>
/// One value in a calculation. A small struct: numbers, booleans, errors and blanks need no allocation, text holds its string, and arrays,
/// references and lambdas hold their object. Equality is structural only for scalars; compare arrays with the evaluator.
/// </summary>
public readonly struct CalcValue : IEquatable<CalcValue>
{
    private readonly double _number;
    private readonly object? _object;

    private CalcValue(ValueKind kind, double number, object? obj)
    {
        Kind = kind;
        _number = number;
        _object = obj;
    }

    /// <summary>The empty value.</summary>
    public static CalcValue Blank => default;

    /// <summary>An omitted argument (<c>IF(A1,,2)</c>). It behaves as <see cref="Blank"/> everywhere except where a function asks <see cref="IsMissing"/> to tell "left out" from "an empty cell".</summary>
    public static CalcValue Missing => new(ValueKind.Blank, 1, null);

    /// <summary>TRUE.</summary>
    public static CalcValue True => new(ValueKind.Boolean, 1, null);

    /// <summary>FALSE.</summary>
    public static CalcValue False => new(ValueKind.Boolean, 0, null);

    /// <summary>The empty text.</summary>
    public static CalcValue EmptyText => new(ValueKind.Text, 0, string.Empty);

    /// <summary>What the value is.</summary>
    public ValueKind Kind { get; }

    /// <summary>True for <see cref="ValueKind.Blank"/>.</summary>
    public bool IsBlank => Kind == ValueKind.Blank;

    /// <summary>True for an omitted argument (<see cref="Missing"/>).</summary>
    public bool IsMissing => Kind == ValueKind.Blank && _number == 1;

    /// <summary>True for <see cref="ValueKind.Error"/>.</summary>
    public bool IsError => Kind == ValueKind.Error;

    /// <summary>True for numbers, text, booleans, errors and blanks (not arrays, references or lambdas).</summary>
    public bool IsScalar => Kind <= ValueKind.Error;

    /// <summary>The number for <see cref="ValueKind.Number"/>; 1 or 0 for a boolean; 0 otherwise.</summary>
    public double Number => Kind is ValueKind.Number or ValueKind.Boolean ? _number : 0;

    /// <summary>The boolean for <see cref="ValueKind.Boolean"/>; false otherwise.</summary>
    public bool Boolean => Kind == ValueKind.Boolean && _number != 0;

    /// <summary>The text for <see cref="ValueKind.Text"/>; the empty string otherwise.</summary>
    public string Text => Kind == ValueKind.Text ? (string)_object! : string.Empty;

    /// <summary>The error for <see cref="ValueKind.Error"/>; <see cref="CalcError.None"/> otherwise.</summary>
    public CalcError Error => Kind == ValueKind.Error ? (CalcError)(int)_number : CalcError.None;

    /// <summary>The array for <see cref="ValueKind.Array"/>; <see langword="null"/> otherwise.</summary>
    public CalcArray? Array => Kind == ValueKind.Array ? (CalcArray?)_object : null;

    /// <summary>The reference for <see cref="ValueKind.Reference"/>; <see langword="null"/> otherwise.</summary>
    public CalcReference? Reference => Kind == ValueKind.Reference ? (CalcReference?)_object : null;

    /// <summary>The lambda for <see cref="ValueKind.Lambda"/>; <see langword="null"/> otherwise.</summary>
    public LambdaValue? Lambda => Kind == ValueKind.Lambda ? (LambdaValue?)_object : null;

    /// <summary>A number.</summary>
    /// <param name="value">The number. NaN and infinities are not numbers in Excel; use <see cref="FromError(CalcError)"/> with <see cref="CalcError.Num"/>.</param>
    /// <returns>The value.</returns>
    public static CalcValue FromNumber(double value) => new(ValueKind.Number, value, null);

    /// <summary>Text.</summary>
    /// <param name="value">The text (null reads as empty).</param>
    /// <returns>The value.</returns>
    public static CalcValue FromText(string? value) => new(ValueKind.Text, 0, value ?? string.Empty);

    /// <summary>A boolean.</summary>
    /// <param name="value">TRUE or FALSE.</param>
    /// <returns>The value.</returns>
    public static CalcValue FromBoolean(bool value) => value ? True : False;

    /// <summary>An error.</summary>
    /// <param name="error">The error; <see cref="CalcError.None"/> reads as <see cref="CalcError.Value"/>.</param>
    /// <returns>The value.</returns>
    public static CalcValue FromError(CalcError error) => new(ValueKind.Error, error == CalcError.None ? (double)CalcError.Value : (double)error, null);

    /// <summary>An array.</summary>
    /// <param name="array">The array.</param>
    /// <returns>The value.</returns>
    public static CalcValue FromArray(CalcArray array)
    {
        ArgumentNullException.ThrowIfNull(array);
        return new(ValueKind.Array, 0, array);
    }

    /// <summary>A reference.</summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The value.</returns>
    public static CalcValue FromReference(CalcReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(ValueKind.Reference, 0, reference);
    }

    /// <summary>A lambda.</summary>
    /// <param name="lambda">The lambda.</param>
    /// <returns>The value.</returns>
    public static CalcValue FromLambda(LambdaValue lambda)
    {
        ArgumentNullException.ThrowIfNull(lambda);
        return new(ValueKind.Lambda, 0, lambda);
    }

    /// <summary>Implicit conversion from a number.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator CalcValue(double value) => FromNumber(value);

    /// <summary>Implicit conversion from text.</summary>
    /// <param name="value">The text.</param>
    public static implicit operator CalcValue(string? value) => FromText(value);

    /// <summary>Implicit conversion from a boolean.</summary>
    /// <param name="value">The boolean.</param>
    public static implicit operator CalcValue(bool value) => FromBoolean(value);

    /// <summary>Scalar equality: same kind and same content (text compared ordinally); arrays, references and lambdas by identity.</summary>
    /// <param name="other">The other value.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(CalcValue other) =>
        Kind == other.Kind && (Kind switch
        {
            ValueKind.Blank => true,
            ValueKind.Text => string.Equals((string)_object!, (string)other._object!, StringComparison.Ordinal),
            ValueKind.Array or ValueKind.Reference or ValueKind.Lambda => ReferenceEquals(_object, other._object),
            _ => _number.Equals(other._number),
        });

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CalcValue v && Equals(v);

    /// <inheritdoc/>
    public override int GetHashCode() => Kind switch
    {
        ValueKind.Text => HashCode.Combine(Kind, string.GetHashCode((string)_object!, StringComparison.Ordinal)),
        ValueKind.Array or ValueKind.Reference or ValueKind.Lambda => HashCode.Combine(Kind, RuntimeHelpers.GetHashCode(_object!)),
        ValueKind.Blank => 0,
        _ => HashCode.Combine(Kind, _number),
    };

    /// <summary>Equality.</summary>
    /// <param name="a">Left.</param>
    /// <param name="b">Right.</param>
    /// <returns>Whether equal.</returns>
    public static bool operator ==(CalcValue a, CalcValue b) => a.Equals(b);

    /// <summary>Inequality.</summary>
    /// <param name="a">Left.</param>
    /// <param name="b">Right.</param>
    /// <returns>Whether different.</returns>
    public static bool operator !=(CalcValue a, CalcValue b) => !a.Equals(b);

    /// <summary>A short description for diagnostics (not Excel's display text).</summary>
    /// <returns>The description.</returns>
    public override string ToString() => Kind switch
    {
        ValueKind.Blank => "(blank)",
        ValueKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        ValueKind.Text => "\"" + (string)_object! + "\"",
        ValueKind.Boolean => _number != 0 ? "TRUE" : "FALSE",
        ValueKind.Error => CalcErrors.ToText(Error),
        ValueKind.Array => $"array {Array!.Rows}x{Array.Columns}",
        ValueKind.Reference => "reference " + Reference,
        _ => "lambda",
    };
}

/// <summary>
/// A rectangular array of values, row-major. Either dense, or a single value broadcast over the shape (a scalar stretched to a size costs
/// one value, not rows × columns). Cells may hold any scalar; nested arrays are not allowed (Excel has none).
/// </summary>
public sealed class CalcArray
{
    private readonly CalcValue[]? _cells;
    private readonly CalcValue _fill;

    /// <summary>The largest array Excel's grid can hold: 1,048,576 rows × 16,384 columns.</summary>
    public const long MaxCells = 1048576L * 16384L;

    /// <summary>Creates a dense array that takes ownership of <paramref name="cells"/>.</summary>
    /// <param name="rows">Row count, at least 1.</param>
    /// <param name="columns">Column count, at least 1.</param>
    /// <param name="cells">Row-major cells; the length must be rows × columns.</param>
    /// <exception cref="ArgumentException">The shape is empty or does not match the cells.</exception>
    public CalcArray(int rows, int columns, CalcValue[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (rows < 1 || columns < 1 || (long)rows * columns != cells.Length)
        {
            throw new ArgumentException("The cells do not fill rows x columns.", nameof(cells));
        }

        Rows = rows;
        Columns = columns;
        _cells = cells;
    }

    private CalcArray(int rows, int columns, CalcValue fill)
    {
        Rows = rows;
        Columns = columns;
        _fill = fill;
    }

    /// <summary>Number of rows, at least 1.</summary>
    public int Rows { get; }

    /// <summary>Number of columns, at least 1.</summary>
    public int Columns { get; }

    /// <summary>Number of cells.</summary>
    public long Count => (long)Rows * Columns;

    /// <summary>True when the array is one value stretched over the shape.</summary>
    public bool IsBroadcast => _cells is null;

    /// <summary>A single value over a shape.</summary>
    /// <param name="rows">Rows, at least 1.</param>
    /// <param name="columns">Columns, at least 1.</param>
    /// <param name="value">The value in every cell.</param>
    /// <returns>The array.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is below 1.</exception>
    public static CalcArray Broadcast(int rows, int columns, CalcValue value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        return new CalcArray(rows, columns, value);
    }

    /// <summary>A 1 × 1 array.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The array.</returns>
    public static CalcArray OfValue(CalcValue value) => new(1, 1, value);

    /// <summary>The value at a position.</summary>
    /// <param name="row">Zero-based row.</param>
    /// <param name="column">Zero-based column.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the array.</exception>
    public CalcValue this[int row, int column]
    {
        get
        {
            if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns)
            {
                throw new ArgumentOutOfRangeException(row >= Rows || row < 0 ? nameof(row) : nameof(column));
            }

            return _cells is null ? _fill : _cells[(row * Columns) + column];
        }
    }

    /// <summary>The value at a position, <c>#N/A</c> outside the array, as broadcasting a one-row or one-column array wants it.</summary>
    /// <param name="row">Zero-based row; a one-row array answers every row.</param>
    /// <param name="column">Zero-based column; a one-column array answers every column.</param>
    /// <returns>The value.</returns>
    public CalcValue Broadcasting(int row, int column)
    {
        if (Rows == 1)
        {
            row = 0;
        }

        if (Columns == 1)
        {
            column = 0;
        }

        if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns)
        {
            return CalcValue.FromError(CalcError.NA);
        }

        return _cells is null ? _fill : _cells[(row * Columns) + column];
    }

    /// <summary>Copies the cells into a new row-major array.</summary>
    /// <returns>The copy.</returns>
    public CalcValue[] ToArray()
    {
        if (_cells is not null)
        {
            return (CalcValue[])_cells.Clone();
        }

        var a = new CalcValue[checked(Rows * Columns)];
        System.Array.Fill(a, _fill);
        return a;
    }
}

/// <summary>One rectangular block of cells on a sheet (or across a run of sheets for a 3-D reference). Rows and columns are 1-based and inclusive.</summary>
/// <param name="Sheet">The sheet name, or <see langword="null"/> for the sheet of the formula's cell.</param>
/// <param name="LastSheet">The last sheet of a 3-D reference (<c>Sheet1:Sheet3!A1</c>), else <see langword="null"/>.</param>
/// <param name="Row1">First row, 1 to 1,048,576.</param>
/// <param name="Column1">First column, 1 to 16,384.</param>
/// <param name="Row2">Last row.</param>
/// <param name="Column2">Last column.</param>
public readonly record struct CellArea(string? Sheet, string? LastSheet, int Row1, int Column1, int Row2, int Column2)
{
    /// <summary>The last row of a sheet.</summary>
    public const int MaxRow = 1048576;

    /// <summary>The last column of a sheet (XFD).</summary>
    public const int MaxColumn = 16384;

    /// <summary>Number of rows.</summary>
    public int Rows => Row2 - Row1 + 1;

    /// <summary>Number of columns.</summary>
    public int Columns => Column2 - Column1 + 1;

    /// <summary>True for a single cell.</summary>
    public bool IsSingleCell => Row1 == Row2 && Column1 == Column2;

    /// <summary>Whether a cell lies inside.</summary>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    /// <returns>True when inside.</returns>
    public bool Contains(int row, int column) => row >= Row1 && row <= Row2 && column >= Column1 && column <= Column2;
}

/// <summary>
/// A reference value: one or more areas (a union has several), optionally the spilled range of a cell (<c>A1#</c>). The reference is
/// resolved by an <see cref="ICalcResolver"/> only when something reads it.
/// </summary>
public sealed class CalcReference
{
    private readonly CellArea[] _areas;

    /// <summary>Creates a reference from areas.</summary>
    /// <param name="areas">At least one area. The array is copied.</param>
    /// <param name="isSpill">True when this is <c>A1#</c>: the area names the anchor cell, the extent comes from the grid.</param>
    /// <exception cref="ArgumentException">No areas were given.</exception>
    public CalcReference(ReadOnlySpan<CellArea> areas, bool isSpill = false)
    {
        if (areas.IsEmpty)
        {
            throw new ArgumentException("A reference needs at least one area.", nameof(areas));
        }

        _areas = areas.ToArray();
        IsSpill = isSpill;
    }

    /// <summary>A reference to one cell.</summary>
    /// <param name="sheet">Sheet name, or null for the current sheet.</param>
    /// <param name="row">1-based row.</param>
    /// <param name="column">1-based column.</param>
    public CalcReference(string? sheet, int row, int column)
        : this([new CellArea(sheet, null, row, column, row, column)])
    {
    }

    /// <summary>The areas, in order.</summary>
    public ReadOnlySpan<CellArea> Areas => _areas;

    /// <summary>Number of areas.</summary>
    public int AreaCount => _areas.Length;

    /// <summary>True for <c>A1#</c>.</summary>
    public bool IsSpill { get; }

    /// <summary>True for exactly one single-cell area.</summary>
    public bool IsSingleCell => _areas.Length == 1 && _areas[0].IsSingleCell && !IsSpill;

    /// <summary>The first area.</summary>
    public CellArea First => _areas[0];

    /// <summary>A short description (<c>Sheet1!R1C1:R2C2</c>) for diagnostics.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _areas.Length; i++)
        {
            CellArea a = _areas[i];
            if (i > 0)
            {
                sb.Append(',');
            }

            if (a.Sheet is not null)
            {
                sb.Append(a.Sheet).Append('!');
            }

            sb.Append(CultureInfo.InvariantCulture, $"R{a.Row1}C{a.Column1}");
            if (!a.IsSingleCell)
            {
                sb.Append(CultureInfo.InvariantCulture, $":R{a.Row2}C{a.Column2}");
            }
        }

        if (IsSpill)
        {
            sb.Append('#');
        }

        return sb.ToString();
    }
}

/// <summary>A function value made by <c>LAMBDA</c>: parameter names, the body and the names it captured. Invoke it with <see cref="Evaluator.Invoke"/>.</summary>
public sealed class LambdaValue
{
    private readonly string[] _parameters;

    internal LambdaValue(string[] parameters, FormulaNode body, Scope? captured)
    {
        _parameters = parameters;
        Body = body;
        Captured = captured;
    }

    // A registry function used as a value (MAP(x, SUM)): it has no body and takes any number of arguments the function accepts.
    internal LambdaValue(FunctionDefinition function)
    {
        _parameters = [];
        Body = new MissingNode();
        Function = function;
    }

    internal FunctionDefinition? Function { get; }

    /// <summary>The parameter names, in order, without the file-form <c>_xlpm.</c> prefix.</summary>
    public ReadOnlySpan<string> Parameters => _parameters;

    /// <summary>The body expression.</summary>
    public FormulaNode Body { get; }

    internal Scope? Captured { get; }
}
