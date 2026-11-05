# Benchmark results

## Machine and load

```text
run: arm64-xlsx-model
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
[before] 2026-11-05T04:27:15Z load1/5/15: 2.56 22.91 27.59
[after] 2026-11-05T04:29:33Z load1/5/15: 2.51 15.53 23.92
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:27:15Z load1/5/15: 2.56 22.91 27.59
[during] 2026-11-05T04:27:25Z load1/5/15: 2.77 22.29 27.31
[during] 2026-11-05T04:27:35Z load1/5/15: 2.65 21.62 27.02
[during] 2026-11-05T04:27:45Z load1/5/15: 2.61 20.99 26.73
[during] 2026-11-05T04:27:55Z load1/5/15: 2.67 20.40 26.45
[during] 2026-11-05T04:28:05Z load1/5/15: 2.95 19.87 26.19
[during] 2026-11-05T04:28:15Z load1/5/15: 2.81 19.29 25.91
[during] 2026-11-05T04:28:25Z load1/5/15: 2.61 18.70 25.62
[during] 2026-11-05T04:28:35Z load1/5/15: 2.43 18.13 25.34
[during] 2026-11-05T04:28:45Z load1/5/15: 2.53 17.63 25.08
[during] 2026-11-05T04:28:55Z load1/5/15: 2.82 17.20 24.84
[during] 2026-11-05T04:29:05Z load1/5/15: 2.85 16.73 24.58
[during] 2026-11-05T04:29:15Z load1/5/15: 2.57 16.21 24.31
[during] 2026-11-05T04:29:25Z load1/5/15: 2.55 15.76 24.05
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

## Full model: open and visit every cell

| Input | Row | Reps | Median | Min | Ratio to OneOffice | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | oneoffice.model.load | 5 | 167.4 ms | 166.7 ms | 1.00x | 92.29 B | 69 MB | 537015189 |
| values-10000 | closedxml.model.load | 5 | 818.2 ms | 758.7 ms | 4.89x | 1611.69 B | 114 MB | 537015189 |
| values-10000 | epplus.model.load | 5 | 193.9 ms | 189.6 ms | 1.16x | 897.17 B | 93 MB | 537015189 |
| values-10000 | npoi.model.load | 5 | 366.1 ms | 355.3 ms | 2.19x | 1227.56 B | 170 MB | 537015189 |
| values-100000 | oneoffice.model.load | 3 | 1.55 s | 263.9 ms | 1.00x | 54.37 B | 107 MB | 12130167350 |
| values-100000 | closedxml.model.load | 3 | 1.57 s | 1.53 s | 1.01x | 1567.58 B | 200 MB | 12130167350 |
| values-100000 | epplus.model.load | 3 | 2.01 s | 1.93 s | 1.30x | 885.21 B | 213 MB | 12130167350 |
| values-100000 | npoi.model.load | 3 | 2.89 s | 2.65 s | 1.87x | 1214.05 B | 1130 MB | 12130167350 |

## Full model: open, change one cell, save

| Input | Row | Reps | Median | Min | Ratio to OneOffice | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | oneoffice.model.editsave | 5 | 262.0 ms | 258.8 ms | 1.00x | 172.11 B | 77 MB | 0 |
| values-10000 | oneoffice.model.loadsave-untouched | 5 | 4.0 ms | 3.7 ms | 0.02x | 2.06 B | 57 MB | 0 |
| values-10000 | closedxml.model.editsave | 5 | 1.30 s | 1.18 s | 4.97x | 1782.73 B | 180 MB | 0 |
| values-10000 | epplus.model.editsave | 5 | 383.3 ms | 374.5 ms | 1.46x | 897.17 B | 115 MB | 0 |
| values-10000 | npoi.model.editsave | 5 | 565.7 ms | 553.9 ms | 2.16x | 1296.62 B | 176 MB | 0 |
| values-100000 | oneoffice.model.editsave | 3 | 2.95 s | 818.7 ms | 1.00x | 173.21 B | 198 MB | 0 |
| values-100000 | oneoffice.model.loadsave-untouched | 3 | 10.1 ms | 9.5 ms | 0.00x | 0.21 B | 57 MB | 0 |
| values-100000 | closedxml.model.editsave | 3 | 3.70 s | 3.54 s | 1.25x | 1722.63 B | 800 MB | 0 |
| values-100000 | epplus.model.editsave | 3 | 4.15 s | 2.39 s | 1.40x | 777.16 B | 316 MB | 0 |
| values-100000 | npoi.model.editsave | 3 | 5.52 s | 3.12 s | 1.87x | 1333.65 B | 1153 MB | 0 |

## Bar: OneOffice against the fastest managed comparator

Ratio is OneOffice median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Full model: open and visit every cell | values-10000 | 167.4 ms | epplus.model.load | 193.9 ms | 0.86x |
| Full model: open and visit every cell | values-100000 | 1.55 s | closedxml.model.load | 1.57 s | 0.99x |
| Full model: open, change one cell, save | values-10000 | 262.0 ms | epplus.model.editsave | 383.3 ms | 0.68x |
| Full model: open, change one cell, save | values-100000 | 2.95 s | closedxml.model.editsave | 3.70 s | 0.80x |

Geometric mean of the ratios: 0.826x over 4 rows. Worst row: Full model: open and visit every cell, values-100000 at 0.99x.

