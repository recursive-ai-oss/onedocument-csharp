using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>Reading Markdown files: character encoding, front matter and line positions.</summary>
internal static class MarkdownText
{
    /// <summary>
    /// Decodes the file. A UTF-8 or UTF-16 byte-order mark decides; without one the bytes are UTF-8. Bytes that are not valid UTF-8 become U+FFFD and the
    /// repair is reported as <c>markdown.encoding-replaced</c>.
    /// </summary>
    public static string Decode(byte[] bytes, OpenContext context, string location)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip);
        }
        catch (DecoderFallbackException ex)
        {
            context.Defects.Report(new Defect(DefectKind.Encoding, "markdown.encoding-replaced", DefectSeverity.Repaired,
                "The bytes are not valid UTF-8; the invalid ones were replaced by U+FFFD.", DefectLocation.InRecord(location, ex.Index)));
            return new UTF8Encoding(false, false).GetString(bytes, skip, bytes.Length - skip);
        }
    }

    /// <summary>The character offset at which a 1-based line starts (the end of the text when there are fewer lines).</summary>
    public static long LineOffset(string text, int line)
    {
        int offset = 0;
        for (int l = 1; l < line; l++)
        {
            int i = text.IndexOfAny(['\n', '\r'], offset);
            if (i < 0)
            {
                return text.Length;
            }

            offset = i + 1 < text.Length && text[i] == '\r' && text[i + 1] == '\n' ? i + 2 : i + 1;
        }

        return offset;
    }

    /// <summary>
    /// Takes a front matter block off the start of the text: a first line of three hyphens, lines of <c>key: value</c>, and a line of three hyphens (or three
    /// dots). Title, author, subject, description and keywords become document properties; every other line is reported as lost. Text without the
    /// closing line is not front matter and comes back unchanged.
    /// </summary>
    public static string SplitFrontMatter(string text, Dictionary<string, string> properties, LossReport loss)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return text;
        }

        int lineEnd = NextLineEnd(text, 0);
        if (text.AsSpan(0, lineEnd).TrimEnd().Length != 3)
        {
            return text;
        }

        int pos = AfterLineEnd(text, lineEnd);
        var entries = new List<string>();
        while (pos < text.Length)
        {
            int end = NextLineEnd(text, pos);
            string line = text.Substring(pos, end - pos).TrimEnd();
            pos = AfterLineEnd(text, end);
            if (line is "---" or "...")
            {
                // Only text that looks like YAML is front matter; two thematic breaks around Markdown are not.
                if (entries.Count == 0 || !entries.All(IsYamlLine))
                {
                    return text;
                }

                ApplyFrontMatter(entries, properties, loss);
                return text[pos..];
            }

            entries.Add(line);
        }

        return text;
    }

    // A mapping key and colon at the start of a line, or a continuation (indented, or a list item), or a blank line.
    private static bool IsYamlLine(string line)
    {
        if (line.Trim().Length == 0 || line[0] is ' ' or '\t' || line.StartsWith("- ", StringComparison.Ordinal))
        {
            return true;
        }

        int colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return false;
        }

        for (int i = 0; i < colon; i++)
        {
            if (!(char.IsLetterOrDigit(line[i]) || line[i] is '_' or '-' or ' ' or '.'))
            {
                return false;
            }
        }

        return colon + 1 == line.Length || line[colon + 1] is ' ' or '\t';
    }

    private static int NextLineEnd(string text, int from)
    {
        int i = text.AsSpan(from).IndexOfAny('\n', '\r');
        return i < 0 ? text.Length : from + i;
    }

    private static int AfterLineEnd(string text, int lineEnd)
    {
        if (lineEnd >= text.Length)
        {
            return text.Length;
        }

        return text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
    }

    private static void ApplyFrontMatter(List<string> lines, Dictionary<string, string> properties, LossReport loss)
    {
        foreach (string line in lines)
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || char.IsWhiteSpace(line[0]))
            {
                // A continuation line, a list item or nested data: not read.
                loss.Report("loss.markdown-front-matter", "front matter", 1, "Front matter lines other than simple key: value pairs for title, author, subject, description and keywords were not read.");
                continue;
            }

            string key = line[..colon].Trim().ToLowerInvariant();
            string value = Unquote(line[(colon + 1)..].Trim());
            string? target = key switch
            {
                "title" => "title",
                "author" or "authors" or "creator" => "author",
                "subject" => "subject",
                "description" or "summary" or "abstract" => "description",
                "keywords" or "tags" => "keywords",
                _ => null,
            };
            if (target is null || value.Length == 0)
            {
                loss.Report("loss.markdown-front-matter", "front matter", 1, "Front matter lines other than simple key: value pairs for title, author, subject, description and keywords were not read.");
                continue;
            }

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                value = string.Join(", ", value[1..^1].Split(',').Select(static v => Unquote(v.Trim())).Where(static v => v.Length > 0));
            }

            properties.TryAdd(target, value);
        }
    }

    private static string Unquote(string v) =>
        v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')) ? v[1..^1] : v;
}
