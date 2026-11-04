using System.Globalization;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Documents.Revisions;

/// <summary>
/// One tracked change found in a part. A revision is a snapshot of the markup at the time of the scan: after any accept, reject
/// or authoring call, read <see cref="RevisionSet.Revisions"/> again, because the elements it points to may have been replaced.
/// </summary>
public sealed class Revision
{
    internal Revision(IRevisionPart part, MarkupNode element, RevisionKind kind, RevisionScope scope, int id, string author, DateTimeOffset? date)
    {
        Part = part;
        Element = element;
        Kind = kind;
        Scope = scope;
        Id = id;
        Author = author;
        Date = date;
    }

    /// <summary>Gets the part the revision is in.</summary>
    public IRevisionPart Part { get; }

    /// <summary>Gets the revision element: <c>w:ins</c>, <c>w:del</c>, <c>w:rPrChange</c> and so on. It is a markup node of <see cref="IRevisionPart.Markup"/>.</summary>
    public MarkupNode Element { get; }

    /// <summary>Gets what the revision does.</summary>
    public RevisionKind Kind { get; }

    /// <summary>Gets what the revision applies to.</summary>
    public RevisionScope Scope { get; }

    /// <summary>Gets the revision's <c>w:id</c>, or -1 when it is missing or not a number.</summary>
    public int Id { get; }

    /// <summary>Gets the author (<c>w:author</c>); empty when the attribute is missing.</summary>
    public string Author { get; }

    /// <summary>Gets the date (<c>w:date</c>) in UTC when the attribute is present and valid; a date without a zone is taken as UTC.</summary>
    public DateTimeOffset? Date { get; }

    /// <summary>For moves, the name that pairs the source and the destination (<c>w:name</c> of the range marker next to the element); otherwise null.</summary>
    public string? MoveName { get; internal set; }

    /// <summary>Gets the byte offset of the revision element in its part, as the part was parsed (or -1 for an element added since).</summary>
    public int Offset => Element.SourceStart;

    /// <summary>Gets the paragraph the revision sits in or on (the paragraph whose mark it is, for mark revisions), or a null node for revisions outside paragraphs such as table rows.</summary>
    public MarkupNode Paragraph
    {
        get
        {
            MarkupNode p = Wml.Ancestor(Element, "p");
            return p;
        }
    }

    /// <summary>
    /// Gets the text the revision covers: for insertions and moves the inserted or moved text, for deletions the deleted text
    /// (tabs and line breaks as <c>\t</c> and <c>\n</c>); empty for revisions that carry no text of their own.
    /// </summary>
    public string Text => Scope == RevisionScope.Content ? RevisionText.Collect(Element) : string.Empty;

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Kind} #{Id} by '{Author}'");

    internal static DateTimeOffset? ParseDate(string? text)
    {
        if (text is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset d) ? d : null;
    }
}
