# Benchmark results

## Machine and load

```text
run: x64-xlsx-read
filter: *Bench.Docx*
threads: 1
load override: forced start at load 3.69 (limit 2.0)
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-05T04:03:12Z load1/5/15: 3.69 7.80 8.92
[after] 2026-11-05T04:04:58Z load1/5/15: 3.67 6.60 8.36
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:03:13Z load1/5/15: 3.69 7.80 8.92
[during] 2026-11-05T04:03:23Z load1/5/15: 3.51 7.63 8.85
[during] 2026-11-05T04:03:33Z load1/5/15: 3.28 7.44 8.77
[during] 2026-11-05T04:03:43Z load1/5/15: 3.16 7.28 8.70
[during] 2026-11-05T04:03:53Z load1/5/15: 3.38 7.19 8.66
[during] 2026-11-05T04:04:03Z load1/5/15: 3.71 7.13 8.62
[during] 2026-11-05T04:04:13Z load1/5/15: 3.99 7.08 8.59
[during] 2026-11-05T04:04:23Z load1/5/15: 4.52 7.09 8.58
[during] 2026-11-05T04:04:33Z load1/5/15: 4.44 6.99 8.53
[during] 2026-11-05T04:04:43Z load1/5/15: 4.06 6.82 8.46
[during] 2026-11-05T04:04:53Z load1/5/15: 3.82 6.68 8.39
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
| values-10000 | oneoffice.read.string | 5 | 72.0 ms | 70.3 ms | 1.00x | 40.95 B | 47 MB | 537015189 |
| values-10000 | oneoffice.read.span | 5 | 70.0 ms | 68.0 ms | 0.97x | 39.48 B | 47 MB | 537015189 |
| values-10000 | exceldatareader.read | 5 | 111.5 ms | 109.7 ms | 1.55x | 206.83 B | 66 MB | 537015189 |
| values-10000 | miniexcel.read | 5 | 185.6 ms | 185.3 ms | 2.58x | 2005.73 B | 100 MB | 537015189 |
| values-10000 | sylvan.read | 5 | 67.5 ms | 67.3 ms | 0.94x | 5.54 B | 46 MB | 537015189 |
| values-100000 | oneoffice.read.string | 3 | 666.1 ms | 658.1 ms | 1.00x | 0.75 B | 44 MB | 12130167350 |
| values-100000 | oneoffice.read.span | 3 | 674.5 ms | 672.2 ms | 1.01x | 0.60 B | 44 MB | 12130167350 |
| values-100000 | exceldatareader.read | 3 | 1.24 s | 1.23 s | 1.86x | 213.69 B | 98 MB | 12130167350 |
| values-100000 | miniexcel.read | 3 | 2.61 s | 2.35 s | 3.91x | 1837.00 B | 107 MB | 12130167350 |
| values-100000 | sylvan.read | 3 | 685.7 ms | 682.4 ms | 1.03x | 0.66 B | 46 MB | 12130167350 |
| values-1000000 | oneoffice.read.string | 2 | 1.50 s | 1.46 s | 1.00x | 0.08 B | 48 MB | 796312017999 |
| values-1000000 | oneoffice.read.span | 2 | 1.39 s | 1.36 s | 0.93x | 0.06 B | 48 MB | 796312017999 |
| values-1000000 | exceldatareader.read | 2 | 4.21 s | 4.12 s | 2.80x | 216.86 B | 101 MB | 796312017999 |
| values-1000000 | miniexcel.read | 2 | 8.17 s | 8.05 s | 5.44x | 2021.05 B | 109 MB | 796312017999 |
| values-1000000 | sylvan.read | 2 | 2.58 s | 1.76 s | 1.72x | 0.19 B | 53 MB | 796312017999 |

## Bar: OneOffice against the fastest managed comparator

Ratio is OneOffice median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming read, typed values (every cell) | values-10000 | 72.0 ms | sylvan.read | 67.5 ms | 1.07x |
| Streaming read, typed values (every cell) | values-100000 | 666.1 ms | sylvan.read | 685.7 ms | 0.97x |
| Streaming read, typed values (every cell) | values-1000000 | 1.50 s | sylvan.read | 2.58 s | 0.58x |

Geometric mean of the ratios: 0.845x over 3 rows. Worst row: Streaming read, typed values (every cell), values-10000 at 1.07x.

