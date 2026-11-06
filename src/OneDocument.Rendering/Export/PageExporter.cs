using System.Buffers.Binary;
using System.Globalization;
using OneImage;
using OneRender.Image;
using OneRender.Painting;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Rendering.Presentations;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Rendering.Svg;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering;

/// <summary>
/// Exports the pages of a document, workbook or presentation as images: PNG, JPEG, TIFF (one file with every page, or one per page),
/// WebP, or SVG. Pages are laid out once, painted into a display list and drawn by OneRender's image backend (or written as SVG);
/// the encoders are OneImage's.
/// </summary>
public static class PageExporter
{
    /// <summary>The most pixels one exported page may have (100 million).</summary>
    public const long MaxPixels = 100_000_000;

    private const double DefaultDpi = 144;
    private const double CssDpi = 96;

    /// <summary>Finds the image format an output path names by its extension.</summary>
    /// <param name="path">A path or file name.</param>
    /// <param name="format">Receives the format.</param>
    /// <returns>False when the extension is not <c>.png</c>, <c>.jpg</c>, <c>.jpeg</c>, <c>.tif</c>, <c>.tiff</c>, <c>.webp</c> or <c>.svg</c>.</returns>
    public static bool TryGetFormat(string path, out PageImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(path);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".png":
                format = PageImageFormat.Png;
                return true;
            case ".jpg" or ".jpeg":
                format = PageImageFormat.Jpeg;
                return true;
            case ".tif" or ".tiff":
                format = PageImageFormat.Tiff;
                return true;
            case ".webp":
                format = PageImageFormat.WebP;
                return true;
            case ".svg":
                format = PageImageFormat.Svg;
                return true;
            default:
                format = default;
                return false;
        }
    }

    /// <summary>Gets the file extension of a format, with the dot.</summary>
    /// <param name="format">The format.</param>
    /// <returns><c>.png</c>, <c>.jpg</c>, <c>.tif</c>, <c>.webp</c> or <c>.svg</c>.</returns>
    public static string Extension(PageImageFormat format) => format switch
    {
        PageImageFormat.Jpeg => ".jpg",
        PageImageFormat.Tiff => ".tif",
        PageImageFormat.WebP => ".webp",
        PageImageFormat.Svg => ".svg",
        _ => ".png",
    };

    /// <summary>Writes the selected pages to one stream.</summary>
    /// <param name="file">A <see cref="Document"/>, <see cref="Workbook"/> or <see cref="Presentation"/>.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <param name="format">The format. A TIFF holds every selected page (all pages by default); the other formats hold one page (the first by default).</param>
    /// <param name="options">Pages, size and encoder settings; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between pages.</param>
    /// <returns>What was written and approximated.</returns>
    /// <exception cref="ArgumentException">The selection names several pages and the format holds one; use <see cref="ExportPages"/>. Or <paramref name="file"/> is not a document, workbook or presentation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The selection names a page the document does not have, or a size or quality is out of range.</exception>
    /// <exception cref="OfficeFormatException">A page could not be drawn or encoded (code <c>render.failed</c>).</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static PageExportReport Export(OfficeFile file, Stream destination, PageImageFormat format, PageExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);
        PageExportOptions opt = Check(options);
        using PageSource source = PageSource.Create(file, opt, format == PageImageFormat.Svg);
        IReadOnlyList<int> pages = (opt.Pages ?? (format == PageImageFormat.Tiff ? PageSelection.All : PageSelection.Page(1))).Resolve(source.Count);
        if (format != PageImageFormat.Tiff && pages.Count != 1)
        {
            throw new ArgumentException("A " + format + " file holds one page, but " + pages.Count.ToString(CultureInfo.InvariantCulture) + " were selected; use ExportPages to write one file per page.", nameof(options));
        }

        if (format != PageImageFormat.Tiff)
        {
            PageImage image = RenderPage(source, pages[0], format, opt);
            destination.Write(image.Data);
            return source.Report(pages);
        }

        var parts = new List<byte[]>();
        foreach (int page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parts.Add(RenderPage(source, page, format, opt).Data);
        }

        destination.Write(parts.Count == 1 ? parts[0] : TiffPages.Join(parts));
        return source.Report(pages);
    }

    /// <summary>Renders the selected pages one at a time, each to its own encoded file.</summary>
    /// <param name="file">A <see cref="Document"/>, <see cref="Workbook"/> or <see cref="Presentation"/>.</param>
    /// <param name="format">The format of every page (a TIFF page is a single-page TIFF).</param>
    /// <param name="sink">Receives each page as it is finished; only one page is held in memory at a time.</param>
    /// <param name="options">Pages (all by default), size and encoder settings; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels between pages.</param>
    /// <returns>What was written and approximated.</returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> is not a document, workbook or presentation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The selection names a page the document does not have, or a size or quality is out of range.</exception>
    /// <exception cref="OfficeFormatException">A page could not be drawn or encoded (code <c>render.failed</c>).</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static PageExportReport ExportPages(OfficeFile file, PageImageFormat format, Action<PageImage> sink, PageExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(sink);
        PageExportOptions opt = Check(options);
        using PageSource source = PageSource.Create(file, opt, format == PageImageFormat.Svg);
        IReadOnlyList<int> pages = (opt.Pages ?? PageSelection.All).Resolve(source.Count);
        foreach (int page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sink(RenderPage(source, page, format, opt));
        }

        return source.Report(pages);
    }

    // What the registered writers do (SaveAs, Office.Convert): a TIFF with every page, anything else the first page.
    internal static void Write(OfficeFile file, OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        PageImageFormat f = format switch
        {
            OfficeFormat.Jpeg => PageImageFormat.Jpeg,
            OfficeFormat.Tiff => PageImageFormat.Tiff,
            OfficeFormat.WebP => PageImageFormat.WebP,
            OfficeFormat.Svg => PageImageFormat.Svg,
            _ => PageImageFormat.Png,
        };
        if (file is not (Document or Workbook or Presentation))
        {
            throw new FormatNotAvailableException(format, "render a " + file.Kind + " as");
        }

        Export(file, destination, f, null, cancellationToken);
    }

    private static PageExportOptions Check(PageExportOptions? options)
    {
        PageExportOptions o = options ?? new PageExportOptions();
        if (o.Dpi is { } dpi && !(dpi >= 1 && dpi <= 2400))
        {
            throw new ArgumentOutOfRangeException(nameof(options), dpi, "Dpi is 1 to 2400.");
        }

        if (o.WidthPixels is < 1 || o.HeightPixels is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "WidthPixels and HeightPixels must be at least 1.");
        }

        if (o.Quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), o.Quality, "Quality is 1 to 100.");
        }

        return o;
    }

    private static PageImage RenderPage(PageSource source, int page, PageImageFormat format, PageExportOptions opt)
    {
        (double wCss, double hCss) = source.Size(page - 1);
        if (format == PageImageFormat.Svg)
        {
            double scale = Scale(wCss, hCss, opt, CssDpi);
            using var ms = new MemoryStream();
            DisplayList list = source.Paint(page - 1);
            var svg = new SvgOptions
            {
                Width = wCss,
                Height = hCss,
                Scale = scale,
                TransparentBackground = opt.TransparentBackground,
                GlyphMode = opt.SvgGlyphMode,
                Images = source.ImageSource,
                Rasterizer = source.Rasterizer,
                IncludeTags = opt.SvgTags,
                Language = source.Language,
                Title = source.Title,
            };
            SvgWriteResult result;
            try
            {
                result = SvgWriter.Write(list, ms, svg);
            }
            catch (Exception ex) when (ex is DisplayListException or ArgumentException)
            {
                throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The page could not be written as SVG: " + ex.Message, default, ex);
            }

            foreach (SvgNote n in result.Notes)
            {
                source.AddNote(n.Code, n.Message);
            }

            return new PageImage(page, ms.ToArray(), (int)Math.Round(wCss * scale), (int)Math.Round(hCss * scale));
        }

        double ratio = Scale(wCss, hCss, opt, DefaultDpi);
        int w = (int)Math.Ceiling(wCss * ratio), h = (int)Math.Ceiling(hCss * ratio);
        if (w < 1 || h < 1 || (long)w * h > MaxPixels)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", $"The page cannot be drawn as a {w} x {h} pixel image.");
        }

        try
        {
            bool alpha = opt.TransparentBackground && format != PageImageFormat.Jpeg;
            RgbaImage image = DisplayListRasterizer.Render(
                source.Paint(page - 1), w, h,
                new RasterOptions { DevicePixelRatio = (float)ratio, Images = source.Images, Background = alpha ? PaintColor.Transparent : PaintColor.White });
            return new PageImage(page, Encode(image, format, opt), w, h);
        }
        catch (Exception ex) when (ex is RasterizationException or DisplayListException or ArgumentException or ImageException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The page could not be drawn: " + ex.Message, default, ex);
        }
    }

    // Output pixels per CSS pixel: from the pixel size when one is set (fitting inside both when both are), else from the dpi.
    private static double Scale(double wCss, double hCss, PageExportOptions opt, double defaultDpi)
    {
        if (opt.WidthPixels is { } w && opt.HeightPixels is { } h)
        {
            return Math.Min(w / wCss, h / hCss);
        }

        if (opt.WidthPixels is { } w1)
        {
            return w1 / wCss;
        }

        if (opt.HeightPixels is { } h1)
        {
            return h1 / hCss;
        }

        return (opt.Dpi ?? defaultDpi) / CssDpi;
    }

    private static byte[] Encode(RgbaImage image, PageImageFormat format, PageExportOptions opt)
    {
        if (format == PageImageFormat.Png)
        {
            return image.ToPng();
        }

        int n = image.Width * image.Height;
        var pixels = new Rgba32[n];
        byte[] src = image.Pixels;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            pixels[i] = new Rgba32(src[o], src[o + 1], src[o + 2], src[o + 3]);
        }

        Image img = Image.FromPixels<Rgba32>(pixels, image.Width, image.Height);
        using var ms = new MemoryStream();
        switch (format)
        {
            case PageImageFormat.Jpeg:
                // Text needs full-resolution colour: the default 4:2:0 fringes every glyph edge.
                img.Save(ms, JpegFormat.Instance, new SaveOptions { EncoderOptions = new JpegEncoderOptions { Quality = opt.Quality, Subsampling = JpegSubsampling.Yuv444 } });
                break;
            case PageImageFormat.WebP:
                img.Save(ms, WebPFormat.Instance, new SaveOptions { EncoderOptions = new WebPEncoderOptions { Lossless = opt.WebPLossless, Quality = opt.Quality } });
                break;
            default:
                img.Save(ms, TiffFormat.Instance, new SaveOptions { EncoderOptions = new TiffEncoderOptions { Compression = opt.TiffCompression } });
                break;
        }

        return ms.ToArray();
    }

    // ---- the pages of each kind

    private abstract class PageSource : IDisposable
    {
        private readonly List<string> _notes = [];
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private ISvgImageSource? _imageSource;
        private ISvgRasterizer? _rasterizer;

        public abstract int Count { get; }

        public DrawingImages Images { get; protected init; } = new();

        public string Language { get; protected init; } = "en-US";

        public string? Title { get; protected init; }

        public ISvgImageSource ImageSource => _imageSource ??= new ImageAdapter(Images);

        public ISvgRasterizer Rasterizer => _rasterizer ??= new RasterAdapter(Images);

        // The page size in CSS pixels.
        public abstract (double Width, double Height) Size(int index);

        public abstract DisplayList Paint(int index);

        public void AddNote(string code, string message)
        {
            if (_seen.Add(code))
            {
                _notes.Add(code + ": " + message);
            }
        }

        public PageExportReport Report(IReadOnlyList<int> exported)
        {
            CollectNotes();
            return new PageExportReport(Count, exported, _notes);
        }

        protected virtual void CollectNotes()
        {
        }

        public void Dispose()
        {
        }

        public static PageSource Create(OfficeFile file, PageExportOptions opt, bool tagged) => file switch
        {
            Document d => new DocumentPages(d, opt, tagged),
            Workbook w => new WorkbookPages(w, opt, tagged),
            Presentation p => new PresentationPages(p, opt, tagged),
            _ => throw new ArgumentException("Only documents, workbooks and presentations have pages; this file is a " + file.Kind + ".", nameof(file)),
        };
    }

    private sealed class DocumentPages : PageSource
    {
        private readonly LayoutDocument _layout;
        private readonly PaintOptions _paint;

        public DocumentPages(Document document, PageExportOptions opt, bool tagged)
        {
            Language = DocumentRenderer.DefaultLanguage(document);
            Title = document.Properties.Core.Title;
            _layout = LayoutEngine.Layout(document, opt.Layout, CancellationToken.None);
            _paint = new PaintOptions { Tagged = tagged, Language = Language, Images = Images, DrawingNotes = DrawingNotes };
        }

        private List<DrawingNote> DrawingNotes { get; } = [];

        public override int Count => _layout.Pages.Count;

        public override (double Width, double Height) Size(int index) =>
            (_layout.Pages[index].Width * LayoutPainter.PixelsPerPoint, _layout.Pages[index].Height * LayoutPainter.PixelsPerPoint);

        public override DisplayList Paint(int index) => LayoutPainter.Paint(_layout.Pages[index], _paint);

        protected override void CollectNotes()
        {
            foreach (FontSubstitutionNote n in _layout.FontNotes)
            {
                AddNote("font-substitution", n.Message);
            }

            foreach (LayoutNote n in _layout.Notes)
            {
                AddNote(n.Code, n.Message);
            }

            foreach (DrawingNote n in DrawingNotes)
            {
                AddNote(n.Code, n.Message);
            }
        }
    }

    private sealed class WorkbookPages : PageSource
    {
        private readonly SheetLayoutDocument _layout;

        public WorkbookPages(Workbook workbook, PageExportOptions opt, bool tagged)
        {
            _layout = WorkbookRenderer.Layout(workbook, new SheetRenderOptions { Fonts = opt.Layout.Fonts, Tagged = tagged });
            Images = _layout.Context.Images;
            Title = workbook.Sheets.Count > 0 ? workbook.Sheets[0].Name : null;
        }

        // A workbook with nothing to print exports one blank Letter page, as the PNG always did.
        public override int Count => Math.Max(1, _layout.Pages.Count);

        public override (double Width, double Height) Size(int index) => _layout.Pages.Count == 0
            ? (612 * LayoutPainter.PixelsPerPoint, 792 * LayoutPainter.PixelsPerPoint)
            : (_layout.Pages[index].Width * LayoutPainter.PixelsPerPoint, _layout.Pages[index].Height * LayoutPainter.PixelsPerPoint);

        public override DisplayList Paint(int index)
        {
            if (_layout.Pages.Count > 0)
            {
                return WorkbookRenderer.Paint(_layout, _layout.Pages[index]);
            }

            var blank = new DisplayList();
            blank.FillRect(ItemId.Of(1), new RectF(0, 0, (float)(612 * LayoutPainter.PixelsPerPoint), (float)(792 * LayoutPainter.PixelsPerPoint)), PaintColor.White);
            blank.Validate();
            return blank;
        }

        protected override void CollectNotes()
        {
            foreach (FontSubstitutionNote n in _layout.FontNotes)
            {
                AddNote("font-substitution", n.Message);
            }

            foreach (SheetRenderNote n in _layout.Notes)
            {
                AddNote(n.Code, n.Message);
            }
        }
    }

    private sealed class PresentationPages : PageSource
    {
        private const double K = 96.0 / 72.0;
        private readonly RenderSession _session;
        private readonly List<PageSpec> _specs;

        public PresentationPages(Presentation presentation, PageExportOptions opt, bool tagged)
        {
            _session = new RenderSession(presentation, new PresentationRenderOptions { Fonts = opt.Layout.Fonts, Tagged = tagged });
            _specs = _session.Enumerate(out _);
            Images = _session.Images;
            Title = _session.Slides.Count > 0 ? _session.Slides[0].Title : null;
        }

        public override int Count => _specs.Count;

        public override (double Width, double Height) Size(int index)
        {
            (double w, double h) = _session.PageSize(_specs[index]);
            return (w * K, h * K);
        }

        public override DisplayList Paint(int index) => _session.PaintPage(_specs[index]);

        protected override void CollectNotes()
        {
            foreach (FontSubstitutionNote n in _session.FontNotes)
            {
                AddNote("font-substitution", n.Message);
            }

            foreach (DrawingNote n in _session.Notes)
            {
                AddNote(n.Code, n.Message);
            }
        }
    }

    // ---- SVG adapters over the shared picture set

    private sealed class ImageAdapter(DrawingImages images) : ISvgImageSource
    {
        private readonly Dictionary<int, SvgImageData?> _cache = [];

        public bool TryGetImage(ImageHandle handle, out SvgImageData data)
        {
            if (!_cache.TryGetValue(handle.Value, out SvgImageData? d))
            {
                RgbaImage? px = images.Resolve(handle);
                d = px is null ? null : new SvgImageData(px.ToPng(), "image/png", px.Width, px.Height);
                _cache[handle.Value] = d;
            }

            data = d.GetValueOrDefault();
            return d is not null;
        }
    }

    private sealed class RasterAdapter(DrawingImages images) : ISvgRasterizer
    {
        public byte[]? RasterizePng(DisplayList list, int widthPixels, int heightPixels, float pixelRatio)
        {
            try
            {
                return DisplayListRasterizer.Render(list, widthPixels, heightPixels, new RasterOptions { DevicePixelRatio = pixelRatio, Images = images, Background = PaintColor.Transparent }).ToPng();
            }
            catch (Exception ex) when (ex is RasterizationException or DisplayListException or ArgumentException)
            {
                return null;
            }
        }
    }
}

