namespace RecursiveAI.OneOffice.Markup.Xml;

/// <summary>The kind of the token an <see cref="XmlTokenizer"/> is positioned on.</summary>
public enum XmlTokenKind : byte
{
    /// <summary>No token has been read yet, or the end of the document was reached.</summary>
    None = 0,

    /// <summary>The XML declaration, <c>&lt;?xml version="1.0" ...?&gt;</c>. Only ever the first token of a document.</summary>
    XmlDeclaration,

    /// <summary>A start tag, <c>&lt;a b="c"&gt;</c>.</summary>
    StartElement,

    /// <summary>An empty-element tag, <c>&lt;a b="c"/&gt;</c>. It has no matching <see cref="EndElement"/> token.</summary>
    EmptyElement,

    /// <summary>An end tag, <c>&lt;/a&gt;</c>.</summary>
    EndElement,

    /// <summary>Character data that contains at least one character other than space, tab, line feed and carriage return.</summary>
    Text,

    /// <summary>Character data made only of space, tab, line feed and carriage return. Outside the root element this is the only character data allowed.</summary>
    Whitespace,

    /// <summary>A CDATA section, <c>&lt;![CDATA[...]]&gt;</c>.</summary>
    CData,

    /// <summary>A comment, <c>&lt;!-- ... --&gt;</c>.</summary>
    Comment,

    /// <summary>A processing instruction other than the XML declaration, <c>&lt;?target data?&gt;</c>.</summary>
    ProcessingInstruction,
}
