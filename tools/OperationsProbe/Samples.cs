using System;
using System.IO;
using System.IO.Compression;
using System.Text;

// Two small documents that conflict on purpose, for comparing a merge with what the word processor does for Insert > Text from File.
internal static class Samples
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Ct = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    public static void Write(string dir)
    {
        Directory.CreateDirectory(dir);
        Save(Path.Combine(dir, "merge-a.docx"), "Ann", "28", "b", "First document");
        Save(Path.Combine(dir, "merge-b.docx"), "Bob", "40", "i", "Second document");
    }

    private static void Save(string path, string author, string size, string emphasis, string title)
    {
        string body =
            $"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:bookmarkStart w:id=\"0\" w:name=\"intro\"/><w:r><w:t>{title}</w:t></w:r><w:bookmarkEnd w:id=\"0\"/></w:p>" +
            "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>First item</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>Second item</w:t></w:r><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>" +
            $"<w:p><w:pPr><w:pStyle w:val=\"Quote\"/></w:pPr><w:commentRangeStart w:id=\"0\"/><w:r><w:t>A quotation</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>" +
            $"<w:p><w:ins w:id=\"5\" w:author=\"{author}\" w:date=\"2024-01-02T03:04:05Z\"><w:r><w:t>Inserted by {author}</w:t></w:r></w:ins></w:p>" +
            "<w:p><w:r><w:t xml:space=\"preserve\">See </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> REF intro \\h </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>heading</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>" +
            "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";
        string styles =
            $"<w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            $"<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:{emphasis}/><w:sz w:val=\"{size}\"/></w:rPr></w:style>" +
            $"<w:style w:type=\"paragraph\" w:styleId=\"Quote\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:{emphasis}/></w:rPr></w:style></w:styles>";
        string numbering =
            $"<w:numbering xmlns:w=\"{W}\"><w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"singleLevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>";
        string footnotes =
            $"<w:footnotes xmlns:w=\"{W}\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>" +
            $"<w:footnote w:id=\"1\"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> Note of {title}.</w:t></w:r></w:p></w:footnote></w:footnotes>";
        string comments =
            $"<w:comments xmlns:w=\"{W}\"><w:comment w:id=\"0\" w:author=\"{author}\" w:initials=\"{author[0]}\"><w:p><w:r><w:t>Comment by {author}</w:t></w:r></w:p></w:comment></w:comments>";

        using FileStream fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml",
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            $"<Override PartName=\"/word/document.xml\" ContentType=\"{Ct}document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"{Ct}styles+xml\"/><Override PartName=\"/word/numbering.xml\" ContentType=\"{Ct}numbering+xml\"/>" +
            $"<Override PartName=\"/word/footnotes.xml\" ContentType=\"{Ct}footnotes+xml\"/><Override PartName=\"/word/comments.xml\" ContentType=\"{Ct}comments+xml\"/></Types>");
        Add(zip, "_rels/.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{Rel}officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add(zip, "word/_rels/document.xml.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            $"<Relationship Id=\"rId1\" Type=\"{Rel}styles\" Target=\"styles.xml\"/><Relationship Id=\"rId2\" Type=\"{Rel}numbering\" Target=\"numbering.xml\"/>" +
            $"<Relationship Id=\"rId3\" Type=\"{Rel}footnotes\" Target=\"footnotes.xml\"/><Relationship Id=\"rId4\" Type=\"{Rel}comments\" Target=\"comments.xml\"/></Relationships>");
        Add(zip, "word/document.xml", $"<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:body>{body}</w:body></w:document>");
        Add(zip, "word/styles.xml", styles);
        Add(zip, "word/numbering.xml", numbering);
        Add(zip, "word/footnotes.xml", footnotes);
        Add(zip, "word/comments.xml", comments);
    }

    private static void Add(ZipArchive zip, string name, string xml)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream s = e.Open();
        s.Write(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + xml));
    }
}
