# The 1.0 corpus gate

What was run over the public corpus before 1.0, with the numbers and an explanation for every difference. The runs are one pass each,
driven by `tools/gate/gate.sh` (stages), the probes under `tools/`, and the pinned oracles (LibreOffice 26.8.2.2, Jing 20241231 with the
ODF 1.3 RelaxNG schema, lxml/libxml2 for XSD, Apache Tika 3.3.3, veraPDF 1.30.3), on the build worker. Corpus files stay on the worker; only
counts are written here. The fixes the gate found are in the last section. Job ids are the worker's remote-run ids.

**Corpus** (all fetched by `tools/corpus` and `tools/setup`, none committed): Common Crawl Office samples (2,000 DOCX, 365 XLSX), public bug-tracker
attachments (800 DOCX, 702 XLSX, 300 PPTX, 18 XLSB), LibreOffice-converted sets made from govdocs1 (453 XLSX, 237 PPTX), govdocs1 itself (810 DOC, 453 XLS,
680 PPT, 23 RTF), generated documents (25) and the 1M × 20 stress workbook. 4,900 OOXML/XLSB packages in all. There is no public ODF/RTF corpus in the
set: ODF files are ours (written from the corpus) and LibreOffice's (converted from it), see the ODF section.

## 1. Round trip

