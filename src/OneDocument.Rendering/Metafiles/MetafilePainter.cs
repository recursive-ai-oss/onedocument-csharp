using System.Buffers.Binary;
using System.Numerics;
using OneImage.Metafile;
using OneRender.Painting;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Metafiles;

/// <summary>What a metafile painter needs from the page being painted.</summary>
public sealed class MetafilePaintContext
{
    /// <summary>Initializes a context.</summary>
    /// <param name="list">The display list to paint into, in CSS pixels.</param>
    /// <param name="images">The bitmap table the output backend resolves image handles from.</param>
    /// <param name="nextId">Gives each display-list item a fresh id.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MetafilePaintContext(DisplayList list, PictureImages images, Func<ItemId> nextId)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(nextId);
        List = list;
        Images = images;
        NextId = nextId;
    }

    /// <summary>Gets the display list painted into.</summary>
    public DisplayList List { get; }

    /// <summary>Gets the bitmap table.</summary>
    public PictureImages Images { get; }

    /// <summary>Gets the id source for display-list items.</summary>
    public Func<ItemId> NextId { get; }

    /// <summary>Gets or sets the fonts metafile text is drawn with. Null takes <see cref="FontLibrary.CreateDefault"/>, created once per context.</summary>
    public IFontProvider? Fonts { get; set; }

    /// <summary>Gets or sets where problems are reported; null drops them. Each is a <see cref="Defect"/> with a <c>render.metafile-</c> code.</summary>
    public Action<Defect>? Report { get; set; }

    /// <summary>Gets or sets the location attached to reported defects, normally the picture's part (<see cref="DefectLocation.InPart"/>).</summary>
    public DefectLocation Location { get; set; }

    /// <summary>Gets or sets whether the picture is wrapped in a <c>Figure</c> tag. Default false; set it when the page is tagged.</summary>
    public bool Tagged { get; set; }

    /// <summary>Gets or sets the alternative text of the <c>Figure</c> tag.</summary>
    public string? AltText { get; set; }

    /// <summary>
    /// Gets or sets whether a picture the display list cannot express (XOR and other raster operations, triangle gradients, union and
    /// exclusive-or clips) is drawn as a bitmap of the whole picture. Default true. When false such a picture is drawn as well as the
    /// vector commands allow, and a defect says what was approximated.
    /// </summary>
    public bool AllowRasterFallback { get; set; } = true;

    /// <summary>Gets or sets the longest side, in pixels, of a rasterised fallback. Default 4096.</summary>
    public int MaxRasterSide { get; set; } = 4096;

    /// <summary>Gets or sets the metafile interpreter's limits; null takes the defaults.</summary>
    public MetafileLimits? Limits { get; set; }

    internal IFontProvider? ResolvedFonts { get; set; }
}

