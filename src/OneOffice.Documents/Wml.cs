using System.Globalization;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>Small readers over WordprocessingML elements that work the same for the Transitional and the Strict namespaces.</summary>
internal static class Wml
{
    public static bool Is(MarkupRef e, ReadOnlySpan<byte> local) =>
        e.IsValid && e.Namespace == SchemaNamespace.Wordprocessing && e.LocalName.SequenceEqual(local);

    /// <summary>Reads an attribute of the WordprocessingML namespace (or an unprefixed one) by local name.</summary>
    public static string? Attr(MarkupRef e, string local)
    {
        if (!e.IsValid)
        {
            return null;
        }

        foreach (MarkupAttributeRef a in e.Node.Attributes)
        {
            if (a.IsNamespaceDeclaration || !AsciiEquals(a.LocalName, local))
            {
                continue;
            }

            if (a.Prefix.Length == 0 || SchemaNamespaces.Resolve(a.NamespaceUriBytes) == SchemaNamespace.Wordprocessing)
            {
                return a.Value;
            }
        }

        return null;
    }

    /// <summary>Compares UTF-8 bytes with an ASCII name without allocating.</summary>
    public static bool AsciiEquals(ReadOnlySpan<byte> bytes, string ascii)
    {
        if (bytes.Length != ascii.Length)
        {
            return false;
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != ascii[i])
            {
                return false;
            }
        }

        return true;
    }

    public static MarkupRef? Child(MarkupRef parent, ReadOnlySpan<byte> local) =>
        parent.IsValid ? parent.FindChild(SchemaNamespace.Wordprocessing, local) : null;

    public static IEnumerable<MarkupRef> Elements(MarkupRef parent)
    {
        if (!parent.IsValid)
        {
            yield break;
        }

        for (MarkupNode n = parent.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element)
            {
                yield return new MarkupRef(n);
            }
        }
    }

    /// <summary>ECMA-376 Part 1 §17.17.4 (ST_OnOff): true, 1, on / false, 0, off. Anything else is null.</summary>
    public static bool? OnOff(string? text) => text switch
    {
        "true" or "1" or "on" => true,
        "false" or "0" or "off" => false,
        _ => null,
    };

    /// <summary>The value of an on/off property element: present without a value means on (§17.3.2.1).</summary>
    public static bool OnOffElement(MarkupRef e) => OnOff(Attr(e, "val")) ?? true;

    public static long? Long(string? text) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) ? v : null;

    public static MarkupRef? AncestorOrSelf(MarkupRef e, ReadOnlySpan<byte> local)
    {
        for (MarkupNode n = e.Node; !n.IsNull && n.Kind == MarkupNodeKind.Element; n = n.Parent)
        {
            var r = new MarkupRef(n);
            if (Is(r, local))
            {
                return r;
            }
        }

        return null;
    }
}
