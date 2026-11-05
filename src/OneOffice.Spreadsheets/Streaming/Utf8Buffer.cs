using System.Buffers;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>A fixed buffer in front of a stream, for writing XML as UTF-8 without building strings. Flushes when it fills.</summary>
internal sealed class Utf8Buffer(Stream sink, int size = 65536)
{
    // Characters that cannot go into element text as they are: markup, controls other than tab and line feed, the escape lead '_'
    // (checked for a following xHHHH_), surrogates (checked for pairing) and the two non-characters.
    private static readonly SearchValues<char> Special = SearchValues.Create(BuildSpecial());

    private static readonly SearchValues<char> PlainSpecial = SearchValues.Create(BuildPlainSpecial());

    private static readonly SearchValues<char> PlainBad = SearchValues.Create(BuildPlainBad());

    private readonly byte[] _buffer = new byte[size];
    private int _length;

    private static string BuildPlainBad() => BuildSpecial().Replace("_", string.Empty, StringComparison.Ordinal).Replace("&", string.Empty, StringComparison.Ordinal)
        .Replace("<", string.Empty, StringComparison.Ordinal).Replace(">", string.Empty, StringComparison.Ordinal);

    private static string BuildPlainSpecial()
    {
        // Like Special without the escape lead; tab and line feed pass.
        return BuildSpecial().Replace("_", string.Empty, StringComparison.Ordinal);
    }

    private static string BuildSpecial()
    {
        var sb = new StringBuilder("&<>_");
        for (char c = '\0'; c < 0x20; c++)
        {
            if (c is not ('\t' or '\n'))
            {
                sb.Append(c);
            }
        }

        for (int c = 0xD800; c <= 0xDFFF; c++)
        {
            sb.Append((char)c);
        }

        sb.Append('\uFFFE').Append('\uFFFF');
        return sb.ToString();
    }

    public long Written { get; private set; }

    /// <summary>Returns room for at least <paramref name="n"/> bytes (n at most the buffer size).</summary>
    public Span<byte> Reserve(int n)
    {
        if (_length + n > _buffer.Length)
        {
            Flush();
        }

        return _buffer.AsSpan(_length);
    }

    public void Advance(int n)
    {
        _length += n;
        Written += n;
    }

