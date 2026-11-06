# OneDocument

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

OneDocument is a clean-room document processor for `net10.0` that depends only on the .NET base class library and other Recursive AI libraries. It is NativeAOT-, trim- and browser-WebAssembly-friendly: no reflection, no native code, no `System.Xml` in the document path.

- **Lossless models.** `Document`, `Workbook` and `Presentation` are views over the file's own markup. Parts you do not touch are written back byte-identical (raw zip entries, never recompressed); edits keep unknown elements, attributes, prefixes, `mc:AlternateContent` and `extLst` in order.
- **Every layer is public:** containers and records, a lazy markup tree, generated typed views of the schemas, the hubs, and the services on top (calculation, templating, layout, operations). The `Office`, `Spreadsheet` and `Template` statics are one line over the model.
- **Tolerant reading, typed defects.** Damage is repaired where Office repairs it and reported as a located defect (`docs/defects.md`); resource limits are on by default; nothing touches the network or runs code.
- **Conversions say what they lose.** Every conversion between formats can return a loss report.

## Status: preview, not released

Nothing is published to nuget.org yet. Read the tables below as "what the code does today and what has been checked". A cell that says **pending (1.0 gate)** has not been measured yet; nothing in it should be read as passed.

## Samples

Each sample under [`samples/`](samples) is a small console program that generates its own input and runs in CI.

| Task | Sample | The call |
|---|---|---|
| Convert a document, workbook or presentation to PDF | [`ConvertToPdf`](samples/ConvertToPdf) | `Office.Convert("deck.pptx", "deck.pdf")` |
| Fill a template and write PDF | [`TemplateToPdf`](samples/TemplateToPdf) | `Template.Render("invoice.docx", invoice, "invoice.pdf")` |
| Rows of plain objects to and from a workbook | [`SpreadsheetRows`](samples/SpreadsheetRows) | `Spreadsheet.Write(path, orders)`, `Spreadsheet.Read<Order>(path)` |
| Text of a legacy `.doc` | [`LegacyDocText`](samples/LegacyDocText) | `Office.ExtractText("old.doc")` |
| Edit a cell, recalculate, save as PDF | [`WorkbookRecalcPdf`](samples/WorkbookRecalcPdf) | `Workbook.Open`, `cell.Value =`, `wb.Recalculate()`, `wb.SaveAs("x.pdf")` |
| Filter paragraphs by style, accept tracked changes, save | [`DocumentRevisions`](samples/DocumentRevisions) | `doc.Paragraphs.Where(...)`, `doc.Revisions.AcceptAll()`, `doc.SaveAs(...)` |

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
| XLSB | yes | yes | unedited parts copied byte for byte (`docs/xlsb.md`) | n/a (native) | `docs/xlsb.md` |
| XLS (BIFF8) | yes | yes (one clean BIFF8 dialect; RC4 CryptoAPI password optional) | rebuilt, not carried through | yes, on conversion | `docs/xls.md` |
| XLS (BIFF2 to BIFF5) | yes | no | n/a | yes | `docs/xls.md` (layouts written from observed files) |
| DOC (Word 97 to 2013, Word 6/95, fast-saved) | yes | no (post-1.0 on demand) | n/a | yes | `docs/doc.md` |
| PPT (97 to 2003) | yes | no (post-1.0 on demand) | n/a | yes | `docs/ppt.md` |
| Word 2003 XML, Excel 2003 XML | yes | no | n/a | yes | `docs/xml-2003.md` |
| RTF 1.9.1 | yes | yes | RTF saved without an edit keeps its bytes (`docs/rtf.md`) | yes, both directions | `docs/rtf.md` |
| ODT, OTT, flat FODT | yes | yes | entries copied raw (`docs/odf.md`) | yes | `docs/odf.md` |
| ODP, OTP, flat FODP | yes (`Presentation`) | yes | read and saved in its own format without an edit writes the original bytes; any edit writes a package of its own (`docs/odp.md`) | yes, both directions | `docs/odp.md` |
| ODS, FODS | pending (not on this branch yet) | pending | pending | pending | `docs/odf.md` |
| CSV, TSV | yes | yes | n/a | writing reports dropped formulas, styles and sheets | `docs/csv.md` |
| HTML | yes (sanitised by default) | yes (documents; workbooks and presentations at a basic level) | n/a | yes | `docs/html.md` |
| Markdown (CommonMark, GFM) | yes | yes | n/a | yes | `docs/markdown.md` |
| PDF (tagged; PDF/UA-1 or PDF/A-2a profiles), PNG | no | yes, for documents, workbooks and presentations | n/a | n/a | `docs/layout.md` |
| Text | no | yes (`Office.ExtractText`) | n/a | n/a | `docs/text-extraction.md` |
| Embedded EMF, EMF+, WMF pictures | rendered | no | carried as parts | n/a | `docs/metafiles.md` |

