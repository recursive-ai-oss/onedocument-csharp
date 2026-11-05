using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>
/// Prints workbooks the way Excel does: print areas and titles, page setup and scaling, page breaks, headers and footers, cell text,
/// borders, fills and conditional formats, painted into a display list and written as tagged PDF or PNG. Cached values are printed;
/// nothing is recalculated.
/// </summary>
public static class WorkbookRenderer
{
    /// <summary>Lays out the pages a workbook prints.</summary>
    /// <param name="workbook">The workbook.</param>
    /// <param name="options">Fonts, tagging, hooks and the clock; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between sheets.</param>
    /// <returns>The pages in print order, with notes for what was skipped or approximated. A workbook with nothing to print has no pages.</returns>
    /// <exception cref="OfficeFormatException">A sheet's part could not be read.</exception>
    public static SheetLayoutDocument Layout(Workbook workbook, SheetRenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return SheetLayouter.Layout(workbook, options ?? new SheetRenderOptions(), cancellationToken);
    }

    /// <summary>Paints one page into a display list in CSS pixels (1 pt = 4/3 px), the size of the paper.</summary>
    /// <param name="layout">The layout the page came from.</param>
    /// <param name="page">The page.</param>
    /// <returns>A finished display list.</returns>
    public static DisplayList Paint(SheetLayoutDocument layout, SheetPage page)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(page);
        return SheetPainter.Paint(layout, page);
    }

    /// <summary>Lays a workbook out and writes it as a PDF.</summary>
    /// <param name="workbook">The workbook.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <param name="pdf">Tagging, profile, title and (through <see cref="PdfRenderOptions.Layout"/>) fonts; null for the defaults.</param>
    /// <param name="sheets">Sheet options; null for the defaults. Its fonts and tagging win over <paramref name="pdf"/>'s when set.</param>
    /// <param name="cancellationToken">Cancels between pages.</param>
    /// <returns>The layout the PDF was made from.</returns>
    /// <exception cref="OfficeFormatException">The writer could not express the workbook (code <c>render.failed</c>).</exception>
    public static SheetLayoutDocument WritePdf(Workbook workbook, Stream destination, PdfRenderOptions? pdf = null, SheetRenderOptions? sheets = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(destination);
        PdfRenderOptions opt = pdf ?? new PdfRenderOptions();
        SheetRenderOptions so = sheets ?? new SheetRenderOptions { Tagged = opt.Tagged };
        so.Fonts ??= opt.Layout.Fonts;
        SheetLayoutDocument layout = Layout(workbook, so, cancellationToken);
        WritePdf(layout, destination, opt, cancellationToken);
        return layout;
    }

    /// <summary>Writes an existing layout as a PDF.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <param name="pdf">Tagging, profile and title; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between pages.</param>
    /// <exception cref="OfficeFormatException">The writer could not express the workbook (code <c>render.failed</c>).</exception>
    public static void WritePdf(SheetLayoutDocument layout, Stream destination, PdfRenderOptions? pdf = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(destination);
        PdfRenderOptions opt = pdf ?? new PdfRenderOptions();
        var pdfOptions = new PdfOutputOptions
        {
            Title = opt.Title ?? (layout.Workbook.Sheets.Count > 0 ? layout.Workbook.Sheets[0].Name : "Workbook"),
            Language = "en-US",
            Tagged = opt.Tagged,
            Compress = opt.Compress,
            Profile = opt.Profile,
            Creator = "OneDocument",
            LeaveOpen = true,
        };
        try
        {
            using var writer = new DisplayListPdfWriter(destination, pdfOptions, layout.Context.Images);
            if (layout.Pages.Count == 0)
            {
                // Nothing to print: the PDF still needs a page.
                layout.Context.Note("sheet-layout.empty", "The workbook has nothing to print; the PDF holds one blank page.");
                var blank = new DisplayList();
                blank.FillRect(ItemId.Of(1), new RectF(0, 0, 612 * 96f / 72, 792 * 96f / 72), PaintColor.White);
                blank.Validate();
                writer.AddPage(blank, new PdfPageSetup(612 * 96.0 / 72, 792 * 96.0 / 72), cancellationToken);
            }

            foreach (SheetPage page in layout.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DisplayList list = Paint(layout, page);
                writer.AddPage(list, new PdfPageSetup(page.Width * LayoutPainter.PixelsPerPoint, page.Height * LayoutPainter.PixelsPerPoint), cancellationToken);
            }

            writer.Finish(cancellationToken);
        }
        catch (Exception ex) when (ex is PdfRenderException or DisplayListException or ArgumentException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The workbook could not be written as PDF: " + ex.Message, default, ex);
        }
    }

    internal static bool TryWrite(OfficeFile file, OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        if (file is not Workbook workbook)
        {
            return false;
        }

        if (format == OfficeFormat.Pdf)
        {
            WritePdf(workbook, destination, null, null, cancellationToken);
            return true;
        }

        SheetLayoutDocument sheets = Layout(workbook, null, cancellationToken);
        destination.Write(RenderPng(sheets, sheets.Pages.Count > 0 ? sheets.Pages[0] : null));
        return true;
    }

    /// <summary>Renders one page as a PNG at <see cref="DocumentRenderer.PngDpi"/>.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="page">The page; null renders a blank Letter page (a workbook with nothing to print).</param>
    /// <returns>The PNG bytes.</returns>
    /// <exception cref="OfficeFormatException">The page could not be drawn (code <c>render.failed</c>).</exception>
    public static byte[] RenderPng(SheetLayoutDocument layout, SheetPage? page)
    {
        ArgumentNullException.ThrowIfNull(layout);
        double widthPt = page?.Width ?? 612;
        double heightPt = page?.Height ?? 792;
        DisplayList list;
        if (page is null)
        {
            list = new DisplayList();
            list.FillRect(ItemId.Of(1), new RectF(0, 0, (float)(widthPt * LayoutPainter.PixelsPerPoint), (float)(heightPt * LayoutPainter.PixelsPerPoint)), PaintColor.White);
            list.Validate();
        }
        else
        {
            list = Paint(layout, page);
        }

        double ratio = DocumentRenderer.PngDpi / 96.0;
        int w = (int)Math.Ceiling(widthPt * LayoutPainter.PixelsPerPoint * ratio);
        int h = (int)Math.Ceiling(heightPt * LayoutPainter.PixelsPerPoint * ratio);
        if (w < 1 || h < 1 || (long)w * h > DocumentRenderer.MaxPngPixels)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", $"The page cannot be drawn as a {w} x {h} pixel image.");
        }

        try
        {
            RgbaImage image = DisplayListRasterizer.Render(list, w, h, new RasterOptions { DevicePixelRatio = (float)ratio, Images = layout.Context.Images });
            return image.ToPng();
        }
        catch (Exception ex) when (ex is RasterizationException or DisplayListException or ArgumentException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The page could not be drawn: " + ex.Message, default, ex);
        }
    }
}
