using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Gets what a conversion on open could not carry over, or null when the document was opened from a native OOXML package.
    /// A Word 2003 XML file opens as a document by conversion; this lists every feature it dropped or flattened.
    /// </summary>
    public LossReport? ImportLoss { get; internal set; }
}
