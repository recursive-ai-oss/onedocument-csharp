using OneRender.Image;
using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.Drawing;

/// <summary>Something a drawing could not do exactly: a feature that was approximated or skipped.</summary>
/// <param name="Code">A stable code of the form <c>drawing.problem</c>.</param>
/// <param name="Message">A one-line description.</param>
public sealed record DrawingNote(string Code, string Message);

/// <summary>
/// The decoded pictures of a document, shared by every page of one rendering: the display lists refer to them by handle, and the PDF
/// and image backends ask this object for the pixels (it is their <see cref="IImageResolver"/>).
/// </summary>
public sealed class DrawingImages : IImageResolver
{
    private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);
    private readonly ImageStore _store;

    /// <summary>Creates an empty set.</summary>
    public DrawingImages()
    {
        _store = new ImageStore(address => _bytes.TryGetValue(address, out byte[]? b) ? b : null);
    }

    /// <inheritdoc/>
    public RgbaImage? Resolve(ImageHandle handle) => _store.Resolve(handle);

    // The bitmaps a metafile painter produces, in the same handle space as the pictures.
    internal RecursiveAI.OneDocument.Rendering.Metafiles.PictureImages Pictures => _pictures ??= new RecursiveAI.OneDocument.Rendering.Metafiles.PictureImages(_store);

    private RecursiveAI.OneDocument.Rendering.Metafiles.PictureImages? _pictures;

    /// <summary>Registers (once) and looks up a picture.</summary>
    /// <param name="key">A key unique to the picture within the document.</param>
    /// <param name="bytes">The encoded file; only read the first time the key is seen.</param>
    /// <param name="info">The handle and size in pixels.</param>
    /// <returns>False when the bytes are not an image OneImage can decode.</returns>
    public bool TryGet(string key, byte[] bytes, out ImageInfo info)
    {
        _bytes.TryAdd(key, bytes);
        return _store.TryGetImage(key, out info);
    }

    /// <summary>Registers (once) a picture derived from another, such as a recoloured copy, and looks it up.</summary>
    /// <param name="baseKey">The picture it derives from; must already be registered.</param>
    /// <param name="variant">A name for the variant, unique per transformation.</param>
    /// <param name="transform">Makes the variant's pixels from the original's.</param>
    /// <param name="info">The handle and size.</param>
    /// <returns>False when the original is not available.</returns>
    internal bool TryGetVariant(string baseKey, string variant, Func<RgbaImage, RgbaImage> transform, out ImageInfo info)
    {
        string key = baseKey + "\u0002" + variant;
        if (_store.TryGetImage(key, out info))
        {
            return true;
        }

        info = default;
        if (!_store.TryGetImage(baseKey, out ImageInfo baseInfo) || _store.Resolve(baseInfo.Handle) is not { } original)
        {
            return false;
        }

        _store.Add(key, transform(original));
        return _store.TryGetImage(key, out info);
    }
}

/// <summary>What the painter needs to know besides the markup: colours, pictures, tagging and where notes go.</summary>
public sealed class DrawingContext
{
    private readonly List<DrawingNote> _notes = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    /// <summary>Creates a context.</summary>
    /// <param name="colors">Resolves scheme colours; null uses the default theme and colour map.</param>
    /// <param name="images">The picture set shared by the pages of one rendering; null makes a private one (its pixels are then not available to a backend).</param>
    /// <param name="imageBytes">Returns the bytes of the picture a relationship id (<c>r:embed</c>) names, or null. Linked pictures are never asked for.</param>
    /// <param name="scope">Distinguishes pictures of different parts that use the same relationship id (for example the part name).</param>
    public DrawingContext(ColorResolver? colors = null, DrawingImages? images = null, Func<string, byte[]?>? imageBytes = null, string scope = "")
    {
        Colors = colors ?? new ColorResolver();
        Images = images ?? new DrawingImages();
        ImageBytes = imageBytes ?? (_ => null);
        Scope = scope;
    }

    /// <summary>Gets the colour resolver (theme and colour map).</summary>
    public ColorResolver Colors { get; }

    /// <summary>Gets the picture set.</summary>
    public DrawingImages Images { get; }

