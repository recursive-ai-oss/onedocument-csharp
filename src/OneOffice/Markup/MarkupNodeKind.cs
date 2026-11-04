namespace RecursiveAI.OneOffice.Markup;

/// <summary>The kind of a node in a <see cref="MarkupDocument"/>.</summary>
public enum MarkupNodeKind : byte
{
    /// <summary>No node (the default value of <see cref="MarkupNode"/>).</summary>
    None = 0,

    /// <summary>The document itself: the parent of the XML declaration, the root element and the comments and whitespace around it.</summary>
    Document,

    /// <summary>The XML declaration.</summary>
    XmlDeclaration,

    /// <summary>An element.</summary>
    Element,

    /// <summary>Character data with something other than whitespace in it.</summary>
    Text,

    /// <summary>Character data made only of whitespace. It is kept as a node so that formatting round-trips.</summary>
    Whitespace,

    /// <summary>A CDATA section.</summary>
    CData,

    /// <summary>A comment.</summary>
    Comment,

    /// <summary>A processing instruction.</summary>
    ProcessingInstruction,
}
