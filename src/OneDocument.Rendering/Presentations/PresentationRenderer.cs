using System.Globalization;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Presentations;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

/// <summary>Renders presentations to PDF and PNG.</summary>
/// <remarks>
/// Slides are composed from the presentation's masters, layouts and slides with their placeholders, backgrounds, shapes, tables, charts
/// and pictures, and painted into OneRender's display list; text is laid out by OneDocument's own text layout. See
/// <c>docs/presentation-rendering.md</c> for the rules and what is approximated. Rendering reads the presentation and never changes it.
/// </remarks>
public static class PresentationRenderer
{
    /// <summary>The resolution of PNG pages, in dots per inch.</summary>
    public const int PngDpi = 144;

    /// <summary>The most pixels <see cref="RenderPng"/> draws for one page (100 million).</summary>
    public const long MaxPngPixels = 100_000_000;

    /// <summary>Renders a presentation as a PDF with one page per slide (and notes pages or handouts when asked).</summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <param name="options">What to render and how; null for the defaults (visible slides only, tagged).</param>
    /// <param name="cancellationToken">Cancels between pages.</param>
    /// <returns>The pages written and what was approximated or left out.</returns>
    /// <exception cref="OfficeFormatException">The writer could not express the presentation (code <c>render.failed</c>).</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static PresentationRenderReport WritePdf(Presentation presentation, Stream destination, PresentationRenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(destination);
        PresentationRenderOptions opt = options ?? new PresentationRenderOptions();
        var session = new RenderSession(presentation, opt);
        List<PageSpec> specs = session.Enumerate(out int hidden);
        var pages = new List<RenderedPage>();
        string language = DefaultLanguage(presentation);
        string? firstTitle = null;
        foreach (Slide s in session.Slides)
        {
            if (s.Title is { Length: > 0 } t)
            {
                firstTitle = t;
                break;
            }
        }

        var pdfOptions = new PdfOutputOptions
        {
            Title = opt.Title ?? firstTitle ?? "Presentation",
            Language = language,
            Tagged = opt.Tagged,
            Compress = opt.Compress,
            Profile = opt.Profile,
            Creator = "OneDocument",
            LeaveOpen = true,
        };
        try
        {
            using var writer = new DisplayListPdfWriter(destination, pdfOptions, session.Images);
            foreach (PageSpec spec in specs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (double w, double h) = session.PageSize(spec);
                DisplayList list = session.PaintPage(spec);
                writer.AddPage(list, new PdfPageSetup(w * RenderSessionScale, h * RenderSessionScale), cancellationToken);
                pages.Add(new RenderedPage(pages.Count + 1, spec.Kind, spec.SlideIndex, w, h));
            }

            if (specs.Count == 0)
            {
                // A PDF needs a page; a presentation with nothing to show gets one blank slide-sized page.
                (double w, double h) = session.SlideSize();
                writer.AddPage(new DisplayList(), new PdfPageSetup(w * RenderSessionScale, h * RenderSessionScale), cancellationToken);
                session.AddNote("slide.no-pages", "There was no slide to render; the PDF has one blank page.");
            }

            writer.Finish(cancellationToken);
        }
        catch (Exception ex) when (ex is PdfRenderException or DisplayListException or ArgumentException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The presentation could not be written as PDF: " + ex.Message, default, ex);
        }

        return new PresentationRenderReport(pages, hidden, session.FontNotes, session.Notes);
    }

    internal static bool TryWrite(OfficeFile file, OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        if (file is not Presentation presentation)
        {
            return false;
        }

        if (format == OfficeFormat.Pdf)
        {
            WritePdf(presentation, destination, null, cancellationToken);
        }
        else
        {
            destination.Write(RenderPng(presentation));
        }

        return true;
    }

    private const double RenderSessionScale = 96.0 / 72.0;

    /// <summary>Renders one page of a presentation as a PNG at <see cref="PngDpi"/>.</summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="pageIndex">The zero-based page among the pages <paramref name="options"/> selects (by default, the visible slides).</param>
    /// <param name="options">What to render; null for the defaults.</param>
    /// <returns>The PNG bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page.</exception>
    /// <exception cref="OfficeFormatException">The page could not be drawn (code <c>render.failed</c>).</exception>
    public static byte[] RenderPng(Presentation presentation, int pageIndex = 0, PresentationRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        PresentationRenderOptions opt = options ?? new PresentationRenderOptions();
        var untagged = new PresentationRenderOptions
        {
            Output = opt.Output, IncludeHiddenSlides = opt.IncludeHiddenSlides, Tagged = false, Fonts = opt.Fonts, Clock = opt.Clock,
            Autofit = opt.Autofit, HandoutSlidesPerPage = opt.HandoutSlidesPerPage,
        };
        var session = new RenderSession(presentation, untagged);
        List<PageSpec> specs = session.Enumerate(out _);
        if (pageIndex < 0 || pageIndex >= specs.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "The presentation has " + specs.Count.ToString(CultureInfo.InvariantCulture) + " pages to render.");
        }

        PageSpec spec = specs[pageIndex];
        (double pw, double ph) = session.PageSize(spec);
        DisplayList list = session.PaintPage(spec);
        double ratio = PngDpi / 96.0;
        int w = (int)Math.Ceiling(pw * RenderSessionScale * ratio);
        int h = (int)Math.Ceiling(ph * RenderSessionScale * ratio);
        if (w < 1 || h < 1 || (long)w * h > MaxPngPixels)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", $"The page cannot be drawn as a {w} x {h} pixel image.");
        }

        try
        {
            RgbaImage image = DisplayListRasterizer.Render(list, w, h, new RasterOptions { DevicePixelRatio = (float)ratio, Images = session.Images });
            return image.ToPng();
        }
        catch (Exception ex) when (ex is RasterizationException or DisplayListException or ArgumentException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The page could not be drawn: " + ex.Message, default, ex);
        }
    }

    private static string DefaultLanguage(Presentation presentation)
    {
        Schema.MarkupRef style = presentation.DefaultTextStyle;
        if (style.IsValid)
        {
            foreach (Markup.MarkupNode lvl in style.Node.ChildElements)
            {
                Markup.MarkupNode def = Drawing.DrawingTheme.Child(lvl, "defRPr");
                if (!def.IsNull && def.GetAttribute("lang") is { Length: > 0 } lang)
                {
                    return lang;
                }
            }
        }

        return "en-US";
    }
}
