using RecursiveAI.OneDocument.Inspection;
using static RecursiveAI.OneDocument.Cli.Tests.TortureFiles;

namespace RecursiveAI.OneDocument.Cli.Tests;

/// <summary>
/// One adversarial document: a place where hidden data or personal information can sit, the categories inspection has to report for
/// it, and (when known) why sanitising cannot yet remove it. Every personal string is <c>PLANTED-</c> followed by the case name, so a raw
/// search of the written file shows what is left.
/// </summary>
internal sealed record ReviewScenario(string Name, OfficeKind Kind, byte[] Bytes, InspectionCategory[] Expect)
{
    /// <summary>The categories to remove; null means every removable category.</summary>
    internal InspectionCategory[]? Only { get; init; }

    /// <summary>Categories whose items are expected to be kept and reported (so a planted string may remain in them).</summary>
    internal InspectionCategory[] Kept { get; init; } = [];

    /// <summary>A gap that is known and recorded in the review: the case is skipped while it still fails and fails when it starts to pass.</summary>
    internal string? Gap { get; init; }

    /// <summary>Text that inspection's notes must contain (for findings that are notes, not categories).</summary>
    internal string? ExpectNote { get; init; }

    /// <summary>Parts that must not be in the written file.</summary>
    internal string[] Gone { get; init; } = [];

    /// <summary>The planted marker of the case.</summary>
    internal string Marker => "PLANTED-" + Name;
}

/// <summary>The adversarial documents of the sanitiser-coverage review, built with the same package builder as the torture files.</summary>
internal static class ReviewScenarios
{
    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string WpNs = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string WpsNs = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string PNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string MsRel = "http://schemas.microsoft.com/office/";
    private const string WCt = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
    private const string SCt = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
    private const string PCt = "application/vnd.openxmlformats-officedocument.presentationml.";
    private const string When = "w:date=\"2024-01-01T00:00:00Z\"";

    private static readonly string s_wordNs = "xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\" xmlns:mc=\"" + Mc + "\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\" xmlns:wp=\"" + WpNs + "\" xmlns:a=\"" + A + "\" xmlns:wps=\"" + WpsNs + "\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" mc:Ignorable=\"w14 w15\"";

    private static ReviewScenario[]? s_all;

    internal static IReadOnlyList<ReviewScenario> All => s_all ??= Build();

    // ---- Word ----

    private static string Para(string text) => "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static Builder Word(string body, string sect = "<w:sectPr/>", string? wordNs = null)
    {
        var b = new Builder();
        b.Rel(null, "rId1", OdRel + "officeDocument", "word/document.xml");
        b.Part("/word/document.xml", WCt + "document.main+xml", Decl + "<w:document " + (wordNs ?? s_wordNs) + "><w:body>" + Para("Visible") + body + sect + "</w:body></w:document>");
        return b;
    }

    private static void WordPart(Builder b, string name, string ct, string relType, string xml, string relId)
    {
        b.Part("/word/" + name, WCt + ct, Decl + xml);
        b.Rel("/word/document.xml", relId, relType, name);
    }

    private static string Notes(string kind, string inner) =>
        "<w:" + kind + "s " + s_wordNs + "><w:" + kind + " w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:" + kind + "><w:" + kind + " w:id=\"1\">" + inner + "</w:" + kind + "></w:" + kind + "s>";

    private static ReviewScenario Doc(string name, Builder b, params InspectionCategory[] expect) => new(name, OfficeKind.Document, b.ToBytes(), expect);

