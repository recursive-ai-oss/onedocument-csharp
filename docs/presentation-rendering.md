# Rendering presentations

How a presentation becomes PDF and PNG pages. Namespace `RecursiveAI.OneDocument.Rendering.Presentations`, in the
`RecursiveAI.OneDocument.Rendering` package. The hub it reads is described in `presentations.md`, the shape painter in
`drawingml-rendering.md`, the shared paint contract in `layout.md`. References are to ECMA-376 Part 1. Items marked **to check against
PowerPoint** are rules the standard leaves open; they are settled by comparing with PowerPoint's own output, and are listed in the
table at the end. Nothing here has been compared with PowerPoint yet.

## Entry points

| Call | Result |
|---|---|
| `PresentationRenderer.WritePdf(presentation, stream, options)` | A PDF with one page per page of output, and a `PresentationRenderReport` (pages, hidden slides skipped, font substitutions, notes). |
| `PresentationRenderer.RenderPng(presentation, pageIndex, options)` | One page as a PNG at 144 dpi. |
| `presentation.SaveAs("deck.pdf")`, `Office.Convert("deck.pptx", "deck.pdf")` | The same with the default options; a `.png` target writes the first page. Registered by `RenderingFormats.Register`, which referencing the package does by itself. |
| CLI `onedocument convert deck.pptx deck.pdf` (or `.png`) | The same, through the registry. |

`PresentationRenderOptions`: `Output` (flags `Slides`, `NotesPages`, `Handouts`; default `Slides`), `IncludeHiddenSlides` (default false),
`Tagged` (true), `Compress`, `Profile` (PDF/UA-1, PDF/A-2a), `Title`, `Fonts` (an `IFontProvider`, default the open fonts of
`FontLibrary.CreateDefault()`), `Clock`, `Autofit`, `HandoutSlidesPerPage` (1, 2, 3, 4, 6 or 9; default 6). Rendering reads the
presentation and never changes it, and is not safe to run on one presentation from several threads.

## A slide

The page is the slide size (`p:sldSz`, 10 by 7.5 inches when it is missing; limited to 1 pt to 200 in). A slide is painted back to front:

1. **Background.** The first of the slide's, its layout's and its master's `p:bg` is painted over the page: `bgPr` with a solid, gradient,
   picture or pattern fill, or `bgRef` into the theme's background fill styles (§19.3.1.1). The part that owns the background supplies
   the colour map, theme and pictures.
2. **Master and layout shapes**, unless the slide says `showMasterSp="0"` (which hides both); the master's are also hidden when the
   layout says so (§19.3.1.38). Only shapes that are not placeholders are drawn: placeholders on a layout or master supply positions and
   formatting, and show nothing of their own. They are one artifact in the structure tree.
3. **The slide's own shapes** in shape-tree order (the z-order and the reading order). Shapes marked hidden (`cNvPr/@hidden`) are skipped.

Hidden slides (`show="0"`) are left out of the output unless `IncludeHiddenSlides`; the report counts them. A presentation with nothing to
render still writes a one-page PDF (blank, with the note `slide.no-pages`).

### Placeholders and inheritance

The hub resolves which layout and master placeholder a shape inherits from (`presentations.md`). The painter asks for that chain through
`DrawingContext.ShapeResolver` and takes, per property block, the first shape in the chain that has it:

- position and size (`a:xfrm`), preset or custom geometry, fill, line and effects, each independently;
- the shape's `p:style` when it has none of its own;
- a shape with a text body and no geometry anywhere in its chain draws no outline of its own and keeps its text in its box.

Text properties inherit as the hub describes (shape `lstStyle`, layout and master placeholder `lstStyle`, the master's title, body or other
style by placeholder type, the presentation's `defaultTextStyle`; the notes master's `notesStyle` for notes pages), by paragraph level 1
to 9, attribute by attribute. A bullet is taken as a unit from the first source that names one; its font, size and colour are
independent. Scheme colours go through the slide's colour map override or the master's, then the master's theme; theme fonts
(`+mj-lt`, `+mn-lt`) resolve to the theme's major and minor Latin fonts. East Asian and complex-script theme fonts are not read from the
theme: those characters use the run's own `ea`/`cs` typeface when it names one, else the font library's script fallback.

### Pictures, charts, diagrams, objects, tables

