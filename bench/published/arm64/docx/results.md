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
[before] 2026-11-06T09:10:12Z load1/5/15: 2.14 2.06 1.99
[after] 2026-11-06T09:19:12Z load1/5/15: 2.04 2.09 2.04
```

## Load while running (sampled)

```text
[during] 2026-11-06T09:10:13Z load1/5/15: 2.14 2.06 1.99
[during] 2026-11-06T09:10:23Z load1/5/15: 2.26 2.09 2.00
[during] 2026-11-06T09:10:33Z load1/5/15: 2.22 2.08 2.00
[during] 2026-11-06T09:10:43Z load1/5/15: 2.11 2.06 2.00
[during] 2026-11-06T09:10:53Z load1/5/15: 2.10 2.06 2.00
[during] 2026-11-06T09:11:03Z load1/5/15: 2.48 2.14 2.03
[during] 2026-11-06T09:11:13Z load1/5/15: 2.33 2.12 2.02
[during] 2026-11-06T09:11:23Z load1/5/15: 2.21 2.10 2.01
[during] 2026-11-06T09:11:33Z load1/5/15: 2.32 2.13 2.02
[during] 2026-11-06T09:11:43Z load1/5/15: 2.27 2.12 2.02
[during] 2026-11-06T09:11:53Z load1/5/15: 2.23 2.12 2.02
[during] 2026-11-06T09:12:03Z load1/5/15: 2.19 2.11 2.02
[during] 2026-11-06T09:12:13Z load1/5/15: 2.16 2.11 2.02
[during] 2026-11-06T09:12:23Z load1/5/15: 2.15 2.11 2.02
[during] 2026-11-06T09:12:33Z load1/5/15: 2.27 2.13 2.03
[during] 2026-11-06T09:12:43Z load1/5/15: 2.38 2.16 2.04
[during] 2026-11-06T09:12:53Z load1/5/15: 2.32 2.16 2.04
[during] 2026-11-06T09:13:03Z load1/5/15: 2.19 2.13 2.03
[during] 2026-11-06T09:13:13Z load1/5/15: 2.16 2.13 2.03
[during] 2026-11-06T09:13:23Z load1/5/15: 2.13 2.12 2.03
[during] 2026-11-06T09:13:33Z load1/5/15: 2.10 2.12 2.03
[during] 2026-11-06T09:13:43Z load1/5/15: 2.23 2.15 2.04
[during] 2026-11-06T09:13:53Z load1/5/15: 2.20 2.14 2.04
[during] 2026-11-06T09:14:03Z load1/5/15: 2.17 2.14 2.04
[during] 2026-11-06T09:14:13Z load1/5/15: 2.31 2.17 2.05
[during] 2026-11-06T09:14:23Z load1/5/15: 2.26 2.16 2.05
[during] 2026-11-06T09:14:33Z load1/5/15: 2.38 2.19 2.06
[during] 2026-11-06T09:14:43Z load1/5/15: 2.39 2.20 2.06
[during] 2026-11-06T09:14:53Z load1/5/15: 2.26 2.17 2.06
[during] 2026-11-06T09:15:03Z load1/5/15: 2.22 2.17 2.06
[during] 2026-11-06T09:15:13Z load1/5/15: 2.26 2.18 2.06
[during] 2026-11-06T09:15:23Z load1/5/15: 2.24 2.17 2.06
[during] 2026-11-06T09:15:33Z load1/5/15: 2.20 2.17 2.06
[during] 2026-11-06T09:15:43Z load1/5/15: 2.02 2.13 2.05
[during] 2026-11-06T09:15:53Z load1/5/15: 2.01 2.12 2.05
[during] 2026-11-06T09:16:03Z load1/5/15: 2.01 2.12 2.04
[during] 2026-11-06T09:16:13Z load1/5/15: 2.17 2.15 2.06
[during] 2026-11-06T09:16:23Z load1/5/15: 2.14 2.14 2.05
[during] 2026-11-06T09:16:34Z load1/5/15: 2.04 2.12 2.05
[during] 2026-11-06T09:16:44Z load1/5/15: 2.11 2.13 2.05
[during] 2026-11-06T09:16:54Z load1/5/15: 2.34 2.18 2.07
[during] 2026-11-06T09:17:04Z load1/5/15: 2.22 2.16 2.06
[during] 2026-11-06T09:17:14Z load1/5/15: 2.26 2.17 2.07
[during] 2026-11-06T09:17:24Z load1/5/15: 2.14 2.14 2.06
[during] 2026-11-06T09:17:34Z load1/5/15: 1.96 2.11 2.05
[during] 2026-11-06T09:17:44Z load1/5/15: 1.89 2.09 2.04
[during] 2026-11-06T09:17:54Z load1/5/15: 1.98 2.10 2.05
[during] 2026-11-06T09:18:04Z load1/5/15: 1.98 2.09 2.04
[during] 2026-11-06T09:18:14Z load1/5/15: 1.98 2.09 2.04
[during] 2026-11-06T09:18:24Z load1/5/15: 1.98 2.09 2.04
[during] 2026-11-06T09:18:34Z load1/5/15: 2.06 2.10 2.05
[during] 2026-11-06T09:18:44Z load1/5/15: 2.13 2.11 2.05
[during] 2026-11-06T09:18:54Z load1/5/15: 2.04 2.09 2.05
[during] 2026-11-06T09:19:04Z load1/5/15: 1.96 2.07 2.04
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

