using RecursiveAI.OneOffice.Documents.Revisions;
using static RecursiveAI.OneOffice.Documents.Tests.Fixture;

namespace RecursiveAI.OneOffice.Documents.Tests;

/// <summary>What resolving leaves of a wrapper whose whole content was a tracked change; the rules were observed in Word (see docs/revisions.md).</summary>
public class RevisionEmptyWrapperTests
{
    private const string Auth = "w:author=\"A\" w:date=\"2024-01-01T00:00:00Z\"";
    private static string Inserted => "<w:ins w:id=\"1\" " + Auth + "><w:r><w:t>new</w:t></w:r></w:ins>";
    private static string Deleted => "<w:del w:id=\"1\" " + Auth + "><w:r><w:delText>old</w:delText></w:r></w:del>";

    private static string Para(string wrapperStart, string content, string wrapperEnd) =>
        "<w:p><w:r><w:t>before </w:t></w:r>" + wrapperStart + content + wrapperEnd + "<w:r><w:t> after</w:t></w:r></w:p>";

    [Theory]
    [InlineData("<w:smartTag w:uri=\"urn:t\" w:element=\"x\"><w:smartTagPr><w:attr w:name=\"a\" w:val=\"b\"/></w:smartTagPr>", "</w:smartTag>")]
    [InlineData("<w:customXml w:uri=\"urn:t\" w:element=\"x\">", "</w:customXml>")]
    [InlineData("<w:smartTag w:uri=\"urn:t\" w:element=\"x\"><w:customXml w:uri=\"urn:t\" w:element=\"y\">", "</w:customXml></w:smartTag>")]
    public void GroupingWrappersGoWhenEmptied(string start, string end)
    {
        RevisionSet reject = Set(Para(start, Inserted, end));
        reject.RejectAll();
        Assert.DoesNotContain("smartTag", Xml(reject), StringComparison.Ordinal);
        Assert.DoesNotContain("customXml", Xml(reject), StringComparison.Ordinal);
        Assert.Equal("before  after\n", Text(reject, RevisionView.Accepted));

        RevisionSet accept = Set(Para(start, Deleted, end));
        accept.AcceptAll();
        Assert.DoesNotContain("smartTag", Xml(accept), StringComparison.Ordinal);
        Assert.DoesNotContain("customXml", Xml(accept), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<w:smartTag w:uri=\"urn:t\" w:element=\"x\">", "</w:smartTag>")]
    [InlineData("<w:customXml w:uri=\"urn:t\" w:element=\"x\">", "</w:customXml>")]
    public void GroupingWrappersStayWhileTheyHoldContent(string start, string end)
    {
        RevisionSet set = Set(Para(start, Inserted + "<w:r><w:t>kept</w:t></w:r>", end));
        set.RejectAll();
        Assert.Contains(start[..start.IndexOf(' ')], Xml(set), StringComparison.Ordinal);
        Assert.Contains("kept", Xml(set), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<w:sdt><w:sdtPr><w:alias w:val=\"N\"/><w:text/></w:sdtPr><w:sdtContent>", "</w:sdtContent></w:sdt>", "<w:sdt>")]
    [InlineData("<w:sdt><w:sdtPr/><w:sdtContent>", "</w:sdtContent></w:sdt>", "<w:sdt>")]
    [InlineData("<w:hyperlink w:anchor=\"_Top\">", "</w:hyperlink>", "<w:hyperlink ")]
    [InlineData("<w:fldSimple w:instr=\" DATE \">", "</w:fldSimple>", "<w:fldSimple ")]
    public void ContentControlsHyperlinksAndFieldsStayWhenEmptied(string start, string end, string expected)
    {
        RevisionSet set = Set(Para(start, Inserted, end));
        set.RejectAll();
        Assert.Contains(expected, Xml(set), StringComparison.Ordinal);
        Assert.Empty(set.Revisions);
        Assert.Equal("before  after\n", Text(set, RevisionView.Accepted));
    }

    [Fact]
    public void PruningWorksInAnyResolveOrder()
    {
        string content = Inserted + "<w:del w:id=\"2\" " + Auth + "><w:r><w:delText>old</w:delText></w:r></w:del>";
        foreach (bool reverse in new[] { false, true })
        {
            RevisionSet set = Set(Para("<w:smartTag w:uri=\"urn:t\" w:element=\"x\">", content, "</w:smartTag>"));
            while (set.Revisions.Count > 0)
            {
                Revision r = reverse ? set.Revisions[^1] : set.Revisions[0];
                // Reject the insertion, accept the deletion: both leave nothing.
                Assert.True(r.Kind == RevisionKind.Insertion ? set.Reject(r) : set.Accept(r));
            }

            Assert.DoesNotContain("smartTag", Xml(set), StringComparison.Ordinal);
        }
    }
}
