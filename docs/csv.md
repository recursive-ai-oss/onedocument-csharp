# CSV and TSV

Delimited text is a native layer of `RecursiveAI.OneOffice.Spreadsheets` (namespace `RecursiveAI.OneOffice.Spreadsheets.Csv`): a streaming reader, a value parser, a streaming writer, and a bridge to the workbook model. The rules below are RFC 4180 (§2, definitions of the format) as the baseline, extended the way Excel reads and writes the files. Where Excel's behaviour was not documented it was found by opening generated files in Excel 16.0 (build 20527) and comparing cell by cell; the generator and the comparison are `tools/csv-grid/` and `tools/CsvProbe/`.

## Reader (`CsvReader`)

Forward-only. After `ReadRecord()` the fields of the record are spans over the reader's buffer, already unquoted. Reading allocates nothing per field once the buffers have grown to the largest record, and memory is the largest record, not the file. One record is limited by `OfficeLimits.MaxAllocationBytes`.

- **Records** end at CR LF, LF or a lone CR (Excel reads all three). A final line break does not start a record. An empty line is a record of one empty field (`IsBlank`), as Excel keeps it as an empty row; `SkipBlankLines` drops them. A delimiter at the very end of the input still gives a last empty field.
- **Quoting.** A field that starts with the quote character is quoted: it may hold delimiters, line breaks and doubled quotes (`""` is one quote). A quote anywhere else in a field is ordinary text (`5" pipe`). Text between a closing quote and the next delimiter is kept after the quoted text (`"ab"cd` is `abcd`).
- **Delimiter.** Set it, or leave it null to detect it: of comma, semicolon, tab and pipe, the one that splits each of the first (up to) 20 records into the same number (more than one) of fields outside quotes, from the first 32,768 characters; otherwise the one that occurs most; otherwise comma. A first line `sep=X` (what Excel writes and reads) names the delimiter and is not data.
- **Encoding.** A byte-order mark wins: UTF-8, UTF-16 little endian, UTF-16 big endian. Without one: the caller's `Encoding` if given; else UTF-8 when the bytes are valid UTF-8 (a seekable stream is checked whole; a forward-only stream by its first block), else Windows-1252, which the reader decodes with its own table, so no code-page provider is needed (defect `csv.encoding-assumed`). Excel reads UTF-8 without a mark as the system's legacy code page and shows mojibake; the reader deliberately does better. Any other code page needs the caller to register `CodePagesEncodingProvider` and pass the `Encoding`, which is a platform decision (trimmed and WebAssembly hosts may not have the tables).
- **Ragged records** (a different field count from the first record) are returned as they are, never padded or cut, with `csv.ragged-record` located by character offset.

### Defects

| Code | Severity | Meaning |
|---|---|---|
| `csv.ragged-record` | repaired | A record has a different number of fields from the first. |
| `csv.stray-quote` | repaired | Text follows the closing quote of a quoted field; it is kept after the quoted text. |
| `csv.quote-unterminated` | repaired | A quoted field is open at the end of the input; it ends there. |
| `csv.encoding-assumed` | repaired | No byte-order mark and not valid UTF-8; read as Windows-1252. |
| `csv.encoding-invalid` | repaired | A byte sequence is not valid in the encoding (or UTF-16 ends in half a unit); it reads as U+FFFD (or is dropped). Reported once per reader. |
| `csv.grid-exceeded` | dropped | Import only: more than 1,048,576 records or 16,384 fields in a record; the rest are dropped. |

## Values (`CsvValueParser`)

Reading is separate from splitting: a field is text until the parser says otherwise, and `CsvValueOptions.Infer = false` keeps everything as text. The parser allocates nothing per call. Booleans and errors must be the whole field (Excel keeps `" TRUE"` as text); everything else ignores spaces around the field.

What is recognised, with the number format Excel gives the cell (en-US):

