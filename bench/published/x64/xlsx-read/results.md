# Benchmark results

## Machine and load

```text
run: recheck-xlsx-read
group: xlsx read
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-05T08:39:38Z load1/5/15: 1.81 4.54 8.05
[after] 2026-11-05T08:41:37Z load1/5/15: 3.77 4.72 7.73
```

## Load while running (sampled)

```text
[during] 2026-11-05T08:39:52Z load1/5/15: 6.09 5.37 8.28
[during] 2026-11-05T08:40:02Z load1/5/15: 5.47 5.26 8.21
[during] 2026-11-05T08:40:12Z load1/5/15: 4.93 5.15 8.15
[during] 2026-11-05T08:40:22Z load1/5/15: 4.56 5.06 8.09
[during] 2026-11-05T08:40:32Z load1/5/15: 4.32 5.00 8.03
[during] 2026-11-05T08:40:42Z load1/5/15: 4.28 4.96 7.99
[during] 2026-11-05T08:40:52Z load1/5/15: 4.08 4.90 7.93
[during] 2026-11-05T08:41:02Z load1/5/15: 4.14 4.89 7.90
[during] 2026-11-05T08:41:12Z load1/5/15: 4.11 4.86 7.85
[during] 2026-11-05T08:41:22Z load1/5/15: 4.09 4.83 7.81
[during] 2026-11-05T08:41:32Z load1/5/15: 3.92 4.77 7.76
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
| values-10000 | oneoffice.read.string | 5 | 73.0 ms | 72.2 ms | 1.00x | 40.95 B | 47 MB | 537015189 |
| values-10000 | oneoffice.read.span | 5 | 72.8 ms | 71.7 ms | 1.00x | 39.49 B | 48 MB | 537015189 |
| values-10000 | exceldatareader.read | 5 | 120.8 ms | 120.6 ms | 1.65x | 206.83 B | 66 MB | 537015189 |
| values-10000 | miniexcel.read | 5 | 191.4 ms | 190.9 ms | 2.62x | 2005.73 B | 100 MB | 537015189 |
| values-10000 | sylvan.read | 5 | 72.6 ms | 71.9 ms | 0.99x | 5.54 B | 47 MB | 537015189 |
| values-100000 | oneoffice.read.string | 3 | 699.9 ms | 698.8 ms | 1.00x | 0.75 B | 44 MB | 12130167350 |
| values-100000 | oneoffice.read.span | 3 | 697.6 ms | 696.7 ms | 1.00x | 0.60 B | 44 MB | 12130167350 |
| values-100000 | exceldatareader.read | 3 | 1.21 s | 1.20 s | 1.72x | 213.69 B | 99 MB | 12130167350 |
| values-100000 | miniexcel.read | 3 | 2.75 s | 2.10 s | 3.92x | 1607.56 B | 107 MB | 12130167350 |
| values-100000 | sylvan.read | 3 | 716.1 ms | 716.1 ms | 1.02x | 0.66 B | 46 MB | 12130167350 |
| values-1000000 | oneoffice.read.string | 2 | 1.45 s | 1.44 s | 1.00x | 0.08 B | 49 MB | 796312017999 |
| values-1000000 | oneoffice.read.span | 2 | 1.43 s | 1.43 s | 0.99x | 0.06 B | 48 MB | 796312017999 |
| values-1000000 | exceldatareader.read | 2 | 4.12 s | 4.11 s | 2.84x | 216.86 B | 101 MB | 796312017999 |
| values-1000000 | miniexcel.read | 2 | 8.46 s | 8.39 s | 5.83x | 2021.05 B | 109 MB | 796312017999 |
| values-1000000 | sylvan.read | 2 | 1.81 s | 1.81 s | 1.25x | 0.19 B | 53 MB | 796312017999 |

## Bar: OneOffice against the fastest managed comparator

Ratio is OneOffice median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming read, typed values (every cell) | values-10000 | 73.0 ms | sylvan.read | 72.6 ms | 1.01x |
| Streaming read, typed values (every cell) | values-100000 | 699.9 ms | sylvan.read | 716.1 ms | 0.98x |
| Streaming read, typed values (every cell) | values-1000000 | 1.45 s | sylvan.read | 1.81 s | 0.80x |

Geometric mean of the ratios: 0.924x over 3 rows. Worst row: Streaming read, typed values (every cell), values-10000 at 1.01x.

