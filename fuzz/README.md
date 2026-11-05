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
| `lookups` | a function byte + bytes that make arrays and scalar arguments | the lookup, reference, dynamic-array and LAMBDA-helper functions called over small mixed arrays with small limits: only a typed `OfficeException` may escape, results are never references and repeat on a second run |
| `stream` | one mode byte + a part (worksheet, shared strings or style sheet) | the streaming workbook cursor: every row and cell is read through every accessor, typed rejections only, and a second walk gives the same result |
| `streamwrite` | a script of writer calls | the streaming writer: numbers, any text (controls, lone surrogates), booleans, errors, formulas, gaps and merges are written, and the cursor and the workbook model read back exactly the cells that were written |
| `styles` | a style sheet part | the style model: every cell format resolved, every number format parsed, colours resolved through palette and theme, a format and a differential format added, the part written and read back |
| `sheetfeat` | a worksheet part | tables, conditional formats and validations of a worksheet read in full, then a validation, a rule, a table and a name added and the file written and read back |
| `table` | a table part | a table read, restyled, resized and renamed; refusals are typed or documented argument errors |

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
