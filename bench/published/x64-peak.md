# Benchmark results

## Machine and load

```text
run: final-x64-peak
filter: *Bench.Docx*
threads: 1
load override: forced start at load 2.39 (limit 2.0)
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-05T00:44:50Z load1/5/15: 2.39 5.74 6.77
[after] 2026-11-05T00:45:33Z load1/5/15: 1.65 5.08 6.49
```

## Load while running (sampled)

```text
[during] 2026-11-05T00:44:52Z load1/5/15: 2.28 5.66 6.74
[during] 2026-11-05T00:45:02Z load1/5/15: 2.08 5.51 6.67
[during] 2026-11-05T00:45:12Z load1/5/15: 1.91 5.36 6.61
[during] 2026-11-05T00:45:22Z load1/5/15: 1.77 5.22 6.55
[during] 2026-11-05T00:45:32Z load1/5/15: 1.65 5.08 6.49
```

## Versions

```text
runtime	.NET 10.0.14
OneOffice	0.1.0-preview.1
DocumentFormat.OpenXml	3.5.1
NPOI	2.7.6
BenchmarkDotNet	0.15.8
```

## Peak memory (one run in a fresh process)

Baseline is the working set after the input bytes are loaded; peak is the process high-water mark; allocated is managed bytes allocated by the row.

