# Desktop Office results for 1.0

What desktop Word, Excel and PowerPoint did with OneDocument's output, and what OneDocument got wrong or right against them. The
oracle drives the applications through COM from a scheduled task in a signed-in user session (`tools/office-oracle`); only files
we generated were sent. Nothing here is copied from Office: every row is a file, an outcome or a number that came back.

## The run

| | |
|---|---|
| Applications | Word, Excel and PowerPoint 16.0.20527.20112 (Microsoft 365 desktop), Windows 10.0.26300 |
| Mode | an interactive scheduled task in the user's desktop session, one run at a time, ≤ 25 minutes each, with a lock file shared with other oracle users; every run ended with no Office process of ours left |
| Fonts | 260 families installed. Present: Calibri, Cambria, Cambria Math, Times New Roman, Arial, Courier New, Segoe UI, Consolas, Symbol, Wingdings, MS Gothic, SimSun. Absent: Carlito, Caladea, Liberation, Noto (Word substitutes Arial for Liberation Sans). The Aptos family is not installed as files; no input used it except one probe row. Fonts never leave the machine |
| Same-font rule | a layout row gates only when every font Word embedded in its PDF has a metric-compatible partner in ours (Arial = Liberation Sans, Calibri = Carlito, Cambria = Caladea, Times New Roman = Liberation Serif, Courier New = Liberation Mono; `tools/office-oracle/layout-floors.py` reads both PDFs with `pdffonts`). Any other font (CJK, Arabic, Hebrew, Thai, Cambria Math against the open math font, Aptos, Wingdings) makes the row a *font-substitution* row: reported, not gating |
| Not covered | the layout differential ran on the **generated** layout cases (`tools/LayoutProbe gen`: 142 documents, 29 workbooks, 14 decks and the drawing, float and diagram samples), because corpus files never leave the build host. There is no separate "business documents" set of real files on the oracle machine |

Two failure shapes show up in the tables and are told apart by experiment, not by guess: an *input problem* (the file is damaged
or hand-written wrongly: Excel and Word fail on the *source* the same way) and a *defect* (our writer produced it; fixed, or open).

## Verdicts first

### 1. No repair prompt for the writers' outputs

Word and PowerPoint report a file that needs repair as `repair-prompt` (opening it as it is fails, opening with repair succeeds) or
`unreadable`; Excel reports an open failure. Every failure was bisected (part swaps, element removal, schema validation) before it was
called a defect.

| Set (files) | Result |
|---|---|
| edits: tables, sections, comments, notes, fields, authored and resolved revisions, content controls, text boxes, properties, formats (57) | 57 clean |
| authoring and revision grid (104 documents, with accept/reject actions run) | 104 clean |
| template outputs: DOCX 33, XLSX 36, PPTX 23 | 89 clean; 5 are input problems (below) |
| mail merge: our 127 outputs, 12 merges Word ran from our templates | 123 clean, 4 input problems; 12 of 13 Word merges completed (the 13th, a template whose data lacks a field, blocks Word on a modal dialog) |
| content-control bindings (22) | 22 clean |
| writers: DOC, RTF, ODT, HTML → DOCX, DOCX → RTF and ODT (40 Word files) | 40 clean after fix 1 |
| writers: PPT, ODP → PPTX, PPTX → ODP (30 PowerPoint files) | 30 clean after fixes 2 and 3 |
| writers: XLS, XLSB, ODS → XLSX, XLS, XLSB, ODS; written XLS (47 Excel files, rerun after fix 4) | 37 clean; 10 fail: 3 are input problems, 7 are ODS files Excel does not open (information, below). Every written `.xls` (13 converted, 5 rewritten fixtures) opens clean |
| layout cases: DOCX 168, XLSX 29, PPTX 26 | 216 clean; 10 input problems (hand-written probe markup that fails the ECMA-376 schemas or is damaged on purpose) |

Defects found and fixed in this run (each has a test):

