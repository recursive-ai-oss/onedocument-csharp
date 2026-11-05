using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Equations;

// How the atoms of a formula space themselves (TeX's classes, which Word's math spacing also follows).
internal enum AtomClass
{
    Ord = 0,
    Op,
    Bin,
    Rel,
    Open,
    Close,
    Punct,
    Inner,
}

// Characters of math text: which class they have and how letters take the shapes of the Mathematical Alphanumeric Symbols block
// (Unicode chapter 22.2) for italic, bold, script, fraktur, double-struck, sans-serif and monospace text.
internal static class MathText
{
    // Spacing in 18ths of an em between a left and a right atom class (TeXbook ch. 18, Table 7.1; the Unicode Technical Note on
    // plain-text math gives the same numbers). A value of 100 + n applies only in display and text styles, never in scripts.
    private static readonly int[,] Spacing =
    {
        //          Ord     Op     Bin    Rel    Open   Close  Punct  Inner
        /* Ord   */ { 0,     3,     104,   105,   0,     0,     0,     103 },
        /* Op    */ { 3,     3,     0,     105,   0,     0,     0,     103 },
        /* Bin   */ { 104,   104,   0,     0,     104,   0,     0,     104 },
        /* Rel   */ { 105,   105,   0,     0,     105,   0,     0,     105 },
        /* Open  */ { 0,     0,     0,     0,     0,     0,     0,     0 },
        /* Close */ { 0,     3,     104,   105,   0,     0,     0,     103 },
        /* Punct */ { 103,   103,   0,     103,   103,   103,   103,   103 },
        /* Inner */ { 103,   3,     104,   105,   103,   0,     103,   103 },
    };

    public static double SpaceBetween(AtomClass left, AtomClass right, bool script)
    {
        int v = Spacing[(int)left, (int)right];
        if (v >= 100)
        {
            return script ? 0 : (v - 100) / 18.0;
        }

        return v / 18.0;
    }

    public static AtomClass ClassOf(int cp)
    {
        switch (cp)
        {
            case '(' or '[' or '{' or 0x27E8 or 0x2308 or 0x230A or 0x27E6 or 0x2329:
                return AtomClass.Open;
            case ')' or ']' or '}' or 0x27E9 or 0x2309 or 0x230B or 0x27E7 or 0x232A:
                return AtomClass.Close;
            case ',' or ';':
                return AtomClass.Punct;
            case '+' or '-' or '*' or 0x2212 or 0x00B1 or 0x2213 or 0x00D7 or 0x00F7 or 0x2217 or 0x2218 or 0x2219 or 0x00B7 or 0x2022 or 0x22C5
                or 0x2227 or 0x2228 or 0x2229 or 0x222A or 0x2295 or 0x2296 or 0x2297 or 0x2298 or 0x2299 or 0x228E or 0x2293 or 0x2294 or 0x2216
                or 0x22C6 or 0x2020 or 0x2021 or 0x22C4 or 0x25CB or 0x2240 or 0x2A3F:
                return AtomClass.Bin;
            case '=' or '<' or '>' or 0x2260 or 0x2264 or 0x2265 or 0x2248 or 0x2261 or 0x223C or 0x2243 or 0x2245 or 0x221D or 0x2208 or 0x2209
                or 0x220B or 0x2282 or 0x2283 or 0x2286 or 0x2287 or 0x2284 or 0x2285 or 0x22A5 or 0x2225 or 0x2223 or 0x226A or 0x226B or 0x227A
                or 0x227B or 0x2190 or 0x2192 or 0x2194 or 0x21D0 or 0x21D2 or 0x21D4 or 0x21A6 or 0x27F6 or 0x27F5 or 0x27F7 or 0x27F9 or 0x27F8
                or 0x27FA or 0x225C or 0x225D or 0x2254 or 0x2237 or 0x2249 or 0x2262 or 0x2241 or 0x2244 or 0x2247 or 0x2270 or 0x2271 or 0x22A2
                or 0x22A3 or 0x22A8 or 0x2191 or 0x2193 or 0x2195 or 0x2197 or 0x2198 or 0x21BC or 0x21C0 or 0x21CC:
                return AtomClass.Rel;
            case 0x2211 or 0x220F or 0x2210 or 0x222B or 0x222C or 0x222D or 0x222E or 0x22C3 or 0x22C2 or 0x2A01 or 0x2A02 or 0x2A00 or 0x2A04
                or 0x22C1 or 0x22C0:
                return AtomClass.Op;
            default:
                return AtomClass.Ord;
        }
    }

    // Large operators that grow in display style.
    public static bool IsLargeOperator(int cp) => cp is 0x2211 or 0x220F or 0x2210 or 0x222B or 0x222C or 0x222D or 0x222E or 0x222F or 0x2230
        or 0x22C3 or 0x22C2 or 0x2A01 or 0x2A02 or 0x2A00 or 0x2A04 or 0x22C1 or 0x22C0;

    public static bool IsIntegral(int cp) => cp is >= 0x222B and <= 0x2233 or >= 0x2A0C and <= 0x2A1C;

    // Word shows a typed hyphen-minus as the minus sign and an asterisk as the asterisk operator.
    public static int Normalise(int cp) => cp switch
    {
        '-' => 0x2212,
        '*' => 0x2217,
        _ => cp,
    };

