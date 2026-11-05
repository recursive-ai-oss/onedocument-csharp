using OneRender.Image;
using OneRender.Painting;

namespace RecursiveAI.OneDocument.Rendering.Metafiles;

/// <summary>
/// The bitmaps a set of display lists refers to. A display list names images by handle only, so everything that paints a bitmap
/// (a metafile with a <c>StretchDIBits</c> record, a rasterised fallback) adds its pixels here and the output backend asks this table
/// for them: pass it to <c>DisplayListPdfWriter</c> or <c>RasterOptions.Images</c> as the image resolver.
/// </summary>
/// <remarks>
/// One table serves one rendering job and may be shared by all of its pages. Adding is not thread-safe; resolving after painting is.
/// </remarks>
public sealed class PictureImages : IImageResolver
{
    private readonly List<RgbaImage> _images = [];
    private readonly ImageStore? _shared;

    /// <summary>Creates an empty table of its own.</summary>
    public PictureImages()
    {
    }

    // A table that shares the handles of a drawing's image store, so one resolver serves both the pictures and the metafile bitmaps.
    internal PictureImages(ImageStore shared) => _shared = shared;

    /// <summary>Gets the number of images held.</summary>
    public int Count => _shared is null ? _images.Count : _count;

    private int _count;

    /// <summary>Adds an image and returns the handle display-list items use for it.</summary>
    /// <param name="image">The pixels, RGBA with straight alpha.</param>
    /// <returns>The handle. Handles are never reused within one table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    public ImageHandle Add(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (_shared is not null)
        {
            string key = "metafile-bitmap:" + (++_count).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _shared.Add(key, image);
            return _shared.TryGetImage(key, out ImageInfo info) ? info.Handle : default;
        }

        _images.Add(image);
        return new ImageHandle(_images.Count);
    }

    /// <summary>Returns the image for a handle made by <see cref="Add"/>.</summary>
    /// <param name="handle">The handle a display-list item carries.</param>
    /// <returns>The pixels, or null when the handle is not from this table.</returns>
    public RgbaImage? Resolve(ImageHandle handle) =>
        _shared is not null ? _shared.Resolve(handle) : handle.Value >= 1 && handle.Value <= _images.Count ? _images[handle.Value - 1] : null;
}
