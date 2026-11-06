using OneFont;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Footnotes and endnotes on the page model: numbering, placement, splitting and the reference-line rule.
public sealed class NoteTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    private static LayoutDocument Lay(byte[] docx)
    {
        RenderingFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
        return LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts });
    }

    private static LayoutDocument Case(string name) => Lay(LayoutCases.All().First(c => c.Name == name).Docx);

    private static double LineHeight(double size)
    {
        FontMetrics m = Fonts.Resolve(new FontRequest("Liberation Sans"))!.Face.Metrics;
        return (m.Ascender + m.LineGap - m.Descender) * size / m.UnitsPerEm;
    }

    private static IEnumerable<string> FootnoteTexts(LayoutPage p) => p.FootnoteParagraphs.Select(x => x.Text);

    [Fact]
    public void AFootnoteSitsAtTheBottomOfItsPageBehindTheSeparator()
    {
        LayoutDocument l = Case("110-footnote-single");
        LayoutPage page = l.Pages[0];
        Assert.Single(l.Pages);
        Assert.Equal([LayoutNoteRole.Separator, LayoutNoteRole.Footnote], page.NoteBlocks.Select(b => b.Role));
        Assert.Equal("A sentence with a footnote.1", page.BodyParagraphs.First().Text);
        Assert.StartsWith("1 The footnote text", FootnoteTexts(page).First(), StringComparison.Ordinal);

        // The last footnote line ends where the body area ends (72 pt margin).
        LayoutParagraph last = page.FootnoteParagraphs.Last();
        Assert.InRange(last.Bounds.Bottom, 720 - 0.6, 720 + 0.01);
        Assert.True(page.NotesReservation > LineHeight(10));
        Assert.Equal(720 - page.NotesReservation, page.ContentArea.Bottom, 0.5);
        LayoutDecoration rule = Assert.Single(page.Decorations, d => d.DecorationKind == LayoutDecorationKind.NoteSeparator);
        Assert.Equal(468.0 / 3, rule.Bounds.Width, 0.01);
        Assert.True(rule.Bounds.Y < page.FootnoteParagraphs.First().Bounds.Y);

        // The mark in the body is a superscript run and the footnote text is 10 pt.
        LayoutRun mark = page.BodyParagraphs.First().Lines.First().Runs.Last();
        Assert.Equal("1", mark.Text);
        Assert.True(mark.FontSize < 12);
        Assert.Equal(10.0, page.FootnoteParagraphs.First().Lines.First().Runs.Last().FontSize);
    }

    [Fact]
    public void ManyFootnotesOnOnePageAreNumberedInOrder()
    {
        LayoutDocument l = Case("111-footnotes-many");
        LayoutPage page = l.Pages[0];
        Assert.Equal(["1", "2", "3", "4", "5", "6"], page.NoteBlocks.Where(b => b.Role == LayoutNoteRole.Footnote).Select(b => b.Mark));
        string[] starts = [.. FootnoteTexts(page).Select(t => t[..2])];
        Assert.Equal(["1 ", "2 ", "3 ", "4 ", "5 ", "6 "], starts);
        double previous = 0;
        foreach (LayoutParagraph p in page.FootnoteParagraphs)
        {
            Assert.True(p.Bounds.Y >= previous - 0.01);
            previous = p.Bounds.Bottom;
        }

        Assert.True(previous <= 720.01);
    }

    [Fact]
    public void ALongNoteSplitsWithContinuationSeparatorAndNotice()
    {
        LayoutDocument l = Case("112-footnote-split-continuation");
        Assert.Equal(2, l.Pages.Count);
        LayoutPage first = l.Pages[0];
        LayoutPage second = l.Pages[1];

        // The first page keeps the line with the reference, the start of the note and the continuation notice at the foot.
        Assert.Single(first.BodyParagraphs);
        Assert.Equal([LayoutNoteRole.Separator, LayoutNoteRole.Footnote, LayoutNoteRole.ContinuationNotice], first.NoteBlocks.Select(b => b.Role));
        Assert.True(first.NoteBlocks.Single(b => b.Role == LayoutNoteRole.Footnote).ContinuesOnNextPage);
        Assert.Equal("(continued)", first.NoteBlocks.Last().Paragraphs.Single().Text);

        // The second page opens its note area with the continuation separator and holds the rest.
        Assert.Equal(LayoutNoteRole.ContinuationSeparator, second.NoteBlocks.First().Role);
        LayoutNoteBlock rest = second.NoteBlocks.Single(b => b.Role == LayoutNoteRole.Footnote);
        Assert.True(rest.ContinuesFromPreviousPage);
        Assert.False(rest.ContinuesOnNextPage);
        Assert.Contains(second.Decorations, d => d.DecorationKind == LayoutDecorationKind.NoteContinuationSeparator && Math.Abs(d.Bounds.Width - 468) < 0.01);
        Assert.Equal(90, first.FootnoteParagraphs.Count() + second.FootnoteParagraphs.Count());
        Assert.True(second.BodyParagraphs.Any());
        Assert.True(first.FootnoteParagraphs.Last().Bounds.Bottom <= first.NoteBlocks.Last().Bounds.Y + 0.01);
    }

    [Fact]
    public void TheReferenceLineMovesWhenNotEvenTheFirstLineOfItsNoteFits()
    {
        double lh = LineHeight(12);
        int perPage = (int)Math.Floor((648 + 0.01) / lh);
        LayoutDocument tight = Lay(NoteDocs.Build(NoteCases.Fill(perPage - 1) + NoteDocs.Pn("Reference line", 1) + NoteCases.Fill(2), NoteDocs.Fn(1, "The note.")));
        Assert.Equal(2, tight.Pages.Count);
        Assert.Empty(tight.Pages[0].NoteBlocks);
        Assert.DoesNotContain(tight.Pages[0].BodyParagraphs, p => p.Text.StartsWith("Reference", StringComparison.Ordinal));
        Assert.Equal("Reference line1", tight.Pages[1].BodyParagraphs.First().Text);
        Assert.Contains(tight.Pages[1].NoteBlocks, b => b.Role == LayoutNoteRole.Footnote);

        // With two spare lines the same line stays, and its note fits under it.
        LayoutDocument roomy = Lay(NoteDocs.Build(NoteCases.Fill(perPage - 4) + NoteDocs.Pn("Reference line", 1) + NoteCases.Fill(2), NoteDocs.Fn(1, "The note.")));
        Assert.Contains(roomy.Pages[0].BodyParagraphs, p => p.Text.StartsWith("Reference", StringComparison.Ordinal));
        Assert.Contains(roomy.Pages[0].NoteBlocks, b => b.Role == LayoutNoteRole.Footnote);
        foreach (LayoutPage page in roomy.Pages)
        {
            double bodyBottom = page.BodyParagraphs.Max(p => p.Bounds.Bottom);
            double notesTop = page.NoteBlocks.Select(b => b.Bounds.Y).DefaultIfEmpty(720).Min();
            Assert.True(bodyBottom <= notesTop + 0.01);
        }
    }

    [Fact]
    public void NotesTakeSpaceFromTheBodyAndNothingOverlaps()
    {
        LayoutDocument l = Case("124-footnotes-fill-pages");
        Assert.True(l.Pages.Count >= 2);
        foreach (LayoutPage page in l.Pages)
        {
            double bodyBottom = page.BodyParagraphs.Any() ? page.BodyParagraphs.Max(p => p.Bounds.Bottom) : 0;
            double notesTop = page.NoteBlocks.Select(b => b.Bounds.Y).DefaultIfEmpty(720).Min();
            Assert.True(bodyBottom <= notesTop + 0.01, $"page {page.Number}: body to {bodyBottom}, notes from {notesTop}");
            Assert.True(page.FootnoteParagraphs.All(p => p.Bounds.Bottom <= 720.01));
        }

        // Every reference has its note on the page the reference is on.
        foreach (LayoutPage page in l.Pages)
        {
            int refs = page.BodyParagraphs.Count(p => !p.ContinuesOnNextPage && char.IsDigit(p.Text[^1]));
            Assert.Equal(refs, page.NoteBlocks.Count(b => b.Role == LayoutNoteRole.Footnote && !b.ContinuesFromPreviousPage));
        }
    }

    [Fact]
    public void RestartEachPageNumbersFromOneOnEveryPage()
    {
        LayoutDocument l = Case("114-footnotes-restart-each-page");
        Assert.True(l.Pages.Count >= 2);
        int pagesWithNotes = 0;
        foreach (LayoutPage page in l.Pages)
        {
            string[] marks = [.. page.NoteBlocks.Where(b => b.Role == LayoutNoteRole.Footnote && !b.ContinuesFromPreviousPage).Select(b => b.Mark)];
            if (marks.Length == 0)
            {
                continue;
            }

            pagesWithNotes++;
            Assert.Equal(Enumerable.Range(1, marks.Length).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), marks);
        }

        Assert.True(pagesWithNotes >= 2, "the references must fall on at least two pages");
        Assert.DoesNotContain(l.Notes, n => n.Code == "layout.note-numbering-unsettled");
    }

    [Fact]
    public void RestartEachSectionAndFormatsFollowTheSection()
    {
        LayoutDocument sect = Case("115-footnotes-restart-each-section");
        string[] marks = [.. sect.Pages.SelectMany(p => p.NoteBlocks).Where(b => b.Role == LayoutNoteRole.Footnote).Select(b => b.Mark)];
        Assert.Equal(["1", "2", "1", "2", "1"], marks);

        LayoutDocument fmt = Case("116-footnote-number-formats");
        marks = [.. fmt.Pages.SelectMany(p => p.NoteBlocks).Where(b => b.Role == LayoutNoteRole.Footnote).Select(b => b.Mark)];
        Assert.Equal(["iii", "iv", "A", "B", "*", "†", "‡"], marks);
    }

    [Fact]
    public void ACustomMarkTakesNoNumber()
    {
        LayoutDocument l = Case("117-footnote-custom-mark");
        LayoutPage page = l.Pages[0];
        Assert.Equal("Custom mark* then an automatic one1", page.BodyParagraphs.Single().Text);
        Assert.Equal(["* The starred note.", "1 The first numbered note."], FootnoteTexts(page));
    }

    [Fact]
    public void EndnotesFollowTheTextOfTheDocumentWithTheirSeparator()
    {
        LayoutDocument l = Case("118-endnotes-document-end");
        LayoutParagraph[] body = [.. l.Pages.SelectMany(p => p.BodyParagraphs)];
        Assert.Equal(LayoutNoteRole.Separator, body.First(p => p.NoteRole is not null).NoteRole);
        string[] ends = [.. body.Where(p => p.NoteRole == LayoutNoteRole.Endnote).Select(p => p.Text)];

        // Word's default endnote format is lower-case roman.
        Assert.StartsWith("i First endnote text.", ends[0], StringComparison.Ordinal);
        Assert.StartsWith("ii Second endnote.", ends[1], StringComparison.Ordinal);
        int firstEnd = Array.FindIndex(body, p => p.NoteRole is not null);
        Assert.All(body[..firstEnd], p => Assert.Null(p.NoteRole));
        Assert.Contains(l.Pages.SelectMany(p => p.Decorations), d => d.DecorationKind == LayoutDecorationKind.NoteSeparator);
        Assert.Contains(body, p => p.Text == "First paragraph.i");
    }

    [Fact]
    public void EndnotesAtSectionEndComeBeforeTheNextSection()
    {
        LayoutDocument l = Case("119-endnotes-section-end");
        string[] order = [.. l.Pages.SelectMany(p => p.BodyParagraphs).Select(p => p.NoteRole is { } r ? r + ":" + p.Text : p.Text)];
        int sectionTwo = Array.FindIndex(order, t => t.StartsWith("Section two.", StringComparison.Ordinal));
        int endnoteOne = Array.FindIndex(order, t => t.StartsWith("Endnote:i Endnote of section one.", StringComparison.Ordinal));
        Assert.True(endnoteOne >= 0 && endnoteOne < sectionTwo, string.Join(" | ", order));
        Assert.Contains(order, t => t.StartsWith("Endnote:ii Endnote two.", StringComparison.Ordinal) || t.StartsWith("Endnote:i Endnote two.", StringComparison.Ordinal));
        Assert.Equal(1, order.Count(t => t.StartsWith("Separator", StringComparison.Ordinal)) - 1);
    }

    [Fact]
    public void FootnotesInColumnsSitAtTheBottomOfTheirColumn()
    {
        LayoutDocument l = Case("120-footnotes-two-columns");
        LayoutPage page = l.Pages[0];
        LayoutNoteBlock[] notes = [.. page.NoteBlocks.Where(b => b.Role == LayoutNoteRole.Footnote)];
        Assert.Equal(3, notes.Length);
        double columnWidth = (468 - 36) / 2.0;
        foreach (LayoutNoteBlock b in notes)
        {
            double expected = b.Column == 0 ? 72 : 72 + columnWidth + 36;
            Assert.Equal(expected, b.Bounds.X, 0.5);
            Assert.Equal(columnWidth, b.Bounds.Width, 0.5);
        }

        Assert.Contains(notes, b => b.Column == 1);
        foreach (LayoutNoteBlock b in notes)
        {
            double bodyBottom = page.BodyParagraphs.Where(p => p.Column == b.Column).Max(p => p.Bounds.Bottom);
            double top = page.NoteBlocks.Where(x => x.Column == b.Column).Min(x => x.Bounds.Y);
            Assert.True(bodyBottom <= top + 0.01);
        }
    }

    [Fact]
    public void ANoteFromATableCellIsPlacedOnThePageOfItsRow()
    {
        LayoutDocument l = Case("121-footnote-in-table");
        LayoutPage page = l.Pages[0];
        Assert.Equal(["1 Note from a table cell.", "2 Note after the table."], FootnoteTexts(page));
        Assert.True(page.Tables.Single().Bounds.Bottom <= page.NoteBlocks.Min(b => b.Bounds.Y) + 0.01);
    }

    [Fact]
    public void TableRowsKeepTheirNotesOnTheirPage()
    {
        LayoutDocument l = Case("125-footnotes-table-rows");
        Assert.True(l.Pages.Count >= 2);
        foreach (LayoutPage page in l.Pages)
        {
            double tableBottom = page.Tables.Select(t => t.Bounds.Bottom).DefaultIfEmpty(0).Max();
            double notesTop = page.NoteBlocks.Select(b => b.Bounds.Y).DefaultIfEmpty(720).Min();
            Assert.True(tableBottom <= notesTop + 0.01, $"page {page.Number}: table to {tableBottom}, notes from {notesTop}");
        }

        Assert.Equal(["1", "2", "3", "4"], l.Pages.SelectMany(p => p.NoteBlocks).Where(b => b.Role == LayoutNoteRole.Footnote).Select(b => b.Mark));
    }

    [Fact]
    public void AReferenceToAMissingNoteShowsNothing()
    {
        LayoutDocument l = Case("126-footnote-reference-to-missing-note");
        Assert.Equal(["Dangling", "Real1"], l.Pages[0].BodyParagraphs.Select(p => p.Text));
        Assert.Equal(["1 The real note."], FootnoteTexts(l.Pages[0]));
    }

    [Fact]
    public void BeneathTextNotesFollowTheLastLineOfText()
    {
        LayoutDocument l = Case("122-footnotes-beneath-text");
        LayoutPage page = l.Pages[0];
        double textBottom = page.BodyParagraphs.Max(p => p.Bounds.Bottom);
        LayoutNoteBlock first = page.NoteBlocks.First();
        Assert.Equal(textBottom, first.Bounds.Y, 0.5);
        Assert.True(page.FootnoteParagraphs.Last().Bounds.Bottom < 300);
    }

    [Fact]
    public void ADocumentWideBeneathTextPositionIsNotApplied()
    {
        LayoutPage page = Case("122-footnotes-beneath-text-settings-ignored").Pages[0];
        Assert.True(page.FootnoteParagraphs.Last().Bounds.Y > 600);
    }

    [Fact]
    public void ReferencesInHeadersAreIgnored()
    {
        LayoutDocument l = Case("123-footnote-header-ignored");
        LayoutPage page = l.Pages[0];
        Assert.Equal(["1 The only note."], FootnoteTexts(page));
        Assert.Equal("Header", page.HeaderParagraphs.Single().Text);
    }

    [Fact]
    public void DocumentsWithoutNotesAreUntouched()
    {
        LayoutDocument l = Case("25-multipage");
        Assert.All(l.Pages, p => Assert.Empty(p.NoteBlocks));
        Assert.All(l.Pages, p => Assert.Equal(0, p.NotesReservation));
    }

    [Fact]
    public void EveryNoteCaseWritesATaggedPdf()
    {
        foreach ((string name, byte[] docx) in NoteCases.All())
        {
            using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
            using var ms = new MemoryStream();
            LayoutDocument layout = DocumentRenderer.WritePdf(d, ms, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = Fonts }, Compress = false });
            string text = System.Text.Encoding.Latin1.GetString(ms.ToArray());
            Assert.Contains("/StructTreeRoot", text, StringComparison.Ordinal);
            Assert.True(layout.Pages.Count >= 1, name);
        }
    }
}
