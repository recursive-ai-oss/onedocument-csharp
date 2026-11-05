# Benchmark results

## Machine and load

```text
run: arm64-xlsx-model
group: xlsx model
threads: 1
load override: forced start at load 2.27 (limit 2.0)
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-05T05:04:39Z load1/5/15: 2.27 2.34 4.44
[after] 2026-11-05T05:07:26Z load1/5/15: 2.63 2.45 4.11
```

## Load while running (sampled)

```text
[during] 2026-11-05T05:04:39Z load1/5/15: 2.27 2.34 4.44
[during] 2026-11-05T05:04:49Z load1/5/15: 2.53 2.40 4.43
[during] 2026-11-05T05:04:59Z load1/5/15: 2.60 2.42 4.41
[during] 2026-11-05T05:05:09Z load1/5/15: 2.43 2.39 4.38
[during] 2026-11-05T05:05:19Z load1/5/15: 2.29 2.36 4.34
[during] 2026-11-05T05:05:29Z load1/5/15: 2.32 2.36 4.32
[during] 2026-11-05T05:05:39Z load1/5/15: 2.34 2.36 4.30
[during] 2026-11-05T05:05:49Z load1/5/15: 2.37 2.37 4.28
[during] 2026-11-05T05:05:59Z load1/5/15: 2.54 2.40 4.27
[during] 2026-11-05T05:06:09Z load1/5/15: 2.46 2.39 4.24
[during] 2026-11-05T05:06:19Z load1/5/15: 2.53 2.41 4.23
[during] 2026-11-05T05:06:29Z load1/5/15: 2.53 2.41 4.21
[during] 2026-11-05T05:06:39Z load1/5/15: 2.44 2.40 4.18
[during] 2026-11-05T05:06:49Z load1/5/15: 2.45 2.40 4.16
[during] 2026-11-05T05:06:59Z load1/5/15: 2.60 2.44 4.15
[during] 2026-11-05T05:07:09Z load1/5/15: 2.51 2.42 4.13
[during] 2026-11-05T05:07:19Z load1/5/15: 2.43 2.41 4.10
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
| values-10000 | oneoffice.model.load | 5 | 168.2 ms | 166.7 ms | 1.00x | 92.30 B | 69 MB | 537015189 |
| values-10000 | closedxml.model.load | 5 | 600.7 ms | 537.8 ms | 3.57x | 1611.73 B | 113 MB | 537015189 |
| values-10000 | epplus.model.load | 5 | 190.5 ms | 188.3 ms | 1.13x | 897.10 B | 93 MB | 537015189 |
| values-10000 | npoi.model.load | 5 | 357.3 ms | 354.2 ms | 2.12x | 1227.68 B | 170 MB | 537015189 |
| values-100000 | oneoffice.model.load | 3 | 259.7 ms | 259.4 ms | 1.00x | 54.37 B | 107 MB | 12130167350 |
| values-100000 | closedxml.model.load | 3 | 1.57 s | 1.51 s | 6.05x | 1567.55 B | 200 MB | 12130167350 |
| values-100000 | epplus.model.load | 3 | 834.3 ms | 818.5 ms | 3.21x | 741.20 B | 215 MB | 12130167350 |
| values-100000 | npoi.model.load | 3 | 2.43 s | 2.29 s | 9.37x | 1214.02 B | 1023 MB | 12130167350 |

## Full model: open, change one cell, save

| Input | Row | Reps | Median | Min | Ratio to OneOffice | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | oneoffice.model.editsave | 5 | 258.1 ms | 257.3 ms | 1.00x | 172.05 B | 77 MB | 0 |
| values-10000 | oneoffice.model.loadsave-untouched | 5 | 4.2 ms | 3.6 ms | 0.02x | 2.06 B | 57 MB | 0 |
| values-10000 | closedxml.model.editsave | 5 | 977.7 ms | 941.6 ms | 3.79x | 1782.73 B | 180 MB | 0 |
| values-10000 | epplus.model.editsave | 5 | 370.9 ms | 327.9 ms | 1.44x | 897.19 B | 116 MB | 0 |
| values-10000 | npoi.model.editsave | 5 | 633.3 ms | 613.7 ms | 2.45x | 1296.37 B | 176 MB | 0 |
| values-100000 | oneoffice.model.editsave | 3 | 537.0 ms | 535.3 ms | 1.00x | 173.21 B | 198 MB | 0 |
| values-100000 | oneoffice.model.loadsave-untouched | 3 | 11.3 ms | 10.5 ms | 0.02x | 0.21 B | 57 MB | 0 |
| values-100000 | closedxml.model.editsave | 3 | 3.63 s | 3.50 s | 6.75x | 1722.62 B | 800 MB | 0 |
| values-100000 | epplus.model.editsave | 3 | 1.72 s | 1.67 s | 3.19x | 743.21 B | 314 MB | 0 |
| values-100000 | npoi.model.editsave | 3 | 3.40 s | 3.35 s | 6.34x | 1333.65 B | 1268 MB | 0 |

## Bar: OneOffice against the fastest managed comparator

Ratio is OneOffice median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Full model: open and visit every cell | values-10000 | 168.2 ms | epplus.model.load | 190.5 ms | 0.88x |
| Full model: open and visit every cell | values-100000 | 259.7 ms | epplus.model.load | 834.3 ms | 0.31x |
| Full model: open, change one cell, save | values-10000 | 258.1 ms | epplus.model.editsave | 370.9 ms | 0.70x |
| Full model: open, change one cell, save | values-100000 | 537.0 ms | epplus.model.editsave | 1.72 s | 0.31x |

Geometric mean of the ratios: 0.495x over 4 rows. Worst row: Full model: open and visit every cell, values-10000 at 0.88x.

