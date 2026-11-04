using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>The XML namespaces the extractors tell apart. Transitional and Strict spellings of one namespace map to one value.</summary>
internal enum NsKind : byte
{
    Other = 0,
    Wml,
    Dml,
    Pml,
    Sml,
    Math,
    Mc,
    Rel,
}

internal static class Ns
{
    // ECMA-376 Part 1 (Transitional) and ISO/IEC 29500 Strict name each namespace differently; the elements inside are the same.
    public static NsKind Classify(ReadOnlySpan<byte> uri)
    {
        if (uri.Length == 0)
        {
            return NsKind.Other;
        }

        if (uri.SequenceEqual("http://schemas.openxmlformats.org/wordprocessingml/2006/main"u8) || uri.SequenceEqual("http://purl.oclc.org/ooxml/wordprocessingml/main"u8))
        {
            return NsKind.Wml;
        }

        if (uri.SequenceEqual("http://schemas.openxmlformats.org/drawingml/2006/main"u8) || uri.SequenceEqual("http://purl.oclc.org/ooxml/drawingml/main"u8))
        {
            return NsKind.Dml;
        }

        if (uri.SequenceEqual("http://schemas.openxmlformats.org/presentationml/2006/main"u8) || uri.SequenceEqual("http://purl.oclc.org/ooxml/presentationml/main"u8))
        {
            return NsKind.Pml;
        }

        if (uri.SequenceEqual("http://schemas.openxmlformats.org/spreadsheetml/2006/main"u8) || uri.SequenceEqual("http://purl.oclc.org/ooxml/spreadsheetml/main"u8))
        {
            return NsKind.Sml;
        }

        if (uri.SequenceEqual("http://schemas.openxmlformats.org/officeDocument/2006/math"u8) || uri.SequenceEqual("http://purl.oclc.org/ooxml/officeDocument/math"u8))
        {
            return NsKind.Math;
        }

        if (uri.SequenceEqual("http://schemas.openxmlformats.org/markup-compatibility/2006"u8))
        {
            return NsKind.Mc;
        }

        if (uri.SequenceEqual("http://schemas.openxmlformats.org/officeDocument/2006/relationships"u8) || uri.SequenceEqual("http://purl.oclc.org/ooxml/officeDocument/relationships"u8))
        {
            return NsKind.Rel;
        }

        return NsKind.Other;
    }

    /// <summary>Finds an attribute by local name, whatever its prefix. Returns false when absent.</summary>
    public static bool TryGetAttribute(MarkupReader reader, ReadOnlySpan<byte> localName, out ReadOnlySpan<byte> rawValue)
    {
        foreach (Markup.Xml.XmlAttributeToken a in reader.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual(localName))
            {
                rawValue = a.RawValue;
                return true;
            }
        }

        rawValue = default;
        return false;
    }

    /// <summary>Finds the relationship-id attribute (<c>r:id</c>, in either spelling of the relationships namespace).</summary>
    public static bool TryGetRelationshipId(MarkupReader reader, out string id)
    {
        foreach (Markup.Xml.XmlAttributeToken a in reader.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual("id"u8) && Classify(a.NamespaceUri) == NsKind.Rel)
            {
                id = Markup.Xml.XmlText.GetString(a.RawValue, true);
                return true;
            }
        }

        id = string.Empty;
        return false;
    }
}

/// <summary>
/// Chooses the branch of <c>mc:AlternateContent</c> to read, as a stream: the first <c>mc:Choice</c> whose required namespaces are all
/// understood, else the <c>mc:Fallback</c> (ECMA-376 Part 3 §10.2.4 and §10.2.5). The branch not chosen is skipped, so its text, which
/// repeats the chosen branch's, is not extracted twice.
/// </summary>
internal sealed class AlternateContentGate
{
    private readonly List<bool> _chosen = [];

    /// <summary>
    /// Handles a start tag. Returns true when it was an <c>mc:</c> element and the caller must not treat it as anything else;
    /// the reader has already been moved past a branch that is not taken.
    /// </summary>
    public bool OnStart(MarkupReader reader, ReadOnlySpan<byte> localName)
    {
        if (localName.SequenceEqual("AlternateContent"u8))
        {
            if (reader.Kind == Markup.Xml.XmlTokenKind.StartElement)
            {
                _chosen.Add(false);
            }

            return true;
        }

        if (_chosen.Count == 0)
        {
            return localName.SequenceEqual("Choice"u8) || localName.SequenceEqual("Fallback"u8);
        }

        bool isChoice = localName.SequenceEqual("Choice"u8);
        if (!isChoice && !localName.SequenceEqual("Fallback"u8))
        {
            return true;
        }

        int top = _chosen.Count - 1;
        if (_chosen[top] || (isChoice && !RequiresUnderstood(reader)))
        {
            reader.Skip();
            return true;
        }

        _chosen[top] = true;
        return true;
    }

    /// <summary>Handles an end tag of an <c>mc:</c> element.</summary>
    public void OnEnd(ReadOnlySpan<byte> localName)
    {
        if (localName.SequenceEqual("AlternateContent"u8) && _chosen.Count > 0)
        {
            _chosen.RemoveAt(_chosen.Count - 1);
        }
    }

    // A namespace is "understood" when Word, PowerPoint or Excel of the 2010 generation or later reads it: the standard's own
    // namespaces, VML, and Microsoft's Office extension namespaces. Text extraction therefore takes the modern branch (a DrawingML
    // text box, not its VML copy).
    private static bool RequiresUnderstood(MarkupReader reader)
    {
        if (!Ns.TryGetAttribute(reader, "Requires"u8, out ReadOnlySpan<byte> requires))
        {
            return false;
        }

        bool any = false;
        int i = 0;
        while (i < requires.Length)
        {
            while (i < requires.Length && requires[i] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r')
            {
                i++;
            }

            int start = i;
            while (i < requires.Length && requires[i] is not ((byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r'))
            {
                i++;
            }

            if (i == start)
            {
                break;
            }

            any = true;
            if (!reader.TryResolvePrefix(requires[start..i], out ReadOnlySpan<byte> uri) || !IsUnderstood(uri))
            {
                return false;
            }
        }

        return any;
    }

    private static bool IsUnderstood(ReadOnlySpan<byte> uri) =>
        uri.StartsWith("http://schemas.microsoft.com/office/"u8)
        || uri.StartsWith("http://schemas.openxmlformats.org/"u8)
        || uri.StartsWith("http://purl.oclc.org/ooxml/"u8)
        || uri.SequenceEqual("urn:schemas-microsoft-com:vml"u8);
}
