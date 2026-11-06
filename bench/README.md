# OneDocument benchmarks

BenchmarkDotNet rows that compare OneDocument with the managed libraries people use today, plus the scripts that run them the same way every time and turn the output into a Markdown page. Comparators are restored as pinned NuGet packages **in this project only** and run through their public APIs; nothing here (or anywhere else in the repository) reads their source.

## Comparators and versions

| Package | Version | Used for |
|---|---|---|
| DocumentFormat.OpenXml | 3.5.1 | DOCX, PPTX, XLSX rows |
| NPOI | 2.7.6 | DOCX, XLSX, XLS rows (2.8 and later are not used: their binary release carries a fee EULA) |
| ClosedXML | 0.105.1 | spreadsheet rows |
| MiniExcel | 1.46.0 | spreadsheet rows |
| Sylvan.Data.Excel | 0.5.8 | spreadsheet rows |
| ExcelDataReader | 3.9.0 | spreadsheet rows |
| EPPlus | 4.5.3.3 | spreadsheet rows; LGPL, so it is referenced from this project only and never shipped |
| ClosedXML.Report | 0.2.12 (its assembly reports 1.0.0) | XLSX template-fill rows |

ShapeCrawler is not restored: its package declares no licence, so the PPTX rows are gated against the Open XML SDK alone. LibreOffice 26.8.2.2 is run as a binary for the information rows (`lo-info.sh`); it is never a gate.

The numbers in a result page come from the assemblies that actually ran (`versions.txt` in the results directory), not from this table.

The pinned comparators bring in transitive packages with published advisories (an image library, drawing and XML-crypto packages). This project is never shipped and reads only the documents it generates itself, so `OneDocument.Bench.csproj` keeps those advisories as visible warnings instead of build errors. Every shipped project keeps them as errors.

## What the rows measure

Each row is one operation on one generated input, with the same work for every library.

| Class | Operation | Rows |
|---|---|---|
| `DocxLoad` | open and read the first paragraph's text | raw zip baseline (read the main part), Open XML SDK (open read-only, load the main part's DOM), NPOI (load the model), OneDocument (`Document.Open`, first paragraph) |
| `DocxLoadSave` | open a package and save it, untouched | raw zip baseline, Open XML SDK (open + clone), NPOI (load + write), OneDocument (`Document.Open`, `SaveAs`) |
| `DocxEdit` | change one paragraph's text, save | raw zip baseline (rewrite one part), Open XML SDK, NPOI, OneDocument |
| `DocxLazyOpen` | open and read one small part | raw zip baseline, Open XML SDK (styles part), NPOI (loads everything), OneDocument (open, styles) |
| `DocxExtract` | read all body text | Open XML SDK DOM, Open XML SDK forward reader, NPOI extractor, OneDocument streaming extractor |

The format rows (`fmtbench`, one fresh process per row and input, same timing and allocation columns as the spreadsheet rows) are:

| Group | Operation | Rows |
|---|---|---|
| `xls-read` | open a `.xls` made by LibreOffice, visit every cell value | OneDocument `Workbook.Open` (full model); NPOI `HSSFWorkbook` (full model, the like-for-like row); Sylvan.Data.Excel and ExcelDataReader forward readers (a lighter job: reported in the strict table, not in the bar) |
| `xlsb-read` | the same on `.xlsb` | OneDocument `Workbook.Open`; Sylvan.Data.Excel and ExcelDataReader forward readers. No managed comparator builds a full model of a binary workbook, so there is no like-for-like row; the strict table shows the gap to the readers |
| `xls-write` | build N rows by 10 columns in the model, save as `.xls` | OneDocument, NPOI |
| `calc-cold`, `calc-incr` | recalculate every formula of a generated workbook (5 formulas per row plus two column totals); then change one input cell and recalculate | OneDocument (`Recalculate`, one thread), ClosedXML, NPOI evaluator, EPPlus. The clock covers the recalculation only (open and edit are outside); ClosedXML marks dependants dirty on a write and evaluates them when a value is read, NPOI is told which cell changed, EPPlus 4.5.3.3 has no dirty tracking and recalculates everything. Every library's totals are compared through the row checksum |
| `pptx` | open and read the first slide; open and save untouched; change one slide's text and save, each to a file and (for the last two) to a stream | OneDocument `Presentation`, Open XML SDK |
| `extract` | all text of a PPTX or XLSX | OneDocument `Office.ExtractText` into a counting writer, Open XML SDK forward reader, NPOI extractor (XLSX) |
| `template` | fill a template with a loop over N items | OneDocument `Template.Render` (XLSX; DOCX is information, no managed comparator is licence-checked), ClosedXML.Report |
| `info` | rendering and legacy readers with no managed comparator | OneDocument DOCX to PDF (substitute fonts), DOC/RTF/ODT text extraction, ODS load; LibreOffice for the first and last two as information |

