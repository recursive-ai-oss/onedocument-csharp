using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>Small readers over PresentationML and DrawingML elements that work the same for the Transitional and the Strict namespaces.</summary>
internal static class Pml
{
    public const string PTransitional = "http://schemas.openxmlformats.org/presentationml/2006/main";
    public const string ATransitional = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public const string RTransitional = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string PStrict = "http://purl.oclc.org/ooxml/presentationml/main";
    public const string AStrict = "http://purl.oclc.org/ooxml/drawingml/main";
    public const string RStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    public static bool Is(MarkupRef e, SchemaNamespace ns, string local) =>
        e.IsValid && e.Namespace == ns && e.Node.LocalNameString == local;

    public static MarkupRef? Child(MarkupRef parent, SchemaNamespace ns, string local)
    {
        if (!parent.IsValid)
        {
            return null;
        }

        foreach (MarkupNode c in parent.Node.ChildElements)
        {
            var r = new MarkupRef(c);
            if (r.Namespace == ns && c.LocalNameString == local)
            {
                return r;
            }
        }

        return null;
    }

    public static MarkupRef? P(MarkupRef parent, string local) => Child(parent, SchemaNamespace.Presentation, local);

    public static MarkupRef? A(MarkupRef parent, string local) => Child(parent, SchemaNamespace.Drawing, local);

    public static List<MarkupRef> Elements(MarkupRef parent)
    {
        var list = new List<MarkupRef>();
        if (parent.IsValid)
        {
            foreach (MarkupNode c in parent.Node.ChildElements)
            {
                list.Add(new MarkupRef(c));
            }
        }

        return list;
    }

    public static List<MarkupRef> Elements(MarkupRef parent, SchemaNamespace ns, string local)
    {
        var list = new List<MarkupRef>();
        if (parent.IsValid)
        {
            foreach (MarkupNode c in parent.Node.ChildElements)
            {
                var r = new MarkupRef(c);
                if (r.Namespace == ns && c.LocalNameString == local)
                {
                    list.Add(r);
                }
            }
        }

        return list;
    }

    /// <summary>The value of an unprefixed attribute.</summary>
    public static string? Attr(MarkupRef e, string local) => e.IsValid ? e.Node.GetAttribute(local) : null;

    public static long? AttrLong(MarkupRef e, string local) =>
        Attr(e, local) is { } s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    public static int? AttrInt(MarkupRef e, string local) =>
        Attr(e, local) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    // ECMA-376 Part 1 §22.9.2.7 (ST_OnOff): true/false, 1/0, and in Transitional also on/off.
    public static bool? AttrBool(MarkupRef e, string local) => Attr(e, local) switch
    {
        "1" or "true" or "on" => true,
        "0" or "false" or "off" => false,
        _ => null,
    };

    /// <summary>The relationship id an element carries in an attribute of the relationships namespace (<c>r:id</c>, <c>r:embed</c>, <c>r:link</c>).</summary>
    public static string? RelAttr(MarkupRef e, string local)
    {
        if (!e.IsValid)
        {
            return null;
        }

        foreach (MarkupAttributeRef a in e.Node.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && a.LocalName.SequenceEqual(Encoding.ASCII.GetBytes(local))
                && SchemaNamespaces.Resolve(a.NamespaceUriBytes) == SchemaNamespace.Relationships)
            {
                return a.Value;
            }
        }

        return null;
    }

    /// <summary>True when a relationship type is the named one in any of its spellings (Transitional, Strict or a Microsoft extension): the type ends in <c>/relationships/&lt;name&gt;</c> or <c>/&lt;name&gt;</c>.</summary>
    public static bool RelIs(string type, string name) =>
        type.EndsWith("/" + name, StringComparison.Ordinal);

    /// <summary>The last segment of a relationship type.</summary>
    public static string RelName(string type)
    {
        int i = type.LastIndexOf('/');
        return i < 0 ? type : type[(i + 1)..];
    }

    /// <summary>Escapes text for an element or attribute value. Characters XML 1.0 cannot carry are written as <c>_xHHHH_</c>, the way Office writes them.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c < 0x20 && c != '\t' && c != '\n' && c != '\r' || c is '\uFFFE' or '\uFFFF'
                        || (char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(text[i - 1])))
                        || (char.IsHighSurrogate(c) && (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))))
                    {
                        sb.Append("_x").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)).Append('_');
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Escapes text that goes into a fragment template, where braces mark the prefix placeholders.</summary>
    public static string EscapeForFragment(string text) => Escape(text).Replace("{", "&#123;", StringComparison.Ordinal);

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}

/// <summary>The prefixes a generated fragment uses, so that it parses wherever it is inserted: found in scope, else declared on the fragment's first element.</summary>
internal readonly struct Prefixes
{
    public string A { get; }

    public string P { get; }

    public string R { get; }

    /// <summary>Namespace declarations to write on the first element of a fragment (empty when everything is in scope).</summary>
    public string Declare { get; }

    public Prefixes(MarkupNode context)
    {
        bool strict = SchemaNamespaces.IsStrict(context.NamespaceUri);
        var declare = new StringBuilder();
        A = Find(context, strict ? Pml.AStrict : Pml.ATransitional, "a", declare);
        P = Find(context, strict ? Pml.PStrict : Pml.PTransitional, "p", declare);
        R = Find(context, strict ? Pml.RStrict : Pml.RTransitional, "r", declare);
        Declare = declare.ToString();
    }

    private static string Find(MarkupNode context, string uri, string preferred, StringBuilder declare)
    {
        if (context.TryGetPrefixInScope(uri, out string prefix) && prefix.Length > 0)
        {
            return prefix;
        }

        // Declared on the fragment; a prefix already bound to another namespace in scope would make the fragment ambiguous, so pick a free one.
        string chosen = preferred;
        for (int i = 1; context.TryResolvePrefix(chosen, out string other) && other != uri; i++)
        {
            chosen = preferred + "x" + i.ToString(CultureInfo.InvariantCulture);
        }

        declare.Append(" xmlns:").Append(chosen).Append("=\"").Append(uri).Append('"');
        return chosen;
    }
}
