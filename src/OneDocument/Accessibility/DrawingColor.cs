using System.Globalization;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Accessibility;

/// <summary>Resolves a DrawingML colour element (<c>a:srgbClr</c>, <c>a:schemeClr</c>, <c>a:sysClr</c>, <c>a:prstClr</c>) to an RGB value for contrast checks.</summary>
public static class DrawingColor
{
    /// <summary>Resolves a colour element, applying the transforms that change what the eye sees: <c>lumMod</c>, <c>lumOff</c>, <c>tint</c>, <c>shade</c> and <c>alpha</c>.</summary>
    /// <param name="color">The colour element, or the fill element that holds one (<c>a:solidFill</c>).</param>
    /// <param name="scheme">Resolves a scheme colour name (<c>tx1</c>, <c>accent2</c>, …) to 0xRRGGBB, or null when it cannot.</param>
    /// <param name="rgb">The colour as 0xRRGGBB.</param>
    /// <param name="alpha">The opacity from 0 to 1.</param>
    /// <returns>True when the colour could be resolved.</returns>
    public static bool TryResolve(MarkupNode color, Func<string, int?> scheme, out int rgb, out double alpha)
    {
        rgb = 0;
        alpha = 1;
        if (color.IsNull)
        {
            return false;
        }

        if (color.LocalNameString == "solidFill")
        {
            foreach (MarkupNode c in color.ChildElements)
            {
                return TryResolve(c, scheme, out rgb, out alpha);
            }

            return false;
        }

        int? baseColor = null;
        switch (color.LocalNameString)
        {
            case "srgbClr":
                baseColor = Contrast.TryParseHex(color.GetAttribute("val"), out int v) ? v : null;
                break;
            case "sysClr":
                baseColor = Contrast.TryParseHex(color.GetAttribute("lastClr"), out int sv) ? sv : color.GetAttribute("val") is "windowText" ? 0 : color.GetAttribute("val") is "window" ? 0xFFFFFF : null;
                break;
            case "schemeClr":
                baseColor = color.GetAttribute("val") is { } name ? scheme(name) : null;
                break;
            case "prstClr":
                baseColor = color.GetAttribute("val") switch { "black" => 0, "white" => 0xFFFFFF, "red" => 0xFF0000, "green" => 0x008000, "blue" => 0x0000FF, "yellow" => 0xFFFF00, "gray" or "grey" => 0x808080, _ => null };
                break;
        }

        if (baseColor is not { } result)
        {
            return false;
        }

        foreach (MarkupNode t in color.ChildElements)
        {
            if (!double.TryParse(t.GetAttribute("val"), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            {
                continue;
            }

            // ECMA-376 Part 1 §20.1.2.3: transform values are in 1000ths of a percent.
            double f = pct / 100000.0;
            switch (t.LocalNameString)
            {
                case "lumMod" or "lumOff":
                    break;
                case "tint":
                    result = Contrast.ApplyTint(result, 1 - f);
                    break;
                case "shade":
                    result = Contrast.ApplyTint(result, f - 1);
                    break;
                case "alpha":
                    alpha = Math.Clamp(f, 0, 1);
                    break;
            }
        }

        // lumMod and lumOff work together on the HSL luminance: L' = L × mod + off.
        double mod = 1, off = 0;
        bool lum = false;
        foreach (MarkupNode t in color.ChildElements)
        {
            if (double.TryParse(t.GetAttribute("val"), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            {
                if (t.LocalNameString == "lumMod")
                {
                    mod *= pct / 100000.0;
                    lum = true;
                }
                else if (t.LocalNameString == "lumOff")
                {
                    off += pct / 100000.0;
                    lum = true;
                }
            }
        }

        rgb = lum ? ScaleLuminance(result, mod, off) : result;
        return true;
    }

    private static int ScaleLuminance(int rgb, double mod, double off)
    {
        double r = ((rgb >> 16) & 0xFF) / 255.0, g = ((rgb >> 8) & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        double d = max - min;
        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
            h /= 6;
        }

        l = Math.Clamp((l * mod) + off, 0, 1);
        double q = l < 0.5 ? l * (1 + s) : l + s - (l * s), p = (2 * l) - q;
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

        double nr = s == 0 ? l : Hue(p, q, h + (1.0 / 3)), ng = s == 0 ? l : Hue(p, q, h), nb = s == 0 ? l : Hue(p, q, h - (1.0 / 3));
        return ((int)Math.Round(nr * 255) << 16) | ((int)Math.Round(ng * 255) << 8) | (int)Math.Round(nb * 255);
    }
}
