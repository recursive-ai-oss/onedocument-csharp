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
[before] 2026-11-06T12:02:55Z load1/5/15: 2.18 2.70 2.45
[after] 2026-11-06T12:03:19Z load1/5/15: 2.37 2.70 2.46
```

## Load while running (sampled)

```text
[during] 2026-11-06T12:02:55Z load1/5/15: 2.18 2.70 2.45
[during] 2026-11-06T12:03:05Z load1/5/15: 2.39 2.72 2.46
[during] 2026-11-06T12:03:15Z load1/5/15: 2.40 2.72 2.46
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

## XLS read, value streaming

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000.xls | onedocument.xls.read | 15 | 6.7 ms | 6.7 ms | 1.00x | 1.0 MB | 81 MB | 537015189 |
| values-10000.xls | exceldatareader.xls.read | 15 | 35.6 ms | 35.5 ms | 5.31x | 15.5 MB | 78 MB | 537015189 |
| values-10000.xls | sylvan.xls.read | 15 | 10.5 ms | 10.5 ms | 1.57x | 0.2 MB | 63 MB | 537015189 |
| values-50000.xls | onedocument.xls.read | 15 | 31.8 ms | 31.7 ms | 1.00x | 1.1 MB | 85 MB | 4189564533 |
| values-50000.xls | exceldatareader.xls.read | 3 | 249.3 ms | 249.3 ms | 7.84x | 74.9 MB | 84 MB | 4189564533 |
| values-50000.xls | sylvan.xls.read | 15 | 51.5 ms | 51.4 ms | 1.62x | 0.3 MB | 63 MB | 4189564533 |

## XLS read, full model

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000.xls | onedocument.xls.model | 15 | 52.2 ms | 51.5 ms | 1.00x | 23.6 MB | 85 MB | 537015189 |
| values-10000.xls | npoi.xls.model | 15 | 81.7 ms | 77.5 ms | 1.57x | 61.6 MB | 117 MB | 537010189 |
| values-50000.xls | onedocument.xls.model | 3 | 122.0 ms | 121.9 ms | 1.00x | 111.6 MB | 151 MB | 4189564533 |
| values-50000.xls | npoi.xls.model | 3 | 681.4 ms | 552.7 ms | 5.59x | 312.1 MB | 248 MB | 4189539533 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLS read, value streaming | values-10000.xls | 6.7 ms | sylvan.xls.read | 10.5 ms | 0.64x |
| XLS read, value streaming | values-50000.xls | 31.8 ms | sylvan.xls.read | 51.5 ms | 0.62x |
| XLS read, full model | values-10000.xls | 52.2 ms | npoi.xls.model | 81.7 ms | 0.64x |
| XLS read, full model | values-50000.xls | 122.0 ms | npoi.xls.model | 681.4 ms | 0.18x |

Geometric mean of the ratios: 0.461x over 4 rows. Worst row: XLS read, full model, values-10000.xls at 0.64x.

