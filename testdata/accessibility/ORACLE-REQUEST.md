# Accessibility Checker cases for Office (unanswered)

Run once at the final gate on the Office oracle machine. Nothing here has been answered.

Each rule of OneDocument's accessibility checker has a pair of generated files per kind it applies to: `<rule>.positive.<ext>` should make the rule fire and
`<rule>.negative.<ext>` should not. They are written by the `AccessibilityTests` classes of the three test projects when `ONEDOCUMENT_A11Y_FIXTURES`
names a directory.

For every file, in the application that owns it (Word for `.docx`, Excel for `.xlsx`, PowerPoint for `.pptx`):

1. Open it. Record whether a repair prompt appears.
2. Run Review > Check Accessibility. Record every item in the Inspection Results pane: its category (Errors, Warnings, Tips, Intelligent Services), its rule
   title, and what it points at (the object, cell range, slide or paragraph).
3. Compare with the rule in the file name. The question for each pair is the same: does Office report the same category for the positive file, and nothing
   for that rule in the negative one?

Questions the published rule list leaves open (each is settled by what the checker does; our current choice is in `docs/accessibility.md`):

- Word: is a table's header row the `w:tblHeader` repeat flag only, or does the "Header Row" table style option (`w:tblLook` `firstRow`) also satisfy the rule? (`table.header-row`)
- Word: how long is a "long document" for the missing-headings tip, and does a table of contents count? (`headings.missing`)
- Word: how many consecutive spaces, tabs or empty paragraphs does it report? (`spacing.blank-characters`, `spacing.blank-paragraphs`)
- Word: does it report floating text boxes, or only pictures and shapes? (`floating.object`)
- PowerPoint: what makes the reading order "not logical"? We report only a title that is not first in the shape tree. (`slide.reading-order`)
- PowerPoint: does it check hidden slides and objects hidden in the Selection Pane?
- Excel: does it report every merged range or only those inside tables and data ranges? (`table.merged-cells`)
- Excel: does it report red-only formats on cells with no negative value? (`number-format.red-only`)
- All: does it treat the `title` attribute of a picture as alternative text? We do. Does it flag alternative text that ends in a file extension the way we match it? (`alt-text.file-name`)
- All: contrast thresholds: 4.5:1, and 3:1 for 18 pt or 14 pt bold; which colour does it take for text with no colour on a shaded cell? (`contrast.low`)

Rule to file:

| Rule | Files |
|---|---|

| `alt-text.file-name` | `alt-text.file-name.negative.docx`, `alt-text.file-name.negative.pptx`, `alt-text.file-name.negative.xlsx`, `alt-text.file-name.positive.docx`, `alt-text.file-name.positive.pptx`, `alt-text.file-name.positive.xlsx` |
| `alt-text.missing` | `alt-text.missing.negative.docx`, `alt-text.missing.negative.pptx`, `alt-text.missing.negative.xlsx`, `alt-text.missing.positive.docx`, `alt-text.missing.positive.pptx`, `alt-text.missing.positive.xlsx` |
| `alt-text.suggested` | `alt-text.suggested.negative.docx`, `alt-text.suggested.negative.pptx`, `alt-text.suggested.positive.docx`, `alt-text.suggested.positive.pptx` |
| `content-control.title` | `content-control.title.negative.docx`, `content-control.title.positive.docx` |
| `contrast.low` | `contrast.low.negative.docx`, `contrast.low.negative.pptx`, `contrast.low.negative.xlsx`, `contrast.low.positive.docx`, `contrast.low.positive.pptx`, `contrast.low.positive.xlsx` |
| `document.language` | `document.language.negative.docx`, `document.language.negative.pptx`, `document.language.positive.docx`, `document.language.positive.pptx` |
| `document.title` | `document.title.negative.docx`, `document.title.negative.pptx`, `document.title.negative.xlsx`, `document.title.positive.docx`, `document.title.positive.pptx`, `document.title.positive.xlsx` |
| `floating.object` | `floating.object.negative.docx`, `floating.object.positive.docx` |
| `headings.missing` | `headings.missing.negative.docx`, `headings.missing.positive.docx` |
| `headings.skipped` | `headings.skipped.negative.docx`, `headings.skipped.positive.docx` |
| `hyperlink.text` | `hyperlink.text.negative.docx`, `hyperlink.text.negative.pptx`, `hyperlink.text.negative.xlsx`, `hyperlink.text.positive.docx`, `hyperlink.text.positive.pptx`, `hyperlink.text.positive.xlsx` |
| `media.captions` | `media.captions.negative.pptx`, `media.captions.positive.pptx` |
| `number-format.red-only` | `number-format.red-only.negative.xlsx`, `number-format.red-only.positive.xlsx` |
| `section.default-name` | `section.default-name.negative.pptx`, `section.default-name.positive.pptx` |
| `section.duplicate-name` | `section.duplicate-name.negative.pptx`, `section.duplicate-name.positive.pptx` |
| `sheet.blank` | `sheet.blank.negative.xlsx`, `sheet.blank.positive.xlsx` |
| `sheet.default-name` | `sheet.default-name.negative.xlsx`, `sheet.default-name.positive.xlsx` |
| `slide.reading-order` | `slide.reading-order.negative.pptx`, `slide.reading-order.positive.pptx` |
| `slide.title-duplicate` | `slide.title-duplicate.negative.pptx`, `slide.title-duplicate.positive.pptx` |
| `slide.title-missing` | `slide.title-missing.negative.pptx`, `slide.title-missing.positive.pptx` |
| `spacing.blank-characters` | `spacing.blank-characters.negative.docx`, `spacing.blank-characters.positive.docx` |
| `spacing.blank-paragraphs` | `spacing.blank-paragraphs.negative.docx`, `spacing.blank-paragraphs.positive.docx` |
| `table.blank-cells` | `table.blank-cells.negative.docx`, `table.blank-cells.negative.pptx`, `table.blank-cells.positive.docx`, `table.blank-cells.positive.pptx` |
| `table.default-name` | `table.default-name.negative.xlsx`, `table.default-name.positive.xlsx` |
| `table.header-row` | `table.header-row.negative.docx`, `table.header-row.negative.pptx`, `table.header-row.negative.xlsx`, `table.header-row.positive.docx`, `table.header-row.positive.pptx`, `table.header-row.positive.xlsx` |
| `table.irregular-grid` | `table.irregular-grid.negative.docx`, `table.irregular-grid.negative.pptx`, `table.irregular-grid.positive.docx`, `table.irregular-grid.positive.pptx` |
| `table.merged-cells` | `table.merged-cells.negative.docx`, `table.merged-cells.negative.pptx`, `table.merged-cells.negative.xlsx`, `table.merged-cells.positive.docx`, `table.merged-cells.positive.pptx`, `table.merged-cells.positive.xlsx` |
| `table.nested` | `table.nested.negative.docx`, `table.nested.positive.docx` |
| `watermark.image` | `watermark.image.positive.docx` |

sha256 of the committed bytes: `SHA256SUMS`.

## Office run (Word, Excel and PowerPoint 16.0.20527)

Answered as far as the oracle can drive the applications: see the row for this request in [`docs/office-oracle-1.0.md`](../../docs/office-oracle-1.0.md#the-other-requests). Questions not listed there as run are still open.
