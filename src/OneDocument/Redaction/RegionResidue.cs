using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Redaction;

// A region removes content by position, so copies of its text elsewhere (a table of contents entry, a cross-reference result, a cached value)
// are not found by the region itself. This lists the text a region took out and tells where the same text is still written, without removing it:
// a region says where, not what, and the same words elsewhere may be innocent.
internal static class RegionResidue
{
    private const int MinimumLength = 6;
    private const int MaximumRemoved = 5000;

    // Every piece of text in the package's XML parts, as a reader sees it (a paragraph across runs, other text nodes by themselves), with the parts it is in.
    public static Dictionary<string, List<string>> Units(OpcPackage package, OfficeLimits limits)
    {
        var units = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var segments = new List<TextSegment>();
        foreach (IPackagePart part in package.Parts)
        {
            if (!(PackageRedactor.IsXmlType(part.ContentType) || part.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!MarkupDocument.TryParse(part.GetBytes().ToArray(), out MarkupDocument? doc, new OpenContext(limits), part.Name) || doc is null)
            {
                continue;
            }

            Walk(doc.DocumentNode, part.Name, units, segments);
        }

        return units;
    }

    private static void Walk(MarkupNode parent, string part, Dictionary<string, List<string>> units, List<TextSegment> segments)
    {
        for (MarkupNode c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element)
            {
                if (OoxmlText.Classify(c, out _) == TextRole.Container)
                {
                    segments.Clear();
                    OoxmlText.Collect(c, segments);
                    var sb = new StringBuilder();
                    foreach (TextSegment s in segments.Where(static s => !s.Code))
                    {
                        sb.Append(s.Text);
                    }

                    Add(units, sb.ToString(), part);
                }

                Walk(c, part, units, segments);
            }
            else if (c.Kind == MarkupNodeKind.Text && !IsRunText(parent))
            {
                Add(units, c.InnerText, part);
            }
        }
    }

    private static bool IsRunText(MarkupNode leaf)
    {
        if (OoxmlText.Classify(leaf, out _) is not (TextRole.Leaf or TextRole.CodeLeaf))
        {
            return false;
        }

        for (MarkupNode n = leaf.Parent; !n.IsNull; n = n.Parent)
        {
            if (n.Kind == MarkupNodeKind.Element && OoxmlText.Classify(n, out _) == TextRole.Container)
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(Dictionary<string, List<string>> units, string text, string part)
    {
        string t = text.Trim();
        if (t.Length < MinimumLength)
        {
            return;
        }

        if (!units.TryGetValue(t, out List<string>? list))
        {
            units[t] = list = [];
        }

        list.Add(part);
    }

    // Text units that were in the package before the regions ran and are fewer after: what the regions took out.
    public static List<string> Removed(Dictionary<string, List<string>> before, Dictionary<string, List<string>> after)
    {
        var removed = new List<string>();
        foreach ((string text, List<string> where) in before)
        {
            if (removed.Count < MaximumRemoved && (!after.TryGetValue(text, out List<string>? left) || left.Count < where.Count) && text.Any(char.IsLetterOrDigit))
            {
                removed.Add(text);
            }
        }

        return removed;
    }

    // Reports the parts that still contain any of the removed text.
    public static void Report(List<string> removed, Dictionary<string, List<string>> now, RedactionReport report)
    {
        if (removed.Count == 0)
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string text, List<string> where) in now)
        {
            foreach (string gone in removed)
            {
                if (text.Contains(gone, StringComparison.Ordinal))
                {
                    foreach (string part in where.Distinct())
                    {
                        if (reported.Add(part))
                        {
                            report.Gap("redaction.region-copy", part, "Text that a region removed is written here as well (a copy such as a table of contents entry, a cross-reference result or a cached value). It was not removed; add the text as a match to remove it.");
                        }
                    }

                    break;
                }
            }
        }
    }
}
