using System.Buffers;
using System.Text;
using System.Text.Unicode;
using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Spreadsheets.Csv;

/// <summary>
/// A forward-only reader of delimited text (CSV, TSV, semicolon- or pipe-separated). It holds one record at a time: after
/// <see cref="ReadRecord"/> the fields are spans over an internal buffer, already unquoted, so reading allocates nothing per field
/// once the buffers have grown to the largest record. Memory is the largest record, not the file.
/// </summary>
/// <remarks>
/// <para>
/// Rules (RFC 4180 §2 as the baseline, extended the way Excel reads): records end at CRLF, LF or a lone CR; a field that starts with the
/// quote character is quoted and may hold delimiters, line breaks and doubled quotes; a quote inside an unquoted field is ordinary text;
/// text between a closing quote and the next delimiter is kept after the quoted text (<c>"ab"cd</c> is <c>abcd</c>) and reported as
/// <c>csv.stray-quote</c>; a quote left open at the end of the input ends there (<c>csv.quote-unterminated</c>). A record with a different
/// number of fields than the first is returned as it is, never padded or cut, and reported as <c>csv.ragged-record</c>. An empty line is a
/// record with one empty field unless <see cref="CsvReadOptions.SkipBlankLines"/> is set. A final line break does not start another record.
/// </para>
/// <para>Defects locate by character offset in the decoded text (<see cref="DefectLocation.AtText(long)"/>). Not safe for concurrent use.</para>
/// </remarks>
public sealed class CsvReader : IDisposable
{
    private const int CharBufferSize = 1 << 16;
    private const int ByteBufferSize = 1 << 16;
    private const int DetectSize = CharBufferSize / 2;

    private enum Decoding : byte { Utf8, Utf16Le, Utf16Be, Windows1252, Custom }

    private readonly Stream? _stream;
    private readonly TextReader? _text;
    private readonly bool _leaveOpen;
    private readonly OpenContext _context;
    private readonly CsvReadOptions _options;
    private readonly char _quote;
    private readonly long _maxRecordChars;

    private byte[]? _bytes;
    private int _bPos;
    private int _bEnd;
    private bool _bEof;
    private Decoding _decoding;
    private Decoder? _decoder;
    private bool _reportedInvalid;

    private char[] _buf = new char[CharBufferSize];
    private int _pos;
    private int _end;
    private bool _eof;
    private long _base;

    private char _delimiter;
    private bool _started;
    private bool _disposed;

    private char[] _rec = new char[256];
    private int _recLen;
    private int[] _ends = new int[16];
    private bool[] _quoted = new bool[16];
    private int _fields;
    private int _expected = -1;
    private long _recordNumber;
    private long _recordStart;

