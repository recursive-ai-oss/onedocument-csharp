using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>Whose master, layouts and theme inserted slides use.</summary>
public enum SlideFormatting
{
    /// <summary>The slides keep the layout and master they had (PowerPoint's "Keep source formatting"): the source's master, its layouts and theme are added to the destination unless it already has an identical one.</summary>
    KeepSource = 0,

    /// <summary>The slides take the destination's look ("Use destination theme"): each gets the destination layout with the same name, else the same type, else the first.</summary>
    UseDestination = 1,
}

/// <summary>Choices for inserting slides from another presentation.</summary>
public sealed class SlideInsertOptions
{
    /// <summary>Gets or sets whose master, layouts and theme the slides use. Default <see cref="SlideFormatting.KeepSource"/>.</summary>
    public SlideFormatting Formatting { get; set; } = SlideFormatting.KeepSource;
}

/// <summary>The slides an insert made and what it did.</summary>
public sealed class SlideInsertResult
{
    internal SlideInsertResult(IReadOnlyList<Slide> slides, MergeReport report)
    {
        Slides = slides;
        Report = report;
    }

    /// <summary>Gets the new slides in order.</summary>
    public IReadOnlyList<Slide> Slides { get; }

    /// <summary>Gets the report: counts (<c>slides</c>, <c>masters</c>, <c>layouts</c>, <c>images</c>, <c>parts</c>), masters reused or copied and layouts mapped as resolutions, what was dropped as loss items.</summary>
    public MergeReport Report { get; }
}

public sealed partial class Presentation
{
    private const string SlideMasterContentType = "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml";

    /// <summary>
    /// Inserts copies of slides of another presentation. Each slide arrives with the parts only it uses (pictures by content, charts with
    /// their workbooks, diagrams, embedded objects) and, with <see cref="SlideFormatting.KeepSource"/>, with its own master, layouts and theme.
    /// </summary>
    /// <param name="slides">Slides of one other presentation, in the order they are inserted. Slides of this presentation are duplicated.</param>
    /// <param name="index">Where the first new slide goes (0 is first); null appends. A slide inserted after another joins that slide's section.</param>
    /// <param name="options">Choices; null for the defaults.</param>
    /// <returns>The new slides and the report.</returns>
    /// <remarks>
    /// Notes pages come along when this presentation has a notes master; comments are not copied; links to other slides point at the slide
    /// itself. Shapes are not scaled when the slide sizes differ (a loss item says so). Transitions, animations and everything else in the
    /// slide part are kept as written.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="slides"/> is null.</exception>
    /// <exception cref="ArgumentException">The slides belong to more than one presentation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0 to the number of slides.</exception>
    /// <exception cref="OfficeUnsupportedException">One presentation is Strict and the other Transitional.</exception>
    public SlideInsertResult InsertSlides(IEnumerable<Slide> slides, int? index = null, SlideInsertOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ThrowIfDisposed();
        List<Slide> list = [.. slides];
        var report = new MergeReport();
        var created = new List<Slide>();
        if (list.Count == 0)
        {
            return new SlideInsertResult(created, report);
        }

        Presentation source = list[0].Owner;
        if (list.Any(s => !ReferenceEquals(s.Owner, source)))
        {
            throw new ArgumentException("The slides belong to more than one presentation.", nameof(slides));
        }

        int at = index ?? ReadSlideEntries().Count;
        ArgumentOutOfRangeException.ThrowIfNegative(at);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(at, ReadSlideEntries().Count);
        if (ReferenceEquals(source, this))
        {
            foreach (Slide s in list)
            {
                created.Add(DuplicateSlide(s, at++));
                report.Count("slides");
            }

            return new SlideInsertResult(created, report);
        }

        if (source.IsStrict != IsStrict)
        {
            throw new OfficeUnsupportedException("Combining a Strict presentation with a Transitional one", DefectLocation.InPart(MainPartName));
        }

        source.CommitChanges();
        CommitChanges();
        var inserter = new SlideInserter(source, this, options ?? new SlideInsertOptions(), report);
        if (source.SlideSize is { } a && SlideSize is { } b && (a.Width != b.Width || a.Height != b.Height))
        {
            report.Loss.Report("merge.slide-size", "Slide size", list.Count,
                "The source slides are " + a.Width.ToString(CultureInfo.InvariantCulture) + " by " + a.Height.ToString(CultureInfo.InvariantCulture) + " EMU and this presentation's are "
                + b.Width.ToString(CultureInfo.InvariantCulture) + " by " + b.Height.ToString(CultureInfo.InvariantCulture) + "; shapes were not scaled.");
        }

        foreach (Slide s in list)
        {
            string newName = inserter.CopySlide(s.PartName);
            long id = InsertSlideEntry(newName, at++, ReadSlideEntries());
            UpdateCreationId(newName, id);
            created.Add(GetPart(newName, static (p, n) => new Slide(p, n)));
            report.Count("slides");
        }

        return new SlideInsertResult(created, report);
    }

