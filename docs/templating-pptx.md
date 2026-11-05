# Templating: presentations

Namespace `RecursiveAI.OneDocument.Presentations.Templating` in the presentations package. `PresentationTemplate.Render(presentation, data, options)` fills a template presentation in place; `Validate` checks it without data; `Render(Stream template, data, Stream output)` opens, renders and writes. The engine, syntax, data model and defect codes are in `templating.md`; this page states how the presentation binder reads a deck and rebuilds it.

## What is read as text

Every text body of the slides and their notes pages is read, one **unit** per paragraph (`a:p`) and one **segment** per run (`a:r`), line break (`a:br`, a vertical tab) or field (`a:fld`):

* shapes and placeholders (`p:sp`), shapes inside groups, shapes in the first `mc:Choice` of an alternate-content wrapper;
* table cells (`a:tc`), at any position of a row;
* the notes page's shapes.

A tag split across runs (spelling marks, several edits, a different format half way through a tag) is found. What a tag prints takes the run properties of the run **where the tag began**; text after the tag keeps its own run's properties. A paragraph that comes out as it went in is copied byte for byte (so a deck with no tags renders to identical parts); a changed paragraph is rebuilt from `a:pPr`, one run per piece with that run's `a:rPr`, and `a:endParaRPr`. A line feed in a printed value becomes `a:br`. A field (`a:fld`) whose text is unchanged is kept as a field.

Not read: chart and diagram text, `mc:Fallback` branches, `a14:m` equations, shape names and alt text, text in layouts and masters. A changed paragraph drops children other than the ones named above (they only occur in equation paragraphs).

Besides the paragraphs, the binder hands the engine **marker units** (empty text that print nothing): one per slide, one per top-level shape of the slide and of its notes page, and one per table row. They are what lets a block repeat or drop a whole slide, shape or row. A hyperlink on a run whose external target contains a tag (`mailto:{{ c.email }}`) adds one more unit after its paragraph holding the target text.

## Scopes

A block repeats the units between its tags. The binder widens that to whole structures:

| Tags stand in | The block repeats |
|---|---|
| one paragraph | inline text, nothing new |
| different paragraphs of one text body (a cell included) | the paragraphs between them |
| different cells or rows of one table | whole table rows, from the row of the opening tag to the row of the closing tag |
| different top-level shapes of one slide | whole shapes, from the shape of the opening tag to the shape of the closing tag |
| different slides, or a slide and its notes | whole slides, from the slide of the opening tag to the slide of the closing tag (notes and sections follow) |

`scope=` overrides: `slide` (the slides of both tags, or one slide), `table-row`, `table` (the shapes holding the tags), `paragraph` / `list-item` (whole paragraphs: `{% for x in xs scope=paragraph %}{{ x }}{% endfor %}` in one paragraph repeats the paragraph), `inline`. `sheet`, `row`, `column` and `section` do not apply: `pptx.template-scope-unsupported`, and the structure decides. `if` blocks use the same ranges, so a false block with `scope=slide` removes the slide and a false block over table rows removes those rows.

**Shape removal.** A shape whose text is only a false block's content is removed: when a top-level text shape's text contains a block tag, has text, and every paragraph comes out empty (or none remain), the copy is dropped. A shape with only `{{ missing }}` stays. A table whose rows all went is removed too.

## Slides

Cloning uses `Presentation.DuplicateSlide` on the untouched template slide, so each copy keeps its layout, notes page (copied), media (shared), comments (copied), section and deterministic ids. All copies are made first, all shapes are rebuilt from the template's markup, then the slides are written, unused template slides deleted, and the slides moved into output order. `OfficeLimits.MaxPartCount` bounds the number of output slides.

Copies of a shape or table row get fresh shape ids (`p:cNvPr/@id`, next free ids in output order) and fresh `a16:rowId` values; the first copy keeps its ids.

## Data-fed objects

* **Pictures.** `{% image expr %}` as the only content of a text shape (placeholders included) on a slide replaces the shape by a `p:pic` with the shape's id, name, placeholder link (`p:nvPr`), geometry (`prstGeom`/`custGeom`) and box. The value is image bytes or base64 text (`data:` URIs accepted); PNG, JPEG, GIF and BMP are recognised by their header and never decoded. Options: `fit=contain` (default: the picture is fitted inside the box and centred), `fit=cover` (the box is filled and the picture cropped with `a:srcRect`), `fit=stretch`, and `alt=` for the description. A placeholder without its own transform takes the box its layout gives. Identical bytes share one media part (`/ppt/media/imageN.ext`); each copy has its own relationship. Anywhere else (inside other text, table cells, notes, groups) the tag prints nothing: `pptx.template-image-position`. An undecodable value leaves the shape empty: `pptx.template-image-invalid`.
* **Hyperlinks.** An external hyperlink whose target holds a tag gets one relationship per copy with the rendered target (spaces become `%20`); the template's relationship is removed when nothing uses it. The paragraph with the opening tag of a surrounding block does not rebind its own links; put the link in a paragraph inside the block.
* **Charts.** Not bound. The presentation model keeps chart parts as stored and has no chart model to write series through; chart data replacement is a documented gap until the chart model exists.
* **HTML and Markdown tags** insert their value as plain text (`pptx.template-injection-unsupported`).

## Text fit

Text that grows or shrinks is not refitted: `a:normAutofit` and every other body property stay as written, and shape sizes (including table frame heights) are not recomputed. PowerPoint refits on open or edit; rendering does it when the layout stage exists.

## Corpus

`testdata/templates/pptx/NAME/` holds `template.pptx`, `data.json` and `expected/` (the slide parts, notes parts, their relationships, media, `ppt/presentation.xml`, its relationships and `[Content_Types].xml` of the rendered output). The tests render each template again and compare character for character. Regenerate with `ONEDOCUMENT_UPDATE_GOLDEN=1` when behaviour changes on purpose. Templates are written from descriptions by the test project (split runs, proofing marks, empty end-of-paragraph properties, row ids). `OracleCases --pptx-templates CORPUS OUT` renders the corpus for a desktop application to open.
