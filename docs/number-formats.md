# Number formats and date serials

Normative notes for `RecursiveAI.OneOffice.NumberFormats`: Excel's number-format code language as one shared service, and the date-serial rules it and the workbook need. The calc engine's `TEXT` and `VALUE`, cell rendering, text export and templates all call it, so a value reads the same everywhere.

Sources: ECMA-376 Part 1 §18.8.30 (`numFmt`, the built-in IDs and the General rules), §18.8.31 (`numFmts`), §18.2.28 (`date1904`); Microsoft's "Number format codes" and "Review guidelines for customizing a number format" articles; the `CELL` function reference. Excel is the specification for everything these pages leave open. Rules marked *observed in Excel* were read back from desktop Excel (365, build 16.0.20527, US English settings) over a grid of 7,602 code and value pairs (see "Verification"); the answers are committed in `testdata/numfmt/excel-grid.tsv` and checked by a test, so the formatter agrees with Excel on every case of the grid. The few things the grid did not reach are listed under "Not verified".

## API

| Type | What it is |
|---|---|
| `NumberFormat` | A parsed, immutable, thread-safe format code. `Parse` (throws `OfficeFormatException`, defect `numfmt.*`), `TryParse` (returns a `NumberFormatDefect`), `Format(double)`, `TryFormat(double, Span<char>)` (allocation-free), `FormatText(string)`, `TryFormatText`, `Category`, `Sections`, `GetCellFormatCode()`, `GetBuiltInCode(id, culture)`, `FindBuiltInId(code)`. |
| `FormatOptions` | Date system, culture, General width (default 11), maximum width (default none). |
| `FormatResult` / `FormatStatus` | Status, length, section colour and the `*` fill hint. |
| `NumberFormatCulture` | Separators, month and day names, AM/PM, currency and date patterns for 24 locales, compiled in. |
| `ExcelDate` | Serial ↔ date and time for both date systems. |

Cell values stay numbers; a date is a serial. Formatting never reads the host culture: the output is the same with ICU, in invariant-globalization mode, under NativeAOT and in the browser.

## Sections and selection

A code has up to four sections separated by `;` outside quotes, brackets and escapes: positive, negative, zero, text. An empty code is `General`.

* One section applies to every number. Two: the first is for positive numbers and zero, the second for negatives. Three or four: positive, negative, zero (the fourth is text).
* A condition (`[>100]`, `=`, `<>`, `<`, `>`, `<=`, `>=`) replaces the default test of its section. At most the first two numeric sections may need one; the last numeric section takes whatever is left. If nothing matches, the last numeric section is used.
* The minus sign is shown before everything else in the section (`-$5`, `-x1`). It is left out when the section is for negative numbers only. *Observed in Excel*:
  * with no condition anywhere, the second and third sections are the negative ones: `0;0` shows `5` for -5;
  * a section with a condition drops the sign when the condition holds only below zero (`[<0]`, `[<-3]`, `[<=-3]`, `[=-5]`); `[<=0]`, `[<5]` and `[<>-5]` keep it (`[<=0]"n";0` shows `-n`);
  * a leftover section after conditions drops it when the first condition is `<`, `<=` or `<>` (`[<=-3]0;0.0` shows `1.0` for -1), and keeps it after `>`, `>=` or `=` (`[>100]"big";0.0` shows `-5.0`).
* *Observed in Excel*: a negative number that rounds to zero shows no minus sign (`0.00` shows `0.00` for -0.001), except in a fraction with a whole part (`# ?/?` shows `-0` plus spaces). A negative zero is zero.
* *Observed in Excel*: when every section has a condition and none matches (`[>0]0;[<0]0` for 0), nothing can be shown: `FormatStatus.NotRepresentable`, a row of `#`. A section that holds only a colour or a condition (`[Red][<=100];[Blue][>100]`) shows the number as General.
* Text: the fourth section, or else the first section that contains `@` and no digit placeholder. A format without a text section shows text unchanged. A text section without `@` shows only its literals. *Observed in Excel*: a number put through a format that has only a text section (`@`, `"x"@`) shows as General, without the literals; and `@` next to digit placeholders (`0.00@`) is an invalid code.
* `[Red]`, `[Color1]` to `[Color56]` and the other seven names set the section colour (reported, never applied). `[DBNum1-3]` rewrites digits as CJK numerals.

