using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using W = RecursiveAI.OneDocument.Schema.Wordprocessing;

namespace RecursiveAI.OneDocument.Documents.Styles;

/// <summary>The kind of a style (ECMA-376 Part 1 §17.7.4.17, ST_StyleType).</summary>
public enum StyleType
{
    /// <summary>Applies to whole paragraphs (and, through its run properties, to their text).</summary>
    Paragraph = 0,

    /// <summary>Applies to runs.</summary>
    Character = 1,

    /// <summary>Applies to tables, with optional conditional formatting for regions.</summary>
    Table = 2,

    /// <summary>A numbering style: a named list definition (§17.7.4.17).</summary>
    Numbering = 3,
}

/// <summary>One style of the style sheet: a thin wrapper over its <c>w:style</c> element.</summary>
public sealed class StyleDefinition : IEquatable<StyleDefinition>
{
    internal StyleDefinition(StyleSheet sheet, MarkupRef markup)
    {
        Sheet = sheet;
        Markup = markup;
    }

    /// <summary>Gets the style sheet the style belongs to.</summary>
    public StyleSheet Sheet { get; }

    /// <summary>Gets the <c>w:style</c> element.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the typed view of the style.</summary>
    public W.Style View => W.Style.Create(Markup);

    /// <summary>Gets the style id (<c>w:styleId</c>), the key paragraphs and runs refer to; empty when the file omits it.</summary>
    public string Id => View.StyleId ?? string.Empty;

    /// <summary>Gets or sets the display name (<c>w:name</c>), or null. Names of built-in styles are compared ignoring case.</summary>
    public string? Name
    {
        get => View.Name?.Val;
        set
        {
            Sheet.Invalidate();
            if (value is null)
            {
                View.RemoveName();
            }
            else
            {
                View.GetOrAddName().Val = value;
            }
        }
    }

    /// <summary>Gets the type. A style without <c>w:type</c> is a paragraph style (§17.7.4.17).</summary>
    public StyleType Type => Wml.Attr(Markup, "type") switch
    {
        "character" => StyleType.Character,
        "table" => StyleType.Table,
        "numbering" => StyleType.Numbering,
        _ => StyleType.Paragraph,
    };

    /// <summary>Gets the id of the style this one is based on (<c>w:basedOn</c>), or null.</summary>
    public string? BasedOnId => View.BasedOn?.Val;

    /// <summary>Gets the id of the style applied to the paragraph after this one (<c>w:next</c>), or null.</summary>
    public string? NextId => View.Next?.Val;

    /// <summary>Gets the id of the linked style (<c>w:link</c>): the character style paired with a paragraph style or the reverse. Null when none.</summary>
    public string? LinkId => View.Link?.Val;

    /// <summary>Gets a value indicating whether this is the default style of its type (<c>w:default</c>).</summary>
    public bool IsDefault => Wml.OnOff(Wml.Attr(Markup, "default")) ?? false;

    /// <summary>Gets a value indicating whether the user defined the style (<c>w:customStyle</c>).</summary>
    public bool IsCustom => Wml.OnOff(Wml.Attr(Markup, "customStyle")) ?? false;

    /// <summary>Gets the sort priority in the gallery (<c>w:uiPriority</c>), or null.</summary>
    public long? UiPriority => View.UiPriority?.Val;

    /// <summary>Gets a value indicating whether the style shows in the quick-style gallery (<c>w:qFormat</c>).</summary>
    public bool IsQuickStyle => View.QFormat is { } q && (q.Val ?? true);

    /// <summary>Gets the paragraph properties (<c>w:pPr</c>) the style sets, or null.</summary>
    public MarkupRef? ParagraphPropertiesElement => Wml.Child(Markup, "pPr"u8);

    /// <summary>Gets the run properties (<c>w:rPr</c>) the style sets, or null.</summary>
    public MarkupRef? RunPropertiesElement => Wml.Child(Markup, "rPr"u8);

    /// <summary>Gets the table properties (<c>w:tblPr</c>) the style sets, or null.</summary>
    public MarkupRef? TablePropertiesElement => Wml.Child(Markup, "tblPr"u8);

