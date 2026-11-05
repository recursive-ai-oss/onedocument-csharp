using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Documents.Revisions;

/// <summary>Small helpers over the markup tree for WordprocessingML: name tests, attributes, serialisation.</summary>
internal static class WordXml
{
    internal const string Main = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string MainStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private static readonly byte[] MainBytes = Encoding.UTF8.GetBytes(Main);
    private static readonly byte[] StrictBytes = Encoding.UTF8.GetBytes(MainStrict);

    /// <summary>Tests an ASCII name against a span.</summary>
    internal static bool SpanEquals(ReadOnlySpan<byte> span, string ascii)
    {
        if (span.Length != ascii.Length)
        {
            return false;
        }

        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] != ascii[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the element is in the WordprocessingML main namespace (Transitional or Strict).</summary>
    internal static bool InMain(MarkupNode n)
    {
        ReadOnlySpan<byte> ns = n.NamespaceUriBytes;
        return ns.SequenceEqual(MainBytes) || ns.SequenceEqual(StrictBytes);
    }

    /// <summary>True when the node is a WordprocessingML element with this local name.</summary>
    internal static bool Is(MarkupNode n, string local) =>
        n.Kind == MarkupNodeKind.Element && SpanEquals(n.LocalName, local) && InMain(n);

    /// <summary>True when the node is a WordprocessingML element with one of these local names.</summary>
    internal static bool IsAny(MarkupNode n, params string[] locals)
    {
        if (n.Kind != MarkupNodeKind.Element || !InMain(n))
        {
            return false;
        }

        ReadOnlySpan<byte> name = n.LocalName;
        foreach (string l in locals)
        {
            if (SpanEquals(name, l))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets an attribute of the main namespace by local name (an unprefixed attribute is accepted too).</summary>
    internal static string? Attr(MarkupNode n, string local)
    {
        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (a.IsNamespaceDeclaration || !SpanEquals(a.LocalName, local))
            {
                continue;
            }

            if (a.Prefix.Length == 0 || a.IsInNamespace(MainBytes) || a.IsInNamespace(StrictBytes))
            {
                return a.Value;
            }
        }

        return null;
    }

    /// <summary>First child element with the name, or a null node.</summary>
    internal static MarkupNode Child(MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (Is(c, local))
            {
                return c;
            }
        }

        return default;
    }

    /// <summary>The next sibling that is an element, or a null node.</summary>
    internal static MarkupNode NextElement(MarkupNode n)
    {
        for (MarkupNode s = n.NextSibling; !s.IsNull; s = s.NextSibling)
        {
            if (s.Kind == MarkupNodeKind.Element)
            {
                return s;
            }
        }

        return default;
    }

    /// <summary>The previous sibling that is an element, or a null node.</summary>
    internal static MarkupNode PreviousElement(MarkupNode n)
    {
        for (MarkupNode s = n.PreviousSibling; !s.IsNull; s = s.PreviousSibling)
        {
            if (s.Kind == MarkupNodeKind.Element)
            {
                return s;
            }
        }

        return default;
    }

    /// <summary>The preorder successor of a node inside <paramref name="root"/>, optionally not descending.</summary>
    internal static MarkupNode Next(MarkupNode n, MarkupNode root, bool descend)
    {
        if (descend && !n.FirstChild.IsNull)
        {
            return n.FirstChild;
        }

        for (MarkupNode c = n; !c.IsNull && c != root; c = c.Parent)
        {
            if (!c.NextSibling.IsNull)
            {
                return c.NextSibling;
            }
        }

        return default;
    }

    /// <summary>The nearest ancestor element with the name, or a null node.</summary>
    internal static MarkupNode Ancestor(MarkupNode n, string local)
    {
        for (MarkupNode p = n.Parent; !p.IsNull; p = p.Parent)
        {
            if (Is(p, local))
            {
                return p;
            }
        }

        return default;
    }

    /// <summary>The UTF-8 bytes of a string.</summary>
    internal static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>The XML of all children of a node, in order, exactly as they stand, each one made portable (see <see cref="Portable"/>).</summary>
    internal static string InnerXml(MarkupNode n)
    {
        var sb = new StringBuilder();
        for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            sb.Append(Portable(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// The XML of a node for putting it somewhere else in the same part. A prefix the node uses that is declared above it, but not on
    /// the part's root element, would be unbound at a new place under another parent (a declaration on the old paragraph is gone with it),
    /// so those declarations are written on the node's own start tag. When every prefix is declared inside the node or on the root, the
    /// text is exactly <see cref="MarkupNode.OuterXml"/>. XML 1.0 and Namespaces in XML 1.0 §6: a prefix is bound by a declaration on the element or an ancestor.
    /// </summary>
    internal static string Portable(MarkupNode n)
    {
        if (n.Kind != MarkupNodeKind.Element)
        {
            return n.OuterXml;
        }

        SortedDictionary<string, string>? free = null;
        FreePrefixes(n, [], ref free);
        string xml = n.OuterXml;
        if (free is null)
        {
            return xml;
        }

        MarkupNode root = n;
        while (!root.Parent.IsNull && root.Parent.Kind == MarkupNodeKind.Element)
        {
            root = root.Parent;
        }

        var decl = new StringBuilder();
        foreach ((string prefix, string uri) in free)
        {
            if (root.TryResolvePrefix(prefix, out string atRoot) && atRoot == uri)
            {
                continue;
            }

            decl.Append(prefix.Length == 0 ? " xmlns=\"" : " xmlns:" + prefix + "=\"").Append(EscapeAttribute(uri)).Append('"');
        }

        if (decl.Length == 0)
        {
            return xml;
        }

        int at = 1;
        while (at < xml.Length && !char.IsWhiteSpace(xml[at]) && xml[at] != '>' && xml[at] != '/')
        {
            at++;
        }

        return xml[..at] + decl + xml[at..];
    }

    private static void FreePrefixes(MarkupNode e, HashSet<string> declared, ref SortedDictionary<string, string>? free)
    {
        HashSet<string> here = declared;
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                if (ReferenceEquals(here, declared))
                {
                    here = [.. declared];
                }

                here.Add(a.RawName.Length <= 5 ? string.Empty : Encoding.UTF8.GetString(a.LocalName));
            }
        }

        Need(e, Encoding.UTF8.GetString(e.Prefix), here, ref free);
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0)
            {
                Need(e, Encoding.UTF8.GetString(a.Prefix), here, ref free);
            }
        }

        foreach (MarkupNode c in e.ChildElements)
        {
            FreePrefixes(c, here, ref free);
        }
    }

