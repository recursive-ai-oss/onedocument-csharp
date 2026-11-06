# RTF (Rich Text Format 1.9.1)

RTF is read into and written from the word-processing hub (`Document`). The format has no part structure to keep byte-for-byte, so the lossless layer is a group tree of the file, and the hub is the WordprocessingML model: a document read from RTF is a WordprocessingML package in memory (`Document.Format` is `OfficeFormat.Rtf`), and everything the hub does works on it. Conversions return loss reports.

## Layers

| Layer | Namespace / package | What it is |
|---|---|---|
| Tokenizer | `RecursiveAI.OneDocument.Rtf` (core) `RtfReader` | Forward-only tokens over a byte span: groups, control words (with the one delimiting space), control symbols, `\'hh`, text runs, `\binN` data. Offsets on every token. Never throws. |
| Group tree | `RtfTree`, `RtfNode` (core) | The whole file as nested groups with source offsets; text and binary nodes point into the source bytes. `RtfTree.SourceOf(node)` is the exact bytes of any group, so unknown destinations are kept as bytes. |
| Writer | `RtfWriter` (core) | Groups, words, escaped text (7-bit ASCII: `\'hh` for Windows-1252, `\uN?` otherwise), `\bin`, picture hex. |
| Mail layer | `RtfHtmlConverter`, `RtfTextWriter`, `RtfEncapsulator`, `RtfDeencapsulator`, `Compression.*` (core) | The mail-body subset: RTF to HTML and text, HTML and text encapsulation in RTF, compressed RTF. Kept as the mail library had it. |
| Hub conversion | `.Documents` (internal `RtfImporter`, `RtfExporter`) | RTF to WordprocessingML and back, reached through `Document.Open`, `SaveAs("x.rtf")`, `Office.ExtractText` and `Office.Convert`. |

`Document.Open` finds RTF by its `{\rtf` signature, never by extension. `Document.ImportLoss` is the loss report of the read, `Document.ExportLoss` that of the last write to RTF.

## Round trip

A document opened from RTF and saved as RTF with no edit writes **the bytes it was read from**. After any edit (the hub notes an edit when a part is changed), a save writes the exporter's RTF: the hub's content in the word processor's own RTF dialect. Header-level destinations this library does not use (`\*\generator`, `\*\rsidtbl`, `\*\userprops` and the like) are kept as bytes and written back after the `\info` group.

## Reading

Every control word of RTF 1.9.1 that has a WordprocessingML counterpart is read; the rest is counted in the report or ignored as a layout hint Word recomputes.

