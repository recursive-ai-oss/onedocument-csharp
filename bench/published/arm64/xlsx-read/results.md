# Benchmark results

## Machine and load

```text
run: arm64-xlsx-read
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
[before] 2026-11-05T04:24:08Z load1/5/15: 9.05 40.46 33.69
[after] 2026-11-05T04:26:01Z load1/5/15: 3.75 28.84 29.93
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:24:08Z load1/5/15: 9.05 40.46 33.69
[during] 2026-11-05T04:24:18Z load1/5/15: 8.64 39.34 33.37
[during] 2026-11-05T04:24:28Z load1/5/15: 7.62 38.11 33.00
[during] 2026-11-05T04:24:38Z load1/5/15: 6.75 36.92 32.64
[during] 2026-11-05T04:24:48Z load1/5/15: 5.94 35.75 32.27
[during] 2026-11-05T04:24:58Z load1/5/15: 5.34 34.64 31.92
[during] 2026-11-05T04:25:08Z load1/5/15: 4.91 33.58 31.57
[during] 2026-11-05T04:25:18Z load1/5/15: 4.61 32.57 31.24
[during] 2026-11-05T04:25:28Z load1/5/15: 4.29 31.58 30.90
[during] 2026-11-05T04:25:38Z load1/5/15: 3.93 30.61 30.57
[during] 2026-11-05T04:25:48Z load1/5/15: 3.80 29.70 30.24
[during] 2026-11-05T04:25:58Z load1/5/15: 3.75 28.84 29.93
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

## Streaming read, typed values (every cell)

| Input | Row | Reps | Median | Min | Ratio to OneOffice | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000 | oneoffice.read.string | 5 | 78.1 ms | 77.3 ms | 1.00x | 40.95 B | 61 MB | 537015189 |
| values-10000 | oneoffice.read.span | 5 | 84.6 ms | 78.1 ms | 1.08x | 39.48 B | 61 MB | 537015189 |
| values-10000 | exceldatareader.read | 5 | 142.1 ms | 141.3 ms | 1.82x | 206.83 B | 73 MB | 537015189 |
| values-10000 | miniexcel.read | 5 | 218.3 ms | 217.7 ms | 2.80x | 2006.19 B | 81 MB | 537015189 |
| values-10000 | sylvan.read | 5 | 77.7 ms | 76.4 ms | 0.99x | 5.54 B | 64 MB | 537015189 |
| values-100000 | oneoffice.read.string | 3 | 781.6 ms | 665.3 ms | 1.00x | 0.76 B | 62 MB | 12130167350 |
| values-100000 | oneoffice.read.span | 3 | 769.2 ms | 680.7 ms | 0.98x | 0.60 B | 62 MB | 12130167350 |
| values-100000 | exceldatareader.read | 3 | 1.45 s | 1.43 s | 1.86x | 213.69 B | 73 MB | 12130167350 |
| values-100000 | miniexcel.read | 3 | 2.84 s | 2.01 s | 3.64x | 1607.81 B | 82 MB | 12130167350 |
| values-100000 | sylvan.read | 3 | 743.6 ms | 739.4 ms | 0.95x | 0.66 B | 65 MB | 12130167350 |
| values-1000000 | oneoffice.read.string | 2 | 1.97 s | 1.93 s | 1.00x | 0.08 B | 62 MB | 796312017999 |
| values-1000000 | oneoffice.read.span | 2 | 1.95 s | 1.91 s | 0.99x | 0.06 B | 62 MB | 796312017999 |
| values-1000000 | exceldatareader.read | 2 | 4.74 s | 4.71 s | 2.40x | 216.86 B | 75 MB | 796312017999 |
| values-1000000 | miniexcel.read | 2 | 8.63 s | 8.63 s | 4.37x | 2021.45 B | 83 MB | 796312017999 |
| values-1000000 | sylvan.read | 2 | 2.10 s | 2.08 s | 1.06x | 0.19 B | 70 MB | 796312017999 |

## Bar: OneOffice against the fastest managed comparator

Ratio is OneOffice median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming read, typed values (every cell) | values-10000 | 78.1 ms | sylvan.read | 77.7 ms | 1.01x |
| Streaming read, typed values (every cell) | values-100000 | 781.6 ms | sylvan.read | 743.6 ms | 1.05x |
| Streaming read, typed values (every cell) | values-1000000 | 1.97 s | sylvan.read | 2.10 s | 0.94x |

Geometric mean of the ratios: 0.998x over 3 rows. Worst row: Streaming read, typed values (every cell), values-100000 at 1.05x.

