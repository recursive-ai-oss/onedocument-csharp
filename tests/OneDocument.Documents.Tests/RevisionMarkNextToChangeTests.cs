using System.Linq;
using RecursiveAI.OneDocument.Documents.Revisions;
using static RecursiveAI.OneDocument.Documents.Tests.Fixture;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>A paragraph mark revision (<c>w:rPr/w:ins</c> or <c>w:del</c>) sitting beside a <c>w:pPrChange</c>, in the shape tracked-change writers produce.</summary>
public class RevisionMarkNextToChangeTests
{
    private static string Para(string mark, string text) =>
        "<w:p><w:pPr><w:jc w:val=\"center\"/><w:rPr>" + mark + "</w:rPr>" +
        "<w:pPrChange w:id=\"20\" w:author=\"Ann\"><w:pPr><w:jc w:val=\"left\"/></w:pPr></w:pPrChange></w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p><w:p><w:r><w:t>next</w:t></w:r></w:p>";

    private const string Ins = "<w:ins w:id=\"10\" w:author=\"Ann\"/>";
    private const string Del = "<w:del w:id=\"10\" w:author=\"Ann\"/>";

    private static string Cell(string inner) =>
        "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid><w:tr><w:tc>" + inner + "</w:tc></w:tr></w:tbl><w:p/>";

    public static IEnumerable<object[]> Shapes()
    {
        foreach (string mark in new[] { Ins, Del })
        {
            yield return [Para(mark, "body")];
            yield return [Cell(Para(mark, "cell"))];
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void RejectAllAndAcceptAllLeaveNothing(string body)
    {
        foreach (bool accept in new[] { true, false })
        {
            RevisionSet set = Set(body);
            Assert.Equal(2, set.Revisions.Count);
            _ = accept ? set.AcceptAll() : set.RejectAll();
            Assert.Empty(set.Revisions);
            Assert.DoesNotContain("Change", Xml(set), StringComparison.Ordinal);
            Assert.DoesNotContain("<w:ins ", Xml(set), StringComparison.Ordinal);
            Assert.DoesNotContain("<w:del ", Xml(set), StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void OneAtATimeInEitherOrderLeavesNothing(string body)
    {
        foreach (bool accept in new[] { true, false })
        {
            foreach (bool reverse in new[] { true, false })
            {
                RevisionSet set = Set(body);
                while (set.Revisions.Count > 0)
                {
                    Revision r = reverse ? set.Revisions[^1] : set.Revisions[0];
                    Assert.True(accept ? set.Accept(r) : set.Reject(r));
                }

                Assert.Empty(set.Revisions);
                Assert.DoesNotContain("Change", Xml(set), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RejectedChangeRestoresTheOldPropertiesAndKeepsTheMark()
    {
        RevisionSet set = Set(Para(Ins, "body"));
        Assert.True(set.Reject(set.Revisions.First(r => r.Kind == RevisionKind.ParagraphPropertiesChange)));
        string xml = Xml(set);
        Assert.Contains("<w:jc w:val=\"left\"/>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("center", xml, StringComparison.Ordinal);
        Assert.Equal(RevisionKind.ParagraphMarkInserted, Assert.Single(set.Revisions).Kind);
        Assert.Equal(1, set.RejectAll());
        Assert.Empty(set.Revisions);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void AuthoredThroughTheApiAndResolvedOneByOne(bool propertiesFirst, bool reverse)
    {
        using Document d = TestDocs.Open("<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr><w:r><w:t>Hello</w:t></w:r></w:p><w:p><w:r><w:t>Other</w:t></w:r></w:p>");
        Paragraph p = d.Body.Paragraphs().First();
        RevisionAuthor a = d.Revisions.CreateAuthor("Ann", new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var center = new ParagraphPropertiesDelta { Alignment = ParagraphAlignment.Center };
        if (propertiesFirst)
        {
            a.ChangeParagraphProperties(p, center);
        }

        a.InsertParagraph(p, "added");
        if (!propertiesFirst)
        {
            a.ChangeParagraphProperties(p, center);
        }

        d.CommitChanges();
        d.Revisions.Refresh();
        while (d.Revisions.Revisions.Count > 0)
        {
            Revision r = reverse ? d.Revisions.Revisions[^1] : d.Revisions.Revisions[0];
            Assert.True(d.Revisions.Reject(r));
        }

        Assert.Equal(["Hello", "Other"], d.Body.Paragraphs().Select(x => x.Text).ToArray());
        Assert.Contains("right", d.MainMarkup.Root.OuterXml, StringComparison.Ordinal);
        Assert.DoesNotContain("center", d.MainMarkup.Root.OuterXml, StringComparison.Ordinal);
    }
}
