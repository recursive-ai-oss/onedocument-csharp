# RTF test files

| Path | What | Source |
|---|---|---|
| `oxrtfex-3.1.rtf`, `oxrtfex-3.1.html` | the worked HTML-in-RTF encapsulation example of the Exchange RTF extensions specification | typed from the specification's example; used by the encapsulation tests |
| `word-open/*.rtf` | RTF written by this library (`RtfProbe write`) from generated DOCX documents (the layout and table sample sets) | generated; the exporter at the commit that added them |
| `word-open/hand/*.rtf` | small hand-written RTF for behaviour the specification leaves open (picture scale and crop, nested tables, notes and comments, section headers, math and tracked changes) | written for this project |
| `lo/*.rtf` | RTF written by LibreOffice 26.8.2.2 (build 9dea406ab7695df6325b11b0e19fdca8db4b7276) from the same generated DOCX documents, run as a black box (`soffice --headless --convert-to rtf`) | oracle output |

## Questions for the desktop word processor (not yet answered)

The files in `word-open/` and `word-open/hand/` are inputs for the milestone check on the Office oracle. For each file: does it open without a repair or conversion prompt, how many pages, and (with the probe) what text and list labels each paragraph has. Specific questions:

- `hand/picture-scale-crop.rtf`: the size of the second picture. The reader takes the scale to apply to what is left after cropping: (960 - 480) x (720 - 360) twips at 200 % is 960 x 720 twips, 0.67 x 0.5 in.
- `hand/section-headers.rtf`: whether section two shows the header of section one and section three its own.
- `hand/nested-table.rtf`: that the nested table is read as nested and the outer row has two cells.
- `hand/footnote-comment-field.rtf`: the footnote mark, the comment range and the field results.
- `word-open/17-numbered-list.rtf`, `18-bullets.rtf`: the list labels and their indents.
- `word-open/tbl-05-spans-and-merges.rtf`, `tbl-09-nested.rtf`, `tbl-10-header-repeat.rtf`: spans, vertical merges, nesting and repeated header rows.
- `word-open/110-footnote-single.rtf`, `118-endnotes-document-end.rtf`: note placement and numbering.
- `word-open/28-sections.rtf`, `77-header-footer-numbers.rtf`, `78-title-page-even-odd.rtf`: section breaks, headers and footers by type.
