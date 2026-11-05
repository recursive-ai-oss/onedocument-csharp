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

    /// <summary>Page decoration that is not text flow: a page border side, a column separator, a line number or a watermark (<see cref="LayoutDecoration"/>).</summary>
    Decoration = 7,

    /// <summary>A table, or the part of one that sits on this page (or in this cell, for a nested table).</summary>
    Table = 4,

    /// <summary>A row of a table, or the part of a row that sits on this page.</summary>
    TableRow = 5,

    /// <summary>A cell of a table row.</summary>
    TableCell = 6,

    /// <summary>The notes of one column of a page: a footnote, a separator or a continuation notice (<see cref="LayoutNoteBlock"/>).</summary>
    NoteBlock = 8,
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

/// <summary>The part of a page a block belongs to.</summary>
public enum LayoutStory
{
    /// <summary>The body text.</summary>
    Body = 0,

    /// <summary>The page header; painted as an artifact.</summary>
    Header = 1,

    /// <summary>The page footer; painted as an artifact.</summary>
    Footer = 2,

    /// <summary>A footnote in the note area of a page.</summary>
    Footnote = 3,

    /// <summary>An endnote.</summary>
    Endnote = 4,
}

/// <summary>The columns of one stretch of a page, between two continuous section breaks (or the whole body).</summary>
/// <param name="Top">Top of the stretch in points.</param>
/// <param name="Bottom">Bottom of the stretch in points: where the columns end (balanced columns) or the body's bottom edge.</param>
/// <param name="Columns">The column rectangles in flow order (right to left in a right-to-left section).</param>
/// <param name="SectionIndex">The section these columns belong to.</param>
public sealed record LayoutColumnRegion(double Top, double Bottom, IReadOnlyList<LayoutRect> Columns, int SectionIndex);

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

    /// <summary>
    /// Gets the text area in points: inside the margins, and pushed in by a header or footer that is taller than its margin allows.
    /// A continuous section that changes the columns mid-page does not change it.
    /// </summary>
    public LayoutRect ContentArea { get; internal set; }

    /// <summary>Gets the page width in points.</summary>
    public double Width => Bounds.Width;

    /// <summary>Gets the page height in points.</summary>
    public double Height => Bounds.Height;

    /// <summary>Gets all paragraphs on the page in order: header, body, then footer.</summary>
    public IEnumerable<LayoutParagraph> Paragraphs => Children.OfType<LayoutParagraph>();

    /// <summary>Gets the body paragraphs (not those of the header or footer) in order.</summary>
    public IEnumerable<LayoutParagraph> BodyParagraphs => Paragraphs.Where(static p => p.Story == LayoutStory.Body);

    /// <summary>Gets the paragraphs of the page header in order.</summary>
    public IEnumerable<LayoutParagraph> HeaderParagraphs => Paragraphs.Where(static p => p.Story == LayoutStory.Header);

    /// <summary>Gets the paragraphs of the page footer in order.</summary>
    public IEnumerable<LayoutParagraph> FooterParagraphs => Paragraphs.Where(static p => p.Story == LayoutStory.Footer);

    /// <summary>Gets the decorations on the page: borders, column separators, line numbers and watermarks.</summary>
    public IEnumerable<LayoutDecoration> Decorations => Children.OfType<LayoutDecoration>();

    /// <summary>Gets the page number as it prints, after restarts (<c>w:pgNumType</c>); 0 until layout sets it.</summary>
    public long PrintedNumber { get; internal set; }

    /// <summary>Gets the printed number in the section's number format (<c>decimal</c>, <c>upperRoman</c>, …).</summary>
    public string PrintedNumberText { get; internal set; } = string.Empty;

    /// <summary>Gets whether the page was inserted to start a section on an odd or even page; it holds no body text.</summary>
    public bool IsBlank { get; internal set; }

    /// <summary>Gets whether this is the first page of its section.</summary>
    public bool IsFirstPageOfSection { get; internal set; }

    /// <summary>Gets the area the header text occupies, or null when the page has no header content.</summary>
    public LayoutRect? HeaderArea { get; internal set; }

    /// <summary>Gets the area the footer text occupies, or null when the page has no footer content.</summary>
    public LayoutRect? FooterArea { get; internal set; }

    /// <summary>
    /// Gets the text columns of the section the page started in, as rectangles that span the body area. A continuous section that
    /// changes the columns lower on the page adds its columns to <see cref="ColumnRegions"/>.
    /// </summary>
    public IReadOnlyList<LayoutRect> Columns => ColumnRegions.Count == 0 ? [ContentArea] : ColumnRegions[0].Columns;

    /// <summary>Gets the column layout of each stretch of the page, top to bottom (one per section on the page).</summary>
    public IReadOnlyList<LayoutColumnRegion> ColumnRegions { get; internal set; } = [];

    /// <summary>
    /// Gets the height of the footnote area at the bottom of the page: the tallest note area of any column, separator and continuation
    /// notice included. The body's bottom edge (<see cref="ContentArea"/>) is lowered by this much; it is 0 on a page without footnotes.
    /// </summary>
    public double NotesReservation { get; internal set; }

    /// <summary>Gets the note areas on the page, column by column: separators, footnotes (or parts of them) and continuation notices.</summary>
    public IEnumerable<LayoutNoteBlock> NoteBlocks => Children.OfType<LayoutNoteBlock>();

    /// <summary>Gets the footnote paragraphs on the page in order (separators and notices left out).</summary>
    public IEnumerable<LayoutParagraph> FootnoteParagraphs => NoteBlocks.Where(static b => b.Role == LayoutNoteRole.Footnote).SelectMany(static b => b.Paragraphs);

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

    /// <summary>Gets which story of the page the paragraph belongs to.</summary>
    public LayoutStory Story { get; internal set; }

    /// <summary>Gets the 0-based column of the page's column region the paragraph sits in (0 for a single column).</summary>
    public int Column { get; internal set; }

    /// <summary>Gets what kind of note text the paragraph is, or null for ordinary text. Endnote paragraphs flow in the body and carry <see cref="LayoutNoteRole.Endnote"/>.</summary>
    public LayoutNoteRole? NoteRole { get; internal set; }

    /// <summary>Gets the <c>w:id</c> of the note a note paragraph belongs to; 0 for ordinary text.</summary>
    public long NoteId { get; internal set; }

    /// <summary>Gets the lines of this block in order.</summary>
    public IEnumerable<LayoutLine> Lines => Children.OfType<LayoutLine>();

    internal ParagraphPaintInfo Paint { get; set; } = new();

    // Anchored drawings of the paragraph, painted at their own position on the page the paragraph starts on.
    internal List<InlineDrawing> Floats { get; } = [];
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

    /// <summary>An equation: <see cref="LayoutRun.Math"/> holds its glyphs and rules.</summary>
    Math = 4,

    /// <summary>An inline drawing or picture: one box on the baseline whose width is the run's width. Its text is the object replacement character U+FFFC.</summary>
    Drawing = 3,
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

    /// <summary>Gets the laid-out equation of a <see cref="LayoutRunKind.Math"/> run (positions relative to the line's baseline and the run's left edge); null for other runs.</summary>
    public global::RecursiveAI.OneDocument.Rendering.Equations.MathBox? Math { get; internal set; }

    internal RunPaintInfo Paint { get; set; } = new();

    internal GlyphData? Glyphs { get; set; }

    internal InlineDrawing? Drawing { get; set; }
}

