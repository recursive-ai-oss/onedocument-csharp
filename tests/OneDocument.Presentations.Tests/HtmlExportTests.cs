namespace RecursiveAI.OneDocument.Presentations.Tests;

public class HtmlExportTests
{
    [Fact]
    public void Slides_become_sections_with_titles_text_and_notes_and_a_loss_report()
    {
        using Presentation deck = Presentation.Create();
        Slide a = deck.AddSlide(Decks.Layout(deck, "Title and Content"));
        a.Shapes[0].TextBody!.SetText("Quarterly <review>");
        a.Shapes[1].TextBody!.SetText("First point");
        a.Shapes[1].TextBody!.AddParagraph("Second & third");
        Slide b = deck.AddSlide(Decks.Layout(deck, "Title and Content"));
        b.Shapes[0].TextBody!.SetText("Outlook");

        string html = deck.ToHtml();
        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<section class=\"slide\" id=\"slide-1\">", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Quarterly &lt;review&gt;</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<p>First point</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Second &amp; third</p>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Outlook</h2>", html, StringComparison.Ordinal);
        Assert.NotNull(deck.ExportLoss);
        Assert.False(deck.ExportLoss.IsLossless);

        using var ms = new MemoryStream();
        deck.SaveAs(ms, OfficeFormat.Html);
        Assert.Equal(html, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }
}
