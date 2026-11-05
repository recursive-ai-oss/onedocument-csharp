namespace RecursiveAI.OneDocument.Charts;

/// <summary>Supplies the current values of chart data references such as <c>Sheet1!$B$2:$B$13</c>.</summary>
public interface IChartDataSource
{
    /// <summary>Reads a reference.</summary>
    /// <param name="reference">The reference formula as stored in the chart (<c>c:f</c>).</param>
    /// <param name="values">The cells in row-major order; each is a <see cref="double"/>, <see cref="string"/>, <see cref="bool"/> or null for an empty cell.</param>
    /// <returns>False when the reference cannot be read (unknown sheet, several areas, a name).</returns>
    bool TryGetValues(string reference, out IReadOnlyList<object?> values);
}

/// <summary>Replaces a chart's cached values with the current values of its source cells.</summary>
public static class ChartRefresh
{
    /// <summary>Refreshes the series names, categories, values, X values and bubble sizes whose references <paramref name="source"/> can read.</summary>
    /// <param name="chart">The chart to update; its cached data change, nothing else.</param>
    /// <param name="source">Where the values come from.</param>
    /// <returns>The number of references updated.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static int Refresh(ChartSpace chart, IChartDataSource source)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(source);
        int updated = 0;
        foreach (ChartSeries s in chart.Groups.SelectMany(g => g.Series))
        {
            if (s.Name?.Reference is { } nameRef && source.TryGetValues(nameRef, out IReadOnlyList<object?> nv) && nv.Count > 0)
            {
                s.Name.Text = Text(nv[0]);
                updated++;
            }

            updated += Update(s.Categories, source);
            updated += Update(s.Values, source);
            updated += Update(s.XValues, source);
            updated += Update(s.BubbleSizes, source);
        }

        return updated;
    }

    private static int Update(ChartData? data, IChartDataSource source)
    {
        if (data?.Reference is not { } r || !source.TryGetValues(r, out IReadOnlyList<object?> values))
        {
            return 0;
        }

        data.Numbers.Clear();
        data.Strings.Clear();
        if (data.IsNumeric)
        {
            foreach (object? v in values)
            {
                data.Numbers.Add(v switch { double d => d, bool b => b ? 1 : 0, string s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x) => x, _ => null });
            }
        }
        else
        {
            foreach (object? v in values)
            {
                data.Strings.Add(v is null ? null : Text(v));
            }
        }

        return 1;
    }

    private static string Text(object? v) => v switch
    {
        string s => s,
        double d => d.ToString("G15", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        _ => string.Empty,
    };
}
