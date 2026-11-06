namespace RecursiveAI.OneDocument.Operations;

/// <summary>
/// Where a piece of found text sits. Which fields are filled depends on the kind: a document gives the part and the paragraph, a workbook
/// the sheet and the cell, a presentation the slide part and the paragraph.
/// </summary>
/// <param name="Container">The part URI (documents, presentations) or the sheet name (workbooks).</param>
/// <param name="Story">What the text belongs to: <c>body</c>, <c>header</c>, <c>footer</c>, <c>footnotes</c>, <c>endnotes</c>, <c>comments</c>, <c>textbox</c>, <c>field-code</c>; <c>cell</c>, <c>formula</c>; <c>slide</c>, <c>notes</c>, <c>layout</c>, <c>master</c>, <c>notes-master</c>.</param>
/// <param name="Index">The 0-based index of the paragraph within the part (documents, presentations); 0 for cells.</param>
/// <param name="Offset">The UTF-16 offset of the match in the text of that paragraph or cell.</param>
/// <param name="Address">The A1 address for a cell, otherwise null.</param>
public readonly record struct TextLocation(string Container, string Story, int Index, int Offset, string? Address = null);

/// <summary>A piece of text a search found.</summary>
/// <param name="Location">Where it is.</param>
/// <param name="Text">The matched text.</param>
public sealed record TextHit(TextLocation Location, string Text);

/// <summary>One replacement an operation made.</summary>
/// <param name="Location">Where the match was, in the text before the operation.</param>
/// <param name="Original">The text that was replaced.</param>
/// <param name="Replacement">The text that took its place.</param>
/// <param name="Tracked">True when the change was written as a tracked deletion and insertion.</param>
public sealed record ReplacementItem(TextLocation Location, string Original, string Replacement, bool Tracked);

/// <summary>A match an operation found but did not change, with the reason.</summary>
/// <param name="Code">A stable code: <c>formula-invalid</c> (the new formula text does not parse), <c>formula-shared</c> (a shared formula's text belongs to other cells too), <c>formula-too-long</c>, <c>markup-unsupported</c> (the markup around the text cannot be edited safely), <c>rich-text-unsupported</c>.</param>
/// <param name="Message">What happened, in English.</param>
/// <param name="Location">Where the match was.</param>
public sealed record ReplaceIssue(string Code, string Message, TextLocation Location);

/// <summary>
/// What a replace operation did: every replacement with its location, and every match it left alone with the reason. An operation
/// never returns a bare count.
/// </summary>
public sealed class ReplaceReport
{
    private readonly List<ReplacementItem> _items = [];
    private readonly List<ReplaceIssue> _issues = [];

    /// <summary>Gets the replacements made, in document order (parts in the order they were searched).</summary>
    public IReadOnlyList<ReplacementItem> Items => _items;

    /// <summary>Gets the matches that were found but not changed.</summary>
    public IReadOnlyList<ReplaceIssue> Issues => _issues;

    /// <summary>Gets the number of replacements made.</summary>
    public int Count => _items.Count;

    /// <summary>Records a replacement. Called by the operations of the kind packages as they work.</summary>
    /// <param name="item">The replacement.</param>
    public void Add(ReplacementItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    /// <summary>Records a match that was left alone. Called by the operations of the kind packages as they work.</summary>
    /// <param name="issue">The issue.</param>
    public void Add(ReplaceIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        _issues.Add(issue);
    }
}
