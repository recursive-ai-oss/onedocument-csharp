using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class SplitTests
{
    private const string W = TestDocs.WNs;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 5, 5, 5];

    private static string H(string text, int level = 1) => MergeTests.P(text, "Heading" + level);

    private static InMemoryPackage Package(string body, string? numbering = null)
    {
        string styles = MergeTests.Style("Heading1", "heading 1", "<w:b/>") + MergeTests.Style("Heading2", "heading 2", "<w:i/>")
            + MergeTests.Style("Unused", "Unused", "<w:u w:val=\"single\"/>") + MergeTests.Style("Used", "Used", "<w:sz w:val=\"30\"/>")
            + "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        InMemoryPackage p = TestDocs.Package(body, styles, numbering);
        return p;
    }

    private static InMemoryPackage Sample()
    {
        string num = MergeTests.Abstract(0, "decimal", "%1.") + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        string body = MergeTests.P("front")
            + H("Alpha") + MergeTests.P("a1", "Used") + MergeTests.P("a2", null, "<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>")
            + "<w:p><w:r><w:t>note here</w:t></w:r><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>"
            + H("Beta") + MergeTests.Pic("rIdImg", 1) + "<w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>b1</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>"
            + H("Gamma") + H("Gamma sub", 2) + MergeTests.P("g1");
        InMemoryPackage p = Package(body, num);
        PartHelper.Add(p, "/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml", PartHelper.Rel + "footnotes",
            $"<w:footnotes xmlns:w=\"{W}\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:id=\"1\"><w:p><w:r><w:t>fn</w:t></w:r></w:p></w:footnote><w:footnote w:id=\"2\"><w:p><w:r><w:t>orphan</w:t></w:r></w:p></w:footnote></w:footnotes>");
        PartHelper.Add(p, "/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", PartHelper.Rel + "comments",
            $"<w:comments xmlns:w=\"{W}\"><w:comment w:id=\"0\" w:author=\"A\"><w:p><w:r><w:t>c</w:t></w:r></w:p></w:comment></w:comments>");
        MergeTests.AddImage(p, "/word/media/image1.png", "rIdImg", Png);
        p.AddPart("/word/media/unused.png", "image/png", Png);
        return p;
    }

    [Fact]
    public void ByHeading_GivesOnePiecePerHeadingWithOnlyWhatItUses()
    {
        using Document d = MergeTests.Doc(Sample());
        using SplitResult r = d.SplitByHeading(1);
        Assert.Equal(["", "Alpha", "Beta", "Gamma"], r.Pieces.Select(p => p.Title).ToArray());
        Assert.Equal(4, r.Report.CountOf("parts"));
        Assert.Equal(["front"], MergeTests.Texts(r.Pieces[0].Document));
        Assert.Equal(["Alpha", "a1", "a2", "note here"], MergeTests.Texts(r.Pieces[1].Document));
        Assert.Equal(["Gamma", "Gamma sub", "g1"], MergeTests.Texts(r.Pieces[3].Document));

        Document front = r.Pieces[0].Document;
        Assert.Null(front.Styles.Find("Heading1"));
        Assert.NotNull(front.Styles.Find("Normal"));
        Assert.False(front.Numbering.IsPresent && front.Numbering.Instances.Count > 0);
        Assert.Empty(front.Footnotes.Normal());
        Assert.Empty(front.Comments);
        Assert.DoesNotContain(front.Package.Parts, x => x.ContentType == "image/png");

        Document alpha = r.Pieces[1].Document;
        Assert.NotNull(alpha.Styles.Find("Used"));
        Assert.NotNull(alpha.Styles.Find("Heading1"));
        Assert.Null(alpha.Styles.Find("Heading2"));
        Assert.Null(alpha.Styles.Find("Unused"));
        Assert.Single(alpha.Numbering.Instances);
        Assert.Equal(["fn"], alpha.Footnotes.Normal().Select(n => n.Text).ToArray());

        Document beta = r.Pieces[2].Document;
        Assert.Empty(beta.Footnotes.Normal());
        Assert.Single(beta.Comments);
        Assert.Single(beta.Package.Parts, x => x.ContentType == "image/png");
        Assert.Null(beta.Styles.Find("Used"));

        Assert.Equal(2, r.Pieces[3].Document.Styles.Styles.Count(s => s.Name!.StartsWith("heading", StringComparison.Ordinal)));
    }

    [Fact]
    public void ByHeading_PiecesSaveAndOpenAgain_AndTheirTextIsTheOriginalsSlices()
    {
        using Document d = MergeTests.Doc(Sample());
        string[] all = MergeTests.Texts(d);
        using SplitResult r = d.SplitByHeading(2);
        var joined = new List<string>();
        foreach (DocumentPiece p in r.Pieces)
        {
            using Document again = MergeTests.Reopen(p.Document);
            joined.AddRange(MergeTests.Texts(again));
        }

        Assert.Equal(all, joined);
        Assert.Equal(5, r.Pieces.Count);
    }

    [Fact]
    public void ByHeading_DoesNotChangeTheOriginal()
    {
        using Document d = MergeTests.Doc(Sample());
        string before = d.Body.Container.Node.OuterXml;
        int parts = d.Package.Parts.Count;
        using SplitResult r = d.SplitByHeading(1);
        Assert.Equal(before, d.Body.Container.Node.OuterXml);
        Assert.Equal(parts, d.Package.Parts.Count);
        Assert.NotNull(d.Styles.Find("Unused"));
    }

    [Fact]
    public void BySections_EachPieceHasItsOwnPageSetupAndInheritedHeaders()
    {
        string sect(int w, string extra = "") => $"<w:sectPr xmlns:r=\"{TestDocs.RNs}\">{extra}<w:pgSz w:w=\"{w}\" w:h=\"15840\"/></w:sectPr>";
        string body = "<w:p><w:pPr>" + sect(12240, "<w:headerReference w:type=\"default\" r:id=\"rIdH\"/>") + "</w:pPr><w:r><w:t>one</w:t></w:r></w:p>"
            + "<w:p><w:pPr>" + sect(13000) + "</w:pPr><w:r><w:t>two</w:t></w:r></w:p>"
            + MergeTests.P("three") + sect(14000);
        InMemoryPackage p = TestDocs.Package(body);
        PartHelper.Add(p, "/word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml", PartHelper.Rel + "header",
            $"<w:hdr xmlns:w=\"{W}\"><w:p><w:r><w:t>head</w:t></w:r></w:p></w:hdr>");
        var rels = new List<Relationship>(p.GetRelationships("/word/document.xml"));
        rels[^1] = rels[^1] with { Id = "rIdH" };
        p.SetRelationships("/word/document.xml", rels);
        using Document d = MergeTests.Doc(p);
        using SplitResult r = d.SplitBySections();
        Assert.Equal(3, r.Pieces.Count);
        Assert.Equal([12240L, 13000L, 14000L], r.Pieces.Select(x => (long)x.Document.Sections.Single().PageSize.Width).ToArray());
        foreach (DocumentPiece piece in r.Pieces)
        {
            using Document again = MergeTests.Reopen(piece.Document);
            Assert.Equal("head", again.Sections.Single().GetOwn(HeaderFooterKind.Header, HeaderFooterType.Default)!.Text);
        }
    }

    [Fact]
    public void SplitBefore_RefusesPositionsOutsideTheBody()
    {
        using Document d = MergeTests.Doc(Sample());
        Assert.Throws<ArgumentOutOfRangeException>(() => d.SplitBefore([99]));
        Assert.Throws<ArgumentOutOfRangeException>(() => d.SplitByHeading(0));
        using SplitResult one = d.SplitBefore([]);
        Assert.Single(one.Pieces);
    }
}
