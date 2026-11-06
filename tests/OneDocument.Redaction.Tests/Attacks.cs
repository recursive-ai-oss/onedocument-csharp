using System.Text;
using RecursiveAI.OneArchive.Cfb;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Redaction.Tests;

/// <summary>One adversarial input: a small package that hides <see cref="Attacks.T"/> in a way a reader can still read it back.</summary>
public sealed record AttackCase(string Name, OfficeKind Kind, byte[] Input, string Secret = Attacks.T, bool IgnoreCase = false, string? Password = null);

/// <summary>Builders for the adversarial inputs. Each hides the secret differently; the tests in <c>AttackTests</c> run them all.</summary>
internal static class Attacks
{
    public const string T = "QUOKKA4419";

    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
    private const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
    private const string Wml = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
    private const string Sml = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
    private const string SmlNs = "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
    private const string Mc = "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"";

    public static byte[] Docx(string body, Action<PackageBuilder>? extra = null, string rootAttributes = "", string? mainContentType = null)
    {
        var b = new PackageBuilder();
        b.Add("/word/document.xml", mainContentType ?? Wml + "document.main+xml", Decl + "<w:document " + W + " " + rootAttributes + "><w:body>" + body + "</w:body></w:document>")
            .Rel(null, "rId1", "officeDocument", "word/document.xml");
        extra?.Invoke(b);
        return b.Build();
    }

    public static byte[] Xlsx(string sheetData, string? sharedStrings = null, Action<PackageBuilder>? extra = null, string sheetExtra = "", string workbookExtra = "", string sheets = "")
    {
        var b = new PackageBuilder();
        b.Add("/xl/workbook.xml", Sml + "sheet.main+xml", Decl + "<workbook " + SmlNs + "><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/>" + sheets + "</sheets>" + workbookExtra + "</workbook>")
            .Add("/xl/worksheets/sheet1.xml", Sml + "worksheet+xml", Decl + "<worksheet " + SmlNs + "><sheetData>" + sheetData + "</sheetData>" + sheetExtra + "</worksheet>")
            .Rel(null, "rId1", "officeDocument", "xl/workbook.xml")
            .Rel("/xl/workbook.xml", "rId1", "worksheet", "worksheets/sheet1.xml");
        if (sharedStrings is not null)
        {
            b.Add("/xl/sharedStrings.xml", Sml + "sharedStrings+xml", Decl + "<sst " + SmlNs + ">" + sharedStrings + "</sst>")
                .Rel("/xl/workbook.xml", "rId9", "sharedStrings", "sharedStrings.xml");
        }

        extra?.Invoke(b);
        return b.Build();
    }

    /// <summary>Opens a package, lets the caller add parts and relationships, and writes it again.</summary>
    public static byte[] Mutate(byte[] zip, Action<OpcPackage> edit)
    {
        using OpcPackage package = OpcPackage.Open(new MemoryStream(zip), leaveOpen: true);
        edit(package);
        using var ms = new MemoryStream();
        package.WriteTo(ms);
        return ms.ToArray();
    }

    public static void AddRel(OpcPackage p, string? owner, string id, string type, string target, bool external = false)
    {
        var list = new List<Relationship>(p.GetRelationships(owner))
        {
            new(id, type.StartsWith("http", StringComparison.Ordinal) ? type : PackageBuilder.RelBase + type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal),
        };
        p.SetRelationships(owner, list);
    }

    public static string Base64(string s, Encoding? e = null) => Convert.ToBase64String((e ?? Encoding.UTF8).GetBytes(s));

    // A compound file whose one stream is fragmented: the two halves of the secret sit in sectors that are not neighbours in the file.
    // [MS-CFB] §2.3: sectors of a stream are chained through the FAT and need not be contiguous.
    public static byte[] FragmentedCompoundFile(byte[] secretBytes)
    {
        const int sector = 512;
        const int streamSectors = 8;
        var data = new byte[streamSectors * sector];
        // Logical position 508: the secret starts four bytes before the end of the first stream sector.
        secretBytes.CopyTo(data, sector - 4);
        // Chain order in the file: logical sector i lives in file sector order[i] (+2 for the FAT and directory sectors).
        int[] order = [3, 0, 6, 1, 5, 2, 7, 4];
        var file = new byte[sector * (2 + streamSectors + 0) + sector];
        void U32(int at, uint v) => BitConverter.TryWriteBytes(file.AsSpan(at, 4), v);
        void U16(int at, ushort v) => BitConverter.TryWriteBytes(file.AsSpan(at, 2), v);
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(file, 0);
        U16(24, 0x3E);
        U16(26, 3);
        U16(28, 0xFFFE);
        U16(30, 9);
        U16(32, 6);
        U32(44, 1); // FAT sectors
        U32(48, 1); // first directory sector
        U32(56, 4096); // mini stream cutoff
        U32(60, 0xFFFFFFFE); // no mini FAT
        U32(68, 0xFFFFFFFE); // no DIFAT
        U32(76, 0); // DIFAT[0] = the FAT sector
        for (int i = 1; i < 109; i++)
        {
            U32(76 + (i * 4), 0xFFFFFFFF);
        }

        // Sector numbers: 0 = FAT, 1 = directory, 2.. = stream sectors in file order.
        int FileSector(int logical) => 2 + order[logical];
        int fat = sector * 1; // file offset of sector 0 is 512
        for (int i = 0; i < sector / 4; i++)
        {
            U32(fat + (i * 4), 0xFFFFFFFF);
        }

        U32(fat, 0xFFFFFFFD);
        U32(fat + 4, 0xFFFFFFFE);
        for (int l = 0; l < streamSectors; l++)
        {
            U32(fat + (FileSector(l) * 4), l == streamSectors - 1 ? 0xFFFFFFFE : (uint)FileSector(l + 1));
            data.AsSpan(l * sector, sector).CopyTo(file.AsSpan(sector * (1 + FileSector(l)), sector));
        }

        int dir = sector * 2;
        void Entry(int index, string name, byte type, uint left, uint right, uint child, uint start, long size)
        {
            int at = dir + (index * 128);
            byte[] n = Encoding.Unicode.GetBytes(name);
            n.CopyTo(file, at);
            U16(at + 64, (ushort)(n.Length + 2));
            file[at + 66] = type;
            file[at + 67] = 1;
            U32(at + 68, left);
            U32(at + 72, right);
            U32(at + 76, child);
            U32(at + 116, start);
            BitConverter.TryWriteBytes(file.AsSpan(at + 120, 8), size);
        }

        Entry(0, "Root Entry", 5, 0xFFFFFFFF, 0xFFFFFFFF, 1, 0xFFFFFFFE, 0);
        Entry(1, "Data", 2, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, (uint)FileSector(0), data.Length);
        for (int i = 2; i < 4; i++)
        {
            Entry(i, string.Empty, 0, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0, 0);
            file[dir + (i * 128) + 66] = 0;
            U16(dir + (i * 128) + 64, 0);
        }

        return file;
    }

