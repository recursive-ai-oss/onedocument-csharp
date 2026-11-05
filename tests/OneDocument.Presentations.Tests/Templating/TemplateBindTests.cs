using RecursiveAI.OneDocument.Presentations.Templating;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class TemplateBindTests
{
    [Fact]
    public void ATagSplitAcrossRunsBindsAndKeepsTheFormattingWhereItBegan()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "Title", Tpl.P(Tpl.B("Hello {{ na"), Tpl.R("me }}, welcome"))));
        using Presentation r = Tpl.Render(deck, Tpl.Data(("name", "Ada")));
        Assert.Equal("Hello Ada, welcome", r.Slides[0].Text);
        List<TextRun> runs = r.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs.ToList();
        Assert.Equal(2, runs.Count);
        Assert.Equal("Hello Ada", runs[0].Text);
        Assert.Equal("1", runs[0].Properties.Node.GetAttribute("b"));
        Assert.Equal(", welcome", runs[1].Text);
        Assert.Null(runs[1].Properties.Node.GetAttribute("b"));
    }

    [Fact]
    public void ParagraphsWithoutTagsAreCopiedByteForByte()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "A", Tpl.Text("plain"), Tpl.P(Tpl.R("x {{ v }}"))));
        string before = deck.Slides[0].ShapeTreeMarkup.Node.OuterXml;
        using Presentation r = Tpl.Render(deck, Tpl.Data(("v", 1)));
        string after = r.Slides[0].ShapeTreeMarkup.Node.OuterXml;
        Assert.Contains(Tpl.Text("plain"), after, StringComparison.Ordinal);
        Assert.Contains("x 1", after, StringComparison.Ordinal);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ALoopOverParagraphsInsideOneShapeRepeatsThem()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "List",
            Tpl.Text("{% for i in items %}"), Tpl.Text("- {{ i.name }}: {{ i.qty }}"), Tpl.Text("{% endfor %}"), Tpl.Text("done")));
        var items = new List<object?> { Tpl.Data(("name", "a"), ("qty", 1)), Tpl.Data(("name", "b"), ("qty", 2)) };
        using Presentation r = Tpl.Render(deck, Tpl.Data(("items", items)));
        Assert.Equal("- a: 1\n- b: 2\ndone", r.Slides[0].Text);
    }

    [Fact]
    public void ASlideLoopClonesTheSlideWithItsNotes()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "T", Tpl.Text("{% for c in customers scope=slide %}{{ c.name }}{% endfor %}")));
        var customers = new List<object?> { Tpl.Data(("name", "Ann")), Tpl.Data(("name", "Bob")), Tpl.Data(("name", "Cy")) };
        PresentationTemplate.Render(deck, Tpl.Data(("customers", customers)));
        using Presentation r = Tpl.Reopen(deck);
        Assert.Equal(["Ann", "Bob", "Cy"], Tpl.Texts(r));
    }
}

public class TemplateScopeTests
{
    private static List<object?> Items(params (string Name, int Qty)[] items) =>
        [.. items.Select(i => (object?)Tpl.Data(("name", i.Name), ("qty", i.Qty)))];

    [Fact]
    public void TagsInTwoCellsOfARowRepeatTheRow()
    {
        using Presentation deck = Tpl.Deck(Tpl.Table(2, "T",
            [Tpl.Text("Item"), Tpl.Text("Qty")],
            [Tpl.Text("{% for i in items %}{{ i.name }}"), Tpl.Text("{{ i.qty }}{% endfor %}")],
            [Tpl.Text("total"), Tpl.Text("{{ SUM(items.qty) }}")]));
        using Presentation r = Tpl.Render(deck, Tpl.Data(("items", Items(("a", 1), ("b", 2), ("c", 3)))));
        Table t = r.Slides[0].Shapes[0].Table!;
        Assert.Equal(5, t.RowCount);
        Assert.Equal(["Item", "Qty"], t.Rows[0].Cells.Select(c => c.Text).ToArray());
        Assert.Equal(["b", "2"], t.Rows[2].Cells.Select(c => c.Text).ToArray());
        Assert.Equal(["total", "6"], t.Rows[4].Cells.Select(c => c.Text).ToArray());
    }

