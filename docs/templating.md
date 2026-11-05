# Templating: the engine core

Namespace `RecursiveAI.OneDocument.Templating` in the core package. This page states the template syntax, the extensions to Excel formula syntax, how the engine reads data, the contract a document binder implements, and the defect codes (the full catalogue is in `defects.md`). The engine knows no document format: the DOCX, XLSX and PPTX binders supply text and rebuild documents from what the engine returns.

The syntax is Liquid/Jinja-style blocks around Excel-formula expressions. It is written in one place (`TemplateSyntax`) and separate from the renderer, so the surface can change without touching evaluation.

## Syntax

| Form | Meaning |
|---|---|
| `{{ expression }}` | prints the value of an expression |
| `{% for item in expression %} … {% else %} … {% endfor %}` | repeats the body for each item; `else` runs when there are none |
| `{% if expr %} … {% elif expr %} … {% else %} … {% endif %}` | picks one branch (`elsif` is accepted for `elif`) |
| `{% include 'name' %}` | renders another template found by `ITemplateIncludes`; its text is inserted where the tag stands |
| `{% raw %} … {% endraw %}` | prints the delimiters inside as text |
| `{# … #}` | a comment |
| `{% image expr name=value … %}`, `{% html expr … %}`, `{% markdown expr … %}` | parsed and handed to the binder as a piece with the evaluated value and the options; the engine inserts nothing itself |
| `scope=table-row` on `for` or `if` | a repetition-scope hint for the binder (see below) |

* **Whitespace control.** `{{-`, `-}}`, `{%-` and `-%}` remove the white space next to the tag, inside the same paragraph or line.
* **Tag-only units.** A paragraph or line that holds only block tags (and white space) produces nothing, so `{% for %}` and `{% endfor %}` on lines of their own leave no empty lines. A unit with an output tag or any text is kept, and an empty line stays an empty line.
* **Delimiters** are configurable per template (`TemplateOptions.Delimiters`); the three openers must differ and none may begin another.
* **Quotes.** Word's autocorrect turns `"` into curly quotes; `“ ” „ ‟` are read as `"` and `‘ ’ ‚ ‛` as `'` inside tags. A closing delimiter inside a string literal (`{{ "}}" }}`) does not end the tag. In `include`, `'name'` is a string.
* **Loop variables.** `loop.index` (from 1), `loop.index0`, `loop.first`, `loop.last`, `loop.length`, `loop.revindex`, `loop.revindex0`, and `loop.parent` for the enclosing loop.
* **Truthiness** (`if`): a boolean is itself; a number is true unless 0; text is true unless empty; a record is true; a collection is true when it has items; missing data and null are false. A formula that returns an array is true when any element is.
* **Output.** Numbers print as the General format does (15 significant digits); TRUE/FALSE; dates from data print as ISO 8601 (`2024-03-09`, with the time when it is not midnight) and are serial numbers inside formulas, so `TEXT(d, "dd/mm/yyyy")` formats them; an array prints its cells separated by `, `; a record or null prints empty. Output is deterministic: `NOW()`, `TODAY()` and `RAND()` read `TemplateOptions.Clock` and `RandomSeed`.

## Expressions: Excel formulas plus two extensions

Expressions are Excel formulas parsed by the calc parser and evaluated by the calc evaluator with the full function library, so `TEXT`, `SUM`, `IF`, `LET`, `LAMBDA`, `FILTER`, `SEQUENCE` and the number-format language behave as in a cell. Functions registered with `FunctionRegistry.AddCustom` (`TemplateOptions.Functions`) are the only extension point; the engine compiles no code and uses no reflection.

These are **our extensions to Excel formula syntax**, not Excel behaviour:

1. **Dotted member paths.** `customer.name`, `items.price`: the first segment is a variable (a loop variable, or a member of the root data); later segments are members, matched ignoring case (an exact spelling wins). In the formula AST this is `MemberPathNode`; it prints back exactly as written. A path through `null` is `null`; a missing member is missing data.
2. **Array lifting.** A member of a collection is the collection of that member over its items: `items.price` is the array of every item's `price`, so operators and functions lift over it: `SUM(items.price * items.qty)`. A member that is itself a collection is flattened one level (`orders.lines.price`). A collection also has `length` and `count` when its items have no member of that name. An empty collection reads as one blank cell, so `SUM` gives 0 (Excel's empty array is `#CALC!`).

A plain name or dotted path is read straight from the data, never parsed as a cell reference, so a variable called `rev7` works. Inside a larger formula a cell-style name such as `rev7` also reads the variable of that name (there is no grid); a range over such names is `#REF!`.

A bare record used as a value (`{{ customer }}`) is `#VALUE!` in a formula and prints empty on its own.

