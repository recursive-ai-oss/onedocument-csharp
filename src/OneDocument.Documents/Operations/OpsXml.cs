using System.Linq;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>Small helpers over WordprocessingML trees shared by the document operations.</summary>
internal static class OpsXml
{
    /// <summary>Every element at or below <paramref name="root"/>, in document order.</summary>
    public static IEnumerable<MarkupNode> Elements(MarkupNode root)
    {
        if (root.IsNull)
        {
            yield break;
        }

        var stack = new Stack<MarkupNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            MarkupNode n = stack.Pop();
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            yield return n;
            for (MarkupNode c = n.LastChild; !c.IsNull; c = c.PreviousSibling)
            {
                stack.Push(c);
            }
        }
    }

    /// <summary>The local name of a WordprocessingML element, or null for any other node.</summary>
    public static string? W(MarkupNode n) => n.Kind == MarkupNodeKind.Element && HubXml.IsWordNamespace(n.NamespaceUri) ? n.LocalNameString : null;

    /// <summary>The first child element with the given WordprocessingML name.</summary>
    public static MarkupNode Child(MarkupNode parent, string local)
    {
        for (MarkupNode c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (W(c) == local)
            {
                return c;
            }
        }

        return default;
    }

    /// <summary>The related parts of the main part and their roots: headers, footers, notes and comments, with the part names.</summary>
    public static IEnumerable<(string Name, MarkupDocument Document)> StoryParts(Document d)
    {
        foreach (Relationship r in d.Package.GetRelationships(d.MainPartName))
        {
            if (r.TargetMode == RelationshipTargetMode.Internal
                && (RelationshipTypes.Is(r.Type, "header") || RelationshipTypes.Is(r.Type, "footer") || RelationshipTypes.Is(r.Type, "footnotes")
                    || RelationshipTypes.Is(r.Type, "endnotes") || RelationshipTypes.Is(r.Type, "comments"))
                && PartUri.TryResolve(new PartUri(d.MainPartName), r.Target, out PartUri uri)
                && d.Parts.Load(uri.Value, required: false) is { } doc)
            {
                yield return (uri.Value, doc);
            }
        }
    }

    /// <summary>Removes every part nothing reaches from the package root.</summary>
    public static int RemoveUnreachableParts(IOpcPackage package) => OpcGraph.RemoveUnreachableParts(package);
}
