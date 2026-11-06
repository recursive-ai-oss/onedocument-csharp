using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Redaction.Tests;

public sealed class RedactionTests
{
    static RedactionTests()
    {
        DocumentFormats.Register(FormatRegistry.Default);
        SpreadsheetFormats.Register(FormatRegistry.Default);
        PresentationFormats.Register(FormatRegistry.Default);
    }

    private const string S = Torture.S;

    // Reads every entry of a zip, recursing into zips inside it, and says which entries still hold the needle in UTF-8, UTF-16 or Latin-1.
    private static List<string> RawHits(byte[] zip, string needle, string prefix = "")
    {
        var hits = new List<string>();
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry e in archive.Entries)
        {
            using var ms = new MemoryStream();
            using (Stream s = e.Open())
            {
                s.CopyTo(ms);
            }

            byte[] bytes = ms.ToArray();
            string where = prefix + e.FullName;
            if (e.FullName.Contains(needle, StringComparison.Ordinal))
            {
                hits.Add(where + " (name)");
            }

            foreach (Encoding enc in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.Latin1 })
            {
                if (bytes.AsSpan().IndexOf(enc.GetBytes(needle)) >= 0)
                {
                    hits.Add(where + " (" + enc.WebName + ")");
                    break;
                }
            }

            if (bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 3)
            {
                try
                {
                    hits.AddRange(RawHits(bytes, needle, where + "!"));
                }
                catch (InvalidDataException)
                {
                    // a zip signature on something that is not a zip: already searched as raw bytes above
                }
            }
        }

        return hits;
    }

    private static List<string> PartNames(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return [.. archive.Entries.Select(static e => "/" + e.FullName)];
    }

    private static string Entry(byte[] zip, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry(name.TrimStart('/'))!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] Redact(OfficeFile file, RedactionPlan plan, out RedactionReport report, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        using var output = new MemoryStream();
        string extension = file.Kind switch { OfficeKind.Document => ".docx", OfficeKind.Workbook => ".xlsx", _ => ".pptx" };
        report = file.Redact(plan, output);
        file.Dispose();
        // The outputs can be kept for the schema validator and LibreOffice (information, run on a build host).
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_REDACTION_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + extension), output.ToArray());
        }

        return output.ToArray();
    }

    private static RedactionPlan Literal(RedactionOptions? options = null)
    {
        var plan = new RedactionPlan();
        plan.Matches.Add(RedactionMatch.Literal(S));
        if (options is not null)
        {
            plan.Options = options;
        }

        return plan;
    }

    private static string Normalise(string text) => Regex.Replace(text, "\u2588+|REDACTED[A-Z]+", "@").Replace(S, "@", StringComparison.Ordinal);

    private static string TextOf(byte[] bytes, OfficeKind kind)
    {
        using var ms = new MemoryStream(bytes);
        using OfficeFile f = kind switch
        {
            OfficeKind.Document => Document.Open(ms),
            OfficeKind.Workbook => Workbook.Open(ms),
            _ => Presentation.Open(ms),
        };
        return f.ExtractText();
    }

    private static void AssertClean(byte[] input, byte[] output, RedactionReport report, OfficeKind kind)
    {
        Assert.True(RawHits(input, S).Count > 5, "the torture file plants the secret in many places");
        Assert.Empty(RawHits(output, S));
        Assert.Empty(report.Survivors);
        Assert.True(report.Verified);
        Assert.True(report.TotalReplacements > 10);
        Assert.Equal(Normalise(TextOf(input, kind)), Normalise(TextOf(output, kind)));
    }

    [Fact]
    public void DocxEverywhere()
    {
        byte[] input = Torture.Docx();
        byte[] output = Redact(Document.Open(new MemoryStream(input)), Literal(), out RedactionReport report);
        AssertClean(input, output, report, OfficeKind.Document);
        Assert.DoesNotContain("/docProps/thumbnail.png", PartNames(output));
        Assert.Contains(report.Items, i => i.Category == "thumbnail");
        Assert.Contains(report.Items, i => i.Category == "comments");
        Assert.Contains(report.Items, i => i.Category == "chart");
        Assert.Contains(report.Items, i => i.Category == "hyperlink-target");
        Assert.Contains(report.Items, i => i.Category == "binary-overwrite");
        Assert.Contains(report.Gaps, g => g.Code == "redaction.opaque-part");
        // The split-run paragraph keeps the first run's formatting and the text after the match.
        string doc = Entry(output, "/word/document.xml");
        Assert.Contains("\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2588 split", string.Concat(Regex.Matches(doc, "<w:t[^>]*>([^<]*)</w:t>").Select(static m => m.Groups[1].Value)).Replace("Plain", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void XlsxEverywhere()
    {
        byte[] input = Torture.Xlsx();
        byte[] output = Redact(Workbook.Open(new MemoryStream(input)), Literal(), out RedactionReport report);
        AssertClean(input, output, report, OfficeKind.Workbook);
        Assert.Contains(report.Items, i => i.Category == "shared-strings" || i.Category == "sheet");
        Assert.Contains(report.Items, i => i.Category == "pivot");
        Assert.Contains(report.Items, i => i.Category == "external-link-cache");
    }

    [Fact]
    public void PptxEverywhere()
    {
        byte[] input = Torture.Pptx();
        byte[] output = Redact(Presentation.Open(new MemoryStream(input)), Literal(), out RedactionReport report);
        AssertClean(input, output, report, OfficeKind.Presentation);
        Assert.Contains(report.Items, i => i.Category == "speaker-notes");
        Assert.Contains(report.Items, i => i.Category == "layouts-masters");
        Assert.Contains(report.Items, i => i.Category == "smartart" || i.Category == "other");
    }

    [Fact]
    public void RegexMatchesAndHiddenLength()
    {
        byte[] input = Torture.Docx();
        var plan = new RedactionPlan { Options = new RedactionOptions { HideLength = true, HiddenLength = 5 } };
        plan.Matches.Add(RedactionMatch.Pattern(@"zebra\d{4}", ignoreCase: true));
        byte[] output = Redact(Document.Open(new MemoryStream(input)), plan, out RedactionReport report);
        Assert.Empty(RawHits(output, S));
        Assert.True(report.Verified);
        Assert.Contains("\u2588\u2588\u2588\u2588\u2588", Entry(output, "/word/document.xml"), StringComparison.Ordinal);
        Assert.DoesNotContain("\u2588\u2588\u2588\u2588\u2588\u2588", Entry(output, "/word/document.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyInputIsWrittenAsOoxml()
    {
        byte[] rtf = Encoding.ASCII.GetBytes("{\\rtf1\\ansi Hello " + S + " there}");
        string source = Path.Combine(Path.GetTempPath(), "redact-" + Guid.NewGuid().ToString("N") + ".rtf");
        string target = Path.ChangeExtension(source, ".docx");
        File.WriteAllBytes(source, rtf);
        try
        {
            RedactionReport report = Office.Redact(source, target, Literal());
            Assert.True(report.Converted);
            Assert.Equal(OfficeFormat.Docx, report.OutputFormat);
            Assert.True(report.Verified);
            byte[] output = File.ReadAllBytes(target);
            Assert.Empty(RawHits(output, S));
            Assert.Contains("Hello", TextOf(output, OfficeKind.Document), StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => Office.Redact(source, source, Literal()));
        }
        finally
        {
            File.Delete(source);
            File.Delete(target);
        }
    }

    [Fact]
    public void SurvivorsAreReportedWhenAMatchCannotBeRemoved()
    {
        // An element name is not text the operation rewrites; the verifier must say so rather than pass.
        byte[] input = new PackageBuilder()
            .Add("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>x</w:t></w:r></w:p></w:body></w:document>")
            .Add("/customXml/item1.xml", "application/xml", "<" + S + ">v</" + S + ">")
            .Rel(null, "rId1", "officeDocument", "word/document.xml")
            .Rel("/word/document.xml", "rId2", "customXml", "../customXml/item1.xml")
            .Build();
        _ = Redact(Document.Open(new MemoryStream(input)), Literal(), out RedactionReport report);
        Assert.False(report.Verified);
        Assert.Contains(report.Survivors, x => x.Location.Contains("item1.xml", StringComparison.Ordinal));
    }

    [Fact]
    public void DocxParagraphRegion()
    {
        byte[] input = Torture.Docx();
        var plan = new RedactionPlan();
        plan.Regions.Add(new ParagraphRegion(13, 13)); // the commented paragraph with a footnote
        plan.Regions.Add(new ParagraphRegion(15, 15)); // the embedded object
        plan.Regions.Add(new ParagraphRegion(8, 8)); // the chart
        byte[] output = Redact(Document.Open(new MemoryStream(input)), plan, out RedactionReport report);
        Assert.True(report.Verified); // no matches in this plan, so there is nothing to find
        string comments = Entry(output, "/word/comments.xml");
        Assert.DoesNotContain("comment " + S, comments, StringComparison.Ordinal);
        Assert.Contains("reply " + S, comments, StringComparison.Ordinal); // not anchored in the region
        Assert.DoesNotContain("footnote " + S, Entry(output, "/word/footnotes.xml"), StringComparison.Ordinal);
        List<string> names = PartNames(output);
        Assert.DoesNotContain("/word/embeddings/oleObject1.bin", names);
        Assert.DoesNotContain("/word/charts/chart1.xml", names);
        Assert.DoesNotContain("/word/embeddings/Microsoft_Excel_Sheet1.xlsx", names);
        Assert.DoesNotContain("commented", Entry(output, "/word/document.xml"), StringComparison.Ordinal);
        Assert.Contains("Unrelated trailing paragraph.", Entry(output, "/word/document.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void NumbersThatMatchBecomeStrings()
    {
        byte[] input = Torture.Xlsx();
        var plan = new RedactionPlan();
        plan.Matches.Add(RedactionMatch.Literal("7731"));
        plan.Matches.Add(RedactionMatch.Literal("42"));
        byte[] output = Redact(Workbook.Open(new MemoryStream(input)), plan, out RedactionReport report);
        Assert.True(report.Verified);
        Assert.Empty(RawHits(output, "7731"));
        string sheet = Entry(output, "/xl/worksheets/sheet1.xml");
        Assert.Matches("<c r=\"C2\"[^>]*t=\"inlineStr\"", sheet);
        Assert.Matches("<c r=\"B3\"[^>]*t=\"inlineStr\"", sheet);
    }

    [Fact]
    public void XlsxCellRegion()
    {
        byte[] input = Torture.Xlsx();
        var plan = new RedactionPlan();
        plan.Regions.Add(new CellRangeRegion("Sheet1", "A2"));
        plan.Regions.Add(new CellRangeRegion("Sheet1", "F6:H11"));
        byte[] output = Redact(Workbook.Open(new MemoryStream(input)), plan, out RedactionReport report);
        Assert.DoesNotContain("untouched text", Entry(output, "/xl/sharedStrings.xml"), StringComparison.Ordinal);
        Assert.Contains("second sheet", Entry(output, "/xl/sharedStrings.xml"), StringComparison.Ordinal);
        Assert.DoesNotContain("note " + S, Entry(output, "/xl/comments1.xml"), StringComparison.Ordinal);
        List<string> names = PartNames(output);
        Assert.DoesNotContain("/xl/charts/chart1.xml", names);
        Assert.DoesNotContain("/xl/embeddings/Microsoft_Excel_Sheet1.xlsx", names);
        Assert.Contains(report.Items, i => i.Category == "region");
        Assert.Contains(report.Gaps, g => g.Code == "redaction.pivot-cache-source");
        Assert.DoesNotContain("example.com/" + S, Entry(output, "/xl/worksheets/_rels/sheet1.xml.rels"), StringComparison.Ordinal);
    }

    [Fact]
    public void PptxSlideAndShapeRegions()
    {
        byte[] input = Torture.Pptx();
        var plan = new RedactionPlan();
        plan.Regions.Add(new SlideRegion(1));
        byte[] output = Redact(Presentation.Open(new MemoryStream(input)), plan, out _);
        List<string> names = PartNames(output);
        Assert.DoesNotContain("/ppt/slides/slide1.xml", names);
        Assert.DoesNotContain("/ppt/notesSlides/notesSlide1.xml", names);
        Assert.DoesNotContain("/ppt/charts/chart1.xml", names);
        Assert.DoesNotContain("/ppt/media/image1.png", names);
        Assert.Contains("/ppt/slides/slide2.xml", names);
        Assert.Single(Regex.Matches(Entry(output, "/ppt/presentation.xml"), "<p:sldId "));

        var shape = new RedactionPlan();
        shape.Regions.Add(new ShapeRegion(1, "Pic"));
        byte[] output2 = Redact(Presentation.Open(new MemoryStream(input)), shape, out RedactionReport report2);
        Assert.DoesNotContain("/ppt/media/image1.png", PartNames(output2));
        Assert.DoesNotContain("<p:timing", Entry(output2, "/ppt/slides/slide1.xml"), StringComparison.Ordinal);
        Assert.Contains(report2.Gaps, g => g.Code == "redaction.animations-removed");
        Assert.Contains("/ppt/slides/slide1.xml", PartNames(output2));
    }

    [Fact]
    public void RegionsThatDoNotFitAreReported()
    {
        var plan = new RedactionPlan();
        plan.Regions.Add(new ParagraphRegion(0, 1));
        plan.Regions.Add(new CellRangeRegion("Nope", "A1"));
        _ = Redact(Workbook.Open(new MemoryStream(Torture.Xlsx())), plan, out RedactionReport report);
        Assert.Contains(report.Gaps, g => g.Code == "redaction.region-not-applicable");
        Assert.Contains(report.Gaps, g => g.Code == "redaction.region-failed");
    }

    [Fact]
    public void PartsThatCannotBeRewrittenAreSearchedAsBytesAndReported()
    {
        byte[] junkZip = [.. new byte[] { 0x50, 0x4B, 3, 4 }, .. Encoding.ASCII.GetBytes("not really a zip " + S)];
        byte[] input = new PackageBuilder()
            .Add("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>x</w:t></w:r></w:p></w:body></w:document>")
            .AddBytes("/customXml/broken.xml", "application/xml", Encoding.ASCII.GetBytes("<a>" + S + "</b>"))
            .AddBytes("/customXml/inner.zip", "application/zip", junkZip)
            .AddBytes("/customXml/" + S + ".bin", "application/octet-stream", [1, 2, 3, 4])
            .Rel(null, "rId1", "officeDocument", "word/document.xml")
            .Rel("/word/document.xml", "rId2", "customXml", "../customXml/broken.xml")
            .Rel("/word/document.xml", "rId3", "customXml", "../customXml/inner.zip")
            .Rel("/word/document.xml", "rId4", "customXml", "../customXml/" + S + ".bin")
            .Build();
        byte[] output = Redact(Document.Open(new MemoryStream(input)), Literal(), out RedactionReport report);
        Assert.Contains(report.Gaps, g => g.Code == "redaction.part-not-xml");
        Assert.Contains(report.Gaps, g => g.Code == "redaction.embedded-package");
        Assert.Contains(report.Gaps, g => g.Code == "redaction.part-name");
        Assert.False(report.Verified); // the part name still holds the secret, and the verdict says so
        Assert.Contains(report.Survivors, x => x.Location.EndsWith("(name)", StringComparison.Ordinal));
        // Only the name itself (in the zip directory, the content types and the relationships) still holds the secret.
        Assert.DoesNotContain(RawHits(output, S), h => !h.Contains("(name)", StringComparison.Ordinal) && !h.StartsWith("[Content_Types]", StringComparison.Ordinal) && !h.Contains("_rels/", StringComparison.Ordinal));
    }

    [Fact]
    public void PatternsSeeWordsAcrossRuns()
    {
        byte[] input = new PackageBuilder()
            .Add("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>the</w:t></w:r><w:r><w:t>ory of the</w:t></w:r><w:r><w:t xml:space=\"preserve\"> the</w:t></w:r><w:r><w:t>re</w:t></w:r></w:p></w:body></w:document>")
            .Rel(null, "rId1", "officeDocument", "word/document.xml")
            .Build();
        var plan = new RedactionPlan();
        plan.Matches.Add(RedactionMatch.Pattern(@"\bthe\b"));
        byte[] output = Redact(Document.Open(new MemoryStream(input)), plan, out RedactionReport report);
        Assert.True(report.Verified);
        Assert.Equal(1, report.TotalReplacements); // "theory" and "there" are different words; only the lone "the" goes
        Assert.Equal("theory of \u2588\u2588\u2588 there", TextOf(output, OfficeKind.Document).TrimEnd());
    }
}
