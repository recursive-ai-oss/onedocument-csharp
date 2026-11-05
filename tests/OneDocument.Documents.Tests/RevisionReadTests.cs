using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Revisions;
using static RecursiveAI.OneDocument.Documents.Tests.Fixture;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class RevisionReadTests
{
    private const string Rich =
        "<w:p><w:r><w:t>Plain </w:t></w:r>" + "<w:ins w:id=\"1\" w:author=\"Ann\" w:date=\"2024-03-01T10:00:00Z\"><w:r><w:t>added</w:t></w:r></w:ins>" +
        "<w:del w:id=\"2\" w:author=\"Bob\" w:date=\"2024-03-02T10:00:00Z\"><w:r><w:delText>gone</w:delText></w:r></w:del></w:p>" +
        "<w:p><w:pPr><w:rPr><w:ins w:id=\"3\" w:author=\"Ann\"/></w:rPr></w:pPr><w:r><w:t>second</w:t></w:r></w:p>" +
        "<w:p><w:pPr><w:jc w:val=\"center\"/><w:pPrChange w:id=\"4\" w:author=\"Bob\"><w:pPr><w:jc w:val=\"left\"/></w:pPr></w:pPrChange></w:pPr>" +
        "<w:r><w:rPr><w:b/><w:rPrChange w:id=\"5\" w:author=\"Bob\"><w:rPr/></w:rPrChange></w:rPr><w:t>bold</w:t></w:r></w:p>" +
        "<w:p><w:moveFromRangeStart w:id=\"6\" w:name=\"m1\" w:author=\"Ann\"/><w:moveFrom w:id=\"7\" w:author=\"Ann\"><w:r><w:delText>moved</w:delText></w:r></w:moveFrom><w:moveFromRangeEnd w:id=\"6\"/></w:p>" +
        "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid><w:tr><w:trPr><w:ins w:id=\"8\" w:author=\"Ann\"/></w:trPr>" +
        "<w:tc><w:tcPr><w:cellIns w:id=\"9\" w:author=\"Ann\"/></w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";

    [Fact]
    public void CensusCountsKindsAuthorsAndDates()
    {
        RevisionSet set = Set(Rich);
        RevisionCensus c = set.Census();
        Assert.Equal(8, c.Total);
        Assert.Equal(1, c.ByKind[RevisionKind.Insertion]);
        Assert.Equal(1, c.ByKind[RevisionKind.Deletion]);
        Assert.Equal(1, c.ByKind[RevisionKind.ParagraphMarkInserted]);
        Assert.Equal(1, c.ByKind[RevisionKind.ParagraphPropertiesChange]);
        Assert.Equal(1, c.ByKind[RevisionKind.RunPropertiesChange]);
        Assert.Equal(1, c.ByKind[RevisionKind.MoveFrom]);
        Assert.Equal(1, c.ByKind[RevisionKind.TableRowInserted]);
        Assert.Equal(1, c.ByKind[RevisionKind.CellInserted]);
        Assert.Equal(5, c.ByAuthor["Ann"]);
        Assert.Equal(3, c.ByAuthor["Bob"]);
        Assert.Equal(new DateTimeOffset(2024, 3, 1, 10, 0, 0, TimeSpan.Zero), c.Earliest);
        Assert.Equal(9, set.MaxId);
        Assert.False(set.Parts[0].Markup.IsModified);
    }

    [Fact]
    public void RevisionsCarryTextMoveNameAndParagraph()
    {
        RevisionSet set = Set(Rich);
        Revision ins = set.Revisions.First(r => r.Kind == RevisionKind.Insertion);
        Assert.Equal("added", ins.Text);
        Assert.Equal("Ann", ins.Author);
        Assert.Equal(1, ins.Id);
        Assert.Equal(RevisionScope.Content, ins.Scope);
        Assert.Equal("Plain ", Wml(ins.Paragraph.FirstChild));
        Revision del = set.Revisions.First(r => r.Kind == RevisionKind.Deletion);
        Assert.Equal("gone", del.Text);
        Revision mv = set.Revisions.First(r => r.Kind == RevisionKind.MoveFrom);
        Assert.Equal("m1", mv.MoveName);
        Assert.True(ins.Offset > 0);
    }

    private static string Wml(RecursiveAI.OneDocument.Markup.MarkupNode n) => n.InnerText;

    [Fact]
    public void ViewsShowAcceptedAndRejectedText()
    {
        RevisionSet set = Set(Rich);
        Assert.Equal("Plain added\nsecond\nbold\n\ncell\n", Text(set, RevisionView.Accepted));
        Assert.Equal("Plain gone\nsecondbold\nmoved\n", Text(set, RevisionView.Rejected));
    }

    [Fact]
    public void MalformedRevisionAttributesAreReportedWithLocations()
    {
        var sink = new DefectCollection();
        var part = Part("<w:p><w:ins><w:r><w:t>x</w:t></w:r></w:ins><w:ins w:id=\"x\" w:author=\"A\" w:date=\"yesterday\"/></w:p>");
        RevisionSet set = RevisionSet.Scan([part], new OpenContext(defects: sink));
        Assert.Equal(2, set.Revisions.Count);
        Assert.Equal("", set.Revisions[0].Author);
        Assert.Equal(-1, set.Revisions[0].Id);
        Assert.Null(set.Revisions[1].Date);
        string[] codes = sink.Select(d => d.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.Equal(["docx.revision-author-missing", "docx.revision-date-invalid", "docx.revision-id-invalid", "docx.revision-id-invalid"], codes);
        Assert.All(sink, d => Assert.True(d.Location.Offset > 0));
    }

    [Fact]
    public void InRangeAndByAuthorSelect()
    {
        RevisionSet set = Set(Rich);
        Assert.Equal(3, set.ByAuthor("Bob").Count);
        IReadOnlyList<Revision> r = set.InRange(Paragraph(set, 0), Paragraph(set, 1));
        Assert.Equal([1, 2, 3], r.Select(x => x.Id).ToArray());
    }
}
