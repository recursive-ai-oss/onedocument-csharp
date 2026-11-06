using OneFont;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class PageSplitTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    private static Document Open(byte[] docx)
    {
        RenderingFormats.Register(FormatRegistry.Default);
        return Document.Open(new MemoryStream(docx), leaveOpen: false);
    }

    [Fact]
    public void EachPieceIsOnePageAndTheTextIsTheOriginalsInOrder()
    {
        int n = FieldDocs.LinesPerPage;
        using Document d = Open(FieldDocs.Build(FieldDocs.Lines(n * 2 + 5)));
        var options = new LayoutOptions { Fonts = Fonts };
        Assert.Equal(3, LayoutEngine.Layout(d, options).Pages.Count);

        using SplitResult r = d.SplitByPage(1, options);
        Assert.Equal(3, r.Pieces.Count);
        var texts = new List<string>();
        foreach (DocumentPiece piece in r.Pieces)
        {
            Assert.Single(LayoutEngine.Layout(piece.Document, options).Pages);
            texts.AddRange(piece.Document.Body.Paragraphs().Select(p => p.Text));
        }

        Assert.Equal(d.Body.Paragraphs().Select(p => p.Text), texts);
        Assert.Equal([0, n, 2 * n], r.Pieces.Select(p => p.FirstBlock).ToArray());
    }

    [Fact]
    public void SeveralPagesPerPieceAndABlockThatRunsOverTheBoundary()
    {
        int n = FieldDocs.LinesPerPage;
        // One paragraph longer than a page sits across the first boundary, so the second piece starts after it.
        string longParagraph = FieldDocs.Para(FieldDocs.Run(string.Join(' ', Enumerable.Repeat("word", n * 40))));
        using Document d = Open(FieldDocs.Build(FieldDocs.Lines(n - 2) + longParagraph + FieldDocs.Lines(n)));
        var options = new LayoutOptions { Fonts = Fonts };
        int pages = LayoutEngine.Layout(d, options).Pages.Count;
        Assert.True(pages >= 3);
        using SplitResult r = d.SplitByPage(2, options);
        Assert.True(r.Pieces.Count >= 2);
        Assert.Equal(d.Body.Paragraphs().Count(), r.Pieces.Sum(p => p.BlockCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => d.SplitByPage(0));
    }
}
