using System.Linq;
using System.Globalization;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Numbering;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents.Styles;

/// <summary>The list a paragraph is on, as its direct formatting and its style say (ECMA-376 Part 1 §17.9.18, <c>w:numPr</c>).</summary>
/// <param name="NumberingId">The <c>w:numId</c>; never 0 (a paragraph that opts out has no reference).</param>
/// <param name="Level">The zero-based level.</param>
/// <param name="NumberingIdIsDirect">True when the paragraph itself names the list; false when its style does.</param>
/// <param name="StyleId">The style that supplied the list, when it came from one.</param>
public readonly record struct NumberingReference(long NumberingId, int Level, bool NumberingIdIsDirect, string? StyleId);

public sealed partial class StyleSheet
{
    private static readonly string[] ParagraphSkip = ["pStyle", "sectPr", "rPr", "pPrChange", "cnfStyle"];
    private static readonly string[] RunSkip = ["rStyle", "rPrChange"];

    // ECMA-376 Part 1 §17.7.3: these properties toggle; their values are combined by XOR across the table, paragraph and character style levels.
    private static readonly string[] Toggles = ["b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss", "imprint", "vanish"];

    /// <summary>
    /// Works out the list a paragraph is on. A direct <c>w:numId</c> wins; else the nearest style in the paragraph style's chain that has one.
    /// <c>w:numId</c> 0 removes numbering. The level is the direct <c>w:ilvl</c>, else the style's, else the level linked to the paragraph's
    /// style, else 0.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <returns>The reference, or null when the paragraph is not numbered.</returns>
    public NumberingReference? GetNumbering(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        MarkupRef? direct = paragraph.Properties is { } p ? Wml.Child(p.Markup, "numPr"u8) : null;
        long? directId = Wml.Long(Wml.Attr(Wml.Child(direct ?? default, "numId"u8) ?? default, "val"));
        long? directLevel = Wml.Long(Wml.Attr(Wml.Child(direct ?? default, "ilvl"u8) ?? default, "val"));

        StyleDefinition? style = ParagraphStyleOf(paragraph);
        long? numId = directId;
        string? idFromStyle = null;
        long? level = directLevel;
        if (style is not null)
        {
            foreach (StyleDefinition s in style.Lineage.Reverse())
            {
                MarkupRef? np = s.ParagraphPropertiesElement is { } pp ? Wml.Child(pp, "numPr"u8) : null;
                if (np is null)
                {
                    continue;
                }

                if (numId is null && Wml.Long(Wml.Attr(Wml.Child(np.Value, "numId"u8) ?? default, "val")) is { } sid)
                {
                    numId = sid;
                    idFromStyle = s.Id;
                }

                level ??= Wml.Long(Wml.Attr(Wml.Child(np.Value, "ilvl"u8) ?? default, "val"));
            }
        }

        if (numId is null or <= 0)
        {
            return null;
        }

        int lvl;
        if (level is { } l)
        {
            lvl = (int)Math.Clamp(l, 0, 8);
        }
        else
        {
            // §17.9.23 (pStyle in a level): a level can name the paragraph style that numbers with it.
            lvl = style is not null && _owner.Numbering.FindLevelLinkedTo(numId.Value, style.Id) is { } linked ? linked : 0;
        }

        return new NumberingReference(numId.Value, lvl, directId is not null, idFromStyle);
    }

    private StyleDefinition? ParagraphStyleOf(Paragraph paragraph)
    {
        string? id = paragraph.StyleId;
        if (id is not null)
        {
            StyleDefinition? s = Find(id);
            if (s is not null && s.Type == StyleType.Paragraph)
            {
                return s;
            }

            Report("docx.style-unknown", DefectKind.Reference, DefectSeverity.Repaired, "p:" + id,
                "The paragraph style '" + id + "' is not defined; the default paragraph style applies.", paragraph.Markup);
        }

        return Default(StyleType.Paragraph);
    }

