using System.Text;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// Reads the data of one record together with the CONTINUE records after it as one logical sequence, remembering where each part
/// begins. A string split by a CONTINUE restarts its characters with a new flag byte ([MS-XLS] §2.1.4 Record, §2.5.293 XLUnicodeRichExtendedString),
/// which is why the parts are kept apart instead of being joined.
/// </summary>
internal sealed class RecordReader
{
    private const int ContinueType = 0x003C;
    private readonly ReadOnlyMemory<byte>[] _parts;
    private int _part;
    private int _pos;

    public RecordReader(ReadOnlyMemory<byte> single)
    {
        _parts = [single];
    }

    private RecordReader(ReadOnlyMemory<byte>[] parts)
    {
        _parts = parts;
    }

    /// <summary>Reads record <paramref name="index"/> and, when <paramref name="withContinues"/>, the CONTINUE records that follow it.</summary>
    public static RecordReader Open(BiffRecordSet stream, int index, bool withContinues, out int lastIndex)
    {
        IReadOnlyList<BiffRecord> recs = stream.Records;
        lastIndex = index;
        if (!withContinues || index + 1 >= recs.Count || recs[index + 1].Type != ContinueType)
        {
            return new RecordReader(stream.Data(recs[index]));
        }

        var parts = new List<ReadOnlyMemory<byte>> { stream.Data(recs[index]) };
        while (lastIndex + 1 < recs.Count && recs[lastIndex + 1].Type == ContinueType)
        {
            lastIndex++;
            parts.Add(stream.Data(recs[lastIndex]));
        }

        return new RecordReader([.. parts]);
    }

    /// <summary>Bytes left in the whole sequence.</summary>
    public int Remaining
    {
        get
        {
            int n = _parts[_part].Length - _pos;
            for (int i = _part + 1; i < _parts.Length; i++)
            {
                n += _parts[i].Length;
            }

            return n;
        }
    }

    /// <summary>Bytes left in the current part.</summary>
    public int RemainingInPart => _parts[_part].Length - _pos;

    /// <summary>True when every byte has been read.</summary>
    public bool AtEnd => Remaining <= 0;

    /// <summary>Moves to the start of the next part when the current one is used up.</summary>
    private void Normalize()
    {
        while (_pos >= _parts[_part].Length && _part + 1 < _parts.Length)
        {
            _part++;
            _pos = 0;
        }
    }

    public bool TryByte(out byte b)
    {
        Normalize();
        if (_pos < _parts[_part].Length)
        {
            b = _parts[_part].Span[_pos++];
            return true;
        }

        b = 0;
        return false;
    }

    /// <summary>Reads a byte; past the end gives 0 and sets <see cref="Overrun"/>.</summary>
    public byte U8()
    {
        if (TryByte(out byte b))
        {
            return b;
        }

        Overrun = true;
        return 0;
    }

    public bool Overrun { get; private set; }

    public ushort U16() => (ushort)(U8() | (U8() << 8));

    public short I16() => (short)U16();

    public uint U32() => U16() | ((uint)U16() << 16);

    public int I32() => (int)U32();

    public double F64()
    {
        long v = 0;
        for (int i = 0; i < 8; i++)
        {
            v |= (long)U8() << (8 * i);
        }

        return BitConverter.Int64BitsToDouble(v);
    }

    public void Skip(int n)
    {
        while (n > 0)
        {
            Normalize();
            int take = Math.Min(n, _parts[_part].Length - _pos);
            if (take <= 0)
            {
                Overrun = true;
                return;
            }

            _pos += take;
            n -= take;
        }
    }

    /// <summary>Reads <paramref name="n"/> bytes, across parts.</summary>
    public byte[] Bytes(int n)
    {
        n = Math.Clamp(n, 0, Math.Max(Remaining, 0));
        var b = new byte[n];
        for (int i = 0; i < n; i++)
        {
            b[i] = U8();
        }

        return b;
    }

    /// <summary>The rest of the current part, without moving.</summary>
    public ReadOnlySpan<byte> PeekPart() => _parts[_part].Span[_pos..];

    /// <summary>The whole rest of the sequence joined.</summary>
    public byte[] RestJoined() => Bytes(Remaining);

    /// <summary>Reads a string of <paramref name="cch"/> characters that are 8-bit in <paramref name="enc"/> or 16-bit, with the CONTINUE flag rule.</summary>
    public string Chars(int cch, bool wide, Encoding enc)
    {
        if (cch <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(cch, 1 << 16));
        int left = cch;
        while (left > 0)
        {
            Normalize();
            if (_pos >= _parts[_part].Length)
            {
                Overrun = true;
                break;
            }

            // A part that ends inside the characters is followed by a part whose first byte says how its characters are stored.
            ReadOnlySpan<byte> span = _parts[_part].Span;
            int avail = span.Length - _pos;
            int unit = wide ? 2 : 1;
            int fit = Math.Min(left, avail / unit);
            if (fit == 0)
            {
                // A wide character split across parts: take the bytes one at a time.
                int lo = U8();
                Normalize();
                if (_part >= _parts.Length || (_pos >= _parts[_part].Length))
                {
                    Overrun = true;
                    break;
                }

                int hi = U8();
                sb.Append((char)(lo | (hi << 8)));
                left--;
            }
            else
            {
                ReadOnlySpan<byte> chunk = span.Slice(_pos, fit * unit);
                if (wide)
                {
                    for (int i = 0; i < fit; i++)
                    {
                        sb.Append((char)(chunk[2 * i] | (chunk[2 * i + 1] << 8)));
                    }
                }
                else
                {
                    sb.Append(enc.GetString(chunk));
                }

                _pos += fit * unit;
                left -= fit;
            }

            if (left > 0 && _pos >= _parts[_part].Length && _part + 1 < _parts.Length)
            {
                _part++;
                _pos = 0;
                byte flag = U8();
                wide = (flag & 1) != 0;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads an XLUnicodeString-family string: a character count (1 or 2 bytes), a flag byte, then the characters, with the
    /// rich-text and phonetic parts that follow skipped ([MS-XLS] §2.5.293, §2.5.294, §2.5.240).
    /// </summary>
    public string UnicodeString(int cchBytes, bool hasFlags, Encoding enc8)
    {
        int cch = cchBytes == 1 ? U8() : U16();
        return UnicodeStringBody(cch, hasFlags, enc8);
    }

    /// <summary>Reads a BIFF2-5 string: a 1-byte length and 8-bit characters in the workbook's code page.</summary>
    public string UnicodeStringShort(Encoding enc8) => Chars(U8(), false, enc8);

    public string UnicodeStringBody(int cch, bool hasFlags, Encoding enc8)
    {
        byte flags = hasFlags ? U8() : (byte)0;
        bool wide = (flags & 1) != 0;
        int runs = (flags & 8) != 0 ? U16() : 0;
        int ext = (flags & 4) != 0 ? I32() : 0;
        string s = Chars(cch, wide, enc8);
        Skip(runs * 4);
        if (ext > 0)
        {
            Skip(ext);
        }

        return s;
    }
}
