using System.Buffers;
using System.Text;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.Markup;

/// <summary>
/// Markup compatibility (ECMA-376 Part 3): helpers for reading <c>mc:Ignorable</c>, <c>mc:ProcessContent</c>,
/// <c>mc:MustUnderstand</c> and <c>mc:AlternateContent</c> from a <see cref="MarkupDocument"/>. They only read: the tree keeps every branch and
/// every ignorable construct, and a typed view uses these to decide which branch or element to interpret.
/// </summary>
/// <remarks>
/// The functions take the set of namespaces the consumer understands as a predicate. A prefix list that names a prefix with no
/// binding in scope is non-conformant (Part 3 §7.2) and that prefix is skipped.
/// </remarks>
public static class MarkupCompatibility
{
    /// <summary>The Markup Compatibility namespace.</summary>
    public const string Namespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static ReadOnlySpan<byte> NamespaceUtf8 => "http://schemas.openxmlformats.org/markup-compatibility/2006"u8;

    /// <summary>Tells whether the node is an <c>mc:AlternateContent</c> element.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True for <c>mc:AlternateContent</c>.</returns>
    public static bool IsAlternateContent(MarkupNode node) => node.IsElement(NamespaceUtf8, "AlternateContent"u8);

    /// <summary>Tells whether the node is an <c>mc:Choice</c> element.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True for <c>mc:Choice</c>.</returns>
    public static bool IsChoice(MarkupNode node) => node.IsElement(NamespaceUtf8, "Choice"u8);

    /// <summary>Tells whether the node is an <c>mc:Fallback</c> element.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True for <c>mc:Fallback</c>.</returns>
    public static bool IsFallback(MarkupNode node) => node.IsElement(NamespaceUtf8, "Fallback"u8);

    /// <summary>
    /// Lists the namespaces declared ignorable at an element or any of its ancestors (Part 3 §7.2), outermost first, without duplicates.
    /// </summary>
    /// <param name="context">The element (or any node below it) whose scope is asked about.</param>
    /// <returns>The ignorable namespace URIs.</returns>
    public static IReadOnlyList<string> GetIgnorableNamespaces(MarkupNode context)
    {
        return CollectOutwardIn(context, "Ignorable");
    }

