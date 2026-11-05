# Fields at layout time

Fields that depend on pages, and the table of contents, index and table of authorities, are resolved while a document is laid out
(`LayoutEngine.Layout`, so also `DocumentRenderer`, `Office.Convert` and `document.SaveAs("x.pdf")`). The document is only read: results
go to the pages, never back into the file. References are to ECMA-376 Part 1; items marked **to check against Word** are rules the
specification leaves open and are listed in the table at the end.

## Policy

| What | Default | With `LayoutOptions.UpdateFields` (or `w:updateFields` in the file) |
|---|---|---|
| `PAGE`, `NUMPAGES`, `SECTIONPAGES`, `SECTION`, `PAGEREF`, `NOTEREF`, `REF`, `STYLEREF`, `SEQ` | computed from the layout | the same |
| `TOC`, `INDEX`, `TOA` with no stored result (or only an empty one) | built | rebuilt |
| `TOC` with a stored result | entries kept as stored; the `PAGEREF` fields in them are recomputed like any `PAGEREF` | rebuilt from the document |
| `DATE`, `TIME` | stored result | computed from `LayoutOptions.Clock`; stored result when `Clock` is null |
| `CREATEDATE`, `SAVEDATE`, `PRINTDATE`, `AUTHOR`, `TITLE`, `SUBJECT`, `KEYWORDS`, `COMMENTS`, `LASTSAVEDBY`, `REVNUM`, `DOCPROPERTY`, `FILENAME`, `NUMWORDS`, `NUMCHARS`, `IF`, `=`, `SYMBOL`, `QUOTE` | stored result | computed |
| Everything else (`HYPERLINK`, `INCLUDEPICTURE`, `INCLUDETEXT`, `MERGEFIELD`, …) | stored result | stored result |

- A result that equals the stored one is left alone, so the field keeps the formatting of its stored runs. A computed result goes
  into the first result run (its character formatting) and the other result runs of that field show nothing.
- A field with `w:fldLock` is never rebuilt; a table of contents, index or authorities whose field is not closed, or whose code and result
  are not in paragraphs of one container, keeps its stored result and adds `layout.field-skipped`.
- `w:updateFields` in the settings part (§17.15.1.90, Word updates every field when it opens the file) turns `UpdateFields` on, because
  that is what Word does with the file.
- Nothing is fetched: `INCLUDEPICTURE`, `INCLUDETEXT` and `HYPERLINK` targets are never followed.
- The system clock is never read. File dates come from the document properties, shown in the clock's time zone (UTC without a clock).

## Passes

A field that depends on pages cannot know its value until pages exist, and its value can change the pages (a longer number, a longer
contents list). Layout therefore runs again with the **facts** of the pass before — the printed number and number format of each page,
the page each paragraph starts on (and, for a paragraph that splits, where each piece starts), the page counts of the document and its
sections — until a pass produces the facts it was given:

1. Pass 1 has no facts: fields that need them keep their stored result (`NUMPAGES` shows 1).
2. Pass *k* reads the facts of pass *k* − 1 and produces its own.
3. When both are equal the pass stands. When `LayoutOptions.MaxFieldPasses` (default 6) is reached first, the last pass stands and
   `LayoutDocument.Notes` gets `layout.fields-unsettled`. `LayoutDocument.FieldPasses` tells how many passes ran.
4. A document in which no field read the facts is laid out once.

A table of contents is built before pass 1 (its length does not depend on pages); only its page numbers wait for the facts. That is
how a contents list that makes the headings move down a page ends with the numbers of the pages the headings are on.

## The working copy

A table of contents, index or table of authorities that has to be built is built in a **copy** of the document (the document is saved
to memory and opened again; `LayoutDocument.WorkingCopy` returns it, and `LayoutParagraph.Source` of that layout belongs to the copy).
Entries are real paragraphs, as Word writes them:

| Field | Entry paragraphs |
|---|---|
| `TOC` | style `TOC 1`–`TOC 9` (a built-in-like direct format when the document has none: indent 220 twips per level, 5 pt after); a right tab with dot leader at the text width; the heading's list number, a tab, its text; a tab and a `PAGEREF` field; with `\h` all of it in a `w:hyperlink` to the heading's `_Toc` bookmark. The heading gets a `_Toc` bookmark in the copy unless it has one. |
| `INDEX` | styles `Index 1`–`Index 9`; the entry, the `\e` separator (default `, `), then a page-list field naming bookmarks that the builder puts at each `XE` field; `\t` cross references follow `\k` (default `. `). |
| `TOA` | style `Table of Authorities`; category headings with `\h`; the long citation, a right tab with dot leader (or `\e`), the page list. |

The page-list field exists only in the copy (`ODXEPAGES`, with the bookmark names as arguments and `\l` and `\g` separators), so the lists
follow the pages like `PAGEREF` does.

## Fields

| Field | What is computed |
|---|---|
| `PAGE` | the printed number of the page (§17.16.5.44) in the section's `pgNumType` format; in the body, the page of the paragraph the field is in |
| `NUMPAGES`, `SECTIONPAGES`, `SECTION` | page count, pages of the section, section number (§17.16.5.42, .54, .55) |
| `PAGEREF` | the printed page of the bookmark in the format of that page; `\p` gives `above`/`below` on the same page and `on page N` on another (§17.16.5.45); `\h` has no effect on the text |
| `NOTEREF` | the mark the note prints (the numbering the notes are laid out with: format, start, restart per section or page, custom marks), for the note referenced inside the bookmark; `\p` as `REF` |
| `REF` | the text of the bookmark (a SEQ result inside it is the computed number); `\n` the paragraph number without trailing periods, `\r` the levels the field's paragraph does not share, `\w` all levels, the levels joined as written (`1.` `a.` `i.` read `1.a.i`); `\p` gives `above` (the bookmark is before the field) or `below`, and after a number switch it is appended (`i above`) (§17.16.5.51) |
| `STYLEREF` | in a header or footer: the first (`\l`: last) text of the style on the page, else the nearest before the page, else after; in the body: the nearest before the field, else after (`\l`: after, else before); paragraph styles give the paragraph text, character styles the first or last stretch of runs; `\n`, `\r`, `\w`, `\p` as `REF` (§17.16.5.59) |
| `SEQ` | a counter per identifier in document order; `\c` repeats, `\r n` resets, `\h` hides unless a `\*` switch is present (the number still counts), `\s n` restarts at each heading of level *n*; a SEQ inside the stored result of a TOC, index, `REF`, `STYLEREF` or `PAGEREF` is a copy and does not count (§17.16.5.56) |
| `TOC` | `\o` (heading styles by name, `heading N`), `\u` (outline levels), `\t` (style name, level pairs; `,` or `;`), `\b`, `\c` (paragraphs holding a `SEQ` of that identifier), `\f`/`\l` (`TC` fields), `\n` (no page numbers, all levels or a range), `\p` (separator text instead of the tab), `\h`, `\w` (keep tabs), `\x` (keep line breaks); `\a`, `\s`, `\d`, `\z` are not applied (§17.16.5.68) |
| `INDEX` with `XE` | main entries and subentries to any depth (`:`), sorted without regard to case; `\b` bold and `\i` italic page numbers, `\t` cross references, `\r` page ranges (`XE \r`), `\f` entry types, `\b` range, `\e`, `\k`, `\l`, `\g`, `\h` (heading per letter, `A` replaced by the letter, a space for a blank line), `\r` (run-in subentries: `main: sub, 3; sub, 4`), `\c` 1–4 columns (the index sits in a continuous section of its own); `\p`, `\s`, `\y`, `\z` are not applied (§17.16.5.29, .72) |
| `TOA` with `TA` | categories 1–7 named as Word names them, other numbers `Category N`; `\c 0` all, `\h` headings, `\e`, `\l`, `\g`, `\p` (`passim` from five pages); `\s` is not applied (§17.16.5.67, .62) |
| `DATE`, `TIME`, `CREATEDATE`, `SAVEDATE`, `PRINTDATE` | with `\@` pictures (the mail-merge `WordPicture`); defaults `M/d/yyyy`, `h:mm AM/PM`, `M/d/yyyy h:mm AM/PM`; a never-printed document gives `0/0/0000 0:00:00 AM` (§17.16.5.47) |
| `AUTHOR`, `TITLE`, `SUBJECT`, `KEYWORDS`, `COMMENTS`, `LASTSAVEDBY`, `REVNUM`, `DOCPROPERTY` | the core, extended or custom property; an argument replaces the value (nothing is written back) |
| `FILENAME` | the file name (`\p` the path) of a document opened from a path; the stored result otherwise |
| `NUMWORDS`, `NUMCHARS` | the value recorded in the extended properties (§17.16.5.43), else a count of the text of the body |
| `IF` | the comparison of the mail merge (numbers, else text; `?` and `*` wildcards), nested fields in the instruction contribute their results |
| `=` | the calc engine on the expression: `ABOVE`/`BELOW`/`LEFT`/`RIGHT` as the contiguous numeric cells of the table in that direction (a blank first cell counts as 0), cell names and ranges (`A1`, `B1:B3`, `B:B`), bookmark names with a number, `MOD` and `INT` as §17.16.3.4 defines them; without a `\#` picture at most two decimals are kept (`1/3` is `0.33`) (§17.16.3) |
| `SYMBOL`, `QUOTE` | the character (decimal or `0x` code; symbol fonts map as list bullets do), the quoted text |

