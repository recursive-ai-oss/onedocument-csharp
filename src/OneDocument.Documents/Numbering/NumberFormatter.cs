using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Documents.Numbering;

/// <summary>The text of one number in a given number format, and whether Word's own output for that format has been checked.</summary>
/// <param name="Text">The formatted number.</param>
/// <param name="NeedsWordOutput">True when the format is not one OneDocument reproduces exactly: the text is a best guess (or decimal for a format with no rule yet) and is listed for comparison against Word.</param>
public readonly record struct FormattedNumber(string Text, bool NeedsWordOutput);

/// <summary>
/// Number formats of list levels (ECMA-376 Part 1 §17.18.59, ST_NumberFormat). The formats listed in <see cref="ExactFormats"/> are
/// produced as Word does; the others are marked <see cref="FormattedNumber.NeedsWordOutput"/>.
/// </summary>
public static class NumberFormatter
{
    /// <summary>The formats reproduced exactly.</summary>
    public static IReadOnlyList<string> ExactFormats { get; } =
    [
        "decimal", "decimalZero", "upperRoman", "lowerRoman", "upperLetter", "lowerLetter", "ordinal",
        "bullet", "none", "decimalEnclosedCircle", "decimalFullWidth", "decimalHalfWidth",
    ];

    private static readonly string[] RomanSymbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
    private static readonly int[] RomanValues = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];

    private static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen",
        "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static readonly string[] OrdinalOnes =
    [
        "zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth",
        "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth",
    ];

    /// <summary>Formats a counter value.</summary>
    /// <param name="format">The <c>w:numFmt</c> value; null reads as <c>decimal</c>.</param>
    /// <param name="value">The counter value.</param>
    /// <returns>The text and whether it still needs comparing with Word's output.</returns>
    public static FormattedNumber Format(string? format, long value)
    {
        string inv = value.ToString(CultureInfo.InvariantCulture);
        switch (format ?? "decimal")
        {
            case "decimal":
                return new FormattedNumber(inv, false);
            case "decimalZero":
                return new FormattedNumber(value is >= 0 and < 10 ? "0" + inv : inv, false);
            case "upperRoman":
                return new FormattedNumber(value is >= 1 and <= 32767 ? Roman(value) : inv, value is < 1 or > 32767);
            case "lowerRoman":
                return new FormattedNumber(value is >= 1 and <= 32767 ? Roman(value).ToLowerInvariant() : inv, value is < 1 or > 32767);
            case "upperLetter":
                return new FormattedNumber(value >= 1 ? Letters(value, upper: true) : inv, value < 1);
            case "lowerLetter":
                return new FormattedNumber(value >= 1 ? Letters(value, upper: false) : inv, value < 1);
            case "ordinal":
                return new FormattedNumber(inv + OrdinalSuffix(value), false);
            case "bullet":
            case "none":
                return new FormattedNumber(string.Empty, false);
            case "decimalEnclosedCircle":
                // U+2460..U+2473 are the circled digits 1 to 20; beyond that Word falls back to plain digits (unchecked).
                return value is >= 1 and <= 20 ? new FormattedNumber(((char)(0x2460 + value - 1)).ToString(), false) : new FormattedNumber(inv, true);
            case "decimalFullWidth":
            case "decimalFullWidth2":
                return new FormattedNumber(value >= 0 ? FullWidth(inv) : inv, format != "decimalFullWidth");
            case "decimalHalfWidth":
                return new FormattedNumber(inv, false);
            case "cardinalText":
                return new FormattedNumber(value is >= 0 and < 1_000_000 ? Capitalize(CardinalWords(value)) : inv, true);
            case "ordinalText":
                return new FormattedNumber(value is >= 1 and < 1_000_000 ? Capitalize(OrdinalWords(value)) : inv, true);
            case "chicago":
                return new FormattedNumber(value >= 1 ? Chicago(value) : inv, true);
            case "hex":
                return new FormattedNumber(value.ToString("X", CultureInfo.InvariantCulture), true);
            default:
                // CJK, Hebrew, Arabic, Thai, Hindi, Russian and the other script counting systems have their own rules; until each is
                // checked against Word, they read as decimal and are flagged.
                return new FormattedNumber(inv, true);
        }
    }

    private static string Roman(long value)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < RomanValues.Length; i++)
        {
            while (value >= RomanValues[i])
            {
                sb.Append(RomanSymbols[i]);
                value -= RomanValues[i];
            }
        }

        return sb.ToString();
    }

    // Word's letter lists repeat the letter instead of adding a digit: a..z, aa..zz, aaa..zzz (§17.18.59, ST_NumberFormat upperLetter/lowerLetter).
    private static string Letters(long value, bool upper)
    {
        long index = (value - 1) % 26;
        long repeat = ((value - 1) / 26) + 1;
        char c = (char)((upper ? 'A' : 'a') + index);
        return new string(c, (int)Math.Min(repeat, 1000));
    }

    private static string OrdinalSuffix(long value)
    {
        long mod100 = value % 100;
        if (mod100 is >= 11 and <= 13)
        {
            return "th";
        }

        return (value % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }

    private static string FullWidth(string digits)
    {
        var sb = new StringBuilder(digits.Length);
        foreach (char c in digits)
        {
            sb.Append(c is >= '0' and <= '9' ? (char)(0xFF10 + (c - '0')) : c);
        }

        return sb.ToString();
    }

    private static string Chicago(long value)
    {
        char[] marks = ['*', '\u2020', '\u2021', '\u00A7'];
        long index = (value - 1) % 4;
        long repeat = ((value - 1) / 4) + 1;
        return new string(marks[index], (int)Math.Min(repeat, 1000));
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string CardinalWords(long n)
    {
        if (n < 20)
        {
            return Ones[n];
        }

        if (n < 100)
        {
            return Tens[n / 10] + (n % 10 != 0 ? "-" + Ones[n % 10] : string.Empty);
        }

        if (n < 1000)
        {
            return Ones[n / 100] + " hundred" + (n % 100 != 0 ? " " + CardinalWords(n % 100) : string.Empty);
        }

        return CardinalWords(n / 1000) + " thousand" + (n % 1000 != 0 ? " " + CardinalWords(n % 1000) : string.Empty);
    }

    private static string OrdinalWords(long n)
    {
        if (n < 20)
        {
            return OrdinalOnes[n];
        }

        if (n < 100)
        {
            if (n % 10 == 0)
            {
                string t = Tens[n / 10];
                return t[..^1] + "ieth";
            }

            return Tens[n / 10] + "-" + OrdinalOnes[n % 10];
        }

        // Round hundreds and thousands take the ordinal suffix on the last word; otherwise only the last number word changes.
        if (n < 1000)
        {
            return n % 100 == 0 ? Ones[n / 100] + " hundredth" : Ones[n / 100] + " hundred " + OrdinalWords(n % 100);
        }

        return n % 1000 == 0 ? CardinalWords(n / 1000) + " thousandth" : CardinalWords(n / 1000) + " thousand " + OrdinalWords(n % 1000);
    }
}