    /// <summary>Tells whether a namespace is declared ignorable in the scope of a node (Part 3 §7.2).</summary>
    /// <param name="context">The node whose scope is asked about.</param>
    /// <param name="namespaceUri">The namespace URI.</param>
    /// <returns>True when an <c>mc:Ignorable</c> at the node or an ancestor names it.</returns>
    public static bool IsNamespaceIgnorable(MarkupNode context, string namespaceUri)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        var list = new List<string>();
        for (MarkupNode e = ElementOf(context); !e.IsNull; e = e.Parent)
        {
            if (e.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            list.Clear();
            Collect(e, "Ignorable", list);
            if (list.Contains(namespaceUri))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tells whether a consumer that understands only some namespaces should skip an element (Part 3 §9.2 step 1): it is in a
    /// namespace that is declared ignorable and is not understood.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="isUnderstood">Tells whether the consumer understands a namespace.</param>
    /// <returns>True when the element is ignorable and not understood. See <see cref="ProcessesContent"/> for whether its children are still read.</returns>
    public static bool IsIgnored(MarkupNode element, Func<string, bool> isUnderstood)
    {
        ArgumentNullException.ThrowIfNull(isUnderstood);
        if (element.Kind != MarkupNodeKind.Element)
        {
            return false;
        }

        string ns = element.NamespaceUri;
        return ns.Length > 0 && ns != Namespace && !isUnderstood(ns) && IsNamespaceIgnorable(element, ns);
    }

    /// <summary>
    /// Tells whether an ignored element's content is still to be read because an <c>mc:ProcessContent</c> at the element or an
    /// ancestor lists its name (Part 3 §7.3), as <c>prefix:local</c> or <c>prefix:*</c>.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>True when its children replace it.</returns>
    public static bool ProcessesContent(MarkupNode element)
    {
        if (element.Kind != MarkupNodeKind.Element)
        {
            return false;
        }

        string ns = element.NamespaceUri;
        string local = element.LocalNameString;
        var tokens = new List<(string Namespace, string Local)>();
        for (MarkupNode e = element; !e.IsNull; e = e.Parent)
        {
            if (e.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            string? value = e.GetAttribute(Namespace, "ProcessContent");
            if (value is null)
            {
                continue;
            }

            tokens.Clear();
            foreach (string token in value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = token.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0 || !TryResolve(e, token[..colon], out string uri))
                {
                    continue;
                }

                string name = token[(colon + 1)..];
                if (uri == ns && (name == "*" || name == local))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Lists the namespaces an <c>mc:MustUnderstand</c> at the node or an ancestor requires the consumer to understand (Part 3 §7.4).</summary>
    /// <param name="context">The node.</param>
    /// <returns>The namespace URIs, outermost first, without duplicates.</returns>
    public static IReadOnlyList<string> GetMustUnderstandNamespaces(MarkupNode context)
    {
        return CollectOutwardIn(context, "MustUnderstand");
    }

    /// <summary>Lists the namespaces an <c>mc:Choice</c> requires (its unqualified <c>Requires</c> attribute, Part 3 §7.6), resolved in the scope of the Choice.</summary>
    /// <param name="choice">The <c>mc:Choice</c> element.</param>
    /// <returns>The namespace URIs in the order written.</returns>
    public static IReadOnlyList<string> GetRequiredNamespaces(MarkupNode choice)
    {
        var result = new List<string>();
        string? requires = choice.Kind == MarkupNodeKind.Element ? choice.GetAttribute("Requires") : null;
        if (requires is not null)
        {
            AddPrefixes(choice, requires, result);
        }

        return result;
    }

    /// <summary>
    /// Picks the branch of an <c>mc:AlternateContent</c> that a consumer should read (Part 3 §9.3 step 2): the first
    /// <c>mc:Choice</c> whose required namespaces are all understood, otherwise the <c>mc:Fallback</c>.
    /// </summary>
    /// <param name="alternateContent">The <c>mc:AlternateContent</c> element.</param>
    /// <param name="isUnderstood">Tells whether the consumer understands a namespace.</param>
    /// <returns>The selected <c>mc:Choice</c> or <c>mc:Fallback</c>, or the default node when there is neither. Nothing is removed from the tree.</returns>
    public static MarkupNode SelectAlternateContent(MarkupNode alternateContent, Func<string, bool> isUnderstood)
    {
        ArgumentNullException.ThrowIfNull(isUnderstood);
        MarkupNode fallback = default;
        foreach (MarkupNode child in alternateContent.ChildElements)
        {
            if (IsChoice(child))
            {
                bool all = true;
                foreach (string ns in GetRequiredNamespaces(child))
                {
                    if (!isUnderstood(ns))
                    {
                        all = false;
                        break;
                    }
                }

                if (all)
                {
                    return child;
                }
            }
            else if (IsFallback(child) && fallback.IsNull)
            {
                fallback = child;
            }
        }

        return fallback;
    }

    private static List<string> CollectOutwardIn(MarkupNode context, string attribute)
    {
        var chain = new List<MarkupNode>();
        for (MarkupNode e = ElementOf(context); !e.IsNull; e = e.Parent)
        {
            if (e.Kind == MarkupNodeKind.Element)
            {
                chain.Add(e);
            }
        }

        var result = new List<string>();
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            Collect(chain[i], attribute, result);
        }

        return result;
    }

    private static MarkupNode ElementOf(MarkupNode n)
    {
        while (!n.IsNull && n.Kind != MarkupNodeKind.Element)
        {
            n = n.Parent;
        }

        return n;
    }

    private static void Collect(MarkupNode element, string attribute, List<string> into)
    {
        string? value = element.GetAttribute(Namespace, attribute);
        if (value is not null)
        {
            AddPrefixes(element, value, into);
        }
    }

    private static void AddPrefixes(MarkupNode element, string list, List<string> into)
    {
        foreach (string prefix in list.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryResolve(element, prefix, out string uri) && !into.Contains(uri))
            {
                into.Add(uri);
            }
        }
    }

    private static bool TryResolve(MarkupNode element, string prefix, out string uri)
    {
        if (element.Doc!.TryResolvePrefix(element.Index, Encoding.UTF8.GetBytes(prefix), out ReadOnlySpan<byte> raw))
        {
            uri = XmlText.GetString(raw, true);
            return true;
        }

        uri = string.Empty;
        return false;
    }
}
