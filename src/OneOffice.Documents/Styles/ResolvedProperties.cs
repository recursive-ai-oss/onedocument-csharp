using System.Collections.ObjectModel;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents.Styles;

/// <summary>Where a resolved property value was set (ECMA-376 Part 1 §17.7.2, the order styles are applied in).</summary>
public enum PropertySource
{
    /// <summary>The document defaults (<c>w:docDefaults</c>).</summary>
    DocumentDefaults = 0,

    /// <summary>A table style, whole table or a conditional region.</summary>
    TableStyle = 1,

    /// <summary>The paragraph properties of a numbering level.</summary>
    NumberingLevel = 2,

    /// <summary>A paragraph style, or one of the styles it is based on.</summary>
    ParagraphStyle = 3,

    /// <summary>A character style, or one of the styles it is based on.</summary>
    CharacterStyle = 4,

    /// <summary>Direct formatting on the paragraph or run itself.</summary>
    DirectFormatting = 5,
}

/// <summary>Where one value came from.</summary>
/// <param name="Source">The kind of source.</param>
/// <param name="StyleId">The style that set it, for style sources; null for defaults and direct formatting.</param>
/// <param name="Element">The property element (or the container for flattened properties) that set it, for going back to the markup.</param>
public readonly record struct PropertyOrigin(PropertySource Source, string? StyleId, MarkupRef Element);

/// <summary>One attribute of a resolved property with its own origin (spacing's <c>before</c> and <c>after</c> may come from different places).</summary>
/// <param name="Value">The attribute text as written.</param>
/// <param name="Origin">Where it was set.</param>
public readonly record struct ResolvedAttributeValue(string Value, PropertyOrigin Origin);

/// <summary>One resolved formatting property: its effective attributes and where each came from.</summary>
public sealed class ResolvedProperty
{
    internal ResolvedProperty(string name, MarkupRef element, PropertyOrigin origin, Dictionary<string, ResolvedAttributeValue> attributes)
    {
        Name = name;
        Element = element;
        Origin = origin;
        Attributes = attributes;
    }

    /// <summary>Gets the property's element name (<c>b</c>, <c>spacing</c>); for borders and tab stops the container and entry (<c>pBdr/top</c>, <c>tab/720</c>), for numbering <c>numPr/numId</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the element that last set the property.</summary>
    public MarkupRef Element { get; }

    /// <summary>Gets where the property was last set. For an attribute-merged property (spacing, indents, fonts, language) the attributes carry their own origins.</summary>
    public PropertyOrigin Origin { get; internal set; }

    /// <summary>Gets the effective attributes by local name (<c>val</c>, <c>before</c>, <c>ascii</c>), each with its origin.</summary>
    public IReadOnlyDictionary<string, ResolvedAttributeValue> Attributes { get; }

    /// <summary>Gets the <c>w:val</c> attribute text, or null.</summary>
    public string? Value => Attributes.TryGetValue("val", out ResolvedAttributeValue a) ? a.Value : null;

    /// <summary>Gets the property read as an on/off value: an element without a value is on (§17.3.2.1), and so is one whose value is not a recognised on/off value.</summary>
    public bool IsOn => Wml.OnOff(Value) ?? true;

    internal Dictionary<string, ResolvedAttributeValue> MutableAttributes => (Dictionary<string, ResolvedAttributeValue>)Attributes;

    /// <summary>Gets an attribute's value, or null.</summary>
    /// <param name="name">The local attribute name.</param>
    /// <returns>The value, or null.</returns>
    public string? Attribute(string name) => Attributes.TryGetValue(name, out ResolvedAttributeValue a) ? a.Value : null;

    /// <summary>Gets an attribute read as a whole number, or null when absent or not a number.</summary>
    /// <param name="name">The local attribute name.</param>
    /// <returns>The value, or null.</returns>
    public long? Number(string name) => Wml.Long(Attribute(name));
}

/// <summary>
/// The effective formatting of a paragraph or run: every property that any layer set, with the value that wins and where it came
/// from. Properties no layer sets are absent; the caller applies the format's own defaults (not bold, left aligned, and so on).
/// </summary>
public class ResolvedProperties
{
    internal ResolvedProperties(SortedDictionary<string, ResolvedProperty> properties, ThemeData theme)
    {
        Properties = new ReadOnlyDictionary<string, ResolvedProperty>(properties);
        Theme = theme;
    }

    /// <summary>Gets all resolved properties by name, in ordinal order.</summary>
    public IReadOnlyDictionary<string, ResolvedProperty> Properties { get; }

