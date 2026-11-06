# OpenDocument spreadsheets and OpenFormula

ODS, OTS and flat FODS (OpenDocument 1.2 and 1.3) read into the workbook hub and write back out, with a loss report each way. The package layer
(`OdfPackage`, `odf.md`) does the zip, manifest and flat-file work; this page covers the spreadsheet conversion in
`RecursiveAI.OneDocument.Spreadsheets.Odf` and the formula language in `RecursiveAI.OneDocument.Calc.OpenFormula`. Defect codes (`ods.*`,
`openformula.*`) are in `defects.md`.

## Using it

`Workbook.Open` sniffs the content (a `mimetype` of `application/vnd.oasis.opendocument.spreadsheet` or `-template`, or an `office:document` root
for flat files), so the extension never decides. `SaveAs` picks `.ods`, `.ots` or `.fods` by extension, or takes the format explicitly;
`Office.Convert` goes both ways between these and every other workbook format. `OpenDocumentSheet.ToPackage` is the public converter from ODF
bytes to an in-memory XLSX package, for callers who want the OOXML parts.

An ODS that is opened and saved without an edit writes the bytes it was read from. Any edit ends that, and the file is written by the exporter.
Opening never recalculates: the values the file stores stay until `Recalculate`.

## Reading (ODF → hub)

The importer converts the document to an in-memory XLSX package that `Workbook` wraps, as the other legacy readers do.

| ODF | Hub |
|---|---|
| `table:table` | sheet (hidden, tab colour, right-to-left, grid lines, frozen panes and active sheet from `settings.xml`) |
| `table:table-column`, `table:table-row` | widths, heights, hidden and filtered rows and columns, outline groups, header rows and columns; repeats within the limits |
| `table:table-cell`, `covered-table-cell` | cells of the types float, percentage, currency, date, time, boolean and string with their cached values; spans to merged ranges; array formulas |
| `style:style` (cell, column, row, table) | cell formats: font, fill, border, alignment, wrapping, rotation, protection; a style's parent chain is flattened |
| `number:*-style` | Excel number-format codes through the shared number-format service, including conditions, colours, fractions, scientific, text parts |
| `table:named-range`, `named-expression` | defined names (workbook and sheet scope); `Excel_BuiltIn_*` names to built-in names (print areas and titles, filter databases) |
| `table:database-range` | auto-filters (without conditions) |
| `calcext:conditional-format`, `style:map` | conditional formats: cell-value and formula conditions with differential formats, colour scales, data bars, icon sets |
| `table:content-validations` | data validation (list, whole, decimal, date, time, text length, custom) with messages |
| `office:annotation` | comments |
| `text:a` in a cell | hyperlinks |
| `style:page-layout`, `master-page`, print ranges | page setup, margins, scaling, headers and footers, print areas and titles |
| `office:meta` | document properties |

Reported as losses (`loss.*` on `Workbook.ImportLoss`): shapes, pictures and charts on a sheet and body content without a counterpart are reported
under the element's name (`loss.shapes`, `loss.data-pilot-tables`, `loss.scenarios` and so on), then `sparklines`, `tracked-changes`,
`protection-password` (the hash is a different algorithm), `filter-conditions`, `database-ranges` (a named range keeps only its cells), `rich-text`
(formatting inside a cell's text; the text is kept), `number-format` (parts of a data style Excel codes cannot express), `conditional-formats` (rules
with no Excel counterpart), `validation`, `null-date` (a date origin other than 1899-12-30 is converted) and `formulas` (see below).

Formulas are read as OpenFormula. A formula that does not parse, uses a syntax other than `of:` (`oooc:`, `msoxl:`), or points at another
document keeps its cached value as a constant and reports `ods.bad-formula` or `ods.formula-dialect` with the cell address.

Limits: repeats are counted against the cell budget from `OfficeLimits` (`odf.repeat-capped`); rows and columns past the sheet's size are dropped
(`ods.row-limit`, `ods.column-limit`); an empty repeated row or cell costs nothing.

## Writing (hub → ODF)

The exporter reads the hub through its public model and each sheet's markup frame, and writes `content.xml`, `styles.xml`, `meta.xml` and
`settings.xml` (view settings: active sheet, freeze panes, zoom) through `OdfPackage.Create`. Flat output writes the same four parts into one
`office:document`. Every format written is checked against the ODF 1.3 RelaxNG schema (see Checks).

Styles are written as automatic styles, de-duplicated; number formats become `number:*-style` elements with the same meaning; formulas are
printed as OpenFormula with `of:=`; the cached value is written beside every formula (`office:value`, `office:string-value`,
`calcext:value-type="error"` on error cells).

Reported as losses (`ExportLoss`): `charts`, `pictures`, `drawings`, `pivot-tables`, `vba`, `external-links`, `tables` (the table definition; its
cells and styles are kept), `validation` (a rule ODF cannot express), `formulas` (dynamic arrays and spill, `LAMBDA`, structured references,
data tables: the cached value is written), and `function-not-portable` (a function LibreOffice does not know is written anyway, and other readers
show `#NAME?` after recalculating).

