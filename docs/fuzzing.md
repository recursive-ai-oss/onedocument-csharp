# Fuzz results

The 1.0 fuzz campaign: every target in `fuzz/run.sh` for 600 s under libFuzzer with SharpFuzz instrumentation (the library assemblies and OneArchive), one process and one core per target, seeded from each target's `WriteSeeds` corpus. How to run and add targets: `fuzz/README.md`.

- Run with `tools/fuzz-campaign.sh prepare`, then `tools/fuzz-campaign.sh run 600 <target>...`: one build, nine to eleven targets side by side per job (12 cores), per-input timeout 10 s, memory limit 1024 MB.
- Machine: 32-core x64 build host, .NET 10.0.14, libFuzzer host and SharpFuzz as pinned in the tools directory.
- A target is clean when it finishes its 600 s with no crash, hang, out-of-memory or invariant failure. All 70 targets finished clean on the final code. In the first pass six of the 62 targets then in `fuzz/run.sh` reported findings (five distinct causes, listed below); each was fixed, and the affected targets plus the targets that share the changed code were run again for 600 s.
- exec/s is low for the targets that build whole documents per input (`rtf`, `html`, `markdown`, `xlsb`, `richtext`, `doc`, `stream`, `pivot`); the feature counts still plateaued.
- Where a target ran twice, the row shows the final run; the first run is named in the last column.

## Results

Executions, exec/s and features are from the final 600 s run of each target.