    /// <summary>Gets or sets whether semantic tags are written. Default true.</summary>
    public bool Tagged { get; set; } = true;

    /// <summary>Gets the notes recorded so far, each code once.</summary>
    public IReadOnlyList<DrawingNote> Notes => _notes;

    internal Func<string, byte[]?> ImageBytes { get; }

    internal string Scope { get; }

    /// <summary>
    /// Gets or sets a hook that lays out and paints the text of a shape. Called with the text body (<c>wps:txbx</c>/<c>p:txBody</c> element
    /// owner), the text rectangle in points on the page after the shape's transform, and the shape's rotation; it paints into the list
    /// it is given. Null leaves shape text unpainted (a note is recorded).
    /// </summary>
    public Action<ShapeTextRequest>? TextPainter { get; set; }

    /// <summary>
    /// Gets or sets the chart painter for a graphic frame that shows a chart (<c>c:chart</c> or <c>cx:chart</c>). It receives the
    /// relationship id of the chart in the frame's part, the display list and the frame's box in CSS pixels, and returns false when the
    /// chart cannot be painted. Null leaves charts unpainted (note <c>drawing.graphic-frame</c>).
    /// </summary>
    public Func<string, DisplayList, double, double, double, double, bool>? ChartPainter { get; set; }

    /// <summary>Gets or sets the font provider metafile text uses; null takes the default library.</summary>
    public RecursiveAI.OneDocument.Layout.IFontProvider? Fonts { get; set; }

    internal void Note(string code, string message)
    {
        if (_seen.Add(code))
        {
            _notes.Add(new DrawingNote(code, message));
        }
    }
}

/// <summary>A request to paint the text of one shape.</summary>
/// <param name="List">The list to paint into; its coordinates are CSS pixels on the page.</param>
/// <param name="Shape">The shape element (<c>wps:wsp</c>, <c>p:sp</c>).</param>
/// <param name="Transform">Maps points in the text rectangle's own space (origin at its top-left, y down) to page pixels; it includes the shape's rotation.</param>
/// <param name="Width">The text rectangle's width in points, insets already removed.</param>
/// <param name="Height">The text rectangle's height in points, insets already removed.</param>
/// <param name="NextId">Allocates item ids.</param>
/// <param name="BodyProperties">The <c>bodyPr</c> element: anchoring, wrap, vertical text; may be a null node.</param>
public sealed record ShapeTextRequest(DisplayList List, MarkupNode Shape, System.Numerics.Matrix3x2 Transform, double Width, double Height, Func<ItemId> NextId, MarkupNode BodyProperties);

/// <summary>The place a drawing takes on the page, in points.</summary>
/// <param name="X">Left edge from the page's left.</param>
/// <param name="Y">Top edge from the page's top.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct DrawingBox(double X, double Y, double Width, double Height);

/// <summary>What a Word <c>w:drawing</c> says about itself apart from its picture or shapes.</summary>
public sealed class DrawingInfo
{
    /// <summary>Gets whether the drawing is inline (<c>wp:inline</c>) rather than anchored.</summary>
    public bool Inline { get; init; }

    /// <summary>Gets the displayed width in points (<c>wp:extent</c>).</summary>
    public double Width { get; init; }

    /// <summary>Gets the displayed height in points.</summary>
    public double Height { get; init; }

    /// <summary>Gets the alternative text (<c>wp:docPr/@descr</c>, else its title), or null.</summary>
    public string? AltText { get; init; }

    /// <summary>Gets whether the drawing is marked decorative (<c>adec:decorative</c>).</summary>
    public bool Decorative { get; init; }

    /// <summary>Gets the <c>a:graphic</c> element, or a null node.</summary>
    public MarkupNode Graphic { get; init; }

    /// <summary>Gets the horizontal anchor position for an anchored drawing, in points from the page's left edge when <c>relativeFrom</c> is <c>page</c>, else the offset from its reference; null for inline drawings.</summary>
    public double? OffsetX { get; init; }

    /// <summary>Gets the vertical anchor offset, as <see cref="OffsetX"/>.</summary>
    public double? OffsetY { get; init; }

