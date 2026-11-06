using System.Text;
using OneImage;
using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Cli.Tests;

public sealed class RenderCommandTests
{
    // A document of several pages: enough short paragraphs to run past three pages.
    private static string Make(string dir)
    {
        using Document doc = Document.Create();
        for (int i = 1; i <= 220; i++)
        {
            doc.Body.AddParagraph("Paragraph " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        string path = Path.Combine(dir, "in.docx");
        doc.SaveAs(path);
        return path;
    }

    private static string Scratch()
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "render-cli-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ImageFormatsAndPageRangesComeFromTheOutputAndPagesOptions()
    {
        string dir = Scratch();
        try
        {
            Rendering.RenderingFormats.Register(Formats.FormatRegistry.Default);
            string input = Make(dir);
            var err = new StringWriter();

            // One page to one file: page 1 by default, the page asked for otherwise.
            Assert.Equal(0, RenderCommand.Run([input, "-o", Path.Combine(dir, "a.webp"), "--dpi", "48"], err));
            Assert.Equal((byte)'R', File.ReadAllBytes(Path.Combine(dir, "a.webp"))[0]);
            Assert.Equal(0, RenderCommand.Run([input, "-o", Path.Combine(dir, "b.jpg"), "--page", "3", "--width", "300"], err));
            Assert.Equal(300, Image.Identify(File.ReadAllBytes(Path.Combine(dir, "b.jpg"))).Width);

            // Several pages: one numbered file each.
            Assert.Equal(0, RenderCommand.Run([input, "-o", Path.Combine(dir, "c.png"), "--pages", "2-3", "--dpi", "48"], err));
            Assert.True(File.Exists(Path.Combine(dir, "c-2.png")) && File.Exists(Path.Combine(dir, "c-3.png")));
            Assert.False(File.Exists(Path.Combine(dir, "c.png")));

            // A TIFF holds all of them.
            Assert.Equal(0, RenderCommand.Run([input, "-o", Path.Combine(dir, "d.tif"), "--dpi", "48"], err));
            Assert.True(Image.Identify(File.ReadAllBytes(Path.Combine(dir, "d.tif"))).Frames.Count >= 4);
            Assert.Equal(0, RenderCommand.Run([input, "-o", Path.Combine(dir, "e.tiff"), "--pages", "2,3", "--dpi", "48"], err));
            Assert.Equal(2, Image.Identify(File.ReadAllBytes(Path.Combine(dir, "e.tiff"))).Frames.Count);

            // SVG of a page, text kept as text.
            Assert.Equal(0, RenderCommand.Run([input, "-o", Path.Combine(dir, "f.svg"), "--page", "2", "--svg-glyphs", "text"], err));
            string svg = File.ReadAllText(Path.Combine(dir, "f.svg"), Encoding.UTF8);
            Assert.StartsWith("<svg ", svg, StringComparison.Ordinal);
            Assert.Contains("<text ", svg, StringComparison.Ordinal);

            // Mistakes are reported, not thrown.
            Assert.Equal(1, RenderCommand.Run([input, "-o", Path.Combine(dir, "g.png"), "--page", "9"], err));
            Assert.Equal(2, RenderCommand.Run([input, "-o", Path.Combine(dir, "h.png"), "--pages", "x"], err));
            Assert.Equal(2, RenderCommand.Run([input, "-o", Path.Combine(dir, "i.bmp")], err));
            Assert.Contains("page", err.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
