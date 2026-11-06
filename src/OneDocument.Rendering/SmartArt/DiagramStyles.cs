using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

// The colour transform (dgm:colorsDef, ECMA-376 Part 1 §21.4.2.9) and the quick style (dgm:styleDef, §21.4.2.34) of a diagram: for each
// style label, which colours a shape takes from the theme and which shape-style references it carries.

/// <summary>One colour list of a style label: a method of walking the colours and the colour elements themselves.</summary>
internal sealed class ColorList
{
    public string Method { get; init; } = "span";

    public string HueDirection { get; init; } = "cw";

    public List<MarkupNode> Colors { get; } = [];
}

internal sealed class ColorStyleLabel
{
    public ColorList? Fill { get; set; }

    public ColorList? Line { get; set; }

    public ColorList? Effect { get; set; }

    public ColorList? TextLine { get; set; }

    public ColorList? TextFill { get; set; }

    public ColorList? TextEffect { get; set; }
}

internal sealed class DiagramColors
{
    public Dictionary<string, ColorStyleLabel> Labels { get; } = new(StringComparer.Ordinal);

    public static DiagramColors? TryRead(MarkupDocument doc)
    {
        MarkupNode root = doc.Root;
        if (root.IsNull || root.LocalNameString != "colorsDef")
        {
            return null;
        }

        var result = new DiagramColors();
        foreach (MarkupNode lbl in Dx.Children(root, "styleLbl"))
        {
            if (lbl.GetAttribute("name") is not { } name)
            {
                continue;
            }

            result.Labels[name] = new ColorStyleLabel
            {
                Fill = ReadList(Dx.Child(lbl, "fillClrLst")),
                Line = ReadList(Dx.Child(lbl, "linClrLst")),
                Effect = ReadList(Dx.Child(lbl, "effectClrLst")),
                TextLine = ReadList(Dx.Child(lbl, "txLinClrLst")),
                TextFill = ReadList(Dx.Child(lbl, "txFillClrLst")),
                TextEffect = ReadList(Dx.Child(lbl, "txEffectClrLst")),
            };
        }

        return result;
    }

    private static ColorList? ReadList(MarkupNode e)
    {
        if (e.IsNull)
        {
            return null;
        }

        var list = new ColorList { Method = e.GetAttribute("meth") ?? "span", HueDirection = e.GetAttribute("hueDir") ?? "cw" };
        foreach (MarkupNode c in e.ChildElements)
        {
            list.Colors.Add(c);
        }

        return list;
    }

    /// <summary>
    /// Picks the colour of the <paramref name="index"/>-th of <paramref name="count"/> shapes sharing a style label, as colour-element XML
    /// in the DrawingML namespace, or null when the list is empty.
    /// </summary>
    /// <remarks>
    /// §21.4.7.? (ST_ClrAppMethod): <c>repeat</c> and <c>cycle</c> walk the list round and round; <c>span</c> spreads the list over the
    /// shapes, blending neighbours in HSL along the hue direction. The standard gives no formula for the blend; this is the plain
    /// linear one.
    /// </remarks>
    public static string? Pick(ColorList? list, int index, int count, ColorResolver colors)
    {
        if (list is null || list.Colors.Count == 0)
        {
            return null;
        }

        int k = list.Colors.Count;
        if (k == 1 || list.Method != "span" || count <= 1)
        {
            MarkupNode chosen = list.Colors[list.Method == "span" ? 0 : index % k];
            return DrawingXml.Copy(chosen);
        }

        double pos = (double)index / (count - 1) * (k - 1);
        int lo = Math.Min((int)Math.Floor(pos), k - 1);
        int hi = Math.Min(lo + 1, k - 1);
        double t = pos - lo;
        if (colors.Resolve(list.Colors[lo]) is not { } a || colors.Resolve(list.Colors[hi]) is not { } b)
        {
            return DrawingXml.Copy(list.Colors[lo]);
        }

        (double h1, double s1, double l1) = ColorResolver.RgbToHsl(a);
        (double h2, double s2, double l2) = ColorResolver.RgbToHsl(b);
        double dh = h2 - h1;
        if (list.HueDirection == "ccw")
        {
            if (dh > 0)
            {
                dh -= 360;
            }
        }
        else if (dh < 0)
        {
            dh += 360;
        }

        double h = (h1 + (dh * t)) % 360;
        if (h < 0)
        {
            h += 360;
        }

        (double r, double g, double bl) = ColorResolver.HslToRgb(h, s1 + ((s2 - s1) * t), l1 + ((l2 - l1) * t));
        uint rgb = new DrawingColor(r, g, bl).Rgb;
        return $"<a:srgbClr val=\"{rgb:X6}\"/>";
    }
}

/// <summary>The shape-style references of one style label of a quick style.</summary>
internal sealed class StyleLabelDef
{
    public int LineIndex { get; init; } = 2;

    public int FillIndex { get; init; } = 1;

    public int EffectIndex { get; init; }

    public string FontIndex { get; init; } = "minor";
}

internal sealed class DiagramQuickStyle
{
    public Dictionary<string, StyleLabelDef> Labels { get; } = new(StringComparer.Ordinal);

    public static DiagramQuickStyle? TryRead(MarkupDocument doc)
    {
        MarkupNode root = doc.Root;
        if (root.IsNull || root.LocalNameString != "styleDef")
        {
            return null;
        }

        var result = new DiagramQuickStyle();
        foreach (MarkupNode lbl in Dx.Children(root, "styleLbl"))
        {
            if (lbl.GetAttribute("name") is not { } name)
            {
                continue;
            }

            MarkupNode style = Dx.Child(lbl, "style");
            result.Labels[name] = new StyleLabelDef
            {
                LineIndex = Dx.Int(Dx.Child(style, "lnRef").GetAttribute("idx"), 2),
                FillIndex = Dx.Int(Dx.Child(style, "fillRef").GetAttribute("idx"), 1),
                EffectIndex = Dx.Int(Dx.Child(style, "effectRef").GetAttribute("idx"), 0),
                FontIndex = Dx.Child(style, "fontRef").GetAttribute("idx") ?? "minor",
            };
        }

        return result;
    }
}

// Writes DrawingML markup read from one part into a new document: elements are rewritten in the "a" prefix so that the source's prefix
// choices do not matter, and only unprefixed attributes survive (colour, line and text-run elements use no others).
internal static class DrawingXml
{
    public static string Copy(MarkupNode e)
    {
        var sb = new StringBuilder();
        Write(e, sb, 0);
        return sb.ToString();
    }

    public static void Write(MarkupNode e, StringBuilder sb, int depth)
    {
        if (depth > 16)
        {
            return;
        }

        string name = e.LocalNameString;
        sb.Append("<a:").Append(name);
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (a.IsNamespaceDeclaration || a.Prefix.Length != 0)
            {
                continue;
            }

            sb.Append(' ').Append(Encoding.UTF8.GetString(a.RawName)).Append("=\"").Append(Dx.Esc(a.Value)).Append('"');
        }

        bool any = false;
        foreach (MarkupNode c in e.ChildElements)
        {
            if (!any)
            {
                sb.Append('>');
                any = true;
            }

            Write(c, sb, depth + 1);
        }

        if (any)
        {
            sb.Append("</a:").Append(name).Append('>');
        }
        else
        {
            sb.Append("/>");
        }
    }
}