Untouched = open and save with no edit. The comparison is per part by SHA-256 against the input, read by an independent zip reader (the BCL's), and
for the hub sweeps also `RoundTrip` (raw entries preserved = stored bytes, method and CRC unchanged).

| Layer / set | Files | Result | Parts compared | Identical |
|---|---|---|---|---|
| Package layer (`PackageSweep opc`), all OOXML/XLSB sets | 4,900 tried | 4,894 identical; 6 rejected with a typed defect (5 × `packaging.missing-content-types`, 1 × `packaging.damaged`); 0 untyped failures | 111,239 | 111,239 (100%) |
| – of which recovered from damage (truncated downloads, rewritten from the local headers) | 44 | included in the identical count, defects reported | | |
| DOCX hub (`Document.Open` → `Save`) | 2,810 | 2,773 saved; 37 typed `format.unrecognised` (36 truncated downloads without a central directory, 1 not a package) | 56,316 | 56,316; raw entries preserved 56,316 of 56,316 |
| XLSX hub (`XlsxProbe sweep`) | 1,520 | 1,508 ok; 12 typed (11 truncated `format.unrecognised`, 1 `packaging.missing-content-types`) | 39,328 | 39,328 |
| PPTX hub (`PptxProbe sweep`) | 537 | 537 ok | 43,986 | 43,986 |
| XLSB (`PackageSweep`, `XlsbProbe sweep`) | 18 | 18 identical | 334 | 334 |

Edits (the hub changes only the part it edits; unknown markup stays because the part is rewritten from the tree):

| Edit | Files | Result |
|---|---|---|
| DOCX: one inserted paragraph | 2,668 edited (Common Crawl 1,935, bug tracker 723, generated 10; the others are among the 37 above, had no paragraph to edit, or raised 1 typed `xml.duplicate-attribute` on a main part) | exactly one part changed in every one (the main part; in 3 files it is named `document2.xml`); all other parts identical, raw entries preserved |
| XLSX: one cell set, and every worksheet rewritten from the cell store | 1,508 | the single-cell edit changed exactly one part (the worksheet) in all 1,508; every sheet rewritten from the store, saved, reopened and compared cell by cell: 103.8M cells, 0 mismatches |
| PPTX: text, cell, add, duplicate, delete, move (`PptxProbe`) | 537 decks, 2,193 edits (add 537, text 452, dup 537, delete 302, move 302, cell 63) | only the slide, `presentation.xml`, its rels and `[Content_Types].xml` changed among existing parts (0 other); added and removed parts are the slide's own |

Differences explained: the 37 DOCX and 11 XLSX `format.unrecognised` are truncated downloads (36 of 37 DOCX stop at the 5 MiB crawl cap and have no end-of-central-directory record).
`Document.Open` names a format from the package's directory and rejects them; `OpcPackage.Open` reads 33 of the 36 by scanning local headers. Whether `Open` should do the same is
a design question, listed in the open items. Jobs: package layer `princessDonut.hg8w12ad`, DOCX `princessDonut.8p25lr6h`, XLSX/PPTX `princessDonut.qf11890d`, XLSB `princessDonut.qe8wdagk`.

## 2. Write validity

Our output against the pinned schemas (ECMA-376 5th ed. Transitional and OPC, plus the Microsoft extension schemas under `schemas/ms`, markup compatibility applied first, `xml:space`
on text elements not counted because Office writes it too), compared with the same check on the input: a part is *newly invalid* if it is invalid in the output and valid (or absent) in the input.

| Output | Sample | Parts checked | Invalid in output | Inherited from the input | Newly invalid |
|---|---|---|---|---|---|
| DOCX with one paragraph added (`paired_validate.py`) | 275 pairs | 4,568 | 1,018 in 207 files | 1,018 | 0 |
| XLSX with every worksheet rewritten from the cell store (`xlsx_edit_checks.py`) | 360 pairs | all parts | 1,191 | 1,191 | 0 valid → invalid; the error count rose in 4 already-invalid worksheets (below) |
| PPTX after each edit, existing parts | 515 decks, 2,101 outputs | all parts | | | 0; added parts invalid beyond the inherited errors: 0 |
| LibreOffice opens the edit outputs | XLSX 180 (CSV of every sheet equal to the original's in 180), PPTX 121 (all convert) | | | | |

The error count rose in 4 worksheets that were already invalid (49 → 50 twice and 9 → 50 in Common Crawl files, 1 → 2 in a bug-tracker file); the validator reports at most 50 errors per part, so
the 9 → 50 one may be a cap effect. The error text was not traced to a writer rule in this pass (open item).

Legacy → OOXML (the conversions the hub does on open), 100 files per format sampled by name order, schema-validated and opened by LibreOffice:

| Source → output | Converted | Typed | Untyped | Valid / opened by LibreOffice | Files with a loss report |
|---|---|---|---|---|---|
| DOC → DOCX (810 DOC, sample) | 90 | 0 | 0 | 90 / 90 | 62 of 90 |
| XLS → XLSX (453 XLS, sample) | 90 | 1 (`format.unrecognised`) | 0 | 90 / 90 | 52 of 90 |
| PPT → PPTX (680 PPT, sample) | 98 | 0 | 0 | 98 / 98 | 96 of 98 |
| RTF → DOCX (all 23) | 23 | 0 | 0 | 23 / 23 | 7 of 23 |

Loss codes by count of files (items): DOC `loss.doc-properties` 43 (45), `loss.doc-sprms` 24 (4,205), `loss.doc-drawing-shapes` 32 (289), `loss.doc-text-boxes` 23 (662), `loss.doc-header-text-boxes` 13 (13),
`loss.doc-ole-objects` 8 (22), `loss.doc-form-fields` 1 (126). XLS `loss.rich-text` 35 (784), `loss.drawing-shapes` 13 (156), `loss.charts` 11 (73), `loss.document-properties` 11 (11), `loss.vba` 11 (11),
`loss.external-links` 4 (7), `loss.defined-names` 2 (3), and one each of `loss.autofilter-criteria`, `loss.form-controls`, `loss.macro-names`, `loss.pictures`, `loss.pivot-tables`. PPT `ppt.animations-dropped` 76 (9,880),
`ppt.document-properties-not-carried` 91 (118), `ppt.shape-adjust-dropped` 22 (228), `ppt.ole-object-flattened` 18 (43), `ppt.shape-approximated` 15 (65), `ppt.table-borders-approximated` 13 (37), `ppt.fill-approximated` 4 (10),
`ppt.picture-pict` 4 (35), `ppt.wordart-flattened` 3 (3), `ppt.table-as-group` 2 (5), `ppt.path-escape` 1 (6), `ppt.transition-dropped` 1 (13). RTF `loss.rtf-text-box` 5 (36), `loss.rtf-shape` 5 (52), `loss.rtf-frames` 2 (11).
Every conversion that lost something carried a report; a conversion with an empty report was lossless (DOC 28, XLS 38, PPT 2, RTF 16).
Job `princessDonut.glkafoz7` (after the TotalTime fix below; the first pass found 1 invalid PPT-derived `app.xml`).

OOXML → other formats we write, sample of 60 per format (Common Crawl, bug tracker), loss reports counted, LibreOffice opening the output:

| Output | Saved | Typed | Untyped | LibreOffice opens | Notes |
|---|---|---|---|---|---|
| DOCX → RTF | 59 | 0 | 0 | 59 / 59 | all 59 carry a report (`loss.rtf-shape-position` 304 items, `loss.rtf-table-style` 99, `loss.rtf-run-effect` 87 …) |
| DOCX → HTML | 59 | 0 | 0 | 59 / 59 | 35 with reports |
| PPTX → HTML | 50 | 0 | 0 | 50 / 50 | 50 with reports (`loss.html-slide-layout`, `loss.html-pictures`, `loss.html-animations`) |
| DOCX → Markdown, XLSX → CSV | 59, 51 | 0, 2 | 0 | n/a | reports present where content was dropped |
| XLSX → XLS (BIFF8) | 51 + 59 (two sets) | 2 (truncated inputs) | 0 after the fix below (2 before) | 51 / 51, 59 / 59 | reports on formulas, defined names, charts, pictures |
| XLSX → XLSB | 51 | 2 (truncated inputs) | 0 | 49 / 51; 2 LibreOffice timeouts at 120 s | lossless |

Jobs `princessDonut.7jok1n7c`, `princessDonut.f4su074s`.

## 3. OpenDocument

No ODF corpus is public; the sets are ours (written from the corpus by the exporters) and LibreOffice's (converted from the same files). Jing validates every XML part and the
manifest against the ODF 1.3 RelaxNG schema; foreign-namespace attributes and elements (LibreOffice extensions) are dropped before checking because the OASIS schema allows none.

| Set | Files | Parts | Jing-valid files | LibreOffice opens | Entry round trip | One-entry edit |
|---|---|---|---|---|---|---|
| Our ODT from 59 DOCX | 59 | 236 | 56 (3 fail, see below) | 59 / 59 | 340 / 340 entries identical | 59 / 59 leave the other entries identical |
| Our ODS from 53 XLSX sampled (51 written, 2 truncated inputs typed) | 51 | 255 | 50 (1 fails) | 51 / 51 | 306 / 306 | 51 / 51 |
| Our ODP from 50 PPTX | 50 | 200 | 50 | 50 / 50 | 464 / 464 | 50 / 50 |
| LibreOffice's ODT / ODS / ODP (59 / 49 / 38) → read by us → OOXML (59 / 48 + 1 typed / 38) | 59 / 49 / 38 | | | | 642 / 642, 699 / 699, 389 / 389 | 59, 49, 38 of 59, 49, 38 |
| – the OOXML we write from them | 59 DOCX, 48 XLSX, 38 PPTX | | schema-valid: 59, 48, 38 | LibreOffice opens 59, 48, 38 | | |

The entry round trip is entry content (the package's zip container is rewritten; entries are copied raw). The three ODT failures: `text:soft-hyphen` as the only child of a `text:span`
(6 elements), a `table:table-row` without all its cells (3), `style:header-first` out of order (1). The ODS failure: `number:text`/`number:number` order inside a number style (4 elements). Open items.
Before the fixes below the same run failed 5 ODT, 10 ODS and 8 ODP files. Jobs `princessDonut.c70lg8py` (after), `princessDonut.4kmf5dhy` (before), `princessDonut.58k8vaks` (read side).

## 4. Text extraction against Apache Tika 3.3.3

150 files per format (every k-th by name), our text (`Office.ExtractText`) against Tika's, as the Dice coefficient of character 5-grams ("sim", white space ignored). "precision" is the share of our
word tokens that Tika also has; "recall (no digits)" is the share of Tika's tokens without digits that we have (workbooks leave numbers out of the text by design, `docs/text-extraction.md`).

| Format | Files | sim mean / median / p5 | precision mean | recall (no digits) mean / median | Files with sim < 0.8 |
|---|---|---|---|---|---|
| DOCX (Common Crawl) | 136 | 0.949 / 0.990 / 0.831 | 1.000 | 0.962 / 0.998 | 5 |
| DOCX (bug tracker) | 134 | 0.843 / 0.996 / 0.000 | 0.991 | 0.878 / 1.000 | 26 |
| DOC | 135 | 0.982 / 0.995 / 0.938 | 0.997 | 0.986 / 0.998 | 2 |
| RTF (all 23) | 23 | 0.991 / 0.997 / 0.961 | 1.000 | 0.997 / 1.000 | 0 |
| XLSX | 120 | 0.751 / 0.839 / 0.189 | 1.000 | 0.898 / 0.988 | 50 |
| XLS | 114 | 0.792 / 0.895 / 0.208 | 0.999 | 0.941 / 0.996 | 39 |
| XLSB | 18 | 0.565 / 0.735 / 0.000 | 1.000 | 0.820 / 0.977 | 12 |
| PPTX | 100 | 0.599 / 0.818 / 0.000 | 0.992 | 0.606 / 0.829 | 50 |
| PPT | 135 | 0.861 / 0.952 / 0.177 | 0.985 | 0.883 / 0.971 | 23 |

Nothing of ours is invented (precision ≥ 0.985 everywhere). The differences are what Tika emits and we do not, by policy:
- numbers, booleans and error values of workbook cells (`#REF!`, `TRUE`), and hyperlink targets (`mailto:`): XLSX, XLS, XLSB;
- the file names of embedded pictures and objects (`image1.png`, `image2.svg`, `oleObject1.bin`): 22 of 100 PPTX and 8 of 134 bug-tracker DOCX are exactly the files where Tika lists only such names and we have no text;
- master and layout text of presentations (footer placeholders such as a copyright line, the title-layout "PowerPoint Presentation") and the slide-number field `‹#›`;
- text inside embedded OLE objects (a bug-tracker DOCX carries 105,027 characters of Lorem ipsum in embedded documents) and chart parts of DOCX: Tika recurses, we extract the document's own text.
The weakest DOCX, XLSX and PPT rows are each a handful of files of this kind. No extraction raised an untyped error (typed errors: 2 truncated XLSX, 1 PPT). Job `princessDonut.q3rhicta`.

## 5. Calc: cached values reproduced

Every formula cell is remembered, the workbook recalculated in full, and the result compared with the cached value (numbers at 15 significant digits, text, booleans and errors exactly). Cells with
no cached value, cells whose formula spells `#REF!` and results in it, and cells whose formula calls a function the registry lacks or another workbook are counted but not testable. The floor is
matches / testable. Mismatch classes: *precision* = equal to 9 digits but not to 15; *volatile* = `NOW`/`TODAY`/`RAND*` itself; *volatile-downstream* = in a workbook that calls them; *import-gap-ref* = a legacy
reference into another workbook that the converter leaves as `#REF!` (documented in `docs/xls.md`), and what depends on it; *bug* = the rest, grouped by workbook below.

| Set | Workbooks | Formulas | Testable | Match | Floor | Mismatches by class |
|---|---|---|---|---|---|---|
| Common Crawl XLSX | 354 (11 unreadable truncated) | 713,769 | 701,782 | 698,401 | **99.52%** | bug 1,595 (10 workbooks), volatile-downstream 1,585, external 3,196, precision 165, volatile 31, unimplemented 21, downstream-or-bug 5 |
| Bug-tracker XLSX | BZ_FILES | BZ_FORMULAS | BZ_TESTABLE | BZ_MATCH | **BZ_FLOOR** | BZ_CLASSES |
| LibreOffice-converted XLSX (from XLS) | 453 | 2,238,301 | 2,228,078 | 2,089,807 | **93.79%** | bug 79,957 (17 workbooks; 79,428 are the external-reference chains of 4 workbooks), precision 58,298, external 3,356, import-gap-ref 8, volatile 4 |
| govdocs1 XLS (BIFF) | 450 (3 unreadable) | 2,234,318 | 2,229,877 | 2,121,468 | **95.14%** | import-gap-ref 98,758 (5 workbooks), bug 3,005 (8), volatile-downstream 3,136, precision 2,195, downstream-or-bug 1,310, unimplemented 1,205 (XLM macro functions), volatile 5 |
| Bug-tracker XLSB | 18 | 83,846 | 82,347 | 81,971 | **99.54%** | import-gap-ref 222, precision 123, volatile-downstream 28, volatile 3 |
| ODS (LibreOffice-converted from XLSX) | 48 (1 unreadable) | 25,346 | 10,752 | 7,021 | **65.30%** | bug 3,730 (4 workbooks: 3,704 are one workbook whose formulas contain `#REF!` and depend on it, cached `#NAME?` by LibreOffice, ours `#REF!`), unimplemented 3, volatile 1 |

What the *bug* column is made of, after the fixes below (the biggest first, from `tools/gate/calc_causes.py`):
- **References into other workbooks in legacy files** (LibreOffice and XLS sets, ~79,000 and ~98,000 cells in 4–5 workbooks): the BIFF/LibreOffice conversion cannot express the reference, so the formula becomes `#REF!` and everything depending on it follows. The cached values are kept until recalculation. This is the documented limit (`docs/xls.md`, external workbook references); it is a conversion gap, not a calc-engine result.
- **`SUBTOTAL(9, …)`** over a range (cached 120,401, ours 264,410 in one Common Crawl workbook): probably rows hidden by an autofilter, which the engine does not know; not traced.
- **Numerics**: `NORMINV`/`NORMSINV`/`NORMDIST` differ from the cache in the 8th digit in files from the XLS era (577 + 134 cells), consistent with the older approximation Excel used then (not confirmed against an old Excel). `precision` cells (≤ 1e-9 relative) are cancellation noise in subtraction chains.
- Workbook-specific causes of a few dozen to a few hundred cells each, not traced: `SUMIF` over whole columns (63), `OFFSET` over a named range (392), `COUNTIF(range, range>100)` (7), `UPPER(range)` in a legacy array (32).
- **`GETPIVOTDATA`** (13 cells in one ODS) answered `#REF!` where the cached pivot has the value.
- Cells whose formula is literally `#REF!` and whose result is an error are not compared (`error-literal-formula`: 2,457 / 4,543 / 3,018 / 1,854 / 1,499 cells in the Common Crawl, LibreOffice-converted, XLS, ODS and XLSB sets): another implementation's cached error (`#NAME?`) for a formula that spells `#REF!` says nothing about the engine.

Recalculation was run with one thread. One workbook (a 114 KB bug-tracker file, SUMIFS and VLOOKUP over mismatched ranges, 11,876 formulas) needs about 31 GB of resident memory (it fails under a 16 GB heap limit); it is skipped by name
in the run above (`--skip`) and listed as an open item. Jobs: `princessDonut.ihenmbih` (others), `princessDonut.bthshk19` (bug-tracker XLSX).

## 6. Rendering: veraPDF 1.30.3

Tagged PDF written by `LayoutProbe render` (the same code path as `Office.Convert`), a deterministic sample per kind, each in the profile asked for; veraPDF is run with the matching flavour.
When the profile cannot be met the writer raises `render.failed` with the violated rules instead of writing a file, so a PDF that exists is the one veraPDF sees.

| Kind, profile | Sampled | PDF written | veraPDF compliant | Refused by our own check |
|---|---|---|---|---|
| DOCX, PDF/UA-1 | 25 | 11 | **11 / 11** | 13 (10 × `7.5` a table without header cells, 3 × `7.3` a figure without alternate text) and 1 not a package |
| XLSX, PDF/UA-1 | 20 | 11 | **11 / 11** | 8 (6 × `7.5` rows covering different column counts, 2 × `7.5` table without header cells) and 1 not a package |
| PPTX, PDF/UA-1 | 19 | 16 | **16 / 16** | 3 (`7.3` figure without alternate text) |
| DOCX, PDF/A-2a | 25 | 20 | 19 / 20 | 4 (`6.7` figure without alternate text) and 1 not a package; the one failure is rule `6.2.11.7.3` (a character in the Private Use Area without `ActualText`), 9 checks in 1 file |
| PPTX, PDF/A-2a | 19 | 16 | 15 / 16 | 3 (`6.7`); the one failure is the same rule `6.2.11.7.3`, 4 checks in 1 file |
| DOCX / XLSX / PPTX, tagged with no profile, checked as PDF/UA-1 | 24 / 19 / 19 | all | 0 | not applicable: they fail `7.1 #8` (no `pdfuaid` in the catalog metadata) as expected without the profile; beyond that: `7.2 #42/#43` table rows of different widths in 4 DOCX and 6 + 3 XLSX, `7.3 #1` figure without alt text in 4 DOCX and 3 PPTX |

So the profile either refuses a document it cannot make conformant (10 of 24 readable Word documents are refused for PDF/UA because a table has no header row; this is the known table-header item),
or produces a PDF veraPDF passes, except for the Private Use Area `ActualText` case in PDF/A (the multi-code-point ToUnicode item on OnePDF's list, not ours). The first run of this stage
refused 22 of 23 DOCX because the probe set no title; that was the probe, fixed (`Title = name`). Jobs `princessDonut.mz13cypl` (final), `princessDonut.j1axptck` (first).

## 7. Memory

| Gate | Input | Result |
|---|---|---|
| Lazy open, 1,000-page DOCX | `stress-1000p.docx`, 55 parts | open 59 ms; 199 KiB read in 7 reads; 358 KiB allocated; 2 of 55 parts' content read; peak working set 37 MB |
| Lazy open, 500 MB package | generated, 503 MB, 39 parts | open 67 ms; 197 KiB read (0.038% of the file); 282 KiB allocated; 2 parts read; the 200 MB main part read only when asked (556 ms, then peak 241 MB) |
| Streaming read | 1,000,001 rows × 20 = 20,000,020 cells, 127 MB | 3.98 s; **52,656 bytes allocated in all, 0.0026 B/cell**; working set 49 / 50 / 50 MB at 10k / 100k / 1M rows (41 MB before reading) |
| Streaming write | 1,000,000 × 20, 194 MB written | 2.27 s; 717 KB allocated in all (0.036 B/cell); working set 38 / 41 / 41 MB at 10k / 100k / 1M rows |
| Full model, bytes retained per cell | the same 20,000,020-cell workbook | OneDocument **37.2**, ClosedXML 78.8, NPOI 360.4 (743,967,768 / 1,575,651,240 / 7,208,587,912 bytes) |

The timings printed by the probes are indicative only. Jobs `princessDonut.pdljjal5`, `princessDonut.xc4dfc5v`, `princessDonut.eglftr6s`.

## 8. What the gate found and what was fixed

Fixed (each with a test):
- `ADDRESS(r, c, , , "Sheet")`: an empty fourth argument was read as R1C1, not A1 (`LookupFunctions.cs`). 41,000 cells in one legacy workbook went from `#REF!` to Excel's values.
- `ROW(A2)` in A2 (and `COLUMN`, `ROWS`, `COLUMNS` of a reference to the cell itself) counted as a circular reference and evaluated to 0 (`DependencyWalker.cs`): these functions read where a reference is, not its value.
- A PPT/DOC whose edit-time field held a stray timestamp wrote `TotalTime` beyond `xsd:int`, so `docProps/app.xml` failed the schema (`OleDocumentProperties.cs`): reported as lost instead.
- BIFF8 writer: a workbook with a style sheet that lists no fills or borders raised an untyped `ArgumentOutOfRangeException` from `SaveAs` (`XlsStyleMap.cs`; 2 of 51 Common Crawl workbooks).
- ODT writer: style names that start with a digit, hyphen or full stop are not XML names (`OdfConversionSupport.cs`); ODS writer: `scale-to` written beside `scale-to-X/Y` for a fit-to-page sheet (`OdsExporter.Features.cs`); ODP writer: negative text insets as padding and connectors without `svg:viewBox` (`OdpExporter.Shapes.cs`, `OdpExporter.Text.cs`).
- Probes: the calc sweep reads XLS, XLSB, ODS and classifies mismatches; the text and PDF stages set a title and register all kinds.

Open items (not fixed here):
1. **Memory of one calc input**: a 114 KB workbook needs ~31 GB to recalculate; calc has no allocation ceiling that turns this into a typed limit error.
2. `Document.Open` does not recover truncated packages that `OpcPackage.Open` can (33 of 36 in the DOCX set): a design choice for the sniffer, which names formats from the directory. A test pins the current behaviour (`Damaged_containers_are_unknown_not_exceptions`).
3. ODT: `text:soft-hyphen` alone in a `text:span`; `table:table-row` incomplete (merged cells); `style:header-first` order. ODS: number-style children order. (Jing messages above.)
4. Autofilter-hidden rows in `SUBTOTAL`; `GETPIVOTDATA` against a cached pivot; external-workbook references in BIFF (convert to `[n]Sheet!A1` with the cached values instead of `#REF!`).
5. XLSX single-cell edit: one extra schema error in 3 of 360 worksheets that were already invalid (cause not traced).
6. Text: no option yet to include numbers, booleans and error values of workbook cells for a Tika-like dump.