| Target | s | Executions | exec/s | Features | Crashes found | Fixed in |
|---|---|---|---|---|---|---|
| `formula` | 600 | 13,221,748 | 21,999 | 14,356 | 1 (R1C1 text of an unquoted sheet name that reads as coordinates) | `5019576` |
| `formulaeval` | 600 | 400,309 | 666 | 30,634 | 0 | - |
| `finfuncs` | 600 | 4,067,741 | 6,768 | 18,647 | 1 hang (CUMPRINC, periods past 2^53) | `9dcc11c` |
| `statfuncs` | 600 | 804,390 | 1,338 | 36,085 | 1 hang (CUMIPMT, same loop) | `9dcc11c` |
| `funcs` | 600 | 4,578,403 | 7,617 | 59,207 | 1 (PERCENTOF overflow gave infinity) | `9dcc11c` |
| `lookups` | 600 | 4,152,153 | 6,908 | 11,810 | 0 | - |
| `sniff` | 600 | 9,896,321 | 16,466 | 5,720 | 0 | - |
| `xml` | 600 | 5,677,640 | 9,446 | 4,401 | 0 | - |
| `markup` | 600 | 8,092,985 | 13,465 | 6,236 | 0 | - |
| `opc` | 600 | 1,843,164 | 3,066 | 21,279 | 0 | - |
| `cfb` | 600 | 3,752,735 | 6,244 | 7,708 | 0 | - |
| `sdt` | 600 | 4,597,269 | 7,649 | 16,439 | 0 | - |
| `sdtpath` | 600 | 8,596,140 | 14,303 | 11,042 | 0 | - |
| `text` | 600 | 8,938,909 | 14,873 | 8,192 | 0 | - |
| `revisions` | 600 | 7,545,259 | 12,554 | 17,734 | 0 | - |
| `stories` | 600 | 5,628,271 | 9,364 | 14,298 | 0 | - |
| `pptx` | 600 | 3,322,567 | 5,528 | 13,312 | 0 | - |
| `fieldinstr` | 600 | 22,705,251 | 37,779 | 583 | 0 | - |
| `fields` | 600 | 6,200,191 | 10,316 | 13,990 | 0 | - |
| `numfmt` | 600 | 4,822,946 | 8,024 | 15,252 | 0 | - |
| `numfmtfmt` | 600 | 8,182,639 | 13,615 | 12,574 | 0 | - |
| `sheet` | 600 | 3,970,666 | 6,606 | 16,299 | 0 | - |
| `sst` | 600 | 6,037,797 | 10,046 | 8,722 | 0 | - |
| `wbpart` | 600 | 6,652,512 | 11,069 | 9,949 | 0 | - |
| `styles` | 600 | 2,303,807 | 3,833 | 15,777 | 0 | - |
| `sheetfeat` | 600 | 2,403,873 | 3,999 | 14,780 | 0 | - |
| `table` | 600 | 2,077,910 | 3,457 | 8,048 | 0 | - |
| `pivot` | 600 | 207,716 | 345 | 12,059 | 0 | - |
| `csv` | 600 | 1,750,182 | 2,912 | 17,607 | 0 | - |
| `stream` | 600 | 203,223 | 338 | 13,944 | 0 | - |
| `streamwrite` | 600 | 861,776 | 1,433 | 15,294 | 0 | - |
| `template` | 600 | 770,856 | 1,280 | 30,397 | 1 hang (`{{ N:ak }}` read 24 million variable names) | `9dcc11c` |
| `richtext` | 600 | 95,800 | 159 | 31,869 | 0 | - |
| `mailmerge` | 600 | 269,564 | 448 | 16,419 | 0 | - |
| `math` | 600 | 8,879,988 | 14,775 | 13,007 | 0 | - |
| `sheetrender` | 600 | 2,790,610 | 4,643 | 30,885 | 0 | - |
| `rtf` | 600 | 62,931 | 104 | 32,470 | 0 | - |
| `rtftree` | 600 | 13,991,415 | 23,280 | 2,069 | 0 | - |
| `smartart` | 600 | 584,212 | 965 | 26,920 | 0 | - |
| `doc` | 600 | 124,344 | 206 | 19,564 | 0 | - |
| `sprm` | 600 | 24,230,692 | 40,317 | 422 | 0 | - |
| `xls` | 600 | 472,138 | 785 | 61,245 | 1 (lone surrogate in a string failed the shared string write) | `9dcc11c` |
| `ptg` | 600 | 17,070,536 | 28,403 | 6,976 | 0 | - |
| `ppt` | 600 | 2,018,855 | 3,359 | 28,455 | 0 | - |
| `xml2003` | 600 | 3,158,962 | 5,256 | 20,907 | 0 | - |
| `xlsb` | 600 | 60,561 | 100 | 24,497 | 0 | - |
| `olepropset` | 600 | 2,871,389 | 4,777 | 11,518 | 0 | - |
| `oleobject` | 600 | 5,132,985 | 8,540 | 7,924 | 0 | - |
| `encinfo` | 600 | 5,580,121 | 9,284 | 4,690 | 0 | - |
| `filepass` | 600 | 8,539,304 | 14,208 | 2,260 | 0 | - |
| `html` | 600 | 34,866 | 58 | 23,365 | 0 | - |
| `markdown` | 600 | 48,699 | 81 | 40,700 | 0 | - |
| `markdownparse` | 600 | 5,846,428 | 9,727 | 22,408 | 0 | - |
| `vbacompress` | 600 | 21,427,451 | 35,652 | 365 | 0 | - |
| `vbadir` | 600 | 2,099,559 | 3,493 | 4,749 | 0 | - |
| `vbaproject` | 600 | 6,257,245 | 10,411 | 8,093 | 0 | - |
| `vbaremove` | 600 | 2,083,537 | 3,466 | 3,987 | 0 | - |
| `odfpkg` | 600 | 1,281,327 | 2,131 | 16,066 | 0 | - |
| `odt` | 600 | 196,642 | 327 | 24,601 | 0 | - |
| `odp` | 600 | 497,311 | 827 | 24,860 | 0 | - |
| `ods` | 600 | 429,183 | 714 | 43,255 | 0 | - |
| `openformula` | 600 | 17,621,042 | 29,319 | 11,480 | 0 | - |
| `chart` | 600 | 7,504,495 | 12,486 | 29,498 | 6 files, 5 causes (see below) | `4d2df65` |
| `drawing` | 600 | 6,224,984 | 10,357 | 14,296 | 6 files, 5 causes | `4d2df65` |
| `docparts` | 600 | 2,969,882 | 4,941 | 18,050 | 0 | - |
| `docxlayout` | 600 | 564,515 | 939 | 27,858 | 1 | `4d2df65` |
| `pptxlayout` | 600 | 522,344 | 869 | 13,054 | 0 | - |
| `xlsxprint` | 600 | 3,744,253 | 6,230 | 39,047 | 1 | `4d2df65` |
| `metafile` | 600 | 258,236 | 427 | 6,912 | 3 files, 3 causes | `4d2df65` |
| `operations` | 600 | 113,410 | 188 | 28,158 | 0 | - |


