namespace RecursiveAI.OneDocument.Charts;

/// <summary>The resolved scale of a value axis.</summary>
/// <param name="Min">Axis minimum.</param>
/// <param name="Max">Axis maximum.</param>
/// <param name="MajorUnit">Distance between major ticks (the log base for a logarithmic axis).</param>
/// <param name="MinorUnit">Distance between minor ticks.</param>
/// <param name="LogBase">The base for a logarithmic axis, else 0.</param>
public readonly record struct AxisScale(double Min, double Max, double MajorUnit, double MinorUnit, double LogBase)
{
    /// <summary>True for a logarithmic scale.</summary>
    public bool IsLog => LogBase > 0;

    /// <summary>Maps a value to 0..1 along the axis (0 at <see cref="Min"/>); values outside are extrapolated.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The fraction.</returns>
    public double Fraction(double value)
    {
        if (IsLog)
        {
            double lo = Math.Log(Min, LogBase);
            double hi = Math.Log(Max, LogBase);
            return hi == lo ? 0 : (Math.Log(Math.Max(value, double.Epsilon), LogBase) - lo) / (hi - lo);
        }

        return Max == Min ? 0 : (value - Min) / (Max - Min);
    }

    /// <summary>The major tick values from <see cref="Min"/> to <see cref="Max"/>.</summary>
    /// <returns>The values; at most 1000.</returns>
    public IReadOnlyList<double> MajorTicks() => Ticks(MajorUnit);

    /// <summary>The minor tick values (including those on major ticks).</summary>
    /// <returns>The values; at most 5000.</returns>
    public IReadOnlyList<double> MinorTicks() => IsLog ? LogMinor() : Ticks(MinorUnit, 5000);

    private List<double> LogMinor()
    {
        var list = new List<double>();
        double lo = Math.Floor(Math.Log(Min, LogBase) + 1e-9);
        double hi = Math.Ceiling(Math.Log(Max, LogBase) - 1e-9);
        for (double e = lo; e < hi && list.Count < 5000; e++)
        {
            double decade = Math.Pow(LogBase, e);
            for (int k = 1; k < LogBase && k < 100; k++)
            {
                double v = decade * k;
                if (v >= Min * (1 - 1e-9) && v <= Max * (1 + 1e-9))
                {
                    list.Add(v);
                }
            }
        }

        list.Add(Max);
        return list;
    }

    private List<double> Ticks(double unit, int cap = 1000)
    {
        var list = new List<double>();
        if (IsLog)
        {
            double lo = Math.Round(Math.Log(Min, LogBase));
            for (double e = lo; list.Count < cap; e++)
            {
                double v = Math.Pow(LogBase, e);
                if (v > Max * (1 + 1e-9))
                {
                    break;
                }

                if (v >= Min * (1 - 1e-9))
                {
                    list.Add(v);
                }
            }

            return list;
        }

        if (!(unit > 0) || !double.IsFinite(unit))
        {
            list.Add(Min);
            list.Add(Max);
            return list;
        }

        // Counting from the minimum with an index avoids drift from repeated addition.
        long n = (long)Math.Floor(((Max - Min) / unit) + 1e-9);
        for (long i = 0; i <= n && list.Count < cap; i++)
        {
            list.Add(Snap(Min + (i * unit), unit));
        }

        return list;
    }

    // Removes representation noise such as 0.30000000000000004 relative to the unit.
    internal static double Snap(double v, double unit)
    {
        if (unit <= 0)
        {
            return v;
        }

        int digits = Math.Clamp(3 - (int)Math.Floor(Math.Log10(unit)), 0, 15);
        return Math.Round(v, digits);
    }
}

/// <summary>
/// Automatic choice of value-axis minimum, maximum and major unit, modelled on Excel's: a zero rule at one sixth, 5 % head-room and
/// tick steps of 1, 2 or 5 times a power of ten. The model is unverified against Excel; docs/charts.md lists the generated cases
/// that settle it.
/// </summary>
public static class AxisScaler
{
    /// <summary>The most major intervals the automatic unit accepts (Excel picks the smallest step 1/2/5 × 10^k that gives at most this many).</summary>
    public const int MaxAutoIntervals = 10;

