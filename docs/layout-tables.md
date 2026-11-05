# Tables in layout

How `LayoutEngine` lays out and paints Word tables. It builds on the layout tree and paint contract in `layout.md` and on the logical
table model in `docx-tables-sections.md` (grid, spans, vertical merges, resolved table-style regions). References are to ECMA-376
Part 1 unless noted. Items marked **to check against Word** are choices the specification leaves open; they are settled by comparing
with Word's own output and are listed at the end.

## Tree and paint

`LayoutPage` gains `Tables`; a table is `LayoutTable` > `LayoutTableRow` > `LayoutTableCell` > blocks (`LayoutParagraph`, or a nested
`LayoutTable`). Positions are points on the page, as everywhere else.

- A table that breaks across pages has one `LayoutTable` per page (`ContinuesFromPreviousPage`/`ContinuesOnNextPage`). A row that
  splits has one `LayoutTableRow` per page with the same `Index`; its cells are split the same way.
- Header rows repeated at the top of a continuation page are `LayoutTableRow`s with `IsRepeatedHeader`.
- A cell's box covers every row of a vertical merge that sits on the page. It is a child of the row where it starts.
- Paragraphs inside cells are the same `LayoutParagraph` nodes as on the page, with real positions, so text extraction and
  the tagged output work unchanged.

Paint order: each cell's shading, then its content, then all borders. Tags: `Table` > `TR` > `TH`/`TD` (with column and row span)
> content. A cell is `TH` when its row is a header row (`w:tblHeader`). Repeated header rows are artifacts. Shading and borders are
plain fills (artifacts).

## Column widths (§17.4.53 tblLayout, §17.4.64 tblW, §17.4.42 tcW)

Preferred widths: `dxa` is twentieths of a point, `pct` is fiftieths of a percent (or a `50%` string); `auto`, `nil` and a width of
0 leave the width to the algorithm. A percentage is of the table's stated width when it has one, else of the space available (the
text width, or the inner width of the cell that holds the table). With cell spacing, the spacing between and around the columns is
taken off the width first.

**Fixed layout** (`w:tblLayout w:type="fixed"`): contents never change a width.
1. A column takes the preferred width of the first single-column cell that states one (tcW wins over `w:tblGrid`).
2. A spanning cell that states a width gives it to its columns, in proportion to the grid, when none of them has its own.
3. A column nobody states takes its grid width.
4. A table that states a width but no cell does scales its grid to that width. Otherwise the table is as wide as its columns.

**Autofit** (the default): contents decide inside the preferred widths.
1. For each cell the narrowest width (the longest stretch with no break opportunity, plus the cell's padding), the widest (the
   longest line with no wrapping, plus padding) and the preferred width. `w:noWrap` makes narrowest equal widest. A nested table
   contributes its own narrowest and widest.
2. A spanning cell raises its columns' totals when they fall short, spreading the extra in proportion to the columns' widest widths.
3. A column's target is its preferred width (not below its narrowest) when it has one, else its widest width, and a column no cell
   covers keeps its grid width.
4. The limit is the stated table width, else the space available. Over the limit, columns that state no width shrink first (towards
   their narrowest, in proportion to what they have above it), then the ones that do. If even the narrowest widths exceed the limit,
   the table is wider than its space and runs past the margin.
5. Under a stated limit, the extra goes to the columns that state no width, in proportion to their size (to all columns when every
   one states a width). A table with no stated width is as wide as the targets.

## Geometry

- **Cell padding** is the cell margin (`w:tcMar` over `w:tblCellMar`, default 5.4 pt left and right, 0 top and bottom, §17.4.43) plus
  half the width of the cell's border on that side; with cell spacing (`w:tblCellSpacing`, §17.4.44) the whole width, because the
  border then sits inside the cell box.
- **Cell spacing** puts a gap between cells and around the table; each cell paints all four of its borders.
- **Position** (§17.4.28 jc, §17.4.51 tblInd): left uses the indent; center and right place the table in the available width and
  ignore the indent. In compatibility modes up to 14 the table is pulled out by its left cell margin so the text of the first column
  lines up with the margin; mode 15 puts the table's edge at the indent. `w:bidiVisual` mirrors the columns.
