using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>Small helpers shared by the streaming reader's part scanners.</summary>
internal static class StreamXml
{
    /// <summary>
    /// Appends the text inside the current element, up to its end tag, to a reusable buffer with references decoded. Allocates only
    /// when the buffer has to grow.
    /// </summary>
    public static void AppendText(MarkupReader reader, ref byte[] buffer, ref int length)
    {
        int depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.Kind == XmlTokenKind.EndElement && reader.Depth == depth)
            {
                return;
            }

            if (reader.Kind is XmlTokenKind.Text or XmlTokenKind.Whitespace or XmlTokenKind.CData)
            {
                ReadOnlySpan<byte> piece = reader.Content;
                if (length + piece.Length > buffer.Length)
                {
                    Array.Resize(ref buffer, Math.Max(buffer.Length * 2, length + piece.Length));
                }

                if (reader.Kind == XmlTokenKind.CData || !XmlText.NeedsDecoding(piece))
                {
                    piece.CopyTo(buffer.AsSpan(length));
                    length += piece.Length;
                }
                else
                {
                    length += XmlText.Decode(piece, buffer.AsSpan(length));
                }
            }
        }
    }

    public static bool TryInt(ReadOnlySpan<byte> v, out int value) =>
        int.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out value);

    public static bool ParseBool(ReadOnlySpan<byte> v) =>
        v.SequenceEqual("1"u8) || v.SequenceEqual("true"u8) || v.SequenceEqual("True"u8) || v.SequenceEqual("TRUE"u8);

    /// <summary>
    /// Decodes <c>_xHHHH_</c> escapes in place (ECMA-376 Part 1 ST_Xstring) and returns the new length. Nothing is allocated.
    /// </summary>
    public static int DecodeEscapes(Span<char> text)
    {
        if (text.IndexOf("_x".AsSpan()) < 0)
        {
            return text.Length;
        }

        int w = 0;
        for (int i = 0; i < text.Length; i++)
        {
            // _xHHHH_ is seven characters long.
            if (text[i] == '_' && i + 7 <= text.Length && text[i + 1] == 'x' && text[i + 6] == '_' && TryHex4(text.Slice(i + 2, 4), out char c))
            {
                text[w++] = c;
                i += 6;
            }
            else
            {
                text[w++] = text[i];
            }
        }

        return w;
    }

    private static bool TryHex4(ReadOnlySpan<char> s, out char c)
    {
        int v = 0;
        foreach (char h in s)
        {
            int d = h switch
            {
                >= '0' and <= '9' => h - '0',
                >= 'a' and <= 'f' => h - 'a' + 10,
                >= 'A' and <= 'F' => h - 'A' + 10,
                _ => -1,
            };
            if (d < 0)
            {
                c = '\0';
                return false;
            }

            v = (v << 4) | d;
        }

        c = (char)v;
        return true;
    }

    /// <summary>Decodes UTF-8 text into a new string with escapes resolved.</summary>
    public static string ToString(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return string.Empty;
        }

        if (utf8.IndexOf("_x"u8) < 0)
        {
            return Encoding.UTF8.GetString(utf8);
        }

        char[] tmp = new char[Encoding.UTF8.GetMaxCharCount(utf8.Length)];
        int n = Encoding.UTF8.GetChars(utf8, tmp);
        n = DecodeEscapes(tmp.AsSpan(0, n));
        return new string(tmp, 0, n);
    }
}
