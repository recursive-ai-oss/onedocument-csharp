# Calc: values, formulas, evaluator and function registry

Namespace `RecursiveAI.OneOffice.Calc` in the core package. This page is the contract the function families, the workbook binding, recalculation and the template engine build on. Excel is the specification for anything the text below does not settle; rules settled by a run against Excel 16.0 (build 20527) say so and cite the committed grid in `testdata/calc/`; a rule marked *(unverified)* is a first reading and is listed at the end.

## Values

`CalcValue` is a 24-byte struct: a kind tag, a `double` and an object slot.

| Kind | Holds | Allocation |
|---|---|---|
| `Blank` | an empty cell. `CalcValue.Missing` is a blank that remembers it was an omitted argument (`IsMissing`). | none |
| `Number` | a `double` (dates and times are serials). Never NaN or infinite: a result that is not finite is `#NUM!`. | none |
| `Text` | a `string` | the string |
| `Boolean` | TRUE or FALSE | none |
| `Error` | a `CalcError`: `#NULL! #DIV/0! #VALUE! #REF! #NAME? #NUM! #N/A #GETTING_DATA #SPILL! #CALC! #BLOCKED! #CONNECT! #BUSY! #FIELD! #UNKNOWN!` | none |
| `Array` | a `CalcArray`: rows × columns, row-major, cells are scalars. Dense, or one value broadcast over a shape (`CalcArray.Broadcast`). No nested arrays. | the array |
| `Reference` | a `CalcReference`: one or more `CellArea`s (sheet name or null for the formula's sheet, optional last sheet for 3-D, 1-based inclusive rows and columns), optionally `IsSpill` for `A1#`. Not read until something reads it. | the reference |
| `Lambda` | a `LambdaValue` (parameter names, body, captured names) | the lambda |

`CalcValue.Equals` is structural for scalars and identity for the rest. Comparison as the `=` and `<` operators do it is `Evaluator.Compare`.

`CalcConvert` holds Excel's scalar conversions: `ToNumber` (blank 0, TRUE 1, text read as number, percent, currency amount, date or time), `ToBoolean`, `ToText` (up to 15 significant digits), `NumberToText`, `TextToNumber`.

## Formula text

`FormulaParser.Parse` / `TryParse` read formula text (no leading `=` unless `LeadingEquals`) into a `FormulaNode` tree; `FormulaPrinter.Print` writes it.

* **Notations.** A1 and R1C1 (`FormulaParserOptions.Syntax`, with the cell the formula sits in as origin: relative R1C1 offsets are resolved against it). In the tree a coordinate is a `RefCoord(index, absolute)` and a relative index is an absolute position in the formula's own frame, so one tree prints as A1 or R1C1.
* **Forms.** `FormulaForm.File` is what is stored: en-US separators and names, `_xlfn.` / `_xlfn._xlws.` prefixes, `@` as `_xlfn.SINGLE(...)`, `#` as `_xlfn.ANCHORARRAY(...)`. `Display` is what a user reads: no prefixes, `@x`, `A1#`, local separators and names from a `FormulaLocale`.
* **Round trip.** A tree that was parsed and not edited prints back to the characters it came from in file form: nodes keep their spaces (`LeadingSpace`, `TrailingSpace`, the operator text), the spelling of numbers, references, names, function names (with prefixes), error literals and array constants. `FunctionCallNode.NameIsFileForm` is false for text a user typed (`FileForm = false`), where printing to file form adds the prefix `FutureFunctions.PrefixFor(name)` gives.
* **What parses.** Numbers; text with doubled quotes; TRUE/FALSE; error literals (all fifteen); cells, areas, whole rows and columns, with `$`; sheet prefixes (`Sheet1!`, `'My Sheet'!`, `Sheet1:Sheet3!`, `[1]Sheet1!`, `[1]!Name`, `Sheet1!#REF!`); defined names (dots and `_xlpm.` allowed); table references (`Table1[Col]`, `Table1[[#This Row],[Col]]`, `[@Col]`; the specifier is kept as written and `GetItems()` splits it); function calls with omitted arguments (`MissingNode`); calls of computed callees (`LAMBDA(x,x)(1)`, `InvokeNode`); array constants; operators with Excel's precedence, lowest first: comparison, `&`, `+ -`, `* /`, `^` (left associative), `%` postfix, prefix `+ - @`, then reference operators `:` (range), space (intersection), `,` (union, only inside parentheses). Unary minus binds tighter than `^`, so `-2^2` is 4 as in Excel.
* **Template extensions** (ours, off unless `TemplateExtensions`): a dotted name `item.price` becomes `MemberPathNode`, resolved through `ICalcResolver.ResolveMember`. Array lifting needs no syntax: a member path may return an array and the evaluator lifts operators and functions over it. In normal mode `item.price` is an ordinary defined name (names may contain dots).
* **Locales.** `FormulaLocale` carries the decimal, argument, array-column and array-row separators and an optional function-name table. `FormulaLocale.ForDecimalSeparator(",")` gives the comma-decimal family (`;` arguments and rows, `.` array columns); `ForDecimalSeparator(",", '\\')` is what Excel 16 wrote and accepted when its separators were set to a comma decimal on an en-US system (array columns `\`; the same with a space as thousands separator). 52 formulas agree with `Formula2` and `Formula2Local` in that configuration (`testdata/calc/excel-grid/loc*.fgrid`). Separators of the regional installs, and **localized function-name tables, are not shipped**: the only published per-language lists found are Microsoft's localized function pages, which are machine-translated and not reliable (e.g. `CEILING.MATH` becomes `DECKE. MATHEMATIK`, `ABS` becomes `АБС-пластик`), and the laptop's Excel is en-US only, so they cannot be checked. A `FormulaLocale` accepts any table the caller supplies.
* **Errors.** `FormulaParseException` (an `OfficeFormatException`, `DefectKind.Formula`) or `TryParse` with a `FormulaSyntaxError(code, message, offset, length)`; the codes are in `defects.md`. Text longer than `OfficeLimits.MaxFormulaLength` or nested deeper than `MaxFormulaDepth` throws `OfficeLimitException` from `Parse` and is `formula.too-long` / `formula.too-deep` from `TryParse`.
* **Shared formulas.** `FormulaTranslator.Translate(anchorTree, rowDelta, columnDelta)` moves relative coordinates; a reference pushed off the sheet becomes `#REF!`; unchanged subtrees are shared.
* **Positions.** Every parsed node has `Offset` and `Length` in the source text.

## Evaluator

`new Evaluator(resolver, registry?, limits?, options?)`; `Evaluate(tree, CalcCell origin)` returns a scalar, an array or a lambda, never a reference (a formula that is only `A1` returns that cell's value). One evaluator per thread. Opening a workbook never calls it; recalculation does.

* **Resolver.** `ICalcResolver` has one required method, `GetCell(sheet, row, column)`. Defaults, overridable: `GetRange` (loops `GetCell`), `TryExpandSheets` (3-D; default none, so `#REF!`), `ResolveName` (`#NAME?`), `ResolveStructured` (`#REF!`), `ResolveExternal` (`#REF!`: external links are default-deny), `TryGetSpill` (none, so `A1#` is `#REF!`), `ResolveMember` (`#NAME?`). A resolver may return references from `ResolveName` / `ResolveStructured` (names for ranges, tables). The sheet passed is the reference's sheet or the origin's.
* **Operators.** Errors propagate left first. Arithmetic coerces operands with `CalcConvert.ToNumber` (`#VALUE!` when text is not a number); results that are not finite are `#NUM!`; `x/0` is `#DIV/0!`; `0^0` is `#NUM!`, `0^negative` is `#DIV/0!`, negative base with a fractional exponent is `#NUM!`. Comparison orders numbers < text < booleans, text ignoring case, blank as 0 / "" / FALSE against the other kind, numbers equal when they agree to 15 significant digits. `&` converts with `ToText`. Operators lift over arrays: shapes broadcast (a one-row or one-column array stretches, a scalar stretches), a cell outside a shorter array is `#N/A`.
* **Near-zero snap** (measured: `testdata/calc/excel-grid`, probes `pr`). A `+` or `-` that is the outermost operation of the formula, and `SUM`'s additions, return 0 when the result is smaller than 2^-49 of the binade of the larger operand, that is fewer than 8 units in the last place of that operand: `=1+7ulp-1` is 0, `=1+8ulp-1` is not (checked at binades 2^-4, 2^-2, 2^0, 2^6 and 2^9). So `=1.1-1-0.1` and `=(1+1E-15)-1` are 0, `=SUM(1.1,-1,-0.1)` and `=SUM(0.1,0.2,-0.3)` are 0, but `=(1.1-1-0.1)` is 8.3E-17, `=IF(TRUE,1.1-1-0.1)` is 8.3E-17 and `=1.1-1` is 0.10000000000000009. Only the outermost operation snaps; any wrapper (parentheses, `IF`, `ABS`, `IFERROR`) leaves the raw result.
* **Power.** Negative base with a fractional exponent: the real root when the exponent is 1/odd (`(-8)^(1/3)` is -2), otherwise `#NUM!`. A negative base with an integer exponent of 2^32 or more is `#NUM!` (`(-1)^2147483649` is -1, `(-1)^4294967296` is `#NUM!`). `Math.Pow` differs from Excel's routine in the last digit or two for some inputs (`"12:30"^"1,000"`, 14th digit); not reproduced.
* **Number to text** (`&`, `CONCAT`, `TEXT` with General). 15 significant digits; plain digits up to 1E+19 and scientific from 1E+20 (`1.5E+20`); below 1, plain while the plain form needs at most 18 decimals, so `1E-18` and `1.5E-17` are plain, `1E-19` is `1E-19` and `1.23456789012345E-5` is `1.23456789012345E-05`.
* **Accepted by Excel only at the formula bar.** Excel refuses at entry: `SUM()`, `AND()`, a constant that overflows (`1E308*10`), `LET` with a repeated name, `#SPILL!` / `#CALC!` typed as literals, a leading `==`. Our parser accepts these and evaluates them (`SUM()` is `#VALUE!` here); the grid lists them in `mismatches.txt` as entry rejections, not value differences.
* **References.** Range `:`, intersection and union operate on references and give references (`#NULL!` for an empty intersection, `#VALUE!` for non-references). `A1#` asks the resolver for the spill extent.
* **Arrays and implicit intersection.** By default a range where one value is wanted makes the result an array (dynamic arrays). `EvaluatorOptions.LegacyImplicitIntersection` cuts it to the formula's row or column instead; `@x` and `SINGLE` do so explicitly. The caller gets the array; putting it on a grid and `#SPILL!` detection belong to recalculation.
* **LET / LAMBDA.** Names live in an immutable scope list; a lambda captures the list at creation. A lambda is invoked by `f(args)` (a LET name or parameter), by `LAMBDA(...)(args)`, by `Evaluator.Invoke`. Recursion deeper than `OfficeLimits.MaxRecursionDepth` yields `#NUM!` and sets `RecursionLimitHit`.
* **Limits.** `EvaluatorOptions.MaxSteps` (node visits, default 50,000,000) and `OfficeLimits.MaxAllocationBytes` (arrays, 24 bytes a cell) throw `OfficeLimitException`. Function code makes arrays with `Evaluator.NewCells` so the ceiling applies.
* **Allocation.** Scalar arithmetic and comparison over literals allocate nothing per evaluation (tested). References are cached on their node; function calls use a value stack, not per-call arrays; lifting, arrays and text results allocate what they hold.

## Function registry

`FunctionRegistry` maps names (case-insensitive, `_xlfn.` / `_xlws.` prefixes ignored) to `FunctionDefinition`s. `FunctionRegistry.CreateDefault()` returns a fresh registry with the library's functions; a function family adds one `Register…` method that calls `Add` for each function. Nothing is found by reflection; the evaluator is never edited to add a function.

`FunctionDefinition.Create(name, minArgs, maxArgs, kinds, implementation, flags, repeatLast)`:

* **Argument kinds** (`ArgKind`), one per position, the last repeating with `repeatLast`:
  * `Value`: one scalar. A one-cell reference is read; a block or array makes the call *lift*: the function runs once per element and the evaluator assembles the array result (shapes broadcast). Errors in `Value` arguments return the first error left to right without calling the function, unless the function has `HandlesErrors`.
  * `Array`: values as an array (references are read; a scalar stays a scalar; lambdas pass through). No lifting.
  * `Any`: the argument as evaluated: references stay references (so a function can tell `A1` from `5`, or take a shape), arrays and lambdas pass through.
* **Implementation** `EagerFunction(Evaluator, ReadOnlySpan<CalcValue> args)`; the span is valid during the call. An omitted argument arrives as `CalcValue.Missing`. Return a scalar, an array (a dynamic-array result) or a reference. Helpers on the evaluator: `Dereference`, `ToScalar`, `ToArray`, `ImplicitIntersect`, `TryReadAreas`, `NewCells`, `Invoke`, `Origin`, `Options`.
* **Lazy functions** `CreateLazy`: `LazyFunction(Evaluator, ReadOnlySpan<FormulaNode> args)` for `IF`, `IFERROR`, `LET`: evaluate only what is needed with `Evaluate(node)`.
* **Flags**: `Volatile` (sets `Evaluator.UsedVolatile`), `HandlesErrors`, `UsesOrigin`.
* **Arity**: too few or too many arguments is `#VALUE!`.
* **Custom C# functions**: `registry.AddCustom(name, min, max, args => value, isVolatile)`: value arguments that lift over arrays, errors propagate before the call, delegate-based and AOT-safe.

Shipped now (the seed set that proves the contract): `SUM AND OR NOT CONCAT LEN ISERROR ROWS COLUMNS INDEX SEQUENCE MAKEARRAY MAP TEXT IF IFS CHOOSE IFERROR IFNA LET LAMBDA SINGLE ANCHORARRAY`. `TEXT` runs on the number-format service. The lookup, dynamic-array and LAMBDA-helper families are in `docs/calc-functions.md`; the other families come as their own units. A registry function written bare (`REDUCE(0, a, SUM)`) works as a lambda in the helpers that take one.

## Future-function prefixes

`FutureFunctions.PrefixFor(name)`: the set is the `future-function-list` and `worksheet-only-function-list` rules of the Excel extensions to the file format (Formulas grammar), corrected by Excel 16.0: each of ~500 candidate names (the grammar plus Microsoft's alphabetical function list) was entered with 0..4 arguments and the saved file read back. Excel stores `NETWORKDAYS.INTL`, `WORKDAY.INTL`, `ISO.CEILING` and `ECMA.CEILING` bare although the grammar lists them, and prefixes `IFS`, `CONCAT`, `TEXTJOIN`, `SWITCH`, `MAXIFS`, `MINIFS`, `XLOOKUP`, `XMATCH`, `GROUPBY`, `REGEX*`, `TRIMRANGE` and others the grammar does not list. `FILTER` and `SORT` get `_xlfn._xlws.`. `@` on a range is not stored (`=@A1:A3` is saved as `A1:A3`); `_xlfn.SINGLE` appears only for an explicit `SINGLE`. Names the grammar lists that this build does not know (`COPILOT`, `LONGTEXT`, `PQSOURCE`, `PYTHON_*`, `QUERYSTRING`) follow the grammar. Answers: `testdata/calc/future-functions-excel.tsv`.

## Not here yet

Dependency graph, recalculation, putting spills on the grid, `#SPILL!` detection, workbook names / tables / sheets / external links (the resolver is the seam), localized function-name tables, BIFF Ptg and XLSB binary formulas, OpenFormula, the function families beyond the seed set.

## Open against Excel *(unverified)*

Text-to-number coercion for unusual spellings (fractions, parentheses); text ordering beyond the Windows-like word sort already matched on the grid; array separators and function names of regional Excel installs; how `INDEX` treats a missing row with a one-column range.
