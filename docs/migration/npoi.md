# Coming from NPOI

NPOI has three families that matter here: `XSSF*` (OOXML workbooks), `HSSF*` (legacy `.xls`) and `XWPF*` (Word documents). OneDocument has one model per kind (`Workbook`, `Document`, `Presentation`) that opens whichever file format holds it. Read [README.md](README.md) first for what is true on every page.

## Workbooks: open, read, write, save

```csharp
// NPOI
using var fs = File.OpenRead(path);
IWorkbook wb = new XSSFWorkbook(fs);          // HSSFWorkbook for .xls, or WorkbookFactory.Create
ISheet sheet = wb.GetSheet("Sales");
IRow row = sheet.GetRow(1) ?? sheet.CreateRow(1);
ICell cell = row.GetCell(1) ?? row.CreateCell(1);
cell.SetCellValue(1200);
using var outFs = File.Create(outPath);
wb.Write(outFs);
```

```csharp
// OneDocument
using Workbook wb = Workbook.Open(path);      // .xlsx, .xlsb and .xls all open as a Workbook
Worksheet ws = wb.Sheets["Sales"];
ws[2, 2].Value = 1200;                        // NPOI rows and columns are 0-based; here they are 1-based
wb.SaveAs(outPath);                           // .xlsx, .xlsb, .xls, .csv or .pdf by extension
```

No row or cell creation step: assigning a value creates the cell. Row and column numbers are **1-based**.

## Formulas and evaluation

```csharp
// NPOI
cell.SetCellFormula("SUM(B2:B4)");
wb.GetCreationHelper().CreateFormulaEvaluator().EvaluateAll();
```

```csharp
// OneDocument
ws["B5"].Formula = "SUM(B2:B4)";
wb.RecalculateAll();
```

## Styles

```csharp
// NPOI
ICellStyle style = wb.CreateCellStyle();
style.DataFormat = wb.CreateDataFormat().GetFormat("#,##0.00");
cell.CellStyle = style;
```

```csharp
// OneDocument
ws["B2"].UpdateStyle(s => s with { NumberFormatCode = "#,##0.00" });
```

The workbook's style table is managed for you; a style is a record, not a shared object to create and attach.

## Legacy .xls

OneDocument reads BIFF2 to BIFF8 and writes one BIFF8 dialect (`wb.SaveAs("out.xls")`, or with a password for RC4 encryption). HSSF's low-level record classes have no counterpart; the record layer is `RecursiveAI.OneDocument.Spreadsheets.Biff` (`docs/xls.md`).

## Word documents (XWPF)

```csharp
// NPOI
using var fs = File.OpenRead(path);
var doc = new XWPFDocument(fs);
foreach (XWPFParagraph p in doc.Paragraphs) { Console.WriteLine(p.Text); }
XWPFParagraph np = doc.CreateParagraph();
np.CreateRun().SetText("Added");
using var outFs = File.Create(outPath);
doc.Write(outFs);
```

```csharp
// OneDocument
using Document doc = Document.Open(path);       // also .doc, .rtf, .odt
foreach (Paragraph p in doc.Paragraphs) { Console.WriteLine(p.Text); }
doc.Body.AddParagraph("Added");
doc.SaveAs(outPath);
```

Paragraph style is `p.StyleId`; `Body.AddParagraph(text, styleId)` sets it. Tables are `doc.Tables` with cell and row edits (`docs/docx-tables-sections.md`); there is no call that creates a table. Character formatting of a run is edited through the typed view `run.View` (`docs/docx-styles-numbering.md`).

## Text extraction

```csharp
// NPOI (the extractor classes)
var extractor = new XWPFWordExtractor(doc);
string text = extractor.Text;
```

```csharp
// OneDocument
string text = Office.ExtractText(path);          // any supported format, streaming where it can
```

## Streaming

NPOI's `SXSSFWorkbook` streams writes. OneDocument's `XlsxStreamWriter` and `XlsxStreamReader` stream both ways (`docs/spreadsheets-streaming.md`).

## Not supported or different

- No `HSLF`/`XSLF` (PowerPoint) object model matching: use `Presentation` (`docs/presentations.md`); `.ppt` is read-only.
- No writing of `.doc` or `.ppt`.
- No picture, drawing or chart creation call (existing ones are kept and rendered).
- No `IRow` / `IFormulaEvaluator` objects; no `ICellStyle` instances.
- `.xls` support is BIFF8 for writing; the formula evaluation engine is not NPOI's, and results are documented in `docs/spreadsheets-calc.md`.
