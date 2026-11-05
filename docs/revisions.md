# Revisions: tracked changes in WordprocessingML

This note states what the revision model (`RecursiveAI.OneDocument.Documents.Revisions`) does. The XML documentation on each type states the details. Section numbers are ECMA-376 Part 1 (5th edition) unless noted.

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

`Document.Revisions.CreateAuthor(name, date)` (or `RevisionSet.CreateAuthor`) gives a `RevisionAuthor`: the way a comparison tool or a program writes its edits as tracked changes. Everything it writes resolves cleanly: after `RejectAll` the part's text and structure are the original, and after `AcceptAll` the text is what the edits describe. It works on any part of the set: the body, headers, footers, footnotes, endnotes and comments.

- **Ids.** `CreateAuthor(name, date)` numbers from one above the largest `w:id` in the parts (revisions, bookmarks, comment ranges, move ranges). `CreateAuthor(name, date, ids)` takes a `RevisionIds`: `StartingAt(n)` counts up from `n`; `FromCallback(next)` asks a function for every id. One source can serve several authors on one set. An id that is negative or already used throws `RevisionIdConflictException` (`docx.revision-id-conflict`); runs the call had already split are not put back, so treat the document as spoiled. The order in which ids are taken is fixed for each operation, so the same edits on the same document write the same ids.
- **Dates and the clock.** The date is the one given (UTC, to the second) or none. The library never reads the clock, and the output depends only on the document and the calls: the same calls on the same bytes write the same bytes.
- **Positions** are UTF-16 offsets into a paragraph's current text: the text with every revision accepted (deleted and moved-away text does not count; inserted text does). A tab or a line break is one character; field instructions and drawings are none.
- **Two levels.** Every operation takes a `MarkupNode` (the low-level path, with property changes as XML fragments in the part's own prefix) and the document model's own `Paragraph`, `Run`, `Table`, `TableRow`, `TableCell` (with typed property changes). The model overloads do what the markup ones do; `InsertParagraph` and `InsertTable` return the new object.
- **Text:** `InsertText`, `DeleteText`, `ReplaceText` (deletion then insertion; the new text takes the formatting of the first replaced character), `MoveText`. Runs are split at the edges of the edit and keep their properties; inserted text takes the properties of the run before it. Deleting text the same author inserted removes it outright; deleting another author's inserted text puts a deletion inside their insertion. Inserting in the middle of another author's insertion splits their wrapper.
- **Moves.** `MoveText` and `MoveParagraph` write `w:moveFrom` and `w:moveTo` with `w:moveFromRangeStart/End` and `w:moveToRangeStart/End`. The two ends are paired by name: `move<id>` by default, or the `moveName` the caller gives (refused when another move in the parts already uses it). Moved-away text stays `w:t`.
- **Paragraphs and tables:** `InsertParagraph` marks the existing paragraph's mark as inserted and gives the new paragraph the old paragraph's properties (what Word writes when Enter is pressed at the end of a paragraph), so a reject restores the original properties wherever the paragraph is. `DeleteParagraph` deletes its content and mark. `MoveParagraph` moves it after another paragraph. `InsertRow(table, index, texts)`, `MarkRowInserted`, `MarkRowDeleted`, `InsertTable(after, cells)`, `DeleteTable`, `MarkCellInserted`, `MarkCellDeleted` and `MergeCells` write table revisions; a table inserted at the end of a cell is followed by an inserted empty paragraph (a cell must end with one), which a reject leaves as one empty paragraph.
- **Formatting, typed.** `RunPropertiesDelta` (style, font, size in points, bold, italic, strike, caps, underline, colour, highlight), `ParagraphPropertiesDelta` (style, alignment, left/right/first-line/hanging indents, spacing before/after, line spacing as a multiple, keep-with-next, keep-lines, page-break-before, list id and level), `TablePropertiesDelta`, `RowPropertiesDelta` and `CellPropertiesDelta` set what is not null, keep everything else (including attributes of `w:ind` and `w:spacing` the delta does not name), and put new elements where the schema's order puts them. `FormatText`, `ChangeRunProperties`, `ChangeParagraphProperties`, `ChangeTableProperties`, `ChangeTableRowProperties` and `ChangeTableCellProperties` take them and record the old properties as `w:rPrChange`, `w:pPrChange`, `w:tblPrChange`, `w:trPrChange` or `w:tcPrChange`. A pending change keeps its first recorded original. A numbering change is a paragraph property change (`NumberingId`; 0 takes the paragraph out of its list). Invalid values (a colour that is not six hex digits, a size that is not a multiple of half a point, a list level above 8) throw `ArgumentException` before anything changes. Section properties and table grids take XML fragments only.
- **Example.** A comparison tool recording that one word changed, one paragraph was added and a heading became centred:

```csharp
using Document doc = Document.Open("before.docx");
RevisionAuthor au = doc.Revisions.CreateAuthor("Compare", new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero), RevisionIds.StartingAt(1000));
List<Paragraph> p = doc.Body.Paragraphs().ToList();
au.ReplaceText(p[2], 14, 5, "revised");
Paragraph added = au.InsertParagraph(p[2], "A new paragraph after it.");
au.ChangeParagraphProperties(p[0], new ParagraphPropertiesDelta { Alignment = ParagraphAlignment.Center });
au.FormatText(added, 0, 1, new RunPropertiesDelta { Bold = true });
doc.SaveAs("after.docx");   // accepting every change gives the new text, rejecting gives before.docx's
```

## Observed in Word

Checked with Word 365 (16.0.20527) accepting and rejecting every revision of generated files: text, paragraph formatting, list labels and table cells agree with ours for every operation above, in the body, a footnote, a header and a comment. One difference: rejecting a `w:tblPrChange` through the object model removes the record but leaves the new table properties in place (and, when row or cell changes are present as well, may split the table at the first row). Our reject restores the recorded properties, as §17.13.5 describes; row and cell property changes behave alike in both.

## Not covered

- Revisions inside text boxes and drawing canvases are found (they are elements in the part) and resolved, but the views do not read text boxes.
- **`mc:AlternateContent`.** Only the branch a reader takes (ECMA-376 Part 3 §9.3: the first `mc:Choice` whose required namespaces are understood, else `mc:Fallback`) is listed, so a DrawingML text box with a VML copy counts once, as Word counts it. A revision in the other branch is its copy: accepting or rejecting the listed revision resolves the copy with it, and `AcceptAll`/`RejectAll` resolve every copy. Copies are paired with the listed revisions by kind and order inside the same `mc:AlternateContent`. The copy is brought up to date by `Document.CommitChanges`, which writes the exposed branch into the other.
- **Property changes and paragraph marks.** Rejecting a `w:pPrChange` keeps the other revisions of the same `w:pPr` (the paragraph mark's `w:ins`/`w:del`, a row's or cell's marks) as they are, so they can still be resolved in the same call. `InsertParagraph` on a paragraph with a pending `w:pPrChange` gives the new paragraph a copy of it (a new id), so rejecting everything restores the original properties.
- Tracked changes outside WordprocessingML (DrawingML, SpreadsheetML change tracking) belong to their own models.
- Word's choice when two paragraphs join and both hold properties is taken from the paragraph mark rule above; whether Word keeps the first paragraph's properties when the first paragraph still has text is an open question for the Office oracle.
- **Namespace declarations when markup moves.** Joining paragraphs, `InsertParagraph`, moves, unwrapping an insertion and copying a text box's story into its `mc:Fallback` copy rewrite markup under another parent. A prefix the moved markup uses that is declared above it but not on the part's root element (for example `xmlns:r` on the old `w:p`) is declared again on the moved element, so no prefix is ever written unbound. When every prefix is on the root, the bytes are the markup's own, unchanged. A prefix that is bound nowhere in the source is a defect of the source markup; the edit then fails with the existing typed outcome (`ArgumentException` when authoring, `docx.revision-unresolvable` when resolving) and leaves the revision in place.
- **Wrappers emptied by resolving.** Observed in Word 365 (16.0.20527) by rejecting the only insertion in, or accepting the only deletion in, a wrapper:
  - `w:smartTag` and `w:customXml` (with their `w:smartTagPr`/`w:customXmlPr`) are removed, and so are such wrappers that emptying them leaves empty in turn. `w:bdo` and `w:dir` follow the same rule here (not probed).
  - `w:hyperlink` stays as an empty element.
  - `w:sdt` (inline or block) stays. Word also marks it `w:showingPlcHdr` and fills `w:sdtContent` with a placeholder run of spaces; OneDocument leaves the content empty and does not invent a placeholder, so the text of that content control differs from Word's by those spaces.
  - `w:fldSimple`: Word rewrites it as a complex field and recalculates its result. OneDocument keeps the `w:fldSimple` with no result run; cached results stay authoritative and nothing recalculates on open.
  - A block-level content control whose only paragraph had an inserted mark: Word joins the remaining content into the paragraph after the control; OneDocument leaves an empty paragraph in the control.