    public void Raw(ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length > 0)
        {
            int n = Math.Min(bytes.Length, _buffer.Length / 2);
            bytes[..n].CopyTo(Reserve(n));
            Advance(n);
            bytes = bytes[n..];
        }
    }

    public void Ascii(string s)
    {
        foreach (char c in s)
        {
            Reserve(1)[0] = (byte)c;
            Advance(1);
        }
    }

    public void Int(long value)
    {
        value.TryFormat(Reserve(20), out int n, default, CultureInfo.InvariantCulture);
        Advance(n);
    }

    // The shortest form that reads back to the same double. For some powers of two the shortest form is one unit in the last place off,
    // so long forms are read back and replaced by 17 significant digits when they differ.
    public void Double(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
        {
            Int((long)value);
            return;
        }

        Span<byte> s = Reserve(32);
        value.TryFormat(s, out int n, default, CultureInfo.InvariantCulture);
        if (n >= 15 && (!double.TryParse(s[..n], NumberStyles.Float, CultureInfo.InvariantCulture, out double back) || back != value))
        {
            value.TryFormat(s, out n, "G17", CultureInfo.InvariantCulture);
        }

        Advance(n);
    }

    private void Hex4(char c)
    {
        Span<byte> d = Reserve(7);
        const string Digits = "0123456789ABCDEF";
        d[0] = (byte)'_';
        d[1] = (byte)'x';
        d[2] = (byte)Digits[(c >> 12) & 15];
        d[3] = (byte)Digits[(c >> 8) & 15];
        d[4] = (byte)Digits[(c >> 4) & 15];
        d[5] = (byte)Digits[c & 15];
        d[6] = (byte)'_';
        Advance(7);
    }

    /// <summary>
    /// Writes text as element content. Characters XML cannot carry are written as <c>_xHHHH_</c> and a literal that looks like such an
    /// escape has its underscore escaped (ECMA-376 Part 1 ST_Xstring).
    /// </summary>
    public void Text(ReadOnlySpan<char> s)
    {
        while (s.Length > 0)
        {
            int i = s.IndexOfAny(Special);
            ReadOnlySpan<char> run = i < 0 ? s : s[..i];
            Encoded(run);
            if (i < 0)
            {
                return;
            }

            char c = s[i];
            int consumed = 1;
            switch (c)
            {
                case '&':
                    Raw("&amp;"u8);
                    break;
                case '<':
                    Raw("&lt;"u8);
                    break;
                case '>':
                    Raw("&gt;"u8);
                    break;
                case '_':
                    if (i + 7 <= s.Length && s[i + 1] == 'x' && s[i + 6] == '_' && IsHex(s.Slice(i + 2, 4)))
                    {
                        Raw("_x005F_"u8);
                    }
                    else
                    {
                        Raw("_"u8);
                    }

                    break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        Encoded(s.Slice(i, 2));
                        consumed = 2;
                    }
                    else
                    {
                        Hex4(c);
                    }

                    break;
            }

            s = s[(i + consumed)..];
        }
    }

    /// <summary>
    /// Writes text that is not an ST_Xstring (a formula): markup characters are escaped and nothing else changes, so it reads back exactly.
    /// </summary>
    /// <exception cref="ArgumentException">The text has a character XML cannot carry.</exception>
    public void Plain(ReadOnlySpan<char> s)
    {
        while (s.Length > 0)
        {
            int i = s.IndexOfAny(PlainSpecial);
            Encoded(i < 0 ? s : s[..i]);
            if (i < 0)
            {
                return;
            }

            switch (s[i])
            {
                case '&':
                    Raw("&amp;"u8);
                    break;
                case '<':
                    Raw("&lt;"u8);
                    break;
                case '>':
                    Raw("&gt;"u8);
                    break;
                default:
                    if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        Encoded(s.Slice(i, 2));
                        s = s[(i + 2)..];
                        continue;
                    }

                    throw new ArgumentException("The text has a character that XML cannot carry.");
            }

            s = s[(i + 1)..];
        }
    }

    /// <summary>Tells whether <see cref="Plain"/> can write the text.</summary>
    public static bool IsPlainSafe(ReadOnlySpan<char> s)
    {
        while (true)
        {
            int i = s.IndexOfAny(PlainBad);
            if (i < 0)
            {
                return true;
            }

            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                s = s[(i + 2)..];
                continue;
            }

            return false;
        }
    }

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private void Encoded(ReadOnlySpan<char> run)
    {
        const int Chunk = 8192;
        while (run.Length > 0)
        {
            int n = Math.Min(run.Length, Chunk);
            // Never split a surrogate pair across chunks (the run holds only well-formed pairs).
            if (n < run.Length && char.IsHighSurrogate(run[n - 1]))
            {
                n--;
            }

            Span<byte> d = Reserve(n * 3);
            int w = Encoding.UTF8.GetBytes(run[..n], d);
            Advance(w);
            run = run[n..];
        }
    }

    private static readonly SearchValues<char> AttributeSpecial = SearchValues.Create(BuildAttributeSpecial());

    private static string BuildAttributeSpecial()
    {
        var sb = new StringBuilder("&<>\"");
        for (char c = '\0'; c < 0x20; c++)
        {
            sb.Append(c);
        }

        return sb.Append('\uFFFE').Append('\uFFFF').ToString();
    }

    /// <summary>Writes text as an attribute value, quotes escaped.</summary>
    /// <exception cref="ArgumentException">The text has a control character or non-character XML cannot carry.</exception>
    public void Attribute(ReadOnlySpan<char> s)
    {
        while (s.Length > 0)
        {
            int i = s.IndexOfAny(AttributeSpecial);
            Encoded(i < 0 ? s : s[..i]);
            if (i < 0)
            {
                return;
            }

            switch (s[i])
            {
                case '&':
                    Raw("&amp;"u8);
                    break;
                case '<':
                    Raw("&lt;"u8);
                    break;
                case '>':
                    Raw("&gt;"u8);
                    break;
                case '"':
                    Raw("&quot;"u8);
                    break;
                default:
                    throw new ArgumentException("The text has a character that XML cannot carry.");
            }

            s = s[(i + 1)..];
        }
    }

    public void Flush()
    {
        if (_length > 0)
        {
            sink.Write(_buffer, 0, _length);
            _length = 0;
        }
    }
}
