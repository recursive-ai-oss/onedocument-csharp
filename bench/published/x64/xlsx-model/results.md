# Benchmark results

## Machine and load

```text
run: x64-xlsx-model
group: xlsx model
threads: 1
load override: forced start at load 2.34 (limit 2.0)
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-05T06:36:33Z load1/5/15: 2.34 2.26 2.27
[after] 2026-11-05T06:39:28Z load1/5/15: 2.12 2.16 2.22
```

## Load while running (sampled)

```text
[during] 2026-11-05T06:36:33Z load1/5/15: 2.34 2.26 2.27
[during] 2026-11-05T06:36:43Z load1/5/15: 2.21 2.23 2.26
[during] 2026-11-05T06:36:53Z load1/5/15: 2.18 2.22 2.26
[during] 2026-11-05T06:37:03Z load1/5/15: 2.07 2.20 2.25
[during] 2026-11-05T06:37:13Z load1/5/15: 2.14 2.21 2.25
[during] 2026-11-05T06:37:23Z load1/5/15: 2.12 2.20 2.25
[during] 2026-11-05T06:37:33Z load1/5/15: 2.11 2.20 2.24
[during] 2026-11-05T06:37:43Z load1/5/15: 2.09 2.19 2.24
[during] 2026-11-05T06:37:53Z load1/5/15: 2.07 2.18 2.24
[during] 2026-11-05T06:38:03Z load1/5/15: 2.06 2.18 2.24
[during] 2026-11-05T06:38:13Z load1/5/15: 2.12 2.19 2.24
[during] 2026-11-05T06:38:23Z load1/5/15: 2.02 2.16 2.23
[during] 2026-11-05T06:38:33Z load1/5/15: 2.11 2.17 2.23
[during] 2026-11-05T06:38:43Z load1/5/15: 2.10 2.17 2.23
[during] 2026-11-05T06:38:53Z load1/5/15: 2.01 2.15 2.22
[during] 2026-11-05T06:39:03Z load1/5/15: 2.17 2.18 2.23
[during] 2026-11-05T06:39:13Z load1/5/15: 2.13 2.17 2.23
[during] 2026-11-05T06:39:23Z load1/5/15: 2.04 2.15 2.22
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

## Full model: open and visit every cell

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | onedocument.model.load | 5 | 152.4 ms | 152.0 ms | 1.00x | 92.30 B | 62 MB | 537015189 |
| values-10000 | closedxml.model.load | 5 | 598.5 ms | 584.6 ms | 3.93x | 1611.62 B | 137 MB | 537015189 |
| values-10000 | epplus.model.load | 5 | 184.0 ms | 183.1 ms | 1.21x | 897.06 B | 117 MB | 537015189 |
| values-10000 | npoi.model.load | 5 | 390.9 ms | 373.7 ms | 2.56x | 1227.53 B | 173 MB | 537015189 |
| values-100000 | onedocument.model.load | 3 | 289.4 ms | 282.7 ms | 1.00x | 54.37 B | 106 MB | 12130167350 |
| values-100000 | closedxml.model.load | 3 | 1.73 s | 1.73 s | 5.99x | 1567.49 B | 223 MB | 12130167350 |
| values-100000 | epplus.model.load | 3 | 2.47 s | 975.4 ms | 8.52x | 741.20 B | 246 MB | 12130167350 |
| values-100000 | npoi.model.load | 3 | 2.14 s | 2.13 s | 7.39x | 1213.99 B | 926 MB | 12130167350 |

## Full model: open, change one cell, save

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | onedocument.model.editsave | 5 | 260.7 ms | 260.3 ms | 1.00x | 172.11 B | 69 MB | 0 |
| values-10000 | onedocument.model.loadsave-untouched | 5 | 10.1 ms | 10.0 ms | 0.04x | 2.06 B | 46 MB | 0 |
| values-10000 | closedxml.model.editsave | 5 | 1.03 s | 989.8 ms | 3.94x | 1782.68 B | 173 MB | 0 |
| values-10000 | epplus.model.editsave | 5 | 346.3 ms | 345.0 ms | 1.33x | 897.09 B | 132 MB | 0 |
| values-10000 | npoi.model.editsave | 5 | 668.5 ms | 631.9 ms | 2.56x | 1296.35 B | 176 MB | 0 |
| values-100000 | onedocument.model.editsave | 3 | 574.4 ms | 564.0 ms | 1.00x | 173.21 B | 219 MB | 0 |
| values-100000 | onedocument.model.loadsave-untouched | 3 | 16.5 ms | 16.3 ms | 0.03x | 0.21 B | 46 MB | 0 |
| values-100000 | closedxml.model.editsave | 3 | 3.82 s | 3.81 s | 6.65x | 1722.54 B | 844 MB | 0 |
| values-100000 | epplus.model.editsave | 3 | 2.04 s | 1.87 s | 3.55x | 743.20 B | 343 MB | 0 |
| values-100000 | npoi.model.editsave | 3 | 2.83 s | 2.83 s | 4.93x | 1333.63 B | 1060 MB | 0 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Full model: open and visit every cell | values-10000 | 152.4 ms | epplus.model.load | 184.0 ms | 0.83x |
| Full model: open and visit every cell | values-100000 | 289.4 ms | closedxml.model.load | 1.73 s | 0.17x |
| Full model: open, change one cell, save | values-10000 | 260.7 ms | epplus.model.editsave | 346.3 ms | 0.75x |
| Full model: open, change one cell, save | values-100000 | 574.4 ms | epplus.model.editsave | 2.04 s | 0.28x |

Geometric mean of the ratios: 0.414x over 4 rows. Worst row: Full model: open and visit every cell, values-10000 at 0.83x.

