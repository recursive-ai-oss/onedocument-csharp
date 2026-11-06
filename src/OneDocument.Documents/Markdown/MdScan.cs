using System.Globalization;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>Character classes and the raw-HTML scanners shared by the block and inline parsers. Every scanner moves forward only, so each is linear in what it reads.</summary>
internal static class MdScan
{
    // CommonMark 0.31.2 §2.1: an ASCII punctuation character is one of !"#$%&'()*+,-./:;<=>?@[\]^_`{|}~.
    public static bool IsAsciiPunctuation(char c) => c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    // §2.1: a Unicode punctuation character is in general category P or S (0.31); ASCII punctuation counts as well.
    public static bool IsPunctuation(string s, int index)
    {
        char c = s[index];
        if (c < 0x80)
        {
            return IsAsciiPunctuation(c);
        }

        UnicodeCategory cat = char.GetUnicodeCategory(s, index);
        return cat is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;
    }

    // §2.1: Unicode whitespace is category Zs, tab, line feed, form feed and carriage return.
    public static bool IsUnicodeWhitespace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r' || (c > 0x7F && char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator);

    public static bool IsSpaceOrTab(char c) => c is ' ' or '\t';

    /// <summary>Index of the last character of a surrogate pair's start, so a code point before <paramref name="index"/> is read whole.</summary>
    public static int CodePointStartBefore(string s, int index)
    {
        int i = index - 1;
        if (i > 0 && char.IsLowSurrogate(s[i]) && char.IsHighSurrogate(s[i - 1]))
        {
            i--;
        }

        return i;
    }

    // ---- raw HTML (CommonMark 0.31.2 §6.6) ----------------------------------------------------------------------------------

    /// <summary>
    /// Remembers that a closer was not found from some position on, so the next opener of the same kind does not search to the end of the
    /// text again. Without it a text of many unclosed <c>&lt;!--</c> would take quadratic time.
    /// </summary>
    public sealed class HtmlMemo
    {
        public int NoCommentClose = int.MaxValue;
        public int NoProcessingClose = int.MaxValue;
        public int NoCdataClose = int.MaxValue;
        public int NoDeclarationClose = int.MaxValue;
    }

    /// <summary>Tries to read an open tag, closing tag, comment, processing instruction, declaration or CDATA section that starts with '&lt;' at <paramref name="pos"/>.</summary>
    public static bool TryScanHtmlTag(string s, int pos, int limit, HtmlMemo memo, out int end)
    {
        end = pos;
        if (pos + 2 >= limit || s[pos] != '<')
        {
            return false;
        }

        char c = s[pos + 1];
        if (char.IsAsciiLetter(c))
        {
            return TryScanOpenTag(s, pos, limit, out end);
        }

        switch (c)
        {
            case '/':
                return TryScanCloseTag(s, pos, limit, out end);
            case '!':
                if (pos + 3 < limit && s[pos + 2] == '-' && s[pos + 3] == '-')
                {
                    // 0.31: "<!-->" and "<!--->" are comments; otherwise "<!--", text without "-->", then "-->".
                    if (pos + 4 < limit && s[pos + 4] == '>')
                    {
                        end = pos + 5;
                        return true;
                    }

                    if (pos + 5 < limit && s[pos + 4] == '-' && s[pos + 5] == '>')
                    {
                        end = pos + 6;
                        return true;
                    }

                    if (pos + 4 >= memo.NoCommentClose)
                    {
                        return false;
                    }

                    int i = s.IndexOf("-->", pos + 4, limit - pos - 4, StringComparison.Ordinal);
                    if (i < 0)
                    {
                        memo.NoCommentClose = Math.Min(memo.NoCommentClose, pos + 4);
                        return false;
                    }

                    end = i + 3;
                    return true;
                }

                if (pos + 8 < limit && string.CompareOrdinal(s, pos + 2, "[CDATA[", 0, 7) == 0)
                {
                    if (pos + 9 >= memo.NoCdataClose)
                    {
                        return false;
                    }

                    int i = s.IndexOf("]]>", pos + 9, limit - pos - 9, StringComparison.Ordinal);
                    if (i < 0)
                    {
                        memo.NoCdataClose = Math.Min(memo.NoCdataClose, pos + 9);
                        return false;
                    }

                    end = i + 3;
                    return true;
                }

                if (char.IsAsciiLetter(s[pos + 2]))
                {
                    // <! then letters, white space, then anything but '>' up to '>'.
                    int i = pos + 2;
                    while (i < limit && char.IsAsciiLetter(s[i]))
                    {
                        i++;
                    }

                    if (i >= limit || !IsHtmlWhitespace(s[i]))
                    {
                        return false;
                    }

                    if (i >= memo.NoDeclarationClose)
                    {
                        return false;
                    }

                    int gt = s.IndexOf('>', i, limit - i);
                    if (gt < 0)
                    {
                        memo.NoDeclarationClose = Math.Min(memo.NoDeclarationClose, i);
                        return false;
                    }

                    end = gt + 1;
                    return true;
                }

                return false;
            case '?':
                {
                    if (pos + 2 >= memo.NoProcessingClose)
                    {
                        return false;
                    }

                    int i = s.IndexOf("?>", pos + 2, limit - pos - 2, StringComparison.Ordinal);
                    if (i < 0)
                    {
                        memo.NoProcessingClose = Math.Min(memo.NoProcessingClose, pos + 2);
                        return false;
                    }

                    end = i + 2;
                    return true;
                }

            default:
                return false;
        }
    }

