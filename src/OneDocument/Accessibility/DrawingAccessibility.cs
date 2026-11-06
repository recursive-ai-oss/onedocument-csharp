using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Accessibility;

/// <summary>
/// Alternative text on DrawingML objects. Word (<c>wp:docPr</c>), PowerPoint (<c>p:cNvPr</c>) and Excel (<c>xdr:cNvPr</c>) store it the
/// same way: a <c>descr</c> attribute, and a decorative marker in an extension. This is the shared check.
/// </summary>
public static class DrawingAccessibility
{
    /// <summary>The URI of the extension that carries the decorative marker ([MS-ODRAWXML] §2.3.1, <c>adec:decorative</c>).</summary>
    public const string DecorativeExtensionUri = "{C183D7F6-B498-43B3-948B-1728B52AA6E4}";

    /// <summary>The namespace of <c>adec:decorative</c>.</summary>
    public const string DecorativeNamespace = "http://schemas.microsoft.com/office/drawing/2017/decorative";

    /// <summary>Tells whether the object is marked decorative.</summary>
    /// <param name="nonVisualProperties">The <c>cNvPr</c> or <c>docPr</c> element.</param>
    /// <returns>True when its extension list holds <c>adec:decorative</c> with <c>val</c> true.</returns>
    public static bool IsDecorative(MarkupNode nonVisualProperties)
    {
        foreach (MarkupNode ext in Descendants(nonVisualProperties, 0))
        {
            if (ext.LocalNameString == "decorative" && ext.GetAttribute("val") is "1" or "true")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the alternative text: the <c>descr</c> attribute, else the <c>title</c> attribute; null when both are empty.</summary>
    /// <param name="nonVisualProperties">The <c>cNvPr</c> or <c>docPr</c> element.</param>
    /// <returns>The text, or null.</returns>
    public static string? AltText(MarkupNode nonVisualProperties)
    {
        string? d = nonVisualProperties.GetAttribute("descr");
        if (!string.IsNullOrWhiteSpace(d))
        {
            return d;
        }

        string? t = nonVisualProperties.GetAttribute("title");
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    /// <summary>
    /// Checks one object's alternative text and adds the findings: missing (unless decorative), a file name, or an automatic description.
    /// </summary>
    /// <param name="collector">Where findings go.</param>
    /// <param name="nonVisualProperties">The <c>cNvPr</c> or <c>docPr</c> element.</param>
    /// <param name="location">Where the object is.</param>
    /// <param name="what">What the object is, in words (“picture”, “chart”).</param>
    /// <param name="canBeDecorative">Whether the object can be marked decorative (a fixer is offered for it).</param>
    public static void Check(AccessibilityCollector collector, MarkupNode nonVisualProperties, AccessibilityLocation location, string what, bool canBeDecorative = true)
    {
        ArgumentNullException.ThrowIfNull(collector);
        if (nonVisualProperties.IsNull || IsDecorative(nonVisualProperties))
        {
            return;
        }

        CheckAlt(collector, nonVisualProperties.GetAttribute("name"), AltText(nonVisualProperties), location, what,
            canBeDecorative ? AccessibilityFixKind.MarkDecorative : AccessibilityFixKind.None, nonVisualProperties);
    }

    /// <summary>Checks alternative text read from anywhere (for example the <c>alt</c> attribute of a legacy VML shape) and adds the findings.</summary>
    /// <param name="collector">Where findings go.</param>
    /// <param name="name">The object's name, or null.</param>
    /// <param name="alt">The alternative text, or null when there is none.</param>
    /// <param name="location">Where the object is.</param>
    /// <param name="what">What the object is, in words.</param>
    /// <param name="fix">The fixer offered for a missing text.</param>
    /// <param name="target">The element the fixer works on.</param>
    public static void CheckAlt(AccessibilityCollector collector, string? name, string? alt, AccessibilityLocation location, string what, AccessibilityFixKind fix, MarkupNode target)
    {
        ArgumentNullException.ThrowIfNull(collector);
        if (string.IsNullOrWhiteSpace(alt))
        {
            collector.Add(new AccessibilityIssue(AccessibilityRules.AltTextMissing, location, name,
                "The " + what + (string.IsNullOrEmpty(name) ? "" : " “" + name + "”") + " has no alternative text.",
                "Describe what the " + what + " shows or says, or mark it as decorative if it adds no information.", fix, target));
        }
        else if (AccessibilityText.LooksLikeFileName(alt))
        {
            collector.Add(new AccessibilityIssue(AccessibilityRules.AltTextFileName, location, name,
                "The alternative text of the " + what + " is a file name (“" + alt + "”).",
                "Replace the file name with a short description of what the " + what + " shows.", AccessibilityFixKind.None, target));
        }
        else if (AccessibilityText.IsGeneratedAltText(alt))
        {
            collector.Add(new AccessibilityIssue(AccessibilityRules.AltTextSuggested, location, name,
                "The alternative text of the " + what + " was generated automatically.",
                "Check that the description is accurate, then remove the “" + AccessibilityText.GeneratedAltTextMarker + "” note.", AccessibilityFixKind.None, target));
        }
    }

    /// <summary>Marks the object decorative: adds the extension (when there is none) and clears the alternative text.</summary>
    /// <param name="nonVisualProperties">The <c>cNvPr</c> or <c>docPr</c> element.</param>
    /// <returns>True when the markup was edited; false when it was decorative already.</returns>
    public static bool MarkDecorative(MarkupNode nonVisualProperties)
    {
        if (nonVisualProperties.IsNull || IsDecorative(nonVisualProperties))
        {
            return false;
        }

        // [MS-ODRAWXML] §2.3.1: the marker is a:ext (uri above) holding adec:decorative val="1" in the object's extension list.
        string aNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        string ext = "<a:ext xmlns:a=\"" + aNs + "\" uri=\"" + DecorativeExtensionUri + "\"><adec:decorative xmlns:adec=\"" + DecorativeNamespace + "\" val=\"1\"/></a:ext>";
        MarkupNode list = default;
        foreach (MarkupNode c in nonVisualProperties.ChildElements)
        {
            if (c.LocalNameString == "extLst")
            {
                list = c;
            }
        }

        if (list.IsNull)
        {
            nonVisualProperties.AppendXml(System.Text.Encoding.UTF8.GetBytes("<a:extLst xmlns:a=\"" + aNs + "\">" + ext + "</a:extLst>"));
        }
        else
        {
            list.AppendXml(System.Text.Encoding.UTF8.GetBytes(ext));
        }

        nonVisualProperties.RemoveAttribute("descr");
        return true;
    }

    private static IEnumerable<MarkupNode> Descendants(MarkupNode node, int depth)
    {
        if (depth > 8)
        {
            yield break;
        }

        foreach (MarkupNode c in node.ChildElements)
        {
            yield return c;
            foreach (MarkupNode d in Descendants(c, depth + 1))
            {
                yield return d;
            }
        }
    }
}
