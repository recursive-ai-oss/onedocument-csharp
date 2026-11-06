# Inspecting and sanitising: hidden data and personal information

`Office.Inspect(path)` reports what a file holds that a reader does not see; `Office.Sanitize(input, output)` removes it into a new file and proves the result is clean. `Inspector` and `Sanitizer` (namespace `RecursiveAI.OneDocument.Inspection`) take streams, an open package and the options. The categories follow the groups of Office's Document Inspector as Microsoft describes them in "Remove hidden data and personal information by inspecting documents, presentations, or workbooks" (support.microsoft.com, read 2026-11-05); the article is read as a list of categories, none of its text is reproduced.

```
onedocument inspect <file> [--json] [--password <p>]
onedocument sanitize <in> <out> [--all] [--keep <categories>] [--only <categories>] [--json] [--password <p>]
```

`--keep` and `--only` take comma-separated category names (`comments,hidden-sheets`). `--all` starts from every removable category instead of the defaults. Exit code 0 on success, 1 when the file cannot be processed or the result fails verification (nothing is written), 2 on a usage error. `sanitize` warns when the output name does not match the format written (a macro-free file named `.docm`, a converted `.doc` named `.doc`).

## What the operations promise

- **Inspect** never changes, runs or fetches anything. Every finding has a category, a location (part name, optionally `!` and a position inside it: a sheet and rows, a slide number, an element name), a description, a count, and for short values the value itself (an author, a property).
- **Sanitise** writes a new file (an atomic replace of `<out>`). Parts it does not touch are copied byte for byte; edited parts keep unknown elements, prefixes, `mc:AlternateContent` and `extLst` in order. Output is deterministic.
- **Verification.** The written bytes are opened again and inspected. Every selected category with no `NotRemovedItem` must be empty in them, or `SanitizeVerificationException` (`sanitize.verification-failed`) is thrown and the destination is not written. A category with kept items is `PartlyRemoved`, and each kept item has a code and a reason.
- **Report.** `SanitizeReport` lists, per category, the status (`Removed`, `NothingFound`, `PartlyRemoved`, `ReportOnly`, `NotSelected`), what was removed (location, description, count), what was kept and why (`sanitize.referenced`: something else uses it; `sanitize.unsupported-construct`; `sanitize.report-only`), the inspections before and after, the format written, and notes.
- **Other formats in.** Legacy binary, RTF and 2003 XML files are converted through the document model first and the report says so (`WasConverted`); sanitising writes OOXML (DOC to DOCX, XLS to XLSX, PPT to PPTX). A macro project in the original DOC, XLS or PPT is reported but is not carried into the conversion. An encrypted file is decrypted with the context's password and written unencrypted. Binary workbooks (XLSB) get the categories shared by every package only.
- **Nothing is executed.** Macros, OLE payloads and fields are read as data. No link is followed.

## Categories

"Office" is what the article says the Document Inspector does for that group. Defaults are what `Office.Sanitize` and `sanitize` remove without `--all`/`--only`.

