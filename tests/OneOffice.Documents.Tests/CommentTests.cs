using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Comments;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Documents.Tests;

public class CommentTests
{
    private const string W = TestDocs.WNs;
    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private const string Ms = "http://schemas.microsoft.com/office/2011/relationships/";

    private const string Body =
        "<w:p><w:r><w:t>before </w:t></w:r><w:commentRangeStart w:id=\"0\"/><w:commentRangeStart w:id=\"1\"/><w:r><w:t>target</w:t></w:r>" +
        "<w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r><w:commentRangeEnd w:id=\"1\"/><w:r><w:commentReference w:id=\"1\"/></w:r></w:p>";

    private static InMemoryPackage WithComments(bool extended = true)
    {
        InMemoryPackage p = TestDocs.Package(Body, "<w:style w:type=\"paragraph\" w:styleId=\"A\"/>");
        PartHelper.Add(p, "/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", PartHelper.Rel + "comments",
            $"<w:comments xmlns:w=\"{W}\" xmlns:w14=\"{W14}\">" +
            "<w:comment w:id=\"0\" w:author=\"Ann\" w:date=\"2024-05-01T10:00:00Z\" w:initials=\"A\"><w:p w14:paraId=\"11111111\"><w:r><w:annotationRef/></w:r><w:r><w:t>Why?</w:t></w:r></w:p></w:comment>" +
            "<w:comment w:id=\"1\" w:author=\"Bob\"><w:p w14:paraId=\"22222222\"><w:r><w:t>Because.</w:t></w:r></w:p></w:comment></w:comments>");
        if (extended)
        {
            PartHelper.Add(p, "/word/commentsExtended.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml", Ms + "commentsExtended",
                $"<w15:commentsEx xmlns:w15=\"{W15}\"><w15:commentEx w15:paraId=\"11111111\" w15:done=\"1\"/><w15:commentEx w15:paraId=\"22222222\" w15:paraIdParent=\"11111111\" w15:done=\"0\"/></w15:commentsEx>");
        }

        return p;
    }

