using RecursiveAI.OneDocument.Documents.ContentControls;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class BlockContentControl
{
    /// <summary>Gets the property and content model of the control.</summary>
    public ContentControl Control => new(Owner, Markup, null);
}

public sealed partial class InlineContentControl
{
    /// <summary>Gets the property and content model of the control.</summary>
    public ContentControl Control => new(Owner, Markup, null);
}
