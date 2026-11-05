using System.Globalization;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>The twelve colours of a theme's colour scheme, in the order a style's <c>theme</c> index selects them, and the colour arithmetic of tints.</summary>
internal sealed class ThemeColors
{
    // ECMA-376 Part 1 §18.8.3: theme index 0 = lt1, 1 = dk1, 2 = lt2, 3 = dk2, 4–9 = accent1–6, 10 = hlink, 11 = folHlink.
    private static readonly string[] SchemeOrder = ["lt1", "dk1", "lt2", "dk2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink"];

    // The Office theme of the current default workbook, used when a workbook has no theme part.
    private static readonly int[] Fallback =
    [
        unchecked((int)0xFFFFFFFF), unchecked((int)0xFF000000), unchecked((int)0xFFE7E6E6), unchecked((int)0xFF44546A), unchecked((int)0xFF4472C4), unchecked((int)0xFFED7D31),
        unchecked((int)0xFFA5A5A5), unchecked((int)0xFFFFC000), unchecked((int)0xFF5B9BD5), unchecked((int)0xFF70AD47), unchecked((int)0xFF0563C1), unchecked((int)0xFF954F72),
    ];

    // ECMA-376 Part 1 §18.8.27: the default indexed palette (RGB, indexes 0–63).
    public static readonly uint[] DefaultPalette =
    [
        0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
        0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
        0x800000, 0x008000, 0x000080, 0x808000, 0x800080, 0x008080, 0xC0C0C0, 0x808080,
        0x9999FF, 0x993366, 0xFFFFCC, 0xCCFFFF, 0x660066, 0xFF8080, 0x0066CC, 0xCCCCFF,
        0x000080, 0xFF00FF, 0xFFFF00, 0x00FFFF, 0x800080, 0x800000, 0x008080, 0x0000FF,
        0x00CCFF, 0xCCFFFF, 0xCCFFCC, 0xFFFF99, 0x99CCFF, 0xFF99CC, 0xCC99FF, 0xFFCC99,
        0x3366FF, 0x33CCCC, 0x99CC00, 0xFFCC00, 0xFF9900, 0xFF6600, 0x666699, 0x969696,
        0x003366, 0x339966, 0x003300, 0x333300, 0x993300, 0x993366, 0x333399, 0x333333,
    ];

    private readonly int[] _colors;

    private ThemeColors(int[] colors) => _colors = colors;

    public static ThemeColors Default { get; } = new(Fallback);

    public int Get(int index) => index >= 0 && index < _colors.Length ? _colors[index] : unchecked((int)0xFF000000);

    public static ThemeColors Read(ReadOnlyMemory<byte> bytes, OpenContext context, string partName)
    {
        MarkupDocument doc;
        try
        {
            doc = MarkupDocument.Parse(bytes, context, partName);
        }
        catch (OfficeFormatException)
        {
            context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.theme-unreadable", DefectSeverity.Repaired, "The theme part is not well-formed XML; the default Office colours are used.", DefectLocation.InPart(partName)));
            return Default;
        }

        int[] colors = [.. Fallback];
        foreach (MarkupNode scheme in Descendants(doc.Root, "clrScheme"))
        {
            foreach (MarkupNode c in scheme.ChildElements)
            {
                int i = Array.IndexOf(SchemeOrder, c.LocalNameString);
                if (i < 0)
                {
                    continue;
                }

                foreach (MarkupNode v in c.ChildElements)
                {
                    string? hex = v.LocalNameString == "srgbClr" ? v.GetAttribute("val") : v.LocalNameString == "sysClr" ? v.GetAttribute("lastClr") ?? v.GetAttribute("val") : null;
                    if (hex is { Length: 6 } && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                    {
                        colors[i] = unchecked((int)0xFF000000) | rgb;
                    }
                }
            }

            break;
        }

        return new ThemeColors(colors);
    }

    private static IEnumerable<MarkupNode> Descendants(MarkupNode n, string local)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                yield return c;
            }
            else
            {
                foreach (MarkupNode d in Descendants(c, local))
                {
                    yield return d;
                }
            }
        }
    }

    /// <summary>ECMA-376 Part 1 §18.3.1.15: a negative tint darkens (L × (1 + tint)); a positive tint lightens (L × (1 − tint) + tint) in HLS space.</summary>
    public static int ApplyTint(int argb, double tint)
    {
        double r = ((argb >> 16) & 0xFF) / 255.0, g = ((argb >> 8) & 0xFF) / 255.0, b = (argb & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }

        l = tint < 0 ? l * (1 + tint) : l * (1 - tint) + tint;
        l = Math.Clamp(l, 0, 1);
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }

        double nr = s == 0 ? l : Hue(p, q, h + 1.0 / 3), ng = s == 0 ? l : Hue(p, q, h), nb = s == 0 ? l : Hue(p, q, h - 1.0 / 3);
        return unchecked((int)(0xFF000000u | ((uint)Math.Round(nr * 255) << 16) | ((uint)Math.Round(ng * 255) << 8) | (uint)Math.Round(nb * 255)));
    }
}