| Category | Found | Removed by sanitising | Office | Default |
|---|---|---|---|---|
| `comments` | Word comments, extended/ids/extensible comment parts; workbook notes and threaded comments; presentation comments (classic and modern) and their author lists | Anchors in the content (`commentRangeStart/End`, `commentReference` runs), the parts and relationships. Workbook note boxes are taken out of the sheet's legacy drawing (form controls in it stay; the drawing goes with the last note) | removes | yes |
| `revisions` | Word `ins`, `del`, `moveFrom/To` and the property-change records, by author; shared-workbook change history and user names; the co-authoring revision data of a presentation | Word: **accepted** through the document model (`docs/revisions.md`); workbook and presentation parts removed | removes ("revision marks") | yes |
| `properties` | Core properties (title, creator, last modified by, dates, keywords, ...), extended properties (company, manager, template, hyperlink base, titles of parts), custom properties, Word document variables, comments on defined names and tables, scenario authors and comments, web-publishing file paths | The elements and attributes; custom properties part removed. Word data-binding links are reported under `custom-xml`: the last value stays as text | removes | yes |
| `personal-information` | Person parts (names and sign-in ids), range permissions that name a user (kept, reported), printer settings, Word `rsid` editing-session ids, file-sharing user name, the stored absolute path of the last save | Removed; the page-setup reference to printer settings is dropped, the page setup stays | removes (user name, printer path) | yes |
| `custom-xml` | Custom XML data parts (including document-management and server properties), Word content-control data bindings, inline `customXml` and `smartTag` wrappers | Parts, relationships, `dataBinding` elements and wrappers; the controls and wrapped content keep their text. A wrapper that declares its own namespaces stays (`sanitize.unsupported-construct`) | removes | yes |
| `headers-footers` | Non-empty Word headers, footers and watermarks; sheet page-setup headers and footers and header pictures | References, parts, and the sheet elements | removes | yes |
| `hidden-text` | Word runs with `vanish`/`specVanish` (not in styles, not inside a change record), hidden paragraph marks. Tip: white text is listed in `Tips` only | Hidden runs removed; hidden marks shown. A hidden run holding a field character stays (`sanitize.unsupported-construct`) | removes | yes |
| `invisible-content` | Shapes, pictures and drawings with `hidden="1"` (slides, sheet drawings, Word drawings), Word VML shapes styled `visibility:hidden` | The shape (in a sheet, with its anchor) and relationships only it used; a shape an animation targets stays (`sanitize.referenced`) | removes | yes |
| `off-slide-content` | Top-level slide shapes lying wholly outside the slide, rotated shapes excepted | As above; a shape an animation targets stays | removes (not animated ones) | yes |
| `hidden-rows-columns` | Rows and columns with `hidden` | Cells in them are removed and the rows and columns are shown, so references stay valid; those in a table or PivotTable are shown with their contents. A sheet where a shared formula starts in a cleared cell and continues in a kept one is kept (`sanitize.unsupported-construct`). Formulas elsewhere keep their cached values until recalculated | removes (deletes; shows table and PivotTable ones) | yes |
| `hidden-sheets` | Hidden and very hidden sheets | The sheet with its drawings, comments and tables, names scoped to it, `calcChain` and the titles list in the extended properties; later names' sheet positions move up. Kept when a formula, name, chart, pivot source or hyperlink in another place refers to it, or to a table on it (`sanitize.referenced`) | removes | yes |
| `hidden-names` | Hidden defined names other than the ones Excel hides (`_xl...`) | The name. Kept when a formula, rule or chart uses it | finds, cannot remove | no |
| `hidden-slides` | Slides with `show="0"` | The slide, its notes, its entry in the slide list, sections and custom shows. Kept when another slide links to it, and when every slide is hidden | not in the list | no |
| `speaker-notes` | Notes pages with text | All notes pages | removes (not pictures in notes) | yes |
| `ink` | Ink parts (`application/inkml+xml`) | The content-part element and the part | removes | yes |
| `embedded-objects` | OLE objects and embedded packages | Not removed | finds, cannot remove | report only |
| `macros` | A VBA project, XLM macro sheets | VBA through `VbaRemoval` (`docs/vba.md`); the main part is retyped to the macro-free format and `OutputFormat` says so. Macro sheets go like hidden sheets | finds, cannot remove | no (it changes the format) |
| `active-x` | ActiveX control parts | Not removed | finds, cannot remove | report only |
| `external-links` | Workbook links to other workbooks; linked pictures, objects, frames, sub-documents and media; mail-merge sources | Word mail-merge data sources (connection string, query, source file) in the settings. Workbook links are broken: formulas that read them become their last values, names that read them go, the link parts go. A mail-merge data source is taken out of the settings and the document is no longer bound to it. Kept when validation, conditional formatting or a chart still reads the other workbook. Linked pictures and objects stay (`sanitize.unsupported-construct`) | finds, cannot remove | no |
| `remote-templates` | The attached template of a Word document (file or URL) | Element and relationship | removes the template name | yes |
| `linked-content` | Word `INCLUDEPICTURE`, `INCLUDETEXT`, `LINK`, `DDE`, `DDEAUTO`, `IMPORT` fields | Not removed | not in the list | report only |
| `data-connections` | Workbook connections and query tables | Connections removed when no query table or PivotTable cache uses them | removes inactive ones | yes |
| `collapsed-headings` | Word `w15:collapsed` | The mark is removed, which expands the headings | removes | yes |
| `web-extensions` | Task-pane and content add-ins | Parts and relationships | removes | yes |
| `pivot-caches` | PivotTable, slicer and timeline caches | Not removed | finds, cannot remove | report only |
| `data-model` | The Power Pivot data model, Power Query in custom XML | Not removed (Power Query goes with `custom-xml`) | finds, cannot remove | report only |
| `thumbnails` | The package preview picture | Relationship and part | not in the list | yes |

`--only` with a report-only category records one `sanitize.report-only` item per finding.

## Gaps and deviations

- **Hidden rows and columns are cleared, not deleted.** Deleting shifts every reference below and to the right; clearing keeps them. Results of formulas that depended on the cleared cells change at the next recalculation, not at sanitising.
- **Revisions are accepted.** Office's own behaviour on "Remove All" for revision marks is an open question for the Office run; both readings leave no revision.
- **Hidden-sheet and hidden-name removal is conservative:** anything that refers to them keeps them, where Office removes them and lets formulas change.
- **Not covered:** document versions (Word's pre-2007 versions have no OOXML form), email headers and routing slips (legacy), Excel surveys and Scenario Manager scenarios beyond their authors, filters, cube formula caches, text boxes' linked chains, hidden objects covered by other objects, text in white or tiny type, metadata inside embedded pictures (EXIF), and the contents of embedded documents.
- **Not read:** ODF files (a later stage of the ODF work adds them through the hub), and parts that are not well-formed XML (noted in the report).
- Sheet shapes of a *hidden row or column* are not detected, as in Office.
- **Parts no relationship reaches** are never read by Office. Inspection notes them; sanitising drops them (and the notes of a sanitise report name them).
- **Cleared cells and removed sheets** also lose their strings in the shared string table (emptied in place, so indexes hold), their hyperlinks, validation lists and conditional formats, and the calculation chain is dropped.
- **Kept items do not excuse leftovers.** After writing, a category with kept items is re-checked: every finding must be explained by a kept item, or verification fails.
- **Known gaps, each a skipped test in `SanitizeReviewTests`:** text hidden by a style (only direct run formatting is found); rows hidden by default (`sheetFormatPr zeroHeight`); text on notes and handout masters; off-slide shapes on layouts and masters; PowerPoint tags. A tracked change in a Word glossary document is found but kept. See `docs/sanitize-review.md`.

## Checking a category

Each row above is one function in `PackageInspection` (shared categories) or in the kind's provider (`WordInspectionProvider`, `ExcelInspectionProvider`, `PowerPointInspectionProvider`). A provider's `Inspect` and `Sanitize` run one code path, so what is found and what is removed cannot differ. `tools/InspectProbe` runs both over a directory and prints the counts and the typed failures. The generated test documents with every category present are in `tests/OneDocument.Cli.Tests/TortureFiles.cs`; `testdata/inspect/ORACLE-REQUEST.md` lists what to ask Office about them.
