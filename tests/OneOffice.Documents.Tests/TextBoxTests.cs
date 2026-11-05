using System.Text;
using System.Linq;
using RecursiveAI.OneOffice.Documents.Revisions;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Documents.Tests;

public class TextBoxTests
{
    private const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string V = "urn:schemas-microsoft-com:vml";

    private static string Doc(string body) =>
        $"<w:document xmlns:w=\"{TestDocs.WNs}\" xmlns:wps=\"{Wps}\" xmlns:mc=\"{Mc}\" xmlns:v=\"{V}\" xmlns:r=\"{TestDocs.RNs}\"><w:body>{body}</w:body></w:document>";

    private static Document Open(string body)
    {
        InMemoryPackage p = TestDocs.Package("");
        p.FindPart("/word/document.xml")!.SetBytes(Encoding.UTF8.GetBytes(Doc(body)));
        return Document.FromPackage(p);
    }

    private static string Content(string text) => $"<w:txbxContent><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:txbxContent>";

    private static string DrawingBox(string text, string txbxAttrs = "") =>
        $"<w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wps:wsp><wps:txbx{txbxAttrs}>{Content(text)}</wps:txbx></wps:wsp></w:drawing></mc:Choice>" +
        $"<mc:Fallback><w:pict><v:shape id=\"s1\"><v:textbox>{Content(text)}</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r>";

    [Fact]
    public void FindsTheDrawingBoxOnceAndKeepsTheVmlCopyAsAlternate()
    {
        using Document d = Open($"<w:p><w:r><w:t>Anchor</w:t></w:r>{DrawingBox("Inside")}</w:p>");
        TextBox box = Assert.Single(d.TextBoxes());
        Assert.Equal(TextBoxKind.DrawingMl, box.Kind);
        Assert.Equal("Inside", box.Text);
        Assert.Equal("Anchor", box.Anchor!.Text.Substring(0, 6));
        Assert.True(box.Alternate.IsValid);
        Assert.Equal(StoryKind.Body, box.HostStory);
        Assert.Same(box, d.TextBoxes()[0]);

        // The box's paragraphs are not paragraphs of the body.
        Assert.Single(d.Body.Paragraphs());
        Assert.Single(d.Body.Paragraphs().First().TextBoxes());
    }

    [Fact]
    public void FindsAPlainVmlBoxAndBoxesInTables()
    {
        using Document d = Open(
            $"<w:p><w:r><w:pict><v:rect id=\"a\"><v:textbox>{Content("Vml one")}</v:textbox></v:rect></w:pict></w:r></w:p>" +
            $"<w:tbl><w:tr><w:tc><w:p><w:r><w:pict><v:rect id=\"b\"><v:textbox>{Content("Vml two")}</v:textbox></v:rect></w:pict></w:r></w:p></w:tc></w:tr></w:tbl>");
        IReadOnlyList<TextBox> boxes = d.TextBoxes();
        Assert.Equal(["Vml one", "Vml two"], boxes.Select(b => b.Text).ToArray());
        Assert.All(boxes, b => Assert.Equal(TextBoxKind.Vml, b.Kind));
        Assert.All(boxes, b => Assert.False(b.Alternate.IsValid));
        Assert.Equal(2, boxes.Count(b => b.Anchor is not null));
    }

    [Fact]
    public void UnknownRequiredNamespaceExposesTheFallbackBranch()
    {
        using Document d = Open(
            "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"zzz\" xmlns:zzz=\"urn:unknown\">" + Content("new") + "</mc:Choice>" +
            "<mc:Fallback>" + Content("old") + "</mc:Fallback></mc:AlternateContent></w:r></w:p>");
        TextBox box = Assert.Single(d.TextBoxes());
        Assert.Equal("old", box.Text);
    }

