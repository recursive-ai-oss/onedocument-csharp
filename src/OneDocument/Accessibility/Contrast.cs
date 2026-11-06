using System.Globalization;

namespace RecursiveAI.OneDocument.Accessibility;

/// <summary>Colour contrast arithmetic as WCAG 2.x defines it, on sRGB colours.</summary>
public static class Contrast
{
    /// <summary>The ratio normal-size text needs.</summary>
    public const double NormalTextRatio = 4.5;

    /// <summary>The ratio large text needs.</summary>
    public const double LargeTextRatio = 3.0;

    /// <summary>Parses <c>RRGGBB</c> (a leading <c>#</c> and an alpha prefix <c>AARRGGBB</c> are tolerated).</summary>
    /// <param name="hex">The text.</param>
    /// <param name="rgb">The colour as 0xRRGGBB.</param>
    /// <returns>True when it is a colour.</returns>
    public static bool TryParseHex(string? hex, out int rgb)
    {
        rgb = 0;
        if (hex is null)
        {
            return false;
        }

        ReadOnlySpan<char> s = hex.AsSpan().Trim().TrimStart('#');
        if (s.Length == 8)
        {
            s = s[2..];
        }

        return s.Length == 6 && int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
    }

    /// <summary>Gives the relative luminance of a colour (WCAG 2.x: linearised sRGB, weights 0.2126, 0.7152, 0.0722).</summary>
    /// <param name="rgb">The colour as 0xRRGGBB.</param>
    /// <returns>A value from 0 (black) to 1 (white).</returns>
    public static double RelativeLuminance(int rgb)
    {
        static double Channel(int v)
        {
            double c = v / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel((rgb >> 16) & 0xFF)) + (0.7152 * Channel((rgb >> 8) & 0xFF)) + (0.0722 * Channel(rgb & 0xFF));
    }

    /// <summary>Gives the contrast ratio of two colours, from 1 to 21, whichever is lighter.</summary>
    /// <param name="foreground">One colour as 0xRRGGBB.</param>
    /// <param name="background">The other colour as 0xRRGGBB.</param>
    /// <returns>The ratio.</returns>
    public static double Ratio(int foreground, int background)
    {
        double a = RelativeLuminance(foreground), b = RelativeLuminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Tells whether the contrast is enough for the text size.</summary>
    /// <param name="ratio">The ratio.</param>
    /// <param name="sizePoints">The font size in points (0 when unknown, taken as normal).</param>
    /// <param name="bold">Whether the text is bold.</param>
    /// <returns>True when the ratio passes: 3:1 for text of 18 pt or more, or 14 pt or more in bold; 4.5:1 otherwise.</returns>
    public static bool Passes(double ratio, double sizePoints, bool bold) =>
        ratio >= (sizePoints >= 18 || (bold && sizePoints >= 14) ? LargeTextRatio : NormalTextRatio);

    /// <summary>Applies a tint (towards white, 0..1) or shade (towards black, 0..1), the way DrawingML's <c>lumMod</c>/<c>lumOff</c> pair is commonly used: linear mix in sRGB.</summary>
    /// <param name="rgb">The colour as 0xRRGGBB.</param>
    /// <param name="tint">Positive to lighten towards white, negative to darken towards black; a fraction from -1 to 1.</param>
    /// <returns>The adjusted colour.</returns>
    public static int ApplyTint(int rgb, double tint)
    {
        int Mix(int v) => tint >= 0 ? (int)Math.Round(v + ((255 - v) * tint)) : (int)Math.Round(v * (1 + tint));
        return (Mix((rgb >> 16) & 0xFF) << 16) | (Mix((rgb >> 8) & 0xFF) << 8) | Mix(rgb & 0xFF);
    }

    /// <summary>Composites a colour with an opacity over a background.</summary>
    /// <param name="foreground">The colour as 0xRRGGBB.</param>
    /// <param name="alpha">Opacity from 0 to 1.</param>
    /// <param name="background">The colour underneath as 0xRRGGBB.</param>
    /// <returns>The resulting opaque colour.</returns>
    public static int Blend(int foreground, double alpha, int background)
    {
        int Mix(int f, int b) => (int)Math.Round((f * alpha) + (b * (1 - alpha)));
        return (Mix((foreground >> 16) & 0xFF, (background >> 16) & 0xFF) << 16) | (Mix((foreground >> 8) & 0xFF, (background >> 8) & 0xFF) << 8) | Mix(foreground & 0xFF, background & 0xFF);
    }
}
