# RecursiveAI.OneOffice.Spreadsheets

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

This package is the spreadsheet part of OneOffice, an early preview. It opens, edits and saves XLSX, XLSM, XLTX and Flat OPC workbooks, reads and writes CSV and TSV, and streams rows and cells of very large sheets. There is no calculation engine in this release: cached values are what you read, and opening never recalculates.

- **Lossless.** Parts you did not edit are written back byte-identical. Editing one cell rewrites only its sheet part; unknown row and cell attributes, `extLst` and `mc:AlternateContent` survive.
- **Compact.** Cells live in a columnar, sparse store: about 37 bytes per cell on a workbook with styles, strings and formulas, and about 0 bytes allocated per cell while iterating.
- **Streaming.** `XlsxStreamReader` and `XlsxStreamWriter` work forward-only at constant memory with no allocation per cell.
- **Typed rows.** Mark a `partial` class, struct or record with `[SpreadsheetRow]` and `Spreadsheet.Read<T>(path)` / `Spreadsheet.Write(path, rows)` move it to and from XLSX, CSV and TSV in one call, streaming. A source generator that ships in this package writes the binding when you compile, so there is no reflection and it works under trimming and NativeAOT.
- **Safe.** The format is found from the content, never the extension. Damage is reported as typed defects with cell addresses or part offsets, and resource limits are on by default.
- **No setup.** `Office.Open` reaches workbooks as soon as this package is referenced; no registration call is needed.
- **Excel's number formats** (TEXT and cell display, 14 locales) are a shared service in the core package.

```csharp
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Streaming;

[SpreadsheetRow(Sheet = "Orders")]
public partial record Order(int Id, string Customer, decimal Total);

Spreadsheet.Write("orders.xlsx", orders);                       // or .csv, .tsv
foreach (Order o in Spreadsheet.Read<Order>("orders.xlsx")) { }  // lazy, streaming

using var wb = Workbook.Open("sales.xlsx");
wb.Sheets[0]["B2"].SetNumber(1200);
wb.Save();                                    // atomic replace; only the edited sheet part changes

using XlsxStreamReader book = XlsxStreamReader.Open("big.xlsx");
using SheetCursor sheet = book.OpenSheet(0);
while (sheet.ReadRow())
{
    while (sheet.ReadCell())
    {
        if (sheet.Kind == CellValueKind.Number) { total += sheet.Number; }
    }
}
```

See the [repository](https://github.com/recursive-ai-oss/oneoffice-csharp) for the status table and measurements, and the [changelog](https://github.com/recursive-ai-oss/oneoffice-csharp/blob/main/CHANGELOG.md).

Licensed `MIT OR Apache-2.0`, © Recursive AI, LLC. Not affiliated with Microsoft or with any other product or company named OneOffice.
