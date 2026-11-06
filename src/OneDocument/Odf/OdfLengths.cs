using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Odf;

/// <summary>
/// Lengths, style-name encoding and attribute escaping shared by the OpenDocument readers and writers of every kind.
/// </summary>
public static class OdfLengths
{
    /// <summary>Reads an OpenDocument length (OpenDocument 1.3 Part 1, Data types: a number followed by cm, mm, in, pt, pc or px) as points.</summary>
    /// <param name="value">The attribute text; a bare number reads as points.</param>
    /// <param name="points">The length in points (1/72 inch).</param>
    /// <returns>False when the text is empty, not a number, not finite or has an unknown unit (percentages included).</returns>
    public static bool TryPoints(string? value, out double points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> s = value.AsSpan().Trim();
        int i = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            i++;
        }

        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
        {
            i++;
        }

        if (i == 0 || !double.TryParse(s[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n))
        {
            return false;
        }

        ReadOnlySpan<char> unit = s[i..].Trim();
        double factor;
        if (unit.SequenceEqual("pt") || unit.IsEmpty)
        {
            factor = 1;
        }
        else if (unit.SequenceEqual("cm"))
        {
            factor = 72 / 2.54;
        }
        else if (unit.SequenceEqual("mm"))
        {
            factor = 72 / 25.4;
        }
        else if (unit.SequenceEqual("in"))
        {
            factor = 72;
        }
        else if (unit.SequenceEqual("pc"))
        {
            factor = 12;
        }
        else if (unit.SequenceEqual("px"))
        {
            factor = 0.75;
        }
        else
        {
            return false;
        }

        points = n * factor;
        return true;
    }

    /// <summary>Writes points as a centimetre length with at most four decimals.</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>For example <c>2.54cm</c>.</returns>
    public static string Cm(double points) => (points * 2.54 / 72).ToString("0.####", CultureInfo.InvariantCulture) + "cm";

    /// <summary>Writes points as a point length with at most two decimals.</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>For example <c>10.5pt</c>.</returns>
    public static string Pt(double points) => points.ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    /// <summary>
    /// Decodes a style name: OpenDocument encodes characters that XML names dislike as <c>_HH_</c> in hexadecimal (<c>Heading_20_1</c>).
    /// </summary>
    /// <param name="name">The encoded name.</param>
    /// <returns>The display form.</returns>
    public static string DecodeStyleName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!name.Contains('_', StringComparison.Ordinal))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '_' && i + 3 < name.Length && name[i + 3] == '_'
                && int.TryParse(name.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp))
            {
                sb.Append((char)cp);
                i += 3;
            }
            else
            {
                sb.Append(name[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>Encodes a display name as a style name: anything but letters, digits, <c>-</c> and <c>.</c> below U+0100 becomes <c>_HH_</c>.</summary>
    /// <param name="name">The display name.</param>
    /// <returns>The name for <c>style:name</c>.</returns>
    public static string EncodeStyleName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.')
            {
                sb.Append(c);
            }
            else if (c == '_' || c < 0x100)
            {
                sb.Append('_').Append(((int)c).ToString("x2", CultureInfo.InvariantCulture)).Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Escapes text for an XML attribute value or text node (<c>&amp; &lt; &gt; "</c>).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}
