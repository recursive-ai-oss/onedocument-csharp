namespace RecursiveAI.OneDocument.Presentations.Tests;

public class TextEditTests
{
    private static (Presentation Deck, Slide Slide) Deck()
    {
        Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Title and Content"));
        return (deck, slide);
    }

    [Fact]
    public void SetTextKeepsRunFormattingParagraphPropertiesAndUnknownMarkup()
    {
        var (deck, slide) = Deck();
        using (deck)
        {
            Decks.AddShapeXml(slide,
                "<p:sp><p:nvSpPr><p:cNvPr id=\"5\" name=\"Box\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/>" +
                "<a:p><a:pPr algn=\"ctr\"><a:buNone/></a:pPr><a:r><a:rPr lang=\"de-DE\" b=\"1\" dirty=\"0\"><a:solidFill><a:srgbClr val=\"FF0000\"/></a:solidFill></a:rPr><a:t>old one</a:t></a:r>" +
                "<a:r><a:rPr lang=\"en-US\"/><a:t> old two</a:t></a:r><a:endParaRPr lang=\"de-DE\" sz=\"900\"/></a:p>" +
                "<a:p><a:r><a:t>gone</a:t></a:r></a:p></p:txBody></p:sp>");
            Shape box = slide.Shapes[^1];
            box.TextBody!.SetText("new\nline two");
            string xml = box.Markup.Node.OuterXml;
            Assert.Equal("new\nline two", box.TextBody.Text);
            Assert.Equal(2, box.TextBody.Paragraphs.Count);
            // The first run's formatting (language, bold, fill, and the attribute the model does not know) is on every new run.
            Assert.Equal(2, CountOf(xml, "<a:rPr lang=\"de-DE\" b=\"1\" dirty=\"0\"><a:solidFill><a:srgbClr val=\"FF0000\"/></a:solidFill></a:rPr>"));
            // The paragraph properties and end properties are copied to the new paragraph and kept on the first.
            Assert.Equal(2, CountOf(xml, "<a:pPr algn=\"ctr\"><a:buNone/></a:pPr>"));
            Assert.Equal(2, CountOf(xml, "<a:endParaRPr lang=\"de-DE\" sz=\"900\"/>"));
            Assert.DoesNotContain("gone", xml);
            Assert.Contains("<a:bodyPr/><a:lstStyle/>", xml);
        }
    }

    private static int CountOf(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Fact]
    public void SpecialCharactersAreEscapedAndControlCharactersUseOfficesSpelling()
    {
        var (deck, slide) = Deck();
        using (deck)
        {
            TextBody body = slide.Shapes[0].TextBody!;
            string text = "a < b & c > d \"q\" {a} {p} \u0001 caf\u00e9 \U0001F600";
            body.SetText(text);
            Assert.Equal(text, body.Text);
            byte[] saved = Decks.Save(deck);
            string xml = Decks.Xml(saved, "ppt/slides/slide1.xml");
            Assert.Contains("a &lt; b &amp; c &gt; d", xml);
            Assert.Contains("_x0001_", xml);
            using Presentation again = Decks.Reopen(saved);
            Assert.Equal(text, again.Slides[0].Shapes[0].TextBody!.Text);
        }
    }

    [Fact]
    public void LineBreaksStayInsideTheParagraph()
    {
        var (deck, slide) = Deck();
        using (deck)
        {
            TextParagraph p = slide.Shapes[1].TextBody!.Paragraphs[0];
            p.SetText("top\vbottom");
            Assert.Equal("top\vbottom", p.Text);
            Assert.Equal([TextRunKind.Text, TextRunKind.LineBreak, TextRunKind.Text], p.Runs.Select(r => r.Kind).ToArray());
            Assert.Single(slide.Shapes[1].TextBody!.Paragraphs);
            p.SetText(string.Empty);
            Assert.Empty(p.Runs);
            Assert.Equal(string.Empty, p.Text);
        }
    }