    private StyleDefinition? CharacterStyleOf(Run run)
    {
        string? id = run.StyleId;
        if (id is not null)
        {
            StyleDefinition? s = Find(id);
            if (s is not null && s.Type == StyleType.Character)
            {
                return s;
            }

            Report("docx.style-unknown", DefectKind.Reference, DefectSeverity.Repaired, "r:" + id,
                "The character style '" + id + "' is not defined; no character style applies.", run.Markup);
            return Default(StyleType.Character);
        }

        return Default(StyleType.Character);
    }

    /// <summary>
    /// Resolves the effective paragraph properties: document defaults, table style, numbering level, paragraph style chain and direct
    /// formatting, in that order (ECMA-376 Part 1 §17.7.2), each property remembering where it was set. A numbering level's properties
    /// come before the paragraph style when the style supplies the list and after it when the paragraph names the list itself.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <returns>The effective properties.</returns>
    public ResolvedParagraphProperties ResolveParagraph(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        StyleDefinition? style = ParagraphStyleOf(paragraph);
        var layers = new List<Layer>();

        var defaults = new Layer();
        MergeContainer(defaults.Bag, DefaultParagraphProperties, PropertySource.DocumentDefaults, null, paragraph: true);
        layers.Add(defaults);

        var table = new Layer();
        ApplyTableStyle(table.Bag, paragraph.Markup, paragraphProps: true);
        layers.Add(table);

        NumberingReference? num = GetNumbering(paragraph);
        var numbering = new Layer();
        if (num is { } n && _owner.Numbering.GetLevel(n.NumberingId, n.Level) is { } level)
        {
            MergeContainer(numbering.Bag, level.ParagraphProperties, PropertySource.NumberingLevel, null, paragraph: true);
        }

        var para = new Layer();
        if (style is not null)
        {
            foreach (StyleDefinition s in style.Lineage)
            {
                MergeContainer(para.Bag, s.ParagraphPropertiesElement, PropertySource.ParagraphStyle, s.Id, paragraph: true);
            }
        }

        if (num is { NumberingIdIsDirect: true })
        {
            layers.Add(para);
            layers.Add(numbering);
        }
        else
        {
            layers.Add(numbering);
            layers.Add(para);
        }

        var direct = new Layer();
        MergeContainer(direct.Bag, paragraph.Properties?.Markup, PropertySource.DirectFormatting, null, paragraph: true);
        layers.Add(direct);

        // The numbering reference itself is a resolved property too, with the origin of whoever supplied it.
        SortedDictionary<string, ResolvedProperty> result = Combine(layers, toggleLayers: null);
        return new ResolvedParagraphProperties(result, Theme, style?.Id);
    }

    /// <summary>
    /// Resolves the effective run properties: document defaults, table style, the paragraph style's run properties, the character style
    /// chain and direct formatting (§17.7.2). Toggle properties (bold, italic, caps, …) combine across the style levels by XOR (§17.7.3).
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The effective properties.</returns>
    public ResolvedRunProperties ResolveRun(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Paragraph? paragraph = Wml.AncestorOrSelf(new MarkupRef(run.Markup.Node.Parent), "p"u8) is { } pr ? new Paragraph(_owner, pr) : null;

        StyleDefinition? paraStyle = paragraph is null ? null : ParagraphStyleOf(paragraph);
        StyleDefinition? charStyle = CharacterStyleOf(run);

        var defaults = new Layer();
        MergeContainer(defaults.Bag, DefaultRunProperties, PropertySource.DocumentDefaults, null, paragraph: false);

        var table = new Layer();
        if (paragraph is not null)
        {
            ApplyTableStyle(table.Bag, paragraph.Markup, paragraphProps: false);
        }

        var para = new Layer();
        if (paraStyle is not null)
        {
            foreach (StyleDefinition s in paraStyle.Lineage)
            {
                MergeContainer(para.Bag, s.RunPropertiesElement, PropertySource.ParagraphStyle, s.Id, paragraph: false);
            }
        }

        var character = new Layer();
        if (charStyle is not null)
        {
            foreach (StyleDefinition s in charStyle.Lineage)
            {
                MergeContainer(character.Bag, s.RunPropertiesElement, PropertySource.CharacterStyle, s.Id, paragraph: false);
            }
        }

        var direct = new Layer();
        MergeContainer(direct.Bag, run.Properties?.Markup, PropertySource.DirectFormatting, null, paragraph: false);

        var layers = new List<Layer> { defaults, table, para, character, direct };
        SortedDictionary<string, ResolvedProperty> result = Combine(layers, toggleLayers: [table, para, character]);
        return new ResolvedRunProperties(result, Theme, charStyle?.Id, paraStyle?.Id);
    }

