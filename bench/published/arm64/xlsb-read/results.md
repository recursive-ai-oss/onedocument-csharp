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
[before] 2026-11-06T08:35:41Z load1/5/15: 2.27 1.99 1.95
[after] 2026-11-06T08:36:00Z load1/5/15: 2.58 2.08 1.98
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:35:42Z load1/5/15: 2.27 1.99 1.95
[during] 2026-11-06T08:35:52Z load1/5/15: 2.60 2.07 1.98
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

## XLSB read, full model

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000.xlsb | onedocument.xlsb.model | 5 | 76.3 ms | 75.5 ms | 1.00x | 34.3 MB | 97 MB | 537015189 |
| values-10000.xlsb | exceldatareader.xlsb.read (strict only) | 15 | 30.2 ms | 30.2 ms | 0.40x | 8.2 MB | 82 MB | 537015189 |
| values-10000.xlsb | sylvan.xlsb.read (strict only) | 15 | 7.5 ms | 7.4 ms | 0.10x | 0.5 MB | 76 MB | 537015189 |
| values-50000.xlsb | onedocument.xlsb.model | 3 | 1.28 s | 908.2 ms | 1.00x | 195.0 MB | 202 MB | 4189564533 |
| values-50000.xlsb | exceldatareader.xlsb.read (strict only) | 3 | 214.8 ms | 214.4 ms | 0.17x | 39.6 MB | 83 MB | 4189564533 |
| values-50000.xlsb | sylvan.xlsb.read (strict only) | 15 | 35.9 ms | 35.7 ms | 0.03x | 0.5 MB | 76 MB | 4189564533 |

## Strict: also against the lighter-job comparators (forward value readers against our full model)

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLSB read, full model | values-10000.xlsb | 76.3 ms | sylvan.xlsb.read | 7.5 ms | 10.17x |
| XLSB read, full model | values-50000.xlsb | 1.28 s | sylvan.xlsb.read | 35.9 ms | 35.67x |

Geometric mean of the ratios: 19.049x over 2 rows. Worst row: XLSB read, full model, values-50000.xlsb at 35.67x.

