using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Documents.ContentControls;

/// <summary>The kind of a content control, from the element in its <c>w:sdtPr</c> that names the type.</summary>
public enum ContentControlType
{
    /// <summary>Rich text (<c>w:richText</c>). A control with no type element is rich text too (<see cref="ContentControl.IsTypeExplicit"/> tells them apart).</summary>
    RichText = 0,

    /// <summary>Plain text (<c>w:text</c>); <see cref="ContentControl.IsMultiLine"/> says whether it may hold line breaks.</summary>
    PlainText = 1,

    /// <summary>A picture (<c>w:picture</c>).</summary>
    Picture = 2,

    /// <summary>A date picker (<c>w:date</c>).</summary>
    Date = 3,

    /// <summary>A drop-down list (<c>w:dropDownList</c>): the value must be one of the items.</summary>
    DropDownList = 4,

    /// <summary>A combo box (<c>w:comboBox</c>): an item or free text.</summary>
    ComboBox = 5,

    /// <summary>A check box (<c>w14:checkbox</c>).</summary>
    Checkbox = 6,

    /// <summary>A repeating section (<c>w15:repeatingSection</c>); its items are the <see cref="RepeatingSectionItem"/> children.</summary>
    RepeatingSection = 7,

    /// <summary>One item of a repeating section (<c>w15:repeatingSectionItem</c>).</summary>
    RepeatingSectionItem = 8,

    /// <summary>A group (<c>w:group</c>): a container that locks its content as a unit.</summary>
    Group = 9,

    /// <summary>A citation (<c>w:citation</c>).</summary>
    Citation = 10,

    /// <summary>A bibliography (<c>w:bibliography</c>).</summary>
    Bibliography = 11,

    /// <summary>An equation (<c>w:equation</c>).</summary>
    Equation = 12,

    /// <summary>A building block gallery control (<c>w:docPartObj</c>), such as a table of contents or a cover page.</summary>
    BuildingBlock = 13,

    /// <summary>A building block list (<c>w:docPartList</c>).</summary>
    BuildingBlockList = 14,
}

/// <summary>Where in the document a content control sits.</summary>
public enum ContentControlLevel
{
    /// <summary>Around paragraphs and tables.</summary>
    Block = 0,

    /// <summary>Inside a paragraph, around runs.</summary>
    Inline = 1,

    /// <summary>Around table rows (<c>w:tr</c>).</summary>
    Row = 2,

    /// <summary>Around table cells (<c>w:tc</c>).</summary>
    Cell = 3,
}

/// <summary>What a content control's lock (<c>w:lock</c>) prevents.</summary>
public enum ContentControlLock
{
    /// <summary>No lock: the control can be deleted and its content edited.</summary>
    Unlocked = 0,

    /// <summary>The control cannot be deleted; its content can be edited.</summary>
    SdtLocked = 1,

    /// <summary>The content cannot be edited; the control can be deleted.</summary>
    ContentLocked = 2,

    /// <summary>Neither the control can be deleted nor its content edited.</summary>
    SdtContentLocked = 3,
}

/// <summary>One entry of a drop-down list or combo box.</summary>
/// <param name="DisplayText">The text shown (<c>w:displayText</c>; the value when absent).</param>
/// <param name="Value">The stored value (<c>w:value</c>; the display text when absent).</param>
public readonly record struct ContentControlListItem(string DisplayText, string Value);

/// <summary>
/// A structured document tag (<c>w:sdt</c>, a content control) at block, inline, row or cell level: a thin wrapper over the element.
/// Reads go straight to the markup; edits change only the control's own markup and keep what is unknown.
/// ECMA-376 Part 1 §17.5.2.
/// </summary>
public sealed class ContentControl : IEquatable<ContentControl>
{
    internal const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    internal const string W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    internal ContentControl(Document owner, MarkupRef markup, string? partName)
    {
        Owner = owner;
        Markup = markup;
        PartName = partName;
    }