    /// <summary>Gets the conditional formatting of a table style (<c>w:tblStylePr</c>): one entry per region, in file order.</summary>
    public IReadOnlyList<TableStyleRegion> TableRegions
    {
        get
        {
            var list = new List<TableStyleRegion>();
            foreach (MarkupRef e in Wml.Elements(Markup))
            {
                if (Wml.Is(e, "tblStylePr"u8))
                {
                    list.Add(new TableStyleRegion(Wml.Attr(e, "type") ?? string.Empty, e));
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Gets the chain of styles this one inherits from through <c>w:basedOn</c>, base first and this style last. A reference to a
    /// style that does not exist ends the chain (<c>docx.style-base-missing</c>); a cycle is cut where it closes (<c>docx.style-cycle</c>).
    /// </summary>
    public IReadOnlyList<StyleDefinition> Lineage => Sheet.LineageOf(this);

    /// <inheritdoc/>
    public bool Equals(StyleDefinition? other) => other is not null && Markup == other.Markup;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as StyleDefinition);

    /// <inheritdoc/>
    public override int GetHashCode() => Markup.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Id;
}

/// <summary>The formatting a table style applies to one region of the table (§17.7.6.6, <c>w:tblStylePr</c>).</summary>
/// <param name="Type">The region: <c>wholeTable</c>, <c>firstRow</c>, <c>lastRow</c>, <c>firstCol</c>, <c>lastCol</c>, <c>band1Vert</c>, <c>band2Vert</c>, <c>band1Horz</c>, <c>band2Horz</c>, <c>neCell</c>, <c>nwCell</c>, <c>seCell</c> or <c>swCell</c>.</param>
/// <param name="Markup">The <c>w:tblStylePr</c> element.</param>
public readonly record struct TableStyleRegion(string Type, MarkupRef Markup)
{
    /// <summary>Gets the paragraph properties the region sets, or null.</summary>
    public MarkupRef? ParagraphProperties => Wml.Child(Markup, "pPr"u8);

    /// <summary>Gets the run properties the region sets, or null.</summary>
    public MarkupRef? RunProperties => Wml.Child(Markup, "rPr"u8);
}

/// <summary>An entry of the latent style list (§17.7.4.8): the defaults Word assumes for a built-in style the file does not define.</summary>
/// <param name="Name">The built-in style name.</param>
/// <param name="Locked">Whether the style is locked.</param>
/// <param name="UiPriority">The gallery sort priority, or null.</param>
/// <param name="SemiHidden">Whether the style is hidden until used.</param>
/// <param name="UnhideWhenUsed">Whether the style unhides once used.</param>
/// <param name="QuickStyle">Whether the style shows in the quick-style gallery.</param>
public readonly record struct LatentStyle(string Name, bool? Locked, long? UiPriority, bool? SemiHidden, bool? UnhideWhenUsed, bool? QuickStyle);

/// <summary>
/// The style sheet of a document (<c>/word/styles.xml</c>): paragraph, character, table and numbering styles, the document
/// defaults, the latent style list, and the resolution of effective properties through them. A document without a styles part
/// has an empty style sheet whose <see cref="Markup"/> is invalid until a style is added.
/// </summary>
/// <remarks>
/// Lookups are cached; changes made through this type keep the cache right, and edits made directly on <see cref="Markup"/>
/// or a view need a call to <see cref="Invalidate"/>.
/// </remarks>
public sealed partial class StyleSheet
{
    private readonly Document _owner;
    private MarkupRef _markup;
    private string? _partName;
    private List<StyleDefinition>? _styles;
    private Dictionary<string, StyleDefinition>? _byId;
    private Dictionary<string, StyleDefinition>? _byName;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    internal StyleSheet(Document owner, MarkupDocument? doc, string? partName)
    {
        _owner = owner;
        _partName = partName;
        _markup = doc is null ? default : new MarkupRef(doc.Root);
    }

    /// <summary>Gets the document the style sheet belongs to.</summary>
    public Document Owner => _owner;

    /// <summary>Gets the <c>w:styles</c> element, or an invalid reference when the document has no styles part.</summary>
    public MarkupRef Markup => _markup;

    /// <summary>Gets a value indicating whether the document has a styles part.</summary>
    public bool IsPresent => _markup.IsValid;

    /// <summary>Gets the typed view of <c>w:styles</c>, or null when the document has no styles part.</summary>
    public W.Styles? View => _markup.IsValid ? W.Styles.Create(_markup) : null;

    /// <summary>Drops the lookup caches. Call after editing the styles part through <see cref="Markup"/> or a view.</summary>
    public void Invalidate()
    {
        _styles = null;
        _byId = null;
        _byName = null;
    }

    /// <summary>Gets the styles in file order.</summary>
    public IReadOnlyList<StyleDefinition> Styles
    {
        get
        {
            EnsureIndex();
            return _styles!;
        }
    }

    private void EnsureIndex()
    {
        if (_styles is not null)
        {
            return;
        }

        var styles = new List<StyleDefinition>();
        var byId = new Dictionary<string, StyleDefinition>(StringComparer.Ordinal);
        var byName = new Dictionary<string, StyleDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (MarkupRef e in Wml.Elements(_markup))
        {
            if (!Wml.Is(e, "style"u8))
            {
                continue;
            }

            var def = new StyleDefinition(this, e);
            styles.Add(def);
            string id = def.Id;
            if (!byId.TryAdd(id, def))
            {
                // Word keeps the first definition of an id and ignores later ones [observed behaviour is not documented; the first wins here].
                Report("docx.style-duplicate-id", DefectKind.Reference, DefectSeverity.Repaired, id,
                    "Style id '" + id + "' is defined more than once; the first definition is used.", e);
            }

            if (def.Name is { } name)
            {
                byName.TryAdd(name, def);
            }
        }

        _byId = byId;
        _byName = byName;
        _styles = styles;
    }

    /// <summary>Finds a style by id.</summary>
    /// <param name="styleId">The id (<c>w:styleId</c>); matched exactly.</param>
    /// <returns>The style, or null when there is none or the id is null.</returns>
    public StyleDefinition? Find(string? styleId)
    {
        if (styleId is null)
        {
            return null;
        }

        EnsureIndex();
        return _byId!.GetValueOrDefault(styleId);
    }

    /// <summary>Finds a style by display name, ignoring case.</summary>
    /// <param name="name">The name (<c>w:name</c>).</param>
    /// <returns>The style, or null.</returns>
    public StyleDefinition? FindByName(string name)
    {
        EnsureIndex();
        return _byName!.GetValueOrDefault(name);
    }

    /// <summary>Gets the default style of a type (<c>w:default="1"</c>): the style that applies when none is named. The first one wins when a file marks several.</summary>
    /// <param name="type">The style type.</param>
    /// <returns>The style, or null.</returns>
    public StyleDefinition? Default(StyleType type)
    {
        foreach (StyleDefinition s in Styles)
        {
            if (s.IsDefault && s.Type == type)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>Gets the paragraph properties element of the document defaults (<c>w:docDefaults/w:pPrDefault/w:pPr</c>), or null.</summary>
    public MarkupRef? DefaultParagraphProperties => Wml.Child(Wml.Child(Wml.Child(_markup, "docDefaults"u8) ?? default, "pPrDefault"u8) ?? default, "pPr"u8);

    /// <summary>Gets the run properties element of the document defaults (<c>w:docDefaults/w:rPrDefault/w:rPr</c>), or null.</summary>
    public MarkupRef? DefaultRunProperties => Wml.Child(Wml.Child(Wml.Child(_markup, "docDefaults"u8) ?? default, "rPrDefault"u8) ?? default, "rPr"u8);

    /// <summary>Gets the latent style list: the per-style defaults for built-in styles the file does not define.</summary>
    public IReadOnlyList<LatentStyle> LatentStyles
    {
        get
        {
            var list = new List<LatentStyle>();
            MarkupRef latent = Wml.Child(_markup, "latentStyles"u8) ?? default;
            foreach (MarkupRef e in Wml.Elements(latent))
            {
                if (Wml.Is(e, "lsdException"u8))
                {
                    list.Add(new LatentStyle(
                        Wml.Attr(e, "name") ?? string.Empty,
                        Wml.OnOff(Wml.Attr(e, "locked")),
                        Wml.Long(Wml.Attr(e, "uiPriority")),
                        Wml.OnOff(Wml.Attr(e, "semiHidden")),
                        Wml.OnOff(Wml.Attr(e, "unhideWhenUsed")),
                        Wml.OnOff(Wml.Attr(e, "qFormat"))));
                }
            }

            return list;
        }
    }

    /// <summary>Gets the theme the document's formatting refers to; <see cref="ThemeData.Empty"/> when it has none.</summary>
    public ThemeData Theme => _owner.Theme;

    internal IReadOnlyList<StyleDefinition> LineageOf(StyleDefinition style)
    {
        var chain = new List<StyleDefinition> { style };
        var seen = new HashSet<string>(StringComparer.Ordinal) { style.Id };
        StyleDefinition current = style;
        while (current.BasedOnId is { } baseId)
        {
            StyleDefinition? next = Find(baseId);
            if (next is null)
            {
                Report("docx.style-base-missing", DefectKind.Reference, DefectSeverity.Repaired, current.Id + ">" + baseId,
                    "Style '" + current.Id + "' is based on '" + baseId + "', which does not exist; the chain ends there.", current.Markup);
                break;
            }

            if (!seen.Add(next.Id))
            {
                Report("docx.style-cycle", DefectKind.Reference, DefectSeverity.Repaired, current.Id + ">" + baseId,
                    "Style '" + current.Id + "' is based on '" + baseId + "', which closes a cycle; the chain is cut there.", current.Markup);
                break;
            }

            chain.Add(next);
            current = next;
        }

        chain.Reverse();
        return chain;
    }

    internal void Report(string code, DefectKind kind, DefectSeverity severity, string key, string message, MarkupRef at)
    {
        // Each distinct problem is reported once however often it is met.
        if (_reported.Add(code + "|" + key))
        {
            _owner.Context.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InPart(_partName ?? "/word/styles.xml", at.IsValid ? at.Node.SourceStart : -1)));
        }
    }

    // --- editing ---

    /// <summary>Creates the part when missing and returns its root; used by operations that move styles between documents.</summary>
    internal MarkupRef EnsureRootForMerge()
    {
        EnsurePart();
        return _markup;
    }

    private void EnsurePart()
    {
        if (_markup.IsValid)
        {
            return;
        }

        MarkupDocument doc = _owner.CreateRelatedPart(
            "styles",
            "/word/styles.xml",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml",
            "styles",
            out string partName);
        _partName = partName;
        _markup = new MarkupRef(doc.Root);
        Invalidate();
    }

    /// <summary>
    /// Adds a style to the end of the style sheet, creating the styles part when the document has none. The style has an id and a
    /// name and sets nothing else; set its properties through <see cref="StyleDefinition.Markup"/> or the typed view.
    /// </summary>
    /// <param name="styleId">The id; must be unique and not empty.</param>
    /// <param name="name">The display name.</param>
    /// <param name="type">The style type.</param>
    /// <param name="basedOnId">The id of the style to inherit from, or null.</param>
    /// <returns>The new style.</returns>
    /// <exception cref="ArgumentException">The id is empty or already used.</exception>
    public StyleDefinition AddStyle(string styleId, string name, StyleType type, string? basedOnId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleId);
        ArgumentNullException.ThrowIfNull(name);
        if (Find(styleId) is not null)
        {
            throw new ArgumentException("A style with id '" + styleId + "' exists.", nameof(styleId));
        }

        EnsurePart();
        W.Style style = W.Styles.Create(_markup).AddStyle();
        style.TypeText = type switch
        {
            StyleType.Character => "character",
            StyleType.Table => "table",
            StyleType.Numbering => "numbering",
            _ => "paragraph",
        };
        style.StyleId = styleId;
        style.GetOrAddName().Val = name;
        if (basedOnId is not null)
        {
            style.GetOrAddBasedOn().Val = basedOnId;
        }

        Invalidate();
        return Find(styleId)!;
    }

    /// <summary>
    /// Applies a paragraph style: sets <c>w:pStyle</c> in the paragraph's properties. Direct formatting stays as it is.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="style">A paragraph style of this style sheet.</param>
    /// <exception cref="ArgumentException">The style is not a paragraph style or belongs to another sheet.</exception>
    public void Apply(Paragraph paragraph, StyleDefinition style)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(style);
        if (!ReferenceEquals(style.Sheet, this) || style.Type != StyleType.Paragraph)
        {
            throw new ArgumentException("The style must be a paragraph style of this document.", nameof(style));
        }

        paragraph.StyleId = style.Id;
    }

    /// <summary>Applies a character style to a run: sets <c>w:rStyle</c> in the run's properties.</summary>
    /// <param name="run">The run.</param>
    /// <param name="style">A character style of this style sheet.</param>
    /// <exception cref="ArgumentException">The style is not a character style or belongs to another sheet.</exception>
    public void Apply(Run run, StyleDefinition style)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(style);
        if (!ReferenceEquals(style.Sheet, this) || style.Type != StyleType.Character)
        {
            throw new ArgumentException("The style must be a character style of this document.", nameof(style));
        }

        run.StyleId = style.Id;
    }
}
