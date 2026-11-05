using System.Collections;
using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>Reading and writing space-separated range lists (<c>sqref</c>, ST_Sqref).</summary>
internal static class SqRef
{
    public static List<CellRange> Parse(string? text)
    {
        var list = new List<CellRange>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        foreach (string part in text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (CellRange.TryParse(part, out CellRange r))
            {
                list.Add(r);
            }
        }

        return list;
    }

    public static string Format(IEnumerable<CellRange> ranges)
    {
        var sb = new StringBuilder();
        foreach (CellRange r in ranges)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(r.ToString());
        }

        return sb.ToString();
    }
}

/// <summary>Finds the extended (x14) forms of worksheet features inside the worksheet's <c>extLst</c>.</summary>
internal static class SheetExtensions
{
    public static IEnumerable<MarkupNode> Find(MarkupNode root, string item)
    {
        foreach (MarkupNode extLst in root.ChildElements)
        {
            if (!extLst.LocalName.SequenceEqual("extLst"u8))
            {
                continue;
            }

            foreach (MarkupNode ext in extLst.ChildElements)
            {
                foreach (MarkupNode group in ext.ChildElements)
                {
                    if (group.LocalNameString != item + "s")
                    {
                        continue;
                    }

                    foreach (MarkupNode n in group.ChildElements)
                    {
                        if (n.LocalNameString == item)
                        {
                            yield return n;
                        }
                    }
                }
            }
        }
    }

    /// <summary>After an item was removed from an extension group: removes the group, its <c>ext</c> and the list when they are empty.</summary>
    public static void CleanupEmpty(MarkupNode group)
    {
        if (group.IsNull || HasElement(group))
        {
            return;
        }

        MarkupNode ext = group.Parent;
        group.Remove();
        if (ext.IsNull || HasElement(ext))
        {
            return;
        }

        MarkupNode list = ext.Parent;
        ext.Remove();
        if (!list.IsNull && !HasElement(list))
        {
            list.Remove();
        }
    }

    private static bool HasElement(MarkupNode n)
    {
        foreach (MarkupNode _ in n.ChildElements)
        {
            return true;
        }

        return false;
    }
}

/// <summary>The kind of data a validation allows (ST_DataValidationType, ECMA-376 Part 1 §18.18.20).</summary>
public enum DataValidationType : byte
{
    /// <summary>Any value.</summary>
    None,

    /// <summary>Whole numbers.</summary>
    WholeNumber,

    /// <summary>Decimal numbers.</summary>
    DecimalNumber,

    /// <summary>A list of values.</summary>
    List,

    /// <summary>Dates.</summary>
    Date,

    /// <summary>Times.</summary>
    Time,

    /// <summary>Text of a given length.</summary>
    TextLength,

    /// <summary>A formula decides.</summary>
    Custom,
}

/// <summary>What Excel does with a value that fails validation (ST_DataValidationErrorStyle).</summary>
public enum DataValidationErrorStyle : byte
{
    /// <summary>Refuse the value (the default).</summary>
    Stop,

    /// <summary>Warn and allow the value to be kept.</summary>
    Warning,

    /// <summary>Inform and allow the value.</summary>
    Information,
}

/// <summary>A data validation (CT_DataValidation, §18.3.1.32) or its extended form in the worksheet's <c>extLst</c>. A view over the element.</summary>
public sealed class DataValidation
{
    private static readonly string[] TypeNames = ["none", "whole", "decimal", "list", "date", "time", "textLength", "custom"];
    private readonly MarkupNode _node;

    internal DataValidation(MarkupNode node, bool extended)
    {
        _node = node;
        IsExtended = extended;
    }

    /// <summary>Gets the markup element.</summary>
    public MarkupNode Markup => _node;

    /// <summary>Gets a value indicating whether the validation lives in the worksheet's extension list (x14, used for lists and formulas that refer to other sheets).</summary>
    public bool IsExtended { get; }

    /// <summary>Gets the kind of data allowed.</summary>
    public DataValidationType Type
    {
        get
        {
            int i = Array.IndexOf(TypeNames, _node.GetAttribute("type") ?? "none");
            return i < 0 ? DataValidationType.None : (DataValidationType)i;
        }
    }

