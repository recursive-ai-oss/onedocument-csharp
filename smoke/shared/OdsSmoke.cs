using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Calc.OpenFormula;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Reads a tiny flat OpenDocument spreadsheet, writes it as ODS and reads that back, and round-trips an OpenFormula expression, under NativeAOT and WASM.</summary>
internal static class OdsSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        const string Flat =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.spreadsheet\"><office:body><office:spreadsheet>" +
            "<table:table table:name=\"S\"><table:table-row><table:table-cell office:value-type=\"float\" office:value=\"2\"/>" +
            "<table:table-cell table:formula=\"of:=[.A1]*3\" office:value-type=\"float\" office:value=\"6\"/></table:table-row></table:table></office:spreadsheet></office:body></office:document>";
        using (Workbook wb = Workbook.Open(new MemoryStream(Encoding.UTF8.GetBytes(Flat))))
        {
            Worksheet s = wb.Sheets[0];
            if (s["B1"].Formula != "A1*3") failures.Add("ods formula");
            if (s["B1"].Number != 6) failures.Add("ods cached value");

            using var ods = new MemoryStream();
            wb.SaveAs(ods, OfficeFormat.Ods);
            ods.Position = 0;
            using Workbook back = Workbook.Open(ods);
            if (back.Sheets[0]["B1"].Formula != "A1*3" || back.Sheets[0]["B1"].Number != 6) failures.Add("ods round trip");
        }

        if (!OpenFormulaParser.TryParse("of:=SUM([.A1:.B2];1)", out FormulaNode? node, out _) || node is null || OpenFormulaPrinter.Print(node) != "SUM([.A1:.B2];1)")
        {
            failures.Add("openformula round trip");
        }

        return failures;
    }
}
