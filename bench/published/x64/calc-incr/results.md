# Benchmark results

## Machine and load

```text
run: x64-calc-incr
group: fmt calc-incr
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:31:44Z load1/5/15: 1.12 3.12 5.98
[after] 2026-11-06T08:45:33Z load1/5/15: 1.06 1.19 3.05
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:31:45Z load1/5/15: 1.11 3.08 5.96
[during] 2026-11-06T08:31:55Z load1/5/15: 1.17 3.03 5.91
[during] 2026-11-06T08:32:05Z load1/5/15: 1.14 2.96 5.85
[during] 2026-11-06T08:32:15Z load1/5/15: 1.12 2.90 5.80
[during] 2026-11-06T08:32:25Z load1/5/15: 1.10 2.83 5.75
[during] 2026-11-06T08:32:35Z load1/5/15: 1.16 2.79 5.70
[during] 2026-11-06T08:32:45Z load1/5/15: 1.14 2.73 5.65
[during] 2026-11-06T08:32:55Z load1/5/15: 1.11 2.67 5.60
[during] 2026-11-06T08:33:05Z load1/5/15: 1.10 2.62 5.55
[during] 2026-11-06T08:33:15Z load1/5/15: 1.08 2.56 5.50
[during] 2026-11-06T08:33:25Z load1/5/15: 1.07 2.51 5.45
[during] 2026-11-06T08:33:35Z load1/5/15: 1.06 2.46 5.41
[during] 2026-11-06T08:33:45Z load1/5/15: 1.05 2.41 5.36
[during] 2026-11-06T08:33:55Z load1/5/15: 1.19 2.40 5.32
[during] 2026-11-06T08:34:05Z load1/5/15: 1.16 2.35 5.28
[during] 2026-11-06T08:34:15Z load1/5/15: 1.14 2.31 5.23
[during] 2026-11-06T08:34:25Z load1/5/15: 1.12 2.26 5.18
[during] 2026-11-06T08:34:35Z load1/5/15: 1.10 2.22 5.14
[during] 2026-11-06T08:34:45Z load1/5/15: 1.08 2.18 5.09
[during] 2026-11-06T08:34:55Z load1/5/15: 1.07 2.14 5.05
[during] 2026-11-06T08:35:05Z load1/5/15: 1.06 2.10 5.00
[during] 2026-11-06T08:35:15Z load1/5/15: 1.05 2.07 4.96
[during] 2026-11-06T08:35:25Z load1/5/15: 1.12 2.05 4.92
[during] 2026-11-06T08:35:35Z load1/5/15: 1.10 2.01 4.88
[during] 2026-11-06T08:35:45Z load1/5/15: 1.09 1.98 4.84
[during] 2026-11-06T08:35:55Z load1/5/15: 1.07 1.95 4.80
[during] 2026-11-06T08:36:05Z load1/5/15: 1.06 1.91 4.76
[during] 2026-11-06T08:36:15Z load1/5/15: 1.05 1.88 4.72
[during] 2026-11-06T08:36:25Z load1/5/15: 1.04 1.85 4.68
[during] 2026-11-06T08:36:35Z load1/5/15: 1.04 1.83 4.64
[during] 2026-11-06T08:36:45Z load1/5/15: 1.03 1.80 4.60
[during] 2026-11-06T08:36:55Z load1/5/15: 1.02 1.77 4.56
[during] 2026-11-06T08:37:05Z load1/5/15: 1.10 1.76 4.52
[during] 2026-11-06T08:37:15Z load1/5/15: 1.08 1.74 4.49
[during] 2026-11-06T08:37:25Z load1/5/15: 1.07 1.71 4.45
[during] 2026-11-06T08:37:35Z load1/5/15: 1.06 1.69 4.41
[during] 2026-11-06T08:37:45Z load1/5/15: 1.05 1.66 4.37
[during] 2026-11-06T08:37:55Z load1/5/15: 1.04 1.64 4.34
[during] 2026-11-06T08:38:05Z load1/5/15: 1.04 1.62 4.30
[during] 2026-11-06T08:38:15Z load1/5/15: 1.11 1.62 4.27
[during] 2026-11-06T08:38:25Z load1/5/15: 1.09 1.60 4.23
[during] 2026-11-06T08:38:35Z load1/5/15: 1.08 1.58 4.20
[during] 2026-11-06T08:38:45Z load1/5/15: 1.07 1.56 4.16
[during] 2026-11-06T08:38:55Z load1/5/15: 1.06 1.54 4.13
[during] 2026-11-06T08:39:05Z load1/5/15: 1.05 1.52 4.10
[during] 2026-11-06T08:39:15Z load1/5/15: 1.04 1.50 4.06
[during] 2026-11-06T08:39:25Z load1/5/15: 1.03 1.49 4.03
[during] 2026-11-06T08:39:35Z load1/5/15: 1.03 1.47 4.00
[during] 2026-11-06T08:39:45Z load1/5/15: 1.02 1.45 3.96
[during] 2026-11-06T08:39:55Z load1/5/15: 1.02 1.44 3.93
[during] 2026-11-06T08:40:05Z load1/5/15: 1.02 1.42 3.90
[during] 2026-11-06T08:40:15Z load1/5/15: 1.01 1.41 3.87
[during] 2026-11-06T08:40:25Z load1/5/15: 1.01 1.39 3.84
[during] 2026-11-06T08:40:35Z load1/5/15: 1.01 1.38 3.81
[during] 2026-11-06T08:40:45Z load1/5/15: 1.01 1.37 3.78
[during] 2026-11-06T08:40:55Z load1/5/15: 1.00 1.36 3.75
[during] 2026-11-06T08:41:05Z load1/5/15: 1.00 1.34 3.71
[during] 2026-11-06T08:41:15Z load1/5/15: 1.00 1.33 3.69
[during] 2026-11-06T08:41:25Z load1/5/15: 1.00 1.32 3.66
[during] 2026-11-06T08:41:35Z load1/5/15: 1.00 1.31 3.63
[during] 2026-11-06T08:41:45Z load1/5/15: 1.00 1.30 3.60
[during] 2026-11-06T08:41:55Z load1/5/15: 1.00 1.29 3.57
[during] 2026-11-06T08:42:05Z load1/5/15: 1.00 1.28 3.54
[during] 2026-11-06T08:42:15Z load1/5/15: 1.00 1.27 3.51
[during] 2026-11-06T08:42:25Z load1/5/15: 1.00 1.26 3.49
[during] 2026-11-06T08:42:35Z load1/5/15: 1.00 1.25 3.46
[during] 2026-11-06T08:42:45Z load1/5/15: 1.00 1.24 3.43
[during] 2026-11-06T08:42:55Z load1/5/15: 1.00 1.23 3.41
[during] 2026-11-06T08:43:05Z load1/5/15: 1.00 1.23 3.38
[during] 2026-11-06T08:43:15Z load1/5/15: 1.22 1.27 3.37
[during] 2026-11-06T08:43:25Z load1/5/15: 1.19 1.26 3.34
[during] 2026-11-06T08:43:35Z load1/5/15: 1.16 1.25 3.32
[during] 2026-11-06T08:43:45Z load1/5/15: 1.13 1.24 3.29
[during] 2026-11-06T08:43:55Z load1/5/15: 1.11 1.23 3.27
[during] 2026-11-06T08:44:05Z load1/5/15: 1.17 1.24 3.25
[during] 2026-11-06T08:44:15Z load1/5/15: 1.14 1.23 3.22
[during] 2026-11-06T08:44:25Z load1/5/15: 1.19 1.24 3.21
[during] 2026-11-06T08:44:35Z load1/5/15: 1.16 1.23 3.18
[during] 2026-11-06T08:44:45Z load1/5/15: 1.14 1.22 3.16
[during] 2026-11-06T08:44:55Z load1/5/15: 1.12 1.22 3.13
[during] 2026-11-06T08:45:05Z load1/5/15: 1.10 1.21 3.11
[during] 2026-11-06T08:45:15Z load1/5/15: 1.08 1.20 3.09
[during] 2026-11-06T08:45:25Z load1/5/15: 1.07 1.19 3.07
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

## Recalculation, incremental (one input cell changed)

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| calc-10000.xlsx | onedocument.calc.incr | 15 | 0.7 ms | 0.6 ms | 1.00x | 0.5 MB | 143 MB | 66031877 |
| calc-10000.xlsx | closedxml.calc.incr | 15 | 8.8 ms | 7.9 ms | 12.57x | 0.0 MB | 206 MB | 66031877 |
| calc-10000.xlsx | epplus.calc.incr | 3 | 1.04 s | 815.5 ms | 1488.14x | 407.0 MB | 182 MB | 66031878 |
| calc-10000.xlsx | npoi.calc.incr | 15 | 6.7 ms | 4.2 ms | 9.57x | 3.5 MB | 197 MB | 66031877 |
| calc-50000.xlsx | onedocument.calc.incr | 15 | 3.1 ms | 2.1 ms | 1.00x | 2.3 MB | 228 MB | 329961877 |
| calc-50000.xlsx | closedxml.calc.incr | 15 | 64.6 ms | 55.6 ms | 20.84x | 0.0 MB | 505 MB | 329961877 |
| calc-50000.xlsx | epplus.calc.incr | 3 | 2.32 s | 2.31 s | 748.10x | 1900.1 MB | 370 MB | 329961878 |
| calc-50000.xlsx | npoi.calc.incr | 15 | 47.0 ms | 42.9 ms | 15.16x | 16.2 MB | 665 MB | 329961877 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Recalculation, incremental (one input cell changed) | calc-10000.xlsx | 0.7 ms | npoi.calc.incr | 6.7 ms | 0.10x |
| Recalculation, incremental (one input cell changed) | calc-50000.xlsx | 3.1 ms | npoi.calc.incr | 47.0 ms | 0.07x |

Geometric mean of the ratios: 0.083x over 2 rows. Worst row: Recalculation, incremental (one input cell changed), calc-10000.xlsx at 0.10x.

