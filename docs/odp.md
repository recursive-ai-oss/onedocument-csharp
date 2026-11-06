# OpenDocument presentations (ODP, OTP, FODP)

`Presentation.Open` accepts ODP, OTP and flat FODP by content (the `mimetype` entry or `office:mimetype`), never the extension. The file is
converted to an in-memory PPTX package, and the hub wraps it like any PPTX; `Presentation.Format` stays `Odp`, `Otp` or `Fodp`, and
`Presentation.ImportLoss` lists what did not map. `SaveAs("x.odp" | "x.otp" | "x.fodp")` (or with an explicit `OfficeFormat`) writes OpenDocument from
the hub, and `Presentation.ExportLoss` lists what it could not carry. `Office.Open`, `Office.ExtractText`, `Office.Convert` and the renderer reach
the formats through the registry like any other presentation. Direct entry point: `OpenDocumentPresentation.ToPackage` for the PPTX package.
The package layer (`OdfPackage`, styles, metadata) is the one in `odf.md`.

**Untouched files.** An ODP/OTP/FODP read and saved in its own format without an edit writes the bytes it was read from, so everything the hub
does not carry (animations, transitions, charts, embedded objects, producer extensions) survives. Any edit reaches the exporter, which writes a
valid package of its own from the hub; a save to another OpenDocument format goes through the exporter too.

## Reading (ODP → hub)

