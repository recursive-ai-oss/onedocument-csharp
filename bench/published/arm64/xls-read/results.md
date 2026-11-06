# Benchmark results

## Machine and load

```text
run: arm64-xls-read
group: fmt xls-read
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T08:35:18Z load1/5/15: 1.95 1.90 1.92
[after] 2026-11-06T08:35:41Z load1/5/15: 2.27 1.99 1.95
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:35:19Z load1/5/15: 1.95 1.90 1.92
[during] 2026-11-06T08:35:29Z load1/5/15: 2.26 1.97 1.94
[during] 2026-11-06T08:35:39Z load1/5/15: 2.29 1.99 1.95
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

## XLS read, full model

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000.xls | onedocument.xls.model | 15 | 56.8 ms | 56.1 ms | 1.00x | 23.6 MB | 85 MB | 537015189 |
| values-10000.xls | exceldatareader.xls.read (strict only) | 15 | 35.8 ms | 35.7 ms | 0.63x | 15.5 MB | 78 MB | 537015189 |
| values-10000.xls | npoi.xls.model | 5 | 112.5 ms | 110.9 ms | 1.98x | 61.6 MB | 117 MB | 537010189 |
| values-10000.xls | sylvan.xls.read (strict only) | 15 | 10.5 ms | 10.4 ms | 0.18x | 0.2 MB | 63 MB | 537015189 |
| values-50000.xls | onedocument.xls.model | 3 | 278.5 ms | 275.0 ms | 1.00x | 111.6 MB | 153 MB | 4189564533 |
| values-50000.xls | exceldatareader.xls.read (strict only) | 3 | 258.8 ms | 257.8 ms | 0.93x | 74.9 MB | 84 MB | 4189564533 |
| values-50000.xls | npoi.xls.model | 3 | 756.6 ms | 749.7 ms | 2.72x | 312.1 MB | 248 MB | 4189539533 |
| values-50000.xls | sylvan.xls.read (strict only) | 15 | 51.6 ms | 51.4 ms | 0.19x | 0.3 MB | 63 MB | 4189564533 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLS read, full model | values-10000.xls | 56.8 ms | npoi.xls.model | 112.5 ms | 0.50x |
| XLS read, full model | values-50000.xls | 278.5 ms | npoi.xls.model | 756.6 ms | 0.37x |

Geometric mean of the ratios: 0.431x over 2 rows. Worst row: XLS read, full model, values-10000.xls at 0.50x.

## Strict: also against the lighter-job comparators (forward value readers against our full model)

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLS read, full model | values-10000.xls | 56.8 ms | sylvan.xls.read | 10.5 ms | 5.41x |
| XLS read, full model | values-50000.xls | 278.5 ms | sylvan.xls.read | 51.6 ms | 5.40x |

Geometric mean of the ratios: 5.403x over 2 rows. Worst row: XLS read, full model, values-10000.xls at 5.41x.

