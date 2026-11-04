using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Styles;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;
using W = RecursiveAI.OneOffice.Schema.Wordprocessing;

namespace RecursiveAI.OneOffice.Documents.Numbering;

/// <summary>One level of a list definition, as a concrete numbering instance sees it (an override may replace the level or its start).</summary>
public sealed class LevelDefinition
{
    internal LevelDefinition(int level, MarkupRef element, long? startOverride)
    {
        Level = level;
        Markup = element;
        StartOverride = startOverride;
    }

    /// <summary>Gets the zero-based level (<c>w:ilvl</c>), 0 to 8.</summary>
    public int Level { get; }

    /// <summary>Gets the <c>w:lvl</c> element the level reads from.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the typed view of the level.</summary>
    public W.Level View => W.Level.Create(Markup);

    /// <summary>Gets the <c>w:startOverride</c> value of the numbering instance for this level, or null.</summary>
    public long? StartOverride { get; }

    /// <summary>Gets the first number of the level. An override wins; else <c>w:start</c>; the schema's default is 0 when both are absent (§17.9.25).</summary>
    public long Start => StartOverride ?? Wml.Long(Wml.Attr(Wml.Child(Markup, "start"u8) ?? default, "val")) ?? 0;

    /// <summary>Gets the number format (<c>w:numFmt</c>), <c>decimal</c> when absent.</summary>
    public string Format => Wml.Attr(Wml.Child(Markup, "numFmt"u8) ?? default, "val") ?? "decimal";

    /// <summary>Gets the level text (<c>w:lvlText</c>) with its <c>%1</c> to <c>%9</c> placeholders; empty when absent.</summary>
    public string Text => Wml.Attr(Wml.Child(Markup, "lvlText"u8) ?? default, "val") ?? string.Empty;

    /// <summary>Gets what follows the label: <c>tab</c> (the default), <c>space</c> or <c>nothing</c>.</summary>
    public string Suffix => Wml.Attr(Wml.Child(Markup, "suff"u8) ?? default, "val") ?? "tab";

    /// <summary>Gets a value indicating whether the level is legal numbering (<c>w:isLgl</c>): placeholders of higher levels show as Arabic numerals.</summary>
    public bool IsLegal => Wml.Child(Markup, "isLgl"u8) is { } e && Wml.OnOffElement(e);

    /// <summary>Gets the one-based level whose use restarts this level (<c>w:lvlRestart</c>); 0 means never; null means after any higher level.</summary>
    public long? RestartAfterLevel => Wml.Long(Wml.Attr(Wml.Child(Markup, "lvlRestart"u8) ?? default, "val"));

    /// <summary>Gets the id of the paragraph style linked to this level (<c>w:pStyle</c>), or null.</summary>
    public string? LinkedStyleId => Wml.Attr(Wml.Child(Markup, "pStyle"u8) ?? default, "val");

    /// <summary>Gets the alignment of the label within its space (<c>w:lvlJc</c>), <c>left</c> when absent.</summary>
    public string LabelJustification => Wml.Attr(Wml.Child(Markup, "lvlJc"u8) ?? default, "val") ?? "left";

    /// <summary>Gets the paragraph properties the level applies (<c>w:pPr</c>), or null.</summary>
    public MarkupRef? ParagraphProperties => Wml.Child(Markup, "pPr"u8);

    /// <summary>Gets the run properties of the label (<c>w:rPr</c>), or null.</summary>
    public MarkupRef? RunProperties => Wml.Child(Markup, "rPr"u8);

    /// <summary>Gets a value indicating whether the level's format is <c>bullet</c>.</summary>
    public bool IsBullet => Format == "bullet";
}

/// <summary>An abstract numbering definition (<c>w:abstractNum</c>): nine levels that numbering instances share.</summary>
public sealed class AbstractNumberingDefinition
{
    internal AbstractNumberingDefinition(MarkupRef element)
    {
        Markup = element;
    }

