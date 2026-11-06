using System.Buffers;
using System.Globalization;

namespace RecursiveAI.OneDocument.Rtf;

/// <summary>
/// Writes RTF tokens (RTF 1.9.1): groups, control words and symbols, escaped text and binary data. It adds the one space that must
/// delimit a control word from the text after it, escapes <c>\</c>, <c>{</c> and <c>}</c>, and writes every character above 0x7F as the
/// Windows-1252 byte (<c>\'hh</c>) when it has one and otherwise as <c>\uN?</c> (one fallback character, matching <c>\uc1</c>), so the
/// output is 7-bit ASCII and reads back under <c>\ansicpg1252</c>.
/// </summary>
/// <remarks>The writer never checks that groups are balanced: <see cref="Depth"/> says how many are open.</remarks>
public sealed class RtfWriter
{
    // Windows-1252 0x80-0x9F; U+0000 marks the five undefined positions.
    private const string Cp1252High = "\u20AC\0\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\0\u017D\0\0\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\0\u017E\u0178";

    private readonly ArrayBufferWriter<byte> _out = new(4096);
    private bool _needSpace;
    private int _depth;

    /// <summary>Gets the number of groups opened and not yet closed.</summary>
    public int Depth => _depth;

    /// <summary>Gets the number of bytes written so far.</summary>
    public int Length => _out.WrittenCount;

    /// <summary>Writes <c>{</c>.</summary>
    public void StartGroup()
    {
        Put((byte)'{');
        _depth++;
        _needSpace = false;
    }

    /// <summary>Writes <c>}</c>.</summary>
    public void EndGroup()
    {
        Put((byte)'}');
        if (_depth > 0)
        {
            _depth--;
        }

        _needSpace = false;
    }

    /// <summary>Writes a control word without a parameter.</summary>
    /// <param name="name">The letters of the word, without the backslash.</param>
    public void Word(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Put((byte)'\\');
        PutAscii(name);
        _needSpace = true;
    }

    /// <summary>Writes a control word with a numeric parameter.</summary>
    /// <param name="name">The letters of the word, without the backslash.</param>
    /// <param name="value">The parameter.</param>
    public void Word(string name, int value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Put((byte)'\\');
        PutAscii(name);
        PutAscii(value.ToString(CultureInfo.InvariantCulture));
        _needSpace = true;
    }

    /// <summary>Writes a control symbol: a backslash and one non-letter character (<c>\*</c>, <c>\~</c>).</summary>
    /// <param name="symbol">The character.</param>
    public void Symbol(char symbol)
    {
        Put((byte)'\\');
        Put((byte)symbol);
        _needSpace = false;
    }

    /// <summary>Starts an extension destination: <c>{\*\name</c>.</summary>
    /// <param name="name">The destination's control word.</param>
    public void StartDestination(string name)
    {
        StartGroup();
        Symbol('*');
        Word(name);
    }

    /// <summary>Writes text, escaped.</summary>
    /// <param name="text">The text. A tab becomes <c>\tab</c>; line breaks are the caller's concern (<c>\par</c>, <c>\line</c>).</param>
    public void Text(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\\':
                case '{':
                case '}':
                    Put((byte)'\\');
                    Put((byte)c);
                    _needSpace = false;
                    break;
                case '\t':
                    Word("tab");
                    break;
                case < ' ':
                case '\u007F':
                    break; // control characters have no place in RTF text
                case < '\u007F':
                    Delimit();
                    Put((byte)c);
                    break;
                default:
                    {
                        int b = Cp1252Byte(c);
                        if (b >= 0)
                        {
                            Hex((byte)b);
                        }
                        else
                        {
                            Put((byte)'\\');
                            PutAscii("u" + ((short)c).ToString(CultureInfo.InvariantCulture));
                            Put((byte)'?');
                            _needSpace = false;
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>Writes <c>\'hh</c>.</summary>
    /// <param name="value">The byte.</param>
    public void Hex(byte value)
    {
        const string digits = "0123456789abcdef";
        Put((byte)'\\');
        Put((byte)'\'');
        Put((byte)digits[value >> 4]);
        Put((byte)digits[value & 15]);
        _needSpace = false;
    }

    /// <summary>Writes <c>\binN</c> and the bytes after it, which are not escaped.</summary>
    /// <param name="data">The bytes.</param>
    public void Binary(ReadOnlySpan<byte> data)
    {
        Word("bin", data.Length);
        Put((byte)' ');
        _needSpace = false;
        data.CopyTo(_out.GetSpan(data.Length));
        _out.Advance(data.Length);
    }

    /// <summary>Writes bytes exactly as given, for groups kept from a source file.</summary>
    /// <param name="bytes">RTF bytes.</param>
    public void Raw(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_out.GetSpan(bytes.Length));
        _out.Advance(bytes.Length);
        _needSpace = false;
    }

    /// <summary>Writes a picture's data as hexadecimal text, 64 characters to a line.</summary>
    /// <param name="data">The picture bytes.</param>
    public void HexData(ReadOnlySpan<byte> data)
    {
        const string digits = "0123456789abcdef";
        Delimit();
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0 && (i & 31) == 0)
            {
                Put((byte)'\r');
                Put((byte)'\n');
            }

            Put((byte)digits[data[i] >> 4]);
            Put((byte)digits[data[i] & 15]);
        }

        _needSpace = false;
    }

    /// <summary>Writes a line break between tokens, which carries no meaning in RTF; it keeps lines short for readers and diff tools.</summary>
    public void Newline()
    {
        Put((byte)'\r');
        Put((byte)'\n');
        _needSpace = false;
    }

    /// <summary>Gets the bytes written so far.</summary>
    /// <returns>A copy.</returns>
    public byte[] ToArray() => _out.WrittenSpan.ToArray();

    /// <summary>Copies the bytes written so far to a stream.</summary>
    /// <param name="destination">The stream.</param>
    public void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(_out.WrittenSpan);
    }

    private static int Cp1252Byte(char c)
    {
        if (c is >= '\u00A0' and <= '\u00FF')
        {
            return c;
        }

        if (c >= '\u0080' && c < '\u00A0')
        {
            return -1;
        }

        int i = Cp1252High.IndexOf(c, StringComparison.Ordinal);
        return i >= 0 && c != '\0' ? 0x80 + i : -1;
    }

    private void Delimit()
    {
        if (_needSpace)
        {
            Put((byte)' ');
            _needSpace = false;
        }
    }

    private void Put(byte b)
    {
        _out.GetSpan(1)[0] = b;
        _out.Advance(1);
    }

    private void PutAscii(string s)
    {
        Span<byte> span = _out.GetSpan(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            span[i] = (byte)s[i];
        }

        _out.Advance(s.Length);
    }
}