    /// <summary>Gets the theme used for the typed accessors.</summary>
    protected ThemeData Theme { get; }

    /// <summary>Gets a property by element name.</summary>
    /// <param name="name">The name, such as <c>jc</c>, <c>b</c> or <c>pBdr/top</c>.</param>
    /// <returns>The property, or null when no layer sets it.</returns>
    public ResolvedProperty? Get(string name) => Properties.GetValueOrDefault(name);

    /// <summary>Gets an on/off property.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>True or false as resolved (toggle properties already combined per §17.7.3), or null when no layer sets it.</returns>
    public bool? Flag(string name) => Get(name) is { } p ? p.IsOn : null;

    /// <summary>Gets the <c>w:val</c> of a property.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The text, or null.</returns>
    public string? Value(string name) => Get(name)?.Value;

    /// <summary>Gets the <c>w:val</c> of a property as a whole number.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The number, or null.</returns>
    public long? Number(string name) => Wml.Long(Value(name));
}

/// <summary>Effective paragraph formatting. The typed accessors cover the common properties; <see cref="ResolvedProperties.Get"/> reaches the rest.</summary>
public sealed class ResolvedParagraphProperties : ResolvedProperties
{
    internal ResolvedParagraphProperties(SortedDictionary<string, ResolvedProperty> properties, ThemeData theme, string? styleId, bool wordSpacingDefaults = false)
        : base(properties, theme)
    {
        StyleId = styleId;
        _wordSpacingDefaults = wordSpacingDefaults;
    }

    // The document has no w:pPrDefault, so Word's own defaults stand in for it.
    private readonly bool _wordSpacingDefaults;

    /// <summary>Gets the id of the paragraph style that applies: the one named directly, else the default paragraph style; null when the document has none.</summary>
    public string? StyleId { get; }

    /// <summary>Gets the alignment (<c>w:jc</c>): <c>left</c>, <c>center</c>, <c>right</c>, <c>both</c>, … with <c>start</c> and <c>end</c> as written. Null when unset.</summary>
    public string? Justification => Value("jc");

    /// <summary>Gets the space before the paragraph in twentieths of a point, or null.</summary>
    public long? SpaceBefore => Get("spacing")?.Number("before");

    /// <summary>
    /// Gets the space after the paragraph in twentieths of a point, or null. A document without <c>w:pPrDefault</c> reads as 160 (8 points)
    /// when nothing else sets it: that is what Word 365 (16.0.20527) applies, though the schema's default is 0.
    /// </summary>
    public long? SpaceAfter => Get("spacing")?.Number("after") ?? (_wordSpacingDefaults ? 160 : null);

    /// <summary>Gets the line spacing (<c>w:line</c>) in the unit the rule gives: 240ths of a line for <c>auto</c>, twentieths of a point otherwise.</summary>
    public long? LineSpacing => Get("spacing")?.Number("line");

    /// <summary>Gets the line spacing rule (<c>w:lineRule</c>: <c>auto</c>, <c>exact</c>, <c>atLeast</c>), or null.</summary>
    public string? LineRule => Get("spacing")?.Attribute("lineRule");

    /// <summary>Gets the left (start) indent in twentieths of a point, or null.</summary>
    public long? LeftIndent => Get("ind") is { } i ? i.Number("left") ?? i.Number("start") : null;

    /// <summary>Gets the right (end) indent in twentieths of a point, or null.</summary>
    public long? RightIndent => Get("ind") is { } i ? i.Number("right") ?? i.Number("end") : null;

    /// <summary>Gets the first-line indent in twentieths of a point, or null.</summary>
    public long? FirstLineIndent => Get("ind")?.Number("firstLine");

    /// <summary>Gets the hanging indent in twentieths of a point, or null.</summary>
    public long? HangingIndent => Get("ind")?.Number("hanging");

    /// <summary>Gets whether the paragraph keeps with the next one, or null when unset.</summary>
    public bool? KeepNext => Flag("keepNext");

    /// <summary>Gets whether the paragraph's lines stay together, or null when unset.</summary>
    public bool? KeepLines => Flag("keepLines");

    /// <summary>Gets whether a page break goes before the paragraph, or null when unset.</summary>
    public bool? PageBreakBefore => Flag("pageBreakBefore");

    /// <summary>Gets the outline level (0 to 8; 9 is body text), or null.</summary>
    public long? OutlineLevel => Number("outlineLvl");
}

