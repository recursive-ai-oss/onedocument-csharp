using System.Globalization;
using System.Text;

namespace RecursiveAI.OneOffice.Schema;

/// <summary>
/// A reference to one element in a markup tree: the tree and the node handle. This is the hop between the hub and the typed
/// views: every view has a <c>Markup</c> property returning one, and every view is built from one.
/// </summary>
/// <remarks>Views hold nothing else, so copying a view or a reference is free and reading through it allocates nothing.</remarks>
public readonly struct MarkupRef : IEquatable<MarkupRef>
{
    /// <summary>Wraps a node of <paramref name="tree"/>.</summary>
    /// <param name="tree">The tree that owns the node.</param>
    /// <param name="node">The node handle, which must be an element of that tree.</param>
    public MarkupRef(IMarkupTree tree, int node)
    {
        Tree = tree;
        Node = node;
    }

    /// <summary>The tree that owns the element; null for <c>default</c>.</summary>
    public IMarkupTree Tree { get; }

    /// <summary>The element's handle in <see cref="Tree"/>; 0 for <c>default</c>.</summary>
    public int Node { get; }

    /// <summary>True when this refers to an element.</summary>
    public bool IsValid => Tree is not null && Node != 0;

    /// <summary>The element's namespace, or <see cref="SchemaNamespace.Unknown"/> when no loaded schema uses its URI.</summary>
    public SchemaNamespace Namespace => SchemaNamespaces.Resolve(Tree.NamespaceUri(Node));

    /// <summary>The element's local name as UTF-8.</summary>
    public ReadOnlySpan<byte> LocalName => Tree.LocalName(Node);

    /// <summary>The element's local name.</summary>
    public string LocalNameText => Encoding.UTF8.GetString(Tree.LocalName(Node));

    /// <summary>The element's namespace URI.</summary>
    public string NamespaceUriText => Encoding.UTF8.GetString(Tree.NamespaceUri(Node));

    /// <summary>All child elements, known or not, in document order.</summary>
    public ChildElements Children => new(this);

    /// <summary>The first child element that is <c>ns:local</c>, or null. Strict and Transitional namespaces both match.</summary>
    public MarkupRef? FindChild(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        for (var c = Tree.FirstChildElement(Node); c != 0; c = Tree.NextSiblingElement(c))
            if (NameIs(c, ns, local)) return new MarkupRef(Tree, c);
        return null;
    }

    /// <summary>Reinterprets the element as view <typeparamref name="T"/> when its name is one the view is declared for.</summary>
    /// <typeparam name="T">A generated view type.</typeparam>
    /// <returns>The view, or null when the element's name is not one <typeparamref name="T"/> belongs to.</returns>
    public T? As<T>() where T : struct, IElementView<T>
        => IsValid && T.Accepts(Namespace, LocalName) ? T.Create(this) : null;

    /// <inheritdoc/>
    public bool Equals(MarkupRef other) => ReferenceEquals(Tree, other.Tree) && Node == other.Node;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MarkupRef r && Equals(r);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Tree), Node);

    /// <summary>Equality of two references.</summary>
    public static bool operator ==(MarkupRef left, MarkupRef right) => left.Equals(right);

    /// <summary>Inequality of two references.</summary>
    public static bool operator !=(MarkupRef left, MarkupRef right) => !left.Equals(right);

    // ---- helpers for the generated views ----

    internal bool NameIs(int node, SchemaNamespace ns, ReadOnlySpan<byte> local)
        => Tree.LocalName(node).SequenceEqual(local) && SchemaNamespaces.Resolve(Tree.NamespaceUri(node)) == ns;

    private bool IsStrictFamily => SchemaNamespaces.IsStrict(Tree.NamespaceUri(Node));

    internal bool TryGetRaw(SchemaNamespace ns, ReadOnlySpan<byte> local, out ReadOnlySpan<byte> raw)
    {
        if (Tree.TryGetAttribute(Node, SchemaNamespaces.Uri(ns, false), local, out raw)) return true;
        var strict = SchemaNamespaces.Uri(ns, true);
        return !strict.SequenceEqual(SchemaNamespaces.Uri(ns, false)) && Tree.TryGetAttribute(Node, strict, local, out raw);
    }

    internal string? GetString(SchemaNamespace ns, ReadOnlySpan<byte> local)
        => TryGetRaw(ns, local, out var raw) ? Decode(raw) : null;

    internal void SetString(SchemaNamespace ns, ReadOnlySpan<byte> local, string? value)
    {
        if (value is null) RemoveAttribute(ns, local);
        else SetRaw(ns, local, value);
    }

    internal void SetRaw(SchemaNamespace ns, ReadOnlySpan<byte> local, string value)
    {
        // Overwrite wherever the attribute already is (either namespace flavour); a new one follows this element's flavour.
        var strictForm = SchemaNamespaces.Uri(ns, true);
        var transForm = SchemaNamespaces.Uri(ns, false);
        ReadOnlySpan<byte> uri;
        if (Tree.TryGetAttribute(Node, transForm, local, out _)) uri = transForm;
        else if (Tree.TryGetAttribute(Node, strictForm, local, out _)) uri = strictForm;
        else uri = SchemaNamespaces.Uri(ns, IsStrictFamily);
        Tree.SetAttribute(Node, uri, local, SchemaNamespaces.Prefix(ns), value);
    }

    internal bool RemoveAttribute(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        var removed = Tree.RemoveAttribute(Node, SchemaNamespaces.Uri(ns, false), local);
        var strict = SchemaNamespaces.Uri(ns, true);
        if (!strict.SequenceEqual(SchemaNamespaces.Uri(ns, false))) removed |= Tree.RemoveAttribute(Node, strict, local);
        return removed;
    }

    internal bool? GetBool(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        if (!TryGetRaw(ns, local, out var raw)) return null;
        return ParseBool(raw);
    }

    internal void SetBool(SchemaNamespace ns, ReadOnlySpan<byte> local, bool? value)
    {
        if (value is null) RemoveAttribute(ns, local);
        else SetRaw(ns, local, value.Value ? "true" : "false");
    }

    internal long? GetInt64(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        if (!TryGetRaw(ns, local, out var raw)) return null;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    internal void SetInt64(SchemaNamespace ns, ReadOnlySpan<byte> local, long? value)
    {
        if (value is null) RemoveAttribute(ns, local);
        else SetRaw(ns, local, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    internal double? GetDouble(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        if (!TryGetRaw(ns, local, out var raw)) return null;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    internal void SetDouble(SchemaNamespace ns, ReadOnlySpan<byte> local, double? value)
    {
        if (value is null) RemoveAttribute(ns, local);
        else SetRaw(ns, local, value.Value.ToString("R", CultureInfo.InvariantCulture));
    }

    internal T? GetChild<T>(SchemaNamespace ns, ReadOnlySpan<byte> local) where T : struct, IElementView<T>
        => FindChild(ns, local) is { } c ? T.Create(c) : null;

    internal bool RemoveChild(SchemaNamespace ns, ReadOnlySpan<byte> local)
    {
        var removed = false;
        for (var c = Tree.FirstChildElement(Node); c != 0;)
        {
            var next = Tree.NextSiblingElement(c);
            if (NameIs(c, ns, local)) { Tree.RemoveElement(c); removed = true; }
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
        var before = 0;
        if (rank >= 0)
        {
            for (var c = Tree.FirstChildElement(Node); c != 0; c = Tree.NextSiblingElement(c))
            {
                TParent.ChildKind(SchemaNamespaces.Resolve(Tree.NamespaceUri(c)), Tree.LocalName(c), out var r);
                if (r > rank) { before = c; break; }
            }
        }
        var node = Tree.InsertElement(Node, before, SchemaNamespaces.Uri(ns, IsStrictFamily), local, SchemaNamespaces.Prefix(ns));
        return new MarkupRef(Tree, node);
    }

    internal string GetText() => Tree.GetText(Node);

    internal void SetText(string? value) => Tree.SetText(Node, value ?? string.Empty);

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

    /// <summary>Decodes UTF-8 attribute text, resolving the five predefined entities and numeric character references.</summary>
    internal static string Decode(ReadOnlySpan<byte> raw)
    {
        if (raw.IndexOf((byte)'&') < 0) return Encoding.UTF8.GetString(raw);
        var s = Encoding.UTF8.GetString(raw);
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '&') { sb.Append(s[i]); continue; }
            var semi = s.IndexOf(';', i + 1);
            if (semi < 0) { sb.Append(s[i]); continue; }
            var name = s.AsSpan(i + 1, semi - i - 1);
            if (name.SequenceEqual("amp")) sb.Append('&');
            else if (name.SequenceEqual("lt")) sb.Append('<');
            else if (name.SequenceEqual("gt")) sb.Append('>');
            else if (name.SequenceEqual("quot")) sb.Append('"');
            else if (name.SequenceEqual("apos")) sb.Append('\'');
            else if (name.Length > 1 && name[0] == '#' && TryCodePoint(name[1..], out var cp)) sb.Append(char.ConvertFromUtf32(cp));
            else { sb.Append(s[i]); continue; }
            i = semi;
        }
        return sb.ToString();
    }

    private static bool TryCodePoint(ReadOnlySpan<char> digits, out int cp)
    {
        cp = 0;
        var ok = digits.Length > 0 && digits[0] == 'x'
            ? int.TryParse(digits[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out cp)
            : int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out cp);
        return ok && cp is > 0 and <= 0x10FFFF && !(cp is >= 0xD800 and <= 0xDFFF);
    }
}

/// <summary>Enumerates the child elements of an element, whatever they are.</summary>
public ref struct ChildElements
{
    private readonly MarkupRef _parent;
    private int _current;

    internal ChildElements(MarkupRef parent)
    {
        _parent = parent;
        _current = 0;
        Started = false;
    }

    private bool Started { get; set; }

    /// <summary>Returns this enumerator so the type works in <c>foreach</c>.</summary>
    public readonly ChildElements GetEnumerator() => this;

    /// <summary>The current child.</summary>
    public readonly MarkupRef Current => new(_parent.Tree, _current);

    /// <summary>Moves to the next child element.</summary>
    public bool MoveNext()
    {
        _current = Started ? _parent.Tree.NextSiblingElement(_current) : _parent.Tree.FirstChildElement(_parent.Node);
        Started = true;
        return _current != 0;
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
        private int _current;
        private bool _started;

        internal Enumerator(MarkupRef parent, SchemaNamespace ns, ReadOnlySpan<byte> local)
        {
            _parent = parent;
            _ns = ns;
            _local = local;
            _current = 0;
            _started = false;
        }

        /// <summary>The current child as a view.</summary>
        public readonly T Current => T.Create(new MarkupRef(_parent.Tree, _current));

        /// <summary>Moves to the next matching child.</summary>
        public bool MoveNext()
        {
            var tree = _parent.Tree;
            var c = _started ? tree.NextSiblingElement(_current) : tree.FirstChildElement(_parent.Node);
            _started = true;
            while (c != 0 && !_parent.NameIs(c, _ns, _local)) c = tree.NextSiblingElement(c);
            _current = c;
            return c != 0;
        }
    }
}

/// <summary>The child elements the view's content model does not declare: extensions, <c>mc:AlternateContent</c>, foreign markup.</summary>
/// <typeparam name="T">The view whose content model decides what is known.</typeparam>
public ref struct UnknownChildren<T> where T : struct, IElementView<T>
{
    private readonly MarkupRef _parent;
    private int _current;
    private bool _started;

    internal UnknownChildren(MarkupRef parent)
    {
        _parent = parent;
        _current = 0;
        _started = false;
    }

    /// <summary>Returns this enumerator so the type works in <c>foreach</c>.</summary>
    public readonly UnknownChildren<T> GetEnumerator() => this;

    /// <summary>The current unknown child.</summary>
    public readonly MarkupRef Current => new(_parent.Tree, _current);

    /// <summary>Moves to the next unknown child.</summary>
    public bool MoveNext()
    {
        var tree = _parent.Tree;
        var c = _started ? tree.NextSiblingElement(_current) : tree.FirstChildElement(_parent.Node);
        _started = true;
        while (c != 0 && T.ChildKind(SchemaNamespaces.Resolve(tree.NamespaceUri(c)), tree.LocalName(c), out _) != ViewKind.None)
            c = tree.NextSiblingElement(c);
        _current = c;
        return c != 0;
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
    /// <returns>The attributes whose namespace and name the view's schema type does not declare.</returns>
    public static List<UnknownAttributeValue> Of<T>(T view) where T : struct, IElementView<T>
    {
        var m = view.Markup;
        var list = new List<UnknownAttributeValue>();
        var count = m.Tree.AttributeCount(m.Node);
        for (var i = 0; i < count; i++)
        {
            m.Tree.GetAttributeName(m.Node, i, out var ns, out var local);
            if (T.IsKnownAttribute(SchemaNamespaces.Resolve(ns), local)) continue;
            m.Tree.TryGetAttribute(m.Node, ns, local, out var raw);
            list.Add(new UnknownAttributeValue(Encoding.UTF8.GetString(ns), Encoding.UTF8.GetString(local), MarkupRef.Decode(raw)));
        }
        return list;
    }
}
