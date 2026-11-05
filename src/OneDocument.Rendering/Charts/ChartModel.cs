namespace RecursiveAI.OneDocument.Charts;

// The chart model: what a classic chart part (ECMA-376 Part 1 §21.2, c:chartSpace) or a chartex part ([MS-ODRAWXML] §2.24, cx:chartSpace)
// says, with values taken from the cached data. Rendering reads only this model.

/// <summary>The kind of plot one chart group draws.</summary>
public enum ChartType
{
    /// <summary>Horizontal bars (c:barChart with barDir=bar).</summary>
    Bar,

    /// <summary>Vertical columns (c:barChart with barDir=col).</summary>
    Column,

    /// <summary>Lines (c:lineChart).</summary>
    Line,

    /// <summary>Areas (c:areaChart).</summary>
    Area,

    /// <summary>Pie (c:pieChart, c:ofPieChart drawn as a pie).</summary>
    Pie,

    /// <summary>Doughnut (c:doughnutChart).</summary>
    Doughnut,

    /// <summary>XY scatter (c:scatterChart).</summary>
    Scatter,

    /// <summary>Bubble (c:bubbleChart).</summary>
    Bubble,

    /// <summary>Radar (c:radarChart).</summary>
    Radar,

    /// <summary>Stock (c:stockChart): high-low-close or open-high-low-close.</summary>
    Stock,

    /// <summary>Surface (c:surfaceChart, c:surface3DChart), drawn flattened as a contour grid.</summary>
    Surface,

    /// <summary>Waterfall (cx layoutId waterfall).</summary>
    Waterfall,

    /// <summary>Sunburst (cx layoutId sunburst).</summary>
    Sunburst,

    /// <summary>Treemap (cx layoutId treemap).</summary>
    Treemap,

    /// <summary>Histogram (cx layoutId clusteredColumn with binning).</summary>
    Histogram,

    /// <summary>Pareto (cx layoutId paretoLine over a histogram).</summary>
    Pareto,

    /// <summary>Box and whisker (cx layoutId boxWhisker).</summary>
    BoxWhisker,

    /// <summary>Funnel (cx layoutId funnel).</summary>
    Funnel,

    /// <summary>Region map (cx layoutId regionMap); drawn as a placeholder with a note.</summary>
    RegionMap,

    /// <summary>A chartex layout this reader does not know; drawn as a placeholder.</summary>
    Unknown,
}

/// <summary>How the series of one group combine (c:grouping).</summary>
public enum ChartGrouping
{
    /// <summary>Series side by side, or lines/areas from the axis (standard, clustered).</summary>
    Standard,

    /// <summary>Series added on top of each other.</summary>
    Stacked,

    /// <summary>Series stacked and scaled to 100 %.</summary>
    PercentStacked,
}

/// <summary>The role of an axis.</summary>
public enum ChartAxisKind
{
    /// <summary>Category axis (c:catAx).</summary>
    Category,

    /// <summary>Value axis (c:valAx).</summary>
    Value,

    /// <summary>Date axis (c:dateAx).</summary>
    Date,

    /// <summary>Series axis of a 3-D chart (c:serAx).</summary>
    Series,
}

/// <summary>Where an axis sits (c:axPos).</summary>
public enum ChartAxisPosition
{
    /// <summary>Bottom edge.</summary>
    Bottom,

    /// <summary>Left edge.</summary>
    Left,

    /// <summary>Top edge.</summary>
    Top,

    /// <summary>Right edge.</summary>
    Right,
}

/// <summary>Where a legend sits (c:legendPos).</summary>
public enum ChartLegendPosition
{
    /// <summary>Right of the plot (the default).</summary>
    Right,

    /// <summary>Above the plot.</summary>
    Top,

    /// <summary>Below the plot.</summary>
    Bottom,

    /// <summary>Left of the plot.</summary>
    Left,

    /// <summary>Top-right corner.</summary>
    TopRight,
}

/// <summary>How a gap in the data is shown (c:dispBlanksAs).</summary>
public enum ChartBlanksAs
{
    /// <summary>A gap.</summary>
    Gap,

