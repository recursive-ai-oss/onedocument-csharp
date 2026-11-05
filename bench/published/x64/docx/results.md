# Benchmark results

## Machine and load

```text
run: x64-docx
filter: *Bench.Docx*
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-05T04:12:38Z load1/5/15: 1.33 2.56 5.68
[after] 2026-11-05T04:21:26Z load1/5/15: 1.04 1.41 3.73
```

## Load while running (sampled)

```text
[during] 2026-11-05T04:12:38Z load1/5/15: 1.33 2.56 5.68
[during] 2026-11-05T04:12:48Z load1/5/15: 1.28 2.51 5.63
[during] 2026-11-05T04:12:58Z load1/5/15: 1.53 2.52 5.60
[during] 2026-11-05T04:13:08Z load1/5/15: 1.45 2.47 5.56
[during] 2026-11-05T04:13:18Z load1/5/15: 1.53 2.46 5.52
[during] 2026-11-05T04:13:28Z load1/5/15: 1.52 2.42 5.47
[during] 2026-11-05T04:13:38Z load1/5/15: 1.59 2.41 5.43
[during] 2026-11-05T04:13:48Z load1/5/15: 1.50 2.36 5.39
[during] 2026-11-05T04:13:58Z load1/5/15: 1.42 2.32 5.34
[during] 2026-11-05T04:14:08Z load1/5/15: 1.43 2.29 5.30
[during] 2026-11-05T04:14:18Z load1/5/15: 1.44 2.26 5.26
[during] 2026-11-05T04:14:28Z load1/5/15: 1.37 2.22 5.21
[during] 2026-11-05T04:14:38Z load1/5/15: 1.40 2.20 5.17
[during] 2026-11-05T04:14:48Z load1/5/15: 1.34 2.16 5.13
[during] 2026-11-05T04:14:58Z load1/5/15: 1.28 2.12 5.08
[during] 2026-11-05T04:15:08Z load1/5/15: 1.32 2.10 5.04
[during] 2026-11-05T04:15:18Z load1/5/15: 1.27 2.06 5.00
[during] 2026-11-05T04:15:28Z load1/5/15: 1.23 2.03 4.95
[during] 2026-11-05T04:15:38Z load1/5/15: 1.19 1.99 4.91
[during] 2026-11-05T04:15:48Z load1/5/15: 1.16 1.96 4.87
[during] 2026-11-05T04:15:58Z load1/5/15: 1.22 1.94 4.83
[during] 2026-11-05T04:16:08Z load1/5/15: 1.18 1.91 4.79
[during] 2026-11-05T04:16:18Z load1/5/15: 1.23 1.90 4.76
[during] 2026-11-05T04:16:28Z load1/5/15: 1.27 1.88 4.72
[during] 2026-11-05T04:16:38Z load1/5/15: 1.23 1.85 4.68
[during] 2026-11-05T04:16:48Z load1/5/15: 1.42 1.88 4.66
[during] 2026-11-05T04:16:58Z load1/5/15: 1.35 1.85 4.62
[during] 2026-11-05T04:17:08Z load1/5/15: 1.30 1.82 4.58
[during] 2026-11-05T04:17:18Z load1/5/15: 1.40 1.82 4.55
[during] 2026-11-05T04:17:28Z load1/5/15: 1.34 1.80 4.51
[during] 2026-11-05T04:17:38Z load1/5/15: 1.29 1.77 4.47
[during] 2026-11-05T04:17:48Z load1/5/15: 1.24 1.74 4.43
[during] 2026-11-05T04:17:58Z load1/5/15: 1.28 1.74 4.40
[during] 2026-11-05T04:18:08Z load1/5/15: 1.24 1.71 4.37
[during] 2026-11-05T04:18:18Z load1/5/15: 1.28 1.70 4.33
[during] 2026-11-05T04:18:28Z load1/5/15: 1.39 1.71 4.31
[during] 2026-11-05T04:18:38Z load1/5/15: 1.33 1.69 4.27
[during] 2026-11-05T04:18:48Z load1/5/15: 1.28 1.67 4.24
[during] 2026-11-05T04:18:58Z load1/5/15: 1.24 1.64 4.20
[during] 2026-11-05T04:19:08Z load1/5/15: 1.20 1.62 4.17
[during] 2026-11-05T04:19:18Z load1/5/15: 1.17 1.60 4.13
[during] 2026-11-05T04:19:28Z load1/5/15: 1.22 1.60 4.10
[during] 2026-11-05T04:19:38Z load1/5/15: 1.18 1.58 4.07
[during] 2026-11-05T04:19:48Z load1/5/15: 1.23 1.58 4.04
[during] 2026-11-05T04:19:58Z load1/5/15: 1.19 1.56 4.01
[during] 2026-11-05T04:20:08Z load1/5/15: 1.16 1.54 3.98
[during] 2026-11-05T04:20:18Z load1/5/15: 1.13 1.52 3.94
[during] 2026-11-05T04:20:28Z load1/5/15: 1.11 1.50 3.91
[during] 2026-11-05T04:20:38Z load1/5/15: 1.09 1.48 3.88
[during] 2026-11-05T04:20:48Z load1/5/15: 1.08 1.47 3.85
[during] 2026-11-05T04:20:58Z load1/5/15: 1.07 1.45 3.82
[during] 2026-11-05T04:21:08Z load1/5/15: 1.06 1.44 3.79
[during] 2026-11-05T04:21:18Z load1/5/15: 1.05 1.42 3.76
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
| medium | RawZip | 17.92 ms | 1.00x | 6.2 MB |
| medium | OpenXml | 99.87 ms | 5.57x | 13.2 MB |
| medium | Npoi | 113.73 ms | 6.35x | 32.8 MB |
| medium | OneOffice | 19.79 ms | 1.10x | 12.0 MB |
| pages1000 | RawZip | 121.01 ms | 1.00x | 57.7 MB |
| pages1000 | OpenXml | 792.04 ms | 6.55x | 124.5 MB |
| pages1000 | Npoi | 1.45 s | 12.01x | 344.5 MB |
| pages1000 | OneOffice | 364.00 ms | 3.01x | 78.2 MB |
| small | RawZip | 120.54 us | 1.00x | 72.3 KB |
| small | OpenXml | 1.13 ms | 9.37x | 297.0 KB |
| small | Npoi | 2.79 ms | 23.13x | 1.3 MB |
| small | OneOffice | 729.06 us | 6.05x | 1.6 MB |

## DocxExtract

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | OpenXmlDom | 75.56 ms | 1.00x | 7.0 MB |
| medium | OpenXmlReader | 59.40 ms | 0.79x | 8.2 MB |
| medium | Npoi | 76.37 ms | 1.01x | 33.8 MB |
| medium | OneOffice | 9.09 ms | 0.12x | 1.3 MB |
| pages1000 | OpenXmlDom | 663.62 ms | 1.00x | 78.4 MB |
| pages1000 | OpenXmlReader | 434.43 ms | 0.65x | 93.5 MB |
| pages1000 | Npoi | 1.16 s | 1.75x | 375.3 MB |
| pages1000 | OneOffice | 209.81 ms | 0.32x | 420.1 KB |
| small | OpenXmlDom | 1.02 ms | 1.00x | 306.1 KB |
| small | OpenXmlReader | 1.50 ms | 1.47x | 293.4 KB |
| small | Npoi | 1.53 ms | 1.51x | 791.8 KB |
| small | OneOffice | 565.99 us | 0.56x | 250.1 KB |

## DocxLazyOpen

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 7.05 us | 1.00x | 26.1 KB |
| medium | OpenXml | 772.57 us | 109.59x | 228.8 KB |
| medium | Npoi | 51.92 ms | 7365.82x | 26.1 MB |
| medium | OneOffice | 285.30 us | 40.47x | 343.6 KB |
| pages1000 | RawZip | 6.72 us | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 1.02 ms | 151.54x | 212.7 KB |
| pages1000 | Npoi | 1.09 s | 162211.86x | 287.6 MB |
| pages1000 | OneOffice | 261.57 us | 38.93x | 338.2 KB |
| small | RawZip | 7.18 us | 1.00x | 25.0 KB |
| small | OpenXml | 467.20 us | 65.05x | 212.7 KB |
| small | Npoi | 1.23 ms | 171.22x | 683.8 KB |
| small | OneOffice | 255.50 us | 35.58x | 219.6 KB |

## DocxLoad

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 518.37 us | 1.00x | 26.1 KB |
| medium | OpenXml | 67.89 ms | 130.96x | 7.0 MB |
| medium | Npoi | 66.54 ms | 128.36x | 26.1 MB |
| medium | OneOffice | 9.34 ms | 18.02x | 6.3 MB |
| pages1000 | RawZip | 6.12 ms | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 650.02 ms | 106.23x | 78.4 MB |
| pages1000 | Npoi | 1.12 s | 182.93x | 287.6 MB |
| pages1000 | OneOffice | 107.02 ms | 17.49x | 50.8 MB |
| small | RawZip | 11.18 us | 1.00x | 25.0 KB |
| small | OpenXml | 1.55 ms | 138.42x | 306.7 KB |
| small | Npoi | 1.18 ms | 105.63x | 672.6 KB |
| small | OneOffice | 389.05 us | 34.80x | 242.3 KB |

## DocxLoadSave

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 16.54 ms | 1.00x | 2.5 MB |
| medium | OpenXml | 18.26 ms | 1.10x | 7.5 MB |
| medium | Npoi | 106.63 ms | 6.45x | 32.8 MB |
| medium | OneOffice | 436.65 us | 0.03x | 2.3 MB |
| pages1000 | RawZip | 109.67 ms | 1.00x | 4.9 MB |
| pages1000 | OpenXml | 124.81 ms | 1.14x | 30.8 MB |
| pages1000 | Npoi | 1.47 s | 13.39x | 344.5 MB |
| pages1000 | OneOffice | 540.73 us | 0.00x | 4.3 MB |
| small | RawZip | 118.43 us | 1.00x | 33.9 KB |
| small | OpenXml | 817.15 us | 6.90x | 331.9 KB |
| small | Npoi | 2.87 ms | 24.22x | 1.3 MB |
| small | OneOffice | 170.43 us | 1.44x | 181.8 KB |

## Bar: OneOffice against the fastest comparator

Ratio is OneOffice median / the fastest comparator's median in the same class and input; at or below 1.00 is faster. The raw zip row is a floor and is not a comparator.

| Class | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| DocxEdit | medium | 19.79 ms | OpenXml | 99.87 ms | 0.20x |
| DocxEdit | pages1000 | 364.00 ms | OpenXml | 792.04 ms | 0.46x |
| DocxEdit | small | 729.06 us | OpenXml | 1.13 ms | 0.65x |
| DocxExtract | medium | 9.09 ms | OpenXmlReader | 59.40 ms | 0.15x |
| DocxExtract | pages1000 | 209.81 ms | OpenXmlReader | 434.43 ms | 0.48x |
| DocxExtract | small | 565.99 us | OpenXmlDom | 1.02 ms | 0.56x |
| DocxLazyOpen | medium | 285.30 us | OpenXml | 772.57 us | 0.37x |
| DocxLazyOpen | pages1000 | 261.57 us | OpenXml | 1.02 ms | 0.26x |
| DocxLazyOpen | small | 255.50 us | OpenXml | 467.20 us | 0.55x |
| DocxLoad | medium | 9.34 ms | Npoi | 66.54 ms | 0.14x |
| DocxLoad | pages1000 | 107.02 ms | OpenXml | 650.02 ms | 0.16x |
| DocxLoad | small | 389.05 us | Npoi | 1.18 ms | 0.33x |
| DocxLoadSave | medium | 436.65 us | OpenXml | 18.26 ms | 0.02x |
| DocxLoadSave | pages1000 | 540.73 us | OpenXml | 124.81 ms | 0.00x |
| DocxLoadSave | small | 170.43 us | OpenXml | 817.15 us | 0.21x |

Geometric mean of the ratios: 0.194x over 15 rows. Worst row: DocxEdit small at 0.65x.

## Peak memory (one run in a fresh process)

Baseline is the working set after the input bytes are loaded; peak is the process high-water mark; allocated is managed bytes allocated by the row.

| Row | Input | Baseline | Peak | Allocated |
|---|---|---:|---:|---:|
| npoi.edit | large | 74.5 MB | 706.8 MB | 834.5 MB |
| npoi.edit | medium | 29.8 MB | 96.6 MB | 36.6 MB |
| npoi.edit | pages1000 | 30.6 MB | 329.1 MB | 348.2 MB |
| npoi.edit | small | 29.2 MB | 62.8 MB | 5.0 MB |
| npoi.extract | large | 74.5 MB | 594.8 MB | 729.9 MB |
| npoi.extract | medium | 29.8 MB | 85.5 MB | 34.1 MB |
| npoi.extract | pages1000 | 30.6 MB | 337.0 MB | 377.7 MB |
| npoi.extract | small | 29.2 MB | 51.5 MB | 1.1 MB |
| npoi.lazyopen | large | 74.6 MB | 574.6 MB | 577.2 MB |
| npoi.lazyopen | medium | 30.0 MB | 85.3 MB | 27.8 MB |
| npoi.lazyopen | pages1000 | 30.6 MB | 281.0 MB | 289.4 MB |
| npoi.lazyopen | small | 29.1 MB | 58.9 MB | 2.4 MB |
| npoi.load | large | 74.6 MB | 569.1 MB | 575.8 MB |
| npoi.load | medium | 29.9 MB | 77.7 MB | 26.4 MB |
| npoi.load | pages1000 | 30.7 MB | 291.7 MB | 287.9 MB |
| npoi.load | small | 29.3 MB | 51.5 MB | 974.6 KB |
| npoi.loadsave | large | 74.5 MB | 688.6 MB | 834.5 MB |
| npoi.loadsave | medium | 29.9 MB | 96.8 MB | 36.6 MB |
| npoi.loadsave | pages1000 | 30.6 MB | 329.0 MB | 348.2 MB |
| npoi.loadsave | small | 29.2 MB | 62.9 MB | 5.0 MB |
| oneoffice.edit | large | 74.5 MB | 288.2 MB | 302.4 MB |
| oneoffice.edit | medium | 29.8 MB | 51.3 MB | 12.2 MB |
| oneoffice.edit | pages1000 | 30.6 MB | 97.5 MB | 78.4 MB |
| oneoffice.edit | small | 29.2 MB | 41.5 MB | 1.8 MB |
| oneoffice.extract | large | 74.6 MB | 85.8 MB | 622.3 KB |
| oneoffice.extract | medium | 29.9 MB | 41.9 MB | 1.5 MB |
| oneoffice.extract | pages1000 | 30.7 MB | 41.7 MB | 575.4 KB |
| oneoffice.extract | small | 29.3 MB | 39.6 MB | 395.4 KB |
| oneoffice.lazyopen | large | 74.6 MB | 85.6 MB | 464.4 KB |
| oneoffice.lazyopen | medium | 29.9 MB | 40.8 MB | 423.0 KB |
| oneoffice.lazyopen | pages1000 | 30.6 MB | 41.6 MB | 423.6 KB |
| oneoffice.lazyopen | small | 29.2 MB | 40.0 MB | 305.0 KB |
| oneoffice.load | large | 74.6 MB | 169.1 MB | 98.4 MB |
| oneoffice.load | medium | 29.9 MB | 46.2 MB | 6.4 MB |
| oneoffice.load | pages1000 | 30.7 MB | 87.4 MB | 50.9 MB |
| oneoffice.load | small | 29.1 MB | 39.9 MB | 344.2 KB |
| oneoffice.loadsave | large | 74.6 MB | 195.3 MB | 132.1 MB |
| oneoffice.loadsave | medium | 29.8 MB | 41.3 MB | 2.4 MB |
| oneoffice.loadsave | pages1000 | 30.5 MB | 43.6 MB | 4.5 MB |
| oneoffice.loadsave | small | 29.0 MB | 38.5 MB | 331.7 KB |
| openxml.edit | large | 74.5 MB | 412.8 MB | 385.8 MB |
| openxml.edit | medium | 29.9 MB | 66.9 MB | 14.1 MB |
| openxml.edit | pages1000 | 30.7 MB | 161.8 MB | 125.4 MB |
| openxml.edit | small | 29.2 MB | 53.7 MB | 1.2 MB |
| openxml.extract.dom | large | 74.7 MB | 234.3 MB | 134.8 MB |
| openxml.extract.dom | medium | 30.0 MB | 60.6 MB | 7.6 MB |
| openxml.extract.dom | pages1000 | 30.6 MB | 134.1 MB | 79.1 MB |
| openxml.extract.dom | small | 29.2 MB | 53.1 MB | 978.6 KB |
| openxml.extract.reader | large | 74.6 MB | 146.9 MB | 160.7 MB |
| openxml.extract.reader | medium | 30.0 MB | 62.4 MB | 8.9 MB |
| openxml.extract.reader | pages1000 | 30.7 MB | 102.5 MB | 94.2 MB |
| openxml.extract.reader | small | 29.1 MB | 52.8 MB | 965.9 KB |
| openxml.lazyopen | large | 74.6 MB | 98.4 MB | 809.2 KB |
| openxml.lazyopen | medium | 29.9 MB | 53.5 MB | 705.5 KB |
| openxml.lazyopen | pages1000 | 30.6 MB | 54.0 MB | 689.4 KB |
| openxml.lazyopen | small | 29.2 MB | 52.9 MB | 689.4 KB |
| openxml.load | large | 74.5 MB | 234.3 MB | 134.8 MB |
| openxml.load | medium | 29.8 MB | 60.6 MB | 7.6 MB |
| openxml.load | pages1000 | 30.6 MB | 125.6 MB | 79.1 MB |
| openxml.load | small | 29.1 MB | 52.9 MB | 979.1 KB |
| openxml.loadsave | large | 74.6 MB | 282.7 MB | 326.2 MB |
| openxml.loadsave | medium | 29.9 MB | 53.1 MB | 7.9 MB |
| openxml.loadsave | pages1000 | 30.6 MB | 74.1 MB | 31.2 MB |
| openxml.loadsave | small | 29.1 MB | 47.6 MB | 749.8 KB |
| rawzip.edit | large | 74.5 MB | 243.2 MB | 258.5 MB |
| rawzip.edit | medium | 29.9 MB | 40.1 MB | 6.5 MB |
| rawzip.edit | pages1000 | 30.6 MB | 84.5 MB | 58.0 MB |
| rawzip.edit | small | 29.2 MB | 34.0 MB | 382.9 KB |
| rawzip.lazyopen | large | 74.6 MB | 78.7 MB | 84.5 KB |
| rawzip.lazyopen | medium | 29.8 MB | 34.0 MB | 79.6 KB |
| rawzip.lazyopen | pages1000 | 30.6 MB | 34.8 MB | 78.4 KB |
| rawzip.lazyopen | small | 29.2 MB | 33.4 MB | 78.4 KB |
| rawzip.load | large | 74.5 MB | 78.7 MB | 84.5 KB |
| rawzip.load | medium | 30.0 MB | 34.1 MB | 79.6 KB |
| rawzip.load | pages1000 | 30.5 MB | 34.7 MB | 78.4 KB |
| rawzip.load | small | 29.0 MB | 33.2 MB | 78.4 KB |
| rawzip.loadsave | large | 74.6 MB | 204.4 MB | 158.7 MB |
| rawzip.loadsave | medium | 29.8 MB | 36.9 MB | 2.8 MB |
| rawzip.loadsave | pages1000 | 30.6 MB | 39.7 MB | 5.2 MB |
| rawzip.loadsave | small | 29.1 MB | 34.0 MB | 344.4 KB |