All of them take `\*` (`Upper`, `Lower`, `FirstCap`, `Caps`, `Roman`, `roman`, `ALPHABETIC`, `alphabetic`, `Arabic`, `ArabicDash`, `CardText`,
`OrdText`, `Ordinal`, `Hex`), `\#` and `\@` as in `mail-merge.md` (§17.16.4). `MERGEFORMAT` and `CHARFORMAT` are accepted and ignored.
A reference to a bookmark that does not exist keeps the stored result and adds `layout.field-source-missing`; with `UpdateFields` it shows
`Error! Reference source not found.`.

## Notes

`layout.fields-unsettled`, `layout.field-source-missing`, `layout.field-skipped`.

## Open points (to check against Word)

| Point | What is done |
|---|---|
| Page of a bookmark inside a paragraph that splits across pages | the page of the line at the same fraction of the paragraph's text; the start of the paragraph when it does not split |
| `TOC` heading numbers and tabs | number, tab, text; a left tab at 440 twips per level for numbered entries (Word's built-in styles) |
| Closing paragraph of a `TOC` field written as one paragraph | the end character moves to the last entry paragraph, so there is no extra empty paragraph (Word writes a separate one for its own tables) |
| `INDEX \c` | an empty trailing section is left after the index when nothing follows it |
| Cross-page `NOTEREF \p`, `REF \p` | `above`/`below` only (the specification names `on page N` for `PAGEREF` alone) |
| `=` fields in a table that read other formula cells | read the stored result of that cell, not its new value |
| Cell numbers such as `$20.50`, `1,234`, `15%` | read as numbers; Word's rule is not documented |
| Sort order of index entries | invariant culture, case-insensitive |
| `STYLEREF` in footnotes and comments | not supported (body and headers/footers only) |

## Tests and differential inputs

`tests/OneDocument.Rendering.Tests/FieldTests.cs` lays out generated documents (`FieldDocs`): a three-level contents list whose entries
move after relayout, numbered headings, `TOC` switches and `TC` fields, `PAGEREF`/`REF`/`NOTEREF`/`STYLEREF`/`SEQ` with their switches,
the picture switches, index and authorities (multi-level, ranges, run-in, columns), the stored-versus-updated policy, `IF`/`=`/dates,
a bounded non-converging relayout and a 800-heading contents list.

The same documents are cases 100–112 of `LayoutCases.All()`, so `tools/layout-differential.sh` sends them to Word with the rest of the
layout set. They are unanswered until the differential runs; the cases that need a built table of contents carry `w:updateFields`, so
Word has to update fields before its export (or the oracle has to call its field update).

## For a later document operation

`FieldUpdater` (builds the entries in a copy), `FieldModel` (bookmarks, fields, SEQ and note numbers) and `FieldEvaluator` (one value per
field type) are internal to `RecursiveAI.OneDocument.Rendering`; a hub-level `UpdateFields()` writes the computed results with
`Field.SetResult` after one layout, using the facts of its last pass. `LayoutDocument.WorkingCopy` already holds a document with the
tables built, which that operation can save.
