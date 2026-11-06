// Open a workbook, change a cell, recalculate and save the result as PDF.
using RecursiveAI.OneDocument.Samples;
using RecursiveAI.OneDocument.Spreadsheets;

string dir = SampleSupport.WorkDirectory(args);
string path = Path.Combine(dir, "sales.xlsx");

using (Workbook created = Workbook.Create())
{
    Worksheet sales = created.Sheets[0];
    sales.Rename("Sales");
    sales["A1"].SetString("Total");
    sales["B1"].Formula = "SUM(B2:B3)";
    sales["B2"].SetNumber(100);
    sales["B3"].SetNumber(200);
    created.SaveAs(path);
}

// The task.
using Workbook wb = Workbook.Open(path);
wb.Sheets["Sales"]["B2"].Value = 1200;
wb.Recalculate();
wb.SaveAs(Path.Combine(dir, "sales.pdf"));

double total = wb.Sheets["Sales"]["B1"].Number;
if (total != 1400)
{
    throw new InvalidOperationException("Expected 1400 after recalculation, got " + total);
}

SampleSupport.RequirePdf(Path.Combine(dir, "sales.pdf"));
