# Templating: workbooks

`XlsxTemplateBinder.Render(workbook, data, options, xlsxOptions)` (namespace `RecursiveAI.OneDocument.Spreadsheets.Templating`) renders a workbook template with the engine described in `templating.md`. The workbook is edited in place and comes back as the result; sheets with no tag in any cell or in their name are not touched, so they stay byte-identical on save.

## What is a unit

* A **cell** whose text holds a tag (a shared string, an inline string or a rich-text string, read as plain text) is a unit; so are cells without tags, which are carried through unchanged (value, formula, style). Formulas are not templated; they are moved.
* The **sheet name** is the first unit of its sheet, so `Region {{ r.name }}` names a sheet.
* Before the cells of each row (or column, in column mode) comes an empty **marker unit**: it carries the row's own properties (height, hidden, outline level, row style, unknown attributes) and keeps an empty row alive.
* A tag never spans two cells.
* A **comment** is a unit of its cell's row: it repeats with the row and lands on the copy's cell, with its text rendered per copy (the formatting of the first run is kept). A comment on a removed row goes with it.
* A **header or footer string** (`oddHeader`, `evenFooter`, ...) and each **paragraph of a text box** in the sheet's drawing that holds a tag is a unit of the sheet, rendered once per copy of a sheet block. The rendered text goes into the first run of a text-box paragraph (the others are emptied) and replaces a header string whole, so keep the `&L`/`&C`/`&R` codes in the string.

## Values

A cell holding only one `{{ expr }}` becomes a typed value: a number, a boolean, an error, or text; the cell keeps its style, so a number format on the cell formats it. Anything else in a cell (text around a tag, several tags) becomes text. An empty result leaves an empty styled cell. Dates in data are serial numbers inside formulas; format them with the cell's number format or with `TEXT`.

## Pictures

`{% image expr width=N height=N alt=text %}` in a cell adds a picture anchored at that cell: a two-cell anchor (`editAs="oneCell"`) whose size is `width`/`height` in pixels, or the image's own size read from its header (PNG, JPEG, GIF, BMP; pixels are never decoded) when neither is given. With one of the two the other keeps the aspect ratio. The end cell is worked out from the sheet's column widths and row heights (64 px and 20 px when unset). The value is bytes or base64 text. The tag prints nothing in the cell, and inside a row block every copy gets its own picture. Equal images share one media part. A bad value is `template.image-invalid`, located at its cell. The sheet gets a drawing part when it has none. `html` and `markdown` tags stay unbound (`template.tag-not-bound`).

## Scopes

The row is the default scope. A block whose open and close tags sit in different cells repeats the whole rows from the row of the open tag to the row of the close tag, so `{% for %}` in A2 and `{% endfor %}` in D2 repeat row 2, and in A2 and A3 they repeat both rows as a band. Tag-only cells print nothing but keep their place and style in each copy. A block whose tags are in one cell is inline text inside that cell (`scope=inline` says so explicitly).

| Hint | Meaning |
|---|---|
| none, `row`, `table-row` | whole rows, as above |
| `column` | whole columns, same rules, with columns as the unit; a sheet is rendered in column mode when any cell says `scope=column`, and then all blocks on that sheet repeat columns |
| `sheet` | the block covers the whole sheet; each item produces a copy of the sheet, named by the sheet-name unit; nested blocks work inside it |

`{% if %}` follows the same rules: rows are kept or dropped as a whole. A sheet block with no items removes the sheet unless it is the last one. Names are cleaned (`[ ] : * ? / \` become `_`, at most 31 characters) and made unique with ` (2)`, `template.sheet-name-repaired`.

## What follows the moved cells

Rows are positioned like Excel's insert: empty source rows keep their distance; removed rows leave no gap; every copy of a band is contiguous.

* **Formulas** are rewritten on the formula syntax tree. A reference to a cell in a repeated band, from a formula in the same band, goes to the copy in its own iteration (`=B2*C2` in each row). From outside the band a single cell goes to the first copy and a range grows to cover every copy (`SUM(B2:B2)` becomes `SUM(B2:B4)`), as inserting rows inside the range does. A reference to a removed row is `#REF!`; a range loses only the removed ends. Shared formulas are expanded to one formula per cell first. Cross-sheet references follow, including to renamed sheets, and each copy of a sheet block refers to its own cells.
* **Defined names** (including print areas and titles) are rewritten the same way.
* **Merged cells** inside a repeated band are repeated with it; others stretch like ranges.
* **Tables** (`tableParts`) are resized to the rendered range and keep their totals row. Each extra copy of a sheet block gets its own table over the mapped range, named like the template's with `_2`, `_3` appended when taken, and a fresh id.
* **Conditional formatting, data validation, hyperlinks**: their ranges are mapped as ranges (they grow over repeated rows). The formulas of rules and validations (`formula`, `formula1`, `formula2`) are rewritten like cell formulas, with references taken as references from outside any repeated band: a reference to a row of a repeated band goes to its first copy and a range grows. Rules inside `extLst` (x14 forms) are carried only on the template's own sheet, not rewritten.
* **Charts**: the series references of every chart (`c:f` under `numRef`, `strRef`, `multiLvlStrRef`, data-label ranges) on the template's sheets, and of charts on other sheets that plot cells that moved, are rewritten the same way, so a series over a repeated band grows with it. The cached points (`numCache`, `strCache`) are rebuilt from the rendered cells; a formula cell without a cached value contributes no point until the workbook is calculated (with `RecalculateNow` the caches hold the calculated values). A drawing anchored to rows that moved moves with them. Charts over a union or over names are left as they were.
* **Pivot caches** whose `worksheetSource` is a range of a rendered sheet follow the range (and the sheet's new name) and are marked `refreshOnLoad`, since their stored records describe the template. With `RecalculateNow` every pivot table is refreshed by the pivot engine after the recalculation.
* **Copies of a sheet block** also get the template's page setup (`printOptions`, `pageMargins`, `pageSetup`, `headerFooter`, breaks), conditional formats, validations, comments and a duplicate of its drawing (the drawing part and its charts are copied; pictures are shared). Hyperlinks, sheet views and protection are not copied.
* **Row properties** follow their source row into every copy; column properties follow columns in column mode.

After rendering the workbook is marked for a full calculation on open (`fullCalcOnLoad`), and formulas written by the binder carry no cached value. With `XlsxTemplateOptions.RecalculateNow` the calc engine recalculates before the call returns. If the workbook's calculation mode is manual the binder always recalculates, so a stale value is never left behind silently.

## Defects

`template.sheet-overflow` (fatal), `template.sheet-name-repaired`, `template.tag-not-bound`, `template.image-invalid`; the engine's codes (`templating.md`, located at `sheet!cell`) apply to cells.

## Not yet

Tags inside formulas, threaded comments (their text lives in the threaded-comment part; the legacy placeholder comment is what is bound), tags in chart titles and in chart user shapes, x14 conditional-format and validation extensions, data-fed charts, `html` and `markdown` tags in cells, and streaming output for very large reports.

## Checking a template

`XlsxTemplateBinder.Validate(workbook, options)` parses every tag of every sheet (cells, sheet names, comments, headers and footers, text boxes) and returns the errors and warnings with their locations, without binding data and without changing the workbook.
