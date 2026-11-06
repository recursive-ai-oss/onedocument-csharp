# OneDocument

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

OneDocument is a clean-room document processor for `net10.0` that depends only on the .NET base class library and other Recursive AI libraries. It is NativeAOT-, trim- and browser-WebAssembly-friendly: no reflection, no native code, no `System.Xml` in the document path.

- **Lossless models.** `Document`, `Workbook` and `Presentation` are views over the file's own markup. Parts you do not touch are written back byte-identical (raw zip entries, never recompressed); edits keep unknown elements, attributes, prefixes, `mc:AlternateContent` and `extLst` in order.
- **Every layer is public:** containers and records, a lazy markup tree, generated typed views of the schemas, the hubs, and the services on top (calculation, templating, layout, operations). The `Office`, `Spreadsheet` and `Template` statics are one line over the model.
- **Tolerant reading, typed defects.** Damage is repaired where Office repairs it and reported as a located defect (`docs/defects.md`); resource limits are on by default; nothing touches the network or runs code.
- **Conversions say what they lose.** Every conversion between formats can return a loss report.

## Status: 1.0

The tables below say what the code does and what has been checked, with the note or test that holds each number. A check that could not be run says so and is listed under [Gaps](#gaps); nothing here claims what was not measured. Measurements were taken on the code at the 1.0 freeze.

## Samples

Each sample under [`samples/`](samples) is a small console program that generates its own input and runs in CI.

| Task | Sample | The call |
|---|---|---|
| Convert a document, workbook or presentation to PDF | [`ConvertToPdf`](samples/ConvertToPdf) | `Office.Convert("deck.pptx", "deck.pdf")` |
| Fill a template and write PDF | [`TemplateToPdf`](samples/TemplateToPdf) | `Template.Render("invoice.docx", invoice, "invoice.pdf")` |
| Rows of plain objects to and from a workbook | [`SpreadsheetRows`](samples/SpreadsheetRows) | `Spreadsheet.Write(path, orders)`, `Spreadsheet.Read<Order>(path)` |
| Text of a legacy `.doc` | [`LegacyDocText`](samples/LegacyDocText) | `Office.ExtractText("old.doc")` |
| Edit a cell, recalculate, save as PDF | [`WorkbookRecalcPdf`](samples/WorkbookRecalcPdf) | `Workbook.Open`, `cell.Value =`, `wb.Recalculate()`, `wb.SaveAs("x.pdf")` |
| Filter paragraphs by style, accept tracked changes, save | [`DocumentRevisions`](samples/DocumentRevisions) | `doc.Paragraphs.Where(p => p.Style == "Heading 1")`, `doc.Revisions.AcceptAll()`, `doc.SaveAs(...)` |
| Sanitise in memory, then save | [`HappyPath`](samples/HappyPath) | `doc.Sanitize()`, `doc.SaveAs("clean.docx")` |
| Build a document, a workbook and a deck from nothing | [`Authoring`](samples/Authoring) | `body.AddParagraph(...)`, `body.AddTable(rows)`, `sheet.Range("A1:C3").Values = ...`, `deck.AddSlide("Title Slide")` |

[`HappyPath`](samples/HappyPath) runs the introduction's sketch as written, one or two lines per task.

```csharp
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;

using var doc = Document.Open("contract.docx");   // the format comes from the content, never the extension
doc.Paragraphs.First().SetText("Master services agreement");
doc.Save();                                       // atomic; only word/document.xml changes
Office.Convert("contract.docx", "contract.pdf");  // needs the Rendering package (or the All metapackage)
```

The `Office` statics find the readers and writers of every kind package the application references; no registration call is needed (each package registers itself into the referencing C# project at build time, with no reflection). A format whose package is not referenced raises `FormatNotAvailableException` naming it. Referencing one kind package (say `.Spreadsheets`) is enough for that kind and pulls in no other. In a project that is not C# or that sets `<OneDocumentAutoRegister>false</OneDocumentAutoRegister>`, call `DocumentFormats.Register(FormatRegistry.Default)` (and `SpreadsheetFormats`, `PresentationFormats`, `RenderingFormats`) once at start-up; [`docs/contracts.md`](docs/contracts.md#formats-registry-and-sniffing-recursiveaionedocumentformats) has the table. Coming from another library? See [`docs/migration/`](docs/migration).

## Formats

"Untouched round trip" is open then save with no edit: for the OOXML, ODF and XLSB families every part is written back as stored; the other formats are rebuilt from the model and say so in the loss report. Details and defect codes are in the linked notes.

| Format | Read | Write | Untouched round trip | Loss report | Notes |
|---|---|---|---|---|---|
| DOCX, DOCM, DOTX, DOTM, Flat OPC | yes | yes | parts byte-identical (`docs/contracts.md`, `DocumentOpenTests`) | n/a (native) | `docs/docx-*.md`, `docs/revisions.md` |
| XLSX, XLSM, XLTX, XLTM, XLAM, Flat OPC | yes (model and streaming) | yes (model and streaming) | parts byte-identical (`docs/spreadsheets.md`) | n/a (native) | `docs/spreadsheets*.md` |
| PPTX, PPTM, POTX, PPSX, Flat OPC | yes | yes | parts byte-identical (`docs/presentations.md`, `OpenAndSaveTests`) | n/a (native) | `docs/presentations.md` |
| XLSB | yes (model and streaming) | yes | unedited parts copied byte for byte (`docs/xlsb.md`) | n/a (native) | `docs/xlsb.md` |
| XLS (BIFF8) | yes (model and streaming) | yes (one clean BIFF8 dialect; RC4 CryptoAPI password optional) | rebuilt, not carried through | yes, on conversion | `docs/xls.md` |
| XLS (BIFF2 to BIFF5) | yes (model and streaming) | no | n/a | yes | `docs/xls.md` (layouts written from observed files) |
| DOC (Word 97 to 2013, Word 6/95, fast-saved) | yes | no (post-1.0 on demand) | n/a | yes | `docs/doc.md` |
| PPT (97 to 2003) | yes | no (post-1.0 on demand) | n/a | yes | `docs/ppt.md` |
| Word 2003 XML, Excel 2003 XML | yes | no | n/a | yes | `docs/xml-2003.md` |
| RTF 1.9.1 | yes | yes | RTF saved without an edit keeps its bytes (`docs/rtf.md`) | yes, both directions | `docs/rtf.md` |
| ODT, OTT, flat FODT | yes | yes | entries copied raw (`docs/odf.md`) | yes | `docs/odf.md` |
| ODP, OTP, flat FODP | yes (`Presentation`) | yes | read and saved in its own format without an edit writes the original bytes; any edit writes a package of its own (`docs/odp.md`) | yes, both directions | `docs/odp.md` |
| ODS, OTS, flat FODS | yes (`Workbook`) | yes | entries copied raw (`docs/odf.md`) | yes, both directions | `docs/ods.md` |
| CSV, TSV | yes | yes | n/a | writing reports dropped formulas, styles and sheets | `docs/csv.md` |
| HTML | yes (sanitised by default) | yes (documents; workbooks and presentations at a basic level) | n/a | yes | `docs/html.md` |
| Markdown (CommonMark, GFM) | yes | yes | n/a | yes | `docs/markdown.md` |
| PDF (tagged; PDF/UA-1 or PDF/A-2a profiles); PNG, JPEG, TIFF (multi-page), WebP and SVG pages | no | yes, for documents, workbooks and presentations | n/a | n/a | `docs/layout.md`, `docs/rendering-export.md` |
| Text | no | yes (`Office.ExtractText`) | n/a | n/a | `docs/text-extraction.md` |
| Embedded EMF, EMF+, WMF pictures | rendered | no | carried as parts | n/a | `docs/metafiles.md` |

Encrypted files open when `OpenContext.Password` is supplied: OOXML Agile and Standard, RC4, RC4 CryptoAPI and XOR for the legacy formats (`docs/encryption.md`). Excel, Word and PowerPoint open our RC4 CryptoAPI XLS output, and the Agile and RC4 files other applications wrote, with the same password (9 of 10 fixtures; the XOR one is the exception).

## Checks

Numbers come from the committed note or test named in the last column. Office-oracle checks (Word, Excel and PowerPoint opening our output without a repair prompt, Office's own result for the same operation, the layout differential) ran on desktop Office 16.0.20527 for the 1.0 gate: results, floors and the open items are in [`docs/office-oracle-1.0.md`](docs/office-oracle-1.0.md). The remaining requests under `testdata/` are listed there as not run.

| Check | Result | Source |
|---|---|---|
| Untouched round trip over a public corpus, per-part hashes | 4,900 OOXML and XLSB packages (Common Crawl, bug-tracker attachments, LibreOffice-converted sets): 111,239 of 111,239 parts byte-identical at the package layer; 6 files refused with a typed defect. Through the hubs: 2,773 DOCX (56,316 parts), 1,508 XLSX (39,328), 537 PPTX (43,986) and 18 XLSB (334) saved, every part identical; an edit changes only the edited part in every file | `docs/gates-1.0.md` §1 |
| Written OOXML against the ECMA-376 and extension schemas | no file went from valid to invalid by our write: DOCX 275 pairs, XLSX 360, PPTX 515 decks (2,101 outputs); legacy conversions to OOXML 301 of 301 valid and opened by LibreOffice 26.8; ODF 1.3 against the RelaxNG schema: ODT 59, ODS 51 and ODP 50 written files (results and open items in the note) | `docs/gates-1.0.md` §2, §3 |
| Output opens in Word, Excel and PowerPoint with no repair prompt | 718 generated files open clean (edits, authoring, templates, mail merge, content controls, conversions from DOC, PPT, RTF, ODF, HTML, XLS, XLSB, written XLS with notes, layout cases); every other file is an input that fails the same way or an ODS file Excel does not open, each listed | `docs/office-oracle-1.0.md`, `testdata/*/ORACLE-REQUEST.md` |
| Number formats against Excel (`TEXT` and cell display) | 7,602 of 7,602 agree on a grid; exceptions are listed | `docs/number-formats.md`, `NumberFormatExcelGridTests` |
| Calculation: cached values reproduced over a corpus | recalculation reproduces the cached values of 99.52% of the testable formulas in 354 Common Crawl workbooks, 93.02% in 702 bug-tracker workbooks, 93.79% in 453 LibreOffice-converted workbooks, 95.14% in 450 XLS files, 99.54% in 18 XLSB files; every mismatch class is explained in the note. | `docs/gates-1.0.md` §5, `docs/calc.md`, `docs/spreadsheets-calc.md` |
| Calculation: per-function grids against Excel 16.0.20527 | functions 22,700 rows 93.3 % agree, financial 84.5 %, engineering 88.0 %, lookup and reference 99.5 % (9,707 rows), database 100 % (324 rows); every disagreement is listed. | `docs/office-oracle-1.0.md`, `docs/calc.md`, `docs/spreadsheets-calc.md` |
| Layout: page count, line breaks and object geometry against Office's PDF export (generated cases; rows where both sides had the same fonts) | Page count agrees on 196 of 196 documents and workbooks and 24 of 24 decks. Mean text-line agreement and, for Word, word positions within 2 pt: business documents 76 rows 0.951 and 0.872; long documents 15 rows 0.966 and 0.941; edge features (floats, drawings, diagrams, math) 29 rows 0.834 and 0.588; worksheets 26 rows 0.934 (geometry is information: Excel's default row height differs by display scaling); CJK and right-to-left 1 row 1.000 and 0.000 (8 more rows are font-substitution rows and do not gate); presentations 0.748 (geometry not run). These are floors: they only rise | `docs/office-oracle-1.0.md` §3, `docs/layout.md` |
| Tagged PDF against veraPDF 1.30.3 (PDF/UA-1, PDF/A-2a) | PDF/UA-1: every PDF written passes (DOCX 11, XLSX 11, PPTX 16); a document the profile cannot make conformant is refused with the rule. PDF/A-2a: 19 of 20 DOCX and 15 of 16 PPTX pass; the failures are one rule, a private-use character without `ActualText` (a PDF-writer item) | `docs/gates-1.0.md` §6 |
| Text extraction against Apache Tika 3.3.3 | 150 files per format: precision of our words in Tika's at least 0.985 for every format; the differences are what Tika emits and we leave out by policy | `docs/gates-1.0.md` §4 |
| CommonMark and GFM conformance | 652 of 652 and 28 of 28 examples | `docs/markdown.md`, `MarkdownSpecTests` |
| Metafiles against Windows' own pictures | 64 Windows-written metafiles compared (25 EMF, 24 EMF+, 15 WMF); per-case agreement in the note | `docs/metafiles.md`, `MetafileTests` |
| Fuzzing, 600 s per parser | 70 targets, 600 s each under libFuzzer with SharpFuzz instrumentation, all clean on the final code. the campaign found and fixed crashes, hangs and limit failures (calculation loops and overflow, a template range clip, shared-string surrogates, R1C1 quoting, chart, drawing, layout and metafile clamps), each with a regression test | `docs/fuzzing.md`, `fuzz/` |
| NativeAOT and trimming | the smoke app publishes with warnings as errors and runs | `smoke/aot`, `build/ci.sh aot` |
| Browser WebAssembly | 25 smoke groups run in headless Chromium (Agile, Standard and RC4-MD5 decryption excepted, see Gaps) | `docs/platforms.md`, `build/ci.sh wasm-run` |
| iOS and Android | the smoke checks pass in an iOS simulator and an Android emulator (Mono ahead-of-time release builds; NativeAOT on those hosts is not available for simulators) | `docs/platforms.md` |
| Memory | 37 retained bytes per cell on a 20M-cell styled workbook (ClosedXML 79, NPOI 360); streaming read allocates 0.08 bytes per cell at 1M rows; streaming working set 47 to 49 MB from 10k to 1M rows (x64) | `bench/README.md` |
| Speed (1.0 gate rows) | against the fastest like-for-like managed comparator over 49 rows, single thread: geomean 0.30 (x64) and 0.32 (arm64), worst row 1.08 (x64) and 1.07 (arm64); the save rows gate on the save-to-stream variants, and the save-to-file rows are published beside them (our `Save()` flushes the temp file to disk and renames it atomically, and the Open XML SDK edits the file in place without a flush); `.xls` and `.xlsb` reads go through forward cursors and are 0.55 to 0.64 times (xls) and 0.93 to 1.01 times (xlsb) the time of Sylvan.Data.Excel, the fastest forward reader | `bench/README.md`, `bench/published/` |

## Operations

| Operation | State |
|---|---|
| Find and replace, merge, append, split, protection | implemented (`docs/operations-find-replace.md`, `docs/operations-merge.md`, `docs/protection.md`) |
| Redaction (`Office.Redact`) | implemented, writes a new file and re-reads it to verify; an independent completeness review passed (`docs/redaction-review.md`), with its open items listed there. Office opens the redacted outputs: not run (see `docs/office-oracle-1.0.md`) |
| Inspect and sanitise (`Office.Inspect`, `Office.Sanitize`, `onedocument inspect`, `sanitize`) | implemented (`docs/sanitize.md`); the Document Inspectors that automation can run (Word 5, Excel 5, PowerPoint 4) find nothing in 36 sanitised files that open, and Office opens them without repair (`docs/office-oracle-1.0.md`); an independent coverage review passed (`docs/sanitize-review.md`) |
| VBA read, extract, remove | implemented, never executed (`docs/vba.md`) |
| Accessibility checking | implemented (`docs/accessibility.md`); agreement with Office's checker is **pending**: the checker has no automation interface, so it was not measured (`docs/office-oracle-1.0.md`) |

## Gaps

Stated here so they are not found by surprise.

- **Encrypted output.** Encrypted files are written only for XLS (RC4 CryptoAPI). Agile OOXML and ODF encryption on write wait for the Cryptography package.
- **Cryptography package.** `RecursiveAI.OneDocument.Cryptography` (signatures, encryption on write) has no content yet and is not part of the `All` metapackage.
- **Browser.** WebAssembly has no `Aes` or MD5, so opening Agile or Standard encrypted OOXML and RC4-MD5 files there raises `OfficeUnsupportedException`. RC4 CryptoAPI and XOR files read normally (`docs/platforms.md`). The XOR reader's array-index rule rests on LibreOffice alone: Excel does not open our XOR test file, so that scheme is unconfirmed against Office (`docs/office-oracle-1.0.md`).
- **Document Inspector agreement.** Office's object model exposes only some inspectors (Word 5, Excel 5, PowerPoint 4). Comments, revisions, document properties and personal information, embedded documents, macros and links cannot be asked, so agreement for those categories was not measured; our own re-inspection finds nothing left in sanitised output (`docs/office-oracle-1.0.md`, `docs/sanitize.md`).
- **Accessibility Checker.** Pending: Office has no automation for it, so our results are checked against the published rule set only (`docs/accessibility.md`).
- **PDF/A-2a.** In the veraPDF run, 19 of 20 DOCX and 15 of 16 PPTX pass; the failures are one rule (6.2.11.7.3, a private-use character without `ActualText`) in the PDF writer (`docs/gates-1.0.md` §6).
- **Calculation.** Recalculation reproduces cached values over each corpus at the floors in the Checks table; the mismatches fall in named classes (precision beyond 9 digits, volatile functions and what depends on them, references into other workbooks, legacy references the converter leaves as `#REF!`, XLM macro functions, and unexplained cases), listed per set in `docs/gates-1.0.md` §5. ODS-sourced workbooks reproduce 65.3% and were not rerun after the fixes. The per-function grids against Excel disagree on 6.7% of function rows, 15.5% of financial and 12.0% of engineering rows (distribution inverses on boundary arguments, text conversions, coupon-date functions, iteration limits, error kinds on out-of-range arguments); `GETPIVOTDATA` over some cached pivots, `SUBTOTAL` with an autofilter and external references in BIFF are open.
- **Schema validation.** On four already-invalid worksheets in the public corpus, the validator counts one more `xr:uid` error after our rewrite than before; the attribute is on the original too and its cause was not established (`docs/gates-1.0.md` §2).
- **ODS.** No forward cursor: ODS reads through the model.
- **Office opens ODS.** Excel does not open the ODS files we write (they validate against the ODF 1.3 schema and LibreOffice opens them); information, not bisected.
- **Formula text** is en-US and invariant only; localized function names are not supported.
- **DOC and PPT** are read-only. **Word 6/95 and BIFF2 to BIFF5** layouts come from observed files, not a specification.
- **Metafiles.** Drawn as vector content where the interpreter supports the records; custom pen caps, line-end anchors, colour matrices and text clipping to a string's rectangle are not drawn, and some raster-operation cases fall back to a bitmap (`docs/metafiles.md`).
- **Output formats.** PDF, and PNG, JPEG, TIFF, WebP and SVG pages (`PageExporter`; `SaveAs` and `Office.Convert` write the first page, or every page for TIFF). HTML, Markdown and text export are on the hubs.
- **Rendering fidelity.** Page layout is implemented from the Word, PowerPoint and Excel rules in `docs/layout*.md`; how closely it matches Office is measured in `docs/office-oracle-1.0.md` (Word 16.0.20527, generated cases), and the layout notes list the places still to be checked against Word.
- **Not executed.** Macros, OLE payloads, Power Query, the Data Model and Solver are carried and (for VBA) listed or removed, never run.

## Packages

| Package | Contents |
|---|---|
| `RecursiveAI.OneDocument` | Core: contracts, containers (OPC, compound files, ODF package), XML tokenizer and markup tree, format registry, formula and calc core, number formats, template engine, text extraction, RTF and VBA layers |
| `RecursiveAI.OneDocument.Documents` | Word-processing documents: DOCX, DOC, RTF, ODT, 2003 XML, HTML, Markdown, revisions, mail merge, operations |
| `RecursiveAI.OneDocument.Spreadsheets` | Workbooks: XLSX, XLSB, XLS, CSV/TSV, 2003 XML, calculation, pivots, streaming, typed row binding |
| `RecursiveAI.OneDocument.Presentations` | Presentations: PPTX, PPT |
| `RecursiveAI.OneDocument.Schema` | Generated zero-copy typed views over every pinned schema: WordprocessingML, SpreadsheetML, PresentationML, DrawingML, charts, chartex, diagrams, VML, Office Math, the package parts and the Microsoft extension schemas; `.Spreadsheets` uses them |
| `RecursiveAI.OneDocument.Rendering` | Layout, charts, SmartArt, equations, drawing; PDF and page-image (PNG, JPEG, TIFF, WebP, SVG) output for all three kinds |
| `RecursiveAI.OneDocument.All` | Metapackage of every package above |
| `RecursiveAI.OneDocument.Cryptography` | Signatures and encryption on write (no content yet; not in `All`) |
| `onedocument` | Command-line tool: `detect`, `extract`, `recalc`, `template`, `render`, `convert`, `inspect`, `sanitize` |

## Building

```
dotnet build OneDocument.slnx
dotnet test --solution OneDocument.slnx
build/ci.sh            # restore, build, test, samples, NativeAOT smoke, WebAssembly smoke, pack
```

The public surface of every library assembly is frozen in `src/<Project>/PublicAPI.Shipped.txt`, checked on each build by the public-API analyzer (build-only, nothing flows into the packages): a public type or member added, removed or changed without a listing edit fails the build. List an intended change in `PublicAPI.Unshipped.txt` (`tools/api/update-public-api.py --unshipped` does it); a release moves those lines into `Shipped`.

The .NET SDK version is pinned in `global.json`. Build output goes to `artifacts/` (ignored by git); set `ONEDOCUMENT_ARTIFACTS` to put it elsewhere.

## Layout

`src/` packages, `samples/` runnable examples, `tests/` unit tests, `tools/` harness and generator tools, `fuzz/` libFuzzer targets, `bench/` benchmarks, `smoke/` NativeAOT and WebAssembly smoke apps, `docs/` normative notes (index in [`docs/README.md`](docs/README.md)).

## Licence

`MIT OR Apache-2.0`, © Recursive AI, LLC. See `LICENSE-MIT`, `LICENSE-APACHE` and `THIRD-PARTY-NOTICES.md`.

OneDocument is not affiliated with Microsoft or with any other product or company named OneDocument. Word, Excel and PowerPoint are Microsoft product names used here only to describe the formats.
