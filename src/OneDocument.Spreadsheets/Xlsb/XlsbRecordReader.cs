using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>
/// Reads the records of one binary workbook part in order ([MS-XLSB] §2.1.4): a one- or two-byte record type, a one- to four-byte
/// size, then that many bytes of data. The reader holds one record at a time, so a part of any size is read at constant memory
/// when it comes from a stream.
/// </summary>
/// <remarks>
/// A part that ends inside a record is not an exception: the cut record is reported as <c>xlsb.record-truncated</c> and the
/// read ends there, because Office opens such files with what came before. A record larger than
/// <see cref="OfficeLimits.MaxRecordSize"/> or more records than <see cref="OfficeLimits.MaxRecordCount"/> raise
/// <see cref="OfficeLimitException"/>. Not thread-safe.
/// </remarks>
public sealed class XlsbRecordReader
{
    private readonly Stream? _stream;
    private readonly ReadOnlyMemory<byte> _memory;
    private readonly OpenContext _context;
    private readonly string _partName;
    private byte[] _buffer = [];
    private int _bufferStart;
    private int _bufferEnd;
    private long _streamPosition;
    private int _memoryPosition;
    private ReadOnlyMemory<byte> _data;
    private byte[] _recordBuffer = [];
    private long _count;
    private bool _ended;

    /// <summary>Starts a reader over a part held in memory; records are slices of the memory, nothing is copied.</summary>
    /// <param name="part">The part's bytes.</param>
    /// <param name="context">Limits and the defect sink.</param>
    /// <param name="partName">The part name used in defect locations.</param>
    public XlsbRecordReader(ReadOnlyMemory<byte> part, OpenContext context, string partName)
    {
        ArgumentNullException.ThrowIfNull(context);
        _memory = part;
        _context = context;
        _partName = partName;
    }

    /// <summary>Starts a reader over a stream, which is read forward only in blocks of 64 KiB.</summary>
    /// <param name="stream">A readable stream positioned at the start of the part.</param>
    /// <param name="context">Limits and the defect sink.</param>
    /// <param name="partName">The part name used in defect locations.</param>
    public XlsbRecordReader(Stream stream, OpenContext context, string partName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(context);
        _stream = stream;
        _context = context;
        _partName = partName;
        _buffer = new byte[65536];
    }

    /// <summary>Gets the type of the current record (a value of <see cref="XlsbRecordType"/>), valid after <see cref="Read"/> returned true.</summary>
    public int Type { get; private set; }

    /// <summary>Gets the offset in the part of the first byte of the current record (its type).</summary>
    public long Offset { get; private set; }

    /// <summary>Gets the data of the current record; valid until the next <see cref="Read"/>.</summary>
    public ReadOnlySpan<byte> Data => _data.Span;

    /// <summary>Gets the data of the current record as memory, valid until the next <see cref="Read"/>.</summary>
    public ReadOnlyMemory<byte> DataMemory => _data;

    /// <summary>Gets the number of records read so far.</summary>
    public long RecordCount => _count;

    /// <summary>Gets the part name used in defect locations.</summary>
    public string PartName => _partName;

    /// <summary>Gets a value indicating whether the part ended inside a record.</summary>
    public bool WasTruncated { get; private set; }

    /// <summary>Reads the next record.</summary>
    /// <returns>True when a record is available; false at the end of the part, including a part cut inside a record.</returns>
    /// <exception cref="OfficeLimitException">The record is larger than <see cref="OfficeLimits.MaxRecordSize"/> or the part has more than <see cref="OfficeLimits.MaxRecordCount"/> records.</exception>
    public bool Read()
    {
        if (_ended)
        {
            return false;
        }

        long start = Position;
        int first = ReadByte();
        if (first < 0)
        {
            _ended = true;
            return false;
        }

        // [MS-XLSB] §2.1.4: the type is two bytes if and only if the high bit of the low byte is set; it is then the seven low bits of each.
        int type = first;
        if ((first & 0x80) != 0)
        {
            int second = ReadByte();
            if (second < 0)
            {
                return Truncate(start);
            }

            type = (first & 0x7F) | ((second & 0x7F) << 7);
        }

        // The size is one to four bytes, seven bits each, low bits first; the high bit of the fourth byte is ignored.
        int size = 0;
        for (int i = 0; i < 4; i++)
        {
            int b = ReadByte();
            if (b < 0)
            {
                return Truncate(start);
            }

            size |= (b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0 || i == 3)
            {
                break;
            }
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordSize), _context.Limits.MaxRecordSize, size, DefectLocation.InRecord(_partName, start));
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), _context.Limits.MaxRecordCount, _count + 1, DefectLocation.InRecord(_partName, start));
        if (!Fill(size))
        {
            return Truncate(start);
        }

        Type = type;
        Offset = start;
        _count++;
        return true;
    }

    private long Position => _stream is null ? _memoryPosition : _streamPosition - (_bufferEnd - _bufferStart);

    private bool Truncate(long start)
    {
        _ended = true;
        WasTruncated = true;
        _context.Defects.Report(new Defect(DefectKind.Record, "xlsb.record-truncated", DefectSeverity.Dropped,
            "The part ends inside a record; the records before it are used.", DefectLocation.InRecord(_partName, start)));
        return false;
    }

    private int ReadByte()
    {
        if (_stream is null)
        {
            return _memoryPosition < _memory.Length ? _memory.Span[_memoryPosition++] : -1;
        }

        if (_bufferStart == _bufferEnd && !Refill())
        {
            return -1;
        }

        return _buffer[_bufferStart++];
    }

    private bool Refill()
    {
        _bufferStart = 0;
        _bufferEnd = _stream!.Read(_buffer, 0, _buffer.Length);
        _streamPosition += _bufferEnd;
        return _bufferEnd > 0;
    }

    private bool Fill(int size)
    {
        if (_stream is null)
        {
            if (_memory.Length - _memoryPosition < size)
            {
                return false;
            }

            _data = _memory.Slice(_memoryPosition, size);
            _memoryPosition += size;
            return true;
        }

        if (_bufferEnd - _bufferStart >= size)
        {
            _data = new ReadOnlyMemory<byte>(_buffer, _bufferStart, size);
            _bufferStart += size;
            return true;
        }

        if (_recordBuffer.Length < size)
        {
            _recordBuffer = new byte[Math.Max(size, 1024)];
        }

        int have = _bufferEnd - _bufferStart;
        Buffer.BlockCopy(_buffer, _bufferStart, _recordBuffer, 0, have);
        _bufferStart = _bufferEnd;
        while (have < size)
        {
            int n = _stream.Read(_recordBuffer, have, size - have);
            if (n <= 0)
            {
                return false;
            }

            have += n;
            _streamPosition += n;
        }

        _data = new ReadOnlyMemory<byte>(_recordBuffer, 0, size);
        return true;
    }
}
