# Benchmark results

## Machine and load

```text
run: x64-xlsx-model
filter: *Bench.Docx*
threads: 1
load override: forced start at load 2.99 (limit 2.0)
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-05T04:06:11Z load1/5/15: 2.99 5.72 7.91
[after] 2026-11-05T04:08:22Z load1/5/15: 1.67 4.25 7.08
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:06:11Z load1/5/15: 2.99 5.72 7.91
[during] 2026-11-05T04:06:21Z load1/5/15: 2.84 5.59 7.85
[during] 2026-11-05T04:06:31Z load1/5/15: 3.03 5.54 7.80
[during] 2026-11-05T04:06:41Z load1/5/15: 2.72 5.39 7.73
[during] 2026-11-05T04:06:51Z load1/5/15: 2.45 5.25 7.66
[during] 2026-11-05T04:07:01Z load1/5/15: 2.23 5.11 7.59
[during] 2026-11-05T04:07:11Z load1/5/15: 2.12 4.99 7.52
[during] 2026-11-05T04:07:21Z load1/5/15: 1.95 4.85 7.45
[during] 2026-11-05T04:07:31Z load1/5/15: 1.80 4.73 7.38
[during] 2026-11-05T04:07:41Z load1/5/15: 1.83 4.64 7.32
[during] 2026-11-05T04:07:51Z load1/5/15: 1.93 4.57 7.27
[during] 2026-11-05T04:08:01Z load1/5/15: 1.94 4.48 7.21
[during] 2026-11-05T04:08:11Z load1/5/15: 1.79 4.37 7.15
[during] 2026-11-05T04:08:21Z load1/5/15: 1.67 4.25 7.08
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
| values-10000 | oneoffice.model.load | 5 | 147.4 ms | 144.4 ms | 1.00x | 92.29 B | 62 MB | 537015189 |
| values-10000 | closedxml.model.load | 5 | 631.6 ms | 622.2 ms | 4.28x | 1611.62 B | 137 MB | 537015189 |
| values-10000 | epplus.model.load | 5 | 179.4 ms | 175.1 ms | 1.22x | 897.06 B | 117 MB | 537015189 |
| values-10000 | npoi.model.load | 5 | 367.8 ms | 356.7 ms | 2.50x | 1227.59 B | 173 MB | 537015189 |
| values-100000 | oneoffice.model.load | 3 | 1.53 s | 1.21 s | 1.00x | 54.37 B | 106 MB | 12130167350 |
| values-100000 | closedxml.model.load | 3 | 1.70 s | 1.70 s | 1.11x | 1567.57 B | 226 MB | 12130167350 |
| values-100000 | epplus.model.load | 3 | 1.48 s | 1.22 s | 0.97x | 741.20 B | 245 MB | 12130167350 |
| values-100000 | npoi.model.load | 3 | 2.94 s | 2.92 s | 1.92x | 1214.00 B | 930 MB | 12130167350 |

## Full model: open, change one cell, save

| Input | Row | Reps | Median | Min | Ratio to OneOffice | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | oneoffice.model.editsave | 5 | 263.4 ms | 260.5 ms | 1.00x | 172.03 B | 68 MB | 0 |
| values-10000 | oneoffice.model.loadsave-untouched | 5 | 9.1 ms | 8.9 ms | 0.03x | 2.06 B | 46 MB | 0 |
| values-10000 | closedxml.model.editsave | 5 | 1.08 s | 1.04 s | 4.09x | 1782.66 B | 173 MB | 0 |
| values-10000 | epplus.model.editsave | 5 | 337.6 ms | 330.1 ms | 1.28x | 897.09 B | 133 MB | 0 |
| values-10000 | npoi.model.editsave | 5 | 523.2 ms | 510.7 ms | 1.99x | 1296.35 B | 174 MB | 0 |
| values-100000 | oneoffice.model.editsave | 3 | 2.71 s | 1.18 s | 1.00x | 173.23 B | 220 MB | 0 |
| values-100000 | oneoffice.model.loadsave-untouched | 3 | 12.1 ms | 11.9 ms | 0.00x | 0.21 B | 46 MB | 0 |
| values-100000 | closedxml.model.editsave | 3 | 3.69 s | 3.62 s | 1.36x | 1722.54 B | 820 MB | 0 |
| values-100000 | epplus.model.editsave | 3 | 3.53 s | 2.65 s | 1.30x | 815.20 B | 345 MB | 0 |
| values-100000 | npoi.model.editsave | 3 | 4.78 s | 3.55 s | 1.76x | 1333.63 B | 1060 MB | 0 |

## Bar: OneOffice against the fastest managed comparator

Ratio is OneOffice median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Full model: open and visit every cell | values-10000 | 147.4 ms | epplus.model.load | 179.4 ms | 0.82x |
| Full model: open and visit every cell | values-100000 | 1.53 s | epplus.model.load | 1.48 s | 1.03x |
| Full model: open, change one cell, save | values-10000 | 263.4 ms | epplus.model.editsave | 337.6 ms | 0.78x |
| Full model: open, change one cell, save | values-100000 | 2.71 s | epplus.model.editsave | 3.53 s | 0.77x |

Geometric mean of the ratios: 0.844x over 4 rows. Worst row: Full model: open and visit every cell, values-100000 at 1.03x.

