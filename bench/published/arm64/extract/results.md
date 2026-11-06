# Benchmark results

## Machine and load

```text
run: arm64-extract
group: fmt extract
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T09:39:50Z load1/5/15: 2.38 2.11 2.00
[after] 2026-11-06T09:40:43Z load1/5/15: 2.39 2.17 2.03
```

## Load while running (sampled)

```text
[during] 2026-11-06T09:39:50Z load1/5/15: 2.38 2.11 2.00
[during] 2026-11-06T09:40:00Z load1/5/15: 2.69 2.18 2.03
[during] 2026-11-06T09:40:10Z load1/5/15: 2.58 2.18 2.03
[during] 2026-11-06T09:40:21Z load1/5/15: 2.56 2.19 2.03
[during] 2026-11-06T09:40:31Z load1/5/15: 2.55 2.20 2.04
[during] 2026-11-06T09:40:41Z load1/5/15: 2.39 2.17 2.03
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
| deck-50.pptx | onedocument.pptx.extract | 15 | 6.3 ms | 6.3 ms | 1.00x | 2.0 MB | 66 MB | 9896 |
| deck-50.pptx | openxml.pptx.extract | 15 | 11.4 ms | 11.3 ms | 1.81x | 4.4 MB | 93 MB | 9647 |
| deck-500.pptx | onedocument.pptx.extract | 15 | 62.4 ms | 61.6 ms | 1.00x | 17.9 MB | 76 MB | 107163 |
| deck-500.pptx | openxml.pptx.extract | 5 | 115.0 ms | 114.7 ms | 1.84x | 42.6 MB | 100 MB | 104664 |

## Text extraction, XLSX

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| values-10000.xlsx | onedocument.xlsx.extract | 5 | 26.5 ms | 26.4 ms | 1.00x | 3.9 MB | 72 MB | 485800 |
| values-10000.xlsx | npoi.xlsx.extract | 5 | 543.4 ms | 401.3 ms | 20.51x | 251.9 MB | 174 MB | 884098 |
| values-10000.xlsx | openxml.xlsx.extract | 5 | 256.9 ms | 254.9 ms | 9.69x | 70.0 MB | 100 MB | 802780 |
| values-50000.xlsx | onedocument.xlsx.extract | 5 | 983.7 ms | 117.5 ms | 1.00x | 0.7 MB | 69 MB | 2429000 |
| values-50000.xlsx | npoi.xlsx.extract | 5 | 1.88 s | 1.41 s | 1.91x | 1245.5 MB | 516 MB | 4504118 |
| values-50000.xlsx | openxml.xlsx.extract | 5 | 1.20 s | 1.08 s | 1.22x | 347.2 MB | 101 MB | 4097658 |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Text extraction, PPTX | deck-50.pptx | 6.3 ms | openxml.pptx.extract | 11.4 ms | 0.55x |
| Text extraction, PPTX | deck-500.pptx | 62.4 ms | openxml.pptx.extract | 115.0 ms | 0.54x |
| Text extraction, XLSX | values-10000.xlsx | 26.5 ms | openxml.xlsx.extract | 256.9 ms | 0.10x |
| Text extraction, XLSX | values-50000.xlsx | 983.7 ms | openxml.xlsx.extract | 1.20 s | 0.82x |

Geometric mean of the ratios: 0.399x over 4 rows. Worst row: Text extraction, XLSX, values-50000.xlsx at 0.82x.

