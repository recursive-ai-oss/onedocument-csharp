using System.Text;
using RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class DocumentRevisionTests
{
    private const string RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private static (InMemoryPackage Package, Document Doc) Open()
    {
        InMemoryPackage p = TestDocs.Package(
            "<w:p><w:r><w:t>Body </w:t></w:r><w:ins w:id=\"1\" w:author=\"Ann\"><w:r><w:t>new</w:t></w:r></w:ins></w:p>",
            stylesXml: "<w:style w:type=\"paragraph\" w:styleId=\"Normal\"/>");
        p.AddPart("/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml",
            Encoding.UTF8.GetBytes($"<w:footnotes xmlns:w=\"{TestDocs.WNs}\"><w:footnote w:id=\"2\"><w:p><w:del w:id=\"3\" w:author=\"Bob\"><w:r><w:delText>old</w:delText></w:r></w:del></w:p></w:footnote></w:footnotes>"));
        p.SetRelationships("/word/document.xml",
        [
            new Relationship("rId2", RelType + "styles", "styles.xml"),
            new Relationship("rId5", RelType + "footnotes", "footnotes.xml"),
        ]);
        return (p, Document.FromPackage(p));
    }

    [Fact]
    public void DocumentListsRevisionsOfEveryRevisionPart()
    {
        (_, Document doc) = Open();
        Assert.Equal(2, doc.Revisions.Revisions.Count);
        Assert.Equal(["/word/document.xml", "/word/footnotes.xml"], doc.Revisions.Revisions.Select(r => r.Part.Uri).ToArray());
        Assert.Equal("Body new", doc.Body.Paragraphs().First().GetText(RevisionView.Accepted));
        Assert.Equal("Body ", doc.Body.Paragraphs().First().GetText(RevisionView.Rejected));
        Assert.True(doc.Revisions.Revisions[0].Markup.IsValid);
    }

    [Fact]
    public void ResolvingThroughTheDocumentRewritesOnlyTheTouchedParts()
    {
        (InMemoryPackage p, Document doc) = Open();
        byte[] stylesBefore = p.FindPart("/word/styles.xml")!.GetBytes().ToArray();
        byte[] footnotesBefore = p.FindPart("/word/footnotes.xml")!.GetBytes().ToArray();
        doc.Revisions.ByAuthor("Ann");
        Assert.Equal(1, doc.Revisions.Accept(doc.Revisions.ByAuthor("Ann")));
        doc.CommitChanges();
        Assert.Contains("<w:r><w:t>new</w:t></w:r>", Encoding.UTF8.GetString(p.FindPart("/word/document.xml")!.GetBytes().Span));
        Assert.DoesNotContain("w:ins", Encoding.UTF8.GetString(p.FindPart("/word/document.xml")!.GetBytes().Span));
        Assert.Equal(stylesBefore, p.FindPart("/word/styles.xml")!.GetBytes().ToArray());
        Assert.Equal(footnotesBefore, p.FindPart("/word/footnotes.xml")!.GetBytes().ToArray());
        Assert.Single(doc.Revisions.Revisions);
    }
}