| Content | Painted as |
|---|---|
| `p:pic` | the shape painter's picture path (crop, outline, effects); metafiles through the picture hook (`metafiles.md`). A picture is a `Figure` with its `descr` as alternative text, or an artifact when marked decorative. |
| chart frame | the chart renderer (`charts.md`) at the frame's box; a `Figure` with the frame's description. |
| SmartArt | the drawing PowerPoint saved with the diagram (`dsp:drawing`, found through the data part's `dataModelExt` and the slide's relationship), painted as ordinary shapes in the frame; note `slide.smartart-cached`. A diagram with no saved drawing is not painted (`slide.smartart-not-painted`); regenerating one from its layout definition is a later unit. |
| embedded object (OLE) | the preview picture saved with it (`p:pic` inside `p:oleObj`); note `slide.object-not-painted` when there is none. Objects are never opened or run. |
| table (`a:tbl`) | see below |
| connectors, other shapes | the shape painter; a connector is an artifact. |

Animations and transitions are preserved in the file and not rendered.

## Text bodies

Text goes through OneDocument's own text layout (`SlideTextLayout`), built on OneFont: bidi levels, Unicode line-break opportunities,
shaping with script fallback, and the same font provider and substitution notes as Word layout. Lengths are points.

**The body** (§21.1.2.1.1 `bodyPr`), each attribute inherited like the rest:

- Insets default to 0.1 in left and right and 0.05 in top and bottom. The text rectangle is the shape's text rectangle (for a preset
  shape, the preset's own, so an ellipse or a rounded rectangle takes a smaller one) less the insets.
- `anchor` `t`/`ctr`/`b` places the block of lines in the rectangle. Text taller than the rectangle is not clipped; it overflows downward
  from the top anchor, both ways from the middle, upward from the bottom. `anchorCtr` centres the block horizontally (the widest line's
  extent, which is what a body with `wrap="none"` is about).
- `wrap="none"` does not break lines; a centred or right-aligned line is placed against the box's edges even when it is wider.
- `numCol`/`spcCol`: the width is divided into equal columns and lines flow down the first column until the next would pass the
  bottom of the rectangle, then into the next; the last column takes the rest.
- `vert`: `vert` rotates the text 90° clockwise (lines run down, the first line is at the right), `vert270` 90° counter-clockwise (first
  line at the left); `eaVert`, `mongolianVert`, `wordArtVert` and `wordArtVertRtl` are drawn as these (notes
  `slide.text-eavert-approximated`, `slide.text-vertical-approximated`). Columns do not apply to vertical text.
- `rot` turns the text about the centre of its rectangle on top of the shape's rotation; `upright="1"` keeps the text level whatever
  the shape's rotation (about the rectangle's centre). A flipped shape does not mirror its text (a vertical flip turns it over, as
  PowerPoint does).

**Paragraphs** (§21.1.2.2.7 `pPr`): `marL` is the left margin of every line, `indent` moves the first line from it, `marR` the right
margin; `algn` `l`, `ctr`, `r`, `just` (the last line is not stretched), `dist` (every line, letters included, is spread across the
width); `lnSpc` as a percentage of the font's natural line height (ascent plus descent plus line gap) or exact points, the line's
baseline sitting at the same proportion of the line; `spcBef`/`spcAft` in points or as a percentage of the line height; `tabLst` stops
and `defTabSz` intervals measured from the left edge of the text rectangle (every stop acts as a left tab); `a:br` is a line break that
starts the next line at the paragraph's indent. Lines are broken greedily at the last break opportunity that fits; spaces at the end of a
line hang. `rtl="1"` sets the paragraph's base direction; margins and `algn` stay physical.

**Runs** (§21.1.2.3.9 `rPr`): `sz` (default 18 pt), `b`, `i`, `u` (every underline style is drawn as a single line, `dbl` as two,
`heavy…` thicker), `strike`/`dblStrike`, `cap` (`all`, `small` at 80 % of the size), `spc` (added after each cluster),
`baseline` (shifted by that fraction of the size, the text set at 2/3 size), `kern` (kerning on when the size is at least the value),
colour from `solidFill` (a gradient takes its first stop), `highlight`, `latin`/`ea`/`cs` typefaces. A run with a hyperlink takes the
theme's link colour and an underline and is a `Link` with its target (external hyperlinks only; slide jumps and actions are drawn as
plain underlined text).

**Fields** (§21.1.2.2.4): `slidenum` shows the slide's number (`firstSlideNum` plus the slide's index, hidden slides counted); a
`datetime…` field shows the text stored in the file, or, when `Clock` is set, the date or time in the format its type names
(`datetime1` is `M/d/yyyy` and so on). The output does not depend on the date it is made unless the caller supplies the clock.

