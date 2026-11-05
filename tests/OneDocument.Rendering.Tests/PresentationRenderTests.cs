using System.Text;
using OneRender.Painting;
using OneRender.Image;
using OnePdf;
using OnePdf.Text;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering.Presentations;
using static RecursiveAI.OneDocument.Rendering.Tests.TestPptx;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class PresentationRenderTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public static IEnumerable<object[]> CaseNames() => SlideCases.All().Select(c => new object[] { c.Name });

    private static PresentationRenderOptions Options(Action<PresentationRenderOptions>? edit = null)
    {
        var o = new PresentationRenderOptions { Fonts = TestFonts.Library, Compress = false };
        edit?.Invoke(o);
        return o;
    }

    private static Presentation Open(byte[] pptx) => Presentation.Open(new MemoryStream(pptx), leaveOpen: false);

    private static (PresentationRenderReport Report, byte[] Pdf) Pdf(byte[] pptx, Action<PresentationRenderOptions>? edit = null)
    {
        using Presentation p = Open(pptx);
        using var ms = new MemoryStream();
        PresentationRenderReport report = PresentationRenderer.WritePdf(p, ms, Options(edit));
        return (report, ms.ToArray());
    }

    private sealed record Line(string Text, double Left, double Right, double Top, double Bottom, bool Vertical)
    {
        public double Height => Bottom - Top;
    }

    // Lines of a PDF page with their boxes in points from the page's top-left.
    private static List<Line> Lines(byte[] pdf, int page, double pageHeight = 540)
    {
        using var doc = PdfDocument.Open(pdf);
        PdfPageText text = doc.ExtractText().ElementAt(page);
        return [.. text.Blocks.SelectMany(b => b.Lines).Select(l => new Line(string.Join(' ', l.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), l.Bounds.Left, l.Bounds.Right, pageHeight - l.Bounds.Top, pageHeight - l.Bounds.Bottom, l.IsVertical))];
    }

    private static string Squash(IEnumerable<Line> lines) => string.Concat(lines.Select(l => l.Text)).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static RgbaImage Raster(byte[] pptx, int pageIndex = 0)
    {
        using Presentation p = Open(pptx);
        var session = new RenderSession(p, Options(o => o.Tagged = false));
        List<PageSpec> specs = session.Enumerate(out _);
        DisplayList list = session.PaintPage(specs[pageIndex]);
        (double w, double h) = session.PageSize(specs[pageIndex]);
        return DisplayListRasterizer.Render(list, (int)Math.Ceiling(w * 4 / 3), (int)Math.Ceiling(h * 4 / 3), new RasterOptions { Images = session.Images });
    }

    private static (int R, int G, int B) Pixel(RgbaImage img, double xPt, double yPt)
    {
        int x = (int)(xPt * 4 / 3), y = (int)(yPt * 4 / 3);
        int i = ((y * img.Width) + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2]);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryCaseRendersToATaggedPdfAndPng(string name)
    {
        byte[] pptx = SlideCases.Get(name);
        (PresentationRenderReport report, byte[] pdf) = Pdf(pptx);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8), StringComparison.Ordinal);
        Assert.Contains("/StructTreeRoot", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        using (var doc = PdfDocument.Open(pdf))
        {
            Assert.Equal(report.Pages.Count, doc.PageCount);
        }

        using Presentation p = Open(pptx);
        byte[] png = PresentationRenderer.RenderPng(p, 0, Options());
        Assert.True(png.Length > 100);
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_SLIDE_SAMPLES") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".pptx"), pptx);
            File.WriteAllBytes(Path.Combine(dir, name + ".pdf"), pdf);
            (_, byte[] all) = Pdf(pptx, o => o.Output = PresentationOutput.Slides | PresentationOutput.NotesPages | PresentationOutput.Handouts);
            File.WriteAllBytes(Path.Combine(dir, name + "-all.pdf"), all);
            for (int i = 0; i < Math.Min(report.Pages.Count, 3); i++)
            {
                File.WriteAllBytes(Path.Combine(dir, $"{name}-p{i + 1}.png"), PresentationRenderer.RenderPng(p, i, Options()));
            }
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryCaseMeetsThePdfUaProfile(string name)
    {
        // The writer checks the profile's structure rules and refuses output that breaks them (a table without header cells, a figure without text).
        (PresentationRenderReport report, byte[] pdf) = Pdf(SlideCases.Get(name), o => o.Profile = OneRender.Pdf.PdfProfile.PdfUA1);
        Assert.NotEmpty(report.Pages);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageIsTheSlideSize()
    {
        (PresentationRenderReport report, byte[] pdf) = Pdf(SlideCases.Get("inherit-position"));
        Assert.Equal(720, report.Pages[0].Width);
        Assert.Equal(540, report.Pages[0].Height);
        using var doc = PdfDocument.Open(pdf);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void ATitleWithoutPositionTakesItFromTheLayoutAndMaster()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("inherit-position"));
        Line title = Lines(pdf, 0).Single(l => l.Text.Contains("Inherited title position", StringComparison.Ordinal));
        // The master's title box is 36 pt from the left, 648 pt wide (centred text, so the line's centre is at 360), 21.6 pt from the top and 90 pt tall (anchored in the middle).
        Assert.InRange((title.Left + title.Right) / 2, 357, 363);
        Assert.InRange((title.Top + title.Bottom) / 2, 21.6, 111.6);
        Assert.InRange(title.Height, 38, 56);
    }

    [Fact]
    public void BodyLevelsTakeIndentsAndSizesFromTheMasterStyles()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("inherit-position"));
        List<Line> lines = Lines(pdf, 0);
        Line first = lines.Single(l => l.Text.Contains("First level text", StringComparison.Ordinal));
        Line second = lines.Single(l => l.Text.Contains("Second level text", StringComparison.Ordinal));
        Line third = lines.Single(l => l.Text.Contains("Third level text", StringComparison.Ordinal));
        // The body box starts at 36 pt + 7.2 pt inset; the first level's text is at its 27 pt margin, each deeper level 36 pt further.
        double bulletFree(Line l) => l.Left;
        Assert.True(second.Left > first.Left + 20);
        Assert.True(third.Left > second.Left + 20);
        Assert.True(first.Height > second.Height && second.Height > third.Height, $"heights {first.Height} {second.Height} {third.Height}");
        Assert.True(first.Top > 126, "the body starts at 126 pt");
        _ = bulletFree(first);
    }

    [Fact]
    public void EveryBulletKindAndAutoNumberRestartAreDrawn()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("bullets"));
        string all = Squash(Lines(pdf, 0));
        foreach (string expected in new[] { "1.Firstnumber", "2.Secondnumber", "a)Lowerletterinside", "b)Anotherletter", "3.Thirdnumbercontinues", "1.Restartedatone", "IV.Romanfromfour", "V.Romanfive", "–Reddashat150percent", "Nobullethere" })
        {
            Assert.Contains(expected, all, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ANumberedParagraphAfterAnInterruptionStartsAgain()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("bullets"));
        string all = Squash(Lines(pdf, 0));
        Assert.DoesNotContain("4.Restartedatone", all, StringComparison.Ordinal);
        Assert.DoesNotContain("2.Restartedatone", all, StringComparison.Ordinal);
    }

    [Fact]
    public void StoredAutofitIsAppliedAsStoredAndComputedAutofitFitsTheBox()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("autofit"));
        List<Line> lines = Lines(pdf, 0);
        double Bottom(double x0, double x1, double y0, double y1) => lines.Where(l => l.Left >= x0 && l.Right <= x1 + 1 && l.Top >= y0 - 1 && l.Top <= y1 + 5).Max(l => l.Bottom);
        double stored = Bottom(30, 350, 30, 230);
        double computed = Bottom(370, 690, 30, 230);
        double none = Bottom(30, 350, 280, 480);
        Assert.True(stored <= 232, $"stored scale text ends at {stored}");
        Assert.True(computed <= 232, $"computed scale text ends at {computed}");
        Assert.True(none > 482, $"no autofit overflows the 480 pt bottom: {none}");
        Line storedFirst = lines.First(l => l.Left >= 30 && l.Right <= 351 && l.Top < 60);
        Line noneFirst = lines.First(l => l.Left >= 30 && l.Right <= 351 && l.Top >= 280);
        Assert.InRange(storedFirst.Height / noneFirst.Height, 0.55, 0.7);
    }

    [Fact]
    public void StoredOnlyModeLeavesAnAutofitWithoutValuesAlone()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("autofit"), o => o.Autofit = SlideAutofitMode.Stored);
        double bottom = Lines(pdf, 0).Where(l => l.Left >= 370 && l.Top < 240).Max(l => l.Bottom);
        Assert.True(bottom > 232, "without stored values nothing is shrunk");
    }

    [Fact]
    public void AShapeThatResizesWithItsTextGrowsToHoldIt()
    {
        RgbaImage img = Raster(SlideCases.Get("autofit"));
        // The box is declared 40 pt tall at y = 280; its three lines of 18 pt text need about 70 pt, so the fill continues past 320.
        Assert.Equal((0xDD, 0xEB, 0xF7), Pixel(img, 686, 340));
        Assert.NotEqual((0xDD, 0xEB, 0xF7), Pixel(img, 686, 400));
    }

    [Fact]
    public void VerticalTextRunsDownTheBox()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("vertical-and-columns"));
        List<Line> lines = Lines(pdf, 0);
        Line v = lines.First(l => l.Right < 125 && l.Left > 25);
        Assert.True(v.Vertical || v.Height > v.Right - v.Left, $"vertical line box {v}");
    }

    [Fact]
    public void ColumnsFillOneAfterTheOther()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("vertical-and-columns"));
        List<Line> lines = Lines(pdf, 0).Where(l => l.Text.StartsWith("Column", StringComparison.Ordinal)).ToList();
        Assert.Contains(lines, l => Math.Abs(l.Left - 267.2) < 2);
        Assert.Contains(lines, l => Math.Abs(l.Left - 484) < 3);
        Assert.All(lines, l => Assert.True(l.Bottom <= 182, "no text past the box"));
    }

    [Fact]
    public void AnchorsPlaceTextAtTopMiddleAndBottom()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("anchors-wrap"));
        List<Line> lines = Lines(pdf, 0);
        Line top = lines.Single(l => l.Text == "Top");
        Line middle = lines.Single(l => l.Text == "Middle");
        Line bottom = lines.Single(l => l.Text == "Bottom");
        Assert.InRange(top.Top, 33, 38);
        Assert.InRange((middle.Top + middle.Bottom) / 2, 85, 95);
        Assert.InRange(bottom.Bottom, 142, 148);
        Line noWrap = lines.Single(l => l.Text.StartsWith("No wrap", StringComparison.Ordinal));
        Assert.True(noWrap.Right - noWrap.Left > 200, "a line that does not wrap is wider than its box");
        Line insets = lines.Single(l => l.Text == "Large insets");
        Assert.InRange(insets.Left, 470 + 36 - 1, 470 + 36 + 1);
    }

    [Fact]
    public void AlignmentSpacingTabsAndBreaksAreLaidOut()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("alignment-spacing"));
        List<Line> lines = Lines(pdf, 0);
        string all = Squash(lines);
        Assert.Contains("linetwoafterabreak", all, StringComparison.Ordinal);
        Line one = lines.Single(l => l.Text.StartsWith("Line one", StringComparison.Ordinal));
        Line two = lines.Single(l => l.Text.StartsWith("line two", StringComparison.Ordinal));
        Assert.True(two.Top > one.Top + 10 && Math.Abs(two.Left - one.Left) < 1, "a line break starts a new line at the same indent");
        Line tab = lines.Single(l => l.Text.StartsWith("Tab", StringComparison.Ordinal));
        Assert.True(tab.Right > 380 + 7.2 + 100, "the tab stop at one hundred points moves the text");
    }

    [Fact]
    public void RunFormattingReachesThePdfText()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("runs"));
        string all = Squash(Lines(pdf, 0));
        Assert.Contains("CAPS", all, StringComparison.Ordinal);
        Assert.Contains("SMALLCAPS", all, StringComparison.Ordinal);
        Assert.Contains("Alinkandplaintextafterit.", all, StringComparison.Ordinal);
    }

    [Fact]
    public void AHyperlinkIsATaggedLink()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("runs"));
        using var doc = PdfDocument.Open(pdf);
        Assert.Contains("Link", doc.StructureTree!.Elements().Select(e => e.Type));
    }

    [Fact]
    public void TitlesAreHeadingsAndBulletedParagraphsAreListItems()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("bullets"));
        using var doc = PdfDocument.Open(pdf);
        HashSet<string> types = [.. doc.StructureTree!.Elements().Select(e => e.Type)];
        Assert.Contains("H1", types);
        Assert.Contains("L", types);
        Assert.Contains("LI", types);
        Assert.Contains("Lbl", types);
        Assert.Contains("LBody", types);
        Assert.Contains("Sect", types);
    }

    [Fact]
    public void TablesDrawTextTagsAndGrowRowsToHoldIt()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("table"));
        List<Line> lines = Lines(pdf, 0);
        string all = Squash(lines);
        foreach (string expected in new[] { "Name", "Banded", "Mergedacrosstwocolumns", "Vertical", "Last", "Plain", "Nostyle" })
        {
            Assert.Contains(expected, all, StringComparison.Ordinal);
        }

        Assert.Single(lines, l => l.Text.Contains("Merged across two columns", StringComparison.Ordinal));
        Assert.True(lines.Count(l => l.Left < 100 && l.Top > 220) >= 3, "the tall cell wraps");
        using var doc = PdfDocument.Open(pdf);
        HashSet<string> types = [.. doc.StructureTree!.Elements().Select(e => e.Type)];
        Assert.Contains("Table", types);
        Assert.Contains("TR", types);
        Assert.Contains("TH", types);
        Assert.Contains("TD", types);
    }

    [Fact]
    public void TheDefaultTableStyleColoursHeaderAndBands()
    {
        RgbaImage img = Raster(SlideCases.Get("table"));
        // Medium Style 2 - Accent 1: the header row is the accent colour (4472C4), the first banded row a 40 % tint.
        Assert.Equal((0x44, 0x72, 0xC4), Pixel(img, 560, 150));
        (int r, int g, int b) = Pixel(img, 560, 178);
        Assert.True(r > 0x90 && b > 0xC0, $"banded row colour {r:X2}{g:X2}{b:X2}");
    }

    [Fact]
    public void HiddenSlidesAreSkippedUnlessAsked()
    {
        (PresentationRenderReport report, byte[] pdf) = Pdf(SlideCases.Get("slides-hidden-notes"));
        Assert.Equal(2, report.SlidePages);
        Assert.Equal(1, report.HiddenSlidesSkipped);
        string all = Squash(Lines(pdf, 0)) + Squash(Lines(pdf, 1));
        Assert.DoesNotContain("Neversh", all, StringComparison.Ordinal);
        (PresentationRenderReport withHidden, _) = Pdf(SlideCases.Get("slides-hidden-notes"), o => o.IncludeHiddenSlides = true);
        Assert.Equal(3, withHidden.SlidePages);
    }

    [Fact]
    public void SlideNumberAndDateFieldsAreFilledIn()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("slides-hidden-notes"), o => o.Clock = new FixedClock(new DateTimeOffset(2026, 3, 14, 9, 0, 0, TimeSpan.Zero)));
        Assert.Contains(Lines(pdf, 0), l => l.Text == "3");
        List<Line> second = Lines(pdf, 1);
        Assert.Contains(second, l => l.Text.Contains("Today: 3/14/2026", StringComparison.Ordinal));
        (_, byte[] stored) = Pdf(SlideCases.Get("slides-hidden-notes"));
        Assert.Contains(Lines(stored, 1), l => l.Text.Contains("Today: 1/1/2000", StringComparison.Ordinal));
    }

    [Fact]
    public void NotesPagesHoldTheSlideImageAndTheNotesText()
    {
        (PresentationRenderReport report, byte[] pdf) = Pdf(SlideCases.Get("slides-hidden-notes"), o => o.Output = PresentationOutput.Slides | PresentationOutput.NotesPages);
        Assert.Equal(2, report.SlidePages);
        Assert.Equal(2, report.NotesPages);
        RenderedPage notes = report.Pages.First(p => p.Kind == RenderedPageKind.Notes);
        Assert.Equal(540, notes.Width);
        Assert.Equal(720, notes.Height);
        List<Line> page = Lines(pdf, 2, 720);
        Assert.Contains(page, l => l.Text.Contains("Speaker notes for the first slide.", StringComparison.Ordinal));
        Assert.Contains(page, l => l.Text.Contains("First slide", StringComparison.Ordinal));
        string all = Squash(Lines(pdf, 2, 720)) + Squash(Lines(pdf, 3, 720));
        Assert.DoesNotContain("hiddenslide", all, StringComparison.Ordinal);
    }

    [Fact]
    public void HandoutPagesHoldSeveralSlides()
    {
        (PresentationRenderReport report, byte[] pdf) = Pdf(SlideCases.Get("slides-hidden-notes"), o => o.Output = PresentationOutput.Handouts);
        Assert.Single(report.Pages);
        Assert.Equal(RenderedPageKind.Handout, report.Pages[0].Kind);
        string all = Squash(Lines(pdf, 0, 720));
        Assert.Contains("Firstslide", all, StringComparison.Ordinal);
        Assert.Contains("Thirdslide", all, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundsInheritFromLayoutAndMasterShapesCanBeHidden()
    {
        byte[] pptx = SlideCases.Get("backgrounds");
        RgbaImage first = Raster(pptx, 0);
        Assert.Equal((0x1F, 0x38, 0x64), Pixel(first, 10, 10)); // the master's own rectangle
        (int r, int g, int b) = Pixel(first, 10, 520);
        Assert.True(r > 0xE8 && b == 0xFF, $"bottom of the layout gradient {r:X2}{g:X2}{b:X2}");
        (int r2, _, _) = Pixel(first, 10, 60);
        Assert.True(r2 < 0xE8, "the gradient starts blue at the top");
        RgbaImage second = Raster(pptx, 1);
        Assert.Equal((0xFF, 0xF2, 0xCC), Pixel(second, 10, 10)); // own background, master graphics hidden
    }

    [Fact]
    public void ShapesPaintInZOrderAndGroupsScaleTheirChildren()
    {
        RgbaImage img = Raster(SlideCases.Get("shapes-groups"));
        Assert.Equal((0x44, 0x72, 0xC4), Pixel(img, 60, 60));
        Assert.Equal((0xED, 0x7D, 0x31), Pixel(img, 200, 150)); // the later rectangle covers the earlier one
        Assert.Equal((0xC6, 0xE0, 0xB4), Pixel(img, 600, 440)); // the group's child, stretched to twice its size
        (_, byte[] pdf) = Pdf(SlideCases.Get("shapes-groups"));
        Line grouped = Lines(pdf, 0).Single(l => l.Text.Contains("Grouped text", StringComparison.Ordinal));
        Assert.True(grouped.Left > 400 && grouped.Top > 300);
    }

    [Fact]
    public void OfficeConvertWritesPdfAndPngForPresentations()
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "slide-convert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "deck.pptx");
            File.WriteAllBytes(input, SlideCases.Get("inherit-position"));
            PresentationFormats.Register(FormatRegistry.Default);
            RenderingFormats.Register(FormatRegistry.Default);
            using (Presentation p = Presentation.Open(input))
            {
                p.SaveAs(Path.Combine(dir, "deck.pdf"), OfficeFormat.Pdf);
                p.SaveAs(Path.Combine(dir, "deck.png"), OfficeFormat.Png);
            }

            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dir, "deck.pdf")), 0, 5), StringComparison.Ordinal);
            Assert.True(File.ReadAllBytes(Path.Combine(dir, "deck.png")).Length > 100);
            Office.Convert(input, Path.Combine(dir, "converted.pdf"));
            Assert.True(new FileInfo(Path.Combine(dir, "converted.pdf")).Length > 500);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FontSubstitutionsAndApproximationsAreReported()
    {
        (PresentationRenderReport report, _) = Pdf(SlideCases.Get("vertical-and-columns"));
        Assert.DoesNotContain(report.FontNotes, n => n.Requested == "Liberation Sans");
        (PresentationRenderReport slide, _) = Pdf(SlideCases.Get("slides-hidden-notes"), o => o.Output = PresentationOutput.Handouts);
        Assert.Contains(slide.Notes, n => n.Code == "slide.handout-master-ignored");
    }

    [Fact]
    public void ARunWithNoPageStillWritesAValidPdf()
    {
        var deck = new Deck { Slides = [new SlideSpec { Shapes = string.Empty, Hidden = true }] };
        (PresentationRenderReport report, byte[] pdf) = Pdf(TestPptx.Build(deck));
        Assert.Empty(report.Pages);
        using var doc = PdfDocument.Open(pdf);
        Assert.Equal(1, doc.PageCount);
        Assert.Contains(report.Notes, n => n.Code == "slide.no-pages");
    }

    [Fact]
    public void PicturesChartsAndSavedDiagramsArePainted()
    {
        byte[] pptx = SlideCases.Get("pictures-charts-diagrams");
        RgbaImage img = Raster(pptx);
        Assert.Equal((255, 0, 0), Pixel(img, 90, 90)); // the picture
        int inked = 0;
        for (int x = 200; x < 660; x += 2)
        {
            for (int y = 40; y < 280; y += 2)
            {
                if (Pixel(img, x, y) != (255, 255, 255))
                {
                    inked++;
                }
            }
        }

        Assert.True(inked > 300, $"the chart paints {inked} sampled pixels");
        Assert.Equal((0x44, 0x72, 0xC4), Pixel(img, 110, 340)); // the diagram's cached node
        (PresentationRenderReport report, byte[] pdf) = Pdf(pptx);
        Assert.Contains("Cachednode", Squash(Lines(pdf, 0)), StringComparison.Ordinal);
        Assert.DoesNotContain(report.Notes, n => n.Code is "drawing.chart-not-painted" or "drawing.graphic-frame" or "drawing.picture-undecodable");
        Assert.Contains(report.Notes, n => n.Code == "slide.smartart-cached");
    }

    [Fact]
    public void PicturesAndFramesAreFiguresWithAlternativeText()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("pictures-charts-diagrams"));
        using var doc = PdfDocument.Open(pdf);
        List<string> figures = [.. doc.StructureTree!.Elements().Where(e => e.Type == "Figure").Select(e => e.Alt ?? string.Empty)];
        Assert.Contains("A red square", figures);
        Assert.Contains("A line chart", figures);
        Assert.Contains("A diagram", figures);
        Assert.Contains("A second line chart", figures); // two charts in one list need distinct item ids
    }

    [Fact]
    public void BulletsAreDrawnWithoutTags()
    {
        using Presentation p = Open(SlideCases.Get("bullets"));
        byte[] png = PresentationRenderer.RenderPng(p, 0, Options(o => o.Tagged = false));
        Assert.True(png.Length > 100);
        RgbaImage img = Raster(SlideCases.Get("bullets"));
        // The round bullet of the first body line sits at the box's left inset plus nothing; find ink between x = 43 and 52 pt.
        bool ink = false;
        for (int x = 43; x < 52 && !ink; x++)
        {
            for (int y = 128; y < 150 && !ink; y++)
            {
                ink = Pixel(img, x, y).R < 100;
            }
        }

        Assert.True(ink, "the bullet is painted");
    }

    [Fact]
    public void AWideLabelPushesTheTextToTheNextTabStop()
    {
        (_, byte[] pdf) = Pdf(SlideCases.Get("bullets"));
        List<Line> lines = Lines(pdf, 0);
        Line roman = lines.First(l => l.Text.Contains("Roman from four", StringComparison.Ordinal));
        Line plain = lines.First(l => l.Text.Contains("Restarted at one", StringComparison.Ordinal));
        Assert.True(roman.Left >= plain.Left - 0.5 || roman.Text.StartsWith("IV", StringComparison.Ordinal));
        Line text = lines.First(l => l.Text == "Roman from four");
        Assert.InRange(text.Left, 36 + 7.2 + 72 - 1, 36 + 7.2 + 72 + 1);
    }

    [Fact]
    public void HostileValuesDoNotBreakTheLayout()
    {
        string hostile = Box(2, 10, 10, 200, 100,
            PP(" marL=\"2147483647\" indent=\"-2147483647\"", "<a:lnSpc><a:spcPct val=\"2147483647\"/></a:lnSpc><a:spcBef><a:spcPts val=\"2147483647\"/></a:spcBef>", R("Huge margins", " sz=\"2147483647\" spc=\"2147483647\" baseline=\"2147483647\"")) +
            P("Zero scale", " algn=\"just\""),
            "<a:bodyPr lIns=\"-100\" rIns=\"2147483647\" numCol=\"99999\" spcCol=\"2147483647\"><a:normAutofit fontScale=\"0\" lnSpcReduction=\"2147483647\"/></a:bodyPr>");
        var deck = new Deck { Slides = [new SlideSpec { Shapes = hostile }] };
        (PresentationRenderReport report, _) = Pdf(TestPptx.Build(deck));
        Assert.Single(report.Pages);
    }

    [Fact]
    public void AnEnormousTextBodyIsCutOffWithANote()
    {
        string text = new('x', 150_000);
        var deck = new Deck { Slides = [new SlideSpec { Shapes = Box(2, 10, 10, 300, 300, P(text) + P(text) + P("after")) }] };
        (PresentationRenderReport report, _) = Pdf(TestPptx.Build(deck));
        Assert.Contains(report.Notes, n => n.Code == "slide.text-truncated");
    }

    [Fact]
    public void AnEquationInSlideTextIsLaidOutInline()
    {
        string math = "<a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Before </a:t></a:r><mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\" Requires=\"a14\"><a14:m><m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath></a14:m></mc:Choice><mc:Fallback><a:r><a:t>fallback</a:t></a:r></mc:Fallback></mc:AlternateContent><a:r><a:rPr lang=\"en-US\"/><a:t> after</a:t></a:r></a:p>";
        var deck = new Deck { Slides = [new SlideSpec { Shapes = Box(2, 10, 10, 300, 100, math) }] };
        (PresentationRenderReport report, byte[] pdf) = Pdf(TestPptx.Build(deck));
        List<Line> lines = Lines(pdf, 0);
        string all = Squash(lines);
        Assert.Contains("Before", all, StringComparison.Ordinal);
        Assert.Contains("after", all, StringComparison.Ordinal);
        Assert.DoesNotContain("fallback", all, StringComparison.Ordinal);
        using var doc = PdfDocument.Open(pdf);
        Assert.Contains(doc.StructureTree!.Elements(), e => e.Type == "Figure");
        Assert.DoesNotContain(report.Notes, n => n.Code == "slide.math-plain-text");
    }
}