    /// <summary>Gets the <c>w:abstractNum</c> element.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the typed view.</summary>
    public W.AbstractNumbering View => W.AbstractNumbering.Create(Markup);

    /// <summary>Gets the id (<c>w:abstractNumId</c>), or -1 when absent.</summary>
    public long Id => Wml.Long(Wml.Attr(Markup, "abstractNumId")) ?? -1;

    /// <summary>Gets the type (<c>w:multiLevelType</c>: <c>singleLevel</c>, <c>multilevel</c>, <c>hybridMultilevel</c>), or null.</summary>
    public string? MultiLevelType => Wml.Attr(Wml.Child(Markup, "multiLevelType"u8) ?? default, "val");

    /// <summary>Gets the id of the numbering style this definition refers to for its levels (<c>w:numStyleLink</c>), or null.</summary>
    public string? NumberingStyleLink => Wml.Attr(Wml.Child(Markup, "numStyleLink"u8) ?? default, "val");

    /// <summary>Gets the id of the numbering style this definition is the list of (<c>w:styleLink</c>), or null.</summary>
    public string? StyleLink => Wml.Attr(Wml.Child(Markup, "styleLink"u8) ?? default, "val");

    /// <summary>Gets the level elements by level (<c>w:ilvl</c>); a missing level is absent.</summary>
    public IReadOnlyDictionary<int, MarkupRef> Levels
    {
        get
        {
            var map = new SortedDictionary<int, MarkupRef>();
            foreach (MarkupRef e in Wml.Elements(Markup))
            {
                if (Wml.Is(e, "lvl"u8) && Wml.Long(Wml.Attr(e, "ilvl")) is { } i and >= 0 and <= 8)
                {
                    map.TryAdd((int)i, e);
                }
            }

            return map;
        }
    }
}

/// <summary>A numbering instance (<c>w:num</c>): the id paragraphs refer to, the abstract definition it uses and its overrides.</summary>
public sealed class NumberingInstance
{
    internal NumberingInstance(MarkupRef element)
    {
        Markup = element;
    }

    /// <summary>Gets the <c>w:num</c> element.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the typed view.</summary>
    public W.NumberingInstance View => W.NumberingInstance.Create(Markup);

    /// <summary>Gets the id (<c>w:numId</c>) paragraphs refer to, or -1 when absent.</summary>
    public long Id => Wml.Long(Wml.Attr(Markup, "numId")) ?? -1;

    /// <summary>Gets the id of the abstract definition (<c>w:abstractNumId</c>), or -1 when absent.</summary>
    public long AbstractNumberingId => Wml.Long(Wml.Attr(Wml.Child(Markup, "abstractNumId"u8) ?? default, "val")) ?? -1;

    /// <summary>Gets the overrides (<c>w:lvlOverride</c>) by level: a replacement level and/or a start value.</summary>
    internal IEnumerable<(int Level, MarkupRef? Replacement, long? Start)> Overrides
    {
        get
        {
            foreach (MarkupRef e in Wml.Elements(Markup))
            {
                if (Wml.Is(e, "lvlOverride"u8) && Wml.Long(Wml.Attr(e, "ilvl")) is { } i and >= 0 and <= 8)
                {
                    MarkupRef? repl = Wml.Child(e, "lvl"u8);
                    long? start = Wml.Long(Wml.Attr(Wml.Child(e, "startOverride"u8) ?? default, "val"));
                    yield return ((int)i, repl, start);
                }
            }
        }
    }
}

