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

    // Three sets: GDI-recorded EMF, GDI+ recorded EMF (dual and plus-only), and WMF (placeable and not). Each case is a path relative to Dir.
    private static readonly (string Folder, string Pattern, string Reference)[] Sets =
    [
        ("", "*.emf", "{0}_gdi_256x256.png"),
        ("gdiplus", "*.emf", "{0}_gdiplus_256x256.png"),
        ("wmf", "*.wmf", "{0}_wmf_gdi_256x256.png"),
    ];

    public static IEnumerable<object[]> Cases() =>
        Sets.SelectMany(set => Directory.GetFiles(Path.Combine(Dir, set.Folder), set.Pattern).Select(f => new object[] { Path.GetRelativePath(Dir, f).Replace('\\', '/') })).OrderBy(c => (string)c[0], StringComparer.Ordinal);

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
        var (list, _, defects, painted) = Paint(File.ReadAllBytes(Path.Combine(Dir, name)));
        Assert.True(painted, name);
        Assert.True(list.Count > 2, name);
        Assert.DoesNotContain(defects, d => d.Code == "render.metafile-damaged");
    }

    [Fact]
    public void AgreementWithWindows()
    {
        var report = new StringBuilder("| Case | Agreement % | Drawn as |\n|---|---|---|\n");
        var results = new List<(string Set, string Name, double Pct)>();
        foreach ((string folder, string pattern, string reference) in Sets)
        {
            foreach (string file in Directory.GetFiles(Path.Combine(Dir, folder), pattern).OrderBy(f => f, StringComparer.Ordinal))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string refPng = Path.Combine(Dir, folder, string.Format(CultureInfo.InvariantCulture, reference, name));
                if (!File.Exists(refPng))
                {
                    continue;
                }

                string label = (folder.Length == 0 ? string.Empty : folder + "/") + name;
                var (list, images, defects, painted) = Paint(File.ReadAllBytes(file));
                Assert.True(painted, label);
                RgbaImage ours = Raster(list, images);
                if (Environment.GetEnvironmentVariable("ONEDOCUMENT_ARTIFACTS") is { Length: > 0 } art)
                {
                    Directory.CreateDirectory(art);
                    File.WriteAllBytes(Path.Combine(art, label.Replace('/', '_') + "_ours.png"), ours.ToPng());
                }

                OneImage.Image reference256 = OneImage.Image.Load(File.ReadAllBytes(refPng));
                Rgba32[] want = reference256.ToArray<Rgba32>();
                Assert.Equal(Size * Size, want.Length);
                int bad = 0;
                for (int i = 0; i < want.Length; i++)
                {
                    // The reference is opaque on white; ours is opaque too because the rasterizer starts from white.
                    // GDI+ references may carry alpha (a translucent clear); they are compared as if shown on white.
                    int a = want[i].A;
                    int wr = ((want[i].R * a) + (255 * (255 - a))) / 255, wg = ((want[i].G * a) + (255 * (255 - a))) / 255, wb = ((want[i].B * a) + (255 * (255 - a))) / 255;
                    int dr = Math.Abs(ours.Pixels[i * 4] - wr), dg = Math.Abs(ours.Pixels[(i * 4) + 1] - wg), db = Math.Abs(ours.Pixels[(i * 4) + 2] - wb);
                    if (dr > 48 || dg > 48 || db > 48)
                    {
                        bad++;
                    }
                }

                double pct = 100.0 * (want.Length - bad) / want.Length;
                results.Add((folder, label, pct));
                string how = defects.FirstOrDefault(d => d.Code == "render.metafile-rasterised") is { } r ? "bitmap: " + r.Message["The picture uses ".Length..] : "vector";
                report.Append(CultureInfo.InvariantCulture, $"| {label} | {pct:F2} | {how} |\n");
            }
        }

        foreach ((string folder, _, _) in Sets)
        {
            var set = results.Where(r => r.Set == folder).ToList();
            report.Append(CultureInfo.InvariantCulture, $"\nSet '{(folder.Length == 0 ? "gdi" : folder)}': mean {set.Average(r => r.Pct):F2} over {set.Count} cases.\n");
        }

        string? outDir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ARTIFACTS");
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "metafile-agreement.md"), report.ToString());
        }

        // Floors, not targets: the worst cases are explained in docs/metafiles.md.
        Assert.All(results, r => Assert.True(r.Pct >= 60, $"{r.Name} {r.Pct:F2}"));
        Assert.True(results.Where(r => r.Set == "").Average(r => r.Pct) >= 95, report.ToString());
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
        Assert.Contains(defects, d => d.Code == "render.metafile-damaged");

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

    // The interpreter has no injection seam, so containment is shown with hand-built hostile files: whatever the interpreter or the display
    // list does with them, nothing escapes the adapter and the list stays balanced.
    [Fact]
    public void HostileHandBuiltRecordsNeverEscapeTheAdapter()
    {
        foreach (float scale in new[] { float.PositiveInfinity, float.NaN, float.MaxValue })
        {
            var b = new List<byte>();
            void I(int v) => b.AddRange(BitConverter.GetBytes(v));
            void F(float v) => b.AddRange(BitConverter.GetBytes(v));
            I(1); I(88); I(0); I(0); I(99); I(99); I(0); I(0); I(2646); I(2646);
            I(0x464D4520); I(0x10000); I(88 + 32 + 24 + 8 + 20); I(4); I(1); I(0); I(0); I(0);
            I(1000); I(1000); I(100); I(100);
            I(35); I(32); F(scale); F(0); F(0); F(scale); F(0); F(0);
            I(43); I(24); I(int.MinValue); I(int.MinValue); I(int.MaxValue); I(int.MaxValue);
            I(14); I(20); I(0); I(16); I(20);
            var (list, _, _, _) = Paint(b.ToArray());
            list.Validate();
        }
    }

    [Fact]
    public void EmfPlusStringsAndBrushesAreDrawnAsVectorsWhenFallbackIsOff()
    {
        var (text, _, _, p1) = Paint(File.ReadAllBytes(Path.Combine(Dir, "gdiplus", "g_text_plusonly.emf")), allowRaster: false);
        Assert.True(p1);
        Assert.Contains(Enumerable.Range(0, text.Count), i => text[i].Kind == ItemKind.GlyphRun);

        var (gradients, images, _, p2) = Paint(File.ReadAllBytes(Path.Combine(Dir, "gdiplus", "g_gradients_plusonly.emf")), allowRaster: false);
        Assert.True(p2);
        Assert.Equal(0, images.Count);
        Assert.True(gradients.Count > 4);

        var (_, imgs3, _, p3) = Paint(File.ReadAllBytes(Path.Combine(Dir, "gdiplus", "g_images_plusonly.emf")));
        Assert.True(p3);
        Assert.True(imgs3.Count > 0);
    }

    [Fact]
    public void WmfIsDrawnWithAndWithoutThePlaceableHeader()
    {
        foreach (string name in new[] { "rect_basic.wmf", "rect_basic_nonplaceable.wmf" })
        {
            var (list, _, defects, painted) = Paint(File.ReadAllBytes(Path.Combine(Dir, "wmf", name)));
            Assert.True(painted, name);
            Assert.True(list.Count > 3, name);
            Assert.DoesNotContain(defects, d => d.Code == "render.metafile-damaged");
        }
    }
}