    [Fact]
    public void ARowRepeatGivesEachCopyItsOwnRowId()
    {
        using Presentation deck = Tpl.Deck(Tpl.Table(2, "T", [Tpl.Text("{% for i in items %}{{ i.name }}"), Tpl.Text("{{ i.qty }}{% endfor %}")]));
        using Presentation r = Tpl.Render(deck, Tpl.Data(("items", Items(("a", 1), ("b", 2), ("c", 3)))));
        string xml = r.Slides[0].ShapeTreeMarkup.Node.OuterXml;
        var ids = new List<string>();
        int at = 0;
        while ((at = xml.IndexOf("rowId", at, StringComparison.Ordinal)) >= 0)
        {
            int v = xml.IndexOf("val=\"", at, StringComparison.Ordinal) + 5;
            ids.Add(xml[v..xml.IndexOf('"', v)]);
            at = v;
        }

        Assert.Equal(3, ids.Count);
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public void AShapeWhoseOnlyTextIsAFalseBlockIsRemoved()
    {
        using Presentation deck = Tpl.Deck(
            Tpl.Sp(2, "Keep", Tpl.Text("always")),
            Tpl.Sp(3, "Warn", Tpl.Text("{% if overdue %}"), Tpl.Text("Payment overdue"), Tpl.Text("{% endif %}")),
            Tpl.Sp(4, "Inline", Tpl.Text("{% if overdue %}Late{% endif %}")));
        using Presentation no = Tpl.Render(deck, Tpl.Data(("overdue", false)));
        Assert.Equal(["always"], no.Slides[0].Shapes.Select(s => s.TextBody!.Text).ToArray());
    }

    [Fact]
    public void AShapeWithATrueBlockIsKept()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(3, "Warn", Tpl.Text("{% if overdue %}"), Tpl.Text("Payment overdue"), Tpl.Text("{% endif %}")));
        using Presentation yes = Tpl.Render(deck, Tpl.Data(("overdue", true)));
        Assert.Equal("Payment overdue", yes.Slides[0].Text);
    }

    [Fact]
    public void TagsInTwoShapesRepeatTheShapesWithNewIds()
    {
        using Presentation deck = Tpl.Deck(
            Tpl.Sp(2, "Open", Tpl.Text("{% for i in items %}")),
            Tpl.Sp(3, "Card", Tpl.Text("{{ i.name }}")),
            Tpl.Sp(4, "Close", Tpl.Text("{% endfor %}")));
        using Presentation r = Tpl.Render(deck, Tpl.Data(("items", Items(("a", 1), ("b", 2)))));
        Assert.Equal(["a", "b"], r.Slides[0].Shapes.Select(s => s.TextBody!.Text).Where(t => t.Length > 0).ToArray());
        Assert.Equal(r.Slides[0].Shapes.Count, r.Slides[0].Shapes.Select(s => s.Markup.Node.GetAttribute("x") ?? s.Id.ToString()).Distinct().Count());
    }

    [Fact]
    public void SlidesBetweenTheTagsRepeatAsBlock()
    {
        using Presentation deck = Tpl.Deck(
            [Tpl.Sp(2, "S1", Tpl.Text("{% for i in items %}intro {{ i.name }}"))],
            [Tpl.Sp(2, "S2", Tpl.Text("detail {{ i.name }}{% endfor %}"))],
            [Tpl.Sp(2, "S3", Tpl.Text("the end"))]);
        using Presentation r = Tpl.Render(deck, Tpl.Data(("items", Items(("a", 1), ("b", 2)))));
        Assert.Equal(["intro a", "detail a", "intro b", "detail b", "the end"], Tpl.Texts(r));
    }

    [Fact]
    public void AFalseSlideBlockDropsTheSlide()
    {
        using Presentation deck = Tpl.Deck(
            [Tpl.Sp(2, "S1", Tpl.Text("first"))],
            [Tpl.Sp(2, "S2", Tpl.Text("{% if extra scope=slide %}extra{% endif %}"))],
            [Tpl.Sp(2, "S3", Tpl.Text("last"))]);
        using Presentation no = Tpl.Render(deck, Tpl.Data(("extra", false)));
        Assert.Equal(["first", "last"], Tpl.Texts(no));
    }

    [Fact]
    public void NotesAreBoundAndEachCloneKeepsItsOwnNotes()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "T", Tpl.Text("{% for c in items scope=slide %}{{ c.name }}{% endfor %}")));
        Decks.AddNotesMaster(deck);
        deck.Slides[0].SetNotesText("note for {{ c.name }}");
        PresentationTemplate.Render(deck, Tpl.Data(("items", Items(("a", 1), ("b", 2)))));
        using Presentation r = Tpl.Reopen(deck);
        Assert.Equal(2, r.Slides.Count);
        Assert.Contains("note for a", r.Slides[0].Notes!.Text, StringComparison.Ordinal);
        Assert.Contains("note for b", r.Slides[1].Notes!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnclosedBlockIsReportedWithItsPlaceBeforeAnythingChanges()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "Bad", Tpl.Text("{% for x in y %}")));
        var defects = PresentationTemplate.Validate(deck);
        Assert.Contains(defects, d => d.Code == "template.unclosed-block" && d.Location.ToString().Contains("slide1.xml", StringComparison.Ordinal));
        Assert.Throws<TemplateException>(() => PresentationTemplate.Render(deck, Tpl.Data()));
    }
}

