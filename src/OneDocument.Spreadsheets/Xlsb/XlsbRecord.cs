using System.Buffers.Binary;
using System.Text;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>
/// Writes records to a stream in the framing of [MS-XLSB] §2.1.4. Compose the data of a record in a <see cref="XlsbRecordData"/>
/// (or pass a span you already have) and call <see cref="Write(int, ReadOnlySpan{byte})"/>. Not thread-safe.
/// </summary>
public sealed class XlsbRecordWriter
{
    private readonly Stream _sink;
    private readonly byte[] _header = new byte[6];

    /// <summary>Starts a writer over a stream; the caller keeps ownership of the stream.</summary>
    /// <param name="sink">A writable stream.</param>
    public XlsbRecordWriter(Stream sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <summary>Gets the number of records written.</summary>
    public long RecordCount { get; private set; }

    /// <summary>Writes one record.</summary>
    /// <param name="type">The record type, 0 to 16383.</param>
    /// <param name="data">The record data, at most 2^28 - 1 bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">The type or the size does not fit the framing.</exception>
    public void Write(int type, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(type);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(type, 0x3FFF);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, 0x0FFFFFFF);
        int n = 0;
        if (type < 0x80)
        {
            _header[n++] = (byte)type;
        }
        else
        {
            _header[n++] = (byte)((type & 0x7F) | 0x80);
            _header[n++] = (byte)(type >> 7);
        }

        int size = data.Length;
        do
        {
            byte b = (byte)(size & 0x7F);
            size >>= 7;
            if (size != 0)
            {
                b |= 0x80;
            }

            _header[n++] = b;
        }
        while (size != 0);

        _sink.Write(_header, 0, n);
        _sink.Write(data);
        RecordCount++;
    }

    /// <summary>Writes one record with no data (the Begin/End markers).</summary>
    /// <param name="type">The record type.</param>
    public void Write(int type) => Write(type, ReadOnlySpan<byte>.Empty);

    /// <summary>Writes bytes that are already framed records (a block composed with another writer).</summary>
    /// <param name="framed">The framed records.</param>
    internal void WriteFramed(ReadOnlySpan<byte> framed) => _sink.Write(framed);

    /// <summary>Writes one record whose data was composed in <paramref name="data"/>, then clears it for reuse.</summary>
    /// <param name="type">The record type.</param>
    /// <param name="data">The composed data.</param>
    public void Write(int type, XlsbRecordData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Write(type, data.Span);
        data.Clear();
    }
}

/// <summary>A growable little-endian buffer in which the data of one record is composed before it is framed by <see cref="XlsbRecordWriter"/>.</summary>
public sealed class XlsbRecordData
{
    private byte[] _buffer = new byte[256];
    private int _length;

    /// <summary>Gets the composed bytes.</summary>
    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

    /// <summary>Gets the number of bytes composed so far.</summary>
    public int Length => _length;

    /// <summary>Empties the buffer.</summary>
    public void Clear() => _length = 0;

    private Span<byte> Take(int n)
    {
        if (_buffer.Length - _length < n)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + n));
        }

        Span<byte> s = _buffer.AsSpan(_length, n);
        _length += n;
        return s;
    }

    /// <summary>Appends a byte.</summary>
    /// <param name="value">The byte.</param>
    public void U8(int value) => Take(1)[0] = (byte)value;

    /// <summary>Appends a 16-bit unsigned integer.</summary>
    /// <param name="value">The value.</param>
    public void U16(int value) => BinaryPrimitives.WriteUInt16LittleEndian(Take(2), (ushort)value);

    /// <summary>Appends a 32-bit signed integer.</summary>
    /// <param name="value">The value.</param>
    public void I32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Take(4), value);

    /// <summary>Appends a 32-bit unsigned integer.</summary>
    /// <param name="value">The value.</param>
    public void U32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), value);

    /// <summary>Appends a 64-bit IEEE 754 number (Xnum, [MS-XLSB] §2.5.172).</summary>
    /// <param name="value">The value.</param>
    public void Number(double value) => BinaryPrimitives.WriteDoubleLittleEndian(Take(8), value);

    /// <summary>Appends raw bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    public void Bytes(ReadOnlySpan<byte> bytes) => bytes.CopyTo(Take(bytes.Length));

    /// <summary>Appends <paramref name="count"/> zero bytes.</summary>
    /// <param name="count">The count.</param>
    public void Zeros(int count) => Take(count).Clear();

    /// <summary>Appends an XLWideString ([MS-XLSB] §2.5.169): a 32-bit character count, then UTF-16.</summary>
    /// <param name="text">The text.</param>
    public void WideString(string text)
    {
        U32((uint)text.Length);
        Encoding.Unicode.GetBytes(text, Take(text.Length * 2));
    }

    /// <summary>Appends an XLNullableWideString ([MS-XLSB] §2.5.165): a null is a count of 0xFFFFFFFF.</summary>
    /// <param name="text">The text, or null.</param>
    public void NullableWideString(string? text)
    {
        if (text is null)
        {
            U32(0xFFFFFFFF);
        }
        else
        {
            WideString(text);
        }
    }
}

