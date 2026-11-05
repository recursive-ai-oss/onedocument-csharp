# Charts

How OneDocument reads, lays out and paints charts. Namespace `RecursiveAI.OneDocument.Charts` in `RecursiveAI.OneDocument.Rendering`
(`src/OneDocument.Rendering/Charts/`). References are to ECMA-376 Part 1 §21.2 (DrawingML charts) and [MS-ODRAWXML] (chartex) unless
noted.

## Liftable by design

The namespace depends on three things only: its own model, the markup layer (`RecursiveAI.OneDocument.Markup`, itself free of
other OneDocument code) and the display list and OneFont. `ChartHost.cs` is the one file that touches the rest of OneDocument
(packages, the font library, the number-format service, workbook cells). Text and number formatting reach the painter through two
small interfaces, `IChartFonts` and `IChartNumberFormatter`. A project that wants the chart code takes the folder plus an adapter.

## Pipeline

`ChartReader.Read(bytes)` → `ChartSpace` (model) → `ChartPainter.Paint(displayList, chart, x, y, w, h, options)` → `ChartLayoutInfo`.
`ChartHost` wraps the common cases: `Load(package, sourcePart, relId)` for the chart a frame points at, `Fonts`, `Numbers`,
`WorkbookSource`, and `Paint`/`WritePdf`/`RenderPng` for a chart on its own. `ChartFrames.Find(part)` lists the chart references of a
slide, spreadsheet drawing or document part (`p:graphicFrame`, `xdr:graphicFrame`, `wp:inline`/`wp:anchor`) with their extent, name
and description, so each host paints the chart at its frame's box and uses the description as the figure's alternative text.

Painting is in CSS pixels (1 pt = 4/3 px) into the caller's display list, wrapped in one `Figure` tag whose alternative text is the
frame description, else the chart title, else the first series name. Strokes are filled outlines because the display list fills
only.

## Model

- **Classic** (`c:chartSpace`): bar/column (clustered, stacked, 100 %), line, area, pie, doughnut, `ofPie` (drawn as one pie, with a
  note), scatter, bubble, radar (standard, markers, filled), stock (high-low-close, open-high-low-close, up/down bars), surface
  (drawn flat as contour bands). The 3-D chart elements read as their 2-D types with the note `chart.3d-flattened`.
- **Chartex** (`cx:chartSpace`): waterfall (subtotals), sunburst, treemap, histogram, Pareto, box and whisker, funnel. A region map
  and any unknown layout read as a placeholder with `chart.region-map` / `chart.layout-unknown`.
- Values and categories come from the cached data (`numCache`, `strCache`, `multiLvlStrCache`, literals; chartex `lvl` points). Nothing
  is recalculated on read. `ChartRefresh.Refresh(chart, source)` replaces the caches from an `IChartDataSource`;
  `ChartHost.WorkbookSource(workbook)` reads cell values as stored.
- Axes (value, category, date, series) with min/max/major/minor units, log base, reverse order, crossing, tick marks, label
  position, skips, titles, gridlines and number format; titles, legends (position, overlay, deleted entries, manual layout), data
  labels, trendlines and error bars (basic), data points, markers, plot- and chart-area formatting, chart style number, theme override.
- `extLst` elements are kept as text in `ChartSpace.Extensions`; `ChartSpace.SourceBytes` is the part's original bytes, which is
  what a package writes back. The model's setters change the model only (there is no writer yet), so an unedited chart part stays
  byte-identical in a saved package.
- Errors: a part that is not well-formed XML raises the markup layer's `OfficeFormatException`; a root other than `chartSpace` raises
  `chart.not-a-chart`; values the reader cannot use raise `chart.malformed`. `TryRead` returns false instead.
- Notes (`ChartSpace.Notes`, `ChartLayoutInfo.Notes`): `chart.region-map`, `chart.layout-unknown`, `chart.group-skipped`,
  `chart.3d-flattened`, `chart.of-pie`, `chart.data-table`, `chart.surface-flattened`, `chart.font-missing`, `chart.placeholder`.

## Colours

Series colours are the theme's accents in order, and from the seventh series the accents darkened and lightened in fixed steps
(`ChartTheme.AutomaticSeriesColor`); Office varies the steps with the number of series, which is a known approximation. Colour
transforms (`lumMod`, `lumOff`, `satMod`, `tint`, `shade`, `alpha`) are applied in order. Pattern and picture fills draw as flat
colour. Text defaults to the 65 %-grey Office uses, gridlines to 15 % grey.