    private static ReviewScenario[] WordScenarios()
    {
        var list = new List<ReviewScenario>();
        InspectionCategory rev = InspectionCategory.Revisions;
        string who(string n) => "w:author=\"PLANTED-" + n + "\" " + When;

        // -- tracked changes: every record that carries an author --
        list.Add(Doc("rev-rpr-change", Word("<w:p><w:r><w:rPr><w:b/><w:rPrChange w:id=\"1\" " + who("rev-rpr-change") + "><w:rPr/></w:rPrChange></w:rPr><w:t>x</w:t></w:r></w:p>"), rev));
        list.Add(Doc("rev-move-ranges", Word(
            "<w:p><w:moveFromRangeStart w:id=\"3\" w:name=\"m1\" " + who("rev-move-ranges") + "/><w:moveFrom w:id=\"4\" " + who("rev-move-ranges") + "><w:r><w:t>moved</w:t></w:r></w:moveFrom><w:moveFromRangeEnd w:id=\"3\"/></w:p>"
            + "<w:p><w:moveToRangeStart w:id=\"5\" w:name=\"m1\" " + who("rev-move-ranges") + "/><w:moveTo w:id=\"6\" " + who("rev-move-ranges") + "><w:r><w:t>moved</w:t></w:r></w:moveTo><w:moveToRangeEnd w:id=\"5\"/></w:p>"), rev));
        list.Add(Doc("rev-custom-xml-range", Word("<w:customXmlInsRangeStart w:id=\"7\" " + who("rev-custom-xml-range") + "/><w:p><w:r><w:t>y</w:t></w:r></w:p><w:customXmlInsRangeEnd w:id=\"7\"/>"), rev));
        list.Add(Doc("rev-table", Word(
            "<w:tbl><w:tblPr><w:tblPrChange w:id=\"8\" " + who("rev-table") + "><w:tblPr/></w:tblPrChange></w:tblPr><w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid>"
            + "<w:tr><w:trPr><w:ins w:id=\"9\" " + who("rev-table") + "/></w:trPr><w:tc><w:tcPr><w:cellIns w:id=\"10\" " + who("rev-table") + "/></w:tcPr><w:p><w:r><w:t>c</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/>"), rev));
        list.Add(Doc("rev-section-and-paragraph", Word(
            "<w:p><w:pPr><w:pPrChange w:id=\"11\" " + who("rev-section-and-paragraph") + "><w:pPr/></w:pPrChange></w:pPr><w:r><w:t>p</w:t></w:r></w:p>",
            "<w:sectPr><w:sectPrChange w:id=\"12\" " + who("rev-section-and-paragraph") + "><w:sectPr/></w:sectPrChange></w:sectPr>"), rev));
        {
            Builder b = Word(Para("body"));
            WordPart(b, "styles.xml", "styles+xml", OdRel + "styles",
                "<w:styles " + s_wordNs + "><w:style w:type=\"paragraph\" w:styleId=\"X\"><w:name w:val=\"X\"/><w:rPr><w:b/><w:rPrChange w:id=\"1\" " + who("rev-styles") + "><w:rPr/></w:rPrChange></w:rPr></w:style></w:styles>", "rIdSt");
            list.Add(Doc("rev-styles", b, rev));
        }

        {
            Builder b = Word(Para("body"));
            WordPart(b, "numbering.xml", "numbering+xml", OdRel + "numbering",
                "<w:numbering " + s_wordNs + "><w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:pPr><w:ind w:left=\"720\"/><w:pPrChange w:id=\"1\" " + who("rev-numbering") + "><w:pPr/></w:pPrChange></w:pPr></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>", "rIdNu");
            list.Add(Doc("rev-numbering", b, rev));
        }

        {
            Builder b = Word("<w:p><w:r><w:t>n</w:t></w:r><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>");
            WordPart(b, "footnotes.xml", "footnotes+xml", OdRel + "footnotes",
                Notes("footnote", "<w:p><w:ins w:id=\"20\" " + who("rev-footnote") + "><w:r><w:t>fn</w:t></w:r></w:ins></w:p>"), "rIdFn");
            list.Add(Doc("rev-footnote", b, rev));
        }

        {
            Builder b = Word(Para("body"), "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr>");
            WordPart(b, "header1.xml", "header+xml", OdRel + "header", "<w:hdr " + s_wordNs + "><w:p><w:ins w:id=\"21\" " + who("rev-header") + "><w:r><w:t>h</w:t></w:r></w:ins></w:p></w:hdr>", "rIdH");
            list.Add(Doc("rev-header", b, rev) with { Only = [rev] });
        }

        list.Add(Doc("rev-textbox-choice-and-fallback", Word(
            "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wp:inline><wp:extent cx=\"1\" cy=\"1\"/><wp:docPr id=\"30\" name=\"tb\"/><a:graphic><a:graphicData uri=\"" + WpsNs + "\"><wps:wsp><wps:txbx><w:txbxContent><w:p><w:ins w:id=\"31\" " + who("rev-textbox-choice-and-fallback") + "><w:r><w:t>c</w:t></w:r></w:ins></w:p></w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice>"
            + "<mc:Fallback><w:pict><v:shape id=\"fb\"><v:textbox><w:txbxContent><w:p><w:ins w:id=\"32\" " + who("rev-textbox-choice-and-fallback") + "><w:r><w:t>f</w:t></w:r></w:ins></w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>"), rev));
        {
            Builder b = Word(Para("body"));
            WordPart(b, "glossary/document.xml", "document.glossary+xml", OdRel + "glossaryDocument",
                "<w:glossaryDocument " + s_wordNs + "><w:docParts><w:docPart><w:docPartPr><w:name w:val=\"g\"/></w:docPartPr><w:docPartBody><w:p><w:ins w:id=\"40\" " + who("rev-glossary") + "><w:r><w:t>gl</w:t></w:r></w:ins></w:p></w:docPartBody></w:docPart></w:docParts></w:glossaryDocument>", "rIdGl");
            list.Add(Doc("rev-glossary", b, rev) with { Kept = [rev] });
        }

        // -- permission ranges name the users allowed to edit --
        list.Add(Doc("perm-editor", Word("<w:permStart w:id=\"50\" w:ed=\"PLANTED-perm-editor@example.com\"/><w:p><w:r><w:t>editable</w:t></w:r></w:p><w:permEnd w:id=\"50\"/>"), InspectionCategory.PersonalInformation) with { Kept = [InspectionCategory.PersonalInformation] });

        // -- hidden text --
        InspectionCategory hid = InspectionCategory.HiddenText;
        string hrun(string n) => "<w:r><w:rPr><w:vanish/></w:rPr><w:t>PLANTED-" + n + "</w:t></w:r>";
        list.Add(Doc("hid-table-sdt-hyperlink", Word(
            "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid><w:tr><w:tc><w:p>" + hrun("hid-table-sdt-hyperlink") + "</w:p></w:tc></w:tr></w:tbl>"
            + "<w:p><w:sdt><w:sdtPr/><w:sdtContent>" + hrun("hid-table-sdt-hyperlink") + "</w:sdtContent></w:sdt><w:hyperlink w:anchor=\"a\">" + hrun("hid-table-sdt-hyperlink") + "</w:hyperlink></w:p>"), hid) with { Only = [hid] });
        {
            Builder b = Word("<w:p><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p><w:p><w:r><w:endnoteReference w:id=\"1\"/></w:r></w:p>", "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr>");
            WordPart(b, "footnotes.xml", "footnotes+xml", OdRel + "footnotes", Notes("footnote", "<w:p>" + hrun("hid-notes-header") + "</w:p>"), "rIdFn");
            WordPart(b, "endnotes.xml", "endnotes+xml", OdRel + "endnotes", Notes("endnote", "<w:p>" + hrun("hid-notes-header") + "</w:p>"), "rIdEn");
            WordPart(b, "header1.xml", "header+xml", OdRel + "header", "<w:hdr " + s_wordNs + "><w:p>" + hrun("hid-notes-header") + "<w:r><w:t>shown</w:t></w:r></w:p></w:hdr>", "rIdH");
            list.Add(Doc("hid-notes-header", b, hid) with { Only = [hid] });
        }

        list.Add(Doc("hid-textbox-fallback", Word(
            "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wp:inline><wp:extent cx=\"1\" cy=\"1\"/><wp:docPr id=\"30\" name=\"tb\"/><a:graphic><a:graphicData uri=\"" + WpsNs + "\"><wps:wsp><wps:txbx><w:txbxContent><w:p>" + hrun("hid-textbox-fallback") + "</w:p></w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice>"
            + "<mc:Fallback><w:pict><v:shape id=\"fb\"><v:textbox><w:txbxContent><w:p>" + hrun("hid-textbox-fallback") + "</w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>"), hid) with { Only = [hid] });
        list.Add(Doc("hid-val-spellings", Word(
            "<w:p><w:r><w:rPr><w:vanish w:val=\"on\"/></w:rPr><w:t>PLANTED-hid-val-spellings</w:t></w:r><w:r><w:rPr><w:vanish w:val=\"true\"/></w:rPr><w:t>PLANTED-hid-val-spellings</w:t></w:r><w:r><w:rPr><w:vanish w:val=\"1\"/></w:rPr><w:t>PLANTED-hid-val-spellings</w:t></w:r></w:p>"), hid) with { Only = [hid] });
        list.Add(Doc("hid-paragraph-mark-spec-vanish", Word("<w:p><w:pPr><w:rPr><w:vanish/><w:specVanish/></w:rPr></w:pPr><w:r><w:t>joined</w:t></w:r></w:p>"), hid) with { Only = [hid] });
        {
            Builder b = Word("<w:p><w:pPr><w:pStyle w:val=\"Sneaky\"/></w:pPr><w:r><w:t>PLANTED-hid-style</w:t></w:r></w:p><w:p><w:r><w:rPr><w:rStyle w:val=\"SneakyChar\"/></w:rPr><w:t>PLANTED-hid-style</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val=\"Child\"/></w:pPr><w:r><w:t>PLANTED-hid-style</w:t></w:r><w:r><w:rPr><w:vanish w:val=\"0\"/></w:rPr><w:t>shown-by-override</w:t></w:r></w:p>");
            WordPart(b, "styles.xml", "styles+xml", OdRel + "styles",
                "<w:styles " + s_wordNs + "><w:style w:type=\"paragraph\" w:styleId=\"Sneaky\"><w:name w:val=\"Sneaky\"/><w:rPr><w:vanish/></w:rPr></w:style><w:style w:type=\"character\" w:styleId=\"SneakyChar\"><w:name w:val=\"SneakyChar\"/><w:rPr><w:vanish/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Child\"><w:name w:val=\"Child\"/><w:basedOn w:val=\"Sneaky\"/></w:style></w:styles>", "rIdSt");
            list.Add(Doc("hid-style", b, hid) with { Only = [hid] });
        }

        // -- comments --
        {
            Builder b = Word("<w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>c</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p><w:p><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>");
            WordPart(b, "comments.xml", "comments+xml", OdRel + "comments", "<w:comments " + s_wordNs + "><w:comment w:id=\"0\" w:author=\"PLANTED-com-all-parts\" w:initials=\"PC\" " + When + "><w:p><w:r><w:t>PLANTED-com-all-parts</w:t></w:r></w:p></w:comment></w:comments>", "rIdC");
            WordPart(b, "commentsExtended.xml", "commentsExtended+xml", MsRel + "2011/relationships/commentsExtended", "<w15:commentsEx " + s_wordNs + "><w15:commentEx w15:paraId=\"00000001\" w15:done=\"0\"/></w15:commentsEx>", "rIdCe");
            WordPart(b, "commentsIds.xml", "commentsIds+xml", MsRel + "2016/09/relationships/commentsIds", "<w16cid:commentsIds xmlns:w16cid=\"http://schemas.microsoft.com/office/word/2016/wordml/cid\"><w16cid:commentId w16cid:paraId=\"00000001\" w16cid:durableId=\"00000002\"/></w16cid:commentsIds>", "rIdCi");
            WordPart(b, "commentsExtensible.xml", "commentsExtensible+xml", MsRel + "2018/08/relationships/commentsExtensible", "<w16cex:commentsExtensible xmlns:w16cex=\"http://schemas.microsoft.com/office/word/2018/wordml/cex\"><w16cex:commentExtensible w16cex:durableId=\"00000002\" w16cex:dateUtc=\"2024-01-01T00:00:00Z\"/></w16cex:commentsExtensible>", "rIdCx");
            WordPart(b, "people.xml", "people+xml", MsRel + "2011/relationships/people", "<w15:people " + s_wordNs + "><w15:person w15:author=\"PLANTED-com-all-parts\"><w15:presenceInfo w15:providerId=\"AD\" w15:userId=\"PLANTED-com-all-parts\"/></w15:person></w15:people>", "rIdPe");
            WordPart(b, "footnotes.xml", "footnotes+xml", OdRel + "footnotes", Notes("footnote", "<w:p><w:commentRangeStart w:id=\"1\"/><w:r><w:t>f</w:t></w:r><w:commentRangeEnd w:id=\"1\"/><w:r><w:commentReference w:id=\"1\"/></w:r></w:p>"), "rIdFn");
            list.Add(Doc("com-all-parts", b, InspectionCategory.Comments, InspectionCategory.PersonalInformation));
        }

        {
            // A comments part nothing points at: Word ignores it, but the bytes are in the file.
            Builder b = Word(Para("body"));
            b.Part("/word/comments9.xml", WCt + "comments+xml", Decl + "<w:comments " + s_wordNs + "><w:comment w:id=\"0\" w:author=\"PLANTED-com-orphan\"><w:p><w:r><w:t>PLANTED-com-orphan</w:t></w:r></w:p></w:comment></w:comments>");
            list.Add(Doc("com-orphan", b, InspectionCategory.Comments));
        }

        // -- properties and settings --
        {
            Builder b = Word(Para("body"));
            b.Rel(null, "rId2", PkgRel + "metadata/core-properties", "docProps/core.xml");
            b.Part("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml",
                Decl + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + "<dc:subject>PLANTED-prop-core</dc:subject><dc:description>PLANTED-prop-core</dc:description><cp:category>PLANTED-prop-core</cp:category><cp:contentStatus>PLANTED-prop-core</cp:contentStatus><cp:keywords>PLANTED-prop-core</cp:keywords><dc:identifier>PLANTED-prop-core</dc:identifier></cp:coreProperties>");
            list.Add(Doc("prop-core-all-fields", b, InspectionCategory.Properties));
        }

        {
            Builder b = Word(Para("body"));
            b.Rel(null, "rId3", OdRel + "extended-properties", "docProps/app.xml");
            b.Part("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml",
                Decl + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Template>PLANTED-prop-app.dotm</Template><HyperlinkBase>PLANTED-prop-app</HyperlinkBase>"
                + "<HLinks><vt:vector size=\"2\" baseType=\"variant\"><vt:variant><vt:lpwstr>PLANTED-prop-app</vt:lpwstr></vt:variant><vt:variant><vt:i4>0</vt:i4></vt:variant></vt:vector></HLinks></Properties>");
            list.Add(Doc("prop-app-links", b, InspectionCategory.Properties));
        }

        {
            // The package holds a second core-properties part that no relationship of the package points at.
            Builder b = Word(Para("body"));
            b.Part("/docProps/core2.xml", "application/vnd.openxmlformats-package.core-properties+xml",
                Decl + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>PLANTED-prop-orphan-core</dc:creator></cp:coreProperties>");
            list.Add(Doc("prop-orphan-core", b) with { ExpectNote = "not reachable" });
        }

        {
            Builder b = Word(Para("body"));
            WordPart(b, "settings.xml", "settings+xml", OdRel + "settings",
                "<w:settings " + s_wordNs + "><w:rsids><w:rsidRoot w:val=\"00AB12CD\"/><w:rsid w:val=\"00AB12CD\"/></w:rsids><w:docVars><w:docVar w:name=\"PLANTED-settings-docvar\" w:val=\"PLANTED-settings-docvar\"/></w:docVars>"
                + "<w:attachedTemplate r:id=\"rIdAt\"/></w:settings>", "rIdSe");
            b.Rel("/word/settings.xml", "rIdAt", OdRel + "attachedTemplate", "file:///C:/PLANTED-settings-docvar/Normal.dotm", external: true);
            list.Add(Doc("settings-docvars-template-rsids", b, InspectionCategory.Properties, InspectionCategory.RemoteTemplates, InspectionCategory.PersonalInformation));
        }

        {
            // A mail-merge data source: the connection string and query are stored in the settings and name servers, files and logins.
            Builder b = Word(Para("body"));
            WordPart(b, "settings.xml", "settings+xml", OdRel + "settings",
                "<w:settings " + s_wordNs + "><w:mailMerge><w:mainDocumentType w:val=\"formLetters\"/><w:connectString w:val=\"Provider=SQLOLEDB;Data Source=PLANTED-mailmerge-connection;Password=PLANTED-mailmerge-connection\"/><w:query w:val=\"SELECT * FROM PLANTED-mailmerge-connection\"/><w:dataSource r:id=\"rIdDs\"/></w:mailMerge></w:settings>", "rIdSe");
            b.Rel("/word/settings.xml", "rIdDs", OdRel + "mailMergeSource", "file:///C:/PLANTED-mailmerge-connection/people.xlsx", external: true);
            list.Add(Doc("mailmerge-connection", b, InspectionCategory.ExternalLinks));
        }

        {
            Builder b = Word(Para("body"));
            WordPart(b, "settings.xml", "settings+xml", OdRel + "settings",
                "<w:settings " + s_wordNs + "><w:mailMerge><w:mainDocumentType w:val=\"formLetters\"/><w:connectString w:val=\"Provider=SQLOLEDB;Data Source=PLANTED-mailmerge-embedded;Password=PLANTED-mailmerge-embedded\"/><w:query w:val=\"SELECT * FROM PLANTED-mailmerge-embedded\"/></w:mailMerge></w:settings>", "rIdSe");
            list.Add(Doc("mailmerge-embedded", b, InspectionCategory.ExternalLinks));
        }

        // -- custom XML --
        {
            Builder b = Word("<w:p><w:customXml w:uri=\"urn:planted\" w:element=\"PLANTED-customxml-inline\"><w:customXmlPr><w:attr w:uri=\"urn:planted\" w:name=\"n\" w:val=\"PLANTED-customxml-inline\"/></w:customXmlPr><w:r><w:t>wrapped</w:t></w:r></w:customXml></w:p>");
            list.Add(Doc("customxml-inline", b, InspectionCategory.CustomXml));
        }

        {
            Builder b = Word(Para("body"), "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr>");
            WordPart(b, "header1.xml", "header+xml", OdRel + "header", "<w:hdr " + s_wordNs + "><w:p><w:sdt><w:sdtPr><w:dataBinding w:xpath=\"/r/s\" w:storeItemID=\"{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}\"/></w:sdtPr><w:sdtContent><w:r><w:t>b</w:t></w:r></w:sdtContent></w:sdt></w:p></w:hdr>", "rIdH");
            b.Part("/customXml/item1.xml", "application/xml", Decl + "<r xmlns=\"urn:planted\"><s>PLANTED-customxml-from-package-root</s></r>");
            b.Rel(null, "rIdX", OdRel + "customXml", "customXml/item1.xml");
            list.Add(Doc("customxml-from-package-root-and-header-binding", b, InspectionCategory.CustomXml) with { Only = [InspectionCategory.CustomXml] });
        }

        // -- headers and footers --
        {
            Builder b = Word("<w:p><w:pPr><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr></w:pPr></w:p>", "<w:sectPr><w:headerReference w:type=\"first\" r:id=\"rIdH2\"/><w:titlePg/></w:sectPr>");
            WordPart(b, "header1.xml", "header+xml", OdRel + "header", "<w:hdr " + s_wordNs + "><w:p><w:r><w:t>PLANTED-hdr-sections-and-orphan</w:t></w:r></w:p></w:hdr>", "rIdH");
            WordPart(b, "header2.xml", "header+xml", OdRel + "header", "<w:hdr " + s_wordNs + "><w:p><w:r><w:t>PLANTED-hdr-sections-and-orphan</w:t></w:r></w:p></w:hdr>", "rIdH2");
            b.Part("/word/header9.xml", WCt + "header+xml", Decl + "<w:hdr " + s_wordNs + "><w:p><w:r><w:t>PLANTED-hdr-sections-and-orphan</w:t></w:r></w:p></w:hdr>");
            list.Add(Doc("hdr-sections-and-orphan", b, InspectionCategory.HeadersFooters) with { Only = [InspectionCategory.HeadersFooters] });
        }

        // -- linked content --
        InspectionCategory lnk = InspectionCategory.LinkedContent;
        list.Add(Doc("link-split-instruction", Word(
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> INCLUDE</w:instrText></w:r><w:r><w:instrText xml:space=\"preserve\">PICTURE \"C:\\PLANTED-link-split-instruction\\a.png\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>r</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>"), lnk) with { Kept = [lnk] });
        list.Add(Doc("link-lowercase-and-switches", Word(
            "<w:p><w:fldSimple w:instr=\"  includetext  &quot;C:\\PLANTED-link-lowercase-and-switches\\a.docx&quot; \\* MERGEFORMAT\"><w:r><w:t>r</w:t></w:r></w:fldSimple></w:p>"
            + "<w:p><w:fldSimple w:instr=\"DDEAUTO cmd PLANTED-link-lowercase-and-switches\"><w:r><w:t>d</w:t></w:r></w:fldSimple></w:p>"), lnk) with { Kept = [lnk] });
        {
            Builder b = Word("<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"1\" cy=\"1\"/><wp:docPr id=\"1\" name=\"p\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"p\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:link=\"rIdL\"/></pic:blipFill><pic:spPr/></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
            b.Rel("/word/document.xml", "rIdL", OdRel + "image", "file:///C:/PLANTED-link-picture/a.png", external: true);
            list.Add(Doc("link-picture", b, InspectionCategory.ExternalLinks) with { Kept = [InspectionCategory.ExternalLinks] });
        }

        {
            Builder b = Word("<w:altChunk r:id=\"rIdAc\"/>");
            b.Part("/word/afchunk.html", "text/html", "<html><body>PLANTED-altchunk</body></html>");
            b.Rel("/word/document.xml", "rIdAc", OdRel + "aFChunk", "afchunk.html");
            list.Add(Doc("altchunk", b, InspectionCategory.EmbeddedObjects) with { Kept = [InspectionCategory.EmbeddedObjects] });
        }

        // -- hidden drawing objects --
        {
            Builder b = Word("<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wp:inline><wp:extent cx=\"1\" cy=\"1\"/><wp:docPr id=\"30\" name=\"PLANTED-inv-textbox\" hidden=\"1\"/><a:graphic><a:graphicData uri=\"" + WpsNs + "\"><wps:wsp><wps:txbx><w:txbxContent><w:p><w:r><w:t>PLANTED-inv-textbox</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice>"
                + "<mc:Fallback><w:pict><v:shape id=\"fb\" style=\"visibility:hidden\"><v:textbox><w:txbxContent><w:p><w:r><w:t>PLANTED-inv-textbox</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>", "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr>");
            WordPart(b, "header1.xml", "header+xml", OdRel + "header",
                "<w:hdr " + s_wordNs + "><w:p><w:r><w:drawing><wp:anchor><wp:extent cx=\"1\" cy=\"1\"/><wp:docPr id=\"31\" name=\"PLANTED-inv-textbox\" hidden=\"1\"/><a:graphic><a:graphicData uri=\"" + WpsNs + "\"><wps:wsp/></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r><w:r><w:t>shown</w:t></w:r></w:p></w:hdr>", "rIdH");
            list.Add(Doc("inv-textbox-header", b, InspectionCategory.InvisibleContent) with { Only = [InspectionCategory.InvisibleContent] });
        }

        // -- ink --
        {
            Builder b = Word("<w:p><w:r><w:contentPart r:id=\"rIdInk\"/></w:r></w:p>");
            b.Part("/word/ink/ink1.xml", "application/inkml+xml", Decl + "<inkml:ink xmlns:inkml=\"http://www.w3.org/2003/InkML\"><inkml:trace>1 1</inkml:trace><inkml:annotation type=\"PLANTED-ink-word\">PLANTED-ink-word</inkml:annotation></inkml:ink>");
            b.Rel("/word/document.xml", "rIdInk", OdRel + "customXml", "ink/ink1.xml");
            list.Add(Doc("ink-word", b, InspectionCategory.Ink));
        }

        // -- Strict namespaces --
        {
            const string sw = "http://purl.oclc.org/ooxml/wordprocessingml/main";
            const string sr = "http://purl.oclc.org/ooxml/officeDocument/relationships";
            string sns = "xmlns:w=\"" + sw + "\" xmlns:r=\"" + sr + "\"";
            var b = new Builder();
            b.Rel(null, "rId1", "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument", "word/document.xml");
            b.Part("/word/document.xml", WCt + "document.main+xml",
                Decl + "<w:document " + sns + "><w:body><w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>Visible</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>"
                + "<w:p><w:ins w:id=\"1\" w:author=\"PLANTED-strict\" " + When + "><w:r><w:t>ins</w:t></w:r></w:ins><w:r><w:rPr><w:vanish/></w:rPr><w:t>PLANTED-strict</w:t></w:r></w:p><w:sectPr/></w:body></w:document>");
            b.Part("/word/comments.xml", WCt + "comments+xml", Decl + "<w:comments " + sns + "><w:comment w:id=\"0\" w:author=\"PLANTED-strict\"><w:p><w:r><w:t>PLANTED-strict</w:t></w:r></w:p></w:comment></w:comments>");
            b.Rel("/word/document.xml", "rIdC", "http://purl.oclc.org/ooxml/officeDocument/relationships/comments", "comments.xml");
            list.Add(Doc("strict-namespaces", b, InspectionCategory.Comments, InspectionCategory.Revisions, InspectionCategory.HiddenText));
        }

        return [.. list];
    }

    // ---- Excel ----

    private static string Cell(string r, string text) => "<c r=\"" + r + "\" t=\"inlineStr\"><is><t>" + text + "</t></is></c>";

    private static string Sheet(string rows, string tail = "", string head = "") =>
        Decl + "<worksheet xmlns=\"" + S + "\" xmlns:r=\"" + R + "\">" + head + "<sheetData>" + rows + "</sheetData>" + tail + "</worksheet>";

    private static Builder Excel(string workbookExtra, params (string Name, string State, string Xml)[] sheets)
    {
        var b = new Builder();
        b.Rel(null, "rId1", OdRel + "officeDocument", "xl/workbook.xml");
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < sheets.Length; i++)
        {
            string state = sheets[i].State.Length == 0 ? string.Empty : " state=\"" + sheets[i].State + "\"";
            sb.Append("<sheet name=\"").Append(sheets[i].Name).Append("\" sheetId=\"").Append(i + 1).Append('"').Append(state).Append(" r:id=\"rIdS").Append(i + 1).Append("\"/>");
            b.Part("/xl/worksheets/sheet" + (i + 1) + ".xml", SCt + "worksheet+xml", sheets[i].Xml);
            b.Rel("/xl/workbook.xml", "rIdS" + (i + 1), OdRel + "worksheet", "worksheets/sheet" + (i + 1) + ".xml");
        }

        b.Part("/xl/workbook.xml", SCt + "sheet.main+xml", Decl + "<workbook xmlns=\"" + S + "\" xmlns:r=\"" + R + "\" xmlns:mc=\"" + Mc + "\" xmlns:x15ac=\"http://schemas.microsoft.com/office/spreadsheetml/2010/11/ac\" xmlns:xr=\"http://schemas.microsoft.com/office/spreadsheetml/2014/revision\" mc:Ignorable=\"x15ac xr\">" + workbookExtra.Split('\u0001')[0] + "<sheets>" + sb + "</sheets>" + (workbookExtra.Contains('\u0001', StringComparison.Ordinal) ? workbookExtra.Split('\u0001')[1] : string.Empty) + "</workbook>");
        return b;
    }

    private static ReviewScenario Book(string name, Builder b, params InspectionCategory[] expect) => new(name, OfficeKind.Workbook, b.ToBytes(), expect);

    private static ReviewScenario[] ExcelScenarios()
    {
        var list = new List<ReviewScenario>();
        string visible = Sheet("<row r=\"1\">" + Cell("A1", "visible") + "</row>");

        {
            // Cleared cells keep their text in the shared string table, and a formula in a cleared cell stays in the calculation chain.
            Builder b = Excel(string.Empty, ("S", string.Empty, Sheet("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row><row r=\"2\" hidden=\"1\"><c r=\"A2\" t=\"s\"><v>1</v></c><c r=\"B2\"><f>1+1</f><v>2</v></c></row>")));
            b.Part("/xl/sharedStrings.xml", SCt + "sharedStrings+xml", Decl + "<sst xmlns=\"" + S + "\" count=\"2\" uniqueCount=\"2\"><si><t>visible</t></si><si><t>PLANTED-xl-hidden-row-shared-string</t></si></sst>");
            b.Rel("/xl/workbook.xml", "rIdSs", OdRel + "sharedStrings", "sharedStrings.xml");
            b.Part("/xl/calcChain.xml", SCt + "calcChain+xml", Decl + "<calcChain xmlns=\"" + S + "\"><c r=\"B2\" i=\"1\"/></calcChain>");
            b.Rel("/xl/workbook.xml", "rIdCc", OdRel + "calcChain", "calcChain.xml");
            list.Add(Book("xl-hidden-row-shared-string", b, InspectionCategory.HiddenRowsColumns) with { Gone = ["xl/calcChain.xml"] });
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, Sheet("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>")), ("Hid", "hidden", Sheet("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>1</v></c></row>")));
            b.Part("/xl/sharedStrings.xml", SCt + "sharedStrings+xml", Decl + "<sst xmlns=\"" + S + "\" count=\"2\" uniqueCount=\"2\"><si><t>visible</t></si><si><t>PLANTED-xl-hidden-sheet-shared-string</t></si></sst>");
            b.Rel("/xl/workbook.xml", "rIdSs", OdRel + "sharedStrings", "sharedStrings.xml");
            list.Add(Book("xl-hidden-sheet-shared-string", b, InspectionCategory.HiddenSheets));
        }

