using System.Text;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Markup;

/// <summary>One attribute of an element, as spans over the document's buffer. It is valid until the document is edited.</summary>
public readonly ref struct MarkupAttributeRef
{
    private readonly MarkupNode _element;
    private readonly XmlAttributeToken _token;

    internal MarkupAttributeRef(MarkupNode element, XmlAttributeToken token)
    {
        _element = element;
        _token = token;
    }

    /// <summary>Gets the name as written, prefix included.</summary>
    public ReadOnlySpan<byte> RawName => _token.RawName;

    /// <summary>Gets the prefix as written; empty when the name has none.</summary>
    public ReadOnlySpan<byte> Prefix => _token.Prefix;

    /// <summary>Gets the local part of the name.</summary>
    public ReadOnlySpan<byte> LocalName => _token.LocalName;

    /// <summary>Gets the value between the quotes exactly as written: references are not decoded.</summary>
    public ReadOnlySpan<byte> RawValue => _token.RawValue;

    /// <summary>Gets the quote character that delimits the value: <c>"</c> or <c>'</c>.</summary>
    public byte Quote => _token.Quote;

    /// <summary>Gets the value, references decoded and white space normalised as XML requires of attribute values (allocates).</summary>
    public string Value => XmlText.GetString(_token.RawValue, isAttributeValue: true);

    /// <summary>Gets a value that says whether this is a namespace declaration, <c>xmlns</c> or <c>xmlns:prefix</c>.</summary>
    public bool IsNamespaceDeclaration => _token.IsNamespaceDeclaration;

    /// <summary>Gets the namespace URI of the attribute (allocates): empty for unprefixed attributes and namespace declarations.</summary>
    public string NamespaceUri
    {
        get
        {
            if (_token.Prefix.Length == 0 || IsNamespaceDeclaration)
            {
                return string.Empty;
            }

            return _element.Doc!.TryResolvePrefix(_element.Index, _token.Prefix, out ReadOnlySpan<byte> raw) ? XmlText.GetString(raw, true) : string.Empty;
        }
    }

    /// <summary>Tells whether the attribute is in the given namespace, without allocating.</summary>
    /// <param name="namespaceUri">The namespace URI in UTF-8.</param>
    /// <returns>True when the attribute's prefix is bound to that namespace. Unprefixed attributes are in no namespace.</returns>
    public bool IsInNamespace(ReadOnlySpan<byte> namespaceUri)
    {
        if (_token.Prefix.Length == 0 || IsNamespaceDeclaration)
        {
            return namespaceUri.Length == 0;
        }

        return _element.Doc!.TryResolvePrefix(_element.Index, _token.Prefix, out ReadOnlySpan<byte> raw) && MarkupDocument.UriEquals(raw, namespaceUri);
    }
}

/// <summary>The attributes of an element, for <c>foreach</c>. It allocates nothing.</summary>
public readonly ref struct MarkupAttributes
{
    private readonly MarkupNode _element;

    internal MarkupAttributes(MarkupNode element) => _element = element;

    /// <summary>Gets an enumerator over the attributes in document order.</summary>
    /// <returns>An enumerator; empty when the node is not an element.</returns>
    public MarkupAttributeEnumerator GetEnumerator() => new(_element);
}

/// <summary>Enumerates the attributes of an element.</summary>
public ref struct MarkupAttributeEnumerator
{
    private readonly MarkupNode _element;
    private XmlAttributeEnumerator _inner;

    internal MarkupAttributeEnumerator(MarkupNode element)
    {
        _element = element;
        _inner = element.Kind == MarkupNodeKind.Element ? element.Doc!.AttributesOf(element.Index, out _) : default;
    }

    /// <summary>Gets the current attribute.</summary>
    public readonly MarkupAttributeRef Current => new(_element, _inner.Current);

    /// <summary>Moves to the next attribute.</summary>
    /// <returns>False after the last one.</returns>
    public bool MoveNext() => _inner.MoveNext();
}
