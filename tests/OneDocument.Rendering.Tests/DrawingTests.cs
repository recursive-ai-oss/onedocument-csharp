using System.Text;
using OneRender.Image;
using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void The_standard_defines_186_distinct_presets()
    {
        Assert.Equal(186, GeometryDefinition.PresetNames.Count);
        Assert.Equal(186, GeometryDefinition.PresetNames.Distinct().Count());
        Assert.Contains("rect", GeometryDefinition.PresetNames);
        Assert.Contains("wedgeRoundRectCallout", GeometryDefinition.PresetNames);
        Assert.Null(GeometryDefinition.Preset("noSuchShape"));
    }

    // Every preset, at several sizes and with adjust values at and far beyond their useful range, gives finite, drawable paths.
    [Theory]
    [InlineData(100, 60)]
    [InlineData(10, 400)]
    [InlineData(0.5, 0.5)]
    [InlineData(0, 50)]
    public void Every_preset_gives_finite_closed_paths_for_default_and_extreme_adjust_values(double w, double h)
    {
        double[] extremes = [0, -1e9, 1e9, 100000, 21600000, -21600000, 50000, 1];
        foreach (string name in GeometryDefinition.PresetNames)
        {
            GeometryDefinition def = GeometryDefinition.Preset(name)!;
            var cases = new List<Dictionary<string, double>?> { null };
            foreach (double e in extremes)
            {
                cases.Add(def.AdjustNames.ToDictionary(n => n, _ => e));
            }

            foreach (Dictionary<string, double>? adj in cases)
            {
                EvaluatedGeometry g = def.Evaluate(w, h, adj);
                Assert.NotEmpty(g.Paths);
                Assert.True(double.IsFinite(g.TextLeft) && double.IsFinite(g.TextTop) && double.IsFinite(g.TextRight) && double.IsFinite(g.TextBottom), name);
                foreach (GeometryPath p in g.Paths)
                {
                    foreach (float c in p.Path.Coords)
                    {
                        Assert.True(float.IsFinite(c) && Math.Abs(c) < 1e9, name + " coordinate " + c);
                    }
                }
            }
        }
    }

    [Fact]
    public void Star5_fills_its_box_with_a_point_at_the_top_centre()
    {
        EvaluatedGeometry g = GeometryDefinition.Preset("star5")!.Evaluate(100, 100);
        float[] c = g.Paths[0].Path.Coords.ToArray();
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < c.Length; i += 2)
        {
            minX = Math.Min(minX, c[i]);
            maxX = Math.Max(maxX, c[i]);
            minY = Math.Min(minY, c[i + 1]);
            maxY = Math.Max(maxY, c[i + 1]);
        }

        Assert.Equal(0, minX, 1);
        Assert.Equal(100, maxX, 1);
        Assert.Equal(0, minY, 1);
        Assert.InRange(maxY, 90, 100.1);
        Assert.Contains(Enumerable.Range(0, c.Length / 2), i => Math.Abs(c[2 * i] - 50) < 0.1 && Math.Abs(c[(2 * i) + 1]) < 0.1);
    }

    [Fact]
    public void Rect_is_the_box_and_its_text_rectangle_is_the_box()
    {
        EvaluatedGeometry g = GeometryDefinition.Preset("rect")!.Evaluate(200, 100);
        GeometryPath p = Assert.Single(g.Paths);
        Assert.Equal(new float[] { 0, 0, 200, 0, 200, 100, 0, 100 }, p.Path.Coords.ToArray());
        Assert.Equal((0, 0, 200, 100), (g.TextLeft, g.TextTop, g.TextRight, g.TextBottom));
    }

    [Fact]
    public void Triangle_apex_follows_the_adjust_value()
    {
        GeometryDefinition def = GeometryDefinition.Preset("triangle")!;
        // adj is the apex position as a fraction of the width in 1/100000: 50000 centred, 0 at the left edge.
        float[] centred = def.Evaluate(100, 50).Paths[0].Path.Coords.ToArray();
        Assert.Equal(new float[] { 0, 50, 50, 0, 100, 50 }, centred.Take(6).Select(Math.Abs).Select(Round).ToArray());
        float[] left = def.Evaluate(100, 50, new Dictionary<string, double> { ["adj"] = 0 }).Paths[0].Path.Coords.ToArray();
        Assert.Equal(0f, left[2], 3);
    }

    [Fact]
    public void RoundRect_corner_radius_is_a_sixth_of_the_short_side_by_default()
    {
        // adj 16667 of ss: the first segment starts after the corner arc, at x = ss * 16667 / 100000.
        EvaluatedGeometry g = GeometryDefinition.Preset("roundRect")!.Evaluate(200, 100);
        float[] c = g.Paths[0].Path.Coords.ToArray();
        Assert.Equal(100 * 16667 / 100000.0, c[1], 2);
        Assert.Equal(0f, c[0], 3);
    }

    [Fact]
    public void Ellipse_arcs_end_on_the_box_edges()
    {
        EvaluatedGeometry g = GeometryDefinition.Preset("ellipse")!.Evaluate(80, 40);
        PathBuffer p = g.Paths[0].Path;
        float[] c = p.Coords.ToArray();
        Assert.Equal((0f, 20f), (c[0], c[1]));
        Assert.Equal(4, p.Verbs.ToArray().Count(v => v == (byte)PathVerb.Cubic));
        float minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 2; i < c.Length; i += 2)
        {
            minX = Math.Min(minX, c[i]);
            maxX = Math.Max(maxX, c[i]);
        }

        Assert.InRange(maxX, 79.9, 80.1);
    }

    [Fact]
    public void Custom_geometry_scales_its_path_space_to_the_shape()
    {
        MarkupDocument doc = Parse("<a:custGeom xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:avLst/><a:gdLst/><a:pathLst><a:path w=\"10\" h=\"10\"><a:moveTo><a:pt x=\"0\" y=\"0\"/></a:moveTo><a:lnTo><a:pt x=\"10\" y=\"0\"/></a:lnTo><a:lnTo><a:pt x=\"5\" y=\"10\"/></a:lnTo><a:close/></a:path></a:pathLst></a:custGeom>");
        EvaluatedGeometry g = GeometryDefinition.FromCustomGeometry(doc.Root).Evaluate(100, 50);
        Assert.Equal(new float[] { 0, 0, 100, 0, 50, 50 }, g.Paths[0].Path.Coords.ToArray().Select(Round).ToArray());
    }

    private static float Round(float v) => (float)Math.Round(v, 2);

    internal static MarkupDocument Parse(string xml)
    {
        Assert.True(MarkupDocument.TryParse(Encoding.UTF8.GetBytes(xml), out MarkupDocument? doc));
        return doc!;
    }
}

