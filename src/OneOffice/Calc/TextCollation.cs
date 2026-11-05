using System;
using System.Text;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>
/// The order Excel gives text in comparisons: case-insensitive, accents are a tie-break after letters, hyphens and apostrophes are skipped unless
/// everything else ties ("a-b" sorts after "ab"), punctuation sorts before digits and digits before letters. This reproduces the English word-sort
/// order Windows applies, from the answers Excel gives, for ASCII and the Latin letters with accents; other scripts compare by code point.
/// </summary>
internal static class TextCollation
{
    // Observed order of the ASCII symbols, lowest first (Excel 16.0.20527, English).
    private const string Symbols = " _-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$";

    private const string Accented = "àáâãäåçèéêëìíîïñòóôõöøùúûüýÿ";
    private const string Plain = "aaaaaaceeeeiiiinoooooouuuuyy";

    public static int Compare(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        (string pa, string da, int ia) = Keys(a);
        (string pb, string db, int ib) = Keys(b);
        int c = string.CompareOrdinal(pa, pb);
        if (c != 0)
        {
            return c < 0 ? -1 : 1;
        }

        c = string.CompareOrdinal(da, db);
        if (c != 0)
        {
            return c < 0 ? -1 : 1;
        }

        return ia.CompareTo(ib);
    }

    // Primary key: one character per letter or symbol, ordered. Secondary: the accent marks. Third: how many skipped characters there were.
    private static (string Primary, string Accents, int Skipped) Keys(string s)
    {
        var p = new StringBuilder(s.Length);
        var d = new StringBuilder();
        int skipped = 0;
        foreach (char raw in s)
        {
            char c = char.ToLowerInvariant(raw);
            if (c is '-' or '\'')
            {
                skipped++;
                continue;
            }

            if (c == 'ß')
            {
                p.Append(Letter('s')).Append(Letter('s'));
                d.Append('\u0000').Append('\u0000'); // Excel treats the sharp s as ss.
                continue;
            }

            int acc = Accented.IndexOf(c, StringComparison.Ordinal);
            if (acc >= 0)
            {
                p.Append(Letter(Plain[acc]));
                d.Append((char)(acc + 2));
                continue;
            }

            d.Append('\u0000');
            int sym = Symbols.IndexOf(c, StringComparison.Ordinal);
            if (sym >= 0)
            {
                p.Append((char)(0x100 + sym));
            }
            else if (c is >= '0' and <= '9')
            {
                p.Append((char)(0x200 + (c - '0')));
            }
            else if (c is >= 'a' and <= 'z')
            {
                p.Append(Letter(c));
            }
            else
            {
                p.Append((char)(0x400 + c));
            }
        }

        return (p.ToString(), d.ToString(), skipped);

        static char Letter(char l) => (char)(0x300 + (l - 'a'));
    }
}