    /// <summary>
    /// Resolves the properties a style itself has once its chain and the document defaults are applied: what Word's style dialog
    /// shows. For a table style the whole-table formatting only.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <param name="runProperties">True for run properties, false for paragraph properties.</param>
    /// <returns>The effective properties.</returns>
    public ResolvedProperties ResolveStyle(StyleDefinition style, bool runProperties)
    {
        ArgumentNullException.ThrowIfNull(style);
        var defaults = new Layer();
        MergeContainer(defaults.Bag, runProperties ? DefaultRunProperties : DefaultParagraphProperties, PropertySource.DocumentDefaults, null, !runProperties);
        var chain = new Layer();
        PropertySource source = style.Type == StyleType.Character ? PropertySource.CharacterStyle : style.Type == StyleType.Table ? PropertySource.TableStyle : PropertySource.ParagraphStyle;
        foreach (StyleDefinition s in style.Lineage)
        {
            MergeContainer(chain.Bag, runProperties ? s.RunPropertiesElement : s.ParagraphPropertiesElement, source, s.Id, !runProperties);
        }

        SortedDictionary<string, ResolvedProperty> result = Combine([defaults, chain], toggleLayers: null);
        return runProperties ? new ResolvedRunProperties(result, Theme, null, null) : new ResolvedParagraphProperties(result, Theme, style.Id);
    }

    // --- layers ---

    private sealed class Layer
    {
        public SortedDictionary<string, ResolvedProperty> Bag { get; } = new(StringComparer.Ordinal);
    }

    private static SortedDictionary<string, ResolvedProperty> Combine(List<Layer> layers, List<Layer>? toggleLayers)
    {
        var result = new SortedDictionary<string, ResolvedProperty>(StringComparer.Ordinal);
        foreach (Layer layer in layers)
        {
            foreach (ResolvedProperty p in layer.Bag.Values)
            {
                MergeEntry(result, p);
            }
        }

        if (toggleLayers is not null)
        {
            // §17.7.3: direct formatting sets a toggle outright. Otherwise the style levels that set it are XORed; with none, the defaults stand.
            Layer direct = layers[^1];
            foreach (string name in Toggles)
            {
                if (direct.Bag.ContainsKey(name))
                {
                    continue;
                }

                bool any = false;
                bool value = false;
                ResolvedProperty? last = null;
                foreach (Layer l in toggleLayers)
                {
                    if (l.Bag.TryGetValue(name, out ResolvedProperty? p))
                    {
                        any = true;
                        value ^= p.IsOn;
                        last = p;
                    }
                }

                if (any && last is not null)
                {
                    var attrs = new Dictionary<string, ResolvedAttributeValue>(StringComparer.Ordinal)
                    {
                        ["val"] = new ResolvedAttributeValue(value ? "true" : "false", last.Origin),
                    };
                    result[name] = new ResolvedProperty(name, last.Element, last.Origin, attrs);
                }
            }
        }

        // A tab stop cleared at a higher level is gone, not a stop with value "clear".
        foreach (string key in result.Keys.Where(k => k.StartsWith("tab/", StringComparison.Ordinal) && result[k].Value == "clear").ToList())
        {
            result.Remove(key);
        }

        return result;
    }

