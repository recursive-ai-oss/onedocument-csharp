using System.IO.Compression;
using System.Text;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Documents.Tests;

public sealed class LazyMainPartTests
{
    private static InMemoryPackage Malformed(string xml)
    {
        InMemoryPackage p = TestDocs.Package(string.Empty);
        p.FindPart("/word/document.xml")!.SetBytes(Encoding.UTF8.GetBytes(xml));
        return p;
    }

    [Fact]
    public void MalformedMainPart_RaisesAtFirstAccess_WithLocation()
    {
        using Document doc = Document.FromPackage(Malformed($"<w:document xmlns:w=\"{TestDocs.WNs}\"><w:body><w:p></w:body></w:document>"));
        OfficeFormatException ex = Assert.ThrowsAny<OfficeFormatException>(() => doc.Body);
        Assert.Equal("/word/document.xml", ex.Location.Container);
        Assert.ThrowsAny<OfficeFormatException>(() => doc.MainMarkup);
    }

    [Fact]
    public void WrongRoot_RaisesAtFirstAccess()
    {
        using Document doc = Document.FromPackage(Malformed("<x:other xmlns:x=\"urn:x\"/>"));
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => doc.Paragraphs.ToList());
        Assert.Equal("docx.wrong-root", ex.Defect.Code);
    }

    [Fact]
    public void UntouchedSave_CopiesMalformedMainPartUnchanged()
    {
        const string xml = "<w:document xmlns:w=\"urn:broken\"><w:body><w:p></w:document>";
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "lazymain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string source = Path.Combine(dir, "in.docx");
            string copy = Path.Combine(dir, "out.docx");
            using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                Add(zip, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                Add(zip, "word/document.xml", xml);
            }

            using (Document doc = Document.Open(source))
            {
                doc.SaveAs(copy);
            }

            using ZipArchive saved = ZipFile.OpenRead(copy);
            using var reader = new StreamReader(saved.GetEntry("word/document.xml")!.Open());
            Assert.Equal(xml, reader.ReadToEnd());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using Stream s = zip.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    public void ConcurrentFirstAccess_SharesOneInstance()
    {
        for (int round = 0; round < 20; round++)
        {
            using Document doc = TestDocs.Open("<w:p><w:r><w:t>a</w:t></w:r></w:p>", stylesXml: "<w:style w:type=\"paragraph\" w:styleId=\"Normal\" w:default=\"1\"/>");
            var bodies = new Body[16];
            var styles = new object[16];
            var markups = new object[16];
            using var gate = new ManualResetEventSlim();
            Thread[] threads = Enumerable.Range(0, 16).Select(i => new Thread(() =>
            {
                gate.Wait();
                bodies[i] = doc.Body;
                styles[i] = doc.Styles;
                markups[i] = doc.MainMarkup;
            })).ToArray();
            foreach (Thread t in threads)
            {
                t.Start();
            }

            gate.Set();
            foreach (Thread t in threads)
            {
                t.Join();
            }

            Assert.All(bodies, b => Assert.Same(bodies[0], b));
            Assert.All(styles, s => Assert.Same(styles[0], s));
            Assert.All(markups, m => Assert.Same(markups[0], m));
        }
    }
}
