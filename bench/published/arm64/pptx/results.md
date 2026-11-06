# Benchmark results

## Machine and load

```text
run: arm64-pptx
group: fmt pptx
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T09:39:33Z load1/5/15: 2.19 2.05 1.98
[after] 2026-11-06T09:39:50Z load1/5/15: 2.38 2.11 2.00
```

## Load while running (sampled)

```text
[during] 2026-11-06T09:39:33Z load1/5/15: 2.19 2.05 1.98
[during] 2026-11-06T09:39:43Z load1/5/15: 2.45 2.11 2.00
```

## Versions

```text
runtime	.NET 10.0.14
OneDocument	0.1.0-preview.1
DocumentFormat.OpenXml	3.5.1
NPOI	2.7.6
ClosedXML	0.105.1
MiniExcel	1.46.0
Sylvan.Data.Excel	0.5.8
ExcelDataReader	3.9.0
EPPlus	4.5.3.3
ClosedXML.Report	1.0.0
BenchmarkDotNet	0.15.8
```

## PPTX open, read the first slide

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.load | 15 | 1.6 ms | 1.6 ms | 1.00x | 0.7 MB | 65 MB | 0 |
| deck-50.pptx | openxml.pptx.load | 15 | 3.6 ms | 3.6 ms | 2.25x | 3.4 MB | 83 MB | 179 |
| deck-500.pptx | onedocument.pptx.load | 15 | 14.1 ms | 14.0 ms | 1.00x | 5.3 MB | 72 MB | 0 |
| deck-500.pptx | openxml.pptx.load | 15 | 33.4 ms | 32.8 ms | 2.37x | 31.7 MB | 92 MB | 179 |

## PPTX open and save untouched

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.loadsave | 15 | 4.8 ms | 4.6 ms | 1.00x | 0.7 MB | 65 MB | 80691 |
| deck-50.pptx | openxml.pptx.loadsave | 15 | 7.4 ms | 7.4 ms | 1.54x | 5.0 MB | 78 MB | 65337 |
| deck-500.pptx | onedocument.pptx.loadsave | 15 | 15.3 ms | 14.5 ms | 1.00x | 5.0 MB | 71 MB | 747771 |
| deck-500.pptx | openxml.pptx.loadsave | 15 | 72.8 ms | 72.5 ms | 4.76x | 47.9 MB | 89 MB | 604834 |

## PPTX edit one slide's text, save

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.edit | 15 | 6.7 ms | 5.3 ms | 1.00x | 2.2 MB | 68 MB | 80244 |
| deck-50.pptx | openxml.pptx.edit | 15 | 4.1 ms | 4.1 ms | 0.61x | 2.6 MB | 83 MB | 80393 |
| deck-500.pptx | onedocument.pptx.edit | 15 | 28.4 ms | 28.0 ms | 1.00x | 8.3 MB | 76 MB | 747323 |
| deck-500.pptx | openxml.pptx.edit | 15 | 35.3 ms | 34.4 ms | 1.24x | 24.6 MB | 93 MB | 744776 |

## PPTX open and save untouched, to a stream

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.loadsave.mem | 15 | 1.2 ms | 1.2 ms | 1.00x | 0.8 MB | 65 MB | 80691 |
| deck-50.pptx | openxml.pptx.loadsave.mem | 15 | 6.9 ms | 6.9 ms | 5.75x | 5.2 MB | 78 MB | 65337 |
| deck-500.pptx | onedocument.pptx.loadsave.mem | 15 | 11.0 ms | 10.9 ms | 1.00x | 7.7 MB | 73 MB | 747771 |
| deck-500.pptx | openxml.pptx.loadsave.mem | 15 | 69.4 ms | 69.2 ms | 6.31x | 49.4 MB | 90 MB | 604834 |

## PPTX edit one slide's text, save to a stream

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.edit.mem | 15 | 2.5 ms | 2.4 ms | 1.00x | 2.4 MB | 67 MB | 80244 |
| deck-50.pptx | openxml.pptx.edit.mem | 15 | 3.0 ms | 3.0 ms | 1.20x | 2.7 MB | 83 MB | 80393 |
| deck-500.pptx | onedocument.pptx.edit.mem | 15 | 24.2 ms | 24.0 ms | 1.00x | 11.0 MB | 77 MB | 747323 |
| deck-500.pptx | openxml.pptx.edit.mem | 15 | 28.3 ms | 27.4 ms | 1.17x | 26.4 MB | 95 MB | 744776 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| PPTX open, read the first slide | deck-50.pptx | 1.6 ms | openxml.pptx.load | 3.6 ms | 0.44x |
| PPTX open, read the first slide | deck-500.pptx | 14.1 ms | openxml.pptx.load | 33.4 ms | 0.42x |
| PPTX open and save untouched | deck-50.pptx | 4.8 ms | openxml.pptx.loadsave | 7.4 ms | 0.65x |
| PPTX open and save untouched | deck-500.pptx | 15.3 ms | openxml.pptx.loadsave | 72.8 ms | 0.21x |
| PPTX edit one slide's text, save | deck-50.pptx | 6.7 ms | openxml.pptx.edit | 4.1 ms | 1.63x |
| PPTX edit one slide's text, save | deck-500.pptx | 28.4 ms | openxml.pptx.edit | 35.3 ms | 0.80x |
| PPTX open and save untouched, to a stream | deck-50.pptx | 1.2 ms | openxml.pptx.loadsave.mem | 6.9 ms | 0.17x |
| PPTX open and save untouched, to a stream | deck-500.pptx | 11.0 ms | openxml.pptx.loadsave.mem | 69.4 ms | 0.16x |
| PPTX edit one slide's text, save to a stream | deck-50.pptx | 2.5 ms | openxml.pptx.edit.mem | 3.0 ms | 0.83x |
| PPTX edit one slide's text, save to a stream | deck-500.pptx | 24.2 ms | openxml.pptx.edit.mem | 28.3 ms | 0.86x |

Geometric mean of the ratios: 0.481x over 10 rows. Worst row: PPTX edit one slide's text, save, deck-50.pptx at 1.63x.

