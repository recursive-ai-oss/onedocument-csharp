# Pivot caches and tables

Namespace `RecursiveAI.OneDocument.Spreadsheets.Pivots`, in the `.Spreadsheets` package. Entry points: `Workbook.PivotCaches`, `Worksheet.PivotTables`,
`PivotTable.Refresh`, `Workbook.RefreshPivots`, and `GETPIVOTDATA` in formulas. Sources: ECMA-376 Part 1 §18.10 (`pivotCacheDefinition`,
`pivotCacheRecords`, `pivotTableDefinition`) and the shape of the files Excel writes. Where the schema is silent the choice below is the first reading;
those marked *(pending the Excel check)* are settled by the sample workbooks in `testdata/pivots`, which are generated and committed unanswered and run
through Excel once, at the gate.

## What is read, and what stays as it was

The cache definition, its records part and each pivot table definition are kept as markup trees over their parts, so unknown elements and attributes,
prefixes and `extLst` stay where they are. A workbook that is opened and saved without a refresh writes every pivot part byte-identical. The parts are
read when first used: `Workbook.PivotCaches` (from the `pivotCaches` element and its relationships) and `Worksheet.PivotTables` (from the sheet's
`pivotTable` relationships). A part that is missing or is not the expected element is reported as a defect and left as stored
(`xlsx.pivot-*`, `defects.md`).

`PivotCache` exposes the id, the source (kind, sheet, range, name), `RefreshOnLoad`, `SaveData`, the record count and the field names. `PivotTable`
exposes the name, cache, location, the row, column and page field indexes (`-2` is the data fields) and the data fields (function, show-values-as,
base field and item, number format).

## Refresh

