using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Generated documents for footnotes and endnotes. They join the layout case set (LayoutCases.All), so the tests render them and the
// differential tool sends them to Word.
internal static class NoteCases
{
    private const string Lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.";

    private const string Cols2 = "<w:cols w:num=\"2\" w:space=\"720\"/>";

    // n one-line paragraphs.
    internal static string Fill(int n, string tag = "Filler") =>
        string.Concat(Enumerable.Range(1, n).Select(i => TestDocx.P(tag + " line " + i)));

    // n paragraphs of running text with the references given as (paragraph number, ids).
    internal static string Running(int n, Dictionary<int, long[]> refs, string tag = "Para") =>
        string.Concat(Enumerable.Range(1, n).Select(i => NoteDocs.Pn(tag + " " + i + ". " + Lorem + " ", refs.TryGetValue(i, out long[]? ids) ? ids : [])));

    // One-line note paragraphs: "Note k" for k in 1..n.
    internal static string[] Lines(string prefix, int n) => [.. Enumerable.Range(1, n).Select(k => prefix + " " + k)];

    internal static string Sect(string inner, string? type = null) => PageDocs.Sect(inner, type);

    public static IReadOnlyList<(string Name, byte[] Docx)> All()
    {
        var list = new List<(string, byte[])>();
        void Add(string name, byte[] docx) => list.Add((name, docx));

        Add("110-footnote-single", NoteDocs.Build(NoteDocs.Pn("A sentence with a footnote.", 1) + Fill(3), NoteDocs.Fn(1, "The footnote text, a little longer than a line so that it wraps across the page width when the note area is narrow enough to need it. " + Lorem)));

        Add("111-footnotes-many", NoteDocs.Build(
            Running(8, new() { [1] = [1], [2] = [2, 3], [4] = [4], [6] = [5], [7] = [6] }),
            string.Concat(Enumerable.Range(1, 6).Select(i => NoteDocs.Fn(i, "Footnote number " + i + ". " + Lorem[..Math.Min(Lorem.Length, 40 * i)])))));

        Add("112-footnote-split-continuation", NoteDocs.Build(
            NoteDocs.Pn("A short paragraph whose footnote is longer than the page.", 1) + Running(3, new()),
            NoteDocs.Fn(1, Lines("Long note, paragraph", 90)),
            notice: "(continued)"));

        // The reference is on the last line that fits the page; its footnote cannot start there, so the line moves on with it.
        Add("113-footnote-moves-reference-line", NoteDocs.Build(Fill(45, "Above") + NoteDocs.Pn("Reference line near the bottom of the page.", 1) + Fill(4, "Below"), NoteDocs.Fn(1, "The note that pulls its reference line to the next page.")));

        Add("114-footnotes-restart-each-page", NoteDocs.Build(
            Running(30, new() { [1] = [1], [5] = [2], [12] = [3], [20] = [4], [26] = [5], [28] = [6] }),
            string.Concat(Enumerable.Range(1, 6).Select(i => NoteDocs.Fn(i, "Note " + i + " text that sits on whatever page its reference lands on."))),
            settings: NoteDocs.Settings("<w:footnotePr><w:numRestart w:val=\"eachPage\"/></w:footnotePr>")));

        Add("115-footnotes-restart-each-section", NoteDocs.Build(
            NoteDocs.Pn("Section one, first.", 1) + NoteDocs.Pn("Section one, second.", 2) + PageDocs.End(Sect("<w:footnotePr><w:numRestart w:val=\"eachSect\"/></w:footnotePr>")) +
            NoteDocs.Pn("Section two, first.", 3) + NoteDocs.Pn("Section two, second.", 4) + PageDocs.End(Sect("<w:footnotePr><w:numRestart w:val=\"eachSect\"/></w:footnotePr>")) +
            NoteDocs.Pn("Section three, first.", 5),
            string.Concat(Enumerable.Range(1, 5).Select(i => NoteDocs.Fn(i, "Footnote " + i + " of section " + ((i + 1) / 2) + "."))),
            sectPr: Sect("<w:footnotePr><w:numRestart w:val=\"eachSect\"/></w:footnotePr>")));

        Add("116-footnote-number-formats", NoteDocs.Build(
            NoteDocs.Pn("Roman, from three.", 1, 2) + PageDocs.End(Sect("<w:footnotePr><w:numFmt w:val=\"lowerRoman\"/><w:numStart w:val=\"3\"/><w:numRestart w:val=\"eachSect\"/></w:footnotePr>")) +
            NoteDocs.Pn("Letters.", 3, 4) + PageDocs.End(Sect("<w:footnotePr><w:numFmt w:val=\"upperLetter\"/><w:numRestart w:val=\"eachSect\"/></w:footnotePr>")) +
            NoteDocs.Pn("Symbols.", 5, 6, 7),
            string.Concat(Enumerable.Range(1, 7).Select(i => NoteDocs.Fn(i, "Note " + i + "."))),
            sectPr: Sect("<w:footnotePr><w:numFmt w:val=\"chicago\"/><w:numRestart w:val=\"eachSect\"/></w:footnotePr>")));

        Add("117-footnote-custom-mark", NoteDocs.Build(
            "<w:p><w:r><w:t>Custom mark</w:t></w:r>" + NoteDocs.Ref(1, custom: true, customText: "*") + "<w:r><w:t xml:space=\"preserve\"> then an automatic one</w:t></w:r>" + NoteDocs.Ref(2) + "</w:p>",
            NoteDocs.FnCustom(1, "*", "The starred note.") + NoteDocs.Fn(2, "The first numbered note.")));

        Add("118-endnotes-document-end", NoteDocs.Build(
            NoteDocs.PEnd("First paragraph.", 1) + NoteDocs.PEnd("Second paragraph.", 2) + Fill(3),
            endnotes: NoteDocs.En(1, "First endnote text.") + NoteDocs.En(2, "Second endnote. " + Lorem)));

        Add("119-endnotes-section-end", NoteDocs.Build(
            NoteDocs.PEnd("Section one.", 1) + PageDocs.End(Sect(string.Empty)) + NoteDocs.PEnd("Section two.", 2) + NoteDocs.PEnd("More of section two.", 3),
            endnotes: NoteDocs.En(1, "Endnote of section one.") + NoteDocs.En(2, "Endnote two.") + NoteDocs.En(3, "Endnote three."),
            settings: NoteDocs.Settings("<w:endnotePr><w:pos w:val=\"sectEnd\"/></w:endnotePr>")));

        Add("120-footnotes-two-columns", NoteDocs.Build(
            Running(10, new() { [1] = [1], [6] = [2], [8] = [3] }),
            string.Concat(Enumerable.Range(1, 3).Select(i => NoteDocs.Fn(i, "Column footnote " + i + ". " + Lorem[..60]))),
            sectPr: Sect(Cols2)));

        Add("121-footnote-in-table", NoteDocs.Build(
            "<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/><w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/><w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w=\"4680\"/><w:gridCol w:w=\"4680\"/></w:tblGrid>" +
            "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"4680\" w:type=\"dxa\"/></w:tcPr>" + NoteDocs.Pn("Cell with a note", 1) + "</w:tc><w:tc><w:tcPr><w:tcW w:w=\"4680\" w:type=\"dxa\"/></w:tcPr>" + TestDocx.P("Plain cell") + "</w:tc></w:tr></w:tbl>" + NoteDocs.Pn("After the table", 2),
            NoteDocs.Fn(1, "Note from a table cell.") + NoteDocs.Fn(2, "Note after the table.")));

        Add("122-footnotes-beneath-text", NoteDocs.Build(
            NoteDocs.Pn("Only two short lines of text.", 1) + TestDocx.P("The notes follow the text directly."),
            NoteDocs.Fn(1, "Beneath the text, not at the bottom of the page."),
            settings: NoteDocs.Settings("<w:footnotePr><w:pos w:val=\"beneathText\"/></w:footnotePr>")));

        Add("123-footnote-header-ignored", NoteDocs.Build(
            NoteDocs.Pn("Body text with a real note.", 1),
            NoteDocs.Fn(1, "The only note.") + NoteDocs.Fn(2, "Never shown."),
            sectPr: Sect(PageDocs.Ref("header", "default", "header1.xml")),
            parts: [("header1.xml", PageDocs.Story("hdr", "<w:p><w:r><w:t>Header</w:t></w:r>" + NoteDocs.Ref(2) + "</w:p>"))]));

        Add("124-footnotes-fill-pages", NoteDocs.Build(
            Running(30, Enumerable.Range(1, 30).Where(i => i % 3 == 0).ToDictionary(i => i, i => new[] { (long)(i / 3) })),
            string.Concat(Enumerable.Range(1, 10).Select(i => NoteDocs.Fn(i, "Note " + i + ". " + Lorem[..(30 + (i * 9))])))));

        Add("125-footnotes-table-rows", NoteDocs.Build(
            "<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"9360\"/></w:tblGrid>" +
            string.Concat(Enumerable.Range(1, 60).Select(i => "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"9360\" w:type=\"dxa\"/></w:tcPr>" + NoteDocs.Pn("Row " + i, i % 15 == 0 ? new long[] { i / 15 } : []) + "</w:tc></w:tr>")) + "</w:tbl>" + TestDocx.P("After."),
            string.Concat(Enumerable.Range(1, 4).Select(i => NoteDocs.Fn(i, "Row note " + i + ". " + Lorem)))));

        Add("126-footnote-reference-to-missing-note", NoteDocs.Build(NoteDocs.Pn("Dangling", 99) + NoteDocs.Pn("Real", 1), NoteDocs.Fn(1, "The real note.")));

        return list;
    }
}