/// <summary>The result of laying out a document.</summary>
public sealed class LayoutDocument
{
    internal LayoutDocument(IReadOnlyList<LayoutPage> pages, IReadOnlyList<FontSubstitutionNote> fontNotes, IReadOnlyList<LayoutNote> notes, int compatibilityMode, int fieldPasses = 1, Document? workingCopy = null)
    {
        Pages = pages;
        FontNotes = fontNotes;
        Notes = notes;
        CompatibilityMode = compatibilityMode;
        FieldPasses = fieldPasses;
        WorkingCopy = workingCopy;
    }

    /// <summary>Gets how many layout passes ran; 1 when no field needed the layout, more while page-dependent fields settled.</summary>
    public int FieldPasses { get; }

    /// <summary>
    /// Gets the copy of the document the pages were laid out from when a table of contents, index or table of authorities had to be built (the
    /// paragraphs of such pages belong to it, not to the document passed in); null when the document itself was laid out.
    /// </summary>
    public Document? WorkingCopy { get; }

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

    /// <summary>
    /// Gets or sets whether every field is updated, as Word does when it prints with "update fields before printing" on. Default false: fields
    /// that depend on the page (<c>PAGE</c>, <c>NUMPAGES</c>, <c>SECTIONPAGES</c>, <c>SECTION</c>, <c>PAGEREF</c>, <c>NOTEREF</c>, <c>REF</c>,
    /// <c>STYLEREF</c>, <c>SEQ</c>) are computed from the layout and the others show the result stored in the file; a <c>TOC</c> with no stored
    /// result is built, and one with a result keeps its entries and gets the page numbers of its <c>PAGEREF</c> fields recomputed. When true,
    /// the table of contents, index and table of authorities are rebuilt and the date, property, <c>IF</c>, <c>=</c>, <c>SYMBOL</c> and <c>QUOTE</c>
    /// fields are computed too. A document whose settings carry <c>w:updateFields</c> is laid out as if this were true, as Word updates its
    /// fields when it opens such a file. The document itself is never changed.
    /// </summary>
    public bool UpdateFields { get; set; }

    /// <summary>
    /// Gets or sets the clock <c>DATE</c> and <c>TIME</c> read when <see cref="UpdateFields"/> is on. Null (the default) keeps their stored results:
    /// layout never reads the system time on its own, so the same input gives the same pages.
    /// </summary>
    public TimeProvider? Clock { get; set; }

    /// <summary>
    /// Gets or sets how many layout passes may run while fields settle (page numbers that depend on layout that depends on page numbers).
    /// Default 6. When the fields still change after the last pass the layout keeps that pass and adds the note <c>layout.fields-unsettled</c>.
    /// </summary>
    public int MaxFieldPasses { get; set; } = 6;

    // The same options with every field updated: what a document that asks Word to update its fields on open gets.
    internal LayoutOptions WithUpdateFields()
    {
        var copy = (LayoutOptions)MemberwiseClone();
        copy.UpdateFields = true;
        return copy;
    }
}