    /// <summary>Gets the error style.</summary>
    public DataValidationErrorStyle ErrorStyle => _node.GetAttribute("errorStyle") switch { "warning" => DataValidationErrorStyle.Warning, "information" => DataValidationErrorStyle.Information, _ => DataValidationErrorStyle.Stop };

    /// <summary>Gets the comparison operator (<c>between</c>, <c>notBetween</c>, <c>equal</c>, <c>notEqual</c>, <c>lessThan</c>, <c>lessThanOrEqual</c>, <c>greaterThan</c>, <c>greaterThanOrEqual</c>); <c>between</c> when absent.</summary>
    public string Operator => _node.GetAttribute("operator") ?? "between";

    /// <summary>Gets a value indicating whether blank cells are accepted.</summary>
    public bool AllowBlank => Xl.FlagAttr(_node, "allowBlank");

    /// <summary>Gets a value indicating whether a list shows its drop-down arrow. (The file stores the inverse, <c>showDropDown</c>.)</summary>
    public bool InCellDropDown => !Xl.FlagAttr(_node, "showDropDown");

    /// <summary>Gets a value indicating whether the input message is shown.</summary>
    public bool ShowInputMessage => Xl.FlagAttr(_node, "showInputMessage");

    /// <summary>Gets a value indicating whether the error alert is shown.</summary>
    public bool ShowErrorMessage => Xl.FlagAttr(_node, "showErrorMessage");

    /// <summary>Gets the error alert's title.</summary>
    public string? ErrorTitle => _node.GetAttribute("errorTitle");

    /// <summary>Gets the error alert's message.</summary>
    public string? Error => _node.GetAttribute("error");

    /// <summary>Gets the input message's title.</summary>
    public string? PromptTitle => _node.GetAttribute("promptTitle");

    /// <summary>Gets the input message.</summary>
    public string? Prompt => _node.GetAttribute("prompt");

    /// <summary>Gets the ranges the validation applies to.</summary>
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

    /// <summary>Gets the first formula or value as text (for a list: a quoted comma-separated list or a range), or null.</summary>
    public string? Formula1 => FormulaText("formula1");

    /// <summary>Gets the second formula or value as text (for <c>between</c> and <c>notBetween</c>), or null.</summary>
    public string? Formula2 => FormulaText("formula2");

    private string? FormulaText(string local)
    {
        foreach (MarkupNode c in _node.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                // The extended form wraps the text in an xm:f element.
                foreach (MarkupNode f in c.ChildElements)
                {
                    return f.InnerText;
                }

                return c.InnerText;
            }
        }

        return null;
    }

    internal static string NameOf(DataValidationType t) => TypeNames[(int)t];
}

/// <summary>A data validation to add.</summary>
public sealed record DataValidationSpec
{
    /// <summary>Gets the kind of data allowed.</summary>
    public DataValidationType Type { get; init; }

    /// <summary>Gets the first formula or value as text (a list: <c>"a,b,c"</c> in quotes, or a range such as <c>$A$1:$A$5</c>).</summary>
    public string? Formula1 { get; init; }

    /// <summary>Gets the second formula or value, for <c>between</c> and <c>notBetween</c>.</summary>
    public string? Formula2 { get; init; }

    /// <summary>Gets the operator; null means <c>between</c>.</summary>
    public string? Operator { get; init; }

    /// <summary>Gets the error style.</summary>
    public DataValidationErrorStyle ErrorStyle { get; init; }

    /// <summary>Gets a value indicating whether blank cells are accepted.</summary>
    public bool AllowBlank { get; init; } = true;

    /// <summary>Gets a value indicating whether a list shows its drop-down arrow.</summary>
    public bool InCellDropDown { get; init; } = true;

    /// <summary>Gets the error alert's title; with <see cref="Error"/> it turns the alert on.</summary>
    public string? ErrorTitle { get; init; }

    /// <summary>Gets the error alert's message.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the input message's title; with <see cref="Prompt"/> it turns the message on.</summary>
    public string? PromptTitle { get; init; }

    /// <summary>Gets the input message.</summary>
    public string? Prompt { get; init; }

