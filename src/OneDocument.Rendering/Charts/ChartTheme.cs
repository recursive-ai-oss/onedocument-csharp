using System.Globalization;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Charts;

/// <summary>An RGBA colour with 8-bit channels, as resolved from a <see cref="ChartColor"/>.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="A">Alpha 0..1.</param>
public readonly record struct ChartRgb(byte R, byte G, byte B, double A = 1.0)
{
    /// <summary>Parses six hex digits, or returns black.</summary>
    /// <param name="hex">The digits.</param>
    /// <returns>The colour.</returns>
    public static ChartRgb FromHex(string? hex) =>
        hex is { Length: 6 } && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) ? new((byte)(v >> 16), (byte)(v >> 8), (byte)v) : new(0, 0, 0);
}

/// <summary>The theme colours and fonts a chart resolves against (ECMA-376 Part 1 §20.1.6).</summary>
public sealed class ChartTheme
{
    private readonly Dictionary<string, ChartRgb> _colors = new(StringComparer.Ordinal);

    /// <summary>The body (minor) latin font name.</summary>
    public string MinorFont { get; set; } = "Calibri";

    /// <summary>The heading (major) latin font name.</summary>
    public string MajorFont { get; set; } = "Calibri Light";

    /// <summary>The theme Office has shipped since 2013 (accent1 4472C4 ...), used when the host supplies none.</summary>
    public static ChartTheme Default { get; } = Create(
        ("dk1", "000000"), ("lt1", "FFFFFF"), ("dk2", "44546A"), ("lt2", "E7E6E6"),
        ("accent1", "4472C4"), ("accent2", "ED7D31"), ("accent3", "A5A5A5"), ("accent4", "FFC000"), ("accent5", "5B9BD5"), ("accent6", "70AD47"),
        ("hlink", "0563C1"), ("folHlink", "954F72"));

    private static ChartTheme Create(params (string Name, string Hex)[] colors)
    {
        var t = new ChartTheme();
        foreach ((string name, string hex) in colors)
        {
            t._colors[name] = ChartRgb.FromHex(hex);
        }

        return t;
    }

    /// <summary>Reads <c>a:theme</c> (the clrScheme and fontScheme).</summary>
    /// <param name="themePart">The theme part's bytes.</param>
    /// <returns>The theme; the default theme's values fill what the part lacks. Null when the part is not well-formed XML.</returns>
    public static ChartTheme? TryParse(ReadOnlyMemory<byte> themePart)
    {
        if (!MarkupDocument.TryParse(themePart, out MarkupDocument? doc, null, null) || doc is null)
        {
            return null;
        }

        var t = new ChartTheme();
        foreach ((string k, ChartRgb v) in Default._colors)
        {
            t._colors[k] = v;
        }

        MarkupNode root = doc.Root;
        MarkupNode elements = ChartReader.Child(root, "themeElements");
        MarkupNode scheme = ChartReader.Child(elements, "clrScheme");
        foreach (MarkupNode c in ChartReader.Children(scheme))
        {
            ChartColor? col = ChartReader.Color(c);
            if (col is not null)
            {
                t._colors[c.LocalNameString] = ChartRgb.FromHex(col.Value.Length == 6 ? col.Value : null);
            }
        }

        MarkupNode fonts = ChartReader.Child(elements, "fontScheme");
        if (ChartReader.Child(ChartReader.Child(fonts, "minorFont"), "latin").GetAttribute("typeface") is { Length: > 0 } minor)
        {
            t.MinorFont = minor;
        }

        if (ChartReader.Child(ChartReader.Child(fonts, "majorFont"), "latin").GetAttribute("typeface") is { Length: > 0 } major)
        {
            t.MajorFont = major;
        }

        return t;
    }

