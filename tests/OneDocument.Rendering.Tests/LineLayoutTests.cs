using OneFont;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class LineLayoutTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    private static LayoutDocument Lay(string body, string? styles = null, string? numbering = null, string? settings = null, string? sectPr = null)
    {
        using Document d = TestDocx.Open(body, styles, numbering, settings, sectPr);
        return LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts });
    }

    private static FontFace Sans => Fonts.Resolve(new FontRequest("Liberation Sans"))!.Face;

    // Width of a string without kerning or ligatures, the way the layout measures it when w:kern is absent.
    private static double Width(FontFace face, string s, double size)
    {
        double w = 0;
        foreach (char c in s)
        {
            w += face.GetAdvance(face.GetGlyphIndex(c)) * size / face.UnitsPerEm;
        }

        return w;
    }

    private static (double Ascent, double Descent) Lm(FontFace f, double size)
    {
        FontMetrics m = f.Metrics;
        return ((m.Ascender + m.LineGap) * size / m.UnitsPerEm, -m.Descender * size / m.UnitsPerEm);
    }

    [Fact]
    public void OneLineHasTheFontsWidthAndHeight()
    {
        LayoutDocument l = Lay(TestDocx.P("Hello world"));
        LayoutPage page = Assert.Single(l.Pages);
        LayoutLine line = Assert.Single(Assert.Single(page.Paragraphs).Lines);
        (double asc, double desc) = Lm(Sans, 12);
        Assert.Equal(72 + asc, line.Baseline, 3);
        Assert.Equal(asc + desc, line.Bounds.Height, 3);
        Assert.Equal(Width(Sans, "Hello world", 12), line.Bounds.Width, 3);
        Assert.Equal(72, line.Bounds.X, 3);
        Assert.Equal("Hello world", line.Text);
    }

    [Fact]
    public void LongTextBreaksAtSpacesAndFitsTheMeasure()
    {
        string text = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor sit amet", 12));
        LayoutDocument l = Lay(TestDocx.P(text));
        LayoutParagraph p = Assert.Single(l.Pages[0].Paragraphs);
        var lines = p.Lines.ToList();
        Assert.True(lines.Count > 3);
        Assert.Equal(text, string.Concat(lines.Select(x => x.Text)));
        foreach (LayoutLine line in lines)
        {
            Assert.True(Width(Sans, line.Text.TrimEnd(), 12) <= 468 + 0.01, line.Text);
            Assert.True(line.Text.TrimEnd().Length > 0);
        }

        // A line is as full as it can be: the next word would not have fit.
        for (int i = 0; i < lines.Count - 1; i++)
        {
            string nextWord = lines[i + 1].Text.Split(' ')[0];
            Assert.True(Width(Sans, lines[i].Text + nextWord, 12) > 468);
        }
    }

    [Fact]
    public void LineStartsAndLengthsIndexTheParagraphText()
    {
        string text = string.Join(' ', Enumerable.Repeat("alpha beta gamma", 40));
        LayoutParagraph p = Lay(TestDocx.P(text)).Pages[0].Paragraphs.Single();
        int at = 0;
        foreach (LayoutLine line in p.Lines)
        {
            Assert.Equal(at, line.TextStart);
            Assert.Equal(p.Text.Substring(line.TextStart, line.TextLength), line.Text);
            at += line.TextLength;
        }

        Assert.Equal(p.Text.Length, at);
    }

    [Fact]
    public void DefaultTabsFallEvery36Points()
    {
        LayoutLine line = Lay(TestDocx.P("A\tB")).Pages[0].Paragraphs.Single().Lines.Single();
        LayoutRun[] runs = line.Runs.ToArray();
        Assert.Equal(LayoutRunKind.Text, runs[0].RunKind);
        Assert.Equal(LayoutRunKind.Tab, runs[1].RunKind);
        Assert.Equal(72 + 36, runs[2].Bounds.X, 3);
    }

    [Fact]
    public void CustomTabStopsAndRightAlignedTabs()
    {
        string pPr = "<w:tabs><w:tab w:val=\"left\" w:pos=\"2880\"/><w:tab w:val=\"right\" w:pos=\"8640\"/></w:tabs>";
        LayoutLine line = Lay(TestDocx.P("a\tb\tc", pPr)).Pages[0].Paragraphs.Single().Lines.Single();
        LayoutRun[] text = line.Runs.Where(r => r.RunKind == LayoutRunKind.Text).ToArray();
        Assert.Equal(72 + 144, text[1].Bounds.X, 3);
        Assert.Equal(72 + 432, text[2].Bounds.Right, 3);
    }

    [Fact]
    public void HangingIndentPutsFirstLineLeftOfTheRest()
    {
        string text = string.Join(' ', Enumerable.Repeat("word", 60));
        LayoutParagraph p = Lay(TestDocx.P(text, "<w:ind w:left=\"1440\" w:hanging=\"720\"/>")).Pages[0].Paragraphs.Single();
        var lines = p.Lines.ToList();
        Assert.Equal(72 + 36, lines[0].Bounds.X, 3);
        Assert.Equal(72 + 72, lines[1].Bounds.X, 3);
        Assert.True(lines.All(l => l.Bounds.Right <= 72 + 468 + 0.01 + Width(Sans, " ", 12)));
    }

    [Fact]
    public void FirstLineAndRightIndents()
    {
        string text = string.Join(' ', Enumerable.Repeat("word", 60));
        LayoutParagraph p = Lay(TestDocx.P(text, "<w:ind w:left=\"720\" w:right=\"720\" w:firstLine=\"360\"/>")).Pages[0].Paragraphs.Single();
        var lines = p.Lines.ToList();
        Assert.Equal(72 + 36 + 18, lines[0].Bounds.X, 3);
        Assert.Equal(72 + 36, lines[1].Bounds.X, 3);
        Assert.True(lines.All(l => Width(Sans, l.Text.TrimEnd(), 12) <= 468 - 72 + 0.01));
    }

    [Theory]
    [InlineData("center")]
    [InlineData("right")]
    public void CenterAndRightAlignment(string jc)
    {
        LayoutLine line = Lay(TestDocx.P("Hello", "<w:jc w:val=\"" + jc + "\"/>")).Pages[0].Paragraphs.Single().Lines.Single();
        double w = Width(Sans, "Hello", 12);
        double expected = jc == "center" ? 72 + ((468 - w) / 2) : 72 + 468 - w;
        Assert.Equal(expected, line.Bounds.X, 3);
    }

    [Fact]
    public void JustifiedLinesFillTheMeasureExceptTheLast()
    {
        string text = string.Join(' ', Enumerable.Repeat("justify these words please", 20));
        var lines = Lay(TestDocx.P(text, "<w:jc w:val=\"both\"/>")).Pages[0].Paragraphs.Single().Lines.ToList();
        for (int i = 0; i < lines.Count - 1; i++)
        {
            // The line's last word ends at the right margin; the space after it hangs.
            LayoutRun last = lines[i].Runs.Last(r => r.RunKind == LayoutRunKind.Text);
            double contentRight = last.Text.EndsWith(' ') ? last.ClusterX[^2] : last.Bounds.Right;
            Assert.Equal(72 + 468, contentRight, 0.5);
        }

        Assert.True(lines[^1].Bounds.Right < 72 + 468 - 20);
    }

    [Fact]
    public void LineSpacingRules()
    {
        (double asc, double desc) = Lm(Sans, 12);
        double single = asc + desc;
        LayoutLine two = Lay(TestDocx.P("x", "<w:spacing w:line=\"480\" w:lineRule=\"auto\"/>")).Pages[0].Paragraphs.Single().Lines.Single();
        Assert.Equal(single * 2, two.Bounds.Height, 3);
        LayoutLine exact = Lay(TestDocx.P("x", "<w:spacing w:line=\"400\" w:lineRule=\"exact\"/>")).Pages[0].Paragraphs.Single().Lines.Single();
        Assert.Equal(20, exact.Bounds.Height, 3);
        LayoutLine atLeast = Lay(TestDocx.P("x", "<w:spacing w:line=\"100\" w:lineRule=\"atLeast\"/>")).Pages[0].Paragraphs.Single().Lines.Single();
        Assert.Equal(single, atLeast.Bounds.Height, 3);
        LayoutLine atLeastBig = Lay(TestDocx.P("x", "<w:spacing w:line=\"600\" w:lineRule=\"atLeast\"/>")).Pages[0].Paragraphs.Single().Lines.Single();
        Assert.Equal(30, atLeastBig.Bounds.Height, 3);
    }

    [Fact]
    public void SpaceBeforeAndAfterAdd()
    {
        (double asc, double desc) = Lm(Sans, 12);
        string body = TestDocx.P("a", "<w:spacing w:before=\"200\" w:after=\"300\"/>") + TestDocx.P("b", "<w:spacing w:before=\"100\" w:after=\"0\"/>");
        var paragraphs = Lay(body).Pages[0].Paragraphs.ToList();
        // The first paragraph keeps its space before on the first page; the gap between is after + before.
        Assert.Equal(72 + 10, paragraphs[0].Bounds.Y, 3);
        Assert.Equal(72 + 10 + asc + desc + 15 + 5, paragraphs[1].Bounds.Y, 3);
    }

    [Fact]
    public void ContextualSpacingRemovesGapBetweenSameStyle()
    {
        (double asc, double desc) = Lm(Sans, 12);
        string pPr = "<w:spacing w:before=\"200\" w:after=\"300\"/><w:contextualSpacing/>";
        var paragraphs = Lay(TestDocx.P("a", pPr) + TestDocx.P("b", pPr)).Pages[0].Paragraphs.ToList();
        Assert.Equal(paragraphs[0].Bounds.Bottom, paragraphs[1].Bounds.Y, 3);
        Assert.Equal(asc + desc, paragraphs[0].Bounds.Height, 3);
    }

    [Fact]
    public void ExplicitLineBreakStartsANewLine()
    {
        string body = "<w:p><w:r><w:t>one</w:t><w:br/><w:t>two</w:t></w:r></w:p>";
        var lines = Lay(body).Pages[0].Paragraphs.Single().Lines.ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("one\n", lines[0].Text);
        Assert.Equal("two", lines[1].Text);
    }

    [Fact]
    public void CharacterSpacingScaleAndPosition()
    {
        double plain = Lay(TestDocx.P("abcd")).Pages[0].Paragraphs.Single().Lines.Single().Bounds.Width;
        double spaced = Lay(TestDocx.P("abcd", null, "<w:spacing w:val=\"40\"/>")).Pages[0].Paragraphs.Single().Lines.Single().Bounds.Width;
        Assert.Equal(plain + (4 * 2), spaced, 3);
        double scaled = Lay(TestDocx.P("abcd", null, "<w:w w:val=\"50\"/>")).Pages[0].Paragraphs.Single().Lines.Single().Bounds.Width;
        Assert.Equal(plain / 2, scaled, 3);
    }

    [Fact]
    public void CapsAndHiddenText()
    {
        LayoutLine caps = Lay(TestDocx.P("abc", null, "<w:caps/>")).Pages[0].Paragraphs.Single().Lines.Single();
        Assert.Equal(Width(Sans, "ABC", 12), caps.Bounds.Width, 3);
        string body = "<w:p><w:r><w:t>shown</w:t></w:r><w:r><w:rPr><w:vanish/></w:rPr><w:t>hidden</w:t></w:r></w:p>";
        // Hidden runs are removed by the hub's run properties, not by the layout; the layout keeps them out of the line.
        LayoutLine line = Lay(body).Pages[0].Paragraphs.Single().Lines.Single();
        Assert.Equal("shown", line.Text);
    }

    [Fact]
    public void KerningOnlyWithTheKernElement()
    {
        double off = Lay(TestDocx.P("AVAVAV")).Pages[0].Paragraphs.Single().Lines.Single().Bounds.Width;
        double on = Lay(TestDocx.P("AVAVAV", null, "<w:kern w:val=\"2\"/>")).Pages[0].Paragraphs.Single().Lines.Single().Bounds.Width;
        Assert.Equal(Width(Sans, "AVAVAV", 12), off, 3);
        Assert.True(on < off);
    }

    [Fact]
    public void ListLabelsPrecedeTheTextWithATabSuffix()
    {
        string numbering = "<w:numbering xmlns:w=\"" + TestDocx.W + "\"><w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"singleLevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>";
        string pPr = "<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>";
        var paragraphs = Lay(TestDocx.P("first", pPr) + TestDocx.P("second", pPr), numbering: numbering).Pages[0].Paragraphs.ToList();
        LayoutLine l0 = paragraphs[0].Lines.Single();
        LayoutRun[] runs = l0.Runs.ToArray();
        Assert.Equal("1.", paragraphs[0].Label);
        Assert.Equal(LayoutRunKind.Label, runs[0].RunKind);
        Assert.Equal(72 + 18, runs[0].Bounds.X, 3);
        Assert.Equal(LayoutRunKind.Tab, runs[1].RunKind);
        // The text starts at the left indent, 36 pt in.
        Assert.Equal(72 + 36, runs[2].Bounds.X, 3);
        Assert.Equal("2.", paragraphs[1].Label);
    }

    [Fact]
    public void RightToLeftParagraphFlowsFromTheRight()
    {
        var fonts = FontLibrary.CreateDefault();
        OneRender.Fonts.Noto.NotoArabicFonts.Register();
        fonts.AddOptionalPackages();
        using Document d = TestDocx.Open(TestDocx.P("\u0645\u0631\u062D\u0628\u0627", "<w:bidi/>"));
        LayoutParagraph p = LayoutEngine.Layout(d, new LayoutOptions { Fonts = fonts }).Pages[0].Paragraphs.Single();
        LayoutLine line = p.Lines.Single();
        Assert.True(p.IsRightToLeft);
        Assert.Equal(72 + 468, line.Bounds.Right, 0.5);
        Assert.All(line.Runs, r => Assert.Equal(1, r.BidiLevel));
    }

    [Fact]
    public void MixedDirectionLineKeepsLatinInOrder()
    {
        var fonts = FontLibrary.CreateDefault();
        OneRender.Fonts.Noto.NotoArabicFonts.Register();
        fonts.AddOptionalPackages();
        using Document d = TestDocx.Open(TestDocx.P("abc \u0645\u0631\u062D\u0628\u0627 def"));
        LayoutLine line = LayoutEngine.Layout(d, new LayoutOptions { Fonts = fonts }).Pages[0].Paragraphs.Single().Lines.Single();
        LayoutRun[] runs = line.Runs.ToArray();
        // Visual order: Latin, space, Arabic, space, Latin; the Arabic run sits between the two Latin ones.
        Assert.Equal("abc", runs[0].Text.Trim());
        Assert.Contains(runs, r => r.BidiLevel == 1);
        Assert.Equal("def", runs[^1].Text.Trim());
    }
}
