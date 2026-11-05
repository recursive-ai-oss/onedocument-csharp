using System.Globalization;
using System.Numerics;
using OneFont;
using OneRender.Painting;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Schema;
using DrawingColorMap = RecursiveAI.OneDocument.Rendering.Drawing.ColorMap;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

/// <summary>A page to render: a slide, its notes page, or a handout page.</summary>
internal sealed record PageSpec(RenderedPageKind Kind, int SlideIndex, int SlideCount);

/// <summary>
/// The state of one rendering of one presentation: fonts, pictures, per-part painting contexts and the notes collected on the way. It
/// is used from one thread at a time.
/// </summary>
internal sealed partial class RenderSession
{
    private const double K = 96.0 / 72.0;
    private const double DefaultSlideWidth = 720, DefaultSlideHeight = 540;
    private readonly Dictionary<string, DrawingTheme> _themes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PartContext> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DrawingNote> _notes = [];
    private uint _owner;

    public RenderSession(Presentation presentation, PresentationRenderOptions options)
    {
        Presentation = presentation;
        Options = options;
        Fonts = options.Fonts ?? FontLibrary.CreateDefault();
        FontNotes = [];
        Layout = new SlideTextLayout(Fonts, FontNotes);
        Tagged = options.Tagged;
        Fields.Clock = options.Clock;
        Fields.Note = AddNote;
        Slides = presentation.Slides;
    }

    public Presentation Presentation { get; }

    public PresentationRenderOptions Options { get; }

    public IFontProvider Fonts { get; }

    public DrawingImages Images { get; } = new();

    public List<FontSubstitutionNote> FontNotes { get; }

    public SlideTextLayout Layout { get; }

    public FieldContext Fields { get; } = new();

    public bool Tagged { get; set; }

    public IReadOnlyList<Slide> Slides { get; }

    /// <summary>Notes about what was approximated: the painters' notes plus the renderer's own (<c>slide.*</c>).</summary>
    public IReadOnlyList<DrawingNote> Notes
    {
        get
        {
            foreach (PartContext c in _parts.Values)
            {
                foreach (DrawingNote n in c.Drawing.Notes)
                {
                    AddNote(n.Code, n.Message);
                }
            }

            return _notes;
        }
    }

    public void AddNote(string code, string message)
    {
        foreach (DrawingNote n in _notes)
        {
            if (n.Code == code)
            {
                return;
            }
        }

        _notes.Add(new DrawingNote(code, message));
    }

    public ItemId NextId() => ItemId.Of(++_owner, 0, 0);

    public PartContext Context(PresentationPart part)
    {
        if (!_parts.TryGetValue(part.PartName, out PartContext? c))
        {
            c = new PartContext(this, part);
            _parts[part.PartName] = c;
        }

        c.Drawing.Tagged = Tagged;
        return c;
    }

    // ---------------------------------------------------------------- theme and colour map

    public DrawingTheme ThemeOf(PresentationPart owner)
    {
        if (_themes.TryGetValue(owner.PartName, out DrawingTheme? cached))
        {
            return cached;
        }

        DrawingTheme theme = DrawingTheme.Default;
        foreach (Relationship r in owner.Relationships)
        {
            if (r.Type.EndsWith("/theme", StringComparison.Ordinal) && OpcPackage.TryResolveTarget(owner.PartName, r, out PartUri target)
                && owner.Owner.Package.FindPart(target.Value) is { } part
                && MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? doc, owner.Owner.Context, target.Value) && doc is not null)
            {
                theme = DrawingTheme.Read(doc.Root);
                break;
            }
        }

