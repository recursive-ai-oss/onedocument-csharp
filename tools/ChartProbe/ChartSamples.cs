using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Tools.ChartProbe;

/// <summary>
/// Chart parts written from nothing, one per chart type and feature, for the tests, the renderer sweep and the Office comparison.
/// Every part is classic (<c>c:chartSpace</c>) or chartex (<c>cx:chartSpace</c>) XML in the shape Excel writes.
/// </summary>
public static class ChartSamples
{
    private const string C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    /// <summary>One generated chart part.</summary>
    /// <param name="Name">File-name-safe case name.</param>
    /// <param name="Xml">The part.</param>
    /// <param name="IsExtended">True for chartex.</param>
    public sealed record Sample(string Name, string Xml, bool IsExtended);

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun"];
    private static readonly double[] V1 = [12, 19, 7, 15, 22, 9];
    private static readonly double[] V2 = [8, 14, 18, 11, 6, 17];
    private static readonly double[] V3 = [-5, 9, -12, 14, 3, -7];

    private static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static string Cat(IEnumerable<string> items, string f = "Sheet1!$A$2:$A$7")
    {
        string[] a = [.. items];
        var sb = new StringBuilder($"<c:cat><c:strRef><c:f>{f}</c:f><c:strCache><c:ptCount val=\"{a.Length}\"/>");
        for (int i = 0; i < a.Length; i++)
        {
            sb.Append($"<c:pt idx=\"{i}\"><c:v>{System.Security.SecurityElement.Escape(a[i])}</c:v></c:pt>");
        }

        return sb.Append("</c:strCache></c:strRef></c:cat>").ToString();
    }

    private static string Num(string tag, IEnumerable<double> items, string f, string fmt = "General")
    {
        double[] a = [.. items];
        var sb = new StringBuilder($"<c:{tag}><c:numRef><c:f>{f}</c:f><c:numCache><c:formatCode>{fmt}</c:formatCode><c:ptCount val=\"{a.Length}\"/>");
        for (int i = 0; i < a.Length; i++)
        {
            sb.Append($"<c:pt idx=\"{i}\"><c:v>{F(a[i])}</c:v></c:pt>");
        }

        return sb.Append($"</c:numCache></c:numRef></c:{tag}>").ToString();
    }

    private static string Ser(int idx, string name, string body, string head = "") =>
        $"<c:ser><c:idx val=\"{idx}\"/><c:order val=\"{idx}\"/><c:tx><c:strRef><c:f>Sheet1!${(char)('B' + idx)}$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>{name}</c:v></c:pt></c:strCache></c:strRef></c:tx>{head}{body}</c:ser>";

    private static string CatSer(int idx, string name, double[] v, string head = "", string tail = "") =>
        Ser(idx, name, Cat(Months) + Num("val", v, $"Sheet1!${(char)('B' + idx)}$2:${(char)('B' + idx)}$7") + tail, head);

    private const string Axes = "<c:axId val=\"111\"/><c:axId val=\"222\"/>";

    private static string CatAx(string extra = "", string pos = "b", string cross = "autoZero") =>
        $"<c:catAx><c:axId val=\"111\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"{pos}\"/>{extra}<c:numFmt formatCode=\"General\" sourceLinked=\"1\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"222\"/><c:crosses val=\"{cross}\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>";

    private static string ValAx(string scaling = "", string extra = "", string pos = "l", string cross = "autoZero", string between = "between", string fmt = "General", long id = 222, long crossAx = 111, string delete = "0", string grid = "<c:majorGridlines/>") =>
        $"<c:valAx><c:axId val=\"{id}\"/><c:scaling><c:orientation val=\"minMax\"/>{scaling}</c:scaling><c:delete val=\"{delete}\"/><c:axPos val=\"{pos}\"/>{grid}{extra}<c:numFmt formatCode=\"{fmt}\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"{crossAx}\"/><c:crosses val=\"{cross}\"/><c:crossBetween val=\"{between}\"/></c:valAx>";

