using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.TextExtraction;
using static RecursiveAI.OneOffice.Tests.Text.OoxmlSamples;

namespace RecursiveAI.OneOffice.Tests.Text;

public class WordTextTests
{
    internal static string Extract(byte[] package, TextExtractionOptions? options = null, OpenContext? context = null)
    {
        using IByteSource source = ByteSource.FromMemory(package);
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        OoxmlTextExtractor.Extract(source, writer, context, options);
        return writer.ToString();
    }

    [Fact]
    public void Paragraphs_runs_tabs_and_breaks()
    {
        byte[] doc = Docx(
            "<w:p><w:r><w:t>Hel</w:t></w:r><w:r><w:t xml:space=\"preserve\">lo </w:t></w:r><w:r><w:t>&amp; &#x41;</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t>a</w:t><w:tab/><w:t>b</w:t><w:br/><w:t>c</w:t><w:br w:type=\"page\"/><w:noBreakHyphen/><w:softHyphen/></w:r></w:p>" +
            "<w:p/><w:p><w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"720\"/></w:tabs></w:pPr>" + Run("<![CDATA[x<y]]>") + "</w:p>");
        Assert.Equal("Hello & A\na\tb\nc\n-\n\nx<y\n", Extract(doc));
    }

    [Fact]
    public void Field_results_are_kept_and_codes_dropped()
    {
        byte[] doc = Docx(
            "<w:p><w:r><w:t>See </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \"http://x\" </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>the site</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "<w:fldSimple w:instr=\" PAGE \"><w:r><w:t> p.3</w:t></w:r></w:fldSimple></w:p>");
        Assert.Equal("See the site p.3\n", Extract(doc));
    }

    [Fact]
    public void Tables_put_a_row_on_a_line_and_flatten_inside_cells()
    {
        byte[] doc = Docx(
            Para("before") +
            "<w:tbl><w:tr><w:tc>" + Para("a1") + "</w:tc><w:tc>" + Para("b1") + Para("b2") + "</w:tc><w:tc>" + Para("c&lt;1") + "</w:tc></w:tr>" +
            "<w:tr><w:tc>" + Para("a2") + "</w:tc><w:tc><w:p><w:r><w:t>x</w:t><w:br/><w:t>y</w:t></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc>" + Para("n1") + "</w:tc><w:tc>" + Para("n2") + "</w:tc></w:tr></w:tbl><w:p/></w:tc></w:tr></w:tbl>" +
            Para("after"));
        string text = Extract(doc);
        Assert.Equal("before\na1\tb1 b2\tc<1\na2\tx y n1 n2\nafter\n", text);
    }

    [Fact]
    public void Deleted_text_is_left_out_unless_asked_for_and_a_deleted_mark_joins_paragraphs()
    {
        byte[] doc = Docx(
            "<w:p><w:r><w:t>keep </w:t></w:r><w:del w:id=\"1\" w:author=\"a\"><w:r><w:delText>gone</w:delText></w:r></w:del><w:ins w:id=\"2\" w:author=\"a\"><w:r><w:t>new</w:t></w:r></w:ins></w:p>" +
            "<w:p><w:pPr><w:rPr><w:del w:id=\"3\" w:author=\"a\"/></w:rPr></w:pPr>" + Run("first") + "</w:p>" + Para(" second") +
            "<w:p><w:moveFrom w:id=\"4\"><w:r><w:t>moved away</w:t></w:r></w:moveFrom><w:moveTo w:id=\"5\"><w:r><w:t>moved here</w:t></w:r></w:moveTo></w:p>");
        Assert.Equal("keep new\nfirst second\nmoved here\n", Extract(doc));
        Assert.Equal("keep gonenew\nfirst\n second\nmoved awaymoved here\n", Extract(doc, new TextExtractionOptions { IncludeDeletedText = true }));
    }

    [Fact]
    public void Alternate_content_reads_one_branch()
    {
        string tb(string t) => $"<w:txbxContent>{Para(t)}</w:txbxContent>";
        byte[] doc = Docx(
            "<w:p>" + Run("host") + "<w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><wps:wsp><wps:txbx>" + tb("box") + "</wps:txbx></wps:wsp></mc:Choice>" +
            "<mc:Fallback><w:pict><v:shape><v:textbox>" + tb("box") + "</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r>" + Run(" tail") + "</w:p>" +
            "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"x9\">" + Run("unknown") + "</mc:Choice><mc:Fallback>" + Run("fallback") + "</mc:Fallback></mc:AlternateContent></w:r></w:p>" +
            "<w:p><w:r><w:pict><v:shape><v:textbox>" + tb("vml only") + "</v:textbox></v:shape></w:pict></w:r></w:p>");
        Assert.Equal("host tail\nbox\nfallback\n\nvml only\n", Extract(doc));
    }

    [Fact]
    public void Nested_text_boxes_come_out_in_order_after_their_host()
    {
        string box(string inner) => $"<w:r><w:pict><v:shape><v:textbox><w:txbxContent>{inner}</w:txbxContent></v:textbox></v:shape></w:pict></w:r>";
        byte[] doc = Docx("<w:p>" + Run("A") + box("<w:p>" + Run("B") + box(Para("C")) + "</w:p>" + Para("D")) + Run("E") + "</w:p>" + Para("F"));
        Assert.Equal("AE\nB\nC\nD\nF\n", Extract(doc));
    }

