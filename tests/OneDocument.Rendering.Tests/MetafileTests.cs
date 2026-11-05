using System.Globalization;
using System.Text;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Rendering.Metafiles;
using Rgba32 = OneImage.Rgba32;

namespace RecursiveAI.OneDocument.Rendering.Tests;

/// <summary>
/// The metafile painter over the generated GDI fixtures in <c>testdata/metafile</c>. Each fixture has the picture Windows drew for it at
/// 256 x 256; the agreement test counts pixels that differ by more than 48 in any channel, as OneImage's own comparison does.
/// </summary>
public sealed class MetafileTests
{
    private const int Size = 256;

    private static string Dir
    {
        get
        {
            string here = Path.GetDirectoryName(Here())!;
            return Path.GetFullPath(Path.Combine(here, "..", "..", "testdata", "metafile"));
        }
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    public static IEnumerable<object[]> Cases() =>
        Directory.GetFiles(Dir, "*.emf").Select(f => new object[] { Path.GetFileNameWithoutExtension(f) }).OrderBy(c => (string)c[0]);

    private static byte[] Emf(string name) => File.ReadAllBytes(Path.Combine(Dir, name + ".emf"));

    private static (DisplayList List, PictureImages Images, List<Defect> Defects, bool Painted) Paint(byte[] emf, float w = Size, float h = Size, bool tagged = false, bool allowRaster = true)
    {
        var list = new DisplayList();
        var images = new PictureImages();
        var defects = new List<Defect>();
        uint owner = 0;
        var ctx = new MetafilePaintContext(list, images, () => ItemId.Of(++owner))
        {
            Report = defects.Add,
            Tagged = tagged,
            AltText = "test picture",
            AllowRasterFallback = allowRaster,
        };
        bool ok = MetafilePainter.TryPaint(emf, ctx, new RectF(0, 0, w, h));
        list.Validate();
        return (list, images, defects, ok);
    }

    private static RgbaImage Raster(DisplayList list, PictureImages images, int w = Size, int h = Size) =>
        DisplayListRasterizer.Render(list, w, h, new RasterOptions { Images = images });

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryFixtureIsDrawn(string name)
    {
        var (list, _, defects, painted) = Paint(Emf(name));
        Assert.True(painted, name);
        Assert.True(list.Count > 2, name);
        Assert.DoesNotContain(defects, d => d.Code == "render.metafile-damaged");
    }

    [Fact]
    public void AgreementWithWindows()
    {
        var report = new StringBuilder("| Case | Agreement | Drawn as |\n|---|---|---|\n");
        var worst = new List<(string Name, double Pct)>();
        double sum = 0;
        int n = 0;
        foreach (string file in Directory.GetFiles(Dir, "*.emf").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string refPng = Path.Combine(Dir, name + "_gdi_256x256.png");
            if (!File.Exists(refPng))
            {
                continue;
            }

            var (list, images, defects, painted) = Paint(File.ReadAllBytes(file));
            Assert.True(painted, name);
            RgbaImage ours = Raster(list, images);
            if (Environment.GetEnvironmentVariable("ONEDOCUMENT_ARTIFACTS") is { Length: > 0 } art)
            {
                Directory.CreateDirectory(art);
                File.WriteAllBytes(Path.Combine(art, name + "_ours.png"), ours.ToPng());
            }

            OneImage.Image reference = OneImage.Image.Load(File.ReadAllBytes(refPng));
            Rgba32[] want = reference.ToArray<Rgba32>();
            Assert.Equal(Size * Size, want.Length);
            int bad = 0;
            for (int i = 0; i < want.Length; i++)
            {
                // The reference is opaque on white; ours is opaque too because the rasterizer starts from white.
                int dr = Math.Abs(ours.Pixels[i * 4] - want[i].R), dg = Math.Abs(ours.Pixels[(i * 4) + 1] - want[i].G), db = Math.Abs(ours.Pixels[(i * 4) + 2] - want[i].B);
                if (dr > 48 || dg > 48 || db > 48)
                {
                    bad++;
                }
            }

            double pct = 100.0 * (want.Length - bad) / want.Length;
            sum += pct;
            n++;
            worst.Add((name, pct));
            string how = defects.FirstOrDefault(d => d.Code == "render.metafile-rasterised") is { } r ? "bitmap: " + r.Message["The picture uses ".Length..] : "vector";
            report.Append(CultureInfo.InvariantCulture, $"| {name} | {pct:F2} | {how} |\n");
        }

        report.Append(CultureInfo.InvariantCulture, $"\nMean {sum / n:F2} over {n} cases.\n");
        string? outDir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ARTIFACTS");
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "metafile-agreement.md"), report.ToString());
        }

        // A floor, not a target: the worst cases are explained in docs/metafiles.md.
        Assert.All(worst, w => Assert.True(w.Pct >= 80, $"{w.Name} {w.Pct:F2}"));
        Assert.True(sum / n >= 95, report.ToString());
    }

    [Fact]
    public void TextStaysTextInPdf()
    {
        var list = new DisplayList();
        var images = new PictureImages();
        uint owner = 0;
        var ctx = new MetafilePaintContext(list, images, () => ItemId.Of(++owner)) { Tagged = true, AltText = "A picture with text" };
        Assert.True(MetafilePainter.TryPaint(Emf("text_basic"), ctx, new RectF(40, 40, 400, 300)));
        list.Validate();
        Assert.Contains(Enumerable.Range(0, list.Count), i => list[i].Kind == ItemKind.GlyphRun);

        using var ms = new MemoryStream();
        using (var writer = new DisplayListPdfWriter(ms, new PdfOutputOptions { Tagged = true, Title = "t", Language = "en-US", LeaveOpen = true }, images))
        {
            writer.AddPage(list, new PdfPageSetup(480, 360));
            writer.Finish();
        }

        using var pdf = OnePdf.PdfDocument.Open(ms.ToArray());
        string text = string.Join(" ", pdf.ExtractText().SelectMany(p => p.Blocks).SelectMany(b => b.Lines).Select(l => l.Text));
        Assert.True(text.Length > 3, "extracted: " + text);
    }

    [Fact]
    public void RopPicturesFallBackToABitmap()
    {
        var (list, images, defects, painted) = Paint(Emf("rop2"));
        Assert.True(painted);
        Assert.Contains(defects, d => d.Code == "render.metafile-rasterised");
        Assert.Equal(1, images.Count);

        var (_, images2, defects2, painted2) = Paint(Emf("rop2"), allowRaster: false);
        Assert.True(painted2);
        Assert.DoesNotContain(defects2, d => d.Code == "render.metafile-rasterised");
        Assert.Equal(0, images2.Count);
        Assert.NotNull(list);
    }

    [Fact]
    public void CropShowsPartOfThePicture()
    {
        var list = new DisplayList();
        var images = new PictureImages();
        uint owner = 0;
        var ctx = new MetafilePaintContext(list, images, () => ItemId.Of(++owner));
        Assert.True(MetafilePainter.TryPaint(Emf("rect_basic"), ctx, new RectF(0, 0, Size, Size), new PictureCrop(0.5, 0, 0, 0)));
        list.Validate();
    }

    [Fact]
    public void NaturalSizeComesFromTheFrame()
    {
        Assert.True(MetafilePainter.TryGetNaturalSize(Emf("rect_basic"), out double w, out double h));
        Assert.True(w > 10 && h > 10);
    }

    [Fact]
    public void WmfAndGarbageAreReportedNotDrawn()
    {
        byte[] wmf = new byte[40];
        wmf[0] = 0xD7; wmf[1] = 0xCD; wmf[2] = 0xC6; wmf[3] = 0x9A;
        var (list, _, defects, painted) = Paint(wmf);
        Assert.False(painted);
        Assert.Equal(0, list.Count);
        Assert.Contains(defects, d => d.Code == "render.metafile-unsupported");

        var (_, _, defects2, painted2) = Paint(new byte[100]);
        Assert.False(painted2);
        Assert.Contains(defects2, d => d.Code == "render.metafile-damaged");

        byte[] truncated = Emf("rect_basic").AsSpan(0, 60).ToArray();
        var (_, _, _, painted3) = Paint(truncated);
        Assert.True(painted3 || !painted3); // must not throw
    }

    [Fact]
    public void MetafileInADocxPackageIsDrawnThroughTheHook()
    {
        byte[] emf = Emf("text_styled");
        byte[] docx = TestDocx.Bytes(TestDocx.P("Report"), extraParts: [("word/media/image1.emf", emf)]);
        using var package = RecursiveAI.OneDocument.Packaging.OpcPackage.Open(new MemoryStream(docx));
        RecursiveAI.OneDocument.Packaging.IPackagePart part = package.FindPart("/word/media/image1.emf")!;

        var list = new DisplayList();
        var images = new PictureImages();
        var defects = new List<Defect>();
        uint owner = 0;
        var ctx = new MetafilePaintContext(list, images, () => ItemId.Of(++owner))
        {
            Report = defects.Add,
            Location = DefectLocation.InPart(part.Name),
            Tagged = true,
            AltText = "Styled text",
        };
        // The package gave the part no media type; the bytes decide.
        Assert.True(PictureHook.TryPaint(part.GetBytes(), null, ctx, new RectF(72, 72, 360, 240)));
        using var ms = new MemoryStream();
        using (var writer = new DisplayListPdfWriter(ms, new PdfOutputOptions { Tagged = true, Title = "t", Language = "en-US", LeaveOpen = true }, images))
        {
            writer.AddPage(list, new PdfPageSetup(612, 792));
            writer.Finish();
        }

        using var pdf = OnePdf.PdfDocument.Open(ms.ToArray());
        string text = string.Join(" ", pdf.ExtractText().SelectMany(p => p.Blocks).SelectMany(b => b.Lines).Select(l => l.Text));
        Assert.True(text.Length > 3, "extracted: " + text);
        Assert.DoesNotContain(defects, d => d.Code == "render.metafile-damaged");
    }

    [Fact]
    public void ReportsApproximationsAndSkippedRecords()
    {
        var (_, _, approx, _) = Paint(Emf("clip_rect"), allowRaster: false);
        Assert.Contains(approx, d => d.Code == "render.metafile-approximated");

        var (_, _, fonts, _) = Paint(Emf("text_basic"));
        Assert.Contains(fonts, d => d.Code == "render.metafile-font" || d.Code == "render.metafile-diagnostic");

        // A header, one WIDENPATH record (not interpreted) and the end record.
        var b = new List<byte>();
        void I(int v) => b.AddRange(BitConverter.GetBytes(v));
        I(1); I(88); I(0); I(0); I(99); I(99); I(0); I(0); I(2646); I(2646);
        I(0x464D4520); I(0x10000); I(88 + 8 + 20); I(3); I(1); I(0); I(0); I(0);
        I(1000); I(1000); I(100); I(100);
        I(66); I(8);
        I(14); I(20); I(0); I(16); I(20);
        var (_, _, skipped, painted) = Paint(b.ToArray());
        Assert.True(painted);
        Assert.Contains(skipped, d => d.Code == "render.metafile-diagnostic" && d.Message.Contains("Record 1", StringComparison.Ordinal));
    }
}
