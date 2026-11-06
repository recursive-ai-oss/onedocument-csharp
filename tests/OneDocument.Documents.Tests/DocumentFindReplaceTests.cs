using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Protection;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class DocumentFindReplaceTests
{
    private const string W = TestDocs.WNs;
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private static readonly DateTimeOffset When = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private static string T(string text) => "<w:t xml:space=\"preserve\">" + text + "</w:t>";

    private static string Run(string text, string props = "") => "<w:r>" + (props.Length > 0 ? "<w:rPr>" + props + "</w:rPr>" : "") + T(text) + "</w:r>";

    private static string AllText(Document d) =>
        string.Join("|", d.Body.Paragraphs().Select(p => p.GetText(RevisionView.Accepted)));

    private static string Xml(Document d) => d.MainMarkup.Root.OuterXml;

    [Fact]
    public void LiteralMatchAcrossRunsKeepsTheFirstRunsFormatting()
    {
        Document d = TestDocs.Open("<w:p>" + Run("The qui", "<w:b/>") + Run("ck brown", "<w:i/>") + Run(" fox") + "</w:p>");
        ReplaceReport r = d.Replace("quick brown", "slow red");
        Assert.Equal(1, r.Count);
        Assert.Equal("The slow red fox", AllText(d));
        Assert.Equal(new TextLocation("/word/document.xml", "body", 0, 4), r.Items[0].Location);
        Assert.Equal("quick brown", r.Items[0].Original);
        // the new text is in a run that carries the first matched run's properties (bold), not the second's (italic)
        Assert.Contains("<w:r><w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">slow red</w:t></w:r>", Xml(d));
        Assert.DoesNotContain("<w:i/>", Xml(d));
    }

    [Fact]
    public void MatchesAcrossHyperlinkBookmarkFieldResultAndContentControl()
    {
        string body = "<w:p>" + Run("See ")
            + "<w:hyperlink r:id=\"rId9\">" + Run("the full ", "<w:rStyle w:val=\"Hyperlink\"/>") + "</w:hyperlink>"
            + "<w:bookmarkStart w:id=\"1\" w:name=\"mark\"/>" + Run("rep") + "<w:bookmarkEnd w:id=\"1\"/>"
            + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> REF x </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" + Run("ort 4")
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
            + "<w:sdt><w:sdtPr><w:alias w:val=\"c\"/></w:sdtPr><w:sdtContent>" + Run(" and more") + "</w:sdtContent></w:sdt></w:p>";
        Document d = TestDocs.Open(body);
        Assert.Equal("See the full report 4 and more", d.Body.Paragraphs().First().GetText(RevisionView.Accepted));
        ReplaceReport r = d.Replace("See the full report 4 and", "OK");
        Assert.Equal(1, r.Count);
        Assert.Equal("OK more", AllText(d));
        string xml = Xml(d);
        // structure around the text stays: the bookmark, the field marks, the instruction, the hyperlink and the control
        Assert.Contains("<w:bookmarkStart w:id=\"1\" w:name=\"mark\"/>", xml);
        Assert.Contains("<w:bookmarkEnd w:id=\"1\"/>", xml);
        Assert.Contains("REF x", xml);
        Assert.Equal(3, Count(xml, "<w:fldChar"));
        Assert.Contains("<w:sdtPr><w:alias w:val=\"c\"/></w:sdtPr>", xml);
    }

    [Fact]
    public void TextInOtherAuthorsInsertionsMatchesAndDeletedTextDoesNot()
    {
        string body = "<w:p>" + Run("Total: ") + Fixture.Ins(5, "Ann", Run("12")) + Fixture.Del(6, "Ann", "99") + Run("0 units") + "</w:p>";
        Document d = TestDocs.Open(body);
        ReplaceReport r = d.Replace("Total: 120", "Sum: 7");
        Assert.Equal(1, r.Count);
        Assert.Equal("Sum: 7 units", AllText(d));
        // the tracked deletion stays where it was
        Assert.Contains("<w:delText xml:space=\"preserve\">99</w:delText>", Xml(d));
        Assert.Empty(d.Replace("Total: 1299", "x").Items);
    }

    [Fact]
    public void ReplacingInsideAnotherAuthorsInsertionKeepsItAroundTheNewText()
    {
        // Found in a corpus run: every run of the paragraph sits in one w:ins, and the match is in the middle of it.
        Document d = TestDocs.Open("<w:p>" + Fixture.Ins(5, "Ann", Run("ab ") + Run("o") + Run("f cd")) + "</w:p>");
        Assert.Equal(1, d.Replace("of", "X").Count);
        Assert.Equal("ab X cd", AllText(d));
        Assert.Equal(1, Count(Xml(d), "<w:ins "));
        Assert.Contains("<w:t xml:space=\"preserve\">X</w:t>", Xml(d));
        // and the same replacement tracked: accepting gives the new text, rejecting everything gives the paragraph without Ann's text
        Document t = TestDocs.Open("<w:p>" + Fixture.Ins(5, "Ann", Run("ab ") + Run("o") + Run("f cd")) + "</w:p>");
        Assert.Equal(1, t.Replace("of", "X", new DocumentReplaceOptions { TrackedAuthor = "Bob" }).Count);
        t.Revisions.AcceptAll();
        Assert.Equal("ab X cd", AllText(t));
    }

    [Fact]
    public void RegexGroupsWholeWordAndCase()
    {
        Document d = TestDocs.Open("<w:p>" + Run("Order 123-45 and cat, concat, Cat") + "</w:p>");
        ReplaceReport r = d.Replace("(\\d+)-(\\d+)", "$2/$1", new DocumentReplaceOptions { Search = new SearchOptions { UseRegex = true } });
        Assert.Equal(1, r.Count);
        Assert.Equal("Order 45/123 and cat, concat, Cat", AllText(d));
        r = d.Replace("cat", "dog", new DocumentReplaceOptions { Search = new SearchOptions { WholeWord = true, MatchCase = true } });
        Assert.Equal(1, r.Count);
        Assert.Equal("Order 45/123 and dog, concat, Cat", AllText(d));
        r = d.Replace("cat", "dog", new DocumentReplaceOptions { Search = new SearchOptions { WholeWord = true } });
        Assert.Equal(1, r.Count);
        Assert.Equal("Order 45/123 and dog, concat, dog", AllText(d));
    }

    [Fact]
    public void SeveralMatchesInOneParagraphAreAllReplaced()
    {
        Document d = TestDocs.Open("<w:p>" + Run("a1 b") + Run("a1 c a") + Run("1") + "</w:p>");
        ReplaceReport r = d.Replace("a1", "<&>");
        Assert.Equal(3, r.Count);
        Assert.Equal("<&> b<&> c <&>", AllText(d));
        Assert.Equal([0, 4, 9], r.Items.Select(i => i.Location.Offset).ToArray());
    }

    [Fact]
    public void TabAndLineBreakReplacements()
    {
        Document d = TestDocs.Open("<w:p>" + Run("a;b;c") + "</w:p>");
        d.Replace(";", "^t", new DocumentReplaceOptions { Search = new SearchOptions { SpecialCodes = true } });
        Assert.Equal("a\tb\tc", AllText(d));
        Assert.Equal(2, Count(Xml(d), "<w:tab/>"));
        d.Replace("\t", "^l", new DocumentReplaceOptions { Search = new SearchOptions { SpecialCodes = true } });
        Assert.Equal("a\nb\nc", AllText(d));
        Assert.Equal(2, Count(Xml(d), "<w:br/>"));
        Assert.Throws<ArgumentException>(() => d.Replace("a", "^p", new DocumentReplaceOptions { Search = new SearchOptions { SpecialCodes = true } }));
    }

    [Fact]
    public void RunPropertiesGivenAreMergedOverTheFirstRuns()
    {
        Document d = TestDocs.Open("<w:p>" + Run("keep ") + Run("this one", "<w:b/><w:color w:val=\"00FF00\"/>") + "</w:p>");
        d.Replace("this", "THAT", new DocumentReplaceOptions { RunPropertiesXml = "<w:color w:val=\"FF0000\"/><w:i/>" });
        string xml = Xml(d);
        Assert.Contains("<w:rPr><w:b/><w:color w:val=\"FF0000\"/><w:i/></w:rPr><w:t xml:space=\"preserve\">THAT</w:t>", xml);
        Assert.Equal("keep THAT one", AllText(d));
    }

    [Fact]
    public void UnknownMarkupAndAttributesSurvive()
    {
        string body = "<w:p w:rsidR=\"00AA11BB\"><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r w:rsidRPr=\"00CC22DD\"><w:rPr><w:b/></w:rPr><w:t>He</w:t></w:r><x:note xmlns:x=\"urn:example\" x:k=\"v\"/><w:r><w:t>llo world</w:t></w:r><w:proofErr w:type=\"spellEnd\"/></w:p>";
        Document d = TestDocs.Open(body);
        d.Replace("Hello", "Howdy");
        string xml = Xml(d);
        Assert.Contains("w:rsidR=\"00AA11BB\"", xml);
        Assert.Contains("<w:jc w:val=\"center\"/>", xml);
        Assert.Contains("<x:note xmlns:x=\"urn:example\" x:k=\"v\"/>", xml);
        Assert.Contains("<w:proofErr w:type=\"spellEnd\"/>", xml);
        Assert.Equal("Howdy world", AllText(d));
    }

    [Fact]
    public void NoMatchLeavesTheDocumentUnmodified()
    {
        Document d = TestDocs.Open("<w:p>" + Run("nothing here") + "</w:p>");
        ReplaceReport r = d.Replace("absent", "x");
        Assert.Equal(0, r.Count);
        Assert.False(d.HasUncommittedChanges);
        Assert.Empty(d.Find("absent"));
    }

    [Fact]
    public void TrackedReplacementWritesInsAndDelThatRejectAndAcceptBack()
    {
        Document d = TestDocs.Open("<w:p>" + Run("The qui", "<w:b/>") + Run("ck fox") + "</w:p><w:p>" + Run("quick again") + "</w:p>");
        ReplaceReport r = d.Replace("quick", "slow", new DocumentReplaceOptions { TrackedAuthor = "Edit Bot", TrackedDate = When });
        Assert.Equal(2, r.Count);
        Assert.All(r.Items, i => Assert.True(i.Tracked));
        string xml = Xml(d);
        Assert.Contains("w:author=\"Edit Bot\"", xml);
        Assert.Contains("w:date=\"2025-03-04T05:06:07Z\"", xml);
        Assert.Equal(4, d.Revisions.Revisions.Count);
        Assert.Equal("The slow fox|slow again", AllText(d));
        Assert.Equal("The quick fox|quick again", string.Join("|", d.Body.Paragraphs().Select(p => p.GetText(RevisionView.Rejected))));
        d.Revisions.RejectAll();
        Assert.Equal("The quick fox|quick again", AllText(d));
    }

    [Fact]
    public void TrackedReplacementAcceptsToTheNewText()
    {
        Document d = TestDocs.Open("<w:p>" + Run("alpha beta gamma") + "</w:p>");
        d.Replace("beta", "BETA", new DocumentReplaceOptions { TrackedAuthor = "A", TrackedIds = RevisionIds.StartingAt(40) });
        Assert.Equal(2, d.Revisions.Revisions.Count);
        Assert.Contains("w:id=\"40\"", Xml(d));
        d.Revisions.AcceptAll();
        Assert.Equal("alpha BETA gamma", AllText(d));
        Assert.Empty(d.Revisions.Revisions);
    }

    [Fact]
    public void RegexTimeoutIsATypedLimitError()
    {
        var ctx = new OpenContext(new OfficeLimits { RegexMatchTimeoutMilliseconds = 40 });
        Document d = Document.FromPackage(TestDocs.Package("<w:p>" + Run(new string('a', 5000) + "!") + "</w:p>"), ctx);
        // a back-reference rules out the linear-time engine; the nested quantifier makes the backtracking engine exponential
        var options = new DocumentFindOptions { Search = new SearchOptions { UseRegex = true } };
        OfficeLimitException ex = Assert.Throws<OfficeLimitException>(() => d.Find("^(a+)+\\1?$", options));
        Assert.Equal("RegexMatchTimeoutMilliseconds", ex.LimitName);
        Assert.Equal("limit.regex-match-timeout-milliseconds", ex.Defect.Code);
        Assert.Throws<OfficeLimitException>(() => d.Replace("^(a+)+\\1?$", "x", new DocumentReplaceOptions { Search = new SearchOptions { UseRegex = true } }));
        Assert.False(d.HasUncommittedChanges);
    }

    [Fact]
    public void LinearRegexRunsOnLargeInputWithoutTimeout()
    {
        Document d = Document.FromPackage(TestDocs.Package("<w:p>" + Run(new string('a', 20000) + "!") + "</w:p>"), new OpenContext(new OfficeLimits { RegexMatchTimeoutMilliseconds = 2000 }));
        Assert.Empty(d.Find("^(a+)+$", new DocumentFindOptions { Search = new SearchOptions { UseRegex = true } }));
    }

    [Fact]
    public void FieldCodesAreSearchedOnlyWhenAsked()
    {
        string body = "<w:p>" + Run("Go to ")
            + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \"http://old.example/a\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" + Run("old.example")
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
        Document d = TestDocs.Open(body);
        ReplaceReport r = d.Replace("old.example", "new.example");
        Assert.Equal(1, r.Count);
        Assert.Contains("old.example/a", Xml(d));
        r = d.Replace("old.example", "new.example", new DocumentReplaceOptions { IncludeFieldCodes = true });
        Assert.Equal(1, r.Count);
        Assert.Equal("field-code", r.Items[0].Location.Story);
        Assert.Contains("HYPERLINK \"http://new.example/a\"", Xml(d));
        Assert.Equal("Go to new.example", AllText(d));
        // tracked mode does not touch instructions and says so
        Document t = TestDocs.Open("<w:p>" + Run("Go ")
            + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \"http://old.example/a\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" + Run("link")
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        r = t.Replace("old.example", "n", new DocumentReplaceOptions { IncludeFieldCodes = true, TrackedAuthor = "x" });
        Assert.Equal(0, r.Count);
        Assert.Single(r.Issues);
        Assert.Equal("markup-unsupported", r.Issues[0].Code);
    }

    [Fact]
    public void HeadersFootersNotesCommentsAndTextBoxesAreSearchedAndScoped()
    {
        OpcPackage p = BuildPackage();
        Document d = Document.FromPackage(p);
        IReadOnlyList<TextHit> hits = d.Find("needle");
        Assert.Equal(["body", "textbox", "header", "footnotes", "comments"], hits.Select(h => h.Location.Story).ToArray());
        Assert.Single(d.Find("needle", new DocumentFindOptions { Stories = DocumentStories.Body }));
        Assert.Single(d.Find("needle", new DocumentFindOptions { Stories = DocumentStories.TextBoxes }));
        ReplaceReport r = d.Replace("needle", "thread");
        Assert.Equal(5, r.Count);
        Assert.Empty(d.Find("needle"));
        // the VML copy of the text box is brought up to date when the changes are committed
        d.CommitChanges();
        string main = Encoding.UTF8.GetString(p.FindPart("/word/document.xml")!.OpenRead().ReadAllBytesForTest());
        Assert.DoesNotContain("needle", main);
        Assert.Equal(3, Count(main, ">thread<"));
    }

    [Fact]
    public void TrackedReplacementInTextBoxesAcceptsAndRejects()
    {
        Document d = Document.FromPackage(BuildPackage());
        Assert.Equal(5, d.Replace("needle", "thread", new DocumentReplaceOptions { TrackedAuthor = "Bob" }).Count);
        Assert.Equal(5, d.Find("thread").Count);
        d.Revisions.AcceptAll();
        Assert.Equal(5, d.Find("thread").Count);
        Assert.Empty(d.Find("needle"));
        Document r = Document.FromPackage(BuildPackage());
        r.Replace("needle", "thread", new DocumentReplaceOptions { TrackedAuthor = "Bob" });
        r.Revisions.RejectAll();
        Assert.Equal(5, r.Find("needle").Count);
        Assert.Empty(r.Find("thread"));
    }

    [Fact]
    public void TrackedReplacementRightAfterAnAnchoredObjectKeepsTheObject()
    {
        // Found in a corpus run: the replaced words came right after a drawing, and accepting the change removed the drawing too.
        string drawing = "<w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"/></w:drawing></w:r>";
        Document d = TestDocs.Open("<w:p>" + drawing + Run("the end") + "</w:p>");
        Assert.Equal(1, d.Replace("the", "THE", new DocumentReplaceOptions { TrackedAuthor = "Bob" }).Count);
        d.Revisions.AcceptAll();
        Assert.Contains("<w:drawing>", Xml(d));
        Assert.Equal("THE end", AllText(d));
        Document u = TestDocs.Open("<w:p>" + drawing + Run("the end") + "</w:p>");
        Assert.Equal(1, u.Replace("the", "THE").Count);
        Assert.Contains("<w:drawing>", Xml(u));
        // the same when the drawing sits in alternate content, as Word writes anchored shapes and text boxes
        string alt = "<w:r><mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice Requires=\"wps\"><w:drawing/></mc:Choice><mc:Fallback><w:pict/></mc:Fallback></mc:AlternateContent></w:r>";
        Document a = TestDocs.Open("<w:p>" + alt + Run("the end") + "</w:p>");
        Assert.Equal(1, a.Replace("the", "THE", new DocumentReplaceOptions { TrackedAuthor = "Bob" }).Count);
        a.Revisions.AcceptAll();
        Assert.Contains("<mc:AlternateContent", Xml(a));
        // a drawing inside the matched text still goes with it when the change is tracked and accepted
        Document m = TestDocs.Open("<w:p>" + Run("ab") + drawing + Run("cd") + "</w:p>");
        Assert.Equal(1, m.Replace("abcd", "X", new DocumentReplaceOptions { TrackedAuthor = "Bob" }).Count);
        m.Revisions.AcceptAll();
        Assert.Equal("X", AllText(m));
    }

    [Fact]
    public void ReplacedDocumentSavesAndReopens()
    {
        using Document d = Document.Create();
        d.Body.AddParagraph("one two three");
        d.Replace("two thr", "2 3");
        using var ms = new MemoryStream();
        d.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        using Document back = Document.Open(ms);
        Assert.EndsWith("one 2 3ee", AllText(back), StringComparison.Ordinal);
    }

    [Fact]
    public void SamplesForExternalChecksAreWritten()
    {
        string dir = Path.Combine(Path.GetTempPath(), "operation-samples");
        Directory.CreateDirectory(dir);
        using (Document d = Document.Create())
        {
            d.Body.AddParagraph("The quick brown fox jumps over the lazy dog.");
            Paragraph second = d.Body.AddParagraph("Second paragraph, quick again.");
            d.Replace("quick", "slow", new DocumentReplaceOptions { TrackedAuthor = "Sample Author", TrackedDate = When });
            d.Replace("lazy dog", "sleepy cat", new DocumentReplaceOptions { RunPropertiesXml = "<w:i/>" });
            d.AddEditingException(second, second);
            d.Protection.Protect(DocumentEditRestriction.TrackedChanges, "sample", false, new ProtectionOptions { SpinCount = 1000 });
            d.WriteProtection.Set("reserve", true, new ProtectionOptions { SpinCount = 1000 });
            d.SaveAs(Path.Combine(dir, "replace-protect.docx"));
        }

        Document box = Document.FromPackage(BuildPackage());
        box.Replace("needle", "thread");
        box.SaveAs(Path.Combine(dir, "replace-textbox.docx"));
        box.Dispose();
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    private static OpcPackage BuildPackage()
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml")]);
        string choice = "<w:txbxContent><w:p>" + Run("needle in box") + "</w:p></w:txbxContent>";
        string vml = "<w:txbxContent><w:p>" + Run("needle in box") + "</w:p></w:txbxContent>";
        string box = "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wp:anchor><a:graphic><a:graphicData><wps:wsp><wps:txbx>" + choice
            + "</wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice><mc:Fallback><w:pict><v:shape><v:textbox>" + vml + "</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>";
        string ns = $"xmlns:w=\"{W}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:v=\"urn:schemas-microsoft-com:vml\" mc:Ignorable=\"wps\"";
        p.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document {ns}><w:body><w:p>{Run("a needle here")}</w:p>{box}</w:body></w:document>"));
        p.AddPart("/word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml",
            Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:hdr xmlns:w=\"{W}\"><w:p>{Run("head needle")}</w:p></w:hdr>"));
        p.AddPart("/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml",
            Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:footnotes xmlns:w=\"{W}\"><w:footnote w:id=\"1\"><w:p>{Run("note needle")}</w:p></w:footnote></w:footnotes>"));
        p.AddPart("/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml",
            Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:comments xmlns:w=\"{W}\"><w:comment w:id=\"0\" w:author=\"x\"><w:p>{Run("comment needle")}</w:p></w:comment></w:comments>"));
        p.SetRelationships("/word/document.xml",
        [
            new Relationship("rId2", Rel + "header", "header1.xml"),
            new Relationship("rId3", Rel + "footnotes", "footnotes.xml"),
            new Relationship("rId4", Rel + "comments", "comments.xml"),
        ]);
        return p;
    }
}

internal static class StreamTestExtensions
{
    internal static byte[] ReadAllBytesForTest(this Stream s)
    {
        using (s)
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