1. **ODF packages: Word rejected the `mimetype` entry.** Our entry had "version needed to extract" 0; Word's OpenDocument reader treats
   that as damage (all four ODTs needed repair, LibreOffice's own ODT did not). The entry now says 20 (`OdfPackage`). Bisected by
   rewriting the same bytes with the version field patched in one place.
2. **PPT → PPTX: slide masters carried `subTitle` and `ctrTitle` placeholders.** PowerPoint refuses a master with anything but
   title, body, date, footer and slide number placeholders (`PptShapeWriter`: master placeholders are mapped to those).
3. **ODP → PPTX: adjust values were named `adj1`, `adj2`…** for every preset. A `roundRect` has `adj`, a `star5` has `adj`, `hf`, `vf`;
   PowerPoint refuses a guide the preset does not declare. The names now come from the preset's own definition
   (`tools/DrawingPresets/adjust-names.py` generates `PresetAdjustNames.g.cs`).
4. **XLS notes: Excel refused every BIFF8 workbook we wrote with cell notes. Fixed.** Cause, found by swapping record groups between our file
   and a workbook LibreOffice wrote from the same cells (both are black-box files; the swap tool rewrites the compound file): the
   `MsoDrawing` records carried the right shapes, but the lengths declared by each note's shape container, its group container and the
   drawing container did not count the eight-byte text box marker that follows in the next record. Swapping only the `MsoDrawing` records
   made Excel open the file; the `Obj`, `TxO`, `Note` records and the drawing group were not at fault. The containers now count the marker;
   the note shape also carries the property set other writers use, the drawing group's `spidMax` is the last shape identifier and `Obj`
   starts with flags 0 (`XlsWriter.Drawing`, tested by `HyperlinksAndNotesSurviveAWriteAndRead`).
5. **`xls-features.ods` and the six ODS written from the XLSX templates do not open in Excel; information, not a gate.** The ODF gate is the
   RelaxNG schemas and LibreOffice: all eleven ODS files written in this run validate against ODF 1.3 (Jing, extension namespaces ignored,
   0 failures) and LibreOffice opens them. Excel's OpenDocument reader refuses them (and LibreOffice's own `ods-rich.ods`); they were not bisected.

