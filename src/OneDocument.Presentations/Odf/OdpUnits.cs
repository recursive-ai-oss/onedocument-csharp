using System.Globalization;
using RecursiveAI.OneDocument.Ppt.Binary;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// Lengths, colours and text escapes shared by the OpenDocument presentation importer and exporter.
internal static class OdpUnits
{
    // OpenDocument 1.3 Part 1 §18.3 (length): a number followed by cm, mm, in, pt, pc or px. DrawingML counts in EMU (914,400 to the inch).
    public static bool TryPoints(string? value, out double points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> s = value.AsSpan().Trim();
        int i = 0;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] is '.' or '-' or '+'))
        {
            i++;
        }

        if (i == 0 || !double.TryParse(s[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n))
        {
            return false;
        }

        ReadOnlySpan<char> unit = s[i..].Trim();
        double factor;
        if (unit.SequenceEqual("pt"))
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
        else if (unit.IsEmpty)
        {
            factor = 1;
        }
        else
        {
            return false;
        }

        points = n * factor;
        return true;
    }

    public static bool TryEmu(string? value, out long emu)
    {
        emu = 0;
        if (!TryPoints(value, out double pt))
        {
            return false;
        }

        emu = (long)Math.Clamp(Math.Round(pt * 12700), -(1L << 40), 1L << 40);
        return true;
    }

    public static long Emu(string? value, long fallback = 0) => TryEmu(value, out long e) ? e : fallback;

    public static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static string Esc(string? s) => PptXml.Escape(s);

    // OpenDocument lengths written from EMU: centimetres with up to four decimals, which is finer than an EMU needs to survive a round trip.
    public static string Cm(long emu) => (emu / 360000.0).ToString("0.####", CultureInfo.InvariantCulture) + "cm";

    public static string Pt(double points) => points.ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    // #rrggbb to the six upper-case hex digits DrawingML wants; null for transparent or anything that is not a colour.
    public static string? Color(string? value)
    {
        if (value is { Length: 7 } && value[0] == '#' && int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
        {
            return value[1..].ToUpperInvariant();
        }

        return null;
    }

    // "50%" to 0.5; null when the value is not a percentage.
    public static double? Percent(string? value)
    {
        if (value is { Length: > 1 } && value[^1] == '%' && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
        {
            return v / 100;
        }

        return null;
    }

    // Style names in an OpenDocument file encode characters as _HH_ (hex): "Title_20_Slide" is "Title Slide".
    public static string DecodeStyleName(string name)
    {
        if (!name.Contains('_', StringComparison.Ordinal))
        {
            return name;
        }

        var sb = new System.Text.StringBuilder(name.Length);
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

    public static string EncodeStyleName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.')
            {
                sb.Append(c);
            }
            else if (c == '_')
            {
                sb.Append("_5f_");
            }
            else if (c < 256)
            {
                sb.Append('_').Append(((int)c).ToString("x2", CultureInfo.InvariantCulture)).Append('_');
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.Length > 0 && !(char.IsLetter(sb[0]) || sb[0] == '_') ? "S" + sb : sb.ToString();
    }
}
