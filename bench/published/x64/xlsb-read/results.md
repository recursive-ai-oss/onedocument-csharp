# Benchmark results

## Machine and load

```text
run: x64-xlsb-read
group: fmt xlsb-read
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T12:23:20Z load1/5/15: 1.50 3.48 6.90
[after] 2026-11-06T12:23:40Z load1/5/15: 1.36 3.32 6.77
```

## Load while running (sampled)

```text
[during] 2026-11-06T12:23:21Z load1/5/15: 1.50 3.48 6.90
[during] 2026-11-06T12:23:31Z load1/5/15: 1.42 3.40 6.83
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
| values-10000.xlsb | onedocument.xlsb.read | 15 | 6.9 ms | 6.9 ms | 1.00x | 2.7 MB | 67 MB | 537015189 |
| values-10000.xlsb | exceldatareader.xlsb.read | 15 | 25.7 ms | 25.1 ms | 3.72x | 8.2 MB | 67 MB | 537015189 |
| values-10000.xlsb | sylvan.xlsb.read | 15 | 6.8 ms | 6.8 ms | 0.99x | 0.5 MB | 60 MB | 537015189 |
| values-50000.xlsb | onedocument.xlsb.read | 15 | 32.0 ms | 31.4 ms | 1.00x | 1.1 MB | 66 MB | 4189564533 |
| values-50000.xlsb | exceldatareader.xlsb.read | 3 | 334.0 ms | 333.0 ms | 10.44x | 39.6 MB | 99 MB | 4189564533 |
| values-50000.xlsb | sylvan.xlsb.read | 15 | 32.1 ms | 31.9 ms | 1.00x | 0.5 MB | 60 MB | 4189564533 |

## Rows with no managed comparator (information)

| Input | Row | Reps | Median | Min | Allocated | Peak working set |
|---|---|---:|---:|---:|---:|---:|
| values-10000.xlsb | onedocument.xlsb.model | 5 | 202.5 ms | 199.8 ms | 34.3 MB | 99 MB |
| values-50000.xlsb | onedocument.xlsb.model | 3 | 1.04 s | 723.9 ms | 195.0 MB | 240 MB |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLSB read, value streaming | values-10000.xlsb | 6.9 ms | sylvan.xlsb.read | 6.8 ms | 1.01x |
| XLSB read, value streaming | values-50000.xlsb | 32.0 ms | sylvan.xlsb.read | 32.1 ms | 1.00x |

Geometric mean of the ratios: 1.006x over 2 rows. Worst row: XLSB read, value streaming, values-10000.xlsb at 1.01x.

