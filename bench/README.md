# OneOffice benchmarks

BenchmarkDotNet rows that compare OneOffice with the managed libraries people use today, plus the scripts that run them the same way every time and turn the output into a Markdown page. Comparators are restored as pinned NuGet packages **in this project only** and run through their public APIs; nothing here (or anywhere else in the repository) reads their source.

## Comparators and versions

| Package | Version | Used for |
|---|---|---|
| DocumentFormat.OpenXml | 3.5.1 | DOCX rows now; PPTX and XLSX rows later |
| NPOI | 2.7.6 | DOCX rows now; XLS rows later (2.8 and later are not used: their binary release carries a fee EULA) |
| ClosedXML | 0.105.1 | spreadsheet rows (not wired yet) |
| MiniExcel | 1.46.0 | spreadsheet rows (not wired yet) |
| Sylvan.Data.Excel | 0.5.8 | spreadsheet rows (not wired yet) |
| ExcelDataReader | 3.9.0 | spreadsheet rows (not wired yet) |
| EPPlus | 4.5.3.3 | spreadsheet rows (not wired yet); LGPL, so it is referenced from this project only and never shipped |

The numbers in a result page come from the assemblies that actually ran (`versions.txt` in the results directory), not from this table.

The pinned comparators bring in transitive packages with published advisories (an image library, drawing and XML-crypto packages). This project is never shipped and reads only the documents it generates itself, so `OneOffice.Bench.csproj` keeps those advisories as visible warnings instead of build errors. Every shipped project keeps them as errors.

## What the rows measure

Each row is one operation on one generated input, with the same work for every library.

| Class | Operation | Rows |
|---|---|---|
| `DocxLoad` | open and read the first paragraph's text | raw zip baseline (read the main part), Open XML SDK (open read-only, load the main part's DOM), NPOI (load the model), OneOffice (`Document.Open`, first paragraph) |
| `DocxLoadSave` | open a package and save it, untouched | raw zip baseline, Open XML SDK (open + clone), NPOI (load + write), OneOffice (`Document.Open`, `SaveAs`) |
| `DocxEdit` | change one paragraph's text, save | raw zip baseline (rewrite one part), Open XML SDK, NPOI, OneOffice |
| `DocxLazyOpen` | open and read one small part | raw zip baseline, Open XML SDK (styles part), NPOI (loads everything), OneOffice (open, styles) |
| `DocxExtract` | read all body text | Open XML SDK DOM, Open XML SDK forward reader, NPOI extractor, OneOffice streaming extractor |

The raw zip baseline is the BCL zip classes reading and writing every entry: the floor any tool that recompresses has to pay. It is the baseline row of its class, and ratios in the tables are relative to the first row of each class.

Rows for a layer that does not exist yet are listed by `dotnet <bench.dll> rows` as `pending:`. When a layer lands, its unit adds the row (see below).

### Like-for-like modes

- **Load/save, edit:** whole-document model against whole-document model. The SDK row opens read-only and clones, so it copies the package parts without parsing the main part; NPOI parses and rebuilds its model. The tables show what each library does; neither is made to do more or less.
- **Extraction:** the SDK is measured both through its DOM and through its forward reader, and the faster one is the comparator.
- **Threads:** none of the DOCX comparators parallelises, so `--threads 1` (the runtime sees one core: `DOTNET_PROCESSOR_COUNT=1`) is the like-for-like mode; `--threads all` is recorded for rows where a library does use more cores.

## Inputs

`DocxGenerator` (in `Inputs/`) writes every input from a seed. The same spec gives the same bytes on the same runtime: plain WordprocessingML text from a fixed word list and a fixed-seed xorshift generator, fixed zip timestamps, no clock, no random source, and no comparator library involved.

| Name | Paragraphs | Notes | Size (.NET 10) |
|---|---|---|---|
| `small` | 40 | a page or two | ~5 KB |
| `medium` | 3,000 | plus 600 KB of incompressible embedded parts | ~0.7 MB |
| `large` | 60,000 | plus 45 MB of stored embedded parts | ~48 MB |
| `pages1000` | 35,000 | a hard page break every 35 paragraphs: 1,000 pages | ~1.5 MB |

Every ~40th paragraph is a heading, every ~25th a 3×3 table, and body paragraphs have one to three runs, some bold. `dotnet <bench.dll> gen <dir>` writes the files and a `manifest.tsv` with sizes and SHA-256 hashes; two runs give identical manifests (checked). Hashes can change with the runtime's zlib; the manifest records what each run used. Inputs are never committed.

## Running

```sh
bench/run.sh --run my-run --filter '*Bench.DocxLoadSave*' --threads 1
```

`run.sh` refuses to start when the 1-minute load average is above `ONEOFFICE_BENCH_MAXLOAD` (default 2.0; `--force` overrides and records that). It writes `$ONEOFFICE_RESULTS/<run>/` (default `~/oneoffice/results/bench/<run>/`):