    [Fact]
    public void NestedBoxesAreReachedThroughTheOuterBox()
    {
        string inner = $"<w:r><w:pict><v:rect><v:textbox>{Content("inner")}</v:textbox></v:rect></w:pict></w:r>";
        using Document d = Open($"<w:p><w:r><w:pict><v:rect><v:textbox><w:txbxContent><w:p>{inner}</w:p></w:txbxContent></v:textbox></v:rect></w:pict></w:r></w:p>");
        TextBox outer = Assert.Single(d.TextBoxes());
        TextBox nested = Assert.Single(outer.TextBoxes());
        Assert.Equal("inner", nested.Text);
    }

    [Fact]
    public void EditingTheBoxIsCopiedToTheAlternateOnCommit()
    {
        using Document d = Open($"<w:p>{DrawingBox("Before")}</w:p>");
        TextBox box = d.TextBoxes()[0];
        box.Paragraphs().First().SetText("After");
        d.CommitChanges();
        string xml = Encoding.UTF8.GetString(d.Package.FindPart("/word/document.xml")!.GetBytes().Span);
        Assert.Equal(2, CountOf(xml, "After"));
        Assert.DoesNotContain("Before", xml);

        // Untouched boxes are left exactly as they are.
        using Document d2 = Open($"<w:p>{DrawingBox("Same")}</w:p>");
        d2.TextBoxes();
        Assert.False(d2.TextBoxes()[0].SyncAlternate());
        Assert.False(d2.HasUncommittedChanges);
    }

