# Spreadsheets: rows of plain objects

`Spreadsheet.Read<T>` and `Spreadsheet.Write<T>` move rows of plain classes, structs and records to and from XLSX, CSV and TSV in one call. They sit on the streaming cursors (`spreadsheets-streaming.md`) and the CSV layer (`csv.md`), so memory stays constant however many rows there are. Nothing uses reflection: a source generator, run when you compile, writes the reading and writing code for each type, so the binding works under trimming and NativeAOT. Defect codes (`binding.`) are in `defects.md`.

```csharp
[SpreadsheetRow(Sheet = "Orders")]
public partial record Order(int Id, string Customer, decimal Total, DateOnly Placed, Status? State);

Spreadsheet.Write("orders.xlsx", orders);                       // or .csv, .tsv
foreach (Order o in Spreadsheet.Read<Order>("orders.xlsx")) { }  // lazy, streaming
await foreach (Order o in Spreadsheet.ReadAsync<Order>(stream)) { }
IEnumerable<object?[]> raw = Spreadsheet.ReadRows("orders.xlsx"); // no type: one array per row
```

## Marking a type

`[SpreadsheetRow]` goes on a `partial` class, struct, record or record struct (and on nothing generic). The generator adds the `ISpreadsheetRow<T>` implementation that `Read<T>` and `Write<T>` require as their constraint, so a type that is not marked does not compile into the call. Nested types work when every containing type is `partial` too.

Type options (`SpreadsheetRowAttribute`):

| Option | Default | Meaning |
|---|---|---|
| `Sheet` | first visible worksheet / `Sheet1` | The sheet to read and to write. Ignored for CSV and TSV. |
| `Strict` | false | Lenient reading reports a bad cell or a missing column as a defect and keeps the default; strict reading throws at the first one. |
| `HasHeader` | true | The first row holds column names. Without it, columns are found by position or `Index`, and writing leaves the header out. |

Strictness is a property of the type, not of the call: the statics take no options object.

## Which members are columns

- Public instance properties and fields, in declaration order. Private members only when marked `[SpreadsheetColumn]`.
- A get-only property that nothing fills (no constructor parameter of that name) is taken for a computed value and left out. Marking it `[SpreadsheetColumn]` writes it and never reads it.
- A set-only property is read and not written.
- Reading uses the parameterless constructor when the author wrote one; otherwise the constructor with the most parameters (a record's primary constructor), each parameter filled from the column of the same name (ignoring case); then an object initializer sets the remaining writable members, `init` and `required` ones included. A constructor parameter with no member is error OOBIND003; one whose member is ignored gets `default`.

`[SpreadsheetColumn]` options: a name (the header text; default the member name), `Order` (marked columns first, ascending, then the rest in declaration order), `Index` (a fixed 1-based sheet column, used for reading and writing instead of the header), `Ignore`, `Format` (an XLSX number format code), `EnumAsNumber`.

## Supported types

`string`, `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `DateTime`, `DateOnly`, `TimeOnly`, `DateTimeOffset`, `Guid`, any enum, and `Nullable<>` of the value types. Anything else is warning OOBIND001 and the member is left out.

## Reading

- **Header.** The first row with a cell is the header. A header cell matches a column by name, ignoring case and surrounding white space. Extra sheet columns are ignored. A column the sheet lacks is `binding.column-missing` (reported once, member keeps its default; strict types fail only for members that cannot be null). A repeated header uses the first and reports `binding.header-duplicate`.
- **Rows.** Rows in which no mapped column holds a value are skipped (blank lines, formatted-but-empty rows). The value of a formula cell is its cached result.
- **Empty cells.** An empty cell leaves the default (`null` for nullable members, `""` for a non-nullable string) silently; strict types raise `binding.cell-empty` for a member that cannot be null.
- **Coercions** follow Excel's: text that looks like a number reads as a number; a number reads as TRUE unless it is 0; TRUE reads as 1; a number reads as text the way it is stored (a date-formatted number as ISO 8601). An integer member takes a number only when it is whole and in range, otherwise `binding.cell-unconvertible`. A `decimal` from a number cell has the 15 significant digits Excel keeps; from text it keeps all of them.
- **Dates and times.** A number is a serial of the workbook's date system (1900, with its leap-year quirk, or 1904); ISO 8601 and other invariant-culture date text also parse. `TimeOnly` takes the fraction of the day, rounded to the millisecond. A `DateTimeOffset` has offset zero (a spreadsheet keeps no time zone), or the offset of the text it came from.
- **Enums** read from a name (any case) or a whole number that is a defined value.
- **Format by content.** XLSX, XLSM, templates and Flat OPC go through the streaming cursor; CSV and TSV through the CSV reader (delimiter detected, TSV forced to tab). A file with only a header line cannot be recognised by content, so `.csv`, `.tsv` and `.txt` extensions are the fallback.
- **Allocation.** The row buffer is reused: reading allocates the objects and the strings of string members (a string shared by many cells is built once by the cursor), nothing per column.

## Writing

- The header row is bold, the first row is frozen, and columns are wide enough for their header and content. Dates are written as serials with a date format (`yyyy-mm-dd` for `DateOnly`, `yyyy-mm-dd hh:mm:ss` for `DateTime` and `DateTimeOffset`, `hh:mm:ss` for `TimeOnly`), so Excel shows and sorts them as dates.
- `DateTime.MinValue` (an unset date) is written as an empty cell; any other date before the date system's start raises `binding.value-out-of-range`. The kind and offset of a date are dropped.
- Whole numbers beyond ±2^53 cannot be held by a spreadsheet number, so they are written as text of their digits and read back exactly. NaN and infinity are written as an empty cell. A `decimal` is written as a double in XLSX (15 significant digits) and in full in CSV.
- Enums are written by name (a value with no name as its number); `EnumAsNumber` writes numbers. A `Guid` is text in the 36-character form. `bool` is a boolean cell (`TRUE`/`FALSE` in CSV).
- CSV and TSV use the CSV writer's defaults, including the formula-injection escape: a text that starts with `=`, `+`, `-` or `@` is written with a leading apostrophe. Reading such a file back gives the apostrophe.
- Writing to a path replaces the file atomically; cancellation leaves the old file. XLSX output is deterministic.
- At most 1,048,575 rows below the header; more raises `OfficeLimitException`.

## Generator diagnostics

| Id | Severity | Meaning |
|---|---|---|
| OOBIND001 | warning | A member's type cannot be a column; it is left out. Mark it `[SpreadsheetColumn(Ignore = true)]` to silence. |
| OOBIND002 | error | The type or a containing type is not `partial`. |
| OOBIND003 | error | A constructor parameter matches no property or field. |
| OOBIND004 | warning | Two columns have the same name (ignoring case). |
| OOBIND005 | error | The type, or a containing type, is generic. |
| OOBIND006 | warning | `Index` is outside 1 to 16384 and is ignored. |
| OOBIND007 | warning | The type has no columns. |

## Not covered

Types you cannot edit (from another assembly) cannot be marked; wrap them in a row type. There are no custom converters yet, and no column-by-name lookups on the non-generic `ReadRows` (it returns values positionally, with dates as `DateTime`).