## DocxEdit

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 17.33 ms | 1.00x | 6.2 MB |
| medium | OpenXml | 70.52 ms | 4.07x | 13.2 MB |
| medium | Npoi | 136.60 ms | 7.88x | 32.8 MB |
| medium | OneDocument | 22.30 ms | 1.29x | 12.0 MB |
| pages1000 | RawZip | 97.97 ms | 1.00x | 57.7 MB |
| pages1000 | OpenXml | 1.00 s | 10.25x | 124.5 MB |
| pages1000 | Npoi | 1.31 s | 13.40x | 344.5 MB |
| pages1000 | OneDocument | 312.43 ms | 3.19x | 78.2 MB |
| small | RawZip | 170.43 us | 1.00x | 72.3 KB |
| small | OpenXml | 1.67 ms | 9.82x | 297.1 KB |
| small | Npoi | 3.15 ms | 18.50x | 1.3 MB |
| small | OneDocument | 947.32 us | 5.56x | 1.6 MB |

## DocxExtract

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | OpenXmlDom | 83.73 ms | 1.00x | 7.0 MB |
| medium | OpenXmlReader | 63.68 ms | 0.76x | 8.2 MB |
| medium | Npoi | 72.82 ms | 0.87x | 33.8 MB |
| medium | OneDocument | 20.17 ms | 0.24x | 1.3 MB |
| pages1000 | OpenXmlDom | 702.75 ms | 1.00x | 78.4 MB |
| pages1000 | OpenXmlReader | 733.14 ms | 1.04x | 93.5 MB |
| pages1000 | Npoi | 1.02 s | 1.46x | 377.4 MB |
| pages1000 | OneDocument | 225.82 ms | 0.32x | 420.3 KB |
| small | OpenXmlDom | 1.18 ms | 1.00x | 306.2 KB |
| small | OpenXmlReader | 1.24 ms | 1.05x | 293.5 KB |
| small | Npoi | 1.54 ms | 1.31x | 792.0 KB |
| small | OneDocument | 581.98 us | 0.49x | 250.3 KB |

## DocxLazyOpen

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 5.34 us | 1.00x | 26.1 KB |
| medium | OpenXml | 734.95 us | 137.54x | 228.9 KB |
| medium | Npoi | 72.26 ms | 13522.07x | 26.1 MB |
| medium | OneDocument | 319.36 us | 59.77x | 344.0 KB |
| pages1000 | RawZip | 6.13 us | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 921.73 us | 150.25x | 212.8 KB |
| pages1000 | Npoi | 797.58 ms | 130010.40x | 287.6 MB |
| pages1000 | OneDocument | 307.25 us | 50.08x | 338.5 KB |
| small | RawZip | 6.15 us | 1.00x | 25.0 KB |
| small | OpenXml | 451.22 us | 73.40x | 212.8 KB |
| small | Npoi | 1.48 ms | 239.94x | 684.0 KB |
| small | OneDocument | 280.68 us | 45.66x | 219.9 KB |

