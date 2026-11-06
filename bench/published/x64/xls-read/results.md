# Benchmark results

## Machine and load

```text
run: x64-xls-read
group: fmt xls-read
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T12:22:57Z load1/5/15: 1.76 3.70 7.06
[after] 2026-11-06T12:23:20Z load1/5/15: 1.50 3.48 6.90
```

## Load while running (sampled)

```text
[during] 2026-11-06T12:22:57Z load1/5/15: 1.70 3.65 7.03
[during] 2026-11-06T12:23:07Z load1/5/15: 1.59 3.56 6.96
[during] 2026-11-06T12:23:17Z load1/5/15: 1.50 3.48 6.90
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
| values-10000.xls | onedocument.xls.read | 15 | 5.4 ms | 5.2 ms | 1.00x | 1.0 MB | 64 MB | 537015189 |
| values-10000.xls | exceldatareader.xls.read | 15 | 30.2 ms | 30.0 ms | 5.59x | 15.5 MB | 74 MB | 537015189 |
| values-10000.xls | sylvan.xls.read | 15 | 9.2 ms | 9.1 ms | 1.70x | 0.2 MB | 55 MB | 537015189 |
| values-50000.xls | onedocument.xls.read | 15 | 24.6 ms | 23.9 ms | 1.00x | 1.1 MB | 67 MB | 4189564533 |
| values-50000.xls | exceldatareader.xls.read | 3 | 289.7 ms | 284.7 ms | 11.78x | 74.9 MB | 114 MB | 4189564533 |
| values-50000.xls | sylvan.xls.read | 15 | 44.6 ms | 44.5 ms | 1.81x | 0.3 MB | 55 MB | 4189564533 |

## XLS read, full model

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000.xls | onedocument.xls.model | 15 | 47.6 ms | 46.7 ms | 1.00x | 23.6 MB | 85 MB | 537015189 |
| values-10000.xls | npoi.xls.model | 5 | 135.2 ms | 134.1 ms | 2.84x | 61.6 MB | 123 MB | 537010189 |
| values-50000.xls | onedocument.xls.model | 3 | 101.1 ms | 100.5 ms | 1.00x | 111.6 MB | 142 MB | 4189564533 |
| values-50000.xls | npoi.xls.model | 3 | 749.9 ms | 745.6 ms | 7.42x | 312.1 MB | 256 MB | 4189539533 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLS read, value streaming | values-10000.xls | 5.4 ms | sylvan.xls.read | 9.2 ms | 0.59x |
| XLS read, value streaming | values-50000.xls | 24.6 ms | sylvan.xls.read | 44.6 ms | 0.55x |
| XLS read, full model | values-10000.xls | 47.6 ms | npoi.xls.model | 135.2 ms | 0.35x |
| XLS read, full model | values-50000.xls | 101.1 ms | npoi.xls.model | 749.9 ms | 0.13x |

Geometric mean of the ratios: 0.352x over 4 rows. Worst row: XLS read, value streaming, values-10000.xls at 0.59x.

