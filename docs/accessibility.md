# Accessibility checking

`Document.CheckAccessibility()`, `Workbook.CheckAccessibility()`, `Presentation.CheckAccessibility()` and the one-liner `Office.CheckAccessibility(path)` check a file against Office's published accessibility rules and return an `AccessibilityReport`. Checking reads the model and edits nothing; it never recalculates a workbook.

The rules and their categories come from Microsoft's public support articles "Rules for the Accessibility Checker" and "Improve accessibility with the Accessibility Checker". The articles list rules by name and category and say little about thresholds; where they are silent the choice is ours, stated in the table, and is one of the questions put to Office's own checker (`testdata/accessibility/ORACLE-REQUEST.md`). Until that comparison is made, "agrees with Office" is a goal, not a claim.

## Results

An `AccessibilityIssue` has:

- `Rule` (`RuleId`, `Category`, `Name`, and `OfficeRule`, the name in Office's list, or null for a check OneDocument adds because Office's guidance asks for it);
- `Location`: the fields that suit the kind. Word: `Part`, `Story` (`body`, `header`, `footer`, `footnotes`, `endnotes`), `Table`, `Paragraph` (1-based, counted in document order through table cells), `Run`. Excel: `Sheet`, `Range`, `ShapeId` for drawing objects. PowerPoint: `Slide` (1-based), `ShapeId`, `Section`. `Location.Description` says it in words;
- `ObjectName`, `Message` (what is wrong) and `Suggestion` (how to fix it), in plain words;
- `Fix` and `Target` (see Fixers).

`AccessibilityReport.Skipped` lists every check that could not be made, with the reason (contrast on a gradient or picture background; an unreadable drawing part). A rule keeps at most `OfficeLimits.MaxDefects` findings (10,000 by default); the rest are counted in one `Skipped` entry for that rule. A rule that cannot be evaluated is never silently passed.

Categories follow the article: **Error** (hard or impossible to use), **Warning** (hard in most cases), **Tip** (understandable but could be better), **IntelligentServices** (generated content to review).

## Rules

`ID` is stable and never reused. "Office rule" is the name in Office's list; "added" means Office's guidance asks for the check but its list names no rule.

| ID | Category | Applies to | Office rule | What is checked | Known differences |
|---|---|---|---|---|---|
| `alt-text.missing` | Error | D W P | All non-text content has alternative text | Pictures, charts, SmartArt, groups, canvases, ink, embedded objects, VML pictures and shapes without text, media. Marked-decorative objects pass. A shape that holds text (a text box) is described by its text; a group's alternative text covers the objects inside it. Placeholders, connectors and tables are not asked for it | Alternative text is `descr`, else `title`. Office may not accept `title` |
| `alt-text.file-name` | Error | D W P | same | The text ends in a file extension of 1–5 letters/digits after a dot, or is a path | The match is ours |
| `alt-text.suggested` | Intelligent Services | D P | Suggested alternative text | The text still carries "Description automatically generated" | We cannot see which service wrote a description; the marker is what Word appends |
| `table.header-row` | Error | D W P | Tables specify column header information | Word: the first row has `w:tblHeader`. Excel: the table has `headerRowCount` 0. PowerPoint: `firstRow` is off | Word's "Header Row" table style option (`w:tblLook`) is not accepted as a header row. Excel: ranges that are not tables are not checked |
| `table.merged-cells` | Warning | D W P | Table has a simple structure | Word `gridSpan`/`vMerge`/`hMerge`; PowerPoint `gridSpan`/`rowSpan`/merge flags; Excel: merged ranges, one finding per sheet (with the count and the first range) | Excel: all merged ranges, not only those in data ranges |
| `table.irregular-grid` | Warning | D P | same | Rows with different numbers of grid columns (split cells) | Reported only when nothing is merged |
| `table.nested` | Warning | D | same | A table inside a cell of another table | |
| `table.blank-cells` | Warning | D P | added | A whole row or column of empty cells, in a table of more than one row/column | |
| `table.default-name` | Tip | W | added | Workbook table named `Table<N>` | |
| `contrast.low` | Warning | D W P | Sufficient contrast between text and background | WCAG 2.x contrast ratio: at least 4.5:1, or 3:1 for text of 18 pt or more, or bold 14 pt or more. See Contrast | |
| `document.title` | Tip | D W P | added | The `dc:title` property is empty | |
| `document.language` | Tip | D P | added | Word: no `w:lang` in the styles or the text. PowerPoint: no run with `lang` | |
| `content-control.title` | Error | D | All content control fields have titles | A content control with no `w:alias`. Building-block controls (tables of contents, cover pages) and citations/bibliographies are not form fields | |
| `headings.missing` | Tip | D | Documents use heading styles | No heading paragraph, no table of contents, and at least 500 words in the body | The length is ours |
| `headings.skipped` | Tip | D | added | A heading more than one level below the one before it. Headings are styles named "heading N" or an outline level | |
| `spacing.blank-characters` | Tip | D | added | Three or more consecutive spaces inside text, or two or more tabs | Thresholds are ours |
| `spacing.blank-paragraphs` | Tip | D | added | Two or more consecutive empty paragraphs outside tables. A paragraph with a drawing, field, page break or section end is not empty | |
| `floating.object` | Warning | D | added | A picture or shape with `wp:anchor` (or VML `position:absolute`) | |
| `hyperlink.text` | Warning | D W P | added | Link text that is empty, generic ("click here", "read more", "here"), or the address itself | |
| `watermark.image` | Warning | D | added | A picture named `PowerPlusWaterMarkObject…` in a header | |
| `sheet.default-name` | Warning | W | Sheet tabs have meaningful names | `Sheet<N>` (also Chart/Macro/Dialog) | |
| `sheet.blank` | Warning | W | same | A worksheet with no cells, drawings or tables | |
| `number-format.red-only` | Error | W | Cells … don't use red-only formatting for negative numbers | A number format whose negative section is red and has no minus sign or parentheses, on a cell that holds a number | Reported for the cells that hold numbers; Office may report formatted cells with no negative value |
| `slide.title-missing` | Error | P | All slides have titles | The slide has no title or ctrTitle placeholder with text | Hidden slides are checked |
| `slide.title-duplicate` | Tip | P | Slide titles in a deck are unique | The same title (case and spacing ignored) as an earlier slide | |
| `slide.reading-order` | Warning | P | The reading order … is logical | The title is not first in the shape tree (footer, date and slide-number placeholders aside) | Office's rule is broader; we check only this |
| `section.default-name` | Error | P | All sections have meaningful names | "Default Section", "Untitled Section", "Section N" | |
| `section.duplicate-name` | Tip | P | The section names in a deck are unique | Equal names, case-insensitive | |
| `media.captions` | Warning | P | Closed captions are included for inserted audio and video | Audio/video with no `track` entry in its extension list | In-band captions cannot be seen |

