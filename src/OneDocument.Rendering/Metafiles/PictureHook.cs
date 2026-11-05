using OneRender.Painting;

namespace RecursiveAI.OneDocument.Rendering.Metafiles;

/// <summary>
/// The one call a picture painter makes for picture bytes it may not be able to draw itself. Today it handles metafiles; the caller
/// draws the picture's cached bitmap, or a placeholder, when it returns false.
/// </summary>
public static class PictureHook
{
    /// <summary>Tells whether picture bytes are a metafile, by media type or by content.</summary>
    /// <param name="data">The picture bytes.</param>
    /// <param name="contentType">The part's content type or file extension, or null when unknown. The bytes decide when it is null or not a metafile type.</param>
    /// <returns>True for EMF and WMF.</returns>
    public static bool IsVectorPicture(ReadOnlySpan<byte> data, string? contentType = null) =>
        MetafilePainter.IsMetafileType(contentType) || MetafilePainter.IsMetafile(data);

    /// <summary>Reads the natural size of a picture the hook can draw.</summary>
    /// <param name="data">The picture bytes.</param>
    /// <param name="widthPoints">Natural width in points.</param>
    /// <param name="heightPoints">Natural height in points.</param>
    /// <returns>False when the bytes are not a metafile with a usable frame.</returns>
    public static bool TryGetNaturalSize(ReadOnlyMemory<byte> data, out double widthPoints, out double heightPoints) =>
        MetafilePainter.TryGetNaturalSize(data, out widthPoints, out heightPoints);

    /// <summary>Draws a picture as vector content when it is a metafile.</summary>
    /// <param name="data">The picture bytes.</param>
    /// <param name="contentType">The part's content type or extension, or null.</param>
    /// <param name="context">The list, bitmap table, fonts and reporting.</param>
    /// <param name="destination">The picture's box in CSS pixels.</param>
    /// <param name="crop">The picture's source rectangle (<c>a:srcRect</c>); default shows all of it.</param>
    /// <param name="cancellationToken">Cancels interpretation of a large file.</param>
    /// <returns>True when the picture was drawn. False when it is not a metafile, or one that cannot be drawn (a defect says why); nothing was added to the list.</returns>
    public static bool TryPaint(ReadOnlyMemory<byte> data, string? contentType, MetafilePaintContext context, in RectF destination, PictureCrop crop = default, CancellationToken cancellationToken = default) =>
        IsVectorPicture(data.Span, contentType) && MetafilePainter.TryPaint(data, context, destination, crop, cancellationToken);
}
