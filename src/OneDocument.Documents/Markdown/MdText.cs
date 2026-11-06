using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using OneHtmlDocument = RecursiveAI.OneHtml.Document;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>Backslash escapes, character references and reference-label normalisation (CommonMark 0.31.2 §2.4, §2.5, §6.1).</summary>
internal static class MdText
{
    private static readonly ConcurrentDictionary<string, string?> s_named = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a named character reference such as <c>&amp;copy;</c> (name without the ampersand, semicolon included) through the HTML parser's
    /// table, so the whole HTML5 set is known without a second copy of it. Unknown names give null.
    /// </summary>
    private static string? Named(string nameWithSemicolon)
    {
        if (s_named.TryGetValue(nameWithSemicolon, out string? cached))
        {
            return cached;
        }

        string? value = null;
        try
        {
            var doc = OneHtmlDocument.Parse("<!DOCTYPE html><body>&" + nameWithSemicolon);
            var body = doc.Root.QuerySelector("body");
            string text = body.IsNull ? string.Empty : body.TextContent ?? string.Empty;
            // An unknown name stays as the text "&name;"; a known one is replaced by its characters (which may themselves be '&' or '<').
            if (text.Length > 0 && text != "&" + nameWithSemicolon)
            {
                value = text;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            value = null;
        }

        if (s_named.Count < 4096)
        {
            s_named[nameWithSemicolon] = value;
        }

        return value;
    }

    /// <summary>Tries to read a character reference at <paramref name="pos"/> (the '&amp;').</summary>
    public static bool TryEntity(string s, int pos, out string value, out int end)
    {
        value = string.Empty;
        end = pos;
        int i = pos + 1;
        if (i >= s.Length)
        {
            return false;
        }

        if (s[i] == '#')
        {
            i++;
            bool hex = i < s.Length && s[i] is 'x' or 'X';
            if (hex)
            {
                i++;
            }

            int digitsStart = i;
            long code = 0;
            while (i < s.Length && (hex ? char.IsAsciiHexDigit(s[i]) : char.IsAsciiDigit(s[i])) && i - digitsStart < 8)
            {
                code = code * (hex ? 16 : 10) + (hex ? Convert.ToInt32(s[i].ToString(), 16) : s[i] - '0');
                i++;
            }

            int count = i - digitsStart;
            if (count == 0 || count > (hex ? 6 : 7) || i >= s.Length || s[i] != ';')
            {
                return false;
            }

            // §2.5: invalid code points, surrogates and NUL become U+FFFD.
            value = code is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF) ? "\uFFFD" : char.ConvertFromUtf32((int)code);
            end = i + 1;
            return true;
        }

        int nameStart = i;
        while (i < s.Length && char.IsAsciiLetterOrDigit(s[i]) && i - nameStart < 32)
        {
            i++;
        }

        if (i == nameStart || i >= s.Length || s[i] != ';')
        {
            return false;
        }

        string? v = Named(s.Substring(nameStart, i - nameStart + 1));
        if (v is null)
        {
            return false;
        }

        value = v;
        end = i + 1;
        return true;
    }

    /// <summary>Removes backslash escapes of ASCII punctuation and resolves character references.</summary>
    public static string Unescape(string s)
    {
        if (s.IndexOf('\\', StringComparison.Ordinal) < 0 && s.IndexOf('&', StringComparison.Ordinal) < 0)
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length && MdScan.IsAsciiPunctuation(s[i + 1]))
            {
                sb.Append(s[i + 1]);
                i += 2;
            }
            else if (c == '&' && TryEntity(s, i, out string v, out int end))
            {
                sb.Append(v);
                i = end;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// §4.7: a label matches another after stripping the outer brackets and white space, collapsing inner white space and Unicode case
    /// folding. The fold is lower then upper case invariant, with ß and ẞ spelled out as "ss".
    /// </summary>
    public static string NormalizeLabel(string label)
    {
        var sb = new StringBuilder(label.Length);
        bool space = false;
        foreach (char ch in label)
        {
            if (ch is ' ' or '\t' or '\n' or '\r')
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            if (ch is '\u00DF' or '\u1E9E')
            {
                sb.Append("SS");
            }
            else
            {
                sb.Append(char.ToUpperInvariant(char.ToLowerInvariant(ch)));
            }
        }

        return sb.ToString();
    }

    /// <summary>Percent-encodes a link destination the way a browser-bound renderer should (existing %XX kept, spaces and non-ASCII encoded, backslash encoded).</summary>
    public static string EncodeUri(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsAsciiLetterOrDigit(c) || ";/?:@&=+$,-_.!~*'()#".Contains(c, StringComparison.Ordinal))
            {
                sb.Append(c);
            }
            else if (c == '%' && i + 2 < s.Length && char.IsAsciiHexDigit(s[i + 1]) && char.IsAsciiHexDigit(s[i + 2]))
            {
                sb.Append(c);
            }
            else
            {
                int len = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
                foreach (byte b in Encoding.UTF8.GetBytes(len == 2 ? s.Substring(i, 2) : (char.IsSurrogate(c) ? "\uFFFD" : c.ToString())))
                {
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }

                i += len - 1;
            }
        }

        return sb.ToString();
    }
}
