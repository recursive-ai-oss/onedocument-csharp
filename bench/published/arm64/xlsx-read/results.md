# Benchmark results

## Machine and load

```text
run: arm64-xlsx-read
group: xlsx read
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T07:49:18Z load1/5/15: 2.18 2.28 2.36
[after] 2026-11-06T07:51:09Z load1/5/15: 2.03 2.19 2.31
```

## Load while running (sampled)

```text
[during] 2026-11-06T07:49:18Z load1/5/15: 2.18 2.28 2.36
[during] 2026-11-06T07:49:28Z load1/5/15: 2.15 2.27 2.36
[during] 2026-11-06T07:49:38Z load1/5/15: 2.13 2.26 2.35
[during] 2026-11-06T07:49:48Z load1/5/15: 2.03 2.23 2.34
[during] 2026-11-06T07:49:58Z load1/5/15: 2.03 2.23 2.34
[during] 2026-11-06T07:50:08Z load1/5/15: 2.10 2.23 2.34
[during] 2026-11-06T07:50:18Z load1/5/15: 2.09 2.23 2.33
[during] 2026-11-06T07:50:28Z load1/5/15: 2.08 2.22 2.33
[during] 2026-11-06T07:50:38Z load1/5/15: 2.07 2.21 2.32
[during] 2026-11-06T07:50:48Z load1/5/15: 2.06 2.20 2.32
[during] 2026-11-06T07:50:58Z load1/5/15: 2.13 2.21 2.32
[during] 2026-11-06T07:51:08Z load1/5/15: 2.03 2.19 2.31
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

## Streaming read, typed values (every cell)

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | onedocument.read.string | 5 | 78.0 ms | 77.6 ms | 1.00x | 40.95 B | 65 MB | 537015189 |
| values-10000 | onedocument.read.span | 5 | 77.9 ms | 77.5 ms | 1.00x | 39.49 B | 65 MB | 537015189 |
| values-10000 | exceldatareader.read | 5 | 138.7 ms | 137.9 ms | 1.78x | 206.83 B | 76 MB | 537015189 |
| values-10000 | miniexcel.read | 5 | 208.4 ms | 207.3 ms | 2.67x | 2006.19 B | 84 MB | 537015189 |
| values-10000 | sylvan.read | 5 | 73.2 ms | 73.0 ms | 0.94x | 5.54 B | 67 MB | 537015189 |
| values-100000 | onedocument.read.string | 3 | 761.6 ms | 716.1 ms | 1.00x | 0.75 B | 63 MB | 12130167350 |
| values-100000 | onedocument.read.span | 3 | 764.9 ms | 710.7 ms | 1.00x | 0.60 B | 63 MB | 12130167350 |
| values-100000 | exceldatareader.read | 3 | 1.39 s | 1.37 s | 1.82x | 213.69 B | 77 MB | 12130167350 |
| values-100000 | miniexcel.read | 3 | 3.18 s | 1.94 s | 4.17x | 1607.81 B | 86 MB | 12130167350 |
| values-100000 | sylvan.read | 3 | 717.9 ms | 716.7 ms | 0.94x | 0.66 B | 67 MB | 12130167350 |
| values-1000000 | onedocument.read.string | 2 | 1.88 s | 1.87 s | 1.00x | 0.08 B | 66 MB | 796312017999 |
| values-1000000 | onedocument.read.span | 2 | 1.88 s | 1.87 s | 1.00x | 0.06 B | 66 MB | 796312017999 |
| values-1000000 | exceldatareader.read | 2 | 4.51 s | 4.46 s | 2.40x | 216.86 B | 79 MB | 796312017999 |
| values-1000000 | miniexcel.read | 2 | 8.20 s | 8.20 s | 4.36x | 2021.45 B | 87 MB | 796312017999 |
| values-1000000 | sylvan.read | 2 | 2.01 s | 2.01 s | 1.07x | 0.19 B | 73 MB | 796312017999 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming read, typed values (every cell) | values-10000 | 78.0 ms | sylvan.read | 73.2 ms | 1.07x |
| Streaming read, typed values (every cell) | values-100000 | 761.6 ms | sylvan.read | 717.9 ms | 1.06x |
| Streaming read, typed values (every cell) | values-1000000 | 1.88 s | sylvan.read | 2.01 s | 0.94x |

Geometric mean of the ratios: 1.019x over 3 rows. Worst row: Streaming read, typed values (every cell), values-10000 at 1.07x.

