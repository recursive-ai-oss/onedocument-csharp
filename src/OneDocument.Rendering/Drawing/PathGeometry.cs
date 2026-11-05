using System.Numerics;
using OneRender.Painting;

namespace RecursiveAI.OneDocument.Rendering.Drawing;

/// <summary>A flattened figure: straight segments through the points, optionally closed.</summary>
internal sealed class Polyline(List<Vector2> points, bool closed)
{
    public List<Vector2> Points { get; } = points;

    public bool Closed { get; } = closed;
}

internal enum LineCap
{
    Flat,
    Square,
    Round,
}

internal enum LineJoin
{
    Round,
    Bevel,
    Miter,
}

// Turns outlines into polygons: curves are flattened, and a stroke is the union (non-zero fill) of one quad per segment, a wedge or
// disc per join and a cap per open end. Every piece is wound the same way, which is what lets overlaps fill rather than cancel.
internal static class PathGeometry
{
    private const int MaxPoints = 200_000;

    public static List<Polyline> Flatten(PathBuffer path, Matrix3x2 transform)
    {
        var result = new List<Polyline>();
        ReadOnlySpan<byte> verbs = path.Verbs;
        float[] c = path.Coords.ToArray();
        List<Vector2>? cur = null;
        Vector2 last = default, start = default;
        int ci = 0, total = 0;
        Vector2 P(int i) => Vector2.Transform(new Vector2(c[i], c[i + 1]), transform);
        void End(bool closed)
        {
            if (cur is { Count: > 0 })
            {
                result.Add(new Polyline(cur, closed));
            }

            cur = null;
        }

        foreach (byte verb in verbs)
        {
            if (total > MaxPoints)
            {
                break;
            }

            switch ((PathVerb)verb)
            {
                case PathVerb.Move:
                    End(false);
                    last = start = P(ci);
                    cur = [last];
                    ci += 2;
                    break;
                case PathVerb.Line:
                    cur ??= [last];
                    last = P(ci);
                    cur.Add(last);
                    ci += 2;
                    total++;
                    break;
                case PathVerb.Quad:
                    {
                        cur ??= [last];
                        Vector2 p1 = P(ci), p2 = P(ci + 2);
                        int n = Steps(last, p1, p2);
                        for (int i = 1; i <= n; i++)
                        {
                            float t = i / (float)n, u = 1 - t;
                            cur.Add((u * u * last) + (2 * u * t * p1) + (t * t * p2));
                        }

                        last = p2;
                        ci += 4;
                        total += n;
                        break;
                    }

                case PathVerb.Cubic:
                    {
                        cur ??= [last];
                        Vector2 p1 = P(ci), p2 = P(ci + 2), p3 = P(ci + 4);
                        int n = Steps(last, p1, p2, p3);
                        for (int i = 1; i <= n; i++)
                        {
                            float t = i / (float)n, u = 1 - t;
                            cur.Add((u * u * u * last) + (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t * p3));
                        }

                        last = p3;
                        ci += 6;
                        total += n;
                        break;
                    }

                case PathVerb.Close:
                    End(true);
                    last = start;
                    break;
            }
        }

        End(false);
        return result;
    }

    private static int Steps(params Vector2[] pts)
    {
        float len = 0;
        for (int i = 1; i < pts.Length; i++)
        {
            len += Vector2.Distance(pts[i - 1], pts[i]);
        }

        return Math.Clamp((int)Math.Ceiling(Math.Sqrt(len) * 1.5), 4, 48);
    }

    public static PathBuffer ToPath(List<Polyline> lines)
    {
        var pb = new PathBuffer();
        foreach (Polyline l in lines)
        {
            if (l.Points.Count < 2)
            {
                continue;
            }

            pb.MoveTo(l.Points[0].X, l.Points[0].Y);
            for (int i = 1; i < l.Points.Count; i++)
            {
                pb.LineTo(l.Points[i].X, l.Points[i].Y);
            }

            if (l.Closed)
            {
                pb.Close();
            }
        }

        return pb;
    }

