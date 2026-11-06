# Coming from MiniExcel

MiniExcel moves rows of data and dictionaries in and out of XLSX and CSV with static methods and fills workbook templates. OneDocument has the same one-call shape for typed rows (`Spreadsheet.Read<T>` / `Write`), a forward-only cursor for the lowest-level streaming, the full workbook model when you need cells and styles, and `Template.Render` for templates. Read [README.md](README.md) first for what is true on every page.

## Typed rows

```csharp
// MiniExcel
MiniExcel.SaveAs(path, orders);
IEnumerable<Order> back = MiniExcel.Query<Order>(path);
```

```csharp
// OneDocument: Order is a partial class or record marked [SpreadsheetRow]
Spreadsheet.Write(path, orders);                 // .xlsx, .csv or .tsv by extension
IEnumerable<Order> back = Spreadsheet.Read<Order>(path);
```

MiniExcel binds by reflection at run time; OneDocument binds with code generated when you compile, so it works under trimming and NativeAOT. Column names, order, formats and ignoring are attributes on the members (`[SpreadsheetColumn]`, `docs/spreadsheets-poco.md`). Asynchronous forms: `Spreadsheet.ReadAsync<T>` and `WriteAsync`.

## Untyped rows

```csharp
// MiniExcel
foreach (dynamic row in MiniExcel.Query(path)) { Console.WriteLine(row.A); }
```

```csharp
// OneDocument: one object array per row, no dynamic
foreach (object?[] row in Spreadsheet.ReadRows(path, sheet: "Sheet1")) { Console.WriteLine(row[0]); }
```

## Writing dictionaries and tables

MiniExcel's `SaveAs` also takes dictionaries and `DataTable`/`IDataReader`. In OneDocument, write a typed record, or open a `Workbook` and set cells, or use the streaming writer:

```csharp
// OneDocument: streaming write, constant memory
using XlsxStreamWriter book = XlsxStreamWriter.Create(path);
SheetStreamWriter sheet = book.AddSheet("Data");
foreach (Order o in orders)
{
    sheet.BeginRow();
    sheet.WriteNumber(o.Id);
    sheet.WriteText(o.Customer);
}

sheet.Complete();
book.Finish();
```

## Templates

```csharp
// MiniExcel
MiniExcel.SaveAsByTemplate(outPath, templatePath, new { Title = "Report", Items = items });
```

```csharp
// OneDocument
Template.Render(templatePath, new Report { Title = "Report", Items = items }, outPath);
```

The data type is marked `[TemplateModel]` (generated binding), or pass a dictionary, JSON or a `DataTable`. Tags are `{{ title }}` and `{% for item in items %}` with Excel formulas as expressions (`docs/templating.md`, `docs/templating-xlsx.md`); MiniExcel's own template syntax is not read, so templates are rewritten once. The same call fills `.docx` and `.pptx` templates.

## CSV

```csharp
// MiniExcel
MiniExcel.SaveAs("out.csv", orders);
```

```csharp
// OneDocument
Spreadsheet.Write("out.csv", orders);            // formula-injection safe by default; delimiter detected on read
```

CSV details: `docs/csv.md`.

## Not supported or different

- No `QueryAsDataTable` or `GetSheetNames` statics: open a `Workbook` (`wb.Sheets`) or use `XlsxStreamReader`.
- No dynamic rows and no `Dictionary<string, object>` output from the typed binding.
- No `MiniExcel.Insert` (append to an existing file); open the workbook, add rows, save.
- No image or chart embedding option on `SaveAs`; the template engine inserts pictures (`{% image %}`).
- Typed binding needs `partial` types marked `[SpreadsheetRow]`, so types you do not own cannot be bound directly; map them to a record you own.