public sealed class ColorTests
{
    private const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"";

    private static uint Rgb(string element, ColorResolver? resolver = null) =>
        (resolver ?? new ColorResolver()).Resolve(GeometryTests.Parse(element).Root)!.Value.Rgb;

    private static void Near(uint expected, uint actual)
    {
        for (int shift = 0; shift <= 16; shift += 8)
        {
            Assert.InRange((int)((actual >> shift) & 0xFF), (int)((expected >> shift) & 0xFF) - 1, (int)((expected >> shift) & 0xFF) + 1);
        }
    }

    // The accent-1 shades in Office's colour picker (Lighter 80 %, 60 %, 40 %, Darker 25 %, 50 %) are lumMod/lumOff results.
    [Theory]
    [InlineData("<a:lumMod val=\"20000\"/><a:lumOff val=\"80000\"/>", 0xD9E2F3u)]
    [InlineData("<a:lumMod val=\"40000\"/><a:lumOff val=\"60000\"/>", 0xB4C7E7u)]
    [InlineData("<a:lumMod val=\"60000\"/><a:lumOff val=\"40000\"/>", 0x8FAADCu)]
    [InlineData("<a:lumMod val=\"75000\"/>", 0x2F5597u)]
    [InlineData("<a:lumMod val=\"50000\"/>", 0x203864u)]
    public void Luminance_transforms_match_the_office_palette(string transforms, uint expected) =>
        Near(expected, Rgb($"<a:schemeClr {Ns} val=\"accent1\">{transforms}</a:schemeClr>"));

