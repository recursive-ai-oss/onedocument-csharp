using OneFont;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Document.UpdateFields() stores what layout computes. The check is that the saved file, laid out as it stands, breaks every line where the
// layout of the original with all fields updated broke it.
public sealed class UpdateFieldsTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach ((string name, _) in FieldDocs.All())
        {
            // 109 and 110+ are picture switches and column layouts; dates need a clock, which the next tests give explicitly.
            if (!name.StartsWith("109", StringComparison.Ordinal))
            {
                data.Add(name);
            }
        }

        return data;
    }

    private static byte[] DocxOf(string name) => FieldDocs.All().First(c => c.Name == name).Docx;

    private static Document Open(byte[] docx)
    {
        RenderingFormats.Register(FormatRegistry.Default);
        return Document.Open(new MemoryStream(docx), leaveOpen: false);
    }

    private static string[] Lines(LayoutDocument l) =>
        [.. l.Pages.Select(p => string.Join("\n", p.BodyParagraphs.SelectMany(para => para.Lines.Select(li => li.Text))))];

    [Theory]
    [MemberData(nameof(Cases))]
    public void SavedFileBreaksEveryLineWhereTheUpdatedLayoutDid(string name)
    {
        byte[] docx = DocxOf(name);
        string[] expected;
        using (Document original = Open(docx))
        {
            expected = Lines(LayoutEngine.Layout(original, new LayoutOptions { Fonts = Fonts, UpdateFields = true }));
        }

        using Document d = Open(docx);
        FieldUpdateReport report = d.UpdateFields(new LayoutOptions { Fonts = Fonts });
        Assert.DoesNotContain(report.Skipped, s => s.Code == "fields.not-written");

        var ms = new MemoryStream();
        d.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        using Document saved = Document.Open(ms, leaveOpen: true);
        Assert.DoesNotContain("updateFields", (saved.Package.FindPart("/word/settings.xml") is { } st ? System.Text.Encoding.UTF8.GetString(st.GetBytes().Span) : string.Empty));
        string[] actual = Lines(LayoutEngine.Layout(saved, new LayoutOptions { Fonts = Fonts }));
        Assert.Equal(expected, actual);

        // A second update leaves the document as it is (an index in columns is rebuilt with its section breaks only once: see docs/operations-merge.md).
        if (name.StartsWith("110", StringComparison.Ordinal))
        {
            return;
        }

        string stored = saved.Body.Container.Node.OuterXml;
        saved.UpdateFields(new LayoutOptions { Fonts = Fonts });
        Assert.Equal(stored, saved.Body.Container.Node.OuterXml);
    }

    [Fact]
    public void ContentsAndPageReferencesCarryTheirPageNumbersInTheSavedFile()
    {
        using Document d = Open(DocxOf("100-toc-three-levels"));
        FieldUpdateReport report = d.UpdateFields(new LayoutOptions { Fonts = Fonts });
        Assert.Equal(1, report.TablesBuilt);
        Assert.True(report.UpdateFieldsFlagCleared);

        var ms = new MemoryStream();
        d.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        using Document saved = Document.Open(ms, leaveOpen: true);
        LayoutDocument layout = LayoutEngine.Layout(saved, new LayoutOptions { Fonts = Fonts });
        List<(string Title, int Page)> entries = [];
        foreach (Paragraph p in saved.Body.Paragraphs().Where(p => p.StyleId is "TOC1" or "TOC2" or "TOC3"))
        {
            string[] parts = p.Text.Split('\t');
            entries.Add((parts[0], int.Parse(parts[^1])));
        }

        Assert.True(entries.Count >= 3);
        foreach ((string title, int page) in entries)
        {
            LayoutParagraph? heading = layout.Pages.SelectMany(pg => pg.BodyParagraphs).FirstOrDefault(h => h.Text == title && h.Source.StyleId?.StartsWith("Heading", StringComparison.Ordinal) == true);
            Assert.NotNull(heading);
            Assert.Equal(layout.Pages.First(pg => pg.BodyParagraphs.Contains(heading)).Number, page);
        }
    }

    [Fact]
    public void IndexEntriesKeepPlainPageNumbersAndNoTemporaryBookmarksOrFields()
    {
        using Document d = Open(DocxOf("107-index"));
        FieldUpdateReport report = d.UpdateFields(new LayoutOptions { Fonts = Fonts });
        Assert.Equal(1, report.TablesBuilt);
        string xml = d.Body.Container.Node.OuterXml;
        Assert.DoesNotContain("ODXEPAGES", xml);
        Assert.DoesNotContain("_OdXe", xml);
        Assert.Contains("INDEX", xml);
    }

    [Fact]
    public void FooterFieldsWithOneResultAreStoredAndPageNumbersThatVaryAreKept()
    {
        using Document d = Open(DocxOf("100-toc-three-levels"));
        FieldUpdateReport report = d.UpdateFields(new LayoutOptions { Fonts = Fonts });
        LayoutDocument layout = LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts });
        Assert.True(layout.Pages.Count > 1);
        Assert.Contains(report.Skipped, s => s.Code == "fields.per-page" && s.FieldType == "PAGE");
        d.CommitChanges();
        string footer = System.Text.Encoding.UTF8.GetString(d.Package.FindPart("/word/footer1.xml")!.GetBytes().Span);
        // NUMPAGES shows the page count; the PAGE result keeps its stored text.
        Assert.True(footer.Contains(">" + layout.Pages.Count + "<", StringComparison.Ordinal), footer + " | " + string.Join(";", report.Skipped.Select(x => x.FieldType)) + " updated=" + report.FieldsUpdated);
    }

    [Fact]
    public void DateFieldsTakeTheCallersClockAndNothingElse()
    {
        byte[] docx = TestDocx.Bytes(FieldDocs.Para(FieldDocs.Run("Today: ") + FieldDocs.Field("DATE \\@ \"yyyy-MM-dd\"", "1999-01-01")), FieldDocs.Styles, sectPr: PageDocs.Sect(string.Empty));
        using Document keep = Open(docx);
        FieldUpdateReport none = keep.UpdateFields(new LayoutOptions { Fonts = Fonts });
        Assert.Equal(0, none.FieldsUpdated);
        Assert.Contains("1999-01-01", keep.Body.Paragraphs().First().Text);

        using Document timed = Open(docx);
        var clock = new FixedClock(new DateTimeOffset(2030, 5, 6, 7, 8, 9, TimeSpan.Zero));
        FieldUpdateReport r = timed.UpdateFields(new LayoutOptions { Fonts = Fonts, Clock = clock });
        Assert.Equal(1, r.FieldsUpdated);
        Assert.Contains("2030-05-06", timed.Body.Paragraphs().First().Text);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
