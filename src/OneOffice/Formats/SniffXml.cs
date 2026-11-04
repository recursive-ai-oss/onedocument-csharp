using System.Text;

namespace RecursiveAI.OneOffice.Formats;

/// <summary>
/// A tiny scanner for the start tags of an XML text, used only by format sniffing to read a few attributes from the top of a file
/// (the root element, a relationship's target, a part's content type). It is deliberately not an XML parser: it skips comments,
/// processing instructions, CDATA and the DOCTYPE, never expands entities beyond the five predefined ones, and tolerates a truncated
/// text. The real tokenizer, with its limits, is the markup layer's.
/// </summary>
internal static class SniffXml
{
    internal readonly record struct Tag(string Name, IReadOnlyList<KeyValuePair<string, string>> Attributes)
    {
        public string LocalName
        {
            get
            {
                int i = Name.IndexOf(':', StringComparison.Ordinal);
                return i < 0 ? Name : Name[(i + 1)..];
            }
        }

        public string? Get(string name)
        {
            foreach (KeyValuePair<string, string> a in Attributes)
            {
                if (a.Key == name)
                {
                    return a.Value;
                }
            }

            return null;
        }

        public string? GetByLocalName(string local)
        {
            foreach (KeyValuePair<string, string> a in Attributes)
            {
                int i = a.Key.IndexOf(':', StringComparison.Ordinal);
                if ((i < 0 ? a.Key : a.Key[(i + 1)..]) == local)
                {
                    return a.Value;
                }
            }

            return null;
        }
    }

    /// <summary>Decodes a prefix as text by its byte order mark (UTF-8 if none). Returns null when it does not look like text.</summary>
    internal static string? DecodeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }

        // Text has no NUL bytes; this is the cheapest reliable test that a prefix is not binary.
        return bytes.Contains((byte)0) ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Lists the start tags (and empty-element tags) of the text in order, skipping everything else.</summary>
    internal static IEnumerable<Tag> StartTags(string text)
    {
        int i = 0;
        while (true)
        {
            i = text.IndexOf('<', i);
            if (i < 0 || i + 1 >= text.Length)
            {
                yield break;
            }

            char c = text[i + 1];
            if (c == '?')
            {
                i = SkipTo(text, i + 2, "?>");
            }
            else if (c == '!')
            {
                if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
                {
                    i = SkipTo(text, i + 4, "-->");
                }
                else if (string.CompareOrdinal(text, i, "<![CDATA[", 0, 9) == 0)
                {
                    i = SkipTo(text, i + 9, "]]>");
                }
                else
                {
                    i = SkipDoctype(text, i + 2);
                }
            }
            else if (c == '/')
            {
                i = text.IndexOf('>', i + 2);
            }
            else
            {
                int end = ParseTag(text, i + 1, out Tag tag);
                if (end < 0)
                {
                    yield break;
                }

                yield return tag;
                i = end;
            }

            if (i < 0)
            {
                yield break;
            }
        }
    }

    /// <summary>Gets the text of the first processing instruction named <paramref name="target"/>, or null.</summary>
    internal static string? ProcessingInstruction(string text, string target)
    {
        int i = 0;
        while ((i = text.IndexOf("<?", i, StringComparison.Ordinal)) >= 0)
        {
            int end = text.IndexOf("?>", i + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                return null;
            }

            string body = text[(i + 2)..end];
            if (body.StartsWith(target, StringComparison.Ordinal) && (body.Length == target.Length || char.IsWhiteSpace(body[target.Length])))
            {
                return body[target.Length..].Trim();
            }

            i = end + 2;
        }

        return null;
    }

    private static int SkipTo(string text, int from, string terminator)
    {
        int i = text.IndexOf(terminator, from, StringComparison.Ordinal);
        return i < 0 ? -1 : i + terminator.Length;
    }

    // A DOCTYPE may hold an internal subset in [...]; skip to the closing '>' outside it.
    private static int SkipDoctype(string text, int from)
    {
        int depth = 0;
        for (int i = from; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
            }
            else if (c == '>' && depth <= 0)
            {
                return i + 1;
            }
        }

        return -1;
    }

    // Parses "name attr='v' attr2="v2" ... [/]>" starting after '<'. Returns the index after '>', or -1 if the text ends first.
    private static int ParseTag(string text, int i, out Tag tag)
    {
        tag = default;
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/')
        {
            i++;
        }

        string name = text[start..i];
        var attrs = new List<KeyValuePair<string, string>>();
        while (i < text.Length)
        {
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == '/'))
            {
                i++;
            }

            if (i >= text.Length)
            {
                return -1;
            }

            if (text[i] == '>')
            {
                tag = new Tag(name, attrs);
                return i + 1;
            }

            int nameStart = i;
            while (i < text.Length && text[i] != '=' && text[i] != '>' && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            string attrName = text[nameStart..i];
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i < text.Length && text[i] == '=')
            {
                i++;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                if (i >= text.Length)
                {
                    return -1;
                }

                char quote = text[i];
                if (quote is '"' or '\'')
                {
                    int close = text.IndexOf(quote, i + 1);
                    if (close < 0)
                    {
                        return -1;
                    }

                    attrs.Add(new(attrName, Unescape(text[(i + 1)..close])));
                    i = close + 1;
                }
                else
                {
                    // An unquoted value is not XML; take it up to a space or the end of the tag, as a lenient reader would.
                    int vs = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>')
                    {
                        i++;
                    }

                    attrs.Add(new(attrName, text[vs..i]));
                }
            }
            else if (attrName.Length > 0)
            {
                attrs.Add(new(attrName, string.Empty));
            }
        }

        return -1;
    }

    private static string Unescape(string value) =>
        value.Contains('&', StringComparison.Ordinal)
            ? value.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal)
                .Replace("&quot;", "\"", StringComparison.Ordinal).Replace("&apos;", "'", StringComparison.Ordinal)
                .Replace("&amp;", "&", StringComparison.Ordinal)
            : value;
}
