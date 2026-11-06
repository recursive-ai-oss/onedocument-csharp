using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Rendering;

/// <summary>Options for writing a PDF.</summary>
public sealed class PdfRenderOptions
{
    /// <summary>Gets or sets whether the PDF is tagged (a structure tree with headings, lists, links). Default true.</summary>
    public bool Tagged { get; set; } = true;

    /// <summary>Gets or sets whether page content is compressed. Default true; turn it off to read the PDF as text.</summary>
    public bool Compress { get; set; } = true;

    /// <summary>Gets or sets the PDF profile: none, PDF/UA-1, PDF/A-2a, or both. Default none. A profile needs <see cref="Tagged"/>.</summary>
    public PdfProfile Profile { get; set; }

    /// <summary>Gets or sets the PDF title; null takes the document's title property, or the first heading, or "Document".</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets layout options (fonts and limits).</summary>
    public LayoutOptions Layout { get; set; } = new();
}

/// <summary>Writes laid-out documents as PDF and PNG.</summary>
public static class DocumentRenderer
{
    /// <summary>The resolution of PNG pages, in dots per inch.</summary>
    public const int PngDpi = 144;

    /// <summary>Lays a document out and writes it as a PDF.</summary>
    /// <param name="document">The document.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <param name="options">Tagging, profile, title and layout options; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between pages.</param>
    /// <returns>The layout the PDF was made from, with its font substitution notes.</returns>
    /// <exception cref="OfficeFormatException">The writer could not express the document (code <c>render.failed</c>).</exception>
    public static LayoutDocument WritePdf(Document document, Stream destination, PdfRenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        PdfRenderOptions opt = options ?? new PdfRenderOptions();
        LayoutDocument layout = LayoutEngine.Layout(document, opt.Layout, cancellationToken);
        WritePdf(layout, document, destination, opt, cancellationToken);
        return layout;
    }

    /// <summary>Writes an existing layout as a PDF.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="document">The document the layout came from (for its title and language).</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <param name="options">Tagging, profile and title; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between pages.</param>
    /// <exception cref="OfficeFormatException">The writer could not express the document (code <c>render.failed</c>).</exception>
    public static void WritePdf(LayoutDocument layout, Document document, Stream destination, PdfRenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        PdfRenderOptions opt = options ?? new PdfRenderOptions();
        string language = DefaultLanguage(document);
        var images = new Drawing.DrawingImages();
        var paint = new PaintOptions { Tagged = opt.Tagged, Language = language, Images = images };
        var pdfOptions = new PdfOutputOptions
        {
            Title = opt.Title ?? document.Properties.Core.Title ?? FirstHeading(layout) ?? "Document",
            Language = language,
            Tagged = opt.Tagged,
            Compress = opt.Compress,
            Profile = opt.Profile,
            Creator = "OneDocument",
            LeaveOpen = true,
        };
        try
        {
            using var writer = new DisplayListPdfWriter(destination, pdfOptions, images);
            foreach (LayoutPage page in layout.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DisplayList list = LayoutPainter.Paint(page, paint);
                var setup = new PdfPageSetup(page.Width * LayoutPainter.PixelsPerPoint, page.Height * LayoutPainter.PixelsPerPoint);
                writer.AddPage(list, setup, cancellationToken);
            }

            writer.Finish(cancellationToken);
        }
        catch (Exception ex) when (ex is PdfRenderException or DisplayListException or ArgumentException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The document could not be written as PDF: " + ex.Message, default, ex);
        }
    }

    /// <summary>The most pixels <see cref="RenderPng"/> draws for one page (100 million, about 400 MB of pixels).</summary>
    public const long MaxPngPixels = 100_000_000;

