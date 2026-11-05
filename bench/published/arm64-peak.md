# Benchmark results

## Machine and load

```text
run: final-arm64-peak
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
[before] 2026-11-05T00:45:36Z load1/5/15: 1.80 1.80 1.77
[after] 2026-11-05T00:46:19Z load1/5/15: 2.02 1.86 1.79
```

## Load while running (sampled)

```text
[during] 2026-11-05T00:45:38Z load1/5/15: 1.80 1.80 1.77
[during] 2026-11-05T00:45:48Z load1/5/15: 1.75 1.79 1.77
[during] 2026-11-05T00:45:58Z load1/5/15: 2.03 1.84 1.78
[during] 2026-11-05T00:46:08Z load1/5/15: 2.03 1.85 1.79
[during] 2026-11-05T00:46:18Z load1/5/15: 2.02 1.85 1.79
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
| npoi.edit | large | 87.0 MB | 0 B | 834.7 MB |
| npoi.edit | medium | 42.2 MB | 0 B | 36.7 MB |
| npoi.edit | pages1000 | 43.0 MB | 0 B | 348.3 MB |
| npoi.edit | small | 41.5 MB | 0 B | 5.0 MB |
| npoi.extract | large | 86.9 MB | 0 B | 729.9 MB |
| npoi.extract | medium | 42.2 MB | 0 B | 34.1 MB |
| npoi.extract | pages1000 | 43.0 MB | 0 B | 377.7 MB |
| npoi.extract | small | 41.4 MB | 0 B | 1.1 MB |
| npoi.lazyopen | large | 87.0 MB | 0 B | 577.3 MB |
| npoi.lazyopen | medium | 42.2 MB | 0 B | 27.9 MB |
| npoi.lazyopen | pages1000 | 43.1 MB | 0 B | 289.4 MB |
| npoi.lazyopen | small | 41.5 MB | 0 B | 2.4 MB |
| npoi.load | large | 86.9 MB | 0 B | 575.8 MB |
| npoi.load | medium | 42.2 MB | 0 B | 26.4 MB |
| npoi.load | pages1000 | 43.0 MB | 0 B | 288.0 MB |
| npoi.load | small | 41.4 MB | 0 B | 1005.5 KB |
| npoi.loadsave | large | 86.8 MB | 0 B | 834.7 MB |
| npoi.loadsave | medium | 42.2 MB | 0 B | 36.7 MB |
| npoi.loadsave | pages1000 | 42.9 MB | 0 B | 348.3 MB |
| npoi.loadsave | small | 41.4 MB | 0 B | 5.1 MB |
| oneoffice.edit | large | 86.9 MB | 0 B | 302.4 MB |
| oneoffice.edit | medium | 42.1 MB | 0 B | 12.2 MB |
| oneoffice.edit | pages1000 | 42.9 MB | 0 B | 78.4 MB |
| oneoffice.edit | small | 41.5 MB | 0 B | 1.8 MB |
| oneoffice.extract | large | 86.9 MB | 0 B | 625.5 KB |
| oneoffice.extract | medium | 42.2 MB | 0 B | 1.5 MB |
| oneoffice.extract | pages1000 | 42.9 MB | 0 B | 584.6 KB |
| oneoffice.extract | small | 41.5 MB | 0 B | 398.6 KB |
| oneoffice.lazyopen | large | 86.9 MB | 0 B | 98.5 MB |
| oneoffice.lazyopen | medium | 42.2 MB | 0 B | 6.4 MB |
| oneoffice.lazyopen | pages1000 | 43.0 MB | 0 B | 51.0 MB |
| oneoffice.lazyopen | small | 41.5 MB | 0 B | 406.3 KB |
| oneoffice.load | large | 87.0 MB | 0 B | 98.4 MB |
| oneoffice.load | medium | 42.2 MB | 0 B | 6.4 MB |
| oneoffice.load | pages1000 | 43.0 MB | 0 B | 50.9 MB |
| oneoffice.load | small | 41.4 MB | 0 B | 347.4 KB |
| oneoffice.loadsave | large | 86.9 MB | 0 B | 230.2 MB |
| oneoffice.loadsave | medium | 42.2 MB | 0 B | 8.4 MB |
| oneoffice.loadsave | pages1000 | 42.8 MB | 0 B | 55.0 MB |
| oneoffice.loadsave | small | 41.5 MB | 0 B | 433.2 KB |
| openxml.edit | large | 86.9 MB | 0 B | 385.8 MB |
| openxml.edit | medium | 42.3 MB | 0 B | 14.2 MB |
| openxml.edit | pages1000 | 43.0 MB | 0 B | 125.5 MB |
| openxml.edit | small | 41.5 MB | 0 B | 1.3 MB |
| openxml.extract.dom | large | 87.0 MB | 0 B | 134.8 MB |
| openxml.extract.dom | medium | 42.2 MB | 0 B | 7.6 MB |
| openxml.extract.dom | pages1000 | 43.0 MB | 0 B | 79.1 MB |
| openxml.extract.dom | small | 41.5 MB | 0 B | 1014.6 KB |
| openxml.extract.reader | large | 86.8 MB | 0 B | 160.7 MB |
| openxml.extract.reader | medium | 42.2 MB | 0 B | 8.9 MB |
| openxml.extract.reader | pages1000 | 43.0 MB | 0 B | 94.2 MB |
| openxml.extract.reader | small | 41.5 MB | 0 B | 1002.0 KB |
| openxml.lazyopen | large | 87.0 MB | 0 B | 857.3 KB |
| openxml.lazyopen | medium | 42.2 MB | 0 B | 741.5 KB |
| openxml.lazyopen | pages1000 | 42.9 MB | 0 B | 725.4 KB |
| openxml.lazyopen | small | 41.5 MB | 0 B | 725.4 KB |
| openxml.load | large | 86.9 MB | 0 B | 134.8 MB |
| openxml.load | medium | 42.2 MB | 0 B | 7.6 MB |
| openxml.load | pages1000 | 43.0 MB | 0 B | 79.1 MB |
| openxml.load | small | 41.5 MB | 0 B | 1015.3 KB |
| openxml.loadsave | large | 86.9 MB | 0 B | 326.2 MB |
| openxml.loadsave | medium | 42.2 MB | 0 B | 7.9 MB |
| openxml.loadsave | pages1000 | 43.0 MB | 0 B | 31.2 MB |
| openxml.loadsave | small | 41.6 MB | 0 B | 773.8 KB |
| rawzip.edit | large | 86.8 MB | 0 B | 258.5 MB |
| rawzip.edit | medium | 42.2 MB | 0 B | 6.5 MB |
| rawzip.edit | pages1000 | 42.9 MB | 0 B | 58.0 MB |
| rawzip.edit | small | 41.5 MB | 0 B | 417.6 KB |
| rawzip.lazyopen | large | 86.9 MB | 0 B | 119.3 KB |
| rawzip.lazyopen | medium | 42.2 MB | 0 B | 114.3 KB |
| rawzip.lazyopen | pages1000 | 42.9 MB | 0 B | 113.2 KB |
| rawzip.lazyopen | small | 41.5 MB | 0 B | 113.2 KB |
| rawzip.load | large | 87.0 MB | 0 B | 119.3 KB |
| rawzip.load | medium | 42.2 MB | 0 B | 114.3 KB |
| rawzip.load | pages1000 | 43.0 MB | 0 B | 113.2 KB |
| rawzip.load | small | 41.5 MB | 0 B | 113.2 KB |
| rawzip.loadsave | large | 86.9 MB | 0 B | 158.7 MB |
| rawzip.loadsave | medium | 42.2 MB | 0 B | 2.8 MB |
| rawzip.loadsave | pages1000 | 42.9 MB | 0 B | 5.2 MB |
| rawzip.loadsave | small | 41.5 MB | 0 B | 379.1 KB |
