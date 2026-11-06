# Benchmark results

## Machine and load

```text
run: x64-calc-cold
group: fmt calc-cold
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:27:07Z load1/5/15: 4.73 6.24 7.70
[after] 2026-11-06T08:31:44Z load1/5/15: 1.12 3.12 5.98
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:27:08Z load1/5/15: 4.73 6.24 7.70
[during] 2026-11-06T08:27:18Z load1/5/15: 4.16 6.07 7.62
[during] 2026-11-06T08:27:28Z load1/5/15: 3.67 5.90 7.55
[during] 2026-11-06T08:27:38Z load1/5/15: 3.26 5.74 7.48
[during] 2026-11-06T08:27:48Z load1/5/15: 2.91 5.58 7.41
[during] 2026-11-06T08:27:58Z load1/5/15: 2.62 5.43 7.34
[during] 2026-11-06T08:28:08Z load1/5/15: 2.37 5.29 7.27
[during] 2026-11-06T08:28:18Z load1/5/15: 2.16 5.14 7.21
[during] 2026-11-06T08:28:28Z load1/5/15: 1.98 5.01 7.14
[during] 2026-11-06T08:28:38Z load1/5/15: 1.83 4.88 7.07
[during] 2026-11-06T08:28:48Z load1/5/15: 1.70 4.75 7.01
[during] 2026-11-06T08:28:58Z load1/5/15: 1.59 4.62 6.94
[during] 2026-11-06T08:29:08Z load1/5/15: 1.50 4.50 6.88
[during] 2026-11-06T08:29:18Z load1/5/15: 1.42 4.39 6.81
[during] 2026-11-06T08:29:28Z load1/5/15: 1.36 4.27 6.75
[during] 2026-11-06T08:29:38Z load1/5/15: 1.30 4.17 6.69
[during] 2026-11-06T08:29:48Z load1/5/15: 1.33 4.08 6.63
[during] 2026-11-06T08:29:58Z load1/5/15: 1.28 3.98 6.57
[during] 2026-11-06T08:30:08Z load1/5/15: 1.32 3.89 6.52
[during] 2026-11-06T08:30:18Z load1/5/15: 1.27 3.80 6.46
[during] 2026-11-06T08:30:28Z load1/5/15: 1.23 3.71 6.40
[during] 2026-11-06T08:30:38Z load1/5/15: 1.19 3.62 6.34
[during] 2026-11-06T08:30:48Z load1/5/15: 1.16 3.53 6.28
[during] 2026-11-06T08:30:58Z load1/5/15: 1.14 3.45 6.23
[during] 2026-11-06T08:31:08Z load1/5/15: 1.11 3.36 6.17
[during] 2026-11-06T08:31:18Z load1/5/15: 1.10 3.29 6.11
[during] 2026-11-06T08:31:28Z load1/5/15: 1.16 3.23 6.06
[during] 2026-11-06T08:31:38Z load1/5/15: 1.14 3.15 6.01
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

## Recalculation, cold (every formula)

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| calc-10000.xlsx | onedocument.calc.cold | 3 | 516.2 ms | 509.3 ms | 1.00x | 162.6 MB | 139 MB | 65982500 |
| calc-10000.xlsx | closedxml.calc.cold | 3 | 1.81 s | 1.77 s | 3.51x | 231.8 MB | 202 MB | 65982500 |
| calc-10000.xlsx | epplus.calc.cold | 3 | 1.30 s | 1.28 s | 2.52x | 415.4 MB | 162 MB | 65982500 |
| calc-10000.xlsx | npoi.calc.cold | 3 | 1.11 s | 921.1 ms | 2.15x | 377.2 MB | 188 MB | 65982500 |
| calc-50000.xlsx | onedocument.calc.cold | 3 | 1.24 s | 1.23 s | 1.00x | 804.3 MB | 217 MB | 329912500 |
| calc-50000.xlsx | closedxml.calc.cold | 3 | 36.72 s | 36.70 s | 29.70x | 1163.0 MB | 493 MB | 329912500 |
| calc-50000.xlsx | epplus.calc.cold | 3 | 2.30 s | 2.28 s | 1.86x | 1900.2 MB | 340 MB | 329912500 |
| calc-50000.xlsx | npoi.calc.cold | 3 | 2.79 s | 2.77 s | 2.25x | 1905.1 MB | 652 MB | 329912500 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Recalculation, cold (every formula) | calc-10000.xlsx | 516.2 ms | npoi.calc.cold | 1.11 s | 0.47x |
| Recalculation, cold (every formula) | calc-50000.xlsx | 1.24 s | epplus.calc.cold | 2.30 s | 0.54x |

Geometric mean of the ratios: 0.500x over 2 rows. Worst row: Recalculation, cold (every formula), calc-50000.xlsx at 0.54x.

