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
[before] 2026-11-06T09:28:21Z load1/5/15: 4.62 5.00 5.72
[after] 2026-11-06T09:37:35Z load1/5/15: 1.12 1.75 3.69
```

## Load while running (sampled)

```text
[during] 2026-11-06T09:28:22Z load1/5/15: 4.62 5.00 5.72
[during] 2026-11-06T09:28:32Z load1/5/15: 4.22 4.90 5.68
[during] 2026-11-06T09:28:42Z load1/5/15: 3.88 4.80 5.64
[during] 2026-11-06T09:28:52Z load1/5/15: 4.39 4.87 5.65
[during] 2026-11-06T09:29:02Z load1/5/15: 4.10 4.80 5.62
[during] 2026-11-06T09:29:12Z load1/5/15: 3.93 4.74 5.59
[during] 2026-11-06T09:29:22Z load1/5/15: 3.48 4.61 5.54
[during] 2026-11-06T09:29:32Z load1/5/15: 3.18 4.51 5.50
[during] 2026-11-06T09:29:42Z load1/5/15: 2.84 4.39 5.45
[during] 2026-11-06T09:29:52Z load1/5/15: 2.56 4.28 5.40
[during] 2026-11-06T09:30:02Z load1/5/15: 2.32 4.17 5.35
[during] 2026-11-06T09:30:12Z load1/5/15: 2.12 4.07 5.31
[during] 2026-11-06T09:30:22Z load1/5/15: 1.94 3.97 5.26
[during] 2026-11-06T09:30:32Z load1/5/15: 1.80 3.87 5.21
[during] 2026-11-06T09:30:42Z load1/5/15: 1.75 3.79 5.17
[during] 2026-11-06T09:30:52Z load1/5/15: 1.63 3.70 5.13
[during] 2026-11-06T09:31:02Z load1/5/15: 1.54 3.61 5.08
[during] 2026-11-06T09:31:12Z load1/5/15: 1.45 3.52 5.04
[during] 2026-11-06T09:31:22Z load1/5/15: 1.38 3.44 4.99
[during] 2026-11-06T09:31:32Z load1/5/15: 1.40 3.37 4.96
[during] 2026-11-06T09:31:42Z load1/5/15: 1.34 3.29 4.91
[during] 2026-11-06T09:31:52Z load1/5/15: 1.29 3.22 4.87
[during] 2026-11-06T09:32:02Z load1/5/15: 1.24 3.14 4.83
[during] 2026-11-06T09:32:12Z load1/5/15: 1.21 3.07 4.79
[during] 2026-11-06T09:32:22Z load1/5/15: 1.17 3.01 4.75
[during] 2026-11-06T09:32:32Z load1/5/15: 1.15 2.94 4.71
[during] 2026-11-06T09:32:42Z load1/5/15: 1.12 2.87 4.67
[during] 2026-11-06T09:32:52Z load1/5/15: 1.10 2.81 4.63
[during] 2026-11-06T09:33:02Z load1/5/15: 1.09 2.75 4.59
[during] 2026-11-06T09:33:12Z load1/5/15: 1.07 2.69 4.55
[during] 2026-11-06T09:33:22Z load1/5/15: 1.06 2.64 4.51
[during] 2026-11-06T09:33:32Z load1/5/15: 1.05 2.58 4.47
[during] 2026-11-06T09:33:42Z load1/5/15: 1.04 2.53 4.43
[during] 2026-11-06T09:33:52Z load1/5/15: 1.04 2.48 4.40
[during] 2026-11-06T09:34:02Z load1/5/15: 1.03 2.43 4.36
[during] 2026-11-06T09:34:12Z load1/5/15: 1.02 2.38 4.32
[during] 2026-11-06T09:34:22Z load1/5/15: 1.02 2.34 4.29
[during] 2026-11-06T09:34:32Z load1/5/15: 1.02 2.29 4.25
[during] 2026-11-06T09:34:42Z load1/5/15: 1.01 2.25 4.22
[during] 2026-11-06T09:34:52Z load1/5/15: 1.01 2.21 4.18
[during] 2026-11-06T09:35:02Z load1/5/15: 1.01 2.17 4.15
[during] 2026-11-06T09:35:12Z load1/5/15: 1.01 2.13 4.11
[during] 2026-11-06T09:35:22Z load1/5/15: 1.01 2.09 4.08
[during] 2026-11-06T09:35:32Z load1/5/15: 1.08 2.07 4.05
[during] 2026-11-06T09:35:42Z load1/5/15: 1.15 2.05 4.02
[during] 2026-11-06T09:35:52Z load1/5/15: 1.12 2.02 3.99
[during] 2026-11-06T09:36:02Z load1/5/15: 1.10 1.98 3.96
[during] 2026-11-06T09:36:12Z load1/5/15: 1.09 1.95 3.93
[during] 2026-11-06T09:36:22Z load1/5/15: 1.07 1.92 3.89
[during] 2026-11-06T09:36:32Z load1/5/15: 1.06 1.89 3.86
[during] 2026-11-06T09:36:42Z load1/5/15: 1.05 1.86 3.83
[during] 2026-11-06T09:36:52Z load1/5/15: 1.04 1.83 3.80
[during] 2026-11-06T09:37:02Z load1/5/15: 1.11 1.82 3.78
[during] 2026-11-06T09:37:12Z load1/5/15: 1.17 1.81 3.75
[during] 2026-11-06T09:37:22Z load1/5/15: 1.15 1.78 3.72
[during] 2026-11-06T09:37:32Z load1/5/15: 1.12 1.75 3.69
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
| medium | RawZip | 18.07 ms | 1.00x | 6.2 MB |
| medium | OpenXml | 100.67 ms | 5.57x | 13.2 MB |
| medium | Npoi | 109.96 ms | 6.08x | 32.8 MB |
| medium | OneDocument | 19.26 ms | 1.07x | 12.0 MB |
| pages1000 | RawZip | 119.82 ms | 1.00x | 57.7 MB |
| pages1000 | OpenXml | 812.00 ms | 6.78x | 124.5 MB |
| pages1000 | Npoi | 1.42 s | 11.89x | 344.5 MB |
| pages1000 | OneDocument | 373.67 ms | 3.12x | 78.2 MB |
| small | RawZip | 120.75 us | 1.00x | 72.3 KB |
| small | OpenXml | 1.13 ms | 9.33x | 297.0 KB |
| small | Npoi | 2.75 ms | 22.81x | 1.3 MB |
| small | OneDocument | 766.55 us | 6.35x | 1.6 MB |

