using System.Numerics;
using System.Xml.Linq;
using OneRender.Painting;
using RecursiveAI.OneDocument.Rendering.Svg;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class SvgWriterTests
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    private static SvgOptions Page(double w = 200, double h = 100) => new() { Width = w, Height = h };

    private static XDocument Parse(string svg) => XDocument.Parse(svg);

    private static readonly string[] StopOffsets = ["0", "1"];

    private static int _id;

    private static ItemId Id() => ItemId.Of((uint)Interlocked.Increment(ref _id));

    [Fact]
    public void FillsClipsTransformsAndGroupsNestAsElements()
    {
        var list = new DisplayList();
        list.PushClipRect(Id(), new RectF(0, 0, 100, 50));
        list.PushTransform(Id(), Matrix3x2.CreateTranslation(10, 20));
        list.PushGroup(Id(), 0.5f, BlendMode.Multiply);
        list.FillRect(Id(), new RectF(1, 2, 30, 40), new PaintColor(1, 0, 0, 0.5f));
        list.PopGroup();
        list.PopTransform();
        list.PopClip();
        list.Validate();
        string svg = SvgWriter.ToSvg(list, Page());
        XDocument x = Parse(svg);
        XElement root = x.Root!;
        Assert.Equal(Ns + "svg", root.Name);
        Assert.Equal("0 0 200 100", (string?)root.Attribute("viewBox"));
        XElement clipped = root.Elements(Ns + "g").Single();
        Assert.StartsWith("url(#c", (string?)clipped.Attribute("clip-path"), StringComparison.Ordinal);
        XElement moved = clipped.Element(Ns + "g")!;
        Assert.Equal("matrix(1 0 0 1 10 20)", (string?)moved.Attribute("transform"));
        XElement group = moved.Element(Ns + "g")!;
        Assert.Equal("0.5", (string?)group.Attribute("opacity"));
        Assert.Contains("mix-blend-mode:multiply", (string?)group.Attribute("style"), StringComparison.Ordinal);
        XElement rect = group.Element(Ns + "rect")!;
        Assert.Equal("#ff0000", (string?)rect.Attribute("fill"));
        Assert.Equal("0.5", (string?)rect.Attribute("fill-opacity"));
        Assert.Equal("30", (string?)rect.Attribute("width"));
    }

    [Fact]
    public void OutputIsDeterministicAndUsesInvariantNumbers()
    {
        var list = new DisplayList();
        var path = new PathBuffer();
        path.MoveTo(0.12345f, -0.0001f);
        path.LineTo(10.5f, 20f);
        path.QuadTo(1, 2, 3, 4);
        path.CubicTo(1, 2, 3, 4, 5, 6);
        path.Close();
        list.FillPath(Id(), path, new PaintColor(0, 0, 1, 1));
        list.Validate();
        string a = SvgWriter.ToSvg(list, Page());
        string b = SvgWriter.ToSvg(list, Page());
        Assert.Equal(a, b);
        Assert.Contains("d=\"M0.123 0L10.5 20Q1 2 3 4C1 2 3 4 5 6Z\"", a, StringComparison.Ordinal);
    }

    [Fact]
    public void GradientsAreDefinedInUserSpaceWithTheirStops()
    {
        var list = new DisplayList();
        Paint lin = list.LinearGradient(0, 0, 100, 0, [new GradientStop(0, new PaintColor(1, 0, 0, 1)), new GradientStop(1, new PaintColor(0, 0, 1, 1))]);
        list.FillRect(Id(), new RectF(0, 0, 100, 10), lin);
        Paint rad = list.RadialGradient(50, 50, 40, 20, [new GradientStop(0, PaintColor.White), new GradientStop(1, PaintColor.Black)]);
        list.FillRect(Id(), new RectF(0, 20, 100, 40), rad);
        list.Validate();
        XDocument x = Parse(SvgWriter.ToSvg(list, Page()));
        XElement lg = x.Descendants(Ns + "linearGradient").Single();
        Assert.Equal("userSpaceOnUse", (string?)lg.Attribute("gradientUnits"));
        Assert.Equal("100", (string?)lg.Attribute("x2"));
        Assert.Equal(2, lg.Elements(Ns + "stop").Count());
        XElement rg = x.Descendants(Ns + "radialGradient").Single();
        Assert.Equal("matrix(40 0 0 20 50 50)", (string?)rg.Attribute("gradientTransform"));
        string fill = (string)x.Descendants(Ns + "rect").Last().Attribute("fill")!;
        Assert.Equal("url(#" + (string?)rg.Attribute("id") + ")", fill);
    }

    [Fact]
    public void RepeatingLinearGradientIsCutToItsSpan()
    {
        var list = new DisplayList();
        Paint p = list.LinearGradient(0, 0, 100, 0, [new GradientStop(0.2f, PaintColor.White), new GradientStop(0.4f, PaintColor.Black)], repeating: true);
        list.FillRect(Id(), new RectF(0, 0, 100, 10), p);
        list.Validate();
        XElement lg = Parse(SvgWriter.ToSvg(list, Page())).Descendants(Ns + "linearGradient").Single();
        Assert.Equal("repeat", (string?)lg.Attribute("spreadMethod"));
        Assert.Equal("20", (string?)lg.Attribute("x1"));
        Assert.Equal("40", (string?)lg.Attribute("x2"));
        Assert.Equal(StopOffsets, lg.Elements(Ns + "stop").Select(s => (string?)s.Attribute("offset")).ToArray());
    }

    [Fact]
    public void TagsBecomeRolesLinksAndLabels()
    {
        var list = new DisplayList();
        list.BeginTag(Id(), TagRole.Heading2, language: "fr-FR");
        list.BeginTag(Id(), TagRole.Link, linkTarget: "https://example.com/?a=1&b=2");
        list.FillRect(Id(), new RectF(0, 0, 5, 5), PaintColor.Black);
        list.EndTag();
        list.EndTag();
        list.BeginTag(Id(), TagRole.Figure, altText: "A \"chart\" <of> sales");
        list.FillRect(Id(), new RectF(0, 10, 5, 5), PaintColor.Black);
        list.EndTag();
        list.BeginTag(Id(), TagRole.Artifact);
        list.FillRect(Id(), new RectF(0, 20, 5, 5), PaintColor.Black);
        list.EndTag();
        list.Validate();
        XElement root = Parse(SvgWriter.ToSvg(list, Page())).Root!;
        XElement h = root.Elements(Ns + "g").First();
        Assert.Equal("heading", (string?)h.Attribute("role"));
        Assert.Equal("2", (string?)h.Attribute("aria-level"));
        Assert.Equal("fr-FR", (string?)h.Attribute(XNamespace.Xml + "lang"));
        XElement a = h.Element(Ns + "a")!;
        Assert.Equal("https://example.com/?a=1&b=2", (string?)a.Attribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href"));
        XElement fig = root.Elements(Ns + "g").ElementAt(1);
        Assert.Equal("img", (string?)fig.Attribute("role"));
        Assert.Equal("A \"chart\" <of> sales", (string?)fig.Attribute("aria-label"));
        Assert.Equal("A \"chart\" <of> sales", fig.Element(Ns + "title")!.Value);
        Assert.Equal("true", (string?)root.Elements(Ns + "g").Last().Attribute("aria-hidden"));
    }

    [Fact]
    public void TagsCanBeTurnedOffWithoutUnbalancingTheTree()
    {
        var list = new DisplayList();
        list.BeginTag(Id(), TagRole.Paragraph);
        list.FillRect(Id(), new RectF(0, 0, 5, 5), PaintColor.Black);
        list.EndTag();
        list.Validate();
        SvgOptions o = Page();
        o.IncludeTags = false;
        XElement root = Parse(SvgWriter.ToSvg(list, o)).Root!;
        Assert.Empty(root.Elements(Ns + "g"));
        Assert.Single(root.Elements(Ns + "rect"), r => (string?)r.Attribute("fill") == "#000000");
    }

    [Fact]
    public void FiltersBecomeFilterPrimitives()
    {
        var list = new DisplayList();
        list.PushFilter(Id(), [new FilterOp(FilterOpKind.Blur, 2f), new FilterOp(FilterOpKind.Grayscale, 1f), new FilterOp(FilterOpKind.Opacity, 0.5f)]);
        list.FillRect(Id(), new RectF(10, 10, 20, 20), PaintColor.Black);
        list.PopFilter();
        list.Validate();
        XDocument x = Parse(SvgWriter.ToSvg(list, Page()));
        XElement f = x.Descendants(Ns + "filter").Single();
        Assert.Equal("2", (string?)f.Element(Ns + "feGaussianBlur")!.Attribute("stdDeviation"));
        Assert.Equal("matrix", (string?)f.Element(Ns + "feColorMatrix")!.Attribute("type"));
        Assert.Equal("0.5", (string?)f.Descendants(Ns + "feFuncA").Single().Attribute("slope"));
        Assert.Equal("url(#" + (string?)f.Attribute("id") + ")", (string?)x.Descendants(Ns + "g").Single().Attribute("filter"));
    }

    [Fact]
    public void AMaskOfOneLayerBecomesAMaskElement()
    {
        var list = new DisplayList();
        list.PushMask(Id());
        list.PushMaskLayer(Id(), MaskLayerMode.Luminance, MaskCompositeOp.Add);
        list.FillRect(Id(), new RectF(0, 0, 50, 50), PaintColor.White);
        list.PopMaskLayer();
        list.FillRect(Id(), new RectF(0, 0, 100, 100), new PaintColor(0, 1, 0, 1));
        list.PopMask();
        list.Validate();
        XElement root = Parse(SvgWriter.ToSvg(list, Page())).Root!;
        XElement mask = root.Element(Ns + "mask")!;
        XElement masked = root.Elements(Ns + "g").Single();
        Assert.Equal("url(#" + (string?)mask.Attribute("id") + ")", (string?)masked.Attribute("mask"));
        Assert.Single(mask.Elements(Ns + "rect"));
        Assert.Single(masked.Elements(Ns + "rect"));
    }

    [Fact]
    public void ConicGradientsAndMultiLayerMasksUseTheRasterizerWhenOneIsGiven()
    {
        var fake = new FakeRasterizer();
        var list = new DisplayList();
        Paint conic = list.ConicGradient(10, 10, 0, [new GradientStop(0, PaintColor.White), new GradientStop(1, PaintColor.Black)]);
        list.FillRect(Id(), new RectF(0, 0, 20, 20), conic);
        list.PushMask(Id());
        list.PushMaskLayer(Id(), MaskLayerMode.Alpha, MaskCompositeOp.Add);
        list.FillRect(Id(), new RectF(0, 0, 50, 50), PaintColor.White);
        list.PopMaskLayer();
        list.PushMaskLayer(Id(), MaskLayerMode.Alpha, MaskCompositeOp.Subtract);
        list.FillRect(Id(), new RectF(10, 10, 5, 5), PaintColor.White);
        list.PopMaskLayer();
        list.FillRect(Id(), new RectF(0, 0, 100, 100), PaintColor.Black);
        list.PopMask();
        list.Validate();
        SvgOptions o = Page();
        o.Rasterizer = fake;
        var sw = new StringWriter();
        SvgWriteResult r = SvgWriter.Write(list, sw, o);
        Assert.Equal(2, r.RasterizedRegions);
        Assert.Equal(2, fake.Calls);
        XElement root = Parse(sw.ToString()).Root!;
        Assert.Equal(2, root.Elements(Ns + "image").Count());
        Assert.Contains(r.Notes, n => n.Code == "svg.conic-gradient");
        Assert.Contains(r.Notes, n => n.Code == "svg.mask-layers");
        Assert.Empty(root.Descendants(Ns + "mask"));
    }

    [Fact]
    public void WithoutARasterizerUnsupportedEffectsAreReportedAndContentKept()
    {
        var list = new DisplayList();
        list.PushMask(Id());
        list.PushMaskLayer(Id(), MaskLayerMode.Alpha, MaskCompositeOp.Add);
        list.FillRect(Id(), new RectF(0, 0, 50, 50), PaintColor.White);
        list.PopMaskLayer();
        list.PushMaskLayer(Id(), MaskLayerMode.Alpha, MaskCompositeOp.Intersect);
        list.FillRect(Id(), new RectF(10, 10, 5, 5), PaintColor.White);
        list.PopMaskLayer();
        list.FillRect(Id(), new RectF(0, 0, 100, 100), PaintColor.Black);
        list.PopMask();
        list.Validate();
        var sw = new StringWriter();
        SvgWriteResult r = SvgWriter.Write(list, sw, Page());
        Assert.Contains(r.Notes, n => n.Code == "svg.mask-layers");
        XElement root = Parse(sw.ToString()).Root!;
        Assert.Single(root.Descendants(Ns + "rect"), e => (string?)e.Attribute("fill") == "#000000");
        Assert.Empty(root.Descendants(Ns + "mask"));
    }

    [Fact]
    public void BordersShadowsAndInvalidSizesBehave()
    {
        var list = new DisplayList();
        var solid = new BorderSide(2, BorderStyle.Solid, PaintColor.Black);
        var dashed = new BorderSide(2, BorderStyle.Dashed, new PaintColor(1, 0, 0, 1));
        list.DrawBorder(Id(), new RectF(10, 10, 80, 40), CornerRadii.Zero, solid, dashed, solid, new BorderSide(0, BorderStyle.None, default));
        list.DrawBoxShadow(Id(), new RectF(10, 10, 80, 40), CornerRadii.Zero, new PaintColor(0, 0, 0, 0.5f), 3, 3, 4, 0, false);
        list.Validate();
        XDocument x = Parse(SvgWriter.ToSvg(list, Page()));
        Assert.Equal(2, x.Descendants(Ns + "path").Count(p => (string?)p.Attribute("fill") == "#000000" && p.Attribute("fill-opacity") is null));
        XElement line = x.Descendants(Ns + "line").Single();
        Assert.Equal("4 2", (string?)line.Attribute("stroke-dasharray"));
        Assert.Single(x.Descendants(Ns + "filter"));
        Assert.Throws<ArgumentException>(() => SvgWriter.ToSvg(list, new SvgOptions { Width = 0, Height = 10 }));
    }

    [Fact]
    public void ControlCharactersAreNotWrittenIntoTheDocument()
    {
        var list = new DisplayList();
        list.BeginTag(Id(), TagRole.Figure, altText: "a\u0001b\u0000c&d");
        list.EndTag();
        list.Validate();
        XElement g = Parse(SvgWriter.ToSvg(list, Page())).Root!.Element(Ns + "g")!;
        Assert.Equal("abc&d", (string?)g.Attribute("aria-label"));
    }

    [Fact]
    public void CancellationStopsTheWriter()
    {
        var list = new DisplayList();
        list.FillRect(Id(), new RectF(0, 0, 5, 5), PaintColor.Black);
        list.Validate();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => SvgWriter.Write(list, new StringWriter(), Page(), cts.Token));
    }

    private sealed class FakeRasterizer : ISvgRasterizer
    {
        public int Calls { get; private set; }

        public byte[]? RasterizePng(DisplayList list, int widthPixels, int heightPixels, float pixelRatio)
        {
            Calls++;
            Assert.True(widthPixels > 0 && heightPixels > 0 && pixelRatio > 0);
            return [0x89, (byte)'P', (byte)'N', (byte)'G'];
        }
    }
}
