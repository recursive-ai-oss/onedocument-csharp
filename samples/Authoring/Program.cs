// Build a document, a workbook and a deck from nothing with the authoring calls, and save each.
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Samples;
using RecursiveAI.OneDocument.Spreadsheets;

string dir = SampleSupport.WorkDirectory(args);

// A 1 x 1 pixel PNG stands in for a logo.
byte[] logo = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

using (Document doc = Document.Create())
{
    doc.Styles.AddBuiltIn("Title");
    doc.Styles.AddBuiltIn("Heading 1");
    doc.Body.AddParagraph("Quarterly report").Style = "Title";
    doc.Body.AddParagraph("Summary").Style = "Heading 1";
    Paragraph intro = doc.Body.AddParagraph("Revenue grew ");
    Run grew = intro.AddRun("12%");
    grew.Bold = true;
    grew.Color = "2F5496";
    intro.AddRun(" in every region; see ");
    intro.AddHyperlink("the dashboard", "https://example.com/dashboard");
    intro.AddRun(".");
    RecursiveAI.OneDocument.Documents.Table table = doc.Body.AddTable([["Region", "Revenue"], ["North", "1,200"], ["South", "950"]]);
    table[0, 0].Paragraphs().First().Runs().First().Bold = true;
    doc.Body.AddPicture(logo, widthPoints: 72, altText: "Company logo");
    doc.SaveAs(Path.Combine(dir, "report.docx"));
}

using (Workbook wb = Workbook.Create())
{
    Worksheet sheet = wb.Sheets[0];
    sheet.Rename("Sales");
    sheet.Range("A1:C1").Values = new object?[,] { { "Region", "H1", "H2" } };
    sheet.Range("A1:C1").UpdateStyle(s => s with { Font = (s.Font ?? new CellFont()) with { Bold = true } });
    sheet.Range("A2:C3").Values = new object?[,] { { "North", 1200, 1350.5 }, { "South", 950, 1010 } };
    sheet.Range("B2:C3").NumberFormat = "#,##0.00";
    sheet["A4"].SetString("Total");
    sheet["B4"].Formula = "SUM(B2:B3)";
    sheet.Range("A6:C6").Values = new object?[,] { { "Notes", null, null } };
    sheet.Range("A6:C6").Merge();
    sheet.AddPicture(logo, "E1", 48, 48, "Company logo");
    wb.SaveAs(Path.Combine(dir, "sales.xlsx"));
}

using (Presentation deck = Presentation.Create())
{
    Slide title = deck.AddSlide("Title Slide");
    title.SetPlaceholderText("title", "Quarterly review");
    title.SetPlaceholderText("subTitle", "Half-year results");
    Slide content = deck.AddSlide("Title and Content");
    content.SetPlaceholderText("title", "Regions");
    content.SetPlaceholderText(1, "North grew\nSouth held");
    content.AddTable([["Region", "Revenue"], ["North", "1,200"], ["South", "950"]], left: 60, top: 300, width: 400);
    content.AddPicture(logo, 560, 20, 72, altText: "Company logo");
    deck.SaveAs(Path.Combine(dir, "review.pptx"));
}

foreach (string name in new[] { "report.docx", "sales.xlsx", "review.pptx" })
{
    Console.WriteLine(name + ": " + new FileInfo(Path.Combine(dir, name)).Length + " bytes");
}