        {
            // A hidden sheet with its own comments, drawing, image and table: everything only it used has to go with it.
            Builder b = Excel(string.Empty, ("S", string.Empty, visible), ("Hid", "veryHidden",
                Sheet("<row r=\"1\">" + Cell("A1", "PLANTED-xl-hidden-sheet-parts") + "</row>", "<drawing r:id=\"rIdD\"/><legacyDrawing r:id=\"rIdV\"/><tableParts count=\"1\"><tablePart r:id=\"rIdT\"/></tableParts>")));
            b.Part("/xl/comments1.xml", SCt + "comments+xml", Decl + "<comments xmlns=\"" + S + "\"><authors><author>PLANTED-xl-hidden-sheet-parts</author></authors><commentList><comment ref=\"A1\" authorId=\"0\"><text><r><t>PLANTED-xl-hidden-sheet-parts</t></r></text></comment></commentList></comments>");
            b.Rel("/xl/worksheets/sheet2.xml", "rIdN", OdRel + "comments", "../comments1.xml");
            b.Part("/xl/drawings/vmlDrawing1.vml", SCt + "vmlDrawing", Decl + "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"><v:shape id=\"s1\"><x:ClientData ObjectType=\"Note\"><x:Row>0</x:Row></x:ClientData></v:shape></xml>");
            b.Rel("/xl/worksheets/sheet2.xml", "rIdV", OdRel + "vmlDrawing", "../drawings/vmlDrawing1.vml");
            b.Part("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml", Decl + "<xdr:wsDr xmlns:xdr=\"" + Xdr + "\" xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\"><xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx=\"1\" cy=\"1\"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"2\" name=\"PLANTED-xl-hidden-sheet-parts\"/><xdr:cNvPicPr/></xdr:nvPicPr><xdr:blipFill><a:blip r:embed=\"rIdI\"/></xdr:blipFill><xdr:spPr/></xdr:pic><xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>");
            b.Rel("/xl/worksheets/sheet2.xml", "rIdD", OdRel + "drawing", "../drawings/drawing1.xml");
            var marked = new byte[Png.Length + 24];
            Png.CopyTo(marked, 0);
            System.Text.Encoding.ASCII.GetBytes("PLANTED-xl-hidden-sheet-parts").AsSpan(0, 24).CopyTo(marked.AsSpan(Png.Length));
            b.Binary("/xl/media/image1.png", "image/png", marked);
            b.Rel("/xl/drawings/drawing1.xml", "rIdI", OdRel + "image", "../media/image1.png");
            b.Part("/xl/tables/table1.xml", SCt + "table+xml", Decl + "<table xmlns=\"" + S + "\" id=\"1\" name=\"PLANTED_xl_hidden_sheet_parts\" displayName=\"PLANTED_xl_hidden_sheet_parts\" ref=\"A1:A2\"><tableColumns count=\"1\"><tableColumn id=\"1\" name=\"c\"/></tableColumns></table>");
            b.Rel("/xl/worksheets/sheet2.xml", "rIdT", OdRel + "table", "../tables/table1.xml");
            list.Add(Book("xl-hidden-sheet-parts", b, InspectionCategory.HiddenSheets, InspectionCategory.Comments));
        }

