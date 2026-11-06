using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class OdsPageSetupTests
{
    static OdsPageSetupTests() => SpreadsheetFormats.Register(FormatRegistry.Default);

    [Fact]
    public void FitToPageWritesOnlyTheFitAttributesNotAlsoThePercentage()
    {
        // ODF 1.3 Part 1 (style:page-layout-properties): scale-to, scale-to-pages and scale-to-X / scale-to-Y are alternatives.
        string sheet = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"" + TestBook.Main + "\"><sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>"
            + "<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData>"
            + "<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/><pageSetup scale=\"80\" fitToWidth=\"1\" fitToHeight=\"2\"/></worksheet>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(sheet1: sheet));
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Ods);
        ms.Position = 0;
        using var zip = new ZipArchive(ms);
        var all = new StringBuilder();
        foreach (string part in new[] { "styles.xml", "content.xml" })
        {
            using var reader = new StreamReader(zip.GetEntry(part)!.Open(), Encoding.UTF8);
            all.Append(reader.ReadToEnd());
        }

        string xml = all.ToString();
        Assert.Contains("style:scale-to-X=\"1\"", xml, StringComparison.Ordinal);
        Assert.Contains("style:scale-to-Y=\"2\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("style:scale-to=", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void PercentageScaleIsWrittenWhenTheSheetDoesNotFitToPages()
    {
        string sheet = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"" + TestBook.Main + "\">"
            + "<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData>"
            + "<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/><pageSetup scale=\"80\"/></worksheet>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(sheet1: sheet));
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Ods);
        ms.Position = 0;
        using var zip = new ZipArchive(ms);
        using var reader = new StreamReader(zip.GetEntry("styles.xml")!.Open(), Encoding.UTF8);
        string xml = reader.ReadToEnd();
        Assert.Contains("style:scale-to=\"80%\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("scale-to-X", xml, StringComparison.Ordinal);
    }
}