    /// <summary>Reads delimited text from bytes.</summary>
    /// <param name="stream">The source; read forward once.</param>
    /// <param name="options">Delimiter, quote and encoding; null for the defaults.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the reader is disposed.</param>
    public CsvReader(Stream stream, CsvReadOptions? options = null, OpenContext? context = null, bool leaveOpen = true)
        : this(options, context)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    /// <summary>Reads delimited text that is already decoded.</summary>
    /// <param name="text">The source; read forward once. <see cref="CsvReadOptions.Encoding"/> is ignored.</param>
    /// <param name="options">Delimiter and quote; null for the defaults.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the reader open when this reader is disposed.</param>
    public CsvReader(TextReader text, CsvReadOptions? options = null, OpenContext? context = null, bool leaveOpen = true)
        : this(options, context)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _leaveOpen = leaveOpen;
    }

    private CsvReader(CsvReadOptions? options, OpenContext? context)
    {
        _options = options ?? new CsvReadOptions();
        _context = context ?? new OpenContext();
        _quote = _options.Quote;
        _maxRecordChars = Math.Min(_context.Limits.MaxAllocationBytes / sizeof(char), int.MaxValue / 4);
        _delimiter = _options.Delimiter ?? ',';
    }

    /// <summary>Gets the delimiter in use. Before the first <see cref="ReadRecord"/> it is the configured one, or a comma when detection is pending.</summary>
    public char Delimiter => _delimiter;

    /// <summary>Gets the number of fields in the current record.</summary>
    public int FieldCount => _fields;

    /// <summary>Gets the 1-based number of the current record, counting from the first record read; 0 before the first.</summary>
    public long RecordNumber => _recordNumber;

    /// <summary>Gets the character offset in the decoded text where the current record starts.</summary>
    public long RecordOffset => _recordStart;

    /// <summary>Gets a value indicating whether the current record is an empty line (one empty, unquoted field).</summary>
    public bool IsBlank => _fields == 1 && _ends[0] == 0 && !_quoted[0];

    /// <summary>Gets a field of the current record, quotes removed and doubled quotes collapsed.</summary>
    /// <param name="index">0 to <see cref="FieldCount"/> - 1.</param>
    /// <returns>A span over the reader's buffer; it is valid until the next <see cref="ReadRecord"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the record.</exception>
    public ReadOnlySpan<char> GetField(int index)
    {
        if ((uint)index >= (uint)_fields)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int start = index == 0 ? 0 : _ends[index - 1];
        return _rec.AsSpan(start, _ends[index] - start);
    }

    /// <summary>Gets whether a field of the current record was written in quotes.</summary>
    /// <param name="index">0 to <see cref="FieldCount"/> - 1.</param>
    /// <returns>True for a quoted field (even an empty one).</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the record.</exception>
    public bool IsQuoted(int index)
    {
        if ((uint)index >= (uint)_fields)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _quoted[index];
    }

    /// <summary>Reads the next record.</summary>
    /// <param name="cancellationToken">Checked once per record.</param>
    /// <returns>True when a record was read; false at the end of the input.</returns>
    /// <exception cref="OfficeLimitException">One record is larger than the allocation limit.</exception>
    /// <exception cref="IOException">The source cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public bool ReadRecord(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_started)
        {
            Start();
        }

        for (;;)
        {
            if (_pos == _end && !Fill())
            {
                _fields = 0;
                return false;
            }

            _recordStart = _base + _pos;
            ParseRecord();
            if (_options.SkipBlankLines && IsBlank)
            {
                continue;
            }

            _recordNumber++;
            if (_expected < 0)
            {
                _expected = _fields;
            }
            else if (_fields != _expected)
            {
                Report("csv.ragged-record", DefectKind.Structure, DefectSeverity.Repaired,
                    "The record has " + _fields.ToString(System.Globalization.CultureInfo.InvariantCulture) + " fields; the first record has "
                    + _expected.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". It is returned as it is.", _recordStart);
            }

            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_leaveOpen)
        {
            _stream?.Dispose();
            _text?.Dispose();
        }

        if (_bytes is not null)
        {
            ArrayPool<byte>.Shared.Return(_bytes);
            _bytes = null;
        }
    }

    private void Report(string code, DefectKind kind, DefectSeverity severity, string message, long offset) =>
        _context.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.AtText(offset)));

    // ---- start: encoding and delimiter ----

    private void Start()
    {
        _started = true;
        if (_stream is not null)
        {
            _bytes = ArrayPool<byte>.Shared.Rent(ByteBufferSize);
            ChooseEncoding();
        }

        if (_options.Delimiter is null)
        {
            while (!_eof && _end < DetectSize)
            {
                if (!FillMore())
                {
                    break;
                }
            }

            // Excel's "sep=;" first line names the delimiter and is not data.
            // The window is exactly DetectSize characters (or the whole input if shorter), however the bytes arrived.
            ReadOnlySpan<char> head = _buf.AsSpan(_pos, Math.Min(_end - _pos, DetectSize));
            if (head.Length >= 6 && head.StartsWith("sep=") && head[4] is not ('\r' or '\n' or '"') && head[5] is '\r' or '\n')
            {
                _delimiter = head[4];
                _pos += head[5] == '\r' && head.Length > 6 && head[6] == '\n' ? 7 : 6;
            }
            else
            {
                _delimiter = DetectDelimiter(head, _quote);
            }
        }
    }

    private void ChooseEncoding()
    {
        // Fill the whole first block, however the stream hands out bytes, so the choice does not depend on read sizes.
        FillBytes();
        while (!_bEof && _bEnd < _bytes!.Length)
        {
            int n = _stream!.Read(_bytes, _bEnd, _bytes.Length - _bEnd);
            if (n <= 0)
            {
                _bEof = true;
            }
            else
            {
                _bEnd += n;
            }
        }

        ReadOnlySpan<byte> b = _bytes.AsSpan(_bPos, _bEnd - _bPos);
        if (b.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            _decoding = Decoding.Utf8;
            _bPos += 3;
        }
        else if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            _decoding = Decoding.Utf16Le;
            _bPos += 2;
        }
        else if (b.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            _decoding = Decoding.Utf16Be;
            _bPos += 2;
        }
        else if (_options.Encoding is { } e)
        {
            switch (e.CodePage)
            {
                case 65001:
                    _decoding = Decoding.Utf8;
                    break;
                case 1200:
                    _decoding = Decoding.Utf16Le;
                    break;
                case 1201:
                    _decoding = Decoding.Utf16Be;
                    break;
                default:
                    _decoding = Decoding.Custom;
                    _decoder = e.GetDecoder();
                    break;
            }
        }
        else
        {
            // A seekable stream is checked whole, so a file that turns out to be Windows-1252 late in the text is not read as UTF-8.
            // Otherwise the first block decides; a prefix that ends inside a multi-byte sequence is still UTF-8.
            bool valid = _stream!.CanSeek ? ScanUtf8(_stream, _bPos, _bEnd) : Utf8.IsValid(b);
            for (int cut = 1; !_stream.CanSeek && !valid && cut <= 3 && !_bEof && cut < b.Length; cut++)
            {
                valid = Utf8.IsValid(b[..^cut]);
            }

            if (valid)
            {
                _decoding = Decoding.Utf8;
            }
            else
            {
                _decoding = Decoding.Windows1252;
                Report("csv.encoding-assumed", DefectKind.Encoding, DefectSeverity.Repaired,
                    "The bytes are not valid UTF-8 and have no byte-order mark; they are read as Windows-1252.", 0);
            }
        }
    }

    // Reads the rest of a seekable stream (after the bytes already buffered) and says whether all of it is valid UTF-8.
    private static bool ScanUtf8(Stream stream, int bufferedStart, int bufferedEnd)
    {
        long start = stream.Position;
        byte[] block = ArrayPool<byte>.Shared.Rent(ByteBufferSize + 4);
        try
        {
            // The stream is already past the buffered bytes; rewind to the start of the file's content and walk it in blocks, carrying an
            // unfinished multi-byte sequence into the next block.
            stream.Position = start - (bufferedEnd - bufferedStart);
            int carry = 0;
            for (;;)
            {
                int n = stream.Read(block, carry, ByteBufferSize);
                int len = carry + n;
                if (n == 0)
                {
                    return Utf8.IsValid(block.AsSpan(0, len));
                }

                // Cut back to a sequence boundary (at most 3 bytes) before testing.
                int cut = len;
                int back = 0;
                while (back < 3 && cut > 0 && (block[cut - 1] & 0xC0) == 0x80)
                {
                    cut--;
                    back++;
                }

                if (cut > 0 && block[cut - 1] >= 0xC0)
                {
                    cut--;
                }

                if (!Utf8.IsValid(block.AsSpan(0, cut)))
                {
                    return false;
                }

                carry = len - cut;
                Buffer.BlockCopy(block, cut, block, 0, carry);
            }
        }
        finally
        {
            stream.Position = start;
            ArrayPool<byte>.Shared.Return(block);
        }
    }

    /// <summary>Picks the delimiter that splits the leading records consistently (see <see cref="CsvReadOptions.Delimiter"/>).</summary>
    /// <param name="text">The leading text.</param>
    /// <param name="quote">The quote character.</param>
    /// <returns>The delimiter.</returns>
    internal static char DetectDelimiter(ReadOnlySpan<char> text, char quote)
    {
        // Four candidates, four counters each: fixed 16 ints and 4 bools on the stack, whatever the input.
        ReadOnlySpan<char> candidates = [',', ';', '\t', '|'];
        Span<int> first = stackalloc int[4];
        Span<int> total = stackalloc int[4];
        Span<int> cur = stackalloc int[4];
        Span<bool> consistent = stackalloc bool[4];
        consistent.Fill(true);
        int lines = 0;
        bool inQuotes = false;
        bool atFieldStart = true;
        bool hadChars = false;
        for (int i = 0; i <= text.Length && lines < 20; i++)
        {
            char c = i < text.Length ? text[i] : '\n';
            if (inQuotes)
            {
                if (c == quote)
                {
                    inQuotes = false;
                }

                continue;
            }

            if (c == quote && atFieldStart)
            {
                inQuotes = true;
                atFieldStart = false;
                hadChars = true;
                continue;
            }

            if (c is '\n' or '\r')
            {
                if (hadChars)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        total[k] += cur[k];
                        if (lines == 0)
                        {
                            first[k] = cur[k];
                        }
                        else if (cur[k] != first[k])
                        {
                            consistent[k] = false;
                        }

                        cur[k] = 0;
                    }

                    lines++;
                }

                atFieldStart = true;
                hadChars = false;
                continue;
            }

            hadChars = true;
            int idx = candidates.IndexOf(c);
            if (idx >= 0)
            {
                cur[idx]++;
                atFieldStart = true;
            }
            else
            {
                atFieldStart = false;
            }
        }

        if (lines > 0)
        {
            for (int k = 0; k < 4; k++)
            {
                if (consistent[k] && first[k] > 0)
                {
                    return candidates[k];
                }
            }
        }

        int best = 0;
        for (int k = 1; k < 4; k++)
        {
            if (total[k] > total[best])
            {
                best = k;
            }
        }

        return total[best] > 0 ? candidates[best] : ',';
    }

    // ---- buffering ----

    // Moves the unread characters to the front and reads more. Returns true when at least one unread character exists afterwards.
    private bool Fill()
    {
        if (_pos < _end)
        {
            return true;
        }

        return FillMore();
    }

    private bool FillMore()
    {
        if (_eof)
        {
            return _pos < _end;
        }

        if (_pos > 0)
        {
            Array.Copy(_buf, _pos, _buf, 0, _end - _pos);
            _base += _pos;
            _end -= _pos;
            _pos = 0;
        }

        while (_end < _buf.Length - 8 && !_eof)
        {
            int n = _stream is not null ? DecodeInto(_buf.AsSpan(_end)) : _text!.Read(_buf, _end, _buf.Length - _end);
            if (n <= 0)
            {
                _eof = true;
                break;
            }

            _end += n;
            if (_text is not null || _end > _buf.Length / 2)
            {
                break;
            }
        }

        return _pos < _end;
    }

    private void FillBytes()
    {
        if (_bEof)
        {
            return;
        }

        if (_bPos > 0)
        {
            Buffer.BlockCopy(_bytes!, _bPos, _bytes!, 0, _bEnd - _bPos);
            _bEnd -= _bPos;
            _bPos = 0;
        }

        while (_bEnd < _bytes!.Length)
        {
            int n = _stream!.Read(_bytes, _bEnd, _bytes.Length - _bEnd);
            if (n <= 0)
            {
                _bEof = true;
                break;
            }

            _bEnd += n;
            if (_bEnd >= 4)
            {
                break;
            }
        }
    }

    // Decodes bytes into dst; returns 0 only at the end of the input.
    private int DecodeInto(Span<char> dst)
    {
        for (;;)
        {
            if (_bEnd - _bPos < 8 && !_bEof)
            {
                FillBytes();
            }

            ReadOnlySpan<byte> src = _bytes.AsSpan(_bPos, _bEnd - _bPos);
            int read = 0;
            int written = 0;
            switch (_decoding)
            {
                case Decoding.Utf8:
                    Utf8.ToUtf16(src, dst, out read, out written, replaceInvalidSequences: true, isFinalBlock: _bEof);
                    if (written > 0 && !_reportedInvalid && dst[..written].Contains('\uFFFD'))
                    {
                        _reportedInvalid = true;
                        Report("csv.encoding-invalid", DefectKind.Encoding, DefectSeverity.Repaired,
                            "The bytes hold a sequence that is not valid UTF-8 (or a literal U+FFFD); it reads as U+FFFD.", _base + _end);
                    }

                    break;
                case Decoding.Utf16Le:
                case Decoding.Utf16Be:
                    {
                        int units = src.Length / 2;
                        bool le = _decoding == Decoding.Utf16Le;
                        int i = 0;
                        bool replaced = false;
                        while (i < units && written + 2 <= dst.Length)
                        {
                            char ch = le ? (char)(src[2 * i] | (src[2 * i + 1] << 8)) : (char)((src[2 * i] << 8) | src[2 * i + 1]);
                            if (char.IsHighSurrogate(ch))
                            {
                                if (i + 1 >= units && !_bEof)
                                {
                                    break; // the low half may be in the next block
                                }

                                char next = i + 1 < units ? (le ? (char)(src[2 * i + 2] | (src[2 * i + 3] << 8)) : (char)((src[2 * i + 2] << 8) | src[2 * i + 3])) : '\0';
                                if (char.IsLowSurrogate(next))
                                {
                                    dst[written++] = ch;
                                    dst[written++] = next;
                                    i += 2;
                                    continue;
                                }

                                dst[written++] = '\uFFFD';
                                replaced = true;
                            }
                            else if (char.IsLowSurrogate(ch))
                            {
                                dst[written++] = '\uFFFD';
                                replaced = true;
                            }
                            else
                            {
                                dst[written++] = ch;
                            }

                            i++;
                        }

                        read = i * 2;
                        if (replaced && !_reportedInvalid)
                        {
                            _reportedInvalid = true;
                            Report("csv.encoding-invalid", DefectKind.Encoding, DefectSeverity.Repaired,
                                "The UTF-16 text holds an unpaired surrogate; it reads as U+FFFD.", _base + _end);
                        }

                        if (_bEof && src.Length - read == 1)
                        {
                            read = src.Length;
                            Report("csv.encoding-invalid", DefectKind.Encoding, DefectSeverity.Repaired,
                                "The UTF-16 text ends in half a code unit; the byte is dropped.", _base + _end + written);
                        }

                        break;
                    }

                case Decoding.Windows1252:
                    {
                        int n = Math.Min(src.Length, dst.Length);
                        for (int i = 0; i < n; i++)
                        {
                            byte x = src[i];
                            dst[i] = x is >= 0x80 and < 0xA0 ? Cp1252High[x - 0x80] : (char)x;
                        }

                        read = n;
                        written = n;
                        break;
                    }

                default:
                    _decoder!.Convert(src, dst, _bEof, out read, out written, out _);
                    break;
            }

            _bPos += read;
            if (written > 0)
            {
                return written;
            }

            if (_bEof && _bPos >= _bEnd)
            {
                return 0;
            }

            if (read == 0 && _bEof)
            {
                return 0;
            }

            if (!_bEof)
            {
                FillBytes();
            }
        }
    }

    // Windows-1252 0x80-0x9F; the five undefined positions map to the C1 control of the same value, as the platform's best-fit does.
    private static readonly char[] Cp1252High =
    [
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021', '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014', '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    ];

    // ---- parsing ----

    private void Append(ReadOnlySpan<char> chunk)
    {
        if (chunk.Length == 0)
        {
            return;
        }

        long need = (long)_recLen + chunk.Length;
        if (need > _rec.Length)
        {
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _context.Limits.MaxAllocationBytes, need * sizeof(char),
                DefectLocation.AtText(_recordStart));
            Array.Resize(ref _rec, (int)Math.Min(Math.Max(need, (long)_rec.Length * 2), _maxRecordChars + 1));
        }

        chunk.CopyTo(_rec.AsSpan(_recLen));
        _recLen += chunk.Length;
    }

    private void EndField(bool quoted)
    {
        if (_fields == _ends.Length)
        {
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _context.Limits.MaxAllocationBytes,
                (long)_fields * 2 * (sizeof(int) + 1), DefectLocation.AtText(_recordStart));
            Array.Resize(ref _ends, _fields * 2);
            Array.Resize(ref _quoted, _fields * 2);
        }

        _ends[_fields] = _recLen;
        _quoted[_fields] = quoted;
        _fields++;
    }

    private void ParseRecord()
    {
        _recLen = 0;
        _fields = 0;
        for (;;)
        {
            // Start of a field.
            bool quoted = false;
            bool more = Fill();
            if (more && _buf[_pos] == _quote)
            {
                quoted = true;
                _pos++;
                ReadQuoted();
            }

            ReadUnquotedTail(quoted);
            EndField(quoted);
            // Now at a delimiter, a line break or the end of input.
            if (_pos == _end)
            {
                return; // end of input after a field
            }

            char c = _buf[_pos];
            if (c == _delimiter)
            {
                _pos++;
                continue; // a delimiter at the very end of the input yields one more empty field on the next turn
            }

            // CR, LF or CRLF.
            _pos++;
            if (c == '\r' && Fill() && _buf[_pos] == '\n')
            {
                _pos++;
            }

            return;
        }
    }

    // Inside a quoted field: copies up to the closing quote. Leaves _pos after the closing quote (or at the end of input).
    private void ReadQuoted()
    {
        for (;;)
        {
            if (_pos == _end && !Fill())
            {
                Report("csv.quote-unterminated", DefectKind.Markup, DefectSeverity.Repaired,
                    "A quoted field is not closed before the end of the input; it ends there.", _recordStart);
                return;
            }

            int q = _buf.AsSpan(_pos, _end - _pos).IndexOf(_quote);
            if (q < 0)
            {
                Append(_buf.AsSpan(_pos, _end - _pos));
                _pos = _end;
                continue;
            }

            Append(_buf.AsSpan(_pos, q));
            _pos += q + 1;
            if (_pos == _end && !Fill())
            {
                return; // closing quote at the end of the input
            }

            if (_buf[_pos] == _quote)
            {
                Append(_buf.AsSpan(_pos, 1));
                _pos++;
                continue;
            }

            return;
        }
    }

    // Copies the text up to the next delimiter or line break. After a quoted part, anything here is stray.
    private void ReadUnquotedTail(bool afterQuoted)
    {
        bool reported = false;
        for (;;)
        {
            if (_pos == _end && !Fill())
            {
                return;
            }

            ReadOnlySpan<char> window = _buf.AsSpan(_pos, _end - _pos);
            int n = window.IndexOfAny(_delimiter, '\n', '\r');
            if (n < 0)
            {
                n = window.Length;
            }

            if (n > 0)
            {
                if (afterQuoted && !reported)
                {
                    reported = true;
                    Report("csv.stray-quote", DefectKind.Markup, DefectSeverity.Repaired,
                        "Text follows the closing quote of a quoted field; it is kept after the quoted text.", _base + _pos);
                }

                Append(window[..n]);
                _pos += n;
            }

            if (_pos < _end)
            {
                return;
            }
        }
    }
}
