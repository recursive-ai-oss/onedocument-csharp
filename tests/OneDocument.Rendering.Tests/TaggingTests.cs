using OneFont;
using OneRender.Painting;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using static RecursiveAI.OneDocument.Rendering.Tests.MathCases;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// The structure roles and artifact kinds the page painter writes: Note, Reference, TOC/TOCI, Formula, and Pagination artifacts.
public sealed class TaggingTests
{
    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    private static DisplayList PaintFirstPage(byte[] docx)
    {
        RenderingFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument l = LayoutEngine.Layout(d, new LayoutOptions { Fonts = Fonts });
        return LayoutPainter.Paint(l.Pages[0]);
    }

    private static List<DisplayItem> Tags(DisplayList list)
    {
        var tags = new List<DisplayItem>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.BeginTag)
            {
                tags.Add(list[i]);
            }
        }

        return tags;
    }

    private static string Toc(int level, string text) => TestDocx.P(text, "<w:pStyle w:val=\"TOC" + level + "\"/>");

    [Fact]
    public void HeaderFooterPageNumberTocCrossReferenceAndEquationCarryTheirRolesAndKinds()
    {
        string hdr = PageDocs.Story("hdr", TestDocx.P("Running head"));
        string ftr = PageDocs.Story("ftr", "<w:p>" + PageDocs.PageField("PAGE", "1") + "</w:p>");
        string sect = PageDocs.Sect(PageDocs.Ref("header", "default", "header1.xml") + PageDocs.Ref("footer", "default", "footer1.xml"));
        string xref = "<w:p><w:r><w:t xml:space=\"preserve\">See page </w:t></w:r>" + PageDocs.PageField("PAGEREF _Toc1 \\h", "7") + "</w:p>";
        string body = Toc(1, "Chapter one") + Toc(2, "Section") + TestDocx.P("Body text") + xref + Display(Frac(R("a"), R("b")));
        DisplayList list = PaintFirstPage(PageDocs.Build(body, sect, [("header1.xml", hdr), ("footer1.xml", ftr)]));
        List<DisplayItem> tags = Tags(list);

        Assert.Contains(tags, t => t.Role == TagRole.Artifact && t.ArtifactType == ArtifactType.Pagination && t.ArtifactSubtype == ArtifactSubtype.Header);
        Assert.Contains(tags, t => t.Role == TagRole.Artifact && t.ArtifactType == ArtifactType.Pagination && t.ArtifactSubtype == ArtifactSubtype.Footer);

        // One TOC holding both entries, each a TOCI; the body paragraph after it is outside.
        Assert.Single(tags, t => t.Role == TagRole.TOC);
        Assert.Equal(2, tags.Count(t => t.Role == TagRole.TOCI));
        int toc = tags.FindIndex(t => t.Role == TagRole.TOC);
        Assert.Equal(TagRole.TOCI, tags[toc + 1].Role);

        Assert.Single(tags, t => t.Role == TagRole.Reference);

        DisplayItem formula = Assert.Single(tags, t => t.Role == TagRole.Formula);
        Assert.Equal("a/b", formula.AltText);
        list.Validate();
    }

    [Fact]
    public void FootnotesAreNotesAndTheirReferenceMarksAreReferences()
    {
        byte[] docx = LayoutCases.All().First(c => c.Name == "110-footnote-single").Docx;
        List<DisplayItem> tags = Tags(PaintFirstPage(docx));
        Assert.Single(tags, t => t.Role == TagRole.Note);
        Assert.Single(tags, t => t.Role == TagRole.Reference);
        Assert.Contains(tags, t => t.Role == TagRole.Artifact && t.ArtifactType == ArtifactType.Layout);
    }

    [Fact]
    public void AWatermarkIsAPaginationArtifactOfSubtypeWatermark()
    {
        string pict = "<w:p><w:r><w:pict><v:shape id=\"PowerPlusWaterMarkObject1\" type=\"#_x0000_t136\" style=\"position:absolute;width:400pt;height:100pt;rotation:315\" fillcolor=\"silver\" stroked=\"f\"><v:fill opacity=\".5\"/><v:textpath style=\"font-family:&quot;Calibri&quot;;font-size:1pt\" string=\"DRAFT\"/></v:shape></w:pict></w:r></w:p>";
        string sect = PageDocs.Sect(PageDocs.Ref("header", "default", "header1.xml"));
        List<DisplayItem> tags = Tags(PaintFirstPage(PageDocs.Build(TestDocx.P("body"), sect, [("header1.xml", PageDocs.Story("hdr", pict))])));
        Assert.Contains(tags, t => t.Role == TagRole.Artifact && t.ArtifactType == ArtifactType.Pagination && t.ArtifactSubtype == ArtifactSubtype.Watermark);
    }
}