/// <summary>Joins single-page TIFF files into one multi-page file by chaining their image file directories.</summary>
/// <remarks>
/// OneImage writes one image per call, so each page is encoded alone and the files are joined here: the bytes after the 8-byte header are
/// copied unchanged to their new place and the offsets inside them (directory entries that point to data, strip offsets, the link to the
/// next directory) are moved by the distance. Only the plain tags OneImage's encoder writes are accepted; anything that holds further
/// offsets (sub-directories, JPEG-in-TIFF) is refused, so a changed encoder fails loudly rather than corrupting a file.
/// </remarks>
internal static class TiffPages
{
    public static byte[] Join(IReadOnlyList<byte[]> pages)
    {
        bool little = pages[0].Length >= 8 && pages[0][0] == (byte)'I';
        var output = new List<byte>(pages.Sum(p => p.Length) + 16);
        output.AddRange(pages[0].AsSpan(0, 8).ToArray());
        var ifdAt = new List<int>(); // where each page's directory lands in the output
        foreach (byte[] page in pages)
        {
            if (page.Length < 16 || !((page[0] == (byte)'I' && page[1] == (byte)'I') || (page[0] == (byte)'M' && page[1] == (byte)'M')) || (page[0] == (byte)'I') != little)
            {
                throw Refused("a page is not a classic TIFF of the same byte order");
            }

            if (output.Count % 2 != 0)
            {
                output.Add(0);
            }

            int shift = output.Count - 8;
            int ifd = (int)U32(page, 4, little);
            if (U16(page, 2, little) != 42 || ifd < 8 || ifd + 2 > page.Length)
            {
                throw Refused("a page has no valid directory");
            }

            int count = U16(page, ifd, little);
            int entriesEnd = ifd + 2 + (count * 12);
            if (entriesEnd + 4 > page.Length)
            {
                throw Refused("a directory runs past the end of the file");
            }

            // Work on a copy of the page with offsets moved, then append its body.
            byte[] moved = (byte[])page.Clone();
            for (int e = 0; e < count; e++)
            {
                int at = ifd + 2 + (e * 12);
                int t = U16(moved, at, little), type = U16(moved, at + 2, little);
                long n = U32(moved, at + 4, little);
                int size = TypeSize(type);
                if (t is 330 or 34665 or 34853 or 40965 or 513 or 514 or 519 or 520 or 521 || size == 0)
                {
                    throw Refused("tag " + t.ToString(CultureInfo.InvariantCulture) + " holds offsets this joiner does not move");
                }

                long bytes = size * n;
                if (bytes > 4)
                {
                    long off = U32(moved, at + 8, little);
                    if (off < 8 || off + bytes > page.Length)
                    {
                        throw Refused("a value lies outside the file");
                    }

                    if (t is 273 or 324)
                    {
                        if (type != 4)
                        {
                            throw Refused("strip offsets are not LONG");
                        }

                        for (long k = 0; k < n; k++)
                        {
                            int p = (int)(off + (k * 4));
                            Put32(moved, p, U32(moved, p, little) + (uint)shift, little);
                        }
                    }

                    Put32(moved, at + 8, (uint)(off + shift), little);
                }
                else if (t is 273 or 324)
                {
                    if (type != 4)
                    {
                        throw Refused("strip offsets are not LONG");
                    }

                    Put32(moved, at + 8, U32(moved, at + 8, little) + (uint)shift, little);
                }
            }

            ifdAt.Add(ifd + shift);
            for (int i = 8; i < moved.Length; i++)
            {
                output.Add(moved[i]);
            }

        }

        byte[] result = [.. output];
        Put32(result, 4, (uint)ifdAt[0], little);
        for (int k = 0; k < pages.Count; k++)
        {
            int ifd = ifdAt[k];
            int count = U16(result, ifd, little);
            Put32(result, ifd + 2 + (count * 12), k + 1 < pages.Count ? (uint)ifdAt[k + 1] : 0u, little);
        }

        return result;
    }

    private static int TypeSize(int type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 or 16 or 17 or 18 => 8,
        _ => 0,
    };

    private static OfficeFormatException Refused(string why) =>
        new(DefectKind.Structure, "render.failed", "The pages could not be joined into one TIFF: " + why + ".");

    private static int U16(byte[] b, int at, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at));

    private static uint U32(byte[] b, int at, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at));

    private static void Put32(byte[] b, int at, uint v, bool little)
    {
        if (little)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), v);
        }
    }
}
