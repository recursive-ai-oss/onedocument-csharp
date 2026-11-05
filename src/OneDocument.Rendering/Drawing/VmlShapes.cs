using System.Globalization;
using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.Drawing;

// Legacy VML shapes (w:pict with v:shape, v:rect, v:roundrect, v:oval, v:line), read to the same information a DrawingML anchor carries
// and painted as plain shapes: fill colour, stroke, a picture (v:imagedata) or a text box (v:textbox). ECMA-376 Part 4 §14 (VML) and
// §14.1.2.19 (w10:wrap) describe the elements; positions come from the CSS-like style attribute (Part 4 §14.1.2.19, [MS-OI29500]).
internal static class VmlShapes
{
    private static readonly string[] ShapeNames = ["shape", "rect", "roundrect", "oval", "line"];

    public static MarkupNode FindShape(MarkupNode pict)
    {
        foreach (MarkupNode c in pict.ChildElements)
        {
            if (Array.IndexOf(ShapeNames, c.LocalNameString) >= 0)
            {
                return c;
            }
        }

        return default;
    }

    public static Dictionary<string, string> Style(MarkupNode shape)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in (shape.GetAttribute("style") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int i = part.IndexOf(':', StringComparison.Ordinal);
            if (i > 0)
            {
                d[part[..i].Trim()] = part[(i + 1)..].Trim();
            }
        }