    /// <summary>Gets the document the control belongs to.</summary>
    public Document Owner { get; }

    /// <summary>Gets the <c>w:sdt</c> element in the markup tree.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the name of the part the control is in, or null when it was reached from a hub object.</summary>
    public string? PartName { get; }

    /// <summary>Gets the <c>w:sdtPr</c> element, or null when the control has none.</summary>
    public MarkupRef? PropertiesElement => Wml.Child(Markup, "sdtPr"u8);

    /// <summary>Gets the <c>w:sdtContent</c> element, or null when the control has none.</summary>
    public MarkupRef? ContentElement => Wml.Child(Markup, "sdtContent"u8);

    /// <summary>
    /// Gets where the control sits, from its parent: a paragraph (or a wrapper inside one) makes it inline, a table row-level,
    /// a table row cell-level; anything else is block-level. A control nested in another has its parent control's level.
    /// </summary>
    public ContentControlLevel Level
    {
        get
        {
            // ECMA-376 Part 1 §17.5.2 (sdt, sdtContent): the content's kind follows the place of the tag.
            MarkupNode n = Markup.Node.Parent;
            while (!n.IsNull && n.Kind == MarkupNodeKind.Element)
            {
                var e = new MarkupRef(n);
                if (!e.IsValid || e.Namespace != SchemaNamespace.Wordprocessing)
                {
                    return ContentControlLevel.Block;
                }

                ReadOnlySpan<byte> name = e.LocalName;
                if (name.SequenceEqual("p"u8) || name.SequenceEqual("hyperlink"u8) || name.SequenceEqual("fldSimple"u8))
                {
                    return ContentControlLevel.Inline;
                }

                if (name.SequenceEqual("tbl"u8))
                {
                    return ContentControlLevel.Row;
                }

                if (name.SequenceEqual("tr"u8))
                {
                    return ContentControlLevel.Cell;
                }

                bool transparent = name.SequenceEqual("sdtContent"u8) || name.SequenceEqual("sdt"u8) || name.SequenceEqual("ins"u8)
                    || name.SequenceEqual("del"u8) || name.SequenceEqual("moveFrom"u8) || name.SequenceEqual("moveTo"u8)
                    || name.SequenceEqual("customXml"u8) || name.SequenceEqual("smartTag"u8) || name.SequenceEqual("dir"u8) || name.SequenceEqual("bdo"u8);
                if (!transparent)
                {
                    return ContentControlLevel.Block;
                }

                n = n.Parent;
            }

            return ContentControlLevel.Block;
        }
    }

    private MarkupNode TypeElement
    {
        get
        {
            MarkupRef? pr = PropertiesElement;
            if (pr is null)
            {
                return default;
            }

            foreach (MarkupNode c in pr.Value.Node.ChildElements)
            {
                if (TypeOf(c) is not null)
                {
                    return c;
                }
            }

            return default;
        }
    }

    private static ContentControlType? TypeOf(MarkupNode e)
    {
        string ns = e.NamespaceUri;
        string local = e.LocalNameString;
        if (ns == W14)
        {
            return local == "checkbox" ? ContentControlType.Checkbox : null;
        }

        if (ns == W15)
        {
            return local switch
            {
                "repeatingSection" => ContentControlType.RepeatingSection,
                "repeatingSectionItem" => ContentControlType.RepeatingSectionItem,
                _ => null,
            };
        }

        if (SchemaNamespaces.Resolve(ns) != SchemaNamespace.Wordprocessing)
        {
            return null;
        }

        // ECMA-376 Part 1 §17.5.2 (sdtPr): the type is the one choice element among the properties.
        return local switch
        {
            "richText" => ContentControlType.RichText,
            "text" => ContentControlType.PlainText,
            "picture" => ContentControlType.Picture,
            "date" => ContentControlType.Date,
            "dropDownList" => ContentControlType.DropDownList,
            "comboBox" => ContentControlType.ComboBox,
            "group" => ContentControlType.Group,
            "citation" => ContentControlType.Citation,
            "bibliography" => ContentControlType.Bibliography,
            "equation" => ContentControlType.Equation,
            "docPartObj" => ContentControlType.BuildingBlock,
            "docPartList" => ContentControlType.BuildingBlockList,
            _ => null,
        };
    }