**Bullets** (§21.1.2.4): `buChar` and `buAutoNum` text is shaped in the bullet's font (`buFont`, else the text's) at `buSzPct`
(default 100 %) or `buSzPts` of the first run's size, in `buClr` or the first run's colour, at `marL + indent`. Symbol and Wingdings
code points are mapped to Unicode characters the open symbol font has. A picture bullet (`buBlip`) is drawn as a round bullet. When the
bullet fits in the hanging space the text starts at `marL`; a wider bullet is followed by the next tab stop.
Numbers count up through consecutive paragraphs of the same scheme and `startAt` at one level; a paragraph at a shallower level, one
without that numbering, or a different scheme ends the run. Deeper levels in between do not reset a shallower count. A paragraph with
no text shows no bullet and takes no number. Schemes: arabic, alpha (a, b … z, aa, bb …), roman, each lower or upper with `Period`,
`ParenR`, `ParenBoth` or `Plain`; other schemes are numbered as arabic with a period.

## Autofit

- `a:noAutofit` (or none anywhere in the chain): text is laid out as it is and may overflow.
- `a:normAutofit` (shrink text on overflow): `fontScale` multiplies every font size (bullets included) and `lnSpcReduction` is taken off
  percentage line spacing (100 % becomes 90 % for 10000), **as stored**, which is what PowerPoint shows. Exact line spacing and
  spacing in points are not changed. When the file stores neither, the scale is **computed** (default
  `SlideAutofitMode.StoredThenComputed`): the first of these steps whose text fits the rectangle is used, the last one when none does.

  | font scale | 100 % | 100 % | 100 % | 92.5 % | 92.5 % | 85 % | 77.5 % | 70 % | 62.5 % | 55 % | 47.5 % | 40 % | 32.5 % | 25 % |
  |---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
  | line spacing reduction | 0 | 10 % | 20 % | 10 % | 20 % | 20 % | 20 % | 20 % | 20 % | 20 % | 20 % | 20 % | 20 % | 20 % |

  The steps are the values PowerPoint is seen to store; which step it picks for a given text, and whether it rounds sizes, is **to
  check**. `Autofit = Stored` never computes; `Computed` ignores stored values.
- `a:spAutoFit` (resize shape to fit text): the shape's height becomes the text's height plus the top and bottom insets, keeping the top
  edge for a top anchor, the centre for a middle anchor and the bottom edge for a bottom anchor, before the shape is painted, so its
  fill and outline follow. The width is not changed, including for `wrap="none"`; vertical text does not resize.

## Tables

A graphic frame holding `a:tbl` is painted from the grid: column widths from `a:gridCol`, row heights from `a:tr/@h`, never narrower
than the frame says and growing taller when a cell's text needs it (a merged cell's need goes to the last row it spans). The frame's
own `ext` is not used to scale the grid.

- Merges: `gridSpan` + `hMerge`, `rowSpan` + `vMerge`; the origin cell is drawn over the whole region and the continuation cells are
  skipped.
- Cell margins `marL`/`marR`/`marT`/`marB` (0.1 in, 0.1 in, 0.05 in, 0.05 in), `anchor` `t`/`ctr`/`b`; cell text goes through the text
  layout with the presentation's default text style.
- Table styles (§20.1.4.2.27 `tblStyle`): the style named by `tableStyleId`, looked up in `ppt/tableStyles.xml`, applies `wholeTbl`, then
  `band1H`/`band2H` (with `bandRow`, counting from the first data row), `band1V`/`band2V` (`bandCol`), `firstCol`/`lastCol`,
  `lastRow`, `firstRow` (the later part wins) for text bold and colour, fill (`a:fill` or `fillRef`) and the six borders
  (`insideH`/`insideV` on interior edges). A run's own bold or colour wins over the style's. The cell's own `tcPr` fill and `lnL`/`lnR`/`lnT`/`lnB`
  win over the style. Generators often name the default style `{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}` without defining it; the
  built-in Medium Style 2 – Accent 1 is used for that id.
- Borders are solid bars centred on the cell edge (dash styles are not drawn); gradient, pattern and picture cell fills are approximated
  (notes `slide.table-gradient-approximated`, `slide.table-fill-approximated`). A table with more than 20,000 cells is not painted
  (`slide.table-limit`).

## Notes pages and handouts

`Output.NotesPages` adds, after the slides, one page per rendered slide at the notes size (`p:notesSz`, 6 by 8 in when absent): the
notes master's background and non-placeholder shapes, the slide scaled into the notes master's (or notes slide's) slide-image
placeholder with its outline, and the notes slide's own shapes (the notes text in the body placeholder, in the notes master's
`notesStyle`). A slide without notes gets a page with the picture and no text. Without a notes master the picture is placed in the
upper part of the page. The picture is a `Figure` named "Slide *n*: title".

