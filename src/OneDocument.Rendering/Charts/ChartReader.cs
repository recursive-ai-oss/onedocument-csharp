using RecursiveAI.OneDocument.Diagnostics;
using System.Globalization;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Charts;

/// <summary>
/// Reads a chart part into a <see cref="ChartSpace"/>: classic charts (ECMA-376 Part 1 §21.2, root <c>c:chartSpace</c>) and chartex
/// ([MS-ODRAWXML] §2.24, root <c>cx:chartSpace</c>). Values come from the cached data (<c>numCache</c>, <c>strCache</c>, chartex
/// <c>lvl</c> points); nothing is recalculated. The reader is tolerant: an element it does not understand is skipped and, when it
/// matters, listed in <see cref="ChartSpace.Notes"/>.
/// </summary>
public static class ChartReader
{
    private const int MaxPoints = 2_000_000;

    /// <summary>Reads a chart part.</summary>
    /// <param name="part">The part's bytes.</param>
    /// <param name="partUri">The part's URI for error locations, or null.</param>
    /// <returns>The model; <see cref="ChartSpace.SourceBytes"/> holds <paramref name="part"/>.</returns>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML, is not a chart space (<c>chart.not-a-chart</c>) or has nonsense the reader cannot step over (<c>chart.malformed</c>).</exception>
    /// <exception cref="OfficeLimitException">A limit of the markup layer was exceeded.</exception>
    public static ChartSpace Read(ReadOnlyMemory<byte> part, string? partUri = null)
    {
        MarkupDocument doc = MarkupDocument.Parse(part, null, partUri);
        MarkupNode root = doc.Root;
        if (root.IsNull || !Is(root, "chartSpace"))
        {
            throw new OfficeFormatException(DefectKind.Structure, "chart.not-a-chart", "The part's root element is not chartSpace.", partUri is null ? default : DefectLocation.InPart(partUri, 0));
        }

        var space = new ChartSpace { SourceBytes = part };
        try
        {
            bool extended = root.NamespaceUri.Contains("chartex", StringComparison.Ordinal) || root.NamespaceUri.EndsWith("/2016/5/10/chartex", StringComparison.Ordinal);
            space.IsExtended = extended;
            if (extended)
            {
                new ExtendedReader(space).Read(root);
            }
            else
            {
                new ClassicReader(space).Read(root);
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "chart.malformed", "The chart part has a value the reader cannot use: " + ex.Message, partUri is null ? default : DefectLocation.InPart(partUri, 0), ex);
        }

        return space;
    }

    /// <summary>Reads a chart part, reporting failure with a return value.</summary>
    /// <param name="part">The part's bytes.</param>
    /// <param name="space">The model, or null.</param>
    /// <param name="partUri">The part's URI, or null.</param>
    /// <returns>True when <see cref="Read"/> would have succeeded.</returns>
    public static bool TryRead(ReadOnlyMemory<byte> part, out ChartSpace? space, string? partUri = null)
    {
        try
        {
            space = Read(part, partUri);
            return true;
        }
        catch (OfficeException)
        {
            space = null;
            return false;
        }
    }

    // ---- small helpers over the markup tree; names are matched by local name because the prefix is the file's choice ----

