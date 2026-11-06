# Markdown

CommonMark 0.31.2 with the GitHub Flavored Markdown extensions, into and out of the document hub, and out of workbooks and presentations.

Markdown is the one format that is never recognised from content: the caller names it (`Document.OpenMarkdown`, `Document.ParseMarkdown`, `Document.Open(path, OfficeFormat.Markdown)`, `Office.Open(path, OfficeFormat.Markdown)`), or it is chosen by a file name's extension (`Office.Convert("a.md", "a.docx")`, `SaveAs("a.md")`). Text that merely looks like Markdown is plain text to `FormatSniffer`.

## The parser

`Documents/Markdown/MarkdownParser.cs` is a block parser that works line by line as CommonMark's appendix describes (each line meets the open containers, then the block starts, then lazy continuation); `MdInlineParser.cs` does the inlines with the delimiter-run algorithm of §6.2 and the bracket stack of §6.3. Both are our own code, written against the specification text. The tree is `MdNode` (internal).

Dialect switches (`MarkdownParserOptions`): tables, strike-through (one or two tildes), task lists, extended autolinks (`www.`, `http://`, `https://`, `ftp://`, e-mail, `mailto:`/`xmpp:`), footnotes (`[^1]` and `[^1]:`), raw HTML. The import uses all of them; the specification run uses `CommonMark` (all off) for the core examples.

**Limits and run time.** The parser is linear in the input:

- block containers nest at most `OfficeLimits.MaxRecursionDepth` deep (at least 8); a marker beyond that is read as text and reported as `markdown.too-deep`;
- per line, work is bounded by that depth;
- a failed search for a code-span closer, an HTML comment, processing instruction, CDATA section or declaration closer is remembered, so a text of many unclosed openers is not scanned again for each;
- emphasis uses the `openers_bottom` bounds, link deactivation walks only the part of the bracket stack not already deactivated, link destinations stop at 32 nested parentheses, and an autolink scan stops at the first space or `<`;
- no regular expressions are used in the parser.

Nesting inside inline content (emphasis in emphasis, images in images) is not limited in the tree; every walker (importer, template converter, text of an image) stops flattening at `MaxRecursionDepth`.

Character references: numeric ones are decoded in the parser; named ones go through OneHtml's table, so the whole HTML5 set is known.

## Conformance

The tests fetch the published specifications when they run (the text is CC BY-SA and is not kept in the repository):

| Specification | Where | Examples | Result |
|---|---|---|---|
| CommonMark 0.31.2 | `https://spec.commonmark.org/0.31.2/spec.json` | 652 | 652 pass |
| GFM (0.29-gfm), extension sections: Tables, Task list items, Strikethrough, Autolinks, Disallowed Raw HTML | `https://github.github.com/gfm/` (the examples are read from the page) | 28 | 28 pass |

