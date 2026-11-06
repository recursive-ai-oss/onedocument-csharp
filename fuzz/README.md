# Fuzzing

`fuzz/OneDocument.Fuzz` holds the fuzz targets; SharpFuzz instruments `RecursiveAI.OneDocument.dll` and `RecursiveAI.OneArchive.dll` and libFuzzer drives them. Every parser gets a target here as it is written.

| Target | Input | Surface |
|---|---|---|
| `sniff` | raw bytes | format identification, including the zip and compound-file directory readers it reaches; the same bytes through a memory source and a stream source must give the same answer |
| `opc` | raw bytes | the OPC package reader (zip directory and recovery, content types, relationships, Flat OPC) and writer: every part is read, the package is rewritten and reopened, and the parts must come back with the same bytes |
| `cfb` | raw bytes | the compound-file package adapter (directory, recovery, streams): one stream is replaced, the file is rewritten and reopened, and the other streams must come back unchanged |
| `xml` | raw bytes | the XML tokenizer: tokens tile the input; reading in blocks gives the same tokens as reading whole; refusals are typed |
| `markup` | raw bytes | the markup tree and streaming reader: parse then write gives the input byte for byte; the tree, the reader and the tokenizer agree on what is well-formed; an edit then re-parse stays well-formed |
| `fieldinstr` | UTF-8 text | the field instruction parser: it never fails, keeps the text, and every switch name is one character |
| `fields` | document part, NUL, footnotes part, NUL, comments part | the field, note and comment readers and editors: typed outcomes only, and every edited part parses again |
| `text` | one mode byte + a part | the OOXML text extractors (word-processing, drawing, shared strings and worksheet): typed rejections only, and extracting twice gives the same text |
| `revisions` | raw bytes (a WordprocessingML part) | the revision model: scan, both text views, accept-all, reject-all, one revision, and an authoring call with positions taken from the input; the results re-parse |
| `numfmt` | UTF-8 text (a format code) | the number-format parser: a rejection is a located `numfmt.` defect; an accepted code formats a spread of values in both date systems and several cultures without throwing, deterministically |
| `csv` | one option byte + delimited text | the CSV reader, value parser, writer and workbook import: the same records whether read whole or in 1 to 8 byte pieces, write then read gives the same fields, every field parses as a value, the import then export works; typed rejections only |
| `numfmtfmt` | 8 bytes double, 1 option byte, UTF-8 code | the formatter: string and span results agree, an exactly sized span succeeds, one character less reports `DestinationTooSmall` |
| `formula` | 1 option byte, UTF-8 formula text | the formula parser and printer: a rejection is a located `formula.` error; text that parses prints back to the same characters in file form, and its printed A1, R1C1, display and translated forms parse again |
| `funcs` | 2 bytes pick a function, then bytes for the argument count and values | calls of the math, logical, information and text functions with random arguments over a fixed grid: only a typed `OfficeException` may escape; results are finite, never references, and repeat on a second run |
| `formulaeval` | 1 option byte, UTF-8 formula text | the evaluator over a deterministic grid with small limits: only a typed `OfficeException` may escape; results are finite, never references, and the same on a second run |
| `finfuncs` | function index, argument count, then typed arguments | the financial, engineering and database functions called with random numbers, pool text, ranges over a small database and omitted arguments: only a typed `OfficeException` may escape; number results are finite, never references, and repeatable |
| `statfuncs` | function index byte, argument count byte, one byte per argument | random calls of every registered function over a deterministic grid with a fixed clock: only a typed `OfficeException` may escape; no result is a reference or a non-finite number, and a second evaluation gives the same value |
| `lookups` | a function byte + bytes that make arrays and scalar arguments | the lookup, reference, dynamic-array and LAMBDA-helper functions called over small mixed arrays with small limits: only a typed `OfficeException` may escape, results are never references and repeat on a second run |
| `template` | 1 option byte (strict, delimiters, binder scopes), template text with one unit per line and one segment per 0x01 byte | the template parser and renderer: a rejected template carries located `template.`/`formula.` defects; a valid one renders twice to the same text against fixed data with small limits, and only a typed `OfficeException` may escape |
| `mailmerge` | a field instruction | merges it, as a simple and a complex field, against fixed records in both modes (pictures, `IF`, `NEXTIF`, `SKIPIF`, `SET`/`REF`): only a typed `OfficeException` may escape and the result is deterministic |
| `richtext` | 1 byte (bit 0: html or markdown), the value | the document template's `html`/`markdown` converters: the rendered document saves and opens again; only a typed `OfficeException` may escape |
| `math` | 1 option byte (bit 0: display style, bit 1: a 120 pt measure), OMML as XML | the equation reader, linear text and layout: nothing may throw, and the box extents are finite |
| `smartart` | 1 option byte (bits 0-1: the frame size), then the data model, layout definition, colour transform, quick style and stored drawing of a diagram, separated by zero bytes | the diagram layout engine and painter: nothing may throw, every shape has finite coordinates, and the display lists validate with the stored drawing and with the layout forced |
| `vbacompress` | raw bytes | the VBA compression decoder: it never throws, its answer and its problem agree, and a literal container of the input decodes back to the input |
| `vbadir` | the decompressed `dir` stream | the VBA project reader: the bytes serve as the `dir` stream, as module sources and as the `PROJECT` stream; typed rejections only, and every module can be decoded and exported |
| `vbaproject` | raw bytes (a compound file) | the VBA project reader over any compound file |
| `vbaremove` | 1 layout byte + a stream | the project removers for Word, Excel and PowerPoint compound files: typed rejections only, and the output is a readable compound file |
| `sheetrender` | a worksheet part (the workbook around it is fixed) | print rendering: the sheet is paged (page setup, breaks, titles, headers and footers, merges, conditional formats) and the first three pages painted; typed rejections only |
| `rtftree` | RTF bytes | the RTF group tree (tokenizer included): nothing may throw, and every group lies inside the input |
| `markdownparse` | Markdown text | the CommonMark and GFM parser with both option sets and a small nesting limit: nothing may throw |
| `markdown` | Markdown text | Markdown opened as a document with small limits, written back as Markdown and DOCX and read again: only a typed `OfficeException` may escape |
| `rtf` | RTF bytes | RTF opened as a document with small limits, its text extracted, written as DOCX and RTF and read again (the written RTF comes from the exporter): only a typed `OfficeException` may escape |
| `doc` | a 24-bit length, the `WordDocument` stream, then the table stream (repeated as the `Data` stream) | Word binary files: the streams go into a compound file and through text extraction and the conversion into the model and out as DOCX; typed rejections only, and a `doc.malformed` fallback counts as a finding |
| `sprm` | a property modifier list | the modifier splitter and name lookup accept any bytes |
| `xls` | a legacy workbook (compound file or bare BIFF stream) | the BIFF2-8 converter: records, cells, formulas, styles, drawings, then the converted workbook is walked and written as XLSX; typed rejections only |
| `olepropset` | a property set stream (`\u0005SummaryInformation`, the document summary and user-defined sets) | the property set reader, a write and re-read of what it read, and the mapping to and from the package properties; typed rejections only |
| `oleobject` | a compound file taken as one embedded-object storage | the storage copy, the object description (embedded, linked or package, moniker) and the compound file written back; typed rejections only |
| `ptg` | formula tokens: a version byte, a split byte, then tokens and extra data | the Ptg decoder in every BIFF layout, and for BIFF8 an encode and decode of what it read; typed rejections only |
| `encinfo` | an `EncryptionInfo` stream or a binary encryption header | the standard, agile and extensible parsers and the RC4 headers of Word, Excel and PowerPoint, then a password against each (spin count limited); typed rejections only |
| `filepass` | the body of a `FilePass` record | a small BIFF stream with that record, read as a workbook with a password; typed rejections only |
| `xml2003` | a whole Office 2003 XML file (Word or Excel, by sniffing) | the 2003 readers: opened, saved as DOCX or XLSX, opened again, text extracted; typed rejections only |
| `odfpkg` | a whole OpenDocument file (zip package or flat XML) | the package reader: parts, manifest, styles and metadata walked, written again as a zip and as flat XML; typed rejections only |
| `odt` | a whole OpenDocument text file | the ODT reader and writer: opened as a document, text extracted, saved as ODT and DOCX, the ODT opened again; typed rejections only |
| `html` | an HTML page (bytes, any encoding) | imported with small limits, extracted, written as DOCX and as HTML (page and fragment) and read again with the raw-but-safe switch; typed rejections only |
| `xlsb` | one mode byte, then a replacement for the worksheet, style sheet, shared strings or workbook part of a generated binary workbook, a bare record stream, or a formula token string | the binary layer: the hub opens the book, reads every cell, saves it as SpreadsheetML; the streaming cursor walks it; tokens decode and re-encode; typed rejections only |
| `pptx` | the presentation part, a slide, its layout, the layout's master and a comments part, separated by NUL bytes | the presentation hub wired from those parts: typed rejections only on reading; text, table-cell, add, duplicate, move and delete edits may refuse with argument or state errors, and every part that parsed before an edit parses after it |
| `ppt` | a 4-byte Current User edit offset, then the `PowerPoint Document` stream | the PowerPoint 97-2003 reader: record level, persist directory and edit chain, conversion to PresentationML and the hub over it; only typed exceptions may come out |
| `stream` | one mode byte + a part (worksheet, shared strings or style sheet) | the streaming workbook cursor: every row and cell is read through every accessor, typed rejections only, and a second walk gives the same result |
| `streamwrite` | a script of writer calls | the streaming writer: numbers, any text (controls, lone surrogates), booleans, errors, formulas, gaps and merges are written, and the cursor and the workbook model read back exactly the cells that were written |
| `styles` | a style sheet part | the style model: every cell format resolved, every number format parsed, colours resolved through palette and theme, a format and a differential format added, the part written and read back |
| `sheetfeat` | a worksheet part | tables, conditional formats and validations of a worksheet read in full, then a validation, a rule, a table and a name added and the file written and read back |
| `table` | a table part | a table read, restyled, resized and renamed; refusals are typed or documented argument errors |
| `pivot` | a pivot table definition, or a cache definition and a table definition split by a zero byte | read through the views, refreshed against a small source sheet and written; refusals are typed |

