# Benchmark results

## Machine and load

```text
run: final-x64-timing
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
[before] 2026-11-05T00:16:29Z load1/5/15: 1.74 2.59 5.32
[after] 2026-11-05T00:24:40Z load1/5/15: 1.63 1.98 3.93
```

## Load while running (sampled)

```text
[during] 2026-11-05T00:16:41Z load1/5/15: 4.43 3.15 5.46
[during] 2026-11-05T00:16:51Z load1/5/15: 4.31 3.16 5.44
[during] 2026-11-05T00:17:02Z load1/5/15: 3.94 3.12 5.41
[during] 2026-11-05T00:17:12Z load1/5/15: 3.56 3.07 5.36
[during] 2026-11-05T00:17:22Z load1/5/15: 3.17 3.00 5.32
[during] 2026-11-05T00:17:32Z load1/5/15: 2.84 2.93 5.27
[during] 2026-11-05T00:17:42Z load1/5/15: 2.55 2.87 5.22
[during] 2026-11-05T00:17:52Z load1/5/15: 2.39 2.82 5.18
[during] 2026-11-05T00:18:02Z load1/5/15: 2.18 2.76 5.14
[during] 2026-11-05T00:18:12Z load1/5/15: 2.14 2.74 5.10
[during] 2026-11-05T00:18:22Z load1/5/15: 2.04 2.70 5.06
[during] 2026-11-05T00:18:32Z load1/5/15: 1.96 2.66 5.03
[during] 2026-11-05T00:18:42Z load1/5/15: 1.81 2.60 4.98
[during] 2026-11-05T00:18:52Z load1/5/15: 1.68 2.55 4.94
[during] 2026-11-05T00:19:02Z load1/5/15: 1.58 2.50 4.90
[during] 2026-11-05T00:19:12Z load1/5/15: 1.49 2.45 4.85
[during] 2026-11-05T00:19:22Z load1/5/15: 1.49 2.41 4.82
[during] 2026-11-05T00:19:32Z load1/5/15: 1.65 2.42 4.79
[during] 2026-11-05T00:19:42Z load1/5/15: 1.77 2.42 4.77
[during] 2026-11-05T00:19:52Z load1/5/15: 1.94 2.44 4.75
[during] 2026-11-05T00:20:02Z load1/5/15: 1.88 2.41 4.71
[during] 2026-11-05T00:20:12Z load1/5/15: 2.05 2.43 4.69
[during] 2026-11-05T00:20:22Z load1/5/15: 1.97 2.40 4.66
[during] 2026-11-05T00:20:32Z load1/5/15: 2.05 2.40 4.64
[during] 2026-11-05T00:20:42Z load1/5/15: 1.96 2.37 4.60
[during] 2026-11-05T00:20:52Z load1/5/15: 2.03 2.37 4.58
[during] 2026-11-05T00:21:02Z load1/5/15: 1.87 2.33 4.54
[during] 2026-11-05T00:21:12Z load1/5/15: 1.74 2.28 4.50
[during] 2026-11-05T00:21:22Z load1/5/15: 1.71 2.26 4.47
[during] 2026-11-05T00:21:32Z load1/5/15: 1.92 2.28 4.45
[during] 2026-11-05T00:21:42Z load1/5/15: 1.94 2.27 4.43
[during] 2026-11-05T00:21:52Z load1/5/15: 1.79 2.23 4.39
[during] 2026-11-05T00:22:02Z load1/5/15: 1.67 2.19 4.35
[during] 2026-11-05T00:22:12Z load1/5/15: 1.57 2.15 4.32
[during] 2026-11-05T00:22:22Z load1/5/15: 1.64 2.15 4.29
[during] 2026-11-05T00:22:32Z load1/5/15: 1.54 2.11 4.25
[during] 2026-11-05T00:22:42Z load1/5/15: 1.54 2.09 4.22
[during] 2026-11-05T00:22:52Z load1/5/15: 1.53 2.07 4.20
[during] 2026-11-05T00:23:02Z load1/5/15: 1.60 2.06 4.17
[during] 2026-11-05T00:23:12Z load1/5/15: 1.51 2.03 4.14
[during] 2026-11-05T00:23:22Z load1/5/15: 1.43 2.00 4.10
[during] 2026-11-05T00:23:32Z load1/5/15: 1.67 2.03 4.09
[during] 2026-11-05T00:23:42Z load1/5/15: 2.03 2.09 4.09
[during] 2026-11-05T00:23:52Z load1/5/15: 2.03 2.09 4.07
[during] 2026-11-05T00:24:02Z load1/5/15: 1.94 2.07 4.04
[during] 2026-11-05T00:24:12Z load1/5/15: 1.96 2.07 4.02
[during] 2026-11-05T00:24:22Z load1/5/15: 1.81 2.03 3.98
[during] 2026-11-05T00:24:32Z load1/5/15: 1.68 2.00 3.95
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
| medium | RawZip | 18.04 ms | 1.00x | 6.2 MB |
| medium | OpenXml | 94.18 ms | 5.22x | 13.2 MB |
| medium | Npoi | 111.64 ms | 6.19x | 32.8 MB |
| medium | OneOffice | 20.18 ms | 1.12x | 12.0 MB |
| pages1000 | RawZip | 121.34 ms | 1.00x | 57.7 MB |
| pages1000 | OpenXml | 840.10 ms | 6.92x | 124.5 MB |
| pages1000 | Npoi | 1.44 s | 11.91x | 344.4 MB |
| pages1000 | OneOffice | 365.82 ms | 3.01x | 78.2 MB |
| small | RawZip | 122.43 us | 1.00x | 72.3 KB |
| small | OpenXml | 1.14 ms | 9.32x | 297.0 KB |
| small | Npoi | 2.73 ms | 22.30x | 1.3 MB |
| small | OneOffice | 966.90 us | 7.90x | 1.6 MB |

## DocxExtract

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | OpenXmlDom | 78.59 ms | 1.00x | 7.0 MB |
| medium | OpenXmlReader | 59.98 ms | 0.76x | 8.2 MB |
| medium | Npoi | 97.70 ms | 1.24x | 33.8 MB |
| medium | OneOffice | 4.44 ms | 0.06x | 1.3 MB |
| pages1000 | OpenXmlDom | 749.50 ms | 1.00x | 78.4 MB |
| pages1000 | OpenXmlReader | 450.48 ms | 0.60x | 93.5 MB |
| pages1000 | Npoi | 1.06 s | 1.41x | 377.4 MB |
| pages1000 | OneOffice | 210.62 ms | 0.28x | 420.1 KB |
| small | OpenXmlDom | 1.07 ms | 1.00x | 305.9 KB |
| small | OpenXmlReader | 1.49 ms | 1.39x | 293.2 KB |
| small | Npoi | 1.32 ms | 1.24x | 791.8 KB |
| small | OneOffice | 653.19 us | 0.61x | 250.1 KB |

## DocxLazyOpen

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 6.74 us | 1.00x | 26.1 KB |
| medium | OpenXml | 828.15 us | 122.91x | 228.6 KB |
| medium | Npoi | 56.96 ms | 8453.17x | 26.1 MB |
| medium | OneOffice | 6.10 ms | 905.40x | 6.3 MB |
| pages1000 | RawZip | 7.26 us | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 1.00 ms | 138.38x | 212.5 KB |
| pages1000 | Npoi | 1.13 s | 155459.54x | 287.6 MB |
| pages1000 | OneOffice | 110.76 ms | 15264.30x | 50.9 MB |
| small | RawZip | 6.41 us | 1.00x | 25.0 KB |
| small | OpenXml | 515.89 us | 80.51x | 212.5 KB |
| small | Npoi | 1.29 ms | 200.85x | 683.8 KB |
| small | OneOffice | 475.85 us | 74.26x | 301.2 KB |

## DocxLoad

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 524.85 us | 1.00x | 26.1 KB |
| medium | OpenXml | 77.85 ms | 148.33x | 7.0 MB |
| medium | Npoi | 49.35 ms | 94.02x | 26.1 MB |
| medium | OneOffice | 6.84 ms | 13.03x | 6.3 MB |
| pages1000 | RawZip | 6.24 ms | 1.00x | 25.0 KB |
| pages1000 | OpenXml | 695.49 ms | 111.54x | 78.4 MB |
| pages1000 | Npoi | 1.14 s | 182.49x | 287.6 MB |
| pages1000 | OneOffice | 108.77 ms | 17.44x | 50.8 MB |
| small | RawZip | 11.27 us | 1.00x | 25.0 KB |
| small | OpenXml | 993.35 us | 88.15x | 306.5 KB |
| small | Npoi | 1.20 ms | 106.57x | 672.6 KB |
| small | OneOffice | 377.97 us | 33.54x | 242.3 KB |

## DocxLoadSave

| Input | Method | Median | Ratio | Allocated/op |
|---|---|---:|---:|---:|
| medium | RawZip | 16.91 ms | 1.00x | 2.5 MB |
| medium | OpenXml | 18.66 ms | 1.10x | 7.5 MB |
| medium | Npoi | 137.10 ms | 8.11x | 32.8 MB |
| medium | OneOffice | 12.79 ms | 0.76x | 8.3 MB |
| pages1000 | RawZip | 111.43 ms | 1.00x | 4.9 MB |
| pages1000 | OpenXml | 125.00 ms | 1.12x | 30.8 MB |
| pages1000 | Npoi | 1.43 s | 12.79x | 344.4 MB |
| pages1000 | OneOffice | 115.63 ms | 1.04x | 54.9 MB |
| small | RawZip | 118.52 us | 1.00x | 33.9 KB |
| small | OpenXml | 828.38 us | 6.99x | 331.7 KB |
| small | Npoi | 2.54 ms | 21.46x | 1.3 MB |
| small | OneOffice | 406.40 us | 3.43x | 263.6 KB |

## Bar: OneOffice against the fastest comparator

Ratio is OneOffice median / the fastest comparator's median in the same class and input; at or below 1.00 is faster. The raw zip row is a floor and is not a comparator.

| Class | Input | OneOffice | Fastest comparator | Comparator median | Ratio |
|---|---|---:|---|---:|---:|
| DocxEdit | medium | 20.18 ms | OpenXml | 94.18 ms | 0.21x |
| DocxEdit | pages1000 | 365.82 ms | OpenXml | 840.10 ms | 0.44x |
| DocxEdit | small | 966.90 us | OpenXml | 1.14 ms | 0.85x |
| DocxExtract | medium | 4.44 ms | OpenXmlReader | 59.98 ms | 0.07x |
| DocxExtract | pages1000 | 210.62 ms | OpenXmlReader | 450.48 ms | 0.47x |
| DocxExtract | small | 653.19 us | OpenXmlDom | 1.07 ms | 0.61x |
| DocxLazyOpen | medium | 6.10 ms | OpenXml | 828.15 us | 7.37x |
| DocxLazyOpen | pages1000 | 110.76 ms | OpenXml | 1.00 ms | 110.31x |
| DocxLazyOpen | small | 475.85 us | OpenXml | 515.89 us | 0.92x |
| DocxLoad | medium | 6.84 ms | Npoi | 49.35 ms | 0.14x |
| DocxLoad | pages1000 | 108.77 ms | OpenXml | 695.49 ms | 0.16x |
| DocxLoad | small | 377.97 us | OpenXml | 993.35 us | 0.38x |
| DocxLoadSave | medium | 12.79 ms | OpenXml | 18.66 ms | 0.69x |
| DocxLoadSave | pages1000 | 115.63 ms | OpenXml | 125.00 ms | 0.93x |
| DocxLoadSave | small | 406.40 us | OpenXml | 828.38 us | 0.49x |

Geometric mean of the ratios: 0.682x over 15 rows. Worst row: DocxLazyOpen pages1000 at 110.31x.

