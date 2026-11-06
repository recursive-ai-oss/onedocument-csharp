using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Redaction;

// Slide and shape regions of a presentation. A removed slide takes its notes and everything only it used; a removed shape takes its
// pictures, charts and embedded objects, and the slide's animations when they named it.
internal static class PresentationRegions
{
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelNsStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    public static void Apply(OpcPackage package, RedactionPlan plan, RedactionReport report, OfficeLimits limits)
    {
        foreach (RedactionRegion region in plan.Regions)
        {
            if (region is not (ShapeRegion or SlideRegion))
            {
                report.Gap("redaction.region-not-applicable", region.GetType().Name, "Only slide and shape regions apply to a presentation.");
            }
        }

        string? main = RedactionSupport.MainPart(package);
        MarkupDocument? presentation = main is null ? null : RedactionSupport.Load(package, main, limits);
        if (main is null || presentation is null || presentation.Root.IsNull)
        {
            report.Gap("redaction.region-failed", "/", "The presentation part could not be read.");
            return;
        }

        MarkupNode list = RedactionSupport.ChildrenNamed(presentation.Root, "sldIdLst").FirstOrDefault();
        List<MarkupNode> ids = list.IsNull ? [] : RedactionSupport.ChildrenNamed(list, "sldId").ToList();
        string?[] slideParts = ids.Select(i => RelId(i) is { } rid ? RedactionSupport.RelatedPart(package, main, rid) : null).ToArray();
        HashSet<string> before = RedactionSupport.Reachable(package);
        var ctx = new RedactionContext(plan, report, limits);

        // Shapes first (slide numbers refer to the slides as they were), then whole slides.
        foreach (ShapeRegion shape in plan.Regions.OfType<ShapeRegion>())
        {
            if (shape.SlideNumber > slideParts.Length || slideParts[shape.SlideNumber - 1] is not { } part)
            {
                report.Gap("redaction.region-failed", "slide " + shape.SlideNumber, "There is no such slide.");
                continue;
            }

            Shape(package, part, shape, report, limits);
        }

        var drop = plan.Regions.OfType<SlideRegion>().Select(static s => s.SlideNumber).Distinct().OrderByDescending(static n => n).ToList();
        var dropped = new List<(string? Id, string? RelId)>();
        foreach (int number in drop)
        {
            if (number > ids.Count)
            {
                report.Gap("redaction.region-failed", "slide " + number, "There is no such slide.");
                continue;
            }

            MarkupNode id = ids[number - 1];
            dropped.Add((id.GetAttribute("id"), RelId(id)));
            id.Remove();
            report.Add("region-slide", slideParts[number - 1] ?? "slide " + number, 1);
        }

        if (dropped.Count > 0)
        {
            RemoveSlideReferences(package, main, presentation, dropped, limits);
            DropSlideTitles(package, limits);
        }

        RedactionSupport.Save(package, main, presentation);
        _ = ctx;
        _ = RedactionSupport.RemoveNewlyUnreachable(package, before, report);
    }

    private static string? RelId(MarkupNode e) => e.GetAttribute(RelNs, "id") ?? e.GetAttribute(RelNsStrict, "id");

    private static void RemoveSlideReferences(OpcPackage package, string main, MarkupDocument presentation, List<(string? Id, string? RelId)> dropped, OfficeLimits limits)
    {
        var relIds = new HashSet<string>(dropped.Where(static d => d.RelId is not null).Select(static d => d.RelId!), StringComparer.Ordinal);
        var slideIds = new HashSet<string>(dropped.Where(static d => d.Id is not null).Select(static d => d.Id!), StringComparer.Ordinal);

        // Sections and custom shows name slides too.
        foreach (MarkupNode e in RedactionSupport.Descendants(presentation.Root).ToList())
        {
            string local = e.LocalNameString;
            if (local == "sldId" && e.Parent.LocalNameString == "sldIdLst" && e.Parent.Parent.LocalNameString == "section" && e.GetAttribute("id") is { } sid && slideIds.Contains(sid))
            {
                e.Remove();
            }
            else if (local == "sld" && e.Parent.LocalNameString == "sldLst" && RelId(e) is { } rid && relIds.Contains(rid))
            {
                e.Remove();
            }
        }

        IReadOnlyList<Relationship> rels = package.GetRelationships(main);
        package.SetRelationships(main, [.. rels.Where(r => !relIds.Contains(r.Id))]);
    }

