using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Documents.Tests;

public class SectionTests
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private static string Sect(string inner) => "<w:sectPr>" + inner + "</w:sectPr>";

    private static string Break(string text, string inner) => "<w:p><w:pPr>" + Sect(inner) + "</w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static string Hdr(string kind, string type, string id) => $"<w:{kind}Reference w:type=\"{type}\" r:id=\"{id}\"/>";

    /// <summary>A package with header1..3 and footer1 related to the main part as rId11..rId14.</summary>
    private static InMemoryPackage WithStories(string body, bool evenAndOdd = false)
    {
        InMemoryPackage p = TestDocs.Package(body);
        var rels = new List<Relationship>();
        void Story(string name, string root, string rid, string text)
        {
            p.AddPart("/word/" + name, "application/vnd.openxmlformats-officedocument.wordprocessingml." + (root == "hdr" ? "header" : "footer") + "+xml",
                Encoding.UTF8.GetBytes($"<w:{root} xmlns:w=\"{TestDocs.WNs}\"><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:{root}>"));
            rels.Add(new Relationship(rid, Rel + (root == "hdr" ? "header" : "footer"), name));
        }

        Story("header1.xml", "hdr", "rId11", "H1");
        Story("header2.xml", "hdr", "rId12", "H2");
        Story("header3.xml", "hdr", "rId13", "H3");
        Story("footer1.xml", "ftr", "rId14", "F1");
        if (evenAndOdd)
        {
            p.AddPart("/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml",
                Encoding.UTF8.GetBytes($"<w:settings xmlns:w=\"{TestDocs.WNs}\"><w:evenAndOddHeaders/></w:settings>"));
            rels.Add(new Relationship("rId20", Rel + "settings", "settings.xml"));
        }

        p.SetRelationships("/word/document.xml", rels);
        return p;
    }

    [Fact]
    public void Sections_SplitAtParagraphsAndBody()
    {
        using Document doc = TestDocs.Open(
            "<w:p><w:r><w:t>one</w:t></w:r></w:p>" + Break("two", "<w:type w:val=\"continuous\"/>") + "<w:p><w:r><w:t>three</w:t></w:r></w:p>" +
            Break("four", "<w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/>") + "<w:p><w:r><w:t>five</w:t></w:r></w:p>" + Sect("<w:pgSz w:w=\"11906\" w:h=\"16838\"/>"));
        IReadOnlyList<Section> s = doc.Sections;
        Assert.Equal(3, s.Count);
        Assert.Equal(["one|two", "three|four", "five"], s.Select(x => string.Join("|", x.Blocks().OfType<Paragraph>().Select(p => p.Text))).ToArray());
        Assert.Equal(SectionBreakType.Continuous, s[0].BreakType);
        Assert.Equal(SectionBreakType.NextPage, s[1].BreakType);
        Assert.Equal(new PageSize(15840, 12240, PageOrientation.Landscape, null), s[1].PageSize);
        Assert.Equal(new PageSize(11906, 16838, PageOrientation.Portrait, null), s[2].PageSize);
        Assert.True(s[2].IsLast);
    }

    [Fact]
    public void BodyWithoutSectPr_HasOneDefaultSection_AndEditCreatesIt()
    {
        using Document doc = TestDocs.Open("<w:p/>");
        Section s = Assert.Single(doc.Sections);
        Assert.False(s.PropertiesElement.IsValid);
        Assert.Equal(new PageSize(12240, 15840, PageOrientation.Portrait, null), s.PageSize);
        Assert.Equal(new PageMargins(1440, 1440, 1440, 1440, 720, 720, 0), s.Margins);
        Assert.Equal((1, true, 720L, false), (s.Columns.Count, s.Columns.EqualWidth, s.Columns.Space, s.Columns.Separator));
        s.SetPageSize(11906, 16838, PageOrientation.Portrait);
        s.SetMargins(new PageMargins(1000, 900, 1000, 900, 500, 500, 0));
        Assert.Equal(11906, doc.Sections[0].PageSize.Width);
        Assert.Equal(900, doc.Sections[0].Margins.Left);
        Assert.Contains("<w:sectPr><w:pgSz", doc.Markup.Node.OuterXml);
    }

    [Fact]
    public void SectionData_ColumnsNumberingLinesGrid()
    {
        using Document doc = TestDocs.Open(Sect(
            "<w:type w:val=\"oddPage\"/><w:pgSz w:w=\"12240\" w:h=\"15840\" w:code=\"1\"/><w:pgMar w:top=\"-100\" w:right=\"1\" w:bottom=\"2\" w:left=\"3\" w:header=\"4\" w:footer=\"5\" w:gutter=\"6\"/>" +
            "<w:lnNumType w:countBy=\"5\" w:start=\"0\" w:distance=\"360\" w:restart=\"continuous\"/><w:pgNumType w:start=\"7\" w:fmt=\"upperRoman\"/>" +
            "<w:cols w:num=\"2\" w:equalWidth=\"0\" w:sep=\"1\"><w:col w:w=\"3000\" w:space=\"200\"/><w:col w:w=\"6000\"/></w:cols><w:titlePg/><w:docGrid w:type=\"lines\" w:linePitch=\"360\"/>"));
        Section s = doc.Sections.Single();
        Assert.Equal(SectionBreakType.OddPage, s.BreakType);
        Assert.Equal(1, s.PageSize.PaperCode);
        Assert.Equal(new PageMargins(-100, 1, 2, 3, 4, 5, 6), s.Margins);
        Assert.Equal(new LineNumbering(5, 0, 360, "continuous"), s.LineNumbering);
        Assert.Equal(new PageNumbering(7, "upperRoman", null, null), s.PageNumbering);
        SectionColumns c = s.Columns;
        Assert.Equal((2, false, true), (c.Count, c.EqualWidth, c.Separator));
        Assert.Equal([new ColumnDefinition(3000, 200), new ColumnDefinition(6000, null)], c.Columns);
        Assert.True(s.TitlePage);
        Assert.Equal(new DocumentGrid("lines", 360, null), s.DocumentGrid);
    }

    [Fact]
    public void Columns_CountMismatch_AndBadPageSize_AreDefects()
    {
        var defects = new DefectCollection();
        using Document doc = TestDocs.Open(Sect("<w:pgSz w:w=\"0\"/><w:cols w:num=\"3\" w:equalWidth=\"0\"><w:col w:w=\"1\"/><w:col w:w=\"2\"/></w:cols>"), defects: defects);
        Section s = doc.Sections.Single();
        Assert.Equal(2, s.Columns.Count);
        Assert.Equal(12240, s.PageSize.Width);
        string[] codes = defects.ToArray().Select(d => d.Code).ToArray();
        Assert.Contains("docx.section-columns-mismatch", codes);
        Assert.Contains("docx.section-page-size-invalid", codes);
    }

    [Fact]
    public void Edits_ColumnsBreakNumberingTitlePage()
    {
        using Document doc = TestDocs.Open(Sect("<w:cols w:num=\"2\" w:equalWidth=\"0\"><w:col w:w=\"1\"/><w:col w:w=\"2\"/></w:cols>"));
        Section s = doc.Sections[0];
        s.SetEqualColumns(3, 400, separator: true);
        s.SetBreakType(SectionBreakType.Continuous);
        s.SetTitlePage(true);
        s.SetPageNumbering(new PageNumbering(5, "lowerRoman", null, null));
        Section again = doc.Sections[0];
        Assert.Equal((3, true, 400L, true), (again.Columns.Count, again.Columns.EqualWidth, again.Columns.Space, again.Columns.Separator));
        Assert.Empty(again.Columns.Columns);
        Assert.Equal(SectionBreakType.Continuous, again.BreakType);
        Assert.True(again.TitlePage);
        Assert.Equal(5, again.PageNumbering.Start);
        again.SetPageNumbering(default);
        again.SetTitlePage(false);
        again.SetBreakType(SectionBreakType.NextPage);
        Assert.Equal(default, doc.Sections[0].PageNumbering);
        string xml = doc.Markup.Node.OuterXml;
        Assert.DoesNotContain("pgNumType", xml);
        Assert.DoesNotContain("titlePg", xml);
        // children stay in schema order: type, cols
        Assert.Matches("<w:sectPr><w:cols", xml);
    }

    [Fact]
    public void HeadersFooters_LinkToPrevious_AndPageChoice()
    {
        // Section 1: default+first header, default footer. Section 2: no references (linked). Section 3: own default header only, titlePg.
        InMemoryPackage p = WithStories(
            Break("a", Hdr("header", "default", "rId11") + Hdr("header", "first", "rId12") + Hdr("footer", "default", "rId14") + "<w:titlePg/>") +
            Break("b", string.Empty) +
            Sect(Hdr("header", "default", "rId13") + "<w:titlePg/>"));
        using var doc = Document.FromPackage(p);
        IReadOnlyList<Section> s = doc.Sections;
        Assert.Equal(3, s.Count);

        ResolvedHeaderFooter r = s[1].Resolve(HeaderFooterKind.Header, HeaderFooterType.Default);
        Assert.Equal("H1", r.Story!.Text);
        Assert.True(r.IsLinked);
        Assert.Equal(0, r.Source!.Index);

        Assert.Equal("H3", s[2].Resolve(HeaderFooterKind.Header, HeaderFooterType.Default).Story!.Text);
        Assert.False(s[2].Resolve(HeaderFooterKind.Header, HeaderFooterType.Default).IsLinked);
        // The footer is inherited all the way down; first pages of section 3 inherit the "first" header from section 1.
        Assert.Equal("F1", s[2].Resolve(HeaderFooterKind.Footer, HeaderFooterType.Default).Story!.Text);
        Assert.Equal("H2", s[2].ForPage(HeaderFooterKind.Header, isFirstPageOfSection: true, pageNumber: 5).Story!.Text);
        Assert.Equal("H3", s[2].ForPage(HeaderFooterKind.Header, isFirstPageOfSection: false, pageNumber: 6).Story!.Text);
        // No even stories and no evenAndOddHeaders: an even page uses the default.
        Assert.Equal("H3", s[2].ForPage(HeaderFooterKind.Header, false, 2).Story!.Text);
        Assert.Null(s[0].Resolve(HeaderFooterKind.Header, HeaderFooterType.Even).Story);
        Assert.Single(s[0].HeaderFooterReferences(), x => x.Type == HeaderFooterType.First);
    }

    [Fact]
    public void EvenAndOddHeaders_ChooseTheEvenStory()
    {
        InMemoryPackage p = WithStories(Sect(Hdr("header", "default", "rId11") + Hdr("header", "even", "rId12")), evenAndOdd: true);
        using var doc = Document.FromPackage(p);
        Assert.True(doc.EvenAndOddHeaders);
        Section s = doc.Sections.Single();
        Assert.Equal("H2", s.ForPage(HeaderFooterKind.Header, false, 2).Story!.Text);
        Assert.Equal("H1", s.ForPage(HeaderFooterKind.Header, false, 3).Story!.Text);
        // With the setting off an even page falls to the default.
        doc.EvenAndOddHeaders = false;
        Assert.Equal("H1", s.ForPage(HeaderFooterKind.Header, false, 2).Story!.Text);
    }

    // Generated cases layout-evenodd-restart and layout-evenodd-restart2; observed in Word 365 (16.0.20527): section two restarts at 1 (or 2)
    // and its first page is physical page 5 (or 4), after a blank page; headers follow the printed number.
    [Fact]
    public void EvenAndOdd_FollowsThePrintedNumber_AndBlankPageKeepsParity()
    {
        InMemoryPackage p = WithStories(Sect(Hdr("header", "default", "rId11") + Hdr("header", "even", "rId12")), evenAndOdd: true);
        using var doc = Document.FromPackage(p);
        Section s = doc.Sections.Single();
        Assert.Equal(4, s.FirstPrintedPageNumber(3));
        s.SetPageNumbering(new PageNumbering(1, null, null, null));
        Assert.Equal(1, s.FirstPrintedPageNumber(3));
        Assert.False(s.RequiresBlankPageBefore(5, 1));
        Assert.True(s.RequiresBlankPageBefore(4, 1));
        Assert.True(s.RequiresBlankPageBefore(3, 2));
        s.SetPageNumbering(new PageNumbering(2, null, null, null));
        Assert.Equal(2, s.FirstPrintedPageNumber(9));
        // Physical page 3 prints 2, so the even header is used for it once the blank page is accounted for (physical 4).
        Assert.Equal("H2", s.ForPage(HeaderFooterKind.Header, isFirstPageOfSection: true, pageNumber: 2).Story!.Text);
        Assert.Equal("H1", s.ForPage(HeaderFooterKind.Header, false, 3).Story!.Text);
        doc.EvenAndOddHeaders = false;
        Assert.False(s.RequiresBlankPageBefore(4, 1));
    }

    [Fact]
    public void BrokenReferences_AreDefects()
    {
        var defects = new DefectCollection();
        InMemoryPackage p = WithStories(Sect(Hdr("header", "default", "rId99") + Hdr("header", "first", "rId11") + Hdr("header", "first", "rId12")));
        using var doc = Document.FromPackage(p, new OpenContext(defects: defects));
        Section s = doc.Sections.Single();
        Assert.Null(s.Resolve(HeaderFooterKind.Header, HeaderFooterType.Default).Story);
        Assert.Equal("H1", s.GetOwn(HeaderFooterKind.Header, HeaderFooterType.First)!.Text);
        string[] codes = defects.ToArray().Select(d => d.Code).ToArray();
        Assert.Contains("docx.header-missing", codes);
        Assert.Contains("docx.header-reference-duplicate", codes);
    }

    [Fact]
    public void AddOwn_CreatesPartRelationshipAndReference_ChangingOnlyThoseParts()
    {
        InMemoryPackage p = TestDocs.Package("<w:p><w:r><w:t>x</w:t></w:r></w:p>" + Sect("<w:pgSz w:w=\"1\" w:h=\"2\"/>"), "<w:style w:type=\"paragraph\" w:styleId=\"A\"/>");
        byte[] styles = p.FindPart("/word/styles.xml")!.GetBytes().ToArray();
        using var doc = Document.FromPackage(p);
        HeaderFooter h = doc.Sections[0].AddOwn(HeaderFooterKind.Header, HeaderFooterType.Default);
        Assert.Empty(h.Paragraphs().Single().Runs());
        Assert.Equal("/word/header1.xml", h.PartName);
        Assert.Equal(h.RelationshipId, doc.Sections[0].AddOwn(HeaderFooterKind.Header, HeaderFooterType.Default).RelationshipId);
        doc.CommitChanges();
        Assert.Equal(styles, p.FindPart("/word/styles.xml")!.GetBytes().ToArray());
        string main = Encoding.UTF8.GetString(p.FindPart("/word/document.xml")!.GetBytes().Span);
        Assert.Contains("<w:headerReference w:type=\"default\" r:id=\"" + h.RelationshipId + "\"/>", main);
        using var reopened = Document.FromPackage(p);
        Assert.Equal(h.PartName, reopened.Sections[0].GetOwn(HeaderFooterKind.Header, HeaderFooterType.Default)!.PartName);
        // The settings part is created on demand.
        reopened.EvenAndOddHeaders = true;
        reopened.CommitChanges();
        using var third = Document.FromPackage(p);
        Assert.True(third.EvenAndOddHeaders);
    }
}
