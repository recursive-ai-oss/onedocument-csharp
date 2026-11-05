using System.Numerics;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Layout;

// What text has to go around: a floating object's box (and outline) with the distances Word keeps between it and the text.
// Drawings, floating tables and frames all register one of these on the page they sit on.
internal sealed class FloatBox
{
    public LayoutRect Rect;
    public DrawingWrap Wrap;
    public string WrapText = "bothSides";
    public double DistanceTop;
    public double DistanceBottom;
    public double DistanceLeft;
    public double DistanceRight;

    // The object's outline in page coordinates (points): the wrap polygon, or the shape's own outline; null for a rectangle.
    public List<Vector2[]>? Outline;

    public bool AllowOverlap = true;
    public bool WrapsBody = true;

    // The vertical band text must keep out of, including the distances.
    public double TopBound => Rect.Y - DistanceTop;

    public double BottomBound => Rect.Bottom + DistanceBottom;

    // The horizontal extent of the object inside the band [y0, y1] with the side distances added, or false when the band misses it.
    // ECMA-376 Part 1 §20.4.2.17 (wrapSquare): the rectangle plus the distances; §20.4.2.19 (wrapTight): the polygon's extent on each line.
    public bool Extent(double y0, double y1, out double left, out double right)
    {
        left = right = 0;
        if (y1 <= TopBound + 1e-6 || y0 >= BottomBound - 1e-6)
        {
            return false;
        }

        if (Wrap is DrawingWrap.Square or DrawingWrap.TopAndBottom || Outline is null)
        {
            left = Rect.X - DistanceLeft;
            right = Rect.Right + DistanceRight;
            return true;
        }

        // The polygon seen through the band, widened by the vertical distances: a line just under the object is still blocked.
        double a = y0 - DistanceBottom, b = y1 + DistanceTop;
        double minX = double.MaxValue, maxX = double.MinValue;
        foreach (Vector2[] poly in Outline)
        {
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % poly.Length];
                double lo = Math.Min(p.Y, q.Y), hi = Math.Max(p.Y, q.Y);
                if (hi < a || lo > b)
                {
                    continue;
                }

                foreach (double y in new[] { Math.Max(lo, a), Math.Min(hi, b) })
                {
                    double x = Math.Abs(q.Y - p.Y) < 1e-9 ? p.X : p.X + ((q.X - p.X) * (y - p.Y) / (q.Y - p.Y));
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }

                if (Math.Abs(q.Y - p.Y) < 1e-9)
                {
                    minX = Math.Min(minX, Math.Min(p.X, q.X));
                    maxX = Math.Max(maxX, Math.Max(p.X, q.X));
                }
            }
        }

        if (minX > maxX)
        {
            return false;
        }

        left = minX - DistanceLeft;
        right = maxX + DistanceRight;
        return true;
    }
}
