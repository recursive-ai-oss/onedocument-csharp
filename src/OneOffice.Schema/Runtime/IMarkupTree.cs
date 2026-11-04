namespace RecursiveAI.OneOffice.Schema;

/// <summary>
/// What a typed view needs from a markup tree. The lazy markup layer implements this once; views never see its other details.
/// </summary>
/// <remarks>
/// <para>Nodes are <see cref="int"/> handles that stay valid while the node is in the tree, including across edits to other
/// nodes. Handle 0 is "no node". Only element nodes are exposed: text, comments and processing instructions are the tree's
/// business and are kept as they are.</para>
/// <para>Names and attribute values are UTF-8 spans straight from the part buffer, so reading allocates nothing. Attribute
/// values are returned raw, with entity references still in place; the view layer decodes them when a string is asked for.
/// Namespaces are given as URIs, never prefixes: prefixes are the tree's to preserve.</para>
/// <para>An implementation may throw nothing for a stale handle; callers do not pass one.</para>
/// </remarks>
public interface IMarkupTree
{
    /// <summary>The first child element of <paramref name="parent"/>, or 0.</summary>
    int FirstChildElement(int parent);

    /// <summary>The next sibling element after <paramref name="element"/>, or 0.</summary>
    int NextSiblingElement(int element);

    /// <summary>The namespace URI of the element, empty when it has none.</summary>
    ReadOnlySpan<byte> NamespaceUri(int element);

    /// <summary>The local name of the element.</summary>
    ReadOnlySpan<byte> LocalName(int element);

    /// <summary>Finds an attribute by namespace URI (empty for none) and local name; the value is raw, entities not decoded.</summary>
    bool TryGetAttribute(int element, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName, out ReadOnlySpan<byte> rawValue);

    /// <summary>
    /// Sets an attribute, replacing the value in place when it exists so its position and prefix stay. A new attribute uses the
    /// prefix bound to the namespace in scope, else <paramref name="preferredPrefix"/> (declaring it). The tree escapes the value.
    /// </summary>
    void SetAttribute(int element, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName, ReadOnlySpan<byte> preferredPrefix, string value);

    /// <summary>Removes an attribute; false when it was not there.</summary>
    bool RemoveAttribute(int element, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName);

    /// <summary>The number of attributes on the element, namespace declarations excluded.</summary>
    int AttributeCount(int element);

    /// <summary>The namespace URI and local name of the attribute at <paramref name="index"/>, in document order.</summary>
    void GetAttributeName(int element, int index, out ReadOnlySpan<byte> namespaceUri, out ReadOnlySpan<byte> localName);

    /// <summary>
    /// Inserts a new empty element as a child of <paramref name="parent"/> before the child element <paramref name="before"/>
    /// (0 appends) and returns its handle. The prefix follows the same rule as <see cref="SetAttribute"/>.
    /// </summary>
    int InsertElement(int parent, int before, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName, ReadOnlySpan<byte> preferredPrefix);

    /// <summary>Removes the element with all it contains.</summary>
    void RemoveElement(int element);

    /// <summary>The element's text: its character data children concatenated, entities decoded.</summary>
    string GetText(int element);

    /// <summary>Replaces the element's character data with <paramref name="text"/>, leaving child elements where they are.</summary>
    void SetText(int element, string text);
}
