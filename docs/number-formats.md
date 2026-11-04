# Number formats and date serials

Normative notes for `RecursiveAI.OneOffice.NumberFormats`: Excel's number-format code language as one shared service, and the date-serial rules it and the workbook need. The calc engine's `TEXT` and `VALUE`, cell rendering, text export and templates all call it, so a value reads the same everywhere.

Sources: ECMA-376 Part 1 §18.8.30 (`numFmt`, the built-in IDs and the General rules), §18.8.31 (`numFmts`), §18.2.28 (`date1904`); Microsoft's "Number format codes" and "Review guidelines for customizing a number format" articles; the `CELL` function reference. Where a rule below says *unsettled*, the text is our best reading and an Excel run of the grid (see "Verification") decides it.

## API

| Type | What it is |
|---|---|
| `NumberFormat` | A parsed, immutable, thread-safe format code. `Parse` (throws `OfficeFormatException`, defect `numfmt.*`), `TryParse` (returns a `NumberFormatDefect`), `Format(double)`, `TryFormat(double, Span<char>)` (allocation-free), `FormatText(string)`, `TryFormatText`, `Category`, `Sections`, `GetCellFormatCode()`, `GetBuiltInCode(id, culture)`, `FindBuiltInId(code)`. |
| `FormatOptions` | Date system, culture, General width (default 11), maximum width (default none). |
| `FormatResult` / `FormatStatus` | Status, length, section colour and the `*` fill hint. |
| `NumberFormatCulture` | Separators, month and day names, AM/PM, currency and date patterns for 13 locales, compiled in. |
| `ExcelDate` | Serial ↔ date and time for both date systems. |

Cell values stay numbers; a date is a serial. Formatting never reads the host culture: the output is the same with ICU, in invariant-globalization mode, under NativeAOT and in the browser.

## Sections and selection

A code has up to four sections separated by `;` outside quotes, brackets and escapes: positive, negative, zero, text. An empty code is `General`.

* One section applies to every number. Two: the first is for positive numbers and zero, the second for negatives. Three or four: positive, negative, zero (the fourth is text).
* A condition (`[>100]`, `=`, `<>`, `<`, `>`, `<=`, `>=`) replaces the default test of its section. At most the first two numeric sections may need one; the last numeric section takes whatever is left. If nothing matches, the last numeric section is used.
* The minus sign is shown before everything else in the section (`-$5`, `-x1`). It is left out when a later section is chosen without a condition of its own (`0;0` shows `5` for -5). A section chosen by an explicit condition shows the sign. *Unsettled* for sections with conditions that imply negatives (`[<0]-0;0`).
* A negative number that rounds to zero keeps its sign (`0.00` shows `-0.00` for -0.001); a negative zero is zero.
* Text: the fourth section, or else the first section that contains `@` and no digit placeholder. A format without a text section shows text unchanged. A text section without `@` shows only its literals.
* `[Red]`, `[Color1]` to `[Color56]` and the other seven names set the section colour (reported, never applied). `[DBNum1-3]` rewrites digits as CJK numerals.

## Literals and layout hints

`"text"`, `\c`, and the characters Excel allows unquoted (`$ - + / ( ) : ! ^ & ' ~ { } < > =` and space) are literal; so are non-ASCII characters. An unquoted ASCII letter that is not a code is the defect `numfmt.unexpected-character`. `_c` writes one space. `*c` writes nothing and reports `FillPosition`/`FillCharacter` so the renderer can repeat the character to the cell width. `[$text-LCID]` writes `text` (a currency tag) and selects the locale for names and calendar; `[$-F800]` and `[$-F400]` are the system long date and time (resolved against US English at parse time).

## Numbers

Excel shows at most 15 significant digits. A double is first reduced to 15 significant decimal digits (correctly rounded from the exact binary value), and everything after (percent and thousands scaling, rounding, digit placement) works on those decimal digits with round-half-away-from-zero. So `0.285` with `0.00` is `0.29`, `1.005` is `1.01`, `0.1+0.2` is `0.30`, and `1234567890123456789` with `0` is `1234567890123460000`.

