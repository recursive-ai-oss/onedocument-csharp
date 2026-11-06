# Word binary files (DOC)

`RecursiveAI.OneDocument.Doc.Binary` (package `.Documents`) reads Word 97, 2000, 2002, 2003, 2007, 2010 and 2013 binary documents, Word 6.0 and Word 95 documents, and files that were saved incrementally ("fast saved"). Reading only: DOC writing is not planned for 1.0. A DOC opened with `Document.Open`, `Office.Open` or `Office.Convert` is converted into the WordprocessingML model (an in-memory package that saves as DOCX); `Office.ExtractText` reads the text without building the model.

Specification: [MS-DOC] (Word Binary File Format), with [MS-ODRAW] for pictures and [MS-OSHARED] for number formats. Word 6.0 and Word 95 files are outside [MS-DOC]; see "Word 6/95" below for what was settled from real files.

## Layers

| Type | Layer |
|---|---|
| `DocFile` | The compound file's `WordDocument`, `0Table`/`1Table` and `Data` streams read into memory; the FIB; the piece table; the whole text and the character range of each story. |
| `DocFib` | The File Information Block: version, flags, story lengths and the (offset, size) pairs ([MS-DOC] 2.5). Pairs are read up to the count the file declares. |
| `PieceTable`, `Piece` | `Clx`, `PlcPcd`, `Pcd`, `Prc` ([MS-DOC] 2.9.38, 2.8.35): where each run of characters lives, 8-bit or UTF-16, in any file order. |
| `PropertyRuns` | All character or paragraph formatted disk pages (`ChpxFkp`, `PapxFkp`) behind the bin tables, flattened and sorted. |
| `Prl`, `SprmReader`, `SprmNames` | Property modifiers with operands kept as bytes; unknown modifiers are carried, named when [MS-DOC] 2.6 lists them. `SprmTable.g.cs` is generated from the specification by `tools/DocSprmTable/generate.py`. |
| `DocReader` | Property lookup per character position (run plus the piece's `Prm`), the paragraph walk (paragraph, cell and row marks, table depth), `Plc` arrays, section ends. |
| `DocStyleSheet`, `DocFontTable`, `DocLists` | `STSH`, `SttbfFfn`, `PlfLst`/`PlfLfo`. |
| `DocTextExtractor` | Text without the model. |

The conversion (`DocConverter`, internal) writes WordprocessingML parts from these. All of it is bounded by the `OpenContext` limits: stream sizes by `MaxPartSize`, the text by `MaxAllocationBytes`, every length and offset checked before use.

## What is read

- **Text**: all stories (main, footnotes, headers and footers, comments, endnotes, text boxes, header text boxes), 8-bit text with the `FcCompressed` mapping of 0x82 to 0x9F, UTF-16 text, complex (fast-saved) piece tables.
- **Formatting**: character, paragraph, table and section modifiers through the modifier tables of [MS-DOC] 2.6 (the modifiers that are mapped are listed in the source of `RunFormat`, `ParaFormat`, `TableRowFormat`, `SectionFormat`); toggles with "same as style" and "opposite of style" are resolved against the style chain; `sprmPHugePapx` is followed into the Data stream; a piece's `Prm` (simple and complex) is applied.
- **Styles** with inheritance, next style and aliases; **fonts**; **lists** (list definitions, overrides, level overrides, bullets and number formats); **fields** as complex fields (instruction kept, result kept); **bookmarks**; **footnotes and endnotes** with separators; **comments** with author and initials (anchored at the reference mark); **headers and footers** per section and kind (even, odd, first); **sections** (page size, margins, columns, orientation, borders, line numbers, page numbering, vertical alignment, document grid); **tables** (cell edges, merges, borders, shading, row height, header rows, nesting; one grid for the union of all rows' cell edges); **tracked changes** (insertions and deletions with author and date, on runs and paragraph marks); **pictures** (inline pictures from the Data stream, floating pictures from the drawing layer with position, wrapping and z-order; EMF, WMF, JPEG, PNG, DIB, TIFF) ; **document settings** (default tab stop, odd/even headers, revision tracking).

## What is lost

Every loss is a `loss.doc-*` entry in `Document.ImportLoss`, and where a location exists a defect.

| Code | Meaning |
|---|---|
| `loss.doc-sprms` | Property modifiers with no counterpart (count); each kind is one `doc.sprm-not-mapped` defect with the stream offset of the first occurrence. |
| `loss.doc-text-boxes` | Text boxes: their text is appended after the body as paragraphs; position and frame are not kept. |
| `loss.doc-header-text-boxes` | Text boxes in headers and footers are dropped. |
| `loss.doc-drawing-shapes` | Shapes that are not pictures (lines, rectangles, WordArt, groups) are dropped. |
| `loss.doc-ole-objects` | Objects whose storage could not be carried (missing from the `ObjectPool`, a link with no readable moniker, or inside tracked changes) keep only their preview picture. Objects that could be carried are not counted: see `ole.md`. |
| `loss.doc-form-fields` | Form fields stay plain fields; check box state and drop-down lists are dropped. |
| `loss.doc-table-merge-split` | `sprmTMerge`/`sprmTSplit` row edits. |
| `loss.doc-vba` | A `Macros` storage was present. |
| `loss.doc-properties` | Properties of the summary information streams with no counterpart in the package (count), each named in the message: the thumbnail, the VBA signature, hyperlink lists, custom property types without an equivalent, unknown identifiers. The rest become core, extended and custom properties: see `ole.md`. |
| `loss.doc-word6-formatting` | Word 6/95 formatting. |

## Text extraction

`Office.ExtractText` reads the piece table, the paragraph and character runs (for table structure and tracked deletions) and the story tables, and nothing else. Output: one line per paragraph; a table row on one line with the cells separated by a tab and the paragraphs of a cell by a space (nested tables flatten into their cell); a field gives its result and never its instruction; tracked deletions are left out (a deleted paragraph mark joins the paragraph to the next); then headers and footers (empty ones skipped), footnotes, endnotes and comments, each part set off by a blank line; then the text boxes. Footnote, comment and picture characters give no text. Differences from the OOXML extractor, none about words: the order of headers and text boxes relative to the body.

## Word 6/95

[MS-DOC] covers Word 97 onwards. A Word 6.0 or Word 95 file (`wIdent` 0xA5DC, `nFib` 101 or 104) is read with these rules, **settled by reading the headers of the 34 Word 6.0 and Word 95 files in the govdocs1 set** (4 with `nFib` 101, 30 with 104) (the black-box documents; nothing else was consulted):

- the header is one fixed block: `fcMin` and `fcMac` at 0x18 and 0x1C bracket the 8-bit text (the text exceeds the sum of the story lengths by one character in 28 of the 34 files and equals it in 6);
- the story lengths are 32-bit values from 0x34: main text, footnotes, headers, macros, comments, endnotes, text boxes, header text boxes;
- the offset pairs start at 0x58, in the order of `FibRgFcLcb97`;
- there is no table stream: structures that live there in Word 97 are in the `WordDocument` stream;
- a document that was saved in full has no piece table (the text is one 8-bit run); a fast-saved one has a `Clx` whose `Pcd.fc` values are plain byte offsets (no compression flag);
- the 8-bit text is in the character set at 0x14 (0 is ANSI); other sets are read as Windows-1252 with a `doc.codepage-unsupported` defect;
- paragraphs end at 0x0D, table cells at 0x07, the row end is a cell mark that follows another with nothing between.

Word 6/95 character, paragraph and section formatting uses a different, one-byte modifier numbering that [MS-DOC] does not describe and that was not settled; it is reported as `loss.doc-word6-formatting`. Styles, lists and pictures of those files are likewise not read.

Word 1.0 and 2.0 files (a flat file starting `FE 37 00 23`, no compound file) and files that are not compound files at all are not recognised: `format.unrecognised`.

## Encryption

A file with `fEncrypted` set is decrypted with `OpenContext.Password` (XOR, RC4, RC4 CryptoAPI), or raises `OfficeEncryptedException`; see `encryption.md`. Password-protected Word 6/95 files raise `OfficeUnsupportedException`.

## Questions Office decides

Committed as unanswered cases for the 1.0 gate (`testdata/doc/word-conversion-cases.md`): how Word's own conversion of a DOC to DOCX differs from this one on tables with differing row edges, on lists linked to styles, on footnote/comment numbering and on text boxes.