| File | Contents |
|---|---|
| `env.txt` | CPU model, cores, ISA flags, memory, storage, SDK, load before and after |
| `load.log` | load average sampled every 10 s during the run |
| `versions.txt` | runtime and the library versions that ran |
| `inputs.txt` | sizes and hashes of the inputs used |
| `bdn/results/*-report-full.json` | BenchmarkDotNet's full results: median, statistics, allocations |
| `peak.tsv` | one fresh process per row and input: working set after loading the input, peak working set, managed bytes allocated |
| `results.md` | the page built from all of the above (`dotnet <bench.dll> report <dir>` rebuilds it) |

Peak memory comes from a separate pass because BenchmarkDotNet runs many iterations in one process. Each `peak` run starts a new process, loads the input bytes, runs the row once and reads the process high-water mark, so a large row never inherits another row's peak.

Each class runs one launch with one warm-up and five measured iterations. A filtered group (one class, all sizes) stays well under 20 minutes; run classes separately rather than all at once.

### Under the worker queue

Timing runs that are not the final published numbers go through the heavy class, limited to four cores:

```sh
wq run --class heavy --cores 4 --max 20m --label "oneoffice bench indicative" -- \
  bench/run.sh --run indicative --filter '*Bench.DocxLoadSave*' --threads 1 --force
```

Published rows use the bench class, once per machine, in groups of at most 20 minutes. A run that overlapped other work is not publishable.

## Published rows: Word-processing documents, first preview

Medians of five measured iterations after one warm-up (BenchmarkDotNet 0.15.8, .NET 10.0.14, SDK 10.0.403), one thread visible to the runtime (`--threads 1`). The operations are single-document and none of the comparators uses more than one core, so single-thread is the like-for-like mode; the all-cores mode is not published for these rows. Comparators: Open XML SDK 3.5.1 and NPOI 2.7.6 (XWPF). Inputs: `small`, `medium`, `pages1000` (the 1,000-page document, 11 MB of main-part XML). The `large` input is used only in the peak-memory pass. Like-for-like modes are as in the section above: the SDK's untouched-save row clones the package without parsing the main part, and its load row parses the whole main part.

Each machine ran one timing group and one peak-memory group under the machine's exclusive benchmark queue class, each started alone on the machine. Full result pages, with the load log and per-class tables: [`published/x64-timing.md`](published/x64-timing.md), [`published/x64-peak.md`](published/x64-peak.md), [`published/arm64-timing.md`](published/arm64-timing.md), [`published/arm64-peak.md`](published/arm64-peak.md).

| | x64 | arm64 |
|---|---|---|
| Machine | AMD Ryzen 9 7950X3D, 16 cores / 32 threads, AVX2/AVX-512, 125 GB, NVMe, Linux 7.0 | Apple M2 (Mac mini), 4 performance + 4 efficiency cores, 8 GB, NEON, macOS |
| Load average (1 min) before / during (min–max of 48 samples) / after | 1.74 / 1.43–4.43 / 1.63 | 1.29 / 1.35–2.25 (47 samples) / 2.11 |

The x64 peak-memory group started at a load average of 2.39 (limit 2.0) and was forced; memory figures do not depend on load. Other light work on both machines kept the load average above idle during the runs (the start check only guarantees an idle machine at the start), so treat ratios within a few percent of 1.00 as ties.

### Ratios against the fastest comparator (OneOffice median / comparator median)

| Class | Input | x64 | arm64 |
|---|---|---:|---:|
| DocxLoad | small | 0.38 | 0.34 |
| DocxLoad | medium | 0.14 | 0.07 |
| DocxLoad | pages1000 | 0.16 | 0.17 |
| DocxLoadSave | small | 0.49 | 0.57 |
| DocxLoadSave | medium | 0.69 | 0.19 |
| DocxLoadSave | pages1000 | **0.93** | **1.09** |
| DocxEdit | small | 0.85 | 0.59 |
| DocxEdit | medium | 0.21 | 0.26 |
| DocxEdit | pages1000 | 0.44 | 0.33 |
| DocxExtract | small | 0.61 | 0.52 |
| DocxExtract | medium | 0.07 | 0.07 |
| DocxExtract | pages1000 | 0.47 | 0.33 |
| **Geometric mean, 12 rows** | | **0.36** | **0.29** |
| **Worst row** | | 0.93 | 1.09 |

Every row is at the bar (geometric mean at most 1.00, no row above 1.25). The tightest rows are untouched save of the 1,000-page document: it is within noise of the SDK, which copies the package without reading the main part, while OneOffice reads and checks the 11 MB main part on open. About 85% of that row's time is the XML tokenizer; one pass of tuning in this release (skipping the namespace-declaration pass for tags that declare none) took the tokenizer from 110 ms to 95 ms on the same machine.

**Opening a document and reading only a small part is not at the bar.** The `DocxLazyOpen` class opens a document and reads the style definitions. The SDK does not touch the main part for this; OneOffice's `Document.Open` parses and checks the main part (it must be well-formed XML), so on `medium` it is 7.4x (x64) and 12.5x (arm64) slower and on `pages1000` 110x and 99x (111 ms against 1.0 ms), while on `small` it is level (0.92x, 0.97x). Package-level access (`OpcPackage.Open`) is lazy and reads about 0.2 MB of a 48 MB file; it is the document hub that parses the main part up front. This row is not in the closing set above because the lazy-open comparison is between a hub that validates the main part and a package reader that does not, but it is a real gap for anything that wants only a style or a property of a large file. Making the main-part parse lazy would also bring untouched save of large documents well under the SDK; it changes when a malformed main part is reported (at first use instead of at open), so it is a design decision, not a tuning change.

