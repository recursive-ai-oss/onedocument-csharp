# Benchmark results

## Machine and load

```text
run: arm64-xlsx-write
group: xlsx write
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T07:48:13Z load1/5/15: 2.00 2.25 2.36
[after] 2026-11-06T07:49:18Z load1/5/15: 2.18 2.28 2.36
```

## Load while running (sampled)

```text
[during] 2026-11-06T07:48:13Z load1/5/15: 2.00 2.25 2.36
[during] 2026-11-06T07:48:23Z load1/5/15: 2.00 2.24 2.35
[during] 2026-11-06T07:48:33Z load1/5/15: 2.15 2.26 2.36
[during] 2026-11-06T07:48:43Z load1/5/15: 2.57 2.35 2.39
[during] 2026-11-06T07:48:53Z load1/5/15: 2.48 2.34 2.39
[during] 2026-11-06T07:49:03Z load1/5/15: 2.32 2.31 2.38
[during] 2026-11-06T07:49:13Z load1/5/15: 2.20 2.28 2.36
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

## Streaming write

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated per cell | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| write-10000 | onedocument.write.shared | 5 | 22.7 ms | 22.3 ms | 1.00x | 8.81 B | 62 MB | 10000 |
| write-10000 | onedocument.write.inline | 5 | 22.7 ms | 22.5 ms | 1.00x | 4.03 B | 59 MB | 10000 |
| write-10000 | miniexcel.write | 5 | 66.0 ms | 65.7 ms | 2.91x | 571.11 B | 83 MB | 10000 |
| write-10000 | openxml.write | 5 | 298.3 ms | 297.5 ms | 13.14x | 817.18 B | 95 MB | 10000 |
| write-100000 | onedocument.write.shared | 3 | 215.0 ms | 214.0 ms | 1.00x | 0.88 B | 60 MB | 100000 |
| write-100000 | onedocument.write.inline | 3 | 222.5 ms | 221.9 ms | 1.03x | 0.40 B | 59 MB | 100000 |
| write-100000 | miniexcel.write | 3 | 647.2 ms | 646.1 ms | 3.01x | 440.39 B | 83 MB | 100000 |
| write-100000 | openxml.write | 3 | 1.39 s | 643.4 ms | 6.47x | 781.58 B | 173 MB | 100000 |
| write-1000000 | onedocument.write.shared | 2 | 2.59 s | 1.91 s | 1.00x | 0.09 B | 61 MB | 1000000 |
| write-1000000 | onedocument.write.inline | 2 | 2.80 s | 1.16 s | 1.08x | 0.04 B | 60 MB | 1000000 |
| write-1000000 | miniexcel.write | 2 | 3.31 s | 2.66 s | 1.28x | 390.16 B | 85 MB | 1000000 |
| write-1000000 | openxml.write | 2 | 6.53 s | 6.52 s | 2.52x | 842.93 B | 968 MB | 1000000 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming write | write-10000 | 22.7 ms | miniexcel.write | 66.0 ms | 0.34x |
| Streaming write | write-100000 | 215.0 ms | miniexcel.write | 647.2 ms | 0.33x |
| Streaming write | write-1000000 | 2.59 s | miniexcel.write | 3.31 s | 0.78x |

Geometric mean of the ratios: 0.447x over 3 rows. Worst row: Streaming write, write-1000000 at 0.78x.

