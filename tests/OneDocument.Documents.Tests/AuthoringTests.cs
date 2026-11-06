using System.Text;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class AuthoringTests
{
    // A 1 x 1 pixel PNG.
    internal static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private const string Styles =
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Strong\"><w:name w:val=\"Strong\"/></w:style>" +
        "<w:style w:type=\"table\" w:styleId=\"Grid\"><w:name w:val=\"Grid\"/></w:style>";

    private static readonly string[] RunPropertyOrder = ["rFonts", "<w:b/>", "<w:i ", "w:color", "w:sz ", "w:u "];

    private static string MainXml(Document d) => Encoding.UTF8.GetString(d.MainMarkup.ToArray());

    [Fact]
    public void ParagraphStyle_ReadsTheNameWordShows_AndSetsByName()
    {
        using Document d = TestDocs.Open(
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>H</w:t></w:r></w:p><w:p><w:r><w:t>body</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val=\"Gone\"/></w:pPr></w:p>",
            Styles);
        Paragraph[] ps = d.Paragraphs.ToArray();
        Assert.Equal("Heading 1", ps[0].Style);
        Assert.Equal("Normal", ps[1].Style);                  // no pStyle: the default paragraph style
        Assert.Equal("Gone", ps[2].Style);                    // an id the sheet lacks reads as the id

        ps[1].Style = "HEADING 1";
        Assert.Equal("Heading1", ps[1].StyleId);
        ps[1].Style = null;
        Assert.Null(ps[1].StyleId);
    }

    [Fact]
    public void SettingAnUnknownOrWrongTypeOfStyle_ThrowsTypedAndChangesNothing()
    {
        using Document d = TestDocs.Open("<w:p/>", Styles);
        Paragraph p = d.Paragraphs.Single();
        StyleNotFoundException missing = Assert.Throws<StyleNotFoundException>(() => p.Style = "Heading 9");
        Assert.Equal("docx.style-not-found", missing.Defect.Code);
        Assert.Equal("Heading 9", missing.Name);
        Assert.Throws<StyleNotFoundException>(() => p.Style = "Strong");              // a character style
        Assert.Null(p.StyleId);
        Assert.Null(d.Styles.FindByName("Heading 9"));                                // nothing was created
        Assert.Throws<StyleNotFoundException>(() => d.Styles["nope"]);
    }

    [Fact]
    public void RunAndTableStyles_AreSetByName()
    {
        using Document d = TestDocs.Open("<w:p><w:r><w:t>x</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>", Styles);
        Run r = d.Paragraphs.First().Runs().Single();
        r.Style = "Strong";
        Assert.Equal("Strong", r.StyleId);
        Assert.Equal("Strong", r.Style);
        Assert.Throws<StyleNotFoundException>(() => r.Style = "Heading 1");
        Table t = d.Tables.Single();
        t.Style = "Grid";
        Assert.Equal("Grid", t.StyleId);
        Assert.Throws<StyleNotFoundException>(() => t.Style = "Strong");
    }

    [Fact]
    public void AddBuiltIn_GivesANewDocumentTheHeadings_Once()
    {
        using Document d = Document.Create();
        Assert.Throws<StyleNotFoundException>(() => d.Body.AddParagraph("x").Style = "Heading 1");
        StyleDefinition h1 = d.Styles.AddBuiltIn("Heading 1");
        Assert.Same(h1.Markup.Node.Document, d.Styles.AddBuiltIn("heading 1").Markup.Node.Document);
        Assert.Single(d.Styles.Styles, s => s.Id == "Heading1");
        Paragraph p = d.Body.AddParagraph("Intro");
        p.Style = "Heading 1";
        Assert.Equal("Heading 1", p.Style);
        Assert.Equal("Heading1", d.Styles.AddBuiltIn("Heading 1").Id);
        foreach (string name in new[] { "Title", "Heading 2", "Heading 3", "Caption", "List Paragraph", "Hyperlink", "Table Grid" })
        {
            Assert.NotNull(d.Styles.AddBuiltIn(name));
        }

        Assert.Throws<ArgumentException>(() => d.Styles.AddBuiltIn("Fancy"));
    }

    [Fact]
    public void RunFormatting_ThroughSimpleProperties()
    {
        using Document d = Document.Create();
        Paragraph p = d.Body.AddParagraph(string.Empty);
        Run r = p.AddRun("hello\tworld");
        r.Bold = true;
        r.Italic = false;
        r.Underline = true;
        r.FontSize = 12.5;
        r.Color = "c00000";
        r.FontName = "Georgia";
        Assert.True(r.Bold);
        Assert.False(r.Italic);
        Assert.True(r.Underline);
        Assert.Equal(12.5, r.FontSize);
        Assert.Equal("C00000", r.Color);
        Assert.Equal("Georgia", r.FontName);
        Assert.Equal("hello\tworld", p.Text);

        string xml = MainXml(d);
        Assert.Contains("w:sz w:val=\"25\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:u w:val=\"single\"", xml, StringComparison.Ordinal);
        foreach (string expected in RunPropertyOrder)
        {
            Assert.Contains(expected, xml, StringComparison.Ordinal);
        }

        r.Bold = null;
        r.FontSize = null;
        Assert.Null(r.Bold);
        Assert.Null(r.FontSize);
        Assert.Throws<ArgumentException>(() => r.Color = "red");
        Assert.Throws<ArgumentOutOfRangeException>(() => r.FontSize = 0);
        Assert.Throws<ArgumentException>(() => p.AddRun("bad\u0001text"));
    }

    [Fact]
    public void Hyperlink_ExternalGetsARelationship_AnchorDoesNot()
    {
        using Document d = Document.Create();
        Paragraph p = d.Body.AddParagraph("See ");
        Hyperlink link = p.AddHyperlink("the site", "https://example.com/a?b=1&c=2");
        Hyperlink anchor = p.AddHyperlink("top", "#Top");
        Assert.NotNull(link.RelationshipId);
        Assert.Equal("Top", anchor.Anchor);
        Relationship rel = d.Package.GetRelationships(d.MainPartName).Single(r => r.Id == link.RelationshipId);
        Assert.Equal(RelationshipTargetMode.External, rel.TargetMode);
        Assert.Equal("https://example.com/a?b=1&c=2", rel.Target);
        Assert.NotNull(d.Styles.FindByName("Hyperlink"));
        Assert.Equal("See the sitetop", p.Text);
        Assert.Throws<ArgumentException>(() => p.AddHyperlink("x", " "));
    }

    [Fact]
    public void Picture_IsStoredOnce_WithUniqueDrawingIds_AndRejectsNonPictures()
    {
        using Document d = Document.Create();
        Paragraph a = d.Body.AddPicture(Png, 72, 36, "first");
        Run b = d.Body.AddParagraph("x").AddPicture(Png, widthPoints: 100);
        Assert.NotNull(a);
        Assert.Equal(2, d.Package.Parts.Count(p => p.Name.StartsWith("/word/media/", StringComparison.Ordinal)));
        string xml = MainXml(d);
        Assert.Contains("wp:extent cx=\"914400\" cy=\"457200\"", xml, StringComparison.Ordinal);
        Assert.Contains("wp:extent cx=\"1270000\" cy=\"1270000\"", xml, StringComparison.Ordinal);   // one side given: proportions kept
        Assert.Contains("descr=\"first\"", xml, StringComparison.Ordinal);
        Assert.Contains("docPr id=\"1\"", xml, StringComparison.Ordinal);
        Assert.Contains("docPr id=\"2\"", xml, StringComparison.Ordinal);
        Assert.Equal(2, d.Package.GetRelationships(d.MainPartName).Count(r => r.Type.EndsWith("/image", StringComparison.Ordinal)));
        _ = b;

        int before = d.Package.Parts.Count;
        Assert.Throws<ArgumentException>(() => d.Body.AddPicture([1, 2, 3, 4]));
        Assert.Throws<ArgumentException>(() => d.Body.AddPicture(Png, -1));
        Assert.Equal(before, d.Package.Parts.Count);
    }

    [Fact]
    public void Table_FromRows_PadsRaggedRows_AndReachesCells()
    {
        using Document d = Document.Create();
        Table t = d.Body.AddTable([["Item", "Qty", "Price"], ["Tea", "2"], ["Cake"]]);
        Assert.Equal("Table Grid", t.Style);
        Assert.Equal(3, t.RowCount);
        Assert.Equal("Item", t[0, 0].Text);
        Assert.Equal("2", t[1, 1].Text);
        Assert.Equal(string.Empty, t[2, 2].Text);
        Assert.Equal(3, t.GridColumns.Count);
        Assert.Equal(9360L, t.GridColumns.Sum());                        // the page's 6.5 inch text width
        t.SetCellText(2, 2, "9.50");
        Assert.Equal("9.50", t[2, 2].Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => t[3, 0]);

        Table grid = d.Body.AddTable(new string?[,] { { "a", null }, { "c", "d" } });
        Assert.Equal(string.Empty, grid[0, 1].Text);
        Table empty = d.Body.AddTable(2, 2);
        Assert.Equal(2, empty.RowCount);
        Assert.Same(d.Body.Blocks().OfType<Table>().Last().Markup.Node.Document, empty.Markup.Node.Document);
    }

    [Fact]
    public void Table_BadInput_ThrowsAndLeavesTheBodyAlone()
    {
        using Document d = Document.Create();
        string before = MainXml(d);
        Assert.Throws<ArgumentException>(() => d.Body.AddTable(Array.Empty<string[]>()));
        Assert.Throws<ArgumentException>(() => d.Body.AddTable([["a\u0002"]]));
        Assert.Throws<ArgumentOutOfRangeException>(() => d.Body.AddTable(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => d.Body.AddTable(3, 64));
        Assert.Equal(before, MainXml(d));
    }

    [Fact]
    public void TableWidthFollowsThePageMargins()
    {
        using Document d = TestDocs.Open("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1000\" w:bottom=\"1440\" w:left=\"1000\"/></w:sectPr>", Styles);
        Table t = d.Body.AddTable(1, 2);
        Assert.Equal(2L * ((11906 - 2000) / 2), t.GridColumns.Sum());
        // The table goes before the closing section properties.
        Assert.IsType<Table>(d.Body.Blocks().Last());
    }

    [Fact]
    public void AuthoringEdits_ChangeOnlyTheMainPartAndItsNewNeighbours()
    {
        byte[] original;
        using (Document d = Document.Create())
        {
            d.Body.AddParagraph("kept");
            d.Styles.AddBuiltIn("Heading 1");
            using var ms = new MemoryStream();
            d.SaveAs(ms, OfficeFormat.Docx);
            original = ms.ToArray();
        }

        Dictionary<string, byte[]> before = Parts(original);
        byte[] edited;
        using (Document d = Document.Open(new MemoryStream(original)))
        {
            Paragraph p = d.Body.AddParagraph("added");
            p.AddRun(" more").Bold = true;
            d.Body.AddTable([["a", "b"]]);
            using var ms = new MemoryStream();
            d.SaveAs(ms, OfficeFormat.Docx);
            edited = ms.ToArray();
        }

        Dictionary<string, byte[]> after = Parts(edited);
        foreach ((string name, byte[] bytes) in before)
        {
            if (name is "/word/document.xml" or "/word/styles.xml")
            {
                continue;                                       // the edited part, and Table Grid added to the style sheet
            }

            Assert.True(after.TryGetValue(name, out byte[]? now) && now.AsSpan().SequenceEqual(bytes), name + " changed");
        }

        using Document back = Document.Open(new MemoryStream(edited));
        Assert.Equal(["kept", "added more", "a", "b"], back.Paragraphs.Select(x => x.Text).Where(t => t.Length > 0).ToArray());
    }

    private static Dictionary<string, byte[]> Parts(byte[] zip)
    {
        using OpcPackage package = OpcPackage.Open(new MemoryStream(zip));
        return package.Parts.ToDictionary(p => p.Name, p => p.GetBytes().ToArray());
    }

    private static Document WithPersonalData()
    {
        Document d = Document.Create();
        d.Body.AddParagraph("text");
        IOpcPackage p = d.Package;
        p.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>Jane Doe</dc:creator><dc:title>Plan</dc:title></cp:coreProperties>"));
        p.SetRelationships(null,
        [
            .. p.GetRelationships(),
            new Relationship("rId9", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
        ]);
        return d;
    }

    [Fact]
    public void Sanitize_RunsInMemory_VerifiesOnTheCall_AndKeepsPendingEdits()
    {
        using Document d = WithPersonalData();
        IOpcPackage package = d.Package;
        d.Body.AddParagraph("pending edit");
        byte[] stylesBefore = package.FindPart("/word/styles.xml")!.GetBytes().ToArray();

        SanitizeReport report = d.Sanitize();

        Assert.Contains(report.Removed, r => r.Category == InspectionCategory.Properties);
        Assert.DoesNotContain("Jane Doe", Encoding.UTF8.GetString(package.FindPart("/docProps/core.xml")?.GetBytes().ToArray() ?? []), StringComparison.Ordinal);
        Assert.Equal(["text", "pending edit"], d.Paragraphs.Select(p => p.Text).Where(t => t.Length > 0).ToArray());
        Assert.True(package.FindPart("/word/styles.xml")!.GetBytes().Span.SequenceEqual(stylesBefore));       // untouched parts keep their bytes
        d.Body.AddParagraph("after");                                                                          // the document is still usable
        using var ms = new MemoryStream();
        d.SaveAs(ms, OfficeFormat.Docx);
        using Document back = Document.Open(new MemoryStream(ms.ToArray()));
        Assert.Equal(3, back.Paragraphs.Count(p => p.Text.Length > 0));
        Assert.Equal(0, Inspector.Inspect(new MemoryStream(ms.ToArray())).CountOf(InspectionCategory.Properties));
    }

    [Fact]
    public void Sanitize_WithOptions_RemovesOnlyThoseCategories()
    {
        using Document d = WithPersonalData();
        SanitizeReport report = d.Sanitize(SanitizeOptions.Only(InspectionCategory.Comments));
        Assert.DoesNotContain(report.Removed, r => r.Category == InspectionCategory.Properties);
    }
}
