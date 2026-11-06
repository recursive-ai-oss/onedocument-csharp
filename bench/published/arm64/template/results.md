# Benchmark results

## Machine and load

```text
run: arm64-template
group: fmt template
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T09:40:43Z load1/5/15: 2.39 2.17 2.03
[after] 2026-11-06T09:40:59Z load1/5/15: 2.26 2.16 2.03
```

## Load while running (sampled)

```text
[during] 2026-11-06T09:40:44Z load1/5/15: 2.35 2.17 2.03
[during] 2026-11-06T09:40:54Z load1/5/15: 2.33 2.17 2.03
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

## Template fill, XLSX (loop over N items)

| Input | Row | Reps | Median | Min | Ratio to OneDocument | Allocated | Peak working set | Checksum |
|---|---|---:|---:|---:|---:|---:|---:|---|
| n-1000 | onedocument.tpl.xlsx | 15 | 11.5 ms | 10.0 ms | 1.00x | 8.7 MB | 95 MB | 15071 |
| n-1000 | closedxmlreport.tpl.xlsx | 3 | 160.8 ms | 159.9 ms | 13.98x | 48.9 MB | 187 MB | 25504 |
| n-5000 | onedocument.tpl.xlsx | 15 | 40.0 ms | 38.8 ms | 1.00x | 25.0 MB | 107 MB | 55231 |
| n-5000 | closedxmlreport.tpl.xlsx | 3 | 749.7 ms | 744.0 ms | 18.74x | 223.6 MB | 218 MB | 90792 |

## Rows with no managed comparator (information)

| Input | Row | Reps | Median | Min | Allocated | Peak working set |
|---|---|---:|---:|---:|---:|---:|
| n-1000 | onedocument.tpl.docx | 15 | 14.5 ms | 13.3 ms | 9.5 MB | 97 MB |
| n-5000 | onedocument.tpl.docx | 15 | 57.4 ms | 54.5 ms | 41.4 MB | 111 MB |

## Bar: OneDocument against the fastest like-for-like managed comparator

Ratio is OneDocument median / the fastest comparator's median for the same operation and input; at or below 1.00 is faster.

| Operation | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| Template fill, XLSX (loop over N items) | n-1000 | 11.5 ms | closedxmlreport.tpl.xlsx | 160.8 ms | 0.07x |
| Template fill, XLSX (loop over N items) | n-5000 | 40.0 ms | closedxmlreport.tpl.xlsx | 749.7 ms | 0.05x |

Geometric mean of the ratios: 0.062x over 2 rows. Worst row: Template fill, XLSX (loop over N items), n-1000 at 0.07x.