## Literals and layout hints

`"text"`, `\c`, and the characters Excel allows unquoted (`$ - + / ( ) : ! ^ & ' ~ { } < > =` and space) are literal; so are non-ASCII characters. An unquoted ASCII letter that is not a code is the defect `numfmt.unexpected-character`. `_c` writes one space. `*c` writes nothing and reports `FillPosition`/`FillCharacter` so the renderer can repeat the character to the cell width. `[$text-LCID]` writes `text` (a currency tag) and selects the locale for names and calendar; `[$-F800]` and `[$-F400]` are the system long date and time (resolved against US English at parse time).

## Numbers

Excel shows at most 15 significant digits. A double is first reduced to 15 significant decimal digits (correctly rounded from the exact binary value), and everything after (percent and thousands scaling, rounding, digit placement) works on those decimal digits with round-half-away-from-zero. So `0.285` with `0.00` is `0.29`, `1.005` is `1.01`, `0.1+0.2` is `0.30`, and `1234567890123456789` with `0` is `1234567890123460000`.

* `0` always shows a digit, `#` only a significant one, `?` a space in place of an insignificant zero. Extra integer digits are always shown. With only `#` left of the point, a value below 1 starts with the point (`.47`); the point itself is always written when the code has one (`0.#` shows `1.`).
* A comma between placeholders groups thousands (the culture's separator); after the last placeholder each comma divides by 1000. `%` multiplies by 100 and is written where it stands (each `%` counts).
* Scientific: `E+`/`E-` followed by 0/# for the exponent digits. *Observed in Excel*: only the capital E; `0.00e+00` is invalid (`numfmt.exponent-without-digits`). The integer placeholder count sets the exponent step (`##0.0E+0` is engineering). Rounding that carries into a new digit moves the exponent. For the value zero every integer placeholder shows `0` (`#0.0E+0` gives `00.0E+0`).
* Fractions: `# ?/?`, `# ??/??`, `?/?` (improper), fixed denominators (`# ?/8`). *Observed in Excel*: the fraction is the last continued-fraction convergent whose denominator fits the placeholders and whose numerator fits 31 bits, computed on the binary double (so 5.3 gives `5 2/7` and 0.1+0.2 gives `1/3`), not the closest fraction; when not even the first step fits, the number is rounded to a whole (`0.08` with `?/?` is `0/1`). A numerator past 2147483647 cannot be shown: `FormatStatus.NotRepresentable`. A fixed denominator rounds `fraction x denominator` to 15 digits first, then half away (`0.285` over 100 is `29/100`). A zero numerator after a whole part shows blanks for the whole fraction (`2` with `# ?/8` is `2` plus four spaces), unless the numerator has a `0` placeholder (`0 0/0` shows `2 0/1`). Without a whole part the fraction keeps its zero (`0/1`) and no minus sign.
* `General`: up to `GeneralWidth` characters (11; the sign is not counted), at most 15 significant digits. Scientific (`1.23457E+11`) when the integer part is wider than that. *Observed in Excel* (`TEXT` and cells alike): below one, the plain form wins unless the scientific form keeps more of the digits; `0.00001` and `0.000000001` stay plain, `0.000123457` stays plain, `1.2345E-08` and `1.2E-10` go scientific. `GeneralWidth` is a parameter. A caller such as `TEXT` that refuses results over 255 characters does so itself: `1E+300` with `0` is 301 characters, which `TEXT` reports as `#VALUE!`, and a cell shows a row of `#` when the text does not fit its column.
* `MaxWidth`: when the caller gives a width and the text is wider, `TryFormat` returns `DoesNotFit` and a row of `#` of that width (not for General, which fits itself).

## Dates and times

Codes: `y yy yyy yyyy`, `m mm mmm mmmm mmmmm`, `d dd ddd dddd`, `h hh`, `m mm` (minutes), `s ss`, `AM/PM am/pm A/P a/p`, `[h] [m] [s]` (elapsed, `[hh]` pads), `.0 .00 .000` after seconds, `e ee`, `g gg ggg`, `b bb bbbb`. `m`/`mm` are minutes right after an hour code or right before a seconds code (literals between are skipped), otherwise months. AM/PM selects the 12-hour clock. `y` and `yy` are two digits, `yyy` and `yyyy` four. A date or time section that has digit placeholders is the defect `numfmt.mixed-codes`.

* Serials: 1900 system: 0 is "1900-01-00" (a Saturday), 1 is Sunday 1900-01-01, 60 is the fictitious 1900-02-29, 61 is 1900-03-01 and later serials are real days (1899-12-30 plus the serial). 1904 system: 0 is Friday 1904-01-01. The largest serials are 2958465 and 2957003 (9999-12-31). Weekdays follow Excel's count, so serials below 61 differ from the real calendar.
* Times are fractions of a day rounded to the nearest millisecond; for display they are rounded to the precision of the code (whole seconds unless `.0` digits are present), and a time that rounds up to a whole day carries into the next date (23:59:59.9996 shows `00:00:00` the next day).
* A date or time format of a negative number, or one past the last day, is not shown: `FormatStatus.DateOutOfRange`; `Format` returns `#` characters. *Observed in Excel*: a format whose only date or time part is one elapsed field (`[h]`, `[m]`, `[ss]`) is a plain count: no range limit, a minus sign for negative values, rounded to whole seconds first and then cut to the unit (0.99999999 days is `24` hours); `[h]:mm` and the like keep the range.
* *Observed in Excel*: `am/pm` in lower case still shows `AM` and `PM`; only `a/p` shows lower case. The written pairs `上午/下午`, `午前/午後` and `오전/오후` are AM/PM codes that show their own half. `[$-F800]` is `dddd, mmmm d, yyyy` (no padded day).
* `e`, `g`: Gregorian year unless the locale tag or its calendar bits say otherwise: Japanese eras (Meiji from 1868-09-08, Taisho, Showa, Heisei, Reiwa; `g` one letter, `gg` the first kanji, `ggg` the era name), Minguo (a tag whose calendar byte is 4, year-1911, `民國`). *Observed in Excel*: `[$-404]e` alone is the Gregorian year, and the Gregorian calendar has no era name (`g` is empty). `b` is the Buddhist year on its own, but the Gregorian year when a locale tag without the Thai calendar is present (`[$-41E]bbbb` is 1900 for serial 1). Locale tags `[$-xxxxxxxx]`: the low 16 bits are the LCID; the next byte is a Windows calendar ID (3 Japanese, 4 Taiwan, 7 Thai); the byte above that is the numeral system (`0D` is Thai digits). With calendar 7, `yyyy` is the Buddhist year; other calendars (Hijri, Hebrew) fall back to Gregorian.

## Numerals

`[DBNum1]`, `[DBNum2]` and `[DBNum3]` write digits as CJK numerals, but only with a Chinese, Japanese or Korean locale (in the tag, or `FormatOptions.Culture`); without one they change nothing, as in Excel on an English system. *Observed in Excel* (zh-CN, zh-TW, ja-JP, ko-KR):

* The digits of a number are written one by one: 123 is `一二三`, 105 is `一○五` (zero is `○` U+25CB for zh, `〇` U+3007 for ja, `０` for ko), including digits after the point and grouping separators in place (`一,○○○`).
* A year, a padded part (`mm` of 05) and any digit after a point are digit by digit too. An unpadded month, day, hour, minute or second is a place-value numeral: `十二月三十一日`; DBNum2 `壹拾贰月叁拾壹日`.
* DBNum2 digits: zh-CN `零壹贰叁肆伍陆柒捌玖`, zh-TW `零壹貳參肆伍陸柒捌玖`, ja-JP `〇壱弐参四伍六七八九`, ko-KR like zh-TW except that zero is U+F9B2, three U+F96B and four `四`. DBNum3 is full-width digits `０-９` everywhere.
* Strings longer than 256 characters keep plain digits.

## Cultures

`NumberFormatCulture` covers en-US, en-GB, de-DE, fr-FR, es-ES, it-IT, pt-BR, nl-NL, ru-RU, ja-JP, zh-CN, zh-TW, ko-KR, th-TH, hu-HU, da-DK, nb-NO, sv-SE, fi-FI, lt-LT, pl-PL, cs-CZ, tr-TR and dv-MV (the last eleven were added after workbooks in the wild used them; their names, weekday and AM/PM texts are Excel's own, 25 tags and 1,350 cells in `testdata/numfmt/excel-locales.tsv`). Under any `[$-LCID]` tag, `A/P` and `a/p` show the locale's whole AM/PM text (observed in Excel, even for `[$-409]`); untagged they are one letter. It holds the decimal and group separators (French groups with U+202F, Russian with U+00A0), month and day names, AM/PM text, the local currency symbol and the short and long date patterns. The names were compared with Excel's output for every month and weekday (`mmmm`, `mmm`, `dddd`, `ddd`). Where Excel differs from the usual convention, Excel wins: Russian month and short weekday names are capitalised and nominative even next to a day (`15 Март 2023`), Spanish short names end in a full stop (`ene.`, `ma.`), Japanese and Korean `mmm` are the bare number, zh-TW `mmm` is the long name. A `[$-LCID]` tag changes names, AM/PM and calendar for its section; separators always come from `FormatOptions.Culture`. A locale outside the table falls back to its language, then to US English.

Built-in IDs 0 to 49 come from `NumberFormat.GetBuiltInCode`. IDs 5 to 8, 14, 22 and 41 to 44 follow the culture's currency symbol and date pattern; 37 to 40 are written as Excel saves them (`#,##0_);(#,##0)`; the ISO text shows a space where Excel writes `_)`). IDs 27 to 36 and 50 to 58 are defined for zh-TW, zh-CN, ja-JP and ko-KR, and are `null` for other cultures. IDs 59 to 81 (Thai) are not implemented.

## Not verified

The grid did not reach: Korean `DBNum2` digits 6 to 9 (written with the traditional forms, not read back); Hijri and Hebrew calendars (fall back to Gregorian); `General` for widths other than 11; locales outside the table; right-to-left and Thai built-in IDs 59 to 81.

## Categories and `CELL("format")`

`Category`: General, Number, Currency, Accounting (a `*` fill), Date, Time, DateTime, Percentage, Fraction, Scientific, Text, Custom, from the first section that applies to numbers. `GetCellFormatCode()` returns the `CELL` codes `G`, `F0`, `,2`, `C0`, `P2`, `S2`, `D1`…`D9`, with `-` when negative numbers are coloured and `()` for parentheses; codes Excel has no letter for give `G`.

## Defects

Invalid codes are never exceptions of another type. `TryParse` returns a `NumberFormatDefect` (code, message, position in the code); `Parse` throws `OfficeFormatException` with the same code. Codes: see `docs/defects.md` (`numfmt.*`).

## Verification

* `tools/NumFmtGrid` writes the edge-case grid: `grid-text.xlsx` (`=TEXT(value, code)`) and `grid-cells-NN.xlsx` (values in cells formatted with the code, in shards of 12 codes so that a code Excel refuses costs one shard), and our answer for each case in `manifest.tsv`. `tools/office-oracle` (`run --grid`) opens each workbook in desktop Excel and writes the answers back; `NumFmtGrid compare` reports agreement. `tools/NumFmtGrid/run-libreoffice.sh` does the same with LibreOffice headless (information only; its null date, truncated seconds and `?` spacing differ from Excel).
* Agreement with Excel 16.0.20527 over the 7,602 cases: `TEXT()` 7,602 and cell display 7,602, after setting aside what is not the formatter's: `TEXT` turns numeric-looking text into a number, refuses results over 255 characters, and a cell shows `#` rows when the text is too wide and stretches `*` fills. The two codes Excel rejects (`0.00e+00`, `0.00@`) are rejected here.
* `NumberFormatExcelGridTests` replays `testdata/numfmt/excel-grid.tsv` against the formatter.
* Unit tests take their expected strings from Microsoft's articles (cited in the test file) or from the rules above.
* Fuzz targets `numfmt` (parser and formatter on arbitrary codes) and `numfmtfmt` (string, span and exact-size span agree).
* The span path allocates nothing; a test measures it over thousands of calls.
