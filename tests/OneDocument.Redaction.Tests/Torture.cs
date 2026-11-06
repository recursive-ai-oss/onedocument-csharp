using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Redaction.Tests;

/// <summary>Builds small OOXML packages that plant a secret in every place the redaction must reach.</summary>
internal sealed class PackageBuilder
{
    public const string Secret = "ZEBRA7731";

    private readonly OpcPackage _package = OpcPackage.Create();
    private readonly Dictionary<string, List<Relationship>> _rels = [];

    public const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public PackageBuilder Add(string name, string contentType, string xml) => AddBytes(name, contentType, Encoding.UTF8.GetBytes(xml));

    public PackageBuilder AddBytes(string name, string contentType, byte[] bytes)
    {
        _package.AddPart(name, contentType, bytes);
        return this;
    }

    public PackageBuilder Rel(string? owner, string id, string type, string target, bool external = false)
    {
        string key = owner ?? string.Empty;
        if (!_rels.TryGetValue(key, out List<Relationship>? list))
        {
            _rels[key] = list = [];
        }

        list.Add(new Relationship(id, type.StartsWith("http", StringComparison.Ordinal) ? type : RelBase + type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
        return this;
    }

    public byte[] Build()
    {
        foreach ((string owner, List<Relationship> list) in _rels)
        {
            _package.SetRelationships(owner.Length == 0 ? null : owner, list);
        }

        using var ms = new MemoryStream();
        _package.WriteTo(ms);
        _package.Dispose();
        return ms.ToArray();
    }
}

internal static class Torture
{
    public const string S = PackageBuilder.Secret;

    private const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
    private const string A = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    // The secret in a PNG text chunk and in a binary object, as UTF-8, UTF-16 and with the secret split nowhere.
    private static byte[] Png() => [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. Encoding.ASCII.GetBytes("tEXtComment\0" + S + " in metadata"), 0, 0, 0, 0];

    private static byte[] OleBlob() => [.. Encoding.ASCII.GetBytes("MZ-binary " + S + " ascii "), 0, 0, .. Encoding.Unicode.GetBytes(S + " wide"), 0, 1, 2];

    private static string ChartXml(string sheet) =>
        Decl + "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" " + A + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><c:chart>" +
        "<c:title><c:tx><c:rich><a:bodyPr/><a:p><a:r><a:t>Chart " + S + "</a:t></a:r></a:p></c:rich></c:tx></c:title><c:plotArea><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/>" +
        "<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:tx><c:strRef><c:f>" + sheet + "!$B$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>" + S + " series</c:v></c:pt></c:strCache></c:strRef></c:tx>" +
        "<c:cat><c:strRef><c:f>" + sheet + "!$A$2:$A$3</c:f><c:strCache><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>" + S + " a</c:v></c:pt><c:pt idx=\"1\"><c:v>other</c:v></c:pt></c:strCache></c:strRef></c:cat>" +
        "<c:val><c:numRef><c:f>" + sheet + "!$C$2:$C$3</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"2\"/><c:pt idx=\"0\"><c:v>1</c:v></c:pt><c:pt idx=\"1\"><c:v>2</c:v></c:pt></c:numCache></c:numRef></c:val></c:ser>" +
        "<c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart></c:plotArea></c:chart><c:externalData r:id=\"rId1\"><c:autoUpdate val=\"0\"/></c:externalData></c:chartSpace>";

    private static byte[] EmbeddedWorkbook()
    {
        return new PackageBuilder()
            .Add("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Decl + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>")
            .Add("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Decl + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row></sheetData></worksheet>")
            .Add("/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml", Decl + "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"1\" uniqueCount=\"1\"><si><t>embedded " + S + "</t></si></sst>")
            .Rel(null, "rId1", "officeDocument", "xl/workbook.xml")
            .Rel("/xl/workbook.xml", "rId1", "worksheet", "worksheets/sheet1.xml")
            .Rel("/xl/workbook.xml", "rId2", "sharedStrings", "sharedStrings.xml")
            .Build();
    }

    private static string DiagramData() =>
        Decl + "<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" " + A + "><dgm:ptLst><dgm:pt modelId=\"1\"><dgm:t><a:bodyPr/><a:p><a:r><a:t>Node " + S + "</a:t></a:r></a:p></dgm:t></dgm:pt></dgm:ptLst></dgm:dataModel>";

    private static string DiagramDrawing() =>
        Decl + "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" " + A + "><dsp:spTree><dsp:sp modelId=\"1\"><dsp:txBody><a:bodyPr/><a:p><a:r><a:t>Drawn " + S + "</a:t></a:r></a:p></dsp:txBody></dsp:sp></dsp:spTree></dsp:drawing>";

    private static string Core() =>
        Decl + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>Title " + S + "</dc:title><dc:creator>Author " + S + "</dc:creator><cp:keywords>" + S + "</cp:keywords></cp:coreProperties>";

    private static string App() =>
        Decl + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Company>" + S + " Corp</Company><Manager>M</Manager></Properties>";

    private static string CustomProps() =>
        Decl + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"2\" name=\"Custom " + S + "\"><vt:lpwstr>value " + S + "</vt:lpwstr></property></Properties>";

    private static PackageBuilder Common(PackageBuilder b, string main)
    {
        const string Core = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
        return b
            .Add("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Torture.Core())
            .Add("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml", App())
            .Add("/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml", CustomProps())
            .AddBytes("/docProps/thumbnail.png", "image/png", Png())
            .Rel(null, "rId1", "officeDocument", main)
            .Rel(null, "rId2", Core, "docProps/core.xml")
            .Rel(null, "rId3", "extended-properties", "docProps/app.xml")
            .Rel(null, "rId4", "custom-properties", "docProps/custom.xml")
            .Rel(null, "rId5", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail", "docProps/thumbnail.png");
    }

    public static byte[] Docx()
    {
        string body =
            "<w:p><w:r><w:t>Plain " + S + " in one run.</w:t></w:r></w:p>" +
            "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>ZEB</w:t></w:r><w:r><w:t>RA77</w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>31 split</w:t></w:r></w:p>" +
            "<w:p><w:del w:id=\"1\" w:author=\"a\"><w:r><w:delText>deleted " + S + "</w:delText></w:r></w:del><w:r><w:t>kept</w:t></w:r></w:p>" +
            "<w:p><w:moveFrom w:id=\"2\" w:author=\"a\"><w:r><w:delText>moved " + S + "</w:delText></w:r></w:moveFrom></w:p>" +
            "<w:p><w:fldSimple w:instr=\" REF " + S + " \\h\"><w:r><w:t>result " + S + "</w:t></w:r></w:fldSimple></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> DOCPROPERTY " + S + " </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>cached " + S + "</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>" +
            "<w:p><w:bookmarkStart w:id=\"3\" w:name=\"" + S + "\"/><w:r><w:t>marked</w:t></w:r><w:bookmarkEnd w:id=\"3\"/></w:p>" +
            "<w:p><w:hyperlink r:id=\"rId20\" w:tooltip=\"tip " + S + "\" w:anchor=\"" + S + "\"><w:r><w:t>link</w:t></w:r></w:hyperlink></w:p>" +
            "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"><wp:docPr id=\"1\" name=\"Picture " + S + "\" descr=\"alt " + S + "\" title=\"title " + S + "\"/><a:graphic " + A + "><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId21\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>" +
            "<w:p><w:r><mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice Requires=\"wps\"><w:drawing><wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"><wps:txbx><w:txbxContent><w:p><w:r><w:t>box " + S + "</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></w:drawing></mc:Choice><mc:Fallback><w:pict><w:txbxContent><w:p><w:r><w:t>box " + S + "</w:t></w:r></w:p></w:txbxContent></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>" +
            "<w:sdt><w:sdtPr><w:alias w:val=\"alias " + S + "\"/><w:tag w:val=\"tag" + S + "\"/><w:dataBinding w:xpath=\"/root/secret\" w:storeItemID=\"{11111111-1111-1111-1111-111111111111}\"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>" + S + "</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:p><w:r><w:t>Note</w:t></w:r><w:r><w:footnoteReference w:id=\"2\"/></w:r><w:commentRangeStart w:id=\"0\"/><w:r><w:t>commented</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell " + S + "</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "<w:p><w:r><w:object><w:ole r:id=\"rId22\"/></w:object></w:r></w:p>" +
            "<w:p><w:r><w:t>Unrelated trailing paragraph.</w:t></w:r></w:p>" +
            "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rId23\"/><w:footerReference w:type=\"default\" r:id=\"rId24\"/></w:sectPr>";
        var b = new PackageBuilder();
        Common(b, "word/document.xml");
        const string Wml = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
        b.Add("/word/document.xml", Wml + "document.main+xml", Decl + "<w:document " + W + "><w:body>" + body + "</w:body></w:document>")
            .Add("/word/settings.xml", Wml + "settings+xml", Decl + "<w:settings " + W + "><w:docVars><w:docVar w:name=\"var" + S + "\" w:val=\"" + S + "\"/></w:docVars></w:settings>")
            .Add("/word/styles.xml", Wml + "styles+xml", Decl + "<w:styles " + W + "><w:style w:type=\"paragraph\" w:styleId=\"Custom\"><w:name w:val=\"Style " + S + "\"/></w:style></w:styles>")
            .Add("/word/comments.xml", Wml + "comments+xml", Decl + "<w:comments " + W + "><w:comment w:id=\"0\" w:author=\"Reviewer\" w:initials=\"R\"><w:p><w:r><w:t>comment " + S + "</w:t></w:r></w:p></w:comment><w:comment w:id=\"1\" w:author=\"Replier\"><w:p><w:r><w:t>reply " + S + "</w:t></w:r></w:p></w:comment></w:comments>")
            .Add("/word/footnotes.xml", Wml + "footnotes+xml", Decl + "<w:footnotes " + W + "><w:footnote w:id=\"2\"><w:p><w:r><w:t>footnote " + S + "</w:t></w:r></w:p></w:footnote></w:footnotes>")
            .Add("/word/endnotes.xml", Wml + "endnotes+xml", Decl + "<w:endnotes " + W + "><w:endnote w:id=\"2\"><w:p><w:r><w:t>endnote " + S + "</w:t></w:r></w:p></w:endnote></w:endnotes>")
            .Add("/word/header1.xml", Wml + "header+xml", Decl + "<w:hdr " + W + "><w:p><w:r><w:t>header " + S + "</w:t></w:r></w:p></w:hdr>")
            .Add("/word/footer1.xml", Wml + "footer+xml", Decl + "<w:ftr " + W + "><w:p><w:r><w:t>footer " + S + "</w:t></w:r></w:p></w:ftr>")
            .Add("/word/glossary/document.xml", Wml + "glossary+xml", Decl + "<w:glossaryDocument " + W + "><w:docParts><w:docPart><w:docPartPr><w:name w:val=\"part " + S + "\"/></w:docPartPr><w:docPartBody><w:p><w:r><w:t>glossary " + S + "</w:t></w:r></w:p></w:docPartBody></w:docPart></w:docParts></w:glossaryDocument>")
            .Add("/customXml/item1.xml", "application/xml", Decl + "<root><secret>" + S + "</secret><!-- note " + S + " --></root>")
            .Add("/word/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", ChartXml("Sheet1"))
            .AddBytes("/word/embeddings/Microsoft_Excel_Sheet1.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", EmbeddedWorkbook())
            .AddBytes("/word/embeddings/oleObject1.bin", "application/vnd.openxmlformats-officedocument.oleObject", OleBlob())
            .AddBytes("/word/media/image1.png", "image/png", Png())
            .Add("/word/diagrams/data1.xml", "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml", DiagramData())
            .Add("/word/diagrams/drawing1.xml", "application/vnd.ms-office.drawingml.diagramDrawing+xml", DiagramDrawing())
            .Rel("/word/document.xml", "rId20", "hyperlink", "http://example.com/page?q=" + S, external: true)
            .Rel("/word/document.xml", "rId21", "chart", "charts/chart1.xml")
            .Rel("/word/document.xml", "rId22", "oleObject", "embeddings/oleObject1.bin")
            .Rel("/word/document.xml", "rId23", "header", "header1.xml")
            .Rel("/word/document.xml", "rId24", "footer", "footer1.xml")
            .Rel("/word/document.xml", "rId25", "comments", "comments.xml")
            .Rel("/word/document.xml", "rId26", "footnotes", "footnotes.xml")
            .Rel("/word/document.xml", "rId27", "endnotes", "endnotes.xml")
            .Rel("/word/document.xml", "rId28", "settings", "settings.xml")
            .Rel("/word/document.xml", "rId29", "styles", "styles.xml")
            .Rel("/word/document.xml", "rId30", "glossaryDocument", "glossary/document.xml")
            .Rel("/word/document.xml", "rId31", "customXml", "../customXml/item1.xml")
            .Rel("/word/document.xml", "rId32", "image", "media/image1.png")
            .Rel("/word/document.xml", "rId33", "diagramData", "diagrams/data1.xml")
            .Rel("/word/document.xml", "rId34", "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing", "diagrams/drawing1.xml")
            .Rel("/word/charts/chart1.xml", "rId1", "package", "../embeddings/Microsoft_Excel_Sheet1.xlsx");
        return b.Build();
    }

    public static byte[] Xlsx()
    {
        const string Sml = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
        const string Ns = "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
        var b = new PackageBuilder();
        Common(b, "xl/workbook.xml");
        b.Add("/xl/workbook.xml", Sml + "sheet.main+xml", Decl + "<workbook " + Ns + "><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Sheet " + S + "\" sheetId=\"2\" r:id=\"rId2\"/></sheets>" +
                "<definedNames><definedName name=\"Name" + S + "\">Sheet1!$A$1</definedName><definedName name=\"Plain\">\"text " + S + "\"</definedName></definedNames></workbook>")
            .Add("/xl/worksheets/sheet1.xml", Sml + "worksheet+xml", Decl + "<worksheet " + Ns + "><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"inlineStr\"><is><t>inline " + S + "</t></is></c></row>" +
                "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\" t=\"str\"><f>IF(A1=\"" + S + "\",\"yes\",\"no\")</f><v>cached " + S + "</v></c><c r=\"C2\"><v>7731</v></c></row>" +
                "<row r=\"3\"><c r=\"A3\" t=\"str\"><f>\"" + S + "\"</f><v>" + S + "</v></c><c r=\"B3\"><v>42</v></c></row>" +
                "</sheetData><dataValidations count=\"1\"><dataValidation type=\"list\" sqref=\"A1\"><formula1>\"a,b," + S + "\"</formula1></dataValidation></dataValidations>" +
                "<hyperlinks><hyperlink ref=\"A2\" r:id=\"rId1\" tooltip=\"tip " + S + "\"/></hyperlinks><headerFooter><oddHeader>&amp;C" + S + "</oddHeader></headerFooter><drawing r:id=\"rId2\"/><legacyDrawing r:id=\"rId3\"/><tableParts count=\"1\"><tablePart r:id=\"rId4\"/></tableParts></worksheet>")
            .Add("/xl/worksheets/sheet2.xml", Sml + "worksheet+xml", Decl + "<worksheet " + Ns + "><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>3</v></c></row></sheetData></worksheet>")
            .Add("/xl/sharedStrings.xml", Sml + "sharedStrings+xml", Decl + "<sst " + Ns + " count=\"4\" uniqueCount=\"4\"><si><t>Header " + S + "</t></si><si><r><t>ZEBRA</t></r><r><rPr><b/></rPr><t>7731 rich</t></r></si><si><t>untouched text</t></si><si><t>second sheet</t></si></sst>")
            .Add("/xl/comments1.xml", Sml + "comments+xml", Decl + "<comments " + Ns + "><authors><author>Author " + S + "</author></authors><commentList><comment ref=\"A2\" authorId=\"0\"><text><r><t>note " + S + "</t></r></text></comment></commentList></comments>")
            .Add("/xl/drawings/vmlDrawing1.vml", "application/vnd.openxmlformats-officedocument.vmlDrawing", Decl + "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"><v:shape id=\"s1\" alt=\"" + S + "\"><x:ClientData ObjectType=\"Note\"><x:Row>1</x:Row><x:Column>0</x:Column></x:ClientData></v:shape></xml>")
            .Add("/xl/tables/table1.xml", Sml + "table+xml", Decl + "<table " + Ns + " id=\"1\" name=\"Table" + S + "\" displayName=\"Table" + S + "\" ref=\"A1:B1\"><tableColumns count=\"2\"><tableColumn id=\"1\" name=\"Header " + S + "\"/><tableColumn id=\"2\" name=\"Other\"/></tableColumns></table>")
            .Add("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml", Decl + "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" " + A + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><xdr:twoCellAnchor><xdr:from><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>5</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>8</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>10</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to><xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"Chart " + S + "\" descr=\"alt " + S + "\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId1\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>")
            .Add("/xl/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", ChartXml("Sheet1"))
            .AddBytes("/xl/embeddings/Microsoft_Excel_Sheet1.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", EmbeddedWorkbook())
            .Add("/xl/externalLinks/externalLink1.xml", Sml + "externalLink+xml", Decl + "<externalLink " + Ns + "><externalBook r:id=\"rId1\"><sheetNames><sheetName val=\"Ext\"/></sheetNames><sheetDataSet><sheetData sheetId=\"0\"><row r=\"1\"><cell r=\"A1\" t=\"str\"><v>external " + S + "</v></cell><cell r=\"B1\"><v>7731</v></cell></row></sheetData></sheetDataSet></externalBook></externalLink>")
            .Add("/xl/pivotCache/pivotCacheDefinition1.xml", Sml + "pivotCacheDefinition+xml", Decl + "<pivotCacheDefinition " + Ns + " r:id=\"rId1\" recordCount=\"1\"><cacheSource type=\"worksheet\"><worksheetSource ref=\"A1:B2\" sheet=\"Sheet1\"/></cacheSource><cacheFields count=\"1\"><cacheField name=\"Field " + S + "\" numFmtId=\"0\"><sharedItems count=\"1\"><s v=\"" + S + "\"/></sharedItems></cacheField></cacheFields></pivotCacheDefinition>")
            .Add("/xl/pivotCache/pivotCacheRecords1.xml", Sml + "pivotCacheRecords+xml", Decl + "<pivotCacheRecords " + Ns + " count=\"1\"><r><s v=\"" + S + " record\"/></r></pivotCacheRecords>")
            .Rel("/xl/workbook.xml", "rId1", "worksheet", "worksheets/sheet1.xml")
            .Rel("/xl/workbook.xml", "rId2", "worksheet", "worksheets/sheet2.xml")
            .Rel("/xl/workbook.xml", "rId3", "sharedStrings", "sharedStrings.xml")
            .Rel("/xl/workbook.xml", "rId4", "externalLink", "externalLinks/externalLink1.xml")
            .Rel("/xl/workbook.xml", "rId5", "pivotCacheDefinition", "pivotCache/pivotCacheDefinition1.xml")
            .Rel("/xl/externalLinks/externalLink1.xml", "rId1", "externalLinkPath", "file:///C:/data/" + S + ".xlsx", external: true)
            .Rel("/xl/pivotCache/pivotCacheDefinition1.xml", "rId1", "pivotCacheRecords", "pivotCacheRecords1.xml")
            .Rel("/xl/worksheets/sheet1.xml", "rId1", "hyperlink", "http://example.com/" + S, external: true)
            .Rel("/xl/worksheets/sheet1.xml", "rId2", "drawing", "../drawings/drawing1.xml")
            .Rel("/xl/worksheets/sheet1.xml", "rId3", "vmlDrawing", "../drawings/vmlDrawing1.vml")
            .Rel("/xl/worksheets/sheet1.xml", "rId4", "table", "../tables/table1.xml")
            .Rel("/xl/worksheets/sheet1.xml", "rId5", "comments", "../comments1.xml")
            .Rel("/xl/drawings/drawing1.xml", "rId1", "chart", "../charts/chart1.xml")
            .Rel("/xl/charts/chart1.xml", "rId1", "package", "../embeddings/Microsoft_Excel_Sheet1.xlsx");
        return b.Build();
    }

    public static byte[] Pptx()
    {
        const string Pml = "application/vnd.openxmlformats-officedocument.presentationml.";
        const string P = "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " + A + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
        string Slide(string shapes, string timing = "") => Decl + "<p:sld " + P + "><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + shapes + "</p:spTree></p:cSld>" + timing + "</p:sld>";
        string Tx(int id, string name, string descr, string text) =>
            "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\" descr=\"" + descr + "\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:p>" + text + "</a:p></p:txBody></p:sp>";
        var b = new PackageBuilder();
        Common(b, "ppt/presentation.xml");
        string slide1 =
            Tx(2, "Title", "alt " + S, "<a:r><a:rPr b=\"1\"/><a:t>ZEBRA</a:t></a:r><a:r><a:t>7731 title</a:t></a:r>") +
            Tx(3, "Link", "", "<a:r><a:rPr><a:hlinkClick r:id=\"rId10\" tooltip=\"tip " + S + "\"/></a:rPr><a:t>link</a:t></a:r><a:fld id=\"{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}\" type=\"slidenum\"><a:t>cached " + S + "</a:t></a:fld>") +
            "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"4\" name=\"Chart " + S + "\" descr=\"chart alt " + S + "\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1\" cy=\"1\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId11\"/></a:graphicData></a:graphic></p:graphicFrame>" +
            "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"5\" name=\"Table\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1\" cy=\"1\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblGrid><a:gridCol w=\"1\"/></a:tblGrid><a:tr h=\"1\"><a:tc><a:txBody><a:bodyPr/><a:p><a:r><a:t>cell " + S + "</a:t></a:r></a:p></a:txBody></a:tc></a:tr></a:tbl></a:graphicData></a:graphic></p:graphicFrame>" +
            "<p:pic><p:nvPicPr><p:cNvPr id=\"6\" name=\"Pic\" descr=\"pic " + S + "\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed=\"rId12\"/></p:blipFill><p:spPr/></p:pic>";
        b.Add("/ppt/presentation.xml", Pml + "presentation.main+xml", Decl + "<p:presentation " + P + "><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId2\"/><p:sldId id=\"257\" r:id=\"rId3\"/></p:sldIdLst><p:sldSz cx=\"9144000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>")
            .Add("/ppt/slideMasters/slideMaster1.xml", Pml + "slideMaster+xml", Decl + "<p:sldMaster " + P + "><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + Tx(2, "Master", "", "<a:r><a:t>master " + S + "</a:t></a:r>") + "</p:spTree></p:cSld><p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/><p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst></p:sldMaster>")
            .Add("/ppt/slideLayouts/slideLayout1.xml", Pml + "slideLayout+xml", Decl + "<p:sldLayout " + P + "><p:cSld name=\"Layout " + S + "\"><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + Tx(2, "Prompt", "", "<a:r><a:t>layout " + S + "</a:t></a:r>") + "</p:spTree></p:cSld></p:sldLayout>")
            .Add("/ppt/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml", Decl + "<a:theme " + A + " name=\"Office Theme\"><a:themeElements><a:clrScheme name=\"c\"><a:dk1><a:srgbClr val=\"000000\"/></a:dk1><a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"000000\"/></a:dk2><a:lt2><a:srgbClr val=\"FFFFFF\"/></a:lt2><a:accent1><a:srgbClr val=\"000000\"/></a:accent1><a:accent2><a:srgbClr val=\"000000\"/></a:accent2><a:accent3><a:srgbClr val=\"000000\"/></a:accent3><a:accent4><a:srgbClr val=\"000000\"/></a:accent4><a:accent5><a:srgbClr val=\"000000\"/></a:accent5><a:accent6><a:srgbClr val=\"000000\"/></a:accent6><a:hlink><a:srgbClr val=\"000000\"/></a:hlink><a:folHlink><a:srgbClr val=\"000000\"/></a:folHlink></a:clrScheme><a:fontScheme name=\"f\"><a:majorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont><a:minorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme><a:fmtScheme name=\"m\"><a:fillStyleLst><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w=\"1\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:ln><a:ln w=\"1\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:ln><a:ln w=\"1\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>")
            .Add("/ppt/slides/slide1.xml", Pml + "slide+xml", Slide(slide1, "<p:timing><p:tnLst><p:par><p:cTn id=\"1\" dur=\"indefinite\"><p:childTnLst><p:set><p:cBhvr><p:cTn id=\"2\"/><p:tgtEl><p:spTgt spid=\"6\"/></p:tgtEl><p:attrNameLst><p:attrName>style.visibility</p:attrName></p:attrNameLst></p:cBhvr></p:set></p:childTnLst></p:cTn></p:par></p:tnLst></p:timing>"))
            .Add("/ppt/slides/slide2.xml", Pml + "slide+xml", Slide(Tx(2, "Body", "", "<a:r><a:t>Second slide, nothing secret.</a:t></a:r>")))
            .Add("/ppt/notesSlides/notesSlide1.xml", Pml + "notesSlide+xml", Decl + "<p:notes " + P + "><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + Tx(2, "Notes", "", "<a:r><a:t>speaker " + S + "</a:t></a:r>") + "</p:spTree></p:cSld></p:notes>")
            .Add("/ppt/comments/comment1.xml", Pml + "comments+xml", Decl + "<p:cmLst " + P + "><p:cm authorId=\"0\" dt=\"2020-01-01T00:00:00\" idx=\"1\"><p:pos x=\"0\" y=\"0\"/><p:text>comment " + S + "</p:text></p:cm></p:cmLst>")
            .Add("/ppt/commentAuthors.xml", Pml + "commentAuthors+xml", Decl + "<p:cmAuthorLst " + P + "><p:cmAuthor id=\"0\" name=\"Author " + S + "\" initials=\"A\" lastIdx=\"1\" clrIdx=\"0\"/></p:cmAuthorLst>")
            .Add("/ppt/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", ChartXml("Sheet1"))
            .AddBytes("/ppt/embeddings/Microsoft_Excel_Sheet1.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", EmbeddedWorkbook())
            .AddBytes("/ppt/media/image1.png", "image/png", Png())
            .Add("/ppt/diagrams/data1.xml", "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml", DiagramData())
            .Add("/ppt/diagrams/drawing1.xml", "application/vnd.ms-office.drawingml.diagramDrawing+xml", DiagramDrawing())
            .Rel("/ppt/presentation.xml", "rId1", "slideMaster", "slideMasters/slideMaster1.xml")
            .Rel("/ppt/presentation.xml", "rId2", "slide", "slides/slide1.xml")
            .Rel("/ppt/presentation.xml", "rId3", "slide", "slides/slide2.xml")
            .Rel("/ppt/presentation.xml", "rId4", "theme", "theme/theme1.xml")
            .Rel("/ppt/presentation.xml", "rId5", "commentAuthors", "commentAuthors.xml")
            .Rel("/ppt/slideMasters/slideMaster1.xml", "rId1", "slideLayout", "../slideLayouts/slideLayout1.xml")
            .Rel("/ppt/slideMasters/slideMaster1.xml", "rId2", "theme", "../theme/theme1.xml")
            .Rel("/ppt/slideLayouts/slideLayout1.xml", "rId1", "slideMaster", "../slideMasters/slideMaster1.xml")
            .Rel("/ppt/slides/slide1.xml", "rId1", "slideLayout", "../slideLayouts/slideLayout1.xml")
            .Rel("/ppt/slides/slide1.xml", "rId2", "notesSlide", "../notesSlides/notesSlide1.xml")
            .Rel("/ppt/slides/slide1.xml", "rId3", "comments", "../comments/comment1.xml")
            .Rel("/ppt/slides/slide1.xml", "rId10", "hyperlink", "http://example.com/" + S, external: true)
            .Rel("/ppt/slides/slide1.xml", "rId11", "chart", "../charts/chart1.xml")
            .Rel("/ppt/slides/slide1.xml", "rId12", "image", "../media/image1.png")
            .Rel("/ppt/slides/slide1.xml", "rId13", "diagramData", "../diagrams/data1.xml")
            .Rel("/ppt/slides/slide1.xml", "rId14", "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing", "../diagrams/drawing1.xml")
            .Rel("/ppt/slides/slide2.xml", "rId1", "slideLayout", "../slideLayouts/slideLayout1.xml")
            .Rel("/ppt/notesSlides/notesSlide1.xml", "rId1", "slide", "../slides/slide1.xml")
            .Rel("/ppt/charts/chart1.xml", "rId1", "package", "../embeddings/Microsoft_Excel_Sheet1.xlsx");
        return b.Build();
    }
}
