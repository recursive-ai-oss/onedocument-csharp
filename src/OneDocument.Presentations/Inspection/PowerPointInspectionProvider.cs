using System.Linq;
using System.Globalization;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations.Inspection;

/// <summary>
/// Inspects and sanitises what only a presentation holds: comments (classic and modern) and their authors, speaker notes, hidden
/// slides and shapes placed outside the slide. Edits are made on the markup of the parts, so untouched parts stay byte-identical.
/// </summary>
internal sealed class PowerPointInspectionProvider : IInspectionProvider
{
    internal static readonly PowerPointInspectionProvider Instance = new();

    public void Inspect(InspectionSession session) => Run(session, null);

    public void Sanitize(SanitizeSession session) => Run(session, session);

    private static bool Doing(SanitizeSession? z, InspectionCategory c) => z is not null && z.Selected(c);

    private sealed record SlideEntry(MarkupNode Element, string? RelId, IPackagePart Part, int Number, string? SlideId);

    private static void Run(InspectionSession s, SanitizeSession? z)
    {
        if (s.MainPartName is null)
        {
            return;
        }

        IPackagePart? main = s.Package.FindPart(s.MainPartName);
        MarkupDocument? pres = main is null ? null : s.Markup(main);
        if (main is null || pres is null)
        {
            return;
        }

        List<SlideEntry> slides = ReadSlides(s, main, pres);
        Comments(s, z);
        Tags(s, z);
        RevisionData(s, z);
        SpeakerNotes(s, z, slides);
        OffSlide(s, z, main, pres, slides);
        HiddenSlides(s, z, main, pres, slides);
    }

    private static List<SlideEntry> ReadSlides(InspectionSession s, IPackagePart main, MarkupDocument pres)
    {
        var list = new List<SlideEntry>();
        foreach (MarkupNode lst in InspectionXml.Children(pres.Root))
        {
            if (!lst.LocalName.SequenceEqual("sldIdLst"u8))
            {
                continue;
            }

            int n = 0;
            foreach (MarkupNode e in InspectionXml.Children(lst))
            {
                n++;
                string rid = RelId(e);
                foreach (Relationship r in s.Package.GetRelationships(main.Name))
                {
                    if (r.Id == rid && s.Target(main.Name, r) is { } part)
                    {
                        list.Add(new SlideEntry(e, r.Id, part, n, SlideNumberId(e)));
                    }
                }
            }
        }

        return list;
    }

