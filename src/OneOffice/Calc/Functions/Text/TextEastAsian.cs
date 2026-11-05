using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneOffice.Calc;

internal static partial class TextFunctions
{
    private static void RegisterEastAsian(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("ASC", 1, 1, V, (_, a) => CalcValue.FromText(ToHalfWidth(Str(a[0])))));
        r.Add(FunctionDefinition.Create("DBCS", 1, 1, V, (_, a) => CalcValue.FromText(ToFullWidth(Str(a[0])))));
        r.Add(FunctionDefinition.Create("JIS", 1, 1, V, (_, a) => CalcValue.FromText(ToFullWidth(Str(a[0])))));
        r.Add(FunctionDefinition.Create("PHONETIC", 1, 1, [ArgKind.Any], Phonetic));
        r.Add(FunctionDefinition.Create("BAHTTEXT", 1, 1, V, BahtText));
    }

    // Full-width katakana and its half-width forms, in the same order (Unicode blocks Katakana and Halfwidth and Fullwidth Forms).
    private const string FullKana = "ァアィイゥウェエォオカキクケコサシスセソタチッツテトナニヌネノハヒフヘホマミムメモャヤュユョヨラリルレロワヲンー、。「」・゛゜";
    private const string HalfKana = "ｧｱｨｲｩｳｪｴｫｵｶｷｸｹｺｻｼｽｾｿﾀﾁｯﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓｬﾔｭﾕｮﾖﾗﾘﾙﾚﾛﾜｦﾝｰ､｡｢｣･ﾞﾟ";
    private const string Voiced = "ガギグゲゴザジズゼゾダヂヅデドバビブベボヴ";
    private const string VoicedBase = "ｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾊﾋﾌﾍﾎｳ";
    private const string SemiVoiced = "パピプペポ";
    private const string SemiVoicedBase = "ﾊﾋﾌﾍﾎ";

    // ASC: full-width letters, digits, punctuation and katakana become their half-width forms (voiced kana become two characters).
    internal static string ToHalfWidth(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == '\u3000')
            {
                sb.Append(' ');
            }
            else if (c is >= '\uFF01' and <= '\uFF5E')
            {
                sb.Append((char)(c - 0xFF01 + 0x21));
            }
            else if (c == '\uFFE5')
            {
                sb.Append('\u00A5');
            }
            else if (c == '\uFFE0')
            {
                sb.Append('\u00A2');
            }
            else if (c == '\uFFE1')
            {
                sb.Append('\u00A3');
            }
            else if (c == '\uFFE2')
            {
                sb.Append('\u00AC');
            }
            else if (c == '\uFFE3')
            {
                sb.Append('\u00AF');
            }
            else if (FullKana.IndexOf(c, StringComparison.Ordinal) is var k and >= 0)
            {
                sb.Append(HalfKana[k]);
            }
            else if (Voiced.IndexOf(c, StringComparison.Ordinal) is var v and >= 0)
            {
                sb.Append(VoicedBase[v]).Append('\uFF9E');
            }
            else if (SemiVoiced.IndexOf(c, StringComparison.Ordinal) is var p and >= 0)
            {
                sb.Append(SemiVoicedBase[p]).Append('\uFF9F');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // DBCS / JIS: the reverse of ASC; a half-width kana followed by a voicing mark joins into one voiced full-width kana.
    internal static string ToFullWidth(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ')
            {
                sb.Append('\u3000');
            }
            else if (c is >= '\u0021' and <= '\u007E')
            {
                sb.Append((char)(c - 0x21 + 0xFF01));
            }
            else if (c == '\u00A5')
            {
                sb.Append('\uFFE5');
            }
            else if (c == '\u00A2')
            {
                sb.Append('\uFFE0');
            }
            else if (c == '\u00A3')
            {
                sb.Append('\uFFE1');
            }
            else if (c == '\u00AC')
            {
                sb.Append('\uFFE2');
            }
            else if (c == '\u00AF')
            {
                sb.Append('\uFFE3');
            }
            else if (HalfKana.IndexOf(c, StringComparison.Ordinal) is var k and >= 0)
            {
                char next = i + 1 < s.Length ? s[i + 1] : '\0';
                int vi = VoicedBase.IndexOf(c, StringComparison.Ordinal);
                int pi = SemiVoicedBase.IndexOf(c, StringComparison.Ordinal);
                if (next == '\uFF9E' && vi >= 0)
                {
                    sb.Append(Voiced[vi]);
                    i++;
                }
                else if (next == '\uFF9F' && pi >= 0)
                {
                    sb.Append(SemiVoiced[pi]);
                    i++;
                }
                else
                {
                    sb.Append(FullKana[k]);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // PHONETIC(reference) gives the furigana stored with the cell; cells here carry none, so it is the cell's own text.
    private static CalcValue Phonetic(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcValue v = ev.ToScalar(a[0]);
        return v.IsError ? v : CalcValue.FromText(Str(v));
    }

    // ---- BAHTTEXT ----------------------------------------------------------------------------------------------------------------

    private static readonly string[] ThaiDigits = ["ศูนย์", "หนึ่ง", "สอง", "สาม", "สี่", "ห้า", "หก", "เจ็ด", "แปด", "เก้า"];
    private static readonly string[] ThaiPlaces = ["", "สิบ", "ร้อย", "พัน", "หมื่น", "แสน"];

    // Thai reading of a whole number: groups of six digits joined by ล้าน (million); 1 in the units place after a tens digit is เอ็ด, 2 in the
    // tens place is ยี่, and 1 in the tens place is silent.
    private static string ThaiNumber(ulong n)
    {
        if (n == 0)
        {
            return string.Empty;
        }

        if (n >= 1_000_000)
        {
            ulong high = n / 1_000_000;
            ulong low = n % 1_000_000;
            return ThaiNumber(high) + "ล้าน" + ThaiNumber(low);
        }

        var sb = new StringBuilder();
        string digits = n.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < digits.Length; i++)
        {
            int d = digits[i] - '0';
            int place = digits.Length - 1 - i;
            if (d == 0)
            {
                continue;
            }

            if (place == 1 && d == 1)
            {
                sb.Append(ThaiPlaces[1]);
            }
            else if (place == 1 && d == 2)
            {
                sb.Append("ยี่").Append(ThaiPlaces[1]);
            }
            else if (place == 0 && d == 1 && digits.Length > 1)
            {
                sb.Append("เอ็ด");
            }
            else
            {
                sb.Append(ThaiDigits[d]).Append(ThaiPlaces[place]);
            }
        }

        return sb.ToString();
    }

    private static CalcValue BahtText(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double x, out CalcValue e))
        {
            return e;
        }

        bool neg = x < 0;
        double rounded = Math.Round(Math.Abs(x), 2, MidpointRounding.AwayFromZero);
        if (rounded >= 1e15)
        {
            return Fx.Err(CalcError.Value);
        }

        ulong baht = (ulong)Math.Floor(rounded);
        int satang = (int)Math.Round((rounded - baht) * 100);
        var sb = new StringBuilder();
        if (neg && rounded > 0)
        {
            sb.Append("ลบ");
        }

        sb.Append(baht == 0 ? ThaiDigits[0] : ThaiNumber(baht)).Append("บาท");
        if (satang == 0)
        {
            sb.Append("ถ้วน");
        }
        else
        {
            sb.Append(ThaiNumber((ulong)satang)).Append("สตางค์");
        }

        return CalcValue.FromText(sb.ToString());
    }
}