## Data

The engine reads data only through `ITemplateData` (null, scalar, record, sequence). `TemplateData.From(object)` adapts, without reflection:

| Source | Notes |
|---|---|
| Classes marked `[TemplateModel]` (partial; also structs, records) | the source generator implements `ITemplateObject`: every public readable instance property and field, matched exactly then ignoring case |
| `JsonDocument`, `JsonElement` | objects, arrays, numbers, strings, booleans, null |
| `IDictionary` / `Dictionary<string, T>` (any value type) | keys are read as text |
| Any `IEnumerable` (lists, arrays) | a sequence; capped at `MaxRows` |
| `DataTable`, `DataRow`, `DataRowView`, `IDataReader` | a table is a sequence of records by column name; a reader is read to the end (capped); `DBNull` is null |
| XML (`TemplateData.FromXml`) | read with the library's tokenizer (no DTDs). Attributes and child elements are members; repeated elements are a sequence; text that reads as a number is a number in formulas and still prints as written |
| strings, numbers, `bool`, dates, `Guid`, enums (their name), `byte[]` | scalars |

Anything else (a class with no generated model) throws `OfficeUnsupportedException` rather than being read by reflection.

## Missing data and errors

All problems with the template are found by `TemplateEngine.Parse`, before any data is bound: `ParsedTemplate.Defects` lists every error with its place. Rendering an invalid template throws `TemplateException` for the first.

At render time the default is lenient: missing data renders empty and is a `template.missing-data` warning at the tag; an expression that evaluates to an error value prints that value and is `template.expression-error`. With `TemplateOptions.Strict`, the first of these throws `TemplateException`. Missing data that a formula handles (`IFERROR`, `ISBLANK`...) is not reported.

## Limits and sandbox

* No file, network or clock access except through `ITemplateIncludes`, a supplied `TimeProvider` and registered functions. `include` is denied unless the caller supplies a resolver.
* `TemplateOptions.MaxIterations` (1,000,000 loop iterations per render), `MaxOutputCharacters` (64M), `MaxBlockDepth` (64: nested blocks and includes together, also bounded by `OfficeLimits.MaxRecursionDepth`); `OfficeLimits` formula length, depth and evaluation limits apply to every expression. Exceeding one throws `OfficeLimitException`.
* `CancellationToken` is checked on every node and loop iteration.

## The binder contract

A binder converts a document part to a `TemplateSource`, renders it, and rebuilds the part.

* **Units and segments.** A `TemplateUnit` is the structural thing that can be repeated or dropped (a Word paragraph, a table-cell paragraph, a slide text shape, a cell). It holds `TemplateSegment`s: the text of its runs, in order. Tags may be split across segments (Word splits a tag whenever it was typed in several edits, or spell-checked, or revised); the engine reads a unit's segments as one text. A tag never spans two units.
* **Result.** `TemplateResult.Units` is the output: a list of `TemplateRenderedUnit(SourceUnit, Pieces)`. A source unit may appear several times (a loop body), once, or not at all (false branch, tag-only unit). A binder emits, for each, a clone of the source unit with its runs rebuilt from the pieces.
* **Pieces.** `TemplatePiece(Kind, Segment, Text, Insertion)`: a text piece takes the formatting of segment `Segment` of the source unit. Text kept from the template stays in the segment it came from. A printed tag takes the segment where the tag *began*; a tag split over segments 3 and 4 prints into 3, and what follows the tag in 4 stays in 4. Adjacent text pieces from the same segment are merged. Image, HTML and Markdown tags come as non-text pieces with a `TemplateInsertion(Value, Options)` for the binder to render.
* **Repetition scope.** By default a block repeats what lies between its tags in the unit order: inside one unit that is inline text (no new unit per iteration); across units it is the units between the tags, and each iteration starts a new output unit. To repeat larger structures (a table row whose tags sit in two cells, a slide, a sheet row) the binder implements `ITemplateScopeResolver` and returns the range of units a block covers, given the author's `scope=` hint and the units of the two tags. The engine then repeats (or keeps) those whole units, every iteration producing new units even when both tags sit in one unit, and the tags print nothing. A range that misses a tag is `template.scope-invalid`.
* **Plain text.** `TemplateSource.FromText` makes one unit per line; `TemplateEngine.RenderText` is the plain-text binder.
* **Locations.** `TemplateUnit.Location` (for example a part URI plus a path) is used for defects together with the offset in the unit's text; without it, defects carry a character offset in the whole text.

## Not here

The DOCX, XLSX and PPTX binders, Word mail merge, content-control binding, rendering of image/HTML/Markdown tags, and the `Template.Render` one-liner come with the binders.
