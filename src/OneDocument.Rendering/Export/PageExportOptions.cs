using System.Globalization;
using System.Text;
using OneImage;

namespace RecursiveAI.OneDocument.Rendering;

/// <summary>The image formats pages are exported to.</summary>
public enum PageImageFormat
{
    /// <summary>PNG, lossless, with alpha when the background is transparent.</summary>
    Png = 0,

    /// <summary>JPEG, lossy and always opaque (white background).</summary>
    Jpeg = 1,

    /// <summary>TIFF; one file can hold several pages.</summary>
    Tiff = 2,

    /// <summary>WebP, lossless by default.</summary>
    WebP = 3,

    /// <summary>SVG 1.1, text kept as text where that is exact. See <see cref="Svg.SvgWriter"/>.</summary>
    Svg = 4,
}

/// <summary>
/// Which pages to export, as 1-based page numbers: <c>"1-3,5"</c>, <c>"7-"</c> (page 7 to the end), <c>"all"</c>. Pages come out in
/// ascending order, each once, whatever the order the selection names them.
/// </summary>
public sealed class PageSelection
{
    private readonly List<(int From, int To)> _ranges;

    private PageSelection(List<(int From, int To)> ranges) => _ranges = ranges;

    /// <summary>Gets the selection of every page.</summary>
    public static PageSelection All { get; } = new([(1, int.MaxValue)]);

    /// <summary>Gets whether the selection names exactly one page (a single number, or a range of one).</summary>
    public bool IsSinglePage => _ranges.Count == 1 && _ranges[0].From == _ranges[0].To;

    /// <summary>Selects a single page.</summary>
    /// <param name="page">The 1-based page number.</param>
    /// <returns>The selection.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> is below 1.</exception>
    public static PageSelection Page(int page) => Range(page, page);

    /// <summary>Selects a range of pages.</summary>
    /// <param name="from">The first page, 1-based.</param>
    /// <param name="to">The last page, or <see cref="int.MaxValue"/> for the end of the document.</param>
    /// <returns>The selection.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> is below 1 or <paramref name="to"/> is below <paramref name="from"/>.</exception>
    public static PageSelection Range(int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(from, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);
        return new PageSelection([(from, to)]);
    }

