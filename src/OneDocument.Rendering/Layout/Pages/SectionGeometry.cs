using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// One side of a page border. ECMA-376 Part 1 §17.6.2 (pgBorders), §17.18.2 (ST_Border).
internal readonly record struct PageBorderSide(string Style, double Width, double Space, uint Color, bool Art);

internal sealed class PageBorders
{
    public PageBorderSide? Top;
    public PageBorderSide? Left;
    public PageBorderSide? Bottom;
    public PageBorderSide? Right;
    public bool FromPage; // offsetFrom="page": space is measured from the page edge, else from the text area
    public string Display = "allPages";
}

// A column's left edge relative to the text area and its width, in points.
internal readonly record struct ColumnSlot(double X, double Width);

// The geometry of one section's pages, read once from its section properties. Sizes are points (the file stores twentieths).
internal sealed class SectionGeometry
{
    public Section? Source;
    public double Width;
    public double Height;
    public double Top;
    public double Bottom;
    public double Left; // margins as written, before the gutter
    public double Right;
    public double Gutter;
    public double Header; // distance from the page edge to the header text
    public double Footer;
    public bool Mirror;
    public bool GutterAtTop;
    public bool Rtl;
    public ColumnSlot[] Columns = [];
    public bool Separator;
    public string VAlign = "top";
    public PageBorders? Borders;
    public double GridPitch; // points; 0 when the section has no line grid
    public int Index;

    // Letter size with one-inch margins, for a document with no section properties at all.
    public static SectionGeometry Default() => new()
    {
        Width = 612,
        Height = 792,
        Top = 72,
        Bottom = 72,
        Left = 72,
        Right = 72,
        Header = 36,
        Footer = 36,
        Columns = [new ColumnSlot(0, 468)],
    };

    public static SectionGeometry Read(Section s, CompatSettings compat)
    {
        PageSize size = s.PageSize;
        PageMargins m = s.Margins;
        var g = new SectionGeometry
        {
            Source = s,
            Index = s.Index,
            Width = size.Width / 20.0,
            Height = size.Height / 20.0,
            Top = m.Top / 20.0,
            Bottom = m.Bottom / 20.0,
            Left = m.Left / 20.0,
            Right = m.Right / 20.0,
            Gutter = m.Gutter / 20.0,
            Header = m.Header / 20.0,
            Footer = m.Footer / 20.0,
            Mirror = compat.MirrorMargins,
            GutterAtTop = compat.GutterAtTop,
            Rtl = s.IsRightToLeft,
            VAlign = s.VerticalAlignment ?? "top",
        };
        g.Columns = ReadColumns(s, g.Width - g.Left - g.Right - (g.GutterAtTop ? 0 : g.Gutter), g.Rtl, out g.Separator);
        g.Borders = ReadBorders(s);
        if (s.DocumentGrid is { Type: "lines" or "linesAndChars" or "snapToChars", LinePitch: > 0 } grid)
        {
            // ECMA-376 Part 1 §17.6.5 (docGrid): linePitch is the grid's line height in twentieths of a point. Only the line pitch
            // is applied; the character grid (charSpace) is not.
            g.GridPitch = grid.LinePitch.Value / 20.0;
        }

        return g;
    }

    // The text area of a page whose printed number is `printed`, before a header or footer pushes it.
    // ECMA-376 Part 1 §17.15.1.57 (mirrorMargins): odd pages keep the inside margin on the left, even pages on the right; the gutter
    // (§17.6.11 pgMar gutter) sits on the inside. Without mirroring it is added on the left (right for a right-to-left section,
    // top when gutterAtTop is set, §17.15.1.46).
    public LayoutRect TextArea(long printed)
    {
        double l = Left;
        double r = Right;
        double t = Top;
        double gl = GutterAtTop ? 0 : Gutter;
        if (GutterAtTop)
        {
            t += Gutter;
        }

        if (Mirror)
        {
            if ((printed & 1) == 0)
            {
                (l, r) = (r, l + gl);
            }
            else
            {
                l += gl;
            }
        }
        else if (Rtl)
        {
            r += gl;
        }
        else
        {
            l += gl;
        }

        return new LayoutRect(l, t, Math.Max(1, Width - l - r), Math.Max(1, Height - t - Bottom));
    }

