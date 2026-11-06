using System.Text;

namespace RecursiveAI.OneDocument.Redaction;

// Ways a string is stored that a reader decodes before it sees it: SpreadsheetML's _xHHHH_ escapes and a whole value held as base64.
internal static class EncodedText
{
    // ECMA-376 Part 1 §22.4.2.4 ST_Xstring: _xHHHH_ is the UTF-16 code unit HHHH; a literal "_x" followed by four hex digits and an underscore is
    // written with its first underscore escaped as _x005F_.
    public static string DecodeX(string text)
    {
        if (!text.Contains("_x", StringComparison.Ordinal))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '_' && i + 6 < text.Length && IsEscape(text, i))
            {
                sb.Append((char)Convert.ToInt32(text.AsSpan(i + 2, 4).ToString(), 16));
                i += 6;
                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static bool IsEscape(string text, int i) =>
        i + 6 < text.Length && text[i + 1] == 'x' && text[i + 6] == '_' && char.IsAsciiHexDigit(text[i + 2]) && char.IsAsciiHexDigit(text[i + 3]) && char.IsAsciiHexDigit(text[i + 4]) && char.IsAsciiHexDigit(text[i + 5]);

    // The inverse for text that was decoded and then edited: control characters XML cannot hold are escaped again, and a literal that looks like
    // an escape gets its underscore escaped.
    public static string EncodeX(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c < 0x20 && c is not ('\t' or '\n')) || c is '\uFFFE' or '\uFFFF')
            {
                sb.Append("_x").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)).Append('_');
            }
            else if (c == '_' && IsEscape(text, i))
            {
                sb.Append("_x005F_");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // A whole value that is base64: at least eight characters of the alphabet (white space allowed between them) in a multiple of four.
    public static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length < 8)
        {
            return false;
        }

        var compact = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=')
            {
                compact.Append(c);
            }
            else if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        if (compact.Length < 8 || compact.Length % 4 != 0)
        {
            return false;
        }

        var buffer = new byte[compact.Length / 4 * 3];
        if (!Convert.TryFromBase64String(compact.ToString(), buffer, out int written))
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

    // Writes the new bytes back in the old layout: the same white space in the same places.
    public static string EncodeBase64Like(string original, byte[] bytes)
    {
        string encoded = Convert.ToBase64String(bytes);
        var sb = new StringBuilder(original.Length);
        int next = 0;
        foreach (char c in original)
        {
            if (char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
            else if (next < encoded.Length)
            {
                sb.Append(encoded[next++]);
            }
        }

        return sb.ToString();
    }

    // Decoded bytes that are mostly printable text (ASCII or UTF-16) so that a pattern, which could match anything, is only run on real text.
    public static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return false;
        }

        int covered = 0;
        foreach (BinaryText.Run run in BinaryText.Runs(bytes))
        {
            covered += run.Text.Length * run.Stride;
        }

        return covered * 10 >= bytes.Length * 8;
    }
}