Inputs: the xlsx values workbooks and the DOCX inputs as before; `values-N.xlsb` converted from them by OneDocument (no other library writes XLSB); `calc-N.xlsx`, `deck-N.pptx` (the one-slide fixture `testdata/slides/slide-runs.pptx` replicated N times) and the templates written by `fmtbench gen`; `.xls`, `.doc`, `.rtf`, `.odt` and `.ods` made by LibreOffice from those (`make-legacy-inputs.sh`), so no library under test makes its own legacy input. A short operation gets a longer warm-up (twenty runs or three seconds) and at least fifteen measured runs, the same for every library; the median is published.

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

`run.sh` refuses to start when the 1-minute load average is above `ONEDOCUMENT_BENCH_MAXLOAD` (default 2.0; `--force` overrides and records that). It writes `$ONEDOCUMENT_RESULTS/<run>/` :

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

Format groups: `bench/run-formats.sh PREFIX GROUP...` (needs `ONEDOCUMENT_RESULTS`, `ONEDOCUMENT_BENCH_INPUTS` and `ONEDOCUMENT_BENCH_DLL`; makes the legacy inputs with LibreOffice when they are missing), or `bench/run.sh --run NAME --fmt GROUP --threads 1`. `bench/lo-info.sh INPUTS OUT.tsv [soffice]` times LibreOffice on the rendering inputs.

### Shared build hosts

On a machine shared with other work, run indicative timings with a limited core count; they are not publishable:

```sh
bench/run.sh --run indicative --filter '*Bench.DocxLoadSave*' --threads 1 --force
```

Published rows are run once per machine, alone on it, in groups of at most 20 minutes. A run that overlapped other work is not publishable.

## Published rows: the 1.0 matrix

Every area of the speed table, measured at the 1.0 code freeze on both machines, **single thread** (`DOTNET_PROCESSOR_COUNT=1`; none of the comparators parallelises a single document, and recalculation was run on one thread on both sides), medians, BenchmarkDotNet 0.15.8 for the document classes and one fresh process per row for the others, .NET 10.0.14 (SDK 10.0.403). Each group ran alone on the machine under the exclusive benchmark queue class (`wq --class bench`, at most 20 minutes). Raw results, `env.txt` (CPU, cores, ISA, storage, load before and after), `load.log` (load every 10 s), `versions.txt` (the assemblies that ran), `inputs.txt` (sizes and SHA-256) and `results.md` (every row with median, minimum, allocations and peak working set for OneDocument and every comparator) are in [`published/x64/<group>/`](published/x64) and [`published/arm64/<group>/`](published/arm64).

