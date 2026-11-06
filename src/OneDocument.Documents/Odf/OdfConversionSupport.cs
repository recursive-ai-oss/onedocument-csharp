using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Lengths and small string helpers shared by the OpenDocument importer and exporter.
internal static class OdfUnits
{
    // OpenDocument 1.3 Part 1, Data types: a length is a number followed by one of cm, mm, in, pt, pc, px (or em, ex for fonts);
    // the importer converts to twentieths of a point (twips) and to EMU.
    public static bool TryPoints(string? value, out double points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> s = value.AsSpan().Trim();
        int i = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            i++;
        }

        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
        {
            i++;
        }

        if (i == 0 || !double.TryParse(s[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || double.IsNaN(n) || double.IsInfinity(n))
        {
            return false;
        }

        ReadOnlySpan<char> unit = s[i..].Trim();
        double factor;
        if (unit.SequenceEqual("pt"))
        {
            factor = 1;
        }
        else if (unit.SequenceEqual("cm"))
        {
            factor = 72 / 2.54;
        }
        else if (unit.SequenceEqual("mm"))
        {
            factor = 72 / 25.4;
        }
        else if (unit.SequenceEqual("in"))
        {
            factor = 72;
        }
        else if (unit.SequenceEqual("pc"))
        {
            factor = 12;
        }
        else if (unit.SequenceEqual("px"))
        {
            factor = 0.75;
        }
        else if (unit.IsEmpty)
        {
            factor = 1;
        }
        else
        {
            return false;
        }

        points = n * factor;
        return true;
    }

    public static bool TryTwips(string? value, out int twips)
    {
        twips = 0;
        if (!TryPoints(value, out double pt))
        {
            return false;
        }

        twips = (int)Math.Clamp(Math.Round(pt * 20, MidpointRounding.AwayFromZero), int.MinValue / 2, int.MaxValue / 2);
        return true;
    }

    public static int Twips(string? value, int fallback = 0) => TryTwips(value, out int t) ? t : fallback;

    public static bool TryEmu(string? value, out long emu)
    {
        emu = 0;
        if (!TryPoints(value, out double pt))
        {
            return false;
        }

        emu = (long)Math.Clamp(Math.Round(pt * 12700), -(1L << 40), 1L << 40);
        return true;
    }

    public static string Cm(double points) => (points * 2.54 / 72).ToString("0.####", CultureInfo.InvariantCulture) + "cm";

    public static string TwipsToCm(long twips) => Cm(twips / 20.0);

    public static string EmuToCm(long emu) => Cm(emu / 12700.0);

    public static string Pt(double points) => points.ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    public static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    // Style names in an OpenDocument file encode characters that XML names or CSS identifiers dislike as _HH_ (hex): "Heading_20_1".
    public static string DecodeStyleName(string name)
    {
        if (!name.Contains('_', StringComparison.Ordinal))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '_' && i + 3 < name.Length + 0 && name[i + 3] == '_'
                && int.TryParse(name.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp))
            {
                sb.Append((char)cp);
                i += 3;
            }
            else
            {
                sb.Append(name[i]);
            }
        }

        return sb.ToString();
    }

    public static string EncodeStyleName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            // Style names are XML names without colons (NCName) in the ODF schema, which may not start with a digit, a hyphen or a full stop.
            bool first = sb.Length == 0;
            if (char.IsAsciiLetter(c) || (!first && (char.IsAsciiDigit(c) || c == '-' || c == '.')))
            {
                sb.Append(c);
            }
            else if (c == '_' || c < 0x100)
            {
                sb.Append('_').Append(((int)c).ToString("x2", CultureInfo.InvariantCulture)).Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public static string Esc(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}

// A set of property elements keyed by element name, kept as ready XML so a later assignment replaces an earlier one (a nearer style
// overrides a farther one) and the elements are written in the order the schema asks for.
internal sealed class PropBag
{
    private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

    public bool IsEmpty => _items.Count == 0;

    public void Set(string key, string xml) => _items[key] = xml;

    public bool Has(string key) => _items.ContainsKey(key);

    public bool Remove(string key) => _items.Remove(key);

    public string? Get(string key) => _items.GetValueOrDefault(key);

    public void Merge(PropBag other)
    {
        foreach (KeyValuePair<string, string> kv in other._items)
        {
            _items[kv.Key] = kv.Value;
        }
    }

    public PropBag Clone()
    {
        var b = new PropBag();
        b.Merge(this);
        return b;
    }

    public string ToXml(string[] order)
    {
        var sb = new StringBuilder();
        foreach (string k in order)
        {
            if (_items.TryGetValue(k, out string? v))
            {
                sb.Append(v);
            }
        }

        return sb.ToString();
    }
}

internal static class WordOrder
{
    public static readonly string[] PPr =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers", "pBdr", "shd", "tabs",
        "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap", "jc", "textDirection", "textAlignment",
        "textboxTightWrap", "outlineLvl", "divId", "cnfStyle", "rPr", "sectPr",
    ];

    public static readonly string[] RPr =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss", "imprint", "noProof",
        "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect", "bdr", "shd",
        "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout", "specVanish", "oMath",
    ];

    public static readonly string[] TcPr =
    [
        "cnfStyle", "tcW", "gridSpan", "hMerge", "vMerge", "tcBorders", "shd", "noWrap", "tcMar", "textDirection", "tcFitText", "vAlign", "hideMark",
    ];

    public static readonly string[] TblPr =
    [
        "tblStyle", "tblpPr", "tblOverlap", "bidiVisual", "tblStyleRowBandSize", "tblStyleColBandSize", "tblW", "jc", "tblCellSpacing", "tblInd",
        "tblBorders", "shd", "tblLayout", "tblCellMar", "tblLook",
    ];
}
