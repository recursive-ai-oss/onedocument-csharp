using System;
using System.Text;

namespace RecursiveAI.OneOffice.NumberFormats;

// ECMA-376 Part 1 §18.8.30 (numFmt): the built-in format IDs 0 to 49 need no definition in styles.xml. The currency and date entries
// follow the reader's locale; the East Asian IDs 27 to 36 and 50 to 58 are given with their Taiwan-locale definitions.
internal static class BuiltInFormats
{
    private static readonly string?[] Fixed =
    [
        "General", "0", "0.00", "#,##0", "#,##0.00", null, null, null, null, "0%", "0.00%", "0.00E+00", "# ?/?", "# ??/??",
        null, "d-mmm-yy", "d-mmm", "mmm-yy", "h:mm AM/PM", "h:mm:ss AM/PM", "h:mm", "h:mm:ss", null,
        null, null, null, null,
        null, null, null, null, null, null, null, null, null, null,
        "#,##0_);(#,##0)", "#,##0_);[Red](#,##0)", "#,##0.00_);(#,##0.00)", "#,##0.00_);[Red](#,##0.00)",
        null, null, null, null,
        "mm:ss", "[h]:mm:ss", "mmss.0", "##0.0E+0", "@",
    ];

    // ECMA-376 Part 1 §18.8.30 (numFmt): IDs 27 to 36 and 50 to 58 are defined per East Asian locale; this holds them in the order
    // 27..36, 50..58. Other locales leave these IDs to the writer's explicit format codes.
    private static readonly string[] ZhTw =
    [
        "[$-404]e/m/d", "[$-404]e\"年\"m\"月\"d\"日\"", "[$-404]e\"年\"m\"月\"d\"日\"", "m/d/yy", "yyyy\"年\"m\"月\"d\"日\"",
        "hh\"時\"mm\"分\"", "hh\"時\"mm\"分\"ss\"秒\"", "上午/下午hh\"時\"mm\"分\"", "上午/下午hh\"時\"mm\"分\"ss\"秒\"", "[$-404]e/m/d",
        "[$-404]e/m/d", "[$-404]e\"年\"m\"月\"d\"日\"", "上午/下午hh\"時\"mm\"分\"", "上午/下午hh\"時\"mm\"分\"ss\"秒\"",
        "[$-404]e\"年\"m\"月\"d\"日\"", "上午/下午hh\"時\"mm\"分\"", "上午/下午hh\"時\"mm\"分\"ss\"秒\"", "[$-404]e/m/d", "[$-404]e\"年\"m\"月\"d\"日\"",
    ];

    private static readonly string[] ZhCn =
    [
        "yyyy\"年\"m\"月\"", "m\"月\"d\"日\"", "m\"月\"d\"日\"", "m-d-yy", "yyyy\"年\"m\"月\"d\"日\"",
        "h\"时\"mm\"分\"", "h\"时\"mm\"分\"ss\"秒\"", "上午/下午h\"时\"mm\"分\"", "上午/下午h\"时\"mm\"分\"ss\"秒\"", "yyyy\"年\"m\"月\"",
        "yyyy\"年\"m\"月\"", "m\"月\"d\"日\"", "yyyy\"年\"m\"月\"", "m\"月\"d\"日\"",
        "m\"月\"d\"日\"", "上午/下午h\"时\"mm\"分\"", "上午/下午h\"时\"mm\"分\"ss\"秒\"", "yyyy\"年\"m\"月\"", "m\"月\"d\"日\"",
    ];

    private static readonly string[] Ja =
    [
        "[$-411]ge.m.d", "[$-411]ggge\"年\"m\"月\"d\"日\"", "[$-411]ggge\"年\"m\"月\"d\"日\"", "m/d/yy", "yyyy\"年\"m\"月\"d\"日\"",
        "h\"時\"mm\"分\"", "h\"時\"mm\"分\"ss\"秒\"", "yyyy\"年\"m\"月\"", "m\"月\"d\"日\"", "[$-411]ge.m.d",
        "[$-411]ge.m.d", "[$-411]ggge\"年\"m\"月\"d\"日\"", "yyyy\"年\"m\"月\"", "m\"月\"d\"日\"",
        "[$-411]ggge\"年\"m\"月\"d\"日\"", "yyyy\"年\"m\"月\"", "m\"月\"d\"日\"", "[$-411]ge.m.d", "[$-411]ggge\"年\"m\"月\"d\"日\"",
    ];

    private static readonly string[] Ko =
    [
        "yyyy\"年\" mm\"月\" dd\"日\"", "mm-dd", "mm-dd", "mm-dd-yy", "yyyy\"년\" mm\"월\" dd\"일\"",
        "h\"시\" mm\"분\"", "h\"시\" mm\"분\" ss\"초\"", "yyyy-mm-dd", "yyyy-mm-dd", "yyyy\"年\" mm\"月\" dd\"日\"",
        "yyyy\"年\" mm\"月\" dd\"日\"", "mm-dd", "yyyy-mm-dd", "yyyy-mm-dd",
        "mm-dd", "yyyy-mm-dd", "yyyy-mm-dd", "yyyy\"年\" mm\"月\" dd\"日\"", "mm-dd",
    ];

