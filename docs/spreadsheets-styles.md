# Spreadsheets: styles, tables, conditional formatting, validation and names

`RecursiveAI.OneOffice.Spreadsheets` models the rest of the workbook around the cells (ECMA-376 Part 1 §18). Every model is a view over the part's markup tree: what the model does not read (extension lists, unknown attributes and elements, attribute order, prefixes) stays where it was when the part is saved, and a part nobody touched is copied as stored. Reading a feature never marks the workbook modified. Defect codes are in `defects.md` (`xlsx.`).

## Styles

`Workbook.Styles` (`StyleSheet`) is read from the `styles` part the first time it is used. A workbook without one gets a default sheet in memory (Calibri 11, one cell format, `Normal`); it becomes a part only when something is added.

| Collection | Read | Add |
|---|---|---|
| number formats | `NumberFormats` (id to code), `GetNumberFormatCode(id)`, `GetNumberFormat(formatIndex)` (parsed, cached) | `GetOrAddNumberFormat(code)`: a built-in code reuses its id, an existing custom code its id, else the next id from 164 |
| fonts, fills (pattern and gradient), borders | `GetFont`, `GetFill`, `GetBorder` | through `GetOrAdd` |
| cell formats (`cellXfs`) | `GetCellFormat(i)` as stored; `Resolve(i)` as effective | `GetOrAdd(CellStyleSpec)` |
| named cell styles (`cellStyles` + `cellStyleXfs`) | `CellStyles`, `GetStyleFormat(i)` | `AddNamedStyle(name, spec)` |
| differential formats | `GetDifferentialFormat(i)` | `AddDifferentialFormat` |
| table styles | `TableStyles`, `DefaultTableStyle` | read only |

### Resolution