| | x64 | arm64 |
|---|---|---|
| Machine | AMD Ryzen 9 7950X3D, 16 cores / 32 threads, AVX2/AVX-512, 125 GB, NVMe, Linux 7.0 | Apple M2 (Mac mini), 4 performance + 4 efficiency cores, 8 GB, NEON, macOS |
| Load average (1 min) at group start | 4.7 to 6.2 (the queue had just drained other projects' jobs; `wq` confirmed the CPU idle before starting) | 2.0 to 2.3 (the machine's idle level) |
| Jobs (worker / Mac id, queue id) | `bwm9ejnr` 005295 (xlsx write, read; its DOCX group failed to build, see below), `8re3zw4q` 005300 (xlsx model, bytes per cell, xls read, xlsb read, xls write), `115on4d8` 005308 (recalculation), `cp17e9hs` 005310 (pptx, extract, template, info, LibreOffice), `xma48xdj` 005336 (DOCX) | `f7gpkthx` 001973 (xlsx write, read; DOCX failed to build), `l9omuc37` (xlsx model, bytes per cell, xls read, xlsb read, xls write), `gkjt2nkx` 001975 (cold recalculation; the group was killed at the 20-minute limit during the incremental one), `d6gw3erd` 001977 (incremental recalculation), `8eot9wg5` 001978 (pptx, extract, template, info), `9k5h9zqj` 001976 (DOCX) |

The first DOCX group on both machines produced no timings: BenchmarkDotNet rebuilds the benchmark project for its child process, and with the larger project that build exceeded its two-minute default under the one-core limit. `run.sh` now passes `--buildTimeout 1500`; both machines re-ran the group (the directories published here are the re-runs). Total bench time was therefore above the usual 45 minutes per machine.

### Ratios against the fastest like-for-like managed comparator (OneDocument median / comparator median)

