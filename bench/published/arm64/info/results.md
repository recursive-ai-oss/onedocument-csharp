# Benchmark results

## Machine and load

```text
run: arm64-info
group: fmt info
threads: 1
load override: none
cpu: Apple M2
cores: 8 (performance 4, efficiency 4)
isa: arm64 (NEON)
memory: 8 GB
storage (inputs): /dev/disk3s5 478724992
os: Darwin 27.0.0 arm64
sdk: 10.0.403
[before] 2026-11-06T09:40:59Z load1/5/15: 2.26 2.16 2.03
[after] 2026-11-06T09:41:30Z load1/5/15: 2.21 2.16 2.03
```

## Load while running (sampled)

```text
[during] 2026-11-06T09:41:00Z load1/5/15: 2.26 2.16 2.03
[during] 2026-11-06T09:41:10Z load1/5/15: 2.30 2.17 2.03
[during] 2026-11-06T09:41:20Z load1/5/15: 2.25 2.17 2.03
[during] 2026-11-06T09:41:30Z load1/5/15: 2.21 2.16 2.03
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
| medium.doc | onedocument.doc.extract | 15 | 10.5 ms | 10.5 ms | 9.7 MB | 70 MB |
| values-10000.ods | onedocument.ods.model | 3 | 991.6 ms | 985.4 ms | 351.1 MB | 166 MB |
| medium.odt | onedocument.odt.extract | 3 | 391.8 ms | 391.4 ms | 61.5 MB | 91 MB |
| medium.docx | onedocument.render.docx | 3 | 1.74 s | 1.58 s | 967.7 MB | 142 MB |
| small.docx | onedocument.render.docx | 15 | 38.4 ms | 38.3 ms | 10.7 MB | 91 MB |
| medium.rtf | onedocument.rtf.extract | 3 | 249.5 ms | 248.5 ms | 112.2 MB | 130 MB |

