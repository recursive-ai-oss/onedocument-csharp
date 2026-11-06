using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;
using static RecursiveAI.OneDocument.Ppt.Binary.PptXml;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>Turns OfficeArt freeform geometry (vertices and path segments) into DrawingML custom geometry.</summary>
internal static class PptGeometry
{
    /// <summary>The <c>a:custGeom</c> for a shape's vertices, or null when the shape has no usable vertex list.</summary>
    public static string? Custom(ArtShape s, ILossSink loss)
    {
        ArtProperties p = s.Properties;
        IReadOnlyList<ReadOnlyMemory<byte>> raw = p.GetArray(ArtPropertyId.Vertices, out int width);
        if (raw.Count == 0 || width is not (4 or 8))
        {
            return null;
        }

        var points = new List<(long X, long Y)>(raw.Count);
        bool guided = false;
        foreach (ReadOnlyMemory<byte> element in raw)
        {
            ReadOnlySpan<byte> e = element.Span;
            long x;
            long y;
            if (width == 8)
            {
                x = BinaryPrimitives.ReadInt32LittleEndian(e);
                y = BinaryPrimitives.ReadInt32LittleEndian(e[4..]);
            }
            else
            {
                x = BinaryPrimitives.ReadInt16LittleEndian(e);
                y = BinaryPrimitives.ReadInt16LittleEndian(e[2..]);
            }

            // [MS-ODRAW] §2.3.6.7: a coordinate from 0x80000000 to 0x8000007F refers to a guide formula, which this converter does not evaluate.
            if ((uint)x >= 0x80000000 && (uint)x <= 0x8000007F || (uint)y >= 0x80000000 && (uint)y <= 0x8000007F)
            {
                guided = true;
                x = 0;
                y = 0;
            }

            points.Add((x, y));
        }

        if (guided)
        {
            loss.Report("ppt.geometry-guides", "custom geometry", 1, "A freeform shape uses guide formulas for some coordinates; those points are written at the origin.");
        }

        int left = p.GetInt(ArtPropertyId.GeoLeft, 0);
        int top = p.GetInt(ArtPropertyId.GeoTop, 0);
        long w = Math.Max(1, (long)p.GetInt(ArtPropertyId.GeoRight, 21600) - left);
        long h = Math.Max(1, (long)p.GetInt(ArtPropertyId.GeoBottom, 21600) - top);
        var sb = new StringBuilder("<a:custGeom><a:avLst/><a:gdLst/><a:ahLst/><a:cxnLst/><a:rect l=\"0\" t=\"0\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"");
        sb.Append(Num(w)).Append("\" h=\"").Append(Num(h)).Append("\">");

        string Pt((long X, long Y) q) => "<a:pt x=\"" + Num(q.X - left) + "\" y=\"" + Num(q.Y - top) + "\"/>";

        IReadOnlyList<ReadOnlyMemory<byte>> segments = p.GetArray(ArtPropertyId.SegmentInfo, out int segmentWidth);
        int next = 0;
        bool open = false;
        if (segments.Count == 0 || segmentWidth != 2)
        {
            // Without segment info, the shapePath property decides how the vertices connect: 0 lines, 1 closed lines, 2 curves, 3 closed curves.
            int shapePath = p.GetInt(ArtPropertyId.ShapePath, 1);
            sb.Append("<a:moveTo>").Append(Pt(points[0])).Append("</a:moveTo>");
            if (shapePath is 2 or 3)
            {
                for (int i = 1; i + 2 < points.Count; i += 3)
                {
                    sb.Append("<a:cubicBezTo>").Append(Pt(points[i])).Append(Pt(points[i + 1])).Append(Pt(points[i + 2])).Append("</a:cubicBezTo>");
                }
            }
            else
            {
                for (int i = 1; i < points.Count; i++)
                {
                    sb.Append("<a:lnTo>").Append(Pt(points[i])).Append("</a:lnTo>");
                }
            }

            if (shapePath is 1 or 3)
            {
                sb.Append("<a:close/>");
            }
        }
        else
        {
            foreach (ReadOnlyMemory<byte> segment in segments)
            {
                ushort info = BinaryPrimitives.ReadUInt16LittleEndian(segment.Span);
                int type = info >> 13;
                int count = info & 0x1FFF;
                switch (type)
                {
                    case 0:
                        for (int k = 0; k < count && next < points.Count; k++, next++)
                        {
                            if (!open)
                            {
                                sb.Append("<a:moveTo>").Append(Pt(points[next])).Append("</a:moveTo>");
                                open = true;
                                continue;
                            }

                            sb.Append("<a:lnTo>").Append(Pt(points[next])).Append("</a:lnTo>");
                        }

                        break;
                    case 1:
                        for (int k = 0; k < count && next + 2 < points.Count; k++, next += 3)
                        {
                            if (!open)
                            {
                                sb.Append("<a:moveTo>").Append(Pt(points[next])).Append("</a:moveTo>");
                                open = true;
                            }

                            sb.Append("<a:cubicBezTo>").Append(Pt(points[next])).Append(Pt(points[next + 1])).Append(Pt(points[next + 2])).Append("</a:cubicBezTo>");
                        }

                        break;
                    case 2:
                        if (next < points.Count)
                        {
                            sb.Append("<a:moveTo>").Append(Pt(points[next++])).Append("</a:moveTo>");
                            open = true;
                        }

                        break;
                    case 3:
                        sb.Append("<a:close/>");
                        open = false;
                        break;
                    case 4:
                        break;
                    default:
                        // Escapes carry arcs, fill and stroke switches, and host extensions; none is converted.
                        loss.Report("ppt.path-escape", "custom geometry", 1, "A freeform path uses an escape segment (arcs or fill switches), which is not converted.");
                        break;
                }
            }
        }

        return sb.Append("</a:path></a:pathLst></a:custGeom>").ToString();
    }
}
