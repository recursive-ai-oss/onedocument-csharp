# Templating: word-processing documents

Namespace `RecursiveAI.OneDocument.Documents.Templating` in the documents package. The engine, syntax, data model and defect codes are in `templating.md`; this page states how the document binder reads a document, what the blocks repeat, and what the data-fed tags insert.

`Template.Render(templatePath, data, outputPath)` opens the file, finds the binder by its sniffed format, renders in place and saves in the format of the output extension. `Template.Render(Stream, data, Stream, OfficeFormat?)` does the same over streams, `Template.Render(OfficeFile, data)` renders an open file, and `Template.Validate(path)` returns the located errors without data. The CLI is `onedocument template <template> <data.json> -o <out> [--strict] [--validate]`. A `.dotx`/`.dotm` template renders like a `.docx`; saving to `.docx` writes the document content type.

## What is read as text

Every story is bound: the body, headers and footers, footnotes and endnotes, comments, text boxes (rendered as stories of their own, innermost first), and the content of content controls. A paragraph (`w:p`) is one **unit** and each text run is one **segment**. Everything else in the paragraph is kept as opaque items between the segments (bookmarks, proofing marks, `w:rsid` noise, fields, drawings, tracked-change wrappers, `mc:AlternateContent`), with their wrapper chains (`w:ins`, `w:del`, `w:hyperlink`, `w:smartTag`, `w:sdt`) intact.

A tag split across runs (spell-check marks, several edits, a formatting change halfway through) is found. What a tag prints takes the run properties of the run **where the tag began**; text after the tag keeps its own run's properties. A story with no tag is not rewritten, so a document without tags renders to identical parts. A changed paragraph keeps its `w:pPr` and unknown attributes.

## Scopes

| Tags stand in | The block repeats |
|---|---|
| one paragraph | the runs between the tags (inline) |
| different paragraphs of one story or cell | the paragraphs between them, with list numbering continuing |
| different cells of one table row, or in rows holding only tags | whole table rows (`table-row`, inferred) |
| different sections | whole sections (`section`) |

`scope=` overrides: `table-row` (the row holding the tags), `table` (the outermost tables holding them), `section`, `paragraph`, `inline`. A table row that holds only block tags is removed from the output, and a table whose rows all went is removed. A cell left empty gets one empty paragraph, as Word requires. A repeated final section gets section breaks so each copy keeps its page setup.

A scope the structure cannot honour (`scope=table-row` or `table` outside a table) reports `template.scope-unusable` and repeats the paragraphs between the tags instead.

## Identifiers

Copies must stay valid, so repeated content gets fresh, deterministic identifiers (the first copy keeps its own):

* bookmark ids and names (a numeric suffix), comment ids (the comment and its range marks are cloned, with the comment text in `comments.xml`), footnote and endnote ids (the note is cloned in its part), `wp:docPr/@id` and `w14:paraId`.
* Nothing depends on the clock or on randomness: the same template and data give the same bytes.

## Data-fed objects

* **Pictures.** `{% image expr %}` replaces the tag's run with an inline picture in a run with the properties of the run where the tag began. The value is bytes, a stream, base64 text or a `data:` URI; file paths are never read. PNG, JPEG, GIF and BMP are recognised by their header and never decoded. Options: `width=`, `height=` (a number is pixels at 96 dpi; units `px`, `pt`, `in`, `cm`, `mm`, `emu`), `fit=contain|stretch` (default `contain`: with both `width` and `height` the picture keeps its aspect ratio inside the box; `stretch` fills it; with one of them the other follows the aspect ratio; with neither the picture keeps its pixel size, at most 6 inches wide), `alt=` (alternative text). A value that is not a picture prints nothing and reports `template.image-unsupported`.
* **Hyperlinks.** `{{ link(url, text) }}` inserts a `w:hyperlink` with an external relationship and the `Hyperlink` character style (added when the document has none). Allowed schemes are `http`, `https`, `mailto`, `tel` and `#anchor` (an internal link to a bookmark); anything else prints the text as plain text.
* **HTML and Markdown.** `{% html expr %}` and `{% markdown expr %}` insert block content in place of the paragraph that holds the tag; the tag's own text before and after stays in paragraphs of its own. Supported: paragraphs, headings (`Heading1`..`Heading6`), bold, italic, underline, strike-through, inline code, links (allowed schemes as above), line breaks, block quotes (`Quote`), and bullet and numbered lists to any depth (the numbering definitions are added to the document on demand). The HTML is read by OneHtml's parser and converted through an allowlist, which is the sanitiser: scripts, event attributes, unknown elements, `javascript:` and other disallowed links, images, and table structure are dropped (their text content is kept). Markdown is a CommonMark subset (ATX headings, emphasis, strong, code spans and fenced blocks, links, lists, block quotes; no raw HTML, images or thematic breaks). Anything left out is listed in one `template.markup-dropped` warning per tag. The full importers are separate (see the ODF/HTML/Markdown work).

## Defects

`template.scope-unusable` (repaired), `template.image-unsupported` and `template.markup-dropped` (dropped), in `defects.md`. They are returned with the engine's warnings by `Template.Render`; in strict mode the engine's missing-data and expression errors are fatal as described in `templating.md`.

## Known gaps

* VML shape ids (`v:shape/@id`, `o:spid`) in repeated content are not renumbered; DrawingML ids are.
* `docProps/core.xml` and `docProps/app.xml` are not templated.
* Strict-namespace documents are rendered with Strict names for the markup the binder writes, but no golden covers them.
* Fields are not updated; cached results of a repeated field are copied as they are.
* Tags in alternative-content fallback branches, shape names and chart text are not read.

## Corpus

`testdata/templates/docx/NAME/` holds `template.docx`, `data.json` and `expected/` (the main part, rewritten stories, notes, comments, relationships and `[Content_Types].xml` of the rendered output). The tests render each template again and compare character for character. Regenerate with `ONEDOCUMENT_UPDATE_GOLDEN=1` when behaviour changes on purpose.
