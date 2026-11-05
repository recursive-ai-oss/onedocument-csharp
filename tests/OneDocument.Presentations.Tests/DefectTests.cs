using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class DefectTests
{
    private static InMemoryPackage Package(string presentationXml, string? slideXml = null, string slideRelTarget = "slides/slide1.xml")
    {
        var p = new InMemoryPackage();
        p.SetRelationships(null, [new Relationship("rId1", Decks.Rel + "officeDocument", "ppt/presentation.xml")]);
        p.AddPart("/ppt/presentation.xml", Decks.Pml + "presentation.main+xml", Decks.Utf8(presentationXml));
        p.SetRelationships("/ppt/presentation.xml", [new Relationship("rId1", Decks.Rel + "slide", slideRelTarget), new Relationship("rId2", Decks.Rel + "slide", "slides/missing.xml")]);
        if (slideXml is not null)
        {
            p.AddPart("/ppt/slides/slide1.xml", Decks.Pml + "slide+xml", Decks.Utf8(slideXml));
        }

        return p;
    }

    private static string Pres(string sldIds) =>
        "<p:presentation xmlns:p=\"" + Decks.P + "\" xmlns:r=\"" + Decks.R + "\"><p:sldIdLst>" + sldIds + "</p:sldIdLst></p:presentation>";

    private const string GoodSlide = "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree/></p:cSld></p:sld>";

    [Fact]
    public void NoPresentationPartIsFatalAtOpen()
    {
        var p = new InMemoryPackage();
        p.SetRelationships(null, [new Relationship("rId1", Decks.Rel + "officeDocument", "ppt/presentation.xml")]);
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => Presentation.FromPackage(p));
        Assert.Equal("pptx.no-main-part", ex.Defect.Code);
        Assert.Equal("/_rels/.rels", ex.Location.Container);
    }

    [Fact]
    public void ABrokenPresentationPartRaisesAtFirstUseWithItsLocationEveryTime()
    {
        var p = new InMemoryPackage();
        p.SetRelationships(null, [new Relationship("rId1", Decks.Rel + "officeDocument", "ppt/presentation.xml")]);
        p.AddPart("/ppt/presentation.xml", Decks.Pml + "presentation.main+xml", Decks.Utf8("<p:presentation xmlns:p=\"x\"><p:sldIdLst></p:presentation>"));
        using Presentation deck = Presentation.FromPackage(p);
        for (int i = 0; i < 2; i++)
        {
            OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => deck.Slides);
            Assert.StartsWith("xml.", ex.Defect.Code, StringComparison.Ordinal);
            Assert.Equal("/ppt/presentation.xml", ex.Location.Container);
            Assert.True(ex.Location.Offset > 0);
        }
    }

    [Fact]
    public void ASlideListedWithoutAPartIsSkippedAndReportedOnce()
    {
        using Presentation deck = Presentation.FromPackage(Package(Pres("<p:sldId id=\"256\" r:id=\"rId1\"/><p:sldId id=\"257\" r:id=\"rId2\"/><p:sldId id=\"258\" r:id=\"rId9\"/>"), GoodSlide));
        Assert.Single(deck.Slides);
        _ = deck.Slides;
        Defect[] missing = deck.Defects.Where(d => d.Code == "pptx.slide-part-missing").ToArray();
        Assert.Equal(2, missing.Length);
        Assert.All(missing, d => Assert.Equal("/ppt/presentation.xml", d.Location.Container));
        Assert.All(missing, d => Assert.True(d.Location.Offset > 0));
        Assert.All(missing, d => Assert.Equal(DefectSeverity.Dropped, d.Severity));
    }

    [Fact]
    public void DuplicateIdsAreKeptAndAPartListedTwiceIsSkipped()
    {
        using Presentation deck = Presentation.FromPackage(Package(Pres("<p:sldId id=\"256\" r:id=\"rId1\"/><p:sldId id=\"256\" r:id=\"rId1\"/>"), GoodSlide));
        Assert.Single(deck.Slides);
        Assert.Contains(deck.Defects, d => d.Code == "pptx.slide-listed-twice");

        var p = Package(Pres("<p:sldId id=\"256\" r:id=\"rId1\"/><p:sldId id=\"256\" r:id=\"rId3\"/>"), GoodSlide);
        p.AddPart("/ppt/slides/slide3.xml", Decks.Pml + "slide+xml", Decks.Utf8(GoodSlide));
        p.SetRelationships("/ppt/presentation.xml", [.. p.GetRelationships("/ppt/presentation.xml"), new Relationship("rId3", Decks.Rel + "slide", "slides/slide3.xml")]);
        using Presentation twin = Presentation.FromPackage(p);
        Assert.Equal(2, twin.Slides.Count);
        Defect dup = Assert.Single(twin.Defects, d => d.Code == "pptx.slide-id-duplicate");
        Assert.Equal(DefectSeverity.Repaired, dup.Severity);
    }

    [Fact]
    public void ABrokenSlideReadsAsEmptyIsReportedAndSurvivesASaveUnchanged()
    {
        InMemoryPackage p = Package(Pres("<p:sldId id=\"256\" r:id=\"rId1\"/>"), "<p:sld xmlns:p=\"x\"><p:cSld><p:spTree></p:sld>");
        using Presentation deck = Presentation.FromPackage(p);
        Slide slide = Assert.Single(deck.Slides);
        Assert.Empty(slide.Shapes);
        Assert.Null(slide.Title);
        Defect d = Assert.Single(deck.Defects, x => x.Code == "pptx.part-unreadable");
        Assert.Equal("/ppt/slides/slide1.xml", d.Location.Container);
        Assert.True(d.Location.Offset > 0);
        deck.CommitChanges();
        Assert.Equal("<p:sld xmlns:p=\"x\"><p:cSld><p:spTree></p:sld>", System.Text.Encoding.UTF8.GetString(p.FindPart("/ppt/slides/slide1.xml")!.GetBytes().Span));
    }

    [Fact]
    public void ASlideThatIsNotASlideReadsAsEmpty()
    {
        InMemoryPackage p = Package(Pres("<p:sldId id=\"256\" r:id=\"rId1\"/>"), "<p:notes xmlns:p=\"" + Decks.P + "\"/>");
        using Presentation deck = Presentation.FromPackage(p);
        Assert.Empty(deck.Slides[0].Shapes);
        Defect d = Assert.Single(deck.Defects, x => x.Code == "pptx.wrong-root");
        Assert.Equal("/ppt/slides/slide1.xml", d.Location.Container);
    }

    [Fact]
    public void ALayoutWithoutAMasterIsReported()
    {
        using Presentation deck = Presentation.Create();
        SlideLayout layout = Decks.Layout(deck, "Blank");
        deck.Package.SetRelationships(layout.PartName, []);
        Assert.Null(layout.Master);
        Assert.Contains(deck.Defects, d => d.Code == "pptx.master-missing" && d.Location.Container == layout.PartName);
    }

    [Fact]
    public void ADanglingMasterEntryAndLayoutEntryAreSkipped()
    {
        using Presentation deck = Presentation.Create();
        SlideMaster master = deck.Masters[0];
        deck.Package.RemovePart("/ppt/slideLayouts/slideLayout2.xml");
        Assert.Equal(["Title Slide", "Blank"], master.Layouts.Select(l => l.Name).ToArray());
        Defect d = Assert.Single(deck.Defects, x => x.Code == "pptx.layout-missing");
        Assert.Equal(master.PartName, d.Location.Container);
        deck.Package.RemovePart(master.PartName);
        Assert.Empty(deck.Masters);
        Assert.Contains(deck.Defects, x => x.Code == "pptx.master-missing" && x.Location.Container == "/ppt/presentation.xml");
    }

    [Fact]
    public void TruncatedAndGarbageInputIsATypedFailureNeverAnUntypedOne()
    {
        byte[] good;
        using (Presentation deck = Presentation.Create())
        {
            deck.AddSlide(Decks.Layout(deck, "Title and Content")).Shapes[0].TextBody!.SetText("x");
            good = Decks.Save(deck);
        }

        foreach (int cut in new[] { 0, 10, good.Length / 4, good.Length / 2, good.Length - 30, good.Length - 1 })
        {
            byte[] bad = good[..cut];
            try
            {
                using Presentation p = Presentation.Open(new MemoryStream(bad));
                foreach (Slide s in p.Slides)
                {
                    _ = s.Text;
                }

                _ = p.ExtractText();
            }
            catch (OfficeException)
            {
                // typed: fine
            }
        }

        Assert.Throws<OfficeFormatException>(() => Presentation.Open(new MemoryStream(Decks.Utf8("this is not a package at all"))));
    }

    [Fact]
    public void DeeplyNestedGroupsHitTheRecursionLimit()
    {
        using Presentation deck = Presentation.Create(new OpenContext(new OfficeLimits { MaxRecursionDepth = 8 }));
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Blank"));
        string inner = Decks.TextBox(9, "deep");
        for (int i = 0; i < 12; i++)
        {
            inner = "<p:grpSp><p:nvGrpSpPr><p:cNvPr id=\"" + (20 + i) + "\" name=\"g\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + inner + "</p:grpSp>";
        }

        Decks.AddShapeXml(slide, inner);
        OfficeLimitException ex = Assert.Throws<OfficeLimitException>(() => slide.AllShapes().ToList());
        Assert.Equal("limit.max-recursion-depth", ex.Defect.Code);
        Assert.Equal("/ppt/slides/slide1.xml", ex.Location.Container);
    }
}
