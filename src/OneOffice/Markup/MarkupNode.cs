using System.Buffers;
using System.Collections;
using System.Text;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Markup;

/// <summary>
/// A handle to one node of a <see cref="MarkupDocument"/>: the document, an element, text, a comment and so on. It is a small
/// value (the document and an index); copying it costs nothing and it holds no data. Names, attributes and text are read from the
/// part's buffer when asked for.
/// </summary>
/// <remarks>
/// A node that has been removed stays readable but is no longer reachable from the document. Spans returned by members
/// (<see cref="RawName"/>, <see cref="RawContent"/>) point into the document's buffers and must not be used after an edit.
/// </remarks>
public readonly struct MarkupNode : IEquatable<MarkupNode>
{
    private readonly MarkupDocument? _doc;
    private readonly int _index;

    internal MarkupNode(MarkupDocument doc, int index)
    {
        _doc = doc;
        _index = index;
    }

    /// <summary>Gets a value that says whether this is the default value, which refers to no node.</summary>
    public bool IsNull => _doc is null;

    /// <summary>Gets the document the node belongs to, or null for the default value.</summary>
    public MarkupDocument? Document => _doc;

    /// <summary>Gets the kind of node, or <see cref="MarkupNodeKind.None"/> for the default value.</summary>
    public MarkupNodeKind Kind => _doc is null ? MarkupNodeKind.None : _doc.KindOf(_index);

    /// <summary>Gets the parent, or the default value for the document node and for a removed node.</summary>
    public MarkupNode Parent => _doc is null || _index == 0 || _doc.ParentOf(_index) < 0 ? default : new MarkupNode(_doc, _doc.ParentOf(_index));

    /// <summary>Gets the first child, or the default value when there is none.</summary>
    public MarkupNode FirstChild => Make(_doc?.FirstChildOf(_index) ?? 0);

    /// <summary>Gets the last child, or the default value when there is none.</summary>
    public MarkupNode LastChild => Make(_doc?.LastChildOf(_index) ?? 0);

    /// <summary>Gets the next sibling, or the default value for the last child.</summary>
    public MarkupNode NextSibling => Make(_doc?.NextOf(_index) ?? 0);

    /// <summary>Gets the previous sibling, or the default value for the first child.</summary>
    public MarkupNode PreviousSibling => Make(_doc?.PrevOf(_index) ?? 0);

    /// <summary>Gets the child nodes of all kinds, in document order. Empty for nodes that cannot have children.</summary>
    public MarkupChildren Children => new(this, elementsOnly: false);

    /// <summary>Gets the child elements, in document order.</summary>
    public MarkupChildren ChildElements => new(this, elementsOnly: true);

    /// <summary>Gets the offset of the node's first byte in the document's original bytes, or -1 when the node's bytes are new (inserted or rewritten by an edit) or the node is null.</summary>
    public int SourceStart => _doc is null ? -1 : _doc.SourceStart(_index);

    /// <summary>Gets the offset after the node's last byte in the original bytes (for an element, after its end tag), or -1 when any part of it is new.</summary>
    public int SourceEnd => _doc is null ? -1 : _doc.SourceEnd(_index);

    /// <summary>Gets the element name exactly as written, prefix included, such as <c>w:p</c>. Empty for other kinds.</summary>
    public ReadOnlySpan<byte> RawName
    {
        get
        {
            if (Kind != MarkupNodeKind.Element)
            {
                return default;
            }

            _doc!.ElementName(_index, out ReadOnlySpan<byte> raw, out _, out _);
            return raw;
        }
    }

    /// <summary>Gets the element's prefix as written; empty when it has none or for other kinds.</summary>
    public ReadOnlySpan<byte> Prefix
    {
        get
        {
            if (Kind != MarkupNodeKind.Element)
            {
                return default;
            }

            _doc!.ElementName(_index, out _, out ReadOnlySpan<byte> prefix, out _);
            return prefix;
        }
    }

    /// <summary>Gets the element's local name; empty for other kinds.</summary>
    public ReadOnlySpan<byte> LocalName
    {
        get
        {
            if (Kind != MarkupNodeKind.Element)
            {
                return default;
            }

            _doc!.ElementName(_index, out _, out _, out ReadOnlySpan<byte> local);
            return local;
        }
    }

    /// <summary>Gets the element name as written, as a string (allocates). Empty for other kinds.</summary>
    public string Name => Encoding.UTF8.GetString(RawName);

    /// <summary>Gets the element's local name as a string (allocates). Empty for other kinds.</summary>
    public string LocalNameString => Encoding.UTF8.GetString(LocalName);

    /// <summary>Gets the element's namespace URI (allocates), or an empty string when the element is in no namespace. Found by walking up the declarations in scope.</summary>
    public string NamespaceUri
    {
        get
        {
            if (Kind != MarkupNodeKind.Element)
            {
                return string.Empty;
            }

            return _doc!.TryResolvePrefix(_index, Prefix, out ReadOnlySpan<byte> raw) ? XmlText.GetString(raw, true) : string.Empty;
        }
    }

    /// <summary>
    /// Gets the element's namespace URI as the bytes written in the declaration, without allocating. Empty when the element is in no
    /// namespace. Entity references in the declaration are not decoded (use <see cref="NamespaceUri"/> for that; Office parts never have them).
    /// Valid until the document is edited.
    /// </summary>
    public ReadOnlySpan<byte> NamespaceUriBytes
    {
        get
        {
            if (Kind != MarkupNodeKind.Element)
            {
                return default;
            }

            return _doc!.TryResolvePrefix(_index, Prefix, out ReadOnlySpan<byte> raw) ? raw : default;
        }
    }

    /// <summary>Finds the prefix bound to a namespace in the scope of this node (the nearest element at or above it), without changing anything.</summary>
    /// <param name="namespaceUri">The namespace URI.</param>
    /// <param name="prefix">The prefix as declared; <c>xml</c> for the XML namespace. Never empty: a default-namespace declaration does not count, because attributes cannot use it.</param>
    /// <returns>True when a prefix is bound to the namespace here and not rebound closer.</returns>
    public bool TryGetPrefixInScope(string namespaceUri, out string prefix)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        if (_doc is null)
        {
            prefix = string.Empty;
            return false;
        }

        return _doc.TryFindPrefix(_index, namespaceUri, out prefix);
    }

    /// <summary>Tells whether this is an element with the given namespace and local name, without allocating.</summary>
    /// <param name="namespaceUri">The namespace URI in UTF-8; empty for no namespace.</param>
    /// <param name="localName">The local name in UTF-8.</param>
    /// <returns>True when the element matches.</returns>
    public bool IsElement(ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName)
    {
        if (Kind != MarkupNodeKind.Element)
        {
            return false;
        }

        _doc!.ElementName(_index, out _, out ReadOnlySpan<byte> prefix, out ReadOnlySpan<byte> local);
        if (!local.SequenceEqual(localName))
        {
            return false;
        }

        if (_doc.TryResolvePrefix(_index, prefix, out ReadOnlySpan<byte> raw))
        {
            return MarkupDocument.UriEquals(raw, namespaceUri);
        }

        return namespaceUri.Length == 0;
    }

    /// <summary>Tells whether this is an element with the given namespace and local name.</summary>
    /// <param name="namespaceUri">The namespace URI; empty for no namespace.</param>
    /// <param name="localName">The local name.</param>
    /// <returns>True when the element matches.</returns>
    public bool IsElement(string namespaceUri, string localName)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        ArgumentNullException.ThrowIfNull(localName);
        return IsElement(Encoding.UTF8.GetBytes(namespaceUri), Encoding.UTF8.GetBytes(localName));
    }

    /// <summary>Gets a value that says whether the element was written as <c>&lt;a/&gt;</c> (and has no content).</summary>
    public bool IsSelfClosing => Kind == MarkupNodeKind.Element && _doc!.IsSelfClosing(_index);

    /// <summary>Gets the attributes of an element in document order, namespace declarations included. Empty for other kinds.</summary>
    public MarkupAttributes Attributes => new(this);

    /// <summary>
    /// Gets the value of an attribute in no namespace (an unprefixed attribute), decoded.
    /// </summary>
    /// <param name="localName">The attribute's name.</param>
    /// <returns>The value, or null when the element has no such attribute.</returns>
    public string? GetAttribute(string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        byte[] name = Encoding.UTF8.GetBytes(localName);
        foreach (MarkupAttributeRef a in Attributes)
        {
            if (a.Prefix.Length == 0 && a.RawName.SequenceEqual(name))
            {
                return a.Value;
            }
        }

        return null;
    }

    /// <summary>Gets the value of a namespaced attribute, decoded.</summary>
    /// <param name="namespaceUri">The attribute's namespace URI.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <returns>The value, or null when the element has no such attribute.</returns>
    public string? GetAttribute(string namespaceUri, string localName)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        ArgumentNullException.ThrowIfNull(localName);
        byte[] ns = Encoding.UTF8.GetBytes(namespaceUri);
        byte[] local = Encoding.UTF8.GetBytes(localName);
        foreach (MarkupAttributeRef a in Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && a.LocalName.SequenceEqual(local) && a.IsInNamespace(ns))
            {
                return a.Value;
            }
        }

        return null;
    }

    /// <summary>Gets the raw bytes of a text-like node: the text with its references, the inside of a CDATA section or comment, the data of a processing instruction. Empty for elements and the document.</summary>
    public ReadOnlySpan<byte> RawContent => _doc is null || Kind is MarkupNodeKind.Element or MarkupNodeKind.Document ? default : _doc.Content(_index);

    /// <summary>
    /// Gets the text: for an element or the document, all text and CDATA below it joined (comments and processing instructions
    /// left out); for text nodes the decoded text; for comments and processing instructions their content. References are
    /// decoded and line ends normalised to line feeds.
    /// </summary>
    public string InnerText => _doc is null ? string.Empty : _doc.InnerText(_index);

    /// <summary>Writes this node and everything below it, as bytes: the original bytes where untouched.</summary>
    /// <param name="destination">Where the UTF-8 bytes go.</param>
    public void WriteTo(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        RequireNode();
        _doc!.Emit(_index, destination);
    }

    /// <summary>Gets this node and everything below it as XML text (allocates).</summary>
    public string OuterXml
    {
        get
        {
            if (_doc is null)
            {
                return string.Empty;
            }

            var w = new ArrayBufferWriter<byte>();
            _doc.Emit(_index, w);
            return Encoding.UTF8.GetString(w.WrittenSpan);
        }
    }

    /// <summary>Sets an attribute with a name as written, such as <c>w:val</c> or <c>id</c>. An existing attribute keeps its place, quote style and spacing and changes only its value; a new one is added after the last attribute.</summary>
    /// <param name="rawName">The qualified name. A prefix must be bound at this element (<c>xmlns</c> declarations are always allowed).</param>
    /// <param name="value">The value; it is escaped.</param>
    /// <exception cref="InvalidOperationException">The node is not an element.</exception>
    /// <exception cref="ArgumentException">The name is invalid or its prefix unbound, or the value has a character XML cannot carry.</exception>
    public void SetAttribute(string rawName, string value)
    {
        ArgumentNullException.ThrowIfNull(rawName);
        ArgumentNullException.ThrowIfNull(value);
        RequireNode();
        _doc!.SetAttribute(_index, rawName, value);
    }

    /// <summary>Sets a namespaced attribute. The prefix is found among the declarations in scope; when none binds the namespace, a declaration <c>xmlns:ns1</c> (or the next free number) is added to this element.</summary>
    /// <param name="namespaceUri">The attribute's namespace URI (not empty).</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The value; it is escaped.</param>
    /// <exception cref="InvalidOperationException">The node is not an element.</exception>
    /// <exception cref="ArgumentException">The name is invalid, or the value has a character XML cannot carry.</exception>
    public void SetAttribute(string namespaceUri, string localName, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(namespaceUri);
        ArgumentNullException.ThrowIfNull(localName);
        ArgumentNullException.ThrowIfNull(value);
        RequireNode();
        _doc!.SetAttribute(_index, namespaceUri, localName, value);
    }

    /// <summary>Sets a namespaced attribute, reusing a prefix in scope for the namespace, else declaring <paramref name="preferredPrefix"/> on this element.</summary>
    /// <param name="namespaceUri">The attribute's namespace URI (not empty).</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The value; it is escaped.</param>
    /// <param name="preferredPrefix">The prefix to declare when none is in scope. When it is already bound to another namespace here, <c>ns1</c> (or the next free number) is used instead.</param>
    /// <exception cref="InvalidOperationException">The node is not an element.</exception>
    /// <exception cref="ArgumentException">The name is invalid, or the value has a character XML cannot carry.</exception>
    public void SetAttribute(string namespaceUri, string localName, string value, string preferredPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(namespaceUri);
        ArgumentNullException.ThrowIfNull(localName);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(preferredPrefix);
        RequireNode();
        _doc!.SetAttribute(_index, namespaceUri, localName, value, preferredPrefix);
    }

    /// <summary>Removes an attribute, with the white space before it.</summary>
    /// <param name="rawName">The attribute's name as written.</param>
    /// <returns>True when the attribute existed.</returns>
    /// <exception cref="InvalidOperationException">The node is not an element.</exception>
    public bool RemoveAttribute(string rawName)
    {
        ArgumentNullException.ThrowIfNull(rawName);
        RequireNode();
        return _doc!.RemoveAttribute(_index, rawName);
    }

    /// <summary>Replaces the content of an element with one text node, or sets the text of a text node.</summary>
    /// <param name="text">The text; it is escaped. Empty clears an element's content.</param>
    /// <exception cref="InvalidOperationException">The node is a comment, processing instruction, declaration or the document.</exception>
    /// <exception cref="ArgumentException">The text has a character XML cannot carry.</exception>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        RequireNode();
        _doc!.SetText(_index, text);
    }

    /// <summary>Parses <paramref name="xml"/> as content and adds it after the last child. The text may hold several nodes. A self-closing element gains an end tag.</summary>
    /// <param name="xml">UTF-8 XML content. Prefixes it uses must be declared in it or in scope here.</param>
    /// <exception cref="OfficeException">The XML is not well-formed (<see cref="Diagnostics.DefectKind.Markup"/>), or nests too deeply.</exception>
    /// <exception cref="ArgumentException">A prefix is unbound here, or the node is the document and the XML has elements or text.</exception>
    /// <exception cref="InvalidOperationException">The node cannot have children.</exception>
    public void AppendXml(ReadOnlySpan<byte> xml)
    {
        RequireNode();
        _doc!.InsertXml(_index, 0, xml);
    }

    /// <summary>Parses <paramref name="xml"/> as content and adds it before the first child (see <see cref="AppendXml(ReadOnlySpan{byte})"/>).</summary>
    /// <param name="xml">UTF-8 XML content.</param>
    /// <exception cref="OfficeException">The XML is not well-formed, or nests too deeply.</exception>
    /// <exception cref="ArgumentException">A prefix is unbound here, or the node is the document and the XML has elements or text.</exception>
    /// <exception cref="InvalidOperationException">The node cannot have children.</exception>
    public void PrependXml(ReadOnlySpan<byte> xml)
    {
        RequireNode();
        _doc!.InsertXml(_index, _doc.FirstChildOf(_index), xml);
    }

    /// <summary>Parses <paramref name="xml"/> as content and adds it before this node (see <see cref="AppendXml(ReadOnlySpan{byte})"/>).</summary>
    /// <param name="xml">UTF-8 XML content.</param>
    /// <exception cref="OfficeException">The XML is not well-formed, or nests too deeply.</exception>
    /// <exception cref="ArgumentException">A prefix is unbound there, or the XML is not allowed at that place.</exception>
    /// <exception cref="InvalidOperationException">The node has no parent.</exception>
    public void InsertXmlBefore(ReadOnlySpan<byte> xml)
    {
        RequireNode();
        int parent = _doc!.ParentOf(_index);
        if (parent < 0 || _index == 0)
        {
            throw new InvalidOperationException("The node has no parent.");
        }

        _doc.InsertXml(parent, _index, xml);
    }

    /// <summary>Parses <paramref name="xml"/> as content and adds it after this node (see <see cref="AppendXml(ReadOnlySpan{byte})"/>).</summary>
    /// <param name="xml">UTF-8 XML content.</param>
    /// <exception cref="OfficeException">The XML is not well-formed, or nests too deeply.</exception>
    /// <exception cref="ArgumentException">A prefix is unbound there, or the XML is not allowed at that place.</exception>
    /// <exception cref="InvalidOperationException">The node has no parent.</exception>
    public void InsertXmlAfter(ReadOnlySpan<byte> xml)
    {
        RequireNode();
        int parent = _doc!.ParentOf(_index);
        if (parent < 0 || _index == 0)
        {
            throw new InvalidOperationException("The node has no parent.");
        }

        _doc.InsertXml(parent, _doc.NextOf(_index), xml);
    }

    /// <summary>Replaces this node with the parsed content.</summary>
    /// <param name="xml">UTF-8 XML content; may be empty, which removes the node.</param>
    /// <exception cref="OfficeException">The XML is not well-formed, or nests too deeply.</exception>
    /// <exception cref="ArgumentException">A prefix is unbound there, or the XML is not allowed at that place.</exception>
    /// <exception cref="InvalidOperationException">The node is the document or the root element, or has no parent.</exception>
    public void ReplaceWithXml(ReadOnlySpan<byte> xml)
    {
        RequireNode();
        _doc!.ReplaceNode(_index, xml);
    }

    /// <summary>Removes this node and everything below it from the document. Bytes around it stay as they are.</summary>
    /// <exception cref="InvalidOperationException">The node is the document or the root element.</exception>
    public void Remove()
    {
        RequireNode();
        _doc!.RemoveNode(_index);
    }

    /// <inheritdoc/>
    public bool Equals(MarkupNode other) => ReferenceEquals(_doc, other._doc) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MarkupNode n && Equals(n);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_doc, _index);

    /// <summary>Tells whether two handles refer to the same node.</summary>
    /// <param name="left">A handle.</param>
    /// <param name="right">A handle.</param>
    /// <returns>True when they are the same node of the same document.</returns>
    public static bool operator ==(MarkupNode left, MarkupNode right) => left.Equals(right);

    /// <summary>Tells whether two handles refer to different nodes.</summary>
    /// <param name="left">A handle.</param>
    /// <param name="right">A handle.</param>
    /// <returns>True when they differ.</returns>
    public static bool operator !=(MarkupNode left, MarkupNode right) => !left.Equals(right);

    internal MarkupDocument? Doc => _doc;

    internal int Index => _index;

    private MarkupNode Make(int index) => _doc is null || index <= 0 ? default : new MarkupNode(_doc, index);

    private void RequireNode()
    {
        if (_doc is null)
        {
            throw new InvalidOperationException("The node is null.");
        }
    }
}

