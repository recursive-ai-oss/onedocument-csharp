# OneOffice

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

OneOffice is a clean-room document processor for `net10.0` that depends only on the .NET base class library and other Recursive AI libraries. It is NativeAOT-, trim- and browser-WebAssembly-friendly: no reflection, no native code, no `System.Xml` in the document path.

## Status: early preview

What works today is **word-processing documents in the DOCX family**, and text extraction from OOXML files:

- **`Document`** (`RecursiveAI.OneOffice.Documents`): `Document.Open` (path, stream or byte source; the format comes from the content, never the extension), `Document.Create`, `Save()` (atomic replace), `SaveAs(path)` (format by extension; `.xml` is Flat OPC) and `SaveAs(path|stream, format)`. It opens DOCX, DOCM, DOTX, DOTM and Flat OPC; any other format raises `FormatNotAvailableException` naming it. Untouched parts are written back byte-identical and unedited zip entries are copied as stored; edits keep unknown markup, prefixes, `mc:AlternateContent` and `extLst` in order. The model covers paragraphs and runs, tables, sections, headers and footers, styles with inheritance, numbering, fields, footnotes and endnotes, comments, revisions and content controls, with generated typed views (`RecursiveAI.OneOffice.Schema`) over the same markup.
- **Text extraction** from OOXML documents, workbooks and presentations: `Office.ExtractText(path)` and `oneoffice extract <file> [-o out.txt] [--json]`; `Document.ExtractText()` for an open document, edits included.
- **The foundation:** byte sources, one limits object, typed defects and exceptions with locations, content-based format identification, the format registry and the part/package model (OPC on zip and Flat OPC; compound files).

Not done yet: the "opens in Word without a repair prompt" check is pending, and there is no calculation, templating, rendering, sanitising or redaction. Every other format below is planned.

```csharp
using RecursiveAI.OneOffice.Documents;

using var doc = Document.Open("contract.docx");
doc.Paragraphs.First().SetText("Master services agreement");
doc.Save();                      // atomic; only word/document.xml changes
string text = doc.ExtractText();
```

The `Office` statics (`Office.Open`, `Office.ExtractText`, `Office.Convert`) find readers through `FormatRegistry.Default`; an application calls `DocumentFormats.Register(FormatRegistry.Default)` once at start. `Document.Open` and `Office.ExtractText` need no registration.

## Formats

| Format | Read | Write |
|---|---|---|
| DOCX/DOCM/DOTX/DOTM, Flat OPC | yes | yes |
| XLSX/XLSM/XLTX/XLTM/XLAM, Flat OPC | text extraction only | planned |
| PPTX/PPTM/POTX/PPSX, Flat OPC | text extraction only | planned |
| XLSB | planned | planned |
| XLS (BIFF8) | planned | planned |
| XLS (BIFF2–5), DOC, PPT, Word and Excel 2003 XML | planned | not planned |
| RTF | planned | planned |
| ODT/ODS/ODP and flat ODF | planned | planned |
| CSV/TSV | planned | planned |
| HTML, Markdown | planned (import) | planned (export) |
| PDF, images, SVG, text | not applicable | planned (export) |

## Packages

| Package | Contents |
|---|---|
| `RecursiveAI.OneOffice` | The core: contracts, containers, markup, format registry, OOXML text extraction |
| `RecursiveAI.OneOffice.Documents` | Word-processing documents (DOCX family) |
| `RecursiveAI.OneOffice.Spreadsheets` | Workbooks and calculation (not yet) |
| `RecursiveAI.OneOffice.Presentations` | Presentations (not yet) |
| `RecursiveAI.OneOffice.Schema` | Generated typed views of the WordprocessingML elements, over the markup tree |
| `RecursiveAI.OneOffice.Rendering` | Layout and PDF/image output (not yet) |
| `RecursiveAI.OneOffice.Cryptography` | Signatures and encryption on write (not yet) |
| `RecursiveAI.OneOffice.All` | Metapackage |
| `oneoffice` | Command-line tool: `oneoffice detect <file>`, `oneoffice extract <file> [-o out.txt] [--json]` |

## Building

```
dotnet build OneOffice.slnx
dotnet test --solution OneOffice.slnx
build/ci.sh            # restore, build, test, NativeAOT smoke, WebAssembly smoke, pack
```

The .NET SDK version is pinned in `global.json`. Build output goes to `artifacts/` (ignored by git); set `ONEOFFICE_ARTIFACTS` to put it elsewhere.

## Layout

`src/` packages, `tests/` unit tests, `tools/` harness and generator tools, `fuzz/` libFuzzer targets, `bench/` benchmarks, `smoke/` NativeAOT and WebAssembly smoke apps, `docs/` normative notes (`docs/contracts.md`).

## Licence

`MIT OR Apache-2.0`, © Recursive AI, LLC. See `LICENSE-MIT`, `LICENSE-APACHE` and `THIRD-PARTY-NOTICES.md`.

OneOffice is not affiliated with Microsoft or with any other product or company named OneOffice. Word, Excel and PowerPoint are Microsoft product names used here only to describe the formats.
