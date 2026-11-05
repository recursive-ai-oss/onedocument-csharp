namespace RecursiveAI.OneOffice.Spreadsheets.Binding;

/// <summary>What a column holds; decides the default number format written and how text is turned into a value.</summary>
public enum ColumnType : byte
{
    /// <summary>Text.</summary>
    Text = 0,

    /// <summary>TRUE or FALSE.</summary>
    Boolean = 1,

    /// <summary>Any integer or floating-point number, or a decimal.</summary>
    Number = 2,

    /// <summary>A calendar date (<see cref="DateOnly"/>).</summary>
    Date = 3,

    /// <summary>A date with a time (<see cref="DateTime"/>, <see cref="DateTimeOffset"/>).</summary>
    DateTime = 4,

    /// <summary>A time of day (<see cref="TimeOnly"/>).</summary>
    Time = 5,

    /// <summary>A <see cref="Guid"/>, kept as text.</summary>
    Identifier = 6,

    /// <summary>An enum, written by name or by number and read from either.</summary>
    Enum = 7,
}

/// <summary>One column of a <see cref="RowSchema"/>, as the generator worked it out from the member and its attributes.</summary>
/// <param name="Name">The header text.</param>
/// <param name="Type">What the column holds.</param>
/// <param name="Nullable">True when the member can hold no value (a nullable value type or reference), so an empty cell is not an error.</param>
/// <param name="Index">The fixed 1-based sheet column, or 0 when the column is found by name and written after the previous one.</param>
/// <param name="Format">The number format code for XLSX, or null for the default of <paramref name="Type"/>.</param>
/// <param name="CanRead">True when reading fills the member.</param>
/// <param name="CanWrite">True when writing emits the member.</param>
public readonly record struct ColumnSpec(string Name, ColumnType Type, bool Nullable, int Index, string? Format, bool CanRead, bool CanWrite);

/// <summary>The columns of a spreadsheet row type, in output order. Generated code builds one per type; it is immutable.</summary>
public sealed class RowSchema
{
    private readonly ColumnSpec[] _columns;

    /// <summary>Initializes a schema. Generated code calls this; the arguments are not checked beyond null.</summary>
    /// <param name="typeName">The name of the row type, for messages.</param>
    /// <param name="sheet">The worksheet to use, or null.</param>
    /// <param name="strict">True for strict reading.</param>
    /// <param name="hasHeader">True when the first row holds column names.</param>
    /// <param name="columns">The columns, in output order. The array is kept.</param>
    public RowSchema(string typeName, string? sheet, bool strict, bool hasHeader, ColumnSpec[] columns)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(columns);
        TypeName = typeName;
        Sheet = sheet;
        Strict = strict;
        HasHeader = hasHeader;
        _columns = columns;
    }

    /// <summary>Gets the name of the row type.</summary>
    public string TypeName { get; }

    /// <summary>Gets the worksheet the type names, or null.</summary>
    public string? Sheet { get; }

    /// <summary>Gets a value indicating whether reading is strict.</summary>
    public bool Strict { get; }

    /// <summary>Gets a value indicating whether the first row holds column names.</summary>
    public bool HasHeader { get; }

    /// <summary>Gets the columns, in output order.</summary>
    public ReadOnlySpan<ColumnSpec> Columns => _columns;

    /// <summary>Gets the number of columns.</summary>
    public int Count => _columns.Length;
}

/// <summary>
/// Implemented by generated code on a <see cref="SpreadsheetRowAttribute"/> type. Application code does not implement it; it is the
/// constraint of <c>Spreadsheet.Read&lt;T&gt;</c> and <c>Spreadsheet.Write&lt;T&gt;</c>.
/// </summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface ISpreadsheetRow<TSelf>
    where TSelf : ISpreadsheetRow<TSelf>
{
    /// <summary>Gets the columns of the type.</summary>
    static abstract RowSchema Schema { get; }

    /// <summary>Builds one value from the current row.</summary>
    /// <param name="row">The row, holding one cell per column of <see cref="Schema"/> (the column number is the index in the schema).</param>
    /// <returns>The new value.</returns>
    static abstract TSelf Read(RowReader row);

    /// <summary>Writes one value as a row.</summary>
    /// <param name="value">The value.</param>
    /// <param name="row">The row writer; cells are written by schema column number.</param>
    static abstract void Write(in TSelf value, RowWriter row);
}
