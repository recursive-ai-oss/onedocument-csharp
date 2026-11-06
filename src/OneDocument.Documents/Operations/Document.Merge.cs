using RecursiveAI.OneDocument.Operations;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Copies the body of <paramref name="source"/> (or the range <see cref="MergeOptions.SourceStart"/> / <see cref="MergeOptions.SourceCount"/> of its body blocks) to the end of this document.
    /// </summary>
    /// <param name="source">The document to copy from. It is only read; it may be this document.</param>
    /// <param name="options">How conflicts are settled; null for the defaults (destination's styles, lists continue, destination's page setup).</param>
    /// <returns>What moved, each conflict with its resolution, what was lost on the way and what could not be done.</returns>
    /// <remarks>
    /// Everything the copied content refers to comes along and is renumbered so that nothing collides: styles (matched by name and type),
    /// list definitions (deduplicated by definition), bookmarks (renamed on collision, with the references inside the copied content),
    /// footnotes, endnotes and comments, revision, bookmark, content-control and drawing ids, and related parts such as pictures and
    /// headers (pictures with equal bytes are stored once). Parts nobody touched stay byte-identical; edited parts keep their unknown
    /// markup. Fonts, theme and document defaults stay the destination's and a loss item says when the source's differ. The result is
    /// deterministic.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The source range lies outside the source's body.</exception>
    /// <exception cref="OfficeUnsupportedException">One document uses the Strict namespaces and the other the Transitional ones.</exception>
    public MergeReport Append(Document source, MergeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureBodyElement();
        return new DocumentMerger(this, source, options ?? new MergeOptions()).Execute(DocumentMerger.BodyBlocks(Body.Container.Node).Count);
    }

    /// <summary>Copies the body of <paramref name="source"/> (or a range of it) into this document before body block <paramref name="blockIndex"/>.</summary>
    /// <param name="blockIndex">The index among this document's body blocks (the paragraphs, tables and content controls directly in the body); the block count appends.</param>
    /// <param name="source">The document to copy from.</param>
    /// <param name="options">How conflicts are settled; null for the defaults.</param>
    /// <returns>The report (see <see cref="Append(Document, MergeOptions?)"/>).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The position or the source range lies outside the bodies.</exception>
    /// <exception cref="OfficeUnsupportedException">One document uses the Strict namespaces and the other the Transitional ones.</exception>
    public MergeReport Insert(int blockIndex, Document source, MergeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureBodyElement();
        return new DocumentMerger(this, source, options ?? new MergeOptions()).Execute(blockIndex);
    }

    /// <summary>Makes sure the document has a <c>w:body</c> element and returns it.</summary>
    internal Markup.MarkupNode EnsureBodyElement()
    {
        if (Body.Container.IsValid)
        {
            return Body.Container.Node;
        }

        lock (Parts.Gate)
        {
            Markup.Node.AppendXml(System.Text.Encoding.UTF8.GetBytes("<w:body xmlns:w=\"" + WordprocessingNamespaceUri + "\"/>"));
            _body = null;
        }

        return Body.Container.Node;
    }

    /// <summary>Drops the lookups that cache what an operation changed behind the hub's back.</summary>
    internal void InvalidateAfterOperation()
    {
        lock (Parts.Gate)
        {
            _footnotes = null;
            _endnotes = null;
            _comments = null;
            _revisions = null;
        }

        Styles.Invalidate();
    }
}