    /// <summary>Gets the <c>relativeFrom</c> of the horizontal position (<c>page</c>, <c>margin</c>, <c>column</c>, <c>character</c>…), or null.</summary>
    public string? RelativeFromH { get; init; }

    /// <summary>Gets the <c>relativeFrom</c> of the vertical position (<c>page</c>, <c>margin</c>, <c>paragraph</c>, <c>line</c>…), or null.</summary>
    public string? RelativeFromV { get; init; }

    /// <summary>Gets the horizontal alignment (<c>left</c>, <c>center</c>, <c>right</c>, <c>inside</c>, <c>outside</c>) used instead of an offset, or null.</summary>
    public string? AlignH { get; init; }

    /// <summary>Gets the vertical alignment (<c>top</c>, <c>center</c>, <c>bottom</c>, <c>inside</c>, <c>outside</c>) used instead of an offset, or null.</summary>
    public string? AlignV { get; init; }

    /// <summary>Gets whether an anchored drawing sits behind the text (<c>behindDoc</c>).</summary>
    public bool BehindText { get; init; }

    /// <summary>Reads a <c>w:drawing</c> element.</summary>
    /// <param name="drawing">The <c>w:drawing</c> element.</param>
    /// <returns>The information, or null when the drawing holds neither <c>wp:inline</c> nor <c>wp:anchor</c> with an extent.</returns>
    public static DrawingInfo? Read(MarkupNode drawing)
    {
        MarkupNode holder = DrawingTheme.Child(drawing, "inline");
        bool inline = !holder.IsNull;
        if (!inline)
        {
            holder = DrawingTheme.Child(drawing, "anchor");
        }

        if (holder.IsNull)
        {
            return null;
        }

        MarkupNode extent = DrawingTheme.Child(holder, "extent");
        if (extent.IsNull)
        {
            return null;
        }

        // ECMA-376 Part 1 §20.4.2.7 (extent): cx and cy in EMU, 12700 per point.
        double cx = Emu(extent.GetAttribute("cx")) / 12700.0;
        double cy = Emu(extent.GetAttribute("cy")) / 12700.0;
        MarkupNode docPr = DrawingTheme.Child(holder, "docPr");
        string? alt = docPr.IsNull ? null : (docPr.GetAttribute("descr") is { Length: > 0 } d ? d : docPr.GetAttribute("title"));
        bool decorative = false;
        if (!docPr.IsNull)
        {
            foreach (MarkupNode ext in Descendants(docPr))
            {
                if (ext.LocalNameString == "decorative" && ext.GetAttribute("val") is "1" or "true")
                {
                    decorative = true;
                }
            }
        }

        double? ox = null, oy = null;
        string? rh = null, rv = null, ah = null, av = null;
        if (!inline)
        {
            MarkupNode ph = DrawingTheme.Child(holder, "positionH"), pv = DrawingTheme.Child(holder, "positionV");
            rh = ph.GetAttribute("relativeFrom");
            rv = pv.GetAttribute("relativeFrom");
            ox = Emu(DrawingTheme.Child(ph, "posOffset").InnerText) / 12700.0;
            oy = Emu(DrawingTheme.Child(pv, "posOffset").InnerText) / 12700.0;
            MarkupNode alignH = DrawingTheme.Child(ph, "align"), alignV = DrawingTheme.Child(pv, "align");
            ah = alignH.IsNull ? null : alignH.InnerText.Trim();
            av = alignV.IsNull ? null : alignV.InnerText.Trim();
        }

        return new DrawingInfo
        {
            Inline = inline,
            Width = Math.Clamp(cx, 0, 20000),
            Height = Math.Clamp(cy, 0, 20000),
            AltText = alt,
            Decorative = decorative,
            Graphic = DrawingTheme.Child(holder, "graphic"),
            OffsetX = ox,
            OffsetY = oy,
            RelativeFromH = rh,
            RelativeFromV = rv,
            AlignH = ah,
            AlignV = av,
            BehindText = holder.GetAttribute("behindDoc") is "1" or "true",
        };
    }

    private static IEnumerable<MarkupNode> Descendants(MarkupNode n)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            yield return c;
            foreach (MarkupNode d in Descendants(c))
            {
                yield return d;
            }
        }
    }

    internal static double Emu(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : 0;
}