    /// <summary>Zero.</summary>
    Zero,

    /// <summary>The line spans the gap.</summary>
    Span,
}

/// <summary>A colour as written (ECMA-376 Part 1 §20.1.2.3): a base colour and the transforms applied to it.</summary>
public sealed class ChartColor
{
    /// <summary>The base kind: <c>srgb</c>, <c>scheme</c>, <c>sys</c>, <c>prst</c>, <c>hsl</c> or <c>scrgb</c>.</summary>
    public string Kind { get; set; } = "srgb";

    /// <summary>The base value: hex RGB, scheme name (<c>accent1</c>, <c>tx1</c>, ...), system colour name or preset name.</summary>
    public string Value { get; set; } = "000000";

    /// <summary>Transforms in order, as local name and value in 1/100000 (lumMod, lumOff, tint, shade, alpha, satMod, ...).</summary>
    public List<KeyValuePair<string, double>> Transforms { get; } = [];
}

/// <summary>A gradient stop.</summary>
public sealed class ChartGradientStop
{
    /// <summary>Position 0..1.</summary>
    public double Position { get; set; }

    /// <summary>The colour.</summary>
    public ChartColor Color { get; set; } = new();
}

/// <summary>A fill. Null properties on <see cref="ChartShapeFormat"/> mean "automatic".</summary>
public sealed class ChartFill
{
    /// <summary>True for no fill.</summary>
    public bool None { get; set; }

    /// <summary>The solid colour, or null.</summary>
    public ChartColor? Solid { get; set; }

    /// <summary>Gradient stops (drawn as a linear gradient at <see cref="GradientAngle"/>), empty if not a gradient.</summary>
    public List<ChartGradientStop> Gradient { get; } = [];

    /// <summary>Linear gradient angle in degrees, clockwise from left-to-right.</summary>
    public double GradientAngle { get; set; }

    /// <summary>True for a pattern or picture fill, which is drawn as a flat mid colour.</summary>
    public bool Other { get; set; }
}

/// <summary>A line.</summary>
public sealed class ChartLine
{
    /// <summary>True for no line.</summary>
    public bool None { get; set; }

    /// <summary>Width in EMU (12700 = 1 pt), or null for automatic.</summary>
    public long? WidthEmu { get; set; }

    /// <summary>Colour, or null.</summary>
    public ChartColor? Color { get; set; }

    /// <summary>The preset dash (<c>solid</c>, <c>dash</c>, <c>sysDot</c>, ...), or null.</summary>
    public string? Dash { get; set; }

    /// <summary>Gradient lines use their first stop's colour; set when the line was a gradient.</summary>
    public bool FromGradient { get; set; }
}

/// <summary>Shape properties (c:spPr): fill and line.</summary>
public sealed class ChartShapeFormat
{
    /// <summary>The fill, or null for automatic.</summary>
    public ChartFill? Fill { get; set; }

    /// <summary>The line, or null for automatic.</summary>
    public ChartLine? Line { get; set; }
}

/// <summary>Text properties (c:txPr / a:rPr) reduced to what the renderer uses.</summary>
public sealed class ChartTextFormat
{
    /// <summary>Size in points, or null.</summary>
    public double? SizePoints { get; set; }

    /// <summary>Bold.</summary>
    public bool? Bold { get; set; }

    /// <summary>Italic.</summary>
    public bool? Italic { get; set; }

    /// <summary>Colour.</summary>
    public ChartColor? Color { get; set; }

    /// <summary>Latin typeface; theme fonts (<c>+mn-lt</c>) are kept as written.</summary>
    public string? Font { get; set; }

    /// <summary>Rotation in degrees (bodyPr rot / 60000), or null.</summary>
    public double? RotationDegrees { get; set; }

    /// <summary>True for vertical text (<c>vert</c> other than horz).</summary>
    public bool Vertical { get; set; }
}