## DocxLoad

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 864.62 us | 1.00x | 26.1 KB |
| medium | OpenXml | 78.63 ms | 90.94x | 7.0 MB |
| medium | Npoi | 60.24 ms | 69.68x | 26.1 MB |
| medium | OneDocument | 5.19 ms | 6.00x | 6.3 MB |
| pages1000 | RawZip | 10.20 ms | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 627.06 ms | 61.46x | 78.4 MB |
| pages1000 | Npoi | 832.93 ms | 81.64x | 287.6 MB |
| pages1000 | OneDocument | 107.04 ms | 10.49x | 50.8 MB |
| small | RawZip | 14.24 us | 1.00x | 25.0 KB |
| small | OpenXml | 1.14 ms | 80.38x | 306.8 KB |
| small | Npoi | 1.36 ms | 95.88x | 672.8 KB |
| small | OneDocument | 378.91 us | 26.62x | 242.8 KB |

## DocxLoadSave

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 16.91 ms | 1.00x | 2.5 MB |
| medium | OpenXml | 18.55 ms | 1.10x | 7.5 MB |
| medium | Npoi | 137.04 ms | 8.11x | 32.8 MB |
| medium | OneDocument | 466.98 us | 0.03x | 2.3 MB |
| pages1000 | RawZip | 94.32 ms | 1.00x | 4.9 MB |
| pages1000 | OpenXml | 98.36 ms | 1.04x | 30.8 MB |
| pages1000 | Npoi | 1.32 s | 13.96x | 344.5 MB |
| pages1000 | OneDocument | 618.46 us | 0.01x | 4.3 MB |
| small | RawZip | 167.28 us | 1.00x | 33.9 KB |
| small | OpenXml | 788.48 us | 4.71x | 332.0 KB |
| small | Npoi | 3.00 ms | 17.95x | 1.3 MB |
| small | OneDocument | 201.70 us | 1.21x | 182.3 KB |

## Bar: OneDocument against the fastest comparator

Ratio is OneDocument median / the fastest comparator's median in the same class and input; at or below 1.00 is faster. The raw zip row is a floor and is not a comparator.

| Class | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| DocxEdit | medium | 22.30 ms | OpenXml | 70.52 ms | 0.32x |
| DocxEdit | pages1000 | 312.43 ms | OpenXml | 1.00 s | 0.31x |
| DocxEdit | small | 947.32 us | OpenXml | 1.67 ms | 0.57x |
| DocxExtract | medium | 20.17 ms | OpenXmlReader | 63.68 ms | 0.32x |
| DocxExtract | pages1000 | 225.82 ms | OpenXmlDom | 702.75 ms | 0.32x |
| DocxExtract | small | 581.98 us | OpenXmlDom | 1.18 ms | 0.49x |
| DocxLazyOpen | medium | 319.36 us | OpenXml | 734.95 us | 0.43x |
| DocxLazyOpen | pages1000 | 307.25 us | OpenXml | 921.73 us | 0.33x |
| DocxLazyOpen | small | 280.68 us | OpenXml | 451.22 us | 0.62x |
| DocxLoad | medium | 5.19 ms | Npoi | 60.24 ms | 0.09x |
| DocxLoad | pages1000 | 107.04 ms | OpenXml | 627.06 ms | 0.17x |
| DocxLoad | small | 378.91 us | OpenXml | 1.14 ms | 0.33x |
| DocxLoadSave | medium | 466.98 us | OpenXml | 18.55 ms | 0.03x |
| DocxLoadSave | pages1000 | 618.46 us | OpenXml | 98.36 ms | 0.01x |
| DocxLoadSave | small | 201.70 us | OpenXml | 788.48 us | 0.26x |

