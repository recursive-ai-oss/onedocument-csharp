using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Redaction;

// Package and markup helpers the per-kind region code shares.
internal static class RedactionSupport
{
    public const string OfficeDocumentRelationship = "officeDocument";

    public static string? MainPart(OpcPackage package)
    {
        foreach (Relationship r in package.GetRelationships())
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && r.Type.EndsWith("/officeDocument", StringComparison.Ordinal)
                && OpcPackage.TryResolveTarget(null, r, out PartUri target) && package.FindPart(target.Value) is { } part)
            {
                return part.Name;
            }
        }

        return null;
    }

    public static string? Resolve(OpcPackage package, string? owner, Relationship rel) =>
        rel.TargetMode == RelationshipTargetMode.Internal && OpcPackage.TryResolveTarget(owner, rel, out PartUri t) && package.FindPart(t.Value) is { } p ? p.Name : null;

    public static string? RelatedPart(OpcPackage package, string owner, string relationshipId)
    {
        foreach (Relationship r in package.GetRelationships(owner))
        {
            if (r.Id == relationshipId)
            {
                return Resolve(package, owner, r);
            }
        }

        return null;
    }

    public static IEnumerable<string> RelatedParts(OpcPackage package, string? owner, string typeSuffix)
    {
        foreach (Relationship r in package.GetRelationships(owner))
        {
            if (r.Type.EndsWith(typeSuffix, StringComparison.Ordinal) && Resolve(package, owner, r) is { } name)
            {
                yield return name;
            }
        }
    }

    public static MarkupDocument? Load(OpcPackage package, string partName, OfficeLimits limits)
    {
        IPackagePart? part = package.FindPart(partName);
        return part is not null && MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? doc, new OpenContext(limits), partName) ? doc : null;
    }

    public static void Save(OpcPackage package, string partName, MarkupDocument doc)
    {
        if (doc.IsModified && package.FindPart(partName) is { } part)
        {
            part.SetBytes(doc.ToArray());
        }
    }

    public static IEnumerable<MarkupNode> Descendants(MarkupNode parent)
    {
        for (MarkupNode c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            yield return c;
            foreach (MarkupNode d in Descendants(c))
            {
                yield return d;
            }
        }
    }

    public static bool Is(MarkupNode e, NsKind ns, string local) => Ns.Classify(e.NamespaceUriBytes) == ns && e.LocalNameString == local;

    // The value of an attribute by local name, whatever its prefix (MarkupNode.GetAttribute(name) sees only unprefixed ones).
    public static string? Attr(MarkupNode e, string local)
    {
        byte[] name = Encoding.UTF8.GetBytes(local);
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual(name))
            {
                return a.Value;
            }
        }

        return null;
    }

    public static bool IsNamed(MarkupNode e, string local) => e.LocalNameString == local;

    public static IEnumerable<MarkupNode> ChildrenNamed(MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                yield return c;
            }
        }
    }

    // Attribute values anywhere in the subtree that equal one of the known relationship ids of the owning part.
    public static HashSet<string> RelationshipIdsUsed(MarkupNode subtree, ISet<string> known)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        void Visit(MarkupNode e)
        {
            foreach (MarkupAttributeRef a in e.Attributes)
            {
                if (!a.IsNamespaceDeclaration)
                {
                    string v = a.Value;
                    if (known.Contains(v))
                    {
                        used.Add(v);
                    }
                }
            }
        }

        Visit(subtree);
        foreach (MarkupNode d in Descendants(subtree))
        {
            Visit(d);
        }

        return used;
    }

    // Drops the relationships from <paramref name="owner"/> whose ids were used by removed markup and are no longer used by what remains.
    public static void DropUnusedRelationships(OpcPackage package, string owner, MarkupDocument ownerDoc, IEnumerable<string> candidates)
    {
        IReadOnlyList<Relationship> rels = package.GetRelationships(owner);
        var known = new HashSet<string>(rels.Select(static r => r.Id), StringComparer.Ordinal);
        HashSet<string> stillUsed = ownerDoc.Root.IsNull ? [] : RelationshipIdsUsed(ownerDoc.Root, known);
        var drop = new HashSet<string>(candidates.Where(c => !stillUsed.Contains(c)), StringComparer.Ordinal);
        if (drop.Count == 0)
        {
            return;
        }

        package.SetRelationships(owner, [.. rels.Where(r => !drop.Contains(r.Id))]);
    }

    // Part names reachable from the package relationships through internal relationships.
    public static HashSet<string> Reachable(OpcPackage package)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (Relationship r in package.GetRelationships())
        {
            if (Resolve(package, null, r) is { } name && seen.Add(name))
            {
                queue.Enqueue(name);
            }
        }

        while (queue.Count > 0)
        {
            string owner = queue.Dequeue();
            foreach (Relationship r in package.GetRelationships(owner))
            {
                if (Resolve(package, owner, r) is { } name && seen.Add(name))
                {
                    queue.Enqueue(name);
                }
            }
        }

        return seen;
    }

    // Removes the parts that were reachable before an edit and are not now (pictures, charts, embedded objects and notes only the removed
    // content used). Parts that were already unreachable are left alone.
    public static int RemoveNewlyUnreachable(OpcPackage package, HashSet<string> before, RedactionReport report)
    {
        HashSet<string> now = Reachable(package);
        int removed = 0;
        foreach (string name in before)
        {
            if (!now.Contains(name) && package.RemovePart(name))
            {
                removed++;
                report.Add("region-part", name, 1);
            }
        }

        return removed;
    }

    public static string Prefix(MarkupNode e) => e.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(e.Prefix) + ":";
}
