using System.Text;

namespace RecursiveAI.OneDocument.TextExtraction;

/// <summary>What every Markdown writer in the family shares: escaping text so it reads back as itself, and the pipe table.</summary>
internal static class MarkdownWriting
{
    private static readonly System.Buffers.SearchValues<char> s_entityChars = System.Buffers.SearchValues.Create("#abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");

    /// <summary>Escapes what Markdown would read as markup: the characters that start emphasis, links, code or tags, and the starts of lines that read as block markers.</summary>
    public static string EscapeText(string text, bool inTable, bool lineStart)
    {
        var sb = new StringBuilder(text.Length + 8);
        int i = 0;
        if (lineStart)
        {
            // Block markers at the start of a line: #, bullet and number markers, setext underlines and indented code.
            while (i < text.Length && text[i] is ' ' or '\t')
            {
                i++;
            }

            if (i < text.Length)
            {
                char c = text[i];
                if (c == '#' || ((c is '-' or '+') && (i + 1 >= text.Length || text[i + 1] is ' ' or '-' or '+')) || (c == '=' && text.AsSpan(i).IndexOfAnyExcept('=') < 0))
                {
                    sb.Append('\\');
                }
                else if (char.IsAsciiDigit(c))
                {
                    int j = i;
                    while (j < text.Length && char.IsAsciiDigit(text[j]))
                    {
                        j++;
                    }

                    if (j < text.Length && text[j] is '.' or ')' && j - i <= 9)
                    {
                        sb.Append(text, i, j - i).Append('\\');
                        i = j;
                    }
                }
            }
        }

        for (; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\\' or '`' or '*' or '[' or ']' or '<' or '>' or '~':
                    sb.Append('\\').Append(c);
                    break;
                case '|':
                    sb.Append(inTable ? "\\|" : "|");
                    break;
                case '_':
                    {
                        bool prevWord = i > 0 && char.IsLetterOrDigit(text[i - 1]);
                        bool nextWord = i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]);
                        sb.Append(prevWord && nextWord ? "_" : "\\_");
                        break;
                    }

                case '&':
                    {
                        int semi = text.IndexOf(';', i);
                        bool entity = semi > i + 1 && semi - i <= 33 && text.AsSpan(i + 1, semi - i - 1).IndexOfAnyExcept(s_entityChars) < 0;
                        sb.Append(entity ? "\\&" : "&");
                        break;
                    }

                case '\t':
                    sb.Append(' ');
                    break;
                case '\n' or '\r':
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Writes a GitHub Flavored Markdown pipe table. The first row is the header. Cells must already be escaped and free of line ends.</summary>
    /// <param name="sb">Where the table goes; every line ends with a line feed.</param>
    /// <param name="rows">The rows, all of the same length, the header first.</param>
    /// <param name="alignments">Per column: 0 none, 1 left, 2 centre, 3 right.</param>
    public static void WriteTable(StringBuilder sb, IReadOnlyList<List<string>> rows, IReadOnlyList<int> alignments)
    {
        void Row(List<string> cells)
        {
            sb.Append('|');
            foreach (string c in cells)
            {
                sb.Append(' ').Append(c.Length == 0 ? " " : c).Append(" |");
            }

            sb.Append('\n');
        }

        Row(rows[0]);
        sb.Append('|');
        foreach (int a in alignments)
        {
            sb.Append(a switch { 1 => " :--- |", 2 => " :---: |", 3 => " ---: |", _ => " --- |" });
        }

        sb.Append('\n');
        for (int r = 1; r < rows.Count; r++)
        {
            Row(rows[r]);
        }
    }
}