## Findings

Each reduced to the smallest formula, template or text that triggers it, and kept as a regression test.

| Finding | Cause | Fix | Test |
|---|---|---|---|
| `finfuncs`, `statfuncs` hang | `CUMIPMT`/`CUMPRINC` walked the periods with a `double` counter; past 2^53 `p++` does not change `p`, so the loop never ended | only spans below 2^53 are walked, larger ones use the closed form (`src/OneDocument/Calc/Functions/Financial/FinancialFunctions.cs`) | `FuzzRegressionTests.CumulativePaymentsWithHugePeriodsReturnPromptly` |
| `funcs` invariant | `PERCENTOF(256,4.4E-323)` divided into infinity and returned it as a number | a non-finite quotient is `#NUM!` (`Functions/Arrays/GroupFunctions.cs`) | `PercentOfAnOverflowingQuotientIsANumberError` |
| `template` timeout (27 s, 14 bytes) | `{{ N:ak }}` is a whole-column range; each cell reads a template variable, 24 columns × 1,048,576 rows | the template resolver clips whole-axis areas to 256 rows or columns (`Templating/TemplateEngine.cs`) | `TemplateWholeColumnRangeIsBounded` |
| `xls` untyped `ArgumentException` | a BIFF string with a lone UTF-16 surrogate: the shared-string encoder's quick scan did not flag it, so the part writer refused it | the scan flags surrogates and the encoder escapes the unpaired ones as `_xHHHH_` (`Spreadsheets/SharedStrings.cs`) | `SharedStringEncodingEscapesLoneSurrogates` |
| `formula` round trip | `R:1Shet3!E1` (A1 text) kept its unquoted spelling when printed as R1C1 text, where it reads as coordinates and parses as something else | R1C1 printing builds an unquoted prefix from its parts, so the name is quoted (`Calc/FormulaPrinter.cs`) | `R1C1TextQuotesSheetNamesThatLookLikeCoordinates` |

The `formula` target found the last one twice: the first input (`R05C2:\xff!C:B`) is the same cause. The target's own guard for such names had matched only word characters and was left unchanged, so the product fix is what the final run tested.

Output changes: R1C1 formula text now quotes an unquoted sheet prefix that reads as coordinates (it is not written to any file format); the shared-string part escapes unpaired surrogates as `_xD800_`, the way Excel does.

### Second pass: the targets added for the gaps

Eight targets cover what the first pass listed as missing (`chart` also reads the chart-theme override part). `docparts`, `pptxlayout` and `operations` found nothing in 600 s. Everything below is in `src/OneDocument.Rendering` unless noted, and each has a reduced test in `tests/OneDocument.Rendering.Tests/FuzzRegressionTests.cs`. A common cause: OneRender's display list refuses coordinates that are not finite or lie beyond a billion pixels, sizes of zero and blur radii above 4096, and a hostile file can ask for any of them. Each painter now brings such values into range or leaves the shape out, and a list-limit exception becomes an `OfficeLimitException`.

