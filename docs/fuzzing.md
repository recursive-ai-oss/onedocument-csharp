# Fuzz results

The 1.0 fuzz campaign: every target in `fuzz/run.sh` for 600 s under libFuzzer with SharpFuzz instrumentation (the library assemblies and OneArchive), one process and one core per target, seeded from each target's `WriteSeeds` corpus. How to run and add targets: `fuzz/README.md`.

- Run with `tools/fuzz-campaign.sh prepare`, then `tools/fuzz-campaign.sh run 600 <target>...`: one build, nine to eleven targets side by side per queue job (12 cores), per-input timeout 10 s, memory limit 1024 MB.
- Machine: 32-core x64 build host, .NET 10.0.14, libFuzzer host and SharpFuzz as pinned in the tools directory.
- A target is clean when it finishes its 600 s with no crash, hang, out-of-memory or invariant failure. All 62 targets finished clean on the final code. Six targets reported findings (five distinct causes, listed below); each was fixed, and the affected targets plus the targets that share the changed code were run again for 600 s.
- exec/s is low for the targets that build whole documents per input (`rtf`, `html`, `markdown`, `xlsb`, `richtext`, `doc`, `stream`, `pivot`); the feature counts still plateaued.
- Where a target ran twice, the row shows the final run; the first run is named in the last column.

## Results

Executions, exec/s and features are from the final 600 s run of each target.