    /// <summary>Parses <c>"1-3,5"</c>, <c>"7-"</c>, <c>"-3"</c> (the first three pages) or <c>"all"</c>.</summary>
    /// <param name="text">The selection text.</param>
    /// <param name="selection">Receives the selection.</param>
    /// <returns>False when the text is not a selection.</returns>
    public static bool TryParse(string? text, out PageSelection selection)
    {
        selection = All;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string t = text.Trim();
        if (t.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ranges = new List<(int, int)>();
        foreach (string part in t.Split(','))
        {
            string p = part.Trim();
            int dash = p.IndexOf('-', StringComparison.Ordinal);
            int from, to;
            if (dash < 0)
            {
                if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out from) || from < 1)
                {
                    return false;
                }

                to = from;
            }
            else
            {
                string a = p[..dash].Trim(), b = p[(dash + 1)..].Trim();
                from = 1;
                to = int.MaxValue;
                if ((a.Length > 0 && (!int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out from) || from < 1))
                    || (b.Length > 0 && (!int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out to) || to < 1))
                    || (a.Length == 0 && b.Length == 0) || to < from)
                {
                    return false;
                }
            }

            ranges.Add((from, to));
        }

        selection = new PageSelection(ranges);
        return true;
    }

    /// <summary>Parses a selection; see <see cref="TryParse"/>.</summary>
    /// <param name="text">The selection text.</param>
    /// <returns>The selection.</returns>
    /// <exception cref="FormatException">The text is not a selection.</exception>
    public static PageSelection Parse(string text) =>
        TryParse(text, out PageSelection s) ? s : throw new FormatException("Not a page selection: " + text);

    /// <summary>Lists the selected pages of a document. The end of a range is cut to the last page, so <c>1-99</c> is every page of a short document.</summary>
    /// <param name="pageCount">The number of pages the document has.</param>
    /// <returns>The 1-based page numbers, ascending, without repeats.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A range starts past the last page. A range that ends past it is cut to the last page.</exception>
    public IReadOnlyList<int> Resolve(int pageCount)
    {
        var set = new SortedSet<int>();
        foreach ((int from, int to) in _ranges)
        {
            if (from > pageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageCount), "The document has " + pageCount.ToString(CultureInfo.InvariantCulture) + " page(s); there is no page "
                    + from.ToString(CultureInfo.InvariantCulture) + ".");
            }

            for (int p = from; p <= Math.Min(to, pageCount); p++)
            {
                set.Add(p);
            }
        }

        return [.. set];
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach ((int from, int to) in _ranges)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(from.ToString(CultureInfo.InvariantCulture));
            if (to != from)
            {
                sb.Append('-');
                if (to != int.MaxValue)
                {
                    sb.Append(to.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return sb.ToString();
    }
}

/// <summary>Options for <see cref="PageExporter"/>.</summary>
public sealed class PageExportOptions
{
    /// <summary>Gets or sets the pages to export. Default null: every page for <see cref="PageExporter.ExportPages"/> and for a TIFF, the first page for a single-page format written to one stream.</summary>
    public PageSelection? Pages { get; set; }

    /// <summary>
    /// Gets or sets the resolution in dots per inch (1 to 2400). Default null: 144 for raster formats, and 96 (CSS pixels) for the width and
    /// height of an SVG. Ignored when <see cref="WidthPixels"/> or <see cref="HeightPixels"/> is set.
    /// </summary>
    public double? Dpi { get; set; }

    /// <summary>Gets or sets the width of every page in pixels; the height follows the page's proportions. Default null. With <see cref="HeightPixels"/> too, the page is scaled to fit inside both.</summary>
    public int? WidthPixels { get; set; }

    /// <summary>Gets or sets the height of every page in pixels; see <see cref="WidthPixels"/>. Default null.</summary>
    public int? HeightPixels { get; set; }

    /// <summary>Gets or sets the quality of JPEG and lossy WebP, 1 to 100. Default 90.</summary>
    public int Quality { get; set; } = 90;

    /// <summary>Gets or sets whether WebP is lossless. Default true: pages are mostly text and flat colour, which lossy coding smears.</summary>
    public bool WebPLossless { get; set; } = true;

    /// <summary>Gets or sets the TIFF compression. Default <see cref="TiffCompression.Deflate"/>.</summary>
    public TiffCompression TiffCompression { get; set; } = TiffCompression.Deflate;

    /// <summary>Gets or sets whether the page background is transparent (PNG, WebP, TIFF and SVG; JPEG is always white). Default false: white paper.</summary>
    public bool TransparentBackground { get; set; }

    /// <summary>Gets or sets how glyph runs are written to SVG. Default <see cref="Svg.SvgGlyphMode.Auto"/>.</summary>
    public Svg.SvgGlyphMode SvgGlyphMode { get; set; } = Svg.SvgGlyphMode.Auto;

    /// <summary>Gets or sets whether SVG carries the document structure as roles, labels and links. Default true.</summary>
    public bool SvgTags { get; set; } = true;

    /// <summary>Gets or sets the layout options (fonts and limits) for documents; workbooks and presentations take its fonts. Default: the standard fonts.</summary>
    public RecursiveAI.OneDocument.Layout.LayoutOptions Layout { get; set; } = new();
}

/// <summary>One exported page.</summary>
/// <param name="Number">The 1-based page number in the document.</param>
/// <param name="Data">The encoded file.</param>
/// <param name="WidthPixels">Width in pixels (for SVG, the width written on the root).</param>
/// <param name="HeightPixels">Height in pixels.</param>
public sealed record PageImage(int Number, byte[] Data, int WidthPixels, int HeightPixels);

/// <summary>What an export did.</summary>
/// <param name="PageCount">How many pages the document has.</param>
/// <param name="Exported">The 1-based numbers of the pages written.</param>
/// <param name="Notes">What was approximated or left out, as <c>code: message</c> lines (font substitutions, features the layout or the SVG writer could not express).</param>
public sealed record PageExportReport(int PageCount, IReadOnlyList<int> Exported, IReadOnlyList<string> Notes);
