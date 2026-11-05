using System.Text;

namespace RecursiveAI.OneOffice.Spreadsheets.Csv;

/// <summary>When a field is put in quotes.</summary>
public enum CsvQuoting : byte
{
    /// <summary>Only a field that holds the delimiter, a quote or a line break, as Excel writes it.</summary>
    Minimal = 0,

    /// <summary>Every field that is not empty.</summary>
    Always = 1,
}

/// <summary>What the writer does with text that a spreadsheet program would run as a formula.</summary>
public enum CsvFormulaInjection : byte
{
    /// <summary>
    /// Text that starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return gets a single quote put in front of it, and is
    /// written quoted, so that Excel and other programs show it as text instead of calculating it (the OWASP "CSV injection" guidance).
    /// Numbers written as numbers are never touched. This is the default: the file is usually opened in a spreadsheet, and the cost is a
    /// visible quote on the few texts that really start with these characters.
    /// </summary>
    Escape = 0,

    /// <summary>Text is written as it is. Choose it when the file is read by a program, not opened in a spreadsheet.</summary>
    None = 1,
}

/// <summary>How a CSV or TSV is written.</summary>
public sealed class CsvWriteOptions
{
    /// <summary>Gets the field delimiter. The default is a comma; use a tab for TSV.</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>Gets the quote character. The default is the double quote.</summary>
    public char Quote { get; init; } = '"';

    /// <summary>Gets the line break after each record. The default is CR LF, which is what Excel writes.</summary>
    public string LineEnding { get; init; } = "\r\n";

    /// <summary>Gets the encoding. The default is UTF-8. A code page other than the Unicode ones and Latin-1 needs a registered provider; characters it cannot hold become <c>?</c>.</summary>
    public Encoding Encoding { get; init; } = Encoding.UTF8;

    /// <summary>
    /// Gets a value indicating whether the byte-order mark of <see cref="Encoding"/> is written first. The default is true: Excel reads
    /// UTF-8 without a mark as the system's legacy code page and garbles non-ASCII text. Set false for tools that dislike the mark.
    /// </summary>
    public bool ByteOrderMark { get; init; } = true;

    /// <summary>Gets when fields are quoted. The default is <see cref="CsvQuoting.Minimal"/>.</summary>
    public CsvQuoting Quoting { get; init; }

    /// <summary>Gets how text that looks like a formula is written. The default is <see cref="CsvFormulaInjection.Escape"/>.</summary>
    public CsvFormulaInjection FormulaInjection { get; init; }

    /// <summary>
    /// Gets a value indicating whether cell values are written as they display (numbers through their number format, dates as dates) or raw
    /// (numbers in the shortest form that reads back exactly, dates as serial numbers, booleans as TRUE and FALSE). The default is true, as
    /// Excel's Save As does. Applies to sheets only; a row stream writes what it is given.
    /// </summary>
    public bool Formatted { get; init; } = true;
}

/// <summary>
/// A forward-only writer of delimited text. Fields are written one at a time with <see cref="WriteField(ReadOnlySpan{char}, bool)"/> and a record
/// ends with <see cref="EndRecord"/>. The writer buffers a little and holds no record in memory, so a file of any size is written in constant space.
/// </summary>
public sealed class CsvWriter : IDisposable
{
    private readonly TextWriter _out;
    private readonly bool _ownsWriter;
    private readonly CsvWriteOptions _options;
    private readonly char _delimiter;
    private readonly char _quote;
    private readonly System.Buffers.SearchValues<char> _special;
    private int _fieldsInRecord;
    private bool _disposed;

    /// <summary>Writes to a stream.</summary>
    /// <param name="stream">The destination.</param>
    /// <param name="options">Delimiter, quoting, encoding; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the writer is disposed.</param>
    public CsvWriter(Stream stream, CsvWriteOptions? options = null, bool leaveOpen = true)
        : this(MakeWriter(stream, options ?? new CsvWriteOptions(), leaveOpen), options, ownsWriter: true)
    {
    }

    /// <summary>Writes to text. The encoding options are ignored.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="options">Delimiter and quoting; null for the defaults.</param>
    /// <param name="ownsWriter">True to dispose the writer with this one.</param>
    public CsvWriter(TextWriter writer, CsvWriteOptions? options = null, bool ownsWriter = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _out = writer;
        _ownsWriter = ownsWriter;
        _options = options ?? new CsvWriteOptions();
        _delimiter = _options.Delimiter;
        _quote = _options.Quote;
        if (_delimiter == _quote || _delimiter is '\r' or '\n' || _quote is '\r' or '\n')
        {
            throw new ArgumentException("The delimiter and the quote must differ from each other and from the line break characters.", nameof(options));
        }

        _special = System.Buffers.SearchValues.Create([_delimiter, _quote, '\r', '\n']);
    }

    private static StreamWriter MakeWriter(Stream stream, CsvWriteOptions options, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Encoding e = options.Encoding;
        if (e is UTF8Encoding)
        {
            e = new UTF8Encoding(options.ByteOrderMark);
        }
        else if (!options.ByteOrderMark && e is UnicodeEncoding)
        {
            e = new UnicodeEncoding(e.CodePage == 1201, false);
        }

        return new StreamWriter(stream, e, 1 << 15, leaveOpen);
    }

    /// <summary>Gets the number of fields written in the current record.</summary>
    public int FieldsInRecord => _fieldsInRecord;

    /// <summary>Writes one field.</summary>
    /// <param name="text">The field's text. Empty text writes an empty field.</param>
    /// <param name="isText">True (the default) for text, which gets the formula-injection treatment of <see cref="CsvWriteOptions.FormulaInjection"/>; false for the text of a number, date or boolean.</param>
    /// <exception cref="ObjectDisposedException">The writer is disposed.</exception>
    public void WriteField(ReadOnlySpan<char> text, bool isText = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fieldsInRecord > 0)
        {
            _out.Write(_delimiter);
        }

        _fieldsInRecord++;
        if (text.Length == 0)
        {
            return;
        }

        bool escape = isText && _options.FormulaInjection == CsvFormulaInjection.Escape && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
        bool quote = escape || _options.Quoting == CsvQuoting.Always || text.ContainsAny(_special);
        if (!quote)
        {
            _out.Write(text);
            return;
        }

        _out.Write(_quote);
        if (escape)
        {
            _out.Write('\'');
        }

        while (text.Length > 0)
        {
            int q = text.IndexOf(_quote);
            if (q < 0)
            {
                _out.Write(text);
                break;
            }

            _out.Write(text[..(q + 1)]);
            _out.Write(_quote);
            text = text[(q + 1)..];
        }

        _out.Write(_quote);
    }

    /// <summary>Writes a number in the shortest form that reads back as the same value, with a dot as the decimal separator.</summary>
    /// <param name="value">The number.</param>
    public void WriteNumber(double value)
    {
        Span<char> buffer = stackalloc char[32]; // the longest round-trip double is 24 characters
        if (!value.TryFormat(buffer, out int n, "R", System.Globalization.CultureInfo.InvariantCulture))
        {
            n = 0;
        }

        WriteField(buffer[..n], isText: false);
    }

    /// <summary>Ends the record.</summary>
    public void EndRecord()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _out.Write(_options.LineEnding);
        _fieldsInRecord = 0;
    }

    /// <summary>Writes whatever is buffered to the destination.</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _out.Flush();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsWriter)
        {
            _out.Dispose();
        }
        else
        {
            _out.Flush();
        }
    }
}
