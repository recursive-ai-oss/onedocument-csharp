using OneFont;
using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>A rectangle in points. The origin is the top-left corner of the page and y grows downward.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width, 0 or more.</param>
/// <param name="Height">Height, 0 or more.</param>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    /// <summary>Gets the right edge.</summary>
    public double Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public double Bottom => Y + Height;
}

/// <summary>The kinds of node in the layout tree.</summary>
public enum LayoutNodeKind
{
    /// <summary>A page: the root of the tree below <see cref="LayoutDocument"/>.</summary>
    Page = 0,

    /// <summary>A laid-out paragraph, or the part of one that sits on this page.</summary>
    Paragraph = 1,

    /// <summary>One line of a paragraph.</summary>
    Line = 2,

    /// <summary>A stretch of text in one font on a line, a tab, or a list label.</summary>
    Run = 3,

    /// <summary>A table, or the part of one that sits on this page (or in this cell, for a nested table).</summary>
    Table = 4,

    /// <summary>A row of a table, or the part of a row that sits on this page.</summary>
    TableRow = 5,

    /// <summary>A cell of a table row.</summary>
    TableCell = 6,
}

/// <summary>
/// A node of the layout tree. The tree is pages, then blocks (paragraphs for now; tables, floating objects and notes join them as
/// those are laid out), then lines, then runs. Every position is in points on the page; the tree is immutable once layout returns.
/// </summary>
public abstract class LayoutNode
{
    private readonly List<LayoutNode> _children = [];

    internal LayoutNode(LayoutNodeKind kind)
    {
        Kind = kind;
    }

    /// <summary>Gets what the node is.</summary>
    public LayoutNodeKind Kind { get; }

    /// <summary>Gets the node's box in page coordinates, in points.</summary>
    public LayoutRect Bounds { get; internal set; }

    /// <summary>Gets the parent node; null for a page.</summary>
    public LayoutNode? Parent { get; private set; }

    /// <summary>Gets the child nodes in layout order (for a line: in visual left-to-right order).</summary>
    public IReadOnlyList<LayoutNode> Children => _children;

    internal void Add(LayoutNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }
}

/// <summary>One page of the laid-out document.</summary>
public sealed class LayoutPage : LayoutNode
{
    internal LayoutPage(int number, int sectionIndex, double width, double height, LayoutRect content)
        : base(LayoutNodeKind.Page)
    {
        Number = number;
        SectionIndex = sectionIndex;
        Bounds = new LayoutRect(0, 0, width, height);
        ContentArea = content;
    }

    /// <summary>Gets the page number counted from 1 across the document.</summary>
    public int Number { get; }

    /// <summary>Gets the index of the section whose page size and margins this page uses.</summary>
    public int SectionIndex { get; }

    /// <summary>Gets the text area inside the margins, in points.</summary>
    public LayoutRect ContentArea { get; }

    /// <summary>Gets the page width in points.</summary>
    public double Width => Bounds.Width;

    /// <summary>Gets the page height in points.</summary>
    public double Height => Bounds.Height;

    /// <summary>Gets the paragraphs on the page in order.</summary>
    public IEnumerable<LayoutParagraph> Paragraphs => Children.OfType<LayoutParagraph>();

    /// <summary>Gets the tables on the page in order (the part of each that sits on this page). Paragraphs inside cells are under their cells, not here.</summary>
    public IEnumerable<LayoutTable> Tables => Children.OfType<LayoutTable>();
}

/// <summary>A paragraph on a page. A paragraph that breaks across pages has one <see cref="LayoutParagraph"/> per page.</summary>
public sealed class LayoutParagraph : LayoutNode
{
    internal LayoutParagraph(Paragraph source, string text)
        : base(LayoutNodeKind.Paragraph)
    {
        Source = source;
        Text = text;
    }

    /// <summary>Gets the hub paragraph this block was laid out from.</summary>
    public Paragraph Source { get; }

    /// <summary>
    /// Gets the paragraph's visible text as laid out: hidden text removed, <c>\t</c> for a tab, <c>\n</c> for a line break. Offsets in
    /// <see cref="LayoutLine"/> and <see cref="LayoutRun"/> index this string. It is the same string on every page of a split paragraph.
    /// </summary>
    public string Text { get; }

    /// <summary>Gets the list label text (for example <c>1.</c> or a bullet), or null when the paragraph is not numbered.</summary>
    public string? Label { get; internal set; }

    /// <summary>Gets the heading level 1 to 6 from the outline level or a heading style, or 0 for body text.</summary>
    public int HeadingLevel { get; internal set; }

    /// <summary>Gets whether the paragraph is right to left (<c>w:bidi</c>).</summary>
    public bool IsRightToLeft { get; internal set; }

    /// <summary>Gets whether this block continues a paragraph that started on an earlier page.</summary>
    public bool ContinuesFromPreviousPage { get; internal set; }

    /// <summary>Gets whether this block continues on the next page.</summary>
    public bool ContinuesOnNextPage { get; internal set; }

    /// <summary>Gets the lines of this block in order.</summary>
    public IEnumerable<LayoutLine> Lines => Children.OfType<LayoutLine>();

    internal ParagraphPaintInfo Paint { get; set; } = new();
}

/// <summary>One line of a paragraph.</summary>
public sealed class LayoutLine : LayoutNode
{
    internal LayoutLine()
        : base(LayoutNodeKind.Line)
    {
    }

