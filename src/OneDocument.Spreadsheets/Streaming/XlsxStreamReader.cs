using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Spreadsheets.Streaming;

/// <summary>One sheet of a workbook as the streaming reader lists it.</summary>
/// <param name="Index">The 0-based position in the workbook.</param>
/// <param name="Name">The sheet's name.</param>
/// <param name="SheetId">The <c>sheetId</c> of the sheet element.</param>
/// <param name="Kind">Worksheet, chart sheet, dialog sheet or macro sheet. Only worksheets can be opened as cursors.</param>
/// <param name="State">Visible, hidden or very hidden.</param>
/// <param name="PartName">The sheet's part, or null when its relationship leads nowhere (the sheet then reads as empty).</param>
public readonly record struct StreamSheetInfo(int Index, string Name, int SheetId, SheetKind Kind, SheetState State, string? PartName);

/// <summary>
/// Opens an XLSX, XLSM, XLTX, XLTM, XLAM or Flat OPC workbook for forward-only reading: lists the sheets and streams the rows and cells
/// of one at a time through a <see cref="SheetCursor"/>, without building the workbook model.
/// </summary>
/// <remarks>
/// <para>
/// Opening reads the package directory, the content types, the relationships and the workbook part, nothing else. The shared string
/// table and the style sheet are read the first time a cursor asks for text or a date; the table is then held as plain text (the text
/// of every entry, UTF-8, plus four bytes per entry), so a workbook with many distinct long strings costs that much memory; nothing
/// else grows with the file. Cell data is never held.
/// </para>
/// <para>A sheet part under 8 MB is read into the package once and kept; larger parts are streamed from the file. Opening never recalculates; cached values are what the cursor returns.</para>
/// </remarks>
public sealed class XlsxStreamReader : IDisposable
{
    private readonly Workbook _book;
    private readonly List<StreamSheetInfo> _sheets = [];
    private readonly CancellationToken _cancellation;
    private StreamStrings? _strings;
    private StreamStyles? _styles;
    private bool _disposed;

    private XlsxStreamReader(Workbook book, CancellationToken cancellation)
    {
        _book = book;
        _cancellation = cancellation;
        int i = 0;
        foreach (Worksheet s in book.Sheets)
        {
            _sheets.Add(new StreamSheetInfo(i++, s.Name, s.SheetId, s.Kind, s.State, s.PartName));
        }

        Context = book.OpenContext;
        DateSystem = book.Uses1904Dates ? DateSystem.Date1904 : DateSystem.Date1900;
    }

    internal OpenContext Context { get; }

    // Tests and the fuzz target set this to compare the sheet scanner with the full tokenizer.
    internal bool ForceFullTokenizer { get; set; }

    /// <summary>Gets the sheets in workbook order.</summary>
    public IReadOnlyList<StreamSheetInfo> Sheets => _sheets;

    /// <summary>Gets the date system of the workbook (<c>workbookPr date1904</c>), used by <see cref="SheetCursor.TryGetDateTime"/>.</summary>
    public DateSystem DateSystem { get; }

    /// <summary>Opens a workbook from a file. The format is found from the content, never the extension.</summary>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open, and the row reads of every cursor made from this reader.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised workbook, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static XlsxStreamReader Open(string path, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        new(Workbook.Open(path, context, cancellationToken), cancellationToken);

    /// <summary>Opens a workbook from a seekable stream.</summary>
    /// <param name="stream">The stream; it must stay readable until the reader is disposed.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the reader is disposed.</param>
    /// <param name="cancellationToken">Cancels the open, and the row reads of every cursor made from this reader.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised workbook, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format.</exception>
    public static XlsxStreamReader Open(Stream stream, OpenContext? context = null, bool leaveOpen = true, CancellationToken cancellationToken = default) =>
        new(Workbook.Open(stream, context, leaveOpen, cancellationToken), cancellationToken);

