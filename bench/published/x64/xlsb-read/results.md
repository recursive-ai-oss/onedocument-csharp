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
[before] 2026-11-06T08:16:14Z load1/5/15: 1.21 2.85 7.11
[after] 2026-11-06T08:16:32Z load1/5/15: 1.15 2.73 6.97
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:16:14Z load1/5/15: 1.21 2.85 7.11
[during] 2026-11-06T08:16:24Z load1/5/15: 1.18 2.79 7.04
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
| values-10000.xlsb | onedocument.xlsb.model | 5 | 294.2 ms | 290.1 ms | 1.00x | 34.3 MB | 97 MB | 537015189 |
| values-10000.xlsb | exceldatareader.xlsb.read (strict only) | 15 | 25.9 ms | 25.4 ms | 0.09x | 8.2 MB | 67 MB | 537015189 |
| values-10000.xlsb | sylvan.xlsb.read (strict only) | 15 | 6.9 ms | 6.8 ms | 0.02x | 0.5 MB | 59 MB | 537015189 |
| values-50000.xlsb | onedocument.xlsb.model | 3 | 1.10 s | 596.8 ms | 1.00x | 195.0 MB | 239 MB | 4189564533 |
| values-50000.xlsb | exceldatareader.xlsb.read (strict only) | 3 | 328.1 ms | 325.7 ms | 0.30x | 39.6 MB | 99 MB | 4189564533 |
| values-50000.xlsb | sylvan.xlsb.read (strict only) | 15 | 31.6 ms | 31.5 ms | 0.03x | 0.5 MB | 59 MB | 4189564533 |

## Strict: also against the lighter-job comparators (forward value readers against our full model)

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLSB read, full model | values-10000.xlsb | 294.2 ms | sylvan.xlsb.read | 6.9 ms | 42.64x |
| XLSB read, full model | values-50000.xlsb | 1.10 s | sylvan.xlsb.read | 31.6 ms | 34.73x |

Geometric mean of the ratios: 38.480x over 2 rows. Worst row: XLSB read, full model, values-10000.xlsb at 42.64x.