### Peak memory and allocations (fresh process per row, `pages1000`, 1.5 MB file, 11 MB main part)

Baseline is about 31 MB on both sides (runtime and loaded input). Peak is the process high-water mark; allocated is managed bytes allocated by the row. Values from `published/x64-peak.md`.

| Row | OneOffice peak / allocated | Open XML SDK peak / allocated | NPOI peak / allocated |
|---|---|---|---|
| load | 91 MB / 53 MB | 132 MB / 83 MB | 305 MB / 302 MB |
| load + save untouched | 95 MB / 58 MB | 78 MB / 33 MB | 360 MB / 365 MB |
| edit one paragraph | 102 MB / 82 MB | 169 MB / 132 MB | 361 MB / 365 MB |
| extract text | 44 MB / 0.6 MB | 108–141 MB / 83–99 MB | 353 MB / 396 MB |

Untouched save is the one row where OneOffice uses more memory than the SDK (1.2x peak, 1.8x allocated), for the same reason as above. On arm64 the peak columns read zero because the runtime does not report a high-water mark on macOS; the allocated column is valid (`published/arm64-peak.md` has the same figures as x64 to within 1%).

Lazy open, bytes read from the source: `Document.Open` on the 1,000-page document reads 1.9 MB for a 1.5 MB file (the directory, then the main part, with some regions read twice) and 3.1 MB for the 47.6 MB `large` input (6.5%); opening only the package reads 0.2 MB of either.

### Setting up another machine

The arm64 numbers come from an Apple M2 Mac mini with a user-local SDK (`dotnet-install.sh --version 10.0.403 --install-dir <dir>`, no administrator rights). Restore needs the pinned preview of the archive library from a local source (`dotnet restore --source <feed dir> --source https://api.nuget.org/v3/index.json`) until it is on nuget.org. `run.sh` reads the load average and CPU details from `sysctl` on macOS.

## Adding a row

1. Add a method to `Rows.cs` that takes the input's `byte[]` and returns a number derived from the result. Register it in `Rows.All` as `library.operation` (OneOffice's rows use the prefix `oneoffice.`) and remove its line from `Rows.Pending`.
2. Add a `[Benchmark]` in the matching class of `DocxBenchmarks.cs` (or a new class deriving from `DocxBenchmarkBase`) that calls it. Keep the baseline attribute on the class's first row.
3. Nothing else: the peak pass and the report pick the row up by name.

Use only the public API of the library under test, and do the same work as the other rows in the class. A row that needs an API that does not exist yet stays pending.

## Memory per unit: bytes per cell

`bpc` (`OneOffice.Bench bpc <oneoffice|closedxml|npoi> <file.xlsx>`) loads a workbook in a fresh process with one library, touches every cell through the library's public API, collects twice and reports what stays reachable divided by the cell count; `bpcall <dir> <out.tsv>` runs every library over every `.xlsx` in a directory. The inputs are generated by `tools/corpus/make_stress_xlsx.py OUT --rows N` (20 columns: integers, doubles, shared strings, booleans, dates, formatted numbers, ordinary and shared formulas with cached values). Timings in the same row are one run on a loaded host: indicative only.

Indicative run, 2026-11-04, a shared build host (AMD Ryzen 9 7950X3D, x64, 32 logical cores, 124 GB, NVMe, shared with other jobs, load 10-12 during the run), .NET 10.0.14, OneOffice 0.1.0-preview.1, ClosedXML 0.105.1, NPOI 2.7.6 (one process per cell, `wq --class heavy`, not a bench slot):

| Cells | Library | Retained bytes/cell | Allocated bytes/cell | Load ms | Peak working set MB |
|---|---|---|---|---|---|
| 200,020 (10k rows) | OneOffice | 75.2 | 91 | 618 | 68 |
| | ClosedXML | 105.4 | 1,365 | 2,107 | 155 |
| | NPOI | 361.3 | 1,053 | 1,277 | 263 |
| 2,000,020 (100k rows) | OneOffice | 37.7 | 51 | 1,477 | 142 |
| | ClosedXML | 81.2 | 1,292 | 5,077 | 304 |
| | NPOI | 359.1 | 1,047 | 6,158 | 1,631 |
| 20,000,020 (1M rows) | OneOffice | 37.2 | 50 | 8,857 | 920 |
| | ClosedXML | 78.8 | 1,284 | 45,345 | 1,729 |
| | NPOI | 360.4 | 1,049 | 47,589 | 15,491 |

The small file's higher per-cell figure is fixed cost (type loading and caches, about 7 MB) spread over few cells. OneOffice's store is 15 bytes per cell (19 with a formula) in five parallel arrays; the rest is the interned formula records and the row headers. The shared string table and the other parts are not read unless a string or the part is touched. These are not final rows: they are not a bench-class run and carry no bar.
