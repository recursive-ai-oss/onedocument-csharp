namespace RecursiveAI.OneDocument.Layout;

/// <summary>What a note area block or a note paragraph is.</summary>
public enum LayoutNoteRole
{
    /// <summary>A footnote (or the part of one) at the bottom of a page or column, or beneath the text.</summary>
    Footnote = 0,

    /// <summary>An endnote, laid out in the flow at the end of its section or of the document.</summary>
    Endnote = 1,

    /// <summary>The separator that precedes the notes (<c>w:separator</c>).</summary>
    Separator = 2,

    /// <summary>The separator that precedes notes continued from the previous page (<c>w:continuationSeparator</c>).</summary>
    ContinuationSeparator = 3,

    /// <summary>The text printed after a note that continues on the next page (<c>w:continuationNotice</c>).</summary>
    ContinuationNotice = 4,
}

/// <summary>
/// The note area of one column of a page: a footnote or the part of a footnote that sits on this page, or a separator or continuation
/// notice. Its children are the note's <see cref="LayoutParagraph"/>s. A note that is too long for its page has one block per page,
/// with <see cref="ContinuesOnNextPage"/> and <see cref="ContinuesFromPreviousPage"/> set.
/// </summary>
public sealed class LayoutNoteBlock : LayoutNode
{
    internal LayoutNoteBlock(LayoutNoteRole role)
        : base(LayoutNodeKind.NoteBlock)
    {
        Role = role;
    }

    /// <summary>Gets what the block is.</summary>
    public LayoutNoteRole Role { get; }

    /// <summary>Gets the note's <c>w:id</c>; the separator notes' ids for separator blocks.</summary>
    public long NoteId { get; internal set; }

    /// <summary>Gets the mark the note's reference shows (its number in the note's number format, or empty for a custom mark).</summary>
    public string Mark { get; internal set; } = string.Empty;

    /// <summary>Gets the 0-based column of the page the notes belong to (0 for a single column).</summary>
    public int Column { get; internal set; }

    /// <summary>Gets whether this block continues a note that started on an earlier page.</summary>
    public bool ContinuesFromPreviousPage { get; internal set; }

    /// <summary>Gets whether the note continues on the next page.</summary>
    public bool ContinuesOnNextPage { get; internal set; }

    /// <summary>Gets the paragraphs of the block in order.</summary>
    public IEnumerable<LayoutParagraph> Paragraphs => Children.OfType<LayoutParagraph>();
}
