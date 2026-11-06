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
| writers: XLS, XLSB, ODS → XLSX, XLS, XLSB, ODS; written XLS (47 Excel files) | 34 clean; 13 fail: 3 are **open defect 4**, 1 is **open defect 5**, 3 are input problems, 6 are ODS written from the XLSX templates (below) |
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
4. **Open: XLS notes.** Every BIFF8 workbook we write with cell notes fails to open in Excel (immediate open error; before the first change a hang then a crash).
   One-feature workbooks isolate it: comments are the only feature that fails (autofilter, validation, five kinds of conditional format,
   hyperlinks, outline, protection, print setup, merges and names are clean). The text-box marker of each note was moved out of the shape
   container into its own `MsoDrawing` record and the shape gained the text properties LibreOffice writes; Excel still refuses. A workbook
   written by LibreOffice with notes opens, so the difference is in the note drawing (the `Opt` property set, the `Obj` flags, the
   `TxO` flags or the drawing group); the next step is to copy LibreOffice's note records byte by byte and remove differences one at a time.
   `docs/xls.md` lists XLS notes as written; until this is closed they are written but Excel does not open the result.
5. **Open: `xls-features.ods`.** The ODS written from the legacy features workbook (notes, validation, conditional formats, filters) fails to open
   in Excel; one-feature ODS files (notes included) and the other converted ODS files open clean. Not bisected.

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
| lookup, reference, dynamic-array and LAMBDA helpers | 9,707 | 1,350 | 86.1 % |

What the disagreements are (the lists are committed; this is the shape, not an excuse for each row):

- **Lookup.** 1,221 are `XMATCH` and 81 are `MATCH` on unsorted or mixed-type data in the binary-search modes (737 of the `XMATCH` rows use mode
  2 or -2): Excel returns whatever its binary search lands on, we return `#N/A`. This is the open item 1 of `docs/calc-functions.md`.
  The rest are single rows in `GROUPBY`/`PIVOTBY` shapes, `RANDARRAY` (volatile) and `LAMBDA` helpers.
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
  The database grid (`database.fgrid`) was not run: it needs its data cells entered first, which the oracle does not do.

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
| `testdata/operations` | merge, split, field-update and sheet and slide copy comparisons need Word's UI actions (Insert Text from File, Move or Copy, paste with keep-source-formatting); none is a COM call the oracle drives | not run |
| `testdata/xls` (legacy cases) | the five legacy workbooks open clean in Excel; the saved `.xlsx` copies were not compared cell by cell | opens only |
| `testdata/xls` written | `basic`, `formulas`, `generated`, `styles` (rewritten) open clean; `features-rewritten` fails (defect 4) | partial |
| `testdata/ppt` | all 18 PowerPoint binary files open clean; PDFs exported; conversion comparison not run | opens only |
| `testdata/odp` | 17 of 18 LibreOffice-written ODP open clean; ours: `pptx → odp` 14 of 14 clean after defect 1 | opens only |
| `testdata/ods` | ours open clean for XLS and XLSX sources; defect 5 and six template ODS fail | partial |
| `testdata/ole`, `testdata/vba` | the OLE property fixtures and the macro workbooks open clean; `lo-roundtrip.docm` is refused; the results list 18 of the 19 files, one macro deck is unreported | opens only |
| `testdata/doc`, `testdata/html`, `testdata/odf` | DOC, ODT and HTML-derived DOCX open clean; Word's own conversions were not compared | opens only |
| `testdata/encryption` | the oracle cannot supply a password to an open call | not run |
| `testdata/redaction`, `testdata/inspect` | the redacted outputs are produced by the test suite, not by a command; the Document Inspector has no COM interface | not run |
| `testdata/accessibility` | the Accessibility Checker has no object model, so its verdicts need the user interface | not run |
| mail merge | Word ran 12 merges from our templates (one document for all records); the merged text was not compared with ours | partial |

## Tooling changes

`tools/office-oracle` now installs as `OneDocumentOracle` under `<profile>\onedocument` (the earlier task and directory were removed);
the driver picks the application for an `.xml` Flat OPC file from its processing instruction and opens `.odt`, `.ods`, `.odp`, `.html`.
`layout-geometry.py` and `layout-floors.py` are new. A file that crashes Excel leaves a recovery instance (`EXCEL.EXE /restore`) that
later runs attach to and fail on; the batch scripts used one run per risky file and stopped that instance by its process id.
