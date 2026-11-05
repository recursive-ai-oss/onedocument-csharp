# Metafiles

How OneDocument draws embedded Windows metafiles. Namespace `RecursiveAI.OneDocument.Rendering.Metafiles`, package `RecursiveAI.OneDocument.Rendering`. The interpreter is OneImage's (`RecursiveAI.OneImage.Metafile`: it reads the file and plays it as a stream of device-independent drawing commands, see its own documentation); this page is the adapter from that stream to OneRender's display list. References are to [MS-EMF] and [MS-WMF].

## What is drawn

| Input | Result |
|---|---|
| EMF (GDI records, including the GDI records of EMF+ dual files) | vector content, described below |
| EMF+-only, WMF | not drawn yet: `render.metafile-unsupported`, `TryPaint` returns false and the caller falls back to the picture's cached bitmap or a placeholder |
| damaged or not a metafile | `render.metafile-damaged`, returns false |

OneImage's interpreter reads EMF only. EMF+ records and WMF are on its list; the adapter needs no change when they arrive, because it works on the command stream.

## The hook

`PictureHook.TryPaint(bytes, contentType, context, destination, crop)` is the one call a picture painter makes. It decides by media type (`image/x-emf`, `image/emf`, `image/x-wmf`, `image/wmf`, or the extension) **or** by content (the EMF header signature, the placeable-WMF key, the standard WMF header), so a part with a wrong or missing content type still draws. It returns false without touching the list when it cannot draw. `PictureHook.TryGetNaturalSize` reads the frame from the header (hundredths of a millimetre, converted to points), for pictures laid out at natural size.

`MetafilePaintContext` carries what a page painter already has: the `DisplayList` (CSS pixels), a `PictureImages` table (display lists name images by handle; hand the same table to `DisplayListPdfWriter` or `RasterOptions.Images`), an id source, an optional font provider (default: the standard `FontLibrary`), a defect callback with the picture's part location, and whether to wrap the picture in a `Figure` tag with alternative text.

`PictureCrop` is `a:srcRect` (ECMA-376 Part 1 §20.1.8.55): fractions cut from each side, negative values extend the picture. The shown part spans `destination`, and everything the picture draws is clipped to it. Flips and rotation belong to the caller's own transform around the call.

## Mapping

