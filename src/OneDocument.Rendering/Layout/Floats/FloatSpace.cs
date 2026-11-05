using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Layout;

// Where text may go on a band of the page. The paragraph formatter asks for each row of lines in turn.
internal interface ILineSpace
{
    // The y (relative to where the paragraph's lines start) the first line of the paragraph's lines sits at.
    double StartOffset { get; }

    // Free stretches for a row of the given height at yRel, as flow coordinates (distance from the text area's leading edge), left to
    // right in reading order. Returns false when no text fits in the band; `skipTo` is then the yRel to try next.
    bool Segments(double yRel, double height, List<(double Start, double End)> segments, out double skipTo);
}

internal sealed class FloatSpace(IReadOnlyList<FloatBox> boxes, double y0, double startOffset, double colLeft, double colRight, bool rtl) : ILineSpace
{
    // The narrowest stretch text is put in; narrower gaps beside an object are left empty. The figure is an approximation of Word's
    // behaviour and is listed in docs/layout-floats.md as needing Word's own output.
    internal const double MinimumSegment = 12;

    public double StartOffset => startOffset;

    public bool Segments(double yRel, double height, List<(double Start, double End)> segments, out double skipTo)
    {
        segments.Clear();
        skipTo = yRel + Math.Max(height, 1);
        double top = y0 + yRel, bottom = top + Math.Max(height, 0.01);
        var blocked = new List<(double A, double B)>();
        double nextEdge = double.MaxValue;
        string? onlySide = null;
        foreach (FloatBox box in boxes)
        {
            if (!box.WrapsBody || box.Wrap == DrawingWrap.None)
            {
                continue;
            }

            if (!box.Extent(top, bottom, out double l, out double r))
            {
                continue;
            }

            if (r <= colLeft || l >= colRight)
            {
                continue;
            }

            nextEdge = Math.Min(nextEdge, box.BottomBound);
            if (box.Wrap == DrawingWrap.TopAndBottom)
            {
                // ECMA-376 Part 1 §20.4.2.18 (wrapTopAndBottom): nothing beside the object; text resumes below it.
                skipTo = box.BottomBound - y0;
                return false;
            }

            switch (box.WrapText)
            {
                case "left":
                    // Text only on the left of the object: everything from its left edge on is blocked.
                    blocked.Add((l, double.MaxValue));
                    break;
                case "right":
                    blocked.Add((double.MinValue, r));
                    break;
                case "largest":
                    onlySide = "largest";
                    blocked.Add((l, r));
                    break;
                default:
                    blocked.Add((l, r));
                    break;
            }
        }

        if (blocked.Count == 0)
        {
            segments.Add(Flow(colLeft, colRight));
            return true;
        }

        blocked.Sort(static (x, y) => x.A.CompareTo(y.A));
        double cursor = colLeft;
        var free = new List<(double, double)>();
        foreach ((double a, double b) in blocked)
        {
            if (a > cursor)
            {
                free.Add((cursor, Math.Min(a, colRight)));
            }

            cursor = Math.Max(cursor, b);
            if (cursor >= colRight)
            {
                break;
            }
        }

        if (cursor < colRight)
        {
            free.Add((cursor, colRight));
        }

        free.RemoveAll(static f => f.Item2 - f.Item1 < MinimumSegment);
        if (onlySide == "largest" && free.Count > 1)
        {
            (double, double) best = free[0];
            foreach ((double, double) f in free)
            {
                if (f.Item2 - f.Item1 > best.Item2 - best.Item1)
                {
                    best = f;
                }
            }

            free = [best];
        }

        if (free.Count == 0)
        {
            skipTo = Math.Max(nextEdge, top + 0.5) - y0;
            return false;
        }

        foreach ((double a, double b) in free)
        {
            segments.Add(Flow(a, b));
        }

        if (rtl)
        {
            segments.Reverse();
        }

        return true;
    }

    // Page x to flow x: from the leading edge of the column, which is its right edge in a right-to-left paragraph.
    private (double, double) Flow(double a, double b) => rtl ? (colRight - b, colRight - a) : (a - colLeft, b - colLeft);
}