public class TemplateObjectTests
{
    [Fact]
    public void AnImageTagReplacesTheShapeKeepingItsGeometryAndFitsTheBox()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(5, "Logo", Tpl.Text("{% image photo %}")));
        // The shape box is 4572000 x 457200 EMU: a square image fits at the height, centred.
        byte[] png = TestImages.Png(20, 20, 200, 10, 10);
        PresentationTemplate.Render(deck, Tpl.Data(("photo", png)));
        using Presentation r = Tpl.Reopen(deck);
        Shape pic = r.Slides[0].Shapes.Single();
        Assert.Equal(ShapeKind.Picture, pic.Kind);
        Assert.Equal(new ShapeBounds(914400 + ((4572000 - 457200) / 2), 500000, 457200, 457200), pic.Bounds);
        Assert.Contains(r.Package.GetRelationships(r.Slides[0].PartName), x => x.Type.EndsWith("/image", StringComparison.Ordinal));
        Assert.NotNull(r.Package.FindPart("/ppt/media/image1.png"));
    }

    [Fact]
    public void ImageCopiesInALoopShareOneMediaPartAndGetOwnIds()
    {
        using Presentation deck = Tpl.Deck(
            Tpl.Sp(2, "Open", Tpl.Text("{% for i in items %}")),
            Tpl.Sp(3, "Img", Tpl.Text("{% image i.pic fit=stretch %}")),
            Tpl.Sp(4, "Close", Tpl.Text("{% endfor %}")));
        byte[] png = TestImages.Png(8, 4, 1, 2, 3);
        var items = new List<object?> { Tpl.Data(("pic", png)), Tpl.Data(("pic", png)) };
        PresentationTemplate.Render(deck, Tpl.Data(("items", items)));
        using Presentation r = Tpl.Reopen(deck);
        Assert.Equal(2, r.Slides[0].Shapes.Count(s => s.Kind == ShapeKind.Picture));
        Assert.Single(r.Package.GetRelationships(r.Slides[0].PartName).Select(x => x.Target).Where(t => t.Contains("media", StringComparison.Ordinal)).Distinct());
    }

    [Fact]
    public void AnImageTagInsideOtherTextIsReportedAndLeftOut()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(5, "Mixed", Tpl.Text("Logo: {% image photo %}")));
        PresentationTemplateResult res = PresentationTemplate.Render(deck, Tpl.Data(("photo", TestImages.Png(2, 2, 0, 0, 0))));
        Assert.Contains(res.Defects, d => d.Code == "pptx.template-image-position");
        Assert.Equal("Logo: ", deck.Slides[0].Text);
    }

    [Fact]
    public void AnInvalidImageValueIsReported()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(5, "Logo", Tpl.Text("{% image photo %}")));
        PresentationTemplateResult res = PresentationTemplate.Render(deck, Tpl.Data(("photo", new byte[] { 1, 2, 3 })));
        Assert.Contains(res.Defects, d => d.Code == "pptx.template-image-invalid");
    }

    [Fact]
    public void AHyperlinkTargetWithATagBindsPerCopy()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "Link", Tpl.Text("{% for i in items %}"),
            "<a:p><a:r><a:rPr lang=\"en-US\"><a:hlinkClick r:id=\"rId77\"/></a:rPr><a:t>{{ i.name }}</a:t></a:r></a:p>", Tpl.Text("{% endfor %}")));
        string part = deck.Slides[0].PartName;
        var rels = new List<RecursiveAI.OneDocument.Packaging.Relationship>(deck.Package.GetRelationships(part))
        {
            new("rId77", Decks.Rel + "hyperlink", "https://example.com/u/%7B%7B%20i.name%20%7D%7D", RecursiveAI.OneDocument.Packaging.RelationshipTargetMode.External),
        };
        deck.Package.SetRelationships(part, rels);
        var items = new List<object?> { Tpl.Data(("name", "ann")), Tpl.Data(("name", "bob")) };
        PresentationTemplate.Render(deck, Tpl.Data(("items", items)));
        using Presentation r = Tpl.Reopen(deck);
        string[] targets = r.Package.GetRelationships(r.Slides[0].PartName).Where(x => x.TargetMode == RecursiveAI.OneDocument.Packaging.RelationshipTargetMode.External).Select(x => x.Target).ToArray();
        Assert.Contains("ann", string.Concat(targets), StringComparison.Ordinal);
        Assert.Contains("bob", string.Concat(targets), StringComparison.Ordinal);
    }
}

