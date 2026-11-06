# OneDocument

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

OneDocument is a clean-room document processor for `net10.0` that depends only on the .NET base class library and other Recursive AI libraries. It is NativeAOT-, trim- and browser-WebAssembly-friendly: no reflection, no native code, no `System.Xml` in the document path.

## Status: early preview

What works today is **word-processing documents in the DOCX family**, **workbooks (XLSX family, CSV and TSV)**, and text extraction from OOXML files:

- **`Document`** (`RecursiveAI.OneDocument.Documents`): `Document.Open` (path, stream or byte source; the format comes from the content, never the extension), `Document.Create`, `Save()` (atomic replace), `SaveAs(path)` (format by extension; `.xml` is Flat OPC) and `SaveAs(path|stream, format)`. It opens DOCX, DOCM, DOTX, DOTM and Flat OPC; any other format raises `FormatNotAvailableException` naming it. Untouched parts are written back byte-identical and unedited zip entries are copied as stored; edits keep unknown markup, prefixes, `mc:AlternateContent` and `extLst` in order. The model covers paragraphs and runs, tables, sections, headers and footers, styles with inheritance, numbering, fields, footnotes and endnotes, comments, revisions and content controls, with generated typed views (`RecursiveAI.OneDocument.Schema`) over the same markup.
- **Text extraction** from OOXML documents, workbooks and presentations: `Office.ExtractText(path)` and `onedocument extract <file> [-o out.txt] [--json]`; `Document.ExtractText()` for an open document, edits included.
- **The foundation:** byte sources, one limits object, typed defects and exceptions with locations, content-based format identification, the format registry and the part/package model (OPC on zip and Flat OPC; compound files).

- **`Workbook`** (`RecursiveAI.OneDocument.Spreadsheets`): open, edit and save XLSX, XLSM, XLTX and Flat OPC workbooks in a compact columnar cell store with styles, number formats, tables, conditional formatting, data validation, defined names and the 1900/1904 date systems. Cached values are what you read; there is no calculation yet and opening never recalculates. **Streaming** cursors read and write at constant memory with no allocation per cell, and `Spreadsheet.Read<T>` / `Spreadsheet.Write` bind plain classes and records to XLSX, CSV and TSV through a source generator (no reflection). CSV and TSV have their own reader and writer.

Not done yet: calculation, templating, rendering, sanitising and redaction. Every other format below is planned.

```csharp
using RecursiveAI.OneDocument.Documents;

using var doc = Document.Open("contract.docx");
doc.Paragraphs.First().SetText("Master services agreement");
doc.Save();                      // atomic; only word/document.xml changes
string text = doc.ExtractText();
```

