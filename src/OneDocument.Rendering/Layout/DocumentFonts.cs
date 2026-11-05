using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>What the document's font table says about one font: the hints used to pick a stand-in when the font is missing.</summary>
/// <param name="Name">The font name.</param>
/// <param name="FamilyClass">The <c>w:family</c> value, or null.</param>
/// <param name="Panose">The <c>w:panose1</c> value, or null.</param>
/// <param name="Charset">The <c>w:charset</c> value as a number, or null.</param>
public sealed record DocumentFontInfo(string Name, string? FamilyClass, string? Panose, int? Charset);

/// <summary>
/// Reads the fonts embedded in a document (<c>w:embedRegular</c>, <c>w:embedBold</c>, <c>w:embedItalic</c>, <c>w:embedBoldItalic</c> in the
/// font table) and the font table's hints.
/// </summary>
public static class DocumentFonts
{
    /// <summary>
    /// Undoes the obfuscation of an embedded font part in place. ECMA-376 Part 1 §17.8.1: the first 32 bytes are XORed with the 16 bytes of
    /// the font key GUID taken from the end of its string form, twice over.
    /// </summary>
    /// <param name="data">The obfuscated font bytes; the first 32 bytes are changed. Shorter data is left alone.</param>
    /// <param name="fontKey">The <c>w:fontKey</c> value, a GUID such as <c>{1B2C3D4E-5F60-7182-93A4-B5C6D7E8F901}</c>.</param>
    /// <returns>True when the key was valid and the data was changed.</returns>
    public static bool Deobfuscate(Span<byte> data, string fontKey)
    {
        ArgumentNullException.ThrowIfNull(fontKey);
        byte[] key = new byte[16];
        int hexIndex = 0;
        var hex = new char[32];
        foreach (char c in fontKey)
        {
            if (Uri.IsHexDigit(c))
            {
                if (hexIndex == 32)
                {
                    return false;
                }

                hex[hexIndex++] = c;
            }
            else if (c is not ('{' or '}' or '-'))
            {
                return false;
            }
        }

        if (hexIndex != 32 || data.Length < 32)
        {
            return false;
        }

        for (int i = 0; i < 16; i++)
        {
            // Key byte i is the pair counted from the end of the string.
            key[i] = Convert.ToByte(new string(hex, 30 - (2 * i), 2), 16);
        }

        for (int i = 0; i < 32; i++)
        {
            data[i] ^= key[i % 16];
        }

        return true;
    }

    /// <summary>Reads the font table's entries.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The entries by name, compared without regard to case; empty when the document has no font table.</returns>
    public static IReadOnlyDictionary<string, DocumentFontInfo> ReadFontTable(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new Dictionary<string, DocumentFontInfo>(StringComparer.OrdinalIgnoreCase);
        (MarkupDocument? table, _) = Xml.LoadRelated(document, "fontTable");
        if (table is null)
        {
            return result;
        }

        foreach (MarkupRef font in Xml.Elements(new MarkupRef(table.Root)))
        {
            if (Xml.Attr(font, "name") is not { } name)
            {
                continue;
            }

            int? charset = null;
            if (Xml.Child(font, "charset") is { } cs && Xml.Attr(cs, "val") is { } csv && int.TryParse(csv, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int cv))
            {
                charset = cv;
            }

            result[name] = new DocumentFontInfo(
                name,
                Xml.Child(font, "family") is { } fam ? Xml.Attr(fam, "val") : null,
                Xml.Child(font, "panose1") is { } pan ? Xml.Attr(pan, "val") : null,
                charset);
        }

        return result;
    }

    /// <summary>
    /// Adds the fonts embedded in a document to a collection, so the document's own fonts win over substitutes. Damaged or unreadable
    /// embedded parts are skipped.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="collection">The collection to add to.</param>
    /// <returns>The number of faces added.</returns>
    public static int AddEmbeddedFonts(Document document, FontLibrary collection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(collection);
        (MarkupDocument? table, string? tablePart) = Xml.LoadRelated(document, "fontTable");
        if (table is null || tablePart is null)
        {
            return 0;
        }

        var relationships = document.Package.GetRelationships(tablePart).ToDictionary(static r => r.Id, StringComparer.Ordinal);
        int added = 0;
        foreach (MarkupRef font in Xml.Elements(new MarkupRef(table.Root)))
        {
            foreach (MarkupRef embed in Xml.Elements(font))
            {
                if (!Xml.Eq(embed.LocalName, "embedRegular") && !Xml.Eq(embed.LocalName, "embedBold") && !Xml.Eq(embed.LocalName, "embedItalic") && !Xml.Eq(embed.LocalName, "embedBoldItalic"))
                {
                    continue;
                }

                string? id = Xml.AnyAttr(embed, "id");
                if (id is null || !relationships.TryGetValue(id, out Relationship? rel))
                {
                    continue;
                }

                if (!OpcPackage.TryResolveTarget(tablePart, rel, out PartUri target))
                {
                    continue;
                }

                IPackagePart? part = document.Package.FindPart(target.Value);
                if (part is null)
                {
                    continue;
                }

                byte[] bytes = part.GetBytes().ToArray();
                if (Xml.Attr(embed, "fontKey") is { } key)
                {
                    Deobfuscate(bytes, key);
                }

                added += collection.AddFont(bytes);
            }
        }

        return added;
    }
}
