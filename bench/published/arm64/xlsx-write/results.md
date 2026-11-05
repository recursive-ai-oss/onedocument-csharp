# Benchmark results

## Machine and load

```text
run: arm64-xlsx-write
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
[before] 2026-11-05T04:26:06Z load1/5/15: 3.53 28.37 29.76
[after] 2026-11-05T04:27:14Z load1/5/15: 2.56 22.91 27.59
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:26:06Z load1/5/15: 3.53 28.37 29.76
[during] 2026-11-05T04:26:16Z load1/5/15: 3.52 27.56 29.45
[during] 2026-11-05T04:26:26Z load1/5/15: 3.45 26.75 29.14
[during] 2026-11-05T04:26:36Z load1/5/15: 3.23 25.93 28.83
[during] 2026-11-05T04:26:46Z load1/5/15: 2.89 25.11 28.50
[during] 2026-11-05T04:26:56Z load1/5/15: 2.89 24.38 28.20
[during] 2026-11-05T04:27:06Z load1/5/15: 2.75 23.64 27.90
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

## Streaming write

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| write-10000 | onedocument.write.shared | 5 | 23.4 ms | 22.8 ms | 1.00x | 8.81 B | 58 MB | 10000 |
| write-10000 | onedocument.write.inline | 5 | 22.9 ms | 22.7 ms | 0.98x | 4.03 B | 56 MB | 10000 |
| write-10000 | miniexcel.write | 5 | 68.6 ms | 68.5 ms | 2.93x | 571.13 B | 80 MB | 10000 |
| write-10000 | openxml.write | 5 | 297.7 ms | 296.7 ms | 12.72x | 817.18 B | 92 MB | 10000 |
| write-100000 | onedocument.write.shared | 3 | 226.9 ms | 221.7 ms | 1.00x | 0.88 B | 56 MB | 100000 |
| write-100000 | onedocument.write.inline | 3 | 233.6 ms | 228.9 ms | 1.03x | 0.40 B | 56 MB | 100000 |
| write-100000 | miniexcel.write | 3 | 693.1 ms | 687.6 ms | 3.05x | 440.39 B | 80 MB | 100000 |
| write-100000 | openxml.write | 3 | 822.2 ms | 808.5 ms | 3.62x | 791.18 B | 170 MB | 100000 |
| write-1000000 | onedocument.write.shared | 2 | 2.73 s | 2.39 s | 1.00x | 0.09 B | 57 MB | 1000000 |
| write-1000000 | onedocument.write.inline | 2 | 2.79 s | 1.20 s | 1.02x | 0.04 B | 57 MB | 1000000 |
| write-1000000 | miniexcel.write | 2 | 3.43 s | 2.93 s | 1.26x | 390.16 B | 82 MB | 1000000 |
| write-1000000 | openxml.write | 2 | 6.89 s | 6.89 s | 2.52x | 842.93 B | 964 MB | 1000000 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming write | write-10000 | 23.4 ms | miniexcel.write | 68.6 ms | 0.34x |
| Streaming write | write-100000 | 226.9 ms | miniexcel.write | 693.1 ms | 0.33x |
| Streaming write | write-1000000 | 2.73 s | miniexcel.write | 3.43 s | 0.80x |

Geometric mean of the ratios: 0.446x over 3 rows. Worst row: Streaming write, write-1000000 at 0.80x.

