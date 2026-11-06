using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Odf;

/// <summary>
/// The XML namespaces of OpenDocument (OpenDocument 1.3 Part 1 §3.1.2 lists the prefixes the specification uses; documents may choose
/// others, so code always matches on the namespace name, never on the prefix).
/// </summary>
public static class OdfNamespaces
{
    /// <summary><c>office:</c>, the document and body elements.</summary>
    public const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    /// <summary><c>style:</c>, styles, page layouts and master pages.</summary>
    public const string Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";

    /// <summary><c>text:</c>, paragraphs, lists, fields and notes.</summary>
    public const string Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    /// <summary><c>table:</c>, tables and spreadsheet content.</summary>
    public const string Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

    /// <summary><c>draw:</c>, frames, shapes and images.</summary>
    public const string Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    /// <summary><c>fo:</c>, the XSL-FO property names ODF reuses.</summary>
    public const string Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    /// <summary><c>svg:</c>, the SVG-compatible length and font properties ODF reuses.</summary>
    public const string Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    /// <summary><c>xlink:</c>, links.</summary>
    public const string XLink = "http://www.w3.org/1999/xlink";

    /// <summary><c>dc:</c>, Dublin Core elements of the metadata.</summary>
    public const string DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary><c>meta:</c>, the metadata elements.</summary>
    public const string Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";

    /// <summary><c>number:</c>, data styles (number, date and time formats).</summary>
    public const string Number = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";

    /// <summary><c>manifest:</c>, the package manifest.</summary>
    public const string Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    /// <summary><c>config:</c>, the settings part.</summary>
    public const string Config = "urn:oasis:names:tc:opendocument:xmlns:config:1.0";

    /// <summary><c>form:</c>, form controls.</summary>
    public const string Form = "urn:oasis:names:tc:opendocument:xmlns:form:1.0";

    /// <summary><c>chart:</c>, charts.</summary>
    public const string Chart = "urn:oasis:names:tc:opendocument:xmlns:chart:1.0";

    /// <summary><c>presentation:</c>, presentation-only elements.</summary>
    public const string Presentation = "urn:oasis:names:tc:opendocument:xmlns:presentation:1.0";

    /// <summary><c>script:</c>, scripts.</summary>
    public const string Script = "urn:oasis:names:tc:opendocument:xmlns:script:1.0";

    /// <summary><c>loext:</c>, a producer's extension namespace; kept and never interpreted.</summary>
    public const string LibreOfficeExtension = "urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0";

    /// <summary><c>math:</c>, embedded MathML.</summary>
    public const string MathMl = "http://www.w3.org/1998/Math/MathML";

    /// <summary>The metadata attribute namespace of OpenDocument 1.2 and later (<c>xhtml:</c>).</summary>
    public const string XHtml = "http://www.w3.org/1999/xhtml";
}

/// <summary>Small helpers for reading ODF markup by namespace and local name.</summary>
public static class OdfMarkup
{
    /// <summary>Gets the first child element with the given namespace and local name, or a null node.</summary>
    /// <param name="parent">The element to look in.</param>
    /// <param name="namespaceUri">The namespace name.</param>
    /// <param name="localName">The local name.</param>
    /// <returns>The child, or <c>default</c> when there is none (<see cref="MarkupNode.IsNull"/> is true).</returns>
    public static MarkupNode Child(MarkupNode parent, string namespaceUri, string localName)
    {
        if (parent.IsNull)
        {
            return default;
        }

        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.IsElement(namespaceUri, localName))
            {
                return c;
            }
        }

        return default;
    }

    /// <summary>Gets an attribute by namespace and local name, or null when absent.</summary>
    /// <param name="element">The element.</param>
    /// <param name="namespaceUri">The attribute's namespace name.</param>
    /// <param name="localName">The local name.</param>
    /// <returns>The decoded value, or null.</returns>
    public static string? Attribute(MarkupNode element, string namespaceUri, string localName) =>
        element.IsNull ? null : element.GetAttribute(namespaceUri, localName);
}
