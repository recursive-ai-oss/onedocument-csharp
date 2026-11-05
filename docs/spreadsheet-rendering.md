# Spreadsheet print rendering

How OneDocument prints a workbook the way Excel does: which cells land on which page, how a cell's text is formed and placed, and what
is painted. Package `RecursiveAI.OneDocument.Rendering`, namespace `RecursiveAI.OneDocument.Rendering.Spreadsheets`. References are to
ECMA-376 Part 1 unless noted. The page layer is shared with `layout.md` (display list in CSS pixels, tagged paint, fonts and substitution
notes). Rules the specification leaves open are listed at the end as points to settle against Excel's own PDF export.

## The pipeline

`WorkbookRenderer.Layout(workbook, SheetRenderOptions?)` returns a `SheetLayoutDocument` (the pages in print order, font and layout notes).
`WorkbookRenderer.Paint(layout, page)` returns the page's display list; `WritePdf` and `RenderPng` write it. `Office.Convert("in.xlsx",
"out.pdf")` and `workbook.SaveAs("out.pdf")` go through the same path. A PNG written through `SaveAs` is page 1.

Rendering reads the cached cell values and never recalculates. Nothing in the workbook changes. The hub gained three additive read-only
members for it: `Worksheet.LayoutMarkup` (the sheet part without its cells), `Worksheet.TryGetSharedStringIndex` with
`SharedStringTable.GetItemXml` (rich-text runs), and `Workbook.EvaluateFormula` (one formula on the stored values, for conditional formats).

One format handler per output format serves every kind that renders: `RenderingFormats.Register` dispatches on the file (document or
workbook), because the registry keeps one writer per format.

## What prints

- **Sheets:** every visible worksheet in tab order. Hidden and very hidden sheets print only with `IncludeHiddenSheets`. A chart or macro
  sheet is skipped with `sheet-layout.sheet-kind-skipped`. A sheet with nothing on it prints nothing; a workbook with nothing to print
  writes one blank Letter page (`sheet-layout.empty`).
- **Range:** `_xlnm.Print_Area` (several areas print one after another, each paginated by itself), else the used range from A1: the last
  cell with a value, or with a visible fill or border, merged ranges, and the cell after the last drawing anchor. A whole-row or
  whole-column area stops at the used cells.
- **Titles:** `_xlnm.Print_Titles` rows and columns repeat on every page that starts below (or right of) them. The pages that contain them
  print them in place.
- **Page setup** (`SheetPrintSettings.Read`): `paperSize` (the ECMA-376 §18.3.1.51 list; `paperWidth`/`paperHeight` override), `orientation`,
  `scale` (10–400), `fitToPage` with `fitToWidth`/`fitToHeight` (0 = as many as needed), `firstPageNumber` with `useFirstPageNumber`,
  `pageOrder`, `errors`, `blackAndWhite`; `pageMargins` with header and footer distances; `printOptions` centring, gridlines, headings;
  `sheetFormatPr`; manual `rowBreaks` and `colBreaks`.

## Pagination

Sizes are points before scaling. Available width = (paper − margins) / scale − heading width; likewise height.

1. **Column and row sizes.** The default font's maximum digit width is the widest digit advance at 96 dpi rounded to whole pixels (7 for
   Calibri 11). A stored width *w* is `trunc(((256·w + trunc(128/MDW)) / 256) · MDW)` pixels; 1 px = 0.75 pt. Without `defaultColWidth` the
   default column is `ceil((baseColWidth·MDW + 5) / 8) · 8` px (64 px for the Calibri default). Row heights come from `ht`; a row
   without one takes the default row height, or the height its tallest cell needs (font line height, times the lines of wrapped text,
   rotated text by its rotated extent). The default row is the font's ascent, descent and line gap each rounded to pixels plus 2 px (20 px,
   15 pt for Calibri 11). Hidden rows and columns have size 0 and take no room. A page that would hold only hidden lines is dropped.
2. **Scale.** `scale / 100`, or with fit-to-page the largest whole percent (at most 100, at least 10) at which every area fits the requested
   pages across and down. Manual breaks are ignored with fit-to-page.
3. **Breaks.** Lines are packed until the next one would not fit (a page always holds at least one). A manual break `id` is the last row
   (column) of its page (§18.3.1.1). Titles take room on the pages that repeat them.
4. **Order and numbers.** Down-then-over or over-then-down. Page numbers run on through the workbook; a sheet with `useFirstPageNumber`
   restarts at its `firstPageNumber`. `&N` is the larger of the page count and the highest number.
5. **Position.** The range sits at the left and top margins, or centred between them. Headings, when printed, add a heading column and row
   (digits of the last row number plus padding wide; one default row high) inside the scaled content.

## Cells

- **Text.** Numbers go through the shared number-format service (`NumberFormat.TryFormat`): the section colour overrides the font colour,
  `*c` fill is expanded to the cell width, `_x` spacing comes from the service. General fits the number to the column by trying fewer
  digits and scientific form, down to a single digit. A number that still does not fit, or any other format that is too wide, prints `#`
  repeated to fill the width (`####`). Text with a text format goes through the format's text section. Booleans print `TRUE` or `FALSE`;
  errors print as stored, or blank, `--`, `#N/A` per `pageSetup/@errors`. Rich text (shared string runs, §18.4) keeps each run's font,
  size, bold, italic, underline, strike, colour and super/subscript.