/// <summary>Rich or referenced text: a chart title, axis title or series name.</summary>
public sealed class ChartText
{
    /// <summary>The visible text (rich text runs joined, paragraphs separated by newline), or the cached string of a reference.</summary>
    public string? Text { get; set; }

    /// <summary>The cell reference formula when the text comes from a cell, else null.</summary>
    public string? Reference { get; set; }

    /// <summary>The text properties of the first run, merged over the paragraph defaults.</summary>
    public ChartTextFormat? Format { get; set; }
}

/// <summary>A manual layout box (c:manualLayout), as fractions of the chart space.</summary>
public sealed class ChartManualLayout
{
    /// <summary>X, as a fraction of chart width, or null.</summary>
    public double? X { get; set; }

    /// <summary>Y, as a fraction of chart height, or null.</summary>
    public double? Y { get; set; }

    /// <summary>Width fraction, or null.</summary>
    public double? Width { get; set; }

    /// <summary>Height fraction, or null.</summary>
    public double? Height { get; set; }

    /// <summary>True when x/y are offsets from the default position (xMode/yMode = factor); false for edge.</summary>
    public bool XIsFactor { get; set; }

    /// <summary>True when y is a factor.</summary>
    public bool YIsFactor { get; set; }

    /// <summary>True when layoutTarget is inner (the plot area without its labels).</summary>
    public bool InnerTarget { get; set; }
}

/// <summary>A title (c:title).</summary>
public sealed class ChartTitle
{
    /// <summary>The text, or null when the title is auto (series name or "Chart Title").</summary>
    public ChartText? Text { get; set; }

    /// <summary>True when the title floats over the plot.</summary>
    public bool Overlay { get; set; }

    /// <summary>Manual layout.</summary>
    public ChartManualLayout? Layout { get; set; }

    /// <summary>Shape properties.</summary>
    public ChartShapeFormat? Format { get; set; }

    /// <summary>Text properties (c:txPr) when the text is not rich.</summary>
    public ChartTextFormat? TextFormat { get; set; }
}

/// <summary>Cached data of one dimension: numbers or strings, one entry per point index.</summary>
public sealed class ChartData
{
    /// <summary>The reference formula (c:f), or null.</summary>
    public string? Reference { get; set; }

    /// <summary>Format code of the numbers (c:formatCode), or null.</summary>
    public string? FormatCode { get; set; }

    /// <summary>True when the data are numbers; false for strings.</summary>
    public bool IsNumeric { get; set; }

    /// <summary>The numeric points; null for a missing point. Empty for string data.</summary>
    public List<double?> Numbers { get; } = [];

    /// <summary>The string points; null for a missing point. For numeric data, filled lazily by the renderer.</summary>
    public List<string?> Strings { get; } = [];

    /// <summary>Extra category levels of a multi-level reference, innermost first after <see cref="Strings"/>; each list is aligned to the points.</summary>
    public List<List<string?>> OuterLevels { get; } = [];

    /// <summary>Number of points (c:ptCount, or the highest index + 1).</summary>
    public int Count => IsNumeric ? Numbers.Count : Strings.Count;
}

/// <summary>A marker (c:marker).</summary>
public sealed class ChartMarker
{
    /// <summary>Symbol name: circle, square, diamond, triangle, x, plus, star, dash, dot, none, auto.</summary>
    public string Symbol { get; set; } = "auto";

    /// <summary>Size 2..72, default 5.</summary>
    public int Size { get; set; } = 5;

    /// <summary>Shape properties.</summary>
    public ChartShapeFormat? Format { get; set; }
}

/// <summary>Data labels (c:dLbls).</summary>
public sealed class ChartDataLabels
{
    /// <summary>Show the value.</summary>
    public bool ShowValue { get; set; }

    /// <summary>Show the category name.</summary>
    public bool ShowCategoryName { get; set; }

    /// <summary>Show the series name.</summary>
    public bool ShowSeriesName { get; set; }

    /// <summary>Show the percentage (pie).</summary>
    public bool ShowPercent { get; set; }

