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

    /// <summary>
    /// Gets or sets a hook that tells the painter which shapes a shape inherits from (see <see cref="ShapeInheritance"/>). Null means
    /// every shape stands on its own, as in a Word drawing.
    /// </summary>
    public Func<MarkupNode, ShapeInheritance?>? ShapeResolver { get; set; }

    /// <summary>
    /// Gets or sets a painter for graphic frames the shape painter does not know (tables, diagrams, objects). It receives the
    /// <c>p:graphicFrame</c> element, the display list and the frame's box in CSS pixels, and returns true when it painted the frame;
    /// on false the frame goes on to the chart painter. Null leaves such frames unpainted (note <c>drawing.graphic-frame</c>).
    /// </summary>
    public Func<MarkupNode, DisplayList, double, double, double, double, bool>? FramePainter { get; set; }

    /// <summary>
    /// Gets or sets a hook that says how a shape appears in the structure tree: a figure with alternative text, or an artifact. Null
    /// means shapes are not tagged by the painter (a Word drawing is tagged by the caller). A null result leaves one shape untagged.
    /// </summary>
    public Func<MarkupNode, DrawingTag?>? ShapeTagger { get; set; }

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

/// <summary>How the painter tags one shape.</summary>
/// <param name="Role">The structure role: <see cref="TagRole.Figure"/>, <see cref="TagRole.Artifact"/> or another.</param>
/// <param name="AltText">Alternative text for a figure; null for none.</param>
public sealed record DrawingTag(TagRole Role, string? AltText);

/// <summary>A request to paint the text of one shape.</summary>
/// <param name="List">The list to paint into; its coordinates are CSS pixels on the page.</param>
/// <param name="Shape">The shape element (<c>wps:wsp</c>, <c>p:sp</c>).</param>
/// <param name="Transform">Maps points in the text rectangle's own space (origin at its top-left, y down) to page pixels; it includes the shape's rotation.</param>
/// <param name="Width">The text rectangle's width in points, insets already removed.</param>
/// <param name="Height">The text rectangle's height in points, insets already removed.</param>
/// <param name="NextId">Allocates item ids.</param>
/// <param name="BodyProperties">The <c>bodyPr</c> element: anchoring, wrap, vertical text; may be a null node.</param>
public sealed record ShapeTextRequest(DisplayList List, MarkupNode Shape, System.Numerics.Matrix3x2 Transform, double Width, double Height, Func<ItemId> NextId, MarkupNode BodyProperties)
{
    /// <summary>Gets the left edge of the shape's text rectangle (before insets) in the shape's own space, in points.</summary>
    public double RectX { get; init; }

    /// <summary>Gets the top edge of the shape's text rectangle (before insets) in the shape's own space, in points.</summary>
    public double RectY { get; init; }

    /// <summary>Gets the width of the shape's text rectangle before insets, in points.</summary>
    public double RectWidth { get; init; }

    /// <summary>Gets the height of the shape's text rectangle before insets, in points.</summary>
    public double RectHeight { get; init; }

    /// <summary>Gets the matrix from the shape's own space (points, origin at the top-left of its box) to page pixels, rotation and flips included.</summary>
    public System.Numerics.Matrix3x2 ShapeMatrix { get; init; } = System.Numerics.Matrix3x2.Identity;
}

/// <summary>
/// What a host knows about a shape beyond its own markup: the shapes it inherits properties from (a slide placeholder takes its
/// position, geometry, fill and line from its layout and master placeholders) and a size the host has decided on.
/// </summary>
public sealed class ShapeInheritance
{
    /// <summary>Gets the shape elements that supply a property the shape does not write, nearest first.</summary>
    public IReadOnlyList<MarkupNode> Parents { get; init; } = [];

    /// <summary>Gets a replacement top edge in EMU, or null to keep the shape's.</summary>
    public long? TopEmu { get; init; }

    /// <summary>Gets a replacement height in EMU, or null to keep the shape's (a shape that grows to fit its text).</summary>
    public long? HeightEmu { get; init; }
}

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

    /// <summary>Gets the positioning and wrapping details of an anchored drawing; null for inline drawings.</summary>
    public DrawingAnchor? Anchor { get; init; }

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
            Anchor = inline ? null : DrawingAnchor.Read(holder, cx, cy),
        };
    }

    /// <summary>
    /// Reads a legacy <c>w:pict</c> that holds one VML shape (<c>v:shape</c>, <c>v:rect</c>, <c>v:roundrect</c>, <c>v:oval</c>, <c>v:line</c>):
    /// its size and position from the CSS-like <c>style</c> attribute and its wrapping from <c>w10:wrap</c>.
    /// </summary>
    /// <param name="pict">The <c>w:pict</c> element.</param>
    /// <returns>The information, or null when the element holds no shape with a width and height.</returns>
    public static DrawingInfo? ReadPict(MarkupNode pict) => VmlShapes.Read(pict);

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