/// <summary>The part of a picture that is shown, as fractions of its size trimmed from each side (<c>a:srcRect</c>, ECMA-376 Part 1 §20.1.8.55).</summary>
/// <param name="Left">Fraction cut from the left; negative extends the picture.</param>
/// <param name="Top">Fraction cut from the top.</param>
/// <param name="Right">Fraction cut from the right.</param>
/// <param name="Bottom">Fraction cut from the bottom.</param>
public readonly record struct PictureCrop(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Draws embedded Windows metafiles into a display list as vector content. This is the one hook picture painters call for
/// <c>image/x-emf</c>, <c>image/x-wmf</c> and sniffed metafile bytes: it returns false when it cannot draw, and the caller then draws
/// the picture's cached bitmap or a placeholder.
/// </summary>
/// <remarks>
/// <para>
/// Commands come from OneImage's metafile interpreter. Paths, pens, brushes, clips, bitmaps, gradients and text become display-list
/// fills, clips, images and glyph runs; text stays text (selectable, extractable, tagged), set in the fonts of the context's font
/// provider. Strokes are expanded to outlines because the display list has no stroke item.
/// </para>
/// <para>
/// EMF (including EMF+, dual and plus-only) and WMF (placeable or not) are read; bytes that are neither are reported (<c>render.metafile-damaged</c>) and not drawn.
/// </para>
/// </remarks>
public static class MetafilePainter
{
    /// <summary>Tells whether bytes start like an enhanced metafile or a Windows metafile.</summary>
    /// <param name="data">The first bytes of the picture (at least 44).</param>
    /// <returns>True for an EMF header, a placeable WMF key or a standard WMF header.</returns>
    public static bool IsMetafile(ReadOnlySpan<byte> data) => MetafileDocument.IsEmf(data) || IsWmf(data);

    /// <summary>Tells whether a media type names a metafile.</summary>
    /// <param name="contentType">An OPC content type or file extension without the dot, compared without regard to case.</param>
    /// <returns>True for <c>image/x-emf</c>, <c>image/emf</c>, <c>image/x-wmf</c>, <c>image/wmf</c>, <c>emf</c> and <c>wmf</c>.</returns>
    public static bool IsMetafileType(string? contentType) =>
        contentType is not null
        && (contentType.Equals("image/x-emf", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("image/emf", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("image/x-wmf", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("image/wmf", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("emf", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("wmf", StringComparison.OrdinalIgnoreCase));

    // [MS-WMF] §2.3.2.1 META_PLACEABLE: Key 0x9AC6CDD7. §2.2.2.1 META_HEADER: Type 1 (memory) or 2 (disk), HeaderSize 9 words.
    internal static bool IsWmf(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 22 && BinaryPrimitives.ReadUInt32LittleEndian(d) == 0x9AC6CDD7)
        {
            return true;
        }

        return d.Length >= 18
            && BinaryPrimitives.ReadUInt16LittleEndian(d) is 1 or 2
            && BinaryPrimitives.ReadUInt16LittleEndian(d[2..]) == 9
            && BinaryPrimitives.ReadUInt16LittleEndian(d[4..]) is 0x0100 or 0x0300;
    }

    /// <summary>Reads the natural size of an enhanced metafile from its header.</summary>
    /// <param name="data">The file bytes.</param>
    /// <param name="widthPoints">The picture frame's width in points.</param>
    /// <param name="heightPoints">The picture frame's height in points.</param>
    /// <returns>False when the bytes are not an enhanced metafile with a usable frame.</returns>
    public static bool TryGetNaturalSize(ReadOnlyMemory<byte> data, out double widthPoints, out double heightPoints)
    {
        widthPoints = heightPoints = 0;
        try
        {
            if (!IsMetafile(data.Span))
            {
                return false;
            }

            MetafileInfo info = MetafileDocument.Identify(data);
            // [MS-EMF] §2.2.9 Header: Frame is in 0.01 mm; 1 pt = 25.4 / 72 mm.
            widthPoints = Math.Abs(info.Frame.Width) / 100.0 * 72.0 / 25.4;
            heightPoints = Math.Abs(info.Frame.Height) / 100.0 * 72.0 / 25.4;
            if (widthPoints <= 0 || heightPoints <= 0)
            {
                widthPoints = info.Width * 0.75;
                heightPoints = info.Height * 0.75;
            }

            return widthPoints > 0 && heightPoints > 0;
        }
        catch (Exception ex) when (ex is OneImage.ImageException)
        {
            return false;
        }
    }

    /// <summary>Draws a metafile into a rectangle of the display list.</summary>
    /// <param name="data">The file bytes; kept in use until this call returns.</param>
    /// <param name="context">The list, bitmap table, fonts and reporting.</param>
    /// <param name="destination">Where the picture goes, in CSS pixels. The picture is clipped to it.</param>
    /// <param name="crop">The part of the picture shown; default shows all of it.</param>
    /// <param name="cancellationToken">Cancels interpretation of a large file.</param>
    /// <returns>True when the picture was drawn. False when the bytes are not a drawable metafile (a defect says why) and nothing was added to the list.</returns>
    public static bool TryPaint(ReadOnlyMemory<byte> data, MetafilePaintContext context, in RectF destination, PictureCrop crop = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsMetafile(data.Span))
        {
            Say(context, "render.metafile-damaged", DefectKind.Record, DefectSeverity.Dropped, "The picture is not a metafile (no EMF header signature, no WMF header).");
            return false;
        }

        MetafileDocument doc;
        try
        {
            doc = MetafileDocument.Parse(data, new MetafileReadOptions { Limits = context.Limits ?? MetafileLimits.Default }, cancellationToken);
        }
        catch (Exception ex) when (ex is OneImage.ImageException)
        {
            Say(context, "render.metafile-damaged", DefectKind.Record, DefectSeverity.Dropped, "The metafile could not be read: " + ex.Message);
            return false;
        }

        RectD pr = doc.Info.PictureRectangle.Normalized;
        if (!(pr.Width > 0) || !(pr.Height > 0) || !(destination.Width > 0) || !(destination.Height > 0))
        {
            return false;
        }

        // ECMA-376 Part 1 §20.1.8.55 srcRect: the shown part of the picture spans the destination.
        double shownW = pr.Width * (1 - crop.Left - crop.Right);
        double shownH = pr.Height * (1 - crop.Top - crop.Bottom);
        if (!(shownW > 0) || !(shownH > 0))
        {
            return false;
        }

        Matrix3x2 toPage =
            Matrix3x2.CreateTranslation((float)-(pr.Left + (crop.Left * pr.Width)), (float)-(pr.Top + (crop.Top * pr.Height)))
            * Matrix3x2.CreateScale((float)(destination.Width / shownW), (float)(destination.Height / shownH))
            * Matrix3x2.CreateTranslation(destination.X, destination.Y);

        foreach (DiagnosticCommand d in doc.Diagnostics)
        {
            if (d.Severity != DiagnosticSeverity.Info)
            {
                Say(context, "render.metafile-diagnostic", DefectKind.Record, DefectSeverity.Dropped, $"Record {d.RecordIndex}: {d.Message}");
            }
        }

        var painter = new MetafileVisitor(context, doc.Info, toPage, destination);
        string why = string.Empty;
        bool raster = context.AllowRasterFallback && MetafileVisitor.NeedsRaster(doc, out why);

        painter.Begin();
        try
        {
            if (raster && painter.PaintRaster(data, pr))
            {
                Say(context, "render.metafile-rasterised", DefectKind.Unsupported, DefectSeverity.Repaired, "The picture uses " + why + ", which the display list cannot express; it is drawn as a bitmap.");
            }
            else
            {
                doc.Play(painter);
            }
        }
        finally
        {
            painter.End();
        }

        return true;
    }

    internal static void Say(MetafilePaintContext c, string code, DefectKind kind, DefectSeverity severity, string message) =>
        c.Report?.Invoke(new Defect(kind, code, severity, message, c.Location));
}