        {
            // A sheet name is stored in more places than the sheet list.
            Builder b = Excel("<bookViews><workbookView activeTab=\"0\"/></bookViews><customWorkbookViews><customWorkbookView name=\"PLANTED-xl-hidden-sheet-name-views\" guid=\"{11111111-1111-1111-1111-111111111111}\" windowWidth=\"1\" windowHeight=\"1\" activeSheetId=\"2\"/></customWorkbookViews>\u0001<definedNames><definedName name=\"_xlnm.Print_Area\" localSheetId=\"1\">'PLANTED-xl-hidden-sheet-name'!$A$1</definedName></definedNames>",
                ("S", string.Empty, visible), ("PLANTED-xl-hidden-sheet-name", "hidden", Sheet("<row r=\"1\">" + Cell("A1", "x") + "</row>")));
            b.Rel(null, "rId3", OdRel + "extended-properties", "docProps/app.xml");
            b.Part("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml", Decl + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><TitlesOfParts><vt:vector size=\"2\" baseType=\"lpstr\"><vt:lpstr>S</vt:lpstr><vt:lpstr>PLANTED-xl-hidden-sheet-name</vt:lpstr></vt:vector></TitlesOfParts></Properties>");
            list.Add(Book("xl-hidden-sheet-name-everywhere", b, InspectionCategory.HiddenSheets, InspectionCategory.Properties));
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, Sheet("<row r=\"1\">" + Cell("A1", "v") + "</row>", "<legacyDrawing r:id=\"rIdV\"/>")));
            b.Part("/xl/comments1.xml", SCt + "comments+xml", Decl + "<comments xmlns=\"" + S + "\"><authors><author>PLANTED-xl-comments-threaded</author></authors><commentList><comment ref=\"A1\" authorId=\"0\"><text><r><t>PLANTED-xl-comments-threaded</t></r></text></comment></commentList></comments>");
            b.Rel("/xl/worksheets/sheet1.xml", "rIdN", OdRel + "comments", "../comments1.xml");
            b.Part("/xl/drawings/vmlDrawing1.vml", SCt + "vmlDrawing", Decl + "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"><v:shape id=\"s1\"><x:ClientData ObjectType=\"Note\"><x:Row>0</x:Row></x:ClientData></v:shape></xml>");
            b.Rel("/xl/worksheets/sheet1.xml", "rIdV", OdRel + "vmlDrawing", "../drawings/vmlDrawing1.vml");
            b.Part("/xl/threadedComments/threadedComment1.xml", "application/vnd.ms-excel.threadedcomments+xml", Decl + "<ThreadedComments xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments\"><threadedComment ref=\"A1\" personId=\"{11111111-1111-1111-1111-111111111111}\" id=\"{22222222-2222-2222-2222-222222222222}\"><text>PLANTED-xl-comments-threaded</text></threadedComment></ThreadedComments>");
            b.Rel("/xl/worksheets/sheet1.xml", "rIdTc", MsRel + "2017/10/relationships/threadedComment", "../threadedComments/threadedComment1.xml");
            b.Part("/xl/persons/person.xml", "application/vnd.ms-excel.person+xml", Decl + "<personList xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments\"><person displayName=\"PLANTED-xl-comments-threaded\" id=\"{11111111-1111-1111-1111-111111111111}\" userId=\"PLANTED-xl-comments-threaded\" providerId=\"None\"/></personList>");
            b.Rel("/xl/workbook.xml", "rIdPe", MsRel + "2017/10/relationships/person", "persons/person.xml");
            list.Add(Book("xl-comments-threaded", b, InspectionCategory.Comments, InspectionCategory.PersonalInformation));
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, visible));
            b.Part("/xl/comments9.xml", SCt + "comments+xml", Decl + "<comments xmlns=\"" + S + "\"><authors><author>PLANTED-xl-comments-orphan</author></authors><commentList/></comments>");
            list.Add(Book("xl-comments-orphan", b) with { ExpectNote = "not reachable" });
        }

        {
            // Hidden rows and columns: the cell text can also sit in a hyperlink, a note and a data validation of the cleared cell.
            Builder b = Excel(string.Empty, ("S", string.Empty, Sheet(
                "<row r=\"1\">" + Cell("A1", "v") + "</row><row r=\"2\" hidden=\"1\">" + Cell("A2", "PLANTED-xl-hidden-row-companions") + "</row>",
                "<dataValidations count=\"1\"><dataValidation type=\"list\" sqref=\"A2\"><formula1>\"PLANTED-xl-hidden-row-companions,b\"</formula1></dataValidation></dataValidations><hyperlinks><hyperlink ref=\"A2\" display=\"PLANTED-xl-hidden-row-companions\" tooltip=\"PLANTED-xl-hidden-row-companions\" location=\"S!A1\"/></hyperlinks>",
                "<cols><col min=\"3\" max=\"3\" width=\"9\" hidden=\"1\" customWidth=\"1\"/></cols>")));
            list.Add(Book("xl-hidden-row-companions", b, InspectionCategory.HiddenRowsColumns));
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, Sheet("<row r=\"1\">" + Cell("A1", "v") + "</row><row r=\"3\">" + Cell("A3", "PLANTED-xl-zero-height-default") + "</row>", string.Empty, string.Empty).Replace("<sheetData>", "<sheetFormatPr defaultRowHeight=\"15\" zeroHeight=\"1\"/><sheetData>", StringComparison.Ordinal)));
            list.Add(Book("xl-zero-height-default", b, InspectionCategory.HiddenRowsColumns) with { Gap = "Rows hidden by default (sheetFormatPr zeroHeight) are not recognised; how Excel marks the visible rows needs the Office run." });
        }

        {
            Builder b = Excel(
                "<fileSharing userName=\"PLANTED-xl-personal\"/><mc:AlternateContent><mc:Choice Requires=\"x15ac\"><x15ac:absPath url=\"C:\\Users\\PLANTED-xl-personal\\d\"/></mc:Choice></mc:AlternateContent><x15ac:absPath url=\"C:\\Users\\PLANTED-xl-personal\\d2\"/>\u0001<definedNames><definedName name=\"N\" comment=\"PLANTED-xl-personal\">S!$A$1</definedName></definedNames><oleSize ref=\"A1\"/>",
                ("S", string.Empty, Sheet("<row r=\"1\">" + Cell("A1", "v") + "</row>", "<pageSetup orientation=\"portrait\" r:id=\"rIdPr\"/><scenarios><scenario name=\"sc\" count=\"1\" user=\"PLANTED-xl-personal\" comment=\"PLANTED-xl-personal\"><inputCells r=\"A1\" val=\"1\"/></scenario></scenarios>".Replace("<scenarios>", "<scenarios>", StringComparison.Ordinal))));
            b.Binary("/xl/printerSettings/printerSettings1.bin", SCt + "printerSettings", [1, 2, 3]);
            b.Rel("/xl/worksheets/sheet1.xml", "rIdPr", OdRel + "printerSettings", "../printerSettings/printerSettings1.bin");
            list.Add(Book("xl-personal", b, InspectionCategory.PersonalInformation, InspectionCategory.Properties));
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, visible));
            b.Rel("/xl/workbook.xml", "rIdEx", OdRel + "externalLink", "externalLinks/externalLink1.xml");
            b.Part("/xl/externalLinks/externalLink1.xml", SCt + "externalLink+xml", Decl + "<externalLink xmlns=\"" + S + "\" xmlns:r=\"" + R + "\"><externalBook r:id=\"rId1\"><sheetNames><sheetName val=\"PLANTED-xl-external\"/></sheetNames><sheetDataSet><sheetData sheetId=\"0\"><row r=\"1\"><cell r=\"A1\" t=\"str\"><v>PLANTED-xl-external</v></cell></row></sheetData></sheetDataSet></externalBook></externalLink>");
            b.Rel("/xl/externalLinks/externalLink1.xml", "rId1", OdRel + "externalLinkPath", "file:///C:/PLANTED-xl-external/book.xlsx", external: true);
            list.Add(Book("xl-external-link-cache", b, InspectionCategory.ExternalLinks));
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, visible));
            b.Part("/xl/connections.xml", SCt + "connections+xml", Decl + "<connections xmlns=\"" + S + "\"><connection id=\"1\" name=\"PLANTED-xl-connection\" type=\"1\" refreshedVersion=\"6\"><dbPr connection=\"Provider=X;Password=PLANTED-xl-connection\" command=\"select 1\"/></connection></connections>");
            b.Rel("/xl/workbook.xml", "rIdCn", OdRel + "connections", "connections.xml");
            list.Add(Book("xl-connection", b, InspectionCategory.DataConnections));
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, visible));
            b.Part("/xl/pivotCache/pivotCacheRecords1.xml", SCt + "pivotCacheRecords+xml", Decl + "<pivotCacheRecords xmlns=\"" + S + "\" count=\"1\"><r><s v=\"PLANTED-xl-pivot-cache\"/></r></pivotCacheRecords>");
            b.Part("/xl/pivotCache/pivotCacheDefinition1.xml", SCt + "pivotCacheDefinition+xml", Decl + "<pivotCacheDefinition xmlns=\"" + S + "\" xmlns:r=\"" + R + "\" r:id=\"rId1\"><cacheSource type=\"worksheet\"><worksheetSource ref=\"A1:A2\" sheet=\"S\"/></cacheSource><cacheFields count=\"1\"><cacheField name=\"f\"/></cacheFields></pivotCacheDefinition>");
            b.Rel("/xl/pivotCache/pivotCacheDefinition1.xml", "rId1", OdRel + "pivotCacheRecords", "pivotCacheRecords1.xml");
            b.Rel("/xl/workbook.xml", "rIdPc", OdRel + "pivotCacheDefinition", "pivotCache/pivotCacheDefinition1.xml");
            list.Add(Book("xl-pivot-cache", b, InspectionCategory.PivotCaches) with { Kept = [InspectionCategory.PivotCaches] });
        }

        {
            // A chart that reads a hidden sheet carries a copy of the values it shows.
            Builder b = Excel(string.Empty, ("S", string.Empty, Sheet("<row r=\"1\">" + Cell("A1", "v") + "</row>", "<drawing r:id=\"rIdD\"/>")), ("Hid", "hidden", Sheet("<row r=\"1\"><c r=\"A1\"><v>5</v></c></row>")));
            b.Part("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml", Decl + "<xdr:wsDr xmlns:xdr=\"" + Xdr + "\" xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\"><xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx=\"1\" cy=\"1\"/><xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"chart\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1\" cy=\"1\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rIdCh\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>");
            b.Rel("/xl/worksheets/sheet1.xml", "rIdD", OdRel + "drawing", "../drawings/drawing1.xml");
            b.Part("/xl/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", Decl + "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart><c:plotArea><c:barChart><c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:tx><c:strRef><c:f>Hid!$B$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>PLANTED-xl-chart-cache</c:v></c:pt></c:strCache></c:strRef></c:tx><c:val><c:numRef><c:f>Hid!$A$1</c:f><c:numCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>5</c:v></c:pt></c:numCache></c:numRef></c:val></c:ser></c:barChart></c:plotArea></c:chart></c:chartSpace>");
            b.Rel("/xl/drawings/drawing1.xml", "rIdCh", OdRel + "chart", "../charts/chart1.xml");
            list.Add(Book("xl-chart-reads-hidden-sheet", b, InspectionCategory.HiddenSheets) with { Kept = [InspectionCategory.HiddenSheets] });
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, visible));
            b.Part("/xl/revisions/userNames.xml", SCt + "userNames+xml", Decl + "<users xmlns=\"" + S + "\" count=\"1\"><userInfo guid=\"{11111111-1111-1111-1111-111111111111}\" name=\"PLANTED-xl-revisions\" id=\"1\" dateTime=\"2024-01-01T00:00:00\"/></users>");
            b.Rel("/xl/workbook.xml", "rIdUn", OdRel + "usernames", "revisions/userNames.xml");
            list.Add(Book("xl-revisions-user-names", b, InspectionCategory.Revisions));
        }

        {
            Builder b = Excel(string.Empty, ("S", string.Empty, Sheet("<row r=\"1\">" + Cell("A1", "v") + "</row>", "<tableParts count=\"1\"><tablePart r:id=\"rIdT\"/></tableParts>")));
            b.Part("/xl/tables/table1.xml", SCt + "table+xml", Decl + "<table xmlns=\"" + S + "\" id=\"1\" name=\"T\" displayName=\"T\" ref=\"A1:A2\" comment=\"PLANTED-xl-table-comment\"><tableColumns count=\"1\"><tableColumn id=\"1\" name=\"c\"/></tableColumns></table>");
            b.Rel("/xl/worksheets/sheet1.xml", "rIdT", OdRel + "table", "../tables/table1.xml");
            list.Add(Book("xl-table-comment", b, InspectionCategory.Properties));
        }

        {
            Builder b = Excel("\u0001<definedNames><definedName name=\"Sneaky\" hidden=\"1\" comment=\"PLANTED-xl-hidden-name\">S!$A$1</definedName></definedNames>", ("S", string.Empty, visible));
            list.Add(Book("xl-hidden-name", b, InspectionCategory.HiddenNames, InspectionCategory.Properties));
        }

        {
            // A sheet can hold the sheet-level header/footer text as well as the first-page and even-page variants.
            Builder b = Excel(string.Empty, ("S", string.Empty, Sheet("<row r=\"1\">" + Cell("A1", "v") + "</row>", "<headerFooter differentFirst=\"1\"><firstHeader>&amp;LPLANTED-xl-header-variants</firstHeader><evenFooter>&amp;LPLANTED-xl-header-variants</evenFooter></headerFooter>")));
            list.Add(Book("xl-header-variants", b, InspectionCategory.HeadersFooters));
        }

        return [.. list];
    }

    // ---- PowerPoint ----

    private static string SpTree(string shapes) => "<p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + shapes + "</p:spTree></p:cSld>";

    private static string Shape(int id, string name, long x, long y, string text, string extra = "") =>
        "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\"" + extra + "/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"1000000\" cy=\"500000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr/><a:p><a:r><a:t>" + text + "</a:t></a:r></a:p></p:txBody></p:sp>";

    private static string Slide(string shapes, string rootAttrs = "", string tail = "") =>
        Decl + "<p:sld xmlns:p=\"" + PNs + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\" xmlns:mc=\"" + Mc + "\"" + rootAttrs + ">" + SpTree(shapes) + tail + "</p:sld>";

    // A presentation with a master, a layout and the given slides.
    private static Builder Deck(params (string Xml, string Extra)[] slides) => DeckWith(null, slides);

    private static Builder DeckWith(string? layout, params (string Xml, string Extra)[] slides)
    {
        var b = new Builder();
        b.Rel(null, "rId1", OdRel + "officeDocument", "ppt/presentation.xml");
        b.Part("/ppt/slideMasters/slideMaster1.xml", PCt + "slideMaster+xml", Decl + "<p:sldMaster xmlns:p=\"" + PNs + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\">" + SpTree(string.Empty) + "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/><p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst></p:sldMaster>");
        b.Part("/ppt/slideLayouts/slideLayout1.xml", PCt + "slideLayout+xml", layout ?? Decl + "<p:sldLayout xmlns:p=\"" + PNs + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\" type=\"blank\">" + SpTree(string.Empty) + "</p:sldLayout>");
        b.Rel("/ppt/slideMasters/slideMaster1.xml", "rId1", OdRel + "slideLayout", "../slideLayouts/slideLayout1.xml");
        b.Rel("/ppt/slideLayouts/slideLayout1.xml", "rId1", OdRel + "slideMaster", "../slideMasters/slideMaster1.xml");
        var ids = new System.Text.StringBuilder();
        for (int i = 0; i < slides.Length; i++)
        {
            string n = "/ppt/slides/slide" + (i + 1) + ".xml";
            b.Part(n, PCt + "slide+xml", slides[i].Xml);
            b.Rel(n, "rId1", OdRel + "slideLayout", "../slideLayouts/slideLayout1.xml");
            b.Rel("/ppt/presentation.xml", "rIdS" + (i + 1), OdRel + "slide", "slides/slide" + (i + 1) + ".xml");
            ids.Append("<p:sldId id=\"").Append(256 + i).Append("\" r:id=\"rIdS").Append(i + 1).Append("\"/>");
        }

        b.Rel("/ppt/presentation.xml", "rIdM", OdRel + "slideMaster", "slideMasters/slideMaster1.xml");
        b.Part("/ppt/presentation.xml", PCt + "presentation.main+xml", Decl + "<p:presentation xmlns:p=\"" + PNs + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\"><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rIdM\"/></p:sldMasterIdLst><p:sldIdLst>" + ids + "</p:sldIdLst><p:sldSz cx=\"9144000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
        return b;
    }

    private static ReviewScenario Show(string name, Builder b, params InspectionCategory[] expect) => new(name, OfficeKind.Presentation, b.ToBytes(), expect);

    private static ReviewScenario[] PowerPointScenarios()
    {
        var list = new List<ReviewScenario>();
        string plain = Slide(Shape(2, "t", 100000, 100000, "visible"));

        {
            Builder b = Deck((plain, string.Empty));
            b.Rel(null, "rId9", "http://schemas.microsoft.com/office/2016/11/relationships/changesInfo", "ppt/changesInfos/changesInfo1.xml");
            b.Part("/ppt/changesInfos/changesInfo1.xml", "application/vnd.ms-powerpoint.changesinfo+xml", Decl + "<pc:chgInfo xmlns:pc=\"http://schemas.microsoft.com/office/powerpoint/2013/main/command\"><pc:docChgLst><pc:chg dt=\"2024-01-01T00:00:00Z\" v=\"1\"><pc:docMk/></pc:chg><pc:chg dt=\"2024-01-01T00:00:00Z\" v=\"1\" userId=\"PLANTED-ppt-changes-info\"/></pc:docChgLst></pc:chgInfo>");
            b.Rel("/ppt/presentation.xml", "rIdRi", "http://schemas.microsoft.com/office/2015/10/relationships/revisionInfo", "revisionInfo.xml");
            b.Part("/ppt/revisionInfo.xml", "application/vnd.ms-powerpoint.revisioninfo+xml", Decl + "<p1510:revInfo xmlns:p1510=\"http://schemas.microsoft.com/office/powerpoint/2015/10/main\"><p1510:revLst><p1510:client id=\"{11111111-1111-1111-1111-111111111111}\" v=\"1\" name=\"PLANTED-ppt-changes-info\"/></p1510:revLst></p1510:revInfo>");
            list.Add(Show("ppt-changes-info", b, InspectionCategory.Revisions));
        }

        {
            // A hidden slide: its notes, picture, layout and the names of its parts have to go; the visible slide stays.
            Builder b = Deck((plain, string.Empty), (Slide(Shape(2, "h", 100000, 100000, "PLANTED-ppt-hidden-slide") + "<p:pic><p:nvPicPr><p:cNvPr id=\"3\" name=\"p\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed=\"rIdI\"/></p:blipFill><p:spPr/></p:pic>", " show=\"0\"", "<p:custDataLst><p:custData r:id=\"rIdCd\"/></p:custDataLst>"), string.Empty));
            var marked = new byte[Png.Length + 24];
            Png.CopyTo(marked, 0);
            System.Text.Encoding.ASCII.GetBytes("PLANTED-ppt-hidden-slide").AsSpan(0, 24).CopyTo(marked.AsSpan(Png.Length));
            b.Binary("/ppt/media/image1.png", "image/png", marked);
            b.Rel("/ppt/slides/slide2.xml", "rIdI", OdRel + "image", "../media/image1.png");
            b.Part("/customXml/item7.xml", "application/xml", Decl + "<r>PLANTED-ppt-hidden-slide</r>");
            b.Rel("/ppt/slides/slide2.xml", "rIdCd", OdRel + "customXml", "../../customXml/item7.xml");
            b.Part("/ppt/notesSlides/notesSlide2.xml", PCt + "notesSlide+xml", Decl + "<p:notes xmlns:p=\"" + PNs + "\" xmlns:a=\"" + A + "\">" + SpTree(Shape(2, "n", 1, 1, "PLANTED-ppt-hidden-slide")) + "</p:notes>");
            b.Rel("/ppt/slides/slide2.xml", "rIdNs", OdRel + "notesSlide", "../notesSlides/notesSlide2.xml");
            b.Rel("/ppt/notesSlides/notesSlide2.xml", "rId1", OdRel + "slide", "../slides/slide2.xml");
            list.Add(Show("ppt-hidden-slide-parts", b, InspectionCategory.HiddenSlides, InspectionCategory.SpeakerNotes, InspectionCategory.CustomXml));
        }

        {
            Builder b = Deck((plain, string.Empty));
            b.Part("/ppt/notesMasters/notesMaster1.xml", PCt + "notesMaster+xml", Decl + "<p:notesMaster xmlns:p=\"" + PNs + "\" xmlns:a=\"" + A + "\">" + SpTree(Shape(2, "n", 1, 1, "PLANTED-ppt-notes-master")) + "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/></p:notesMaster>");
            b.Rel("/ppt/presentation.xml", "rIdNm", OdRel + "notesMaster", "notesMasters/notesMaster1.xml");
            b.Part("/ppt/handoutMasters/handoutMaster1.xml", PCt + "handoutMaster+xml", Decl + "<p:handoutMaster xmlns:p=\"" + PNs + "\" xmlns:a=\"" + A + "\">" + SpTree(Shape(2, "h", 1, 1, "PLANTED-ppt-notes-master")) + "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/></p:handoutMaster>");
            b.Rel("/ppt/presentation.xml", "rIdHm", OdRel + "handoutMaster", "handoutMasters/handoutMaster1.xml");
            list.Add(Show("ppt-notes-master", b) with { Gap = "Text on the notes master and the handout master is neither reported nor removed (they hold page furniture, and Office's inspector does not look there either)." });
        }

        {
            Builder b = Deck((Slide(Shape(2, "t", 100000, 100000, "visible"), string.Empty, "<p:extLst><p:ext uri=\"{6950BFC3-D8DA-4A85-94F7-54DA5524770B}\"><p188:commentRel xmlns:p188=\"http://schemas.microsoft.com/office/powerpoint/2018/8/main\" r:id=\"rIdMc\"/></p:ext></p:extLst>"), string.Empty));
            b.Part("/ppt/comments/modernComment_100_1.xml", "application/vnd.ms-powerpoint.comments+xml", Decl + "<p188:cmLst xmlns:p188=\"http://schemas.microsoft.com/office/powerpoint/2018/8/main\"><p188:cm id=\"{11111111-1111-1111-1111-111111111111}\" authorId=\"{66666666-6666-6666-6666-666666666666}\" created=\"2024-01-01T00:00:00.000\"><p188:txBody><a:bodyPr xmlns:a=\"" + A + "\"/><a:p xmlns:a=\"" + A + "\"><a:r><a:t>PLANTED-ppt-comments-modern</a:t></a:r></a:p></p188:txBody></p188:cm></p188:cmLst>");
            b.Rel("/ppt/slides/slide1.xml", "rIdMc", MsRel + "2018/10/relationships/comments", "../comments/modernComment_100_1.xml");
            b.Part("/ppt/authors.xml", "application/vnd.ms-powerpoint.authors+xml", Decl + "<p188:authorLst xmlns:p188=\"http://schemas.microsoft.com/office/powerpoint/2018/8/main\"><p188:author id=\"{66666666-6666-6666-6666-666666666666}\" name=\"PLANTED-ppt-comments-modern\" initials=\"M\" userId=\"PLANTED-ppt-comments-modern\" providerId=\"None\"/></p188:authorLst>");
            b.Rel("/ppt/presentation.xml", "rIdAu", MsRel + "2018/10/relationships/authors", "authors.xml");
            list.Add(Show("ppt-comments-modern-ext", b, InspectionCategory.Comments));
        }

        {
            Builder b = Deck((plain, string.Empty));
            b.Part("/ppt/commentAuthors.xml", PCt + "commentAuthors+xml", Decl + "<p:cmAuthorLst xmlns:p=\"" + PNs + "\"><p:cmAuthor id=\"0\" name=\"PLANTED-ppt-comments-legacy\" initials=\"L\" lastIdx=\"1\" clrIdx=\"0\"/></p:cmAuthorLst>");
            b.Rel("/ppt/presentation.xml", "rIdCa", OdRel + "commentAuthors", "commentAuthors.xml");
            b.Part("/ppt/comments/comment1.xml", PCt + "comments+xml", Decl + "<p:cmLst xmlns:p=\"" + PNs + "\"><p:cm authorId=\"0\" dt=\"2024-01-01T00:00:00.000\" idx=\"1\"><p:pos x=\"1\" y=\"1\"/><p:text>PLANTED-ppt-comments-legacy</p:text></p:cm></p:cmLst>");
            b.Rel("/ppt/slides/slide1.xml", "rIdLc", OdRel + "comments", "../comments/comment1.xml");
            list.Add(Show("ppt-comments-legacy", b, InspectionCategory.Comments));
        }

        {
            // Tags are free-form name/value pairs that add-ins store on slides and the presentation.
            Builder b = Deck((Slide(Shape(2, "t", 100000, 100000, "visible")).Replace("</p:spTree></p:cSld>", "</p:spTree><p:custDataLst><p:tags r:id=\"rIdTg\"/></p:custDataLst></p:cSld>", StringComparison.Ordinal), string.Empty));
            b.Part("/ppt/tags/tag1.xml", PCt + "tags+xml", Decl + "<p:tagLst xmlns:p=\"" + PNs + "\"><p:tag name=\"PLANTED-ppt-tags\" val=\"PLANTED-ppt-tags\"/></p:tagLst>");
            b.Rel("/ppt/slides/slide1.xml", "rIdTg", OdRel + "tags", "../tags/tag1.xml");
            list.Add(Show("ppt-tags", b, InspectionCategory.CustomXml));
        }

        {
            // Off-slide shapes in the layout and the master are never shown on a slide either.
            Builder b = DeckWith(Decl + "<p:sldLayout xmlns:p=\"" + PNs + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\" type=\"blank\">" + SpTree(Shape(3, "hid", 100000, 100000, "PLANTED-ppt-layout-hidden-shape", " hidden=\"1\"")) + "</p:sldLayout>", (plain, string.Empty));
            list.Add(Show("ppt-layout-hidden-shape", b, InspectionCategory.InvisibleContent));
            Builder o = DeckWith(Decl + "<p:sldLayout xmlns:p=\"" + PNs + "\" xmlns:r=\"" + R + "\" xmlns:a=\"" + A + "\" type=\"blank\">" + SpTree(Shape(2, "off", 20000000, 100000, "PLANTED-ppt-layout-off-slide")) + "</p:sldLayout>", (plain, string.Empty));
            list.Add(Show("ppt-layout-off-slide", o, InspectionCategory.OffSlideContent) with { Gap = "Shapes outside the slide on layouts and masters are not reported; templates park instructions there on purpose." });
        }

        {
            Builder b = Deck((Slide(Shape(2, "t", 100000, 100000, "visible") + "<p:contentPart r:id=\"rIdInk\"/>"), string.Empty));
            b.Part("/ppt/ink/ink1.xml", "application/inkml+xml", Decl + "<inkml:ink xmlns:inkml=\"http://www.w3.org/2003/InkML\"><inkml:annotation type=\"PLANTED-ppt-ink\">PLANTED-ppt-ink</inkml:annotation></inkml:ink>");
            b.Rel("/ppt/slides/slide1.xml", "rIdInk", OdRel + "customXml", "../ink/ink1.xml");
            list.Add(Show("ppt-ink", b, InspectionCategory.Ink));
        }

        {
            // Shapes that are off the slide only in part, grouped, rotated, or hidden inside a group.
            Builder b = Deck((Slide(Shape(2, "t", 100000, 100000, "visible")
                + "<p:grpSp><p:nvGrpSpPr><p:cNvPr id=\"5\" name=\"g\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + Shape(6, "inner", 100000, 100000, "PLANTED-ppt-group-hidden", " hidden=\"1\"") + "</p:grpSp>"), string.Empty));
            list.Add(Show("ppt-hidden-in-group", b, InspectionCategory.InvisibleContent));
        }

        {
            // A hidden slide that the presentation's sections and custom shows also name.
            Builder b = Deck((plain, string.Empty), (Slide(Shape(2, "h", 100000, 100000, "PLANTED-ppt-hidden-slide-lists"), " show=\"0\""), string.Empty));
            list.Add(Show("ppt-hidden-slide-lists", b, InspectionCategory.HiddenSlides));
        }

        return [.. list];
    }

    private static ReviewScenario[] Build() => [.. WordScenarios(), .. ExcelScenarios(), .. PowerPointScenarios()];
}
