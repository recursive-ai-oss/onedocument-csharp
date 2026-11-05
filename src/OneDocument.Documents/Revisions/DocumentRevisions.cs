using System.Linq;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using Rev = RecursiveAI.OneDocument.Documents.Revisions;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    private Rev.RevisionSet? _revisions;

    /// <summary>
    /// Gets the tracked changes of the document: those of the main part, headers, footers, footnotes, endnotes and comments, and of
    /// the style and numbering definitions. The set is read on first use and edits through it go straight into the parts' trees
    /// (<see cref="CommitChanges"/> writes them back; parts without revisions are left alone). After editing a paragraph by other
    /// means call <see cref="Rev.RevisionSet.Refresh"/>.
    /// </summary>
    public Rev.RevisionSet Revisions => Init(ref _revisions, () => Rev.RevisionSet.Scan(RevisionParts(), Context));

    private List<Rev.IRevisionPart> RevisionParts()
    {
        var parts = new List<Rev.IRevisionPart> { new Rev.RevisionPart(MainPartName, MainMarkup) };
        string[] names = ["header", "footer", "footnotes", "endnotes", "comments", "styles", "numbering"];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MainPartName };
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (r.TargetMode != RelationshipTargetMode.Internal || !names.Any(n => RelationshipTypes.Is(r.Type, n)))
            {
                continue;
            }

            if (PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && seen.Add(uri.Value)
                && Package.FindPart(uri.Value) is { } part && Parts.Load(part.Name, required: false) is { } markup)
            {
                parts.Add(new Rev.RevisionPart(part.Name, markup));
            }
        }

        return parts;
    }
}

public sealed partial class Paragraph
{
    /// <summary>Gets the paragraph's text as if every tracked change were accepted or every one rejected, without its paragraph break.</summary>
    /// <param name="view">Which version to read.</param>
    /// <returns>The text; tabs and line breaks as <c>\t</c> and <c>\n</c>.</returns>
    public string GetText(Rev.RevisionView view) => Rev.RevisionSet.GetParagraphText(Markup.Node, view);
}