    /// <summary>Gets the control's type. A control whose properties name no type is <see cref="ContentControlType.RichText"/>.</summary>
    public ContentControlType Type
    {
        get
        {
            // A repeating section item and a repeating section can sit beside another type element; the w15 ones win.
            MarkupRef? pr = PropertiesElement;
            if (pr is null)
            {
                return ContentControlType.RichText;
            }

            ContentControlType? found = null;
            foreach (MarkupNode c in pr.Value.Node.ChildElements)
            {
                ContentControlType? t = TypeOf(c);
                if (t is ContentControlType.RepeatingSection or ContentControlType.RepeatingSectionItem)
                {
                    return t.Value;
                }

                found ??= t;
            }

            return found ?? ContentControlType.RichText;
        }
    }

    /// <summary>Gets a value indicating whether the properties name the type explicitly.</summary>
    public bool IsTypeExplicit => !TypeElement.IsNull;

    /// <summary>Gets a value indicating whether a plain text control accepts line breaks (<c>w:text w:multiLine</c>).</summary>
    public bool IsMultiLine => Type == ContentControlType.PlainText && Wml.Attr(new MarkupRef(TypeElement), "multiLine") is { } v && Wml.OnOff(v) == true;

    private string? PropertyValue(ReadOnlySpan<byte> local) => PropertiesElement is { } pr && Wml.Child(pr, local) is { } e ? Wml.Attr(e, "val") : null;

    /// <summary>Gets the tag (<c>w:tag</c>), the programmatic name; null when absent.</summary>
    public string? Tag => PropertyValue("tag"u8);

    /// <summary>Gets the alias (<c>w:alias</c>), the title shown to the user; null when absent.</summary>
    public string? Alias => PropertyValue("alias"u8);

    /// <summary>Gets the control's identifier (<c>w:id</c>); null when absent or not a number.</summary>
    public long? Id => Wml.Long(PropertyValue("id"u8));

    /// <summary>Gets the lock (<c>w:lock</c>); <see cref="ContentControlLock.Unlocked"/> when absent or unknown.</summary>
    public ContentControlLock Lock => PropertyValue("lock"u8) switch
    {
        "sdtLocked" => ContentControlLock.SdtLocked,
        "contentLocked" => ContentControlLock.ContentLocked,
        "sdtContentLocked" => ContentControlLock.SdtContentLocked,
        _ => ContentControlLock.Unlocked,
    };

    /// <summary>Gets a value indicating whether Word removes the control once its content is edited (<c>w:temporary</c>).</summary>
    public bool IsTemporary => PropertiesElement is { } pr && Wml.Child(pr, "temporary"u8) is { } e && Wml.OnOffElement(e);

    /// <summary>Gets a value indicating whether the content is the placeholder text (<c>w:showingPlcHdr</c>), not a value.</summary>
    public bool IsShowingPlaceholder => PropertiesElement is { } pr && Wml.Child(pr, "showingPlcHdr"u8) is { } e && Wml.OnOffElement(e);

    /// <summary>Gets the glossary entry that holds the placeholder text (<c>w:placeholder/w:docPart</c>); null when absent.</summary>
    public string? PlaceholderDocPart => PropertiesElement is { } pr && Wml.Child(pr, "placeholder"u8) is { } ph && Wml.Child(ph, "docPart"u8) is { } dp ? Wml.Attr(dp, "val") : null;

