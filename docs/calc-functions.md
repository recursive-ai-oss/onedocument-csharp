# Calc: function families

What each family does, written from Microsoft's function reference pages. Where the pages leave a behaviour open, the code takes the reading below and the Excel grid in `testdata/calc/lookup-grid` settles it at the full Excel run; every such item is listed under **Open**.

## Lookup and reference

`MATCH`, `XMATCH`, `XLOOKUP`, `LOOKUP`, `VLOOKUP`, `HLOOKUP`, `INDEX`, `CHOOSE`, `CHOOSECOLS`, `CHOOSEROWS`, `OFFSET`, `INDIRECT`, `ROW`, `COLUMN`, `ROWS`, `COLUMNS`, `AREAS`, `ADDRESS`, `TRANSPOSE`, `HYPERLINK`, `GETPIVOTDATA`, `TRIMRANGE`.

* **Matching.** The type must agree: the number 1 never matches the text "1", and a blank cell matches nothing. Text ignores case; wildcards (`*`, `?`, `~` escape) apply to `MATCH` type 0, `XMATCH`/`XLOOKUP` match mode 2 and exact `VLOOKUP`/`HLOOKUP`. A looked-up blank counts as 0.
* **Approximate search** is a binary search that assumes sorted data and, on unsorted data, answers whatever the probing finds. Cells of another type than the target are stepped over towards the low end.
* **`XLOOKUP`** returns a reference when `return_array` is one (so `XLOOKUP(...):XLOOKUP(...)` is a range). A `lookup_array` that is not one row or one column is `#VALUE!`; a `return_array` of the wrong length is `#VALUE!`.
* **`OFFSET`** needs a reference (`#VALUE!` otherwise), a zero height or width is `#REF!`, a negative one extends up or left, a result off the sheet is `#REF!`. Volatile. **`INDIRECT`** reads A1 or R1C1 text through the formula parser and accepts only a cell, a range, a defined name or a structured reference; anything else, and an external workbook, is `#REF!`. Volatile.
* **`GETPIVOTDATA`** is `#REF!` until pivot tables are modelled. **`HYPERLINK`** returns `friendly_name`, or the link text; opening the link is the host's.
* **`TRIMRANGE`** removes empty edge rows and columns (modes 0 none, 1 leading, 2 trailing, 3 both). The trim-ref operators (`A1:.E8`) are not parsed yet.

## Dynamic arrays

`FILTER`, `SORT`, `SORTBY`, `UNIQUE`, `TAKE`, `DROP`, `EXPAND`, `HSTACK`, `VSTACK`, `TOCOL`, `TOROW`, `WRAPROWS`, `WRAPCOLS`, `RANDARRAY`, `SEQUENCE`.

* **Sort order**: numbers, then text (ignoring case, `TextCollation`), then FALSE before TRUE, then errors; descending reverses that; empty cells come last either way. The sort is stable. Several keys are given as arrays for `sort_index` and `sort_order`.
* An empty result is `#CALC!`. `HSTACK`/`VSTACK`/`EXPAND` pad with `#N/A`; `WRAPROWS`/`WRAPCOLS` likewise.
* `RANDARRAY` draws from the evaluator's seeded sequence (`EvaluatorOptions.RandomSeed`): the same seed gives the same numbers.
* Results are arrays; putting them on the grid and `#SPILL!` belong to recalculation.

## LAMBDA helpers

`MAP`, `REDUCE`, `SCAN`, `BYROW`, `BYCOL`, `MAKEARRAY`, `ISOMITTED`, `GROUPBY`, `PIVOTBY`, `PERCENTOF`.

* The helpers take a function written as a LAMBDA or as a bare function name (`MAP(a, LEN)`, `REDUCE(0, a, SUM)`): a name that is not a defined name or a LET name becomes a function value. Only functions that evaluate their arguments before the call can be used this way (not `IF`, `LET`).
* A lambda result that is an array of more than one cell makes `MAP`, `SCAN`, `BYROW`, `BYCOL` and `MAKEARRAY` `#CALC!` as a whole (measured on Excel 16.0 for `MAP` and `MAKEARRAY`); `REDUCE` may carry an array accumulator.
* `GROUPBY` supports one aggregation function, a `field_headers` mode 0-3, total depth, sort order and a filter array; `PIVOTBY` supports one value column and grand totals.

## Open: pending the 1.0 Excel grid

The function pages do not say, and the code takes the reading in brackets:

1. How `MATCH` / `XMATCH` / `VLOOKUP` / `LOOKUP` binary search behaves on unsorted data and on arrays mixing types (step over other-type cells towards the low end; duplicates: the last equal element for approximate search).
2. `MATCH(x, a, )` and `VLOOKUP(x, t, i, )` with an empty argument between commas (0 / FALSE).
3. Which duplicate a binary `XMATCH`/`XLOOKUP` exact search returns (the last).
4. `XLOOKUP` with an array `lookup_value` whose matches are rows or columns (each match is reduced to its first cell by the lifting rule).
5. `ROW`/`COLUMN` of an array or constant (`#VALUE!`), and of a union (first area).
6. `ROWS`/`COLUMNS` of a union.
7. Errors, blank cells and mixed types in `SORT` (errors after text and logicals, blanks last in both directions); `SORT` with fewer orders than keys (the last order repeats).
8. `UNIQUE` equality for numbers that differ in the 16th digit, and blank against 0 (distinct).
9. `TAKE`/`DROP` with neither count given (`#VALUE!`); `FILTER` when `include` holds text (`#VALUE!`) or non-integers (non-zero is true).
10. `RANDARRAY` with integer bounds that are not whole numbers (`#VALUE!`).
11. The optional `[y]` parameter syntax of `LAMBDA` and how `ISOMITTED` sees a skipped argument; the parser does not read `[y]` yet.
12. `GROUPBY`: "automatic" `field_headers`, labels of subtotal rows, layout of several row fields (repeated outer keys), `field_relationship`, `HSTACK` of several functions; `PIVOTBY`: several value columns, total depths beyond 1, `relative_to`, the text of cells with no data (empty text).
13. `INDIRECT` with leading or trailing spaces, `$` signs, unions and intersections; `OFFSET` with array offsets.
14. `TRIMRANGE` of a range with nothing in it (`#REF!`) and the trim-ref operators.
15. `ADDRESS` quoting of sheet names that look like cell references or numbers.
