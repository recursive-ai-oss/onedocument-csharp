using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace RecursiveAI.OneOffice.Markup.Xml;

/// <summary>Namespace names that XML itself defines.</summary>
public static class XmlNamespaces
{
    /// <summary>The namespace bound to the predeclared <c>xml</c> prefix.</summary>
    public const string Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>The namespace of the <c>xmlns</c> attributes. It can never be bound to a prefix.</summary>
    public const string Xmlns = "http://www.w3.org/2000/xmlns/";

    internal static ReadOnlySpan<byte> XmlUtf8 => "http://www.w3.org/XML/1998/namespace"u8;

    internal static ReadOnlySpan<byte> XmlnsUtf8 => "http://www.w3.org/2000/xmlns/"u8;
}

/// <summary>Decoding and escaping of XML character data, entity references included.</summary>
public static class XmlText
{
    private static readonly SearchValues<char> s_escapeChars = SearchValues.Create(
        "&<>\"'\t\n\r\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000B\u000C\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F\uFFFE\uFFFF");

    /// <summary>
    /// Tells whether a raw span from the tokenizer needs <see cref="Decode"/>: it holds a reference, or a carriage return, or
    /// (for an attribute value) a tab or line feed that normalisation turns into a space.
    /// </summary>
    /// <param name="raw">The raw bytes, exactly as they stand in the document.</param>
    /// <param name="isAttributeValue">True for an attribute value, false for element content.</param>
    /// <returns>True when decoding would change the bytes.</returns>
    public static bool NeedsDecoding(ReadOnlySpan<byte> raw, bool isAttributeValue = false) =>
        isAttributeValue
            ? raw.IndexOfAny((byte)'&', (byte)'\r', (byte)'\n') >= 0 || raw.IndexOf((byte)'\t') >= 0
            : raw.IndexOfAny((byte)'&', (byte)'\r') >= 0;

    /// <summary>
    /// Decodes references and normalises line ends in a raw span that the tokenizer has already validated.
    /// </summary>
    /// <param name="raw">Raw text or attribute-value bytes.</param>
    /// <param name="destination">Receives the UTF-8 result. The result is never longer than <paramref name="raw"/>, so a buffer of that length always fits.</param>
    /// <param name="isAttributeValue">True for an attribute value: tab, line feed and carriage return written literally become spaces (XML 1.0 §3.3.3).</param>
    /// <returns>The number of bytes written.</returns>
    /// <remarks>
    /// A carriage-return/line-feed pair, or a lone carriage return, in element content becomes one line feed (XML 1.0 §2.11).
    /// A malformed reference, which the tokenizer would have refused, is copied unchanged.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <paramref name="raw"/>.</exception>
    public static int Decode(ReadOnlySpan<byte> raw, Span<byte> destination, bool isAttributeValue = false)
    {
        if (destination.Length < raw.Length)
        {
            throw new ArgumentException("The destination must be at least as long as the raw bytes.", nameof(destination));
        }

        int w = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            byte b = raw[i];
            if (b == '&')
            {
                int semi = raw[i..].IndexOf((byte)';');
                if (semi > 1 && semi <= 12)
                {
                    ReadOnlySpan<byte> body = raw.Slice(i + 1, semi - 1);
                    int cp = -1;
                    if (body.SequenceEqual("amp"u8))
                    {
                        cp = '&';
                    }
                    else if (body.SequenceEqual("lt"u8))
                    {
                        cp = '<';
                    }
                    else if (body.SequenceEqual("gt"u8))
                    {
                        cp = '>';
                    }
                    else if (body.SequenceEqual("quot"u8))
                    {
                        cp = '"';
                    }
                    else if (body.SequenceEqual("apos"u8))
                    {
                        cp = '\'';
                    }
                    else if (body[0] == '#' && XmlScan.TryParseCharReference(body, out int c))
                    {
                        cp = c;
                    }

                    if (cp >= 0)
                    {
                        w += new Rune(cp).EncodeToUtf8(destination[w..]);
                        i += semi;
                        continue;
                    }
                }

                destination[w++] = b;
            }
            else if (b == '\r')
            {
                if (i + 1 < raw.Length && raw[i + 1] == '\n')
                {
                    i++;
                }

                destination[w++] = isAttributeValue ? (byte)' ' : (byte)'\n';
            }
            else if (isAttributeValue && (b == '\n' || b == '\t'))
            {
                destination[w++] = (byte)' ';
            }
            else
            {
                destination[w++] = b;
            }
        }

