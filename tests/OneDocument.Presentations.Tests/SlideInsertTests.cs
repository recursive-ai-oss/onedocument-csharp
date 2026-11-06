using System.Text;
using RecursiveAI.OneDocument.Operations;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class SlideInsertTests
{
    private static Presentation DeckWith(params string[] titles)
    {
        Presentation deck = Presentation.Create();
        foreach (string t in titles)
        {
            deck.AddSlide(Decks.Layout(deck, "Title and Content")).Shapes[0].TextBody!.SetText(t);
        }

        return deck;
    }

    private static string[] Titles(Presentation deck) => deck.Slides.Select(s => s.Title ?? "-").ToArray();

    // A deck whose master is not the default one: its theme has another name, so no destination has an identical master.
    private static Presentation OtherLookDeck(params string[] titles)
    {
        Presentation deck = DeckWith(titles);
        deck.CommitChanges();
        string theme = deck.Package.GetRelationships(deck.Masters[0].PartName).First(r => r.Type.EndsWith("/theme", StringComparison.Ordinal)).Target;
        string themePart = "/ppt/" + theme.Replace("../", string.Empty, StringComparison.Ordinal);
        string xml = Encoding.UTF8.GetString(deck.Package.FindPart(themePart)!.GetBytes().Span);
        deck.Package.FindPart(themePart)!.SetBytes(Encoding.UTF8.GetBytes(xml.Replace("<a:theme ", "<a:theme x=\"other\" ", StringComparison.Ordinal)));
        return deck;
    }

    private static Presentation RoundTrip(Presentation deck)
    {
        byte[] saved = Decks.Save(deck);
        return Decks.Reopen(saved);
    }

    [Fact]
    public void Insert_FromADeckWithTheSameMaster_ReusesItAndKeepsTextAndLayout()
    {
        using Presentation dst = DeckWith("a", "b");
        using Presentation src = DeckWith("x", "y", "z");
        SlideInsertResult r = dst.InsertSlides([src.Slides[0], src.Slides[2]], 1);
        Assert.Equal(["a", "x", "z", "b"], Titles(dst));
        Assert.Equal(2, r.Report.CountOf("slides"));
        Assert.Single(dst.Masters);
        Assert.Single(r.Report.ResolutionsOf("merge.master-reused"));
        Assert.Equal("Title and Content", dst.Slides[1].Layout!.Name);
        Assert.Equal(dst.Masters[0].PartName, dst.Slides[1].Layout!.Master!.PartName);
        using Presentation again = RoundTrip(dst);
        Assert.Equal(["a", "x", "z", "b"], Titles(again));
        Assert.Empty(again.Defects);
        Assert.Equal(4, again.Slides.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Insert_KeepSource_AddsADifferentMasterWithItsLayoutsThemeAndUniqueIds()
    {
        using Presentation dst = DeckWith("a");
        using Presentation src = OtherLookDeck("x");
        int layouts = src.Masters[0].Layouts.Count;
        SlideInsertResult r = dst.InsertSlides(src.Slides);
        Assert.Equal(2, dst.Masters.Count);
        Assert.Equal(1, r.Report.CountOf("masters"));
        Assert.Equal(layouts, r.Report.CountOf("layouts"));
        Assert.Single(r.Report.ResolutionsOf("merge.master-copied"));
        Assert.Equal(dst.Masters[1].PartName, dst.Slides[1].Layout!.Master!.PartName);
        Assert.Equal("Title and Content", dst.Slides[1].Layout!.Name);
        Assert.NotNull(dst.Masters[1].Theme);

        using Presentation again = RoundTrip(dst);
        Assert.Empty(again.Defects);
        Assert.Equal(2, again.Masters.Count);
        string xml = again.MainMarkup.Root.OuterXml;
        string[] ids = [.. System.Text.RegularExpressions.Regex.Matches(xml, "sldMasterId id=\"(\\d+)\"").Select(m => m.Groups[1].Value)];
        Assert.Equal(2, ids.Distinct().Count());
        List<string> all = [.. ids];
        foreach (SlideMaster m in again.Masters)
        {
            all.AddRange(System.Text.RegularExpressions.Regex.Matches(m.Markup.Node.OuterXml, "sldLayoutId id=\"(\\d+)\"").Select(x => x.Groups[1].Value));
        }

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal("x", again.Slides[1].Title);
    }

    [Fact]
    public void Insert_UseDestination_MapsTheLayoutByNameAndAddsNoMaster()
    {
        using Presentation dst = DeckWith("a");
        using Presentation src = OtherLookDeck("x");
        SlideInsertResult r = dst.InsertSlides(src.Slides, 0, new SlideInsertOptions { Formatting = SlideFormatting.UseDestination });
        Assert.Single(dst.Masters);
        Assert.Equal(["x", "a"], Titles(dst));
        Assert.Equal("Title and Content", dst.Slides[0].Layout!.Name);
        Assert.Single(r.Report.ResolutionsOf("merge.layout-mapped"));
    }

    [Fact]
    public void Insert_BringsPicturesByContentChartsWithTheirWorkbooksAndNotes()
    {
        using Presentation dst = DeckWith("a");
        Decks.AddNotesMaster(dst);
        string shared = Decks.Attach(dst, dst.Slides[0].PartName, "image", "/ppt/media/image1.png", "image/png", [9, 9, 9]);
        Decks.AddShapeXml(dst.Slides[0], Decks.Picture(20, shared));

        using Presentation src = DeckWith("x");
        Slide s = src.Slides[0];
        string same = Decks.Attach(src, s.PartName, "image", "/ppt/media/image7.png", "image/png", [9, 9, 9]);
        string other = Decks.Attach(src, s.PartName, "image", "/ppt/media/image8.png", "image/png", [8, 8]);
        Decks.AddShapeXml(s, Decks.Picture(30, same));
        Decks.AddShapeXml(s, Decks.Picture(31, other));
        Decks.Attach(src, s.PartName, "chart", "/ppt/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", Decks.Utf8("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/>"));
        Decks.Attach(src, "/ppt/charts/chart1.xml", "package", "/ppt/embeddings/Microsoft_Excel_Sheet1.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [7, 7]);
        Decks.AddNotesMaster(src);
        s.SetNotesText("speaker");

        SlideInsertResult r = dst.InsertSlides(src.Slides);
        Assert.Equal(1, r.Report.CountOf("images"));
        Assert.Equal(1, r.Report.CountOf("parts-shared"));
        Assert.Equal(2, r.Report.CountOf("parts"));
        Assert.Equal(1, r.Report.CountOf("notes"));
        Assert.Equal("speaker", dst.Slides[1].Notes!.NotesText);
        Assert.Equal(dst.NotesMaster!.PartName, dst.Slides[1].Notes!.NotesMaster!.PartName);

        byte[] saved = Decks.Save(dst);
        using Presentation again = Decks.Reopen(saved);
        Assert.Empty(again.Defects);
        var entries = Decks.Entries(saved);
        Assert.Equal(2, entries.Keys.Count(k => k.StartsWith("ppt/media/", StringComparison.Ordinal)));
        Assert.Single(entries.Keys, k => k.StartsWith("ppt/embeddings/", StringComparison.Ordinal));
        Assert.Single(entries.Keys, k => k.StartsWith("ppt/charts/", StringComparison.Ordinal) && k.EndsWith(".xml", StringComparison.Ordinal));
    }

    [Fact]
    public void Insert_NotesAreDroppedWithALossItemWhenTheDestinationHasNoNotesMaster()
    {
        using Presentation dst = DeckWith("a");
        using Presentation src = DeckWith("x");
        Decks.AddNotesMaster(src);
        src.Slides[0].SetNotesText("speaker");
        SlideInsertResult r = dst.InsertSlides(src.Slides);
        Assert.Null(dst.Slides[1].Notes);
        Assert.Equal(1, r.Report.Loss.CountOf("merge.notes-dropped"));
    }

    [Fact]
    public void Insert_ReportsADifferentSlideSize()
    {
        using Presentation dst = DeckWith("a");
        using Presentation src = DeckWith("x");
        src.Markup.FindChild(RecursiveAI.OneDocument.Schema.SchemaNamespace.Presentation, "sldSz"u8)!.Value.Node.SetAttribute("cx", "5000000");
        SlideInsertResult r = dst.InsertSlides(src.Slides);
        Assert.Equal(2, dst.Slides.Count);
        Assert.Equal(1, r.Report.Loss.CountOf("merge.slide-size"));
    }

    [Fact]
    public void Insert_IntoTheSamePresentationDuplicates()
    {
        using Presentation deck = DeckWith("a", "b");
        SlideInsertResult r = deck.InsertSlides([deck.Slides[0]], 2);
        Assert.Equal(["a", "b", "a"], Titles(deck));
        Assert.Equal(1, r.Report.CountOf("slides"));
    }

    [Fact]
    public void Insert_RefusesSlidesFromTwoPresentationsAndABadIndex()
    {
        using Presentation dst = DeckWith("a");
        using Presentation s1 = DeckWith("x");
        using Presentation s2 = DeckWith("y");
        Assert.Throws<ArgumentException>(() => dst.InsertSlides([s1.Slides[0], s2.Slides[0]]));
        Assert.Throws<ArgumentOutOfRangeException>(() => dst.InsertSlides(s1.Slides, 9));
    }

    [Fact]
    public void Extract_KeepsTheRangeAndLeavesOutMastersNoKeptSlideUses()
    {
        using Presentation deck = DeckWith("a", "b");
        using Presentation other = OtherLookDeck("x", "y");
        deck.InsertSlides(other.Slides);
        Assert.Equal(2, deck.Masters.Count);
        using Presentation first = deck.ExtractSlides(0, 2);
        Assert.Equal(["a", "b"], Titles(first));
        Assert.Single(first.Masters);
        using Presentation last = deck.ExtractSlides(2, 2);
        Assert.Equal(["x", "y"], Titles(last));
        Assert.Single(last.Masters);
        Assert.Equal(4, deck.Slides.Count);
        Assert.Equal(2, deck.Masters.Count);

        using Presentation again = RoundTrip(last);
        Assert.Empty(again.Defects);
        Assert.Equal(["x", "y"], Titles(again));
        Assert.True(again.Package.Parts.Count < deck.Package.Parts.Count);
    }

    [Fact]
    public void SplitEvery_GivesPresentationsOfAtMostThatManySlides()
    {
        using Presentation deck = DeckWith("1", "2", "3", "4", "5");
        IReadOnlyList<Presentation> parts = deck.SplitEvery(2);
        try
        {
            Assert.Equal([2, 2, 1], parts.Select(p => p.Slides.Count).ToArray());
            Assert.Equal(["5"], Titles(parts[2]));
        }
        finally
        {
            foreach (Presentation p in parts)
            {
                p.Dispose();
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => deck.SplitEvery(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => deck.ExtractSlides(3, 5));
    }
}
