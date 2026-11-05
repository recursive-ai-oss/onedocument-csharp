using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Spreadsheets.Binding;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>
/// One-line reading and writing of rows of plain objects. A row type is a <c>partial</c> class, struct or record marked with
/// <see cref="SpreadsheetRowAttribute"/>; a source generator binds its members to columns at compile time, so nothing here uses
/// reflection and it all works under trimming and NativeAOT. Reading and writing stream: memory stays constant however many rows there are.
/// </summary>
/// <remarks>
/// <para>
/// The format is found from the content when reading (XLSX, XLSM, Flat OPC, CSV, TSV), never from the extension alone; a file that
/// holds only a header line, and so cannot be recognised by content, falls back to its <c>.csv</c>, <c>.tsv</c> or <c>.txt</c> extension.
/// Writing picks the format from the extension (<c>.xlsx</c>, <c>.csv</c>, <c>.tsv</c>) or from an explicit <see cref="OfficeFormat"/>.
/// How strictly cells are converted is a property of the row type (<see cref="SpreadsheetRowAttribute.Strict"/>), not of these methods.
/// </para>
/// <para>
/// Readers are lazy: nothing is opened until the sequence is enumerated, and the source is closed when the enumeration ends or is
/// disposed. A sequence read from a stream starts from the stream's position at the first step and must be enumerated once at a time.
/// </para>
/// </remarks>
public static class Spreadsheet
{
    /// <summary>Reads the rows of the first visible worksheet (or the sheet the type names) of a file, or of CSV or TSV text.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits and the defect sink; null for the defaults. Lenient types report unconvertible cells to its <see cref="OpenContext.Defects"/>.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>A lazy sequence with one value per row below the header. Rows with no value in any mapped column are skipped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="OfficeFormatException">Raised when the sequence is enumerated: the content is not a spreadsheet, the sheet is missing, or a strict type meets a bad cell or a missing column.</exception>
    /// <exception cref="OfficeLimitException">Raised when the sequence is enumerated: a limit was exceeded.</exception>
    public static IEnumerable<T> Read<T>(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
        where T : ISpreadsheetRow<T> =>
        RowPipeline.ReadFile<T>(path ?? throw new ArgumentNullException(nameof(path)), context, cancellationToken);

    /// <summary>Reads the rows from a seekable stream; see <see cref="Read{T}(string, OpenContext?, CancellationToken)"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="stream">A readable, seekable stream; it is left open.</param>
    /// <param name="context">Limits and the defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>A lazy sequence of rows.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">Raised when the sequence is enumerated: the stream is not seekable.</exception>
    public static IEnumerable<T> Read<T>(Stream stream, OpenContext? context = null, CancellationToken cancellationToken = default)
        where T : ISpreadsheetRow<T> =>
        RowPipeline.ReadStream<T>(stream ?? throw new ArgumentNullException(nameof(stream)), null, context, cancellationToken);

    /// <summary>Reads the rows of a file as an asynchronous sequence that hands the thread back every thousand rows; the cursors read synchronously.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits and the defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static IAsyncEnumerable<T> ReadAsync<T>(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
        where T : ISpreadsheetRow<T> =>
        RowPipeline.ReadFileAsync<T>(path ?? throw new ArgumentNullException(nameof(path)), context, cancellationToken);

    /// <summary>Reads the rows of a seekable stream as an asynchronous sequence.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="stream">A readable, seekable stream; it is left open.</param>
    /// <param name="context">Limits and the defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public static IAsyncEnumerable<T> ReadAsync<T>(Stream stream, OpenContext? context = null, CancellationToken cancellationToken = default)
        where T : ISpreadsheetRow<T> =>
        RowPipeline.ReadStreamAsync<T>(stream ?? throw new ArgumentNullException(nameof(stream)), null, context, cancellationToken);

    /// <summary>
    /// Reads every row of a sheet without a row type: one array per row, indexed by column (index 0 is column A), with null for an empty
    /// cell. A value is a <see cref="double"/>, a <see cref="string"/>, a <see cref="bool"/>, a <see cref="DateTime"/> (a number in a
    /// date format), or a <see cref="CellError"/>; delimited text gives strings. The header is an ordinary row.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="sheet">The sheet's name; null for the first visible worksheet. Ignored for delimited text.</param>
    /// <param name="context">Limits and the defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>A lazy sequence of rows; rows are as long as their last stored cell.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static IEnumerable<object?[]> ReadRows(string path, string? sheet = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ReadRowsFile(path, sheet, context, cancellationToken);
    }

    private static IEnumerable<object?[]> ReadRowsFile(string path, string? sheet, OpenContext? context, CancellationToken cancellationToken)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        foreach (object?[] row in RowPipeline.ReadRows(file, Path.GetExtension(path), sheet, context, cancellationToken))
        {
            yield return row;
        }
    }

    /// <summary>Writes rows to a file whose extension picks the format: <c>.xlsx</c>, <c>.csv</c> or <c>.tsv</c>. The file is replaced atomically.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="path">The file to write.</param>
    /// <param name="rows">The rows. A header row is written first unless the type has <see cref="SpreadsheetRowAttribute.HasHeader"/> false. The sequence is read once, in order.</param>
    /// <param name="cancellationToken">Cancels the write; the existing file is left as it was.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="OfficeUnsupportedException">The extension is not one of the three.</exception>
    /// <exception cref="OfficeLimitException">There are more rows than a sheet holds.</exception>
    /// <exception cref="OfficeFormatException">A date is outside the range of the workbook's date system.</exception>
    public static void Write<T>(string path, IEnumerable<T> rows, CancellationToken cancellationToken = default)
        where T : ISpreadsheetRow<T> =>
        RowPipeline.WriteFile(path ?? throw new ArgumentNullException(nameof(path)), rows ?? throw new ArgumentNullException(nameof(rows)), cancellationToken);

    /// <summary>Writes rows to a stream in a stated format.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="stream">A writable stream; it is left open. XLSX output does not need it to seek.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="format">XLSX, CSV or TSV.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="FormatNotAvailableException">The format is not XLSX, CSV or TSV.</exception>
    public static void Write<T>(Stream stream, IEnumerable<T> rows, OfficeFormat format = OfficeFormat.Xlsx, CancellationToken cancellationToken = default)
        where T : ISpreadsheetRow<T> =>
        RowPipeline.WriteStream(stream ?? throw new ArgumentNullException(nameof(stream)), rows ?? throw new ArgumentNullException(nameof(rows)), format, cancellationToken);

    /// <summary>Writes an asynchronous sequence of rows to a file whose extension picks the format.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="path">The file to write.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static Task WriteAsync<T>(string path, IAsyncEnumerable<T> rows, CancellationToken cancellationToken = default)
        where T : ISpreadsheetRow<T> =>
        RowPipeline.WriteFileAsync(path ?? throw new ArgumentNullException(nameof(path)), rows ?? throw new ArgumentNullException(nameof(rows)), cancellationToken);

    /// <summary>Writes an asynchronous sequence of rows to a stream in a stated format.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="stream">A writable stream; it is left open.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="format">XLSX, CSV or TSV.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the last row is written.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static Task WriteAsync<T>(Stream stream, IAsyncEnumerable<T> rows, OfficeFormat format = OfficeFormat.Xlsx, CancellationToken cancellationToken = default)
        where T : ISpreadsheetRow<T> =>
        RowPipeline.WriteStreamAsync(stream ?? throw new ArgumentNullException(nameof(stream)), rows ?? throw new ArgumentNullException(nameof(rows)), format, cancellationToken);
}
