using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Xml2003;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class WordMl2003Tests
{
    static WordMl2003Tests() => DocumentFormats.Register(FormatRegistry.Default);

    private const string Header =
        "<?xml version=\"1.0\"?><?mso-application progid=\"Word.Document\"?>" +
        "<w:wordDocument xmlns:w=\"http://schemas.microsoft.com/office/word/2003/wordml\" xmlns:wx=\"http://schemas.microsoft.com/office/word/2003/auxHint\" " +
        "xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:aml=\"http://schemas.microsoft.com/aml/2001/core\" " +
        "xmlns:dt=\"uuid:C2F41010-65B3-11d1-A29F-00AA00C14882\">";

    private static string Doc(string inner) => Header + inner + "</w:wordDocument>";

    private static string Body(string blocks) => Doc("<w:body><wx:sect><wx:sub-section>" + blocks + "</wx:sub-section></wx:sect></w:body>");

    private static Document Open(string xml, OpenContext? context = null) =>
        Document.Open(new MemoryStream(Encoding.UTF8.GetBytes(xml)), context ?? new OpenContext(), leaveOpen: false);

    // The DOCX the document saves as, as a map of part name to text.
    private static Dictionary<string, string> Saved(Document doc)
    {
        using var ms = new MemoryStream();
        doc.SaveAs(ms, OfficeFormat.Docx);
        using var zip = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read);
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            parts[e.FullName] = e.FullName.EndsWith(".png", StringComparison.Ordinal) ? "<binary>" : reader.ReadToEnd();
        }

        return parts;
    }

    private static string[] Codes(OpenContext ctx) => [.. ((DefectCollection)ctx.Defects).Select(d => d.Code)];

    [Fact]
    public void ParagraphsRunsAndFormattingConvert()
    {
        using Document doc = Open(Body(
            "<w:p><w:pPr><w:jc w:val=\"center\"/><w:spacing w:before=\"120\" w:after=\"60\" w:line=\"300\" w:line-rule=\"exact\"/><w:ind w:left=\"720\" w:first-line=\"360\"/></w:pPr>" +
            "<w:r><w:rPr><w:rFonts w:ascii=\"Arial\" w:h-ansi=\"Arial\" w:fareast=\"MS Mincho\"/><w:b/><w:i-cs/><w:sz w:val=\"28\"/><w:color w:val=\"FF0000\"/></w:rPr><w:t>Hello </w:t></w:r>" +
            "<w:r><w:t>world</w:t></w:r></w:p>"));
        Assert.Equal(OfficeFormat.Docx, Format(doc));
        Assert.Equal("Hello world", doc.Paragraphs.Single().Text);
        string xml = Saved(doc)["word/document.xml"];
        Assert.Contains("<w:jc w:val=\"center\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:lineRule=\"exact\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:firstLine=\"360\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:hAnsi=\"Arial\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:eastAsia=\"MS Mincho\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:iCs/>", xml, StringComparison.Ordinal);
        // The schema order of paragraph properties is spacing, ind, jc whatever the source order was.
        Assert.True(xml.IndexOf("<w:spacing", StringComparison.Ordinal) < xml.IndexOf("<w:ind", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<w:ind", StringComparison.Ordinal) < xml.IndexOf("<w:jc", StringComparison.Ordinal));
    }

    private static OfficeFormat Format(Document doc) => doc.Format;

    [Fact]
    public void StylesFontsAndPropertiesConvert()
    {
        using Document doc = Open(Doc(
            "<o:DocumentProperties><o:Title>Quarterly</o:Title><o:Author>Ann</o:Author><o:Created>2005-01-04T10:11:00Z</o:Created><o:Company>Acme</o:Company><o:Pages>3</o:Pages></o:DocumentProperties>" +
            "<w:fonts><w:defaultFonts w:ascii=\"Times New Roman\" w:fareast=\"MS Mincho\" w:h-ansi=\"Times New Roman\" w:cs=\"Times New Roman\"/></w:fonts>" +
            "<w:styles><w:style w:type=\"paragraph\" w:default=\"on\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><wx:uiName wx:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><wx:uiName wx:val=\"Heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/>" +
            "<w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr><w:pPr><w:keepNext/><w:outlineLvl w:val=\"0\"/></w:pPr></w:style></w:styles>" +
            "<w:body><wx:sect><w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Title</w:t></w:r></w:p></wx:sect></w:body>"));
        Dictionary<string, string> parts = Saved(doc);
        Assert.Contains("w:styleId=\"Heading1\"", parts["word/styles.xml"], StringComparison.Ordinal);
        Assert.Contains("<w:basedOn w:val=\"Normal\"/>", parts["word/styles.xml"], StringComparison.Ordinal);
        Assert.Contains("w:ascii=\"Times New Roman\"", parts["word/styles.xml"], StringComparison.Ordinal);
        // pPr before rPr in a style, whatever the source order.
        Assert.True(parts["word/styles.xml"].IndexOf("<w:pPr>", StringComparison.Ordinal) < parts["word/styles.xml"].IndexOf("<w:rPr><w:b/>", StringComparison.Ordinal));
        Assert.Contains("<dc:title>Quarterly</dc:title>", parts["docProps/core.xml"], StringComparison.Ordinal);
        Assert.Contains("<dc:creator>Ann</dc:creator>", parts["docProps/core.xml"], StringComparison.Ordinal);
        Assert.Contains("<Company>Acme</Company>", parts["docProps/app.xml"], StringComparison.Ordinal);
        Assert.Contains("<Pages>3</Pages>", parts["docProps/app.xml"], StringComparison.Ordinal);
        Assert.Equal("Quarterly", doc.Properties.Core.Title);
    }

    [Fact]
    public void TablesMapMergesAndGrid()
    {
        using Document doc = Open(Body(
            "<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"dxa\"/><w:tblBorders><wx:borders><w:top w:val=\"single\" w:sz=\"4\"/></wx:borders></w:tblBorders></w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"2500\"/><w:gridCol w:w=\"1500\"/><w:gridCol w:w=\"1000\"/></w:tblGrid>" +
            "<w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc><w:tcPr><w:tcW w:w=\"2500\" w:type=\"dxa\"/><w:vmerge w:val=\"restart\"/></w:tcPr><w:p><w:r><w:t>a</w:t></w:r></w:p></w:tc>" +
            "<w:tc><w:tcPr><w:hmerge w:val=\"restart\"/></w:tcPr><w:p><w:r><w:t>b</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:hmerge w:val=\"continue\"/></w:tcPr><w:p/></w:tc></w:tr>" +
            "<w:tr><w:tc><w:tcPr><w:vmerge/></w:tcPr><w:p/></w:tc><w:tc><w:p><w:r><w:t>c</w:t></w:r></w:p></w:tc><w:tc/></w:tr></w:tbl>"));
        Table table = doc.Tables.Single();
        Assert.NotNull(table);
        string xml = Saved(doc)["word/document.xml"];
        Assert.Contains("<w:gridSpan w:val=\"2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tblHeader/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/></w:tblBorders>", xml, StringComparison.Ordinal);
        // The empty last cell was given the paragraph a cell must end with.
        Assert.Contains("<w:tc><w:p/></w:tc>", xml, StringComparison.Ordinal);
        // The merged-away continuation cell is gone: the first row has two cells.
        Assert.Equal(2, CountOccurrences(xml.Split("</w:tr>")[0], "<w:tc>"));
    }

    private static int CountOccurrences(string text, string value)
    {
        int n = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Fact]
    public void ListsBecomeNumbering()
    {
        using Document doc = Open(Doc(
            "<w:lists><w:listDef w:listDefId=\"0\"><w:lsid w:val=\"1A2B3C4D\"/><w:plt w:val=\"HybridMultilevel\"/>" +
            "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:nfc w:val=\"4\"/><w:lvlText w:val=\"%1)\"/><w:lvlJc w:val=\"left\"/><w:suff w:val=\"Tab\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>" +
            "<w:lvl w:ilvl=\"1\"><w:nfc w:val=\"23\"/><w:lvlText w:val=\"o\"/></w:lvl></w:listDef>" +
            "<w:list w:ilfo=\"1\"><w:ilst w:val=\"0\"/></w:list><w:list w:ilfo=\"2\"><w:ilst w:val=\"0\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"5\"/></w:lvlOverride></w:list></w:lists>" +
            "<w:body><w:p><w:pPr><w:listPr><w:ilvl w:val=\"0\"/><w:ilfo w:val=\"1\"/><wx:t wx:val=\"a)\"/></w:listPr></w:pPr><w:r><w:t>item</w:t></w:r></w:p></w:body>"));
        Dictionary<string, string> parts = Saved(doc);
        string numbering = parts["word/numbering.xml"];
        Assert.Contains("<w:abstractNum w:abstractNumId=\"0\">", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:nsid w:val=\"1A2B3C4D\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:multiLevelType w:val=\"hybridMultilevel\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:numFmt w:val=\"lowerLetter\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:numFmt w:val=\"bullet\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:suff w:val=\"tab\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:num w:numId=\"2\"><w:abstractNumId w:val=\"0\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"5\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.NotNull(doc.Paragraphs.Single().Numbering);
    }

    [Fact]
    public void HyperlinksBookmarksAndFields()
    {
        using Document doc = Open(Body(
            "<w:p><aml:annotation aml:id=\"7\" w:type=\"Word.Bookmark.Start\" w:name=\"here\"/><w:hlink w:dest=\"https://example.com/a\" w:screenTip=\"tip\"><w:r><w:t>site</w:t></w:r></w:hlink>" +
            "<w:hlink w:bookmark=\"here\"><w:r><w:t>local</w:t></w:r></w:hlink><aml:annotation aml:id=\"7\" w:type=\"Word.Bookmark.End\"/>" +
            "<w:fldSimple w:instr=\" PAGE \"><w:r><w:t>1</w:t></w:r></w:fldSimple>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> DATE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>today</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>"));
        Dictionary<string, string> parts = Saved(doc);
        string xml = parts["word/document.xml"];
        Assert.Contains("<w:bookmarkStart w:id=\"7\" w:name=\"here\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:bookmarkEnd w:id=\"7\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:anchor=\"here\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:tooltip=\"tip\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:fldSimple w:instr=\" PAGE \">", xml, StringComparison.Ordinal);
        Assert.Contains("w:fldCharType=\"separate\"", xml, StringComparison.Ordinal);
        Assert.Contains("TargetMode=\"External\"", parts["word/_rels/document.xml.rels"], StringComparison.Ordinal);
        Assert.Contains("https://example.com/a", parts["word/_rels/document.xml.rels"], StringComparison.Ordinal);
        Assert.Equal("sitelocal1today", doc.Paragraphs.Single().Text);
    }

    [Fact]
    public void NotesAndCommentsConvert()
    {
        using Document doc = Open(Body(
            "<w:p><w:r><w:t>Text</w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnote><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t> The note.</w:t></w:r></w:p></w:footnote></w:r>" +
            "<w:r><w:endnote><w:p><w:r><w:endnoteRef/></w:r><w:r><w:t>The end.</w:t></w:r></w:p></w:endnote></w:r>" +
            "<aml:annotation aml:id=\"3\" w:type=\"Word.Comment.Start\"/><w:r><w:t> commented</w:t></w:r><aml:annotation aml:id=\"3\" w:type=\"Word.Comment.End\"/>" +
            "<aml:annotation aml:id=\"3\" aml:author=\"Rae\" aml:createdate=\"2005-02-03T04:05:06Z\" w:initials=\"R\" w:type=\"Word.Comment\"><aml:content><w:p><w:r><w:annotationRef/></w:r><w:r><w:t>Check this</w:t></w:r></w:p></aml:content></aml:annotation></w:p>"));
        Dictionary<string, string> parts = Saved(doc);
        Assert.Contains("<w:footnoteReference w:id=\"1\"/>", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.Contains("<w:endnoteReference w:id=\"1\"/>", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.Contains("The note.", parts["word/footnotes.xml"], StringComparison.Ordinal);
        Assert.Contains("The end.", parts["word/endnotes.xml"], StringComparison.Ordinal);
        Assert.Contains("<w:commentRangeStart w:id=\"0\"/>", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.Contains("<w:commentReference w:id=\"0\"/>", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.Contains("w:author=\"Rae\"", parts["word/comments.xml"], StringComparison.Ordinal);
        Assert.Contains("Check this", parts["word/comments.xml"], StringComparison.Ordinal);
        Assert.Single(doc.Comments);
        Assert.Equal("Rae", doc.Comments[0].Author);
        Assert.Contains(doc.Footnotes, n => n.Text.Contains("The note.", StringComparison.Ordinal));
    }

    [Fact]
    public void RevisionsBecomeInsertionsAndDeletions()
    {
        using Document doc = Open(Body(
            "<w:p><w:r><w:t>kept </w:t></w:r><aml:annotation aml:id=\"10\" aml:author=\"Ed\" aml:createdate=\"2005-03-04T05:06:07Z\" w:type=\"Word.Insertion\"><aml:content><w:r><w:t>added</w:t></w:r></aml:content></aml:annotation>" +
            "<aml:annotation aml:id=\"11\" aml:author=\"Ed\" aml:createdate=\"2005-03-04T05:07:07Z\" w:type=\"Word.Deletion\"><aml:content><w:r><w:t>gone</w:t></w:r></aml:content></aml:annotation></w:p>"));
        string xml = Saved(doc)["word/document.xml"];
        Assert.Contains("<w:ins w:id=\"1\" w:author=\"Ed\" w:date=\"2005-03-04T05:06:07Z\">", xml, StringComparison.Ordinal);
        Assert.Contains("<w:del w:id=\"2\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:delText xml:space=\"preserve\">gone</w:delText>", xml, StringComparison.Ordinal);
        Assert.Equal(2, doc.Revisions.Revisions.Count);
        Assert.Equal("kept added", doc.Paragraphs.Single().Text);
    }

    [Fact]
    public void SectionsGetHeadersFootersAndBreaks()
    {
        using Document doc = Open(Doc(
            "<w:body><wx:sect><wx:sub-section><w:p><w:r><w:t>one</w:t></w:r></w:p>" +
            "<w:sectPr><w:type w:val=\"next-page\"/><w:pgSz w:w=\"12240\" w:h=\"15840\" w:orient=\"portrait\"/><w:pgMar w:top=\"1440\" w:left=\"1800\" w:bottom=\"1440\" w:right=\"1800\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/><w:cols w:space=\"720\"/>" +
            "<w:hdr w:type=\"odd\"><w:p><w:r><w:t>Head</w:t></w:r></w:p></w:hdr><w:ftr w:type=\"even\"><w:p><w:r><w:t>Foot</w:t></w:r></w:p></w:ftr></w:sectPr></wx:sub-section></wx:sect>" +
            "<wx:sect><wx:sub-section><w:p><w:r><w:t>two</w:t></w:r></w:p><w:sectPr><w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:titlePg/></w:sectPr></wx:sub-section></wx:sect></w:body>"));
        Dictionary<string, string> parts = Saved(doc);
        string xml = parts["word/document.xml"];
        // The first section's properties ride on its last paragraph; the last section's close the body.
        Assert.Contains("<w:p><w:pPr><w:sectPr>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:titlePg/></w:sectPr></w:body>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:headerReference w:type=\"default\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:footerReference w:type=\"even\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:evenAndOddHeaders/>", parts["word/settings.xml"], StringComparison.Ordinal);
        Assert.Contains("Head", parts["word/header1.xml"], StringComparison.Ordinal);
        Assert.Contains("Foot", parts["word/footer1.xml"], StringComparison.Ordinal);
        Assert.Equal(2, doc.Sections.Count);
    }

    // 1x1 PNG.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public void PicturesAndTextBoxesConvert()
    {
        using Document doc = Open(Body(
            "<w:p><w:r><w:pict><w:binData w:name=\"wordml://03000001.png\">" + Png + "</w:binData><v:shape id=\"_x0000_i1025\" style=\"width:72pt;height:36pt\" alt=\"A pixel\"><v:imagedata src=\"wordml://03000001.png\" o:title=\"pix\"/></v:shape></w:pict></w:r></w:p>" +
            "<w:p><w:r><w:pict><v:shape id=\"b1\" style=\"position:absolute;width:100pt;height:50pt\"><v:textbox><w:txbxContent><w:p><w:r><w:t>In a box</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></w:r></w:p>"));
        Dictionary<string, string> parts = Saved(doc);
        string xml = parts["word/document.xml"];
        Assert.Contains("<wp:extent cx=\"914400\" cy=\"457200\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("descr=\"A pixel\"", xml, StringComparison.Ordinal);
        Assert.Contains("word/media/image1.png", parts.Keys);
        Assert.Contains("media/image1.png", parts["word/_rels/document.xml.rels"], StringComparison.Ordinal);
        Assert.Contains("In a box", xml, StringComparison.Ordinal);
        Assert.Contains("<w:txbxContent>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void LossIsReportedAndRepairsAreDefects()
    {
        var ctx = new OpenContext();
        using Document doc = Open(Body(
            "<w:p><w:smartTag w:uri=\"x\" w:element=\"y\"><w:r><w:t>tagged</w:t></w:r></w:smartTag>" +
            "<w:r><w:pict><v:shape><v:imagedata src=\"wordml://missing.png\"/></v:shape></w:pict></w:r>" +
            "<w:r><w:pict><v:oval id=\"o1\"/></w:pict></w:r>" +
            "<w:pPr/></w:p>"), ctx);
        Assert.NotNull(doc.ImportLoss);
        Assert.True(doc.ImportLoss.CountOf("loss.smart-tags") >= 1);
        Assert.True(doc.ImportLoss.CountOf("loss.shapes") >= 1);
        Assert.Contains("wml2003.missing-picture", Codes(ctx));
    }

    [Fact]
    public void BadBase64AndFractionsAndDuplicatesAreRepaired()
    {
        var ctx = new OpenContext();
        using Document doc = Open(Body(
            "<w:p><w:pPr><w:ind w:left=\"720.5\"/><w:ind w:left=\"360\"/></w:pPr><w:r><w:pict><w:binData w:name=\"wordml://x.png\">***not base64***</w:binData><v:shape><v:imagedata src=\"wordml://x.png\"/></v:shape></w:pict></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>"), ctx);
        string[] codes = Codes(ctx);
        Assert.Contains("wml2003.bad-base64", codes);
        Assert.Contains("wml2003.duplicate-property", codes);
        Assert.Contains("wml2003.table-grid", codes);
        string xml = Saved(doc)["word/document.xml"];
        Assert.Contains("<w:ind w:left=\"360\"/>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("720.5", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void FractionalMeasuresAreRounded()
    {
        var ctx = new OpenContext();
        using Document doc = Open(Body("<w:p><w:pPr><w:spacing w:before=\"239.79\"/></w:pPr></w:p>"), ctx);
        Assert.Contains("<w:spacing w:before=\"240\"/>", Saved(doc)["word/document.xml"], StringComparison.Ordinal);
        Assert.Contains("wml2003.bad-value", Codes(ctx));
    }

    [Fact]
    public void ListNumberFormatWithoutNameIsRepaired()
    {
        var ctx = new OpenContext();
        using Document doc = Open(Doc("<w:lists><w:listDef w:listDefId=\"0\"><w:lvl w:ilvl=\"0\"><w:nfc w:val=\"99\"/></w:lvl></w:listDef></w:lists><w:body/>"), ctx);
        Assert.Contains("wml2003.number-format", Codes(ctx));
        Assert.Contains("<w:numFmt w:val=\"decimal\"/>", Saved(doc)["word/numbering.xml"], StringComparison.Ordinal);
    }

    [Fact]
    public void StyleWithoutIdGetsOne()
    {
        var ctx = new OpenContext();
        using Document doc = Open(Doc("<w:styles><w:style w:type=\"paragraph\"><w:name w:val=\"My Style\"/></w:style></w:styles><w:body/>"), ctx);
        Assert.Contains("wml2003.style-id", Codes(ctx));
        Assert.Contains("w:styleId=\"My_Style\"", Saved(doc)["word/styles.xml"], StringComparison.Ordinal);
    }

    [Fact]
    public void DatesAndCountsInPropertiesAreChecked()
    {
        var ctx = new OpenContext();
        using Document doc = Open(Doc("<o:DocumentProperties><o:Created>yesterday</o:Created><o:Pages>many</o:Pages></o:DocumentProperties><w:body/>"), ctx);
        Assert.Equal(2, Codes(ctx).Count(c => c == "wml2003.bad-value"));
    }

    [Fact]
    public void NotWordMlIsATypedError()
    {
        var ex = Assert.Throws<OfficeFormatException>(() => WordMl2003.ToPackage(ByteSource.FromMemory(Encoding.UTF8.GetBytes("<root xmlns=\"urn:x\"/>"))));
        Assert.Equal("wml2003.not-wordml", ex.Defect.Code);
        Assert.Throws<OfficeFormatException>(() => WordMl2003.ToPackage(ByteSource.FromMemory(Encoding.UTF8.GetBytes(Header + "<w:body>"))));
        Assert.Throws<OfficeFormatException>(() => WordMl2003.ToPackage(ByteSource.FromMemory(Encoding.UTF8.GetBytes("<!DOCTYPE w [<!ENTITY a \"b\">]>" + Doc("<w:body/>")))));
    }

    [Fact]
    public void SniffedAndOpenedThroughTheRegistry()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Body("<w:p><w:r><w:t>Through the registry</w:t></w:r></w:p>"));
        Assert.Equal(OfficeFormat.Word2003Xml, FormatSniffer.Detect(ByteSource.FromMemory(bytes), OfficeLimits.Default));
        DocumentFormats.Register(FormatRegistry.Default);
        using OfficeFile file = Office.Open(ByteSource.FromMemory(bytes));
        Assert.IsType<Document>(file);
        Assert.Equal("Through the registry", ExtractText(bytes));
    }

    private static string ExtractText(byte[] bytes)
    {
        using var writer = new StringWriter();
        Office.ExtractText(ByteSource.FromMemory(bytes), writer);
        return writer.ToString().Trim();
    }

    [Fact]
    public void ConvertedDocumentRoundTripsThroughDocx()
    {
        using Document doc = Open(Body("<w:p><w:r><w:t>Round trip</w:t></w:r></w:p>"));
        using var ms = new MemoryStream();
        doc.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        using Document again = Document.Open(ms);
        Assert.Equal("Round trip", again.Paragraphs.Single().Text);
        Assert.Null(again.ImportLoss);
    }

    private static string Fixture(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "xml2003", name));

    [Fact]
    public void LibreOfficeWordFileOpens()
    {
        var ctx = new OpenContext();
        using Document doc = Document.Open(Fixture("lo-word.xml"), ctx);
        string[] texts = [.. doc.Paragraphs.Select(p => p.Text)];
        Assert.Contains("Heading one", texts);
        Assert.Contains("Plain, bold red and a link.", texts);
        Assert.Contains("first item", texts);
        Dictionary<string, string> parts = Saved(doc);
        Assert.Contains("word/header1.xml", parts.Keys);
        Assert.Contains("word/footnotes.xml", parts.Keys);
        Assert.Contains("word/comments.xml", parts.Keys);
        Assert.Contains("word/media/image1.png", parts.Keys);
        Assert.Contains("<w:gridSpan w:val=\"2\"/>", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.Single(doc.Tables);
        Assert.Equal("Oracle sample", doc.Properties.Core.Title);
        Assert.DoesNotContain(((DefectCollection)ctx.Defects), d => d.Severity == DefectSeverity.Fatal);
    }

    [Fact]
    public void ExtractedTextOfOracleFile()
    {
        string text = Office.ExtractText(Fixture("lo-word.xml"));
        Assert.Contains("second item", text, StringComparison.Ordinal);
        Assert.Contains("r2c2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagesCanBeReopenedAsPlainOpc()
    {
        using OpcPackage package = WordMl2003.ToPackage(ByteSource.FromMemory(Encoding.UTF8.GetBytes(Body("<w:p><w:r><w:t>x</w:t></w:r></w:p>"))));
        Assert.NotNull(package.FindPart("/word/document.xml"));
        Assert.NotNull(package.FindPart("/word/styles.xml"));
    }

    [Fact]
    public void FeatureDocumentConvertsAndSavesAsDocx()
    {
        var ctx = new OpenContext();
        using Document doc = Document.Open(Fixture("word-features.xml"), ctx);
        string[] texts = [.. doc.Paragraphs.Select(p => p.Text)];
        Assert.Contains("Feature sample", texts);
        Assert.Contains("First numbered", texts);
        Assert.Equal(2, doc.Revisions.Revisions.Count);
        Assert.Single(doc.Comments);
        Assert.Single(doc.Tables);
        Assert.Equal("Feature sample", doc.Properties.Core.Title);
        Dictionary<string, string> parts = Saved(doc);
        Assert.Contains("docProps/custom.xml", parts.Keys);
        Assert.Contains("<property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"2\" name=\"Project\"><vt:lpwstr>Alpha</vt:lpwstr></property>", parts["docProps/custom.xml"], StringComparison.Ordinal);
        Assert.Contains("<w:u w:val=\"dashLong\"/>", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.Contains("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FFFF99\"/>", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.Contains("w:val=\"num\"", parts["word/numbering.xml"], StringComparison.Ordinal);
        Assert.Contains("w:pos=\"8640\"", parts["word/document.xml"], StringComparison.Ordinal);
        Assert.Empty(((DefectCollection)ctx.Defects));
    }

    [Fact]
    public void ConvertsToDocxByExtension()
    {
        string dir = Path.Combine(Path.GetTempPath(), "xml2003-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string output = Path.Combine(dir, "out.docx");
            Office.Convert(Fixture("word-features.xml"), output);
            using Document again = Document.Open(output);
            Assert.Contains("First numbered", again.Paragraphs.Select(p => p.Text));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RootTagLongerThanTheSniffPrefixIsStillWord2003()
    {
        var namespaces = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            namespaces.Append(" xmlns:n").Append(i).Append("=\"urn:example:namespace:number:").Append(i).Append('"');
        }

        string xml = "<?xml version=\"1.0\"?><?mso-application progid=\"Word.Document\"?><w:wordDocument" + namespaces + " xmlns:w=\"http://schemas.microsoft.com/office/word/2003/wordml\"><w:body><w:p><w:r><w:t>long root</w:t></w:r></w:p></w:body></w:wordDocument>";
        Assert.True(xml.IndexOf("xmlns:w=", StringComparison.Ordinal) > 4500);
        Assert.Equal(OfficeFormat.Word2003Xml, FormatSniffer.Detect(ByteSource.FromMemory(Encoding.UTF8.GetBytes(xml)), OfficeLimits.Default));
        using Document doc = Open(xml);
        Assert.Equal("long root", doc.Paragraphs.Single().Text);
    }
}
