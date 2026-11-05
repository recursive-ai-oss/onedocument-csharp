using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>
/// Connects the document model to text extraction: list labels computed from the numbering definitions, in the form the streaming
/// extractor takes (<see cref="TextExtractionOptions.ListLabels"/>).
/// </summary>
public static class DocumentText
{
    /// <summary>
    /// Builds the list-label source for every story of a document: for every numbered paragraph of the body, headers, footers,
    /// footnotes, endnotes, comments and text boxes, its label (counted as Word does, see
    /// <see cref="Numbering.NumberingDefinitions.LabelStories"/>) and the separator that follows it (a tab, a space or nothing), found by
    /// the part it is in and where it starts in that part.
    /// </summary>
    /// <param name="document">The document. Its parts must not have been edited since they were loaded or last committed, because
    /// paragraphs are matched by their byte offsets in the part as extraction will read it.</param>
    /// <returns>The source.</returns>
    /// <exception cref="InvalidOperationException">The document has uncommitted changes.</exception>
    public static IListLabelSource ListLabels(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.HasUncommittedChanges)
        {
            throw new InvalidOperationException("Commit the document's changes first: labels are matched to paragraphs by their offsets in the stored part.");
        }

        var byPart = new Dictionary<string, Dictionary<long, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Numbering.StoryLabels story in document.Numbering.LabelStories())
        {
            foreach (Numbering.ListLabel label in story.Labels)
            {
                int start = label.Paragraph.Markup.Node.SourceStart;
                if (start < 0 || label.Text.Length == 0 || document.Parts.PartNameOf(label.Paragraph.Markup.Node) is not { } part)
                {
                    continue;
                }

                if (!byPart.TryGetValue(part, out Dictionary<long, string>? offsets))
                {
                    byPart[part] = offsets = [];
                }

                // ECMA-376 Part 1 §17.9.28: the suffix is what follows the label before the paragraph's text.
                offsets[start] = label.Text + label.Suffix switch
                {
                    "space" => " ",
                    "nothing" => string.Empty,
                    _ => "\t",
                };
            }
        }

        return new Source(byPart);
    }

    private sealed class Source(Dictionary<string, Dictionary<long, string>> byPart) : IListLabelSource
    {
        public string? LabelFor(string name, long paragraphOffset) =>
            byPart.TryGetValue(name, out Dictionary<long, string>? offsets) ? offsets.GetValueOrDefault(paragraphOffset) : null;
    }
}
