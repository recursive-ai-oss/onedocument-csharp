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
* **`GETPIVOTDATA`** reads what the pivot table shows (see `spreadsheets-pivots.md`): the answer is the table as last refreshed, `#REF!` when the sheet has no pivot table at the cell, the data field or an item is unknown, or the combination is not shown. Through `ICalcResolver.GetPivotData`; without a workbook it is `#REF!`. **`HYPERLINK`** returns `friendly_name`, or the link text; opening the link is the host's.
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

1. *(Settled against the 1.0 Excel grid, 99.5 % of the lookup grid agrees; what is left is not a search.)* `XMATCH` binary modes bisect across types in the order numbers, text, logical values, errors (an equal probe is kept and the search goes on towards the start when ascending, the end when descending); `XMATCH` next-smaller/larger scans compare across types and an empty needle is the empty text; legacy approximate `MATCH`/`VLOOKUP`/`LOOKUP` probes step up to a cell of the target's type and stop on an equal cell, taking the last of the equal run when ascending; `MATCH` type -1 over a short array is a linear read. Open: how Excel handles `MATCH` type -1 beyond short arrays (the code bisects), and where the question mark sorts among the other symbols (it sorts after the asterisk).
2. `MATCH(x, a, )` and `VLOOKUP(x, t, i, )` with an empty argument between commas (0 / FALSE).
3. *(Settled.)* A binary `XMATCH`/`XLOOKUP` exact search returns the first of equal cells when ascending and the last when descending.
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
## Statistical

Source: Microsoft's per-function reference pages ("Statistical functions (reference)" and each function's page). Numerics are in `Functions/Statistical/SpecialMath.cs`: log-gamma
(Lanczos below 15, Stirling above), the error function (a series below 2.5, a continued fraction above), the regularized incomplete gamma and beta functions (series and
continued fractions, each returning the tail that does not cancel), the normal quantile (a rational start refined by Halley steps) and a bisection solver for the other
quantiles. `Exp`, `Ln`, `Pow`, `Log1p`, `Expm1` and `Tanh` come from OneMath so results are the same on every platform.

**Reading the arguments.** `Stat.Gather`: in a reference or array only numbers count (text, logicals and blanks are skipped); a value typed in the argument list is converted
(`"3"` and `TRUE` count, `"x"` is `#VALUE!`); an error anywhere is returned. The "A" forms (`AVERAGEA`, `MAXA`, `MINA`, `VARA`, `VARPA`, `STDEVA`, `STDEVPA`) count text in
cells as 0 and `TRUE` as 1. `COUNT` never returns an error. Paired functions (`CORREL`, `SLOPE`, `COVARIANCE.*`, `FORECAST`, ...) drop a pair when either cell is not a number,
return an error found in either, and give `#N/A` for arrays of different shapes.

