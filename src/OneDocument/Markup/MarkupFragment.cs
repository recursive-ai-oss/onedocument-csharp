using System.Buffers;
using System.Text;

namespace RecursiveAI.OneDocument.Markup;

/// <summary>What a <see cref="MarkupFragmentOptions.RewriteAttribute"/> callback decides about one attribute.</summary>
/// <param name="Drop">True to leave the attribute out.</param>
/// <param name="Value">A new (unescaped) value, or null to keep the attribute as written.</param>
public readonly record struct AttributeEdit(bool Drop, string? Value)
{
    /// <summary>Keeps the attribute exactly as written.</summary>
    public static AttributeEdit Keep => default;

    /// <summary>Leaves the attribute out.</summary>
    public static AttributeEdit Remove => new(true, null);

    /// <summary>Gives the attribute a new value.</summary>
    /// <param name="value">The unescaped value.</param>
    /// <returns>The edit.</returns>
    public static AttributeEdit Set(string value) => new(false, value);
}

/// <summary>Decides, for one attribute of one element, whether it stays, goes or changes its value.</summary>
/// <param name="element">The element the attribute belongs to (in the source tree).</param>
/// <param name="attribute">The attribute. Namespace declarations are never passed.</param>
/// <returns>The decision.</returns>
public delegate AttributeEdit AttributeRewriter(MarkupNode element, in MarkupAttributeRef attribute);

/// <summary>Callbacks for <see cref="MarkupFragment"/>. All are optional; nodes passed to them belong to the source tree.</summary>
public sealed class MarkupFragmentOptions
{
    /// <summary>Gets or sets a filter over elements: return false to leave an element out together with everything below it. Null keeps all.</summary>
    public Func<MarkupNode, bool>? KeepElement { get; set; }

    /// <summary>Gets or sets the attribute callback. Null keeps all attributes.</summary>
    public AttributeRewriter? RewriteAttribute { get; set; }

    /// <summary>Gets or sets a callback for text nodes: return the replacement (unescaped) text, or null to keep the node as written.</summary>
    public Func<MarkupNode, string?>? RewriteText { get; set; }
}

/// <summary>
/// Serialises nodes of one markup tree so that they can be inserted into another: every namespace declaration the nodes depend on
/// travels with them (unless the destination already binds the prefix to the same namespace), and attributes and text can be
/// rewritten on the way. Used by the operations that move content between documents (merge, append, split, copy).
/// </summary>
/// <remarks>The output is the bytes of the source nodes with the requested changes; whitespace, quote style, attribute order and prefixes are kept.</remarks>
public static class MarkupFragment
{
    /// <summary>Serialises <paramref name="nodes"/> one after the other for insertion where <paramref name="destinationScope"/> is in scope.</summary>
    /// <param name="nodes">The nodes (any kind; elements get the declarations they need).</param>
    /// <param name="destinationScope">The element the XML will be inserted into, or a node whose scope applies. Declarations already in force there are not repeated. May be a null node when nothing is in scope.</param>
    /// <param name="options">Rewrite callbacks, or null.</param>
    /// <returns>UTF-8 XML content, possibly several top-level nodes.</returns>
    public static byte[] Serialize(IEnumerable<MarkupNode> nodes, MarkupNode destinationScope, MarkupFragmentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var w = new ArrayBufferWriter<byte>();
        foreach (MarkupNode n in nodes)
        {
            Write(w, n, destinationScope, options ?? new MarkupFragmentOptions(), top: true);
        }

        return w.WrittenSpan.ToArray();
    }

    /// <summary>Serialises one node (see <see cref="Serialize(IEnumerable{MarkupNode}, MarkupNode, MarkupFragmentOptions?)"/>).</summary>
    /// <param name="node">The node.</param>
    /// <param name="destinationScope">Where the XML will be inserted.</param>
    /// <param name="options">Rewrite callbacks, or null.</param>
    /// <returns>UTF-8 XML.</returns>
    public static byte[] Serialize(MarkupNode node, MarkupNode destinationScope, MarkupFragmentOptions? options = null) =>
        Serialize([node], destinationScope, options);

    /// <summary>Serialises the children of <paramref name="parent"/> (not the element itself).</summary>
    /// <param name="parent">The element whose content is copied.</param>
    /// <param name="destinationScope">Where the XML will be inserted.</param>
    /// <param name="options">Rewrite callbacks, or null.</param>
    /// <returns>UTF-8 XML content.</returns>
    public static byte[] SerializeChildren(MarkupNode parent, MarkupNode destinationScope, MarkupFragmentOptions? options = null)
    {
        var kids = new List<MarkupNode>();
        foreach (MarkupNode c in parent.Children)
        {
            kids.Add(c);
        }

        return Serialize(kids, destinationScope, options);
    }