    /// <summary>Resolves a colour to RGBA: scheme names through the map (tx1 = dk1, bg1 = lt1, tx2 = dk2, bg2 = lt2), then each transform in order.</summary>
    /// <param name="c">The colour.</param>
    /// <returns>The resolved colour.</returns>
    public ChartRgb Resolve(ChartColor c)
    {
        ArgumentNullException.ThrowIfNull(c);
        ChartRgb rgb = c.Kind switch
        {
            "scheme" => SchemeColor(c.Value),
            "prst" => Preset(c.Value),
            _ => ChartRgb.FromHex(c.Value),
        };
        double a = 1.0;
        double r = rgb.R;
        double g = rgb.G;
        double b = rgb.B;
        foreach (KeyValuePair<string, double> t in c.Transforms)
        {
            double v = t.Value / 100000.0;
            switch (t.Key)
            {
                case "alpha":
                    a = Math.Clamp(v, 0, 1);
                    break;
                case "alphaMod":
                    a = Math.Clamp(a * v, 0, 1);
                    break;
                case "lumMod":
                case "lumOff":
                case "satMod":
                    (double h, double s, double l) = ToHsl(r, g, b);
                    if (t.Key == "lumMod")
                    {
                        l = Math.Clamp(l * v, 0, 1);
                    }
                    else if (t.Key == "lumOff")
                    {
                        l = Math.Clamp(l + v, 0, 1);
                    }
                    else
                    {
                        s = Math.Clamp(s * v, 0, 1);
                    }

                    (r, g, b) = FromHsl(h, s, l);
                    break;
                case "tint":
                    // ECMA-376 Part 1 §20.1.2.3.34: tint moves the colour toward white.
                    r = 255 - ((255 - r) * v);
                    g = 255 - ((255 - g) * v);
                    b = 255 - ((255 - b) * v);
                    break;
                case "shade":
                    r *= v;
                    g *= v;
                    b *= v;
                    break;
            }
        }

        return new ChartRgb((byte)Math.Clamp(Math.Round(r), 0, 255), (byte)Math.Clamp(Math.Round(g), 0, 255), (byte)Math.Clamp(Math.Round(b), 0, 255), a);
    }

    private ChartRgb SchemeColor(string name)
    {
        string key = name switch { "tx1" => "dk1", "bg1" => "lt1", "tx2" => "dk2", "bg2" => "lt2", "phClr" => "accent1", _ => name };
        return _colors.TryGetValue(key, out ChartRgb v) ? v : new ChartRgb(0, 0, 0);
    }

    private static ChartRgb Preset(string name) => name.ToLowerInvariant() switch
    {
        "white" => new(255, 255, 255),
        "red" => new(255, 0, 0),
        "green" => new(0, 128, 0),
        "blue" => new(0, 0, 255),
        "yellow" => new(255, 255, 0),
        "gray" or "grey" => new(128, 128, 128),
        "ltgray" or "lightgray" => new(211, 211, 211),
        "dkgray" or "darkgray" => new(169, 169, 169),
        "orange" => new(255, 165, 0),
        _ => new(0, 0, 0),
    };

    private static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        r /= 255;
        g /= 255;
        b /= 255;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, l);
        }

        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
        return (h / 6, s, l);
    }

    private static (double R, double G, double B) FromHsl(double h, double s, double l)
    {
        if (s == 0)
        {
            return (l * 255, l * 255, l * 255);
        }

        double q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        double p = (2 * l) - q;
        return (Hue(p, q, h + (1.0 / 3)) * 255, Hue(p, q, h) * 255, Hue(p, q, h - (1.0 / 3)) * 255);

        static double Hue(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            return t < 1.0 / 6 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
        }
    }

    /// <summary>The automatic colour of series number <paramref name="index"/> in the default chart style: accent 1..6, then the accents darkened and lightened.</summary>
    /// <param name="index">The series (or point) index.</param>
    /// <param name="count">How many series there are (the shade steps spread over them in Office's styles; here fixed steps).</param>
    /// <returns>The colour.</returns>
    public ChartRgb AutomaticSeriesColor(int index, int count)
    {
        _ = count;
        string accent = "accent" + ((index % 6) + 1).ToString(CultureInfo.InvariantCulture);
        int cycle = index / 6;
        var c = new ChartColor { Kind = "scheme", Value = accent };
        if (cycle == 1)
        {
            c.Transforms.Add(new("lumMod", 60000));
        }
        else if (cycle == 2)
        {
            c.Transforms.Add(new("lumMod", 80000));
            c.Transforms.Add(new("lumOff", 20000));
        }
        else if (cycle >= 3)
        {
            c.Transforms.Add(new("lumMod", 60000));
            c.Transforms.Add(new("lumOff", 40000));
        }

        return Resolve(c);
    }
}
