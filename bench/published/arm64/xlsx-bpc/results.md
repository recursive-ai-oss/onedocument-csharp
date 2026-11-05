# Benchmark results

## Machine and load

```text
run: arm64-xlsx-bpc
filter: *Bench.Docx*
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-05T04:38:11Z load1/5/15: 1.96 4.54 14.05
[after] 2026-11-05T04:40:17Z load1/5/15: 2.47 3.84 12.47
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:38:13Z load1/5/15: 2.20 4.55 14.00
[during] 2026-11-05T04:38:23Z load1/5/15: 2.10 4.45 13.85
[during] 2026-11-05T04:38:33Z load1/5/15: 2.24 4.40 13.73
[during] 2026-11-05T04:38:43Z load1/5/15: 2.36 4.35 13.60
[during] 2026-11-05T04:38:53Z load1/5/15: 2.46 4.31 13.48
[during] 2026-11-05T04:39:03Z load1/5/15: 2.31 4.21 13.34
[during] 2026-11-05T04:39:13Z load1/5/15: 2.34 4.16 13.21
[during] 2026-11-05T04:39:23Z load1/5/15: 2.31 4.12 13.14
[during] 2026-11-05T04:39:33Z load1/5/15: 2.27 4.05 13.01
[during] 2026-11-05T04:39:43Z load1/5/15: 2.16 3.97 12.88
[during] 2026-11-05T04:39:53Z load1/5/15: 2.29 3.94 12.76
[during] 2026-11-05T04:40:03Z load1/5/15: 2.48 3.92 12.65
[during] 2026-11-05T04:40:13Z load1/5/15: 2.33 3.84 12.52
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
BenchmarkDotNet	0.15.8
```

## Memory per cell, full model (fresh process per library and input)

Retained is the managed heap still reachable after the file is loaded, every cell visited and two collections, divided by the cell count; peak is the process high-water mark.

| Input | Cells | Library | Retained bytes/cell | Allocated bytes/cell | Load | Peak working set |
|---|---:|---|---:|---:|---:|---:|
| stress-100000.xlsx | 2,000,020 | onedocument | 83.1 | 51 | 3.83 s | 154 MB |
| stress-100000.xlsx | 2,000,020 | closedxml | 165.0 | 1323 | 12.44 s | 292 MB |
| stress-100000.xlsx | 2,000,020 | npoi | 361.0 | 1047 | 7.06 s | 1854 MB |
| values-10000.xlsx | 100,000 | onedocument | 111.9 | 75 | 221.0 ms | 62 MB |
| values-10000.xlsx | 100,000 | closedxml | 211.2 | 1645 | 952.0 ms | 106 MB |
| values-10000.xlsx | 100,000 | npoi | 690.1 | 1035 | 347.0 ms | 157 MB |
| values-100000.xlsx | 1,000,000 | onedocument | 72.3 | 38 | 1.67 s | 91 MB |
| values-100000.xlsx | 1,000,000 | closedxml | 160.2 | 1604 | 7.54 s | 188 MB |
| values-100000.xlsx | 1,000,000 | npoi | 365.7 | 1034 | 3.09 s | 895 MB |
| values-1000000.xlsx | 10,000,000 | onedocument | 44.6 | 37 | 5.15 s | 413 MB |
| values-1000000.xlsx | 10,000,000 | closedxml | 100.9 | 1572 | 29.30 s | 1160 MB |
| values-1000000.xlsx | 10,000,000 | npoi | 363.3 | 1036 | 35.56 s | 3201 MB |

