using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class OdtSoftHyphenTests
{
    [Fact]
    public void SoftHyphenIsTheCharacter_NotAnElement()
    {
        using var ms = new MemoryStream();
        using (Document doc = Document.Create())
        {
            doc.Body.AddParagraph("x");
            doc.Paragraphs.Last().Markup.Node.AppendXml(Encoding.UTF8.GetBytes(
                $"<w:r xmlns:w=\"{TestDocs.WNs}\"><w:rPr><w:b/></w:rPr><w:t>co</w:t><w:softHyphen/><w:t>operate</w:t></w:r>"));
            doc.SaveAs(ms, OfficeFormat.Odt);
        }

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
        string content = reader.ReadToEnd();

        // The ODF 1.3 schema has no soft-hyphen element; Jing rejected it inside a span.
        Assert.DoesNotContain("soft-hyphen", content, StringComparison.Ordinal);
        Assert.Contains("co\u00ADoperate", content, StringComparison.Ordinal);
    }
}