| Target | s | Executions | exec/s | Features | Crashes found | Fixed in | Final run job (remote / wq) |
|---|---|---|---|---|---|---|---|
| `formula` | 600 | 13,221,748 | 21,999 | 14,356 | 1 (R1C1 text of an unquoted sheet name that reads as coordinates) | `5019576` | `txvji01l` / 5299 (first run `5hl4ou2t` / 5277) |
| `formulaeval` | 600 | 400,309 | 666 | 30,634 | 0 | - | `txvji01l` / 5299 (first run `5hl4ou2t` / 5277) |
| `finfuncs` | 600 | 4,067,741 | 6,768 | 18,647 | 1 hang (CUMPRINC, periods past 2^53) | `9dcc11c` | `qopabahj` / 5292 (first run `5hl4ou2t` / 5277) |
| `statfuncs` | 600 | 804,390 | 1,338 | 36,085 | 1 hang (CUMIPMT, same loop) | `9dcc11c` | `qopabahj` / 5292 (first run `5hl4ou2t` / 5277) |
| `funcs` | 600 | 4,578,403 | 7,617 | 59,207 | 1 (PERCENTOF overflow gave infinity) | `9dcc11c` | `qopabahj` / 5292 (first run `5hl4ou2t` / 5277) |
| `lookups` | 600 | 4,152,153 | 6,908 | 11,810 | 0 | - | `qopabahj` / 5292 (first run `5hl4ou2t` / 5277) |
| `sniff` | 600 | 9,896,321 | 16,466 | 5,720 | 0 | - | `5hl4ou2t` / 5277 |
| `xml` | 600 | 5,677,640 | 9,446 | 4,401 | 0 | - | `5hl4ou2t` / 5277 |
| `markup` | 600 | 8,092,985 | 13,465 | 6,236 | 0 | - | `5hl4ou2t` / 5277 |
| `opc` | 600 | 1,843,164 | 3,066 | 21,279 | 0 | - | `5hl4ou2t` / 5277 |
| `cfb` | 600 | 3,752,735 | 6,244 | 7,708 | 0 | - | `5hl4ou2t` / 5277 |
| `sdt` | 600 | 4,597,269 | 7,649 | 16,439 | 0 | - | `or2y5er3` / 5278 |
| `sdtpath` | 600 | 8,596,140 | 14,303 | 11,042 | 0 | - | `or2y5er3` / 5278 |
| `text` | 600 | 8,938,909 | 14,873 | 8,192 | 0 | - | `or2y5er3` / 5278 |
| `revisions` | 600 | 7,545,259 | 12,554 | 17,734 | 0 | - | `or2y5er3` / 5278 |
| `stories` | 600 | 5,628,271 | 9,364 | 14,298 | 0 | - | `or2y5er3` / 5278 |
| `pptx` | 600 | 3,322,567 | 5,528 | 13,312 | 0 | - | `or2y5er3` / 5278 |
| `fieldinstr` | 600 | 22,705,251 | 37,779 | 583 | 0 | - | `or2y5er3` / 5278 |
| `fields` | 600 | 6,200,191 | 10,316 | 13,990 | 0 | - | `or2y5er3` / 5278 |
| `numfmt` | 600 | 4,822,946 | 8,024 | 15,252 | 0 | - | `or2y5er3` / 5278 |
| `numfmtfmt` | 600 | 8,182,639 | 13,615 | 12,574 | 0 | - | `or2y5er3` / 5278 |
| `sheet` | 600 | 3,970,666 | 6,606 | 16,299 | 0 | - | `or2y5er3` / 5278 |
| `sst` | 600 | 6,037,797 | 10,046 | 8,722 | 0 | - | `qopabahj` / 5292 (first run `pu6p88ha` / 5279) |
| `wbpart` | 600 | 6,652,512 | 11,069 | 9,949 | 0 | - | `qopabahj` / 5292 (first run `pu6p88ha` / 5279) |
| `styles` | 600 | 2,303,807 | 3,833 | 15,777 | 0 | - | `pu6p88ha` / 5279 |
| `sheetfeat` | 600 | 2,403,873 | 3,999 | 14,780 | 0 | - | `pu6p88ha` / 5279 |
| `table` | 600 | 2,077,910 | 3,457 | 8,048 | 0 | - | `pu6p88ha` / 5279 |
| `pivot` | 600 | 207,716 | 345 | 12,059 | 0 | - | `pu6p88ha` / 5279 |
| `csv` | 600 | 1,750,182 | 2,912 | 17,607 | 0 | - | `pu6p88ha` / 5279 |
| `stream` | 600 | 203,223 | 338 | 13,944 | 0 | - | `pu6p88ha` / 5279 |
| `streamwrite` | 600 | 861,776 | 1,433 | 15,294 | 0 | - | `pu6p88ha` / 5279 |
| `template` | 600 | 770,856 | 1,280 | 30,397 | 1 hang (`{{ N:ak }}` read 24 million variable names) | `9dcc11c` | `txvji01l` / 5299 (first run `pu6p88ha` / 5279) |
| `richtext` | 600 | 95,800 | 159 | 31,869 | 0 | - | `6q7ekipa` / 5280 |
| `mailmerge` | 600 | 269,564 | 448 | 16,419 | 0 | - | `qopabahj` / 5292 (first run `6q7ekipa` / 5280) |
| `math` | 600 | 8,879,988 | 14,775 | 13,007 | 0 | - | `6q7ekipa` / 5280 |
| `sheetrender` | 600 | 2,790,610 | 4,643 | 30,885 | 0 | - | `6q7ekipa` / 5280 |
| `rtf` | 600 | 62,931 | 104 | 32,470 | 0 | - | `6q7ekipa` / 5280 |
| `rtftree` | 600 | 13,991,415 | 23,280 | 2,069 | 0 | - | `6q7ekipa` / 5280 |
| `smartart` | 600 | 584,212 | 965 | 26,920 | 0 | - | `6q7ekipa` / 5280 |
| `doc` | 600 | 124,344 | 206 | 19,564 | 0 | - | `6q7ekipa` / 5280 |
| `sprm` | 600 | 24,230,692 | 40,317 | 422 | 0 | - | `6q7ekipa` / 5280 |
| `xls` | 600 | 472,138 | 785 | 61,245 | 1 (lone surrogate in a string failed the shared string write) | `9dcc11c` | `txvji01l` / 5299 (first run `6q7ekipa` / 5280) |
| `ptg` | 600 | 17,070,536 | 28,403 | 6,976 | 0 | - | `txvji01l` / 5299 (first run `6q7ekipa` / 5280) |
| `ppt` | 600 | 2,018,855 | 3,359 | 28,455 | 0 | - | `6q7ekipa` / 5280 |
| `xml2003` | 600 | 3,158,962 | 5,256 | 20,907 | 0 | - | `bupmujkc` / 5281 |
| `xlsb` | 600 | 60,561 | 100 | 24,497 | 0 | - | `bupmujkc` / 5281 |
| `olepropset` | 600 | 2,871,389 | 4,777 | 11,518 | 0 | - | `bupmujkc` / 5281 |
| `oleobject` | 600 | 5,132,985 | 8,540 | 7,924 | 0 | - | `bupmujkc` / 5281 |
| `encinfo` | 600 | 5,580,121 | 9,284 | 4,690 | 0 | - | `bupmujkc` / 5281 |
| `filepass` | 600 | 8,539,304 | 14,208 | 2,260 | 0 | - | `bupmujkc` / 5281 |
| `html` | 600 | 34,866 | 58 | 23,365 | 0 | - | `bupmujkc` / 5281 |
| `markdown` | 600 | 48,699 | 81 | 40,700 | 0 | - | `bupmujkc` / 5281 |
| `markdownparse` | 600 | 5,846,428 | 9,727 | 22,408 | 0 | - | `bupmujkc` / 5281 |
| `vbacompress` | 600 | 21,427,451 | 35,652 | 365 | 0 | - | `4ejqlo5q` / 5282 |
| `vbadir` | 600 | 2,099,559 | 3,493 | 4,749 | 0 | - | `4ejqlo5q` / 5282 |
| `vbaproject` | 600 | 6,257,245 | 10,411 | 8,093 | 0 | - | `4ejqlo5q` / 5282 |
| `vbaremove` | 600 | 2,083,537 | 3,466 | 3,987 | 0 | - | `4ejqlo5q` / 5282 |
| `odfpkg` | 600 | 1,281,327 | 2,131 | 16,066 | 0 | - | `4ejqlo5q` / 5282 |
| `odt` | 600 | 196,642 | 327 | 24,601 | 0 | - | `4ejqlo5q` / 5282 |
| `odp` | 600 | 497,311 | 827 | 24,860 | 0 | - | `4ejqlo5q` / 5282 |
| `ods` | 600 | 429,183 | 714 | 43,255 | 0 | - | `txvji01l` / 5299 (first run `4ejqlo5q` / 5282) |
| `openformula` | 600 | 17,621,042 | 29,319 | 11,480 | 0 | - | `txvji01l` / 5299 (first run `4ejqlo5q` / 5282) |

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

## Parsers of untrusted input without a target

Not in this unit's scope; listed for a decision. The end-to-end paths (`Office.Open` on a whole file) reach most of these through the format targets, but not with their own input shapes:

- DrawingML and chart parts (`.Rendering/Charts`, `Drawing`): chart read and layout of a hostile `c:chartSpace` or `cx:` part.
- Word styles, numbering, section and table readers as parts of their own (`Documents/Styles`, `Numbering`, `Sections`, `Tables`): reached through `text`, `stories` and `fields` only in part.
- Document layout (`.Rendering/Layout`) on a hostile document part; only `sheetrender` and `smartart` have targets.
- Metafile commands in `.Rendering/Metafiles` (the interpreter itself is OneImage's).
- Inspect, sanitise, redaction and accessibility passes over hostile documents.
