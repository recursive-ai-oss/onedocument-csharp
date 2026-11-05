namespace RecursiveAI.OneDocument.Markup.Xml;

/// <summary>
/// Where the current token lies in the block it was read from, as plain offsets. It lets a caller that discards the tokenizer
/// (it is a ref struct) keep the token and read its parts from the same block later.
/// </summary>
public readonly struct XmlTokenInfo
{
    internal XmlTokenInfo(XmlTokenKind kind, int start, int end, int nameStart, int nameEnd, int colon, int innerStart, int innerEnd, int attributeCount, int depth)
    {
        Kind = kind;
        Start = start;
        End = end;
        NameStart = nameStart;
        NameEnd = nameEnd;
        Colon = colon;
        InnerStart = innerStart;
        InnerEnd = innerEnd;
        AttributeCount = attributeCount;
        Depth = depth;
    }

    /// <summary>Gets the kind of token.</summary>
    public XmlTokenKind Kind { get; }

    /// <summary>Gets the offset of the token's first byte in the block.</summary>
    public int Start { get; }

    /// <summary>Gets the offset after the token's last byte in the block. Tokens tile the input: one starts where the previous one ended.</summary>
    public int End { get; }

    /// <summary>Gets the offset of the first byte of the element name, or of the processing-instruction target.</summary>
    public int NameStart { get; }

    /// <summary>Gets the offset after the last byte of the element name, or of the processing-instruction target.</summary>
    public int NameEnd { get; }

    /// <summary>Gets the offset of the colon in the element name, or -1 when the name has no prefix.</summary>
    public int Colon { get; }

    /// <summary>
    /// Gets the offset where the token's content starts: the text itself, the inside of a CDATA section or comment, the data of a
    /// processing instruction, the pseudo-attributes of the XML declaration, or the end of the name of a tag (where attributes begin).
    /// </summary>
    public int InnerStart { get; }

    /// <summary>
    /// Gets the offset where the token's content ends: the end of the text, CDATA section, comment, processing-instruction data
    /// or XML declaration pseudo-attributes, or for a tag the offset of its closing <c>&gt;</c> or <c>/&gt;</c>.
    /// </summary>
    public int InnerEnd { get; }

    /// <summary>Gets the number of attributes of a start or empty-element tag, namespace declarations included.</summary>
    public int AttributeCount { get; }

    /// <summary>Gets the number of open ancestors: 0 for the root element's tags, 1 for its children and for text directly inside it.</summary>
    public int Depth { get; }

    /// <summary>Gets the whole token, markup included.</summary>
    /// <param name="block">The block the token was read from.</param>
    /// <returns>The bytes exactly as they stand in the input.</returns>
    public ReadOnlySpan<byte> Raw(ReadOnlySpan<byte> block) => block[Start..End];

    /// <summary>Gets the element name or processing-instruction target as written, prefix included.</summary>
    /// <param name="block">The block the token was read from.</param>
    /// <returns>The raw name bytes; empty for tokens without a name.</returns>
    public ReadOnlySpan<byte> RawName(ReadOnlySpan<byte> block) => NameEnd > NameStart ? block[NameStart..NameEnd] : default;

    /// <summary>Gets the prefix of the element name as written; empty when there is none.</summary>
    /// <param name="block">The block the token was read from.</param>
    /// <returns>The prefix bytes.</returns>
    public ReadOnlySpan<byte> Prefix(ReadOnlySpan<byte> block) => Colon >= 0 ? block[NameStart..Colon] : default;

    /// <summary>Gets the local part of the element name.</summary>
    /// <param name="block">The block the token was read from.</param>
    /// <returns>The local name bytes; the whole name when it has no prefix.</returns>
    public ReadOnlySpan<byte> LocalName(ReadOnlySpan<byte> block) => NameEnd > NameStart ? block[(Colon >= 0 ? Colon + 1 : NameStart)..NameEnd] : default;

    /// <summary>Gets the content of the token (see <see cref="InnerStart"/>), raw: references are not decoded and line ends are not normalised.</summary>
    /// <param name="block">The block the token was read from.</param>
    /// <returns>The raw content bytes.</returns>
    public ReadOnlySpan<byte> Content(ReadOnlySpan<byte> block) => block[InnerStart..InnerEnd];

    /// <summary>Gets the attributes of a start or empty-element tag.</summary>
    /// <param name="block">The block the token was read from.</param>
    /// <param name="state">The tokenizer state, to resolve namespaces while this token is current; null to skip resolution.</param>
    /// <returns>An enumerator over the attributes in document order. Empty for other tokens.</returns>
    public XmlAttributeEnumerator Attributes(ReadOnlySpan<byte> block, XmlTokenizerState? state = null) =>
        Kind is XmlTokenKind.StartElement or XmlTokenKind.EmptyElement
            ? new XmlAttributeEnumerator(block, InnerStart, InnerEnd, state)
            : default;
}
