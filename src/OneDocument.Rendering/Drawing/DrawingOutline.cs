using System.Numerics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.Drawing;

// The outline of a drawing's shape as polygons in points, for text that wraps close around it (wrapTight and wrapThrough without
// a wrap polygon).
internal static class DrawingOutline
{
    public static List<Vector2[]>? Polygons(MarkupNode graphic, double widthPoints, double heightPoints)
    {
        MarkupNode shape = DrawingPainter.FindShape(graphic);
        if (shape.IsNull)
        {
            return null;
        }

        MarkupNode spPr = DrawingTheme.Child(shape, "spPr");
        GeometryDefinition? geometry = null;
        var adjust = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (MarkupNode g in spPr.ChildElements)
        {
            if (g.LocalNameString == "prstGeom")
            {
                geometry = GeometryDefinition.Preset(g.GetAttribute("prst") ?? "rect");
                foreach (MarkupNode av in DrawingTheme.Child(g, "avLst").ChildElements)
                {
                    string[] parts = (av.GetAttribute("fmla") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && parts[0] == "val" && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                    {
                        adjust[av.GetAttribute("name") ?? string.Empty] = v;
                    }
                }
            }
            else if (g.LocalNameString == "custGeom")
            {
                geometry = GeometryDefinition.FromCustomGeometry(g);
            }
        }

        if (geometry is null)
        {
            return null;
        }

        EvaluatedGeometry eg = geometry.Evaluate(widthPoints, heightPoints, adjust);
        var result = new List<Vector2[]>();
        foreach (GeometryPath gp in eg.Paths)
        {
            if (gp.Fill == PathFillMode.None)
            {
                continue;
            }

            foreach (Polyline line in PathGeometry.Flatten(gp.Path, Matrix3x2.Identity))
            {
                if (line.Points.Count >= 3)
                {
                    result.Add([.. line.Points]);
                }
            }
        }

        return result.Count == 0 ? null : result;
    }
}
