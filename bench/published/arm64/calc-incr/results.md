# Benchmark results

## Machine and load

```text
run: arm64-calc-incr
group: fmt calc-incr
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T09:19:32Z load1/5/15: 1.89 2.05 2.03
[after] 2026-11-06T09:39:21Z load1/5/15: 2.32 2.07 1.98
```

## Load while running (sampled)

```text
[during] 2026-11-06T09:19:32Z load1/5/15: 1.89 2.05 2.03
[during] 2026-11-06T09:19:42Z load1/5/15: 1.99 2.06 2.04
[during] 2026-11-06T09:19:52Z load1/5/15: 2.22 2.11 2.05
[during] 2026-11-06T09:20:02Z load1/5/15: 2.10 2.09 2.04
[during] 2026-11-06T09:20:12Z load1/5/15: 2.09 2.08 2.04
[during] 2026-11-06T09:20:22Z load1/5/15: 2.07 2.08 2.04
[during] 2026-11-06T09:20:32Z load1/5/15: 1.98 2.06 2.04
[during] 2026-11-06T09:20:42Z load1/5/15: 1.90 2.04 2.03
[during] 2026-11-06T09:20:53Z load1/5/15: 1.84 2.02 2.02
[during] 2026-11-06T09:21:03Z load1/5/15: 1.95 2.04 2.03
[during] 2026-11-06T09:21:13Z load1/5/15: 1.88 2.02 2.02
[during] 2026-11-06T09:21:23Z load1/5/15: 1.90 2.02 2.02
[during] 2026-11-06T09:21:33Z load1/5/15: 1.84 2.00 2.01
[during] 2026-11-06T09:21:43Z load1/5/15: 1.79 1.99 2.01
[during] 2026-11-06T09:21:53Z load1/5/15: 1.82 1.99 2.00
[during] 2026-11-06T09:22:03Z load1/5/15: 1.93 2.00 2.01
[during] 2026-11-06T09:22:13Z load1/5/15: 1.94 2.00 2.01
[during] 2026-11-06T09:22:23Z load1/5/15: 1.79 1.97 2.00
[during] 2026-11-06T09:22:33Z load1/5/15: 1.83 1.97 2.00
[during] 2026-11-06T09:22:43Z load1/5/15: 1.77 1.95 1.99
[during] 2026-11-06T09:22:53Z load1/5/15: 1.71 1.94 1.98
[during] 2026-11-06T09:23:03Z load1/5/15: 1.75 1.94 1.98
[during] 2026-11-06T09:23:13Z load1/5/15: 1.63 1.91 1.97
[during] 2026-11-06T09:23:23Z load1/5/15: 1.76 1.93 1.98
[during] 2026-11-06T09:23:33Z load1/5/15: 1.95 1.96 1.99
[during] 2026-11-06T09:23:43Z load1/5/15: 1.95 1.96 1.99
[during] 2026-11-06T09:23:53Z load1/5/15: 1.88 1.95 1.98
[during] 2026-11-06T09:24:03Z load1/5/15: 1.90 1.95 1.98
[during] 2026-11-06T09:24:13Z load1/5/15: 1.84 1.93 1.98
[during] 2026-11-06T09:24:23Z load1/5/15: 1.86 1.93 1.98
[during] 2026-11-06T09:24:33Z load1/5/15: 1.89 1.94 1.98
[during] 2026-11-06T09:24:43Z load1/5/15: 1.91 1.94 1.98
[during] 2026-11-06T09:24:53Z load1/5/15: 2.00 1.96 1.98
[during] 2026-11-06T09:25:03Z load1/5/15: 2.00 1.96 1.98
[during] 2026-11-06T09:25:13Z load1/5/15: 1.92 1.94 1.98
[during] 2026-11-06T09:25:23Z load1/5/15: 1.93 1.94 1.98
[during] 2026-11-06T09:25:33Z load1/5/15: 1.94 1.94 1.98
[during] 2026-11-06T09:25:43Z load1/5/15: 1.95 1.95 1.98
[during] 2026-11-06T09:25:53Z load1/5/15: 1.96 1.95 1.98
[during] 2026-11-06T09:26:03Z load1/5/15: 1.89 1.93 1.97
[during] 2026-11-06T09:26:13Z load1/5/15: 1.99 1.95 1.98
[during] 2026-11-06T09:26:23Z load1/5/15: 1.92 1.93 1.97
[during] 2026-11-06T09:26:33Z load1/5/15: 1.85 1.92 1.97
[during] 2026-11-06T09:26:43Z load1/5/15: 1.95 1.94 1.97
[during] 2026-11-06T09:26:53Z load1/5/15: 2.11 1.97 1.98
[during] 2026-11-06T09:27:03Z load1/5/15: 2.09 1.97 1.98
[during] 2026-11-06T09:27:13Z load1/5/15: 2.00 1.96 1.98
[during] 2026-11-06T09:27:23Z load1/5/15: 2.00 1.96 1.98
[during] 2026-11-06T09:27:33Z load1/5/15: 1.85 1.93 1.97
[during] 2026-11-06T09:27:43Z load1/5/15: 1.71 1.89 1.96
[during] 2026-11-06T09:27:53Z load1/5/15: 1.84 1.91 1.96
[during] 2026-11-06T09:28:03Z load1/5/15: 1.86 1.92 1.96
[during] 2026-11-06T09:28:13Z load1/5/15: 1.88 1.92 1.96
[during] 2026-11-06T09:28:23Z load1/5/15: 1.90 1.92 1.96
[during] 2026-11-06T09:28:33Z load1/5/15: 1.84 1.91 1.96
[during] 2026-11-06T09:28:43Z load1/5/15: 1.95 1.93 1.96
[during] 2026-11-06T09:28:53Z load1/5/15: 1.81 1.89 1.95
[during] 2026-11-06T09:29:03Z load1/5/15: 1.91 1.91 1.96
[during] 2026-11-06T09:29:13Z load1/5/15: 2.00 1.93 1.96
[during] 2026-11-06T09:29:23Z load1/5/15: 2.00 1.93 1.96
[during] 2026-11-06T09:29:33Z load1/5/15: 1.92 1.92 1.96
[during] 2026-11-06T09:29:43Z load1/5/15: 1.86 1.91 1.95
[during] 2026-11-06T09:29:53Z load1/5/15: 1.80 1.89 1.94
[during] 2026-11-06T09:30:03Z load1/5/15: 1.76 1.88 1.94
[during] 2026-11-06T09:30:13Z load1/5/15: 1.71 1.87 1.93
[during] 2026-11-06T09:30:23Z load1/5/15: 1.84 1.89 1.94
[during] 2026-11-06T09:30:33Z load1/5/15: 1.86 1.89 1.94
[during] 2026-11-06T09:30:43Z load1/5/15: 2.03 1.92 1.95
[during] 2026-11-06T09:30:53Z load1/5/15: 1.94 1.91 1.94
[during] 2026-11-06T09:31:03Z load1/5/15: 1.88 1.90 1.94
[during] 2026-11-06T09:31:13Z load1/5/15: 1.90 1.90 1.94
[during] 2026-11-06T09:31:23Z load1/5/15: 1.91 1.90 1.94
[during] 2026-11-06T09:31:34Z load1/5/15: 1.93 1.91 1.94
[during] 2026-11-06T09:31:44Z load1/5/15: 1.94 1.91 1.94
[during] 2026-11-06T09:31:54Z load1/5/15: 1.95 1.91 1.94
[during] 2026-11-06T09:32:04Z load1/5/15: 1.96 1.91 1.94
[during] 2026-11-06T09:32:14Z load1/5/15: 1.96 1.91 1.94
[during] 2026-11-06T09:32:24Z load1/5/15: 1.96 1.92 1.94
[during] 2026-11-06T09:32:34Z load1/5/15: 1.89 1.90 1.93
[during] 2026-11-06T09:32:44Z load1/5/15: 1.82 1.89 1.93
[during] 2026-11-06T09:32:54Z load1/5/15: 1.93 1.91 1.93
[during] 2026-11-06T09:33:04Z load1/5/15: 1.87 1.89 1.93
[during] 2026-11-06T09:33:14Z load1/5/15: 1.81 1.88 1.92
[during] 2026-11-06T09:33:24Z load1/5/15: 1.84 1.88 1.92
[during] 2026-11-06T09:33:34Z load1/5/15: 2.01 1.92 1.93
[during] 2026-11-06T09:33:44Z load1/5/15: 2.16 1.95 1.94
[during] 2026-11-06T09:33:54Z load1/5/15: 2.06 1.94 1.94
[during] 2026-11-06T09:34:04Z load1/5/15: 2.21 1.97 1.95
[during] 2026-11-06T09:34:14Z load1/5/15: 2.26 1.99 1.96
[during] 2026-11-06T09:34:24Z load1/5/15: 2.22 1.99 1.96
[during] 2026-11-06T09:34:34Z load1/5/15: 2.18 1.99 1.96
[during] 2026-11-06T09:34:44Z load1/5/15: 2.07 1.97 1.95
[during] 2026-11-06T09:34:54Z load1/5/15: 2.06 1.97 1.95
[during] 2026-11-06T09:35:04Z load1/5/15: 2.05 1.97 1.95
[during] 2026-11-06T09:35:14Z load1/5/15: 2.04 1.97 1.95
[during] 2026-11-06T09:35:24Z load1/5/15: 2.03 1.97 1.95
[during] 2026-11-06T09:35:34Z load1/5/15: 2.03 1.97 1.95
[during] 2026-11-06T09:35:44Z load1/5/15: 2.10 1.99 1.96
[during] 2026-11-06T09:35:54Z load1/5/15: 2.00 1.97 1.95
[during] 2026-11-06T09:36:04Z load1/5/15: 2.00 1.97 1.95
[during] 2026-11-06T09:36:14Z load1/5/15: 2.00 1.97 1.95
[during] 2026-11-06T09:36:24Z load1/5/15: 1.92 1.96 1.94
[during] 2026-11-06T09:36:34Z load1/5/15: 2.09 1.99 1.96
[during] 2026-11-06T09:36:44Z load1/5/15: 2.23 2.02 1.97
[during] 2026-11-06T09:36:54Z load1/5/15: 2.20 2.02 1.97
[during] 2026-11-06T09:37:04Z load1/5/15: 2.17 2.02 1.97
[during] 2026-11-06T09:37:14Z load1/5/15: 1.99 1.99 1.96
[during] 2026-11-06T09:37:24Z load1/5/15: 1.83 1.96 1.94
[during] 2026-11-06T09:37:34Z load1/5/15: 1.95 1.97 1.95
[during] 2026-11-06T09:37:44Z load1/5/15: 1.95 1.97 1.95
[during] 2026-11-06T09:37:54Z load1/5/15: 2.04 1.99 1.96
[during] 2026-11-06T09:38:04Z load1/5/15: 2.04 1.99 1.96
[during] 2026-11-06T09:38:14Z load1/5/15: 2.19 2.02 1.97
[during] 2026-11-06T09:38:24Z load1/5/15: 2.16 2.02 1.97
[during] 2026-11-06T09:38:34Z load1/5/15: 1.98 1.99 1.96
[during] 2026-11-06T09:38:44Z load1/5/15: 2.06 2.00 1.96
[during] 2026-11-06T09:38:54Z load1/5/15: 1.90 1.97 1.95
[during] 2026-11-06T09:39:04Z load1/5/15: 2.22 2.04 1.97
[during] 2026-11-06T09:39:14Z load1/5/15: 2.26 2.05 1.98
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

## Recalculation, incremental (one input cell changed)

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| calc-10000.xlsx | onedocument.calc.incr | 15 | 0.4 ms | 0.4 ms | 1.00x | 0.5 MB | 111 MB | 66031877 |
| calc-10000.xlsx | closedxml.calc.incr | 15 | 7.7 ms | 7.0 ms | 19.25x | 0.0 MB | 211 MB | 66031877 |
| calc-10000.xlsx | epplus.calc.incr | 3 | 924.3 ms | 733.8 ms | 2310.75x | 402.3 MB | 164 MB | 66031878 |
| calc-10000.xlsx | npoi.calc.incr | 15 | 16.3 ms | 4.6 ms | 40.75x | 3.5 MB | 203 MB | 66031877 |
| calc-50000.xlsx | onedocument.calc.incr | 15 | 4.5 ms | 3.6 ms | 1.00x | 2.3 MB | 187 MB | 329961877 |
| calc-50000.xlsx | closedxml.calc.incr | 15 | 45.0 ms | 44.7 ms | 10.00x | 0.0 MB | 517 MB | 329961877 |
| calc-50000.xlsx | epplus.calc.incr | 3 | 1.72 s | 1.71 s | 381.71x | 1900.1 MB | 431 MB | 329961878 |
| calc-50000.xlsx | npoi.calc.incr | 15 | 81.0 ms | 79.4 ms | 18.00x | 16.2 MB | 811 MB | 329961877 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Recalculation, incremental (one input cell changed) | calc-10000.xlsx | 0.4 ms | closedxml.calc.incr | 7.7 ms | 0.05x |
| Recalculation, incremental (one input cell changed) | calc-50000.xlsx | 4.5 ms | closedxml.calc.incr | 45.0 ms | 0.10x |

Geometric mean of the ratios: 0.072x over 2 rows. Worst row: Recalculation, incremental (one input cell changed), calc-50000.xlsx at 0.10x.

