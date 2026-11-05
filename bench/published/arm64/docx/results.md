# Benchmark results

## Machine and load

```text
run: arm64-docx
filter: *Bench.Docx*
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-05T04:29:36Z load1/5/15: 2.39 15.29 23.79
[after] 2026-11-05T04:38:06Z load1/5/15: 2.04 4.60 14.13
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:29:37Z load1/5/15: 2.39 15.29 23.79
[during] 2026-11-05T04:29:47Z load1/5/15: 2.40 14.87 23.54
[during] 2026-11-05T04:29:57Z load1/5/15: 2.64 14.51 23.31
[during] 2026-11-05T04:30:07Z load1/5/15: 2.54 14.10 23.06
[during] 2026-11-05T04:30:17Z load1/5/15: 2.46 13.70 22.81
[during] 2026-11-05T04:30:27Z load1/5/15: 2.47 13.33 22.58
[during] 2026-11-05T04:30:37Z load1/5/15: 2.39 12.96 22.34
[during] 2026-11-05T04:30:47Z load1/5/15: 2.49 12.63 22.11
[during] 2026-11-05T04:30:57Z load1/5/15: 2.72 12.35 21.90
[during] 2026-11-05T04:31:07Z load1/5/15: 2.85 12.05 21.68
[during] 2026-11-05T04:31:17Z load1/5/15: 2.72 11.72 21.45
[during] 2026-11-05T04:31:27Z load1/5/15: 2.61 11.40 21.22
[during] 2026-11-05T04:31:37Z load1/5/15: 2.43 11.08 20.99
[during] 2026-11-05T04:31:47Z load1/5/15: 2.37 10.78 20.77
[during] 2026-11-05T04:31:57Z load1/5/15: 2.24 10.47 20.54
[during] 2026-11-05T04:32:07Z load1/5/15: 2.21 10.19 20.33
[during] 2026-11-05T04:32:17Z load1/5/15: 2.17 9.92 20.11
[during] 2026-11-05T04:32:27Z load1/5/15: 2.37 9.71 19.92
[during] 2026-11-05T04:32:37Z load1/5/15: 2.30 9.45 19.71
[during] 2026-11-05T04:32:47Z load1/5/15: 2.32 9.22 19.51
[during] 2026-11-05T04:32:57Z load1/5/15: 2.35 9.00 19.31
[during] 2026-11-05T04:33:07Z load1/5/15: 2.30 8.77 19.10
[during] 2026-11-05T04:33:17Z load1/5/15: 2.41 8.58 18.92
[during] 2026-11-05T04:33:27Z load1/5/15: 2.42 8.38 18.72
[during] 2026-11-05T04:33:37Z load1/5/15: 2.44 8.19 18.53
[during] 2026-11-05T04:33:47Z load1/5/15: 2.37 7.98 18.34
[during] 2026-11-05T04:33:57Z load1/5/15: 2.46 7.82 18.16
[during] 2026-11-05T04:34:07Z load1/5/15: 2.39 7.62 17.97
[during] 2026-11-05T04:34:17Z load1/5/15: 2.25 7.42 17.78
[during] 2026-11-05T04:34:27Z load1/5/15: 2.29 7.26 17.60
[during] 2026-11-05T04:34:37Z load1/5/15: 2.24 7.09 17.42
[during] 2026-11-05T04:34:47Z load1/5/15: 2.13 6.90 17.23
[during] 2026-11-05T04:34:57Z load1/5/15: 2.27 6.77 17.06
[during] 2026-11-05T04:35:07Z load1/5/15: 2.39 6.65 16.90
[during] 2026-11-05T04:35:17Z load1/5/15: 2.41 6.51 16.73
[during] 2026-11-05T04:35:27Z load1/5/15: 2.34 6.36 16.56
[during] 2026-11-05T04:35:37Z load1/5/15: 2.28 6.22 16.39
[during] 2026-11-05T04:35:47Z load1/5/15: 2.32 6.10 16.22
[during] 2026-11-05T04:35:57Z load1/5/15: 2.26 5.96 16.06
[during] 2026-11-05T04:36:07Z load1/5/15: 2.14 5.81 15.89
[during] 2026-11-05T04:36:17Z load1/5/15: 2.27 5.72 15.74
[during] 2026-11-05T04:36:27Z load1/5/15: 2.31 5.61 15.58
[during] 2026-11-05T04:36:37Z load1/5/15: 2.26 5.49 15.42
[during] 2026-11-05T04:36:47Z load1/5/15: 2.14 5.36 15.26
[during] 2026-11-05T04:36:57Z load1/5/15: 2.05 5.23 15.10
[during] 2026-11-05T04:37:07Z load1/5/15: 1.96 5.11 14.94
[during] 2026-11-05T04:37:17Z load1/5/15: 1.97 5.01 14.79
[during] 2026-11-05T04:37:27Z load1/5/15: 1.97 4.91 14.64
[during] 2026-11-05T04:37:37Z load1/5/15: 2.06 4.83 14.49
[during] 2026-11-05T04:37:47Z load1/5/15: 1.97 4.72 14.34
[during] 2026-11-05T04:37:57Z load1/5/15: 2.14 4.71 14.28
```

