# Spreadsheets: streaming cursors

`RecursiveAI.OneOffice.Spreadsheets.Streaming` reads and writes XLSX, XLSM and Flat OPC workbooks as a forward-only stream of rows and cells, beside the workbook hub (`spreadsheets.md`), not through it. Use it when the sheet is larger than you want in memory or when you only need to pass the values through once. Both sides use the hub's `CellValue`, `CellValueKind`, `CellError`, `CellAddress` and `ColumnProperties`, so a value means the same thing in both. Defect codes are in `defects.md` (prefix `xlsx.`).

## Reading

```csharp
using XlsxStreamReader book = XlsxStreamReader.Open("big.xlsx");
using SheetCursor sheet = book.OpenSheet("Data");
while (sheet.ReadRow())
{
    while (sheet.ReadCell())
    {
        switch (sheet.Kind)
        {
            case CellValueKind.Number: total += sheet.Number; break;
            case CellValueKind.Text:   Consume(sheet.TextUtf8); break;   // span, no allocation
        }
    }
}
```

- **Opening** reads the package directory, content types, relationships and the workbook part. The shared string table and the style sheet are read the first time a cell asks for text or a date.
- **Row** (valid after `ReadRow` returns true): `RowNumber`, `RowHeight` (-1 when none), `IsRowHidden`, `HasCustomRowHeight`, `HasCustomRowFormat`, `IsRowCollapsed`, `RowOutlineLevel`, `RowStyle`. `ReadRow` skips the cells of the previous row that were not read.
- **Cell** (valid after `ReadCell` returns true, until the cursor moves): `Column`, `Address`, `Style`, `Kind`, `Value` (a `CellValue`), `Number`, `Boolean`, `Error`, `SharedStringIndex`, `TextUtf8`, `TryCopyText`, `GetText`, `IsIsoDateText`, and for formulas `HasFormula`, `FormulaKind`, `FormulaUtf8`, `GetFormula`, `FormulaRangeUtf8`, `SharedFormulaIndex`. `IsDate` and `TryGetDateTime` go through the style's number format and the workbook's date system.
- **Cached values** are what the cursor returns; opening never recalculates. A formula cell reports the kind of its cached result.
- **Text** is a span of UTF-8 with references resolved and rich-text runs joined (phonetic runs left out). `_xHHHH_` escapes are resolved by `TryCopyText` and `GetText`, not by the span.
- **Order.** Rows and cells are delivered in the order the part stores them. A part that stores them out of order is reported once per row (`xlsx.stream-row-order`, `xlsx.stream-cell-order`) and delivered as stored; the hub sorts and merges instead. Cursor and hub give equal values for every in-order sheet.
- **Memory.** Bounded by the largest token of the sheet part (a long string or formula), plus the shared string table as plain text (the text of every entry plus four bytes per entry) when text is read. Cell data is never held.
- **Allocation.** Zero per cell: numbers, booleans, errors, styles, indexes and spans allocate nothing; only `GetText`, `GetFormula` and the other string-returning members do. A shared string is built once and reused for every cell that uses it, up to a budget of about 8 million characters (about 16 MB); past that, strings are built per call.
- **Failures.** Ill-formed XML or a wrong root raises `OfficeFormatException`; limits raise `OfficeLimitException`; both carry the part and byte offset. Repairs (`xlsx.cell-address-invalid`, `xlsx.cell-value-invalid`, `xlsx.row-index-invalid`, …) go to `OpenContext.Defects` with the cell address. The cancellation token given to `Open` is checked on every `ReadRow`.

### The sheet scanner

A worksheet part is machine-written UTF-8 and is almost all of the work, so the cursor reads it with a scanner built for that shape instead of the general tokenizer: vectorised tag and quote search, attributes sliced on demand, a text-plus-end-tag shortcut for `<v>`, and an exact shortcut for plain decimals of up to 15 digits (a whole number below 2^53 divided by a power of ten is one correctly rounded division, equal to the full parser's result). It reports the same tokens as the general tokenizer for every well-formed part, and hands the part to the general tokenizer for UTF-16. It is more tolerant than the tokenizer in three ways: end tags are matched by depth, not by name; a malformed attribute list ends early; namespace declarations are not checked. It still refuses DTDs, truncated parts, stray end tags and the depth, name and allocation limits. The `stream` fuzz target runs every sheet through both and requires equal cells wherever the tokenizer accepts the part.

## Writing

```csharp
using XlsxStreamWriter book = XlsxStreamWriter.Create("out.xlsx");
int money = book.AddFormat(new StreamCellFormat { NumberFormat = "#,##0.00" });
SheetStreamWriter sheet = book.AddSheet("Data", new SheetWriteOptions { FreezeRows = 1 });
for (int r = 1; r <= rows; r++)
{
    sheet.BeginRow();
    sheet.WriteText(name[r]);
    sheet.WriteNumber(amount[r], money);
}
sheet.Complete();
book.Finish();
```

- Rows are written in order; `BeginRow` starts the next (or a given) row, cells go left to right (`MoveToColumn`, `SkipColumns` leave gaps). `WriteNumber`, `WriteText`, `WriteBoolean`, `WriteError`, `WriteBlank`, `WriteDate`, `WriteValue`; formulas as text with an optional cached result (`WriteFormula`, `WriteFormulaNumber/Boolean/String/Error`). Formula text and style indexes are checked before the cell starts, so a bad argument leaves the sheet writable.
- Styles come from `AddFormat(StreamCellFormat)` (font, fill, border, alignment, number format) or from a caller-supplied `XlsxWriteOptions.StylesXml`. Column widths and hidden/outline state, freeze panes, gridlines and sheet state are `SheetWriteOptions`; `Merge` records merged ranges.
- Strings are shared (up to `MaxSharedStrings` distinct strings, then inline) or all inline, per `XlsxWriteOptions.StringStorage`.
- The writer has its own streaming zip (data descriptors; `Zip64` for parts or archives past 4 GB). Memory is constant in the number of rows: the working set is the shared strings table (capped) plus a few buffers.
- Output validates against the pinned ECMA-376 transitional and OPC schemas and is deterministic: no clock or random value is written.

## Measured (indicative; see the unit report for machines and loads)

Read, 1M x 20 stress sheet: 0.002 bytes allocated per cell, peak working set unchanged from 10k to 1M rows. Write, 1M x 20: constant working set of about 39 MB, 0.03 bytes per cell.