| Operation | Input | Mode | x64 OneDocument | x64 fastest comparator | x64 ratio | arm64 OneDocument | arm64 fastest comparator | arm64 ratio |
|---|---|---|---:|---|---:|---:|---|---:|
| Recalculation, cold (every formula) | calc-10000.xlsx | single thread | 516.2 ms | npoi.calc.cold 1.11 s | 0.47x | 490.2 ms | npoi.calc.cold 1.22 s | 0.40x |
| Recalculation, cold (every formula) | calc-50000.xlsx | single thread | 1.24 s | epplus.calc.cold 2.30 s | 0.54x | 1.16 s | epplus.calc.cold 3.12 s | 0.37x |
| Recalculation, incremental (one input cell changed) | calc-10000.xlsx | single thread | 0.7 ms | npoi.calc.incr 6.7 ms | 0.10x | 0.4 ms | closedxml.calc.incr 7.7 ms | 0.05x |
| Recalculation, incremental (one input cell changed) | calc-50000.xlsx | single thread | 3.1 ms | npoi.calc.incr 47.0 ms | 0.07x | 4.5 ms | closedxml.calc.incr 45.0 ms | 0.10x |
| DocxEdit | medium | full model | 19.26 ms | OpenXml 100.67 ms | 0.19x | 22.30 ms | OpenXml 70.52 ms | 0.32x |
| DocxEdit | pages1000 | full model | 373.67 ms | OpenXml 812.00 ms | 0.46x | 312.43 ms | OpenXml 1.00 s | 0.31x |
| DocxEdit | small | full model | 766.55 us | OpenXml 1.13 ms | 0.68x | 947.32 us | OpenXml 1.67 ms | 0.57x |
| DocxExtract | medium | streaming extract | 4.39 ms | OpenXmlReader 60.47 ms | 0.07x | 20.17 ms | OpenXmlReader 63.68 ms | 0.32x |
| DocxExtract | pages1000 | streaming extract | 230.17 ms | OpenXmlReader 430.17 ms | 0.54x | 225.82 ms | OpenXmlDom 702.75 ms | 0.32x |
| DocxExtract | small | streaming extract | 566.58 us | OpenXmlDom 992.68 us | 0.57x | 581.98 us | OpenXmlDom 1.18 ms | 0.49x |
| DocxLazyOpen | medium | lazy open | 271.82 us | OpenXml 774.36 us | 0.35x | 319.36 us | OpenXml 734.95 us | 0.43x |
| DocxLazyOpen | pages1000 | lazy open | 270.78 us | OpenXml 1.05 ms | 0.26x | 307.25 us | OpenXml 921.73 us | 0.33x |
| DocxLazyOpen | small | lazy open | 254.50 us | OpenXml 485.01 us | 0.52x | 280.68 us | OpenXml 451.22 us | 0.62x |
| DocxLoad | medium | full model | 9.03 ms | Npoi 66.88 ms | 0.14x | 5.19 ms | Npoi 60.24 ms | 0.09x |
| DocxLoad | pages1000 | full model | 105.67 ms | OpenXml 621.84 ms | 0.17x | 107.04 ms | OpenXml 627.06 ms | 0.17x |
| DocxLoad | small | full model | 385.48 us | OpenXml 962.15 us | 0.40x | 378.91 us | OpenXml 1.14 ms | 0.33x |
| DocxLoadSave | medium | full model | 418.99 us | OpenXml 18.16 ms | 0.02x | 466.98 us | OpenXml 18.55 ms | 0.03x |
| DocxLoadSave | pages1000 | full model | 560.61 us | OpenXml 124.27 ms | 0.00x | 618.46 us | OpenXml 98.36 ms | 0.01x |
| DocxLoadSave | small | full model | 180.61 us | OpenXml 779.93 us | 0.23x | 201.70 us | OpenXml 788.48 us | 0.26x |
| Text extraction, PPTX | deck-50.pptx | streaming extract | 6.1 ms | openxml.pptx.extract 9.0 ms | 0.68x | 6.3 ms | openxml.pptx.extract 11.4 ms | 0.55x |
| Text extraction, PPTX | deck-500.pptx | streaming extract | 55.9 ms | openxml.pptx.extract 88.3 ms | 0.63x | 62.4 ms | openxml.pptx.extract 115.0 ms | 0.54x |
| Text extraction, XLSX | values-10000.xlsx | streaming extract | 35.8 ms | openxml.xlsx.extract 534.7 ms | 0.07x | 26.5 ms | openxml.xlsx.extract 256.9 ms | 0.10x |
| Text extraction, XLSX | values-50000.xlsx | streaming extract | 104.1 ms | openxml.xlsx.extract 1.11 s | 0.09x | 983.7 ms | openxml.xlsx.extract 1.20 s | 0.82x |
| PPTX open, read the first slide | deck-50.pptx | full model | 1.5 ms | openxml.pptx.load 3.5 ms | 0.43x | 1.6 ms | openxml.pptx.load 3.6 ms | 0.44x |
| PPTX open, read the first slide | deck-500.pptx | full model | 13.2 ms | openxml.pptx.load 30.9 ms | 0.43x | 14.1 ms | openxml.pptx.load 33.4 ms | 0.42x |
| PPTX open and save untouched | deck-50.pptx | full model | 7.6 ms | openxml.pptx.loadsave 7.1 ms | 1.07x | 4.8 ms | openxml.pptx.loadsave 7.4 ms | 0.65x |
| PPTX open and save untouched | deck-500.pptx | full model | 16.9 ms | openxml.pptx.loadsave 66.7 ms | 0.25x | 15.3 ms | openxml.pptx.loadsave 72.8 ms | 0.21x |
| PPTX edit one slide's text, save | deck-50.pptx | full model | 8.7 ms | openxml.pptx.edit 3.7 ms | 2.35x | 6.7 ms | openxml.pptx.edit 4.1 ms | 1.63x |
| PPTX edit one slide's text, save | deck-500.pptx | full model | 29.5 ms | openxml.pptx.edit 31.2 ms | 0.95x | 28.4 ms | openxml.pptx.edit 35.3 ms | 0.80x |
| PPTX open and save untouched, to a stream | deck-50.pptx | full model (stream, no fsync) | 1.2 ms | openxml.pptx.loadsave.mem 6.8 ms | 0.18x | 1.2 ms | openxml.pptx.loadsave.mem 6.9 ms | 0.17x |
| PPTX open and save untouched, to a stream | deck-500.pptx | full model (stream, no fsync) | 9.9 ms | openxml.pptx.loadsave.mem 64.2 ms | 0.15x | 11.0 ms | openxml.pptx.loadsave.mem 69.4 ms | 0.16x |
| PPTX edit one slide's text, save to a stream | deck-50.pptx | full model (stream, no fsync) | 2.5 ms | openxml.pptx.edit.mem 2.8 ms | 0.89x | 2.5 ms | openxml.pptx.edit.mem 3.0 ms | 0.83x |
| PPTX edit one slide's text, save to a stream | deck-500.pptx | full model (stream, no fsync) | 22.7 ms | openxml.pptx.edit.mem 24.3 ms | 0.93x | 24.2 ms | openxml.pptx.edit.mem 28.3 ms | 0.86x |
| Template fill, XLSX (loop over N items) | n-1000 | full | 14.7 ms | closedxmlreport.tpl.xlsx 146.9 ms | 0.10x | 11.5 ms | closedxmlreport.tpl.xlsx 160.8 ms | 0.07x |
| Template fill, XLSX (loop over N items) | n-5000 | full | 37.9 ms | closedxmlreport.tpl.xlsx 477.8 ms | 0.08x | 40.0 ms | closedxmlreport.tpl.xlsx 749.7 ms | 0.05x |
| XLS read, full model | values-10000.xls | full model | 47.9 ms | npoi.xls.model 140.7 ms | 0.34x | 56.8 ms | npoi.xls.model 112.5 ms | 0.50x |
| XLS read, full model | values-50000.xls | full model | 99.8 ms | npoi.xls.model 737.3 ms | 0.14x | 278.5 ms | npoi.xls.model 756.6 ms | 0.37x |
| XLS write (build 10 columns per row, save) | n-10000 | full model | 73.3 ms | npoi.xls.write 67.9 ms | 1.08x | 77.0 ms | npoi.xls.write 131.2 ms | 0.59x |
| XLS write (build 10 columns per row, save) | n-50000 | full model | 353.1 ms | npoi.xls.write 744.3 ms | 0.47x | 368.8 ms | npoi.xls.write 773.9 ms | 0.48x |
| Full model: open and visit every cell | values-10000 | full model | 146.1 ms | epplus.model.load 174.8 ms | 0.84x | 164.2 ms | epplus.model.load 191.9 ms | 0.86x |
| Full model: open and visit every cell | values-100000 | full model | 279.9 ms | epplus.model.load 901.0 ms | 0.31x | 260.3 ms | epplus.model.load 836.0 ms | 0.31x |
| Full model: open, change one cell, save | values-10000 | full model | 250.3 ms | epplus.model.editsave 330.9 ms | 0.76x | 253.1 ms | epplus.model.editsave 367.3 ms | 0.69x |
| Full model: open, change one cell, save | values-100000 | full model | 541.1 ms | epplus.model.editsave 1.82 s | 0.30x | 535.9 ms | epplus.model.editsave 1.67 s | 0.32x |
| Streaming read, typed values (every cell) | values-10000 | value streaming | 70.9 ms | sylvan.read 69.1 ms | 1.03x | 78.0 ms | sylvan.read 73.2 ms | 1.07x |
| Streaming read, typed values (every cell) | values-100000 | value streaming | 646.4 ms | sylvan.read 681.1 ms | 0.95x | 761.6 ms | sylvan.read 717.9 ms | 1.06x |
| Streaming read, typed values (every cell) | values-1000000 | value streaming | 1.34 s | sylvan.read 1.75 s | 0.77x | 1.88 s | sylvan.read 2.01 s | 0.94x |
| Streaming write | write-10000 | streaming | 17.8 ms | miniexcel.write 58.3 ms | 0.31x | 22.7 ms | miniexcel.write 66.0 ms | 0.34x |
| Streaming write | write-100000 | streaming | 165.3 ms | miniexcel.write 546.6 ms | 0.30x | 215.0 ms | miniexcel.write 647.2 ms | 0.33x |
| Streaming write | write-1000000 | streaming | 2.29 s | miniexcel.write 3.07 s | 0.75x | 2.59 s | miniexcel.write 3.31 s | 0.78x |