    [Fact]
    public void FieldsReadAsTheirLastTextAndKeepTheirType()
    {
        var (deck, slide) = Deck();
        using (deck)
        {
            Decks.AddShapeXml(slide,
                "<p:sp><p:nvSpPr><p:cNvPr id=\"6\" name=\"Num\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Page </a:t></a:r>" +
                "<a:fld id=\"{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>7</a:t></a:fld></a:p></p:txBody></p:sp>");
            TextParagraph p = slide.Shapes[^1].TextBody!.Paragraphs[0];
            Assert.Equal("Page 7", p.Text);
            TextRun field = p.Runs[1];
            Assert.Equal(TextRunKind.Field, field.Kind);
            Assert.Equal("slidenum", field.FieldType);
            field.Text = "8";
            Assert.Equal("Page 8", p.Text);
            Assert.Contains("type=\"slidenum\"", slide.Shapes[^1].Markup.Node.OuterXml);
        }
    }

    [Fact]
    public void AddParagraphCopiesTheLastParagraphsFormatting()
    {
        var (deck, slide) = Deck();
        using (deck)
        {
            TextBody body = slide.Shapes[1].TextBody!;
            body.SetText("first");
            body.Paragraphs[0].Level = 2;
            TextParagraph added = body.AddParagraph("second");
            Assert.Equal(2, added.Level);
            Assert.Equal("first\nsecond", body.Text);
            body.Paragraphs[0].Level = 0;
            Assert.Equal(0, body.Paragraphs[0].Level);
            Assert.Throws<ArgumentOutOfRangeException>(() => body.Paragraphs[0].Level = 9);
        }
    }

    [Fact]
    public void EditingTextChangesOnlyThatSlidesPart()
    {
        byte[] before;
        using (Presentation deck = Presentation.Create())
        {
            deck.AddSlide(Decks.Layout(deck, "Title Slide"));
            deck.AddSlide(Decks.Layout(deck, "Title and Content"));
            before = Decks.Save(deck);
        }

        using Presentation edit = Decks.Reopen(before);
        edit.Slides[0].Shapes[0].TextBody!.SetText("Changed");
        Assert.True(edit.HasUncommittedChanges);
        Assert.Equal(["ppt/slides/slide1.xml"], Decks.Changed(before, Decks.Save(edit)));
    }

    [Fact]
    public void ExtractTextFollowsSlidesThenTheirNotes()
    {
        using Presentation deck = Presentation.Create();
        Decks.AddNotesMaster(deck);
        Slide one = deck.AddSlide(Decks.Layout(deck, "Title Slide"));
        Slide two = deck.AddSlide(Decks.Layout(deck, "Title and Content"));
        one.Shapes[0].TextBody!.SetText("Alpha");
        one.SetNotesText("note A");
        two.Shapes[0].TextBody!.SetText("Beta");
        string text = deck.ExtractText();
        Assert.True(text.IndexOf("Alpha", StringComparison.Ordinal) < text.IndexOf("note A", StringComparison.Ordinal));
        Assert.True(text.IndexOf("note A", StringComparison.Ordinal) < text.IndexOf("Beta", StringComparison.Ordinal));
    }
}

public class ShapeModelTests
{
    [Fact]
    public void ShapesPicturesGroupsFramesAndAlternateContentAreModelled()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Blank"));
        string rid = Decks.Attach(deck, slide.PartName, "image", "/ppt/media/image1.png", "image/png", new byte[] { 1, 2, 3, 4 });
        Decks.AddShapeXml(slide, Decks.Picture(10, rid));
        Decks.AddShapeXml(slide, "<p:grpSp><p:nvGrpSpPr><p:cNvPr id=\"11\" name=\"Group\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"5\" y=\"6\"/><a:ext cx=\"7\" cy=\"8\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"7\" cy=\"8\"/></a:xfrm></p:grpSpPr>" + Decks.TextBox(12, "inner") + "</p:grpSp>");
        Decks.AddShapeXml(slide,
            "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"13\" name=\"Chart\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"1\" y=\"2\"/><a:ext cx=\"3\" cy=\"4\"/></p:xfrm>" +
            "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId77\"/></a:graphicData></a:graphic></p:graphicFrame>");
        Decks.AddShapeXml(slide,
            "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\" Requires=\"p14\">" +
            Decks.TextBox(14, "choice") + "</mc:Choice><mc:Fallback>" + Decks.TextBox(14, "fallback") + "</mc:Fallback></mc:AlternateContent>");

