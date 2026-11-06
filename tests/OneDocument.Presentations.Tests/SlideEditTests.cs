using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class SlideEditTests
{
    private const string P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";

    private static string[] Titles(Presentation deck) => deck.Slides.Select(s => s.Title ?? "-").ToArray();

    private static Presentation DeckWith(params string[] titles)
    {
        Presentation deck = Presentation.Create();
        foreach (string t in titles)
        {
            deck.AddSlide(Decks.Layout(deck, "Title Only" == t ? "Blank" : "Title and Content")).Shapes[0].TextBody!.SetText(t);
        }

        return deck;
    }

    // p14:sectionLst: names with the slide titles each holds.
    private static void AddSections(Presentation deck, params (string Name, int[] Slides)[] sections)
    {
        IReadOnlyList<Slide> slides = deck.Slides;
        var sb = new System.Text.StringBuilder();
        sb.Append("<p:extLst xmlns:p14=\"" + P14 + "\"><p:ext uri=\"{521415D9-36F7-43E2-AB2F-B90AF26B5E84}\"><p14:sectionLst>");
        int g = 0;
        foreach ((string name, int[] idx) in sections)
        {
            sb.Append("<p14:section name=\"" + name + "\" id=\"{00000000-0000-0000-0000-00000000000" + g++ + "}\"><p14:sldIdLst>");
            foreach (int i in idx)
            {
                sb.Append("<p14:sldId id=\"" + slides[i].Id + "\"/>");
            }

            sb.Append("</p14:sldIdLst></p14:section>");
        }

        sb.Append("</p14:sectionLst></p:ext></p:extLst>");
        deck.Markup.Node.AppendXml(Decks.Utf8(sb.ToString()));
    }

    private static string[][] SectionTitles(Presentation deck) =>
        deck.Sections.Select(s => s.Slides.Select(x => x.Title!).ToArray()).ToArray();

    [Fact]
    public void AddSlideBuildsTheSlideFromItsLayoutAndKeepsEverythingConsistent()
    {
        using Presentation deck = Presentation.Create();
        Slide a = deck.AddSlide(Decks.Layout(deck, "Title and Content"));
        Slide b = deck.AddSlide(Decks.Layout(deck, "Title Slide"), index: 0);
        Assert.Equal([b.PartName, a.PartName], deck.Slides.Select(s => s.PartName).ToArray());
        Assert.Equal("/ppt/slides/slide1.xml", a.PartName);
        Assert.Equal("/ppt/slides/slide2.xml", b.PartName);
        Assert.Equal([256L, 257L], deck.Slides.Select(s => s.Id!.Value).OrderBy(x => x).ToArray());
        Assert.Equal(0, b.Index);

        // Placeholders of the layout, without date/footer/number; idx and type copied; text bodies empty.
        Assert.Equal(["title", "obj"], a.Shapes.Select(s => s.Placeholder!.Value.Type).ToArray());
        Assert.Equal([0, 1], a.Shapes.Select(s => s.Placeholder!.Value.Index).ToArray());
        Assert.Equal(["ctrTitle", "subTitle"], b.Shapes.Select(s => s.Placeholder!.Value.Type).ToArray());
        Assert.Same(Decks.Layout(deck, "Title and Content"), a.Layout);
        Assert.Equal(string.Empty, a.Text.Trim());

        byte[] saved = Decks.Save(deck);
        Assert.Contains("presentationml.slide+xml", Decks.Xml(saved, "[Content_Types].xml"));
        using Presentation again = Decks.Reopen(saved);
        Assert.Equal(2, again.Slides.Count);
        Assert.Empty(again.Defects);
        Assert.Throws<ArgumentException>(() => deck.AddSlide(again.Masters[0].Layouts[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => deck.AddSlide(Decks.Layout(deck, "Blank"), 3));
    }

    [Fact]
    public void AddingASlideChangesOnlyThePresentationItsRelationshipsContentTypesAndTheNewParts()
    {
        byte[] before;
        using (Presentation deck = Presentation.Create())
        {
            deck.AddSlide(Decks.Layout(deck, "Blank"));
            before = Decks.Save(deck);
        }

        using Presentation edit = Decks.Reopen(before);
        edit.AddSlide(Decks.Layout(edit, "Title and Content"));
        Assert.Equal(
            ["[Content_Types].xml", "ppt/_rels/presentation.xml.rels", "ppt/presentation.xml", "ppt/slides/_rels/slide2.xml.rels", "ppt/slides/slide2.xml"],
            Decks.Changed(before, Decks.Save(edit)).ToArray());
    }

    [Fact]
    public void DuplicateCopiesOwnedPartsSharesPicturesAndKeepsIdsConsistent()
    {
        using Presentation deck = DeckWith("one", "two");
        Decks.AddNotesMaster(deck);
        Slide one = deck.Slides[0];
        string image = Decks.Attach(deck, one.PartName, "image", "/ppt/media/image1.png", "image/png", new byte[] { 9, 9, 9 });
        Decks.AddShapeXml(one, Decks.Picture(20, image));
        string chart = Decks.Attach(deck, one.PartName, "chart", "/ppt/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", Decks.Utf8("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/>"));
        Decks.Attach(deck, "/ppt/charts/chart1.xml", "package", "/ppt/embeddings/Microsoft_Excel_Sheet1.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", new byte[] { 7, 7 });
        Decks.AddShapeXml(one, "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"21\" name=\"Chart\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1\" cy=\"1\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"" + chart + "\"/></a:graphicData></a:graphic></p:graphicFrame>");
        one.SetNotesText("note for one");

        Slide copy = deck.DuplicateSlide(one);
        Assert.Equal(["one", "one", "two"], Titles(deck));
        Assert.Equal(1, copy.Index);
        Assert.NotEqual(one.Id, copy.Id);
        Assert.Same(one.Layout, copy.Layout);

        // The picture part is shared; the chart and its workbook are copied; the notes page is copied and points at the copy.
        string[] targets(Slide s) => deck.Package.GetRelationships(s.PartName).Select(r => PartUri.TryResolve(new PartUri(s.PartName), r.Target, out PartUri u) ? u.Value : r.Target).ToArray();
        Assert.Contains("/ppt/media/image1.png", targets(copy));
        Assert.Contains("/ppt/charts/chart2.xml", targets(copy));
        Assert.DoesNotContain("/ppt/charts/chart1.xml", targets(copy));
        Assert.Contains("/ppt/embeddings/Microsoft_Excel_Sheet2.xlsx", deck.Package.GetRelationships("/ppt/charts/chart2.xml").Select(r => "/ppt/embeddings/" + r.Target.Split('/')[^1]));
        Assert.NotNull(copy.Notes);
        Assert.NotEqual(one.Notes!.PartName, copy.Notes!.PartName);
        Assert.Equal("note for one", copy.Notes.NotesText);
        Assert.Same(copy, copy.Notes.Slide);
        Assert.Same(one, one.Notes.Slide);
        Assert.Equal("/ppt/media/image1.png", copy.Shapes.First(s => s.Kind == ShapeKind.Picture).ImagePartName);

        // Editing the copy leaves the original alone.
        copy.Shapes[0].TextBody!.SetText("copy");
        copy.Notes.Body!.SetText("note for copy");
        Assert.Equal("one", one.Title);
        Assert.Equal("note for one", one.Notes.NotesText);

        byte[] saved = Decks.Save(deck);
        using Presentation again = Decks.Reopen(saved);
        Assert.Equal(["one", "copy", "two"], Titles(again));
        Assert.Empty(again.Defects);
        Assert.Equal(1, Decks.Entries(saved).Keys.Count(k => k == "ppt/media/image1.png"));
    }

    [Fact]
    public void DuplicateAtAnIndexAndOfALastSlide()
    {
        using Presentation deck = DeckWith("a", "b", "c");
        deck.DuplicateSlide(deck.Slides[2]);
        Assert.Equal(["a", "b", "c", "c"], Titles(deck));
        deck.DuplicateSlide(deck.Slides[0], 0);
        Assert.Equal(["a", "a", "b", "c", "c"], Titles(deck));
        Assert.Equal(5, deck.Slides.Select(s => s.Id).Distinct().Count());
        Assert.Equal(5, deck.Slides.Select(s => s.PartName).Distinct().Count());
    }

    [Fact]
    public void PartViewsReachTheGeneratedSlideAndLayoutTypes()
    {
        using Presentation deck = DeckWith("a");
        var slide = deck.Slides[0].View;
        Assert.NotNull(slide);
        var view = slide.Value;
        view.ShowMasterSp = false;
        Assert.False(deck.Slides[0].ShowMasterShapes);
        Assert.NotNull(deck.Slides[0].Layout?.View);
        Assert.NotNull(deck.Slides[0].Master?.View);
    }

    [Fact]
    public void DuplicateGivesTheCopyItsOwnCreationId()
    {
        using Presentation deck = DeckWith("a");
        deck.Slides[0].Markup.Node.AppendXml(Decks.Utf8("<p:extLst><p:ext uri=\"{BB962C8B-B14F-4D97-AF65-F5344CB8AC3E}\"><p14:creationId xmlns:p14=\"" + P14 + "\" val=\"42\"/></p:ext></p:extLst>"));
        Slide copy = deck.DuplicateSlide(deck.Slides[0]);
        Assert.Contains("val=\"42\"", deck.Slides[0].Markup.Node.OuterXml);
        Assert.DoesNotContain("val=\"42\"", copy.Markup.Node.OuterXml);
        Assert.Contains("creationId", copy.Markup.Node.OuterXml);
    }

    [Fact]
    public void DeleteRemovesTheSlideAndWhatOnlyItOwnedButKeepsSharedParts()
    {
        using Presentation deck = DeckWith("one", "two", "three");
        Decks.AddNotesMaster(deck);
        Slide one = deck.Slides[0];
        Slide two = deck.Slides[1];
        string shared1 = Decks.Attach(deck, one.PartName, "image", "/ppt/media/shared.png", "image/png", new byte[] { 1 });
        Decks.Attach(deck, two.PartName, "image", "/ppt/media/shared.png", "image/png", new byte[] { 1 });
        Decks.Attach(deck, one.PartName, "image", "/ppt/media/only-one.png", "image/png", new byte[] { 2 });
        Decks.AddShapeXml(one, Decks.Picture(30, shared1));
        one.SetNotesText("n1");
        string notes = one.Notes!.PartName;
        _ = deck.Slides[2].Id;

        deck.DeleteSlide(one);
        Assert.Equal(["two", "three"], Titles(deck));
        Assert.Null(deck.Package.FindPart("/ppt/slides/slide1.xml"));
        Assert.Null(deck.Package.FindPart(notes));
        Assert.Null(deck.Package.FindPart("/ppt/media/only-one.png"));
        Assert.NotNull(deck.Package.FindPart("/ppt/media/shared.png"));
        Assert.NotNull(Decks.Layout(deck, "Title and Content"));
        Assert.DoesNotContain(deck.Package.GetRelationships(deck.MainPartName), r => r.Target.EndsWith("slide1.xml", StringComparison.Ordinal));

        deck.DeleteSlide(two);
        Assert.Null(deck.Package.FindPart("/ppt/media/shared.png"));
        byte[] saved = Decks.Save(deck);
        using Presentation again = Decks.Reopen(saved);
        Assert.Equal(["three"], Titles(again));
        Assert.Empty(again.Defects);
        Assert.Throws<ArgumentException>(() => deck.DeleteSlide(one));

        // A new slide reuses the freed part name and still reads correctly (no stale cached tree).
        Slide fresh = deck.AddSlide(Decks.Layout(deck, "Blank"));
        Assert.Empty(fresh.Shapes);
        Assert.Equal("/ppt/slides/slide1.xml", fresh.PartName);
    }

    [Fact]
    public void MoveReordersTheListOnly()
    {
        byte[] before;
        using (Presentation deck = DeckWith("a", "b", "c", "d"))
        {
            before = Decks.Save(deck);
        }

        using Presentation edit = Decks.Reopen(before);
        long idA = edit.Slides[0].Id!.Value;
        edit.MoveSlide(edit.Slides[0], 2);
        Assert.Equal(["b", "c", "a", "d"], Titles(edit));
        edit.MoveSlide(edit.Slides[3], 0);
        Assert.Equal(["d", "b", "c", "a"], Titles(edit));
        edit.MoveSlide(edit.Slides[0], 3);
        Assert.Equal(["b", "c", "a", "d"], Titles(edit));
        Assert.Equal(idA, edit.Slides[2].Id);
        Assert.Equal(["ppt/presentation.xml"], Decks.Changed(before, Decks.Save(edit)));
        Assert.Throws<ArgumentOutOfRangeException>(() => edit.MoveSlide(edit.Slides[0], 4));
    }

    [Fact]
    public void SectionsFollowAddDuplicateMoveAndDelete()
    {
        using Presentation deck = DeckWith("a", "b", "c", "d");
        AddSections(deck, ("One", [0, 1]), ("Two", [2, 3]));
        Assert.Equal([["a", "b"], ["c", "d"]], SectionTitles(deck));

        deck.DuplicateSlide(deck.Slides[1]); // b copy follows b, in section One
        Assert.Equal([["a", "b", "b"], ["c", "d"]], SectionTitles(deck));

        Slide added = deck.AddSlide(Decks.Layout(deck, "Blank"), 3); // after the copy of b, before c
        Assert.Equal(4, deck.Sections[0].SlideIds.Count);

        deck.MoveSlide(deck.Slides[0], 5); // a to the end: joins section Two
        Assert.Equal(1, deck.Sections.Count(s => s.SlideIds.Contains(deck.Slides[5].Id!.Value)));
        Assert.Equal(deck.Slides.Select(s => s.Id!.Value).ToArray(), deck.Sections.SelectMany(s => s.SlideIds).ToArray());

        deck.MoveSlide(deck.Slides[5], 0); // back to the front: first section
        Assert.Equal(deck.Slides.Select(s => s.Id!.Value).ToArray(), deck.Sections.SelectMany(s => s.SlideIds).ToArray());
        Assert.Equal("a", deck.Sections[0].Slides[0].Title);

        long gone = deck.Slides[2].Id!.Value;
        deck.DeleteSlide(deck.Slides[2]);
        Assert.DoesNotContain(gone, deck.Sections.SelectMany(s => s.SlideIds));
        Assert.Equal(deck.Slides.Select(s => s.Id!.Value).ToArray(), deck.Sections.SelectMany(s => s.SlideIds).ToArray());
    }

    [Fact]
    public void CustomShowsDropADeletedSlide()
    {
        using Presentation deck = DeckWith("a", "b", "c");
        string ra = deck.Slides[0].RelationshipId!;
        string rb = deck.Slides[1].RelationshipId!;
        // p:custShowLst sits after p:sldIdLst... and before p:sldSz? No: after p:notesSz, before p:photoAlbum / kinsoku / defaultTextStyle.
        deck.Markup.FindChild(SchemaNamespace.Presentation, "notesSz"u8)!.Value.Node.InsertXmlAfter(Decks.Utf8(
            "<p:custShowLst><p:custShow name=\"Short\" id=\"0\"><p:sldLst><p:sld r:id=\"" + ra + "\"/><p:sld r:id=\"" + rb + "\"/></p:sldLst></p:custShow></p:custShowLst>"));
        CustomShow show = Assert.Single(deck.CustomShows);
        Assert.Equal("Short", show.Name);
        Assert.Equal(["a", "b"], show.Slides.Select(s => s.Title!).ToArray());
        deck.DeleteSlide(deck.Slides[0]);
        Assert.Equal(["b"], Assert.Single(deck.CustomShows).Slides.Select(s => s.Title!).ToArray());
    }

    [Fact]
    public void CommentsAuthorsAndNotesAreReadAndCopiedWithTheSlide()
    {
        using Presentation deck = DeckWith("a");
        Slide slide = deck.Slides[0];
        Decks.Attach(deck, deck.MainPartName, "commentAuthors", "/ppt/commentAuthors.xml", Decks.Pml + "commentAuthors+xml",
            Decks.Utf8("<p:cmAuthorLst xmlns:p=\"" + Decks.P + "\"><p:cmAuthor id=\"0\" name=\"Ada Lovelace\" initials=\"AL\" lastIdx=\"1\" clrIdx=\"0\"/></p:cmAuthorLst>"));
        Decks.Attach(deck, slide.PartName, "comments", "/ppt/comments/comment1.xml", Decks.Pml + "comments+xml",
            Decks.Utf8("<p:cmLst xmlns:p=\"" + Decks.P + "\"><p:cm authorId=\"0\" dt=\"2024-05-01T10:00:00.000\" idx=\"1\"><p:pos x=\"10\" y=\"20\"/><p:text>Check the numbers</p:text></p:cm></p:cmLst>"));
        SlideComment c = Assert.Single(slide.Comments);
        Assert.Equal(CommentKind.Legacy, c.Kind);
        Assert.Equal("Ada Lovelace", c.Author);
        Assert.Equal("AL", c.Initials);
        Assert.Equal("Check the numbers", c.Text);
        Assert.Equal("2024-05-01T10:00:00.000", c.Created);
        Assert.Equal("Ada Lovelace", Assert.Single(deck.CommentAuthors).Name);

        // A modern thread: author list and comment part use the 2018 namespaces.
        const string P188 = "http://schemas.microsoft.com/office/powerpoint/2018/8/main";
        Decks.Attach(deck, deck.MainPartName, "authors", "/ppt/authors.xml", "application/vnd.ms-powerpoint.authors+xml",
            Decks.Utf8("<p188:authorLst xmlns:p188=\"" + P188 + "\"><p188:author id=\"{11111111-1111-1111-1111-111111111111}\" name=\"Grace Hopper\" initials=\"GH\" userId=\"S::g\" providerId=\"AD\"/></p188:authorLst>"));
        Decks.Attach(deck, slide.PartName, "comments", "/ppt/comments/modernComment_100_1.xml", "application/vnd.ms-powerpoint.comments+xml",
            Decks.Utf8("<p188:cmLst xmlns:p188=\"" + P188 + "\" xmlns:a=\"" + Decks.A + "\"><p188:cm id=\"{22222222-2222-2222-2222-222222222222}\" authorId=\"{11111111-1111-1111-1111-111111111111}\" created=\"2024-06-01T00:00:00.000\" status=\"resolved\">" +
                "<p188:replyLst><p188:reply id=\"{33333333-3333-3333-3333-333333333333}\" authorId=\"{11111111-1111-1111-1111-111111111111}\" created=\"2024-06-02T00:00:00.000\"><p188:txBody><a:bodyPr/><a:p><a:r><a:t>Done</a:t></a:r></a:p></p188:txBody></p188:reply></p188:replyLst>" +
                "<p188:txBody><a:bodyPr/><a:p><a:r><a:t>Please verify</a:t></a:r></a:p></p188:txBody></p188:cm></p188:cmLst>"));
        Assert.Equal(2, slide.Comments.Count);
        SlideComment modern = slide.Comments.First(x => x.Kind == CommentKind.Modern);
        Assert.Equal("Please verify", modern.Text);
        Assert.Equal("Grace Hopper", modern.Author);
        Assert.Equal("resolved", modern.Status);
        Assert.Equal("Done", Assert.Single(modern.Replies).Text);

        Slide copy = deck.DuplicateSlide(slide);
        Assert.Equal(2, copy.Comments.Count);
        Assert.NotEqual(
            slide.RelatedPartNames("comments").Order().ToArray(),
            copy.RelatedPartNames("comments").Order().ToArray());
        deck.DeleteSlide(copy);
        Assert.Equal(2, slide.Comments.Count);
        Assert.Equal(2, deck.Package.Parts.Count(p => p.Name.StartsWith("/ppt/comments/", StringComparison.Ordinal)));
    }
}

public class TableTests
{
    private static readonly string TableXml =
        "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"40\" name=\"Table 40\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"3000\" cy=\"2000\"/></p:xfrm>" +
        "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr firstRow=\"1\"><a:tableStyleId>{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}</a:tableStyleId></a:tblPr>" +
        "<a:tblGrid><a:gridCol w=\"1000\"/><a:gridCol w=\"1000\"/><a:gridCol w=\"1000\"/></a:tblGrid>" +
        "<a:tr h=\"500\">" + Cell("head", "gridSpan=\"2\"") + Cell(string.Empty, "hMerge=\"1\"") + Cell("h3") + "</a:tr>" +
        "<a:tr h=\"500\">" + Cell("tall", "rowSpan=\"2\"") + Cell("b2") + Cell("b3") + "</a:tr>" +
        "<a:tr h=\"500\">" + Cell(string.Empty, "vMerge=\"1\"") + Cell("c2") + Cell("c3") + "</a:tr>" +
        "</a:tbl></a:graphicData></a:graphic></p:graphicFrame>";

    private static string Cell(string text, string attrs = "") =>
        "<a:tc " + attrs + "><a:txBody><a:bodyPr/><a:lstStyle/><a:p>" + (text.Length == 0 ? "<a:endParaRPr lang=\"en-US\"/>" : "<a:r><a:rPr lang=\"en-US\"/><a:t>" + text + "</a:t></a:r>") + "</a:p></a:txBody><a:tcPr/></a:tc>";

    [Fact]
    public void TablesAreALogicalGridWithMerges()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Blank"));
        Decks.AddShapeXml(slide, TableXml);
        Shape frame = slide.Shapes[0];
        Assert.Equal(GraphicFrameKind.Table, frame.FrameKind);
        Table table = frame.Table!;
        Assert.Equal(3, table.RowCount);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal([1000L, 1000L, 1000L], table.ColumnWidths.ToArray());
        Assert.True(table.HasHeaderRow);
        Assert.Equal("{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}", table.StyleId);

        TableCell head = table[0, 0];
        Assert.Equal(2, head.ColumnSpan);
        Assert.False(head.IsMergedContinuation);
        Assert.Equal((0, 0), table[0, 1].Origin);
        Assert.True(table[0, 1].IsMergedContinuation);
        Assert.Equal((1, 0), table[2, 0].Origin);
        Assert.Equal(2, table[1, 0].RowSpan);
        Assert.Equal((1, 0), table[1, 0].Origin);
        Assert.Equal((2, 1), table[2, 1].Origin);
        Assert.Equal("head", head.Text);
        Assert.Equal("head\nh3\ntall\nb2\nb3\nc2\nc3", frame.Text);
        Assert.Equal("head\nh3\ntall\nb2\nb3\nc2\nc3", slide.Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => table[3, 0]);
    }

    [Fact]
    public void EditingACellChangesOnlyThatCell()
    {
        byte[] before;
        using (Presentation deck = Presentation.Create())
        {
            Decks.AddShapeXml(deck.AddSlide(Decks.Layout(deck, "Blank")), TableXml);
            before = Decks.Save(deck);
        }

        using Presentation edit = Decks.Reopen(before);
        Table table = edit.Slides[0].Shapes[0].Table!;
        table[2, 2].SetText("changed <&>");
        table[0, 1].SetText("filled continuation"); // an empty continuation cell can take text without losing hMerge
        string xml = Decks.Xml(Decks.Save(edit), "ppt/slides/slide1.xml");
        Assert.Contains("<a:t>changed &lt;&amp;&gt;</a:t>", xml);
        Assert.Contains("hMerge=\"1\"", xml);
        Assert.Equal(["ppt/slides/slide1.xml"], Decks.Changed(before, Decks.Save(edit)));
        using Presentation again = Decks.Reopen(Decks.Save(edit));
        Assert.Equal("changed <&>", again.Slides[0].Shapes[0].Table![2, 2].Text);
        Assert.Equal("c2", again.Slides[0].Shapes[0].Table![2, 1].Text);
    }

    [Fact]
    public void ACellWithoutATextBodyGetsOne()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Blank"));
        Decks.AddShapeXml(slide, TableXml.Replace(Cell("b2"), "<a:tc><a:tcPr/></a:tc>", StringComparison.Ordinal));
        TableCell cell = slide.Shapes[0].Table![1, 1];
        Assert.Null(cell.TextBody);
        cell.SetText("new");
        Assert.Equal("new", cell.Text);
        Assert.StartsWith("<a:tc><a:txBody>", cell.Markup.Node.OuterXml);
    }
}