    /// <summary>Gets the baseline's y in page coordinates, in points.</summary>
    public double Baseline { get; internal set; }

    /// <summary>Gets the height above the baseline that the line's tallest content needs, in points.</summary>
    public double Ascent { get; internal set; }

    /// <summary>Gets the height below the baseline that the line's deepest content needs, in points.</summary>
    public double Descent { get; internal set; }

    /// <summary>Gets the offset in <see cref="LayoutParagraph.Text"/> of the line's first character.</summary>
    public int TextStart { get; internal set; }

    /// <summary>Gets the number of characters of <see cref="LayoutParagraph.Text"/> on the line, including trailing spaces and a line break.</summary>
    public int TextLength { get; internal set; }

    /// <summary>Gets the line's text, including trailing spaces and a line-break character, in logical order.</summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>Gets whether this is the last line of the paragraph.</summary>
    public bool IsLastLine { get; internal set; }

    /// <summary>Gets the runs on the line in visual left-to-right order.</summary>
    public IEnumerable<LayoutRun> Runs => Children.OfType<LayoutRun>();
}

/// <summary>What a <see cref="LayoutRun"/> holds.</summary>
public enum LayoutRunKind
{
    /// <summary>Text in one font.</summary>
    Text = 0,

    /// <summary>The space a tab character takes, possibly with a leader.</summary>
    Tab = 1,

    /// <summary>The list label text of a numbered paragraph.</summary>
    Label = 2,
}

/// <summary>A stretch of text with one font, size and direction on a line, or a tab or list label.</summary>
public sealed class LayoutRun : LayoutNode
{
    internal LayoutRun(LayoutRunKind runKind)
        : base(LayoutNodeKind.Run)
    {
        RunKind = runKind;
    }

    /// <summary>Gets what the run holds.</summary>
    public LayoutRunKind RunKind { get; }

    /// <summary>Gets the run's characters in logical order. For a label run this is the label text; for a tab run it is a tab character.</summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>Gets the offset of the run's first character in <see cref="LayoutParagraph.Text"/>; -1 for a label.</summary>
    public int TextStart { get; internal set; }

    /// <summary>Gets the hub run the characters came from, or null for a label.</summary>
    public Run? Source { get; internal set; }

    /// <summary>Gets the font chosen for the run, with how closely it matches the one the document names.</summary>
    public FontMatch? Font { get; internal set; }

    /// <summary>Gets the font size in points after superscript/subscript and small-caps scaling.</summary>
    public double FontSize { get; internal set; }

    /// <summary>Gets the bidi embedding level (even is left to right).</summary>
    public int BidiLevel { get; internal set; }

    /// <summary>Gets the run's advance width in points.</summary>
    public double Width => Bounds.Width;

    /// <summary>Gets the character offsets of the start of each glyph cluster, relative to the run's text, and one more for the end. Empty for tabs.</summary>
    public IReadOnlyList<int> ClusterStarts { get; internal set; } = [];

    /// <summary>Gets each cluster's left x in page coordinates followed by the right edge; same count as <see cref="ClusterStarts"/>. Clusters of a right-to-left run run right to left.</summary>
    public IReadOnlyList<double> ClusterX { get; internal set; } = [];

    internal RunPaintInfo Paint { get; set; } = new();

    internal GlyphData? Glyphs { get; set; }
}

/// <summary>The result of laying out a document.</summary>
public sealed class LayoutDocument
{
    internal LayoutDocument(IReadOnlyList<LayoutPage> pages, IReadOnlyList<FontSubstitutionNote> fontNotes, IReadOnlyList<LayoutNote> notes, int compatibilityMode)
    {
        Pages = pages;
        FontNotes = fontNotes;
        Notes = notes;
        CompatibilityMode = compatibilityMode;
    }

    /// <summary>Gets the pages in order.</summary>
    public IReadOnlyList<LayoutPage> Pages { get; }

    /// <summary>Gets one note for every font family that was replaced by another, never silent.</summary>
    public IReadOnlyList<FontSubstitutionNote> FontNotes { get; }

    /// <summary>Gets what the layout skipped or approximated (for example floating objects, which a later layout stage handles).</summary>
    public IReadOnlyList<LayoutNote> Notes { get; }

    /// <summary>Gets the compatibility mode the layout followed (11 to 15), from <c>w:compatSetting compatibilityMode</c>; 12 when the document names none.</summary>
    public int CompatibilityMode { get; }
}

/// <summary>Something the layout could not do exactly, with where in the document it happened.</summary>
/// <param name="Code">A stable code of the form <c>layout.problem</c>.</param>
/// <param name="Message">A one-line description.</param>
/// <param name="Source">The paragraph or block concerned, or null.</param>
public sealed record LayoutNote(string Code, string Message, BlockElement? Source);

/// <summary>Options for laying a document out.</summary>
public sealed class LayoutOptions
{
    /// <summary>Gets or sets the fonts to use; null creates <see cref="FontLibrary.CreateDefault"/> with the document's embedded fonts added.</summary>
    public IFontProvider? Fonts { get; set; }

    /// <summary>Gets or sets whether the fonts embedded in the document are used when <see cref="Fonts"/> is null. Default true.</summary>
    public bool UseEmbeddedFonts { get; set; } = true;

    /// <summary>Gets or sets the most pages to lay out; layout stops there and adds a note. Default 100,000.</summary>
    public int MaxPages { get; set; } = 100_000;
}
