using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Rendering;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Lays out a small document and a small workbook with the packaged fonts and writes them as PDF and PNG: under NativeAOT and WASM.</summary>
internal static class RenderSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using Document document = Document.Create();
        document.Body.AddParagraph("Rendering smoke: a paragraph long enough to need shaping and a line break or two, " +
            "written once by the layout core and painted into a tagged PDF.");
        using var pdf = new MemoryStream();
        LayoutDocument layout = DocumentRenderer.WritePdf(document, pdf);
        byte[] bytes = pdf.ToArray();
        if (layout.Pages.Count != 1 || !layout.Pages[0].Paragraphs.Any())
        {
            failures.Add("render layout");
        }

        if (bytes.Length < 500 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
        {
            failures.Add("render pdf");
        }

        byte[] png = DocumentRenderer.RenderPng(layout.Pages[0]);
        if (png.Length < 500 || png[0] != 0x89)
        {
            failures.Add("render png");
        }

        using Workbook workbook = Workbook.Create();
        Worksheet sheet = workbook.Sheets[0];
        sheet["A1"].SetString("Item");
        sheet["B1"].SetString("Amount");
        sheet["A2"].SetString("Widgets");
        sheet["B2"].SetNumber(1234.5);
        using var sheetPdf = new MemoryStream();
        SheetLayoutDocument sheets = WorkbookRenderer.WritePdf(workbook, sheetPdf);
        if (sheets.Pages.Count != 1 || sheets.Pages[0].Body.ToString() != "A1:B2")
        {
            failures.Add("render sheet layout");
        }

        byte[] sheetBytes = sheetPdf.ToArray();
        if (sheetBytes.Length < 500 || Encoding.ASCII.GetString(sheetBytes, 0, 5) != "%PDF-")
        {
            failures.Add("render sheet pdf");
        }

        if (WorkbookRenderer.RenderPng(sheets, sheets.Pages[0]) is not { Length: > 500 } sheetPng || sheetPng[0] != 0x89)
        {
            failures.Add("render sheet png");
        }

        return failures;
    }
}
