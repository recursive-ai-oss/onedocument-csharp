# Benchmark results

## Machine and load

```text
run: x64-xlsx-write
group: xlsx write
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T07:55:36Z load1/5/15: 1.24 5.09 13.28
[after] 2026-11-06T07:56:41Z load1/5/15: 1.08 4.29 12.45
```

## Load while running (sampled)

```text
[during] 2026-11-06T07:55:37Z load1/5/15: 1.24 5.09 13.28
[during] 2026-11-06T07:55:47Z load1/5/15: 1.20 4.95 13.15
[during] 2026-11-06T07:55:57Z load1/5/15: 1.17 4.82 13.02
[during] 2026-11-06T07:56:07Z load1/5/15: 1.14 4.70 12.89
[during] 2026-11-06T07:56:17Z load1/5/15: 1.12 4.57 12.76
[during] 2026-11-06T07:56:27Z load1/5/15: 1.10 4.46 12.64
[during] 2026-11-06T07:56:37Z load1/5/15: 1.08 4.34 12.51
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
| write-10000 | onedocument.write.shared | 5 | 17.8 ms | 17.3 ms | 1.00x | 8.81 B | 54 MB | 10000 |
| write-10000 | onedocument.write.inline | 5 | 18.6 ms | 18.4 ms | 1.04x | 4.03 B | 52 MB | 10000 |
| write-10000 | miniexcel.write | 5 | 58.3 ms | 57.3 ms | 3.28x | 571.14 B | 94 MB | 10000 |
| write-10000 | openxml.write | 5 | 204.5 ms | 204.0 ms | 11.49x | 817.08 B | 119 MB | 10000 |
| write-100000 | onedocument.write.shared | 3 | 165.3 ms | 161.9 ms | 1.00x | 0.88 B | 52 MB | 100000 |
| write-100000 | onedocument.write.inline | 3 | 184.4 ms | 183.3 ms | 1.12x | 0.40 B | 52 MB | 100000 |
| write-100000 | miniexcel.write | 3 | 546.6 ms | 545.7 ms | 3.31x | 440.35 B | 106 MB | 100000 |
| write-100000 | openxml.write | 3 | 1.61 s | 772.7 ms | 9.77x | 781.58 B | 193 MB | 100000 |
| write-1000000 | onedocument.write.shared | 2 | 2.29 s | 1.83 s | 1.00x | 0.09 B | 52 MB | 1000000 |
| write-1000000 | onedocument.write.inline | 2 | 2.23 s | 2.21 s | 0.97x | 0.04 B | 53 MB | 1000000 |
| write-1000000 | miniexcel.write | 2 | 3.07 s | 2.38 s | 1.34x | 390.14 B | 109 MB | 1000000 |
| write-1000000 | openxml.write | 2 | 7.35 s | 7.32 s | 3.21x | 842.93 B | 1245 MB | 1000000 |

## Bar: OneDocument against the fastest managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster. Gate rows only (the informational rows above are not in it).

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Streaming write | write-10000 | 17.8 ms | miniexcel.write | 58.3 ms | 0.31x |
| Streaming write | write-100000 | 165.3 ms | miniexcel.write | 546.6 ms | 0.30x |
| Streaming write | write-1000000 | 2.29 s | miniexcel.write | 3.07 s | 0.75x |

Geometric mean of the ratios: 0.410x over 3 rows. Worst row: Streaming write, write-1000000 at 0.75x.

