# Spreadsheets: the workbook hub and the cell store

`RecursiveAI.OneOffice.Spreadsheets` holds the SpreadsheetML hub: `Workbook`, `Worksheet`, `Cell`, the cell store and the shared string table. This note states what they do; the XML documentation on each type gives the details. Defect codes are in `defects.md` (prefix `xlsx.`).

## What is read, when

`Workbook.Open` (path, stream or `IByteSource`) identifies the content (never the extension), opens the package, and reads the content types, the package relationships, the workbook part and its relationships. Nothing else.

| Part | Read when |
|---|---|
| the workbook part | open (it holds the sheet list) |
| a worksheet part | the first use of that sheet's `Store`, a cell or its row, column or merge data |
| the shared string table | a string cell is read as text, or a string is set |
| styles, themes, drawings, tables, pivots, calc chain, … | never; they stay as stored |

A chart sheet, dialog sheet or macro sheet is listed (`Worksheet.Kind`) and never opened. Opening never recalculates.

## The round-trip contract

- A part nobody edited is copied by the package as stored (the raw zip entry), so a workbook opened and saved without edits has every part byte-identical. Reading a sheet does not edit it.
- A sheet is **edited** when its store was changed (a value, style, formula, row property, a removed cell), when its columns or merged cells were set, or when `Worksheet.Rewrite()` forces it. An edited sheet is written from the store; everything else changes nothing else.
- In an edited sheet every element around `sheetData` stays where it was, in order, with its prefixes, `mc:Ignorable`, `extLst`, `x14ac:` attributes and unknown elements: the bytes before and after `sheetData` are kept, and the few elements the model edits (`dimension`, `cols`, `mergeCells`) are changed in a small markup tree built from those bytes. Unknown attributes on rows, cells and formulas and unknown children of rows and cells are kept in side tables and written back unchanged.
- What an edited sheet does **not** keep exactly: the `spans` hint on rows is dropped (it is derived; Excel rebuilds it); attribute order on a cell is `r`, `s`, `t`, then the unknown ones; a cell's children are written in the order `f`, `v`/`is`, unknown; numbers are written in the shortest form that reads back to the same double; a self-closed `<sheetData/>` becomes an element with content when rows exist. Values, types, styles, formulas, row and column properties and merges read back equal (checked over the corpus; see the unit report).
- A sheet that cannot be read (ill-formed XML, wrong root) raises a typed `OfficeFormatException` when it is first touched; it is never replaced by an empty sheet on save, because it stays an untouched part.

### What one cell edit changes

An edit changes the part of its sheet, and the following, only when required:

- **`xl/sharedStrings.xml`** when a string is set (new entries are appended; stored entries, rich text and phonetic runs are not rewritten; `uniqueCount` is exact, `count` is advisory).
- **`xl/workbook.xml`** when a formula is set: `calcPr` gets `fullCalcOnLoad="1"` (created if absent; left alone when calculation is manual), so Excel recalculates on load. Nothing is calculated here.
- **`xl/calcChain.xml`** (removed, with its relationship and content-type override) when a formula was removed or replaced, a formula cell was cleared, or the sheet list changed: Excel reports a damaged file for a chain that lists a cell without a formula or a sheet that is gone, and rebuilds the chain itself. Adding a formula needs no change to the chain.
- `[Content_Types].xml` and `_rels` follow part additions and removals.

## The cell store

`CellStore` (per sheet) keeps used cells only. Rows are sparse and sorted; each row keeps its cells as parallel arrays sorted by column ("struct of arrays"): a `ushort` column, a kind byte, an 8-byte payload, a 4-byte style index, and, in rows that hold formulas, a 4-byte index into the sheet's interned formula records. That is 15 bytes per cell (19 with formulas) plus about 250 bytes per row, with no object per cell. Lookup is two binary searches; iteration by row is a linear scan of dense arrays; iteration by column visits each row once (`EnumerateColumn`).

| Kind byte | Meaning |
|---|---|
| bits 0-2 | `CellValueKind`: empty, number, text, boolean, error |
| bit 3 | a formula is attached (the value is its cached result) |
| bit 4 | the text is an ISO 8601 date (`t="d"`), written back as such |

### The value representation

`CellValue` is a 16-byte value type: a kind, a double (numbers; 0 or 1 for booleans) and an int. Reading a cell allocates nothing. The calculation layer builds its array value model on it.

- **Number**: the double. Dates stay numbers (the 1900/1904 rules are another layer's).
- **Boolean**: 1.0 or 0.0.
- **Error**: a `CellError` number (`#NULL!` … `#BUSY!`). An error text the table does not know reads as `CellError.Unknown` and keeps its text in the sheet pool.
- **Text**: a handle, not a string. A non-negative handle is an index into the shared string table; a negative handle is the complement of an index into the sheet's own string pool (inline strings, text results of formulas, ISO dates). `Worksheet.GetText(value)` or `Cell.Text` resolves it. Handles are valid for the workbook they came from.
- **Formula**: kept as text with its kind (`Normal`, `Shared`, `Array`, `DataTable`), its range and shared index, and the other attributes of `f` as written. The cells of a shared formula that are not its anchor have empty text and the shared index; translating the anchor's text for them is the formula layer's job. Identical records are interned, so a shared formula costs one record per `si`.

Text goes through ST_Xstring: `_x000D_` style escapes are decoded on read and written for characters XML cannot carry (and for text that looks like an escape).

### Row and column data

Row properties are in the store (`Worksheet.GetRow(n)`: height, hidden, outline level, collapsed, style); column runs (`Worksheet.Columns`, `SetColumns`), merged ranges (`MergedCells`, `Merge`, `Unmerge`), the declared dimension and frozen panes are read from the markup around `sheetData`. Writing the column and merge lists replaces those elements at their schema position; the `dimension` of an edited sheet is set to the used range when the part had one.

## Sheets

`Workbook.Sheets` lists the sheets in order (`Name`, `State`, `Kind`, `SheetId`). `Add`, `Insert`, `Remove`, `Move` and `Worksheet.Rename` edit the sheet list. They keep each existing `sheet` element exactly as written, remap `localSheetId` of defined names and the active tab, remove names scoped to a removed sheet (and an emptied `definedNames`), and remove a deleted sheet's part and the parts only it uses (drawings, tables, comments). **Renaming does not rewrite formulas, defined names or chart references that mention the old name; that belongs to the formula layer.** A workbook keeps at least one visible sheet. `Workbook.Create()` writes the same bytes on every run.

## Saving

`Save()` is the package's atomic replace (temp file and rename). `SaveAs(path)` picks the format by extension (`.xml` is Flat OPC); `SaveAs(stream, format)` writes XLSX, XLSM, XLTX, XLTM, XLAM or Flat OPC. Changing between the zip formats changes the main part's content type only; saving a workbook that holds a macro project as XLSX or XLTX is refused (`OfficeUnsupportedException`). The workbook's own `Format` does not change.

## Limits

`MaxFormulaLength` bounds a formula (read and set); `MaxRecordCount` bounds the cells of a sheet and the entries of the string table; `MaxXmlDepth` and the other XML limits apply to every part; a row may not hold more than 2^20 cells before the duplicates are resolved. The sheet reader streams the part: memory is the store, not the XML.

## Known limits of this layer

- Styles are indices; the style model, number formats, dates, tables, conditional formatting, data validation and defined-name semantics are other layers and stay untouched.
- A UTF-16 sheet part is read and written as UTF-8 (the declaration is replaced).
- Adding a string looks for an equal string only among those added in the session.