    /// <summary>Show the legend key.</summary>
    public bool ShowLegendKey { get; set; }

    /// <summary>Position token (c:dLblPos: outEnd, inEnd, ctr, inBase, t, b, l, r, bestFit), or null.</summary>
    public string? Position { get; set; }

    /// <summary>Number format code, or null.</summary>
    public string? FormatCode { get; set; }

    /// <summary>True when the format is linked to the source.</summary>
    public bool SourceLinked { get; set; } = true;

    /// <summary>Separator between parts, or null for the default.</summary>
    public string? Separator { get; set; }

    /// <summary>Text properties.</summary>
    public ChartTextFormat? TextFormat { get; set; }

    /// <summary>Shape properties.</summary>
    public ChartShapeFormat? Format { get; set; }

    /// <summary>True when the group was deleted (c:delete).</summary>
    public bool Deleted { get; set; }
}

/// <summary>A trendline (c:trendline), reduced to the fit type.</summary>
public sealed class ChartTrendline
{
    /// <summary>exp, linear, log, movingAvg, poly, power.</summary>
    public string Type { get; set; } = "linear";

    /// <summary>Polynomial order or moving-average period.</summary>
    public int Order { get; set; } = 2;

    /// <summary>Periods forward.</summary>
    public double Forward { get; set; }

    /// <summary>Periods backward.</summary>
    public double Backward { get; set; }

    /// <summary>Line format.</summary>
    public ChartShapeFormat? Format { get; set; }
}

/// <summary>Error bars (c:errBars).</summary>
public sealed class ChartErrorBars
{
    /// <summary>x or y (scatter and bubble only name x).</summary>
    public string Direction { get; set; } = "y";

    /// <summary>both, minus or plus.</summary>
    public string BarType { get; set; } = "both";

    /// <summary>cust, fixedVal, percentage, stdDev, stdErr.</summary>
    public string ValueType { get; set; } = "fixedVal";

    /// <summary>The value for fixedVal, percentage or stdDev.</summary>
    public double Value { get; set; }

    /// <summary>Custom plus values.</summary>
    public List<double?> Plus { get; } = [];

    /// <summary>Custom minus values.</summary>
    public List<double?> Minus { get; } = [];

    /// <summary>True to draw the end cap.</summary>
    public bool EndCap { get; set; } = true;

    /// <summary>Line format.</summary>
    public ChartShapeFormat? Format { get; set; }
}

/// <summary>Formatting for one data point (c:dPt).</summary>
public sealed class ChartDataPoint
{
    /// <summary>The point index.</summary>
    public int Index { get; set; }

    /// <summary>Shape properties.</summary>
    public ChartShapeFormat? Format { get; set; }

    /// <summary>Pie explosion percent, or null.</summary>
    public int? Explosion { get; set; }

    /// <summary>True when the bar inverts if negative.</summary>
    public bool InvertIfNegative { get; set; }

    /// <summary>Marker override.</summary>
    public ChartMarker? Marker { get; set; }

    /// <summary>Waterfall: this point is a subtotal (cx:subtotals idx).</summary>
    public bool IsSubtotal { get; set; }
}

/// <summary>One series.</summary>
public sealed class ChartSeries
{
    /// <summary>The series index (c:idx), which decides its automatic colour.</summary>
    public int Index { get; set; }

    /// <summary>The plotting order (c:order).</summary>
    public int Order { get; set; }

    /// <summary>The series name.</summary>
    public ChartText? Name { get; set; }

    /// <summary>Categories (c:cat) or X values for chartex; null when absent.</summary>
    public ChartData? Categories { get; set; }

    /// <summary>Values (c:val, c:yVal); null when absent.</summary>
    public ChartData? Values { get; set; }

    /// <summary>X values of scatter and bubble (c:xVal); null otherwise.</summary>
    public ChartData? XValues { get; set; }

    /// <summary>Bubble sizes (c:bubbleSize); null otherwise.</summary>
    public ChartData? BubbleSizes { get; set; }

    /// <summary>Shape properties.</summary>
    public ChartShapeFormat? Format { get; set; }