* `0` always shows a digit, `#` only a significant one, `?` a space in place of an insignificant zero. Extra integer digits are always shown. With only `#` left of the point, a value below 1 starts with the point (`.47`); the point itself is always written when the code has one (`0.#` shows `1.`).
* A comma between placeholders groups thousands (the culture's separator); after the last placeholder each comma divides by 1000. `%` multiplies by 100 and is written where it stands (each `%` counts).
* Scientific: `E+`/`E-` (and lower case, kept as written) followed by 0/# for the exponent digits. The integer placeholder count sets the exponent step (`##0.0E+0` is engineering). Rounding that carries into a new digit moves the exponent.
* Fractions: `# ?/?`, `# ??/??`, `?/?` (improper), fixed denominators (`# ?/8`). The denominator is the closest fraction within the placeholder width (smaller wins a tie). A zero numerator in a variable-denominator code shows blanks after the whole part (`2` with `# ?/?` is `2` plus spaces); with a fixed denominator it shows `0/8`. *Unsettled*: both.
* `General`: up to `GeneralWidth` characters (11; the sign is not counted), at most 15 significant digits. Scientific (`1.23457E+11`) when the integer part is wider than that, or below 0.0001. `GeneralWidth` is a parameter: the cell display passes the column's width, `TEXT` passes a larger number to get the full 15 digits. *Unsettled*: the small-number switch (ECMA-376 §18.8.30 describes it by rule: fixed while all significant digits fit, exponential when the exponent is below -3 and they do not).
* `MaxWidth`: when the caller gives a width and the text is wider, `TryFormat` returns `DoesNotFit` and a row of `#` of that width (not for General, which fits itself).

## Dates and times

Codes: `y yy yyy yyyy`, `m mm mmm mmmm mmmmm`, `d dd ddd dddd`, `h hh`, `m mm` (minutes), `s ss`, `AM/PM am/pm A/P a/p`, `[h] [m] [s]` (elapsed, `[hh]` pads), `.0 .00 .000` after seconds, `e ee`, `g gg ggg`, `b bb bbbb`. `m`/`mm` are minutes right after an hour code or right before a seconds code (literals between are skipped), otherwise months. AM/PM selects the 12-hour clock. `y` and `yy` are two digits, `yyy` and `yyyy` four. A date or time section that has digit placeholders is the defect `numfmt.mixed-codes`.

* Serials: 1900 system: 0 is "1900-01-00" (a Saturday), 1 is Sunday 1900-01-01, 60 is the fictitious 1900-02-29, 61 is 1900-03-01 and later serials are real days (1899-12-30 plus the serial). 1904 system: 0 is Friday 1904-01-01. The largest serials are 2958465 and 2957003 (9999-12-31). Weekdays follow Excel's count, so serials below 61 differ from the real calendar.
* Times are fractions of a day rounded to the nearest millisecond; for display they are rounded to the precision of the code (whole seconds unless `.0` digits are present), and a time that rounds up to a whole day carries into the next date (23:59:59.9996 shows `00:00:00` the next day). *Unsettled*: seconds are rounded, not truncated.
* A date or time format of a negative number, or one past the last day, is not shown: `FormatStatus.DateOutOfRange`; `Format` returns `#` characters. Elapsed codes follow the same range.
* `e`, `g`: Gregorian year unless the locale tag or its calendar bits say otherwise: Japanese eras (Meiji from 1868-09-08, Taisho, Showa, Heisei, Reiwa; `g` one letter, `gg` the first kanji, `ggg` the era name), Minguo (`[$-404]`, year-1911, `民國`), Thai Buddhist (year+543). `b` is always the Buddhist year. Locale tags `[$-xxxxxxxx]`: the low 16 bits are the LCID; the next byte is a Windows calendar ID (3 Japanese, 4 Taiwan, 7 Thai); other calendars (Hijri, Hebrew) fall back to Gregorian.

## Numerals

`[DBNum1]` (〇一二三…, 十百千万亿), `[DBNum2]` (financial: 零壹贰叁…拾佰仟), `[DBNum3]` (full-width digits for Japanese and Korean, Arabic digits with units for Chinese). Whole numbers become positional numerals (`一百二十三`), digits after the decimal point and every run of three or more digits in a date (the year) are written one by one. Strings longer than 256 characters keep plain digits. *Unsettled*: all of it; Chinese-locale details differ per application.

## Cultures

`NumberFormatCulture` covers en-US, en-GB, de-DE, fr-FR, es-ES, it-IT, pt-BR, nl-NL, ru-RU, ja-JP, zh-CN, zh-TW and ko-KR. It holds the decimal and group separators (French groups with U+202F, Russian with U+00A0), month and day names, AM/PM text, the local currency symbol and the short and long date patterns. Russian months take the genitive form when a day number is in the same section. A `[$-LCID]` tag changes names, AM/PM and calendar for its section; separators always come from `FormatOptions.Culture`. A locale outside the table falls back to its language, then to US English.

Built-in IDs 0 to 49 come from `NumberFormat.GetBuiltInCode`. IDs 5 to 8, 14, 22 and 41 to 44 follow the culture's currency symbol and date pattern; 37 to 40 are written as Excel saves them (`#,##0_);(#,##0)`; the ISO text shows a space where Excel writes `_)`). IDs 27 to 36 and 50 to 58 are defined for zh-TW, zh-CN, ja-JP and ko-KR, and are `null` for other cultures. IDs 59 to 81 (Thai) are not implemented.

## Categories and `CELL("format")`

`Category`: General, Number, Currency, Accounting (a `*` fill), Date, Time, DateTime, Percentage, Fraction, Scientific, Text, Custom, from the first section that applies to numbers. `GetCellFormatCode()` returns the `CELL` codes `G`, `F0`, `,2`, `C0`, `P2`, `S2`, `D1`…`D9`, with `-` when negative numbers are coloured and `()` for parentheses; codes Excel has no letter for give `G`.

## Defects

Invalid codes are never exceptions of another type. `TryParse` returns a `NumberFormatDefect` (code, message, position in the code); `Parse` throws `OfficeFormatException` with the same code. Codes: see `docs/defects.md` (`numfmt.*`).

## Verification

* `tools/NumFmtGrid` writes the edge-case grid: two workbooks (`grid-text.xlsx`: `=TEXT(value, code)`; `grid-cells.xlsx`: values in cells formatted with the code) over about 5,700 code × value cases, and our answer for each case in `manifest.tsv`. An Excel run reads back `TEXT` and the cell's display text; `NumFmtGrid compare` reports agreement. `tools/NumFmtGrid/run-libreoffice.sh` does the same with LibreOffice headless as an oracle (information only).
* Unit tests take their expected strings from Microsoft's articles (cited in the test file) or from the rules above.
* Fuzz targets `numfmt` (parser and formatter on arbitrary codes) and `numfmtfmt` (string, span and exact-size span agree).
* The span path allocates nothing; a test measures it over thousands of calls.