- **Shaping and wrapping** use OneFont: clusters shaped per run and bidi level, line breaks at UAX #14 opportunities (a word wider than
  the line breaks between characters), the visual order of each line by UAX #9 rule L2. Left-to-right text in a right-to-left reading order
  and right-to-left cells align right by default.
- **Alignment.** General: text left, numbers right, booleans and errors centred. Left, centre, right, fill (the text repeated across the
  width), justify and distributed (spread over the spaces of wrapped lines; the last justified line stays left), centre-across-selection
  (over the empty cells to the right with the same setting). Indent is MDW·0.75 pt per level. Vertical: bottom (default), top, centre,
  justify and distributed (as top). Cell padding is 2 px left and right, 1 px top and bottom.
- **Wrap, shrink, rotation.** Wrap wraps at the cell width less padding and indent. Shrink-to-fit scales the font size so one line fits.
  `textRotation` 1–90 is counter-clockwise, 91–180 clockwise (value − 90), 255 stacks the characters; rotated text is placed by the
  bounding box of the rotated block and clipped to the cell. Rotated 90° text wraps at the cell height.
- **Overflow.** Unwrapped, non-numeric text that is wider than its cell spills into empty neighbouring cells (no value, not merged): to
  the right when left-aligned, to the left when right-aligned, both ways when centred. It stops at the first occupied cell and at the page.
  Text that cannot spill is clipped to its cell.
- **Merged cells** draw once, from the first of their cells on the page, over the whole range (clipped by the page). Only the range's outer
  edges carry borders.