    /// <summary>
    /// Makes a presentation of its own from a range of this one's slides: the slides, their notes, the masters they use with all those
    /// masters' layouts, and the parts they use. Masters no kept slide uses, and everything only they used, are left out.
    /// </summary>
    /// <param name="start">Index of the first slide to keep.</param>
    /// <param name="count">How many slides to keep.</param>
    /// <returns>The new presentation, which the caller disposes. This presentation is not changed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside the slides, or empty.</exception>
    public Presentation ExtractSlides(int start, int count)
    {
        ThrowIfDisposed();
        List<SlideEntry> entries = ReadSlideEntries();
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + count, entries.Count);
        CommitChanges();
        var ms = new MemoryStream();
        try
        {
            SaveAs(ms, Format);
        }
        catch (Exception ex) when (ex is OfficeUnsupportedException or FormatNotAvailableException)
        {
            ms.SetLength(0);
            SaveAs(ms, OfficeFormat.Pptx);
        }

        ms.Position = 0;
        Presentation copy = Open(ms, new OpenContext(Context.Limits), leaveOpen: false);
        List<Slide> all = [.. copy.Slides];
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (i < start || i >= start + count)
            {
                copy.DeleteSlide(all[i]);
            }
        }

        copy.RemoveUnusedMasters();
        return copy;
    }

    /// <summary>Splits the presentation into presentations of at most <paramref name="slidesPerPart"/> slides each (see <see cref="ExtractSlides"/>).</summary>
    /// <param name="slidesPerPart">Slides in each part, at least 1.</param>
    /// <returns>The parts in order; the caller disposes them.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slidesPerPart"/> is less than 1.</exception>
    public IReadOnlyList<Presentation> SplitEvery(int slidesPerPart)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slidesPerPart, 1);
        int total = ReadSlideEntries().Count;
        var parts = new List<Presentation>();
        try
        {
            for (int i = 0; i < total; i += slidesPerPart)
            {
                parts.Add(ExtractSlides(i, Math.Min(slidesPerPart, total - i)));
            }
        }
        catch
        {
            foreach (Presentation p in parts)
            {
                p.Dispose();
            }

            throw;
        }

        return parts;
    }

    // Removes the masters no slide uses (through its layout), their list entries and relationships, and then the parts nothing reaches.
    private void RemoveUnusedMasters()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SlideEntry e in ReadSlideEntries())
        {
            foreach (Relationship r in Package.GetRelationships(e.PartName))
            {
                if (Pml.RelIs(r.Type, "slideLayout") && ResolveTarget(e.PartName, r) is { } layout)
                {
                    foreach (Relationship m in Package.GetRelationships(layout))
                    {
                        if (Pml.RelIs(m.Type, "slideMaster") && ResolveTarget(layout, m) is { } master)
                        {
                            used.Add(master);
                        }
                    }
                }
            }
        }

        MarkupRef? ids = Pml.P(Markup, "sldMasterIdLst");
        if (ids is null || used.Count == 0)
        {
            return;
        }

        var rels = new List<Relationship>(Package.GetRelationships(MainPartName));
        foreach (MarkupRef entry in Pml.Elements(ids.Value, SchemaNamespace.Presentation, "sldMasterId").ToList())
        {
            string? rid = Pml.RelAttr(entry, "id");
            Relationship? rel = rid is null ? null : FindRelationship(MainPartName, rid);
            string? target = rel is null ? null : ResolveTarget(MainPartName, rel);
            if (target is not null && !used.Contains(target))
            {
                entry.Node.Remove();
                rels.RemoveAll(r => r.Id == rid);
            }
        }

        Package.SetRelationships(MainPartName, rels);
        CommitChanges();
        OpcGraph.RemoveUnreachableParts(Package);
    }

    // ---- the copy of one slide ---------------------------------------------------------------------------------------------------

    private sealed class SlideInserter
    {
        private readonly Presentation _src;
        private readonly Presentation _dst;
        private readonly SlideInsertOptions _o;
        private readonly MergeReport _r;
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _layoutMap = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string>? _leaf;
        private long _dropped;

        public SlideInserter(Presentation src, Presentation dst, SlideInsertOptions o, MergeReport r)
        {
            _src = src;
            _dst = dst;
            _o = o;
            _r = r;
        }

        public string CopySlide(string srcSlide)
        {
            IPackagePart original = _src.Package.FindPart(srcSlide)!;
            string name = _dst.NextPartName("/ppt/slides/slide{0}.xml");
            _map[srcSlide] = name;
            _dst.Package.AddPart(name, original.ContentType, original.GetBytes());
            var rels = new List<Relationship>();
            foreach (Relationship r in _src.Package.GetRelationships(srcSlide))
            {
                string type = Pml.RelName(r.Type);
                if (r.TargetMode == RelationshipTargetMode.External)
                {
                    rels.Add(r);
                    continue;
                }

                string? target = _src.ResolveTarget(srcSlide, r);
                if (target is null)
                {
                    continue;
                }

                string? newTarget = type switch
                {
                    "slideLayout" => MapLayout(target),
                    "notesSlide" => CopyNotes(target, name),
                    "comments" or "commentAuthors" => Drop("comments"),
                    "slide" => name,
                    _ => CopyPart(target),
                };
                if (newTarget is not null)
                {
                    rels.Add(new Relationship(r.Id, r.Type, RelativeTarget(name, newTarget)));
                }
            }

            _dst.Package.SetRelationships(name, rels);
            return name;
        }

        private string? Drop(string what)
        {
            _dropped++;
            _r.Loss.Report("merge." + what + "-dropped", char.ToUpperInvariant(what[0]) + what[1..], 1, "The source slide's " + what + " were not copied.");
            return null;
        }

        private string? CopyNotes(string srcNotes, string newSlide)
        {
            string? destMaster = _dst.Package.GetRelationships(_dst.MainPartName).Where(r => Pml.RelIs(r.Type, "notesMaster")).Select(r => _dst.ResolveTarget(_dst.MainPartName, r)).FirstOrDefault(t => t is not null);
            if (destMaster is null)
            {
                return Drop("notes");
            }

            IPackagePart original = _src.Package.FindPart(srcNotes)!;
            string name = _dst.NextPartName("/ppt/notesSlides/notesSlide{0}.xml");
            _map[srcNotes] = name;
            _dst.Package.AddPart(name, original.ContentType, original.GetBytes());
            var rels = new List<Relationship>();
            foreach (Relationship r in _src.Package.GetRelationships(srcNotes))
            {
                string type = Pml.RelName(r.Type);
                string? target = r.TargetMode == RelationshipTargetMode.Internal ? _src.ResolveTarget(srcNotes, r) : null;
                if (target is null)
                {
                    rels.Add(r);
                    continue;
                }

                string newTarget = type switch
                {
                    "slide" => newSlide,
                    "notesMaster" => destMaster,
                    _ => CopyPart(target),
                };
                rels.Add(new Relationship(r.Id, r.Type, RelativeTarget(name, newTarget)));
            }

            _dst.Package.SetRelationships(name, rels);
            _r.Count("notes");
            return name;
        }

        // The graph below a part (charts, workbooks, diagrams, objects) is copied whole; pictures and other leaf parts are shared by content.
        private string CopyPart(string srcPart)
        {
            if (_map.TryGetValue(srcPart, out string? done))
            {
                return done;
            }

            IPackagePart original = _src.Package.FindPart(srcPart)!;
            IReadOnlyList<Relationship> children = _src.Package.GetRelationships(srcPart);
            ReadOnlyMemory<byte> bytes = original.GetBytes();
            bool leaf = children.Count == 0 && IsBinary(original.ContentType);
            string hash = string.Empty;
            if (leaf)
            {
                hash = Convert.ToHexString(SHA256.HashData(bytes.Span)) + "|" + original.ContentType;
                _leaf ??= BuildLeafIndex();
                if (_leaf.TryGetValue(hash, out string? existing))
                {
                    _map[srcPart] = existing;
                    _r.Count("parts-shared");
                    return existing;
                }
            }

            string name = _dst.CopyName(srcPart);
            _map[srcPart] = name;
            _dst.Package.AddPart(name, original.ContentType, bytes);
            if (leaf)
            {
                _leaf![hash] = name;
            }

            _r.Count(original.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "images" : "parts");
            var rels = new List<Relationship>();
            foreach (Relationship r in children)
            {
                string? target = r.TargetMode == RelationshipTargetMode.Internal ? _src.ResolveTarget(srcPart, r) : null;
                rels.Add(target is null ? r : new Relationship(r.Id, r.Type, RelativeTarget(name, CopyPart(target))));
            }

            if (rels.Count > 0)
            {
                _dst.Package.SetRelationships(name, rels);
            }

            return name;
        }

        private static bool IsBinary(string contentType) =>
            !(contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase));

        private Dictionary<string, string> BuildLeafIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (IPackagePart p in _dst.Package.Parts)
            {
                if (IsBinary(p.ContentType) && _dst.Package.GetRelationships(p.Name).Count == 0)
                {
                    index.TryAdd(Convert.ToHexString(SHA256.HashData(p.GetBytes().Span)) + "|" + p.ContentType, p.Name);
                }
            }

            return index;
        }

        // ---- layouts and masters ----------------------------------------------------------------------------------------------

        private string MapLayout(string srcLayout)
        {
            if (_layoutMap.TryGetValue(srcLayout, out string? known))
            {
                return known;
            }

            if (_o.Formatting == SlideFormatting.UseDestination)
            {
                return MapToDestinationLayout(srcLayout);
            }

            string? srcMaster = _src.Package.GetRelationships(srcLayout).Where(r => Pml.RelIs(r.Type, "slideMaster")).Select(r => _src.ResolveTarget(srcLayout, r)).FirstOrDefault(t => t is not null);
            if (srcMaster is null)
            {
                return MapToDestinationLayout(srcLayout);
            }

            EnsureMaster(srcMaster);
            return _layoutMap.TryGetValue(srcLayout, out string? mapped) ? mapped : MapToDestinationLayout(srcLayout);
        }

        private string MapToDestinationLayout(string srcLayout)
        {
            SlideLayout source = _src.GetPart(srcLayout, static (p, n) => new SlideLayout(p, n));
            List<SlideLayout> candidates = [.. _dst.Masters.SelectMany(m => m.Layouts)];
            SlideLayout? pick = candidates.FirstOrDefault(l => string.Equals(l.Name, source.Name, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(l => l.Type == source.Type)
                ?? candidates.FirstOrDefault();
            if (pick is null)
            {
                throw new OfficeUnsupportedException("Inserting slides into a presentation without a slide layout", DefectLocation.InPart(_dst.MainPartName));
            }

            _layoutMap[srcLayout] = pick.PartName;
            _r.Resolve(new MergeResolution("merge.layout-mapped", source.Name ?? source.Type, "used destination layout '" + (pick.Name ?? pick.Type) + "'",
                "Slides on the layout '" + (source.Name ?? source.Type) + "' use the destination's layout '" + (pick.Name ?? pick.Type) + "'."));
            return pick.PartName;
        }

        // A master is identified by what it looks like: its own markup, its theme and its layouts in order. An identical one in the
        // destination is reused; otherwise the master, theme and all layouts are copied.
        private void EnsureMaster(string srcMaster)
        {
            List<string> srcLayouts = LayoutsOf(_src, srcMaster);
            string signature = Signature(_src, srcMaster, srcLayouts);
            foreach (SlideMaster m in _dst.Masters)
            {
                List<string> dstLayouts = LayoutsOf(_dst, m.PartName);
                if (dstLayouts.Count == srcLayouts.Count && Signature(_dst, m.PartName, dstLayouts) == signature)
                {
                    for (int i = 0; i < srcLayouts.Count; i++)
                    {
                        _layoutMap[srcLayouts[i]] = dstLayouts[i];
                    }

                    _r.Resolve(new MergeResolution("merge.master-reused", NameOf(_src, srcMaster), "reused", "The destination has an identical slide master and layouts; the slides use them."));
                    return;
                }
            }

            string master = _dst.NextPartName("/ppt/slideMasters/slideMaster{0}.xml");
            _map[srcMaster] = master;
            var layoutNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string l in srcLayouts)
            {
                string n = _dst.NextPartName("/ppt/slideLayouts/slideLayout{0}.xml");
                layoutNames[l] = n;
                _map[l] = n;
                _dst.Package.AddPart(n, _src.Package.FindPart(l)!.ContentType, "<x/>"u8.ToArray());
            }

            _dst.Package.AddPart(master, SlideMasterContentType, "<x/>"u8.ToArray());

            // Unique ids: masters and layouts share one id space of values from 2^31 (ECMA-376 Part 1 §19.2.1.35 sldMasterId, §19.3.1.39 sldLayoutId).
            long next = _dst.MaxMasterOrLayoutId() + 1;
            long masterId = next++;

            // layouts first, so that the master's list can name them
            foreach (string l in srcLayouts)
            {
                string n = layoutNames[l];
                var rels = new List<Relationship>();
                foreach (Relationship r in _src.Package.GetRelationships(l))
                {
                    string? target = r.TargetMode == RelationshipTargetMode.Internal ? _src.ResolveTarget(l, r) : null;
                    string? mapped = target is null ? null : Pml.RelIs(r.Type, "slideMaster") ? master : CopyPart(target);
                    rels.Add(mapped is null ? r : new Relationship(r.Id, r.Type, RelativeTarget(n, mapped)));
                }

                _dst.Package.SetRelationships(n, rels);
                _dst.Package.FindPart(n)!.SetBytes(_src.Package.FindPart(l)!.GetBytes());
                _layoutMap[l] = n;
                _r.Count("layouts");
            }

            // the master: its theme and images are copied, its layout list gets new ids
            var masterRels = new List<Relationship>();
            foreach (Relationship r in _src.Package.GetRelationships(srcMaster))
            {
                string? target = r.TargetMode == RelationshipTargetMode.Internal ? _src.ResolveTarget(srcMaster, r) : null;
                string? mapped = target is null ? null : layoutNames.TryGetValue(target, out string? ln) ? ln : CopyPart(target);
                masterRels.Add(mapped is null ? r : new Relationship(r.Id, r.Type, RelativeTarget(master, mapped)));
            }

            _dst.Package.SetRelationships(master, masterRels);
            MarkupDocument tree = MarkupDocument.Parse(_src.Package.FindPart(srcMaster)!.GetBytes(), _dst.Context, master);
            MarkupRef list = Pml.P(new MarkupRef(tree.Root), "sldLayoutIdLst") ?? default;
            if (list.IsValid)
            {
                foreach (MarkupRef e in Pml.Elements(list, SchemaNamespace.Presentation, "sldLayoutId"))
                {
                    e.Node.SetAttribute("id", (next++).ToString(CultureInfo.InvariantCulture));
                }
            }

            _dst.Package.FindPart(master)!.SetBytes(tree.ToArray());
            _dst.AddMasterEntry(master, masterId);
            _r.Count("masters");
            _r.Resolve(new MergeResolution("merge.master-copied", NameOf(_src, srcMaster), "copied",
                "The destination has no identical slide master; the source's master, its " + srcLayouts.Count.ToString(CultureInfo.InvariantCulture) + " layout(s) and its theme were added."));
        }

        private static string NameOf(Presentation p, string master) => p.GetPart(master, static (pr, n) => new SlideMaster(pr, n)).Name ?? Path.GetFileNameWithoutExtension(master);

        private static List<string> LayoutsOf(Presentation p, string master)
        {
            var list = new List<string>();
            MarkupDocument? tree = p.Parts.Load(master, required: false);
            if (tree is null)
            {
                return list;
            }

            MarkupRef? ids = Pml.P(new MarkupRef(tree.Root), "sldLayoutIdLst");
            if (ids is null)
            {
                return list;
            }

            foreach (MarkupRef e in Pml.Elements(ids.Value, SchemaNamespace.Presentation, "sldLayoutId"))
            {
                string? rid = Pml.RelAttr(e, "id");
                Relationship? rel = rid is null ? null : p.FindRelationship(master, rid);
                if (rel is not null && p.ResolveTarget(master, rel) is { } t)
                {
                    list.Add(t);
                }
            }

            return list;
        }

        // Equal when the master, the theme and each layout have the same markup apart from the ids that number them.
        private static string Signature(Presentation p, string master, List<string> layouts)
        {
            var sb = new StringBuilder();
            sb.Append(Normal(p, master));
            foreach (Relationship r in p.Package.GetRelationships(master))
            {
                if (Pml.RelIs(r.Type, "theme") && p.ResolveTarget(master, r) is { } theme)
                {
                    sb.Append(Convert.ToHexString(SHA256.HashData(p.Package.FindPart(theme)!.GetBytes().Span)));
                }
            }

            foreach (string l in layouts)
            {
                sb.Append('|').Append(Normal(p, l));
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        private static string Normal(Presentation p, string part)
        {
            string xml = Encoding.UTF8.GetString(p.Package.FindPart(part)!.GetBytes().Span);
            return System.Text.RegularExpressions.Regex.Replace(xml, "<(\\w+:)?sldLayoutId [^>]*/>|\\s+r:id=\"[^\"]*\"|\\s+id=\"\\d+\"", string.Empty);
        }
    }

    private long MaxMasterOrLayoutId()
    {
        long max = 2147483647;
        if (Pml.P(Markup, "sldMasterIdLst") is { } ids)
        {
            foreach (MarkupRef e in Pml.Elements(ids, SchemaNamespace.Presentation, "sldMasterId"))
            {
                max = Math.Max(max, Pml.AttrLong(e, "id") ?? 0);
            }
        }

        foreach (SlideMaster m in Masters)
        {
            if (Pml.P(m.Markup, "sldLayoutIdLst") is { } list)
            {
                foreach (MarkupRef e in Pml.Elements(list, SchemaNamespace.Presentation, "sldLayoutId"))
                {
                    max = Math.Max(max, Pml.AttrLong(e, "id") ?? 0);
                }
            }
        }

        return max;
    }

    private void AddMasterEntry(string masterPart, long id)
    {
        var rels = new List<Relationship>(Package.GetRelationships(MainPartName));
        int n = 1;
        while (rels.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        string rid = "rId" + n.ToString(CultureInfo.InvariantCulture);
        rels.Add(new Relationship(rid, RelationshipType("slideMaster"), RelativeTarget(MainPartName, masterPart)));
        Package.SetRelationships(MainPartName, rels);
        MarkupRef list = View.GetOrAddSldMasterIdLst().Markup;
        var prefixes = new Prefixes(list.Node);
        list.Node.AppendXml(Pml.Utf8(TextBody.Fill("<{p}:sldMasterId id=\"" + id.ToString(CultureInfo.InvariantCulture) + "\" {r}:id=\"" + rid + "\"/>", prefixes)));
    }
}
