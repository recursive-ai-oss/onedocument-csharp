# Benchmark results

## Machine and load

```text
run: x64-info
group: fmt info
threads: 1
load override: none
cpu: AMD Ryzen 9 7950X3D 16-Core Processor
cores: 32 (visible to this run: 32)
isa: pclmulqdq sse4_2 aes avx2 bmi2 avx512f sha_ni 
memory: 125 GB
storage (inputs): /dev/nvme0n1p4 1.2T
os: Linux 7.0.0-38-generic x86_64
sdk: 10.0.403
[before] 2026-11-06T08:49:06Z load1/5/15: 1.55 1.79 2.94
[after] 2026-11-06T08:49:39Z load1/5/15: 1.52 1.76 2.89
```

## Load while running (sampled)

```text
[during] 2026-11-06T08:49:07Z load1/5/15: 1.55 1.79 2.94
[during] 2026-11-06T08:49:17Z load1/5/15: 1.54 1.78 2.92
[during] 2026-11-06T08:49:27Z load1/5/15: 1.62 1.79 2.91
[during] 2026-11-06T08:49:37Z load1/5/15: 1.52 1.76 2.89
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

## Rows with no managed comparator (information)

| Input | Row | Reps | Median | Min | Allocated | Peak working set |
|---|---|---:|---:|---:|---:|---:|
| medium.doc | onedocument.doc.extract | 15 | 10.4 ms | 10.4 ms | 9.7 MB | 67 MB |
| values-10000.ods | onedocument.ods.model | 3 | 791.8 ms | 784.7 ms | 351.1 MB | 198 MB |
| medium.odt | onedocument.odt.extract | 3 | 504.8 ms | 496.8 ms | 61.5 MB | 119 MB |
| medium.docx | onedocument.render.docx | 3 | 1.25 s | 1.23 s | 967.2 MB | 175 MB |
| small.docx | onedocument.render.docx | 15 | 40.1 ms | 39.4 ms | 10.7 MB | 84 MB |
| medium.rtf | onedocument.rtf.extract | 3 | 268.6 ms | 266.2 ms | 112.1 MB | 151 MB |

