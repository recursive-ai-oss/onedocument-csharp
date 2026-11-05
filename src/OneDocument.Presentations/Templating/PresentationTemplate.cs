using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Presentations.Templating;

/// <summary>What rendering a presentation template reports.</summary>
/// <param name="Defects">Warnings from the template engine (missing data, expression errors) and from the binder (<c>pptx.template-*</c>), located.</param>
/// <param name="SlideCount">The number of slides in the rendered presentation.</param>
public sealed record PresentationTemplateResult(IReadOnlyList<Defect> Defects, int SlideCount);

/// <summary>
/// Fills a presentation template. Text in shapes, placeholders, groups, table cells and notes is read as paragraphs; tags split across runs
/// are found, and what a tag prints takes the formatting of the run where the tag began. Blocks repeat paragraphs inside a text body, table
/// rows, whole shapes or whole slides depending on where their tags stand (or on <c>scope=</c>), and an <c>{% image %}</c> tag that is all a
/// shape says puts a picture in the shape's place.
/// </summary>
public static class PresentationTemplate
{
    /// <summary>Reads and checks the template without binding data.</summary>
    /// <param name="presentation">The template.</param>
    /// <param name="options">Options, or null for the defaults. Its <see cref="TemplateOptions.Scopes"/> is replaced by the presentation's.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>Every error and warning with its place (a part and the shape and paragraph); empty when the template is sound.</returns>
    /// <exception cref="OfficeException">A slide cannot be read.</exception>
    public static IReadOnlyList<Defect> Validate(Presentation presentation, TemplateOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var warnings = new List<Defect>();
        var model = new PptxTemplateModel(presentation, (options ?? new TemplateOptions()).Delimiters, warnings);
        model.Collect(cancellationToken);
        ParsedTemplate parsed = TemplateEngine.Parse(model.Source, WithScopes(options, model, presentation.Limits));
        warnings.AddRange(parsed.Defects);
        return warnings;
    }

    /// <summary>Renders the template in place: slides are cloned, removed and reordered, text is replaced, and pictures are inserted.</summary>
    /// <param name="presentation">The template, edited in place; save it afterwards (the original file is not touched until you do).</param>
    /// <param name="data">The data, as <see cref="TemplateData.From"/> reads it. The root must be an object.</param>
    /// <param name="options">Options, or null for the defaults. Its <see cref="TemplateOptions.Scopes"/> is replaced by the presentation's.</param>
    /// <param name="cancellationToken">Cancels a long render.</param>
    /// <returns>The warnings and the slide count.</returns>
    /// <exception cref="TemplateException">The template has an error (the first), or strict mode met missing data or an error value.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded: loop iterations, output size, or more output slides than the part limit allows.</exception>
    public static PresentationTemplateResult Render(Presentation presentation, object? data, TemplateOptions? options = null, CancellationToken cancellationToken = default) =>
        Render(presentation, TemplateData.From(data, options?.MaxRows ?? new TemplateOptions().MaxRows), options, cancellationToken);

    /// <summary>Renders the template in place with data already adapted.</summary>
    /// <param name="presentation">The template, edited in place.</param>
    /// <param name="data">The data; null is an empty object.</param>
    /// <param name="options">Options, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels a long render.</param>
    /// <returns>The warnings and the slide count.</returns>
    /// <exception cref="TemplateException">The template has an error (the first), or strict mode met missing data or an error value.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static PresentationTemplateResult Render(Presentation presentation, ITemplateData? data, TemplateOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var warnings = new List<Defect>();
        TemplateOptions given = options ?? new TemplateOptions();
        var model = new PptxTemplateModel(presentation, given.Delimiters, warnings);
        model.Collect(cancellationToken);
        TemplateOptions opts = WithScopes(options, model, presentation.Limits);
        ParsedTemplate parsed = TemplateEngine.Parse(model.Source, opts);
        TemplateResult result = parsed.Render(data, cancellationToken);
        warnings.AddRange(result.Defects);