## DocxExtract

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | OpenXmlDom | 82.22 ms | 1.00x | 7.0 MB |
| medium | OpenXmlReader | 60.47 ms | 0.74x | 8.2 MB |
| medium | Npoi | 77.86 ms | 0.95x | 33.8 MB |
| medium | OneDocument | 4.39 ms | 0.05x | 1.3 MB |
| pages1000 | OpenXmlDom | 512.24 ms | 1.00x | 78.4 MB |
| pages1000 | OpenXmlReader | 430.17 ms | 0.84x | 93.5 MB |
| pages1000 | Npoi | 1.15 s | 2.25x | 375.3 MB |
| pages1000 | OneDocument | 230.17 ms | 0.45x | 420.3 KB |
| small | OpenXmlDom | 992.68 us | 1.00x | 306.1 KB |
| small | OpenXmlReader | 1.47 ms | 1.48x | 293.4 KB |
| small | Npoi | 1.32 ms | 1.33x | 791.8 KB |
| small | OneDocument | 566.58 us | 0.57x | 250.3 KB |

## DocxLazyOpen

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 6.60 us | 1.00x | 26.1 KB |
| medium | OpenXml | 774.36 us | 117.27x | 228.8 KB |
| medium | Npoi | 51.32 ms | 7772.31x | 26.1 MB |
| medium | OneDocument | 271.82 us | 41.17x | 344.0 KB |
| pages1000 | RawZip | 6.33 us | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 1.05 ms | 165.80x | 212.7 KB |
| pages1000 | Npoi | 1.10 s | 174546.75x | 287.6 MB |
| pages1000 | OneDocument | 270.78 us | 42.80x | 338.5 KB |
| small | RawZip | 6.24 us | 1.00x | 25.0 KB |
| small | OpenXml | 485.01 us | 77.70x | 212.7 KB |
| small | Npoi | 1.19 ms | 190.75x | 683.8 KB |
| small | OneDocument | 254.50 us | 40.77x | 219.9 KB |

