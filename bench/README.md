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
| `DocxLoadSave` | open a package and save it, untouched | raw zip baseline, Open XML SDK (open + clone), NPOI (load + write) |
| `DocxEdit` | change one paragraph's text, save | raw zip baseline (rewrite one part), Open XML SDK, NPOI |
| `DocxLazyOpen` | open and read one small part | raw zip baseline, Open XML SDK (styles part), NPOI (loads everything) |
| `DocxExtract` | read all body text | Open XML SDK DOM, Open XML SDK forward reader, NPOI extractor |

The raw zip baseline is the BCL zip classes reading and writing every entry: the floor any tool that recompresses has to pay. It is the baseline row of its class, and ratios in the tables are relative to the first row of each class.

Rows for OneOffice's own layers are listed by `dotnet <bench.dll> rows` as `pending:` until the layer exists. When a layer lands, its unit adds the row (see below).

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

## Adding a row

1. Add a method to `Rows.cs` that takes the input's `byte[]` and returns a number derived from the result. Register it in `Rows.All` as `library.operation` (OneOffice's rows use the prefix `oneoffice.`) and remove its line from `Rows.Pending`.
2. Add a `[Benchmark]` in the matching class of `DocxBenchmarks.cs` (or a new class deriving from `DocxBenchmarkBase`) that calls it. Keep the baseline attribute on the class's first row.
3. Nothing else: the peak pass and the report pick the row up by name.

Use only the public API of the library under test, and do the same work as the other rows in the class. A row that needs an API that does not exist yet stays pending.

## Memory per unit

`Footprint.Measure` is the shape of the bytes-per-cell and bytes-per-node measurement: it builds a model, forces collections, and reports retained and allocated bytes per unit. It will be wired to the spreadsheet model against ClosedXML and NPOI when that model exists.
