# Benchmark results

## Machine and load

```text
run: x64-xlsx-model
group: xlsx model
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:08:53Z load1/5/15: 8.12 8.50 10.73
[after] 2026-11-06T08:11:38Z load1/5/15: 1.56 5.36 9.16
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:08:54Z load1/5/15: 8.12 8.50 10.73
[during] 2026-11-06T08:09:04Z load1/5/15: 7.03 8.26 10.62
[during] 2026-11-06T08:09:14Z load1/5/15: 6.10 8.02 10.52
[during] 2026-11-06T08:09:24Z load1/5/15: 5.31 7.79 10.42
[during] 2026-11-06T08:09:34Z load1/5/15: 4.65 7.56 10.32
[during] 2026-11-06T08:09:44Z load1/5/15: 4.09 7.34 10.22
[during] 2026-11-06T08:09:54Z load1/5/15: 3.76 7.17 10.13
[during] 2026-11-06T08:10:04Z load1/5/15: 3.34 6.96 10.03
[during] 2026-11-06T08:10:14Z load1/5/15: 3.05 6.78 9.94
[during] 2026-11-06T08:10:24Z load1/5/15: 2.73 6.59 9.84
[during] 2026-11-06T08:10:34Z load1/5/15: 2.47 6.41 9.75
[during] 2026-11-06T08:10:44Z load1/5/15: 2.24 6.23 9.65
[during] 2026-11-06T08:10:54Z load1/5/15: 2.05 6.06 9.56
[during] 2026-11-06T08:11:04Z load1/5/15: 1.89 5.89 9.47
[during] 2026-11-06T08:11:14Z load1/5/15: 1.75 5.73 9.38
[during] 2026-11-06T08:11:24Z load1/5/15: 1.72 5.59 9.29
[during] 2026-11-06T08:11:34Z load1/5/15: 1.60 5.44 9.20
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
| values-10000 | onedocument.model.load | 5 | 146.1 ms | 144.5 ms | 1.00x | 92.30 B | 68 MB | 537015189 |
| values-10000 | closedxml.model.load | 5 | 609.2 ms | 604.3 ms | 4.17x | 1611.62 B | 141 MB | 537015189 |
| values-10000 | epplus.model.load | 5 | 174.8 ms | 171.7 ms | 1.20x | 897.06 B | 121 MB | 537015189 |
| values-10000 | npoi.model.load | 5 | 344.4 ms | 338.6 ms | 2.36x | 1227.53 B | 178 MB | 537015189 |
| values-100000 | onedocument.model.load | 3 | 279.9 ms | 276.1 ms | 1.00x | 54.37 B | 111 MB | 12130167350 |
| values-100000 | closedxml.model.load | 3 | 1.71 s | 1.70 s | 6.10x | 1567.49 B | 234 MB | 12130167350 |
| values-100000 | epplus.model.load | 3 | 901.0 ms | 894.4 ms | 3.22x | 741.20 B | 255 MB | 12130167350 |
| values-100000 | npoi.model.load | 3 | 2.05 s | 2.04 s | 7.31x | 1213.99 B | 933 MB | 12130167350 |

## Full model: open, change one cell, save

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | onedocument.model.editsave | 5 | 250.3 ms | 249.2 ms | 1.00x | 172.11 B | 73 MB | 0 |
| values-10000 | onedocument.model.loadsave-untouched | 5 | 10.3 ms | 10.0 ms | 0.04x | 2.06 B | 51 MB | 0 |
| values-10000 | closedxml.model.editsave | 5 | 762.8 ms | 751.7 ms | 3.05x | 1782.64 B | 177 MB | 0 |
| values-10000 | epplus.model.editsave | 5 | 330.9 ms | 324.2 ms | 1.32x | 897.08 B | 136 MB | 0 |
| values-10000 | npoi.model.editsave | 5 | 577.7 ms | 536.3 ms | 2.31x | 1296.36 B | 179 MB | 0 |
| values-100000 | onedocument.model.editsave | 3 | 541.1 ms | 522.0 ms | 1.00x | 173.21 B | 226 MB | 0 |
| values-100000 | onedocument.model.loadsave-untouched | 3 | 12.1 ms | 12.0 ms | 0.02x | 0.21 B | 51 MB | 0 |
| values-100000 | closedxml.model.editsave | 3 | 3.63 s | 3.61 s | 6.70x | 1722.54 B | 850 MB | 0 |
| values-100000 | epplus.model.editsave | 3 | 1.82 s | 1.81 s | 3.37x | 743.20 B | 356 MB | 0 |
| values-100000 | npoi.model.editsave | 3 | 2.86 s | 2.67 s | 5.29x | 1333.63 B | 1066 MB | 0 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Full model: open and visit every cell | values-10000 | 146.1 ms | epplus.model.load | 174.8 ms | 0.84x |
| Full model: open and visit every cell | values-100000 | 279.9 ms | epplus.model.load | 901.0 ms | 0.31x |
| Full model: open, change one cell, save | values-10000 | 250.3 ms | epplus.model.editsave | 330.9 ms | 0.76x |
| Full model: open, change one cell, save | values-100000 | 541.1 ms | epplus.model.editsave | 1.82 s | 0.30x |

Geometric mean of the ratios: 0.491x over 4 rows. Worst row: Full model: open and visit every cell, values-10000 at 0.84x.