    /// <summary>Marker for line, scatter and radar series.</summary>
    public ChartMarker? Marker { get; set; }

    /// <summary>Data labels.</summary>
    public ChartDataLabels? Labels { get; set; }

    /// <summary>Point overrides.</summary>
    public List<ChartDataPoint> Points { get; } = [];

    /// <summary>Trendlines.</summary>
    public List<ChartTrendline> Trendlines { get; } = [];

    /// <summary>Error bars.</summary>
    public List<ChartErrorBars> ErrorBars { get; } = [];

    /// <summary>Smooth line.</summary>
    public bool Smooth { get; set; }

    /// <summary>Series explosion percent (pie).</summary>
    public int Explosion { get; set; }

    /// <summary>Invert fill when negative (bars).</summary>
    public bool InvertIfNegative { get; set; }

    /// <summary>For a chartex series, true when this is a Pareto line.</summary>
    public bool IsParetoLine { get; set; }

    /// <summary>Which axis group the series belongs to, from its chart group.</summary>
    public bool Hidden { get; set; }
}

/// <summary>One chart group in the plot area: a chart type with its series and axes (c:barChart, c:lineChart, ...; or a chartex layout).</summary>
public sealed class ChartGroup
{
    /// <summary>The type.</summary>
    public ChartType Type { get; set; }

    /// <summary>How series combine.</summary>
    public ChartGrouping Grouping { get; set; }

    /// <summary>The series, in file order.</summary>
    public List<ChartSeries> Series { get; } = [];

    /// <summary>Axis ids this group plots against (c:axId), usually two; empty for pie.</summary>
    public List<long> AxisIds { get; } = [];

    /// <summary>Gap width percent between bar clusters (default 150).</summary>
    public int GapWidth { get; set; } = 150;

    /// <summary>Overlap percent between bars of a cluster (-100..100; default 0, stacked groups use 100).</summary>
    public int Overlap { get; set; }

    /// <summary>Colour each point separately (c:varyColors).</summary>
    public bool VaryColors { get; set; }

    /// <summary>Doughnut hole size percent (default 50).</summary>
    public int HoleSize { get; set; } = 50;

    /// <summary>First slice angle in degrees clockwise from 12 o'clock.</summary>
    public int FirstSliceAngle { get; set; }

    /// <summary>Bubble scale percent.</summary>
    public int BubbleScale { get; set; } = 100;

    /// <summary>Scatter style (lineMarker, smoothMarker, marker, line, smooth) or radar style (standard, marker, filled).</summary>
    public string? Style { get; set; }

    /// <summary>Line group draws markers (c:marker val) .</summary>
    public bool ShowMarkers { get; set; } = true;

    /// <summary>Stock charts: up/down bars present.</summary>
    public bool HasUpDownBars { get; set; }

    /// <summary>Stock charts: high-low lines present.</summary>
    public bool HasHighLowLines { get; set; }

    /// <summary>Group-level data labels.</summary>
    public ChartDataLabels? Labels { get; set; }

    /// <summary>Chartex: binning (histogram) by count, or null for automatic.</summary>
    public int? BinCount { get; set; }

    /// <summary>Chartex: histogram bin size, or null.</summary>
    public double? BinSize { get; set; }

    /// <summary>Chartex box-whisker: show mean marker.</summary>
    public bool ShowMeanMarker { get; set; } = true;

    /// <summary>Chartex box-whisker: show outliers.</summary>
    public bool ShowOutliers { get; set; } = true;

    /// <summary>Chartex box-whisker: exclusive quartile method (default); false for inclusive.</summary>
    public bool ExclusiveQuartiles { get; set; } = true;

    /// <summary>Chartex waterfall: connector lines shown.</summary>
    public bool ShowConnectorLines { get; set; } = true;

    /// <summary>Chartex: free-form note, set for placeholders (region map, unknown layouts).</summary>
    public string? Note { get; set; }

    /// <summary>The group's secondary-axis flag is derived: true when the group plots on an axis pair other than the first group's.</summary>
    public bool IsSecondary { get; internal set; }
}