    // ECMA-376 Part 1 §17.6.4 (cols): equal columns share the width left after the gaps; otherwise each w:col has its own width and
    // the gap that follows it.
    private static ColumnSlot[] ReadColumns(Section s, double textWidth, bool rtl, out bool separator)
    {
        SectionColumns cols = s.Columns;
        separator = cols.Separator && cols.Count > 1;
        textWidth = Math.Max(1, textWidth);
        var slots = new List<ColumnSlot>();
        if (cols.Count <= 1)
        {
            slots.Add(new ColumnSlot(0, textWidth));
            return [.. slots];
        }

        if (cols.EqualWidth || cols.Columns.Count == 0)
        {
            double space = cols.Space / 20.0;
            double w = (textWidth - ((cols.Count - 1) * space)) / cols.Count;
            if (w < 12)
            {
                // Gaps this wide leave no room for text; Word shrinks them.
                space = Math.Max(0, (textWidth - (12 * cols.Count)) / (cols.Count - 1));
                w = (textWidth - ((cols.Count - 1) * space)) / cols.Count;
            }

            for (int i = 0; i < cols.Count; i++)
            {
                slots.Add(new ColumnSlot(i * (w + space), w));
            }
        }
        else
        {
            double x = 0;
            for (int i = 0; i < cols.Columns.Count; i++)
            {
                ColumnDefinition c = cols.Columns[i];
                double w = Math.Max(12, (c.Width ?? 0) / 20.0);
                if (x + w > textWidth)
                {
                    w = Math.Max(12, textWidth - x);
                }

                slots.Add(new ColumnSlot(x, w));
                x += w + ((c.Space ?? 0) / 20.0);
            }
        }

        if (rtl)
        {
            // A right-to-left section starts its first column at the right edge.
            for (int i = 0; i < slots.Count; i++)
            {
                slots[i] = new ColumnSlot(textWidth - slots[i].X - slots[i].Width, slots[i].Width);
            }
        }

        return [.. slots];
    }

    private static PageBorders? ReadBorders(Section s)
    {
        if (Xml.Child(s.PropertiesElement, "pgBorders") is not { } e)
        {
            return null;
        }

        var b = new PageBorders { FromPage = Xml.Attr(e, "offsetFrom") == "page", Display = Xml.Attr(e, "display") ?? "allPages" };
        foreach (MarkupRef side in Xml.Elements(e))
        {
            string name = System.Text.Encoding.UTF8.GetString(side.LocalName);
            string style = Xml.Attr(side, "val") ?? "nil";
            if (style is "nil" or "none")
            {
                continue;
            }

            string? art = Xml.Attr(side, "art");
            double width = (Xml.Long(Xml.Attr(side, "sz")) ?? 4) / 8.0; // eighths of a point
            double space = Math.Min(31, Xml.Long(Xml.Attr(side, "space")) ?? 0);
            uint color = 0;
            string? c = Xml.Attr(side, "color");
            if (c is { Length: 6 } && uint.TryParse(c, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
            {
                color = rgb;
            }

            var ps = new PageBorderSide(style, width, space, color, art is not null);
            switch (name)
            {
                case "top":
                    b.Top = ps;
                    break;
                case "left" or "start":
                    b.Left = ps;
                    break;
                case "bottom":
                    b.Bottom = ps;
                    break;
                case "right" or "end":
                    b.Right = ps;
                    break;
            }
        }

        return b.Top is null && b.Left is null && b.Bottom is null && b.Right is null ? null : b;
    }
}
