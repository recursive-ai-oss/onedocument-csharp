using System.Text;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>A growable little-endian byte buffer for building the data of one BIFF record.</summary>
internal sealed class BiffBuffer
{
    private byte[] _bytes = new byte[64];
    private int _length;

    /// <summary>The number of bytes written.</summary>
    public int Length => _length;

    /// <summary>The bytes written so far.</summary>
    public ReadOnlySpan<byte> Span => _bytes.AsSpan(0, _length);

    /// <summary>Empties the buffer for reuse.</summary>
    public void Clear() => _length = 0;

    private void Ensure(int extra)
    {
        if (_length + extra > _bytes.Length)
        {
            Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, _length + extra));
        }
    }

    /// <summary>Writes one byte.</summary>
    public void U8(int v)
    {
        Ensure(1);
        _bytes[_length++] = (byte)v;
    }

    /// <summary>Writes a 16-bit value.</summary>
    public void U16(int v)
    {
        Ensure(2);
        _bytes[_length++] = (byte)v;
        _bytes[_length++] = (byte)(v >> 8);
    }

    /// <summary>Writes a 32-bit value.</summary>
    public void U32(uint v)
    {
        Ensure(4);
        _bytes[_length++] = (byte)v;
        _bytes[_length++] = (byte)(v >> 8);
        _bytes[_length++] = (byte)(v >> 16);
        _bytes[_length++] = (byte)(v >> 24);
    }

    /// <summary>Writes a signed 32-bit value.</summary>
    public void I32(int v) => U32(unchecked((uint)v));

    /// <summary>Writes an IEEE double.</summary>
    public void F64(double v)
    {
        Ensure(8);
        BitConverter.TryWriteBytes(_bytes.AsSpan(_length, 8), v);
        _length += 8;
    }

    /// <summary>Writes raw bytes.</summary>
    public void Bytes(ReadOnlySpan<byte> b)
    {
        Ensure(b.Length);
        b.CopyTo(_bytes.AsSpan(_length));
        _length += b.Length;
    }

    /// <summary>Writes zero bytes.</summary>
    public void Zeros(int n)
    {
        Ensure(n);
        Array.Clear(_bytes, _length, n);
        _length += n;
    }

    /// <summary>Overwrites two bytes at an earlier position.</summary>
    public void PatchU16(int at, int v)
    {
        _bytes[at] = (byte)v;
        _bytes[at + 1] = (byte)(v >> 8);
    }

    /// <summary>Overwrites four bytes at an earlier position.</summary>
    public void PatchU32(int at, uint v)
    {
        _bytes[at] = (byte)v;
        _bytes[at + 1] = (byte)(v >> 8);
        _bytes[at + 2] = (byte)(v >> 16);
        _bytes[at + 3] = (byte)(v >> 24);
    }

    /// <summary>True when every character fits the compressed (Latin-1) form of a BIFF8 string.</summary>
    public static bool IsCompressible(string s)
    {
        foreach (char c in s)
        {
            if (c > 0xFF)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The characters of a string after its flag byte: one byte each when compressible, otherwise UTF-16.</summary>
    public void Chars(string s, bool wide)
    {
        if (wide)
        {
            Ensure(s.Length * 2);
            foreach (char c in s)
            {
                _bytes[_length++] = (byte)c;
                _bytes[_length++] = (byte)(c >> 8);
            }
        }
        else
        {
            Ensure(s.Length);
            foreach (char c in s)
            {
                _bytes[_length++] = (byte)c;
            }
        }
    }

    /// <summary>XLUnicodeString ([MS-XLS] §2.5.294): a 16-bit count, a flag byte, the characters.</summary>
    public void String16(string s)
    {
        bool wide = !IsCompressible(s);
        U16(s.Length);
        U8(wide ? 1 : 0);
        Chars(s, wide);
    }

    /// <summary>ShortXLUnicodeString ([MS-XLS] §2.5.240): an 8-bit count, a flag byte, the characters.</summary>
    public void String8(string s)
    {
        bool wide = !IsCompressible(s);
        U8(s.Length);
        U8(wide ? 1 : 0);
        Chars(s, wide);
    }

    /// <summary>XLUnicodeStringNoCch: a flag byte and the characters (the count is stored elsewhere).</summary>
    public void StringNoCount(string s)
    {
        bool wide = !IsCompressible(s);
        U8(wide ? 1 : 0);
        Chars(s, wide);
    }

    /// <summary>UTF-16 characters with no flag byte (hyperlink strings).</summary>
    public void Utf16(string s) => Bytes(Encoding.Unicode.GetBytes(s));
}

/// <summary>Collects records into a workbook stream, splitting data longer than 8,224 bytes into <c>CONTINUE</c> records ([MS-XLS] §2.1.4).</summary>
internal sealed class BiffSink
{
    /// <summary>The most data one record carries.</summary>
    public const int MaxData = 8224;

    private byte[] _bytes = new byte[1024];
    private int _length;

    /// <summary>The position the next record starts at.</summary>
    public int Position => _length;

    /// <summary>The stream written so far.</summary>
    public byte[] ToArray() => _bytes.AsSpan(0, _length).ToArray();

    /// <summary>Empties the sink for reuse.</summary>
    public void Reset() => _length = 0;

    /// <summary>Appends bytes that are already whole records.</summary>
    public void AppendRaw(ReadOnlySpan<byte> records) => Put(records);

    private void Put(ReadOnlySpan<byte> data)
    {
        if (_length + data.Length > _bytes.Length)
        {
            Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, _length + data.Length));
        }

        data.CopyTo(_bytes.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>Writes a record. Data over the limit continues in <c>CONTINUE</c> records cut at arbitrary points.</summary>
    public void Record(int type, ReadOnlySpan<byte> data)
    {
        int first = Math.Min(data.Length, MaxData);
        Header(type, first);
        Put(data[..first]);
        int done = first;
        while (done < data.Length)
        {
            int n = Math.Min(data.Length - done, MaxData);
            Header(0x003C, n);
            Put(data.Slice(done, n));
            done += n;
        }
    }

    /// <summary>Writes a record from a buffer.</summary>
    public void Record(int type, BiffBuffer data) => Record(type, data.Span);

    /// <summary>Writes a record with no data.</summary>
    public void Record(int type) => Header(type, 0);

    /// <summary>Writes a record whose data the caller has already cut to size (a base record or a CONTINUE).</summary>
    public void Exact(int type, ReadOnlySpan<byte> data)
    {
        Header(type, data.Length);
        Put(data);
    }

    /// <summary>Overwrites four bytes at an earlier position (a sheet's stream offset in <c>BOUNDSHEET</c>).</summary>
    public void PatchU32(int at, uint v) => BitConverter.TryWriteBytes(_bytes.AsSpan(at, 4), v);

    private void Header(int type, int length)
    {
        Span<byte> h = stackalloc byte[4]; // the record header is four bytes
        h[0] = (byte)type;
        h[1] = (byte)(type >> 8);
        h[2] = (byte)length;
        h[3] = (byte)(length >> 8);
        Put(h);
    }
}
