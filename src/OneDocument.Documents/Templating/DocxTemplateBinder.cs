using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>
/// The word-processing template binder. It hands the engine every story of the document (body, headers and footers, footnotes and endnotes, comments, text boxes)
/// as paragraphs made of runs, so a tag split across runs by Word binds; it renders each story that holds tags and puts the result back in place. Stories
/// without tags are not touched, so their parts stay byte-identical.
/// </summary>
internal sealed class DocxTemplateBinder : ITemplateBinder
{
    public static DocxTemplateBinder Instance { get; } = new();

    public IReadOnlyList<Defect> Validate(OfficeFile file, TemplateOptions options, CancellationToken cancellationToken) =>
        Run(file, null, options, cancellationToken);

    public IReadOnlyList<Defect> Render(OfficeFile file, ITemplateData data, TemplateOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Run(file, data, options, cancellationToken);
    }

    private sealed record StoryRef(string Part, MarkupNode Root, string Label);

    private static List<Defect> Run(OfficeFile file, ITemplateData? data, TemplateOptions options, CancellationToken ct)
    {
        if (file is not Document doc)
        {
            throw new FormatNotAvailableException(file.Format, "render a document template from a " + file.Kind);
        }

        var ctx = new RenderContext(doc, options);
        var defects = new List<Defect>();
        List<StoryRef> topLevel = Discover(doc);
        var boxes = new List<StoryRef>();
        foreach (StoryRef s in topLevel)
        {
            ctx.RootIds.Scan(s.Root.OuterXml);
            int n = 0;
            CollectTextBoxes(s.Root, doc.WordprocessingNamespaceUri, s.Part, ref n, boxes);
        }

        // Inner stories first: a text box sits inside a paragraph of an outer story, and that paragraph is copied as it stands after the box was rendered.
        var order = new List<StoryRef>();
        for (int i = boxes.Count - 1; i >= 0; i--)
        {
            order.Add(boxes[i]);
        }

        order.AddRange(topLevel);

        bool validateOnly = data is null;
        var warnings = new List<Defect>();
        foreach (StoryRef story in order)
        {
            ct.ThrowIfCancellationRequested();
            RenderStory(ctx, story, data, options, validateOnly, defects, warnings, ct);
        }

        if (validateOnly)
        {
            return defects;
        }

        AddClones(ctx);
        warnings.AddRange(ctx.Warnings);
        warnings.AddRange(defects);
        foreach (Defect d in ctx.Warnings.Concat(defects))
        {
            options.Defects?.Report(d);
        }

        return warnings;
    }

    private static List<StoryRef> Discover(Document doc)
    {
        var list = new List<StoryRef>();
        string ns = doc.WordprocessingNamespaceUri;
        MarkupNode body = doc.MainMarkup.Root.FirstChild;
        foreach (MarkupNode c in doc.MainMarkup.Root.ChildElements)
        {
            if (c.IsElement(ns, "body"))
            {
                body = c;
            }
        }

        if (body.IsElement(ns, "body"))
        {
            list.Add(new StoryRef(doc.MainPartName, body, "body"));
        }

        foreach (Relationship r in doc.Package.GetRelationships(doc.MainPartName))
        {
            if (r.TargetMode != RelationshipTargetMode.Internal)
            {
                continue;
            }

            string? kind = null;
            foreach (string k in (ReadOnlySpan<string>)["header", "footer", "footnotes", "endnotes", "comments"])
            {
                if (RelationshipTypes.Is(r.Type, k))
                {
                    kind = k;
                }
            }

            if (kind is null || !PartUri.TryResolve(new PartUri(doc.MainPartName), r.Target, out PartUri uri) || doc.Package.FindPart(uri.Value) is not { } part)
            {
                continue;
            }

            MarkupDocument? markup = doc.Parts.Load(part.Name, required: false);
            if (markup is not null && !markup.Root.IsNull)
            {
                list.Add(new StoryRef(part.Name, markup.Root, kind));
            }
        }

        return list;
    }