    // Splits polylines into dashes. pattern holds alternating on/off lengths in the same unit as the points.
    public static List<Polyline> Dash(List<Polyline> lines, double[] pattern)
    {
        var result = new List<Polyline>();
        double sum = 0;
        foreach (double d in pattern)
        {
            sum += d;
        }

        if (pattern.Length == 0 || sum < 1e-6)
        {
            return lines;
        }

        foreach (Polyline line in lines)
        {
            List<Vector2> pts = line.Points;
            if (line.Closed && pts.Count > 1)
            {
                pts = [.. pts, pts[0]];
            }

            int idx = 0;
            double remain = pattern[0];
            bool on = true;
            var cur = new List<Vector2>();
            if (pts.Count > 0)
            {
                cur.Add(pts[0]);
            }

            int emitted = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 a = pts[i - 1], b = pts[i];
                double segLen = Vector2.Distance(a, b);
                double pos = 0;
                while (segLen - pos > remain && emitted < 100_000)
                {
                    pos += remain;
                    Vector2 p = Vector2.Lerp(a, b, (float)(pos / segLen));
                    if (on)
                    {
                        cur.Add(p);
                        result.Add(new Polyline(cur, false));
                        emitted++;
                        cur = [];
                    }
                    else
                    {
                        cur = [p];
                    }

                    on = !on;
                    idx = (idx + 1) % pattern.Length;
                    remain = Math.Max(pattern[idx], 1e-4);
                }

                remain -= segLen - pos;
                if (on)
                {
                    cur.Add(b);
                }
            }

            if (on && cur.Count > 1)
            {
                result.Add(new Polyline(cur, false));
            }
        }

