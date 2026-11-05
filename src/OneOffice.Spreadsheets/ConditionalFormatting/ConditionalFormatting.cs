using System.Collections;
using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>The kind of a conditional formatting rule (ST_CfType, ECMA-376 Part 1 §18.18.12).</summary>
public enum ConditionalRuleType : byte
{
    /// <summary>A type the model does not know; <see cref="ConditionalFormatRule.TypeName"/> has the text.</summary>
    Unknown,

    /// <summary>A formula decides.</summary>
    Expression,

    /// <summary>Compare the cell with one or two values (<see cref="ConditionalFormatRule.Operator"/>).</summary>
    CellIs,

    /// <summary>A two- or three-colour scale.</summary>
    ColorScale,

    /// <summary>Data bars.</summary>
    DataBar,

    /// <summary>An icon set.</summary>
    IconSet,

    /// <summary>The top or bottom N items or percent.</summary>
    Top10,

    /// <summary>Unique values.</summary>
    UniqueValues,

    /// <summary>Duplicate values.</summary>
    DuplicateValues,

    /// <summary>Cells containing text.</summary>
    ContainsText,

    /// <summary>Cells not containing text.</summary>
    NotContainsText,

    /// <summary>Cells beginning with text.</summary>
    BeginsWith,

    /// <summary>Cells ending with text.</summary>
    EndsWith,

    /// <summary>Blank cells.</summary>
    ContainsBlanks,

    /// <summary>Cells that are not blank.</summary>
    NotContainsBlanks,

    /// <summary>Cells with an error.</summary>
    ContainsErrors,

    /// <summary>Cells without an error.</summary>
    NotContainsErrors,

    /// <summary>Dates in a period (<see cref="ConditionalFormatRule.TimePeriod"/>).</summary>
    TimePeriod,

    /// <summary>Above or below the average.</summary>
    AboveAverage,
}

/// <summary>One value point of a colour scale, data bar or icon set (CT_Cfvo, §18.3.1.11).</summary>
/// <param name="Type">The type: <c>num</c>, <c>percent</c>, <c>max</c>, <c>min</c>, <c>formula</c>, <c>percentile</c> (or <c>autoMin</c>/<c>autoMax</c> in the extended data bar).</param>
/// <param name="Value">The value or formula text, or null.</param>
/// <param name="GreaterOrEqual">For an icon set, whether the threshold is "greater than or equal" (the default) rather than "greater than".</param>
public readonly record struct ConditionalValuePoint(string Type, string? Value = null, bool GreaterOrEqual = true);

/// <summary>A colour scale (CT_ColorScale, §18.3.1.16).</summary>
/// <param name="Points">Two or three value points.</param>
/// <param name="Colors">One colour per point.</param>
public sealed record ColorScaleInfo(IReadOnlyList<ConditionalValuePoint> Points, IReadOnlyList<StyleColor> Colors);

/// <summary>Data bars (CT_DataBar, §18.3.1.28).</summary>
/// <param name="Points">The shortest and longest bar value points.</param>
/// <param name="Color">The bar colour.</param>
/// <param name="MinLength">The shortest bar as a percent of the cell width.</param>
/// <param name="MaxLength">The longest bar as a percent of the cell width.</param>
/// <param name="ShowValue">Show the cell's value next to the bar.</param>
public sealed record DataBarInfo(IReadOnlyList<ConditionalValuePoint> Points, StyleColor Color, int MinLength = 10, int MaxLength = 90, bool ShowValue = true);

/// <summary>An icon set (CT_IconSet, §18.3.1.49).</summary>
/// <param name="Name">The icon set, such as <c>3TrafficLights1</c>.</param>
/// <param name="Points">The thresholds.</param>
/// <param name="ShowValue">Show the cell's value next to the icon.</param>
/// <param name="Percent">The thresholds are percents.</param>
/// <param name="Reverse">Reverse the icon order.</param>
public sealed record IconSetInfo(string Name, IReadOnlyList<ConditionalValuePoint> Points, bool ShowValue = true, bool Percent = true, bool Reverse = false);