| RTF | Becomes |
|---|---|
| `\fonttbl` (families, `\fcharset`, `\cpg`, `\falt`) | `fontTable.xml`; the font's charset chooses the code page for `\'hh` bytes, so double-byte text decodes |
| `\colortbl` | colours as `w:color`, `w:shd` fills, `w:highlight` (the sixteen named colours) |
| `\stylesheet` (`\s`, `\*\cs`, `\*\ts` read, `\sbasedon`, `\snext`) | `styles.xml`; paragraph and character styles with their properties; table styles are not carried; `\s0` is the default style |
| character formatting | `w:rPr` in schema order: bold, italic, underline styles, strike, caps, small caps, hidden, outline, shadow, emboss, imprint, super/subscript, `\up`/`\dn`, `\expnd`, `\charscalex`, `\kerning`, `\fs`, `\cf`, `\highlight`, `\cb`, `\lang`, `\cs`, `\rtlch` |
| `\u`, `\uc`, `\upr`/`\ud` | Unicode characters with the ANSI fallback skipped; the `\ud` copy is read |
| paragraph formatting | `w:pPr`: alignment, indents, spacing (`\sl` with `\slmult`), keep/page-break/widow control, borders, shading, tab stops with leaders, outline level, bidi |
| `\listtable`, `\listoverridetable`, `\ls`, `\ilvl` | `numbering.xml`: one abstract list per RTF list, one `w:num` per override, level format, text (`\leveltext` with its level placeholders), start value, justification, follow, indents |
| tables (`\trowd`, `\cellx`, `\cell`, `\row`) | `w:tbl` on a grid made of every cell edge any row uses; `gridSpan`, `gridBefore`/`gridAfter`, vertical and horizontal merges, borders, shading, cell and row properties, header rows, row height |
| nested tables (`\itap`, `\nestcell`, `\nestrow`, `\*\nesttableprops`) | nested `w:tbl` in the cell |
| sections (`\sectd`, `\sect`, `\sbk*`, `\pgwsxn`…, `\cols`, `\titlepg`, `\pgn*`) | `w:sectPr` on the last paragraph of each section and in the body |
| headers and footers (`\header`, `\headerl`, `\headerr`, `\headerf`, the footers) | `header`/`footer` parts referenced by type (default, even, first) from the section |
| `\footnote` (and `\ftnalt` for endnotes) with `\chftn` | `footnotes.xml`/`endnotes.xml` with separators, reference runs and `footnoteRef` |
| `\field`, `\fldinst`, `\fldrslt`, `\flddirty`, `\fldlock`, `\chpgn`, `\chdate`, `\chtime` | complex fields (begin, instruction, separate, result, end); fields may span paragraphs |
| `\*\bkmkstart`, `\*\bkmkend` | bookmarks |
| `\*\atnid`, `\*\atnauthor`, `\*\annotation`, `\*\atrfstart`/`atrfend`, `\atndate` | comments with their ranges, author, initials and date |
| `\revtbl`, `\revised`, `\deleted`, `\revauth`, `\revdttm` and the `del` forms | `w:ins`, `w:del` with `w:delText`, author and date |
| `\pict` with `\pngblip`, `\jpegblip`, `\emfblip`, `\wmetafile`, `\dibitmap`; `\shppict`/`\nonshppict` | media parts and inline drawings. A metafile without the placeable header gets one from `\picw`/`\pich`; a DIB gets a bitmap file header. Sizes: `\picwgoal`, scale, crops (`a:srcRect`). `\shppict` is read and `\nonshppict` skipped when both are present |
| `\shp` | a picture shape (`pib`) as an inline picture; a text shape (`\shptxt`) as ordinary paragraphs at the place of the shape |
| `\object` | the picture of `\result` (OLE data is never read) |
| `\mmath` | Office Math: the groups are OMML element for element (`{\mf …}` is `m:f`, `{\mchr x}` is `<m:chr m:val="x"/>`, `{\mr text}` is `m:r`) |
| `\info` | core and extended document properties (title, subject, author, keywords, comment, operator, category, company, manager, creation and revision time) |
| `\paperw`…, `\deftab`, `\facingp`, `\margmirror`, `\revisions`, `\deff`, `\deflang` | page setup of the first section, settings, document defaults |
| `\pntext` (legacy bullets and numbers) | the label stays as text; the `\pn` definition is dropped |

Unknown destinations (`{\*\name …}`) and unknown control words are skipped. At the top of the file an unknown destination is also kept as bytes for writing back.

### Loss report (reading), codes

| Code | When |
|---|---|
| `loss.rtf-unknown-destination` | an extension destination the reader does not know (`\*\…`), other than the ones that only hold editing state (`\generator`, `\rsidtbl`, `\datastore`…) |
| `loss.rtf-shape` | a drawing shape: its picture or text is kept, its position, size, wrapping and geometry are not |
| `loss.rtf-text-box` | the text of a shape read as ordinary paragraphs |
| `loss.rtf-drawing-object` | Word 97 `\do` drawing objects |
| `loss.rtf-ole-object` | an embedded object: only its preview picture is read |
| `loss.rtf-picture` | a picture that could not be carried (damaged, unsupported format, too large) |
| `loss.rtf-frames` | positioned paragraphs and drop caps (`\absw`, `\posx`, `\dropcapli`…) read as ordinary paragraphs |

