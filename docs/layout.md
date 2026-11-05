# Layout and rendering

How OneDocument turns a word-processing document into pages, and the contracts later layout stages build on. Package
`RecursiveAI.OneDocument.Rendering`, namespaces `RecursiveAI.OneDocument.Layout` (tree, engine, fonts) and
`RecursiveAI.OneDocument.Rendering` (paint, PDF and PNG output). References are to ECMA-376 Part 1 unless noted. Items marked
**to check against Word** are rules the specification leaves open; they are settled by comparing with Word's own output and are
listed in the deviations table at the end.

## The pipeline

`LayoutEngine.Layout(Document, LayoutOptions?)` → `LayoutDocument` (pages, font notes, notes, field passes) →
`LayoutPainter.Paint(LayoutPage)` → OneRender `DisplayList` → `DocumentRenderer.WritePdf` / `RenderPng`, or any OneRender backend.
OneDocument does its own text layout; OneRender's CSS engine is never involved. Layout reads the document and never changes it.
`Office.Convert("in.docx", "out.pdf")` and `document.SaveAs("out.pdf")` go through the same path once
`RenderingFormats.Register` has run (referencing the package does it by itself, as for the other kinds). A PNG written through
`SaveAs` is page 1; `DocumentRenderer.RenderPng(page)` renders any page at 144 dpi.

## The layout tree

All positions are points, origin at the top-left of the page, y down. The tree is immutable once layout returns and is safe to read
from several threads.

| Node | Holds |
|---|---|
| `LayoutPage` | number (1-based), size, `ContentArea`, `SectionIndex`, its blocks |
| `LayoutParagraph` | the hub `Paragraph`, `Text` (the visible text as laid out), `Label`, `HeadingLevel`, `IsRightToLeft`, `ContinuesFromPreviousPage` / `ContinuesOnNextPage` |
| `LayoutLine` | `Baseline`, `Ascent`, `Descent`, `TextStart`/`TextLength` into `LayoutParagraph.Text`, `Text`, `IsLastLine` |
| `LayoutRun` | `RunKind` (Text, Tab, Label), `Text`, `TextStart`, the hub `Run`, the chosen `FontMatch`, `FontSize`, `BidiLevel`, cluster offsets and x positions |

- A paragraph that crosses a page or column has one `LayoutParagraph` per page or column; each has the full `Text` and its lines carry offsets into it.
- `Text` has hidden text removed, `\t` for a tab, `\n` for a line break and `\f` for a page break and U+000E for a column break. Offsets are UTF-16 units.
- A line's runs are in visual left-to-right order. A line's `Text` is in logical order and includes the spaces that hang past the
  margin and a trailing line break.
- Later stages add blocks beside `LayoutParagraph` (tables, floating objects, notes) under the page, and fields re-run layout; the
  node classes are open for that through `LayoutNodeKind`. Every node knows its parent and children.
- `LayoutDocument.FontNotes` has one `FontSubstitutionNote` per replaced family and `Notes` has `layout.*` notes for what was skipped.
  Codes so far: `layout.font-missing`, `layout.drawing-skipped`, `layout.block-skipped`, `layout.blank-page`, `layout.page-limit`,
  `layout.page-border-art`, `layout.valign-both`, for fields `layout.fields-unsettled`, `layout.field-source-missing`, `layout.field-skipped`, `layout.noteref-page-restart` (see `layout-fields.md`), and for tables `layout.table-skipped` (a table in a header or footer),
  `layout.table-floating`, `layout.table-text-direction`, `layout.table-depth` (see `layout-tables.md`).

## Paint contract

`LayoutPainter.Paint(page, PaintOptions?)` returns a display list in CSS pixels (1 pt = 4/3 px) the size of the page.

| Source | Display list |
|---|---|
| paragraph | `BeginTag(Paragraph)`; headings `Heading1`..`Heading6` (outline level, else a "heading N" style) |
| consecutive numbered paragraphs | one `List`; each `ListItem` holds `ListLabel` (label run) and `ListBody` > paragraph or heading |
| hyperlink run | `Link` with the target (relationship target, or `#bookmark`) |
| run whose language differs from the document's | `Span` with the language |
| shading, borders, highlight, underline, strike, tab leaders | plain fills, which the PDF backend writes as artifacts |
| text | one `DrawGlyphRun` per same-font stretch: real glyph ids, per-glyph x, cluster indexes and the run text (for text extraction) |