`Resolve(formatIndex)` returns a `ResolvedCellStyle` (font, fill, border, alignment, protection, number format id and code, quote prefix, the named style's name), one object per format, so cells cost nothing. Rules:

- A cell format's own `fontId`, `fillId`, `borderId` and `numFmtId` are always used; the `applyX` flags are not consulted. Checked in Excel 16.0.20527 with generated cell formats whose named style has 0% and whose own format is General or 0.00%, with `applyNumberFormat` false, absent and true: Excel shows the cell format's own format in all cases. An alignment or protection child missing from the cell format is taken from its named style.
- An index past the last cell format resolves like format 0. A missing id (font 9 of 3) resolves to the default font, an empty fill or an empty border.
- A `numFmt` in the file overrides a built-in id of the same number; otherwise built-in ids come from the US-English table in `NumberFormat`.
- Colours: `ResolveColor` gives ARGB. Indexed colours use the file's `indexedColors` when present, else the default palette of §18.8.27 (64 is the system foreground, 65 the background). Theme colours use the workbook's theme part (`lt1, dk1, lt2, dk2, accent1–6, hlink, folHlink` for indexes 0–11); a workbook without a usable theme (defect `xlsx.theme-unreadable` when it is broken) uses the current Office colours. A tint is applied in HLS space as §18.3.1.15 describes (lighter: `L' = L(1-t)+t`, darker: `L' = L(1+t)`); `8FAADC` for accent 1 at +0.4 matches what Excel names "Lighter 40%".
- A solid fill in a differential format shows its **background** colour (Excel's convention); `CellFill.Foreground`/`Background` are the stored attributes.

### Interning

`GetOrAdd(spec)` reuses an existing cell format that has the same number format, font, fill, border, alignment, protection, quote prefix and named style, and otherwise appends one. Fonts, fills and borders are interned by their canonical XML. Apply flags are not compared; a new format sets `applyX="1"` for each property that differs from its named style's. A format with attributes or children the model does not read (`pivotButton`, `extLst`) is never reused. Appending changes `count` of the collection and leaves everything else in the part alone. A cell's `StyleIndex` stays a plain integer: cells hold no style objects.

### Formatted text and dates

`Cell.FormattedText` formats a numeric cell through its resolved number format with the workbook's date system (`workbookPr/@date1904`, `Workbook.DateSystem`), text through the format's text section, booleans as TRUE/FALSE. General uses the width of a default column (11 characters); `GetFormattedText(width)` takes another. `Cell.TryFormatNumber` writes into a span without allocating. `Cell.IsDate`, `Cell.DateValue` and `Cell.SetDate` use the same rules; `SetDate` gives a cell whose format does not show dates the built-in date format. The format engine is `RecursiveAI.OneOffice.NumberFormats` (see `number-formats.md`).

## Tables

`Worksheet.Tables` reads the sheet's `tableParts` and their parts when first enumerated. `Table` exposes id, name, display name, range, header and totals row counts, data range, autofilter range, columns (id, name, totals function and label, calculated column formula) and the style name. Structured-reference evaluation belongs to the calculation layer.

- `Add(name, range, hasHeaderRow, style)` writes the header cells (a text cell keeps its text, a repeated name gets a number appended, anything else becomes `ColumnN`), creates `/xl/tables/tableN.xml` with the next free table id, the sheet relationship and the `tablePart` element (declaring `xmlns:r` on the sheet root when it is not in scope).
- `Resize(range)` changes `ref`, the autofilter (without the totals row), adds or removes columns at the end (new names `ColumnN`, unique) and rewrites header cells that do not hold their column's name. It refuses a range that overlaps another table or puts a merged cell in the header.
- `Rename` and `SetStyle` change the attributes. `Remove` drops the part, its relationship and the element; cells keep their values.
- A table name must be valid as a defined name, must not look like a cell reference, and must be unique among the workbook's tables and workbook-scoped names (case-insensitive).
- Table parts are rewritten only when edited. Removing a sheet already removes the tables only it uses.

## Conditional formatting

`Worksheet.ConditionalFormats` lists the `conditionalFormatting` groups of the sheet body and then the x14 groups in `extLst` (`IsExtended`). `ConditionalFormatRule` reads type, priority, `dxfId`, `stopIfTrue`, operator, text, time period, rank/percent/bottom, above-average, the formulas (text), colour scales, data bars and icon sets (value points included). `Add(ranges, spec)` builds a standard rule (`ConditionalRuleSpec.Expression`, `CellIs`, `TwoColorScale`, `ThreeColorScale`, `DataBars`, `Icons`, `Top`, `Duplicates`) in the schema position after `mergeCells`/`phoneticPr` and before `dataValidations`, with the next free priority across both forms (so it is evaluated last; set `Priority` to reorder). `Remove` deletes a group, and an emptied x14 container with it. Priorities and `dxfId` can be edited in place. Extended rules are not authored.

## Data validation

`Worksheet.Validations` lists the `dataValidation` elements and the x14 ones (which hold lists and formulas that refer to other sheets in older files). A validation reads type, error style, operator, allow-blank, the in-cell drop-down (the file stores the inverse `showDropDown`), messages, ranges and both formulas as text. `Add(ranges, spec)` takes `DataValidationSpec` (`List`, `ListFromRange`, `WholeBetween`, `DecimalBetween`, `Custom`, or the properties) and checks Excel's limits (titles 32 characters, messages 255, an inline list 255). `Remove` deletes one and drops an emptied container. An existing validation is replaced by removing it and adding a new one.

## Defined names

`Workbook.Names` lists the `definedName` elements in file order: name, scope sheet, formula text, hidden, comment, function/macro flags, and the built-in kind for `_xlnm.` names. `Add(name, formula, scope, hidden)`, `AddBuiltIn(kind, sheet, formula)`, `Rename`, `Remove` and `Find(name, scope)` (case-insensitive) edit only `definedNames`; an emptied container is removed because the schema needs one name inside it. `IsValidName` applies Excel's rules (first character a letter, underscore or backslash; letters, digits, `_ . ? \` after; at most 255 characters; not a cell reference in A1 or R1C1 form). Renaming a name or a sheet does not rewrite formulas that use the old text, and formula text is not parsed: that belongs to the formula layer. Sheet moves keep `localSheetId` right, as before.

## Limits

- Counts are bounded by `MaxRecordCount` through the markup tree; every part counts against `MaxPartSize` and the XML depth limits.
- Resolution allocates one object per cell format, on first use. Reading cells does not touch the style sheet.
- A damaged table part is ignored (`xlsx.table-unreadable`) and kept as stored. A styles part that is not a style sheet raises `xlsx.styles-wrong-root` when first used, and cells still read.
