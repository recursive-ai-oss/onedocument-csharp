# Benchmark results

## Machine and load

```text
run: arm64-calc-cold
group: fmt calc-cold
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T08:50:03Z load1/5/15: 1.98 1.70 1.75
[after] 2026-11-06T08:56:26Z load1/5/15: 2.03 1.94 1.84
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:50:03Z load1/5/15: 1.98 1.71 1.75
[during] 2026-11-06T08:50:13Z load1/5/15: 2.15 1.75 1.76
[during] 2026-11-06T08:50:23Z load1/5/15: 2.19 1.77 1.77
[during] 2026-11-06T08:50:33Z load1/5/15: 2.16 1.78 1.77
[during] 2026-11-06T08:50:43Z load1/5/15: 2.14 1.79 1.78
[during] 2026-11-06T08:50:54Z load1/5/15: 2.04 1.78 1.77
[during] 2026-11-06T08:51:04Z load1/5/15: 1.95 1.77 1.77
[during] 2026-11-06T08:51:14Z load1/5/15: 2.11 1.81 1.78
[during] 2026-11-06T08:51:24Z load1/5/15: 2.18 1.83 1.79
[during] 2026-11-06T08:51:34Z load1/5/15: 2.16 1.83 1.79
[during] 2026-11-06T08:51:44Z load1/5/15: 2.05 1.82 1.79
[during] 2026-11-06T08:51:54Z load1/5/15: 2.12 1.84 1.79
[during] 2026-11-06T08:52:04Z load1/5/15: 2.18 1.86 1.80
[during] 2026-11-06T08:52:14Z load1/5/15: 2.08 1.85 1.80
[during] 2026-11-06T08:52:24Z load1/5/15: 1.91 1.82 1.79
[during] 2026-11-06T08:52:34Z load1/5/15: 2.07 1.86 1.80
[during] 2026-11-06T08:52:44Z load1/5/15: 1.99 1.85 1.80
[during] 2026-11-06T08:52:54Z load1/5/15: 1.99 1.85 1.80
[during] 2026-11-06T08:53:04Z load1/5/15: 1.91 1.84 1.80
[during] 2026-11-06T08:53:14Z load1/5/15: 1.84 1.83 1.79
[during] 2026-11-06T08:53:24Z load1/5/15: 1.95 1.85 1.80
[during] 2026-11-06T08:53:34Z load1/5/15: 2.03 1.87 1.81
[during] 2026-11-06T08:53:44Z load1/5/15: 2.03 1.88 1.81
[during] 2026-11-06T08:53:54Z load1/5/15: 2.02 1.88 1.81
[during] 2026-11-06T08:54:04Z load1/5/15: 2.01 1.88 1.81
[during] 2026-11-06T08:54:14Z load1/5/15: 1.94 1.87 1.81
[during] 2026-11-06T08:54:24Z load1/5/15: 1.95 1.87 1.81
[during] 2026-11-06T08:54:34Z load1/5/15: 1.95 1.88 1.81
[during] 2026-11-06T08:54:44Z load1/5/15: 1.96 1.88 1.82
[during] 2026-11-06T08:54:54Z load1/5/15: 1.89 1.87 1.81
[during] 2026-11-06T08:55:04Z load1/5/15: 2.07 1.91 1.83
[during] 2026-11-06T08:55:14Z load1/5/15: 2.06 1.91 1.83
[during] 2026-11-06T08:55:24Z load1/5/15: 2.05 1.91 1.83
[during] 2026-11-06T08:55:34Z load1/5/15: 2.04 1.91 1.83
[during] 2026-11-06T08:55:44Z load1/5/15: 2.11 1.93 1.84
[during] 2026-11-06T08:55:54Z load1/5/15: 2.17 1.95 1.85
[during] 2026-11-06T08:56:04Z load1/5/15: 2.14 1.95 1.85
[during] 2026-11-06T08:56:14Z load1/5/15: 2.12 1.95 1.85
[during] 2026-11-06T08:56:24Z load1/5/15: 2.03 1.94 1.84
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

## Recalculation, cold (every formula)

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| calc-10000.xlsx | onedocument.calc.cold | 3 | 490.2 ms | 488.9 ms | 1.00x | 162.6 MB | 108 MB | 65982500 |
| calc-10000.xlsx | closedxml.calc.cold | 3 | 2.48 s | 2.48 s | 5.07x | 232.3 MB | 207 MB | 65982500 |
| calc-10000.xlsx | epplus.calc.cold | 3 | 1.24 s | 1.04 s | 2.52x | 415.4 MB | 135 MB | 65982500 |
| calc-10000.xlsx | npoi.calc.cold | 3 | 1.22 s | 1.14 s | 2.48x | 377.2 MB | 187 MB | 65982500 |
| calc-50000.xlsx | onedocument.calc.cold | 3 | 1.16 s | 1.16 s | 1.00x | 804.3 MB | 186 MB | 329912500 |
| calc-50000.xlsx | closedxml.calc.cold | 3 | 57.42 s | 57.25 s | 49.43x | 1163.0 MB | 517 MB | 329912500 |
| calc-50000.xlsx | epplus.calc.cold | 3 | 3.12 s | 2.92 s | 2.68x | 2033.8 MB | 305 MB | 329912500 |
| calc-50000.xlsx | npoi.calc.cold | 3 | 3.15 s | 3.14 s | 2.71x | 1905.1 MB | 581 MB | 329912500 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Recalculation, cold (every formula) | calc-10000.xlsx | 490.2 ms | npoi.calc.cold | 1.22 s | 0.40x |
| Recalculation, cold (every formula) | calc-50000.xlsx | 1.16 s | epplus.calc.cold | 3.12 s | 0.37x |

Geometric mean of the ratios: 0.387x over 2 rows. Worst row: Recalculation, cold (every formula), calc-10000.xlsx at 0.40x.