        return result;
    }

    public static PathBuffer Stroke(List<Polyline> lines, double width, LineCap cap, LineJoin join, double miterLimit)
    {
        var pb = new PathBuffer();
        float hw = (float)(width / 2);
        if (hw <= 0)
        {
            return pb;
        }

        foreach (Polyline line in lines)
        {
            List<Vector2> pts = Dedupe(line.Points, line.Closed);
            if (pts.Count == 1)
            {
                if (cap == LineCap.Round)
                {
                    Circle(pb, pts[0], hw);
                }
                else if (cap == LineCap.Square)
                {
                    Poly(pb, new Vector2(pts[0].X - hw, pts[0].Y - hw), new Vector2(pts[0].X + hw, pts[0].Y - hw), new Vector2(pts[0].X + hw, pts[0].Y + hw), new Vector2(pts[0].X - hw, pts[0].Y + hw));
                }

                continue;
            }

            if (pts.Count < 2)
            {
                continue;
            }

            int n = pts.Count;
            int segs = line.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % n];
                Vector2 d = Vector2.Normalize(b - a);
                Vector2 nrm = new Vector2(-d.Y, d.X) * hw;
                Poly(pb, a + nrm, b + nrm, b - nrm, a - nrm);
            }

            int joinFrom = line.Closed ? 0 : 1;
            int joinTo = line.Closed ? n : n - 1;
            for (int i = joinFrom; i < joinTo; i++)
            {
                Vector2 prev = pts[(i - 1 + n) % n], v = pts[i], next = pts[(i + 1) % n];
                Join(pb, prev, v, next, hw, join, (float)miterLimit);
            }

            if (!line.Closed)
            {
                Cap(pb, pts[1], pts[0], hw, cap);
                Cap(pb, pts[n - 2], pts[n - 1], hw, cap);
            }
        }

        return pb;
    }

    // The line shifted sideways by d (positive to the left of its direction). Corners use the averaged normal, limited so sharp
    // corners do not spike.
    public static List<Polyline> Offset(List<Polyline> lines, double d)
    {
        var result = new List<Polyline>(lines.Count);
        foreach (Polyline line in lines)
        {
            List<Vector2> pts = Dedupe(line.Points, line.Closed);
            int n = pts.Count;
            if (n < 2)
            {
                continue;
            }

            var o = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                bool hasPrev = line.Closed || i > 0, hasNext = line.Closed || i < n - 1;
                Vector2 n1 = hasPrev ? Normal(pts[(i - 1 + n) % n], pts[i]) : Normal(pts[i], pts[i + 1]);
                Vector2 n2 = hasNext ? Normal(pts[i], pts[(i + 1) % n]) : n1;
                Vector2 m = n1 + n2;
                float len = m.Length();
                m = len < 1e-6f ? n1 : m / len;
                float scale = 1f / Math.Max(0.5f, Vector2.Dot(m, n1));
                o.Add(pts[i] + (m * (float)d * scale));
            }

            result.Add(new Polyline(o, line.Closed));
        }

        return result;
    }

    private static Vector2 Normal(Vector2 a, Vector2 b)
    {
        Vector2 d = Vector2.Normalize(b - a);
        return new Vector2(-d.Y, d.X);
    }

    private static List<Vector2> Dedupe(List<Vector2> pts, bool closed)
    {
        var r = new List<Vector2>(pts.Count);
        foreach (Vector2 p in pts)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y))
            {
                continue;
            }

            if (r.Count == 0 || Vector2.DistanceSquared(r[^1], p) > 1e-8f)
            {
                r.Add(p);
            }
        }

        if (closed && r.Count > 1 && Vector2.DistanceSquared(r[0], r[^1]) <= 1e-8f)
        {
            r.RemoveAt(r.Count - 1);
        }

        return r;
    }

    private static void Join(PathBuffer pb, Vector2 prev, Vector2 v, Vector2 next, float hw, LineJoin join, float miterLimit)
    {
        Vector2 d1 = Vector2.Normalize(v - prev), d2 = Vector2.Normalize(next - v);
        float cross = (d1.X * d2.Y) - (d1.Y * d2.X);
        if (Math.Abs(cross) < 1e-5f && Vector2.Dot(d1, d2) > 0)
        {
            return;
        }

        if (join == LineJoin.Round)
        {
            Circle(pb, v, hw);
            return;
        }

        // The outer side of the turn is opposite the direction the line turns toward.
        float side = cross > 0 ? -1f : 1f;
        Vector2 n1 = new Vector2(-d1.Y, d1.X) * hw * side, n2 = new Vector2(-d2.Y, d2.X) * hw * side;
        if (join == LineJoin.Miter)
        {
            float cosHalf = Vector2.Dot(Vector2.Normalize(n1), Vector2.Normalize(n1 + n2));
            if (cosHalf > 1e-3f && 1f / cosHalf <= miterLimit)
            {
                Vector2 tip = v + ((n1 + n2) / (2 * cosHalf * cosHalf));
                Poly(pb, v, v + n1, tip, v + n2);
                return;
            }
        }

        Poly(pb, v, v + n1, v + n2);
    }

    private static void Cap(PathBuffer pb, Vector2 from, Vector2 end, float hw, LineCap cap)
    {
        if (cap == LineCap.Flat)
        {
            return;
        }

        if (cap == LineCap.Round)
        {
            Circle(pb, end, hw);
            return;
        }

        Vector2 d = Vector2.Normalize(end - from) * hw;
        Vector2 nrm = new(-d.Y, d.X);
        Poly(pb, end + nrm, end + nrm + d, end - nrm + d, end - nrm);
    }

    private static void Circle(PathBuffer pb, Vector2 c, float r)
    {
        int n = r < 2 ? 12 : r < 10 ? 20 : 32;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts[i] = new Vector2(c.X + (float)(r * Math.Cos(a)), c.Y + (float)(r * Math.Sin(a)));
        }

        Poly(pb, pts);
    }

    // Adds a polygon wound in one fixed direction whatever order the points came in.
    private static void Poly(PathBuffer pb, params Vector2[] pts)
    {
        double area = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        if (Math.Abs(area) < 1e-9)
        {
            return;
        }

        bool reverse = area < 0;
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 p = pts[reverse ? pts.Length - 1 - i : i];
            if (i == 0)
            {
                pb.MoveTo(p.X, p.Y);
            }
            else
            {
                pb.LineTo(p.X, p.Y);
            }
        }

        pb.Close();
    }
}
