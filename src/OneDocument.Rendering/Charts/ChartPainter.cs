using OneRender.Painting;

namespace RecursiveAI.OneDocument.Charts;

/// <summary>How to paint a chart.</summary>
public sealed class ChartPaintOptions
{
    /// <summary>Faces for text. Null draws no text (it is still measured by estimate).</summary>
    public IChartFonts? Fonts { get; set; }

    /// <summary>Number formatting; null uses a plain General formatter.</summary>
    public IChartNumberFormatter? Numbers { get; set; }

    /// <summary>Theme colours and fonts; null uses <see cref="ChartTheme.Default"/>.</summary>
    public ChartTheme? Theme { get; set; }

    /// <summary>Wrap the chart in a Figure tag whose alternative text is the chart's description.</summary>
    public bool Tagged { get; set; } = true;
}

/// <summary>Where the parts of a painted chart landed (CSS pixels, in the display list's coordinates).</summary>
public sealed class ChartLayoutInfo
{
    /// <summary>The chart area.</summary>
    public RectF ChartArea { get; init; }

    /// <summary>The plot area (the inner rectangle the axes enclose); empty for charts without one.</summary>
    public RectF PlotArea { get; init; }

    /// <summary>The title box, or empty.</summary>
    public RectF Title { get; init; }

    /// <summary>The legend box, or empty.</summary>
    public RectF Legend { get; init; }

    /// <summary>The resolved scale of each value axis by axis id.</summary>
    public IReadOnlyDictionary<long, AxisScale> Scales { get; init; } = new Dictionary<long, AxisScale>();

    /// <summary>What the painter approximated or skipped (<c>chart.*</c> codes), after the model's own notes.</summary>
    public IReadOnlyList<ChartNote> Notes { get; init; } = [];
}

/// <summary>Lays out a <see cref="ChartSpace"/> and paints it into a display list.</summary>
public static class ChartPainter
{
    /// <summary>CSS pixels per point.</summary>
    public const double PixelsPerPoint = 96.0 / 72.0;

    /// <summary>Paints a chart.</summary>
    /// <param name="list">The display list to add to (CSS pixels).</param>
    /// <param name="chart">The chart.</param>
    /// <param name="x">Left of the chart's box.</param>
    /// <param name="y">Top of the chart's box.</param>
    /// <param name="width">Width of the box, at least 20.</param>
    /// <param name="height">Height of the box, at least 20.</param>
    /// <param name="options">Fonts, number formatting, theme, tagging.</param>
    /// <returns>The layout actually used.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> or <paramref name="chart"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The box is smaller than 20 px or not finite.</exception>
    public static ChartLayoutInfo Paint(DisplayList list, ChartSpace chart, double x, double y, double width, double height, ChartPaintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(chart);
        if (!(width >= 20) || !(height >= 20) || !double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The chart box must be at least 20 x 20 px.");
        }

        options ??= new ChartPaintOptions();
        var notes = new List<ChartNote>();
        var canvas = new ChartCanvas(list, options.Fonts, options.Theme ?? ChartTheme.Default, notes);
        var r = new ChartRenderer(chart, canvas, options, x, y, width, height, notes);
        try
        {
            return r.Render();
        }
        catch (DisplayListLimitException ex)
        {
            // A chart with more points, glyphs or segments than one display list holds.
            throw new OfficeLimitException("DisplayList", 0, -1, default, ex);
        }
    }
}
