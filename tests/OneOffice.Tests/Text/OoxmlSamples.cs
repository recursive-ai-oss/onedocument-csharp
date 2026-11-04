using System.IO.Compression;
using System.Text;

namespace RecursiveAI.OneOffice.Tests.Text;

/// <summary>Builds small OOXML packages in memory for the text extraction tests.</summary>
internal static class OoxmlSamples
{
    public const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
    public const string WStrict = "xmlns:w=\"http://purl.oclc.org/ooxml/wordprocessingml/main\"";

    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public static byte[] Docx(string bodyXml, params (string Name, string? Xml, string RelType)[] related) =>
        DocxWith(W, bodyXml, related);

    public static byte[] DocxWith(string namespaces, string bodyXml, params (string Name, string? Xml, string RelType)[] related)
    {
        var parts = new List<(string, string)>
        {
            ("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document {namespaces} xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:x9=\"urn:example:unknown\"><w:body>{bodyXml}</w:body></w:document>"),
        };
        var rels = new StringBuilder();
        int n = 1;
        foreach ((string name, string? xml, string type) in related)
        {
            if (xml is not null)
            {
                parts.Add((name, xml));
            }

            rels.Append($"<Relationship Id=\"rId{n++}\" Type=\"{Rel}{type}\" Target=\"{name[(name.IndexOf('/') + 1)..]}\"/>");
        }

        parts.Add(("word/_rels/document.xml.rels", $"<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{rels}</Relationships>"));
        return Package("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", parts);
    }

    public static string Part(string root, string bodyXml, string ns = W) =>
        $"<?xml version=\"1.0\"?><w:{root} {ns}>{bodyXml}</w:{root}>";

    public static byte[] Package(string mainPart, string contentType, List<(string Name, string Xml)> parts)
    {
        var all = new List<(string, string)>
        {
            ("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                $"<Override PartName=\"/{mainPart}\" ContentType=\"{contentType}\"/></Types>"),
            ("_rels/.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                $"<Relationship Id=\"rId1\" Type=\"{Rel}officeDocument\" Target=\"{mainPart}\"/></Relationships>"),
        };
        all.AddRange(parts);
        return Zip(all);
    }

    public static byte[] Zip(List<(string Name, string Xml)> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string xml) in entries)
            {
                ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using Stream s = e.Open();
                s.Write(Encoding.UTF8.GetBytes(xml));
            }
        }

        return ms.ToArray();
    }

    public static byte[] Xlsx(string sharedStringsXml, params (string Name, string SheetXml)[] sheets)
    {
        var parts = new List<(string, string)>();
        var wbSheets = new StringBuilder();
        var rels = new StringBuilder();
        int n = 1;
        foreach ((string name, string xml) in sheets)
        {
            parts.Add(($"xl/worksheets/{name}.xml", xml));
            wbSheets.Append($"<sheet name=\"{name}\" sheetId=\"{n}\" r:id=\"rId{n}\"/>");
            rels.Append($"<Relationship Id=\"rId{n}\" Type=\"{Rel}worksheet\" Target=\"worksheets/{name}.xml\"/>");
            n++;
        }

        if (sharedStringsXml is not null)
        {
            parts.Add(("xl/sharedStrings.xml", sharedStringsXml));
            rels.Append($"<Relationship Id=\"rId{n}\" Type=\"{Rel}sharedStrings\" Target=\"sharedStrings.xml\"/>");
        }

        parts.Add(("xl/workbook.xml", "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            $"<sheets>{wbSheets}</sheets></workbook>"));
        parts.Add(("xl/_rels/workbook.xml.rels", $"<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{rels}</Relationships>"));
        return Package("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", parts);
    }

    public const string S = "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"";

    public static byte[] Pptx(params (string Name, string SlideXml, string? NotesXml)[] slides)
    {
        var parts = new List<(string, string)>();
        var ids = new StringBuilder();
        var rels = new StringBuilder();
        int n = 1;
        foreach ((string name, string xml, string? notes) in slides)
        {
            parts.Add(($"ppt/slides/{name}.xml", xml));
            ids.Append($"<p:sldId id=\"{255 + n}\" r:id=\"rId{n}\"/>");
            rels.Append($"<Relationship Id=\"rId{n}\" Type=\"{Rel}slide\" Target=\"slides/{name}.xml\"/>");
            if (notes is not null)
            {
                parts.Add(($"ppt/notesSlides/{name}.xml", notes));
                parts.Add(($"ppt/slides/_rels/{name}.xml.rels", $"<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{Rel}notesSlide\" Target=\"../notesSlides/{name}.xml\"/></Relationships>"));
            }

            n++;
        }

        parts.Add(("ppt/presentation.xml", "<?xml version=\"1.0\"?><p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            $"<p:sldIdLst>{ids}</p:sldIdLst></p:presentation>"));
        parts.Add(("ppt/_rels/presentation.xml.rels", $"<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{rels}</Relationships>"));
        return Package("ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml", parts);
    }

    public const string P = "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"";

    public static string Run(string text) => $"<w:r><w:t>{text}</w:t></w:r>";

    public static string Para(string text) => $"<w:p>{Run(text)}</w:p>";
}