/// <summary>
/// A forward cursor over the data of one record. Reading past the end does not throw: it returns zero or empty and sets
/// <see cref="Overrun"/>, so a reader finishes the record, then reports one defect with the record's location.
/// </summary>
public ref struct XlsbRecordFields
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    /// <summary>Starts a cursor at the beginning of a record's data.</summary>
    /// <param name="data">The record data.</param>
    public XlsbRecordFields(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
        Overrun = false;
    }

    /// <summary>Gets a value indicating whether a read went past the end of the data.</summary>
    public bool Overrun { get; private set; }

    /// <summary>Gets the offset of the next byte to read.</summary>
    public readonly int Position => _position;

    /// <summary>Gets the number of bytes left.</summary>
    public readonly int Remaining => _data.Length - _position;

    /// <summary>Gets the whole data of the record.</summary>
    public readonly ReadOnlySpan<byte> Data => _data;

    private ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || _data.Length - _position < n)
        {
            Overrun = true;
            _position = _data.Length;
            return default;
        }

        ReadOnlySpan<byte> s = _data.Slice(_position, n);
        _position += n;
        return s;
    }

    /// <summary>Reads a byte.</summary>
    /// <returns>The byte, or 0 past the end.</returns>
    public int U8()
    {
        ReadOnlySpan<byte> s = Take(1);
        return s.IsEmpty ? 0 : s[0];
    }

    /// <summary>Reads a 16-bit unsigned integer.</summary>
    /// <returns>The value, or 0 past the end.</returns>
    public int U16()
    {
        ReadOnlySpan<byte> s = Take(2);
        return s.IsEmpty ? 0 : BinaryPrimitives.ReadUInt16LittleEndian(s);
    }

    /// <summary>Reads a 16-bit signed integer.</summary>
    /// <returns>The value, or 0 past the end.</returns>
    public int I16()
    {
        ReadOnlySpan<byte> s = Take(2);
        return s.IsEmpty ? 0 : BinaryPrimitives.ReadInt16LittleEndian(s);
    }

    /// <summary>Reads a 32-bit signed integer.</summary>
    /// <returns>The value, or 0 past the end.</returns>
    public int I32()
    {
        ReadOnlySpan<byte> s = Take(4);
        return s.IsEmpty ? 0 : BinaryPrimitives.ReadInt32LittleEndian(s);
    }

    /// <summary>Reads a 32-bit unsigned integer.</summary>
    /// <returns>The value, or 0 past the end.</returns>
    public uint U32()
    {
        ReadOnlySpan<byte> s = Take(4);
        return s.IsEmpty ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(s);
    }

    /// <summary>Reads a 64-bit IEEE 754 number.</summary>
    /// <returns>The value, or 0 past the end.</returns>
    public double Number()
    {
        ReadOnlySpan<byte> s = Take(8);
        return s.IsEmpty ? 0 : BinaryPrimitives.ReadDoubleLittleEndian(s);
    }

    /// <summary>Reads a block of bytes.</summary>
    /// <param name="count">The length.</param>
    /// <returns>The bytes, or empty past the end.</returns>
    public ReadOnlySpan<byte> Bytes(int count) => Take(count);

    /// <summary>Skips bytes.</summary>
    /// <param name="count">The count.</param>
    public void Skip(int count) => Take(count);

    /// <summary>Reads an XLWideString ([MS-XLSB] §2.5.169).</summary>
    /// <returns>The text; empty when the data ends or the count does not fit.</returns>
    public string WideString()
    {
        uint n = U32();
        if (Overrun || n > (uint)(Remaining / 2))
        {
            Overrun = true;
            _position = _data.Length;
            return string.Empty;
        }

        ReadOnlySpan<byte> s = Take((int)n * 2);
        return Encoding.Unicode.GetString(s);
    }

    /// <summary>Reads an XLNullableWideString ([MS-XLSB] §2.5.165).</summary>
    /// <returns>The text, or null for a count of 0xFFFFFFFF.</returns>
    public string? NullableWideString()
    {
        if (Remaining >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position)) == 0xFFFFFFFF)
        {
            _position += 4;
            return null;
        }

        return WideString();
    }
}
