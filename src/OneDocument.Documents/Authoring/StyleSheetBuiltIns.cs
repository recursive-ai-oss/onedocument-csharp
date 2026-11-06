using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using W = RecursiveAI.OneDocument.Schema.Wordprocessing;

namespace RecursiveAI.OneDocument.Documents.Styles;

public sealed partial class StyleSheet
{
    /// <summary>
    /// Gets a style by its display name, ignoring case ("Heading 1" finds the built-in style Word stores as "heading 1").
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <exception cref="StyleNotFoundException">The document has no style of that name (<c>docx.style-not-found</c>).</exception>
    public StyleDefinition this[string name] =>
        FindByName(name ?? throw new ArgumentNullException(nameof(name)))
        ?? throw new StyleNotFoundException(name, StyleType.Paragraph, "the document defines no style with that name");

    /// <summary>
    /// Finds a style of a type by display name, ignoring case; throws instead of returning null.
    /// </summary>
    internal StyleDefinition Require(string name, StyleType type)
    {
        ArgumentNullException.ThrowIfNull(name);
        StyleDefinition? found = FindByName(name);
        if (found is null)
        {
            throw new StyleNotFoundException(name, type, "the document defines no style with that name");
        }

        if (found.Type != type)
        {
            throw new StyleNotFoundException(name, type, "that name belongs to a " + found.Type.ToString().ToLowerInvariant() + " style");
        }

        return found;
    }

    /// <summary>
    /// The name Word shows for a style: built-in styles are stored in lower case ("heading 1") and shown capitalised ("Heading 1").
    /// </summary>
    internal static string DisplayName(StyleDefinition style)
    {
        string? name = style.Name;
        if (string.IsNullOrEmpty(name))
        {
            return style.Id;
        }

        return !style.IsCustom && char.IsLower(name[0]) ? char.ToUpperInvariant(name[0]) + name[1..] : name;
    }

    /// <summary>
    /// Adds a built-in style when the document does not have it yet, and returns it. A document made by <see cref="Document.Create"/>
    /// has only <c>Normal</c>; this gives it the style a user expects under a well-known name.
    /// </summary>
    /// <param name="name">One of "Title", "Heading 1", "Heading 2", "Heading 3", "Caption", "List Paragraph", "Hyperlink" or "Table Grid" (ignoring case).</param>
    /// <returns>The existing style of that name, or the new one.</returns>
    /// <exception cref="ArgumentException">The name is not one of those above.</exception>
    public StyleDefinition AddBuiltIn(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (FindByName(name) is { } existing)
        {
            return existing;
        }

        // ECMA-376 Part 1 §17.7.4.17 (style): the element order is name, basedOn, next, uiPriority, qFormat, then pPr and rPr. The
        // typed view places the first ones; the property elements are appended after them.
        string ns = _owner.WordprocessingNamespaceUri;
        string w = "xmlns:w=\"" + ns + "\"";
        string normal = Find("Normal") is not null ? "Normal" : string.Empty;
        StyleDefinition style;
        switch (name.ToLowerInvariant())
        {
            case "title":
                style = Create("Title", "Title", StyleType.Paragraph, normal, normal, 10, true);
                Append(style, "<w:pPr " + w + "><w:spacing w:after=\"80\" w:line=\"240\" w:lineRule=\"auto\"/><w:contextualSpacing/></w:pPr>");
                Append(style, "<w:rPr " + w + "><w:spacing w:val=\"-10\"/><w:kern w:val=\"28\"/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/></w:rPr>");
                break;
            case "heading 1":
                style = Heading(1, 32, 240, 80);
                break;
            case "heading 2":
                style = Heading(2, 26, 160, 80);
                break;
            case "heading 3":
                style = Heading(3, 24, 160, 80);
                break;
            case "caption":
                style = Create("Caption", "caption", StyleType.Paragraph, normal, normal, 35, true);
                Append(style, "<w:pPr " + w + "><w:spacing w:after=\"200\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>");
                Append(style, "<w:rPr " + w + "><w:i/><w:iCs/><w:color w:val=\"44546A\"/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/></w:rPr>");
                break;
            case "list paragraph":
                style = Create("ListParagraph", "List Paragraph", StyleType.Paragraph, normal, string.Empty, 34, true);
                Append(style, "<w:pPr " + w + "><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr>");
                break;
            case "hyperlink":
                style = Create("Hyperlink", "Hyperlink", StyleType.Character, Find("DefaultParagraphFont") is not null ? "DefaultParagraphFont" : string.Empty, string.Empty, 99, false);
                Append(style, "<w:rPr " + w + "><w:color w:val=\"0563C1\"/><w:u w:val=\"single\"/></w:rPr>");
                break;
            case "table grid":
                style = Create("TableGrid", "Table Grid", StyleType.Table, Find("TableNormal") is not null ? "TableNormal" : string.Empty, string.Empty, 39, false);
                Append(style, "<w:pPr " + w + "><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>");
                Append(style, "<w:tblPr " + w + "><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/>" +
                    "<w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/>" +
                    "<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/></w:tblBorders></w:tblPr>");
                break;
            default:
                throw new ArgumentException("'" + name + "' is not a built-in style this method knows (Title, Heading 1-3, Caption, List Paragraph, Hyperlink, Table Grid).", nameof(name));
        }

        Invalidate();
        return Find(style.Id)!;

        StyleDefinition Heading(int level, int size, int before, int after)
        {
            string id = "Heading" + level;
            StyleDefinition s = Create(id, "heading " + level, StyleType.Paragraph, normal, normal, 9, true);
            Append(s, "<w:pPr " + w + "><w:keepNext/><w:keepLines/><w:spacing w:before=\"" + before + "\" w:after=\"" + after + "\"/><w:outlineLvl w:val=\"" + (level - 1) + "\"/></w:pPr>");
            Append(s, "<w:rPr " + w + "><w:b/><w:color w:val=\"2F5496\"/><w:sz w:val=\"" + size + "\"/><w:szCs w:val=\"" + size + "\"/></w:rPr>");
            return s;
        }
    }

    private StyleDefinition Create(string id, string name, StyleType type, string basedOn, string next, int priority, bool quick)
    {
        StyleDefinition style = AddStyle(id, name, type, basedOn.Length > 0 ? basedOn : null);
        W.Style view = style.View;
        if (next.Length > 0)
        {
            view.GetOrAddNext().Val = next;
        }

        view.GetOrAddUiPriority().Val = priority;
        if (quick)
        {
            view.GetOrAddQFormat();
        }

        return style;
    }

    private static void Append(StyleDefinition style, string xml) => style.Markup.Node.AppendXml(Encoding.UTF8.GetBytes(xml));
}