public class StrictAndMacroTests
{
    [Fact]
    public void StrictDeckReadsAndEditsInItsOwnNamespaces()
    {
        const string a = "http://purl.oclc.org/ooxml/drawingml/main";
        const string p = "http://purl.oclc.org/ooxml/presentationml/main";
        const string r = "http://purl.oclc.org/ooxml/officeDocument/relationships";
        var package = new InMemoryPackage();
        package.SetRelationships(null, [new Relationship("rId1", r + "/officeDocument", "ppt/presentation.xml")]);
        package.AddPart("/ppt/presentation.xml", Decks.Pml + "presentation.main+xml", Decks.Utf8(
            "<p:presentation xmlns:p=\"" + p + "\" xmlns:r=\"" + r + "\"><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId1\"/></p:sldIdLst></p:presentation>"));
        package.SetRelationships("/ppt/presentation.xml", [new Relationship("rId1", r + "/slide", "slides/slide1.xml")]);
        package.AddPart("/ppt/slides/slide1.xml", Decks.Pml + "slide+xml", Decks.Utf8(
            "<p:sld xmlns:p=\"" + p + "\" xmlns:a=\"" + a + "\"><p:cSld><p:spTree><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"T\"/><p:cNvSpPr/><p:nvPr><p:ph type=\"title\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:p><a:r><a:t>strict</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>"));
        using Presentation deck = Presentation.FromPackage(package);
        Assert.True(deck.IsStrict);
        Slide slide = Assert.Single(deck.Slides);
        Assert.Equal("strict", slide.Title);
        slide.Shapes[0].TextBody!.SetText("edited\nsecond");
        Assert.Equal("edited\nsecond", slide.Title);
        string xml = slide.Markup.Node.OuterXml;
        Assert.DoesNotContain("schemas.openxmlformats.org", xml);
        Assert.Null(slide.Layout); // no layout relationship: reported, not thrown
    }

    [Fact]
    public void MacroEnabledDecksKeepTheirFormatAndRefuseMacroFreeSaves()
    {
        using Presentation deck = Presentation.Create();
        deck.Package.AddPart("/ppt/vbaProject.bin", "application/vnd.ms-office.vbaProject", new byte[] { 1, 2, 3 });
        Assert.True(deck.HasVbaProject);
        Assert.Throws<OfficeUnsupportedException>(() => Decks.Save(deck, OfficeFormat.Pptx));
        byte[] pptm = Decks.Save(deck, OfficeFormat.Pptm);
        using Presentation again = Decks.Reopen(pptm);
        Assert.Equal(OfficeFormat.Pptm, again.Format);
        Assert.Equal(new byte[] { 1, 2, 3 }, Decks.Entries(pptm)["ppt/vbaProject.bin"]);
    }
}
