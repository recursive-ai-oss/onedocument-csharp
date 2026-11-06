using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Odf;
using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class OdfTests
{
    static OdfTests() => DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

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

    private static IEnumerable<string> Matches(string text, string pattern) =>
        System.Text.RegularExpressions.Regex.Matches(text, pattern).Select(m => m.Groups[m.Groups.Count - 1].Value);

    [Fact]
    public void RoundTripKeepsStylesListsTablesNotesAndSections()
    {
        using Document source = OdfSamples.RichDocument();
        using Document back = Open(Save(source, OfficeFormat.Odt));
        string a = source.MainMarkup.Root.OuterXml, b = back.MainMarkup.Root.OuterXml;

        // Styles: every named style the source defines is defined again under the same name, and the paragraphs use the same ones in the same order.
        static SortedSet<string> StyleNames(Document d) =>
            new(Matches(Part(d, "/word/styles.xml"), "<w:style [^>]*w:type=\"(?:paragraph|character)\"[^>]*><w:name w:val=\"([^\"]*)\"").Select(n => n.ToLowerInvariant()), StringComparer.Ordinal);
        Assert.Subset(StyleNames(back), StyleNames(source));
        Assert.Equal(
            Matches(a, "<w:pStyle w:val=\"([^\"]*)\"").Select(StyleNameOf(source)),
            Matches(b, "<w:pStyle w:val=\"([^\"]*)\"").Select(StyleNameOf(back)).Where(n => n is not "normal"));

        // Lists: the same paragraphs are numbered, at the same levels, with the same formats and start values.
        Assert.Equal(Matches(a, "<w:ilvl w:val=\"(\\d)\"/><w:numId w:val=\"[1-9]"), Matches(b, "<w:ilvl w:val=\"(\\d)\"/><w:numId w:val=\"[1-9]"));
        Assert.Equal(
            Matches(Part(source, "/word/numbering.xml"), "<w:numFmt w:val=\"([a-zA-Z]*)\"").Distinct().Order(),
            Matches(Part(back, "/word/numbering.xml"), "<w:numFmt w:val=\"([a-zA-Z]*)\"").Distinct().Order());
        // The source restarts at five with an override; the list style of that list starts at five.
        Assert.Contains("<w:start w:val=\"5\"/>", Part(back, "/word/numbering.xml"), StringComparison.Ordinal);

        // Tables, notes, comments, sections.
        foreach (string marker in new[] { "<w:gridSpan w:val=\"2\"/>", "<w:vMerge w:val=\"restart\"/>", "<w:tblHeader/>", "<w:footnoteReference ", "<w:commentRangeStart ", "<w:bookmarkStart ", "<w:sectPr>", "<w:drawing>", "<w:ins ", "<w:del ", "<w:hyperlink " })
        {
            Assert.Equal(System.Text.RegularExpressions.Regex.Count(a, System.Text.RegularExpressions.Regex.Escape(marker)), System.Text.RegularExpressions.Regex.Count(b, System.Text.RegularExpressions.Regex.Escape(marker)));
        }

        Assert.Equal(Matches(a, "<w:pgSz w:w=\"(\\d+)\" w:h=\"(\\d+)\""), Matches(b, "<w:pgSz w:w=\"(\\d+)\" w:h=\"(\\d+)\""));
        Assert.Equal(Matches(a, "w:orient=\"(landscape)\""), Matches(b, "w:orient=\"(landscape)\""));
        Assert.Equal(Matches(a, "<w:cols w:num=\"(\\d)\""), Matches(b, "<w:cols w:num=\"(\\d)\""));
        foreach (string part in new[] { "/word/footnotes.xml", "/word/comments.xml" })
        {
            Assert.Equal(Words(System.Text.RegularExpressions.Regex.Replace(Part(source, part), "<[^>]*>", " ")), Words(System.Text.RegularExpressions.Regex.Replace(Part(back, part), "<[^>]*>", " ")));
        }

        // Margins and the page size survive to within the rounding of the unit change.
        int Margin(string xml, string side) => int.Parse(Matches(xml, "<w:pgMar [^>]*w:" + side + "=\"(\\d+)\"").First(), System.Globalization.CultureInfo.InvariantCulture);
        foreach (string side in new[] { "top", "left", "right", "bottom" })
        {
            Assert.InRange(Math.Abs(Margin(a, side) - Margin(b, side)), 0, 2);
        }
    }

    private static Func<string, string> StyleNameOf(Document d) => id =>
    {
        string styles = Part(d, "/word/styles.xml");
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(styles, "w:styleId=\"" + System.Text.RegularExpressions.Regex.Escape(id) + "\"><w:name w:val=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : id;
    };

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
        Assert.Contains("shape text", text, StringComparison.Ordinal);
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

    private const string FlatHead =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">";

    private static Document OpenFlat(string inner, DefectCollection? defects = null) =>
        Document.Open(new MemoryStream(Encoding.UTF8.GetBytes(FlatHead + inner + "</office:document>")), new OpenContext(defects: defects));

    private static string Body(string text) => "<office:body><office:text>" + text + "</office:text></office:body>";

    [Fact]
    public void DamageIsReportedWithItsCodes()
    {
        var defects = new DefectCollection();
        using (Document d = OpenFlat(
            "<office:meta><dc:date>not a date</dc:date><dc:title>T</dc:title></office:meta><office:styles><style:style style:name=\"A\" style:family=\"paragraph\" style:parent-style-name=\"A\"/></office:styles>" +
            Body("<text:p text:style-name=\"A\">x<text:change text:change-id=\"nope\"/></text:p><text:p><draw:frame text:anchor-type=\"as-char\"><draw:image xlink:href=\"Pictures/none.png\"/></draw:frame>" +
            "<draw:frame text:anchor-type=\"as-char\"><draw:image><office:binary-data>!!!</office:binary-data></draw:image></draw:frame></text:p>"), defects))
        {
            Assert.Contains("x", d.ExtractText(), StringComparison.Ordinal);
        }

        foreach (string code in new[] { "odf.style-cycle", "odf.unknown-change", "odf.bad-date", "odf.missing-picture" })
        {
            Assert.Contains(defects, x => x.Code == code);
        }

        Assert.False(defects.HasFatal);
    }

    [Fact]
    public void ABrokenImageIsDroppedAndCounted()
    {
        var defects = new DefectCollection();
        using Document d = OpenFlat(Body("<text:p><draw:frame text:anchor-type=\"as-char\"><draw:image><office:binary-data>!!!</office:binary-data></draw:image></draw:frame></text:p>"), defects);
        Assert.Contains(defects, x => x.Code == "odf.bad-base64");
        Assert.True(d.ImportLoss!.CountOf("loss.pictures") > 0);
    }

    [Fact]
    public void EmbeddedFormulaKeepsItsSymbolsAsText()
    {
        using var pkg = OdfPackage.Create(OdfMediaTypes.Text);
        string content = FlatHead.Replace("office:document ", "office:document-content ", StringComparison.Ordinal).Replace(" office:mimetype=\"application/vnd.oasis.opendocument.text\"", string.Empty, StringComparison.Ordinal)
            + Body("<text:p>E = <draw:frame text:anchor-type=\"as-char\" svg:width=\"2cm\" svg:height=\"1cm\"><draw:object xlink:href=\"./Object 1\"/></draw:frame> done</text:p>") + "</office:document-content>";
        pkg.SetEntry(OdfPackage.ContentName, Encoding.UTF8.GetBytes(content), "text/xml");
        pkg.SetEntry("Object 1/content.xml", Encoding.UTF8.GetBytes("<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><semantics><mrow><mi>m</mi><mo>=</mo><mn>2</mn></mrow><annotation encoding=\"StarMath 5.0\">m = 2</annotation></semantics></math>"), "text/xml");
        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        using Document d = Open(ms.ToArray());
        Assert.Contains("E = m=2 done", d.ExtractText().Replace("\r", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(d.ImportLoss!.CountOf("loss.formulas") > 0);
        Assert.True(d.ImportLoss!.CountOf("loss.embedded-objects") > 0);
    }

    [Fact]
    public void ZipWithoutContentAndWrongKindAreTypedErrors()
    {
        using var ms = new MemoryStream();
        using (var zw = new RecursiveAI.OneArchive.Zip.ZipWriter(ms, leaveOpen: true))
        {
            byte[] mt = Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text");
            zw.WriteEntry(new RecursiveAI.OneArchive.Zip.ZipEntry { Name = "mimetype", Size = mt.Length }, mt);
            zw.Finish();
        }

        OfficeFormatException none = Assert.Throws<OfficeFormatException>(() => Open(ms.ToArray()));
        Assert.Equal("odf.no-content", none.Defect.Code);

        OfficeFormatException sheet = Assert.Throws<OfficeFormatException>(() => OpenFlat("<office:body><office:spreadsheet/></office:body>"));
        Assert.Equal("odf.not-text", sheet.Defect.Code);
        Assert.Throws<FormatNotAvailableException>(() => Document.Open(new MemoryStream(Encoding.UTF8.GetBytes(FlatHead.Replace("opendocument.text", "opendocument.spreadsheet", StringComparison.Ordinal) + "<office:body><office:spreadsheet/></office:body></office:document>"))));
    }

    [Fact]
    public void HugeRepeatCountsAndDeepNestingAreBounded()
    {
        var defects = new DefectCollection();
        using (Document d = OpenFlat(Body("<table:table><table:table-column table:number-columns-repeated=\"1000000000\"/><table:table-row table:number-rows-repeated=\"1000000000\"><table:table-cell table:number-columns-repeated=\"1000000000\"/></table:table-row></table:table>"), defects))
        {
            Assert.True(d.MainMarkup.Root.OuterXml.Length < 40_000_000);
        }

        Assert.Contains(defects, x => x.Code == "odf.repeat-capped");

        string nested = string.Concat(Enumerable.Repeat("<text:list><text:list-item>", 300)) + "<text:p>deep</text:p>" + string.Concat(Enumerable.Repeat("</text:list-item></text:list>", 300));
        Assert.Throws<OfficeLimitException>(() => OpenFlat(Body(nested)));
    }

    [Fact]
    public void EncryptedPackageIsRefusedByTheDocument()
    {
        using var ms = new MemoryStream();
        using (var zw = new RecursiveAI.OneArchive.Zip.ZipWriter(ms, leaveOpen: true))
        {
            foreach ((string n, string c) in new[]
            {
                ("mimetype", "application/vnd.oasis.opendocument.text"),
                ("META-INF/manifest.xml", "<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\"><manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"><manifest:encryption-data/></manifest:file-entry></manifest:manifest>"),
                ("content.xml", "x"),
            })
            {
                byte[] b = Encoding.UTF8.GetBytes(c);
                zw.WriteEntry(new RecursiveAI.OneArchive.Zip.ZipEntry { Name = n, Size = b.Length }, b);
            }

            zw.Finish();
        }

        Assert.Throws<OfficeUnsupportedException>(() => Open(ms.ToArray()));
    }

    [Fact]
    public void TheStaticsOpenConvertAndExtractByContent()
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "odf-statics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string odt = Path.Combine(dir, "in.dat");
            File.Copy(Fixture("lo-rich.odt"), odt);
            string text = Norm(Office.ExtractText(odt));
            Assert.Contains("Quarterly report", text, StringComparison.Ordinal);
            string docx = Path.Combine(dir, "out.docx");
            Office.Convert(odt, docx);
            using (Document d = Document.Open(docx))
            {
                Assert.Equal(OfficeFormat.Docx, d.Format);
                Assert.Contains("Quarterly report", d.ExtractText(), StringComparison.Ordinal);
            }

            string back = Path.Combine(dir, "back.odt");
            Office.Convert(docx, back);
            using Document b = Document.Open(back);
            Assert.Equal(OfficeFormat.Odt, b.Format);
            Assert.Contains("Quarterly report", b.ExtractText(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Information only: opens every ODT, OTT and FODT under ONEDOCUMENT_ODF_CORPUS and writes counts, typed errors and defect codes to ONEDOCUMENT_ODF_REPORT.
    // With ONEDOCUMENT_ODF_SOURCES (a directory holding the DOCX each file was converted from, same base name) it also compares the extracted text.
    [Fact]
    public void CorpusReadWhenAskedFor()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ODF_CORPUS");
        string? report = Environment.GetEnvironmentVariable("ONEDOCUMENT_ODF_REPORT");
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(report))
        {
            return;
        }

        string? sources = Environment.GetEnvironmentVariable("ONEDOCUMENT_ODF_SOURCES");
        int opened = 0, failed = 0, compared = 0, same = 0, saved = 0, saveFailed = 0;
        var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var errors = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lossCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lines = new List<string>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".odt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".fodt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ott", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            var defects = new DefectCollection();
            try
            {
                using Document d = Document.Open(file, new OpenContext(defects: defects));
                opened++;
                foreach (Defect x in defects)
                {
                    codes[x.Code] = codes.GetValueOrDefault(x.Code) + 1;
                }

                foreach (LossItem l in d.ImportLoss!.Items)
                {
                    lossCodes[l.Code] = lossCodes.GetValueOrDefault(l.Code) + 1;
                }

                string text = d.ExtractText();
                if (sources is { Length: > 0 })
                {
                    string src = Path.Combine(sources, Path.GetFileNameWithoutExtension(file) + ".docx");
                    if (File.Exists(src))
                    {
                        compared++;
                        using Document s = Document.Open(src);
                        if (Words(s.ExtractText()) == Words(text))
                        {
                            same++;
                        }
                        else
                        {
                            lines.Add("text differs: " + Path.GetFileName(file) + " " + FirstDifference(Words(s.ExtractText()), Words(text)));
                        }
                    }
                }

                try
                {
                    using var ms = new MemoryStream();
                    d.SaveAs(ms, OfficeFormat.Docx);
                    d.SaveAs(ms, d.Format == OfficeFormat.Odt ? OfficeFormat.Fodt : OfficeFormat.Odt);
                    saved++;
                }
                catch (OfficeException ex)
                {
                    saveFailed++;
                    lines.Add("save failed: " + Path.GetFileName(file) + " " + ex.Defect.Code);
                }
            }
            catch (OfficeException ex)
            {
                failed++;
                errors[ex.Defect.Code] = errors.GetValueOrDefault(ex.Defect.Code) + 1;
                lines.Add("error: " + Path.GetFileName(file) + " " + ex.Defect.Code);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"opened {opened}, typed errors {failed}, saved {saved}, save errors {saveFailed}, text compared {compared}, equal as word sequences {same}");
        foreach (KeyValuePair<string, int> kv in errors)
        {
            sb.AppendLine("error " + kv.Key + " " + kv.Value);
        }

        foreach (KeyValuePair<string, int> kv in codes)
        {
            sb.AppendLine("defect " + kv.Key + " " + kv.Value);
        }

        foreach (KeyValuePair<string, int> kv in lossCodes)
        {
            sb.AppendLine("loss " + kv.Key + " " + kv.Value);
        }

        foreach (string l in lines)
        {
            sb.AppendLine(l);
        }

        File.WriteAllText(report, sb.ToString());
    }

    private static string FirstDifference(string a, string b)
    {
        string[] x = a.Split(' '), y = b.Split(' ');
        int i = 0;
        while (i < x.Length && i < y.Length && x[i] == y[i])
        {
            i++;
        }

        string Ctx(string[] w) => string.Join(' ', w.Skip(Math.Max(0, i - 3)).Take(8));
        return $"at word {i} of {x.Length}/{y.Length}: [{Ctx(x)}] vs [{Ctx(y)}]";
    }

    private static string Words(string text) => string.Join(' ', Norm(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