## DocxLoad

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 516.85 us | 1.00x | 26.1 KB |
| medium | OpenXml | 73.61 ms | 142.42x | 7.0 MB |
| medium | Npoi | 66.88 ms | 129.39x | 26.1 MB |
| medium | OneDocument | 9.03 ms | 17.47x | 6.3 MB |
| pages1000 | RawZip | 6.09 ms | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 621.84 ms | 102.10x | 78.4 MB |
| pages1000 | Npoi | 1.10 s | 180.30x | 287.6 MB |
| pages1000 | OneDocument | 105.67 ms | 17.35x | 50.8 MB |
| small | RawZip | 11.28 us | 1.00x | 25.0 KB |
| small | OpenXml | 962.15 us | 85.28x | 306.7 KB |
| small | Npoi | 1.18 ms | 104.77x | 672.6 KB |
| small | OneDocument | 385.48 us | 34.17x | 242.6 KB |

## DocxLoadSave

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 16.39 ms | 1.00x | 2.5 MB |
| medium | OpenXml | 18.16 ms | 1.11x | 7.5 MB |
| medium | Npoi | 128.76 ms | 7.86x | 32.8 MB |
| medium | OneDocument | 418.99 us | 0.03x | 2.3 MB |
| pages1000 | RawZip | 107.57 ms | 1.00x | 4.9 MB |
| pages1000 | OpenXml | 124.27 ms | 1.16x | 30.8 MB |
| pages1000 | Npoi | 1.38 s | 12.81x | 344.5 MB |
| pages1000 | OneDocument | 560.61 us | 0.01x | 4.3 MB |
| small | RawZip | 118.10 us | 1.00x | 33.9 KB |
| small | OpenXml | 779.93 us | 6.60x | 331.9 KB |
| small | Npoi | 2.84 ms | 24.05x | 1.3 MB |
| small | OneDocument | 180.61 us | 1.53x | 182.3 KB |

## Bar: OneDocument against the fastest comparator

Ratio is OneDocument median / the fastest comparator's median in the same class and input; at or below 1.00 is faster. The raw zip row is a floor and is not a comparator.

| Class | Input | OneDocument | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| DocxEdit | medium | 19.26 ms | OpenXml | 100.67 ms | 0.19x |
| DocxEdit | pages1000 | 373.67 ms | OpenXml | 812.00 ms | 0.46x |
| DocxEdit | small | 766.55 us | OpenXml | 1.13 ms | 0.68x |
| DocxExtract | medium | 4.39 ms | OpenXmlReader | 60.47 ms | 0.07x |
| DocxExtract | pages1000 | 230.17 ms | OpenXmlReader | 430.17 ms | 0.54x |
| DocxExtract | small | 566.58 us | OpenXmlDom | 992.68 us | 0.57x |
| DocxLazyOpen | medium | 271.82 us | OpenXml | 774.36 us | 0.35x |
| DocxLazyOpen | pages1000 | 270.78 us | OpenXml | 1.05 ms | 0.26x |
| DocxLazyOpen | small | 254.50 us | OpenXml | 485.01 us | 0.52x |
| DocxLoad | medium | 9.03 ms | Npoi | 66.88 ms | 0.14x |
| DocxLoad | pages1000 | 105.67 ms | OpenXml | 621.84 ms | 0.17x |
| DocxLoad | small | 385.48 us | OpenXml | 962.15 us | 0.40x |
| DocxLoadSave | medium | 418.99 us | OpenXml | 18.16 ms | 0.02x |
| DocxLoadSave | pages1000 | 560.61 us | OpenXml | 124.27 ms | 0.00x |
| DocxLoadSave | small | 180.61 us | OpenXml | 779.93 us | 0.23x |