## Versions

```text
runtime	.NET 10.0.14
OneOffice	0.1.0-preview.1
DocumentFormat.OpenXml	3.5.1
NPOI	2.7.6
ClosedXML	0.105.1
MiniExcel	1.46.0
Sylvan.Data.Excel	0.5.8
ExcelDataReader	3.9.0
EPPlus	4.5.3.3
BenchmarkDotNet	0.15.8
```

## DocxEdit

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 17.38 ms | 1.00x | 6.2 MB |
| medium | OpenXml | 114.50 ms | 6.59x | 13.2 MB |
| medium | Npoi | 140.00 ms | 8.06x | 32.8 MB |
| medium | OneOffice | 16.00 ms | 0.92x | 12.0 MB |
| pages1000 | RawZip | 100.55 ms | 1.00x | 57.7 MB |
| pages1000 | OpenXml | 1.02 s | 10.14x | 124.5 MB |
| pages1000 | Npoi | 1.32 s | 13.14x | 344.5 MB |
| pages1000 | OneOffice | 341.22 ms | 3.39x | 78.2 MB |
| small | RawZip | 171.38 us | 1.00x | 72.3 KB |
| small | OpenXml | 1.63 ms | 9.50x | 297.1 KB |
| small | Npoi | 3.23 ms | 18.82x | 1.3 MB |
| small | OneOffice | 973.47 us | 5.68x | 1.6 MB |

## DocxExtract

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | OpenXmlDom | 84.15 ms | 1.00x | 7.0 MB |
| medium | OpenXmlReader | 50.54 ms | 0.60x | 8.2 MB |
| medium | Npoi | 80.07 ms | 0.95x | 33.8 MB |
| medium | OneOffice | 4.32 ms | 0.05x | 1.3 MB |
| pages1000 | OpenXmlDom | 740.97 ms | 1.00x | 78.4 MB |
| pages1000 | OpenXmlReader | 775.41 ms | 1.05x | 93.5 MB |
| pages1000 | Npoi | 1.04 s | 1.41x | 377.4 MB |
| pages1000 | OneOffice | 234.56 ms | 0.32x | 420.1 KB |
| small | OpenXmlDom | 1.24 ms | 1.00x | 306.2 KB |
| small | OpenXmlReader | 1.14 ms | 0.92x | 293.5 KB |
| small | Npoi | 1.55 ms | 1.25x | 792.0 KB |
| small | OneOffice | 596.64 us | 0.48x | 250.1 KB |

