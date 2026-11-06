// The one-page tour: every task of the introduction, each one or two lines. Inputs are generated first so the sample needs nothing on disk.
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Samples;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Binding;
using RecursiveAI.OneDocument.Templating;

string dir = SampleSupport.WorkDirectory(args);
Directory.SetCurrentDirectory(dir);                      // the calls below use plain relative names

// ---- inputs ---------------------------------------------------------------------------------------------------------------
using (Presentation made = Presentation.Create())
{
    made.AddSlide("Title Slide").SetPlaceholderText("title", "Results");
    made.SaveAs("deck.pptx");
}

using (Document template = Document.Create())
{
    template.Body.AddParagraph("Invoice {{ number }} for {{ customer }}");
    template.SaveAs("invoice.docx");
}

Order[] orders = [new(1, "Ada", 120.5m, new DateOnly(2026, 3, 1)), new(2, "Grace", 80m, new DateOnly(2026, 3, 2))];
Spreadsheet.Write("orders.xlsx", orders);

using (Workbook made = Workbook.Create())
{
    made.Sheets[0].Rename("Sales");
    made.Sheets["Sales"]["B1"].Formula = "SUM(B2:B3)";
    made.Sheets["Sales"]["B2"].SetNumber(100);
    made.Sheets["Sales"]["B3"].SetNumber(200);
    made.SaveAs("sales.xlsx");
}

File.Copy(Path.Combine(AppContext.BaseDirectory, "basic.doc"), "old.doc", overwrite: true);

using (Document made = Document.Create())
{
    made.Styles.AddBuiltIn("Heading 1");
    made.Body.AddParagraph("Scope").Style = "Heading 1";
    Paragraph body = made.Body.AddParagraph("The supplier delivers the goods.");
    made.Body.AddParagraph("Payment").Style = "Heading 1";
    made.Revisions.CreateAuthor("Reviewer", null).InsertText(body, 0, "Within 30 days, ");
    made.SaveAs("contract.docx");
}

var invoice = new Invoice { Number = "1001", Customer = "Ada Lovelace" };

// ---- the sketch, as written -----------------------------------------------------------------------------------------------
// one-liners: each is one line over the object model
Office.Convert("deck.pptx", "deck.pdf");
Template.Render("invoice.docx", invoice, "invoice.pdf");
var read = Spreadsheet.Read<Order>("orders.xlsx");
Spreadsheet.Write("report.xlsx", read);
string text = Office.ExtractText("old.doc");

// the object model when you need more
using var wb = Workbook.Open("sales.xlsx");
wb.Sheets["Sales"]["B2"].Value = 1200;
wb.Recalculate();
wb.SaveAs("sales.pdf");

using var doc = Document.Open("contract.docx");
var headings = doc.Paragraphs.Where(p => p.Style == "Heading 1").ToList();
doc.Revisions.AcceptAll();
doc.Sanitize();
var markup = doc.Markup;                                 // the hop to the typed views
doc.SaveAs("clean.docx");

// ---- checks ---------------------------------------------------------------------------------------------------------------
SampleSupport.RequirePdf("deck.pdf");
SampleSupport.RequirePdf("invoice.pdf");
SampleSupport.RequirePdf("sales.pdf");
if (headings.Count != 2 || wb.Sheets["Sales"]["B1"].Number != 1400 || string.IsNullOrWhiteSpace(text) || !markup.IsValid
    || doc.Revisions.Revisions.Count != 0 || read.Count() != 2 || !File.Exists("report.xlsx"))
{
    throw new InvalidOperationException("The sketch did not produce what it should.");
}

Console.WriteLine(headings.Count + " headings, total " + wb.Sheets["Sales"]["B1"].Number + ", " + text.Length + " characters of old text");

[SpreadsheetRow(Sheet = "Orders")]
internal sealed partial record Order(int Id, string Customer, decimal Total, DateOnly Placed);

[TemplateModel]
internal sealed partial class Invoice
{
    public string Number { get; init; } = string.Empty;

    public string Customer { get; init; } = string.Empty;
}