    // [MS-OVBA] §2.4.1: "QUOKKA QUOKKA4419" where the second QUOKKA is a copy token, so the secret never appears as a run of bytes.
    public static byte[] OvbaSplitSecret() => [0x01, 0x0E, 0xB0, 0x80, (byte)'Q', (byte)'U', (byte)'O', (byte)'K', (byte)'K', (byte)'A', (byte)' ', 0x03, 0x60, 0x00, (byte)'4', (byte)'4', (byte)'1', (byte)'9'];

    public static byte[] CompoundWithStream(string stream, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var writer = new CompoundFileWriter(ms, leaveOpen: true))
        {
            writer.WriteEntry(new RecursiveAI.OneArchive.ArchiveEntry { Name = stream, Kind = RecursiveAI.OneArchive.ArchiveEntryKind.File, Size = content.Length }, content);
            writer.Finish();
        }

        return ms.ToArray();
    }

    public static IEnumerable<AttackCase> All()
    {
        // ---- Word: the secret split or hidden inside the body ------------------------------------------------------------------
        yield return Doc("split-runs-proof-smarttag-bookmark-hyperlink",
            "<w:p><w:r><w:t>QUO</w:t></w:r><w:proofErr w:type=\"spellStart\"/><w:r><w:rPr><w:b/></w:rPr><w:t>KKA</w:t></w:r><w:bookmarkStart w:id=\"1\" w:name=\"bm\"/><w:smartTag w:uri=\"urn:x\" w:element=\"x\"><w:r><w:t>44</w:t></w:r></w:smartTag><w:bookmarkEnd w:id=\"1\"/><w:hyperlink w:anchor=\"a\"><w:r><w:t>19</w:t></w:r></w:hyperlink></w:p>");
        yield return Doc("split-revisions", "<w:p><w:ins w:id=\"2\" w:author=\"a\"><w:r><w:t>QUOKKA</w:t></w:r></w:ins><w:del w:id=\"3\" w:author=\"a\"><w:r><w:delText>44</w:delText></w:r></w:del><w:moveFrom w:id=\"4\" w:author=\"a\"><w:r><w:t>19</w:t></w:r></w:moveFrom></w:p>");
        yield return Doc("complex-field-code-and-result",
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> QUO</w:instrText></w:r><w:r><w:instrText>KKA4419 </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>QUOK</w:t></w:r><w:r><w:t>KA4419</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        yield return Doc("xml-comment-inside-text", "<w:p><w:r><w:t>QUO<!-- x -->KKA4419</w:t></w:r></w:p>");
        yield return Doc("cdata-pieces", "<w:p><w:r><w:t><![CDATA[QUO]]>KKA<![CDATA[4419]]></w:t></w:r></w:p>");
        yield return Doc("character-references", "<w:p><w:r><w:t>&#81;&#x55;OKKA&#52;4&#x31;9</w:t></w:r></w:p>");
        yield return Doc("attribute-character-references", "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"&#81;UOKKA4419\"/><w:r><w:t>x</w:t></w:r><w:bookmarkEnd w:id=\"1\"/></w:p>");
        yield return Doc("alternate-content-choice-and-fallback",
            "<w:p><w:r><mc:AlternateContent " + Mc + "><mc:Choice Requires=\"w14\"><w:t>QUOKKA4419 choice</w:t></mc:Choice><mc:Fallback><w:t>QUOKKA4419 fallback</w:t></mc:Fallback></mc:AlternateContent></w:r></w:p>");
        yield return Doc("alternate-content-split-across-branches",
            "<w:p><w:r><mc:AlternateContent " + Mc + "><mc:Choice Requires=\"w14\"><w:t>QUOKKA</w:t></mc:Choice><mc:Fallback><w:t>QUOKKA</w:t></mc:Fallback></mc:AlternateContent></w:r><w:r><w:t>4419</w:t></w:r></w:p>");
        yield return Doc("unknown-namespace-element-attribute-comment",
            "<w:p><w:r><w:t>x</w:t></w:r></w:p><q:data xmlns:q=\"urn:example:q\" q:note=\"QUOKKA4419\"><q:item>QUOKKA4419</q:item><!--QUOKKA4419--><?pi QUOKKA4419?></q:data>");
        yield return Doc("unknown-namespace-prefixed-like-relationships",
            "<w:p w:rsidR=\"00000001\" xmlns:rr=\"urn:other\" rr:note=\"QUOKKA4419\"><w:r><w:t>x</w:t></w:r></w:p>");
        yield return Doc("attribute-with-relationship-prefix-bound-elsewhere",
            "<w:p xmlns:r=\"urn:not-relationships\" r:note=\"QUOKKA4419\"><w:r><w:t>x</w:t></w:r></w:p>");
        yield return Doc("namespace-uri-holds-secret", "<w:p><w:r><w:t>x</w:t></w:r></w:p><q:data xmlns:q=\"urn:QUOKKA4419\"/>");
        yield return Doc("element-and-attribute-names", "<w:p><w:r><w:t>x</w:t></w:r></w:p><QUOKKA4419 QUOKKA4419=\"1\"/>");
        yield return Doc("ext-list-in-properties", "<w:p><w:pPr><w:extLst><w:ext uri=\"{1}\"><q:v xmlns:q=\"urn:example:q\">QUOKKA4419</q:v></w:ext></w:extLst></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        yield return Doc("soft-hyphen-and-zero-width", "<w:p><w:r><w:t>QUO\u200BKKA\u00AD4419</w:t></w:r></w:p>");
        yield return Doc("soft-hyphen-element", "<w:p><w:r><w:t>QUOKKA</w:t><w:softHyphen/><w:t>4419</w:t></w:r></w:p>");
        yield return Doc("decomposed-unicode", "<w:p><w:r><w:t>Caf\u0065\u0301-4419</w:t></w:r></w:p>", secret: "Caf\u00E9-4419");
        yield return Doc("other-case", "<w:p><w:r><w:t>quokka4419 Quokka4419</w:t></w:r></w:p>", ignoreCase: true);
        yield return Doc("sdt-placeholder-and-bound-value", "<w:sdt><w:sdtPr><w:alias w:val=\"QUOKKA4419\"/><w:showingPlcHdr/><w:text/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>QUOKKA4419</w:t></w:r></w:p></w:sdtContent></w:sdt>",
            b => b.Add("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Decl + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:subject>QUOKKA4419</dc:subject><cp:lastModifiedBy>QUOKKA4419</cp:lastModifiedBy></cp:coreProperties>")
                .Rel(null, "rId7", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));

        // ---- Word: other parts ------------------------------------------------------------------------------------------------
        yield return Doc("custom-xml-base64", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .Add("/customXml/item1.xml", "application/xml", Decl + "<root><blob>" + Base64(T) + "</blob><wide>" + Base64(T, Encoding.Unicode) + "</wide></root>")
            .Rel("/word/document.xml", "rId10", "customXml", "../customXml/item1.xml"));
        yield return Doc("custom-xml-item-properties", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .Add("/customXml/item1.xml", "application/xml", Decl + "<root/>")
            .Add("/customXml/itemProps1.xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml", Decl + "<ds:datastoreItem xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\" ds:itemID=\"{11111111-1111-1111-1111-111111111111}\"><ds:schemaRefs><ds:schemaRef ds:uri=\"urn:QUOKKA4419\"/></ds:schemaRefs></ds:datastoreItem>")
            .Rel("/word/document.xml", "rId10", "customXml", "../customXml/item1.xml")
            .Rel("/customXml/item1.xml", "rId1", "customXmlProps", "itemProps1.xml"));
        yield return Doc("settings-attached-template-and-mail-merge", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .Add("/word/settings.xml", Wml + "settings+xml", Decl + "<w:settings " + W + "><w:attachedTemplate r:id=\"rId1\"/><w:mailMerge><w:mainDocumentType w:val=\"formLetters\"/><w:connectString w:val=\"Provider=x;Data Source=QUOKKA4419.xlsx\"/><w:query w:val=\"SELECT * FROM `QUOKKA4419$`\"/><w:dataSource r:id=\"rId2\"/><w:odso><w:udl w:val=\"QUOKKA4419\"/><w:table w:val=\"QUOKKA4419\"/><w:src r:id=\"rId2\"/></w:odso></w:mailMerge><w:docVars><w:docVar w:name=\"QUOKKA4419\" w:val=\"v\"/></w:docVars></w:settings>")
            .Rel("/word/document.xml", "rId10", "settings", "settings.xml")
            .Rel("/word/settings.xml", "rId1", "attachedTemplate", "file:///C:/Users/QUOKKA4419/Templates/t.dotm", external: true)
            .Rel("/word/settings.xml", "rId2", "mailMergeSource", "file:///C:/QUOKKA4419/data.xlsx", external: true));
        yield return Doc("people-and-comment-extensions", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .Add("/word/people.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml", Decl + "<w15:people xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\"><w15:person w15:author=\"QUOKKA4419\"><w15:presenceInfo w15:providerId=\"AD\" w15:userId=\"S::QUOKKA4419@example.com::\"/></w15:person></w15:people>")
            .Add("/word/commentsExtensible.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtensible+xml", Decl + "<w16cex:commentsExtensible xmlns:w16cex=\"http://schemas.microsoft.com/office/word/2018/wordml/cex\"><w16cex:commentExtensible w16cex:durableId=\"1\" w16cex:dateUtc=\"2020-01-01T00:00:00Z\"><w16cex:extLst><w16cex:ext uri=\"{x}\"><q:t xmlns:q=\"urn:q\">QUOKKA4419</q:t></w16cex:ext></w16cex:extLst></w16cex:commentExtensible></w16cex:commentsExtensible>")
            .Rel("/word/document.xml", "rId10", "http://schemas.microsoft.com/office/2011/relationships/people", "people.xml")
            .Rel("/word/document.xml", "rId11", "http://schemas.microsoft.com/office/2018/08/relationships/commentsExtensible", "commentsExtensible.xml"));
        yield return Doc("header-footer-field-and-textbox", "<w:p><w:r><w:t>x</w:t></w:r></w:p><w:sectPr><w:headerReference w:type=\"first\" r:id=\"rId10\"/></w:sectPr>", b => b
            .Add("/word/header1.xml", Wml + "header+xml", Decl + "<w:hdr " + W + "><w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> QUOKKA4419 </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p></w:hdr>")
            .Rel("/word/document.xml", "rId10", "header", "header1.xml"));
        yield return Doc("orphan-thumbnail", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/docProps/thumbnail.jpeg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]));
        yield return Doc("vba-project-split-by-compression", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/vbaProject.bin", "application/vnd.ms-office.vbaProject", CompoundWithStream("Module1", [.. new byte[8], .. OvbaSplitSecret()]))
            .Rel("/word/document.xml", "rId10", "http://schemas.microsoft.com/office/2006/relationships/vbaProject", "vbaProject.bin"), main: "application/vnd.ms-word.document.macroEnabled.main+xml");
        yield return Doc("ole-object-fragmented-compound-file", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/embeddings/oleObject1.bin", "application/vnd.openxmlformats-officedocument.oleObject", FragmentedCompoundFile(Encoding.ASCII.GetBytes(T)))
            .Rel("/word/document.xml", "rId10", "oleObject", "embeddings/oleObject1.bin"));
        yield return Doc("ole-object-fragmented-utf16", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/embeddings/oleObject1.bin", "application/vnd.openxmlformats-officedocument.oleObject", FragmentedCompoundFile(Encoding.Unicode.GetBytes(T)))
            .Rel("/word/document.xml", "rId10", "oleObject", "embeddings/oleObject1.bin"));
        yield return Doc("ole-package-holds-zip", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/embeddings/oleObject1.bin", "application/vnd.openxmlformats-officedocument.oleObject",
                CompoundWithStream("\u0001Ole10Native", Zip(("a.txt", "The secret is " + T + " and again " + T)))) // deflated: the text is not visible as bytes
            .Rel("/word/document.xml", "rId10", "oleObject", "embeddings/oleObject1.bin"));
        yield return Doc("png-compressed-text-chunk", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/media/image1.png", "image/png", PngWithCompressedText("Comment", "taken by " + T + " at home"))
            .Rel("/word/document.xml", "rId10", "image", "media/image1.png"));
        yield return Doc("embedded-font-name-table-utf16-big-endian", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/fonts/font1.odttf", "application/vnd.openxmlformats-officedocument.obfuscatedFont", [.. new byte[] { 1, 2, 3, 4, 0xFF, 0xFE, 7 }, .. Encoding.BigEndianUnicode.GetBytes("Copyright " + T + " Corp"), 0, 0, 9])
            .Rel("/word/document.xml", "rId10", "font", "fonts/font1.odttf"));
        yield return Doc("gzip-part", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/embeddings/data.gz", "application/gzip", Gzip("hello " + T + " world " + T))
            .Rel("/word/document.xml", "rId10", "oleObject", "embeddings/data.gz"));
        yield return Doc("utf16-xml-part-with-character-references", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/customXml/item1.xml", "application/xml", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("<root>&#81;UOKKA4419</root>")])
            .Rel("/word/document.xml", "rId10", "customXml", "../customXml/item1.xml"));
        yield return Doc("utf16-xml-part-plain", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/customXml/item1.xml", "application/xml", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("<root>" + T + "</root>")])
            .Rel("/word/document.xml", "rId10", "customXml", "../customXml/item1.xml"));
        yield return Doc("latin1-declared-xml-part", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/customXml/item1.xml", "application/xml", Encoding.Latin1.GetBytes("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?><root>M\u00FCller-4419 &#77;&#252;ller-4419</root>"))
            .Rel("/word/document.xml", "rId10", "customXml", "../customXml/item1.xml"), secret: "M\u00FCller-4419");
        yield return Doc("hyperlink-target-percent-encoded", "<w:p><w:hyperlink r:id=\"rId10\"><w:r><w:t>x</w:t></w:r></w:hyperlink></w:p>", b => b
            .Rel("/word/document.xml", "rId10", "hyperlink", "http://example.com/%51UOKKA4419?q=%2551UOKKA4419", external: true));
        yield return Doc("hyperlink-target-in-field-code", "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \"http://example.com/QUOKKA4419\" \\o \"QUOKKA4419\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>link</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        yield return Doc("internal-relationship-target-name", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/customXml/QUOKKA4419.xml", "application/xml", Encoding.UTF8.GetBytes("<a/>"))
            .Rel("/word/document.xml", "rId10", "customXml", "../customXml/QUOKKA4419.xml"));
        yield return Doc("unreferenced-part", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .Add("/word/orphan.xml", "application/xml", Decl + "<a>QUOKKA4419</a>"));
        yield return Doc("content-type-override-and-default", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/customXml/data.QUOKKA4419", "application/x-QUOKKA4419", [1, 2, 3])
            .Rel("/word/document.xml", "rId10", "customXml", "../customXml/data.QUOKKA4419"));

        yield return Doc("alt-chunk-html-with-entities", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/afchunk.html", "text/html", Encoding.UTF8.GetBytes("<html><body><p>&#81;UOKKA&#x34;419 and &#81;UOKKA4419</p></body></html>"))
            .Rel("/word/document.xml", "rId10", "aFChunk", "afchunk.html"));
        yield return Doc("alt-chunk-rtf-escapes", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/afchunk.rtf", "application/rtf", Encoding.ASCII.GetBytes("{\\rtf1\\ansi {\\b QUO}KKA\\'34\\'34\\'31\\'39\\par}"))
            .Rel("/word/document.xml", "rId10", "aFChunk", "afchunk.rtf"));
        yield return Doc("alt-chunk-quoted-printable", "<w:p><w:r><w:t>x</w:t></w:r></w:p>", b => b
            .AddBytes("/word/afchunk.mht", "message/rfc822", Encoding.ASCII.GetBytes("Content-Transfer-Encoding: quoted-printable\r\n\r\n=51UOKKA=\r\n4419"))
            .Rel("/word/document.xml", "rId10", "aFChunk", "afchunk.mht"));
        yield return Doc("relationship-id-holds-secret", "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><wp:docPr id=\"1\" name=\"p\"/><a:graphic><a:graphicData uri=\"x\"><a:blip r:embed=\"QUOKKA4419\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>", b => b
            .AddBytes("/word/media/image1.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0])
            .Rel("/word/document.xml", "QUOKKA4419", "image", "media/image1.png"));
        yield return Doc("non-breaking-space-in-name", "<w:p><w:r><w:t>Jane\u00A0Doe-4419</w:t></w:r></w:p>", secret: "Jane Doe-4419");
        yield return Doc("curly-apostrophe-in-name", "<w:p><w:r><w:t>O\u2019Brien-4419</w:t></w:r></w:p>", secret: "O'Brien-4419");

        // ---- Spreadsheets -------------------------------------------------------------------------------------------------------
        yield return Sheet("escaped-characters-shared-string", "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>", "<si><t>_x0051_UOKKA4419 and Q_x0055_OKKA4419</t></si>");
        yield return Sheet("escaped-characters-inline-string", "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>QUOKKA_x0034_419</t></is></c></row>");
        yield return Sheet("escaped-characters-formula-string-result", "<row r=\"1\"><c r=\"A1\" t=\"str\"><f>\"x\"</f><v>_x0051_UOKKA4419</v></c></row>");
        yield return Sheet("rich-text-split", "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>", "<si><r><t>QUO</t></r><r><rPr><b/></rPr><t>KKA</t></r><r><t>4419</t></r></si>");
        yield return Sheet("phonetic-text", "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>", "<si><t>base</t><rPh sb=\"0\" eb=\"1\"><t>QUOKKA4419</t></rPh><phoneticPr fontId=\"0\"/></si>");
        yield return Sheet("number-as-secret", "<row r=\"1\"><c r=\"A1\"><v>4419</v></c></row>", secret: "4419");
        yield return Sheet("formula-text-and-string-literal", "<row r=\"1\"><c r=\"A1\" t=\"str\"><f>CONCAT(\"QUOK\",\"KA4419\")</f><v>QUOKKA4419</v></c><c r=\"B1\"><f>LEN(\"QUOKKA4419\")</f><v>10</v></c></row>");
        yield return Sheet("sheet-name-and-cross-sheet-formula", "<row r=\"1\"><c r=\"A1\"><f>'QUOKKA4419'!A1</f><v>5</v></c></row>", sheets: "<sheet name=\"QUOKKA4419\" sheetId=\"2\" r:id=\"rId2\"/>", extra: b => b
            .Add("/xl/worksheets/sheet2.xml", Sml + "worksheet+xml", Decl + "<worksheet " + SmlNs + "><sheetData><row r=\"1\"><c r=\"A1\"><v>5</v></c></row></sheetData></worksheet>")
            .Rel("/xl/workbook.xml", "rId2", "worksheet", "worksheets/sheet2.xml"),
            workbookExtra: "<definedNames><definedName name=\"n1\" comment=\"QUOKKA4419\" localSheetId=\"0\">'QUOKKA4419'!$A$1</definedName></definedNames>");
        yield return Sheet("validation-prompts-and-headers", "<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>",
            sheetExtra: "<dataValidations count=\"1\"><dataValidation type=\"list\" sqref=\"A1\" promptTitle=\"QUOKKA4419\" prompt=\"QUOKKA4419\" errorTitle=\"QUOKKA4419\" error=\"QUOKKA4419\"><formula1>\"QUOKKA4419,b\"</formula1></dataValidation></dataValidations><headerFooter><oddHeader>&amp;LQUOKKA4419</oddHeader></headerFooter>");
        yield return Sheet("legacy-comments-and-vml", "<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", sheetExtra: "<legacyDrawing r:id=\"rId5\"/>", extra: b => b
            .Add("/xl/comments1.xml", Sml + "comments+xml", Decl + "<comments " + SmlNs + "><authors><author>QUOKKA4419</author></authors><commentList><comment ref=\"A1\" authorId=\"0\"><text><r><t>QUO</t></r><r><t>KKA4419</t></r></text></comment></commentList></comments>")
            .Add("/xl/drawings/vmlDrawing1.vml", "application/vnd.openxmlformats-officedocument.vmlDrawing", Decl + "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"><v:shape id=\"s1\" alt=\"QUOKKA4419\"><v:textbox><div>QUOKKA4419</div></v:textbox><x:ClientData ObjectType=\"Note\"><x:Row>0</x:Row></x:ClientData></v:shape></xml>")
            .Rel("/xl/worksheets/sheet1.xml", "rId4", "comments", "../comments1.xml")
            .Rel("/xl/worksheets/sheet1.xml", "rId5", "vmlDrawing", "../drawings/vmlDrawing1.vml"));
        yield return Sheet("threaded-comments-persons-connections", "<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", extra: b => b
            .Add("/xl/threadedComments/threadedComment1.xml", "application/vnd.ms-excel.threadedcomments+xml", Decl + "<ThreadedComments xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments\"><threadedComment ref=\"A1\" dT=\"2020-01-01T00:00:00.00\" personId=\"{1}\" id=\"{2}\"><text>QUOKKA4419</text></threadedComment></ThreadedComments>")
            .Add("/xl/persons/person.xml", "application/vnd.ms-excel.person+xml", Decl + "<personList xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments\"><person displayName=\"QUOKKA4419\" id=\"{1}\" userId=\"QUOKKA4419@example.com\" providerId=\"None\"/></personList>")
            .Add("/xl/connections.xml", Sml + "connections+xml", Decl + "<connections " + SmlNs + "><connection id=\"1\" name=\"QUOKKA4419\" description=\"QUOKKA4419\" type=\"1\"><dbPr connection=\"Provider=x;Data Source=QUOKKA4419\" command=\"select QUOKKA4419\"/></connection></connections>")
            .Add("/xl/richData/rdrichvalue.xml", "application/vnd.ms-excel.rdrichvalue+xml", Decl + "<rvData xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2017/richdata\" count=\"1\"><rv s=\"0\"><v>QUOKKA4419</v></rv></rvData>")
            .Add("/xl/slicerCaches/slicerCache1.xml", "application/vnd.ms-excel.slicerCache+xml", Decl + "<slicerCacheDefinition xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" name=\"Slicer_QUOKKA4419\" sourceName=\"QUOKKA4419\"/>")
            .Rel("/xl/worksheets/sheet1.xml", "rId4", "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment", "../threadedComments/threadedComment1.xml")
            .Rel("/xl/workbook.xml", "rId20", "http://schemas.microsoft.com/office/2017/10/relationships/person", "persons/person.xml")
            .Rel("/xl/workbook.xml", "rId21", "connections", "connections.xml")
            .Rel("/xl/workbook.xml", "rId22", "http://schemas.microsoft.com/office/2017/06/relationships/richValue", "richData/rdrichvalue.xml")
            .Rel("/xl/workbook.xml", "rId23", "http://schemas.microsoft.com/office/2007/relationships/slicerCache", "slicerCaches/slicerCache1.xml"));
        yield return Sheet("external-link-cache-and-path", "<row r=\"1\"><c r=\"A1\"><f>[1]Sheet1!A1</f><v>1</v></c></row>", workbookExtra: "<externalReferences><externalReference r:id=\"rId30\"/></externalReferences>", extra: b => b
            .Add("/xl/externalLinks/externalLink1.xml", Sml + "externalLink+xml", Decl + "<externalLink " + SmlNs + "><externalBook r:id=\"rId1\"><sheetNames><sheetName val=\"QUOKKA4419\"/></sheetNames><sheetDataSet><sheetData sheetId=\"0\"><row r=\"1\"><cell r=\"A1\" t=\"str\"><v>QUOKKA4419</v></cell></row></sheetData></sheetDataSet></externalBook></externalLink>")
            .Rel("/xl/workbook.xml", "rId30", "externalLink", "externalLinks/externalLink1.xml")
            .Rel("/xl/externalLinks/externalLink1.xml", "rId1", "externalLinkPath", "file:///C:/QUOKKA4419/book.xlsx", external: true)
            .Rel("/xl/externalLinks/externalLink1.xml", "rId2", "externalLinkPath", "QUOKKA4419.xlsx", external: true));
        yield return Sheet("pivot-cache-records-and-definition", "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>", "<si><t>head</t></si>", workbookExtra: "<pivotCaches><pivotCache cacheId=\"1\" r:id=\"rId40\"/></pivotCaches>", extra: b => b
            .Add("/xl/pivotCache/pivotCacheDefinition1.xml", Sml + "pivotCacheDefinition+xml", Decl + "<pivotCacheDefinition " + SmlNs + " r:id=\"rId1\" recordCount=\"1\"><cacheSource type=\"worksheet\"><worksheetSource ref=\"A1:A2\" sheet=\"Sheet1\"/></cacheSource><cacheFields count=\"1\"><cacheField name=\"QUOKKA4419\" numFmtId=\"0\"><sharedItems count=\"1\"><s v=\"QUOKKA4419\"/></sharedItems></cacheField></cacheFields></pivotCacheDefinition>")
            .Add("/xl/pivotCache/pivotCacheRecords1.xml", Sml + "pivotCacheRecords+xml", Decl + "<pivotCacheRecords " + SmlNs + " count=\"2\"><r><s v=\"QUOKKA4419\"/></r><r><n v=\"4419\"/></r></pivotCacheRecords>")
            .Rel("/xl/workbook.xml", "rId40", "pivotCacheDefinition", "pivotCache/pivotCacheDefinition1.xml")
            .Rel("/xl/pivotCache/pivotCacheDefinition1.xml", "rId1", "pivotCacheRecords", "pivotCacheRecords1.xml"));
        yield return Sheet("table-columns-and-structured-reference", "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>QUOKKA4419</t></is></c></row><row r=\"2\"><c r=\"A2\"><f>SUM(T1[QUOKKA4419])</f><v>0</v></c></row>", sheetExtra: "<tableParts count=\"1\"><tablePart r:id=\"rId6\"/></tableParts>", extra: b => b
            .Add("/xl/tables/table1.xml", Sml + "table+xml", Decl + "<table " + SmlNs + " id=\"1\" name=\"T1\" displayName=\"T1\" ref=\"A1:A2\"><tableColumns count=\"1\"><tableColumn id=\"1\" name=\"QUOKKA4419\"/></tableColumns></table>")
            .Rel("/xl/worksheets/sheet1.xml", "rId6", "table", "../tables/table1.xml"));
        yield return Sheet("printer-settings-utf16", "<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", extra: b => b
            .AddBytes("/xl/printerSettings/printerSettings1.bin", Sml + "printerSettings", [.. Encoding.Unicode.GetBytes("\\\\QUOKKA4419\\HP Laser"), 0, 0, 1, 2])
            .Rel("/xl/worksheets/sheet1.xml", "rId7", "printerSettings", "../printerSettings/printerSettings1.bin"));
        yield return Sheet("extension-list-conditional-format", "<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", sheetExtra: "<extLst><ext uri=\"{1}\" xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" xmlns:xm=\"http://schemas.microsoft.com/office/excel/2006/main\"><x14:conditionalFormattings><x14:conditionalFormatting><x14:cfRule type=\"expression\" id=\"{2}\"><xm:f>$A$1=\"QUOKKA4419\"</xm:f></x14:cfRule><xm:sqref>A1</xm:sqref></x14:conditionalFormatting></x14:conditionalFormattings></ext></extLst>");

        // ---- Presentations ---------------------------------------------------------------------------------------------------
        yield return Slides("tags-sections-custom-shows-authors", pkg =>
        {
            pkg.AddPart("/ppt/tags/tagQ.xml", "application/vnd.openxmlformats-officedocument.presentationml.tags+xml", Encoding.UTF8.GetBytes(Decl + "<p:tagLst xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><p:tag name=\"QUOKKA4419\" val=\"QUOKKA4419\"/></p:tagLst>"));
            pkg.AddPart("/ppt/commentAuthorsQ.xml", "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml", Encoding.UTF8.GetBytes(Decl + "<p:cmAuthorLst xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><p:cmAuthor id=\"0\" name=\"QUOKKA4419\" initials=\"Q\" lastIdx=\"1\" clrIdx=\"0\"/></p:cmAuthorLst>"));
            pkg.AddPart("/ppt/handoutMasters/handoutMasterQ.xml", "application/vnd.openxmlformats-officedocument.presentationml.handoutMaster+xml", Encoding.UTF8.GetBytes(Decl + "<p:handoutMaster xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld name=\"QUOKKA4419\"><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"h\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:p><a:r><a:t>QUOKKA4419</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:handoutMaster>"));
            AddRel(pkg, "/ppt/presentation.xml", "rId90", "tags", "tags/tagQ.xml");
            AddRel(pkg, "/ppt/presentation.xml", "rId91", "commentAuthors", "commentAuthorsQ.xml");
            AddRel(pkg, "/ppt/presentation.xml", "rId92", "handoutMaster", "handoutMasters/handoutMasterQ.xml");
            IPackagePart pres = pkg.FindPart("/ppt/presentation.xml")!;
            string xml = Encoding.UTF8.GetString(pres.GetBytes().Span);
            xml = xml.Replace("</p:presentation>", "<p:custShowLst><p:custShow name=\"QUOKKA4419\" id=\"1\"><p:sldLst><p:sld r:id=\"rId1000\"/></p:sldLst></p:custShow></p:custShowLst></p:presentation>", StringComparison.Ordinal);
            pres.SetBytes(Encoding.UTF8.GetBytes(xml));
        });
        yield return Slides("layout-name-and-modern-comments", pkg =>
        {
            foreach (IPackagePart p in pkg.Parts.ToArray())
            {
                if (p.Name.Contains("slideLayout", StringComparison.Ordinal) && p.Name.EndsWith(".xml", StringComparison.Ordinal))
                {
                    string xml = Encoding.UTF8.GetString(p.GetBytes().Span).Replace("<p:cSld>", "<p:cSld name=\"QUOKKA4419\">", StringComparison.Ordinal);
                    p.SetBytes(Encoding.UTF8.GetBytes(xml));
                }
            }

            pkg.AddPart("/ppt/comments/modernComment_Q.xml", "application/vnd.ms-powerpoint.comments+xml", Encoding.UTF8.GetBytes(Decl + "<p188:cmLst xmlns:p188=\"http://schemas.microsoft.com/office/powerpoint/2018/8/main\"><p188:cm id=\"{1}\" authorId=\"{2}\"><p188:txBody><a:bodyPr xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"/><a:p xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:r><a:t>QUOKKA4419</a:t></a:r></a:p></p188:txBody></p188:cm></p188:cmLst>"));
            pkg.AddPart("/ppt/authorsQ.xml", "application/vnd.ms-powerpoint.authors+xml", Encoding.UTF8.GetBytes(Decl + "<p188:authorLst xmlns:p188=\"http://schemas.microsoft.com/office/powerpoint/2018/8/main\"><p188:author id=\"{2}\" name=\"QUOKKA4419\" initials=\"Q\" userId=\"QUOKKA4419\" providerId=\"None\"/></p188:authorLst>"));
            AddRel(pkg, "/ppt/presentation.xml", "rId93", "http://schemas.microsoft.com/office/2018/10/relationships/authors", "authorsQ.xml");
        });
        yield return Slides("embedded-fonts-and-media-names", pkg =>
        {
            pkg.AddPart("/ppt/media/QUOKKA4419.mp3", "audio/mpeg", new byte[] { 1, 2, 3, 4 });
            AddRel(pkg, "/ppt/presentation.xml", "rId94", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio", "media/QUOKKA4419.mp3");
        });

        // ---- The container itself ----------------------------------------------------------------------------------------------
        yield return new AttackCase("zip-entry-comments-and-archive-comment", OfficeKind.Document, ZipComments(Docx("<w:p><w:r><w:t>x</w:t></w:r></w:p>"), T));
        yield return new AttackCase("zip-unicode-path-extra-field", OfficeKind.Document, ZipWithUnicodePathField(Docx("<w:p><w:r><w:t>x</w:t></w:r></w:p>"), T));
        yield return new AttackCase("flat-opc-with-binary-part", OfficeKind.Document, FlatOpc(Docx("<w:p><w:r><w:t>x</w:t></w:r></w:p><w:p><w:r><w:pict/></w:r></w:p>", b => b
            .AddBytes("/word/media/image1.png", "image/png", [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. Encoding.ASCII.GetBytes("tEXtComment\0" + T + " in metadata"), 0, 0, 0, 0])
            .Rel("/word/document.xml", "rId10", "image", "media/image1.png"))));

        // ---- Legacy inputs staged to Office Open XML -----------------------------------------------------------------------------
        yield return new AttackCase("rtf-split-groups-escapes-info-hidden", OfficeKind.Document, Encoding.ASCII.GetBytes("{\\rtf1\\ansi{\\info{\\title QUOKKA4419}{\\author QUOKKA4419}}\\pard {\\b QUO}{\\i KKA}\\'344\\u49?9 {\\v hidden QUOKKA4419}{\\field{\\*\\fldinst HYPERLINK \"http://x/QUOKKA4419\"}{\\fldrslt link}}\\par}"));
        yield return new AttackCase("doc-binary", OfficeKind.Document, File.ReadAllBytes(Fixture("doc", "basic.doc")), Secret: "@TEXT");
        yield return new AttackCase("doc-with-comments-and-notes", OfficeKind.Document, File.ReadAllBytes(Fixture("doc", "notes.doc")), Secret: "@TEXT");
        yield return new AttackCase("xls-binary", OfficeKind.Workbook, File.ReadAllBytes(Fixture("xls", "features.xls")), Secret: "@TEXT");
        yield return new AttackCase("ppt-binary", OfficeKind.Presentation, File.ReadAllBytes(Fixture("ppt", "lo-slide-runs.ppt")), Secret: "@TEXT");
        yield return new AttackCase("encrypted-agile-xlsx", OfficeKind.Workbook, File.ReadAllBytes(Fixture("encryption", "secret-basic.xlsx")), Secret: "@TEXT", Password: "secret");
        yield return new AttackCase("encrypted-rc4-doc", OfficeKind.Document, File.ReadAllBytes(Fixture("encryption", "secret-basic.doc")), Secret: "@TEXT", Password: "secret");

        // ---- Encrypted ----------------------------------------------------------------------------------------
        yield return new AttackCase("encrypted-agile-docx", OfficeKind.Document, File.ReadAllBytes(Fixture("encryption", "secret-basic.docx")), Secret: "@TEXT", Password: "secret");
    }

    public static string Fixture(string folder, string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../testdata", folder, name));

    private static AttackCase Doc(string name, string body, Action<PackageBuilder>? extra = null, string secret = T, bool ignoreCase = false, string? main = null) =>
        new(name, OfficeKind.Document, Docx(body, extra, mainContentType: main), secret, ignoreCase);

    private static AttackCase Sheet(string name, string sheetData, string? sharedStrings = null, string sheetExtra = "", Action<PackageBuilder>? extra = null, string workbookExtra = "", string sheets = "", string secret = T) =>
        new(name, OfficeKind.Workbook, Xlsx(sheetData, sharedStrings, extra, sheetExtra, workbookExtra, sheets), secret);

    private static AttackCase Slides(string name, Action<OpcPackage> edit) =>
        new(name, OfficeKind.Presentation, Mutate(Torture.Pptx(), edit));

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                using Stream s = z.CreateEntry(name, System.IO.Compression.CompressionLevel.SmallestSize).Open();
                s.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return ms.ToArray();
    }

    // Entry comments and the archive comment live outside every part.
    private static byte[] ZipComments(byte[] zip, string comment)
    {
        using var ms = new MemoryStream();
        using (var source = new System.IO.Compression.ZipArchive(new MemoryStream(zip), System.IO.Compression.ZipArchiveMode.Read))
        using (var target = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (System.IO.Compression.ZipArchiveEntry e in source.Entries)
            {
                System.IO.Compression.ZipArchiveEntry copy = target.CreateEntry(e.FullName);
                copy.Comment = comment;
                using Stream from = e.Open();
                using Stream to = copy.Open();
                from.CopyTo(to);
            }
        }

        byte[] bytes = ms.ToArray();
        int eocd = bytes.AsSpan().LastIndexOf(new byte[] { 0x50, 0x4B, 0x05, 0x06 });
        byte[] text = Encoding.ASCII.GetBytes(comment);
        bytes[eocd + 20] = (byte)text.Length;
        bytes[eocd + 21] = 0;
        return [.. bytes, .. text];
    }

    // Entries stored as they are, each with an Info-ZIP Unicode Path field (0x7075) that holds a second copy of the name with the secret in it,
    // in both the local and the central header. APPNOTE 4.6.9.
    private static byte[] ZipWithUnicodePathField(byte[] zip, string secret)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var central = new List<byte[]>();
        using var source = new System.IO.Compression.ZipArchive(new MemoryStream(zip), System.IO.Compression.ZipArchiveMode.Read);
        foreach (System.IO.Compression.ZipArchiveEntry e in source.Entries)
        {
            byte[] name = Encoding.UTF8.GetBytes(e.FullName);
            byte[] data;
            using (var buffer = new MemoryStream())
            {
                using (Stream s = e.Open())
                {
                    s.CopyTo(buffer);
                }

                data = buffer.ToArray();
            }

            byte[] alt = Encoding.UTF8.GetBytes(e.FullName + "-" + secret);
            byte[] extra = [0x75, 0x70, (byte)(5 + alt.Length), 0, 1, 0, 0, 0, 0, .. alt];
            uint crc = Crc32(data);
            int offset = (int)ms.Position;
            w.Write(0x04034B50u);
            w.Write((ushort)20);
            w.Write((ushort)0x0800);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)0x21);
            w.Write(crc);
            w.Write(data.Length);
            w.Write(data.Length);
            w.Write((ushort)name.Length);
            w.Write((ushort)extra.Length);
            w.Write(name);
            w.Write(extra);
            w.Write(data);
            using var c = new MemoryStream();
            using var cw = new BinaryWriter(c);
            cw.Write(0x02014B50u);
            cw.Write((ushort)20);
            cw.Write((ushort)20);
            cw.Write((ushort)0x0800);
            cw.Write((ushort)0);
            cw.Write((ushort)0);
            cw.Write((ushort)0x21);
            cw.Write(crc);
            cw.Write(data.Length);
            cw.Write(data.Length);
            cw.Write((ushort)name.Length);
            cw.Write((ushort)extra.Length);
            cw.Write((ushort)0);
            cw.Write((ushort)0);
            cw.Write((ushort)0);
            cw.Write(0u);
            cw.Write(offset);
            cw.Write(name);
            cw.Write(extra);
            cw.Flush();
            central.Add(c.ToArray());
        }

        int directory = (int)ms.Position;
        foreach (byte[] c in central)
        {
            w.Write(c);
        }

        w.Write(0x06054B50u);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)central.Count);
        w.Write((ushort)central.Count);
        w.Write(central.Sum(static c => c.Length));
        w.Write(directory);
        w.Write((ushort)0);
        w.Flush();
        return ms.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    private static byte[] FlatOpc(byte[] zip)
    {
        using OpcPackage p = OpcPackage.Open(new MemoryStream(zip), leaveOpen: true);
        using var ms = new MemoryStream();
        p.WriteFlatOpcTo(ms);
        return ms.ToArray();
    }

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var g = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            g.Write(Encoding.UTF8.GetBytes(text));
        }

        return ms.ToArray();
    }

    // A PNG whose text is in a compressed iTXt/zTXt chunk: the keyword is visible, the text is deflated.
    private static byte[] PngWithCompressedText(string keyword, string text)
    {
        using var z = new MemoryStream();
        using (var d = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            d.Write(Encoding.UTF8.GetBytes(text));
        }

        byte[] data = [.. Encoding.ASCII.GetBytes(keyword), 0, 0, .. z.ToArray()];
        byte[] length = [0, 0, (byte)(data.Length >> 8), (byte)data.Length];
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. length, .. "zTXt"u8.ToArray(), .. data, 0, 0, 0, 0];
    }
}