- **Row height** (§17.4.81 trHeight): a row is as tall as its tallest cell (padding, content and the paragraph spacing after the last
  paragraph). `atLeast` (the default) is a floor, `exact` fixes the height and cuts the content (whole lines only), `auto` ignores
  the value.
- **Vertical merge:** a merged cell's content is measured over its whole region; if the rows fall short, the last row of the region
  grows. Content is laid from the top of the region.
- **Vertical alignment** (§17.4.84): top (also `both`), center and bottom place the content in the cell box when it is not split.
- **Cell content** is laid out as on a page at the cell's inner width; nested tables are laid out recursively (limit: 6 levels, then
  the table is left out with `layout.table-depth`). Space before the first paragraph and after the last count.
- **Floating tables** (`w:tblpPr`, §17.4.57) are painted at their position (`tblpX`/`tblpY` or the alignment keywords, relative to
  page, margin or text) without text wrapping around them, take no room in the flow and do not split (`layout.table-floating`).
- **Text direction** in cells other than left-to-right is laid out horizontally (`layout.table-text-direction`).

## Borders (§17.4.38 tblBorders, §17.4.66 tcBorders, §17.3.4 border)

Each edge of a cell comes from its `tcBorders` (a border set on the cell), else the table's: outer borders at the table's edge,
`insideH`/`insideV` between cells. `start`/`end` mean left/right (reversed in a right-to-left table). A border of style `none` or `nil`
is a border of weight 0 that still counts as set.

When two cells share an edge (no cell spacing), one border wins:
1. a border set on a cell beats one that comes from the table;
2. between two of the same origin, the heavier: width times the number of lines (double and the thin-thick styles count 2, triple 3);
3. then the style later in the precedence list (`single`, `thick`, `double`, `dotted`, `dashed`, ...);
4. then the darker colour, by R + B + 2G.

Both cells then hold the same resolved border, and the right and bottom edges are painted by the cell only when no cell lies beyond
them. Collapsed borders are centred on the cell edge and meet at the corners; double and triple styles draw alternate strips,
dotted and dashed draw segments, every other style is drawn solid.

## Shading (§17.3.4 shd, §17.18.78)

`clear` shows the fill, `solid` the colour, `pctN` mixes N percent of the colour into the fill; stripe and trellis patterns are
approximated as `pct50`. The cell's own shading beats the table style's, which beats the table's.

## Pagination

- Rows go onto the page in order. Vertically merged rows are a **group** that moves together when it fits on a page.
- **Row splitting:** a row that does not fit splits between lines (or nested rows) of its cells, each cell taking what fits; the rest
  continues on the next page. Space before at the top of the continuation is dropped. Widow and orphan control is not applied inside
  cells (**to check against Word**).
- **`w:cantSplit`** (§17.4.6) and exact-height rows move whole to the next page when they would split and fit on a page. A row
  taller than a page splits anyway.
- **Header rows** (`w:tblHeader`, §17.4.49): the rows at the top of the table that carry it repeat at the top of every later page, as
  long as together they take at most half the page. Header rows are not repeated inside nested tables.
- A group of merged rows taller than a page breaks between rows; the merged cell's content continues across the break.
- Space after the paragraph before a table is kept; the table is not kept with the next paragraph.

## Open points (to check against Word)

| Point | What is done | Why open |
|---|---|---|
| Autofit algorithm | the steps above | the specification names no algorithm; checked against Word's widths at the layout differential |
| Fixed layout: tcW or `w:tblGrid` | tcW wins | Word may rebuild the grid from the first row |
| Half a border in the cell padding | half of each side | Word's exact geometry for collapsed borders |
| Edge pulled out by the left margin | modes up to 14 | observed behaviour, per mode, not specified |
| Widow control inside cells, keep-with-next into tables | not applied | needs Word output |
| Rows with only merged-continuation cells | their covered cells' paragraphs add no height | Word counts the empty paragraph |
| Repeated header with merged cells that reach below the header | the merged cell is drawn clipped to the header rows | rare |
| Floating tables | painted without wrap; do not split | wrapping is the floats stage |
| Text direction `btLr`/`tbRl` | horizontal | basics only |
| Nested table width from a percentage while measuring | ignored (measured by content) | |
