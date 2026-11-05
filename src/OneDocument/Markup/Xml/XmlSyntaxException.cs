namespace RecursiveAI.OneDocument.Markup.Xml;

/// <summary>What was wrong with the input an <see cref="XmlTokenizer"/> refused.</summary>
public enum XmlErrorCode
{
    /// <summary>The input ended inside a token or with elements still open.</summary>
    UnexpectedEnd,

    /// <summary>The input has a document type declaration. DTDs, internal subsets and entity declarations are always refused.</summary>
    DtdRefused,

    /// <summary>A character that XML 1.0 does not allow (a control character, or <c>&lt;</c> in an attribute value).</summary>
    InvalidCharacter,

    /// <summary>The bytes are not valid UTF-8 (or not valid UTF-16 where a transcoding helper was used).</summary>
    InvalidEncoding,

    /// <summary>The input is in an encoding the tokenizer does not read directly (UTF-16 or UTF-32); use <see cref="XmlEncoding"/> first.</summary>
    UnsupportedEncoding,

    /// <summary>An element, attribute, prefix or processing-instruction name is malformed.</summary>
    InvalidName,

    /// <summary>Anything else that breaks the XML grammar.</summary>
    Syntax,

    /// <summary>An end tag does not match the open element.</summary>
    MismatchedEndTag,

    /// <summary>An element has the same attribute twice, by name or by namespace and local name.</summary>
    DuplicateAttribute,

    /// <summary>A name uses a prefix that no namespace declaration binds.</summary>
    UnboundPrefix,

    /// <summary>A namespace declaration breaks Namespaces in XML (reserved prefix or URI, empty prefixed binding).</summary>
    InvalidNamespaceDeclaration,

    /// <summary>A character or entity reference is malformed, names an entity other than the five predefined ones, or is not an XML character.</summary>
    InvalidReference,

    /// <summary>Content where the document structure does not allow it: text or a second root element outside the root.</summary>
    MisplacedContent,

    /// <summary>The document has no root element.</summary>
    NoRoot,

    /// <summary>The XML declaration is malformed or is not the first thing in the document.</summary>
    InvalidDeclaration,

    /// <summary>A limit in <see cref="XmlLimits"/> was exceeded; <see cref="XmlSyntaxException.LimitName"/> says which.</summary>
    LimitExceeded,
}

/// <summary>
/// Raised by the XML tokenizer, writer helpers and encoding helpers when the input cannot be read. It is the tokenizer's own
/// typed error, with the byte offset of the problem. The markup layer above turns it into the library's exceptions.
/// </summary>
public sealed class XmlSyntaxException : Exception
{
    /// <summary>Creates an error for a malformed input.</summary>
    /// <param name="code">What was wrong.</param>
    /// <param name="offset">Byte offset in the input (or -1 if unknown).</param>
    /// <param name="message">A description for people.</param>
    public XmlSyntaxException(XmlErrorCode code, long offset, string message)
        : base(message)
    {
        Code = code;
        Offset = offset;
    }

    /// <summary>Creates an error for a limit that was exceeded.</summary>
    /// <param name="limitName">The name of the <see cref="XmlLimits"/> property.</param>
    /// <param name="limit">The limit's value.</param>
    /// <param name="actual">What the input asked for.</param>
    /// <param name="offset">Byte offset in the input.</param>
    public XmlSyntaxException(string limitName, long limit, long actual, long offset)
        : base($"{limitName} is {limit}; the input needs {actual}.")
    {
        Code = XmlErrorCode.LimitExceeded;
        Offset = offset;
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>Gets what was wrong.</summary>
    public XmlErrorCode Code { get; }

    /// <summary>Gets the byte offset of the problem in the input, counted from the start of the whole input (not of one block). -1 if unknown.</summary>
    public long Offset { get; }

    /// <summary>Gets the name of the exceeded <see cref="XmlLimits"/> property, or null when <see cref="Code"/> is not <see cref="XmlErrorCode.LimitExceeded"/>.</summary>
    public string? LimitName { get; }

    /// <summary>Gets the exceeded limit's value (0 when not a limit error).</summary>
    public long Limit { get; }

    /// <summary>Gets the value the input asked for (0 when not a limit error).</summary>
    public long Actual { get; }

    /// <summary>Gets the stable defect code for <see cref="Code"/>, such as <c>xml.mismatched-end-tag</c>.</summary>
    public string CodeName => CodeNameOf(Code);

    /// <summary>Gets the stable defect code for an error code.</summary>
    /// <param name="code">The error code.</param>
    /// <returns>A code of the form <c>xml.problem</c>.</returns>
    public static string CodeNameOf(XmlErrorCode code) => code switch
    {
        XmlErrorCode.UnexpectedEnd => "xml.unexpected-end",
        XmlErrorCode.DtdRefused => "xml.dtd-refused",
        XmlErrorCode.InvalidCharacter => "xml.invalid-character",
        XmlErrorCode.InvalidEncoding => "xml.invalid-encoding",
        XmlErrorCode.UnsupportedEncoding => "xml.unsupported-encoding",
        XmlErrorCode.InvalidName => "xml.invalid-name",
        XmlErrorCode.MismatchedEndTag => "xml.mismatched-end-tag",
        XmlErrorCode.DuplicateAttribute => "xml.duplicate-attribute",
        XmlErrorCode.UnboundPrefix => "xml.unbound-prefix",
        XmlErrorCode.InvalidNamespaceDeclaration => "xml.invalid-namespace-declaration",
        XmlErrorCode.InvalidReference => "xml.invalid-reference",
        XmlErrorCode.MisplacedContent => "xml.misplaced-content",
        XmlErrorCode.NoRoot => "xml.no-root",
        XmlErrorCode.InvalidDeclaration => "xml.invalid-declaration",
        XmlErrorCode.LimitExceeded => "limit.xml",
        _ => "xml.syntax",
    };
}