/// <summary>An axis.</summary>
public sealed class ChartAxis
{
    /// <summary>The id the groups refer to (c:axId).</summary>
    public long Id { get; set; }

    /// <summary>The kind.</summary>
    public ChartAxisKind Kind { get; set; }

    /// <summary>The position.</summary>
    public ChartAxisPosition Position { get; set; }

    /// <summary>The id of the axis this one crosses (c:crossAx).</summary>
    public long CrossAxisId { get; set; }

    /// <summary>The axis is not drawn (c:delete).</summary>
    public bool Deleted { get; set; }

    /// <summary>Fixed minimum, or null for automatic.</summary>
    public double? Min { get; set; }

    /// <summary>Fixed maximum, or null for automatic.</summary>
    public double? Max { get; set; }

    /// <summary>Fixed major unit, or null.</summary>
    public double? MajorUnit { get; set; }

    /// <summary>Fixed minor unit, or null.</summary>
    public double? MinorUnit { get; set; }

    /// <summary>Logarithm base (2..1000) when the axis is logarithmic, else null.</summary>
    public double? LogBase { get; set; }

    /// <summary>Values run from high to low (c:orientation maxMin).</summary>
    public bool Reverse { get; set; }

    /// <summary>Where this axis crosses the crossing axis: autoZero, min or max.</summary>
    public string Crosses { get; set; } = "autoZero";

    /// <summary>Explicit crossing value (c:crossesAt), or null.</summary>
    public double? CrossesAt { get; set; }

    /// <summary>Major gridlines present.</summary>
    public bool MajorGridlines { get; set; }

    /// <summary>Major gridline format.</summary>
    public ChartShapeFormat? MajorGridlineFormat { get; set; }

    /// <summary>Minor gridlines present.</summary>
    public bool MinorGridlines { get; set; }

    /// <summary>Minor gridline format.</summary>
    public ChartShapeFormat? MinorGridlineFormat { get; set; }

    /// <summary>The axis title.</summary>
    public ChartTitle? Title { get; set; }

    /// <summary>Number format code, or null.</summary>
    public string? FormatCode { get; set; }

    /// <summary>The format follows the source data.</summary>
    public bool SourceLinked { get; set; } = true;

    /// <summary>Major tick marks: none, in, out, cross.</summary>
    public string MajorTickMark { get; set; } = "out";

    /// <summary>Minor tick marks.</summary>
    public string MinorTickMark { get; set; } = "none";

    /// <summary>Label position: nextTo, low, high, none.</summary>
    public string TickLabelPosition { get; set; } = "nextTo";

    /// <summary>Category axes: label every n categories (c:tickLblSkip), or null for automatic.</summary>
    public int? TickLabelSkip { get; set; }

    /// <summary>Category axes: tick mark every n categories.</summary>
    public int? TickMarkSkip { get; set; }

    /// <summary>Category axes: the value axis crosses between categories (c:crossBetween between) rather than on them.</summary>
    public bool CrossBetween { get; set; } = true;

    /// <summary>Date axes: base unit days, months or years, or null for automatic.</summary>
    public string? BaseTimeUnit { get; set; }

    /// <summary>Display unit (c:dispUnits builtInUnit), or null.</summary>
    public string? DisplayUnit { get; set; }

    /// <summary>The axis line.</summary>
    public ChartShapeFormat? Format { get; set; }

    /// <summary>Label text properties.</summary>
    public ChartTextFormat? TextFormat { get; set; }
}

/// <summary>The legend (c:legend).</summary>
public sealed class ChartLegend
{
    /// <summary>The position.</summary>
    public ChartLegendPosition Position { get; set; } = ChartLegendPosition.Right;

    /// <summary>The legend floats over the plot.</summary>
    public bool Overlay { get; set; }

    /// <summary>Manual layout.</summary>
    public ChartManualLayout? Layout { get; set; }