    [Fact]
    public void Headers_footers_notes_and_comments_follow_the_body()
    {
        byte[] doc = Docx(
            Para("body"),
            ("word/header1.xml", Part("hdr", Para("head")), "header"),
            ("word/footer1.xml", Part("ftr", Para("foot")), "footer"),
            ("word/footnotes.xml", Part("footnotes", "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:id=\"1\">" + Para("note one") + "</w:footnote>"), "footnotes"),
            ("word/endnotes.xml", Part("endnotes", "<w:endnote w:id=\"1\">" + Para("end one") + "</w:endnote>"), "endnotes"),
            ("word/comments.xml", Part("comments", "<w:comment w:id=\"0\" w:author=\"A\">" + Para("remark") + "</w:comment>"), "comments"));
        Assert.Equal("body\n\nhead\n\nfoot\n\nnote one\n\nend one\n\nremark\n", Extract(doc));
        Assert.Equal("body\n\nhead\n\nfoot\n\nnote one\n\nend one\n", Extract(doc, new TextExtractionOptions { IncludeComments = false }));
        Assert.Equal("body\n", Extract(doc, new TextExtractionOptions { IncludeComments = false, IncludeNotes = false, IncludeHeadersAndFooters = false }));
    }

    [Fact]
    public void Strict_namespace_and_math_text()
    {
        byte[] doc = DocxWith(WStrict, "<w:p>" + Run("x") + "<m:oMath><m:r><m:t>a+b</m:t></m:r></m:oMath></w:p>");
        Assert.Equal("xa+b\n", Extract(doc));
    }

    [Fact]
    public void List_labels_come_from_the_label_source_by_paragraph_offset()
    {
        byte[] doc = Docx("<w:p>" + Run("item") + "</w:p>" + Para("plain") + "<w:p>" + Run("bullet") + "</w:p>");
        string xml = ReadPartText(doc, "word/document.xml");
        int first = xml.IndexOf("<w:p>", StringComparison.Ordinal);
        int third = xml.LastIndexOf("<w:p>", StringComparison.Ordinal);
        var labels = new FakeLabels(new Dictionary<long, string> { [first] = "a)\t", [third] = "\u2022 " });
        Assert.Equal("a)\titem\nplain\n\u2022 bullet\n", Extract(doc, new TextExtractionOptions { ListLabels = labels }));
        Assert.All(labels.Asked, part => Assert.Equal("/word/document.xml", part));
        Assert.Equal("item\nplain\nbullet\n", Extract(doc));
    }

    private sealed class FakeLabels(Dictionary<long, string> byOffset) : IListLabelSource
    {
        public List<string> Asked { get; } = [];

        public string? LabelFor(string partName, long paragraphOffset)
        {
            Asked.Add(partName);
            return byOffset.GetValueOrDefault(paragraphOffset);
        }
    }

    [Fact]
    public void Normalized_paragraphs_locate_their_offsets()
    {
        byte[] doc = Docx(Para("alpha") + "<w:p>" + Run("be") + Run("ta") + "</w:p>" + "<w:tbl><w:tr><w:tc>" + Para("cell") + "</w:tc></w:tr></w:tbl>");
        using IByteSource source = ByteSource.FromMemory(doc);
        NormalizedText n = OoxmlTextExtractor.ExtractNormalized(source);
        Assert.Equal(["alpha", "beta", "cell"], n.Paragraphs.Select(p => p.Text));
        Assert.Equal("alpha\nbeta\ncell", n.Text);
        Assert.All(n.Paragraphs, p => Assert.Equal("/word/document.xml", p.PartName));
        Assert.True(n.Paragraphs[2].InTable);
        Assert.False(n.Paragraphs[0].InTable);

        // The paragraph's offset is the offset of its start tag in the part.
        string xml = ReadPartText(doc, "word/document.xml");
        Assert.Equal(xml.IndexOf("<w:p>", StringComparison.Ordinal), n.Paragraphs[0].PartOffset);
        Assert.Equal(xml.IndexOf("<w:p>", xml.IndexOf("<w:p>", StringComparison.Ordinal) + 1, StringComparison.Ordinal), n.Paragraphs[1].PartOffset);

        Assert.True(n.TryLocate(7, out NormalizedParagraph? p1, out int inP));
        Assert.Equal(1, p1.Index);
        Assert.Equal(1, inP);
        Assert.True(n.TryLocate(n.Text.Length, out NormalizedParagraph? last, out _));
        Assert.Equal(2, last.Index);
        Assert.False(n.TryLocate(n.Text.Length + 1, out _, out _));
        Assert.False(n.TryLocate(-1, out _, out _));
    }

