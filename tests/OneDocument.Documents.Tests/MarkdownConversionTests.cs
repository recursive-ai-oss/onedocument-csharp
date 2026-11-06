using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Markdown;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

public sealed class MarkdownConversionTests
{
    static MarkdownConversionTests() => RecursiveAI.OneDocument.Documents.DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

    // What a reader of the document sees: each block's style, text and list label, each table's cells, the targets of links.
    internal static string Signature(Document doc)
    {
        var sb = new StringBuilder();
        var labels = new Dictionary<BlockElement, string>();
        foreach (var label in doc.Numbering.LabelBody())
        {
            labels[label.Paragraph] = label.Text;
        }

        void Blocks(IEnumerable<BlockElement> blocks, string indent)
        {
            foreach (BlockElement b in blocks)
            {
                switch (b)
                {
                    case Paragraph p:
                        if (p.Text.Length == 0 && p.StyleId is null && !labels.ContainsKey(p))
                        {
                            break;
                        }

                        sb.Append(indent).Append(p.StyleId ?? "-").Append('|').Append(labels.TryGetValue(p, out string? l) ? l : string.Empty).Append('|').Append(p.Text.TrimEnd());
                        foreach (Hyperlink h in p.Inlines().OfType<Hyperlink>())
                        {
                            sb.Append(" <").Append(Target(doc, h)).Append('>');
                        }

                        sb.Append('\n');
                        break;
                    case Table t:
                        sb.Append(indent).Append("table\n");
                        foreach (TableRow row in t.Rows())
                        {
                            sb.Append(indent).Append("  row:").Append(string.Join(" ; ", row.Cells().Select(c => c.Text))).Append('\n');
                        }

                        break;
                }
            }
        }

        Blocks(doc.Body.Blocks(), string.Empty);
        return sb.ToString();
    }

    private static string Target(Document doc, Hyperlink h)
    {
        if (h.Anchor is { } a)
        {
            return "#" + a;
        }

        foreach (Relationship r in doc.Package.GetRelationships(doc.MainPartName))
        {
            if (r.Id == h.RelationshipId)
            {
                return r.Target;
            }
        }

        return "?";
    }

    private const string Sample =
        "---\ntitle: A sample\nauthor: Someone\n---\n\n# Heading one\n\nA paragraph with *emphasis*, **strong**, ***both***, ~~struck~~ and `code`.\n" +
        "A second line, then a [link](https://example.com/a?b=1 \"Title\") and <https://example.org>.\n\n" +
        "## Lists\n\n- apple\n- banana\n  - nested one\n  - nested two\n- cherry\n\n3. three\n4. four\n\n- [ ] open task\n- [x] done task\n\n" +
        "> quoted text\n> over two lines\n\n```csharp\nvar x = 1;\n\nvar y = 2;\n```\n\n| Name | Qty |\n|:-----|----:|\n| a | 1 |\n| b | 2 |\n\n---\n\nA note.[^1]\n\n[^1]: The note text.\n";

    [Fact]
    public void ImportMapsEachConstructToItsStyle()
    {
        using Document doc = Document.ParseMarkdown(Sample);
        Assert.Equal(OfficeFormat.Markdown, doc.Format);
        string sig = Signature(doc);
        Assert.Contains("Heading1||Heading one", sig);
        Assert.Contains("Heading2||Lists", sig);
        Assert.Contains("-||A paragraph with emphasis, strong, both, struck and code. A second line, then a link and https://example.org.", sig);
        Assert.Contains("<https://example.com/a?b=1>", sig);
        Assert.Contains("ListParagraph|", sig);
        Assert.Contains("ListParagraph|3.|three", sig);
        Assert.Contains("table\n  row:Name ; Qty\n  row:a ; 1\n  row:b ; 2", sig);
        Assert.Equal("A sample", doc.Properties.Core.Title);
        Assert.Equal("Someone", doc.Properties.Core.Creator);
        Assert.Single(doc.Footnotes.Normal());
        Assert.Contains("SourceCode||var x = 1;", sig);

        // Strong and emphasis are run properties.
        Paragraph para = doc.Paragraphs.First(p => p.Text.StartsWith("A paragraph", StringComparison.Ordinal));
        Assert.Contains(para.Runs(), r => r.Text == "strong" && r.Properties?.Bold is not null);
        Assert.Contains(para.Runs(), r => r.Text == "emphasis" && r.Properties?.Italic is not null);
        Assert.Contains(para.Runs(), r => r.Text == "struck" && r.Properties?.Strike is not null);
    }

