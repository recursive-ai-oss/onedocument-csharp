# Benchmark results

## Machine and load

```text
run: arm64-xlsx-model
group: xlsx model
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T08:17:02Z load1/5/15: 1.82 1.91 1.95
[after] 2026-11-06T08:19:49Z load1/5/15: 1.85 1.94 1.95
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:17:03Z load1/5/15: 1.82 1.91 1.95
[during] 2026-11-06T08:17:13Z load1/5/15: 2.00 1.95 1.96
[during] 2026-11-06T08:17:23Z load1/5/15: 1.85 1.92 1.95
[during] 2026-11-06T08:17:33Z load1/5/15: 1.95 1.94 1.95
[during] 2026-11-06T08:17:43Z load1/5/15: 2.03 1.95 1.96
[during] 2026-11-06T08:17:53Z load1/5/15: 1.95 1.94 1.95
[during] 2026-11-06T08:18:03Z load1/5/15: 1.88 1.92 1.95
[during] 2026-11-06T08:18:13Z load1/5/15: 1.90 1.93 1.95
[during] 2026-11-06T08:18:23Z load1/5/15: 1.92 1.93 1.95
[during] 2026-11-06T08:18:33Z load1/5/15: 1.93 1.93 1.95
[during] 2026-11-06T08:18:43Z load1/5/15: 1.94 1.93 1.95
[during] 2026-11-06T08:18:53Z load1/5/15: 2.19 1.98 1.96
[during] 2026-11-06T08:19:03Z load1/5/15: 2.24 2.00 1.97
[during] 2026-11-06T08:19:13Z load1/5/15: 2.20 2.00 1.97
[during] 2026-11-06T08:19:23Z load1/5/15: 2.17 2.00 1.97
[during] 2026-11-06T08:19:33Z load1/5/15: 1.99 1.97 1.96
[during] 2026-11-06T08:19:43Z load1/5/15: 1.84 1.93 1.95
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

## Full model: open and visit every cell

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | onedocument.model.load | 5 | 164.2 ms | 163.3 ms | 1.00x | 92.30 B | 73 MB | 537015189 |
| values-10000 | closedxml.model.load | 5 | 611.7 ms | 609.3 ms | 3.73x | 1611.34 B | 116 MB | 537015189 |
| values-10000 | epplus.model.load | 5 | 191.9 ms | 190.1 ms | 1.17x | 897.09 B | 97 MB | 537015189 |
| values-10000 | npoi.model.load | 5 | 353.6 ms | 352.4 ms | 2.15x | 1227.56 B | 173 MB | 537015189 |
| values-100000 | onedocument.model.load | 3 | 260.3 ms | 259.5 ms | 1.00x | 54.37 B | 110 MB | 12130167350 |
| values-100000 | closedxml.model.load | 3 | 1.60 s | 1.56 s | 6.14x | 1567.53 B | 204 MB | 12130167350 |
| values-100000 | epplus.model.load | 3 | 836.0 ms | 823.7 ms | 3.21x | 741.20 B | 217 MB | 12130167350 |
| values-100000 | npoi.model.load | 3 | 2.20 s | 1.87 s | 8.46x | 1214.02 B | 1133 MB | 12130167350 |

## Full model: open, change one cell, save

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | onedocument.model.editsave | 5 | 253.1 ms | 252.0 ms | 1.00x | 172.05 B | 80 MB | 0 |
| values-10000 | onedocument.model.loadsave-untouched | 5 | 3.5 ms | 3.1 ms | 0.01x | 2.06 B | 61 MB | 0 |
| values-10000 | closedxml.model.editsave | 5 | 936.5 ms | 925.9 ms | 3.70x | 1782.73 B | 184 MB | 0 |
| values-10000 | epplus.model.editsave | 5 | 367.3 ms | 334.4 ms | 1.45x | 897.12 B | 119 MB | 0 |
| values-10000 | npoi.model.editsave | 5 | 624.0 ms | 568.0 ms | 2.47x | 1296.40 B | 180 MB | 0 |
| values-100000 | onedocument.model.editsave | 3 | 535.9 ms | 534.8 ms | 1.00x | 173.22 B | 201 MB | 0 |
| values-100000 | onedocument.model.loadsave-untouched | 3 | 10.0 ms | 9.1 ms | 0.02x | 0.21 B | 60 MB | 0 |
| values-100000 | closedxml.model.editsave | 3 | 3.51 s | 3.47 s | 6.54x | 1722.64 B | 914 MB | 0 |
| values-100000 | epplus.model.editsave | 3 | 1.67 s | 1.65 s | 3.11x | 743.21 B | 318 MB | 0 |
| values-100000 | npoi.model.editsave | 3 | 2.68 s | 2.68 s | 5.01x | 1333.65 B | 1158 MB | 0 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Full model: open and visit every cell | values-10000 | 164.2 ms | epplus.model.load | 191.9 ms | 0.86x |
| Full model: open and visit every cell | values-100000 | 260.3 ms | epplus.model.load | 836.0 ms | 0.31x |
| Full model: open, change one cell, save | values-10000 | 253.1 ms | epplus.model.editsave | 367.3 ms | 0.69x |
| Full model: open, change one cell, save | values-100000 | 535.9 ms | epplus.model.editsave | 1.67 s | 0.32x |

Geometric mean of the ratios: 0.493x over 4 rows. Worst row: Full model: open and visit every cell, values-10000 at 0.86x.

