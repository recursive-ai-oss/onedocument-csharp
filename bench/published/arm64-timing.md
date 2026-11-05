# Benchmark results

## Machine and load

```text
run: final-arm64-timing
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
[before] 2026-11-05T00:31:44Z load1/5/15: 1.29 1.58 1.65
[after] 2026-11-05T00:39:36Z load1/5/15: 2.11 1.92 1.80
```

## Load while running (sampled)

```text
[during] 2026-11-05T00:31:46Z load1/5/15: 1.35 1.58 1.65
[during] 2026-11-05T00:31:56Z load1/5/15: 1.53 1.61 1.66
[during] 2026-11-05T00:32:06Z load1/5/15: 1.76 1.66 1.68
[during] 2026-11-05T00:32:16Z load1/5/15: 1.88 1.69 1.69
[during] 2026-11-05T00:32:26Z load1/5/15: 1.90 1.70 1.69
[during] 2026-11-05T00:32:36Z load1/5/15: 1.99 1.72 1.70
[during] 2026-11-05T00:32:46Z load1/5/15: 2.07 1.74 1.70
[during] 2026-11-05T00:32:56Z load1/5/15: 2.07 1.75 1.71
[during] 2026-11-05T00:33:06Z load1/5/15: 2.13 1.78 1.72
[during] 2026-11-05T00:33:16Z load1/5/15: 2.19 1.80 1.72
[during] 2026-11-05T00:33:26Z load1/5/15: 2.15 1.80 1.73
[during] 2026-11-05T00:33:36Z load1/5/15: 2.13 1.81 1.73
[during] 2026-11-05T00:33:46Z load1/5/15: 2.03 1.80 1.73
[during] 2026-11-05T00:33:56Z load1/5/15: 2.10 1.82 1.74
[during] 2026-11-05T00:34:06Z load1/5/15: 2.16 1.84 1.75
[during] 2026-11-05T00:34:16Z load1/5/15: 2.06 1.83 1.74
[during] 2026-11-05T00:34:26Z load1/5/15: 2.12 1.85 1.75
[during] 2026-11-05T00:34:36Z load1/5/15: 2.03 1.84 1.75
[during] 2026-11-05T00:34:46Z load1/5/15: 1.95 1.83 1.74
[during] 2026-11-05T00:34:56Z load1/5/15: 1.88 1.82 1.74
[during] 2026-11-05T00:35:06Z load1/5/15: 1.82 1.81 1.74
[during] 2026-11-05T00:35:16Z load1/5/15: 1.92 1.83 1.75
[during] 2026-11-05T00:35:26Z load1/5/15: 1.93 1.83 1.75
[during] 2026-11-05T00:35:36Z load1/5/15: 2.10 1.87 1.76
[during] 2026-11-05T00:35:46Z load1/5/15: 2.08 1.88 1.77
[during] 2026-11-05T00:35:56Z load1/5/15: 1.91 1.85 1.76
[during] 2026-11-05T00:36:07Z load1/5/15: 1.93 1.85 1.76
[during] 2026-11-05T00:36:17Z load1/5/15: 2.25 1.92 1.78
[during] 2026-11-05T00:36:27Z load1/5/15: 2.21 1.92 1.79
[during] 2026-11-05T00:36:37Z load1/5/15: 2.10 1.91 1.78
[during] 2026-11-05T00:36:47Z load1/5/15: 2.08 1.91 1.78
[during] 2026-11-05T00:36:57Z load1/5/15: 1.99 1.90 1.78
[during] 2026-11-05T00:37:07Z load1/5/15: 1.92 1.88 1.78
[during] 2026-11-05T00:37:17Z load1/5/15: 1.93 1.89 1.78
[during] 2026-11-05T00:37:27Z load1/5/15: 1.94 1.89 1.78
[during] 2026-11-05T00:37:37Z load1/5/15: 2.03 1.91 1.79
[during] 2026-11-05T00:37:47Z load1/5/15: 1.94 1.90 1.78
[during] 2026-11-05T00:37:57Z load1/5/15: 1.95 1.90 1.79
[during] 2026-11-05T00:38:07Z load1/5/15: 1.88 1.89 1.78
[during] 2026-11-05T00:38:17Z load1/5/15: 1.90 1.89 1.78
[during] 2026-11-05T00:38:27Z load1/5/15: 1.76 1.86 1.77
[during] 2026-11-05T00:38:37Z load1/5/15: 1.72 1.85 1.77
[during] 2026-11-05T00:38:47Z load1/5/15: 1.68 1.83 1.77
[during] 2026-11-05T00:38:57Z load1/5/15: 1.73 1.84 1.77
[during] 2026-11-05T00:39:07Z load1/5/15: 1.78 1.84 1.77
[during] 2026-11-05T00:39:17Z load1/5/15: 2.05 1.90 1.79
[during] 2026-11-05T00:39:27Z load1/5/15: 2.04 1.90 1.79
```

