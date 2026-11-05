using System.Text;
using OneFont;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class PaginationTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    private static LayoutDocument Lay(string body, string? sectPr = null)
    {
        using Document d = TestDocx.Open(body, sectPr: sectPr);
        return LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts });
    }

    private static double LineHeight
    {
        get
        {
            FontMetrics m = Fonts.Resolve(new FontRequest("Liberation Sans"))!.Face.Metrics;
            return (m.Ascender + m.LineGap - m.Descender) * 12.0 / m.UnitsPerEm;
        }
    }

    [Fact]
    public void ThousandParagraphsFlowOntoPages()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append(TestDocx.P("Paragraph " + i));
        }

        LayoutDocument l = Lay(sb.ToString());
        int perPage = (int)Math.Floor((648 + 0.01) / LineHeight);
        Assert.Equal((1000 + perPage - 1) / perPage, l.Pages.Count);
        Assert.Equal(1000, l.Pages.Sum(p => p.Paragraphs.Count()));
        foreach (LayoutPage p in l.Pages)
        {
            Assert.All(p.Paragraphs, x => Assert.True(x.Bounds.Bottom <= 72 + 648 + 0.01));
        }

        Assert.Equal(Enumerable.Range(1, l.Pages.Count), l.Pages.Select(p => p.Number));
    }

    [Fact]
    public void PageBreakBeforeAndHardBreaks()
    {
        string body = TestDocx.P("one") + TestDocx.P("two", "<w:pageBreakBefore/>") + "<w:p><w:r><w:t>three</w:t><w:br w:type=\"page\"/></w:r></w:p>" + TestDocx.P("four");
        LayoutDocument l = Lay(body);
        Assert.Equal(3, l.Pages.Count);
        Assert.Equal(["two", "three\f"], l.Pages[1].Paragraphs.Select(p => p.Text));
        Assert.Equal("four", l.Pages[2].Paragraphs.Single().Text);
        Assert.Equal(72, l.Pages[2].Paragraphs.Single().Bounds.Y, 3);
    }

    [Fact]
    public void WidowAndOrphanControlKeepTwoLines()
    {
        int perPage = (int)Math.Floor((648 + 0.01) / LineHeight);
        // Fill the page except room for exactly one more line, then add a two-line paragraph: the orphan rule moves it whole.
        var sb = new StringBuilder();
        for (int i = 0; i < perPage - 1; i++)
        {
            sb.Append(TestDocx.P("fill " + i));
        }

        string twoLines = string.Join(' ', Enumerable.Repeat("wordy", 120));
        sb.Append(TestDocx.P(twoLines));
        LayoutDocument l = Lay(sb.ToString());
        LayoutParagraph last = l.Pages[1].Paragraphs.First();
        Assert.Equal(twoLines, last.Text);
        Assert.False(last.ContinuesFromPreviousPage);

        // With widow control off the first line stays on the first page.
        string off = sb.ToString().Replace("</w:t></w:r></w:p><w:p><w:r><w:t xml:space=\"preserve\">wordy", "</w:t></w:r></w:p><w:p><w:r><w:t xml:space=\"preserve\">wordy", StringComparison.Ordinal);
        string unprotected = off.Replace(TestDocx.P(twoLines), TestDocx.P(twoLines, "<w:widowControl w:val=\"0\"/>"), StringComparison.Ordinal);
        LayoutDocument l2 = Lay(unprotected);
        Assert.True(l2.Pages[0].Paragraphs.Last().ContinuesOnNextPage);
    }

    [Fact]
    public void SplitParagraphKeepsTextAcrossPages()
    {
        string text = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 400));
        LayoutDocument l = Lay(TestDocx.P(text));
        Assert.True(l.Pages.Count >= 3);
        var all = l.Pages.SelectMany(p => p.Paragraphs).ToList();
        Assert.All(all, p => Assert.Equal(text, p.Text));
        Assert.True(all[0].ContinuesOnNextPage);
        Assert.True(all[1].ContinuesFromPreviousPage);
        string joined = string.Concat(all.SelectMany(p => p.Lines).Select(x => x.Text));
        Assert.Equal(text, joined);
    }

    [Fact]
    public void KeepWithNextMovesTheHeadingWithItsParagraph()
    {
        int perPage = (int)Math.Floor((648 + 0.01) / LineHeight);
        var sb = new StringBuilder();
        for (int i = 0; i < perPage - 1; i++)
        {
            sb.Append(TestDocx.P("fill " + i));
        }

        sb.Append(TestDocx.P("Heading", "<w:keepNext/>"));
        sb.Append(TestDocx.P("Body text follows."));
        LayoutDocument l = Lay(sb.ToString());
        Assert.Equal("Heading", l.Pages[1].Paragraphs.First().Text);
    }

    [Fact]
    public void KeepLinesTogetherMovesTheWholeParagraph()
    {
        int perPage = (int)Math.Floor((648 + 0.01) / LineHeight);
        var sb = new StringBuilder();
        for (int i = 0; i < perPage - 3; i++)
        {
            sb.Append(TestDocx.P("fill " + i));
        }

        string text = string.Join(' ', Enumerable.Repeat("keep these lines together", 40));
        sb.Append(TestDocx.P(text, "<w:keepLines/>"));
        LayoutDocument l = Lay(sb.ToString());
        Assert.Equal(text, l.Pages[1].Paragraphs.First().Text);
        Assert.False(l.Pages[1].Paragraphs.First().ContinuesFromPreviousPage);
    }

    [Fact]
    public void SectionsUseTheirOwnPageSize()
    {
        string landscape = "<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:pPr><w:r><w:t>wide</w:t></w:r></w:p>";
        LayoutDocument l = Lay(landscape + TestDocx.P("tall"));
        Assert.Equal(2, l.Pages.Count);
        Assert.Equal(792, l.Pages[0].Width);
        Assert.Equal(612, l.Pages[0].Height);
        Assert.Equal(612, l.Pages[1].Width);
        Assert.Equal(0, l.Pages[0].SectionIndex);
        Assert.Equal(1, l.Pages[1].SectionIndex);
    }

    [Fact]
    public void TablesAreLaidOutBetweenParagraphs()
    {
        string table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        LayoutDocument l = Lay(TestDocx.P("a") + table + TestDocx.P("b"));
        Assert.DoesNotContain(l.Notes, n => n.Code == "layout.table-skipped");
        Assert.Equal(2, l.Pages[0].Paragraphs.Count());
        Assert.Equal("cell", l.Pages[0].Tables.Single().Rows.Single().Cells.Single().Paragraphs.Single().Text);
    }

    [Fact]
    public void EmptyDocumentHasOnePage()
    {
        LayoutDocument l = Lay(string.Empty);
        Assert.Single(l.Pages);
        Assert.Empty(l.Pages[0].Paragraphs);
    }

    [Fact]
    public void EmptyParagraphTakesTheParagraphMarksHeight()
    {
        LayoutDocument l = Lay("<w:p/>" + TestDocx.P("after"));
        var ps = l.Pages[0].Paragraphs.ToList();
        Assert.Equal(LineHeight, ps[0].Bounds.Height, 3);
        Assert.Equal(LineHeight, ps[1].Bounds.Y - 72, 3);
    }
}