## Automatic value-axis scale

`AxisScaler.Compute` follows the model below; every number in it is a **hypothesis to settle against Excel**, not a verified rule.

1. Fixed `min`/`max`/`majorUnit` win. A logarithmic axis runs from `base^floor(log min)` to `base^ceil(log max)` with the base as unit.
2. 100 % stacked groups: 0 to 1 in steps of 0.1.
3. Otherwise the raw range comes from the data (running totals for stacked groups, 0 included):
   - all values ≥ 0: if `(max - min) / max` is more than 1/6 the minimum is 0, else `min - (max - min) / 2`; the maximum is
     `max + 5 % × (max - axisMin)`;
   - all values ≤ 0: the mirror image; mixed signs: 5 % of the data range is added at both ends;
   - one value: 0 to 1.05 × the value.
4. The major unit is the smallest of 1, 2 or 5 × 10^k that spans the raw range in at most 10 intervals; the minor unit is a fifth of it.
5. The minimum is floored and the maximum ceiled to a multiple of the major unit.

Worked cases: data 0–100 → 0–120 step 20; 0–10 → 0–12 step 2; 0–1 → 0–1.2 step 0.2; 6–22 → 0–25 step 5; 0–200 → 0–250 step 50.

Unsure points, all in the generated cases below: where exactly the 1/6 rule switches (`just-over-sixth`, `just-under-sixth`), whether
10 or 11 intervals are accepted (`range-101`, `range-99`), tiny and huge magnitudes, negative-only data, single values and all-zero
data, and the head-room for mixed signs.

`tools/ChartProbe gen DIR` writes one workbook per case (a clustered column chart of one series) and `scaling-cases.json` with our
`min`/`max`/`major` and an empty `excel` slot. Excel's answer for each (`Axes(xlValue).MinimumScale`, `MaximumScale`, `MajorUnit`) goes
into that slot at the comparison run; the committed copy is `testdata/charts/`. Until then the cases are unanswered.

## Layout

Order of work: chart area, title, legend, then the plot.

- **Title** at the top, centred, 1.4 × the base size (14 pt from a 10 pt base); a manual layout places it from the chart's top left.
- **Legend** right, left, top, bottom or top-right; entries per series (per category for pie, doughnut and single-series vary-colours
  charts; Increase/Decrease/Total for waterfall); a manual layout or overlay takes no space.
- **Plot** for axis charts: value labels, tick length and axis titles are measured and subtracted; category labels are laid out for
  the slot size (horizontal if they fit, wrapped at spaces in up to three lines, rotated 45° or 90°, else every n-th label). A manual
  inner layout sets the plot directly. Secondary value axes sit on the opposite side; the Pareto line has its own 0–100 % axis.
- **Bars:** cluster width from gap width and overlap (`barWidth = slot / (n - (n-1) × overlap + gap)`); bars grow from the category
  axis crossing. **Lines/areas:** categories at slot centres (`crossBetween between`) or on the ticks (`midCat`); date axes place
  points by date. **Pie:** from `firstSliceAng` clockwise, explosion shrinks the radius to fit; best-fit labels go inside large slices.
- **Charts without axes:** pie, doughnut (one ring per series), radar, sunburst (rings), treemap (squarified, parent headers), funnel.
- Overlap avoidance is basic (label skipping and rotation only). Labels are not nudged apart.

## Known gaps

- Pattern/picture fills, gradient outlines, shadows and 3-D are not drawn. Surface charts are flat.
- Data tables, display units text, trendline equations and R², up/down-bar formatting, and per-label manual positions are not drawn.
- Chartex: region maps are a placeholder; waterfall and box-whisker connector and mean-line options are partial; sunburst and treemap
  label overflow is not handled; histogram bin labels use plain number formatting.
- Auto colours past six series use fixed steps.
- Text uses the substitute fonts; line breaks and widths differ from Office until the same fonts are installed.

## Checks

- Unit tests (`ChartTests`): every generated sample reads, keeps its bytes through a package round trip, and paints a tagged figure;
  scaling rows; frames; refresh; typed errors.
- `tools/ChartProbe sweep INPUT OUT [--render N]` reads every chart part of every document under INPUT and counts types.
- Comparison inputs, unanswered: the 38 generated chart workbooks and the scaling workbooks in `testdata/charts/` for Excel's PDF
  export and axis values, at the 1.0 gate.
