namespace RecursiveAI.OneOffice.Markup.Xml;

/// <summary>One attribute of a start or empty-element tag, as spans over the block it was read from.</summary>
public readonly ref struct XmlAttributeToken
{
    private readonly ReadOnlySpan<byte> _block;
    private readonly XmlTokenizerState? _state;
    private readonly int _colon;

    internal XmlAttributeToken(ReadOnlySpan<byte> block, XmlTokenizerState? state, int start, int end, int nameStart, int nameEnd, int colon, int valueStart, int valueEnd)
    {
        _block = block;
        _state = state;
        Start = start;
        End = end;
        NameStart = nameStart;
        NameEnd = nameEnd;
        _colon = colon;
        ValueStart = valueStart;
        ValueEnd = valueEnd;
    }

    /// <summary>Gets the offset in the block of the first byte of the name.</summary>
    public int Start { get; }

    /// <summary>Gets the offset in the block after the closing quote.</summary>
    public int End { get; }

    /// <summary>Gets the offset in the block of the first byte of the name (same as <see cref="Start"/>).</summary>
    public int NameStart { get; }

    /// <summary>Gets the offset in the block after the last byte of the name.</summary>
    public int NameEnd { get; }

    /// <summary>Gets the offset in the block of the first byte of the value (after the opening quote).</summary>
    public int ValueStart { get; }

    /// <summary>Gets the offset in the block after the last byte of the value (the closing quote's offset).</summary>
    public int ValueEnd { get; }

    /// <summary>Gets the name as written, prefix included.</summary>
    public ReadOnlySpan<byte> RawName => _block[NameStart..NameEnd];

    /// <summary>Gets the prefix as written; empty when the name has none.</summary>
    public ReadOnlySpan<byte> Prefix => _colon >= 0 ? _block[NameStart.._colon] : default;

    /// <summary>Gets the local part of the name.</summary>
    public ReadOnlySpan<byte> LocalName => _block[(_colon >= 0 ? _colon + 1 : NameStart)..NameEnd];

    /// <summary>Gets the value between the quotes, raw: references are not decoded. See <see cref="XmlText.GetString"/>.</summary>
    public ReadOnlySpan<byte> RawValue => _block[ValueStart..ValueEnd];

    /// <summary>Gets the quote character that delimits the value: <c>"</c> or <c>'</c>.</summary>
    public byte Quote => _block[ValueStart - 1];

    /// <summary>Gets a value that says whether this is a namespace declaration: <c>xmlns</c> or <c>xmlns:prefix</c>.</summary>
    public bool IsNamespaceDeclaration => _colon < 0 ? RawName.SequenceEqual("xmlns"u8) : Prefix.SequenceEqual("xmlns"u8);

    /// <summary>Gets the prefix a namespace declaration declares; empty for <c>xmlns</c>. Meaningful only when <see cref="IsNamespaceDeclaration"/> is true.</summary>
    public ReadOnlySpan<byte> DeclaredPrefix => _colon < 0 ? default : LocalName;

    /// <summary>
    /// Gets the namespace of the attribute, resolved with the tokenizer's current scope. Empty for unprefixed attributes (they are
    /// in no namespace), for namespace declarations, and when the enumerator was made without a state.
    /// </summary>
    public ReadOnlySpan<byte> NamespaceUri
    {
        get
        {
            if (_colon < 0 || _state is null || IsNamespaceDeclaration)
            {
                return default;
            }

            return _state.TryResolve(Prefix, out ReadOnlySpan<byte> uri) ? uri : default;
        }
    }
}

/// <summary>
/// Enumerates the attributes of a tag. The tokenizer has already validated the tag, so this only slices; it allocates nothing.
/// It is its own enumerable, so <c>foreach (var a in token.Attributes(block))</c> works.
/// </summary>
public ref struct XmlAttributeEnumerator
{
    private readonly ReadOnlySpan<byte> _block;
    private readonly XmlTokenizerState? _state;
    private readonly int _end;
    private int _pos;
    private int _start;
    private int _nameEnd;
    private int _colon;
    private int _valueStart;
    private int _valueEnd;

    internal XmlAttributeEnumerator(ReadOnlySpan<byte> block, int from, int to, XmlTokenizerState? state)
    {
        _block = block;
        _pos = from;
        _end = to;
        _state = state;
        _start = _nameEnd = _valueStart = _valueEnd = 0;
        _colon = -1;
    }

    /// <summary>Gets the attribute the enumerator is on.</summary>
    public readonly XmlAttributeToken Current => new(_block, _state, _start, _valueEnd + 1, _start, _nameEnd, _colon, _valueStart, _valueEnd);

    /// <summary>Returns this enumerator, so the type can be used in <c>foreach</c>.</summary>
    /// <returns>This enumerator.</returns>
    public readonly XmlAttributeEnumerator GetEnumerator() => this;

    /// <summary>Moves to the next attribute.</summary>
    /// <returns>False after the last attribute.</returns>
    public bool MoveNext()
    {
        ReadOnlySpan<byte> d = _block;
        int p = XmlScan.SkipSpace(d, _pos);
        if (p >= _end)
        {
            _pos = _end;
            return false;
        }

        _start = p;
        _nameEnd = XmlScan.ScanName(d, p);
        _colon = d[p.._nameEnd].IndexOf((byte)':');
        if (_colon >= 0)
        {
            _colon += p;
        }

        int q = XmlScan.SkipSpace(d, _nameEnd);
        q = XmlScan.SkipSpace(d, q + 1); // the '='
        byte quote = d[q];
        _valueStart = q + 1;
        _valueEnd = _valueStart + d[_valueStart..].IndexOf(quote);
        _pos = _valueEnd + 1;
        return true;
    }
}