    private static void Write(ArrayBufferWriter<byte> w, MarkupNode n, MarkupNode scope, MarkupFragmentOptions o, bool top)
    {
        switch (n.Kind)
        {
            case MarkupNodeKind.Element:
                WriteElement(w, n, scope, o, top);
                break;
            case MarkupNodeKind.Text:
                if (o.RewriteText?.Invoke(n) is { } replaced)
                {
                    Put(w, Escape(replaced, false));
                }
                else
                {
                    Put(w, n.RawContent);
                }

                break;
            case MarkupNodeKind.Whitespace:
                Put(w, n.RawContent);
                break;
            case MarkupNodeKind.CData:
                Put(w, "<![CDATA["u8);
                Put(w, n.RawContent);
                Put(w, "]]>"u8);
                break;
            case MarkupNodeKind.Comment:
                Put(w, "<!--"u8);
                Put(w, n.RawContent);
                Put(w, "-->"u8);
                break;
            default:
                // Processing instructions and declarations have no place inside a fragment.
                break;
        }
    }

    private static void WriteElement(ArrayBufferWriter<byte> w, MarkupNode e, MarkupNode scope, MarkupFragmentOptions o, bool top)
    {
        if (o.KeepElement is { } keep && !keep(e))
        {
            return;
        }

        Put(w, "<"u8);
        Put(w, e.RawName);

        var own = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                own.Add(Encoding.UTF8.GetString(a.RawName));
            }
        }

        if (top)
        {
            WriteScopeDeclarations(w, e, scope, own);
        }

        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                Put(w, " "u8);
                Put(w, a.RawName);
                Put(w, "="u8);
                PutQuoted(w, a.Quote, a.RawValue);
                continue;
            }

            AttributeEdit edit = o.RewriteAttribute is null ? default : o.RewriteAttribute(e, a);
            if (edit.Drop)
            {
                continue;
            }

            Put(w, " "u8);
            Put(w, a.RawName);
            Put(w, "="u8);
            if (edit.Value is { } v)
            {
                Put(w, "\""u8);
                Put(w, Escape(v, true));
                Put(w, "\""u8);
            }
            else
            {
                PutQuoted(w, a.Quote, a.RawValue);
            }
        }

        if (e.FirstChild.IsNull && e.IsSelfClosing)
        {
            Put(w, "/>"u8);
            return;
        }

        Put(w, ">"u8);
        for (MarkupNode c = e.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            Write(w, c, scope, o, top: false);
        }

        Put(w, "</"u8);
        Put(w, e.RawName);
        Put(w, ">"u8);
    }

    // Every declaration in force at the element (nearest wins) that the destination does not already have, except those the element declares itself.
    private static void WriteScopeDeclarations(ArrayBufferWriter<byte> w, MarkupNode e, MarkupNode scope, HashSet<string> own)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (MarkupNode a = e.Parent; !a.IsNull && a.Kind == MarkupNodeKind.Element; a = a.Parent)
        {
            foreach (MarkupAttributeRef d in a.Attributes)
            {
                if (!d.IsNamespaceDeclaration)
                {
                    continue;
                }

                string name = Encoding.UTF8.GetString(d.RawName);
                if (own.Contains(name) || !seen.Add(name))
                {
                    continue;
                }

                string prefix = name.Length > 6 ? name[6..] : string.Empty; // "xmlns:p" -> "p"; "xmlns" -> ""
                string uri = d.Value;
                if (!scope.IsNull && scope.TryResolvePrefix(prefix, out string current) && current == uri)
                {
                    continue;
                }

                Put(w, " "u8);
                Put(w, d.RawName);
                Put(w, "="u8);
                PutQuoted(w, d.Quote, d.RawValue);
            }
        }
    }

    private static void PutQuoted(ArrayBufferWriter<byte> w, byte quote, ReadOnlySpan<byte> raw)
    {
        byte q = quote == (byte)'\'' ? (byte)'\'' : (byte)'"';
        Span<byte> one = [q];
        Put(w, one);
        Put(w, raw);
        Put(w, one);
    }

    private static void Put(ArrayBufferWriter<byte> w, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(w.GetSpan(bytes.Length));
        w.Advance(bytes.Length);
    }

    private static void Put(ArrayBufferWriter<byte> w, string text) => Put(w, Encoding.UTF8.GetBytes(text));

    private static string Escape(string s, bool attribute)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"' when attribute: sb.Append("&quot;"); break;
                case '\t' when attribute: sb.Append("&#9;"); break;
                case '\n' when attribute: sb.Append("&#10;"); break;
                case '\r' when attribute: sb.Append("&#13;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}