    [Fact]
    public void Run_splitting_does_not_change_the_normalized_text()
    {
        byte[] a = Docx(Para("Hello world"));
        byte[] b = Docx("<w:p>" + Run("H") + Run("ello") + Run(" wor") + Run("ld") + "</w:p>");
        using IByteSource sa = ByteSource.FromMemory(a);
        using IByteSource sb = ByteSource.FromMemory(b);
        Assert.Equal(OoxmlTextExtractor.ExtractNormalized(sa).Text, OoxmlTextExtractor.ExtractNormalized(sb).Text);
    }

    [Fact]
    public void A_missing_or_damaged_secondary_part_is_reported_and_extraction_goes_on()
    {
        byte[] doc = Docx(
            Para("body"),
            ("word/header1.xml", null, "header"),
            ("word/footer1.xml", "<w:ftr xmlns:w=\"" + "http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>ok</w:t></w:r></w:p><w:p><w:r></w:ftr>", "footer"),
            ("word/comments.xml", Part("comments", Para("remark")), "comments"));
        var defects = new DefectCollection();
        string text = Extract(doc, context: new OpenContext(defects: defects));
        Assert.Equal("body\n\nok\n\nremark\n", text);
        Assert.Contains(defects, d => d.Code == "text.part-missing" && d.Location.Container == "/word/header1.xml");
        Assert.Contains(defects, d => d.Code == "text.part-unreadable" && d.Location.Container == "/word/footer1.xml");
    }

    [Fact]
    public void A_damaged_main_part_raises_a_located_exception()
    {
        byte[] doc = Docx(Para("body") + "<w:p><w:r><w:t>oops</w:r>");
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Extract(doc));
        Assert.StartsWith("xml.", ex.Defect.Code, StringComparison.Ordinal);
        Assert.Equal("/word/document.xml", ex.Location.Container);
    }

    [Fact]
    public void A_package_without_a_main_part_is_refused()
    {
        byte[] pkg = Package("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", []);
        // The sniffer sees a Word package; the part itself is missing.
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Extract(pkg));
        Assert.Equal("text.no-main-part", ex.Defect.Code);
    }

    [Fact]
    public void An_expansion_bomb_hits_the_limit()
    {
        string many = string.Concat(Enumerable.Repeat("<w:p/>", 7_000_000));
        byte[] doc = Docx(many);
        Assert.Throws<OfficeLimitException>(() => Extract(doc));
    }

    [Fact]
    public void Cancellation_stops_the_extraction()
    {
        byte[] doc = Docx(string.Concat(Enumerable.Repeat(Para("x"), 100)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using IByteSource source = ByteSource.FromMemory(doc);
        Assert.Throws<OperationCanceledException>(() => OoxmlTextExtractor.Extract(source, TextWriter.Null, cancellationToken: cts.Token));
    }

    [Fact]
    public void The_default_registry_extracts_through_the_statics()
    {
        string path = Path.Combine(Path.GetTempPath(), "u11-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            File.WriteAllBytes(path, Docx(Para("through the registry")));
            Assert.Equal("through the registry\n", Office.ExtractText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Streaming_allocates_nothing_per_paragraph()
    {
        static byte[] Part(int paragraphs) => System.Text.Encoding.UTF8.GetBytes(
            $"<w:document {W}><w:body>" + string.Concat(Enumerable.Repeat("<w:p><w:r><w:t>some text here</w:t></w:r><w:r><w:tab/><w:t>and more &amp; more</w:t></w:r></w:p>", paragraphs)) + "</w:body></w:document>");

        // The extractor itself, over a part in memory: nothing is allocated per paragraph once its buffers have grown.
        static long Extractor(byte[] part)
        {
            var context = new OpenContext();
            var sink = new PlainTextSink(TextWriter.Null);
            using var warm = new RecursiveAI.OneOffice.Markup.MarkupReader(part, context);
            new WordTextExtractor(context, TextExtractionOptions.Default, sink, "/word/document.xml", default).Run(warm);
            long before = GC.GetAllocatedBytesForCurrentThread();
            using var reader = new RecursiveAI.OneOffice.Markup.MarkupReader(part, context);
            new WordTextExtractor(context, TextExtractionOptions.Default, sink, "/word/document.xml", default).Run(reader);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        long small = Extractor(Part(200));
        long large = Extractor(Part(40_200));
        Assert.True(large - small < 4_000, $"{large - small} extra bytes for 40,000 more paragraphs ({small} for 200, {large} for 40,200)");

        // The whole path, through the zip reader: the archive layer's read buffering adds a little per megabyte, not per paragraph.
        static long Whole(int paragraphs)
        {
            string body = string.Concat(Enumerable.Repeat("<w:p><w:r><w:t>some text here</w:t></w:r><w:r><w:tab/><w:t>and more &amp; more</w:t></w:r></w:p>", paragraphs));
            using IByteSource source = ByteSource.FromMemory(Docx(body));
            OoxmlTextExtractor.Extract(source, TextWriter.Null);
            long before = GC.GetAllocatedBytesForCurrentThread();
            OoxmlTextExtractor.Extract(source, TextWriter.Null);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        double perParagraph = (Whole(40_200) - Whole(200)) / 40_000.0;
        Assert.True(perParagraph < 4.0, $"{perParagraph} bytes per paragraph through the package");
    }

    internal static string ReadPartText(byte[] package, string name)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(package));
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
