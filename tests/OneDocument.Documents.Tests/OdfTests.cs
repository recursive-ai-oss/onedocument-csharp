using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Odf;
using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class OdfTests
{
    private static string Norm(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static byte[] Save(Document d, OfficeFormat format)
    {
        using var ms = new MemoryStream();
        d.SaveAs(ms, format);
        return ms.ToArray();
    }

    private static Document Open(byte[] bytes, OpenContext? ctx = null) => Document.Open(new MemoryStream(bytes), ctx);

    [Fact]
    public void RichDocumentWritesAsOdtAndReadsBackWithTheSameText()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        Assert.NotNull(source.ExportLoss);
        using Document back = Open(odt);
        Assert.Equal(OfficeFormat.Odt, back.Format);
        Assert.Equal(Norm(source.ExtractText()), Norm(back.ExtractText()));
        Assert.Equal(source.Tables.Count(), back.Tables.Count());
    }

    [Fact]
    public void RichDocumentWritesAsFlatOdtAndReadsBack()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] fodt = Save(source, OfficeFormat.Fodt);
        Assert.Contains("<office:document ", Encoding.UTF8.GetString(fodt), StringComparison.Ordinal);
        using Document back = Open(fodt);
        Assert.Equal(OfficeFormat.Fodt, back.Format);
        Assert.Equal(Norm(source.ExtractText()), Norm(back.ExtractText()));
        Assert.Contains("office:binary-data", Encoding.UTF8.GetString(fodt), StringComparison.Ordinal);
    }

    [Fact]
    public void UntouchedOdtSavesItsOriginalBytes()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        using Document back = Open(odt);
        Assert.Equal(odt, Save(back, OfficeFormat.Odt));
        // An edit ends the pass-through: the output is a valid package again, with the edit in it.
        back.Paragraphs.First().SetText("Edited title");
        byte[] edited = Save(back, OfficeFormat.Odt);
        Assert.NotEqual(odt, edited);
        using Document again = Open(edited);
        Assert.StartsWith("Edited title", again.ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void WrittenPackageStartsWithAStoredMimetype()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        Assert.Equal((byte)'P', odt[0]);
        Assert.Equal(0, BitConverter.ToUInt16(odt, 8)); // method: stored
        Assert.Equal("mimetype", Encoding.ASCII.GetString(odt, 30, 8));
        Assert.Equal("application/vnd.oasis.opendocument.text", Encoding.ASCII.GetString(odt, 38, 39));
    }

    [Fact]
    public void ExportKeepsStructure()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        using OdfPackage pkg = OdfPackage.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(odt));
        string content = pkg.ContentRoot.OuterXml;
        Assert.Contains("<text:h ", content, StringComparison.Ordinal);
        Assert.Contains("text:outline-level=\"1\"", content, StringComparison.Ordinal);
        Assert.Contains("<text:list ", content, StringComparison.Ordinal);
        Assert.Contains("<text:note ", content, StringComparison.Ordinal);
        Assert.Contains("<office:annotation ", content, StringComparison.Ordinal);
        Assert.Contains("table:number-columns-spanned=\"2\"", content, StringComparison.Ordinal);
        Assert.Contains("table:number-rows-spanned=\"2\"", content, StringComparison.Ordinal);
        Assert.Contains("<table:table-header-rows>", content, StringComparison.Ordinal);
        Assert.Contains("<text:changed-region", content, StringComparison.Ordinal);
        Assert.Contains("xlink:href=\"https://example.org/page\"", content, StringComparison.Ordinal);
        Assert.Contains("text:bookmark-start", content, StringComparison.Ordinal);
        Assert.Contains("<draw:image xlink:href=\"Pictures/", content, StringComparison.Ordinal);
        Assert.True(pkg.Contains("Pictures/image1.png"));
        string styles = pkg.StylesRoot.OuterXml;
        Assert.Contains("style:master-page", styles, StringComparison.Ordinal);
        Assert.Contains("<style:header>", styles, StringComparison.Ordinal);
        Assert.Contains("text:page-number", styles, StringComparison.Ordinal);
        Assert.Contains("style:print-orientation=\"landscape\"", styles, StringComparison.Ordinal);
        Assert.Equal("Report", pkg.Metadata.Title);
    }

    [Fact]
    public void ImportMapsStructureBackToTheHub()
    {
        using Document source = OdfSamples.RichDocument();
        byte[] odt = Save(source, OfficeFormat.Odt);
        using Document back = Open(odt);
        string xml = back.MainMarkup.Root.OuterXml;
        Assert.Contains("<w:pStyle w:val=\"Heading1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:numPr>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:footnoteReference", xml, StringComparison.Ordinal);
        Assert.Contains("<w:commentRangeStart", xml, StringComparison.Ordinal);
        Assert.Contains("<w:gridSpan w:val=\"2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tblHeader/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:ins ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:del ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:hyperlink ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:drawing>", xml, StringComparison.Ordinal);
        Assert.Contains("w:orient=\"landscape\"", xml, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(xml, "<w:sectPr>"));
    }

    [Fact]
    public void OutputFilesForTheOracleRunsWhenAskedFor()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ODF_OUT");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using Document source = OdfSamples.RichDocument();
        source.SaveAs(Path.Combine(dir, "rich.docx"));
        File.WriteAllBytes(Path.Combine(dir, "rich.odt"), Save(source, OfficeFormat.Odt));
        File.WriteAllBytes(Path.Combine(dir, "rich.fodt"), Save(source, OfficeFormat.Fodt));
        File.WriteAllBytes(Path.Combine(dir, "rich.ott"), Save(source, OfficeFormat.Ott));
        File.WriteAllText(Path.Combine(dir, "rich.docx.txt"), Norm(source.ExtractText()));
        using Document back = Open(Save(source, OfficeFormat.Odt));
        File.WriteAllText(Path.Combine(dir, "rich.odt.txt"), Norm(back.ExtractText()));
        back.SaveAs(Path.Combine(dir, "rich-back.docx"));
    }
}