**Geometric mean over the 45 gated rows: 0.29 on x64, 0.32 on arm64.** The gated rows are the DOCX classes, XLSX streaming read and write, XLSX model, XLS read (against NPOI's full model), XLS write, both recalculation modes, PPTX (the file rows) , text extraction and template fill; the "to a stream" PPTX rows repeat the save rows without the durable flush and are published beside them, not counted twice.

**Rows above 1.00**, none of them above 1.25 except the first:
- `PPTX edit one slide's text, save`, 50 slides: **2.35x on x64, 1.63x on arm64**. `Save` and `SaveAs(path)` write a temporary file, flush it to disk and rename it over the target; the flush costs about 5 ms on the worker's NVMe, and the Open XML SDK row edits a copy in place without one. The same operation to a stream, where neither side flushes, is 0.89x and 0.83x, and at 500 slides the file row is 0.95x and 0.80x. The difference is the documented atomic-save contract, a fixed cost that matters only for files of about 100 KB or less (the DOCX edit rows on the small input pass at 0.68x and 0.57x because the DOCX comparator is slower than the PPTX one).
- `PPTX open and save untouched`, 50 slides: 1.07x x64 (same cause); `XLS write`, 10,000 rows: 1.08x x64; `Streaming read`, 10,000 rows: 1.03x x64, 1.07x arm64, and 100,000 rows 1.06x arm64 (level with Sylvan.Data.Excel within the noise of a 70 to 760 ms single-process run; ExcelDataReader is 1.6x to 2.9x and MiniExcel 2.5x to 5.9x slower than OneDocument at every size).

**XLSB and XLS reads against forward readers (not in the bar).** The speed table lists Sylvan.Data.Excel and ExcelDataReader as comparators for binary reads. They are forward value readers; OneDocument has no forward cursor for `.xls` or `.xlsb` (the streaming cursors are XLSX only), so its row builds the full workbook model, a heavier job. The like-for-like row for XLS is NPOI's model (0.34x/0.14x on x64, 0.50x/0.37x on arm64 as above); no managed library builds a model of an XLSB, so that row has no comparator. Against the forward readers (the "strict" tables in each group's `results.md`):

