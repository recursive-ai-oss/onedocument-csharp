using System.Text;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>What a node of the Markdown tree is.</summary>
internal enum MdKind
{
    // Blocks.
    Document,
    BlockQuote,
    List,
    Item,
    CodeBlock,
    HtmlBlock,
    Paragraph,
    Heading,
    ThematicBreak,
    Table,
    TableRow,
    TableCell,
    FootnoteDefinition,

    // Inlines.
    Text,
    SoftBreak,
    LineBreak,
    Code,
    HtmlInline,
    Emph,
    Strong,
    Strike,
    Link,
    Image,
    FootnoteReference,
}

/// <summary>
/// One node of the parsed tree: a document of blocks, each holding blocks or inlines. Parent, child and sibling links are fields so that
/// the parser can move runs of siblings under a new parent without copying. The fields after <see cref="Task"/> are parser state.
/// </summary>
internal sealed class MdNode(MdKind kind)
{
    public MdKind Kind { get; set; } = kind;

    public MdNode? Parent { get; private set; }

    public MdNode? FirstChild { get; private set; }

    public MdNode? LastChild { get; private set; }

    public MdNode? Next { get; private set; }

    public MdNode? Prev { get; private set; }

    /// <summary>The text of a text, code, code block, raw HTML or autolink node.</summary>
    public string Literal { get; set; } = string.Empty;

    /// <summary>The target of a link or image, with backslash escapes and entities already resolved (not percent-encoded).</summary>
    public string Destination { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>The info string of a fenced code block.</summary>
    public string Info { get; set; } = string.Empty;

    /// <summary>Heading level 1 to 6.</summary>
    public int Level { get; set; }

    /// <summary>For a list: false when any item is separated from the next by a blank line.</summary>
    public bool Tight { get; set; } = true;

    public bool Ordered { get; set; }

    /// <summary>The number the first item of an ordered list carries.</summary>
    public int Start { get; set; }

    /// <summary>The bullet character or the ordered-list delimiter ('.' or ')').</summary>
    public char Marker { get; set; }

    /// <summary>For a list item: 0 for a plain item, 1 for an unchecked task, 2 for a checked task.</summary>
    public int Task { get; set; }

    /// <summary>For a table cell: 0 no alignment, 1 left, 2 centre, 3 right.</summary>
    public int Align { get; set; }

    public bool IsHeader { get; set; }

    /// <summary>The label of a footnote definition or reference.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The 1-based line a block started on.</summary>
    public int StartLine { get; set; }

    // ---- parser state ------------------------------------------------------------------------------------------------------

    internal bool Open { get; set; } = true;

    internal bool LastLineBlank { get; set; }

    internal bool LastLineChecked { get; set; }

    internal StringBuilder? Content { get; set; }

    internal string? Text { get; set; }

    internal bool Fenced { get; set; }

    internal char FenceChar { get; set; }

    internal int FenceLength { get; set; }

    internal int FenceOffset { get; set; }

    internal int HtmlType { get; set; }

    internal int MarkerOffset { get; set; }

    internal int Padding { get; set; }

    internal int Depth { get; set; }

    public void AppendChild(MdNode child)
    {
        child.Unlink();
        child.Parent = this;
        if (LastChild is { } last)
        {
            last.Next = child;
            child.Prev = last;
        }
        else
        {
            FirstChild = child;
        }

        LastChild = child;
    }

    public void InsertAfter(MdNode sibling)
    {
        sibling.Unlink();
        sibling.Prev = this;
        if (Next is { } n)
        {
            sibling.Next = n;
            n.Prev = sibling;
        }
        else
        {
            Parent!.LastChild = sibling;
        }

        sibling.Parent = Parent;
        Next = sibling;
    }

    public void Unlink()
    {
        if (Prev is { } p)
        {
            p.Next = Next;
        }
        else if (Parent is { } par)
        {
            par.FirstChild = Next;
        }

        if (Next is { } n)
        {
            n.Prev = Prev;
        }
        else if (Parent is { } par2)
        {
            par2.LastChild = Prev;
        }

        Parent = null;
        Next = null;
        Prev = null;
    }

    public IEnumerable<MdNode> Children()
    {
        for (MdNode? c = FirstChild; c is not null; c = c.Next)
        {
            yield return c;
        }
    }

    internal bool IsContainer => Kind is MdKind.Document or MdKind.BlockQuote or MdKind.List or MdKind.Item or MdKind.FootnoteDefinition;
}