| Target | Finding | Fix | Test |
|---|---|---|---|
| `chart` | a bar taller than the coordinate range (a value far beyond a fixed axis maximum) | every coordinate the chart canvas writes is clamped (`Charts/ChartCanvas.cs`) | `ChartBarFarBeyondTheAxisPaints` |
| `chart` | a font size of 1e90 and a font size of 0 | text sizes are clamped to 0 to 2000 points, and size 0 draws nothing | `ChartTextWithAnAbsurdFontSizePaints`, `ChartTextWithAZeroFontSizePaints` |
| `chart` | `Sequence contains no elements`: a group with no series, in the axis range, the category count and the radial, legend and funnel sizing | `Max` over a possibly empty list takes a default (`Charts/ChartRenderer*.cs`) | `StackedBarGroupWithoutSeriesPaints` |
| `chart`, `drawing` | a display-list limit (too many points, glyphs or segments) escaped as OneRender's own exception | `ChartPainter.Paint` and `DrawingPainter.DrawShapes` throw `OfficeLimitException("DisplayList")` | covered by the fuzz runs |
| `drawing` | a path command with too few points (`<a:moveTo/>`) indexed past its arguments | such a command draws nothing (`Drawing/GeometryDefinition.cs`) | `CustomGeometryCommandWithoutPointsDrawsNothing` |
| `drawing` | a shape placed at an offset of 1e50 EMU gave a transform beyond range | a shape whose transform is out of range is left out and noted `drawing.shape-out-of-range` (`Drawing/DrawingPainter.cs`) | `ShapeFarOffThePageIsLeftOut` |
| `drawing` | an outline width of 1e22 EMU pushed the stroked path out of range | line widths are capped, and a path with an out-of-range coordinate is not drawn | `LineWithAnAbsurdWidthPaints` |
| `drawing` | a shadow offset or blur radius of 1e16 | offsets are clamped to a million pixels and blur to the list's 4096 | `ShadowWithAbsurdOffsetAndBlurPaints` |
| `xlsxprint` | a print-titles name naming row 1,048,577 indexed past the last row | a title row outside 1 to 1,048,576 is ignored (`Spreadsheets/SheetPrintSettings.cs`) | `PrintTitlesOutsideTheSheetAreIgnored` |
| `docxlayout` | a page gutter of 6.7e14 twips put the first glyph out of range | page size and margins are clamped to a million twips in the layout (`Layout/Pages/SectionGeometry.cs`); the hub still reports the stated value | `PageWithAnAbsurdGutterPaints` |
| `metafile` | transformed points beyond range | every transformed point is clamped (`Metafiles/MetafileVisitor.cs`) | `MetafilePathBeyondTheCoordinateRangePaints` |
| `metafile` | a record the list refuses part-way through left clips and transforms open, and the closing pop then threw | the painter tracks what it opened, closes it on failure, reports `render.metafile-damaged` and keeps what was drawn (`Metafiles/MetafilePainter.cs`, `MetafileVisitor.cs`) | `MetafileWithARecordTheListRefusesLeavesABalancedList` (`testdata/fuzz/metafile-record-refused.emf`) |
| `metafile` | OneImage's interpreter raised `OverflowException` on a pen record (`EmfInterpreter.CreatePen`) | the adapter reports `render.metafile-damaged` for an arithmetic or index failure from the interpreter; the defect itself is OneImage's | `MetafileThatOverflowsTheInterpreterIsReportedAsDamaged` (`testdata/fuzz/metafile-createpen-overflow.emf`) |

Output changes: a drawing shape beyond range, a chart value beyond range and a metafile record the list refuses are now left out instead of failing the page; page margins above a million twips are laid out as a million.

## Parsers of untrusted input without a target

None known. The first pass listed DrawingML and chart parts, the Word style, numbering, section and table readers, document and slide layout, the metafile adapter, and the inspect, sanitise, redaction and accessibility passes; the eight targets above cover them. The remaining untargeted code reads only what other targets already feed it, or is not a parser: the SVG and PNG page writers, the PDF writers (OnePDF's) and the CLI argument parser.
