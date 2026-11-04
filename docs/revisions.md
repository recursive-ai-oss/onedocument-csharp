# Revisions: tracked changes in WordprocessingML

This note states what the revision model (`RecursiveAI.OneOffice.Documents.Revisions`) does. The XML documentation on each type states the details. Section numbers are ECMA-376 Part 1 (5th edition) unless noted.

## What is modelled

A revision is an element in the main namespace that records a change by an author at a date (`w:id`, `w:author`, `w:date`; the date is optional) (§17.13.5). The model reads them from any set of parts: the main document, headers, footers, footnotes, endnotes, comments, styles and numbering.

| Kind | Element | Where |
|---|---|---|
| Insertion, Deletion | `w:ins`, `w:del` | around runs and other inline content (§17.13.5.18, §17.13.5.14) |
| MoveFrom, MoveTo | `w:moveFrom`, `w:moveTo`, with `w:moveFromRangeStart/End`, `w:moveToRangeStart/End` | around runs; the range markers carry the name that pairs source and destination (§17.13.5.22, §17.13.5.25) |
| ParagraphMarkInserted / Deleted / MovedFrom / MovedTo | `w:ins`, `w:del`, `w:moveFrom`, `w:moveTo` | in the run properties of the paragraph mark (`w:pPr/w:rPr`) |
| RunProperties, ParagraphProperties, SectionProperties, TableProperties, TablePropertyExceptions, TableGrid, TableRowProperties, TableCellProperties changes | `w:rPrChange`, `w:pPrChange`, `w:sectPrChange`, `w:tblPrChange`, `w:tblPrExChange`, `w:tblGridChange`, `w:trPrChange`, `w:tcPrChange` | last child of the property container; holds the previous properties |
| TableRowInserted / Deleted | `w:ins`, `w:del` | in `w:trPr` |
| CellInserted / Deleted / Merged | `w:cellIns`, `w:cellDel`, `w:cellMerge` | in `w:tcPr` |
| NumberingChange, NumberingInserted | `w:numberingChange`, `w:ins` | in `w:numPr` |

Moved-away text stays `w:t` inside `w:moveFrom`; only `w:del` uses `w:delText`. A `w:moveFrom` holding `w:delText` makes Word 365 (16.0.20527) offer to repair the file, so authoring writes `w:t` there; reading accepts either.

The same element name means different things by its parent, so a revision's kind comes from the element and its parent together. Strict and Transitional namespaces are read alike.

Each revision has an author, a date (UTC; a date without a zone is taken as UTC), an id, a scope (content, paragraph mark, formatting, table, numbering), the part, the element and, for content, its text. A revision with a missing or unusable `w:id`, `w:author` or `w:date` is still listed and reported as a defect (`docx.revision-id-invalid`, `docx.revision-author-missing`, `docx.revision-date-invalid`).

## Accepting and rejecting

| Revision | Accept | Reject |
|---|---|---|
| Insertion, MoveTo | the content stays, the wrapper goes | the wrapper and content go |
| Deletion, MoveFrom | the wrapper and content go | the content stays; `w:delText` becomes `w:t` and `w:delInstrText` becomes `w:instrText` |
| Paragraph mark inserted or moved-to | the marker goes | the mark goes: the paragraph's content joins the next paragraph (see below) |
| Paragraph mark deleted or moved-from | the mark goes: the content joins the next paragraph | the marker goes |
| Property change | the record goes; the current properties stay | the properties are replaced by the recorded ones; entries that are not part of the change stay (see below) |
| Row inserted / deleted | marker goes / the row goes | the row goes / marker goes |
| Cell inserted / deleted | marker goes / the cell goes | the cell goes / marker goes |
| Cell merge | the vertical merge becomes `vMerge` of the record | the vertical merge becomes `vMergeOrig` of the record |
| Numbering inserted | marker goes | the marker goes; `w:numPr` stays (observed in Word 365, 16.0.20527) |
| Numbering change | the record goes | the record goes (it holds only a description of the old numbering) |

