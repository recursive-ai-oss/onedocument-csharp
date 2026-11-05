using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Builds small DOCX files in memory for the layout tests. Defaults are chosen so expected values come straight from font metrics:
// Liberation Sans 12 pt, no space after, single line spacing, Letter page with one-inch margins.
internal static class TestDocx
{
    internal const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    internal const string DefaultStyles =
        "<w:styles xmlns:w=\"" + W + "\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Liberation Sans\" w:hAnsi=\"Liberation Sans\" w:eastAsia=\"Noto Sans CJK SC\" w:cs=\"Noto Sans Arabic\"/><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/><w:lang w:val=\"en-US\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"120\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:rPr><w:color w:val=\"0563C1\"/><w:u w:val=\"single\"/></w:rPr></w:style>" +
        "</w:styles>";

    internal const string LetterSection = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";

    internal static byte[] Bytes(string body, string? styles = null, string? numbering = null, string? settings = null, string? sectPr = null, string? extraRels = null, IEnumerable<(string Name, byte[] Data)>? extraParts = null, string? fontTable = null, string? extraTypes = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"odttf\" ContentType=\"application/vnd.openxmlformats-officedocument.obfuscatedFont\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                (numbering is null ? string.Empty : "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>") +
                (settings is null ? string.Empty : "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>") +
                (fontTable is null ? string.Empty : "<Override PartName=\"/word/fontTable.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml\"/>") +
                extraTypes + "</Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/_rels/document.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rIdS\" Type=\"" + R + "/styles\" Target=\"styles.xml\"/>" +
                (numbering is null ? string.Empty : "<Relationship Id=\"rIdN\" Type=\"" + R + "/numbering\" Target=\"numbering.xml\"/>") +
                (settings is null ? string.Empty : "<Relationship Id=\"rIdT\" Type=\"" + R + "/settings\" Target=\"settings.xml\"/>") +
                (fontTable is null ? string.Empty : "<Relationship Id=\"rIdF\" Type=\"" + R + "/fontTable\" Target=\"fontTable.xml\"/>") +
                extraRels + "</Relationships>");
            Add(zip, "word/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + (styles ?? DefaultStyles));
            if (numbering is not null)
            {
                Add(zip, "word/numbering.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + numbering);
            }

            if (settings is not null)
            {
                Add(zip, "word/settings.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + settings);
            }

            if (fontTable is not null)
            {
                Add(zip, "word/fontTable.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + fontTable);
            }

            Add(zip, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\"><w:body>" + body + (sectPr ?? LetterSection) + "</w:body></w:document>");
            foreach ((string name, byte[] data) in extraParts ?? [])
            {
                ZipArchiveEntry e = zip.CreateEntry(name);
                using Stream s = e.Open();
                s.Write(data);
            }
        }

        return ms.ToArray();
    }

    internal static Document Open(string body, string? styles = null, string? numbering = null, string? settings = null, string? sectPr = null)
    {
        RenderingFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        return Document.Open(new MemoryStream(Bytes(body, styles, numbering, settings, sectPr)), leaveOpen: false);
    }

    internal static string P(string text, string? pPr = null, string? rPr = null) =>
        "<w:p>" + (pPr is null ? string.Empty : "<w:pPr>" + pPr + "</w:pPr>") + "<w:r>" + (rPr is null ? string.Empty : "<w:rPr>" + rPr + "</w:rPr>") + "<w:t xml:space=\"preserve\">" + System.Security.SecurityElement.Escape(text) + "</w:t></w:r></w:p>";

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name);
        using Stream s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
