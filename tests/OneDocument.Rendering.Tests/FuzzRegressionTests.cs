using System.Globalization;
using System.Text;
using OneRender.Painting;
using RecursiveAI.OneDocument.Charts;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Rendering.Metafiles;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Tests;

/// <summary>Inputs the fuzz campaign found in the rendering code, reduced.</summary>
public sealed class FuzzRegressionTests
{
    // A fixed axis maximum with a value far beyond it made a bar taller than the display list's coordinate range.
    [Fact]
    public void ChartBarFarBeyondTheAxisPaints()
    {
        const string xml = "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart><c:plotArea><c:layout/><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/>" +
            "<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:cat><c:strRef><c:f>S!$A$1:$A$2</c:f><c:strCache><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>a</c:v></c:pt><c:pt idx=\"1\"><c:v>b</c:v></c:pt></c:strCache></c:strRef></c:cat>" +
            "<c:val><c:numRef><c:f>S!$B$1:$B$2</c:f><c:numCache><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>86666666666666666666666666666666666666666666666666666</c:v></c:pt><c:pt idx=\"1\"><c:v>14</c:v></c:pt></c:numCache></c:numRef></c:val></c:ser>" +
            "<c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart><c:catAx><c:axId val=\"1\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/><c:crossAx val=\"2\"/></c:catAx>" +
            "<c:valAx><c:axId val=\"2\"/><c:scaling><c:orientation val=\"minMax\"/><c:max val=\"40\"/><c:min val=\"0\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:crossAx val=\"1\"/></c:valAx></c:plotArea></c:chart></c:chartSpace>";
        ChartSpace chart = ChartReader.Read(Encoding.UTF8.GetBytes(xml));
        var list = new DisplayList();
        _ = ChartHost.Paint(list, chart, 0, 0, 480, 320);
        list.Validate();
    }

    // A moveTo without a point child left the command with no arguments, and evaluating the geometry indexed past them.
    [Fact]
    public void CustomGeometryCommandWithoutPointsDrawsNothing()
    {
        const string xml = "<a:custGeom xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:pathLst><a:path w=\"10\" h=\"10\"><a:moveTo/><a:lnTo><a:pt x=\"1\" y=\"1\"/></a:lnTo><a:cubicBezTo><a:pt x=\"1\" y=\"1\"/></a:cubicBezTo><a:arcTo/></a:path></a:pathLst></a:custGeom>";
        MarkupDocument doc = MarkupDocument.Parse(Encoding.UTF8.GetBytes(xml));
        EvaluatedGeometry g = GeometryDefinition.FromCustomGeometry(doc.Root).Evaluate(100, 50);
        Assert.Single(g.Paths);
    }

    // A print-titles name that names a row beyond the sheet made the page plan index past the last row.
    [Theory]
    [InlineData("T!$1:$1048577")]
    [InlineData("T!$0:$2")]
    public void PrintTitlesOutsideTheSheetAreIgnored(string reference)
    {
        var sheet = new TestXlsx.Sheet("T");
        sheet.Set("A1", "x");
        byte[] file = TestXlsx.Build([sheet], "<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"0\">" + reference + "</definedName>");
        using Workbook wb = Workbook.Open(new MemoryStream(file), leaveOpen: false);
        SheetLayoutDocument layout = WorkbookRenderer.Layout(wb, new SheetRenderOptions { Fonts = TestFonts.Library, FileName = "book.xlsx" });
        Assert.NotEmpty(layout.Pages);
    }

    // An EMF whose transform throws a filled path past a billion pixels: the points are brought into range.
    [Fact]
    public void MetafilePathBeyondTheCoordinateRangePaints()
    {
        const string hex =
            "01000000580000000000000000000000" +
            "630000004a0000000000000000000000" +
            "350c00002809000020454d4600000100" +
            "e4000000080000000300000000000000" +
            "00000000000000000004000000030000" +
            "40010000f00000002700000018000000" +
            "01000000000000003070c00000000000" +
            "250000000c000000010000002b000000" +
            "1800000005000000050000004600003f" +
            "32000000270000001800000002000000" +
            "00000000f0a030000000000025000000" +
            "0c000000020000002a00000018000000" +
            "28000100000000005f00000046000000" +
            "0e00000014000000007a000010000000" +
            "14000000";
        byte[] emf = Convert.FromHexString(hex);
        Assert.Equal(228, emf.Length);
        var list = new DisplayList();
        var images = new PictureImages();
        uint owner = 0;
        var ctx = new MetafilePaintContext(list, images, () => ItemId.Of(++owner)) { Report = _ => { }, AllowRasterFallback = true, MaxRasterSide = 512 };
        _ = MetafilePainter.TryPaint(emf, ctx, new RectF(0, 0, 200, 150));
        list.Validate();
    }