| Operation | Input | x64 OneDocument | x64 Sylvan.Data.Excel | x64 ratio | arm64 ratio |
|---|---|---:|---:|---:|---:|
| XLS read | 10,000 rows | 47.9 ms | 9.1 ms | 5.26x | 5.41x |
| XLS read | 50,000 rows | 99.8 ms | 44.0 ms | 2.27x | 5.40x |
| XLSB read | 10,000 rows | 294.2 ms | 6.9 ms | 42.64x | 10.17x |
| XLSB read | 50,000 rows | 1.10 s | 31.6 ms | 34.73x | 35.67x |

An XLSB opens by converting its records to SpreadsheetML in memory and loading that. A workbook of 1,000,000 XLSX cells loads in 0.28 s while 500,000 XLSB cells take 1.10 s on x64, so the conversion path is where the time goes; the 10,000-row XLSB took 294 ms on x64 and 76 ms on arm64, a spread the single-process harness cannot explain. A forward cursor over the binary records, like the XLSX one, would close the gap to the readers. That is a feature, not a tuning change, and it is listed for the decision.

### Memory (x64 unless marked)

| | OneDocument | ClosedXML | NPOI |
|---|---:|---:|---:|
| Retained bytes per cell, 20M-cell stress workbook | 37.2 | 78.8 | 360.4 |
| Retained bytes per cell, 10M-cell values workbook | 36.1 | 79.7 | 363.2 |
| Retained bytes per cell, 10M-cell values workbook, arm64 | 44.6 | 100.8 | 363.3 |
| Peak working set, 20M cells | 952 MB | 1,740 MB | 15,576 MB |
| Peak working set, 20M cells, arm64 | 1,132 MB | 1,852 MB | 3,739 MB |

