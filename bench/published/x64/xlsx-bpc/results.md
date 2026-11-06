# Benchmark results

## Machine and load

```text
run: x64-xlsx-bpc
group: xlsx bpc
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:11:38Z load1/5/15: 1.56 5.36 9.16
[after] 2026-11-06T08:15:51Z load1/5/15: 1.20 2.97 7.23
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:11:38Z load1/5/15: 1.56 5.36 9.16
[during] 2026-11-06T08:11:48Z load1/5/15: 1.55 5.24 9.07
[during] 2026-11-06T08:11:58Z load1/5/15: 1.47 5.10 8.99
[during] 2026-11-06T08:12:08Z load1/5/15: 1.39 4.96 8.90
[during] 2026-11-06T08:12:18Z load1/5/15: 1.41 4.85 8.82
[during] 2026-11-06T08:12:28Z load1/5/15: 1.34 4.72 8.74
[during] 2026-11-06T08:12:38Z load1/5/15: 1.29 4.60 8.65
[during] 2026-11-06T08:12:48Z load1/5/15: 1.25 4.48 8.57
[during] 2026-11-06T08:12:58Z load1/5/15: 1.21 4.36 8.49
[during] 2026-11-06T08:13:08Z load1/5/15: 1.17 4.25 8.41
[during] 2026-11-06T08:13:18Z load1/5/15: 1.15 4.14 8.33
[during] 2026-11-06T08:13:28Z load1/5/15: 1.12 4.04 8.25
[during] 2026-11-06T08:13:38Z load1/5/15: 1.18 3.95 8.18
[during] 2026-11-06T08:13:48Z load1/5/15: 1.22 3.87 8.11
[during] 2026-11-06T08:13:58Z load1/5/15: 1.19 3.78 8.03
[during] 2026-11-06T08:14:08Z load1/5/15: 1.16 3.69 7.95
[during] 2026-11-06T08:14:18Z load1/5/15: 1.28 3.63 7.89
[during] 2026-11-06T08:14:28Z load1/5/15: 1.31 3.56 7.82
[during] 2026-11-06T08:14:38Z load1/5/15: 1.26 3.47 7.75
[during] 2026-11-06T08:14:48Z load1/5/15: 1.30 3.41 7.68
[during] 2026-11-06T08:14:58Z load1/5/15: 1.25 3.33 7.61
[during] 2026-11-06T08:15:08Z load1/5/15: 1.29 3.27 7.54
[during] 2026-11-06T08:15:18Z load1/5/15: 1.25 3.19 7.47
[during] 2026-11-06T08:15:28Z load1/5/15: 1.21 3.12 7.40
[during] 2026-11-06T08:15:38Z load1/5/15: 1.18 3.05 7.33
[during] 2026-11-06T08:15:48Z load1/5/15: 1.22 3.00 7.27
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

## Memory per cell, full model (fresh process per library and input)

Retained is the managed heap still reachable after the file is loaded, every cell visited and two collections, divided by the cell count; peak is the process high-water mark.

| Input | Cells | Library | Retained bytes/cell | Allocated bytes/cell | Load | Peak working set |
|---|---:|---|---:|---:|---:|---:|
| stress-100000.xlsx | 2,000,020 | onedocument | 37.7 | 51 | 3.33 s | 148 MB |
| stress-100000.xlsx | 2,000,020 | closedxml | 81.2 | 1317 | 12.39 s | 314 MB |
| stress-100000.xlsx | 2,000,020 | npoi | 359.1 | 1047 | 7.84 s | 1633 MB |
| stress-1000000.xlsx | 20,000,020 | onedocument | 37.2 | 50 | 12.34 s | 952 MB |
| stress-1000000.xlsx | 20,000,020 | closedxml | 78.8 | 1286 | 46.19 s | 1740 MB |
| stress-1000000.xlsx | 20,000,020 | npoi | 360.4 | 1050 | 50.24 s | 15576 MB |
| values-10000.xlsx | 100,000 | onedocument | 71.2 | 75 | 224.0 ms | 55 MB |
| values-10000.xlsx | 100,000 | closedxml | 107.6 | 1645 | 850.0 ms | 138 MB |
| values-10000.xlsx | 100,000 | npoi | 363.2 | 1035 | 328.0 ms | 161 MB |
| values-100000.xlsx | 1,000,000 | onedocument | 36.4 | 38 | 1.48 s | 84 MB |
| values-100000.xlsx | 1,000,000 | closedxml | 82.8 | 1604 | 6.74 s | 220 MB |
| values-100000.xlsx | 1,000,000 | npoi | 361.6 | 1033 | 3.14 s | 835 MB |
| values-1000000.xlsx | 10,000,000 | onedocument | 36.1 | 37 | 7.82 s | 404 MB |
| values-1000000.xlsx | 10,000,000 | closedxml | 79.7 | 1572 | 26.39 s | 990 MB |
| values-1000000.xlsx | 10,000,000 | npoi | 363.2 | 1036 | 26.08 s | 7685 MB |
| values-50000.xlsx | 500,000 | onedocument | 36.5 | 38 | 781.0 ms | 66 MB |
| values-50000.xlsx | 500,000 | closedxml | 85.4 | 1607 | 3.24 s | 171 MB |
| values-50000.xlsx | 500,000 | npoi | 361.7 | 1033 | 1.59 s | 464 MB |

