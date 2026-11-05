using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Documents.Styles;

/// <summary>
/// The parts of a DrawingML theme that formatting refers to: the major and minor fonts and the colour scheme. The theme part is
/// read only. A document without a theme reads as <see cref="Empty"/>, whose lookups return null.
/// </summary>
public sealed class ThemeData
{
    private readonly Dictionary<string, string> _colors;
    private readonly Dictionary<string, string> _fonts;

    private ThemeData(Dictionary<string, string> colors, Dictionary<string, string> fonts)
    {
        _colors = colors;
        _fonts = fonts;
    }

    /// <summary>A theme with no fonts and no colours.</summary>
    public static ThemeData Empty { get; } = new([], []);

    /// <summary>Reads the font and colour schemes of a theme part.</summary>
    /// <param name="root">The <c>a:theme</c> element.</param>
    /// <returns>The theme data; elements that are missing are simply absent.</returns>
    public static ThemeData Read(MarkupRef root)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        var fonts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (MarkupRef elements in Wml.Elements(root))
        {
            if (elements.LocalNameText != "themeElements")
            {
                continue;
            }

            foreach (MarkupRef scheme in Wml.Elements(elements))
            {
                if (scheme.LocalNameText == "clrScheme")
                {
                    // ECMA-376 Part 1 §20.1.4.1.1 (clrScheme): each slot holds an sRGB colour or a system colour with a last-known value.
                    foreach (MarkupRef slot in Wml.Elements(scheme))
                    {
                        foreach (MarkupRef c in Wml.Elements(slot))
                        {
                            string? hex = c.LocalNameText == "srgbClr" ? c.Node.GetAttribute("val") : c.Node.GetAttribute("lastClr");
                            if (hex is not null)
                            {
                                colors[slot.LocalNameText] = hex.ToUpperInvariant();
                            }

                            break;
                        }
                    }
                }
                else if (scheme.LocalNameText == "fontScheme")
                {
                    // §20.1.4.1.18 (fontScheme): majorFont and minorFont each name a latin, east Asian and complex-script typeface.
                    foreach (MarkupRef group in Wml.Elements(scheme))
                    {
                        string prefix = group.LocalNameText == "majorFont" ? "major" : group.LocalNameText == "minorFont" ? "minor" : "";
                        if (prefix.Length == 0)
                        {
                            continue;
                        }

                        foreach (MarkupRef face in Wml.Elements(group))
                        {
                            string name = face.LocalNameText;
                            if ((name is "latin" or "ea" or "cs") && face.Node.GetAttribute("typeface") is { } typeface)
                            {
                                fonts[prefix + "/" + name] = typeface;
                            }
                        }
                    }
                }
            }
        }

        return new ThemeData(colors, fonts);
    }

    /// <summary>
    /// Resolves a theme font reference as written in <c>w:rFonts</c> (<c>majorAscii</c>, <c>minorHAnsi</c>, <c>majorEastAsia</c>,
    /// <c>minorBidi</c>, …) to a typeface.
    /// </summary>
    /// <param name="themeFont">The attribute value.</param>
    /// <returns>The typeface, or null when the reference is unknown or the theme lacks it. An empty typeface (a theme that leaves the slot to the script) reads as null.</returns>
    public string? ResolveFont(string? themeFont)
    {
        if (themeFont is null)
        {
            return null;
        }

        string group = themeFont.StartsWith("major", StringComparison.Ordinal) ? "major" : themeFont.StartsWith("minor", StringComparison.Ordinal) ? "minor" : "";
        if (group.Length == 0)
        {
            return null;
        }

        string slot = themeFont.EndsWith("EastAsia", StringComparison.Ordinal) ? "ea" : themeFont.EndsWith("Bidi", StringComparison.Ordinal) ? "cs" : "latin";
        return _fonts.TryGetValue(group + "/" + slot, out string? face) && face.Length > 0 ? face : null;
    }

    /// <summary>
    /// Resolves a <c>w:themeColor</c> value to the scheme colour, as <c>RRGGBB</c>, without tint or shade.
    /// </summary>
    /// <param name="themeColor">The attribute value, such as <c>accent1</c>, <c>text1</c> or <c>background2</c>.</param>
    /// <returns>The colour, or null when unknown.</returns>
    public string? ResolveColor(string? themeColor)
    {
        // ECMA-376 Part 1 §17.18.97 (ST_ThemeColor) with the default colour map (§19.3.1.6): text1=dk1, background1=lt1, text2=dk2, background2=lt2.
        string? slot = themeColor switch
        {
            "dark1" or "text1" => "dk1",
            "light1" or "background1" => "lt1",
            "dark2" or "text2" => "dk2",
            "light2" or "background2" => "lt2",
            "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6" => themeColor,
            "hyperlink" => "hlink",
            "followedHyperlink" => "folHlink",
            _ => null,
        };
        return slot is not null && _colors.TryGetValue(slot, out string? hex) ? hex : null;
    }
}