    // With ONEDOCUMENT_MD_SAMPLES set, writes the sample as DOCX (and the Markdown it exports as) for opening in other applications and validating against the schemas.
    [Fact]
    public void WritesSamplesForExternalChecks()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_MD_SAMPLES");
        if (dir is null)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using Document doc = Document.ParseMarkdown(Sample);
        doc.SaveAs(Path.Combine(dir, "sample.docx"));
        File.WriteAllText(Path.Combine(dir, "sample-export.md"), doc.ToMarkdown());
        using Document nested = Document.ParseMarkdown("1. one\n   - a\n     1. deep\n   - b\n2. two\n\n   para in item\n\n   ```\n   code in item\n   ```\n\n> quote\n>\n> - quoted item\n\n| h |\n|---|\n| c |\n\n![x](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==)\n");
        nested.SaveAs(Path.Combine(dir, "nested.docx"));
    }

    [Fact]
    public void ImportThenSaveAsDocxReopens()
    {
        using Document doc = Document.ParseMarkdown(Sample);
        using var buffer = new MemoryStream();
        doc.SaveAs(buffer, OfficeFormat.Docx);
        using Document again = Document.Open(new MemoryStream(buffer.ToArray()));
        Assert.Equal(Signature(doc), Signature(again));
        Assert.Equal(OfficeFormat.Docx, again.Format);
    }

    [Fact]
    public void ListNumbersStartWhereTheMarkdownDoes()
    {
        using Document doc = Document.ParseMarkdown("3. three\n4. four\n\ntext\n\n1. one\n2. two\n");
        var labels = doc.Numbering.LabelBody().Select(l => l.Text).ToArray();
        Assert.Equal(["3.", "4.", "1.", "2."], labels);
    }

    [Fact]
    public void RoundTripMarkdownToDocumentToMarkdownKeepsTheDocument()
    {
        using Document first = Document.ParseMarkdown(Sample);
        string md = first.ToMarkdown();
        using Document second = Document.ParseMarkdown(md);
        Assert.Equal(Signature(first), Signature(second));
        Assert.Contains("# Heading one", md);
        Assert.Contains("| Name | Qty |", md);
        Assert.True(md.Contains("| --- | ---: |", StringComparison.Ordinal), md);
        Assert.Contains("[^1]: The note text.", md);
    }

    [Fact]
    public void RawHtmlGoesThroughTheHtmlImporter()
    {
        using Document doc = Document.ParseMarkdown("<div><h2>From html</h2><ul><li>one</li><li>two</li></ul></div>\n\nA <b>bold <i>and italic</i></b> and <u>under</u> word<br>next <script>alert(1)</script>.\n");
        string sig = Signature(doc);
        Assert.Contains("Heading2||From html", sig);
        Assert.Contains("ListParagraph|\u2022|one", sig);
        Paragraph para = doc.Paragraphs.First(p => p.Text.StartsWith("A bold", StringComparison.Ordinal));
        Assert.Contains(para.Runs(), r => r.Text == "bold " && r.Properties?.Bold is not null);
        Assert.Contains(para.Runs(), r => r.Text == "and italic" && r.Properties?.Italic is not null && r.Properties?.Bold is not null);
        Assert.Contains(para.Runs(), r => r.Text == "under" && r.Properties?.Underline is not null);
        // The script is not carried, and the loss says so.
        Assert.DoesNotContain("alert", doc.ExtractText(), StringComparison.Ordinal);
        Assert.True(doc.ImportLoss!.CountOf("loss.markdown-raw-html") >= 1);
    }

    [Fact]
    public void PicturesNeedAResolverUnlessTheyAreDataAddresses()
    {
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");
        string data = "![dot](data:image/png;base64," + Convert.ToBase64String(png) + ")";
        using Document withData = Document.ParseMarkdown(data);
        Assert.NotNull(withData.Package.FindPart("/word/media/image1.png"));

        int calls = 0;
        using Document noResolver = Document.ParseMarkdown("![x](pic.png)");
        Assert.Equal(1, noResolver.ImportLoss!.CountOf("loss.markdown-image"));
        Assert.Null(noResolver.Package.FindPart("/word/media/image1.png"));
        using Document resolved = Document.ParseMarkdown("![x](pic.png)", new MarkdownImportOptions { ImageResolver = a => { calls++; return a == "pic.png" ? png : null; } });
        Assert.Equal(1, calls);
        Assert.NotNull(resolved.Package.FindPart("/word/media/image1.png"));

        // Export hands the picture to the sink.
        var names = new List<string>();
        string md = resolved.ToMarkdown(new MarkdownExportOptions { ImageSink = (n, ct, d) => { names.Add(n + ":" + ct + ":" + d.Length); return "img/" + n; } });
        Assert.Contains("![x](img/image1.png)", md);
        Assert.Single(names);
    }

    [Fact]
    public void LinksWithOtherSchemesAreKeptAsText()
    {
        using Document doc = Document.ParseMarkdown("[x](javascript:alert(1)) and [y](./a.md) and [z](#top)\n");
        Assert.Equal(1, doc.ImportLoss!.CountOf("loss.markdown-link-target"));
        Assert.Equal(2, doc.Paragraphs.First().Inlines().OfType<Hyperlink>().Count());
    }

    [Fact]
    public void UntouchedMarkdownSavesTheSameBytes()
    {
        string md = "# T\n\ntext  \nmore\n";
        using Document doc = Document.ParseMarkdown(md);
        using var ms = new MemoryStream();
        doc.SaveAs(ms, OfficeFormat.Markdown);
        Assert.Equal(md, Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public void ExportWritesHeadingsListsTablesAndEscapes()
    {
        const string body =
            "<w:p><w:pPr><w:pStyle w:val=\"Heading2\"/></w:pPr><w:r><w:t>Title *x*</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t xml:space=\"preserve\">Plain </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r><w:r><w:t xml:space=\"preserve\"> and 1. not a list_item</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>one</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>sub</w:t></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>H1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>H2</w:t></w:r></w:p></w:tc></w:tr><w:tr><w:tc><w:p><w:r><w:t>a|b</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>2</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        const string styles = "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/></w:style>";
        const string numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"-\"/></w:lvl><w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"-\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        using Document doc = Document.FromPackage(TestDocs.Package(body, styles, numbering));
        string md = doc.ToMarkdown();
        Assert.Equal(
            "## Title \\*x\\*\n\nPlain **bold** and 1. not a list_item\n\n- one\n  - sub\n\n| H1 | H2 |\n| --- | --- |\n| a\\|b | 2 |\n", md);
    }

    [Fact]
    public void ConvertByExtension()
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "md-convert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string md = Path.Combine(dir, "a.md");
            File.WriteAllText(md, Sample);
            string docx = Path.Combine(dir, "a.docx");
            Office.Convert(md, docx);
            using (Document d = Document.Open(docx))
            {
                Assert.Contains("Heading one", d.ExtractText());
            }

            string back = Path.Combine(dir, "b.md");
            Office.Convert(docx, back);
            Assert.StartsWith("---\ntitle: A sample", File.ReadAllText(back), StringComparison.Ordinal);
            using Document viaFormat = Document.Open(md, OfficeFormat.Markdown);
            Assert.Equal(OfficeFormat.Markdown, viaFormat.Format);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DeepNestingIsReportedAndBounded()
    {
        var defects = new DefectCollection();
        var context = new OpenContext(new OfficeLimits { MaxRecursionDepth = 32 }, defects);
        using Document quotes = Document.ParseMarkdown(new string('>', 20_000) + " deep\n", context: context);
        Assert.Contains("deep", quotes.ExtractText());
        Assert.Contains(defects, d => d.Code == "markdown.too-deep");
    }

    [Fact]
    public void InvalidUtf8IsReplacedAndReported()
    {
        var defects = new DefectCollection();
        byte[] bytes = [.. "ok "u8.ToArray(), 0xFF, 0xFE, 0x41, .. " end\n"u8.ToArray()];
        // 0xFF 0xFE would be a UTF-16 mark at the very start, so the invalid bytes sit inside the text.
        using Document doc = Document.OpenMarkdown(new MemoryStream(bytes), context: new OpenContext(null, defects));
        Assert.Contains(defects, d => d.Code == "markdown.encoding-replaced");
        Assert.Contains("\uFFFD", doc.ExtractText());
    }

    // The worst cases of the inline and block algorithms, at two sizes: the time must grow about as fast as the input (a quadratic parser would take 4x, not 2x).
    [Theory]
    [InlineData("*a ", "b* ")]
    [InlineData("[a](", "")]
    [InlineData("` `` ``` ", "")]
    [InlineData("![", "](u)")]
    [InlineData("<!-- ", "")]
    [InlineData("_a *b ", "")]
    [InlineData("> ", "")]
    [InlineData("- ", "")]
    [InlineData("[a]: /u\n", "")]
    [InlineData("<a ", "")]
    [InlineData("www.a.b(", "")]
    public void PathologicalInputsParseInLinearTime(string unit, string tail)
    {
        static long Time(string text)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            MarkdownParser.Parse(text, MarkdownParserOptions.Gfm);
            return sw.ElapsedTicks;
        }

        string Build(int n) => string.Concat(Enumerable.Repeat(unit, n)) + string.Concat(Enumerable.Repeat(tail, n)) + "\n";
        _ = Time(Build(2000));
        long small = Math.Max(1, Time(Build(50_000)));
        long large = Time(Build(200_000));
        // 4x the input: linear is 4x the time; leave room for noise, a quadratic parser would be 16x.
        Assert.True(large < small * 10 + System.Diagnostics.Stopwatch.Frequency / 20, unit + ": " + small + " -> " + large);
    }
}
