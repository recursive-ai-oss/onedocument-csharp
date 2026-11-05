using System.Text;
using OneRender.Painting;
using RecursiveAI.OneDocument.Charts;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Tools.ChartProbe;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class ChartTests
{
    public static IEnumerable<object[]> Names() => ChartSamples.All().Select(s => new object[] { s.Name });

    private static ChartSamples.Sample Sample(string name) => ChartSamples.All().First(s => s.Name == name);

    private static ChartSpace Read(string name) => ChartReader.Read(Encoding.UTF8.GetBytes(Sample(name).Xml), name);

    [Theory]
    [MemberData(nameof(Names))]
    public void EverySampleReadsAndKeepsItsBytes(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Sample(name).Xml);
        ChartSpace chart = ChartReader.Read(bytes, name);
        Assert.Equal(Sample(name).IsExtended, chart.IsExtended);
        Assert.NotEmpty(chart.Groups);
        Assert.All(chart.Groups, g => Assert.NotEmpty(g.Series));
        Assert.True(chart.SourceBytes.Span.SequenceEqual(bytes));
    }

    [Theory]
    [InlineData("column-clustered", ChartType.Column)]
    [InlineData("bar-clustered", ChartType.Bar)]
    [InlineData("line", ChartType.Line)]
    [InlineData("area", ChartType.Area)]
    [InlineData("pie", ChartType.Pie)]
    [InlineData("doughnut", ChartType.Doughnut)]
    [InlineData("scatter-markers", ChartType.Scatter)]
    [InlineData("bubble", ChartType.Bubble)]
    [InlineData("radar", ChartType.Radar)]
    [InlineData("stock-ohlc", ChartType.Stock)]
    [InlineData("surface", ChartType.Surface)]
    [InlineData("waterfall", ChartType.Waterfall)]
    [InlineData("histogram", ChartType.Histogram)]
    [InlineData("pareto", ChartType.Pareto)]
    [InlineData("boxwhisker", ChartType.BoxWhisker)]
    [InlineData("funnel", ChartType.Funnel)]
    [InlineData("treemap", ChartType.Treemap)]
    [InlineData("sunburst", ChartType.Sunburst)]
    [InlineData("regionmap", ChartType.RegionMap)]
    public void TheFirstGroupHasTheRightType(string name, ChartType type) => Assert.Equal(type, Read(name).Groups[0].Type);

    [Fact]
    public void ValuesCategoriesAndTitleComeFromTheCache()
    {
        ChartSpace c = Read("column-clustered");
        Assert.Equal("Sales by quarter", c.Title?.Text?.Text);
        ChartSeries s = c.Groups[0].Series[0];
        Assert.Equal("North", s.Name?.Text);
        Assert.Equal([12d, 19, 7, 15, 22, 9], s.Values!.Numbers.Select(n => n!.Value));
        Assert.Equal("Mar", s.Categories!.Strings[2]);
        Assert.Equal(ChartLegendPosition.Bottom, c.Legend!.Position);
        Assert.Equal(100, c.Groups[0].GapWidth);
    }

    [Fact]
    public void ComboChartMarksTheSecondGroupSecondary()
    {
        ChartSpace c = Read("combo-secondary");
        Assert.Equal(2, c.Groups.Count);
        Assert.False(c.Groups[0].IsSecondary);
        Assert.True(c.Groups[1].IsSecondary);
        Assert.Equal(4, c.Axes.Count);
    }

    [Fact]
    public void ChartexKeepsHierarchyAndSubtotals()
    {
        ChartSpace tree = Read("treemap");
        ChartData cats = tree.Groups[0].Series[0].Categories!;
        Assert.Equal("Apple", cats.Strings[0]);
        Assert.Equal("Fruit", cats.OuterLevels[0][0]);
        ChartSeries wf = Read("waterfall").Groups[0].Series[0];
        Assert.Equal([0, 5], wf.Points.Where(p => p.IsSubtotal).Select(p => p.Index).OrderBy(i => i));
        Assert.Contains(Read("regionmap").Notes, n => n.Code == "chart.region-map");
    }

    [Fact]
    public void NotAChartAndBrokenXmlRaiseTypedExceptions()
    {
        OfficeFormatException e = Assert.Throws<OfficeFormatException>(() => ChartReader.Read("<a/>"u8.ToArray()));
        Assert.Equal("chart.not-a-chart", e.Defect.Code);
        Assert.Throws<OfficeFormatException>(() => ChartReader.Read("<c:chartSpace"u8.ToArray()));
        Assert.False(ChartReader.TryRead("<<<"u8.ToArray(), out _));
    }

    [Fact]
    public void ChartPartsRoundTripThroughAPackageByteForByte()
    {
        using OpcPackage pkg = OpcPackage.Create();
        var parts = new Dictionary<string, byte[]>();
        foreach (ChartSamples.Sample s in ChartSamples.All())
        {
            string name = "/xl/charts/" + s.Name + ".xml";
            byte[] bytes = Encoding.UTF8.GetBytes(s.Xml);
            parts[name] = bytes;
            pkg.AddPart(name, s.IsExtended ? "application/vnd.ms-office.chartex+xml" : "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", bytes);
        }

        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        ms.Position = 0;
        using OpcPackage again = OpcPackage.Open(ms);
        IReadOnlyList<string> found = ChartHost.FindChartParts(again);
        Assert.Equal(parts.Count, found.Count);
        foreach (string name in found)
        {
            ChartPart? loaded = ChartHost.LoadPart(again, name);
            Assert.NotNull(loaded);
            Assert.True(again.FindPart(name)!.GetBytes().Span.SequenceEqual(parts[name]), name);
            Assert.True(loaded.Chart.SourceBytes.Span.SequenceEqual(parts[name]));
        }
    }

    [Theory]
    [InlineData(0, 100, 0, 120, 20)]
    [InlineData(0, 10, 0, 12, 2)]
    [InlineData(0, 1, 0, 1.2, 0.2)]
    [InlineData(1, 8, 0, 9, 1)]
    [InlineData(10, 200, 0, 250, 50)]
    [InlineData(6, 22, 0, 25, 5)]
    [InlineData(-90, -5, -100, 0, 10)]
    public void AutomaticAxisScale(double lo, double hi, double min, double max, double major)
    {
        AxisScale s = AxisScaler.Compute(new ChartAxis(), lo, hi);
        Assert.Equal(min, s.Min, 9);
        Assert.Equal(max, s.Max, 9);
        Assert.Equal(major, s.MajorUnit, 9);
    }

    [Fact]
    public void TightRangesDoNotStartAtZero()
    {
        AxisScale s = AxisScaler.Compute(new ChartAxis(), 1000, 1010);
        Assert.True(s.Min > 900 && s.Min <= 1000 && s.Max >= 1010);
    }

    [Fact]
    public void FixedLogAndPercentAxes()
    {
        AxisScale fixedScale = AxisScaler.Compute(new ChartAxis { Min = 5, Max = 40, MajorUnit = 7 }, 0, 100);
        Assert.Equal((5d, 40d, 7d), (fixedScale.Min, fixedScale.Max, fixedScale.MajorUnit));
        AxisScale log = AxisScaler.Compute(new ChartAxis { LogBase = 10 }, 30, 4000);
        Assert.Equal((10d, 10000d), (log.Min, log.Max));
        Assert.Equal(0.5, log.Fraction(Math.Sqrt(10 * 10000)), 9);
        AxisScale pct = AxisScaler.Compute(new ChartAxis(), 0, 1, percentStacked: true);
        Assert.Equal((0d, 1d, 0.1), (pct.Min, pct.Max, pct.MajorUnit));
        Assert.Equal(11, pct.MajorTicks().Count);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void EverySamplePaintsATaggedFigure(string name)
    {
        ChartSpace chart = Read(name);
        var list = new DisplayList();
        ChartLayoutInfo info = ChartHost.Paint(list, chart, 10, 20, 640, 384);
        Assert.True(list.Count > (name == "regionmap" ? 3 : 12), name);
        Assert.Equal(TagRole.Figure, list[0].Role);
        Assert.False(string.IsNullOrEmpty(list[0].AltText));
        Assert.Equal(0, list.OpenDepth);
        Assert.Equal(10, info.ChartArea.X);
        if (name != "regionmap")
        {
            Assert.True(info.PlotArea.Width > 50 && info.PlotArea.Height > 50, name + " plot " + info.PlotArea);
            Assert.True(info.PlotArea.X >= 10 && info.PlotArea.Right <= 650.5f && info.PlotArea.Bottom <= 404.5f, name);
        }

        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_CHART_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using FileStream pdf = File.Create(Path.Combine(dir, name + ".pdf"));
            ChartHost.WritePdf(chart, pdf, 480, 288);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), ChartHost.RenderPng(chart, 480, 288));
        }
    }

    [Fact]
    public void PdfAndPngComeOutForAChart()
    {
        ChartSpace chart = Read("column-clustered");
        using var ms = new MemoryStream();
        ChartHost.WritePdf(chart, ms, 480, 288);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(ms.ToArray(), 0, 4));
        byte[] png = ChartHost.RenderPng(chart, 480, 288);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
    }

    [Fact]
    public void ColumnChartScaleAndPlotAreFollowTheDataAndLayout()
    {
        var list = new DisplayList();
        ChartLayoutInfo info = ChartHost.Paint(list, Read("column-clustered"), 0, 0, 640, 384);
        AxisScale s = info.Scales[222];
        Assert.Equal((0d, 25d, 5d), (s.Min, s.Max, s.MajorUnit));
        ChartLayoutInfo manual = ChartHost.Paint(new DisplayList(), Read("manual-layout"), 0, 0, 640, 400);
        Assert.Equal(96, manual.PlotArea.X, 0.5);
        Assert.Equal(80, manual.PlotArea.Y, 0.5);
        Assert.Equal(448, manual.PlotArea.Width, 0.5);
    }

    [Fact]
    public void LegendAndTitleReserveSpace()
    {
        ChartLayoutInfo info = ChartHost.Paint(new DisplayList(), Read("column-clustered"), 0, 0, 640, 384);
        Assert.True(info.Title.Height > 10 && info.Legend.Y > info.PlotArea.Bottom);
        Assert.True(info.PlotArea.Y >= info.Title.Bottom);
    }

    [Fact]
    public void RefreshReplacesCachesFromASource()
    {
        ChartSpace chart = Read("column-clustered");
        int n = ChartRefresh.Refresh(chart, new FakeSource());
        Assert.True(n >= 3);
        Assert.Equal([1d, 2, 3], chart.Groups[0].Series[0].Values!.Numbers.Select(x => x!.Value));
        Assert.Equal("renamed", chart.Groups[0].Series[0].Name!.Text);
    }

    private sealed class FakeSource : IChartDataSource
    {
        public bool TryGetValues(string reference, out IReadOnlyList<object?> values)
        {
            values = reference.EndsWith("$1", StringComparison.Ordinal) ? ["renamed"] : reference.Contains("$A$", StringComparison.Ordinal) ? ["x", "y", "z"] : [1.0, 2.0, 3.0];
            return true;
        }
    }

    [Fact]
    public void FramesAreFoundInSlideDrawingAndDocumentParts()
    {
        string slide = "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><p:cSld><p:spTree><p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"4\" name=\"Chart 3\" descr=\"Sales\"/></p:nvGraphicFramePr><p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"5486400\" cy=\"3200400\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart r:id=\"rId2\"/></a:graphicData></a:graphic></p:graphicFrame></p:spTree></p:cSld></p:sld>";
        ChartFrame f = Assert.Single(ChartFrames.Find(MarkupDocument.Parse(Encoding.UTF8.GetBytes(slide))));
        Assert.Equal(("rId2", 5486400L, 3200400L, "Chart 3", "Sales"), (f.RelationshipId, f.WidthEmu, f.HeightEmu, f.Name, f.Description));
        string doc = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body><w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"100\" cy=\"200\"/><wp:docPr id=\"1\" name=\"Chart 1\"/><a:graphic><a:graphicData><c:chart r:id=\"rId9\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p></w:body></w:document>";
        ChartFrame d = Assert.Single(ChartFrames.Find(MarkupDocument.Parse(Encoding.UTF8.GetBytes(doc))));
        Assert.Equal(("rId9", 100L, 200L), (d.RelationshipId, d.WidthEmu, d.HeightEmu));
    }

    [Fact]
    public void ThemeColoursResolveWithTransforms()
    {
        ChartRgb accent = ChartTheme.Default.Resolve(new ChartColor { Kind = "scheme", Value = "accent1" });
        Assert.Equal((0x44, 0x72, 0xC4), (accent.R, accent.G, accent.B));
        var grey = new ChartColor { Kind = "scheme", Value = "tx1" };
        grey.Transforms.Add(new("lumMod", 65000));
        grey.Transforms.Add(new("lumOff", 35000));
        ChartRgb g = ChartTheme.Default.Resolve(grey);
        Assert.InRange(g.R, 0x58, 0x5A);
    }
}
