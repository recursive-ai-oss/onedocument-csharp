using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// Reads the records of a BIFF stream one at a time from a stream, through a buffer, without cutting the whole stream into a list first
/// ([MS-XLS] §2.1.4 Record: a 2-byte type, a 2-byte size, then the data). The data of the current record stays valid until the next
/// <see cref="Read"/>, also across <see cref="PeekType"/>.
/// </summary>
internal sealed class BiffRecordStream
{
    private const int ContinueType = 0x003C;
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[1 << 17];
    private int _start;
    private int _end;
    private int _keepFrom;
    private int _dataAt;
    private long _bufferOffset;

    public BiffRecordStream(Stream stream)
    {
        _stream = stream;
        _bufferOffset = stream.CanSeek ? stream.Position : 0;
    }

    /// <summary>The type of the current record.</summary>
    public int Type { get; private set; }

    /// <summary>The length of the current record's data.</summary>
    public int Length { get; private set; }

    /// <summary>The offset of the current record's header in the stream.</summary>
    public long Offset { get; private set; }

    /// <summary>True when the stream ended inside a record (the record is not delivered).</summary>
    public bool Truncated { get; private set; }

    /// <summary>The data of the current record.</summary>
    public ReadOnlySpan<byte> Data => _buffer.AsSpan(_dataAt, Length);

    /// <summary>The offset of the next record's header.</summary>
    public long Position => _bufferOffset + _start;

    /// <summary>Moves to the next record.</summary>
    /// <returns>False at the end of the stream, or when the stream ends inside a record (<see cref="Truncated"/>).</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool Read()
    {
        _keepFrom = _start;
        if (!Ensure(4))
        {
            Truncated = _end - _start > 0;
            return false;
        }

        int type = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_start));
        int length = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_start + 2));
        if (!Ensure(4 + length))
        {
            Truncated = true;
            return false;
        }

        Type = type;
        Length = length;
        Offset = _bufferOffset + _start;
        _dataAt = _start + 4;
        _start += 4 + length;
        return true;
    }

    /// <summary>Gets the type of the record after the current one without moving; -1 at the end.</summary>
    public int PeekType() => Ensure(4) ? BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_start)) : -1;

    /// <summary>Tells whether a CONTINUE record follows.</summary>
    public bool NextIsContinue() => PeekType() == ContinueType;

    /// <summary>Moves so that the next <see cref="Read"/> returns the record whose header is at <paramref name="offset"/>.</summary>
    public void Seek(long offset)
    {
        Truncated = false;
        if (offset >= _bufferOffset && offset <= _bufferOffset + _end)
        {
            _start = (int)(offset - _bufferOffset);
            _keepFrom = _start;
            return;
        }

        _stream.Position = offset;
        _bufferOffset = offset;
        _start = 0;
        _end = 0;
        _keepFrom = 0;
    }

    private bool Ensure(int n)
    {
        if (_end - _start >= n)
        {
            return true;
        }

        if (_keepFrom > 0)
        {
            // Drop what was read before the current record; its own bytes stay where Data can find them.
            Buffer.BlockCopy(_buffer, _keepFrom, _buffer, 0, _end - _keepFrom);
            _bufferOffset += _keepFrom;
            _end -= _keepFrom;
            _start -= _keepFrom;
            _dataAt -= _keepFrom;
            _keepFrom = 0;
        }

        while (_end - _start < n)
        {
            int read = _stream.Read(_buffer, _end, _buffer.Length - _end);
            if (read <= 0)
            {
                return false;
            }

            _end += read;
        }

        return true;
    }
}
