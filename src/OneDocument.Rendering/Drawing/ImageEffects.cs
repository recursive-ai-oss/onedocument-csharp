using OneRender.Image;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.Drawing;

// The cheap picture effects of ECMA-376 Part 1 §20.1.8: they change pixels, so each distinct combination makes one derived bitmap.
internal static class ImageEffects
{
    public static bool Handles(string name) => name is "alphaModFix" or "grayscl" or "biLevel" or "duotone" or "lum" or "clrChange";

    public static RgbaImage Apply(RgbaImage source, List<MarkupNode> effects, ColorResolver colors)
    {
        byte[] px = (byte[])source.Pixels.Clone();
        foreach (MarkupNode e in effects)
        {
            switch (e.LocalNameString)
            {
                case "grayscl":
                    ForEach(px, (ref double r, ref double g, ref double b, ref double a) =>
                    {
                        double y = (0.299 * r) + (0.587 * g) + (0.114 * b);
                        r = g = b = y;
                    });
                    break;
                case "biLevel":
                    {
                        double thresh = Value(e, "thresh", 50000) / 100000.0;
                        ForEach(px, (ref double r, ref double g, ref double b, ref double a) =>
                        {
                            double y = (0.299 * r) + (0.587 * g) + (0.114 * b);
                            r = g = b = y >= thresh ? 1 : 0;
                        });
                        break;
                    }

                case "lum":
                    {
                        // ECMA-376 Part 1 §20.1.8.42 (lum): bright shifts every channel, contrast scales around mid-grey.
                        double bright = Value(e, "bright", 0) / 100000.0, contrast = Value(e, "contrast", 0) / 100000.0;
                        double k = 1 + contrast;
                        ForEach(px, (ref double r, ref double g, ref double b, ref double a) =>
                        {
                            r = Math.Clamp(((r - 0.5) * k) + 0.5 + bright, 0, 1);
                            g = Math.Clamp(((g - 0.5) * k) + 0.5 + bright, 0, 1);
                            b = Math.Clamp(((b - 0.5) * k) + 0.5 + bright, 0, 1);
                        });
                        break;
                    }

                case "alphaModFix":
                    {
                        double amt = Value(e, "amt", 100000) / 100000.0;
                        ForEach(px, (ref double r, ref double g, ref double b, ref double a) => a *= amt);
                        break;
                    }

                case "duotone":
                    {
                        // §20.1.8.23: dark pixels take the first colour, light ones the second, in between a blend by luminance.
                        var cs = new List<DrawingColor>();
                        foreach (MarkupNode c in e.ChildElements)
                        {
                            if (colors.Resolve(c) is { } col)
                            {
                                cs.Add(col);
                            }
                        }

                        if (cs.Count >= 2)
                        {
                            DrawingColor c0 = cs[0], c1 = cs[1];
                            ForEach(px, (ref double r, ref double g, ref double b, ref double a) =>
                            {
                                double y = (0.299 * r) + (0.587 * g) + (0.114 * b);
                                r = c0.R + ((c1.R - c0.R) * y);
                                g = c0.G + ((c1.G - c0.G) * y);
                                b = c0.B + ((c1.B - c0.B) * y);
                            });
                        }

                        break;
                    }

                case "clrChange":
                    {
                        // §20.1.8.16: pixels of clrFrom become clrTo (often transparent).
                        MarkupNode from = DrawingTheme.Child(e, "clrFrom"), to = DrawingTheme.Child(e, "clrTo");
                        if (!from.IsNull && !to.IsNull && colors.ResolveChild(from) is { } cf && colors.ResolveChild(to) is { } ct)
                        {
                            uint match = cf.Rgb;
                            ForEach(px, (ref double r, ref double g, ref double b, ref double a) =>
                            {
                                if (new DrawingColor(r, g, b).Rgb == match)
                                {
                                    r = ct.R;
                                    g = ct.G;
                                    b = ct.B;
                                    a *= ct.A;
                                }
                            });
                        }

                        break;
                    }
            }
        }

        return new RgbaImage(source.Width, source.Height, px);
    }

    private delegate void Pixel(ref double r, ref double g, ref double b, ref double a);

    private static void ForEach(byte[] px, Pixel f)
    {
        for (int i = 0; i + 3 < px.Length; i += 4)
        {
            double r = px[i] / 255.0, g = px[i + 1] / 255.0, b = px[i + 2] / 255.0, a = px[i + 3] / 255.0;
            f(ref r, ref g, ref b, ref a);
            px[i] = (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255);
            px[i + 1] = (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255);
            px[i + 2] = (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255);
            px[i + 3] = (byte)Math.Clamp((int)Math.Round(a * 255), 0, 255);
        }
    }

    private static double Value(MarkupNode e, string attr, double fallback) =>
        double.TryParse(e.GetAttribute(attr), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
}

// ECMA-376 Part 1 §20.1.10.51 (ST_PresetPatternVal): the 54 pattern fills are eight by eight bitmaps. The standard names them without
// drawing them, so the commonly seen ones are built from simple rules (lines at a pitch, dithered percentages, grids, checks) and the
// rest fall back to a 50 % dither and are reported as approximated.
internal static class PatternTiles
{
    private static readonly int[] Bayer =
    [
        0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26,
        12, 44, 4, 36, 14, 46, 6, 38, 60, 28, 52, 20, 62, 30, 54, 22,
        3, 35, 11, 43, 1, 33, 9, 41, 51, 19, 59, 27, 49, 17, 57, 25,
        15, 47, 7, 39, 13, 45, 5, 37, 63, 31, 55, 23, 61, 29, 53, 21,
    ];

