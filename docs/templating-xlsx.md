# Templating: workbooks

`XlsxTemplateBinder.Render(workbook, data, options, xlsxOptions)` (namespace `RecursiveAI.OneDocument.Spreadsheets.Templating`) renders a workbook template with the engine described in `templating.md`. The workbook is edited in place and comes back as the result; sheets with no tag in any cell or in their name are not touched, so they stay byte-identical on save.

## What is a unit

* A **cell** whose text holds a tag (a shared string, an inline string or a rich-text string, read as plain text) is a unit; so are cells without tags, which are carried through unchanged (value, formula, style). Formulas are not templated; they are moved.
* The **sheet name** is the first unit of its sheet, so `Region {{ r.name }}` names a sheet.
* Before the cells of each row (or column, in column mode) comes an empty **marker unit**: it carries the row's own properties (height, hidden, outline level, row style, unknown attributes) and keeps an empty row alive.
* A tag never spans two cells. Tags in comments, headers and footers, text boxes and drawings are not bound yet (see Not yet).

## Values

A cell holding only one `{{ expr }}` becomes a typed value: a number, a boolean, an error, or text; the cell keeps its style, so a number format on the cell formats it. Anything else in a cell (text around a tag, several tags) becomes text. An empty result leaves an empty styled cell. Dates in data are serial numbers inside formulas; format them with the cell's number format or with `TEXT`.

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
* **Tables** (`tableParts`) are resized to the rendered range and keep their totals row. Tables of the extra copies of a sheet block are not created.
* **Conditional formatting, data validation, hyperlinks**: their ranges are mapped as ranges (they grow over repeated rows). Rule formulas are not rewritten.
* **Row properties** follow their source row into every copy; column properties follow columns in column mode.

After rendering the workbook is marked for a full calculation on open (`fullCalcOnLoad`), and formulas written by the binder carry no cached value. With `XlsxTemplateOptions.RecalculateNow` the calc engine recalculates before the call returns. If the workbook's calculation mode is manual the binder always recalculates, so a stale value is never left behind silently.

## Defects

`template.sheet-overflow` (fatal), `template.sheet-name-repaired`, `template.tag-not-bound`; the engine's codes (`templating.md`, located at `sheet!cell`) apply to cells.

## Not yet

Data-fed images (`{% image %}` prints nothing and reports `template.tag-not-bound`), tags in headers and footers, comments and text boxes, tags inside formulas, chart series and pivot cache source ranges, rewriting of conditional-format and validation rule formulas, a copy of a sheet's tables, drawings and page setup for the extra copies of a sheet block (they get cells, row and column properties and merged cells, but not the sheet's drawings, tables, conditional formats, validations or print setup), and streaming output for very large reports.
