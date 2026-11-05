using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Revisions;

/// <summary>
/// One XML part of a document that can carry tracked changes: the main document, a header, a footer, the footnotes or endnotes,
/// the comments, or the numbering and style definitions. The revision model works on the part's markup tree and nothing else, so
/// any owner of parts can hand them over.
/// </summary>
public interface IRevisionPart
{
    /// <summary>Gets the part's URI, as used in defect locations and revision reports.</summary>
    string Uri { get; }

    /// <summary>Gets the part's markup tree. Edits go straight into it.</summary>
    MarkupDocument Markup { get; }
}

/// <summary>A plain <see cref="IRevisionPart"/> over a part URI and its markup tree.</summary>
public sealed class RevisionPart : IRevisionPart
{
    /// <summary>Creates a part.</summary>
    /// <param name="uri">The part URI.</param>
    /// <param name="markup">The part's markup tree.</param>
    public RevisionPart(string uri, MarkupDocument markup)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(markup);
        Uri = uri;
        Markup = markup;
    }

    /// <inheritdoc />
    public string Uri { get; }

    /// <inheritdoc />
    public MarkupDocument Markup { get; }
}
