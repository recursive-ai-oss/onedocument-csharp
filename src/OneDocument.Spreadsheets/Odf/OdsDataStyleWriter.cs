using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

/// <summary>
/// Number-format codes written as OpenDocument data styles (<c>number:number-style</c> and its relatives; OpenDocument 1.3 Part 1 §16): the digits,
/// separators, dates and times of a section become the typed child elements, and the sections of a code become one style with <c>style:map</c> conditions.
/// </summary>
internal static class OdsDataStyleWriter
{
    private abstract record Token;

    private sealed record Lit(string Text) : Token;

    private sealed record DateToken(char Code, int Count, bool Elapsed, int Decimals) : Token;

    private sealed record AmPm : Token;

    private sealed record Digits(string Pattern) : Token;

    private sealed record Percent : Token;

    private sealed record Currency(string Symbol) : Token;

    private sealed record Exponent(bool Sign) : Token;

    private sealed record TextHolder : Token;

    private sealed record Fraction(string Numerator, string Denominator, string Integer = "") : Token;

    /// <summary>One section of a code, split into parts.</summary>
    private sealed class Section
    {
        public string Source = string.Empty;
        public string? Condition;
        public string? Color;
        public readonly List<Token> Tokens = [];
    }

    /// <summary>Writes the data styles for a code.</summary>
    /// <param name="code">A number-format code.</param>
    /// <param name="name">The style name to give the main style (conditional parts get <c>name</c> + <c>P0</c>, <c>P1</c>).</param>
    /// <param name="xml">Receives the style elements.</param>
    /// <param name="issues">Receives what could not be carried over.</param>
    /// <returns>False when the code is General (no data style is needed).</returns>
    public static bool Write(string code, string name, StringBuilder xml, ICollection<string> issues)
    {
        if (string.IsNullOrEmpty(code) || code.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        List<Section> sections = Split(code, issues);
        if (sections.Count == 0)
        {
            return false;
        }

        if (sections.Count > 3)
        {
            // A fourth section formats text; styles for text are separate in OpenDocument.
            issues.Add("text-section");
            sections.RemoveRange(3, sections.Count - 3);
        }

        // Excel: with no conditions, the sections are positive; negative; zero. With conditions, they are tried in order and the last one is the rest.
        bool conditional = sections.Any(static s => s.Condition is not null);
        var maps = new List<(string Condition, int Index)>();
        int main;
        if (sections.Count == 1)
        {
            main = 0;
        }
        else if (conditional)
        {
            main = sections.Count - 1;
            for (int i = 0; i < sections.Count - 1; i++)
            {
                maps.Add((sections[i].Condition ?? (i == 0 ? "value()>0" : "value()<0"), i));
            }
        }
        else
        {
            main = 0;
            maps.Add(("value()<0", 1));
            if (sections.Count == 3)
            {
                maps.Add(("value()=0", 2));
            }
        }

        for (int i = 0; i < sections.Count; i++)
        {
            if (i == main)
            {
                continue;
            }

            WriteSection(sections[i], name + "P" + i.ToString(CultureInfo.InvariantCulture), xml, null, issues);
        }

        WriteSection(sections[main], name, xml, maps.Select(m => (m.Condition, name + "P" + m.Index.ToString(CultureInfo.InvariantCulture))).ToList(), issues);
        return true;
    }

    private static List<Section> Split(string code, ICollection<string> issues)
    {
        var sections = new List<Section>();
        var sb = new StringBuilder();
        bool quote = false;
        bool bracket = false;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '\\' && i + 1 < code.Length && !quote)
            {
                sb.Append(c).Append(code[++i]);
                continue;
            }

            if (c == '"')
            {
                quote = !quote;
            }
            else if (!quote && c == '[')
            {
                bracket = true;
            }
            else if (!quote && c == ']')
            {
                bracket = false;
            }

            if (c == ';' && !quote && !bracket)
            {
                sections.Add(Parse(sb.ToString(), issues));
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        sections.Add(Parse(sb.ToString(), issues));
        return sections;
    }

    private static Section Parse(string source, ICollection<string> issues)
    {
        var s = new Section { Source = source };
        var tokens = new List<Token>();
        if (source.Trim().Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            s.Tokens.Add(new Digits("General"));
            return s;
        }

        var lit = new StringBuilder();
        void FlushLit()
        {
            if (lit.Length > 0)
            {
                tokens.Add(new Lit(lit.ToString()));
                lit.Clear();
            }
        }

        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            switch (c)
            {
                case '"':
                    int end = source.IndexOf('"', i + 1);
                    if (end < 0)
                    {
                        end = source.Length;
                    }

                    lit.Append(source, i + 1, end - i - 1);
                    i = Math.Min(source.Length, end + 1);
                    continue;
                case '\\':
                    if (i + 1 < source.Length)
                    {
                        lit.Append(source[i + 1]);
                    }

                    i += 2;
                    continue;
                case '_':
                    lit.Append(' ');
                    i += 2;
                    continue;
                case '*':
                    i += 2;
                    continue;
                case '[':
                    int close = source.IndexOf(']', i);
                    if (close < 0)
                    {
                        close = source.Length - 1;
                    }

                    string item = source.Substring(i + 1, close - i - 1);
                    i = close + 1;
                    if (item.Length > 0 && (item[0] is '>' or '<' or '='))
                    {
                        s.Condition = "value()" + item.Replace("<>", "!=", StringComparison.Ordinal);
                    }
                    else if (item.StartsWith('$'))
                    {
                        // [$-409] is only a locale tag; [$EUR-407] is a currency symbol with one.
                        FlushLit();
                        int dash = item.IndexOf('-', StringComparison.Ordinal);
                        string symbol = dash >= 0 ? item[1..dash] : item[1..];
                        if (symbol.Length > 0)
                        {
                            tokens.Add(new Currency(symbol));
                        }
                    }
                    else if (item.Length > 0 && (item[0] is 'h' or 'H' or 'm' or 'M' or 's' or 'S'))
                    {
                        FlushLit();
                        tokens.Add(new DateToken(char.ToLowerInvariant(item[0]) == 'm' ? 'n' : char.ToLowerInvariant(item[0]), item.Length, Elapsed: true, 0));
                    }
                    else if (ColorHex(item) is { } hex)
                    {
                        s.Color = hex;
                    }
                    else
                    {
                        issues.Add("bracket-" + item);
                    }

                    continue;
                case '0' or '#' or '?' or '.' or ',' when c is '0' or '#' or '?' || (c == '.' && i + 1 < source.Length && source[i + 1] is '0' or '#' or '?'):
                    FlushLit();
                    int start = i;
                    while (i < source.Length && source[i] is '0' or '#' or '?' or '.' or ',')
                    {
                        i++;
                    }

                    string digits = source[start..i];
                    // A fraction: digit placeholders, '/', then a denominator pattern or a number.
                    if (i < source.Length && source[i] == '/')
                    {
                        int d = i + 1;
                        while (d < source.Length && (source[d] is '?' or '#' or '0' || char.IsAsciiDigit(source[d])))
                        {
                            d++;
                        }

                        if (d > i + 1)
                        {
                            // the integer part (when present) is a preceding Digits token; the numerator is the last run of placeholders
                            tokens.Add(new Fraction(digits, source[(i + 1)..d]));
                            i = d;
                            continue;
                        }
                    }

                    tokens.Add(new Digits(digits));
                    continue;
                case '%':
                    FlushLit();
                    tokens.Add(new Percent());
                    i++;
                    continue;
                case '@':
                    FlushLit();
                    tokens.Add(new TextHolder());
                    i++;
                    continue;
                case 'E' or 'e' when i + 1 < source.Length && source[i + 1] is '+' or '-' && tokens.Count > 0 && tokens[^1] is Digits:
                    FlushLit();
                    bool sign = source[i + 1] == '+';
                    i += 2;
                    while (i < source.Length && source[i] == '0')
                    {
                        i++;
                    }

                    tokens.Add(new Exponent(sign));
                    continue;
                case 'y' or 'Y' or 'd' or 'D' or 'h' or 'H' or 's' or 'S' or 'm' or 'M' or 'e' or 'g' or 'b':
                    FlushLit();
                    char lower = char.ToLowerInvariant(c);
                    int run = 1;
                    while (i + run < source.Length && char.ToLowerInvariant(source[i + run]) == lower)
                    {
                        run++;
                    }

                    i += run;
                    int decimals = 0;
                    if (lower == 's' && i < source.Length && source[i] == '.' && i + 1 < source.Length && source[i + 1] == '0')
                    {
                        int z = i + 1;
                        while (z < source.Length && source[z] == '0')
                        {
                            z++;
                        }

                        decimals = z - i - 1;
                        i = z;
                    }

                    tokens.Add(new DateToken(lower, run, Elapsed: false, decimals));
                    continue;
                case 'A' or 'a':
                    if (source.AsSpan(i).StartsWith("AM/PM", StringComparison.OrdinalIgnoreCase))
                    {
                        FlushLit();
                        tokens.Add(new AmPm());
                        i += 5;
                        continue;
                    }

                    if (source.AsSpan(i).StartsWith("A/P", StringComparison.OrdinalIgnoreCase))
                    {
                        FlushLit();
                        tokens.Add(new AmPm());
                        i += 3;
                        continue;
                    }

                    lit.Append(c);
                    i++;
                    continue;
                case 'G':
                    lit.Append(c);
                    i++;
                    continue;
                case '$':
                    FlushLit();
                    tokens.Add(new Currency("$"));
                    i++;
                    continue;
                default:
                    lit.Append(c);
                    i++;
                    continue;
            }
        }

        FlushLit();

        // A fraction takes the integer digits that stand in front of it ('# ?/?').
        for (int t = 1; t < tokens.Count; t++)
        {
            if (tokens[t] is Fraction fr)
            {
                int back = t - 1;
                if (back >= 0 && tokens[back] is Lit { Text: " " })
                {
                    back--;
                }

                if (back >= 0 && tokens[back] is Digits d0 && back < t)
                {
                    tokens[t] = fr with { Integer = d0.Pattern };
                    tokens.RemoveRange(back, t - back);
                    t = back;
                }
            }
        }

        // Excel's m is a minute right after hours or right before seconds, otherwise a month.
        for (int t = 0; t < tokens.Count; t++)
        {
            if (tokens[t] is DateToken { Code: 'm', Elapsed: false } m)
            {
                bool minute = false;
                for (int b = t - 1; b >= 0; b--)
                {
                    if (tokens[b] is DateToken prev)
                    {
                        minute = prev.Code == 'h';
                        break;
                    }

                    if (tokens[b] is not Lit)
                    {
                        break;
                    }
                }

                if (!minute)
                {
                    for (int f = t + 1; f < tokens.Count; f++)
                    {
                        if (tokens[f] is DateToken next)
                        {
                            minute = next.Code == 's';
                            break;
                        }

                        if (tokens[f] is not Lit)
                        {
                            break;
                        }
                    }
                }

                tokens[t] = m with { Code = minute ? 'n' : 'm' };
            }
        }

        s.Tokens.AddRange(tokens);
        return s;
    }

