using System.IO.Compression;
using System.Text;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class DrawingDocumentTests
{
    private static string? SampleDir => Environment.GetEnvironmentVariable("ONEDOCUMENT_SAMPLE_DIR");

    public static IEnumerable<object[]> Names() => DrawingSamples.All().Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(Names))]
    public void Sample_documents_render_to_tagged_pdf_and_png(string name)
    {
        byte[] docx = DrawingSamples.All().First(s => s.Name == name).Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        using var pdf = new MemoryStream();
        LayoutDocument layout = DocumentRenderer.WritePdf(doc, pdf, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library } });
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.ToArray(), 0, 4));
        Assert.NotEmpty(layout.Pages);
        byte[] png = DocumentRenderer.RenderPng(layout.Pages[0]);
        if (SampleDir is { } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".docx"), docx);
            File.WriteAllBytes(Path.Combine(dir, name + ".pdf"), pdf.ToArray());
            for (int i = 0; i < Math.Min(layout.Pages.Count, 4); i++)
            {
                File.WriteAllBytes(Path.Combine(dir, $"{name}-p{i + 1}.png"), DocumentRenderer.RenderPng(layout.Pages[i]));
            }
        }

        Assert.True(png.Length > 2000, "first page is not blank");
    }

    [Fact]
    public void An_inline_picture_takes_its_place_in_the_line_and_raises_it()
    {
        byte[] docx = DrawingSamples.All().First(s => s.Name == "pictures").Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        LayoutLine line = layout.Pages.SelectMany(p => p.Paragraphs).SelectMany(p => p.Lines).First(l => l.Text.StartsWith("Inline picture", StringComparison.Ordinal));
        LayoutRun pic = line.Runs.Single(r => r.RunKind == LayoutRunKind.Drawing);
        Assert.Equal(48, pic.Width, 3);
        Assert.True(line.Ascent >= 36 - 1e-6, "the line is at least as tall as the picture above the baseline");
        Assert.Contains('\uFFFC', line.Text);
        // The text before and after the picture is laid out on the same line, on either side of it.
        double picX = pic.Bounds.X;
        Assert.Contains(line.Runs, r => r.RunKind == LayoutRunKind.Text && r.Bounds.Right <= picX + 1e-6);
        Assert.Contains(line.Runs, r => r.RunKind == LayoutRunKind.Text && r.Bounds.X >= picX + 48 - 1e-6);
    }

    [Fact]
    public void Figures_are_tagged_with_alt_text_and_decorative_pictures_are_artifacts()
    {
        byte[] docx = DrawingSamples.All().First(s => s.Name == "pictures").Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        var notes = new List<DrawingNote>();
        DisplayList list = LayoutPainter.Paint(layout.Pages[0], new PaintOptions { Images = new DrawingImages(), DrawingNotes = notes });
        var figures = new List<string?>();
        int artifacts = 0;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem item = list[i];
            if (item.Kind == ItemKind.BeginTag && item.Role == TagRole.Figure)
            {
                figures.Add(item.AltText);
            }
            else if (item.Kind == ItemKind.BeginTag && item.Role == TagRole.Artifact)
            {
                artifacts++;
            }
        }

        Assert.Contains("centre crop", figures);
        Assert.Contains("grayscale", figures);
        Assert.True(artifacts >= 1, "the decorative picture is an artifact");
        Assert.Contains(notes, n => n.Code == "drawing.picture-missing");
        Assert.Contains(notes, n => n.Code == "drawing.picture-undecodable");
    }

    [Fact]
    public void A_metafile_picture_and_a_chart_are_painted_as_vector_content()
    {
        byte[] docx = DrawingSamples.All().First(s => s.Name == "metafile-chart").Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        var notes = new List<DrawingNote>();
        DisplayList list = LayoutPainter.Paint(layout.Pages[0], new PaintOptions { Images = new DrawingImages(), DrawingNotes = notes });
        Assert.DoesNotContain(notes, n => n.Code is "drawing.picture-undecodable" or "drawing.graphic-frame" or "drawing.chart-not-painted");
        int glyphRuns = 0, fills = 0;
        for (int i = 0; i < list.Count; i++)
        {
            glyphRuns += list[i].Kind == ItemKind.GlyphRun ? 1 : 0;
            fills += list[i].Kind == ItemKind.FillPath ? 1 : 0;
        }

        Assert.True(glyphRuns > 5, "chart axis labels and legend are text");
        Assert.True(fills > 6, "metafile shapes and chart bars are fills");
    }

    [Fact]
    public void Layout_stays_correct_when_a_drawing_cannot_be_painted()
    {
        // A drawing without extent, and an inline shape of zero size, must not stop the page.
        string body = "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"/></w:drawing></w:r><w:r><w:t>after</w:t></w:r></w:p>";
        using Document doc = Document.Open(new MemoryStream(TestDocx.Bytes(body)), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        Assert.Contains(layout.Notes, n => n.Code == "layout.drawing-skipped");
        Assert.Contains("after", layout.Pages[0].Paragraphs.First().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_slide_shape_tree_paints_through_the_shared_painter()
    {
        MarkupDocument tree = GeometryTests.Parse(DrawingSamples.SlideTree());
        var list = new DisplayList();
        uint n = 0;
        var ctx = new DrawingContext();
        int shapes = DrawingPainter.DrawShapes(list, () => ItemId.Of(++n), tree.Root, 0, 0, ctx);
        Assert.Equal(5, shapes);
        list.Validate();
        RgbaImage img = DisplayListRasterizer.Render(list, 720, 480, new RasterOptions { Images = ctx.Images });
        Assert.NotEqual(255, img.Pixels[(100 * 720 * 4) + (100 * 4)]);
        using var pdf = new MemoryStream();
        using (var w = new DisplayListPdfWriter(pdf, new PdfOutputOptions { Title = "slide", Language = "en-US", Tagged = true, LeaveOpen = true }, ctx.Images))
        {
            w.AddPage(list, new PdfPageSetup(720, 480));
            w.Finish();
        }

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.ToArray(), 0, 4));
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_SAMPLE_DIR") is { } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "slide-shapes.xml"), DrawingSamples.SlideTree());
            File.WriteAllBytes(Path.Combine(dir, "slide-shapes.pdf"), pdf.ToArray());
            File.WriteAllBytes(Path.Combine(dir, "slide-shapes.png"), img.ToPng());
        }
    }

    // The dark pixels of a rendered page inside a rectangle given in points: (minX, minY, maxX, maxY), or null when there are none.
    private static (double, double, double, double)? Ink(RgbaImage img, double x, double y, double w, double h)
    {
        double k = 96.0 / 72.0;
        int x0 = (int)(x * k) + 2, y0 = (int)(y * k) + 2, x1 = (int)((x + w) * k) - 2, y1 = (int)((y + h) * k) - 2;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                int i = ((py * img.Width) + px) * 4;
                if (img.Pixels[i] < 90 && img.Pixels[i + 1] < 90 && img.Pixels[i + 2] < 90)
                {
                    minX = Math.Min(minX, px);
                    maxX = Math.Max(maxX, px);
                    minY = Math.Min(minY, py);
                    maxY = Math.Max(maxY, py);
                }
            }
        }

        return maxX < 0 ? null : (minX / k, minY / k, maxX / k, maxY / k);
    }

    [Fact]
    public void Text_in_a_shape_is_anchored_inset_and_rotated_by_its_body_properties()
    {
        byte[] docx = DrawingSamples.All().First(s => s.Name == "shape-text").Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        LayoutPage page = layout.Pages[0];
        DisplayList list = LayoutPainter.Paint(page, new PaintOptions { Images = new DrawingImages() });
        RgbaImage img = DisplayListRasterizer.Render(list, (int)Math.Ceiling(page.Width * 96 / 72), (int)Math.Ceiling(page.Height * 96 / 72), new RasterOptions());
        var boxes = page.Paragraphs.SelectMany(p => p.Lines).SelectMany(l => l.Runs.Where(r => r.RunKind == LayoutRunKind.Drawing).Select(r => (r, l))).ToList();
        (double X, double Y, double W, double H) Box(int i) => (boxes[i].r.Bounds.X, boxes[i].l.Baseline - boxes[i].r.Bounds.Height, boxes[i].r.Bounds.Width, boxes[i].r.Bounds.Height);

        // Top, centre, bottom: the ink sits at the top (below the 3.6 pt inset), in the middle, and at the bottom of the same-sized boxes.
        (double X, double Y, double W, double H) top = Box(0), mid = Box(1), bot = Box(2);
        var inkTop = Ink(img, top.X, top.Y, top.W, top.H);
        var inkMid = Ink(img, mid.X, mid.Y, mid.W, mid.H);
        var inkBot = Ink(img, bot.X, bot.Y, bot.W, bot.H);
        Assert.NotNull(inkTop);
        Assert.NotNull(inkMid);
        Assert.NotNull(inkBot);
        Assert.InRange(inkTop!.Value.Item2 - top.Y, 3.5, 10);
        Assert.InRange(((inkMid!.Value.Item2 + inkMid.Value.Item4) / 2) - (mid.Y + (mid.H / 2)), -6, 6);
        Assert.InRange((bot.Y + bot.H) - inkBot!.Value.Item4, 1.5, 9);

        // Wide insets (28.35 pt left and right) keep the ink off the sides.
        (double X, double Y, double W, double H) wide = Box(3);
        var inkWide = Ink(img, wide.X, wide.Y, wide.W, wide.H);
        Assert.NotNull(inkWide);
        Assert.True(inkWide!.Value.Item1 - wide.X >= 28, "left inset honoured");
        Assert.True((wide.X + wide.W) - inkWide.Value.Item3 >= 27, "right inset honoured");
        Assert.True(inkWide.Value.Item2 - wide.Y >= 14, "top inset honoured");

        // Vertical text: a narrow tall box holds a single column of rotated text, so the ink is taller than it is wide and sits at
        // the right (vert) or the left (vert270) side.
        (double X, double Y, double W, double H) v90 = Box(4), v270 = Box(5);
        var inkV90 = Ink(img, v90.X, v90.Y, v90.W, v90.H);
        var inkV270 = Ink(img, v270.X, v270.Y, v270.W, v270.H);
        Assert.NotNull(inkV90);
        Assert.NotNull(inkV270);
        Assert.True(inkV90!.Value.Item4 - inkV90.Value.Item2 > 2 * (inkV90.Value.Item3 - inkV90.Value.Item1), "vertical text runs down the box");
        Assert.True(inkV90.Value.Item3 > v90.X + (v90.W / 2), "vert text starts at the right");
        Assert.True(inkV270!.Value.Item1 < v270.X + (v270.W / 2), "vert270 text starts at the left");
    }
}
