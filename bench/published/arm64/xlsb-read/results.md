# Benchmark results

## Machine and load

```text
run: arm64-xlsb-read
group: fmt xlsb-read
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T12:03:26Z load1/5/15: 2.26 2.67 2.45
[after] 2026-11-06T12:03:46Z load1/5/15: 2.18 2.63 2.44
```

## Load while running (sampled)

```text
[during] 2026-11-06T12:03:26Z load1/5/15: 2.26 2.67 2.45
[during] 2026-11-06T12:03:36Z load1/5/15: 2.22 2.65 2.45
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

## XLSB read, value streaming

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000.xlsb | onedocument.xlsb.read | 15 | 7.0 ms | 7.0 ms | 1.00x | 2.7 MB | 86 MB | 537015189 |
| values-10000.xlsb | exceldatareader.xlsb.read | 15 | 30.6 ms | 30.5 ms | 4.37x | 8.2 MB | 83 MB | 537015189 |
| values-10000.xlsb | sylvan.xlsb.read | 15 | 7.5 ms | 7.4 ms | 1.07x | 0.5 MB | 76 MB | 537015189 |
| values-50000.xlsb | onedocument.xlsb.read | 15 | 34.9 ms | 34.7 ms | 1.00x | 1.1 MB | 85 MB | 4189564533 |
| values-50000.xlsb | exceldatareader.xlsb.read | 3 | 169.2 ms | 169.0 ms | 4.85x | 39.6 MB | 83 MB | 4189564533 |
| values-50000.xlsb | sylvan.xlsb.read | 15 | 35.9 ms | 35.7 ms | 1.03x | 0.5 MB | 76 MB | 4189564533 |

## Rows with no managed comparator (information)

| Input | Row | Reps | Median | Min | Allocated | Peak working set |
|---|---|---:|---:|---:|---:|---:|
| values-10000.xlsb | onedocument.xlsb.model | 5 | 229.7 ms | 228.9 ms | 34.3 MB | 96 MB |
| values-50000.xlsb | onedocument.xlsb.model | 3 | 1.40 s | 760.0 ms | 195.0 MB | 201 MB |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLSB read, value streaming | values-10000.xlsb | 7.0 ms | sylvan.xlsb.read | 7.5 ms | 0.93x |
| XLSB read, value streaming | values-50000.xlsb | 34.9 ms | sylvan.xlsb.read | 35.9 ms | 0.97x |

Geometric mean of the ratios: 0.953x over 2 rows. Worst row: XLSB read, value streaming, values-50000.xlsb at 0.97x.