    /// <summary>A list of fixed choices.</summary>
    /// <param name="choices">The choices; they must not contain commas, and together fit in 255 characters.</param>
    /// <returns>The spec.</returns>
    public static DataValidationSpec List(params string[] choices) => new() { Type = DataValidationType.List, Formula1 = "\"" + string.Join(',', choices) + "\"" };

    /// <summary>A list taken from a range.</summary>
    /// <param name="range">The range as a formula, for example <c>$A$1:$A$10</c> or <c>Lists!$A$1:$A$10</c>.</param>
    /// <returns>The spec.</returns>
    public static DataValidationSpec ListFromRange(string range) => new() { Type = DataValidationType.List, Formula1 = range };

    /// <summary>Whole numbers between two limits.</summary>
    /// <param name="min">The lower limit.</param>
    /// <param name="max">The upper limit.</param>
    /// <returns>The spec.</returns>
    public static DataValidationSpec WholeBetween(int min, int max) =>
        new() { Type = DataValidationType.WholeNumber, Formula1 = min.ToString(CultureInfo.InvariantCulture), Formula2 = max.ToString(CultureInfo.InvariantCulture) };

    /// <summary>Decimal numbers between two limits.</summary>
    /// <param name="min">The lower limit.</param>
    /// <param name="max">The upper limit.</param>
    /// <returns>The spec.</returns>
    public static DataValidationSpec DecimalBetween(double min, double max) =>
        new() { Type = DataValidationType.DecimalNumber, Formula1 = Xl.Num(min), Formula2 = Xl.Num(max) };

    /// <summary>A formula decides.</summary>
    /// <param name="formula">The formula, relative to the top-left cell of the first range.</param>
    /// <returns>The spec.</returns>
    public static DataValidationSpec Custom(string formula) => new() { Type = DataValidationType.Custom, Formula1 = formula };
}

/// <summary>The data validations of a worksheet: first those in the worksheet body, then those in the extension list.</summary>
public sealed class DataValidationCollection : IEnumerable<DataValidation>
{
    private readonly Worksheet _sheet;

    internal DataValidationCollection(Worksheet sheet) => _sheet = sheet;

