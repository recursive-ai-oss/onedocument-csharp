# OpenDocument spreadsheets in Excel (unanswered)

Run once at the final gate on the Office oracle machine. Nothing here has been answered.

`tools/ods/run-oracle-check.sh WORK` writes, for every workbook in `testdata`, an `.ods` and a `.fods` into `WORK` (the `Export` method of `OdsOracleTests`).
Take about twenty of them, chosen to cover styles and number formats, merged cells, comments, validation, conditional formats, defined names, hidden
rows and columns, and formulas (`ods-rich`, `ods-big` and the calc grid workbooks).

For each `.ods` file, in Excel:

1. Open it. Record whether a repair prompt or an error appears.
2. Save it as `.xlsx` and read it back with OneDocument. Compare, cell by cell, values, formulas, number formats, merges, comments, validation, conditional formats and defined names with what OneDocument reads from the `.ods` itself.
3. Recalculate (Ctrl+Alt+F9) and record every cell whose value changes.

Questions the specifications leave open (our current choice is in `docs/ods.md`):

- Does Excel read `of:=` formulas with `COM.MICROSOFT.` and `LEGACY.` prefixed function names the way we print them (`STDEV.S`, `FDIST`, `NORMSDIST`)?
- Does it read the `[.A1]` references with a `$` sheet prefix and `$'My Sheet'` quoting?
- How does it show a date origin other than 1899-12-30 (`table:null-date`), and 1904 workbooks?
- Does it keep our `table:content-validation` conditions (`of:cell-content-is-between(…)`, `of:is-true-formula(…)`) and the error and help messages?
- Does it read data styles (`number:*-style`) with conditions, colours and fractions to the format codes we print?
- Does it read the `calcext:conditional-format` data bars and icon sets, or only the `style:map` conditions?

Rule to file: any difference in value, formula or format between Excel's reading and ours is filed against the exporter or importer with the file's hash.

## Office run (Word, Excel and PowerPoint 16.0.20527)

Answered as far as the oracle can drive the applications: see the row for this request in [`docs/office-oracle-1.0.md`](../../docs/office-oracle-1.0.md#the-other-requests). Questions not listed there as run are still open.