    private static string? EastAsian(int id, NumberFormatCulture c)
    {
        string[]? t = c.Lcid switch
        {
            0x0404 => ZhTw,
            0x0804 => ZhCn,
            0x0411 => Ja,
            0x0412 => Ko,
            _ => null,
        };
        return t?[id <= 36 ? id - 27 : id - 50 + 10];
    }

    internal static string? GetCode(int id, NumberFormatCulture c)
    {
        if (id is >= 27 and <= 36 or >= 50 and <= 58)
        {
            return EastAsian(id, c);
        }

        if (id < 0 || id >= Fixed.Length)
        {
            return null;
        }

        string? fixedCode = Fixed[id];
        if (fixedCode is not null)
        {
            return fixedCode;
        }

        switch (id)
        {
            case 5: return Currency(c, 0, false);
            case 6: return Currency(c, 0, true);
            case 7: return Currency(c, 2, false);
            case 8: return Currency(c, 2, true);
            case 14: return c.ShortDatePattern;
            case 22: return c.ShortDatePattern + " h:mm";
            case 41: return "_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"_);_(@_)";
            case 42: return Accounting(c, 0);
            case 43: return "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)";
            case 44: return Accounting(c, 2);
            default: return null;
        }
    }

    private static string Digits(int decimals) => decimals == 0 ? "#,##0" : "#,##0.00";

    private static string Currency(NumberFormatCulture c, int decimals, bool red)
    {
        string num = Digits(decimals);
        string neg = red ? "[Red]" : string.Empty;
        string sym = "\"" + c.CurrencySymbol + "\"";
        return c.CurrencyAfterNumber
            ? num + "\\ " + sym + "_);" + neg + "\\(" + num + "\\ " + sym + "\\)"
            : sym + num + "_);" + neg + "\\(" + sym + num + "\\)";
    }

    private static string Accounting(NumberFormatCulture c, int decimals)
    {
        string num = Digits(decimals);
        string dash = decimals == 0 ? "\"-\"_)" : "\"-\"??_)";
        string sym = "\"" + c.CurrencySymbol + "\"";
        return c.CurrencyAfterNumber
            ? "_(* " + num + "_)\\ " + sym + ";_(* \\(" + num + "\\)\\ " + sym + ";_(* " + dash + "\\ " + sym + ";_(@_)"
            : "_(" + sym + "* " + num + "_);_(" + sym + "* \\(" + num + "\\);_(" + sym + "* " + dash + ";_(@_)";
    }

    internal static int FindId(string code)
    {
        for (int id = 0; id <= 49; id++)
        {
            string? c = GetCode(id, NumberFormatCulture.EnglishUnitedStates);
            if (c is not null && string.Equals(c, code, StringComparison.Ordinal))
            {
                return id;
            }
        }

        return code switch
        {
            "mm-dd-yy" => 14,
            "m/d/yy h:mm" => 22,
            _ => -1,
        };
    }
}

// CELL("format") codes. Microsoft's CELL function reference lists them: "G" General, "F0", ",0", "F2", ",2", "C0", "C2", "P0", "P2",
// "S2", "D1" to "D9", a "-" suffix when negative numbers are coloured and "()" when positive numbers are in parentheses.
internal static class CellFormatCodes
{
    internal static string For(NumberFormat f)
    {
        NumberFormatSection s = f.SectionArray[0];
        string code;
        switch (s.Kind)
        {
            case NumberFormatSectionKind.DateTime:
                code = DateCode(s);
                return code;
            case NumberFormatSectionKind.Number or NumberFormatSectionKind.Scientific:
                break;
            default:
                return "G";
        }

        int dec = s.FractionPlaceholders;
        string prefix = s.Kind == NumberFormatSectionKind.Scientific ? "S" : s.HasPercent ? "P" : s.HasCurrency ? "C" : s.UsesGrouping ? "," : "F";
        if (prefix == "," )
        {
            code = "," + dec;
        }
        else
        {
            code = prefix + dec;
        }

        if (f.Sections.Count > 1 && f.Sections[1].Color is not null)
        {
            code += "-";
        }

        if (HasParens(s) || (f.Sections.Count > 1 && HasParens(f.Sections[1])))
        {
            code += "()";
        }

        return code;
    }

    private static bool HasParens(NumberFormatSection s)
    {
        foreach (Element e in s.Elements)
        {
            if (e.Kind == EK.Literal && e.Text is "(" or ")")
            {
                return true;
            }
        }

        return false;
    }

    private static string DateCode(NumberFormatSection s)
    {
        bool y = false, d = false, mText = false, mNum = false, h = false, sec = false;
        foreach (Element e in s.Elements)
        {
            switch (e.Kind)
            {
                case EK.Year: y = true; break;
                case EK.Day: d = true; break;
                case EK.Month when e.N >= 3: mText = true; break;
                case EK.Month: mNum = true; break;
                case EK.Hour or EK.ElapsedHour or EK.Minute: h = true; break;
                case EK.Second: sec = true; break;
            }
        }

        if (h)
        {
            return s.HasAmPm ? (sec ? "D6" : "D7") : (sec ? "D8" : "D9");
        }

        if (y && d && mText)
        {
            return "D1";
        }

        if (d && mText)
        {
            return "D2";
        }

        if (y && mText)
        {
            return "D3";
        }

        if (y && (mNum || d))
        {
            return "D4";
        }

        if (d && mNum)
        {
            return "D5";
        }

        return "G";
    }
}