A run with character scale other than 100 % is drawn under a horizontal-scale transform. The document root tag is the backend's.
Notes, references, TOC and header/footer artifacts use `Div`/`Span`/`Link`/`Artifact` until OneRender has the roles.

## Fonts

`IFontProvider` is the layout's only view of fonts: `Resolve(FontRequest)` returns a `FontMatch` (a OneFont `FontFace`, the family
asked for, the family used, and `FontMatchKind`), and `FindFallback(codePoint, bold, italic)` finds a face for a character the chosen
face lacks. `FontLibrary` is the standard provider:

- **Sources:** `CreateDefault()` (Liberation Sans/Serif/Mono, Carlito, Caladea and the symbols fallback, from OneRender's font
  package), `AddFont(bytes)`, `AddFile`, `AddDirectory`, `AddSystemFonts`, `AddFallback`, `AddOptionalPackages()` (the Noto
  script and CJK packages that have been `Register()`ed; they are not referenced by this package because the CJK set is 38 MB), and
  `DocumentFonts.AddEmbeddedFonts(document, library)`.
- **Embedded fonts:** the font table's `embedRegular`/`embedBold`/`embedItalic`/`embedBoldItalic` parts are de-obfuscated per §17.8.1:
  the first 32 bytes are XORed with the 16 bytes of the `fontKey` GUID read from the end of its string, repeated twice. Embedded fonts
  are added to the library before layout (unless `LayoutOptions.UseEmbeddedFonts` is false), so an embedded Calibri beats Carlito.
- **Lookup order:** exact family in the library (supplied and default alike) → the substitution table's candidates in order → a
  generic face chosen by `w:family`, PANOSE (serif style and proportion digits) and words in the name → any face in the library.
- **Reporting:** every result that is not `Exact` is a `FontSubstitutionNote` (once per pair) with its kind; nothing is silent.

| Kind | Meaning | Families |
|---|---|---|
| `MetricCompatible` | same advance widths by design | Calibri→Carlito, Cambria→Caladea, Arial and Helvetica→Liberation Sans, Times New Roman and Times→Liberation Serif, Courier New and Courier→Liberation Mono |
| `Approximate` | nearest open design, widths differ | Aptos, Aptos Display, Aptos Narrow, Calibri Light, Segoe UI family→Carlito; Aptos Serif, Georgia, Constantia, Palatino and other text serifs→Liberation Serif; Arial Narrow, Tahoma, Verdana, Trebuchet and other sans→Liberation Sans; Consolas, Lucida Console, Cascadia→Liberation Mono; Symbol, Wingdings, Webdings→Noto Sans Symbols 2 (list bullets are mapped, other symbols show missing glyphs); East Asian system fonts→Noto Sans CJK SC; Arabic and Hebrew system fonts→Noto Sans Arabic/Hebrew |
| `Generic` | no stand-in known | a default sans, serif or monospace face |

Fonts are not committed. OneRender's font packages carry the OFL-licensed faces above (licence text and manifest ship in those packages).

## Paragraph and line layout

Run segmentation, shaping, line breaking and placement, in order:

1. **Properties** come from the hub's resolved paragraph and run properties (styles, numbering levels, direct formatting).
2. **Text:** runs are walked in order (through hyperlinks, simple fields, content controls, insertions); `w:t`, tabs, breaks,
   `noBreakHyphen`, `softHyphen` and `sym` make characters; vanished runs contribute nothing; drawings are reported and skipped.