    [Fact]
    public void Scheme_colours_follow_the_colour_map()
    {
        var map = ColorMap.Read(GeometryTests.Parse("<a:clrMap xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" bg1=\"dk1\" tx1=\"lt1\" bg2=\"dk2\" tx2=\"lt2\"/>").Root);
        Assert.Equal(0u, Rgb($"<a:schemeClr {Ns} val=\"bg1\"/>", new ColorResolver(null, map)));
        Assert.Equal(0xFFFFFFu, Rgb($"<a:schemeClr {Ns} val=\"tx1\"/>", new ColorResolver(null, map)));
        Assert.Equal(0xFFFFFFu, Rgb($"<a:schemeClr {Ns} val=\"bg1\"/>"));
    }

    [Fact]
    public void Shade_and_tint_work_in_linear_light()
    {
        // Half of white's linear value is sRGB 0.7354, byte 0xBC; tint 50 % of black is the same grey.
        Near(0xBCBCBCu, Rgb($"<a:srgbClr {Ns} val=\"FFFFFF\"><a:shade val=\"50000\"/></a:srgbClr>"));
        Near(0xBCBCBCu, Rgb($"<a:srgbClr {Ns} val=\"000000\"><a:tint val=\"50000\"/></a:srgbClr>"));
    }

    [Fact]
    public void Alpha_transforms_and_the_other_colour_models()
    {
        DrawingColor c = new ColorResolver().Resolve(GeometryTests.Parse($"<a:srgbClr {Ns} val=\"336699\"><a:alpha val=\"40000\"/><a:alphaMod val=\"50000\"/><a:alphaOff val=\"10000\"/></a:srgbClr>").Root)!.Value;
        Assert.Equal(0.3, c.A, 6);
        Assert.Equal(0xFF0000u, Rgb($"<a:prstClr {Ns} val=\"red\"/>"));
        Assert.Equal(0xFF0000u, Rgb($"<a:hslClr {Ns} hue=\"0\" sat=\"100000\" lum=\"50000\"/>"));
        Assert.Equal(0xFFFFFFu, Rgb($"<a:scrgbClr {Ns} r=\"100000\" g=\"100000\" b=\"100000\"/>"));
        Assert.Equal(0x00FFFFu, Rgb($"<a:srgbClr {Ns} val=\"FF0000\"><a:comp/></a:srgbClr>"));
        Near(0x808080u, Rgb($"<a:srgbClr {Ns} val=\"FF0000\"><a:sat val=\"0\"/><a:lum val=\"50000\"/></a:srgbClr>"));
        Assert.Equal(0x00FF00u, Rgb($"<a:srgbClr {Ns} val=\"FF0000\"><a:red val=\"0\"/><a:green val=\"100000\"/></a:srgbClr>"));
    }

    [Fact]
    public void A_placeholder_colour_stands_in_for_phClr()
    {
        var r = new ColorResolver();
        DrawingColor? c = r.Resolve(GeometryTests.Parse($"<a:schemeClr {Ns} val=\"phClr\"><a:lumMod val=\"50000\"/></a:schemeClr>").Root, new DrawingColor(1, 0, 0));
        Assert.Equal(0x800000u, c!.Value.Rgb);
    }
}

public sealed class DrawingPainterTests
{
    private const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    private static (RgbaImage Image, DrawingContext Context) Render(string shapeXml, double w, double h, DrawingContext? ctx = null)
    {
        ctx ??= new DrawingContext();
        var list = new DisplayList();
        uint n = 0;
        MarkupDocument doc = GeometryTests.Parse(shapeXml);
        Assert.True(DrawingPainter.Draw(list, () => ItemId.Of(++n), doc.Root, new DrawingBox(10, 10, w, h), ctx, "alt"));
        list.Validate();
        RgbaImage img = DisplayListRasterizer.Render(list, (int)((w + 20) * 4 / 3) + 1, (int)((h + 20) * 4 / 3) + 1, new RasterOptions { Images = ctx.Images });
        return (img, ctx);
    }

