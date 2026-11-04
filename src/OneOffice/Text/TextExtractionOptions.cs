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
    /// Gets or sets where list labels (<c>1.</c>, <c>a)</c>, bullets) come from. The default, null, leaves labels out. The numbering
    /// model supplies this when it is available.
    /// </summary>
    internal IListLabelSource? ListLabels { get; init; }
}

/// <summary>Supplies the label a numbered or bulleted paragraph shows, in document order.</summary>
internal interface IListLabelSource
{
    /// <summary>Gets the label of the next paragraph of a list, counting it.</summary>
    /// <param name="numId">The numbering instance the paragraph names directly, or -1 when it names none (its style may).</param>
    /// <param name="level">The list level, 0 to 8.</param>
    /// <param name="styleId">The paragraph's style id, or null.</param>
    /// <returns>The label text, or null when the paragraph shows none.</returns>
    string? NextLabel(int numId, int level, string? styleId);
}