    internal static bool Is(MarkupNode n, string local) => n.Kind == MarkupNodeKind.Element && n.LocalName.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(local));

    internal static MarkupNode Child(MarkupNode n, string local)
    {
        if (n.IsNull)
        {
            return default;
        }

        ReadOnlySpan<char> l = local;
        for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element && Equal(c.LocalName, l))
            {
                return c;
            }
        }

        return default;
    }

    internal static IEnumerable<MarkupNode> Children(MarkupNode n)
    {
        if (n.IsNull)
        {
            yield break;
        }

        for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element)
            {
                yield return c;
            }
        }
    }

    internal static IEnumerable<MarkupNode> Children(MarkupNode n, string local)
    {
        foreach (MarkupNode c in Children(n))
        {
            if (Equal(c.LocalName, local))
            {
                yield return c;
            }
        }
    }

    private static bool Equal(ReadOnlySpan<byte> bytes, ReadOnlySpan<char> s)
    {
        if (bytes.Length != s.Length)
        {
            return false;
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != s[i])
            {
                return false;
            }
        }

        return true;
    }

    internal static string? Val(MarkupNode n) => n.IsNull ? null : n.GetAttribute("val");

    internal static string? Val(MarkupNode parent, string child) => Val(Child(parent, child));

    internal static double? Dbl(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? d : null;

    internal static int? Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    internal static long? Lng(string? s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    // ST_OnOff / CT_Boolean: an element without val is true.
    internal static bool Bool(MarkupNode n, bool whenAbsent = false)
    {
        if (n.IsNull)
        {
            return whenAbsent;
        }

        string? v = n.GetAttribute("val");
        return v is null || v is "1" or "true" or "on";
    }

    // ---- DrawingML bits shared by both readers (ECMA-376 Part 1 §20.1.2.3 colours, §20.1.2.2.35 spPr, §21.1.2 text) ----

    internal static ChartColor? Color(MarkupNode holder)
    {
        foreach (MarkupNode c in Children(holder))
        {
            string kind;
            string value;
            switch (c.LocalNameString)
            {
                case "srgbClr":
                    kind = "srgb";
                    value = c.GetAttribute("val") ?? "000000";
                    break;
                case "schemeClr":
                    kind = "scheme";
                    value = c.GetAttribute("val") ?? "tx1";
                    break;
                case "sysClr":
                    kind = "sys";
                    value = c.GetAttribute("lastClr") ?? c.GetAttribute("val") ?? "000000";
                    break;
                case "prstClr":
                    kind = "prst";
                    value = c.GetAttribute("val") ?? "black";
                    break;
                case "scrgbClr":
                    kind = "srgb";
                    value = Scrgb(c);
                    break;
                case "hslClr":
                    kind = "srgb";
                    value = "808080";
                    break;
                default:
                    continue;
            }

            var color = new ChartColor { Kind = kind, Value = value };
            foreach (MarkupNode t in Children(c))
            {
                if (Dbl(t.GetAttribute("val")) is { } d)
                {
                    color.Transforms.Add(new KeyValuePair<string, double>(t.LocalNameString, d));
                }
            }

            return color;
        }

        return null;
    }

    private static string Scrgb(MarkupNode c)
    {
        static int Ch(double? v) => (int)Math.Round(Math.Clamp((v ?? 0) / 100000.0, 0, 1) * 255);
        return Ch(Dbl(c.GetAttribute("r"))).ToString("X2", CultureInfo.InvariantCulture)
            + Ch(Dbl(c.GetAttribute("g"))).ToString("X2", CultureInfo.InvariantCulture)
            + Ch(Dbl(c.GetAttribute("b"))).ToString("X2", CultureInfo.InvariantCulture);
    }

    private static ChartFill? ReadFill(MarkupNode spPr)
    {
        foreach (MarkupNode c in Children(spPr))
        {
            switch (c.LocalNameString)
            {
                case "noFill":
                    return new ChartFill { None = true };
                case "solidFill":
                    return new ChartFill { Solid = Color(c) };
                case "gradFill":
                    var g = new ChartFill();
                    foreach (MarkupNode gs in Children(Child(c, "gsLst"), "gs"))
                    {
                        g.Gradient.Add(new ChartGradientStop { Position = (Dbl(gs.GetAttribute("pos")) ?? 0) / 100000.0, Color = Color(gs) ?? new ChartColor() });
                    }

                    g.GradientAngle = (Dbl(Child(c, "lin").GetAttribute("ang")) ?? 0) / 60000.0;
                    if (g.Gradient.Count == 1)
                    {
                        g.Solid = g.Gradient[0].Color;
                        g.Gradient.Clear();
                    }

                    return g;
                case "pattFill":
                    var p = new ChartFill { Other = true };
                    p.Solid = Color(Child(c, "fgClr"));
                    return p;
                case "blipFill":
                    return new ChartFill { Other = true };
            }
        }

        return null;
    }

    internal static ChartShapeFormat? ShapeFormat(MarkupNode spPr)
    {
        if (spPr.IsNull)
        {
            return null;
        }

        var f = new ChartShapeFormat { Fill = ReadFill(spPr) };
        MarkupNode ln = Child(spPr, "ln");
        if (!ln.IsNull)
        {
            var line = new ChartLine { WidthEmu = Lng(ln.GetAttribute("w")) };
            ChartFill? lf = ReadFill(ln);
            if (lf is not null)
            {
                line.None = lf.None;
                line.Color = lf.Solid ?? (lf.Gradient.Count > 0 ? lf.Gradient[0].Color : null);
                line.FromGradient = lf.Gradient.Count > 0;
            }

            line.Dash = Val(ln, "prstDash");
            f.Line = line;
        }

        return f;
    }

    internal static ChartTextFormat? RunFormat(MarkupNode rPr, ChartTextFormat? into = null)
    {
        if (rPr.IsNull)
        {
            return into;
        }

        ChartTextFormat t = into ?? new ChartTextFormat();
        if (Dbl(rPr.GetAttribute("sz")) is { } sz)
        {
            t.SizePoints = sz / 100.0;
        }

        if (rPr.GetAttribute("b") is { } b)
        {
            t.Bold = b is "1" or "true";
        }

        if (rPr.GetAttribute("i") is { } i)
        {
            t.Italic = i is "1" or "true";
        }

        MarkupNode fill = Child(rPr, "solidFill");
        if (!fill.IsNull)
        {
            t.Color = Color(fill);
        }

        if (Child(rPr, "latin") is { IsNull: false } latin && latin.GetAttribute("typeface") is { Length: > 0 } face)
        {
            t.Font = face;
        }

        return t;
    }

    // c:txPr / c:rich share a:bodyPr, a:lstStyle and a:p; only the first paragraph's defaults matter for formatting.
    internal static ChartTextFormat? TextProperties(MarkupNode txPr)
    {
        if (txPr.IsNull)
        {
            return null;
        }

        var t = new ChartTextFormat();
        MarkupNode body = Child(txPr, "bodyPr");
        if (!body.IsNull)
        {
            if (Dbl(body.GetAttribute("rot")) is { } rot)
            {
                t.RotationDegrees = rot / 60000.0;
            }

            if (body.GetAttribute("vert") is { } v && v != "horz")
            {
                t.Vertical = true;
            }
        }

        MarkupNode p = Child(txPr, "p");
        RunFormat(Child(Child(p, "pPr"), "defRPr"), t);
        return t;
    }

    internal static ChartText? RichText(MarkupNode rich)
    {
        if (rich.IsNull)
        {
            return null;
        }

        var sb = new System.Text.StringBuilder();
        ChartTextFormat? fmt = TextProperties(rich);
        bool first = true;
        bool runFormatTaken = false;
        foreach (MarkupNode p in Children(rich, "p"))
        {
            if (!first)
            {
                sb.Append('\n');
            }

            first = false;
            foreach (MarkupNode r in Children(p))
            {
                string n = r.LocalNameString;
                if (n == "r")
                {
                    sb.Append(Child(r, "t").InnerText);
                    if (!runFormatTaken)
                    {
                        fmt = RunFormat(Child(r, "rPr"), fmt ?? new ChartTextFormat());
                        runFormatTaken = true;
                    }
                }
                else if (n == "fld")
                {
                    sb.Append(Child(r, "t").InnerText);
                }
                else if (n == "br")
                {
                    sb.Append('\n');
                }
            }
        }

        return new ChartText { Text = sb.ToString(), Format = fmt };
    }

    internal static ChartManualLayout? Layout(MarkupNode layout)
    {
        MarkupNode m = Child(layout, "manualLayout");
        if (m.IsNull)
        {
            return null;
        }

        return new ChartManualLayout
        {
            X = Dbl(Val(m, "x")),
            Y = Dbl(Val(m, "y")),
            Width = Dbl(Val(m, "w")),
            Height = Dbl(Val(m, "h")),
            XIsFactor = Val(m, "xMode") == "factor",
            YIsFactor = Val(m, "yMode") == "factor",
            InnerTarget = Val(m, "layoutTarget") == "inner",
        };
    }

    internal static void CollectExtensions(MarkupNode node, ChartSpace space)
    {
        // extLst items are kept as text for callers that inspect them; the part's original bytes remain the lossless form.
        foreach (MarkupNode c in Children(node))
        {
            if (c.LocalName.SequenceEqual("extLst"u8))
            {
                if (space.Extensions.Count < 64)
                {
                    space.Extensions.Add(c.OuterXml);
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------

    private sealed class ClassicReader(ChartSpace space)
    {
        public void Read(MarkupNode root)
        {
            CollectExtensions(root, space);
            foreach (MarkupNode c in Children(root))
            {
                switch (c.LocalNameString)
                {
                    case "date1904":
                        space.Date1904 = Bool(c);
                        break;
                    case "roundedCorners":
                        space.RoundedCorners = Bool(c, true);
                        break;
                    case "style":
                        space.Style = Int(Val(c)) ?? 2;
                        break;
                    case "AlternateContent":
                        // The c14:style in Choice is 100 + the style; the Fallback carries the c:style.
                        MarkupNode fb = Child(c, "Fallback");
                        if (Child(fb, "style") is { IsNull: false } st)
                        {
                            space.Style = Int(Val(st)) ?? 2;
                        }

                        break;
                    case "chart":
                        ReadChart(c);
                        break;
                    case "spPr":
                        space.ChartAreaFormat = ShapeFormat(c);
                        break;
                    case "txPr":
                        space.TextFormat = TextProperties(c);
                        break;
                    case "externalData":
                        space.ExternalDataRelationshipId = c.GetAttribute("http://schemas.openxmlformats.org/officeDocument/2006/relationships", "id") ?? c.GetAttribute("id");
                        break;
                }
            }

            if (space.Style is < 1 or > 48)
            {
                space.Style = space.Style is >= 101 and <= 148 ? space.Style - 100 : 2;
            }
        }

        private void ReadChart(MarkupNode chart)
        {
            CollectExtensions(chart, space);
            foreach (MarkupNode c in Children(chart))
            {
                switch (c.LocalNameString)
                {
                    case "title":
                        space.Title = Title(c);
                        break;
                    case "autoTitleDeleted":
                        space.AutoTitleDeleted = Bool(c);
                        break;
                    case "view3D":
                        space.Has3DView = true;
                        break;
                    case "plotArea":
                        ReadPlotArea(c);
                        break;
                    case "legend":
                        space.Legend = Legend(c);
                        break;
                    case "plotVisOnly":
                        space.PlotVisibleOnly = Bool(c, true);
                        break;
                    case "dispBlanksAs":
                        space.BlanksAs = Val(c) switch { "zero" => ChartBlanksAs.Zero, "span" => ChartBlanksAs.Span, _ => ChartBlanksAs.Gap };
                        break;
                }
            }
        }

        private static ChartTitle Title(MarkupNode t)
        {
            var title = new ChartTitle { Overlay = Bool(Child(t, "overlay")), Layout = Layout(Child(t, "layout")), Format = ShapeFormat(Child(t, "spPr")), TextFormat = TextProperties(Child(t, "txPr")) };
            MarkupNode tx = Child(t, "tx");
            if (!tx.IsNull)
            {
                MarkupNode rich = Child(tx, "rich");
                if (!rich.IsNull)
                {
                    title.Text = RichText(rich);
                }
                else
                {
                    title.Text = TextOfRef(tx);
                }
            }

            return title;
        }

        // c:tx holding strRef (cell reference with cache) or v.
        private static ChartText? TextOfRef(MarkupNode tx)
        {
            MarkupNode sr = Child(tx, "strRef");
            if (!sr.IsNull)
            {
                string? f = Child(sr, "f").InnerText;
                string? v = null;
                MarkupNode pt = Child(Child(Child(sr, "strCache"), "pt"), "v");
                if (!pt.IsNull)
                {
                    v = pt.InnerText;
                }

                return new ChartText { Text = v, Reference = string.IsNullOrEmpty(f) ? null : f };
            }

            MarkupNode vn = Child(tx, "v");
            return vn.IsNull ? null : new ChartText { Text = vn.InnerText };
        }

        private static ChartLegend Legend(MarkupNode l)
        {
            var legend = new ChartLegend
            {
                Position = Val(l, "legendPos") switch { "t" => ChartLegendPosition.Top, "b" => ChartLegendPosition.Bottom, "l" => ChartLegendPosition.Left, "tr" => ChartLegendPosition.TopRight, _ => ChartLegendPosition.Right },
                Overlay = Bool(Child(l, "overlay")),
                Layout = Layout(Child(l, "layout")),
                Format = ShapeFormat(Child(l, "spPr")),
                TextFormat = TextProperties(Child(l, "txPr")),
            };
            foreach (MarkupNode e in Children(l, "legendEntry"))
            {
                if (Bool(Child(e, "delete")) && Int(Val(e, "idx")) is { } idx)
                {
                    legend.DeletedEntries.Add(idx);
                }
            }

            return legend;
        }

        private void ReadPlotArea(MarkupNode plot)
        {
            CollectExtensions(plot, space);
            foreach (MarkupNode c in Children(plot))
            {
                string n = c.LocalNameString;
                switch (n)
                {
                    case "layout":
                        space.PlotAreaLayout = Layout(c);
                        break;
                    case "spPr":
                        space.PlotAreaFormat = ShapeFormat(c);
                        break;
                    case "catAx":
                    case "valAx":
                    case "dateAx":
                    case "serAx":
                        space.Axes.Add(Axis(c, n));
                        break;
                    case "dTable":
                        space.Notes.Add(new ChartNote("chart.data-table", "The chart's data table is not drawn."));
                        break;
                    default:
                        if (n.EndsWith("Chart", StringComparison.Ordinal))
                        {
                            ChartGroup? g = Group(c, n);
                            if (g is not null)
                            {
                                space.Groups.Add(g);
                            }
                        }

                        break;
                }
            }

            // A group is secondary when it plots on axes other than those of the first group.
            if (space.Groups.Count > 0)
            {
                HashSet<long> primary = [.. space.Groups[0].AxisIds];
                foreach (ChartGroup g in space.Groups)
                {
                    g.IsSecondary = g.AxisIds.Count > 0 && primary.Count > 0 && !g.AxisIds.Any(primary.Contains);
                }
            }
        }

        private ChartGroup? Group(MarkupNode c, string name)
        {
            var g = new ChartGroup();
            bool is3D = name.EndsWith("3DChart", StringComparison.Ordinal);
            switch (name)
            {
                case "barChart":
                case "bar3DChart":
                    g.Type = Val(c, "barDir") == "bar" ? ChartType.Bar : ChartType.Column;
                    break;
                case "lineChart":
                case "line3DChart":
                    g.Type = ChartType.Line;
                    break;
                case "areaChart":
                case "area3DChart":
                    g.Type = ChartType.Area;
                    break;
                case "pieChart":
                case "pie3DChart":
                    g.Type = ChartType.Pie;
                    break;
                case "ofPieChart":
                    g.Type = ChartType.Pie;
                    space.Notes.Add(new ChartNote("chart.of-pie", "A pie-of-pie or bar-of-pie chart is drawn as a single pie."));
                    break;
                case "doughnutChart":
                    g.Type = ChartType.Doughnut;
                    break;
                case "scatterChart":
                    g.Type = ChartType.Scatter;
                    break;
                case "bubbleChart":
                    g.Type = ChartType.Bubble;
                    break;
                case "radarChart":
                    g.Type = ChartType.Radar;
                    break;
                case "stockChart":
                    g.Type = ChartType.Stock;
                    break;
                case "surfaceChart":
                case "surface3DChart":
                    g.Type = ChartType.Surface;
                    break;
                default:
                    space.Notes.Add(new ChartNote("chart.group-skipped", "The chart group " + name + " is not known."));
                    return null;
            }

            if (is3D)
            {
                space.Has3DView = true;
                space.Notes.Add(new ChartNote("chart.3d-flattened", "The 3-D chart " + name + " is drawn flat."));
            }

            g.Grouping = Val(c, "grouping") switch { "stacked" => ChartGrouping.Stacked, "percentStacked" => ChartGrouping.PercentStacked, _ => ChartGrouping.Standard };
            g.VaryColors = Bool(Child(c, "varyColors"), g.Type is ChartType.Pie or ChartType.Doughnut);
            g.GapWidth = Int(Val(c, "gapWidth")) ?? 150;
            g.Overlap = Int(Val(c, "overlap")) ?? (g.Grouping != ChartGrouping.Standard ? 100 : 0);
            g.HoleSize = Int(Val(c, "holeSize")) ?? 50;
            g.FirstSliceAngle = Int(Val(c, "firstSliceAng")) ?? 0;
            g.BubbleScale = Int(Val(c, "bubbleScale")) ?? 100;
            g.Style = Val(c, "scatterStyle") ?? Val(c, "radarStyle");
            g.ShowMarkers = Bool(Child(c, "marker"), true);
            g.HasUpDownBars = !Child(c, "upDownBars").IsNull;
            g.HasHighLowLines = !Child(c, "hiLowLines").IsNull;
            foreach (MarkupNode ch in Children(c))
            {
                switch (ch.LocalNameString)
                {
                    case "ser":
                        g.Series.Add(Series(ch, g));
                        break;
                    case "axId":
                        if (Lng(Val(ch)) is { } id)
                        {
                            g.AxisIds.Add(id);
                        }

                        break;
                    case "dLbls":
                        g.Labels = Labels(ch);
                        break;
                }
            }

            return g;
        }

        private static ChartSeries Series(MarkupNode ser, ChartGroup group)
        {
            var s = new ChartSeries
            {
                Index = Int(Val(ser, "idx")) ?? 0,
                Order = Int(Val(ser, "order")) ?? 0,
                Format = ShapeFormat(Child(ser, "spPr")),
                Smooth = Bool(Child(ser, "smooth")),
                Explosion = Int(Val(ser, "explosion")) ?? 0,
                InvertIfNegative = Bool(Child(ser, "invertIfNegative")),
            };
            MarkupNode tx = Child(ser, "tx");
            if (!tx.IsNull)
            {
                s.Name = TextOfRef(tx);
            }

            foreach (MarkupNode c in Children(ser))
            {
                switch (c.LocalNameString)
                {
                    case "cat":
                        s.Categories = Data(c);
                        break;
                    case "val":
                        s.Values = Data(c);
                        break;
                    case "xVal":
                        s.XValues = Data(c);
                        break;
                    case "yVal":
                        s.Values = Data(c);
                        break;
                    case "bubbleSize":
                        s.BubbleSizes = Data(c);
                        break;
                    case "marker":
                        s.Marker = Marker(c);
                        break;
                    case "dLbls":
                        s.Labels = Labels(c);
                        break;
                    case "dPt":
                        s.Points.Add(new ChartDataPoint
                        {
                            Index = Int(Val(c, "idx")) ?? 0,
                            Format = ShapeFormat(Child(c, "spPr")),
                            Explosion = Int(Val(c, "explosion")),
                            InvertIfNegative = Bool(Child(c, "invertIfNegative")),
                            Marker = Child(c, "marker") is { IsNull: false } m ? Marker(m) : null,
                        });
                        break;
                    case "trendline":
                        s.Trendlines.Add(new ChartTrendline
                        {
                            Type = Val(c, "trendlineType") ?? "linear",
                            Order = Int(Val(c, "order")) ?? Int(Val(c, "period")) ?? 2,
                            Forward = Dbl(Val(c, "forward")) ?? 0,
                            Backward = Dbl(Val(c, "backward")) ?? 0,
                            Format = ShapeFormat(Child(c, "spPr")),
                        });
                        break;
                    case "errBars":
                        s.ErrorBars.Add(ErrBars(c));
                        break;
                }
            }

            return s;
        }

        private static ChartErrorBars ErrBars(MarkupNode c)
        {
            var e = new ChartErrorBars
            {
                Direction = Val(c, "errDir") ?? "y",
                BarType = Val(c, "errBarType") ?? "both",
                ValueType = Val(c, "errValType") ?? "fixedVal",
                Value = Dbl(Val(c, "val")) ?? 0,
                EndCap = !(Val(c, "noEndCap") is "1" or "true"),
                Format = ShapeFormat(Child(c, "spPr")),
            };
            Fill(Child(c, "plus"), e.Plus);
            Fill(Child(c, "minus"), e.Minus);
            return e;

            static void Fill(MarkupNode holder, List<double?> into)
            {
                ChartData? d = Data(holder);
                if (d is { IsNumeric: true })
                {
                    into.AddRange(d.Numbers);
                }
            }
        }

        private static ChartMarker Marker(MarkupNode m) => new()
        {
            Symbol = Val(m, "symbol") ?? "auto",
            Size = Math.Clamp(Int(Val(m, "size")) ?? 5, 2, 72),
            Format = ShapeFormat(Child(m, "spPr")),
        };

        private static ChartDataLabels Labels(MarkupNode d)
        {
            var l = new ChartDataLabels
            {
                Deleted = Bool(Child(d, "delete")),
                ShowValue = Bool(Child(d, "showVal")),
                ShowCategoryName = Bool(Child(d, "showCatName")),
                ShowSeriesName = Bool(Child(d, "showSerName")),
                ShowPercent = Bool(Child(d, "showPercent")),
                ShowLegendKey = Bool(Child(d, "showLegendKey")),
                Position = Val(d, "dLblPos"),
                Separator = Child(d, "separator") is { IsNull: false } sep ? sep.InnerText : null,
                TextFormat = TextProperties(Child(d, "txPr")),
                Format = ShapeFormat(Child(d, "spPr")),
            };
            MarkupNode nf = Child(d, "numFmt");
            if (!nf.IsNull)
            {
                l.FormatCode = nf.GetAttribute("formatCode");
                l.SourceLinked = nf.GetAttribute("sourceLinked") is "1" or "true";
            }

            return l;
        }

        // c:cat, c:val, c:xVal ... : numRef/numLit/strRef/strLit/multiLvlStrRef.
        internal static ChartData? Data(MarkupNode holder)
        {
            if (holder.IsNull)
            {
                return null;
            }

            foreach (MarkupNode c in Children(holder))
            {
                string n = c.LocalNameString;
                var d = new ChartData();
                if (n is "numRef" or "numLit")
                {
                    d.IsNumeric = true;
                    d.Reference = n == "numRef" ? Child(c, "f").InnerText : null;
                    MarkupNode cache = n == "numRef" ? Child(c, "numCache") : c;
                    d.FormatCode = Child(cache, "formatCode") is { IsNull: false } fc ? fc.InnerText : null;
                    int count = Math.Clamp(Int(Val(cache, "ptCount")) ?? 0, 0, MaxPoints);
                    for (int i = 0; i < count; i++)
                    {
                        d.Numbers.Add(null);
                    }

                    foreach (MarkupNode pt in Children(cache, "pt"))
                    {
                        if (Int(pt.GetAttribute("idx")) is { } idx && idx >= 0 && idx < MaxPoints)
                        {
                            while (d.Numbers.Count <= idx)
                            {
                                d.Numbers.Add(null);
                            }

                            d.Numbers[idx] = Dbl(Child(pt, "v").InnerText.Trim());
                        }
                    }

                    return d;
                }

                if (n is "strRef" or "strLit")
                {
                    d.Reference = n == "strRef" ? Child(c, "f").InnerText : null;
                    ReadStrings(n == "strRef" ? Child(c, "strCache") : c, d.Strings);
                    return d;
                }

                if (n == "multiLvlStrRef")
                {
                    d.Reference = Child(c, "f").InnerText;
                    MarkupNode cache = Child(c, "multiLvlStrCache");
                    int count = Math.Clamp(Int(Val(cache, "ptCount")) ?? 0, 0, MaxPoints);
                    var levels = new List<List<string?>>();
                    foreach (MarkupNode lvl in Children(cache, "lvl"))
                    {
                        var list = new List<string?>();
                        for (int i = 0; i < count; i++)
                        {
                            list.Add(null);
                        }

                        foreach (MarkupNode pt in Children(lvl, "pt"))
                        {
                            if (Int(pt.GetAttribute("idx")) is { } idx && idx >= 0 && idx < MaxPoints)
                            {
                                while (list.Count <= idx)
                                {
                                    list.Add(null);
                                }

                                list[idx] = Child(pt, "v").InnerText;
                            }
                        }

                        levels.Add(list);
                    }

                    if (levels.Count > 0)
                    {
                        d.Strings.AddRange(levels[0]);
                        d.OuterLevels.AddRange(levels.Skip(1));
                    }

                    return d;
                }
            }

            return null;
        }

        private static void ReadStrings(MarkupNode cache, List<string?> into)
        {
            int count = Math.Clamp(Int(Val(cache, "ptCount")) ?? 0, 0, MaxPoints);
            for (int i = 0; i < count; i++)
            {
                into.Add(null);
            }

            foreach (MarkupNode pt in Children(cache, "pt"))
            {
                if (Int(pt.GetAttribute("idx")) is { } idx && idx >= 0 && idx < MaxPoints)
                {
                    while (into.Count <= idx)
                    {
                        into.Add(null);
                    }

                    into[idx] = Child(pt, "v").InnerText;
                }
            }
        }

        private static ChartAxis Axis(MarkupNode a, string name)
        {
            var ax = new ChartAxis
            {
                Kind = name switch { "valAx" => ChartAxisKind.Value, "dateAx" => ChartAxisKind.Date, "serAx" => ChartAxisKind.Series, _ => ChartAxisKind.Category },
                Id = Lng(Val(a, "axId")) ?? 0,
                CrossAxisId = Lng(Val(a, "crossAx")) ?? 0,
                Deleted = Bool(Child(a, "delete")),
                Position = Val(a, "axPos") switch { "l" => ChartAxisPosition.Left, "t" => ChartAxisPosition.Top, "r" => ChartAxisPosition.Right, _ => ChartAxisPosition.Bottom },
                MajorUnit = Dbl(Val(a, "majorUnit")),
                MinorUnit = Dbl(Val(a, "minorUnit")),
                Crosses = Val(a, "crosses") ?? "autoZero",
                CrossesAt = Dbl(Val(a, "crossesAt")),
                MajorTickMark = Val(a, "majorTickMark") ?? "cross",
                MinorTickMark = Val(a, "minorTickMark") ?? "cross",
                TickLabelPosition = Val(a, "tickLblPos") ?? "nextTo",
                TickLabelSkip = Int(Val(a, "tickLblSkip")),
                TickMarkSkip = Int(Val(a, "tickMarkSkip")),
                CrossBetween = Val(a, "crossBetween") != "midCat",
                BaseTimeUnit = Val(a, "baseTimeUnit"),
                DisplayUnit = Val(Child(a, "dispUnits"), "builtInUnit"),
                Format = ShapeFormat(Child(a, "spPr")),
                TextFormat = TextProperties(Child(a, "txPr")),
            };
            MarkupNode sc = Child(a, "scaling");
            ax.Min = Dbl(Val(sc, "min"));
            ax.Max = Dbl(Val(sc, "max"));
            ax.LogBase = Dbl(Val(sc, "logBase"));
            ax.Reverse = Val(sc, "orientation") == "maxMin";
            MarkupNode mg = Child(a, "majorGridlines");
            ax.MajorGridlines = !mg.IsNull;
            ax.MajorGridlineFormat = ShapeFormat(Child(mg, "spPr"));
            MarkupNode ng = Child(a, "minorGridlines");
            ax.MinorGridlines = !ng.IsNull;
            ax.MinorGridlineFormat = ShapeFormat(Child(ng, "spPr"));
            MarkupNode nf = Child(a, "numFmt");
            if (!nf.IsNull)
            {
                ax.FormatCode = nf.GetAttribute("formatCode");
                ax.SourceLinked = nf.GetAttribute("sourceLinked") is "1" or "true";
            }

            MarkupNode t = Child(a, "title");
            if (!t.IsNull)
            {
                ax.Title = Title(t);
            }

            return ax;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------

    private sealed class ExtendedReader(ChartSpace space)
    {
        private readonly Dictionary<string, ExData> _data = [];

        private sealed class ExData
        {
            public ChartData? Categories;
            public ChartData? Values;
        }

        public void Read(MarkupNode root)
        {
            CollectExtensions(root, space);
            MarkupNode cd = Child(root, "chartData");
            foreach (MarkupNode d in Children(cd, "data"))
            {
                string id = d.GetAttribute("id") ?? string.Empty;
                var ex = new ExData();
                foreach (MarkupNode dim in Children(d))
                {
                    string n = dim.LocalNameString;
                    if (n is not ("strDim" or "numDim"))
                    {
                        continue;
                    }

                    ChartData data = Dim(dim, n == "numDim");
                    if (n == "strDim")
                    {
                        ex.Categories ??= data;
                    }
                    else
                    {
                        ex.Values ??= data;
                    }
                }

                _data[id] = ex;
            }

            MarkupNode chart = Child(root, "chart");
            MarkupNode title = Child(chart, "title");
            if (!title.IsNull)
            {
                space.Title = ExTitle(title);
            }

            MarkupNode plot = Child(chart, "plotArea");
            MarkupNode region = Child(plot, "plotAreaRegion");
            ReadSeries(region);
            foreach (MarkupNode ax in Children(plot, "axis"))
            {
                space.Axes.Add(ExAxis(ax));
            }

            MarkupNode lg = Child(chart, "legend");
            if (!lg.IsNull)
            {
                space.Legend = new ChartLegend
                {
                    Position = lg.GetAttribute("pos") switch { "t" => ChartLegendPosition.Top, "b" => ChartLegendPosition.Bottom, "l" => ChartLegendPosition.Left, "tr" => ChartLegendPosition.TopRight, _ => ChartLegendPosition.Right },
                    Overlay = lg.GetAttribute("overlay") is "1" or "true",
                    TextFormat = TextProperties(Child(lg, "txPr")),
                    Format = ShapeFormat(Child(lg, "spPr")),
                };
            }

            space.ChartAreaFormat = ShapeFormat(Child(root, "spPr"));
            space.TextFormat = TextProperties(Child(root, "txPr"));
            space.PlotAreaFormat = ShapeFormat(Child(Child(plot, "plotSurface"), "spPr"));
        }

        private static ChartTitle ExTitle(MarkupNode t)
        {
            var title = new ChartTitle { Overlay = t.GetAttribute("overlay") is "1" or "true", Format = ShapeFormat(Child(t, "spPr")), TextFormat = TextProperties(Child(t, "txPr")) };
            MarkupNode tx = Child(t, "tx");
            MarkupNode rich = Child(tx, "rich");
            if (!rich.IsNull)
            {
                title.Text = RichText(rich);
            }
            else if (Child(Child(tx, "txData"), "v") is { IsNull: false } v)
            {
                title.Text = new ChartText { Text = v.InnerText };
            }

            return title;
        }

        // cx:strDim / cx:numDim: cx:f, then cx:lvl (ptCount, formatCode, name) with cx:pt idx.
        private static ChartData Dim(MarkupNode dim, bool numeric)
        {
            var d = new ChartData { IsNumeric = numeric, Reference = Child(dim, "f").InnerText };
            bool firstLevel = true;
            foreach (MarkupNode lvl in Children(dim, "lvl"))
            {
                int count = Math.Clamp(Int(lvl.GetAttribute("ptCount")) ?? 0, 0, MaxPoints);
                var strings = new List<string?>();
                var nums = new List<double?>();
                for (int i = 0; i < count; i++)
                {
                    strings.Add(null);
                    nums.Add(null);
                }

                foreach (MarkupNode pt in Children(lvl, "pt"))
                {
                    if (Int(pt.GetAttribute("idx")) is { } idx && idx >= 0 && idx < MaxPoints)
                    {
                        while (strings.Count <= idx)
                        {
                            strings.Add(null);
                            nums.Add(null);
                        }

                        string text = pt.InnerText;
                        strings[idx] = text;
                        nums[idx] = Dbl(text.Trim());
                    }
                }

                if (firstLevel)
                {
                    d.FormatCode = lvl.GetAttribute("formatCode");
                    if (numeric)
                    {
                        d.Numbers.AddRange(nums);
                    }
                    else
                    {
                        d.Strings.AddRange(strings);
                    }

                    firstLevel = false;
                }
                else
                {
                    d.OuterLevels.Add(strings);
                }
            }

            return d;
        }

        private void ReadSeries(MarkupNode region)
        {
            ChartGroup? group = null;
            foreach (MarkupNode ser in Children(region, "series"))
            {
                string layout = ser.GetAttribute("layoutId") ?? string.Empty;
                ChartType type = layout switch
                {
                    "waterfall" => ChartType.Waterfall,
                    "sunburst" => ChartType.Sunburst,
                    "treemap" => ChartType.Treemap,
                    "clusteredColumn" => ChartType.Histogram,
                    "paretoLine" => ChartType.Pareto,
                    "boxWhisker" => ChartType.BoxWhisker,
                    "funnel" => ChartType.Funnel,
                    "regionMap" => ChartType.RegionMap,
                    _ => ChartType.Unknown,
                };
                if (type == ChartType.Pareto && group is not null)
                {
                    group.Type = ChartType.Pareto;
                }
                else if (group is null)
                {
                    group = new ChartGroup { Type = type };
                    space.Groups.Add(group);
                    if (type == ChartType.RegionMap)
                    {
                        group.Note = "Region maps need map tiles and are shown as a placeholder.";
                        space.Notes.Add(new ChartNote("chart.region-map", "A region map is drawn as a placeholder."));
                    }
                    else if (type == ChartType.Unknown)
                    {
                        group.Note = "Chart layout '" + layout + "' is not known.";
                        space.Notes.Add(new ChartNote("chart.layout-unknown", "The chartex layout '" + layout + "' is not known."));
                    }
                }

                var s = new ChartSeries
                {
                    Index = group.Series.Count,
                    Order = group.Series.Count,
                    Format = ShapeFormat(Child(ser, "spPr")),
                    Hidden = ser.GetAttribute("hidden") is "1" or "true",
                    IsParetoLine = type == ChartType.Pareto,
                };
                MarkupNode tx = Child(Child(ser, "tx"), "txData");
                if (!tx.IsNull)
                {
                    s.Name = new ChartText { Text = Child(tx, "v").InnerText, Reference = Child(tx, "f") is { IsNull: false } f ? f.InnerText : null };
                }

                string? dataId = Val(ser, "dataId");
                if (dataId is not null && _data.TryGetValue(dataId, out ExData? data))
                {
                    s.Categories = data.Categories;
                    s.Values = data.Values;
                }

                foreach (MarkupNode dp in Children(ser, "dataPt"))
                {
                    s.Points.Add(new ChartDataPoint { Index = Int(dp.GetAttribute("idx")) ?? 0, Format = ShapeFormat(Child(dp, "spPr")) });
                }

                MarkupNode dl = Child(ser, "dataLabels");
                if (!dl.IsNull)
                {
                    s.Labels = new ChartDataLabels
                    {
                        Position = dl.GetAttribute("pos"),
                        ShowValue = Child(dl, "visibility") is { IsNull: false } vis ? vis.GetAttribute("value") is not ("0" or "false") : true,
                        FormatCode = Child(dl, "numFmt") is { IsNull: false } nf ? nf.GetAttribute("formatCode") : null,
                        TextFormat = TextProperties(Child(dl, "txPr")),
                    };
                    if (Child(dl, "visibility") is { IsNull: false } v2)
                    {
                        s.Labels.ShowCategoryName = v2.GetAttribute("categoryName") is "1" or "true";
                        s.Labels.ShowSeriesName = v2.GetAttribute("seriesName") is "1" or "true";
                        s.Labels.ShowValue = v2.GetAttribute("value") is not ("0" or "false");
                    }
                }

                MarkupNode lp = Child(ser, "layoutPr");
                foreach (MarkupNode st in Children(Child(lp, "subtotals"), "idx"))
                {
                    if (Int(st.GetAttribute("val")) is { } idx)
                    {
                        s.Points.Add(new ChartDataPoint { Index = idx, IsSubtotal = true });
                    }
                }

                MarkupNode binning = Child(lp, "binning");
                if (!binning.IsNull)
                {
                    group.BinSize = Dbl(Val(binning, "binSize"));
                    group.BinCount = Int(Val(binning, "binCount"));
                    if (type == ChartType.Histogram || type == ChartType.Pareto)
                    {
                        group.Type = type == ChartType.Pareto ? ChartType.Pareto : ChartType.Histogram;
                    }
                }

                MarkupNode stats = Child(lp, "statistics");
                if (!stats.IsNull)
                {
                    group.ExclusiveQuartiles = stats.GetAttribute("quartileMethod") != "inclusive";
                }

                MarkupNode vis3 = Child(lp, "visibility");
                if (!vis3.IsNull)
                {
                    group.ShowMeanMarker = vis3.GetAttribute("meanMarker") is not ("0" or "false");
                    group.ShowOutliers = vis3.GetAttribute("outliers") is not ("0" or "false");
                    group.ShowConnectorLines = vis3.GetAttribute("connectorLines") is not ("0" or "false");
                }

                group.Series.Add(s);
            }
        }

        private static ChartAxis ExAxis(MarkupNode a)
        {
            var ax = new ChartAxis
            {
                Id = Lng(a.GetAttribute("id")) ?? 0,
                Deleted = a.GetAttribute("hidden") is "1" or "true",
                Format = ShapeFormat(Child(a, "spPr")),
                TextFormat = TextProperties(Child(a, "txPr")),
                MajorGridlines = !Child(a, "majorGridlines").IsNull,
                MajorGridlineFormat = ShapeFormat(Child(Child(a, "majorGridlines"), "spPr")),
                MinorGridlines = !Child(a, "minorGridlines").IsNull,
            };
            MarkupNode val = Child(a, "valScaling");
            if (!val.IsNull)
            {
                ax.Kind = ChartAxisKind.Value;
                ax.Position = ChartAxisPosition.Left;
                ax.Min = Dbl(val.GetAttribute("min"));
                ax.Max = Dbl(val.GetAttribute("max"));
                ax.MajorUnit = Dbl(val.GetAttribute("majorUnit"));
                ax.MinorUnit = Dbl(val.GetAttribute("minorUnit"));
            }
            else
            {
                ax.Kind = ChartAxisKind.Category;
                ax.Position = ChartAxisPosition.Bottom;
            }

            MarkupNode nf = Child(a, "numFmt");
            if (!nf.IsNull)
            {
                ax.FormatCode = nf.GetAttribute("formatCode");
                ax.SourceLinked = nf.GetAttribute("sourceLinked") is "1" or "true";
            }

            MarkupNode t = Child(a, "title");
            if (!t.IsNull)
            {
                ax.Title = ExTitle(t);
            }

            return ax;
        }
    }
}