    private static string? ColorHex(string name) => name.ToLowerInvariant() switch
    {
        "black" => "#000000",
        "white" => "#ffffff",
        "red" => "#ff0000",
        "green" => "#00ff00",
        "blue" => "#0000ff",
        "yellow" => "#ffff00",
        "magenta" => "#ff00ff",
        "cyan" => "#00ffff",
        _ => null,
    };

    private static string X(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    private static void WriteSection(Section s, string name, StringBuilder xml, List<(string Condition, string Style)>? maps, ICollection<string> issues)
    {
        bool hasDate = s.Tokens.Any(static t => t is DateToken or AmPm);
        bool hasYmd = s.Tokens.Any(static t => t is DateToken { Code: 'y' or 'm' or 'd' or 'e' or 'g' or 'b' });
        bool percent = s.Tokens.Any(static t => t is Percent);
        bool currency = s.Tokens.Any(static t => t is Currency);
        bool text = s.Tokens.Any(static t => t is TextHolder);
        bool onlyText = text && !s.Tokens.Any(static t => t is Digits or DateToken);
        string element = onlyText ? "text-style" : hasDate ? (hasYmd ? "date-style" : "time-style") : percent ? "percentage-style" : currency ? "currency-style" : "number-style";

        var body = new StringBuilder();
        if (s.Color is not null)
        {
            body.Append("<style:text-properties fo:color=\"").Append(s.Color).Append("\"/>");
        }

        bool hasGrouping = false;
        bool numberWritten = false;
        for (int i = 0; i < s.Tokens.Count; i++)
        {
            Token t = s.Tokens[i];
            switch (t)
            {
                case Lit l:
                    body.Append("<number:text>").Append(X(l.Text)).Append("</number:text>");
                    break;
                case TextHolder:
                    body.Append("<number:text-content/>");
                    break;
                case Percent:
                    body.Append("<number:text>%</number:text>");
                    break;
                case Currency c:
                    // Only a currency style may hold the symbol; elsewhere it is plain text.
                    body.Append(element == "currency-style" ? "<number:currency-symbol>" + X(c.Symbol) + "</number:currency-symbol>" : "<number:text>" + X(c.Symbol) + "</number:text>");
                    break;
                case AmPm:
                    body.Append("<number:am-pm/>");
                    break;
                case Exponent:
                    break;
                case Fraction f:
                    body.Append("<number:fraction number:min-integer-digits=\"").Append(f.Integer.Count(static ch => ch == '0')).Append("\" number:min-numerator-digits=\"")
                        .Append(Math.Max(1, f.Numerator.Count(static ch => ch is '?' or '0'))).Append('"');
                    if (f.Denominator.All(char.IsAsciiDigit))
                    {
                        body.Append(" number:denominator-value=\"").Append(f.Denominator).Append('"');
                    }
                    else
                    {
                        body.Append(" number:min-denominator-digits=\"").Append(Math.Max(1, f.Denominator.Count(static ch => ch is '?' or '0'))).Append('"');
                    }

                    body.Append("/>");
                    break;
                case Digits d:
                    if (d.Pattern == "General")
                    {
                        body.Append("<number:number number:min-integer-digits=\"1\"/>");
                        break;
                    }

                    // A number style holds one number element (the ODF 1.3 schema allows no second one); a code with several digit groups, such as a
                    // phone-number picture, keeps the first and reports the rest.
                    if (numberWritten)
                    {
                        issues.Add("multiple-digit-groups");
                        break;
                    }

                    numberWritten = true;
                    bool sci = i + 1 < s.Tokens.Count && s.Tokens[i + 1] is Exponent;
                    int expDigits = 2;
                    bool sign = true;
                    if (sci && s.Tokens[i + 1] is Exponent e)
                    {
                        sign = e.Sign;
                        expDigits = Math.Max(1, ExponentDigits(s.Source));
                    }

                    body.Append(NumberElement(d.Pattern, sci, sign, expDigits, out bool grouping));
                    hasGrouping |= grouping;
                    break;
                case DateToken dt:
                    body.Append(DatePart(dt, s));
                    break;
                default:
                    break;
            }
        }

        if (percent && !s.Tokens.Any(static t => t is Digits))
        {
            issues.Add("percent-without-digits");
        }

        xml.Append('<').Append("number:").Append(element).Append(" style:name=\"").Append(name).Append('"');
        if (hasDate && s.Tokens.Any(static t => t is DateToken { Elapsed: true }))
        {
            xml.Append(" number:truncate-on-overflow=\"false\"");
        }

        // Adjacent literals are one text element: the schema allows a single text before and after the number, and dropping a
        // second digit group above leaves its separators next to each other.
        body.Replace("</number:text><number:text>", string.Empty);
        xml.Append('>').Append(body);
        if (maps is not null)
        {
            foreach ((string condition, string style) in maps)
            {
                xml.Append("<style:map style:condition=\"").Append(X(condition)).Append("\" style:apply-style-name=\"").Append(style).Append("\"/>");
            }
        }

        xml.Append("</number:").Append(element).Append('>');
        _ = hasGrouping;
    }

    private static int ExponentDigits(string source)
    {
        int e = source.IndexOfAny(['E', 'e']);
        if (e < 0)
        {
            return 2;
        }

        int n = 0;
        for (int i = e + 2; i < source.Length && source[i] == '0'; i++)
        {
            n++;
        }

        return n == 0 ? 2 : n;
    }

    // The digits of one numeric part: integer placeholders, an optional point and the decimals; commas group thousands, or divide by 1000 when they trail.
    private static string NumberElement(string pattern, bool scientific, bool exponentSign, int exponentDigits, out bool grouping)
    {
        grouping = false;
        string p = pattern;
        int divisions = 0;
        while (p.EndsWith(',') && p.Length > 1)
        {
            divisions++;
            p = p[..^1];
        }

        int point = p.IndexOf('.', StringComparison.Ordinal);
        string integer = point < 0 ? p : p[..point];
        string decimals = point < 0 ? string.Empty : p[(point + 1)..].Replace(",", string.Empty, StringComparison.Ordinal);
        grouping = integer.Contains(',', StringComparison.Ordinal);
        int minInteger = integer.Count(static c => c == '0');
        int places = decimals.Length;
        int minPlaces = decimals.Count(static c => c == '0');
        var sb = new StringBuilder();
        if (scientific)
        {
            sb.Append("<number:scientific-number number:decimal-places=\"").Append(places).Append("\" number:min-decimal-places=\"").Append(minPlaces)
                .Append("\" number:min-integer-digits=\"").Append(Math.Max(1, minInteger)).Append("\" number:min-exponent-digits=\"").Append(exponentDigits)
                .Append("\" number:exponent-interval=\"1\" number:forced-exponent-sign=\"").Append(exponentSign ? "true" : "false").Append("\"/>");
            return sb.ToString();
        }

        sb.Append("<number:number number:decimal-places=\"").Append(places).Append("\" number:min-decimal-places=\"").Append(minPlaces)
            .Append("\" number:min-integer-digits=\"").Append(minInteger).Append('"');
        if (grouping)
        {
            sb.Append(" number:grouping=\"true\"");
        }

        if (divisions > 0)
        {
            sb.Append(" number:display-factor=\"").Append(Math.Pow(1000, divisions).ToString("0", CultureInfo.InvariantCulture)).Append('"');
        }

        return sb.Append("/>").ToString();
    }

    private static string DatePart(DateToken t, Section s)
    {
        string style = t.Count >= 2 ? " number:style=\"long\"" : string.Empty;
        switch (t.Code)
        {
            case 'd':
                return t.Count >= 3
                    ? "<number:day-of-week" + (t.Count >= 4 ? " number:style=\"long\"" : string.Empty) + "/>"
                    : "<number:day" + style + "/>";
            case 'm':
                return t.Count >= 3
                    ? "<number:month number:textual=\"true\"" + (t.Count >= 4 ? " number:style=\"long\"" : string.Empty) + "/>"
                    : "<number:month" + style + "/>";
            case 'y':
                return "<number:year" + (t.Count >= 3 ? " number:style=\"long\"" : string.Empty) + "/>";
            case 'h':
                return "<number:hours" + style + (t.Elapsed ? " number:truncate-on-overflow=\"false\"" : string.Empty) + "/>";
            case 'n':
                return "<number:minutes" + style + (t.Elapsed ? " number:truncate-on-overflow=\"false\"" : string.Empty) + "/>";
            case 's':
                return "<number:seconds" + style + (t.Elapsed ? " number:truncate-on-overflow=\"false\"" : string.Empty) + (t.Decimals > 0 ? " number:decimal-places=\"" + t.Decimals + "\"" : string.Empty) + "/>";
            case 'e' or 'g' or 'b':
                return "<number:era" + (t.Count >= 2 ? " number:style=\"long\"" : string.Empty) + "/>";
            default:
                _ = s;
                return string.Empty;
        }
    }
}