        IReadOnlyList<Shape> shapes = slide.Shapes;
        Assert.Equal([ShapeKind.Picture, ShapeKind.Group, ShapeKind.GraphicFrame, ShapeKind.AlternateContent], shapes.Select(s => s.Kind).ToArray());
        Shape pic = shapes[0];
        Assert.Equal(10, pic.Id);
        Assert.Equal("alt 10", pic.AltText);
        Assert.Equal(rid, pic.ImageRelationshipId);
        Assert.Equal("/ppt/media/image1.png", pic.ImagePartName);
        Assert.Equal(new ShapeBounds(100, 200, 300, 400), pic.Bounds);
        Assert.Equal(["/ppt/media/image1.png"], pic.References.Select(r => r.PartName).ToArray());

        Shape group = shapes[1];
        Assert.Equal(new ShapeBounds(5, 6, 7, 8), group.Bounds);
        Shape inner = Assert.Single(group.Children);
        Assert.Same(group, inner.Group);
        Assert.Equal("inner", inner.Text);

        Shape frame = shapes[2];
        Assert.Equal(GraphicFrameKind.Chart, frame.FrameKind);
        Assert.Equal(new ShapeBounds(1, 2, 3, 4), frame.Bounds);
        ShapeReference chart = Assert.Single(frame.References);
        Assert.Equal("rId77", chart.RelationshipId);
        Assert.Null(chart.Type); // dangling: reported as data, the shape is left alone

        Shape alt = shapes[3];
        Assert.Equal("choice", Assert.Single(alt.Children).Text);

        Assert.Equal([10L, 11, 12, 13, 14], slide.AllShapes().Select(s => s.Id ?? -1).Where(i => i > 0).ToArray());
        pic.AltText = "new alt";
        pic.Name = "Renamed";
        Assert.Equal("new alt", slide.Shapes[0].AltText);
        Assert.Equal("Renamed", slide.Shapes[0].Name);
    }

    [Fact]
    public void TransitionsAnimationsAndExtensionsSurviveAnEditByteForByte()
    {
        byte[] original;
        const string tail =
            "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\" Requires=\"p14\">" +
            "<p:transition spd=\"med\" p14:dur=\"700\"><p14:vortex dir=\"r\"/></p:transition></mc:Choice><mc:Fallback><p:transition spd=\"med\"><p:fade/></p:transition></mc:Fallback></mc:AlternateContent>" +
            "<p:timing><p:tnLst><p:par><p:cTn id=\"1\" dur=\"indefinite\" restart=\"never\" nodeType=\"tmRoot\"/></p:par></p:tnLst></p:timing>" +
            "<p:extLst><p:ext uri=\"{BB962C8B-B14F-4D97-AF65-F5344CB8AC3E}\"><p14:creationId xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\" val=\"42\"/></p:ext></p:extLst>";
        using (Presentation deck = Presentation.Create())
        {
            Slide slide = deck.AddSlide(Decks.Layout(deck, "Title Slide"));
            slide.Markup.Node.AppendXml(Decks.Utf8(tail));
            original = Decks.Save(deck);
        }

        using Presentation edit = Decks.Reopen(original);
        Slide s = edit.Slides[0];
        Assert.True(s.HasTransition);
        Assert.True(s.HasAnimations);
        s.Shapes[0].TextBody!.SetText("Edited");
        string xml = Decks.Xml(Decks.Save(edit), "ppt/slides/slide1.xml");
        Assert.Contains(tail, xml);
    }

    [Fact]
    public void HiddenSlidesAndHeaderFooterSwitches()
    {
        using Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Blank"));
        Assert.False(slide.IsHidden);
        slide.IsHidden = true;
        Assert.True(slide.IsHidden);
        Assert.Contains("show=\"0\"", slide.Markup.Node.OuterXml);
        slide.IsHidden = false;
        Assert.DoesNotContain("show=", slide.Markup.Node.OuterXml);
        Assert.Equal(default, deck.Masters[0].HeaderFooter);
        deck.Masters[0].Markup.Node.FirstChild.NextSibling.NextSibling.InsertXmlAfter(Decks.Utf8("<p:hf hdr=\"0\" dt=\"0\"/>"));
        Assert.Equal(new HeaderFooterFlags(true, false, true, false), deck.Masters[0].HeaderFooter);
    }
}