| ODF | Becomes |
|---|---|
| `style:master-page` with its page layout | one `p:sldMaster` per master page (own theme), with its background (`draw:style-name` drawing-page style), drawing content and text styles (`<master>-title`, `<master>-outline1…5` presentation styles) |
| `presentation:presentation-page-layout` used by a slide | one `p:sldLayout` per master page and page layout a slide uses: type from the objects it lists (title + subtitle → `title`, title + outline → `obj`, title only → `titleOnly`, nothing → `blank`), placeholders with the geometry the page layout gives |
| `draw:page` | `p:sld`: name, master/layout, `presentation:visibility="hidden"` → `show="0"`, background from the page's drawing-page style |
| frames with `presentation:class` `title`, `subtitle`, `outline`, `text` | placeholders `title`, `subTitle idx=1`, `body idx=1,2…` (in order of appearance, matching the layout) |
| `date-time`, `footer`, `page-number` frames of the master page | placeholders on the slide when the page style turns them on (`presentation:display-footer` and friends); the footer and date text come from the `presentation:footer-decl` / `date-time-decl` the slide names |
| `presentation:notes` | a notes slide (body placeholder with the `notes` frame's text) and one notes master |
| `draw:rect`, `ellipse`, `circle`, `caption`, `custom-shape` | `p:sp` with a preset geometry: `ooxml-<name>` types (the adjust values are the `draw:modifiers`), `mso-spt<N>`, `flowchart-*` and the standard names (`rectangle`, `round-rectangle`, `right-arrow`…) map; `draw:rect` with a corner radius is `roundRect` |
| `draw:polygon`, `polyline`, `path` | `a:custGeom` from `draw:points` / `svg:d` and `svg:viewBox` (M L H V C S Q T Z, absolute and relative; arcs become straight lines) |
| `draw:line`, `draw:connector` | `p:cxnSp` (`line`, `straightConnector1`, `bentConnector3`, `curvedConnector3`) |
| `draw:g` | `p:grpSp` with the box of its children |
| `draw:frame` with `draw:text-box` | a text box or placeholder; with `draw:image` a `p:pic` (PNG, JPEG, GIF, BMP, TIFF, EMF, WMF; zip entries or inline `office:binary-data`); with `table:table` a graphic frame |
| `draw:transform` (`rotate`, `translate`) | `rot` and the position of the unrotated box (ODF rotates about the top-left corner, PresentationML about the centre) |
| graphic and presentation styles | solid, gradient (linear, axial, radial as a two or three stop gradient) and bitmap fills with opacity; lines (width, colour, dash, caps, joins, arrows); shadows (offset, blur, colour, opacity); text insets, anchor, no-wrap, shrink-to-fit (`a:normAutofit`) and grow-to-fit (`a:spAutoFit`) |
| `text:p`, `text:h`, `text:span`, `text:a`, `text:s`, `text:tab`, `text:line-break`, `text:page-number`, `text:date` | paragraphs, runs, hyperlinks (external only), line breaks, slide-number and date fields; white space collapses as Part 1 §6.1.2 says |
| text and paragraph properties | size, bold, italic, underline, strike, colour (`use-window-font-color` takes black or white to contrast with the fill), highlight, fonts (latin, Asian, complex through the font-face declarations), super/subscript, caps, spacing, language; alignment, margins and indent, line height, spacing before and after, right-to-left |
| `text:list` with list styles | bullets (`a:buChar`, with font, size and colour) and numbering (`a:buAutoNum` from `style:num-format`, prefix, suffix and `text:start-value`); level geometry from the label-alignment or the older space-before / minimum label width form; nesting is the paragraph level. Paragraphs outside a list get `a:buNone`, so they do not pick a bullet up from the master |
| `table:table` | `a:tbl` with column widths and row heights from the styles, spans (`gridSpan`, `rowSpan`, `hMerge`, `vMerge` for covered cells), header rows (`firstRow`), cell fill, borders, padding and vertical alignment; repeat counts are capped |
| `meta.xml` | `docProps/core.xml`: title, subject, creator, keywords, description, last modified by, dates, language |

Every shape is written with direct formatting resolved from its style chain (the shape's own style, the presentation or graphic style it names,
the frame's text style, the paragraph and span styles, then the family defaults), so the PPTX does not rely on inheritance that a master page
cannot express.

### Reported as losses (`ImportLoss`)

| Code | What |
|---|---|
| `loss.odp-animations`, `loss.odp-transitions` | animations (`anim:*`) and transitions (`smil:*`, `presentation:transition-*`), counted in slides; they stay in the file for an unchanged save |
| `loss.odp-embedded-object`, `loss.odp-object`, `loss.odp-object-ole`, `loss.odp-plugin`… | charts, formulas and other embedded objects (the preview picture is kept when there is one), plug-ins, controls, measure lines |
| `loss.odp-custom-geometry` | custom shapes whose geometry is a formula-driven path: drawn as rectangles |
| `loss.odp-fill-hatch` | hatch fills |
| `loss.odp-picture-crop`, `loss.odp-picture-format`, `loss.linked-images`, `loss.pictures` | cropping (it needs the picture's size, and the picture is never decoded), StarView and other formats PresentationML cannot hold, linked pictures (never fetched), pictures the file does not hold |
| `loss.odp-internal-links`, `loss.odp-notes`, `loss.odp-comments`, `loss.odp-ruby`, `loss.odp-inline-shapes`, `loss.odp-picture-bullets`, `loss.odp-caption`, `loss.odp-ellipse-*`, `loss.odp-path-arcs`, `loss.odp-regular-polygon`, `loss.odp-line-text`, `loss.odp-page-sizes` | links to slides, footnotes, comments, ruby, shapes anchored in text, picture bullets, caption callout lines, partial ellipses, path arcs, regular polygons, text on lines, master pages whose page sizes differ (the first master's size is used) |
| `loss.unmapped-markup.<name>` | any other drawing element, counted |

## Writing (hub → ODP)

Masters and layouts become master pages (one for each layout, named after it, holding the layout's placeholders with resolved geometry, the
non-placeholder shapes of the master and the layout, and the background). Slides become `draw:page` with their master page, name, hidden state,
background and notes. Shapes keep their resolved geometry (placeholders take it from the layout), rotation (`draw:transform`), fill, line, shadow
and text formatting; presets with no ODF counterpart of their own are `ooxml-<name>` custom shapes with the adjust values as modifiers, and
`custGeom` paths are `draw:path`. Bullets and numbers become list styles (one per distinct bullet and level geometry; a numbered list that comes
back after other lists continues counting unless the paragraph restarts it). Tables keep column widths, row heights, spans, cell fill, borders and
padding; cells with no fill of their own are written transparent, so no reader paints its default table design on them. Fonts are declared in
the font-face declarations. Pictures go to `Pictures/` (or inline base64 in FODP). `meta.xml` carries the core and custom properties.

### Reported as losses (`ExportLoss`)

`loss.odp-export-transitions`, `-animations`, `-comments`, `-charts`, `-diagrams`, `-ole`, `-graphic-frames`, `-table-styles` (the banding and header
colours a table style gives; formatting on the cells is kept), `-header-rows` (written as ordinary rows: LibreOffice drops the rows of
`table:table-header-rows` when it opens a presentation), `-crop`, `-flips`, `-group-rotation`, `-path-arcs`, `-pattern-fills`, `-gradient-stops` (first
and last colour), `-picture-bullets`, `-numbering-schemes`, `-slide-links`, `-hidden-shapes`, `-unpositioned`, `-unknown-shapes`, `-empty-geometry`,
`-deep-groups`.

## Defects

`odf.not-presentation` (fatal), `odf.damaged` (fatal), `odf.export-failed` (fatal), `odp.no-master-page`, `odp.missing-master-page`, `odp.bad-viewbox`,
`odp.bad-path`, `odp.empty-table`, `odp.missing-fill-image`, `odp.group-too-deep` and the shared `odf.repeat-capped`, `odf.missing-picture`,
`odf.bad-base64`, `odf.bad-date` (see `defects.md`). The limits object passes through: part count, size, XML depth, recursion depth (nested lists
and groups). A table is read up to 50,000 cells.

## Checked against

ODF 1.3 RelaxNG with Jing (`tools/validate/validate_odf.py`) on every deck of `testdata/` written as ODP and FODP; LibreOffice opens the written files
and exports the same number of pages as it does for the PresentationML deck (`tools/odp/lo-open-check.sh`); the ODP files LibreOffice writes from
the same decks (`tools/odp/make-fixtures.sh`, committed in `testdata/odp`) read back with the same slides, text, notes, hidden slides, tables and
pictures. PowerPoint opening our ODP, and PowerPoint's own ODP conversion, are unanswered (`testdata/odp/ORACLE-REQUEST.md`).