        _themes[owner.PartName] = theme;
        return theme;
    }

    // ECMA-376 Part 1 §19.3.1.6 (clrMap), §19.3.1.7 (clrMapOvr): a slide or layout uses the master's mapping unless it overrides it.
    public static DrawingColorMap ColorMapOf(PresentationPart part)
    {
        MarkupRef root = part.Markup;
        if (root.IsValid)
        {
            MarkupNode ovr = DrawingTheme.Child(DrawingTheme.Child(root.Node, "clrMapOvr"), "overrideClrMapping");
            if (!ovr.IsNull)
            {
                return DrawingColorMap.Read(ovr);
            }

            MarkupNode own = DrawingTheme.Child(root.Node, "clrMap");
            if (!own.IsNull)
            {
                return DrawingColorMap.Read(own);
            }
        }

        PresentationPart? master = part switch
        {
            Slide s => s.Master,
            SlideLayout l => l.Master,
            NotesSlide n => n.NotesMaster,
            _ => null,
        };
        return master is null ? DrawingColorMap.Default : ColorMapOf(master);
    }

    // ---------------------------------------------------------------- pages

    public (double Width, double Height) SlideSize()
    {
        if (Presentation.SlideSize is { } s && s.Width > 0 && s.Height > 0)
        {
            return (Math.Clamp(s.Width / 12700.0, 1, 14400), Math.Clamp(s.Height / 12700.0, 1, 14400));
        }

        AddNote("slide.no-size", "The presentation has no slide size; 10 by 7.5 inches is used.");
        return (DefaultSlideWidth, DefaultSlideHeight);
    }

    public (double Width, double Height) NotesSize()
    {
        MarkupNode sz = DrawingTheme.Child(Presentation.Markup.Node, "notesSz");
        if (!sz.IsNull && double.TryParse(sz.GetAttribute("cx"), NumberStyles.Float, CultureInfo.InvariantCulture, out double cx)
            && double.TryParse(sz.GetAttribute("cy"), NumberStyles.Float, CultureInfo.InvariantCulture, out double cy) && cx > 0 && cy > 0)
        {
            return (Math.Clamp(cx / 12700.0, 1, 14400), Math.Clamp(cy / 12700.0, 1, 14400));
        }

        return (540, 720);
    }

    public int FirstSlideNumber()
    {
        string? v = Presentation.Markup.Node.GetAttribute("firstSlideNum");
        return v is not null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 ? n : 1;
    }

    public List<PageSpec> Enumerate(out int hiddenSkipped)
    {
        hiddenSkipped = 0;
        var included = new List<int>();
        for (int i = 0; i < Slides.Count; i++)
        {
            if (Slides[i].IsHidden && !Options.IncludeHiddenSlides)
            {
                hiddenSkipped++;
                continue;
            }

            included.Add(i);
        }

        var pages = new List<PageSpec>();
        if (Options.Output.HasFlag(PresentationOutput.Slides))
        {
            foreach (int i in included)
            {
                pages.Add(new PageSpec(RenderedPageKind.Slide, i, 1));
            }
        }

        if (Options.Output.HasFlag(PresentationOutput.NotesPages))
        {
            foreach (int i in included)
            {
                pages.Add(new PageSpec(RenderedPageKind.Notes, i, 1));
            }
        }

        if (Options.Output.HasFlag(PresentationOutput.Handouts))
        {
            int per = Options.HandoutSlidesPerPage is 1 or 2 or 3 or 4 or 6 or 9 ? Options.HandoutSlidesPerPage : 6;
            for (int k = 0; k < included.Count; k += per)
            {
                pages.Add(new PageSpec(RenderedPageKind.Handout, included[k], Math.Min(per, included.Count - k)));
            }
        }

        return pages;
    }

    public (double Width, double Height) PageSize(PageSpec page) => page.Kind == RenderedPageKind.Slide ? SlideSize() : NotesSize();

    /// <summary>Paints one page into a new display list (CSS pixels).</summary>
    public DisplayList PaintPage(PageSpec page)
    {
        var list = new DisplayList();
        Tagged = Options.Tagged;
        if (Tagged)
        {
            list.BeginTag(NextId(), TagRole.Section);
        }

        switch (page.Kind)
        {
            case RenderedPageKind.Slide:
                PaintSlide(list, Slides[page.SlideIndex], page.SlideIndex, 0, 0);
                break;
            case RenderedPageKind.Notes:
                PaintNotesPage(list, page.SlideIndex);
                break;
            default:
                PaintHandout(list, page);
                break;
        }

        if (Tagged)
        {
            list.EndTag();
        }

        return list;
    }

    private int SlideNumberOf(int slideIndex) => FirstSlideNumber() + slideIndex;

    /// <summary>Paints a slide with its top-left corner at (x, y) points: background, master shapes, layout shapes, then the slide's own.</summary>
    private void PaintSlide(DisplayList list, Slide slide, int index, double x, double y)
    {
        (double w, double h) = SlideSize();
        Fields.SlideNumber = SlideNumberOf(index);
        SlideLayout? layout = slide.Layout;
        SlideMaster? master = slide.Master;
        var box = new DrawingBox(x, y, w, h);
        bool tagged = Tagged;

        // The background of the nearest part that has one.
        PresentationPart?[] chain = [slide, layout, master];
        foreach (PresentationPart? part in chain)
        {
            if (part is null || !part.Markup.IsValid)
            {
                continue;
            }

            MarkupNode bg = DrawingTheme.Child(DrawingTheme.Child(part.Markup.Node, "cSld"), "bg");
            if (!bg.IsNull)
            {
                if (tagged)
                {
                    list.BeginTag(NextId(), TagRole.Artifact);
                }

                DrawingPainter.DrawBackground(list, NextId, bg, box, Context(part).Drawing);
                if (tagged)
                {
                    list.EndTag();
                }

                break;
            }
        }

        // ECMA-376 Part 1 §19.3.1.38 (sld showMasterSp) and §19.3.1.39 (sldLayout showMasterSp): "Hide background graphics" on a slide hides
        // the layout's and master's shapes; on a layout it hides the master's.
        if (slide.ShowMasterShapes)
        {
            if (tagged)
            {
                list.BeginTag(NextId(), TagRole.Artifact);
            }

            if (master is not null && (layout?.ShowMasterShapes ?? true))
            {
                PaintBackdropShapes(list, master, x, y);
            }

            if (layout is not null)
            {
                PaintBackdropShapes(list, layout, x, y);
            }

            if (tagged)
            {
                list.EndTag();
            }
        }

        PartContext sc = Context(slide);
        DrawingPainter.DrawShapes(list, NextId, slide.ShapeTreeMarkup.Node, x, y, sc.Drawing, n => !IsHiddenShape(n));
    }

    // Shapes of a layout or master that show on the slides: not the placeholders, which only supply positions and formatting.
    private void PaintBackdropShapes(DisplayList list, PresentationPart part, double x, double y)
    {
        PartContext pc = Context(part);
        DrawingPainter.DrawShapes(list, NextId, part.ShapeTreeMarkup.Node, x, y, pc.Drawing, n => !IsHiddenShape(n) && !IsPlaceholder(n));
    }

    private static bool IsHiddenShape(MarkupNode n)
    {
        foreach (MarkupNode nv in n.ChildElements)
        {
            string name = nv.LocalNameString;
            if (name.StartsWith("nv", StringComparison.Ordinal) && name.EndsWith("Pr", StringComparison.Ordinal))
            {
                MarkupNode c = DrawingTheme.Child(nv, "cNvPr");
                return !c.IsNull && c.GetAttribute("hidden") is "1" or "true";
            }
        }

        return false;
    }

    private static bool IsPlaceholder(MarkupNode n)
    {
        if (n.LocalNameString == "AlternateContent")
        {
            return false;
        }

        foreach (MarkupNode nv in n.ChildElements)
        {
            string name = nv.LocalNameString;
            if (name.StartsWith("nv", StringComparison.Ordinal) && name.EndsWith("Pr", StringComparison.Ordinal))
            {
                return !DrawingTheme.Child(DrawingTheme.Child(nv, "nvPr"), "ph").IsNull;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- notes pages and handouts

    private void PaintNotesPage(DisplayList list, int index)
    {
        Slide slide = Slides[index];
        (double pw, double ph) = NotesSize();
        (double sw, double sh) = SlideSize();
        NotesSlide? notes = slide.Notes;
        NotesMaster? master = notes?.NotesMaster ?? FindNotesMaster();
        Fields.SlideNumber = SlideNumberOf(index);
        var box = new DrawingBox(0, 0, pw, ph);

        PresentationPart?[] chain = [notes, master];
        foreach (PresentationPart? part in chain)
        {
            if (part is null || !part.Markup.IsValid)
            {
                continue;
            }

            MarkupNode bg = DrawingTheme.Child(DrawingTheme.Child(part.Markup.Node, "cSld"), "bg");
            if (!bg.IsNull)
            {
                DrawingPainter.DrawBackground(list, NextId, bg, box, Context(part).Drawing);
                break;
            }
        }

        if (master is not null)
        {
            if (Tagged)
            {
                list.BeginTag(NextId(), TagRole.Artifact);
            }

            PaintBackdropShapes(list, master, 0, 0);
            if (Tagged)
            {
                list.EndTag();
            }
        }

        // The slide's picture sits where the notes page's slide-image placeholder is: its own, else the master's, else the upper part of the page.
        double ix = pw * 0.125, iy = ph * 0.0833, iw = pw * 0.75, ih = iw * sh / sw;
        ShapeBounds? bounds = null;
        if (notes is not null)
        {
            foreach (Shape s in notes.AllShapes())
            {
                if (s.Placeholder is { Type: "sldImg" } && s.ResolveBounds() is { } b)
                {
                    bounds = b;
                    break;
                }
            }
        }

        if (bounds is null && master is not null)
        {
            foreach (Shape s in master.AllShapes())
            {
                if (s.Placeholder is { Type: "sldImg" } && s.Bounds is { } b)
                {
                    bounds = b;
                    break;
                }
            }
        }

        if (bounds is { } bb && bb.Width > 0 && bb.Height > 0)
        {
            ix = bb.X / 12700.0;
            iy = bb.Y / 12700.0;
            iw = bb.Width / 12700.0;
            ih = bb.Height / 12700.0;
        }

        PaintSlideImage(list, slide, index, ix, iy, iw, ih, slide.Title);

        if (notes is not null)
        {
            PartContext nc = Context(notes);
            DrawingPainter.DrawShapes(list, NextId, notes.ShapeTreeMarkup.Node, 0, 0, nc.Drawing, n => !IsHiddenShape(n));
        }
    }

    private NotesMaster? FindNotesMaster()
    {
        foreach (Slide s in Slides)
        {
            if (s.Notes?.NotesMaster is { } m)
            {
                return m;
            }
        }

        return null;
    }

    // The slide drawn scaled into a rectangle of the page, clipped to it. The scaled copy is one figure in the structure tree.
    private void PaintSlideImage(DisplayList list, Slide slide, int index, double x, double y, double w, double h, string? title)
    {
        (double sw, double sh) = SlideSize();
        double scale = Math.Min(w / sw, h / sh);
        double dx = x + ((w - (sw * scale)) / 2), dy = y + ((h - (sh * scale)) / 2);
        if (Tagged)
        {
            list.BeginTag(NextId(), TagRole.Figure, string.IsNullOrWhiteSpace(title) ? "Slide " + (index + 1).ToString(CultureInfo.InvariantCulture) : "Slide " + (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + title);
        }

        bool saved = Tagged;
        Tagged = false;
        list.PushClipRect(NextId(), new RectF((float)(dx * K), (float)(dy * K), (float)(sw * scale * K), (float)(sh * scale * K)));
        list.PushTransform(NextId(), Matrix3x2.CreateScale((float)scale) * Matrix3x2.CreateTranslation((float)(dx * K), (float)(dy * K)));
        PaintSlide(list, slide, index, 0, 0);
        list.PopTransform();
        list.PopClip();
        Tagged = saved;
        if (Tagged)
        {
            list.EndTag();
        }
    }

    // A handout page: slide pictures in a grid, each with a thin frame.
    private void PaintHandout(DisplayList list, PageSpec page)
    {
        (double pw, double ph) = NotesSize();
        (double sw, double sh) = SlideSize();
        int per = Options.HandoutSlidesPerPage is 1 or 2 or 3 or 4 or 6 or 9 ? Options.HandoutSlidesPerPage : 6;
        (int cols, int rows) = per switch { 1 => (1, 1), 2 => (1, 2), 3 => (1, 3), 4 => (2, 2), 6 => (2, 3), _ => (3, 3) };
        const double margin = 36;
        double cellW = (pw - (2 * margin)) / cols, cellH = (ph - (2 * margin)) / rows;
        AddNote("slide.handout-master-ignored", "Handout pages use a plain grid; the handout master's header, footer and placeholders are not drawn.");
        var included = new List<int>();
        for (int i = 0; i < Slides.Count; i++)
        {
            if (!Slides[i].IsHidden || Options.IncludeHiddenSlides)
            {
                included.Add(i);
            }
        }

        int start = included.IndexOf(page.SlideIndex);
        for (int k = 0; k < page.SlideCount && start + k < included.Count; k++)
        {
            int c = k % cols, r = k / cols;
            double cx = margin + (c * cellW) + 8, cy = margin + (r * cellH) + 8;
            double w = cellW - 16, h = cellH - 16;
            double scale = Math.Min(w / sw, h / sh);
            double dx = cx + ((w - (sw * scale)) / 2), dy = cy + ((h - (sh * scale)) / 2);
            int idx = included[start + k];
            PaintSlideImage(list, Slides[idx], idx, dx, dy, sw * scale, sh * scale, Slides[idx].Title);
            if (Tagged)
            {
                list.BeginTag(NextId(), TagRole.Artifact);
            }

            Paint frame = OneRender.Painting.Paint.FromColor(new PaintColor(0f, 0f, 0f, 1f));
            double t = 0.75;
            double fw = sw * scale, fh = sh * scale;
            list.FillRect(NextId(), new RectF((float)(dx * K), (float)((dy - t) * K), (float)(fw * K), (float)(t * K)), frame);
            list.FillRect(NextId(), new RectF((float)(dx * K), (float)((dy + fh) * K), (float)(fw * K), (float)(t * K)), frame);
            list.FillRect(NextId(), new RectF((float)((dx - t) * K), (float)(dy * K), (float)(t * K), (float)(fh * K)), frame);
            list.FillRect(NextId(), new RectF((float)((dx + fw) * K), (float)(dy * K), (float)(t * K), (float)(fh * K)), frame);
            if (Tagged)
            {
                list.EndTag();
            }
        }
    }
}
