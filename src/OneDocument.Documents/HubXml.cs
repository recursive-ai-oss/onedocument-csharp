using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>Builds small markup fragments that are inserted into a part, using the prefixes that part already uses.</summary>
internal static class HubXml
{
    public const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// Finds the prefix for <paramref name="ns"/> in scope at <paramref name="context"/>. When none is bound, <paramref name="preferred"/>
    /// is used and <paramref name="declaration"/> holds the <c>xmlns</c> attribute a fragment's root element must carry.
    /// </summary>
    public static string Prefix(MarkupNode context, string ns, string preferred, out string declaration)
    {
        if (context.TryGetPrefixInScope(ns, out string prefix))
        {
            declaration = string.Empty;
            return prefix;
        }

        declaration = " xmlns:" + preferred + "=\"" + ns + "\"";
        return preferred;
    }

    /// <summary>Escapes character data and attribute values. Characters XML cannot carry are refused.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c < 0x20 && c != '\t' && c != '\n' && c != '\r' || c == '\uFFFE' || c == '\uFFFF')
                    {
                        throw new ArgumentException("The text contains a character that XML cannot carry (U+" + ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + ").", nameof(text));
                    }

                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The run content for a text: <c>w:t</c> elements with tabs as <c>w:tab</c> and line feeds as <c>w:br</c>, so that
    /// <see cref="Run.Text"/> reads the same string back.
    /// </summary>
    public static string RunContent(string prefix, string text, string textElement = "t")
    {
        var sb = new StringBuilder();
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool end = i == text.Length;
            char c = end ? '\0' : text[i];
            if (!end && c != '\t' && c != '\n')
            {
                continue;
            }

            if (i > start)
            {
                sb.Append('<').Append(prefix).Append(':').Append(textElement).Append(" xml:space=\"preserve\">")
                    .Append(Escape(text.Substring(start, i - start).Replace("\r", string.Empty, StringComparison.Ordinal)))
                    .Append("</").Append(prefix).Append(':').Append(textElement).Append('>');
            }

            if (!end)
            {
                sb.Append('<').Append(prefix).Append(':').Append(c == '\t' ? "tab" : "br").Append("/>");
            }

            start = i + 1;
        }

        return sb.ToString();
    }

    public static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    /// <summary>Removes every child element of <paramref name="element"/> except the ones named in <paramref name="keep"/>.</summary>
    public static void ClearChildrenExcept(MarkupNode element, string keep)
    {
        var doomed = new List<MarkupNode>();
        for (MarkupNode n = element.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == keep)
            {
                continue;
            }

            doomed.Add(n);
        }

        foreach (MarkupNode n in doomed)
        {
            n.Remove();
        }
    }

    public static MarkupNode FirstChildElement(MarkupNode parent, string local)
    {
        for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == local)
            {
                return n;
            }
        }

        return default;
    }

    /// <summary>
    /// Finds, in document order, the elements below <paramref name="root"/> that are in a WordprocessingML namespace and have one
    /// of the local names. The <c>mc:Fallback</c> branch of an alternate content block is skipped: it repeats the chosen branch.
    /// </summary>
    public static List<MarkupNode> FindWordElements(MarkupNode root, params string[] localNames)
    {
        var found = new List<MarkupNode>();
        if (!root.IsNull)
        {
            Find(root, localNames, found);
        }

        return found;
    }

    private static void Find(MarkupNode parent, string[] names, List<MarkupNode> found)
    {
        for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            string ns = n.NamespaceUri;
            if (ns == "http://schemas.openxmlformats.org/markup-compatibility/2006" && n.LocalNameString == "Fallback")
            {
                continue;
            }

            if (IsWordNamespace(ns) && Array.IndexOf(names, n.LocalNameString) >= 0)
            {
                found.Add(n);
            }

            Find(n, names, found);
        }
    }

    public static bool IsWordNamespace(string ns) => ns == WordprocessingNamespace || ns == "http://purl.oclc.org/ooxml/wordprocessingml/main";

    /// <summary>
    /// Inserts <paramref name="xml"/> as a child of <paramref name="parent"/> before the first child whose local name is in
    /// <paramref name="successors"/> (the elements the schema sequence puts after it), else at the end.
    /// </summary>
    public static void InsertInSequence(MarkupNode parent, string xml, string[] successors)
    {
        for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && Array.IndexOf(successors, n.LocalNameString) >= 0)
            {
                n.InsertXmlBefore(Bytes(xml));
                return;
            }
        }

        parent.AppendXml(Bytes(xml));
    }
}