D = word-processing documents, W = workbooks, P = presentations.

Not checked, because the file does not carry the information: "Document access is not restricted" (Information Rights Management is applied outside the file's own parts), and colour used as the only cue other than the red-only number format (Office lists this as a limitation of its own checker).

## Contrast

Foreground and background colours are resolved to sRGB and compared with the WCAG 2.x relative-luminance ratio.

- **Word**: the foreground is the resolved run colour (style inheritance, theme colour, tint/shade). Automatic colour is skipped: it adapts to the background. The background is, in order: the run highlight, the run shading, the paragraph shading, the cell or table shading, the page colour (only when the document displays it), else white. Table style shading is not resolved.
- **Excel**: the cell font colour against the cell's solid fill (none is white). Gradients and patterns are skipped and recorded in `Skipped`. Conditional formats are not applied. Cells with the same format are reported once, with the bounding range.
- **PowerPoint**: the run colour (own, list style, master style, the shape's style reference, presentation default, then `tx1`) against the shape fill, else the cell fill, else the slide background (slide, layout, master). Hyperlink runs use the theme hyperlink colour. Gradient, picture and pattern backgrounds are skipped and recorded in `Skipped`. Colours in text boxes with a style-list fill (`fillRef` index above 1) are treated as unknown. Table cells are checked only when the run colour is written on the run.

## Fixers

A fixer edits the model only when called, and never from `CheckAccessibility`.

- `Document.ApplyAccessibilityFix(issue)` and `Presentation.ApplyAccessibilityFix(issue)` apply the fix an issue offers: `MarkDecorative` (adds the decorative marker and clears the alternative text) and `SetHeaderRow` (Word: `w:tblHeader` on the first row; PowerPoint: `firstRow`). The issue must come from the same file; a result says what was done or why not.
- `SetAccessibilityTitle(title)` on all three kinds sets the title property.
- `Document.SetAccessibilityLanguage(tag)` sets the default language in the style sheet's document defaults.
- Workbooks have no `MarkDecorative` or header-row fixer: their drawings live in parts the workbook does not hold open, and a header row changes the table's range.

Writing alternative text, restructuring tables and rewriting link text need a person; those issues have `Fix` of `None`.

## Limits and safety

The check walks markup with the file's recursion limit and honours the cancellation token. Hostile or damaged input raises the same typed exceptions as any access to the part; a drawing part that cannot be parsed is recorded in `Skipped`. Nothing is fetched and no macro or embedded object is run.

## Verification

`testdata/accessibility/` holds one positive and one negative generated file per rule and kind (`ORACLE-REQUEST.md`, `SHA256SUMS`), written by the `AccessibilityTests` of the three test projects when `ONEDOCUMENT_A11Y_FIXTURES` names a directory. Office's own results for them are collected at the final gate.