public class TemplateDefectTests
{
    [Fact]
    public void ASpreadsheetScopeIsReportedAndTheStructureDecides()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "A", Tpl.Text("{% for i in items scope=sheet %}"), Tpl.Text("{{ i }}"), Tpl.Text("{% endfor %}")));
        PresentationTemplateResult res = PresentationTemplate.Render(deck, Tpl.Data(("items", new List<object?> { 1, 2 })));
        Assert.Contains(res.Defects, d => d.Code == "pptx.template-scope-unsupported");
        Assert.Equal("1\n2", deck.Slides[0].Text);
    }

    [Fact]
    public void TagsOnDifferentSlidesWithInlineScopeLeaveOrphans()
    {
        using Presentation deck = Tpl.Deck(
            [Tpl.Sp(2, "A", Tpl.Text("{% for i in items scope=inline %}"), Tpl.Text("a{{ i }}"))],
            [Tpl.Sp(2, "B", Tpl.Text("b{{ i }}"), Tpl.Text("{% endfor %}"))]);
        PresentationTemplateResult res = PresentationTemplate.Render(deck, Tpl.Data(("items", new List<object?> { 1, 2 })));
        Assert.Contains(res.Defects, d => d.Code == "pptx.template-orphan-unit");
    }

    [Fact]
    public void HtmlAndMarkdownTagsInsertPlainText()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "A", Tpl.Text("{% markdown md %} and more")));
        PresentationTemplateResult res = PresentationTemplate.Render(deck, Tpl.Data(("md", "**bold**")));
        Assert.Contains(res.Defects, d => d.Code == "pptx.template-injection-unsupported");
        Assert.Equal("**bold** and more", deck.Slides[0].Text);
    }

    [Fact]
    public void StrictModeThrowsOnMissingData()
    {
        using Presentation deck = Tpl.Deck(Tpl.Sp(2, "A", Tpl.Text("{{ nothing }}")));
        Assert.Throws<TemplateException>(() => PresentationTemplate.Render(deck, Tpl.Data(), new TemplateOptions { Strict = true }));
    }

    [Fact]
    public void ADeckWithoutTagsComesOutByteIdentical()
    {
        using Presentation deck = Tpl.Deck([Tpl.Sp(2, "A", Tpl.Text("plain"), Tpl.P(Tpl.B("bold"), Tpl.R(" and more")))], [Tpl.Table(3, "T", [Tpl.Text("x"), Tpl.Text("y")])]);
        byte[] before = Decks.Save(deck);
        using Presentation again = Decks.Reopen(before);
        PresentationTemplate.Render(again, Tpl.Data());
        byte[] after = Decks.Save(again);
        Assert.Equal(Decks.Entries(before).Keys, Decks.Entries(after).Keys);
        Assert.Empty(Decks.Changed(before, after));
    }
}
