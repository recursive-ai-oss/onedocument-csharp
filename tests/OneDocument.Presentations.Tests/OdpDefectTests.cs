using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Presentations.Odf;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>Crafted and hostile OpenDocument presentations: each repair is a typed defect, and nothing but a typed error leaves the reader.</summary>
public class OdpDefectTests
{
    static OdpDefectTests() => PresentationFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

    private const string Ns =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:presentation=\"urn:oasis:names:tc:opendocument:xmlns:presentation:1.0\"";

    private const string PageLayout = "<style:page-layout style:name=\"PM\"><style:page-layout-properties fo:page-width=\"28cm\" fo:page-height=\"15.75cm\"/></style:page-layout>";
    private const string Master = "<style:master-page style:name=\"M\" style:page-layout-name=\"PM\"/>";

    private static byte[] Odp(string pages, string automatic = "", string? masters = Master, string common = "")
    {
        using var pkg = OdfPackage.Create(OdfMediaTypes.Presentation);
        string content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-content " + Ns + " office:version=\"1.3\"><office:automatic-styles>" + automatic +
            "</office:automatic-styles><office:body><office:presentation>" + pages + "</office:presentation></office:body></office:document-content>";
        string styles = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-styles " + Ns + " office:version=\"1.3\"><office:styles>" + common + "</office:styles><office:automatic-styles>" + PageLayout +
            "</office:automatic-styles>" + (masters is null ? "" : "<office:master-styles>" + masters + "</office:master-styles>") + "</office:document-styles>";
        pkg.SetEntry("content.xml", Encoding.UTF8.GetBytes(content), "text/xml");
        pkg.SetEntry("styles.xml", Encoding.UTF8.GetBytes(styles), "text/xml");
        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        return ms.ToArray();
    }

    private static Presentation Open(byte[] bytes, OpenContext? context = null) => Presentation.Open(new MemoryStream(bytes), context);

    private static bool Has(Presentation p, string code) => p.Defects.Any(d => d.Code == code);

    private static string Page(string shapes, string attrs = "draw:master-page-name=\"M\"") => "<draw:page draw:name=\"S\" " + attrs + ">" + shapes + "</draw:page>";

