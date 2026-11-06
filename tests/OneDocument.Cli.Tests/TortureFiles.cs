using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Cli.Tests;

/// <summary>
/// Generated documents with every category of hidden data and personal information present, one per kind. Every personal string starts
/// with <c>PLANTED-</c> so that a raw search of a sanitised file's parts shows what is left. The files are written deterministically.
/// </summary>
internal static class TortureFiles
{
    internal const string OdRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    internal const string PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships/";
    internal const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    internal const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    // A 1x1 PNG.
    internal static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    internal sealed class Builder
    {
        private readonly OpcPackage _package = OpcPackage.Create();
        private readonly Dictionary<string, List<Relationship>> _rels = [];

        internal void Part(string name, string contentType, string xml) => _package.AddPart(name, contentType, Encoding.UTF8.GetBytes(xml));

        internal void Binary(string name, string contentType, byte[] bytes) => _package.AddPart(name, contentType, bytes);

        internal void Rel(string? source, string id, string type, string target, bool external = false)
        {
            string key = source ?? string.Empty;
            if (!_rels.TryGetValue(key, out List<Relationship>? list))
            {
                _rels[key] = list = [];
            }

            list.Add(new Relationship(id, type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
        }

        internal byte[] ToBytes()
        {
            foreach (KeyValuePair<string, List<Relationship>> kv in _rels)
            {
                _package.SetRelationships(kv.Key.Length == 0 ? null : kv.Key, kv.Value);
            }

            using var ms = new MemoryStream();
            _package.WriteTo(ms);
            _package.Dispose();
            return ms.ToArray();
        }
    }

    private static void CommonParts(Builder b, string mainPart, string mainRelType)
    {
        b.Rel(null, "rId1", OdRel + mainRelType, mainPart.TrimStart('/'));
        b.Rel(null, "rId2", PkgRel + "metadata/core-properties", "docProps/core.xml");
        b.Rel(null, "rId3", OdRel + "extended-properties", "docProps/app.xml");
        b.Rel(null, "rId4", OdRel + "custom-properties", "docProps/custom.xml");
        b.Rel(null, "rId5", PkgRel + "metadata/thumbnail", "docProps/thumbnail.png");
        b.Rel(null, "rId6", "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes", "word/webextensions/taskpanes.xml");
        b.Part("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml",
            Decl + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + "<dc:title>PLANTED-Title</dc:title><dc:creator>PLANTED-Creator</dc:creator><cp:lastModifiedBy>PLANTED-LastBy</cp:lastModifiedBy>"
            + "<dcterms:created xsi:type=\"dcterms:W3CDTF\">2024-01-02T03:04:05Z</dcterms:created></cp:coreProperties>");
        b.Part("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml",
            Decl + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">"
            + "<Application>OneDocument</Application><Company>PLANTED-Company</Company><Manager>PLANTED-Manager</Manager>"
            + "<TitlesOfParts><vt:vector size=\"1\" baseType=\"lpstr\"><vt:lpstr>PLANTED-PartTitle</vt:lpstr></vt:vector></TitlesOfParts></Properties>");
        b.Part("/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml",
            Decl + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">"
            + "<property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"2\" name=\"Secret\"><vt:lpwstr>PLANTED-Custom</vt:lpwstr></property></Properties>");
        b.Binary("/docProps/thumbnail.png", "image/png", Png);
        b.Part("/word/webextensions/taskpanes.xml", "application/vnd.ms-office.webextensiontaskpanes+xml",
            Decl + "<wetp:taskpanes xmlns:wetp=\"http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11\"><wetp:taskpane dockstate=\"right\" visibility=\"0\" width=\"350\" row=\"4\"><wetp:webextensionref xmlns:r=\"" + R + "\" r:id=\"rId1\"/></wetp:taskpane></wetp:taskpanes>");
        b.Rel("/word/webextensions/taskpanes.xml", "rId1", "http://schemas.microsoft.com/office/2011/relationships/webextension", "webextension1.xml");
        b.Part("/word/webextensions/webextension1.xml", "application/vnd.ms-office.webextension+xml",
            Decl + "<we:webextension xmlns:we=\"http://schemas.microsoft.com/office/webextensions/webextension/2010/11\" id=\"{11111111-2222-3333-4444-555555555555}\"><we:reference id=\"PLANTED-addin\" version=\"1.0.0.0\" store=\"developer\" storeType=\"Registry\"/><we:alternateReferences/><we:properties/><we:bindings/></we:webextension>");
        b.Part("/customXml/item1.xml", "application/xml", Decl + "<root xmlns=\"urn:planted\"><secret>PLANTED-CustomXml</secret></root>");
        b.Part("/customXml/itemProps1.xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml",
            Decl + "<ds:datastoreItem xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\" ds:itemID=\"{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}\"><ds:schemaRefs/></ds:datastoreItem>");
        b.Rel("/customXml/item1.xml", "rId1", OdRel + "customXmlProps", "itemProps1.xml");
        b.Rel(mainPart, "rIdCx", OdRel + "customXml", "../customXml/item1.xml");
        b.Binary("/embeddings/oleObject1.bin", "application/vnd.openxmlformats-officedocument.oleObject", [1, 2, 3, 4]);
        b.Rel(mainPart, "rIdOle", OdRel + "oleObject", "../embeddings/oleObject1.bin");
    }

    internal static byte[] Docx()
    {
        var b = new Builder();
        CommonParts(b, "/word/document.xml", "officeDocument");
        const string d = "/word/document.xml";
        b.Part(d, "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            Decl + "<w:document xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"" + A + "\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"w15\"><w:body>"
            + "<w:p w:rsidR=\"00AB12CD\" w:rsidRDefault=\"00AB12CD\"><w:r w:rsidR=\"00AB12CD\"><w:t>Visible text</w:t></w:r></w:p>"
            + "<w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>Commented</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:rPr><w:rStyle w:val=\"CommentReference\"/></w:rPr><w:commentReference w:id=\"0\"/></w:r></w:p>"
            + "<w:p><w:ins w:id=\"10\" w:author=\"PLANTED-RevAuthor\" w:date=\"2024-01-01T00:00:00Z\"><w:r><w:t>inserted</w:t></w:r></w:ins><w:del w:id=\"11\" w:author=\"PLANTED-RevAuthor\" w:date=\"2024-01-01T00:00:00Z\"><w:r><w:delText>PLANTED-deleted</w:delText></w:r></w:del></w:p>"
            + "<w:p><w:r><w:rPr><w:vanish/></w:rPr><w:t>PLANTED-hidden</w:t></w:r><w:r><w:t>shown</w:t></w:r></w:p>"
            + "<w:p><w:sdt><w:sdtPr><w:dataBinding w:prefixMappings=\"xmlns:ns0='urn:planted'\" w:xpath=\"/ns0:root[1]/ns0:secret[1]\" w:storeItemID=\"{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}\"/></w:sdtPr><w:sdtContent><w:r><w:t>bound</w:t></w:r></w:sdtContent></w:sdt></w:p>"
            + "<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"9525\" cy=\"9525\"/><wp:docPr id=\"5\" name=\"PLANTED-hidden-picture\" hidden=\"1\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"p.png\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"rIdImg\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"9525\" cy=\"9525\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>"
            + "<w:p><w:fldSimple w:instr=\" INCLUDETEXT &quot;C:\\secret\\other.docx&quot; \"><w:r><w:t>included</w:t></w:r></w:fldSimple></w:p>"
            + "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w15:collapsed/></w:pPr><w:r><w:t>Collapsed heading</w:t></w:r></w:p>"
            + "<w:p><w:r><w:rPr><w:color w:val=\"FFFFFF\"/></w:rPr><w:t>white text</w:t></w:r></w:p>"
            + "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/><w:footerReference w:type=\"default\" r:id=\"rIdF\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr></w:body></w:document>");
        b.Binary("/word/media/p.png", "image/png", Png);
        b.Rel(d, "rIdImg", OdRel + "image", "media/p.png");
        b.Part("/word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml",
            Decl + "<w:hdr xmlns:w=\"" + W + "\"><w:p><w:r><w:t>PLANTED-header</w:t></w:r></w:p></w:hdr>");
        b.Part("/word/footer1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml",
            Decl + "<w:ftr xmlns:w=\"" + W + "\"><w:p><w:r><w:t>PLANTED-footer</w:t></w:r></w:p></w:ftr>");
        b.Rel(d, "rIdH", OdRel + "header", "header1.xml");
        b.Rel(d, "rIdF", OdRel + "footer", "footer1.xml");
        b.Part("/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml",
            Decl + "<w:comments xmlns:w=\"" + W + "\"><w:comment w:id=\"0\" w:author=\"PLANTED-Commenter\" w:date=\"2024-01-01T00:00:00Z\" w:initials=\"PC\"><w:p><w:r><w:t>PLANTED-comment</w:t></w:r></w:p></w:comment></w:comments>");
        b.Rel(d, "rIdC", OdRel + "comments", "comments.xml");
        b.Part("/word/people.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml",
            Decl + "<w15:people xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\"><w15:person w15:author=\"PLANTED-Commenter\"><w15:presenceInfo w15:providerId=\"None\" w15:userId=\"PLANTED-Commenter\"/></w15:person></w15:people>");
        b.Rel(d, "rIdP", "http://schemas.microsoft.com/office/2011/relationships/people", "people.xml");
        b.Part("/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml",
            Decl + "<w:settings xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\"><w:attachedTemplate r:id=\"rIdT\"/><w:docVars><w:docVar w:name=\"PLANTED-var\" w:val=\"PLANTED-value\"/></w:docVars><w:rsids><w:rsidRoot w:val=\"00AB12CD\"/><w:rsid w:val=\"00AB12CD\"/></w:rsids></w:settings>");
        b.Rel(d, "rIdS", OdRel + "settings", "settings.xml");
        b.Rel("/word/settings.xml", "rIdT", OdRel + "attachedTemplate", "file:///C:/Users/PLANTED/Templates/Corp.dotm", external: true);
        b.Part("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml",
            Decl + "<w:styles xmlns:w=\"" + W + "\"><w:docDefaults/><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/></w:style></w:styles>");
        b.Rel(d, "rIdSt", OdRel + "styles", "styles.xml");
        return b.ToBytes();
    }

    internal const string S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal const string Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    internal static byte[] Xlsx(bool referenceHiddenSheet = false)
    {
        var b = new Builder();
        CommonParts(b, "/xl/workbook.xml", "officeDocument");
        const string wb = "/xl/workbook.xml";
        b.Part(wb, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            Decl + "<workbook xmlns=\"" + S + "\" xmlns:r=\"" + R + "\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:x15ac=\"http://schemas.microsoft.com/office/spreadsheetml/2010/11/ac\" mc:Ignorable=\"x15ac\">"
            + "<fileSharing userName=\"PLANTED-Sharer\"/>"
            + "<mc:AlternateContent><mc:Choice Requires=\"x15ac\"><x15ac:absPath url=\"C:\\Users\\PLANTED\\Documents\" xmlns:x15ac=\"http://schemas.microsoft.com/office/spreadsheetml/2010/11/ac\"/></mc:Choice></mc:AlternateContent>"
            + "<bookViews><workbookView activeTab=\"0\"/></bookViews>"
            + "<sheets><sheet name=\"Visible\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Secret\" sheetId=\"2\" state=\"hidden\" r:id=\"rId2\"/><sheet name=\"Deep\" sheetId=\"3\" state=\"veryHidden\" r:id=\"rId3\"/><sheet name=\"Last\" sheetId=\"4\" r:id=\"rId4\"/></sheets>"
            + "<externalReferences><externalReference r:id=\"rIdX\"/></externalReferences><definedNames><definedName name=\"HiddenName\" hidden=\"1\">Secret!$A$1</definedName><definedName name=\"_xlnm.Print_Area\" localSheetId=\"2\">Deep!$A$1:$B$2</definedName><definedName name=\"_xlnm.Print_Area\" localSheetId=\"3\">Last!$A$1:$B$2</definedName></definedNames>"
            + "</workbook>");
        string sheetXml(string rows, string tail = "", string cols = "") =>
            Decl + "<worksheet xmlns=\"" + S + "\" xmlns:r=\"" + R + "\">" + cols + "<sheetData>" + rows + "</sheetData>" + tail + "</worksheet>";
        b.Part("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
            sheetXml(
                "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>visible</t></is></c><c r=\"B1\"><v>1</v></c><c r=\"C1\" t=\"inlineStr\"><is><t>PLANTED-hidden-column</t></is></c><c r=\"D1\"><f>[1]Sheet1!A1</f><v>42</v></c></row>"
                + "<row r=\"2\" hidden=\"1\"><c r=\"A2\" t=\"inlineStr\"><is><t>PLANTED-hidden-row</t></is></c></row>"
                + "<row r=\"3\"><c r=\"A3\"><f>SUM(B1:B1)</f><v>1</v></c>" + (referenceHiddenSheet ? "<c r=\"B3\"><f>Secret!A1</f><v>7</v></c>" : string.Empty) + "</row>",
                "<pageSetup orientation=\"portrait\" r:id=\"rIdPr\"/><headerFooter><oddHeader>&amp;LPLANTED-sheet-header</oddHeader></headerFooter><drawing r:id=\"rIdD\"/><legacyDrawing r:id=\"rIdV\"/>",
                "<cols><col min=\"3\" max=\"3\" width=\"9\" hidden=\"1\" customWidth=\"1\"/></cols>"));
        b.Part("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
            sheetXml("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>PLANTED-in-hidden-sheet</t></is></c></row>"));
        b.Part("/xl/worksheets/sheet3.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
            sheetXml("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>PLANTED-in-very-hidden-sheet</t></is></c></row>"));
        b.Part("/xl/worksheets/sheet4.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
            sheetXml("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>last</t></is></c></row>"));
        b.Rel(wb, "rId1", OdRel + "worksheet", "worksheets/sheet1.xml");
        b.Rel(wb, "rId2", OdRel + "worksheet", "worksheets/sheet2.xml");
        b.Rel(wb, "rId3", OdRel + "worksheet", "worksheets/sheet3.xml");
        b.Rel(wb, "rId4", OdRel + "worksheet", "worksheets/sheet4.xml");
        b.Binary("/xl/printerSettings/printerSettings1.bin", "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings", [9, 9, 9]);
        b.Rel("/xl/worksheets/sheet1.xml", "rIdPr", OdRel + "printerSettings", "../printerSettings/printerSettings1.bin");
        b.Part("/xl/comments1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml",
            Decl + "<comments xmlns=\"" + S + "\"><authors><author>PLANTED-NoteAuthor</author></authors><commentList><comment ref=\"A1\" authorId=\"0\"><text><r><t>PLANTED-note</t></r></text></comment></commentList></comments>");
        b.Rel("/xl/worksheets/sheet1.xml", "rIdN", OdRel + "comments", "../comments1.xml");
        b.Part("/xl/drawings/vmlDrawing1.vml", "application/vnd.openxmlformats-officedocument.vmlDrawing",
            Decl + "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"><v:shape id=\"_x0000_s1025\" type=\"#_x0000_t202\" style=\"visibility:hidden\"><x:ClientData ObjectType=\"Note\"><x:Row>0</x:Row><x:Column>0</x:Column></x:ClientData></v:shape></xml>");
        b.Rel("/xl/worksheets/sheet1.xml", "rIdV", OdRel + "vmlDrawing", "../drawings/vmlDrawing1.vml");
        b.Part("/xl/threadedComments/threadedComment1.xml", "application/vnd.ms-excel.threadedcomments+xml",
            Decl + "<ThreadedComments xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments\"><threadedComment ref=\"B1\" dT=\"2024-01-01T00:00:00.00\" personId=\"{11111111-1111-1111-1111-111111111111}\" id=\"{22222222-2222-2222-2222-222222222222}\"><text>PLANTED-threaded</text></threadedComment></ThreadedComments>");
        b.Rel("/xl/worksheets/sheet1.xml", "rIdTc", "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment", "../threadedComments/threadedComment1.xml");
        b.Part("/xl/persons/person.xml", "application/vnd.ms-excel.person+xml",
            Decl + "<personList xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments\"><person displayName=\"PLANTED-Person\" id=\"{11111111-1111-1111-1111-111111111111}\" userId=\"PLANTED-Person\" providerId=\"None\"/></personList>");
        b.Rel(wb, "rIdPe", "http://schemas.microsoft.com/office/2017/10/relationships/person", "persons/person.xml");
        b.Part("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml",
            Decl + "<xdr:wsDr xmlns:xdr=\"" + Xdr + "\" xmlns:a=\"" + A + "\"><xdr:twoCellAnchor><xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>2</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>"
            + "<xdr:sp macro=\"\" textlink=\"\"><xdr:nvSpPr><xdr:cNvPr id=\"2\" name=\"PLANTED-hidden-shape\" hidden=\"1\"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"10\" cy=\"10\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:sp><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>");
        b.Rel("/xl/worksheets/sheet1.xml", "rIdD", OdRel + "drawing", "../drawings/drawing1.xml");
        b.Part("/xl/externalLinks/externalLink1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml",
            Decl + "<externalLink xmlns=\"" + S + "\" xmlns:r=\"" + R + "\"><externalBook r:id=\"rId1\"><sheetNames><sheetName val=\"Sheet1\"/></sheetNames></externalBook></externalLink>");
        b.Rel("/xl/externalLinks/externalLink1.xml", "rId1", OdRel + "externalLinkPath", "file:///C:/Users/PLANTED/other.xlsx", external: true);
        b.Rel(wb, "rIdX", OdRel + "externalLink", "externalLinks/externalLink1.xml");
        b.Part("/xl/connections.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml",
            Decl + "<connections xmlns=\"" + S + "\"><connection id=\"1\" name=\"PLANTED-conn\" type=\"1\" refreshedVersion=\"6\"><dbPr connection=\"Provider=PLANTED\" command=\"select 1\"/></connection></connections>");
        b.Rel(wb, "rIdCn", OdRel + "connections", "connections.xml");
        b.Part("/xl/revisions/revisionHeaders.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.revisionHeaders+xml",
            Decl + "<headers xmlns=\"" + S + "\" guid=\"{33333333-3333-3333-3333-333333333333}\"><header guid=\"{44444444-4444-4444-4444-444444444444}\" dateTime=\"2024-01-01T00:00:00\" maxSheetId=\"4\" userName=\"PLANTED-Reviser\" r:id=\"rId1\" xmlns:r=\"" + R + "\"><sheetIdMap count=\"1\"><sheetId val=\"1\"/></sheetIdMap></header></headers>");
        b.Rel(wb, "rIdRv", OdRel + "revisionHeaders", "revisions/revisionHeaders.xml");
        b.Part("/xl/calcChain.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml",
            Decl + "<calcChain xmlns=\"" + S + "\"><c r=\"A3\" i=\"1\"/></calcChain>");
        b.Rel(wb, "rIdCc", OdRel + "calcChain", "calcChain.xml");
        return b.ToBytes();
    }

    internal const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";

    internal static byte[] Pptx()
    {
        var b = new Builder();
        CommonParts(b, "/ppt/presentation.xml", "officeDocument");
        const string pr = "/ppt/presentation.xml";
        b.Part(pr, "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml",
            Decl + "<p:presentation xmlns:p=\"" + P + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\"><p:sldIdLst><p:sldId id=\"256\" r:id=\"rIdS1\"/><p:sldId id=\"257\" r:id=\"rIdS2\"/><p:sldId id=\"258\" r:id=\"rIdS3\"/></p:sldIdLst><p:sldSz cx=\"9144000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
        string spTree(string shapes) => "<p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + shapes + "</p:spTree></p:cSld>";
        string shape(int id, string name, long x, long y, string text, string extra = "") =>
            "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\"" + extra + "/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"1000000\" cy=\"500000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr/><a:p><a:r><a:t>" + text + "</a:t></a:r></a:p></p:txBody></p:sp>";
        const string ct = "application/vnd.openxmlformats-officedocument.presentationml.slide+xml";
        string ink = "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\" Requires=\"p14\"><p:contentPart r:id=\"rIdInk\"/></mc:Choice></mc:AlternateContent>";
        b.Part("/ppt/slides/slide1.xml", ct,
            Decl + "<p:sld xmlns:p=\"" + P + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\">"
            + spTree(shape(2, "Title", 100000, 100000, "Visible slide") + shape(3, "Hidden", 200000, 200000, "PLANTED-hidden-shape", " hidden=\"1\"") + shape(4, "Away", -5000000, 100000, "PLANTED-off-slide") + ink)
            + "<p:extLst><p:ext uri=\"{6950BFC3-D8DA-4A85-94F7-54DA5524770B}\"><p188:commentRel xmlns:p188=\"http://schemas.microsoft.com/office/powerpoint/2018/8/main\" r:id=\"rIdMc\"/></p:ext></p:extLst></p:sld>");
        b.Part("/ppt/slides/slide2.xml", ct,
            Decl + "<p:sld xmlns:p=\"" + P + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\" show=\"0\">" + spTree(shape(2, "Body", 100000, 100000, "PLANTED-hidden-slide")) + "</p:sld>");
        b.Part("/ppt/slides/slide3.xml", ct,
            Decl + "<p:sld xmlns:p=\"" + P + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\">" + spTree(shape(2, "Body", 100000, 100000, "Third slide")) + "</p:sld>");
        b.Rel(pr, "rIdS1", OdRel + "slide", "slides/slide1.xml");
        b.Rel(pr, "rIdS2", OdRel + "slide", "slides/slide2.xml");
        b.Rel(pr, "rIdS3", OdRel + "slide", "slides/slide3.xml");
        b.Part("/ppt/ink/ink1.xml", "application/inkml+xml", Decl + "<inkml:ink xmlns:inkml=\"http://www.w3.org/2003/InkML\"><inkml:trace>0 0, 10 10</inkml:trace></inkml:ink>");
        b.Rel("/ppt/slides/slide1.xml", "rIdInk", "http://schemas.microsoft.com/office/2007/relationships/ink", "../ink/ink1.xml");
        b.Part("/ppt/commentAuthors.xml", "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml",
            Decl + "<p:cmAuthorLst xmlns:p=\"" + P + "\"><p:cmAuthor id=\"0\" name=\"PLANTED-SlideCommenter\" initials=\"PS\" lastIdx=\"1\" clrIdx=\"0\"/></p:cmAuthorLst>");
        b.Rel(pr, "rIdCa", OdRel + "commentAuthors", "commentAuthors.xml");
        b.Part("/ppt/comments/comment1.xml", "application/vnd.openxmlformats-officedocument.presentationml.comments+xml",
            Decl + "<p:cmLst xmlns:p=\"" + P + "\"><p:cm authorId=\"0\" dt=\"2024-01-01T00:00:00.000\" idx=\"1\"><p:pos x=\"10\" y=\"10\"/><p:text>PLANTED-slide-comment</p:text></p:cm></p:cmLst>");
        b.Rel("/ppt/slides/slide3.xml", "rIdCm", OdRel + "comments", "../comments/comment1.xml");
        b.Part("/ppt/comments/modernComment_100_0.xml", "application/vnd.ms-powerpoint.comments+xml",
            Decl + "<p188:cmLst xmlns:p188=\"http://schemas.microsoft.com/office/powerpoint/2018/8/main\"><p188:cm id=\"{55555555-5555-5555-5555-555555555555}\" authorId=\"{66666666-6666-6666-6666-666666666666}\" created=\"2024-01-01T00:00:00.000\"><p188:txBody><a:bodyPr xmlns:a=\"" + A + "\"/><a:p xmlns:a=\"" + A + "\"><a:r><a:t>PLANTED-modern-comment</a:t></a:r></a:p></p188:txBody></p188:cm></p188:cmLst>");
        b.Rel("/ppt/slides/slide1.xml", "rIdMc", "http://schemas.microsoft.com/office/2018/10/relationships/comments", "../comments/modernComment_100_0.xml");
        b.Part("/ppt/authors.xml", "application/vnd.ms-powerpoint.authors+xml",
            Decl + "<p188:authorLst xmlns:p188=\"http://schemas.microsoft.com/office/powerpoint/2018/8/main\"><p188:author id=\"{66666666-6666-6666-6666-666666666666}\" name=\"PLANTED-ModernAuthor\" initials=\"MA\" userId=\"PLANTED\" providerId=\"None\"/></p188:authorLst>");
        b.Rel(pr, "rIdAu", "http://schemas.microsoft.com/office/2018/10/relationships/authors", "authors.xml");
        string notes(string text) => Decl + "<p:notes xmlns:p=\"" + P + "\" xmlns:a=\"" + A + "\">" + spTree(shape(2, "Notes", 100000, 100000, text)) + "</p:notes>";
        b.Part("/ppt/notesSlides/notesSlide1.xml", "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml", notes("PLANTED-speaker-notes"));
        b.Rel("/ppt/slides/slide1.xml", "rIdNs", OdRel + "notesSlide", "../notesSlides/notesSlide1.xml");
        b.Rel("/ppt/notesSlides/notesSlide1.xml", "rId1", OdRel + "slide", "../slides/slide1.xml");
        b.Part("/ppt/notesSlides/notesSlide2.xml", "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml", notes("PLANTED-notes-of-hidden-slide"));
        b.Rel("/ppt/slides/slide2.xml", "rIdNs", OdRel + "notesSlide", "../notesSlides/notesSlide2.xml");
        b.Rel("/ppt/notesSlides/notesSlide2.xml", "rId1", OdRel + "slide", "../slides/slide2.xml");
        return b.ToBytes();
    }
}