    public static byte[] Png(string prst, DrawingColor fg, DrawingColor bg, out bool approximated)
    {
        bool approx = false;
        var px = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool on = On(prst, x, y, ref approx);
                DrawingColor c = on ? fg : bg;
                int i = ((y * 8) + x) * 4;
                px[i] = (byte)Math.Round(c.R * 255);
                px[i + 1] = (byte)Math.Round(c.G * 255);
                px[i + 2] = (byte)Math.Round(c.B * 255);
                px[i + 3] = (byte)Math.Round(c.A * 255);
            }
        }

        approximated = approx;
        return new RgbaImage(8, 8, px).ToPng();
    }

    private static bool On(string p, int x, int y, ref bool approx)
    {
        if (p.StartsWith("pct", StringComparison.Ordinal) && int.TryParse(p.AsSpan(3), out int pct))
        {
            return Bayer[((y & 7) * 8) + (x & 7)] < pct * 64 / 100;
        }

        switch (p)
        {
            case "horz": return y % 8 < 2;
            case "ltHorz": return y % 4 == 0;
            case "dkHorz": return y % 4 < 3;
            case "narHorz": return y % 2 == 0;
            case "dashHorz": return y % 4 == 0 && ((x + (y / 4 * 4)) % 8) < 4;
            case "vert": return x % 8 < 2;
            case "ltVert": return x % 4 == 0;
            case "dkVert": return x % 4 < 3;
            case "narVert": return x % 2 == 0;
            case "dashVert": return x % 4 == 0 && ((y + (x / 4 * 4)) % 8) < 4;
            case "ltDnDiag": return (x + y) % 4 == 0;
            case "dnDiag": return (x + y) % 4 < 2;
            case "dkDnDiag": return (x + y) % 4 < 3;
            case "wdDnDiag": return (x + y) % 8 < 3;
            case "dashDnDiag": return (x + y) % 4 == 0 && (x % 8) < 4;
            case "ltUpDiag": return (x - y + 8) % 4 == 0;
            case "upDiag": return (x - y + 8) % 4 < 2;
            case "dkUpDiag": return (x - y + 8) % 4 < 3;
            case "wdUpDiag": return (x - y + 8) % 8 < 3;
            case "dashUpDiag": return (x - y + 8) % 4 == 0 && (x % 8) < 4;
            case "cross": return x % 8 == 0 || y % 8 == 0;
            case "diagCross": return (x + y) % 8 == 0 || (x - y + 8) % 8 == 0;
            case "smallGrid": return x % 4 == 0 || y % 4 == 0;
            case "largeGrid": return x % 8 == 0 || y % 8 == 0;
            case "dotGrid": return x % 4 == 0 && y % 4 == 0;
            case "dotDmnd": return (x + y) % 4 == 0 && (x - y + 8) % 4 == 0;
            case "smallCheck": return ((x / 2) + (y / 2)) % 2 == 0;
            case "largeCheck": return ((x / 4) + (y / 4)) % 2 == 0;
            case "smallConfetti": return ((x * 3) + (y * 5)) % 8 < 2;
            case "largeConfetti": return ((x * 3) + (y * 5)) % 8 < 4 && (x + y) % 2 == 0;
            case "openDmnd": return (x + y) % 8 == 0 || (x - y + 8) % 8 == 0;
            case "horzBrick": return y % 4 == 0 || (x + ((y / 4) * 4)) % 8 == 0;
            case "diagBrick": return (x + y) % 8 == 0 || (x - y + 8) % 8 == 4;
            case "plaid": return (y % 8 < 4 && x % 2 == 0) || (x % 8 < 4 && y % 2 == 0);
            case "weave": return ((x + y) % 4 == 0) || ((x - y + 8) % 8 == 2);
            case "shingle": return (x - (y * 2) + 16) % 8 == 0 || y % 4 == 0;
            case "trellis": return (x + y) % 4 < 2 && (x - y + 8) % 4 < 2;
            case "zigZag": return (y % 4 == (x % 4 < 2 ? x % 4 : 4 - (x % 4)) % 4);
            case "wave": return y % 8 == (x % 8 < 4 ? x % 4 : 4 - (x % 4)) % 8;
            case "sphere": return (x % 4 is 1 or 2) && (y % 4 is 1 or 2);
            case "solidDmnd": return Math.Abs((x % 8) - 3.5) + Math.Abs((y % 8) - 3.5) < 4;
            case "divot": return (x % 4 == 1 && y % 4 is 1 or 2) || (x % 4 == 2 && y % 4 == 1);
            case "horzBrickDash": return y % 4 == 0;
            default:
                approx = true;
                return Bayer[((y & 7) * 8) + (x & 7)] < 32;
        }
    }
}
