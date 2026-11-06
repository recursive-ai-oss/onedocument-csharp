using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

/// <summary>
/// OpenDocument data styles (<c>number:number-style</c>, <c>percentage-style</c>, <c>currency-style</c>, <c>date-style</c>, <c>time-style</c>,
/// <c>boolean-style</c>, <c>text-style</c>; OpenDocument 1.3 Part 1 §16) read as number-format codes of the shared number-format language.
/// </summary>
internal static partial class OdsDataStyles
{
    private static readonly Regex Condition = ConditionPattern();

    [GeneratedRegex(@"^\s*value\(\)\s*(>=|<=|!=|<>|=|<|>)\s*(-?[0-9]*\.?[0-9]+(?:[eE][+-]?[0-9]+)?)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ConditionPattern();

    /// <summary>Reads a data style as a format code.</summary>
    /// <param name="style">The data style.</param>
    /// <param name="styles">The style set, for the styles a <c>style:map</c> applies.</param>
    /// <param name="issues">Receives what has no counterpart in the code language (embedded text, week numbers, ...).</param>
    /// <returns>The code, <c>General</c> when the style has no parts.</returns>
    public static string ToCode(OdfStyle style, OdfStyleSet styles, ICollection<string> issues)
    {
        string own = OwnCode(style.Element, issues);
        var maps = new List<(string Condition, string Code)>();
        foreach (MarkupNode c in style.Element.ChildElements)
        {
            if (!c.IsElement(OdfNamespaces.Style, "map"))
            {
                continue;
            }

            string? cond = c.GetAttribute(OdfNamespaces.Style, "condition");
            string? apply = c.GetAttribute(OdfNamespaces.Style, "apply-style-name");
            OdfStyle? target = apply is null ? null : OdsStyleLookup.Find(styles, "data", apply);
            Match m = cond is null ? Match.Empty : Condition.Match(cond);
            if (target is null || !m.Success)
            {
                issues.Add("style-map");
                continue;
            }

            string op = m.Groups[1].Value switch { "!=" => "<>", var o => o };
            maps.Add(("[" + op + m.Groups[2].Value + "]", OwnCode(target.Element, issues)));
        }

        if (maps.Count == 0)
        {
            return own;
        }

        if (maps.Count > 2)
        {
            issues.Add("style-map");
            maps.RemoveRange(2, maps.Count - 2);
        }

        // "<0" (then "=0") with the style itself for the rest is the plain positive;negative;zero layout.
        if (maps.Count > 0 && maps[0].Condition == "[<0]" && (maps.Count == 1 || maps[1].Condition == "[=0]"))
        {
            return own + ";" + string.Join(';', maps.Select(static m => m.Code));
        }

        var sb = new StringBuilder();
        // ">0" then "<0" with the style itself for zero is the plain positive;negative;zero layout.
        bool plain = maps.Count == 2 && maps[0].Condition == "[>0]" && maps[1].Condition == "[<0]";
        foreach ((string cond, string code) in maps)
        {
            sb.Append(plain ? string.Empty : cond).Append(code).Append(';');
        }

        return sb.Append(own).ToString();
    }

    private static string OwnCode(MarkupNode style, ICollection<string> issues)
    {
        string local = style.LocalNameString;
        var sb = new StringBuilder();
        bool percentage = local == "percentage-style";
        bool anyPart = false;
        foreach (MarkupNode part in style.ChildElements)
        {
            if (part.NamespaceUri != OdfNamespaces.Number)
            {
                continue;
            }

            anyPart = true;
            switch (part.LocalNameString)
            {
                case "number":
                    sb.Append(Number(part));
                    break;
                case "scientific-number":
                    sb.Append(Scientific(part));
                    break;
                case "fraction":
                    sb.Append(Fraction(part));
                    break;
                case "text":
                    string text = part.InnerText;
                    sb.Append(percentage && text == "%" ? "%" : Literal(text, local is "date-style" or "time-style"));
                    break;
                case "text-content":
                    sb.Append('@');
                    break;
                case "currency-symbol":
                    sb.Append(Currency(part));
                    break;
                case "day":
                    sb.Append(Long(part) ? "dd" : "d");
                    break;
                case "month":
                    sb.Append(part.GetAttribute(OdfNamespaces.Number, "textual") == "true" ? (Long(part) ? "mmmm" : "mmm") : (Long(part) ? "mm" : "m"));
                    break;
                case "year":
                    sb.Append(Long(part) ? "yyyy" : "yy");
                    break;
                case "day-of-week":
                    sb.Append(Long(part) ? "dddd" : "ddd");
                    break;
                case "hours":
                    sb.Append(Elapsed(part, Long(part) ? "hh" : "h"));
                    break;
                case "minutes":
                    sb.Append(Elapsed(part, Long(part) ? "mm" : "m"));
                    break;
                case "seconds":
                    string seconds = Elapsed(part, Long(part) ? "ss" : "s");
                    if (int.TryParse(part.GetAttribute(OdfNamespaces.Number, "decimal-places"), NumberStyles.None, CultureInfo.InvariantCulture, out int dp) && dp > 0)
                    {
                        seconds += "." + new string('0', Math.Min(dp, 9));
                    }

                    sb.Append(seconds);
                    break;
                case "am-pm":
                    sb.Append("AM/PM");
                    break;
                case "era":
                case "week-of-year":
                case "quarter":
                    issues.Add(part.LocalNameString);
                    break;
                case "boolean":
                    break;
                default:
                    break;
            }
        }

        if (!anyPart || sb.Length == 0)
        {
            return "General";
        }

        string code = sb.ToString();
        MarkupNode props = OdfMarkup.Child(style, OdfNamespaces.Style, "text-properties");
        if (OdfMarkup.Attribute(props, OdfNamespaces.Fo, "color") is { } color)
        {
            string? named = ColorName(color);
            if (named is null)
            {
                issues.Add("color");
            }
            else
            {
                code = "[" + named + "]" + code;
            }
        }

        if (local == "boolean-style")
        {
            return "General";
        }

        return code;
    }

    private static bool Long(MarkupNode part) => part.GetAttribute(OdfNamespaces.Number, "style") == "long";

    // Elapsed time: number:truncate-on-overflow="false" lets hours, minutes and seconds run past 24, 60 and 60.
    private static string Elapsed(MarkupNode part, string code) => part.GetAttribute(OdfNamespaces.Number, "truncate-on-overflow") == "false" ? "[" + code + "]" : code;

    private static int IntAttr(MarkupNode n, string local, int fallback) =>
        int.TryParse(n.GetAttribute(OdfNamespaces.Number, local), NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? Math.Min(v, 30) : fallback;

    private static string Number(MarkupNode part)
    {
        bool grouping = part.GetAttribute(OdfNamespaces.Number, "grouping") == "true";
        int minInt = IntAttr(part, "min-integer-digits", 1);
        bool hasPlaces = part.GetAttribute(OdfNamespaces.Number, "decimal-places") is not null;
        int places = IntAttr(part, "decimal-places", 0);
        int minPlaces = IntAttr(part, "min-decimal-places", places);
        if (!hasPlaces && !grouping && minInt <= 1)
        {
            return "General";
        }

        string integer = grouping ? (minInt <= 1 ? "#,##0" : "#," + new string('0', minInt)) : (minInt == 0 ? "#" : new string('0', minInt));
        var sb = new StringBuilder(integer);
        if (places > 0)
        {
            sb.Append('.').Append('0', Math.Min(minPlaces, places)).Append('#', Math.Max(0, places - minPlaces));
        }

        // number:display-factor divides by that power of 1000: one trailing comma each.
        if (double.TryParse(part.GetAttribute(OdfNamespaces.Number, "display-factor"), NumberStyles.Float, CultureInfo.InvariantCulture, out double factor) && factor >= 1000)
        {
            for (double f = factor; f >= 1000; f /= 1000)
            {
                sb.Append(',');
            }
        }

        return sb.ToString();
    }

    private static string Scientific(MarkupNode part)
    {
        int minInt = Math.Max(1, IntAttr(part, "min-integer-digits", 1));
        int places = IntAttr(part, "decimal-places", 0);
        int minPlaces = IntAttr(part, "min-decimal-places", places);
        int exp = Math.Max(1, IntAttr(part, "min-exponent-digits", 2));
        var sb = new StringBuilder();
        sb.Append('0', minInt);
        if (places > 0)
        {
            sb.Append('.').Append('0', Math.Min(minPlaces, places)).Append('#', Math.Max(0, places - minPlaces));
        }

        sb.Append(part.GetAttribute(OdfNamespaces.Number, "forced-exponent-sign") == "false" ? "E-" : "E+").Append('0', exp);
        return sb.ToString();
    }

    private static string Fraction(MarkupNode part)
    {
        int minInt = IntAttr(part, "min-integer-digits", 0);
        int numerator = Math.Max(1, IntAttr(part, "min-numerator-digits", 1));
        string denominator = part.GetAttribute(OdfNamespaces.Number, "denominator-value") is { Length: > 0 } fixedValue && int.TryParse(fixedValue, NumberStyles.None, CultureInfo.InvariantCulture, out int d) && d > 0
            ? d.ToString(CultureInfo.InvariantCulture)
            : new string('?', Math.Max(1, IntAttr(part, "min-denominator-digits", 1)));
        string integer = minInt == 0 ? "# " : new string('0', minInt) + " ";
        return integer + new string('?', numerator) + "/" + denominator;
    }

    private static string Currency(MarkupNode part)
    {
        string symbol = part.InnerText;
        if (symbol == "$")
        {
            return "$";
        }

        return "[$" + symbol.Replace("]", string.Empty, StringComparison.Ordinal) + "]";
    }

    // Characters Excel shows as they are without quotes.
    private const string Bare = " $-+/():!^&'~{}<>=";

    internal static string Literal(string text, bool bareComma = false)
    {
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in text)
        {
            if (Bare.Contains(c, StringComparison.Ordinal) || (bareComma && c == ','))
            {
                if (quoted)
                {
                    sb.Append('"');
                    quoted = false;
                }

                sb.Append(c);
            }
            else if (c == '"')
            {
                if (quoted)
                {
                    sb.Append('"');
                    quoted = false;
                }

                sb.Append("\\\"");
            }
            else
            {
                if (!quoted)
                {
                    sb.Append('"');
                    quoted = true;
                }

                sb.Append(c);
            }
        }

        if (quoted)
        {
            sb.Append('"');
        }

        return sb.ToString();
    }

    private static string? ColorName(string color) => color.ToLowerInvariant() switch
    {
        "#000000" => "Black",
        "#ffffff" => "White",
        "#ff0000" => "Red",
        "#00ff00" => "Green",
        "#0000ff" => "Blue",
        "#ffff00" => "Yellow",
        "#ff00ff" => "Magenta",
        "#00ffff" => "Cyan",
        _ => null,
    };
}