Only `OfficeException` may come out of a library call: it is the typed rejection of bad input. Any other exception, a failed invariant, a hang (`-timeout`) or a memory blow-up (`-rss_limit_mb`) is a finding.

## Running

    export ONEDOCUMENT_FUZZ_TOOLS=/path/to/tools     # holds fuzz/libfuzzer-dotnet and sharpfuzz/sharpfuzz
    export ONEDOCUMENT_FUZZ_WORK=/path/to/workdir    # builds, corpora, findings; must be outside the checkout
    fuzz/run.sh sniff 600                          # fuzz for 10 minutes
    fuzz/run.sh replay sniff some-input...         # run saved inputs once, without libFuzzer

`run.sh` builds the harness in release mode into `$ONEDOCUMENT_FUZZ_WORK/bin`, writes seeds into `$ONEDOCUMENT_FUZZ_WORK/corpus/<target>/`, instruments the library assemblies, and runs libFuzzer. Findings land in `$ONEDOCUMENT_FUZZ_WORK/findings/<target>/`. `replay` runs on the uninstrumented build.

The tools:

* `libfuzzer-dotnet`: the C++ host from the SharpFuzz project, compiled with `clang++ -fsanitize=fuzzer`.
* `sharpfuzz`: `dotnet tool install SharpFuzz.CommandLine --tool-path <dir>/sharpfuzz`.

Reading the output: `ft` (features) is the number to watch. The host's own `cov` stays small because only the .NET side is instrumented. Run one fuzz process at a time.

## Adding a target

Write `static void Run(ReadOnlySpan<byte> data)` that lets only `OfficeException` escape, add it to `Targets` in `Program.cs`, give it seeds and a maximum input length in `run.sh`.

## Findings

Minimize the input, save it under `testdata/fuzz/` (under 1 MB, from our own files), and add a test that feeds it and asserts a typed result.
