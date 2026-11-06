using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets.Csv;
using RecursiveAI.OneDocument.Spreadsheets.Streaming;

namespace RecursiveAI.OneDocument.Spreadsheets.Binding;

/// <summary>The machinery behind the <see cref="Spreadsheet"/> statics: opening a source by its content, and pumping rows through generated code.</summary>
internal static class RowPipeline
{
    // ------------------------------------------------------------------ reading

    public static IEnumerable<T> ReadFile<T>(string path, OpenContext? context, CancellationToken cancellationToken)
        where T : ISpreadsheetRow<T>
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        foreach (T row in ReadStream<T>(file, Path.GetExtension(path), context, cancellationToken))
        {
            yield return row;
        }
    }

    public static IEnumerable<T> ReadStream<T>(Stream stream, string? extension, OpenContext? context, CancellationToken cancellationToken)
        where T : ISpreadsheetRow<T>
    {
        RowSchema schema = T.Schema;
        OpenContext ctx = context ?? new OpenContext();
        using RowSource source = OpenSource(stream, extension, schema, ctx, cancellationToken);
        var reader = new RowReader(schema, ctx, source.SheetName, source.DateSystem);
        source.Bind(reader);
        while (source.Next(reader))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return T.Read(reader);
        }
    }

    public static async IAsyncEnumerable<T> ReadStreamAsync<T>(Stream stream, string? extension, OpenContext? context, [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ISpreadsheetRow<T>
    {
        // The cursors read synchronously from a seekable source; the loop gives the caller's thread back every so often.
        int count = 0;
        foreach (T row in ReadStream<T>(stream, extension, context, cancellationToken))
        {
            yield return row;
            if ((++count & 1023) == 0)
            {
                await Task.Yield();
            }
        }
    }

    public static async IAsyncEnumerable<T> ReadFileAsync<T>(string path, OpenContext? context, [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ISpreadsheetRow<T>
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
        await foreach (T row in ReadStreamAsync<T>(file, Path.GetExtension(path), context, cancellationToken).ConfigureAwait(false))
        {
            yield return row;
        }
    }

    private static RowSource OpenSource(Stream stream, string? extension, RowSchema schema, OpenContext context, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        }

        long start = stream.Position;
        OfficeFormat format;
        using (IByteSource bytes = ByteSource.FromStream(stream, leaveOpen: true))
        {
            format = FormatSniffer.Detect(bytes, context.Limits);
        }

        stream.Position = start;
        if (format == OfficeFormat.Unknown && extension is not null
            && (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            format = OfficeFormat.Csv;
        }
        else if (format == OfficeFormat.Unknown && extension is not null && extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase))
        {
            format = OfficeFormat.Tsv;
        }

        switch (format)
        {
            case OfficeFormat.Xlsx:
            case OfficeFormat.Xlsm:
            case OfficeFormat.Xltx:
            case OfficeFormat.Xltm:
            case OfficeFormat.Xlam:
            case OfficeFormat.FlatOpcWorkbook:
                return CursorRowSource.Xlsx(stream, schema, context, cancellationToken);
            case OfficeFormat.Xlsb:
                return CursorRowSource.Xlsb(stream, schema, context, cancellationToken);
            case OfficeFormat.Xls:
            case OfficeFormat.XlsLegacy:
                return CursorRowSource.Xls(stream, schema, context, cancellationToken);
            case OfficeFormat.Csv:
            case OfficeFormat.Tsv:
                return new CsvRowSource(stream, format == OfficeFormat.Tsv, schema, context, cancellationToken);
            case OfficeFormat.Unknown:
                throw new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a spreadsheet or delimited text.");
            default:
                throw new FormatNotAvailableException(format, "read rows from");
        }
    }

    public static IEnumerable<object?[]> ReadRows(Stream stream, string? extension, string? sheet, OpenContext? context, CancellationToken cancellationToken)
    {
        // A schema-less read: the first visible worksheet (or the named one), every row as it is stored.
        RowSchema none = new("rows", sheet, strict: false, hasHeader: false, []);
        OpenContext ctx = context ?? new OpenContext();
        long start = stream.Position;
        OfficeFormat format;
        using (IByteSource bytes = ByteSource.FromStream(stream, leaveOpen: true))
        {
            format = FormatSniffer.Detect(bytes, ctx.Limits);
        }

        stream.Position = start;
        if (format is OfficeFormat.Csv or OfficeFormat.Tsv
            || (format == OfficeFormat.Unknown && extension is not null && (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase) || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))))
        {
            bool tab = format == OfficeFormat.Tsv || (extension is not null && extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase));
            using var csv = new CsvReader(stream, new CsvReadOptions { Delimiter = tab ? '\t' : null }, ctx, leaveOpen: true);
            while (csv.ReadRecord(cancellationToken))
            {
                var row = new object?[csv.FieldCount];
                for (int f = 0; f < row.Length; f++)
                {
                    ReadOnlySpan<char> field = csv.GetField(f);
                    row[f] = field.IsEmpty && !csv.IsQuoted(f) ? null : new string(field);
                }

                yield return row;
            }

            yield break;
        }

        IDisposable book;
        ICellCursor cursor;
        switch (format)
        {
            case OfficeFormat.Xlsx or OfficeFormat.Xlsm or OfficeFormat.Xltx or OfficeFormat.Xltm or OfficeFormat.Xlam or OfficeFormat.FlatOpcWorkbook:
            {
                XlsxStreamReader xlsx = XlsxStreamReader.Open(stream, ctx, leaveOpen: true, cancellationToken);
                book = xlsx;
                cursor = xlsx.OpenSheet(CursorRowSource.FindSheet(xlsx.Sheets, none.Sheet));
                break;
            }

            case OfficeFormat.Xlsb:
            {
                XlsbStreamReader xlsb = XlsbStreamReader.Open(stream, ctx, leaveOpen: true, cancellationToken);
                book = xlsb;
                cursor = xlsb.OpenSheet(CursorRowSource.FindSheet(xlsb.Sheets, none.Sheet));
                break;
            }

            case OfficeFormat.Xls:
            case OfficeFormat.XlsLegacy:
            {
                XlsStreamReader xls = XlsStreamReader.Open(stream, ctx, leaveOpen: true, cancellationToken);
                book = xls;
                cursor = xls.OpenSheet(CursorRowSource.FindSheet(xls.Sheets, none.Sheet));
                break;
            }

            default:
                throw format == OfficeFormat.Unknown
                    ? new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a spreadsheet or delimited text.")
                    : new FormatNotAvailableException(format, "read rows from");
        }

        using (book)
        using (cursor)
        {
            var cells = new List<object?>();
            while (cursor.ReadRow())
            {
                cells.Clear();
                while (cursor.ReadCell())
                {
                    while (cells.Count < cursor.Column - 1)
                    {
                        cells.Add(null);
                    }

                    cells.Add(cursor.Kind switch
                    {
                        CellValueKind.Number => cursor.IsDate && cursor.TryGetDateTime(out DateTime d) ? d : cursor.Number,
                        CellValueKind.Text => cursor.GetText(),
                        CellValueKind.Boolean => cursor.Boolean,
                        CellValueKind.Error => cursor.Error,
                        _ => null,
                    });
                }

                yield return [.. cells];
            }
        }
    }

    // ------------------------------------------------------------------ writing

    public static OfficeFormat FormatOf(string path)
    {
        string ext = Path.GetExtension(path);
        if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Xlsx;
        }

        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Csv;
        }

        if (ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Tsv;
        }

        throw new OfficeUnsupportedException("writing rows to a '" + ext + "' file (use .xlsx, .csv or .tsv)");
    }

    public static void WriteFile<T>(string path, IEnumerable<T> rows, CancellationToken cancellationToken)
        where T : ISpreadsheetRow<T>
    {
        OfficeFormat format = FormatOf(path);
        AtomicFile.Write(path, stream => WriteStream(stream, rows, format, cancellationToken));
    }

    public static async Task WriteFileAsync<T>(string path, IAsyncEnumerable<T> rows, CancellationToken cancellationToken)
        where T : ISpreadsheetRow<T>
    {
        OfficeFormat format = FormatOf(path);
        // The rows are awaited, the file is written synchronously: collect nothing, hold the stream open while the sequence is read.
        await AtomicFile.WriteAsync(path, async (stream, token) => await WriteStreamAsync(stream, rows, format, token).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    public static void WriteStream<T>(Stream stream, IEnumerable<T> rows, OfficeFormat format, CancellationToken cancellationToken)
        where T : ISpreadsheetRow<T>
    {
        using var pump = new RowPump<T>(stream, format, cancellationToken);
        foreach (T row in rows)
        {
            pump.Add(in row);
        }

        pump.Finish();
    }

    public static async Task WriteStreamAsync<T>(Stream stream, IAsyncEnumerable<T> rows, OfficeFormat format, CancellationToken cancellationToken)
        where T : ISpreadsheetRow<T>
    {
        using var pump = new RowPump<T>(stream, format, cancellationToken);
        await foreach (T row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            pump.Add(in row);
        }

        pump.Finish();
    }

    /// <summary>Owns the writer, sink and row counter for one write.</summary>
    private sealed class RowPump<T> : IDisposable
        where T : ISpreadsheetRow<T>
    {
        private readonly CancellationToken _cancellation;
        private readonly RowSchema _schema = T.Schema;
        private readonly XlsxStreamWriter? _book;
        private readonly CsvWriter? _csv;
        private readonly RowWriter _writer;
        private int _row;

        public RowPump(Stream stream, OfficeFormat format, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            _cancellation = cancellationToken;
            RowSink sink;
            switch (format)
            {
                case OfficeFormat.Xlsx:
                    _book = XlsxStreamWriter.Create(stream, null, leaveOpen: true, cancellationToken);
                    sink = new XlsxRowSink(_book, _schema);
                    break;
                case OfficeFormat.Csv:
                case OfficeFormat.Tsv:
                    _csv = new CsvWriter(stream, new CsvWriteOptions { Delimiter = format == OfficeFormat.Tsv ? '\t' : ',' }, leaveOpen: true);
                    sink = new CsvRowSink(_csv);
                    break;
                default:
                    throw new FormatNotAvailableException(format, "write rows as");
            }

            _writer = new RowWriter(_schema, sink, _schema.Sheet ?? "Sheet1");
            if (_schema.HasHeader)
            {
                _row = 1;
                _writer.RowNumber = 1;
                _writer.WriteHeader();
            }
        }

        public void Add(in T row)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (row is null)
            {
                throw new ArgumentException("The sequence of rows holds a null.");
            }

            _row++;
            if (_row > CellAddress.MaxRows)
            {
                throw new OfficeLimitException("MaxRows", CellAddress.MaxRows, _row);
            }

            _writer.RowNumber = _row;
            _writer.BeginRow();
            T.Write(in row, _writer);
            _writer.EndRow();
        }

        public void Finish()
        {
            _book?.Finish();
            _csv?.Flush();
        }

        public void Dispose()
        {
            _book?.Dispose();
            _csv?.Dispose();
        }
    }
}
