using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>A word-processing document that uses most of what the OpenDocument conversion maps, built in memory.</summary>
internal static class OdfSamples
{
    private const string W = TestDocs.WNs;
    private const string R = TestDocs.RNs;
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Ct = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    // A 1x1 PNG.
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static string Hdr(string root, string inner) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:{root} xmlns:w=\"{W}\" xmlns:r=\"{R}\">{inner}</w:{root}>";

    public const string Styles =
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Liberation Serif\" w:hAnsi=\"Liberation Serif\"/><w:sz w:val=\"24\"/><w:lang w:val=\"en-US\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"120\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
        "<w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"60\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Liberation Sans\" w:hAnsi=\"Liberation Sans\"/><w:b/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Heading1\"/><w:qFormat/><w:pPr><w:outlineLvl w:val=\"1\"/></w:pPr><w:rPr><w:sz w:val=\"28\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Quote2\"><w:name w:val=\"My Quote\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"720\" w:right=\"720\"/><w:jc w:val=\"center\"/></w:pPr><w:rPr><w:i/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:rPr><w:color w:val=\"0000FF\"/><w:u w:val=\"single\"/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Strong\"><w:name w:val=\"Strong\"/><w:rPr><w:b/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"FootnoteReference\"><w:name w:val=\"footnote reference\"/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"FootnoteText\"><w:name w:val=\"footnote text\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:sz w:val=\"20\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Header\"><w:name w:val=\"header\"/><w:basedOn w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Footer\"><w:name w:val=\"footer\"/><w:basedOn w:val=\"Normal\"/></w:style>";

    public const string Numbering =
        "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>" +
        "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerLetter\"/><w:lvlText w:val=\"%2)\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"1440\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>" +
        "<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"multilevel\"/>" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"\u2022\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>" +
        "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>" +
        "<w:num w:numId=\"3\"><w:abstractNumId w:val=\"0\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"5\"/></w:lvlOverride></w:num>";

    private static string P(string text, string? style = null, string? ppr = null, string? rpr = null) =>
        "<w:p>" + (style is null && ppr is null ? "" : "<w:pPr>" + (style is null ? "" : $"<w:pStyle w:val=\"{style}\"/>") + ppr + "</w:pPr>") +
        $"<w:r>{(rpr is null ? "" : "<w:rPr>" + rpr + "</w:rPr>")}<w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static string Li(string text, int numId, int level) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    public static string Body =>
        P("Quarterly report", "Heading1") +
        "<w:p><w:r><w:t xml:space=\"preserve\">Plain, </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r><w:r><w:t xml:space=\"preserve\">, </w:t></w:r>" +
        "<w:r><w:rPr><w:i/><w:color w:val=\"FF0000\"/><w:sz w:val=\"28\"/></w:rPr><w:t>italic red large</w:t></w:r><w:r><w:t xml:space=\"preserve\">, </w:t></w:r>" +
        "<w:r><w:rPr><w:u w:val=\"single\"/><w:strike/></w:rPr><w:t>underlined struck</w:t></w:r><w:r><w:t xml:space=\"preserve\"> and a </w:t></w:r>" +
        $"<w:hyperlink r:id=\"rIdLink\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/></w:rPr><w:t>link</w:t></w:r></w:hyperlink><w:r><w:t xml:space=\"preserve\"> with a </w:t></w:r>" +
        "<w:bookmarkStart w:id=\"7\" w:name=\"mark1\"/><w:r><w:t>bookmark</w:t></w:r><w:bookmarkEnd w:id=\"7\"/>" +
        "<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r>" +
        "<w:commentRangeStart w:id=\"0\"/><w:r><w:t xml:space=\"preserve\"> and a comment</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>" +
        P("Second  heading", "Heading2") +
        P("Centered quote  with  spaces", "Quote2") +
        Li("First", 1, 0) + Li("Nested a", 1, 1) + Li("Nested b", 1, 1) + Li("Second", 1, 0) +
        P("A bullet list follows") +
        Li("Apple", 2, 0) + Li("Pear", 2, 0) +
        P("A list restarting at five") +
        Li("Five", 3, 0) + Li("Six", 3, 0) +
        "<w:tbl><w:tblPr><w:tblW w:w=\"9000\" w:type=\"dxa\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/>" +
        "<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/></w:tblBorders></w:tblPr>" +
        "<w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid>" +
        "<w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc><w:tcPr><w:tcW w:w=\"6000\" w:type=\"dxa\"/><w:gridSpan w:val=\"2\"/><w:shd w:val=\"clear\" w:fill=\"DDDDDD\"/></w:tcPr><w:p><w:r><w:t>Spanning header</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>H3</w:t></w:r></w:p></w:tc></w:tr>" +
        "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/><w:vMerge w:val=\"restart\"/></w:tcPr><w:p><w:r><w:t>Tall</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>B2</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr><w:p><w:r><w:t>C2</w:t></w:r></w:p></w:tc></w:tr>" +
        "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/><w:vMerge/></w:tcPr><w:p/></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>B3</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>C3</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
        P("After the table") +
        "<w:p><w:r><w:t xml:space=\"preserve\">Picture: </w:t></w:r><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
        "<wp:extent cx=\"914400\" cy=\"457200\"/><wp:docPr id=\"1\" name=\"Picture 1\" descr=\"A dot\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"1\" name=\"dot.png\"/><pic:cNvPicPr/></pic:nvPicPr>" +
        "<pic:blipFill><a:blip r:embed=\"rIdImg\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"457200\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>" +
        "<w:p><w:r><w:t xml:space=\"preserve\">Tracked: kept </w:t></w:r><w:ins w:id=\"21\" w:author=\"Ann\" w:date=\"2024-01-02T03:04:05Z\"><w:r><w:t>inserted</w:t></w:r></w:ins><w:del w:id=\"22\" w:author=\"Bob\" w:date=\"2024-01-03T03:04:05Z\"><w:r><w:delText>deleted</w:delText></w:r></w:del><w:r><w:t xml:space=\"preserve\"> end</w:t></w:r></w:p>" +
        "<w:p><w:r><w:t>Before the break</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>After the page break</w:t></w:r></w:p>" +
        "<w:p><w:pPr><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdHdr\"/><w:footerReference w:type=\"default\" r:id=\"rIdFtr\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1700\" w:right=\"1134\" w:bottom=\"1700\" w:left=\"1134\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:pPr><w:r><w:t>End of section one</w:t></w:r></w:p>" +
        P("Section two is landscape") +
        "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdHdr\"/><w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/><w:cols w:num=\"2\" w:space=\"720\"/></w:sectPr>";

