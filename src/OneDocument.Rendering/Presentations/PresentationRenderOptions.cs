using OneRender.Pdf;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

/// <summary>Which pages of a presentation are rendered.</summary>
[Flags]
public enum PresentationOutput
{
    /// <summary>One page per slide, at the slide size.</summary>
    Slides = 1,

    /// <summary>One notes page per slide: the notes master's layout with the slide's image and the speaker notes.</summary>
    NotesPages = 2,

    /// <summary>Handout pages with several slide images each (see <see cref="PresentationRenderOptions.HandoutSlidesPerPage"/>).</summary>
    Handouts = 4,
}

/// <summary>How text that does not fit its shape is treated when the shape shrinks text on overflow.</summary>
public enum SlideAutofitMode
{
    /// <summary>
    /// Uses the font scale and line spacing reduction the file stores, which is what PowerPoint shows; when a shrinking shape stores
    /// neither, the scale is computed.
    /// </summary>
    StoredThenComputed = 0,

    /// <summary>Uses only what the file stores; a shrinking shape with no stored values is not shrunk.</summary>
    Stored = 1,

    /// <summary>Always computes the scale, ignoring stored values.</summary>
    Computed = 2,
}

/// <summary>Options for rendering a presentation.</summary>
public sealed class PresentationRenderOptions
{
    /// <summary>Gets or sets which pages are written. Default <see cref="PresentationOutput.Slides"/>.</summary>
    public PresentationOutput Output { get; set; } = PresentationOutput.Slides;

    /// <summary>Gets or sets whether hidden slides are rendered. Default false: they are skipped, as in a slide show or a print.</summary>
    public bool IncludeHiddenSlides { get; set; }

    /// <summary>Gets or sets whether the PDF is tagged (a structure tree). Default true.</summary>
    public bool Tagged { get; set; } = true;

    /// <summary>Gets or sets whether page content is compressed. Default true.</summary>
    public bool Compress { get; set; } = true;

    /// <summary>Gets or sets the PDF profile (none, PDF/UA-1, PDF/A-2a, or both). A profile needs <see cref="Tagged"/>.</summary>
    public PdfProfile Profile { get; set; }

    /// <summary>Gets or sets the PDF title; null takes the first slide's title, else "Presentation".</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the font provider; null makes the default library (the open fonts that stand in for the common Office fonts).</summary>
    public IFontProvider? Fonts { get; set; }

    /// <summary>
    /// Gets or sets the clock date and time fields read. Default null: a field shows the text stored in the file, so the output does
    /// not depend on when it is made.
    /// </summary>
    public TimeProvider? Clock { get; set; }

    /// <summary>Gets or sets how shrink-on-overflow is applied. Default <see cref="SlideAutofitMode.StoredThenComputed"/>.</summary>
    public SlideAutofitMode Autofit { get; set; }

    /// <summary>Gets or sets how SmartArt diagrams are painted (stored drawing, or the layout run again) and a report callback. Null: the stored drawing unless it is missing or stale.</summary>
    public SmartArt.DiagramOptions? Diagrams { get; set; }

    /// <summary>Gets or sets the number of slide images on a handout page: 1, 2, 3, 4, 6 or 9. Default 6.</summary>
    public int HandoutSlidesPerPage { get; set; } = 6;
}

/// <summary>What kind of page a rendered page is.</summary>
public enum RenderedPageKind
{
    /// <summary>A slide.</summary>
    Slide,

    /// <summary>A notes page.</summary>
    Notes,

    /// <summary>A handout page.</summary>
    Handout,
}

/// <summary>One page that was rendered.</summary>
/// <param name="Number">The page number in the output, from 1.</param>
/// <param name="Kind">Slide, notes page or handout.</param>
/// <param name="SlideIndex">The zero-based index of the slide in the presentation's slide order (the first slide of a handout page).</param>
/// <param name="Width">The page width in points.</param>
/// <param name="Height">The page height in points.</param>
public sealed record RenderedPage(int Number, RenderedPageKind Kind, int SlideIndex, double Width, double Height);

/// <summary>What a rendering did, apart from the output itself: the pages, what was skipped and what was approximated.</summary>
public sealed class PresentationRenderReport
{
    internal PresentationRenderReport(IReadOnlyList<RenderedPage> pages, int hiddenSkipped, IReadOnlyList<FontSubstitutionNote> fontNotes, IReadOnlyList<Drawing.DrawingNote> notes)
    {
        Pages = pages;
        HiddenSlidesSkipped = hiddenSkipped;
        FontNotes = fontNotes;
        Notes = notes;
    }

    /// <summary>Gets the pages written, in order.</summary>
    public IReadOnlyList<RenderedPage> Pages { get; }

    /// <summary>Gets the number of hidden slides that were left out.</summary>
    public int HiddenSlidesSkipped { get; }

    /// <summary>Gets the fonts that were replaced, once per pair.</summary>
    public IReadOnlyList<FontSubstitutionNote> FontNotes { get; }

    /// <summary>Gets what was approximated or left out (<c>drawing.*</c> and <c>slide.*</c> codes), once per code.</summary>
    public IReadOnlyList<Drawing.DrawingNote> Notes { get; }

    /// <summary>Gets the number of slide pages.</summary>
    public int SlidePages => Pages.Count(static p => p.Kind == RenderedPageKind.Slide);

    /// <summary>Gets the number of notes pages.</summary>
    public int NotesPages => Pages.Count(static p => p.Kind == RenderedPageKind.Notes);
}