The command stream is a canvas with a transform and a clip, both set by commands. The adapter maps every point to page pixels itself (logical → picture device → page), so the display list holds only clips (the destination box, then the metafile's own clip) and short transforms around text and bitmaps.

| Command | Display list |
|---|---|
| `set-transform`, `save`, `restore` | the adapter's own state; no items |
| `clip` | a stack of `PushClipPath` items, re-pushed when the clip changes. Replace and reset clear it, intersect adds a shape, exclude adds a huge box with the shape cut out (even-odd). `offset-clip` moves the shapes |
| `fill-path` solid | `FillPath` |
| `fill-path` hatch | background fill, then 1 px lines on an 8 px grid anchored at the brush origin, clipped to the path. Horizontal and vertical lines sit on whole pixels |
| `fill-path` pattern/DIB brush | the bitmap as an image, tiled unscaled from the brush origin, clipped to the path |
| `stroke-path` | the pen is expanded to an outline (OneImage's stroker: width, caps, joins, miter limit, dashes) and filled, because the display list has no stroke item. A cosmetic pen is 1 CSS px wide whatever the scale; a geometric pen scales with the picture. Gaps of a dashed line take the background colour when the file says so |
| `blit` | `DrawImage` under a transform that maps the bitmap onto the destination parallelogram (so `PlgBlt` rotation and mirrored `StretchBlt` work); constant alpha is a group; a transparent colour or a mask becomes pixel alpha; `PatBlt` with `PATCOPY`, `BLACKNESS`, `WHITENESS` is a fill |
| `gradient-fill` rectangles | `FillPath` with a linear gradient between the two vertex colours |
| `draw-text` | `DrawGlyphRun`: the resolved font (OneFont face), glyph ids, per-glyph x from the record's advances (or the font's), cluster indexes and the text. Text stays text, so it is selectable and extractable in PDF. Escapement rotates the run in device space; alignment, character extra, justification, underline, strike-out, opaque and clip rectangles are applied |
| `comment` | ignored |
| `diagnostic` | warnings and errors become `render.metafile-diagnostic` defects with the record index (information-level ones are dropped) |

Font choice: GDI face name, weight and italic go to the font provider like a document run's. A face that is not installed is reported once as `render.metafile-font` and the provider's stand-in is used (Arial → Liberation Sans, metric-compatible). A GDI cell height (positive `lfHeight`) is converted to an em size with the face's ascender and descender, as the interpreter does.

## What the display list cannot express

Binary and ternary raster operations other than copy (XOR, AND, invert, `SRCINVERT`…), triangle gradients, pens made from hatch or pattern brushes, and union or exclusive-or of clip regions have no display-list form. When a picture contains any of them the **whole picture** is drawn as one bitmap through OneImage (`MetafileFormat` at twice the destination size, at most `MaxRasterSide` = 4096 px), and `render.metafile-rasterised` names the reason. A half-vector picture with a wrong region looks worse than a clean bitmap, and these records are rare in Office pictures. Setting `AllowRasterFallback = false` draws the vector approximation instead: raster operations as plain copies, triangle gradients as the average colour, union/xor clips as replacements (`render.metafile-approximated`).

Not yet: right-to-left text order inside a `draw-text` run (the text is drawn in logical order), `ETO_GLYPH_INDEX` text beyond treating the indices as code points (as the interpreter does), vertical advances, and stroke placement refinements GDI applies to cosmetic pens (a half-pixel shift measured by OneImage; the vector output can differ from GDI by one pixel along edges).

## Checked against Windows

`testdata/metafile` holds 37 EMF files written by Windows (25 recorded with GDI calls, 12 GDI+ "dual" files) and the picture Windows GDI drew for 25 of them at 256 × 256 (see its `NOTICE.md`). `MetafileTests.AgreementWithWindows` draws each with the adapter, rasterises the display list with OneRender's image backend at 256 × 256 on white and counts pixels whose channels all differ by at most 48 (the same measure OneImage uses). The measure is information; the test only guards a floor (every case ≥ 80 %, mean ≥ 95 %).

| Case | Agreement % | Drawn as | Case | Agreement % | Drawn as |
|---|---|---|---|---|---|
| alphablend | 99.70 | vector | polys | 97.63 | vector |
| arcs | 98.20 | vector | rect_basic | 98.25 | vector |
| beziers | 99.20 | vector | regions | 97.63 | bitmap (DSTINVERT) |
| bitblt_dc | 98.59 | bitmap (SRCINVERT) | rop2 | 94.77 | bitmap (raster ops) |
| clip_path | 98.48 | vector | state_stack | 98.16 | vector |
| clip_rect | 99.17 | bitmap (union/xor clip) | stock_objects | 94.54 | vector |
| dib_pattern | 96.02 | vector | stretchdibits | 95.66 | bitmap (SRCINVERT) |
| geom_pens | 97.29 | bitmap (hatch pen) | text_basic | 95.72 | vector |
| gradient | 98.96 | bitmap (triangles) | text_styled | 96.06 | vector |
| hatch | 90.14 | vector | world_xform | 98.10 | vector |
| lines_pens | 97.72 | vector | mapmodes | 97.04 | vector |
| mmtext_raw | 97.18 | vector | paths | 96.46 | vector |
| pixels_misc | 99.12 | bitmap (XOR) | | | |

Mean 97.19 over the 25 cases. The weakest: `hatch` (the diagonal lines are anti-aliased where GDI draws hard one-pixel lines, and the picture is scaled by 2/3 so their phase differs), `stock_objects` and `rop2` (GDI draws wide-pen overlaps and stock pens its own way), `text_basic`/`text_styled` (Arial is drawn in Liberation Sans, which has the same advances but different outlines and hinting), and the bitmap-drawn cases, which carry the interpreter's own differences from GDI.

Not checked here, and needed before 1.0: Word, Excel and PowerPoint's own rendering of pictures with metafiles on the layout differential. The picture painter for inline and anchored pictures calls the hook when it lands; documents with embedded EMF pictures join the differential inputs then.
