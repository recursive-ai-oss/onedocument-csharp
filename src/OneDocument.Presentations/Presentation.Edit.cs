using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;
using P = RecursiveAI.OneDocument.Schema.Presentation;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    private const string TransitionalRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StrictRelationships = "http://purl.oclc.org/ooxml/officeDocument/relationships/";
    private const string SlideContentType = "application/vnd.openxmlformats-officedocument.presentationml.slide+xml";
    private const string NotesSlideContentType = "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml";

    // Relationship types whose targets are shared by every slide that uses them: pictures, media, fonts, layouts and masters, themes
    // and links to other slides. Everything else a slide relates to (charts, diagrams, embedded objects, tags, comments, notes) belongs
    // to that slide alone, so a duplicate gets its own copy.
    private static readonly HashSet<string> SharedRelationships = new(StringComparer.Ordinal)
    {
        "image", "hyperlink", "slideLayout", "slideMaster", "notesMaster", "handoutMaster", "theme", "video", "audio", "media", "hdphoto",
        "font", "slide",
    };

    private string RelationshipType(string name) => (IsStrict ? StrictRelationships : TransitionalRelationships) + name;

    /// <summary>
    /// Adds a slide based on a layout, with an empty placeholder for each of the layout's content placeholders (the date, footer and
    /// slide number placeholders are left off, as PowerPoint does), and puts it in the slide list.
    /// </summary>
    /// <param name="layout">A layout of this presentation.</param>
    /// <param name="index">Where the slide goes (0 is first); null appends it. A slide added after another joins that slide's section.</param>
    /// <returns>The new slide.</returns>
    /// <exception cref="ArgumentException">The layout belongs to another presentation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0 to the number of slides.</exception>
    public Slide AddSlide(SlideLayout layout, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ThrowIfDisposed();
        if (!ReferenceEquals(layout.Owner, this))
        {
            throw new ArgumentException("The layout belongs to another presentation.", nameof(layout));
        }

        List<SlideEntry> entries = ReadSlideEntries();
        int at = index ?? entries.Count;
        ArgumentOutOfRangeException.ThrowIfNegative(at);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(at, entries.Count);

        _ = Markup; // the presentation part must be readable before anything is added to the package
        string partName = NextPartName("/ppt/slides/slide{0}.xml");
        Package.AddPart(partName, SlideContentType, Encoding.UTF8.GetBytes(NewSlideXml(layout)));
        Package.SetRelationships(partName, [new Relationship("rId1", RelationshipType("slideLayout"), RelativeTarget(partName, layout.PartName))]);
        long id = InsertSlideEntry(partName, at, entries);
        _ = id;
        return GetPart(partName, static (p, n) => new Slide(p, n));
    }

    private string NewSlideXml(SlideLayout layout)
    {
        bool strict = IsStrict;
        string a = strict ? Pml.AStrict : Pml.ATransitional;
        string r = strict ? Pml.RStrict : Pml.RTransitional;
        string p = strict ? Pml.PStrict : Pml.PTransitional;
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<p:sld xmlns:a=\"").Append(a).Append("\" xmlns:r=\"").Append(r).Append("\" xmlns:p=\"").Append(p).Append("\"><p:cSld><p:spTree>");
        sb.Append("<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>");
        int next = 2;
        foreach (Shape shape in layout.Shapes)
        {
            if (shape.Kind != ShapeKind.Shape || shape.Placeholder is not { } ph || ph.Type is "dt" or "ftr" or "sldNum" or "hdr")
            {
                continue;
            }

            // ECMA-376 Part 1 §19.3.1.36 (ph): the slide's placeholder repeats the layout's type, idx, sz and orient so the two match.
            sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(next++.ToString(CultureInfo.InvariantCulture)).Append("\" name=\"").Append(Pml.Escape(shape.Name)).Append("\"/>");
            sb.Append("<p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph");
            if (ph.HasType)
            {
                sb.Append(" type=\"").Append(Pml.Escape(ph.Type)).Append('"');
            }

            if (ph.Orientation is not null)
            {
                sb.Append(" orient=\"").Append(Pml.Escape(ph.Orientation)).Append('"');
            }

            if (ph.Size is not null)
            {
                sb.Append(" sz=\"").Append(Pml.Escape(ph.Size)).Append('"');
            }

            if (ph.Index != 0)
            {
                sb.Append(" idx=\"").Append(ph.Index.ToString(CultureInfo.InvariantCulture)).Append('"');
            }

            sb.Append("/></p:nvPr></p:nvSpPr><p:spPr/>");
            if (ph.Type is "title" or "ctrTitle" or "subTitle" or "body" or "obj")
            {
                sb.Append("<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody>");
            }

            sb.Append("</p:sp>");
        }

        sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
        return sb.ToString();
    }

    /// <summary>
    /// Duplicates a slide. The copy has the same layout, shape ids and content; it is placed right after the original unless told
    /// otherwise, and joins the original's section. Pictures, media, fonts and links are shared with the original (the same parts); charts,
    /// diagrams, embedded objects, tags, comments and the notes page are copied, so editing one slide never changes the other.
    /// </summary>
    /// <param name="slide">A slide of this presentation.</param>
    /// <param name="index">Where the copy goes (0 is first); null puts it after the original.</param>
    /// <returns>The new slide.</returns>
    /// <exception cref="ArgumentException">The slide belongs to another presentation or is not in the slide list.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0 to the number of slides.</exception>
    public Slide DuplicateSlide(Slide slide, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ThrowIfDisposed();
        List<SlideEntry> entries = ReadSlideEntries();
        SlideEntry source = FindEntry(slide, entries);
        int at = index ?? source.Index + 1;
        ArgumentOutOfRangeException.ThrowIfNegative(at);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(at, entries.Count);

        CommitChanges();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string copy = CopyPart(source.PartName, map);
        long id = InsertSlideEntry(copy, at, entries, joinSectionOf: at == source.Index + 1 ? source.Id : null);
        UpdateCreationId(copy, id);
        return GetPart(copy, static (p, n) => new Slide(p, n));
    }

    private SlideEntry FindEntry(Slide slide, List<SlideEntry> entries)
    {
        if (!ReferenceEquals(slide.Owner, this))
        {
            throw new ArgumentException("The slide belongs to another presentation.", nameof(slide));
        }

        foreach (SlideEntry e in entries)
        {
            if (string.Equals(e.PartName, slide.PartName, StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }

        throw new ArgumentException("The slide is not in the presentation's slide list.", nameof(slide));
    }

    // Copies a part and, depth first, the parts it owns; 'map' holds every old name already copied (or standing for itself).
    private string CopyPart(string source, Dictionary<string, string> map)
    {
        IPackagePart original = Package.FindPart(source)!;
        string name = CopyName(source);
        map[source] = name;
        Package.AddPart(name, original.ContentType, original.GetBytes());
        var rels = new List<Relationship>();
        foreach (Relationship r in Package.GetRelationships(source))
        {
            string? target = r.TargetMode == RelationshipTargetMode.Internal ? ResolveTarget(source, r) : null;
            if (target is null)
            {
                rels.Add(r);
                continue;
            }

            string newTarget;
            if (map.TryGetValue(target, out string? mapped))
            {
                newTarget = mapped;
            }
            else if (SharedRelationships.Contains(Pml.RelName(r.Type)))
            {
                newTarget = target;
            }
            else
            {
                newTarget = CopyPart(target, map);
            }

            rels.Add(new Relationship(r.Id, r.Type, RelativeTarget(name, newTarget)));
        }

        if (rels.Count > 0)
        {
            Package.SetRelationships(name, rels);
        }

        return name;
    }

    // A free name like the original's: the trailing number of the file name is replaced by the next unused one.
    private string CopyName(string source)
    {
        int slash = source.LastIndexOf('/');
        int dot = source.LastIndexOf('.');
        string dir = source[..(slash + 1)];
        string ext = dot > slash ? source[dot..] : string.Empty;
        string stem = source[(slash + 1)..(dot > slash ? dot : source.Length)];
        string prefix = stem.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        for (int i = 1; ; i++)
        {
            string candidate = dir + prefix + i.ToString(CultureInfo.InvariantCulture) + ext;
            if (Package.FindPart(candidate) is null)
            {
                return candidate;
            }
        }
    }

    private string NextPartName(string pattern)
    {
        for (int i = 1; ; i++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, pattern, i);
            if (Package.FindPart(candidate) is null)
            {
                return candidate;
            }
        }
    }

    // The relationship target (relative to the source part's folder) that reaches 'target' (ECMA-376 Part 2 §9.3.3).
    internal static string RelativeTarget(string source, string target)
    {
        string[] from = source.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] to = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        while (common < from.Length - 1 && common < to.Length - 1 && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        var sb = new StringBuilder();
        for (int i = common; i < from.Length - 1; i++)
        {
            sb.Append("../");
        }

        sb.Append(string.Join('/', to[common..]));
        return sb.ToString();
    }

    // Adds the presentation's relationship and slide-list entry for a slide part, and its section entry. Returns the new slide id.
    private long InsertSlideEntry(string partName, int at, List<SlideEntry> entries, long? joinSectionOf = null)
    {
        var rels = new List<Relationship>(Package.GetRelationships(MainPartName));
        int n = 1;
        while (rels.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        string rid = "rId" + n.ToString(CultureInfo.InvariantCulture);
        rels.Add(new Relationship(rid, RelationshipType("slide"), RelativeTarget(MainPartName, partName)));
        Package.SetRelationships(MainPartName, rels);

        // ECMA-376 Part 1 §19.2.1.34 (sldId): ids are at least 256 and unique.
        long id = 255;
        foreach (SlideEntry e in entries)
        {
            id = Math.Max(id, e.Id);
        }

        id++;
        MarkupRef list = View.GetOrAddSldIdLst().Markup;
        var prefixes = new Prefixes(list.Node);
        string entryXml = TextBody.Fill("<{p}:sldId id=\"" + id.ToString(CultureInfo.InvariantCulture) + "\" {r}:id=\"" + rid + "\"/>", prefixes);
        if (at >= entries.Count)
        {
            list.Node.AppendXml(Pml.Utf8(entryXml));
        }
        else
        {
            entries[at].Element.Node.InsertXmlBefore(Pml.Utf8(entryXml));
        }

        long? after = joinSectionOf ?? (at > 0 ? entries[at - 1].Id : null);
        SectionInsert(id, after);
        return id;
    }

    // Puts a slide id into the section of the slide it follows (or the first section when it leads).
    private void SectionInsert(long id, long? afterId)
    {
        MarkupRef? sectionList = SectionListElement();
        if (sectionList is null)
        {
            return;
        }

        List<MarkupRef> sections = Pml.Elements(sectionList.Value, SchemaNamespace.Presentation2010, "section");
        if (sections.Count == 0)
        {
            return;
        }

        string idText = id.ToString(CultureInfo.InvariantCulture);
        const string Ns = "xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\"";
        if (afterId is { } prev)
        {
            foreach (MarkupRef section in sections)
            {
                if (Pml.Child(section, SchemaNamespace.Presentation2010, "sldIdLst") is not { } idList)
                {
                    continue;
                }

                foreach (MarkupRef entry in Pml.Elements(idList, SchemaNamespace.Presentation2010, "sldId"))
                {
                    if (Pml.AttrLong(entry, "id") == prev)
                    {
                        entry.Node.InsertXmlAfter(Pml.Utf8(Wrap14(entry.Node, "<{p14}:sldId id=\"" + idText + "\"/>")));
                        return;
                    }
                }
            }

            // The slide it follows is in no section: the new one joins the last section.
            AppendToSection(sections[^1], idText);
            return;
        }

        _ = Ns;
        AppendToSection(sections[0], idText, first: true);
    }

    private static string Wrap14(MarkupNode context, string template)
    {
        const string Uri = "http://schemas.microsoft.com/office/powerpoint/2010/main";
        if (context.TryGetPrefixInScope(Uri, out string prefix) && prefix.Length > 0)
        {
            return template.Replace("{p14}", prefix, StringComparison.Ordinal);
        }

        return template.Replace("<{p14}:sldId", "<p14:sldId xmlns:p14=\"" + Uri + "\"", StringComparison.Ordinal);
    }

    private static void AppendToSection(MarkupRef section, string idText, bool first = false)
    {
        string entry = "<{p14}:sldId id=\"" + idText + "\"/>";
        if (Pml.Child(section, SchemaNamespace.Presentation2010, "sldIdLst") is { } idList)
        {
            byte[] xml = Pml.Utf8(Wrap14(idList.Node, entry));
            if (first)
            {
                idList.Node.PrependXml(xml);
            }
            else
            {
                idList.Node.AppendXml(xml);
            }

            return;
        }

        // A section with no list yet (CT_Section puts sldIdLst first, before extLst).
        section.Node.PrependXml(Pml.Utf8(Wrap14(section.Node, "<{p14}:sldIdLst>" + entry.Replace("{p14}", "{p14}", StringComparison.Ordinal) + "</{p14}:sldIdLst>").Replace("<{p14}:sldIdLst>", "<{p14}:sldIdLst>", StringComparison.Ordinal)));
    }

    private void SectionRemove(long id)
    {
        MarkupRef? sectionList = SectionListElement();
        if (sectionList is null)
        {
            return;
        }

        foreach (MarkupRef section in Pml.Elements(sectionList.Value, SchemaNamespace.Presentation2010, "section"))
        {
            if (Pml.Child(section, SchemaNamespace.Presentation2010, "sldIdLst") is not { } idList)
            {
                continue;
            }

            foreach (MarkupRef entry in Pml.Elements(idList, SchemaNamespace.Presentation2010, "sldId"))
            {
                if (Pml.AttrLong(entry, "id") == id)
                {
                    entry.Node.Remove();
                    return;
                }
            }
        }
    }

    // A duplicate must not share the original's creation id (p14:creationId): give it one derived from its slide id.
    private void UpdateCreationId(string partName, long slideId)
    {
        MarkupDocument? doc = Parts.Load(partName, required: false);
        if (doc is null || doc.Root.IsNull)
        {
            return;
        }

        MarkupRef? extLst = Pml.P(new MarkupRef(doc.Root), "extLst");
        if (extLst is null)
        {
            return;
        }

        foreach (MarkupRef ext in Pml.Elements(extLst.Value, SchemaNamespace.Presentation, "ext"))
        {
            if (Pml.Child(ext, SchemaNamespace.Presentation2010, "creationId") is { } creation)
            {
                // Deterministic, odd-multiplier hash of the slide id into the unsigned 32-bit range creationId allows.
                uint value = unchecked((uint)(slideId * 2654435761L));
                creation.Node.SetAttribute("val", value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>
    /// Deletes a slide: removes it from the slide list, its section and every custom show, and removes its part together with the parts
    /// only it used (its notes page, comments, charts, diagrams, embedded objects and pictures no other part uses). Layouts, masters,
    /// themes and parts other slides use stay. The slide object must not be used afterwards.
    /// </summary>
    /// <param name="slide">A slide of this presentation.</param>
    /// <exception cref="ArgumentException">The slide belongs to another presentation or is not in the slide list.</exception>
    /// <remarks>Document properties (<c>docProps/app.xml</c>) are not rewritten: the slide count and titles they hold are statistics PowerPoint recomputes when it saves.</remarks>
    public void DeleteSlide(Slide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ThrowIfDisposed();
        List<SlideEntry> entries = ReadSlideEntries();
        SlideEntry entry = FindEntry(slide, entries);

        entry.Element.Node.Remove();
        SectionRemove(entry.Id);
        RemoveFromCustomShows(entry.RelationshipId);
        var rels = new List<Relationship>(Package.GetRelationships(MainPartName));
        rels.RemoveAll(r => string.Equals(r.Id, entry.RelationshipId, StringComparison.Ordinal));
        Package.SetRelationships(MainPartName, rels);

        // The slide's part and what only it owns. The closure follows owned relationships only; shared ones (layout, master, theme,
        // links to other slides) stay out.
        var doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entry.PartName };
        var queue = new Queue<string>();
        queue.Enqueue(entry.PartName);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach (Relationship r in Package.GetRelationships(current))
            {
                if (ResolveTarget(current, r) is not { } target || doomed.Contains(target))
                {
                    continue;
                }

                string kind = Pml.RelName(r.Type);
                if (kind is "slideLayout" or "slideMaster" or "notesMaster" or "handoutMaster" or "theme" or "slide")
                {
                    continue;
                }

                if (candidates.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        // Remove a candidate when no part outside the removal set still points at it; repeat, because removing one can free another.
        bool changed = true;
        while (changed)
        {
            changed = false;
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Relationship r in Package.GetRelationships())
            {
                if (ResolveTarget(null, r) is { } t)
                {
                    referenced.Add(t);
                }
            }

            foreach (IPackagePart part in Package.Parts)
            {
                if (doomed.Contains(part.Name))
                {
                    continue;
                }

                foreach (Relationship r in Package.GetRelationships(part.Name))
                {
                    if (ResolveTarget(part.Name, r) is { } t)
                    {
                        referenced.Add(t);
                    }
                }
            }

            foreach (string c in candidates)
            {
                if (!doomed.Contains(c) && !referenced.Contains(c))
                {
                    doomed.Add(c);
                    changed = true;
                }
            }
        }

        foreach (string name in doomed)
        {
            ForgetPart(name);
            Package.RemovePart(name);
        }
    }

    private void RemoveFromCustomShows(string relationshipId)
    {
        MarkupRef? list = Pml.P(Markup, "custShowLst");
        if (list is null)
        {
            return;
        }

        foreach (MarkupRef show in Pml.Elements(list.Value, SchemaNamespace.Presentation, "custShow"))
        {
            if (Pml.P(show, "sldLst") is not { } sldLst)
            {
                continue;
            }

            foreach (MarkupRef sld in Pml.Elements(sldLst, SchemaNamespace.Presentation, "sld"))
            {
                if (string.Equals(Pml.RelAttr(sld, "id"), relationshipId, StringComparison.Ordinal))
                {
                    sld.Node.Remove();
                }
            }
        }
    }

    /// <summary>Moves a slide to a new position in the slide list. Its id, part and relationships do not change; its section follows its new neighbour.</summary>
    /// <param name="slide">A slide of this presentation.</param>
    /// <param name="newIndex">The slide's position after the move (0 is first).</param>
    /// <exception cref="ArgumentException">The slide belongs to another presentation or is not in the slide list.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0 to the number of slides minus one.</exception>
    public void MoveSlide(Slide slide, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ThrowIfDisposed();
        List<SlideEntry> entries = ReadSlideEntries();
        SlideEntry entry = FindEntry(slide, entries);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, entries.Count);
        if (newIndex == entry.Index)
        {
            return;
        }

        string xml = entry.Element.Node.OuterXml;
        MarkupNode list = entry.Element.Node.Parent;
        entry.Element.Node.Remove();
        List<SlideEntry> rest = ReadSlideEntries();
        if (newIndex >= rest.Count)
        {
            list.AppendXml(Pml.Utf8(xml));
        }
        else
        {
            rest[newIndex].Element.Node.InsertXmlBefore(Pml.Utf8(xml));
        }

        SectionRemove(entry.Id);
        SectionInsert(entry.Id, newIndex > 0 ? rest[newIndex - 1].Id : null);
    }
}

public sealed partial class Slide
{
    /// <summary>
    /// Sets the speaker notes. The text goes into the notes page's body placeholder (see <see cref="TextBody.SetText"/>); a slide without
    /// a notes page gets one, based on the notes master.
    /// </summary>
    /// <param name="text">The notes; empty clears them.</param>
    /// <returns>The notes page.</returns>
    /// <exception cref="InvalidOperationException">The slide has no notes page and the presentation has no notes master to base one on.</exception>
    public NotesSlide SetNotesText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        NotesSlide notes = Notes ?? Owner.CreateNotes(this);
        if (notes.Body is { } body)
        {
            body.SetText(text);
        }

        return notes;
    }
}

public sealed partial class Presentation
{
    internal NotesSlide CreateNotes(Slide slide)
    {
        NotesMaster master = NotesMaster ?? throw new InvalidOperationException("The presentation has no notes master to base a notes page on.");
        bool strict = IsStrict;
        string a = strict ? Pml.AStrict : Pml.ATransitional;
        string r = strict ? Pml.RStrict : Pml.RTransitional;
        string p = strict ? Pml.PStrict : Pml.PTransitional;
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<p:notes xmlns:a=\"" + a + "\" xmlns:r=\"" + r + "\" xmlns:p=\"" + p + "\"><p:cSld><p:spTree>" +
            "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/>" +
            "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>" +
            "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>";
        string name = NextPartName("/ppt/notesSlides/notesSlide{0}.xml");
        Package.AddPart(name, NotesSlideContentType, Encoding.UTF8.GetBytes(xml));
        Package.SetRelationships(name,
        [
            new Relationship("rId1", RelationshipType("notesMaster"), RelativeTarget(name, master.PartName)),
            new Relationship("rId2", RelationshipType("slide"), RelativeTarget(name, slide.PartName)),
        ]);
        var slideRels = new List<Relationship>(Package.GetRelationships(slide.PartName));
        int n = 1;
        while (slideRels.Exists(x => x.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        slideRels.Add(new Relationship("rId" + n.ToString(CultureInfo.InvariantCulture), RelationshipType("notesSlide"), RelativeTarget(slide.PartName, name)));
        Package.SetRelationships(slide.PartName, slideRels);
        return GetPart(name, static (pr, nm) => new NotesSlide(pr, nm));
    }
}
