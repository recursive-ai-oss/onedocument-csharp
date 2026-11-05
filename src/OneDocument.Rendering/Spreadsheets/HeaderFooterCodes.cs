using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>The values the field codes of a header or footer stand for.</summary>
internal sealed record HeaderFooterFields(int Page, int Pages, string Date, string Time, string File, string Path, string Sheet);

/// <summary>One part of a header or footer: left, centre or right, as runs of text.</summary>
internal sealed class HeaderFooterSection
{
    public List<(string Text, TextStyle Style)> Runs { get; } = [];

    public bool HasPicture;

    public bool IsEmpty => Runs.Count == 0 && !HasPicture;
}

/// <summary>Reads the code strings of <c>headerFooter</c> elements.</summary>
internal static class HeaderFooterCodes
{
    /// <summary>Parses a header or footer string into its left, centre and right sections.</summary>
    /// <remarks>
    /// ECMA-376 Part 1 §18.3.1.39 (headerFooter): <c>&amp;L</c>, <c>&amp;C</c>, <c>&amp;R</c> start the sections; <c>&amp;P</c> page number, <c>&amp;N</c> page count,
    /// <c>&amp;D</c> date, <c>&amp;T</c> time, <c>&amp;F</c> file name, <c>&amp;Z</c> path, <c>&amp;A</c> sheet name, <c>&amp;G</c> picture, <c>&amp;"font,style"</c> font,
    /// <c>&amp;nn</c> size, <c>&amp;K</c> colour, <c>&amp;B &amp;I &amp;U &amp;E &amp;S &amp;X &amp;Y</c> bold, italic, underline, double underline, strike, superscript, subscript, <c>&amp;&amp;</c> an ampersand.
    /// </remarks>
    public static HeaderFooterSection[] Parse(string? code, TextStyle baseStyle, HeaderFooterFields fields)
    {
        HeaderFooterSection[] sections = [new(), new(), new()];
        if (string.IsNullOrEmpty(code))
        {
            return sections;
        }

        int current = 0; // the default section is the centre in Excel's UI, but a string without a section code starts at the left
        TextStyle style = baseStyle;
        var text = new StringBuilder();

        void Flush()
        {
            if (text.Length > 0)
            {
                sections[current].Runs.Add((text.ToString(), style));
                text.Clear();
            }
        }

        for (int i = 0; i < code.Length; i++)
        {
            char ch = code[i];
            if (ch != '&' || i + 1 >= code.Length)
            {
                text.Append(ch == '\r' ? '\n' : ch);
                continue;
            }

            char n = code[++i];
            switch (n)
            {
                case 'L': Flush(); current = 0; break;
                case 'C': Flush(); current = 1; break;
                case 'R': Flush(); current = 2; break;
                case '&': text.Append('&'); break;
                case 'P': i = Number(code, i, fields.Page, text); break;
                case 'N': i = Number(code, i, fields.Pages, text); break;
                case 'D': text.Append(fields.Date); break;
                case 'T': text.Append(fields.Time); break;
                case 'F': text.Append(fields.File); break;
                case 'Z': text.Append(fields.Path); break;
                case 'A': text.Append(fields.Sheet); break;
                case 'G': Flush(); sections[current].HasPicture = true; break;
                case 'B': Flush(); style = style with { Bold = !style.Bold }; break;
                case 'I': Flush(); style = style with { Italic = !style.Italic }; break;
                case 'U': Flush(); style = style with { Underline = style.Underline == FontUnderline.Straight ? FontUnderline.None : FontUnderline.Straight }; break;
                case 'E': Flush(); style = style with { Underline = style.Underline == FontUnderline.Doubled ? FontUnderline.None : FontUnderline.Doubled }; break;
                case 'S': Flush(); style = style with { Strike = !style.Strike }; break;
                case 'X': Flush(); style = style with { Vertical = style.Vertical == FontVerticalAlignment.Superscript ? FontVerticalAlignment.Baseline : FontVerticalAlignment.Superscript }; break;
                case 'Y': Flush(); style = style with { Vertical = style.Vertical == FontVerticalAlignment.Subscript ? FontVerticalAlignment.Baseline : FontVerticalAlignment.Subscript }; break;
                case '"':
                    {
                        int end = code.IndexOf('"', i + 1);
                        if (end < 0)
                        {
                            end = code.Length;
                        }

                        Flush();
                        string spec = code[(i + 1)..end];
                        int comma = spec.IndexOf(',', StringComparison.Ordinal);
                        string family = comma < 0 ? spec : spec[..comma];
                        string face = comma < 0 ? string.Empty : spec[(comma + 1)..];
                        style = style with
                        {
                            Family = family.Length > 0 ? family : style.Family,
                            Bold = face.Contains("Bold", StringComparison.OrdinalIgnoreCase),
                            Italic = face.Contains("Italic", StringComparison.OrdinalIgnoreCase) || face.Contains("Oblique", StringComparison.OrdinalIgnoreCase),
                        };
                        i = end;
                        break;
                    }

                case 'K':
                    {
                        // &Krrggbb. The theme form (&Knn+ttt) is read past and keeps the current colour.
                        Flush();
                        if (i + 6 < code.Length + 0 && uint.TryParse(code.AsSpan(i + 1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                        {
                            style = style with { Rgb = rgb };
                            i += 6;
                        }
                        else if (i + 6 < code.Length && code[i + 3] == '+')
                        {
                            i += 6;
                        }

                        break;
                    }

                case >= '0' and <= '9':
                    {
                        int j = i;
                        while (j < code.Length && j - i < 2 && char.IsAsciiDigit(code[j]))
                        {
                            j++;
                        }

                        Flush();
                        if (double.TryParse(code.AsSpan(i, j - i), NumberStyles.Integer, CultureInfo.InvariantCulture, out double size) && size > 0)
                        {
                            style = style with { Size = size };
                        }

                        i = j - 1;
                        break;
                    }

                case '[':
                    {
                        int end = code.IndexOf(']', i);
                        if (end > i)
                        {
                            string tag = code[(i + 1)..end];
                            text.Append(tag switch { "Page" => fields.Page.ToString(CultureInfo.InvariantCulture), "Pages" => fields.Pages.ToString(CultureInfo.InvariantCulture), "Date" => fields.Date, "Time" => fields.Time, "File" => fields.File, "Path" => fields.Path, "Tab" => fields.Sheet, _ => string.Empty });
                            if (tag == "Picture")
                            {
                                Flush();
                                sections[current].HasPicture = true;
                            }

                            i = end;
                        }

                        break;
                    }

                default:
                    // An unknown code is dropped with its letter.
                    break;
            }
        }

        Flush();
        return sections;
    }

    // &P+1 and &P-2 add an offset to the number.
    private static int Number(string code, int i, int value, StringBuilder text)
    {
        if (i + 2 < code.Length && (code[i + 1] is '+' or '-') && char.IsAsciiDigit(code[i + 2]))
        {
            int j = i + 2;
            while (j < code.Length && char.IsAsciiDigit(code[j]))
            {
                j++;
            }

            if (int.TryParse(code.AsSpan(i + 1, j - i - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int offset))
            {
                value += offset;
                i = j - 1;
            }
        }

        text.Append(value.ToString(CultureInfo.InvariantCulture));
        return i;
    }
}
