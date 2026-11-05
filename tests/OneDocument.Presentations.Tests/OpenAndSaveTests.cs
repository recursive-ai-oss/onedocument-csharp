using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class OpenAndSaveTests
{
    [Fact]
    public void CreatedDeckHasMasterLayoutsThemeAndNoSlides()
    {
        using Presentation deck = Presentation.Create();
        Assert.Equal(OfficeFormat.Pptx, deck.Format);
        Assert.Equal(OfficeKind.Presentation, deck.Kind);
        Assert.Empty(deck.Slides);
        SlideMaster master = Assert.Single(deck.Masters);
        Assert.Equal(2147483648, master.Id);
        Assert.Equal(["Title Slide", "Title and Content", "Blank"], master.Layouts.Select(l => l.Name).ToArray());
        Assert.Equal(["title", "obj", "blank"], master.Layouts.Select(l => l.Type).ToArray());
        Assert.Equal(new SlideSize(12192000, 6858000, null), deck.SlideSize);
        Assert.Equal("Calibri Light", master.Theme!.Font(major: true));
        Assert.Equal("4472C4", master.Theme.Color("accent1"));
        Assert.Equal("000000", master.Theme.Color("dk1"));
        Assert.False(deck.IsStrict);
    }

    [Fact]
    public void CreatedDecksAreDeterministic()
    {
        byte[] one, two;
        using (Presentation a = Presentation.Create()) { a.AddSlide(Decks.Layout(a, "Title Slide")); one = Decks.Save(a); }
        using (Presentation b = Presentation.Create()) { b.AddSlide(Decks.Layout(b, "Title Slide")); two = Decks.Save(b); }
        Assert.Empty(Decks.Changed(one, two));
    }

    [Fact]
    public void UntouchedDeckSavesEveryPartByteIdentical()
    {
        byte[] original;
        using (Presentation deck = Presentation.Create())
        {
            Slide s = deck.AddSlide(Decks.Layout(deck, "Title and Content"));
            s.Shapes[0].TextBody!.SetText("Hello");
            original = Decks.Save(deck);
        }

        using Presentation reopened = Decks.Reopen(original);
        // Read everything the hub offers; reading must not touch a part.
        foreach (Slide slide in reopened.Slides)
        {
            _ = slide.Text;
            _ = slide.Layout!.Master!.Theme;
            foreach (Shape shape in slide.AllShapes())
            {
                _ = shape.InheritanceChain;
                _ = shape.ResolveBounds();
            }
        }

        Assert.False(reopened.HasUncommittedChanges);
        Assert.Empty(Decks.Changed(original, Decks.Save(reopened)));
    }

    [Fact]
    public void SaveWritesToTheOpenedFileAtomicallyAndOnlyEditedPartsChange()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pptx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "a.pptx");
            using (Presentation deck = Presentation.Create())
            {
                deck.AddSlide(Decks.Layout(deck, "Title Slide"));
                deck.AddSlide(Decks.Layout(deck, "Title and Content"));
                deck.SaveAs(path);
            }

            byte[] before = File.ReadAllBytes(path);
            using (Presentation deck = Presentation.Open(path))
            {
                deck.Slides[1].Shapes[0].TextBody!.SetText("Second");
                deck.Save();
            }

            byte[] after = File.ReadAllBytes(path);
            Assert.Equal(["ppt/slides/slide2.xml"], Decks.Changed(before, after));
            using Presentation again = Presentation.Open(path);
            Assert.Equal("Second", again.Slides[1].Title);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveAsAnotherPresentationFormatOnlyRetypesTheMainPart()
    {
        using Presentation deck = Presentation.Create();
        byte[] pptx = Decks.Save(deck, OfficeFormat.Pptx);
        byte[] potx = Decks.Save(deck, OfficeFormat.Potx);
        byte[] ppsx = Decks.Save(deck, OfficeFormat.Ppsx);
        Assert.Equal(["[Content_Types].xml"], Decks.Changed(pptx, potx));
        Assert.Contains("presentationml.template.main+xml", Decks.Xml(potx, "[Content_Types].xml"));
        Assert.Contains("presentationml.slideshow.main+xml", Decks.Xml(ppsx, "[Content_Types].xml"));
        using Presentation template = Decks.Reopen(potx);
        Assert.Equal(OfficeFormat.Potx, template.Format);
        Assert.Equal(OfficeFormat.Pptx, deck.Format);
        Assert.Throws<FormatNotAvailableException>(() => Decks.Save(deck, OfficeFormat.Docx));
    }

    [Fact]
    public void FlatOpcRoundTrips()
    {
        using Presentation deck = Presentation.Create();
        deck.AddSlide(Decks.Layout(deck, "Title Slide")).Shapes[0].TextBody!.SetText("Flat");
        using var ms = new MemoryStream();
        deck.SaveAs(ms, OfficeFormat.FlatOpcPresentation);
        ms.Position = 0;
        using Presentation flat = Presentation.Open(ms);
        Assert.Equal(OfficeFormat.FlatOpcPresentation, flat.Format);
        Assert.Equal("Flat", flat.Slides[0].Title);
    }

    [Fact]
    public void OpenReadsOnlyThePackageStructure()
    {
        byte[] bytes;
        using (Presentation deck = Presentation.Create())
        {
            deck.AddSlide(Decks.Layout(deck, "Title Slide"));
            bytes = Decks.Save(deck);
        }

        using Presentation reopened = Decks.Reopen(bytes);
        Assert.False(reopened.HasUncommittedChanges);
        Assert.Single(reopened.Slides);
    }

    [Fact]
    public void OfficeStaticsReachPresentationsOnceRegistered()
    {
        PresentationFormats.Register(FormatRegistry.Default);
        PresentationFormats.Register(FormatRegistry.Default);
        string dir = Path.Combine(Path.GetTempPath(), "pptx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "deck.pptx");
            using (Presentation deck = Presentation.Create())
            {
                deck.AddSlide(Decks.Layout(deck, "Title Slide")).Shapes[0].TextBody!.SetText("Quarterly results");
                deck.SaveAs(path);
            }

            using OfficeFile file = Office.Open(path);
            Presentation opened = Assert.IsType<Presentation>(file);
            Assert.Equal("Quarterly results", opened.Slides[0].Title);
            Assert.Contains("Quarterly results", Office.ExtractText(path));
            Assert.Equal(Office.ExtractText(path), opened.ExtractText());
            Assert.Contains(OfficeFormat.Pptx, FormatRegistry.Default.WritableFormats());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WordDocumentsAreNotPresentations()
    {
        // A package whose main part is a word-processing document is another kind.
        var package = new RecursiveAI.OneDocument.Packaging.InMemoryPackage();
        package.SetRelationships(null, [new RecursiveAI.OneDocument.Packaging.Relationship("rId1", Decks.Rel + "officeDocument", "word/document.xml")]);
        package.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", Decks.Utf8("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'/>"));
        using Presentation p = Presentation.FromPackage(package);
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => p.Markup);
        Assert.Equal("pptx.wrong-root", ex.Defect.Code);
    }
}
