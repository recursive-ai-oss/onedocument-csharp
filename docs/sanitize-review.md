# Sanitiser coverage review

An independent review of `Inspector` and `Sanitizer` (`docs/sanitize.md`): for every category inspection reports, does it find every instance, and does sanitising remove every instance it says it removed? The checks are committed as tests: `tests/OneDocument.Cli.Tests/ReviewScenarios.cs` builds the adversarial documents and `SanitizeReviewTests.cs` runs them.

## Method

Each adversarial document plants one place where data can hide, with a string `PLANTED-<case>` in it. A case passes when:

1. inspection reports the categories the case was written for;
2. sanitising with every removable category (and, where the case names them, with those categories alone) writes a file in which the string appears in no part, unless the report says that item was kept;
3. no part uses a relationship id the file no longer has, and no relationship points at a missing part;
4. the file opens, and a second inspection finds nothing in a category the report calls `Removed`;
5. LibreOffice (26.8.2, headless, on the review worker) converts the output to PDF. 64 of 64 outputs did.

A recorded gap is a test that is skipped while it still fails and fails when it starts to pass, so the list below cannot go stale. Nothing here ran Office; the cases for it are in `testdata/inspect/ORACLE-REQUEST.md` (section 5).

## Verdict: pass, with the gaps listed

Every removal the report claims holds in the 61 passing cases, the verifier no longer lets a category with kept items hide other leftovers, and each remaining gap is documented in `docs/sanitize.md` and recorded as a skipped test. A dozen defects were found and fixed (below). Three gaps remain; none is a removal that the report claims and does not perform.

## Defects found and fixed

| Case | What was wrong | Fix |
|---|---|---|
| `rev-styles`, `rev-numbering` | Property-change records in styles and numbering definitions (`w:rPrChange`, `w:pPrChange` with their author and date) were neither reported nor removed. | Inspected in both parts; sanitising drops the records (the formatting itself stays). |
| `rev-custom-xml-range` | Custom-XML change ranges (`w:customXmlInsRangeStart` and kin, with author) were not counted as revisions and stayed. | Counted; the range markers are dropped after the changes are accepted. Move-range markers are counted too. |
| `rev-glossary` | A tracked change in the glossary document was found but stayed, and the report said the category was removed. | Found but not accepted (the document model has no glossary): reported as a kept item (`sanitize.unsupported-construct`). |
| `link-split-instruction` | A field instruction split over several `w:instrText` runs (the way Word writes a long path) was not seen: `INCLUDE` + `PICTURE` was not matched. Lower-case field names in `fldSimple` were matched but only by luck of trimming. | Complex field instructions are assembled between `begin` and `separate` before the name is read. The fields are report-only, so nothing is removed; the report now lists them. |
| `mailmerge-embedded`, `mailmerge-connection` | The mail-merge connection string, query and data-source reference in `settings.xml` (server names, file paths, passwords) were not reported; only the external relationship was. | Reported under `external-links`; sanitising removes the data-source elements and their relationships, so the document is no longer bound to the data. |
| `customxml-inline` | Inline `w:customXml` and `w:smartTag` wrappers (element names, attribute values) were not reported or removed. | Reported under `custom-xml`; the wrapper is taken out and its content kept. A wrapper that declares its own namespaces is kept and reported. |
| `hdr-sections-and-orphan` | A header or footer part that no relationship reaches stayed in the file, and verification then failed with `sanitize.verification-failed`, so such a file could not be sanitised at all. | Parts that nothing reaches are dropped when sanitising and named in the inspection notes. |
| `prop-orphan-core`, `xl-comments-orphan`, `ppt-changes-info` | Same class: any part outside the relationship graph kept its bytes (a second core-properties part, a comments part, a change log). PowerPoint's `changesInfo` part (who changed what, when) was not recognised at all. | Unreachable parts are removed and noted; `changesInfo` is a revision-data part. |
| `xl-hidden-row-shared-string`, `xl-hidden-sheet-shared-string`, `xl-hidden-row-companions` | The text of cleared cells and removed sheets stayed in the shared string table, a hidden row's hyperlinks, validation lists and conditional formats stayed, and `calcChain` kept the cleared cells. | Strings no cell uses any more are emptied in place (indexes unchanged); companions of cleared cells go with them; `calcChain` is dropped whenever cells were removed. |
| `perm-editor` | A permission range names who may edit it (`w:permStart w:ed`). Not reported. | Reported under `personal-information`; kept, because removing the range would lock the text (`sanitize.unsupported-construct`). |
| `altchunk` | An imported document part (`w:altChunk`) was not reported at all. | Reported under `embedded-objects` (report only). |
| Verifier | A category with any kept item was not re-checked, so a leftover next to a kept item passed. | After writing, every finding in a category with kept items must be explained by one of them (`A_kept_item_does_not_excuse_other_findings_in_its_category`). |
| Hidden shapes | `hidden="1"` shapes were looked for on slides only, not on layouts, masters, notes pages or the handout master. | Looked for in all of them. |

