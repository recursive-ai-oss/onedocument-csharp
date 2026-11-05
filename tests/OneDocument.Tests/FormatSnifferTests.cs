using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Tests;

public class FormatSnifferTests
{
    private const string Wml = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
    private const string Sml = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
    private const string Pml = "application/vnd.openxmlformats-officedocument.presentationml.";

    public static TheoryData<string, string, OfficeFormat> OpcCases => new()
    {
        { "word/document.xml", Wml + "document.main+xml", OfficeFormat.Docx },
        { "word/document.xml", "application/vnd.ms-word.document.macroEnabled.main+xml", OfficeFormat.Docm },
        { "word/document.xml", Wml + "template.main+xml", OfficeFormat.Dotx },
        { "word/document.xml", "application/vnd.ms-word.template.macroEnabledTemplate.main+xml", OfficeFormat.Dotm },
        { "xl/workbook.xml", Sml + "sheet.main+xml", OfficeFormat.Xlsx },
        { "xl/workbook.xml", "application/vnd.ms-excel.sheet.macroEnabled.main+xml", OfficeFormat.Xlsm },
        { "xl/workbook.xml", Sml + "template.main+xml", OfficeFormat.Xltx },
        { "xl/workbook.xml", "application/vnd.ms-excel.template.macroEnabled.main+xml", OfficeFormat.Xltm },
        { "xl/workbook.xml", "application/vnd.ms-excel.addin.macroEnabled.main+xml", OfficeFormat.Xlam },
        { "xl/workbook.bin", "application/vnd.ms-excel.sheet.binary.macroEnabled.main", OfficeFormat.Xlsb },
        { "ppt/presentation.xml", Pml + "presentation.main+xml", OfficeFormat.Pptx },
        { "ppt/presentation.xml", "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml", OfficeFormat.Pptm },
        { "ppt/presentation.xml", Pml + "template.main+xml", OfficeFormat.Potx },
        { "ppt/presentation.xml", Pml + "slideshow.main+xml", OfficeFormat.Ppsx },
        { "ppt/presentation.xml", "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml", OfficeFormat.Ppsm },
    };

    [Theory]
    [MemberData(nameof(OpcCases))]
    public void Opc_package_is_named_by_its_main_part_content_type(string part, string contentType, OfficeFormat expected) =>
        Assert.Equal(expected, FormatSniffer.Detect(Samples.Opc(part, contentType)));

    [Fact]
    public void Main_part_is_found_through_the_relationship_not_the_first_override()
    {
        // A decoy override for another part comes first; only the part the officeDocument relationship names counts.
        byte[] zip = Samples.Zip(
            ("[Content_Types].xml", "<Types xmlns=\"x\"><Override PartName=\"/other.xml\" ContentType=\"" + Sml + "sheet.main+xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"" + Wml + "document.main+xml\"/></Types>"),
            ("_rels/.rels", Samples.RootRels("word/document.xml")));
        Assert.Equal(OfficeFormat.Docx, FormatSniffer.Detect(zip));
    }

    [Theory]
    [InlineData("word/document.xml", "document.main+xml", OfficeFormat.Docx)]
    [InlineData("xl/workbook.xml", "sheet.main+xml", OfficeFormat.Xlsx)]
    [InlineData("ppt/presentation.xml", "presentation.main+xml", OfficeFormat.Pptx)]
    public void Main_part_typed_only_by_a_default_extension_is_named(string part, string suffix, OfficeFormat expected)
    {
        // ECMA-376 Part 2 §10.1.2: a part without an Override has the type of its extension's Default.
        string ns = part.StartsWith("word", StringComparison.Ordinal) ? Wml : part.StartsWith("xl", StringComparison.Ordinal) ? Sml : Pml;
        byte[] zip = Samples.Zip(
            ("[Content_Types].xml", "<Types xmlns=\"x\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"" + ns + suffix + "\"/></Types>"),
            ("_rels/.rels", Samples.RootRels(part)));
        Assert.Equal(expected, FormatSniffer.Detect(zip));
    }

    [Fact]
    public void An_override_for_the_main_part_beats_a_default_listed_first()
    {
        byte[] zip = Samples.Zip(
            ("[Content_Types].xml", "<Types xmlns=\"x\"><Default Extension=\"xml\" ContentType=\"" + Sml + "sheet.main+xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"" + Wml + "document.main+xml\"/></Types>"),
            ("_rels/.rels", Samples.RootRels("word/document.xml")));
        Assert.Equal(OfficeFormat.Docx, FormatSniffer.Detect(zip));
    }

