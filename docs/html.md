# HTML import and export

HTML reads into the word-processing hub and the hub writes it back. Both directions report what they could not carry.

```csharp
using Document doc = Document.Open("page.html");            // sniffed by content: <!DOCTYPE html>, <html>, <head>, <body> or a tag such as <p>, <h1>, <table>
using Document frag = Document.FromHtml("<h1>Title</h1><p>Hello <b>world</b></p>");
LossReport? lost = doc.ImportLoss;                        // scripts, handlers, unsafe links, forms, frames, external images ...
Office.Convert("page.html", "page.docx");                 // and "page.docx" -> "page.html"
string html = doc.ToHtml();                               // a page; new HtmlExportOptions { Fragment = true } for the content only
doc.SaveAsHtml("out.html", new HtmlExportOptions { ImageSink = i => { File.WriteAllBytes(i.SuggestedName, i.Data.ToArray()); return i.SuggestedName; } });
```

`Workbook.ToHtml` and `Presentation.ToHtml` (and `SaveAs("x.html")` on them) write a basic export, below.

## Import

The page is parsed as a browser parses it (OneHtml: encoding from the byte-order mark or `meta`, tree construction, error recovery) and its cascade
(user-agent rules, `style` elements, `style` attributes, legacy attributes such as `align`, `bgcolor`, `border`, `font`) supplies a computed style per
element. The importer reads those computed values; it does not interpret CSS itself. The result is an in-memory DOCX package that the hub wraps.