/// <summary>The children of a node, for <c>foreach</c> and LINQ.</summary>
public readonly struct MarkupChildren : IEnumerable<MarkupNode>
{
    private readonly MarkupNode _parent;
    private readonly bool _elementsOnly;

    internal MarkupChildren(MarkupNode parent, bool elementsOnly)
    {
        _parent = parent;
        _elementsOnly = elementsOnly;
    }

    /// <summary>Gets an enumerator.</summary>
    /// <returns>An enumerator that allocates nothing.</returns>
    public Enumerator GetEnumerator() => new(_parent, _elementsOnly);

    /// <inheritdoc/>
    IEnumerator<MarkupNode> IEnumerable<MarkupNode>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates children in document order.</summary>
    public struct Enumerator : IEnumerator<MarkupNode>
    {
        private readonly MarkupNode _parent;
        private readonly bool _elementsOnly;
        private MarkupNode _current;
        private bool _started;

        internal Enumerator(MarkupNode parent, bool elementsOnly)
        {
            _parent = parent;
            _elementsOnly = elementsOnly;
            _current = default;
            _started = false;
        }

        /// <summary>Gets the current child.</summary>
        public readonly MarkupNode Current => _current;

        /// <inheritdoc/>
        readonly object IEnumerator.Current => _current;

        /// <summary>Moves to the next child.</summary>
        /// <returns>False after the last child.</returns>
        public bool MoveNext()
        {
            _current = _started ? _current.NextSibling : _parent.FirstChild;
            _started = true;
            while (_elementsOnly && !_current.IsNull && _current.Kind != MarkupNodeKind.Element)
            {
                _current = _current.NextSibling;
            }

            return !_current.IsNull;
        }

        /// <summary>Not supported.</summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public readonly void Reset() => throw new NotSupportedException();

        /// <inheritdoc/>
        public readonly void Dispose()
        {
        }
    }
}