Encrypted files open when `OpenContext.Password` is supplied: OOXML Agile and Standard, RC4, RC4 CryptoAPI and XOR for the legacy formats (`docs/encryption.md`).

## Checks

Numbers come from the committed note or test named in the last column. Office-oracle checks (Word, Excel and PowerPoint opening our output without a repair prompt, Office's own result for the same operation, the layout differential) are written as requests under `testdata/` and are all **pending (1.0 gate)**.

| Check | Result | Source |
|---|---|---|
| Untouched round trip over a public corpus, per-part hashes | pending (1.0 gate) | |
| Written OOXML against the ECMA-376 and extension schemas | pending (1.0 gate) | |
| Output opens in Word, Excel and PowerPoint with no repair prompt | pending (1.0 gate) | `testdata/*/ORACLE-REQUEST.md` |
| Number formats against Excel (`TEXT` and cell display) | 7,602 of 7,602 agree on a grid; exceptions are listed | `docs/number-formats.md`, `NumberFormatExcelGridTests` |
| Calculation: cached values reproduced over a corpus; per-function grids against Excel | pending (1.0 gate) | `docs/calc.md`, `docs/spreadsheets-calc.md` |
| Layout: page count, line breaks and object geometry against Office's PDF export | pending (1.0 gate) | `docs/layout.md` |
| Tagged PDF against veraPDF (PDF/UA-1, PDF/A) | pending (1.0 gate) | |
| CommonMark and GFM conformance | 652 of 652 and 28 of 28 examples | `docs/markdown.md`, `MarkdownSpecTests` |
| Metafiles against Windows' own pictures | 64 Windows-written metafiles compared (25 EMF, 24 EMF+, 15 WMF); per-case agreement in the note | `docs/metafiles.md`, `MetafileTests` |
| Fuzzing, 600 s per parser | pending (1.0 gate) | `fuzz/` |
| NativeAOT and trimming | the smoke app publishes with warnings as errors and runs | `smoke/aot`, `build/ci.sh aot` |
| Browser WebAssembly | 25 smoke groups run in headless Chromium | `docs/platforms.md`, `build/ci.sh wasm-run` |
| Memory | 37 retained bytes per cell on a 20M-cell styled workbook (ClosedXML 79, NPOI 360); streaming read allocates 0.08 bytes per cell at 1M rows; streaming working set 47 to 49 MB from 10k to 1M rows (x64) | `bench/README.md` |
| Speed (preview rows, re-run at the 1.0 gate) | against the fastest managed comparator: documents geomean 0.19 (x64) and 0.18 (arm64) over 15 rows, worst 0.65 and 0.63; workbooks geomean 0.53 and 0.59 over 10 rows, worst 1.01 and 1.05 | `bench/README.md`, `bench/published/` |

## Operations

| Operation | State |
|---|---|
| Find and replace, merge, append, split, protection | implemented (`docs/operations-find-replace.md`, `docs/operations-merge.md`, `docs/protection.md`) |
| Redaction (`Office.Redact`) | implemented, writes a new file and re-reads it to verify; an independent completeness review passed (`docs/redaction-review.md`), with its open items listed there. Office check pending (1.0 gate) |
| Inspect and sanitise (`Office.Inspect`, `Office.Sanitize`, `onedocument inspect`, `sanitize`) | implemented (`docs/sanitize.md`); coverage review pending |
| VBA read, extract, remove | implemented, never executed (`docs/vba.md`) |
| Accessibility checking | implemented (`docs/accessibility.md`); agreement with Office's checker pending (1.0 gate) |

## Gaps

Stated here so they are not found by surprise.

- **Encrypted output.** Encrypted files are written only for XLS (RC4 CryptoAPI). Agile OOXML and ODF encryption on write wait for the Cryptography package.
- **Cryptography package.** `RecursiveAI.OneDocument.Cryptography` (signatures, encryption on write) has no content yet and is not part of the `All` metapackage.
- **Browser.** WebAssembly has no `Aes` or MD5, so opening Agile or Standard encrypted OOXML and RC4-MD5 files there raises `OfficeUnsupportedException`. RC4 CryptoAPI and XOR files read normally (`docs/platforms.md`).
- **Formula text** is en-US and invariant only; localized function names are not supported.
- **DOC and PPT** are read-only. **Word 6/95 and BIFF2 to BIFF5** layouts come from observed files, not a specification.
- **Metafiles.** Drawn as vector content where the interpreter supports the records; custom pen caps, line-end anchors, colour matrices and text clipping to a string's rectangle are not drawn, and some raster-operation cases fall back to a bitmap (`docs/metafiles.md`).
- **Output formats.** PDF and PNG (first page) only: no SVG, JPEG, TIFF or WebP page export yet. HTML, Markdown and text export are on the hubs.
- **Rendering fidelity.** Page layout is implemented from the Word, PowerPoint and Excel rules in `docs/layout*.md`; how closely it matches Office is pending (1.0 gate), and the layout notes list the places still to be checked against Word.
- **Not executed.** Macros, OLE payloads, Power Query, the Data Model and Solver are carried and (for VBA) listed or removed, never run.

## Packages

| Package | Contents |
|---|---|
| `RecursiveAI.OneDocument` | Core: contracts, containers (OPC, compound files, ODF package), XML tokenizer and markup tree, format registry, formula and calc core, number formats, template engine, text extraction, RTF and VBA layers |
| `RecursiveAI.OneDocument.Documents` | Word-processing documents: DOCX, DOC, RTF, ODT, 2003 XML, HTML, Markdown, revisions, mail merge, operations |
| `RecursiveAI.OneDocument.Spreadsheets` | Workbooks: XLSX, XLSB, XLS, CSV/TSV, 2003 XML, calculation, pivots, streaming, typed row binding |
| `RecursiveAI.OneDocument.Presentations` | Presentations: PPTX, PPT |
| `RecursiveAI.OneDocument.Schema` | Generated zero-copy typed views over every pinned schema: WordprocessingML, SpreadsheetML, PresentationML, DrawingML, charts, chartex, diagrams, VML, Office Math, the package parts and the Microsoft extension schemas; `.Spreadsheets` uses them |
| `RecursiveAI.OneDocument.Rendering` | Layout, charts, SmartArt, equations, drawing; PDF and PNG output for all three kinds |
| `RecursiveAI.OneDocument.All` | Metapackage of every package above |
| `RecursiveAI.OneDocument.Cryptography` | Signatures and encryption on write (no content yet; not in `All`) |
| `onedocument` | Command-line tool: `detect`, `extract`, `recalc`, `template`, `render`, `convert`, `inspect`, `sanitize` |

## Building

```
dotnet build OneDocument.slnx
dotnet test --solution OneDocument.slnx
build/ci.sh            # restore, build, test, samples, NativeAOT smoke, WebAssembly smoke, pack
```

The .NET SDK version is pinned in `global.json`. Build output goes to `artifacts/` (ignored by git); set `ONEDOCUMENT_ARTIFACTS` to put it elsewhere.

## Layout

`src/` packages, `samples/` runnable examples, `tests/` unit tests, `tools/` harness and generator tools, `fuzz/` libFuzzer targets, `bench/` benchmarks, `smoke/` NativeAOT and WebAssembly smoke apps, `docs/` normative notes (index in [`docs/README.md`](docs/README.md)).

## Licence

`MIT OR Apache-2.0`, © Recursive AI, LLC. See `LICENSE-MIT`, `LICENSE-APACHE` and `THIRD-PARTY-NOTICES.md`.

OneDocument is not affiliated with Microsoft or with any other product or company named OneDocument. Word, Excel and PowerPoint are Microsoft product names used here only to describe the formats.
