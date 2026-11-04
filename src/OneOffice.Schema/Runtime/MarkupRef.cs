using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Schema;

/// <summary>Parses attribute text (UTF-8, entity references still in place) into an enumeration member.</summary>
/// <typeparam name="T">The enumeration.</typeparam>
/// <param name="raw">The raw attribute value.</param>
/// <returns>The member.</returns>
public delegate T SpanParser<out T>(ReadOnlySpan<byte> raw);

/// <summary>
/// One element in a markup tree: the hop between the hub and the typed views. Every view has a <c>Markup</c> property returning
/// one, and every view is built from one. It is the only place the views touch the markup layer.
/// </summary>
/// <remarks>Views hold nothing else, so copying a view or a reference is free and reading through it allocates nothing it does not return.</remarks>
public readonly struct MarkupRef : IEquatable<MarkupRef>
{
    /// <summary>Wraps an element of a markup document.</summary>
    /// <param name="node">The element.</param>
    public MarkupRef(MarkupNode node)
    {
        Node = node;
    }

    /// <summary>The markup node: the way back to the markup layer.</summary>
    public MarkupNode Node { get; }

    /// <summary>True when this refers to an element.</summary>
    public bool IsValid => !Node.IsNull && Node.Kind == MarkupNodeKind.Element;

    /// <summary>The element's namespace, or <see cref="SchemaNamespace.Unknown"/> when no loaded schema uses its URI.</summary>
    public SchemaNamespace Namespace => SchemaNamespaces.Resolve(Node.NamespaceUri);

    /// <summary>The element's local name as UTF-8. Valid until the document is next edited.</summary>
    public ReadOnlySpan<byte> LocalName => Node.LocalName;

    /// <summary>The element's local name.</summary>
    public string LocalNameText => Node.LocalNameString;

    /// <summary>The element's namespace URI.</summary>
    public string NamespaceUriText => Node.NamespaceUri;

    /// <summary>All child elements, known or not, in document order.</summary>
    public ChildElements Children => new(this);

    /// <summary>The first child element that is <c>ns:local</c>, or null. Strict and Transitional namespaces both match.</summary>
    /// <param name="ns">The child's namespace.</param>
    /// <param name="local">The child's local name, UTF-8.</param>
    /// <returns>The child, or null.</returns>
    public MarkupRef? FindChild(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        for (var c = Node.FirstChild; !c.IsNull; c = c.NextSibling)
            if (c.Kind == MarkupNodeKind.Element && NameIs(c, ns, local)) return new MarkupRef(c);
        return null;
    }

    /// <summary>Reinterprets the element as view <typeparamref name="T"/> when its name is one the view is declared for.</summary>
    /// <typeparam name="T">A generated view type.</typeparam>
    /// <returns>The view, or null when the element's name is not one <typeparamref name="T"/> belongs to.</returns>
    public T? As<T>() where T : struct, IElementView<T>
        => IsValid && T.Accepts(Namespace, LocalName) ? T.Create(this) : null;

    /// <inheritdoc/>
    public bool Equals(MarkupRef other) => Node == other.Node;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MarkupRef r && Equals(r);

    /// <inheritdoc/>
    public override int GetHashCode() => Node.GetHashCode();

    /// <summary>Equality of two references.</summary>
    public static bool operator ==(MarkupRef left, MarkupRef right) => left.Equals(right);

    /// <summary>Inequality of two references.</summary>
    public static bool operator !=(MarkupRef left, MarkupRef right) => !left.Equals(right);

    // ---- names ----

    internal static bool NameIs(MarkupNode node, SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        var plain = SchemaNamespaces.Uri(ns, false);
        if (node.IsElement(plain, local)) return true;
        var strict = SchemaNamespaces.Uri(ns, true);
        return strict.Length != plain.Length && node.IsElement(strict, local);
    }

    private bool IsStrictFamily => SchemaNamespaces.IsStrict(Node.NamespaceUri);

    private static bool AttributeMatches(MarkupAttributeRef a, SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        if (a.IsNamespaceDeclaration || !a.LocalName.SequenceEqual(local)) return false;
        if (ns == SchemaNamespace.None) return a.Prefix.Length == 0;
        return a.Prefix.Length > 0 && (a.IsInNamespace(SchemaNamespaces.Uri(ns, false)) || a.IsInNamespace(SchemaNamespaces.Uri(ns, true)));
    }

    // ---- attribute reads: nothing is allocated except a returned string ----

    internal string? GetString(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        foreach (var a in Node.Attributes)
            if (AttributeMatches(a, ns, local)) return a.Value;
        return null;
    }

    internal bool? GetBool(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        foreach (var a in Node.Attributes)
            if (AttributeMatches(a, ns, local)) return ParseBool(a.RawValue);
        return null;
    }

    internal long? GetInt64(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        foreach (var a in Node.Attributes)
            if (AttributeMatches(a, ns, local))
                return long.TryParse(a.RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        return null;
    }

    internal double? GetDouble(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        foreach (var a in Node.Attributes)
            if (AttributeMatches(a, ns, local))
                return double.TryParse(a.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return null;
    }

    internal T? GetEnum<T>(SchemaNamespace ns, ReadOnlySpan<byte> local, SpanParser<T> parser) where T : struct
    {
        foreach (var a in Node.Attributes)
            if (AttributeMatches(a, ns, local)) return parser(a.RawValue);
        return null;
    }

    // ---- attribute writes ----

    internal void SetString(SchemaNamespace ns, ReadOnlySpan<byte> local, string? value)
    {
        if (value is null) { RemoveAttribute(ns, local); return; }
        var name = Encoding.UTF8.GetString(local);
        if (ns == SchemaNamespace.None) { Node.SetAttribute(name, value); return; }
        // Overwrite wherever the attribute already is (either namespace flavour); a new one follows this element's flavour.
        foreach (var a in Node.Attributes)
        {
            if (!AttributeMatches(a, ns, local)) continue;
            var raw = Encoding.UTF8.GetString(a.RawName);
            Node.SetAttribute(raw, value);
            return;
        }
        var uri = Encoding.UTF8.GetString(SchemaNamespaces.Uri(ns, IsStrictFamily));
        var prefix = PrefixInScope(Node, uri, attribute: true);
        if (prefix is null)
        {
            var preferred = Encoding.UTF8.GetString(SchemaNamespaces.Prefix(ns));
            if (preferred.Length > 0 && !PrefixDeclared(Node, preferred))
            {
                Node.SetAttribute("xmlns:" + preferred, uri);
                prefix = preferred;
            }
        }
        if (prefix is null) Node.SetAttribute(uri, name, value);
        else Node.SetAttribute(prefix + ":" + name, value);
    }

    internal void SetBool(SchemaNamespace ns, ReadOnlySpan<byte> local, bool? value)
        => SetString(ns, local, value is null ? null : value.Value ? "true" : "false");

    internal void SetInt64(SchemaNamespace ns, ReadOnlySpan<byte> local, long? value)
        => SetString(ns, local, value?.ToString(CultureInfo.InvariantCulture));

    internal void SetDouble(SchemaNamespace ns, ReadOnlySpan<byte> local, double? value)
        => SetString(ns, local, value?.ToString("R", CultureInfo.InvariantCulture));

    internal bool RemoveAttribute(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        string? raw = null;
        foreach (var a in Node.Attributes)
            if (AttributeMatches(a, ns, local)) { raw = Encoding.UTF8.GetString(a.RawName); break; }
        return raw is not null && Node.RemoveAttribute(raw);
    }

    // ---- children ----

    internal T? GetChild<T>(SchemaNamespace ns, ReadOnlySpan<byte> local) where T : struct, IElementView<T>
        => FindChild(ns, local) is { } c ? T.Create(c) : null;

    internal bool RemoveChild(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        var removed = false;
        for (var c = Node.FirstChild; !c.IsNull;)
        {
            var next = c.NextSibling;
            if (c.Kind == MarkupNodeKind.Element && NameIs(c, ns, local)) { c.Remove(); removed = true; }
            c = next;
        }
        return removed;
    }

    internal T GetOrAddChild<T, TParent>(SchemaNamespace ns, ReadOnlySpan<byte> local)
        where T : struct, IElementView<T> where TParent : struct, IElementView<TParent>
        => FindChild(ns, local) is { } c ? T.Create(c) : AddChild<T, TParent>(ns, local);

    /// <summary>Adds a child in the position the parent's content model gives it, after the elements that must precede it.</summary>
    internal T AddChild<T, TParent>(SchemaNamespace ns, ReadOnlySpan<byte> local)
        where T : struct, IElementView<T> where TParent : struct, IElementView<TParent>
        => T.Create(AddChildElement<TParent>(ns, local));

    internal MarkupRef AddChildElement<TParent>(SchemaNamespace ns, ReadOnlySpan<byte> local)
        where TParent : struct, IElementView<TParent>
    {
        TParent.ChildKind(ns, local, out var rank);
        var before = default(MarkupNode);
        if (rank >= 0)
        {
            for (var c = Node.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind != MarkupNodeKind.Element) continue;
                TParent.ChildKind(SchemaNamespaces.Resolve(c.NamespaceUri), c.LocalName, out var r);
                if (r > rank) { before = c; break; }
            }
        }
        var uri = Encoding.UTF8.GetString(SchemaNamespaces.Uri(ns, IsStrictFamily));
        var name = Encoding.UTF8.GetString(local);
        var (prefix, declare) = ElementPrefix(Node, uri, Encoding.UTF8.GetString(SchemaNamespaces.Prefix(ns)));
        var qname = prefix.Length == 0 ? name : prefix + ":" + name;
        var xml = Encoding.UTF8.GetBytes("<" + qname + (declare ? (prefix.Length == 0 ? " xmlns=\"" : " xmlns:" + prefix + "=\"") + uri + "\"" : "") + "/>");
        if (before.IsNull)
        {
            Node.AppendXml(xml);
            return new MarkupRef(Node.LastChild);
        }
        before.InsertXmlBefore(xml);
        return new MarkupRef(before.PreviousSibling);
    }

    // ---- namespace declarations in scope ----

    /// <summary>Finds a prefix bound to <paramref name="uri"/> at <paramref name="start"/>; "" for a default-namespace binding (elements only). Null when none.</summary>
    private static string? PrefixInScope(MarkupNode start, string uri, bool attribute)
    {
        HashSet<string>? seen = null;
        for (var n = start; !n.IsNull && n.Kind == MarkupNodeKind.Element; n = n.Parent)
        {
            foreach (var a in n.Attributes)
            {
                if (!a.IsNamespaceDeclaration) continue;
                var raw = Encoding.UTF8.GetString(a.RawName);
                var prefix = raw.Length > 6 ? raw[6..] : string.Empty; // "xmlns:p" or "xmlns"
                seen ??= [];
                if (!seen.Add(prefix)) continue; // rebound closer to the element
                if (a.Value == uri && (prefix.Length > 0 || !attribute)) return prefix;
            }
        }
        return null;
    }

    private static bool PrefixDeclared(MarkupNode start, string prefix)
    {
        var raw = "xmlns:" + prefix;
        for (var n = start; !n.IsNull && n.Kind == MarkupNodeKind.Element; n = n.Parent)
            foreach (var a in n.Attributes)
                if (a.IsNamespaceDeclaration && a.RawName.SequenceEqual(Encoding.UTF8.GetBytes(raw))) return true;
        return false;
    }

    private static (string Prefix, bool Declare) ElementPrefix(MarkupNode parent, string uri, string preferred)
    {
        var found = PrefixInScope(parent, uri, attribute: false);
        if (found is not null) return (found, false);
        if (preferred.Length > 0 && !PrefixDeclared(parent, preferred)) return (preferred, true);
        // Neither the conventional prefix nor an existing binding is usable: the first free ns<N>, which keeps the output deterministic.
        for (var i = 1; ; i++)
        {
            var p = "ns" + i.ToString(CultureInfo.InvariantCulture);
            if (!PrefixDeclared(parent, p)) return (p, true);
        }
    }

    // ---- text ----

    internal string GetText() => Node.InnerText;

    internal void SetText(string? value) => Node.SetText(value ?? string.Empty);

    internal static bool? ParseBool(ReadOnlySpan<byte> raw)
    {
        // ECMA-376 Part 1 §22.9.2.7 ST_OnOff: true, false, 0, 1, plus on and off in Transitional documents.
        switch (raw.Length)
        {
            case 1: return raw[0] == '1' ? true : raw[0] == '0' ? false : null;
            case 2: return Lower(raw[0]) == 'o' && Lower(raw[1]) == 'n' ? true : null;
            case 3: return Lower(raw[0]) == 'o' && Lower(raw[1]) == 'f' && Lower(raw[2]) == 'f' ? false : null;
            case 4: return Lower(raw[0]) == 't' && Lower(raw[1]) == 'r' && Lower(raw[2]) == 'u' && Lower(raw[3]) == 'e' ? true : null;
            case 5:
                return Lower(raw[0]) == 'f' && Lower(raw[1]) == 'a' && Lower(raw[2]) == 'l' && Lower(raw[3]) == 's' && Lower(raw[4]) == 'e'
                    ? false : null;
            default: return null;
        }
    }

    private static int Lower(byte b) => b | 0x20;
}

/// <summary>Enumerates the child elements of an element, whatever they are.</summary>
public ref struct ChildElements
{
    private readonly MarkupNode _parent;
    private MarkupNode _current;

    internal ChildElements(MarkupRef parent)
    {
        _parent = parent.Node;
        _current = default;
    }

    /// <summary>Returns this enumerator so the type works in <c>foreach</c>.</summary>
    public readonly ChildElements GetEnumerator() => this;

    /// <summary>The current child.</summary>
    public readonly MarkupRef Current => new(_current);

    /// <summary>Moves to the next child element.</summary>
    public bool MoveNext()
    {
        var c = _current.IsNull ? _parent.FirstChild : _current.NextSibling;
        while (!c.IsNull && c.Kind != MarkupNodeKind.Element) c = c.NextSibling;
        _current = c;
        return !c.IsNull;
    }
}

/// <summary>The children of an element that are <c>ns:local</c>, as views of type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The view type of the children.</typeparam>
public readonly ref struct ChildList<T> where T : struct, IElementView<T>
{
    private readonly MarkupRef _parent;
    private readonly SchemaNamespace _ns;
    private readonly ReadOnlySpan<byte> _local;

    internal ChildList(MarkupRef parent, SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        _parent = parent;
        _ns = ns;
        _local = local;
    }

    /// <summary>Returns an enumerator over the matching children, in document order.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(_parent, _ns, _local);

    /// <summary>The number of matching children; walks the children.</summary>
    public int Count
    {
        get
        {
            var n = 0;
            var e = GetEnumerator();
            while (e.MoveNext()) n++;
            return n;
        }
    }

    /// <summary>Copies the matching children into a new array.</summary>
    /// <returns>The views.</returns>
    public T[] ToArray()
    {
        var list = new List<T>();
        foreach (var c in this) list.Add(c);
        return list.ToArray();
    }

    /// <summary>Enumerator for <see cref="ChildList{T}"/>.</summary>
    public ref struct Enumerator
    {
        private readonly MarkupRef _parent;
        private readonly SchemaNamespace _ns;
        private readonly ReadOnlySpan<byte> _local;
        private MarkupNode _current;

        internal Enumerator(MarkupRef parent, SchemaNamespace ns, ReadOnlySpan<byte> local)
        {
            _parent = parent;
            _ns = ns;
            _local = local;
            _current = default;
        }

        /// <summary>The current child as a view.</summary>
        public readonly T Current => T.Create(new MarkupRef(_current));

        /// <summary>Moves to the next matching child.</summary>
        public bool MoveNext()
        {
            var c = _current.IsNull ? _parent.Node.FirstChild : _current.NextSibling;
            while (!c.IsNull && !(c.Kind == MarkupNodeKind.Element && MarkupRef.NameIs(c, _ns, _local))) c = c.NextSibling;
            _current = c;
            return !c.IsNull;
        }
    }
}

/// <summary>The child elements the view's content model does not declare: extensions, <c>mc:AlternateContent</c>, foreign markup.</summary>
/// <typeparam name="T">The view whose content model decides what is known.</typeparam>
public ref struct UnknownChildren<T> where T : struct, IElementView<T>
{
    private readonly MarkupNode _parent;
    private MarkupNode _current;

    internal UnknownChildren(MarkupRef parent)
    {
        _parent = parent.Node;
        _current = default;
    }

    /// <summary>Returns this enumerator so the type works in <c>foreach</c>.</summary>
    /// <returns>The enumerator.</returns>
    public readonly UnknownChildren<T> GetEnumerator() => this;

    /// <summary>The current unknown child.</summary>
    public readonly MarkupRef Current => new(_current);

    /// <summary>Moves to the next unknown child.</summary>
    public bool MoveNext()
    {
        var c = _current.IsNull ? _parent.FirstChild : _current.NextSibling;
        while (!c.IsNull && (c.Kind != MarkupNodeKind.Element
            || T.ChildKind(SchemaNamespaces.Resolve(c.NamespaceUri), c.LocalName, out _) != ViewKind.None))
            c = c.NextSibling;
        _current = c;
        return !c.IsNull;
    }
}

/// <summary>An attribute no schema declares on the view's element.</summary>
public readonly struct UnknownAttributeValue
{
    internal UnknownAttributeValue(string namespaceUri, string localName, string value)
    {
        NamespaceUri = namespaceUri;
        LocalName = localName;
        Value = value;
    }

    /// <summary>The attribute's namespace URI, empty for none.</summary>
    public string NamespaceUri { get; }

    /// <summary>The attribute's local name.</summary>
    public string LocalName { get; }

    /// <summary>The decoded value.</summary>
    public string Value { get; }
}

/// <summary>Reads the attributes of an element that the view's schema type does not declare.</summary>
public static class UnknownAttributes
{
    /// <summary>Lists the undeclared attributes of <paramref name="view"/>'s element in document order (allocates the result).</summary>
    /// <typeparam name="T">The view type.</typeparam>
    /// <param name="view">The view to inspect.</param>
    /// <returns>The attributes whose namespace and name the view's schema type does not declare; namespace declarations are not attributes.</returns>
    public static List<UnknownAttributeValue> Of<T>(T view) where T : struct, IElementView<T>
    {
        var list = new List<UnknownAttributeValue>();
        foreach (var a in view.Markup.Node.Attributes)
        {
            if (a.IsNamespaceDeclaration) continue;
            var uri = a.NamespaceUri;
            if (T.IsKnownAttribute(uri.Length == 0 ? SchemaNamespace.None : SchemaNamespaces.Resolve(uri), a.LocalName)) continue;
            list.Add(new UnknownAttributeValue(uri, Encoding.UTF8.GetString(a.LocalName), a.Value));
        }
        return list;
    }
}
