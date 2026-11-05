# OneOffice benchmarks

BenchmarkDotNet rows that compare OneOffice with the managed libraries people use today, plus the scripts that run them the same way every time and turn the output into a Markdown page. Comparators are restored as pinned NuGet packages **in this project only** and run through their public APIs; nothing here (or anywhere else in the repository) reads their source.

## Comparators and versions

| Package | Version | Used for |
|---|---|---|
| DocumentFormat.OpenXml | 3.5.1 | DOCX rows now; PPTX and XLSX rows later |
| NPOI | 2.7.6 | DOCX rows now; XLS rows later (2.8 and later are not used: their binary release carries a fee EULA) |
| ClosedXML | 0.105.1 | spreadsheet rows |
| MiniExcel | 1.46.0 | spreadsheet rows |
| Sylvan.Data.Excel | 0.5.8 | spreadsheet rows |
| ExcelDataReader | 3.9.0 | spreadsheet rows |
| EPPlus | 4.5.3.3 | spreadsheet rows; LGPL, so it is referenced from this project only and never shipped |

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

## Published rows: Word-processing documents (re-measured for the second preview)

Medians of five measured iterations after one warm-up (BenchmarkDotNet 0.15.8, .NET 10.0.14, SDK 10.0.403), one thread visible to the runtime (`--threads 1`). The operations are single-document and none of the comparators uses more than one core, so single-thread is the like-for-like mode; the all-cores mode is not published for these rows. Comparators: Open XML SDK 3.5.1 and NPOI 2.7.6 (XWPF). Inputs: `small`, `medium`, `pages1000` (the 1,000-page document, 11 MB of main-part XML). The `large` input is used only in the peak-memory pass. Like-for-like modes are as in the section above: the SDK's untouched-save row clones the package without parsing the main part, and its load row parses the whole main part.

Each machine ran one timing group and one peak-memory group under the machine's exclusive benchmark queue class, each started alone on the machine. Full result pages, with the load log and per-class tables: [`published/x64/docx/results.md`](published/x64/docx/results.md) and [`published/arm64/docx/results.md`](published/arm64/docx/results.md).

| | x64 | arm64 |
|---|---|---|
| Machine | AMD Ryzen 9 7950X3D, 16 cores / 32 threads, AVX2/AVX-512, 125 GB, NVMe, Linux 7.0 | Apple M2 (Mac mini), 4 performance + 4 efficiency cores, 8 GB, NEON, macOS |
| Load average (1 min) before / after (samples every 10 s in `load.log`) | 1.33 / 1.04 | 2.39 / 2.04 |