    private static string? SlideNumberId(MarkupNode e)
    {
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length == 0 && a.LocalName.SequenceEqual("id"u8))
            {
                return a.Value;
            }
        }

        return null;
    }

    private static string RelId(MarkupNode e)
    {
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && a.LocalName.SequenceEqual("id"u8))
            {
                return a.Value;
            }
        }

        return string.Empty;
    }

    private static bool IsCommentPart(string ct) =>
        ct.EndsWith("presentationml.comments+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("powerpoint.comments+xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthorsPart(string ct) =>
        ct.EndsWith("presentationml.commentAuthors+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("powerpoint.authors+xml", StringComparison.OrdinalIgnoreCase);

    private static void Comments(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.Comments);
        var authors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (IPackagePart a in s.PartsWhere(IsAuthorsPart))
        {
            if (s.Markup(a) is not { } ad)
            {
                continue;
            }

            foreach (MarkupNode e in InspectionXml.Descendants(ad.DocumentNode))
            {
                string? id = InspectionXml.Attr(e, "id"u8);
                string? name = InspectionXml.Attr(e, "name"u8);
                if (id is not null && name is not null)
                {
                    authors[id] = name;
                }
            }
        }

        foreach (IPackagePart p in s.PartsWhere(IsCommentPart))
        {
            MarkupDocument? doc = s.Markup(p);
            int n = 0;
            var names = new SortedSet<string>(StringComparer.Ordinal);
            if (doc is not null)
            {
                foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
                {
                    if (e.LocalName.SequenceEqual("cm"u8))
                    {
                        n++;
                        string key = InspectionXml.Attr(e, "authorId"u8) ?? string.Empty;
                        names.Add(authors.TryGetValue(key, out string? nm) ? nm : key);
                    }
                }
            }

            s.Add(InspectionCategory.Comments, p.Name, "comments", Math.Max(1, n), names.Count == 0 ? null : "authors: " + string.Join(", ", names));
        }

        foreach (IPackagePart a in s.PartsWhere(IsAuthorsPart))
        {
            var own = new List<string>();
            if (s.Markup(a) is { } od)
            {
                foreach (MarkupNode e in InspectionXml.Descendants(od.DocumentNode))
                {
                    if (InspectionXml.Attr(e, "name"u8) is { } nm && InspectionXml.Attr(e, "id"u8) is not null)
                    {
                        own.Add(nm);
                    }
                }
            }

            s.Add(InspectionCategory.Comments, a.Name, "comment authors", Math.Max(1, own.Count), string.Join(", ", own.Distinct().Take(5)));
        }

        if (!doing)
        {
            return;
        }

        var commentParts = s.PartsWhere(IsCommentPart).Select(p => p.Name).ToList();
        foreach (string name in commentParts)
        {
            int n = s.Markup(name) is { } d ? InspectionXml.Descendants(d.DocumentNode).Count(e => e.LocalName.SequenceEqual("cm"u8)) : 1;
            z!.RemoveEverywhere(name);
            z.Removed(InspectionCategory.Comments, name, "comments", Math.Max(1, n));
        }

        foreach (string name in s.PartsWhere(IsAuthorsPart).Select(p => p.Name).ToList())
        {
            z!.RemoveEverywhere(name);
            z.Removed(InspectionCategory.Comments, name, "comment authors");
        }
    }

    // ECMA-376 Part 1 §19.3.3 tagLst and §19.3.3.x custDataLst: free-form name/value pairs that add-ins store on the presentation, slides
    // and shapes. They are custom data, so they are reported with the custom XML parts.
    private static void Tags(InspectionSession s, SanitizeSession? z)
    {
        foreach (IPackagePart p in s.PartsWhere(ct => ct.EndsWith("presentationml.tags+xml", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            int n = s.Markup(p) is { } d ? Math.Max(1, InspectionXml.Children(d.Root).Count) : 1;
            string name = p.Name;
            s.Add(InspectionCategory.CustomXml, name, "tags (name and value pairs stored by add-ins)", n);
            if (Doing(z, InspectionCategory.CustomXml))
            {
                z!.RemoveEverywhere(name);
                z.Removed(InspectionCategory.CustomXml, name, "tags", n);
            }
        }
    }

    // Co-authoring keeps who edited what and when in parts of their own: the revision list and, since PowerPoint 2016, the change log
    // ([MS-PPTX] changesInfo). The inspector calls them revision tracking data.
    private static void RevisionData(InspectionSession s, SanitizeSession? z)
    {
        foreach (string name in s.PartsWhere(ct => ct.Contains("revisioninfo", StringComparison.OrdinalIgnoreCase) || ct.Contains("changesinfo", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToList())
        {
            s.Add(InspectionCategory.Revisions, name, "co-authoring revision data (who edited the file and when)");
            if (Doing(z, InspectionCategory.Revisions))
            {
                z!.RemoveEverywhere(name);
                z.Removed(InspectionCategory.Revisions, name, "co-authoring revision data");
            }
        }
    }

    private static void SpeakerNotes(InspectionSession s, SanitizeSession? z, List<SlideEntry> slides)
    {
        bool doing = Doing(z, InspectionCategory.SpeakerNotes);
        foreach (IPackagePart p in s.PartsWhere(ct => ct.EndsWith("presentationml.notesSlide+xml", StringComparison.OrdinalIgnoreCase)))
        {
            MarkupDocument? doc = s.Markup(p);
            string text = string.Empty;
            if (doc is not null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
                {
                    // Slide-number and image placeholders in a notes page hold no speaker text; only a:t runs count.
                    if (e.LocalName.SequenceEqual("t"u8) && e.NamespaceUriBytes.IndexOf("drawingml"u8) >= 0)
                    {
                        sb.Append(e.InnerText).Append(' ');
                    }
                }

                text = sb.ToString().Trim();
            }

            if (text.Length > 0)
            {
                s.Add(InspectionCategory.SpeakerNotes, p.Name, "speaker notes", 1, InspectionXml.Brief(text));
            }
        }

        if (!doing)
        {
            return;
        }

        foreach (string name in s.PartsWhere(ct => ct.EndsWith("presentationml.notesSlide+xml", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToList())
        {
            bool hadText = s.Findings.Any(f => f.Category == InspectionCategory.SpeakerNotes && f.Location == name);
            z!.RemoveEverywhere(name);
            if (hadText)
            {
                z.Removed(InspectionCategory.SpeakerNotes, name, "speaker notes");
            }
        }

        _ = slides;
    }

    private static (long W, long H) SlideSize(MarkupDocument pres)
    {
        foreach (MarkupNode e in InspectionXml.Children(pres.Root))
        {
            if (e.LocalName.SequenceEqual("sldSz"u8)
                && long.TryParse(InspectionXml.Attr(e, "cx"u8), NumberStyles.None, CultureInfo.InvariantCulture, out long cx)
                && long.TryParse(InspectionXml.Attr(e, "cy"u8), NumberStyles.None, CultureInfo.InvariantCulture, out long cy))
            {
                return (cx, cy);
            }
        }

        return (0, 0);
    }

    // ECMA-376 Part 1 §19.2.1.34 sldSz and §20.1.7.4 xfrm: a shape whose box (off, ext) lies wholly outside 0..cx, 0..cy is never seen
    // in a show. Rotated shapes are left alone because their drawn box differs from the stored one.
    private static void OffSlide(InspectionSession s, SanitizeSession? z, IPackagePart main, MarkupDocument pres, List<SlideEntry> slides)
    {
        bool doing = Doing(z, InspectionCategory.OffSlideContent);
        (long w, long h) = SlideSize(pres);
        if (w <= 0 || h <= 0)
        {
            return;
        }

        foreach (SlideEntry sl in slides)
        {
            MarkupDocument? doc = doing ? z!.Edit(sl.Part) : s.Markup(sl.Part);
            if (doc is null)
            {
                continue;
            }

            MarkupNode tree = default;
            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                if (e.LocalName.SequenceEqual("spTree"u8))
                {
                    tree = e;
                    break;
                }
            }

            if (tree.IsNull)
            {
                continue;
            }

            foreach (MarkupNode shape in InspectionXml.Children(tree))
            {
                ReadOnlySpan<byte> n = shape.LocalName;
                if (!(n.SequenceEqual("sp"u8) || n.SequenceEqual("pic"u8) || n.SequenceEqual("grpSp"u8) || n.SequenceEqual("graphicFrame"u8) || n.SequenceEqual("cxnSp"u8)))
                {
                    continue;
                }

                if (!TryBox(shape, out long x, out long y, out long cx, out long cy, out bool rotated) || rotated)
                {
                    continue;
                }

                if (!(x + cx <= 0 || x >= w || y + cy <= 0 || y >= h))
                {
                    continue;
                }

                string id = "?";
                string name = "shape";
                foreach (MarkupNode c in InspectionXml.Descendants(shape))
                {
                    if (c.LocalName.SequenceEqual("cNvPr"u8))
                    {
                        id = InspectionXml.Attr(c, "id"u8) ?? "?";
                        name = InspectionXml.Attr(c, "name"u8) ?? "shape";
                        break;
                    }
                }

                string where = "slide " + sl.Number.ToString(CultureInfo.InvariantCulture) + " (" + sl.Part.Name + ")!" + name;
                s.Add(InspectionCategory.OffSlideContent, where, "shape outside the slide", 1, InspectionXml.Brief(shape.InnerText));
                if (!doing)
                {
                    continue;
                }

                if (HasAnimation(doc, id))
                {
                    z!.NotRemoved(InspectionCategory.OffSlideContent, "sanitize.referenced", where, "An animation refers to the shape (it may fly in from off the slide).");
                    continue;
                }

                z!.RemoveSubtree(sl.Part.Name, shape);
                z.Removed(InspectionCategory.OffSlideContent, where, "shape outside the slide");
            }
        }

        _ = main;
    }

    private static bool HasAnimation(MarkupDocument doc, string id)
    {
        foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
        {
            if (e.LocalName.SequenceEqual("spTgt"u8) && InspectionXml.Attr(e, "spid"u8) == id)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBox(MarkupNode shape, out long x, out long y, out long cx, out long cy, out bool rotated)
    {
        x = y = cx = cy = 0;
        rotated = false;
        foreach (MarkupNode e in InspectionXml.Descendants(shape))
        {
            if (!e.LocalName.SequenceEqual("xfrm"u8))
            {
                continue;
            }

            // Only the shape's own xfrm: its spPr/grpSpPr child, or the graphic frame's xfrm; not those of grouped children.
            MarkupNode parent = e.Parent;
            bool own = parent == shape || parent.Parent == shape;
            if (!own)
            {
                continue;
            }

            rotated = long.TryParse(InspectionXml.Attr(e, "rot"u8), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long rot) && rot != 0;
            bool gotOff = false;
            bool gotExt = false;
            foreach (MarkupNode c in InspectionXml.Children(e))
            {
                if (c.LocalName.SequenceEqual("off"u8))
                {
                    gotOff = long.TryParse(InspectionXml.Attr(c, "x"u8), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out x)
                        & long.TryParse(InspectionXml.Attr(c, "y"u8), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out y);
                }
                else if (c.LocalName.SequenceEqual("ext"u8))
                {
                    gotExt = long.TryParse(InspectionXml.Attr(c, "cx"u8), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out cx)
                        & long.TryParse(InspectionXml.Attr(c, "cy"u8), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out cy);
                }
            }

            return gotOff && gotExt;
        }

        return false;
    }

    private static void HiddenSlides(InspectionSession s, SanitizeSession? z, IPackagePart main, MarkupDocument pres, List<SlideEntry> slides)
    {
        bool doing = Doing(z, InspectionCategory.HiddenSlides);
        var hidden = new List<SlideEntry>();
        foreach (SlideEntry sl in slides)
        {
            MarkupDocument? doc = s.Markup(sl.Part);
            if (doc is not null && InspectionXml.Attr(doc.Root, "show"u8) is "0" or "false")
            {
                hidden.Add(sl);
                s.Add(InspectionCategory.HiddenSlides, "slide " + sl.Number.ToString(CultureInfo.InvariantCulture) + " (" + sl.Part.Name + ")", "hidden slide", 1, InspectionXml.Brief(doc.Root.InnerText, 60));
            }
        }

        if (!doing || hidden.Count == 0)
        {
            return;
        }

        if (hidden.Count == slides.Count)
        {
            foreach (SlideEntry sl in hidden)
            {
                z!.NotRemoved(InspectionCategory.HiddenSlides, "sanitize.unsupported-construct", sl.Part.Name, "Every slide is hidden; removing them all would leave an empty presentation.");
            }

            return;
        }

        z!.Edit(main);
        var removedIds = new HashSet<string>(StringComparer.Ordinal);
        var removedRels = new List<string>();
        foreach (SlideEntry sl in hidden)
        {
            string where = "slide " + sl.Number.ToString(CultureInfo.InvariantCulture) + " (" + sl.Part.Name + ")";
            List<string?> owners = z.OwnersOf(sl.Part.Name);
            string? other = owners.FirstOrDefault(o => o is not null && !string.Equals(o, main.Name, StringComparison.OrdinalIgnoreCase)
                && z.Package.FindPart(o) is { } op && !op.ContentType.EndsWith("notesSlide+xml", StringComparison.OrdinalIgnoreCase));
            if (other is not null)
            {
                z.NotRemoved(InspectionCategory.HiddenSlides, "sanitize.referenced", where, other + " links to the slide.");
                continue;
            }

            if (sl.SlideId is not null)
            {
                removedIds.Add(sl.SlideId);
            }

            removedRels.Add(sl.RelId ?? string.Empty);
            z.Removed(InspectionCategory.HiddenSlides, where, "hidden slide with its notes");
        }

        // The id list entry, section lists and custom shows name the slide by r:id or by slide id; all of them go.
        z.Unlink(main.Name, removedRels);
        foreach (MarkupNode e in InspectionXml.Descendants(pres.DocumentNode))
        {
            if (e.LocalName.SequenceEqual("sldId"u8) && e.Parent.LocalName.SequenceEqual("sldIdLst"u8) && !e.Parent.Parent.LocalName.SequenceEqual("presentation"u8)
                && removedIds.Contains(SlideNumberId(e) ?? string.Empty) && InspectionXml.IsAttached(e))
            {
                e.Remove();
            }
        }

        foreach (IPackagePart app in z.TargetsOf(null, "/extended-properties"))
        {
            if (z.Edit(app) is { } ad)
            {
                foreach (MarkupNode c in InspectionXml.Children(ad.Root))
                {
                    if (c.LocalName.SequenceEqual("TitlesOfParts"u8) || c.LocalName.SequenceEqual("HeadingPairs"u8))
                    {
                        c.Remove();
                    }
                }
            }
        }

    }
}