    // A very large font size in a chart title reached the display list unchecked.
    [Fact]
    public void ChartTextWithAnAbsurdFontSizePaints()
    {
        const string xml = "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><c:chart><c:title><c:tx><c:rich><a:bodyPr/><a:p><a:r><a:rPr sz=\"1433333333333333333333333333333333333333333300\"/><a:t>Title</a:t></a:r></a:p></c:rich></c:tx></c:title>" +
            "<c:plotArea><c:layout/><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:val><c:numRef><c:f>S!$B$1:$B$2</c:f><c:numCache><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>1</c:v></c:pt><c:pt idx=\"1\"><c:v>2</c:v></c:pt></c:numCache></c:numRef></c:val></c:ser><c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart>" +
            "<c:catAx><c:axId val=\"1\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/><c:crossAx val=\"2\"/></c:catAx><c:valAx><c:axId val=\"2\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:crossAx val=\"1\"/></c:valAx></c:plotArea></c:chart></c:chartSpace>";
        var list = new DisplayList();
        _ = ChartHost.Paint(list, ChartReader.Read(Encoding.UTF8.GetBytes(xml)), 0, 0, 480, 320);
        list.Validate();
    }

    // A shape placed at an offset beyond any page is left out and noted, not handed to the display list.
    [Fact]
    public void ShapeFarOffThePageIsLeftOut()
    {
        const string xml = "<p:spTree xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"s\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"50800888888888888888888888888888888888888888888880\" y=\"4191000\"/><a:ext cx=\"3302000\" cy=\"0\"/></a:xfrm><a:prstGeom prst=\"line\"><a:avLst/></a:prstGeom></p:spPr></p:sp></p:spTree>";
        MarkupDocument tree = MarkupDocument.Parse(Encoding.UTF8.GetBytes(xml));
        var list = new DisplayList();
        uint n = 0;
        var ctx = new DrawingContext();
        _ = DrawingPainter.DrawShapes(list, () => ItemId.Of(++n), tree.Root, 0, 0, ctx);
        list.Validate();
        Assert.Contains(ctx.Notes, x => x.Code == "drawing.shape-out-of-range");
    }

