# Word conversion comparison cases (unanswered)

Inputs: every `*.doc` in this folder (hashes in `README.md`). Each is to be opened in desktop Word (the version pinned for the release gate) and saved as DOCX, and the result compared with `Document.Open(file)` saved as DOCX by this library. Nothing here has been run.

For each file record, from Word's own conversion and from ours:

1. Word opens the `.doc` without a repair or compatibility prompt (yes/no).
2. Paragraph count, the text of each paragraph, and the number of tables, rows and cells.
3. Styles applied to each paragraph (names) and the effective font size, bold, italic, colour of each run.
4. Footnotes, endnotes and comments: count, text, the position of each reference.
5. Section count, page size and orientation, columns, and which headers and footers each section shows.
6. Tracked changes: kind, author, date, text.
7. Pictures: count and size in EMU.

Questions the specification leaves open, to be decided by what Word does:

- `table`: how the grid is formed when rows have different cell edges (this library uses the union of all rows' edges).
- `lists`: whether Word links a numbering level to a style when the list definition names one, and what it writes for a bullet character in the Symbol font.
- `notes`: whether the comment range is written when the document stores only the reference mark (this library anchors the comment at the reference).
- `sections`: whether Word keeps the even-page and first-page header slots a writer stored although the document uses neither.
- `basic`: how Word writes the paragraph that follows a page break stored as a paragraph property.
