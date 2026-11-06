# Redaction

True redaction removes content from the file, not from the view. `OfficeFile.Redact(plan, path)` (and `Office.Redact(source, destination, plan)`) takes a `RedactionPlan` of **matches** (a literal or a regular expression) and **regions** (paragraphs, cell ranges, shapes, slides), writes a **new file**, re-opens it and searches every entry for what was removed. The source file is never changed, and `Redact` refuses an output path equal to the source path.

## What it does

1. **Stage.** The file is saved through its hub as OOXML in memory: its own format for DOCX/DOCM/DOTX/DOTM, XLSX/XLSM/XLTX/XLTM/XLAM, PPTX/PPTM/POTX/POTM/PPSX/PPSM/PPAM; for everything else (DOC, RTF, 2003 XML, XLS, XLSB, ODF, Flat OPC, PPT) the OOXML format of its kind (`RedactionReport.Converted` and `OutputFormat` say so). The original binary is never edited. Untouched OOXML parts keep their bytes through this step.
2. **Regions** (kind packages, `Redaction/*Regions.cs`) run first, then everything below.
3. **Matches** run over every part of the package and of each package embedded in it (embedded workbooks of charts, nested OLE packages), so no location is skipped for lack of a rule. Only edited nodes change; unedited bytes of an edited part, `mc:AlternateContent`, `extLst`, prefixes and order stay as they were. Parts nothing matched stay byte-identical.
4. **Verification** (on by default) re-opens the written zip and searches **every entry** (parts, `.rels`, `[Content_Types].xml`, entries that are not parts), see below. The report says `Verified` or lists `Survivors` (location and match index, never the text).

## Matching

- Text is matched **as a reader sees it across runs**: for each paragraph (`w:p`, `a:p`), string item (`si`, `is`), cell note (`text`) and phonetic run (`rPh`), the text of its runs is joined in reading order (visible text and deleted text together; tab and break characters count as `\t`/`\n`; nested paragraphs such as text-box content are their own units) and matched as a whole. The first run touched receives the mark and keeps its formatting, the covered characters are cut from the later runs. A match that starts or ends on a tab or break leaves that tab or break in place.
- Field codes (`w:instrText`) are matched as their own stream; the cached result runs are ordinary text.
- Every other text node, attribute value (except namespace declarations, `r:`/`mc:` attributes, `xml:space`), comment and processing instruction in every XML part is matched on its own.
- Matches do not span paragraphs.
- Literals compare ordinally (`ignoreCase` → invariant, ordinal-ignore-case); patterns are .NET regular expressions with the limits' timeout (`OfficeLimitException` on timeout). Empty matches are ignored.
- Binary parts (embedded objects, macro projects, picture metadata): a literal is searched as UTF-8, UTF-16LE and Latin-1 bytes; patterns run over printable runs of four characters or more (ASCII and UTF-16LE). The hit is overwritten in place with `X` (the part keeps its length and structure) and counted as `binary-overwrite`. In pictures patterns apply only in runs of eight or more characters and to matches of six or more characters, because compressed pixels look like text by chance; literals always apply.

## Marks