    private static void Need(MarkupNode e, string prefix, HashSet<string> declared, ref SortedDictionary<string, string>? free)
    {
        if (prefix == "xml" || declared.Contains(prefix) || (free is not null && free.ContainsKey(prefix)))
        {
            return;
        }

        if (e.TryResolvePrefix(prefix, out string uri) && (uri.Length > 0 || prefix.Length > 0))
        {
            (free ??= [])[prefix] = uri;
        }
    }

    /// <summary>The XML of the element children of a node (text between them left out).</summary>
    internal static string ElementsXml(MarkupNode n)
    {
        var sb = new StringBuilder();
        foreach (MarkupNode c in n.ChildElements)
        {
            sb.Append(Portable(c));
        }

        return sb.ToString();
    }

    /// <summary>Replaces all children of an element with the given XML.</summary>
    internal static void SetContent(MarkupNode element, string xml)
    {
        for (MarkupNode c = element.FirstChild; !c.IsNull;)
        {
            MarkupNode next = c.NextSibling;
            c.Remove();
            c = next;
        }

        if (xml.Length > 0)
        {
            element.AppendXml(Bytes(xml));
        }
    }

    /// <summary>The start tag of an element as written, with its attributes (an end of tag inside a quoted value is respected).</summary>
    internal static string StartTag(MarkupNode element)
    {
        string xml = element.OuterXml;
        char quote = '\0';
        for (int i = 0; i < xml.Length; i++)
        {
            char c = xml[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return xml[i - 1] == '/' ? xml[..(i - 1)] + ">" : xml[..(i + 1)];
            }
        }

        return xml;
    }

    /// <summary>Escapes text for element content.</summary>
    internal static string EscapeText(string s)
    {
        if (s.AsSpan().IndexOfAny('&', '<', '>') < 0)
        {
            return s;
        }

        return s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    }

    /// <summary>Escapes text for a double-quoted attribute value.</summary>
    internal static string EscapeAttribute(string s) =>
        EscapeText(s).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("\t", "&#9;", StringComparison.Ordinal).Replace("\n", "&#10;", StringComparison.Ordinal).Replace("\r", "&#13;", StringComparison.Ordinal);

    /// <summary>Checks that every character is allowed in XML 1.0 and throws otherwise.</summary>
    internal static void RequireXmlText(string s, string paramName)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool ok = c is '\t' or '\n' or '\r' || (c >= 0x20 && c < 0xD800) || (c >= 0xE000 && c <= 0xFFFD);
            if (!ok && char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
                ok = true;
            }

            if (!ok)
            {
                throw new ArgumentException("The text has a character that XML cannot carry.", paramName);
            }
        }
    }

    /// <summary>Formats a date the way Word writes it: UTC to the second.</summary>
    internal static string FormatDate(DateTimeOffset d) =>
        d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Builds WordprocessingML fragments with the prefix and flavour of the place they will be inserted into.</summary>
internal readonly struct WmlWriter
{
    private readonly string _prefix;

    private WmlWriter(string prefix, bool strict)
    {
        _prefix = prefix;
        IsStrict = strict;
    }

    internal bool IsStrict { get; }

    /// <summary>Finds the prefix the main namespace has at <paramref name="context"/>.</summary>
    internal static WmlWriter For(MarkupNode context)
    {
        if (context.TryGetPrefixInScope(WordXml.Main, out string p))
        {
            return new WmlWriter(p, false);
        }

        if (context.TryGetPrefixInScope(WordXml.MainStrict, out p))
        {
            return new WmlWriter(p, true);
        }

        throw new ArgumentException("The node is not inside a WordprocessingML part (the main namespace is not bound with a prefix here).", nameof(context));
    }

    internal string Name(string local) => _prefix + ":" + local;

    /// <summary>An empty element with attributes: <c>&lt;w:ins w:id=".."/&gt;</c>.</summary>
    internal string Empty(string local, string attributes = "") => "<" + Name(local) + attributes + "/>";

    /// <summary>An element around content.</summary>
    internal string Wrap(string local, string attributes, string content) =>
        "<" + Name(local) + attributes + ">" + content + "</" + Name(local) + ">";

    /// <summary>An attribute: <c> w:val="x"</c>.</summary>
    internal string Attr(string local, string value) => " " + Name(local) + "=\"" + WordXml.EscapeAttribute(value) + "\"";
}