/// <summary>How text wraps around an anchored drawing (ECMA-376 Part 1 §20.4.2.15 to §20.4.2.19, the <c>wrap*</c> elements).</summary>
public enum DrawingWrap
{
    /// <summary>No wrapping: the drawing floats over or behind the text (<c>wrapNone</c>, also when no wrap element is present).</summary>
    None = 0,

    /// <summary>Text wraps around the drawing's rectangle (<c>wrapSquare</c>).</summary>
    Square = 1,

    /// <summary>Text wraps close around the drawing's outline (<c>wrapTight</c>).</summary>
    Tight = 2,

    /// <summary>Text wraps around the outline and may enter concave parts (<c>wrapThrough</c>).</summary>
    Through = 3,

    /// <summary>Text stops above the drawing and resumes below it (<c>wrapTopAndBottom</c>).</summary>
    TopAndBottom = 4,
}

/// <summary>
/// The positioning and wrapping an anchored drawing (<c>wp:anchor</c>) states: where it sits relative to what, how text wraps around it,
/// and how it stacks. Lengths are in points; percentages are fractions (1 is 100 %).
/// </summary>
public sealed class DrawingAnchor
{
    /// <summary>Gets whether <c>simplePos</c> is on: the drawing sits at <see cref="SimpleX"/>, <see cref="SimpleY"/> from the page's top-left corner.</summary>
    public bool SimplePosition { get; init; }

    /// <summary>Gets the simple position's x in points.</summary>
    public double SimpleX { get; init; }

    /// <summary>Gets the simple position's y in points.</summary>
    public double SimpleY { get; init; }

    /// <summary>Gets the horizontal position as a fraction of the base (<c>wp14:pctPosHOffset</c>), or null.</summary>
    public double? PercentX { get; init; }

    /// <summary>Gets the vertical position as a fraction of the base (<c>wp14:pctPosVOffset</c>), or null.</summary>
    public double? PercentY { get; init; }

    /// <summary>Gets what the percentage width is relative to (<c>wp14:sizeRelH/@relativeFrom</c>), or null.</summary>
    public string? SizeRelativeH { get; init; }

    /// <summary>Gets the width as a fraction of that base (<c>wp14:pctWidth</c>), or null.</summary>
    public double? PercentWidth { get; init; }

    /// <summary>Gets what the percentage height is relative to, or null.</summary>
    public string? SizeRelativeV { get; init; }

    /// <summary>Gets the height as a fraction of that base (<c>wp14:pctHeight</c>), or null.</summary>
    public double? PercentHeight { get; init; }

    /// <summary>Gets the stacking order (<c>relativeHeight</c>); larger is in front.</summary>
    public long RelativeHeight { get; init; }

    /// <summary>Gets whether the drawing may overlap other drawings (<c>allowOverlap</c>; default true).</summary>
    public bool AllowOverlap { get; init; } = true;

    /// <summary>Gets whether the drawing is positioned relative to its table cell when anchored in one (<c>layoutInCell</c>; default true).</summary>
    public bool LayoutInCell { get; init; } = true;

    /// <summary>Gets how text wraps.</summary>
    public DrawingWrap Wrap { get; init; }

    /// <summary>Gets the sides text may be on (<c>wrapText</c>: <c>bothSides</c>, <c>left</c>, <c>right</c>, <c>largest</c>).</summary>
    public string WrapText { get; init; } = "bothSides";

    /// <summary>Gets the distance from text above in points.</summary>
    public double DistanceTop { get; init; }

    /// <summary>Gets the distance from text below in points.</summary>
    public double DistanceBottom { get; init; }

    /// <summary>Gets the distance from text on the left in points.</summary>
    public double DistanceLeft { get; init; }

    /// <summary>Gets the distance from text on the right in points.</summary>
    public double DistanceRight { get; init; }

    /// <summary>Gets the wrap polygon of tight and through wrapping in 21600ths of the drawing's width and height, or null when the drawing's own outline is used.</summary>
    public IReadOnlyList<(double X, double Y)>? Polygon { get; init; }

