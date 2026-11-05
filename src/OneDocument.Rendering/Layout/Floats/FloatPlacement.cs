using System.Numerics;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Layout;

// Where an anchored drawing sits, from the numbers the anchor states and the places it can be relative to.
internal readonly record struct FloatContext(
    double PageWidth,
    double PageHeight,
    LayoutRect Area,
    double ColumnX,
    double ColumnWidth,
    double ParagraphTop,
    double ParagraphHeight,
    double LineTop,
    double LineHeight,
    double AnchorX,
    bool OddPage);

internal static class FloatPlacement
{
    // ECMA-376 Part 1 §20.4.2.10 (positionH) and §20.4.2.11 (positionV): the position is an offset or an alignment, measured from the
    // edge named by relativeFrom. §20.4.2.13 (simplePos) puts the object at absolute page coordinates and overrides the rest.
    public static LayoutRect Place(DrawingInfo info, in FloatContext c, bool clampToPage)
    {
        DrawingAnchor a = info.Anchor ?? new DrawingAnchor();
        double w = info.Width, h = info.Height;
        // [MS-ODRAWXML] wp14:sizeRelH / sizeRelV: the size as a fraction of a page-level base overrides the stated extent.
        if (a.PercentWidth is > 0 and { } pw)
        {
            (_, double bw) = HBase(a.SizeRelativeH ?? "margin", c);
            w = pw * bw;
        }

        if (a.PercentHeight is > 0 and { } ph)
        {
            (_, double bh) = VBase(a.SizeRelativeV ?? "margin", c);
            h = ph * bh;
        }

        if (a.SimplePosition)
        {
            return new LayoutRect(a.SimpleX, a.SimpleY, w, h);
        }

        (double bx, double bwid) = HBase(info.RelativeFromH ?? "column", c);
        (double by, double bhgt) = VBase(info.RelativeFromV ?? "paragraph", c);
        double x = info.AlignH switch
        {
            "center" => bx + ((bwid - w) / 2),
            "left" => bx,
            "right" => bx + bwid - w,
            "inside" => c.OddPage ? bx : bx + bwid - w,
            "outside" => c.OddPage ? bx + bwid - w : bx,
            _ => a.PercentX is { } px ? bx + (px * bwid) : bx + (info.OffsetX ?? 0),
        };
        double y = info.AlignV switch
        {
            "center" => by + ((bhgt - h) / 2),
            "top" or "inside" => by,
            "bottom" or "outside" => by + bhgt - h,
            _ => a.PercentY is { } py ? by + (py * bhgt) : by + (info.OffsetY ?? 0),
        };

        if (clampToPage)
        {
            // Word keeps an object that is positioned relative to the text on the page it is anchored on.
            if (y + h > c.PageHeight)
            {
                y = Math.Max(0, c.PageHeight - h);
            }

            if (y < 0)
            {
                y = 0;
            }
        }

        return new LayoutRect(x, y, w, h);
    }

    private static (double X, double Width) HBase(string from, in FloatContext c) => from switch
    {
        "page" => (0, c.PageWidth),
        "margin" => (c.Area.X, c.Area.Width),
        "column" => (c.ColumnX, c.ColumnWidth),
        "character" => (c.AnchorX, 0),
        "leftMargin" => (0, c.Area.X),
        "rightMargin" => (c.Area.Right, c.PageWidth - c.Area.Right),
        "insideMargin" => c.OddPage ? (0, c.Area.X) : (c.Area.Right, c.PageWidth - c.Area.Right),
        "outsideMargin" => c.OddPage ? (c.Area.Right, c.PageWidth - c.Area.Right) : (0, c.Area.X),
        _ => (c.ColumnX, c.ColumnWidth),
    };

    private static (double Y, double Height) VBase(string from, in FloatContext c) => from switch
    {
        "page" => (0, c.PageHeight),
        "margin" => (c.Area.Y, c.Area.Height),
        "paragraph" => (c.ParagraphTop, c.ParagraphHeight),
        "line" => (c.LineTop, c.LineHeight),
        "topMargin" or "insideMargin" => (0, c.Area.Y),
        "bottomMargin" or "outsideMargin" => (c.Area.Bottom, c.PageHeight - c.Area.Bottom),
        _ => (c.ParagraphTop, c.ParagraphHeight),
    };

    // The box text has to keep out of, with the outline text wraps close to for tight and through wrapping.
    public static FloatBox Box(InlineDrawing drawing, LayoutRect rect, bool wrapsBody)
    {
        DrawingAnchor a = drawing.Info.Anchor ?? new DrawingAnchor();
        (double el, double et, double er, double eb) = a.EffectExtent;
        var box = new FloatBox
        {
            Rect = new LayoutRect(rect.X - el, rect.Y - et, rect.Width + el + er, rect.Height + et + eb),
            Wrap = a.Wrap,
            WrapText = a.WrapText,
            DistanceTop = a.DistanceTop,
            DistanceBottom = a.DistanceBottom,
            DistanceLeft = a.DistanceLeft,
            DistanceRight = a.DistanceRight,
            AllowOverlap = a.AllowOverlap,
            WrapsBody = wrapsBody,
        };
        if (a.Wrap is DrawingWrap.Tight or DrawingWrap.Through)
        {
            if (a.Polygon is { } poly)
            {
                // ECMA-376 Part 1 §20.4.2.20 (wrapPolygon): points in 21600ths of the object's width and height.
                var pts = new Vector2[poly.Count];
                for (int i = 0; i < pts.Length; i++)
                {
                    pts[i] = new Vector2((float)(rect.X + (poly[i].X / 21600.0 * rect.Width)), (float)(rect.Y + (poly[i].Y / 21600.0 * rect.Height)));
                }

                box.Outline = [pts];
            }
            else if (DrawingOutline.Polygons(drawing.Info.Graphic, rect.Width, rect.Height) is { } outline)
            {
                // Without a polygon Word wraps to the shape's own outline.
                var moved = new List<Vector2[]>(outline.Count);
                foreach (Vector2[] shape in outline)
                {
                    var pts = new Vector2[shape.Length];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        pts[i] = new Vector2((float)(rect.X + shape[i].X), (float)(rect.Y + shape[i].Y));
                    }

                    moved.Add(pts);
                }

                box.Outline = moved;
            }
        }

        return box;
    }
}
