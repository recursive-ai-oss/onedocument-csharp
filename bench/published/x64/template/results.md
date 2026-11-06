# Benchmark results

## Machine and load

```text
run: x64-template
group: fmt template
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:48:51Z load1/5/15: 1.70 1.83 2.97
[after] 2026-11-06T08:49:06Z load1/5/15: 1.55 1.79 2.94
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:48:52Z load1/5/15: 1.70 1.83 2.97
[during] 2026-11-06T08:49:02Z load1/5/15: 1.59 1.80 2.95
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

## Template fill, XLSX (loop over N items)

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| n-1000 | onedocument.tpl.xlsx | 15 | 14.7 ms | 14.5 ms | 1.00x | 8.7 MB | 80 MB | 15071 |
| n-1000 | closedxmlreport.tpl.xlsx | 3 | 146.9 ms | 146.2 ms | 9.99x | 48.9 MB | 196 MB | 25504 |
| n-5000 | onedocument.tpl.xlsx | 15 | 37.9 ms | 37.2 ms | 1.00x | 25.0 MB | 98 MB | 55231 |
| n-5000 | closedxmlreport.tpl.xlsx | 3 | 477.8 ms | 468.0 ms | 12.61x | 210.5 MB | 213 MB | 90792 |

## Rows with no managed comparator (information)

| Input | Row | Reps | Median | Min | Allocated | Peak working set |
|---|---|---:|---:|---:|---:|---:|
| n-1000 | onedocument.tpl.docx | 15 | 17.0 ms | 16.8 ms | 9.5 MB | 79 MB |
| n-5000 | onedocument.tpl.docx | 15 | 61.3 ms | 60.9 ms | 41.4 MB | 115 MB |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Template fill, XLSX (loop over N items) | n-1000 | 14.7 ms | closedxmlreport.tpl.xlsx | 146.9 ms | 0.10x |
| Template fill, XLSX (loop over N items) | n-5000 | 37.9 ms | closedxmlreport.tpl.xlsx | 477.8 ms | 0.08x |

Geometric mean of the ratios: 0.089x over 2 rows. Worst row: Template fill, XLSX (loop over N items), n-1000 at 0.10x.

