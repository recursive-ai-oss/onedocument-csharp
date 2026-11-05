using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Spreadsheets.Streaming;

/// <summary>
/// Writes a new workbook sheet by sheet and row by row, with memory that does not grow with the number of rows: every row is turned
/// into XML and deflated into the output as it is written. Add a sheet with <see cref="AddSheet"/>, write its rows through the returned
/// <see cref="SheetStreamWriter"/>, add the next sheet (which completes the previous one), and call <see cref="Finish"/>.
/// </summary>
/// <remarks>
/// <para>
/// The output stream need not seek. The file is written in one pass: sheets as they come, then the shared strings, the style sheet, the
/// workbook part, the relationships and the content types. Output is deterministic: no clock, random value or machine setting reaches
/// the bytes.
/// </para>
/// <para>
/// Memory held until the end: the shared strings (up to <see cref="XlsxWriteOptions.MaxSharedStrings"/>), the cell formats, the merged ranges of the
/// sheet being written, and a few buffers. Formulas are written as text; a formula written without a cached value makes the workbook
/// ask the application to calculate everything on opening (<c>fullCalcOnLoad</c>). Nothing is calculated here.
/// </para>
/// <para>Disposing the writer finishes the file if <see cref="Finish"/> was not called, like a zip archive does; when a write failed, dispose the output stream and discard it instead.</para>
/// </remarks>
public sealed class XlsxStreamWriter : IDisposable
{
    internal const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    private static readonly System.Buffers.SearchValues<char> InvalidNameChars = System.Buffers.SearchValues.Create(":\\/?*[]");

    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly string? _tempPath;
    private readonly string? _finalPath;
    private readonly StreamZipWriter _zip;
    private readonly XlsxWriteOptions _options;
    private readonly CancellationToken _cancellation;
    private readonly List<(string Name, SheetState State)> _sheets = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly StyleSheetBuilder _styles = new();
    private readonly Dictionary<StreamCellFormat, int> _formatIndex = [];
    private readonly List<string> _strings = [];
    private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);
    private long _stringChars;
    private long _stringReferences;
    private SheetStreamWriter? _current;
    private bool _finished;
    private bool _formulaWithoutValue;

    private XlsxStreamWriter(Stream output, bool leaveOpen, XlsxWriteOptions options, string? tempPath, string? finalPath, CancellationToken cancellation)
    {
        _output = output;
        _leaveOpen = leaveOpen;
        _options = options;
        _cancellation = cancellation;
        _tempPath = tempPath;
        _finalPath = finalPath;
        _zip = new StreamZipWriter(output, options.Zip64, options.CompressionLevel);
        DateSystem = options.Date1904 ? DateSystem.Date1904 : DateSystem.Date1900;
    }

    internal int StyleCount => _options.StylesXml.IsEmpty ? _styles.Count : int.MaxValue;

    internal bool SheetCountIsOne => _sheets.Count == 1;

    internal DateSystem DateSystem { get; }

    internal CancellationToken Cancellation => _cancellation;

    internal XlsxWriteOptions Options => _options;

    /// <summary>Starts a workbook that is written to a stream.</summary>
    /// <param name="output">A writable stream; it need not seek.</param>
    /// <param name="options">How to build the file; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the writer is disposed.</param>
    /// <param name="cancellationToken">Checked at the start of every row.</param>
    /// <returns>The writer, which the caller disposes.</returns>
    /// <exception cref="ArgumentException">The stream cannot be written.</exception>
    public static XlsxStreamWriter Create(Stream output, XlsxWriteOptions? options = null, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The stream must be writable.", nameof(output));
        }

        return new XlsxStreamWriter(output, leaveOpen, options ?? new XlsxWriteOptions(), null, null, cancellationToken);
    }

    /// <summary>Starts a workbook that is written to a file. The bytes go to a temporary file beside it, which replaces the file when the workbook is finished.</summary>
    /// <param name="path">The file to create or replace.</param>
    /// <param name="options">How to build the file; null for the defaults.</param>
    /// <param name="cancellationToken">Checked at the start of every row.</param>
    /// <returns>The writer, which the caller disposes.</returns>
    /// <exception cref="IOException">The file cannot be created.</exception>
    public static XlsxStreamWriter Create(string path, XlsxWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);
        string temp = full + ".part";
        var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.SequentialScan);
        return new XlsxStreamWriter(fs, false, options ?? new XlsxWriteOptions(), temp, full, cancellationToken);
    }

    /// <summary>Adds a cell format and returns the style index cells use for it. Equal formats return the same index; 0 is the default format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The style index, at most 63,999.</returns>
    /// <exception cref="InvalidOperationException">The options gave a style sheet of their own, or the workbook already holds the most formats Excel allows.</exception>
    public int AddFormat(StreamCellFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (!_options.StylesXml.IsEmpty)
        {
            throw new InvalidOperationException("The options supply a style sheet; style indexes are the caller's.");
        }

        if (!_formatIndex.TryGetValue(format, out int i))
        {
            i = _styles.Add(format);
            _formatIndex[format] = i;
        }

        return i;
    }

    /// <summary>Starts a sheet, completing the one before it.</summary>
    /// <param name="name">The sheet's name: 1 to 31 characters, none of <c>: \ / ? * [ ]</c>, not starting or ending with an apostrophe, and not equal (ignoring case) to another sheet's.</param>
    /// <param name="options">The sheet's set-up; null for the defaults.</param>
    /// <returns>The sheet writer. It stays valid until the next sheet is added or the workbook is finished.</returns>
    /// <exception cref="ArgumentException">The name is not valid or is taken.</exception>
    /// <exception cref="InvalidOperationException">The workbook was finished.</exception>
    public SheetStreamWriter AddSheet(string name, SheetWriteOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        ValidateName(name);
        _current?.Complete();
        _names.Add(name);
        options ??= new SheetWriteOptions();
        _sheets.Add((name, options.State));
        Stream entry = _zip.BeginEntry("xl/worksheets/sheet" + _sheets.Count.ToString(CultureInfo.InvariantCulture) + ".xml");
        _current = new SheetStreamWriter(this, entry, options);
        return _current;
    }

    private void ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is 0 or > 31 || name.AsSpan().IndexOfAny(InvalidNameChars) >= 0 || name[0] == '\'' || name[^1] == '\'')
        {
            throw new ArgumentException("A sheet name is 1 to 31 characters, without : \\ / ? * [ ], and does not start or end with an apostrophe.", nameof(name));
        }

        if (_names.Contains(name))
        {
            throw new ArgumentException("A sheet named '" + name + "' exists.", nameof(name));
        }
    }

    internal void NoteFormulaWithoutValue() => _formulaWithoutValue = true;

    /// <summary>Returns the shared string index for a text, adding it, or -1 when strings are inline or the table is full.</summary>
    internal int SharedIndex(ReadOnlySpan<char> text)
    {
        if (_options.StringStorage != StreamStringStorage.Shared)
        {
            return -1;
        }

        Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> lookup = _stringIndex.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(text, out int i))
        {
            _stringReferences++;
            return i;
        }

        if (_strings.Count >= _options.MaxSharedStrings || _stringChars + text.Length > 16_000_000)
        {
            return -1;
        }

        string s = text.ToString();
        i = _strings.Count;
        _strings.Add(s);
        _stringIndex[s] = i;
        _stringChars += s.Length;
        _stringReferences++;
        return i;
    }

    /// <summary>Completes the last sheet and writes the rest of the package. Calling it again does nothing.</summary>
    /// <exception cref="InvalidOperationException">No sheet was added (a workbook has at least one).</exception>
    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        if (_sheets.Count == 0)
        {
            throw new InvalidOperationException("A workbook has at least one sheet.");
        }

        _current?.Complete();
        _finished = true;
        bool hasStrings = _strings.Count > 0;
        if (hasStrings)
        {
            using Stream s = _zip.BeginEntry("xl/sharedStrings.xml");
            var w = new Utf8Buffer(s);
            w.Ascii(Decl + "<sst xmlns=\"" + MainNamespace + "\" count=\"" + _stringReferences.ToString(CultureInfo.InvariantCulture) + "\" uniqueCount=\""
                + _strings.Count.ToString(CultureInfo.InvariantCulture) + "\">");
            foreach (string str in _strings)
            {
                w.Ascii(NeedsPreserve(str) ? "<si><t xml:space=\"preserve\">" : "<si><t>");
                w.Text(str);
                w.Ascii("</t></si>");
            }

            w.Ascii("</sst>");
            w.Flush();
        }

        using (Stream s = _zip.BeginEntry("xl/styles.xml"))
        {
            if (!_options.StylesXml.IsEmpty)
            {
                s.Write(_options.StylesXml.Span);
            }
            else
            {
                var w = new Utf8Buffer(s);
                _styles.Write(w, MainNamespace);
                w.Flush();
            }
        }

        using (Stream s = _zip.BeginEntry("xl/workbook.xml"))
        {
            var w = new Utf8Buffer(s);
            w.Ascii(Decl + "<workbook xmlns=\"" + MainNamespace + "\" xmlns:r=\"" + RelNamespace + "\"><workbookPr" + (_options.Date1904 ? " date1904=\"1\"" : string.Empty)
                + "/><bookViews><workbookView/></bookViews><sheets>");
            for (int i = 0; i < _sheets.Count; i++)
            {
                (string name, SheetState state) = _sheets[i];
                string n = (i + 1).ToString(CultureInfo.InvariantCulture);
                w.Ascii("<sheet name=\"");
                w.Attribute(name);
                w.Ascii("\" sheetId=\"" + n + "\"" + (state == SheetState.Hidden ? " state=\"hidden\"" : state == SheetState.VeryHidden ? " state=\"veryHidden\"" : string.Empty)
                    + " r:id=\"rId" + n + "\"/>");
            }

            w.Ascii("</sheets><calcPr calcId=\"191029\"" + (_formulaWithoutValue ? " fullCalcOnLoad=\"1\"" : string.Empty) + "/></workbook>");
            w.Flush();
        }

        using (Stream s = _zip.BeginEntry("xl/_rels/workbook.xml.rels"))
        {
            var w = new Utf8Buffer(s);
            w.Ascii(Decl + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < _sheets.Count; i++)
            {
                string n = (i + 1).ToString(CultureInfo.InvariantCulture);
                w.Ascii("<Relationship Id=\"rId" + n + "\" Type=\"" + RelType + "worksheet\" Target=\"worksheets/sheet" + n + ".xml\"/>");
            }

            int next = _sheets.Count + 1;
            w.Ascii("<Relationship Id=\"rId" + next.ToString(CultureInfo.InvariantCulture) + "\" Type=\"" + RelType + "styles\" Target=\"styles.xml\"/>");
            if (hasStrings)
            {
                w.Ascii("<Relationship Id=\"rId" + (next + 1).ToString(CultureInfo.InvariantCulture) + "\" Type=\"" + RelType + "sharedStrings\" Target=\"sharedStrings.xml\"/>");
            }

            w.Ascii("</Relationships>");
            w.Flush();
        }

        _zip.WriteEntry("_rels/.rels", Encoding.UTF8.GetBytes(Decl + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"" + RelType + "officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>"));

        using (Stream s = _zip.BeginEntry("[Content_Types].xml"))
        {
            var w = new Utf8Buffer(s);
            w.Ascii(Decl + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            for (int i = 1; i <= _sheets.Count; i++)
            {
                w.Ascii("<Override PartName=\"/xl/worksheets/sheet" + i.ToString(CultureInfo.InvariantCulture) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }

            w.Ascii("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            if (hasStrings)
            {
                w.Ascii("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
            }

            w.Ascii("</Types>");
            w.Flush();
        }

        _zip.Finish();
    }

    internal static bool NeedsPreserve(ReadOnlySpan<char> s) =>
        s.Length > 0 && (s[0] is ' ' or '\t' or '\n' or '\r' || s[^1] is ' ' or '\t' or '\n' or '\r');

    /// <summary>Finishes the file when <see cref="Finish"/> was not called, then closes the output (a file this writer created replaces its target).</summary>
    public void Dispose()
    {
        if (_current is null && _sheets.Count == 0 && !_finished)
        {
            // Nothing was written: leave no file behind.
            CloseOutput(keep: false);
            return;
        }

        bool ok = false;
        try
        {
            Finish();
            ok = true;
        }
        finally
        {
            CloseOutput(ok);
        }
    }

    private void CloseOutput(bool keep)
    {
        if (_tempPath is not null)
        {
            _output.Dispose();
            if (keep && _finalPath is not null)
            {
                File.Move(_tempPath, _finalPath, overwrite: true);
            }
            else
            {
                File.Delete(_tempPath);
            }
        }
        else if (!_leaveOpen)
        {
            _output.Dispose();
        }
    }
}
