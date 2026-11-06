# Benchmark results

## Machine and load

```text
run: x64-pptx
group: fmt pptx
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:47:40Z load1/5/15: 2.96 2.00 3.11
[after] 2026-11-06T08:47:57Z load1/5/15: 2.67 1.98 3.09
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:47:41Z load1/5/15: 2.96 2.00 3.11
[during] 2026-11-06T08:47:51Z load1/5/15: 2.82 2.00 3.10
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
| deck-50.pptx | onedocument.pptx.load | 15 | 1.5 ms | 1.5 ms | 1.00x | 0.7 MB | 56 MB | 0 |
| deck-50.pptx | openxml.pptx.load | 15 | 3.5 ms | 3.5 ms | 2.33x | 3.4 MB | 68 MB | 179 |
| deck-500.pptx | onedocument.pptx.load | 15 | 13.2 ms | 13.1 ms | 1.00x | 5.3 MB | 68 MB | 0 |
| deck-500.pptx | openxml.pptx.load | 15 | 30.9 ms | 30.6 ms | 2.34x | 31.7 MB | 98 MB | 179 |

## PPTX open and save untouched

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.loadsave | 15 | 7.6 ms | 7.3 ms | 1.00x | 0.7 MB | 57 MB | 80691 |
| deck-50.pptx | openxml.pptx.loadsave | 15 | 7.1 ms | 7.1 ms | 0.93x | 5.0 MB | 64 MB | 65337 |
| deck-500.pptx | onedocument.pptx.loadsave | 15 | 16.9 ms | 16.6 ms | 1.00x | 5.0 MB | 68 MB | 747771 |
| deck-500.pptx | openxml.pptx.loadsave | 15 | 66.7 ms | 65.7 ms | 3.95x | 47.9 MB | 112 MB | 604834 |

## PPTX edit one slide's text, save

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.edit | 15 | 8.7 ms | 8.6 ms | 1.00x | 2.2 MB | 60 MB | 80244 |
| deck-50.pptx | openxml.pptx.edit | 15 | 3.7 ms | 3.7 ms | 0.43x | 2.6 MB | 68 MB | 80393 |
| deck-500.pptx | onedocument.pptx.edit | 15 | 29.5 ms | 29.4 ms | 1.00x | 8.3 MB | 72 MB | 747323 |
| deck-500.pptx | openxml.pptx.edit | 15 | 31.2 ms | 31.1 ms | 1.06x | 24.6 MB | 93 MB | 744776 |

## PPTX open and save untouched, to a stream

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.loadsave.mem | 15 | 1.2 ms | 1.1 ms | 1.00x | 0.8 MB | 56 MB | 80691 |
| deck-50.pptx | openxml.pptx.loadsave.mem | 15 | 6.8 ms | 6.7 ms | 5.67x | 5.2 MB | 65 MB | 65337 |
| deck-500.pptx | onedocument.pptx.loadsave.mem | 15 | 9.9 ms | 9.8 ms | 1.00x | 7.7 MB | 69 MB | 747771 |
| deck-500.pptx | openxml.pptx.loadsave.mem | 15 | 64.2 ms | 63.6 ms | 6.48x | 49.3 MB | 113 MB | 604834 |

## PPTX edit one slide's text, save to a stream

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.edit.mem | 15 | 2.5 ms | 2.4 ms | 1.00x | 2.4 MB | 59 MB | 80244 |
| deck-50.pptx | openxml.pptx.edit.mem | 15 | 2.8 ms | 2.8 ms | 1.12x | 2.7 MB | 68 MB | 80393 |
| deck-500.pptx | onedocument.pptx.edit.mem | 15 | 22.7 ms | 22.6 ms | 1.00x | 11.0 MB | 72 MB | 747323 |
| deck-500.pptx | openxml.pptx.edit.mem | 15 | 24.3 ms | 23.5 ms | 1.07x | 26.4 MB | 94 MB | 744776 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| PPTX open, read the first slide | deck-50.pptx | 1.5 ms | openxml.pptx.load | 3.5 ms | 0.43x |
| PPTX open, read the first slide | deck-500.pptx | 13.2 ms | openxml.pptx.load | 30.9 ms | 0.43x |
| PPTX open and save untouched | deck-50.pptx | 7.6 ms | openxml.pptx.loadsave | 7.1 ms | 1.07x |
| PPTX open and save untouched | deck-500.pptx | 16.9 ms | openxml.pptx.loadsave | 66.7 ms | 0.25x |
| PPTX edit one slide's text, save | deck-50.pptx | 8.7 ms | openxml.pptx.edit | 3.7 ms | 2.35x |
| PPTX edit one slide's text, save | deck-500.pptx | 29.5 ms | openxml.pptx.edit | 31.2 ms | 0.95x |
| PPTX open and save untouched, to a stream | deck-50.pptx | 1.2 ms | openxml.pptx.loadsave.mem | 6.8 ms | 0.18x |
| PPTX open and save untouched, to a stream | deck-500.pptx | 9.9 ms | openxml.pptx.loadsave.mem | 64.2 ms | 0.15x |
| PPTX edit one slide's text, save to a stream | deck-50.pptx | 2.5 ms | openxml.pptx.edit.mem | 2.8 ms | 0.89x |
| PPTX edit one slide's text, save to a stream | deck-500.pptx | 22.7 ms | openxml.pptx.edit.mem | 24.3 ms | 0.93x |

Geometric mean of the ratios: 0.549x over 10 rows. Worst row: PPTX edit one slide's text, save, deck-50.pptx at 2.35x.

