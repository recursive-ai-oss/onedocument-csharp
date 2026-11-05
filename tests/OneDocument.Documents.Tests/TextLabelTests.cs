using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class TextLabelTests
{
    private static string Item(int num, int lvl, string text) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{lvl}\"/><w:numId w:val=\"{num}\"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Extract(Document d, TextExtractionOptions options)
    {
        // The stored main part, wrapped in a minimal zip package, is what extraction reads.
        byte[] main = d.Package.FindPart(d.MainPartName)!.GetBytes().ToArray();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content) { using Stream s = zip.CreateEntry(name).Open(); s.Write(Encoding.UTF8.GetBytes(content)); }
            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            using Stream m = zip.CreateEntry("word/document.xml").Open();
            m.Write(main);
        }

        using IByteSource source = ByteSource.FromMemory(ms.ToArray());
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        OoxmlTextExtractor.Extract(source, writer, options: options);
        return writer.ToString();
    }

    [Fact]
    public void Extraction_prefixes_numbered_paragraphs_with_their_labels()
    {
        string numbering =
            "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl>" +
            "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerLetter\"/><w:suff w:val=\"space\"/><w:lvlText w:val=\"%2)\"/></w:lvl></w:abstractNum>" +
            "<w:abstractNum w:abstractNumId=\"1\"><w:lvl w:ilvl=\"0\"><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"\u2022\"/></w:lvl></w:abstractNum>" +
            "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>";
        using Document d = TestDocs.Open(Item(1, 0, "one") + Item(1, 1, "sub") + "<w:p><w:r><w:t>plain</w:t></w:r></w:p>" + Item(1, 0, "two") + Item(2, 0, "dot"), null, numbering);

        Assert.Equal("1.\tone\na) sub\nplain\n2.\ttwo\n\u2022\tdot\n", Extract(d, new TextExtractionOptions { ListLabels = DocumentText.ListLabels(d) }));
        Assert.Equal("one\nsub\nplain\ntwo\ndot\n", Extract(d, TextExtractionOptions.Default));
    }

    [Fact]
    public void Labels_are_refused_while_the_document_has_uncommitted_changes()
    {
        using Document d = TestDocs.Open(Item(1, 0, "one"), null, "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
        d.MainMarkup.Root.SetAttribute("urn:x", "y", "1", "x");
        Assert.True(d.HasUncommittedChanges);
        Assert.Throws<InvalidOperationException>(() => DocumentText.ListLabels(d));
    }
}
