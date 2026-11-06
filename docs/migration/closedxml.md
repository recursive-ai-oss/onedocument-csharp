# Coming from ClosedXML

ClosedXML edits workbooks through `XLWorkbook`, `IXLWorksheet`, `IXLCell` and `IXLRange`; ClosedXML.Report fills templates. OneDocument's equivalents are `Workbook`, `Worksheet`, `Cell`, `Spreadsheet` and `Template`. Read [README.md](README.md) first for what is true on every page.

## Open, read, write, save

```csharp
// ClosedXML
using var wb = new XLWorkbook(path);
IXLWorksheet ws = wb.Worksheet("Sales");
ws.Cell("B2").Value = 1200;
ws.Cell("A2").Value = "North";
double d = ws.Cell("B2").GetDouble();
string s = ws.Cell("A2").GetString();
wb.Save();
```

```csharp
// OneDocument
using Workbook wb = Workbook.Open(path);
Worksheet ws = wb.Sheets["Sales"];
ws["B2"].Value = 1200;
ws["A2"].SetString("North");
double d = ws["B2"].Number;
string s = ws["A2"].Text;
wb.Save();
```

`ws.Cell(row, column)` is `ws[row, column]`. A new workbook is `Workbook.Create()`; `ws.Cell(...).Value = "text"` is `SetString` because a string cell holds a handle into the workbook's string table. `SaveAs(path)` chooses the format by extension (`.xlsx`, `.xlsb`, `.xls`, `.csv`, `.pdf`, ...).

## Formulas

```csharp
// ClosedXML
ws.Cell("B5").FormulaA1 = "SUM(B2:B4)";
wb.RecalculateAllFormulas();
```

```csharp
// OneDocument
ws["B5"].Formula = "SUM(B2:B4)";
wb.RecalculateAll();     // or wb.Recalculate() for only what changed
```

Reading a formula cell returns its **cached** value; ClosedXML evaluates on demand, OneDocument never does until you call `Recalculate`. `FormulaR1C1` has a counterpart in the public formula tree (`docs/calc.md`).

## Styles

```csharp
// ClosedXML
ws.Range("A1:C1").Style.Font.Bold = true;
ws.Cell("B2").Style.NumberFormat.Format = "#,##0.00";
ws.Range("A1:C1").Merge();
```

```csharp
// OneDocument
for (int c = 1; c <= 3; c++)
{
    ws[1, c].UpdateStyle(s => s with { Font = (s.Font ?? new CellFont()) with { Bold = true } });
}

ws["B2"].UpdateStyle(s => s with { NumberFormatCode = "#,##0.00" });
ws.Merge(CellRange.Parse("A1:C1"));
```

There is no `IXLRange` with a shared style object: styles are immutable records applied cell by cell.

## Tables and data

```csharp
// ClosedXML
ws.Cell("A1").InsertTable(orders);
ws.Range("A1:D10").CreateTable("Orders");
```

```csharp
// OneDocument
Spreadsheet.Write(path, orders);                      // [SpreadsheetRow] on the type; also .csv and .tsv
ws.Tables.Add("Orders", CellRange.Parse("A1:D10"));   // a table over cells already there
```

`Spreadsheet.Read<T>(path)` is the typed way back (`docs/spreadsheets-poco.md`). There is no call that inserts a collection at a cell of an open workbook.

## Templates (ClosedXML.Report)

```csharp
// ClosedXML.Report
var template = new XLTemplate(path);
template.AddVariable(invoice);
template.Generate();
template.SaveAs(outPath);
```

```csharp
// OneDocument
Template.Render(path, invoice, outPath);              // the same call for .xlsx, .docx and .pptx
```

The tag syntax differs: OneDocument uses `{{ expression }}` and `{% for item in items %}` with Excel formulas as expressions (`docs/templating.md`, `docs/templating-xlsx.md`). Existing ClosedXML.Report templates must be rewritten.

## PDF

ClosedXML does not render. `Office.Convert("in.xlsx", "out.pdf")` does, with the Rendering package.

## Streaming

ClosedXML loads the whole workbook. For large sheets use `XlsxStreamReader` and `XlsxStreamWriter` (`docs/spreadsheets-streaming.md`).

## Not supported or different

- No range objects (`IXLRange`, `IXLRows`, `IXLColumns`) with bulk operations: loop over cells and rows.
- No `AdjustToContents`, `InsertRowsAbove` style layout helpers; column width and row height are set through `ColumnProperties` and `GetRow(row).Height`.
- No picture, chart or shape creation call. Existing ones are kept and rendered.
- Formulas are not evaluated on read; no `ws.Evaluate("...")` shortcut on the sheet.
- Pivot tables: existing ones are kept and can be refreshed (`docs/spreadsheets-pivots.md`); there is no pivot builder.
