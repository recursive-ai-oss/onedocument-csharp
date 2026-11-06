# Benchmark results

## Machine and load

```text
run: x64-xls-write
group: fmt xls-write
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:16:32Z load1/5/15: 1.15 2.73 6.97
[after] 2026-11-06T08:16:49Z load1/5/15: 1.12 2.65 6.88
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:16:33Z load1/5/15: 1.15 2.73 6.97
[during] 2026-11-06T08:16:43Z load1/5/15: 1.13 2.68 6.91
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
| n-10000 | onedocument.xls.write | 15 | 73.3 ms | 71.7 ms | 1.00x | 46.7 MB | 119 MB | 1657344 |
| n-10000 | npoi.xls.write | 15 | 67.9 ms | 67.2 ms | 0.93x | 48.5 MB | 117 MB | 1811968 |
| n-50000 | onedocument.xls.write | 3 | 353.1 ms | 350.2 ms | 1.00x | 250.3 MB | 227 MB | 8231936 |
| n-50000 | npoi.xls.write | 3 | 744.3 ms | 729.0 ms | 2.11x | 243.5 MB | 324 MB | 9003520 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| XLS write (build 10 columns per row, save) | n-10000 | 73.3 ms | npoi.xls.write | 67.9 ms | 1.08x |
| XLS write (build 10 columns per row, save) | n-50000 | 353.1 ms | npoi.xls.write | 744.3 ms | 0.47x |

Geometric mean of the ratios: 0.716x over 2 rows. Worst row: XLS write (build 10 columns per row, save), n-10000 at 1.08x.