    /// <inheritdoc />
    public IEnumerator<DataValidation> GetEnumerator()
    {
        MarkupNode root = _sheet.FrameRoot;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalName.SequenceEqual("dataValidations"u8))
            {
                foreach (MarkupNode d in c.ChildElements)
                {
                    if (d.LocalName.SequenceEqual("dataValidation"u8))
                    {
                        yield return new DataValidation(d, false);
                    }
                }
            }
        }

        foreach (MarkupNode n in SheetExtensions.Find(root, "dataValidation"))
        {
            yield return new DataValidation(n, true);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Gets the number of validations.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (DataValidation _ in this) n++;
            return n;
        }
    }

    /// <summary>Adds a validation.</summary>
    /// <param name="ranges">The ranges it applies to.</param>
    /// <param name="spec">What is allowed.</param>
    /// <returns>The new validation.</returns>
    /// <exception cref="ArgumentException">There is no range, a formula is missing, or a limit is exceeded (a fixed list longer than 255 characters; a message longer than Excel allows: title 32, text 255).</exception>
    public DataValidation Add(IEnumerable<CellRange> ranges, DataValidationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(spec);
        var list = new List<CellRange>(ranges);
        if (list.Count == 0)
        {
            throw new ArgumentException("A validation needs at least one range.", nameof(ranges));
        }

        if (spec.Type != DataValidationType.None && string.IsNullOrEmpty(spec.Formula1))
        {
            throw new ArgumentException("This validation needs a first formula or value.", nameof(spec));
        }

        // Excel limits (§18.3.1.32 attribute lengths): titles 32 characters, messages 255, an inline list 255.
        if ((spec.ErrorTitle?.Length ?? 0) > 32 || (spec.PromptTitle?.Length ?? 0) > 32 || (spec.Error?.Length ?? 0) > 255 || (spec.Prompt?.Length ?? 0) > 255
            || (spec.Type == DataValidationType.List && spec.Formula1!.StartsWith('"') && spec.Formula1.Length > 257))
        {
            throw new ArgumentException("A title, message or inline list is longer than Excel allows.", nameof(spec));
        }

        string pc = Xl.Prefix(_sheet.FrameRoot);
        var sb = new StringBuilder("<").Append(pc).Append("dataValidation");
        if (spec.Type != DataValidationType.None) sb.Append(" type=\"").Append(DataValidation.NameOf(spec.Type)).Append('"');
        if (spec.ErrorStyle != DataValidationErrorStyle.Stop) sb.Append(" errorStyle=\"").Append(spec.ErrorStyle == DataValidationErrorStyle.Warning ? "warning" : "information").Append('"');
        if (spec.Operator is { } op && op != "between") sb.Append(" operator=\"").Append(Xl.Esc(op)).Append('"');
        if (spec.AllowBlank) sb.Append(" allowBlank=\"1\"");
        if (!spec.InCellDropDown) sb.Append(" showDropDown=\"1\"");
        if (spec.Prompt is not null || spec.PromptTitle is not null) sb.Append(" showInputMessage=\"1\"");
        if (spec.Error is not null || spec.ErrorTitle is not null) sb.Append(" showErrorMessage=\"1\"");
        if (spec.ErrorTitle is not null) sb.Append(" errorTitle=\"").Append(Xl.Esc(spec.ErrorTitle)).Append('"');
        if (spec.Error is not null) sb.Append(" error=\"").Append(Xl.Esc(spec.Error)).Append('"');
        if (spec.PromptTitle is not null) sb.Append(" promptTitle=\"").Append(Xl.Esc(spec.PromptTitle)).Append('"');
        if (spec.Prompt is not null) sb.Append(" prompt=\"").Append(Xl.Esc(spec.Prompt)).Append('"');
        sb.Append(" sqref=\"").Append(SqRef.Format(list)).Append("\">");
        if (spec.Formula1 is not null) sb.Append('<').Append(pc).Append("formula1>").Append(Xl.Esc(spec.Formula1)).Append("</").Append(pc).Append("formula1>");
        if (spec.Formula2 is not null) sb.Append('<').Append(pc).Append("formula2>").Append(Xl.Esc(spec.Formula2)).Append("</").Append(pc).Append("formula2>");
        sb.Append("</").Append(pc).Append("dataValidation>");

        MarkupNode container = default;
        foreach (MarkupNode c in _sheet.FrameRoot.ChildElements)
        {
            if (c.LocalName.SequenceEqual("dataValidations"u8))
            {
                container = c;
                break;
            }
        }

        if (container.IsNull)
        {
            container = _sheet.InsertFrameElement("dataValidations", "<" + pc + "dataValidations count=\"1\">" + sb + "</" + pc + "dataValidations>");
        }
        else
        {
            container.AppendXml(Encoding.UTF8.GetBytes(sb.ToString()));
            int n = 0;
            foreach (MarkupNode c in container.ChildElements)
            {
                if (c.LocalName.SequenceEqual("dataValidation"u8)) n++;
            }

            container.SetAttribute("count", n.ToString(CultureInfo.InvariantCulture));
        }

        _sheet.NoteFrameEdited();
        MarkupNode last = Xl.LastElement(container);
        return new DataValidation(last, false);
    }

    /// <summary>Adds a validation for one range.</summary>
    /// <param name="range">The range.</param>
    /// <param name="spec">What is allowed.</param>
    /// <returns>The new validation.</returns>
    public DataValidation Add(CellRange range, DataValidationSpec spec) => Add([range], spec);

    /// <summary>Removes a validation.</summary>
    /// <param name="validation">The validation.</param>
    public void Remove(DataValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        MarkupNode parent = validation.Markup.Parent;
        if (parent.IsNull)
        {
            return;
        }

        validation.Markup.Remove();
        if (validation.IsExtended)
        {
            SheetExtensions.CleanupEmpty(parent);
        }
        else
        {
            int n = 0;
            foreach (MarkupNode c in parent.ChildElements)
            {
                if (c.LocalName.SequenceEqual("dataValidation"u8)) n++;
            }

            if (n == 0)
            {
                parent.Remove();
            }
            else
            {
                parent.SetAttribute("count", n.ToString(CultureInfo.InvariantCulture));
            }
        }

        _sheet.NoteFrameEdited();
    }
}
