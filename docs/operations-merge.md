# Merge, append, split and field update

Operations that combine documents, cut them apart, and store layout-time field results in the file. They edit the hub (the markup of the
parts they change); parts they do not change stay as stored, and edited parts keep their unknown markup, prefixes, `mc:` blocks and
`extLst`. Every operation returns what it did: counts, each conflict with its resolution, a loss report, and the things it could not do as
defects with a code and a location (`MergeReport`, in `RecursiveAI.OneDocument.Operations`). Output is deterministic: new ids come from the
largest id in use plus one, never from a clock or a random source. Codes are in `defects.md` (`merge.`).

## Documents: `Document.Append`, `Document.Insert`

`dest.Append(source, options)` copies the body of `source` (or the range `SourceStart`/`SourceCount` of its body blocks) to the end of `dest`;
`dest.Insert(index, source, options)` puts it before body block `index`. A **block** is an element directly in `w:body` other than the final
`w:sectPr`: a paragraph, table or content control. The source is only read and may be `dest` itself. A Strict document cannot be combined with
a Transitional one (`OfficeUnsupportedException`).

All content is moved as markup with the namespace declarations it needs (`MarkupFragment` in the core package); the rules below rewrite what
would collide.

| What | Rule | Report |
|---|---|---|
| Styles | Matched by name and type (case-insensitive), not by id. An equal definition (ignoring prefixes, white space, `rsid*` and the style id) is reused with no report. A different definition is settled by `MergeOptions.Styles`: `KeepDestination` (default: the copied text takes the destination's), `UseSource` (the destination's definition is replaced: existing text restyles), `KeepBoth` (the source's style is added as `name (2)`). A style the destination lacks is copied with its `basedOn`/`next`/`link` chain; its id gets a number if the id is taken by another style. `w:default` is not copied. | `merge.style-conflict` (item = style name, resolution = `kept destination` / `used source` / `kept both`); count `styles` |
| Lists | A paragraph's `w:numId` names a `w:num`, which names a `w:abstractNum`. An abstract definition equal to one in the destination (ignoring `nsid`, `tmpl`, ids) is not duplicated. `NumberingContinuation.Continue` (default) joins a destination list that has the same definition and the same level overrides; `Restart` makes a new `w:num` that starts every level again (`w:startOverride`). A definition the destination lacks is copied with new ids (`nsid` is dropped). | `merge.list-continued`, `merge.list-restarted`; count `numbering` |
| Sections | `UseDestination` (default): the copied content continues the destination's section; sections that end inside the copied range keep their own page setup. `KeepSource` (when the range reaches the end of the source): the destination's governing section ends in a section-break paragraph before the copied content, and the source's last section (headers and footers copied) describes it. | loss `merge.section-properties` when the source's last section differs and was not kept; count `sections`, `headers`, `footers` |
| Headers and footers | The part is copied with its own relationships when a copied `w:sectPr` refers to it. | |
| Notes and comments | The notes and comments the copied content refers to are copied into the destination's parts (created when missing) with new ids; separators are not copied. A reference whose note is missing in the source is left out. | counts `footnotes`, `endnotes`, `comments`; `merge.note-missing`, `merge.comment-missing`; loss `merge.comment-threading` (replies and resolved state are not carried: they hang on `w14:paraId`, which is dropped to keep paragraph ids unique) |
| Bookmarks | A name the destination already has is renamed `name_2`, `name_3` … (the copied bookmark and every `hyperlink@anchor`, `REF`, `PAGEREF`, `NOTEREF` and `HYPERLINK \l` in the copied content that names it). Ids are renumbered. | `merge.bookmark-renamed` |
| Revisions | Every `w:ins`, `w:del`, move, property-change and range element gets a new id; authors and dates are kept. | `merge.revision-author` for an author the destination did not have; count `revisions` |
| Content controls, drawings | `w:sdtPr/w:id` and `wp:docPr/@id` are renumbered. A data binding to the source's custom XML is dropped. | loss `merge.data-binding` |
| Related parts | Every attribute in the relationships namespace (`r:id`, `r:embed`, `r:link` …) is resolved in the source part's relationships; the target part is copied (pictures with equal bytes and content type are stored once; a part with its own relationships is copied with its graph) and related to the destination part under a new id. External targets are related as they are. | counts `images`, `parts`, `parts-shared`, `external-links`; `merge.relationship-missing`, `merge.part-missing` |
| Fonts, theme, document defaults | The destination's win. | loss `merge.fonts` (count of fonts the font table lacks), `merge.theme`, `merge.doc-defaults` |
| `mc:Ignorable` namespaces | A namespace the copied content uses and the source root lists as ignorable is declared on the destination root and added to its `mc:Ignorable`. A prefix the destination root binds to another namespace is a problem. | `merge.namespace-prefix-clash` |
| Lists missing in the source | The reference is kept as written. | `merge.numbering-missing` |

The hub's cached lookups (styles, notes, comments, revisions) are dropped after the operation; objects obtained before it should be fetched again.

## Documents: split

`SplitBySections()`, `SplitByHeading(level)` and `SplitBefore(blockIndices)` return a `SplitResult` of `DocumentPiece`s (a `Document`, the first
block and block count in the original, a title). `DocumentPageSplit.SplitByPage(pagesPerPiece, options)` in the rendering package finds the
breaks from the layout tree. A piece can only begin at a body block; when a block runs over a page boundary the break moves to the next block start.
A table of contents or index with no stored result counts as empty for the page split (run `UpdateFields` first).

Each piece is the original saved to memory and cut down: the body blocks outside the range are removed, the section that covers the end of the
range becomes the body's last section, and a section that inherited its headers and footers from an earlier one gets the references written
into it. Then the piece is pruned, so that it is a standalone file with only what it uses:

- styles (the used ones, the document defaults, and what they refer to through `basedOn`, `next`, `link`, list levels, and style names that field
  codes mention; all `heading N` styles when a `TOC` field is present);
- list definitions and instances;
- footnotes, endnotes and comments that nothing refers to (separators stay), and the modern comment parts' entries for them;
- relationships of pictures, links, headers, footers, charts, diagrams and objects that nothing in the part refers to;
- every part that no chain of relationships from the package root reaches (`OpcGraph.RemoveUnreachableParts`).

The original is not changed. A heading is a paragraph whose outline level (on the paragraph or its style chain) or built-in style name
(`heading 1` … `heading 9`) says so. Statistics in `docProps/app.xml` are not rewritten.

## Fields: `Document.UpdateFields()`

In the rendering package (`DocumentFieldUpdate.UpdateFields`). It rebuilds the table of contents, index and table of authorities in the
document (`FieldUpdater`, the same builder the layout uses in its private copy), lays the document out with all fields updated, and writes each
computed result into its field with `Field.SetResult` (the first result run keeps its formatting; the other result runs of the field go). Fields
in headers, footers and notes are written too. `w:updateFields` is removed from the settings. The layout results are those of the last pass of
`docs/layout-fields.md`, so laying the saved file out again moves no line (tested against the layout of the original with `UpdateFields` on).

- A field in a header or footer that shows a different result on different pages (`PAGE`) keeps its stored result: `FieldUpdateSkip` code
  `fields.per-page`. `NUMPAGES`, `SECTIONPAGES` and the like in a footer have one result and are written.
- `w:fldLock` fields are never changed. `DATE` and `TIME` change only when `LayoutOptions.Clock` is given.
- Index and authorities entries hold their page numbers as plain text, and the `ODXEPAGES` helper fields and `_OdXe` bookmarks the build uses are
  removed again.
- Known limit: an `INDEX \c` (columns) rebuilt a second time loses the section breaks that give it its columns; the first update is right.
- Rebuilding a table of contents whose old result ended in a paragraph of its own moves the field end into the last entry, so repeated updates
  do not add an empty line each time.

## Workbooks: `Worksheet.CopyTo`, `Worksheet.MoveTo`, `Workbook.SplitBySheet`

`sheet.CopyTo(workbook, options)` copies a worksheet within or between workbooks. It copies the cells with their **cached values** (nothing is
recalculated and the destination's `fullCalcOnLoad` is not set), formulas, styles, row and column settings, merged cells, tables, conditional
formats, data validations, `sheetPr`/`sheetViews`/`sheetFormatPr`, print and page settings, sheet-scoped names (including `_xlnm.` built-ins) and
hyperlinks.

- **Styles** are carried by value: each cell format is resolved in the source and interned in the destination's style sheet. `dxf` formats of
  conditional formats are added to the destination. Date serials are shifted by 1462 days when the two workbooks use different date systems
  (`merge.date-system`).
- **Name** of the copy: the source name, or `Name (2)`, `Name (3)` … when taken (`merge.sheet-renamed`).
- **Formulas** are parsed with the formula parser, rewritten with `FormulaRewriter` and printed back; a formula that does not parse is copied as
  written (`merge.formula-unreadable`). References to the sheet itself follow its new name. Between workbooks, references to **other sheets of the
  source workbook** become references to an external link to the source (`[1]Other!A1`), whose part lists the sheets and caches the cells the copy
  reads (areas up to 4,096 cells), so the copy still calculates to the same values; the link target is `SheetCopyOptions.SourceLinkTarget`, else
  the source's file name, else `Source.xlsx` (loss `merge.external-references`). The source's own external links are copied whole.
- **Names**: a workbook-level name a copied formula uses is added to the destination (its formula rewritten the same way). If the destination
  already has the name with another definition it is kept and reported (`merge.name-conflict`); the copied formulas then use the destination's.
- **Tables** are added with their range, header and style under a free name (`Items_2`; `merge.table-renamed`). Totals rows and calculated column
  formulas are not carried (loss `merge.table-detail`).
- **Not copied**: drawings, charts, pictures, comments, pivot tables, slicers and every other part related to the sheet (one
  `merge.sheet-part-not-copied` problem per kind, located at the sheet part), and the sheet's `extLst` (loss `merge.sheet-extensions`).
- `MoveTo` copies and then removes the sheet; formulas in the sheets left behind that referred to it are not re-pointed and are counted
  (`merge.references-left`).
- `SplitBySheet()` makes one new workbook per worksheet with `CopyTo`; each piece has its own report.

## Presentations: `Presentation.InsertSlides`, `ExtractSlides`, `SplitEvery`

`deck.InsertSlides(slides, index, options)` copies slides of one other presentation (of this one, it duplicates).

- **Formatting**: `SlideFormatting.KeepSource` (default) keeps each slide's master, layouts and theme. An identical master in the destination (same
  master markup, theme bytes and layouts in order, apart from the ids) is reused (`merge.master-reused`); otherwise the master, **all** its
  layouts and its theme are copied, with new master and layout ids above every id in use (`merge.master-copied`; counts `masters`, `layouts`).
  `UseDestination` maps each slide's layout to the destination layout with the same name, else the same type, else the first (`merge.layout-mapped`).
- The slide part and the parts only it uses are copied: pictures by content, charts with their embedded workbooks, diagrams, objects, tags. The
  notes page is copied when the destination has a notes master and dropped otherwise (loss `merge.notes-dropped`); comments are not copied
  (`merge.comments-dropped`); a link to another slide points at the slide itself. `p14:creationId` is replaced as `DuplicateSlide` does. New
  slides join the section of the slide before them.
- Shapes are not scaled when slide sizes differ (loss `merge.slide-size`).
- `ExtractSlides(start, count)` saves the presentation to memory, deletes the other slides (`DeleteSlide` removes what only they used), removes the
  masters no kept slide uses and what only they reached. `SplitEvery(n)` calls it for each run of `n` slides.

## Checks

Tests: `tests/OneDocument.Documents.Tests/MergeTests.cs`, `SplitTests.cs`, `tests/OneDocument.Rendering.Tests/UpdateFieldsTests.cs`,
`PageSplitTests.cs`, `tests/OneDocument.Spreadsheets.Tests/SheetCopyTests.cs`, `tests/OneDocument.Presentations.Tests/SlideInsertTests.cs`.
`tools/OperationsProbe` runs the operations over a directory and writes the outputs for the schema validator and LibreOffice
(`docx-merge`, `docx-split`, `docx-fields`, `xlsx-copy`, `pptx-insert`, `pptx-split`). The comparison with the Office applications is listed in
`testdata/operations/ORACLE-REQUEST.md` and has not been run.

## Not done

- Comment threads and the resolved state in a merge; picture bullets (`w:numPicBullet`) in copied lists; `w:customXml` stores of the source.
- Charts, pictures, comments and pivot tables on a copied sheet.
- Presentation notes master and handout master copying; comments on inserted slides; custom shows.
- A second rebuild of an `INDEX \c` (see above).
