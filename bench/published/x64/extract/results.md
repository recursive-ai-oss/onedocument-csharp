# Benchmark results

## Machine and load

```text
run: x64-extract
group: fmt extract
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:47:57Z load1/5/15: 2.67 1.98 3.09
[after] 2026-11-06T08:48:51Z load1/5/15: 1.70 1.83 2.97
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:47:58Z load1/5/15: 2.67 1.98 3.09
[during] 2026-11-06T08:48:08Z load1/5/15: 2.49 1.97 3.07
[during] 2026-11-06T08:48:18Z load1/5/15: 2.26 1.93 3.05
[during] 2026-11-06T08:48:28Z load1/5/15: 2.07 1.90 3.03
[during] 2026-11-06T08:48:38Z load1/5/15: 1.90 1.87 3.00
[during] 2026-11-06T08:48:48Z load1/5/15: 1.76 1.84 2.98
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

## Text extraction, PPTX

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| deck-50.pptx | onedocument.pptx.extract | 15 | 6.1 ms | 6.0 ms | 1.00x | 2.0 MB | 59 MB | 9896 |
| deck-50.pptx | openxml.pptx.extract | 15 | 9.0 ms | 8.9 ms | 1.48x | 4.4 MB | 76 MB | 9647 |
| deck-500.pptx | onedocument.pptx.extract | 15 | 55.9 ms | 55.1 ms | 1.00x | 17.9 MB | 81 MB | 107163 |
| deck-500.pptx | openxml.pptx.extract | 15 | 88.3 ms | 86.6 ms | 1.58x | 42.6 MB | 115 MB | 104664 |

## Text extraction, XLSX

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000.xlsx | onedocument.xlsx.extract | 5 | 35.8 ms | 35.4 ms | 1.00x | 3.9 MB | 65 MB | 485800 |
| values-10000.xlsx | npoi.xlsx.extract | 5 | 759.5 ms | 443.1 ms | 21.22x | 248.5 MB | 189 MB | 884098 |
| values-10000.xlsx | openxml.xlsx.extract | 5 | 534.7 ms | 256.7 ms | 14.94x | 70.0 MB | 127 MB | 802780 |
| values-50000.xlsx | onedocument.xlsx.extract | 5 | 104.1 ms | 102.6 ms | 1.00x | 0.7 MB | 62 MB | 2429000 |
| values-50000.xlsx | npoi.xlsx.extract | 5 | 2.14 s | 1.57 s | 20.55x | 1245.5 MB | 517 MB | 4504118 |
| values-50000.xlsx | openxml.xlsx.extract | 5 | 1.11 s | 1.09 s | 10.62x | 347.1 MB | 127 MB | 4097658 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Text extraction, PPTX | deck-50.pptx | 6.1 ms | openxml.pptx.extract | 9.0 ms | 0.68x |
| Text extraction, PPTX | deck-500.pptx | 55.9 ms | openxml.pptx.extract | 88.3 ms | 0.63x |
| Text extraction, XLSX | values-10000.xlsx | 35.8 ms | openxml.xlsx.extract | 534.7 ms | 0.07x |
| Text extraction, XLSX | values-50000.xlsx | 104.1 ms | openxml.xlsx.extract | 1.11 s | 0.09x |

Geometric mean of the ratios: 0.228x over 4 rows. Worst row: Text extraction, PPTX, deck-50.pptx at 0.68x.