    private static void CollectTextBoxes(MarkupNode node, string ns, string part, ref int counter, List<StoryRef> into)
    {
        foreach (MarkupNode c in node.ChildElements)
        {
            if (c.IsElement(ns, "txbxContent"))
            {
                counter++;
                into.Add(new StoryRef(part, c, "textbox" + counter.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            CollectTextBoxes(c, ns, part, ref counter, into);
        }
    }

    private static bool HasTag(StoryModel story, TemplateDelimiters d)
    {
        foreach (StoryNode u in story.Units)
        {
            string t = u.Paragraph!.Text;
            if (t.Contains(d.ExpressionOpen, StringComparison.Ordinal) || t.Contains(d.BlockOpen, StringComparison.Ordinal) || t.Contains(d.CommentOpen, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static TemplateOptions WithBinder(TemplateOptions o, OfficeLimits limits, FunctionRegistry functions, ITemplateScopeResolver scopes) => new()
    {
        Delimiters = o.Delimiters,
        Strict = o.Strict,
        Limits = o.Limits ?? limits,
        Defects = o.Defects,
        Functions = functions,
        Clock = o.Clock,
        RandomSeed = o.RandomSeed,
        Scopes = scopes,
        Includes = o.Includes,
        MaxIterations = o.MaxIterations,
        MaxOutputCharacters = o.MaxOutputCharacters,
        MaxBlockDepth = o.MaxBlockDepth,
        MaxRows = o.MaxRows,
    };

    private static void RenderStory(RenderContext ctx, StoryRef story, ITemplateData? data, TemplateOptions options, bool validateOnly, List<Defect> defects, List<Defect> warnings, CancellationToken ct)
    {
        Document doc = ctx.Document;
        string ns = doc.WordprocessingNamespaceUri;
        StoryModel model = StoryModel.Read(story.Root, story.Part, ns);
        if (!HasTag(model, options.Delimiters))
        {
            return;
        }

        model.MarkTagRows(options.Delimiters);
        var units = new List<TemplateUnit>(model.Units.Count);
        for (int i = 0; i < model.Units.Count; i++)
        {
            ParagraphModel p = model.Units[i].Paragraph!;
            var segments = new List<TemplateSegment>(p.Segments.Count);
            foreach (ParagraphItem s in p.Segments)
            {
                segments.Add(new TemplateSegment(s.Text));
            }

            units.Add(new TemplateUnit(segments, story.Part + "#" + story.Label + "/p" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var resolver = new DocxScopeResolver(model, defects);
        TemplateOptions bound = WithBinder(options, doc.Limits, ctx.Functions, resolver);
        ParsedTemplate parsed = TemplateEngine.Parse(new TemplateSource(units), bound);
        if (validateOnly)
        {
            defects.AddRange(parsed.Defects);
            return;
        }

        ctx.CurrentPart = story.Part;
        ctx.Ids = story.Label.StartsWith("textbox", StringComparison.Ordinal) ? ctx.RootIds.Fork() : ctx.RootIds;
        try
        {
            TemplateResult result = parsed.Render(data, ct);
            warnings.AddRange(result.Defects);
            string inner = new StoryRebuilder(model, ctx, parsed.Blocks, resolver).Run(result.Units);
            Replace(story.Root, inner);
        }
        finally
        {
            ctx.Ids = ctx.RootIds;
        }
    }

    // The children are replaced by appending the new content first, so a failure in the XML leaves the story as it was.
    private static void Replace(MarkupNode root, string innerXml)
    {
        var old = new List<MarkupNode>();
        foreach (MarkupNode c in root.Children)
        {
            old.Add(c);
        }

        root.AppendXml(Encoding.UTF8.GetBytes(innerXml));
        foreach (MarkupNode c in old)
        {
            c.Remove();
        }
    }

    // A comment or footnote anchored in a repeated paragraph is copied in its part, so every anchor points at its own text.
    private static void AddClones(RenderContext ctx)
    {
        Document doc = ctx.Document;
        string ns = doc.WordprocessingNamespaceUri;
        if (ctx.RootIds.CommentClones.Count > 0 && doc.LoadRelatedByType(ctx.RelationshipType("comments"), out _) is { } comments)
        {
            foreach ((string old, string fresh) in ctx.RootIds.CommentClones)
            {
                CopyElement(comments.Root, ns, "comment", old, fresh, ctx);
            }
        }

        foreach ((string old, string fresh, bool endnote) in ctx.RootIds.NoteClones)
        {
            if (doc.LoadRelatedByType(ctx.RelationshipType(endnote ? "endnotes" : "footnotes"), out _) is { } notes)
            {
                CopyElement(notes.Root, ns, endnote ? "endnote" : "footnote", old, fresh, ctx);
            }
        }
    }

    private static void CopyElement(MarkupNode root, string ns, string name, string oldId, string newId, RenderContext ctx)
    {
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.IsElement(ns, name) && c.GetAttribute(ns, "id") == oldId)
            {
                root.AppendXml(Encoding.UTF8.GetBytes(ctx.RootIds.Reid(c.OuterXml, newId)));
                return;
            }
        }
    }
}