    // The code points a run of math text shows. `italicDefault` is true for text that has no explicit style (variables are italic).
    public static string Styled(string text, MathRunShape shape)
    {
        if (!shape.Italic && !shape.Bold && shape.Script == RecursiveAI.OneDocument.Equations.MathScript.Roman)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 4);
        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.ConvertToUtf32(text, i);
            if (char.IsHighSurrogate(text[i]))
            {
                i++;
            }

            sb.Append(char.ConvertFromUtf32(Map(cp, shape)));
        }

        return sb.ToString();
    }

    private static int Map(int cp, MathRunShape shape)
    {
        bool isUpperLatin = cp is >= 'A' and <= 'Z';
        bool isLowerLatin = cp is >= 'a' and <= 'z';
        bool isUpperGreek = cp is >= 0x391 and <= 0x3A9 and not 0x3A2;
        bool isLowerGreek = cp is >= 0x3B1 and <= 0x3C9;
        bool isDigit = cp is >= '0' and <= '9';
        if (!(isUpperLatin || isLowerLatin || isUpperGreek || isLowerGreek || isDigit))
        {
            return cp;
        }

        bool bold = shape.Bold;
        bool italic = shape.Italic;
        if (shape.Script != RecursiveAI.OneDocument.Equations.MathScript.Roman)
        {
            return MapScript(cp, shape.Script, bold, italic, isUpperLatin, isLowerLatin, isDigit);
        }

        if (isDigit)
        {
            return bold ? 0x1D7CE + (cp - '0') : cp;
        }

        if (isUpperLatin || isLowerLatin)
        {
            int idx = isUpperLatin ? cp - 'A' : 26 + (cp - 'a');
            if (!bold && !italic)
            {
                return cp;
            }

            int start = bold ? (italic ? 0x1D468 : 0x1D400) : 0x1D434;
            if (!bold && cp == 'h')
            {
                return 0x210E;
            }

            return start + idx;
        }

        // Greek: italic and bold forms exist as capitals and small letters.
        if (!bold && !italic)
        {
            return cp;
        }

        int baseUpper = bold ? (italic ? 0x1D71C : 0x1D6A8) : 0x1D6E2;
        int baseLower = bold ? (italic ? 0x1D736 : 0x1D6C2) : 0x1D6FC;
        return isUpperGreek ? baseUpper + (cp - 0x391) : baseLower + (cp - 0x3B1);
    }

    private static int MapScript(int cp, RecursiveAI.OneDocument.Equations.MathScript script, bool bold, bool italic, bool upper, bool lower, bool digit)
    {
        if (digit)
        {
            return script switch
            {
                RecursiveAI.OneDocument.Equations.MathScript.DoubleStruck => 0x1D7D8 + (cp - '0'),
                RecursiveAI.OneDocument.Equations.MathScript.SansSerif => (bold ? 0x1D7EC : 0x1D7E2) + (cp - '0'),
                RecursiveAI.OneDocument.Equations.MathScript.Monospace => 0x1D7F6 + (cp - '0'),
                _ => bold ? 0x1D7CE + (cp - '0') : cp,
            };
        }

        if (!upper && !lower)
        {
            return cp;
        }

        int idx = upper ? cp - 'A' : 26 + (cp - 'a');
        int start;
        switch (script)
        {
            case RecursiveAI.OneDocument.Equations.MathScript.Script:
                start = bold ? 0x1D4D0 : 0x1D49C;
                break;
            case RecursiveAI.OneDocument.Equations.MathScript.Fraktur:
                start = bold ? 0x1D56C : 0x1D504;
                break;
            case RecursiveAI.OneDocument.Equations.MathScript.DoubleStruck:
                start = 0x1D538;
                break;
            case RecursiveAI.OneDocument.Equations.MathScript.SansSerif:
                start = bold ? (italic ? 0x1D63C : 0x1D5D4) : (italic ? 0x1D608 : 0x1D5A0);
                break;
            default:
                start = 0x1D670;
                break;
        }

        int mapped = start + idx;
        return mapped switch
        {
            0x1D49D => 0x212C,
            0x1D4A0 => 0x2130,
            0x1D4A1 => 0x2131,
            0x1D4A3 => 0x210B,
            0x1D4A4 => 0x2110,
            0x1D4A7 => 0x2112,
            0x1D4A8 => 0x2133,
            0x1D4AD => 0x211B,
            0x1D4BA => 0x212F,
            0x1D4BC => 0x210A,
            0x1D4C4 => 0x2134,
            0x1D506 => 0x212D,
            0x1D50B => 0x210C,
            0x1D50C => 0x2111,
            0x1D515 => 0x211C,
            0x1D51D => 0x2128,
            0x1D53A => 0x2102,
            0x1D53F => 0x210D,
            0x1D545 => 0x2115,
            0x1D547 => 0x2119,
            0x1D548 => 0x211A,
            0x1D549 => 0x211D,
            0x1D551 => 0x2124,
            _ => mapped,
        };
    }
}

// The effective shape of a run's letters, after the run's style, script and the italic-by-default rule.
internal readonly record struct MathRunShape(RecursiveAI.OneDocument.Equations.MathScript Script, bool Bold, bool Italic);