    /// <summary>Gets the building block gallery of a <see cref="ContentControlType.BuildingBlock"/> control (for example <c>Table of Contents</c>); null otherwise.</summary>
    public string? BuildingBlockGallery
    {
        get
        {
            MarkupNode t = TypeElement;
            if (t.IsNull || Type is not (ContentControlType.BuildingBlock or ContentControlType.BuildingBlockList))
            {
                return null;
            }

            return Wml.Child(new MarkupRef(t), "docPartGallery"u8) is { } g ? Wml.Attr(g, "val") : null;
        }
    }

    /// <summary>Gets the list items of a drop-down list or combo box; empty for other types.</summary>
    public IReadOnlyList<ContentControlListItem> ListItems
    {
        get
        {
            var items = new List<ContentControlListItem>();
            if (Type is ContentControlType.DropDownList or ContentControlType.ComboBox)
            {
                foreach (MarkupRef li in Wml.Elements(new MarkupRef(TypeElement)))
                {
                    if (!Wml.Is(li, "listItem"u8))
                    {
                        continue;
                    }

                    string? value = Wml.Attr(li, "value");
                    string? display = Wml.Attr(li, "displayText");
                    items.Add(new ContentControlListItem(display ?? value ?? string.Empty, value ?? display ?? string.Empty));
                }
            }

            return items;
        }
    }

    /// <summary>Gets the stored value of the list selection (<c>w:lastValue</c>); null when absent or not a list.</summary>
    public string? LastValue => Type is ContentControlType.DropDownList or ContentControlType.ComboBox ? Wml.Attr(new MarkupRef(TypeElement), "lastValue") : null;