        List<SlideOut> slides = Assemble(model, result, warnings);
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), presentation.Limits.MaxPartCount, slides.Count, DefectLocation.InPart(presentation.MainPartName));

        // Copies are made from the untouched template slides, before anything is written to them.
        IReadOnlyList<Slide> source = model.Slides;
        var used = new bool[source.Count];
        foreach (SlideOut s in slides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!used[s.Source])
            {
                used[s.Source] = true;
                s.Target = source[s.Source];
            }
            else
            {
                s.Target = presentation.DuplicateSlide(source[s.Source]);
            }
        }

        // Every copy's markup is built from the template's nodes first; only then are any slides written to.
        var writer = new PptxSlideWriter(model, presentation, warnings);
        var pending = new List<(PartContext Part, MarkupRef Tree, string Xml)>();
        foreach (SlideOut s in slides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Slide target = s.Target!;
            var part = new PartContext(presentation, target.PartName, target.Markup.Node);
            string shapes = writer.EmitShapes(s.Shapes, part);
            pending.Add((part, target.ShapeTreeMarkup, shapes));
            if (s.NotesShapes.Count > 0 && target.Notes is { } notes && notes.Markup.IsValid)
            {
                var notesPart = new PartContext(presentation, notes.PartName, notes.Markup.Node);
                pending.Add((notesPart, notes.ShapeTreeMarkup, writer.EmitShapes(s.NotesShapes, notesPart)));
            }
        }

        foreach ((PartContext part, MarkupRef tree, string xml) in pending)
        {
            PptxSlideWriter.ReplaceShapes(tree, xml);
            part.Flush(tree.Node.Document!.Root);
        }

        for (int i = 0; i < source.Count; i++)
        {
            if (!used[i])
            {
                presentation.DeleteSlide(source[i]);
            }
        }

        for (int i = 0; i < slides.Count; i++)
        {
            presentation.MoveSlide(slides[i].Target!, i);
        }

        presentation.CommitChanges();
        if (options?.Defects is { } sink)
        {
            foreach (Defect d in warnings)
            {
                if (d.Code.StartsWith("pptx.", StringComparison.Ordinal))
                {
                    sink.Report(d);
                }
            }
        }

        return new PresentationTemplateResult(warnings, slides.Count);
    }

    /// <summary>Opens a template, renders it and writes the result.</summary>
    /// <param name="template">The template presentation; read to the end, not closed.</param>
    /// <param name="data">The data, as <see cref="TemplateData.From"/> reads it.</param>
    /// <param name="output">Where the rendered presentation goes; not closed.</param>
    /// <param name="options">Options, or null.</param>
    /// <param name="format">The output format; the template's own when null.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The warnings and the slide count.</returns>
    /// <exception cref="TemplateException">The template has an error, or strict mode met missing data.</exception>
    /// <exception cref="OfficeException">The template cannot be opened or the result cannot be written.</exception>
    public static PresentationTemplateResult Render(Stream template, object? data, Stream output, TemplateOptions? options = null, OfficeFormat? format = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(output);
        using Presentation deck = Presentation.Open(template, null, true, cancellationToken);
        PresentationTemplateResult result = Render(deck, data, options, cancellationToken);
        deck.SaveAs(output, format ?? deck.Format, cancellationToken);
        return result;
    }

    private static TemplateOptions WithScopes(TemplateOptions? o, ITemplateScopeResolver scopes, OfficeLimits limits)
    {
        o ??= new TemplateOptions();
        return new TemplateOptions
        {
            Delimiters = o.Delimiters,
            Strict = o.Strict,
            Limits = o.Limits ?? limits,
            Defects = o.Defects,
            Functions = o.Functions,
            Clock = o.Clock,
            RandomSeed = o.RandomSeed,
            Scopes = scopes,
            Includes = o.Includes,
            MaxIterations = o.MaxIterations,
            MaxOutputCharacters = o.MaxOutputCharacters,
            MaxBlockDepth = o.MaxBlockDepth,
            MaxRows = o.MaxRows,
        };
    }

    // Turns the engine's flat list of output units back into slides of shapes of rows and paragraphs, by the markers each carries.
    private static List<SlideOut> Assemble(PptxTemplateModel model, TemplateResult result, List<Defect> warnings)
    {
        var slides = new List<SlideOut>();
        SlideOut? slide = null;
        ShapeOut? shape = null;
        RowOut? row = null;
        ParagraphOut? last = null;
        bool injection = false;

        void Orphan(int unit)
        {
            warnings.Add(new Defect(DefectKind.Structure, "pptx.template-orphan-unit", DefectSeverity.Dropped, "Text came out outside the slide, shape or row it belongs to (a block's tags are in different places than its scope); it is left out.", DefectLocation.InPart(model.PartOf(unit))));
        }

        foreach (TemplateRenderedUnit ru in result.Units)
        {
            PptxUnit u = model.Units[ru.SourceUnit];
            switch (u.Kind)
            {
                case PptxUnitKind.Slide:
                    slide = new SlideOut(u.SlideIndex);
                    slides.Add(slide);
                    shape = null;
                    row = null;
                    last = null;
                    break;
                case PptxUnitKind.Shape:
                    if (slide is null || slide.Source != u.SlideIndex)
                    {
                        Orphan(ru.SourceUnit);
                        shape = null;
                        break;
                    }

                    shape = new ShapeOut(ru.SourceUnit);
                    (u.Notes ? slide.NotesShapes : slide.Shapes).Add(shape);
                    row = null;
                    last = null;
                    break;
                case PptxUnitKind.Row:
                    if (shape is null || shape.Unit != u.ShapeUnit)
                    {
                        Orphan(ru.SourceUnit);
                        row = null;
                        break;
                    }

                    row = new RowOut(ru.SourceUnit);
                    shape.Rows.Add(row);
                    last = null;
                    break;
                case PptxUnitKind.Paragraph:
                    {
                        Dictionary<MarkupNode, List<ParagraphOut>>? bodies = u.RowUnit >= 0
                            ? (row is not null && row.Unit == u.RowUnit ? row.Bodies : null)
                            : (shape is not null && shape.Unit == u.ShapeUnit ? shape.Bodies : null);
                        if (bodies is null)
                        {
                            Orphan(ru.SourceUnit);
                            last = null;
                            break;
                        }

                        IReadOnlyList<TemplatePiece> pieces = ru.Pieces;
                        if (pieces.Any(static p => p.Kind is TemplatePieceKind.Html or TemplatePieceKind.Markdown))
                        {
                            injection = true;
                            pieces = [.. pieces.Select(static p => p.Kind is TemplatePieceKind.Html or TemplatePieceKind.Markdown ? new TemplatePiece(TemplatePieceKind.Text, p.Segment, p.Insertion?.Value.Raw?.ToString() ?? string.Empty) : p)];
                        }

                        last = new ParagraphOut(ru.SourceUnit, pieces);
                        if (!bodies.TryGetValue(u.Body, out List<ParagraphOut>? list))
                        {
                            bodies[u.Body] = list = [];
                        }

                        list.Add(last);
                        break;
                    }

                case PptxUnitKind.Link:
                    if (last is not null && last.Unit == u.OwnerParagraph)
                    {
                        (last.Links ??= [])[u.RelationshipId!] = ru.Text.Trim().Replace(" ", "%20", StringComparison.Ordinal);
                    }

                    break;
            }
        }

        if (injection)
        {
            warnings.Add(new Defect(DefectKind.Unsupported, "pptx.template-injection-unsupported", DefectSeverity.Dropped, "HTML and Markdown tags are inserted as plain text in presentations.", DefectLocation.None));
        }

        return slides;
    }
}