`Output.Handouts` adds pages of `HandoutSlidesPerPage` slide pictures (1×1, 1×2, 1×3, 2×2, 2×3 or 3×3 grids) with a thin frame, on the
notes page size. The handout master (header, footer, lines) is not drawn (`slide.handout-master-ignored`).

## Tagging

Each page is a `Sect`. A title or centre-title placeholder's paragraphs are `H1`; other paragraphs are `P`; bulleted and numbered
paragraphs are `L` > `LI` > `Lbl` + `LBody` (consecutive ones share a list); tables are `Table` > `TR` > `TH`/`TD` with spans, the
first row always being header cells (PDF/UA wants a header in every table, so a table that does not mark `firstRow` still gets one); pictures, charts and diagrams are `Figure` with alternative text;
connectors and objects marked decorative are artifacts; backgrounds, master and layout shapes, date, footer and slide-number
placeholders, and shape fills are artifacts. Reading order is the shape-tree order. `Note`, `Reference`, `TOC` and artifact kinds
wait for OneRender's roles (see `layout.md`).

## Limits and defects

A text body longer than 200,000 characters is cut off (`slide.text-truncated`); lengths, scales and spacing read from the file are
clamped to what a slide can show; shapes per drawing, nesting and tables are limited as above and in `drawingml-rendering.md`; the
page size is clamped and a PNG over 100 million pixels raises `OfficeFormatException` with code `render.failed`. Malformed parts reach
rendering as the hub's typed defects (`defects.md`, `pptx.*`); rendering adds no defect codes. What was approximated or left out is
reported as notes on the report, once per code:

| Code | Meaning |
|---|---|
| `slide.no-size`, `slide.no-pages`, `slide.text-body-missing` | no `p:sldSz`; nothing to render; a shape's text body was not found |
| `slide.text-truncated`, `slide.table-limit` | limits |
| `slide.text-vertical-approximated`, `slide.text-eavert-approximated` | vertical text variants |
| `slide.smartart-cached`, `slide.smartart-not-painted`, `slide.object-not-painted` | diagrams and objects |
| `slide.table-gradient-approximated`, `slide.table-fill-approximated` | table fills |
| `slide.handout-master-ignored` | handouts |
| `slide.math-plain-text` | an equation in slide text is drawn as its plain text (math layout is a separate unit and plugs in here) |
| `drawing.*` | from the shape painter (`drawingml-rendering.md`) |

## Fonts

The default provider substitutes metric-compatible open fonts for the common Office fonts and reports each substitution
(`PresentationRenderReport.FontNotes`). Aptos and the other new Office defaults are approximated by Carlito, so line breaks of decks in
those fonts differ from PowerPoint's until both sides use the same installed fonts (`layout.md`).

## To check against PowerPoint

Generated inputs are in `testdata/slides` (`tools/layout-differential.sh` asks PowerPoint for its PDF of each); none is answered yet.

| Rule | Chosen here | Case |
|---|---|---|
| Space before the first paragraph of a body | not applied | `spacing-checks` |
| Percentage spacing before/after | percentage of the first line's height | `spacing-checks` |
| Line spacing below 100 % on the first line | the whole line shrinks, baseline kept at the same proportion | `spacing-checks` |
| Text start after a bullet wider than the hanging space | next tab stop | `bullets`, `spacing-checks` |
| Autofit step chosen for a text; font size rounding | first fitting step of the table above | `autofit` (stored values only; PowerPoint recomputes when text is edited, so the oracle driver must edit text to answer) |
| Percentage line spacing is cut by `lnSpcReduction` | subtracted (100 % → 90 %) | `autofit` |
| Margins and alignment of right-to-left paragraphs | physical (`marL` stays on the left) | `spacing-checks` |
| Centre, right and decimal tab stops | treated as left | `spacing-checks` |
| Empty numbered paragraph | no bullet, no number consumed | `bullets` |
| Non-placeholder text style source | master `otherStyle`, then `defaultTextStyle` | `inherit-position` |
| `bodyPr/@rot` pivot and `upright` placement | rectangle centre | `vertical-and-columns` |
| Underline thickness and position, strike position | font metrics; 30 % of the size above the baseline | `runs` |
| Baseline-shifted text size | two thirds | `runs` |
| Table borders with `cmpd`/dash, border winner on shared edges | solid; later cell wins | `table` |