| Text | Value | Format |
|---|---|---|
| `42`, `-3.5`, `+7`, `.5`, `5.`, `00` | number | General |
| `1,234`, `1,234.5`, `1,2345` | number | `#,##0`, `#,##0.00` |
| groups: first group 1–3 digits without a leading zero, every later group 3 or more digits | | `1,23`, `12,34`, `0,123`, `1,234,56` stay text |
| `--5`, `+-5` | up to two signs, each `-` flips | |
| `$5`, `-$5`, `$-5`, `($5)`, `$1,234.50` | number | `$#,##0_);[Red]($#,##0)`, with `.00` when decimals were written |
| `(5)`, `(1,234)` | negative number | General / `#,##0`; a sign inside the parentheses is text; `5-`, `5$`, `5+` are text |
| `1E3`, `1.5e-2`, `1,000E3` | number | `0.00E+00` |
| `5%`, `5 %`, `%5`, `(5%)`, `0.5%`, `1,000%` | fraction | `0%`, `0.00%` when decimals were written; `5%%` is text; `1E3%` is the number 10 |
| `TRUE`, `false` (any case) | boolean | |
| `#N/A`, `#DIV/0!`, `#VALUE!`, `#REF!`, `#NAME?`, `#NUM!`, `#NULL!`, `#SPILL!`, `#CALC!`, `#GETTING_DATA` | error | |
| `1/2/2020`, `1-2-20`, `2020-01-02`, `2020/1/2` | date | `m/d/yyyy` (the culture's short date elsewhere) |
| `2-Jan-2020`, `2 Jan 20`, `2 January 2020`, `Jan 2, 2020`, `Sept 2, 2020` | date | `d-mmm-yy` |
| `Jan 2020`, `Jan-2020`, `January 2020` | first of the month | `mmm-yy` |
| `13:45`, `1:45`, `1:45 PM`, `1:45 P`, `12:00 AM`, `0:30 AM`, `1:` | time of day | `h:mm`, `h:mm AM/PM`, `h:mm:ss` with seconds, `mm:ss.0` with fractions of a second |
| `24:00`, `25:30`, `100:00` | elapsed time | `[h]:mm:ss` |
| date and time joined by one space | date and time | `m/d/yyyy h:mm` |

Text that Excel also keeps as text: `1/1/1899` and earlier (years before 1900), `2/29/2019`, `13/1/2020`, `2020-02-30`, `99-01-02` (two-digit year first), `20200102` is a *number*, `2020.01.02`, `Jan 2 2020` and `Jan-2-2020` and `2 Jan, 2020` (the comma is required in the month-first form and forbidden in the day-first form), `Monday, January 2, 2020`, `1:45PM` (a space is required before AM/PM), `13:45 AM`, `2020-01-02T13:45:30` (ISO 8601 with `T` is not read by Excel), `1.2.2020` (only where the culture's date separator is a dot), numbers outside Excel's range (below 2.2250738585072014E-308 or above 9.99999999999999E+307 in magnitude), `Infinity`, `NaN`, `0x1F`.

### Deliberate differences from Excel

- **A date without a year** (`1/2`, `1-2`, `2-Jan`, `Jan 2`, `Jan-20` meaning 20 January) is a date in the current year in Excel. The library never reads the clock, so these stay text unless `CsvValueOptions.DefaultYear` gives the year; with it they read as Excel does (`d-mmm` format).
- **Leading zeros and long numbers.** Excel turns `007` into 7 and keeps 15 significant digits of `12345678901234567890`. By default a whole number written with a leading zero, or with more than 15 digits, stays text (`PreserveLeadingZeros`, `PreserveLongNumbers`); set them false to match Excel, which then keeps 15 significant digits (later integer digits become zeros, later fraction digits are dropped).
- **Formulas.** Excel evaluates a field such as `=1+1` (and `-Infinity`, as `=-Infinity`) when it opens the file. The reader keeps them as text: a CSV is data, and running it is the injection the writer defends against.
- **Encoding** as above.
- **Two-digit years** use Excel's pivot (00–29 are 2000–2029, 30–99 are 1930–1999).

## Writer (`CsvWriter`, `CsvDocument.Write`)

`CsvWriter` writes fields one at a time and ends records with `EndRecord()`; it buffers a little and holds no record, so any size is constant space. Defaults are Excel's: comma, double quote, CR LF, UTF-8 **with** a byte-order mark (without it Excel garbles non-ASCII text; `ByteOrderMark = false` for tools that dislike the mark), quotes only where needed (a field with the delimiter, a quote, CR or LF), an empty field as nothing. `Quoting = Always` quotes every non-empty field. The encoding is any `Encoding`; unmappable characters become `?` (the platform's replacement).

`CsvDocument.Write(sheet | workbook, stream, options, loss)` writes the used range from A1, one record per row (empty rows are empty lines, empty cells are empty fields), the first visible worksheet of a workbook. A formula cell writes its cached value. With `Formatted = true` (the default, as Excel's Save As) a number is written as its number format displays it (`General` at up to 20 characters, so full precision is kept; dates as dates), with `Formatted = false` raw: the shortest text that reads back as the same double, dates as serial numbers. Booleans are `TRUE`/`FALSE`, errors their text.

### Formula injection

A spreadsheet runs a field that starts with `=`, `+`, `-` or `@` (and, in some programs, a tab or carriage return). With `FormulaInjection = Escape` — **the default** — *text* that starts with one of these gets a single quote put in front of it and is written quoted: `"'=1+1"`. Numbers written as numbers (a negative amount) are never touched. The default is on because a CSV is most often opened in a spreadsheet and the cost is a visible quote on the few real texts that start with these characters; programs that read the file back choose `None`. `CsvWriter.WriteField(text, isText: false)` marks the text of a number.

### Loss report

Writing a sheet reports into an `ILossSink` (a `LossReport`): `loss.formulas` (cells, written as cached values), `loss.cell-styles` (cells with a style other than the default), `loss.number-formats` (cells that had one, in raw mode only), `loss.sheets` (sheets not written), `loss.merged-cells` (ranges), `loss.embedded-objects` (drawings, charts, tables, comments, pivot tables).

## The workbook

`Workbook.Open` finds delimited text by content (the sniffer wants two complete lines with the same number of tabs, commas or semicolons) and gives a workbook of one worksheet (`Sheet1`), with every field a cell: values typed as above, dates, times, percentages, grouped numbers and currency with a number format Excel would give them (a fixed style sheet is generated), text as strings. A tab-separated content is read with a tab. `CsvDocument.ToWorkbook(stream | path, CsvImportOptions)` sets the delimiter, encoding, inference, a header row (kept as text) and the sheet name. `workbook.SaveAs("x.csv")` and `SaveAs("x.tsv")` write the first visible sheet with the defaults. Cell counts are bounded by `OfficeLimits.MaxRecordCount`.

## What was checked

Reading against Excel 16.0.20527: 455 generated value cases and 14 shape files opened in Excel and read cell by cell; the numbers are in the unit's report. Fuzzing: `fuzz/run.sh csv`. Corpus: 89 CSV files read with typed defects only, field-equal after a rewrite.
