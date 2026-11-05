using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using OnePdf.Text;

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
        Assert.Equal(layout.Pages.Count, CountPages(pdf));
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
        HashSet<string> headings = StructureTypes("16-headings");
        Assert.Contains("H1", headings);
        Assert.Contains("P", headings);

        HashSet<string> list = StructureTypes("17-numbered-list");
        Assert.Contains("LBody", list);
        Assert.Contains("Lbl", list);

        using Document link = Document.Open(new MemoryStream(Docx("30-hyperlink")), leaveOpen: false);
        using var ms3 = new MemoryStream();
        DocumentRenderer.WritePdf(link, ms3, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library }, Compress = false });
        Assert.Contains("Link", StructureTypes("30-hyperlink"));
    }

    // Structure elements sit in compressed object streams, so they are read back through a PDF reader.
    private static HashSet<string> StructureTypes(string name)
    {
        using Document d = Document.Open(new MemoryStream(Docx(name)), leaveOpen: false);
        using var ms = new MemoryStream();
        DocumentRenderer.WritePdf(d, ms, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library } });
        using var pdf = OnePdf.PdfDocument.Open(ms.ToArray());
        OnePdf.PdfStructureTree tree = pdf.StructureTree ?? throw new InvalidOperationException("no structure tree");
        return [.. tree.Elements().Select(e => e.Type)];
    }

    [Fact]
    public void CapitalisedRunsExtractAsTheCapitalsTheyShow()
    {
        using Document d = Document.Open(new MemoryStream(Docx("12-caps-smallcaps")), leaveOpen: false);
        using var ms = new MemoryStream();
        DocumentRenderer.WritePdf(d, ms, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library } });
        using var pdf = OnePdf.PdfDocument.Open(ms.ToArray());
        string text = string.Join(" ", pdf.ExtractText().SelectMany(p => p.Blocks).SelectMany(b => b.Lines).Select(l => l.Text));
        Assert.Contains("ALL CAPS TEXT", text, StringComparison.Ordinal);
        Assert.Contains("SMALL CAPS TEXT", text, StringComparison.Ordinal);
        Assert.Contains("normal", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABadPageSizeIsReportedAsRenderFailed()
    {
        // A page far past the pixel ceiling cannot be rasterized; the failure must leave as the typed exception, not an ArgumentException.
        string section = "<w:sectPr><w:pgSz w:w=\"400000\" w:h=\"400000\"/><w:pgMar w:top=\"0\" w:right=\"0\" w:bottom=\"0\" w:left=\"0\" w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/></w:sectPr>";
        byte[] docx = TestDocx.Bytes(TestDocx.P("x"), null, null, null, section);
        using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(d, new LayoutOptions { Fonts = TestFonts.Library });
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => DocumentRenderer.RenderPng(layout.Pages[0]));
        Assert.Equal("render.failed", ex.Defect.Code);
    }

    // Page objects sit in compressed object streams, so the count comes from a PDF reader, not a text scan.
    private static int CountPages(byte[] pdf)
    {
        using var document = OnePdf.PdfDocument.Open(pdf);
        return document.PageCount;
    }
}