    /// <summary>Opens a workbook from a byte source, which the reader then owns.</summary>
    /// <param name="source">The bytes; it must support synchronous reads.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open, and the row reads of every cursor made from this reader.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised workbook, or is damaged beyond repair.</exception>
    public static XlsxStreamReader Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        new(Workbook.Open(source, context, cancellationToken), cancellationToken);

    /// <summary>Opens a sheet's rows.</summary>
    /// <param name="index">The 0-based index into <see cref="Sheets"/>.</param>
    /// <returns>The cursor, positioned before the first row. The caller disposes it. Several cursors may be open; each streams its own part.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No such sheet.</exception>
    /// <exception cref="ArgumentException">The sheet is a chart, dialog or macro sheet.</exception>
    /// <exception cref="OfficeFormatException">The sheet part is not a worksheet or its head is not well-formed XML.</exception>
    public SheetCursor OpenSheet(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_sheets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        StreamSheetInfo info = _sheets[index];
        if (info.Kind != SheetKind.Worksheet)
        {
            throw new ArgumentException("Sheet '" + info.Name + "' is not a worksheet.", nameof(index));
        }

        if (info.PartName is null || _book.Package.FindPart(info.PartName) is not { } part)
        {
            return new SheetCursor(this, Context, info.Name);
        }

        return new SheetCursor(this, part.OpenRead(), Context, info.PartName, info.Name, _cancellation);
    }

    /// <summary>Opens a sheet's rows by name (compared ignoring case; the first match when names repeat).</summary>
    /// <param name="name">The sheet's name.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="KeyNotFoundException">No sheet has that name.</exception>
    /// <exception cref="ArgumentException">The sheet is a chart, dialog or macro sheet.</exception>
    public SheetCursor OpenSheet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (StreamSheetInfo s in _sheets)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return OpenSheet(s.Index);
            }
        }

        throw new KeyNotFoundException("No sheet is named '" + name + "'.");
    }

    /// <summary>Gets the number of entries in the shared string table (reads the table on first use).</summary>
    /// <exception cref="OfficeFormatException">The table part is not well-formed XML.</exception>
    public int SharedStringCount => GetSharedStrings().Count;

    /// <summary>Gets one shared string as text (reads the table on first use; each string is built once and kept, within a budget of about 8 million characters, then built per call).</summary>
    /// <param name="index">The 0-based index.</param>
    /// <returns>The text, empty for an index outside the table.</returns>
    public string GetSharedString(int index) => GetSharedStrings().GetString(index);

    /// <summary>Tells whether a cell style shows dates or times, by the number format of the style's cell format.</summary>
    /// <param name="styleIndex">The cell's <c>s</c> index.</param>
    /// <returns>True for a date or time format; false for any other, and for an index the style sheet does not have.</returns>
    /// <exception cref="OfficeFormatException">The style part is not well-formed XML.</exception>
    public bool IsDateStyle(int styleIndex) => GetStyles().IsDate(styleIndex);

    /// <summary>Gets the number format id of a cell style (0 for General and for an unknown index).</summary>
    /// <param name="styleIndex">The cell's <c>s</c> index.</param>
    /// <returns>The <c>numFmtId</c>.</returns>
    public int GetNumberFormatId(int styleIndex) => GetStyles().NumberFormatId(styleIndex);

    internal StreamStrings GetSharedStrings()
    {
        if (_strings is not null)
        {
            return _strings;
        }

        _strings = StreamStrings.Empty;
        if (_book.FindRelatedPart("sharedStrings") is { } part)
        {
            using Stream s = part.OpenRead();
            _strings = StreamStrings.Read(s, Context, part.Name);
        }

        return _strings;
    }

    private StreamStyles GetStyles()
    {
        if (_styles is not null)
        {
            return _styles;
        }

        _styles = StreamStyles.Empty;
        if (_book.FindRelatedPart("styles") is { } part)
        {
            using Stream s = part.OpenRead();
            _styles = StreamStyles.Read(s, Context, part.Name);
        }

        return _styles;
    }

    /// <summary>Closes the workbook.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _book.Dispose();
        }
    }
}