`PivotTable.Refresh()` and `Workbook.RefreshPivots()` rebuild the cache from its worksheet, table or defined-name source, then every table that uses the
cache (the cache's shared item indexes change, so a sibling table cannot be left alone). The work is done in memory first: a cancelled refresh, a
source that cannot be read and a cache that is not refreshable change nothing. The result lists one outcome per table:

| Status | When |
|---|---|
| `Refreshed` | the cache and the table were rebuilt |
| `NotSupported` | the cache source is external, a cube, the Data Model, a consolidation or a scenario (kept as stored), or the table has no cache |
| `SourceInvalid` | the source sheet, range, table or name is missing, or a header cell of the source is empty |

Refreshing writes the cells of the table into its sheet, so formulas that read them see the new values at the next recalculation; refresh itself
recalculates nothing. Nothing in the written bytes depends on the clock: `refreshedDate` and `refreshedBy` change only when the options give them.

### The cache

Source columns become cache fields, matched to the stored fields by name (case-insensitive); a header repeated in the source is read as `Name2`,
`Name3` as Excel does. Calculated fields and group fields (`databaseField="0"`) follow the columns in their stored order. If the columns changed, every
reference in the table (`rowFields`, `colFields`, `pageFields`, `dataFields`, `fieldGroup base`) is remapped; a reference to a column that is gone is
dropped with a defect.

- **Values.** Numbers, text, booleans, errors, dates (a number whose cell format is a date format) and blanks are read from the cells. A pivot cache
  lists text without regard to case: "a" and "A" are one item and the first spelling is kept.
- **Shared items.** A field lists its items (`sharedItems` with `s`/`n`/`b`/`e`/`d`/`m`, in order of first appearance, and `<x v>` in the records)
  when it holds anything but numbers and blanks, is on an axis of any table, or is grouped. A field of only numbers (and blanks) that is not on an axis
  stores them inline (`<n v>`, `<m/>`) and its `sharedItems` carries only the flags and `minValue`/`maxValue`. *(pending the Excel check)*
- **Flags.** `containsSemiMixedTypes`, `containsNonDate`, `containsDate`, `containsString`, `containsBlank`, `containsMixedTypes`, `containsNumber`,
  `containsInteger`, `minValue`/`maxValue`, `minDate`/`maxDate`, written only where they differ from the schema default (ECMA-376 Part 1 §18.10.1.90).
- **Records.** One `r` per source row; calculated and group fields have no entry. A cache that stores no records (`saveData="0"`) still stores none.
  A records part that did not exist is added, with its relationship and the definition's `r:id`.
- `recordCount` is updated.

### Grouping

A cache field with `fieldGroup` is recomputed from the base field's shared items.

| `groupBy` | Group items | Notes |
|---|---|---|
| `years` | `<start`, one item per year from the first to the last, `>end` | |
| `quarters` | `<start`, `Qtr1`…`Qtr4`, `>end` | |
| `months` | `<start`, `Jan`…`Dec`, `>end` | years are merged unless a years group is also on the table |
| `days` | `<start`, 366 items `d-Mon` (29 Feb included), `>end`; with `groupInterval` > 1, one item per interval `d-Mon - d-Mon` | |
| `hours`, `minutes`, `seconds` | `<start`, `0`…`23` or `0`…`59`, `>end` | label text *(pending the Excel check)* |
| range (no `groupBy`) | `<start`, one item per `groupInterval` from `startNum`, `>end` | `lo-hi` with `hi = lo + interval - 1` when the data and interval are whole numbers, else `lo + interval` *(pending the Excel check)* |
| discrete (`discretePr`) | the stored group items | stored members follow their item by value; an item that is new gets a group of its own named after it |

`<start` and `>end` use `M/d/yyyy` (the en-US short date) *(pending the Excel check for other locales)*. With `autoStart`/`autoEnd` (the default) the
`startDate`/`endDate` or `startNum`/`endNum` are set to the smallest and largest value in the data. A record whose base value is blank has no group
and is left out of the table. A `groupBy` the library does not know drops the group with `pivot.group-unsupported`.

### The table

Each table is read with its field indexes mapped to the new cache, then laid out.

- **Field items.** An axis field's items (`pivotField/items`) keep their stored order, their flags (`h`, `s`, `n`, …) and the trailing subtotal items
  (`t="default"`, added when `defaultSubtotal` is on and there are none); items whose value is gone are dropped. New items are placed by `sortType`:
  `ascending` and `descending` sort the whole list afresh (numbers and dates, then text without regard to case, then booleans, errors, blanks; grouped
  fields by group order); `manual` appends the new items in ascending order. The item `h="1"` hides an item, and its records are left out of every
  total.
- **Axes.** Only combinations with records are listed. A table with several data fields lists them as the field `-2` on the column axis (added when the
  stored definition has none; `-2` anywhere else is kept where it is).
- **Aggregates.** Sum, Count (non-empty values), Average, Max, Min, Product, CountNums, StdDev, StdDevP, Var and VarP over the numbers of the
  group; text, booleans and errors are ignored by all but Count. Average, StdDev and Var of nothing (and StdDev/Var of one value) give `#DIV/0!`. A
  group that has no records at all is an empty cell, or `missingCaption` when `showMissing` is on and one is set.
- **Show values as.** Normal, percent of grand total, of row and of column, of parent row and parent column, index, and difference, percent,
  percent difference and running total along a base field (`baseItem` an item position, `1048828` the previous and `1048829` the next item). The
  base-field forms apply at the base field's level; elsewhere the cell is empty. Percent of parent, percent running total and the rank forms show the
  normal value with `pivot.show-data-as-unsupported`. Percent forms get `0.00%` unless the data field names a number format.
- **Calculated fields.** The formula is read by the calc engine with each field name (also in single quotes) standing for the sum of that field over
  the cell's group; the result is the formula applied to those sums, never the sum of per-record results (Excel's rule for calculated fields).
  An unreadable formula shows `#VALUE!` with `pivot.calculated-field-unparsable`.