    private static string Title(string text) =>
        $"<c:title><c:tx><c:rich><a:bodyPr/><a:p><a:pPr><a:defRPr sz=\"1400\" b=\"0\"/></a:pPr><a:r><a:rPr lang=\"en-US\" sz=\"1400\"/><a:t>{text}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>";

    private const string Legend = "<c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend>";

    /// <summary>Wraps plot-area content into a complete classic chart part.</summary>
    /// <param name="groups">Chart group elements.</param>
    /// <param name="axes">Axis elements.</param>
    /// <param name="title">Title text or null.</param>
    /// <param name="legend">Legend XML or empty.</param>
    /// <param name="plotLayout">Plot area layout XML or empty.</param>
    /// <returns>The XML.</returns>
    public static string Classic(string groups, string axes, string? title = null, string legend = "", string plotLayout = "<c:layout/>") =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<c:chartSpace xmlns:c=\"{C}\" xmlns:a=\"{A}\" xmlns:r=\"{R}\"><c:date1904 val=\"0\"/><c:roundedCorners val=\"0\"/><c:chart>{(title is null ? "<c:autoTitleDeleted val=\"1\"/>" : Title(title) + "<c:autoTitleDeleted val=\"0\"/>")}<c:plotArea>{plotLayout}{groups}{axes}</c:plotArea>{legend}<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart></c:chartSpace>";

    private static string Bar(string dir, string grouping, string extraSer = "", int overlap = 0, double[]? second = null) =>
        $"<c:barChart><c:barDir val=\"{dir}\"/><c:grouping val=\"{grouping}\"/><c:varyColors val=\"0\"/>{CatSer(0, "North", V1, tail: extraSer)}{CatSer(1, "South", second ?? V2)}<c:gapWidth val=\"100\"/>{(overlap != 0 ? $"<c:overlap val=\"{overlap}\"/>" : string.Empty)}{Axes}</c:barChart>";

    private static string Line(string grouping, string marker, bool smooth = false, string serHead = "") =>
        $"<c:lineChart><c:grouping val=\"{grouping}\"/><c:varyColors val=\"0\"/>{CatSer(0, "North", V1, serHead, smooth ? "<c:smooth val=\"1\"/>" : "<c:smooth val=\"0\"/>")}{CatSer(1, "South", V2, serHead, smooth ? "<c:smooth val=\"1\"/>" : "<c:smooth val=\"0\"/>")}<c:marker val=\"1\"/>{Axes}</c:lineChart>".Replace("<c:marker val=\"1\"/>", marker);

    private static string Pie(string extra = "", string head = "") =>
        $"<c:pieChart><c:varyColors val=\"1\"/>{Ser(0, "Share", Cat(Months) + Num("val", V1, "Sheet1!$B$2:$B$7"), head)}{extra}<c:firstSliceAng val=\"0\"/></c:pieChart>";

    private static string XY(string tag, string style, string ser) => $"<c:{tag}>{(tag == "scatterChart" ? $"<c:scatterStyle val=\"{style}\"/>" : string.Empty)}<c:varyColors val=\"0\"/>{ser}{Axes}</c:{tag}>";

    private static readonly double[] Xs = [1, 2, 3.5, 4, 6, 7.5];

    private static string XYSer(int idx, string name, double[] y, string head = "", string tail = "") =>
        Ser(idx, name, Num("xVal", Xs, "Sheet1!$A$2:$A$7") + Num("yVal", y, $"Sheet1!${(char)('B' + idx)}$2:${(char)('B' + idx)}$7") + tail, head);

    private static string Line0(string color) => $"<c:spPr><a:ln w=\"28575\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill></a:ln></c:spPr>";

    private const string NoLine = "<c:spPr><a:ln w=\"19050\"><a:noFill/></a:ln></c:spPr>";

    private static string DLbls(string pos = "outEnd") =>
        $"<c:dLbls><c:spPr><a:noFill/></c:spPr><c:dLblPos val=\"{pos}\"/><c:showLegendKey val=\"0\"/><c:showVal val=\"1\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/></c:dLbls>";

    /// <summary>All classic and chartex samples.</summary>
    /// <returns>The samples in a stable order.</returns>
    public static IReadOnlyList<Sample> All()
    {
        var list = new List<Sample>();
        void Add(string name, string xml, bool ex = false) => list.Add(new Sample(name, xml, ex));

        Add("column-clustered", Classic(Bar("col", "clustered"), CatAx() + ValAx(), "Sales by quarter", Legend));
        Add("bar-clustered", Classic(Bar("bar", "clustered"), CatAx(pos: "l") + ValAx(pos: "b"), "Bar chart", Legend));
        Add("column-stacked", Classic(Bar("col", "stacked", overlap: 100), CatAx() + ValAx(), "Stacked", Legend));
        Add("column-percent", Classic(Bar("col", "percentStacked", overlap: 100), CatAx() + ValAx(fmt: "0%"), "100% stacked", Legend));
        Add("column-negative", Classic(Bar("col", "clustered", second: V3), CatAx(pos: "b") + ValAx(), "With negatives", Legend));
        Add("column-labels", Classic(Bar("col", "clustered", DLbls()), CatAx() + ValAx(), "Data labels"));
        Add("line", Classic(Line("standard", "<c:marker val=\"0\"/>", serHead: "<c:marker><c:symbol val=\"none\"/></c:marker>"), CatAx() + ValAx(), "Lines", Legend));
        Add("line-markers", Classic(Line("standard", "<c:marker val=\"1\"/>", serHead: "<c:marker><c:symbol val=\"circle\"/><c:size val=\"6\"/></c:marker>"), CatAx() + ValAx(), "Lines with markers", Legend));
        Add("line-smooth", Classic(Line("standard", "<c:marker val=\"0\"/>", true, "<c:marker><c:symbol val=\"none\"/></c:marker>"), CatAx() + ValAx(), "Smooth lines"));
        Add("line-stacked", Classic(Line("stacked", "<c:marker val=\"0\"/>", serHead: "<c:marker><c:symbol val=\"none\"/></c:marker>"), CatAx() + ValAx(), "Stacked lines"));
        Add("area", Classic($"<c:areaChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>{CatSer(0, "North", V1)}{CatSer(1, "South", V2)}{Axes}</c:areaChart>", CatAx() + ValAx(between: "midCat"), "Area", Legend));
        Add("area-stacked", Classic($"<c:areaChart><c:grouping val=\"stacked\"/><c:varyColors val=\"0\"/>{CatSer(0, "North", V1)}{CatSer(1, "South", V2)}{Axes}</c:areaChart>", CatAx() + ValAx(between: "midCat"), "Stacked area", Legend));
        Add("pie", Classic(Pie(DLbls("bestFit").Replace("<c:showVal val=\"1\"/>", "<c:showVal val=\"0\"/>").Replace("<c:showPercent val=\"0\"/>", "<c:showPercent val=\"1\"/>")), string.Empty, "Pie", Legend.Replace("\"b\"", "\"r\"")));
        Add("pie-exploded", Classic(Pie(head: "<c:explosion val=\"12\"/>"), string.Empty, "Exploded pie", Legend.Replace("\"b\"", "\"r\"")));
        Add("doughnut", Classic($"<c:doughnutChart><c:varyColors val=\"1\"/>{Ser(0, "Share", Cat(Months) + Num("val", V1, "Sheet1!$B$2:$B$7"))}<c:firstSliceAng val=\"0\"/><c:holeSize val=\"55\"/></c:doughnutChart>", string.Empty, "Doughnut", Legend.Replace("\"b\"", "\"r\"")));
        string xAx = ValAx(pos: "b", id: 111, crossAx: 222, grid: string.Empty, between: "midCat");
        string yAx = ValAx(between: "midCat");
        Add("scatter-markers", Classic(XY("scatterChart", "lineMarker", XYSer(0, "A", V1, NoLine + "<c:marker><c:symbol val=\"circle\"/><c:size val=\"7\"/></c:marker>") + XYSer(1, "B", V2, NoLine + "<c:marker><c:symbol val=\"diamond\"/><c:size val=\"7\"/></c:marker>")), xAx + yAx, "Scatter", Legend));
        Add("scatter-lines", Classic(XY("scatterChart", "lineMarker", XYSer(0, "A", V1, Line0("4472C4") + "<c:marker><c:symbol val=\"none\"/></c:marker>")), xAx + yAx, "Scatter with lines"));
        Add("scatter-trendline", Classic(XY("scatterChart", "lineMarker", XYSer(0, "A", V1, NoLine + "<c:marker><c:symbol val=\"circle\"/><c:size val=\"7\"/></c:marker>", "<c:trendline><c:trendlineType val=\"linear\"/><c:dispRSqr val=\"0\"/><c:dispEq val=\"0\"/></c:trendline>")), xAx + yAx, "Linear trend"));
        Add("scatter-errorbars", Classic(XY("scatterChart", "lineMarker", XYSer(0, "A", V1, NoLine + "<c:marker><c:symbol val=\"circle\"/><c:size val=\"7\"/></c:marker>", "<c:errBars><c:errDir val=\"y\"/><c:errBarType val=\"both\"/><c:errValType val=\"fixedVal\"/><c:noEndCap val=\"0\"/><c:val val=\"2\"/></c:errBars>")), xAx + yAx, "Error bars"));
        Add("bubble", Classic($"<c:bubbleChart><c:varyColors val=\"0\"/>{Ser(0, "A", Num("xVal", Xs, "Sheet1!$A$2:$A$7") + Num("yVal", V1, "Sheet1!$B$2:$B$7") + Num("bubbleSize", [3, 8, 5, 12, 6, 9], "Sheet1!$C$2:$C$7") + "<c:bubble3D val=\"0\"/>")}<c:bubbleScale val=\"100\"/>{Axes}</c:bubbleChart>", xAx + yAx, "Bubble"));
        Add("radar", Classic($"<c:radarChart><c:radarStyle val=\"marker\"/><c:varyColors val=\"0\"/>{CatSer(0, "North", V1, "<c:marker><c:symbol val=\"circle\"/></c:marker>")}{CatSer(1, "South", V2, "<c:marker><c:symbol val=\"circle\"/></c:marker>")}{Axes}</c:radarChart>", CatAx() + ValAx(between: "between"), "Radar", Legend));
        Add("radar-filled", Classic($"<c:radarChart><c:radarStyle val=\"filled\"/><c:varyColors val=\"0\"/>{CatSer(0, "North", V1)}{CatSer(1, "South", V2)}{Axes}</c:radarChart>", CatAx() + ValAx(), "Filled radar"));
        double[] open = [20, 22, 21, 24, 23, 25], high = [24, 26, 25, 28, 27, 29], low = [18, 20, 19, 22, 21, 23], close = [22, 21, 24, 23, 25, 28];
        Add("stock-hlc", Classic($"<c:stockChart>{CatSer(0, "High", high, NoLine)}{CatSer(1, "Low", low, NoLine)}{CatSer(2, "Close", close, NoLine)}<c:hiLowLines/>{Axes}</c:stockChart>", CatAx() + ValAx(), "High-low-close"));
        Add("stock-ohlc", Classic($"<c:stockChart>{CatSer(0, "Open", open, NoLine)}{CatSer(1, "High", high, NoLine)}{CatSer(2, "Low", low, NoLine)}{CatSer(3, "Close", close, NoLine)}<c:hiLowLines/><c:upDownBars><c:gapWidth val=\"150\"/></c:upDownBars>{Axes}</c:stockChart>", CatAx() + ValAx(), "Open-high-low-close"));
        Add("surface", Classic($"<c:surfaceChart><c:wireframe val=\"0\"/>{CatSer(0, "R1", V1)}{CatSer(1, "R2", V2)}{CatSer(2, "R3", [9, 13, 20, 8, 16, 11])}<c:axId val=\"111\"/><c:axId val=\"222\"/><c:axId val=\"333\"/></c:surfaceChart>", CatAx() + ValAx(), "Surface"));
        Add("combo-secondary", Classic(
            $"<c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>{CatSer(0, "Revenue", [120, 150, 90, 180, 210, 140])}<c:gapWidth val=\"150\"/>{Axes}</c:barChart>" +
            $"<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>{CatSer(1, "Margin", [0.12, 0.18, 0.09, 0.22, 0.25, 0.15], "<c:marker><c:symbol val=\"circle\"/></c:marker>")}<c:marker val=\"1\"/><c:axId val=\"333\"/><c:axId val=\"444\"/></c:lineChart>",
            CatAx() + ValAx() + ValAx(pos: "r", cross: "max", id: 444, crossAx: 333, grid: string.Empty, fmt: "0%") + CatAx().Replace("<c:axId val=\"111\"/>", "<c:axId val=\"333\"/>").Replace("<c:delete val=\"0\"/>", "<c:delete val=\"1\"/>").Replace("<c:crossAx val=\"222\"/>", "<c:crossAx val=\"444\"/>"), "Combo with secondary axis", Legend));
        Add("log-scale", Classic(Bar("col", "clustered", second: [100, 1000, 25, 4000, 70, 900]).Replace(F(V1[0]), "30"), CatAx() + ValAx("<c:logBase val=\"10\"/>"), "Log scale"));
        Add("axis-titles-fixed", Classic(Bar("col", "clustered"), CatAx("<c:title><c:tx><c:rich><a:bodyPr/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Month</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>") + ValAx("<c:max val=\"40\"/><c:min val=\"0\"/>", "<c:title><c:tx><c:rich><a:bodyPr rot=\"-5400000\" vert=\"horz\"/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Units</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>"), "Axis titles", Legend));
        Add("manual-layout", Classic(Bar("col", "clustered"), CatAx() + ValAx(), "Manual plot", Legend, "<c:layout><c:manualLayout><c:layoutTarget val=\"inner\"/><c:xMode val=\"edge\"/><c:yMode val=\"edge\"/><c:x val=\"0.15\"/><c:y val=\"0.2\"/><c:w val=\"0.7\"/><c:h val=\"0.5\"/></c:manualLayout></c:layout>"));
        Add("reverse-order", Classic(Bar("bar", "clustered"), CatAx(pos: "l").Replace("<c:orientation val=\"minMax\"/>", "<c:orientation val=\"maxMin\"/>") + ValAx(pos: "b"), "Reversed categories"));

        // Chartex.
        Add("waterfall", ExChart("waterfall", ["Start", "Sales", "Returns", "Costs", "Other", "Total"], [100, 60, -25, -40, 15, 110], "<cx:layoutPr><cx:subtotals><cx:idx val=\"0\"/><cx:idx val=\"5\"/></cx:subtotals></cx:layoutPr>", "Waterfall"), true);
        Add("histogram", ExChart("clusteredColumn", null, [1, 2, 2, 3, 3, 3, 4, 4, 5, 5, 5, 5, 6, 7, 8, 8, 9], "<cx:layoutPr><cx:binning intervalClosed=\"r\"/></cx:layoutPr>", "Histogram"), true);
        Add("pareto", ExChart("clusteredColumn", ["A", "B", "C", "D", "E"], [4, 9, 2, 7, 3], "<cx:layoutPr><cx:aggregation val=\"count\"/></cx:layoutPr>", "Pareto", pareto: true), true);
        Add("boxwhisker", ExChart("boxWhisker", ["G1", "G1", "G1", "G1", "G1", "G1", "G2", "G2", "G2", "G2", "G2", "G2"], [3, 5, 6, 7, 8, 20, 10, 11, 12, 14, 15, 16], "<cx:layoutPr><cx:visibility meanLine=\"0\" meanMarker=\"1\" nonoutliers=\"0\" outliers=\"1\"/><cx:statistics quartileMethod=\"exclusive\"/></cx:layoutPr>", "Box and whisker"), true);
        Add("funnel", ExChart("funnel", ["Visits", "Sign-ups", "Trials", "Paid"], [1000, 600, 300, 120], string.Empty, "Funnel"), true);
        Add("treemap", ExTree("treemap", "Treemap"), true);
        Add("sunburst", ExTree("sunburst", "Sunburst"), true);
        Add("regionmap", ExChart("regionMap", ["France", "Spain", "Italy"], [5, 3, 8], string.Empty, "Region map"), true);
        return list;
    }

    private static string ExChart(string layout, string[]? cats, double[] vals, string layoutPr, string title, bool pareto = false)
    {
        var sb = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cx:chartSpace xmlns:a=\"{A}\" xmlns:r=\"{R}\" xmlns:cx=\"{Cx}\"><cx:chartData><cx:data id=\"0\">");
        if (cats is not null)
        {
            sb.Append($"<cx:strDim type=\"cat\"><cx:f>Sheet1!$A$2:$A${cats.Length + 1}</cx:f><cx:lvl ptCount=\"{cats.Length}\">");
            for (int i = 0; i < cats.Length; i++)
            {
                sb.Append($"<cx:pt idx=\"{i}\">{cats[i]}</cx:pt>");
            }

            sb.Append("</cx:lvl></cx:strDim>");
        }

        sb.Append($"<cx:numDim type=\"val\"><cx:f>Sheet1!$B$2:$B${vals.Length + 1}</cx:f><cx:lvl ptCount=\"{vals.Length}\" formatCode=\"General\">");
        for (int i = 0; i < vals.Length; i++)
        {
            sb.Append($"<cx:pt idx=\"{i}\">{F(vals[i])}</cx:pt>");
        }

        sb.Append("</cx:lvl></cx:numDim></cx:data></cx:chartData>");
        sb.Append($"<cx:chart><cx:title pos=\"t\" align=\"ctr\" overlay=\"0\"><cx:tx><cx:txData><cx:v>{title}</cx:v></cx:txData></cx:tx></cx:title><cx:plotArea><cx:plotAreaRegion>");
        sb.Append($"<cx:series layoutId=\"{layout}\" uniqueId=\"{{00000000-0000-0000-0000-000000000001}}\"><cx:tx><cx:txData><cx:f>Sheet1!$B$1</cx:f><cx:v>Series</cx:v></cx:txData></cx:tx><cx:dataLabels pos=\"outEnd\"><cx:visibility seriesName=\"0\" categoryName=\"0\" value=\"1\"/></cx:dataLabels><cx:dataId val=\"0\"/>{layoutPr}</cx:series>");
        if (pareto)
        {
            sb.Append("<cx:series layoutId=\"paretoLine\" ownerIdx=\"0\" uniqueId=\"{00000000-0000-0000-0000-000000000002}\"><cx:axisId val=\"2\"/></cx:series>");
        }

        sb.Append("</cx:plotAreaRegion><cx:axis id=\"0\"><cx:catScaling gapWidth=\"0.5\"/><cx:tickLabels/></cx:axis><cx:axis id=\"1\"><cx:valScaling/><cx:majorGridlines/><cx:tickLabels/></cx:axis></cx:plotArea></cx:chart></cx:chartSpace>");
        return sb.ToString();
    }

    private static string ExTree(string layout, string title)
    {
        string[] top = ["Fruit", "Fruit", "Fruit", "Veg", "Veg", "Grain", "Grain"];
        string[] leaf = ["Apple", "Pear", "Plum", "Kale", "Leek", "Rye", "Oat"];
        double[] v = [30, 18, 9, 22, 11, 14, 6];
        var sb = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cx:chartSpace xmlns:a=\"{A}\" xmlns:r=\"{R}\" xmlns:cx=\"{Cx}\"><cx:chartData><cx:data id=\"0\"><cx:strDim type=\"cat\"><cx:f>Sheet1!$A$2:$B$8</cx:f>");
        sb.Append("<cx:lvl ptCount=\"7\">");
        for (int i = 0; i < 7; i++)
        {
            sb.Append($"<cx:pt idx=\"{i}\">{leaf[i]}</cx:pt>");
        }

        sb.Append("</cx:lvl><cx:lvl ptCount=\"7\">");
        for (int i = 0; i < 7; i++)
        {
            if (i == 0 || top[i] != top[i - 1])
            {
                sb.Append($"<cx:pt idx=\"{i}\">{top[i]}</cx:pt>");
            }
        }

        sb.Append("</cx:lvl></cx:strDim><cx:numDim type=\"size\"><cx:f>Sheet1!$C$2:$C$8</cx:f><cx:lvl ptCount=\"7\" formatCode=\"General\">");
        for (int i = 0; i < 7; i++)
        {
            sb.Append($"<cx:pt idx=\"{i}\">{F(v[i])}</cx:pt>");
        }

        sb.Append($"</cx:lvl></cx:numDim></cx:data></cx:chartData><cx:chart><cx:title pos=\"t\" align=\"ctr\" overlay=\"0\"><cx:tx><cx:txData><cx:v>{title}</cx:v></cx:txData></cx:tx></cx:title><cx:plotArea><cx:plotAreaRegion>");
        sb.Append($"<cx:series layoutId=\"{layout}\" uniqueId=\"{{00000000-0000-0000-0000-000000000001}}\"><cx:dataId val=\"0\"/><cx:layoutPr><cx:parentLabelLayout val=\"banner\"/></cx:layoutPr></cx:series></cx:plotAreaRegion></cx:plotArea></cx:chart></cx:chartSpace>");
        return sb.ToString();
    }

    /// <summary>A clustered column chart of one series, for the axis-scaling comparison.</summary>
    /// <param name="values">The series values.</param>
    /// <returns>The chart part.</returns>
    public static string ScalingChart(double[] values)
    {
        string cats = Cat(values.Select((_, i) => "C" + (i + 1)), "Sheet1!$A$2:$A$" + (values.Length + 1));
        string ser = Ser(0, "Values", cats + Num("val", values, "Sheet1!$B$2:$B$" + (values.Length + 1)));
        return Classic($"<c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>{ser}<c:gapWidth val=\"150\"/>{Axes}</c:barChart>", CatAx() + ValAx(fmt: "General"));
    }

    /// <summary>Datasets for the axis-scaling comparison: the name and the values of one series in a clustered column chart.</summary>
    /// <returns>The cases in a stable order.</returns>
    public static IReadOnlyList<(string Name, double[] Values)> ScalingCases() =>
    [
        ("zero-to-100", [0, 20, 55, 100]),
        ("zero-to-10", [0, 3, 10]),
        ("zero-to-1", [0, 0.4, 1]),
        ("zero-to-8", [1, 4, 8]),
        ("zero-to-200", [10, 90, 200]),
        ("single-value", [5, 5, 5]),
        ("single-large", [4200]),
        ("tight-high", [1000, 1005, 1010]),
        ("tight-mid", [90, 95, 100]),
        ("just-over-sixth", [83, 90, 100]),
        ("just-under-sixth", [84, 92, 100]),
        ("small-fraction", [0.001, 0.004, 0.009]),
        ("tiny-range", [0.0002, 0.0003, 0.0004]),
        ("thousands", [1200, 3400, 9800]),
        ("millions", [1200000, 3400000, 9800000]),
        ("negative-only", [-90, -40, -5]),
        ("negative-tight", [-100, -95, -90]),
        ("mixed", [-30, 10, 45]),
        ("mixed-small-neg", [-2, 40, 80]),
        ("decimals", [2.2, 3.7, 4.1]),
        ("ones", [1, 1, 1, 1]),
        ("zero-only", [0, 0, 0]),
        ("range-9", [0, 4.5, 9]),
        ("range-11", [0, 5, 11]),
        ("range-47", [3, 20, 47]),
        ("range-52", [0, 30, 52]),
        ("range-99", [10, 60, 99]),
        ("range-101", [0, 50, 101]),
        ("range-0.7", [0.1, 0.4, 0.7]),
        ("range-12.5", [2, 6, 12.5]),
        ("range-250", [30, 120, 250]),
        ("range-1999", [100, 1000, 1999]),
        ("range-5", [0, 2, 5]),
        ("range-6", [0, 3, 6]),
        ("range-7", [0, 3, 7]),
        ("range-3", [0, 1, 3]),
        ("range-2", [0, 1, 2]),
        ("range-0.3", [0, 0.1, 0.3]),
        ("range-70", [0, 35, 70]),
        ("range-15", [0, 7, 15]),
    ];
}