The document groups on both machines started under the load limit. The x64 streaming-write group was started with `--force` at load 3.7 and the x64 model groups at 1.7 and 2.3 (the queue had settled; other projects' light jobs kept the load above idle). The x64 streaming-read group was re-run alone under the exclusive benchmark queue class with no override, starting at load 1.81, and replaces the first run; each `env.txt` records the override. Other light work kept the load average above idle during the runs, so treat ratios within a few percent of 1.00 as ties.

### Ratios against the fastest comparator (OneOffice median / comparator median)

| Class | Input | x64 | arm64 |
|---|---|---:|---:|
| DocxLoad | small | 0.33 | 0.36 |
| DocxLoad | medium | 0.14 | 0.05 |
| DocxLoad | pages1000 | 0.16 | 0.17 |
| DocxLoadSave | small | 0.21 | 0.26 |
| DocxLoadSave | medium | 0.02 | 0.03 |
| DocxLoadSave | pages1000 | <0.01 | 0.01 |
| DocxEdit | small | 0.65 | 0.60 |
| DocxEdit | medium | 0.20 | 0.14 |
| DocxEdit | pages1000 | 0.46 | 0.33 |
| DocxExtract | small | 0.55 | 0.52 |
| DocxExtract | medium | 0.15 | 0.09 |
| DocxExtract | pages1000 | 0.48 | 0.32 |
| DocxLazyOpen | small | 0.55 | 0.63 |
| DocxLazyOpen | medium | 0.37 | 0.45 |
| DocxLazyOpen | pages1000 | 0.26 | 0.31 |
| **Geometric mean, 15 rows** | | **0.19** | **0.18** |
| **Worst row** | | 0.65 | 0.63 |

Every row is at the bar (geometric mean at most 1.00, no row above 1.25); the worst row is 0.65x (x64) and 0.63x (arm64). Untouched save no longer parses the main part, so it is near zero against the SDK, which still clones the package.

Opening a document and reading only a small part is now at the bar: the main part is parsed on first use, so `DocxLazyOpen` (open, read the style definitions) is 0.26x to 0.63x of the Open XML SDK on both machines (it was 7x to 110x slower in the first preview, when `Document.Open` parsed the main part). A malformed main part now raises its typed exception at first access. These rows were re-measured for the second preview; the paragraphs before and after this one describe the first preview where they differ.

### Peak memory and allocations (fresh process per row, `pages1000`, 1.5 MB file, 11 MB main part)

Baseline is about 31 MB on both sides (runtime and loaded input). Peak is the process high-water mark; allocated is managed bytes allocated by the row. Values from `published/x64/docx/results.md`.

| Row | OneOffice peak / allocated | Open XML SDK peak / allocated | NPOI peak / allocated |
|---|---|---|---|
| load | 87 MB / 51 MB | 126 MB / 79 MB | 292 MB / 288 MB |
| load + save untouched | 44 MB / 4.5 MB | 74 MB / 31 MB | 329 MB / 348 MB |
| edit one paragraph | 98 MB / 78 MB | 162 MB / 125 MB | 329 MB / 348 MB |
| extract text | 42 MB / 0.6 MB | 102–134 MB / 79–94 MB | 337 MB / 378 MB |

Untouched save no longer parses the main part, so it is also the cheapest row in memory (44 MB peak against 74 MB for the SDK). On arm64 the document peak columns read zero (the runtime does not report a high-water mark on macOS and these groups ran before the harness read it from `/usr/bin/time -l`); the allocated column is valid. The spreadsheet groups on arm64 carry the external figure.

Lazy open, bytes read from the source: `Document.Open` on the 1,000-page document reads 1.9 MB for a 1.5 MB file (the directory, then the main part, with some regions read twice) and 3.1 MB for the 47.6 MB `large` input (6.5%); opening only the package reads 0.2 MB of either.

## Published rows: workbooks, second preview

Commands: `bench/run.sh --run NAME --xlsx read|write|model|bpc --threads 1`; results and machine records in [`published/x64/`](published/x64) and [`published/arm64/`](published/arm64) (one directory per group: `results.md`, `env.txt` with CPU, cores and load before/after, `load.log` sampled every 10 s, `versions.txt`, `inputs.txt` with hashes, the raw `.tsv`). Every group ran under the exclusive benchmark queue class. Each row is one fresh process: one warm-up (three for the whole-model rows, which the runtime only optimises after a few runs), then 5 / 3 / 2 measured repetitions at 10k / 100k / 1M rows; the table gives the median. Peak working set is the process high-water mark (on macOS, `/usr/bin/time -l`). Machines: x64 AMD Ryzen 9 7950X3D, 32 threads, Linux; arm64 Apple M2 Mac mini, 8 cores, macOS. .NET 10.0.14, OneOffice 0.1.0-preview.1, comparators as in `versions.txt`. Single thread is the like-for-like mode: no comparator reads or writes one workbook with more than one core.

Inputs (`xlsxstream gen`, deterministic, built with the BCL zip classes): `values-N`, N rows by 10 columns of numbers, shared strings, booleans and date-styled numbers; and `tools/corpus/make_stress_xlsx.py` workbooks (20 columns with styles and 4M formulas at 1M rows) for bytes per cell.

Like-for-like modes:
- **Streaming read:** every cell's value is read in its typed form. OneOffice: the cursor, text as a string. Sylvan.Data.Excel: `GetExcelDataType` then the typed accessor (its documented typed path; `GetValue` under its default schema returns text, a different job). ExcelDataReader: `GetValue`. MiniExcel: `Query`, rows as dictionaries. The `read.span` row (text left as UTF-8) is informational.
- **Streaming write:** the same 10 cells per row, shared strings where the writer has them. The Open XML SDK row uses `OpenXmlWriter` with inline strings. MiniExcel: `SaveAs` over a lazy enumerable.
- **Full model:** `load` opens and visits every cell value through the library's object model; `editsave` opens, sets one cell and saves to a file. NPOI: `XSSFWorkbook`; ClosedXML: `XLWorkbook`; EPPlus 4.5.3.3: `ExcelPackage`. Timings stop at 100k rows (1M rows takes NPOI about 15 GB); bytes per cell covers 1M.

### Ratios against the fastest managed comparator (OneOffice median / comparator median)

| Operation | Input | x64 | arm64 |
|---|---|---:|---:|
| Streaming read | 10k rows | 1.01 | 1.01 |
| Streaming read | 100k rows | 0.98 | **1.05** |
| Streaming read | 1M rows | 0.80 | 0.94 |
| Streaming write | 10k rows | 0.32 | 0.34 |
| Streaming write | 100k rows | 0.30 | 0.33 |
| Streaming write | 1M rows | 0.72 | 0.80 |
| Model load | 10k rows | 0.83 | 0.88 |
| Model load | 100k rows | 0.17 | 0.31 |
| Model edit + save | 10k rows | 0.75 | 0.70 |
| Model edit + save | 100k rows | 0.28 | 0.31 |
| **Geometric mean, 10 rows** | | **0.53** | **0.59** |
| **Worst row** | | 1.01 | 1.05 |

The streaming read comparator is Sylvan.Data.Excel on every size; write is MiniExcel; the model comparators are EPPlus 4.5.3.3 or ClosedXML. Every row is at the bar (geomean at most 1.00, none above 1.25). Streaming read at 10k and 100k rows is level with Sylvan (within 5%, the noise of a 70 ms to 700 ms single-process run); ExcelDataReader is 1.5x to 2.8x and MiniExcel 2.6x to 5.4x slower than OneOffice. The whole-model 100k rows were re-run with three warm-ups (job IDs in the report) after a first group with one warm-up, whose 100k-row medians for OneOffice were 2x to 6x slower because the runtime had not yet optimised the load path.

### Memory

| | OneOffice | ClosedXML | NPOI |
|---|---:|---:|---:|
| Retained bytes per cell, 20M-cell stress workbook (x64) | 37.2 | 78.8 | 360.4 |
| Retained bytes per cell, 10M-cell values workbook (x64) | 36.1 | 79.7 | 363.2 |
| Peak working set, 20M cells (x64) | 908 MB | 1,736 MB | 15,549 MB |

The small workbooks carry fixed type-loading costs (71 bytes per cell at 100k cells). The arm64 page reports higher retained figures for all three libraries (OneOffice 44.6 bytes per cell at 10M cells, 83.1 at 2M) because the macOS collector leaves more of the heap uncompacted when the figure is read; the ordering and the ratios are the same, and the published budget is the x64 figure.

Streaming read and write hold a constant working set: reading the 10k-row and the 1M-row workbook peaks at 47 MB and 49 MB (x64), 61 MB and 62 MB (arm64); writing 10k and 1M rows peaks at 49 MB and 49 MB (x64). Allocation per cell on the streaming read: 0.08 bytes at 1M rows (the fixed cost of opening, spread over 10M cells; Sylvan 0.19, ExcelDataReader 217, MiniExcel 2,021); the loop itself allocates nothing.

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