    /// <summary>Gets the index in <see cref="ListItems"/> of the selected item: the item whose display text equals the content, else the one whose value is <see cref="LastValue"/>; null when none is.</summary>
    public int? SelectedIndex
    {
        get
        {
            IReadOnlyList<ContentControlListItem> items = ListItems;
            if (items.Count == 0 || IsShowingPlaceholder)
            {
                return null;
            }

            string text = Text;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].DisplayText == text)
                {
                    return i;
                }
            }

            if (LastValue is { } last)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Value == last)
                    {
                        return i;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>Gets the check box state (<c>w14:checked</c>); null when the control is not a check box. A check box without the element is unchecked.</summary>
    public bool? IsChecked
    {
        get
        {
            if (Type != ContentControlType.Checkbox)
            {
                return null;
            }

            MarkupNode cb = TypeElement;
            foreach (MarkupNode c in cb.ChildElements)
            {
                if (c.NamespaceUri == W14 && c.LocalNameString == "checked")
                {
                    return Wml.OnOff(c.GetAttribute(W14, "val")) ?? true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the date format code (<c>w:dateFormat</c>) of a date control; null otherwise.</summary>
    public string? DateFormat => DateChild("dateFormat"u8);

    /// <summary>Gets the date a date control holds (<c>w:date w:fullDate</c>); null when not a date control, empty or unparsable.</summary>
    public DateTimeOffset? Date
    {
        get
        {
            if (Type != ContentControlType.Date)
            {
                return null;
            }

            string? full = Wml.Attr(new MarkupRef(TypeElement), "fullDate");
            return full is not null && DateTimeOffset.TryParse(full, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset d) ? d : null;
        }
    }

    private string? DateChild(ReadOnlySpan<byte> local) =>
        Type == ContentControlType.Date && Wml.Child(new MarkupRef(TypeElement), local) is { } e ? Wml.Attr(e, "val") : null;

    /// <summary>Gets the data binding (<c>w:dataBinding</c>), or null when the control is unbound.</summary>
    public ContentControlBinding? Binding
    {
        get
        {
            if (PropertiesElement is not { } pr || Wml.Child(pr, "dataBinding"u8) is not { } b)
            {
                return null;
            }

            return new ContentControlBinding(Wml.Attr(b, "xpath"), Wml.Attr(b, "prefixMappings"), Wml.Attr(b, "storeItemID"));
        }
    }

    /// <summary>Evaluates the data binding against the custom XML parts. An unbound control gives <see cref="BindingStatus.Invalid"/>.</summary>
    /// <returns>The bound node's current value, or why there is none.</returns>
    public BindingValue EvaluateBinding() => Binding?.Evaluate(Owner) ?? new BindingValue(BindingStatus.Invalid, null);

    /// <summary>Gets the control this one is nested in, or null.</summary>
    public ContentControl? Parent
    {
        get
        {
            for (MarkupNode n = Markup.Node.Parent; !n.IsNull && n.Kind == MarkupNodeKind.Element; n = n.Parent)
            {
                var e = new MarkupRef(n);
                if (Wml.Is(e, "sdt"u8))
                {
                    return new ContentControl(Owner, e, PartName);
                }
            }

            return null;
        }
    }

    /// <summary>Enumerates the controls nested directly in this one (not those nested deeper), in document order. For a repeating section these are its items.</summary>
    /// <returns>The child controls.</returns>
    public IEnumerable<ContentControl> Children()
    {
        MarkupRef? content = ContentElement;
        if (content is null)
        {
            yield break;
        }

        foreach (MarkupNode n in DocumentOrder.Elements(content.Value.Node, stopAt: static e => Wml.Is(new MarkupRef(e), "sdt"u8)))
        {
            if (Wml.Is(new MarkupRef(n), "sdt"u8))
            {
                yield return new ContentControl(Owner, new MarkupRef(n), PartName);
            }
        }
    }

    /// <summary>
    /// Gets the text of the content: <c>w:t</c> text, tabs and line breaks (<c>\t</c>, <c>\n</c>), with <c>\n</c> between paragraphs.
    /// Deleted text, drawings and field instructions are not text. The placeholder text counts when it is showing
    /// (<see cref="IsShowingPlaceholder"/>).
    /// </summary>
    public string Text
    {
        get
        {
            MarkupRef? content = ContentElement;
            if (content is null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            bool pendingBreak = false;
            AppendText(content.Value.Node, sb, ref pendingBreak);
            return sb.ToString();
        }
    }

    private static void AppendText(MarkupNode parent, StringBuilder sb, ref bool pendingBreak)
    {
        // Iterative walk: skip drawings and fallbacks, whose text repeats or is not flow text.
        MarkupNode n = parent.FirstChild;
        Stack<MarkupNode> stack = new();
        while (true)
        {
            if (n.IsNull)
            {
                if (stack.Count == 0)
                {
                    return;
                }

                n = stack.Pop().NextSibling;
                continue;
            }

            if (n.Kind != MarkupNodeKind.Element)
            {
                n = n.NextSibling;
                continue;
            }

            var e = new MarkupRef(n);
            bool descend = true;
            if (e.Namespace == SchemaNamespace.Wordprocessing)
            {
                ReadOnlySpan<byte> name = e.LocalName;
                if (name.SequenceEqual("t"u8))
                {
                    FlushBreak(sb, ref pendingBreak);
                    sb.Append(n.InnerText);
                    descend = false;
                }
                else if (name.SequenceEqual("tab"u8) && IsRunChild(n))
                {
                    FlushBreak(sb, ref pendingBreak);
                    sb.Append('\t');
                    descend = false;
                }
                else if (name.SequenceEqual("br"u8) || name.SequenceEqual("cr"u8))
                {
                    FlushBreak(sb, ref pendingBreak);
                    sb.Append('\n');
                    descend = false;
                }
                else if (name.SequenceEqual("p"u8))
                {
                    if (sb.Length > 0 || pendingBreak)
                    {
                        pendingBreak = true;
                    }
                }
                else if (name.SequenceEqual("drawing"u8) || name.SequenceEqual("pict"u8) || name.SequenceEqual("object"u8)
                    || name.SequenceEqual("pPr"u8) || name.SequenceEqual("rPr"u8) || name.SequenceEqual("sdtPr"u8) || name.SequenceEqual("sdtEndPr"u8)
                    || name.SequenceEqual("instrText"u8) || name.SequenceEqual("delText"u8) || name.SequenceEqual("del"u8))
                {
                    descend = false;
                }
            }
            else if (n.LocalNameString == "Fallback" && n.NamespaceUri.Contains("markup-compatibility", StringComparison.Ordinal))
            {
                descend = false;
            }

            if (descend && !n.FirstChild.IsNull)
            {
                stack.Push(n);
                n = n.FirstChild;
            }
            else
            {
                n = n.NextSibling;
            }
        }
    }

    private static bool IsRunChild(MarkupNode e)
    {
        MarkupNode p = e.Parent;
        return !p.IsNull && Wml.Is(new MarkupRef(p), "r"u8);
    }

    private static void FlushBreak(StringBuilder sb, ref bool pendingBreak)
    {
        if (pendingBreak)
        {
            sb.Append('\n');
            pendingBreak = false;
        }
    }

    // ---- edits ----

    /// <summary>
    /// Replaces the content with <paramref name="text"/> and clears the placeholder state. Inline: one run carrying the first
    /// run's properties, with <c>w:br</c> for <c>\n</c> (only in rich or multi-line plain text; elsewhere a space) and <c>w:tab</c> for <c>\t</c>.
    /// Block: one paragraph per line, with the first paragraph's properties. Everything else in the control's
    /// properties is kept. A drop-down list takes only an item's display text or value; the data store is not updated.
    /// </summary>
    /// <param name="text">The new text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentException">The text has a character XML cannot carry, or is not an item of a drop-down list.</exception>
    /// <exception cref="InvalidOperationException">The control's type or level has no text (check box, picture, repeating section, group, row- and cell-level controls …).</exception>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ContentControlType type = Type;
        if (type is not (ContentControlType.RichText or ContentControlType.PlainText or ContentControlType.Date or ContentControlType.ComboBox
            or ContentControlType.DropDownList or ContentControlType.Citation))
        {
            throw new InvalidOperationException("A " + type + " content control has no settable text.");
        }

        if (type == ContentControlType.DropDownList)
        {
            IReadOnlyList<ContentControlListItem> items = ListItems;
            int found = -1;
            for (int i = 0; i < items.Count && found < 0; i++)
            {
                if (items[i].DisplayText == text || items[i].Value == text)
                {
                    found = i;
                }
            }

            if (found < 0)
            {
                throw new ArgumentException("The text is not an item of the drop-down list.", nameof(text));
            }

            SelectItem(found);
            return;
        }

        ReplaceContent(text);
    }

    /// <summary>Selects an item of a drop-down list or combo box: the content becomes its display text and <c>w:lastValue</c> its value.</summary>
    /// <param name="index">The index into <see cref="ListItems"/>.</param>
    /// <exception cref="InvalidOperationException">The control is not a drop-down list or combo box.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not an item.</exception>
    public void SelectItem(int index)
    {
        if (Type is not (ContentControlType.DropDownList or ContentControlType.ComboBox))
        {
            throw new InvalidOperationException("The control is not a drop-down list or combo box.");
        }

        IReadOnlyList<ContentControlListItem> items = ListItems;
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, items.Count);
        ReplaceContent(items[index].DisplayText);
        MarkupNode list = TypeElement;
        list.SetAttribute(list.NamespaceUri, "lastValue", items[index].Value);
    }

    /// <summary>
    /// Sets a check box: <c>w14:checked</c> and the glyph in the content (the checked or unchecked state's character, ☒ and ☐ by default).
    /// </summary>
    /// <param name="isChecked">The new state.</param>
    /// <exception cref="InvalidOperationException">The control is not a check box.</exception>
    public void SetChecked(bool isChecked)
    {
        if (Type != ContentControlType.Checkbox)
        {
            throw new InvalidOperationException("The control is not a check box.");
        }

        MarkupNode cb = TypeElement;
        string glyph = (isChecked ? "\u2612" : "\u2610");
        foreach (MarkupNode c in cb.ChildElements)
        {
            if (c.NamespaceUri == W14 && c.LocalNameString == (isChecked ? "checkedState" : "uncheckedState")
                && c.GetAttribute(W14, "val") is { } hex
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp) && cp is > 0 and < 0x110000 && !(cp is >= 0xD800 and <= 0xDFFF))
            {
                glyph = char.ConvertFromUtf32(cp);
            }
        }

        string value = isChecked ? "1" : "0";
        MarkupNode existing = default;
        foreach (MarkupNode c in cb.ChildElements)
        {
            if (c.NamespaceUri == W14 && c.LocalNameString == "checked")
            {
                existing = c;
            }
        }

        if (!existing.IsNull)
        {
            existing.SetAttribute(W14, "val", value, "w14");
        }
        else
        {
            // [MS-DOCX] (w14:checkbox): w14:checked comes first among the check box's children.
            cb.PrependXml(Encoding.UTF8.GetBytes("<w14:checked xmlns:w14=\"" + W14 + "\" w14:val=\"" + value + "\"/>"));
        }

        // Replace the glyph text in place so the run's font (a symbol font, usually) stays.
        foreach (MarkupNode t in DocumentOrder.Elements(ContentElement?.Node ?? default, stopAt: null))
        {
            if (Wml.Is(new MarkupRef(t), "t"u8))
            {
                t.SetText(glyph);
                return;
            }
        }

        ReplaceContent(glyph);
    }

    /// <summary>
    /// Removes the control and puts its content in its place: the paragraphs, runs, rows or cells of <c>w:sdtContent</c> stay where they
    /// are in the document, the control's properties go. A control with empty content disappears.
    /// </summary>
    /// <exception cref="InvalidOperationException">The control is the root of its part.</exception>
    public void RemoveKeepingContent()
    {
        MarkupRef? content = ContentElement;
        var xml = new StringBuilder();
        if (content is not null)
        {
            foreach (MarkupNode child in content.Value.Node.Children)
            {
                xml.Append(child.OuterXml);
            }
        }

        Markup.Node.ReplaceWithXml(Encoding.UTF8.GetBytes(xml.ToString()));
    }

    /// <summary>Removes the control and its content.</summary>
    /// <exception cref="InvalidOperationException">The control is the root of its part.</exception>
    public void RemoveWithContent() => Markup.Node.Remove();

    private string Q(string local)
    {
        string prefix = Encoding.UTF8.GetString(Markup.Node.Prefix);
        return prefix.Length == 0 ? local : prefix + ":" + local;
    }

    private void ReplaceContent(string text)
    {
        ContentControlLevel level = Level;
        if (level is ContentControlLevel.Row or ContentControlLevel.Cell)
        {
            throw new InvalidOperationException("A row-level or cell-level content control has no settable text.");
        }

        MarkupRef? contentRef = ContentElement;
        bool showing = IsShowingPlaceholder;

        // Keep the formatting of the first run (and, for blocks, the first paragraph) unless the content is the placeholder,
        // whose run style is the placeholder look.
        string rPr = string.Empty;
        string pPr = string.Empty;
        if (contentRef is not null)
        {
            foreach (MarkupNode n in DocumentOrder.Elements(contentRef.Value.Node, stopAt: null))
            {
                var e = new MarkupRef(n);
                if (pPr.Length == 0 && level == ContentControlLevel.Block && Wml.Is(e, "p"u8) && Wml.Child(e, "pPr"u8) is { } pp)
                {
                    pPr = pp.Node.OuterXml;
                }

                if (rPr.Length == 0 && !showing && Wml.Is(e, "r"u8) && Wml.Child(e, "rPr"u8) is { } rp)
                {
                    rPr = rp.Node.OuterXml;
                }
            }
        }

        bool lines = Type != ContentControlType.PlainText || IsMultiLine;
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (!lines)
        {
            normalized = normalized.Replace('\n', ' ');
        }

        var xml = new StringBuilder();
        if (level == ContentControlLevel.Block)
        {
            string[] parts = normalized.Split('\n');
            foreach (string line in parts)
            {
                xml.Append('<').Append(Q("p")).Append('>').Append(pPr);
                AppendRun(xml, line, rPr);
                xml.Append("</").Append(Q("p")).Append('>');
            }
        }
        else
        {
            AppendRun(xml, normalized, rPr);
        }

        MarkupNode contentNode;
        if (contentRef is null)
        {
            Markup.Node.AppendXml(Encoding.UTF8.GetBytes("<" + Q("sdtContent") + "/>"));
            contentNode = ContentElement!.Value.Node;
        }
        else
        {
            contentNode = contentRef.Value.Node;
            contentNode.SetText(string.Empty);
        }

        contentNode.AppendXml(Encoding.UTF8.GetBytes(xml.ToString()));

        if (showing && PropertiesElement is { } pr && Wml.Child(pr, "showingPlcHdr"u8) is { } sp)
        {
            sp.Node.Remove();
        }
    }

    private void AppendRun(StringBuilder xml, string text, string rPr)
    {
        string r = Q("r");
        xml.Append('<').Append(r).Append('>').Append(rPr);
        int start = 0;
        bool any = false;
        for (int i = 0; i <= text.Length; i++)
        {
            char c = i < text.Length ? text[i] : '\0';
            if (i == text.Length || c is '\n' or '\t')
            {
                if (i > start)
                {
                    xml.Append('<').Append(Q("t")).Append(" xml:space=\"preserve\">");
                    Escape(xml, text.AsSpan(start, i - start));
                    xml.Append("</").Append(Q("t")).Append('>');
                    any = true;
                }

                if (i < text.Length)
                {
                    xml.Append('<').Append(Q(c == '\n' ? "br" : "tab")).Append("/>");
                    any = true;
                }

                start = i + 1;
            }
        }

        if (!any)
        {
            xml.Append('<').Append(Q("t")).Append("/>");
        }

        xml.Append("</").Append(r).Append('>');
    }

    private static void Escape(StringBuilder sb, ReadOnlySpan<char> s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '&')
            {
                sb.Append("&amp;");
            }
            else if (c == '<')
            {
                sb.Append("&lt;");
            }
            else if (c == '>')
            {
                sb.Append("&gt;");
            }
            else if (c is < ' ' || c is '\uFFFE' or '\uFFFF' || (char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(s[i - 1])))
                || (char.IsHighSurrogate(c) && (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))))
            {
                throw new ArgumentException("The text has a character that XML cannot carry (U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + ").", nameof(s));
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    /// <inheritdoc/>
    public bool Equals(ContentControl? other) => other is not null && Markup == other.Markup;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentControl);

    /// <inheritdoc/>
    public override int GetHashCode() => Markup.GetHashCode();
}

/// <summary>Iterative document-order walks over the markup tree, so a hostile deep tree cannot overflow the stack.</summary>
internal static class DocumentOrder
{
    /// <summary>Enumerates the descendant elements of <paramref name="root"/> in document order; when <paramref name="stopAt"/> matches an element it is returned but not entered.</summary>
    public static IEnumerable<MarkupNode> Elements(MarkupNode root, Func<MarkupNode, bool>? stopAt)
    {
        if (root.IsNull)
        {
            yield break;
        }

        MarkupNode n = root.FirstChild;
        while (!n.IsNull)
        {
            if (n.Kind == MarkupNodeKind.Element)
            {
                yield return n;
                if (!n.FirstChild.IsNull && (stopAt is null || !stopAt(n)))
                {
                    n = n.FirstChild;
                    continue;
                }
            }

            while (n.NextSibling.IsNull)
            {
                n = n.Parent;
                if (n.IsNull || n == root)
                {
                    yield break;
                }
            }

            n = n.NextSibling;
        }
    }
}
