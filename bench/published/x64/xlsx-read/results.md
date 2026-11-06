# Benchmark results

## Machine and load

```text
run: x64-xlsx-read
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
[before] 2026-11-06T07:56:41Z load1/5/15: 1.08 4.29 12.45
[after] 2026-11-06T07:58:23Z load1/5/15: 1.09 3.36 11.28
```

## Load while running (sampled)

```text
[during] 2026-11-06T07:56:42Z load1/5/15: 1.08 4.29 12.45
[during] 2026-11-06T07:56:52Z load1/5/15: 1.07 4.18 12.33
[during] 2026-11-06T07:57:02Z load1/5/15: 1.06 4.07 12.20
[during] 2026-11-06T07:57:12Z load1/5/15: 1.05 3.97 12.08
[during] 2026-11-06T07:57:22Z load1/5/15: 1.04 3.87 11.96
[during] 2026-11-06T07:57:32Z load1/5/15: 1.03 3.78 11.85
[during] 2026-11-06T07:57:42Z load1/5/15: 1.03 3.68 11.73
[during] 2026-11-06T07:57:52Z load1/5/15: 1.02 3.60 11.61
[during] 2026-11-06T07:58:02Z load1/5/15: 1.02 3.51 11.50
[during] 2026-11-06T07:58:12Z load1/5/15: 1.02 3.43 11.39
[during] 2026-11-06T07:58:22Z load1/5/15: 1.09 3.36 11.28
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
| values-10000 | onedocument.read.string | 5 | 70.9 ms | 68.4 ms | 1.00x | 40.95 B | 52 MB | 537015189 |
| values-10000 | onedocument.read.span | 5 | 68.8 ms | 66.4 ms | 0.97x | 39.49 B | 52 MB | 537015189 |
| values-10000 | exceldatareader.read | 5 | 110.6 ms | 108.9 ms | 1.56x | 206.83 B | 70 MB | 537015189 |
| values-10000 | miniexcel.read | 5 | 179.3 ms | 175.1 ms | 2.53x | 2005.73 B | 105 MB | 537015189 |
| values-10000 | sylvan.read | 5 | 69.1 ms | 67.8 ms | 0.97x | 5.54 B | 51 MB | 537015189 |
| values-100000 | onedocument.read.string | 3 | 646.4 ms | 644.1 ms | 1.00x | 0.75 B | 49 MB | 12130167350 |
| values-100000 | onedocument.read.span | 3 | 682.1 ms | 678.3 ms | 1.06x | 0.60 B | 49 MB | 12130167350 |
| values-100000 | exceldatareader.read | 3 | 1.14 s | 1.13 s | 1.76x | 213.69 B | 103 MB | 12130167350 |
| values-100000 | miniexcel.read | 3 | 3.24 s | 2.14 s | 5.02x | 2005.04 B | 112 MB | 12130167350 |
| values-100000 | sylvan.read | 3 | 681.1 ms | 677.0 ms | 1.05x | 0.66 B | 51 MB | 12130167350 |
| values-1000000 | onedocument.read.string | 2 | 1.34 s | 1.33 s | 1.00x | 0.08 B | 53 MB | 796312017999 |
| values-1000000 | onedocument.read.span | 2 | 1.34 s | 1.33 s | 1.00x | 0.06 B | 53 MB | 796312017999 |
| values-1000000 | exceldatareader.read | 2 | 3.84 s | 3.84 s | 2.87x | 216.86 B | 106 MB | 796312017999 |
| values-1000000 | miniexcel.read | 2 | 7.86 s | 7.73 s | 5.87x | 2021.05 B | 113 MB | 796312017999 |
| values-1000000 | sylvan.read | 2 | 1.75 s | 1.74 s | 1.31x | 0.19 B | 58 MB | 796312017999 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming read, typed values (every cell) | values-10000 | 70.9 ms | sylvan.read | 69.1 ms | 1.03x |
| Streaming read, typed values (every cell) | values-100000 | 646.4 ms | sylvan.read | 681.1 ms | 0.95x |
| Streaming read, typed values (every cell) | values-1000000 | 1.34 s | sylvan.read | 1.75 s | 0.77x |

Geometric mean of the ratios: 0.907x over 3 rows. Worst row: Streaming read, typed values (every cell), values-10000 at 1.03x.