`RedactionOptions.Mark` (default `█`): one character is repeated once per removed character (`HideLength` makes it a fixed `HiddenLength`, default 8); a longer string replaces the span as written. In places where a name must stay a valid identifier (bookmark, defined-name, sheet and table names, field codes, formulas and defined-name text, validation and conditional-format formulas, hyperlink and link targets) the mark is `REDACTED` plus a letter code, the same for the same removed text, so references inside the file stay consistent. The `█` mark renders as a black box; no other redaction formatting is applied (the mark keeps the first run's formatting).

SpreadsheetML: a number or formula result that matches becomes a string (`t="str"` when the cell has a formula, otherwise an inline string); shared-string cells are handled in the string table, so the string itself is replaced (not only the cell's reference); a pivot cache number that matches becomes a string item.

Relationships: the target of an external relationship (hyperlinks, linked files, external-link paths) is matched raw and percent-decoded; a match is replaced with the identifier mark (percent-encoded where needed).

## Regions

| Region | Kind | Effect |
|---|---|---|
| `ParagraphRegion(first, last)` | document | Counted from zero over every `w:p` below `w:body` in document order (tables, text boxes and structured-document-tag paragraphs included). A paragraph keeps `w:pPr` and gets one run with the mark. Comments (the `w:comment` content, replaced by the mark), footnotes and endnotes referenced from it are blanked; pictures, charts, embedded objects and hyperlink targets only it used are removed with their parts and relationships. |
| `CellRangeRegion(sheet, range)` | workbook | Cells in the range lose value, formula and type and keep their style. Shared strings that only these cells used are blanked. Notes (legacy and threaded comments, with their legacy drawing shapes), hyperlinks and pictures/charts anchored in the range are removed; a chart that draws from the range keeps no cached values and no embedded workbook. |
| `ShapeRegion(slide, name or id)` | presentation | The shape, picture, table, chart or group is removed with its alt text; unused media/chart/embedding parts go; the slide's animations are removed when they name a removed shape. |
| `SlideRegion(slide)` | presentation | The slide is removed with its notes, comments and every part only it used; the slide id lists in sections and custom shows are updated and the title lists in the extended properties are dropped. |

A region that does not fit the kind of file or finds nothing is a gap (`redaction.region-not-applicable`, `redaction.region-failed`), never a silent no-op. A part is removed with a region only when it was reachable before and is not after; parts that were already unreachable are left alone.

## Verification

After writing, the output zip is read with the container reader (limits applied) and every entry is checked: entry names; for XML, every decoded text node, attribute value (also percent-decoded), comment, element and attribute name (literals only), the joined text of every paragraph-like unit, and the raw bytes (literals); for binary entries the literal as UTF-8/UTF-16LE/Latin-1 bytes and every printable run; zips inside zips, recursively (a zip that cannot be read is searched as bytes). A match whose text lies inside a mark that was written is not a survivor, so a pattern such as `.+` does not fail on its own marks.

## Locations covered

Each is exercised by the generated torture packages in `tests/OneDocument.Redaction.Tests` (secret planted in every row; raw search of every decompressed entry of the output, recursively, finds nothing; extracted text equals the original with only the secret replaced).

| Location | How |
|---|---|
| Body text, across runs, tables, structured-document-tag content, text boxes (`mc:Choice` and `mc:Fallback`) | paragraph join |
| Tracked deletions (`w:delText`), moved-from text | paragraph join (visible and deleted text together) |
| Comments and replies, footnotes, endnotes, headers, footers, glossary document | same, every part |
| Alt text, titles, names (`docPr`, `cNvPr`, `descr`, `title`), hyperlink screen tips, anchors | attribute pass |
| Hyperlink and link targets | external relationship pass |
| Bookmark names, defined names, sheet names, table names, structured-document-tag alias/tag | attribute pass (identifier mark) |
| Field codes and cached field results (TOC, REF, DOCPROPERTY) | code stream and paragraph join |
| Content-control values and their bound custom XML parts | paragraph join; custom XML text nodes |
| Document properties (core, extended, custom) | text and attribute pass |
| Settings document variables, styles names | attribute pass |
| Charts: `c:strCache`/`c:numCache`, titles, embedded chart workbooks | text pass; embedded packages processed recursively |
| SmartArt data models and cached drawings | text pass |
| Speaker notes, layouts and masters | text pass |
| Sheet values, inline strings, shared strings, formulas and cached values | cell rules; string table; formula text |
| Cell notes and their legacy drawing, data-validation lists, page-setup headers/footers | text and attribute pass |
| Pivot caches (definitions, shared items, records) | attribute pass; numbers become string items |
| External-link caches and paths | cell rule (`t="str"`); relationship pass |
| Embedded OLE objects, macro projects, picture metadata | raw overwrite; reported as `redaction.opaque-part` |
| Thumbnails | dropped (rasters of the content), reported as `thumbnail` |
| Zip entries that are not parts | verified; reported by the verifier if they hold a match |

## Known gaps

All typed in the report; none is silent.

- **Part names** (including names inside `[Content_Types].xml` and relationship targets) that contain a match are not renamed: `redaction.part-name`, and the verifier reports the survivor.
- **Element and attribute names** are not rewritten (a secret used as a custom XML element name): the verifier reports a survivor.
- **Compressed or encrypted binary content** (a macro project's compressed source, an encrypted package, an OLE object holding its own compressed streams) can only be searched as raw bytes: `redaction.opaque-part` lists every such part. Remove macros and objects with the matching sanitise operations instead.
- **Pictures** are not read for text in pixels. Redact a picture by region.
- **Derived values**: formulas elsewhere that depend on an emptied range keep their cached values (recalculate after a region); a pivot cache built from an emptied range keeps its values (`redaction.pivot-cache-source`; remove the pivot or match its text).
- **Shared or array formulas** whose master is emptied while dependents stay outside the region: `redaction.shared-formula-extent`.
- **Matches that cross paragraphs** or sit in two different parts are not found as one match.
- **A pattern that matches structure** (digits in numeric attributes such as `w:val="12"`) edits that structure; patterns should be as specific as the secret.
- **Table header cells and column names** can differ after a match in a header (`tableColumn name` gets the text mark, structured references the identifier mark): Excel may recalculate a structured reference as `#REF!`. No repair prompt is expected, but Office has not confirmed it yet.
- Rendered redaction as black boxes beyond the `█` mark, and PDF redaction, are not part of this operation.
- Office confirmations (opening each output without a repair prompt) are in `testdata/redaction/ORACLE-REQUEST.md`, unanswered until the Office run.

## Report

`RedactionReport.Items` (category, part, count), `Gaps` (code, location, message), `Survivors`, `Verified`, `OutputFormat`, `Converted`, `TotalReplacements`. Categories: `body`, `comments`, `notes`, `headers-footers`, `chart`, `smartart`, `speaker-notes`, `layouts-masters`, `slide`, `shared-strings`, `sheet`, `pivot`, `external-link-cache`, `custom-xml`, `workbook`, `drawing`, `glossary`, `properties`, `other`, `hyperlink-target`, `binary-overwrite`, `thumbnail`, `region`, `region-*`.

Gap codes: `redaction.part-name`, `redaction.part-not-xml`, `redaction.embedded-package`, `redaction.opaque-part`, `redaction.depth`, `redaction.region-not-applicable`, `redaction.region-failed`, `redaction.shared-formula-extent`, `redaction.pivot-cache-source`, `redaction.animations-removed`. They live in the report, not in the open-time defect list.
