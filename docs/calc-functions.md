# Calc: function families

The functions registered by `FunctionRegistry.CreateDefault()` for these four families, written from Microsoft's per-function reference pages.
Columns: **status** is `docs` when the behaviour follows the reference page and its examples (covered by `FunctionFamilyTests`), `pending`
when the reference leaves an edge open and the choice made here waits for the Excel grid (`testdata/calc/function-grid`, answers not yet recorded); the lookup, array and LAMBDA families have their own grid in `testdata/calc/lookup-grid`.
Function arguments that are `Value` lift over arrays and propagate errors before the call (see `calc.md`); the aggregate functions take references and arrays
unchanged. Numbers go through the core rules: 15-significant-digit comparison, `Evaluator.AddSnapped` for sums, `CalcConvert` for text/number coercion.

## Shared helpers

* **Criteria** (`CalcCriteria`, public): the matching rule of the `*IF(S)` family. A criterion is a number, boolean, error, or text with an optional operator
  (`=`, `<>`, `<`, `<=`, `>`, `>=`). Equality on text understands `?`, `*` and `~` escapes and ignores case; an operand that reads as a number compares numerically
  against number cells (and, for equality, text cells that read as the same number); `""` matches blank and empty-text cells, `"="` only blank cells, `"<>"` every non-blank cell.
  A blank criterion cell counts as 0 *(pending)*.
* **Resolver hooks** (all additive, defaults say "not available"): `IsFormula`, `GetFormulaText`, `GetRowVisibility`, `IsSubtotalCell`, `GetCellInfo`, `GetSheetIndex`,
  `GetSheetCount`, `GetFileName`, `GetInfo`.
* **Random numbers** come from `EvaluatorOptions.Random` (a seeded `Random` gives repeatable results); `OfficeLimits.RegexMatchTimeoutMilliseconds` bounds regular-expression matches.

## Math and trigonometry

| Functions | Status | Notes |
|---|---|---|
| `ABS SIGN SQRT SQRTPI EXP LN LOG LOG10 POWER PI DEGREES RADIANS` | docs | `POWER` shares the `^` operator rules (`0^0` is `#NUM!`). `LOG` base 1 is `#DIV/0!`. |
| `ROUND ROUNDUP ROUNDDOWN TRUNC INT` | docs | Rounding works on the 15-digit decimal value, so `ROUND(2.675,2)` is 2.68. |
| `MROUND CEILING FLOOR CEILING.MATH FLOOR.MATH CEILING.PRECISE FLOOR.PRECISE ISO.CEILING EVEN ODD` | docs | The quotient is rounded to 15 digits before the step is chosen, so `CEILING(0.3,0.1)` is 0.3. `FLOOR` with a negative number and positive significance is *(pending)*. |
| `MOD QUOTIENT` | docs | `MOD` keeps the divisor's sign. Very large quotients (Excel's old 2^27 limit) *(pending)*. |
| `SIN COS TAN ASIN ACOS ATAN ATAN2 SINH COSH TANH ASINH ACOSH ATANH COT COTH CSC CSCH SEC SECH ACOT ACOTH` | docs | `ACOT` is in (0, pi). Huge arguments (|x| >= 2^27) are computed, not refused *(pending)*. |
| `FACT FACTDOUBLE COMBIN COMBINA PERMUT PERMUTATIONA MULTINOMIAL GCD LCM` | docs | Arguments are truncated. `GCD`/`LCM` refuse values from 2^53. `PERMUT`/`PERMUTATIONA` are statistical in Microsoft's list and registered here because they share the factorial table. |
| `ROMAN ARABIC BASE DECIMAL` | docs | `ROMAN` forms 1-4 use the tables in `MathNumbers.cs` checked only against the reference's `499` example *(pending: the full 1-3999 range per form)*. `ARABIC` accepts non-canonical numerals *(pending)*. |
| `SUM PRODUCT SUMSQ SUMPRODUCT SUMX2MY2 SUMX2PY2 SUMXMY2 SERIESSUM` | docs | `SUM` is the seed implementation. Direct arguments are coerced; cells in references and arrays contribute only numbers. |
| `SUMIF SUMIFS` | docs | Ranges must be references (`#VALUE!` otherwise). `SUMIF` re-anchors a differently sized sum range at its top-left cell; `SUMIFS` needs equal sizes. A criterion array returns an array. |
| `SUBTOTAL AGGREGATE` | docs | Functions 1-11 skip filtered rows, 101-111 also hand-hidden rows; cells reported by `IsSubtotalCell` are always skipped. `AGGREGATE` options 0-7 and functions 1-19 as documented. Non-reference arguments to `SUBTOTAL` are treated as values *(pending)*. |
| `MMULT MDETERM MINVERSE MUNIT` | docs | LU decomposition; a pivot that is zero at the matrix scale is singular (`#NUM!`). Results near zero may differ from Excel in the last bits *(pending)*. |
| `RAND RANDBETWEEN` | docs | Volatile; drawn from the evaluator's seeded sequence (`EvaluatorOptions.RandomSeed`, default 0), like `RANDARRAY`. |
| `SEQUENCE` | seed | |