    private static bool IsHtmlWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static bool IsTagNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-';

    private static bool IsAttributeNameStart(char c) => char.IsAsciiLetter(c) || c is '_' or ':';

    private static bool IsAttributeNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or ':' or '.' or '-';

    // §6.6: an open tag is "<", a tag name, attributes (white space, a name, optionally = and a value), optional white space, optional "/", ">".
    private static bool TryScanOpenTag(string s, int pos, int limit, out int end)
    {
        end = pos;
        int i = pos + 1;
        while (i < limit && IsTagNameChar(s[i]))
        {
            i++;
        }

        while (true)
        {
            int ws = i;
            while (i < limit && IsHtmlWhitespace(s[i]))
            {
                i++;
            }

            if (i >= limit)
            {
                return false;
            }

            if (s[i] == '>')
            {
                end = i + 1;
                return true;
            }

            if (s[i] == '/')
            {
                if (i + 1 < limit && s[i + 1] == '>')
                {
                    end = i + 2;
                    return true;
                }

                return false;
            }

            if (i == ws || !IsAttributeNameStart(s[i]))
            {
                return false;
            }

            while (i < limit && IsAttributeNameChar(s[i]))
            {
                i++;
            }

            // Optional value specification.
            int save = i;
            int j = i;
            while (j < limit && IsHtmlWhitespace(s[j]))
            {
                j++;
            }

            if (j < limit && s[j] == '=')
            {
                j++;
                while (j < limit && IsHtmlWhitespace(s[j]))
                {
                    j++;
                }

                if (j >= limit)
                {
                    return false;
                }

                char q = s[j];
                if (q is '"' or '\'')
                {
                    int close = s.IndexOf(q, j + 1, limit - j - 1);
                    if (close < 0)
                    {
                        return false;
                    }

                    i = close + 1;
                }
                else
                {
                    int k = j;
                    while (k < limit && s[k] > ' ' && s[k] is not ('"' or '\'' or '=' or '<' or '>' or '`'))
                    {
                        k++;
                    }

                    if (k == j)
                    {
                        return false;
                    }

                    i = k;
                }
            }
            else
            {
                i = save;
            }
        }
    }

