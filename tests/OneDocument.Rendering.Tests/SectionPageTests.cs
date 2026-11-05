using System.Text;
using OneFont;
using OneRender.Painting;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Pagination over generated documents: section types, page size changes, columns, headers and footers, page numbers and page decoration.
public sealed class SectionPageTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    internal const string Letter = "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>";

    internal static string Sect(string inner, string? type = null, string? pgSzMar = null) =>
        "<w:sectPr>" + inner + (type is null ? string.Empty : "<w:type w:val=\"" + type + "\"/>") + (pgSzMar ?? Letter) + "</w:sectPr>";

    // A paragraph that ends a section.
    internal static string End(string sect) => "<w:p><w:pPr>" + sect + "</w:pPr><w:r><w:t>end</w:t></w:r></w:p>";

    internal static string Story(string root, string content) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:" + root + " xmlns:w=\"" + TestDocx.W + "\" xmlns:v=\"urn:schemas-microsoft-com:vml\">" + content + "</w:" + root + ">";

    internal static string PageField(string instr, string cached) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> " + instr + " </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>" + cached + "</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private static LayoutDocument Lay(string body, string finalSect, (string Name, string Xml)[]? parts = null, string? settings = null)
    {
        RenderingFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        using Document d = Document.Open(new MemoryStream(Build(body, finalSect, parts, settings)), leaveOpen: false);
        return LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts });
    }

    // A package with the given header and footer parts (named header*.xml and footer*.xml), registered with their content types.
    internal static byte[] Build(string body, string finalSect, (string Name, string Xml)[]? parts = null, string? settings = null)
    {
        var rels = new StringBuilder();
        var types = new StringBuilder();
        var extra = new List<(string, byte[])>();
        foreach ((string name, string xml) in parts ?? [])
        {
            string id = "rId_" + name.Replace('.', '_');
            string type = name.StartsWith("header", StringComparison.Ordinal) ? "header" : "footer";
            rels.Append("<Relationship Id=\"" + id + "\" Type=\"" + TestDocx.R + "/" + type + "\" Target=\"" + name + "\"/>");
            types.Append("<Override PartName=\"/word/" + name + "\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml." + type + "+xml\"/>");
            extra.Add(("word/" + name, Encoding.UTF8.GetBytes(xml)));
        }

        return TestDocx.Bytes(body, settings: settings, sectPr: finalSect, extraRels: rels.ToString(), extraParts: extra, extraTypes: types.ToString());
    }

    internal static string Ref(string kind, string type, string part) => "<w:" + kind + "Reference w:type=\"" + type + "\" r:id=\"rId_" + part.Replace('.', '_') + "\"/>";

    internal static string Paras(int n, string prefix = "Line") => string.Concat(Enumerable.Range(0, n).Select(i => TestDocx.P(prefix + " " + i)));

    private static double LineHeight
    {
        get
        {
            FontMetrics m = Fonts.Resolve(new FontRequest("Liberation Sans"))!.Face.Metrics;
            return (m.Ascender + m.LineGap - m.Descender) * 12.0 / m.UnitsPerEm;
        }
    }

    [Fact]
    public void SectionsChangeSizeOrientationAndMargins()
    {
        string landscape = "<w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:pgMar w:top=\"720\" w:right=\"1080\" w:bottom=\"720\" w:left=\"1080\" w:header=\"360\" w:footer=\"360\" w:gutter=\"0\"/>";
        LayoutDocument l = Lay(TestDocx.P("one") + End(Sect(string.Empty)) + TestDocx.P("two"), Sect(string.Empty, "nextPage", landscape));
        Assert.Equal(2, l.Pages.Count);
        Assert.Equal((612.0, 792.0), (l.Pages[0].Width, l.Pages[0].Height));
        Assert.Equal((792.0, 612.0), (l.Pages[1].Width, l.Pages[1].Height));
        Assert.Equal(new LayoutRect(54, 36, 684, 540), l.Pages[1].ContentArea);
        Assert.True(l.Pages[1].IsFirstPageOfSection);
    }

    [Fact]
    public void ContinuousSectionSharesThePageUntilTheSizeChanges()
    {
        LayoutDocument same = Lay(TestDocx.P("one") + End(Sect(string.Empty)) + TestDocx.P("two"), Sect(string.Empty, "continuous"));
        Assert.Single(same.Pages);
        Assert.Equal(3, same.Pages[0].Paragraphs.Count());

        string small = "<w:pgSz w:w=\"8000\" w:h=\"10000\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>";
        LayoutDocument diff = Lay(TestDocx.P("one") + End(Sect(string.Empty)) + TestDocx.P("two"), Sect(string.Empty, "continuous", small));
        Assert.Equal(2, diff.Pages.Count);
    }

    [Fact]
    public void OddAndEvenPageBreaksInsertBlankPages()
    {
        // Section 1 is page 1; an odd-page section lands on page 3 behind a blank page 2.
        LayoutDocument l = Lay(End(Sect(string.Empty)) + TestDocx.P("two") + End(Sect(string.Empty, "oddPage")) + TestDocx.P("three"), Sect(string.Empty, "evenPage"));
        Assert.Equal("False|True|False|False", string.Join('|', l.Pages.Select(p => p.IsBlank)));
        Assert.Contains(l.Notes, n => n.Code == "layout.blank-page");
        Assert.Equal(new long[] { 1, 2, 3, 4 }, l.Pages.Select(p => p.PrintedNumber));
        Assert.Empty(l.Pages[1].BodyParagraphs);
    }

    [Fact]
    public void RestartedNumberingDecidesTheParityOfOddPageBreaks()
    {
        // The second section restarts at 1, which is odd: no blank page is needed.
        LayoutDocument l = Lay(End(Sect(string.Empty)) + TestDocx.P("two"), Sect("<w:pgNumType w:start=\"1\"/>", "oddPage"));
        Assert.Equal(2, l.Pages.Count);
        Assert.Equal(new long[] { 1, 1 }, l.Pages.Select(p => p.PrintedNumber));
    }

    [Fact]
    public void EqualColumnsFlowDownThenAcross()
    {
        int perColumn = (int)Math.Floor((648 + 0.01) / LineHeight);
        LayoutDocument l = Lay(Paras(perColumn * 2 + 5), Sect("<w:cols w:num=\"2\" w:space=\"720\"/>"));
        Assert.Equal(2, l.Pages.Count);
        LayoutPage p = l.Pages[0];
        Assert.Equal(2, p.Columns.Count);
        Assert.Equal(216, p.Columns[0].Width, 3); // (468 - 36) / 2
        Assert.Equal(72 + 216 + 36, p.Columns[1].X, 3);
        Assert.Equal(perColumn, p.Paragraphs.Count(x => x.Column == 0));
        Assert.Equal(perColumn, p.Paragraphs.Count(x => x.Column == 1));
        Assert.Equal(5, l.Pages[1].Paragraphs.Count());
        Assert.All(p.Paragraphs.Where(x => x.Column == 1), x => Assert.Equal(p.Columns[1].X, x.Bounds.X, 3));
    }

    [Fact]
    public void UnequalColumnsUseTheirOwnWidthsAndRewrapText()
    {
        string cols = "<w:cols w:num=\"2\" w:equalWidth=\"0\"><w:col w:w=\"2880\" w:space=\"720\"/><w:col w:w=\"5760\"/></w:cols>";
        string longText = string.Join(' ', Enumerable.Repeat("word", 60));
        LayoutDocument l = Lay(TestDocx.P(longText) + "<w:p><w:r><w:br w:type=\"column\"/></w:r></w:p>" + TestDocx.P(longText), Sect(cols));
        LayoutPage p = l.Pages[0];
        Assert.Equal("144|288", string.Join('|', p.Columns.Select(c => c.Width)));
        Assert.Equal(72 + 144 + 36, p.Columns[1].X, 3);
        var inSecond = p.Paragraphs.Where(x => x.Column == 1).ToList();
        Assert.NotEmpty(inSecond);
        // Text in the wide column wraps wider than the narrow one.
        double narrow = p.Paragraphs.First(x => x.Column == 0).Lines.Max(x => x.Bounds.Width);
        double wide = inSecond[^1].Lines.Max(x => x.Bounds.Width);
        Assert.True(narrow < 150 && wide > 200 && wide < 295, narrow + " / " + wide);
    }

    [Fact]
    public void ColumnBreakStartsTheNextColumnAndAPageBreakTheNextPage()
    {
        LayoutDocument l = Lay(TestDocx.P("a") + "<w:p><w:r><w:br w:type=\"column\"/></w:r><w:r><w:t>b</w:t></w:r></w:p>" + "<w:p><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>c</w:t></w:r></w:p>", Sect("<w:cols w:num=\"2\" w:space=\"720\"/>"));
        Assert.Equal(2, l.Pages.Count);
        Assert.Equal("0|0|1|1", string.Join('|', l.Pages[0].Paragraphs.Select(x => x.Column)));
        Assert.Equal(0, l.Pages[1].Paragraphs.Single().Column);

        // With one column a column break is a page break.
        LayoutDocument one = Lay(TestDocx.P("a") + "<w:p><w:r><w:br w:type=\"column\"/></w:r><w:r><w:t>b</w:t></w:r></w:p>", Sect(string.Empty));
        Assert.Equal(2, one.Pages.Count);
    }

    [Fact]
    public void ContinuousBreakBalancesTheColumnsAbove()
    {
        // 20 short lines in two columns above a continuous section: 10 and 10, and the next section starts below them.
        LayoutDocument l = Lay(Paras(20) + End(Sect("<w:cols w:num=\"2\" w:space=\"720\"/>")) + TestDocx.P("below"), Sect(string.Empty, "continuous"));
        Assert.Single(l.Pages);
        LayoutPage p = l.Pages[0];
        Assert.Equal(2, p.ColumnRegions.Count);
        var balanced = p.Paragraphs.Where(x => x.Bounds.Y < p.ColumnRegions[0].Bottom - 1).ToList();
        int first = balanced.Count(x => x.Bounds.X < 200);
        int second = balanced.Count(x => x.Bounds.X >= 200);
        Assert.True(Math.Abs(first - second) <= 1 && first + second == 21, first + " vs " + second);
        LayoutParagraph below = p.Paragraphs.Last();
        Assert.Equal("below", below.Text);
        Assert.True(below.Bounds.Y >= p.ColumnRegions[0].Bottom - 0.5);
        Assert.True(p.ColumnRegions[0].Bottom < 72 + 12 * LineHeight + 1, "balanced height " + p.ColumnRegions[0].Bottom);
    }

    [Fact]
    public void ColumnSeparatorIsDrawnBetweenColumns()
    {
        LayoutDocument l = Lay(Paras(30), Sect("<w:cols w:num=\"2\" w:space=\"720\" w:sep=\"1\"/>"));
        LayoutDecoration sep = Assert.Single(l.Pages[0].Decorations, d => d.DecorationKind == LayoutDecorationKind.ColumnSeparator);
        Assert.Equal(72 + 216 + 18, sep.Bounds.X + sep.Bounds.Width / 2, 3);
    }

    [Fact]
    public void RightToLeftSectionStartsItsFirstColumnAtTheRight()
    {
        LayoutDocument l = Lay(TestDocx.P("a"), Sect("<w:bidi/><w:cols w:num=\"2\" w:space=\"720\"/>"));
        Assert.True(l.Pages[0].Columns[0].X > l.Pages[0].Columns[1].X);
    }

    [Fact]
    public void HeaderAndFooterAreLaidOutOnEveryPageAsStories()
    {
        string hdr = Story("hdr", TestDocx.P("Header text"));
        string ftr = Story("ftr", TestDocx.P("Footer text"));
        LayoutDocument l = Lay(Paras(100), Sect(Ref("header", "default", "header1.xml") + Ref("footer", "default", "footer1.xml")), [("header1.xml", hdr), ("footer1.xml", ftr)]);
        Assert.True(l.Pages.Count >= 2);
        foreach (LayoutPage p in l.Pages)
        {
            LayoutParagraph h = Assert.Single(p.HeaderParagraphs);
            LayoutParagraph f = Assert.Single(p.FooterParagraphs);
            Assert.Equal("Header text", h.Text);
            Assert.Equal("Footer text", f.Text);
            Assert.Equal(36, h.Bounds.Y, 3);
            Assert.Equal(792 - 36, f.Bounds.Bottom, 3);
            Assert.NotNull(p.HeaderArea);
            Assert.NotNull(p.FooterArea);
            Assert.DoesNotContain(p.BodyParagraphs, x => x.Text.StartsWith("Header", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ATallHeaderPushesTheBodyDown()
    {
        string hdr = Story("hdr", Paras(8, "Head"));
        LayoutDocument l = Lay(TestDocx.P("body"), Sect(Ref("header", "default", "header1.xml")), [("header1.xml", hdr)]);
        LayoutPage p = l.Pages[0];
        double headerBottom = 36 + (8 * LineHeight);
        Assert.True(headerBottom > 72);
        Assert.Equal(headerBottom, p.ContentArea.Y, 2);
        Assert.Equal(headerBottom, p.BodyParagraphs.Single().Bounds.Y, 2);
    }

    [Fact]
    public void ATallFooterPullsTheBodyBottomUp()
    {
        string ftr = Story("ftr", Paras(8, "Foot"));
        LayoutDocument l = Lay(TestDocx.P("body"), Sect(Ref("footer", "default", "footer1.xml")), [("footer1.xml", ftr)]);
        LayoutPage p = l.Pages[0];
        double footerTop = 792 - 36 - (8 * LineHeight);
        Assert.Equal(footerTop, p.ContentArea.Bottom, 2);
        Assert.Equal(footerTop, p.FooterParagraphs.First().Bounds.Y, 2);
    }

    [Fact]
    public void FirstPageAndEvenPageStoriesFollowTitlePageAndEvenAndOddHeaders()
    {
        string settings = "<w:settings xmlns:w=\"" + TestDocx.W + "\"><w:evenAndOddHeaders/></w:settings>";
        (string, string)[] parts =
        [
            ("header1.xml", Story("hdr", TestDocx.P("default"))),
            ("header2.xml", Story("hdr", TestDocx.P("first"))),
            ("header3.xml", Story("hdr", TestDocx.P("even"))),
        ];
        string sect = Sect(Ref("header", "default", "header1.xml") + Ref("header", "first", "header2.xml") + Ref("header", "even", "header3.xml") + "<w:titlePg/>");
        string body = TestDocx.P("a") + "<w:p><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>b</w:t></w:r></w:p><w:p><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>c</w:t></w:r></w:p><w:p><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>d</w:t></w:r></w:p>";
        LayoutDocument l = Lay(body, sect, parts, settings);
        Assert.Equal("first|even|default|even", string.Join('|', l.Pages.Select(p => p.HeaderParagraphs.Single().Text)));
    }

    [Fact]
    public void HeadersLinkToThePreviousSectionUnlessTheyHaveTheirOwn()
    {
        (string, string)[] parts = [("header1.xml", Story("hdr", TestDocx.P("one"))), ("header2.xml", Story("hdr", TestDocx.P("three")))];
        string s1 = Sect(Ref("header", "default", "header1.xml"));
        string s2 = Sect(string.Empty, "nextPage");
        string s3 = Sect(Ref("header", "default", "header2.xml"), "nextPage");
        LayoutDocument l = Lay(End(s1) + End(s2) + TestDocx.P("last"), s3, parts);
        Assert.Equal("one|one|three", string.Join('|', l.Pages.Select(p => p.HeaderParagraphs.Single().Text)));
    }

    [Fact]
    public void PageFieldsShowThePrintedNumberTotalsAndFormats()
    {
        string ftr = Story("ftr", "<w:p><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r>" + PageField("PAGE", "9") + "<w:r><w:t xml:space=\"preserve\"> of </w:t></w:r>" + PageField("NUMPAGES", "9") + "<w:r><w:t xml:space=\"preserve\"> / </w:t></w:r>" + PageField("SECTIONPAGES", "9") + "</w:p>");
        string s1 = Sect(Ref("footer", "default", "footer1.xml"));
        string body = TestDocx.P("a") + "<w:p><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>b</w:t></w:r></w:p>" + End(s1) + TestDocx.P("c");
        LayoutDocument l = Lay(body, Sect("<w:pgNumType w:start=\"10\" w:fmt=\"lowerRoman\"/>", "nextPage"), [("footer1.xml", ftr)]);
        Assert.Equal(3, l.Pages.Count);
        Assert.Equal("Page 1 of 3 / 2|Page 2 of 3 / 2|Page x of 3 / 1", string.Join('|', l.Pages.Select(p => p.FooterParagraphs.Single().Text)));
        Assert.Equal(new long[] { 1, 2, 10 }, l.Pages.Select(p => p.PrintedNumber));
        Assert.Equal("x", l.Pages[2].PrintedNumberText);
    }

    [Fact]
    public void MirroredMarginsSwapSidesOnEvenPagesAndTheGutterGoesInside()
    {
        string settings = "<w:settings xmlns:w=\"" + TestDocx.W + "\"><w:mirrorMargins/></w:settings>";
        string mar = "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"720\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"360\"/>";
        string body = TestDocx.P("a") + "<w:p><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>b</w:t></w:r></w:p>";
        LayoutDocument l = Lay(body, Sect(string.Empty, null, mar), null, settings);
        Assert.Equal(new LayoutRect(72 + 18, 72, 612 - 90 - 36, 648), l.Pages[0].ContentArea);
        Assert.Equal(new LayoutRect(36, 72, 612 - 36 - 90, 648), l.Pages[1].ContentArea);
        Assert.Equal(36, l.Pages[1].BodyParagraphs.Single().Bounds.X, 3);
    }

    [Fact]
    public void PageBordersAreDecorationsAroundTheTextOrFromThePageEdge()
    {
        string borders = "<w:pgBorders w:offsetFrom=\"page\"><w:top w:val=\"single\" w:sz=\"8\" w:space=\"24\" w:color=\"FF0000\"/><w:left w:val=\"single\" w:sz=\"8\" w:space=\"24\"/><w:bottom w:val=\"double\" w:sz=\"8\" w:space=\"24\"/><w:right w:val=\"single\" w:sz=\"8\" w:space=\"24\"/></w:pgBorders>";
        LayoutDocument l = Lay(TestDocx.P("a"), Sect(borders));
        var d = l.Pages[0].Decorations.Where(x => x.DecorationKind == LayoutDecorationKind.PageBorder).ToList();
        Assert.Equal(4, d.Count);
        Assert.Contains(d, x => x.Bounds == new LayoutRect(24, 24, 564, 1) && x.Style == "single");
        Assert.Contains(d, x => x.Style == "double" && Math.Abs(x.Bounds.Height - 3) < 1e-9 && Math.Abs(x.Bounds.Bottom - (792 - 24)) < 1e-9);

        string text = borders.Replace("page", "text", StringComparison.Ordinal);
        LayoutDocument t = Lay(TestDocx.P("a"), Sect(text));
        Assert.Contains(t.Pages[0].Decorations, x => x.Bounds.X == 72 - 24 - 1 && x.Bounds.Width == 1);
    }

    [Fact]
    public void LineNumbersCountEveryLineAndRestartPerPage()
    {
        string ln = "<w:lnNumType w:countBy=\"2\" w:restart=\"newPage\" w:distance=\"360\"/>";
        int perPage = (int)Math.Floor((648 + 0.01) / LineHeight);
        LayoutDocument l = Lay(Paras(perPage + 3), Sect(ln));
        Assert.Equal(2, l.Pages.Count);
        var first = l.Pages[0].Decorations.Where(x => x.DecorationKind == LayoutDecorationKind.LineNumber).ToList();
        Assert.Equal(perPage / 2, first.Count);
        Assert.Equal("2", first[0].Text);
        Assert.Equal("4", first[1].Text);
        Assert.True(first[0].Bounds.Right <= 72 - 18 + 0.01);
        Assert.Equal("2", string.Join('|', l.Pages[1].Decorations.Where(x => x.DecorationKind == LayoutDecorationKind.LineNumber).Select(x => x.Text)));
    }

    [Fact]
    public void VerticalAlignmentCentersTheBodyBetweenTheMargins()
    {
        LayoutDocument l = Lay(TestDocx.P("a"), Sect("<w:vAlign w:val=\"center\"/>"));
        LayoutParagraph p = l.Pages[0].BodyParagraphs.Single();
        double expect = 72 + ((648 - p.Bounds.Height) / 2);
        Assert.Equal(expect, p.Bounds.Y, 2);
        Assert.Equal(expect, p.Lines.First().Bounds.Y, 2);
    }

    [Fact]
    public void DocumentGridSnapsLinesToThePitch()
    {
        LayoutDocument l = Lay(Paras(3), Sect("<w:docGrid w:type=\"lines\" w:linePitch=\"400\"/>"));
        var ys = l.Pages[0].BodyParagraphs.Select(p => p.Bounds.Y).ToArray();
        Assert.Equal(20, ys[1] - ys[0], 3);
        Assert.Equal(20, ys[2] - ys[1], 3);
    }

    [Fact]
    public void TextWatermarkInTheHeaderIsADecorationAndPaintsAsAnArtifact()
    {
        string pict = "<w:p><w:r><w:pict><v:shape id=\"PowerPlusWaterMarkObject1\" type=\"#_x0000_t136\" style=\"position:absolute;width:400pt;height:100pt;rotation:315\" fillcolor=\"silver\" stroked=\"f\"><v:fill opacity=\".5\"/><v:textpath style=\"font-family:&quot;Calibri&quot;;font-size:1pt\" string=\"DRAFT\"/></v:shape></w:pict></w:r></w:p>";
        LayoutDocument l = Lay(TestDocx.P("body"), Sect(Ref("header", "default", "header1.xml")), [("header1.xml", Story("hdr", pict))]);
        LayoutDecoration w = Assert.Single(l.Pages[0].Decorations, d => d.DecorationKind == LayoutDecorationKind.Watermark);
        Assert.Equal("DRAFT", w.Text);
        Assert.Equal(315, w.Rotation);
        Assert.InRange(w.Bounds.Width, 399, 401);
        Assert.DoesNotContain(l.Notes, n => n.Code == "layout.drawing-skipped");
        DisplayList list = RecursiveAI.OneDocument.Rendering.LayoutPainter.Paint(l.Pages[0]);
        Assert.True(list.Count > 0);
    }

    [Fact]
    public void HeadersAndFootersPaintInsideArtifactTags()
    {
        string hdr = Story("hdr", TestDocx.P("Header text"));
        LayoutDocument l = Lay(TestDocx.P("body"), Sect(Ref("header", "default", "header1.xml")), [("header1.xml", hdr)]);
        DisplayList list = RecursiveAI.OneDocument.Rendering.LayoutPainter.Paint(l.Pages[0]);
        var roles = new List<TagRole>();
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem item = list[i];
            if (item.Kind == ItemKind.BeginTag)
            {
                roles.Add(item.Role);
            }
        }

        Assert.Equal(new[] { TagRole.Artifact, TagRole.Paragraph }, roles);
    }
}