## Logical

| Functions | Status | Notes |
|---|---|---|
| `AND OR NOT IF IFS IFERROR IFNA` | seed | |
| `XOR TRUE FALSE` | docs | `XOR` skips text and blanks in references/arrays; no logical value at all is `#VALUE!`. |
| `SWITCH` | docs | Lazy; compares like `=`; an array expression switches element by element. |

## Information

| Functions | Status | Notes |
|---|---|---|
| `ISBLANK ISNUMBER ISTEXT ISNONTEXT ISLOGICAL ISERR ISERROR ISNA ISREF` | docs | |
| `ISEVEN ISODD` | docs | Truncate; booleans and non-numeric text are `#VALUE!` *(pending for booleans)*. |
| `N T NA TYPE ERROR.TYPE` | docs | `TYPE` of a multi-cell reference is 64. `ERROR.TYPE` numbers for the errors after `#GETTING_DATA` follow the reference page; `#BUSY!` has no listed number and gives 15 *(pending)*. |
| `ISFORMULA FORMULATEXT SHEET SHEETS CELL INFO` | docs | Through the resolver hooks. `CELL("format")` maps the format code to the short codes of the reference table. `INFO` defaults are Excel for Windows values. `CELL` without a reference uses the formula's own cell *(pending: Excel uses the last changed cell)*. |

## Text

| Functions | Status | Notes |
|---|---|---|
| `LEFT RIGHT MID LEN REPLACE FIND SEARCH` and the `*B` forms | docs | The byte forms count one byte per character, as Excel does unless a double-byte language is the editing language *(pending: DBCS behaviour)*. `SEARCH` supports `? * ~`. Lengths are UTF-16 code units. |
| `SUBSTITUTE REPT TRIM CLEAN UPPER LOWER PROPER EXACT` | docs | `TRIM` removes only U+0020. `CLEAN` removes code units 0-31. |
| `CHAR CODE UNICHAR UNICODE` | docs | `CHAR`/`CODE` use Windows code page 1252; characters outside it give 63 from `CODE`. |
| `CONCAT CONCATENATE TEXTJOIN ARRAYTOTEXT VALUETOTEXT` | docs | `CONCATENATE` lifts over arrays; results above 32,767 characters are `#VALUE!`. |
| `TEXTBEFORE TEXTAFTER TEXTSPLIT` | docs | Delimiter arrays, negative instances, `match_end`, `if_not_found`, padding. Overlapping delimiters are not re-scanned from the right *(pending)*. |
| `VALUE NUMBERVALUE FIXED DOLLAR TEXT` | docs | `VALUE("")` is `#VALUE!` *(pending)*. `DOLLAR` always uses `$`. |
| `REGEXTEST REGEXEXTRACT REGEXREPLACE` | docs | PCRE2 patterns run on `System.Text.RegularExpressions` after translation: `\d \w \s \h \R \Q..\E \x{..}`, POSIX classes and `(?P<n>)` are rewritten to ASCII semantics. Possessive quantifiers and other constructs .NET cannot parse give `#VALUE!`, as does a match over `OfficeLimits.RegexMatchTimeoutMilliseconds`. `REGEXEXTRACT` mode 1 returns a column and mode 2 a row *(pending)*. |
| `ASC DBCS JIS PHONETIC BAHTTEXT` | docs | `ASC`/`DBCS` convert whatever the text holds (Excel does so only with a double-byte editing language *(pending)*). `PHONETIC` returns the cell text. |
| `TRANSLATE DETECTLANGUAGE` | docs | Service functions: `#CONNECT!` offline *(pending: Excel's offline value)*. |

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

## Open: lookup, arrays and LAMBDA helpers, pending the 1.0 Excel grid

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