    /// <summary>Entry indexes the author deleted (c:legendEntry/c:delete).</summary>
    public HashSet<int> DeletedEntries { get; } = [];

    /// <summary>Shape properties.</summary>
    public ChartShapeFormat? Format { get; set; }

    /// <summary>Text properties.</summary>
    public ChartTextFormat? TextFormat { get; set; }
}

/// <summary>A note the reader attaches to the model (what it skipped or approximated). Codes are <c>chart.*</c>.</summary>
/// <param name="Code">The code, for example <c>chart.region-map</c>.</param>
/// <param name="Message">What happened.</param>
public sealed record ChartNote(string Code, string Message);

/// <summary>A chart part: <c>c:chartSpace</c> or <c>cx:chartSpace</c>.</summary>
public sealed class ChartSpace
{
    /// <summary>True when read from a chartex part.</summary>
    public bool IsExtended { get; set; }

    /// <summary>Title.</summary>
    public ChartTitle? Title { get; set; }

    /// <summary>autoTitleDeleted: no automatic title for a single series chart.</summary>
    public bool AutoTitleDeleted { get; set; }

    /// <summary>Groups in drawing order.</summary>
    public List<ChartGroup> Groups { get; } = [];

    /// <summary>Axes.</summary>
    public List<ChartAxis> Axes { get; } = [];

    /// <summary>The legend, or null.</summary>
    public ChartLegend? Legend { get; set; }

    /// <summary>The plot area layout, or null for automatic.</summary>
    public ChartManualLayout? PlotAreaLayout { get; set; }

    /// <summary>Plot area format.</summary>
    public ChartShapeFormat? PlotAreaFormat { get; set; }

    /// <summary>Chart area format (c:chartSpace/c:spPr).</summary>
    public ChartShapeFormat? ChartAreaFormat { get; set; }

    /// <summary>Chart-wide text properties (c:chartSpace/c:txPr).</summary>
    public ChartTextFormat? TextFormat { get; set; }

    /// <summary>The style number (c:style val, 1..48; 2 when absent).</summary>
    public int Style { get; set; } = 2;

    /// <summary>The chart area has rounded corners.</summary>
    public bool RoundedCorners { get; set; }

    /// <summary>Only visible cells plotted.</summary>
    public bool PlotVisibleOnly { get; set; } = true;

    /// <summary>How blanks are shown.</summary>
    public ChartBlanksAs BlanksAs { get; set; } = ChartBlanksAs.Gap;

    /// <summary>Dates use the 1904 system.</summary>
    public bool Date1904 { get; set; }

    /// <summary>The chart has a 3-D view that is drawn flat.</summary>
    public bool Has3DView { get; set; }

    /// <summary>The relationship id of the embedded workbook (c:externalData), or null.</summary>
    public string? ExternalDataRelationshipId { get; set; }

    /// <summary>Descriptive text from the graphic frame, used as alternative text when the chart has no title.</summary>
    public string? AlternativeText { get; set; }

    /// <summary>Raw XML of <c>extLst</c> elements met anywhere, kept for inspection; the original bytes are the round-trip source.</summary>
    public List<string> Extensions { get; } = [];

    /// <summary>What the reader skipped or approximated.</summary>
    public List<ChartNote> Notes { get; } = [];

    /// <summary>The bytes this model was read from; unchanged parts are written back from these.</summary>
    public ReadOnlyMemory<byte> SourceBytes { get; set; }

    /// <summary>The title text to use as the figure's alternative text: the chart title, else the first series name, else "Chart".</summary>
    /// <returns>The text.</returns>
    public string DescribeForAccessibility()
    {
        if (!string.IsNullOrWhiteSpace(AlternativeText))
        {
            return AlternativeText!;
        }

        if (Title?.Text?.Text is { Length: > 0 } t)
        {
            return t;
        }

        foreach (ChartGroup g in Groups)
        {
            foreach (ChartSeries s in g.Series)
            {
                if (s.Name?.Text is { Length: > 0 } n)
                {
                    return "Chart: " + n;
                }
            }
        }

        return "Chart";
    }
}
