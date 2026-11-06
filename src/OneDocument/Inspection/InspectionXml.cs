using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Inspection;

/// <summary>
/// Small helpers over the markup tree for providers: element tests by namespace fragment and local name (so Transitional and Strict
/// spellings are read alike), attribute lookup ignoring prefixes, and a snapshot of descendants that is safe to remove while walking.
/// </summary>
public static class InspectionXml
{
    /// <summary>Tells whether a node is an element with a local name whose namespace URI contains a fragment.</summary>
    /// <param name="node">The node.</param>
    /// <param name="namespaceFragment">A fragment of the namespace URI, such as <c>wordprocessingml</c>; empty to accept any namespace.</param>
    /// <param name="local">The local name.</param>
    /// <returns>True when it is that element.</returns>
    public static bool Is(MarkupNode node, ReadOnlySpan<byte> namespaceFragment, ReadOnlySpan<byte> local) =>
        node.Kind == MarkupNodeKind.Element
        && node.LocalName.SequenceEqual(local)
        && (namespaceFragment.IsEmpty || node.NamespaceUriBytes.IndexOf(namespaceFragment) >= 0);

    /// <summary>Gets the first attribute with a local name, whatever its prefix.</summary>
    /// <param name="node">The element.</param>
    /// <param name="local">The attribute's local name.</param>
    /// <returns>The decoded value, or null.</returns>
    public static string? Attr(MarkupNode node, ReadOnlySpan<byte> local)
    {
        foreach (MarkupAttributeRef a in node.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual(local))
            {
                return a.Value;
            }
        }

        return null;
    }

    /// <summary>Tells whether an element has a boolean attribute that is on: absent value counts as on for the element itself, so this tests the attribute's value (<c>1</c>, <c>true</c>, <c>on</c>).</summary>
    /// <param name="node">The element.</param>
    /// <param name="local">The attribute's local name.</param>
    /// <returns>True when the attribute is present and true.</returns>
    public static bool AttrTrue(MarkupNode node, ReadOnlySpan<byte> local) => Attr(node, local) is "1" or "true" or "on";

    /// <summary>Gets the elements below a node in document order, as a list that stays valid while nodes are removed.</summary>
    /// <param name="root">The node whose descendants are listed (not itself).</param>
    /// <returns>The descendant elements.</returns>
    public static List<MarkupNode> Descendants(MarkupNode root)
    {
        var list = new List<MarkupNode>();
        MarkupNode n = root.FirstChild;
        var stack = new Stack<MarkupNode>();
        while (!n.IsNull)
        {
            if (n.Kind == MarkupNodeKind.Element)
            {
                list.Add(n);
                MarkupNode child = n.FirstChild;
                if (!child.IsNull)
                {
                    MarkupNode next = n.NextSibling;
                    if (!next.IsNull)
                    {
                        stack.Push(next);
                    }

                    n = child;
                    continue;
                }
            }

            n = n.NextSibling;
            if (n.IsNull && stack.Count > 0)
            {
                n = stack.Pop();
            }
        }

        return list;
    }

    /// <summary>Tells whether a node is still part of its document (it was not removed, and nothing above it was).</summary>
    /// <param name="node">The node.</param>
    /// <returns>True when attached.</returns>
    public static bool IsAttached(MarkupNode node)
    {
        for (MarkupNode n = node; !n.IsNull; n = n.Parent)
        {
            if (n.Kind == MarkupNodeKind.Document)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the child elements of a node as a list.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The children.</returns>
    public static List<MarkupNode> Children(MarkupNode node)
    {
        var list = new List<MarkupNode>();
        foreach (MarkupNode c in node.ChildElements)
        {
            list.Add(c);
        }

        return list;
    }

    /// <summary>Tells whether a node has any child element.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True when it has one.</returns>
    public static bool HasChildElements(MarkupNode node)
    {
        foreach (MarkupNode _ in node.ChildElements)
        {
            return true;
        }

        return false;
    }

    /// <summary>Gets the nearest ancestor element with a local name.</summary>
    /// <param name="node">The node.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The ancestor, or a null node.</returns>
    public static MarkupNode Ancestor(MarkupNode node, ReadOnlySpan<byte> local)
    {
        for (MarkupNode p = node.Parent; !p.IsNull; p = p.Parent)
        {
            if (p.Kind == MarkupNodeKind.Element && p.LocalName.SequenceEqual(local))
            {
                return p;
            }
        }

        return default;
    }

    /// <summary>
    /// Removes a node, and then the wrappers that the removal leaves empty: an <c>ext</c> without content, an <c>extLst</c> without
    /// extensions, and a Markup Compatibility <c>Choice</c> takes its whole <c>AlternateContent</c> with it (the fallback shows the same thing).
    /// </summary>
    /// <param name="node">The node to remove.</param>
    public static void RemoveWithWrappers(MarkupNode node)
    {
        while (!node.IsNull && node.Kind == MarkupNodeKind.Element)
        {
            MarkupNode parent = node.Parent;
            if (!parent.IsNull && parent.Kind == MarkupNodeKind.Element && parent.LocalName.SequenceEqual("Choice"u8))
            {
                MarkupNode alt = parent.Parent;
                if (!alt.IsNull && alt.LocalName.SequenceEqual("AlternateContent"u8) && !alt.Parent.IsNull && alt.Parent.Kind == MarkupNodeKind.Element)
                {
                    node = alt;
                    continue;
                }
            }

            if (parent.IsNull || parent.Kind != MarkupNodeKind.Element)
            {
                return;
            }

            node.Remove();
            if (!HasChildElements(parent) && (parent.LocalName.SequenceEqual("ext"u8) || parent.LocalName.SequenceEqual("extLst"u8)))
            {
                node = parent;
                continue;
            }

            return;
        }
    }

    /// <summary>Counts the element nodes at or below a node's children whose local names match (a cheap census).</summary>
    /// <param name="root">The root.</param>
    /// <param name="namespaceFragment">A fragment of the namespace URI; empty for any.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The elements found.</returns>
    public static List<MarkupNode> Find(MarkupNode root, ReadOnlySpan<byte> namespaceFragment, ReadOnlySpan<byte> local)
    {
        var list = new List<MarkupNode>();
        foreach (MarkupNode n in Descendants(root))
        {
            if (Is(n, namespaceFragment, local))
            {
                list.Add(n);
            }
        }

        return list;
    }

    /// <summary>Shortens a value for use in a finding's detail.</summary>
    /// <param name="value">The text.</param>
    /// <param name="max">The longest result, in UTF-16 units.</param>
    /// <returns>The text trimmed, with an ellipsis when cut.</returns>
    public static string Brief(string value, int max = 80)
    {
        string t = value.Trim();
        return t.Length <= max ? t : string.Concat(t.AsSpan(0, max), "...");
    }

    /// <summary>Gets the UTF-8 bytes of a string (a convenience for building replacement XML).</summary>
    /// <param name="xml">The text.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Utf8(string xml) => Encoding.UTF8.GetBytes(xml);
}