- **Joining paragraphs.** The paragraph mark carries the paragraph's properties (§17.3.1.26), so when a mark goes the paragraph is joined to the next one, and the next paragraph's properties apply to the joined paragraph. A mark that cannot go is only unmarked: the last paragraph of a container, a paragraph before a table, or a paragraph that holds a section break. An emptied inserted paragraph at the end of a container is removed.
- **What a property change does not cover.** `w:rPrChange` in a paragraph-mark `w:rPr` leaves the revision markers there; `w:pPrChange` leaves the mark's `w:rPr` and `w:sectPr`; `w:sectPrChange` leaves the header and footer references; row and cell changes leave their revision markers. These are separate revisions.
- **Empty containers** left by a resolved revision (`w:rPr`, `w:pPr`, `w:trPr`, `w:tcPr` with nothing in them) are removed.
- **Rows and tables.** Removing the last row removes the table; a cell left without a paragraph gets an empty one (§17.4.66). Removing the last cell removes the row.
- **Range markers** of moves (and of custom XML) are removed with their content. After accept-all or reject-all any that remain are removed too.
- **Order.** Revisions of one part are resolved from the end of the part to its start, so content moved by one revision never holds a revision that is still to be resolved. Revisions nested in each other (another author's deletion inside an insertion) resolve independently: accepting only the outer one leaves the inner one in place.
- **One revision, a set, an author, a range, all.** Accepting or rejecting works on any selection taken from the current list. After any change the list is read again, and revisions taken earlier are refused (`ArgumentException`).
- **What is untouched.** Nothing outside the resolved revisions changes: unknown elements, `mc:AlternateContent`, `extLst`, prefixes and attribute order stay as they were, and a part without revisions is not modified. A revision that cannot be resolved because its markup is not what the schema allows stays in place and is reported (`docx.revision-unresolvable`).

## Text views

`RevisionSet.GetText` returns the text of a part as if every revision were accepted or every one rejected, without changing it: one line per paragraph, `\t` for tabs, `\n` for line breaks. A paragraph whose mark is removed in the view has no line end of its own, so it runs on into the next. Rows and cells that do not exist in the view are skipped. The view and the text after `AcceptAll`/`RejectAll` are the same by construction and are checked equal over the test corpus. Text boxes and drawings are not read.

## Authoring

`RevisionSet.CreateAuthor(name, date)` gives a `RevisionAuthor`. It never reads the clock; a null date leaves `w:date` out. Ids continue after the largest id in the set's parts.

- **Positions** are UTF-16 offsets into a paragraph's current text: the text with every revision accepted (deleted and moved-away text does not count; inserted text does). A tab or a line break is one character; field instructions and drawings are none.
- **Text:** `InsertText`, `DeleteText`, `ReplaceText` (deletion then insertion), `MoveText`. Runs are split at the edges of the edit and keep their properties; inserted text takes the properties of the run before it. Deleting text the same author inserted removes it outright; deleting another author's inserted text puts a deletion inside their insertion. Inserting in the middle of another author's insertion splits their wrapper.
- **Paragraphs:** `InsertParagraph` marks the existing paragraph's mark as inserted and gives the new paragraph the old paragraph's properties (what Word writes when Enter is pressed at the end of a paragraph), so a reject restores the original properties wherever the paragraph is. `DeleteParagraph` deletes its content and mark. `MoveParagraph` moves it after another paragraph.
- **Formatting:** `FormatText` merges run properties over a range; `ChangeRunProperties`, `ChangeParagraphProperties`, `ChangeSectionProperties`, `ChangeTableProperties`, `ChangeTableGrid`, `ChangeTableRowProperties`, `ChangeTableCellProperties` replace a whole property set and record the old one. A pending change keeps its first recorded original.
- **Tables:** `MarkRowInserted`, `MarkRowDeleted`, `MarkCellInserted`, `MarkCellDeleted`, `MergeCells`. The caller adds a new row or cell; the author marks it.
- Property fragments are XML with the part's own prefix for the main namespace (`RevisionAuthor.Prefix` gives it). A fragment that is not well-formed throws before anything changes.
- Everything an author writes resolves cleanly: after `RejectAll` the part's text and structure are the original, and after `AcceptAll` the text is what the edits describe. The tests check this against a string model over random edit sequences.

## Not covered

- Revisions inside text boxes and drawing canvases are found (they are elements in the part) and resolved, but the views do not read text boxes.
- Tracked changes outside WordprocessingML (DrawingML, SpreadsheetML change tracking) belong to their own models.
- Word's choice when two paragraphs join and both hold properties is taken from the paragraph mark rule above; whether Word keeps the first paragraph's properties when the first paragraph still has text is an open question for the Office oracle.
