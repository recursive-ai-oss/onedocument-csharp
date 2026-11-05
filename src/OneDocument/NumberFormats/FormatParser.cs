using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.NumberFormats;

// Parses a format code into sections of elements.
// ECMA-376 Part 1 §18.8.31 (numFmts): up to four sections separated by semicolons; positive, negative, zero, text.
// Microsoft's "Number format codes" and "Review guidelines for customizing a number format" articles describe the codes.
internal static class FormatParser
{
    private sealed class Fail : Exception
    {
        public Fail(string code, string message, int position)
            : base(message)
        {
            Defect = new NumberFormatDefect(code, message, position);
        }

        public NumberFormatDefect Defect { get; }
    }

    public static bool TryParse(string code, out NumberFormat? format, out NumberFormatDefect? defect)
    {
        try
        {
            format = ParseCode(code);
            defect = null;
            return true;
        }
        catch (Fail f)
        {
            format = null;
            defect = f.Defect;
            return false;
        }
    }

    private static NumberFormat ParseCode(string code)
    {
        if (code.Length == 0)
        {
            code = "General";
        }

        List<(int Start, int End)> parts = Split(code);
        if (parts.Count > 4)
        {
            throw new Fail("numfmt.too-many-sections", "A format code has at most four sections.", parts[4].Start);
        }

        var sections = new NumberFormatSection[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            sections[i] = ParseSection(code, parts[i].Start, parts[i].End);
        }

        // Only the last of four sections is the text section; a condition there makes no sense.
        if (parts.Count == 4 && sections[3].Condition is not null)
        {
            throw new Fail("numfmt.condition-on-text-section", "The fourth (text) section cannot have a condition.", parts[3].Start);
        }

        return new NumberFormat(code, sections);
    }

    // Splits on semicolons outside quotes, brackets and escapes.
    private static List<(int, int)> Split(string s)
    {
        var list = new List<(int, int)>(4);
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"')
            {
                int j = s.IndexOf('"', i + 1);
                if (j < 0)
                {
                    throw new Fail("numfmt.unterminated-string", "A quoted string is not closed.", i);
                }

                i = j;
            }
            else if (c is '\\' or '_' or '*')
            {
                i++;
            }
            else if (c == '[')
            {
                int j = s.IndexOf(']', i + 1);
                if (j < 0)
                {
                    throw new Fail("numfmt.unterminated-bracket", "A bracketed item is not closed.", i);
                }

                i = j;
            }
            else if (c == ';')
            {
                list.Add((start, i));
                start = i + 1;
            }
        }