/// <summary>
/// A conditional formatting rule (CT_CfRule, §18.3.1.10) or its extended form in a worksheet's <c>extLst</c> (the x14 rules that carry
/// the newer data bar, icon set and cross-sheet formula features). A view over the element: what the model does not read stays in place.
/// </summary>
public sealed class ConditionalFormatRule
{
    private static readonly string[] TypeNames =
    [
        "", "expression", "cellIs", "colorScale", "dataBar", "iconSet", "top10", "uniqueValues", "duplicateValues", "containsText", "notContainsText", "beginsWith", "endsWith",
        "containsBlanks", "notContainsBlanks", "containsErrors", "notContainsErrors", "timePeriod", "aboveAverage",
    ];

    private readonly MarkupNode _node;

    internal ConditionalFormatRule(MarkupNode node, bool extended)
    {
        _node = node;
        IsExtended = extended;
    }

    /// <summary>Gets the markup element.</summary>
    public MarkupNode Markup => _node;

    /// <summary>Gets a value indicating whether the rule lives in the worksheet's extension list (x14).</summary>
    public bool IsExtended { get; }

    /// <summary>Gets the rule's type.</summary>
    public ConditionalRuleType Type
    {
        get
        {
            int i = Array.IndexOf(TypeNames, TypeName);
            return i <= 0 ? ConditionalRuleType.Unknown : (ConditionalRuleType)i;
        }
    }

    /// <summary>Gets the <c>type</c> attribute as written.</summary>
    public string TypeName => _node.GetAttribute("type") ?? string.Empty;

