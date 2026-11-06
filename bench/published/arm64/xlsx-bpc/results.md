# Benchmark results

## Machine and load

```text
run: arm64-xlsx-bpc
group: xlsx bpc
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T08:19:49Z load1/5/15: 1.85 1.94 1.95
[after] 2026-11-06T08:35:17Z load1/5/15: 1.95 1.90 1.92
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:19:49Z load1/5/15: 1.85 1.94 1.95
[during] 2026-11-06T08:19:59Z load1/5/15: 1.87 1.94 1.95
[during] 2026-11-06T08:20:09Z load1/5/15: 1.81 1.92 1.94
[during] 2026-11-06T08:20:19Z load1/5/15: 1.84 1.92 1.94
[during] 2026-11-06T08:20:29Z load1/5/15: 1.94 1.94 1.95
[during] 2026-11-06T08:20:39Z load1/5/15: 1.95 1.94 1.95
[during] 2026-11-06T08:20:49Z load1/5/15: 1.96 1.95 1.95
[during] 2026-11-06T08:20:59Z load1/5/15: 1.96 1.95 1.95
[during] 2026-11-06T08:21:09Z load1/5/15: 1.97 1.95 1.95
[during] 2026-11-06T08:21:20Z load1/5/15: 1.97 1.95 1.95
[during] 2026-11-06T08:21:30Z load1/5/15: 2.06 1.97 1.95
[during] 2026-11-06T08:21:40Z load1/5/15: 2.60 2.08 1.99
[during] 2026-11-06T08:21:50Z load1/5/15: 2.65 2.11 2.00
[during] 2026-11-06T08:22:00Z load1/5/15: 2.48 2.09 2.00
[during] 2026-11-06T08:22:10Z load1/5/15: 2.33 2.07 1.99
[during] 2026-11-06T08:22:20Z load1/5/15: 2.20 2.05 1.99
[during] 2026-11-06T08:22:30Z load1/5/15: 2.10 2.03 1.98
[during] 2026-11-06T08:22:40Z load1/5/15: 2.16 2.05 1.99
[during] 2026-11-06T08:22:50Z load1/5/15: 2.13 2.05 1.99
[during] 2026-11-06T08:23:00Z load1/5/15: 1.95 2.01 1.97
[during] 2026-11-06T08:23:10Z load1/5/15: 1.96 2.01 1.97
[during] 2026-11-06T08:23:20Z load1/5/15: 2.12 2.04 1.99
[during] 2026-11-06T08:23:30Z load1/5/15: 2.18 2.06 1.99
[during] 2026-11-06T08:23:40Z load1/5/15: 2.88 2.22 2.05
[during] 2026-11-06T08:23:50Z load1/5/15: 2.75 2.21 2.05
[during] 2026-11-06T08:24:00Z load1/5/15: 2.72 2.22 2.05
[during] 2026-11-06T08:24:10Z load1/5/15: 2.53 2.20 2.05
[during] 2026-11-06T08:24:20Z load1/5/15: 2.30 2.16 2.03
[during] 2026-11-06T08:24:30Z load1/5/15: 2.26 2.15 2.03
[during] 2026-11-06T08:24:40Z load1/5/15: 2.14 2.13 2.03
[during] 2026-11-06T08:24:50Z load1/5/15: 2.04 2.11 2.02
[during] 2026-11-06T08:25:00Z load1/5/15: 2.04 2.10 2.02
[during] 2026-11-06T08:25:10Z load1/5/15: 1.88 2.07 2.01
[during] 2026-11-06T08:25:20Z load1/5/15: 1.82 2.05 2.00
[during] 2026-11-06T08:25:30Z load1/5/15: 1.77 2.03 1.99
[during] 2026-11-06T08:25:40Z load1/5/15: 1.65 1.99 1.98
[during] 2026-11-06T08:25:50Z load1/5/15: 1.85 2.03 1.99
[during] 2026-11-06T08:26:00Z load1/5/15: 1.95 2.04 2.00
[during] 2026-11-06T08:26:10Z load1/5/15: 1.95 2.04 2.00
[during] 2026-11-06T08:26:20Z load1/5/15: 1.96 2.04 2.00
[during] 2026-11-06T08:26:30Z load1/5/15: 1.81 2.00 1.99
[during] 2026-11-06T08:26:40Z load1/5/15: 1.76 1.99 1.98
[during] 2026-11-06T08:26:50Z load1/5/15: 1.80 1.99 1.98
[during] 2026-11-06T08:27:00Z load1/5/15: 1.83 1.99 1.98
[during] 2026-11-06T08:27:10Z load1/5/15: 1.62 1.94 1.96
[during] 2026-11-06T08:27:20Z load1/5/15: 1.67 1.94 1.96
[during] 2026-11-06T08:27:30Z load1/5/15: 1.57 1.91 1.95
[during] 2026-11-06T08:27:40Z load1/5/15: 1.63 1.91 1.95
[during] 2026-11-06T08:27:50Z load1/5/15: 1.68 1.91 1.95
[during] 2026-11-06T08:28:00Z load1/5/15: 2.03 1.98 1.98
[during] 2026-11-06T08:28:10Z load1/5/15: 1.80 1.93 1.96
[during] 2026-11-06T08:28:20Z load1/5/15: 1.75 1.92 1.95
[during] 2026-11-06T08:28:30Z load1/5/15: 1.79 1.92 1.95
[during] 2026-11-06T08:28:40Z load1/5/15: 1.82 1.92 1.95
[during] 2026-11-06T08:28:50Z load1/5/15: 1.69 1.89 1.94
[during] 2026-11-06T08:29:00Z load1/5/15: 1.75 1.89 1.94
[during] 2026-11-06T08:29:10Z load1/5/15: 1.78 1.90 1.94
[during] 2026-11-06T08:29:20Z load1/5/15: 2.04 1.95 1.96
[during] 2026-11-06T08:29:30Z load1/5/15: 1.95 1.93 1.95
[during] 2026-11-06T08:29:40Z load1/5/15: 1.96 1.93 1.95
[during] 2026-11-06T08:29:50Z load1/5/15: 2.11 1.97 1.96
[during] 2026-11-06T08:30:00Z load1/5/15: 2.01 1.95 1.96
[during] 2026-11-06T08:30:11Z load1/5/15: 1.93 1.94 1.95
[during] 2026-11-06T08:30:21Z load1/5/15: 1.79 1.91 1.94
[during] 2026-11-06T08:30:31Z load1/5/15: 1.81 1.91 1.94
[during] 2026-11-06T08:30:41Z load1/5/15: 1.84 1.91 1.94
[during] 2026-11-06T08:30:51Z load1/5/15: 1.71 1.88 1.93
[during] 2026-11-06T08:31:01Z load1/5/15: 1.68 1.87 1.92
[during] 2026-11-06T08:31:11Z load1/5/15: 1.73 1.87 1.92
[during] 2026-11-06T08:31:21Z load1/5/15: 1.91 1.91 1.93
[during] 2026-11-06T08:31:31Z load1/5/15: 2.48 2.03 1.98
[during] 2026-11-06T08:31:41Z load1/5/15: 2.48 2.04 1.98
[during] 2026-11-06T08:31:51Z load1/5/15: 2.33 2.02 1.98
[during] 2026-11-06T08:32:01Z load1/5/15: 2.13 1.99 1.96
[during] 2026-11-06T08:32:11Z load1/5/15: 1.95 1.96 1.95
[during] 2026-11-06T08:32:21Z load1/5/15: 1.95 1.96 1.95
[during] 2026-11-06T08:32:31Z load1/5/15: 1.81 1.93 1.94
[during] 2026-11-06T08:32:41Z load1/5/15: 2.08 1.98 1.96
[during] 2026-11-06T08:32:51Z load1/5/15: 1.99 1.96 1.95
[during] 2026-11-06T08:33:01Z load1/5/15: 1.84 1.93 1.94
[during] 2026-11-06T08:33:11Z load1/5/15: 1.78 1.91 1.93
[during] 2026-11-06T08:33:21Z load1/5/15: 1.65 1.88 1.92
[during] 2026-11-06T08:33:31Z load1/5/15: 1.63 1.87 1.92
[during] 2026-11-06T08:33:41Z load1/5/15: 1.76 1.89 1.92
[during] 2026-11-06T08:33:51Z load1/5/15: 1.87 1.91 1.93
[during] 2026-11-06T08:34:01Z load1/5/15: 1.73 1.88 1.92
[during] 2026-11-06T08:34:11Z load1/5/15: 1.85 1.90 1.92
[during] 2026-11-06T08:34:21Z load1/5/15: 1.80 1.89 1.92
[during] 2026-11-06T08:34:31Z load1/5/15: 1.75 1.87 1.91
[during] 2026-11-06T08:34:41Z load1/5/15: 1.80 1.88 1.91
[during] 2026-11-06T08:34:51Z load1/5/15: 1.91 1.90 1.92
[during] 2026-11-06T08:35:01Z load1/5/15: 1.92 1.90 1.92
[during] 2026-11-06T08:35:11Z load1/5/15: 1.86 1.89 1.91
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
| stress-100000.xlsx | 2,000,020 | onedocument | 83.1 | 51 | 3.73 s | 157 MB |
| stress-100000.xlsx | 2,000,020 | closedxml | 165.0 | 1323 | 12.32 s | 295 MB |
| stress-100000.xlsx | 2,000,020 | npoi | 361.0 | 1047 | 7.41 s | 1857 MB |
| stress-1000000.xlsx | 20,000,020 | onedocument | 39.5 | 50 | 12.06 s | 1132 MB |
| stress-1000000.xlsx | 20,000,020 | closedxml | 88.5 | 1287 | 46.62 s | 1852 MB |
| stress-1000000.xlsx | 20,000,020 | npoi | 360.4 | 1050 | 164.95 s | 3739 MB |
| values-10000.xlsx | 100,000 | onedocument | 111.9 | 75 | 229.0 ms | 65 MB |
| values-10000.xlsx | 100,000 | closedxml | 211.2 | 1645 | 1.00 s | 110 MB |
| values-10000.xlsx | 100,000 | npoi | 690.0 | 1035 | 353.0 ms | 160 MB |
| values-100000.xlsx | 1,000,000 | onedocument | 72.3 | 38 | 1.65 s | 95 MB |
| values-100000.xlsx | 1,000,000 | closedxml | 160.3 | 1604 | 6.79 s | 192 MB |
| values-100000.xlsx | 1,000,000 | npoi | 365.7 | 1034 | 3.04 s | 899 MB |
| values-1000000.xlsx | 10,000,000 | onedocument | 44.6 | 37 | 5.12 s | 416 MB |
| values-1000000.xlsx | 10,000,000 | closedxml | 100.8 | 1573 | 29.27 s | 1163 MB |
| values-1000000.xlsx | 10,000,000 | npoi | 363.3 | 1037 | 36.01 s | 4333 MB |
| values-50000.xlsx | 500,000 | onedocument | 73.0 | 38 | 855.0 ms | 76 MB |
| values-50000.xlsx | 500,000 | closedxml | 165.1 | 1608 | 4.02 s | 146 MB |
| values-50000.xlsx | 500,000 | npoi | 366.3 | 1034 | 1.56 s | 485 MB |