- **Fills.** Solid colours; a pattern fill is its average colour (pattern colour over background at the pattern's ink density); gradient
  fills are a linear gradient at `degree` (a path gradient is its last stop colour). A differential fill's colour is its `bgColor` when the
  pattern is solid or absent, as Excel writes it.
- **Borders.** All 13 styles: widths 1, 2, 3 px for thin/medium/thick, hairline half a pixel, double as two 1 px lines with a 1 px gap,
  dash patterns in units of the line width (dotted 1:1, dashed 3:1, dash-dot 3:1:1:1, dash-dot-dot 3:1:1:1:1:1). Where two cells share
  an edge the heavier line wins (order: hair, dotted, dash-dot-dot, dash-dot, dashed, thin, then the medium variants, medium, double, thick); at
  equal weight the cell to the right or below wins. Diagonals run corner to corner of the cell or merged range.
- **Gridlines** (`printOptions/@gridLines`) are 0.5 pt grey lines under fills and borders. **Headings** are grey boxes with the row
  numbers and column letters, drawn as artifacts.
- **Black and white** (`pageSetup/@blackAndWhite`) paints every colour as its grey level.

## Conditional formats

Evaluated on stored values through `Workbook.EvaluateFormula` (relative references move from the top-left cell of the first range), in
priority order, honouring `stopIfTrue`; for properties a higher-priority rule has set, a lower one does not change them.

| Rule | Rendering |
|---|---|
| `cellIs`, `expression`, `containsText`, `notContainsText`, `beginsWith`, `endsWith`, blanks, errors | the rule's `dxf`: font bold, italic, strike, underline, colour; fill; borders; number format |
| `top10`, `aboveAverage` (incl. `stdDev`, `equalAverage`), `duplicateValues`, `uniqueValues` | as above, from the statistics of the rule's cells (ties with the k-th value are included) |
| `timePeriod` | as above, against `SheetRenderOptions.Clock` (weeks run Sunday to Saturday) |
| `colorScale` | 2 or 3 colours interpolated in RGB between the points (`min`, `max`, `num`, `percent`, `percentile`, `formula`) |
| `dataBar` | a bar from `minLength` to `maxLength` percent of the cell width (10–90 for 2007-style bars, 0–100 with the x14 extension), gradient fading to a light tint at the tip unless `gradient="0"`. With the x14 extension and negative values: an axis where zero falls, positive bars to the right, negative bars (default red, or the positive colour) to the left, optional border |
| `iconSet` | an icon at the left of the cell, drawn as filled paths |

**Icon shapes.** Each set lists icons from the lowest level to the highest: arrows (up, down, right, and 45° up-right and down-right) in red,
yellow, green or grey; flags; traffic lights (circles, or lights in a dark rounded box); signs (red diamond, yellow warning triangle, green
circle); symbols (cross, exclamation, check, in a coloured disc, or bare in the `…Symbols2` set); stars (empty, half, full); triangles and
a dash; ratings (ascending bars, filled up to the level); quarters (a disc with 0–4 quarters filled); boxes (a square filled from the
bottom in quarters); `4RedToBlack` and `4TrafficLights` discs. Colours are approximations of Excel's: red `E0443E`, yellow `F5B623`,
green `57A84A`, grey `8C8C8C`. Level *i* is the highest whose threshold the value reaches (the first point is the start of level 0).
`showValue="0"` hides the cell's text. `reverse` flips the levels.

## Headers and footers

`headerFooter` strings with `&L`, `&C`, `&R` sections; `&P`, `&N` (with `+n`/`-n`), `&D`, `&T`, `&F`, `&Z`, `&A`; `&G` (a note
`sheet-layout.header-picture` and nothing drawn); `&"font,style"`, `&nn`, `&K` colour, `&B &I &U &E &S &X &Y` toggles, `&&`. Odd, even and
first-page strings follow `differentOddEven` and `differentFirst`. The font is the workbook's default font; text scales with the page when
`scaleWithDoc` is on (the default). The header starts at the header margin and grows down; the footer ends at the footer margin and grows
up. Date and time come from `SheetRenderOptions.Clock`; `&F` is the workbook's file name (or `FileName`).

## Tagging

Each page's text is one `Table`; a row is a `TableRow`, a cell a `TableDataCell` (a `TableHeaderCell` in the print-title rows and in the
header rows of `tableParts` tables), with column and row spans for merges. Rows and columns repeated from an earlier page, headings,
headers and footers, fills, borders, gridlines, data bars and icons are artifacts. Only cells that print text are tagged.

## Drawing objects (hook for the drawing and chart painters)

Anchors in the sheet's drawing part (`twoCellAnchor`, `oneCellAnchor`) are resolved to boxes in page points (`SheetPage.Objects`:
kind picture, chart, shape, group, connector; name; `X`, `Y`, `Width`, `Height` after scaling). `SheetRenderOptions.ObjectPainter` is called
for each object on the page after the cells, with the page's display list (no transform active, pixels = points · 4/3) and the box in pixels.
`ObjectPlaceholders` draws a thin outline where an object sits. Absolute anchors are not placed yet.

## Notes

`SheetLayoutDocument.Notes` (`SheetRenderNote`): `sheet-layout.sheet-kind-skipped`, `sheet-layout.empty`, `sheet-layout.page-limit`,
`sheet-layout.font-missing`, `sheet-layout.header-picture`, `sheet-layout.drawing-unreadable`, `sheet-layout.conditional-limit`
(past 3 million cells evaluated). `FontNotes` lists every replaced family as for documents.

## Points to settle against Excel

These are choices where the specification is silent; the generated `x*` workbooks in the layout differential carry each one, and Excel's
own PDF decides.

| Point | What is done |
|---|---|
| Default row height formula (`round(asc)+round(desc)+round(gap)+2` px) | matches Calibri 11 (20 px) and Arial 10 (17 px) from memory; other fonts unchecked |
| Page numbers across sheets | continue (Auto) unless `useFirstPageNumber` |
| Title rows on the first page when the area starts below them | repeated |
| Font codes in a header string carry over into later sections | yes (a stream) |
| Header and footer left edge with `alignWithMargins="0"` | min(margin, 0.5 inch) |
| Printed gridline colour and weight | 0.5 pt, `808080` |
| Border conflict at equal weight | the right/lower cell wins |
| Indent unit | MDW · 0.75 pt per level |
| Pattern fills | average colour |
| Icon colours and shapes | as above |
| Data bar negative axis position and length rule | zero's position between min and max; max length applies to the longer side |
| Chart sheets, absolute anchors | not printed / not placed |