The arm64 figure is higher because the macOS collector leaves more of the heap uncompacted when the figure is read; the published budget is the x64 figure. Streaming read holds a constant working set: 52, 49 and 53 MB at 10k, 100k and 1M rows on x64 (65, 63, 66 MB on arm64), 0.08 bytes allocated per cell at 1M rows (the zero-allocation gate is measured separately by the allocation tests). Document peaks (`pages1000`, 1.5 MB file, 11 MB main part), x64: load 94 MB (Open XML SDK 139 MB, NPOI 297 MB); load and save untouched 51 MB (80, 333); edit 103 MB (167, 333); extract 47 MB (108 to 132, 320). All from `published/*/docx/peak.tsv` and the `bpc` groups.

### Information rows (no managed comparator)

x64, medians: DOCX to PDF 40 ms (small) and 1.25 s (medium, 3,000 paragraphs) against LibreOffice 26.8.2.2 headless at 627 ms and 1,428 ms including process start; DOC text extraction 10 ms against LibreOffice's 666 ms conversion; ODS load of 10,000 rows 792 ms against 682 ms; RTF 269 ms and ODT 505 ms extraction. LibreOffice times are one process per conversion (`published/x64/info/lo-info.tsv`). arm64 rows are in `published/arm64/info/`. Template fill of the DOCX template is in `published/*/template/` as information. None of these has a bar; the native ratio target (at most 1.5x) is not gating.

### Setting up another machine

The arm64 numbers come from an Apple M2 Mac mini with a user-local SDK (`dotnet-install.sh --version 10.0.403 --install-dir <dir>`, no administrator rights). Restore needs the pinned preview of the archive library from a local source (`dotnet restore --source <feed dir> --source https://api.nuget.org/v3/index.json`) until it is on nuget.org. `run.sh` reads the load average and CPU details from `sysctl` on macOS.

## Adding a row

1. Add a method to `Rows.cs` that takes the input's `byte[]` and returns a number derived from the result. Register it in `Rows.All` as `library.operation` (OneDocument's rows use the prefix `onedocument.`) and remove its line from `Rows.Pending`.
2. Add a `[Benchmark]` in the matching class of `DocxBenchmarks.cs` (or a new class deriving from `DocxBenchmarkBase`) that calls it. Keep the baseline attribute on the class's first row.
3. Nothing else: the peak pass and the report pick the row up by name.

Use only the public API of the library under test, and do the same work as the other rows in the class. A row that needs an API that does not exist yet stays pending.

## Memory per unit: bytes per cell

`bpc` (`OneDocument.Bench bpc <onedocument|closedxml|npoi> <file.xlsx>`) loads a workbook in a fresh process with one library, touches every cell through the library's public API, collects twice and reports what stays reachable divided by the cell count; `bpcall <dir> <out.tsv>` runs every library over every `.xlsx` in a directory. The inputs are generated by `tools/corpus/make_stress_xlsx.py OUT --rows N` (20 columns: integers, doubles, shared strings, booleans, dates, formatted numbers, ordinary and shared formulas with cached values). Timings in the same row are one run on a loaded host: indicative only.

Final rows (x64 and arm64, `published/*/xlsx-bpc/`) are in the memory table above; the 10,000-row file is 71 retained bytes per cell on x64 and the 100,000-row file 38, because the fixed type-loading cost is spread over few cells.

The small file's higher per-cell figure is fixed cost (type loading and caches, about 7 MB) spread over few cells. OneDocument's store is 15 bytes per cell (19 with a formula) in five parallel arrays; the rest is the interned formula records and the row headers. The shared string table and the other parts are not read unless a string or the part is touched.
