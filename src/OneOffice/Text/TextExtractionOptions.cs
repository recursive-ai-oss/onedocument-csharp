namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>
/// What the OOXML text extractors include. The defaults give the text a reader sees on screen with revisions shown as accepted, plus the
/// stories a search index wants (headers, footers, notes and comments).
/// </summary>
public sealed class TextExtractionOptions
{
    /// <summary>Gets the options with every default.</summary>
    public static TextExtractionOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets whether text a tracked change deleted (or moved away) is included. The default is false: deleted text is left out,
    /// and a paragraph whose mark was deleted joins the paragraph after it, as it does once the deletion is accepted
    /// (ECMA-376 Part 1 §17.13.5).
    /// </summary>
    public bool IncludeDeletedText { get; init; }

    /// <summary>Gets or sets whether headers and footers of a word-processing document are included. The default is true.</summary>
    public bool IncludeHeadersAndFooters { get; init; } = true;

    /// <summary>Gets or sets whether footnotes and endnotes (word-processing) and speaker notes (presentations) are included. The default is true.</summary>
    public bool IncludeNotes { get; init; } = true;

    /// <summary>Gets or sets whether comments of a word-processing document are included. The default is true.</summary>
    public bool IncludeComments { get; init; } = true;

    /// <summary>
    /// Gets or sets where list labels (<c>1.</c>, <c>a)</c>, bullets) come from. The default, null, leaves labels out: the numbering
    /// definitions are part of the document model, which streaming extraction does not build. The word-processing package supplies a
    /// source built from the model.
    /// </summary>
    public IListLabelSource? ListLabels { get; init; }
}

/// <summary>
/// Tells the text extractor the label a numbered or bulleted paragraph shows. Paragraphs are named by where they start, so the source
/// can be built from the document model without the extractor knowing about it.
/// </summary>
public interface IListLabelSource
{
    /// <summary>Gets the text to put before a paragraph: its label and the separator that follows it.</summary>
    /// <param name="partName">The part the paragraph is in, for example <c>/word/document.xml</c>.</param>
    /// <param name="paragraphOffset">The byte offset of the paragraph's start tag in the uncompressed part.</param>
    /// <returns>The text, for example <c>"2.\t"</c>; null or empty for a paragraph that is not numbered.</returns>
    string? LabelFor(string partName, long paragraphOffset);
}