    /// <summary>Gets the extra room effects take around the drawing (<c>wp:effectExtent</c>): left, top, right, bottom in points.</summary>
    public (double Left, double Top, double Right, double Bottom) EffectExtent { get; init; }

    internal static DrawingAnchor Read(MarkupNode holder, double cx, double cy)
    {
        _ = cx;
        _ = cy;
        MarkupNode simple = DrawingTheme.Child(holder, "simplePos");
        double Pt(MarkupNode e, string a) => DrawingInfo.Emu(e.GetAttribute(a)) / 12700.0;
        bool simpleOn = holder.GetAttribute("simplePos") is "1" or "true";
        // wp14 percentages are in thousandths of a percent.
        double? Pct(MarkupNode e) => e.IsNull ? null : DrawingInfo.Emu(e.InnerText) / 100000.0;
        MarkupNode ph = DrawingTheme.Child(holder, "positionH"), pv = DrawingTheme.Child(holder, "positionV");
        MarkupNode srH = DrawingTheme.Child(holder, "sizeRelH"), srV = DrawingTheme.Child(holder, "sizeRelV");
        MarkupNode ee = DrawingTheme.Child(holder, "effectExtent");
        DrawingWrap wrap = DrawingWrap.None;
        string wrapText = "bothSides";
        MarkupNode wrapEl = default;
        foreach (MarkupNode c in holder.ChildElements)
        {
            DrawingWrap w = c.LocalNameString switch
            {
                "wrapSquare" => DrawingWrap.Square,
                "wrapTight" => DrawingWrap.Tight,
                "wrapThrough" => DrawingWrap.Through,
                "wrapTopAndBottom" => DrawingWrap.TopAndBottom,
                _ => DrawingWrap.None,
            };
            if (c.LocalNameString is "wrapNone" or "wrapSquare" or "wrapTight" or "wrapThrough" or "wrapTopAndBottom")
            {
                wrap = w;
                wrapEl = c;
            }
        }

        if (!wrapEl.IsNull && wrapEl.GetAttribute("wrapText") is { Length: > 0 } wt)
        {
            wrapText = wt;
        }

        // The distances may sit on the wrap element or on the anchor; the wrap element's own values win.
        double Dist(string name) => !wrapEl.IsNull && wrapEl.GetAttribute(name) is { } v ? DrawingInfo.Emu(v) / 12700.0 : DrawingInfo.Emu(holder.GetAttribute(name)) / 12700.0;
        List<(double, double)>? poly = null;
        MarkupNode wp = DrawingTheme.Child(wrapEl, "wrapPolygon");
        if (!wp.IsNull)
        {
            poly = [];
            foreach (MarkupNode pt in wp.ChildElements)
            {
                if (pt.LocalNameString is "start" or "lineTo")
                {
                    poly.Add((DrawingInfo.Emu(pt.GetAttribute("x")), DrawingInfo.Emu(pt.GetAttribute("y"))));
                }
            }

            if (poly.Count < 3)
            {
                poly = null;
            }
        }

        long.TryParse(holder.GetAttribute("relativeHeight"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long rh);
        return new DrawingAnchor
        {
            SimplePosition = simpleOn,
            SimpleX = simple.IsNull ? 0 : Pt(simple, "x"),
            SimpleY = simple.IsNull ? 0 : Pt(simple, "y"),
            PercentX = Pct(DrawingTheme.Child(ph, "pctPosHOffset")),
            PercentY = Pct(DrawingTheme.Child(pv, "pctPosVOffset")),
            SizeRelativeH = srH.IsNull ? null : srH.GetAttribute("relativeFrom"),
            PercentWidth = Pct(DrawingTheme.Child(srH, "pctWidth")),
            SizeRelativeV = srV.IsNull ? null : srV.GetAttribute("relativeFrom"),
            PercentHeight = Pct(DrawingTheme.Child(srV, "pctHeight")),
            RelativeHeight = rh,
            AllowOverlap = holder.GetAttribute("allowOverlap") is not ("0" or "false"),
            LayoutInCell = holder.GetAttribute("layoutInCell") is not ("0" or "false"),
            Wrap = wrap,
            WrapText = wrapText,
            DistanceTop = Dist("distT"),
            DistanceBottom = Dist("distB"),
            DistanceLeft = Dist("distL"),
            DistanceRight = Dist("distR"),
            Polygon = poly,
            EffectExtent = ee.IsNull ? default : (Pt(ee, "l"), Pt(ee, "t"), Pt(ee, "r"), Pt(ee, "b")),
        };
    }
}
