using System.Text;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents;

public sealed partial class Paragraph
{
    /// <summary>
    /// Replaces the paragraph's text. The first run that holds text keeps its formatting and takes the new text; later runs that hold
    /// only text are removed. Everything else (paragraph properties, bookmarks, fields, drawings, other runs) is left where it is. A paragraph
    /// without a text run gets a new one at its end.
    /// </summary>
    /// <param name="text">The text. A tab character becomes <c>w:tab</c>, a line feed becomes <c>w:br</c>, and nothing else is converted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string ns = Markup.NamespaceUriText;
        string prefix = Owner_Prefix(Markup.Node, ns, out bool declare);
        string runContent = RunContent(text, prefix, declare ? ns : null);

        Run? first = null;
        var drop = new List<Run>();
        foreach (Run run in Runs())
        {
            if (!IsTextRun(run))
            {
                continue;
            }

            if (first is null)
            {
                first = run;
            }
            else if (IsPlainRun(run))
            {
                drop.Add(run);
            }
        }

        if (first is null)
        {
            string xml = "<" + prefix + ":r" + (declare ? " xmlns:" + prefix + "=\"" + ns + "\"" : string.Empty) + ">" + runContent + "</" + prefix + ":r>";
            Markup.Node.AppendXml(Encoding.UTF8.GetBytes(xml));
            return;
        }

        foreach (Run run in drop)
        {
            run.Markup.Node.Remove();
        }

        MarkupNode runNode = first.Markup.Node;
        var children = new List<MarkupNode>();
        foreach (MarkupNode child in runNode.ChildElements)
        {
            if (!child.IsElement(ns, "rPr"))
            {
                children.Add(child);
            }
        }

        foreach (MarkupNode child in children)
        {
            child.Remove();
        }

        runNode.AppendXml(Encoding.UTF8.GetBytes(runContent));
    }

    private static bool IsTextRun(Run run)
    {
        foreach (MarkupNode child in run.Markup.Node.ChildElements)
        {
            if (child.IsElement(run.Markup.NamespaceUriText, "t"))
            {
                return true;
            }
        }

        return false;
    }

    // A run of nothing but properties and text-like content, which SetText can fold into the first one without losing anything.
    private static bool IsPlainRun(Run run)
    {
        string ns = run.Markup.NamespaceUriText;
        foreach (MarkupNode child in run.Markup.Node.ChildElements)
        {
            if (!(child.IsElement(ns, "rPr") || child.IsElement(ns, "t") || child.IsElement(ns, "tab") || child.IsElement(ns, "br")))
            {
                return false;
            }
        }

        return true;
    }

    internal static string Owner_Prefix(MarkupNode node, string ns, out bool declare)
    {
        if (node.TryGetPrefixInScope(ns, out string prefix) && prefix.Length > 0)
        {
            declare = false;
            return prefix;
        }

        declare = true;
        return "w";
    }

    // ECMA-376 Part 1 §17.3.3: w:t holds text, w:tab a tab character, w:br a line break. §17.3.3.31: xml:space="preserve" keeps the
    // leading and trailing spaces of a w:t.
    internal static string RunContent(string text, string prefix, string? declareNamespace = null)
    {
        var sb = new StringBuilder();
        var segment = new StringBuilder();
        string Decl() => declareNamespace is null ? string.Empty : " xmlns:" + prefix + "=\"" + declareNamespace + "\"";
        void Flush()
        {
            if (segment.Length == 0)
            {
                return;
            }

            sb.Append('<').Append(prefix).Append(":t").Append(Decl()).Append(" xml:space=\"preserve\">");
            Escape(sb, segment.ToString());
            sb.Append("</").Append(prefix).Append(":t>");
            segment.Clear();
        }

        foreach (char c in text)
        {
            switch (c)
            {
                case '\t':
                    Flush();
                    sb.Append('<').Append(prefix).Append(":tab").Append(Decl()).Append("/>");
                    break;
                case '\n':
                    Flush();
                    sb.Append('<').Append(prefix).Append(":br").Append(Decl()).Append("/>");
                    break;
                case '\r':
                    break;
                default:
                    segment.Append(c);
                    break;
            }
        }

        Flush();
        return sb.ToString();
    }

    private static void Escape(StringBuilder sb, string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
    }
}

public sealed partial class Body
{
    /// <summary>Adds a paragraph with the given text at the end of the body, before the final section properties.</summary>
    /// <param name="text">The text; see <see cref="Paragraph.SetText(string)"/> for how tabs and line feeds are written.</param>
    /// <param name="styleId">The paragraph style id, or null for the default style. It is not checked against the style sheet.</param>
    /// <returns>The new paragraph.</returns>
    /// <exception cref="InvalidOperationException">The document has no <c>w:body</c> element.</exception>
    public Paragraph AddParagraph(string text, string? styleId = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Container.IsValid)
        {
            throw new InvalidOperationException("The document has no body to add a paragraph to.");
        }

        string ns = Container.NamespaceUriText;
        string prefix = Paragraph.Owner_Prefix(Container.Node, ns, out bool declare);
        var sb = new StringBuilder();
        sb.Append('<').Append(prefix).Append(":p");
        if (declare)
        {
            sb.Append(" xmlns:").Append(prefix).Append("=\"").Append(ns).Append('"');
        }

        sb.Append('>');
        if (styleId is not null)
        {
            sb.Append('<').Append(prefix).Append(":pPr><").Append(prefix).Append(":pStyle ").Append(prefix).Append(":val=\"")
              .Append(System.Security.SecurityElement.Escape(styleId)).Append("\"/></").Append(prefix).Append(":pPr>");
        }

        if (text.Length > 0)
        {
            sb.Append('<').Append(prefix).Append(":r>").Append(Paragraph.RunContent(text, prefix)).Append("</").Append(prefix).Append(":r>");
        }

        sb.Append("</").Append(prefix).Append(":p>");
        byte[] xml = Encoding.UTF8.GetBytes(sb.ToString());

        MarkupNode? sectPr = null;
        foreach (MarkupNode child in Container.Node.ChildElements)
        {
            if (child.IsElement(ns, "sectPr"))
            {
                sectPr = child;
            }
        }

        MarkupNode created;
        if (sectPr is { } s)
        {
            s.InsertXmlBefore(xml);
            created = s.PreviousSibling;
            while (!created.IsNull && created.Kind != MarkupNodeKind.Element)
            {
                created = created.PreviousSibling;
            }
        }
        else
        {
            Container.Node.AppendXml(xml);
            created = Container.Node.LastChild;
            while (!created.IsNull && created.Kind != MarkupNodeKind.Element)
            {
                created = created.PreviousSibling;
            }
        }

        return (Paragraph)BlockElement.Wrap(Owner, new MarkupRef(created));
    }
}
