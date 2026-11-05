using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Documents.Styles;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class NumberingTests
{
    private static string Lvl(int i, string fmt, string text, long start = 1, string extra = "") =>
        $"<w:lvl w:ilvl=\"{i}\"><w:start w:val=\"{start}\"/><w:numFmt w:val=\"{fmt}\"/>{extra}<w:lvlText w:val=\"{text}\"/><w:lvlJc w:val=\"left\"/></w:lvl>";

    private static string Abs(int id, string levels) => $"<w:abstractNum w:abstractNumId=\"{id}\">{levels}</w:abstractNum>";

    private static string Num(int id, int abs, string extra = "") => $"<w:num w:numId=\"{id}\"><w:abstractNumId w:val=\"{abs}\"/>{extra}</w:num>";

    private static string Item(int num, int lvl, string text = "t") =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{lvl}\"/><w:numId w:val=\"{num}\"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string[] Labels(Document d) => d.Numbering.LabelBody().Select(l => l.Text).ToArray();

    [Fact]
    public void Decimal_Counts_AndMultilevelRestartsDeeperLevels()
    {
        string numbering = Abs(0, Lvl(0, "decimal", "%1.") + Lvl(1, "decimal", "%1.%2") + Lvl(2, "lowerLetter", "(%3)")) + Num(1, 0);
        using Document d = TestDocs.Open(Item(1, 0) + Item(1, 1) + Item(1, 1) + Item(1, 2) + Item(1, 2) + Item(1, 0) + Item(1, 1) + Item(1, 2), null, numbering);
        Assert.Equal(["1.", "1.1", "1.2", "(a)", "(b)", "2.", "2.1", "(a)"], Labels(d));
    }

    [Fact]
    public void Formats_Roman_Letters_Ordinal_Zero_AndRepeatedLetters()
    {
        string numbering = Abs(0, Lvl(0, "upperRoman", "%1") + Lvl(1, "ordinal", "%2") + Lvl(2, "decimalZero", "%3")) + Abs(1, Lvl(0, "lowerLetter", "%1)", 26)) + Num(1, 0) + Num(2, 1);
        using Document d = TestDocs.Open(Item(1, 0) + Item(1, 0) + Item(1, 0) + Item(1, 0) + Item(1, 1) + Item(1, 1) + Item(1, 2) + Item(2, 0) + Item(2, 0) + Item(2, 0), null, numbering);
        Assert.Equal(["I", "II", "III", "IV", "1st", "2nd", "01", "z)", "aa)", "bb)"], Labels(d));
    }

    [Fact]
    public void SameAbstractDefinition_ContinuesAcrossInstances_UnlessStartIsOverridden()
    {
        string numbering = Abs(0, Lvl(0, "decimal", "%1.")) + Num(1, 0) + Num(2, 0) + Num(3, 0, "<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"1\"/></w:lvlOverride>");
        using Document d = TestDocs.Open(Item(1, 0) + Item(1, 0) + Item(2, 0) + Item(3, 0) + Item(3, 0) + Item(1, 0), null, numbering);
        Assert.Equal(["1.", "2.", "3.", "1.", "2.", "3."], Labels(d));
    }

    [Fact]
    public void StartOverrideWithAValue_AndReplacementLevel()
    {
        string over = "<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"5\"/></w:lvlOverride><w:lvlOverride w:ilvl=\"1\">" + Lvl(1, "upperLetter", "%2.") + "</w:lvlOverride>";
        string numbering = Abs(0, Lvl(0, "decimal", "%1.") + Lvl(1, "decimal", "%1.%2")) + Num(1, 0) + Num(2, 0, over);
        using Document d = TestDocs.Open(Item(1, 0) + Item(2, 0) + Item(2, 1) + Item(2, 1), null, numbering);
        Assert.Equal(["1.", "5.", "A.", "B."], Labels(d));
    }

    [Fact]
    public void LegalNumbering_ShowsHigherLevelsAsArabic()
    {
        string numbering = Abs(0, Lvl(0, "upperRoman", "%1.") + Lvl(1, "decimal", "%1.%2", 1, "<w:isLgl/>")) + Num(1, 0);
        using Document d = TestDocs.Open(Item(1, 0) + Item(1, 0) + Item(1, 1), null, numbering);
        Assert.Equal(["I.", "II.", "2.1"], Labels(d));
    }

    [Fact]
    public void LvlRestart_ZeroNeverRestarts_AndOneRestartsOnlyAfterLevelOne()
    {
        string numbering = Abs(0, Lvl(0, "decimal", "%1.") + Lvl(1, "decimal", "%2.", 1, "<w:lvlRestart w:val=\"0\"/>")) +
                           Abs(1, Lvl(0, "decimal", "%1.") + Lvl(1, "decimal", "%2.") + Lvl(2, "decimal", "%3.", 1, "<w:lvlRestart w:val=\"1\"/>")) + Num(1, 0) + Num(2, 1);
        using Document d = TestDocs.Open(Item(1, 0) + Item(1, 1) + Item(1, 0) + Item(1, 1) + Item(2, 0) + Item(2, 1) + Item(2, 2) + Item(2, 1) + Item(2, 2) + Item(2, 0) + Item(2, 2), null, numbering);
        Assert.Equal(["1.", "1.", "2.", "2.", "1.", "1.", "1.", "2.", "2.", "2.", "1."], Labels(d));
    }

    [Fact]
    public void Bullets_KeepTheirTextAndSuffix()
    {
        string numbering = Abs(0, Lvl(0, "bullet", "\u2022", 1, "<w:suff w:val=\"space\"/>")) + Num(1, 0);
        using Document d = TestDocs.Open(Item(1, 0), null, numbering);
        ListLabel l = d.Numbering.LabelBody().Single();
        Assert.Equal("\u2022", l.Text);
        Assert.Equal("space", l.Suffix);
        Assert.True(l.Definition.IsBullet);
    }

    [Fact]
    public void StyleNumbering_LevelFromLinkedStyle_AndDirectOptOut()
    {
        string styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"H1\"><w:name w:val=\"H1\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr><w:ind w:left=\"100\"/></w:pPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"H2\"><w:name w:val=\"H2\"/><w:basedOn w:val=\"H1\"/><w:pPr><w:numPr><w:ilvl w:val=\"1\"/></w:numPr></w:pPr></w:style>";
        string numbering = Abs(0, Lvl(0, "decimal", "%1.", 1, "<w:pStyle w:val=\"H1\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr>") + Lvl(1, "decimal", "%1.%2", 1, "<w:pStyle w:val=\"H2\"/>")) + Num(1, 0);
        string body = "<w:p><w:pPr><w:pStyle w:val=\"H1\"/></w:pPr></w:p><w:p><w:pPr><w:pStyle w:val=\"H2\"/></w:pPr></w:p><w:p><w:pPr><w:pStyle w:val=\"H2\"/></w:pPr></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"H1\"/><w:numPr><w:numId w:val=\"0\"/></w:numPr></w:pPr></w:p><w:p><w:pPr><w:pStyle w:val=\"H1\"/></w:pPr></w:p>";
        using Document d = TestDocs.Open(body, styles, numbering);
        Assert.Equal(["1.", "1.1", "1.2", "2."], Labels(d));
        // The style supplies the list, so the style's own indent beats the level's (the level's properties come first).
        Assert.Equal(100, d.Body.Paragraphs().First().ResolvedProperties.LeftIndent);
        Assert.Equal(360, d.Body.Paragraphs().First().ResolvedProperties.HangingIndent);
        Assert.Null(d.Body.Paragraphs().ElementAt(3).Numbering);
    }

    [Fact]
    public void DirectNumbering_LevelPropertiesBeatTheParagraphStyle()
    {
        string styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:pPr><w:ind w:left=\"100\"/></w:pPr></w:style>";
        string numbering = Abs(0, Lvl(0, "decimal", "%1.", 1, "<w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr>")) + Num(1, 0);
        using Document d = TestDocs.Open(Item(1, 0), styles, numbering);
        ResolvedParagraphProperties r = d.Body.Paragraphs().Single().ResolvedProperties;
        Assert.Equal(720, r.LeftIndent);
        Assert.Equal(PropertySource.NumberingLevel, PropertySourceOf(r, "ind"));
    }

    private static PropertySource PropertySourceOf(ResolvedProperties r, string name) => r.Get(name)!.Attributes["left"].Origin.Source;

    [Fact]
    public void NumStyleLink_TakesLevelsFromTheLinkedList()
    {
        string styles = "<w:style w:type=\"numbering\" w:styleId=\"List1\"><w:name w:val=\"List1\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>";
        // The schema wants every abstractNum before the first num.
        string numbering = Abs(0, Lvl(0, "lowerRoman", "%1.")) + "<w:abstractNum w:abstractNumId=\"1\"><w:numStyleLink w:val=\"List1\"/></w:abstractNum>" + Num(1, 0) + Num(2, 1);
        using Document d = TestDocs.Open(Item(2, 0) + Item(2, 0), styles, numbering);
        Assert.Equal(["i.", "ii."], Labels(d));
    }

    [Fact]
    public void MissingListsAndLevels_AreReported_AndLeftUnnumbered()
    {
        var defects = new DefectCollection();
        string numbering = Abs(0, Lvl(0, "decimal", "%1.")) + Num(1, 0) + Num(2, 7);
        using Document d = TestDocs.Open(Item(9, 0) + Item(2, 0) + Item(1, 3) + Item(1, 0), null, numbering, null, defects);
        Assert.Equal(["1."], Labels(d));
        Assert.Contains(defects, x => x.Code == "docx.num-missing");
        Assert.Contains(defects, x => x.Code == "docx.num-abstract-missing");
        Assert.Contains(defects, x => x.Code == "docx.num-level-missing");
    }

    [Fact]
    public void AddingLists_CreatesThePart_InSchemaOrder_AndLabelsFollow()
    {
        var pkg = TestDocs.Package(Item(1, 0) + Item(1, 1) + Item(1, 0));
        using var d = Document.FromPackage(pkg);
        long a = d.Numbering.AddAbstractNumbering([new(Format: "decimal", Text: "%1."), new(Format: "lowerLetter", Text: "%2)", LeftIndent: 1440)]);
        long n1 = d.Numbering.AddInstance(a);
        long a2 = d.Numbering.AddAbstractNumbering([new("bullet", "-")]);
        long n2 = d.Numbering.AddInstance(a2, new Dictionary<int, long> { [0] = 3 });
        Assert.Equal([0L, 1L], [a, a2]);
        Assert.Equal([1L, 2L], [n1, n2]);
        string xml = d.Numbering.Markup.Node.OuterXml;
        Assert.True(xml.IndexOf("w:abstractNumId=\"1\"", StringComparison.Ordinal) < xml.IndexOf("<w:num ", StringComparison.Ordinal));
        Assert.Equal(["1.", "a)", "2."], Labels(d));
        d.CommitChanges();
        Assert.NotNull(pkg.FindPart("/word/numbering.xml"));
        d.Body.Paragraphs().Last().SetNumbering(n2);
        Assert.Equal(["1.", "a)", "-"], Labels(d));
        d.Body.Paragraphs().First().ClearNumbering();
        Assert.Equal(["a)", "-"], Labels(d));
    }

    [Theory]
    [InlineData("upperRoman", 1994, "MCMXCIV", false)]
    [InlineData("lowerRoman", 9, "ix", false)]
    [InlineData("upperLetter", 27, "AA", false)]
    [InlineData("ordinal", 11, "11th", false)]
    [InlineData("ordinal", 22, "22nd", false)]
    [InlineData("decimalZero", 7, "07", false)]
    [InlineData("decimalEnclosedCircle", 3, "\u2462", false)]
    [InlineData("cardinalText", 21, "Twenty-one", true)]
    [InlineData("ordinalText", 3, "Third", true)]
    [InlineData("japaneseCounting", 4, "4", true)]
    public void NumberFormats(string format, long value, string expected, bool needsWord)
    {
        FormattedNumber f = NumberFormatter.Format(format, value);
        Assert.Equal(expected, f.Text);
        Assert.Equal(needsWord, f.NeedsWordOutput);
    }

    [Fact]
    public void BrokenNumStyleLink_IsReported()
    {
        var defects = new DefectCollection();
        string numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:numStyleLink w:val=\"Nope\"/></w:abstractNum>" + Num(1, 0);
        using Document d = TestDocs.Open(Item(1, 0), null, numbering, null, defects);
        Assert.Empty(Labels(d));
        Assert.Contains(defects, x => x.Code == "docx.num-link-broken");
    }
}
