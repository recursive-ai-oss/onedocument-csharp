namespace RecursiveAI.OneDocument.Spreadsheets.Binding;

/// <summary>
/// Marks a <c>partial</c> class, struct or record as a spreadsheet row. A source generator, run at compile time, adds the
/// <see cref="ISpreadsheetRow{TSelf}"/> implementation that <c>Spreadsheet.Read&lt;T&gt;</c> and <c>Spreadsheet.Write&lt;T&gt;</c> use,
/// so binding needs no reflection and stays correct under trimming and NativeAOT.
/// </summary>
/// <remarks>
/// <para>
/// Every public instance property and field is a column, in declaration order, unless it is marked with
/// <see cref="SpreadsheetColumnAttribute"/>. A property with a getter only is a column when a constructor parameter of the same name
/// (ignoring case) fills it, as in a record's primary constructor; otherwise it is taken for a computed value and left out. Private
/// members are columns only when they carry <see cref="SpreadsheetColumnAttribute"/>.
/// </para>
/// <para>
/// To read, the generated code uses the type's parameterless constructor when it has one, otherwise its constructor with the most
/// parameters (every parameter must name a column), and sets the remaining writable members in an object initializer.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class SpreadsheetRowAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name of the worksheet to read and to write. The default (null) reads the first visible worksheet and writes a
    /// sheet called <c>Sheet1</c>. CSV and TSV have no sheets, so the name is ignored there.
    /// </summary>
    public string? Sheet { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether reading is strict. Lenient reading (the default) reports a cell that cannot be
    /// converted, and a column the header lacks, as a defect with the cell's address, and leaves the member at its default. Strict
    /// reading throws an <see cref="OfficeFormatException"/> with the same defect, located, at the first one.
    /// </summary>
    public bool Strict { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the first row holds column names (the default). Without a header, columns are matched
    /// by <see cref="SpreadsheetColumnAttribute.Index"/>, or by position in the order of the members, and writing omits the header row.
    /// </summary>
    public bool HasHeader { get; set; } = true;
}

/// <summary>Maps one property or field of a <see cref="SpreadsheetRowAttribute"/> type to a column, or leaves it out.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SpreadsheetColumnAttribute : Attribute
{
    /// <summary>Initializes the mapping with the member's own name as the column name.</summary>
    public SpreadsheetColumnAttribute()
    {
    }

    /// <summary>Initializes the mapping with a column name.</summary>
    /// <param name="name">The header text. Reading matches it ignoring case and surrounding white space.</param>
    public SpreadsheetColumnAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Gets the column's header text, or null for the member's name.</summary>
    public string? Name { get; }

    /// <summary>
    /// Gets or sets the position of the column among the columns of the type. Columns that set it come first, in ascending order,
    /// then the others in declaration order. It decides the column order when writing and the column of a headerless read.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets a fixed 1-based sheet column (1 is A). Reading uses it instead of looking the header up; writing puts the column
    /// there. Zero (the default) means the column is found by name and written next to the previous one.
    /// </summary>
    public int Index { get; set; }

    /// <summary>Gets or sets a value indicating whether the member is left out of reading and writing.</summary>
    public bool Ignore { get; set; }

    /// <summary>
    /// Gets or sets the number format code used when writing XLSX (for example <c>#,##0.00</c> or <c>dd/mm/yyyy</c>). The default is
    /// <c>yyyy-mm-dd</c> for dates, <c>yyyy-mm-dd hh:mm:ss</c> for date-times, <c>hh:mm:ss</c> for times and General otherwise.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Gets or sets a value indicating whether an enum member is written as its number instead of its name.</summary>
    public bool EnumAsNumber { get; set; }
}
