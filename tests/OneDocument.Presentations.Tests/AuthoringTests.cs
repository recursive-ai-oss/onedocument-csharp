using System.Text;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class AuthoringTests
{
    // A 1 x 1 pixel PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void AddSlide_ByLayoutName_AndSetPlaceholderText()
    {
        using Presentation deck = Presentation.Create();
        Slide title = deck.AddSlide("title slide");
        Slide content = deck.AddSlide("Title and Content");
        title.SetPlaceholderText("title", "Quarterly review");
        title.SetPlaceholderText("subTitle", "Half-year");
        content.SetPlaceholderText("title", "Agenda");
        content.SetPlaceholderText(1, "One\nTwo\nThree");

        Assert.Equal(2, deck.Slides.Count);
        Assert.Equal("Title Slide", title.Layout!.Name);
        Assert.Equal("Quarterly review", title.Title);
        Assert.Equal("One\nTwo\nThree", content.Shapes.Single(s => s.Placeholder?.Index == 1).TextBody!.Text);

        using Presentation back = Decks.Reopen(Decks.Save(deck));
        Assert.Equal("Agenda", back.Slides[1].Title);
    }

    [Fact]
    public void UnknownLayoutOrPlaceholder_ThrowsTypedErrorsListingWhatExists()
    {
        using Presentation deck = Presentation.Create();
        PresentationAuthoringException noLayout = Assert.Throws<PresentationAuthoringException>(() => deck.AddSlide("Fancy"));
        Assert.Equal("pptx.layout-not-found", noLayout.Defect.Code);
        Assert.Contains("Title Slide", noLayout.Message, StringComparison.Ordinal);
        Assert.Empty(deck.Slides);                                                                   // no slide was added

        Slide blank = deck.AddSlide("Blank");
        PresentationAuthoringException noPh = Assert.Throws<PresentationAuthoringException>(() => blank.SetPlaceholderText("title", "x"));
        Assert.Equal("pptx.placeholder-not-found", noPh.Defect.Code);
        Assert.Throws<PresentationAuthoringException>(() => blank.SetPlaceholderText(7, "x"));
    }

    [Fact]
    public void AddPicture_PlacesItInPointsWithAltText_AndKeepsProportions()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide("Blank");
        Shape pic = slide.AddPicture(Png, 72, 36, 144, null, "company logo");
        Assert.Equal(ShapeKind.Picture, pic.Kind);
        Assert.Equal(new ShapeBounds(914400, 457200, 1828800, 1828800), pic.Bounds);
        Assert.Equal("company logo", pic.AltText);
        Shape second = slide.AddPicture(Png, 0, 0, 50, 20);
        Assert.NotEqual(pic.Id, second.Id);
        Assert.NotNull(pic.ImagePartName);

        byte[] bytes = Decks.Save(deck);
        Assert.Equal(2, Decks.Entries(bytes).Keys.Count(k => k.StartsWith("ppt/media/", StringComparison.Ordinal)));

        int parts = deck.Package.Parts.Count;
        Assert.Throws<ArgumentException>(() => slide.AddPicture([1, 2, 3, 4], 0, 0, 10));
        Assert.Throws<ArgumentException>(() => slide.AddPicture(Png, 0, 0, -10));
        Assert.Throws<ArgumentException>(() => slide.AddPicture(Png, double.NaN, 0, 10));
        Assert.Equal(parts, deck.Package.Parts.Count);
    }

    [Fact]
    public void AddTable_FromRows_AndEmpty()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide("Blank");
        Table table = slide.AddTable([["Region", "Sales"], ["North", "12"], ["South"]], 36, 100, 400);
        Assert.Equal(3, table.RowCount);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal("North", table[1, 0].Text);
        Assert.Equal(string.Empty, table[2, 1].Text);
        Assert.True(table.HasHeaderRow);
        table[2, 1].SetText("7");
        Assert.Equal("7", table[2, 1].Text);
        Assert.Equal(400L * 12700, table.ColumnWidths.Sum());

        Table empty = slide.AddTable(2, 3, 0, 300, 300);
        Assert.Equal(2, empty.RowCount);
        Assert.NotEqual(table.Frame.Id, empty.Frame.Id);
        Assert.Throws<ArgumentException>(() => slide.AddTable(Array.Empty<string[]>(), 0, 0, 100));
        Assert.Throws<ArgumentException>(() => slide.AddTable([["a\u0001"]], 0, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => slide.AddTable(0, 2, 0, 0, 100));

        using Presentation back = Decks.Reopen(Decks.Save(deck));
        Assert.Equal("North", back.Slides[0].Shapes.Select(s => s.Table).First(t => t is not null)![1, 0].Text);
    }

    [Fact]
    public void AuthoringEdits_ChangeOnlyTheEditedSlideAndItsNeighbours()
    {
        byte[] original;
        using (Presentation deck = Presentation.Create())
        {
            deck.AddSlide("Blank");
            original = Decks.Save(deck);
        }

        byte[] edited;
        using (Presentation deck = Decks.Reopen(original))
        {
            deck.Slides[0].AddTable(1, 1, 0, 0, 100);
            edited = Decks.Save(deck);
        }

        SortedDictionary<string, byte[]> a = Decks.Entries(original);
        SortedDictionary<string, byte[]> b = Decks.Entries(edited);
        foreach ((string name, byte[] data) in a)
        {
            if (name == "ppt/slides/slide1.xml")
            {
                continue;
            }

            Assert.True(b.TryGetValue(name, out byte[]? now) && now.AsSpan().SequenceEqual(data), name + " changed");
        }
    }

    [Fact]
    public void Sanitize_RunsInMemory_AndTheDeckStaysUsable()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide("Title Slide");
        deck.Package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>Jane Doe</dc:creator></cp:coreProperties>"));
        deck.Package.SetRelationships(null, [.. deck.Package.GetRelationships(), new Relationship("rId9", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")]);
        SanitizeReport report = deck.Sanitize();
        Assert.Contains(report.Removed, r => r.Category == InspectionCategory.Properties);
        Assert.DoesNotContain("Jane Doe", Encoding.UTF8.GetString(deck.Package.FindPart("/docProps/core.xml")?.GetBytes().ToArray() ?? []), StringComparison.Ordinal);
        deck.Slides[0].SetPlaceholderText("title", "after");
        Assert.Equal("after", Decks.Reopen(Decks.Save(deck)).Slides[0].Title);
        Assert.Equal(0, Inspector.Inspect(new MemoryStream(Decks.Save(deck))).CountOf(InspectionCategory.Properties));
    }
}