## Versions

```text
runtime	.NET 10.0.14
OneOffice	0.1.0-preview.1
DocumentFormat.OpenXml	3.5.1
NPOI	2.7.6
BenchmarkDotNet	0.15.8
```

## DocxEdit

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 17.27 ms | 1.00x | 6.2 MB |
| medium | OpenXml | 76.23 ms | 4.41x | 13.2 MB |
| medium | Npoi | 137.78 ms | 7.98x | 32.8 MB |
| medium | OneOffice | 19.72 ms | 1.14x | 12.0 MB |
| pages1000 | RawZip | 98.14 ms | 1.00x | 57.7 MB |
| pages1000 | OpenXml | 996.01 ms | 10.15x | 124.5 MB |
| pages1000 | Npoi | 1.32 s | 13.42x | 344.5 MB |
| pages1000 | OneOffice | 323.95 ms | 3.30x | 78.2 MB |
| small | RawZip | 169.05 us | 1.00x | 72.3 KB |
| small | OpenXml | 1.62 ms | 9.60x | 297.1 KB |
| small | Npoi | 3.01 ms | 17.81x | 1.3 MB |
| small | OneOffice | 956.46 us | 5.66x | 1.6 MB |

## DocxExtract

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | OpenXmlDom | 85.12 ms | 1.00x | 7.0 MB |
| medium | OpenXmlReader | 66.19 ms | 0.78x | 8.2 MB |
| medium | Npoi | 72.75 ms | 0.85x | 33.8 MB |
| medium | OneOffice | 4.37 ms | 0.05x | 1.3 MB |
| pages1000 | OpenXmlDom | 684.45 ms | 1.00x | 78.4 MB |
| pages1000 | OpenXmlReader | 727.26 ms | 1.06x | 93.5 MB |
| pages1000 | Npoi | 1.02 s | 1.49x | 377.4 MB |
| pages1000 | OneOffice | 223.83 ms | 0.33x | 420.1 KB |
| small | OpenXmlDom | 1.25 ms | 1.00x | 306.2 KB |
| small | OpenXmlReader | 1.13 ms | 0.90x | 293.4 KB |
| small | Npoi | 1.56 ms | 1.24x | 792.0 KB |
| small | OneOffice | 586.59 us | 0.47x | 250.1 KB |

## DocxLazyOpen

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 5.36 us | 1.00x | 26.1 KB |
| medium | OpenXml | 760.00 us | 141.87x | 228.9 KB |
| medium | Npoi | 59.16 ms | 11043.11x | 26.1 MB |
| medium | OneOffice | 9.53 ms | 1778.97x | 6.3 MB |
| pages1000 | RawZip | 6.28 us | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 1.07 ms | 170.20x | 212.5 KB |
| pages1000 | Npoi | 917.13 ms | 146001.53x | 287.7 MB |
| pages1000 | OneOffice | 105.33 ms | 16767.68x | 50.9 MB |
| small | RawZip | 6.18 us | 1.00x | 25.0 KB |
| small | OpenXml | 489.07 us | 79.10x | 212.8 KB |
| small | Npoi | 1.43 ms | 231.34x | 684.0 KB |
| small | OneOffice | 476.56 us | 77.07x | 301.3 KB |

