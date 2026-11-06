using System.Text;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class MarkdownPresentationTests
{
    [Fact]
    public void SlidesBecomeSectionsWithTitleTextAndNotes()
    {
        using Presentation deck = Presentation.Create();
        Decks.AddNotesMaster(deck);
        Slide first = deck.AddSlide(Decks.Layout(deck, "Title and Content"));
        first.Shapes[0].TextBody!.SetText("Plan *2027*");
        first.Shapes[1].TextBody!.SetText("First point");
        first.Shapes[1].TextBody!.AddParagraph("Second point");
        deck.AddSlide(Decks.Layout(deck, "Title and Content")).Shapes[0].TextBody!.SetText("Second");
        first.SetNotesText("Say this.");
        string md = deck.ToMarkdown();
        Assert.True(md.StartsWith("## Slide 1: Plan \\*2027\\*\n\n- First point\n- Second point\n\n### Notes\n\nSay this.\n\n## Slide 2: Second", StringComparison.Ordinal), md);
        Assert.NotNull(deck.MarkdownExportLoss);
    }

    [Fact]
    public void SaveAsMarkdownGoesThroughTheRegistry()
    {
        PresentationFormats.Register(Formats.FormatRegistry.Default);
        using Presentation deck = Presentation.Create();
        deck.AddSlide(Decks.Layout(deck, "Title and Content")).Shapes[0].TextBody!.SetText("Only");
        using var ms = new MemoryStream();
        deck.SaveAs(ms, OfficeFormat.Markdown);
        Assert.Contains("## Slide 1: Only", Encoding.UTF8.GetString(ms.ToArray()), StringComparison.Ordinal);
    }
}
