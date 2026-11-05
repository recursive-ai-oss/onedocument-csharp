using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class SkeletonTests
{
    [Fact]
    public void Blocks_AreInDocumentOrder_AndNothingIsHidden()
    {
        using Document doc = TestDocs.Open(
            "<w:p><w:r><w:t>one</w:t></w:r></w:p><w:bookmarkStart w:id=\"1\" w:name=\"b\"/><w:tbl><w:tr><w:tc><w:p><w:r><w:t>in cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "<w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:t>in sdt</w:t></w:r></w:p></w:sdtContent></w:sdt><w:sectPr/>");
        BlockKind[] kinds = doc.Body.Blocks().Select(b => b.Kind).ToArray();
        Assert.Equal([BlockKind.Paragraph, BlockKind.Other, BlockKind.Table, BlockKind.ContentControl], kinds);
        Assert.Equal(["one", "in cell", "in sdt"], doc.Body.Paragraphs().Select(p => p.Text).ToArray());
        Assert.NotNull(doc.Body.SectionProperties);
    }

    [Fact]
    public void Inlines_ReachRunsThroughWrappers()
    {
        using Document doc = TestDocs.Open(
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>a</w:t><w:tab/><w:t>b</w:t></w:r>" +
            "<w:hyperlink w:anchor=\"x\"><w:r><w:t>link</w:t></w:r></w:hyperlink><w:ins w:id=\"1\"><w:r><w:t>new</w:t></w:r></w:ins>" +
            "<w:del w:id=\"2\"><w:r><w:delText>old</w:delText></w:r></w:del><w:fldSimple w:instr=\" PAGE \"><w:r><w:t>3</w:t></w:r></w:fldSimple></w:p>");
        Paragraph p = doc.Body.Paragraphs().Single();
        Assert.Equal("Heading1", p.StyleId);
        Assert.Equal("a\tblinknew3", p.Text);
        Assert.Equal([InlineKind.Run, InlineKind.Hyperlink, InlineKind.Group, InlineKind.Group, InlineKind.SimpleField], p.Inlines().Select(i => i.Kind).ToArray());
    }

    [Fact]
    public void Edit_ChangesOnlyTheMainPart()
    {
        var p = TestDocs.Package("<w:p><w:r><w:t>x</w:t></w:r></w:p>", "<w:style w:type=\"paragraph\" w:styleId=\"A\"/>");
        byte[] stylesBefore = p.FindPart("/word/styles.xml")!.GetBytes().ToArray();
        using var doc = Document.FromPackage(p);
        Assert.False(doc.HasUncommittedChanges);
        doc.Body.Paragraphs().Single().StyleId = "A";
        Assert.True(doc.HasUncommittedChanges);
        doc.CommitChanges();
        Assert.Equal(stylesBefore, p.FindPart("/word/styles.xml")!.GetBytes().ToArray());
        string xml = System.Text.Encoding.UTF8.GetString(p.FindPart("/word/document.xml")!.GetBytes().Span);
        Assert.Contains("<w:pPr><w:pStyle w:val=\"A\"/></w:pPr>", xml);
    }

    [Fact]
    public void NoMainPart_IsATypedFailure()
    {
        var p = new RecursiveAI.OneDocument.Packaging.InMemoryPackage();
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Document.FromPackage(p));
        Assert.Equal("docx.no-main-part", ex.Defect.Code);
    }

    [Fact]
    public void MissingBody_ReadsEmpty_WithADefect()
    {
        var defects = new DefectCollection();
        var p = TestDocs.Package("");
        p.FindPart("/word/document.xml")!.SetBytes(System.Text.Encoding.UTF8.GetBytes($"<w:document xmlns:w=\"{TestDocs.WNs}\"/>"));
        using var doc = Document.FromPackage(p, new OpenContext(defects: defects));
        Assert.Empty(doc.Body.Blocks());
        Assert.Contains(defects, d => d.Code == "docx.no-body");
    }
}