## DocxLoad

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 864.28 us | 1.00x | 26.1 KB |
| medium | OpenXml | 75.14 ms | 86.94x | 7.0 MB |
| medium | Npoi | 72.41 ms | 83.78x | 26.1 MB |
| medium | OneOffice | 5.19 ms | 6.01x | 6.3 MB |
| pages1000 | RawZip | 10.15 ms | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 629.25 ms | 62.01x | 78.4 MB |
| pages1000 | Npoi | 841.40 ms | 82.92x | 287.6 MB |
| pages1000 | OneOffice | 105.97 ms | 10.44x | 50.8 MB |
| small | RawZip | 13.23 us | 1.00x | 25.0 KB |
| small | OpenXml | 1.17 ms | 88.16x | 306.8 KB |
| small | Npoi | 1.40 ms | 105.91x | 672.8 KB |
| small | OneOffice | 394.65 us | 29.84x | 242.4 KB |

## DocxLoadSave

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 16.93 ms | 1.00x | 2.5 MB |
| medium | OpenXml | 18.57 ms | 1.10x | 7.5 MB |
| medium | Npoi | 138.98 ms | 8.21x | 32.8 MB |
| medium | OneOffice | 3.60 ms | 0.21x | 8.3 MB |
| pages1000 | RawZip | 95.07 ms | 1.00x | 4.9 MB |
| pages1000 | OpenXml | 98.54 ms | 1.04x | 30.8 MB |
| pages1000 | Npoi | 1.31 s | 13.78x | 344.4 MB |
| pages1000 | OneOffice | 106.98 ms | 1.13x | 54.9 MB |
| small | RawZip | 166.37 us | 1.00x | 33.9 KB |
| small | OpenXml | 792.02 us | 4.76x | 331.9 KB |
| small | Npoi | 3.12 ms | 18.78x | 1.3 MB |
| small | OneOffice | 448.06 us | 2.69x | 263.6 KB |

## Bar: OneOffice against the fastest comparator

Ratio is OneOffice median / the fastest comparator's median in the same class and input; at or below 1.00 is faster. The raw zip row is a floor and is not a comparator.

| Class | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| DocxEdit | medium | 19.72 ms | OpenXml | 76.23 ms | 0.26x |
| DocxEdit | pages1000 | 323.95 ms | OpenXml | 996.01 ms | 0.33x |
| DocxEdit | small | 956.46 us | OpenXml | 1.62 ms | 0.59x |
| DocxExtract | medium | 4.37 ms | OpenXmlReader | 66.19 ms | 0.07x |
| DocxExtract | pages1000 | 223.83 ms | OpenXmlDom | 684.45 ms | 0.33x |
| DocxExtract | small | 586.59 us | OpenXmlReader | 1.13 ms | 0.52x |
| DocxLazyOpen | medium | 9.53 ms | OpenXml | 760.00 us | 12.54x |
| DocxLazyOpen | pages1000 | 105.33 ms | OpenXml | 1.07 ms | 98.52x |
| DocxLazyOpen | small | 476.56 us | OpenXml | 489.07 us | 0.97x |
| DocxLoad | medium | 5.19 ms | Npoi | 72.41 ms | 0.07x |
| DocxLoad | pages1000 | 105.97 ms | OpenXml | 629.25 ms | 0.17x |
| DocxLoad | small | 394.65 us | OpenXml | 1.17 ms | 0.34x |
| DocxLoadSave | medium | 3.60 ms | OpenXml | 18.57 ms | 0.19x |
| DocxLoadSave | pages1000 | 106.98 ms | OpenXml | 98.54 ms | 1.09x |
| DocxLoadSave | small | 448.06 us | OpenXml | 792.02 us | 0.57x |

Geometric mean of the ratios: 0.586x over 15 rows. Worst row: DocxLazyOpen pages1000 at 98.52x.