    public static OpcPackage Rich()
    {
        OpcPackage p = OpcPackage.Create(new OfficeLimits());
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml"), new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")]);
        p.AddPart("/word/document.xml", Ct + "document.main+xml", Encoding.UTF8.GetBytes(
            $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:body>{Body}</w:body></w:document>"));
        p.AddPart("/word/styles.xml", Ct + "styles+xml", Encoding.UTF8.GetBytes(Hdr("styles", Styles)));
        p.AddPart("/word/numbering.xml", Ct + "numbering+xml", Encoding.UTF8.GetBytes(Hdr("numbering", Numbering)));
        p.AddPart("/word/footnotes.xml", Ct + "footnotes+xml", Encoding.UTF8.GetBytes(Hdr("footnotes",
            "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>" +
            "<w:footnote w:id=\"1\"><w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> The note text.</w:t></w:r></w:p></w:footnote>")));
        p.AddPart("/word/comments.xml", Ct + "comments+xml", Encoding.UTF8.GetBytes(Hdr("comments",
            "<w:comment w:id=\"0\" w:author=\"Reviewer\" w:date=\"2024-02-03T04:05:06Z\" w:initials=\"RV\"><w:p><w:r><w:t>Check this.</w:t></w:r></w:p></w:comment>")));
        p.AddPart("/word/header1.xml", Ct + "header+xml", Encoding.UTF8.GetBytes(Hdr("hdr", "<w:p><w:pPr><w:pStyle w:val=\"Header\"/></w:pPr><w:r><w:t>Report header</w:t></w:r></w:p>")));
        p.AddPart("/word/footer1.xml", Ct + "footer+xml", Encoding.UTF8.GetBytes(Hdr("ftr",
            "<w:p><w:pPr><w:pStyle w:val=\"Footer\"/><w:jc w:val=\"center\"/></w:pPr><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r><w:fldSimple w:instr=\" PAGE \"><w:r><w:t>1</w:t></w:r></w:fldSimple></w:p>")));
        p.AddPart("/word/media/image1.png", "image/png", Png);
        p.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            "<dc:title>Report</dc:title><dc:creator>Writer</dc:creator><cp:keywords>a, b</cp:keywords><dcterms:created xsi:type=\"dcterms:W3CDTF\">2024-01-01T00:00:00Z</dcterms:created></cp:coreProperties>"));
        p.SetRelationships("/word/document.xml",
        [
            new Relationship("rIdStyles", Rel + "styles", "styles.xml"),
            new Relationship("rIdNum", Rel + "numbering", "numbering.xml"),
            new Relationship("rIdFn", Rel + "footnotes", "footnotes.xml"),
            new Relationship("rIdCm", Rel + "comments", "comments.xml"),
            new Relationship("rIdHdr", Rel + "header", "header1.xml"),
            new Relationship("rIdFtr", Rel + "footer", "footer1.xml"),
            new Relationship("rIdImg", Rel + "image", "media/image1.png"),
            new Relationship("rIdLink", Rel + "hyperlink", "https://example.org/page", RelationshipTargetMode.External),
        ]);
        return p;
    }

    public static Document RichDocument()
    {
        using Document built = Document.FromPackage(Rich(), new OpenContext(), ownsPackage: true);
        using var ms = new MemoryStream();
        built.SaveAs(ms, OfficeFormat.Docx);
        return Document.Open(new MemoryStream(ms.ToArray()));
    }
}
