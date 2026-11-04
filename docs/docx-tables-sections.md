# WordprocessingML tables, sections, headers and footers

How the document hub reads and edits them. References are to ECMA-376 Part 1 (5th edition) unless noted. Items marked
**to check against Word** are what the hub does today where the text leaves the answer open.

## Tables (§17.4)

- **Rows and cells** are enumerated through content-control and custom-XML wrappers, so nothing is hidden. A cell is a block
  container (`TableCell`); `w:tcPr` is not a block.
- **Logical grid** (`Table.Logical()`): rows × grid columns. A cell starts at the running column (after `w:gridBefore`) and covers
  `w:gridSpan` columns (below 1 or above 16384 reads as 1). The grid width is the larger of `w:tblGrid` and the widest row, so rows
  wider than the grid widen it (`docx.table-grid-mismatch`).
- **Vertical merge** (§17.4.85): `w:vMerge` with `w:val="restart"` starts a region; with `continue` or no value it continues the cell
  directly above in the same starting grid column. A continuation with nothing mergeable above it (first row, or the cell above is
  not part of a merge) is an orphan: it reads as a plain cell (`docx.table-merge-orphan`). **To check against Word:** Word may merge a
  continuation upward into an unmerged cell above instead.
- **Horizontal merge**: current files use `w:gridSpan`. The legacy `w:hMerge` (restart/continue) widens the cell to its left; an
  orphan continuation is a plain cell.
- A region is one `LogicalCell` anchored at its top-left cell; the swallowed physical cells are in `Covered`. Empty grid positions
  (`gridBefore`, `gridAfter`, short rows) read as null.
- **Table styles**: `Table.Look` reads `w:tblLook` (attributes or the hex mask). `StyleSheet.GetTableRegions` names the conditional
  regions of a logical cell in application order (§17.7.6); the header row is not banded and banding counts from the row after it. A
  merged cell counts by its top-left position for banding and as last row/column when it reaches the last one.
  `ResolveTableCell` layers style table and cell properties, regions, the table's own properties and the cell's own; borders and
  margins are flattened per side (`tblBorders/top`, `tcBorders/left`, `tblCellMar/start`). Region membership by logical column
  differs from the paragraph resolver's physical cell index only in rows with merged cells. **To check against Word.**

### Edits

Edits change only the table's markup; properties, unknown markup and untouched rows stay byte-identical, and only the part that holds
the table is rewritten.

- `InsertRow(index)` copies the row above (the first row at the top), clears the cells to one empty paragraph that keeps the first
  paragraph's properties, removes `w:ins`/`w:del`/`w:trPrChange` from the copy and the paragraph ids. A new cell continues a vertical
  merge exactly when the cells above and below belong to one region.
- `DeleteRow(index)` makes the next row's cell the start of a region that the removed row started. The only row cannot be deleted.
- `InsertColumn(column)` widens cells that span the insertion point, adds a plain empty cell elsewhere (copying the neighbour's
  width) and a `w:gridCol` of the left neighbour's width. Inside `gridBefore`/`gridAfter` the counts change instead.
- `DeleteColumn(column)` narrows spanning cells, removes cells that cover only that column, and removes the `w:gridCol`. The only
  column cannot be deleted.
- `SetCellText` keeps the first paragraph's properties and its first run's formatting and removes every other block of the cell.

## Sections (§17.6)

- A section ends at the paragraph whose `w:pPr` holds a `w:sectPr` (top level or in a block content control); the last section's
  `w:sectPr` closes the body. A body without one has one section of defaults.
- Defaults when attributes are absent: Letter portrait (12240 × 15840), margins 1440, header and footer distance 720, gutter 0, one
  column with 720 space, `w:type` next page. An unusable `w:pgSz` reads as Letter (`docx.section-page-size-invalid`).
- Columns: for unequal layouts the `w:col` list defines the count (`docx.section-columns-mismatch` when `w:num` differs).
- Page numbering, line numbering, document grid, `w:titlePg`, `w:bidi`, `w:vAlign`, `w:textDirection`, `w:noEndnote` are read as data.
  Setters exist for page size, margins, break type, title page, page numbering and equal columns; they respect the schema sequence and
  create the body's `w:sectPr` when missing.

## Headers and footers (§17.10.5)

- A reference is `w:headerReference`/`w:footerReference` with `w:type` `default`, `first` or `even` and an `r:id` of the main part.
  A reference that does not lead to a part is ignored (`docx.header-missing`); a second of the same kind and type is ignored
  (`docx.header-reference-duplicate`).
- **Linked to previous**: a section without its own reference of a kind and type uses the nearest earlier section's. The first
  section without one prints blank. There is no fallback between types: a missing `first` or `even` story is blank (or inherited
  for that type), never the default story.
- **Page choice** (`Section.ForPage`): with `w:titlePg`, the first page of the section uses `first`; else with
  `w:evenAndOddHeaders` (settings part), an even printed page number uses `even`; else `default`.
- **To check against Word:** a section with `w:titlePg` and no `first` reference of its own, after a section that has one, inherits it
  (the hub), while LibreOffice prints the default header on that page.
- `Section.AddOwn` creates `/word/headerN.xml` (or footer) with one empty paragraph, the relationship and the reference.
  `Document.EvenAndOddHeaders` writes `w:evenAndOddHeaders` into settings, creating the part when needed.