- **Report filters.** The selected item (`pageField item`) follows its value through the refresh; with none the field shows `(All)`, or `(Multiple Items)`
  when the field allows several and hides some. The records are filtered accordingly.
- **Not applied.** Label, value and date filters (`filters`) are kept as stored and reported with `pivot.filters-not-applied`; custom subtotal
  functions show the automatic subtotal (`pivot.custom-subtotals-unsupported`).

### Layout

The table keeps its top-left cell; the page fields go in the rows above it, one per row with a blank row between them and the table (the table moves
down when there is no room). The old cells (location and page fields) are cleared, then the new ones written. Numbers are written as numbers (item
labels that are numbers or dates too, dates with `m/d/yyyy`), text as text; compact-form children are indented with the cell alignment, one level per
nesting level times `indent`. Fills, fonts and borders come from the table style, as in Excel.

- **Compact** (`compact` true on the field, the default): consecutive compact row fields share the first column, captioned `Row Labels`
  (`rowHeaderCaption`); each item has its own row, its subtotal in that row when `subtotalTop` is on (default), else on a `X Total` row after its
  children.
- **Outline** (`compact="0"`): one column per field, captioned with the field name; items and their subtotals as in compact.
- **Tabular** (`compact="0" outline="0"`): one column per field, a row per innermost combination with the outer labels on the first row of their
  group, subtotal rows `X Total` after the group.
- **Columns.** Captioned `Column Labels` (the field name when the first column field is not compact) in the first header row; one header row per
  column field, each label over the first column of its group; subtotals `X Total` after a group; the `Grand Total` column after all. With
  several data fields the data field names are the innermost header row, and totals are `Total <data field name>`.
- **Grand totals.** `colGrandTotals` (default on) is the Grand Total row; `rowGrandTotals` the Grand Total column *(pending the Excel check for which
  attribute is which)*. Subtotals appear for fields with `defaultSubtotal` (default on) that have a field inside them.
- **Location** (`firstHeaderRow`, `firstDataRow`, `firstDataCol`): `firstDataCol` is the number of label columns; `firstDataRow` is the column
  field count (the data-field row included) plus one; `firstHeaderRow` is 1 when there are column fields, else 0 for several data fields and 1 for
  one *(pending the Excel check)*.
- **`rowItems` and `colItems`** list the rows and columns: `x v` is the item's position in its field's item list (the data field's index for the
  data row/column, which also sets `i`), `r` repeats that many leading entries of the previous line, `t="default"` marks a subtotal and `t="grand"` a
  grand total.
- A data field placed before the innermost field, and other unusual positions of `-2`, are laid out with the data field names as extra levels; the
  result is not compared with Excel.

## GETPIVOTDATA

`GETPIVOTDATA(data_field, pivot_table, [field1, item1], …)` reads what the table shows, like Excel: the stored `rowItems`/`colItems` give each
row and column its path, and the answer is the cell at that row and column. So it answers from the last refresh; after the source changes it
agrees with the cells until the next `Refresh`, then at the next recalculation.

- `data_field` is a data field's caption (`Sum of Sales`) or its source field's name (`Sales`).
- `pivot_table` is any cell of the table's body; a cell outside every table, or on a sheet without one, gives `#REF!`.
- The field/item pairs may come in any order; the row fields named must be the first fields of the row axis (and likewise the columns), or the result
  is `#REF!`. No pair for an axis gives its grand total. Item matching ignores case; a number matches a numeric item.
- A report-filter field answers only for the item it currently shows.
- An unknown field or item, a combination the table does not show, an empty cell and an odd number of pair arguments give `#REF!`.

The function is `ICalcResolver.GetPivotData` in core; a resolver with no pivot tables returns `#REF!`.

## Not supported

Pivot charts, slicers, timelines, the Data Model, OLAP and external sources are kept as stored. The refresh of calculated items, label and value
filters, show-values-as parent/rank forms, custom subtotals and the hierarchy-based (OLAP) fields is reported, not reproduced.