    /// <summary>Renders one page as a PNG at <see cref="PngDpi"/>.</summary>
    /// <param name="page">The page.</param>
    /// <param name="language">The document's default language, for tagging (unused in the image).</param>
    /// <returns>The PNG bytes.</returns>
    /// <exception cref="OfficeFormatException">The page could not be drawn (code <c>render.failed</c>).</exception>
    public static byte[] RenderPng(LayoutPage page, string language = "en-US")
    {
        ArgumentNullException.ThrowIfNull(page);
        var images = new Drawing.DrawingImages();
        DisplayList list = LayoutPainter.Paint(page, new PaintOptions { Tagged = false, Language = language, Images = images });
        double ratio = PngDpi / 96.0;
        int w = (int)Math.Ceiling(page.Width * LayoutPainter.PixelsPerPoint * ratio);
        int h = (int)Math.Ceiling(page.Height * LayoutPainter.PixelsPerPoint * ratio);
        // The page size comes from the document, so an absurd one must not become an absurd allocation.
        if (w < 1 || h < 1 || (long)w * h > MaxPngPixels)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", $"The page cannot be drawn as a {w} x {h} pixel image.");
        }

        try
        {
            RgbaImage image = DisplayListRasterizer.Render(list, w, h, new RasterOptions { DevicePixelRatio = (float)ratio, Images = images });
            return image.ToPng();
        }
        catch (Exception ex) when (ex is RasterizationException or DisplayListException or ArgumentException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The page could not be drawn: " + ex.Message, default, ex);
        }
    }

    internal static bool TryWrite(OfficeFile file, OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        if (file is not Document document)
        {
            return false;
        }

        if (format == OfficeFormat.Pdf)
        {
            WritePdf(document, destination, null, cancellationToken);
            return true;
        }

        return false;
    }

    internal static string DefaultLanguage(Document document)
    {
        if (document.Styles.DefaultRunProperties is { } rpr && Xml.Child(rpr, "lang") is { } lang && Xml.Attr(lang, "val") is { Length: > 0 } v)
        {
            return v;
        }

        return "en-US";
    }

    private static string? FirstHeading(LayoutDocument layout)
    {
        foreach (LayoutPage page in layout.Pages)
        {
            foreach (LayoutParagraph p in page.Paragraphs)
            {
                if (p.HeadingLevel > 0 && p.Text.Trim().Length > 0)
                {
                    return p.Text.Trim();
                }
            }
        }

        return null;
    }
}

/// <summary>Registers PDF and PNG output for word-processing documents and workbooks with a format registry.</summary>
public static class RenderingFormats
{
    /// <summary>
    /// What one kind of file does to write itself as PDF or PNG. Returns false, writing nothing, when the file is not of its kind.
    /// </summary>
    internal delegate bool KindWriter(OfficeFile file, OfficeFormat format, Stream destination, CancellationToken cancellationToken);

    // The kinds that render, tried in order. A new kind adds its own `TryWrite` here and nothing else: the registry keeps one writer per
    // format, so every kind goes through the single handler below.
    private static readonly KindWriter[] Kinds =
    [
        DocumentRenderer.TryWrite,
        WorkbookRenderer.TryWrite,
        Presentations.PresentationRenderer.TryWrite,
    ];

    /// <summary>Gets the output formats this package writes.</summary>
    public static IReadOnlyList<OfficeFormat> Formats { get; } =
        [OfficeFormat.Pdf, OfficeFormat.Png, OfficeFormat.Jpeg, OfficeFormat.Tiff, OfficeFormat.WebP, OfficeFormat.Svg];

    /// <summary>
    /// Registers PDF and page-image writers (PNG, JPEG, TIFF, WebP, SVG), so <c>Office.Convert("in.docx", "out.pdf")</c> and
    /// <c>document.SaveAs("out.png")</c> work, for documents, workbooks and presentations. An image holds the first page and a TIFF every page;
    /// <see cref="PageExporter"/> chooses pages, size and encoder settings. Calling it again is harmless.
    /// </summary>
    /// <param name="registry">The registry to fill.</param>
    public static void Register(FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterWriter(
            static (file, format, destination, cancellationToken) =>
            {
                if (format != OfficeFormat.Pdf)
                {
                    PageExporter.Write(file, format, destination, cancellationToken);
                    return;
                }

                foreach (KindWriter kind in Kinds)
                {
                    if (kind(file, format, destination, cancellationToken))
                    {
                        return;
                    }
                }

                throw new FormatNotAvailableException(format, "render a " + file.Kind + " as");
            },
            [.. Formats]);
    }
}
