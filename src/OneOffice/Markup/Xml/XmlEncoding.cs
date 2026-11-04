using System.Buffers;
using System.Text;

namespace RecursiveAI.OneOffice.Markup.Xml;

/// <summary>How an XML part's bytes are encoded.</summary>
public enum XmlEncodingKind
{
    /// <summary>UTF-8, with or without a byte order mark.</summary>
    Utf8,

    /// <summary>UTF-16, little-endian, with or without a byte order mark.</summary>
    Utf16LittleEndian,

    /// <summary>UTF-16, big-endian, with or without a byte order mark.</summary>
    Utf16BigEndian,
}

/// <summary>
/// Detects the encoding of an XML part and transcodes UTF-16 to UTF-8 and back. The tokenizer reads UTF-8 only; some Office parts
/// are UTF-16, and the markup tree transcodes them and restores the byte order mark when it writes them back.
/// </summary>
public static class XmlEncoding
{
    /// <summary>Detects the encoding from the first bytes, as XML 1.0 Appendix F describes (a byte order mark, or a "&lt;" in the first character).</summary>
    /// <param name="data">The start of the part.</param>
    /// <param name="kind">The encoding family.</param>
    /// <param name="bomLength">The length of the byte order mark (0 when there is none).</param>
    /// <returns>False when the bytes look like UTF-32 or an unsupported encoding; true otherwise (UTF-8 is the fallback).</returns>
    public static bool TryDetect(ReadOnlySpan<byte> data, out XmlEncodingKind kind, out int bomLength)
    {
        kind = XmlEncodingKind.Utf8;
        bomLength = 0;
        if (data.Length >= 4 && ((data[0] == 0 && data[1] == 0 && (data[2] == 0xFE || data[2] == 0 || data[3] == 0x3C)) || (data[0] == 0xFF && data[1] == 0xFE && data[2] == 0 && data[3] == 0) || (data[0] == 0x3C && data[1] == 0 && data[2] == 0 && data[3] == 0)))
        {
            return false;
        }

        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            bomLength = 3;
        }
        else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            kind = XmlEncodingKind.Utf16LittleEndian;
            bomLength = 2;
        }
        else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            kind = XmlEncodingKind.Utf16BigEndian;
            bomLength = 2;
        }
        else if (data.Length >= 2 && data[0] == 0x3C && data[1] == 0)
        {
            kind = XmlEncodingKind.Utf16LittleEndian;
        }
        else if (data.Length >= 2 && data[0] == 0 && data[1] == 0x3C)
        {
            kind = XmlEncodingKind.Utf16BigEndian;
        }

        return true;
    }

    /// <summary>Transcodes UTF-16 bytes (after any byte order mark) to UTF-8.</summary>
    /// <param name="utf16">The bytes after the byte order mark.</param>
    /// <param name="bigEndian">True for big-endian.</param>
    /// <returns>The UTF-8 bytes, without a byte order mark.</returns>
    /// <exception cref="XmlSyntaxException">The length is odd or a surrogate is unpaired (<see cref="XmlErrorCode.InvalidEncoding"/>).</exception>
    public static byte[] Utf16ToUtf8(ReadOnlySpan<byte> utf16, bool bigEndian)
    {
        if ((utf16.Length & 1) != 0)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidEncoding, utf16.Length, "UTF-16 text has an odd number of bytes.");
        }

        try
        {
            string s = new UnicodeEncoding(bigEndian, byteOrderMark: false, throwOnInvalidBytes: true).GetString(utf16);
            return Encoding.UTF8.GetBytes(s);
        }
        catch (DecoderFallbackException ex)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidEncoding, ex.Index, "UTF-16 text has an unpaired surrogate.");
        }
    }

    /// <summary>Transcodes UTF-8 to UTF-16 and writes it, with a byte order mark when asked.</summary>
    /// <param name="utf8">The UTF-8 bytes.</param>
    /// <param name="bigEndian">True for big-endian.</param>
    /// <param name="writeByteOrderMark">True to write U+FEFF first.</param>
    /// <param name="destination">Where the bytes go.</param>
    /// <exception cref="XmlSyntaxException"><paramref name="utf8"/> is not valid UTF-8.</exception>
    public static void Utf8ToUtf16(ReadOnlySpan<byte> utf8, bool bigEndian, bool writeByteOrderMark, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        string s;
        try
        {
            s = new UTF8Encoding(false, true).GetString(utf8);
        }
        catch (DecoderFallbackException ex)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidEncoding, ex.Index, "The bytes are not valid UTF-8.");
        }

        var enc = new UnicodeEncoding(bigEndian, byteOrderMark: false);
        if (writeByteOrderMark)
        {
            Span<byte> bom = destination.GetSpan(2);
            bom[0] = bigEndian ? (byte)0xFE : (byte)0xFF;
            bom[1] = bigEndian ? (byte)0xFF : (byte)0xFE;
            destination.Advance(2);
        }

        byte[] bytes = enc.GetBytes(s);
        bytes.CopyTo(destination.GetSpan(bytes.Length));
        destination.Advance(bytes.Length);
    }
}