/// <summary>A level of a list that a numbered paragraph is on, with its label.</summary>
/// <param name="Paragraph">The paragraph.</param>
/// <param name="NumberingId">The <c>w:numId</c> of the instance.</param>
/// <param name="Level">The zero-based level.</param>
/// <param name="Value">The counter value at this level.</param>
/// <param name="Text">The label text: the level text with its placeholders filled in. For a bullet, the bullet character as written.</param>
/// <param name="Suffix">What follows the label: <c>tab</c>, <c>space</c> or <c>nothing</c>.</param>
/// <param name="Definition">The level definition (format, label run properties, indents).</param>
/// <param name="NeedsWordOutput">True when a number format in the label is not reproduced exactly (see <see cref="NumberFormatter"/>).</param>
public sealed record ListLabel(Paragraph Paragraph, long NumberingId, int Level, long Value, string Text, string Suffix, LevelDefinition Definition, bool NeedsWordOutput);

/// <summary>
/// The numbering definitions of a document (<c>/word/numbering.xml</c>): abstract definitions, numbering instances with their
/// overrides, and the list labels of numbered paragraphs. A document without a numbering part has none; adding a definition creates it.
/// </summary>
public sealed class NumberingDefinitions
{
    private readonly Document _owner;
    private MarkupRef _markup;
    private string? _partName;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    internal NumberingDefinitions(Document owner, MarkupDocument? doc, string? partName)
    {
        _owner = owner;
        _partName = partName;
        _markup = doc is null ? default : new MarkupRef(doc.Root);
    }

    /// <summary>Gets the document.</summary>
    public Document Owner => _owner;

    /// <summary>Gets the <c>w:numbering</c> element, or an invalid reference when the document has no numbering part.</summary>
    public MarkupRef Markup => _markup;

    /// <summary>Gets a value indicating whether the document has a numbering part.</summary>
    public bool IsPresent => _markup.IsValid;

    /// <summary>Gets the abstract definitions in file order.</summary>
    public IReadOnlyList<AbstractNumberingDefinition> AbstractDefinitions =>
        Wml.Elements(_markup).Where(e => Wml.Is(e, "abstractNum"u8)).Select(e => new AbstractNumberingDefinition(e)).ToList();

    /// <summary>Gets the numbering instances in file order.</summary>
    public IReadOnlyList<NumberingInstance> Instances =>
        Wml.Elements(_markup).Where(e => Wml.Is(e, "num"u8)).Select(e => new NumberingInstance(e)).ToList();

    /// <summary>Finds a numbering instance by <c>w:numId</c>. When a file repeats an id the first wins.</summary>
    /// <param name="numId">The id.</param>
    /// <returns>The instance, or null.</returns>
    public NumberingInstance? FindInstance(long numId)
    {
        foreach (MarkupRef e in Wml.Elements(_markup))
        {
            if (Wml.Is(e, "num"u8) && Wml.Long(Wml.Attr(e, "numId")) == numId)
            {
                return new NumberingInstance(e);
            }
        }

        return null;
    }

    /// <summary>Finds an abstract definition by <c>w:abstractNumId</c>.</summary>
    /// <param name="abstractId">The id.</param>
    /// <returns>The definition, or null.</returns>
    public AbstractNumberingDefinition? FindAbstract(long abstractId)
    {
        foreach (MarkupRef e in Wml.Elements(_markup))
        {
            if (Wml.Is(e, "abstractNum"u8) && Wml.Long(Wml.Attr(e, "abstractNumId")) == abstractId)
            {
                return new AbstractNumberingDefinition(e);
            }
        }

        return null;
    }

