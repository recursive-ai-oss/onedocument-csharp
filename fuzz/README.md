# Fuzzing

`fuzz/OneOffice.Fuzz` holds the fuzz targets; SharpFuzz instruments `RecursiveAI.OneOffice.dll` and `RecursiveAI.OneArchive.dll` and libFuzzer drives them. Every parser gets a target here as it is written.

| Target | Input | Surface |
|---|---|---|
| `sniff` | raw bytes | format identification, including the zip and compound-file directory readers it reaches; the same bytes through a memory source and a stream source must give the same answer |
| `opc` | raw bytes | the OPC package reader (zip directory and recovery, content types, relationships, Flat OPC) and writer: every part is read, the package is rewritten and reopened, and the parts must come back with the same bytes |
| `cfb` | raw bytes | the compound-file package adapter (directory, recovery, streams): one stream is replaced, the file is rewritten and reopened, and the other streams must come back unchanged |
| `xml` | raw bytes | the XML tokenizer: tokens tile the input; reading in blocks gives the same tokens as reading whole; refusals are typed |
| `markup` | raw bytes | the markup tree and streaming reader: parse then write gives the input byte for byte; the tree, the reader and the tokenizer agree on what is well-formed; an edit then re-parse stays well-formed |
| `fieldinstr` | UTF-8 text | the field instruction parser: it never fails, keeps the text, and every switch name is one character |
| `fields` | document part, NUL, footnotes part, NUL, comments part | the field, note and comment readers and editors: typed outcomes only, and every edited part parses again |
| `revisions` | raw bytes (a WordprocessingML part) | the revision model: scan, both text views, accept-all, reject-all, one revision, and an authoring call with positions taken from the input; the results re-parse |

Only `OfficeException` may come out of a library call: it is the typed rejection of bad input. Any other exception, a failed invariant, a hang (`-timeout`) or a memory blow-up (`-rss_limit_mb`) is a finding.

## Running

    export ONEOFFICE_FUZZ_TOOLS=/path/to/tools     # holds fuzz/libfuzzer-dotnet and sharpfuzz/sharpfuzz
    export ONEOFFICE_FUZZ_WORK=/path/to/workdir    # builds, corpora, findings; must be outside the checkout
    fuzz/run.sh sniff 600                          # fuzz for 10 minutes
    fuzz/run.sh replay sniff some-input...         # run saved inputs once, without libFuzzer

`run.sh` builds the harness in release mode into `$ONEOFFICE_FUZZ_WORK/bin`, writes seeds into `$ONEOFFICE_FUZZ_WORK/corpus/<target>/`, instruments the library assemblies, and runs libFuzzer. Findings land in `$ONEOFFICE_FUZZ_WORK/findings/<target>/`. `replay` runs on the uninstrumented build.

The tools:

* `libfuzzer-dotnet`: the C++ host from the SharpFuzz project, compiled with `clang++ -fsanitize=fuzzer`.
* `sharpfuzz`: `dotnet tool install SharpFuzz.CommandLine --tool-path <dir>/sharpfuzz`.

Reading the output: `ft` (features) is the number to watch. The host's own `cov` stays small because only the .NET side is instrumented. Run one fuzz process at a time.

## Adding a target

Write `static void Run(ReadOnlySpan<byte> data)` that lets only `OfficeException` escape, add it to `Targets` in `Program.cs`, give it seeds and a maximum input length in `run.sh`.

## Findings

Minimize the input, save it under `testdata/fuzz/` (under 1 MB, from our own files), and add a test that feeds it and asserts a typed result.
