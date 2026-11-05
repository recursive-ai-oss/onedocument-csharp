using System.Globalization;
using OneFont;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// What a header's watermark shape asks for.
internal sealed record WatermarkSpec(string Text, string Family, bool Bold, bool Italic, double ShapeWidth, uint Color, double Opacity, double Rotation);

// Watermarks are WordArt shapes in the header: a VML shape whose text path holds the string ([MS-OI29500] describes Word writing
// them as `PowerPlusWaterMarkObject`; ECMA-376 Part 1 §L.2 / Part 4 §14 define the shape and textpath elements). Only text
// watermarks are drawn; a picture watermark is reported.
internal static class Watermarks
{
    public static bool IsWatermark(MarkupNode pict) => FindTextPath(pict, 0) is not null;

    public static WatermarkSpec? Read(MarkupNode pict)
    {
        MarkupNode? tp = FindTextPath(pict, 0);
        if (tp is not { } textPath)
        {
            return null;
        }

        string text = textPath.GetAttribute("string") ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        // The textpath style names the font; the enclosing shape gives size, rotation and fill.
        string tpStyle = textPath.GetAttribute("style") ?? string.Empty;
        string family = Style(tpStyle, "font-family")?.Trim('"', '\'', ' ') ?? "Calibri";
        bool bold = tpStyle.Contains("font-weight:bold", StringComparison.Ordinal);
        bool italic = tpStyle.Contains("font-style:italic", StringComparison.Ordinal);
        MarkupNode shape = textPath.Parent;
        string style = shape.IsNull ? string.Empty : shape.GetAttribute("style") ?? string.Empty;
        double width = Length(Style(style, "width")) ?? 400;
        double rotation = double.TryParse(Style(style, "rotation"), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0;
        uint color = Color(shape.IsNull ? null : shape.GetAttribute("fillcolor")) ?? 0xC0C0C0;
        double opacity = 1;
        for (MarkupNode c = shape.IsNull ? default : shape.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element && Xml.Eq(c.LocalName, "fill") && c.GetAttribute("opacity") is { } o)
            {
                opacity = Opacity(o);
            }
        }

        return new WatermarkSpec(text, family, bold, italic, width, color, opacity, rotation);
    }

    private static MarkupNode? FindTextPath(MarkupNode n, int depth)
    {
        if (depth > 12)
        {
            return null;
        }

        for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (Xml.Eq(c.LocalName, "textpath"))
            {
                return c;
            }

            if (FindTextPath(c, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // Reads `name:value;` out of a CSS-like style attribute.
    private static string? Style(string style, string name)
    {
        foreach (string part in style.Split(';'))
        {
            int i = part.IndexOf(':');
            if (i > 0 && string.Equals(part[..i].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return part[(i + 1)..].Trim();
            }
        }

        return null;
    }

    // A VML length in points: pt, in, cm, mm, px (96 per inch); a bare number is points.
    private static double? Length(string? v)
    {
        if (v is null)
        {
            return null;
        }

        int k = 0;
        while (k < v.Length && (char.IsDigit(v[k]) || v[k] is '.' or '-'))
        {
            k++;
        }

        if (!double.TryParse(v.AsSpan(0, k), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
        {
            return null;
        }

        return v[k..].Trim() switch
        {
            "in" => n * 72,
            "cm" => n * 72 / 2.54,
            "mm" => n * 72 / 25.4,
            "px" => n * 0.75,
            _ => n,
        };
    }

    private static uint? Color(string? v)
    {
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }

        if (v[0] == '#' && v.Length >= 7 && uint.TryParse(v.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return rgb;
        }

        // VML colour names Word uses for watermarks.
        return v.Split(' ')[0].ToLowerInvariant() switch
        {
            "silver" => 0xC0C0C0,
            "gray" or "grey" => 0x808080,
            "black" => 0x000000,
            "red" => 0xFF0000,
            "blue" => 0x0000FF,
            "green" => 0x008000,
            "white" => 0xFFFFFF,
            "yellow" => 0xFFFF00,
            _ => null,
        };
    }

    // v:fill opacity is a fraction (".5") or a 16.16 fixed-point number ("32768f").
    private static double Opacity(string v)
    {
        if (v.EndsWith('f') && double.TryParse(v.AsSpan(0, v.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double fx))
        {
            return Math.Clamp(fx / 65536.0, 0, 1);
        }

        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double f) ? Math.Clamp(f, 0, 1) : 1;
    }
}

// Shapes a short string with one face into glyph data for decorations (line numbers, watermarks), outside any paragraph.
internal static class DecorationText
{
    public static (GlyphData Glyphs, double Width)? Shape(string text, FontFace face, double size)
    {
        if (text.Length == 0)
        {
            return null;
        }

        var run = new ShapedRun();
        var options = new ShapingOptions { Kerning = KerningMode.None };
        options.Features.Add(FontFeature.Off("liga"));
        options.Features.Add(FontFeature.Off("clig"));
        TextShaping.ShapeRun(text, face, run, false, options);
        double unit = size / face.UnitsPerEm;
        var ids = new List<int>();
        var xs = new List<float>();
        var ys = new List<float>();
        var cl = new List<int>();
        double pen = 0;
        ReadOnlySpan<ShapedGlyph> glyphs = run.Glyphs;
        ReadOnlySpan<ShapedClusterRange> clusters = run.Clusters;
        for (int c = 0; c < run.ClusterCount; c++)
        {
            ShapedClusterRange cr = clusters[c];
            double clusterPen = pen;
            for (int g = 0; g < cr.GlyphCount; g++)
            {
                ShapedGlyph sg = glyphs[cr.GlyphStart + g];
                ids.Add(sg.GlyphId);
                xs.Add((float)(clusterPen + (sg.XOffset * unit)));
                ys.Add((float)(-sg.YOffset * unit));
                cl.Add(cr.Start);
                clusterPen += sg.XAdvance * unit;
            }

            pen += (double)cr.Advance * unit;
        }

        return (new GlyphData { Face = face, GlyphIds = [.. ids], X = [.. xs], Y = [.. ys], Clusters = [.. cl], RightToLeft = false }, pen);
    }
}