    /// <summary>
    /// Follows <c>w:numStyleLink</c> from an abstract definition to the one that holds the levels: a definition that names a
    /// numbering style takes its levels from the list that style uses (§17.9.21). A broken or circular link is reported and the
    /// definition's own levels are used.
    /// </summary>
    /// <param name="definition">The definition an instance names.</param>
    /// <returns>The definition whose levels apply.</returns>
    public AbstractNumberingDefinition ResolveLevelSource(AbstractNumberingDefinition definition)
    {
        AbstractNumberingDefinition current = definition;
        for (int depth = 0; depth < 8; depth++)
        {
            if (current.NumberingStyleLink is not { } link || current.Levels.Count > 0)
            {
                return current;
            }

            StyleDefinition? style = _owner.Styles.Find(link);
            long? numId = style is null ? null : NumberingIdOfStyle(style);
            NumberingInstance? inst = numId is { } n ? FindInstance(n) : null;
            AbstractNumberingDefinition? next = inst is null ? null : FindAbstract(inst.AbstractNumberingId);
            if (next is null || next.Markup == current.Markup)
            {
                Report("docx.num-link-broken", DefectKind.Reference, link, "The numbering style '" + link + "' named by w:numStyleLink does not lead to a list definition.", current.Markup);
                return current;
            }

            current = next;
        }

        Report("docx.num-link-broken", DefectKind.Reference, "depth", "w:numStyleLink chains are nested too deeply.", definition.Markup);
        return current;
    }

