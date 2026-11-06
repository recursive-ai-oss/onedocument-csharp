using System.Text;
using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>One positive and one negative document per accessibility rule that applies to word-processing documents.</summary>
public class AccessibilityTests
{
    private const string WpNs = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string ANs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static string Drawing(string docPrAttrs, string holder = "inline", string uri = "http://schemas.openxmlformats.org/drawingml/2006/picture", string extra = "") =>
        $"<w:r><w:drawing><wp:{holder} xmlns:wp=\"{WpNs}\"><wp:extent cx=\"100\" cy=\"100\"/><wp:docPr id=\"7\" name=\"Picture 7\" {docPrAttrs}/>" +
        $"<a:graphic xmlns:a=\"{ANs}\"><a:graphicData uri=\"{uri}\">{extra}</a:graphicData></a:graphic></wp:{holder}></w:drawing></w:r>";

    private static string P(string text) => $"<w:p><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static string Cell(string text = "x", string pr = "") =>
        $"<w:tc>{(pr.Length == 0 ? "" : "<w:tcPr>" + pr + "</w:tcPr>")}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Row(string cells, bool header = false) => "<w:tr>" + (header ? "<w:trPr><w:tblHeader/></w:trPr>" : "") + cells + "</w:tr>";

    private static string Tbl(params string[] rows) => "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1\"/><w:gridCol w:w=\"1\"/></w:tblGrid>" + string.Concat(rows) + "</w:tbl>";

    private const string Styles =
        "<w:docDefaults><w:rPrDefault><w:rPr><w:lang w:val=\"en-US\"/></w:rPr></w:rPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:pPr><w:outlineLvl w:val=\"1\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading3\"><w:name w:val=\"heading 3\"/><w:pPr><w:outlineLvl w:val=\"2\"/></w:pPr></w:style>";

    // The test packages live in memory only; a real package is what saves to a zip that Office can open.
    private static Document Open(string body, string? styles = null)
    {
        InMemoryPackage src = TestDocs.Package(body, styles);
        return Real(src);
    }

    private static Document Real(InMemoryPackage src)
    {
        OpcPackage dst = OpcPackage.Create();
        dst.SetRelationships(null, src.GetRelationships(null));
        foreach (IPackagePart part in src.Parts)
        {
            dst.AddPart(part.Name, part.ContentType, part.GetBytes().ToArray());
            IReadOnlyList<Relationship> rels = src.GetRelationships(part.Name);
            if (rels.Count > 0)
            {
                dst.SetRelationships(part.Name, rels);
            }
        }

        return Document.FromPackage(dst, ownsPackage: true);
    }

    // With ONEDOCUMENT_A11Y_FIXTURES set, each rule case is also written as a file, for checking against Office's own checker.
    private static void Emit(Document doc, string? name)
    {
        if (name is not null && Environment.GetEnvironmentVariable("ONEDOCUMENT_A11Y_FIXTURES") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            doc.SaveAs(Path.Combine(dir, name + ".docx"));
        }
    }

    private static AccessibilityReport Check(string body, string? styles = Styles, bool title = true, string? emit = null)
    {
        using Document doc = Open(body, styles);
        if (title)
        {
            doc.Properties.Core.Title = "A title";
        }

        Emit(doc, emit);
        return doc.CheckAccessibility();
    }

    private static string[] Ids(AccessibilityReport r) => [.. r.Issues.Select(i => i.RuleId)];

    private static void Fires(string ruleId, string positive, string negative, Action<AccessibilityIssue>? located = null, string? styles = Styles)
    {
        AccessibilityReport pos = Check(positive, styles, emit: ruleId + ".positive");
        AccessibilityIssue? hit = pos.Issues.FirstOrDefault(i => i.RuleId == ruleId);
        Assert.True(hit is not null, ruleId + " did not fire; got: " + string.Join(", ", Ids(pos)));
        located?.Invoke(hit);
        AccessibilityReport neg = Check(negative, styles, emit: ruleId + ".negative");
        Assert.DoesNotContain(ruleId, Ids(neg));
    }