    private static (int R, int G, int B, int A) Pixel(RgbaImage img, double xPt, double yPt)
    {
        int x = (int)(xPt * 4 / 3), y = (int)(yPt * 4 / 3);
        int i = ((y * img.Width) + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    [Fact]
    public void A_solid_rectangle_with_a_line_paints_fill_and_outline()
    {
        (RgbaImage img, _) = Render($"<wps:wsp {Ns}><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"635000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"FF8000\"/></a:solidFill><a:ln w=\"25400\"><a:solidFill><a:srgbClr val=\"0000FF\"/></a:solidFill></a:ln></wps:spPr></wps:wsp>", 100, 50);
        Assert.Equal((255, 128, 0, 255), Pixel(img, 60, 35));
        (int r, int g, int b, _) = Pixel(img, 10.3, 35);
        Assert.True(b > 200 && r < 60, $"outline pixel {r},{g},{b}");
        Assert.Equal((255, 255, 255, 255), Pixel(img, 3, 3));
    }

    [Fact]
    public void Flip_and_rotation_move_the_shape()
    {
        // A right triangle (rtTriangle has its right angle bottom-left); flipping horizontally puts it bottom-right.
        string xml = "<wps:wsp " + Ns + "><wps:spPr><a:xfrm flipH=\"1\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"1270000\"/></a:xfrm><a:prstGeom prst=\"rtTriangle\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:ln><a:noFill/></a:ln></wps:spPr></wps:wsp>";
        (RgbaImage img, _) = Render(xml, 100, 100);
        Assert.Equal(0, Pixel(img, 90, 95).R);
        Assert.Equal(255, Pixel(img, 20, 95).R);
        Assert.Equal(255, Pixel(img, 90, 20).R);
    }

    [Fact]
    public void Gradient_runs_between_its_stops()
    {
        string xml = "<wps:wsp " + Ns + "><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"2540000\" cy=\"635000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:gradFill><a:gsLst><a:gs pos=\"0\"><a:srgbClr val=\"000000\"/></a:gs><a:gs pos=\"100000\"><a:srgbClr val=\"FFFFFF\"/></a:gs></a:gsLst><a:lin ang=\"0\" scaled=\"0\"/></a:gradFill><a:ln><a:noFill/></a:ln></wps:spPr></wps:wsp>";
        (RgbaImage img, _) = Render(xml, 200, 50);
        int left = Pixel(img, 20, 35).R, mid = Pixel(img, 110, 35).R, right = Pixel(img, 200, 35).R;
        Assert.True(left < mid && mid < right, $"{left} {mid} {right}");
        Assert.InRange(mid, 100, 160);
    }

    [Fact]
    public void Dashed_lines_leave_gaps()
    {
        string xml = "<wps:wsp " + Ns + "><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"2540000\" cy=\"0\"/></a:xfrm><a:prstGeom prst=\"line\"><a:avLst/></a:prstGeom><a:ln w=\"38100\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:prstDash val=\"dash\"/></a:ln></wps:spPr></wps:wsp>";
        (RgbaImage img, _) = Render(xml, 200, 0);
        int dark = 0, light = 0;
        for (double x = 12; x < 205; x += 1)
        {
            if (Pixel(img, x, 10).R < 128)
            {
                dark++;
            }
            else
            {
                light++;
            }
        }

        Assert.True(dark > 40 && light > 40, $"{dark} dark, {light} light");
    }

    [Fact]
    public void Compound_double_line_leaves_a_gap_in_the_middle()
    {
        string xml = "<wps:wsp " + Ns + "><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"2540000\" cy=\"0\"/></a:xfrm><a:prstGeom prst=\"line\"><a:avLst/></a:prstGeom><a:ln w=\"114300\" cmpd=\"dbl\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:ln></wps:spPr></wps:wsp>";
        (RgbaImage img, _) = Render(xml, 200, 0);
        Assert.Equal(255, Pixel(img, 100, 10).R);
        Assert.True(Pixel(img, 100, 7.5).R < 100);
        Assert.True(Pixel(img, 100, 12.5).R < 100);
    }

    [Fact]
    public void Theme_style_references_supply_fill_and_line()
    {
        string xml = "<wps:wsp " + Ns + "><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"635000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></wps:spPr><wps:style><a:lnRef idx=\"2\"><a:schemeClr val=\"accent2\"/></a:lnRef><a:fillRef idx=\"1\"><a:schemeClr val=\"accent1\"/></a:fillRef><a:effectRef idx=\"0\"><a:schemeClr val=\"accent1\"/></a:effectRef></wps:style></wps:wsp>";
        string theme = "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:themeElements><a:clrScheme name=\"t\"><a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1></a:clrScheme><a:fmtScheme name=\"f\"><a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst><a:effectStyleLst/></a:fmtScheme></a:themeElements></a:theme>";
        var ctx = new DrawingContext(new ColorResolver(DrawingTheme.Read(GeometryTests.Parse(theme).Root)));
        (RgbaImage img, _) = Render(xml, 100, 50, ctx);
        (int r, int g, int b, _) = Pixel(img, 60, 35);
        Assert.InRange(r, 0x43, 0x45);
        Assert.InRange(b, 0xC3, 0xC5);
        (int lr, _, int lb, _) = Pixel(img, 10.4, 35);
        Assert.True(lr > lb, "outline takes accent 2 (orange), not the fill colour");
    }

    [Fact]
    public void Pictures_are_cropped_and_placeholders_noted_when_missing()
    {
        byte[] quad = new byte[4 * 4 * 4];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                byte[] c = y < 2 ? (x < 2 ? [255, 0, 0] : [0, 255, 0]) : (x < 2 ? [0, 0, 255] : [255, 255, 0]);
                int i = ((y * 4) + x) * 4;
                quad[i] = c[0];
                quad[i + 1] = c[1];
                quad[i + 2] = c[2];
                quad[i + 3] = 255;
            }
        }

        byte[] png = new RgbaImage(4, 4, quad).ToPng();
        string pic = "<pic:pic " + Ns + "><pic:blipFill><a:blip r:embed=\"rId1\"/><a:srcRect r=\"50000\" b=\"50000\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"1270000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>";
        var ctx = new DrawingContext(imageBytes: id => id == "rId1" ? png : null, scope: "test");
        (RgbaImage img, _) = Render(pic, 100, 100, ctx);
        // Cropping away the right and bottom halves leaves only the top-left (red) pixel, stretched over the whole box.
        Assert.Equal((255, 0, 0), (Pixel(img, 20, 20).R, Pixel(img, 20, 20).G, Pixel(img, 20, 20).B));
        Assert.Equal((255, 0, 0), (Pixel(img, 100, 100).R, Pixel(img, 100, 100).G, Pixel(img, 100, 100).B));

        var missing = new DrawingContext();
        (RgbaImage placeholder, DrawingContext c2) = Render(pic, 100, 100, missing);
        Assert.Contains(c2.Notes, n => n.Code == "drawing.picture-missing");
        Assert.NotEqual(0, Pixel(placeholder, 20, 20).R);
    }

    [Fact]
    public void Groups_map_child_coordinates_onto_their_box()
    {
        // A group 100 pt wide shows children laid out in a 10 x 10 child space, so a child at 5..10 covers the right half.
        string xml = "<wpg:wgp xmlns:wpg=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\" " + Ns + "><wpg:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"1270000\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"10\" cy=\"10\"/></a:xfrm></wpg:grpSpPr>"
            + "<wps:wsp><wps:spPr><a:xfrm><a:off x=\"5\" y=\"0\"/><a:ext cx=\"5\" cy=\"10\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:ln><a:noFill/></a:ln></wps:spPr></wps:wsp></wpg:wgp>";
        (RgbaImage img, _) = Render(xml, 100, 100);
        Assert.Equal(0, Pixel(img, 90, 50).R);
        Assert.Equal(255, Pixel(img, 30, 50).R);
    }
}