The `Office` statics (`Office.Open`, `Office.ExtractText`, `Office.Convert`) find readers of every kind package the application references; no registration call is needed (each kind package registers itself into the referencing C# project at build time, with no reflection, so NativeAOT and trimming keep only the kinds you use). A kind whose package is not referenced raises `FormatNotAvailableException` naming the format.

## Formats

| Format | Read | Write |
|---|---|---|
| DOCX/DOCM/DOTX/DOTM, Flat OPC | yes | yes |
| XLSX/XLSM/XLTX/XLTM/XLAM, Flat OPC | yes (`Workbook`, streaming cursors, `Spreadsheet.Read<T>`) | yes (also streaming and `Spreadsheet.Write`) |
| PPTX/PPTM/POTX/PPSX, Flat OPC | yes (`Presentation`) | yes |
| XLSB | planned | planned |
| XLS (BIFF8) | planned | planned |
| XLS (BIFF2–8), DOC (incl. Word 6/95), PPT | yes (converted into `Workbook` / `Document` / `Presentation` with a loss report) | not planned (XLS BIFF8: planned) |
| Word 2003 XML, Excel 2003 XML | yes (converted into `Document` / `Workbook` with a loss report) | not planned |
| RTF | yes (`Document`; loss report; `docs/rtf.md`) | yes |
| ODT/ODS/ODP and flat ODF | planned | planned |
| CSV/TSV | yes | yes |
| HTML | yes (`Document.FromHtml`, sanitised, loss report; `docs/html.md`) | yes (documents; workbooks and presentations at a basic level) |
| Markdown (CommonMark, GFM) | yes (`Document`; `docs/markdown.md`) | yes (documents, workbooks, presentations) |
| PDF, images, SVG, text | not applicable | planned (export) |

## Support and measurements (DOCX family)

| Check | Result |
|---|---|
| Untouched open and save, every part byte-identical | 2,763 of 2,763 readable public-web and bug-tracker documents (56,157 parts); unreadable inputs (truncated zips, a duplicate attribute) fail with typed errors |
| One-paragraph edit changes only `word/document.xml` | 188 of 188 |
| Edited output against the ECMA-376 schemas | no new schema errors (files invalid before an edit stay invalid in the same way) |
| Opens in Word without a repair prompt | all 45 generated and edited test documents (tables, sections, comments, notes, fields, revisions authored and resolved, content controls; DOCX, DOTX, DOCM, Flat OPC), Word 365 build 16.0.20527 |
| Fuzzing | every parser 600 s with no findings (libFuzzer via SharpFuzz) |
| Speed against the fastest of the Open XML SDK 3.5.1 and NPOI 2.7.6 | geomean 0.36× (x64) and 0.29× (arm64) over load, load+save, edit and extract; worst row 1.09× (untouched save of a 1,000-page document, arm64). Parts are parsed on first use, so opening reads only the package structure. Method and tables: `bench/README.md` |

Deviations and gaps: band regions of table styles in rows with merged cells are not yet verified against Word; text extraction leaves out embedded objects and `altChunk` content.

## Support and measurements (workbooks and CSV)

| Check | Result |
|---|---|
| Untouched open and save, every part byte-identical | 28,709 of 28,709 parts in 1,211 public-web, bug-tracker and converted workbooks that open (35 more fail with typed errors); 39,199 of 39,199 parts in a second 1,520-file set |
| Editing one cell changes only its sheet part | 1,212 of 1,212 |
| Edits of styles, tables, conditional formatting, data validation and names (five classes) | 1,484 of 1,484 succeed, no new schema-invalid parts on the samples checked |
| Values against an independent converter (LibreOffice) | 208 of 210 workbooks equal (the two differences are volatile `RAND` sheets) |
| Opens in Excel without a repair prompt | 8 of 8 edited workbooks and 3 of 3 streamed files (Excel 365 build 16.0.20527) |
| Number formats against Excel (`TEXT` and cell display) | 7,602 of 7,602 agree in a grid of formats and values; 14 locales verified, 24 locales and 1,350 of 1,350 format names; displayed text 982 of 982 on edited workbooks (caller-side exceptions: `TEXT` argument coercion, the 255-character limit, `#` overflow and `*` fill) |
| CSV and TSV | 89 of 89 government-corpus files field-equal after a rewrite; Excel reads our written files back 162 of 162; Excel values agree on 259 of 262 (the 3 are text that looks like a formula, kept as text on purpose) |
| Streaming reader against the workbook model | equal on 103.5 million corpus cells; the 1M-row by 20-column output validates against the ECMA-376 schemas |
| Fuzzing | every parser 600 s with no findings |
| Loss reports | Writing CSV or TSV reports every dropped feature (formulas, styles, other sheets) with a `loss.` code |
| Memory | 37 retained bytes per cell on a 20M-cell styled workbook (ClosedXML 79, NPOI 360); peak working set 908 MB (ClosedXML 1.7 GB, NPOI 15.5 GB); 0.08 allocated bytes per cell on the streaming read; constant working set from 10k to 1M rows (47 MB and 49 MB peak) |
| Speed against the fastest of Sylvan.Data.Excel 0.5.8, MiniExcel 1.46.0, ExcelDataReader 3.9.0, ClosedXML 0.105.1, NPOI 2.7.6 and EPPlus 4.5.3.3 | geomean 0.53x (x64) and 0.59x (arm64) over streaming read, streaming write, model load and model edit-and-save; worst row 1.05x (streaming read of 100k rows on arm64; 1.01x on x64, both level with Sylvan within noise). Method and tables: `bench/README.md` |

Deviations and gaps: no calculation engine; XLSB, XLS and ODS are not read yet; an Arabic-digit text format is not rendered; saving a sheet larger than 2 GB waits for the streaming writer; `xml:space` on shared-string text is not schema-checked.

## Packages

| Package | Contents |
|---|---|
| `RecursiveAI.OneDocument` | The core: contracts, containers, markup, format registry, OOXML text extraction |
| `RecursiveAI.OneDocument.Documents` | Word-processing documents (DOCX family) |
| `RecursiveAI.OneDocument.Spreadsheets` | Workbooks (XLSX family), streaming, CSV/TSV, typed row binding; calculation not yet |
| `RecursiveAI.OneDocument.Presentations` | Presentations |
| `RecursiveAI.OneDocument.Schema` | Generated typed views of the WordprocessingML elements, over the markup tree |
| `RecursiveAI.OneDocument.Rendering` | Layout and PDF/image output (not yet) |
| `RecursiveAI.OneDocument.Cryptography` | Signatures and encryption on write (not yet) |
| `RecursiveAI.OneDocument.All` | Metapackage |
| `onedocument` | Command-line tool: `onedocument detect <file>`, `onedocument extract <file> [-o out.txt] [--json]`, `onedocument inspect <file> [--json]`, `onedocument sanitize <in> <out> [--all] [--keep <categories>]` |

## Building

```
dotnet build OneDocument.slnx
dotnet test --solution OneDocument.slnx
build/ci.sh            # restore, build, test, NativeAOT smoke, WebAssembly smoke, pack
```

The .NET SDK version is pinned in `global.json`. Build output goes to `artifacts/` (ignored by git); set `ONEDOCUMENT_ARTIFACTS` to put it elsewhere.

## Layout

`src/` packages, `tests/` unit tests, `tools/` harness and generator tools, `fuzz/` libFuzzer targets, `bench/` benchmarks, `smoke/` NativeAOT and WebAssembly smoke apps, `docs/` normative notes (`docs/contracts.md`).

## Licence

`MIT OR Apache-2.0`, © Recursive AI, LLC. See `LICENSE-MIT`, `LICENSE-APACHE` and `THIRD-PARTY-NOTICES.md`.

OneDocument is not affiliated with Microsoft or with any other product or company named OneDocument. Word, Excel and PowerPoint are Microsoft product names used here only to describe the formats.