Input problems (the source fails the same way, or fails the schema), kept here so nobody chases them: the PPTX notes templates
`notes-text` and `slide-per-item-with-notes` (a notes master with no transform on the slide image placeholder shares nothing with the
slide master's theme; their unrendered `template.pptx` fail identically); the three XLSX templates with `dxfId` but no `dxfs`
(`cf-and-validation`, `cf-rule-formula`, `sheet-copy-features`: the unrendered templates fail identically); the mail-merge template
`single-document` (a drawing with `urn:x` content); layout probes `drawing-fills`, `drawing-effects-groups`,
`float-wrap-through-ellipse-no-polygon`, `drawing-slide-shapes`, `slide-pictures-charts-diagrams`, `x12-first-odd-even` (schema
failures in hand-written DrawingML, a connector style without children, header and footer order) and
`126-footnote-reference-to-missing-note` (damaged on purpose); the XLSB probe workbooks whose table covers cells that do not exist
(`xlsb-features`, `xlsb-formulas`; removing `tableParts` makes the converted workbook open); the test report `report.docx` of the HTML
cases (hand-written picture markup); `lo-slide-backgrounds.odp` and `lo-roundtrip.docm`, which LibreOffice wrote and PowerPoint and Word
refuse. Six ODS written from the XLSX templates fail in Excel, and so does LibreOffice's own `ods-rich.ods`; not bisected, so they are not counted as defects.

### 2. Excel grids

Excel entered each formula with `Formula2` (dynamic-array semantics), recalculated fully and returned the entered text, the spill
shape and every value; `FunctionGrid fill`, `FormulaGrid compare` and `LookupGrid compare` evaluate the same text with the engine.
Numbers agree to 15 significant digits; text, booleans and errors exactly (errors by kind). The Excel answers are committed beside the
inputs (`testdata/calc/function-grid/*.expected.tsv`, `function-grids/*.fgrid.tsv`, `lookup-grid/*.fgrid.tsv`) and every disagreement
is listed in the `mismatches.txt` of its directory. **Floors** (rows compared / disagree; they only rise):

| Grid | Compared | Disagree | Agree |
|---|---:|---:|---:|
| function grid: math 4,312 / 158, logical 895 / 14, information 277 / 4, text 3,448 / 180, date and time 2,135 / 118, statistical 11,633 / 1,042 | 22,700 | 1,516 | 93.3 % |
| financial | 1,275 | 197 | 84.5 % |
| engineering | 1,212 | 145 | 88.0 % |
| lookup, reference, dynamic-array and LAMBDA helpers (after the binary-search fixes below; 86.1 % at the first run) | 9,707 | 47 | 99.5 % |
| database (`database.fgrid`, data cells entered by the driver) | 324 | 0 | 100 % |

What the disagreements are (the lists are committed; this is the shape, not an excuse for each row):

- **Lookup.** The first run disagreed on 1,350 rows, 1,302 of them `XMATCH` and `MATCH` on unsorted or mixed-type data. They were settled
  from the committed answers (the rules are in `docs/calc-functions.md`, open item 1) and the grid was compared again without asking Excel
  anything new: `XMATCH` binary modes bisect across types (numbers, text, logical values, errors, an equal probe is kept and the search
  goes on towards the start when ascending); next-smaller and next-larger compare across types and an empty needle sorts above everything;
  legacy approximate lookups step up to a cell of the target's type and take the last of an equal run; `MATCH` type -1 over a short array
  is a linear read (all 1,388 `MATCH` rows with literal arrays agree). The question mark sorts after the asterisk in text order. What
  is left (47 rows) is not a search: `GROUPBY`/`PIVOTBY` shapes (9), `RANDARRAY` (5, volatile), reference-function edge cases (`INDEX`,
  `ROW`, `COLUMN`, `ROWS`, `COLUMNS`, `ADDRESS`, `OFFSET`, `INDIRECT`, `HYPERLINK`, `SUM(B1:.E8)`), `LAMBDA`/`LET`/`MAP` error kinds and
  shape rows of `FILTER`, `SORTBY`, `TAKE`, `EXPAND`, `WRAPROWS`, `MAKEARRAY`. Open: `MATCH` type -1 beyond 64 cells is bisected, as no
  grid case is that long.
  **Fixed here:** `VLOOKUP` and `HLOOKUP` looked at the column index before searching; Excel searches first, so a value that is not found is
  `#N/A` whatever the index (335 rows before the change, now 0).
- **Function grid.** Statistical distribution inverses and discrete distributions on boundary arguments (`NEGBINOM.DIST`, `BINOM.INV`,
  `HYPGEOM.DIST`, `F.INV.RT`, `T.DIST`, `WEIBULL`, `POISSON.DIST`: Excel's iteration limits and accepted ranges), text conversions
  (`TEXT` formats, `NUMBERVALUE`, `TEXTSPLIT`, `TEXTBEFORE`/`TEXTAFTER`), `SERIESSUM`, `TIME`, `EOMONTH`/`EDATE` on edge dates,
  `LINEST`/`LOGEST` shape, `GCD`/`LCM`, `MROUND`, `RANDBETWEEN`/`RANDARRAY` (volatile, no stable value) and 16 service-bound rows
  (`DETECTLANGUAGE` and similar: Excel answered over the network, the engine evaluates offline by design).
- **Financial and engineering.** Of 342 rows, 134 are values that differ (coupon-date family `COUPDAYS`, `COUPNCD`, `PRICE`, `YIELD`, `ODDF*`/`ODDL*`
  on basis and boundary dates; `RATE`, `IRR`, `XIRR` iteration; Bessel functions, complex-number functions `IMCOT`, `IMTAN`, `IMCSC`), the rest
  are error-kind differences on out-of-range arguments (`BIN2DEC`, `DEC2BIN` and the base conversions: Excel's `#NUM!` limits).
  **Database.** The driver now enters a `NAME.cells.tsv` sidecar before the formulas, and the first run disagreed on 38 of 324 rows: a boolean
  field (`TRUE` names the first column), `DCOUNT`/`DCOUNTA` with the field left out (they count the matching records), and a criteria block
  of one row or a single value (`#VALUE!`). Fixed; 0 disagree now.

The harness fix that made the financial and engineering numbers meaningful: `FormulaGrid compare` now knows Excel's error numbers
(`-2146826288 + (code - 2000)`) without error literals in the grid (before it, 1,047 rows showed as "unknown code").

### 3. Layout differential (page count, line breaks, geometry)

Both sides exported PDFs; LayoutProbe's text-line comparison and `tools/office-oracle/layout-geometry.py` (every word of Word's PDF paired
with the nearest word of ours with the same text; it agrees when left edge and baseline are within 2 pt) read them. **Page counts agree
on all 196 documents and workbooks and on all 24 decks.** Floors for the rows where both sides used the same fonts:

| Set | Rows | Page count | Text-line agreement (mean) | Word geometry within 2 pt (mean) | Rows ≥ 0.99 |
|---|---:|---:|---:|---:|---:|
| business (paragraphs, tables, sections, fields, notes) | 76 | 76 / 76 | 0.951 | 0.872 | 50 |
| long (3 or more pages) | 15 | 15 / 15 | 0.966 | 0.941 | 13 |
| edge (floats, drawings, diagrams, math) | 29 | 29 / 29 | 0.834 | 0.588 | 10 |
| sheets | 26 | 26 / 26 | 0.934 | 0.083 | 0 |
| CJK and right-to-left | 1 | 1 / 1 | 1.000 | 0.000 | 0 |
| font-substitution rows (not gating): CJK/RTL 8, edge 38, business 1, sheets 2 | 49 | 49 / 49 | 0.42 – 1.0 | 0.10 – 1.0 | |
| decks (24 PowerPoint exports) | 24 | 24 / 24 | 0.748 | not run | |

Fixed from this differential (each observed in Word, each with a test):

- **Paragraph spacing is the larger of space after and space before, not their sum**, in every compatibility mode (six pairs measured; was additive).
- **A multiple of line spacing above 1 adds its room below the text**: the first line of 1.5 spacing sits where single spacing puts it (was above).
- **Space before is kept at the top of the first page of a section that starts a page** (it was dropped, as after a natural break).
- **A decimal tab aligns on the point, not on a group comma** (`1,234.56`).
- **Table cell margin is 0 when the document has no default table style** (the 108 twips come from "Normal Table"); the layout test documents now carry that style as Word documents do.
- **A footnote position in the document settings is not applied**; only a section's own is (Word left the note at the page bottom).

Differences that remain, explained, not fixed:

- Sheets: Word's PDF of a sheet puts rows 14.52 pt apart where our default row is 15 pt (the default row height follows the display scaling of the machine),
  so geometry drifts down the page while line breaks and page counts agree. Geometry is information for sheets.
- Fields: Word exports the stored results of a table of contents and index; layout updates fields by default, so `100`–`102`, `111` differ until
  `LayoutOptions.UpdateFields` is off.
- Tab leaders are drawn as shapes in our PDF and as dot characters in Word's (`07-leaders` line text); a right tab with a second tab past the
  margin keeps the text on the margin line in Word and wraps it in ours (`06-tabs`).
- Tables with their own cell margin or cell spacing start their first text up to 3.5 pt away from Word's (`tbl-06`, `tbl-04`, `tbl-14`, `tbl-18`);
  with a default table style Word pulls the table out by the style margin, with a direct margin and no style it does not; our rule uses
  the table's effective margin. Open.
- Vertical centring of a page (`85`), the document grid (`87`), heading line height at a page top, diagram placement, equation display and
  floating wraps (edge set) are within a few points but not at the 2 pt tolerance; they are the edge-set floor.
- Superscript note numbers split extracted lines in the text comparison (`106`, `117`, `110`): their word geometry agrees to 0.1 pt, so the low
  text-line value there is an extraction effect.

## The other requests

| Request | What ran | Result |
|---|---|---|
| `testdata/authoring` | the 104 revision and authoring documents (all clean); the three sample files by hash and their sanitised copies were not rebuilt | partial |
| `testdata/operations` | **Fields:** Word updated every field and table (`Fields.Update`, `TablesOfContents.Update` and the other tables, in every story) in cases 100–108 and 111–112 (13 documents, all clean) and the result was compared per paragraph with ours: 13 of 24 text parts identical before two fixes, 17 of 24 after (below). **Merge:** Word's Insert Text from File (`Range.InsertFile`) on `merge-a` + `merge-b`, compared with ours (below). Our fields and merge outputs open clean (13 + 2). **Not run:** split (Word has none), sheet Move or Copy and slide paste with a theme choice (`Worksheet.Copy` and `Slides.InsertFromFile` do not offer the conflict dialogs the request asks about) | partial |
| `testdata/xls` (legacy cases) | the five legacy workbooks open clean in Excel; the saved `.xlsx` copies were not compared cell by cell | opens only |
| `testdata/xls` written | `basic`, `formulas`, `generated`, `styles` (rewritten) open clean; `features-rewritten` fails (defect 4) | partial |
| `testdata/ppt` | all 18 PowerPoint binary files open clean; PDFs exported; conversion comparison not run | opens only |
| `testdata/odp` | 17 of 18 LibreOffice-written ODP open clean; ours: `pptx → odp` 14 of 14 clean after defect 1 | opens only |
| `testdata/ods` | ours open clean for XLS and XLSX sources; defect 5 and six template ODS fail | partial |
| `testdata/ole`, `testdata/vba` | the OLE property fixtures and the macro workbooks open clean; `lo-roundtrip.docm` is refused; the results list 18 of the 19 files, one macro deck is unreported | opens only |
| `testdata/doc`, `testdata/html`, `testdata/odf` | DOC, ODT and HTML-derived DOCX open clean; Word's own conversions were not compared | opens only |
| `testdata/encryption` | the driver passes a password to `Workbooks.Open`, `Documents.Open` and, as `file::password::`, `Presentations.Open`. **Opened clean with the password, no prompt, no repair:** our RC4 CryptoAPI writer's output (`written-secret.xls`), the two test-encryptor workbooks (`cryptoapi-plain-basic.xls`, `cryptoapi-features.xls`), LibreOffice's RC4 `secret-basic.xls` and `secret-basic.doc`, its agile `secret-basic.docx`, `secret-basic.xlsx`, `secret-runs.pptx`, and EPPlus's `secret-epplus.xlsx` (9 of 10 files). **Does not open:** `xor-v0-basic.xls` (XOR obfuscation, method 1). Six re-encryptions of it with different array-index rules (stream offset of the first data byte plus the length, the offset alone, the record offset, the offset plus length without the header, the length, zero) all fail the same way, so the cause is not the index rule alone; it is not settled whether Excel declines to open XOR-obfuscated BIFF8 or our key or verifier differs. XOR is read-only in the library and no Office-written XOR file was available. Only the XLS RC4 encryption is written, so there is no other written kind to open | 9 of 10 clean; XOR open |
| `testdata/inspect` | Word's, Excel's and PowerPoint's `DocumentInspectors` collections were run on the committed inputs and on our sanitised outputs (below) | partial |
| `testdata/redaction` | the redacted outputs are produced by the test suite, not by a command; nothing was opened | not run |
| `testdata/accessibility` | the Accessibility Checker has no object model (no COM call runs it or returns its findings), so its verdicts need the user interface | not run |
| mail merge | Word ran 12 merges from our templates (one document for all records); the merged text was not compared with ours | partial |

## Round two: notes, lookups, database, encryption, inspectors, fields

Run on the same machine and build (Excel, Word and PowerPoint 16.0.20527.20112, 260 families; Aptos not installed as files and not used by any
input here), one run at a time with the shared lock taken and released, no Office process left; every file was generated by us (the
encryption fixtures and the inspect fixtures are the committed ones).

### Inspectors (`--inspect`)

The object model exposes only the inspectors an add-in can implement: Word five (Collapsed Headings, Custom XML Data, Headers/Footers/Watermarks,
Invisible Content, Hidden Text), Excel five (Custom XML Data, Headers and Footers, Hidden Rows and Columns, Hidden Worksheets, Invisible
Content), PowerPoint four (Custom XML Data, Invisible On-Slide Content, Off-Slide Content, Presentation Notes). Comments, revisions, document
properties and personal information, embedded documents, macros and links are not in the collection, so their agreement cannot be asked.

| Input | Result |
|---|---|
| `torture.docx` (Word opens it only after repair: the fixture carries planted parts) | Collapsed Headings, Custom XML, Headers/Footers and Hidden Text find something, as ours do; Invisible Content finds nothing where we list one hidden picture |
| `torture.xlsx` | Excel does not open it (input problem: the fixture is synthetic) |
| `torture.pptx`, and `sanitized/*.pptx` | PowerPoint reports the file unreadable: the fixture has no slide master, layout or theme, so it is not a presentation PowerPoint can open (input problem). The pptx rows below use real decks instead |
| `sanitized/default.docx`, `all.docx`, `default.xlsx`, `all.xlsx` | open clean; every inspector finds nothing, except Hidden Worksheets on `default.xlsx` (1): the default sanitiser removes only hidden sheets that nothing else refers to (`docs/sanitize.md`) and the fixture has a name and print areas on them |
| 19 valid documents (10 DOCX, 3 XLSX, 6 PPTX from our edits, templates and authoring cases) sanitised with the default and with `--all` (38 outputs) | 36 open clean; the two failures are the outputs for `xl-tpl-cf-and-validation.xlsx`, whose input fails the same way. Every inspector finds nothing in any output; one input (`sect-headers.docx`) has headers, and its outputs have none |

### Fields and merge (`update-fields`, `insert-file`)

Word updated all fields and tables in the 13 field cases; ours are `Document.UpdateFields`. Per text part (body, footnotes, first header and
footer): 24 compared. First comparison 13 identical; **two fixes** (each now has a test):

1. A table of contents over numbered headings keeps the separator the list level declares after the number (`w:suff`): Word writes `1. Introduction`
   for a level that ends with a space, we wrote a tab.
2. `REF \r` in a paragraph that has no number is relative to the last numbered paragraph before it (Word answers `i` after item `1.a.i`); we gave the full number.

After the fixes 17 of 24 parts are identical (case 101 and 103 now agree). The remaining differences, none a defect of the update itself:
- **Footers:** `Page 1 of 1` in Word's saved copy against our `Page 1 of N`: Word's COM `Fields.Update` on a footer story stores the value for the
  story's own context, while the page count it reports is `N` (`ComputeStatistics`; 3 for case 100, as ours). Not comparable by this route.
- **Case 100:** the entry `Gamma` is on page 3 in Word and 2 in ours (layout difference; the page counts agree).
- **STYLEREF in headers** (cases 105, 112): Word `Alpha`/`Smith`, ours leave the cached `x` (the update reports them as skipped).
- **Case 108:** the table of authorities field is `TOA \h \c 0`; Word answers `Error! Category number not found.`, ours lists every category (our reading of `\c 0`; open).
- **Case 112:** ours keeps a `(continued)` footnote paragraph that Word's saved file does not contain.

Merge (Word's Insert Text from File at the end of `merge-a` against `Document.Append` with the default and with the kept-both options):
- the second heading and quotation use the destination's `Heading1` and `Quote` (28 half-points stays): same as our default;
- the second list is a **new list** in Word (a second abstract numbering, so it restarts at 1); our default continues the first list
  (`NumberingContinuation.Continue`), `Restart` gives a new list. A difference of defaults, not an error;
- the second bookmark `intro` is **dropped** by Word (the later `REF intro` then points at the first); we rename it `intro_2` and retarget the reference;
- footnotes, comment authors (Ann, Bob) and the revision authors agree; comment ids differ (Word 1, 3; ours 0, 1);
- Word joined the first inserted paragraph to the last paragraph of `merge-a` (the cursor sat at the end of it), so the paragraph count differs by one; no section break in either;
- our two merge outputs open clean.

## Tooling changes

`tools/office-oracle` now installs as `OneDocumentOracle` under `<profile>\onedocument` (the earlier task and directory were removed);
the driver picks the application for an `.xml` Flat OPC file from its processing instruction and opens `.odt`, `.ods`, `.odp`, `.html`.
`layout-geometry.py` and `layout-floors.py` are new. A file that crashes Excel leaves a recovery instance (`EXCEL.EXE /restore`) that
later runs attach to and fail on; the batch scripts used one run per risky file and stopped that instance by its process id.

Round two added: `--inspect` (runs the application's `DocumentInspectors` and records name, status and result text), a `<file>.password`
sidecar (passed to the open call of all three applications), a `<name>.cells.tsv` sidecar for formula grids (the cells the formulas read),
and the Word actions `update-fields` (every field of every story, then every table and index) and `insert-file:NAME` (Insert Text from File).
The tool of the rewritten notes bisection is not kept: it rewrote the compound file around a swapped group of records, using a workbook
LibreOffice wrote from the same cells as the reference.
