using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class InheritanceTests
{
    private static Slide ContentSlide(Presentation deck) => deck.AddSlide(Decks.Layout(deck, "Title and Content"));

    [Fact]
    public void SlidePlaceholdersMatchLayoutByIndexAndTypeThenMasterByType()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = ContentSlide(deck);
        Shape title = slide.Shapes[0];
        Shape body = slide.Shapes[1];
        Assert.Equal(new PlaceholderInfo("title", 0, null, null, true), title.Placeholder);
        Assert.Equal(new PlaceholderInfo("obj", 1, null, null, false), body.Placeholder);

        IReadOnlyList<Shape> titleChain = title.InheritanceChain;
        Assert.Equal(2, titleChain.Count);
        Assert.Same(slide.Layout, titleChain[0].Part);
        Assert.Same(slide.Master, titleChain[1].Part);
        Assert.Equal("Title Placeholder 1", titleChain[1].Name);

        // An untyped placeholder with idx 1 finds the layout's idx 1 and, through the master's body role, the master body.
        IReadOnlyList<Shape> bodyChain = body.InheritanceChain;
        Assert.Equal(["Content Placeholder 2", "Text Placeholder 2"], bodyChain.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void CenteredTitleMatchesTitleAndSubtitleMatchesBody()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Title Slide"));
        Assert.Equal("ctrTitle", slide.Shapes[0].Placeholder!.Value.Type);
        Assert.Equal("Title Placeholder 1", slide.Shapes[0].InheritanceChain[1].Name);
        Assert.Equal("subTitle", slide.Shapes[1].Placeholder!.Value.Type);
        Assert.Equal("Text Placeholder 2", slide.Shapes[1].InheritanceChain[1].Name);

        // A slide placeholder of the same type as another but a different idx finds its own layout placeholder.
        slide.ShapeTreeMarkup.Node.AppendXml(Decks.Utf8("<p:sp><p:nvSpPr><p:cNvPr id=\"9\" name=\"Extra\"/><p:cNvSpPr/><p:nvPr><p:ph type=\"subTitle\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>"));
        Assert.Equal("Subtitle 2", slide.Shapes[2].InheritanceChain[0].Name);
    }

    [Fact]
    public void BoundsComeFromTheNearestPlaceholderThatHasThem()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = ContentSlide(deck);
        Shape title = slide.Shapes[0];
        Assert.Null(title.Bounds);
        // The layout's title has an empty spPr, so the master's title geometry applies.
        Assert.Equal(new ShapeBounds(838200, 365125, 10515600, 1325563), title.ResolveBounds());
        Shape centered = deck.AddSlide(Decks.Layout(deck, "Title Slide")).Shapes[0];
        Assert.Equal(new ShapeBounds(1524000, 1122363, 9144000, 2387600), centered.ResolveBounds());

        title.Markup.FindChild(SchemaNamespace.Presentation, "spPr"u8)!.Value.Node.ReplaceWithXml(
            Decks.Utf8("<p:spPr><a:xfrm><a:off x=\"1\" y=\"2\"/><a:ext cx=\"3\" cy=\"4\"/></a:xfrm></p:spPr>"));
        Assert.Equal(new ShapeBounds(1, 2, 3, 4), title.ResolveBounds());
    }

    [Fact]
    public void BulletsLevelsAndSizesInheritFromTheMasterBodyStyle()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = ContentSlide(deck);
        TextBody body = slide.Shapes[1].TextBody!;
        body.SetText("one\ntwo");
        TextParagraph second = body.Paragraphs[1];
        second.Level = 1;

        ParagraphFormat level0 = body.Paragraphs[0].ResolveFormat();
        Assert.Equal(BulletKind.Character, level0.Bullet.Kind);
        Assert.Equal("\u2022", level0.Bullet.Character);
        Assert.Equal("Arial", level0.Bullet.Font);
        Assert.Equal(228600, level0.MarginLeft);
        Assert.Equal(-228600, level0.Indent);
        Assert.Equal(2800, level0.Run.Size);
        Assert.Equal(new Spacing(true, 90000), level0.LineSpacing);
        Assert.Equal(new Spacing(false, 1000), level0.SpaceBefore);
        Assert.Equal("l", level0.Alignment);

        ParagraphFormat level1 = second.ResolveFormat();
        Assert.Equal(685800, level1.MarginLeft);
        Assert.Equal(2400, level1.Run.Size);
        Assert.Equal(BulletKind.Character, level1.Bullet.Kind);
    }

    [Fact]
    public void LayoutListStyleOverridesTheMasterAndABulletComesAsAUnit()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Title Slide"));

        // The subtitle layout style says buNone, so the master's bullet character (and its font) do not apply, while marL/indent are 0 there.
        ParagraphFormat sub = slide.Shapes[1].TextBody!.Paragraphs[0].ResolveFormat();
        Assert.Equal(BulletKind.None, sub.Bullet.Kind);
        Assert.Null(sub.Bullet.Character);
        Assert.Equal(0, sub.MarginLeft);
        Assert.Equal(0, sub.Indent);
        Assert.Equal("ctr", sub.Alignment);
        Assert.Equal(2400, sub.Run.Size);

        // The centred title: layout 6000 beats the master's 4400; alignment ctr from the layout; font from the master through the theme.
        ParagraphFormat title = slide.Shapes[0].TextBody!.Paragraphs[0].ResolveFormat();
        Assert.Equal(6000, title.Run.Size);
        Assert.Equal("ctr", title.Alignment);
        Assert.Equal("Calibri Light", title.Run.Typeface);
        Assert.Equal(BulletKind.None, title.Bullet.Kind);
    }

    [Fact]
    public void TheParagraphsOwnPropertiesWinAndRunsFallBackToTheLevelDefaults()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = ContentSlide(deck);
        TextBody body = slide.Shapes[1].TextBody!;
        body.SetText("x");
        TextParagraph p = body.Paragraphs[0];
        p.Markup.Node.PrependXml(Decks.Utf8("<a:pPr marL=\"1000\" algn=\"r\"><a:buAutoNum type=\"arabicPeriod\" startAt=\"3\"/></a:pPr>"));
        ParagraphFormat f = p.ResolveFormat();
        Assert.Equal(1000, f.MarginLeft);
        Assert.Equal("r", f.Alignment);
        Assert.Equal(BulletKind.AutoNumber, f.Bullet.Kind);
        Assert.Equal("arabicPeriod", f.Bullet.AutoNumberType);
        Assert.Equal(3, f.Bullet.StartAt);
        Assert.Equal(-228600, f.Indent); // not written by the paragraph, so inherited

        TextRun run = p.Runs[0];
        run.Markup.Node.PrependXml(Decks.Utf8("<a:rPr b=\"1\" sz=\"1000\"/>"));
        RunFormat rf = p.ResolveRunFormat(run);
        Assert.True(rf.Bold);
        Assert.Equal(1000, rf.Size);
        Assert.Equal("Calibri", rf.Typeface);
        Assert.Equal(new ColorValue(ColorKind.Scheme, "tx1"), rf.Color);
        Assert.Equal("000000", slide.ResolveColor(rf.Color));
    }

    [Fact]
    public void BodyPropertiesInheritPerAttribute()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = ContentSlide(deck);
        BodyFormat title = slide.Shapes[0].ResolveBodyFormat();
        Assert.Equal("ctr", title.Anchor);
        Assert.Equal(AutofitKind.ShrinkText, title.Autofit);
        Assert.Equal(91440, title.LeftInset);
        Assert.Equal(45720, title.TopInset);

        // The slide writes only wrap; everything else still comes from the master.
        slide.Shapes[0].TextBody!.BodyProperties.Node.SetAttribute("wrap", "none");
        BodyFormat edited = slide.Shapes[0].ResolveBodyFormat();
        Assert.Equal("none", edited.Wrap);
        Assert.Equal("ctr", edited.Anchor);

        BodyFormat subtitle = deck.AddSlide(Decks.Layout(deck, "Title Slide")).Shapes[0].ResolveBodyFormat();
        Assert.Equal("b", subtitle.Anchor); // the layout's bodyPr
    }

    [Fact]
    public void ColorMapAndSlideOverrideResolveSchemeColors()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = ContentSlide(deck);
        Assert.Equal("lt1", slide.ColorMap!.Map("bg1"));
        Assert.Equal("FFFFFF", slide.ResolveSchemeColor("bg1"));
        Assert.Equal("000000", slide.ResolveSchemeColor("tx1"));
        Assert.Equal("E7E6E6", slide.ResolveSchemeColor("bg2"));
        Assert.Equal("4472C4", slide.ResolveSchemeColor("accent1"));
        Assert.Equal("000000", slide.ResolveSchemeColor("dk1")); // a theme slot named directly
        Assert.Null(slide.ResolveSchemeColor("phClr"));

        // A slide override that swaps light and dark, as a dark-background slide does.
        slide.Markup.Node.LastChild.Remove();
        slide.Markup.Node.AppendXml(Decks.Utf8("<p:clrMapOvr><a:overrideClrMapping bg1=\"dk1\" tx1=\"lt1\" bg2=\"dk2\" tx2=\"lt2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/></p:clrMapOvr>"));
        Assert.Equal("000000", slide.ResolveSchemeColor("bg1"));
        Assert.Equal("FFFFFF", slide.ResolveSchemeColor("tx1"));
        Assert.Equal("FFFFFF", slide.ResolveColor(slide.Shapes[1].TextBody!.Paragraphs[0].ResolveFormat().Run.Color));
    }

    [Fact]
    public void NonPlaceholderTextTakesThePresentationDefaults()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = ContentSlide(deck);
        Decks.AddShapeXml(slide, Decks.TextBox(7, "free"));
        Shape box = slide.Shapes[^1];
        Assert.Null(box.Placeholder);
        Assert.Empty(box.InheritanceChain);
        ParagraphFormat f = box.TextBody!.Paragraphs[0].ResolveFormat();
        Assert.Equal(1800, f.Run.Size);
        Assert.Equal(BulletKind.Unspecified, f.Bullet.Kind);
        Assert.Equal("l", f.Alignment);
        Assert.Equal(new ShapeBounds(10, 20, 30, 40), box.ResolveBounds());
        Assert.Equal("none", box.ResolveBodyFormat().Wrap);
    }

    [Fact]
    public void NotesPlaceholdersInheritFromTheNotesMaster()
    {
        using Presentation deck = Presentation.Create();
        Decks.AddNotesMaster(deck);
        Slide slide = ContentSlide(deck);
        NotesSlide notes = slide.SetNotesText("speak slowly\nsecond line");
        Assert.Equal("speak slowly\nsecond line", notes.NotesText);
        Assert.Equal("speak slowly\nsecond line", slide.Notes!.NotesText);
        Assert.Same(deck.NotesMaster, notes.NotesMaster);
        Assert.Same(slide, notes.Slide);
        Shape body = notes.Body!.Shape!;
        Assert.Equal(new ShapeBounds(685800, 4400550, 5486400, 3600450), body.ResolveBounds());
        Assert.Equal(1200, notes.Body.Paragraphs[0].ResolveFormat().Run.Size);
    }

    [Fact]
    public void ASlideWithoutALayoutReportsAndInheritsNothing()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = ContentSlide(deck);
        var rels = deck.Package.GetRelationships(slide.PartName).Where(r => !r.Type.EndsWith("/slideLayout", StringComparison.Ordinal)).ToList();
        deck.Package.SetRelationships(slide.PartName, rels);
        Assert.Null(slide.Layout);
        Assert.Empty(slide.Shapes[0].InheritanceChain);
        Assert.Null(slide.Shapes[0].ResolveBounds());
        Assert.Contains(deck.Defects, d => d.Code == "pptx.layout-missing" && d.Location.Container == slide.PartName);
    }
}
