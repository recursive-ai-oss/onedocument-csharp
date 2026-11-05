using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    private readonly Dictionary<string, PresentationTheme?> _themes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One entry of <c>p:sldIdLst</c>.</summary>
    internal readonly record struct SlideEntry(long Id, string RelationshipId, int Index, string PartName, MarkupRef Element);

    // Reads p:sldIdLst in order. Entries whose relationship or part is missing are reported and skipped; a part listed twice keeps its first entry.
    internal List<SlideEntry> ReadSlideEntries()
    {
        var entries = new List<SlideEntry>();
        MarkupRef? list = Pml.P(Markup, "sldIdLst");
        if (list is null)
        {
            return entries;
        }

        var ids = new HashSet<long>();
        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MarkupRef entry in Pml.Elements(list.Value, SchemaNamespace.Presentation, "sldId"))
        {
            string? rid = Pml.RelAttr(entry, "id");
            Relationship? rel = rid is null ? null : FindRelationship(MainPartName, rid);
            string? target = rel is null ? null : ResolveTarget(MainPartName, rel);
            if (target is null)
            {
                lock (Parts.Gate)
                {
                    if (_reportedSlides.Add("missing:" + rid))
                    {
                        Report("pptx.slide-part-missing", DefectSeverity.Dropped, "A slide listed in p:sldIdLst has no part (relationship " + (rid ?? "none") + "); it is skipped.", MainPartName, entry.Node.SourceStart);
                    }
                }

                continue;
            }

            if (!parts.Add(target))
            {
                lock (Parts.Gate)
                {
                    if (_reportedSlides.Add("twice:" + target))
                    {
                        Report("pptx.slide-listed-twice", DefectSeverity.Dropped, "The slide " + target + " is listed twice in p:sldIdLst; the second entry is skipped.", MainPartName, entry.Node.SourceStart);
                    }
                }

                continue;
            }

            long id = Pml.AttrLong(entry, "id") ?? 0;
            if (!ids.Add(id))
            {
                lock (Parts.Gate)
                {
                    if (_reportedSlides.Add("dupid:" + id))
                    {
                        Report("pptx.slide-id-duplicate", DefectSeverity.Repaired, "Two slides have the id " + id + " in p:sldIdLst; both are kept.", MainPartName, entry.Node.SourceStart);
                    }
                }
            }

            entries.Add(new SlideEntry(id, rid!, entries.Count, target, entry));
        }

        return entries;
    }

    internal SlideEntry? EntryOf(Slide slide)
    {
        foreach (SlideEntry e in ReadSlideEntries())
        {
            if (string.Equals(e.PartName, slide.PartName, StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>Gets the slides in presentation order (<c>p:sldIdLst</c>). A slide whose part is missing is reported (<c>pptx.slide-part-missing</c>) and skipped.</summary>
    /// <remarks>The list is a snapshot: it does not change when slides are added or removed afterwards; read the property again.</remarks>
    public IReadOnlyList<Slide> Slides
    {
        get
        {
            var slides = new List<Slide>();
            foreach (SlideEntry e in ReadSlideEntries())
            {
                slides.Add(GetPart(e.PartName, static (p, n) => new Slide(p, n)));
            }

            return slides;
        }
    }

    /// <summary>Gets the slide masters in the order <c>p:sldMasterIdLst</c> lists them. An entry without a part is reported (<c>pptx.master-missing</c>) and skipped.</summary>
    public IReadOnlyList<SlideMaster> Masters
    {
        get
        {
            var list = new List<SlideMaster>();
            MarkupRef? ids = Pml.P(Markup, "sldMasterIdLst");
            if (ids is null)
            {
                return list;
            }

            foreach (MarkupRef entry in Pml.Elements(ids.Value, SchemaNamespace.Presentation, "sldMasterId"))
            {
                string? rid = Pml.RelAttr(entry, "id");
                Relationship? rel = rid is null ? null : FindRelationship(MainPartName, rid);
                string? target = rel is null ? null : ResolveTarget(MainPartName, rel);
                if (target is null)
                {
                    lock (Parts.Gate)
                    {
                        if (_reportedSlides.Add("master:" + rid))
                        {
                            Report("pptx.master-missing", DefectSeverity.Dropped, "A master listed in p:sldMasterIdLst has no part (relationship " + (rid ?? "none") + "); it is skipped.", MainPartName, entry.Node.SourceStart);
                        }
                    }

                    continue;
                }

                list.Add(GetPart(target, static (p, n) => new SlideMaster(p, n)));
            }

            return list;
        }
    }

    internal long? MasterId(SlideMaster master)
    {
        MarkupRef? ids = Pml.P(Markup, "sldMasterIdLst");
        if (ids is null)
        {
            return null;
        }

        foreach (MarkupRef entry in Pml.Elements(ids.Value, SchemaNamespace.Presentation, "sldMasterId"))
        {
            string? rid = Pml.RelAttr(entry, "id");
            Relationship? rel = rid is null ? null : FindRelationship(MainPartName, rid);
            if (rel is not null && string.Equals(ResolveTarget(MainPartName, rel), master.PartName, StringComparison.OrdinalIgnoreCase))
            {
                return Pml.AttrLong(entry, "id");
            }
        }

        return null;
    }

    /// <summary>Gets the notes master, or null when the presentation has none.</summary>
    public NotesMaster? NotesMaster
    {
        get
        {
            foreach (Relationship r in Package.GetRelationships(MainPartName))
            {
                if (Pml.RelIs(r.Type, "notesMaster") && ResolveTarget(MainPartName, r) is { } target)
                {
                    return GetPart(target, static (p, n) => new NotesMaster(p, n));
                }
            }

            return null;
        }
    }

    internal PresentationTheme? LoadTheme(string? partName)
    {
        if (partName is null)
        {
            return null;
        }

        lock (Parts.Gate)
        {
            if (_themes.TryGetValue(partName, out PresentationTheme? cached))
            {
                return cached;
            }

            MarkupDocument? doc = Parts.Load(partName, required: false);
            PresentationTheme? theme = doc is null || doc.Root.IsNull ? null : PresentationTheme.Read(new MarkupRef(doc.Root));
            _themes[partName] = theme;
            return theme;
        }
    }

    internal bool ReportOnce(string key)
    {
        lock (Parts.Gate)
        {
            return _reportedSlides.Add(key);
        }
    }

    /// <summary>
    /// Gets the sections (<c>p14:sectionLst</c> in the presentation's extension list) in order. Empty when the deck has none; the
    /// slides of all sections together are the slide list, in order.
    /// </summary>
    public IReadOnlyList<PresentationSection> Sections
    {
        get
        {
            var list = new List<PresentationSection>();
            MarkupRef? sectionList = SectionListElement();
            if (sectionList is null)
            {
                return list;
            }

            foreach (MarkupRef section in Pml.Elements(sectionList.Value, SchemaNamespace.Presentation2010, "section"))
            {
                var ids = new List<long>();
                if (Pml.Child(section, SchemaNamespace.Presentation2010, "sldIdLst") is { } idList)
                {
                    foreach (MarkupRef id in Pml.Elements(idList, SchemaNamespace.Presentation2010, "sldId"))
                    {
                        if (Pml.AttrLong(id, "id") is { } v)
                        {
                            ids.Add(v);
                        }
                    }
                }

                list.Add(new PresentationSection(this, Pml.Attr(section, "name") ?? string.Empty, Pml.Attr(section, "id"), ids));
            }

            return list;
        }
    }

    internal MarkupRef? SectionListElement()
    {
        MarkupRef? extLst = Pml.P(Markup, "extLst");
        if (extLst is null)
        {
            return null;
        }

        foreach (MarkupRef ext in Pml.Elements(extLst.Value, SchemaNamespace.Presentation, "ext"))
        {
            if (Pml.Child(ext, SchemaNamespace.Presentation2010, "sectionLst") is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Gets the custom shows (<c>p:custShowLst</c>): named, ordered selections of slides. Entries naming a slide that is not in the presentation are skipped.</summary>
    public IReadOnlyList<CustomShow> CustomShows
    {
        get
        {
            var shows = new List<CustomShow>();
            MarkupRef? list = Pml.P(Markup, "custShowLst");
            if (list is null)
            {
                return shows;
            }

            foreach (MarkupRef show in Pml.Elements(list.Value, SchemaNamespace.Presentation, "custShow"))
            {
                var slides = new List<Slide>();
                if (Pml.P(show, "sldLst") is { } sldLst)
                {
                    foreach (MarkupRef sld in Pml.Elements(sldLst, SchemaNamespace.Presentation, "sld"))
                    {
                        string? rid = Pml.RelAttr(sld, "id");
                        Relationship? rel = rid is null ? null : FindRelationship(MainPartName, rid);
                        if (rel is not null && ResolveTarget(MainPartName, rel) is { } target)
                        {
                            slides.Add(GetPart(target, static (p, n) => new Slide(p, n)));
                        }
                    }
                }

                shows.Add(new CustomShow(Pml.Attr(show, "name") ?? string.Empty, Pml.AttrLong(show, "id"), slides));
            }

            return shows;
        }
    }

    /// <summary>Gets the comment authors, legacy and modern.</summary>
    public IReadOnlyList<CommentAuthor> CommentAuthors => CommentAuthor.Read(this);
}

/// <summary>A section of the slide list (PowerPoint's named groups of slides).</summary>
public sealed class PresentationSection
{
    private readonly Presentation _owner;

    internal PresentationSection(Presentation owner, string name, string? id, IReadOnlyList<long> slideIds)
    {
        _owner = owner;
        Name = name;
        Id = id;
        SlideIds = slideIds;
    }

    /// <summary>Gets the section's name.</summary>
    public string Name { get; }

    /// <summary>Gets the section's id (a GUID in braces), or null.</summary>
    public string? Id { get; }

    /// <summary>Gets the ids (<see cref="Slide.Id"/>) of the section's slides, in order.</summary>
    public IReadOnlyList<long> SlideIds { get; }

    /// <summary>Gets the section's slides in order. Ids that name no slide are skipped.</summary>
    public IReadOnlyList<Slide> Slides
    {
        get
        {
            var slides = new List<Slide>();
            Dictionary<long, Slide> byId = [];
            foreach (Slide s in _owner.Slides)
            {
                if (s.Id is { } id)
                {
                    byId[id] = s;
                }
            }

            foreach (long id in SlideIds)
            {
                if (byId.TryGetValue(id, out Slide? slide))
                {
                    slides.Add(slide);
                }
            }

            return slides;
        }
    }
}

/// <summary>A custom show: a named, ordered selection of slides.</summary>
/// <param name="Name">The show's name.</param>
/// <param name="Id">The show's id, or null.</param>
/// <param name="Slides">The slides in show order.</param>
public sealed record CustomShow(string Name, long? Id, IReadOnlyList<Slide> Slides);

/// <summary>Where a comment author's record lives.</summary>
public enum CommentKind
{
    /// <summary>The classic comments of PowerPoint 2007 to 2019 (<c>p:cmLst</c>).</summary>
    Legacy = 0,

    /// <summary>The modern, threaded comments (<c>p188:cmLst</c>).</summary>
    Modern = 1,
}

/// <summary>A person who commented.</summary>
/// <param name="Kind">Which comment system the author belongs to.</param>
/// <param name="Id">The id comments name the author by (a number for legacy authors, a GUID for modern ones).</param>
/// <param name="Name">The display name.</param>
/// <param name="Initials">The initials, or null.</param>
public sealed record CommentAuthor(CommentKind Kind, string Id, string Name, string? Initials)
{
    internal static List<CommentAuthor> Read(Presentation owner)
    {
        var list = new List<CommentAuthor>();
        foreach (Relationship r in owner.Package.GetRelationships(owner.MainPartName))
        {
            bool legacy = Pml.RelIs(r.Type, "commentAuthors");
            bool modern = Pml.RelIs(r.Type, "authors");
            if (!legacy && !modern || owner.ResolveTarget(owner.MainPartName, r) is not { } target)
            {
                continue;
            }

            MarkupDocument? doc = owner.Parts.Load(target, required: false);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            foreach (MarkupRef author in Pml.Elements(new MarkupRef(doc.Root)))
            {
                string? id = Pml.Attr(author, "id");
                if (id is not null && author.LocalNameText is "cmAuthor" or "author")
                {
                    list.Add(new CommentAuthor(modern ? CommentKind.Modern : CommentKind.Legacy, id, Pml.Attr(author, "name") ?? string.Empty, Pml.Attr(author, "initials")));
                }
            }
        }

        return list;
    }
}

/// <summary>A comment on a slide.</summary>
/// <param name="Kind">Legacy or modern.</param>
/// <param name="Author">The author's display name, or null when the author is not in the author list.</param>
/// <param name="Initials">The author's initials, or null.</param>
/// <param name="Text">The comment text, paragraphs joined by a line feed.</param>
/// <param name="Created">The date as written (<c>dt</c> or <c>created</c>), or null.</param>
/// <param name="Status">A modern comment's status (<c>active</c>, <c>resolved</c>, <c>closed</c>), or null.</param>
/// <param name="Replies">The replies of a modern comment, in order; empty for a legacy comment.</param>
public sealed record SlideComment(CommentKind Kind, string? Author, string? Initials, string Text, string? Created, string? Status, IReadOnlyList<SlideComment> Replies)
{
    internal static List<SlideComment> Read(Slide slide)
    {
        var list = new List<SlideComment>();
        Presentation owner = slide.Owner;
        List<CommentAuthor> authors = CommentAuthor.Read(owner);
        foreach (string part in slide.RelatedPartNames("comments"))
        {
            MarkupDocument? doc = owner.Parts.Load(part, required: false);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            var root = new MarkupRef(doc.Root);
            bool modern = root.Namespace == SchemaNamespace.Presentation201808;
            foreach (MarkupRef cm in Pml.Elements(root))
            {
                if (cm.LocalNameText == "cm")
                {
                    list.Add(ReadOne(cm, modern ? CommentKind.Modern : CommentKind.Legacy, authors));
                }
            }
        }

        return list;
    }

    private static SlideComment ReadOne(MarkupRef cm, CommentKind kind, List<CommentAuthor> authors)
    {
        string? authorId = Pml.Attr(cm, "authorId");
        CommentAuthor? author = authorId is null ? null : authors.Find(a => a.Kind == kind && string.Equals(a.Id, authorId, StringComparison.OrdinalIgnoreCase));
        string text = string.Empty;
        var replies = new List<SlideComment>();
        foreach (MarkupRef child in Pml.Elements(cm))
        {
            switch (child.LocalNameText)
            {
                case "text":
                    text = child.Node.InnerText;
                    break;
                case "txBody":
                    text = TextBody.TextOf(child);
                    break;
                case "replyLst":
                    foreach (MarkupRef reply in Pml.Elements(child))
                    {
                        if (reply.LocalNameText == "reply")
                        {
                            replies.Add(ReadOne(reply, kind, authors));
                        }
                    }

                    break;
            }
        }

        return new SlideComment(kind, author?.Name, author?.Initials, text, Pml.Attr(cm, "dt") ?? Pml.Attr(cm, "created"), Pml.Attr(cm, "status"), replies);
    }
}
