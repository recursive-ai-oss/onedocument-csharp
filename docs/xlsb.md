# Binary workbooks (XLSB)

`RecursiveAI.OneDocument.Xlsb` (in the spreadsheets package) reads and writes the Excel binary workbook ([MS-XLSB]). The format is an OPC package whose parts hold records instead of XML. `Workbook.Open` finds it from the content (the workbook part's content type), shows it through the same `Workbook`/`Worksheet`/`Cell` model as SpreadsheetML (`Workbook.IsBinary`), and `SaveAs("x.xlsb")` or `SaveAs(stream, OfficeFormat.Xlsb, loss)` writes it. `XlsbStreamReader` is the forward-only reader.

## Layers

1. **Records** (`XlsbRecordReader`, `XlsbRecordWriter`, `XlsbRecordData`, `XlsbRecordFields`, `XlsbRecordType`). The framing of [MS-XLSB] §2.1.4: a type of one or two bytes, a size of one to four. A record whose declared size passes the end of the part is a `xlsb.record-truncated` defect and ends the part; a record larger than `OfficeLimits.MaxPartSize` raises a limit exception before anything is allocated. Types and sizes are range-checked on write.
2. **Part translators** (`XlsbWorkbookPart`, `XlsbSheetPart`, `XlsbStylesPart`, `XlsbSharedStringsPart`, `XlsbCommentsPart`, `XlsbTablePart`, `XlsbChartSheetPart`) turn one binary part into SpreadsheetML and back. They are lazy: a part is translated the first time the hub asks for it.
3. **Formulas** (`XlsbFormula`, `XlsbFormulaContext`, `XlsbFunctionTable`). Token strings (`Rgce`/`RgbExtra`, §2.5.98) to the public formula tree and back.
4. **The package** (`XlsbBook`). Keeps the binary package as read, and a SpreadsheetML view of it (`Hub`) that the workbook model works on. Parts with no SpreadsheetML counterpart here (pivot caches and tables, connections, external links, the calculation chain, the worksheet binary index, `customXml`, the VBA project) are not in the view; they stay in the binary package and are written back as they were.

## Nothing is dropped silently

A record the translators do not model becomes `<xb:r t="type">base64</xb:r>` (namespace `urn:onedocument:xlsb`, declared `mc:Ignorable`) at its position in the SpreadsheetML view, or beside `sheetData` (`d="b"` before the first row, `d="e"` after the last) when it stands between rows. Writing the part again puts it back where it was. A feature that is translated as a group (data validation, conditional formatting, a filter column, a table) is translated whole or not at all: if one record in the group is not understood, the whole group stays as raw records. Converting to SpreadsheetML removes these elements and reports `loss.xlsb-records-not-converted` with their count.

## What is translated

| Part | Both ways |
|---|---|
| workbook | sheet list and states, 1904 date system, calculation properties (iteration), defined names (global, sheet-scoped, hidden, future-function names), external-sheet table, workbook views; the macro project and other records stay as stored |
| worksheet | cells of every kind (RK, real, string, shared string, rich string, boolean, error, blank, formula cells with their cached result), rows (height, hidden, outline, collapsed, style), columns, sheet properties and tab colour, views (panes, selections, zoom, grid lines), merged cells, hyperlinks, print options, margins, page setup, header and footer, drawing and legacy-drawing relationships, sheet protection, AutoFilter, conditional formatting, data validation, row and column page breaks, ignored errors, table parts |
| styles | number formats, fonts, fills, borders, cell and style formats, named styles, differential formats, table styles, colours (indexed, theme, RGB, tint) |
| shared strings | plain and rich strings, phonetic runs |
| comments | authors, comment text with rich runs |
| tables | name, range, header and totals rows, columns (totals function and label, calculated-column and totals formulas), table style, the table's AutoFilter |

Filters: list filters with date groups, custom filters, top 10 and dynamic filters. Colour and icon filters keep their records raw. Conditional formatting: cell value, expression, text, blanks and errors, time period, above/below average, duplicate and unique values, top/bottom N, colour scales, data bars, icon sets; rules in the newer extension lists (the `x14` forms) stay raw.

Not translated, kept as stored in binary saves and listed in the loss report on conversion: pivot caches and pivot tables, connections and query tables, external links, `customXml`, the calculation chain, scenarios, sort states, phonetic properties, the extension records of newer Excel versions.

## Saving

`Save`/`SaveAs` to the same format writes only what changed: a part whose SpreadsheetML view was not edited is copied byte for byte (the raw zip entry); an edited worksheet is written again from its view; styles and shared strings are written again when they changed. An untouched workbook saves with every part identical. When a sheet is written again, the worksheet binary index (`xlBinaryIndex`) and the calculation chain are dropped, because both hold offsets or cell order that no longer hold; Excel rebuilds them. A rich-text run whose font is not in the style sheet adds it after the existing fonts, so existing cell formats keep their indexes.

Writing from SpreadsheetML (`XLSX → XLSB`) writes every sheet, the style sheet, shared strings, comments and tables from the view. Shared formulas are written as one formula per cell. Chart sheets, dialog sheets and macro sheets raise `OfficeUnsupportedException` (the binary form of a chart sheet is kept only when the binary package itself is saved).

## Formulas

The round trip is AST → tokens → AST; tokens → AST → tokens gives the same bytes. Checked over every function of the function table at its fewest and most arguments, and over the 43,084 formulas of the calc grids.

- Relative references in names, conditional formats and validations are offsets from the first cell of the range (`PtgRefN`, `PtgAreaN`); cell formulas hold absolute coordinates plus relative flags (§2.5.98.18 and following).
- Newer functions are stored as a call of a defined name `_xlfn.NAME` (function number 255); `@` is `_xlfn.SINGLE`, a spill reference `_xlfn.ANCHORARRAY`, and the decoder turns both back into the operators. A call whose callee is computed (`LAMBDA(x,x+1)(3)`) is a user-defined call whose first operand is the expression.
- Structured references (`PtgList`) need the table's name and column names; they come from the table parts.
- Error values Excel computes but never stores (`#BLOCKED!`, `#CONNECT!`, `#BUSY!`, `#FIELD!`, `#UNKNOWN!`) have no code; a formula holding one is written approximately (`loss.xlsb-formula-approximated`).
- The class of each function argument (value, reference, array) follows the function table; the array-class list for the array functions is our reading, to be settled by Excel.

## Streaming

`XlsbStreamReader.Open(path | stream)` and `OpenSheet` give an `XlsbSheetCursor` with the members of the XLSX cursor (`ReadRow`, `ReadCell`, address, style, kind, number, text as UTF-8 or into a caller's span, formula flags, dates). It holds one record at a time; shared strings are loaded once as one pool of UTF-16 text. Reading allocates nothing per cell (checked by `XlsbStreamingTests` against the allocation probe: 4,000 rows of five cells, no bytes beyond the fixed opening cost).

## Defects

See `defects.md`: `xlsb.record-truncated`, `xlsb.record-short`, `xlsb.row-invalid`, `xlsb.cell-column-invalid`, `xlsb.formula-unreadable`, `xlsb.no-main-part`, `xlsb.part-unreadable`. Only `OfficeException` leaves the public API: a record the translators cannot make sense of is `xlsb.part-unreadable` with the part name.

## Loss vocabulary (conversions and binary saves)

| Code | Count is | Meaning |
|---|---|---|
| `loss.xlsb-part-not-converted` | parts | Binary-only parts left out when converting to SpreadsheetML |
| `loss.xlsb-records-not-converted` | records | Raw records (extension and future records) left out |
| `loss.xlsb-part-not-written` | parts | SpreadsheetML parts with no binary form written (pivot tables and caches, connections, query tables, external links, calculation chain, …) |
| `loss.xlsb-sheet-element`, `loss.xlsb-workbook-element` | elements | Elements with no binary form written yet |
| `loss.xlsb-formula-unparsed` | formulas | Formula text that did not parse; the cell keeps its cached value |
| `loss.xlsb-formula-approximated` | formulas | A token the format cannot hold exactly |
| `loss.xlsb-date-text` | cells | ISO 8601 date cells (`t="d"`), written as text |
| `loss.xlsb-data-table` | cells | Data table formulas, written as their cached values |
| `loss.xlsb-dxf-gradient` | formats | A gradient fill in a differential format |
| `loss.xlsb-filter-dropped`, `loss.xlsb-filter-element` | filters | An AutoFilter without a range; filter elements with no binary form |
| `loss.xlsb-conditional-format-dropped`, `loss.xlsb-conditional-format-rule` | blocks, rules | Conditional formatting without a rule or range; rule types with no binary form |
| `loss.xlsb-validation-dropped` | validations | A data validation without a valid range |
| `loss.xlsb-table-dropped` | tables | A table without a valid range |

## Deviations and open questions for Excel

- Shared formulas are expanded to one formula per cell when a sheet is written again (Excel stores one master and `PtgExp` followers).
- `fAnd` of a custom filter: the specification's text says 0 means AND and 1 means OR, against the field's name; we follow the text (LibreOffice reads our file the same way).
- Icon-set `fReverse`: the specification's description of the bit is the opposite of its name; we write the bit as set for `reverse="1"`.
- The array-class parameter list, the error codes `#SPILL!` (0x2C) and `#CALC!` (0x2D), the `fAbove` bit of above-average rules, and the default font of a rich-text run are our reading and are asked of Excel with the generated workbooks.
- Generated inputs for Excel are in `testdata/xlsb/` (`manifest.tsv` says what each covers, with its hash); `XlsbProbe generate <dir>` rewrites them. They are not answered yet.
