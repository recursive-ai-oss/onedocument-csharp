using RecursiveAI.OneOffice.TextExtraction;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>
/// Connects the document model to text extraction: list labels computed from the numbering definitions, in the form the streaming
/// extractor takes (<see cref="TextExtractionOptions.ListLabels"/>).
/// </summary>
public static class DocumentText
{
    /// <summary>
    /// Builds the list-label source for the main story of a document: for every numbered paragraph, its label and the separator that
    /// follows it (a tab, a space or nothing), found by where the paragraph starts in the main part.
    /// </summary>
    /// <param name="document">The document. Its main part must not have been edited since it was loaded or last committed, because
    /// paragraphs are matched by their byte offsets in the part as extraction will read it.</param>
    /// <returns>The source. Headers, footers and notes get no labels.</returns>
    /// <exception cref="InvalidOperationException">The document has uncommitted changes.</exception>
    public static IListLabelSource ListLabels(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.HasUncommittedChanges)
        {
            throw new InvalidOperationException("Commit the document's changes first: labels are matched to paragraphs by their offsets in the stored part.");
        }

        var byOffset = new Dictionary<long, string>();
        foreach (Numbering.ListLabel label in document.Numbering.LabelBody())
        {
            int start = label.Paragraph.Markup.Node.SourceStart;
            if (start < 0 || label.Text.Length == 0)
            {
                continue;
            }

            // ECMA-376 Part 1 §17.9.28: the suffix is what follows the label before the paragraph's text.
            byOffset[start] = label.Text + label.Suffix switch
            {
                "space" => " ",
                "nothing" => string.Empty,
                _ => "\t",
            };
        }

        return new Source(document.MainPartName, byOffset);
    }

    private sealed class Source(string partName, Dictionary<long, string> byOffset) : IListLabelSource
    {
        public string? LabelFor(string name, long paragraphOffset) =>
            string.Equals(name, partName, StringComparison.OrdinalIgnoreCase) ? byOffset.GetValueOrDefault(paragraphOffset) : null;
    }
}
