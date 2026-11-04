# OneOffice

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

OneOffice is a clean-room document processor for `net10.0` that depends only on the .NET base class library and other Recursive AI libraries. It is NativeAOT-, trim- and browser-WebAssembly-friendly: no reflection, no native code, no `System.Xml` in the document path.

## Status: early preview

This is the repository's foundation. **No document format can be read or written yet.** What exists and works today is the layer everything else is built on:

- **Sources** (`IByteSource`, `ByteSource`): input from a file, stream, memory or a remote object read by ranges, with zero-copy memory access and cancellable async reads.
- **Limits** (`OfficeLimits`): one immutable object of resource limits (zip-bomb ratio, sizes, part and record counts, XML depth, formula depth) on by default and passed through every layer.
- **Defects and exceptions** (`Defect`, `DefectCollection`, `OfficeException` and its subtypes): every repair on read is a typed defect with a location; fatal input raises a typed exception that carries one.
- **Format identification** (`FormatSniffer`, `OfficeFormats`): names the format of a file from its content (zip content types, OpenDocument `mimetype`, compound-file streams, RTF, XML root elements, HTML, delimited text), never from the extension.
- **Format registry** (`FormatRegistry`) and the shared base of the document types (`OfficeFile`) and the `Office` one-liners, which dispatch through the registry. With no kind package registered they raise `FormatNotAvailableException`.
- **Packages** (`IPackage`, `IPackagePart`, `IOpcPackage`, `PartUri`, `AtomicFile`): the part model the container readers will implement, with the untouched-part state and the atomic save.

Nothing here claims PDF output, calculation, templating, sanitising or redaction: none of those exist yet.

## Formats

Every format below is planned; none can be opened or saved yet (the sniffer already recognises them).

| Format | Read | Write |
|---|---|---|
| DOCX/DOCM/DOTX/DOTM, Flat OPC | planned | planned |
| XLSX/XLSM/XLTX/XLTM/XLAM, Flat OPC | planned | planned |
| PPTX/PPTM/POTX/PPSX, Flat OPC | planned | planned |
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
| `RecursiveAI.OneOffice` | The core: contracts, containers, markup, formats registry (the only package with content today) |
| `RecursiveAI.OneOffice.Documents` | Word-processing documents (not yet) |
| `RecursiveAI.OneOffice.Spreadsheets` | Workbooks and calculation (not yet) |
| `RecursiveAI.OneOffice.Presentations` | Presentations (not yet) |
| `RecursiveAI.OneOffice.Schema` | Generated typed views (not yet) |
| `RecursiveAI.OneOffice.Rendering` | Layout and PDF/image output (not yet) |
| `RecursiveAI.OneOffice.Cryptography` | Signatures and encryption on write (not yet) |
| `RecursiveAI.OneOffice.All` | Metapackage |
| `oneoffice` | Command-line tool (`oneoffice detect <file>` today) |

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
