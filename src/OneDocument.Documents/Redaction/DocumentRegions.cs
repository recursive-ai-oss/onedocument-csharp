using System.Linq;
using System.Security;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Redaction;

// Paragraph regions of a word-processing document. A removed paragraph keeps its properties and gets one run holding the mark; what
// hung off it (comments, notes, pictures, embedded objects, hyperlink targets) goes with it.
internal static class DocumentRegions
{
    public static void Apply(OpcPackage package, RedactionPlan plan, RedactionReport report, OfficeLimits limits)
    {
        var ranges = new List<ParagraphRegion>();
        foreach (RedactionRegion region in plan.Regions)
        {
            if (region is ParagraphRegion p)
            {
                ranges.Add(p);
            }
            else
            {
                report.Gap("redaction.region-not-applicable", region.GetType().Name, "Only paragraph regions apply to a word-processing document.");
            }
        }

        if (ranges.Count == 0)
        {
            return;
        }

        string? main = RedactionSupport.MainPart(package);
        MarkupDocument? doc = main is null ? null : RedactionSupport.Load(package, main, limits);
        if (main is null || doc is null)
        {
            report.Gap("redaction.region-failed", "/", "The main document part could not be read.");
            return;
        }

        var ctx = new RedactionContext(plan, report, limits);
        string mark = ctx.RegionMark();
        HashSet<string> before = RedactionSupport.Reachable(package);

        MarkupNode body = RedactionSupport.ChildrenNamed(doc.Root, "body").FirstOrDefault();
        if (body.IsNull)
        {
            return;
        }

        List<MarkupNode> paragraphs = RedactionSupport.Descendants(body).Where(static e => RedactionSupport.Is(e, NsKind.Wml, "p")).ToList();
        var known = new HashSet<string>(package.GetRelationships(main).Select(static r => r.Id), StringComparer.Ordinal);
        var usedRelationships = new HashSet<string>(StringComparer.Ordinal);
        var comments = new HashSet<string>(StringComparer.Ordinal);
        var footnotes = new HashSet<string>(StringComparer.Ordinal);
        var endnotes = new HashSet<string>(StringComparer.Ordinal);
        var done = new List<MarkupNode>();
        int count = 0;

        for (int i = 0; i < paragraphs.Count; i++)
        {
            if (!ranges.Any(r => i >= r.First && i <= r.Last))
            {
                continue;
            }

            MarkupNode p = paragraphs[i];
            if (done.Any(d => IsAncestor(d, p)))
            {
                continue;
            }

            usedRelationships.UnionWith(RedactionSupport.RelationshipIdsUsed(p, known));
            foreach (MarkupNode e in RedactionSupport.Descendants(p))
            {
                string local = e.LocalNameString;
                string? id = RedactionSupport.Attr(e, "id");
                if (id is null)
                {
                    continue;
                }

                if (local is "commentRangeStart" or "commentRangeEnd" or "commentReference")
                {
                    comments.Add(id);
                }
                else if (local == "footnoteReference")
                {
                    footnotes.Add(id);
                }
                else if (local == "endnoteReference")
                {
                    endnotes.Add(id);
                }
            }

            Blank(p, mark);
            done.Add(p);
            count++;
        }

        RedactionSupport.DropUnusedRelationships(package, main, doc, usedRelationships);
        RedactionSupport.Save(package, main, doc);
        report.Add("region", main, count);

        BlankStory(package, main, "/comments", "comment", comments, mark, limits, report);
        BlankStory(package, main, "/footnotes", "footnote", footnotes, mark, limits, report);
        BlankStory(package, main, "/endnotes", "endnote", endnotes, mark, limits, report);
        _ = RedactionSupport.RemoveNewlyUnreachable(package, before, report);
    }

    private static bool IsAncestor(MarkupNode ancestor, MarkupNode node)
    {
        for (MarkupNode n = node.Parent; !n.IsNull; n = n.Parent)
        {
            if (n == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static void Blank(MarkupNode paragraph, string mark)
    {
        var drop = new List<MarkupNode>();
        foreach (MarkupNode c in paragraph.ChildElements)
        {
            if (c.LocalNameString != "pPr")
            {
                drop.Add(c);
            }
        }

        foreach (MarkupNode c in drop)
        {
            c.Remove();
        }

        string p = RedactionSupport.Prefix(paragraph);
        paragraph.AppendXml(Encoding.UTF8.GetBytes($"<{p}r><{p}t xml:space=\"preserve\">{SecurityElement.Escape(mark)}</{p}t></{p}r>"));
    }

    private static void BlankStory(OpcPackage package, string main, string typeSuffix, string element, HashSet<string> ids, string mark, OfficeLimits limits, RedactionReport report)
    {
        if (ids.Count == 0)
        {
            return;
        }

        foreach (string part in RedactionSupport.RelatedParts(package, main, typeSuffix))
        {
            MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            int n = 0;
            foreach (MarkupNode e in RedactionSupport.ChildrenNamed(doc.Root, element).ToList())
            {
                if (RedactionSupport.Attr(e, "id") is { } id && ids.Contains(id))
                {
                    foreach (MarkupNode c in e.ChildElements.ToList())
                    {
                        c.Remove();
                    }

                    string p = RedactionSupport.Prefix(e);
                    e.AppendXml(Encoding.UTF8.GetBytes($"<{p}p><{p}r><{p}t xml:space=\"preserve\">{SecurityElement.Escape(mark)}</{p}t></{p}r></{p}p>"));
                    n++;
                }
            }

            RedactionSupport.Save(package, part, doc);
            report.Add("region-" + element, part, n);
        }
    }
}