    /// <summary>Gets or sets the priority; 1 is applied first. Priorities are unique within a sheet.</summary>
    public int Priority
    {
        get => Xl.Int(_node.GetAttribute("priority"));
        set => _node.SetAttribute("priority", value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Gets or sets a value indicating whether lower-priority rules stop being evaluated when this one applies.</summary>
    public bool StopIfTrue
    {
        get => Xl.FlagAttr(_node, "stopIfTrue");
        set
        {
            if (value) _node.SetAttribute("stopIfTrue", "1");
            else _node.RemoveAttribute("stopIfTrue");
        }
    }

    /// <summary>Gets or sets the index of the differential format to apply, or null when the rule has none (colour scales, data bars, icon sets, extended rules).</summary>
    public int? DxfId
    {
        get => int.TryParse(_node.GetAttribute("dxfId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : null;
        set
        {
            if (value is { } v) _node.SetAttribute("dxfId", v.ToString(CultureInfo.InvariantCulture));
            else _node.RemoveAttribute("dxfId");
        }
    }

    /// <summary>Gets the comparison operator of a cell-is or text rule (<c>lessThan</c>, <c>between</c>, <c>containsText</c>, …), or null.</summary>
    public string? Operator => _node.GetAttribute("operator");

    /// <summary>Gets the text of a text rule, or null.</summary>
    public string? Text => _node.GetAttribute("text");

    /// <summary>Gets the period of a time-period rule (<c>today</c>, <c>lastWeek</c>, …), or null.</summary>
    public string? TimePeriod => _node.GetAttribute("timePeriod");

    /// <summary>Gets the N of a top/bottom rule.</summary>
    public int Rank => Xl.Int(_node.GetAttribute("rank"));

    /// <summary>Gets a value indicating whether a top/bottom rule counts percent.</summary>
    public bool Percent => Xl.FlagAttr(_node, "percent");

    /// <summary>Gets a value indicating whether a top/bottom rule selects the bottom items.</summary>
    public bool Bottom => Xl.FlagAttr(_node, "bottom");

    /// <summary>Gets a value indicating whether an average rule selects values above the average (the default) rather than below.</summary>
    public bool AboveAverage => _node.GetAttribute("aboveAverage") is not ("0" or "false");

    /// <summary>Gets the formulas of the rule, as text without a leading <c>=</c>.</summary>
    public IReadOnlyList<string> Formulas
    {
        get
        {
            var list = new List<string>();
            foreach (MarkupNode c in _node.ChildElements)
            {
                if (c.LocalName.SequenceEqual("formula"u8) || c.LocalName.SequenceEqual("f"u8))
                {
                    list.Add(c.InnerText);
                }
            }

            return list;
        }
    }

    /// <summary>Gets the colour scale, or null when the rule is not one.</summary>
    public ColorScaleInfo? ColorScale
    {
        get
        {
            MarkupNode cs = Child("colorScale");
            if (cs.IsNull)
            {
                return null;
            }

            var colors = new List<StyleColor>();
            foreach (MarkupNode c in cs.ChildElements)
            {
                if (c.LocalName.SequenceEqual("color"u8)) colors.Add(StyleColor.Read(c));
            }

            return new ColorScaleInfo(Points(cs), colors);
        }
    }

    /// <summary>Gets the data bar, or null when the rule is not one.</summary>
    public DataBarInfo? DataBar
    {
        get
        {
            MarkupNode db = Child("dataBar");
            if (db.IsNull)
            {
                return null;
            }

            StyleColor color = default;
            foreach (MarkupNode c in db.ChildElements)
            {
                if (c.LocalName.SequenceEqual("color"u8) || c.LocalName.SequenceEqual("fillColor"u8))
                {
                    color = StyleColor.Read(c);
                    break;
                }
            }

            return new DataBarInfo(Points(db), color, Xl.Int(db.GetAttribute("minLength"), 10), Xl.Int(db.GetAttribute("maxLength"), 90), db.GetAttribute("showValue") is not ("0" or "false"));
        }
    }

    /// <summary>Gets the icon set, or null when the rule is not one.</summary>
    public IconSetInfo? IconSet
    {
        get
        {
            MarkupNode s = Child("iconSet");
            if (s.IsNull)
            {
                return null;
            }

            return new IconSetInfo(s.GetAttribute("iconSet") ?? "3TrafficLights1", Points(s), s.GetAttribute("showValue") is not ("0" or "false"), s.GetAttribute("percent") is not ("0" or "false"), Xl.FlagAttr(s, "reverse"));
        }
    }

    private MarkupNode Child(string local)
    {
        foreach (MarkupNode c in _node.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    private static List<ConditionalValuePoint> Points(MarkupNode parent)
    {
        var list = new List<ConditionalValuePoint>();
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (!c.LocalName.SequenceEqual("cfvo"u8))
            {
                continue;
            }

            string? val = c.GetAttribute("val");
            if (val is null)
            {
                foreach (MarkupNode f in c.ChildElements)
                {
                    val = f.InnerText;
                    break;
                }
            }

            list.Add(new ConditionalValuePoint(c.GetAttribute("type") ?? "num", val, c.GetAttribute("gte") is not ("0" or "false")));
        }

        return list;
    }

    internal static string NameOf(ConditionalRuleType t) => TypeNames[(int)t];
}

/// <summary>A group of rules that apply to the same ranges (CT_ConditionalFormatting, §18.3.1.18, or its x14 form).</summary>
public sealed class ConditionalFormat
{
    private readonly MarkupNode _node;

    internal ConditionalFormat(MarkupNode node, bool extended)
    {
        _node = node;
        IsExtended = extended;
    }

    /// <summary>Gets the markup element.</summary>
    public MarkupNode Markup => _node;

    /// <summary>Gets a value indicating whether the group lives in the worksheet's extension list (x14).</summary>
    public bool IsExtended { get; }

    /// <summary>Gets a value indicating whether the group applies to a pivot table.</summary>
    public bool IsPivot => Xl.FlagAttr(_node, "pivot");

    /// <summary>Gets the ranges the rules apply to.</summary>
    public IReadOnlyList<CellRange> Ranges
    {
        get
        {
            string? sq = _node.GetAttribute("sqref");
            if (sq is null)
            {
                foreach (MarkupNode c in _node.ChildElements)
                {
                    if (c.LocalName.SequenceEqual("sqref"u8))
                    {
                        sq = c.InnerText;
                        break;
                    }
                }
            }

            return SqRef.Parse(sq);
        }
    }

    /// <summary>Gets the rules.</summary>
    public IReadOnlyList<ConditionalFormatRule> Rules
    {
        get
        {
            var list = new List<ConditionalFormatRule>();
            foreach (MarkupNode c in _node.ChildElements)
            {
                if (c.LocalName.SequenceEqual("cfRule"u8))
                {
                    list.Add(new ConditionalFormatRule(c, IsExtended));
                }
            }

            return list;
        }
    }
}

/// <summary>What a new rule is: built with the static methods, passed to <see cref="ConditionalFormatCollection"/>.</summary>
public sealed record ConditionalRuleSpec
{
    /// <summary>Gets the type.</summary>
    public ConditionalRuleType Type { get; init; }

    /// <summary>Gets the formulas (text without <c>=</c>).</summary>
    public IReadOnlyList<string> Formulas { get; init; } = [];

    /// <summary>Gets the operator of a cell-is or text rule.</summary>
    public string? Operator { get; init; }

    /// <summary>Gets the text of a text rule.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the period of a time-period rule.</summary>
    public string? TimePeriod { get; init; }

    /// <summary>Gets the differential format index to apply.</summary>
    public int? DxfId { get; init; }

    /// <summary>Gets the N of a top/bottom rule.</summary>
    public int Rank { get; init; }

    /// <summary>Gets a value indicating whether a top/bottom rule counts percent.</summary>
    public bool Percent { get; init; }

    /// <summary>Gets a value indicating whether a top/bottom rule takes the bottom.</summary>
    public bool Bottom { get; init; }

    /// <summary>Gets a value indicating whether an average rule takes values above the average.</summary>
    public bool AboveAverage { get; init; } = true;

    /// <summary>Gets a value indicating whether to stop evaluating lower-priority rules.</summary>
    public bool StopIfTrue { get; init; }

    /// <summary>Gets the colour scale.</summary>
    public ColorScaleInfo? ColorScale { get; init; }

    /// <summary>Gets the data bar.</summary>
    public DataBarInfo? DataBar { get; init; }

    /// <summary>Gets the icon set.</summary>
    public IconSetInfo? IconSet { get; init; }

    /// <summary>A rule decided by a formula.</summary>
    /// <param name="formula">The formula, relative to the top-left cell of the first range.</param>
    /// <param name="dxfId">The differential format to apply.</param>
    /// <returns>The spec.</returns>
    public static ConditionalRuleSpec Expression(string formula, int dxfId) => new() { Type = ConditionalRuleType.Expression, Formulas = [formula], DxfId = dxfId };

    /// <summary>A comparison with one or two values.</summary>
    /// <param name="op">The operator: <c>lessThan</c>, <c>lessThanOrEqual</c>, <c>equal</c>, <c>notEqual</c>, <c>greaterThanOrEqual</c>, <c>greaterThan</c>, <c>between</c> or <c>notBetween</c>.</param>
    /// <param name="formula1">The first value or formula.</param>
    /// <param name="dxfId">The differential format to apply.</param>
    /// <param name="formula2">The second value, for <c>between</c> and <c>notBetween</c>.</param>
    /// <returns>The spec.</returns>
    public static ConditionalRuleSpec CellIs(string op, string formula1, int dxfId, string? formula2 = null) =>
        new() { Type = ConditionalRuleType.CellIs, Operator = op, Formulas = formula2 is null ? [formula1] : [formula1, formula2], DxfId = dxfId };

    /// <summary>A two-colour scale from the lowest to the highest value.</summary>
    /// <param name="low">The colour of the lowest value.</param>
    /// <param name="high">The colour of the highest value.</param>
    /// <returns>The spec.</returns>
    public static ConditionalRuleSpec TwoColorScale(StyleColor low, StyleColor high) =>
        new() { Type = ConditionalRuleType.ColorScale, ColorScale = new ColorScaleInfo([new("min"), new("max")], [low, high]) };

    /// <summary>A three-colour scale: lowest, 50th percentile, highest.</summary>
    /// <param name="low">The colour of the lowest value.</param>
    /// <param name="middle">The colour of the median.</param>
    /// <param name="high">The colour of the highest value.</param>
    /// <returns>The spec.</returns>
    public static ConditionalRuleSpec ThreeColorScale(StyleColor low, StyleColor middle, StyleColor high) =>
        new() { Type = ConditionalRuleType.ColorScale, ColorScale = new ColorScaleInfo([new("min"), new("percentile", "50"), new("max")], [low, middle, high]) };

    /// <summary>Data bars from the lowest to the highest value.</summary>
    /// <param name="color">The bar colour.</param>
    /// <returns>The spec.</returns>
    public static ConditionalRuleSpec DataBars(StyleColor color) => new() { Type = ConditionalRuleType.DataBar, DataBar = new DataBarInfo([new("min"), new("max")], color) };

    /// <summary>An icon set with evenly spaced percent thresholds.</summary>
    /// <param name="name">The icon set name, such as <c>3Arrows</c>; the digit at the start is the number of icons.</param>
    /// <returns>The spec.</returns>
    public static ConditionalRuleSpec Icons(string name)
    {
        int n = name.Length > 0 && name[0] is >= '3' and <= '5' ? name[0] - '0' : 3;
        var pts = new List<ConditionalValuePoint>();
        for (int i = 0; i < n; i++)
        {
            pts.Add(new ConditionalValuePoint("percent", (i * 100 / n).ToString(CultureInfo.InvariantCulture)));
        }

        return new ConditionalRuleSpec { Type = ConditionalRuleType.IconSet, IconSet = new IconSetInfo(name, pts) };
    }

    /// <summary>The top or bottom N values.</summary>
    /// <param name="rank">N.</param>
    /// <param name="dxfId">The differential format to apply.</param>
    /// <param name="bottom">Take the bottom instead of the top.</param>
    /// <param name="percent">N is a percent.</param>
    /// <returns>The spec.</returns>
    public static ConditionalRuleSpec Top(int rank, int dxfId, bool bottom = false, bool percent = false) =>
        new() { Type = ConditionalRuleType.Top10, Rank = rank, DxfId = dxfId, Bottom = bottom, Percent = percent };

    /// <summary>Duplicate (or, with <paramref name="unique"/>, unique) values.</summary>
    /// <param name="dxfId">The differential format to apply.</param>
    /// <param name="unique">Select unique values instead.</param>
    /// <returns>The spec.</returns>
    public static ConditionalRuleSpec Duplicates(int dxfId, bool unique = false) =>
        new() { Type = unique ? ConditionalRuleType.UniqueValues : ConditionalRuleType.DuplicateValues, DxfId = dxfId };
}

/// <summary>The conditional formatting of a worksheet, in the order the part lists it: first the groups in the worksheet body, then those in the extension list.</summary>
public sealed class ConditionalFormatCollection : IEnumerable<ConditionalFormat>
{
    private readonly Worksheet _sheet;

    internal ConditionalFormatCollection(Worksheet sheet) => _sheet = sheet;

    /// <inheritdoc />
    public IEnumerator<ConditionalFormat> GetEnumerator()
    {
        MarkupNode root = _sheet.FrameRoot;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalName.SequenceEqual("conditionalFormatting"u8))
            {
                yield return new ConditionalFormat(c, false);
            }
        }

        foreach (MarkupNode n in SheetExtensions.Find(root, "conditionalFormatting"))
        {
            yield return new ConditionalFormat(n, true);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Gets the number of groups.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (ConditionalFormat _ in this) n++;
            return n;
        }
    }

    /// <summary>Gets the highest priority number in use on the sheet, or 0.</summary>
    public int MaxPriority
    {
        get
        {
            int max = 0;
            foreach (ConditionalFormat f in this)
            {
                foreach (ConditionalFormatRule r in f.Rules)
                {
                    max = Math.Max(max, r.Priority);
                }
            }

            return max;
        }
    }

    /// <summary>
    /// Adds a rule for some ranges. The rule gets the next free priority (it is evaluated after the existing rules); change
    /// <see cref="ConditionalFormatRule.Priority"/> to reorder.
    /// </summary>
    /// <param name="ranges">The ranges the rule applies to.</param>
    /// <param name="rule">The rule.</param>
    /// <returns>The new rule.</returns>
    /// <exception cref="ArgumentException">There is no range, or the rule lacks what its type needs (a formula, a colour scale, …).</exception>
    public ConditionalFormatRule Add(IEnumerable<CellRange> ranges, ConditionalRuleSpec rule)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(rule);
        var list = new List<CellRange>(ranges);
        if (list.Count == 0)
        {
            throw new ArgumentException("A conditional format needs at least one range.", nameof(ranges));
        }

        string pc = Xl.Prefix(_sheet.FrameRoot);
        string xml = BuildRule(rule, MaxPriority + 1, pc);
        string block = "<" + pc + "conditionalFormatting sqref=\"" + SqRef.Format(list) + "\">" + xml + "</" + pc + "conditionalFormatting>";
        MarkupNode n = _sheet.InsertFrameElement("conditionalFormatting", block);
        _sheet.NoteFrameEdited();
        ConditionalFormatRule? last = null;
        foreach (ConditionalFormatRule r in new ConditionalFormat(n, false).Rules)
        {
            last = r;
        }

        return last!;
    }

    /// <summary>Adds a rule for one range.</summary>
    /// <param name="range">The range.</param>
    /// <param name="rule">The rule.</param>
    /// <returns>The new rule.</returns>
    public ConditionalFormatRule Add(CellRange range, ConditionalRuleSpec rule) => Add([range], rule);

    /// <summary>Removes a group of rules.</summary>
    /// <param name="format">The group.</param>
    public void Remove(ConditionalFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.Markup.Parent.IsNull)
        {
            return;
        }

        MarkupNode parent = format.Markup.Parent;
        format.Markup.Remove();
        if (format.IsExtended)
        {
            SheetExtensions.CleanupEmpty(parent);
        }

        _sheet.NoteFrameEdited();
    }

    private static string BuildRule(ConditionalRuleSpec r, int priority, string pc)
    {
        string name = ConditionalFormatRule.NameOf(r.Type);
        if (r.Type == ConditionalRuleType.Unknown)
        {
            throw new ArgumentException("The rule type is not set.", nameof(r));
        }

        var sb = new StringBuilder("<").Append(pc).Append("cfRule type=\"").Append(name).Append('"');
        if (r.DxfId is { } d) sb.Append(" dxfId=\"").Append(d.ToString(CultureInfo.InvariantCulture)).Append('"');
        sb.Append(" priority=\"").Append(priority.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (r.StopIfTrue) sb.Append(" stopIfTrue=\"1\"");
        if (r.Type == ConditionalRuleType.AboveAverage && !r.AboveAverage) sb.Append(" aboveAverage=\"0\"");
        if (r.Type == ConditionalRuleType.Top10)
        {
            if (r.Percent) sb.Append(" percent=\"1\"");
            if (r.Bottom) sb.Append(" bottom=\"1\"");
            sb.Append(" rank=\"").Append(r.Rank.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        string? op = r.Operator;
        if (op is null && r.Type is ConditionalRuleType.ContainsText) op = "containsText";
        if (op is null && r.Type is ConditionalRuleType.NotContainsText) op = "notContains";
        if (op is null && r.Type is ConditionalRuleType.BeginsWith) op = "beginsWith";
        if (op is null && r.Type is ConditionalRuleType.EndsWith) op = "endsWith";
        if (op is not null) sb.Append(" operator=\"").Append(Xl.Esc(op)).Append('"');
        if (r.Text is not null) sb.Append(" text=\"").Append(Xl.Esc(r.Text)).Append('"');
        if (r.TimePeriod is not null) sb.Append(" timePeriod=\"").Append(Xl.Esc(r.TimePeriod)).Append('"');
        sb.Append('>');

        bool needsFormula = r.Type is ConditionalRuleType.Expression or ConditionalRuleType.CellIs;
        if (needsFormula && r.Formulas.Count == 0)
        {
            throw new ArgumentException("This rule type needs a formula.", nameof(r));
        }

        foreach (string f in r.Formulas)
        {
            sb.Append('<').Append(pc).Append("formula>").Append(Xl.Esc(f)).Append("</").Append(pc).Append("formula>");
        }

        switch (r.Type)
        {
            case ConditionalRuleType.ColorScale:
                if (r.ColorScale is not { } cs || cs.Points.Count is < 2 or > 3 || cs.Colors.Count != cs.Points.Count)
                {
                    throw new ArgumentException("A colour scale needs two or three value points and one colour for each.", nameof(r));
                }

                sb.Append('<').Append(pc).Append("colorScale>");
                AppendPoints(sb, pc, cs.Points);
                foreach (StyleColor c in cs.Colors) sb.Append('<').Append(pc).Append("color ").Append(c.ToAttributes()).Append("/>");
                sb.Append("</").Append(pc).Append("colorScale>");
                break;
            case ConditionalRuleType.DataBar:
                if (r.DataBar is not { } db || db.Points.Count != 2)
                {
                    throw new ArgumentException("A data bar needs two value points.", nameof(r));
                }

                sb.Append('<').Append(pc).Append("dataBar");
                if (db.MinLength != 10) sb.Append(" minLength=\"").Append(db.MinLength.ToString(CultureInfo.InvariantCulture)).Append('"');
                if (db.MaxLength != 90) sb.Append(" maxLength=\"").Append(db.MaxLength.ToString(CultureInfo.InvariantCulture)).Append('"');
                if (!db.ShowValue) sb.Append(" showValue=\"0\"");
                sb.Append('>');
                AppendPoints(sb, pc, db.Points);
                sb.Append('<').Append(pc).Append("color ").Append(db.Color.ToAttributes()).Append("/></").Append(pc).Append("dataBar>");
                break;
            case ConditionalRuleType.IconSet:
                if (r.IconSet is not { } ic || ic.Points.Count < 2)
                {
                    throw new ArgumentException("An icon set needs its thresholds.", nameof(r));
                }

                sb.Append('<').Append(pc).Append("iconSet iconSet=\"").Append(Xl.Esc(ic.Name)).Append('"');
                if (!ic.ShowValue) sb.Append(" showValue=\"0\"");
                if (!ic.Percent) sb.Append(" percent=\"0\"");
                if (ic.Reverse) sb.Append(" reverse=\"1\"");
                sb.Append('>');
                AppendPoints(sb, pc, ic.Points);
                sb.Append("</").Append(pc).Append("iconSet>");
                break;
        }

        return sb.Append("</").Append(pc).Append("cfRule>").ToString();
    }

    private static void AppendPoints(StringBuilder sb, string pc, IReadOnlyList<ConditionalValuePoint> points)
    {
        foreach (ConditionalValuePoint p in points)
        {
            sb.Append('<').Append(pc).Append("cfvo type=\"").Append(Xl.Esc(p.Type)).Append('"');
            if (p.Value is not null) sb.Append(" val=\"").Append(Xl.Esc(p.Value)).Append('"');
            if (!p.GreaterOrEqual) sb.Append(" gte=\"0\"");
            sb.Append("/>");
        }
    }
}