| HTML | Becomes |
|---|---|
| `h1`..`h6` | `Heading1`..`Heading6` (outline levels 0..5) with the formatting of the first heading of each level |
| `p`, `div`, `section`, `li`... with inline content | paragraphs; text directly in a block is an anonymous paragraph with the block's style |
| `class` | a named paragraph style per first class (`.note` becomes style `note`); the first paragraph with the class defines what the style holds, later ones carry only what differs. Classes written by the exporter (`s-<styleId>`, base first) read back as those styles with their `basedOn` chain |
| `b`/`strong`, `i`/`em`, `u`, `s`/`del`, `sup`, `sub`, `small-caps`, `color`, `font-size`, `font-family`, `background-color` (inline), `lang` | run properties; only what differs from the paragraph's style is written |
| `text-align`, `margin-*` and `padding-left/right` of containers, `text-indent`, `line-height`, `border-*`, `background-color` on a block | paragraph alignment, indents (nested blocks add up), spacing before (adjacent margins collapse as in CSS: the larger wins), line spacing, borders, shading |
| `ul`, `ol`, `type`, `start`, `list-style-type` | numbering definitions; one `w:num` per list element, so each ordered list restarts at its `start` (`startOverride`); nesting is the level; a second paragraph in an item continues it without a marker |
| `table`, `thead`, `tbody`, `tfoot`, `tr`, `th`, `td`, `caption`, `col` | tables: `colspan` is `gridSpan`, `rowspan` is `vMerge`, `thead` and leading all-`th` rows are repeated header rows, widths (px, pt, %) become the grid and `tblW`, cell borders, shading, padding and vertical alignment are kept, `border` and `align` on the table too; a caption is a paragraph before the table |
| `a href`, `a name`, `id` | hyperlinks (external relationships or `w:anchor`) and bookmarks. Only ids that a `#fragment` link names become bookmarks; names are made valid for Word (letters, digits, underscore, at most 40 characters) |
| `img` with a `data:` URI | an inline picture (PNG, JPEG, GIF, BMP read from the header; the size comes from `width`/`height` attributes or CSS, else the picture's pixels, capped to the text width); `alt` becomes the description |
| `pre`, `white-space: pre*` | text with preserved spaces, tabs and line breaks; `br` is a line break; `code`/monospace families map to Courier New |
| `blockquote` | style `Quote` and the browser's indent |
| `hr` | an empty paragraph with a bottom border |
| `page-break-before/after`, `break-before/after` (page, left, right, always) | `pageBreakBefore` on the next paragraph |
| `dir`, CSS `direction` | `bidi` on paragraphs, `rtl` on runs of right-to-left script, `jc` swapped to the logical side |
| `title`, `meta name=author/description/keywords` | document properties |

White space collapses as CSS says (runs of white space become one space, leading space in a block and trailing space at its end are dropped, a space
between inline elements is kept once).

### Sanitising

Import is `HtmlSanitization.Strict` unless told otherwise. Nothing is ever executed or fetched. Each of these is left out and reported once per occurrence
in the loss report and as an `html.dropped` defect with the text position of the element:

| Loss code | What |
|---|---|
| `loss.html-script` | `script` elements (their text is not kept) |
| `loss.html-event-handler` | attributes starting `on` (never read) |
| `loss.html-unsafe-link` | link targets other than `http`, `https`, `mailto`, `tel`, `ftp`, `ftps`, `sms`, relative targets and `#fragments`; the check ignores the tabs, line feeds and spaces a browser ignores inside the scheme, so `java\tscript:` is `javascript:`. The link text stays |
| `loss.html-form`, `loss.html-form-control` | the `form` wrapper (its text and blocks stay) and `input`, `button`, `select`, `textarea`, `option`, `output`, `meter`, `progress` (with their content) |
| `loss.html-embedded-content` | `iframe`, `frame`, `frameset`, `object`, `embed`, `applet` |
| `loss.html-media` | `canvas`, `audio`, `video` |
| `loss.html-svg`, `loss.html-math` | inline SVG and MathML |
| `loss.html-external-image` | an `img` whose `src` is not a `data:` URI and that the resolver did not supply; its `alt` text stays |
| `loss.html-image-format` / `loss.html-image-unreadable` | a picture that is not PNG, JPEG, GIF or BMP, or a `data:` URI that does not decode |
| `loss.html-linked-stylesheet` | `link rel=stylesheet` (never fetched) |
| `loss.html-hidden` | `display: none` and the `hidden` attribute |
| `loss.html-depth`, `loss.html-table-size` | content nested deeper than `OfficeLimits.MaxXmlDepth`, or tables past 200,000 cells |

`HtmlImportOptions.Sanitization = HtmlSanitization.KeepInert` keeps the raw-but-safe superset: the text of form controls (button labels, field values, option
text) and the fallback text of frames and objects as plain text, and links with other schemes. It never admits scripts, handlers or `javascript:`,
`vbscript:`, `data:`, `blob:` and `file:` targets, because a document cannot hold them. `HtmlImportOptions.ImageResolver` is the only way an external
`img` is read: the importer hands it the `src` as written and uses the bytes it returns; it never opens a URL itself.

### Limits

`OfficeLimits.MaxPartSize` bounds the input; `MaxXmlDepth` bounds element nesting (the parser stops at that many open elements with an
`OfficeLimitException`) and the importer's recursion; `MaxXmlAttributeCount` bounds attributes per tag; `MaxRecordCount` bounds the cells of one sheet in the
workbook export. Embedded pictures are bounded by `MaxPartSize`. The importer reads no file, no URL and no style sheet it was not given.

### Templates

The template engine's `html` tag runs on this importer: the same parse, cascade and sanitising, flattened to the small vocabulary a tag can insert into
an existing document (paragraphs, headings, bold, italic, underline, strike, code, links, breaks, quotes and nested lists). Tables, pictures and
colours in the value are dropped and reported as `template.markup-dropped`.

## Export

`Document.ToHtml` writes UTF-8 HTML5, deterministic and self-contained:

| Document | HTML |
|---|---|
| paragraph styles and character styles | CSS classes `s-<styleId>` in one `style` element; a style based on another lists both classes, base first; direct formatting is an inline `style` on the paragraph or a `span` |
| `heading N` styles (by name, through `basedOn`) | `h1`..`h6` |
| bold, italic, underline, strike-through, super/subscript | `strong`, `em`, `u`, `s`, `sup`, `sub`; colour, size, font, shading, caps in a `span` |
| numbered paragraphs | `ol` / `ul` nested by level; `start`, `type` (`a`, `A`, `i`, `I`), bullet kind from the level text; numbering continues where it left off if the list is interrupted |
| tables | `table` with `colgroup`, `thead`/`th` for repeated header rows, `colspan`, `rowspan`, borders (direct, then the table style's), shading, widths, vertical alignment, cell margins |
| hyperlinks, bookmarks, `HYPERLINK` fields | `a href`, `span id`; targets keep the import allow-list |
| footnotes and endnotes | a `sup` link per reference (numbered by CSS counter, so no text is added to the body) and one `section role="doc-endnotes"` ordered list at the end with back links |
| pictures | `img` with `alt`, `width` and `height`; a `data:` URI, or the URL the `ImageSink` returns (the sink gets a unique name, the media type and the bytes; returning null keeps the picture inline). EMF, WMF and TIFF are not shown by browsers: their alt text is written and `loss.html-metafile` counts them |
| page and section breaks | an empty `div` with `break-after: page`; the paragraph is split at an inline page break |
| right-to-left paragraphs and runs, languages | `dir="rtl"`, `lang`; `html lang` from the document default |
| text boxes | blocks after the paragraph they were anchored in (`loss.html-text-boxes`) |
| empty paragraph | `<p><br></p>` |

The style sheet puts `white-space: pre-wrap` on paragraph elements, so the spaces and breaks of the document are exactly those of the HTML.

Not written, each a loss item on `Document.ExportLoss`: `loss.html-headers-footers` (HTML has no pages), `loss.html-comments`, `loss.html-revisions`
(deleted text; inserted text is written as plain text), `loss.html-equations` (plain text), `loss.html-charts`, `loss.html-drawings` (shapes, SmartArt, OLE objects
without a picture), `loss.html-floating-pictures` (written in the line), `loss.html-text-boxes`, `loss.html-metafile`, `loss.html-unsafe-link`.

The page this writes reads back with `Document.FromHtml` to the same text, the same headings and styles, the same lists (levels, kinds, starts), the same
tables (grid, spans, merges, header rows, shading) and the same bookmarks and links. Notes come back as an ordered list at the end; the other losses above
stay lost.

### Workbooks and presentations

`Workbook.ToHtml` writes one `table` per visible worksheet under an `h2` with the sheet name. Values are shown as Excel shows them: numbers and dates go
through the cell's number format (the shared number-format service), text as is, booleans and errors as text. Numbers are right-aligned (`td.n`).
Merged ranges are `colspan`/`rowspan`. Not written: styles other than the number format, column widths, hidden sheets (`loss.html-hidden-sheets`),
chart sheets, formulas (cached results are written, `loss.formulas`), drawings, charts, comments and pivot tables (`loss.embedded-objects`).

`Presentation.ToHtml` writes a `section` per slide: the title placeholder as `h2`, other text shapes as paragraphs (indented by level) in shape order,
tables as tables, notes in an `aside`; hidden slides carry `hidden`. Not written: positions and sizes, pictures, charts and other graphic frames,
animations, transitions, masters and layouts (`loss.html-slide-layout`, `loss.html-pictures`, `loss.html-graphic-frames`, `loss.html-animations`).

## Defects

`html.dropped` (one per left-out element, with its offset), `html.damaged` and `html.export-failed` (wrapped unexpected failures); limits raise
`OfficeLimitException`. See `defects.md`.

## Not here

MHTML and single-file archives, CSS paged media (`@page`, running headers), `float`/`position` layout, `<svg>` and `<canvas>` content, Markdown (its own
importer), and any rendering of HTML through OneRender's CSS engine: pages are mapped to the document model and laid out by the document layout core.

## Oracles

LibreOffice opens the exported HTML (`soffice --convert-to`); Word's reading of our HTML, and Word's own HTML export of the same documents, are
recorded for the desktop-Office run in `testdata/html/word-cases.md`.