    // A record the display list refuses part-way through a picture must not leave clips or transforms open.
    [Fact]
    public void MetafileWithARecordTheListRefusesLeavesABalancedList()
    {
        byte[] emf = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "testdata", "fuzz", "metafile-record-refused.emf"));
        var list = new DisplayList();
        var images = new PictureImages();
        uint owner = 0;
        var defects = new List<RecursiveAI.OneDocument.Diagnostics.Defect>();
        var ctx = new MetafilePaintContext(list, images, () => ItemId.Of(++owner)) { Report = defects.Add, AllowRasterFallback = false };
        _ = MetafilePainter.TryPaint(emf, ctx, new RectF(0, 0, 200, 150));
        list.Validate();
        Assert.Equal(0, list.OpenDepth);
    }

    // A zero font size reached the display list, which refuses it.
    [Fact]
    public void ChartTextWithAZeroFontSizePaints()
    {
        const string xml = "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><c:chart><c:title><c:tx><c:rich><a:bodyPr/><a:p><a:r><a:rPr sz=\"0\"/><a:t>Title</a:t></a:r></a:p></c:rich></c:tx></c:title>" +
            "<c:plotArea><c:layout/><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:val><c:numRef><c:f>S!$B$1:$B$2</c:f><c:numCache><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>1</c:v></c:pt><c:pt idx=\"1\"><c:v>2</c:v></c:pt></c:numCache></c:numRef></c:val></c:ser><c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart>" +
            "<c:catAx><c:axId val=\"1\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/><c:txPr><a:bodyPr/><a:p><a:pPr><a:defRPr sz=\"0\"/></a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr><c:crossAx val=\"2\"/></c:catAx><c:valAx><c:axId val=\"2\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:crossAx val=\"1\"/></c:valAx></c:plotArea></c:chart></c:chartSpace>";
        var list = new DisplayList();
        _ = ChartHost.Paint(list, ChartReader.Read(Encoding.UTF8.GetBytes(xml)), 0, 0, 480, 320);
        list.Validate();
    }

    // An outline width beyond any page pushed the stroked path out of the display list's coordinate range.
    [Fact]
    public void LineWithAnAbsurdWidthPaints()
    {
        const string xml = "<p:spTree xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"s\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"508000\" y=\"508000\"/><a:ext cx=\"3302000\" cy=\"1000000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" +
            "<a:ln w=\"9999999999999999999999\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:miter lim=\"99999999999999999\"/></a:ln></p:spPr></p:sp></p:spTree>";
        MarkupDocument tree = MarkupDocument.Parse(Encoding.UTF8.GetBytes(xml));
        var list = new DisplayList();
        uint n = 0;
        _ = DrawingPainter.DrawShapes(list, () => ItemId.Of(++n), tree.Root, 0, 0, new DrawingContext());
        list.Validate();
    }

    // A stacked group with no series made the axis range take the maximum of nothing.
    [Fact]
    public void StackedBarGroupWithoutSeriesPaints()
    {
        const string xml = "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart><c:plotArea><c:layout/><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"stacked\"/><c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart>" +
            "<c:catAx><c:axId val=\"1\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/><c:crossAx val=\"2\"/></c:catAx><c:valAx><c:axId val=\"2\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:crossAx val=\"1\"/></c:valAx></c:plotArea></c:chart></c:chartSpace>";
        var list = new DisplayList();
        if (ChartReader.TryRead(Encoding.UTF8.GetBytes(xml), out ChartSpace? chart) && chart is not null)
        {
            _ = ChartHost.Paint(list, chart, 0, 0, 480, 320);
        }

        list.Validate();
    }

    // A shadow with an offset and blur beyond any page reached the display list's filter check.
    [Fact]
    public void ShadowWithAbsurdOffsetAndBlurPaints()
    {
        const string xml = "<p:spTree xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"s\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"508000\" y=\"508000\"/><a:ext cx=\"3302000\" cy=\"1000000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" +
            "<a:solidFill><a:srgbClr val=\"336699\"/></a:solidFill><a:effectLst><a:outerShdw blurRad=\"9999999999999999\" dist=\"9999999999999999999\" dir=\"0\"><a:srgbClr val=\"000000\"/></a:outerShdw></a:effectLst></p:spPr></p:sp></p:spTree>";
        MarkupDocument tree = MarkupDocument.Parse(Encoding.UTF8.GetBytes(xml));
        var list = new DisplayList();
        uint n = 0;
        _ = DrawingPainter.DrawShapes(list, () => ItemId.Of(++n), tree.Root, 0, 0, new DrawingContext());
        list.Validate();
    }

    // A page margin of absurd size pushed the first line of text beyond the display list's coordinate range.
    [Fact]
    public void PageWithAnAbsurdGutterPaints()
    {
        const string sect = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"666666666666660\"/></w:sectPr>";
        using var doc = RecursiveAI.OneDocument.Documents.Document.Open(new MemoryStream(TestDocx.Bytes("<w:p><w:r><w:t>Some words to lay out</w:t></w:r></w:p>", TestDocx.DefaultStyles, null, null, sect)), leaveOpen: false);
        RecursiveAI.OneDocument.Layout.LayoutDocument layout = RecursiveAI.OneDocument.Layout.LayoutEngine.Layout(doc, new RecursiveAI.OneDocument.Layout.LayoutOptions { Fonts = TestFonts.Library });
        foreach (RecursiveAI.OneDocument.Layout.LayoutPage page in layout.Pages)
        {
            LayoutPainter.Paint(page).Validate();
        }
    }

    // The interpreter (OneImage's) failed with an arithmetic overflow on a pen record; the picture is reported as damaged instead.
    [Fact]
    public void MetafileThatOverflowsTheInterpreterIsReportedAsDamaged()
    {
        byte[] emf = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "testdata", "fuzz", "metafile-createpen-overflow.emf"));
        var list = new DisplayList();
        uint owner = 0;
        var defects = new List<RecursiveAI.OneDocument.Diagnostics.Defect>();
        var ctx = new MetafilePaintContext(list, new PictureImages(), () => ItemId.Of(++owner)) { Report = defects.Add };
        bool painted = MetafilePainter.TryPaint(emf, ctx, new RectF(0, 0, 200, 150));
        list.Validate();
        Assert.False(painted);
        Assert.Contains(defects, d => d.Code == "render.metafile-damaged");
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
