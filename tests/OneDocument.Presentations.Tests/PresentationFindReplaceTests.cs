using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Protection;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class PresentationFindReplaceTests
{
    private const string Shape =
        "<p:sp><p:nvSpPr><p:cNvPr id=\"5\" name=\"Box\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/>{0}</p:txBody></p:sp>";

    private static (Presentation Deck, Slide Slide) Deck(string paragraphs)
    {
        Presentation deck = Presentation.Create();
        Slide slide = deck.AddSlide(Decks.Layout(deck, "Title and Content"));
        Decks.AddShapeXml(slide, Shape.Replace("{0}", paragraphs, StringComparison.Ordinal));
        return (deck, slide);
    }

    [Fact]
    public void MatchAcrossRunsKeepsTheFirstRunsFormattingAndEverythingElse()
    {
        var (deck, slide) = Deck("<a:p><a:pPr algn=\"ctr\"/><a:r><a:rPr lang=\"en-US\" b=\"1\" dirty=\"0\"/><a:t>The qui</a:t></a:r><a:r><a:rPr lang=\"de-DE\" i=\"1\"/><a:t>ck brown</a:t></a:r><a:r><a:rPr lang=\"en-US\"/><a:t> fox</a:t></a:r><a:endParaRPr lang=\"en-US\"/></a:p>");
        using (deck)
        {
            ReplaceReport r = deck.Replace("quick brown", "slow");
            Assert.Equal(1, r.Count);
            Assert.Equal(slide.PartName, r.Items[0].Location.Container);
            Assert.Equal("slide", r.Items[0].Location.Story);
            string xml = slide.Shapes[^1].Markup.Node.OuterXml;
            Assert.Contains("<a:pPr algn=\"ctr\"/><a:r><a:rPr lang=\"en-US\" b=\"1\" dirty=\"0\"/><a:t>The slow</a:t></a:r><a:r><a:rPr lang=\"en-US\"/><a:t> fox</a:t></a:r><a:endParaRPr lang=\"en-US\"/>", xml);
            Assert.DoesNotContain("de-DE", xml);
            Assert.Equal("The slow fox", slide.Shapes[^1].TextBody!.Text);
        }
    }

    [Fact]
    public void TablesGroupsFieldsAndBreaksAreSearched()
    {
        string table = "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"9\" name=\"T\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1\" cy=\"1\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblGrid><a:gridCol w=\"100\"/></a:tblGrid><a:tr h=\"10\"><a:tc><a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>cell needle</a:t></a:r></a:p></a:txBody><a:tcPr/></a:tc></a:tr></a:tbl></a:graphicData></a:graphic></p:graphicFrame>";
        var (deck, slide) = Deck("<a:p><a:r><a:t>line one</a:t></a:r><a:br><a:rPr lang=\"en-US\"/></a:br><a:r><a:t>needle two</a:t></a:r></a:p><a:p><a:fld id=\"{1}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>7</a:t></a:fld></a:p>");
        using (deck)
        {
            Decks.AddShapeXml(slide, table);
            IReadOnlyList<TextHit> hits = deck.Find("needle");
            Assert.Equal(2, hits.Count);
            Assert.Equal(9, hits[0].Location.Offset);   // one + break (U+000B) + ...: "line one\vneedle two"
            Assert.Equal(5, hits[1].Location.Offset);
            Assert.Single(deck.Find("one\vneedle"));
            Assert.Single(deck.Find("7"));
            Assert.Equal(2, deck.Replace("needle", "pin").Count);
            Assert.Contains("cell pin", slide.Markup.Node.OuterXml);
        }
    }

    [Fact]
    public void ReplacementWithALineBreakSplitsTheRunAndKeepsItsProperties()
    {
        var (deck, slide) = Deck("<a:p><a:r><a:rPr lang=\"fr-FR\" b=\"1\"/><a:t>one;two</a:t></a:r></a:p>");
        using (deck)
        {
            Assert.Equal(1, deck.Replace(";", "\n").Count);
            string xml = slide.Shapes[^1].Markup.Node.OuterXml;
            Assert.Contains("<a:r><a:rPr lang=\"fr-FR\" b=\"1\"/><a:t xml:space=\"preserve\">one</a:t></a:r><a:br><a:rPr lang=\"fr-FR\" b=\"1\"/></a:br><a:r><a:rPr lang=\"fr-FR\" b=\"1\"/><a:t xml:space=\"preserve\">two</a:t></a:r>", xml);
            Assert.Equal("one\vtwo", slide.Shapes[^1].TextBody!.Paragraphs[0].Text);
            // and a line break itself can be replaced by text
            Assert.Equal(1, deck.Replace("\v", " / ").Count);
            Assert.Equal("one / two", slide.Shapes[^1].TextBody!.Paragraphs[0].Text);
        }
    }

    [Fact]
    public void NotesLayoutsAndMastersAreSearchedOnlyOnRequest()
    {
        var (deck, slide) = Deck("<a:p><a:r><a:t>plain</a:t></a:r></a:p>");
        using (deck)
        {
            Decks.AddNotesMaster(deck);
            slide.SetNotesText("remember the needle");
            Assert.Single(deck.Find("needle"));
            Assert.Empty(deck.Find("needle", new PresentationFindOptions { Parts = PresentationParts.Slides }));
            TextHit n = deck.Find("needle")[0];
            Assert.Equal("notes", n.Location.Story);
            Assert.True(deck.Find("Click to edit Master", new PresentationFindOptions { Parts = PresentationParts.Masters }).Count > 0);
            Assert.Empty(deck.Find("Click to edit Master"));
            Assert.True(deck.Find("Click to edit", new PresentationFindOptions { Parts = PresentationParts.Layouts }).Count >= 0);
            Assert.Equal(1, deck.Replace("needle", "pin").Count);
            Assert.Equal("remember the pin", slide.Notes!.NotesText);
        }
    }

    [Fact]
    public void ChangesSaveAndOnlyTheEditedPartChanges()
    {
        var (deck, slide) = Deck("<a:p><a:r><a:t>alpha beta</a:t></a:r></a:p>");
        using (deck)
        {
            byte[] before = Decks.Save(deck);
            using Presentation again = Decks.Reopen(before);
            Assert.Equal(1, again.Replace("beta", "gamma", new PresentationReplaceOptions { Search = new SearchOptions { UseRegex = true } }).Count);
            byte[] after = Decks.Save(again);
            Assert.Equal([slide.PartName.TrimStart('/')], Decks.Changed(before, after));
            Assert.Contains("alpha gamma", Decks.Xml(after, slide.PartName.TrimStart('/')));
        }
    }

    [Fact]
    public void ProtectionRoundTripsAndVerifies()
    {
        using Presentation deck = Presentation.Create();
        Assert.False(deck.Protection.IsProtected);
        deck.Protection.Protect("open sesame", new ProtectionOptions { SpinCount = 400, Algorithm = ProtectionHash.Sha384 });
        byte[] bytes = Decks.Save(deck);
        using Presentation back = Decks.Reopen(bytes);
        Assert.True(back.Protection.IsProtected);
        Assert.Equal("SHA-384", back.Protection.Verifier!.AlgorithmName);
        Assert.Equal(PasswordCheck.Match, back.Protection.Verify("open sesame"));
        Assert.Equal(PasswordCheck.Mismatch, back.Protection.Verify("open"));
        Assert.Equal(PasswordCheck.Mismatch, back.Protection.TryRemove("x"));
        Assert.True(back.Protection.IsProtected);
        Assert.Equal(PasswordCheck.Match, back.Protection.TryRemove("open sesame"));
        Assert.False(back.Protection.IsProtected);
        string xml = Decks.Xml(bytes, "ppt/presentation.xml");
        Assert.Contains("<p:modifyVerifier", xml);
        Assert.Contains("spinValue=\"400\"", xml);
        // schema order (ECMA-376 Part 1 CT_Presentation): modifyVerifier follows the slide size and the default text style
        Assert.True(xml.IndexOf("<p:modifyVerifier", StringComparison.Ordinal) > xml.IndexOf("<p:sldMasterIdLst", StringComparison.Ordinal));
    }
}
