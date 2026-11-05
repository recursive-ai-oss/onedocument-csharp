using System.Text;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Paints a shape tree (preset, custom geometry, gradient, pattern, picture, dashed line, shadow) to a raster and a tagged PDF: under NativeAOT and WASM.</summary>
internal static class DrawingSmoke
{
    private const string Tree =
        "<p:spTree xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<p:sp><p:spPr><a:xfrm><a:off x=\"635000\" y=\"635000\"/><a:ext cx=\"2540000\" cy=\"1270000\"/></a:xfrm><a:prstGeom prst=\"star5\"><a:avLst/></a:prstGeom><a:gradFill><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"accent1\"/></a:gs><a:gs pos=\"100000\"><a:srgbClr val=\"102030\"/></a:gs></a:gsLst><a:lin ang=\"5400000\"/></a:gradFill><a:ln w=\"25400\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:prstDash val=\"dash\"/></a:ln><a:effectLst><a:outerShdw blurRad=\"50800\" dist=\"38100\" dir=\"2700000\"><a:srgbClr val=\"000000\"><a:alpha val=\"40000\"/></a:srgbClr></a:outerShdw></a:effectLst></p:spPr></p:sp>" +
        "<p:sp><p:spPr><a:xfrm rot=\"900000\"><a:off x=\"4000000\" y=\"635000\"/><a:ext cx=\"2540000\" cy=\"1270000\"/></a:xfrm><a:custGeom><a:avLst/><a:gdLst/><a:ahLst/><a:cxnLst/><a:rect l=\"0\" t=\"0\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"10\" h=\"10\"><a:moveTo><a:pt x=\"0\" y=\"10\"/></a:moveTo><a:cubicBezTo><a:pt x=\"0\" y=\"0\"/><a:pt x=\"10\" y=\"0\"/><a:pt x=\"10\" y=\"10\"/></a:cubicBezTo><a:close/></a:path></a:pathLst></a:custGeom><a:pattFill prst=\"dkUpDiag\"><a:fgClr><a:srgbClr val=\"2E75B6\"/></a:fgClr><a:bgClr><a:srgbClr val=\"FFFFFF\"/></a:bgClr></a:pattFill></p:spPr></p:sp>" +
        "<p:pic><p:blipFill><a:blip r:embed=\"rId1\"><a:grayscl/></a:blip><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x=\"635000\" y=\"2540000\"/><a:ext cx=\"1905000\" cy=\"1270000\"/></a:xfrm><a:prstGeom prst=\"ellipse\"><a:avLst/></a:prstGeom></p:spPr></p:pic>" +
        "</p:spTree>";

    public static List<string> Run()
    {
        var failures = new List<string>();
        byte[] png = new RgbaImage(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255]).ToPng();
        if (!MarkupDocument.TryParse(Encoding.UTF8.GetBytes(Tree), out MarkupDocument? doc) || doc is null)
        {
            failures.Add("drawing parse");
            return failures;
        }

        var ctx = new DrawingContext(new ColorResolver(DrawingTheme.Default, ColorMap.Default), null, id => id == "rId1" ? png : null, "smoke");
        var list = new DisplayList();
        uint n = 0;
        int shapes = DrawingPainter.DrawShapes(list, () => ItemId.Of(++n), doc.Root, 0, 0, ctx);
        list.Validate();
        if (shapes != 3)
        {
            failures.Add("drawing shapes " + shapes);
        }

        RgbaImage img = DisplayListRasterizer.Render(list, 640, 400, new RasterOptions { Images = ctx.Images });
        bool inked = false;
        for (int i = 0; i < img.Pixels.Length && !inked; i += 4)
        {
            inked = img.Pixels[i] != 255 || img.Pixels[i + 1] != 255;
        }

        if (!inked)
        {
            failures.Add("drawing raster");
        }

        using var pdf = new MemoryStream();
        using (var writer = new DisplayListPdfWriter(pdf, new PdfOutputOptions { Title = "drawing", Language = "en-US", Tagged = true, LeaveOpen = true }, ctx.Images))
        {
            writer.AddPage(list, new PdfPageSetup(640, 400));
            writer.Finish();
        }

        if (pdf.Length < 500)
        {
            failures.Add("drawing pdf");
        }

        return failures;
    }
}
