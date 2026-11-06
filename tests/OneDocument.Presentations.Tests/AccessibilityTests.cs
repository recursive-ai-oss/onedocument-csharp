using RecursiveAI.OneDocument.Accessibility;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>One positive and one negative presentation per accessibility rule that applies to presentations.</summary>
public class AccessibilityTests
{
    private const string P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";

    private static Presentation Deck(out Slide slide, string layout = "Blank")
    {
        Presentation deck = Presentation.Create();
        slide = deck.AddSlide(Decks.Layout(deck, layout));
        deck.SetAccessibilityTitle("Deck");
        return deck;
    }

    // With ONEDOCUMENT_A11Y_FIXTURES set, each rule case is also written as a file, for checking against Office's own checker.
    private static void Emit(Presentation deck, string name)
    {
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_A11Y_FIXTURES") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            deck.SaveAs(Path.Combine(dir, name + ".pptx"));
        }
    }

    private static string[] Ids(AccessibilityReport r) => [.. r.Issues.Select(i => i.RuleId)];

    private static string Pic(int id, string attrs = "", string nvPr = "") =>
        "<p:pic><p:nvPicPr><p:cNvPr id=\"" + id + "\" name=\"Picture " + id + "\" " + attrs + "/><p:cNvPicPr/><p:nvPr>" + nvPr + "</p:nvPr></p:nvPicPr>" +
        "<p:blipFill><a:blip r:embed=\"rId9\"/></p:blipFill><p:spPr/></p:pic>";

    private static string Frame(int id, string uri, string data = "", string attrs = "") =>
        "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"" + id + "\" name=\"Frame " + id + "\" " + attrs + "/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1\" cy=\"1\"/></p:xfrm>" +
        "<a:graphic><a:graphicData uri=\"" + uri + "\">" + data + "</a:graphicData></a:graphic></p:graphicFrame>";

    private static string Title(int id, string text) =>
        "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"Title " + id + "\"/><p:cNvSpPr/><p:nvPr><p:ph type=\"title\"/></p:nvPr></p:nvSpPr><p:spPr/>" +
        "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>" + text + "</a:t></a:r></a:p></p:txBody></p:sp>";

    private static string Tc(string text, string attrs = "") =>
        "<a:tc " + attrs + "><a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>" + text + "</a:t></a:r></a:p></a:txBody><a:tcPr/></a:tc>";

    private static string Table(bool header, params string[] rows) =>
        Frame(20, "http://schemas.openxmlformats.org/drawingml/2006/table",
            "<a:tbl><a:tblPr" + (header ? " firstRow=\"1\"" : "") + "/><a:tblGrid><a:gridCol w=\"1\"/><a:gridCol w=\"1\"/></a:tblGrid>" + string.Concat(rows.Select(r => "<a:tr h=\"1\">" + r + "</a:tr>")) + "</a:tbl>");

    private static void Fires(string ruleId, Action<Slide> positive, Action<Slide> negative, Action<AccessibilityIssue>? located = null)
    {
        using (Presentation deck = Deck(out Slide s))
        {
            positive(s);
            Emit(deck, ruleId + ".positive");
            AccessibilityReport r = deck.CheckAccessibility();
            AccessibilityIssue? hit = r.Issues.FirstOrDefault(i => i.RuleId == ruleId);
            Assert.True(hit is not null, ruleId + " did not fire; got: " + string.Join(", ", Ids(r)));
            located?.Invoke(hit);
        }

        using (Presentation deck = Deck(out Slide s))
        {
            negative(s);
            Emit(deck, ruleId + ".negative");
            Assert.DoesNotContain(ruleId, Ids(deck.CheckAccessibility()));
        }
    }

    private static void Add(Slide s, string xml) => Decks.AddShapeXml(s, xml);

    [Fact]
    public void AltTextMissing_ForPictureChartDiagramGroupAndShape()
    {
        Fires("alt-text.missing", s => Add(s, Pic(7)), s => Add(s, Pic(7, "descr=\"A harbour at dusk\"")), hit =>
        {
            Assert.Equal(AccessibilityCategory.Error, hit.Category);
            Assert.Equal(1, hit.Location.Slide);
            Assert.Equal(7, hit.Location.ShapeId);
            Assert.Equal("Picture 7", hit.ObjectName);
            Assert.Equal(AccessibilityFixKind.MarkDecorative, hit.Fix);
        });

        using Presentation deck = Deck(out Slide s);
        Add(s, Frame(8, "http://schemas.openxmlformats.org/drawingml/2006/chart"));
        Add(s, Frame(9, "http://schemas.openxmlformats.org/drawingml/2006/diagram"));
        Add(s, "<p:grpSp><p:nvGrpSpPr><p:cNvPr id=\"10\" name=\"Group 10\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" + Pic(11) + "</p:grpSp>");
        Add(s, "<p:sp><p:nvSpPr><p:cNvPr id=\"12\" name=\"Rectangle 12\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/></p:sp>");
        Add(s, "<p:sp><p:nvSpPr><p:cNvPr id=\"13\" name=\"Box\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Words</a:t></a:r></a:p></p:txBody></p:sp>");
        long[] ids = [.. deck.CheckAccessibility().ForRule("alt-text.missing").Select(i => i.Location.ShapeId ?? 0)];
        // The group covers the picture inside it; a text box is described by its text.
        Assert.Equal([8L, 9L, 10L, 12L], ids);
    }

    [Fact]
    public void AltText_FileNameSuggestedAndDecorative()
    {
        Fires("alt-text.file-name", s => Add(s, Pic(7, "descr=\"IMG_0042.png\"")), s => Add(s, Pic(7, "descr=\"Photo\"")));
        Fires("alt-text.suggested", s => Add(s, Pic(7, "descr=\"A dog. Description automatically generated\"")), s => Add(s, Pic(7, "descr=\"A dog\"")));
        const string deco = "<a:extLst><a:ext uri=\"{C183D7F6-B498-43B3-948B-1728B52AA6E4}\"><adec:decorative xmlns:adec=\"http://schemas.microsoft.com/office/drawing/2017/decorative\" val=\"1\"/></a:ext></a:extLst>";
        using Presentation deck = Deck(out Slide s);
        Add(s, "<p:pic><p:nvPicPr><p:cNvPr id=\"7\" name=\"Pic\">" + deco + "</p:cNvPr><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed=\"rId9\"/></p:blipFill><p:spPr/></p:pic>");
        Assert.DoesNotContain("alt-text.missing", Ids(deck.CheckAccessibility()));
    }

    [Fact]
    public void SlideTitleMissing_AndDuplicate()
    {
        Fires("slide.title-missing", s => { }, s => Add(s, Title(2, "Welcome")), hit => Assert.Equal(AccessibilityCategory.Error, hit.Category));

        using Presentation deck = Presentation.Create();
        deck.SetAccessibilityTitle("t");
        for (int i = 0; i < 2; i++)
        {
            Slide s = deck.AddSlide(Decks.Layout(deck, "Blank"));
            Add(s, Title(2, "Same title"));
        }

        Emit(deck, "slide.title-duplicate.positive");
        AccessibilityIssue dup = Assert.Single(deck.CheckAccessibility().Issues, i => i.RuleId == "slide.title-duplicate");
        Assert.Equal(2, dup.Location.Slide);
        Assert.Equal(AccessibilityCategory.Tip, dup.Category);
    }

    [Fact]
    public void SlideTitleUnique_Negative()
    {
        using Presentation deck = Presentation.Create();
        deck.SetAccessibilityTitle("t");
        foreach (string title in new[] { "First", "Second" })
        {
            Add(deck.AddSlide(Decks.Layout(deck, "Blank")), Title(2, title));
        }

        Emit(deck, "slide.title-duplicate.negative");
        Assert.DoesNotContain("slide.title-duplicate", Ids(deck.CheckAccessibility()));
    }

    [Fact]
    public void ReadingOrder_TitleMustBeFirst() =>
        Fires("slide.reading-order",
            s => { Add(s, Decks.TextBox(5, "Body text")); Add(s, Title(2, "Title")); },
            s => { Add(s, Title(2, "Title")); Add(s, Decks.TextBox(5, "Body text")); },
            hit => Assert.Equal(AccessibilityCategory.Warning, hit.Category));

    [Fact]
    public void TableHeaderRow()
    {
        string simple = "<a:tr h=\"1\">" + Tc("a") + Tc("b") + "</a:tr>";
        _ = simple;
        Fires("table.header-row", s => Add(s, Table(false, Tc("a") + Tc("b"), Tc("c") + Tc("d"))), s => Add(s, Table(true, Tc("a") + Tc("b"), Tc("c") + Tc("d"))), hit =>
        {
            Assert.Equal(AccessibilityFixKind.SetHeaderRow, hit.Fix);
            Assert.Equal(20, hit.Location.ShapeId);
        });
    }

    [Fact]
    public void TableMergedAndIrregular()
    {
        Fires("table.merged-cells", s => Add(s, Table(true, Tc("a", "gridSpan=\"2\"") + Tc("", "hMerge=\"1\""), Tc("c") + Tc("d"))), s => Add(s, Table(true, Tc("a") + Tc("b"), Tc("c") + Tc("d"))));
        Fires("table.irregular-grid", s => Add(s, Table(true, Tc("a") + Tc("b"), Tc("c"))), s => Add(s, Table(true, Tc("a") + Tc("b"), Tc("c") + Tc("d"))));
    }

    [Fact]
    public void TableBlankRowAndColumn()
    {
        Fires("table.blank-cells", s => Add(s, Table(true, Tc("a") + Tc("b"), Tc("") + Tc(""), Tc("c") + Tc("d"))), s => Add(s, Table(true, Tc("a") + Tc("b"), Tc("c") + Tc("d"))), hit => Assert.Contains("Row 2", hit.Message));
        using Presentation deck = Deck(out Slide s2);
        Add(s2, Table(true, Tc("a") + Tc(""), Tc("c") + Tc("")));
        Assert.Contains(deck.CheckAccessibility().Issues, i => i.RuleId == "table.blank-cells" && i.Message.Contains("Column 2", StringComparison.Ordinal));
    }

    private static void AddSections(Presentation deck, params string[] names)
    {
        var sb = new System.Text.StringBuilder("<p:extLst xmlns:p14=\"" + P14 + "\"><p:ext uri=\"{521415D9-36F7-43E2-AB2F-B90AF26B5E84}\"><p14:sectionLst>");
        for (int i = 0; i < names.Length; i++)
        {
            sb.Append("<p14:section name=\"" + names[i] + "\" id=\"{00000000-0000-0000-0000-00000000000" + i + "}\"><p14:sldIdLst>");
            if (i == 0)
            {
                foreach (Slide s in deck.Slides)
                {
                    sb.Append("<p14:sldId id=\"" + s.Id + "\"/>");
                }
            }

            sb.Append("</p14:sldIdLst></p14:section>");
        }

        sb.Append("</p14:sectionLst></p:ext></p:extLst>");
        deck.Markup.Node.AppendXml(Decks.Utf8(sb.ToString()));
    }

    [Fact]
    public void SectionNames()
    {
        foreach (string name in new[] { "Default Section", "Untitled Section", "Section 3" })
        {
            using Presentation deck = Deck(out _);
            AddSections(deck, name);
            if (name == "Section 3")
            {
                Emit(deck, "section.default-name.positive");
            }

            AccessibilityIssue hit = Assert.Single(deck.CheckAccessibility().Issues, i => i.RuleId == "section.default-name");
            Assert.Equal(name, hit.Location.Section);
            Assert.Equal(AccessibilityCategory.Error, hit.Category);
        }

        using (Presentation ok = Deck(out _))
        {
            AddSections(ok, "Introduction", "Results");
            Emit(ok, "section.default-name.negative");
            Emit(ok, "section.duplicate-name.negative");
            Assert.DoesNotContain("section.default-name", Ids(ok.CheckAccessibility()));
            Assert.DoesNotContain("section.duplicate-name", Ids(ok.CheckAccessibility()));
        }

        using Presentation dup = Deck(out _);
        AddSections(dup, "Results", "results");
        Emit(dup, "section.duplicate-name.positive");
        Assert.Contains("section.duplicate-name", Ids(dup.CheckAccessibility()));
    }

    [Fact]
    public void MediaCaptions() =>
        Fires("media.captions", s => Add(s, Pic(7, "descr=\"Intro video\"", "<a:videoFile r:link=\"rId5\"/>")),
            s => Add(s, Pic(7, "descr=\"Intro video\"", "<a:videoFile r:link=\"rId5\"/><p:extLst><p:ext uri=\"{x}\"><p2017:trackLst xmlns:p2017=\"http://schemas.microsoft.com/office/powerpoint/2017/3/main\"><p2017:track id=\"{00000000-0000-0000-0000-000000000001}\"/></p2017:trackLst></p:ext></p:extLst>")));

    private static string Run(string text, string rPr = "") =>
        "<p:sp><p:nvSpPr><p:cNvPr id=\"30\" name=\"Text 30\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r>" + rPr + "<a:t>" + text + "</a:t></a:r></a:p></p:txBody></p:sp>";

    [Fact]
    public void HyperlinkText()
    {
        static string Link(string text) => Run(text, "<a:rPr lang=\"en-US\"><a:hlinkClick xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:id=\"rId7\"/></a:rPr>");
        Fires("hyperlink.text", s => Add(s, Link("click here")), s => Add(s, Link("Annual report")));
    }

    [Fact]
    public void ContrastLow()
    {
        static string Fg(string hex, string extra = "") => $"<a:rPr lang=\"en-US\"{extra}><a:solidFill><a:srgbClr val=\"{hex}\"/></a:solidFill></a:rPr>";
        Fires("contrast.low", s => Add(s, Run("Faint", Fg("D9D9D9"))), s => Add(s, Run("Strong", Fg("000000"))), hit =>
        {
            Assert.Equal(AccessibilityCategory.Warning, hit.Category);
            Assert.Equal(30, hit.Location.ShapeId);
            Assert.Contains("D9D9D9", hit.Message);
        });
        // The shape's own fill is the background; large text needs 3:1.
        string filled = "<p:sp><p:nvSpPr><p:cNvPr id=\"31\" name=\"Dark\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:solidFill><a:srgbClr val=\"202020\"/></a:solidFill></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r>" + Fg("303030") + "<a:t>Hidden</a:t></a:r></a:p></p:txBody></p:sp>";
        using Presentation deck = Deck(out Slide s);
        Add(s, filled);
        Assert.Contains("contrast.low", Ids(deck.CheckAccessibility()));
        using Presentation big = Deck(out Slide s2);
        Add(s2, Run("Big", Fg("8A8A8A", " sz=\"3200\"")));
        Assert.DoesNotContain("contrast.low", Ids(big.CheckAccessibility()));
    }

    [Fact]
    public void GradientBackground_IsSkippedNotGuessed()
    {
        using Presentation deck = Deck(out Slide s);
        string sp = "<p:sp><p:nvSpPr><p:cNvPr id=\"31\" name=\"Grad\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:gradFill><a:gsLst><a:gs pos=\"0\"><a:srgbClr val=\"000000\"/></a:gs></a:gsLst></a:gradFill></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Text</a:t></a:r></a:p></p:txBody></p:sp>";
        Add(s, sp);
        AccessibilityReport r = deck.CheckAccessibility();
        Assert.DoesNotContain("contrast.low", Ids(r));
        Assert.Contains(r.Skipped, k => k.RuleId == "contrast.low");
    }

    [Fact]
    public void DocumentTitleAndLanguage()
    {
        using Presentation deck = Presentation.Create();
        Slide s = deck.AddSlide(Decks.Layout(deck, "Blank"));
        Add(s, Run("No language here"));
        Emit(deck, "document.title.positive");
        Emit(deck, "document.language.positive");
        AccessibilityReport r = deck.CheckAccessibility();
        Assert.Contains("document.title", Ids(r));
        Assert.Contains("document.language", Ids(r));
        Assert.True(deck.SetAccessibilityTitle("Quarterly review").Applied);
        Assert.DoesNotContain("document.title", Ids(deck.CheckAccessibility()));
        using Presentation ok = Deck(out Slide s2);
        Add(s2, Run("Hello", "<a:rPr lang=\"en-US\"/>"));
        Emit(ok, "document.title.negative");
        Emit(ok, "document.language.negative");
        Assert.DoesNotContain("document.language", Ids(ok.CheckAccessibility()));
    }

    [Fact]
    public void Fixers_MarkDecorativeAndHeaderRow()
    {
        using Presentation deck = Deck(out Slide s);
        Add(s, Pic(7));
        Add(s, Table(false, Tc("a") + Tc("b"), Tc("c") + Tc("d")));
        AccessibilityReport before = deck.CheckAccessibility();
        Assert.True(deck.ApplyAccessibilityFix(before.Issues.First(i => i.Fix == AccessibilityFixKind.MarkDecorative)).Applied);
        Assert.True(deck.ApplyAccessibilityFix(before.Issues.First(i => i.Fix == AccessibilityFixKind.SetHeaderRow)).Applied);
        AccessibilityReport after = deck.CheckAccessibility();
        Assert.DoesNotContain("alt-text.missing", Ids(after));
        Assert.DoesNotContain("table.header-row", Ids(after));
        // An unfixable finding says why nothing happened.
        Assert.False(deck.ApplyAccessibilityFix(new AccessibilityIssue(AccessibilityRules.SlideTitleMissing, new AccessibilityLocation(), null, "m", "Add a title.")).Applied);
    }

    [Fact]
    public void Checking_EditsNothing()
    {
        using Presentation deck = Deck(out Slide s);
        Add(s, Pic(7));
        byte[] before = Decks.Save(deck);
        _ = deck.CheckAccessibility();
        Assert.Equal(before, Decks.Save(deck));
    }
}
