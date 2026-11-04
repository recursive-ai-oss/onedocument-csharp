namespace RecursiveAI.OneOffice.Markup.Xml;

/// <summary>
/// What an <see cref="XmlTokenizer"/> remembers between blocks: the open elements, the namespace bindings in scope and how far
/// the input has been consumed. One instance serves one document. A tokenizer over a single complete buffer creates its own.
/// </summary>
/// <remarks>
/// Bytes are copied here (names, namespace URIs), never offsets into a block, so a block can be discarded once the tokenizer has
/// consumed it. Memory grows with the nesting depth and the number of namespace declarations in scope, never with the document.
/// </remarks>
public sealed class XmlTokenizerState
{
    private struct Binding
    {
        public int PrefixOffset;
        public int PrefixLength;
        public int UriOffset;
        public int UriLength;
    }

    private struct Scope
    {
        public int Bindings;
        public int NamespaceBytes;
        public int NameBytes;
    }

    private byte[] _names = [];
    private int _nameBytes;
    private byte[] _ns = [];
    private int _nsBytes;
    private Binding[] _bindings = [];
    private int _bindingCount;
    private Scope[] _scopes = [];
    private int _depth;
    private bool _pendingPop;

    /// <summary>Creates a state with the default limits for a complete document.</summary>
    public XmlTokenizerState()
        : this(XmlLimits.Default, fragment: false)
    {
    }

    /// <summary>Creates a state.</summary>
    /// <param name="limits">The limits to enforce.</param>
    /// <param name="fragment">
    /// True to read a fragment of content: several top-level nodes, text at the top level, no XML declaration, and prefixes that
    /// no declaration in the fragment binds are accepted (and resolve to nothing).
    /// </param>
    public XmlTokenizerState(XmlLimits limits, bool fragment = false)
    {
        Limits = limits.Normalized();
        IsFragment = fragment;
    }

    /// <summary>Gets the limits in force.</summary>
    public XmlLimits Limits { get; }

    /// <summary>Gets a value that says whether this state reads a fragment rather than a document.</summary>
    public bool IsFragment { get; }

    /// <summary>Gets the offset, from the start of the whole input, at which the next block must begin.</summary>
    public long Consumed { get; internal set; }

    /// <summary>Gets the number of elements open now (the element of the current start-tag token included until the next read).</summary>
    public int Depth => _depth;

    internal bool Started { get; set; }

    internal long TokenCount { get; set; }

    internal bool RootSeen { get; set; }

    internal void SetPendingPop() => _pendingPop = true;

    internal void EndPending()
    {
        if (!_pendingPop)
        {
            return;
        }

        _pendingPop = false;
        _depth--;
        Scope s = _scopes[_depth];
        _nameBytes = s.NameBytes;
        _nsBytes = s.NamespaceBytes;
        _bindingCount = s.Bindings;
    }

    internal void Push(ReadOnlySpan<byte> name)
    {
        if (_depth == _scopes.Length)
        {
            Array.Resize(ref _scopes, Math.Max(16, _scopes.Length * 2));
        }

        _scopes[_depth] = new Scope { Bindings = _bindingCount, NamespaceBytes = _nsBytes, NameBytes = _nameBytes };
        _depth++;
        Grow(ref _names, _nameBytes + name.Length);
        name.CopyTo(_names.AsSpan(_nameBytes));
        _nameBytes += name.Length;
    }

    internal ReadOnlySpan<byte> OpenName => _depth == 0 ? default : _names.AsSpan(_scopes[_depth - 1].NameBytes, _nameBytes - _scopes[_depth - 1].NameBytes);

    internal void Declare(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> rawUri)
    {
        Grow(ref _ns, _nsBytes + prefix.Length + rawUri.Length);
        if (_bindingCount == _bindings.Length)
        {
            Array.Resize(ref _bindings, Math.Max(16, _bindings.Length * 2));
        }

        int po = _nsBytes;
        prefix.CopyTo(_ns.AsSpan(po));
        int uo = po + prefix.Length;
        int ul = XmlText.NeedsDecoding(rawUri, true) ? XmlText.Decode(rawUri, _ns.AsSpan(uo), true) : CopyRaw(rawUri, _ns.AsSpan(uo));
        _bindings[_bindingCount++] = new Binding { PrefixOffset = po, PrefixLength = prefix.Length, UriOffset = uo, UriLength = ul };
        _nsBytes = uo + ul;
    }

    private static int CopyRaw(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        src.CopyTo(dst);
        return src.Length;
    }

    private static void Grow(ref byte[] array, int needed)
    {
        if (needed > array.Length)
        {
            Array.Resize(ref array, Math.Max(needed, Math.Max(256, array.Length * 2)));
        }
    }

    /// <summary>Looks up the namespace bound to a prefix in the scope of the current element.</summary>
    /// <param name="prefix">The prefix as written; empty for the default namespace.</param>
    /// <param name="uri">The namespace URI, entity references decoded. Empty when the default namespace is not declared or was undeclared. Valid until the next read.</param>
    /// <returns>True when a declaration binds the prefix (or it is <c>xml</c>); false when the prefix is unbound.</returns>
    public bool TryResolve(ReadOnlySpan<byte> prefix, out ReadOnlySpan<byte> uri)
    {
        for (int i = _bindingCount - 1; i >= 0; i--)
        {
            Binding b = _bindings[i];
            if (b.PrefixLength == prefix.Length && _ns.AsSpan(b.PrefixOffset, b.PrefixLength).SequenceEqual(prefix))
            {
                uri = _ns.AsSpan(b.UriOffset, b.UriLength);
                return true;
            }
        }

        if (prefix.SequenceEqual("xml"u8))
        {
            uri = XmlNamespaces.XmlUtf8;
            return true;
        }

        uri = default;
        return prefix.Length == 0;
    }
}