Geometric mean of the ratios: 0.206x over 15 rows. Worst row: DocxLazyOpen small at 0.62x.

## Peak memory (one run in a fresh process)

Baseline is the working set after the input bytes are loaded; peak is the process high-water mark; allocated is managed bytes allocated by the row.

| Row | Input | Baseline | Peak | Allocated |
|---|---|---:|---:|---:|
| npoi.edit | large | 91.1 MB | 729.0 MB | 834.7 MB |
| npoi.edit | medium | 46.3 MB | 104.0 MB | 36.7 MB |
| npoi.edit | pages1000 | 47.2 MB | 347.0 MB | 348.3 MB |
| npoi.edit | small | 45.7 MB | 81.0 MB | 5.0 MB |
| npoi.extract | large | 91.1 MB | 600.0 MB | 730.0 MB |
| npoi.extract | medium | 46.3 MB | 101.0 MB | 34.2 MB |
| npoi.extract | pages1000 | 47.2 MB | 324.0 MB | 377.7 MB |
| npoi.extract | small | 45.8 MB | 71.0 MB | 1.1 MB |
| npoi.lazyopen | large | 91.2 MB | 562.0 MB | 577.3 MB |
| npoi.lazyopen | medium | 46.4 MB | 99.0 MB | 27.9 MB |
| npoi.lazyopen | pages1000 | 47.1 MB | 309.0 MB | 289.4 MB |
| npoi.lazyopen | small | 45.6 MB | 77.0 MB | 2.4 MB |
| npoi.load | large | 91.1 MB | 558.0 MB | 575.8 MB |
| npoi.load | medium | 46.4 MB | 93.0 MB | 26.4 MB |
| npoi.load | pages1000 | 47.2 MB | 306.0 MB | 288.0 MB |
| npoi.load | small | 45.7 MB | 71.0 MB | 1021.5 KB |
| npoi.loadsave | large | 91.1 MB | 729.0 MB | 834.7 MB |
| npoi.loadsave | medium | 46.4 MB | 104.0 MB | 36.7 MB |
| npoi.loadsave | pages1000 | 47.2 MB | 347.0 MB | 348.3 MB |
| npoi.loadsave | small | 45.6 MB | 81.0 MB | 5.1 MB |
| onedocument.edit | large | 91.2 MB | 289.0 MB | 302.4 MB |
| onedocument.edit | medium | 46.4 MB | 67.0 MB | 12.2 MB |
| onedocument.edit | pages1000 | 47.2 MB | 115.0 MB | 78.4 MB |
| onedocument.edit | small | 45.7 MB | 57.0 MB | 1.8 MB |
| onedocument.extract | large | 91.2 MB | 100.0 MB | 625.6 KB |
| onedocument.extract | medium | 46.3 MB | 57.0 MB | 1.5 MB |
| onedocument.extract | pages1000 | 47.2 MB | 56.0 MB | 584.8 KB |
| onedocument.extract | small | 45.6 MB | 54.0 MB | 398.7 KB |
| onedocument.lazyopen | large | 91.1 MB | 102.0 MB | 468.0 KB |
| onedocument.lazyopen | medium | 46.4 MB | 57.0 MB | 432.6 KB |
| onedocument.lazyopen | pages1000 | 47.1 MB | 58.0 MB | 427.1 KB |
| onedocument.lazyopen | small | 45.7 MB | 56.0 MB | 308.5 KB |
| onedocument.load | large | 91.1 MB | 185.0 MB | 98.4 MB |
| onedocument.load | medium | 46.3 MB | 62.0 MB | 6.4 MB |
| onedocument.load | pages1000 | 47.1 MB | 103.0 MB | 50.9 MB |
| onedocument.load | small | 45.7 MB | 56.0 MB | 353.9 KB |
| onedocument.loadsave | large | 91.2 MB | 213.0 MB | 132.1 MB |
| onedocument.loadsave | medium | 46.4 MB | 58.0 MB | 2.4 MB |
| onedocument.loadsave | pages1000 | 47.2 MB | 61.0 MB | 4.5 MB |
| onedocument.loadsave | small | 45.6 MB | 55.0 MB | 335.5 KB |
| openxml.edit | large | 91.2 MB | 445.0 MB | 385.9 MB |
| openxml.edit | medium | 46.3 MB | 88.0 MB | 14.2 MB |
| openxml.edit | pages1000 | 47.1 MB | 192.0 MB | 125.5 MB |
| openxml.edit | small | 45.7 MB | 76.0 MB | 1.3 MB |
| openxml.extract.dom | large | 91.1 MB | 256.0 MB | 134.8 MB |
| openxml.extract.dom | medium | 46.4 MB | 82.0 MB | 7.7 MB |
| openxml.extract.dom | pages1000 | 47.0 MB | 156.0 MB | 79.1 MB |
| openxml.extract.dom | small | 45.7 MB | 75.0 MB | 1.0 MB |
| openxml.extract.reader | large | 91.1 MB | 128.0 MB | 160.7 MB |
| openxml.extract.reader | medium | 46.4 MB | 83.0 MB | 8.9 MB |
| openxml.extract.reader | pages1000 | 47.3 MB | 84.0 MB | 94.2 MB |
| openxml.extract.reader | small | 45.7 MB | 75.0 MB | 1002.1 KB |
| openxml.lazyopen | large | 91.2 MB | 121.0 MB | 845.2 KB |
| openxml.lazyopen | medium | 46.4 MB | 76.0 MB | 753.5 KB |
| openxml.lazyopen | pages1000 | 47.2 MB | 77.0 MB | 725.5 KB |
| openxml.lazyopen | small | 45.7 MB | 75.0 MB | 725.5 KB |
| openxml.load | large | 91.0 MB | 256.0 MB | 134.8 MB |
| openxml.load | medium | 46.4 MB | 82.0 MB | 7.6 MB |
| openxml.load | pages1000 | 47.1 MB | 156.0 MB | 79.1 MB |
| openxml.load | small | 45.8 MB | 75.0 MB | 1015.2 KB |
| openxml.loadsave | large | 91.1 MB | 341.0 MB | 326.2 MB |
| openxml.loadsave | medium | 46.4 MB | 73.0 MB | 7.9 MB |
| openxml.loadsave | pages1000 | 47.2 MB | 94.0 MB | 31.2 MB |
| openxml.loadsave | small | 45.7 MB | 67.0 MB | 773.8 KB |
| rawzip.edit | large | 91.1 MB | 304.0 MB | 258.4 MB |
| rawzip.edit | medium | 46.4 MB | 54.0 MB | 6.5 MB |
| rawzip.edit | pages1000 | 47.1 MB | 98.0 MB | 58.0 MB |
| rawzip.edit | small | 45.7 MB | 48.0 MB | 417.6 KB |
| rawzip.lazyopen | large | 91.1 MB | 93.0 MB | 119.3 KB |
| rawzip.lazyopen | medium | 46.4 MB | 48.0 MB | 114.3 KB |
| rawzip.lazyopen | pages1000 | 47.2 MB | 49.0 MB | 113.2 KB |
| rawzip.lazyopen | small | 45.7 MB | 47.0 MB | 113.2 KB |
| rawzip.load | large | 91.1 MB | 93.0 MB | 119.3 KB |
| rawzip.load | medium | 46.5 MB | 48.0 MB | 114.3 KB |
| rawzip.load | pages1000 | 47.1 MB | 49.0 MB | 113.2 KB |
| rawzip.load | small | 45.7 MB | 47.0 MB | 113.2 KB |
| rawzip.loadsave | large | 91.2 MB | 218.0 MB | 158.7 MB |
| rawzip.loadsave | medium | 46.3 MB | 50.0 MB | 2.8 MB |
| rawzip.loadsave | pages1000 | 47.2 MB | 53.0 MB | 5.2 MB |
| rawzip.loadsave | small | 45.7 MB | 48.0 MB | 379.1 KB |
