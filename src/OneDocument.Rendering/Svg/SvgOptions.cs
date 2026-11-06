using OneRender.Painting;

namespace RecursiveAI.OneDocument.Rendering.Svg;

/// <summary>How glyph runs are written to SVG.</summary>
public enum SvgGlyphMode
{
    /// <summary>
    /// Text where the SVG text model reproduces the run exactly (one glyph per character, left to right), and glyph outlines for the rest
    /// (ligatures, marks, right-to-left and joined scripts), as long as the font's licence flags allow outlines. This is the default.
    /// </summary>
    Auto = 0,

    /// <summary>Always <c>&lt;text&gt;</c> with a font family and a position for every character. Searchable, but only as exact as the viewer's font.</summary>
    Text = 1,

    /// <summary>Glyph outlines as paths wherever the font's licence flags allow them: exact in any viewer, not searchable.</summary>
    Outlines = 2,
}

/// <summary>An image of the display list, encoded for embedding.</summary>
/// <param name="Bytes">The encoded image file.</param>
/// <param name="MediaType">Its media type, such as <c>image/png</c>.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct SvgImageData(byte[] Bytes, string MediaType, int Width, int Height);

/// <summary>Supplies the pixels behind the image handles of a display list.</summary>
public interface ISvgImageSource
{
    /// <summary>Gets an image as an encoded file.</summary>
    /// <param name="handle">The handle the display list refers to.</param>
    /// <param name="data">Receives the image.</param>
    /// <returns>False when the handle is unknown; the image is then left out.</returns>
    bool TryGetImage(ImageHandle handle, out SvgImageData data);
}

/// <summary>Draws pieces of a display list that SVG cannot express, so the writer can embed them as images.</summary>
public interface ISvgRasterizer
{
    /// <summary>Draws a display list.</summary>
    /// <param name="list">The items, with their origin at the top left of the area to draw, in CSS pixels. Images are those of the page.</param>
    /// <param name="widthPixels">Width of the bitmap to produce.</param>
    /// <param name="heightPixels">Height of the bitmap to produce.</param>
    /// <param name="pixelRatio">Device pixels per CSS pixel.</param>
    /// <returns>A PNG file with an alpha channel (transparent where nothing is painted), or null when the area cannot be drawn.</returns>
    byte[]? RasterizePng(DisplayList list, int widthPixels, int heightPixels, float pixelRatio);
}

/// <summary>A feature the SVG could not express exactly.</summary>
/// <param name="Code">A stable code of the form <c>svg.problem</c>.</param>
/// <param name="Message">A one-line description.</param>
public sealed record SvgNote(string Code, string Message);

/// <summary>What <see cref="SvgWriter"/> did.</summary>
public sealed class SvgWriteResult
{
    internal SvgWriteResult(IReadOnlyList<SvgNote> notes, int textRuns, int outlineRuns, int rasterized)
    {
        Notes = notes;
        TextRuns = textRuns;
        OutlineRuns = outlineRuns;
        RasterizedRegions = rasterized;
    }

    /// <summary>Gets what was approximated or left out; each code appears once.</summary>
    public IReadOnlyList<SvgNote> Notes { get; }

    /// <summary>Gets the glyph runs written as text.</summary>
    public int TextRuns { get; }

    /// <summary>Gets the glyph runs written as outlines.</summary>
    public int OutlineRuns { get; }

    /// <summary>Gets the regions of the display list embedded as images because SVG cannot express them.</summary>
    public int RasterizedRegions { get; }
}

/// <summary>Options for <see cref="SvgWriter"/>.</summary>
public sealed class SvgOptions
{
    /// <summary>Gets or sets the width of the drawing in CSS pixels (the display list's units). Required; must be above zero and finite.</summary>
    public double Width { get; set; }

    /// <summary>Gets or sets the height of the drawing in CSS pixels. Required; must be above zero and finite.</summary>
    public double Height { get; set; }

    /// <summary>Gets or sets the factor from CSS pixels to the <c>width</c> and <c>height</c> written on the root. Default 1; the view box always keeps the CSS pixels.</summary>
    public double Scale { get; set; } = 1;

    /// <summary>Gets or sets whether the page has no background. Default false: a white rectangle is painted first, as the image backend does.</summary>
    public bool TransparentBackground { get; set; }

    /// <summary>Gets or sets how glyph runs are written. Default <see cref="SvgGlyphMode.Auto"/>.</summary>
    public SvgGlyphMode GlyphMode { get; set; } = SvgGlyphMode.Auto;

    /// <summary>Gets or sets the source of image pixels. Default null: images are left out, with a note.</summary>
    public ISvgImageSource? Images { get; set; }

    /// <summary>
    /// Gets or sets the drawing of what SVG cannot express (conic gradients, rounded or inset borders and shadows, several-layer masks,
    /// 3D transforms). Default null: such content is written without the effect, with a note.
    /// </summary>
    public ISvgRasterizer? Rasterizer { get; set; }

    /// <summary>Gets or sets the pixels per CSS pixel of embedded regions. Default 2 (192 dpi); the scale of the enclosing transforms is added.</summary>
    public double RasterScale { get; set; } = 2;

    /// <summary>Gets or sets the document title written as <c>&lt;title&gt;</c>; null writes none.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the language of the drawing (<c>xml:lang</c>); null writes none.</summary>
    public string? Language { get; set; }

    /// <summary>Gets or sets whether the semantic tags become roles, labels, titles and links. Default true.</summary>
    public bool IncludeTags { get; set; } = true;

    /// <summary>Gets or sets the decimal places of coordinates, 0 to 6. Default 3.</summary>
    public int Precision { get; set; } = 3;
}