    private static long? NumberingIdOfStyle(StyleDefinition style)
    {
        foreach (StyleDefinition s in style.Lineage.Reverse())
        {
            if (s.ParagraphPropertiesElement is { } pPr && Wml.Child(pPr, "numPr"u8) is { } numPr
                && Wml.Long(Wml.Attr(Wml.Child(numPr, "numId"u8) ?? default, "val")) is { } id)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the definition of one level of a numbering instance: the instance's replacement level when it has one, else the abstract
    /// definition's, with the instance's start override.
    /// </summary>
    /// <param name="numId">The <c>w:numId</c>.</param>
    /// <param name="level">The zero-based level, 0 to 8.</param>
    /// <returns>The level, or null when the instance, its definition or the level does not exist.</returns>
    public LevelDefinition? GetLevel(long numId, int level)
    {
        NumberingInstance? inst = FindInstance(numId);
        if (inst is null || level is < 0 or > 8)
        {
            return null;
        }

        MarkupRef? replacement = null;
        long? start = null;
        foreach ((int l, MarkupRef? repl, long? s) in inst.Overrides)
        {
            if (l == level)
            {
                replacement ??= repl;
                start ??= s;
            }
        }

        if (replacement is { } r)
        {
            return new LevelDefinition(level, r, start);
        }

        AbstractNumberingDefinition? abs = FindAbstract(inst.AbstractNumberingId);
        if (abs is null)
        {
            return null;
        }

        return ResolveLevelSource(abs).Levels.TryGetValue(level, out MarkupRef e) ? new LevelDefinition(level, e, start) : null;
    }

    /// <summary>Finds the level of an instance whose <c>w:pStyle</c> links it to a paragraph style.</summary>
    /// <param name="numId">The <c>w:numId</c>.</param>
    /// <param name="styleId">The paragraph style id.</param>
    /// <returns>The zero-based level, or null when no level is linked to the style.</returns>
    public int? FindLevelLinkedTo(long numId, string styleId)
    {
        for (int l = 0; l <= 8; l++)
        {
            if (GetLevel(numId, l)?.LinkedStyleId == styleId)
            {
                return l;
            }
        }

        return null;
    }

    internal void Report(string code, DefectKind kind, string key, string message, MarkupRef at)
    {
        if (_reported.Add(code + "|" + key))
        {
            _owner.Context.Defects.Report(new Defect(kind, code, DefectSeverity.Repaired, message, DefectLocation.InPart(_partName ?? "/word/numbering.xml", at.IsValid ? at.Node.SourceStart : -1)));
        }
    }

    /// <summary>
    /// Computes the list label of every numbered paragraph of the body, in document order. Counters are per abstract definition, so
    /// instances of one definition continue each other's count, and an instance with a start override restarts (as Word does).
    /// </summary>
    /// <returns>The labels; paragraphs that are not numbered have none.</returns>
    public IReadOnlyList<ListLabel> LabelBody() => Label(_owner.Body.Paragraphs());

    /// <summary>Computes the labels of the numbered paragraphs of any one story (body, header, note), given in document order.</summary>
    /// <param name="paragraphs">The paragraphs in document order.</param>
    /// <returns>The labels.</returns>
    public IReadOnlyList<ListLabel> Label(IEnumerable<Paragraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        return new ListLabeler(this).Run(paragraphs);
    }

    /// <summary>Gets the label of one paragraph, counting from the start of the body; null when the paragraph is not numbered.</summary>
    /// <param name="paragraph">A paragraph of the body.</param>
    /// <returns>The label, or null.</returns>
    public ListLabel? LabelOf(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        foreach (ListLabel l in LabelBody())
        {
            if (l.Paragraph == paragraph)
            {
                return l;
            }
        }

        return null;
    }

    // --- editing ---

    private void EnsurePart()
    {
        if (_markup.IsValid)
        {
            return;
        }

        MarkupDocument doc = _owner.CreateRelatedPart(
            "numbering",
            "/word/numbering.xml",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml",
            "numbering",
            out string partName);
        _partName = partName;
        _markup = new MarkupRef(doc.Root);
    }

    /// <summary>What a level of a new list looks like.</summary>
    /// <param name="Format">The <c>w:numFmt</c> value, such as <c>decimal</c> or <c>bullet</c>.</param>
    /// <param name="Text">The level text, such as <c>%1.</c>.</param>
    /// <param name="Start">The first number.</param>
    /// <param name="LeftIndent">The left indent of the paragraph in twentieths of a point.</param>
    /// <param name="Hanging">The hanging indent in twentieths of a point.</param>
    public readonly record struct LevelSpec(string Format, string Text, long Start = 1, long LeftIndent = 720, long Hanging = 360);

    /// <summary>
    /// Adds an abstract definition with the given levels (up to nine) and returns its id. The element goes before the first numbering
    /// instance, as the schema requires. Levels not given are not defined.
    /// </summary>
    /// <param name="levels">The levels, level 0 first.</param>
    /// <returns>The new <c>w:abstractNumId</c>.</returns>
    /// <exception cref="ArgumentException">No level is given, or more than nine.</exception>
    public long AddAbstractNumbering(IReadOnlyList<LevelSpec> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count is < 1 or > 9)
        {
            throw new ArgumentException("An abstract numbering definition has one to nine levels.", nameof(levels));
        }

        EnsurePart();
        string p = PrefixOf(_markup, out string decl);
        long id = AbstractDefinitions.Select(a => a.Id).DefaultIfEmpty(-1).Max() + 1;
        var sb = new StringBuilder();
        sb.Append('<').Append(p).Append(":abstractNum").Append(decl).Append(' ').Append(p).Append(":abstractNumId=\"").Append(id.ToString(CultureInfo.InvariantCulture)).Append("\">");
        sb.Append('<').Append(p).Append(":multiLevelType ").Append(p).Append(":val=\"").Append(levels.Count == 1 ? "singleLevel" : "hybridMultilevel").Append("\"/>");
        for (int i = 0; i < levels.Count; i++)
        {
            LevelSpec s = levels[i];
            sb.Append('<').Append(p).Append(":lvl ").Append(p).Append(":ilvl=\"").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\">");
            sb.Append('<').Append(p).Append(":start ").Append(p).Append(":val=\"").Append(s.Start.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
            sb.Append('<').Append(p).Append(":numFmt ").Append(p).Append(":val=\"").Append(Escape(s.Format)).Append("\"/>");
            sb.Append('<').Append(p).Append(":lvlText ").Append(p).Append(":val=\"").Append(Escape(s.Text)).Append("\"/>");
            sb.Append('<').Append(p).Append(":lvlJc ").Append(p).Append(":val=\"left\"/>");
            sb.Append('<').Append(p).Append(":pPr><").Append(p).Append(":ind ").Append(p).Append(":left=\"").Append(s.LeftIndent.ToString(CultureInfo.InvariantCulture))
                .Append("\" ").Append(p).Append(":hanging=\"").Append(s.Hanging.ToString(CultureInfo.InvariantCulture)).Append("\"/></").Append(p).Append(":pPr>");
            sb.Append("</").Append(p).Append(":lvl>");
        }

        sb.Append("</").Append(p).Append(":abstractNum>");
        byte[] xml = Encoding.UTF8.GetBytes(sb.ToString());

        // ECMA-376 Part 1 §17.9.17 (numbering): numPicBullet*, abstractNum*, num*, numIdMacAtCleanup?: so the new definition goes
        // before the first w:num.
        MarkupRef? firstNum = null;
        foreach (MarkupRef e in Wml.Elements(_markup))
        {
            if (Wml.Is(e, "num"u8) || Wml.Is(e, "numIdMacAtCleanup"u8))
            {
                firstNum = e;
                break;
            }
        }

        if (firstNum is { } before)
        {
            before.Node.InsertXmlBefore(xml);
        }
        else
        {
            _markup.Node.AppendXml(xml);
        }

        return id;
    }

    /// <summary>Adds a numbering instance of an abstract definition and returns its id (the value paragraphs put in <c>w:numId</c>).</summary>
    /// <param name="abstractNumberingId">The abstract definition's id.</param>
    /// <param name="startOverrides">Levels to restart at a given number (level to start value), or null. Such an instance starts its list afresh.</param>
    /// <returns>The new <c>w:numId</c>.</returns>
    /// <exception cref="ArgumentException">The abstract definition does not exist.</exception>
    public long AddInstance(long abstractNumberingId, IReadOnlyDictionary<int, long>? startOverrides = null)
    {
        if (!_markup.IsValid || FindAbstract(abstractNumberingId) is null)
        {
            throw new ArgumentException("The abstract numbering definition does not exist.", nameof(abstractNumberingId));
        }

        string p = PrefixOf(_markup, out string decl);
        long id = Instances.Select(i => i.Id).DefaultIfEmpty(0).Max() + 1;
        var sb = new StringBuilder();
        sb.Append('<').Append(p).Append(":num").Append(decl).Append(' ').Append(p).Append(":numId=\"").Append(id.ToString(CultureInfo.InvariantCulture)).Append("\">");
        sb.Append('<').Append(p).Append(":abstractNumId ").Append(p).Append(":val=\"").Append(abstractNumberingId.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
        if (startOverrides is not null)
        {
            foreach (KeyValuePair<int, long> kv in startOverrides.OrderBy(k => k.Key))
            {
                sb.Append('<').Append(p).Append(":lvlOverride ").Append(p).Append(":ilvl=\"").Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\">");
                sb.Append('<').Append(p).Append(":startOverride ").Append(p).Append(":val=\"").Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append("\"/></").Append(p).Append(":lvlOverride>");
            }
        }

        sb.Append("</").Append(p).Append(":num>");
        byte[] xml = Encoding.UTF8.GetBytes(sb.ToString());
        MarkupRef? cleanup = Wml.Child(_markup, "numIdMacAtCleanup"u8);
        if (cleanup is { } c)
        {
            c.Node.InsertXmlBefore(xml);
        }
        else
        {
            _markup.Node.AppendXml(xml);
        }

        return id;
    }

    private static string PrefixOf(MarkupRef root, out string declaration)
    {
        // The new elements use the prefix the part already binds to the WordprocessingML namespace, so the part keeps one style.
        // A part that only has it as the default namespace gets an explicit declaration on the new element: attributes need a prefix.
        string uri = root.NamespaceUriText;
        if (root.Node.TryGetPrefixInScope(uri, out string prefix) && prefix.Length > 0)
        {
            declaration = string.Empty;
            return prefix;
        }

        declaration = " xmlns:w=\"" + uri + "\"";
        return "w";
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
