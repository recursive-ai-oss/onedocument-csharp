# OpenDocument fixtures

- `rich.docx` is written by the `OdfTests` sample (`OdfSamples.Rich`) through the document hub: headings, character formatting, a hyperlink,
  a bookmark, a footnote, a comment, nested numbered and bullet lists, a table with a column span, a row span and a header row, a picture,
  a tracked insertion and deletion, a page break and two sections (the second landscape in two columns) with a header and a footer.
- `lo-rich.odt` and `lo-rich.fodt` are the output of LibreOffice (version in `oracle-version.txt`, run as a black box, filters `writer8` and
  `OpenDocument Text Flat XML`) on `rich.docx`. They contain no text that the sample did not write.

The tests read `lo-rich.*` into the hub and compare its text with the text of `rich.docx`.
