using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Redaction.Tests;

/// <summary>A region removes by position; copies of its text elsewhere stay, and the report says where.</summary>
public sealed class RegionCopyTests
{
    static RegionCopyTests()
    {
        DocumentFormats.Register(FormatRegistry.Default);
        SpreadsheetFormats.Register(FormatRegistry.Default);
        PresentationFormats.Register(FormatRegistry.Default);
    }

    private static string Field(string code, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> " + code + " </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>" + result + "</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    [Fact]
    public void TableOfContentsAndCrossReferenceCopiesOfAParagraphAreReported()
    {
        byte[] input = Attacks.Docx(
            "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"_Toc1\"/><w:r><w:t>QUOKKA4419 Heading</w:t></w:r><w:bookmarkEnd w:id=\"1\"/></w:p>" +
            "<w:p>" + Field("TOC \\o 1-3", "QUOKKA4419 Heading") + "</w:p><w:p><w:r><w:t>other</w:t></w:r></w:p>");
        var plan = new RedactionPlan();
        plan.Regions.Add(new ParagraphRegion(0, 0));
        using var output = new MemoryStream();
        using Document doc = Document.Open(new MemoryStream(input));
        RedactionReport report = doc.Redact(plan, output);
        Assert.Contains(report.Gaps, static g => g.Code == "redaction.region-copy" && g.Location == "/word/document.xml");
        // The copy is still there: the region does not search for text, and the report is how the caller learns of it.
        Assert.NotEmpty(Attacker.Recover(output.ToArray(), Attacks.T));
    }

    [Fact]
    public void CachedFormulaResultsOfACellRangeAreReported()
    {
        byte[] input = Attacks.Xlsx("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"str\"><f>A1&amp;\"!\"</f><v>QUOKKA4419!</v></c></row>", "<si><t>QUOKKA4419</t></si>");
        var plan = new RedactionPlan();
        plan.Regions.Add(new CellRangeRegion("Sheet1", "A1:A1"));
        using var output = new MemoryStream();
        using Workbook wb = Workbook.Open(new MemoryStream(input));
        RedactionReport report = wb.Redact(plan, output);
        Assert.Contains(report.Gaps, static g => g.Code == "redaction.region-copy");
    }

    [Fact]
    public void ARegionWithNoCopyReportsNoGap()
    {
        byte[] input = Attacks.Docx("<w:p><w:r><w:t>QUOKKA4419 Heading</w:t></w:r></w:p><w:p><w:r><w:t>other paragraph</w:t></w:r></w:p>");
        var plan = new RedactionPlan();
        plan.Regions.Add(new ParagraphRegion(0, 0));
        using var output = new MemoryStream();
        using Document doc = Document.Open(new MemoryStream(input));
        RedactionReport report = doc.Redact(plan, output);
        Assert.DoesNotContain(report.Gaps, static g => g.Code == "redaction.region-copy");
        Assert.Empty(Attacker.Recover(output.ToArray(), Attacks.T));
    }
}

public sealed class SlideRegionTests
{
    static SlideRegionTests()
    {
        PresentationFormats.Register(FormatRegistry.Default);
    }

    [Fact]
    public void ASlideThatAnotherSlideLinksToIsStillRemoved()
    {
        byte[] input = Attacks.Mutate(Torture.Pptx(), pkg =>
        {
            // Slide 1 links to slide 2 (a jump), so slide 2 is reachable from a slide that stays.
            Attacks.AddRel(pkg, "/ppt/slides/slide1.xml", "rId77", "slide", "slide2.xml");
            RecursiveAI.OneDocument.Packaging.IPackagePart slide1 = pkg.FindPart("/ppt/slides/slide1.xml")!;
            string xml = System.Text.Encoding.UTF8.GetString(slide1.GetBytes().Span);
            int at = xml.IndexOf("</a:p>", StringComparison.Ordinal);
            xml = xml.Insert(at, "<a:r><a:rPr><a:hlinkClick r:id=\"rId77\" action=\"ppaction://hlinksldjump\"/></a:rPr><a:t>jump</a:t></a:r>");
            slide1.SetBytes(System.Text.Encoding.UTF8.GetBytes(xml));
        });
        var plan = new RedactionPlan();
        plan.Regions.Add(new SlideRegion(2));
        using var output = new MemoryStream();
        using Presentation p = Presentation.Open(new MemoryStream(input));
        RedactionReport report = p.Redact(plan, output);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(output.ToArray()));
        Assert.DoesNotContain(zip.Entries, static e => e.FullName == "ppt/slides/slide2.xml");
        Assert.DoesNotContain(report.Gaps, static g => g.Code == "redaction.region-failed");
        using var reader = new StreamReader(zip.GetEntry("ppt/slides/slide1.xml")!.Open());
        Assert.DoesNotContain("hlinksldjump", reader.ReadToEnd(), StringComparison.Ordinal);
        using Presentation again = Presentation.Open(new MemoryStream(output.ToArray()));
        Assert.Single(again.Slides);
    }
}
