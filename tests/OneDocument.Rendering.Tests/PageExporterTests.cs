using System.Globalization;
using System.Text;
using OneImage;
using OneImage.Svg;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Rendering.Svg;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class PageExporterTests
{
    private static byte[] Docx(string name) => LayoutCases.All().First(c => c.Name == name).Docx;

    private static Document Open(string name) => Document.Open(new MemoryStream(Docx(name)), leaveOpen: false);

    private static PageExportOptions Options() => new() { Layout = new LayoutOptions { Fonts = TestFonts.Library } };

    // The first case that lays out to several pages.
    private static string MultiPageCase()
    {
        foreach ((string name, byte[] docx) in LayoutCases.All())
        {
            using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
            if (LayoutEngine.Layout(d, new LayoutOptions { Fonts = TestFonts.Library }).Pages.Count >= 3)
            {
                return name;
            }
        }

        throw new InvalidOperationException("No multi-page case.");
    }

    [Fact]
    public void PageSelectionParsesAndResolves()
    {
        Assert.Equal([1, 2, 3, 5], PageSelection.Parse("1-3,5").Resolve(6));
        Assert.Equal([5, 6], PageSelection.Parse("5-").Resolve(6));
        Assert.Equal([1, 2], PageSelection.Parse("-2").Resolve(6));
        Assert.Equal([2, 4, 6], PageSelection.Parse("6, 2,4,2").Resolve(6));
        Assert.Equal([1, 2, 3], PageSelection.Parse("ALL").Resolve(3));
        Assert.Equal("1-3,5", PageSelection.Parse("1-3,5").ToString());
        foreach (string bad in new[] { "", "0", "a", "3-1", "1,,2", "-", "1-2-3", "-5x" })
        {
            Assert.False(PageSelection.TryParse(bad, out _), bad);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => PageSelection.Parse("4").Resolve(3));
        Assert.Equal([2, 3], PageSelection.Parse("2-9").Resolve(3));
        Assert.Throws<FormatException>(() => PageSelection.Parse("x"));
    }

    [Theory]
    [InlineData("a.png", PageImageFormat.Png)]
    [InlineData("a.JPG", PageImageFormat.Jpeg)]
    [InlineData("a.jpeg", PageImageFormat.Jpeg)]
    [InlineData("a.tif", PageImageFormat.Tiff)]
    [InlineData("a.TIFF", PageImageFormat.Tiff)]
    [InlineData("a.webp", PageImageFormat.WebP)]
    [InlineData("a.svg", PageImageFormat.Svg)]
    public void ExtensionsNameFormats(string path, PageImageFormat expected)
    {
        Assert.True(PageExporter.TryGetFormat(path, out PageImageFormat f));
        Assert.Equal(expected, f);
        Assert.False(PageExporter.TryGetFormat("a.pdf", out _));
    }

    [Theory]
    [InlineData(PageImageFormat.Png, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' })]
    [InlineData(PageImageFormat.Jpeg, new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData(PageImageFormat.WebP, new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' })]
    [InlineData(PageImageFormat.Tiff, new byte[] { (byte)'I', (byte)'I', 42, 0 })]
    [InlineData(PageImageFormat.Svg, new byte[] { (byte)'<', (byte)'s', (byte)'v', (byte)'g' })]
    public void EveryFormatWritesAFileOfThatFormat(PageImageFormat format, byte[] magic)
    {
        using Document d = Open("01-plain");
        using var ms = new MemoryStream();
        PageExportReport report = PageExporter.Export(d, ms, format, Options());
        byte[] data = ms.ToArray();
        Assert.Equal(magic, data.Take(magic.Length).ToArray());
        Assert.Equal([1], report.Exported);
        if (format != PageImageFormat.Svg)
        {
            ImageInfo info = Image.Identify(data);
            Assert.Equal(1224, info.Width);
            Assert.Equal(1584, info.Height);
        }
    }

    [Fact]
    public void RasterSizeFollowsDpiOrPixelSize()
    {
        using Document d = Open("01-plain");
        (int w, int h) Size(Action<PageExportOptions> set)
        {
            PageExportOptions o = Options();
            set(o);
            using var ms = new MemoryStream();
            PageExporter.Export(d, ms, PageImageFormat.Png, o);
            ImageInfo i = Image.Identify(ms.ToArray());
            return (i.Width, i.Height);
        }

        Assert.Equal((816, 1056), Size(o => o.Dpi = 96));
        Assert.Equal((612, 792), Size(o => o.Dpi = 72));
        Assert.Equal((400, 518), Size(o => o.WidthPixels = 400));
        (int fw, int fh) = Size(o => o.HeightPixels = 500);
        Assert.Equal(500, fh);
        Assert.InRange(fw, 386, 387);
        (int bw, int bh) = Size(o => { o.WidthPixels = 1000; o.HeightPixels = 500; });
        Assert.True(bw <= 1000 && bh <= 501);
        Assert.Throws<ArgumentOutOfRangeException>(() => Size(o => o.Dpi = 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Size(o => o.WidthPixels = 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Size(o => o.Quality = 101));
        Assert.Throws<OfficeFormatException>(() => Size(o => o.Dpi = 2400));
    }

    [Fact]
    public void ExportPagesWritesTheSelectedPagesInOrder()
    {
        using Document d = Open(MultiPageCase());
        int total = LayoutEngine.Layout(d, new LayoutOptions { Fonts = TestFonts.Library }).Pages.Count;
        var got = new List<PageImage>();
        PageExportOptions o = Options();
        o.Pages = PageSelection.Parse("3,1");
        o.Dpi = 48;
        PageExportReport report = PageExporter.ExportPages(d, PageImageFormat.Jpeg, got.Add, o);
        Assert.Equal([1, 3], got.Select(p => p.Number).ToArray());
        Assert.Equal(total, report.PageCount);
        Assert.Equal([1, 3], report.Exported);
        Assert.All(got, p => Assert.Equal(0xFF, p.Data[0]));
        o.Pages = null;
        got.Clear();
        PageExporter.ExportPages(d, PageImageFormat.WebP, got.Add, o);
        Assert.Equal(total, got.Count);
        Assert.Equal(Enumerable.Range(1, total), got.Select(p => p.Number));
        o.Pages = PageSelection.Page(total + 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => PageExporter.ExportPages(d, PageImageFormat.Png, got.Add, o));
    }

    [Fact]
    public void ASinglePageFormatRefusesSeveralPagesAndATiffTakesThemAll()
    {
        using Document d = Open(MultiPageCase());
        int total = LayoutEngine.Layout(d, new LayoutOptions { Fonts = TestFonts.Library }).Pages.Count;
        PageExportOptions o = Options();
        o.Dpi = 48;
        o.Pages = PageSelection.All;
        Assert.Throws<ArgumentException>(() => PageExporter.Export(d, new MemoryStream(), PageImageFormat.Png, o));

        using var ms = new MemoryStream();
        PageExportReport report = PageExporter.Export(d, ms, PageImageFormat.Tiff, o);
        Assert.Equal(total, report.Exported.Count);
        byte[] tiff = ms.ToArray();
        ImageInfo info = Image.Identify(tiff);
        Assert.Equal(total, info.Frames.Count);
        Image img = Image.Load(tiff);
        Assert.Equal(total, img.FrameCount);
        // Each page is its own picture: the pixels of page 2 are not those of page 1.
        Rgba32[] first = img.Frames[0].ToArray<Rgba32>();
        Rgba32[] second = img.Frames[1].ToArray<Rgba32>();
        Assert.NotEqual(first, second);

        // A page range gives a smaller TIFF; the default for a TIFF is every page.
        o.Pages = PageSelection.Parse("2-3");
        using var part = new MemoryStream();
        PageExporter.Export(d, part, PageImageFormat.Tiff, o);
        Assert.Equal(2, Image.Identify(part.ToArray()).Frames.Count);
        o.Pages = null;
        using var all = new MemoryStream();
        PageExporter.Export(d, all, PageImageFormat.Tiff, o);
        Assert.Equal(total, Image.Identify(all.ToArray()).Frames.Count);
    }

    [Fact]
    public void JoinedTiffPagesDecodeToTheSamePixelsAsSinglePages()
    {
        using Document d = Open(MultiPageCase());
        PageExportOptions o = Options();
        o.Dpi = 48;
        foreach (TiffCompression c in new[] { TiffCompression.None, TiffCompression.Lzw, TiffCompression.Deflate, TiffCompression.PackBits })
        {
            o.TiffCompression = c;
            var singles = new List<PageImage>();
            PageExporter.ExportPages(d, PageImageFormat.Tiff, singles.Add, o);
            using var ms = new MemoryStream();
            PageExporter.Export(d, ms, PageImageFormat.Tiff, o);
            Image joined = Image.Load(ms.ToArray());
            Assert.Equal(singles.Count, joined.FrameCount);
            for (int i = 0; i < singles.Count; i++)
            {
                Image one = Image.Load(singles[i].Data);
                Assert.Equal(one.ToArray<Rgba32>(), joined.Frames[i].ToArray<Rgba32>());
            }
        }
    }

    [Fact]
    public void TransparentBackgroundKeepsAlphaExceptInJpeg()
    {
        using Document d = Open("01-plain");
        PageExportOptions o = Options();
        o.Dpi = 48;
        o.TransparentBackground = true;
        using var png = new MemoryStream();
        PageExporter.Export(d, png, PageImageFormat.Png, o);
        Rgba32[] px = Image.Load(png.ToArray()).ToArray<Rgba32>();
        Assert.Equal(0, px[0].A);
        o.TransparentBackground = false;
        using var opaque = new MemoryStream();
        PageExporter.Export(d, opaque, PageImageFormat.Png, o);
        Assert.Equal(255, Image.Load(opaque.ToArray()).ToArray<Rgba32>()[0].A);
        o.TransparentBackground = true;
        using var jpg = new MemoryStream();
        PageExporter.Export(d, jpg, PageImageFormat.Jpeg, o);
        Rgba32 corner = Image.Load(jpg.ToArray()).ToArray<Rgba32>()[0];
        Assert.True(corner.R > 240 && corner.G > 240 && corner.B > 240);
    }

    [Fact]
    public void QualityChangesJpegSize()
    {
        using Document d = Open("01-plain");
        PageExportOptions o = Options();
        o.Dpi = 96;
        long Size(int q)
        {
            o.Quality = q;
            using var ms = new MemoryStream();
            PageExporter.Export(d, ms, PageImageFormat.Jpeg, o);
            return ms.Length;
        }

        Assert.True(Size(30) < Size(95));
    }

    [Fact]
    public void WorkbooksAndPresentationsExportToEveryFormat()
    {
        var (_, xlsx) = SheetCases.All()[0];
        using Workbook wb = Workbook.Open(new MemoryStream(xlsx), leaveOpen: false);
        var (_, pptx) = SlideCases.All()[0];
        using Presentation deck = Presentation.Open(new MemoryStream(pptx), leaveOpen: false);
        PageExportOptions o = Options();
        o.Dpi = 48;
        foreach (OfficeFile file in new OfficeFile[] { wb, deck })
        {
            foreach (PageImageFormat f in Enum.GetValues<PageImageFormat>())
            {
                using var ms = new MemoryStream();
                PageExportReport report = PageExporter.Export(file, ms, f, o);
                Assert.True(ms.Length > 100, file.Kind + " " + f);
                Assert.Equal([1], report.Exported);
            }
        }
    }

    [Fact]
    public void SaveAsAndConvertPickTheImageFormatByExtension()
    {
        RenderingFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "export-saveas-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            using Document d = Open(MultiPageCase());
            foreach (string ext in new[] { ".png", ".jpg", ".tif", ".webp", ".svg" })
            {
                d.SaveAs(Path.Combine(dir, "out" + ext));
                Assert.True(new FileInfo(Path.Combine(dir, "out" + ext)).Length > 100, ext);
            }

            // The registered writers take the first page, and every page for a TIFF.
            Assert.True(Image.Identify(File.ReadAllBytes(Path.Combine(dir, "out.tif"))).Frames.Count >= 3);
            Assert.Single(Image.Identify(File.ReadAllBytes(Path.Combine(dir, "out.webp"))).Frames);
            string docx = Path.Combine(dir, "in.docx");
            File.WriteAllBytes(docx, Docx("01-plain"));
            Office.Convert(docx, Path.Combine(dir, "conv.webp"));
            Assert.Equal((byte)'R', File.ReadAllBytes(Path.Combine(dir, "conv.webp"))[0]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SvgTextStaysTextWhereExactAndOutlinesElsewhere()
    {
        using Document d = Open("01-plain");
        PageExportOptions o = Options();
        string Write(SvgGlyphMode mode)
        {
            o.SvgGlyphMode = mode;
            using var ms = new MemoryStream();
            PageExporter.Export(d, ms, PageImageFormat.Svg, o);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        string auto = Write(SvgGlyphMode.Auto);
        string outlines = Write(SvgGlyphMode.Outlines);
        string text = Write(SvgGlyphMode.Text);
        Assert.Contains("<text ", auto, StringComparison.Ordinal);
        Assert.DoesNotContain("<text ", outlines, StringComparison.Ordinal);
        Assert.Contains("<text ", text, StringComparison.Ordinal);
        Assert.True(outlines.Length > text.Length);
        System.Xml.Linq.XDocument.Parse(auto);
        System.Xml.Linq.XDocument.Parse(outlines);
        using Document rtl = Open("19-arabic-rtl");
        using var ms2 = new MemoryStream();
        o.SvgGlyphMode = SvgGlyphMode.Auto;
        PageExporter.Export(rtl, ms2, PageImageFormat.Svg, o);
        // Arabic is joined and right to left: it is drawn as outlines, so any viewer shapes it the same.
        Assert.Contains("<path ", Encoding.UTF8.GetString(ms2.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void SvgPagesCarryTheStructureOfTheDocument()
    {
        using Document d = Open("01-plain");
        using var ms = new MemoryStream();
        PageExporter.Export(d, ms, PageImageFormat.Svg, Options());
        string svg = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("role=\"paragraph\"", svg, StringComparison.Ordinal);
        PageExportOptions o = Options();
        o.SvgTags = false;
        using var plain = new MemoryStream();
        PageExporter.Export(d, plain, PageImageFormat.Svg, o);
        Assert.DoesNotContain("role=", Encoding.UTF8.GetString(plain.ToArray()), StringComparison.Ordinal);
    }

    // ---- the SVG written for a page, drawn back by OneImage.Svg, against the image backend's drawing of the same page

    [Fact]
    public void SvgDrawnBackMatchesTheRasterOfTheSamePage()
    {
        var rows = new List<string> { "set\tpage\tsize\tmean\tpct_over_32\tnotes" };
        var worst = new List<string>();
        void Check(string set, string name, OfficeFile file, int maxPages)
        {
            PageExportOptions o = Options();
            o.WidthPixels = 1200;
            o.Pages = PageSelection.Range(1, 1);
            int total = PageExporter.ExportPages(file, PageImageFormat.Png, _ => { }, o).PageCount;
            for (int p = 1; p <= Math.Min(total, maxPages); p++)
            {
                o.Pages = PageSelection.Page(p);
                var png = new List<PageImage>();
                var svg = new List<PageImage>();
                PageExporter.ExportPages(file, PageImageFormat.Png, png.Add, o);
                o.SvgGlyphMode = SvgGlyphMode.Outlines;
                PageExportReport rep = PageExporter.ExportPages(file, PageImageFormat.Svg, svg.Add, o);
                (double mean, double over) = Compare(png[0].Data, svg[0].Data, set + "-" + name + "-" + p);
                string notes = string.Join(';', rep.Notes.Where(n => n.StartsWith("svg.", StringComparison.Ordinal)).Select(n => n.Split(':')[0]));
                rows.Add(string.Join('\t', set, name + "#" + p, png[0].WidthPixels + "x" + png[0].HeightPixels, mean.ToString("F3", CultureInfo.InvariantCulture), (over * 100).ToString("F3", CultureInfo.InvariantCulture), notes));
                if (mean > 1.0 || over > 0.015)
                {
                    worst.Add(set + "/" + name + "#" + p + " mean=" + mean.ToString("F2", CultureInfo.InvariantCulture) + " over32=" + (over * 100).ToString("F2", CultureInfo.InvariantCulture) + "%");
                }
            }
        }

        foreach ((string name, byte[] docx) in LayoutCases.All())
        {
            using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
            Check("docx", name, d, 2);
        }

        foreach ((string name, byte[] docx) in DrawingSamples.All())
        {
            using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
            Check("drawing-docx", name, d, 1);
        }

        foreach ((string name, byte[] pptx) in DrawingSamples.Presentations())
        {
            using Presentation deck = Presentation.Open(new MemoryStream(pptx), leaveOpen: false);
            Check("drawing-pptx", name, deck, 1);
        }

        foreach ((string name, byte[] xlsx) in SheetCases.All())
        {
            using Workbook wb = Workbook.Open(new MemoryStream(xlsx), leaveOpen: false);
            Check("xlsx", name, wb, 1);
        }

        foreach ((string name, byte[] pptx) in SlideCases.All())
        {
            using Presentation deck = Presentation.Open(new MemoryStream(pptx), leaveOpen: false);
            Check("pptx", name, deck, 1);
        }

        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_SVG_REPORT") is { Length: > 0 } report)
        {
            File.WriteAllLines(report, rows);
        }

        Assert.True(worst.Count == 0, "Pages outside the tolerance (mean abs difference 1.0 of 255, 1.5% of 4x4 blocks over 32):\n" + string.Join('\n', worst));
    }

    // The mean absolute difference per channel (0 to 255) and the share of pixels where any channel differs by more than 32.
    private static (double Mean, double OverShare) Compare(byte[] png, byte[] svg, string tag)
    {
        var config = new ImageConfiguration().Add(SvgFormat.Instance);
        Image a = Image.Load(png);
        Image b = Image.Load(svg, new LoadOptions { Configuration = config });
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
        Assert.True(Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1, $"sizes {a.Width}x{a.Height} and {b.Width}x{b.Height}");
        Rgba32[] pa = a.ToArray<Rgba32>(), pb = b.ToArray<Rgba32>();
        // Two rasterizers cover the edge pixels of a glyph differently, so the pages are compared after averaging 4 x 4 blocks:
        // that keeps every misplaced line, glyph or shape and drops the anti-aliasing noise.
        const int Block = 4;
        double sum = 0;
        long over = 0, cells = 0;
        for (int by = 0; by + Block <= h; by += Block)
        {
            for (int bx = 0; bx + Block <= w; bx += Block)
            {
                int ar = 0, ag = 0, ab = 0, br = 0, bg = 0, bb = 0;
                for (int y = by; y < by + Block; y++)
                {
                    for (int x = bx; x < bx + Block; x++)
                    {
                        Rgba32 u = pa[(y * a.Width) + x], v = pb[(y * b.Width) + x];
                        ar += u.R;
                        ag += u.G;
                        ab += u.B;
                        br += v.R;
                        bg += v.G;
                        bb += v.B;
                    }
                }

                int dr = Math.Abs(ar - br) / (Block * Block), dg = Math.Abs(ag - bg) / (Block * Block), db = Math.Abs(ab - bb) / (Block * Block);
                sum += dr + dg + db;
                cells++;
                if (Math.Max(dr, Math.Max(dg, db)) > 32)
                {
                    over++;
                }
            }
        }

        double mean = sum / (3.0 * cells);
        double overShare = over / (double)cells;
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_SVG_DUMP") is { Length: > 0 } dump && mean > 0.2)
        {
            Directory.CreateDirectory(dump);
            File.WriteAllBytes(Path.Combine(dump, tag + ".raster.png"), png);
            File.WriteAllBytes(Path.Combine(dump, tag + ".svg"), svg);
            b.Save(Path.Combine(dump, tag + ".svgdrawn.png"));
        }

        return (mean, overShare);
    }
}
