using System.Linq;

namespace RecursiveAI.OneDocument.Packaging;

/// <summary>Walks of the relationship graph of an OPC package.</summary>
public static class OpcGraph
{
    /// <summary>
    /// Removes every part that no chain of relationships from the package root reaches (ECMA-376 Part 2 §9.3: a part nothing relates to
    /// is not part of the document). Used after an operation removed the last relationship to a part.
    /// </summary>
    /// <param name="package">The package, edited in place.</param>
    /// <returns>How many parts were removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="package"/> is null.</exception>
    public static int RemoveUnreachableParts(IOpcPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string? Source, Relationship Rel)>();
        foreach (Relationship r in package.GetRelationships(null))
        {
            queue.Enqueue((null, r));
        }

        while (queue.Count > 0)
        {
            (string? source, Relationship rel) = queue.Dequeue();
            if (rel.TargetMode != RelationshipTargetMode.Internal
                || !PartUri.TryResolve(source is null ? null : new PartUri(source), rel.Target, out PartUri uri)
                || package.FindPart(uri.Value) is not { } part
                || !reached.Add(part.Name))
            {
                continue;
            }

            foreach (Relationship c in package.GetRelationships(part.Name))
            {
                queue.Enqueue((part.Name, c));
            }
        }

        int removed = 0;
        foreach (string name in package.Parts.Select(p => p.Name).Where(n => !reached.Contains(n)).ToList())
        {
            if (package.RemovePart(name))
            {
                removed++;
            }
        }

        return removed;
    }
}