    [Fact]
    public void Strict_relationship_namespace_is_accepted()
    {
        string rels = "<Relationships><Relationship Id=\"rId1\" Type=\"http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument\" Target=\"/xl/workbook.xml\"/></Relationships>";
        Assert.Equal(OfficeFormat.Xlsx, FormatSniffer.Detect(Samples.Opc("xl/workbook.xml", Sml + "sheet.main+xml", rels)));
    }

    [Fact]
    public void Missing_relationships_fall_back_to_the_first_main_content_type()
    {
        byte[] zip = Samples.Zip(("[Content_Types].xml", "<Types xmlns=\"x\"><Override PartName=\"/ppt/presentation.xml\" ContentType=\"" + Pml + "presentation.main+xml\"/></Types>"));
        Assert.Equal(OfficeFormat.Pptx, FormatSniffer.Detect(zip));
    }

    [Theory]
    [InlineData("application/vnd.oasis.opendocument.text", OfficeFormat.Odt)]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet", OfficeFormat.Ods)]
    [InlineData("application/vnd.oasis.opendocument.presentation", OfficeFormat.Odp)]
    [InlineData("application/vnd.oasis.opendocument.text-template", OfficeFormat.Ott)]
    [InlineData("application/vnd.oasis.opendocument.graphics", OfficeFormat.Unknown)]
    public void Odf_is_named_by_the_mimetype_entry(string mimetype, OfficeFormat expected) =>
        Assert.Equal(expected, FormatSniffer.Detect(Samples.Odf(mimetype)));

