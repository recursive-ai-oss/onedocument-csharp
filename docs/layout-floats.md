# Floating objects and text wrap

How anchored objects are positioned and how text goes round them. Code: `Layout/Floats/`, with hooks in the paragraph formatter, the page
flow and the painter. DrawingML painting itself is in `drawingml-rendering.md`.

## What floats

| Source | Position | Wrap |
|---|---|---|
| `wp:anchor` (picture, shape, text box, chart) | `positionH`/`positionV`, `simplePos`, `wp14` percentages | `wrapNone`, `wrapSquare`, `wrapTight`, `wrapThrough`, `wrapTopAndBottom` |
| `w:pict` VML shape (`v:shape`, `v:rect`, `v:roundrect`, `v:oval`, `v:line`) | `position:absolute` with `margin-left`/`margin-top` and the `mso-position-*` properties | `w10:wrap` type and side |
| floating table (`w:tblpPr`) | `tblpX`/`tblpY` or `tblpXSpec`/`tblpYSpec` from `horzAnchor`/`vertAnchor` | text wraps on both sides, with `leftFromText` etc. |
| text frame (`w:framePr`) | `x`/`y` or `xAlign`/`yAlign` from `hAnchor`/`vAnchor` | `wrap` (`around`/`auto` square, `tight`, `through`, `notBeside` top and bottom, `none`) |
| drop cap (`w:framePr w:dropCap`) | at the left of the paragraph after it | the first `lines` lines go round it; `margin` puts it in the margin without moving text |

Inline drawings are not floats: they take a character's place in the line (`drawingml-rendering.md`).

## Positioning (ECMA-376 Part 1 §20.4.2.10, §20.4.2.11, §20.4.2.13; [MS-ODRAWXML] for `wp14`)

- Horizontal bases: `page`, `margin`, `column`, `character`, `leftMargin`, `rightMargin`, `insideMargin`, `outsideMargin`. Vertical
  bases: `page`, `margin`, `paragraph`, `line`, `topMargin`, `bottomMargin`, `insideMargin`, `outsideMargin` (inside is the top, outside
  the bottom). Inside and outside follow the printed page number: page 1 is a right-hand page, so inside is the left margin.
- The offset (`posOffset`, or `wp14:pctPosHOffset`/`pctPosVOffset` as a fraction of the base) or the alignment (`left`, `center`,
  `right`, `inside`, `outside`; `top`, `center`, `bottom`) is applied to the base. `simplePos` overrides it with page coordinates.
- `wp14:sizeRelH`/`sizeRelV` with a percentage replace the stated extent: width and height are that fraction of the named base.
- Base `paragraph` is the top of the anchor paragraph's first line (after its space before); `line` is the top of the line that holds the
  anchor; `character` is the left edge of the column (the anchor's own x is not known before the lines are cut).
- An object positioned relative to the text stays on its page: when it would run off the bottom it is moved up. Page-relative objects
  are not moved. An object positioned from the text also stays above the footnote area of its column as far as the notes are known when
  it is anchored (the note reservation is lowered into the body's bottom edge); notes that grow afterwards can still reach it.
- `allowOverlap="0"`: an object that would overlap an earlier one that also may not overlap is moved below it, if the page allows.
- `locked` and `layoutInCell` do not change layout, except that an object in a table cell is placed from the cell when `layoutInCell`
  is on (default) and from the page otherwise; text in the cell does not wrap around it.
- The object belongs to the page its anchor paragraph's first line is on. A paragraph that moves to the next page (keep with next,
  keep lines, widow control, a page break) takes its objects with it.

## Wrapping

- Text is cut into **rows**; where an object takes part of the width, a row becomes **segments** of free space, each filled like a
  line of its own. The segments of one row are consecutive `LayoutLine`s of the paragraph that share a baseline and height (the second
  and later have `SameRow` internally; in the tree they have equal `Baseline`). A row is cut again when its height (the tallest segment's)
  moves it into or out of an object's band.
- `wrapSquare`: the box plus `distT`/`distB`/`distL`/`distR` (and `effectExtent`). `wrapTight`/`wrapThrough`: the extent of the wrap
  polygon (21600ths of the object's size) or, without one, of the shape's own outline, on each row, plus the side distances; concave
  parts are not entered (through is treated as tight). `wrapTopAndBottom`: nothing beside the object; text resumes below it plus
  `distB`. `wrapNone`: no effect on text; the object is painted behind (`behindDoc`) or in front.
- `wrapText`: `bothSides` fills every segment, `left`/`right` blocks everything on the other side of the object, `largest` keeps the
  widest segment of the row.
- A segment narrower than 12 pt is left empty (`FloatSpace.MinimumSegment`). If no segment is wide enough the rows move down to the
  nearest object edge.
- A display equation has a row to itself. Where an object splits the row, it takes the widest free stretch and nothing else shares
  its baseline; an inline equation is an unbreakable atom and goes into whichever stretch has room for it. Which of these Word does
  is listed with the points to check below.
- Space skipped above a row (under a top-and-bottom object, or beside one too wide for text) is `GapBefore` on the row's first line, so
  the page flow and the tree see lines at their real positions.
- Paragraphs are cut around the objects already on the page and the ones the paragraph itself anchors. A paragraph that is cut again
  on a later page keeps the lines already placed.
- Objects in headers and footers are painted but do not wrap body text. Text in table cells does not wrap around objects.

## Painting

`LayoutPage.Floats` lists the placed objects (`LayoutFloat`: `Bounds`, `Wrap`, `BehindText`, `ZOrder`, `AltText`, `AnchorParagraph`).
The painter draws, in order: decorations, the objects behind the text (by `relativeHeight`, then anchor order), the blocks, the objects
in front. Objects of a header or footer are artifacts.

## Frames and drop caps

- Consecutive paragraphs with equal `framePr` form one frame, laid out at its width (`w`; without one, the width its text needs up to
  the column), stacked with paragraph spacing, at the frame's position. `hRule` `exact` and `atLeast` set its height. The frame takes
  no room in the flow.
- A drop cap is formatted from the paragraph with `dropCap` `drop` or `margin` and set when the paragraph after it is placed. Its
  baseline is the baseline of the last of its `lines` lines; the lines it spans start to its right (`hSpace` away).

## Notes and limits

`layout.anchored-drawing-unwrapped` and `layout.table-floating` are gone. VML groups, `v:group` and `w:object` stay unlaid (`layout.drawing-skipped`).

Points that need Word's own output at the 1.0 gate: where a display equation goes beside an object, the minimum segment width, the `paragraph` and `line` bases (space before and the
line top), where a drop cap's box ends, the frame width when none is given, and how Word treats tight wrapping without a polygon.

Known gaps: paragraphs already placed on a page do not move when a later anchor positions an object over them; rotated objects wrap
by their unrotated box; a floating table does not wrap text that came before it in the document; text frames do not split across
pages; floats anchored in text boxes, headers and cells do not wrap body text.