    /// <summary>Computes the scale of a value axis.</summary>
    /// <param name="axis">The axis (its fixed min/max/units and log base win over automatic values).</param>
    /// <param name="dataMin">Smallest plotted value (for stacked groups, the smallest running total).</param>
    /// <param name="dataMax">Largest plotted value.</param>
    /// <param name="percentStacked">True when every group on the axis is 100 % stacked: the axis runs 0..1 in steps of 0.1.</param>
    /// <returns>The scale.</returns>
    public static AxisScale Compute(ChartAxis axis, double dataMin, double dataMax, bool percentStacked = false)
    {
        ArgumentNullException.ThrowIfNull(axis);
        if (!double.IsFinite(dataMin) || !double.IsFinite(dataMax))
        {
            dataMin = 0;
            dataMax = 1;
        }

        if (dataMin > dataMax)
        {
            (dataMin, dataMax) = (dataMax, dataMin);
        }

        if (axis.LogBase is { } b && b >= 2 && b <= 1000)
        {
            return Log(axis, b, dataMin, dataMax);
        }

        double min;
        double max;
        double major;
        if (percentStacked && axis.Min is null && axis.Max is null)
        {
            min = 0;
            max = 1;
            major = axis.MajorUnit ?? 0.1;
        }
        else
        {
            (double rawMin, double rawMax) = AutoRange(dataMin, dataMax);
            double lo = axis.Min ?? rawMin;
            double hi = axis.Max ?? rawMax;
            if (hi <= lo)
            {
                hi = lo + 1;
            }

            major = axis.MajorUnit is { } mu && mu > 0 ? mu : NiceUnit(hi - lo);
            min = axis.Min ?? FloorTo(rawMin, major);
            max = axis.Max ?? CeilTo(rawMax, major);
            if (max <= min)
            {
                max = min + major;
            }
        }

        double minor = axis.MinorUnit is { } mn && mn > 0 ? mn : major / 5;
        return new AxisScale(AxisScale.Snap(min, major), AxisScale.Snap(max, major), major, minor, 0);
    }

    // The raw (unrounded) minimum and maximum Excel starts from.
    // The rule as commonly documented for Excel (written from memory of its behaviour, not from a source read for this unit): when the
    // smallest value is more than 5/6 of the largest the axis does not start at zero; the top gets 5 % of the range as head-room.
    // docs/charts.md lists it among the cases to settle against Excel itself.
    internal static (double Min, double Max) AutoRange(double ymin, double ymax)
    {
        if (ymax <= 0 && ymin < 0)
        {
            (double m, double x) = AutoRange(-ymax, -ymin);
            return (-x, -m);
        }

        if (ymin < 0)
        {
            double pad = (ymax - ymin) * 0.05;
            return (ymin - pad, ymax + pad);
        }

        if (ymax == ymin)
        {
            // A single value (or all equal): Excel starts at zero and leaves head-room above the value.
            return (0, ymax == 0 ? 1 : ymax * 1.05);
        }

        double range = ymax - ymin;
        double min = range / ymax > 1.0 / 6.0 ? 0 : ymin - (range / 2);
        double max = ymax + (0.05 * (ymax - min));
        return (min, max);
    }

    // The smallest of 1, 2, 5 (times a power of ten) that spans the range in at most MaxAutoIntervals steps.
    internal static double NiceUnit(double range)
    {
        if (!(range > 0) || !double.IsFinite(range))
        {
            return 1;
        }

        double exp = Math.Floor(Math.Log10(range)) - 1;
        for (int guard = 0; guard < 8; guard++)
        {
            double p = Math.Pow(10, exp);
            foreach (double m in new[] { 1.0, 2.0, 5.0 })
            {
                double unit = m * p;
                if (range / unit <= MaxAutoIntervals + 1e-9)
                {
                    return unit;
                }
            }

            exp++;
        }

        return range;
    }

    private static double FloorTo(double v, double unit) => Math.Floor((v / unit) + 1e-9) * unit;

    private static double CeilTo(double v, double unit) => Math.Ceiling((v / unit) - 1e-9) * unit;

    private static AxisScale Log(ChartAxis axis, double b, double dataMin, double dataMax)
    {
        double lo = dataMin > 0 ? dataMin : 1;
        double hi = dataMax > lo ? dataMax : lo * b;
        double min = axis.Min is { } m && m > 0 ? m : Math.Pow(b, Math.Floor(Math.Log(lo, b) + 1e-9));
        double max = axis.Max is { } x && x > min ? x : Math.Pow(b, Math.Ceiling(Math.Log(hi, b) - 1e-9));
        if (max <= min)
        {
            max = min * b;
        }

        return new AxisScale(min, max, b, 1, b);
    }

    /// <summary>
    /// Picks how many categories to skip between labels so that labels of the given widest width fit side by side.
    /// </summary>
    /// <param name="count">Number of categories.</param>
    /// <param name="slotWidth">Width of one category slot.</param>
    /// <param name="labelWidth">Width of the widest label plus its gap.</param>
    /// <returns>1 for every category, 2 for every second, and so on.</returns>
    public static int LabelSkip(int count, double slotWidth, double labelWidth)
    {
        if (count <= 1 || slotWidth <= 0 || labelWidth <= slotWidth)
        {
            return 1;
        }

        return (int)Math.Ceiling(labelWidth / slotWidth);
    }
}