    private static bool TryScanCloseTag(string s, int pos, int limit, out int end)
    {
        end = pos;
        int i = pos + 2;
        if (i >= limit || !char.IsAsciiLetter(s[i]))
        {
            return false;
        }

        while (i < limit && IsTagNameChar(s[i]))
        {
            i++;
        }

        while (i < limit && IsHtmlWhitespace(s[i]))
        {
            i++;
        }

        if (i < limit && s[i] == '>')
        {
            end = i + 1;
            return true;
        }

        return false;
    }

    // ---- HTML blocks (§4.6) ---------------------------------------------------------------------------------------------------

    private static readonly HashSet<string> s_blockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "base", "basefont", "blockquote", "body", "caption", "center", "col", "colgroup", "dd", "details", "dialog", "dir", "div", "dl", "dt",
        "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hr", "html", "iframe", "legend", "li",
        "link", "main", "menu", "menuitem", "nav", "noframes", "ol", "optgroup", "option", "p", "param", "search", "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead",
        "title", "tr", "track", "ul",
    };

    /// <summary>The HTML block start condition (1 to 7) the line meets, or 0. Condition 7 is only tried when <paramref name="mayStartType7"/> (it cannot interrupt a paragraph).</summary>
    public static int HtmlBlockStart(ReadOnlySpan<char> line, bool mayStartType7)
    {
        if (line.Length < 2 || line[0] != '<')
        {
            return 0;
        }

        // 1: <script, <pre, <textarea or <style followed by white space, '>' or the end of the line.
        foreach (string name in new[] { "script", "pre", "textarea", "style" })
        {
            if (line.Length >= name.Length + 1 && line.Slice(1, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase)
                && (line.Length == name.Length + 1 || IsHtmlWhitespace(line[name.Length + 1]) || line[name.Length + 1] == '>'))
            {
                return 1;
            }
        }

        if (line.StartsWith("<!--", StringComparison.Ordinal))
        {
            return 2;
        }

        if (line[1] == '?')
        {
            return 3;
        }

        if (line[1] == '!' && line.Length > 2 && char.IsAsciiLetter(line[2]))
        {
            return 4;
        }

        if (line.StartsWith("<![CDATA[", StringComparison.Ordinal))
        {
            return 5;
        }

        // 6: '<' or '</' followed by a block-level tag name, then white space, '>', '/>' or the end of the line.
        int i = line[1] == '/' ? 2 : 1;
        int nameStart = i;
        while (i < line.Length && char.IsAsciiLetterOrDigit(line[i]))
        {
            i++;
        }

        if (i > nameStart && s_blockTags.Contains(line[nameStart..i].ToString())
            && (i == line.Length || IsHtmlWhitespace(line[i]) || line[i] == '>' || (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '>')))
        {
            return 6;
        }

        // 7: a complete open or closing tag (not script, style, pre or textarea) alone on its line.
        if (mayStartType7)
        {
            string text = line.ToString();
            if (TryScanHtmlTag(text, 0, text.Length, new HtmlMemo(), out int end) && text[1] != '!' && text[1] != '?')
            {
                int k = end;
                while (k < text.Length && IsHtmlWhitespace(text[k]))
                {
                    k++;
                }

                if (k == text.Length)
                {
                    return 7;
                }
            }
        }

        return 0;
    }

    /// <summary>Whether the line ends HTML block types 1 to 5.</summary>
    public static bool HtmlBlockEnds(int type, ReadOnlySpan<char> line) => type switch
    {
        1 => line.IndexOf("</script>", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("</pre>", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("</textarea>", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("</style>", StringComparison.OrdinalIgnoreCase) >= 0,
        2 => line.IndexOf("-->", StringComparison.Ordinal) >= 0,
        3 => line.IndexOf("?>", StringComparison.Ordinal) >= 0,
        4 => line.IndexOf('>') >= 0,
        5 => line.IndexOf("]]>", StringComparison.Ordinal) >= 0,
        _ => false,
    };
}
