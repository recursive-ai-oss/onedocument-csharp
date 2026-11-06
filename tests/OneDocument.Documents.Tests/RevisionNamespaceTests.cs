using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Markup;
using static RecursiveAI.OneDocument.Documents.Tests.Fixture;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>Markup that moves under another parent keeps the namespace declarations it relies on, when they were not on the part root.</summary>
public class RevisionNamespaceTests
{
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    // The prefixes are declared on the paragraph, so they are gone once the paragraph is.
    private static string P(string inner, string pPr = "") =>
        $"<w:p xmlns:r=\"{R}\" xmlns:w14=\"{W14}\">{(pPr.Length > 0 ? "<w:pPr>" + pPr + "</w:pPr>" : "")}{inner}</w:p>";

    private static void AssertWellFormedAndBound(RevisionSet set) => AssertBound(set.Parts[0].Markup.Root.OuterXml);

    /// <summary>Parsing alone accepts unbound prefixes in a fragment, so check that each prefix used resolves where it is used.</summary>
    private static void AssertBound(string xml)
    {
        MarkupDocument doc = MarkupDocument.Parse(Encoding.UTF8.GetBytes(xml));
        Visit(doc.Root);
    }

    private static void Visit(MarkupNode n)
    {
        if (n.Prefix.Length > 0)
        {
            Assert.True(n.TryResolvePrefix(Encoding.UTF8.GetString(n.Prefix), out _), "unbound element prefix in " + n.Name);
        }

        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0)
            {
                Assert.True(n.TryResolvePrefix(Encoding.UTF8.GetString(a.Prefix), out _), "unbound attribute prefix in " + n.Name);
            }
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            Visit(c);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JoiningAParagraphCarriesItsDeclarations(bool accept)
    {
        // The first paragraph's mark was inserted and its content uses r: and w14:; resolving joins it into the second paragraph.
        string body = P("<w:r w14:x=\"1\"><w:t>one</w:t></w:r><w:hyperlink r:id=\"rId9\"><w:r><w:t>link</w:t></w:r></w:hyperlink>", "<w:rPr><w:del w:id=\"1\" w:author=\"A\"/></w:rPr>") +
            "<w:p><w:r><w:t>two</w:t></w:r></w:p>";
        RevisionSet set = Set(body);
        Assert.Equal(1, accept ? set.AcceptAll() : set.RejectAll());
        AssertWellFormedAndBound(set);
        string xml = Xml(set);
        if (accept)
        {
            Assert.Contains("r:id=\"rId9\"", xml, StringComparison.Ordinal);
            Assert.Contains("xmlns:r=", xml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InsertParagraphCopiesParagraphPropertiesWithTheirPrefixes()
    {
        InsertParagraphAndMove(move: false);
    }

    [Fact]
    public void MovedRunsKeepTheirPrefixes()
    {
        InsertParagraphAndMove(move: true);
    }

    private static void InsertParagraphAndMove(bool move)
    {
        string body = P("<w:r w14:x=\"1\"><w:t>Hello world</w:t></w:r>", "<w:rPr><w14:ligatures w14:val=\"all\"/></w:rPr>") + "<w:p><w:r><w:t>Other</w:t></w:r></w:p>";
        RevisionSet set = Set(body);
        RevisionAuthor a = set.CreateAuthor("Ann", null);
        if (move)
        {
            a.MoveParagraph(Paragraph(set, 0), Paragraph(set, 1));
        }
        else
        {
            a.InsertParagraph(Paragraph(set, 0), "added");
        }

        set.Refresh();
        AssertWellFormedAndBound(set);
        Assert.NotEmpty(set.Revisions);
        set.RejectAll();
        AssertWellFormedAndBound(set);
        Assert.Empty(set.Revisions);
        Assert.Equal("Hello world\nOther\n", Text(set, RevisionView.Accepted));
    }

    [Fact]
    public void ARevisionInBothBranchesOfAlternateContentIsListedOnce()
    {
        // ECMA-376 Part 3 §9.3: the two branches are the same content in two forms, so the change inside them is one change.
        const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        string content = "<w:txbxContent><w:p><w:ins w:id=\"7\" w:author=\"Ann\" w:date=\"2024-01-01T00:00:00Z\"><w:r><w:t>new</w:t></w:r></w:ins></w:p></w:txbxContent>";
        string xml = $"<w:document xmlns:w=\"{TestDocs.WNs}\" xmlns:mc=\"{Mc}\" xmlns:wps=\"{Wps}\" xmlns:v=\"urn:schemas-microsoft-com:vml\"><w:body><w:p><w:r><mc:AlternateContent>" +
            $"<mc:Choice Requires=\"wps\"><w:drawing><wps:wsp><wps:txbx>{content}</wps:txbx></wps:wsp></w:drawing></mc:Choice>" +
            $"<mc:Fallback><w:pict><v:shape id=\"s1\"><v:textbox>{content}</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p></w:body></w:document>";
        var p = TestDocs.Package("");
        p.FindPart("/word/document.xml")!.SetBytes(Encoding.UTF8.GetBytes(xml));
        using Document d = Document.FromPackage(p);
        Assert.Single(d.Revisions.Revisions);
    }

    [Fact]
    public void TextBoxCopyKeepsPrefixesDeclaredOnTheExposedBranch()
    {
        const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        string content = "<w:txbxContent><w:p><w:r><w:t>Hello</w:t></w:r><w:hyperlink r:id=\"rId5\"><w:r><w:t>k</w:t></w:r></w:hyperlink></w:p></w:txbxContent>";
        string xml = $"<w:document xmlns:w=\"{TestDocs.WNs}\" xmlns:mc=\"{Mc}\" xmlns:wps=\"{Wps}\" xmlns:v=\"urn:schemas-microsoft-com:vml\"><w:body><w:p><w:r><mc:AlternateContent>" +
            $"<mc:Choice Requires=\"wps\" xmlns:r=\"{R}\"><w:drawing><wps:wsp><wps:txbx>{content}</wps:txbx></wps:wsp></w:drawing></mc:Choice>" +
            $"<mc:Fallback xmlns:r=\"{R}\"><w:pict><v:shape id=\"s1\"><v:textbox>{content}</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p></w:body></w:document>";
        var p = TestDocs.Package("");
        p.FindPart("/word/document.xml")!.SetBytes(Encoding.UTF8.GetBytes(xml));
        using Document d = Document.FromPackage(p);
        TextBox box = d.TextBoxes()[0];
        d.Revisions.CreateAuthor("Ann", null).InsertText(box.Paragraphs().First(), 5, "!");
        d.CommitChanges();
        string saved = d.MainMarkup.Root.OuterXml;
        AssertBound(saved);
        Assert.Equal(2, saved.Split("r:id=\"rId5\"").Length - 1);
    }
}