The content part is built in memory before it is written, so a very large sheet needs memory in proportion; `MaxPartSize` bounds it.

## OpenFormula

`OpenFormulaParser.TryParse(text, out node, out error)` reads OpenDocument 1.3 Part 4 into the public formula AST; `OpenFormulaPrinter.Print(node)`
prints an AST back. Both ends use the same AST as the A1, R1C1 and BIFF forms, so a formula moves between notations without a separate model.

Syntax handled:

- the `of:=` prefix is stripped; `;` separates arguments, inline arrays use `;` between columns and `|` between rows (`{1;2|3;4}`), `~` is union, `!` is intersection;
- references are bracketed: `[.A1]`, `[.$A$1:.B2]`, `[$Sheet1.A1:.B2]`, `[$'My Sheet'.A1]`, whole columns and rows (`[.A:.A]`, `[.1:.3]`), 3D ranges (`[$S1.A1:$S3.A1]`); a reference into another document is `openformula.external-reference`;
- errors: `#N/A`, `#REF!` (printed lower-case `#ref!` as LibreOffice does), `#DIV/0!`, `#NAME?`, `#NULL!`, `#NUM!`, `#VALUE!`;
- `TRUE()`/`FALSE()`, percent, unary minus precedence as Excel (`-A1^2` is `(-A1)^2`), string escapes by doubling;
- function names with `COM.MICROSOFT.`, `ORG.OPENOFFICE.`, `ORG.LIBREOFFICE.` and `LEGACY.` prefixes in any case; unknown `ORG.*` names are kept as the engine's names.

Function names (`OpenFormulaFunctions`): almost all names are the same as Excel's. The differences, taken from the files the reference reader writes
(`testdata/ods/openformula-functions.tsv` lists the printed form of every function the engine has, 509 rows; `openformula-expressions.tsv` lists 72
expression forms):

| Rule | Examples |
|---|---|
| `COM.MICROSOFT.` prefix on functions Excel added in 2010 and later | `STDEV.S`, `NORM.DIST`, `CONCAT`, `IFS`, `XLOOKUP`, `FILTER`, `SORT`, `LET`, `TEXTJOIN` |
| `LEGACY.` prefix on the pre-2010 statistical names that have replacements | `CHIDIST`, `FDIST`, `NORMSDIST`, `TDIST` |
| spelled differently | `F.DIST` ↔ `FDIST`, `F.INV` ↔ `FINV`, `SKEW.P` ↔ `SKEWP`, `FORMULATEXT` ↔ `FORMULA` |

Functions the engine has and OpenFormula (as the reference reader knows it) does not: `LAMBDA`, `MAP`, `REDUCE`, `SCAN`, `BYROW`, `BYCOL`,
`MAKEARRAY`, `GROUPBY`, `PIVOTBY`, `PERCENTOF`, `REGEXEXTRACT`, `REGEXREPLACE`, `REGEXTEST`, `TRIMRANGE`, `ARRAYTOTEXT`, `VALUETOTEXT`,
`BAHTTEXT`, `PHONETIC`, `TRANSLATE`, `DETECTLANGUAGE`, and a few complex-number and `BINOM.DIST.RANGE` functions. The printer writes them
unchanged and reports `openformula.function`, which the exporter turns into `loss.function-not-portable`. Functions with the same name and a
different result or argument order are not known to exist in the shared set; the 509-row grid is the evidence.

The printer cannot express, and reports as not portable: spill references (`A1#`), structured references, member paths, `LAMBDA` invocation,
`@`. The exporter then writes the cached value without a formula and reports `loss.formulas`.

## Deviations

- Cell styles are flattened: a cell's parent-style chain becomes one cell format, so a round trip through the hub does not keep the style hierarchy.
- A space-preserving `_)` in an accounting-style number format has no counterpart in ODF and becomes a space.
- `calcext:value-type` is written only on error cells (the one extension attribute in an otherwise strict file); `tableooo:tab-color` carries the tab colour. Both are accepted by the reference reader and ignored by strict ODF 1.3 readers.
- Formulas in a syntax other than `of:` are not read.
- Charts, pictures and shapes are loss items; the cell values they were built on are kept.

## Checks

- `tools/ods/make_inputs.py` and `make-oracle-fixtures.sh` produce the oracle fixtures (`testdata/ods`, LibreOffice 26.8.2.2 as a black box; see `oracle-version.txt`).
- `tools/ods/run-oracle-check.sh` converts the workbooks in `testdata` to ODS and FODS, validates them with Jing against the ODF 1.3 schema
  (`tools/validate/validate_odf.py --ignore-extensions`: the `calcext:` and `loext:` extension attributes are not part of the schema), has the reference reader convert each
  file back to XLSX and compares values and formulas with the source (`OdsOracleTests`).
- Fuzz targets `ods` (the reader and the exporter) and `openformula` (the parser and printer).
