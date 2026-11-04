using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>Small helpers for editing WordprocessingML elements in place; every edit goes through the markup tree.</summary>
internal static class WmlEdit
{
    /// <summary>Sets an attribute of the element's own vocabulary (written <c>w:name</c>, using the prefix in scope).</summary>
    public static void SetAttr(MarkupRef e, string local, string value) =>
        e.Node.SetAttribute(e.NamespaceUriText, local, value, "w");

    /// <summary>Removes an attribute given by local name when it is unprefixed or in the element's vocabulary.</summary>
    public static bool RemoveAttr(MarkupRef e, string local)
    {
        string? raw = null;
        foreach (MarkupAttributeRef a in e.Node.Attributes)
        {
            if (!a.IsNamespaceDeclaration && Wml.AsciiEquals(a.LocalName, local)
                && (a.Prefix.Length == 0 || SchemaNamespaces.Resolve(a.NamespaceUriBytes) == SchemaNamespace.Wordprocessing))
            {
                raw = Encoding.UTF8.GetString(a.RawName);
                break;
            }
        }

        return raw is not null && e.Node.RemoveAttribute(raw);
    }

    /// <summary>The element's qualified name for a child in the same vocabulary, using the prefix the element itself is written with.</summary>
    public static string Qualified(MarkupRef near, string local)
    {
        ReadOnlySpan<byte> prefix = near.Node.Prefix;
        return prefix.Length == 0 ? local : Encoding.UTF8.GetString(prefix) + ":" + local;
    }

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Lists the child elements so that they can be removed or moved without disturbing the enumeration.</summary>
    public static List<MarkupRef> ElementList(MarkupRef parent) => [.. Wml.Elements(parent)];

    /// <summary>Removes the paragraph-identity attributes Word adds (<c>w14:paraId</c>, <c>w14:textId</c>) from a copy so that ids stay unique.</summary>
    public static void StripParagraphIds(MarkupRef paragraph)
    {
        var names = new List<string>();
        foreach (MarkupAttributeRef a in paragraph.Node.Attributes)
        {
            if (!a.IsNamespaceDeclaration && (Wml.AsciiEquals(a.LocalName, "paraId") || Wml.AsciiEquals(a.LocalName, "textId")) && a.Prefix.Length > 0)
            {
                names.Add(Encoding.UTF8.GetString(a.RawName));
            }
        }

        foreach (string n in names)
        {
            paragraph.Node.RemoveAttribute(n);
        }
    }
}

public sealed partial class Document
{
    /// <summary>Reports a repair found in the model, located at an element of whichever part it sits in.</summary>
    internal void ReportDefect(string code, DefectKind kind, DefectSeverity severity, string message, MarkupRef at)
    {
        string part = (at.IsValid ? Parts.PartNameOf(at.Node) : null) ?? MainPartName;
        Context.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InPart(part, at.IsValid ? at.Node.SourceStart : -1)));
    }
}
