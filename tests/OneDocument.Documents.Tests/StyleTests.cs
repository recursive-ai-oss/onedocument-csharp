using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Styles;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class StyleTests
{
    private const string Theme = "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:themeElements>" +
        "<a:clrScheme name=\"t\"><a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"44546A\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2><a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1></a:clrScheme>" +
        "<a:fontScheme name=\"t\"><a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme></a:themeElements></a:theme>";

    private const string Styles =
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:asciiTheme=\"minorHAnsi\" w:hAnsiTheme=\"minorHAnsi\"/><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/></w:style>" +
        "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/>" +
        "<w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"0\"/><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"2F5496\" w:themeColor=\"accent1\"/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Strong\"><w:name w:val=\"Strong\"/><w:rPr><w:b/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Emph\"><w:name w:val=\"Emph\"/><w:basedOn w:val=\"Strong\"/><w:rPr><w:i/></w:rPr></w:style>" +
        "<w:style w:type=\"table\" w:styleId=\"Grid\"><w:name w:val=\"Grid\"/><w:basedOn w:val=\"TableNormal\"/><w:tblPr><w:tblStyleRowBandSize w:val=\"1\"/></w:tblPr>" +
        "<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/></w:rPr></w:tblStylePr><w:tblStylePr w:type=\"band1Horz\"><w:rPr><w:i/></w:rPr></w:tblStylePr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"LoopA\"><w:name w:val=\"LoopA\"/><w:basedOn w:val=\"LoopB\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"LoopB\"><w:name w:val=\"LoopB\"/><w:basedOn w:val=\"LoopA\"/></w:style>";

    private static Document Open(string body, DefectCollection? d = null) => TestDocs.Open(body, Styles, null, Theme, d);

    private static Paragraph P(Document d, int i = 0) => d.Body.Paragraphs().ElementAt(i);

    [Fact]
    public void ParagraphProperties_InheritThroughBasedOn_WithOrigins()
    {
        using Document d = Open("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:jc w:val=\"center\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        ResolvedParagraphProperties r = P(d).ResolvedProperties;
        Assert.Equal("Heading1", r.StyleId);
        Assert.Equal(0, r.SpaceAfter);                    // Heading1 overrides the default 160
        Assert.Equal(240, r.SpaceBefore);
        Assert.Equal(259, r.LineSpacing);                 // not set by Heading1: comes from the defaults, attribute by attribute
        Assert.Equal(PropertySource.DocumentDefaults, r.Get("spacing")!.Attributes["line"].Origin.Source);
        Assert.Equal(PropertySource.ParagraphStyle, r.Get("spacing")!.Attributes["before"].Origin.Source);
        Assert.Equal("Heading1", r.Get("spacing")!.Attributes["before"].Origin.StyleId);
        Assert.Equal("center", r.Justification);
        Assert.Equal(PropertySource.DirectFormatting, r.Get("jc")!.Origin.Source);
        Assert.True(r.KeepNext);
    }

    [Fact]
    public void ParagraphWithoutStyle_GetsTheDefaultParagraphStyle()
    {
        using Document d = Open("<w:p><w:r><w:t>x</w:t></w:r></w:p>");
        Assert.Equal("Normal", P(d).ResolvedProperties.StyleId);
        Assert.Equal(160, P(d).ResolvedProperties.SpaceAfter);
    }

    [Fact]
    public void DirectIndent_CancelsInheritedHanging()
    {
        using Document d = Open("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:ind w:firstLine=\"100\"/></w:pPr></w:p>");
        ResolvedParagraphProperties r = P(d).ResolvedProperties;
        Assert.Equal(720, r.LeftIndent);
        Assert.Equal(100, r.FirstLineIndent);
        Assert.Null(r.HangingIndent);
    }

    [Fact]
    public void Toggle_ParagraphAndCharacterStyle_XorEachOther()
    {
        using Document d = Open(
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>a</w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"Strong\"/></w:rPr><w:t>b</w:t></w:r>" +
            "<w:r><w:rPr><w:rStyle w:val=\"Strong\"/><w:b/></w:rPr><w:t>c</w:t></w:r><w:r><w:rPr><w:b w:val=\"0\"/></w:rPr><w:t>d</w:t></w:r></w:p>" +
            "<w:p><w:r><w:rPr><w:rStyle w:val=\"Strong\"/></w:rPr><w:t>e</w:t></w:r></w:p>");
        Run[] runs = d.Body.Paragraphs().SelectMany(p => p.Runs()).ToArray();
        Assert.True(runs[0].ResolvedProperties.Bold);     // heading's bold alone
        Assert.False(runs[1].ResolvedProperties.Bold);    // heading bold XOR strong bold
        Assert.True(runs[2].ResolvedProperties.Bold);     // direct formatting sets it outright
        Assert.False(runs[3].ResolvedProperties.Bold);    // direct off
        Assert.True(runs[4].ResolvedProperties.Bold);     // strong alone
    }

    [Fact]
    public void CharacterStyleChain_AppliesBaseFirst()
    {
        using Document d = Open("<w:p><w:r><w:rPr><w:rStyle w:val=\"Emph\"/></w:rPr><w:t>x</w:t></w:r></w:p>");
        ResolvedRunProperties r = P(d).Runs().Single().ResolvedProperties;
        Assert.True(r.Bold);                              // from Strong, the base
        Assert.True(r.Italic);                            // from Emph
        Assert.Equal("Strong", r.Get("b")!.Origin.StyleId);
        Assert.Equal("Emph", r.CharacterStyleId);
    }

    [Fact]
    public void ThemeFontsAndColours_Resolve()
    {
        using Document d = Open("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        ResolvedRunProperties r = P(d).Runs().Single().ResolvedProperties;
        Assert.Equal("Calibri", r.Fonts.Ascii);
        Assert.Equal("4472C4", r.Color);                  // the theme colour wins over the cached value
        Assert.Equal(32, r.SizeHalfPoints);               // heading beats the default 22
        Assert.Equal(PropertySource.ParagraphStyle, r.Get("sz")!.Origin.Source);
    }

    [Fact]
    public void DirectFont_ReplacesInheritedThemeReference()
    {
        using Document d = Open("<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Arial\"/></w:rPr><w:t>x</w:t></w:r></w:p>");
        ResolvedFonts f = P(d).Runs().Single().ResolvedProperties.Fonts;
        Assert.Equal("Arial", f.Ascii);
        Assert.Equal("Calibri", f.HighAnsi);              // only ascii was replaced
    }

    [Fact]
    public void TableStyle_RegionsApplyByPosition_AndLoseToParagraphStyle()
    {
        string row(string text) => "<w:tr><w:tc><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:tc></w:tr>";
        using Document d = Open("<w:tbl><w:tblPr><w:tblStyle w:val=\"Grid\"/><w:tblLook w:firstRow=\"1\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr>" + row("h") + row("a") + row("b") + "</w:tbl>");
        ResolvedRunProperties[] r = d.Body.Paragraphs().Select(p => p.Runs().Single().ResolvedProperties).ToArray();
        Assert.True(r[0].Bold);                           // header row
        Assert.False(r[0].Italic);                        // the header row is not banded
        Assert.True(r[1].Italic);                         // first data row is band1
        Assert.False(r[2].Italic);                        // band2 has no formatting
        Assert.Equal(PropertySource.TableStyle, r[0].Get("b")!.Origin.Source);
    }

    [Fact]
    public void StyleCycleAndUnknownStyle_AreReportedOnce()
    {
        var defects = new DefectCollection();
        using Document d = Open("<w:p><w:pPr><w:pStyle w:val=\"LoopA\"/></w:pPr></w:p><w:p><w:pPr><w:pStyle w:val=\"Nope\"/></w:pPr></w:p><w:p><w:pPr><w:pStyle w:val=\"Nope\"/></w:pPr></w:p>", defects);
        _ = P(d, 0).ResolvedProperties;
        _ = P(d, 0).ResolvedProperties;
        _ = P(d, 1).ResolvedProperties;
        _ = P(d, 2).ResolvedProperties;
        Assert.Single(defects, x => x.Code == "docx.style-cycle");
        Assert.Single(defects, x => x.Code == "docx.style-unknown");
        Assert.Equal(DefectLocationKind.Part, defects.First(x => x.Code == "docx.style-cycle").Location.Kind);
    }

    [Fact]
    public void LookupByIdAndNameAndDefaults()
    {
        using Document d = Open("");
        Assert.Equal("Heading1", d.Styles.FindByName("Heading 1")!.Id);   // names compare ignoring case
        Assert.Equal("Normal", d.Styles.Default(StyleType.Paragraph)!.Id);
        Assert.Equal(["Normal", "Heading1"], d.Styles.Find("Heading1")!.Lineage.Select(s => s.Id).ToArray());
        Assert.Equal("Normal", d.Styles.Find("Heading1")!.NextId);
    }

    [Fact]
    public void AddStyle_CreatesThePartWhenMissing_AndOnlyTouchesWhatItMust()
    {
        var pkg = TestDocs.Package("<w:p><w:r><w:t>x</w:t></w:r></w:p>");
        using var d = Document.FromPackage(pkg);
        Assert.False(d.Styles.IsPresent);
        StyleDefinition s = d.Styles.AddStyle("Quote1", "Quote", StyleType.Paragraph);
        d.Styles.Apply(P(d), s);
        d.CommitChanges();
        Assert.NotNull(pkg.FindPart("/word/styles.xml"));
        Assert.Contains(pkg.GetRelationships("/word/document.xml"), r => r.Type.EndsWith("/styles", StringComparison.Ordinal));
        string xml = System.Text.Encoding.UTF8.GetString(pkg.FindPart("/word/styles.xml")!.GetBytes().Span);
        Assert.Contains("w:styleId=\"Quote1\"", xml);
        Assert.Throws<ArgumentException>(() => d.Styles.AddStyle("Quote1", "Again", StyleType.Paragraph));
        Assert.Throws<ArgumentException>(() => d.Styles.Apply(P(d).Runs().FirstOrDefault()!, s));
    }

    [Fact]
    public void LatentStylesAndDocDefaults_AreReadable()
    {
        var pkg = TestDocs.Package("", "<w:latentStyles w:defLockedState=\"0\"><w:lsdException w:name=\"Normal\" w:uiPriority=\"0\" w:qFormat=\"1\"/></w:latentStyles>");
        using var d = Document.FromPackage(pkg);
        LatentStyle l = d.Styles.LatentStyles.Single();
        Assert.Equal("Normal", l.Name);
        Assert.Equal(true, l.QuickStyle);
        Assert.Null(d.Styles.DefaultRunProperties);
    }

    [Fact]
    public void DuplicateIdAndMissingBase_AreReported()
    {
        var defects = new DefectCollection();
        string styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Normal\"><w:name w:val=\"Second\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"X\"><w:name w:val=\"X\"/><w:basedOn w:val=\"Gone\"/></w:style>";
        using Document d = TestDocs.Open("<w:p><w:pPr><w:pStyle w:val=\"X\"/></w:pPr></w:p>", styles, null, null, defects);
        _ = P(d).ResolvedProperties;
        Assert.Equal("Normal", d.Styles.Find("Normal")!.Name);
        Assert.Single(defects, x => x.Code == "docx.style-duplicate-id");
        Assert.Single(defects, x => x.Code == "docx.style-base-missing");
    }

    // The generated precedence case prec-styles.docx; every expectation below is what Word 365 (16.0.20527) reported for the same file.
    [Fact]
    public void WordPrecedenceCase_AgreesWithWord()
    {
        const string styles = "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\"/><w:sz w:val=\"28\"/></w:rPr></w:rPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"A\"><w:name w:val=\"A\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:b/><w:sz w:val=\"40\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"B\"><w:name w:val=\"B\"/><w:basedOn w:val=\"A\"/><w:rPr><w:i/><w:color w:val=\"FF0000\"/></w:rPr></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"Bold\"><w:name w:val=\"Bold\"/><w:rPr><w:b/></w:rPr></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"Green\"><w:name w:val=\"Green\"/><w:rPr><w:color w:val=\"00FF00\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Ind\"><w:name w:val=\"Ind\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"720\" w:firstLine=\"360\"/><w:spacing w:before=\"240\" w:after=\"120\"/></w:pPr></w:style>";
        using Document d = TestDocs.Open(
            "<w:p><w:r><w:t>plain</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"B\"/></w:pPr><w:r><w:t>chain</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"A\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"Bold\"/></w:rPr><w:t>toggle</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"B\"/></w:pPr><w:r><w:rPr><w:color w:val=\"0000FF\"/><w:i w:val=\"0\"/></w:rPr><w:t>direct</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"B\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"Green\"/></w:rPr><w:t>charstyle</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"A\"/></w:pPr><w:r><w:rPr><w:b w:val=\"0\"/></w:rPr><w:t>boldoff</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"Ind\"/></w:pPr><w:r><w:t>indent</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"Ind\"/><w:ind w:left=\"1440\"/></w:pPr><w:r><w:t>indent2</w:t></w:r></w:p>", styles);
        (long Size, bool Bold, bool Italic, string? Color)[] want =
        [
            (28, false, false, null), (40, true, true, "FF0000"), (40, false, false, null), (40, true, false, "0000FF"),
            (40, true, true, "00FF00"), (40, false, false, null), (28, false, false, null), (28, false, false, null),
        ];
        Paragraph[] ps = d.Body.Paragraphs().ToArray();
        for (int i = 0; i < want.Length; i++)
        {
            ResolvedRunProperties r = ps[i].Runs().Single().ResolvedProperties;
            Assert.Equal(want[i].Size, r.SizeHalfPoints);
            Assert.Equal(want[i].Bold, r.Bold);
            Assert.Equal(want[i].Italic, r.Italic);
            Assert.Equal(want[i].Color, r.Color);
        }

        // Word: space after 8 pt (160) with no pPrDefault; style spacing and indentation win, direct left indent keeps the style's first line.
        Assert.Equal(160, ps[0].ResolvedProperties.SpaceAfter);
        Assert.Equal(120, ps[6].ResolvedProperties.SpaceAfter);
        Assert.Equal(240, ps[6].ResolvedProperties.SpaceBefore);
        Assert.Equal((720, 360), (ps[6].ResolvedProperties.LeftIndent, ps[6].ResolvedProperties.FirstLineIndent));
        Assert.Equal((1440, 360), (ps[7].ResolvedProperties.LeftIndent, ps[7].ResolvedProperties.FirstLineIndent));
    }
}
