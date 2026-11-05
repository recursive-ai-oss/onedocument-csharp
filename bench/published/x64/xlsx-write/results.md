# Benchmark results

## Machine and load

```text
run: x64-xlsx-write
filter: *Bench.Docx*
threads: 1
load override: forced start at load 3.67 (limit 2.0)
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-05T04:04:58Z load1/5/15: 3.67 6.60 8.36
[after] 2026-11-05T04:06:05Z load1/5/15: 3.17 5.80 7.95
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:04:58Z load1/5/15: 3.67 6.60 8.36
[during] 2026-11-05T04:05:08Z load1/5/15: 3.49 6.47 8.30
[during] 2026-11-05T04:05:18Z load1/5/15: 3.34 6.34 8.23
[during] 2026-11-05T04:05:28Z load1/5/15: 3.14 6.19 8.17
[during] 2026-11-05T04:05:38Z load1/5/15: 2.96 6.05 8.10
[during] 2026-11-05T04:05:49Z load1/5/15: 2.81 5.92 8.03
[during] 2026-11-05T04:05:59Z load1/5/15: 2.92 5.84 7.98
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
| write-10000 | onedocument.write.shared | 5 | 18.6 ms | 18.3 ms | 1.00x | 8.81 B | 49 MB | 10000 |
| write-10000 | onedocument.write.inline | 5 | 19.5 ms | 19.3 ms | 1.05x | 4.03 B | 47 MB | 10000 |
| write-10000 | miniexcel.write | 5 | 58.4 ms | 57.5 ms | 3.14x | 571.10 B | 89 MB | 10000 |
| write-10000 | openxml.write | 5 | 213.7 ms | 209.8 ms | 11.49x | 817.07 B | 114 MB | 10000 |
| write-100000 | onedocument.write.shared | 3 | 170.0 ms | 165.9 ms | 1.00x | 0.88 B | 47 MB | 100000 |
| write-100000 | onedocument.write.inline | 3 | 185.9 ms | 183.6 ms | 1.09x | 0.40 B | 47 MB | 100000 |
| write-100000 | miniexcel.write | 3 | 559.5 ms | 558.2 ms | 3.29x | 440.35 B | 101 MB | 100000 |
| write-100000 | openxml.write | 3 | 1.70 s | 744.3 ms | 9.99x | 781.58 B | 188 MB | 100000 |
| write-1000000 | onedocument.write.shared | 2 | 2.34 s | 1.92 s | 1.00x | 0.09 B | 49 MB | 1000000 |
| write-1000000 | onedocument.write.inline | 2 | 2.31 s | 2.15 s | 0.99x | 0.04 B | 48 MB | 1000000 |
| write-1000000 | miniexcel.write | 2 | 3.26 s | 2.55 s | 1.39x | 390.14 B | 104 MB | 1000000 |
| write-1000000 | openxml.write | 2 | 7.57 s | 7.54 s | 3.23x | 842.93 B | 1240 MB | 1000000 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming write | write-10000 | 18.6 ms | miniexcel.write | 58.4 ms | 0.32x |
| Streaming write | write-100000 | 170.0 ms | miniexcel.write | 559.5 ms | 0.30x |
| Streaming write | write-1000000 | 2.34 s | miniexcel.write | 3.26 s | 0.72x |

Geometric mean of the ratios: 0.411x over 3 rows. Worst row: Streaming write, write-1000000 at 0.72x.

