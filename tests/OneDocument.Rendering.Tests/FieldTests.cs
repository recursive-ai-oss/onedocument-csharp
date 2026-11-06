using System.Globalization;
using OneFont;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Fields that layout resolves: contents, references, captions, running heads, indexes, the picture switches, the stored-versus-updated
// policy and the bounded relayout. Documents are generated (FieldDocs) so every expected value follows from the arithmetic of the page.
public sealed class FieldTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();
    private static readonly (string, string)[] FooterOnly = [("footer1.xml", FieldDocs.Footer())];

    private static LayoutDocument Lay(byte[] docx, Action<LayoutOptions>? configure = null, Action<Document>? prepare = null)
    {
        RenderingFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
        prepare?.Invoke(d);
        var options = new LayoutOptions { Fonts = Fonts };
        configure?.Invoke(options);
        return LayoutEngine.Layout(d, options);
    }

    private static List<string> BodyTexts(LayoutDocument l) => [.. l.Pages.SelectMany(static p => p.BodyParagraphs).Select(static p => p.Text)];

    private static int PageOf(LayoutDocument l, Func<LayoutParagraph, bool> match)
    {
        foreach (LayoutPage page in l.Pages)
        {
            if (page.BodyParagraphs.Any(match))
            {
                return page.Number;
            }
        }

        return -1;
    }

    private static string Paragraph(LayoutDocument l, string startsWith) => BodyTexts(l).First(t => t.StartsWith(startsWith, StringComparison.Ordinal));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void ContentsWithThreeLevelsGetsThePagesTheContentsItselfCauses()
    {
        int n = FieldDocs.LinesPerPage;
        byte[] docx = FieldDocs.Build(FieldDocs.TocBody(n, toc: true), settings: FieldDocs.UpdateFieldsSettings, parts: FooterOnly);
        LayoutDocument control = Lay(FieldDocs.Build(FieldDocs.TocBody(n, toc: false), parts: FooterOnly));

        string before = string.Empty;
        string after = string.Empty;
        LayoutDocument l = Lay(docx, prepare: d => before = d.Body.Container.Node.OuterXml);
        using (Document d2 = Document.Open(new MemoryStream(docx), leaveOpen: false))
        {
            // Laying out never changes the document it is given.
            string xml = d2.Body.Container.Node.OuterXml;
            LayoutEngine.Layout(d2, new LayoutOptions { Fonts = Fonts });
            after = d2.Body.Container.Node.OuterXml;
            Assert.Equal(xml, after);
        }

        Assert.NotEqual(string.Empty, before);
        Assert.NotNull(l.WorkingCopy);
        Assert.Equal(2, l.FieldPasses);
        Assert.DoesNotContain(l.Notes, x => x.Code == "layout.fields-unsettled");

        List<LayoutParagraph> entries = [.. l.Pages.SelectMany(static p => p.BodyParagraphs).Where(static p => p.Source.StyleId?.StartsWith("TOC", StringComparison.Ordinal) == true)];
        Assert.Equal(["TOC1", "TOC2", "TOC3", "TOC1", "TOC2", "TOC1"], entries.Select(static e => e.Source.StyleId));
        string[] titles = ["Alpha", "Alpha detail", "Alpha deeper", "Beta", "Beta detail", "Gamma"];
        for (int i = 0; i < titles.Length; i++)
        {
            string title = titles[i];
            int actual = PageOf(l, p => p.Source.StyleId?.StartsWith("Heading", StringComparison.Ordinal) == true && p.Text == title);
            Assert.Equal(title + "\t" + actual.ToString(CultureInfo.InvariantCulture), entries[i].Text);
        }

        // The longer contents pushed Beta onto page 2; without it Beta is on page 1.
        Assert.Equal(1, PageOf(control, p => p.Text == "Beta"));
        Assert.Equal(2, PageOf(l, p => p.Source.StyleId == "Heading1" && p.Text == "Beta"));
        Assert.EndsWith("\t2", entries[3].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentsWithAStoredResultKeepsItsEntriesAndRefreshesItsPageNumbers()
    {
        int n = FieldDocs.LinesPerPage;
        string toc =
            "<w:p><w:pPr><w:pStyle w:val=\"TOC1\"/></w:pPr><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
            "<w:hyperlink w:anchor=\"_Toc1\"><w:r><w:t>Old title</w:t></w:r><w:r><w:tab/></w:r>" + FieldDocs.Field("PAGEREF _Toc1 \\h", "7") + "</w:hyperlink></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
        string body = toc + FieldDocs.Lines(n) + FieldDocs.Head(1, "Real heading", "_Toc1", 1) + FieldDocs.Lines(2);
        byte[] docx = FieldDocs.Build(body);

        LayoutDocument stored = Lay(docx);
        Assert.Null(stored.WorkingCopy);
        Assert.Equal("Old title\t2", BodyTexts(stored)[0]);

        LayoutDocument updated = Lay(docx, o => o.UpdateFields = true);
        Assert.NotNull(updated.WorkingCopy);
        Assert.Equal("Real heading\t2", BodyTexts(updated)[0]);
    }

    [Fact]
    public void ContentsSwitchesChooseLevelsStylesAndPageNumbers()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "102-toc-switches").Docx);
        List<string> entries = [.. l.Pages.SelectMany(static p => p.BodyParagraphs).Where(static p => p.Source.StyleId?.StartsWith("TOC", StringComparison.Ordinal) == true).Select(static p => p.Text)];
        // \o "1-2" with \n 2-2 (no page numbers on level 2), \p " " (a space instead of the tab), \t "Caption,3" (captions are level 3).
        Assert.Equal(["One 1", "One detail", "Table 1 caption 1", "Two 1"], entries);
    }

    [Fact]
    public void NumberedHeadingsShowTheirNumbersInTheContents()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "101-toc-numbered-headings").Docx);
        List<string> entries = [.. l.Pages.SelectMany(static p => p.BodyParagraphs).Where(static p => p.Source.StyleId?.StartsWith("TOC", StringComparison.Ordinal) == true).Select(static p => p.Text)];
        Assert.Equal(4, entries.Count);
        // The levels of this numbering end their label with a space, and Word 16.0.20527 keeps that in the entry ("1. Introduction").
        Assert.StartsWith("1. Introduction\t", entries[0], StringComparison.Ordinal);
        Assert.StartsWith("1.1 Background\t", entries[1], StringComparison.Ordinal);
        Assert.StartsWith("2. Method\t", entries[2], StringComparison.Ordinal);
    }

    [Fact]
    public void PageReferencesNumbersAndRelativePositionsFollowTheirSwitches()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "103-references").Docx);
        Assert.Equal(2, l.Pages.Count);
        Assert.Equal("See page 2, which is above this paragraph.", Paragraph(l, "See page"));
        // \p on another page says which page; with \n it appends the position to the number (§17.16.5.45, §17.16.5.51).
        Assert.Equal("Forward: on page 2 / i above", Paragraph(l, "Forward"));
        // \n the paragraph's own number, \w the numbers of the levels above it; the last period is dropped. \r is relative to the last
        // numbered paragraph before the field when the field's own paragraph has no number (Word 16.0.20527 answers "i" here).
        Assert.Equal("Clause i / i / 1.a.i in Scope.", Paragraph(l, "Clause"));
        Assert.Equal("Page in capitals: II, in letters: b", Paragraph(l, "Page in capitals"));
    }

    [Fact]
    public void CaptionNumbersCountRestartAndHideAsTheirSwitchesSay()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "104-captions").Docx);
        List<string> texts = BodyTexts(l);
        Assert.Equal(["Figure 1", "Figure 2", "Table I"], texts.Skip(1).Take(3));
        // \s 1 restarts the count at every heading 1.
        Assert.Equal(["Figure 1", "Figure 2"], texts.Skip(5).Take(2));
        // \c repeats, \r 10 restarts at 10, the next one is 11, \h hides the number but still counts.
        Assert.Equal("Repeat: 2, restart: 10, next: 11, hidden: []", texts[^1]);
    }

    [Fact]
    public void RunningHeadsTakeTheFirstAndLastParagraphOfAStyleOnThePage()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "105-styleref-running-head").Docx);
        Assert.Equal(4, l.Pages.Count);
        // Page 3 and 4 have no heading of their own: the search goes back to the nearest one before the page.
        Assert.Equal(
            ["First: Alpha / Last: Beta", "First: Gamma / Last: Gamma", "First: Gamma / Last: Gamma", "First: Gamma / Last: Gamma"],
            l.Pages.Select(static p => p.HeaderParagraphs.Single().Text));
    }

    [Fact]
    public void RunningHeadsCanNameACharacterStyle()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "112-styleref-character-style").Docx);
        Assert.Equal(["On this page: Smith \u2014 Jones", "On this page: Wu \u2014 Xu"], l.Pages.Select(static p => p.HeaderParagraphs.Single().Text));
    }

    [Fact]
    public void RunInIndexesPutSubentriesOnTheMainLine()
    {
        string body = FieldDocs.Para(FieldDocs.Run("x") + FieldDocs.Field("XE \"fruit:apple\"", string.Empty) + FieldDocs.Field("XE \"fruit:pear\"", string.Empty)) + FieldDocs.Para(FieldDocs.Field("INDEX \\r", "Update field"));
        LayoutDocument l = Lay(FieldDocs.Build(body), o => o.UpdateFields = true);
        Assert.Equal("fruit: apple, 1; pear, 1", BodyTexts(l)[1]);
    }

    [Fact]
    public void NoteReferencesShowTheNumberOfTheNoteInTheBookmark()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "106-noteref").Docx);
        Assert.Contains("then 2 and 2.", BodyTexts(l)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndexListsEntriesWithTheirSubentriesAndPages()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "107-index").Docx);
        List<string> texts = BodyTexts(l);
        int at = texts.IndexOf("A");
        Assert.True(at > 0);
        Assert.Equal(["A", "apples, 1", "red, 2", "B", "bananas. See fruit", "P", "pears", "green, 1", "ripe, 2", "S", "span topic, 1\u20132"], texts.Skip(at).Take(11));
        Assert.Equal(["Index1", "Index2"], l.Pages.SelectMany(static p => p.BodyParagraphs).Where(static p => p.Text == "apples, 1" || p.Text == "red, 2").Select(static p => p.Source.StyleId));
    }

    [Fact]
    public void AnAuthoritiesTableGroupsByCategoryAndMergesPages()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "108-authorities").Docx);
        List<string> texts = BodyTexts(l);
        int at = texts.IndexOf("Cases");
        Assert.True(at > 0);
        Assert.Equal(["Cases", "Smith v. Jones, 1 U.S. 1 (1900)\t1, 2", "Statutes", "42 U.S.C. 1\t1"], texts.Skip(at).Take(4));
    }

    [Fact]
    public void PictureSwitchesFormatPageNumbers()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "109-picture-switches").Docx);
        Assert.Equal(2, l.Pages.Count);
        Assert.Equal("Page 01 of two / 2nd / 1 / - 1 - / I", Paragraph(l, "Page 01"));
        Assert.Equal("Second page: p. 2", Paragraph(l, "Second page"));
        // The footers still show the numbers of their pages.
        Assert.Equal(["Page 1 of 2", "Page 2 of 2"], l.Pages.Select(static p => p.FooterParagraphs.Single().Text));
    }

    [Fact]
    public void AnIndexCanRunInColumnsOfItsOwn()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "110-index-columns").Docx);
        Assert.Single(l.Pages);
        // The index has a section of two columns; the empty continuous section after it adds a region of one.
        Assert.Equal([1, 2], l.Pages[0].ColumnRegions.Select(static r => r.Columns.Count).Take(2));
        List<LayoutParagraph> entries = [.. l.Pages[0].BodyParagraphs.Where(static p => p.Source.StyleId == "Index1")];
        Assert.Equal(12, entries.Count);
        Assert.Equal([0, 1], entries.Select(static e => e.Column).Distinct().Order());
    }

    [Fact]
    public void ContentsCanListTcFieldsOfOneIdentifier()
    {
        LayoutDocument l = Lay(FieldDocs.All().First(static c => c.Name == "111-toc-tc-fields").Docx);
        List<string> entries = [.. l.Pages.SelectMany(static p => p.BodyParagraphs).Where(static p => p.Source.StyleId?.StartsWith("TOC", StringComparison.Ordinal) == true).Select(static p => p.Text)];
        Assert.Equal(["Alpha topic\t1", "Beta sub-topic\t2"], entries);
    }

    private static string DocumentFields() =>
        FieldDocs.Para(FieldDocs.Field("IF 1 = 1 \"yes\" \"no\"", "stale") + FieldDocs.Run("|") + FieldDocs.Field("= 2 + 3 * 4 \\# \"0.00\"", "x") + FieldDocs.Run("|") + FieldDocs.Field("DATE \\@ \"yyyy-MM-dd\"", "cached-date") + FieldDocs.Run("|") +
            FieldDocs.Field("AUTHOR", "cached-author") + FieldDocs.Run("|") + FieldDocs.Field("QUOTE \"a\" \"b\"", "q") + FieldDocs.Run("|") + FieldDocs.Field("SYMBOL 65", "?")) +
        FieldDocs.Para(FieldDocs.Run("one two three ") + FieldDocs.Field("NUMWORDS", "0"));

    [Fact]
    public void StoredResultsStayUnlessTheCallerAsksForEveryFieldToBeUpdated()
    {
        byte[] docx = FieldDocs.Build(DocumentFields());
        Assert.Equal("stale|x|cached-date|cached-author|q|?", BodyTexts(Lay(docx))[0]);

        void Author(Document d) => d.Properties.Core.Creator = "Ada";
        LayoutDocument updated = Lay(docx, o => o.UpdateFields = true, Author);
        // DATE keeps its stored result: layout never reads the system clock by itself.
        Assert.Equal("yes|14.00|cached-date|Ada|ab|A", BodyTexts(updated)[0]);
        Assert.Equal("one two three 5", BodyTexts(updated)[1]);

        LayoutDocument dated = Lay(docx, o =>
        {
            o.UpdateFields = true;
            o.Clock = new FixedClock(new DateTimeOffset(2026, 11, 5, 9, 30, 0, TimeSpan.Zero));
        }, Author);
        Assert.Equal("yes|14.00|2026-11-05|Ada|ab|A", BodyTexts(dated)[0]);
    }

    [Fact]
    public void FormulasSumTheCellsAboveInATable()
    {
        string cell(string inner) => "<w:tc><w:tcPr><w:tcW w:w=\"2000\" w:type=\"dxa\"/></w:tcPr>" + FieldDocs.Para(inner) + "</w:tc>";
        string row(string inner) => "<w:tr>" + cell(inner) + "</w:tr>";
        string table = "<w:tbl><w:tblPr><w:tblW w:w=\"2000\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
            row(FieldDocs.Run("Amount")) + row(FieldDocs.Run("10")) + row(FieldDocs.Run("$20.50")) + row(FieldDocs.Field("= SUM(ABOVE) \\# \"$#,##0.00\"", "0")) + "</w:tbl>" + FieldDocs.Para(string.Empty);
        LayoutDocument l = Lay(FieldDocs.Build(table), o => o.UpdateFields = true);
        Assert.Contains("$30.50", l.Pages[0].Tables.Single().Rows.Last().Cells.Single().Paragraphs.Single().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormulasFollowTheSpecificationsExamples()
    {
        // §17.16.3: 1/3 is 0.33; bookmarks name operands; the operators are the spreadsheet ones.
        string body = FieldDocs.Para(FieldDocs.Bookmark(1, "X", FieldDocs.Run("4")) + FieldDocs.Run(" ") + FieldDocs.Bookmark(2, "Y", FieldDocs.Run("2"))) +
            FieldDocs.Para(FieldDocs.Field("= 1/3", "x") + FieldDocs.Run("|") + FieldDocs.Field("=((-1 + X^2) * 3 - Y)/2", "x") + FieldDocs.Run("|") + FieldDocs.Field("= MOD(21,-5)", "x") + FieldDocs.Run("|") + FieldDocs.Field("= INT(-2.5)", "x") + FieldDocs.Run("|") + FieldDocs.Field("= 3 > 2", "x"));
        LayoutDocument l = Lay(FieldDocs.Build(body), o => o.UpdateFields = true);
        Assert.Equal("0.33|21.5|1|-2|1", BodyTexts(l)[1]);
    }

    [Fact]
    public void FormulaRangesCoverTheNumericCellsInTheirDirectionAndStopAtText()
    {
        string cell(string inner) => "<w:tc><w:tcPr><w:tcW w:w=\"1500\" w:type=\"dxa\"/></w:tcPr>" + FieldDocs.Para(inner) + "</w:tc>";
        string row(params string[] cells) => "<w:tr>" + string.Concat(cells) + "</w:tr>";
        string table = "<w:tbl><w:tblPr><w:tblW w:w=\"4500\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"1500\"/><w:gridCol w:w=\"1500\"/><w:gridCol w:w=\"1500\"/></w:tblGrid>" +
            row(cell(FieldDocs.Run("n")), cell(FieldDocs.Run("2")), cell(FieldDocs.Run("x"))) +
            row(cell(FieldDocs.Run("9")), cell(FieldDocs.Run("3")), cell(FieldDocs.Run("y"))) +
            row(cell(FieldDocs.Run("10")), cell(FieldDocs.Run("4")), cell(FieldDocs.Field("= SUM(LEFT)", "x"))) +
            row(cell(FieldDocs.Field("= SUM(ABOVE)", "x")), cell(FieldDocs.Field("= AVERAGE(ABOVE)", "x")), cell(FieldDocs.Field("= SUM(B:B)", "x"))) + "</w:tbl>" + FieldDocs.Para(string.Empty);
        LayoutDocument l = Lay(FieldDocs.Build(table), o => o.UpdateFields = true);
        List<string> cells = [.. l.Pages[0].Tables.Single().Rows.SelectMany(static r => r.Cells).SelectMany(static c => c.Paragraphs).Select(static p => p.Text)];
        // LEFT of C3 is B3 and A3; ABOVE in A4 stops at the text cell "n"; ABOVE in B4 is B1 to B3; B:B is the whole column.
        Assert.Equal(["14", "19", "3", "9"], cells.Skip(8));
    }

    [Fact]
    public void RelayoutIsBoundedAndSaysWhenTheFieldsDoNotSettle()
    {
        int n = FieldDocs.LinesPerPage;
        string longText = string.Concat(Enumerable.Repeat("word ", 25));
        // A field whose text decides the page of the bookmark it names: on page 1 it is long and pushes the bookmark to page 2, where it
        // is short and lets the bookmark back onto page 1, and so on.
        string flip =
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> IF </w:instrText></w:r>" +
            FieldDocs.Field("PAGEREF b", "1") + "<w:r><w:instrText xml:space=\"preserve\"> = 1 \"" + longText + "\" \"x\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
            "<w:r><w:t>x</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
        byte[] docx = FieldDocs.Build(FieldDocs.Lines(n - 2) + flip + FieldDocs.Para(FieldDocs.Bookmark(1, "b", FieldDocs.Run("target"))));

        LayoutDocument l = Lay(docx, o =>
        {
            o.UpdateFields = true;
            o.MaxFieldPasses = 4;
        });
        Assert.Equal(4, l.FieldPasses);
        Assert.Contains(l.Notes, x => x.Code == "layout.fields-unsettled");

        // A document that settles reports no such note and stops early.
        LayoutDocument settled = Lay(FieldDocs.All().First(static c => c.Name == "109-picture-switches").Docx);
        Assert.True(settled.FieldPasses is >= 2 and <= 3);
        Assert.DoesNotContain(settled.Notes, x => x.Code == "layout.fields-unsettled");

        // One pass cannot confirm facts it has not yet seen.
        LayoutDocument one = Lay(FieldDocs.All().First(static c => c.Name == "100-toc-three-levels").Docx, o => o.MaxFieldPasses = 1);
        Assert.Equal(1, one.FieldPasses);
        Assert.Contains(one.Notes, x => x.Code == "layout.fields-unsettled");
    }

    [Fact]
    public void ADocumentWithoutFieldsIsLaidOutOnceFromItself()
    {
        LayoutDocument l = Lay(FieldDocs.Build(FieldDocs.Lines(3)));
        Assert.Equal(1, l.FieldPasses);
        Assert.Null(l.WorkingCopy);
    }

    [Fact]
    public void AFieldThatIsNeverClosedIsLeftAlone()
    {
        string body = "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>stored</w:t></w:r></w:p>";
        LayoutDocument l = Lay(FieldDocs.Build(body), o => o.UpdateFields = true);
        Assert.Null(l.WorkingCopy);
        Assert.Equal("stored", BodyTexts(l)[0]);
    }

    [Fact]
    public void ReferencesToMissingBookmarksKeepTheStoredResultAndSayWhy()
    {
        byte[] docx = FieldDocs.Build(FieldDocs.Para(FieldDocs.Field("REF nowhere \\h", "kept")));
        LayoutDocument l = Lay(docx);
        Assert.Equal("kept", BodyTexts(l)[0]);
        Assert.Contains(l.Notes, x => x.Code == "layout.field-source-missing");
        Assert.Equal("Error! Reference source not found.", BodyTexts(Lay(docx, o => o.UpdateFields = true))[0]);
    }

    [Fact]
    public void ALargeContentsListsEveryHeadingAndSettlesInTwoPasses()
    {
        const int headings = 800;
        var body = new System.Text.StringBuilder(FieldDocs.Para(FieldDocs.Field("TOC \\o \"1-2\" \\h", string.Empty)));
        for (int i = 1; i <= headings; i++)
        {
            body.Append(FieldDocs.Head(1 + (i % 2), "Heading " + i)).Append(FieldDocs.Lines(3));
        }

        LayoutDocument l = Lay(FieldDocs.Build(body.ToString()));
        List<string> entries = [.. l.Pages.SelectMany(static p => p.BodyParagraphs).Where(static p => p.Source.StyleId?.StartsWith("TOC", StringComparison.Ordinal) == true).Select(static p => p.Text)];
        Assert.Equal(headings, entries.Count);
        Assert.Equal(2, l.FieldPasses);
        Assert.StartsWith("Heading " + headings + "\t", entries[^1], StringComparison.Ordinal);
    }
}
