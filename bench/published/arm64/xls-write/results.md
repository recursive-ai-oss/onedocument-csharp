# Benchmark results

## Machine and load

```text
run: arm64-xls-write
group: fmt xls-write
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T08:36:00Z load1/5/15: 2.58 2.08 1.98
[after] 2026-11-06T08:36:16Z load1/5/15: 2.61 2.11 2.00
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:36:01Z load1/5/15: 2.58 2.08 1.98
[during] 2026-11-06T08:36:11Z load1/5/15: 2.49 2.08 1.98
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

## XLS write (build 10 columns per row, save)

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| n-10000 | onedocument.xls.write | 15 | 77.0 ms | 75.9 ms | 1.00x | 46.7 MB | 113 MB | 1657344 |
| n-10000 | npoi.xls.write | 3 | 131.2 ms | 129.8 ms | 1.70x | 48.5 MB | 131 MB | 1811968 |
| n-50000 | onedocument.xls.write | 3 | 368.8 ms | 368.6 ms | 1.00x | 250.3 MB | 178 MB | 8231936 |
| n-50000 | npoi.xls.write | 3 | 773.9 ms | 755.5 ms | 2.10x | 243.5 MB | 298 MB | 9003520 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLS write (build 10 columns per row, save) | n-10000 | 77.0 ms | npoi.xls.write | 131.2 ms | 0.59x |
| XLS write (build 10 columns per row, save) | n-50000 | 368.8 ms | npoi.xls.write | 773.9 ms | 0.48x |

Geometric mean of the ratios: 0.529x over 2 rows. Worst row: XLS write (build 10 columns per row, save), n-10000 at 0.59x.