/// <summary>Typefaces of a run after theme references are resolved.</summary>
/// <param name="Ascii">The typeface for ASCII characters.</param>
/// <param name="HighAnsi">The typeface for other Latin characters.</param>
/// <param name="EastAsia">The typeface for East Asian characters.</param>
/// <param name="ComplexScript">The typeface for complex-script characters.</param>
public readonly record struct ResolvedFonts(string? Ascii, string? HighAnsi, string? EastAsia, string? ComplexScript);

/// <summary>Effective run formatting. The typed accessors cover the common properties; <see cref="ResolvedProperties.Get"/> reaches the rest.</summary>
public sealed class ResolvedRunProperties : ResolvedProperties
{
    internal ResolvedRunProperties(SortedDictionary<string, ResolvedProperty> properties, ThemeData theme, string? characterStyleId, string? paragraphStyleId)
        : base(properties, theme)
    {
        CharacterStyleId = characterStyleId;
        ParagraphStyleId = paragraphStyleId;
    }

    /// <summary>Gets the id of the character style that applies, or null.</summary>
    public string? CharacterStyleId { get; }

    /// <summary>Gets the id of the style of the enclosing paragraph, whose run properties also apply, or null.</summary>
    public string? ParagraphStyleId { get; }

    /// <summary>Gets whether the text is bold (toggle properties combined, §17.7.3). False when no layer sets it.</summary>
    public bool Bold => Flag("b") ?? false;

    /// <summary>Gets whether the text is italic. False when unset.</summary>
    public bool Italic => Flag("i") ?? false;

    /// <summary>Gets whether complex-script text is bold. False when unset.</summary>
    public bool BoldComplexScript => Flag("bCs") ?? false;

    /// <summary>Gets whether complex-script text is italic. False when unset.</summary>
    public bool ItalicComplexScript => Flag("iCs") ?? false;

    /// <summary>Gets whether the text is struck through. False when unset.</summary>
    public bool Strike => Flag("strike") ?? false;

    /// <summary>Gets whether the text is double struck through. False when unset.</summary>
    public bool DoubleStrike => Flag("dstrike") ?? false;

    /// <summary>Gets whether the text is in capitals. False when unset.</summary>
    public bool Caps => Flag("caps") ?? false;

    /// <summary>Gets whether the text is in small capitals. False when unset.</summary>
    public bool SmallCaps => Flag("smallCaps") ?? false;

    /// <summary>Gets whether the text is hidden (<c>w:vanish</c>). False when unset.</summary>
    public bool Hidden => Flag("vanish") ?? false;

    /// <summary>Gets the underline style (<c>w:u</c>), such as <c>single</c>; <c>none</c> or null when not underlined.</summary>
    public string? Underline => Value("u");

    /// <summary>Gets the size in half-points, or null.</summary>
    public long? SizeHalfPoints => Number("sz");

    /// <summary>Gets the complex-script size in half-points, or null.</summary>
    public long? SizeComplexScriptHalfPoints => Number("szCs");

    /// <summary>Gets the highlight colour name (<c>w:highlight</c>), or null.</summary>
    public string? Highlight => Value("highlight");

    /// <summary>Gets the vertical alignment (<c>w:vertAlign</c>: <c>superscript</c>, <c>subscript</c>, <c>baseline</c>), or null.</summary>
    public string? VerticalAlignment => Value("vertAlign");

    /// <summary>
    /// Gets the text colour as <c>RRGGBB</c>, or null when automatic or unset. A theme colour reads from the theme without its tint
    /// or shade; those stay in <c>Get("color")</c> for callers that apply them.
    /// </summary>
    public string? Color
    {
        get
        {
            ResolvedProperty? c = Get("color");
            if (c is null)
            {
                return null;
            }

            if (c.Attribute("themeColor") is { } theme && Theme.ResolveColor(theme) is { } fromTheme)
            {
                return fromTheme;
            }

            string? v = c.Attribute("val");
            return v is null || v == "auto" ? null : v.ToUpperInvariant();
        }
    }

    /// <summary>Gets the typefaces after resolving theme references (<c>w:asciiTheme</c> and the like, which win over the named face).</summary>
    public ResolvedFonts Fonts
    {
        get
        {
            ResolvedProperty? f = Get("rFonts");
            if (f is null)
            {
                return default;
            }

            return new ResolvedFonts(
                Theme.ResolveFont(f.Attribute("asciiTheme")) ?? f.Attribute("ascii"),
                Theme.ResolveFont(f.Attribute("hAnsiTheme")) ?? f.Attribute("hAnsi"),
                Theme.ResolveFont(f.Attribute("eastAsiaTheme")) ?? f.Attribute("eastAsia"),
                Theme.ResolveFont(f.Attribute("cstheme")) ?? f.Attribute("cs"));
        }
    }
}