    [Fact]
    public void A_text_document_raises_not_presentation_from_the_package_reader()
    {
        using var pkg = OdfPackage.Create(OdfMediaTypes.Text);
        pkg.SetEntry("content.xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><office:document-content " + Ns + "><office:body><office:text><text:p>x</text:p></office:text></office:body></office:document-content>"), "text/xml");
        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        var ex = Assert.Throws<OfficeFormatException>(() => OpenDocumentPresentation.ToPackage(ByteSource.FromMemory(ms.ToArray())));
        Assert.Equal("odf.not-presentation", ex.Defect.Code);
    }

    [Fact]
    public void A_presentation_without_master_pages_gets_a_plain_one()
    {
        using Presentation p = Open(Odp(Page("<draw:frame presentation:class=\"title\" svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"5cm\" svg:height=\"2cm\"><draw:text-box><text:p>Hello</text:p></draw:text-box></draw:frame>", ""), masters: null));
        Assert.Equal("Hello", p.Slides[0].Text.Trim());
        Assert.True(Has(p, "odp.no-master-page"));
        Assert.Single(p.Masters);
    }

    [Fact]
    public void A_slide_that_names_a_missing_master_page_takes_the_first()
    {
        using Presentation p = Open(Odp(Page("", "draw:master-page-name=\"Nowhere\"")));
        Assert.True(Has(p, "odp.missing-master-page"));
        Assert.NotNull(p.Slides[0].Layout);
    }

    [Fact]
    public void A_polygon_without_a_viewbox_and_a_path_with_bad_data_are_repaired()
    {
        string shapes = "<draw:polygon draw:points=\"0,0 10,0 10,10\" svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"2cm\" svg:height=\"2cm\"/>" +
            "<draw:path svg:d=\"M 0 0 L 10 0 X 5 5 Z\" svg:viewBox=\"0 0 10 10\" svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"2cm\" svg:height=\"2cm\"/>";
        using Presentation p = Open(Odp(Page(shapes)));
        Assert.True(Has(p, "odp.bad-viewbox"));
        Assert.True(Has(p, "odp.bad-path"));
        Assert.Equal(2, p.Slides[0].Shapes.Count);
    }

    [Fact]
    public void A_table_without_columns_is_dropped_and_a_repeat_count_is_capped()
    {
        string empty = "<draw:frame svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"5cm\" svg:height=\"2cm\"><table:table><table:table-row><table:table-cell><text:p>x</text:p></table:table-cell></table:table-row></table:table></draw:frame>";
        using Presentation p = Open(Odp(Page(empty)));
        Assert.True(Has(p, "odp.empty-table"));
        Assert.Empty(p.Slides[0].Shapes);

        // A hostile column repeat would otherwise ask for a billion cells in every row.
        string wide = "<draw:frame svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"5cm\" svg:height=\"2cm\"><table:table><table:table-column table:number-columns-repeated=\"1000000000\"/>" +
            "<table:table-row table:number-rows-repeated=\"1000000000\"><table:table-cell table:number-columns-repeated=\"1000000000\"><text:p>x</text:p></table:table-cell></table:table-row></table:table></draw:frame>";
        using Presentation q = Open(Odp(Page(wide)));
        Table table = q.Slides[0].Shapes.Single().Table!;
        Assert.True(Has(q, "odf.repeat-capped"));
        Assert.True(table.ColumnCount <= 16384);
        Assert.True(table.RowCount <= 1000);
    }

    [Fact]
    public void A_bitmap_fill_naming_a_missing_image_is_dropped()
    {
        string auto = "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties draw:fill=\"bitmap\" draw:fill-image-name=\"Nope\"/></style:style>";
        using Presentation p = Open(Odp(Page("<draw:rect draw:style-name=\"gr1\" svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"2cm\" svg:height=\"2cm\"/>"), auto));
        Assert.True(Has(p, "odp.missing-fill-image"));
    }

    [Fact]
    public void Groups_nested_past_the_reader_limit_are_dropped_with_a_defect()
    {
        string open = string.Concat(Enumerable.Repeat("<draw:g>", 30));
        string close = string.Concat(Enumerable.Repeat("</draw:g>", 30));
        using Presentation p = Open(Odp(Page(open + "<draw:rect svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"2cm\" svg:height=\"2cm\"/>" + close)));
        Assert.True(Has(p, "odp.group-too-deep"));
    }

    [Fact]
    public void Lists_nested_without_end_raise_a_limit_error_not_a_crash()
    {
        string open = string.Concat(Enumerable.Repeat("<text:list><text:list-item>", 5000));
        string close = string.Concat(Enumerable.Repeat("</text:list-item></text:list>", 5000));
        string frame = "<draw:frame svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"5cm\" svg:height=\"2cm\"><draw:text-box>" + open + "<text:p>deep</text:p>" + close + "</draw:text-box></draw:frame>";
        Assert.ThrowsAny<OfficeException>(() => Open(Odp(Page(frame))).Slides[0].Text);
    }

    [Fact]
    public void Linked_pictures_are_never_fetched_and_external_links_stay_external()
    {
        string frame = "<draw:frame svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"5cm\" svg:height=\"2cm\"><draw:image xlink:href=\"http://example.invalid/a.png\"/></draw:frame>" +
            "<draw:frame presentation:class=\"outline\" svg:x=\"1cm\" svg:y=\"4cm\" svg:width=\"5cm\" svg:height=\"2cm\"><draw:text-box><text:p><text:a xlink:href=\"https://example.invalid/\">link</text:a></text:p></draw:text-box></draw:frame>";
        using Presentation p = Open(Odp(Page(frame)));
        Assert.DoesNotContain(p.Slides[0].AllShapes(), s => s.Kind == ShapeKind.Picture);
        Assert.Contains(p.ImportLoss!.Items, i => i.Code == "loss.linked-images");
        Relationship_IsExternal(p, "https://example.invalid/");
    }

    private static void Relationship_IsExternal(Presentation p, string target)
    {
        Assert.Contains(p.Slides[0].Relationships, r => r.Target == target && r.TargetMode == RecursiveAI.OneDocument.Packaging.RelationshipTargetMode.External);
    }

    [Fact]
    public void Missing_lengths_and_odd_attributes_do_not_stop_the_reader()
    {
        string shapes = "<draw:custom-shape svg:x=\"abc\" svg:width=\"-5cm\" svg:height=\"1e99cm\"><draw:enhanced-geometry draw:type=\"ooxml-\\u0000\" draw:modifiers=\"nan inf 1\"/></draw:custom-shape>" +
            "<draw:line svg:x1=\"1cm\"/><draw:connector/><draw:frame/><draw:g/>";
        using Presentation p = Open(Odp(Page(shapes)));
        Assert.Single(p.Slides);
        Assert.DoesNotContain(p.Defects, d => d.Severity == DefectSeverity.Fatal);
    }

    [Fact]
    public void Garbage_bytes_raise_typed_errors_only()
    {
        byte[] good = Odp(Page(""));
        for (int cut = 40; cut < good.Length; cut += 97)
        {
            try
            {
                using Presentation p = Open(good.AsSpan(0, cut).ToArray());
                _ = p.Slides.Count;
            }
            catch (OfficeException)
            {
            }
        }
    }
}
