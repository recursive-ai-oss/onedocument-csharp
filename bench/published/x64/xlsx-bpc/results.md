# Benchmark results

## Machine and load

```text
run: x64-xlsx-bpc
filter: *Bench.Docx*
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-05T04:08:22Z load1/5/15: 1.67 4.25 7.08
[after] 2026-11-05T04:12:30Z load1/5/15: 1.44 2.60 5.71
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:08:23Z load1/5/15: 1.67 4.25 7.08
[during] 2026-11-05T04:08:33Z load1/5/15: 1.57 4.15 7.02
[during] 2026-11-05T04:08:43Z load1/5/15: 1.48 4.04 6.95
[during] 2026-11-05T04:08:53Z load1/5/15: 1.41 3.94 6.89
[during] 2026-11-05T04:09:03Z load1/5/15: 1.34 3.84 6.82
[during] 2026-11-05T04:09:13Z load1/5/15: 1.77 3.85 6.79
[during] 2026-11-05T04:09:23Z load1/5/15: 1.65 3.76 6.73
[during] 2026-11-05T04:09:33Z load1/5/15: 1.62 3.68 6.67
[during] 2026-11-05T04:09:43Z load1/5/15: 1.53 3.59 6.61
[during] 2026-11-05T04:09:53Z load1/5/15: 1.45 3.51 6.55
[during] 2026-11-05T04:10:03Z load1/5/15: 1.38 3.42 6.49
[during] 2026-11-05T04:10:13Z load1/5/15: 1.32 3.34 6.43
[during] 2026-11-05T04:10:23Z load1/5/15: 1.34 3.28 6.38
[during] 2026-11-05T04:10:33Z load1/5/15: 1.29 3.21 6.32
[during] 2026-11-05T04:10:43Z load1/5/15: 1.32 3.15 6.27
[during] 2026-11-05T04:10:53Z load1/5/15: 1.27 3.08 6.21
[during] 2026-11-05T04:11:03Z load1/5/15: 1.23 3.01 6.15
[during] 2026-11-05T04:11:13Z load1/5/15: 1.19 2.94 6.10
[during] 2026-11-05T04:11:23Z load1/5/15: 1.31 2.91 6.05
[during] 2026-11-05T04:11:33Z load1/5/15: 1.34 2.86 6.00
[during] 2026-11-05T04:11:43Z load1/5/15: 1.28 2.80 5.95
[during] 2026-11-05T04:11:53Z load1/5/15: 1.32 2.76 5.90
[during] 2026-11-05T04:12:03Z load1/5/15: 1.27 2.70 5.85
[during] 2026-11-05T04:12:13Z load1/5/15: 1.30 2.66 5.80
[during] 2026-11-05T04:12:23Z load1/5/15: 1.26 2.60 5.75
```

## Versions

```text
runtime	.NET 10.0.14
OneOffice	0.1.0-preview.1
DocumentFormat.OpenXml	3.5.1
NPOI	2.7.6
ClosedXML	0.105.1
MiniExcel	1.46.0
Sylvan.Data.Excel	0.5.8
ExcelDataReader	3.9.0
EPPlus	4.5.3.3
BenchmarkDotNet	0.15.8
```

## Memory per cell, full model (fresh process per library and input)

Retained is the managed heap still reachable after the file is loaded, every cell visited and two collections, divided by the cell count; peak is the process high-water mark.

| Input | Cells | Library | Retained bytes/cell | Allocated bytes/cell | Load | Peak working set |
|---|---:|---|---:|---:|---:|---:|
| stress-100000.xlsx | 2,000,020 | oneoffice | 37.7 | 51 | 3.34 s | 139 MB |
| stress-100000.xlsx | 2,000,020 | closedxml | 81.2 | 1315 | 12.34 s | 306 MB |
| stress-100000.xlsx | 2,000,020 | npoi | 359.1 | 1047 | 7.74 s | 1628 MB |
| stress-1000000.xlsx | 20,000,020 | oneoffice | 37.2 | 50 | 12.45 s | 908 MB |
| stress-1000000.xlsx | 20,000,020 | closedxml | 78.8 | 1286 | 46.40 s | 1736 MB |
| stress-1000000.xlsx | 20,000,020 | npoi | 360.4 | 1050 | 50.29 s | 15549 MB |
| values-10000.xlsx | 100,000 | oneoffice | 71.2 | 75 | 208.0 ms | 50 MB |
| values-10000.xlsx | 100,000 | closedxml | 107.9 | 1645 | 868.0 ms | 133 MB |
| values-10000.xlsx | 100,000 | npoi | 363.2 | 1035 | 324.0 ms | 156 MB |
| values-100000.xlsx | 1,000,000 | oneoffice | 36.4 | 38 | 1.48 s | 78 MB |
| values-100000.xlsx | 1,000,000 | closedxml | 82.8 | 1604 | 6.91 s | 215 MB |
| values-100000.xlsx | 1,000,000 | npoi | 361.6 | 1033 | 3.20 s | 831 MB |
| values-1000000.xlsx | 10,000,000 | oneoffice | 36.1 | 37 | 6.83 s | 405 MB |
| values-1000000.xlsx | 10,000,000 | closedxml | 79.7 | 1573 | 27.08 s | 984 MB |
| values-1000000.xlsx | 10,000,000 | npoi | 363.2 | 1036 | 26.63 s | 7678 MB |

