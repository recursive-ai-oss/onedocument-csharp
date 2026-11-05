# Calc functions: math, logical, information, text

The functions registered by `FunctionRegistry.CreateDefault()` for these four families, written from Microsoft's per-function reference pages.
Columns: **status** is `docs` when the behaviour follows the reference page and its examples (covered by `FunctionFamilyTests`), `pending`
when the reference leaves an edge open and the choice made here waits for the Excel grid (`testdata/calc/function-grid`, answers not yet recorded).
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
| `RAND RANDBETWEEN RANDARRAY` | docs | Volatile. `RANDARRAY` rows/columns are truncated; `min > max` is `#VALUE!`. |
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
| `ISOMITTED` | docs | True when the LAMBDA parameter is bound to a missing value. Calling a lambda with fewer arguments is not yet supported by the evaluator, so it is FALSE in practice. |

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

## Not in these families

Lookup and reference, dynamic-array helpers, statistical, date and time, financial, engineering and database functions are separate registrations.