        return d;
    }

    // A CSS length in points; unitless numbers are pixels.
    public static double? Length(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        s = s.Trim();
        int k = s.Length;
        while (k > 0 && char.IsLetter(s[k - 1]))
        {
            k--;
        }

        if (!double.TryParse(s[..k], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v))
        {
            return null;
        }

        return s[k..].ToLowerInvariant() switch
        {
            "pt" => v,
            "in" => v * 72,
            "cm" => v * 72 / 2.54,
            "mm" => v * 72 / 25.4,
            "pc" => v * 12,
            _ => v * 0.75,
        };
    }

    public static DrawingInfo? Read(MarkupNode pict)
    {
        MarkupNode shape = FindShape(pict);
        if (shape.IsNull)
        {
            return null;
        }

        Dictionary<string, string> st = Style(shape);
        double? w = Length(st.GetValueOrDefault("width")), h = Length(st.GetValueOrDefault("height"));
        if (w is not > 0 || h is not > 0)
        {
            return null;
        }

        bool absolute = st.GetValueOrDefault("position") is "absolute";
        string? descr = shape.GetAttribute("alt");
        DrawingAnchor? anchor = null;
        string? rh = null, rv = null, ah = null, av = null;
        double? ox = null, oy = null;
        if (absolute)
        {
            rh = st.GetValueOrDefault("mso-position-horizontal-relative") switch
            {
                "page" => "page",
                "margin" => "margin",
                "char" => "character",
                "left-margin-area" => "leftMargin",
                "right-margin-area" => "rightMargin",
                "inner-margin-area" => "insideMargin",
                "outer-margin-area" => "outsideMargin",
                _ => "column",
            };
            rv = st.GetValueOrDefault("mso-position-vertical-relative") switch
            {
                "page" => "page",
                "margin" => "margin",
                "line" => "line",
                "top-margin-area" => "topMargin",
                "bottom-margin-area" => "bottomMargin",
                "inner-margin-area" => "insideMargin",
                "outer-margin-area" => "outsideMargin",
                _ => "paragraph",
            };
            ah = st.GetValueOrDefault("mso-position-horizontal") is { } hv and ("left" or "center" or "right" or "inside" or "outside") ? hv : null;
            av = st.GetValueOrDefault("mso-position-vertical") is { } vv and ("top" or "center" or "bottom" or "inside" or "outside") ? vv : null;
            ox = Length(st.GetValueOrDefault("margin-left")) ?? 0;
            oy = Length(st.GetValueOrDefault("margin-top")) ?? 0;
            MarkupNode wrap = default;
            foreach (MarkupNode c in shape.ChildElements)
            {
                if (c.LocalNameString == "wrap")
                {
                    wrap = c;
                }
            }

            DrawingWrap kind = wrap.IsNull ? DrawingWrap.None : (wrap.GetAttribute("type") switch
            {
                "square" => DrawingWrap.Square,
                "tight" => DrawingWrap.Tight,
                "through" => DrawingWrap.Through,
                "topAndBottom" => DrawingWrap.TopAndBottom,
                _ => DrawingWrap.None,
            });
            string side = wrap.IsNull ? "both" : wrap.GetAttribute("side") ?? "both";
            long z = 0;
            if (st.TryGetValue("z-index", out string? zs))
            {
                long.TryParse(zs, NumberStyles.Integer, CultureInfo.InvariantCulture, out z);
            }

            anchor = new DrawingAnchor
            {
                RelativeHeight = z,
                Wrap = kind,
                WrapText = side switch { "left" => "left", "right" => "right", "largest" => "largest", _ => "bothSides" },
                DistanceLeft = Length(st.GetValueOrDefault("mso-wrap-distance-left")) ?? 9,
                DistanceRight = Length(st.GetValueOrDefault("mso-wrap-distance-right")) ?? 9,
                DistanceTop = Length(st.GetValueOrDefault("mso-wrap-distance-top")) ?? 0,
                DistanceBottom = Length(st.GetValueOrDefault("mso-wrap-distance-bottom")) ?? 0,
            };
        }

        long zIndex = 0;
        if (st.TryGetValue("z-index", out string? z2))
        {
            long.TryParse(z2, NumberStyles.Integer, CultureInfo.InvariantCulture, out zIndex);
        }

        return new DrawingInfo
        {
            Inline = !absolute,
            Width = Math.Clamp(w.Value, 0, 20000),
            Height = Math.Clamp(h.Value, 0, 20000),
            AltText = descr,
            Graphic = default,
            OffsetX = ox,
            OffsetY = oy,
            RelativeFromH = rh,
            RelativeFromV = rv,
            AlignH = ah,
            AlignV = av,
            // A negative z-index puts the shape behind the text (Part 4 §14.1.2.19).
            BehindText = absolute && zIndex < 0,
            Anchor = anchor,
        };
    }

    // v:textbox/@inset is left, top, right, bottom; the default is 0.1 inch and 0.05 inch.
    public static (double Left, double Top, double Right, double Bottom) Insets(MarkupNode textbox)
    {
        double l = 7.2, t = 3.6, r = 7.2, b = 3.6;
        if (textbox.GetAttribute("inset") is { Length: > 0 } inset)
        {
            string[] p = inset.Split(',');
            l = p.Length > 0 && Length(p[0]) is { } a ? a : l;
            t = p.Length > 1 && Length(p[1]) is { } c ? c : t;
            r = p.Length > 2 && Length(p[2]) is { } d ? d : r;
            b = p.Length > 3 && Length(p[3]) is { } e ? e : b;
        }

        return (l, t, r, b);
    }

    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0x000000, ["white"] = 0xFFFFFF, ["red"] = 0xFF0000, ["green"] = 0x008000, ["blue"] = 0x0000FF, ["yellow"] = 0xFFFF00,
        ["silver"] = 0xC0C0C0, ["gray"] = 0x808080, ["grey"] = 0x808080, ["maroon"] = 0x800000, ["purple"] = 0x800080, ["fuchsia"] = 0xFF00FF,
        ["lime"] = 0x00FF00, ["olive"] = 0x808000, ["navy"] = 0x000080, ["teal"] = 0x008080, ["aqua"] = 0x00FFFF, ["orange"] = 0xFFA500,
    };

    // "#RRGGBB", "RRGGBB" or a CSS colour name; anything after a space (a colour modifier such as "fill darken(153)") is ignored.
    public static PaintColor? Color(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        s = s.Trim();
        int sp = s.IndexOf(' ', StringComparison.Ordinal);
        if (sp > 0)
        {
            s = s[..sp];
        }

        s = s.TrimStart('#');
        if (Named.TryGetValue(s, out uint named))
        {
            return PaintColor.FromBytes((byte)(named >> 16), (byte)(named >> 8), (byte)named);
        }

        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return PaintColor.FromBytes((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        return null;
    }
}

public static partial class DrawingPainter
{
    private sealed partial class Scene
    {
        // A VML shape at the box the layout gives it: a filled and stroked rectangle, rounded rectangle, ellipse or line, a picture
        // (v:imagedata) and a text box. Shapes the standard defines by a shape type with its own path are drawn as rectangles.
        private void PaintVml(MarkupNode shape, Matrix3x2 parent, DrawingBox? top)
        {
            Xfrm f = ReadXfrm(default, top);
            double wPt = f.W / EmuPerPoint, hPt = f.H / EmuPerPoint;
            Dictionary<string, string> st = VmlShapes.Style(shape);
            if (st.GetValueOrDefault("rotation") is { } rot && double.TryParse(rot, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double deg))
            {
                f = f with { Rot = deg };
            }

            Matrix3x2 m = Placement(f, EmuPerPoint) * parent;
            string kind = shape.LocalNameString;
            string prst = kind switch { "roundrect" => "roundRect", "oval" => "ellipse", "line" => "line", _ => "rect" };
            var adjust = new Dictionary<string, double>(StringComparer.Ordinal);
            if (kind == "roundrect" && shape.GetAttribute("arcsize") is { } arc)
            {
                // arcsize is a fraction of the shorter side, either "20%" or a number out of 65536 (default 0.2).
                double frac = arc.EndsWith('%') ? DrawingInfo.Emu(arc[..^1]) / 100.0 : (arc.EndsWith('f') ? DrawingInfo.Emu(arc[..^1]) / 65536.0 : DrawingInfo.Emu(arc));
                adjust["adj"] = Math.Clamp(frac, 0, 0.5) * 100000;
            }
            else if (kind == "roundrect")
            {
                adjust["adj"] = 20000;
            }

            EvaluatedGeometry eg = GeometryDefinition.Preset(prst)!.Evaluate(wPt, hPt, adjust);

            bool filled = shape.GetAttribute("filled") is not ("f" or "false");
            bool stroked = shape.GetAttribute("stroked") is not ("f" or "false");
            PaintColor? fill = VmlShapes.Color(shape.GetAttribute("fillcolor")) ?? (filled ? PaintColor.FromBytes(255, 255, 255) : null);
            MarkupNode imagedata = default;
            foreach (MarkupNode c in shape.ChildElements)
            {
                if (c.LocalNameString == "imagedata")
                {
                    imagedata = c;
                }
                else if (c.LocalNameString == "fill" && c.GetAttribute("color") is { } fc && VmlShapes.Color(fc) is { } fcc && filled)
                {
                    fill = fcc;
                }
            }

            var line = new LineSpec { Visible = stroked, Width = VmlShapes.Length(shape.GetAttribute("strokeweight")) ?? 0.75 };
            if (VmlShapes.Color(shape.GetAttribute("strokecolor")) is { } sc)
            {
                line.Color = new DrawingColor(sc.R, sc.G, sc.B, sc.A);
            }
            else
            {
                line.Color = new DrawingColor(0, 0, 0);
            }

            double det = Math.Abs((double)m.M11 * m.M22 - ((double)m.M12 * m.M21));
            double widthFix = det > 1e-18 ? PointsToPixels / Math.Sqrt(det) : 1;
            line.Width *= double.IsFinite(widthFix) && widthFix > 0 ? widthFix : 1;
            line.ArrowMin = 1.5 * (double.IsFinite(widthFix) && widthFix > 0 ? widthFix : 1);

            list.PushTransform(nextId(), m);
            foreach (GeometryPath gp in eg.Paths)
            {
                if (imagedata.IsNull && fill is { } fc2 && gp.Fill != PathFillMode.None && filled)
                {
                    list.FillPath(nextId(), gp.Path, Paint.FromColor(fc2));
                }
                else if (!imagedata.IsNull && gp.Fill != PathFillMode.None)
                {
                    string? rid = null;
                    foreach (MarkupAttributeRef a in imagedata.Attributes)
                    {
                        if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual("id"u8))
                        {
                            rid = a.Value;
                        }
                    }

                    PaintBlip(gp.Path, default, wPt, hPt, rid);
                }
            }

            if (stroked)
            {
                foreach (GeometryPath gp in eg.Paths)
                {
                    if (gp.Stroke)
                    {
                        PaintLine(gp.Path, line);
                    }
                }
            }

            list.PopTransform();
            PaintShapeText(shape, eg, m);
        }
    }
}
