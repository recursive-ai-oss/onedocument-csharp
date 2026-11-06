// Write a list of records to a workbook and read it back as records: one line each, in constant memory.
using RecursiveAI.OneDocument.Samples;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Binding;

string path = Path.Combine(SampleSupport.WorkDirectory(args), "orders.xlsx");
Order[] orders =
[
    new(1, "Ada", 120.5m, new DateOnly(2026, 3, 1)),
    new(2, "Grace", 80m, new DateOnly(2026, 3, 2)),
];

// The task.
Spreadsheet.Write(path, orders);
List<Order> back = Spreadsheet.Read<Order>(path).ToList();

if (!back.SequenceEqual(orders))
{
    throw new InvalidOperationException("The rows read back differ from the rows written.");
}

Console.WriteLine(back.Count + " rows round-tripped through " + Path.GetFileName(path));

/// <summary>Marking a partial type lets a source generator write its reading and writing code.</summary>
[SpreadsheetRow(Sheet = "Orders")]
internal sealed partial record Order(int Id, string Customer, decimal Total, DateOnly Placed);
