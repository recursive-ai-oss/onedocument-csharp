namespace RecursiveAI.OneOffice.Markup.Xml;

/// <summary>
/// The resource limits of the XML tokenizer. The type is self-contained so the tokenizer can be lifted out of OneOffice;
/// the core package builds one from its shared limits object.
/// </summary>
/// <remarks>Each value must be positive. A zero value (as in <c>default(XmlLimits)</c>) is treated as the default.</remarks>
public readonly struct XmlLimits
{
    /// <summary>The default for <see cref="MaxDepth"/>.</summary>
    public const int DefaultMaxDepth = 256;

    /// <summary>The default for <see cref="MaxAttributeCount"/>.</summary>
    public const int DefaultMaxAttributeCount = 256;

    /// <summary>The default for <see cref="MaxNameLength"/>.</summary>
    public const int DefaultMaxNameLength = 1024;

    /// <summary>Gets the default limits.</summary>
    public static XmlLimits Default => new() { MaxDepth = DefaultMaxDepth, MaxAttributeCount = DefaultMaxAttributeCount, MaxNameLength = DefaultMaxNameLength };

    /// <summary>Gets the most elements that may be open at once. The default is 256.</summary>
    public int MaxDepth { get; init; }

    /// <summary>Gets the most attributes one element may have, namespace declarations included. The default is 256.</summary>
    public int MaxAttributeCount { get; init; }

    /// <summary>Gets the longest element, attribute or prefix name in bytes. The default is 1,024.</summary>
    public int MaxNameLength { get; init; }

    internal XmlLimits Normalized() => new()
    {
        MaxDepth = MaxDepth > 0 ? MaxDepth : DefaultMaxDepth,
        MaxAttributeCount = MaxAttributeCount > 0 ? MaxAttributeCount : DefaultMaxAttributeCount,
        MaxNameLength = MaxNameLength > 0 ? MaxNameLength : DefaultMaxNameLength,
    };
}
