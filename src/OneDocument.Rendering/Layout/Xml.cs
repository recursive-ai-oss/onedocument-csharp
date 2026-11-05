using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// Small readers over WordprocessingML markup for the layout. The hub keeps its own helpers internal, so the layout carries the few it needs.
internal static class Xml
{
    public static string? Attr(MarkupRef e, string local)
    {
        if (!e.IsValid)
        {
            return null;
        }

        foreach (MarkupAttributeRef a in e.Node.Attributes)
        {
            if (a.IsNamespaceDeclaration || !Eq(a.LocalName, local))
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

    // An attribute of the relationships namespace (r:id and friends) by local name, whatever prefix the file uses.
    public static string? AnyAttr(MarkupRef e, string local)
    {
        foreach (MarkupAttributeRef a in e.Node.Attributes)
        {
            if (!a.IsNamespaceDeclaration && Eq(a.LocalName, local))
            {
                return a.Value;
            }
        }

        return null;
    }

    public static bool Eq(ReadOnlySpan<byte> bytes, string ascii)
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

    public static MarkupRef? Child(MarkupRef parent, string local)
    {
        if (!parent.IsValid)
        {
            return null;
        }

        for (MarkupNode n = parent.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && Eq(n.LocalName, local))
            {
                return new MarkupRef(n);
            }
        }

        return null;
    }

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

    // ECMA-376 Part 1 §17.17.4 (ST_OnOff): an element without w:val is on.
    public static bool OnOff(MarkupRef e)
    {
        string? v = Attr(e, "val");
        return v is null || !(v is "0" or "false" or "off");
    }

    public static long? Long(string? s) => long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : null;

    // Loads the related part of the main document by relationship type suffix (for example "settings", "fontTable").
    public static (MarkupDocument? Document, string? PartName) LoadRelated(Document doc, string typeSuffix, string? sourcePart = null)
    {
        string source = sourcePart ?? doc.MainPartName;
        foreach (Relationship r in doc.Package.GetRelationships(source))
        {
            if (!r.Type.EndsWith("/" + typeSuffix, StringComparison.Ordinal) || !OpcPackage.TryResolveTarget(source, r, out PartUri target))
            {
                continue;
            }

            IPackagePart? part = doc.Package.FindPart(target.Value);
            if (part is null)
            {
                continue;
            }

            if (MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? parsed, doc.Context, target.Value))
            {
                return (parsed, target.Value);
            }
        }

        return (null, null);
    }
}