## Writing

The exporter writes `\rtf1\ansi\ansicpg1252\uc1\deff0`, the font table (family and charset), colour table, style sheet (document defaults go into the default style, because RTF has no separate defaults), `\listtable` and `\listoverridetable`, `\revtbl` when there are tracked changes, `\info`, kept header destinations, page setup, then the sections. Each paragraph starts `\pard\plain` with its style and direct properties, each formatted run is a group, characters beyond Windows-1252 are `\uN` with a `?` fallback. Tables are written with their row definitions (top-level rows before their cells, nested rows in `\*\nesttableprops` after them), cell spans as `\clmgf`/`\clmrg` cell groups, vertical merges, borders and shading. Hyperlinks become `HYPERLINK` fields. Comments carry their ranges. Footnotes and endnotes are written inline at their reference. A header or footer a section shares with the one before is written once, because a section without its own shows the previous one's. Pictures (PNG, JPEG, EMF, WMF, BMP) are written as hex; a text box is written as a `\shp` with a `\shptxt` part.

### Loss report (writing), codes

| Code | When |
|---|---|
| `loss.rtf-shape` | drawing shapes, charts and diagrams other than pictures and text boxes |
| `loss.rtf-shape-position` | a text box's anchor, offsets and wrapping |
| `loss.rtf-floating-picture` | a floating picture is written inline |
| `loss.rtf-picture-format` | a picture in a format RTF cannot hold (GIF, TIFF, SVG, WebP…) |
| `loss.rtf-picture` | a linked or missing picture |
| `loss.rtf-ole-object` | an embedded object is written as its preview only |
| `loss.rtf-equation` | an equation is written as its linear text |
| `loss.rtf-content-control` | a content control is written as its content |
| `loss.rtf-table-style` | table styles |
| `loss.rtf-run-effect` | text effects, emphasis marks, fit-text and character borders |
| `loss.rtf-frames` | paragraph frames |

## Defects

Codes are in `defects.md` (`rtf.*`, kind `Record`, located by byte offset; limits are kind `Limit`). Reading never throws anything but a typed `OfficeException`: damage the reader repairs is a defect, input that is not RTF raises `rtf.not-rtf`, and an unexpected failure while converting is wrapped as `rtf.damaged`. Limits: the file must fit `OfficeLimits.MaxPartSize`; group nesting is bounded by `MaxRecursionDepth` (at most 1000); the tree is bounded by `MaxRecordCount` nodes; pictures count against `MaxPartSize` each and `MaxAllocationBytes` together.

## Where the specification leaves a choice

- **Picture scale and crop.** `\picscalex`/`y` apply to the picture after cropping, and the crops are twips of the `\picwgoal`/`\pichgoal` size. This is the way LibreOffice writes them (checked on its output for a centred crop); Word is the reference and decides at the milestone check.
- **`\sectd` and headers.** `\sectd` resets the section properties but not the headers and footers collected for the section; a new section starts with none, which is how WordprocessingML inherits them.
- **`\uc` skipping.** After `\uN`, the next `\ucN` characters are skipped when they are `\'hh` escapes or text bytes; control words are never skipped.
- **Footnote marks.** In RTF the body's `\chftn` and the note's own `\chftn` are both marks. The body's is replaced by the reference run the note group produces; the note's becomes `footnoteRef`.
- **Charset 0 fonts** use the document's `\ansicpg`.

## Checks

Unit tests (`RtfTests`, the lifted mail-layer tests), `fuzz` targets `rtftree` and `rtf`, and `tools/RtfProbe` (bulk read and write, text comparison with another extractor, and `oracle.sh` which runs LibreOffice as a black box over the same documents). Files that settle how the desktop word processor opens RTF are generated by `RtfProbe write` and listed in `testdata/rtf/word-open/`; they are answered at the milestone check.
