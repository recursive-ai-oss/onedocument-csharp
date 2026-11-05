using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

internal static class TestFonts
{
    private static readonly Lazy<FontLibrary> Shared = new(() =>
    {
        OneRender.Fonts.Noto.NotoArabicFonts.Register();
        OneRender.Fonts.Noto.NotoHebrewFonts.Register();
        OneRender.Fonts.Noto.NotoThaiFonts.Register();
        OneRender.Fonts.Cjk.CjkFonts.Register();
        var library = FontLibrary.CreateDefault();
        library.AddOptionalPackages();
        return library;
    });

    // Default faces plus the script packages, shared because loading the CJK collection is slow.
    internal static FontLibrary Library => Shared.Value;
}

public sealed class RenderTests
{
    public static IEnumerable<object[]> Cases() => LayoutCases.All().Select(c => new object[] { c.Name });

    private static byte[] Docx(string name) => LayoutCases.All().First(c => c.Name == name).Docx;

    [Fact]
    public void TheCaseSetHasAtLeastThirtyDocuments()
    {
        Assert.True(LayoutCases.All().Count >= 30);
        Assert.Equal(LayoutCases.All().Count, LayoutCases.All().Select(c => c.Name).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryCaseLaysOutAndWritesATaggedPdf(string name)
    {
        RenderingFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        using Document d = Document.Open(new MemoryStream(Docx(name)), leaveOpen: false);
        using var ms = new MemoryStream();
        LayoutDocument layout = DocumentRenderer.WritePdf(d, ms, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library }, Compress = false });
        byte[] pdf = ms.ToArray();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8), StringComparison.Ordinal);
        string text = Encoding.Latin1.GetString(pdf);
        Assert.Contains("/StructTreeRoot", text, StringComparison.Ordinal);
        Assert.Equal(layout.Pages.Count, CountPages(text));
        Assert.True(layout.Pages.Count >= 1);
        Assert.All(layout.Pages.SelectMany(p => p.Paragraphs).SelectMany(p => p.Lines), l => Assert.True(l.Bounds.Width >= 0));
    }

    [Theory]
    [InlineData("01-plain")]
    [InlineData("19-arabic-rtl")]
    [InlineData("22-chinese")]
    public void PagesRenderToPng(string name)
    {
        using Document d = Document.Open(new MemoryStream(Docx(name)), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(d, new LayoutOptions { Fonts = TestFonts.Library });
        byte[] png = DocumentRenderer.RenderPng(layout.Pages[0]);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.True(png.Length > 2000);
    }

    [Fact]
    public void SaveAsPicksPdfAndPngByExtension()
    {
        string dir = Path.Combine(Path.GetTempPath(), "layout-saveas-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            using Document d = Document.Open(new MemoryStream(Docx("02-wrap")), leaveOpen: false);
            d.SaveAs(Path.Combine(dir, "out.pdf"));
            d.SaveAs(Path.Combine(dir, "out.png"));
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dir, "out.pdf")), 0, 5), StringComparison.Ordinal);
            Assert.Equal(0x89, File.ReadAllBytes(Path.Combine(dir, "out.png"))[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void HeadingsListsAndLinksAreTagged()
    {
        using Document d = Document.Open(new MemoryStream(Docx("16-headings")), leaveOpen: false);
        using var ms = new MemoryStream();
        DocumentRenderer.WritePdf(d, ms, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library }, Tagged = true, Compress = false });
        string text = Encoding.Latin1.GetString(ms.ToArray());
        Assert.Contains("/H1", text, StringComparison.Ordinal);
        Assert.Contains("/P", text, StringComparison.Ordinal);

        using Document list = Document.Open(new MemoryStream(Docx("17-numbered-list")), leaveOpen: false);
        using var ms2 = new MemoryStream();
        DocumentRenderer.WritePdf(list, ms2, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library }, Compress = false });
        string listText = Encoding.Latin1.GetString(ms2.ToArray());
        Assert.Contains("/LBody", listText, StringComparison.Ordinal);
        Assert.Contains("/Lbl", listText, StringComparison.Ordinal);

        using Document link = Document.Open(new MemoryStream(Docx("30-hyperlink")), leaveOpen: false);
        using var ms3 = new MemoryStream();
        DocumentRenderer.WritePdf(link, ms3, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library }, Compress = false });
        Assert.Contains("https://example.org/", Encoding.Latin1.GetString(ms3.ToArray()), StringComparison.Ordinal);
    }

    private static int CountPages(string pdf) =>
        System.Text.RegularExpressions.Regex.Count(pdf, @"/Type\s*/Page(?![a-zA-Z])");
}
