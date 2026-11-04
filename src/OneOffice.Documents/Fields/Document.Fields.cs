using RecursiveAI.OneOffice.Documents.Fields;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Lists the fields of the body, simple and complex, in the order their codes begin (outer before inner). A complex field
    /// that spans paragraphs or table cells is one field. A begin without an end, or an end without a begin, is reported
    /// (<c>docx.field-unbalanced</c>); fields nested deeper than the recursion limit are read as plain content
    /// (<c>docx.field-too-deep</c>). Nothing is evaluated.
    /// </summary>
    /// <returns>The fields; empty when the body is empty. The list is a snapshot: scan again after editing.</returns>
    public IReadOnlyList<Field> Fields() => FieldScanner.Scan(this, Body.Container, MainPartName);

    /// <summary>Lists the fields in a container of another part (a note, a comment, a header) or in a nested one.</summary>
    /// <param name="container">The container whose content is scanned.</param>
    /// <returns>The fields in document order.</returns>
    public IReadOnlyList<Field> Fields(BlockContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        MarkupDocument? doc = container.Container.IsValid ? container.Container.Node.Document : null;
        string part = (doc is null ? null : Parts.NameOf(doc)) ?? MainPartName;
        return FieldScanner.Scan(this, container.Container, part);
    }
}
