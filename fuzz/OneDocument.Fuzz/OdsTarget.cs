using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Calc.OpenFormula;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// OpenDocument spreadsheets. <c>ods</c> opens the input as a workbook, walks its cells and saves it as XLSX, ODS and flat ODS. <c>openformula</c> parses the input as
/// an OpenFormula expression, prints the tree back and parses that again. Only a typed <see cref="OfficeException"/> may escape.
/// </summary>
internal static class OdsTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        SpreadsheetFormats.Register(FormatRegistry.Default);
        byte[] bytes = data.ToArray();
        try
        {
            using Workbook wb = Workbook.Open(new MemoryStream(bytes));
            foreach (Worksheet ws in wb.Sheets)
            {
                foreach (CellRowView row in ws.Store.EnumerateRows())
                {
                    for (int i = 0; i < row.Count; i++)
                    {
                        _ = row.ValueAt(i);
                        _ = row.FormulaAt(i);
                    }
                }
            }

            using var xlsx = new MemoryStream();
            wb.SaveAs(xlsx, OfficeFormat.Xlsx);
            using var ods = new MemoryStream();
            wb.SaveAs(ods, OfficeFormat.Ods);
            using var flat = new MemoryStream();
            wb.SaveAs(flat, OfficeFormat.Fods);
            flat.Position = 0;
            using Workbook again = Workbook.Open(flat);
            _ = again.Sheets.Count;
        }
        catch (OfficeException)
        {
        }
    }

    public static void RunFormula(ReadOnlySpan<byte> data)
    {
        string text = Encoding.UTF8.GetString(data);
        if (OpenFormulaParser.TryParse(text, out FormulaNode? node, out _) && node is not null)
        {
            string printed = OpenFormulaPrinter.Print(node);
            _ = OpenFormulaParser.TryParse(printed, out _, out _);
            _ = FormulaPrinter.Print(node);
        }
    }

    public static int WriteSeeds(string directory, bool formulas)
    {
        Directory.CreateDirectory(directory);
        if (formulas)
        {
            string[] samples =
            [
                "of:=SUM([.A1:.B2];[$'My Sheet'.C3])", "of:=COM.MICROSOFT.XLOOKUP(1;[.A1:.A3];[.B1:.B3])", "of:={1;2|3;4}", "of:=IF([.A1];;2)",
                "of:=[.A1:.B2]![.B1:.C3]", "of:=SUM(([.A1]~[.B2]))", "of:=LEGACY.CHIDIST(1;2)+#N/A", "of:=[$Sheet1.A1:$Sheet3.A1]%", "of:=\"a\"\"b\"&TRUE()",
            ];
            for (int i = 0; i < samples.Length; i++)
            {
                File.WriteAllText(Path.Combine(directory, "f" + i + ".txt"), samples[i]);
            }

            return 0;
        }

        const string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
            "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
            "xmlns:number=\"urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.spreadsheet\">" +
            "<office:automatic-styles><number:number-style style:name=\"N1\"><number:number number:decimal-places=\"2\" number:min-integer-digits=\"1\"/></number:number-style>" +
            "<style:style style:name=\"c1\" style:family=\"table-cell\" style:data-style-name=\"N1\"/></office:automatic-styles>" +
            "<office:body><office:spreadsheet><table:table table:name=\"S\"><table:table-row table:number-rows-repeated=\"2\">" +
            "<table:table-cell table:style-name=\"c1\" office:value-type=\"float\" office:value=\"1.5\" table:number-columns-repeated=\"2\"><text:p>1.50</text:p></table:table-cell>" +
            "<table:table-cell table:formula=\"of:=SUM([.A1:.B1])\" office:value-type=\"float\" office:value=\"3\"/><table:table-cell table:number-columns-spanned=\"2\" table:number-rows-spanned=\"2\"/>" +
            "<table:covered-table-cell/></table:table-row></table:table></office:spreadsheet></office:body></office:document>";
        File.WriteAllText(Path.Combine(directory, "seed.fods"), xml, new UTF8Encoding(false));
        return 0;
    }
}
