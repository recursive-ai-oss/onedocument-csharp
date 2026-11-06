using System.Globalization;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneDocument.Documents.Html;

/// <summary>Reads the CSS values the importer maps (lengths, colours, font sizes). Anything it cannot read is null, which means "leave it at the default".</summary>
internal static class HtmlCss
{
    private static readonly Dictionary<string, string> s_colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "000000", ["silver"] = "C0C0C0", ["gray"] = "808080", ["grey"] = "808080", ["white"] = "FFFFFF", ["maroon"] = "800000",
        ["red"] = "FF0000", ["purple"] = "800080", ["fuchsia"] = "FF00FF", ["magenta"] = "FF00FF", ["green"] = "008000", ["lime"] = "00FF00",
        ["olive"] = "808000", ["yellow"] = "FFFF00", ["navy"] = "000080", ["blue"] = "0000FF", ["teal"] = "008080", ["aqua"] = "00FFFF",
        ["cyan"] = "00FFFF", ["orange"] = "FFA500", ["pink"] = "FFC0CB", ["brown"] = "A52A2A", ["gold"] = "FFD700", ["indigo"] = "4B0082",
        ["violet"] = "EE82EE", ["lightgray"] = "D3D3D3", ["lightgrey"] = "D3D3D3", ["darkgray"] = "A9A9A9", ["darkgrey"] = "A9A9A9",
        ["darkred"] = "8B0000", ["darkgreen"] = "006400", ["darkblue"] = "00008B", ["lightblue"] = "ADD8E6", ["lightgreen"] = "90EE90",
        ["lightyellow"] = "FFFFE0", ["orangered"] = "FF4500", ["crimson"] = "DC143C", ["tomato"] = "FF6347", ["coral"] = "FF7F50",
        ["salmon"] = "FA8072", ["khaki"] = "F0E68C", ["beige"] = "F5F5DC", ["ivory"] = "FFFFF0", ["lavender"] = "E6E6FA", ["tan"] = "D2B48C",
        ["turquoise"] = "40E0D0", ["skyblue"] = "87CEEB", ["steelblue"] = "4682B4", ["royalblue"] = "4169E1", ["dodgerblue"] = "1E90FF",
        ["slategray"] = "708090", ["slategrey"] = "708090", ["dimgray"] = "696969", ["dimgrey"] = "696969", ["gainsboro"] = "DCDCDC",
        ["whitesmoke"] = "F5F5F5", ["firebrick"] = "B22222", ["forestgreen"] = "228B22", ["seagreen"] = "2E8B57", ["limegreen"] = "32CD32",
        ["chocolate"] = "D2691E", ["sienna"] = "A0522D", ["peru"] = "CD853F", ["wheat"] = "F5DEB3", ["linen"] = "FAF0E6", ["aliceblue"] = "F0F8FF",
        ["lightcoral"] = "F08080", ["hotpink"] = "FF69B4", ["deeppink"] = "FF1493", ["plum"] = "DDA0DD", ["orchid"] = "DA70D6", ["darkorange"] = "FF8C00",
        ["midnightblue"] = "191970", ["darkviolet"] = "9400D3", ["mediumblue"] = "0000CD", ["cornflowerblue"] = "6495ED", ["lightsteelblue"] = "B0C4DE",
    };

    /// <summary>Parses a CSS colour to RRGGBB, or null for transparent, currentcolor, system colours and what it cannot read.</summary>
    public static string? Color(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string v = value.Trim();
        if (v.StartsWith('#'))
        {
            string h = v[1..];
            if (h.Length is 3 or 4 && IsHex(h))
            {
                return string.Concat(char.ToUpperInvariant(h[0]), char.ToUpperInvariant(h[0]), char.ToUpperInvariant(h[1]), char.ToUpperInvariant(h[1]), char.ToUpperInvariant(h[2]), char.ToUpperInvariant(h[2]));
            }

            return h.Length is 6 or 8 && IsHex(h) ? h[..6].ToUpperInvariant() : null;
        }

        if (v.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) && v.IndexOf('(') is int open and > 0 && v.EndsWith(')'))
        {
            string[] parts = v[(open + 1)..^1].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return null;
            }

            int[] rgb = new int[3];
            for (int i = 0; i < 3; i++)
            {
                string p = parts[i];
                bool pct = p.EndsWith('%');
                if (!double.TryParse(pct ? p[..^1] : p, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    return null;
                }

                rgb[i] = (int)Math.Clamp(Math.Round(pct ? d * 2.55 : d), 0, 255);
            }

            if (parts.Length >= 4 && double.TryParse(parts[3].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha) && alpha == 0)
            {
                return null;
            }

            return rgb[0].ToString("X2", CultureInfo.InvariantCulture) + rgb[1].ToString("X2", CultureInfo.InvariantCulture) + rgb[2].ToString("X2", CultureInfo.InvariantCulture);
        }

        return s_colors.TryGetValue(v, out string? named) ? named : null;
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Converts a CSS length to points. em, ex and rem need <paramref name="emPoints"/>; percentages and unknown units give null.</summary>
    public static double? Points(string? value, double emPoints)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string v = value.Trim();
        int i = 0;
        while (i < v.Length && (char.IsAsciiDigit(v[i]) || v[i] is '.' or '-' or '+'))
        {
            i++;
        }

        if (i == 0 || !double.TryParse(v.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || double.IsNaN(n) || double.IsInfinity(n))
        {
            return null;
        }

        string unit = v[i..].Trim().ToLowerInvariant();
        return unit switch
        {
            "" => n == 0 ? 0 : null,
            "px" => n * 0.75,
            "pt" => n,
            "pc" => n * 12,
            "in" => n * 72,
            "cm" => n * 72 / 2.54,
            "mm" => n * 72 / 25.4,
            "q" => n * 72 / 101.6,
            "em" => n * emPoints,
            "rem" => n * 12,
            "ex" => n * emPoints * 0.5,
            "ch" => n * emPoints * 0.5,
            _ => null,
        };
    }

    public static int? Twips(string? value, double emPoints)
    {
        double? pt = Points(value, emPoints);
        return pt is null ? null : (int)Math.Clamp(Math.Round(pt.Value * 20), -31680, 31680);
    }

    /// <summary>The font size of a computed style in points, resolving keywords, relative sizes and percentages through the parents.</summary>
    public static double FontPoints(ComputedStyle? style)
    {
        if (style is null)
        {
            return 12;
        }

        string v = style.GetPropertyValue("font-size").Trim();
        double parent = style.Parent is null ? 12 : FontPoints(style.Parent);
        double result;
        switch (v)
        {
            case "xx-small": result = 9 * 0.75; break;
            case "x-small": result = 10 * 0.75; break;
            case "small": result = 13 * 0.75; break;
            case "" or "medium": result = 12; break;
            case "large": result = 18 * 0.75; break;
            case "x-large": result = 24 * 0.75; break;
            case "xx-large": result = 32 * 0.75; break;
            case "xxx-large": result = 48 * 0.75; break;
            case "smaller": result = parent / 1.2; break;
            case "larger": result = parent * 1.2; break;
            default:
                if (v.EndsWith('%') && double.TryParse(v[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                {
                    result = parent * pct / 100;
                }
                else
                {
                    result = Points(v, parent) ?? parent;
                }

                break;
        }

        return Math.Clamp(result, 1, 1638);
    }

    /// <summary>The first usable family of a font-family list, mapping the generic families to common fonts; null when none.</summary>
    public static string? Family(string? list)
    {
        if (string.IsNullOrWhiteSpace(list))
        {
            return null;
        }

        foreach (string raw in list.Split(','))
        {
            string f = raw.Trim().Trim('"', '\'').Trim();
            if (f.Length == 0 || f.Length > 64)
            {
                continue;
            }

            switch (f.ToLowerInvariant())
            {
                case "serif": return "Times New Roman";
                case "sans-serif": return "Arial";
                case "monospace" or "ui-monospace": return "Courier New";
                case "cursive": return "Comic Sans MS";
                case "fantasy": return "Impact";
                case "system-ui" or "ui-sans-serif" or "-apple-system" or "blinkmacsystemfont": return "Segoe UI";
                case "ui-serif": return "Times New Roman";
                case "inherit" or "initial" or "unset":
                    continue;
                default:
                    return f;
            }
        }

        return null;
    }

    /// <summary>Maps a CSS border style word to the WordprocessingML border value.</summary>
    public static string BorderValue(string style) => style switch
    {
        "dotted" => "dotted",
        "dashed" => "dashed",
        "double" => "double",
        _ => "single",
    };

    /// <summary>A border width in eighths of a point, clamped to what a border can hold (2 to 96).</summary>
    public static int BorderEighths(string width, double emPoints)
    {
        double? pt = width switch
        {
            "thin" => 0.75,
            "medium" => 2.25,
            "thick" => 3.75,
            _ => Points(width, emPoints),
        };
        return pt is null ? 0 : (int)Math.Clamp(Math.Round(pt.Value * 8), pt.Value > 0 ? 2 : 0, 96);
    }
}
