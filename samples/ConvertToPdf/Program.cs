// Convert a Word-processing document, a workbook and a presentation to PDF: one line each.
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Samples;
using RecursiveAI.OneDocument.Spreadsheets;

string dir = SampleSupport.WorkDirectory(args);

// Input files, generated here so the sample needs nothing on disk.
using (Document doc = Document.Create())
{
    doc.Body.AddParagraph("Quarterly report", "Heading1");
    doc.Body.AddParagraph("Revenue grew in every region.");
    doc.SaveAs(Path.Combine(dir, "report.docx"));
}

using (Workbook wb = Workbook.Create())
{
    wb.Sheets[0]["A1"].SetString("Region");
    wb.Sheets[0]["B1"].SetString("Revenue");
    wb.Sheets[0]["A2"].SetString("North");
    wb.Sheets[0]["B2"].SetNumber(1200);
    wb.SaveAs(Path.Combine(dir, "sales.xlsx"));
}

using (Presentation deck = Presentation.Create())
{
    deck.AddSlide(deck.Masters[0].Layouts[1]).Shapes[0].TextBody!.SetText("Results");
    deck.SaveAs(Path.Combine(dir, "deck.pptx"));
}

// The task.
Office.Convert(Path.Combine(dir, "report.docx"), Path.Combine(dir, "report.pdf"));
Office.Convert(Path.Combine(dir, "sales.xlsx"), Path.Combine(dir, "sales.pdf"));
Office.Convert(Path.Combine(dir, "deck.pptx"), Path.Combine(dir, "deck.pdf"));

foreach (string pdf in new[] { "report.pdf", "sales.pdf", "deck.pdf" })
{
    SampleSupport.RequirePdf(Path.Combine(dir, pdf));
}