Geometric mean of the ratios: 0.189x over 15 rows. Worst row: DocxEdit small at 0.68x.

## Peak memory (one run in a fresh process)

Baseline is the working set after the input bytes are loaded; peak is the process high-water mark; allocated is managed bytes allocated by the row.

| Row | Input | Baseline | Peak | Allocated |
|---|---|---:|---:|---:|
| npoi.edit | large | 80.4 MB | 694.1 MB | 834.5 MB |
| npoi.edit | medium | 35.5 MB | 101.7 MB | 36.6 MB |
| npoi.edit | pages1000 | 36.5 MB | 334.6 MB | 348.2 MB |
| npoi.edit | small | 35.0 MB | 68.5 MB | 5.0 MB |
| npoi.extract | large | 80.5 MB | 607.5 MB | 729.9 MB |
| npoi.extract | medium | 35.8 MB | 91.1 MB | 34.1 MB |
| npoi.extract | pages1000 | 36.3 MB | 320.1 MB | 377.7 MB |
| npoi.extract | small | 35.0 MB | 57.1 MB | 1.1 MB |
| npoi.lazyopen | large | 80.2 MB | 579.7 MB | 577.2 MB |
| npoi.lazyopen | medium | 35.7 MB | 90.6 MB | 27.8 MB |
| npoi.lazyopen | pages1000 | 36.5 MB | 287.8 MB | 289.4 MB |
| npoi.lazyopen | small | 34.9 MB | 64.3 MB | 2.4 MB |
| npoi.load | large | 80.3 MB | 574.5 MB | 575.8 MB |
| npoi.load | medium | 35.3 MB | 82.8 MB | 26.4 MB |
| npoi.load | pages1000 | 36.6 MB | 297.4 MB | 287.9 MB |
| npoi.load | small | 35.1 MB | 56.9 MB | 990.6 KB |
| npoi.loadsave | large | 80.4 MB | 712.4 MB | 834.5 MB |
| npoi.loadsave | medium | 35.5 MB | 101.8 MB | 36.6 MB |
| npoi.loadsave | pages1000 | 36.4 MB | 333.4 MB | 348.2 MB |
| npoi.loadsave | small | 35.1 MB | 68.5 MB | 5.0 MB |
| onedocument.edit | large | 80.3 MB | 294.8 MB | 302.4 MB |
| onedocument.edit | medium | 35.6 MB | 57.8 MB | 12.2 MB |
| onedocument.edit | pages1000 | 36.5 MB | 103.0 MB | 78.4 MB |
| onedocument.edit | small | 34.9 MB | 48.2 MB | 1.8 MB |
| onedocument.extract | large | 80.6 MB | 91.0 MB | 616.4 KB |
| onedocument.extract | medium | 35.6 MB | 47.0 MB | 1.5 MB |
| onedocument.extract | pages1000 | 36.5 MB | 46.9 MB | 575.5 KB |
| onedocument.extract | small | 34.8 MB | 44.4 MB | 395.5 KB |
| onedocument.lazyopen | large | 80.5 MB | 92.1 MB | 464.8 KB |
| onedocument.lazyopen | medium | 35.7 MB | 47.2 MB | 429.4 KB |
| onedocument.lazyopen | pages1000 | 36.5 MB | 47.8 MB | 424.0 KB |
| onedocument.lazyopen | small | 34.9 MB | 46.1 MB | 305.4 KB |
| onedocument.load | large | 80.4 MB | 175.5 MB | 98.4 MB |
| onedocument.load | medium | 35.6 MB | 52.2 MB | 6.4 MB |
| onedocument.load | pages1000 | 36.5 MB | 93.6 MB | 50.9 MB |
| onedocument.load | small | 34.8 MB | 46.2 MB | 350.7 KB |
| onedocument.loadsave | large | 80.4 MB | 202.7 MB | 132.1 MB |
| onedocument.loadsave | medium | 35.6 MB | 48.5 MB | 2.4 MB |
| onedocument.loadsave | pages1000 | 36.3 MB | 51.0 MB | 4.5 MB |
| onedocument.loadsave | small | 35.0 MB | 46.0 MB | 332.3 KB |
| openxml.edit | large | 80.4 MB | 418.3 MB | 385.8 MB |
| openxml.edit | medium | 35.6 MB | 72.1 MB | 14.1 MB |
| openxml.edit | pages1000 | 36.4 MB | 166.8 MB | 125.5 MB |
| openxml.edit | small | 35.0 MB | 59.4 MB | 1.2 MB |
| openxml.extract.dom | large | 80.3 MB | 239.8 MB | 134.8 MB |
| openxml.extract.dom | medium | 35.7 MB | 65.9 MB | 7.6 MB |
| openxml.extract.dom | pages1000 | 36.4 MB | 131.5 MB | 79.1 MB |
| openxml.extract.dom | small | 35.0 MB | 58.5 MB | 978.6 KB |
| openxml.extract.reader | large | 80.4 MB | 152.6 MB | 160.7 MB |
| openxml.extract.reader | medium | 35.5 MB | 67.6 MB | 8.9 MB |
| openxml.extract.reader | pages1000 | 36.4 MB | 107.9 MB | 94.2 MB |
| openxml.extract.reader | small | 34.9 MB | 58.4 MB | 965.9 KB |
| openxml.lazyopen | large | 80.3 MB | 103.5 MB | 809.1 KB |
| openxml.lazyopen | medium | 35.6 MB | 58.5 MB | 705.4 KB |
| openxml.lazyopen | pages1000 | 36.4 MB | 59.6 MB | 689.4 KB |
| openxml.lazyopen | small | 34.9 MB | 58.0 MB | 689.4 KB |
| openxml.load | large | 80.2 MB | 231.5 MB | 134.8 MB |
| openxml.load | medium | 35.7 MB | 66.1 MB | 7.6 MB |
| openxml.load | pages1000 | 36.4 MB | 139.4 MB | 79.1 MB |
| openxml.load | small | 34.9 MB | 58.6 MB | 979.1 KB |
| openxml.loadsave | large | 80.4 MB | 289.7 MB | 326.2 MB |
| openxml.loadsave | medium | 35.6 MB | 58.3 MB | 7.9 MB |
| openxml.loadsave | pages1000 | 36.3 MB | 79.6 MB | 31.2 MB |
| openxml.loadsave | small | 34.8 MB | 52.9 MB | 737.8 KB |
| rawzip.edit | large | 80.5 MB | 249.1 MB | 258.4 MB |
| rawzip.edit | medium | 35.8 MB | 45.8 MB | 6.5 MB |
| rawzip.edit | pages1000 | 36.3 MB | 89.9 MB | 58.0 MB |
| rawzip.edit | small | 35.1 MB | 39.6 MB | 382.9 KB |
| rawzip.lazyopen | large | 80.4 MB | 84.4 MB | 84.5 KB |
| rawzip.lazyopen | medium | 35.7 MB | 39.7 MB | 79.6 KB |
| rawzip.lazyopen | pages1000 | 36.6 MB | 40.6 MB | 78.4 KB |
| rawzip.lazyopen | small | 35.1 MB | 39.1 MB | 78.4 KB |
| rawzip.load | large | 80.3 MB | 84.3 MB | 84.5 KB |
| rawzip.load | medium | 35.7 MB | 39.7 MB | 79.6 KB |
| rawzip.load | pages1000 | 36.4 MB | 40.4 MB | 78.4 KB |
| rawzip.load | small | 34.9 MB | 38.9 MB | 78.4 KB |
| rawzip.loadsave | large | 80.4 MB | 209.8 MB | 158.6 MB |
| rawzip.loadsave | medium | 35.6 MB | 42.4 MB | 2.8 MB |
| rawzip.loadsave | pages1000 | 36.3 MB | 45.0 MB | 5.2 MB |
| rawzip.loadsave | small | 35.0 MB | 39.6 MB | 344.4 KB |