    [Fact]
    public void Plain_zip_is_unknown() =>
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(Samples.Zip(("a.txt", "hello"))));

    [Theory]
    [InlineData("WordDocument", OfficeFormat.Doc)]
    [InlineData("Workbook", OfficeFormat.Xls)]
    [InlineData("PowerPoint Document", OfficeFormat.Ppt)]
    [InlineData("Book", OfficeFormat.XlsLegacy)]
    public void Compound_file_is_named_by_its_root_stream(string stream, OfficeFormat expected) =>
        Assert.Equal(expected, FormatSniffer.Detect(Samples.Cfb(stream, "SummaryInformation")));

    [Fact]
    public void Compound_file_with_encrypted_package_streams_is_encrypted_ooxml() =>
        Assert.Equal(OfficeFormat.EncryptedPackage, FormatSniffer.Detect(Samples.Cfb("EncryptionInfo", "EncryptedPackage")));

    [Fact]
    public void Compound_file_with_other_streams_is_unknown() =>
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(Samples.Cfb("Contents")));

    [Fact]
    public void Content_decides_not_the_extension()
    {
        string path = Path.Combine(Path.GetTempPath(), "onedocument-sniff-" + Environment.ProcessId + ".xls");
        try
        {
            File.WriteAllBytes(path, Samples.Opc("word/document.xml", Wml + "document.main+xml"));
            Assert.Equal(OfficeFormat.Docx, FormatSniffer.Detect(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Binary_signatures()
    {
        Assert.Equal(OfficeFormat.Pdf, FormatSniffer.Detect(Samples.Text("%PDF-1.7\n")));
        Assert.Equal(OfficeFormat.Png, FormatSniffer.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }));
        Assert.Equal(OfficeFormat.Jpeg, FormatSniffer.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }));
        Assert.Equal(OfficeFormat.Tiff, FormatSniffer.Detect(new byte[] { 0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0 }));
        Assert.Equal(OfficeFormat.WebP, FormatSniffer.Detect(Samples.Text("RIFF\0\0\0\0WEBPVP8 ")));
    }

    [Theory]
    [InlineData(new byte[] { 0x09, 0x00, 0x04, 0x00, 0, 0, 0, 0 }, OfficeFormat.XlsLegacy)]
    [InlineData(new byte[] { 0x09, 0x02, 0x06, 0x00, 0, 0, 0, 0 }, OfficeFormat.XlsLegacy)]
    [InlineData(new byte[] { 0x09, 0x04, 0x06, 0x00, 0, 0, 0, 0 }, OfficeFormat.XlsLegacy)]
    [InlineData(new byte[] { 0x09, 0x08, 0x08, 0x00, 0, 0, 0, 0 }, OfficeFormat.XlsLegacy)]
    [InlineData(new byte[] { 0x09, 0x08, 0x10, 0x00, 0, 0, 0, 0 }, OfficeFormat.Xls)]
    public void Bare_biff_streams(byte[] bytes, OfficeFormat expected) =>
        Assert.Equal(expected, FormatSniffer.Detect(bytes));

    [Fact]
    public void Rtf() =>
        Assert.Equal(OfficeFormat.Rtf, FormatSniffer.Detect(Samples.Text("{\\rtf1\\ansi hello}")));

    [Fact]
    public void Flat_opc_kind_comes_from_the_main_part()
    {
        string Flat(string name, string type) =>
            "<?xml version=\"1.0\" standalone=\"yes\"?><?mso-application progid=\"Word.Document\"?>" +
            "<pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\">" +
            "<pkg:part pkg:name=\"/_rels/.rels\" pkg:contentType=\"application/vnd.openxmlformats-package.relationships+xml\"><pkg:xmlData>" +
            $"<Relationships xmlns=\"x\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"{name[1..]}\"/></Relationships></pkg:xmlData></pkg:part>" +
            $"<pkg:part pkg:name=\"{name}\" pkg:contentType=\"{type}\"><pkg:xmlData><x/></pkg:xmlData></pkg:part></pkg:package>";
        Assert.Equal(OfficeFormat.FlatOpcDocument, FormatSniffer.Detect(Samples.Text(Flat("/word/document.xml", Wml + "document.main+xml"))));
        Assert.Equal(OfficeFormat.FlatOpcWorkbook, FormatSniffer.Detect(Samples.Text(Flat("/xl/workbook.xml", Sml + "sheet.main+xml"))));
        Assert.Equal(OfficeFormat.FlatOpcPresentation, FormatSniffer.Detect(Samples.Text(Flat("/ppt/presentation.xml", Pml + "presentation.main+xml"))));
    }

    [Fact]
    public void Flat_opc_main_part_beyond_the_first_prefix_is_found()
    {
        // The content type of the main part lies after more than PrefixLength bytes of other parts.
        string filler = new('a', 3 * FormatSniffer.PrefixLength);
        string xml = "<pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\">" +
            $"<pkg:part pkg:name=\"/word/styles.xml\" pkg:contentType=\"application/xml\"><pkg:xmlData><x>{filler}</x></pkg:xmlData></pkg:part>" +
            $"<pkg:part pkg:name=\"/word/document.xml\" pkg:contentType=\"{Wml}document.main+xml\"><pkg:xmlData><x/></pkg:xmlData></pkg:part></pkg:package>";
        Assert.Equal(OfficeFormat.FlatOpcDocument, FormatSniffer.Detect(Samples.Text(xml)));
    }

    [Fact]
    public void Office_2003_xml_formats()
    {
        Assert.Equal(OfficeFormat.Word2003Xml, FormatSniffer.Detect(Samples.Text(
            "<?xml version=\"1.0\"?><?mso-application progid=\"Word.Document\"?><w:wordDocument xmlns:w=\"http://schemas.microsoft.com/office/word/2003/wordml\"/>")));
        Assert.Equal(OfficeFormat.Excel2003Xml, FormatSniffer.Detect(Samples.Text(
            "<?xml version=\"1.0\"?><?mso-application progid=\"Excel.Sheet\"?><Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"/>")));
    }

    [Fact]
    public void Flat_odf()
    {
        string Fo(string type) => "<?xml version=\"1.0\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" office:version=\"1.3\" office:mimetype=\"" + type + "\"/>";
        Assert.Equal(OfficeFormat.Fodt, FormatSniffer.Detect(Samples.Text(Fo("application/vnd.oasis.opendocument.text"))));
        Assert.Equal(OfficeFormat.Fods, FormatSniffer.Detect(Samples.Text(Fo("application/vnd.oasis.opendocument.spreadsheet"))));
        Assert.Equal(OfficeFormat.Fodp, FormatSniffer.Detect(Samples.Text(Fo("application/vnd.oasis.opendocument.presentation"))));
    }

    [Fact]
    public void Html_and_svg_and_utf8_bom()
    {
        Assert.Equal(OfficeFormat.Html, FormatSniffer.Detect(Samples.Text("<!DOCTYPE html>\n<html><body>x</body></html>")));
        Assert.Equal(OfficeFormat.Html, FormatSniffer.Detect(Samples.Text("\uFEFF  <HTML>\n<HEAD></HEAD>")));
        Assert.Equal(OfficeFormat.Svg, FormatSniffer.Detect(Samples.Text("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>")));
    }

    [Fact]
    public void Utf16_xml_with_a_byte_order_mark()
    {
        byte[] bytes = [.. new byte[] { 0xFF, 0xFE }, .. System.Text.Encoding.Unicode.GetBytes("<?xml version=\"1.0\"?><?mso-application progid=\"Word.Document\"?><w:wordDocument xmlns:w=\"http://schemas.microsoft.com/office/word/2003/wordml\"/>")];
        Assert.Equal(OfficeFormat.Word2003Xml, FormatSniffer.Detect(bytes));
    }

    [Fact]
    public void Delimited_text_is_the_last_resort()
    {
        Assert.Equal(OfficeFormat.Csv, FormatSniffer.Detect(Samples.Text("a,b,c\n1,2,3\n\"x,y\",5,6\n")));
        Assert.Equal(OfficeFormat.Tsv, FormatSniffer.Detect(Samples.Text("a\tb\n1\t2\n")));
        Assert.Equal(OfficeFormat.Csv, FormatSniffer.Detect(Samples.Text("a;b\r\n1;2\r\n")));
        // A quoted field may hold line breaks; the record is still one record.
        Assert.Equal(OfficeFormat.Csv, FormatSniffer.Detect(Samples.Text("a,b\r\nc,\"x\ny\"\r\n1,2\r\n")));
        // Prose with a comma, one line, ragged columns, binary junk and empty input are not tables.
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(Samples.Text("Hello, world\n")));
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(Samples.Text("a,b\n1,2,3\n")));
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void Markdown_and_plain_text_are_never_detected() =>
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(Samples.Text("# Title\n\nSome *text*.\n")));

    [Fact]
    public void Damaged_containers_are_unknown_not_exceptions()
    {
        byte[] zip = Samples.Opc("word/document.xml", Wml + "document.main+xml");
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(zip.AsMemory(0, zip.Length / 2)));
        byte[] cfb = Samples.Cfb("WordDocument");
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(cfb.AsMemory(0, 700)));
        Assert.Equal(OfficeFormat.Unknown, FormatSniffer.Detect(new byte[] { (byte)'P', (byte)'K', 3, 4 }));
    }

    [Fact]
    public void A_container_over_the_part_count_limit_raises_a_typed_limit_exception()
    {
        var entries = Enumerable.Range(0, 20).Select(i => ($"p{i}.xml", "<x/>")).ToArray();
        var ex = Assert.Throws<OfficeLimitException>(() => FormatSniffer.Detect(Samples.Zip(entries), new OfficeLimits { MaxPartCount = 5 }));
        Assert.Equal(nameof(OfficeLimits.MaxPartCount), ex.LimitName);
    }

    [Fact]
    public async Task Async_detection_matches_sync_over_a_ranged_source()
    {
        byte[] zip = Samples.Opc("word/document.xml", Wml + "document.main+xml");
        long bytesRead = 0;
        using IO.IByteSource ranged = IO.ByteSource.FromRanged(zip.Length, (offset, buffer, _) =>
        {
            int n = (int)Math.Min(buffer.Length, zip.Length - offset);
            zip.AsMemory((int)offset, n).CopyTo(buffer);
            bytesRead += n;
            return new ValueTask<int>(n);
        });
        Assert.Equal(OfficeFormat.Docx, await FormatSniffer.DetectAsync(ranged));

        // Text formats are decided from the prefix alone: a large CSV is not downloaded.
        byte[] csv = Samples.Text(string.Concat(Enumerable.Repeat("1,2,3\n", 100_000)));
        bytesRead = 0;
        using IO.IByteSource big = IO.ByteSource.FromRanged(csv.Length, (offset, buffer, _) =>
        {
            int n = (int)Math.Min(buffer.Length, csv.Length - offset);
            csv.AsMemory((int)offset, n).CopyTo(buffer);
            bytesRead += n;
            return new ValueTask<int>(n);
        });
        Assert.Equal(OfficeFormat.Csv, await FormatSniffer.DetectAsync(big));
        Assert.True(bytesRead <= FormatSniffer.PrefixLength, $"read {bytesRead} bytes");
    }
}