## Coverage against Office's Document Inspector

Status: **tested** = covered and exercised by a committed case; **untested** = covered, no adversarial case beyond the torture files; **documented gap**; **undocumented gap** (none remain: every one found is now documented).

| Office inspector | Word | Excel | PowerPoint |
|---|---|---|---|
| Comments / annotations | tested (all comment parts, footnote anchors, people, orphan part) | tested (notes, threaded comments, persons, orphan part) | tested (classic, modern, authors) |
| Revisions / tracking data | tested (inline, move ranges, table, section, styles, numbering, footnotes, headers, text boxes in Choice and Fallback, glossary kept) | tested (shared-workbook user names; change history by torture file) | tested (`changesInfo`, revision info) |
| Document properties and personal information | tested (every core field, extended fields and link lists, docVars, rsids, attached template, mail-merge source, range permissions) | tested (file sharing, absolute path, printer settings, name and table comments, scenarios, web-publish paths) | untested beyond the shared core/app/custom parts (tested through Word and Excel) |
| Custom XML | tested (package-level items, header bindings, inline wrappers) | untested beyond the torture file | tested (hidden-slide custom data, tags) |
| Headers, footers, watermarks | tested (sections, first page, orphan) | tested (first/even variants, pictures by torture file) | not applicable |
| Hidden text | tested (tables, controls, links, notes, headers, text boxes, `val` spellings, paragraph marks); tested (styles, `basedOn`, direct override) | not applicable | not applicable |
| Invisible content | tested (text boxes in Choice and Fallback, headers) | tested (hidden shape by torture file) | tested (slides, layouts, groups) |
| Off-slide content | not applicable | not applicable | tested for slides; **documented gap**: layouts and masters |
| Hidden rows and columns | not applicable | tested (shared strings, hyperlinks, validation, formats, `calcChain`); **documented gap**: rows hidden by default (`zeroHeight`) | not applicable |
| Hidden worksheets | not applicable | tested (names, print areas, tab views, parts of the sheet, chart caches kept and reported) | not applicable |
| Speaker notes | not applicable | not applicable | tested (slides, hidden slides); **documented gap**: notes and handout masters |
| Task-pane add-ins | tested (torture file) | untested | untested |
| Embedded documents | report only (tested, with `altChunk`) | report only | report only |
| Ink | tested | untested | tested |
| Macros | untested here (see `docs/vba.md`) | untested here | untested here |
| Data connections, external links | tested (mail-merge data source, linked pictures kept) | tested (links with caches, connections) | not applicable |
| Collapsed headings | tested (torture file) | not applicable | not applicable |

Categories we cover that Office's inspector does not: thumbnails, hidden slides, hidden names, linked content (report only), the mail-merge source, range permissions, pivot caches (report only), and parts no relationship reaches.

## Remaining gaps (each a skipped test)

1. **Rows hidden by default** (`xl-zero-height-default`): `sheetFormatPr zeroHeight` hides every row without its own height. How Excel marks the rows that are shown needs the Office run.
2. **Notes master and handout master text** (`ppt-notes-master`): neither reported nor removed. Office's inspector does not look there; the content is page furniture. Left as is.
3. **Off-slide shapes on layouts and masters** (`ppt-layout-off-slide`): templates park instructions there on purpose; not reported.

Text hidden by a style and PowerPoint tags were gaps in the first pass and are now covered (`hid-style`, `ppt-tags`): a run is hidden when its character style, or else its paragraph style, sets `vanish` through the `basedOn` chain and the run does not set `vanish` itself; tag parts are reported and removed with their references.

Others stated in `docs/sanitize.md` still stand (documents' pre-2007 versions, metadata inside pictures, text in white, the contents of embedded documents).

## Verifier false negatives

Breaking removal on purpose: a provider that keeps one item and leaks another in the same category passed before; it now fails with `SanitizeVerificationException` and writes nothing (`A_kept_item_does_not_excuse_other_findings_in_its_category`). The existing leak and throw cases still hold. A weaker spot remains by construction: the verifier runs the same inspection code as the sanitiser, so a place inspection does not know about cannot be caught by it. That is why this review plants data in places found from the specifications and tests the written bytes for the planted string independently of inspection.

## Legacy inputs

All `testdata/doc`, `testdata/xls` and `testdata/ppt` files sanitise to OOXML with every default category empty or reported as kept (`Legacy_files_sanitise_to_ooxml_with_every_default_category_empty`).
