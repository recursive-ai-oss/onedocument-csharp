# Coming from EPPlus 4.x

EPPlus 4.x edits workbooks through `ExcelPackage`, `ExcelWorkbook`, `ExcelWorksheet` and `ExcelRange`. OneDocument's workbook is `Workbook` / `Worksheet` / `Cell`. (Later EPPlus versions have a different licence and are not covered.) Read [README.md](README.md) first for what is true on every page.

## Open, read, write, save

```csharp
// EPPlus 4.x
using var package = new ExcelPackage(new FileInfo(path));
ExcelWorksheet ws = package.Workbook.Worksheets["Sales"];
object v = ws.Cells["B2"].Value;
ws.Cells["B2"].Value = 1200;
ws.Cells["A2"].Value = "North";
package.Save();
```

```csharp
// OneDocument
using Workbook wb = Workbook.Open(path);
Worksheet ws = wb.Sheets["Sales"];
CellValue v = ws["B2"].Value;          // .Text, .Number, .FormattedText read it other ways
ws["B2"].Value = 1200;
ws["A2"].SetString("North");
wb.Save();                              // atomic replace; only the edited sheet part changes
```

A new workbook: `ExcelPackage()` + `Worksheets.Add("Sales")` becomes `Workbook.Create()` + `wb.Sheets.Add("Sales")`. Save to a stream with `wb.SaveAs(stream, OfficeFormat.Xlsx)`; `SaveAs(path)` picks the format from the extension.

## Formulas and calculation

```csharp
// EPPlus 4.x
ws.Cells["B5"].Formula = "SUM(B2:B4)";
package.Workbook.Calculate();
```

```csharp
// OneDocument
ws["B5"].Formula = "SUM(B2:B4)";       // no leading '='
wb.Recalculate();                       // incremental; wb.RecalculateAll() for everything
```

The calculation engine is OneDocument's own (spill, `LET`, `LAMBDA`, structured references). Settings such as iteration are on the workbook (`SetIterativeCalculation`). `docs/spreadsheets-calc.md` lists what is calculated and the documented deviations.

## Styles and number formats

```csharp
// EPPlus 4.x
ws.Cells["A1"].Style.Font.Bold = true;
ws.Cells["B2"].Style.Numberformat.Format = "#,##0.00";
```

```csharp
// OneDocument
ws.Range("A1").UpdateStyle(s => s with { Font = (s.Font ?? new CellFont()) with { Bold = true } });
ws.Range("B2").NumberFormat = "#,##0.00";
```

Styles are immutable records (`CellStyleSpec`); `UpdateStyle` returns a new one, for a cell (`ws["A1"].UpdateStyle`) or a block (`ws.Range("A1:C3").UpdateStyle`). `ws["A1"].FormattedText` gives the text as Excel displays it.

## Tables, names, merges, dates

```csharp
// EPPlus 4.x
ws.Tables.Add(ws.Cells["A1:D10"], "Orders");
ws.Cells["A1:C1"].Merge = true;
ws.Cells["C2"].Value = new DateTime(2026, 3, 1);
```

```csharp
// OneDocument
ws.Tables.Add("Orders", CellRange.Parse("A1:D10"));
ws.Range("A1:C1").Merge();
ws["C2"].SetDate(new DateTime(2026, 3, 1));
wb.Names.Add("Rate", "Sales!$B$1");
```

Conditional formatting and data validation are `ws.ConditionalFormats` and `ws.Validations` (`docs/spreadsheets-styles.md`).

## Loading collections

```csharp
// EPPlus 4.x
ws.Cells["A1"].LoadFromCollection(orders, true);
```

```csharp
// OneDocument: mark the type [SpreadsheetRow] (a partial class or record), then
Spreadsheet.Write(path, orders);                     // or .csv, .tsv
foreach (Order o in Spreadsheet.Read<Order>(path)) { }
```

The binding is generated at compile time (no reflection) and streams: see `docs/spreadsheets-poco.md`. To write the same data into a sheet of an open workbook, loop and set cells.

## Streaming

EPPlus 4.x has no streaming reader or writer. OneDocument has `XlsxStreamReader` / `XlsxStreamWriter` (constant memory, no allocation per cell on read): `docs/spreadsheets-streaming.md`.

## PDF

EPPlus 4.x does not render. `wb.SaveAs("report.pdf")` or `Office.Convert("in.xlsx", "out.pdf")` does, with the Rendering package.

## Not supported or different

- `ws.Range("A1:C3")` is a handle with `Values` (a two-dimensional array), `UpdateStyle`, `NumberFormat`, `Clear` and `Merge`; there is no `Copy`, `AutoFitColumns` or `LoadFromCollection` on it (use `Spreadsheet.Write` for rows of objects).
- `ws.AddPicture(bytes, "E1", ...)` adds a picture. Charts are not created through a high-level call; existing ones are kept and rendered.
- No sparkline, form control or shape creation API.
- Password protection: sheet and workbook protection are supported (`docs/protection.md`); encrypted files are written only as XLS (RC4) for now.
- Cell values are `CellValue` (kind plus payload), not `object`; strings are handles into the workbook's string table, hence `SetString`.
- Rows and columns are 1-based on both sides; `ws[row, column]` mirrors `Cells[row, column]`.
- Templates: EPPlus 4.x has none; see `docs/templating-xlsx.md`.
