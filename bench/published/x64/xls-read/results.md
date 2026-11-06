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
[before] 2026-11-06T08:15:51Z load1/5/15: 1.20 2.97 7.23
[after] 2026-11-06T08:16:13Z load1/5/15: 1.21 2.85 7.11
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:15:52Z load1/5/15: 1.20 2.97 7.23
[during] 2026-11-06T08:16:02Z load1/5/15: 1.25 2.92 7.17
[during] 2026-11-06T08:16:12Z load1/5/15: 1.21 2.85 7.11
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
| values-10000.xls | onedocument.xls.model | 15 | 47.9 ms | 47.3 ms | 1.00x | 23.6 MB | 85 MB | 537015189 |
| values-10000.xls | exceldatareader.xls.read (strict only) | 15 | 30.3 ms | 29.9 ms | 0.63x | 15.5 MB | 73 MB | 537015189 |
| values-10000.xls | npoi.xls.model | 5 | 140.7 ms | 136.7 ms | 2.94x | 61.6 MB | 123 MB | 537010189 |
| values-10000.xls | sylvan.xls.read (strict only) | 15 | 9.1 ms | 9.0 ms | 0.19x | 0.2 MB | 54 MB | 537015189 |
| values-50000.xls | onedocument.xls.model | 3 | 99.8 ms | 99.4 ms | 1.00x | 111.6 MB | 142 MB | 4189564533 |
| values-50000.xls | exceldatareader.xls.read (strict only) | 3 | 286.1 ms | 283.4 ms | 2.87x | 74.9 MB | 113 MB | 4189564533 |
| values-50000.xls | npoi.xls.model | 3 | 737.3 ms | 733.9 ms | 7.39x | 312.1 MB | 256 MB | 4189539533 |
| values-50000.xls | sylvan.xls.read (strict only) | 15 | 44.0 ms | 43.6 ms | 0.44x | 0.3 MB | 54 MB | 4189564533 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLS read, full model | values-10000.xls | 47.9 ms | npoi.xls.model | 140.7 ms | 0.34x |
| XLS read, full model | values-50000.xls | 99.8 ms | npoi.xls.model | 737.3 ms | 0.14x |

Geometric mean of the ratios: 0.215x over 2 rows. Worst row: XLS read, full model, values-10000.xls at 0.34x.

## Strict: also against the lighter-job comparators (forward value readers against our full model)

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLS read, full model | values-10000.xls | 47.9 ms | sylvan.xls.read | 9.1 ms | 5.26x |
| XLS read, full model | values-50000.xls | 99.8 ms | sylvan.xls.read | 44.0 ms | 2.27x |

Geometric mean of the ratios: 3.455x over 2 rows. Worst row: XLS read, full model, values-10000.xls at 5.26x.