        return w;
    }

    /// <summary>Decodes a raw span from the tokenizer to a string.</summary>
    /// <param name="raw">Raw text or attribute-value bytes.</param>
    /// <param name="isAttributeValue">True for an attribute value.</param>
    /// <returns>The decoded text. Allocates the string, and a temporary buffer when <see cref="NeedsDecoding"/> is true.</returns>
    public static string GetString(ReadOnlySpan<byte> raw, bool isAttributeValue = false)
    {
        if (!NeedsDecoding(raw, isAttributeValue))
        {
            return Encoding.UTF8.GetString(raw);
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(raw.Length);
        try
        {
            int n = Decode(raw, rented, isAttributeValue);
            return Encoding.UTF8.GetString(rented, 0, n);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Escapes a string as XML character data or as an attribute value and writes it as UTF-8.</summary>
    /// <param name="destination">Where the bytes go.</param>
    /// <param name="value">The text.</param>
    /// <param name="isAttributeValue">True to escape for an attribute value: the quote character, tab, line feed and carriage return become references so they survive attribute-value normalisation.</param>
    /// <param name="quote">The quote character that will delimit the attribute value, <c>"</c> or <c>'</c>; only that one is escaped.</param>
    /// <exception cref="ArgumentException">The text has a character XML cannot carry (a control character other than tab, line feed and carriage return, U+FFFE, U+FFFF or an unpaired surrogate).</exception>
    public static void Escape(IBufferWriter<byte> destination, ReadOnlySpan<char> value, bool isAttributeValue, char quote = '"')
    {
        ArgumentNullException.ThrowIfNull(destination);
        while (value.Length > 0)
        {
            int k = value.IndexOfAny(s_escapeChars);
            ReadOnlySpan<char> run = k < 0 ? value : value[..k];
            if (run.Length > 0)
            {
                WriteUtf8(destination, run);
            }

            if (k < 0)
            {
                return;
            }

            char c = value[k];
            value = value[(k + 1)..];
            switch (c)
            {
                case '&':
                    WriteAscii(destination, "&amp;"u8);
                    break;
                case '<':
                    WriteAscii(destination, "&lt;"u8);
                    break;
                case '>':
                    WriteAscii(destination, "&gt;"u8);
                    break;
                case '"':
                    WriteAscii(destination, isAttributeValue && quote == '"' ? "&quot;"u8 : "\""u8);
                    break;
                case '\'':
                    WriteAscii(destination, isAttributeValue && quote == '\'' ? "&apos;"u8 : "'"u8);
                    break;
                case '\t':
                    WriteAscii(destination, isAttributeValue ? "&#9;"u8 : "\t"u8);
                    break;
                case '\n':
                    WriteAscii(destination, isAttributeValue ? "&#10;"u8 : "\n"u8);
                    break;
                case '\r':
                    WriteAscii(destination, "&#13;"u8);
                    break;
                default:
                    throw new ArgumentException($"U+{(int)c:X4} cannot be written in XML.", nameof(value));
            }
        }
    }

    private static void WriteUtf8(IBufferWriter<byte> destination, ReadOnlySpan<char> chars)
    {
        Span<byte> span = destination.GetSpan(chars.Length * 3);
        OperationStatus st = Utf8.FromUtf16(chars, span, out _, out int written, replaceInvalidSequences: false);
        if (st != OperationStatus.Done)
        {
            throw new ArgumentException("The text has an unpaired surrogate and cannot be written as UTF-8.", nameof(chars));
        }

        destination.Advance(written);
    }

    private static void WriteAscii(IBufferWriter<byte> destination, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(destination.GetSpan(bytes.Length));
        destination.Advance(bytes.Length);
    }
}