Method: the tree is rendered to HTML in the shape the specifications print (`tests/OneDocument.Documents.Tests/MdHtml.cs`, which also applies the GFM tag filter), white space between tags is ignored (as the specifications' own runners do) and the two strings are compared. Set `ONEDOCUMENT_SPEC_DIR` to keep the downloaded files between runs; without network the tests skip. Failures by section go to `$ONEDOCUMENT_ARTIFACTS/commonmark-spec.txt` and `gfm-spec.txt`.

The GFM core sections are not run against the GFM page: that page is CommonMark 0.29, and the CommonMark 0.31.2 run covers the core.

## Import: Markdown to the hub

`Document.OpenMarkdown` builds a WordprocessingML package in memory (the pattern of the RTF and 2003 XML readers) and wraps it in the hub. The document reports `Format == OfficeFormat.Markdown` and saves as DOCX unless asked otherwise; saved unedited as Markdown it writes its source bytes.

| Markdown | Document |
|---|---|
| ATX and setext headings | styles `Heading1` to `Heading6` (outline levels 0 to 5), with a bookmark `md_<slug>` so `[x](#slug)` links land on them |
| paragraph | paragraph; a soft break is a space, a hard break is `w:br` |
| emphasis, strong, strike-through | `w:i`, `w:b`, `w:strike` on the runs |
| code span | character style `InlineCode` (Consolas) |
| fenced and indented code | one paragraph per line in style `SourceCode` (the info string is dropped: `loss.markdown-code-language`) |
| block quote | style `Quote`, indented by 720 twips per level |
| bullet and ordered lists | numbering definitions (nine levels each; bullets `•` `◦` `▪`; decimal, lower letter, lower roman); every list is its own `w:num`, an ordered list carries its start number in a `w:startOverride`; further blocks of an item are indented paragraphs; tight lists use the list style's contextual spacing, loose ones switch it off |
| task list items | a leading ☐ or ☒ in the item text |
| thematic break | an empty paragraph with a bottom border |
| tables | `w:tbl` in style `TableGrid`, first row marked as header and bold, column alignment as paragraph justification (left is the default) |
| links, autolinks | `w:hyperlink`: external ones through a relationship, `#slug` ones as an anchor; targets other than web, mail, `tel:` and relative paths are kept as text (`loss.markdown-link-target`) |
| images | embedded when the address is a `data:` URI or the caller's `MarkdownImportOptions.ImageResolver` returns bytes (PNG, JPEG, GIF, BMP); otherwise the description is kept as text and reported (`loss.markdown-image`, `loss.markdown-image-format`, `loss.markdown-image-size`). Nothing is read from a path or fetched from a URL by default. |
| footnotes | `footnotes.xml` with the separator notes, `FootnoteReference` and `FootnoteText` styles |
| front matter | a first line `---`, `key: value` lines and a closing `---` or `...`: title, author, subject, description and keywords go to the core properties; other lines are `loss.markdown-front-matter`. Text between two thematic breaks that is not YAML-shaped is not front matter. |
| raw HTML (block and inline) | read by the HTML importer's fragment reader (sanitised, nothing fetched; see `html.md`) and written with the document's own styles: a block gives headings, paragraphs, lists, quotes and code; inline tags are matched with their closing tag among the siblings (`A <b>bold</b> word`) and give runs (bold, italic, underline, strike, code, links); `<br>` is a line break; comments are ignored. What the document model does not carry from it (scripts, styles, forms, table structure, pictures) is dropped and reported as `loss.markdown-raw-html`. |

Resource use: input over `MaxPartSize` raises `OfficeLimitException`; pictures count against `MaxAllocationBytes`; invalid UTF-8 is replaced and reported (`markdown.encoding-replaced`). A UTF-8 or UTF-16 byte-order mark is read. Any other failure is a typed `OfficeFormatException` (`markdown.damaged`).

The template engine's `markdown` tag uses the same parser (`MarkdownRich`): a Markdown value in a template and a Markdown file read the same way, then pass through the template vocabulary (which drops tables, pictures, rules and raw HTML, naming them in the `template.markup-dropped` warning).

## Export: the hub to Markdown

`Document.ToMarkdown(MarkdownExportOptions?)`, `WriteMarkdown`, `SaveAs("x.md")`, `Office.Convert("a.docx", "a.md")`. `Document.ExportLoss` lists what Markdown could not say.

- **Front matter** from the core properties (title, author, subject, description, keywords) when any is set.
- **Headings** from a heading style, `Title` and `Subtitle`, or an outline level of 1 to 6 (resolved through the style chain). Bold that comes from the paragraph style is not emphasis; only direct formatting and character styles are.
- **Lists** from numbering (`Numbering.LabelBody`): bullets or numbers, nesting by level, a different list (another numbering instance) gets the other marker (`-`/`*`, `.`/`)`) so adjacent lists stay apart. A paragraph with the list style or an indent after a list item is a continuation of the item, written indented under it; so is code. Empty items keep their marker. A ☐ or ☒ prefix becomes `[ ]` or `[x]`.
- **Quotes** from the styles `Quote`, `Intense Quote`, `Block Text`; depth from the left indent in 720 twip steps.
- **Code** from paragraphs in a style whose name says code (`Source Code`, `HTML Preformatted`, `Verbatim`…): consecutive lines become one fenced block, the fence longer than any backtick run inside. Runs in a monospaced font or a character style named code are code spans.
- **Tables**: the first row is the header; alignment from the first body row's paragraphs; pipes are escaped, paragraphs of a cell are joined with `<br>`; merged cells become empty cells (`loss.markdown-merged-cells`); a table in a cell is flattened (`loss.markdown-nested-table`).
- **Links** from `w:hyperlink` (relationship or anchor), simple `HYPERLINK` fields and complex field runs; an anchor on a heading becomes `#slug` with GitHub's slug and numbering of repeated headings. Anchors elsewhere are `loss.markdown-link-anchor`.
- **Pictures**: `![alt](address)`. With `MarkdownExportOptions.ImageSink` the sink receives the bytes and returns the address; without one the address is `media/imageN.ext` and the bytes are not written (`loss.markdown-image-bytes`).
- **Notes**: footnotes and endnotes become `[^n]` references and definitions at the end, in order of reference (endnotes: `loss.markdown-endnote`).
- **Escaping**: `\ ` * [ ] < > ~` always, `_` at word edges, `|` in tables, `&` where it would start a character reference, and the starts of lines that would read as headings, list markers, setext underlines or ordered list numbers.
- **Left out and reported**: underline, colour, highlight, super- and subscript and fonts (`loss.markdown-formatting`), equations (written as linear text, `loss.markdown-equation`), shapes, text boxes, charts and diagrams (`loss.markdown-drawing`), comments (`loss.markdown-comment`), headers, footers, page and section settings (not reported: layout is not content), tracked changes (insertions kept, deletions dropped).

Round trip: every one of the 652 CommonMark examples goes through Markdown → document → Markdown → document; 642 give the same document on text, styles, list labels, table cells and link targets. The ten that differ are listed in `tests`: tabs and trailing white space inside text, empty headings (dropped), a list item whose only content is a quote or a heading, and one reference-style image.

## Workbooks and presentations

`Workbook.ToMarkdown()` writes, per worksheet, a second-level heading with the sheet name and a pipe table of the used range. Cells appear the way the number format displays them (the same text as CSV with `Formatted`); formulas write their cached results; the first non-empty row is the header; columns whose body cells are all numbers are right-aligned; rows with no cell are left out, since a blank line would end the table. Losses: formulas, cell styles, merged cells, hidden sheets, chart and macro sheets, drawings and pivot tables (`Workbook.MarkdownExportLoss`).

`Presentation.ToMarkdown()` writes a second-level heading per slide (`Slide 3: Title`), the slide's text in reading order (body placeholders as bullet lists by outline level, other text as paragraphs, tables as pipe tables), and speaker notes under `### Notes`. Bold and italic runs keep their emphasis. Pictures, shapes without text, charts, links, layout and animations are reported in `Presentation.MarkdownExportLoss`.

## Defects

`markdown.too-deep` (Limit, repaired), `markdown.encoding-replaced` (Encoding, repaired), `markdown.damaged` and `markdown.export-failed` (Structure, fatal). See `defects.md`. Loss codes: `loss.markdown-*` for documents; `loss.formulas`, `loss.cell-styles`, `loss.merged-cells`, `loss.hidden-sheets`, `loss.sheet-kinds`, `loss.embedded-objects` for workbooks; `loss.pictures`, `loss.shapes`, `loss.links`, `loss.hidden-slides`, `loss.layout` for presentations.