3. **Segments** split at run changes, bidi level changes (UAX #9 through OneFont), small-caps case changes and font category
   changes. Category picks the font: Latin below U+0080 uses `ascii`, other Latin and symbols `hAnsi`, CJK `eastAsia`, complex
   scripts `cs` (with `szCs`, `bCs`, `iCs`). §17.3.2.26.
4. **Shaping** through OneFont per segment, with fallback faces for characters the font lacks. Kerning is on only when the run has
   `w:kern` and its size is at least that value (§17.3.2.19); ligatures are off (Word enables them only with `w14:ligatures`).
   Caps map to capital letters; small caps draw lower-case letters as capitals at 80 % size; superscript and subscript use 65 %.
5. **Breaking** (greedy, per line): opportunities from UAX #14 (OneFont), so CJK breaks between ideographs under the standard
   line-start/line-end rules. Spaces hang past the margin. A word longer than the line breaks at a cluster boundary. `w:wordWrap` off
   allows a break between any two characters. Hyphenation is **not implemented** (a gap; soft hyphens are zero-width break points
   and do not print a hyphen at a line end).
6. **Tabs** (§17.3.1.37): left, center, right and decimal stops; default stops every `w:defaultTabStop` (36 pt) beyond the last
   custom stop; a hanging indent adds a stop at the left indent; leaders dot, hyphen, underscore, heavy and middleDot. Bar tabs are
   ignored. A tab past the right margin ends the line.
7. **Indents** (§17.3.1.12): left/start, right/end, firstLine, hanging. In a right-to-left paragraph `left` is the leading (right) side.
8. **Alignment** (§17.18.44): left/start, center, right/end, both (spreads the extra width over spaces, never the last line, and
   lines ending in a line break unless `doNotExpandShiftReturn` is set), distribute (also between characters). In a right-to-left
   paragraph `left` and `right` read as start and end.
9. **Line height:** per line, the tallest ascent (ascender plus line gap) and deepest descent from the font's metrics, scaled by
   size; the paragraph mark's font takes part in the last line and decides an empty paragraph. Spacing rule `auto` multiplies by
   `line/240` (extra above the text), `exact` fixes the height (descent scaled), `atLeast` is a floor.
10. **Paragraph spacing:** space before and after add (they do not collapse); `contextualSpacing` removes the gap between paragraphs
    of one style; auto-spacing attributes mean 14 pt.
11. **List labels:** the label sits at the first-line position with the suffix `tab` (next tab stop), `space` or nothing; the label
    uses the paragraph mark's character formatting with the level's run properties on top. Symbol and Wingdings bullets are mapped to
    Unicode.
12. **Character formatting:** spacing (`w:spacing`), scale (`w:w`), position, underline styles single/double/thick, strike and double
    strike, highlight and run shading, colour. Paragraph shading and borders are painted; a top border adds its width and space to the
    paragraph's height.

## Pagination

Within a page: widow and orphan control (two lines, on by default), keep-lines-together, keep-with-next (chains up to 20
paragraphs; the first two lines of the following paragraph), page-break-before, and hard page and column breaks, including a break
in the middle of a paragraph (the lines after it go to the next page or column). Space before at the top of a page is dropped after
a natural break and kept on the first page and after forced breaks.

## The page model (`Layout/Pages`)

Every page is built from its section's properties (`SectionGeometry`) and the flow places body text into the page's columns.

**Sections** (§17.6). Page size, margins, gutter and columns come from each section's `sectPr`. A `nextPage` section starts a new
page; `continuous` shares the page when the page size is the same (else it starts a page); `nextColumn` continues in the next column
of the same page; `oddPage` and `evenPage` start on a page whose *printed* number has that parity and insert a blank page when the
next page has the wrong one (`LayoutPage.IsBlank`, note `layout.blank-page`). With `evenAndOddHeaders` on, a `nextPage` section
also keeps its physical page and printed number in step (the hub's `RequiresBlankPageBefore`, observed in Word 365). A blank page
gets the header and footer of the section it precedes and counts in the page numbering.

**Margins.** The gutter is added on the left (right in a right-to-left section, top with `gutterAtTop`). With `mirrorMargins`
odd printed pages keep the inside margin on the left and even pages swap the left and right margins, the gutter staying inside.

**Columns** (§17.6.4). Equal columns share the width left after the gaps; unequal columns use each `w:col` width and the gap that
follows it. A right-to-left section starts at the right edge. The flow fills a column to the body bottom, then the next, then a
new page. A paragraph that moves to a column of another width is cut again for that width and resumes where the text had got to.
A column break moves to the next column (the next page with one column); a page break always starts a new page. The separator
line is a `LayoutDecoration` the height of the text in the region. `LayoutPage.ColumnRegions` lists the column rectangles of each
stretch of the page (one per section on it); `LayoutParagraph.Column` is the column a block sits in.

**Balanced columns.** A section that is followed by a `continuous` section is balanced: layout looks for the smallest column
height at which its text still fits its columns (the real placement run in a scratch flow, by bisection), and the next section
starts below the tallest column. The last section of the document is not balanced.

**Headers and footers** (§17.10). The hub resolves the story for each page (`first` on a section's first page with `titlePg`,
`even` on even printed pages with `evenAndOddHeaders`, else `default`; a missing story is taken from the nearest earlier section
that has it). The header text starts `header` points from the page top and the footer text ends `footer` points from the bottom.
A taller story pushes the body edge in (`LayoutPage.ContentArea` is the body after that). Their paragraphs are
`LayoutParagraph`s with `Story` Header or Footer, listed by `HeaderParagraphs`/`FooterParagraphs`, and painted inside an
`Artifact` tag. Tables in a story are skipped until tables are laid out.

**Page numbers.** `pgNumType start` restarts the printed number at a section's first page; `fmt` formats it. `PAGE`, `NUMPAGES`,
`SECTIONPAGES` and `SECTION` fields in headers, footers and the body show the page's values, and `PAGEREF`, `REF`, `STYLEREF`, `SEQ`,
the table of contents, index and table of authorities are resolved the same way: layout runs again with the facts of the pass before
until they settle (see `layout-fields.md`). Chapter numbers (`chapStyle`) in page numbers are not applied.

**Decoration** (`LayoutPage.Decorations`, painted as artifacts). Page borders (§17.6.2): `offsetFrom` text or page, `display`,
single, thick, double, triple, dashed and dotted drawn as rectangles; art borders are drawn as plain lines (note
`layout.page-border-art`). Line numbers (§17.6.8): `countBy`, `start`, `distance` (automatic is 18 pt), restart per page, section
or never; paragraphs with `suppressLineNumbers` are not counted. Text watermarks: a header shape with a text path (the shape Word
writes for "DRAFT" and the like) is drawn centred in the text area at its rotation, with its fill colour and opacity, with the text
scaled to the shape's width; picture watermarks are not drawn. `vAlign` centre and bottom move the body text (`both` is noted and
laid out at the top, note `layout.valign-both`). A `docGrid` with a line pitch makes each line a whole number of pitches tall, the
extra above the text, unless the paragraph turns `snapToGrid` off; the character grid (`charSpace`) is not applied.

**Footnote hook.** `LayoutPage.NotesReservation` is the space the body gives up at its bottom for notes. Layout lowers the body's
bottom edge by `Flow.ReserveForNotes(page)`, which returns 0 until notes are laid out.

## Compatibility settings

`compatibilityMode` (11–15, from `w:compatSetting`; 11 when `w:compat` exists without it, 12 when there is no `w:compat`) is read into
`LayoutDocument.CompatibilityMode`. What this stage varies by mode:

| Setting | Effect | Modes |
|---|---|---|
| `defaultTabStop` | default tab spacing | all |
| `doNotExpandShiftReturn` | justified lines ending in a line break are not spread | all |
| `doNotUseHTMLParagraphAutoSpacing` | read, not yet used | all |

Differences between modes (table cell layout, line height in tables, justification of lines with tabs and the like) belong to the
stages that lay out those features and are added to this table with them.

## Known deviations and open points (to check against Word)

| Point | What is done | Why open |
|---|---|---|
| Line height source | `hhea` ascender + line gap above, descender below | Word's choice between `hhea` and OS/2 win metrics matters only for fonts where they differ |
| Where leading goes | all of it above the baseline | needs Word's baseline positions |
| `exact` spacing baseline | descent scaled by height / natural | needs Word output |
| Space before at page top | dropped after a natural break | Word's rule has compatibility variants |
| Paragraph spacing | adds, does not collapse | needs Word output |
| Superscript/subscript | 65 % size; raised half the size / lowered a quarter | Word uses font data (OS/2 offsets) |
| Small caps | 80 % size | Word's factor |
| Justification in lines with tabs or bidi | spreads over spaces only | |
| Bidi mirrored punctuation, trailing spaces in right-to-left lines | not handled specially | |
| Hyphenation, kashida, the East Asian character grid and `w:eastAsianLayout` | not implemented | later |
| Inline pictures, shapes, notes | not laid out (reported) | later stages |
| Tables | laid out; see `layout-tables.md`. In columns they keep the width cut for the first column; in headers and footers they are skipped | |
| Blank page from an odd/even break | has the header and footer of the following section, counted in the numbering | needs Word output |
| Header and footer vertical placement | header text top at `header`, footer text bottom at page height minus `footer`; the body edge takes the larger of the margin and the story | needs Word output |
| Balanced columns | bisection on the column height, line granular | Word's balancing with keep rules may differ by a line |
| Mirror margins | parity of the printed page number | Word may use the physical page |
| Line number distance `auto` | 18 pt, numbers right-aligned, `countBy` multiples of the line count | needs Word output |
| Watermark placement | centre of the text area, text width = shape width | Word's `mso-position` and size fitting |
| Section break after the last paragraph on a full page | the section's page is created even if empty | needs Word output |