| Functions | Status | Notes |
|---|---|---|
| `COUNT COUNTA COUNTBLANK COUNTIF COUNTIFS` | docs | `COUNTIF(S)` use `CalcCriteria`. A criterion given as an array runs once per element and returns an array. Ranges of different shapes in `COUNTIFS` are `#VALUE!`. |
| `AVERAGE AVERAGEA AVERAGEIF AVERAGEIFS MAX MAXA MIN MINA MAXIFS MINIFS` | docs | Sums use `Evaluator.AddSnapped`. No numbers: `AVERAGE` is `#DIV/0!`, `MAX`/`MIN`/`MAXIFS`/`MINIFS` are 0. `AVERAGEIF`'s `average_range` is resized to the shape of `range` from its own top-left cell. A matching cell holding an error returns the error. |
| `MEDIAN MODE MODE.SNGL MODE.MULT LARGE SMALL RANK RANK.EQ RANK.AVG` | docs | `MODE.MULT` returns a column in order of first appearance. `LARGE`/`SMALL` round a fractional `k` up *(pending)*. `RANK` ignores non-numbers in `ref`; a number not in `ref` is `#N/A`. |
| `PERCENTILE(.INC/.EXC) QUARTILE(.INC/.EXC) PERCENTRANK(.INC/.EXC)` | docs | Interpolation as the reference defines (`k(n-1)` inclusive, `k(n+1)` exclusive). `PERCENTRANK` truncates (does not round) to `significance` digits after rounding to 15 digits; one value in `.INC` is `#N/A` *(pending)*. |
| `VAR VAR.S VAR.P VARP VARA VARPA STDEV STDEV.S STDEV.P STDEVP STDEVA STDEVPA DEVSQ AVEDEV GEOMEAN HARMEAN TRIMMEAN SKEW SKEW.P KURT STANDARDIZE` | docs | Two-pass sums of squares. `STDEV.S` of one value is `#DIV/0!`; `VAR.P` of one value is 0. `GEOMEAN`/`HARMEAN` of a value ≤ 0 are `#NUM!`. `TRIMMEAN` removes `floor(n*p/2)` from each end. |
| `FREQUENCY PROB` | docs | `FREQUENCY` counts values in (previous bin, bin] and returns `bins+1` rows; unsorted bins are counted in ascending order and the counts put back in the order given *(pending)*. `PROB` needs every probability in (0,1] and a total of 1 within 1e-9. |
| `CORREL PEARSON COVAR COVARIANCE.P COVARIANCE.S RSQ SLOPE INTERCEPT STEYX FORECAST FORECAST.LINEAR` | docs | See above for pairing. Zero variance or fewer than two pairs: `#DIV/0!` (`STEYX` needs three). |
| `LINEST LOGEST TREND GROWTH` | docs | Householder QR. A predictor that is a combination of earlier ones is dropped: its coefficient and standard error are 0, as `LINEST` reports. `known_x` omitted is 1..n. `const` FALSE forces the intercept to 0 and uses the uncentred total sum of squares. With zero residual degrees of freedom the statistics rows give `#NUM!` for standard errors, `sey` and `F` *(pending)*. |
| `FORECAST.ETS FORECAST.ETS.CONFINT FORECAST.ETS.SEASONALITY FORECAST.ETS.STAT` | gap | Microsoft's pages name the "AAA" exponential triple smoothing but do not give the algorithm (initial states, parameter fit, seasonality detection), so all four return `#N/A`. Listed as a gap; not guessed. |
| `NORM.DIST NORM.S.DIST NORM.INV NORM.S.INV LOGNORM.DIST LOGNORM.INV PHI GAUSS FISHER FISHERINV` and legacy `NORMDIST NORMSDIST NORMINV NORMSINV LOGNORMDIST LOGINV` | docs | Legacy names share the implementation (`LOGNORMDIST`, `NORMSDIST` are cumulative only). |
| `T.DIST T.DIST.2T T.DIST.RT T.INV T.INV.2T CHISQ.DIST CHISQ.DIST.RT CHISQ.INV CHISQ.INV.RT F.DIST F.DIST.RT F.INV F.INV.RT BETA.DIST BETA.INV GAMMA.DIST GAMMA.INV` and legacy `TDIST TINV CHIDIST CHIINV FDIST FINV BETADIST BETAINV GAMMADIST GAMMAINV` | docs | Degrees of freedom are truncated; below 1 or from 1e10 is `#NUM!`. Quantiles are solved to the last bit of the double, so they do not reproduce Excel's iteration limit (`#N/A` from `BETA.INV` after 100 steps) *(deviation)*. |
| `BINOM.DIST BINOM.DIST.RANGE BINOM.INV POISSON.DIST NEGBINOM.DIST HYPGEOM.DIST EXPON.DIST WEIBULL.DIST GAMMA GAMMALN GAMMALN.PRECISE` and legacy `BINOMDIST CRITBINOM POISSON NEGBINOMDIST HYPGEOMDIST EXPONDIST WEIBULL` | docs | Counts are truncated after the sign check. Cumulative sums are exact term sums for small sizes and the incomplete beta/gamma functions beyond. More than 1e15 trials, or a cumulative hypergeometric sum of more than 5e6 terms, is `#NUM!` *(deviation: Excel's limits are not documented)*. |
| `CONFIDENCE CONFIDENCE.NORM CONFIDENCE.T` | docs | `CONFIDENCE.T` with size 1 is `#DIV/0!`. |
| `Z.TEST ZTEST T.TEST TTEST F.TEST FTEST CHISQ.TEST CHITEST` | docs | `T.TEST` type 3 uses the Welch degrees of freedom unrounded. `F.TEST` is the two-tailed probability. `CHISQ.TEST` degrees of freedom are `(r-1)(c-1)`, or `max(r,c)-1` for one row or column. |

## Date and time

Source: the per-function pages under "Date and time functions (reference)". Serials are counted in `EvaluatorOptions.DateSystem` (1900 with its leap-year bug, or 1904); all
calendar arithmetic goes through `ExcelDate`. `TODAY` and `NOW` are volatile and read the local time of `ICalcResolver.Clock`, else `EvaluatorOptions.Clock`, else the system
clock (the only place the evaluator reads the wall clock). Date arguments may be serials, logicals or text; text is read
by `DateText` (below) before falling back to a number.

| Functions | Status | Notes |
|---|---|---|
| `DATE TIME` | docs | `DATE`: a year of 0-1899 has 1900 added, 10000 and over is `#NUM!`, months and days roll over (`DATE(2008,14,2)` is 2009-02-02, `DATE(1900,2,29)` is 60, `DATE(1900,1,0)` is 0), a negative serial is `#NUM!`. `TIME`: each part is truncated, parts above 32767 are `#NUM!`, the day part is dropped. Negative parts are `#NUM!` *(pending)*. |
| `DATEVALUE TIMEVALUE` | docs | `DateText`: `m/d/y`, `y-m-d`, `d-mmm-y`, `mmm d, y`, `d mmm y`, month-year, month-day and numeric month/day (current year from the clock); two-digit years pivot at 30; times with 24-hour or AM/PM clocks and fractional seconds; day names are ignored. English names and US order only. A time alone is a `#VALUE!` for `DATEVALUE` and a date alone is 0 for `TIMEVALUE` *(pending)*. |
| `YEAR MONTH DAY HOUR MINUTE SECOND` | docs | Negative serials and serials past 9999-12-31 are `#NUM!`. Times are rounded to the millisecond first (`ExcelDate`), so `SECOND(0.99999999)` is 0 *(pending: Excel may round to the second)*. |
| `WEEKDAY WEEKNUM ISOWEEKNUM` | docs | Types 1, 2, 3, 11-17 (`WEEKDAY`); 1, 2, 11-17, 21 (`WEEKNUM`). Serial 1 of the 1900 system is a Sunday as in Excel; serial 0 is a Saturday and `WEEKNUM(0)` is 0. |
| `EDATE EOMONTH` | docs | The day is clamped to the end of the target month. `EOMONTH` into February 1900 gives 60. |
| `DAYS DAYS360 DATEDIF YEARFRAC` | docs | `DAYS360` US: a start on the last day of a month counts as the 30th, an end on the 31st counts as the 30th once the start is the 30th; both on the last day of February make the end the 30th. `YEARFRAC` basis 1: the span within one year uses 366 when it holds a 29 February, longer spans use the average length of the years touched. `DATEDIF` units `Y M D MD YM YD`; `MD` borrows the length of the month before the end date's month. The 30/360 end-of-February rule and the `DATEDIF` `MD` borrow are *(pending)*. |
| `NETWORKDAYS NETWORKDAYS.INTL WORKDAY WORKDAY.INTL` | docs | Weekend numbers 1-7 and 11-17, or a seven-character Monday-first string (`1111111` is `#VALUE!`; a wrong length is `#VALUE!`; an unknown number is `#NUM!`). Holidays may be any array or range of dates, blanks are skipped, text that is not a date is `#VALUE!`. `WORKDAY` with more than 4,000,000 days is `#NUM!`. |

## Open: statistical and date/time, pending the 1.0 Excel grid

The generated grids are `testdata/calc/function-grid/statistical.*` and `datetime.*` (`tools/function-grid-stat/gen.py`); answers are recorded at the 1.0 gate.

1. `LARGE`/`SMALL` with a fractional `k` (rounded up here); `PERCENTILE` with a fractional `k` position is interpolated.
2. `PERCENTRANK` with a single value, and the exact rounding of the truncation to `significance` digits.
3. Average/sum rounding in `AVERAGE` for sums that cancel (the same snap as `SUM` is used).
4. `FREQUENCY` with unsorted bins, blanks or text in `bins_array`.
5. `LINEST` statistics with zero residual degrees of freedom, with `const` FALSE, and which of several collinear columns Excel drops.
6. `TTEST`/`T.TEST` with arrays of different sizes for type 1 (`#N/A` here) and pairs holding text.
7. The accuracy bound of the distribution kernels (about 1e-14 relative in the bulk and about 1e-13 for `GAMMA` of non-integers above 100), and which results Excel returns as `#NUM!` for extreme arguments (huge degrees of freedom, `p` within 1e-300 of 0 or 1).
8. `TIME` with negative parts, `DATEVALUE` of a time alone, and `TIMEVALUE` of a date alone.
9. Rounding of `HOUR`/`MINUTE`/`SECOND` for fractions within a millisecond of the next second.
10. `DAYS360` end-of-February rules, `YEARFRAC` basis 1 across several years, `DATEDIF` `MD` across a short month.
11. `EDATE`/`EOMONTH` from serial 0 and 60 (1900-01-00 and the fictitious 1900-02-29); `WEEKNUM`/`ISOWEEKNUM` before 1900-03-01.
12. Date text: `"1/5"` (current year), `"Jan-20"` (day or year), `"2020-01"`, 2-digit years at the pivot, `"Mon 1/1/2020"`, and text with both a date and a time given to `YEAR` etc.
13. `WEEKDAY(0)` and 1904-system weekdays (computed from the calendar, since 1904-01-01 is a Friday).