    // Properties whose attributes merge one by one (Word applies spacing before from one style and spacing after from another).
    private static bool MergesAttributes(string name) => name is "spacing" or "ind" or "rFonts" or "lang";

    private static void MergeEntry(SortedDictionary<string, ResolvedProperty> into, ResolvedProperty incoming)
    {
        if (!MergesAttributes(incoming.Name) || !into.TryGetValue(incoming.Name, out ResolvedProperty? existing))
        {
            into[incoming.Name] = Clone(incoming);
            return;
        }

        var attrs = new Dictionary<string, ResolvedAttributeValue>(existing.Attributes, StringComparer.Ordinal);
        foreach (KeyValuePair<string, ResolvedAttributeValue> kv in incoming.Attributes)
        {
            attrs[kv.Key] = kv.Value;
        }

        // Alternatives that cancel one another: a typeface named directly replaces an inherited theme reference, a first-line indent an
        // inherited hanging one, and left/start (right/end) are the same edge.
        foreach ((string set, string[] cancels) in Cancellations(incoming.Name))
        {
            if (incoming.Attributes.ContainsKey(set))
            {
                foreach (string c in cancels)
                {
                    if (!incoming.Attributes.ContainsKey(c))
                    {
                        attrs.Remove(c);
                    }
                }
            }
        }

        into[incoming.Name] = new ResolvedProperty(incoming.Name, incoming.Element, incoming.Origin, attrs);
    }

    private static IEnumerable<(string Set, string[] Cancels)> Cancellations(string property)
    {
        switch (property)
        {
            case "rFonts":
                yield return ("ascii", ["asciiTheme"]);
                yield return ("hAnsi", ["hAnsiTheme"]);
                yield return ("eastAsia", ["eastAsiaTheme"]);
                yield return ("cs", ["cstheme"]);
                break;
            case "ind":
                yield return ("firstLine", ["hanging", "hangingChars"]);
                yield return ("hanging", ["firstLine", "firstLineChars"]);
                yield return ("left", ["start"]);
                yield return ("start", ["left"]);
                yield return ("right", ["end"]);
                yield return ("end", ["right"]);
                break;
        }
    }

    private static ResolvedProperty Clone(ResolvedProperty p) =>
        new(p.Name, p.Element, p.Origin, new Dictionary<string, ResolvedAttributeValue>(p.Attributes, StringComparer.Ordinal));

    private static void MergeContainer(SortedDictionary<string, ResolvedProperty> bag, MarkupRef? container, PropertySource source, string? styleId, bool paragraph)
    {
        if (container is not { } c || !c.IsValid)
        {
            return;
        }

        string[] skip = paragraph ? ParagraphSkip : RunSkip;
        foreach (MarkupRef e in Wml.Elements(c))
        {
            bool inW = e.Namespace == SchemaNamespace.Wordprocessing;
            string name = inW ? e.LocalNameText : e.Node.Name;
            if (inW && Array.IndexOf(skip, name) >= 0)
            {
                continue;
            }

            var origin = new PropertyOrigin(source, styleId, e);
            if (inW && paragraph && name is "pBdr" or "tabs" or "numPr")
            {
                // Borders merge side by side, tab stops by position and the list reference per child.
                foreach (MarkupRef child in Wml.Elements(e))
                {
                    string key = name == "tabs" ? "tab/" + (Wml.Attr(child, "pos") ?? string.Empty)
                        : name + "/" + child.LocalNameText;
                    if (name == "numPr" && child.LocalNameText is not ("ilvl" or "numId"))
                    {
                        continue;
                    }

                    bag[key] = new ResolvedProperty(key, child, new PropertyOrigin(source, styleId, child), ReadAttributes(child, new PropertyOrigin(source, styleId, child)));
                }

                continue;
            }

            if (inW && !paragraph && name == "rPr")
            {
                continue;
            }

            MergeEntry(bag, new ResolvedProperty(name, e, origin, ReadAttributes(e, origin)));
        }
    }