    [Fact]
    public void AltTextMissing_AndDecorative()
    {
        Fires("alt-text.missing", P("a") + "<w:p>" + Drawing("") + "</w:p>", P("a") + "<w:p>" + Drawing("descr=\"A cat\"") + "</w:p>", hit =>
        {
            Assert.Equal(AccessibilityCategory.Error, hit.Category);
            Assert.Equal(2, hit.Location.Paragraph);
            Assert.Equal(1, hit.Location.Run);
            Assert.Equal("body", hit.Location.Story);
            Assert.Equal("Picture 7", hit.ObjectName);
            Assert.Equal(AccessibilityFixKind.MarkDecorative, hit.Fix);
        });
        const string deco = "<a:extLst xmlns:a=\"" + ANs + "\"><a:ext uri=\"{C183D7F6-B498-43B3-948B-1728B52AA6E4}\"><adec:decorative xmlns:adec=\"http://schemas.microsoft.com/office/drawing/2017/decorative\" val=\"1\"/></a:ext></a:extLst>";
        AccessibilityReport r = Check("<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"" + WpNs + "\"><wp:docPr id=\"1\" name=\"x\">" + deco + "</wp:docPr><a:graphic xmlns:a=\"" + ANs + "\"><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"/></a:graphic></wp:inline></w:drawing></w:r></w:p>");
        Assert.DoesNotContain("alt-text.missing", Ids(r));
    }

    [Fact]
    public void AltTextFileName() =>
        Fires("alt-text.file-name", "<w:p>" + Drawing("descr=\"IMG_0042.jpg\"") + "</w:p>", "<w:p>" + Drawing("descr=\"Photo of a harbour\"") + "</w:p>");

    [Fact]
    public void AltTextSuggested() =>
        Fires("alt-text.suggested", "<w:p>" + Drawing("descr=\"A cat on a sofa. Description automatically generated\"") + "</w:p>", "<w:p>" + Drawing("descr=\"A cat on a sofa\"") + "</w:p>");

    [Fact]
    public void Alt_ChartDiagramGroupAndTextBox()
    {
        foreach (string uri in new[] { "http://schemas.openxmlformats.org/drawingml/2006/chart", "http://schemas.openxmlformats.org/drawingml/2006/diagram", "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup" })
        {
            Assert.Contains("alt-text.missing", Ids(Check("<w:p>" + Drawing("", uri: uri) + "</w:p>")));
        }

        // A shape that holds text is a text box: its words describe it.
        const string tb = "<w:txbxContent xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>Words</w:t></w:r></w:p></w:txbxContent>";
        Assert.DoesNotContain("alt-text.missing", Ids(Check("<w:p>" + Drawing("", uri: "http://schemas.microsoft.com/office/word/2010/wordprocessingShape", extra: tb) + "</w:p>")));
        Assert.Contains("alt-text.missing", Ids(Check("<w:p>" + Drawing("", uri: "http://schemas.microsoft.com/office/word/2010/wordprocessingShape") + "</w:p>")));
    }

    [Fact]
    public void FloatingObject() =>
        Fires("floating.object", "<w:p>" + Drawing("descr=\"x y\"", "anchor") + "</w:p>", "<w:p>" + Drawing("descr=\"x y\"") + "</w:p>", hit => Assert.Equal(AccessibilityCategory.Warning, hit.Category));

    [Fact]
    public void AlternateContent_ChoiceOnly()
    {
        string ac = "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice Requires=\"wps\">" + Drawing("").Replace("<w:r>", "").Replace("</w:r>", "")
            + "</mc:Choice><mc:Fallback><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\" id=\"s\"><v:imagedata r:id=\"rId9\"/></v:shape></w:pict></mc:Fallback></mc:AlternateContent>";
        AccessibilityReport r = Check("<w:p><w:r>" + ac + "</w:r></w:p>");
        Assert.Single(r.Issues, i => i.RuleId == "alt-text.missing");
    }

    [Fact]
    public void VmlPicture_AltAttribute()
    {
        static string Vml(string alt) => "<w:p><w:r><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\" id=\"s1\" " + alt + "><v:imagedata/></v:shape></w:pict></w:r></w:p>";
        Fires("alt-text.missing", Vml(""), Vml("alt=\"A lighthouse\""));
    }

    [Fact]
    public void TableHeaderRow()
    {
        Fires("table.header-row", Tbl(Row(Cell() + Cell()), Row(Cell() + Cell())), Tbl(Row(Cell() + Cell(), header: true), Row(Cell() + Cell())), hit =>
        {
            Assert.Equal(AccessibilityCategory.Error, hit.Category);
            Assert.Equal(1, hit.Location.Table);
            Assert.Equal(AccessibilityFixKind.SetHeaderRow, hit.Fix);
        });
    }

    [Fact]
    public void TableMergedCells() =>
        Fires("table.merged-cells", Tbl(Row(Cell("a", "<w:gridSpan w:val=\"2\"/>"), true), Row(Cell() + Cell())), Tbl(Row(Cell() + Cell(), true), Row(Cell() + Cell())));

    [Fact]
    public void TableVerticalMerge() =>
        Assert.Contains("table.merged-cells", Ids(Check(Tbl(Row(Cell("a", "<w:vMerge w:val=\"restart\"/>") + Cell(), true), Row(Cell("", "<w:vMerge/>") + Cell())))));

    [Fact]
    public void TableIrregularGrid() =>
        Fires("table.irregular-grid", Tbl(Row(Cell() + Cell(), true), Row(Cell())), Tbl(Row(Cell() + Cell(), true), Row(Cell() + Cell())));

    [Fact]
    public void TableNested()
    {
        string inner = Tbl(Row(Cell() + Cell(), true));
        string nested = "<w:tc><w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1\"/></w:tblGrid>" + Row(Cell(), true) + "</w:tbl><w:p/></w:tc>";
        _ = inner;
        Fires("table.nested", Tbl(Row(nested + Cell(), true)), Tbl(Row(Cell() + Cell(), true)));
    }

    [Fact]
    public void TableBlankRowAndColumn()
    {
        Fires("table.blank-cells", Tbl(Row(Cell("h") + Cell("h"), true), Row(Cell("") + Cell("")), Row(Cell("a") + Cell("b"))), Tbl(Row(Cell("h") + Cell("h"), true), Row(Cell("a") + Cell("b"))),
            hit => Assert.Contains("Row 2", hit.Message));
        AccessibilityReport col = Check(Tbl(Row(Cell("h") + Cell(""), true), Row(Cell("a") + Cell(""))));
        Assert.Contains(col.Issues, i => i.RuleId == "table.blank-cells" && i.Message.Contains("Column 2", StringComparison.Ordinal));
    }

    [Fact]
    public void ContrastLow()
    {
        static string Run(string rpr) => $"<w:p><w:r><w:rPr>{rpr}</w:rPr><w:t>Hello</w:t></w:r></w:p>";
        Fires("contrast.low", Run("<w:color w:val=\"BFBFBF\"/>"), Run("<w:color w:val=\"000000\"/>"), hit =>
        {
            Assert.Equal(AccessibilityCategory.Warning, hit.Category);
            Assert.Equal(1, hit.Location.Run);
            Assert.Contains("BFBFBF", hit.Message);
        });
        // Dark text on a dark shading fails; the same text on a light shading passes.
        Assert.Contains("contrast.low", Ids(Check(Run("<w:color w:val=\"333333\"/><w:shd w:val=\"clear\" w:fill=\"222222\"/>"))));
        Assert.DoesNotContain("contrast.low", Ids(Check(Run("<w:color w:val=\"333333\"/><w:shd w:val=\"clear\" w:fill=\"FFFFFF\"/>"))));
        // Highlight is a background, and large text needs only 3:1.
        Assert.Contains("contrast.low", Ids(Check(Run("<w:color w:val=\"FFFF00\"/><w:highlight w:val=\"white\"/>"))));
        Assert.DoesNotContain("contrast.low", Ids(Check(Run("<w:color w:val=\"808080\"/><w:sz w:val=\"48\"/>"))));
        Assert.Contains("contrast.low", Ids(Check(Run("<w:color w:val=\"808080\"/>"))));
        // Table cell shading is the background of the text in it.
        Assert.Contains("contrast.low", Ids(Check(Tbl(Row("<w:tc><w:tcPr><w:shd w:val=\"clear\" w:fill=\"000000\"/></w:tcPr><w:p><w:r><w:rPr><w:color w:val=\"111111\"/></w:rPr><w:t>x</w:t></w:r></w:p></w:tc>" + Cell(), true)))));
    }

    [Fact]
    public void DocumentTitleAndLanguage()
    {
        AccessibilityReport r = Check(P("x"), styles: null, title: false, emit: "document.title.positive");
        Check(P("x"), styles: null, title: true, emit: "document.language.positive");
        Assert.Contains("document.title", Ids(r));
        Assert.Contains("document.language", Ids(r));
        AccessibilityReport ok = Check(P("x"), Styles, title: true, emit: "document.title.negative");
        Check(P("x"), Styles, title: true, emit: "document.language.negative");
        Assert.DoesNotContain("document.title", Ids(ok));
        Assert.DoesNotContain("document.language", Ids(ok));
        Assert.Equal(AccessibilityCategory.Tip, r.Issues.First(i => i.RuleId == "document.title").Category);
    }

    [Fact]
    public void ContentControlTitle()
    {
        static string Sdt(string pr) => $"<w:sdt><w:sdtPr>{pr}</w:sdtPr><w:sdtContent><w:p><w:r><w:t>v</w:t></w:r></w:p></w:sdtContent></w:sdt>";
        Fires("content-control.title", Sdt("<w:tag w:val=\"t\"/>"), Sdt("<w:alias w:val=\"Customer name\"/>"), hit => Assert.Equal(AccessibilityCategory.Error, hit.Category));
        Assert.DoesNotContain("content-control.title", Ids(Check(Sdt("<w:docPartObj><w:docPartGallery w:val=\"Table of Contents\"/></w:docPartObj>"))));
    }

    [Fact]
    public void HeadingsMissing_LongDocumentOnly()
    {
        string longText = string.Join(" ", Enumerable.Repeat("word", 600));
        Fires("headings.missing", P(longText), "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Title</w:t></w:r></w:p>" + P(longText));
        Assert.DoesNotContain("headings.missing", Ids(Check(P("short text"))));
    }

    [Fact]
    public void HeadingsSkipped()
    {
        static string H(int level) => $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/></w:pPr><w:r><w:t>h</w:t></w:r></w:p>";
        Fires("headings.skipped", H(1) + H(3), H(1) + H(2) + H(3) + H(2), hit => Assert.Equal(2, hit.Location.Paragraph));
    }

    [Fact]
    public void SpacingBlankParagraphs() =>
        Fires("spacing.blank-paragraphs", P("a") + "<w:p/><w:p/>" + P("b"), P("a") + "<w:p/>" + P("b") + "<w:p/>" + P("c"), hit => Assert.Equal(3, hit.Location.Paragraph));

    [Fact]
    public void SpacingBlankCharacters() =>
        Fires("spacing.blank-characters", P("one     two"), P("one two. Three four."));

    [Fact]
    public void HyperlinkText()
    {
        static string Link(string text) => $"<w:p><w:hyperlink w:anchor=\"bm\"><w:r><w:t>{text}</w:t></w:r></w:hyperlink></w:p>";
        Fires("hyperlink.text", Link("click here"), Link("Quarterly report"));
        Assert.Contains("hyperlink.text", Ids(Check(Link("https://example.org/a"))));
    }

    [Fact]
    public void WatermarkImage_InHeader()
    {
        InMemoryPackage p = TestDocs.Package(P("body") + "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rId11\"/></w:sectPr>", $"<w:docDefaults><w:rPrDefault><w:rPr><w:lang w:val=\"en-US\"/></w:rPr></w:rPrDefault></w:docDefaults>");
        p.AddPart("/word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml", Encoding.UTF8.GetBytes(
            $"<w:hdr xmlns:w=\"{TestDocs.WNs}\" xmlns:v=\"urn:schemas-microsoft-com:vml\"><w:p><w:r><w:pict><v:shape id=\"PowerPlusWaterMarkObject1\"><v:imagedata/></v:shape></w:pict></w:r></w:p></w:hdr>"));
        var rels = new List<Relationship>(p.GetRelationships("/word/document.xml")) { new("rId11", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header", "header1.xml") };
        p.SetRelationships("/word/document.xml", rels);
        using Document doc = Real(p);
        Emit(doc, "watermark.image.positive");
        AccessibilityReport r = doc.CheckAccessibility();
        AccessibilityIssue w = Assert.Single(r.Issues, i => i.RuleId == "watermark.image");
        Assert.Equal("header", w.Location.Story);
        Assert.DoesNotContain("alt-text.missing", Ids(r));
    }

    [Fact]
    public void Fixers_EditOnlyWhenCalled()
    {
        using Document doc = Open("<w:p>" + Drawing("") + "</w:p>" + Tbl(Row(Cell() + Cell()), Row(Cell() + Cell())), Styles);
        doc.Properties.Core.Title = "t";
        AccessibilityReport before = doc.CheckAccessibility();
        Assert.False(doc.HasUncommittedChanges && false);
        AccessibilityIssue deco = before.Issues.First(i => i.Fix == AccessibilityFixKind.MarkDecorative);
        AccessibilityIssue head = before.Issues.First(i => i.Fix == AccessibilityFixKind.SetHeaderRow);
        Assert.True(doc.ApplyAccessibilityFix(deco).Applied);
        Assert.True(doc.ApplyAccessibilityFix(head).Applied);
        Assert.False(doc.ApplyAccessibilityFix(head).Applied);
        AccessibilityReport after = doc.CheckAccessibility();
        Assert.DoesNotContain("alt-text.missing", Ids(after));
        Assert.DoesNotContain("table.header-row", Ids(after));
    }

    [Fact]
    public void Fixers_TitleAndLanguage()
    {
        using Document doc = Open(P("x"), "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault></w:docDefaults>");
        AccessibilityReport before = doc.CheckAccessibility();
        Assert.Contains("document.title", Ids(before));
        Assert.Contains("document.language", Ids(before));
        Assert.True(doc.SetAccessibilityTitle("Report").Applied);
        Assert.True(doc.SetAccessibilityLanguage("en-GB").Applied);
        AccessibilityReport after = doc.CheckAccessibility();
        Assert.DoesNotContain("document.title", Ids(after));
        Assert.DoesNotContain("document.language", Ids(after));
    }

    [Fact]
    public void ReportIsReadOnly_AndNothingIsEditedByChecking()
    {
        using Document doc = Open("<w:p>" + Drawing("") + "</w:p>", Styles);
        _ = doc.CheckAccessibility();
        Assert.False(doc.HasUncommittedChanges);
    }

    [Fact]
    public void OfficeStatic_ChecksAFileByContent()
    {
        string path = Path.Combine(Path.GetTempPath(), "a11y-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            using (Document doc = Open("<w:p>" + Drawing("") + "</w:p>", Styles))
            {
                doc.SaveAs(path);
            }

            DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
            AccessibilityReport r = Office.CheckAccessibility(path);
            Assert.Equal(OfficeKind.Document, r.Kind);
            Assert.Contains("alt-text.missing", Ids(r));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
