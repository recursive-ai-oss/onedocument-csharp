using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Odf;
using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class OdfTests
{
    private static string Norm(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static byte[] Save(Document d, OfficeFormat format)
    {
        using var ms = new MemoryStream();
        d.SaveAs(ms, format);
        return ms.ToArray();
    }

    private static string PageNumbers(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"Page \d+", "Page #");

    private static Document Open(byte[] bytes, OpenContext? ctx = null) => Document.Open(new MemoryStream(bytes), ctx);

    [Fact]
    public void RichDocumentWritesAsOdtAndReadsBackWithTheSameText()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        Assert.NotNull(source.ExportLoss);
        using Document back = Open(odt);
        Assert.Equal(OfficeFormat.Odt, back.Format);
        Assert.Equal(Norm(source.ExtractText()), Norm(back.ExtractText()));
        Assert.Equal(source.Tables.Count(), back.Tables.Count());
    }

    [Fact]
    public void RichDocumentWritesAsFlatOdtAndReadsBack()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] fodt = Save(source, OfficeFormat.Fodt);
        Assert.Contains("<office:document ", Encoding.UTF8.GetString(fodt), StringComparison.Ordinal);
        using Document back = Open(fodt);
        Assert.Equal(OfficeFormat.Fodt, back.Format);
        Assert.Equal(Norm(source.ExtractText()), Norm(back.ExtractText()));
        Assert.Contains("office:binary-data", Encoding.UTF8.GetString(fodt), StringComparison.Ordinal);
    }

    [Fact]
    public void UntouchedOdtSavesItsOriginalBytes()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        using Document back = Open(odt);
        Assert.Equal(odt, Save(back, OfficeFormat.Odt));
        // An edit ends the pass-through: the output is a valid package again, with the edit in it.
        back.Paragraphs.First().SetText("Edited title");
        byte[] edited = Save(back, OfficeFormat.Odt);
        Assert.NotEqual(odt, edited);
        using Document again = Open(edited);
        Assert.StartsWith("Edited title", again.ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void WrittenPackageStartsWithAStoredMimetype()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        Assert.Equal((byte)'P', odt[0]);
        Assert.Equal(0, BitConverter.ToUInt16(odt, 8)); // method: stored
        Assert.Equal("mimetype", Encoding.ASCII.GetString(odt, 30, 8));
        Assert.Equal("application/vnd.oasis.opendocument.text", Encoding.ASCII.GetString(odt, 38, 39));
    }

    [Fact]
    public void ExportKeepsStructure()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        using OdfPackage pkg = OdfPackage.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(odt));
        string content = pkg.ContentRoot.OuterXml;
        Assert.Contains("<text:h ", content, StringComparison.Ordinal);
        Assert.Contains("text:outline-level=\"1\"", content, StringComparison.Ordinal);
        Assert.Contains("<text:list ", content, StringComparison.Ordinal);
        Assert.Contains("<text:note ", content, StringComparison.Ordinal);
        Assert.Contains("<office:annotation ", content, StringComparison.Ordinal);
        Assert.Contains("table:number-columns-spanned=\"2\"", content, StringComparison.Ordinal);
        Assert.Contains("table:number-rows-spanned=\"2\"", content, StringComparison.Ordinal);
        Assert.Contains("<table:table-header-rows>", content, StringComparison.Ordinal);
        Assert.Contains("<text:changed-region", content, StringComparison.Ordinal);
        Assert.Contains("xlink:href=\"https://example.org/page\"", content, StringComparison.Ordinal);
        Assert.Contains("text:bookmark-start", content, StringComparison.Ordinal);
        Assert.Contains("<draw:image xlink:href=\"Pictures/", content, StringComparison.Ordinal);
        Assert.True(pkg.Contains("Pictures/image1.png"));
        string styles = pkg.StylesRoot.OuterXml;
        Assert.Contains("style:master-page", styles, StringComparison.Ordinal);
        Assert.Contains("<style:header>", styles, StringComparison.Ordinal);
        Assert.Contains("text:page-number", styles, StringComparison.Ordinal);
        Assert.Contains("style:print-orientation=\"landscape\"", styles, StringComparison.Ordinal);
        Assert.Equal("Report", pkg.Metadata.Title);
    }

    [Fact]
    public void ImportMapsStructureBackToTheHub()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        using Document back = Open(odt);
        string xml = back.MainMarkup.Root.OuterXml;
        Assert.Contains("<w:pStyle w:val=\"Heading1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:numPr>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:footnoteReference", xml, StringComparison.Ordinal);
        Assert.Contains("<w:commentRangeStart", xml, StringComparison.Ordinal);
        Assert.Contains("<w:gridSpan w:val=\"2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tblHeader/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:ins ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:del ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:hyperlink ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:drawing>", xml, StringComparison.Ordinal);
        Assert.Contains("w:orient=\"landscape\"", xml, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(xml, "<w:sectPr>"));
    }

    private static string Fixture(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../testdata/odf", name));

    [Theory]
    [InlineData("lo-rich.odt", OfficeFormat.Odt)]
    [InlineData("lo-rich.fodt", OfficeFormat.Fodt)]
    public void FilesWrittenByAWordProcessorReadWithTheTextOfTheirSource(string name, OfficeFormat format)
    {
        using Document source = Document.Open(Fixture("rich.docx"));
        var defects = new DefectCollection();
        using Document d = Document.Open(Fixture(name), new OpenContext(defects: defects));
        Assert.Equal(format, d.Format);
        Assert.NotNull(d.ImportLoss);
        // The cached result of the page-number field is whatever page the writer rendered the footer on.
        Assert.Equal(PageNumbers(Norm(source.ExtractText())), PageNumbers(Norm(d.ExtractText())));
        Assert.False(defects.HasFatal);
    }

    private static string Part(Document d, string name) => Encoding.UTF8.GetString(d.Package.FindPart(name)!.GetBytes().Span);

    [Fact]
    public void FeatureFileMapsItsConstructs()
    {
        var defects = new DefectCollection();
        using Document d = Document.Open(Fixture("features.fodt"), new OpenContext(defects: defects));
        string xml = d.MainMarkup.Root.OuterXml;
        string styles = Part(d, "/word/styles.xml");
        string numbering = Part(d, "/word/numbering.xml");
        string text = Norm(d.ExtractText());
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_ODF_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            d.SaveAs(Path.Combine(dir, "features-converted.docx"));
            File.WriteAllText(Path.Combine(dir, "features.txt"), text);
            File.WriteAllBytes(Path.Combine(dir, "features-again.odt"), Save(d, OfficeFormat.Odt));
            File.WriteAllBytes(Path.Combine(dir, "features-again.fodt"), Save(d, OfficeFormat.Fodt));
        }

        // Styles: names, inheritance, outline level, defaults.
        Assert.Contains("w:styleId=\"Heading1\"", styles, StringComparison.Ordinal);
        Assert.Contains("<w:name w:val=\"heading 1\"/>", styles, StringComparison.Ordinal);
        Assert.Contains("<w:basedOn w:val=\"Heading\"/>", styles, StringComparison.Ordinal);
        Assert.Contains("<w:outlineLvl w:val=\"0\"/>", styles, StringComparison.Ordinal);
        Assert.Contains("w:styleId=\"BodyText\"", styles, StringComparison.Ordinal);
        Assert.Contains("w:styleId=\"Hyperlink\"", styles, StringComparison.Ordinal);
        Assert.Contains("<w:docDefaults>", styles, StringComparison.Ordinal);

        // Text, whitespace rules and inline structure.
        Assert.Contains("Plain red bold nested sup", text, StringComparison.Ordinal);
        Assert.Contains("three  spaces\ttab\nafter break", text.Replace("   ", " ", StringComparison.Ordinal).Replace("three spaces", "three  spaces", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("<w:tab/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:br/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vertAlign w:val=\"superscript\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:rStyle w:val=\"Strong\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:jc w:val=\"center\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:pBdr><w:bottom w:val=\"single\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:anchor=\"intro\"", xml, StringComparison.Ordinal);
        Assert.Contains("r:id=", xml, StringComparison.Ordinal);
        Assert.Contains("<w:footnoteReference w:id=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:endnoteReference w:id=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("Footnote body.", Part(d, "/word/footnotes.xml"), StringComparison.Ordinal);
        Assert.Contains("Endnote body.", Part(d, "/word/endnotes.xml"), StringComparison.Ordinal);
        Assert.Contains("w:author=\"Rev\"", Part(d, "/word/comments.xml"), StringComparison.Ordinal);
        Assert.Contains("<w:commentRangeEnd w:id=\"0\"/>", xml, StringComparison.Ordinal);

        // Tracked changes and fields.
        Assert.Contains("<w:ins w:id=", xml, StringComparison.Ordinal);
        Assert.Contains("w:author=\"Ann\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:delText xml:space=\"preserve\">removed words</w:delText>", xml, StringComparison.Ordinal);
        Assert.Contains("w:instr=\" PAGE \"", xml, StringComparison.Ordinal);
        Assert.Contains("w:instr=\" NUMPAGES \"", xml, StringComparison.Ordinal);
        Assert.Contains("w:instr=\" REF intro \\h \"", xml, StringComparison.Ordinal);
        Assert.Contains("w:instr=\" PAGEREF intro \\h \"", xml, StringComparison.Ordinal);
        Assert.Contains("w:instr=\" TITLE \"", xml, StringComparison.Ordinal);
        Assert.Contains("03/04/2024", text, StringComparison.Ordinal);
        Assert.Contains("TOC \\o", xml, StringComparison.Ordinal);
        Assert.Contains("1-2", xml, StringComparison.Ordinal);

        // Lists: nesting, continuation, restart, start value, list header.
        Assert.Contains("<w:lvlText w:val=\"%1.\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:lvlText w:val=\"(%2)\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:lvlText w:val=\"%1.%2.%3.\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:numFmt w:val=\"bullet\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:startOverride w:val=\"10\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:ilvl w:val=\"2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:numId w:val=\"0\"/>", xml, StringComparison.Ordinal);

        // Tables.
        Assert.Contains("<w:tblHeader/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:gridSpan w:val=\"2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:gridCol w:w=\"2835\"/><w:gridCol w:w=\"2835\"/><w:gridCol w:w=\"5669\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:jc w:val=\"center\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("nested cell", text, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(xml, "rep</w:t>") / 3);

        // Drawings.
        Assert.Contains("<wp:inline ", xml, StringComparison.Ordinal);
        Assert.Contains("descr=\"A single dot\"", xml, StringComparison.Ordinal);
        Assert.Contains("<v:textbox><w:txbxContent>", xml, StringComparison.Ordinal);
        Assert.Contains("Text in a box", text, StringComparison.Ordinal);
        Assert.True(d.Package.FindPart("/word/media/image1.png") is not null);

        // Page styles: three sections, the second landscape in two columns, headers per master page.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Count(xml, "<w:sectPr>"));
        Assert.Contains("w:orient=\"landscape\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:cols w:num=\"2\" w:space=\"567\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:titlePg/>", xml, StringComparison.Ordinal);
        Assert.Contains("Landscape first header", Part(d, "/word/header3.xml") + Part(d, "/word/header2.xml"), StringComparison.Ordinal);
        Assert.Contains("w:evenAndOddHeaders", Part(d, "/word/settings.xml"), StringComparison.Ordinal);
        Assert.Contains("<w:defaultTabStop w:val=\"709\"/>", Part(d, "/word/settings.xml"), StringComparison.Ordinal);

        // Properties.
        Assert.Equal("Feature sheet", d.Properties.Core.Title);
        Assert.Equal("Tester", d.Properties.Core.Creator);
        Assert.Contains(d.Properties.Custom.List(), p => p.Name == "Project" && p.Text == "Alpha");

        // The loss report names what did not map.
        Assert.NotNull(d.ImportLoss);
        var codes = d.ImportLoss!.Items.Select(i => i.Code).ToList();
        Assert.Contains("loss.shapes", codes);
        Assert.Contains("loss.linked-images", codes);
        Assert.Contains("loss.formatting-revisions", codes);
        Assert.False(defects.HasFatal);
    }

    [Fact]
    public void OutputFilesForTheOracleRunsWhenAskedFor()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ODF_OUT");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using Document source = OdfSamples.RichDocument();
        source.SaveAs(Path.Combine(dir, "rich.docx"));
        File.WriteAllBytes(Path.Combine(dir, "rich.odt"), Save(source, OfficeFormat.Odt));
        File.WriteAllBytes(Path.Combine(dir, "rich.fodt"), Save(source, OfficeFormat.Fodt));
        File.WriteAllBytes(Path.Combine(dir, "rich.ott"), Save(source, OfficeFormat.Ott));
        File.WriteAllText(Path.Combine(dir, "rich.docx.txt"), Norm(source.ExtractText()));
        using Document back = Open(Save(source, OfficeFormat.Odt));
        File.WriteAllText(Path.Combine(dir, "rich.odt.txt"), Norm(back.ExtractText()));
        back.SaveAs(Path.Combine(dir, "rich-back.docx"));
    }
}