    private static Dictionary<string, ResolvedAttributeValue> ReadAttributes(MarkupRef e, PropertyOrigin origin)
    {
        var d = new Dictionary<string, ResolvedAttributeValue>(StringComparer.Ordinal);
        foreach (MarkupAttributeRef a in e.Node.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                continue;
            }

            bool plain = a.Prefix.Length == 0 || SchemaNamespaces.Resolve(a.NamespaceUriBytes) == SchemaNamespace.Wordprocessing;
            string key = plain ? System.Text.Encoding.UTF8.GetString(a.LocalName) : System.Text.Encoding.UTF8.GetString(a.RawName);
            d[key] = new ResolvedAttributeValue(a.Value, origin);
        }

        return d;
    }

    // --- table styles ---

    // ECMA-376 Part 1 §17.7.6 (conditional formatting): regions apply in this order; a later one overrides an earlier one.
    private static readonly string[] RegionOrder =
    [
        "wholeTable", "band1Vert", "band2Vert", "band1Horz", "band2Horz", "firstCol", "lastCol", "firstRow", "lastRow",
        "nwCell", "neCell", "swCell", "seCell",
    ];

    private void ApplyTableStyle(SortedDictionary<string, ResolvedProperty> bag, MarkupRef paragraph, bool paragraphProps)
    {
        MarkupRef? cell = null;
        for (MarkupNode n = paragraph.Node.Parent; !n.IsNull && n.Kind == MarkupNodeKind.Element; n = n.Parent)
        {
            var r = new MarkupRef(n);
            if (Wml.Is(r, "tc"u8))
            {
                cell = r;
                break;
            }

            if (Wml.Is(r, "tbl"u8))
            {
                break;
            }
        }

        if (cell is not { } tc)
        {
            return;
        }

        var row = new MarkupRef(tc.Node.Parent);
        var table = new MarkupRef(row.Node.Parent);
        if (!Wml.Is(row, "tr"u8) || !Wml.Is(table, "tbl"u8))
        {
            return;
        }

        MarkupRef? tblPr = Wml.Child(table, "tblPr"u8);
        string? styleId = Wml.Attr(Wml.Child(tblPr ?? default, "tblStyle"u8) ?? default, "val");
        StyleDefinition? style = styleId is null ? Default(StyleType.Table) : Find(styleId);
        if (style is null)
        {
            if (styleId is not null)
            {
                Report("docx.style-unknown", DefectKind.Reference, DefectSeverity.Repaired, "t:" + styleId,
                    "The table style '" + styleId + "' is not defined; the default table style applies.", table);
                style = Default(StyleType.Table);
            }

            if (style is null)
            {
                return;
            }
        }

        IReadOnlyList<StyleDefinition> lineage = style.Lineage;
        PropertySource source = PropertySource.TableStyle;
        foreach (StyleDefinition s in lineage)
        {
            MergeContainer(bag, paragraphProps ? s.ParagraphPropertiesElement : s.RunPropertiesElement, source, s.Id, paragraphProps);
        }

        // Which regions the cell is in.
        TableLook look = ReadLook(tblPr);
        int rowIndex = 0;
        int rowCount = 0;
        foreach (MarkupRef sib in Wml.Elements(table))
        {
            if (Wml.Is(sib, "tr"u8))
            {
                if (sib == row)
                {
                    rowIndex = rowCount;
                }

                rowCount++;
            }
        }

        int cellIndex = 0;
        int cellCount = 0;
        foreach (MarkupRef sib in Wml.Elements(row))
        {
            if (Wml.Is(sib, "tc"u8))
            {
                if (sib == tc)
                {
                    cellIndex = cellCount;
                }

                cellCount++;
            }
        }

        long rowBand = BandSize(lineage, tblPr, "tblStyleRowBandSize");
        long colBand = BandSize(lineage, tblPr, "tblStyleColBandSize");
        bool isFirstRow = look.FirstRow && rowIndex == 0;
        bool isLastRow = look.LastRow && rowIndex == rowCount - 1;
        bool isFirstCol = look.FirstColumn && cellIndex == 0;
        bool isLastCol = look.LastColumn && cellIndex == cellCount - 1;
        long hIndex = rowIndex - (look.FirstRow ? 1 : 0);
        long vIndex = cellIndex - (look.FirstColumn ? 1 : 0);
        bool hBanded = !look.NoHBand && !isFirstRow && !isLastRow && hIndex >= 0;
        bool vBanded = !look.NoVBand && !isFirstCol && !isLastCol && vIndex >= 0;

        foreach (string region in RegionOrder)
        {
            bool applies = region switch
            {
                "wholeTable" => true,
                "band1Horz" => hBanded && (hIndex / rowBand) % 2 == 0,
                "band2Horz" => hBanded && (hIndex / rowBand) % 2 == 1,
                "band1Vert" => vBanded && (vIndex / colBand) % 2 == 0,
                "band2Vert" => vBanded && (vIndex / colBand) % 2 == 1,
                "firstRow" => isFirstRow,
                "lastRow" => isLastRow,
                "firstCol" => isFirstCol,
                "lastCol" => isLastCol,
                "nwCell" => isFirstRow && isFirstCol,
                "neCell" => isFirstRow && isLastCol,
                "swCell" => isLastRow && isFirstCol,
                "seCell" => isLastRow && isLastCol,
                _ => false,
            };
            if (!applies)
            {
                continue;
            }

            foreach (StyleDefinition s in lineage)
            {
                foreach (TableStyleRegion r in s.TableRegions)
                {
                    if (r.Type == region)
                    {
                        MergeContainer(bag, paragraphProps ? r.ParagraphProperties : r.RunProperties, source, s.Id, paragraphProps);
                    }
                }
            }
        }
    }

    private readonly record struct TableLook(bool FirstRow, bool LastRow, bool FirstColumn, bool LastColumn, bool NoHBand, bool NoVBand);

    private static TableLook ReadLook(MarkupRef? tblPr)
    {
        MarkupRef? look = Wml.Child(tblPr ?? default, "tblLook"u8);
        if (look is not { } l)
        {
            return default;
        }

        // §17.4.56 (tblLook): either the six attributes, or a hexadecimal bit mask in w:val (0x20 first row, 0x40 last row, 0x80 first
        // column, 0x100 last column, 0x200 no row bands, 0x400 no column bands). Attributes win when both are written.
        long mask = long.TryParse(Wml.Attr(l, "val"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long m) ? m : 0;
        bool Flag(string attr, long bit) => Wml.OnOff(Wml.Attr(l, attr)) ?? ((mask & bit) != 0);
        return new TableLook(Flag("firstRow", 0x20), Flag("lastRow", 0x40), Flag("firstColumn", 0x80), Flag("lastColumn", 0x100), Flag("noHBand", 0x200), Flag("noVBand", 0x400));
    }

    private static long BandSize(IReadOnlyList<StyleDefinition> lineage, MarkupRef? tblPr, string element)
    {
        long? size = Wml.Long(Wml.Attr(Wml.Child(tblPr ?? default, System.Text.Encoding.UTF8.GetBytes(element)) ?? default, "val"));
        for (int i = lineage.Count - 1; size is null && i >= 0; i--)
        {
            size = Wml.Long(Wml.Attr(Wml.Child(lineage[i].TablePropertiesElement ?? default, System.Text.Encoding.UTF8.GetBytes(element)) ?? default, "val"));
        }

        return size is > 0 ? size.Value : 1;
    }
}
