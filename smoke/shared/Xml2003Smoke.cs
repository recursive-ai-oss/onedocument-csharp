using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Opens a tiny Word 2003 XML and Excel 2003 XML file (found by content) and checks text, a converted R1C1 formula and a style, under NativeAOT and WASM.</summary>
internal static class Xml2003Smoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        const string Word =
            "<?xml version=\"1.0\"?><?mso-application progid=\"Word.Document\"?><w:wordDocument xmlns:w=\"http://schemas.microsoft.com/office/word/2003/wordml\">" +
            "<w:body><w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>Old format</w:t></w:r></w:p></w:body></w:wordDocument>";
        using (Document doc = Document.Open(new MemoryStream(Encoding.UTF8.GetBytes(Word))))
        {
            if (doc.Paragraphs.Single().Text != "Old format") failures.Add("wml2003 text");
            if (doc.ImportLoss is null) failures.Add("wml2003 loss");
        }

        const string Sheet =
            "<?xml version=\"1.0\"?><?mso-application progid=\"Excel.Sheet\"?><Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">" +
            "<Styles><Style ss:ID=\"b\"><Font ss:Bold=\"1\"/></Style></Styles><Worksheet ss:Name=\"S\"><Table><Row><Cell ss:StyleID=\"b\"><Data ss:Type=\"Number\">2</Data></Cell>" +
            "<Cell ss:Formula=\"=RC[-1]*3\"><Data ss:Type=\"Number\">6</Data></Cell></Row></Table></Worksheet></Workbook>";
        using (Workbook wb = Workbook.Open(new MemoryStream(Encoding.UTF8.GetBytes(Sheet))))
        {
            Worksheet s = wb.Sheets[0];
            if (s["B1"].Formula != "A1*3") failures.Add("sml2003 formula");
            if (s["B1"].Number != 6) failures.Add("sml2003 cached value");
            if (s["A1"].StyleIndex == 0) failures.Add("sml2003 style");
        }

        return failures;
    }
}