    [Fact]
    public void TrackedChangesInsideABox()
    {
        using Document d = Open($"<w:p>{DrawingBox("Hello world")}</w:p>");
        TextBox box = d.TextBoxes()[0];
        RevisionAuthor author = d.Revisions.CreateAuthor("Ann", new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        author.InsertText(box.Paragraphs().First().Markup.Node, 5, ",");
        d.CommitChanges();
        d.Revisions.Refresh();
        Assert.Equal(2, d.Revisions.Revisions.Count); // one per branch; the copy carries the same id
        Assert.Contains("Hello", box.Paragraphs().First().GetText(RevisionView.Rejected));
        Assert.Equal("Hello world", box.Paragraphs().First().GetText(RevisionView.Rejected));
        Assert.Equal("Hello, world", box.Paragraphs().First().GetText(RevisionView.Accepted));
        string xml = Encoding.UTF8.GetString(d.Package.FindPart("/word/document.xml")!.GetBytes().Span);
        Assert.Equal(2, CountOf(xml, "<w:ins "));
        d.Revisions.AcceptAll();
        Assert.Empty(d.Revisions.Revisions);
        d.CommitChanges();
        xml = Encoding.UTF8.GetString(d.Package.FindPart("/word/document.xml")!.GetBytes().Span);
        Assert.DoesNotContain("<w:ins ", xml, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(xml, "Hello"));
    }

    [Fact]
    public void LinkedDrawingBoxesListTheirChainInOrder()
    {
        string second = "<w:r><w:drawing><wps:wsp><wps:linkedTxbx id=\"7\" seq=\"2\"/></wps:wsp></w:drawing></w:r>";
        string first = "<w:r><w:drawing><wps:wsp><wps:linkedTxbx id=\"7\" seq=\"1\"/></wps:wsp></w:drawing></w:r>";
        using Document d = Open(
            $"<w:p><w:r><w:drawing><wps:wsp><wps:txbx id=\"7\">{Content("Flow")}</wps:txbx></wps:wsp></w:drawing></w:r></w:p><w:p>{second}</w:p><w:p>{first}<w:r><w:t>x</w:t></w:r></w:p>");
        TextBox box = Assert.Single(d.TextBoxes());
        Assert.Equal(7, box.ChainId);
        Assert.Equal(3, box.Chain.Count);
        Assert.Same(box, box.Chain[0].Story);
        Assert.Null(box.Chain[1].Story);
        Assert.Equal("x", box.Chain[1].Anchor!.Text);
        Assert.Equal("", box.Chain[2].Anchor!.Text);
    }

    [Fact]
    public void LinkedVmlBoxesFollowTheNextTextboxStyle()
    {
        string Shape(string id, string text, string? next) =>
            $"<w:p><w:r><w:pict><v:shape id=\"{id}\"><v:textbox{(next is null ? "" : $" style=\"mso-next-textbox:#{next}\"")}>{Content(text)}</v:textbox></v:shape></w:pict></w:r></w:p>";
        using Document d = Open(Shape("c", "third", null) + Shape("a", "first", "b") + Shape("b", "second", "c"));
        TextBox third = d.TextBoxes()[0];
        Assert.Equal(["first", "second", "third"], third.Chain.Select(f => f.Story!.Text).ToArray());
        TextBox first = d.TextBoxes()[1];
        Assert.Equal(third.Chain.Count, first.Chain.Count);
    }

    [Fact]
    public void WrapParagraphAcceptsAnyParagraphOfTheDocumentOnly()
    {
        using Document d = Open($"<w:p>{DrawingBox("Wrapped")}</w:p>");
        TextBox box = d.TextBoxes()[0];
        Paragraph p = d.WrapParagraph(new Schema.MarkupRef(box.Alternate.Node.FirstChild));
        Assert.Equal("Wrapped", p.Text);
        Assert.Throws<ArgumentException>(() => d.WrapParagraph(box.Container));
        using Document other = Open("<w:p/>");
        Assert.Throws<ArgumentException>(() => other.WrapParagraph(new Schema.MarkupRef(d.Body.Paragraphs().First().Markup.Node)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HubRevisionOverloadsWorkOnBoxParagraphs(bool drawing)
    {
        string table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>c1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>c2</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/>";
        string content = $"<w:txbxContent><w:p><w:r><w:t>Hello world</w:t></w:r></w:p>{table}</w:txbxContent>";
        string box = drawing
            ? $"<w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wps:wsp><wps:txbx>{content}</wps:txbx></wps:wsp></w:drawing></mc:Choice><mc:Fallback><w:pict><v:shape id=\"s1\"><v:textbox>{content}</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r>"
            : $"<w:r><w:pict><v:shape id=\"s1\"><v:textbox>{content}</v:textbox></v:shape></w:pict></w:r>";
        using Document d = Open($"<w:p>{box}</w:p>");
        TextBox tb = d.TextBoxes()[0];
        RevisionAuthor author = d.Revisions.CreateAuthor("Ann", new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero), RevisionIds.StartingAt(100));
        Paragraph first = tb.Paragraphs().First();
        author.ReplaceText(first, 0, 5, "HELLO");
        author.FormatText(first, 6, 5, new RunPropertiesDelta { Bold = true });
        author.ChangeParagraphProperties(tb.Paragraphs().Last(), new ParagraphPropertiesDelta { Alignment = ParagraphAlignment.Center });
        Paragraph added = author.InsertParagraph(first, "added");
        Assert.Equal("added", added.Text);
        Assert.Same(d, added.Owner);
        Table t = tb.Blocks().OfType<Table>().Single();
        TableRow row = author.InsertRow(t, 1, ["n1", "n2"]);
        author.MarkRowDeleted(t.Rows().First());
        Assert.Equal("n1", row.Cells().First().Paragraphs().First().Text);
        d.CommitChanges();
        d.Revisions.Refresh();
        Assert.Contains(d.Revisions.Revisions, r => r.Kind == RevisionKind.Insertion && r.Id >= 100);
        Assert.Contains(d.Revisions.Revisions, r => r.Kind == RevisionKind.TableRowDeleted || r.Kind == RevisionKind.Deletion);

        // Rejecting everything restores the text; the copy in the other branch stays equal to the exposed one.
        d.Revisions.RejectAll();
        d.CommitChanges();
        Assert.Empty(d.Revisions.Revisions);
        TextBox again = d.TextBoxes()[0];
        Assert.Equal("Hello world", again.Paragraphs().First().Text);
        if (drawing)
        {
            Assert.Equal(again.Container.Node.OuterXml, again.Alternate.Node.OuterXml);
        }
    }

    private static int CountOf(string text, string needle)
    {
        int n = 0;
        for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