        list.Add((start, s.Length));
        return list;
    }

    private static readonly (string Name, int Rgb)[] NamedColors =
    [
        ("Black", 0x000000), ("Blue", 0x0000FF), ("Cyan", 0x00FFFF), ("Green", 0x00FF00),
        ("Magenta", 0xFF00FF), ("Red", 0xFF0000), ("White", 0xFFFFFF), ("Yellow", 0xFFFF00),
    ];

    // The legacy palette entries 8..63 (index = [ColorN] + 7).
    private static readonly int[] Palette =
    [
        0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
        0x800000, 0x008000, 0x000080, 0x808000, 0x800080, 0x008080, 0xC0C0C0, 0x808080,
        0x9999FF, 0x993366, 0xFFFFCC, 0xCCFFFF, 0x660066, 0xFF8080, 0x0066CC, 0xCCCCFF,
        0x000080, 0xFF00FF, 0xFFFF00, 0x00FFFF, 0x800080, 0x800000, 0x008080, 0x0000FF,
        0x00CCFF, 0xCCFFFF, 0xCCFFCC, 0xFFFF99, 0x99CCFF, 0xFF99CC, 0xCC99FF, 0xFFCC99,
        0x3366FF, 0x33CCCC, 0x99CC00, 0xFFCC00, 0xFF9900, 0xFF6600, 0x666699, 0x969696,
        0x003366, 0x339966, 0x003300, 0x333300, 0x993300, 0x993366, 0x333399, 0x333333,
    ];

    internal static readonly string[] AmPmPairs = ["上午/下午", "午前/午後", "오전/오후"];

    private static NumberFormatSection ParseSection(string code, int start, int end)
    {
        string src = code.Substring(start, end - start);
        var toks = new List<Element>(src.Length);
        NumberFormatCondition? cond = null;
        NumberFormatColor? color = null;
        int locale = 0, calendar = 0, dbnum = 0;
        bool currencyTag = false, systemDate = false, systemTime = false;
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            int pos = start + i;
            switch (c)
            {
                case '"':
                {
                    int j = src.IndexOf('"', i + 1);
                    toks.Add(new Element(EK.Literal, 0, src.Substring(i + 1, j - i - 1)));
                    i = j;
                    break;
                }

                case '\\':
                    if (i + 1 >= src.Length)
                    {
                        throw new Fail("numfmt.dangling-escape", "A backslash ends the section.", pos);
                    }

                    toks.Add(new Element(EK.Literal, 0, src[i + 1].ToString()));
                    i++;
                    break;
                case '_':
                    if (i + 1 >= src.Length)
                    {
                        throw new Fail("numfmt.dangling-escape", "An underscore ends the section.", pos);
                    }

                    // The width of the next character; rendered as one space.
                    toks.Add(new Element(EK.Literal, 0, " "));
                    i++;
                    break;
                case '*':
                    if (i + 1 >= src.Length)
                    {
                        throw new Fail("numfmt.dangling-escape", "An asterisk ends the section.", pos);
                    }

                    toks.Add(new Element(EK.Fill, 0, null, src[i + 1]));
                    i++;
                    break;
                case '[':
                {
                    int j = src.IndexOf(']', i + 1);
                    string inner = src.Substring(i + 1, j - i - 1);
                    ParseBracket(inner, pos, toks, ref cond, ref color, ref locale, ref calendar, ref dbnum, ref currencyTag, ref systemDate, ref systemTime);
                    i = j;
                    break;
                }

                case '0':
                    toks.Add(new Element(EK.Zero));
                    break;
                case '#':
                    toks.Add(new Element(EK.Hash));
                    break;
                case '?':
                    toks.Add(new Element(EK.Q));
                    break;
                case '.':
                    toks.Add(new Element(EK.Point));
                    break;
                case ',':
                    toks.Add(new Element(EK.Comma));
                    break;
                case '%':
                    toks.Add(new Element(EK.Percent));
                    break;
                case '/':
                    toks.Add(new Element(EK.Slash));
                    break;
                case '@':
                    toks.Add(new Element(EK.At));
                    break;
                case >= '1' and <= '9':
                    toks.Add(new Element(EK.Digit, c - '0'));
                    break;
                case ' ' or '$' or '-' or '+' or '(' or ')' or ':' or '!' or '^' or '&' or '\'' or '~' or '{' or '}' or '<' or '>' or '=':
                    toks.Add(new Element(EK.Literal, 0, c.ToString()));
                    break;
                default:
                    i = ParseLetter(src, i, pos, toks);
                    break;
            }
        }

        var section = BuildSection(src, start, toks, cond, color, locale, calendar, dbnum, currencyTag);
        if (systemDate || systemTime)
        {
            // [$-F800] and [$-F400] stand for the system long date and time; resolved against US English at parse time.
            NumberFormatCulture en = NumberFormatCulture.EnglishUnitedStates;
            NumberFormat inner = ParseCode(systemDate ? en.LongDatePattern : en.LongTimePattern);
            var s = inner.SectionArray[0];
            s.Condition = section.Condition;
            s.Color = section.Color;
            return s;
        }

        return section;
    }

    private static void ParseBracket(
        string inner, int pos, List<Element> toks, ref NumberFormatCondition? cond, ref NumberFormatColor? color, ref int locale,
        ref int calendar, ref int dbnum, ref bool currencyTag, ref bool systemDate, ref bool systemTime)
    {
        if (inner.Length > 0 && inner[0] != '$')
        {
            inner = inner.Trim();
        }

        if (inner.Length == 0)
        {
            throw new Fail("numfmt.unknown-bracket", "An empty [] item.", pos);
        }

        if (inner[0] == '$')
        {
            // [$<currency text>-<hex locale>]; either part may be missing.
            string rest = inner.Substring(1);
            int dash = rest.LastIndexOf('-');
            string text = rest;
            if (dash >= 0 && dash < rest.Length - 1 && IsHex(rest.AsSpan(dash + 1)))
            {
                text = rest.Substring(0, dash);
                int v = int.Parse(rest.AsSpan(dash + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                int lcid = v & 0xFFFF;
                if (lcid == 0xF800)
                {
                    systemDate = true;
                }
                else if (lcid == 0xF400)
                {
                    systemTime = true;
                }
                else
                {
                    locale = lcid;
                    // The two bytes above the language: the calendar, then the numeral system.
                    calendar = (v >> 16) & 0xFFFF;
                }
            }
            else if (dash >= 0 && dash == rest.Length - 1)
            {
                text = rest.Substring(0, dash);
            }

            if (text.Length > 0)
            {
                toks.Add(new Element(EK.Literal, 1, text));
                currencyTag = true;
            }

            return;
        }

        if (inner[0] is '<' or '>' or '=')
        {
            cond = ParseCondition(inner, pos);
            return;
        }

        string up = inner.ToUpperInvariant();
        switch (up)
        {
            case "H" or "HH":
                toks.Add(new Element(EK.ElapsedHour, up.Length));
                return;
            case "M" or "MM":
                toks.Add(new Element(EK.ElapsedMinute, up.Length));
                return;
            case "S" or "SS":
                toks.Add(new Element(EK.ElapsedSecond, up.Length));
                return;
        }

        foreach ((string name, int rgb) in NamedColors)
        {
            if (string.Equals(up, name, StringComparison.OrdinalIgnoreCase))
            {
                color = new NumberFormatColor(rgb, 0, name);
                return;
            }
        }

        if (up.StartsWith("COLOR", StringComparison.Ordinal) && int.TryParse(up.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 56)
        {
            color = new NumberFormatColor(Palette[n - 1], n, string.Empty);
            return;
        }

        if (up.StartsWith("DBNUM", StringComparison.Ordinal) && up.Length == 6 && up[5] is >= '1' and <= '3')
        {
            dbnum = up[5] - '0';
            return;
        }

        throw new Fail("numfmt.unknown-bracket", "Unknown bracketed item [" + inner + "].", pos);
    }

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        if (s.Length is 0 or > 8)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static NumberFormatCondition ParseCondition(string inner, int pos)
    {
        int i = 0;
        while (i < inner.Length && inner[i] is '<' or '>' or '=')
        {
            i++;
        }

        string op = inner.Substring(0, i);
        if (op is not ("=" or "<>" or "<" or ">" or "<=" or ">=" or "=="))
        {
            throw new Fail("numfmt.invalid-condition", "Unknown comparison operator '" + op + "'.", pos);
        }

        if (op == "==")
        {
            op = "=";
        }

        string num = inner.Substring(i).Trim();
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || double.IsNaN(v) || double.IsInfinity(v))
        {
            throw new Fail("numfmt.invalid-condition", "The condition [" + inner + "] has no number.", pos);
        }

        return new NumberFormatCondition(op, v);
    }

    // Tokenises a run of letters. Returns the index of the last character consumed.
    private static int ParseLetter(string src, int i, int pos, List<Element> toks)
    {
        char c = src[i];
        char lc = char.ToLowerInvariant(c);
        if (lc == 'g' && string.Compare(src, i, "General", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
        {
            toks.Add(new Element(EK.General));
            return i + 6;
        }

        bool expShape = lc == 'e' && i + 1 < src.Length && src[i + 1] is '+' or '-';
        if (expShape && c == 'e' && i + 2 < src.Length && src[i + 2] is '0' or '#')
        {
            // Observed in Excel: only the capital letter starts an exponent; "0.00e+00" is invalid.
            throw new Fail("numfmt.exponent-without-digits", "The exponent code is a capital E.", i);
        }

        if (expShape)
        {
            toks.Add(new Element(EK.Exp, src[i + 1] == '+' ? 1 : 0, null, c));
            return i + 1;
        }

        if (lc == 'a')
        {
            if (string.Compare(src, i, "AM/PM", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                // Observed in Excel: "am/pm" in lower case still shows AM and PM in capitals; only "a/p" shows lower case.
                toks.Add(new Element(EK.AmPm, 0));
                return i + 4;
            }

            if (string.Compare(src, i, "A/P", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
            {
                toks.Add(new Element(EK.AmPm, c == 'a' ? 3 : 2));
                return i + 2;
            }
        }

        for (int pi = 0; pi < AmPmPairs.Length; pi++)
        {
            string pair = AmPmPairs[pi];
            if (string.CompareOrdinal(src, i, pair, 0, pair.Length) == 0)
            {
                toks.Add(new Element(EK.AmPm, 4 + pi));
                return i + pair.Length - 1;
            }
        }

        if (lc is 'y' or 'm' or 'd' or 'h' or 's' or 'e' or 'g' or 'b')
        {
            int j = i;
            while (j + 1 < src.Length && char.ToLowerInvariant(src[j + 1]) == lc)
            {
                j++;
            }

            int n = j - i + 1;
            switch (lc)
            {
                case 'y':
                    toks.Add(new Element(EK.Year, n <= 2 ? 2 : 4));
                    break;
                case 'm':
                    if (n > 5)
                    {
                        throw new Fail("numfmt.invalid-date-code", "More than five m.", pos);
                    }

                    toks.Add(new Element(EK.Month, n));
                    break;
                case 'd':
                    if (n > 4)
                    {
                        throw new Fail("numfmt.invalid-date-code", "More than four d.", pos);
                    }

                    toks.Add(new Element(EK.Day, n));
                    break;
                case 'h':
                    if (n > 2)
                    {
                        throw new Fail("numfmt.invalid-date-code", "More than two h.", pos);
                    }

                    toks.Add(new Element(EK.Hour, n));
                    break;
                case 's':
                    if (n > 2)
                    {
                        throw new Fail("numfmt.invalid-date-code", "More than two s.", pos);
                    }

                    toks.Add(new Element(EK.Second, n));
                    break;
                case 'e':
                    toks.Add(new Element(EK.EraYear, n <= 1 ? 1 : 2));
                    break;
                case 'g':
                    toks.Add(new Element(EK.EraName, Math.Min(n, 3)));
                    break;
                default:
                    toks.Add(new Element(EK.BuddhistYear, n <= 2 ? 2 : 4));
                    break;
            }

            return j;
        }

        if (c < 128 && char.IsAsciiLetter(c))
        {
            throw new Fail("numfmt.unexpected-character", "The letter '" + c + "' must be quoted or escaped.", pos);
        }

        // Any other character (currency signs, CJK text, symbols) displays as itself.
        int len = char.IsHighSurrogate(c) && i + 1 < src.Length ? 2 : 1;
        toks.Add(new Element(EK.Literal, 0, src.Substring(i, len)));
        return i + len - 1;
    }

    private static NumberFormatSection BuildSection(
        string src, int start, List<Element> toks, NumberFormatCondition? cond, NumberFormatColor? color, int locale, int calendar, int dbnum, bool currencyTag)
    {
        bool hasDate = false, hasTime = false, hasDigits = false, hasGeneral = false, hasAt = false, hasPercent = false, hasFill = false, hasAmPm = false;
        bool hasExp = false;
        foreach (Element e in toks)
        {
            switch (e.Kind)
            {
                case EK.Year or EK.Month or EK.Day or EK.EraYear or EK.EraName or EK.BuddhistYear:
                    hasDate = true;
                    break;
                case EK.Hour or EK.Minute or EK.Second or EK.ElapsedHour or EK.ElapsedMinute or EK.ElapsedSecond:
                    hasTime = true;
                    break;
                case EK.AmPm:
                    hasTime = true;
                    hasAmPm = true;
                    break;
                case EK.Zero or EK.Hash or EK.Q:
                    hasDigits = true;
                    break;
                case EK.General:
                    hasGeneral = true;
                    break;
                case EK.At:
                    hasAt = true;
                    break;
                case EK.Percent:
                    hasPercent = true;
                    break;
                case EK.Fill:
                    hasFill = true;
                    break;
                case EK.Exp:
                    hasExp = true;
                    break;
            }
        }

        bool isDate = hasDate || hasTime;
        NumberFormatSection section;
        if (isDate)
        {
            if (hasExp || hasGeneral)
            {
                throw new Fail("numfmt.mixed-codes", "A date or time section cannot also have exponent or General codes.", start);
            }

            ResolveDate(toks, start);
            hasDate = false;
            hasTime = false;
            foreach (Element e in toks)
            {
                switch (e.Kind)
                {
                    case EK.Year or EK.Month or EK.Day or EK.EraYear or EK.EraName or EK.BuddhistYear:
                        hasDate = true;
                        break;
                    case EK.Hour or EK.Minute or EK.Second or EK.ElapsedHour or EK.ElapsedMinute or EK.ElapsedSecond or EK.AmPm or EK.FractionSecond:
                        hasTime = true;
                        break;
                }
            }

            section = new NumberFormatSection(src, NumberFormatSectionKind.DateTime, toks.ToArray());
            section.HasDateCodes = hasDate;
            section.HasTimeCodes = hasTime;
            section.HasAmPm = hasAmPm;
            foreach (Element e in toks)
            {
                if (e.Kind == EK.FractionSecond)
                {
                    section.FractionSecondDigits = Math.Max(section.FractionSecondDigits, e.N);
                }
            }

            if (section.FractionSecondDigits < 0)
            {
                section.FractionSecondDigits = 0;
            }
        }
        else if (hasGeneral)
        {
            if (hasDigits || hasExp)
            {
                throw new Fail("numfmt.mixed-codes", "General cannot be combined with digit placeholders.", start);
            }

            section = new NumberFormatSection(src, NumberFormatSectionKind.General, StripNumeric(toks).ToArray());
        }
        else if (hasDigits || hasPercent || toks.Exists(t => t.Kind == EK.Point))
        {
            // Observed in Excel: @ beside digit placeholders makes the code invalid.
            if (hasAt && hasDigits)
            {
                throw new Fail("numfmt.mixed-codes", "The text placeholder @ cannot be combined with digit placeholders.", start);
            }

            section = BuildNumeric(src, start, toks);
        }
        else
        {
            var lits = new List<Element>(toks.Count);
            foreach (Element e in toks)
            {
                lits.Add(e.Kind is EK.Comma or EK.Slash or EK.Point or EK.Digit or EK.Exp ? new Element(EK.Literal, 0, SymbolText(e)) : e);
            }

            section = new NumberFormatSection(src, lits.Count == 0 ? NumberFormatSectionKind.Empty : NumberFormatSectionKind.Literal, lits.ToArray());
        }

        section.Condition = cond;
        section.Color = color;
        section.LocaleId = locale;
        section.CalendarId = calendar & 0xFF;
        section.NumeralSystem = calendar >> 8;
        calendar &= 0xFF;
        section.DbNum = dbnum;
        section.ContainsText = hasAt;
        section.HasPercent = hasPercent;
        section.HasFill = hasFill;
        section.HasCurrency = currencyTag || toks.Exists(t => t.Kind == EK.Literal && t.Text is { Length: > 0 } x && IsCurrencyText(x));
        if (calendar == 3)
        {
            section.Era = EraStyle.Japanese;
        }
        else if (calendar == 4)
        {
            section.Era = EraStyle.Minguo;
        }
        else if (calendar == 7)
        {
            section.Era = EraStyle.Buddhist;
        }
        else if (locale != 0 && NumberFormatCulture.TryGet(locale, out NumberFormatCulture? c) && c is not null)
        {
            section.Era = c.EraStyle;
        }

        return section;
    }

    private static bool IsCurrencyText(string x) =>
        x.Length <= 3 && (x.Contains('$', StringComparison.Ordinal) || x.Contains('€', StringComparison.Ordinal) || x.Contains('£', StringComparison.Ordinal)
            || x.Contains('¥', StringComparison.Ordinal) || x.Contains('₩', StringComparison.Ordinal) || x.Contains('₽', StringComparison.Ordinal));

    private static string SymbolText(Element e) => e.Kind switch
    {
        EK.Comma => ",",
        EK.Slash => "/",
        EK.Point => ".",
        EK.Digit => ((char)('0' + e.N)).ToString(),
        _ => e.Ch.ToString(),
    };

    private static List<Element> StripNumeric(List<Element> toks)
    {
        var r = new List<Element>(toks.Count);
        foreach (Element e in toks)
        {
            r.Add(e.Kind is EK.Comma or EK.Slash or EK.Point or EK.Digit or EK.Percent ? new Element(EK.Literal, 0, e.Kind == EK.Percent ? "%" : SymbolText(e)) : e);
        }

        return r;
    }

    // Dates: decides minute versus month, turns ".0" after seconds into fractional seconds and treats the rest as literals.
    private static void ResolveDate(List<Element> toks, int start)
    {
        // Fractional seconds: a point and one to three (or more) zeros right after seconds.
        for (int i = 0; i < toks.Count; i++)
        {
            if (toks[i].Kind == EK.Point && i > 0 && toks[i - 1].Kind is EK.Second or EK.ElapsedSecond && i + 1 < toks.Count && toks[i + 1].Kind == EK.Zero)
            {
                int n = 0;
                while (i + 1 + n < toks.Count && toks[i + 1 + n].Kind == EK.Zero)
                {
                    n++;
                }

                toks[i] = new Element(EK.FractionSecond, n);
                toks.RemoveRange(i + 1, n);
            }
        }

        for (int i = 0; i < toks.Count; i++)
        {
            Element e = toks[i];
            if (e.Kind is EK.Zero or EK.Hash or EK.Q)
            {
                throw new Fail("numfmt.mixed-codes", "Digit placeholders cannot appear in a date or time section.", start);
            }

            if (e.Kind is EK.Point or EK.Comma or EK.Slash or EK.Digit or EK.Percent)
            {
                toks[i] = new Element(EK.Literal, 0, e.Kind == EK.Percent ? "%" : SymbolText(e));
            }
        }

        // m or mm is minutes right after an hour code or right before a seconds code (literals in between are skipped).
        for (int i = 0; i < toks.Count; i++)
        {
            if (toks[i].Kind != EK.Month || toks[i].N > 2)
            {
                continue;
            }

            bool minute = false;
            for (int j = i - 1; j >= 0; j--)
            {
                if (toks[j].Kind is EK.Literal or EK.Fill)
                {
                    continue;
                }

                minute = toks[j].Kind is EK.Hour or EK.ElapsedHour;
                break;
            }

            if (!minute)
            {
                for (int j = i + 1; j < toks.Count; j++)
                {
                    if (toks[j].Kind is EK.Literal or EK.Fill)
                    {
                        continue;
                    }

                    minute = toks[j].Kind is EK.Second or EK.ElapsedSecond;
                    break;
                }
            }

            if (minute)
            {
                toks[i] = new Element(EK.Minute, toks[i].N);
            }
        }
    }

    private static NumberFormatSection BuildNumeric(string src, int start, List<Element> toks)
    {
        int pointIdx = -1, expIdx = -1;
        for (int i = 0; i < toks.Count; i++)
        {
            if (toks[i].Kind == EK.Point)
            {
                if (pointIdx >= 0)
                {
                    throw new Fail("numfmt.multiple-decimal-points", "A section has more than one decimal point.", start);
                }

                pointIdx = i;
            }
            else if (toks[i].Kind == EK.Exp)
            {
                if (expIdx >= 0)
                {
                    throw new Fail("numfmt.multiple-exponents", "A section has more than one exponent code.", start);
                }

                expIdx = i;
            }
        }

        int expDigits = 0;
        if (expIdx >= 0)
        {
            int k = expIdx + 1;
            while (k < toks.Count && toks[k].Kind is EK.Zero or EK.Hash or EK.Q)
            {
                k++;
            }

            expDigits = k - expIdx - 1;
            if (expDigits == 0)
            {
                throw new Fail("numfmt.exponent-without-digits", "E+ and E- need 0 or # after them.", start);
            }

            toks.RemoveRange(expIdx + 1, expDigits);
        }

        // Fraction: a placeholder run, a slash, then placeholders or a fixed denominator.
        int slash = -1;
        for (int i = 1; i < toks.Count - 1; i++)
        {
            if (toks[i].Kind == EK.Slash && toks[i - 1].Kind is EK.Zero or EK.Hash or EK.Q && toks[i + 1].Kind is EK.Zero or EK.Hash or EK.Q or EK.Digit)
            {
                slash = i;
                break;
            }
        }

        int numStart = -1, denPlaceholders = 0, fixedDen = 0;
        if (slash >= 0)
        {
            if (pointIdx >= 0 || expIdx >= 0)
            {
                throw new Fail("numfmt.mixed-codes", "A fraction cannot have a decimal point or an exponent.", start);
            }

            numStart = slash - 1;
            while (numStart > 0 && toks[numStart - 1].Kind is EK.Zero or EK.Hash or EK.Q)
            {
                numStart--;
            }

            int k = slash + 1;
            if (toks[k].Kind == EK.Digit)
            {
                while (k < toks.Count && (toks[k].Kind == EK.Digit || toks[k].Kind == EK.Zero))
                {
                    fixedDen = checked((fixedDen * 10) + (toks[k].Kind == EK.Digit ? toks[k].N : 0));
                    k++;
                    if (fixedDen > 99999)
                    {
                        throw new Fail("numfmt.fraction-denominator", "A fixed denominator is at most 99999.", start);
                    }
                }
            }
            else
            {
                while (k < toks.Count && toks[k].Kind is EK.Zero or EK.Hash or EK.Q)
                {
                    denPlaceholders++;
                    k++;
                }

                if (denPlaceholders > 5)
                {
                    throw new Fail("numfmt.fraction-denominator", "A fraction has at most five denominator digits.", start);
                }
            }

            toks.RemoveRange(slash + 1, k - slash - 1);
        }

        // Commas: between placeholders they group thousands; after the last placeholder they divide by 1000.
        int firstPh = -1, lastIntPh = -1, lastPh = -1;
        int intEnd = pointIdx >= 0 ? pointIdx : (expIdx >= 0 ? expIdx : (numStart >= 0 ? numStart : toks.Count));
        for (int i = 0; i < toks.Count; i++)
        {
            if (toks[i].Kind is EK.Zero or EK.Hash or EK.Q)
            {
                if (firstPh < 0)
                {
                    firstPh = i;
                }

                lastPh = i;
                if (i < intEnd)
                {
                    lastIntPh = i;
                }
            }
        }

        bool grouping = false;
        int scale = 0;
        var outToks = new List<Element>(toks.Count);
        int newPoint = -1;
        for (int i = 0; i < toks.Count; i++)
        {
            Element e = toks[i];
            if (e.Kind == EK.Comma)
            {
                bool before = firstPh >= 0 && firstPh < i;
                if (before && i < intEnd && lastIntPh > i)
                {
                    grouping = true;
                    continue;
                }

                // After the last placeholder of its region a comma divides by 1000.
                if (before && ((i < intEnd && lastIntPh < i) || (i >= intEnd && lastPh < i)))
                {
                    scale++;
                    continue;
                }

                outToks.Add(new Element(EK.Literal, 0, ","));
                continue;
            }

            if (e.Kind == EK.Point)
            {
                newPoint = outToks.Count;
            }

            outToks.Add(e);
        }

        // Re-derive indexes on the final element list.
        int p = 0, f = 0, expAt = -1, numAt = -1, slashAt = -1;
        int percent = 0;
        for (int i = 0; i < outToks.Count; i++)
        {
            Element e = outToks[i];
            if (e.Kind == EK.Exp)
            {
                expAt = i;
            }
            else if (e.Kind == EK.Slash && slash >= 0 && slashAt < 0 && i > 0 && outToks[i - 1].Kind is EK.Zero or EK.Hash or EK.Q)
            {
                slashAt = i;
            }
            else if (e.Kind == EK.Percent)
            {
                percent++;
            }
        }

        if (slashAt >= 0)
        {
            numAt = slashAt - 1;
            while (numAt > 0 && outToks[numAt - 1].Kind is EK.Zero or EK.Hash or EK.Q)
            {
                numAt--;
            }
        }

        int whole = slashAt >= 0 ? numAt : (newPoint >= 0 ? newPoint : (expAt >= 0 ? expAt : outToks.Count));
        for (int i = 0; i < outToks.Count; i++)
        {
            if (outToks[i].Kind is EK.Zero or EK.Hash or EK.Q)
            {
                if (i < whole)
                {
                    p++;
                }
                else if (newPoint >= 0 && i > newPoint && (expAt < 0 || i < expAt))
                {
                    f++;
                }
            }
        }

        NumberFormatSectionKind kind = slashAt >= 0 ? NumberFormatSectionKind.Fraction : expAt >= 0 ? NumberFormatSectionKind.Scientific : NumberFormatSectionKind.Number;
        var s = new NumberFormatSection(src, kind, outToks.ToArray())
        {
            IntegerPlaceholders = p,
            FractionPlaceholders = f,
            UsesGrouping = grouping,
            ScaleCommas = scale,
            PercentCount = percent,
            HasPoint = newPoint >= 0,
            ExponentDigits = expDigits,
            ExponentIndex = expAt,
            NumeratorStart = numAt,
            SlashIndex = slashAt,
            DenominatorPlaceholders = denPlaceholders,
            FixedDenominator = fixedDen,
        };
        return s;
    }
}