| Row | Input | Baseline | Peak | Allocated |
|---|---|---:|---:|---:|
| npoi.edit | large | 74.6 MB | 706.9 MB | 834.6 MB |
| npoi.edit | medium | 29.9 MB | 96.6 MB | 36.6 MB |
| npoi.edit | pages1000 | 30.6 MB | 343.9 MB | 348.2 MB |
| npoi.edit | small | 29.2 MB | 62.9 MB | 5.0 MB |
| npoi.extract | large | 74.6 MB | 594.2 MB | 729.9 MB |
| npoi.extract | medium | 29.9 MB | 85.5 MB | 34.1 MB |
| npoi.extract | pages1000 | 30.6 MB | 336.7 MB | 377.7 MB |
| npoi.extract | small | 29.2 MB | 51.6 MB | 1.1 MB |
| npoi.lazyopen | large | 74.6 MB | 565.6 MB | 577.2 MB |
| npoi.lazyopen | medium | 29.9 MB | 85.0 MB | 27.8 MB |
| npoi.lazyopen | pages1000 | 30.7 MB | 281.7 MB | 289.4 MB |
| npoi.lazyopen | small | 29.2 MB | 58.8 MB | 2.4 MB |
| npoi.load | large | 74.6 MB | 569.2 MB | 575.8 MB |
| npoi.load | medium | 29.9 MB | 77.6 MB | 26.4 MB |
| npoi.load | pages1000 | 30.6 MB | 291.2 MB | 287.9 MB |
| npoi.load | small | 29.2 MB | 51.5 MB | 974.6 KB |
| npoi.loadsave | large | 74.6 MB | 706.8 MB | 834.6 MB |
| npoi.loadsave | medium | 29.8 MB | 96.4 MB | 36.6 MB |
| npoi.loadsave | pages1000 | 30.7 MB | 343.6 MB | 348.2 MB |
| npoi.loadsave | small | 29.1 MB | 62.7 MB | 5.0 MB |
| oneoffice.edit | large | 74.5 MB | 288.3 MB | 302.4 MB |
| oneoffice.edit | medium | 29.8 MB | 51.4 MB | 12.2 MB |
| oneoffice.edit | pages1000 | 30.7 MB | 97.5 MB | 78.4 MB |
| oneoffice.edit | small | 29.1 MB | 41.5 MB | 1.8 MB |
| oneoffice.extract | large | 74.6 MB | 85.6 MB | 616.3 KB |
| oneoffice.extract | medium | 29.9 MB | 41.9 MB | 1.5 MB |
| oneoffice.extract | pages1000 | 30.7 MB | 41.7 MB | 575.4 KB |
| oneoffice.extract | small | 29.1 MB | 39.4 MB | 395.4 KB |
| oneoffice.lazyopen | large | 74.6 MB | 169.6 MB | 98.5 MB |
| oneoffice.lazyopen | medium | 29.8 MB | 46.4 MB | 6.4 MB |
| oneoffice.lazyopen | pages1000 | 30.6 MB | 87.5 MB | 51.0 MB |
| oneoffice.lazyopen | small | 29.2 MB | 40.4 MB | 403.1 KB |
| oneoffice.load | large | 74.6 MB | 169.2 MB | 98.4 MB |
| oneoffice.load | medium | 29.8 MB | 46.1 MB | 6.4 MB |
| oneoffice.load | pages1000 | 30.5 MB | 87.2 MB | 50.9 MB |
| oneoffice.load | small | 29.1 MB | 40.0 MB | 344.2 KB |
| oneoffice.loadsave | large | 74.5 MB | 236.5 MB | 230.2 MB |
| oneoffice.loadsave | medium | 30.0 MB | 47.2 MB | 8.4 MB |
| oneoffice.loadsave | pages1000 | 30.7 MB | 90.9 MB | 55.0 MB |
| oneoffice.loadsave | small | 29.2 MB | 40.2 MB | 436.0 KB |
| openxml.edit | large | 74.5 MB | 424.3 MB | 385.8 MB |
| openxml.edit | medium | 29.9 MB | 66.9 MB | 14.1 MB |
| openxml.edit | pages1000 | 30.7 MB | 161.6 MB | 125.4 MB |
| openxml.edit | small | 29.1 MB | 53.6 MB | 1.2 MB |
| openxml.extract.dom | large | 74.5 MB | 214.4 MB | 134.8 MB |
| openxml.extract.dom | medium | 29.9 MB | 60.6 MB | 7.6 MB |
| openxml.extract.dom | pages1000 | 30.6 MB | 134.1 MB | 79.1 MB |
| openxml.extract.dom | small | 29.1 MB | 53.2 MB | 978.4 KB |
| openxml.extract.reader | large | 74.7 MB | 147.2 MB | 160.7 MB |
| openxml.extract.reader | medium | 29.9 MB | 62.4 MB | 8.9 MB |
| openxml.extract.reader | pages1000 | 30.6 MB | 102.6 MB | 94.2 MB |
| openxml.extract.reader | small | 29.2 MB | 52.9 MB | 965.8 KB |
| openxml.lazyopen | large | 74.5 MB | 98.3 MB | 808.9 KB |
| openxml.lazyopen | medium | 29.9 MB | 53.6 MB | 705.2 KB |
| openxml.lazyopen | pages1000 | 30.6 MB | 54.1 MB | 689.2 KB |
| openxml.lazyopen | small | 29.1 MB | 52.6 MB | 689.2 KB |
| openxml.load | large | 74.6 MB | 234.3 MB | 134.8 MB |
| openxml.load | medium | 29.9 MB | 60.6 MB | 7.6 MB |
| openxml.load | pages1000 | 30.7 MB | 126.0 MB | 79.1 MB |
| openxml.load | small | 29.1 MB | 53.3 MB | 978.9 KB |
| openxml.loadsave | large | 74.6 MB | 282.0 MB | 326.2 MB |
| openxml.loadsave | medium | 29.9 MB | 53.2 MB | 7.9 MB |
| openxml.loadsave | pages1000 | 30.6 MB | 74.3 MB | 31.2 MB |
| openxml.loadsave | small | 29.1 MB | 47.5 MB | 749.6 KB |
| rawzip.edit | large | 74.7 MB | 243.4 MB | 258.5 MB |
| rawzip.edit | medium | 29.8 MB | 40.0 MB | 6.5 MB |
| rawzip.edit | pages1000 | 30.7 MB | 84.6 MB | 58.0 MB |
| rawzip.edit | small | 29.1 MB | 34.1 MB | 382.9 KB |
| rawzip.lazyopen | large | 74.6 MB | 78.8 MB | 84.5 KB |
| rawzip.lazyopen | medium | 29.9 MB | 34.1 MB | 79.6 KB |
| rawzip.lazyopen | pages1000 | 30.5 MB | 34.8 MB | 78.4 KB |
| rawzip.lazyopen | small | 29.3 MB | 33.6 MB | 78.4 KB |
| rawzip.load | large | 74.7 MB | 78.9 MB | 84.5 KB |
| rawzip.load | medium | 29.8 MB | 34.0 MB | 79.6 KB |
| rawzip.load | pages1000 | 30.7 MB | 34.9 MB | 78.4 KB |
| rawzip.load | small | 29.2 MB | 33.4 MB | 78.4 KB |
| rawzip.loadsave | large | 74.4 MB | 204.3 MB | 158.7 MB |
| rawzip.loadsave | medium | 29.9 MB | 37.1 MB | 2.8 MB |
| rawzip.loadsave | pages1000 | 30.6 MB | 39.7 MB | 5.2 MB |
| rawzip.loadsave | small | 29.1 MB | 34.0 MB | 344.4 KB |
