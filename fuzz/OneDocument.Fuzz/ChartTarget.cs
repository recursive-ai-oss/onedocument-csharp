using System;
using System.IO;
using System.Text;
using OneRender.Painting;
using RecursiveAI.OneDocument.Charts;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Rendering.Tests;
using RecursiveAI.OneDocument.Tools.ChartProbe;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Chart parts and DrawingML trees read as standalone parts. <c>chart</c> reads a classic or chartex part into the chart model, walks the
/// model and paints it into a display list, and reads the same bytes as a chart theme override. <c>drawing</c> paints a shape tree (<c>p:spTree</c>,
/// a group or a worksheet drawing) with pictures available, paints it as a background, and evaluates every custom geometry in it. Only
/// <see cref="OfficeException"/> may escape, and a finished display list must validate.
/// </summary>
internal static class ChartTarget
{
    private static readonly OfficeLimits Limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 20, MaxRecursionDepth = 32 };

    public static void RunChart(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        _ = ChartTheme.TryParse(bytes);
        if (!ChartReader.TryRead(bytes, out ChartSpace? chart, "/chart.xml") || chart is null)
        {
            return;
        }

        long points = 0;
        foreach (ChartGroup g in chart.Groups)
        {
            foreach (ChartSeries s in g.Series)
            {
                points += (s.Values?.Numbers.Count ?? 0) + (s.Categories?.Strings.Count ?? 0);
            }
        }

        if (points < 0)
        {
            throw new InvalidOperationException();
        }

        try
        {
            var list = new DisplayList();
            _ = ChartHost.Paint(list, chart, 0, 0, 480, 320);
            list.Validate();
        }
        catch (OfficeException)
        {
        }
    }

    public static void RunDrawing(ReadOnlySpan<byte> data)
    {
        try
        {
            MarkupDocument doc = MarkupDocument.Parse(data.ToArray(), new OpenContext(Limits), "/drawing.xml");
            var list = new DisplayList();
            uint n = 0;
            var ctx = new DrawingContext(imageBytes: _ => Png);
            _ = DrawingPainter.DrawShapes(list, () => ItemId.Of(++n), doc.Root, 0, 0, ctx);
            _ = DrawingPainter.DrawBackground(list, () => ItemId.Of(++n), doc.Root, new DrawingBox(0, 0, 400, 300), ctx);
            list.Validate();
            Geometries(doc.Root, 0);
        }
        catch (OfficeException)
        {
        }
    }

    private static readonly byte[] Png = DrawingSamples.Png(8, 8);

    private static void Geometries(MarkupNode node, int depth)
    {
        if (depth > 40)
        {
            return;
        }

        foreach (MarkupNode c in node.ChildElements)
        {
            if (c.LocalNameString == "custGeom")
            {
                _ = GeometryDefinition.FromCustomGeometry(c).Evaluate(120, 60);
            }

            Geometries(c, depth + 1);
        }
    }

    public static int WriteChartSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (ChartSamples.Sample s in ChartSamples.All())
        {
            File.WriteAllText(Path.Combine(dir, s.Name + ".xml"), s.Xml);
        }

        return 0;
    }

    public static int WriteDrawingSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "slide-tree.xml"), DrawingSamples.SlideTree());
        File.WriteAllText(Path.Combine(dir, "custgeom.xml"),
            "<p:spTree xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"c\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"635000\"/></a:xfrm>" +
            "<a:custGeom><a:avLst/><a:gdLst><a:gd name=\"x1\" fmla=\"*/ w 1 3\"/></a:gdLst><a:pathLst><a:path w=\"100\" h=\"100\"><a:moveTo><a:pt x=\"0\" y=\"0\"/></a:moveTo><a:lnTo><a:pt x=\"100\" y=\"50\"/></a:lnTo><a:arcTo wR=\"50\" hR=\"50\" stAng=\"0\" swAng=\"5400000\"/><a:cubicBezTo><a:pt x=\"10\" y=\"20\"/><a:pt x=\"30\" y=\"40\"/><a:pt x=\"50\" y=\"60\"/></a:cubicBezTo><a:close/></a:path></a:pathLst></a:custGeom><a:solidFill><a:srgbClr val=\"336699\"/></a:solidFill><a:ln w=\"12700\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:prstDash val=\"dash\"/></a:ln></p:spPr></p:sp></p:spTree>");
        return 0;
    }
}