## DocxLazyOpen

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 5.24 us | 1.00x | 26.1 KB |
| medium | OpenXml | 743.47 us | 141.90x | 228.9 KB |
| medium | Npoi | 80.79 ms | 15419.06x | 26.1 MB |
| medium | OneOffice | 331.78 us | 63.32x | 343.7 KB |
| pages1000 | RawZip | 5.89 us | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 1.00 ms | 169.82x | 212.8 KB |
| pages1000 | Npoi | 944.06 ms | 160192.54x | 287.7 MB |
| pages1000 | OneOffice | 313.71 us | 53.23x | 338.2 KB |
| small | RawZip | 5.90 us | 1.00x | 25.0 KB |
| small | OpenXml | 470.56 us | 79.74x | 212.8 KB |
| small | Npoi | 1.46 ms | 247.85x | 684.0 KB |
| small | OneOffice | 295.23 us | 50.03x | 219.6 KB |

## DocxLoad

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 874.66 us | 1.00x | 26.1 KB |
| medium | OpenXml | 81.54 ms | 93.23x | 7.0 MB |
| medium | Npoi | 65.93 ms | 75.38x | 26.1 MB |
| medium | OneOffice | 3.18 ms | 3.64x | 6.3 MB |
| pages1000 | RawZip | 10.20 ms | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 627.28 ms | 61.50x | 78.4 MB |
| pages1000 | Npoi | 860.14 ms | 84.33x | 287.6 MB |
| pages1000 | OneOffice | 107.80 ms | 10.57x | 50.8 MB |
| small | RawZip | 15.20 us | 1.00x | 25.0 KB |
| small | OpenXml | 1.15 ms | 75.97x | 306.8 KB |
| small | Npoi | 1.44 ms | 94.45x | 672.8 KB |
| small | OneOffice | 411.23 us | 27.05x | 242.4 KB |

## DocxLoadSave

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 16.99 ms | 1.00x | 2.5 MB |
| medium | OpenXml | 18.95 ms | 1.11x | 7.5 MB |
| medium | Npoi | 139.17 ms | 8.19x | 32.8 MB |
| medium | OneOffice | 515.56 us | 0.03x | 2.3 MB |
| pages1000 | RawZip | 96.70 ms | 1.00x | 4.9 MB |
| pages1000 | OpenXml | 100.80 ms | 1.04x | 30.8 MB |
| pages1000 | Npoi | 1.42 s | 14.69x | 344.5 MB |
| pages1000 | OneOffice | 626.29 us | 0.01x | 4.3 MB |
| small | RawZip | 170.99 us | 1.00x | 33.9 KB |
| small | OpenXml | 782.67 us | 4.58x | 332.0 KB |
| small | Npoi | 3.15 ms | 18.42x | 1.3 MB |
| small | OneOffice | 200.19 us | 1.17x | 181.8 KB |

## Bar: OneOffice against the fastest comparator

Ratio is OneOffice median / the fastest comparator's median in the same class and input; at or below 1.00 is faster. The raw zip row is a floor and is not a comparator.

| Class | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| DocxEdit | medium | 16.00 ms | OpenXml | 114.50 ms | 0.14x |
| DocxEdit | pages1000 | 341.22 ms | OpenXml | 1.02 s | 0.33x |
| DocxEdit | small | 973.47 us | OpenXml | 1.63 ms | 0.60x |
| DocxExtract | medium | 4.32 ms | OpenXmlReader | 50.54 ms | 0.09x |
| DocxExtract | pages1000 | 234.56 ms | OpenXmlDom | 740.97 ms | 0.32x |
| DocxExtract | small | 596.64 us | OpenXmlReader | 1.14 ms | 0.53x |
| DocxLazyOpen | medium | 331.78 us | OpenXml | 743.47 us | 0.45x |
| DocxLazyOpen | pages1000 | 313.71 us | OpenXml | 1.00 ms | 0.31x |
| DocxLazyOpen | small | 295.23 us | OpenXml | 470.56 us | 0.63x |
| DocxLoad | medium | 3.18 ms | Npoi | 65.93 ms | 0.05x |
| DocxLoad | pages1000 | 107.80 ms | OpenXml | 627.28 ms | 0.17x |
| DocxLoad | small | 411.23 us | OpenXml | 1.15 ms | 0.36x |
| DocxLoadSave | medium | 515.56 us | OpenXml | 18.95 ms | 0.03x |
| DocxLoadSave | pages1000 | 626.29 us | OpenXml | 100.80 ms | 0.01x |
| DocxLoadSave | small | 200.19 us | OpenXml | 782.67 us | 0.26x |

