using RecursiveAI.OneDocument.Layout;
using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>The kind of drawing object anchored on a sheet.</summary>
public enum SheetObjectKind
{
    /// <summary>A picture (<c>xdr:pic</c>).</summary>
    Picture,

    /// <summary>A chart or other graphic frame (<c>xdr:graphicFrame</c>).</summary>
    Chart,

    /// <summary>A shape or text box (<c>xdr:sp</c>).</summary>
    Shape,

    /// <summary>A group of shapes (<c>xdr:grpSp</c>).</summary>
    Group,

    /// <summary>A connector (<c>xdr:cxnSp</c>).</summary>
    Connector,

    /// <summary>Anything else.</summary>
    Other,
}

/// <summary>
/// A drawing object (picture, chart, shape) with the box it occupies on a printed page. This is the hook for the drawing painters:
/// the anchors are resolved to page coordinates here, and <see cref="SheetRenderOptions.ObjectPainter"/> is called with each one.
/// </summary>
/// <param name="Kind">What the object is.</param>
/// <param name="Name">Its name (<c>xdr:cNvPr/@name</c>), or empty.</param>
/// <param name="X">Left edge in points from the left of the page.</param>
/// <param name="Y">Top edge in points from the top of the page.</param>
/// <param name="Width">Width in points, after the page scale.</param>
/// <param name="Height">Height in points, after the page scale.</param>
/// <param name="Source">The anchor element in the drawing part (<c>xdr:twoCellAnchor</c> and the like).</param>
/// <param name="DrawingPart">The name of the drawing part the anchor is in; its relationships name the object's pictures and charts.</param>
public sealed record SheetObject(SheetObjectKind Kind, string Name, double X, double Y, double Width, double Height, MarkupNode Source, string DrawingPart);

/// <summary>What <see cref="SheetRenderOptions.ObjectPainter"/> receives for each object on a page.</summary>
/// <param name="Page">The page being painted.</param>
/// <param name="Item">The object and its box in page points.</param>
/// <param name="List">The display list of the page; the object paints into it. Its units are CSS pixels, 1 pt = 4/3 px, with no transform active.</param>
/// <param name="Bounds">The object's box in the list's pixels.</param>
public sealed record SheetObjectPaintContext(SheetPage Page, SheetObject Item, DisplayList List, RectF Bounds);

/// <summary>Options for laying out and painting worksheets.</summary>
public sealed class SheetRenderOptions
{
    /// <summary>Gets or sets the fonts. Null uses the library's default fonts (metric-compatible stand-ins for the Office fonts).</summary>
    public IFontProvider? Fonts { get; set; }

    /// <summary>Gets or sets whether semantic tags (a table per printed range, rows, header and data cells) are written. Default true.</summary>
    public bool Tagged { get; set; } = true;

    /// <summary>Gets or sets whether a plain box is drawn where a picture, chart or shape sits, for painters that have not been plugged in. Default false.</summary>
    public bool ObjectPlaceholders { get; set; }

    /// <summary>
    /// Gets or sets whether pictures, shapes and charts on the sheet are painted (through the shared DrawingML painter and the chart renderer) at their
    /// anchor boxes. Default true. Text inside shapes is not painted yet.
    /// </summary>
    public bool PaintDrawings { get; set; } = true;

    /// <summary>Gets or sets a painter called for every drawing object after the cells of its page are painted; null for none.</summary>
    public Action<SheetObjectPaintContext>? ObjectPainter { get; set; }

    /// <summary>
    /// Gets or sets the clock the header and footer codes <c>&amp;D</c> and <c>&amp;T</c>, and the date rules of conditional formats, read.
    /// Default <see cref="TimeProvider.System"/>: a printout carries the date it was printed. Pass a fixed provider for repeatable output.
    /// </summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Gets or sets the most pages laid out before the layout stops with a note. Default 10,000.</summary>
    public int MaxPages { get; set; } = 10_000;

    /// <summary>Gets or sets the file name written for the header and footer code <c>&amp;F</c>; null uses the workbook's file name, or <c>Book1.xlsx</c>.</summary>
    public string? FileName { get; set; }

    /// <summary>Gets or sets whether sheets that are hidden or very hidden print too. Default false: only visible sheets print.</summary>
    public bool IncludeHiddenSheets { get; set; }
}

/// <summary>One printed page of a worksheet: which cells, at what scale, where on the paper.</summary>
public sealed class SheetPage
{
    internal SheetPage(SheetPlan plan, int sheetIndex)
    {
        Plan = plan;
        SheetIndex = sheetIndex;
    }

    internal SheetPlan Plan { get; }

    /// <summary>Gets the sheet.</summary>
    public Worksheet Sheet => Plan.Sheet;

    /// <summary>Gets the index of the sheet in the workbook.</summary>
    public int SheetIndex { get; }

    /// <summary>Gets the printed page number (the number a <c>&amp;P</c> header code shows).</summary>
    public int Number { get; internal set; }

    /// <summary>Gets the 1-based position of the page within its sheet.</summary>
    public int NumberInSheet { get; internal set; }

    /// <summary>Gets the paper width in points.</summary>
    public double Width { get; internal set; }

    /// <summary>Gets the paper height in points.</summary>
    public double Height { get; internal set; }

    /// <summary>Gets the page scale (1 is 100 %).</summary>
    public double Scale { get; internal set; }

    /// <summary>Gets the cells of the sheet this page prints, not counting repeated titles.</summary>
    public CellRange Body { get; internal set; }

    /// <summary>Gets the first and last repeated title row, or (0, 0).</summary>
    public (int First, int Last) TitleRows { get; internal set; }

    /// <summary>Gets the first and last repeated title column, or (0, 0).</summary>
    public (int First, int Last) TitleColumns { get; internal set; }

    /// <summary>Gets the left edge of the printed cells (after centring and headings) in points from the left of the page.</summary>
    public double ContentX { get; internal set; }

    /// <summary>Gets the top edge of the printed cells in points from the top of the page.</summary>
    public double ContentY { get; internal set; }

    /// <summary>Gets the width of the printed cells in points, after the scale.</summary>
    public double ContentWidth { get; internal set; }

    /// <summary>Gets the height of the printed cells in points, after the scale.</summary>
    public double ContentHeight { get; internal set; }

    /// <summary>Gets the drawing objects whose boxes meet this page, with the box in page points.</summary>
    public IReadOnlyList<SheetObject> Objects { get; internal set; } = [];

    internal int AreaIndex { get; set; }

    internal int Index { get; set; }
}

/// <summary>The pages a workbook prints and what the layout had to approximate or skip.</summary>
public sealed class SheetLayoutDocument
{
    internal SheetLayoutDocument(Workbook workbook, RenderContext context, IReadOnlyList<SheetPage> pages)
    {
        Workbook = workbook;
        Context = context;
        Pages = pages;
    }

    internal RenderContext Context { get; }

    /// <summary>Gets the workbook.</summary>
    public Workbook Workbook { get; }

    /// <summary>Gets the pages in print order: sheets in tab order, each sheet's print areas in order, pages in the sheet's page order.</summary>
    public IReadOnlyList<SheetPage> Pages { get; }

    /// <summary>Gets one note for every font that was replaced by a different family.</summary>
    public IReadOnlyList<FontSubstitutionNote> FontNotes => Context.Text.Notes;

    /// <summary>Gets what was skipped or approximated (<c>sheet-layout.*</c> codes).</summary>
    public IReadOnlyList<SheetRenderNote> Notes => Context.Notes;
}