    // The extended properties list every slide title; they would outlive a removed slide.
    private static void DropSlideTitles(OpcPackage package, OfficeLimits limits)
    {
        foreach (string app in package.Parts.Where(static p => p.Name.EndsWith("/docProps/app.xml", StringComparison.OrdinalIgnoreCase)).Select(static p => p.Name).ToList())
        {
            MarkupDocument? doc = RedactionSupport.Load(package, app, limits);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            foreach (MarkupNode e in doc.Root.ChildElements.Where(static e => e.LocalNameString is "TitlesOfParts" or "HeadingPairs").ToList())
            {
                e.Remove();
            }

            RedactionSupport.Save(package, app, doc);
        }
    }

    private static void Shape(OpcPackage package, string part, ShapeRegion region, RedactionReport report, OfficeLimits limits)
    {
        MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
        if (doc is null || doc.Root.IsNull)
        {
            report.Gap("redaction.region-failed", part, "The slide part could not be read.");
            return;
        }

        var known = new HashSet<string>(package.GetRelationships(part).Select(static r => r.Id), StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var removedIds = new HashSet<string>(StringComparer.Ordinal);
        var removedShapes = new List<MarkupNode>();
        int n = 0;
        foreach (MarkupNode shape in RedactionSupport.Descendants(doc.Root).Where(static e => e.LocalNameString is "sp" or "pic" or "graphicFrame" or "grpSp" or "cxnSp").ToList())
        {
            MarkupNode nv = shape.ChildElements.FirstOrDefault(static c => c.LocalNameString.StartsWith("nv", StringComparison.Ordinal));
            MarkupNode props = nv.IsNull ? default : RedactionSupport.ChildrenNamed(nv, "cNvPr").FirstOrDefault();
            if (props.IsNull)
            {
                continue;
            }

            bool hit = region.Name is not null
                ? string.Equals(props.GetAttribute("name"), region.Name, StringComparison.Ordinal)
                : props.GetAttribute("id") == region.ShapeId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!hit || IsInside(shape, removedShapes))
            {
                continue;
            }

            used.UnionWith(RedactionSupport.RelationshipIdsUsed(shape, known));
            foreach (MarkupNode d in RedactionSupport.Descendants(shape).Prepend(props).Where(static e => e.LocalNameString == "cNvPr"))
            {
                if (d.GetAttribute("id") is { } id)
                {
                    removedIds.Add(id);
                }
            }

            shape.Remove();
            removedShapes.Add(shape);
            n++;
        }

        // Animations that name a removed shape would dangle; the slide's timing is dropped whole.
        foreach (MarkupNode timing in doc.Root.ChildElements.Where(static e => e.LocalNameString == "timing").ToList())
        {
            if (RedactionSupport.Descendants(timing).Any(e => e.GetAttribute("spid") is { } spid && removedIds.Contains(spid)))
            {
                timing.Remove();
                report.Gap("redaction.animations-removed", part, "The slide's animations named a removed shape and were removed.");
            }
        }

        RedactionSupport.DropUnusedRelationships(package, part, doc, used);
        RedactionSupport.Save(package, part, doc);
        report.Add("region-shape", part, n);
        if (n == 0)
        {
            report.Gap("redaction.region-failed", part, "No shape on the slide has this name or id.");
        }
    }

    private static bool IsInside(MarkupNode node, List<MarkupNode> removed)
    {
        for (MarkupNode n = node.Parent; !n.IsNull; n = n.Parent)
        {
            if (removed.Contains(n))
            {
                return true;
            }
        }

        return false;
    }
}