Geometric mean of the ratios: 0.175x over 15 rows. Worst row: DocxLazyOpen small at 0.63x.

## Peak memory (one run in a fresh process)

Baseline is the working set after the input bytes are loaded; peak is the process high-water mark; allocated is managed bytes allocated by the row.

| Row | Input | Baseline | Peak | Allocated |
|---|---|---:|---:|---:|
| npoi.edit | large | 87.0 MB | 0 B | 834.7 MB |
| npoi.edit | medium | 42.3 MB | 0 B | 36.7 MB |
| npoi.edit | pages1000 | 43.2 MB | 0 B | 348.3 MB |
| npoi.edit | small | 41.7 MB | 0 B | 5.0 MB |
| npoi.extract | large | 87.1 MB | 0 B | 729.9 MB |
| npoi.extract | medium | 42.4 MB | 0 B | 34.1 MB |
| npoi.extract | pages1000 | 43.1 MB | 0 B | 377.7 MB |
| npoi.extract | small | 41.7 MB | 0 B | 1.1 MB |
| npoi.lazyopen | large | 87.1 MB | 0 B | 577.3 MB |
| npoi.lazyopen | medium | 42.4 MB | 0 B | 27.9 MB |
| npoi.lazyopen | pages1000 | 43.0 MB | 0 B | 289.4 MB |
| npoi.lazyopen | small | 41.7 MB | 0 B | 2.4 MB |
| npoi.load | large | 87.0 MB | 0 B | 575.8 MB |
| npoi.load | medium | 42.3 MB | 0 B | 26.4 MB |
| npoi.load | pages1000 | 43.2 MB | 0 B | 288.0 MB |
| npoi.load | small | 41.6 MB | 0 B | 1005.5 KB |
| npoi.loadsave | large | 87.0 MB | 0 B | 834.7 MB |
| npoi.loadsave | medium | 42.4 MB | 0 B | 36.7 MB |
| npoi.loadsave | pages1000 | 43.1 MB | 0 B | 348.3 MB |
| npoi.loadsave | small | 41.7 MB | 0 B | 5.1 MB |
| oneoffice.edit | large | 87.1 MB | 0 B | 302.4 MB |
| oneoffice.edit | medium | 42.3 MB | 0 B | 12.2 MB |
| oneoffice.edit | pages1000 | 43.1 MB | 0 B | 78.4 MB |
| oneoffice.edit | small | 41.6 MB | 0 B | 1.8 MB |
| oneoffice.extract | large | 87.1 MB | 0 B | 625.5 KB |
| oneoffice.extract | medium | 42.4 MB | 0 B | 1.5 MB |
| oneoffice.extract | pages1000 | 43.1 MB | 0 B | 584.6 KB |
| oneoffice.extract | small | 41.6 MB | 0 B | 398.6 KB |
| oneoffice.lazyopen | large | 87.0 MB | 0 B | 461.5 KB |
| oneoffice.lazyopen | medium | 42.4 MB | 0 B | 426.1 KB |
| oneoffice.lazyopen | pages1000 | 43.1 MB | 0 B | 426.8 KB |
| oneoffice.lazyopen | small | 41.6 MB | 0 B | 308.2 KB |
| oneoffice.load | large | 87.1 MB | 0 B | 98.4 MB |
| oneoffice.load | medium | 42.3 MB | 0 B | 6.4 MB |
| oneoffice.load | pages1000 | 43.2 MB | 0 B | 50.9 MB |
| oneoffice.load | small | 41.7 MB | 0 B | 347.4 KB |
| oneoffice.loadsave | large | 87.0 MB | 0 B | 132.1 MB |
| oneoffice.loadsave | medium | 42.4 MB | 0 B | 2.4 MB |
| oneoffice.loadsave | pages1000 | 43.0 MB | 0 B | 4.5 MB |
| oneoffice.loadsave | small | 41.6 MB | 0 B | 328.8 KB |
| openxml.edit | large | 87.1 MB | 0 B | 385.9 MB |
| openxml.edit | medium | 42.3 MB | 0 B | 14.2 MB |
| openxml.edit | pages1000 | 43.1 MB | 0 B | 125.5 MB |
| openxml.edit | small | 41.6 MB | 0 B | 1.3 MB |
| openxml.extract.dom | large | 87.1 MB | 0 B | 134.8 MB |
| openxml.extract.dom | medium | 42.3 MB | 0 B | 7.6 MB |
| openxml.extract.dom | pages1000 | 43.2 MB | 0 B | 79.1 MB |
| openxml.extract.dom | small | 41.7 MB | 0 B | 1014.6 KB |
| openxml.extract.reader | large | 87.2 MB | 0 B | 160.7 MB |
| openxml.extract.reader | medium | 42.4 MB | 0 B | 8.9 MB |
| openxml.extract.reader | pages1000 | 43.1 MB | 0 B | 94.2 MB |
| openxml.extract.reader | small | 41.6 MB | 0 B | 1002.0 KB |
| openxml.lazyopen | large | 87.1 MB | 0 B | 845.2 KB |
| openxml.lazyopen | medium | 42.3 MB | 0 B | 741.5 KB |
| openxml.lazyopen | pages1000 | 43.1 MB | 0 B | 725.5 KB |
| openxml.lazyopen | small | 41.6 MB | 0 B | 737.5 KB |
| openxml.load | large | 87.0 MB | 0 B | 134.8 MB |
| openxml.load | medium | 42.3 MB | 0 B | 7.6 MB |
| openxml.load | pages1000 | 43.0 MB | 0 B | 79.1 MB |
| openxml.load | small | 41.6 MB | 0 B | 1015.4 KB |
| openxml.loadsave | large | 87.0 MB | 0 B | 326.2 MB |
| openxml.loadsave | medium | 42.4 MB | 0 B | 7.9 MB |
| openxml.loadsave | pages1000 | 43.1 MB | 0 B | 31.2 MB |
| openxml.loadsave | small | 41.6 MB | 0 B | 785.9 KB |
| rawzip.edit | large | 87.0 MB | 0 B | 258.5 MB |
| rawzip.edit | medium | 42.3 MB | 0 B | 6.5 MB |
| rawzip.edit | pages1000 | 43.1 MB | 0 B | 58.0 MB |
| rawzip.edit | small | 41.6 MB | 0 B | 417.6 KB |
| rawzip.lazyopen | large | 87.0 MB | 0 B | 119.3 KB |
| rawzip.lazyopen | medium | 42.3 MB | 0 B | 114.3 KB |
| rawzip.lazyopen | pages1000 | 43.1 MB | 0 B | 113.2 KB |
| rawzip.lazyopen | small | 41.7 MB | 0 B | 113.2 KB |
| rawzip.load | large | 87.1 MB | 0 B | 119.3 KB |
| rawzip.load | medium | 42.4 MB | 0 B | 114.3 KB |
| rawzip.load | pages1000 | 43.1 MB | 0 B | 113.2 KB |
| rawzip.load | small | 41.7 MB | 0 B | 113.2 KB |
| rawzip.loadsave | large | 87.0 MB | 0 B | 158.7 MB |
| rawzip.loadsave | medium | 42.4 MB | 0 B | 2.8 MB |
| rawzip.loadsave | pages1000 | 43.1 MB | 0 B | 5.2 MB |
| rawzip.loadsave | small | 41.6 MB | 0 B | 379.1 KB |