    [Fact]
    public void Reads_AuthorsDatesRepliesAndState()
    {
        using var d = Document.FromPackage(WithComments());
        Assert.Equal(2, d.Comments.Count);
        Comment c = d.Comments.Find(0)!;
        Assert.Equal("Ann", c.Author);
        Assert.Equal("A", c.Initials);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero), c.Date);
        Assert.Equal("Why?", c.Text);
        Assert.Equal("11111111", c.ParaId);
        Assert.True(c.IsDone);
        Assert.False(c.IsReply);
        Comment reply = Assert.Single(c.Replies);
        Assert.Equal("Bob", reply.Author);
        Assert.Same(c, reply.Parent);
        Assert.Null(reply.Date);
        Assert.Equal([c], d.Comments.TopLevel().ToArray());
        Assert.NotNull(c.Anchor.Start);
        Assert.NotNull(c.Anchor.End);
        Assert.NotNull(c.Anchor.Reference);
    }

    [Fact]
    public void WithoutModernParts_EverythingIsTopLevel()
    {
        using var d = Document.FromPackage(WithComments(extended: false));
        Assert.All(d.Comments, c => Assert.Null(c.Parent));
        Assert.False(d.Comments.Find(0)!.IsDone);
    }

    [Fact]
    public void Damage_IsReported()
    {
        var defects = new DefectCollection();
        InMemoryPackage p = WithComments();
        p.FindPart("/word/document.xml")!.SetBytes(System.Text.Encoding.UTF8.GetBytes(
            $"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:commentRangeStart w:id=\"0\"/><w:commentReference w:id=\"7\"/></w:p></w:body></w:document>"));
        using var d = Document.FromPackage(p, new OpenContext(defects: defects));
        _ = d.Comments.Find(0)!.Anchor;
        Assert.Contains(defects, x => x.Code == "docx.comment-range-unmatched" && x.Location.Offset > 0);
        Assert.Contains(defects, x => x.Code == "docx.comment-missing");

        var dup = new DefectCollection();
        InMemoryPackage q = WithComments();
        q.FindPart("/word/comments.xml")!.SetBytes(System.Text.Encoding.UTF8.GetBytes(
            $"<w:comments xmlns:w=\"{W}\"><w:comment w:id=\"3\"><w:p/></w:comment><w:comment w:id=\"3\"><w:p/></w:comment></w:comments>"));
        using var d2 = Document.FromPackage(q, new OpenContext(defects: dup));
        Assert.Equal(2, d2.Comments.Count);
        Assert.Single(dup, x => x.Code == "docx.comment-duplicate-id");
    }

    [Fact]
    public void AddThenRemove_TouchesOnlyCommentPartsAndTheDocument_AndRestoresTheBody()
    {
        InMemoryPackage p = TestDocs.Package("<w:p><w:r><w:t>one</w:t></w:r><w:r><w:t>two</w:t></w:r></w:p>", "<w:style w:type=\"paragraph\" w:styleId=\"A\"/>");
        byte[] styles = p.FindPart("/word/styles.xml")!.GetBytes().ToArray();
        byte[] original = p.FindPart("/word/document.xml")!.GetBytes().ToArray();
        string[] before = p.Parts.Select(x => x.Name).Order().ToArray();
        using var d = Document.FromPackage(p);
        Run[] runs = d.Body.Paragraphs().Single().Runs().ToArray();
        Comment c = d.Comments.Add(runs[0], runs[1], "Carol", "Check <this>\nsecond line", "C", new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)));
        d.CommitChanges();

        Assert.Equal(styles, p.FindPart("/word/styles.xml")!.GetBytes().ToArray());
        Assert.Equal(before.Append("/word/comments.xml").Order().ToArray(), p.Parts.Select(x => x.Name).Order().ToArray());
        string doc = PartHelper.Text(p, "/word/document.xml");
        Assert.Contains("<w:commentRangeStart w:id=\"0\"/><w:r><w:t>one</w:t></w:r><w:r><w:t>two</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r>", doc);
        string comments = PartHelper.Text(p, "/word/comments.xml");
        Assert.Contains("w:author=\"Carol\"", comments);
        Assert.Contains("w:date=\"2025-01-02T01:04:05Z\"", comments);
        Assert.Contains("Check &lt;this&gt;", comments);
        Assert.Contains("w14:paraId=\"", comments);
        Assert.Equal("Check <this>\nsecond line", c.Text);
        Assert.Contains(p.GetRelationships("/word/document.xml"), x => x.Type.EndsWith("/comments", StringComparison.Ordinal));

        using var again = Document.FromPackage(p);
        Comment found = again.Comments.Find(0)!;
        Assert.Equal("Carol", found.Author);
        Assert.True(again.Comments.Remove(found));
        again.CommitChanges();
        Assert.Equal(original, p.FindPart("/word/document.xml")!.GetBytes().ToArray());
        Assert.Empty(again.Comments);
    }

    [Fact]
    public void Add_UsesNextIdAndKeepsExistingModernPartsConsistent()
    {
        InMemoryPackage p = WithComments();
        PartHelper.Add(p, "/word/commentsIds.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsIds+xml", "http://schemas.microsoft.com/office/2016/09/relationships/commentsIds",
            "<w16cid:commentsIds xmlns:w16cid=\"http://schemas.microsoft.com/office/word/2016/wordml/cid\"><w16cid:commentId w16cid:paraId=\"11111111\" w16cid:durableId=\"0A0A0A0A\"/></w16cid:commentsIds>");
        PartHelper.Add(p, "/word/people.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml", Ms + "people",
            $"<w15:people xmlns:w15=\"{W15}\"><w15:person w15:author=\"Ann\"><w15:presenceInfo w15:providerId=\"None\" w15:userId=\"Ann\"/></w15:person></w15:people>");
        using var d = Document.FromPackage(p);
        Run run = d.Body.Paragraphs().Single().Runs().First();
        Comment c = d.Comments.Add(run, run, "Dan", "New");
        Assert.Equal(2, c.Id);
        d.CommitChanges();
        string ext = PartHelper.Text(p, "/word/commentsExtended.xml");
        string ids = PartHelper.Text(p, "/word/commentsIds.xml");
        Assert.Equal(3, Count(ext, "<w15:commentEx "));
        Assert.Equal(2, Count(ids, "<w16cid:commentId "));
        Assert.Contains("w15:author=\"Dan\"", PartHelper.Text(p, "/word/people.xml"));
        Assert.Contains("mc:Ignorable=\"w14\"", PartHelper.Text(p, "/word/comments.xml"));
        string? para = c.ParaId;
        Assert.NotNull(para);
        Assert.Contains("w15:paraId=\"" + para + "\"", ext);
        Assert.Contains("w16cid:paraId=\"" + para + "\"", ids);
    }

    [Fact]
    public void Reply_AnchorsBesideTheParent_AndRemovingTheParentRemovesIt()
    {
        InMemoryPackage p = TestDocs.Package("<w:p><w:r><w:t>x</w:t></w:r></w:p>");
        using var d = Document.FromPackage(p);
        Run run = d.Body.Paragraphs().Single().Runs().Single();
        Comment parent = d.Comments.Add(run, run, "Ann", "Question");
        Comment reply = d.Comments.Reply(parent, "Bob", "Answer");
        Assert.Same(parent, reply.Parent);
        Assert.Equal([reply], parent.Replies.ToArray());
        Assert.False(parent.IsDone);
        parent.IsDone = true;
        Assert.True(parent.IsDone);
        d.CommitChanges();

        string doc = PartHelper.Text(p, "/word/document.xml");
        Assert.Contains("<w:commentRangeStart w:id=\"0\"/><w:commentRangeStart w:id=\"1\"/><w:r><w:t>x</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r><w:commentRangeEnd w:id=\"1\"/><w:r><w:commentReference w:id=\"1\"/></w:r>", doc);
        string ext = PartHelper.Text(p, "/word/commentsExtended.xml");
        Assert.Contains("w15:paraIdParent=\"" + parent.ParaId + "\"", ext);

        using var again = Document.FromPackage(p);
        Assert.Equal("Answer", again.Comments.Find(1)!.Text);
        Assert.Same(again.Comments.Find(0), again.Comments.Find(1)!.Parent);
        Assert.True(again.Comments.Remove(again.Comments.Find(0)!));
        again.CommitChanges();
        Assert.Empty(again.Comments);
        Assert.DoesNotContain("comment", PartHelper.Text(p, "/word/document.xml").Replace("xmlns", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(0, Count(PartHelper.Text(p, "/word/commentsExtended.xml"), "<w15:commentEx "));
    }

    [Fact]
    public void SetText_KeepsTheParagraphIdSoRepliesStayLinked()
    {
        using var d = Document.FromPackage(WithComments());
        Comment c = d.Comments.Find(0)!;
        c.SetText("Changed\nTwo");
        Assert.Equal("Changed\nTwo", c.Text);
        Assert.Equal("11111111", c.ParaId);
        Assert.Single(c.Replies);
        c.Author = "Zed";
        c.Date = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        c.Initials = null;
        Assert.Equal("Zed", c.Author);
        Assert.Null(c.Initials);
        Assert.Contains("w:date=\"2020-01-01T00:00:00Z\"", c.Container.Node.OuterXml);
    }

    private static int Count(string text, string find)
    {
        int n = 0;
        for (int i = text.IndexOf(find, StringComparison.Ordinal); i >= 0; i = text.IndexOf(find, i + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
