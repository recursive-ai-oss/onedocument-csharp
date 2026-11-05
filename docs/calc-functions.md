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

## Financial, engineering and database

Behaviour of the three families registered by `FunctionRegistry.CreateDefault()`. Rules marked *(open)* are first readings that the committed grids (`testdata/calc/function-grids/`) settle against Excel at the 1.0 gate.

Behaviour of the three families registered by `FunctionRegistry.CreateDefault()`. Excel is the specification; everything below was built from Microsoft's published function reference. Rules marked *(open)* are first readings that the committed grids (`testdata/calc/function-grids/`) will settle against Excel.

### Conventions shared by all three

* Arguments are `Value` arguments unless said otherwise: a block or array makes the call lift and return an array, an error argument is returned before the call, text is read as a number where it looks like one (`"3"`), `TRUE` is 1.
* An omitted argument takes the function's default; a required argument that is left empty reads as 0.
* A result that is not finite is `#NUM!`. Negative zero is written as 0.
* Dates are serials truncated to whole days. A serial outside the calendar of the workbook's date system is `#NUM!`; text that is not a date is `#VALUE!`.

### Financial

**Day-count bases** (`basis` 0 to 4, default 0; anything else `#NUM!`): 0 US (NASD) 30/360 with the last-day-of-February rules, 1 actual/actual, 2 actual/360, 3 actual/365, 4 European 30/360. On basis 1 a span inside one year uses 366 when it contains 29 February, and a longer span the average year length of the years it touches (`YEARFRAC`'s rule). The helper is `DayCount` (shared by every function that counts days).

**Coupon dates** are counted back from maturity in steps of 12/`frequency` months (frequency 1, 2 or 4, otherwise `#NUM!`). A maturity on the last day of a month gives month-end coupon dates; otherwise the maturity's day of month is kept, clamped to the month's length. `settlement >= maturity` is `#NUM!`. `COUPDAYS` is the actual period on basis 1, 365/frequency on basis 3, otherwise 360/frequency; `COUPDAYBS` is the days from the previous coupon to settlement on the basis's count; `COUPDAYSNC` is the days from settlement to the next coupon (on bases 0 and 4 it is `COUPDAYS` minus `COUPDAYBS`). *(open: bases 0 and 4 for `COUPDAYSNC`)*

**Time value.** `FV PV PMT NPER RATE IPMT PPMT ISPMT CUMIPMT CUMPRINC EFFECT NOMINAL RRI PDURATION FVSCHEDULE` follow the annuity equation `pv(1+r)^n + pmt(1+r·type)((1+r)^n−1)/r + fv = 0`; any non-zero `type` is 1 except in `CUMIPMT`/`CUMPRINC`, where it must be 0 or 1. `IPMT` with payments at the start of the period is 0 in period 1 and the end-of-period figure of the previous period, discounted one period, after that. `CUMIPMT`/`CUMPRINC` truncate `nper`, `start` and `end`, and require rate, nper and pv positive and `1 ≤ start ≤ end ≤ nper`.

**Iteration.** `RATE`, `IRR` and `XIRR` use Newton's method from the guess (default 0.1). Excel documents 20 steps to 0.0000001 (100 for `XIRR`); plain Newton from 0.1 does not converge for long annuities that Excel solves (`RATE(360,-1000,150000)`), so ours halves a step until it keeps the rate above −1 and does not increase the residual, runs up to 100 steps and stops below 1e-12. `#NUM!` when it does not converge, when `IRR`/`XIRR` have no cash flow of each sign, or the guess is −1 or less. `IRR`/`MIRR` read only the numbers of a reference or array (text, booleans and blanks are skipped); `XNPV`/`XIRR`/`FVSCHEDULE` require every cell to be a number (a blank is 0, text is `#VALUE!`). *(open: iteration limit; `XIRR` of a zero result is a rounding-sized number here, Excel returns about 2.98E-9)*

**Net present value.** `NPV` takes the numbers of references and arrays, and coerces direct arguments; `rate = -1` is `#DIV/0!`. `XNPV` uses a 365-day year from the first date; dates before the first are `#NUM!`; counts that differ are `#NUM!`. `MIRR` needs at least two values with both signs (`#DIV/0!` otherwise).

**Depreciation.** `SLN SYD DB DDB VDB AMORLINC AMORDEGRC`. `DB` rounds the rate to three decimals and pro-rates the first and the (life+1)th period by `month`. `DDB` is declining balance that never goes below salvage; a fractional period interpolates linearly between whole periods. *(open)* `VDB` switches to straight-line over the remaining life when that is larger (unless `no_switch`), weights partial periods by their overlap, and limits `end` to the life. `AMORLINC`/`AMORDEGRC` implement the French system: a pro-rated first period, `AMORDEGRC` with the coefficient 1, 1.5, 2 or 2.5 by life (under 3, under 5, up to 6, over 6 years) and every charge rounded to a whole unit; basis 2 is `#NUM!`. *(open: the exhausted-asset tail of `AMORDEGRC`; `DB` with cost 0, which returns 0; fractional `life` and `period`)*

**Securities.** `PRICE YIELD DURATION MDURATION` use the documented formulas with `A`, `E`, `DSC` from the coupon schedule; one coupon left uses the short formula. `YIELD` solves `PRICE` for the yield by bisection (the price falls monotonically as the yield rises, so the root is unique). `DISC INTRATE RECEIVED PRICEDISC YIELDDISC PRICEMAT YIELDMAT ACCRINTM` use the year fraction of the basis. `ACCRINT` sums the share of each quasi-coupon period (counted from `first_interest` in both directions) that lies between issue, or `first_interest` when `calc_method` is FALSE, and settlement. `ODDFPRICE`/`ODDLPRICE` use the quasi-coupon fractions for the odd period, so a long odd period works; `ODDFYIELD` solves by bisection and `ODDLYIELD` has a closed form. `TBILLPRICE/YIELD/EQ` need a bill of at most 365 days; `TBILLEQ` over half a year solves the quadratic with 366 days when the bill spans 29 February. *(open)* `DOLLARDE`/`DOLLARFR` read the decimals as a count of `fraction`ths with as many digits as `fraction` has.

### Engineering

* **Base conversion** `BIN2DEC … OCT2HEX`: input text or number of at most 10 digits (`#NUM!` when longer or not in the base), 10 digits with the top bit set are negative (two's complement of 10, 30 or 40 bits); results are upper case; a negative result is always 10 digits and ignores `places`; `places` from 1 to 10 left-pads with zeros and is `#NUM!` when too small. A boolean is `#VALUE!`; an empty cell is 0; the empty string is `#NUM!` *(open)*. `DEC2x` truncates and accepts −512…511 (binary), −2^29…2^29−1 (octal), −2^39…2^39−1 (hex).
* **Bits.** `BITAND BITOR BITXOR BITLSHIFT BITRSHIFT`: whole numbers from 0 to 2^48−1 (otherwise `#NUM!`); shifts of at most 53 places; a negative shift reverses direction; a left shift past 2^48 is `#NUM!`.
* `DELTA`, `GESTEP`: numbers; omitted second argument 0.
* **`ERF`/`ERFC`** (and `.PRECISE`): the series 2/√π·e^(−x²)·Σ 2ⁿx^(2n+1)/(2n+1)!! for |x| < 2.5, a continued fraction for the complement beyond; accurate to about 1e-15 relative. `ERF(lower, upper)` is `erf(upper) − erf(lower)`; negative limits are accepted.
* **Bessel.** Order truncated; negative order `#NUM!`; `K` and `Y` need x > 0; |x| up to 1e6. `I` by its power series; `J` by the series where it does not cancel, otherwise the trapezoid rule on the periodic integral; `K` by the trapezoid rule on `∫e^(−x cosh t) cosh(nt)dt`; `Y` by Gauss–Legendre on its integral representation. Values are the exact ones to about 1e-15 (absolute near zeros). *(open: Excel's own Bessel values are less accurate: the reference prints `BESSELJ(1.9,2)` as 0.329925829 while the exact value is 0.3299257277.)*
* **`CONVERT`**: the unit table of the Excel reference (mass, distance, time, pressure, force, energy, power, magnetism, temperature, volume, area, information, speed), case-sensitive; metric prefixes `Y Z E P T G M k h da/e d c m u n p f a z y` on the units marked metric, `ki Mi Gi Ti Pi Ei Zi Yi` on `bit` and `byte`; prefixes on area and volume units are raised to the unit's power (`km2` is (1000 m)²). Unknown units or units of different kinds are `#N/A`. Temperature converts through kelvins. *(open: the constants of a few rarely used units; `Schooner` and `Humpen`.)*
* **Complex numbers.** Text `a+bi`, `a-bj`, `bi`, `i`, `-j`, or a plain number; no spaces; suffix `i` or `j` (lower case); a different suffix in two arguments is `#VALUE!`; text that is not a complex number is `#NUM!`. Results are written with 15 significant digits, a coefficient of 1 omitted (`3+i`, `-i`), and the suffix of the arguments (`i` by default). The trigonometric functions use the standard identities, `IMSQRT`/`IMPOWER` De Moivre's formula (so tiny rounding terms appear as in Excel), `IMLN(0)`, division by zero and the reciprocal functions at a pole are `#NUM!`, `IMARGUMENT(0)` is `#DIV/0!`. `IMSUM` and `IMPRODUCT` take ranges; blank cells in a range are skipped.

### Database

`DSUM DAVERAGE DCOUNT DCOUNTA DGET DMAX DMIN DPRODUCT DSTDEV DSTDEVP DVAR DVARP (database, field, criteria)`.

* **Database** and **criteria** are references or arrays; the first row holds the field names. (Excel accepts references only; accepting arrays lets a template evaluate the functions over data it holds as arrays. *(open)*)
* **Field** is a 1-based column number or a header text compared ignoring case; anything else is `#VALUE!`.
* **Criteria** header cells name database fields. Cells in one row must all match; the rows are alternatives. An empty cell places no condition and a row of empty cells matches every record; a criteria block with no data rows matches every record. A header that is not a database field makes its cells *computed* criteria: the record matches when the cell holds TRUE.
* **A criterion** is matched as `COUNTIF` does (operators `= <> < <= > >=`, wildcards `? * ~`), except that text without an operator matches the cells that *begin* with it (`Dav` finds `Davolio`); `=Dav` is exact.
* **Results.** `DSUM` adds the numbers of the field in the matching records (text and blanks are ignored; an error cell in a matching record is the result); `DAVERAGE` of no number is `#DIV/0!`; `DCOUNT` counts numbers, `DCOUNTA` non-blank cells; `DMAX`, `DMIN`, `DPRODUCT` of no number are 0; `DGET` returns the one match (`#VALUE!` for none, `#NUM!` for several); the sample deviation and variance of fewer than two numbers, and the population ones of none, are `#DIV/0!`.

### Grids

`testdata/calc/function-grids/*.fgrid` hold typical, boundary, coercion, error and array inputs for every function (generated by `tools/calc-grids/gen_function_grids.py`). They carry no answers yet; Excel answers them once and the answers become the expected values. The database grid reads the block described by `db-data.tsv`, which the runner fills first.

### Open: financial, engineering and database, pending the 1.0 Excel grid

1. `COUPDAYSNC` on bases 0 and 4 (taken as `COUPDAYS` minus `COUPDAYBS`).
2. `XIRR` with a zero result returns a rounding-sized number here; Excel returns about 2.98E-9. The iteration limit of `RATE`, `IRR` and `XIRR`.
3. `DB` with cost 0; fractional `life` and `period` in `DB`, `DDB` and `VDB`; the exhausted-asset tail and coefficient boundaries of `AMORDEGRC`; basis 2 in `AMORLINC`/`AMORDEGRC` (`#NUM!` here).
4. `TBILLEQ` over half a year (366 days when the bill spans 29 February).
5. Base conversion of the empty string (`#NUM!` here) and of a blank cell (0).
6. A few `CONVERT` constants (`Schooner`, `Humpen`, `Pica`/`pica`, `parsec`) and which units take metric prefixes.
7. `IMSUM`/`IMPRODUCT` skipping blank cells in ranges; `IRR(TRUE)` is `#NUM!`.
8. The database functions accept arrays as well as references (Excel takes references only).
